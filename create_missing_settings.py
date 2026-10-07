"""Create empty prefs.json and ServerConfig.json (plus .pugbackup) for Core Keeper.

Under CrossOver the game can't create a file that doesn't exist yet. Run this
while the game is open and past the main menu; its next save (about once a
minute in a world) fills the empty files with real settings. Then run it again
with --check to confirm they have content before quitting.

Never run it with the game closed: the game may fail to read an empty
prefs.json at startup. If that happens, delete the empty file.

Usage:
  python3 create_missing_settings.py [--steam-id ID] [--bottle NAME]
  python3 create_missing_settings.py --check
"""
import argparse
import subprocess
import sys
from pathlib import Path

BOTTLES = Path.home() / "Library/Application Support/CrossOver/Bottles"
NAMES = ["prefs.json", "ServerConfig.json"]


def game_running():
    # Match only the Wine game process, not anything mentioning CoreKeeper.exe.
    result = subprocess.run(["pgrep", "-f", r"^[A-Za-z]:\\.*\\CoreKeeper\.exe( |$)"], capture_output=True)
    return result.returncode == 0


def find_steam_id(parent):
    """Return the only numeric folder name in parent (the Steam account ID), or exit."""
    ids = sorted(p.name for p in parent.iterdir() if p.is_dir() and p.name.isdigit()) if parent.is_dir() else []
    if len(ids) == 1:
        return ids[0]
    if not ids:
        sys.exit(f"No Steam ID folder in {parent}. Has the game been launched in this bottle?")
    sys.exit(f"More than one Steam ID folder in {parent} ({', '.join(ids)}). Pick one with --steam-id.")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bottle", default="Steam")
    parser.add_argument("--steam-id", help="Steam account ID folder (found automatically if there is only one)")
    parser.add_argument("--check", action="store_true", help="only report file sizes")
    args = parser.parse_args()

    steam_dir = BOTTLES / args.bottle / "drive_c/users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam"
    folder = steam_dir / (args.steam_id or find_steam_id(steam_dir))

    if args.check:
        for name in NAMES:
            for path in (folder / name, folder / f"{name}.pugbackup"):
                size = path.stat().st_size if path.exists() else None
                print(f"{path.name}: {'missing' if size is None else f'{size} bytes'}")
        return

    if not game_running():
        sys.exit("Core Keeper is not running. Launch it and load into a world first.")
    if not folder.is_dir():
        sys.exit(f"No save folder at {folder}")

    for name in NAMES:
        for path in (folder / name, folder / f"{name}.pugbackup"):
            if path.exists():
                print(f"skip (exists): {path.name}")
            else:
                path.touch()
                print(f"created empty: {path.name}")


if __name__ == "__main__":
    main()
