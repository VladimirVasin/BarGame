#!/usr/bin/env python3
"""Build the passive terrace-pilot kit through tools/run-blender.py.

Authored Unity metres: +Y up, +Z up the stair. Full FBX wrappers retain file
units; runtime never resets their imported scale. Plain supports allow declared
dimension scaling; the passage receives only the measured grade-length factor.
Stairs and upper platforms retain their authored metres. Shared City surfaces
supply their final material.
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
spec = importlib.util.spec_from_file_location("terrace_port", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)

MODEL_DIR = ROOT / "Assets/Resources/CityTerraces"
MANIFEST = "CityTerrace3D.json"
NAMES = ("TerraceFlight", "TerraceLanding", "TerraceRail", "TerraceSupport", "TerraceCourt",
         "TerraceUpperCourt", "TerraceBridge", "TerracePassage")
STEPS, RISE, TREAD, WIDTH = 12, .16, .32, 2.10
LENGTH, HEIGHT = STEPS * TREAD, STEPS * RISE
ROLES = ("Concrete", "ConcreteWall", "SteelDark")
MAX_TRIANGLES = (6500, 1800, 2600, 100, 15000, 12000, 1500, 10000)


def geometry(role):
    result = port.Geometry()
    result.role = role
    return result


def box(g, center, size, color=port.CONCRETE, bevel=.008):
    port.chamfer(g, center, size, color, bevel)


def emit(g, name, root, material):
    return port.obj(g, name, root, material)


def anchor(root, name, point):
    port.empty(name, root, point)


def rail_geometry(g, x, length, slope=0.0, first_tread=False):
    # The sloping rail follows the ramp datum +1.12 m. Its physical height
    # above the visible tread stays 0.96–1.12 m, never a floating unsupported
    # line: every post penetrates its actual tread or landing.
    rail_top = 1.12 if slope else 1.025
    start, end = .05, length - .05
    top_a = (x, rail_top + slope * start, start)
    top_b = (x, rail_top + slope * end, end)
    g.rod(top_a, top_b, .025, port.METAL, 8)
    g.rod((x, .24 + slope * start, start), (x, .24 + slope * end, end), .014, port.METAL, 6)
    bays = 4 if slope else 3
    for index in range(bays + 1):
        z = start + (end - start) * index / bays
        floor_y = (min(STEPS, int(z / TREAD) + 1) * RISE) if first_tread else 0.0
        top = rail_top + slope * z
        box(g, (x, floor_y + .013, z), (.12, .026, .10), port.METAL, .004)
        g.rod((x, floor_y, z), (x, top, z), .023, port.METAL, 8)
        # A small visible fixing sits on each footplate, outside the clear lane.
        for offset in (-.038, .038):
            g.rod((x + offset, floor_y + .025, z), (x + offset, floor_y + .033, z), .009, port.DARK, 6)
    pickets = max(1, round(length / .16))
    for index in range(1, pickets):
        z = length * index / pickets
        g.rod((x, .24 + slope * z, z), (x, rail_top + slope * z, z), .009, port.METAL, 6)


def flight(material):
    root = port.empty(NAMES[0])
    stone, metal = geometry("Concrete"), geometry("SteelDark")
    for index in range(STEPS):
        top = (index + 1) * RISE
        # Separate full-depth riser solids close every side and the underside;
        # the .008 m edge relief changes neither tread pitch nor contact datum.
        color = port.CONCRETE if index not in (3, 8) else port.EDGE
        box(stone, (0, (top - .25) * .5, (index + .5) * TREAD),
            (WIDTH, top + .25, TREAD), color, .008)
    for side in (-1, 1):
        rail_geometry(metal, side * .99, LENGTH, HEIGHT / LENGTH, True)
    emit(stone, "FlightSteps", root, material)
    emit(metal, "FlightSupportedRails", root, material)
    anchor(root, "ANCHOR_Lower", (0, 0, 0))
    anchor(root, "ANCHOR_Upper", (0, HEIGHT, LENGTH))
    return root


def slab(name, size, thickness, divisions, material):
    root = port.empty(name)
    stone = geometry("Concrete")
    size_x, size_z = size if isinstance(size, tuple) else (size, size)
    cols, rows = divisions if isinstance(divisions, tuple) else (divisions, divisions)
    box(stone, (0, -(thickness + .045) * .5, 0),
        (size_x, thickness - .045, size_z), port.CONCRETE, .008)
    pitch_x, pitch_z = size_x / cols, size_z / rows
    for row in range(rows):
        for column in range(cols):
            # Shallow seams terminate in the continuous base. All walking tops
            # remain y=0 and the outer footprint is exactly the declared size.
            low_x = -size_x * .5 + column * pitch_x + (.003 if column else 0)
            high_x = -size_x * .5 + (column + 1) * pitch_x - (.003 if column < cols - 1 else 0)
            low_z = -size_z * .5 + row * pitch_z + (.003 if row else 0)
            high_z = -size_z * .5 + (row + 1) * pitch_z - (.003 if row < rows - 1 else 0)
            color = port.EDGE if (column * 3 + row * 7) % 17 == 4 else port.CONCRETE
            box(stone, ((low_x + high_x) * .5, -.0225, (low_z + high_z) * .5),
                (high_x - low_x, .045, high_z - low_z), color, .003)
    emit(stone, "PavingAndBase", root, material)
    anchor(root, "ANCHOR_Surface", (0, 0, 0))
    return root


def rail(material):
    root = port.empty(NAMES[2])
    metal = geometry("SteelDark")
    rail_geometry(metal, 0, 2.4)
    emit(metal, "SupportedRail", root, material)
    anchor(root, "ANCHOR_Start", (0, 0, 0))
    anchor(root, "ANCHOR_End", (0, 0, 2.4))
    return root


def support(material):
    root = port.empty(NAMES[3])
    stone = geometry("ConcreteWall")
    # A plain load-bearing concrete mass. No masonry joints or fittings are
    # stretched when the pilot gives it its explicit foundation dimensions.
    box(stone, (0, -.96, 0), (2.4, 1.92, 2.4), port.CONCRETE, .008)
    emit(stone, "RetainingMass", root, material)
    anchor(root, "ANCHOR_Top", (0, 0, 0))
    anchor(root, "ANCHOR_Base", (0, -1.92, 0))
    return root


def build():
    material = port.base.material("TerraceSharedSurfaceSource")
    return [flight(material), slab(NAMES[1], 2.4, .25, 3, material), rail(material),
            support(material), slab(NAMES[4], 10.0, .30, 10, material),
            slab(NAMES[5], (7.6, 10.0), .30, (8, 10), material),
            slab(NAMES[6], (4.8, 2.4), .35, (4, 2), material),
            slab(NAMES[7], (3.0, 21.5), .30, (3, 22), material)]


def manifest(roots):
    models = []
    for index, root in enumerate(roots):
        measured = port.describe(root)
        meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
        if measured["triangles"] > MAX_TRIANGLES[index] or len(meshes) > 2:
            raise RuntimeError("Terrace geometry budget exceeded: " + root.name)
        for obj in meshes:
            role = obj.name.rsplit("__", 1)[-1].split(".")[0]
            if role not in ROLES or not obj.data.uv_layers or not obj.data.color_attributes:
                raise RuntimeError("Terrace semantic surface lost: " + obj.name)
        measured["renderer_count"] = len(meshes)
        measured["material_roles"] = sorted({obj.name.rsplit("__", 1)[-1].split(".")[0] for obj in meshes})
        measured["maximum_triangles"] = MAX_TRIANGLES[index]
        models.append(measured)
    port.validate_surfaces(roots)
    # Measure the actual authored tread surfaces and landing anchors, not
    # merely the constants written next to them.
    steps = next(obj for obj in roots[0].children_recursive if obj.type == "MESH" and "__Concrete" in obj.name)
    tree = BVHTree.FromPolygons([vertex.co for vertex in steps.data.vertices],
                               [list(polygon.vertices) for polygon in steps.data.polygons])
    for index in range(STEPS):
        z = (index + .5) * TREAD
        point, _, _, _ = tree.ray_cast(Vector(port.source((0, 3, z))), Vector(port.source((0, -1, 0))), 4)
        if point is None or abs(port.source(point)[1] - (index + 1) * RISE) > 1e-5:
            raise RuntimeError("Terrace tread pitch differs from collision contract")
    if models[0]["bounds_min"][0] < -1.051 or models[0]["bounds_max"][0] > 1.051:
        raise RuntimeError("Stair rails escaped the authored flight width")
    if abs(models[0]["bounds_min"][2]) > 1e-5 or abs(models[0]["bounds_max"][2] - LENGTH) > 1e-5:
        raise RuntimeError("Stair rails escaped the lower/upper landing footprint")
    return {
        "design_id": "city_terrace_pilot_kit_v1", "version": "1.1.0", "generator": Path(__file__).name,
        "coordinate_system": "Unity metres +Y up/+Z up-stair; swap YZ/reverse winding for Blender; retain full FBX wrapper units",
        "material_source": "Existing shared City concrete/wall/dark metal; no texture, light, animation or collider in source",
        "geometry_signature_includes": ["vertices", "faces", "colors", "uv0", "transforms"],
        "contracts": {
            "flight": {"steps": STEPS, "rise": RISE, "tread": TREAD, "total_rise": HEIGHT, "run": LENGTH,
                       "width": WIDTH, "clear_width_min": 1.86, "bottom_y": -.25,
                       "ramp_start": [0, 0, 0], "ramp_end": [0, HEIGHT, LENGTH], "rail_ramp_datum_height": 1.12},
            "landing": {"size": [2.4, .25, 2.4], "top_y": 0},
            "rail": {"length": 2.4, "height": 1.05, "axis": "+Z"},
            "support": {"size": [2.4, 1.92, 2.4], "top_y": 0,
                        "permitted_dimension_scaling": "Plain support mass only; preserve wrapper scale and world-metre material mapping"},
            "court": {"size": [10, .30, 10], "top_y": 0},
            "upper_court": {"size": [7.6, .30, 10], "top_y": 0},
            "bridge": {"size": [4.8, .35, 2.4], "top_y": 0,
                       "support": "Span bears on the two upper-court abutments; no full-height mass below the span"},
            "passage": {"size": [3.0, .30, 21.5], "top_y": 0,
                        "permitted_dimension_scaling": "Local Z only: measured sloping length / 21.5; horizontal run and width remain fixed metres"}},
        "semantic_uv_tiles_metres": {role: port.SURFACE_TILES[role] for role in ROLES},
        "models": models, "assets": [name + ".fbx" for name in NAMES]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    port.base.reset()
    roots = build()
    result = manifest(roots)
    if args.validate_only:
        if json.loads((args.model_dir / MANIFEST).read_text()) != result:
            raise RuntimeError("Terrace authored contract differs from published manifest")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            port.base.export(root, args.model_dir / (root.name + ".fbx"))
        (args.model_dir / MANIFEST).write_text(json.dumps(result, indent=2) + "\n")
    port.base.reset()
    if manifest(build()) != result:
        raise RuntimeError("Terrace deterministic rebuild mismatch")
    print("CITY TERRACE ART CONTRACT OK: measured tread pitch, metres, supported rails, semantic UVs and deterministic geometry", flush=True)
    print(json.dumps({"models": [{key: model[key] for key in ("name", "bounds_min", "bounds_max", "triangles", "renderer_count")} for model in result["models"]]}))


if __name__ == "__main__":
    main()
