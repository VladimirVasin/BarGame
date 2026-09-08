"""Hero V2's bone-only cold hold, shoulder rub and brief shiver.

The hands are solved against the opposite upper sleeve, not guessed Euler
angles. This module deliberately adds nothing to the shared pedestrian bank.
"""
from __future__ import annotations

import math

import bpy
from mathutils import Vector

HOLD_NAME = "ColdHold"
RUB_NAME = "ColdShoulderRub"
SHIVER_NAME = "ColdShiver"
HOLD_SECONDS = 4.0
RUB_SECONDS = 2.5
SHIVER_SECONDS = 1.0
SOURCE_FPS = 24
COLD_TIMING = ((HOLD_NAME, HOLD_SECONDS, True), (RUB_NAME, RUB_SECONDS, False),
               (SHIVER_NAME, SHIVER_SECONDS, False))
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
    # Even the quiet loop keeps readjusting its grip. Different harmonics
    # give the hands separate small strokes throughout the breathing pause.
    if not rubbing:
        hand_phase = phase if side == "L" else phase + .025 * math.sin(math.tau * phase)
        drift = (.009 if side == "L" else .008) * (1 - math.cos(math.tau * hand_phase * 2))
        return HOLD_CONTACT_M[side] + drift
    # Three visible passes across the upper sleeve. Brief eased edges leave
    # most of this 2.5-second burst at working amplitude, not a tiny pulse.
    edge = min(1.0, phase / .12, (1.0 - phase) / .12)
    envelope = edge * edge * (3.0 - 2.0 * edge)
    offset = 0.0 if side == "L" else -.65
    stroke = .060 if side == "L" else -.060
    return HOLD_CONTACT_M[side] + stroke * envelope * (
        .5 - .5 * math.cos(phase * math.pi * 6.0 + offset))


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
    else:
        clearance += max(0.0, HOLD_CONTACT_M[side] - distance_m) * 0.14
    target = (upper.head + direction * distance_m * scale +
              normal * clearance * scale)
    return target, (-direction * 0.8 - normal * 0.6).normalized()


