#!/usr/bin/env python3
"""Author the Home shower action's bridge pieces for the undressed hero.

Blender --background --factory-startup --python this-file -- [--validate-only]

The production Hero V2 has no bare skin under its jacket: the torso mesh
caps flat at the shoulders 40 mm below the neck's first ring, and each bare
upper arm starts 12 % of the way down to the elbow, because the jacket and
its sleeves were authored to bridge exactly those gaps. When the shower
scene hides the jacket, three small rigid pieces close them: a shoulder
yoke (the trapezius dome between the torso cap and the neck) and one
deltoid cap per shoulder. They are placed at runtime from bone POSITIONS
only, never parented to an imported bone, so the 100x authoring root and
the imported bone axes never enter into it. All meshes are Blender
exports; runtime only positions them.

Sizes come from the hero generator's own constants
(tools/build-player-3d-model-v2.py): the torso's top ring is
(z 1.415, rx 0.187, ry 0.109); GEO_Neck starts at z 1.455; the A-pose
shoulder joint is (0.210, -0.004, 1.424) and the bare upper arm's first
ring has radius 0.047. The yoke origin is the torso cap centre; a deltoid's
origin is its shoulder joint.
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
SOURCE = ROOT / "ArtSource/HomeShowerAction"
RESOURCES = ROOT / "Assets/Resources/HomeShowerAction"
MODELS = RESOURCES / "Models"
VERSION = "1.0.0"
COLORS = {"Skin": "AE8D7B", "SkinShadow": "67504B"}

# Hero generator facts the bridge closes over (Unity metres, +Y up).
TORSO_CAP_HALF_WIDTH = 0.187
TORSO_CAP_HALF_DEPTH = 0.109
NECK_BASE_ABOVE_CAP = 0.040
NECK_RADIUS = 0.058
SHOULDER_JOINT_RADIUS = 0.047
DELTOID_RADIUS = 0.058


def source_point(point):
    """Match the project's existing fixed-metre Home FBX Y/Z swap contract."""
    return point[0], point[2], point[1]


def bounds(geometry):
    vertices, _ = geometry
    return ([min(p[i] for p in vertices) for i in range(3)],
            [max(p[i] for p in vertices) for i in range(3)])


