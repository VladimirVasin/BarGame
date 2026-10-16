#!/usr/bin/env python3
"""Author the hollow, hinged warehouse locker bank without repacking the world atlas."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("locker_passive_export", ROOT / "tools/build-city-fair-3d-model.py")
passive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(passive)
passive.VERSION = "1.0.0"
passive.SURFACES = {"Steel": (.20, .24, .22), "WornSteel": (.36, .39, .32),
                    "Rust": (.39, .23, .13), "ConcreteDark": (.20, .23, .21)}
MODEL_DIR = ROOT / "Assets/Resources/CombatWarehouse/Lockers"
SOURCE_DIR = ROOT / "ArtSource/CombatWarehouse/Lockers"


def make_locker():
    item = passive.Item("WarehouseLocker")
    # Empty front, real sides/back/shelves: no closed box is hidden behind a door.
    for x in (-1.0825, -.37, .37, 1.0825):
        item.box("Steel", (x, .95, 0), (.035, 1.9, .65), "Cabinet")
    for y in (.0875, 1.8825):
        item.box("Steel", (0, y, 0), (2.2, .035, .65), "Cabinet")
    item.box("Steel", (0, .95, .3075), (2.2, 1.9, .035), "Cabinet")
    for bay in range(3):
        x = (bay - 1) * .74
        pivot = (x - .33, .985, -.3425)
        group = "LockerDoor" + str(bay)
        item.anchor(group, pivot)
        # The collision panel is parented under the same authored hinge.
        collision = "Collision_Door" + str(bay)
        item.anchor(collision, pivot, group)
        item.box("WornSteel", (x, .985, -.3425), (.66, 1.73, .035), group)
        item.box("ConcreteDark", (x, .985, -.3425), (.66, 1.73, .035), collision)
        item.box("Steel", (x, 1.0475, -.0125), (.69, .035, .585), "Cabinet")
        item.box("WornSteel", (x, 1.0475, -.3025), (.69, .055, .035), "ShelfLips")
        item.anchor("Shelf" + str(bay), (x, 1.065, -.08))
        for y in (.29, 1.62):
            for row in range(3):
                item.box("ConcreteDark", (x, y + row * .045, -.364), (.43, .014, .007), group, .001)
                item.box("WornSteel", (x, y + row * .045 + .012, -.370), (.43, .013, .018), group, .001)
        item.box("Steel", (x + .245, .965, -.368), (.075, .23, .018), group)
        item.rod("Rust", (x + .245, .90, -.407), (x + .245, 1.03, -.407), .015, group, sides=8)
        for y in (.40, 1.56):
            item.rod("Rust", (x - .33, y, -.3425), (x - .33, y + .10, -.3425), .022, group, sides=8)
        # Small, varied repairs remain attached to each moving leaf.
        item.box("Rust", (x - .12 + bay * .06, .66 + bay * .12, -.365), (.18, .065, .012), group, .002)
        for dx in (-.27, .27):
            for y in (.20, 1.77):
                item.rod("Steel", (x + dx, y, -.360), (x + dx, y, -.374), .012, group, sides=6)
    # Compound authored cabinet collision preserves the open cavity.
    item.parts[("Collision_Cabinet", "ConcreteDark")] = list(item.parts[("Cabinet", "Steel")])
    item.solids += len(item.parts[("Collision_Cabinet", "ConcreteDark")])
    item.anchor("Approach", (0, 0, -1.45))
    return item


def payload_for(item):
    payload = passive.manifest([item], passive.signature([item]))
    payload["generator"] = "tools/build-warehouse-locker-3d-model.py"
    payload["contract"] = {"bank_dimensions": [2.2, 1.9, .65], "front": "-Z",
        "door_open_angle": 102, "door_count": 3,
        "shelf_contacts": [[(bay - 1) * .74, 1.065, -.08] for bay in range(3)],
        "materials": "shared warehouse auxiliary atlas; no new painting",
        "collision": "hollow shell and separate hinge-parented leaves"}
    return payload


def validate_cavity(item):
    # A line through each open mouth to the shelf must miss all cabinet solids.
    for bay in range(3):
        x = (bay - 1) * .74
        for vertices, _ in item.parts[("Cabinet", "Steel")]:
            low = [min(p[axis] for p in vertices) for axis in range(3)]
            high = [max(p[axis] for p in vertices) for axis in range(3)]
            if low[0] < x < high[0] and low[1] < 1.22 < high[1] and low[2] < .10 and high[2] > -.4:
                raise RuntimeError("Locker mouth is blocked by a cabinet solid")
    if passive.signature([make_locker()]) != passive.signature([item]):
        raise RuntimeError("Repeated locker geometry differs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    item = make_locker()
    validate_cavity(item)
    payload = payload_for(item)
    path = args.model_dir / "WarehouseLocker3D.json"
    if args.validate_only:
        if json.loads(path.read_text(encoding="utf8")) != payload:
            raise RuntimeError("Published locker manifest differs from deterministic geometry")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        root = passive.build_objects([item])[0]
        passive.export(root, args.model_dir / "WarehouseLocker.fbx")
        path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8", newline="\n")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "WarehouseLocker.blend"), check_existing=False)
    passive.verify_fbx(args.model_dir, payload)
    print("WAREHOUSE LOCKER CONTRACT OK " + payload["build_signature"], flush=True)


if __name__ == "__main__":
    main()
