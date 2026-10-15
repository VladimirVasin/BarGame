#!/usr/bin/env python3
"""Deterministic warehouse-only fish puzzle: matching cuts, continuous skin and bone-only death.

Unity metres, head +X, fish lying on its side in XZ, rest bottom Y=0.
The passive parts and joined skin share the exact sampled outline. Only the
joined model imports animation; no existing warehouse module is rewritten.
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

spec = importlib.util.spec_from_file_location("fish_passive", ROOT / "tools/build-city-fair-3d-model.py")
passive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(passive)
VERSION = "1.0.0"
OUT = ROOT / "Assets/Resources/CombatWarehouse/FishAssembly"
SOURCE = ROOT / "ArtSource/CombatWarehouse/FishAssembly"
SURFACES = {"SpoiledFish": (.38, .42, .29), "SpoiledFishFlesh": (.43, .34, .25),
            "SpoiledFishDark": (.075, .085, .055), "WornSteel": (.42, .44, .39),
            "ConcreteDark": (.30, .32, .29)}
NAMES = ("FishTail", "FishBelly", "FishHead", "WarehouseGateKey", "FishPlaySurface", "AssembledFish")
CENTRES = (-.12, 0., .12)
SECTIONS = ((-.18, .002, .006), (-.15, .008, .012), (-.12, .013, .022),
            (-.09, .019, .033), (-.06, .022, .042), (-.03, .024, .048),
            (0., .025, .05), (.03, .024, .048), (.06, .022, .042),
            (.09, .021, .037), (.12, .018, .031), (.15, .012, .022), (.18, .004, .009))
SIDES = 16
FPS = 30
CONVULSE_SECONDS = 4.
DEATH_SECONDS = 1.


def positive(geometry):
    verts, faces = geometry
    return verts, faces if bp.signed_volume(geometry) > 0 else [tuple(reversed(f)) for f in faces]


def body(low, high, offset=0.):
    sections = [s for s in SECTIONS if low - 1e-7 <= s[0] <= high + 1e-7]
    verts = [(x-offset, .025+ry*math.cos(i*math.tau/SIDES), rz*math.sin(i*math.tau/SIDES))
             for x, ry, rz in sections for i in range(SIDES)]
    faces = [tuple(reversed(range(SIDES))), tuple(range(len(verts)-SIDES, len(verts)))]
    for row in range(len(sections)-1):
        for i in range(SIDES):
            a, b = row*SIDES+i, row*SIDES+(i+1)%SIDES
            faces.append((a, b, b+SIDES, a+SIDES))
    return positive((verts, faces))


def fin(points, offset=0.):
    verts = [(x-offset, y, z) for y in (.023, .027) for x, z in points]
    count = len(points)
    faces = [tuple(reversed(range(count))), tuple(range(count, 2*count))]
    faces += [(i, (i+1)%count, (i+1)%count+count, i+count) for i in range(count)]
    return positive((verts, faces))


def add_detail(item, index, offset):
    if index == 0:
        item.add("SpoiledFishDark", fin([(-.18, -.048), (-.147, -.012), (-.147, .012),
                 (-.18, .048), (-.17, 0)], offset), "TailFin")
    if index == 1:
        for sign in (-1, 1):
            item.add("SpoiledFishDark", fin([(-.042, sign*.043), (.035, sign*.045),
                     (.012, sign*.069)], offset), "BodyFins")
        # Dull mottled patches have real thin volume and no new texture sheet.
        for x, z in ((-.025, -.019), (.015, .024), (.04, -.011)):
            item.add("SpoiledFishDark", passive.ellipsoid((x-offset, .048, z), (.017, .0025, .011), 8, 4), "SkinMottle")
    if index == 2:
        item.add("SpoiledFishFlesh", passive.ellipsoid((.132-offset, .0415, -.012), (.021, .009, .019), 12, 6), "EyeRim")
        item.add("SpoiledFishDark", passive.ellipsoid((.134-offset, .046, -.012), (.012, .004, .012), 12, 6), "Eye")
        item.add("SpoiledFishDark", passive.ellipsoid((.173-offset, .029, 0), (.016, .014, .017), 10, 6), "Mouth")
        for z in (-.024, -.012, 0, .012):
            item.rod("SpoiledFishDark", (.09-offset, .043, z-.003), (.1-offset, .044, z+.003), .0014, "Gills", 6)


def fish_item(index):
    low, high, offset = -.18+index*.12, -.06+index*.12, CENTRES[index]
    item = passive.Item(NAMES[index])
    item.add("SpoiledFish", body(low, high, offset), "Skin")
    item.add("ConcreteDark", body(low, high, offset), "Collision_Physics")
    add_detail(item, index, offset)
    for x in (low, high):
        if abs(abs(x)-.06) > .0001:
            continue
        _, ry, rz = next(section for section in SECTIONS if abs(section[0]-x) < .0001)
        item.add("SpoiledFishFlesh", passive.ellipsoid((x-offset, .025, 0), (.0008, ry*1.94, rz*1.94), 16, 6), "CutFace")
    item.anchor("AssemblyOrigin", (-offset, 0, 0))
    if index != 0:
        item.anchor("MateTail", (low-offset, .025, 0))
    if index != 2:
        item.anchor("MateHead", (high-offset, .025, 0))
    return item


def assembled_item():
    item = passive.Item("AssembledFish")
    item.add("SpoiledFish", body(-.18, .18), "Skin")
    for index in range(3):
        add_detail(item, index, 0.)
    item.anchor("KeySpit", (.181, .029, 0))
    item.anchor("AssemblyOrigin", (0, 0, 0))
    return item


def key_item():
    item = passive.Item("WarehouseGateKey")
    # A plain worn warded key, without a label, brand or decorative emblem.
    bow = passive.ring([(-.002, .014), (.002, .014), (.002, .009), (-.002, .009)], 16)
    item.add("WornSteel", kit.translated(bow, (-.029, .004, 0)), "KeyBow")
    item.box("WornSteel", (.006, .004, 0), (.055, .007, .007), "KeyStem", .001)
    item.box("WornSteel", (.026, .004, -.008), (.018, .007, .018), "KeyBit", .0007)
    item.box("SpoiledFishDark", (.022, .0077, -.012), (.005, .0005, .006), "KeyWear", .0001)
    item.anchor("InspectionCenter", (0, .004, 0))
    item.box("ConcreteDark", (-.003, .004, -.004), (.08, .01, .033), "Collision_Physics", .0001)
    return item


def play_surface():
    item = passive.Item("FishPlaySurface")
    item.box("ConcreteDark", (0, .77, 0), (1.92, .02, .86), "Collision_PlayFloor", .0005)
    for sign in (-1, 1):
        item.box("ConcreteDark", (sign*.981, .9, 0), (.042, .24, .92), "Collision_EndRims", .001)
        item.box("ConcreteDark", (0, .9, sign*.451), (1.92, .24, .042), "Collision_SideRims", .001)
    item.anchor("PlaySurface", (0, .78, 0))
    return item


def items():
    passive.SURFACES, passive.VERSION = SURFACES, VERSION
    return [*(fish_item(i) for i in range(3)), key_item(), play_surface(), assembled_item()]


def create_rig(root):
    armature = bpy.data.armatures.new("FishSkeleton")
    rig = bpy.data.objects.new("RIG_Fish", armature)
    bpy.context.collection.objects.link(rig)
    rig.parent = root
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, start, end, parent in (("body", (0, 0, .025), (.06, 0, .025), None),
            ("tail", (-.03, 0, .025), (-.18, 0, .025), "body"),
            ("head", (.03, 0, .025), (.18, 0, .025), "body")):
        bone = armature.edit_bones.new(name)
        bone.head, bone.tail = start, end
        if parent:
            bone.parent = armature.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for obj in list(root.children_recursive):
        if obj.type != "MESH":
            continue
        obj.parent = rig
        groups = {name: obj.vertex_groups.new(name=name) for name in ("body", "tail", "head")}
        for vertex in obj.data.vertices:
            x = vertex.co.x
            tail = min(1., max(0., (-x-.015)/.105))
            head = min(1., max(0., (x-.015)/.105))
            weights = {"tail": tail, "head": head, "body": 1.-tail-head}
            for name, weight in weights.items():
                if weight > 0:
                    groups[name].add([vertex.index], weight, "REPLACE")
        modifier = obj.modifiers.new("Continuous fish skin", "ARMATURE")
        modifier.object = rig
    spit = next(obj for obj in root.children_recursive if obj.name == "KeySpit")
    # Bone parenting includes the bone's length offset. Preserve the authored
    # mouth world matrix so the anchor follows the skin without moving at rest.
    bpy.context.view_layer.update()
    world = spit.matrix_world.copy()
    spit.parent, spit.parent_type, spit.parent_bone = rig, "BONE", "head"
    bpy.context.view_layer.update()
    spit.matrix_world = world
    rig.animation_data_create()
    return rig


def animate(rig):
    scene = bpy.context.scene
    scene.render.fps = FPS
    for name, duration in (("FishConvulse", CONVULSE_SECONDS), ("FishDeath", DEATH_SECONDS)):
        action = bpy.data.actions.new(name)
        rig.animation_data.action = action
        for frame in range(round(duration*FPS)+1):
            scene.frame_set(frame)
            t = frame/FPS
            if name == "FishConvulse":
                # Start/end at the shared rest pose; bursts tighten over time.
                envelope = math.sin(math.pi*t/duration)**2
                pulse = math.sin(t*(22.+t*4.))
                side = math.sin(t*(28.+t*5.))
                bend, lift = .62*envelope*side, .24*envelope*pulse
            else:
                envelope = math.sin(math.pi*t/duration)**2 * math.exp(-t*3.)
                bend, lift = .38*envelope*math.sin(t*29.), .18*envelope*math.sin(t*21.)
            for bone in rig.pose.bones:
                bone.rotation_mode = "XYZ"
                factor = {"body": .24, "tail": -1., "head": .65}[bone.name]
                bone.rotation_euler = (lift*factor, 0., bend*factor)
                # Keep the central contact strip grounded; the root never jumps.
                bone.location = (0, 0, 0)
            bpy.context.view_layer.update()
            lowest = 0.
            for obj in rig.children:
                if obj.type != "MESH":
                    continue
                evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
                mesh = evaluated.to_mesh()
                try:
                    lowest = min(lowest, min((evaluated.matrix_world @ v.co).z for v in mesh.vertices))
                finally:
                    evaluated.to_mesh_clear()
            # A bone translation raises only enough skin to keep the table
            # contact plane intact; the imported model/root never translates.
            rig.pose.bones["body"].location.z = max(0., -lowest)
            for bone in rig.pose.bones:
                bone.keyframe_insert("rotation_euler", frame=frame, group=bone.name)
                bone.keyframe_insert("location", frame=frame, group=bone.name)
        action.use_fake_user = True
    rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    scene.frame_set(0)
    bpy.context.view_layer.update()


def export_rig(root, path):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for obj in root.children_recursive:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"EMPTY", "MESH", "ARMATURE"},
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0., bake_anim_step=1., armature_nodetype="NULL",
        use_custom_props=True, use_mesh_modifiers=True, mesh_smooth_type="FACE")


def verify_skin(rig, root):
    # The assembled body is a single skin. Shared cross-section vertices retain
    # the same weights, so no independent cut pivots can expose gaps in motion.
    skin = next(obj for obj in root.children_recursive if obj.type == "MESH" and obj.name.startswith("Skin__"))
    if len(skin.data.vertices) != len(SECTIONS)*SIDES:
        raise RuntimeError("Joined fish lost its one continuous cross-section skin")
    maximum = 0.
    samples = []
    for action in bpy.data.actions:
        rig.animation_data.action = action
        for frame in range(round(action.frame_range[1])+1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            samples.append((action.name, frame, [[round(v, 9) for v in (*bone.rotation_euler, *bone.location)] for bone in rig.pose.bones]))
            evaluated = skin.evaluated_get(bpy.context.evaluated_depsgraph_get())
            mesh = evaluated.to_mesh()
            try:
                for vertex in mesh.vertices:
                    p = evaluated.matrix_world @ vertex.co
                    if not all(math.isfinite(v) for v in p):
                        raise RuntimeError("Fish action produced non-finite skin")
                    if p.z < -.00001:
                        raise RuntimeError("Fish convulsion penetrates the authored table contact plane")
                    maximum = max(maximum, p.length)
            finally:
                evaluated.to_mesh_clear()
        for frame in (0, round(action.frame_range[1])):
            bpy.context.scene.frame_set(frame)
            if any(max(abs(v) for v in bone.rotation_euler) > .00001 for bone in rig.pose.bones):
                raise RuntimeError("Fish actions must share the exact neutral endpoints")
    rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    bpy.context.scene.frame_set(0)
    signature = hashlib.sha256(json.dumps(samples, separators=(",", ":")).encode()).hexdigest()
    return round(maximum, 6), signature


def verify_animated_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    rig = next((obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"), None)
    if rig is None or set(rig.data.bones.keys()) != {"body", "tail", "head"}:
        raise RuntimeError("Exported fish lost its three-bone continuous skin rig")
    if not all(obj.vertex_groups for obj in rig.children if obj.type == "MESH"):
        raise RuntimeError("Exported fish contains an unweighted surface")
    clips = {}
    for action in bpy.data.actions:
        for name, duration in (("FishConvulse", CONVULSE_SECONDS), ("FishDeath", DEATH_SECONDS)):
            if name in action.name:
                actual = (action.frame_range[1]-action.frame_range[0])/FPS
                if abs(actual-duration) > .001:
                    raise RuntimeError("Exported fish clip duration differs: "+action.name)
                clips[name] = actual
    if len(clips) != 2:
        raise RuntimeError("Exported fish lost a convulsion/death animation")
    print("FISH EXPORTED RIG / SHARED SKIN / TWO CLIP DURATIONS OK "+json.dumps(clips), flush=True)


def preview(roots, path):
    for index, root in enumerate(roots):
        for child in root.children_recursive:
            if child.name.startswith("Collision_"):
                child.hide_render = True
        if root.name == "FishPlaySurface":
            root.hide_render = True
            for child in root.children_recursive:
                child.hide_render = True
        elif index < 3:
            root.location = (CENTRES[index]+(index-1)*.012, -.15, 0)
        elif root.name == "AssembledFish":
            root.location = (0, .12, 0)
        else:
            root.location = (.27, -.15, 0)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1400, 850, 100
    scene.world = bpy.data.worlds.new("Fish review world")
    scene.world.color = (.12, .12, .12)
    scene.view_settings.view_transform = "Standard"
    for name, p, energy in (("Key", (0, -.4, 1.2), 8), ("Fill", (.5, .2, .8), 3)):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.size = energy, 1.
        light = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(light)
        light.location = p
    camera = bpy.data.objects.new("Fish review", bpy.data.cameras.new("Fish review"))
    bpy.context.collection.objects.link(camera)
    camera.location = (.2, -.65, .9)
    camera.rotation_euler = (Vector((.035, 0, .02))-camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type, camera.data.ortho_scale = "ORTHO", .72
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
    authored = items()
    checksum = passive.signature(authored)
    if checksum != passive.signature(items()):
        raise RuntimeError("Fish assembly repeated geometry differs")
    payload = passive.manifest(authored, checksum)
    payload.update(generator="tools/build-warehouse-fish-assembly-3d-model.py", passive=False,
        contract={"part_order": ["Tail", "Belly", "Head"], "part_centres": list(CENTRES),
            "seam_x": [-.06, .06], "head_forward": "+X", "rest_bottom": 0.,
            "play_surface_height": .78, "play_area_half_extents": [.88, .38],
            "fish_length": .36, "key_model": "WarehouseGateKey", "key_spit": [.181, .029, 0],
            "shared_rest_geometry": True, "joined_skin": "continuous cross-section mesh / shared bone weights",
            "clips": {"FishConvulse": CONVULSE_SECONDS, "FishDeath": DEATH_SECONDS},
            "animation": "bone-only/in-place/no events/exact neutral endpoints", "maximum_skin_radius": 0.})
    roots = passive.build_objects(authored)
    rig = create_rig(roots[-1])
    animate(rig)
    radius, action_signature = verify_skin(rig, roots[-1])
    payload["contract"]["maximum_skin_radius"] = radius
    payload["contract"]["animation_signature"] = action_signature
    payload = json.loads(json.dumps(payload))
    manifest_path = args.output_dir/"WarehouseFishAssembly3D.json"
    if args.validate_only:
        if json.loads(manifest_path.read_text(encoding="utf8")) != payload:
            raise RuntimeError("Published fish assembly differs from deterministic geometry/action contract")
    else:
        args.output_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        for root in roots[:-1]:
            passive.export(root, args.output_dir/(root.name+".fbx"))
        export_rig(roots[-1], args.output_dir/"AssembledFish.fbx")
        manifest_path.write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"WarehouseFishAssembly3D.blend"), check_existing=False)
        if not args.no_preview:
            preview(roots, args.source_dir/"WarehouseFishAssembly3D.png")
    passive.verify_fbx(args.output_dir, payload)
    verify_animated_fbx(args.output_dir/"AssembledFish.fbx")
    print("WAREHOUSE FISH ASSEMBLY ART CONTRACT OK "+checksum, flush=True)


if __name__ == "__main__":
    main()
