"""Ground-supported Hero V2 snow locomotion; never changes the NPC bank.

The production builder calls build_snow_actions. This module's focused CLI
refreshes only these two actions in the verified production source, retaining
the model, skin weights and all other animation curves byte-for-byte in data.
"""
from __future__ import annotations

import math
from pathlib import Path

import bpy
from mathutils import Vector

NAMES = ("SnowWalk", "SnowWalkBackward")
SOURCE_FPS = 24
FRAME_COUNT = 48
DURATION_SECONDS = FRAME_COUNT / SOURCE_FPS
CYCLE_DISTANCE_M = 1.10
STANCE_END = .60
SNOW_DEPTH_M = .45


def smooth(value):
    value = max(0., min(1., value))
    return value * value * (3. - 2. * value)


def foot_path(phase, backward=False):
    """Ankle travel in source -Y forward space, with a real extraction arc.

    Contact is phase zero, followed by 60% planted support. The foot is first
    extracted mostly upward, carried across the snow, then placed down. Reverse
    travel has its own target path; knees always bend anatomically forward.
    """
    phase %= 1.
    if phase <= STANCE_END:
        forward = -.33 + CYCLE_DISTANCE_M * phase
        lift = 0.
    else:
        landmarks = ((.60, .33, 0.), (.72, .23, .47),
                     (.83, -.17, .49), (.91, -.32, .39), (1., -.33, 0.))
        for (a, ay, az), (b, by, bz) in zip(landmarks, landmarks[1:]):
            if phase <= b:
                t = smooth((phase - a) / (b - a))
                forward, lift = ay + (by - ay) * t, az + (bz - az) * t
                break
    return (-forward if backward else forward), lift


def snow_pose(builder, common, phase, backward=False):
    P = common.BonePose
    rig = builder.result.rig
    # The body settles at each contact, then travels over its support. This
    # small rise belongs to the weight transfer, never a two-foot flight.
    sway = math.sin(math.tau * phase)
    pelvis_z = -.120 + .090 * math.sin(math.tau * phase) ** 2
    pose = builder.merge_pose(builder.relaxed_pose(), {
        "pelvis": P(armature_location_m=(.033 * sway, 0., pelvis_z),
                    rotation_degrees=(0., 1.3 * sway, 0.)),
        "spine": P(rotation_degrees=(5.5 if not backward else 3., 0., -1.1 * sway)),
        "chest": P(rotation_degrees=(3.0, -2.0 * sway, .8 * sway)),
        "neck": P(rotation_degrees=(-3.5, .5 * sway, 0.)),
        "head": P(rotation_degrees=(-2.5, .5 * sway, 0.)),
        "upper_arm.L": P(target_direction=(.080, .035 + .065 * sway, -.29)),
        "upper_arm.R": P(target_direction=(-.080, .035 - .065 * sway, -.29)),
        "forearm.L": P(rotation_degrees=(-24., 4., -4.)),
        "forearm.R": P(rotation_degrees=(-24., -4., 4.)),
    })
    builder._reset_pose()
    builder._apply_pose(pose)
    for side, offset, sign in (("L", 0., 1.), ("R", .5, -1.)):
        forward, lift = foot_path(phase + offset, backward)
        thigh = rig.pose.bones[f"thigh.{side}"]
        shin = rig.pose.bones[f"shin.{side}"]
        foot = rig.pose.bones[f"foot.{side}"]
        hip = thigh.head.copy()
        rest_ankle = foot.bone.head_local
        ankle = Vector((rest_ankle.x + sign * .013 * builder.scale,
                        rest_ankle.y + forward * builder.scale,
                        rest_ankle.z + lift * builder.scale))
        delta = ankle - hip
        length = delta.length
        a, b = thigh.length, shin.length
        if length >= a + b - .0001:
            raise RuntimeError(f"{NAMES[int(backward)]} {phase:.4f} {side} ankle is out of reach: {length:.4f}/{a+b:.4f}")
        axis = delta.normalized()
        pole = Vector((sign * .10, -1., 0.))
        bend = (pole - axis * pole.dot(axis)).normalized()
        along = (a * a - b * b + length * length) / (2. * length)
        knee = hip + axis * along + bend * math.sqrt(max(0., a * a - along * along))
        pose.update({
            f"thigh.{side}": P(armature_direction=tuple(knee - hip)),
            f"shin.{side}": P(armature_direction=tuple(ankle - knee)),
            # The boot remains level, with the same bind sole, while a flexed
            # knee extracts it. No straight-leg marching or toe through snow.
            f"foot.{side}": P(armature_direction=tuple(foot.bone.tail_local - rest_ankle)),
        })
    return pose


