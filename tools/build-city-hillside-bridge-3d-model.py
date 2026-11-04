#!/usr/bin/env python3
"""Measured side truss and sloped parapet head for the two upper City road bridges."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("bridge_kit", ROOT / "tools/build-city-residential-courtyard-3d-model.py")
kit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kit)
port = kit.port
OUT = ROOT / "Assets/Resources/City/Hillside"
MANIFEST = "CityHillsideBridge3D.json"
SPAN = 21.5
PANELS = 6


def member(g, a, b, depth, breadth, color, i_section=False):
    """Rolled I-section or flat-bar between XY nodes, in Unity metres."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    x_axis, y_axis = dx / length, dy / length
    x, y = (a[0] + b[0]) * .5, (a[1] + b[1]) * .5

    def strip(offset, height, width):
        verts, faces = port.kit.chamfered_box((0, 0, 0), (length, width, height), min(.006, height / 5))
        # Blender Y is Unity Z. Keep winding and signed volume with this
        # proper rotation in Blender's XZ plane before the kit conversion.
        shifted = [(x + x_axis * vx - y_axis * (vz + offset), vy,
                    y + y_axis * vx + x_axis * (vz + offset)) for vx, vy, vz in verts]
        port.kit_add(g, (shifted, faces), color)

    if i_section:
        flange = .026
        strip(0, depth - 2 * flange, .045)
        for side in (-1, 1):
            strip(side * (depth - flange) / 2, flange, breadth)
    else:
        strip(0, depth, breadth)


def truss(mat):
    root = port.empty("CourtBridgeTruss")
    steel, fixings = kit.geometry("SteelDark"), kit.geometry("Steel")
    # Feet rest on the edge of the deck. The end batter opens a view over
    # the first bay as the hero steps onto the crossing.
    nodes = [.15 + i * (SPAN - .30) / PANELS for i in range(PANELS + 1)]
    roof = [.65, 2.18, 2.22, 2.22, 2.22, 2.18, .65]
    for i in range(PANELS):
        a, b = nodes[i], nodes[i + 1]
        member(steel, (a, roof[i]), (b, roof[i + 1]), .215, .19, port.METAL, True)
        member(steel, (a, .22), (b, .22), .19, .17, port.METAL, True)
        if 0 < i < PANELS - 1:
            # Diagonals point toward the centre on the two halves.
            top_at, low_at = (a, b) if i < PANELS / 2 else (b, a)
            member(steel, (low_at, .26), (top_at, 2.15), .145, .11, port.METAL, True)
        # Separate continuous handrail on the inside edge of the frame.
        for y, radius in ((1.04, .032), (.55, .019)):
            steel.rod((a, y, .095), (b, y, .095), radius, port.METAL, 8)
        kit.box(steel, ((a + b) * .5, .52, .094), (.065, .99, .065), port.METAL)
    for i, x in enumerate(nodes):
        top = roof[i]
        kit.box(steel, (x, .045, 0), (.30, .09, .25), port.METAL, .006)
        member(steel, (x, .09), (x, top - .02), .135, .135, port.METAL)
        for y in (.24, top - .075):
            for face in (-1, 1):
                kit.box(fixings, (x, y, face * .079), (.265, .21, .025), port.PAINT, .007)
                for offset in (-.083, .083):
                    fixings.rod((x + offset, y, face * .091), (x + offset, y, face * .112),
                                .025, port.METAL, 6)
    kit.emit(steel, "RolledChordsBracesAndHandrail", root, mat)
    kit.emit(fixings, "GussetPlatesAndBolts", root, mat)
    port.empty("ANCHOR_Start", root, (0, 0, 0))
    port.empty("ANCHOR_End", root, (SPAN, 0, 0))
    return root


def parapet_head(mat):
    root = port.empty("UpperBridgeParapetHead")
    stone = kit.geometry("ConcreteWall")
    # The outside nose lowers toward each bank. A chamfer at both top edges
    # catches daylight even when the far railing dissolves into city fog.
    outline = [(-.48, .10), (.48, .10), (.45, .92), (.40, 1.04),
               (-.15, 1.04), (-.44, .76)]
    def prism(points, half_width, tint):
        verts = [(x, y, z) for z in (-half_width, half_width) for x, y in points]
        n = len(points)
        faces = [tuple(reversed(range(n))), tuple(range(n, 2*n))]
        faces += [(i, (i+1) % n, n+(i+1) % n, n+i) for i in range(n)]
        stone.add(verts, faces, tint)
    kit.box(stone, (0, .066, 0), (1.0, .132, .33), port.CONCRETE, .017)
    prism(outline, .147, port.CONCRETE)
    member(stone, (-.435, .76), (-.144, 1.04), .061, .335, port.EDGE)
    kit.box(stone, (.122, 1.039, 0), (.566, .066, .335), port.EDGE, .011)
    kit.emit(stone, "RakedConcreteHead", root, mat)
    return root


def build():
    mat = port.base.material("HillsideBridgeSource")
    roots = [truss(mat), parapet_head(mat)]
    models = [port.describe(r) for r in roots]
    port.validate_surfaces(roots)
    side, head = models
    if not (21.48 <= side["bounds_max"][0] <= SPAN + .01 and
            2.31 <= side["bounds_max"][1] <= 2.34 and
            1.05 <= head["bounds_max"][1] <= 1.08 and
            side["triangles"] <= 8500 and head["triangles"] <= 250):
        raise ValueError(f"Bridge model outside measured profile: {models}")
    return roots, {"design_id":"upper_road_bridge_fittings_v1", "span_m":SPAN, "bays":PANELS, "models":models}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=OUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    port.base.reset()
    roots, data = build()
    if args.validate_only:
        if json.loads((args.model_dir / MANIFEST).read_text()) != data:
            raise ValueError("Upper bridge model manifest differs")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            port.base.export(root, args.model_dir / (root.name + ".fbx"))
        (args.model_dir / MANIFEST).write_text(json.dumps(data, indent=2) + "\n")
    port.base.reset()
    if build()[1] != data:
        raise ValueError("Upper bridge model geometry changed between builds")
    print("CITY UPPER BRIDGES: measured raked six-bay truss and concrete parapet head", flush=True)


if __name__ == "__main__":
    main()