def ring_loft(rings, sides=12):
    """Closed profiled, flat-shaded shell stacked along local +Y (Unity up)."""
    vertices = []
    for center, rx, rz in rings:
        for i in range(sides):
            angle = math.tau * i / sides
            vertices.append((center[0] + rx * math.cos(angle),
                             center[1],
                             center[2] + rz * math.sin(angle)))
    faces = [tuple(reversed(range(sides)))]
    for row in range(len(rings) - 1):
        for col in range(sides):
            a = row * sides + col
            b = row * sides + (col + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces.append(tuple(range((len(rings) - 1) * sides, len(rings) * sides)))
    # Rings stacked along Y wind the other way round from the toilet kit's
    # Z-stacked lofts; reverse so every face looks outward after the swap.
    return vertices, [tuple(reversed(face)) for face in faces]


def sphere(radius, rings=5, sides=10):
    """A closed, flat-shaded ball centred on the origin."""
    profile = []
    for step in range(rings + 1):
        latitude = -math.pi / 2 + math.pi * step / rings
        y = radius * math.sin(latitude)
        r = max(radius * math.cos(latitude), radius * 0.08)
        profile.append(((0, y, 0), r, r))
    return ring_loft(profile, sides=sides)


def definitions():
    # The dome sits on the torso cap (local y 0) and rises to meet the neck
    # just above its first ring, overlapping both by a few millimetres so
    # neither seam opens when the head bows.
    yoke = ring_loft([
        ((0, -0.018, 0), TORSO_CAP_HALF_WIDTH + 0.004, TORSO_CAP_HALF_DEPTH + 0.004),
        ((0, 0.000, 0), TORSO_CAP_HALF_WIDTH - 0.012, TORSO_CAP_HALF_DEPTH - 0.004),
        ((0, 0.016, 0), 0.128, 0.090),
        ((0, 0.030, 0), 0.090, 0.076),
        ((0, NECK_BASE_ABOVE_CAP + 0.006, 0), NECK_RADIUS + 0.004, NECK_RADIUS + 0.002),
        ((0, NECK_BASE_ABOVE_CAP + 0.020, 0), NECK_RADIUS, NECK_RADIUS - 0.002),
    ], sides=14)
    deltoid = sphere(DELTOID_RADIUS)
    return {
        "ShoulderYoke": {"meshes": [("ShoulderYoke", yoke, "Skin")],
                         "contract": "fixed metres; origin at the torso cap centre; +Y toward the neck; +X toward the hero's right"},
        "DeltoidLeft": {"meshes": [("DeltoidLeft", deltoid, "SkinShadow")],
                        "contract": "fixed metres; origin at the left shoulder joint"},
        "DeltoidRight": {"meshes": [("DeltoidRight", deltoid, "SkinShadow")],
                         "contract": "fixed metres; origin at the right shoulder joint"},
    }


def canonical(defs):
    data = {"version": VERSION, "palette": COLORS, "definitions": defs}
    return hashlib.sha256(json.dumps(data, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def material(name):
    mat = bpy.data.materials.get("MAT_" + name)
    if mat:
        return mat
    mat = bpy.data.materials.new("MAT_" + name)
    rgb = [int(COLORS[name][i:i+2], 16) / 255 for i in (0, 2, 4)]
    rgba = tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4
                 for v in rgb) + (1,)
    mat.diffuse_color = rgba
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = rgba
    shader.inputs["Roughness"].default_value = .88
    mat["bp_palette_hex"] = COLORS[name]
    return mat


def create_mesh(name, geometry, material_name, parent):
    mesh = bpy.data.meshes.new(name + "_Mesh")
    vertices, faces = geometry
    mesh.from_pydata([source_point(p) for p in vertices], [],
                     [tuple(reversed(face)) for face in faces])
    mesh.update(calc_edges=True)
    uv = mesh.uv_layers.new(name="UVMap")
    for face in mesh.polygons:
        face.use_smooth = False
        for loop_index in face.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv.data[loop_index].uv = (point.x + .5, point.z + .5)
    mesh.materials.append(material(material_name))
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    obj["bp_contract"] = "Blender authored; Unity metres; runtime transform only"
    return obj


def validate(defs, objects):
    assert canonical(defs) == canonical(definitions()), "Authoring is nondeterministic"
    total_triangles = 0
    for name, definition in defs.items():
        for mesh_name, geometry, _ in definition["meshes"]:
            mesh = objects[mesh_name].data
            mesh.calc_loop_triangles()
            assert mesh.vertices and mesh.loop_triangles, mesh_name
            for triangle in mesh.loop_triangles:
                a, b, c = [mesh.vertices[i].co for i in triangle.vertices]
                assert (b-a).cross(c-a).length > 1e-10, (mesh_name, "degenerate face")
            assert all(math.isfinite(v) for point in geometry[0] for v in point)
            assert all(not p.use_smooth for p in mesh.polygons), mesh_name
            total_triangles += len(mesh.loop_triangles)
            # Every face must look outward: a ray from the centroid of a
            # closed shell must hit a face whose normal points away from it.
            centre = sum((v.co for v in mesh.vertices), Vector()) / len(mesh.vertices)
            hit, point, normal, _ = objects[mesh_name].ray_cast(centre, Vector((0, 0, 1)))
            assert hit and (point - centre).dot(normal) > 0, (mesh_name, "inward face")
    assert total_triangles < 600, total_triangles
    lo, hi = bounds(defs["ShoulderYoke"]["meshes"][0][1])
    assert lo[1] < 0 < hi[1], "The yoke must straddle the torso cap"
    assert hi[1] > NECK_BASE_ABOVE_CAP, "The yoke must reach the neck's first ring"
    assert hi[0] - lo[0] > 2 * TORSO_CAP_HALF_WIDTH, "The yoke must cover the torso cap"
    for side in ("DeltoidLeft", "DeltoidRight"):
        lo, hi = bounds(defs[side]["meshes"][0][1])
        assert abs(hi[1] - lo[1] - 2 * DELTOID_RADIUS) < 1e-6, side
        # A ten-sided ring never samples the full circle; nine tenths is
        # what a flat-shaded ball of this radius actually spans.
        assert all(0.9 * 2 * DELTOID_RADIUS < hi[i] - lo[i] <= 2 * DELTOID_RADIUS + 1e-6
                   for i in (0, 2)), side
        assert 2 * DELTOID_RADIUS > SHOULDER_JOINT_RADIUS * 2, "A deltoid must cover the bare arm's first ring"
    return {"models": len(defs), "meshes": len(objects), "triangles": total_triangles,
            "deterministic_geometry": True, "outward_faces": True,
            "yoke_spans_cap_to_neck": True}


def guid(path):
    return hashlib.md5(("HomeShowerAction/" + path.as_posix()).encode()).hexdigest()


def write_meta(path, folder=False, model=False):
    target = Path(str(path) + ".meta")
    if target.exists():
        return
    relative = path.relative_to(ROOT)
    if model:
        # Same importer as the proven Home authored meshes, with unique IDs.
        text = (ROOT / "Assets/Home/Interior/Models/HomeInterior3D.fbx.meta").read_text(encoding="utf-8")
        text = text.replace("guid: a841a60cd444e79438c87fd91713c304", "guid: " + guid(relative))
    else:
        text = "fileFormatVersion: 2\nguid: " + guid(relative) + "\n"
        if folder:
            text += "folderAsset: yes\nDefaultImporter:\n"
        else:
            text += "TextScriptImporter:\n"
        text += "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    target.write_text(text, encoding="utf-8")


def validate_exported_geometry(defs):
    """FBX round-trip catches changed metre scale, hierarchy, pivots or axes."""
    for name, definition in defs.items():
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        bpy.ops.import_scene.fbx(filepath=str(MODELS / (name + ".fbx")),
                                 use_custom_normals=True)
        for mesh_name, geometry, _ in definition["meshes"]:
            obj = next(obj for obj in bpy.context.scene.objects
                       if obj.type == "MESH" and obj.name == mesh_name)
            actual = sorted(tuple(round(c, 5) for c in obj.matrix_world @ vertex.co)
                            for vertex in obj.data.vertices)
            expected = sorted(tuple(round(c, 5) for c in source_point(point))
                              for point in geometry[0])
            assert actual == expected, (name, "FBX vertex/pivot/axis mismatch")


def compose_preview(roots):
    """One true-metre view of the three pieces side by side."""
    collection = bpy.data.collections.new("AUTHORING_PREVIEW")
    bpy.context.scene.collection.children.link(collection)
    positions = {"ShoulderYoke": (0, 0, 0), "DeltoidLeft": (-.32, 0, 0), "DeltoidRight": (.32, 0, 0)}
    for name, root in roots.items():
        root.hide_render = True
        for child in root.children:
            child.hide_render = True
            if child.type != "MESH":
                continue
            duplicate = child.copy()
            duplicate.data = child.data
            duplicate.name = "PREVIEW_" + child.name
            duplicate.parent = None
            collection.objects.link(duplicate)
            duplicate.hide_render = False
            duplicate.location = positions[name]
    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("AuthoringCamera")
    camera = bpy.data.objects.new("AuthoringCamera", camera_data)
    collection.objects.link(camera)
    camera.location = (0, -1.4, .55)
    camera.rotation_euler = (Vector((0, 0, .02)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = .95
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.background_type = "WORLD"
    scene.world.color = (.045, .045, .045)
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 600
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(SOURCE / "HomeShowerAction-Authoring.png")
    bpy.ops.render.render(write_still=True)
    for obj in collection.objects:
        if obj.type == "MESH":
            obj.hide_render = False


def main():
    args_list = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", "--validate", action="store_true")
    parser.add_argument("--preview", action="store_true")
    args = parser.parse_args(args_list)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1
    defs = definitions()
    roots, objects = {}, {}
    for name, definition in defs.items():
        root = bpy.data.objects.new(name + "_Root", None)
        scene.collection.objects.link(root)
        roots[name] = root
        for mesh_name, geometry, mat_name in definition["meshes"]:
            objects[mesh_name] = create_mesh(mesh_name, geometry, mat_name, root)
    bpy.context.view_layer.update()
    report = validate(defs, objects)
    signature = canonical(defs)
    if args.validate_only:
        manifest = json.loads((SOURCE / "home-shower-action-3d-model.json").read_text(encoding="utf-8"))
        assert manifest["signature"] == signature, "Export is stale; regenerate assets"
        for name in defs:
            path = MODELS / (name + ".fbx")
            assert path.exists() and path.stat().st_size > 1000, path
            meta = Path(str(path) + ".meta").read_text(encoding="utf-8")
            for setting in ("globalScale: 1", "bakeAxisConversion: 1", "useFileUnits: 1", "preserveHierarchy: 1"):
                assert setting in meta, (path, setting)
        validate_exported_geometry(defs)
        report["fbx_round_trip_metres_and_axes"] = True
        print("HOME_SHOWER_ACTION_VALIDATED " + json.dumps(report) + " sha256=" + signature, flush=True)
        return
    SOURCE.mkdir(parents=True, exist_ok=True)
    MODELS.mkdir(parents=True, exist_ok=True)
    write_meta(RESOURCES, folder=True)
    write_meta(MODELS, folder=True)
    model_records = []
    for name, definition in defs.items():
        root = roots[name]
        bpy.ops.object.select_all(action="DESELECT")
        root.select_set(True)
        for child in root.children:
            child.select_set(True)
        bpy.context.view_layer.objects.active = root
        path = MODELS / (name + ".fbx")
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
            object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
            apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
            add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True,
            mesh_smooth_type="FACE", use_custom_props=True, path_mode="STRIP",
            embed_textures=False)
        write_meta(path, model=True)
        model_records.append({"name": name, "resource": "HomeShowerAction/Models/" + name,
            "contract": definition["contract"],
            "meshes": [{"name": mesh_name, "material": mat_name,
                        "bounds_min": bounds(geometry)[0], "bounds_max": bounds(geometry)[1],
                        "triangles": len(objects[mesh_name].data.loop_triangles)}
                       for mesh_name, geometry, mat_name in definition["meshes"]]})
    payload = {"schema_version": 1, "generator_version": VERSION,
               "signature": signature, "coordinates": "Unity local metres +Y up +Z forward",
               "source_conversion": "Y/Z swap and face rewinding; FBX -Z forward Y up",
               "palette_srgb_hex": COLORS, "report": report, "models": model_records,
               "runtime_contract": "Reuse the hero's shared skin material. No generated runtime geometry. Placed from bone positions each frame; never parented to an imported bone."}
    text = json.dumps(payload, indent=2) + "\n"
    (SOURCE / "home-shower-action-3d-model.json").write_text(text, encoding="utf-8")
    runtime_manifest = RESOURCES / "HomeShowerAction.json"
    runtime_manifest.write_text(text, encoding="utf-8")
    write_meta(runtime_manifest)
    if args.preview:
        compose_preview(roots)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "HomeShowerAction.blend"))
    print("HOME_SHOWER_ACTION_EXPORTED " + json.dumps(report) + " sha256=" + signature, flush=True)


if __name__ == "__main__":
    main()
