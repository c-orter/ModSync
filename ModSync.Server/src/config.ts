import path from "node:path";
import type { PreSptModLoader } from "@spt/loaders/PreSptModLoader";
import type { JsonUtil } from "@spt/utils/JsonUtil";
import type { VFS } from "@spt/utils/VFS";
import { glob } from "./utility/glob";
import { unixPath } from "./utility/misc";
import type { ILogger } from "@spt/models/spt/utils/ILogger";

export type SyncPath = {
	path: string;
	enabled?: boolean;
	enforced?: boolean;
	silent?: boolean;
	restartRequired?: boolean;
};

type RawConfig = {
	syncPaths: (string | SyncPath)[];
	exclusions: string[];
};

const DEFAULT_CONFIG = `{
	"syncPaths": [
		"BepInEx/plugins",
		"BepInEx/patchers",
		"BepInEx/config",
		{
			"enabled": false,
			"path": "user/mods",
			"restartRequired": false
		}
	],
	"exclusions": [
		// SPT Installer
		"BepInEx/plugins/spt",
		"BepInEx/patchers/spt-prepatch.dll",
		// Questing Bots
		"BepInEx/plugins/DanW-SPTQuestingBots/log",
		// Realism
		"user/mods/SPT-Realism/ProfileBackups",
		// Fika
		"user/mods/fika-server/types",
		"user/mods/fika-server/cache",
		"BepInEx/plugins/Fika.Dedicated.dll",
		// Live Flea Prices
		"user/mods/zzDrakiaXYZ-LiveFleaPrices/config",
		// Questing Bots
		"BepInEx/plugins/DanW-SPTQuestingBots/log",
		// EFTApi
		"BepInEx/plugins/kmyuhkyuk-EFTApi/cache",
		// Expanded Task Text (Accounts for bug with current version)
		"user/mods/ExpandedTaskText/src/**/cache.json",
		// Leaves Loot Fuckery
		"user/mods/leaves-loot_fuckery/output",
		// ADD MISSING QUEST WEAPON REQUIREMENTS
		"user/mods/zz_guiltyman-addmissingquestweaponrequirements/log.log",
		"user/mods/zz_guiltyman-addmissingquestweaponrequirements/user/logs",
		// Corter ModSync
		"BepInEx/patchers/Corter-ModSync-Patcher.dll",
		"**/*.nosync",
		"**/*.nosync.txt",
		// General server mods
		"user/mods/**/.git",
		"user/mods/**/node_modules",
		"user/mods/**/*.js",
		"user/mods/**/*.js.map",
		"**/*:Zone.Identifier",
		// acidphantasm-progressivebotsystem logs
		"user/mods/acidphantasm-progressivebotsystem/logs"
	]
}`;

export class Config {
	private _globs: RegExp[];
	constructor(
		public syncPaths: Required<SyncPath>[],
		public exclusions: string[],
	) {
		this._globs = exclusions.map(glob);
	}

	public isExcluded(filePath: string): boolean {
		return this._globs.some(
			(glob) =>
				glob.test(unixPath(filePath)),
		);
	}
}
export class ConfigUtil {
	constructor(
		private vfs: VFS,
		private jsonUtil: JsonUtil,
		private modImporter: PreSptModLoader,
		private logger: ILogger,
	) { }

	/**
	 * @throws {Error} If the config file does not exist
	 */
	private async readConfigFile(): Promise<RawConfig> {
		const modPath = this.modImporter.getModPath("Corter-ModSync");
		const configPath = path.join(modPath, "config.jsonc");

		if (!this.vfs.exists(configPath))
			await this.vfs.writeFilePromisify(configPath, DEFAULT_CONFIG);

		return this.jsonUtil.deserializeJsonC(
			// @ts-expect-error I am right, SPT is wrong
			await this.vfs.readFilePromisify(configPath, { encoding: "utf-8" }),
			"config.jsonc",
		) as RawConfig;
	}

	/**
	 * @throws {Error} If the config is invalid
	 */
	private validateConfig(config: RawConfig): void {
		if (!Array.isArray(config.syncPaths))
			throw new Error(
				"Corter-ModSync: config.jsonc 'syncPaths' is not an array. Please verify your config is correct and try again.",
			);

		if (!Array.isArray(config.exclusions))
			throw new Error(
				"Corter-ModSync: config.jsonc 'exclusions' is not an array. Please verify your config is correct and try again.",
			);

		for (const syncPath of config.syncPaths) {
			if (typeof syncPath !== "string" && !("path" in syncPath))
				throw new Error(
					"Corter-ModSync: config.jsonc 'syncPaths' is missing 'path'. Please verify your config is correct and try again.",
				);

			if (
				typeof syncPath === "string"
					? path.isAbsolute(syncPath)
					: path.isAbsolute(syncPath.path)
			)
				throw new Error(
					`Corter-ModSync: SyncPaths must be relative to SPT server root. Invalid path '${syncPath}'`,
				);

			if (
				path
					.relative(
						process.cwd(),
						path.resolve(
							process.cwd(),
							typeof syncPath === "string" ? syncPath : syncPath.path,
						),
					)
					.startsWith("..")
			)
				throw new Error(
					`Corter-ModSync: SyncPaths must within SPT server root. Invalid path '${syncPath}'`,
				);
		}
	}

	public async load(): Promise<Config> {
		const rawConfig = await this.readConfigFile();
		this.validateConfig(rawConfig);

		return new Config(
			[
				{
					enabled: true,
					enforced: true,
					silent: true,
					restartRequired: false,
					path: "ModSync.Updater.exe",
				},
				{
					enabled: true,
					enforced: true,
					silent: true,
					restartRequired: true,
					path: "BepInEx/plugins/Corter-ModSync.dll",
				},
				...rawConfig.syncPaths
					.map((syncPath) => ({
						enabled: true,
						enforced: false,
						silent: false,
						restartRequired: true,
						...(typeof syncPath === "string" ? { path: syncPath } : syncPath),
					}))
					.sort((a, b) => b.path.length - a.path.length),
			],
			rawConfig.exclusions,
		);
	}
}
