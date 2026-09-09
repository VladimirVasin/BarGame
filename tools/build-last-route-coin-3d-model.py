"""The Ferryman's existing octagonal coin, and a passive glovebox pile of it.

python tools/run-blender.py tools/build-last-route-coin-3d-model.py --expect Assets/Resources/Vehicles/LastRouteCoin3D.fbx --expect Assets/Resources/Vehicles/LastRouteCoin3D.json
The same invocation with --validate-only reconstructs and compares the manifest.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Euler, Vector

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Vehicles"
SOURCE = ROOT / "ArtSource/Vehicles/Blender/LastRouteCoin3D.blend"
RADIUS = .027
THICKNESS = .009
SIDES = 8
SEED = 170903
TINT = (.96, .86, .52, 1)
# Metres relative to the existing glovebox hinge; Unity XYZ.
INTERIOR_MIN = (-.14, .02, -.21)
INTERIOR_MAX = (.14, .12, 0)
BULB_MIN = (-.024, .082, -.046)
BULB_MAX = (.024, .126, -.014)
STACKS = ((6, 7, 5, 7, 6), (7, 8, 8, 8, 7), (7, 8, 8, 8, 7))


def stable(value):
    if isinstance(value, float):
        return round(value, 7)
    if isinstance(value, (tuple, list)):
        return [stable(item) for item in value]
    if isinstance(value, dict):
        return {key: stable(item) for key, item in value.items()}
    return value


def unity(point):
    return (point.x, point.z, point.y)


def bounds(vertices):
    return ([min(v[a] for v in vertices) for a in range(3)],
            [max(v[a] for v in vertices) for a in range(3)])


def make_geometry():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)
    # Preserve the earlier eight-sided runtime coin's measured silhouette.
    bpy.ops.mesh.primitive_cylinder_add(vertices=SIDES, radius=RADIUS,
        depth=THICKNESS, end_fill_type="TRIFAN", location=(0, 0, 0))
    single = bpy.context.object
    single.name = "FerrymanCoin"
    single.data.name = single.name
    for face in single.data.polygons:
        face.use_smooth = False
    material = bpy.data.materials.new("OldBrass")
    material.diffuse_color = TINT
    single.data.materials.append(material)

    # Repeated authored geometry, exported as one static surface. Small yaw
    # offsets break aligned edges; only exposed top coins lean off their stack.
    rng = random.Random(SEED)
    source_vertices = [v.co.copy() for v in single.data.vertices]
    source_faces = [tuple(p.vertices) for p in single.data.polygons]
    vertices, faces, records = [], [], []
    for row, stacks in enumerate(STACKS):
        for column, layers in enumerate(stacks):
            centre_x = (column - 2) * .054
            centre_z = -.037 - row * .063
            for layer in range(layers):
                x = centre_x + rng.uniform(-.001, .001)
                z = centre_z + rng.uniform(-.001, .001)
                top = layer == layers - 1
                pitch = rng.uniform(-.105, .105) if top else 0
                roll = rng.uniform(-.105, .105) if top else 0
                yaw = rng.uniform(-math.pi, math.pi)
                rotation = Euler((pitch, roll, yaw)).to_matrix()
                rotated = [rotation @ vertex for vertex in source_vertices]
                # The lowest point rests on the previous flat coin; no coin
                # floats above its stack and the lamp keeps a clear front bay.
                y = INTERIOR_MIN[1] + layer * THICKNESS - min(v.z for v in rotated)
                offset = Vector((x, z, y))
                points = [point + offset for point in rotated]
                measured = [unity(point) for point in points]
                minimum, maximum = bounds(measured)
                for axis in range(3):
                    assert minimum[axis] >= INTERIOR_MIN[axis] - 1e-6
                    assert maximum[axis] <= INTERIOR_MAX[axis] + 1e-6
                assert not all(minimum[a] < BULB_MAX[a] and
                               maximum[a] > BULB_MIN[a] for a in range(3)), "Coin touches bulb"
                records.append({"centre_unity_m": [x, y, z],
                    "rotation_blender_rad": [pitch, roll, yaw],
                    "bounds_min_unity_m": minimum, "bounds_max_unity_m": maximum})
                first = len(vertices)
                vertices.extend(points)
                faces.extend(tuple(first + index for index in face) for face in source_faces)

    mesh = bpy.data.meshes.new("GloveboxCoins")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.materials.append(material)
    pile = bpy.data.objects.new("GloveboxCoins", mesh)
    bpy.context.collection.objects.link(pile)
    # The source is untextured today, but every repeated face carries its
    # source UV rather than becoming an unwrappable future exception.
    source_uv = single.data.uv_layers.active
    uv = mesh.uv_layers.new(name="UVMap")
    source_loops = len(single.data.loops)
    for index in range(len(mesh.loops)):
        uv.data[index].uv = source_uv.data[index % source_loops].uv
    return single, pile, records


def geometry_record(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    volume = sum(mesh.vertices[t.vertices[0]].co.dot(
        mesh.vertices[t.vertices[1]].co.cross(mesh.vertices[t.vertices[2]].co))
        for t in mesh.loop_triangles) / 6
    assert volume > 0, "Inverted coin faces"
    points = [unity(v.co) for v in mesh.vertices]
    minimum, maximum = bounds(points)
    geometry = stable({"vertices": points,
        "polygons": [list(p.vertices) for p in mesh.polygons]})
    signature = hashlib.sha256(json.dumps(geometry, sort_keys=True).encode()).hexdigest()
    return stable({"name": obj.name, "vertices": len(points),
        "triangles": len(mesh.loop_triangles), "bounds_min_unity_m": minimum,
        "bounds_max_unity_m": maximum, "signed_volume_m3": volume,
        "geometry_sha256": signature})


def manifest(objects, coins):
    # Validate the frame against the actual car model rather than a sketch.
    car = json.loads((ROOT / "Assets/Vehicles/Models/LastRouteCar3D.json").read_text())
    hinge = next(p for p in car["pivots"] if p["name"] == "PIVOT_GloveboxLid")
    assert hinge["local_position"] == [-.46, -.93, .88]
    return stable({"generator": "tools/build-last-route-coin-3d-model.py",
        "version": 1, "seed": SEED, "diameter_m": RADIUS * 2,
        "thickness_m": THICKNESS, "sides": SIDES, "tint": TINT,
        "coin_count": len(coins), "interior_min_unity_m": INTERIOR_MIN,
        "interior_max_unity_m": INTERIOR_MAX,
        "mesh_space": "Unity metres, +Y face normal; pile relative to glovebox hinge",
        "meshes": [geometry_record(obj) for obj in objects], "coins": coins})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--model-dir", type=Path, default=OUTPUT)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    single, pile, coins = make_geometry()
    expected = manifest((single, pile), coins)
    single, pile, coins = make_geometry()
    assert manifest((single, pile), coins) == expected, "Coin regeneration is not deterministic"
    target = args.model_dir / "LastRouteCoin3D.json"
    if args.validate_only:
        assert json.loads(target.read_text(encoding="utf-8")) == expected, "Coin manifest is stale"
        print("PASS Last Route coin silhouette, solid faces, deterministic pile, compartment and bulb clearance")
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    single.select_set(True)
    pile.select_set(True)
    bpy.context.view_layer.objects.active = single
    # Bare mesh assets, matching the existing chess-set export: scale and
    # axis conversion are baked rather than left on an unused model root.
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / "LastRouteCoin3D.fbx"),
        use_selection=True, object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True,
        mesh_smooth_type="FACE", use_metadata=False)
    target.write_text(json.dumps(expected, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    SOURCE.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    print("PASS Last Route authored coin and contained pile published")


if __name__ == "__main__":
    main()
