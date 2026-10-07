"""Run fix_missing_saves.py against a fake bottle and a made-up Player.log.

Usage: python3 test_fix_missing_saves.py
"""
import subprocess
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
SAVE = r"C:\users\crossover\AppData\LocalLow\Pugstorm\Core Keeper\Steam\12345678"


def failed(rel):
    return f"Write failed: Success : '{SAVE}\\{rel}.pugbackup' (-2147024896)\n"


def check_game_detection():
    """A process that only mentions CoreKeeper.exe must not count; a Wine-style one must."""
    sys.path.insert(0, str(HERE))
    import fix_missing_saves as m
    decoy = subprocess.Popen([sys.executable, "-I", "-c", "import time; time.sleep(3)", "notes about CoreKeeper.exe"])
    time.sleep(0.5)
    decoy_counts = m.game_running()
    decoy.kill()
    wine_like = subprocess.Popen(["bash", "-c", "exec -a 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Core Keeper\\CoreKeeper.exe' sleep 3"])
    time.sleep(0.5)
    wine_counts = m.game_running()
    wine_like.kill()
    return {"decoy process not taken for the game": not decoy_counts,
            "Wine-style game process detected": wine_counts}


def main():
    with tempfile.TemporaryDirectory() as tmp:
        drive_c = Path(tmp) / "drive_c"
        game = drive_c / "users/crossover/AppData/LocalLow/Pugstorm/Core Keeper"
        save = game / "Steam/12345678"
        (save / "saves").mkdir(parents=True)
        (save / "saves/0.json").write_text("real data")
        log = game / "Player.log"
        log.write_bytes((
            "CloudSyncDown\n"
            + failed(r"saves\9.json")                # before ready: must not be created
            + failed(r"cloudconflicts\saves\0.json")  # never touched
            + "PugDatabase initialized 3235 datablocks\n"
            + failed(r"saves\0.json")                # exists: left alone
            + failed(r"saves\5.json")                # new character: created
            + failed(r"maps\5\8.mapparts.gzip")      # new folder too: created
            + "Write failed: Success : 'C:\\Windows\\evil.txt.pugbackup' (-2147024896)\n"  # outside: ignored
            + failed(r"..\..\..\..\..\..\..\..\escaped")  # '..' escape: ignored
        ).replace("\n", "\r\n").encode())  # the real log has Windows line endings

        proc = subprocess.Popen(
            [sys.executable, "-I", str(HERE / "fix_missing_saves.py"), "--drive-c", str(drive_c), "--assume-running"],
            stdout=subprocess.PIPE, text=True)
        time.sleep(3)

        # Game restarts: Unity moves the old log aside and starts a new one.
        log.rename(game / "Player-prev.log")
        log.write_text(failed(r"worlds\9.world.gzip"))  # before ready in the new log: not created
        time.sleep(2)
        proc.terminate()
        output = proc.communicate()[0]

        checks = {
            "saves/5.json created empty": (save / "saves/5.json").exists() and (save / "saves/5.json").stat().st_size == 0,
            "saves/5.json.pugbackup created": (save / "saves/5.json.pugbackup").exists(),
            "maps/5/8.mapparts.gzip created": (save / "maps/5/8.mapparts.gzip").exists(),
            "saves/0.json untouched": (save / "saves/0.json").read_text() == "real data",
            "saves/0.json.pugbackup not added": not (save / "saves/0.json.pugbackup").exists(),
            "saves/9.json (startup) not created": not (save / "saves/9.json").exists(),
            "cloudconflicts not created": not (save / "cloudconflicts").exists(),
            "outside path not created": not (drive_c / "Windows").exists(),
            "'..' path not created": not any(Path(tmp).rglob("escaped*")),
            "worlds/9 after restart not created": not (save / "worlds/9.world.gzip").exists(),
            "restart was noticed": output.count("reading new game log") == 2,
        }
        checks.update(check_game_detection())
        print(output)
        for name, ok in checks.items():
            print(f"{'PASS' if ok else 'FAIL'}  {name}")
        sys.exit(0 if all(checks.values()) else 1)


if __name__ == "__main__":
    main()
