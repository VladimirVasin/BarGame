#!/usr/bin/env python3
"""Passive portal stones, bridge profiles and parapet coping at existing metres.

The aperture is the existing 8 x 5.5 m rectangle; no arch, door, cap or collider
is introduced. Bridge/coping roles fit their old unit-box envelopes. Runtime
retains every old collision mesh and bakes the existing metre UV projection.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import sys
from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp

NAME = "MountainRoadStructureDetails3D"
DESIGN = "mountain_road_structure_details_v1"
VERSION = "1.0.0"
MODEL_DIR = ROOT / "Assets/Resources/MountainRoad"
SOURCE_DIR = ROOT / "ArtSource/MountainRoad/StructureDetails"


def closed(solid):
    vertices, faces = solid
    volume = bp.signed_volume(solid)
    if abs(volume) < 1e-9:
        raise ValueError("Degenerate mountain structure solid")
    return vertices, faces if volume > 0 else [tuple(reversed(face)) for face in faces]


def box(center, size, bevel=.016):
    return closed(bp.u_box(center, size, bevel))


def merge(solids):
    return kit.merge_all(closed(solid) for solid in solids)


def girder():
    pieces = [box((0, 0, 0), (.16, .76, 1), .008)]
    for y in (-.43, .43):
        pieces.append(box((0, y, 0), (1, .14, 1), .008))
    for z in (-.499, .499):
        pieces.append(box((0, 0, z), (1, 1, .002), .0002))
    return merge(pieces)


def portal():
    # A modest jointed face on the old straight-sided rock entrance. The
    # underlying nine-metre shell, lighting and uncapped bend remain intact.
    pieces = []
    for side in (-1, 1):
        for course in range(8):
            bottom = course * 5.5 / 8
            top = (course + 1) * 5.5 / 8
            depth = .085 + (course % 3) * .014
            pieces.append(box((side * 4.36, (bottom + top) * .5, depth * .5),
                (.72, top - bottom - .018, depth), .014))
    for block in range(9):
        left, right = -4.72 + block * 9.44 / 9, -4.72 + (block + 1) * 9.44 / 9
        depth = .09 + (block % 2) * .022
        pieces.append(box(((left + right) * .5, 5.86, depth * .5),
            (right - left - .018, .72, depth), .017))
    return merge(pieces)


def geometry():
    longitudinal = girder()
    vertices, faces = longitudinal
    return {"Portal": portal(), "GirderZ": longitudinal,
            "GirderX": ([(-z, y, x) for x, y, z in vertices], faces),
            "Coping": box((0, 0, 0), (1, 1, 1), .045),
            "Plain": box((0, 0, 0), (1, 1, 1), 0)}


def signature(parts):
    return hashlib.sha256(json.dumps(parts, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def validate(parts):
    for role, solid in parts.items():
        if bp.signed_volume(solid) <= 0 or bp.signed_volume(bp.to_source(solid)) <= 0:
            raise ValueError("Inward structure detail " + role)
        if role != "Portal":
            low, high = kit.bounds(solid)
            if low != (-.5, -.5, -.5) or high != (.5, .5, .5):
                raise ValueError("Structure profile changed its unit envelope " + role)
        else:
            # Every triangle stays outside the original clear aperture. The
            # floor, driving ribbon and all tunnel lining remain untouched.
            for x, y, z in solid[0]:
                if abs(x) < 4 - 1e-6 and y < 5.5 - 1e-6:
                    raise ValueError("Portal detail protrudes into the original aperture")
                if z < -1e-6 or z > .12:
                    raise ValueError("Portal face exceeds its bounded relief")
    count = sum(kit.triangle_count(solid) for solid in parts.values())
    if count > 6000:
        raise ValueError("Structure detail triangle budget exceeded")
    return count


def build(args):
    parts = geometry()
    triangles = validate(parts)
    digest = signature(parts)
    if digest != signature(geometry()):
        raise ValueError("Mountain structure details are not deterministic")
    if args.validate_only:
        data = json.loads((args.model_dir / (NAME + ".json")).read_text())
        if data["build_signature"] != digest or data["triangle_count"] != triangles:
            raise ValueError("Published structure details differ from their source")
        print(f"MOUNTAIN STRUCTURE DETAILS VALIDATED: {triangles} triangles; {digest}")
        return
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    root = bpy.data.objects.new(NAME, None)
    bpy.context.collection.objects.link(root)
    objects, records = [], []
    for role, solid in parts.items():
        source = bp.to_source(solid)
        mesh = bpy.data.meshes.new("GEO_RoadStructure_" + role)
        mesh.from_pydata(source[0], [], source[1])
        mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        for face in mesh.polygons:
            axis = max(range(3), key=lambda i: abs(face.normal[i]))
            axes = [i for i in range(3) if i != axis]
            for loop in face.loop_indices:
                point = mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv = (point[axes[0]], point[axes[1]])
        obj = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
        objects.append(obj)
        minimum, maximum = kit.bounds(solid)
        records.append(dict(name=mesh.name, role=role, bounds_min=minimum, bounds_max=maximum,
            triangle_count=kit.triangle_count(solid), signed_volume=bp.signed_volume(solid)))
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    for obj in [root] + objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / (NAME + ".fbx")), use_selection=True,
        object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False, add_leaf_bones=False,
        bake_anim=False, mesh_smooth_type="FACE", path_mode="STRIP")
    payload = dict(design_id=DESIGN, generator_version=VERSION, build_signature=digest,
        mesh_count=len(records), triangle_count=triangles, parts=records,
        aperture_size=[8, 5.5], maximum_template_vertices=16000, maximum_batch_parts=256,
        colliders=False, lights=False, cameras=False, animation_count=0)
    (args.model_dir / (NAME + ".json")).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / (NAME + ".blend")))
    if args.preview:
        for index, obj in enumerate(objects[1:]):
            obj.location = ((index - 1.5) * 2.2, -2.6, .7)
        data = bpy.data.cameras.new("StructureDetailsReview")
        camera = bpy.data.objects.new("StructureDetailsReview", data)
        bpy.context.collection.objects.link(camera)
        camera.location = (10, -15, 10)
        camera.rotation_euler = (Vector((0, 0, 2.4)) - camera.location).to_track_quat("-Z", "Y").to_euler()
        data.type, data.ortho_scale = "ORTHO", 14
        scene = bpy.context.scene
        scene.camera = camera
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.display.shading.light = "STUDIO"
        scene.display.shading.color_type = "SINGLE"
        scene.display.shading.single_color = (.28, .31, .29)
        scene.display.shading.show_cavity = True
        scene.render.resolution_x, scene.render.resolution_y = 1300, 1000
        scene.render.resolution_percentage = 100
        scene.render.filepath = str(args.preview)
        bpy.ops.render.render(write_still=True)
    print(f"MOUNTAIN STRUCTURE DETAILS BUILD OK: {triangles} triangles; {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--preview", type=Path)
    parser.add_argument("--validate-only", action="store_true")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build(parser.parse_args(argv))
