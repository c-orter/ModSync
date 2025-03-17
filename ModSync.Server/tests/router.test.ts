import { expect, beforeEach, describe, it, vi } from "vitest";
import { fs, vol } from "memfs";
import packageJson from "../package.json";

import { Router } from "../src/router";
import { Config } from "../src/config";
import { SyncUtil } from "../src/sync";
import { TestFileSystem } from "./utils/fileSystem";
import type { FileSystem as IFileSystem } from "@spt/utils/FileSystem";
import type { HttpFileUtil } from "@spt/utils/HttpFileUtil";
import { mock } from "vitest-mock-extended";
import { PreSptModLoader } from "./utils/preSptModLoader";
import type { PreSptModLoader as IPreSptModLoader } from "@spt/loaders/PreSptModLoader";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import type { IncomingMessage, ServerResponse } from "node:http";
import { HttpError } from "../src/utility/misc";
import type { HttpServerHelper } from "@spt/helpers/HttpServerHelper";
import { TestStatter } from "./utils/statter";

vi.mock("node:fs", async () => {
	const { readFileSync } =
		await vi.importActual<typeof import("node:fs")>("node:fs");
	const { fs } = await vi.importActual<typeof import("memfs")>("memfs");

	return {
		...fs,
		readFileSync: vi.fn((path, options) =>
			path.endsWith(".wasm")
				? readFileSync("../ModSync.MetroHash/pkg/metrohash_bg.wasm")
				: fs.readFileSync(path, options),
		),
	};
});

