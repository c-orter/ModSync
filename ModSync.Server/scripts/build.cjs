const { config, rm, mkdir, cp, pushd, popd, exec, sed } = require("shelljs");
const { writeFileSync, readFileSync } = require("node:fs");
const { gzipSync } = require("node:zlib");

const packageJson = require("../package.json");

let configuration = "Release";
if (process.argv.includes("--debug")) configuration = "Debug";

rm("-rf", "../dist");
mkdir("-p", "../dist/user/mods/Corter-ModSync/src");
mkdir("-p", "../dist/BepInEx/plugins");

cp("package.json", "../dist/user/mods/Corter-ModSync/");
cp("-r", "src/*", "../dist/user/mods/Corter-ModSync/src");

pushd("-q", "../ModSync.MetroHash");
exec(
	"wasm-pack build --release --target nodejs --out-name metrohash --no-pack --manifest-path Cargo.toml -Z build-std=panic_abort,std -Z build-std-features=optimize_for_size,panic_immediate_abort",
);
cp(
	"pkg/metrohash_bg.wasm",
	"../dist/user/mods/Corter-ModSync/src/utility/metrohash.wasm",
);
popd("-q");

pushd("-q", "../");
exec(`dotnet build -c ${configuration}`);
popd("-q");

pushd("-q", "../ModSync.Updater");
exec(`dotnet publish -c ${configuration} -r win-x64`);
popd("-q");

cp(
	`../ModSync.Plugin/bin/${configuration}/netstandard2.1/Corter-ModSync.dll`,
	"../dist/BepInEx/plugins/",
);
cp(
	`../ModSync.Updater/bin/${configuration}/net9.0-windows/win-x64/publish/ModSync.Updater.exe`,
	"../dist/",
);

pushd("-q", "../dist");
config.silent = true;
exec(
	`7z a -tzip Corter-ModSync-v${packageJson.version}.zip BepInEx/ ModSync.Updater.exe user/`,
);
config.silent = false;
popd("-q");
