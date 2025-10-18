const inc = require("semver/functions/inc");
const valid = require("semver/functions/valid");
const { pushd, popd, sed } = require("shelljs");

const packageJson = require("../package.json");
const currentVersion = packageJson.version;

let level = "patch";
let newVersion = null;

if (process.argv.length > 2) {
	if (process.argv[2].startsWith("--")) {
		level = process.argv[2].slice(2);
	} else if (valid(currentVersion)) {
		newVersion = process.argv[2];
	}
}

newVersion = newVersion ?? inc(currentVersion, level);

sed(
	"-i",
	`"version": "${currentVersion}"`,
	`"version": "${newVersion}"`,
	"package.json",
);

pushd("-q", "../ModSync.Plugin/");
sed("-i", `"${currentVersion}"`, `"${newVersion}"`, "Plugin.cs");
sed(
	"-i",
	`"${currentVersion}"`,
	`"${newVersion}"`,
	"Properties/AssemblyInfo.cs",
);
popd("-q");

sed(
	"-i",
	`"version": "${currentVersion}"`,
	`"version": "${newVersion}"`,
	"package.json",
);

pushd("-q", "../ModSync.Core/");
sed(
	"-i",
	`"${currentVersion}"`,
	`"${newVersion}"`,
	"Properties/AssemblyInfo.cs",
);
popd("-q");

pushd("-q", "../ModSync.Updater");
sed(
	"-i",
	`<Version>${currentVersion}</Version>`,
	`<Version>${newVersion}</Version>`,
	"ModSync.Updater.csproj",
);
popd("-q");

pushd("-q", "../ModSync.MetroHash");
sed(
	"-i",
	`version = "${currentVersion}"`,
	`version = "${newVersion}"`,
	"Cargo.toml",
);
popd("-q");