describe("router", async () => {
	const config = new Config(
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
				name: "user/mods",
				path: "user/mods",
				enabled: true,
				enforced: false,
				silent: false,
				restartRequired: false,
			},
			{
				name: "user/cache",
				path: "user/cache",
				enabled: false,
				enforced: false,
				silent: false,
				restartRequired: true,
			},
		],
		[
			"**/*.nosync",
			"**/*.nosync.txt",
			"plugins/file2.dll",
			"plugins/file3.dll",
			"plugins/ModName",
			"plugins/OtherMod/subdir",
			"user/mods/**/*.js",
			"user/mods/**/*.js.map",
			"**/node_modules",
		],
	);
	const vfs = new TestFileSystem() as IFileSystem;
	const statter = new TestStatter();
	const logger = mock<ILogger>();
	const syncUtil = new SyncUtil(vfs, statter, config, logger);
	const httpFileUtil = mock<HttpFileUtil>();
	const httpServerHelper = mock<HttpServerHelper>();
	const modImporter = new PreSptModLoader() as IPreSptModLoader;
	const router = new Router(
		config,
		syncUtil,
		vfs,
		statter,
		httpFileUtil,
		httpServerHelper,
		modImporter,
		logger,
	);

	describe("getServerVersion", () => {
		let req = mock<IncomingMessage>({
			headers: { "modsync-version": packageJson.version },
		});
		let res = mock<ServerResponse>();

		beforeEach(() => {
			vol.reset();
			vol.fromNestedJSON({ "package.json": '{ "version": "1.0.0" }' });

			req = mock<IncomingMessage>({
				headers: { "modsync-version": packageJson.version },
			});
			res = mock<ServerResponse>();
		});

		it("should return server version", async () => {
			await router.getServerVersion(
				req,
				res,
				mock<RegExpMatchArray>(),
				new URLSearchParams(),
			);

			expect(res.end).toHaveBeenCalledWith(JSON.stringify("1.0.0"));
		});
	});

	describe("getSyncPaths", () => {
		let req = mock<IncomingMessage>({
			headers: { "modsync-version": packageJson.version },
		});
		let res = mock<ServerResponse>();

		beforeEach(() => {
			req = mock<IncomingMessage>({
				headers: { "modsync-version": packageJson.version },
			});
			res = mock<ServerResponse>();
		});

		it("should return sync paths", async () => {
			await router.getSyncPaths(
				req,
				res,
				mock<RegExpMatchArray>(),
				new URLSearchParams(),
			);

			expect(res.end.mock.calls).toMatchSnapshot();
		});

		it("should serve 'rescue' mode paths when no version specified", async () => {
			req.headers["modsync-version"] = undefined;

			await router.getSyncPaths(
				req,
				res,
				mock<RegExpMatchArray>(),
				new URLSearchParams(),
			);

			expect(res.end.mock.calls).toMatchSnapshot();
		});
	});

	describe("getExclusions", () => {
		let req = mock<IncomingMessage>({
			headers: { "modsync-version": packageJson.version },
		});
		let res = mock<ServerResponse>();
		beforeEach(() => {
			req = mock<IncomingMessage>({
				headers: { "modsync-version": packageJson.version },
			});
			res = mock<ServerResponse>();
		});

		it("should return list of exclusions", async () => {
			await router.getExclusions(
				req,
				res,
				mock<RegExpMatchArray>(),
				new URLSearchParams(),
			);

			expect(res.end.mock.calls).toMatchSnapshot();
		});
	});

	describe("getHashes", () => {
		const directoryStructure = {
			plugins: {
				"file1.dll": "Test content",
				"file2.dll": "Test content 2",
				"file2.dll.nosync": "",
				"file3.dll": "Test content 3",
				"file3.dll.nosync.txt": "",
				ModName: {
					"mod_name.dll": "Test content 4",
					".nosync": "",
				},
				OtherMod: {
					"other_mod.dll": "Test content 5",
					subdir: {
						"image.png": "Test Image",
						".nosync": "",
					},
				},
			},
			user: {
				mods: {},
			},
		};

		let req = mock<IncomingMessage>({
			headers: { "modsync-version": packageJson.version },
		});
		let res = mock<ServerResponse>();
		beforeEach(() => {
			vol.reset();
			vol.fromNestedJSON(directoryStructure);

			req = mock<IncomingMessage>({
				headers: { "modsync-version": packageJson.version },
			});
			res = mock<ServerResponse>();
		});

		it("should return hashes", async () => {
			await router.getHashes(
				req,
				res,
				mock<RegExpMatchArray>(),
				new URLSearchParams(),
			);

			expect(res.end.mock.calls).toMatchSnapshot();
		});

		it("should serve 'rescue' mode hashes when no version specified", async () => {
			req.headers["modsync-version"] = undefined;

			await router.getHashes(
				req,
				res,
				mock<RegExpMatchArray>(),
				new URLSearchParams(),
			);

			expect(res.end.mock.calls).toMatchSnapshot();
		});
	});

	describe("fetchModFile", () => {
		const directoryStructure = {
			plugins: {
				"file1.dll": "Test content",
				"file2.dll": "Test content 2",
				"file2.dll.nosync": "",
				"file3.dll": "Test content 3",
				"file3.dll.nosync.txt": "",
				ModName: {
					"mod_name.dll": "Test content 4",
					".nosync": "",
				},
				OtherMod: {
					"other_mod.dll": "Test content 5",
					subdir: {
						"image.png": "Test Image",
						".nosync": "",
					},
				},
			},
			user: {
				mods: {},
			},
		};

		let req = mock<IncomingMessage>({
			headers: { "modsync-version": packageJson.version },
		});
		let res = mock<ServerResponse>();

		beforeEach(() => {
			vol.reset();
			vol.fromNestedJSON(directoryStructure);
			httpFileUtil.sendFileAsync.mockClear();

			req = mock<IncomingMessage>({
				headers: { "modsync-version": packageJson.version },
			});
			res = mock<ServerResponse>();
		});

		it("should return mod file", async () => {
			await router.fetchModFile(
				req,
				res,
				["/modsync/fetch/plugins/file1.dll", "plugins/file1.dll"],
				new URLSearchParams(),
			);

			expect(res.setHeader).toHaveBeenCalledWith("Content-Length", 12);
			expect(httpFileUtil.sendFileAsync).toHaveBeenCalledWith(
				res,
				"plugins\\file1.dll",
			);
		});

		it("should reject on non-existent path", () => {
			expect(
				router.fetchModFile(
					req,
					res,
					["/modsync/fetch/plugins/banana.dll", "plugins/banana.dll"],
					new URLSearchParams(),
				),
			).rejects.toThrowError(
				new HttpError(
					404,
					"Attempt to access non-existent path plugins/banana.dll",
				),
			);
		});
	});
});
