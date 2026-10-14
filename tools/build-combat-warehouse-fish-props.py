#!/usr/bin/env python3
"""Deterministic passive fish props for the isolated warehouse.

The City preparation trough supplies its exact geometry. Its local finish uses
the existing warehouse textures; source City assets remain unchanged. Three
open rusty cans keep small ambiguous spoiled contents beneath large label drawings.
The preparation station has a separate bare bulb, socket, continuous cord and
ceiling rose; runtime owns its white light and shared flicker presentation.
Roots use fixed Unity metres; paper UV0 uses one shared picture atlas while its
UV1 and all other surfaces keep metric projections.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import shutil
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import bar_parts as bp
import interior_kit as kit
import warehouse_surface_authoring as appearance


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


warehouse = module("warehouse_fish_base", "build-combat-warehouse-3d-model.py")
cannery = module("warehouse_fish_cannery", "build-city-cannery-3d-model.py")
passive = warehouse.passive
VERSION = "1.4.0"
MODEL_DIR = ROOT / "Assets/Resources/CombatWarehouse"
SOURCE_DIR = ROOT / "ArtSource/CombatWarehouse/FishProps"
NAMES = ("FishPreparationStation", "RottenFishTailCan", "RottenFishBellyCan", "RottenFishHeadCan",
         "EmergencyRoomLamp", "FishStationLamp")
SURFACES = {**warehouse.SURFACES, "SpoiledFish": (.40, .45, .37),
            "SpoiledFishFlesh": (.57, .57, .43), "SpoiledFishDark": (.14, .19, .145),
            "FishLabelPaper": (1, 1, 1)}
passive.SURFACES, passive.VERSION = SURFACES, VERSION


def preparation_station():
    geom = cannery.Geometry()
    cannery.add_preparation_station(geom)
    # Original floor is .18m; include feet and projecting plumbing when finding
    # the bottom-centred origin instead of trusting the nominal trough size.
    unity = bp.to_source((geom.vertices, geom.faces))
    low, high = kit.bounds(unity)
    origin = ((low[0] + high[0]) / 2, low[1], (low[2] + high[2]) / 2)
    unity = kit.translated(unity, tuple(-v for v in origin))
    item = passive.Item("FishPreparationStation")
    roles = {}
    for face, color in zip(unity[1], geom.colors, strict=True):
        role = "Rubber" if color == cannery.DARK else "Steel" if color == cannery.CABIN else "Rust"
        vertices, faces = roles.setdefault(role, ([], []))
        start = len(vertices)
        vertices.extend(unity[0][i] for i in face)
        faces.append(tuple(range(start, start + len(face))))
    for role, geometry in roles.items():
        item.add(role, geometry, "PreparationTrough")
    # One authored collision proxy agrees with the plan's entire fixed footprint.
    size = tuple(high[i] - low[i] for i in range(3))
    item.add("ConcreteDark", bp.u_box((0, size[1] / 2, 0), size, .008), "Collision_PreparationStation")
    item.anchor("WorkSurface", (0, .78, 0))
    item.anchor("PreparationLoad", (-.7, .78, 0))
    item.anchor("Front", (0, .78, -.61))
    item.source_geometry_signature = hashlib.sha256(json.dumps({
        "vertices": [[round(v, 7) for v in p] for p in unity[0]], "faces": unity[1]},
        sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    item.source_origin = list(origin)
    return item


def transformed(geometry, position=(0, 0, 0), rotation=(0, 0, 0)):
    return kit.translated(bp.u_rotated(geometry, rotation), position)


def ellipsoid(item, role, p, size, group="FishPart", sides=12, rows=6):
    item.add(role, passive.ellipsoid(p, size, sides, rows), group)


def can_shell(item):
    # Double wall and a real inner floor; nothing caps the mouth at the rim.
    profile = [(0, .059), (.005, .063), (.089, .063), (.095, .061),
               (.095, .057), (.012, .057), (.012, .001)]
    item.add("Rust", passive.ring(profile, 20), "CanBody")
    for y in (.006, .034, .067, .093):
        item.add("WornSteel", warehouse.hollow_ring([(y - .0015, .064), (y + .0015, .064),
                 (y + .0015, .0615), (y - .0015, .0615)], 20), "RolledBeads")
    hinge = (0, .095, .061)
    lid = transformed(passive.ring([(-.0015, .061), (.0015, .061)], 20),
                      (0, 0, -.061))
    lid = transformed(lid, hinge, (110, 0, 0))
    item.add("Rust", lid, "OpenLid")
    bead = transformed(warehouse.hollow_ring([(-.001, .048), (.001, .048),
                      (.001, .046), (-.001, .046)], 20), (0, .002, -.061))
    item.add("WornSteel", transformed(bead, hinge, (110, 0, 0)), "OpenLid")
    item.rod("Rust", (-.014, .095, .061), (.014, .095, .061), .003, "LidHinge", sides=8)
    # A quiet dark liquid surface leaves the double wall and fish shape visible.
    item.add("SpoiledFishDark", passive.ring([(.012, .053), (.073, .053)], 20), "SpoiledJuice")
    item.anchor("Rim", (0, .095, 0))
    item.anchor("InspectionCenter", (0, .12, 0))


def paper_label(item):
    # The paper follows the curved body. Its restrained irregular edge and
    # narrow rear seam expose the old rusty tin; neither adds written text.
    vertices, faces = [], []
    sections = 36
    for i in range(sections + 1):
        angle = -math.pi + .13 + (math.tau - .26) * i / sections
        low = .018 + .0011 * math.sin(i * 2.3)
        high = .078 + .0014 * math.sin(i * 1.7)
        if i % 13 == 5:
            high -= .004
        for radius, y in ((.0646, low), (.0646, high), (.0641, high), (.0641, low)):
            vertices.append((math.sin(angle) * radius, y, -math.cos(angle) * radius))
    faces.extend((tuple(reversed(range(4))), tuple(range(sections * 4, sections * 4 + 4))))
    for i in range(sections):
        for j in range(4):
            other = (j + 1) % 4
            faces.append((i * 4 + j, i * 4 + other, (i + 1) * 4 + other, (i + 1) * 4 + j))
    if bp.signed_volume((vertices, faces)) < 0:
        faces = [tuple(reversed(face)) for face in faces]
    item.add("FishLabelPaper", (vertices, faces), "WornPaperLabel")


def abstract_can(name, icon):
    item = passive.Item(name)
    can_shell(item)
    # All three contain the same small, ambiguous dark fragments. The paper
    # large printed drawing supplies their distinction; no fish part protrudes.
    for p, size in (((-.018, .075, -.010), (.032, .020, .029)),
                    ((.018, .079, .010), (.027, .018, .033)),
                    ((.006, .076, -.026), (.023, .021, .022))):
        ellipsoid(item, "SpoiledFishDark", p, size, "SpoiledFragments", sides=8, rows=4)
    ellipsoid(item, "SpoiledFish", (-.006, .080, .019), (.024, .013, .017),
              "SpoiledFragments", sides=8, rows=4)
    paper_label(item)
    item.label_kind = icon
    return item


def station_lamp():
    """Ceiling-origin bare pendant with a continuous, visibly connected supply."""
    item = passive.Item("FishStationLamp")
    # The rose slightly enters the real ceiling instead of stopping below it.
    item.add("Steel", passive.ring([(-.043, .049), (-.032, .071),
             (-.008, .080), (.006, .080)], 16), "CeilingRose")
    for x in (-.052, .052):
        item.rod("WornSteel", (x, -.034, 0), (x, -.039, 0), .007,
                 "RoseScrews", sides=8)
    # The ends overlap rose, strain relief and socket; no detached support.
    item.rod("Rubber", (0, -.024, 0), (0, -1.973, 0), .007,
             "SupplyCord", sides=10)
    item.add("Rubber", passive.ring([(-1.977, .013), (-1.941, .013)], 12), "StrainRelief")
    item.add("Steel", passive.ring([(-2.051, .027), (-2.038, .034),
             (-1.966, .034), (-1.958, .025)], 16), "Socket")
    for y in (-2.027, -2.010, -1.993):
        item.add("WornSteel", warehouse.hollow_ring([(y-.002, .035),
                 (y+.002, .035), (y+.002, .033), (y-.002, .033)], 16), "SocketBands")
    # An opaque frosted envelope remains readable when switched off. The group
    # is a stable renderer hook; existing Lamp material is shared at runtime.
    # At the 10.2m ceiling / 6.4m gallery, the bulb bottom is .831m above
    # the preparation station's authored .78m work surface.
    item.add("Lamp", passive.ring([(-2.189, .006), (-2.179, .034),
             (-2.152, .060), (-2.120, .070), (-2.090, .064),
             (-2.064, .041), (-2.046, .022)], 16), "LampBulb")
    item.anchor("CeilingMount", (0, 0, 0))
    item.anchor("Light", (0, -2.215, 0))
    return item


def make_items():
    return [preparation_station(), abstract_can("RottenFishTailCan", "tail"),
            abstract_can("RottenFishBellyCan", "belly"), abstract_can("RottenFishHeadCan", "head"),
            warehouse.make_emergency_lamp(room=True), station_lamp()]


def validate_geometry(items):
    if tuple(item.name for item in items) != NAMES:
        raise RuntimeError("Warehouse fish supplement changed its bounded model set")
    original_lamp = warehouse.make_emergency_lamp()
    room_lamp = items[4]
    for key, geometry in original_lamp.parts.items():
        if key[0] not in ("SupplyCable", "CableJunction") and room_lamp.parts.get(key) != geometry:
            raise RuntimeError("Room indicator must retain the goal lamp's exact cage and body")
    lamp_bounds = kit.bounds(kit.merge_all(g for solids in room_lamp.parts.values() for g in solids))
    if lamp_bounds[0][1] + 2.72 < 2.55 or lamp_bounds[1][1] + 2.72 > 3:
        raise RuntimeError("Emergency room fixture does not fit above the real doorway")
    pendant = items[5]
    pendant_bounds = kit.bounds(kit.merge_all(g for solids in pendant.parts.values() for g in solids))
    if pendant_bounds != ((-.08, -2.189, -.08), (.08, .006, .08)):
        raise RuntimeError("Preparation lamp must stay within its measured low work-light envelope")
    if pendant.anchors != {"CeilingMount": {"position": [0, 0, 0], "parent": None},
                           "Light": {"position": [0, -2.215, 0], "parent": None}}:
        raise RuntimeError("Preparation lamp anchors must retain ceiling origin and light below the bulb")
    cord = kit.bounds(kit.merge_all(pendant.parts[("SupplyCord", "Rubber")]))
    if abs(cord[0][1] + 1.973) > .000001 or abs(cord[1][1] + .024) > .000001:
        raise RuntimeError("Preparation lamp cord must connect rose to socket without a gap")
    if ("LampBulb", "Lamp") not in pendant.parts or any(group.startswith("Collision_") for group, _ in pendant.parts):
        raise RuntimeError("Preparation pendant needs one passive bulb renderer and no collision proxy")
    for item in items[1:4]:
        if any(group.startswith("Collision_") for group, _ in item.parts):
            raise RuntimeError("Inspection cans must remain collision-free")
        contents = kit.merge_all(g for role in ("SpoiledFish", "SpoiledFishDark")
            for g in item.parts[("SpoiledFragments", role)])
        if kit.bounds(contents)[1][1] > .095:
            raise RuntimeError("Abstract spoiled fragments must stay below the can rim")
        if ("WornPaperLabel", "FishLabelPaper") not in item.parts:
            raise RuntimeError("Each can needs worn paper for its label drawing")
        body = kit.merge_all(item.parts[("CanBody", "Rust")])
        source = bp.to_source(body)
        bvh = BVHTree.FromPolygons(source[0], source[1])
        point, _, _, _ = bvh.ray_cast(Vector((0, 0, 1)), Vector((0, 0, -1)), 2)
        if point is None or abs(point.z - .012) > .0001:
            raise RuntimeError("Rusty can must have a real open mouth and an inner bottom")


def label_source():
    record = json.loads((SOURCE_DIR / "WarehouseFishLabels.json").read_text(encoding="utf8"))
    if record["size"] != 512 or record["text"] or [item["kind"] for item in record["labels"]] != ["tail", "belly", "head"]:
        raise RuntimeError("Can labels require a bounded picture atlas without written text")
    if hashlib.sha256((ROOT / record["texture"]).read_bytes()).hexdigest() != record["sha256"]:
        raise RuntimeError("Fish label drawings differ from their source record")
    return record


def metric_uvs(roots):
    labels = {record["model"]: record for record in label_source()["labels"]}
    for root in roots:
        appearance.preserve_metre_uv(root)
        for obj in root.children_recursive:
            if obj.type != "MESH":
                continue
            if obj.get("bp_surface") == "FishLabelPaper":
                record = labels[root.name]
                _, top, _, bottom = record["cell_pixels"]
                v_low, v_high = 1 - (bottom - 6) / 512, 1 - (top + 6) / 512
                for loop_index, loop in enumerate(obj.data.uv_layers[0].data):
                    point = obj.data.vertices[obj.data.loops[loop_index].vertex_index].co
                    angle = math.atan2(point.x, -point.y)
                    u = min(1, max(0, .5 + angle / 2.6))
                    height = min(1, max(0, (point.z - .018) / .060))
                    loop.uv = (u, v_low + height * (v_high - v_low))
            for layer in obj.data.uv_layers:
                values = np.empty(len(layer.data) * 2, dtype=np.float32)
                layer.data.foreach_get("uv", values)
                if not np.isfinite(values).all():
                    raise RuntimeError("Fish supplement lost finite metre UVs")
    return appearance.cache_uvs(roots)


def source_materials(roots):
    # Review the same reused rust/paint sheets that runtime binds by surface role.
    images, _ = appearance.source_images()
    materials = {}
    for role, texture in appearance.ROLE_SOURCE.items():
        if role not in SURFACES:
            continue
        mat = bpy.data.materials.new("Warehouse fish shared " + role)
        mat.use_nodes = True
        tree = mat.node_tree
        image = tree.nodes.new("ShaderNodeTexImage")
        image.image = images["Warehouse" + texture]
        multiply = tree.nodes.new("ShaderNodeMixRGB")
        multiply.blend_type = "MULTIPLY"
        multiply.inputs[0].default_value = 1
        multiply.inputs[2].default_value = (*appearance.ROLE_TINT.get(role, (.86, .88, .70)), 1)
        tree.links.new(image.outputs["Color"], multiply.inputs[1])
        tree.links.new(multiply.outputs[0], tree.nodes.get("Principled BSDF").inputs["Base Color"])
        tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = .85
        materials[role] = mat
    paper = bpy.data.materials.new("Warehouse fish label drawings shared")
    paper.use_nodes = True
    texture = paper.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(ROOT / label_source()["texture"]), check_existing=True)
    texture.extension = "EXTEND"
    paper.node_tree.links.new(texture.outputs["Color"], paper.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
    paper.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = .85
    materials["FishLabelPaper"] = paper
    for root in roots:
        for obj in root.children_recursive:
            if obj.type != "MESH":
                continue
            if obj.name.startswith("Collision_"):
                obj.hide_render = True
                obj.hide_set(True)
            role = obj["bp_surface"]
            if role in materials:
                obj.data.materials.clear()
                obj.data.materials.append(materials[role])


def preview(items, path):
    roots = passive.build_objects(items)
    metric_uvs(roots)
    source_materials(roots)
    # Review enlarged cans beside the metre-scale trough; only review transforms
    # change, and no enlarged model is exported or saved as the source contract.
    placements = [(-1.7, 0, 0), (1.15, 0, -.3), (2.05, 0, -.3), (2.95, 0, -.3),
                  (.1, 1.2, .65), (3.3, 3.35, 1.1)]
    for root, p in zip(roots, placements, strict=True):
        root.location = (p[0], p[2], p[1])
        if root.name.startswith("Rotten"):
            root.scale = (4, 4, 4)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1700, 1050, 100
    scene.world = bpy.data.worlds.new("Warehouse fish review world")
    scene.world.color = (.13, .13, .13)
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    for name, p, energy, size in (("Key", (0, -4, 5), 650, 5), ("Fill", (2, 3, 4), 350, 4)):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.size = energy, size
        light = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(light)
        light.location = p
        light.rotation_euler = (Vector((.5, 0, .8)) - light.location).to_track_quat("-Z", "Y").to_euler()
    data = bpy.data.cameras.new("Warehouse fish review")
    camera = bpy.data.objects.new(data.name, data)
    bpy.context.collection.objects.link(camera)
    camera.location = (5, -8, 5.2)
    camera.rotation_euler = (Vector((.25, 0, .85)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    data.type, data.ortho_scale = "ORTHO", 7.3
    scene.camera = camera
    scene.render.filepath = str(path)
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--labels-only", action="store_true", help="Refresh picture manifest and source preview, retaining all FBX files")
    parser.add_argument("--only-model", choices=NAMES, action="append",
        help="Export selected models; retain the remaining verified FBX files unchanged")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    items = make_items()
    validate_geometry(items)
    signature = passive.signature(items)
    if signature != passive.signature(make_items()):
        raise RuntimeError("Warehouse fish supplement repeated geometry differs")
    payload = passive.manifest(items, signature)
    payload.update(generator="tools/build-combat-warehouse-fish-props.py", design_id="warehouse_fish_props_v1")
    roots = passive.build_objects(items)
    uv_cache = metric_uvs(roots)
    payload["surfaces"] = {"mode": "shared warehouse textures / paper label UV0 atlas / metric UV1",
        "uv_signature": appearance.uv_signature(uv_cache), "additional_runtime_textures": 1,
        "labels": label_source()}
    payload["contract"] = {"preparation_source": "tools/build-city-cannery-3d-model.py:add_preparation_station",
        "preparation_source_geometry_signature": items[0].source_geometry_signature,
        "preparation_source_origin": items[0].source_origin, "preparation_work_height": .78,
        "can_body_radius": .063, "can_rim_height": .095, "can_inner_bottom": .012,
        "can_lid_open_degrees": 110, "can_colliders": False, "can_text": False,
        "can_contents": "small dark ambiguous fragments entirely below the rim",
        "can_label_roles": ["FishLabelPaper"],
        "can_label_pictures": {item.name: item.label_kind for item in items[1:4]},
        "room_lamp_center_height": 2.72, "room_lamp_lintel_range": [2.55, 3],
        "room_lamp_body": "exact EmergencyLamp cage/body with short side-fed supply",
        "station_lamp": {"ceiling_origin": [0, 0, 0], "light_anchor": [0, -2.215, 0],
            "bulb_renderer": "LampBulb__Lamp", "cord_radius": .007,
            "cord_ends": [[0, -.024, 0], [0, -1.973, 0]], "work_surface_clearance": .831,
            "presentation": "bare frosted bulb and socket on continuous dark cord with ceiling rose"}}
    if uv_cache != metric_uvs(passive.build_objects(items)):
        raise RuntimeError("Warehouse fish supplement repeated metric UVs differ")
    manifest_path = args.model_dir / "WarehouseFishProps3D.json"
    if args.validate_only:
        if json.loads(manifest_path.read_text(encoding="utf8")) != payload:
            raise RuntimeError("Published fish supplement differs from its deterministic contract")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(SOURCE_DIR / "WarehouseFishLabels.json", args.source_dir / "WarehouseFishLabels.json") if args.source_dir.resolve() != SOURCE_DIR.resolve() else None
        previous = json.loads((MODEL_DIR / "WarehouseFishProps3D.json").read_text(encoding="utf8")) if args.only_model or args.labels_only else None
        for item, record in zip(items, payload["models"], strict=True):
            if args.labels_only or args.only_model and item.name not in args.only_model:
                current = next(model for model in previous["models"] if model["name"] == item.name)
                if current != record:
                    raise RuntimeError("Retained fish-prop model changed: " + item.name)
                if args.labels_only:
                    continue
                destination = args.model_dir / (item.name + ".fbx")
                source = MODEL_DIR / (item.name + ".fbx")
                if destination.resolve() != source.resolve():
                    shutil.copyfile(source, destination)
                continue
            root = passive.build_objects([item])[0]
            metric_uvs([root])
            passive.export(root, args.model_dir / (item.name + ".fbx"))
        manifest_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        roots = passive.build_objects(items)
        metric_uvs(roots)
        source_materials(roots)
        for image in bpy.data.images:
            if image.source == "FILE":
                image.pack()
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "WarehouseFishProps3D.blend"), check_existing=False)
        if not args.no_preview:
            preview(items, args.source_dir / "WarehouseFishProps3D.png")
    verification_dir = MODEL_DIR if args.labels_only else args.model_dir
    passive.verify_fbx(verification_dir, payload)
    appearance.verify_fbx_uvs(verification_dir, uv_cache, list(NAMES))
    print("WAREHOUSE FISH PROPS ART CONTRACT OK " + json.dumps({"models": len(items),
        "triangles": payload["triangle_count"], "signature": signature}), flush=True)


if __name__ == "__main__":
    main()
