#!/usr/bin/env python3
"""Fixed-metre broad stair for the surveyed West11/West13 bench frontage.

The imported concrete and supported outer rails reuse the City's existing
terrace authoring/surface grammar. Unity owns placement, ground cut, collision,
foot probes and the two landings. No scene, texture, light or text is authored.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
spec = importlib.util.spec_from_file_location("bench_stair_port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)

NAME = "BenchAccessStair"
WIDTH, STEPS, RISE, TREAD = 10.4, 6, .16, .36
RUN, HEIGHT, BOTTOM = STEPS * TREAD, STEPS * RISE, -.25
MODEL_DIR = ROOT / "Assets/Resources/City/BenchAccess"
SOURCE_DIR = ROOT / "ArtSource/City/BenchAccess"


def build():
    port.base.reset()
    material = port.base.material("BenchStairSharedSurfaceSource")
    root = port.empty(NAME)
    concrete, metal = port.Geometry(), port.Geometry()
    concrete.role, metal.role = "Concrete", "SteelDark"
    for step in range(STEPS):
        top = (step + 1) * RISE
        # Real load-bearing risers and treads, not a sloped visual surface.
        # Buried bases close the sides beneath the shared contact datum.
        port.chamfer(concrete, (0, (top + BOTTOM) * .5, (step + .5) * TREAD),
                     (WIDTH, top - BOTTOM, TREAD), port.CONCRETE, .008)
    slope = HEIGHT / RUN
    for side in (-1, 1):
        x = side * (WIDTH * .5 - .10)
        first, last = .05, RUN - .05
        for height, radius in ((1.12, .025), (.25, .014)):
            metal.rod((x, height + slope * first, first),
                      (x, height + slope * last, last), radius, port.METAL, 8)
        for index in range(4):
            z = first + (last - first) * index / 3
            floor = min(STEPS, int(z / TREAD) + 1) * RISE
            top = 1.12 + slope * z
            port.chamfer(metal, (x, floor + .013, z), (.12, .026, .10), port.METAL, .004)
            metal.rod((x, floor, z), (x, top, z), .023, port.METAL, 8)
            for offset in (-.038, .038):
                metal.rod((x + offset, floor + .025, z),
                          (x + offset, floor + .033, z), .009, port.METAL, 6)
        for index in range(1, 14):
            z = RUN * index / 14
            metal.rod((x, .25 + slope * z, z), (x, 1.12 + slope * z, z), .009, port.METAL, 6)
    port.obj(concrete, "BenchAccessSteps", root, material)
    port.obj(metal, "BenchAccessRails", root, material)
    port.empty("ANCHOR_Lower", root, (0, 0, 0))
    port.empty("ANCHOR_Upper", root, (0, HEIGHT, RUN))
    return root


def validate(root):
    measured = port.describe(root)
    meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    assert len(meshes) == 2 and measured["triangles"] < 6500
    for obj in meshes:
        vertices = [tuple(vertex.co) for vertex in obj.data.vertices]
        faces = [tuple(face.vertices) for face in obj.data.polygons]
        assert bp.signed_volume((vertices, faces)) > 0, "Stair winding must point outward"
        assert obj.data.uv_layers and obj.data.color_attributes
    steps = next(obj for obj in meshes if obj.name.startswith("BenchAccessSteps"))
    tree = BVHTree.FromPolygons([vertex.co for vertex in steps.data.vertices],
                               [list(face.vertices) for face in steps.data.polygons])
    for index in range(STEPS):
        for x in (-WIDTH * .5 + .05, 0, WIDTH * .5 - .05):
            point, _, _, _ = tree.ray_cast(Vector(port.source((x, HEIGHT + 1, (index + .5) * TREAD))),
                                          Vector(port.source((0, -1, 0))), HEIGHT + 2)
            assert point is not None and abs(port.source(point)[1] - (index + 1) * RISE) < .00001
    assert abs(measured["bounds_min"][0] + WIDTH * .5) < .00001
    assert abs(measured["bounds_max"][0] - WIDTH * .5) < .00001
    assert abs(measured["bounds_min"][2]) < .00001 and abs(measured["bounds_max"][2] - RUN) < .00001
    return {"design_id": "city_bench_access_stair_v1", "version": "1.0.0",
            "generator": Path(__file__).name,
            "coordinate_system": "Unity metres +Y up/+Z up-stair; swap YZ and reverse winding; retain FBX units",
            "contract": {"width": WIDTH, "steps": STEPS, "rise": RISE, "tread": TREAD,
                         "total_rise": HEIGHT, "run": RUN, "bottom_y": BOTTOM,
                         "lower": [0, 0, 0], "upper": [0, HEIGHT, RUN],
                         "material_roles": ["Concrete", "SteelDark"],
                         "placement": "fixed metres; no dimension scaling"},
            "model": measured}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    root = build()
    report = validate(root)
    assert validate(build()) == report, "Bench stair geometry must rebuild deterministically"
    manifest = args.model_dir / (NAME + ".json")
    if args.validate_only:
        assert json.loads(manifest.read_text()) == report, "Bench stair manifest is stale"
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        # Export the second identical build; the full FBX authoring root retains units.
        root = bpy.data.objects[NAME]
        port.base.export(root, args.model_dir / (NAME + ".fbx"))
        manifest.write_text(json.dumps(report, indent=2) + "\n")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / (NAME + ".blend")))
    print("BENCH ACCESS STAIR CONTRACT OK: measured treads, width, outward winding and deterministic rebuild")
    print(json.dumps(report["model"]))


if __name__ == "__main__": main()