def build_snow_actions(builder, common, generator_version):
    modifiers = [(modifier, modifier.show_viewport)
                 for part in builder.result.parts for modifier in part.obj.modifiers
                 if modifier.type == "ARMATURE" and modifier.object == builder.result.rig]
    try:
        for modifier, _ in modifiers:
            modifier.show_viewport = False
        for name in NAMES:
            builder.result.rig.animation_data_create().action = None
            keys = [(frame / FRAME_COUNT, snow_pose(builder, common,
                     frame / FRAME_COUNT, name == NAMES[1])) for frame in range(FRAME_COUNT + 1)]
            builder._create_action(name, "locomotion", DURATION_SECONDS, True,
                                   FRAME_COUNT, SOURCE_FPS, keys, interpolation="BEZIER")
            action = builder.result.actions[name].action
            action["bp_generator_version"] = generator_version
            action["bp_event_count"] = 0
            action["bp_torso_skin"] = "pelvis_spine_chest_v1"
            action["bp_gait_style"] = "deep_snow_supported"
    finally:
        for modifier, visible in modifiers:
            modifier.show_viewport = visible
        bpy.context.view_layer.update()


def validate_snow_actions(result, common, errors):
    rig = result.rig
    animation = rig.animation_data_create()
    previous, previous_frame = animation.action, bpy.context.scene.frame_current
    metrics = {}
    try:
        for name in NAMES:
            record = result.actions.get(name)
            if record is None:
                errors.append(f"Missing snow action {name}")
                continue
            if (record.category != "locomotion" or not record.loop or
                    record.source_frame_count != FRAME_COUNT or
                    abs(record.duration_seconds - DURATION_SECONDS) > 1e-6):
                errors.append(f"{name} snow timing/loop contract changed")
            animation.action = record.action
            minimum_support = 0.
            maximum_flex = 0.
            maximum_target_error = 0.
            max_lift = {side: 0. for side in ("L", "R")}
            endpoints = []
            for half_frame in range(FRAME_COUNT * 2 + 1):
                frame = half_frame * .5
                phase = frame / FRAME_COUNT
                bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
                lifts = []
                if half_frame in (0, FRAME_COUNT * 2):
                    endpoints.append({bone.name: bone.matrix.copy() for bone in rig.pose.bones})
                for side, offset in (("L", 0.), ("R", .5)):
                    thigh, shin, foot = [rig.pose.bones[f"{part}.{side}"] for part in ("thigh", "shin", "foot")]
                    lift = foot.head.z - foot.bone.head_local.z
                    lifts.append(lift)
                    max_lift[side] = max(max_lift[side], lift)
                    target_y, target_z = foot_path(phase + offset, name == NAMES[1])
                    maximum_target_error = max(maximum_target_error,
                        abs(foot.head.y - foot.bone.head_local.y - target_y), abs(lift - target_z))
                    maximum_flex = max(maximum_flex, math.degrees((thigh.tail - thigh.head).angle(shin.tail - shin.head)))
                minimum_support = max(minimum_support, min(lifts))
                if rig.pose.bones["root"].matrix_basis.translation.length > 1e-6:
                    errors.append(f"{name} moved the root")
                    break
            if any(abs(value) > 1e-4 for bone in endpoints[0]
                   for row in (endpoints[0][bone] - endpoints[1][bone]) for value in row):
                errors.append(f"{name} does not close its loop")
            if minimum_support > .003 or maximum_target_error > .012:
                errors.append(f"{name} lost ground support/foot targets: {minimum_support:.4f}/{maximum_target_error:.4f} m")
            if min(max_lift.values()) < SNOW_DEPTH_M + .02 or maximum_flex > 150.:
                errors.append(f"{name} needs clear boots and anatomical knee bend: {max_lift}/{maximum_flex:.2f}")
            for curve in common.iter_action_fcurves(record.action):
                if not curve.data_path.startswith('pose.bones['):
                    errors.append(f"{name} contains non-bone animation")
            metrics[name] = {"maximum_ankle_lift_m": round(min(max_lift.values()), 6),
                             "maximum_support_lift_m": round(minimum_support, 6),
                             "maximum_target_error_m": round(maximum_target_error, 6),
                             "maximum_knee_flex_degrees": round(maximum_flex, 4)}
    finally:
        animation.action = previous
        bpy.context.scene.frame_set(previous_frame)
    return metrics


