"""Poll a folder tree and log every file create, delete and size/mtime change.

Usage: python3 watch_saves.py <root> <logfile>
"""
import os
import sys
import time
from datetime import datetime

POLL_SECONDS = 0.1


def snapshot(root):
    result = {}
    for dirpath, dirnames, filenames in os.walk(root):
        for name in dirnames + filenames:
            path = os.path.join(dirpath, name)
            try:
                st = os.stat(path)
            except FileNotFoundError:
                continue
            kind = "dir" if name in dirnames else "file"
            result[os.path.relpath(path, root)] = (kind, st.st_size, st.st_mtime_ns)
    return result


def main():
    root, logfile = sys.argv[1], sys.argv[2]
    with open(logfile, "a", buffering=1) as log:
        def emit(msg):
            log.write(f"{datetime.now().strftime('%H:%M:%S.%f')[:-3]} {msg}\n")

        emit(f"watching {root}")
        prev = {}
        while True:
            cur = snapshot(root) if os.path.isdir(root) else {}
            for path in sorted(cur.keys() - prev.keys()):
                kind, size, _ = cur[path]
                emit(f"CREATE {kind} {path} ({size} bytes)")
            for path in sorted(prev.keys() - cur.keys()):
                emit(f"DELETE {prev[path][0]} {path}")
            for path in sorted(cur.keys() & prev.keys()):
                if cur[path][0] == "file" and cur[path] != prev[path]:
                    emit(f"CHANGE {path} ({prev[path][1]} -> {cur[path][1]} bytes)")
            prev = cur
            time.sleep(POLL_SECONDS)


if __name__ == "__main__":
    main()
