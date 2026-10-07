"""Keep new Core Keeper characters and worlds from being lost under CrossOver.

Under Wine the game can't save to a file that doesn't exist yet, so a new
character or world never reaches disk (see README). This script follows
Player.log while you play. For each failed write to a file that is missing,
it creates that file and its .pugbackup empty. The game's next save (about a
minute later, or Save and Exit) then fills them in.

Safety rules:
- Acts only after the log shows "PugDatabase initialized", which comes after
  the game has loaded its saves. Failures before that (the startup cloud
  download) are only reported: an empty file there could be loaded as a
  blank save. Use restore_from_cloud_cache.py for those.
- Acts only while CoreKeeper.exe is running.
- Only touches paths inside the save folder, never cloudconflicts/, and
  never a file that already exists.

Leave it running in a terminal for the whole play session.

Usage:
  python3 fix_missing_saves.py [--bottle NAME] [--steam-id ID]
"""
import argparse
import os
import re
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path, PureWindowsPath

BOTTLES = Path.home() / "Library/Application Support/CrossOver/Bottles"
FAILED = re.compile(r"^Write failed: .*? : '(?P<path>[^']+)'")
READY = "PugDatabase initialized"
GAME_PROCESS = r"^[A-Za-z]:\\.*\\CoreKeeper\.exe( |$)"
POLL_SECONDS = 0.5


def say(msg):
    print(f"{datetime.now():%H:%M:%S} {msg}", flush=True)


def game_running():
    # Match only the Wine game process (its command line starts with the Windows
    # path), not any process that merely mentions CoreKeeper.exe in its arguments.
    result = subprocess.run(["pgrep", "-f", GAME_PROCESS], capture_output=True)
    return result.returncode == 0


def find_steam_id(parent):
    """Return the only numeric folder name in parent (the Steam account ID), or exit."""
    ids = sorted(p.name for p in parent.iterdir() if p.is_dir() and p.name.isdigit()) if parent.is_dir() else []
    if len(ids) == 1:
        return ids[0]
    if not ids:
        sys.exit(f"No Steam ID folder in {parent}. Has the game been launched in this bottle?")
    sys.exit(f"More than one Steam ID folder in {parent} ({', '.join(ids)}). Pick one with --steam-id.")


def to_mac_path(drive_c, windows_path):
    parts = PureWindowsPath(windows_path).parts
    if not parts or parts[0].upper() != "C:\\" or any(p in (".", "..") for p in parts[1:]):
        return None
    return Path(os.path.normpath(drive_c.joinpath(*parts[1:])))


def handle_failure(windows_path, drive_c, save_root, ready, assume_running):
    path = to_mac_path(drive_c, windows_path)
    if path is None or path == save_root or not path.is_relative_to(save_root):
        say(f"ignored (outside save folder): {windows_path}")
        return
    rel = path.relative_to(save_root)
    if rel.parts[0] == "cloudconflicts":
        return
    target = path.with_name(path.name.removesuffix(".pugbackup"))
    if target.exists():
        return
    rel_target = target.relative_to(save_root)
    if not ready:
        say(f"WARNING: {rel_target} failed during startup; not touched. "
            f"If it should come from Steam Cloud, quit and run restore_from_cloud_cache.py.")
        return
    if not (assume_running or game_running()):
        say(f"game not running; left {rel_target} alone")
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    for p in (target, target.with_name(target.name + ".pugbackup")):
        if not p.exists():
            p.touch()
    say(f"created empty {rel_target} (+ .pugbackup); the next save fills it")


def follow(log_path):
    """Yield (line, is_new_log) forever, starting over when the game rotates the log.

    Reads bytes, not text: the log has Windows line endings, and text-mode
    tell() then returns an opaque cookie instead of a byte offset.
    """
    handle, inode = None, None
    while True:
        try:
            st = os.stat(log_path)
        except FileNotFoundError:
            time.sleep(POLL_SECONDS)
            continue
        if handle is None or st.st_ino != inode or st.st_size < handle.tell():
            if handle:
                handle.close()
            handle = open(log_path, "rb")
            inode = st.st_ino
            yield None, True
        line = handle.readline()
        if line.endswith(b"\n"):
            yield line.decode("utf-8", errors="replace").rstrip("\r\n"), False
        else:
            # Partial line still being written: rewind and wait for the rest.
            handle.seek(-len(line), os.SEEK_CUR)
            time.sleep(POLL_SECONDS)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bottle", default="Steam")
    parser.add_argument("--steam-id", help="Steam account ID folder (found automatically if there is only one)")
    parser.add_argument("--drive-c", type=Path, help="override the bottle's drive_c folder (for testing)")
    parser.add_argument("--assume-running", action="store_true", help="skip the game process check (for testing)")
    args = parser.parse_args()

    drive_c = args.drive_c or BOTTLES / args.bottle / "drive_c"
    game_dir = drive_c / "users/crossover/AppData/LocalLow/Pugstorm/Core Keeper"
    save_root = game_dir / "Steam" / (args.steam_id or find_steam_id(game_dir / "Steam"))
    if not save_root.is_dir():
        sys.exit(f"No save folder at {save_root}")

    say(f"watching {game_dir / 'Player.log'} (Ctrl+C to stop)")
    ready = False
    for line, new_log in follow(game_dir / "Player.log"):
        if new_log:
            ready = False
            say("reading new game log")
            continue
        if READY in line and not ready:
            ready = True
            say("game finished loading saves; fixing missing files from now on")
            continue
        match = FAILED.match(line)
        if match:
            handle_failure(match["path"], drive_c, save_root, ready, args.assume_running)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
