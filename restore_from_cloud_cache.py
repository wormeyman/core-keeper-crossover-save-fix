"""Copy Steam's local cloud cache for Core Keeper into the game's save folder.

Each file is copied twice: as itself and as <name>.pugbackup, keeping dates.
Existing files are never overwritten. Refuses to run while the game is open.

Usage:
  python3 restore_from_cloud_cache.py [--steam-id ID] [--bottle NAME] [--dry-run]
  python3 restore_from_cloud_cache.py --source DIR --dest DIR   (for testing)
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

BOTTLES = Path.home() / "Library/Application Support/CrossOver/Bottles"
APP_ID = "1621690"


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
    parser.add_argument("--source", type=Path, help="override the cloud cache folder")
    parser.add_argument("--dest", type=Path, help="override the save folder")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    drive_c = BOTTLES / args.bottle / "drive_c"
    userdata = drive_c / "Program Files (x86)/Steam/userdata"
    steam_id = args.steam_id
    if not steam_id and not (args.source and args.dest):
        steam_id = find_steam_id(userdata)
    source = args.source or userdata / steam_id / APP_ID / "remote"
    dest = args.dest or drive_c / "users/crossover/AppData/LocalLow/Pugstorm/Core Keeper/Steam" / steam_id

    if game_running():
        sys.exit("Core Keeper is running. Quit it first.")
    if not source.is_dir():
        sys.exit(f"No cloud cache at {source}")

    copied = skipped = 0
    for src in sorted(p for p in source.rglob("*") if p.is_file()):
        rel = src.relative_to(source)
        for target in (dest / rel, dest / f"{rel}.pugbackup"):
            if target.exists():
                print(f"skip (exists): {target.relative_to(dest)}")
                skipped += 1
                continue
            print(f"{'would copy' if args.dry_run else 'copy'}: {target.relative_to(dest)}")
            if not args.dry_run:
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(src, target)
            copied += 1

    print(f"{copied} {'to copy' if args.dry_run else 'copied'}, {skipped} skipped")


if __name__ == "__main__":
    main()
