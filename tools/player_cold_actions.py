"""Hero V2's bone-only cold hold and shoulder rub, composed above locomotion.

The hands are solved against the opposite upper sleeve, not guessed Euler
angles. This module deliberately adds nothing to the shared pedestrian bank.
"""
from __future__ import annotations

import math

import bpy
from mathutils import Vector

HOLD_NAME = "ColdHold"
RUB_NAME = "ColdShoulderRub"
HOLD_SECONDS = 4.0
RUB_SECONDS = 2.5
SOURCE_FPS = 24
CONTACT_CLEARANCE_M = {"L": 0.089, "R": 0.102}
HOLD_CONTACT_M = {"L": 0.045, "R": 0.200}


def breath_amount(phase: float) -> float:
    """One inhale peaks at .30; the exhale occupies .35 through .75."""
    if phase <= 0.30:
        value = phase / 0.30
    elif phase <= 0.35:
        return 1.0
    elif phase < 0.75:
        value = 1.0 - (phase - 0.35) / 0.40
    else:
        return 0.0
    return value * value * (3.0 - 2.0 * value)


def contact_distance(side: str, phase: float, rubbing: bool) -> float:
    # Three short strokes, one hand following the other. The end envelope
    # is exactly zero so either endpoint is the held self-hug.
    if not rubbing:
        return HOLD_CONTACT_M[side]
    envelope = math.sin(math.pi * phase) ** 2
    offset = 0.0 if side == "L" else 0.65
    return HOLD_CONTACT_M[side] + 0.025 * envelope * (
        0.5 + 0.5 * math.sin(phase * math.pi * 6.0 + offset))


def sleeve_contact(rig, side: str, distance_m: float, scale: float):
    opposite = "R" if side == "L" else "L"
    upper = rig.pose.bones[f"upper_arm.{opposite}"]
    direction = (upper.tail - upper.head).normalized()
    chest = rig.pose.bones["chest"]
    rest = chest.bone.matrix_local.to_quaternion()
    chest_delta = chest.matrix.to_quaternion() @ rest.inverted()
    forward = chest_delta @ Vector((0.0, -1.0, 0.0))
    forward += chest_delta @ Vector((0.45 if side == "L" else -0.45, 0.0, 0.0))
    normal = (forward - direction * forward.dot(direction)).normalized()
    # The upper sleeve broadens below its narrow shoulder cap. Let the high
    # hand ride that widening surface during a stroke instead of cutting it.
    clearance = CONTACT_CLEARANCE_M[side]
    if side == "L":
        clearance += max(0.0, distance_m - HOLD_CONTACT_M[side]) * 0.25
    target = (upper.head + direction * distance_m * scale +
              normal * clearance * scale)
    return target, (-direction * 0.8 - normal * 0.6).normalized()


def cold_pose(builder, common, phase: float, rubbing: bool):
    rig = builder.result.rig
    P = common.BonePose
    breath = 0.0 if rubbing else breath_amount(phase)
    # A brief, small shoulder shiver after the out-breath, never a vibrating
    # whole body. Root, pelvis and every leg remain the ordinary neutral pose.
    shiver = (math.sin((phase - 0.78) / 0.20 * math.pi * 6.0) * 0.65
              if not rubbing and 0.78 < phase < 0.98 else 0.0)
    pose = builder.merge_pose(builder.relaxed_pose(), {
        "spine": P(rotation_degrees=(5.0 - breath * 0.35, 0.0, 0.5)),
        "chest": P(rotation_degrees=(7.0 - breath * 0.75, 0.0, -0.7)),
        "neck": P(rotation_degrees=(3.0, 0.0, 0.0)),
        "head": P(rotation_degrees=(2.0, 0.0, -0.5)),
        "clavicle.L": P(target_direction=(0.208, -0.020, 0.033 + breath * 0.003 + shiver * 0.002)),
        "clavicle.R": P(target_direction=(-0.208, -0.018, 0.036 + breath * 0.003 - shiver * 0.002)),
    })
    builder._reset_pose()
    builder._apply_pose(pose)
    # Both upper sleeves move as the arms close. Iterate their two-bone
    # solutions together; source bone lengths are preserved throughout.
    for _ in range(12):
        updates = {}
        for side, sign in (("L", 1.0), ("R", -1.0)):
            upper = rig.pose.bones[f"upper_arm.{side}"]
            forearm = rig.pose.bones[f"forearm.{side}"]
            hand = rig.pose.bones[f"hand.{side}"]
            contact, hand_direction = sleeve_contact(
                rig, side, contact_distance(side, phase, rubbing), builder.scale)
            wrist = contact - hand_direction * hand.length * 0.5
            shoulder = upper.head.copy()
            delta = wrist - shoulder
            distance = delta.length
            a, b = upper.length, forearm.length
            if not abs(a - b) + 0.001 < distance < a + b - 0.001:
                raise RuntimeError(f"Cold {side} sleeve contact is out of reach")
            axis = delta.normalized()
            # The left forearm crosses ABOVE the lower supporting right
            # forearm. Mirrored elbow poles put the sleeve volumes through
            # one another even while both palms meet their shoulder targets.
            pole = (Vector((0.45, -1.10, -0.10)) if side == "L"
                    else Vector((-0.45, -1.0, -1.0)))
            bend = (pole - axis * pole.dot(axis)).normalized()
            along = (a * a - b * b + distance * distance) / (2.0 * distance)
            elbow = shoulder + axis * along + bend * math.sqrt(max(0.0, a * a - along * along))
            updates.update({
                f"upper_arm.{side}": P(armature_direction=tuple(elbow - shoulder)),
                f"forearm.{side}": P(armature_direction=tuple(wrist - elbow)),
                f"hand.{side}": P(armature_direction=tuple(hand_direction)),
            })
        pose.update(updates)
        builder._reset_pose()
        builder._apply_pose(pose)
    return pose


