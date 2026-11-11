#!/usr/bin/env python3
"""Author the fixed-metre Ridge gable sign, its brackets and two floodlights."""
from __future__ import annotations

import argparse
import importlib.util
import json
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
    frame.object("Frame", root, source)
    lenses.object("FloodDiffusers", root, source)
    face = print_face.object("PrintedFace", root, source)
    uv = face.data.uv_layers.active.data
    for loop in face.data.loops:
        point = face.data.vertices[loop.vertex_index].co
        # From in front of the +Z sign, the Unity camera sees -X as screen-right.
        uv[loop.index].uv = (.5 - point.x / WIDTH, point.z / HEIGHT + .5)
    report = port.describe(root)
    assert report["bounds_min"][2] == -.005
    assert report["bounds_max"][2] == 1.55
    assert report["triangles"] < 1000
    return root, {"design_id": "city_ridge_fish_sign", "print_size": [WIDTH, HEIGHT], "model": report}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    _, report = build()
    root, rebuilt = build()
    assert rebuilt == report, "The Ridge sign must rebuild deterministically."
    manifest = args.model_dir / (NAME + ".json")
    if args.validate_only:
        assert json.loads(manifest.read_text()) == report, "The Ridge sign manifest is stale."
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        port.base.export(root, args.model_dir / (NAME + ".fbx"))
        manifest.write_text(json.dumps(report, indent=2) + "\n")
    print("RIDGE FISH SIGN CONTRACT OK: fixed-metre frame, print UVs, floodlight anchors and deterministic rebuild")


if __name__ == "__main__":
    main()
