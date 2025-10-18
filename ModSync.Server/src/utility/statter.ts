import { stat, type Stats } from "fs-extra";

export interface IStatter {
	stat: (path: string) => Promise<Stats>;
}

export class Statter implements IStatter {
	public stat = stat;
}
