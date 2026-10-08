# Core Keeper save fix for CrossOver (Mac)

Core Keeper, run on a Mac through CrossOver, loses new characters and worlds. They seem fine while you play, but they are gone after you quit. This repo has a small mod that fixes that, plus the scripts and notes used to find the cause.

**Tested on:** CrossOver 26.3, macOS 27.0.1 on Apple silicon, Core Keeper Steam build 25723392 (Unity 6000.0.59f2, mod loader 1.3.0), on one Mac in October 2026. It may behave differently on other versions.

**CrossOver Preview 20261006 does not have this bug.** If you use that version, you do not need the mod, but you do need to set the bottle's graphics to DXVK. See [CrossOver Preview](#crossover-preview) below.

This project is not made by or connected to Pugstorm, Fireshine Games, or CodeWeavers.

## The problem

Signs that you have it:

- A new character or world is missing after you quit and launch again.
- `Player.log` has lines like `Write failed: Success : '...\saves\5.json.pugbackup' (-2147024896)`. "Success" means the error code was 0, so the game cannot tell what went wrong.
- `Player.log` has `Failed to upload saves to Steam Cloud`. The cloud upload stops at the first missing file, so even your older characters stop syncing.
- On a fresh install that already has Steam Cloud saves, the game quits at startup with `Init failed for Save Manager`.

`Player.log` is in the bottle at:

```
drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Player.log
```

The game shows no error on screen for any of this.

## CrossOver Preview

We tested CrossOver Preview 20261006 (build 27.0.0.41069, Wine 11.17) on 2026-10-08. The bottle it made for us ran Windows x86 code through the FEX emulator instead of Apple's Rosetta. The save bug did not happen there, with or without the mod.

**Set the bottle's graphics to DXVK.** With the default setting, the game used wined3d (Wine's own Direct3D) and crashed when we made a new character. With DXVK, the same step worked. The crash was in the graphics code, not the save code. To change it, quit the game, select the bottle in CrossOver, open its settings, and set Graphics to DXVK.

What we measured:

| Test | Result |
|---|---|
| `tools/WineFileCheck` | Mono's way of reading the error code got the real 2 on both threads. All checks agreed. |
| First launch of a fresh bottle that already had Steam Cloud saves, no mod | Loaded. No startup crash. |
| Saving an old character whose `.pugbackup` was missing, no mod | Saved, and the game made the `.pugbackup` itself. |
| New character and new world, no mod, DXVK | Both saved, then saved again with backups. 0 `Write failed`, 0 `Failed to upload`. |
| The same in a second fresh bottle, with the mod, DXVK | Same result. Both hooks attached. |

With the mod on this version, saves still work, but you end up with some empty `.pugbackup` files. The mod makes an empty file before each first save, and that empty file becomes the backup. The game does not upload `.pugbackup` files, so this stays on your Mac. You can remove the mod if you only use CrossOver Preview.

