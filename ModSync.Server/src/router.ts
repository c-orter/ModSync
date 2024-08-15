﻿import type { HttpFileUtil } from "@spt/utils/HttpFileUtil";
import type { SyncUtil } from "./sync";
import { glob } from "./glob";
import type { IncomingMessage, ServerResponse } from "node:http";
import path from "node:path";
import type { VFS } from "@spt/utils/VFS";
import type { Config } from "./config";
import { HttpError, winPath } from "./utility";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import type { PreSptModLoader } from "@spt/loaders/PreSptModLoader";
import type { HttpServerHelper } from "@spt/helpers/HttpServerHelper";

const FALLBACK_SYNCPATHS: Record<string, object> = {
	undefined: ["BepInEx\\plugins\\Corter-ModSync.dll", "ModSync.Updater.exe"],
};

const FALLBACK_HASHES: Record<string, object> = {
	undefined: {
		"BepInEx\\plugins\\Corter-ModSync.dll": { crc: 999999999 },
		"ModSync.Updater.exe": { crc: 999999999 },
	},
};

export class Router {
	constructor(
		private config: Config,
		private syncUtil: SyncUtil,
		private vfs: VFS,
		private httpFileUtil: HttpFileUtil,
		private httpServerHelper: HttpServerHelper,
		private modImporter: PreSptModLoader,
		private logger: ILogger,
	) {}

	/**
	 * @internal
	 */
	public async getServerVersion(
		req: IncomingMessage,
		res: ServerResponse,
		_: RegExpMatchArray,
	) {
		const modPath = this.modImporter.getModPath("Corter-ModSync");
		const packageJson = JSON.parse(
			// @ts-expect-error readFile returns a string when given a valid encoding
			await this.vfs
				// @ts-expect-error readFile takes in an options object, including an encoding option
				.readFilePromisify(path.join(modPath, "package.json"), {
					encoding: "utf-8",
				}),
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
	public async getHashes(
		req: IncomingMessage,
		res: ServerResponse,
		_: RegExpMatchArray,
	) {
		const version = req.headers["modsync-version"] as string;
		if (version in FALLBACK_HASHES) {
			res.setHeader("Content-Type", "application/json");
			res.writeHead(200, "OK");
			res.end(JSON.stringify(FALLBACK_HASHES[version]));
			return;
		}

		console.time("hash");
		res.setHeader("Content-Type", "application/json");
		res.writeHead(200, "OK");
		res.end(
			JSON.stringify(await this.syncUtil.hashModFiles(this.config.syncPaths)),
		);
		console.timeEnd("hash");
	}

	/**
	 * @internal
	 */
	public async fetchModFile(
		_: IncomingMessage,
		res: ServerResponse,
		matches: RegExpMatchArray,
	) {
		const filePath = decodeURIComponent(matches[1]);

		const sanitizedPath = this.syncUtil.sanitizeDownloadPath(
			filePath,
			this.config.syncPaths,
		);

		if (!this.vfs.exists(sanitizedPath))
			throw new HttpError(
				404,
				`Attempt to access non-existent path ${filePath}`,
			);

		try {
			const fileStats = await this.vfs.statPromisify(sanitizedPath);
			res.setHeader(
				"Content-Type",
				this.httpServerHelper.getMimeText(path.extname(filePath)) ||
					"text/plain",
			);
			res.setHeader("Content-Length", fileStats.size);
			this.httpFileUtil.sendFile(res, sanitizedPath);
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
				route: glob("/modsync/hashes"),
				handler: this.getHashes.bind(this),
			},
			{
				route: glob("/modsync/fetch/**"),
				handler: this.fetchModFile.bind(this),
			},
		];

		try {
			for (const { route, handler } of routeTable) {
				const matches = route.exec(req.url || "");
				if (matches) return handler(req, res, matches);
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
