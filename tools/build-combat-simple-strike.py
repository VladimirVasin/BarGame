"""A retained-bank, single shoulder arc. No elbow search or supporting-hand solver."""
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
NAMES = ("CombatAttack", "CombatReleaseLight", "CombatReleaseHeavy", "CombatCharge", "CombatRecoil")
SOURCE_HASH = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()


def smooth(value):
    t = max(0., min(1., value))
    return t * t * (3. - 2. * t)


def pose_at(builder, ready, rest, seconds, windup, contact):
    if seconds <= .45:
        rotation = Quaternion((1., 0., 0., 0.)).slerp(windup, smooth(seconds / .45))
    elif seconds <= .63:
        t = smooth((seconds - .45) / .18)
        rotation = windup.slerp(contact, t)
    else:
        rotation = contact.slerp(Quaternion((1., 0., 0., 0.)), smooth((seconds - .63) / .65))
    pose = dict(ready)
    free = min(smooth(seconds / .18), smooth((1.28-seconds) / .18))
    left = builder.linear_key_pose(ready, rest, free)
    for name in ("upper_arm.L", "forearm.L", "hand.L"):
        pose[name] = left[name]
    builder._reset_pose()
    builder._apply_pose(pose)
    upper = builder.result.rig.pose.bones["upper_arm.R"]
    position = upper.head.copy()
    upper.matrix = Matrix.Translation(position) @ (rotation @ upper.matrix.to_quaternion()).to_matrix().to_4x4()
    bpy.context.view_layer.update()
    # One hinge extension, shared by every power/side. The hand keeps its
    # original local wrist frame instead of solving a second grip or a dock.
    rig = builder.result.rig
    lower, hand = (rig.pose.bones[name+".R"] for name in ("forearm", "hand"))
    amount = .90 * min(smooth(seconds/.45), smooth((1.28-seconds)/.65))
    alignment = (hand.head-lower.head).normalized().rotation_difference((lower.head-upper.head).normalized())
    extension = Quaternion((1., 0., 0., 0.)).slerp(alignment, amount)
    lower.matrix = Matrix.Translation(lower.head.copy()) @ (extension @ lower.matrix.to_quaternion()).to_matrix().to_4x4()
    bpy.context.view_layer.update()
    return builder.snapshot_pose()


