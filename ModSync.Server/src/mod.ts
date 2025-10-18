import type { DependencyContainer } from "tsyringe";

import type { IncomingMessage, ServerResponse } from "node:http";
import type { IPreSptLoadMod } from "@spt/models/external/IPreSptLoadMod";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import type { HttpListenerModService } from "@spt/services/mod/httpListener/HttpListenerModService";
import type { HttpFileUtil } from "@spt/utils/HttpFileUtil";
import type { FileSystem } from "@spt/utils/FileSystem";
import type { JsonUtil } from "@spt/utils/JsonUtil";
import { ConfigUtil, type Config } from "./config";
import { SyncUtil } from "./sync";
import { Router } from "./router";
import type { PreSptModLoader } from "@spt/loaders/PreSptModLoader";
import type { HttpServerHelper } from "@spt/helpers/HttpServerHelper";
import { Statter } from "./utility/statter";

class Mod implements IPreSptLoadMod {
	private static container: DependencyContainer;

	private static loadFailed = false;
	private static config: Config;

	public async preSptLoad(container: DependencyContainer): Promise<void> {
		Mod.container = container;
		const logger = container.resolve<ILogger>("WinstonLogger");
		const vfs = container.resolve<FileSystem>("FileSystem");
		const jsonUtil = container.resolve<JsonUtil>("JsonUtil");
		const modImporter = container.resolve<PreSptModLoader>("PreSptModLoader");
		const configUtil = new ConfigUtil(vfs, jsonUtil, modImporter, logger);
		const httpListenerService = container.resolve<HttpListenerModService>(
			"HttpListenerModService",
		);

		httpListenerService.registerHttpListener(
			"ModSyncListener",
			this.canHandleOverride,
			this.handleOverride,
		);

		try {
			Mod.config = await configUtil.load();
		} catch (e) {
			Mod.loadFailed = true;
			logger.error("Corter-ModSync: Failed to load config!");
			throw e;
		}

		if (!vfs.exists("ModSync.Updater.exe")) {
			Mod.loadFailed = true;
			logger.error(
				"Corter-ModSync: ModSync.Updater.exe not found! Please ensure ALL files from the release zip are extracted onto the server.",
			);
		}

		if (!vfs.exists("BepInEx/plugins/Corter-ModSync.dll")) {
			Mod.loadFailed = true;
			logger.error(
				"Corter-ModSync: Corter-ModSync.dll not found! Please ensure ALL files from the release zip are extracted onto the server.",
			);
		}
	}

	public canHandleOverride(_sessionId: string, req: IncomingMessage): boolean {
		return !Mod.loadFailed && (req.url?.startsWith("/modsync/") ?? false);
	}

	public async handleOverride(
		_sessionId: string,
		req: IncomingMessage,
		res: ServerResponse,
	): Promise<void> {
		const logger = Mod.container.resolve<ILogger>("WinstonLogger");
		const vfs = Mod.container.resolve<FileSystem>("FileSystem");
		const httpFileUtil = Mod.container.resolve<HttpFileUtil>("HttpFileUtil");
		const httpServerHelper =
			Mod.container.resolve<HttpServerHelper>("HttpServerHelper");
		const modImporter =
			Mod.container.resolve<PreSptModLoader>("PreSptModLoader");
		const statter = new Statter();
		const syncUtil = new SyncUtil(vfs, statter, Mod.config, logger);
		const router = new Router(
			Mod.config,
			syncUtil,
			vfs,
			statter,
			httpFileUtil,
			httpServerHelper,
			modImporter,
			logger,
		);

		try {
			router.handleRequest(req, res);
		} catch (e) {
			logger.error("Corter-ModSync: Failed to handle request!");
			throw e;
		}
	}
}

export const mod = new Mod();
