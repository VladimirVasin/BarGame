#!/usr/bin/env python3
"""A small, badly cast municipal concrete pigeon for the shared bench landing.

Passive fixed metres, +Y up/+Z forward. Ordinary worn concrete, faint old
paint and tiny dark painted eye buttons; no text, light, animation or lore.
Runtime owns placement and one measured plinth collision footprint.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
import interior_kit as kit
spec = importlib.util.spec_from_file_location("bench_pigeon_port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)

NAME = "BenchPigeonStatue"
MODEL_DIR = ROOT / "Assets/Resources/City/BenchAccess"
SOURCE_DIR = ROOT / "ArtSource/City/BenchAccess"


def lump(center, radius, rotation=(0, 0, 0), segments=14, rings=8):
    """A closed low-poly hand-shaped solid; the two poles remain single vertices."""
    vertices = [(0, -radius[1], 0)]
    for row in range(1, rings):
        latitude = math.pi * row / rings
        for index in range(segments):
            angle = math.tau * index / segments
            uneven = 1 + .025 * math.sin(index * 3 + row * 2)
            vertices.append((math.cos(angle) * math.sin(latitude) * radius[0] * uneven,
                             -math.cos(latitude) * radius[1],
                             math.sin(angle) * math.sin(latitude) * radius[2] * uneven))
    top = len(vertices)
    vertices.append((0, radius[1], 0))
    faces = []
    for index in range(segments):
        after = (index + 1) % segments
        faces.append((0, 1 + index, 1 + after))
        for row in range(rings - 2):
            low, high = 1 + row * segments, 1 + (row + 1) * segments
            faces.append((low + after, low + index, high + index, high + after))
        last = 1 + (rings - 2) * segments
        faces.append((top, last + after, last + index))
    geometry = kit.translated(bp.u_rotated((vertices, faces), rotation), center)
    assert bp.signed_volume(geometry) > 0, "Pigeon solid winding changed"
    return geometry


def add(geometry, solid, color):
    vertices, faces = solid
    geometry.add(vertices, faces, color)


def build():
    port.base.reset()
    source_material = port.base.material("BenchPigeonSharedSurfaceSource")
    root = port.empty(NAME)
    concrete, paint, eyes = port.Geometry(), port.Geometry(), port.Geometry()
    concrete.role, paint.role, eyes.role = "Concrete", "Plaster", "SteelDark"
    # A plain squat plinth, with no plaque recess or space composed as a memorial.
    for center, size, bevel in (((0, .06, 0), (.86, .12, .86), .025),
                                 ((.015, .265, -.01), (.64, .29, .64), .018),
                                 ((0, .445, 0), (.76, .07, .76), .012)):
        port.chamfer(concrete, center, size, port.CONCRETE, bevel)
    # Small thick feet and short uneven legs support the heavy blunt body.
    for x in (-.085, .085):
        port.chamfer(concrete, (x, .505, .065), (.13, .05, .22), port.CONCRETE, .009)
        concrete.rod((x, .51, .035), (x * .85, .68, -.015), .038, port.CONCRETE, 8)
    add(concrete, lump((0, .93, -.035), (.235, .315, .25), (-5, 0, 2)), port.CONCRETE)
    add(concrete, lump((-.02, .79, -.19), (.18, .08, .225), (-12, 0, 0)), port.CONCRETE)
    add(concrete, lump((.025, 1.17, .105), (.145, .205, .145)), port.CONCRETE)
    # Oversized crooked head and mismatched short wings communicate poor craft,
    # without introducing a special creature or a second game character.
    add(concrete, lump((.035, 1.38, .11), (.23, .27, .19), (0, -7, -8)), port.CONCRETE)
    add(paint, lump((-.242, .99, -.05), (.080, .205, .145), (0, 0, -15)), port.CABIN)
    add(paint, lump((.242, .96, -.015), (.088, .170, .155), (0, 0, 21)), port.CABIN)
    # Broad blunt beak; the old sculptor left an inelegant solid wedge.
    beak = ([(0, 1.325, .267), (.13, 1.33, .267), (.13, 1.415, .267),
             (0, 1.42, .267), (.075, 1.365, .43)],
            [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)])
    add(concrete, beak, port.CONCRETE)
    # Two tiny dark painted bumps, not lights or polished metal inlays.
    add(eyes, lump((-.159, 1.423, .226), (.019, .020, .013), segments=8, rings=4), port.METAL)
    add(eyes, lump((.228, 1.405, .214), (.019, .018, .013), segments=8, rings=4), port.METAL)
    port.obj(concrete, "PigeonConcrete", root, source_material)
    port.obj(paint, "PigeonOldPaint", root, source_material)
    port.obj(eyes, "PigeonEyePaint", root, source_material)
    port.empty("ANCHOR_Ground", root, (0, 0, 0))
    port.empty("ANCHOR_PlinthTop", root, (0, .48, 0))
    return root


def validate(root):
    measured = port.describe(root)
    meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    assert len(meshes) == 3 and measured["triangles"] < 4200
    assert abs(measured["bounds_min"][1]) < .00001
    assert max(abs(measured["bounds_min"][0]), abs(measured["bounds_max"][0])) <= .45
    assert max(abs(measured["bounds_min"][2]), abs(measured["bounds_max"][2])) <= .45
    assert 1.60 <= measured["bounds_max"][1] <= 1.68
    for obj in meshes:
        assert obj.data.uv_layers and obj.data.color_attributes
        assert bp.signed_volume(([tuple(v.co) for v in obj.data.vertices],
                                 [tuple(p.vertices) for p in obj.data.polygons])) > 0
    return {"design_id": "city_bench_pigeon_statue_v1", "version": "1.0.0",
            "generator": Path(__file__).name,
            "coordinate_system": "Unity metres +Y up/+Z forward; swap YZ and reverse winding; retain FBX unit root",
            "contract": {"base_size": [.86, .48, .86], "base_center": [0, .24, 0],
                         "height": measured["bounds_max"][1], "forward": [0, 0, 1],
                         "material_roles": ["Concrete", "Plaster", "SteelDark"],
                         "no_animation_text_light_or_collision": True}, "model": measured}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    report = validate(build())
    assert validate(build()) == report, "Pigeon must rebuild deterministically"
    manifest = args.model_dir / (NAME + ".json")
    if args.validate_only:
        assert json.loads(manifest.read_text()) == report, "Pigeon manifest is stale"
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        port.base.export(bpy.data.objects[NAME], args.model_dir / (NAME + ".fbx"))
        manifest.write_text(json.dumps(report, indent=2) + "\n")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / (NAME + ".blend")))
    print("BENCH PIGEON CONTRACT OK: fixed bounds, grounded plinth, outward winding and deterministic rebuild")
    print(json.dumps(report["model"]))


if __name__ == "__main__": main()
