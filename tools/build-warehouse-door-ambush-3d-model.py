"""Deterministic paired door ambush on the retained HeroV2/NpcHumanV2 rigs.

Only the two new bone-only banks and measured manifest are published. Scene
placement, the neck cable and lift belong to the runtime owner. Existing hero,
worker, crane body and action assets are read without being rewritten.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


source = module("door_ambush_hero", "build-home-toilet-seated-actions.py")
crane = module("door_ambush_crane", "build-combat-crane-3d-model.py")
hero, common = source.hero, source.common
FPS = 100
POSE_HZ = 40
OUT = ROOT / "Assets/Resources/CombatDoorAmbush"
HERO_CLIPS = (("DoorAmbushEnter", 8., False), ("DoorAmbushHold", 1., True),
              ("DoorAmbushExit", .65, False))
CRANE_CLIPS = (("CraneDoorAmbush", 5., False), ("CraneDoorHold", 1., True),
               ("CraneDoorRetreat", 1.2, False))
DOOR_GRIP = Vector((-.18, -.54, 1.07))
CRANE_OFFSET = Vector((0., .64, .18))
HERO_HOOK_CONTACT = None
HOOK_WRIST_TO_IMPACT = .11 + .10 + .48


def smooth(value):
    t = min(1., max(0., value))
    return t*t*(3.-2.*t)


def gameplay(point):
    """Source -Y faces gameplay +Z after the production model's 180° yaw."""
    return [round(-point.x, 6), round(point.z, 6), round(-point.y, 6)]


def capture(builder):
    return {bone.name: common.BonePose(
        rotation_degrees=tuple(math.degrees(v) for v in bone.rotation_quaternion.to_euler("XYZ")),
        location_m=tuple(bone.location / builder.scale), scale=tuple(bone.scale))
        for bone in builder.result.rig.pose.bones}


def blend(builder, first, second, weight):
    if weight <= 0.: return first
    if weight >= 1.: return second
    builder.result.rig.animation_data.action = None
    builder._reset_pose(); builder._apply_pose(first)
    initial = {b.name: (b.location.copy(), b.rotation_quaternion.copy(), b.scale.copy())
               for b in builder.result.rig.pose.bones}
    builder._reset_pose(); builder._apply_pose(second)
    return {b.name: common.BonePose(
        rotation_degrees=tuple(math.degrees(v) for v in initial[b.name][1].slerp(b.rotation_quaternion, weight).to_euler("XYZ")),
        location_m=tuple(initial[b.name][0].lerp(b.location, weight) / builder.scale),
        scale=tuple(initial[b.name][2].lerp(b.scale, weight))) for b in builder.result.rig.pose.bones}


