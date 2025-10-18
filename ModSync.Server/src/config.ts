import path from "node:path";
import type { PreSptModLoader } from "@spt/loaders/PreSptModLoader";
import type { JsonUtil } from "@spt/utils/JsonUtil";
import type { FileSystem } from "@spt/utils/FileSystem";
import { glob } from "./utility/glob";
import { unixPath } from "./utility/misc";
import type { ILogger } from "@spt/models/spt/utils/ILogger";

export type SyncPath = {
	name?: string;
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
			"name": "(Optional) Server mods",
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
		"BepInEx/plugins/Fika.Headless.dll",
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
		// Acid's Progressive Bot System
		"user/mods/acidphantasm-progressivebotsystem/logs",
		// Corter ModSync
		"BepInEx/patchers/Corter-ModSync-Patcher.dll",
		"**/*.nosync",
		"**/*.nosync.txt",
		// General server mods
		"user/mods/**/.git",
		"user/mods/**/node_modules",
		"user/mods/**/*.js",
		"user/mods/**/*.js.map",
		"**/*:Zone.Identifier"
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
		return this._globs.some((glob) => glob.test(unixPath(filePath)));
	}
}
export class ConfigUtil {
	constructor(
		private vfs: FileSystem,
		private jsonUtil: JsonUtil,
		private modImporter: PreSptModLoader,
		private logger: ILogger,
	) {}

	/**
	 * @throws {Error} If the config file does not exist
	 */
	private async readConfigFile(): Promise<RawConfig> {
		const modPath = this.modImporter.getModPath("Corter-ModSync");
		const configPath = path.join(modPath, "config.jsonc");

		if (!(await this.vfs.exists(configPath))) {
			await this.vfs.write(configPath, DEFAULT_CONFIG);
		}

		return this.jsonUtil.deserializeJsonC(
			await this.vfs.read(configPath),
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

		const uniquePaths = new Set();
		for (const syncPath of config.syncPaths) {
			if (
				typeof syncPath === "object" &&
				typeof syncPath.name !== "undefined" &&
				typeof syncPath.name !== "string"
			) {
				throw new Error(
					"Corter-ModSync: config.jsonc 'syncPaths.name' must be a string. Please verify your config is correct and try again.",
				);
			}

			if (
				typeof syncPath === "object" &&
				typeof syncPath.name === "string" &&
				/[\n\t\\"'\[\]]/.test(syncPath.name)
			) {
				throw new Error(
					`Corter-ModSync: config.jsonc 'syncPaths.name' contains invalid characters. Please make sure your name does not include any of the following characters: \n \t \\ " ' [ ]`,
				);
			}

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

			if (
				uniquePaths.has(typeof syncPath === "string" ? syncPath : syncPath.path)
			)
				throw new Error(
					`Corter-ModSync: SyncPaths must be unique. Duplicate path '${syncPath}'`,
				);

			if (
				config.exclusions.includes(
					typeof syncPath === "string" ? syncPath : syncPath.path,
				)
			)
				throw new Error(
					`Corter-ModSync: '${syncPath}' has been added as a sync path and is also in the 'exclusions' array. This probably isn't doing what you want. If you no longer want to sync this path, remove it from the 'exclusions' and 'syncPaths' arrays.`,
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
					name: "(Builtin) ModSync Updater",
				},
				{
					enabled: true,
					enforced: true,
					silent: true,
					restartRequired: true,
					path: "BepInEx/plugins/Corter-ModSync.dll",
					name: "(Builtin) ModSync Plugin",
				},
				...rawConfig.syncPaths
					.map((syncPath) => ({
						enabled: true,
						enforced: false,
						silent: false,
						restartRequired: true,
						name: typeof syncPath === "string" ? syncPath : syncPath.path,
						...(typeof syncPath === "string" ? { path: syncPath } : syncPath),
					}))
					.sort((a, b) => b.path.length - a.path.length),
			],
			rawConfig.exclusions,
		);
	}
}