We have not tested a Preview bottle that uses Rosetta instead of FEX, so we do not know yet whether the fix comes from the newer Wine or from FEX. [NotProton](https://github.com/NotProtonNot/NotProton), which runs Windows games from the Mac Steam app, uses its own copy of this Preview, and we have not tested it either.

## The fix: the CrossOverSaveFix mod

The mod runs just before each save. If the file being saved, or its `.pugbackup` copy, does not really exist yet, it creates that file empty. The game's own save then has something to replace, and it works. The mod never changes or deletes a file that already exists.

### Install

1. Quit Core Keeper.
2. Copy the `mod/CrossOverSaveFix` folder into the game's mod folder. With a bottle named `Steam` (fish shell):

   ```fish
   set game "$HOME/Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/Core Keeper"
   mkdir -p "$game/CoreKeeper_Data/StreamingAssets/Mods"
   cp -R mod/CrossOverSaveFix "$game/CoreKeeper_Data/StreamingAssets/Mods/"
   ```

   macOS `cp` and `mkdir` have no long forms of `-R` and `-p`.
3. Launch the game normally. Safe mode turns all mods off, and Steam sometimes offers safe mode after a crash.
4. Check `Player.log`. Near the start you should see:

   ```
   [CrossOverSaveFix] StandaloneFilesystem.Write(2 args): hook attached
   [CrossOverSaveFix] StandaloneFilesystem.Write(3 args): hook attached
   ```

   If it says `HOOK NOT ATTACHED` instead, the game has probably changed, and the mod is not protecting your saves.

When the mod creates a file, it logs `[CrossOverSaveFix] created empty ... so the save can replace it`.

To remove it, delete the `CrossOverSaveFix` folder from the game's mod folder.

### About "elevated access"

The mod uses `System.IO` (file reading and writing) and Harmony (a library that lets a mod hook into the game's own code). Core Keeper blocks both for normal mods. So the mod is marked **Skip Safety Checks** in `ModManifest.json`, the setting Pugstorm's docs call elevated access. That means the game does not limit what it can do on your computer. The whole mod is one short file, `mod/CrossOverSaveFix/Scripts/CrossOverSaveFix.cs`. Please read it before you install it. It only touches:

- files in the save folder that the game is about to write, and only when they are missing;
- its own temporary copy in the game's mod loader folder (see "The mod loader's temp copy" below).

### Tested

| Test | Result |
|---|---|
| New character and new world, play, Save and Exit, relaunch | Kept. All 5 new files saved with real data, including the map file written at quit. 0 `Write failed`, 0 `Failed to upload`. |
| Existing save file whose `.pugbackup` was missing | The mod created the `.pugbackup`, and the save went through. |
| Launch again without any manual cleanup | The mod loaded and attached both hooks. |
| Deleting characters and worlds in game | The deletion reached Steam Cloud once the quit sync could finish. |

### Not tested yet

- **The very first launch of a fresh install that already has Steam Cloud saves, on CrossOver 26.3.** That is the startup crash above. In CrossOver Preview we saw the mod handle that first cloud download: it made every missing file before the game wrote it, and each file ended up with the real data. But Preview does not have the bug, so that does not prove the mod stops the crash on 26.3. If the game still quits at startup, use `restore_from_cloud_cache.py` below.
- Multiplayer.
- Any other CrossOver, macOS or Core Keeper version. A game update could rename the method the mod hooks. If that happens, the mod logs `HOOK NOT ATTACHED`.

## What is going on

These findings come from reading the game's code with `tools/InspectMethods`, logging inside the game, and testing outside it with `tools/WineFileCheck`.

### How the game saves a file

Every save goes through `Pug.Platform.StandaloneFilesystem.Write(name, path, data)` in `Pug.Other.dll`:

1. If `File.Exists` says `<file>.pugbackup` is there, delete it.
2. Write the new data to `<file>.pugtmp`.
3. If `File.Exists` says `<file>` is there, call `File.Replace`. That puts the `.pugtmp` in place and moves the old file to `.pugbackup`. Otherwise, call `File.Move` to rename the `.pugtmp` to `<file>`.
4. On any error, log `Write failed: ...`.

### What goes wrong under CrossOver

In short: the game reads Windows error codes from the wrong place, so it sees the wrong error code, and that makes `File.Exists` say yes for missing files.

When a Windows call fails, it leaves an error code for the program to read, such as 2 for "file not found". Programs normally read it with `GetLastError`. The game runs on Unity's version of Mono (the .NET runtime inside Unity), and Mono reads the code a faster way. It takes it straight from a fixed spot in the thread's memory, at offset `0x68` from the `gs` register (`emit_get_last_error` in [Unity's Mono source](https://github.com/Unity-Technologies/mono/blob/unity-main/mono/mini/mini-amd64.c)). On Windows, and in Wine on Linux, that spot holds the real error code. In CrossOver on the Mac, it does not.

What we measured:

- **Inside the game**, with the mod's diagnostics on: after Windows failed to find a file, `GetLastError` said 2, but the error code Mono kept was 0 on the thread that saves. On the main thread it was a random large number. Wine itself set the right code every time.
- **Outside the game**, `tools/WineFileCheck` reads the error code the same way Mono does. In CrossOver 26.3 (Wine 11.0) it got 0 or a random number, never the real 2. Under Wine 11.19 on Linux, under Proton (Wine 11.0) on Linux, and in a CrossOver Preview 20261006 bottle (Wine 11.17 with FEX), it got 2 every time.
- **Plain Windows calls work.** The same tool asked about files in several ways, in a fresh bottle and in the game's own bottle, with spaces and mixed slashes in the paths, and in the real save folder. Every answer was right. Only the error code read through `gs` is wrong.

How a wrong error code turns into lost saves (from the game's code):

- `File.Exists` asks Windows for the file's details. When that fails, it checks the error code. "Not found" means no. An error code of 0 looks like success, so `File.Exists` says yes, with a size of 0 bytes. That sends the game down the wrong branch in steps 1 and 3.
- The wrong branch then fails. On the save thread every failure reports error code 0, which Wine's message text calls "Success". That is where `Write failed: Success` comes from.
- The random number on the main thread explains the strange `Unknown error (0xa30700)` in the next section.

This matches what we saw before we knew the cause. Saves only worked when both `<file>` and `<file>.pugbackup` really existed, because then no call fails, so no error code is read. A made-up file name was reported correctly from the main thread only because its random number happened not to be 0.

That is why the mod checks for files by listing the folder instead of asking `File.Exists`.

### Linux and Proton

Core Keeper has a native Linux build, which Linux players and the Steam Deck use, so this bug does not affect them. People who run the Windows build through Proton should not have it either: in our test, Proton put the real error code where Mono reads it.

### The mod loader's temp copy

The game copies each mod's scripts to `AppData/Local/Temp/Pugstorm/Core Keeper/ModLoader/<mod>` and compiles them there. On the next launch, it deletes that copy first. Under CrossOver, that delete failed with `IOException: Unknown error (0xa30700)`, most likely the same wrong error code problem as above. The game then skipped the mod. So the mod deletes its own temp copy once it has loaded, and the next launch starts clean.

### How Steam Cloud sync behaves

- At startup, the game downloads cloud files that are new or changed (`CloudSyncDown`).
- At quit, it uploads changed files. Then it removes from the cloud any file you deleted locally (`CloudSyncUp`).
- Any error, such as one missing file, stops the whole upload. Deletions then never reach the cloud, and the deleted characters can come back. If the upload at quit fails, the game retries it at the next launch.
- Uploading a 0-byte file failed (`Failed to upload local file to cloud`), so an empty file left behind by a failed save stays local.

## Scripts (without the mod)

These came first. They work outside the game, and are still useful as a backup. They need Python 3 and no extra packages. Each one finds your Steam ID folder by itself; if you have more than one, pass `--steam-id`. For a bottle with a name other than `Steam`, pass `--bottle`.

- **`restore_from_cloud_cache.py`** copies Steam's local copy of your cloud saves (`drive_c/Program Files (x86)/Steam/userdata/<STEAM_ID>/1621690/remote/`) into the save folder, with a `.pugbackup` of each. This fixed the startup crash. It never overwrites a file, and it will not run while the game is open. Try `--dry-run` first, and back up that `remote/` folder before you start.
- **`fix_missing_saves.py`** follows `Player.log` while you play. For each failed write to a missing file, it creates that file empty, and the next autosave fills it. It only acts after the game has loaded its saves, and only while the game is running. **It has gaps:** it cannot help when the failure line has no file name, and it cannot rescue a file saved for the first time at quit (a character's map of a new world). The mod covers both.
- **`create_missing_settings.py`** creates `prefs.json` and `ServerConfig.json` while the game is running, so your settings stick. Only run it while the game is open; `--check` shows their sizes.
- **`watch_saves.py`** logs every file created, changed or deleted in a folder.
- **`test_fix_missing_saves.py`** tests `fix_missing_saves.py` against a fake bottle.

Example (fish shell):

```fish
python3 restore_from_cloud_cache.py --dry-run
python3 fix_missing_saves.py
```

## Tools

These need the .NET SDK (`dotnet`), version 10 or later, and a bottle with Core Keeper installed.

- **`tools/CompileCheck`** compiles the mod against the game's own DLLs, to catch mistakes before you launch: `dotnet build tools/CompileCheck`. For a bottle not named `Steam`, add `-p:GameManaged="<path to CoreKeeper_Data/Managed>"`.
- **`tools/InspectMethods`** prints a type's methods from a game DLL. With a method name, it also lists that method's calls, branches and strings, in order. It only reads the file, and never runs game code:

  ```fish
  dotnet run --project tools/InspectMethods -- "$game/CoreKeeper_Data/Managed/Pug.Other.dll" Pug.Platform.StandaloneFilesystem Write
  ```

  For a method that calls into Windows, it shows which DLL function it calls.
- **`tools/WineFileCheck`** is a small Windows program that repeats the game's save steps in an empty folder. After each step it asks several ways whether each file exists, and it reads the error code the way Unity's Mono does. It does not need the game. Build it with `dotnet publish tools/WineFileCheck` (one self-contained `.exe`, so the bottle needs no .NET), copy the `.exe` into a bottle, and run it there with a new folder:

  ```fish
  /Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine --bottle <bottle> --cx-app 'C:\WineFileCheck.exe' 'C:\wfc-test'
  ```

  Lines marked `MISMATCH` or `FAILED` show a problem. `--probe <path>...` only asks about the paths you give, and changes nothing.

  In a CrossOver Preview bottle that uses FEX, the `.exe` hangs at startup with no output. .NET crashes running its own built-in precompiled code, then hangs. Turn that code off, and use the Preview's path:

  ```fish
  env DOTNET_ReadyToRun=0 "/Applications/CrossOver Preview.app/Contents/SharedSupport/CrossOver/bin/wine" --bottle <bottle> --cx-app 'C:\WineFileCheck.exe' 'C:\wfc-test'
  ```

## Background

### The fix this started from

A CodeWeavers forum post by ashcraft (2026-02-28), reposting a Reddit fix by Komarov_d for Core Keeper 1.1.2.10: <https://www.codeweavers.com/compatibility/crossover/forum/core-keeper?;msg=347465>. It says: make the new character and world, enter the world, leave the game running, create the missing save files as empty files in a terminal, then choose Save and Exit. That works for the files you create. The mod does the same thing for every file, at the moment it is needed.

### How the save files are named

From the Core Keeper wiki (<https://corekeeper.atma.gg/en/Save_data>) and what we saw in testing. The save folder is `drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<STEAM_ID>/`. Slot numbers start at 0, which is the top slot in the game menu.

| File | Named by | Made when |
|---|---|---|
| `saves/<char>.json` | character slot | you make a character |
| `worlds/<world>.world.gzip` | world slot | you make a world |
| `worldinfos/<world>.worldinfo` | world slot | you make a world |
| `worldgenparams/<world>.json` | world slot | you make a world |
| `maps/<char>/<world>.mapparts.gzip` | both | a character first visits a world (written when you quit) |
| `servermaps/<world>.mapparts.gzip` | world slot | you use a Cartography Table |
| `prefs.json`, `ServerConfig.json` | - | your settings; never synced to the cloud |

Each file also gets a `.pugbackup` copy, which holds the previous version.

### Other macOS prompts you may see

- **"steam" was prevented from modifying apps on your Mac.** This is CrossOver's launcher helper trying to update its launcher apps in `~/Applications/CrossOver/`. It does not affect the game. You can allow it under System Settings > Privacy & Security > App Management.
- **A microphone request** comes from the Steam client inside CrossOver getting its voice chat ready. The game does not need it. Allowing it only matters for Steam voice chat.

## License

MIT. See [LICENSE](LICENSE).
