#!/usr/bin/env -S uv run --quiet --script
# /// script
# requires-python = ">=3.14"
# dependencies = ["dnfile"]
# ///
"""Show what a BuildingBlueprint mod blueprint holds: a map and the items one paste needs.

Blueprints store objects and tiles as numbers. This script reads the names from the
game's own Pug.Base.dll (read only; no game code runs).

Usage:
    uv run tools/blueprint_report.py <blueprint.json> [--managed <CoreKeeper_Data/Managed>]
"""

import argparse
import json
import string
import sys
from collections import Counter
from pathlib import Path

import dnfile

DEFAULT_MANAGED = (
    Path.home() / ".local/share/Steam/steamapps/common/Core Keeper/CoreKeeper_Data/Managed"
)

# Tiles that paste without using up an item of their own (from the mod's BuildingPlaceServer).
TILE_NOTES = {
    "dugUpGround": "needs any hoe in your inventory; the hoe is not used up",
    "water": "uses a filled bucket",
    "roofHole": "uses a Roofing Tool",
    "ground": "uses a dirt (or matching) wall block",
}


def read_enum(dll: Path, type_name: str) -> dict[int, str]:
    """Return {value: name} for an enum type in a .NET DLL."""
    md = dnfile.dnPE(str(dll)).net.mdtables
    consts = {}
    for c in md.Constant:
        parent = c.Parent
        if parent.table and parent.table.name == "Field":
            consts[parent.row_index] = int.from_bytes(c.Value.value, "little", signed=True)
    for t in md.TypeDef:
        if str(t.TypeName) == type_name:
            return {consts[f.row_index]: str(f.row.Name) for f in t.FieldList if f.row_index in consts}
    sys.exit(f"{type_name} not found in {dll}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("blueprint", type=Path)
    parser.add_argument("--managed", type=Path, default=DEFAULT_MANAGED,
                        help="the game's CoreKeeper_Data/Managed folder")
    args = parser.parse_args()

    base = args.managed / "Pug.Base.dll"
    object_names = read_enum(base, "ObjectID")
    tile_names = read_enum(base, "TileType")
    bp = json.loads(args.blueprint.read_text(encoding="utf-8-sig"))

    symbols = iter(string.ascii_uppercase + string.digits + "#$%&*+=@")
    tile_symbols = iter(string.ascii_lowercase)
    legend: dict[str, str] = {}
    obj_symbol: dict[int, str] = {}
    tile_symbol: dict[int, str] = {}
    cells: dict[tuple[int, int], str] = {}
    objects: Counter[str] = Counter()
    tiles: Counter[tuple[str, int]] = Counter()

    for info in bp.get("EntityInfos", []):
        x, y = info["Position"]
        for e in info["Entities"]:
            oid = e["ObjectID"]
            name = object_names.get(oid, f"unknown object {oid}")
            if oid not in obj_symbol:
                obj_symbol[oid] = next(symbols, "?")
                legend[obj_symbol[oid]] = name
            cells[(x, y)] = cells.get((x, y), "") + obj_symbol[oid]
            objects[name] += 1  # variations (often which way it faces) share one item
    for info in bp.get("TileInfos", []):
        x, y = info["Position"]
        for t in info["Tiles"]:
            tid = t["tileType"]
            name = tile_names.get(tid, f"unknown tile {tid}")
            if tid not in tile_symbol:
                tile_symbol[tid] = next(tile_symbols, "?")
                legend[tile_symbol[tid]] = f"{name} (tile)"
            cells[(x, y)] = cells.get((x, y), "") + tile_symbol[tid]
            tiles[(name, t.get("tileset", 0))] += 1

    print(f"Blueprint: {bp.get('Name')}  {bp.get('Description') or ''}".rstrip())
    if not cells:
        print("Empty blueprint.")
        return
    xs = [p[0] for p in cells]
    ys = [p[1] for p in cells]
    print(f"Footprint: {max(xs) - min(xs) + 1} x {max(ys) - min(ys) + 1} tiles\n")
    width = max(2, max(len(v) for v in cells.values()))
    for y in range(max(ys), min(ys) - 1, -1):
        row = " ".join(cells.get((x, y), ".").ljust(width) for x in range(min(xs), max(xs) + 1))
        print(f"{y:>3} {row}")
    print("    " + " ".join(str(x).ljust(width) for x in range(min(xs), max(xs) + 1)))
    print("\nLegend:")
    for sym, name in legend.items():
        print(f"  {sym}  {name}")

    print("\nObjects to carry (one used per object):")
    for name, n in sorted(objects.items(), key=lambda kv: (-kv[1], kv[0])):
        print(f"  {n:>4}  {name}")
    if tiles:
        print("\nTiles (most use one matching block or floor item each):")
        for (name, tileset), n in sorted(tiles.items(), key=lambda kv: (-kv[1], kv[0])):
            note = TILE_NOTES.get(name, "")
            print(f"  {n:>4}  {name} (tileset {tileset})" + (f" - {note}" if note else ""))
    print("\nMissing items are skipped without a warning. Planted crops cannot be pasted"
          " outside creative God Mode, because the mod looks for the plant object, not the seed.")


if __name__ == "__main__":
    main()
