"""The mother's ordinary wool scarf: folded prop, skinned wrap and cloth tail.

The production hero supplies the actual rest skeleton. No hero mesh/action is
published. All surfaces are authored here; Unity only skins/simulates them.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Resources/Player/Scarf"
SOURCE = ROOT / "ArtSource/PlayerScarf"
HERO = ROOT / "ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend"
SEGMENTS = 32
TAIL_ROWS = 20
TAIL_COLUMNS = 6
TAIL_LENGTH = .45
TOP = 1.604
BOTTOM = 1.429
GRIP_RAISED = (0.0, -0.148, TOP)
GRIP_LOWERED = (0.0, -0.094, 1.474)
CLOTH_TINT = (2.0, 1.0, .045, 1.0)


def stable(value):
    if isinstance(value, float):
        return round(value, 7)
    if isinstance(value, (tuple, list)):
        return [stable(item) for item in value]
    if isinstance(value, dict):
        return {key: stable(item) for key, item in value.items()}
    return value


def atlas_uv(u, v):
    # BookCloth, bottom-left of the existing mother's-house positive atlas.
    # An inset keeps bilinear taps within the woven tile. CLOTH_TINT
    # compensates its blue source colour to make the finished scarf yellow.
    inset = 3.0 / 1254.0
    return inset + (0.25 - 2 * inset) * u, inset + (0.25 - 2 * inset) * v


def mesh_object(name, vertices, faces, uv, material, rig=None, weights=None):
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(material)
    layer = mesh.uv_layers.new(name="UVMap")
    for face in mesh.polygons:
        face.use_smooth = True
        for loop in face.loop_indices:
            layer.data[loop].uv = atlas_uv(*uv[mesh.loops[loop].vertex_index])
    if rig is not None:
        obj.parent = rig
        modifier = obj.modifiers.new("ProductionHeroSkin", "ARMATURE")
        modifier.object = rig
        groups = {bone: obj.vertex_groups.new(name=bone) for bone in ("head", "neck", "chest")}
        for index, row in enumerate(weights):
            for bone, weight in row.items():
                if weight > 0:
                    groups[bone].add([index], weight, "REPLACE")
    return obj


def wrap(material, rig):
    vertices, lowered, uv, weights, faces = [], [], [], [], []
    # Fourteen rings capture the overlapping winding ridges in real geometry.
    rows = 14
    for row in range(rows):
        v = row / (rows - 1)
        for column in range(SEGMENTS + 1):
            u = column / SEGMENTS
            angle = math.tau * u
            front = max(0.0, -math.sin(angle))
            fold = math.sin(v * math.pi * 7 + math.cos(angle) * 0.48) * 0.0035
            radius_x = 0.095 + 0.021 * v + fold
            radius_y = 0.084 + 0.039 * v + 0.013 * front + fold
            z = BOTTOM + (TOP - BOTTOM) * v - 0.022 * (1 - front) * v
            y = math.sin(angle) * radius_y - .012 * front * v
            vertices.append((math.cos(angle) * radius_x, y, z))
            # The front and cheek folds descend onto the neck; the knot and
            # back winding stay fixed. This opens the mouth without a fade.
            lower_weight = max(0.0, (front + 0.28) / 1.28) ** 0.55
            low_z = z - 0.130 * v * lower_weight
            low_y = math.sin(angle) * (radius_y - 0.054 * v * lower_weight) - .012 * front * v
            lowered.append((math.cos(angle) * (radius_x - 0.017 * v * lower_weight), low_y, low_z))
            uv.append((u, v))
            head = min(1.0, max(0.0, (v - 0.15) / 0.55))
            weights.append({"head": head, "neck": 1 - head})
    for row in range(rows - 1):
        for col in range(SEGMENTS):
            a = row * (SEGMENTS + 1) + col
            b = a + 1
            c = a + SEGMENTS + 1
            faces.extend(((a, b, c + 1), (a, c + 1, c)))
    obj = mesh_object("ScarfWrap", vertices, faces, uv, material, rig, weights)
    obj.shape_key_add(name="Basis")
    key = obj.shape_key_add(name="MouthLowered")
    for point, co in zip(key.data, lowered):
        point.co = co
    return obj


def tail(material, rig):
    vertices, uv, faces = [], [], []
    for row in range(TAIL_ROWS + 1):
        v = row / TAIL_ROWS
        for column in range(TAIL_COLUMNS + 1):
            u = column / TAIL_COLUMNS
            x = (u - 0.5) * (0.082 + 0.025 * v) + 0.028 * v
            y = 0.125 + 0.065 * v + 0.011 * math.sin(u * math.pi * 3) * v
            z = 1.567 - TAIL_LENGTH * v + 0.009 * math.sin(u * math.pi) * v
            vertices.append((x, y, z))
            uv.append((u, v))
    for row in range(TAIL_ROWS):
        for col in range(TAIL_COLUMNS):
            a = row * (TAIL_COLUMNS + 1) + col
            c = a + TAIL_COLUMNS + 1
            faces.extend(((a, c, c + 1), (a, c + 1, a + 1)))
    return mesh_object("ScarfTail", vertices, faces, uv, material, rig,
                       [{"head": 1.0}] * len(vertices))


def knot(material, rig):
    vertices, uv, faces = [], [], []
    for row in range(7):
        v = row / 6
        radius = 0.011 + math.sin(math.pi * v) * 0.018
        for col in range(13):
            a = math.tau * col / 12
            vertices.append((math.cos(a) * radius, 0.120 + v * 0.044,
                             1.571 + math.sin(a) * radius * 0.72))
            uv.append((col / 12, v))
    for row in range(6):
        for col in range(12):
            a = row * 13 + col
            faces.extend(((a, a + 13, a + 14), (a, a + 14, a + 1)))
    for row in (0, 6):
        centre = len(vertices)
        vertices.append((0, .120 + row / 6 * .044, 1.571))
        uv.append((.5, row / 6))
        for col in range(12):
            a = row * 13 + col
            faces.append((centre, a, a + 1) if row == 0 else (centre, a + 1, a))
    return mesh_object("ScarfKnot", vertices, faces, uv, material, rig,
                       [{"head": 1.0}] * len(vertices))


def folded(material):
    # One broad continuous fabric strip folded back over itself four times.
    path = [(-.115, .008), (.115, .008), (.131, .012), (.134, .022),
            (.119, .029), (-.112, .029), (-.131, .035), (-.131, .046),
            (-.114, .052), (.109, .052), (.124, .058), (.119, .068),
            (.102, .071), (-.080, .071)]
    vertices, uv, faces = [], [], []
    for row, (x, z) in enumerate(path):
        for col in range(9):
            u = col / 8
            y = (u - .5) * .177
            ripple = math.sin(u * math.pi * 3 + row * .35) * .0025
            vertices.append((x + .004 * math.cos(u * math.pi * 2), y, z + ripple))
            uv.append((u, row / (len(path) - 1)))
    for row in range(len(path) - 1):
        for col in range(8):
            a = row * 9 + col
            faces.extend(((a, a + 9, a + 10), (a, a + 10, a + 1)))
    return mesh_object("ScarfFolded", vertices, faces, uv, material)


def export(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={"MESH", "ARMATURE"}, use_mesh_modifiers=False,
        add_leaf_bones=False, bake_anim=False, axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE", use_metadata=False)


def geometry_record(obj):
    vertices = [tuple(vertex.co) for vertex in obj.data.vertices]
    triangles = sum(len(face.vertices) - 2 for face in obj.data.polygons)
    return {"name": obj.name, "vertices": len(vertices), "triangles": triangles,
            "bounds_min": [min(v[i] for v in vertices) for i in range(3)],
            "bounds_max": [max(v[i] for v in vertices) for i in range(3)],
            "geometry_sha256": hashlib.sha256(json.dumps(stable({"vertices": vertices,
                "faces": [tuple(face.vertices) for face in obj.data.polygons]}),
                sort_keys=True).encode()).hexdigest()}


def preview(parts, prop):
    scene = bpy.context.scene
    for obj in list(bpy.data.objects):
        if obj.type == "LIGHT":
            bpy.data.objects.remove(obj, do_unlink=True)
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (.12, .12, .12)
    scene.view_settings.view_transform = "Standard"
    camera_data = bpy.data.cameras.new("Scarf Review Camera")
    camera = bpy.data.objects.new("Scarf Review Camera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = .69
    scene.camera = camera
    for name, location, energy, size in (("Soft Key", (1, -2, 3), 180, 3),
                                         ("Soft Fill", (-2, 1, 2), 100, 2)):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.shape, data.size = energy, "DISK", size
        light = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(light)
        light.location = location
        light.rotation_euler = (Vector((0, 0, 1.5)) - light.location).to_track_quat("-Z", "Y").to_euler()
    prop.hide_render = True
    for name, location, lowered in (("WornFront", (.7, -2, 1.75), 0),
                                     ("LoweredFront", (.7, -2, 1.75), 1),
                                     ("WornBack", (-.7, 2, 1.75), 0)):
        parts[0].data.shape_keys.key_blocks["MouthLowered"].value = lowered
        camera.data.ortho_scale = .85 if name == "WornBack" else .69
        target_height = 1.45 if name == "WornBack" else 1.50
        camera.location = location
        camera.rotation_euler = (Vector((0, 0, target_height)) - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(SOURCE / (name + ".png"))
        bpy.ops.render.render(write_still=True)
    parts[0].data.shape_keys.key_blocks["MouthLowered"].value = 0
    prop.hide_render = False


def validate_face_fit(wrap_object):
    """Measure the actual curved face under the garment, including its nose."""
    face = bpy.data.objects["GEO_FaceSurface"]
    vertices = [wrap_object.matrix_world @ vertex.co for vertex in wrap_object.data.vertices]
    surface = BVHTree.FromPolygons(vertices,
        [tuple(polygon.vertices) for polygon in wrap_object.data.polygons], all_triangles=True)
    gaps = []
    for vertex in face.data.vertices:
        point = face.matrix_world @ vertex.co
        if not 1.50 <= point.z <= 1.59:
            continue
        origin = Vector((0, 0, point.z))
        direction = point - origin
        radius = direction.length
        hit, _, _, distance = surface.ray_cast(origin, direction.normalized(), .5)
        assert hit is not None, "The scarf leaves a lower-face coverage hole"
        gap = distance - radius
        assert gap > .002, f"The actual face pierces the scarf by {-gap:.5f}m"
        gaps.append(gap)
    assert len(gaps) >= 14, "No real face surface was measured"
    lowered = wrap_object.data.shape_keys.key_blocks["MouthLowered"].data
    mouth_vertices = [point.co for point in lowered if abs(point.co.x) < .075 and point.co.y < -.055]
    assert max(point.z for point in mouth_vertices) < 1.51, "Lowered folds still block the mouth"
    return min(gaps)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    bpy.ops.wm.open_mainfile(filepath=str(HERO))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    rig.animation_data_clear()
    for pose in rig.pose.bones:
        pose.matrix_basis.identity()
    bpy.context.view_layer.update()
    material = bpy.data.materials.new("ScarfBookCloth")
    material.diffuse_color = CLOTH_TINT
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = .92
    atlas = bpy.data.images.load(str(ROOT / "Assets/Resources/MothersHouse/Textures/MothersHousePositiveAtlas.png"))
    node = material.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = atlas
    multiply = material.node_tree.nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1
    multiply.inputs[2].default_value = CLOTH_TINT
    material.node_tree.links.new(node.outputs["Color"], multiply.inputs[1])
    material.node_tree.links.new(multiply.outputs[0], bsdf.inputs["Base Color"])
    parts = [wrap(material, rig), knot(material, rig), tail(material, rig)]
    prop = folded(material)
    bpy.context.view_layer.update()
    minimum_face_gap = validate_face_fit(parts[0])
    records = [geometry_record(obj) for obj in parts + [prop]]
    manifest = stable({"generator": "tools/build-player-scarf-3d-model.py", "version": 1,
        "generator_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "source_hero_sha256": hashlib.sha256(HERO.read_bytes()).hexdigest(),
        "source_hero": str(HERO.relative_to(ROOT)).replace("\\", "/"),
        "coordinate_space": "Blender hero metres, forward -Y; Unity swaps Y/Z then hero yaw 180",
        "atlas_resource": "MothersHouse/Textures/MothersHousePositiveAtlas", "atlas_cell": "BookCloth",
        "cloth_tint_linear": CLOTH_TINT,
        "minimum_lower_face_clearance_m": minimum_face_gap,
        "skin_bones": ["head", "neck"], "mouth_blend_shape": "MouthLowered",
        "front_grip_raised": GRIP_RAISED, "front_grip_lowered": GRIP_LOWERED,
        "tail_columns": TAIL_COLUMNS, "tail_rows": TAIL_ROWS, "tail_length_m": TAIL_LENGTH,
        "pinned_source_z_min": 1.56, "parts": records,
        "mouth_shape_sha256": hashlib.sha256(json.dumps(stable([tuple(v.co) for v in
             parts[0].data.shape_keys.key_blocks["MouthLowered"].data])).encode()).hexdigest()})
    assert sum(row["triangles"] for row in records) < 1800
    assert records[-1]["bounds_max"][2] < .09
    assert parts[0].data.shape_keys.key_blocks["MouthLowered"].data[13 * 33 + 24].co.z < 1.50
    for obj in parts:
        for vertex in obj.data.vertices:
            assert abs(sum(g.weight for g in vertex.groups) - 1) < 1e-5
    target = OUT / "PlayerScarf3D.json"
    if args.validate_only:
        assert json.loads(target.read_text(encoding="utf-8")) == manifest, "Scarf manifest is stale"
        print("PASS scarf geometry, rig weights, lowered mouth, atlas and determinism")
        return
    OUT.mkdir(parents=True, exist_ok=True)
    SOURCE.mkdir(parents=True, exist_ok=True)
    export(OUT / "ScarfWorn.fbx", [rig] + parts)
    export(OUT / "ScarfFolded.fbx", [prop])
    target.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if not args.no_preview:
        preview(parts, prop)
    # The standalone editable source contains only this asset and the same rig.
    for obj in list(bpy.data.objects):
        if obj not in [rig, prop] + parts:
            bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "PlayerScarf3D.blend"))
    print("PASS published scarf: " + str(sum(row["triangles"] for row in records)) + " triangles")


if __name__ == "__main__":
    main()