def build_cold_actions(builder, common, generator_version: str) -> None:
    for name, duration, loop in ((HOLD_NAME, HOLD_SECONDS, True),
                                 (RUB_NAME, RUB_SECONDS, False)):
        # Dense keys keep both hands on the curved, moving sleeves during
        # rubbing. Both full-rig endpoints are deliberately the same hold.
        count = round(duration * SOURCE_FPS)
        keys = [(frame / count, cold_pose(builder, common, frame / count, not loop))
                for frame in range(count + 1)]
        builder._create_action(name, "cold", duration, loop, count, SOURCE_FPS,
                               keys, interpolation="BEZIER")
        action = builder.result.actions[name].action
        action["bp_generator_version"] = generator_version
        action["bp_event_count"] = 0
        action["bp_cold_contact"] = "opposite_upper_sleeve"


def validate_cold_actions(result, common, errors: list[str]) -> None:
    rig = result.rig
    animation = rig.animation_data_create()
    previous = animation.action
    previous_frame = bpy.context.scene.frame_current
    snapshots = []
    try:
        for name, seconds, looping in ((HOLD_NAME, HOLD_SECONDS, True),
                                       (RUB_NAME, RUB_SECONDS, False)):
            record = result.actions.get(name)
            if record is None:
                errors.append(f"Missing cold action {name}")
                continue
            if (record.category != "cold" or record.loop != looping or
                    abs(record.duration_seconds - seconds) > 1e-6 or
                    record.source_frame_count != round(seconds * SOURCE_FPS) or
                    record.source_fps != SOURCE_FPS):
                errors.append(f"{name} must retain its exact cold timing contract")
            animation.action = record.action
            end = round(record.action.frame_end)
            maximum_error = 0.0
            for half_frame in range(end * 2 + 1):
                frame = half_frame * 0.5
                bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.0)
                phase = frame / end
                for side in ("L", "R"):
                    hand = rig.pose.bones[f"hand.{side}"]
                    scale = rig.data.bones["root"].length / 0.18
                    expected, _ = sleeve_contact(
                        rig, side, contact_distance(side, phase, not looping), scale)
                    palm = (hand.head + hand.tail) * 0.5
                    maximum_error = max(maximum_error, (palm - expected).length)
                    upper = rig.pose.bones[f"upper_arm.{side}"]
                    lower = rig.pose.bones[f"forearm.{side}"]
                    bend = math.degrees((upper.tail - upper.head).angle(lower.tail - lower.head))
                    if bend > 130.0:
                        errors.append(f"{name} frame {frame}: {side} elbow folds past 130 degrees")
                        break
                for bone_name in ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"):
                    bone = rig.pose.bones[bone_name]
                    if (bone.location.length > 1e-6 or
                            bone.rotation_quaternion.angle > 1e-5 or
                            (bone.scale - Vector((1, 1, 1))).length > 1e-6):
                        errors.append(f"{name} must not animate lower-body bone {bone_name}")
                        break
            if maximum_error > 0.014:
                errors.append(f"{name} palm leaves its opposite sleeve by {maximum_error:.4f} m")
            for frame in (0, end):
                bpy.context.scene.frame_set(frame)
                snapshots.append({bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones})
        if snapshots:
            for pose in snapshots[1:]:
                if any(max(abs(pose[name][r][c] - matrix[r][c]) for r in range(4) for c in range(4)) > 1e-5
                       for name, matrix in snapshots[0].items()):
                    errors.append("Both cold actions must share the exact held self-hug endpoint")
                    break
    finally:
        animation.action = previous
        bpy.context.scene.frame_set(previous_frame)
        if previous is None:
            for bone in rig.pose.bones:
                bone.location = (0.0, 0.0, 0.0)
                bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
                bone.scale = (1.0, 1.0, 1.0)
            bpy.context.view_layer.update()
    if result.parts:
        import player_cold_clearance
        report = player_cold_clearance.measure(
            rig, {name: result.actions[name].action for name in (HOLD_NAME, RUB_NAME)})
        for name, measured in report["actions"].items():
            if measured["penetrating_samples"]:
                worst = measured["worst"]
                errors.append(f"{name} crosses {worst['left']} through {worst['right']} "
                              f"by {-worst['clearance_m']:.4f} m at frame {worst['frame']}")


