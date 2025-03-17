import type { HttpFileUtil } from "@spt/utils/HttpFileUtil";
import type { SyncUtil } from "./sync";
import { glob } from "./utility/glob";
import type { IncomingMessage, ServerResponse } from "node:http";
import path from "node:path";
import type { FileSystem } from "@spt/utils/FileSystem";
import type { Config } from "./config";
import { HttpError, winPath } from "./utility/misc";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import type { PreSptModLoader } from "@spt/loaders/PreSptModLoader";
import type { HttpServerHelper } from "@spt/helpers/HttpServerHelper";
import type { IStatter } from "./utility/statter";

const FALLBACK_SYNCPATHS: Record<string, object> = {};

// @ts-expect-error - undefined indicates a version before 0.8.0
FALLBACK_SYNCPATHS[undefined] = [
	"BepInEx\\plugins\\Corter-ModSync.dll",
	"ModSync.Updater.exe",
];
FALLBACK_SYNCPATHS["0.8.0"] =
	FALLBACK_SYNCPATHS["0.8.1"] =
	FALLBACK_SYNCPATHS["0.8.2"] =
		[
			{
				enabled: true,
				enforced: true,
				path: "BepInEx\\plugins\\Corter-ModSync.dll",
				restartRequired: true,
				silent: false,
			},
			{
				enabled: true,
				enforced: true,
				path: "ModSync.Updater.exe",
				restartRequired: false,
				silent: false,
			},
		];

const FALLBACK_HASHES: Record<string, object> = {};

// @ts-expect-error - undefined indicates a version before 0.8.0
FALLBACK_HASHES[undefined] = {
	"BepInEx\\plugins\\Corter-ModSync.dll": { crc: 999999999 },
	"ModSync.Updater.exe": { crc: 999999999 },
};
FALLBACK_HASHES["0.8.0"] =
	FALLBACK_HASHES["0.8.1"] =
	FALLBACK_HASHES["0.8.2"] =
		{
			"BepInEx\\plugins\\Corter-ModSync.dll": {
				"BepInEx\\plugins\\Corter-ModSync.dll": {
					crc: 999999999,
					nosync: false,
				},
			},
			"ModSync.Updater.exe": {
				"ModSync.Updater.exe": { crc: 999999999, nosync: false },
			},
		};

export class Router {
	constructor(
		private config: Config,
		private syncUtil: SyncUtil,
		private vfs: FileSystem,
		private statter: IStatter,
		private httpFileUtil: HttpFileUtil,
		private httpServerHelper: HttpServerHelper,
		private modImporter: PreSptModLoader,
		private logger: ILogger,
	) {}

	/**
	 * @internal
	 */
	public async getServerVersion(
		_req: IncomingMessage,
		res: ServerResponse,
		_: RegExpMatchArray,
		_params: URLSearchParams,
	) {
		const modPath = this.modImporter.getModPath("Corter-ModSync");
		const packageJson = await this.vfs.readJson(
			path.join(modPath, "package.json"),
		);

		res.setHeader("Content-Type", "application/json");
		res.writeHead(200, "OK");
		res.end(JSON.stringify(packageJson.version));
	}

	/**
	 * @internal
	 */
	public async getSyncPaths(
		req: IncomingMessage,
		res: ServerResponse,
		_: RegExpMatchArray,
		_params: URLSearchParams,
	) {
		const version = req.headers["modsync-version"] as string;
		if (version in FALLBACK_SYNCPATHS) {
			res.setHeader("Content-Type", "application/json");
			res.writeHead(200, "OK");
			res.end(JSON.stringify(FALLBACK_SYNCPATHS[version]));
			return;
		}

		res.setHeader("Content-Type", "application/json");
		res.writeHead(200, "OK");
		res.end(
			JSON.stringify(
				this.config.syncPaths.map(({ path, ...rest }) => ({
					path: winPath(path),
					...rest,
				})),
			),
		);
	}

