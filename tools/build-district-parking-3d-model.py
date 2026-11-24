#!/usr/bin/env python3
"""A reusable four-bay parking surface with two flush street approaches."""
import argparse
import hashlib
import json
from pathlib import Path

import sys

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from bar_parts import signed_volume, to_source, u_box

ROOT = Path(__file__).resolve().parent.parent


def geometry():
    pieces = {"Asphalt": [], "Curbs": [], "Markings": []}

    def box(role, x0, x1, z0, z1, y, depth, bevel):
        pieces[role].append(u_box(
            ((x0 + x1) / 2, y - depth / 2, (z0 + z1) / 2),
            (x1 - x0, depth, z1 - z0), bevel))

    box("Asphalt", -6.2, 8.6, -7.4, 7.4, 0, 1.1, .015)
    for a, b in ((-10.8, -7.4), (7.4, 10.8)):
        # Stations keep the real wheel plane continuous across a grade break.
        count = 7
        for i in range(count):
            z0 = a + (b - a) * i / count
            z1 = a + (b - a) * (i + 1) / count
            box("Asphalt", -4.95, .1, z0, z1, 0, .65, 0)
    for x, a, b in ((8.5, -7.4, 7.4), (-6.1, -7.4, -1.1), (-6.1, 1.1, 7.4)):
        count = round(b - a)
        for i in range(count):
            start, end = a + (b - a) * i / count, a + (b - a) * (i + 1) / count
            box("Curbs", x - .09, x + .09, start + .005, end - .005, .105, .16, .016)
    for i in range(5):
        z = (i - 2) * 2.9
        box("Markings", 2.65, 8.43, z - .045, z + .045, .005, .004, .001)
    box("Markings", 8.38, 8.47, -5.8, 5.8, .006, .004, .001)
    return pieces


def export_parking(pieces, manifest):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/City/DistrictParking")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/City/DistrictParking")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    manifest_text = json.dumps(manifest, indent=2) + "\n"
    if args.validate_only:
        assert (args.model_dir / "DistrictParking.json").read_text() == manifest_text
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    root = bpy.data.objects.new("DistrictParking", None)
    bpy.context.collection.objects.link(root)
    for role, solids in pieces.items():
        vertices, faces = [], []
        for solid in solids:
            points, polygons = to_source(solid)
            offset = len(vertices)
            vertices.extend(points)
            faces.extend(tuple(offset + i for i in face) for face in polygons)
        mesh = bpy.data.meshes.new(role)
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        for polygon in mesh.polygons:
            major = max(range(3), key=lambda axis: abs(polygon.normal[axis]))
            u, v = [axis for axis in range(3) if axis != major]
            for loop in polygon.loop_indices:
                point = mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv = (point[u] * .5, point[v] * .5)
        obj = bpy.data.objects.new(role, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / "DistrictParking.fbx"), use_selection=True,
        object_types={"MESH", "EMPTY"}, axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False, add_leaf_bones=False, mesh_smooth_type="FACE")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "DistrictParking.blend"), compress=True)
    (args.model_dir / "DistrictParking.json").write_text(manifest_text)


if __name__ == "__main__":
    pieces = geometry()
    assert all(signed_volume(solid) > 0 and signed_volume(to_source(solid)) > 0
               for solids in pieces.values() for solid in solids)
    assert pieces == geometry()
    manifest = dict(unit="metre", bay_count=4, bay_width=2.9, bay_length=5.8,
                    core=[-6.2, -7.4, 8.6, 7.4], entrance=[-4.95, .1, 10.8],
                    geometry_sha256=hashlib.sha256(json.dumps(pieces, sort_keys=True).encode()).hexdigest())
    export_parking(pieces, manifest)
