#!/usr/bin/env python3
"""Bake the street stride and each default NPC's gait against their rigs and shoes."""

import argparse
import hashlib
import importlib.util
import json
from dataclasses import replace
from math import degrees, floor, sin, tau
from pathlib import Path
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

import bpy

import player_3d_model_common as common
import player_motion_authoring as motion


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/City/Residents"
WOMAN_WALK = "OrdinaryWomanWalk"
MODELS = (
    ("OrdinaryWorkerWalk", "ArtSource/VillageLife/StationWorker.blend", .065),
    (WOMAN_WALK, "ArtSource/City/Residents/OrdinaryWoman.blend", .090),
)
CYCLE_METRES = .82
FRAME_COUNT = 24
SIDES = (".L", ".R")


def read_authoring_base():
    spec = importlib.util.spec_from_file_location("npc_walk_base", Path(__file__).with_name("build-city-pedestrian-3d-model.py"))
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def shoe_soles(rig):
    into_rig = rig.matrix_world.inverted()
    soles = {side: {} for side in SIDES}
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH" or not obj.name.startswith("CLO_") or not obj.name.endswith(("Sole.L", "Sole.R")):
            continue
        side = obj.name[-2:]
        to_rig = into_rig @ obj.matrix_world
        ankle = rig.data.bones["foot" + side].head_local
        soles[side][obj.name[:-2]] = [to_rig @ vertex.co - ankle for vertex in obj.data.vertices]
    if not soles[".L"] or soles[".L"].keys() != soles[".R"].keys():
        raise RuntimeError("Default NPC footwear needs paired visible soles")
    return soles