	/**
	 * @internal
	 */
	public async getExclusions(
		_req: IncomingMessage,
		res: ServerResponse,
		_: RegExpMatchArray,
		_params: URLSearchParams,
	) {
		res.setHeader("Content-Type", "application/json");
		res.writeHead(200, "OK");
		res.end(JSON.stringify(this.config.exclusions));
	}

	/**
	 * @internal
	 */
	public async getHashes(
		req: IncomingMessage,
		res: ServerResponse,
		_: RegExpMatchArray,
		params: URLSearchParams,
	) {
		const version = req.headers["modsync-version"] as string;
		if (version in FALLBACK_HASHES) {
			res.setHeader("Content-Type", "application/json");
			res.writeHead(200, "OK");
			res.end(JSON.stringify(FALLBACK_HASHES[version]));
			return;
		}

		let pathsToHash = this.config.syncPaths;
		if (params.has("path")) {
			pathsToHash = this.config.syncPaths.filter(
				({ path, enforced }) =>
					enforced || params.getAll("path").includes(path),
			);
		}

		const hashes = await this.syncUtil.hashModFiles(pathsToHash);

		res.setHeader("Content-Type", "application/json");
		res.writeHead(200, "OK");
		res.end(JSON.stringify(hashes));
	}

	/**
	 * @internal
	 */
	public async fetchModFile(
		_: IncomingMessage,
		res: ServerResponse,
		matches: RegExpMatchArray,
		_params: URLSearchParams,
	) {
		const filePath = decodeURIComponent(matches[1]);

		const sanitizedPath = this.syncUtil.sanitizeDownloadPath(
			filePath,
			this.config.syncPaths,
		);

		if (!(await this.vfs.exists(sanitizedPath)))
			throw new HttpError(
				404,
				`Attempt to access non-existent path ${filePath}`,
			);

		try {
			const fileStats = await this.statter.stat(sanitizedPath);
			res.setHeader("Accept-Ranges", "bytes");
			res.setHeader(
				"Content-Type",
				this.httpServerHelper.getMimeText(path.extname(filePath)) ||
					"text/plain",
			);
			res.setHeader("Content-Length", fileStats.size);
			return this.httpFileUtil.sendFileAsync(res, sanitizedPath);
		} catch (e) {
			throw new HttpError(
				500,
				`Corter-ModSync: Error reading '${filePath}'\n${e}`,
			);
		}
	}

	public handleRequest(req: IncomingMessage, res: ServerResponse) {
		const routeTable = [
			{
				route: glob("/modsync/version"),
				handler: this.getServerVersion.bind(this),
			},
			{
				route: glob("/modsync/paths"),
				handler: this.getSyncPaths.bind(this),
			},
			{
				route: glob("/modsync/exclusions"),
				handler: this.getExclusions.bind(this),
			},
			{
				route: glob("/modsync/hashes"),
				handler: this.getHashes.bind(this),
			},
			{
				route: glob("/modsync/fetch/**"),
				handler: this.fetchModFile.bind(this),
			},
		];

		const url = new URL(req.url!, `http://${req.headers.host}`);

		try {
			for (const { route, handler } of routeTable) {
				const matches = route.exec(url.pathname);
				if (matches) return handler(req, res, matches, url.searchParams);
			}

			throw new HttpError(404, "Corter-ModSync: Unknown route");
		} catch (e) {
			if (e instanceof Error)
				this.logger.error(
					`Corter-ModSync: Error when handling [${req.method} ${req.url}]:\n${e.message}\n${e.stack}`,
				);

			if (e instanceof HttpError) {
				res.writeHead(e.code, e.codeMessage);
				res.end(e.message);
			} else {
				res.writeHead(500, "Internal server error");
				res.end(
					`Corter-ModSync: Error handling [${req.method} ${req.url}]:\n${e}`,
				);
			}
		}
	}
}
