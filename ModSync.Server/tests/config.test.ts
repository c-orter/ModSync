import { expect, beforeEach, describe, it, vi } from "vitest";

import { Config, ConfigUtil } from "../src/config";
import { TestFileSystem } from "./utils/fileSystem";
import { JsonUtil } from "./utils/jsonUtil";
import { PreSptModLoader } from "./utils/preSptModLoader";
import type { FileSystem as IFileSystem } from "@spt/utils/FileSystem";
import type { JsonUtil as IJsonUtil } from "@spt/utils/JsonUtil";
import type { PreSptModLoader as IPreSptModLoader } from "@spt/loaders/PreSptModLoader";
import { fs, vol } from "memfs";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import { mock } from "vitest-mock-extended";
import path from "node:path";

vi.mock("node:fs", async () => {
	const path = await vi.importActual("node:path");
	const { readFileSync } = await vi.importActual("node:fs");
	const { fs } = await vi.importActual("memfs");

	// @ts-expect-error Vitest types aren't exactly perfect
	const wasmPath = path.resolve(__dirname, "../src/utility/metrohash.wasm");

	// @ts-expect-error Vitest types aren't exactly perfect
	fs.writeFileSync(wasmPath, readFileSync(wasmPath))

	return fs;
});

describe("Config", () => {
	let config: Config;
	beforeEach(() => {
		config = new Config(
			[
				{
					name: "plugins",
					path: "plugins",
					enabled: true,
					enforced: false,
					silent: false,
					restartRequired: true,
				},
				{
					name: "mods",
					path: "mods",
					enabled: false,
					enforced: false,
					silent: false,
					restartRequired: false,
				},
			],
			["plugins/**/node_modules", "plugins/**/*.js"],
		);
	});

	it("should correctly identify excluded paths", () => {

		expect(config.isExcluded("plugins/test.dll")).toBe(false);
		expect(config.isExcluded("plugins/banana/node_modules")).toBe(true);
		expect(config.isExcluded("plugins/banana/test.js")).toBe(true);
		expect(config.isExcluded("plugins/banana/config.json")).toBe(false);
		expect(config.isExcluded("plugins/banana/node_modules/lodash")).toBe(false);
	});
});

describe("ConfigUtil", () => {
	beforeEach(() => {
		vol.reset();
	});

	it("should create config if none exists", async () => {
		vol.fromNestedJSON(
			{ "placeholder.txt": "to ensure directory exists" },
			process.cwd(),
		);

		await new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		).load();

		expect(fs.existsSync(path.join(process.cwd(), "config.jsonc"))).toBe(true);
	});

	it("should load config", async () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
				"syncPaths": [
					"plugins",
					{ "path": "mods", "enabled": false },
					{ "path": "doesnotexist", "enabled": true }
				],
				// Exclusions for commonly used SPT mods
				"exclusions": [
					"plugins/**/node_modules"
				]
			}`,
				plugins: {},
				mods: {},
			},
			process.cwd(),
		);

		const config = await new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		).load();

		expect(config.syncPaths).toMatchSnapshot();

		expect(config.exclusions).toEqual(["plugins/**/node_modules"]);
	});

	it("Should always include ModSync.Updater.exe in syncPaths", async () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [],
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins/**/node_modules"
					]
				}`,
				plugins: {},
				mods: {},
			},
			process.cwd(),
		);

		const config = await new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		).load();

		expect(config.syncPaths).toMatchSnapshot();
	});

	it("should reject syncPath object without path", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [
						{ "enabled": true, "enforced": false, "path": "plugins", "restartRequired": true, "silent": false },
						{ "enabled": false, "enforced": false, "path": "mods", "restartRequired": true, "silent": false },
						{ "enabled": true, "enforced": false, "restartRequired": true, "silent": false }
					],
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins/**/node_modules"
					]
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});

	it("should reject absolute paths", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [
						"/etc/shadow",
						"C:\\\\Windows\\\\System32\\\\cmd.exe"
					],
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins/**/node_modules"
					]
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});

	it("should reject paths outside of SPT root", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [
						"../../etc/shadow"
					],
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins/**/node_modules"
					]
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});

	it("should reject non-array syncPaths", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": "plugins",
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins/**/node_modules"
					]
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});

	it("should reject on non-string syncPath name", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [
						{ "path": "plugins", "name": 1 }
					],
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins/**/node_modules"
					]
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);	

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});

	it("should reject on syncPath name with invalid characters", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [
						{ "path": "plugins", "name": "[something]\t'test'" }
					],
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins/**/node_modules"
					]
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});

	it("should warn on explicitly excluded syncPaths", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [
						"plugins"
					],
					// Exclusions for commonly used SPT mods
					"exclusions": [
						"plugins"
					]
				}`,
			},
			process.cwd(),
		);

		const logger = {
			warning: vi.fn((...args) => { }),
		} as unknown as ILogger;

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			logger,
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot()
	})

	it("should reject on non-array exclusions", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"syncPaths": [
						"plugins"
					],
					// Exclusions for commonly used SPT mods
					"exclusions": "plugins/**/node_modules"
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});

	it("should reject invalid JSON", () => {
		vol.fromNestedJSON(
			{
				"config.jsonc": `{
					"invalid": "invalid"
				}`,
			},
			process.cwd(),
		);

		const configUtil = new ConfigUtil(
			new TestFileSystem() as IFileSystem,
			new JsonUtil() as IJsonUtil,
			new PreSptModLoader() as IPreSptModLoader,
			mock<ILogger>(),
		);

		expect(configUtil.load()).rejects.toThrowErrorMatchingSnapshot();
	});
});
