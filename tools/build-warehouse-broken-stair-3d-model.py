#!/usr/bin/env python3
"""Author the warehouse's broken upper stair without changing the shared world atlas."""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import bar_parts as bp
import interior_kit as kit

spec = importlib.util.spec_from_file_location("broken_stair_passive_export", ROOT / "tools/build-city-fair-3d-model.py")
passive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(passive)
passive.VERSION = "1.0.0"
passive.SURFACES = {"Steel": (.20, .24, .22), "WornSteel": (.36, .39, .32),
                    "Rust": (.39, .23, .13), "ConcreteDark": (.20, .23, .21)}
MODEL_DIR = ROOT / "Assets/Resources/CombatWarehouse"
SOURCE_DIR = ROOT / "ArtSource/CombatWarehouse/BrokenStair"
RETAINED_TREADS = (*range(6), *range(16, 20))


def solid(item, role, geometry, group, collision=True):
    item.add(role, geometry, group)
    if collision:
        item.add("ConcreteDark", geometry, "Collision_" + group)


def box(item, role, position, size, group, collision=True, rotation=(0, 0, 0)):
    geometry = bp.u_box((0, 0, 0), size, min(.008, min(size) * .2))
    geometry = kit.translated(bp.u_rotated(geometry, rotation), position)
    solid(item, role, geometry, group, collision)


def rod(item, role, a, b, radius, group, collision=True):
    solid(item, role, passive.rod(a, b, radius, 8), group, collision)


def beam(item, a, b, width=.11, depth=.22, group="Stringers", role="Steel"):
    dx, dy, dz = (b[i] - a[i] for i in range(3))
    length = math.sqrt(dx * dx + dy * dy + dz * dz)
    pitch = -math.degrees(math.atan2(dy, math.hypot(dx, dz)))
    yaw = math.degrees(math.atan2(dx, dz))
    box(item, role, tuple((a[i] + b[i]) * .5 for i in range(3)),
        (width, depth, length), group, rotation=(pitch, yaw, 0))


def torn_lip(item, x, y, z, upper):
    # Unequal solid steel teeth expose an old torn edge on each snapped stringer.
    # Short tips hang into the void; they never reconnect the severed flight.
    direction = -1 if upper else 1
    for tooth, (offset, length) in enumerate(((-.036, .09), (0, .16), (.036, .065))):
        start = (x + offset, y + .02 * tooth, z)
        end = (x + offset + .012 * direction, y - .10 - .018 * tooth, z + direction * length)
        beam(item, start, end, .025, .045, "Fracture", "Rust")


def cross_guard(item, z, floor, group):
    # Authored, fastened cross-rails close the surviving mouths on both levels.
    # Collision follows these visible solids, not an invisible replacement ramp.
    for x in (-1.245, 0, 1.245):
        rod(item, "Steel", (x, floor, z), (x, floor + 1.1, z), .035, group)
        box(item, "Rust", (x, floor + .025, z), (.15, .05, .16), group)
    for height in (.52, 1.08):
        rod(item, "WornSteel", (-1.245, floor + height, z),
            (1.245, floor + height, z), .035, group)
    box(item, "Steel", (0, floor + .08, z), (2.49, .13, .045), group)
    # Diagonal bracing and worn clamps make the old closure legible from below.
    beam(item, (-1.20, floor + .14, z), (1.20, floor + .89, z),
         .075, .045, group, "Rust")
    for x in (-1.18, 1.18):
        box(item, "Rust", (x, floor + .89, z - .024), (.16, .12, .024), group, False)
    # The narrow authored guard panel agrees with routing's closure bounds and
    # prevents a crouched capsule squeezing through the visible cross-rails.
    item.add("ConcreteDark", bp.u_box((0, floor + .55, z), (2.56, 1.1, .09), .008),
             "Collision_" + group + "Panel")


