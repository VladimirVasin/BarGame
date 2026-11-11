#!/usr/bin/env python3
"""Build the shared three-segment construction edge profile."""

import argparse
from pathlib import Path
import sys

import bmesh
import bpy

ROOT = Path(__file__).resolve().parents[1]
NAME = "ConstructionJointProfile"


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = bpy.data.meshes.new(NAME)
    source = bmesh.new()
    try:
        bmesh.ops.create_cube(source, size=1.0)
        bmesh.ops.bevel(source, geom=list(source.edges), offset=.125,
                       segments=3, affect="EDGES", profile=.5)
        source.normal_update()
        assert all(edge.is_manifold for edge in source.edges)
        assert 0.9 < source.calc_volume(signed=True) < 1
        source.to_mesh(mesh)
    finally:
        source.free()
    assert all(max(v.co[axis] for v in mesh.vertices) == .5 and
               min(v.co[axis] for v in mesh.vertices) == -.5 for axis in range(3))
    # Constant chart per polygon. The runtime evaluates its metric UVs after
    # fitting the profile, including when a beam is rotated in the world.
    chart = mesh.uv_layers.new(name="ProjectionAxis")
    for face in mesh.polygons:
        axis = max(range(3), key=lambda i: abs(face.normal[i]))
        unity_axis = (0, 2, 1)[axis]
        for loop in face.loop_indices:
            chart.uv[loop].vector = (unity_axis * .5, 0)
    obj = bpy.data.objects.new(NAME, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    return mesh


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Resources/Environment")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/Environment")
    parser.add_argument("--validate-only", action="store_true")
    options = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    mesh = build()
    assert 24 < len(mesh.vertices) <= 128
    if not options.validate_only:
        options.model_dir.mkdir(parents=True, exist_ok=True)
        options.source_dir.mkdir(parents=True, exist_ok=True)
        bpy.ops.export_scene.fbx(filepath=str(options.model_dir / (NAME + ".fbx")),
                                use_selection=True, object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
                                bake_anim=False, use_mesh_modifiers=False, add_leaf_bones=False)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(options.source_dir / (NAME + ".blend")), check_existing=False)
    print("CONSTRUCTION JOINT PROFILE OK")


if __name__ == "__main__":
    main()