def bake_walk(base, source, name, sleeve_space):
    bpy.ops.wm.open_mainfile(filepath=str(source))
    rig, = (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    rig.animation_data_create().action = None
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    scene = bpy.context.scene
    scene.render.fps = FRAME_COUNT
    scene.render.fps_base = 1.
    config = common.BuildConfig(source, None, None, None, None, None, None, 1.75, 0, "apose")
    builder = common.ProductionPlayerBuilderBase(config)
    builder.result = common.BuildResult(rig.parent, rig, {}, {})
    builder._reset_pose()
    shoes = shoe_soles(rig)
    sole_envelope = {side: [point for sole in shoes[side].values() for point in sole] for side in SIDES}
    arm_rest = {side: (rig.data.bones["upper_arm" + side].tail_local -
                       rig.data.bones["upper_arm" + side].head_local).normalized() for side in SIDES}
    # The two stance passes cover one cycle at the normal street pace.
    travel_scale = CYCLE_METRES / (2. * (motion.WALK_FOOT[4][0] - motion.WALK_FOOT[0][0]))
    profile = tuple((travel * travel_scale, lift * .72, roll) for travel, lift, roll in motion.WALK_FOOT)
    is_woman = name == WOMAN_WALK
    upper_arm_swing = .85 if is_woman else .90
    original_hip_lead = tuple(key.lead for key in base.CITIZEN_WALK_CYCLE)

    keys = []
    for frame in range(FRAME_COUNT + 1):
        phase = (frame % FRAME_COUNT) / FRAME_COUNT
        pose = motion._stride_pose({}, common.BonePose, phase, False, arm_rest)
        if is_woman:
            # Restore the lateral shift and roll from woman_walk_keys at
            # the support beats before fitting the planted shoes.
            transfer = sin(tau * phase)
            lead = motion._cycle(original_hip_lead, phase)
            pelvis = pose["pelvis"]
            chest = pose["chest"]
            pose["pelvis"] = replace(
                pelvis,
                rotation_degrees=(*pelvis.rotation_degrees[:2], -2.2 * lead + 1.6 * transfer),
                armature_location_m=(.012 * transfer, *pelvis.armature_location_m[1:]),
            )
            pose["chest"] = replace(
                chest, rotation_degrees=(*chest.rotation_degrees[:2], chest.rotation_degrees[2] - .65 * transfer)
            )
        for side, sign in zip(SIDES, (1, -1)):
            upper = "upper_arm" + side
            x, y, z = pose[upper].target_direction
            pose[upper] = replace(pose[upper], target_direction=(x + sign * sleeve_space, upper_arm_swing * y, z))
        motion._fit_boots(builder, common.BonePose, pose, phase, profile, sole_envelope, name)
        builder._reset_pose()
        builder._apply_pose(pose)
        keys.append((frame / FRAME_COUNT, {
            bone.name: base.BonePose(tuple(degrees(angle) for angle in bone.rotation_quaternion.to_euler("XYZ")),
                                     tuple(bone.location), tuple(bone.scale)) for bone in rig.pose.bones
        }))

    spec = base.ActionSpec(name, name.removesuffix("Walk"), 1., FRAME_COUNT,
                           "relaxed upright posture", "heel roll, alternating weight and arm swing")
    action = base.create_action(rig, spec, keys)
    for curve in common.iter_action_fcurves(action):
        curve.modifiers.new("CYCLES")
        curve.update()
    report = validate_walk(rig, action, shoes)
    rig.animation_data.action = None
    builder._reset_pose()
    report["source"] = source.relative_to(ROOT).as_posix()
    report["source_sha256"] = hashlib.sha256(source.read_bytes()).hexdigest()
    return builder.result, report


def validate_walk(rig, action, shoes):
    rig.animation_data.action = action
    scene = bpy.context.scene
    low = float("inf")
    high = float("-inf")
    flex = 0.
    for curve in common.iter_action_fcurves(action):
        start, end = curve.evaluate(0.), curve.evaluate(FRAME_COUNT)
        entry, exit = curve.evaluate(.01) - start, end - curve.evaluate(FRAME_COUNT - .01)
        if abs(start - end) > 1e-5 or abs(entry - exit) > .00003:
            raise RuntimeError(f"{action.name} has a discontinuous stride at {curve.data_path}")

    for tick in range(FRAME_COUNT * 4):
        frame = tick / 4
        scene.frame_set(floor(frame), subframe=frame % 1.)
        heights = {}
        for side, offset in zip(SIDES, (0., .5)):
            hip, knee, ankle = (rig.pose.bones[name + side] for name in ("thigh", "shin", "foot"))
            heights[side] = {
                name: min((ankle.head + ankle.matrix.to_quaternion() @ ankle.bone.matrix_local.to_quaternion().inverted() @ point).z
                          for point in sole) for name, sole in shoes[side].items()
            }
            if .18 <= (frame / FRAME_COUNT + offset) % 1. <= .32:
                flex = max(flex, degrees((knee.head - hip.head).angle(ankle.head - knee.head)))
        for shoe, left in heights[".L"].items():
            sole = min(left, heights[".R"][shoe])
            low, high = min(low, sole), max(high, sole)

    if low < -.006 or high > .012 or not 0. < flex <= 30.:
        raise RuntimeError(f"{action.name} lost support, sole {low:.4f}–{high:.4f} m, knee {flex:.1f} degrees")
    return {"minimum_sole_m": round(low, 5), "maximum_lower_sole_m": round(high, 5),
            "maximum_midstance_knee_flex_degrees": round(flex, 2)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=OUTPUT)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--clip", choices=tuple(name for name, _, _ in MODELS), help="Export only the selected clip.")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    base = read_authoring_base()
    reports = {}
    for name, path, sleeve_space in MODELS:
        result, reports[name] = bake_walk(base, ROOT / path, name, sleeve_space)
        destination = args.output_dir / (name + ".fbx")
        if args.validate_only:
            if not destination.is_file():
                raise RuntimeError(f"Missing {destination}")
        elif args.clip is None or args.clip == name:
            common.export_animation_fbx(destination, result)
    report = {"generator": Path(__file__).name, "cycle_seconds": 1., "cycle_metres": CYCLE_METRES,
              "root_motion": False, "clips": reports}
    manifest = args.output_dir / "DefaultNpcWalks.json"
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf-8")) != report:
            raise RuntimeError("Default NPC walking report differs from its source")
    else:
        manifest.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
