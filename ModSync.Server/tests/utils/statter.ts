import type { IStatter } from "../../src/utility/statter";
import { fs } from "memfs";

export class TestStatter implements IStatter {
	public stat = (path: string) => Promise.resolve(fs.statSync(path));
}
