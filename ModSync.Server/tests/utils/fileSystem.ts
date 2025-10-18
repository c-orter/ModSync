import { fs } from "memfs";
import type Dirent from "memfs/lib/Dirent";
import type { FileSystem } from "@spt/utils/FileSystem";

export class TestFileSystem {
	public exists(path: string) {
		return Promise.resolve(fs.existsSync(path));
	}

	public getFiles(
		directory: string,
		searchRecursive = false,
		fileTypes?: string[],
		includeInputDir = false,
	) {
		return Promise.resolve(
			(fs.readdirSync(directory, { withFileTypes: true }) as Dirent[])
				.filter((item) => !item.isDirectory())
				.map((item) => item.name.toString()),
		);
	}
	public getDirectories(
		directory: string,
		searchRecursive = false,
		includeInputDir = false,
	) {
		return Promise.resolve(
			(fs.readdirSync(directory, { withFileTypes: true }) as Dirent[])
				.filter((item) => item.isDirectory())
				.map((item) => item.name.toString()),
		);
	}

	public read(path: string): Promise<string> {
		return Promise.resolve(fs.readFileSync(path, "utf-8") as string);
	}

	public readRaw(path: string): Promise<Buffer> {
		const contents = fs.readFileSync(path);

		return Promise.resolve(contents as Buffer);
	}

	public readJson: FileSystem["readJson"] = (file) =>
		Promise.resolve(JSON.parse(fs.readFileSync(file) as string));

	public write: FileSystem["write"] = (path, contents) =>
		Promise.resolve(fs.writeFileSync(path, contents!));
}
