#!/usr/bin/env python3
"""Author four stitchable, fractured rock surfaces for the City mountain rim."""
import argparse
from collections import Counter
import importlib.util
import json
from pathlib import Path
import random
import sys

from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("city_port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
port.SURFACE_TILES["Rock"] = 6.
OUTPUT = ROOT / "Assets/Resources/City/Rockface"
MANIFEST = "CityRockface3D.json"

# Unequal plates under a shared diagonal bedding direction. The larger parent
# plane passes through the corners; the smaller plates weather out in its face.
# Positions are (X along slope, Y toward crest, relative prominence).
BEDS = (
    ((.32, .21, .83), (.72, .45, 1.), (.38, .86, .63)),
    ((.16, .49, .65), (.62, .24, 1.), (.41, .76, .82), (.84, .80, .50)),
    ((.32, .68, 1.), (.36, .20, .61), (.81, .43, .82)),
    ((.16, .21, .48), (.27, .67, .93), (.74, .38, .72), (.72, .91, 1.)),
)
CORNERS = ((0., 0.), (1., 0.), (1., 1.), (0., 1.))
EDGE = tuple((a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t)
             for a, b in zip(CORNERS, CORNERS[1:] + CORNERS[:1]) for t in (0., .25, .5, .75))


def plate_outline(centre, others, slope):
    # Clip by skewed perpendicular bisectors. Shared fracture planes follow
    # the bedding without falling into horizontal, equal-height courses.
    poly = list(CORNERS)
    for other in others:
        dx, dy = other[0]-centre[0], other[1]-centre[1]
        nx, ny = dx + slope*dy, slope*dx + dy
        mx, my = (other[0]+centre[0])*.5, (other[1]+centre[1])*.5
        trimmed = []
        for a, b in zip(poly, poly[1:] + poly[:1]):
            da, db = nx*(a[0]-mx) + ny*(a[1]-my), nx*(b[0]-mx) + ny*(b[1]-my)
            if da <= 0:
                trimmed.append(a)
            if (da < 0) != (db < 0):
                t = da/(da-db)
                trimmed.append((a[0]+t*(b[0]-a[0]), a[1]+t*(b[1]-a[1])))
        poly = trimmed
    return poly


def make_topology(index):
    rng = random.Random(7589 + index)
    points = [(x, y, 0.) for x, y in EDGE]
    edges = [(i, (i+1) % len(EDGE)) for i in range(len(EDGE))]
    beds = BEDS[index]
    slope = (.44, -.52, -.41, .33)[index]
    for plate, (cx, cy, height) in enumerate(beds):
        cell = plate_outline((cx, cy), beds[:plate] + beds[plate+1:], slope)
        # Split broad edges once into asymmetric planes; vertices stay on the
        # fracture line before the rings retract, so neighbouring plates fit.
        corners = []
        for a, b in zip(cell, cell[1:] + cell[:1]):
            corners.append(a)
            if (b[0]-a[0])**2 + (b[1]-a[1])**2 > .64:
                t = rng.uniform(.32, .68)
                corners.append((a[0]+t*(b[0]-a[0]), a[1]+t*(b[1]-a[1])))

        # Shift the exposed brow off-centre to give the slab a steep broken
        # lip on one side and a long sloping weathered face on the other.
        tip = corners[rng.randrange(len(corners))]
        tilt = rng.uniform(-.35, .35)
        for reach, shift, rise, scatter in ((.94, 0., .055, .027), (.68, .14, .55, .13),
                                            (.33, .39, .90, .055)):
            start = len(points)
            for x, y in corners:
                extent = reach + rng.uniform(-.027, .027)
                px = cx + (x-cx)*extent + (tip[0]-cx)*shift
                py = cy + (y-cy)*extent + (tip[1]-cy)*shift
                crown = rise + rng.uniform(-scatter, scatter)
                if reach < .9:
                    crown += tilt*(x-cx) + .20*(y-cy)
                points.append((px, py, max(.01, height*crown)))
            edges.extend((start+i, start+(i+1) % len(corners)) for i in range(len(corners)))

    peak = max(p[2] for p in points)
    points = [(x, y, z/peak) for x, y, z in points]
    _, _, faces, originals, _, _ = delaunay_2d_cdt(
        [Vector((x, y)) for x, y, _ in points], edges, [], 0, 1e-7)
    if any(len(src) != 1 for src in originals):
        raise ValueError("Intersecting rock plate outlines")
    faces = [tuple(originals[i][0] for i in face) for face in faces]
    # A single-valued complete height field with only the four open rim edges.
    adjacency = Counter()
    area = 0.
    for a, b, c in faces:
        p, q, r = [points[i] for i in (a, b, c)]
        signed = ((q[0]-p[0])*(r[1]-p[1]) - (q[1]-p[1])*(r[0]-p[0]))*.5
        if signed <= 1.e-9:
            raise ValueError("Degenerate or back-facing face")
        area += signed
        for pair in ((a, b), (b, c), (c, a)):
            adjacency[tuple(sorted(pair))] += 1
    boundary = {edge for edge, owners in adjacency.items() if owners == 1}
    if boundary != {tuple(sorted(edge)) for edge in edges[:len(EDGE)]} or max(adjacency.values()) != 2:
        raise ValueError("Hole, T-junction or non-manifold rock face")
    if abs(area-1.) > 1.e-6 or len(faces) > 160:
        raise ValueError("Wrong patch area or triangle budget")
    return points, faces


def build():
    roots, models = [], []
    material = port.base.material("RockfaceSource")
    for i, beds in enumerate(BEDS):
        root = port.empty(f"CityRockface{i}")
        geometry = port.Geometry()
        geometry.role = "Rock"
        points, faces = make_topology(i)
        # The source is deliberately a single outward face. The runtime ridge
        # supplies the enclosing cross section at the sides and back.
        geometry.add(points, faces, port.CONCRETE, solid=False)
        port.obj(geometry, root.name + "Surface", root, material)
        model = port.describe(root)
        model.pop("anchors")
        model["plates"] = len(beds)
        if model["bounds_min"] != [0.]*3 or model["bounds_max"] != [1.]*3:
            raise ValueError("Source unit envelope changed")
        roots.append(root)
        models.append(model)
    port.validate_surfaces(roots)
    return roots, {"design_id": "city_west_south_fractured_rim_v1", "generator": Path(__file__).name,
                   "axes": "X along ridge, Y toward crest, +Z toward City", "edge_intervals": 4,
                   "models": models}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=OUTPUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    port.base.reset()
    roots, data = build()
    if args.validate_only:
        if data != json.loads((args.model_dir / MANIFEST).read_text()):
            raise ValueError("Rockface manifest differs")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            port.base.export(root, args.model_dir / (root.name + ".fbx"))
        (args.model_dir / MANIFEST).write_text(json.dumps(data, indent=2) + "\n")
    port.base.reset()
    if data != build()[1]:
        raise ValueError("Rockface deterministic rebuild differs")
    print("CITY ROCKFACE: verified complete forward-facing fractured surfaces and identical stitch edges")


if __name__ == "__main__":
    main()