def make_stair():
    item = passive.Item("BrokenStair")
    for index in RETAINED_TREADS:
        top, z = (index + 1) * .16, (index + .5) * .30
        rotation = (16, 0, -2) if index == 5 else (-13, 0, 2) if index == 16 else (0, 0, 0)
        sag = -.055 if index in (5, 16) else 0
        position = (0, top - .035 + sag, z)
        box(item, "Steel", position, (2.4, .07, .30), "Treads", rotation=rotation)
        nosing = kit.translated(bp.u_rotated(bp.u_box((0, .025, -.14), (2.4, .035, .022), .004), rotation), position)
        solid(item, "Rust", nosing, "TreadNosing", False)
        for x in (-1.05, -.52, 0, .52, 1.05):
            grip = kit.translated(bp.u_rotated(bp.u_box((x, .036, 0), (.025, .002, .19), .0004), rotation), position)
            solid(item, "ConcreteDark", grip, "TreadGrip", False)
    for x in (-1.12, 1.12):
        beam(item, (x, .035, .04), (x, .85, 1.67))
        beam(item, (x, .85, 1.67), (x + .025, .43, 2.03), group="BentStringers")
        torn_lip(item, x + .025, .43, 2.03, False)
        beam(item, (x, 2.51, 4.91), (x, 3.07, 5.97))
        beam(item, (x - .025, 2.27, 4.72), (x, 2.51, 4.91), group="BentStringers")
        torn_lip(item, x - .025, 2.27, 4.72, True)
        box(item, "Steel", (x, .02, .08), (.25, .04, .30), "FootShoes")
    for side in (-1, 1):
        x = side * 1.245
        for index in (0, 4, 16, 19):
            z, top = (index + .5) * .30, (index + 1) * .16
            rod(item, "Steel", (x, top, z), (x, top + .98, z), .035, "SideGuard")
        for height in (.47, .98):
            rod(item, "Steel", (x, .16 + height, .15), (x, .96 + height, 1.65), .035, "SideGuard")
            rod(item, "Steel", (x, 2.72 + height, 4.95), (x, 3.2 + height, 5.85), .035, "SideGuard")
            rod(item, "Rust", (x, .96 + height, 1.65),
                (x + side * .10, .63 + height, 1.92), .033, "BrokenGuard")
            rod(item, "Rust", (x - side * .07, 2.45 + height, 4.71),
                (x, 2.72 + height, 4.95), .033, "BrokenGuard")
    cross_guard(item, 1.65, .96, "LowerClosure")
    cross_guard(item, 6., 3.2, "UpperClosure")
    item.anchor("Foot", (0, 0, 0))
    item.anchor("Top", (0, 3.2, 6.))
    item.anchor("LowerClosure", (0, .96, 1.65))
    item.anchor("UpperClosure", (0, 3.2, 6.))
    return item


def payload_for(item):
    payload = passive.manifest([item], passive.signature([item]))
    payload["generator"] = "tools/build-warehouse-broken-stair-3d-model.py"
    payload["contract"] = {"flight_dimensions": [2.4, 3.2, 6.0],
        "retained_tread_indices": list(RETAINED_TREADS), "missing_tread_indices": list(range(6, 16)),
        "missing_tread_interval_z": [1.8, 4.8], "clear_void_interval_z": [2.25, 4.5],
        "closures": [{"center": [0, 1.51, 1.65], "size": [2.56, 1.1, .09]},
                     {"center": [0, 3.75, 6.0], "size": [2.56, 1.1, .09]}],
        "materials": "shared warehouse auxiliary atlas; no new painting or master-atlas repack",
        "collision": "authored retained treads, severed stringers, side rails and cross-closures; no ramp"}
    return payload


def validate_stair(item):
    for (group, _), solids in item.parts.items():
        for vertices, _ in solids:
            low = min(vertex[2] for vertex in vertices)
            high = max(vertex[2] for vertex in vertices)
            if high > 2.25 and low < 4.5:
                raise RuntimeError("Broken stair has a solid bridging its clear void: " + group)
    if passive.signature([make_stair()]) != passive.signature([item]):
        raise RuntimeError("Repeated broken stair geometry differs")


def preview(root, path):
    path = path if path.is_absolute() else ROOT / path
    for child in root.children_recursive:
        if child.name.startswith("Collision_"):
            child.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new("Stair Preview World")
    scene.world.color = (.09, .09, .09)
    for name, position, energy in (("Key", (-4, -1, 8), 2100), ("Fill", (4, 6, 5), 1300)):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.shape, data.size = energy, "DISK", 6
        light = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(light)
        light.location = position
        light.rotation_euler = (Vector((0, 3, 1.8)) - light.location).to_track_quat("-Z", "Y").to_euler()
    data = bpy.data.cameras.new("Stair Preview")
    camera = bpy.data.objects.new("Stair Preview", data)
    bpy.context.collection.objects.link(camera)
    camera.location = (7, -7, 6)
    camera.rotation_euler = (Vector((0, 3, 1.9)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    data.type, data.ortho_scale = "ORTHO", 9
    scene.camera = camera
    path.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(path)
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--preview", type=Path)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    item = make_stair()
    validate_stair(item)
    payload = payload_for(item)
    path = args.model_dir / "WarehouseBrokenStair3D.json"
    if args.validate_only:
        if json.loads(path.read_text(encoding="utf8")) != payload:
            raise RuntimeError("Published broken stair manifest differs from deterministic geometry")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        root = passive.build_objects([item])[0]
        passive.export(root, args.model_dir / "BrokenStair.fbx")
        path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8", newline="\n")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "BrokenStair.blend"), check_existing=False)
        if args.preview:
            preview(root, args.preview)
    passive.verify_fbx(args.model_dir, payload)
    print("WAREHOUSE BROKEN STAIR CONTRACT OK " + payload["build_signature"], flush=True)


if __name__ == "__main__":
    main()
