#!/usr/bin/env python3
"""Split the existing warehouse valve chain without changing its geometry/atlas UVs.

The two passive lower-gallery tile variants add only the slot needed by the
raised gate and its existing drive header. All roots and joint data are in
the original Unity metre frames; old warehouse files are read-only inputs.
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

spec = importlib.util.spec_from_file_location("gate_chain_warehouse", ROOT / "tools/build-combat-warehouse-3d-model.py")
warehouse = importlib.util.module_from_spec(spec)
spec.loader.exec_module(warehouse)
passive = warehouse.passive
VERSION = "1.0.0"
OUT = ROOT / "Assets/Resources/CombatWarehouse/GateChain"
SOURCE = ROOT / "ArtSource/CombatWarehouse/GateChain"
ORIGINAL = ROOT / "Assets/Resources/CombatWarehouse/GateControl.fbx"
ATLAS = ROOT / "Assets/Resources/CombatWarehouse/Textures/WarehouseMechanismsAtlas.png"
PATH = ((.23, 1.86, -.245), (-.24, 1.13, -.385), (-.05, .99, -.395),
        (.23, 1.10, -.395), (.30, 1.48, -.27), (.23, 1.86, -.245))


def centres():
    result = []
    for a, b in zip(PATH, PATH[1:]):
        count = max(1, round(math.dist(a, b)/.078))
        tilt = -math.degrees(math.atan2(b[0]-a[0], b[1]-a[1]))
        for n in range(count):
            fraction = (n+.5)/count
            result.append((tuple(a[i]+(b[i]-a[i])*fraction for i in range(3)), tilt))
    if len(result) != 28:
        raise RuntimeError("Existing valve chain must retain exactly 28 links")
    return result


def source_links():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ORIGINAL), use_anim=False)
    chain = next((o for o in bpy.context.scene.objects if o.type == "MESH" and o.name.startswith("YellowChain__")), None)
    if chain is None:
        raise RuntimeError("Original GateControl lacks its one authored yellow-chain mesh")
    mesh = chain.data
    adjacency = [set() for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        adjacency[a].add(b)
        adjacency[b].add(a)
    unseen = set(range(len(mesh.vertices)))
    components = []
    while unseen:
        pending = [min(unseen)]
        component = set()
        while pending:
            current = pending.pop()
            if current in component:
                continue
            component.add(current)
            pending.extend(adjacency[current]-component)
        unseen -= component
        components.append(sorted(component))
    if len(components) != 28:
        raise RuntimeError("Original yellow chain is not 28 disconnected authored link solids")
    world = [chain.matrix_world @ vertex.co for vertex in mesh.vertices]
    expected = centres()
    records = [None]*28
    for component in components:
        center = sum((world[i] for i in component), Vector())/len(component)
        unity_center = (center.x, center.z, center.y)
        index = min(range(28), key=lambda i: math.dist(unity_center, expected[i][0]))
        if records[index] is not None or math.dist(unity_center, expected[index][0]) > .00001:
            raise RuntimeError("Original chain link no longer agrees with its authored path centre")
        remap = {old: new for new, old in enumerate(component)}
        face_records = [face for face in mesh.polygons if face.vertices[0] in remap]
        faces = [tuple(remap[i] for i in face.vertices) for face in face_records]
        uvs = [[tuple(layer.data[loop].uv) for face in face_records for loop in face.loop_indices] for layer in mesh.uv_layers]
        if len(uvs) != 2:
            raise RuntimeError("Chain must retain its original atlas UV0 and metric UV1")
        p = expected[index][0]
        points = [(world[i].x, world[i].z, world[i].y) for i in component]
        low, high = kit.bounds((points, faces))
        records[index] = {"name": f"Link{index+1:02d}", "position": list(p), "tilt": expected[index][1],
            "vertices": points, "faces": faces, "uvs": uvs,
            "bounds_min": list(low), "bounds_max": list(high)}
    return records


def add_segment(item, group, role, x0, x1, z0, z1, y, height, collision=False, chamfer=.012):
    if x1-x0 < .00001 or z1-z0 < .00001:
        return
    geometry = bp.u_box(((x0+x1)/2, y, (z0+z1)/2), (x1-x0, height, z1-z0), chamfer)
    item.add(role, geometry, group)
    if collision:
        item.add("ConcreteDark", geometry, "Collision_"+group)


def slotted_platform(left):
    item = passive.Item("GatePlatformLeft" if left else "GatePlatformRight")
    # Global slot X[-2.32,+2.32], Z[11.32,11.72]. Tiles remain centred
    # at (-/+1.5,3.2,10.5); only their intersecting local rectangle is cut.
    cut_low, cut_high = (-.82, 1.5) if left else (-1.5, .82)
    add_segment(item, "Deck", "Steel", -1.5, 1.5, -1.5, .82, -.09, .18, True)
    add_segment(item, "Deck", "Steel", -1.5, 1.5, 1.22, 1.5, -.09, .18, True)
    add_segment(item, "Deck", "Steel", -1.5 if left else .82, -.82 if left else 1.5,
                .82, 1.22, -.09, .18, True)
    for x in (-1.34, 0., 1.34):
        spans = ((-1.42, .82), (1.22, 1.42)) if cut_low <= x <= cut_high else ((-1.42, 1.42),)
        for start, end in spans:
            add_segment(item, "DeckBearers", "Steel", x-.07, x+.07, start, end, -.25, .14)
    for index in range(12):
        x = -1.38+index*.25
        spans = ((-1.44, .82), (1.22, 1.44)) if cut_low <= x <= cut_high else ((-1.44, 1.44),)
        for start, end in spans:
            add_segment(item, "DeckSeams", "WornSteel", x-.007, x+.007, start, end, .003, .004, chamfer=.001)
    item.anchor("DeckCenter", (0, 0, 0))
    item.anchor("SlotFront", (0, 0, .82))
    item.anchor("SlotBack", (0, 0, 1.22))
    return item


def create_chain(records):
    root = passive.empty("ValveChain")
    material = bpy.data.materials.new("Original warehouse chain atlas")
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(ATLAS), check_existing=True)
    material.node_tree.links.new(texture.outputs["Color"], material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
    material.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = .85
    for record in records:
        name, p = record["name"], record["position"]
        anchor = passive.empty(name, root, p)
        previous = records[(int(name[4:])-2)%28]["position"]
        following = records[int(name[4:])%28]["position"]
        for prefix, neighbour in (("Previous", previous), ("Next", following)):
            midpoint = tuple((a+b)/2-c for a, b, c in zip(p, neighbour, p))
            passive.empty(prefix+name[4:], anchor, midpoint)
        mesh = bpy.data.meshes.new(name+"Mesh")
        points = [(x-p[0], z-p[2], y-p[1]) for x, y, z in record["vertices"]]
        mesh.from_pydata(points, [], record["faces"])
        mesh.update(calc_edges=True)
        for index, coords in enumerate(record["uvs"]):
            layer = mesh.uv_layers.new(name="WarehouseUV" if index == 0 else "WarehouseMetres")
            for loop, uv in zip(layer.data, coords, strict=True):
                loop.uv = uv
        mesh.uv_layers.active_index = 0
        mesh.uv_layers[0].active_render = True
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name+"__SafetyYellow", mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = anchor
        obj["bp_surface"] = "SafetyYellow"
    return root


def chain_model(records):
    points = [point for record in records for point in record["vertices"]]
    low, high = kit.bounds((points, []))
    anchors = []
    for index, record in enumerate(records):
        anchors.append({"name": record["name"], "position": record["position"], "parent": None})
        for prefix, neighbour in (("Previous", records[(index-1)%28]), ("Next", records[(index+1)%28])):
            anchors.append({"name": prefix+record["name"][4:],
                "position": [(a+b)/2 for a, b in zip(record["position"], neighbour["position"])], "parent": record["name"]})
    return {"name": "ValveChain", "bounds_min": [round(v, 6) for v in low], "bounds_max": [round(v, 6) for v in high],
        "triangle_count": sum(sum(len(face)-2 for face in record["faces"]) for record in records),
        "solid_count": 28, "mesh_count": 28, "anchors": anchors}


def verify_pack(directory, payload, records):
    passive.verify_fbx(directory, payload)
    # Confirm the atlas values survived split/export, not merely the link positions.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(directory/"ValveChain.fbx"), use_anim=False)
    for record in records:
        obj = bpy.data.objects.get(record["name"]+"__SafetyYellow")
        if obj is None or len(obj.data.uv_layers) != 2:
            raise RuntimeError("Split link lost its stable renderer name or original UV layers")
        for index, expected in enumerate(record["uvs"]):
            actual = [tuple(loop.uv) for loop in obj.data.uv_layers[index].data]
            if len(actual) != len(expected) or any(abs(a-b) > .000001 for pair, other in zip(actual, expected) for a, b in zip(pair, other)):
                raise RuntimeError("Split chain changed existing warehouse atlas UVs")
    for name in ("GatePlatformLeft", "GatePlatformRight"):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(directory/(name+".fbx")), use_anim=False)
        left = name.endswith("Left")
        x = .2 if left else -.2
        # A centre ray through the real exported collision must miss the slot.
        from mathutils.bvhtree import BVHTree
        for obj in bpy.context.scene.objects:
            if obj.type != "MESH" or not obj.name.startswith("Collision_"):
                continue
            points = [obj.matrix_world @ v.co for v in obj.data.vertices]
            tree = BVHTree.FromPolygons(points, [tuple(f.vertices) for f in obj.data.polygons])
            hit, _, _, _ = tree.ray_cast(Vector((x, 1.02, 1)), Vector((0, 0, -1)), 2)
            if hit is not None:
                raise RuntimeError("Slotted platform collision still fills the gate aperture")
    print("GATE CHAIN ORIGINAL ATLAS UVS / SLOT COLLISION APERTURES OK", flush=True)


def preview(roots, path):
    for root in roots:
        for obj in root.children_recursive:
            if obj.name.startswith("Collision_"):
                obj.hide_render = True
    roots[0].location = (-1.5, 0, 0)
    roots[1].location = (1.5, 0, 0)
    roots[2].location = (0, -.7, .2)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1200, 850, 100
    scene.world = bpy.data.worlds.new("Gate supplement review")
    scene.world.color = (.15, .15, .15)
    for name, p, power in (("Key", (0, -2, 7), 500), ("Fill", (2, 4, 5), 250)):
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "AREA"))
        bpy.context.collection.objects.link(light)
        light.location = p
        light.data.energy, light.data.size = power, 5
    camera = bpy.data.objects.new("Gate review", bpy.data.cameras.new("Gate review"))
    bpy.context.collection.objects.link(camera)
    camera.location = (3, -5, 6)
    camera.rotation_euler = (Vector((0, .3, 0))-camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type, camera.data.ortho_scale = "ORTHO", 7.2
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
    records = source_links()
    repeated = source_links()
    if records != repeated:
        raise RuntimeError("Original chain splitting is nondeterministic")
    platforms = [slotted_platform(True), slotted_platform(False)]
    checksum = passive.signature(platforms)
    if checksum != passive.signature([slotted_platform(True), slotted_platform(False)]):
        raise RuntimeError("Slotted platform geometry is nondeterministic")
    payload = passive.manifest(platforms, checksum)
    payload["models"].append(chain_model(records))
    payload["triangle_count"] += payload["models"][-1]["triangle_count"]
    payload.update(generator="tools/build-warehouse-gate-chain-3d-model.py", generator_version=VERSION,
        source={"fbx": str(ORIGINAL.relative_to(ROOT)).replace("\\", "/"), "sha256": hashlib.sha256(ORIGINAL.read_bytes()).hexdigest()},
        chain_signature=hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
        contract={"chain_count": 28, "original_geometry_and_uvs": True, "root_frame": "GateControl local metres",
            "open_joint_between": [18, 19], "joint_order": list(range(19, 29))+list(range(1, 19)),
            "links": [{"name": r["name"], "position": r["position"], "bounds_min": r["bounds_min"], "bounds_max": r["bounds_max"]} for r in records],
            "gate_lift": 3.05, "slot_world_bounds": {"min": [-2.32, 2.88, 11.32], "max": [2.32, 3.205, 11.72]},
            "replace_platforms": [{"name": "GatePlatformLeft", "position": [-1.5, 3.2, 10.5]},
                {"name": "GatePlatformRight", "position": [1.5, 3.2, 10.5]}], "upper_gallery_unchanged": True})
    payload = json.loads(json.dumps(payload))
    manifest = args.output_dir/"WarehouseGateChain3D.json"
    if args.validate_only and json.loads(manifest.read_text(encoding="utf8")) != payload:
        raise RuntimeError("Published gate supplement differs from deterministic source")
    roots = passive.build_objects(platforms)
    for root in roots:
        warehouse.appearance.preserve_metre_uv(root)
    roots.append(create_chain(records))
    if not args.validate_only:
        args.output_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            passive.export(root, args.output_dir/(root.name+".fbx"))
        manifest.write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")
        for image in bpy.data.images:
            if image.source == "FILE":
                image.pack()
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"WarehouseGateChain3D.blend"), check_existing=False)
        if not args.no_preview:
            preview(roots, args.source_dir/"WarehouseGateChain3D.png")
    verify_pack(args.output_dir, payload, records)
    print("WAREHOUSE GATE CHAIN / SLOTTED DECK ART CONTRACT OK "+payload["chain_signature"], flush=True)


if __name__ == "__main__":
    main()
