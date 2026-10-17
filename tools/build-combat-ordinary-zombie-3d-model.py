"""Deterministic ordinary zombie action bank on the undeformed NpcHumanV2 rig.

The ordinary worker's topology/rest skeleton remains untouched. This pack is
CombatTest-only and never enters DefaultNpcCatalog. Run with run-blender.py's
staged --output-dir/--source-dir mappings; failed generation publishes nothing.
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
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("ordinary_zombie_combat", ROOT / "tools/build-combat-test-3d-model.py")
combat = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = combat
spec.loader.exec_module(combat)
common = combat.common

FPS = 100
CLIPS = (("ZombieOrdinaryIdle", 4., True), ("ZombieOrdinaryWalk", 1.6, True),
         ("ZombieOrdinaryAttackRight", 1.77, False), ("ZombieOrdinaryAttackLeft", 1.77, False),
         ("ZombieOrdinaryHit", .45, False))
WALK_DISTANCE = 1.2
PREPARE, ACTIVE, RECOVER = .6, .22, .95
OUT = ROOT / "Assets/Resources/Combat"
SOURCE = ROOT / "ArtSource/CombatZombie"


def smooth(value):
    t = min(1., max(0., value))
    return t*t*t*(10.-15.*t+6.*t*t)


def surge(value):
    t = min(1., max(0., value))
    return t*t*(3.-2.*t)


def load_builder():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "ArtSource/VillageLife/StationWorker.blend"))
    rig = bpy.data.objects["RIG_Player"]
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20261013, "apose")
    builder = combat.CombatBuilder(config, combat.hero.DEFAULT_FACE_ATLAS, combat.hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    builder.points = builder.create_pose_points()
    rig.animation_data_create().action = None
    builder._reset_pose()
    builder.footwork_support_targets = {s: rig.data.bones["foot."+s].head_local.copy() for s in ("L", "R")}
    builder.footwork_support_rotations = {s: rig.data.bones["foot."+s].matrix_local.to_quaternion() for s in ("L", "R")}
    builder.hinge_references = common.calibrate_hinge_references(rig)
    builder.deformation = []
    # Only this actual production rest skeleton is needed for a bone-only
    # export. Removing scene copies avoids reevaluating the worker's modular
    # garments and authored joint modifiers on every dense IK sample.
    for obj in list(bpy.data.objects):
        if obj not in (rig, rig.parent): bpy.data.objects.remove(obj, do_unlink=True)
    bpy.context.view_layer.update()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    rig.parent.name = "ROOT_Player"
    bpy.context.scene.render.fps = FPS
    common.ANIMATION_FPS = FPS
    return builder


def rotation(bone, delta):
    bone.matrix = Matrix.Translation(bone.head.copy()) @ (delta @ bone.matrix.to_quaternion()).to_matrix().to_4x4()
    bpy.context.view_layer.update()


def solve_hand(builder, side, offset, reach=0.):
    rig = builder.result.rig
    upper, fore, hand = (rig.pose.bones[n+"."+side] for n in ("upper_arm", "forearm", "hand"))
    shoulder = upper.head.copy()
    wrist = shoulder+Vector(offset)
    a, b = upper.bone.length, fore.bone.length
    delta = wrist-shoulder
    distance = delta.length
    if not abs(a-b)+.006 < distance < a+b-.006:
        raise ValueError(f"Unreachable ordinary_zombie {side} wrist {distance:.5f}/{a+b:.5f}")
    axis = delta.normalized()
    # Elbows sag forward and down. An outward pole makes hands-on-hips
    # even with low wrist targets; it belongs to the retired fighter stance.
    pole = Vector((.08 if side == "L" else -.08, -1., -.35))
    bend = (pole-axis*pole.dot(axis)).normalized()
    along = (a*a-b*b+distance*distance)/(2.*distance)
    elbow = shoulder+axis*along+bend*math.sqrt(max(0., a*a-along*along))
    for bone, start, end in ((upper, shoulder, elbow), (fore, elbow, wrist)):
        rest = bone.bone
        q = (rest.tail_local-rest.head_local).normalized().rotation_difference((end-start).normalized()) @ rest.matrix_local.to_quaternion()
        bone.matrix = Matrix.Translation(start) @ q.to_matrix().to_4x4()
        bpy.context.view_layer.update()
    # The open fingers extend along the forearm, never curl around a weapon.
    fingers = Vector((0., -.65*reach, -1.+.85*reach)).normalized()
    rest = hand.bone
    q = (rest.tail_local-rest.head_local).normalized().rotation_difference(fingers) @ rest.matrix_local.to_quaternion()
    hand.matrix = Matrix.Translation(wrist) @ q.to_matrix().to_4x4()
    bpy.context.view_layer.update()


def foot_target(builder, side, seconds):
    t = seconds/1.6
    stance = t <= .5 if side == "L" else t >= .5
    swing = min(1., max(0., t*2.-(1. if side == "L" else 0.)))
    if stance:
        progress = .25-t if side == "L" else .75-t
        lift = 0.
    else:
        progress = smooth(swing)-t+(.25 if side == "L" else -.25)
        lift = math.sin(swing*math.pi)**2
    ankle = builder.support(side)
    ankle.y -= WALK_DISTANCE*progress
    # One foot drags a little; both still clear the ground on each swing.
    ankle.z += (.045 if side == "L" else .065)*lift
    return ankle, stance, lift


def attack_foot(builder, side, name, seconds):
    ankle = builder.support(side)
    if not PREPARE < seconds < PREPARE+ACTIVE:
        return ankle, True, 0.
    t = (seconds-PREPARE)/ACTIVE
    root = .7*surge(t)
    leading = side == ("L" if name == "ZombieOrdinaryAttackLeft" else "R")
    if leading:
        swing = min(1.,t/.5)
        travelled = .7*smooth(swing)
        stance = t >= .5
    else:
        swing = max(0.,(t-.5)/.5)
        travelled = .7*smooth(swing)
        stance = t <= .5
    ankle.y += root-travelled
    lift = math.sin(swing*math.pi)**2
    ankle.z += (.050 if leading else .045)*lift
    return ankle, stance, lift


def pose(builder, kind, seconds):
    rig = builder.result.rig
    rig.animation_data.action = None
    builder._reset_pose()
    wave = math.sin(seconds*math.tau/4.) if kind == "ZombieOrdinaryIdle" else 0.
    phase = seconds/1.6 if kind == "ZombieOrdinaryWalk" else 0.
    sway = math.sin(phase*math.tau) if kind == "ZombieOrdinaryWalk" else .4*wave
    prep = follow = hit = 0.
    if kind.startswith("ZombieOrdinaryAttack"):
        prep = smooth(seconds/PREPARE)*(1.-smooth((seconds-PREPARE)/ACTIVE))
        follow = smooth((seconds-PREPARE)/ACTIVE)*(1.-smooth((seconds-PREPARE-ACTIVE)/RECOVER))
    if kind == "ZombieOrdinaryHit":
        hit = smooth(seconds/.09)*(1.-smooth((seconds-.09)/.36))
    pelvis = rig.pose.bones["pelvis"]
    matrix = pelvis.matrix.copy()
    walking_drop = .090 if kind == "ZombieOrdinaryWalk" else 0.
    step_load = .100*math.sin(min(1.,max(0.,(seconds-PREPARE)/ACTIVE))*math.pi)**2 if kind.startswith("ZombieOrdinaryAttack") else 0.
    matrix.translation += Vector((.012*sway, -.025*follow+.025*hit, -.040-walking_drop-step_load-.015*prep-.055*follow))
    pelvis.matrix = matrix
    bpy.context.view_layer.update()
    rotation(rig.pose.bones["spine"], Quaternion(Vector((1., 0., 0.)), math.radians(24.+2.*wave+8.*follow-12.*hit)))
    rotation(rig.pose.bones["chest"], Quaternion(Vector((0., 1., 0.)), math.radians(5.+1.5*sway)))
    rotation(rig.pose.bones["chest"], Quaternion(Vector((1., 0., 0.)), math.radians(6.+4.*prep)))
    rotation(rig.pose.bones["head"], Quaternion(Vector((1., 0., 0.)), math.radians(14.-8.*prep+5.*follow)))
    rotation(rig.pose.bones["head"], Quaternion(Vector((0., 1., 0.)), math.radians(-5.+2.*wave)))
    for side in ("L", "R"):
        sign = 1. if side == "L" else -1.
        offset = Vector((sign*.026, -.14+sign*.030*sway, -.49+( .028 if side == "L" else 0.)))
        reach = 0.
        if kind.startswith("ZombieOrdinaryAttack"):
            attacking = side == ("L" if kind == "ZombieOrdinaryAttackLeft" else "R")
            if attacking:
                loaded = Vector((sign*.10, .08, -.22))
                contact = Vector((-sign*.13, -.48, -.10))
                offset = offset.lerp(loaded, prep).lerp(contact, follow)
                reach = follow
            else:
                offset += Vector((sign*.01, -.20*prep-.15*follow, .16*prep+.12*follow))
                reach = .4*(prep+follow)
        offset += Vector((0., .11*hit, .04*hit))
        solve_hand(builder, side, offset, reach)
    for side in ("L", "R"):
        ankle = builder.support(side)
        foot_rotation = builder.footwork_support_rotations[side]
        if kind == "ZombieOrdinaryWalk":
            ankle, _, lift = foot_target(builder, side, seconds)
            foot_rotation = Quaternion(Vector((1., 0., 0.)), math.radians(-8.*lift)) @ foot_rotation
        elif kind.startswith("ZombieOrdinaryAttack"):
            ankle, _, lift = attack_foot(builder, side, kind, seconds)
            foot_rotation = Quaternion(Vector((1., 0., 0.)), math.radians(-7.*lift)) @ foot_rotation
        builder.recovery_leg(side, ankle, (0., -1., 0.), foot_rotation)
    return builder.snapshot_pose()


def author(builder):
    for name, duration, loop in CLIPS:
        if name in builder.result.actions:
            bpy.data.actions.remove(builder.result.actions.pop(name).action)
        count = round(duration*FPS)
        keys = []
        for f in range(count+1):
            keys.append((f/count, pose(builder, name, duration*f/count)))
            if f and f % 100 == 0: print(f"{name} {f}/{count} source poses",flush=True)
        builder._create_action(name, "combat_ordinary_zombie", duration, loop, count, FPS, keys)
        print("Authored "+name, flush=True)


def measure(builder):
    rig = builder.result.rig
    records = []
    signature = hashlib.sha256()
    for name, duration, loop in CLIPS:
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['):
                raise ValueError("ZombieOrdinary bank contains object motion")
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
        rig.animation_data.action = action
        support_error = loop_error = 0.
        first = None
        minimum_leg_gap = 100.
        max_knee = max_wrist_reach = 0.
        maximum_scale_error = maximum_limb_translation = 0.
        minimum_supports = 2
        for f in range(round(duration*FPS)+1):
            second = f/FPS
            bpy.context.scene.frame_set(f)
            bpy.context.view_layer.update()
            for bone in rig.pose.bones:
                maximum_scale_error = max(maximum_scale_error, max(abs(value-1.) for value in bone.scale))
                if bone.name != "pelvis":
                    maximum_limb_translation = max(maximum_limb_translation, bone.location.length)
            if f == 0: first = {b.name: b.matrix_basis.copy() for b in rig.pose.bones}
            if loop and f == round(duration*FPS):
                loop_error = max(abs(b.matrix_basis[i][j]-first[b.name][i][j]) for b in rig.pose.bones for i in range(4) for j in range(4))
            supports = 0
            for side in ("L", "R"):
                thigh, shin, foot = (rig.pose.bones[n+"."+side] for n in ("thigh", "shin", "foot"))
                target, stance = (foot_target(builder, side, second)[:2] if name == "ZombieOrdinaryWalk" else attack_foot(builder,side,name,second)[:2] if name.startswith("ZombieOrdinaryAttack") else (builder.support(side),True))
                if stance:
                    support_error = max(support_error, (foot.head-target).length)
                    supports += 1
                max_knee = max(max_knee, math.degrees((shin.head-thigh.head).angle(foot.head-shin.head)))
                upper, hand = rig.pose.bones["upper_arm."+side], rig.pose.bones["hand."+side]
                max_wrist_reach = max(max_wrist_reach, (hand.head-upper.head).length)
            minimum_leg_gap = min(minimum_leg_gap, (rig.pose.bones["foot.L"].head-rig.pose.bones["foot.R"].head).length)
            minimum_supports = min(minimum_supports,supports)
        # Matrix decomposition at dense IK samples carries float32 round-off.
        if maximum_scale_error > .0001 or maximum_limb_translation > .0001:
            raise ValueError(f"Ordinary zombie must retain normal limb anatomy: {name}, {maximum_scale_error}, {maximum_limb_translation}")
        if support_error > .001 or loop_error > .00001 or max_knee > 115. or minimum_leg_gap < .15 or minimum_supports < 1:
            raise ValueError(f"ZombieOrdinary supports/anatomy failed {name}: {support_error}, {loop_error}, {max_knee}, {minimum_leg_gap}")
        records.append(dict(name=name, duration_seconds=duration, loop=loop, maximum_ground_support_error_m=support_error,
            maximum_loop_seam_error=loop_error, maximum_knee_flexion_degrees=max_knee,
            minimum_foot_separation_m=minimum_leg_gap, maximum_wrist_reach_m=max_wrist_reach,
            minimum_grounded_feet=minimum_supports, maximum_bone_scale_error=maximum_scale_error,
            maximum_limb_translation_m=maximum_limb_translation))
    return dict(generator="tools/build-combat-ordinary-zombie-3d-model.py", version="1.0.0", test_only=True,
        rig="NpcHumanV2", topology="retained_ordinary_worker", root_motion=False, animation_events=0,
        source_fps=FPS, walk_cycle_distance_m=WALK_DISTANCE, walk_duration_seconds=1.6, prepare_seconds=PREPARE,
        active_seconds=ACTIVE, recovery_seconds=RECOVER, signature=signature.hexdigest(), clips=records)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, default=OUT)
    parser.add_argument("--source-dir", type=Path, default=SOURCE)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    builder = load_builder()
    author(builder)
    payload = measure(builder)
    author(builder)
    if payload != measure(builder):
        raise ValueError("Ordinary zombie action generation is nondeterministic")
    manifest = args.output_dir/"ZombieOrdinary3D.json"
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf8")) != json.loads(json.dumps(payload)):
            raise ValueError("Published ordinary zombie manifest differs from deterministic source")
    else:
        args.output_dir.mkdir(parents=True, exist_ok=True)
        rig=builder.result.rig
        rig.animation_data.action=None
        builder._reset_pose()
        common.export_animation_fbx(args.output_dir/"ZombieOrdinaryActions.fbx", builder.result)
        for modifier, visible in builder.deformation:
            modifier.show_viewport=visible
        common.save_blend(args.source_dir/"ZombieOrdinaryActions.blend")
        manifest.write_text(json.dumps(payload, indent=2)+"\n", encoding="utf8")
    print("COMBAT ORDINARY ZOMBIE ART CONTRACT OK "+payload["signature"], flush=True)


if __name__ == "__main__":
    main()
