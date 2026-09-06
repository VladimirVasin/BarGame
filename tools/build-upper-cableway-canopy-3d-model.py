#!/usr/bin/env python3
"""Fixed-metre passive upper-station canopy; the station plan owns all collision.

Unity-local station coordinates are converted once through bar_parts.to_source.
The imported hierarchy must retain its FBX unit factor. Four closed role meshes
share existing mountain surfaces; no imported materials, lights or animation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp

VERSION = "1.0.0"
DESIGN = "upper_cableway_canopy_v1"
NAME = "UpperCablewayCanopy3D"
MODEL_DIR = ROOT / "Assets/Resources/Village"
SOURCE_DIR = ROOT / "ArtSource/Village/UpperCablewayCanopy"
SURFACES = {"Steel": ("PaintedMetal", 1.2), "Timber": ("Timber", 1.4),
            "Fasteners": ("RustedIron", 1.2), "Snow": ("WindSnow", 5.0)}
COLORS = {"Steel": (.15, .19, .175, 1), "Timber": (.23, .18, .135, 1),
          "Fasteners": (.34, .23, .15, 1), "Snow": (.68, .69, .66, 1)}


def canonical(geometry):
    vertices, faces = geometry
    volume = bp.signed_volume(geometry)
    if abs(volume) < 1e-8:
        raise ValueError("Degenerate canopy solid")
    if volume < 0:
        faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def roof_height(x, z):
    # Shallow old gable, with a real corrugated profile and a restrained sag.
    fold = abs((x / .22) % 1 - .5) * .070
    return 5.39 - abs(x + .12) * .157 + fold - .022 * math.cos(z * .83)


def skin(xs, zs, top, thickness):
    """Closed sampled sheet, including its real underside and edge faces."""
    vertices = [(x, top(x, z) - down, z)
                for down in (0, thickness) for z in zs for x in xs]
    nx, nz = len(xs), len(zs)
    n = nx * nz
    faces = []
    for z in range(nz - 1):
        for x in range(nx - 1):
            a = z * nx + x
            faces.extend(((a, a + nx, a + nx + 1, a + 1),
                          (a + n, a + n + 1, a + n + nx + 1, a + n + nx)))
    edge = (list(range(nx)) + [z * nx + nx - 1 for z in range(1, nz)] +
            list(range(n - 2, n - nx - 1, -1)) +
            [z * nx for z in range(nz - 2, 0, -1)])
    for i, a in enumerate(edge):
        b = edge[(i + 1) % len(edge)]
        faces.append((a, b, b + n, a + n))
    return canonical((vertices, faces))


def beam(first, second, width, depth):
    delta = Vector(second) - Vector(first)
    # Author a solid along Y and rotate its full geometry to the span.
    solid = bp.u_box((0, 0, 0), (width, delta.length, depth), .008)
    rotation = Vector((0, 1, 0)).rotation_difference(delta)
    center = (Vector(first) + Vector(second)) * .5
    return canonical(([tuple(rotation @ Vector(v) + center) for v in solid[0]], solid[1]))


def snow_patch(left, right, back, front, phase):
    """A wind-loaded island with a feathered lobed boundary, not a snow card."""
    cx, cz = (left + right) * .5, (back + front) * .5
    rx, rz = (right - left) * .5, (front - back) * .5
    segments = 20
    samples = [(cx, cz, 0.0)]
    for radius in (.34, .68, 1.0):
        for step in range(segments):
            angle = step * math.tau / segments
            lobe = 1 + .12 * math.sin(angle * 3 + phase) + .075 * math.sin(angle * 7 - phase)
            samples.append((cx + math.cos(angle) * rx * radius * lobe,
                            cz + math.sin(angle) * rz * radius * lobe, radius))
    vertices = []
    for bottom in (False, True):
        for x, z, radius in samples:
            lift = .018 if bottom else .040 + .16 * (1 - radius * radius)
            vertices.append((x, roof_height(x, z) + lift, z))
    count = len(samples)
    faces = []
    for step in range(segments):
        following = (step + 1) % segments
        faces.append((0, 1 + step, 1 + following))
        faces.append((count, count + 1 + following, count + 1 + step))
        for ring in range(2):
            a, b = 1 + ring * segments + step, 1 + ring * segments + following
            faces.append((a, a + segments, b + segments, b))
            faces.append((a + count, b + count, b + segments + count, a + segments + count))
        a, b = 1 + 2 * segments + step, 1 + 2 * segments + following
        faces.append((a, b, b + count, a + count))
    return canonical((vertices, faces))


def make_geometry():
    parts = {role: [] for role in SURFACES}
    def add(role, geometry):
        parts[role].append(canonical(geometry))

    # Ten overlapping old sheets: edges/seams are geometry, not painted lines.
    for panel in range(10):
        left = -4.45 + panel * .89
        xs = [left + i * .89 / 8 for i in range(9)]
        zs = [-3.02, -1.51, 0, 1.51, 3.02]
        add("Steel", skin(xs, zs, roof_height, .035))
        if panel:
            add("Fasteners", skin([left - .027, left + .027], zs,
                lambda x, z: roof_height(x, z) + .026, .040))

    # Four existing columns end at 4.5 m, x +/-3.95, z +/-2.62.
    for z in (-2.62, 2.62):
        add("Timber", bp.u_box((0, 4.53, z), (8.18, .24, .23), .015))
        for side in (-1, 1):
            add("Timber", beam((side * 4.42, 4.68, z), (-.12, 5.31, z), .18, .22))
            add("Timber", beam((side * 3.95, 4.10, z), (side * 3.35, 4.51, z), .12, .15))
            add("Fasteners", bp.u_box((side * 3.95, 4.47, z), (.39, .17, .35), .012))
            for dz in (-.14, .14):
                add("Fasteners", bp.u_box((side * 3.95, 4.51, z + dz), (.095, .095, .035), .008))
    for x in (-4.32, -2.45, -.12, 2.38, 4.32):
        add("Timber", bp.u_box((x, roof_height(x, 0) - .16, 0), (.16, .20, 6.13), .014))

    # Folded eaves and patched front/back fascia have a visible narrow edge.
    for x in (-4.45, 4.45):
        add("Steel", bp.u_box((x, roof_height(x, 0) - .095, 0), (.08, .20, 6.14), .012))
    for z in (-3.06, 3.06):
        for left, right in ((-4.48, -.12), (-.12, 4.48)):
            add("Steel", beam((left, roof_height(left, z) - .045, z),
                (right, roof_height(right, z) - .045, z), .16, .095))
        add("Fasteners", bp.u_box((-.12, 5.35, z), (.29, .17, .12), .010))

    # Broken loaded patches on the roof, with lumpy mass and unequal ends.
    # No continuous white cap and nothing over the rope, machinery or dock.
    patches = [(-4.43, -2.45, -2.94, .12), (-3.95, -1.42, .38, 2.82),
               (.66, 2.22, -2.66, -1.20), (2.72, 4.47, -.58, 2.96)]
    for index, (left, right, back, front) in enumerate(patches):
        add("Snow", snow_patch(left, right, back, front, index))
    return {role: kit.merge_all(solids) for role, solids in parts.items()}


def signature(geometry):
    packed = json.dumps(geometry, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(packed.encode()).hexdigest()


def build(args):
    geometry = make_geometry()
    sig = signature(geometry)
    if signature(make_geometry()) != sig:
        raise ValueError("Canopy is not deterministic")
    total = sum(kit.triangle_count(g) for g in geometry.values())
    if total > 16000:
        raise ValueError(f"Canopy exceeded 16000 triangles: {total}")
    for role, solid in geometry.items():
        if bp.signed_volume(bp.to_source(solid)) <= 0:
            raise ValueError(f"Inward canopy {role}")
    print(f"CANOPY VALIDATED: {total} triangles, 4 roles, signature {sig}")
    if args.validate_only:
        return

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    root = bpy.data.objects.new(NAME, None)
    bpy.context.collection.objects.link(root)
    records = []
    objects = []
    for role, unity in geometry.items():
        source = bp.to_source(unity)
        mesh = bpy.data.meshes.new("GEO_UpperCanopy_" + role)
        mesh.from_pydata(source[0], [], source[1])
        mesh.update()
        layer = mesh.uv_layers.new(name="UVMap")
        pitch = SURFACES[role][1]
        for polygon in mesh.polygons:
            normal = polygon.normal
            drop = max(range(3), key=lambda axis: abs(normal[axis]))
            axes = [axis for axis in range(3) if axis != drop]
            for loop in polygon.loop_indices:
                vertex = mesh.vertices[mesh.loops[loop].vertex_index].co
                layer.data[loop].uv = (vertex[axes[0]] / pitch, vertex[axes[1]] / pitch)
        obj = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
        obj.color = COLORS[role]
        objects.append(obj)
        minimum, maximum = kit.bounds(unity)
        records.append({"name": mesh.name, "role": role, "surface": SURFACES[role][0],
                        "bounds_min": minimum, "bounds_max": maximum,
                        "triangle_count": kit.triangle_count(unity),
                        "signed_volume": bp.signed_volume(unity)})
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    for obj in [root] + objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / (NAME + ".fbx")),
        use_selection=True, object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
        add_leaf_bones=False, bake_anim=False, mesh_smooth_type="FACE", path_mode="STRIP")
    payload = {"design_id": DESIGN, "generator_version": VERSION, "build_signature": sig,
               "mesh_count": 4, "triangle_count": total, "parts": records,
               "bounds_min": kit.bounds(kit.merge_all(geometry.values()))[0],
               "bounds_max": kit.bounds(kit.merge_all(geometry.values()))[1],
               "station_pad_size": [9, 6.2], "column_top_y": 4.5,
               "origin": "station_root_fixed_unity_metres",
               "colliders": False, "lights": False, "cameras": False, "animation_count": 0}
    (args.model_dir / (NAME + ".json")).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / (NAME + ".blend")))
    if not args.no_preview:
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.display.shading.light = "STUDIO"
        scene.display.shading.color_type = "OBJECT"
        scene.display.shading.show_shadows = True
        scene.display.shading.show_cavity = True
        scene.display.shading.background_type = "WORLD"
        scene.world.color = (.065, .075, .080)
        data = bpy.data.cameras.new("CanopyPreview")
        camera = bpy.data.objects.new("CanopyPreview", data)
        bpy.context.collection.objects.link(camera)
        camera.location = (10, -11, 10)
        camera.rotation_euler = (Vector((0, 0, 4.8)) - camera.location).to_track_quat("-Z", "Y").to_euler()
        data.type = "ORTHO"
        data.ortho_scale = 12
        scene.camera = camera
        scene.render.resolution_x, scene.render.resolution_y = 1100, 760
        scene.render.resolution_percentage = 100
        scene.render.filepath = str(args.source_dir / (NAME + ".png"))
        bpy.ops.render.render(write_still=True)
    print("UPPER CABLEWAY CANOPY BUILD OK")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build(parser.parse_args(argv))