if __name__ == "__main__":
    # A focused skeleton-only check: no model generation, texture writing,
    # export or Unity import. The production build calls the same validator.
    import importlib.util
    import argparse
    import hashlib
    import json
    import sys
    from pathlib import Path

    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root / "tools"))
    spec = importlib.util.spec_from_file_location(
        "hero_v2_cold_validation", root / "tools/build-player-3d-model-v2.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--stamp-manifest", type=Path)
    parser.add_argument("--source-blend", type=Path, default=module.DEFAULT_OUTPUT)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])

    def curve_signature(action):
        return [(curve.data_path, curve.array_index,
                 [(tuple(key.co), tuple(key.handle_left), tuple(key.handle_right),
                   key.interpolation) for key in curve.keyframe_points])
                for curve in sorted(module.common.iter_action_fcurves(action),
                                    key=lambda curve: (curve.data_path, curve.array_index))]

    saved_curves = None
    if args.stamp_manifest is not None:
        bpy.ops.wm.open_mainfile(filepath=str(args.source_blend.resolve()))
        saved_curves = {name: curve_signature(bpy.data.actions[name])
                        for name in (HOLD_NAME, RUB_NAME)}
    config = module.common.BuildConfig(
        output=root / "Captures/Tooling/cold-validation-unused.blend",
        preview=None, portrait=None, manifest=None, glb=None, fbx=None,
        animation_fbx=None, height=1.75, seed=17301, pose="apose")
    builder = module.HeroV2Builder(config, module.DEFAULT_FACE_ATLAS,
                                    module.DEFAULT_CLOTHING_ATLAS)
    builder.reset_scene()
    collections = builder.create_collections()
    builder.points = builder.create_pose_points()
    export_root = builder.create_root(collections["export"])
    bones = builder.create_bone_specs()
    rig = builder.create_armature(collections["rig"], export_root, bones)
    builder.bone_heads = {bone.name: bone.head for bone in bones}
    builder.bone_specs = {bone.name: bone for bone in bones}
    builder.result = module.common.BuildResult(
        root=export_root, rig=rig, collections=collections, materials={})
    build_cold_actions(builder, module.common, module.V2_GENERATOR_VERSION)
    failures = []
    validate_cold_actions(builder.result, module.common, failures)
    if failures:
        raise RuntimeError("Cold action validation failed:\n" + "\n".join(failures))
    if saved_curves is not None:
        for name in (HOLD_NAME, RUB_NAME):
            if saved_curves[name] != curve_signature(builder.result.actions[name].action):
                raise RuntimeError(f"{name} published source curves differ; regenerate the production bank before stamping")
        manifest = json.loads(args.stamp_manifest.read_text(encoding="utf-8"))
        if manifest.get("action_count") != 47 or {HOLD_NAME, RUB_NAME} - {
                action["name"] for action in manifest["actions"]}:
            raise RuntimeError("Cannot stamp a manifest without the complete cold action bank")
        manifest["cold_authoring_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        args.stamp_manifest.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print("Published source curves match exactly; refreshed cold authoring source stamp.")
    print("Cold actions validated: 4 s hold / 2.5 s rub; opposite-sleeve contacts, elbows, fixed lower body and shared endpoints.")
