#!/usr/bin/env python3
"""Deterministic passive warehouse kit for the isolated CombatTest scene.

Fixed Unity metres; runtime owns seed placement, collision components and light.
The shared interior kit supplies chamfered solids and real window/door reveals.
Every constituent solid is checked before and after the Unity/Blender axis swap;
published FBX bounds and anchors are measured after an independent import.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import bar_parts as bp
import interior_kit as interior

# Reuse the established passive metric exporter, not a second export convention.
spec = importlib.util.spec_from_file_location("warehouse_passive_export", ROOT / "tools/build-city-fair-3d-model.py")
passive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(passive)
VERSION = "1.0.0"
MODEL_DIR = ROOT / "Assets/Resources/CombatWarehouse"
SOURCE_DIR = ROOT / "ArtSource/CombatWarehouse"
SURFACES = {
    "Concrete": (.48, .49, .45), "ConcreteDark": (.30, .33, .30),
    "Steel": (.23, .29, .28), "WornSteel": (.48, .51, .47),
    "Rust": (.34, .23, .17), "Timber": (.38, .30, .22),
    "Cardboard": (.51, .43, .31), "Glass": (.36, .43, .42),
    "Lamp": (.88, .78, .59), "Rubber": (.10, .12, .115),
}
passive.SURFACES = SURFACES
passive.VERSION = VERSION
Item = passive.Item


def duplicate_collision(item, geometry, name):
    item.add("ConcreteDark", geometry, "Collision_" + name)


def box(item, role, p, size, group="Body", chamfer=.012, collision=False):
    geometry = bp.u_box(p, size, min(chamfer, min(size) * .23))
    item.add(role, geometry, group)
    if collision:
        duplicate_collision(item, geometry, group)


def rod(item, role, a, b, radius, group="Body", collision=False, sides=10):
    geometry = passive.rod(a, b, radius, sides)
    item.add(role, geometry, group)
    if collision:
        duplicate_collision(item, geometry, group)


def wall(item, length, position, turn, openings, group):
    # interior_kit is Z-up; the same reflection helper converts it to Unity.
    geometry = bp.to_source(interior.wall_run(length, 6., .30, openings, .012))
    geometry = interior.translated(bp.u_rotated(geometry, (0, turn, 0)), position)
    item.add("Concrete", geometry, group)
    duplicate_collision(item, geometry, group)


def flange(item, center, axis=(1, 0, 0), radius=.15, group="Body"):
    a = tuple(p - v * .025 for p, v in zip(center, axis))
    b = tuple(p + v * .025 for p, v in zip(center, axis))
    rod(item, "WornSteel", a, b, radius, group, sides=12)


def beam(item, a, b, width, depth, group="Structure", collision=False):
    # A rectangular steel beam follows the incline without stairs made of cubes.
    dx, dy, dz = (b[i] - a[i] for i in range(3))
    length = math.sqrt(dx * dx + dy * dy + dz * dz)
    geometry = bp.u_box((0, 0, 0), (width, depth, length), .008)
    pitch = -math.degrees(math.atan2(dy, math.hypot(dx, dz)))
    yaw = math.degrees(math.atan2(dx, dz))
    geometry = interior.translated(bp.u_rotated(geometry, (pitch, yaw, 0)),
                                   tuple((a[i] + b[i]) * .5 for i in range(3)))
    item.add("Steel", geometry, group)
    if collision:
        duplicate_collision(item, geometry, group)


def make_shell():
    item = Item("Shell")
    box(item, "Concrete", (0, -.12, 0), (24.6, .24, 18.6), "Floor", collision=True)
    high = [interior.Opening(x, 2.10, 5.55, 4.55) for x in (-8, -4, 0, 4, 8)]
    wall(item, 24.6, (0, 0, 9.15), 0, high, "BackWall")
    south = [interior.Opening(x, 1.9, 5.55, 4.55) for x in (-9.6, -5.7, 5.7, 9.6)]
    south.append(interior.Opening(0, 4., 3.8))
    wall(item, 24.6, (0, 0, -9.15), 0, south, "LoadingWall")
    for sign in (-1, 1):
        wall(item, 18., (sign * 12.15, 0, 0), 90, [], "SideWall" + str(sign))
        for z in (-6, 0, 6):
            # Flanged steel stanchions carry the ceiling joists; ends bear on shoes.
            box(item, "Steel", (sign * 11.86, 2.90, z), (.18, 5.80, .22), "Structure")
            box(item, "Steel", (sign * 11.86, .035, z), (.30, .07, .36), "Structure")
            box(item, "WornSteel", (sign * 11.87, .73, z), (.20, .12, .25), "Structure")
        box(item, "ConcreteDark", (sign * 11.985, .34, 0), (.03, .68, 18), "WallProtection")
    for z in (-6, 0, 6):
        box(item, "Steel", (0, 5.82, z), (23.8, .22, .10), "Joists")
        box(item, "Steel", (0, 5.945, z), (23.8, .055, .34), "Joists")
        box(item, "Steel", (0, 5.695, z), (23.8, .055, .34), "Joists")
    box(item, "ConcreteDark", (0, 6.09, 0), (24.6, .18, 18.6), "Roof", collision=True)
    for x in (-10, -5, 0, 5, 10):
        box(item, "Steel", (x, 5.98, 0), (.10, .06, 18), "CeilingRuns")
    for z, centers in ((9.15, (-8, -4, 0, 4, 8)), (-9.15, (-9.6, -5.7, 5.7, 9.6))):
        width = 2.1 if z > 0 else 1.9
        for x in centers:
            box(item, "Glass", (x, 5.05, z), (width - .12, .89, .035), "HighWindow", collision=True)
            for dx in (-width * .5 + .03, 0, width * .5 - .03):
                box(item, "Steel", (x + dx, 5.05, z - math.copysign(.04, z)), (.055, 1.03, .085), "WindowFrame")
            for y in (4.55, 5.55):
                box(item, "Steel", (x, y, z - math.copysign(.04, z)), (width + .07, .075, .11), "WindowFrame")
    # Closed corrugated freight door occupies the actual wall opening.
    box(item, "Steel", (0, 1.90, -9.13), (3.94, 3.80, .085), "LoadingDoor", collision=True)
    for index in range(18):
        box(item, "WornSteel", (0, .16 + index * .205, -9.064), (3.84, .022, .022), "DoorFolds", .004)
    for x in (-2.08, 2.08):
        box(item, "Steel", (x, 1.92, -9.03), (.12, 3.84, .14), "DoorGuide")
    rod(item, "Steel", (-2.10, 3.96, -9.03), (2.10, 3.96, -9.03), .19, "DoorRoll")
    box(item, "WornSteel", (.65, 1.15, -9.045), (.32, .065, .06), "DoorHandle")
    # One maintenance patch, rubbed lower guards and sparse floor joints show use.
    for x in range(-9, 12, 3):
        box(item, "ConcreteDark", (x, .002, 0), (.013, .003, 17.7), "FloorJoints", .001)
    for z in range(-6, 9, 3):
        box(item, "ConcreteDark", (0, .002, z), (23.7, .003, .013), "FloorJoints", .001)
    for x, z, w, d in ((-8.7, -5.2, 1.8, .45), (7.6, 4.2, .8, 1.4), (1.3, -6.7, .7, .4)):
        box(item, "ConcreteDark", (x, .004, z), (w, .004, d), "FloorWear", .001)
    for x in (-6, 6):
        box(item, "Steel", (x, .007, -7.7), (.8, .008, .28), "FlushDrain", .001)
        for index in range(7):
            box(item, "ConcreteDark", (x - .30 + index * .10, .012, -7.7), (.028, .003, .22), "DrainSlots", .0005)
    item.anchor("FloorCenter", (0, 0, 0))
    item.anchor("CeilingCenter", (0, 6., 0))
    return item


def make_platform():
    item = Item("Platform")
    box(item, "Steel", (0, -.09, 0), (3, .18, 3), "Deck", collision=True)
    for x in (-1.34, 1.34):
        for z in (-1.34, 1.34):
            box(item, "Steel", (x, -1.39, z), (.14, 2.42, .14), "Posts", collision=True)
            box(item, "WornSteel", (x, -2.58, z), (.26, .04, .26), "Shoes", collision=True)
            for dx in (-.085, .085):
                for dz in (-.085, .085):
                    rod(item, "WornSteel", (x + dx, -2.56, z + dz), (x + dx, -2.53, z + dz), .016, "Bolts", sides=6)
    for x in (-1.34, 0, 1.34):
        box(item, "Steel", (x, -.25, 0), (.14, .14, 2.84), "DeckBearers")
    for index in range(12):
        box(item, "WornSteel", (-1.38 + index * .25, .003, 0), (.014, .004, 2.88), "DeckSeams", .001)
    item.anchor("DeckCenter", (0, 0, 0))
    return item


def make_stair():
    item = Item("Stair")
    for index in range(16):
        top = (index + 1) * .1625
        z = (index + .5) * .30
        box(item, "Steel", (0, top - .035, z), (2, .07, .30), "Treads", collision=True)
        box(item, "WornSteel", (0, top - .010, z - .14), (2, .035, .022), "TreadNosing", .004)
        for x in (-.85, -.45, 0, .45, .85):
            box(item, "ConcreteDark", (x, top + .001, z), (.025, .002, .19), "TreadGrip", .0004)
    for x in (-.92, .92):
        beam(item, (x, .035, .04), (x, 2.47, 4.77), .11, .22, "Stringers", collision=True)
        box(item, "Steel", (x, .02, .08), (.25, .04, .30), "FootShoes", collision=True)
    for x in (-1.045, 1.045):
        for index in (0, 4, 8, 12, 15):
            z = (index + .5) * .30
            top = (index + 1) * .1625
            rod(item, "Steel", (x, top, z), (x, top + .98, z), .035, "Guard", collision=True)
        for height in (.47, .98):
            rod(item, "Steel", (x, .1625 + height, .15), (x, 2.6 + height, 4.65), .035, "Guard", collision=True)
    item.anchor("Foot", (0, 0, 0))
    item.anchor("Top", (0, 2.6, 4.8))
    return item


def make_rail():
    item = Item("Rail")
    for x in (-.435, .435):
        box(item, "Steel", (x, .51, 0), (.065, 1.02, .065), "Posts", collision=True)
        box(item, "WornSteel", (x, .018, 0), (.13, .036, .13), "Shoes")
    for y in (.52, 1.015):
        rod(item, "Steel", (-.5, y, 0), (.5, y, 0), .035, "Bars", collision=True)
    box(item, "Steel", (0, .08, 0), (1, .13, .025), "ToeGuard", collision=True)
    return item


def cardboard(item, p, size, group="Load"):
    box(item, "Cardboard", p, size, group, .02)
    x, y, z = p
    w, h, d = size
    box(item, "Timber", (x, y + h * .5 + .001, z), (.035, .003, d - .015), "CartonSeams", .0005)
    for sign in (-1, 1):
        box(item, "ConcreteDark", (x, y + .08, z + sign * (d * .5 + .001)), (.20, .05, .003), "CartonHandholds", .0005)


def make_rack():
    item = Item("Rack")
    for x in (-1.405, 1.405):
        for z in (-.505, .505):
            box(item, "Steel", (x, 1.10, z), (.085, 2.20, .085), "Posts")
            box(item, "WornSteel", (x, .025, z), (.19, .05, .19), "Shoes")
    for y in (.18, 1.03, 1.93):
        box(item, "Timber", (0, y, 0), (2.94, .075, 1.13), "Shelves")
        for z in (-.56, .56):
            box(item, "Steel", (0, y - .05, z), (2.94, .12, .045), "ShelfLips")
    rod(item, "Steel", (-1.44, .15, -.55), (1.44, 2.12, -.55), .023, "BackBrace")
    rod(item, "Steel", (1.44, .15, -.55), (-1.44, 2.12, -.55), .023, "BackBrace")
    for x, y, z in ((-.95, .51, -.06), (.1, .51, .05), (.95, 1.33, -.02), (-.5, 1.33, .07)):
        cardboard(item, (x, y, z), (.75, .56, .85))
    duplicate_collision(item, bp.u_box((0, 1.1, 0), (3, 2.2, 1.2), .005), "Rack")
    return item


def make_cargo():
    item = Item("Cargo")
    for x in (-.83, 0, .83):
        box(item, "Timber", (x, .06, 0), (.14, .12, 1.4), "PalletSkids")
    for index in range(7):
        box(item, "Timber", (-.865 + index * (1.73 / 6), .145, 0), (.27, .07, 1.4), "PalletSlats")
    for x in (-.47, .47):
        for z in (-.34, .34):
            cardboard(item, (x, .705, z), (.91, 1.05, .65))
    for x in (-.62, .62):
        box(item, "Rubber", (x, 1.237, 0), (.035, .026, 1.30), "LoadStraps", .004)
        for z in (-.67, .67):
            box(item, "Rubber", (x, .71, z), (.035, 1.07, .015), "LoadStraps", .003)
    duplicate_collision(item, bp.u_box((0, .625, 0), (2, 1.25, 1.4), .01), "Cargo")
    return item


def make_pipe_straight():
    item = Item("PipeStraight")
    rod(item, "Steel", (-1.5, 0, 0), (1.5, 0, 0), .10)
    for x in (-1.47, 1.47):
        flange(item, (x, 0, 0))
    item.anchor("Start", (-1.5, 0, 0))
    item.anchor("End", (1.5, 0, 0))
    return item


def make_pipe_elbow():
    item = Item("PipeElbow")
    previous = None
    for index in range(13):
        angle = -math.pi * .5 + index * math.pi / 24
        p = (.4 * math.cos(angle), 0, .4 + .4 * math.sin(angle))
        if previous is not None:
            rod(item, "Steel", previous, p, .10)
        previous = p
    item.anchor("Start", (0, 0, 0))
    item.anchor("End", (.4, 0, .4))
    return item


def technical_end(item, p, group="Equipment"):
    x, y, z = p
    box(item, "Steel", (x, y, z), (.55, .70, .34), group)
    box(item, "WornSteel", (x, y, z + .18), (.46, .59, .035), group)
    for yy in (-.14, -.07, 0, .07, .14):
        box(item, "ConcreteDark", (x, y + yy, z + .203), (.28, .017, .015), "VentSlots", .003)
    rod(item, "Steel", (x, y + .35, z), (x, y + .60, z), .075, group)
    flange(item, (x, y + .45, z), (0, 1, 0), .115, group)
    item.add("Rust", interior.translated(bp.u_rotated(passive.ring([(-.017, .13), (.017, .13),
        (.017, .108), (-.017, .108)], 12), (90, 0, 0)), (x, y + .52, z + .15)), "ValveWheel")
    rod(item, "Steel", (x, y + .52, z), (x, y + .52, z + .16), .018, group)


def make_pipe_end():
    item = Item("PipeEnd")
    technical_end(item, (0, .35, .17))
    item.anchor("Connection", (0, .95, .17))
    return item


def make_pipes():
    item = Item("Pipes")
    for y, radius in ((4.90, .10), (4.54, .065)):
        rod(item, "Steel", (-4.8, y, .20), (4.8, y, .20), radius)
        for x in (-4.7, -.10, 4.7):
            flange(item, (x, y, .20), radius=radius + .035)
    rod(item, "Steel", (-4.8, 1.28, .20), (-4.8, 4.90, .20), .10)
    rod(item, "Steel", (4.8, .56, .20), (4.8, 4.54, .20), .065)
    technical_end(item, (-4.8, .68, .20))
    for x in (-4.8, -2.4, 0, 2.4, 4.8):
        box(item, "WornSteel", (x, 4.72, .04), (.10, .72, .065), "WallBrackets")
        for y in (4.54, 4.90):
            box(item, "Steel", (x, y, .13), (.09, .065, .20), "PipeSupports")
    for y in (1.6, 2.8, 4.1):
        box(item, "WornSteel", (-4.8, y, .055), (.24, .08, .08), "WallBrackets")
    box(item, "WornSteel", (4.8, .52, .20), (.27, .12, .27), "DrainConnection")
    item.anchor("Supply", (-4.8, 4.9, .20))
    return item


def make_lamp():
    item = Item("Lamp")
    for x in (-.45, .45):
        box(item, "Steel", (x, -.012, 0), (.15, .024, .16), "Mounts")
        rod(item, "Steel", (x, -.024, 0), (x, -.17, 0), .014, "Hangers")
    box(item, "Steel", (0, -.235, 0), (1.20, .13, .35), "Housing", .025)
    box(item, "Lamp", (0, -.326, 0), (1.04, .043, .25), "Lens", .01)
    for z in (-.16, .16):
        box(item, "WornSteel", (0, -.31, z), (1.18, .065, .035), "GuardRim", .005)
    for x in (-.40, 0, .40):
        rod(item, "Steel", (x, -.347, -.15), (x, -.347, .15), .006, "LensGuard", sides=6)
    item.anchor("Light", (0, -.37, 0))
    return item


def make_items():
    return [make_shell(), make_platform(), make_stair(), make_rail(), make_rack(),
            make_cargo(), make_pipes(), make_lamp(), make_pipe_straight(),
            make_pipe_elbow(), make_pipe_end()]


def manifest(items, checksum):
    payload = passive.manifest(items, checksum)
    payload["generator"] = "tools/build-combat-warehouse-3d-model.py"
    payload["design_id"] = "combat_warehouse_modular_v1"
    payload["contract"] = {
        "shell_inner_dimensions": [24, 6, 18], "deck_tile_size": [3, .18, 3],
        "deck_top_local_y": 0, "platform_height": 2.6,
        "platform_posts_xz": [-1.34, 1.34], "platform_post_width": .14,
        "platform_shoe_width": .26,
        "stair_width": 2, "stair_run": 4.8, "stair_rise": 2.6,
        "stair_treads": 16, "stair_tread_depth": .3, "stair_tread_rise": .1625,
        "rail_length": 1, "rack_footprint": [3, 1.2], "cargo_footprint": [2, 1.4],
        "light_anchor": [0, -.37, 0], "collision_mesh_prefix": "Collision_",
    }
    if any(record["triangle_count"] <= 0 for record in payload["models"]):
        raise RuntimeError("Warehouse module lost measurable geometry")
    return payload


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    items = make_items()
    checksum = passive.signature(items)
    if passive.signature(make_items()) != checksum:
        raise RuntimeError("Warehouse repeated geometry differs")
    payload = manifest(items, checksum)
    if args.validate_only:
        current = json.loads((args.model_dir / "CombatWarehouse3D.json").read_text(encoding="utf8"))
        if current != payload:
            raise RuntimeError("Published warehouse manifest differs from deterministic geometry")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        # Blender object names are global. Export each module in a fresh scene
        # so common names such as Posts__Steel and Start never gain .001 suffixes.
        for item in items:
            root = passive.build_objects([item])[0]
            passive.export(root, args.model_dir / (root.name + ".fbx"))
        (args.model_dir / "CombatWarehouse3D.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        passive.build_objects(items)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CombatWarehouse3D.blend"), check_existing=False)
    passive.verify_fbx(args.model_dir, payload)
    print("COMBAT WAREHOUSE ART CONTRACT OK " + json.dumps({"models": len(items),
          "triangles": payload["triangle_count"], "signature": checksum}), flush=True)


if __name__ == "__main__":
    main()