def hero_builder():
    bpy.ops.wm.open_mainfile(filepath=str(hero.DEFAULT_OUTPUT))
    rig = bpy.data.objects["RIG_Player"]
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20260908, "apose")
    builder = source.ToiletBuilder(config, hero.DEFAULT_FACE_ATLAS, hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    builder.points = builder.create_pose_points()
    rig.animation_data_create().action = None
    for obj in list(bpy.data.objects):
        if obj not in (rig, rig.parent): bpy.data.objects.remove(obj, do_unlink=True)
    for action in list(bpy.data.actions): bpy.data.actions.remove(action)
    bpy.context.scene.render.fps = FPS
    common.ANIMATION_FPS = FPS
    builder._reset_pose()
    return builder


def door_pose(builder, seconds):
    B = common.BonePose
    pull = math.sin(smooth((seconds-.8)/2.2)*math.pi*2.)
    pose = builder.merge_pose(builder.relaxed_pose(), {
        "spine": B(rotation_degrees=(5. + 2.*pull, 0., 1.2)),
        "chest": B(rotation_degrees=(6. + 2.*pull, 0., -2.)),
        "neck": B(rotation_degrees=(-3., 0., .8)),
        "head": B(rotation_degrees=(10., 0., -4.)),
    })
    builder.hand(pose, "R", DOOR_GRIP, (0., -.18, -.9837), (-.7, -.2, -.6))
    return pose


def hero_hold(builder, phase=0.):
    B = common.BonePose
    breath = math.sin(math.pi*phase)**2
    pose = builder.merge_pose(builder.relaxed_pose(), {
        "spine": B(rotation_degrees=(22. + .6*breath, 0., -8.)),
        "chest": B(rotation_degrees=(16. + .8*breath, 0., 6.)),
        "neck": B(rotation_degrees=(-10., 0., -6.)),
        "head": B(rotation_degrees=(12. - .8*breath, 0., 10.)),
    })
    # The recoil folds toward the struck shin while both boot contacts retain
    # the grounded entry. Inversion belongs to the subsequent physical tether.
    builder.hand(pose, "R", (-.18,-.22,.90+.004*breath), (0.,-.1,-.995), (-.8,-.3,-.5))
    builder.hand(pose, "L", (-.12,-.10,1.12+.004*breath), (-.1,-.2,-.975), (.8,-.3,-.3))
    return pose


def hero_pose(builder, name, seconds):
    neutral, held = builder.relaxed_pose(), hero_hold(builder)
    if name == "DoorAmbushHold": return hero_hold(builder, seconds)
    if name == "DoorAmbushExit": return blend(builder, held, neutral, smooth(seconds/.65))
    if seconds <= .8:
        return blend(builder, neutral, door_pose(builder, .8), smooth(seconds/.8))
    if seconds <= 3.: return door_pose(builder, seconds)
    action = seconds-3.
    if action < .45:
        return blend(builder, door_pose(builder, 3.), hero_defend(builder, action), smooth(action/.45))
    if action <= 4.7: return hero_defend(builder, action)
    # Only the last jab drives the leg-pain recoil and paired hook contact.
    return blend(builder, hero_defend(builder, 4.7), held, smooth((action-4.7)/.3))


def hero_defend(builder, seconds):
    B = common.BonePose
    effort = math.sin(seconds*math.tau/1.35)
    pose = builder.merge_pose(builder.relaxed_pose(), {
        "spine": B(rotation_degrees=(-3.+2.*effort,0.,7.)),
        "chest": B(rotation_degrees=(-5.,0.,-18.+3.*effort)),
        "neck": B(rotation_degrees=(0.,0.,-14.)),
        "head": B(rotation_degrees=(-8.,0.,-25.+5.*effort)),
    })
    builder.hand(pose,"R",(-.34,.08-.06*effort,1.19),(.1,-.6,-.79),(-.8,.1,-.2))
    builder.hand(pose,"L",(.26,-.04,1.23+.04*effort),(-.1,-.6,-.79),(.8,-.1,-.2))
    return pose


def crane_hold(builder, phase=0., threat=False):
    crane.pose(builder, "CraneIdle", 0.)
    rig = builder.result.rig
    # Crane place() works in the pre-enlargement anatomical authoring frame.
    # Targets are specified in measured source metres relative to its root.
    def unscale(point): return crane.BODY_PIVOT + (Vector(point)-crane.BODY_PIVOT)/crane.BODY_SCALE
    for side, sign in (("L", 1.), ("R", -1.)):
        shoulder = rig.pose.bones["upper_arm."+side].head.copy()
        pulse = math.sin(phase*math.tau/1.4)
        elbow = Vector((sign*.50, -.40, 1.18 if side == "R" else 1.55))
        wrist = (HERO_HOOK_CONTACT + Vector((0.,0.,HOOK_WRIST_TO_IMPACT))-CRANE_OFFSET
                 if side == "R" else Vector((.62,-.48,1.40)))
        if threat:
            elbow = Vector((sign*.60,-.35,1.50+.025*pulse))
            wrist = Vector((-.40,-.88+.04*pulse,1.16+.035*pulse)) if side == "R" else Vector((.42,-.70,1.42+.02*pulse))
        tip = wrist + Vector((0.,0.,-.17))
        crane.place(rig, "upper_arm."+side, unscale(shoulder), unscale(elbow), 1.05, roll=-sign*10.)
        crane.place(rig, "forearm."+side, unscale(elbow), unscale(wrist), .93, roll=sign*18.)
        crane.place(rig, "hand."+side, unscale(wrist), unscale(tip), 1.12, roll=-sign*18.)
    return capture(builder)


def crane_pose(builder, name, seconds):
    held = crane_hold(builder)
    idle = crane.pose(builder, "CraneIdle", 0.)
    if name == "CraneDoorHold": return crane_hold(builder, seconds)
    if name == "CraneDoorRetreat": return blend(builder, held, idle, smooth(seconds/1.2))
    if seconds < .7:
        return blend(builder,idle,crane_hold(builder,seconds,threat=True),smooth(seconds/.7))
    if seconds <= 4.7: return crane_hold(builder,seconds,threat=True)
    # A late downward jab keeps the blade visibly clear throughout the menace.
    return blend(builder,crane_hold(builder,4.7,threat=True),held,smooth((seconds-4.7)/.3))


def snapshot(builder, name, seconds):
    action = builder.result.actions[name].action
    builder.result.rig.animation_data.action = action
    frame = seconds*FPS
    bpy.context.scene.frame_set(math.floor(frame), subframe=frame%1.)
    bpy.context.view_layer.update()
    return {bone.name: bone.matrix.copy() for bone in builder.result.rig.pose.bones}


def digest(action):
    result = hashlib.sha256()
    for curve in sorted(common.iter_action_fcurves(action), key=lambda c: (c.data_path, c.array_index)):
        result.update(json.dumps([curve.data_path, curve.array_index,
            [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
    return result.hexdigest()


def build_bank(builder, clips, pose_function, label):
    records, endpoints = [], {}
    for name, seconds, loop in clips:
        keys = []
        count = max(2, round(seconds*POSE_HZ))
        for index in range(count+1):
            keys.append((index/count, pose_function(builder, name, seconds*index/count)))
        builder._create_action(name, "warehouse_door_ambush", seconds, loop, round(seconds*FPS), FPS, keys)
        print("Authored "+name, flush=True)
    for name, seconds, loop in clips:
        action = builder.result.actions[name].action
        # Bone-only means the controller and imported model roots stay fixed.
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError("Non-bone ambush curve")
            if curve.data_path.startswith('pose.bones["root"]'):
                values = [key.co.y for key in curve.keyframe_points]
                if max(values)-min(values) > .000001: raise ValueError("Ambush root motion")
        samples = []
        for time in sorted(set((0., min(seconds, 3.), seconds*.5, seconds))):
            snapshot(builder, name, time)
            rig = builder.result.rig
            sample = {"seconds": round(time, 6), "anchors_gameplay_local": {
                anchor: gameplay(rig.pose.bones[bone].head) for anchor, bone in
                (("pelvis", "pelvis"), ("neck", "neck"), ("head", "head"),
                 ("left_grip", "SOCKET_Grip.L"), ("right_grip", "SOCKET_Grip.R"),
                 ("left_foot", "foot.L"), ("right_foot", "foot.R"), ("right_shin", "shin.R"))}}
            samples.append(sample)
        endpoints[name] = (snapshot(builder, name, 0.), snapshot(builder, name, seconds))
        records.append(dict(name=name, duration_seconds=seconds, loop=loop,
                            signature=digest(action), samples=samples))
    error = 0.
    def compare(first, second):
        nonlocal error
        error = max(error, max(abs(first[bone][r][c]-second[bone][r][c])
            for bone in first for r in range(4) for c in range(4)))
    enter, hold, exit_name = (name for name, _, _ in clips)
    compare(endpoints[enter][1], endpoints[hold][0])
    compare(endpoints[hold][0], endpoints[hold][1])
    compare(endpoints[hold][1], endpoints[exit_name][0])
    compare(endpoints[enter][0], endpoints[exit_name][1])
    if error > .00003: raise ValueError(label+" endpoint mismatch: "+str(error))
    builder.result.rig.animation_data.action = None
    builder._reset_pose()
    return dict(rig=label, bone_count=len(builder.result.rig.data.bones),
                maximum_endpoint_error=round(error, 9), clips=records)


def file_signature(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def runtime_clearance_contract():
    runtime = ROOT / "Assets/Scripts/Runtime/Combat"
    clearance = (runtime/"CombatActor.CraneClearance.cs").read_text(encoding="utf8")
    anatomy = (runtime/"CombatActor.Crane.cs").read_text(encoding="utf8")
    provider = (runtime/"CombatCraneAssetProvider.cs").read_text(encoding="utf8")
    def array(text, name):
        match = re.search(name+r"\s*=\s*\{([^}]+)\}", text, re.S)
        if match is None: raise ValueError("Missing runtime crane contract: "+name)
        return match.group(1)
    names = re.findall(r'"([^"]+)"', array(anatomy, "CraneAnatomyBoneNames"))
    parents = [int(value.strip()) for value in array(clearance, "CraneLimbParents").split(",")]
    radii = [float(value.strip().rstrip("f")) for value in array(clearance, "CraneLimbRadii").split(",")]
    scale = float(re.search(r"BodyPoseScale\s*=\s*([\d.]+)f", provider).group(1))
    if not (len(names) == len(parents) == len(radii) == 19) or abs(scale-crane.BODY_SCALE) > .000001:
        raise ValueError("Runtime crane anatomy changed; review authored clearance mapping")
    return names, parents, [value*scale for value in radii]


def validate_crane_floor_and_contacts(builder, hero_contacts):
    """Validate the same full anatomical capsules used by the real scene.

    The minimum root height is conservative for the complete retreat and lift:
    runtime may only raise this actor after the ground-level gallery haul.
    All export-frame samples are measured after action curves have been built.
    """
    names, parents, radii = runtime_clearance_contract()
    root_height = .04 + CRANE_OFFSET.z
    clip_records = []
    minimum_floor = minimum_foot = minimum_foot_height = float("inf")
    maximum_top = maximum_hook_contact_error = maximum_hook_axis_error = 0.
    minimum_hook_solid = minimum_pre_jab_distance = float("inf")
    for clip, seconds, _ in CRANE_CLIPS:
        minima = [float("inf")]*len(names)
        maxima = [-float("inf")]*len(names)
        clip_floor = clip_foot = clip_foot_height = float("inf")
        clip_top = 0.
        for frame in range(round(seconds*FPS)+1):
            time = frame/FPS
            snapshot(builder, clip, time)
            bones = builder.result.rig.pose.bones
            heights = [bones[name].head.z + root_height for name in names]
            for index, name in enumerate(names):
                parent = parents[index]
                other = heights[parent] if parent >= 0 else heights[index]
                bottom = min(other, heights[index])-radii[index]
                top = max(other, heights[index])+radii[index]
                minima[index] = min(minima[index], bottom)
                maxima[index] = max(maxima[index], top)
                clip_floor = min(clip_floor, bottom)
                clip_top = max(clip_top, top)
                if name in ("foot.L", "foot.R"):
                    clip_foot = min(clip_foot, bottom)
                    clip_foot_height = min(clip_foot_height, heights[index])
            hand = bones["hand.R"]
            axis = (hand.tail-hand.head).normalized()
            wrist = hand.head + CRANE_OFFSET + Vector((0.,0.,.04))
            impact = wrist + axis*.11 + Vector((0.,0.,-.58))
            if clip == "CraneDoorAmbush" and time <= 4.7:
                minimum_pre_jab_distance = min(minimum_pre_jab_distance,
                    (impact-HERO_HOOK_CONTACT-Vector((0.,0.,.04))).length)
            if clip == "CraneDoorHold":
                maximum_hook_axis_error = max(maximum_hook_axis_error, (axis-Vector((0.,0.,-1.))).length)
                maximum_hook_contact_error = max(maximum_hook_contact_error,
                    (Vector(gameplay(impact))-Vector(hero_contacts[frame]["hook"])).length)
                # Conservative authored hook envelope: eye→furthest forged tip.
                minimum_hook_solid = min(minimum_hook_solid, wrist.z-.11-.10-.61)
        minimum_floor = min(minimum_floor, clip_floor)
        minimum_foot = min(minimum_foot, clip_foot)
        minimum_foot_height = min(minimum_foot_height, clip_foot_height)
        maximum_top = max(maximum_top, clip_top)
        clip_records.append(dict(clip=clip, samples_hz=FPS,
            minimum_floor_clearance_m=round(clip_floor, 7),
            minimum_foot_capsule_clearance_m=round(clip_foot, 7),
            minimum_foot_bone_world_height_m=round(clip_foot_height, 7),
            maximum_capsule_world_height_m=round(clip_top, 7),
            anatomy=[dict(bone=name, parent=names[parents[index]] if parents[index] >= 0 else None,
                radius_m=round(radii[index], 7), minimum_capsule_height_m=round(minima[index], 7),
                maximum_capsule_height_m=round(maxima[index], 7)) for index, name in enumerate(names)]))
    # Source keys are authored at40Hz and quantized to100Hz export frames;
    # the bounded sub-.1mm contact variation includes that time rounding.
    if (minimum_floor < .02 or minimum_foot_height-.15 < .02 or maximum_top >= 3.02 or
            maximum_hook_contact_error > .0001 or maximum_hook_axis_error > .0001 or
            minimum_hook_solid < .02 or minimum_pre_jab_distance < .15):
        raise ValueError("Paired crane floor/gallery/contact contract failed: "+str(
            (minimum_floor, maximum_top, maximum_hook_contact_error, maximum_hook_axis_error,
             minimum_hook_solid,minimum_pre_jab_distance)))
    builder.result.rig.animation_data.action = None
    builder._reset_pose()
    return dict(runtime_source="Assets/Scripts/Runtime/Combat/CombatActor.CraneClearance.cs",
        actor_minimum_root_world_y=round(root_height, 7), minimum_floor_clearance_m=round(minimum_floor, 7),
        minimum_foot_capsule_clearance_m=round(minimum_foot, 7),
        minimum_foot_bone_world_height_m=round(minimum_foot_height, 7),
        conservative_foot_radius_m=.15,
        minimum_conservative_foot_clearance_m=round(minimum_foot_height-.15, 7),
        maximum_capsule_world_height_m=round(maximum_top, 7),
        victim_part="RightShin", maximum_world_hook_impact_contact_error_m=round(maximum_hook_contact_error, 9),
        maximum_hook_down_axis_error=round(maximum_hook_axis_error, 9),
        minimum_hook_impact_clearance_before_final_jab_m=round(minimum_pre_jab_distance, 7),
        minimum_terminal_hook_solid_world_height_m=round(minimum_hook_solid, 7), clips=clip_records)


def cable_guide():
    """Open gallery fairleads and two ceiling-mounted neck-cable sheaves."""
    root = crane.empty("CraneDoorCableGuide", None, (0., 0., 0.))
    pieces = []
    def box(name, center, size):
        vertices = [(center[0]+x*size[0]*.5, center[1]+y*size[1]*.5, center[2]+z*size[2]*.5)
                    for x, y, z in ((-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                                    (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1))]
        return crane.unity_mesh(name, root, vertices, ((0,3,2,1),(4,5,6,7),(0,1,5,4),
            (1,2,6,5),(2,3,7,6),(3,0,4,7)))
    # Eyelets sit inside the well so the overhead feed does not intersect the
    # slab. This cantilevered plate reaches back beneath the actual gallery.
    pieces.append(box("FairleadMountingPlate", (0., .0625, .105), (1.4, .035, .13)))
    def lower_slot(center):
        # A lower semicircle plus two upright cheeks leaves a real upward exit.
        # Two retained 14mm-radius cable segments fit side by side in its 90mm gap.
        sides = 8
        vertices = []
        for depth in (-.0175, .0175):
            for radius in (.065, .045):
                for index in range(sides+1):
                    angle = math.pi + math.pi*index/sides
                    vertices.append((center+radius*math.cos(angle), radius*math.sin(angle), depth))
        width = sides+1
        faces = []
        for index in range(sides):
            next_index = index+1
            faces.extend(((index,next_index,width+next_index,width+index),
                (2*width+index,3*width+index,3*width+next_index,2*width+next_index),
                (index,2*width+index,2*width+next_index,next_index),
                (width+index,width+next_index,3*width+next_index,3*width+index)))
        faces.extend(((0,width,3*width,2*width),
                      (sides,2*width+sides,3*width+sides,width+sides)))
        pieces.append(crane.unity_mesh("OpenFairleadBend", root, vertices, faces))
        for sign in (-1., 1.):
            pieces.append(box("OpenFairleadCheek", (center+sign*.055,.025,0.), (.02,.05,.035)))
            pieces.append(box("FairleadBackArm", (center+sign*.055,.04,.05), (.02,.025,.10)))
    for index, x in enumerate((-.55, -.33, -.11, .11, .33, .55)):
        lower_slot(x)
        crane.empty("ANCHOR_Guide"+str(index), root, (x, 0., 0.))
    for index, sign in enumerate((-1., 1.)):
        # The rope rests in the inward groove; outer flanges retain its depth.
        # Both hanging legs stay on the open, inward side of the sheave.
        center = (sign*.24, 9.90-2.94, 3.85-5.94)
        pieces.append(crane.ring_part("NeckGuideGroove", root, center, .046, .012, .06, "XY", 12))
        for depth in (-.038, .038):
            flange = (center[0], center[1], center[2]+depth)
            pieces.append(crane.ring_part("NeckGuideFlange", root, flange, .075, .012, .016, "XY", 12))
            pieces.append(box("NeckGuideHanger", (center[0],10.025-2.94,center[2]+depth*1.45),
                              (.025,.31,.025)))
        pieces.append(crane.ring_part("NeckGuideAxle", root, center, .012, .004, .14, "XY", 8))
        pieces.append(box("NeckGuideCeilingPlate", (center[0],10.18-2.94,center[2]), (.20,.04,.20)))
        crane.empty("ANCHOR_NeckGuide"+str(index), root, (sign*.18,center[2],center[1]))
    crane.join_parts(pieces, "CraneDoorCableGuideVisible")
    bpy.context.view_layer.update()
    payload = crane.passive_measure(root)
    payload["recommended_world_root"] = [0., 2.94, 5.94]
    payload["support_slot_open_axis"] = [0., 1., 0.]
    payload["support_slot_inner_radius_m"] = .045
    payload["rope_radius_m"] = .014
    payload["ceiling_mount_world_y"] = 10.2
    payload["neck_guide_world_positions"] = [[-.18,9.90,3.85],[.18,9.90,3.85]]
    anchors = {entry["name"]: entry["position"] for entry in payload["anchors"]}
    if len(anchors) != 8 or len([obj for obj in root.children_recursive if obj.type == "MESH"]) != 1:
        raise ValueError("Cable guides require one shared mesh and eight measured anchors")
    for index, expected in enumerate(payload["neck_guide_world_positions"]):
        world = [anchors["ANCHOR_NeckGuide"+str(index)][axis]+payload["recommended_world_root"][axis]
                 for axis in range(3)]
        if max(abs(world[axis]-expected[axis]) for axis in range(3)) > .00001:
            raise ValueError("Ceiling cable guide anchor differs from the scene contract")
    if abs(payload["bounds_max"][1]+2.94-10.2) > .00001:
        raise ValueError("Ceiling cable guide brackets do not meet the warehouse roof")
    return root, payload


def publish_guide_only(args):
    """Update passive geometry while preserving both published action banks."""
    retained = [OUT/name for name in ("HeroActions.fbx", "CraneActions.fbx")]
    retained.extend(OUT/name for name in ("HeroActions.fbx.meta", "CraneActions.fbx.meta", "CraneDoorCableGuide.fbx.meta"))
    before = {str(path): file_signature(path) for path in retained}
    payload = json.loads((OUT/"DoorAmbush3D.json").read_text(encoding="utf8"))
    signature = payload.pop("signature")
    if signature != hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest():
        raise ValueError("The retained animation manifest signature is invalid")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    guide, payload["cable_guide"] = cable_guide()
    payload["signature"] = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    manifest = args.output_dir/"DoorAmbush3D.json"
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf8")) != payload:
            raise ValueError("Published cable guides differ from deterministic measurements")
        if not (args.output_dir/"CraneDoorCableGuide.fbx").is_file():
            raise ValueError("Missing published cable guide model")
    else:
        crane.export_passive(guide, args.output_dir/"CraneDoorCableGuide.fbx")
        manifest.write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")
    if before != {str(path): file_signature(path) for path in retained}:
        raise ValueError("Passive cable guide publication changed retained actions or import metadata")
    print("WAREHOUSE DOOR AMBUSH CABLE GUIDE CONTRACT OK "+payload["signature"], flush=True)


def main():
    global HERO_HOOK_CONTACT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=OUT)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--guide-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    args.output_dir.mkdir(parents=True, exist_ok=True)
    if args.guide_only:
        publish_guide_only(args)
        return
    protected = [hero.DEFAULT_OUTPUT, ROOT/"ArtSource/VillageLife/StationWorker.blend",
                 ROOT/"Assets/Resources/Player/Player3DV2.prefab", ROOT/"Assets/Resources/Combat/CraneActions.fbx"]
    before = {str(path): file_signature(path) for path in protected}
    args.output_dir.mkdir(parents=True, exist_ok=True)
    builder = hero_builder()
    hero_payload = build_bank(builder, HERO_CLIPS, hero_pose, "HeroV2")
    neutral = hero_payload["clips"][0]["samples"][0]["anchors_gameplay_local"]
    held = hero_payload["clips"][1]["samples"][0]["anchors_gameplay_local"]
    grip_error = foot_error = 0.
    for index in range(289):
        time = .8 + 7.2*index/288
        snapshot(builder, "DoorAmbushEnter", time)
        if time <= 3.:
            grip_error = max(grip_error, (builder.result.rig.pose.bones["SOCKET_Grip.R"].head-DOOR_GRIP).length)
        for side, anchor in (("L", "left_foot"), ("R", "right_foot")):
            measured = Vector(gameplay(builder.result.rig.pose.bones["foot."+side].head))
            foot_error = max(foot_error, (measured-Vector(neutral[anchor])).length)
    if grip_error > .0003 or foot_error > .00001:
        raise ValueError("Door handle contact or planted feet mismatch")
    hero_payload["maximum_door_grip_error"] = round(grip_error, 9)
    hero_payload["maximum_foot_anchor_error"] = round(foot_error, 9)
    hero_contacts = []
    for frame in range(FPS+1):
        snapshot(builder, "DoorAmbushHold", frame/FPS)
        bones = builder.result.rig.pose.bones
        contact = bones["foot.R"].head + (bones["shin.R"].head-bones["foot.R"].head).normalized()*.14
        if HERO_HOOK_CONTACT is None: HERO_HOOK_CONTACT = contact.copy()
        hero_contacts.append({"hook": gameplay(contact+Vector((0., 0., .04)))})
    builder.result.rig.animation_data.action = None
    builder._reset_pose()
    if not args.validate_only: common.export_animation_fbx(args.output_dir/"HeroActions.fbx", builder.result)
    builder = crane.zombie.load_builder()
    crane_payload = build_bank(builder, CRANE_CLIPS, crane_pose, "NpcHumanV2")
    crane_payload["floor_and_contact_contract"] = validate_crane_floor_and_contacts(builder, hero_contacts)
    if not args.validate_only: common.export_animation_fbx(args.output_dir/"CraneActions.fbx", builder.result)
    guide, guide_payload = cable_guide()
    if not args.validate_only: crane.export_passive(guide, args.output_dir/"CraneDoorCableGuide.fbx")
    payload = dict(generator="tools/build-warehouse-door-ambush-3d-model.py", generator_version="paired-door-leg-v1",
        root_motion=False, animation_events=0, fps=FPS, pose_samples_hz=POSE_HZ,
        attack_cue_seconds=3., capture_seconds=8., final_jab_start_seconds=7.7, grounded_root_height=.04,
        gameplay_facing=[0., 0., 1.], source_to_gameplay_axes="-x,z,-y",
        hero_neutral_pelvis=list(neutral["pelvis"]), hero_action_pelvis=list(held["pelvis"]),
        hero_exit_pelvis=list(neutral["pelvis"]), hero_door_grip=gameplay(DOOR_GRIP),
        hero_hook_contact=gameplay(HERO_HOOK_CONTACT), hero_hook_victim_part="RightShin",
        hero_hook_distance_from_ankle_m=.14, hook_down_gameplay=[0.,-1.,0.],
        hook_wrist_to_impact_m=HOOK_WRIST_TO_IMPACT,
        hero_hold_left_palm=list(held["left_grip"]),
        hero_hold_right_palm=list(held["right_grip"]), crane_root_offset=gameplay(CRANE_OFFSET),
        entry_pelvis_gameplay_local=neutral["pelvis"], exit_pelvis_gameplay_local=list(neutral["pelvis"]),
        door_grip_gameplay_local=gameplay(DOOR_GRIP), door_contact_start_seconds=.8,
        door_contact_end_seconds=3., crane_root_relative_to_hero_gameplay=gameplay(CRANE_OFFSET),
        hero=hero_payload, crane=crane_payload, cable_guide=guide_payload)
    payload["signature"] = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    manifest = args.output_dir/"DoorAmbush3D.json"
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf8")) != payload:
            raise ValueError("Published door ambush differs from deterministic measurements")
        for name in ("HeroActions.fbx", "CraneActions.fbx", "CraneDoorCableGuide.fbx"):
            if not (args.output_dir/name).is_file(): raise ValueError("Missing ambush bank "+name)
    else: manifest.write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")
    if before != {str(path): file_signature(path) for path in protected}:
        raise ValueError("Door ambush generation modified a retained production asset")
    print("WAREHOUSE DOOR AMBUSH ART CONTRACT OK "+payload["signature"], flush=True)


if __name__ == "__main__": main()
