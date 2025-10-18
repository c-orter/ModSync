import path from "node:path";
import type { ILogger } from "@spt/models/spt/utils/ILogger";
import type { FileSystem } from "@spt/utils/FileSystem";
import type { Config, SyncPath } from "./config";
import { hashFile } from "./utility/imoHash";
import { HttpError, winPath } from "./utility/misc";
import { Semaphore } from "./utility/semaphore";
import type { IStatter } from "./utility/statter";

type ModFile = {
	hash: string;
	directory: boolean;
};

export class SyncUtil {
	private limiter = new Semaphore(1024);

	constructor(
		private vfs: FileSystem,
		private statter: IStatter,
		private config: Config,
		private logger: ILogger,
	) {}

	private async getFilesInDir(baseDir: string, dir: string): Promise<string[]> {
		if (!(await this.vfs.exists(dir))) {
			this.logger.warning(
				`Corter-ModSync: Directory '${dir}' does not exist, will be ignored.`,
			);
			return [];
		}

		const stats = await this.statter.stat(dir);
		if (stats.isFile()) return [dir];

		const files: string[] = [];
		for (const fileName of await this.vfs.getFiles(dir)) {
			const file = path.join(dir, fileName);

			if (this.config.isExcluded(file)) continue;

			files.push(file);
		}

		for (const dirName of await this.vfs.getDirectories(dir)) {
			const subDir = path.join(dir, dirName);

			if (this.config.isExcluded(subDir)) continue;

			const subFiles = await this.getFilesInDir(baseDir, subDir);
			if (
				(await this.vfs.getFiles(subDir)).length === 0 &&
				(await this.vfs.getDirectories(subDir)).length === 0
			)
				files.push(subDir);

			files.push(...subFiles);
		}

		if (
			stats.isDirectory() &&
			(await this.vfs.getFiles(dir)).length === 0 &&
			(await this.vfs.getDirectories(dir)).length === 0
		)
			files.push(dir);

		return files;
	}

	private async buildModFile(
		file: string,
		// biome-ignore lint/correctness/noEmptyPattern: <explanation>
		{}: Required<SyncPath>,
	): Promise<ModFile> {
		const stats = await this.statter.stat(file);
		if (stats.isDirectory()) return { hash: "", directory: true };

		let retryCount = 0;
		const lock = await this.limiter.acquire();
		while (true) {
			try {
				const hash = await hashFile(file);
				lock.release();

				return {
					hash,
					directory: false,
				};
			} catch (e) {
				if (
					e instanceof Error &&
					"code" in e &&
					e.code === "EBUSY" &&
					retryCount < 5
				) {
					this.logger.error(
						`Error reading '${file}'. Retrying (${retryCount}/5)...`,
					);
					await new Promise((resolve) => setTimeout(resolve, 500));
					retryCount++;
					continue;
				}

				this.logger.error(`Error reading '${file}'. Exiting...`);
				this.logger.error(`${e}`);
				throw new HttpError(
					500,
					`Corter-ModSync: Error reading '${file}'\n${e}`,
				);
			}
		}
	}

	public async hashModFiles(
		syncPaths: Config["syncPaths"],
	): Promise<Record<string, Record<string, ModFile>>> {
		const result: Record<string, Record<string, ModFile>> = {};
		const processedFiles = new Set<string>();

		const startTime = performance.now();
		for (const syncPath of syncPaths) {
			const files = await this.getFilesInDir(syncPath.path, syncPath.path);
			const filesResult: Record<string, ModFile> = {};

			for (const file of files) {
				if (processedFiles.has(winPath(file))) continue;

				filesResult[winPath(file)] = await this.buildModFile(file, syncPath);

				processedFiles.add(winPath(file));
			}

			result[winPath(syncPath.path)] = filesResult;
		}

		this.logger.info(
			`Corter-ModSync: Hashed ${processedFiles.size} files in ${performance.now() - startTime}ms`,
		);

		return result;
	}

	/**
	 * @throws {Error} If file path is invalid
	 */
	public sanitizeDownloadPath(
		file: string,
		syncPaths: Config["syncPaths"],
	): string {
		const normalized = path.join(
			path.normalize(file).replace(/^(\.\.(\/|\\|$))+/, ""),
		);

		for (const syncPath of syncPaths) {
			const fullPath = path.join(process.cwd(), syncPath.path);
			if (!path.relative(fullPath, normalized).startsWith("..")) {
				return normalized;
			}
		}

		throw new HttpError(
			400,
			`Corter-ModSync: Requested file '${file}' is not in an enabled sync path!`,
		);
	}
}
