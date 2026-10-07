# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A fix for Core Keeper losing saves under CrossOver on macOS. The main deliverable is a Core Keeper mod (`mod/CrossOverSaveFix/`). The Python scripts are older, outside-the-game workarounds, kept as backups. `README.md` is the user-facing doc and holds the evidence for every claim; keep it in sync when behavior or findings change.

## Commands

```fish
python3 test_fix_missing_saves.py                 # tests fix_missing_saves.py against a fake bottle (prints PASS/FAIL per check)
dotnet build tools/CompileCheck                   # compile the mod against the game's real DLLs (0 warnings expected)
dotnet run --project tools/InspectMethods -- <dll> <Namespace.Type> [Method]   # signatures; with Method, its calls/branches/strings
python3 restore_from_cloud_cache.py --dry-run     # scripts find the Steam ID folder themselves; --bottle/--steam-id override
```

There is no single-test runner: `test_fix_missing_saves.py` is one script with named checks. The game DLLs live in the bottle at `.../steamapps/common/Core Keeper/CoreKeeper_Data/Managed/` (`Pug.Other.dll` has the save code, `PugMod.SDK.Runtime.dll` has `IMod`). `CompileCheck` defaults to a bottle named `Steam`; override with `-p:GameManaged=...`.

## How the mod works and why

- **No Unity build step.** The game compiles mod `.cs` files itself at load time. A mod is just `ModManifest.json` (the SDK's `ModMetadata` serialized) plus `Scripts/*.cs`, installed to `CoreKeeper_Data/StreamingAssets/Mods/CrossOverSaveFix/`. After editing, run `CompileCheck`, copy the folder in, and relaunch the game. Mods only load at game start.
- **The hook:** a Harmony prefix on both `Pug.Platform.StandaloneFilesystem.Write` overloads. Patches are applied automatically from `[HarmonyPatch]` attributes; keep `[HarmonyTargetMethods]`, `[HarmonyPrefix]` and `[HarmonyPostfix]` explicit and the methods `public`, matching the SDK examples. `Init()` logs `hook attached` or `HOOK NOT ATTACHED` for each target.
- **Never trust `File.Exists` for save-folder files inside the game.** Under CrossOver it reported missing `.pugtmp`/`.pugbackup` files as present (0 bytes). That false answer is the root cause of the save bug, and it also made the first versions of the hook silently do nothing. Use `ReallyExists` (folder listing) instead.
- **The game's write routine** (read from IL; see README "How the game saves a file"): delete `.pugbackup` if it "exists", write `.pugtmp`, then `File.Replace` if the target "exists" or `File.Move` if not. A save works when both the target and its `.pugbackup` really exist beforehand, which is what the prefix guarantees.
- **`skipSafetyChecks: true`** in the manifest is required: `System.IO` and Harmony are blocked for normal mods.
- **`RemoveOwnLoaderCopy()`** deletes the loader's temp copy (`AppData/Local/Temp/Pugstorm/Core Keeper/ModLoader/CrossOverSaveFix`) after load. Without it, the next launch's loader fails to delete that folder under Wine (`IOException 0xa30700`) and skips the mod. If the mod ever fails to load with that error, clearing that folder by hand (game closed) recovers it.
- **`Diagnostics`** (default `false`) logs file/backup/tmp state before and after every write. Flip it on when investigating; it is how the `File.Exists` problem was found.

## Python scripts

- Each script is deliberately self-contained (duplicated `game_running()` and `find_steam_id()`), so one file can be copied and run alone, including with `python3 -I`, which cannot import sibling modules.
- `game_running()` matches the Wine process by its Windows-path command line (`^[A-Za-z]:\\...\\CoreKeeper.exe`). A plain `pgrep -f CoreKeeper.exe` matches any process that mentions the name.
- `fix_missing_saves.py` reads `Player.log` in binary: the log has CRLF line endings, and text-mode `tell()` returns huge cookies that made the follow loop spin. It also rejects `..` paths from log lines and only acts after `PugDatabase initialized`.

## Testing against the real game

- Only the user can launch the game. Watch `drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Player.log` (Unity moves the old one to `Player-prev.log` on each launch). Key lines: `Write failed`, `Failed to upload saves to Steam Cloud`, `[CrossOverSaveFix] ...`, `CloudSyncDown`/`CloudSyncUp`.
- Saves are synced with Steam Cloud. Creating, deleting or breaking save files in tests changes the user's cloud saves. Back up `Steam/userdata/<STEAM_ID>/1621690/remote/` before risky tests, and use throwaway characters and worlds.
- Check the game is closed (`game_running()`'s pattern) before changing files in the save folder or the mod folder.

## Public repo

This repo is public on GitHub. Do not commit Steam IDs, save data, world or character names, or absolute home paths. `backups/` is gitignored and must stay local.
