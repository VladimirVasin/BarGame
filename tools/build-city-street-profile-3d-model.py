#!/usr/bin/env python3
"""Deterministic passive City street deformation topology, in Unity metres.

The authored section documents the complete asphalt/kerb/pavement profile.
SurfacePatch and BoundaryStrip are its deformable top/edge topology; runtime
maps these imported vertices onto the immutable street support polygons.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bar_parts as bp
import bpy

ROOT = Path(__file__).resolve().parents[1]

def geometry():
    patch = ([(0, 0, 0), (1, 0, 0), (0, 0, 1)], [(0, 2, 1)])
    strip = ([(0, 0, 0), (1, 0, 0), (0, -1, 0), (1, -1, 0)], [(0, 1, 2), (2, 1, 3)])
    section = []
    for x, width, top, thickness in ((0, 6, .08, .16), (-4, 2, .14, .30), (4, 2, .14, .30)):
        solid = bp.u_box((x, top-thickness/2, .5), (width, thickness, 1), 0)
        assert bp.signed_volume(solid) > 0
        section.append(solid)
    vertices, faces = [], []
    for part_vertices, part_faces in section:
        offset = len(vertices)
        vertices.extend(part_vertices)
        faces.extend(tuple(offset+i for i in face) for face in part_faces)
    return {"SurfacePatch": patch, "BoundaryStrip": strip, "StreetProfile": (vertices, faces)}

def payload(parts):
    serial = json.dumps(parts, sort_keys=True, separators=(",", ":"))
    return {"version": "1.0.0", "signature": hashlib.sha256(serial.encode()).hexdigest(),
            "unity_axes": "Blender (x,z,y), reversed face winding", "asphalt_width": 6,
            "public_sidewalk_width": 2, "road_top": .08, "sidewalk_top": .14,
            "parts": {name: {"vertices": len(verts), "triangles": sum(len(f)-2 for f in faces),
                             "min": [min(p[i] for p in verts) for i in range(3)],
                             "max": [max(p[i] for p in verts) for i in range(3)]}
                      for name, (verts, faces) in parts.items()}}

def main():
    args = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--model-dir", type=Path, default=ROOT/"Assets/Resources/City/StreetProfile")
    parser.add_argument("--source-dir", type=Path, default=ROOT/"ArtSource/City/StreetProfile")
    config = parser.parse_args(args)
    parts = geometry()
    manifest = payload(parts)
    assert manifest == payload(geometry()), "Street profile must be deterministic"
    # The patch points upwards and the strip faces the exterior of a CCW XZ contour.
    from mathutils import Vector
    for name, (verts, faces) in parts.items():
        converted = bp.to_source((verts, faces))
        for face in faces:
            normal = (Vector(verts[face[1]])-Vector(verts[face[0]])).cross(Vector(verts[face[2]])-Vector(verts[face[0]]))
            assert normal.length > 0
            if name == "SurfacePatch": assert normal.y > 0
            if name == "BoundaryStrip": assert normal.z < 0
        if name == "StreetProfile": assert bp.signed_volume(converted) > 0
    if config.validate_only:
        print("CITY STREET PROFILE DIRECT VALIDATION OK " + manifest["signature"])
        return
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for name, part in parts.items():
        verts, faces = bp.to_source(part)
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(verts, [], faces)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
    config.model_dir.mkdir(parents=True, exist_ok=True)
    config.source_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=str(config.model_dir/"CityStreetProfile3D.fbx"),
        use_selection=True, object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options="FBX_SCALE_NONE", bake_space_transform=False, bake_anim=False,
        add_leaf_bones=False, use_mesh_modifiers=False)
    (config.model_dir/"CityStreetProfile3D.json").write_text(json.dumps(manifest, indent=2)+"\n")
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(config.source_dir/"CityStreetProfile3D.blend"), check_existing=False)
    print("CITY STREET PROFILE BUILD OK " + manifest["signature"])

if __name__ == "__main__": main()
