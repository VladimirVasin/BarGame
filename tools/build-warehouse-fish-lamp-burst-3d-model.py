#!/usr/bin/env python3
"""Eight closed thin-shell shards from the existing warehouse pendant bulb.

The old FBX is a read-only geometry/UV source. Four angular quarters in each
of two existing profile bands retain the exact outer bulb surface. New inward
shells and closed fracture edges give each physical shard finite volume.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import bar_parts as bp
import interior_kit as kit

spec = importlib.util.spec_from_file_location("lamp_burst_passive", ROOT / "tools/build-city-fair-3d-model.py")
passive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(passive)
VERSION = "1.0.0"
OUT = ROOT / "Assets/Resources/CombatWarehouse/FishLampBurst"
SOURCE = ROOT / "ArtSource/CombatWarehouse/FishLampBurst"
ORIGINAL = ROOT / "Assets/Resources/CombatWarehouse/FishStationLamp.fbx"
THICKNESS = .0015
PROFILE = ((-2.189, .006), (-2.179, .034), (-2.152, .060), (-2.120, .070),
           (-2.090, .064), (-2.064, .041), (-2.046, .022))
SURFACES = {"Glass": (.52, .59, .56)}


def source_shards():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ORIGINAL), use_anim=False)
    bulb = next((o for o in bpy.context.scene.objects if o.type == "MESH" and o.name.startswith("LampBulb__Lamp")), None)
    if bulb is None:
        raise RuntimeError("Existing warehouse pendant lacks its authored bulb renderer")
    mesh = bulb.data
    points = [tuple((lambda p: (p.x, p.z, p.y))(bulb.matrix_world @ v.co)) for v in mesh.vertices]
    if len(mesh.uv_layers) != 2:
        raise RuntimeError("Existing pendant must retain its two metric UV layers")
    groups = [[] for _ in range(8)]
    outer_faces = 0
    for face in mesh.polygons:
        # The source's tiny flat end discs are not glass shell. Fracture edges
        # close the new inward shell at those rings, beneath the socket and tip.
        ys = [points[i][1] for i in face.vertices]
        if max(ys)-min(ys) < .000001:
            continue
        centroid = [sum(points[i][axis] for i in face.vertices)/len(face.vertices) for axis in range(3)]
        quadrant = int((math.atan2(centroid[2], centroid[0]) % math.tau)/(math.pi/2))
        band = 0 if centroid[1] < -2.120 else 1
        groups[band*4+quadrant].append(face)
        outer_faces += 1
    if outer_faces != 96 or any(len(group) != 12 for group in groups):
        raise RuntimeError("Original bulb changed its bounded 16-side / six-band outer surface")
    records = []
    for index, group in enumerate(groups):
        used = sorted({i for face in group for i in face.vertices})
        remap = {old: new for new, old in enumerate(used)}
        outer = [points[i] for i in used]
        # Reflection from Blender to Unity reverses face winding and UV order.
        faces = [tuple(remap[i] for i in reversed(face.vertices)) for face in group]
        uv_faces = [[[tuple(layer.data[loop].uv) for loop in reversed(face.loop_indices)]
                     for face in group] for layer in mesh.uv_layers]
        inner = []
        for x, y, z in outer:
            row = min(range(len(PROFILE)), key=lambda i: abs(PROFILE[i][0]-y))
            before, after = PROFILE[max(0, row-1)], PROFILE[min(len(PROFILE)-1, row+1)]
            dy, dr = after[0]-before[0], after[1]-before[1]
            radius = math.hypot(x, z)
            normal = Vector((dy*x/radius, -dr, dy*z/radius)).normalized()
            inner.append(tuple(Vector((x, y, z))-THICKNESS*normal))
        count = len(outer)
        vertices = outer+inner
        complete = list(faces)+[tuple(i+count for i in reversed(face)) for face in faces]
        complete_uvs = [list(values)+[list(reversed(uv)) for uv in values] for values in uv_faces]
        edges = {}
        for face in faces:
            for a, b in zip(face, face[1:]+face[:1]):
                edges.setdefault(tuple(sorted((a, b))), []).append((a, b))
        for occurrences in edges.values():
            if len(occurrences) != 1:
                continue
            a, b = occurrences[0]
            wall = (b, a, a+count, b+count)
            complete.append(wall)
            # Fracture edges use metre projections; no UV-degenerate side faces.
            normal = (Vector(vertices[wall[1]])-Vector(vertices[wall[0]])).cross(
                Vector(vertices[wall[2]])-Vector(vertices[wall[0]]))
            axis = max(range(3), key=lambda i: abs(normal[i]))
            axes = [i for i in range(3) if i != axis]
            uv = [tuple(vertices[i][a] for a in axes) for i in wall]
            for values in complete_uvs:
                values.append(uv)
        if bp.signed_volume((vertices, complete)) < 0:
            complete = [tuple(reversed(face)) for face in complete]
            complete_uvs = [[list(reversed(uv)) for uv in values] for values in complete_uvs]
        if bp.signed_volume((vertices, complete)) < 1e-10:
            raise RuntimeError("Bulb shard lacks a positive closed thin-glass volume")
        low, high = kit.bounds((vertices, complete))
        center = tuple((a+b)/2 for a, b in zip(low, high))
        records.append({"name": f"Shard{index+1:02d}", "position": list(center),
            "vertices": vertices, "faces": complete, "uv_faces": complete_uvs,
            "bounds_min": list(low), "bounds_max": list(high), "outer_face_count": len(faces)})
    low, high = kit.bounds(([p for r in records for p in r["vertices"]], []))
    expected = (-.07, -2.189, -.07, .07, -2.046, .07)
    if any(abs(a-b) > .000001 for a, b in zip((*low, *high), expected)):
        raise RuntimeError("Shards changed the original bulb's measured outer envelope")
    return records


def build(records):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = passive.empty("LampShards")
    material = bpy.data.materials.new("Non emissive warehouse bulb glass")
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*SURFACES["Glass"], 1)
    shader.inputs["Roughness"].default_value = .35
    shader.inputs["Metallic"].default_value = 0
    for record in records:
        p = record["position"]
        anchor = passive.empty(record["name"], root, p)
        centered = [(x-p[0], y-p[1], z-p[2]) for x, y, z in record["vertices"]]
        geometry = bp.to_source((centered, record["faces"]))
        mesh = bpy.data.meshes.new(record["name"]+"Mesh")
        mesh.from_pydata(geometry[0], [], geometry[1])
        mesh.update(calc_edges=True)
        for index, face_uvs in enumerate(record["uv_faces"]):
            layer = mesh.uv_layers.new(name="WarehouseUV" if index == 0 else "WarehouseMetres")
            # bp.to_source reverses every face a second time back to Blender.
            values = [uv for face in face_uvs for uv in reversed(face)]
            for loop, uv in zip(layer.data, values, strict=True):
                loop.uv = uv
        mesh.uv_layers.active_index = 0
        mesh.uv_layers[0].active_render = True
        mesh.materials.append(material)
        obj = bpy.data.objects.new(record["name"]+"__Glass", mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = anchor
        obj["bp_surface"] = "Glass"
    return root


def payload(records):
    low, high = kit.bounds(([p for r in records for p in r["vertices"]], []))
    return json.loads(json.dumps({"generator": "tools/build-warehouse-fish-lamp-burst-3d-model.py", "generator_version": VERSION,
        "blender_version": bpy.app.version_string, "units": "metres", "passive": True,
        "build_signature": hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
        "source": {"fbx": "Assets/Resources/CombatWarehouse/FishStationLamp.fbx",
            "sha256": hashlib.sha256(ORIGINAL.read_bytes()).hexdigest(), "renderer": "LampBulb__Lamp"},
        "models": [{"name": "LampShards", "bounds_min": [round(v, 6) for v in low], "bounds_max": [round(v, 6) for v in high],
            "triangle_count": sum(sum(len(f)-2 for f in r["faces"]) for r in records), "mesh_count": 8, "solid_count": 8,
            "anchors": [{"name": r["name"], "position": r["position"], "parent": None} for r in records]}],
        "contract": {"root_frame": "FishStationLamp local metres", "shard_count": 8, "shell_thickness": THICKNESS,
            "layout": "four angular quarters x two existing profile bands", "source_outer_faces": 96,
            "outer_surface_and_uvs": "unchanged original side faces; inward shell and closed fracture edges",
            "material": "shared non emissive Glass / no new texture", "bounds_validation": "FBX world metres and anchors",
            "shards": [{"name": r["name"], "position": r["position"], "bounds_min": r["bounds_min"], "bounds_max": r["bounds_max"]}
                for r in records]}}))


def verify(directory, manifest, records):
    passive.verify_fbx(directory, manifest)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(directory/"LampShards.fbx"), use_anim=False)
    for record in records:
        obj = bpy.data.objects.get(record["name"]+"__Glass")
        if obj is None or len(obj.data.uv_layers) != 2:
            raise RuntimeError("Exported bulb shard lost its stable renderer/metric UV layers")
        points = [obj.matrix_world @ v.co for v in obj.data.vertices]
        edges = {}
        for face in obj.data.polygons:
            for a, b in zip(face.vertices, tuple(face.vertices[1:])+tuple(face.vertices[:1])):
                edge = tuple(sorted((a, b)))
                edges[edge] = edges.get(edge, 0)+1
        if any(count != 2 for count in edges.values()):
            raise RuntimeError("Exported physical bulb shard has an open fracture edge")
        if bp.signed_volume((points, [tuple(f.vertices) for f in obj.data.polygons])) <= 0:
            raise RuntimeError("Exported bulb shard lost outward winding")
        for index, expected in enumerate(record["uv_faces"]):
            expected = [uv for face in expected for uv in reversed(face)]
            actual = [tuple(loop.uv) for loop in obj.data.uv_layers[index].data]
            if len(actual) != len(expected) or any(abs(a-b) > .000001 for x, y in zip(actual, expected) for a, b in zip(x, y)):
                raise RuntimeError("Exported bulb shard changed original shell UV values")
    print("FISH LAMP EIGHT CLOSED SHARDS / ORIGINAL OUTER UVS / POSITIVE VOLUMES OK", flush=True)


def preview(root, path):
    for index, anchor in enumerate(root.children):
        p = anchor.location.copy()
        radial = Vector((p.x, p.y, 0)).normalized()
        anchor.location += radial*.018+Vector((0, 0, -.012 if index < 4 else .012))
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1100, 900, 100
    scene.world = bpy.data.worlds.new("Bulb shard review")
    scene.world.color = (.12, .12, .12)
    for name, p, power in (("Key", (.2, -.3, -1.8), 5), ("Fill", (-.2, .3, -1.9), 3)):
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "AREA"))
        bpy.context.collection.objects.link(light)
        light.location = p
        light.data.energy, light.data.size = power, .4
    camera = bpy.data.objects.new("Shards review", bpy.data.cameras.new("Shards review"))
    bpy.context.collection.objects.link(camera)
    camera.location = (.3, -.4, -1.9)
    camera.rotation_euler = (Vector((0, 0, -2.12))-camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type, camera.data.ortho_scale = "ORTHO", .30
    scene.camera = camera
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=OUT)
    parser.add_argument("--source-dir", type=Path, default=SOURCE)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    records = source_shards()
    if records != source_shards():
        raise RuntimeError("Original bulb shard authoring is nondeterministic")
    manifest = payload(records)
    if args.validate_only:
        if json.loads((args.output_dir/"WarehouseFishLampBurst3D.json").read_text(encoding="utf8")) != manifest:
            raise RuntimeError("Published bulb shard pack differs from deterministic source")
    else:
        args.output_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        root = build(records)
        passive.export(root, args.output_dir/"LampShards.fbx")
        (args.output_dir/"WarehouseFishLampBurst3D.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf8")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"WarehouseFishLampBurst3D.blend"), check_existing=False)
        if not args.no_preview:
            preview(root, args.source_dir/"WarehouseFishLampBurst3D.png")
    verify(args.output_dir, manifest, records)
    print("WAREHOUSE FISH LAMP BURST ART CONTRACT OK "+manifest["build_signature"], flush=True)


if __name__ == "__main__":
    main()