def metrics(builder, poses, fps, label):
    maximum_wrist = maximum_deviation = maximum_flexion = 0.
    gap = floor = native = 10.
    tips = []
    previous = None
    maximum_speed = 0.
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
        if previous is not None:
            maximum_speed = max(maximum_speed, (lower.head-previous).length * fps)
        previous = lower.head.copy()
        maximum_wrist = max(maximum_wrist, wrist)
        maximum_deviation = max(maximum_deviation, deviation)
        maximum_flexion = max(maximum_flexion, flexion)
        gap = min(gap, clearance["body_gap_m"])
        floor = min(floor, clearance["floor_gap_m"])
        native = min(native, right_gap)
        if .45 <= seconds <= .63:
            origin, rotation = builder.weapon_frame()
            tips.append(origin + rotation @ Vector((0., .595, .145)))
    travel = max(((tip-tips[0]).length for tip in tips), default=0.)
    if label == "CombatAttack" and travel < .12:
        raise ValueError("Simple strike has no physical active arc")
    reach = max((-tip.y for tip in tips), default=0.)
    if label == "CombatAttack" and reach < .80:
        raise ValueError(f"Simple strike cannot reach beyond automatic shove: {reach}")
    if maximum_speed > 6.01:
        raise ValueError(f"Simple shoulder arc exceeds elbow speed: {maximum_speed}")
    return dict(maximum_right_wrist_degrees=maximum_wrist, maximum_right_deviation_degrees=maximum_deviation,
        maximum_right_flexion_degrees=maximum_flexion, minimum_body_clearance_m=gap,
        minimum_floor_clearance_m=floor, minimum_native_right_arm_gap_m=native,
        maximum_right_elbow_speed_m_s=maximum_speed, active_tip_travel_m=travel,
        maximum_forward_reach_m=reach)


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
        rest_matrices = r._sample(source.result.rig, source.result.actions["CombatRest"].action, 0.)
        source.result.rig.animation_data.action = None
        builder = r._canonical(m, profile)
        ready = r._apply_matrices(builder, ready_matrices)
        rest = r._apply_matrices(builder, rest_matrices)
        chosen = None
        # A small fixed set of whole-arm arcs, in preference order. Every
        # candidate keeps the original elbow/wrist and exact Ready endpoints.
        identity = Quaternion((1., 0., 0., 0.))
        pose_at(builder, ready, rest, .63, identity, identity)
        origin, bar_rotation = builder.weapon_frame()
        shoulder = builder.result.rig.pose.bones["upper_arm.R"].head.copy()
        tip = origin + bar_rotation @ Vector((0., .595, .145))
        for outside in (.20, .35, .50, .65, .80):
            contact = (tip-shoulder).rotation_difference(Vector((-outside, -1., .05)).normalized())
            windup = Quaternion((0., 0., 1.), math.radians(-25.)) @ contact
            poses = [(frame/200., pose_at(builder, ready, rest, frame/200., windup, contact)) for frame in range(257)]
            try:
                proof = metrics(builder, poses, 200., "CombatAttack")
            except ValueError as error:
                print("SIMPLE ARC REFUSED " + str(error), flush=True)
                continue
            chosen = (windup, contact, proof)
            break
        if chosen is None:
            raise ValueError("No safe simple shoulder arc in the fixed candidate set")
        windup, contact, proof = chosen
        tracks = {}
        for name in NAMES:
            duration = 1. if name == "CombatCharge" else .48 if name == "CombatRecoil" else 1.28
            matrices = []
            for frame in range(round(duration*m.FPS)+1):
                seconds = frame/m.FPS
                time = .18*seconds if name == "CombatCharge" else .63 + .65*smooth(seconds/.48) if name == "CombatRecoil" else seconds
                pose_at(builder, ready, rest, time, windup, contact)
                matrices.append({bone.name: bone.matrix.copy() for bone in builder.result.rig.pose.bones})
            tracks[name] = (duration, matrices)
        bpy.data.objects.remove(builder.result.rig, do_unlink=True)
        bpy.data.objects.remove(builder.result.root, do_unlink=True)
        for name, (duration, matrices) in tracks.items():
            bpy.data.actions.remove(source.result.actions.pop(name).action)
            keys = [(frame/(len(matrices)-1), r._apply_matrices(source, value)) for frame, value in enumerate(matrices)]
            source._create_action(name, "combat", duration, False, len(matrices)-1, m.FPS, keys)
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
        certificates = {}
        for name, (duration, _) in tracks.items():
            imported_poses = []
            for frame in range(round(duration*200)+1):
                matrices = r._sample(imported, actions[name], frame/200.)
                imported.animation_data.action = None
                imported_poses.append((frame/200., r._apply_matrices(audit, matrices)))
            certificates[name] = metrics(audit, imported_poses, 200., name)
        data["simple_strike"] = dict(source_sha256=SOURCE_HASH, unchanged_action_signature=signature,
            fbx_sha256=hashlib.sha256((output/(bank+".fbx")).read_bytes()).hexdigest(),
            windup_shoulder_quaternion=list(windup), contact_shoulder_quaternion=list(contact), clips=certificates)
        data.pop("forehand_anatomy", None)
        print("SIMPLE STRIKE EXPORTED PROOF " + bank + " " + json.dumps(data["simple_strike"]), flush=True)
    (output / "CombatTest3D.json").write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--source-dir", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    build(args.output_dir.resolve(), args.source_dir.resolve())