def action_manifest(record):
    return dict(name=record.action.name, category="locomotion", duration_seconds=record.duration_seconds,
                loop=True, source_frame_count=FRAME_COUNT, source_fps=SOURCE_FPS,
                frame_start=0., frame_end=float(FRAME_COUNT), root_motion=False,
                event_count=0, bone_only=True, in_place=True, gait_style="deep_snow_supported",
                cycle_distance_m=CYCLE_DISTANCE_M, support_fraction=STANCE_END)


def attach_metadata(payload, result, measurements=None):
    import hashlib
    payload["snow_authoring_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    payload["snow_locomotion"] = {"cycle_distance_m": CYCLE_DISTANCE_M, "support_fraction": STANCE_END,
        "left_contact_phase": 0., "right_contact_phase": .5, "snow_depth_m": SNOW_DEPTH_M,
        "runtime_distance_driven": True}
    if measurements is not None:
        payload["snow_locomotion"]["validation"] = measurements
    for row in payload["actions"]:
        if row["name"] in NAMES:
            row.update(action_manifest(result.actions[row["name"]]))


def main():
    import argparse
    import importlib.util
    import json
    import sys
    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root / "tools"))
    spec = importlib.util.spec_from_file_location("hero_v2_snow_refresh", root / "tools/build-player-3d-model-v2.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--source-blend", type=Path, default=module.DEFAULT_OUTPUT)
    parser.add_argument("--source-manifest", type=Path, default=module.DEFAULT_MANIFEST)
    parser.add_argument("--source-dir", type=Path, default=module.DEFAULT_OUTPUT.parent)
    parser.add_argument("--model-dir", type=Path, default=module.DEFAULT_MANIFEST.parent)
    parser.add_argument("--animation-dir", type=Path, default=module.DEFAULT_ANIMATION_FBX.parent)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    manifest = json.loads(args.source_manifest.read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(args.source_blend.resolve()))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    rig.animation_data_create().action = None
    config = module.common.BuildConfig(output=args.source_dir / module.DEFAULT_OUTPUT.name,
        preview=None, portrait=None, manifest=args.model_dir / module.DEFAULT_MANIFEST.name,
        glb=None, fbx=None, animation_fbx=args.animation_dir / module.DEFAULT_ANIMATION_FBX.name,
        height=manifest["height_m"], seed=17301, pose="apose")
    builder = module.HeroV2Builder(config, module.DEFAULT_FACE_ATLAS, module.DEFAULT_CLOTHING_ATLAS)
    records = {row["name"]: module.common.ActionRecord(bpy.data.actions[row["name"]], row["category"],
        row["duration_seconds"], row["loop"], row["source_frame_count"], row["source_fps"]) for row in manifest["actions"]}
    parts = [module.common.PartRecord(bpy.data.objects[row["name"]], row["role"], row["bone"],
        row["sprite_part"], row["side"]) for row in manifest["parts"]]
    # Wardrobe/hair preserve the authoring order in the content signature,
    # whereas the manifest's flat part list is alphabetical.
    ordered = [name for item in manifest["wardrobe"]["items"] for name in item["renderers"]]
    ordered += [name for chain in manifest["hair"]["chains"] for name in chain["renderers"]]
    order = {name: index for index, name in enumerate(ordered)}
    parts.sort(key=lambda part: order.get(part.obj.name, len(order)))
    builder.result = module.common.BuildResult(rig.parent, rig, {}, {}, parts=parts, actions=records)
    builder.result.authored_anchors = [obj for obj in bpy.data.objects if obj.name.startswith("ANCHOR_HandGrip")]
    builder._reset_pose()
    atlas_hashes = (manifest["face_atlas"]["sha256"], manifest["texture_bindings"][0]["sha256"],
                    manifest["bare_skin_atlas"]["sha256"])
    signature = lambda: module.content_signature(config, builder.result, *atlas_hashes)
    if signature() != manifest["content_signature_sha256"]:
        raise RuntimeError("Existing hero source differs from manifest; refusing a stale refresh")
    def curves():
        return {name: [(curve.data_path, curve.array_index,
                        [(tuple(key.co), tuple(key.handle_left), tuple(key.handle_right), key.interpolation)
                         for key in curve.keyframe_points])
                       for curve in module.common.iter_action_fcurves(records[name].action)] for name in NAMES}
    def remove_snow():
        rig.animation_data.action = None
        for name in NAMES:
            records.pop(name, None)
            if name in bpy.data.actions:
                bpy.data.actions.remove(bpy.data.actions[name])
    remove_snow()
    unchanged = signature()
    print("Snow actions: authoring two supported loops on the preserved rig", flush=True)
    build_snow_actions(builder, module.common, module.V2_GENERATOR_VERSION)
    errors = []
    measurements = validate_snow_actions(builder.result, module.common, errors)
    if errors:
        raise RuntimeError("Snow validation failed:\n" + "\n".join(errors))
    first_curves = curves()
    remove_snow()
    builder._reset_pose()
    if signature() != unchanged:
        raise RuntimeError("Snow refresh changed model, weights, rig or another action")
    build_snow_actions(builder, module.common, module.V2_GENERATOR_VERSION)
    if curves() != first_curves:
        raise RuntimeError("Snow action curves are not deterministic")
    rig.animation_data.action = None
    builder._reset_pose()
    manifest["content_signature_sha256"] = signature()
    rows = {row["name"]: row for row in manifest["actions"]}
    rows.update({name: action_manifest(records[name]) for name in NAMES})
    manifest["actions"] = [rows[name] for name in sorted(rows)]
    manifest["action_count"] = manifest["torso_skin"]["action_count"] = len(records)
    attach_metadata(manifest, builder.result, measurements)
    builder.result.root["bp_content_signature_sha256"] = manifest["content_signature_sha256"]
    bpy.context.scene["bp_content_signature_sha256"] = manifest["content_signature_sha256"]
    if not args.validate_only:
        for directory in (args.source_dir, args.model_dir, args.animation_dir):
            directory.mkdir(parents=True, exist_ok=True)
        module.common.export_animation_fbx(config.animation_fbx, builder.result)
        rig.animation_data.action = None
        builder._reset_pose()
        module.common.save_blend(config.output)
        config.manifest.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("Snow refresh validated: supported soles, extraction height, knees, closed loops, deterministic curves; other hero content unchanged.")
    print(json.dumps(measurements))


if __name__ == "__main__":
    main()