def cold_pose(builder, common, phase: float, rubbing: bool, shivering: bool = False):
    rig = builder.result.rig
    P = common.BonePose
    breath = 0.0 if rubbing or shivering else breath_amount(phase)
    # A brief, small shoulder shiver after the out-breath, never a vibrating
    # whole body. Root, pelvis and every leg remain the ordinary neutral pose.
    shiver = (math.sin((phase - 0.78) / 0.20 * math.pi * 6.0) * 0.65
              if not rubbing and not shivering and 0.78 < phase < 0.98 else 0.0)
    pose = builder.merge_pose(builder.relaxed_pose(), {
        "spine": P(rotation_degrees=(5.0 - breath * 0.35, 0.0, 0.5)),
        "chest": P(rotation_degrees=(7.0 - breath * 0.75, 0.0, -0.7)),
        "neck": P(rotation_degrees=(3.0, 0.0, 0.0)),
        "head": P(rotation_degrees=(2.0, 0.0, -0.5)),
        "clavicle.L": P(target_direction=(0.208, -0.020, 0.033 + breath * 0.003 + shiver * 0.002)),
        "clavicle.R": P(target_direction=(-0.208, -0.018, 0.036 + breath * 0.003 - shiver * 0.002)),
    })
    if shivering:
        # A short muscular chill, strongest in its middle. Both shoulders
        # contract together, with a small unequal response, while the neck
        # tucks into the raised collar. No root/pelvis/leg motion is added.
        burst = math.sin(math.pi * phase) ** 2
        contraction = (.5 - .5 * math.cos(math.tau * 5.5 * phase)) * burst
        pose.update({
            "spine": P(rotation_degrees=(5.0 + contraction * .55, 0.0, .5)),
            "chest": P(rotation_degrees=(7.0 + contraction * 1.35, 0.0, -.7)),
            "neck": P(rotation_degrees=(3.0 + contraction * 2.4, 0.0, 0.0)),
            "head": P(rotation_degrees=(2.0 + contraction * .8, 0.0, -.5)),
            "clavicle.L": P(target_direction=(.208 - contraction * .004, -.020,
                                                 .033 + contraction * .016)),
            "clavicle.R": P(target_direction=(-.208 + contraction * .004, -.018,
                                                 .036 + contraction * .014)),
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
                rig, side, HOLD_CONTACT_M[side] if shivering else
                contact_distance(side, phase, rubbing), builder.scale)
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


def build_cold_actions(builder, common, generator_version: str, names=None) -> None:
    for name, duration, loop in COLD_TIMING:
        if names is not None and name not in names:
            continue
        # Dense keys keep both hands on the curved, moving sleeves during
        # rubbing. Both full-rig endpoints are deliberately the same hold.
        count = round(duration * SOURCE_FPS)
        keys = [(frame / count, cold_pose(builder, common, frame / count,
                                         name == RUB_NAME, name == SHIVER_NAME))
                for frame in range(count + 1)]
        builder._create_action(name, "cold", duration, loop, count, SOURCE_FPS,
                               keys, interpolation="BEZIER")
        action = builder.result.actions[name].action
        action["bp_generator_version"] = generator_version
        action["bp_event_count"] = 0
        action["bp_cold_contact"] = "opposite_upper_sleeve"


def validate_cold_actions(result, common, errors: list[str], names=None) -> None:
    rig = result.rig
    animation = rig.animation_data_create()
    previous = animation.action
    previous_frame = bpy.context.scene.frame_current
    snapshots = []
    try:
        # A targeted new-action check still compares its endpoints with the
        # existing Hold, without rerunning its already accepted mesh sweep.
        if HOLD_NAME in result.actions:
            animation.action = result.actions[HOLD_NAME].action
            bpy.context.scene.frame_set(0)
            snapshots.append({bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones})
        for name, seconds, looping in COLD_TIMING:
            if names is not None and name not in names:
                continue
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
            contact_tracks = {side: [] for side in ("L", "R")}
            shoulder_track = []
            for half_frame in range(end * 2 + 1):
                frame = half_frame * 0.5
                bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.0)
                phase = frame / end
                for side in ("L", "R"):
                    hand = rig.pose.bones[f"hand.{side}"]
                    scale = rig.data.bones["root"].length / 0.18
                    expected, _ = sleeve_contact(
                        rig, side, HOLD_CONTACT_M[side] if name == SHIVER_NAME else
                        contact_distance(side, phase, name == RUB_NAME), scale)
                    palm = (hand.head + hand.tail) * 0.5
                    opposite = rig.pose.bones[f"upper_arm.{'R' if side == 'L' else 'L'}"]
                    along_sleeve = (palm - opposite.head).dot((opposite.tail - opposite.head).normalized()) / scale
                    contact_tracks[side].append(along_sleeve)
                    maximum_error = max(maximum_error, (palm - expected).length)
                    upper = rig.pose.bones[f"upper_arm.{side}"]
                    lower = rig.pose.bones[f"forearm.{side}"]
                    bend = math.degrees((upper.tail - upper.head).angle(lower.tail - lower.head))
                    if bend > 130.0:
                        errors.append(f"{name} frame {frame}: {side} elbow folds past 130 degrees")
                        break
                shoulder_track.append(rig.pose.bones["upper_arm.L"].head.z)
                for bone_name in ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"):
                    bone = rig.pose.bones[bone_name]
                    if (bone.location.length > 1e-6 or
                            bone.rotation_quaternion.angle > 1e-5 or
                            (bone.scale - Vector((1, 1, 1))).length > 1e-6):
                        errors.append(f"{name} must not animate lower-body bone {bone_name}")
                        break
            if maximum_error > 0.014:
                errors.append(f"{name} palm leaves its opposite sleeve by {maximum_error:.4f} m")
            for side, track in contact_tracks.items():
                span = max(track) - min(track)
                minimum = .012 if looping else .052 if name == RUB_NAME else 0.0
                if span < minimum:
                    errors.append(f"{name} {side} grip has only {span:.4f} m of visible sleeve travel")
                # A quiet hand may reverse briefly; it must not sit glued to
                # one patch of cloth across a half-second breathing pause.
                window = round(.5 * end / seconds * 2)
                if looping and any(max(track[i:i + window]) - min(track[i:i + window]) < .001
                                   for i in range(len(track) - window + 1)):
                    errors.append(f"{name} {side} grip freezes for half a second")
            if name == SHIVER_NAME:
                peaks = sum(shoulder_track[i] > shoulder_track[i - 1] and
                            shoulder_track[i] > shoulder_track[i + 1]
                            for i in range(1, len(shoulder_track) - 1))
                if max(shoulder_track) - min(shoulder_track) < .010 or not 5 <= peaks <= 6:
                    errors.append(f"{name} must show 5–6 shoulder contractions with at least 10 mm lift")
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
        checked = tuple(name for name, _, _ in COLD_TIMING if names is None or name in names)
        report = player_cold_clearance.measure(
            rig, {name: result.actions[name].action for name in checked}, names=checked)
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
    parser.add_argument("--refresh-actions", action="store_true",
                        help="Refresh selected cold actions in an existing production source")
    parser.add_argument("--refresh-action", action="append", choices=[row[0] for row in COLD_TIMING],
                        help="Limit refresh and mesh validation to this action; may be repeated")
    parser.add_argument("--source-manifest", type=Path, default=module.DEFAULT_MANIFEST)
    parser.add_argument("--stage-dir", type=Path,
                        default=root / "Captures/Tooling/cold-active-20260908")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    requested = tuple(args.refresh_action or (row[0] for row in COLD_TIMING))

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
                        for name, _, _ in COLD_TIMING}
    config = module.common.BuildConfig(
        output=root / "Captures/Tooling/cold-validation-unused.blend",
        preview=None, portrait=None, manifest=None, glb=None, fbx=None,
        animation_fbx=None, height=1.75, seed=17301, pose="apose")
    builder = module.HeroV2Builder(config, module.DEFAULT_FACE_ATLAS,
                                    module.DEFAULT_CLOTHING_ATLAS)
    if args.refresh_actions:
        # Read the existing hero rather than regenerate meshes, atlases or
        # unrelated actions. A new action extends the bank without rebuilding
        # or retiming any already accepted animation.
        manifest = json.loads(args.source_manifest.read_text(encoding="utf-8"))
        bpy.ops.wm.open_mainfile(filepath=str(args.source_blend.resolve()))
        rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
        rig.animation_data_create().action = None
        records = {row["name"]: module.common.ActionRecord(
            bpy.data.actions[row["name"]], row["category"], row["duration_seconds"],
            row["loop"], row["source_frame_count"], row["source_fps"])
            for row in manifest["actions"]}
        parts = [module.common.PartRecord(bpy.data.objects[row["name"]],
                 row["role"], row["bone"], row["sprite_part"], row["side"])
                 for row in manifest["parts"]]
        builder.result = module.common.BuildResult(rig.parent, rig, {}, {}, parts=parts,
                                                   actions=records)
        builder._reset_pose()
        atlas_hashes = (manifest["face_atlas"]["sha256"],
                        manifest["texture_bindings"][0]["sha256"],
                        manifest["bare_skin_atlas"]["sha256"])
        signature = lambda: module.content_signature(config, builder.result, *atlas_hashes)
        if signature() != manifest["content_signature_sha256"]:
            raise RuntimeError("Existing hero source differs from its manifest; do not refresh a stale source")
        expected_names = set(records) | set(requested)
        others = {name: curve_signature(record.action) for name, record in records.items()
                  if name not in requested}
        for name in requested:
            records.pop(name, None)
        unchanged_content = signature()
        for name in requested:
            if name in bpy.data.actions:
                bpy.data.actions.remove(bpy.data.actions[name])
        build_cold_actions(builder, module.common, module.V2_GENERATOR_VERSION, requested)
        failures = []
        validate_cold_actions(builder.result, module.common, failures, requested)
        if failures:
            raise RuntimeError("Cold action validation failed:\n" + "\n".join(failures))
        first_curves = {name: curve_signature(records[name].action) for name in requested}
        # Re-author only the selected actions to prove exact curve determinism.
        rig.animation_data.action = None
        for name in requested:
            records.pop(name)
            bpy.data.actions.remove(bpy.data.actions[name])
        build_cold_actions(builder, module.common, module.V2_GENERATOR_VERSION, requested)
        if any(first_curves[name] != curve_signature(records[name].action) for name in first_curves):
            raise RuntimeError("Cold action curves are not deterministic")
        cold_records = {name: records.pop(name) for name in requested}
        if signature() != unchanged_content or any(
                curve_signature(records[name].action) != curves for name, curves in others.items()):
            raise RuntimeError("Cold refresh changed geometry, rig, weights or another action")
        records.update(cold_records)
        if set(records) != expected_names or set(bpy.data.actions.keys()) != expected_names:
            raise RuntimeError("Cold refresh changed the complete action set")
        rig.animation_data.action = None
        builder._reset_pose()
        manifest["content_signature_sha256"] = signature()
        manifest["cold_authoring_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        manifest["cold_motion"] = {"hold_minimum_sleeve_travel_m": .012,
            "rub_sleeve_stroke_m": .060, "rub_strokes": 3,
            "continuous_hold": True, "shared_endpoints": True,
            "shiver_seconds": SHIVER_SECONDS, "shiver_contractions": 5.5}
        rows = {row["name"]: row for row in manifest["actions"]}
        for name in requested:
            record = records[name]
            rows[name] = dict(rows.get(name, rows[RUB_NAME]), name=name, category="cold",
                duration_seconds=record.duration_seconds, loop=record.loop,
                source_frame_count=record.source_frame_count, source_fps=record.source_fps,
                frame_start=record.action.frame_start, frame_end=record.action.frame_end)
        manifest["actions"] = [rows[name] for name in sorted(rows)]
        manifest["action_count"] = len(records)
        manifest["torso_skin"]["action_count"] = len(records)
        builder.result.root["bp_content_signature_sha256"] = manifest["content_signature_sha256"]
        bpy.context.scene["bp_content_signature_sha256"] = manifest["content_signature_sha256"]
        stage = args.stage_dir.resolve()
        stage.mkdir(parents=True, exist_ok=True)
        module.common.export_animation_fbx(stage / module.DEFAULT_ANIMATION_FBX.name, builder.result)
        rig.animation_data.action = None
        builder._reset_pose()
        module.common.save_blend(stage / module.DEFAULT_OUTPUT.name)
        (stage / module.DEFAULT_MANIFEST.name).write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"Cold refresh validated: deterministic curves; original rig, meshes, skin weights and {len(others)} other actions unchanged.")
        print("Content signature:", manifest["content_signature_sha256"])
        raise SystemExit(0)
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
        for name, _, _ in COLD_TIMING:
            if saved_curves[name] != curve_signature(builder.result.actions[name].action):
                raise RuntimeError(f"{name} published source curves differ; regenerate the production bank before stamping")
        manifest = json.loads(args.stamp_manifest.read_text(encoding="utf-8"))
        if manifest.get("action_count") != 48 or {row[0] for row in COLD_TIMING} - {
                action["name"] for action in manifest["actions"]}:
            raise RuntimeError("Cannot stamp a manifest without the complete cold action bank")
        manifest["cold_authoring_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        args.stamp_manifest.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print("Published source curves match exactly; refreshed cold authoring source stamp.")
    print("Cold actions validated: 4 s hold / 2.5 s rub / 1 s shiver; sleeve contacts, elbows, fixed lower body and shared endpoints.")
