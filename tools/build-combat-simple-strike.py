"""One authored two-hand shoulder swing; block and unrelated bank actions stay intact."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import importlib.util

spec = importlib.util.spec_from_file_location("simple_strike_combat", ROOT / "tools/build-combat-test-3d-model.py")
m = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = m
spec.loader.exec_module(m)
r = m.module("simple_strike_retained", "combat_forehand_repair.py")
import combat_bat_continuity
NAMES = ("CombatAttack", "CombatReleaseLight", "CombatReleaseHeavy", "CombatCharge", "CombatRecoil", "CombatReady")
BAT_GRIP = -.085
HELD_SHAFT_ROLL_DEGREES = -148.4
SOURCE_HASH = hashlib.sha256(Path(__file__).read_bytes() +
    Path(combat_bat_continuity.__file__).read_bytes()).hexdigest()


def smooth(value):
    t = max(0., min(1., value))
    return t * t * (3. - 2. * t)


def body_pose(builder, ready, turn):
    builder._reset_pose(); builder._apply_pose(ready)
    for name, fraction in (("pelvis", .15), ("spine", .35), ("chest", .50)):
        bone = builder.result.rig.pose.bones[name]
        rotation = Quaternion((0., 0., 1.), math.radians(turn * fraction)) @ bone.matrix.to_quaternion()
        bone.matrix = Matrix.Translation(bone.head.copy()) @ rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()
    return builder.pin_supports(builder.snapshot_pose())


def pin_bat_left(builder, pose):
    builder._reset_pose(); builder._apply_pose(pose)
    rig=builder.result.rig; hand=rig.pose.bones["hand.L"]; rest=hand.bone
    centre,rest_axis,_=builder.hand_frame("L")
    origin,bar=builder.weapon_frame();axis=bar @ Vector((0.,1.,0.))
    initial=hand.matrix.to_quaternion()
    delta=initial.to_matrix() @ rest.matrix_local.to_3x3().inverted()
    initial=(delta @ rest_axis).rotation_difference(axis) @ initial
    pole=rig.pose.bones["forearm.L"].head-rig.pose.bones["upper_arm.L"].head
    closest=None
    for degrees in (0.,*(sign*angle for angle in range(3,181,3) for sign in (1.,-1.))):
        rotation=(Quaternion(axis,math.radians(degrees)) @ initial).to_matrix()
        delta=rotation @ rest.matrix_local.to_3x3().inverted()
        wrist=origin+axis*BAT_GRIP-delta @ (centre-rest.head_local)
        choice=builder.grip_elbow("L",wrist,rotation,pole,total_limit=65.,support_head=True,weapon_check=True)
        if choice is not None and builder.weapon_clearance(arms={"L":(rig.pose.bones["upper_arm.L"].head,choice[2],wrist)})["body_gap_m"] >= .005:
            builder.solve_arm("L",wrist,rotation,choice[3]);return builder.snapshot_pose()
    raise ValueError("No continuous cylindrical bat grasp for the left hand")


def align_bat(builder, pose, **kwargs):
    kwargs.pop("left_pole",None);kwargs.pop("support_offset",None)
    complete_clearance=builder.weapon_clearance
    def pending_support_clearance(origin=None,rotation=None,arms=None):
        return complete_clearance(origin,rotation,arms,ignore_arm_sides=("L",))
    builder.weapon_clearance=pending_support_clearance
    try: right=builder.align_right_grip(pose,support=False,**kwargs)
    finally: builder.weapon_clearance=complete_clearance
    return pin_bat_left(builder,right)


def author_stops(builder, ready):
    global HELD_SHAFT_ROLL_DEGREES
    builder.grip_backhand = True  # Include the actual head in both arm clearance gates.
    builder._reset_pose(); builder._apply_pose(ready)
    # Fixed anatomical palm frame: the rigid shaft rotates inside this
    # cylindrical grasp; turning its hook must never twist either wrist.
    contact_bar = Quaternion((.11549465, .94964445, -.29125312, -.00283206)).normalized()
    contact = align_bat(builder,body_pose(builder,ready,40.), frame=(Vector((.15, -.36, 1.15)), contact_bar),
        right_pole=(-.8, -.2, -.8), left_pole=(-.9, .3, .2),
        support_offset=BAT_GRIP)
    loaded_bar = Quaternion((.12851477, .16612934, -.89957434, -.38295022)).normalized()
    side_bar = (loaded_bar @ Vector((0.,1.,0.))).rotation_difference(Vector((-.95,-.10,.28)).normalized()) @ loaded_bar
    side = align_bat(builder,body_pose(builder,ready,30.), frame=(Vector((-.05,-.35,1.30)),side_bar),
        right_pole=(-.8,-.2,-.8),left_pole=(-.9,.3,.2),support_offset=BAT_GRIP)
    origin, side_bar = builder.weapon_frame()
    loaded_bar = (side_bar @ Vector((0.,1.,0.))).rotation_difference(Vector((-.2,.65,.73)).normalized()) @ side_bar
    loaded = align_bat(builder,ready, frame=(Vector((-.25,-.20,1.30)),loaded_bar),
        right_pole=(-.8,-.2,-.8),left_pole=(-.9,.3,.2),support_offset=BAT_GRIP)
    origin, loaded_bar = builder.weapon_frame()
    shaft=loaded_bar @ Vector((0.,1.,0.));hook=loaded_bar @ Vector((0.,0.,1.))
    forward=Vector((0.,-1.,0.));wanted=(forward-shaft*forward.dot(shaft)).normalized()
    HELD_SHAFT_ROLL_DEGREES=-math.degrees(math.atan2(shaft.dot(hook.cross(wanted)),hook.dot(wanted)))
    rear_loaded=body_pose(builder,loaded,-20.)
    builder._reset_pose();builder._apply_pose(rear_loaded)
    _,rear_bar=builder.weapon_frame()
    gathered=align_bat(builder,body_pose(builder,loaded,15.),
        frame=(Vector((-.05,-.33,1.30)),loaded_bar),right_pole=(-.8,-.2,-.8))
    # A deliberate load behind the shoulder, one fast diagonal strike,
    # then its visible overrun. These are poses of ONE action, not variants.
    stops = [(0., loaded), (.30, rear_loaded), (.44, gathered), (.54, side), (.63, contact),
             (.73, body_pose(builder, contact, 18.)), (.80, contact),
             (1.10, body_pose(builder, loaded, -20.)), (1.28, loaded)]
    result = []
    for seconds, pose in stops:
        builder._reset_pose(); builder._apply_pose(pose)
        bones = builder.result.rig.pose.bones
        origin, bar = builder.weapon_frame()
        result.append(dict(seconds=seconds, pose=pose, origin=origin.copy(),
            bar=bar.copy(),
            hand=bones["hand.R"].matrix.to_quaternion().copy(),
            poles={side:(bones["forearm."+side].head-bones["upper_arm."+side].head).normalized()
                   for side in ("R", "L")}))
    return result


def metrics(builder, poses, fps, label):
    maximum_wrist = maximum_deviation = maximum_flexion = 0.
    left_wrist = left_deviation = left_flexion = support_error = support_angle = 0.
    gap = floor = native = 10.
    tips = []
    previous = None
    maximum_speed = 0.
    backward = 0.
    for seconds, pose in poses:
        builder._reset_pose()
        builder._apply_pose(pose)
        rig = builder.result.rig
        upper, lower, hand = (rig.pose.bones[n+".R"] for n in ("upper_arm", "forearm", "hand"))
        direction = (hand.head-lower.head).normalized()
        fingers = (hand.tail-hand.head).normalized()
        delta = hand.matrix.to_3x3() @ hand.bone.matrix_local.to_3x3().inverted()
        across = (delta @ builder.hand_frame("R")[1]).normalized()
        palm = (delta @ builder.hand_frame("R")[2]).normalized()
        deviation = abs(math.degrees(math.asin(max(-1., min(1., direction.dot(across))))))
        flexion = abs(math.degrees(math.atan2(direction.dot(palm), direction.dot(fingers))))
        wrist = math.degrees(direction.angle(fingers))
        clearance = builder.weapon_clearance()
        right_gap = builder.right_arm_native_gap(upper.head, lower.head, hand.head)
        if deviation > 25.01 or flexion > 55.01 or wrist > 65.01 or \
            clearance["body_gap_m"] < .004 or clearance["floor_gap_m"] < .025 or right_gap < -.004:
            raise ValueError(f"{label}@{seconds:.5f}: wrist={wrist:.3f}/{deviation:.3f}/{flexion:.3f}, body={clearance}, native={right_gap:.6f}")
        builder.weapon_anatomy(f"{label}@{seconds:.5f}", 65.)
        ll, lh = (rig.pose.bones[n+".L"] for n in ("forearm", "hand"))
        ld = (lh.head-ll.head).normalized(); lf = (lh.tail-lh.head).normalized()
        ldelta = lh.matrix @ lh.bone.matrix_local.inverted()
        la = (ldelta.to_3x3() @ builder.hand_frame("L")[1]).normalized()
        lp = (ldelta.to_3x3() @ builder.hand_frame("L")[2]).normalized()
        left_wrist = max(left_wrist, math.degrees(ld.angle(lf)))
        left_deviation = max(left_deviation, abs(math.degrees(math.asin(max(-1., min(1., ld.dot(la)))))))
        left_flexion = max(left_flexion, abs(math.degrees(math.atan2(ld.dot(lp), ld.dot(lf)))))
        origin, rotation = builder.weapon_frame(); shaft = rotation @ Vector((0., 1., 0.))
        support_error = max(support_error, ((ldelta @ builder.hand_frame("L")[0])-(origin+shaft*BAT_GRIP)).length)
        support_angle = max(support_angle, math.degrees(la.angle(shaft)))
        tip = origin + rotation @ Vector((0., .570, .145))
        if .30 <= seconds <= .38: backward = max(backward, tip.y)
        if previous is not None:
            speed=max((lower.head-previous[0]).length * fps, (ll.head-previous[1]).length * fps)
            if speed > maximum_speed:
                maximum_speed = speed
                if speed > 6.01: print(f"ELBOW CONTINUITY {seconds:.4f} {speed:.4f} R={tuple(lower.head)} L={tuple(ll.head)}",flush=True)
        previous = (lower.head.copy(), ll.head.copy())
        maximum_wrist = max(maximum_wrist, wrist)
        maximum_deviation = max(maximum_deviation, deviation)
        maximum_flexion = max(maximum_flexion, flexion)
        gap = min(gap, clearance["body_gap_m"])
        floor = min(floor, clearance["floor_gap_m"])
        native = min(native, right_gap)
        if .45 <= seconds <= .73: tips.append(tip)
    travel = max(((tip-tips[0]).length for tip in tips), default=0.)
    if label == "CombatAttack" and (travel < .80 or backward < .10):
        raise ValueError(f"Two-hand strike lost its broad shoulder load: travel={travel}, backward={backward}")
    reach = max((-tip.y for tip in tips), default=0.)
    if label == "CombatAttack" and reach < .80:
        raise ValueError(f"Simple strike cannot reach beyond automatic shove: {reach}")
    if maximum_speed > 6.01:
        raise ValueError(f"Simple shoulder arc exceeds elbow speed: {maximum_speed}")
    if support_error > .012 or support_angle > 12.01:
        raise ValueError(f"Shared bat contact exceeded its existing dock: {support_error:.6f}m/{support_angle:.4f}deg")
    return dict(maximum_right_wrist_degrees=maximum_wrist, maximum_right_deviation_degrees=maximum_deviation,
        maximum_right_flexion_degrees=maximum_flexion, minimum_body_clearance_m=gap,
        minimum_floor_clearance_m=floor, minimum_native_right_arm_gap_m=native,
        maximum_right_elbow_speed_m_s=maximum_speed, active_tip_travel_m=travel,
        maximum_forward_reach_m=reach, maximum_left_wrist_degrees=left_wrist,
        maximum_left_deviation_degrees=left_deviation, maximum_left_flexion_degrees=left_flexion,
        maximum_support_error_m=support_error, maximum_support_angle_degrees=support_angle,
        minimum_windup_tip_backward_m=backward)


def build(output, source_output):
    output.mkdir(parents=True, exist_ok=True)
    source_output.mkdir(parents=True, exist_ok=True)
    payload = json.loads((m.OUT / "CombatTest3D.json").read_text(encoding="utf8"))
    m.common.ANIMATION_FPS = m.FPS
    # Opponents are unarmed zombies. Their retained fall/rise bank is not a
    # second crowbar authoring target.
    for profile, bank in ((True, "CombatActions"),):
        bpy.ops.wm.open_mainfile(filepath=str(m.SOURCE / (bank+".blend")))
        source = r._builder(m, bpy.data.objects["RIG_Player"], profile)
        data = payload["actions"] if profile else payload["actions"]["npc"]
        for clip in m.bank_clip_specs(data):
            action = bpy.data.actions[clip["name"]]
            source.result.actions[clip["name"]] = m.common.ActionRecord(action=action, category=action.get("bp_category", "combat"),
                duration_seconds=clip["duration_seconds"], loop=clip["loop"], source_frame_count=round(clip["duration_seconds"]*m.FPS), source_fps=m.FPS)
        preserved = tuple(name for name in source.result.actions if name not in NAMES)
        signature = m.action_curve_signature(source, preserved)
        source.suspend_mesh_deformation()
        ready_matrices = r._sample(source.result.rig, source.result.actions["CombatReady"].action, 0.)
        source.result.rig.animation_data.action = None
        builder = r._canonical(m, profile)
        ready = r._apply_matrices(builder, ready_matrices)
        stops = author_stops(builder, ready)
        raw_weapon_frame = builder.weapon_frame
        builder.hand_weapon_frame = raw_weapon_frame
        def held_weapon_frame():
            origin, rotation = raw_weapon_frame()
            # The Unity/Blender basis exchange is a reflection, so local
            # shaft-roll angles reverse sign (the held mount remains fixed).
            return origin, rotation @ Quaternion((0., 1., 0.), math.radians(-HELD_SHAFT_ROLL_DEGREES))
        builder.weapon_frame = held_weapon_frame
        print(f"BAT FIXED MOUNT ROLL {HELD_SHAFT_ROLL_DEGREES:.9f}", flush=True)
        poses = combat_bat_continuity.build_poses(builder, stops, BAT_GRIP)
        authored = metrics(builder, poses, 100., "CombatAttack")
        builder._reset_pose(); builder._apply_pose(poses[0][1])
        ready_height = builder.weapon_frame()[0].z / builder.scale
        def sampled_pose(seconds):
            position = max(0., min(128., seconds*100.)); index = min(127, int(position))
            return builder.linear_key_pose(poses[index][1], poses[index+1][1], position-index)
        tracks = {}
        for name in NAMES:
            duration = 4. if name == "CombatReady" else 1. if name == "CombatCharge" else .48 if name == "CombatRecoil" else 1.28
            matrices = []
            for frame in range(round(duration*m.FPS)+1):
                seconds = frame/m.FPS
                time = 0. if name == "CombatReady" else .18*seconds if name == "CombatCharge" else .80 + seconds if name == "CombatRecoil" else seconds
                pose = sampled_pose(time)
                builder._reset_pose(); builder._apply_pose(pose)
                matrices.append({bone.name: bone.matrix.copy() for bone in builder.result.rig.pose.bones})
            tracks[name] = (duration, matrices)
        bpy.data.objects.remove(builder.result.rig, do_unlink=True)
        bpy.data.objects.remove(builder.result.root, do_unlink=True)
        for name, (duration, matrices) in tracks.items():
            bpy.data.actions.remove(source.result.actions.pop(name).action)
            keys = [(frame/(len(matrices)-1), r._apply_matrices(source, value)) for frame, value in enumerate(matrices)]
            source._create_action(name, "combat", duration, name == "CombatReady", len(matrices)-1, m.FPS, keys)
        if signature != m.action_curve_signature(source, preserved):
            raise ValueError("Simple strike changed Ready or unrelated actions")
        data["animation_signature"] = m.action_curve_signature(source, tuple(name for name, _, _ in m.SHARED_CLIPS))
        data["base_action_signature"] = m.action_curve_signature(source, ("CombatReady", "CombatAttack", "CombatBlock", "CombatHit"))
        source.restore_mesh_deformation()
        source.result.rig.animation_data.action = source.result.actions["CombatReady"].action
        bpy.context.scene.frame_set(0)
        m.common.export_animation_fbx(output / (bank+".fbx"), source.result)
        m.common.save_blend(source_output / (bank+".blend"))
        imported, actions = m.import_bank(output / (bank+".fbx"))
        audit = r._canonical(m, profile)
        audit_raw_weapon_frame = audit.weapon_frame
        def audited_held_frame():
            origin, rotation = audit_raw_weapon_frame()
            return origin, rotation @ Quaternion((0., 1., 0.), math.radians(-HELD_SHAFT_ROLL_DEGREES))
        audit.weapon_frame = audited_held_frame
        certificates = {}
        for name, (duration, _) in tracks.items():
            if name == "CombatReady": continue
            if name in ("CombatReleaseLight", "CombatReleaseHeavy"):
                certificates[name] = dict(certificates["CombatAttack"])
                continue
            imported_poses = []
            for frame in range(round(duration*200)+1):
                matrices = r._sample(imported, actions[name], frame/200.)
                imported.animation_data.action = None
                imported_poses.append((frame/200., r._apply_matrices(audit, matrices)))
            certificates[name] = metrics(audit, imported_poses, 200., name)
        holding = data["holding"]
        holding.update(ready_grip_offset_m=BAT_GRIP, block_grip_offset_m=m.BLOCK_GRIP,
            minimum_ready_grip_height_m=ready_height, maximum_ready_grip_height_m=ready_height,
            maximum_authored_left_contact_error_m=authored["maximum_support_error_m"],
            maximum_left_axis_error=1.-math.cos(math.radians(max(value["maximum_support_angle_degrees"] for value in certificates.values()))),
            maximum_supported_left_axis_degrees=12.,
            runtime_contract="independent left palm on the cylinder dock; contact-axis correction <=12 degrees; opposed palms apply only to guard")
        holding["breathing_grip_travel_m"]["CombatReady"] = 0.
        holding["opposed_palm_scope"] = "guard"
        holding["block_hook_clearance_m"] = m.CROWBAR_PATH[1][1]-m.BLOCK_GRIP-holding["left_hand_shaft_extent_m"]
        data["simple_strike"] = dict(source_sha256=SOURCE_HASH, unchanged_action_signature=signature,
            fbx_sha256=hashlib.sha256((output/(bank+".fbx")).read_bytes()).hexdigest(),
            two_handed=True, support_grip_metres=BAT_GRIP, held_shaft_roll_degrees=HELD_SHAFT_ROLL_DEGREES,
            blended_ready_endpoints=True,
            authored_stops=[stop["seconds"] for stop in stops], clips=certificates)
        data.pop("forehand_anatomy", None)
        print("TWO HAND STRIKE EXPORTED PROOF " + bank + " " + json.dumps(data["simple_strike"]), flush=True)
    (output / "CombatTest3D.json").write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--source-dir", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    build(args.output_dir.resolve(), args.source_dir.resolve())
