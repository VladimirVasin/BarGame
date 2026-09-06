#!/usr/bin/env python3
"""Deterministic fixed-metre rock strata for the existing Alpine Village bowl.

Four closed, deeply backed masses follow the physical ridge's 3.6:1 rise.
Their broken ledges carry separate shallow snow deposits. No collision, text,
lights, animation or terrain is exported. Authored Unity +Z points into rock;
the origin sits on the buried foot and every coordinate is in real metres.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
VERSION = "1.0.0"
DESIGN_ID = "alpine_village_bedded_rock_v1"
RISE = 3.6
WIDTHS = (21.0, 24.0, 19.0, 22.5)
HEIGHTS = (45.0, 47.0, 42.0, 46.0)
MAX_TRIANGLES = 5000


def add_solid(target, front_low, front_high, back_low, back_high, fracture=None):
    """A cross-section sweep; vertices and faces remain in Unity metres."""
    vertices, faces = target
    offset = len(vertices)
    count = len(front_low)
    vertices.extend(front_low + front_high + back_low + back_high)
    if fracture is not None:
        vertices.extend(fracture)
    local_faces = []
    for i in range(count - 1):
        front_faces = [(i, i + 1, count + i + 1, count + i)]
        if fracture is not None:
            middle = 4 * count
            front_faces = [
                (i, i + 1, middle + i + 1), (i, middle + i + 1, middle + i),
                (middle + i, middle + i + 1, count + i),
                (middle + i + 1, count + i + 1, count + i),
            ]
        local_faces.extend(front_faces + [
            (count + i, count + i + 1, 3 * count + i + 1, 3 * count + i),
            (2 * count + i + 1, 2 * count + i, 3 * count + i, 3 * count + i + 1),
            (i + 1, i, 2 * count + i, 2 * count + i + 1),
        ])
    if fracture is None:
        local_faces.extend([(0, count, 3 * count, 2 * count),
                            (count - 1, 3 * count - 1, 4 * count - 1, 2 * count - 1)])
    else:
        local_faces.extend([(0, 4 * count, count, 3 * count, 2 * count),
                            (count - 1, 3 * count - 1, 4 * count - 1,
                             2 * count - 1, 5 * count - 1)])
    # The XZ cross-section sweep winds inward; reverse it before export's
    # separate handedness conversion. Validation measures each closed solid.
    outward_faces = [tuple(reversed(face)) for face in local_faces]
    assert volume(vertices[offset:], outward_faces) > 0.0001, "Inverted individual rock/snow solid"
    faces.extend(tuple(offset + v for v in face) for face in outward_faces)


def make_variant(variant):
    rng = random.Random(90173 + variant * 1301)
    width, height = WIDTHS[variant], HEIGHTS[variant]
    stone, snow = ([], []), ([], [])
    levels = (0.0, 0.20, 0.43, 0.71, 1.0)
    for bed in range(4):
        # Five unequal cleavage planes replace an even row of boxes.
        xs = [-width * 0.5]
        xs.extend(width * (-0.5 + (i + rng.uniform(-0.20, 0.20)) / 5)
                  for i in range(1, 5))
        xs.append(width * 0.5)
        front_low, front_high, back_low, back_high, fracture = [], [], [], [], []
        bed_tilt = rng.uniform(-0.20, 0.20)
        for i, x in enumerate(xs):
            taper = (0.76 + bed * 0.035) if i in (0, len(xs) - 1) else 1.0
            lower = levels[bed] * height
            if bed:
                lower += rng.uniform(-0.8, 0.7) + x * bed_tilt
            upper = levels[bed + 1] * height + rng.uniform(-1.1, 0.7) + x * bed_tilt
            lower_z = max(0.0, lower / RISE - rng.uniform(0.20, 0.55))
            upper_z = max(0.16, upper / RISE - rng.uniform(1.40, 2.30))
            front_low.append((x * taper, lower, lower_z))
            high_x = x * (0.78 + bed * 0.04)
            front_high.append((high_x, upper, upper_z))
            back_low.append((x * taper, lower, lower / RISE + 4.1))
            back_high.append((high_x, upper - 0.32, upper / RISE + 4.1))
            amount = rng.uniform(0.35, 0.65)
            middle_y = lower + (upper - lower) * amount
            fracture.append((x * (0.80 + bed * 0.035), middle_y,
                             max(0.02, middle_y / RISE - rng.uniform(1.15, 2.0))))
        add_solid(stone, front_low, front_high, back_low, back_high, fracture)

        # Each snow cap grows out of an actual rock ledge. Broken ends and a
        # domed centre keep the snow from reading as repeated white boards.
        for start, end in ((0, 2), (3, 5)):
            if (variant + bed + start) % 4 == 1:
                continue
            cap_low, cap_high, cap_back_low, cap_back_high = [], [], [], []
            for i in range(start, end + 1):
                x, y, z = front_high[i]
                edge = i in (start, end)
                lip = 0.14 if edge else rng.uniform(0.55, 0.80)
                depth = rng.uniform(1.50, 2.10)
                cap_low.append((x, y - 0.03, z + 0.10))
                cap_high.append((x, y + lip, z - 0.05))
                cap_back_low.append((x, y - 0.13, z + depth))
                cap_back_high.append((x, y + lip * 0.45 - 0.02, z + depth))
            add_solid(snow, cap_low, cap_high, cap_back_low, cap_back_high)
    return [(f"GEO_VillageRock_Variant{variant:02d}_{role}", variant, role, geom)
            for role, geom in (("Stone", stone), ("Snow", snow))]


def make_parts():
    return [part for variant in range(4) for part in make_variant(variant)]


def triangles(faces):
    for face in faces:
        for i in range(1, len(face) - 1):
            yield (face[0], face[i], face[i + 1])


def volume(vertices, faces):
    return sum(Vector(vertices[a]).dot(Vector(vertices[b]).cross(Vector(vertices[c])))
               for a, b, c in triangles(faces)) / 6.0


def bounds(vertices):
    return ([min(v[i] for v in vertices) for i in range(3)],
            [max(v[i] for v in vertices) for i in range(3)])


def signature(parts):
    payload = json.dumps((VERSION, DESIGN_ID, parts), separators=(",", ":"), sort_keys=True)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def validate(parts):
    assert len(parts) == 8 and len({part[0] for part in parts}) == 8
    total = 0
    for name, variant, role, (vertices, faces) in parts:
        low, high = bounds(vertices)
        assert all(math.isfinite(c) for v in vertices for c in v), name
        assert low[0] >= -13 and high[0] <= 13 and low[2] >= 0 and high[2] <= 19, (name, low, high)
        assert low[1] >= -0.1 and high[1] <= 49, (name, low, high)
        assert volume(vertices, faces) > 0.0001, (name, volume(vertices, faces))
        # Every edge must occur twice with opposite orientation. This catches
        # open backs and inverted individual caps, not merely an overall sign.
        directed = {}
        for face in faces:
            for a, b in zip(face, face[1:] + face[:1]):
                directed[(a, b)] = directed.get((a, b), 0) + 1
        assert all(count == directed.get((b, a), 0)
                   for (a, b), count in directed.items()), name
        for a, b, c in triangles(faces):
            assert (Vector(vertices[b]) - Vector(vertices[a])).cross(
                Vector(vertices[c]) - Vector(vertices[a])).length > 0.00001, name
        total += sum(1 for _ in triangles(faces))
    assert total <= MAX_TRIANGLES, total
    assert signature(parts) == signature(make_parts()), "Non-deterministic rock library"
    return total


def make_mesh(name, role, geometry, root):
    vertices, faces = geometry
    mesh = bpy.data.meshes.new(name)
    # Swapping Unity Y/Z reflects handedness: reverse every face once.
    mesh.from_pydata([(x, z, y) for x, y, z in vertices], [],
                     [tuple(reversed(face)) for face in faces])
    mesh.update(calc_edges=True)
    uv = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        # UVs are authored in metres. Runtime applies the existing recipe's
        # pitch once; no descriptor scaling or per-instance material exists.
        normal = poly.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        axes = (0, 1) if axis == 2 else ((0, 2) if axis == 1 else (1, 2))
        for index in poly.loop_indices:
            p = mesh.vertices[mesh.loops[index].vertex_index].co
            uv.data[index].uv = (p[axes[0]], p[axes[1]])
    material = bpy.data.materials.get(role)
    if material is None:
        material = bpy.data.materials.new(role)
        material.diffuse_color = ((0.24, 0.26, 0.25, 1) if role == "Stone"
                                  else (0.64, 0.67, 0.65, 1))
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = root
    return obj


def render_preview(path, objects):
    # Keep review copies separate from source/export selection.
    for obj in objects:
        obj.hide_render = True
        clone = obj.copy()
        clone.data = obj.data
        clone.parent = None
        bpy.context.scene.collection.objects.link(clone)
        variant = int(obj.name.split("Variant")[1][:2])
        clone.location.x = (variant - 1.5) * 25.0
        clone.hide_render = False
    camera_data = bpy.data.cameras.new("Preview Camera")
    camera = bpy.data.objects.new("Preview Camera", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    camera.location = (79, -110, 67)
    camera.rotation_euler = (Vector((0, 7, 22)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 119
    bpy.context.scene.camera = camera
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.background_type = "WORLD"
    scene.world.color = (0.1, 0.1, 0.1)
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 880
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    path.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=ROOT / "Assets/Village/Models")
    parser.add_argument("--source-dir", type=Path, default=ROOT / "ArtSource/Village")
    parser.add_argument("--preview", type=Path, default=ROOT / "Captures/AlpineVillage/rock-library.png")
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    parts = make_parts()
    count = validate(parts)
    build_signature = signature(parts)
    if not args.validate_only:
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        root = bpy.data.objects.new("VillageRocks3D", None)
        bpy.context.scene.collection.objects.link(root)
        objects = [make_mesh(name, role, geometry, root) for name, _, role, geometry in parts]
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        bpy.ops.object.select_all(action="DESELECT")
        root.select_set(True)
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(
            filepath=str(args.model_dir / "VillageRocks3D.fbx"), use_selection=True,
            object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
            apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
            add_leaf_bones=False, bake_anim=False, mesh_smooth_type="FACE")
        manifest = dict(generator="tools/build-village-rocks-3d-model.py",
                        generator_version=VERSION, design_id=DESIGN_ID,
                        build_signature=build_signature, scale_mode="fixed_metres",
                        ridge_rise_per_metre=RISE, variant_count=4, mesh_count=8,
                        triangle_count=count, colliders=False, lights=False,
                        cameras=False, animation_count=0, uv_mode="projected_metres",
                        parts=[dict(mesh=name, variant=variant, role=role,
                                    bounds_min_unity=bounds(geometry[0])[0],
                                    bounds_max_unity=bounds(geometry[0])[1],
                                    triangles=sum(1 for _ in triangles(geometry[1])))
                               for name, variant, role, geometry in parts])
        (args.model_dir / "VillageRocks3D.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "VillageRocks3D.blend"))
        if not args.no_preview:
            render_preview(args.preview, objects)
    print(f"VILLAGE ROCKS 3D VALIDATION OK: 4 variants, 8 meshes, {count} triangles")
    print(f"Signature: {build_signature}; repeated signatures match; all solids closed and outward")


if __name__ == "__main__":
    main()
