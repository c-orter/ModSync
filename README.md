# Corter's Mod Sync For SPT & Fika

This project allows clients to easily synchronize mods between server and client when using a remote SPT/Fika server.

## Getting Started

### Server Setup

1. Download the latest version of the server mod from the [GitHub Releases](https://github.com/Corter/ModSync/releases) page
2. Extract into your SPT folder like any other server mod
3. Start the server

> Look for the message `Mod: corter-modsync version: 0.0.0 by: Corter loaded` to ensure installation was successful

### Client Setup

1. Download the latest version of the client mod from the [GitHub Releases](https://github.com/Corter/ModSync/releases) page
2. Extract into your SPT/Fika folder like any other client mod
3. Start the client and enjoy!


## Configuration

### Server

> No serverside configuration so far.

### Client

| Configuration | Description | Default |
| --- | --- | --- |
| `SyncServerMods` | Sync server mods in addition to client mods | `false` |

## Ignoring Files & Folders

Sometimes you may not want clients to download certain files (for example if you as the host have client mods other users don't want).
Or maybe you as a client have modified configs for some of your client mods and don't want those changes overriden.

**Well fear not!**

For **files** that you don't want synced, create a file next to it with the same name followed by `.nosync`, for example:

For example, if you didn't want to sync the DeClutterer mod, you would create a new file in `BepInEx/plugins` named `TYR_DeClutterer.dll.nosync`
```sh
$ ls BepInEx/plugins
...
TYR_DeClutterer.dll
TYR_DeClutterer.dll.nosync
```

This can be cumbersome for mods with lots of files though, so as a shortcut you can exclude entire **folders** by adding a `.nosync` file to the folder.

For example, if you didn't want to sync the Donuts mod (why would you want that?), you would create a new file in `BepInEx/plugins/dvize.Donuts` named `.nosync`
```sh
ls BepInEx/plugins/dvize.Donuts
...
.nosync
```

> This convention works on both the server and the client so prevent syncing to your heart's content.

## Technical Explanation

This project is essentially a glorified HTTP wrapper with a few additional server routes added to the SPT server. It attempts to use CRC hashes and
UTC modification timestamps to more accurately determine when a file is actually changed, but in reality I haven't tested it that extensively.

Currently the way client mods are synced is by serving any file in `BepInEx/plugins` and `BepInEx/config` from the server. Similarly, server mods are
served from `user/mods`.

## Roadmap

- [x] Initial release
- [x] Super nifty GUI for notifying user of mod changes and monitoring download progress
- [x] Ability to exclude files/folders from syncing from both client and server
- [ ] Custom folder sync support (May be useful for cached bundles? or mods that add files places that aren't BepInEx/plugins, BepInEx/config, or user/mods)
- [ ] Maybe cooler progress bar/custom UI (low priority)
- [ ] Real tests?!? (low priority)