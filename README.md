# Core Keeper save fix for CrossOver (Mac)

Core Keeper, run on a Mac through CrossOver, loses new characters and worlds. They seem fine while you play, but they are gone after you quit. This repo has a small mod that fixes that, plus the scripts and notes used to find the cause.

**Tested on:** CrossOver 26.3, macOS 27.0.1 on Apple silicon, Core Keeper Steam build 25723392 (Unity 6000.0.59f2, mod loader 1.3.0), on one Mac in October 2026. It may behave differently on other versions.

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

- **The very first launch of a fresh install that already has Steam Cloud saves.** That is the startup crash above. The mod loads before the game's cloud download starts, so it should cover it. If the game still quits at startup, use `restore_from_cloud_cache.py` below.
- Multiplayer.
- Any other CrossOver, macOS or Core Keeper version. A game update could rename the method the mod hooks. If that happens, the mod logs `HOOK NOT ATTACHED`.

## What is going on

These findings come from reading the game's code with `tools/InspectMethods`, plus logging inside the game.

### How the game saves a file

Every save goes through `Pug.Platform.StandaloneFilesystem.Write(name, path, data)` in `Pug.Other.dll`:

1. If `File.Exists` says `<file>.pugbackup` is there, delete it.
2. Write the new data to `<file>.pugtmp`.
3. If `File.Exists` says `<file>` is there, call `File.Replace`. That puts the `.pugtmp` in place and moves the old file to `.pugbackup`. Otherwise, call `File.Move` to rename the `.pugtmp` to `<file>`.
4. On any error, log `Write failed: ...`.

### What goes wrong under CrossOver

With the mod logging before and after each write, `File.Exists` reported `.pugtmp` and `.pugbackup` files in the save folder as present, at 0 bytes, when they were not on disk. Listing the folder showed they were missing. A made-up file name in the temp folder was reported correctly, so not every path is affected.

Our best explanation is that these false answers send the game down the wrong branch in steps 1 and 3. Wine (the Windows layer CrossOver is built on) then fails the rename or replace, but returns error code 0. That is where `Write failed: Success` comes from.

What we saw lines up with that:

- Saves only worked when both `<file>` and `<file>.pugbackup` really existed before the write.
- A missing file failed with no path in the message (the `File.Move` branch).
- A file whose `.pugbackup` was missing failed with the `.pugbackup` path in the message (the `File.Replace` branch).

That is why the mod checks for files by listing the folder instead of asking `File.Exists`.

### The mod loader's temp copy

The game copies each mod's scripts to `AppData/Local/Temp/Pugstorm/Core Keeper/ModLoader/<mod>` and compiles them there. On the next launch, it deletes that copy first. Under CrossOver, that delete failed with `IOException: Unknown error (0xa30700)`. The game then skipped the mod. So the mod deletes its own temp copy once it has loaded, and the next launch starts clean.

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
