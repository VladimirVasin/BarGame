#!/usr/bin/env python3
"""Author either fixed-metre City cannery billboard and its two physical floodlights."""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("ridge_sign_port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)

NAME = "RidgeFishSign"
WIDTH, HEIGHT = 8.3, 10.375
MODEL_DIR = ROOT / "Assets/Resources/City/RidgeFishSign"


def finish_board(root, source, frame, print_face, lenses, width, height):
    frame.object("Frame", root, source)
    lenses.object("FloodDiffusers", root, source)
    face = print_face.object("PrintedFace", root, source)
    uv = face.data.uv_layers.active.data
    for loop in face.data.loops:
        point = face.data.vertices[loop.vertex_index].co
        # From in front of the +Z sign, the Unity camera sees -X as screen-right.
        uv[loop.index].uv = (.5 - point.x / width, point.z / height + .5)


def clock_board():
    port.base.reset()
    root = port.empty("ClockFishSign")
    source = port.base.material("ClockFishSignSurfaceSource")
    width, height = 5.7, 3.8
    frame, print_face, lenses = port.Geometry(), port.Geometry(), port.Geometry()
    port.chamfer(frame, (0, 0, .07), (width + .14, height + .14, .14), port.METAL, .018)
    port.chamfer(print_face, (0, 0, .15), (width, height, .025), port.CABIN, .006)
    for suffix, x in (("Left", -1.41), ("Right", 1.41)):
        # Twin bolted wall plates, cantilevers and angled flood heads. Lamps sit
        # clear of the board, so the light can reach its lower outside corners.
        y, z = 2.18, 1.92
        port.chamfer(frame, (x, y, .06), (.20, .45, .12), port.METAL, .015)
        for bolt_y in (y-.16, y+.16):
            frame.rod((x, bolt_y, .11), (x, bolt_y, .14), .024, port.METAL, 6)
        port.chamfer(frame, (x, y+.11, .99), (.058, .078, 1.90), port.METAL, .009)
        frame.rod((x, y-.13, .12), (x, y+.09, 1.37), .025, port.METAL, 8)
        # Tilt both milky lenses and their metal housings towards the artwork.
        aim_y, aim_z = -.52, .163
        tilt = math.atan2(aim_y-y, z-aim_z)
        sin, cos = math.sin(tilt), math.cos(tilt)

        def tilted_part(geom, offset, size, color, bevel):
            verts, faces = port.kit.chamfered_box((0, 0, 0), size, bevel)
            verts = [(x+u, y+v*cos-(w+offset)*sin, z+v*sin+(w+offset)*cos) for u, v, w in verts]
            geom.add(verts, faces, color)

        tilted_part(frame, 0, (.64, .29, .17), port.METAL, .023)
        tilted_part(lenses, -.088, (.54, .20, .016), port.CABIN, .008)
        port.empty("ANCHOR_Flood" + suffix, root, (x, y+.104*sin, z-.104*cos))
        port.empty("ANCHOR_Aim" + suffix, root, (x, aim_y, aim_z))
    finish_board(root, source, frame, print_face, lenses, width, height)
    report = port.describe(root)
    assert report["bounds_min"][0] == -2.92 and report["bounds_max"][0] == 2.92
    assert report["bounds_min"][2] == 0 and report["bounds_max"][2] < 2.2
    assert report["triangles"] < 1000 and math.isclose(width / height, 1.5)
    return root, {"design_id": "city_clock_fish_sign", "print_size": [width, height], "model": report}


def build():
    port.base.reset()
    root = port.empty(NAME)
    source = port.base.material("RidgeFishSignSurfaceSource")
    frame, print_face, lenses = port.Geometry(), port.Geometry(), port.Geometry()
    port.chamfer(frame, (0, 0, .09), (WIDTH + .16, HEIGHT + .16, .18), port.METAL, .025)
    port.chamfer(print_face, (0, 0, .187), (WIDTH, HEIGHT, .024), port.CABIN, .004)
    for name, x, side in (("Upper", -1.75, 1), ("Lower", 1.75, -1)):
        y = side * (HEIGHT * .5 + .24)
        port.chamfer(frame, (x, y, .065), (.32, .34, .14), port.METAL, .015)
        port.chamfer(frame, (x, y, .73), (.11, .12, 1.34), port.METAL, .012)
        frame.rod((x, y + side * .12, .12), (x, y, 1.29), .031, port.METAL, 8)
        port.chamfer(frame, (x, y, 1.43), (.52, .36, .24), port.METAL, .026)
        port.chamfer(lenses, (x, y, 1.304), (.42, .27, .02), port.CABIN, .009)
        port.empty("ANCHOR_Flood" + name, root, (x, y, 1.286))
        port.empty("ANCHOR_Aim" + name, root, (x * .62, side * 1.65, .199))
    finish_board(root, source, frame, print_face, lenses, WIDTH, HEIGHT)
    report = port.describe(root)
    assert report["bounds_min"][2] == -.005
    assert report["bounds_max"][2] == 1.55
    assert report["triangles"] < 1000
    return root, {"design_id": "city_ridge_fish_sign", "print_size": [WIDTH, HEIGHT], "model": report}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path)
    parser.add_argument("--clock", action="store_true", help="Export the separate landscape billboard at the clock tower.")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    builder = clock_board if args.clock else build
    name = "ClockFishSign" if args.clock else NAME
    model_dir = args.model_dir or MODEL_DIR.parent / name
    _, report = builder()
    root, rebuilt = builder()
    assert rebuilt == report, "The sign must rebuild deterministically."
    manifest = model_dir / (name + ".json")
    if args.validate_only:
        assert json.loads(manifest.read_text()) == report, "The sign manifest is stale."
    else:
        model_dir.mkdir(parents=True, exist_ok=True)
        port.base.export(root, model_dir / (name + ".fbx"))
        manifest.write_text(json.dumps(report, indent=2) + "\n")
    print(f"{name} CONTRACT OK: fixed-metre frame, print UVs, floodlight anchors and deterministic rebuild")


if __name__ == "__main__":
    main()
