#!/usr/bin/env python3
"""Deterministic Blender sink cavity and normalized brushing liquid assets.

Blender --background --factory-startup --python tools/build-home-brushing-action-3d-model.py -- [--validate-only]
The existing Home sink footprint, placement and player collision stay unchanged.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/HomeBrushingAction"
RESOURCES = ROOT / "Assets/Resources/HomeBrushingAction"
MODELS = RESOURCES / "Models"
VERSION = "1.1.0"
spec = importlib.util.spec_from_file_location("home_authored_liquid", ROOT / "tools/build-home-toilet-action-3d-model.py")
kit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kit)
kit.COLORS.update({"SinkSteel": "424C46", "Foam": "D2D0B9", "BrushPlastic": "8C261F"})
BASIN_POSITION = (2.075, .78, 3.425)
DRAIN_POSITION = (1.995, .724, 3.425)


def sink_basin():
    """Continuous outer shell, rim, inward slope and bottom; no aperture cap."""
    profiles = (
        ((0, -.100, 0), .270, .105, 2.5),
        ((0, -.086, 0), .315, .128, 3.0),
        ((0, .080, 0), .418, .170, 4.0),
        ((0, .094, 0), .425, .175, 4.0),
        ((0, .100, 0), .417, .167, 4.0),
        ((-.080, .100, 0), .295, .114, 3.0),
        ((-.080, .084, 0), .277, .101, 2.5),
        ((-.080, -.030, 0), .160, .064, 2.0),
        ((-.080, -.060, 0), .055, .033, 2.0),
    )
    sides = 32
    vertices = []
    for center, radius_x, radius_z, exponent in profiles:
        for index in range(sides):
            angle = math.tau * index / sides
            x, z = math.cos(angle), math.sin(angle)
            vertices.append((center[0] + math.copysign(abs(x) ** (2 / exponent), x) * radius_x,
                             center[1],
                             center[2] + math.copysign(abs(z) ** (2 / exponent), z) * radius_z))
    faces = [tuple(range(sides))]
    for row in range(len(profiles) - 1):
        for side in range(sides):
            a, b = row * sides + side, row * sides + (side + 1) % sides
            faces.append((a, a + sides, b + sides, b))
    last = (len(profiles) - 1) * sides
    faces.append(tuple(reversed(range(last, last + sides))))
    return vertices, faces


def sink_drain():
    # Small dark grate sits 4 mm over the genuine ceramic bottom. Both the
    # outside ring and three narrow ribs are geometry; their gaps stay open.
    vertices, faces = kit.hollow_vertical_profile(((.023, -.002), (.023, .002),
                                                  (.0155, .002), (.0155, -.002)), sides=20)
    for z in (-.009, 0, .009):
        length = math.sqrt(.0165 ** 2 - z ** 2) * 2
        x, y, width = length / 2, .002, .0018
        start = len(vertices)
        vertices.extend([(-x, -y, z-width), (x, -y, z-width), (x, -y, z+width), (-x, -y, z+width),
                         (-x, y, z-width), (x, y, z-width), (x, y, z+width), (-x, y, z+width)])
        faces.extend(tuple(start + i for i in f) for f in ((0,1,2,3),(4,7,6,5),(0,4,5,1),
                                                         (1,5,6,2),(2,6,7,3),(3,7,4,0)))
    return vertices, faces


def definitions():
    droplet = kit.ring_loft([((0, 0, -.5), .025, .025), ((0, 0, -.3), .36, .36),
                             ((0, 0, 0), .5, .5), ((0, 0, .3), .36, .36),
                             ((0, 0, .5), .025, .025)], sides=8)
    handle = kit.ring_loft([((0, -.070, 0), .0055, .0055),
                            ((0, -.069, 0), .006, .006),
                            ((0, .069, 0), .006, .006),
                            ((0, .070, 0), .0055, .0055)], sides=12, axis="y")
    return {
        "SinkBasin": {"meshes": [("SinkBasin", sink_basin(), "Enamel")],
                      "anchors": {"BasinFloor": (-.080, -.060, 0)},
                      "contract": "Fixed .85 x .20 x .35 metres, top +.10, actual cavity bottom -.06"},
        "SinkDrain": {"meshes": [("SinkDrain", sink_drain(), "SinkSteel")], "anchors": {},
                      "contract": "Fixed .046 metre perforated grate, local +Y up"},
        "Droplet": {"meshes": [("BrushingDroplet", droplet, "Foam")], "anchors": {},
                    "contract": "Normalized unit droplet, longitudinal +Z; runtime millimetre scale"},
        "Splash": {"meshes": [("BrushingSplash", kit.patch(splash=True), "Foam")], "anchors": {},
                   "contract": "Normalized unit XY splash, surface normal +Z; runtime centimetre scale"},
        "BrushHandle": {"meshes": [("BrushHandle", handle, "BrushPlastic")], "anchors": {},
                        "contract": "Fixed .012 metre diameter, .140 metre length, local +Y; place centre at local Y=.045"},
    }


def validate(defs, objects):
    assert kit.canonical(defs) == kit.canonical(definitions()), "Nondeterministic geometry"
    triangles = 0
    for obj in objects.values():
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles)
        assert all(not p.use_smooth for p in obj.data.polygons)
        for tri in obj.data.loop_triangles:
            a, b, c = [obj.data.vertices[i].co for i in tri.vertices]
            assert (b-a).cross(c-a).length > 1e-10, (obj.name, "degenerate triangle")
    lo, hi = kit.bounds(defs["SinkBasin"]["meshes"][0][1])
    assert all(abs(hi[i] - lo[i] - size) < 1e-7 for i, size in enumerate((.85,.20,.35)))
    bpy.context.view_layer.update()
    basin = objects["SinkBasin"]
    # A visible top cap or the former broad dark insert fails every ray.
    rays = 0
    for x in (-.12, -.06, 0, .06, .12):
        for z in (-.045, 0, .045):
            hit, point, _, _ = basin.ray_cast(Vector(kit.source_point((x-.08, .3, z))),
                                             Vector(kit.source_point((0,-1,0))))
            assert hit and point.z < -.015, ("cavity capped", x,z,tuple(point))
            rays += 1
    hit, point, _, _ = basin.ray_cast(Vector(kit.source_point((-.08,.3,0))),
                                     Vector(kit.source_point((0,-1,0))))
    assert hit and abs(point.z + .06) < 1e-6, "Central bottom must be ceramic at .720 world Y"
    # Incoming trajectories from the same side as the standing hero clear
    # the rim until their contact inside the actual bowl.
    for offset in (-.045, 0, .045):
        start = Vector(kit.source_point((-.08 + offset, .82, -.40)))
        end = Vector(kit.source_point((-.08 + offset, -.05, 0)))
        delta = end - start
        hit, point, _, _ = basin.ray_cast(start, delta.normalized(), distance=delta.length)
        assert not hit, ("incoming spit blocked before cavity", offset, tuple(point))
    for name in ("Droplet", "Splash"):
        lo, hi = kit.bounds(defs[name]["meshes"][0][1])
        assert abs(hi[0] - lo[0] - 1) < 1e-6 and abs(hi[1] - lo[1] - 1) < 1e-6
    lo, hi = kit.bounds(defs["BrushHandle"]["meshes"][0][1])
    assert all(abs(hi[i] - lo[i] - size) < 1e-7 for i, size in enumerate((.012,.140,.012)))
    assert abs(lo[1] + .045 + .025) < 1e-7 and abs(hi[1] + .045 - .115) < 1e-7
    return {"models": len(defs), "meshes": len(objects), "triangles": triangles,
            "deterministic_geometry": True, "fixed_fixture_bounds": [.85,.20,.35],
            "unobstructed_cavity_rays": rays + 1, "incoming_spit_clearance_rays": 3,
            "floor_local_y": -.06, "floor_world_y": .72,
            "source_palette_and_flat_shading": True,
            "brush_handle_fixed_metres_and_placed_y_range": [-.025, .115]}


def write_meta(path, folder=False, model=False):
    meta = Path(str(path) + ".meta")
    if meta.exists(): return
    guid = hashlib.md5(("HomeBrushingAction/" + path.relative_to(ROOT).as_posix()).encode()).hexdigest()
    if model:
        content = (ROOT / "Assets/Home/Interior/Models/HomeInterior3D.fbx.meta").read_text(encoding="utf-8")
        content = content.replace("guid: a841a60cd444e79438c87fd91713c304", "guid: " + guid)
    else:
        content = "fileFormatVersion: 2\nguid: " + guid + "\n"
        content += "folderAsset: yes\nDefaultImporter:\n" if folder else "TextScriptImporter:\n"
        content += "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    meta.write_text(content, encoding="utf-8")


def preview(roots):
    for root in roots.values():
        for child in root.children: child.hide_render = True
    for name, position in (("SinkBasin", (0,0,0)), ("SinkDrain", (-.08,-.056,0))):
        for child in roots[name].children:
            if child.type != "MESH": continue
            duplicate = child.copy()
            duplicate.data = child.data
            bpy.context.scene.collection.objects.link(duplicate)
            duplicate.parent = None
            duplicate.name = "PREVIEW_" + name
            duplicate.location = kit.source_point(position)
            duplicate.hide_render = False
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 960
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.world.color = (.15,.15,.15)
    camera_data = bpy.data.cameras.new("SinkInspectionCamera")
    camera = bpy.data.objects.new("SinkInspectionCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = (.45,-.45,1.15)
    camera.rotation_euler = (Vector((-.04,0,0)) - camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 1.04
    scene.camera = camera
    for name, position, power, size in (("Key",(-1,-1,2),80,2), ("Fill",(1,.4,1),35,1.5)):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.shape, data.size = power, "DISK", size
        light = bpy.data.objects.new(name, data)
        scene.collection.objects.link(light)
        light.location = position
        light.rotation_euler = (-light.location).to_track_quat('-Z','Y').to_euler()
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(SOURCE / "SinkBasin-Opening.png")
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--only-model", choices=tuple(definitions()),
                        help="Export this model only while refreshing the complete source and manifests")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1
    defs, roots, objects = definitions(), {}, {}
    for name, definition in defs.items():
        root = bpy.data.objects.new(name + "_Root", None)
        bpy.context.scene.collection.objects.link(root)
        roots[name] = root
        for mesh_name, geometry, material in definition["meshes"]:
            objects[mesh_name] = kit.create_mesh(mesh_name, geometry, material, root)
        for anchor_name, point in definition["anchors"].items():
            anchor = bpy.data.objects.new(anchor_name, None)
            bpy.context.scene.collection.objects.link(anchor)
            anchor.parent, anchor.location = root, kit.source_point(point)
    report, signature = validate(defs, objects), kit.canonical(defs)
    if args.validate_only:
        manifest = json.loads((SOURCE / "home-brushing-action-3d-model.json").read_text(encoding="utf-8"))
        assert manifest["signature"] == signature, "Stale exported geometry"
        for name in defs:
            path = MODELS / (name + ".fbx")
            assert path.exists() and path.stat().st_size > 1000
            metadata = Path(str(path) + ".meta").read_text(encoding="utf-8")
            for setting in ("isReadable: 1", "globalScale: 1", "bakeAxisConversion: 1", "useFileUnits: 1", "preserveHierarchy: 1"):
                assert setting in metadata, (name, setting)
        kit.MODELS = MODELS
        kit.validate_exported_geometry(defs)
        report["fbx_round_trip_metres_axes_and_anchors"] = True
        print("HOME_BRUSHING_ACTION_VALIDATED " + json.dumps(report) + " sha256=" + signature, flush=True)
        return
    SOURCE.mkdir(parents=True, exist_ok=True)
    MODELS.mkdir(parents=True, exist_ok=True)
    write_meta(RESOURCES, folder=True)
    write_meta(MODELS, folder=True)
    records = []
    for name, definition in defs.items():
        root = roots[name]
        bpy.ops.object.select_all(action="DESELECT")
        root.select_set(True)
        for child in root.children: child.select_set(True)
        bpy.context.view_layer.objects.active = root
        path = MODELS / (name + ".fbx")
        if args.only_model is None or args.only_model == name:
            bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"EMPTY","MESH"},
                                    axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL",
                                    bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                                    use_mesh_modifiers=True, mesh_smooth_type="FACE", use_custom_props=True,
                                    path_mode="STRIP", embed_textures=False)
        write_meta(path, model=True)
        records.append({"name": name, "resource": "HomeBrushingAction/Models/" + name,
                        "contract": definition["contract"], "anchors": definition["anchors"],
                        "meshes": [{"name": mesh_name, "material": material,
                                    "bounds_min": kit.bounds(geometry)[0], "bounds_max": kit.bounds(geometry)[1],
                                    "triangles": len(objects[mesh_name].data.loop_triangles)}
                                   for mesh_name, geometry, material in definition["meshes"]]})
    payload = {"schema_version": 1, "generator_version": VERSION, "signature": signature,
               "coordinates": "Unity local metres +Y up +Z forward", "report": report,
               "basin_world_position": BASIN_POSITION, "drain_world_position": DRAIN_POSITION,
               "runtime_contract": "Reuse shared resources; retain original basin BoxCollider; use actual visible triangle meshes for fluid hits.",
               "models": records}
    content = json.dumps(payload, indent=2) + "\n"
    (SOURCE / "home-brushing-action-3d-model.json").write_text(content, encoding="utf-8")
    runtime = RESOURCES / "HomeBrushingAction.json"
    runtime.write_text(content, encoding="utf-8")
    write_meta(runtime)
    preview(roots)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "HomeBrushingAction.blend"))
    print("HOME_BRUSHING_ACTION_EXPORTED " + json.dumps(report) + " sha256=" + signature, flush=True)


if __name__ == "__main__":
    main()
