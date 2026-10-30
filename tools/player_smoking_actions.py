"""Production-rig smoking contacts and a bounded action-only refresh."""
from __future__ import annotations

import copy
import hashlib
import json
import math
from pathlib import Path

import bpy
import player_hand_grip
import player_smoking_grip
from mathutils import Matrix, Quaternion, Vector

NAMES = ("SmokeEnter", "SmokeLoop", "SmokeExit")
ARM = ("upper_arm.R", "forearm.R", "hand.R")
MAXIMUM_WRIST_BEND_DEGREES = 35.
MAXIMUM_WRIST_TWIST_DEGREES = 35.


def wrist_angles(rotation):
    """Anatomical delta from the hand bind relation to the forearm (+Y)."""
    swing, twist = rotation.to_swing_twist("Y")
    bend = min(swing.angle, 2. * math.pi - swing.angle)
    return math.degrees(abs(bend)), math.degrees(abs(twist))


def solve_contact(builder, pose, common):
    """Bake the hand socket onto the current mouth with a low right elbow.

    Fixed local Euler values missed the current production mouth height.
    Solve in the actual production rig, then keep ordinary bone-only keys.
    """
    rig = builder.result.rig
    animation = rig.animation_data_create()
    previous_action = animation.action
    previous_basis = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    try:
        animation.action = None
        builder._reset_pose()
        builder._apply_pose(pose)
        upper, lower, hand = (rig.pose.bones[name] for name in ARM)
        mouth = rig.pose.bones["SOCKET_Mouth"]
        socket = rig.pose.bones["SOCKET_Cigarette.R"]
        # Position and outward direction constrain the cigarette; its roll
        # remains free. Matching the mouth's entire quaternion bends/twists
        # the wrist to reproduce an unrelated head-bone cross section.
        socket_in_hand = hand.matrix.inverted() @ socket.matrix
        base_hand_rotation = mouth.matrix.to_quaternion() @ socket_in_hand.to_quaternion().inverted()
        forward = (mouth.tail-mouth.head).normalized()
        origin, elbow_start, wrist_start = upper.head.copy(), lower.head.copy(), hand.head.copy()
        upper_start, lower_start = upper.matrix.to_quaternion(), lower.matrix.to_quaternion()
        upper_rest, lower_rest, hand_rest = (bone.bone.matrix_local.to_quaternion() for bone in (upper, lower, hand))
        a, b = (elbow_start-origin).length, (wrist_start-elbow_start).length
        pole = Vector((-.35, -.12, -.32)) * builder.scale

        def candidate(roll_degrees, forearm_roll_degrees):
            hand_rotation = Quaternion(forward, math.radians(roll_degrees)) @ base_hand_rotation
            target = mouth.head - hand_rotation @ socket_in_hand.translation
            delta = target-origin
            distance = delta.length
            if not abs(a-b)+1e-5 < distance < a+b-1e-5:
                raise RuntimeError("Smoking mouth target is outside the production arm's reach")
            axis = delta.normalized()
            bend_axis = (pole-axis*pole.dot(axis)).normalized()
            along = (a*a-b*b+distance*distance)/(2.*distance)
            elbow = origin + axis*along + bend_axis*math.sqrt(max(0., a*a-along*along))
            upper_delta = (elbow_start-origin).rotation_difference(elbow-origin)
            upper_rotation = upper_delta @ upper_start
            lower_rotation = (upper_delta @ (wrist_start-elbow_start)).rotation_difference(target-elbow) @ upper_delta @ lower_start
            lower_rotation = Quaternion((target-elbow).normalized(), math.radians(forearm_roll_degrees)) @ lower_rotation
            neutral_hand_rotation = lower_rotation @ lower_rest.inverted() @ hand_rest
            wrist_rotation = neutral_hand_rotation.inverted() @ hand_rotation
            wrist_bend, wrist_twist = wrist_angles(wrist_rotation)
            neutral_lower_rotation = upper_rotation @ upper_rest.inverted() @ lower_rest
            _, forearm_twist = wrist_angles(neutral_lower_rotation.inverted() @ lower_rotation)
            # Prefer a straight wrist while retaining the low authored elbow.
            # A small forearm-roll cost avoids spending all pronation there.
            cost = 2. * wrist_bend ** 2 + 2. * wrist_twist ** 2 + .025 * forearm_twist ** 2 + .002 * forearm_roll_degrees ** 2
            return cost, upper_rotation, lower_rotation, hand_rotation, elbow, target

        # Bounded coarse search plus local refinements. Free forearm axial
        # rotation carries pronation in the forearm rather than the wrist.
        # Candidate evaluation is pure math; only the chosen pose updates bpy.
        best = min(((roll, pronation) for roll in range(-180, 181, 4)
                    for pronation in range(-120, 121, 6)),
                   key=lambda angles: candidate(*angles)[0])
        for step, count in ((.5, 8), (.1, 5)):
            center_roll, center_pronation = best
            best = min(((center_roll + x * step, center_pronation + y * step)
                        for x in range(-count, count + 1) for y in range(-count, count + 1)),
                       key=lambda angles: candidate(*angles)[0])
        _, upper_rotation, lower_rotation, hand_rotation, elbow, target = candidate(*best)
        for bone, position, rotation in ((upper, origin, upper_rotation),
                (lower, elbow, lower_rotation), (hand, target, hand_rotation)):
            bone.matrix = Matrix.Translation(position) @ rotation.to_matrix().to_4x4()
            bpy.context.view_layer.update()
        solved = dict(pose)
        for name in ARM:
            bone = rig.pose.bones[name]
            solved[name] = common.BonePose(rotation_degrees=tuple(math.degrees(value)
                for value in bone.rotation_quaternion.to_euler("XYZ")))
        return solved
    finally:
        animation.action = previous_action
        for name, matrix in previous_basis.items():
            rig.pose.bones[name].matrix_basis = matrix
        bpy.context.view_layer.update()


def build(builder, common):
    BonePose = common.BonePose
    relaxed = builder.relaxed_pose()
    smoking_relaxed = builder.merge_pose(relaxed, {"SOCKET_Cigarette.R": player_smoking_grip.socket_pose(
        builder.result, builder.scale, player_hand_grip, common)})
    smoke_rest = builder.merge_pose(
        smoking_relaxed,
        {
            "pelvis": BonePose(rotation_degrees=(0.8, -0.4, -1.6)),
            "spine": BonePose(rotation_degrees=(-2.8, 0.0, 1.8)),
            "chest": BonePose(rotation_degrees=(3.2, 0.0, -1.8)),
            "neck": BonePose(rotation_degrees=(-1.5, 0.0, 1.0)),
            "head": BonePose(rotation_degrees=(1.0, 0.0, -0.8)),
        },
    )
    smoke_settle = builder.merge_pose(
        smoke_rest,
        {
            "pelvis": BonePose(rotation_degrees=(1.2, -0.6, -2.2)),
            "spine": BonePose(rotation_degrees=(-3.4, 0.0, 2.4)),
            "head": BonePose(rotation_degrees=(2.0, 0.0, -1.4)),
        },
    )
    smoke_reach = builder.merge_pose(
        smoke_settle,
        {
            "upper_arm.R": BonePose(target_direction=(0.03, -0.12, -0.22)),
            "forearm.R": BonePose(rotation_degrees=(-48.0, 10.0, -24.0)),
            "hand.R": BonePose(rotation_degrees=(14.0, -8.0, 6.0)),
            "head": BonePose(rotation_degrees=(5.0, 0.0, 1.8)),
        },
    )
    smoke_draw_low = builder.merge_pose(
        smoke_settle,
        {
            "upper_arm.R": BonePose(target_direction=(-0.03, -0.12, -0.24)),
            "forearm.R": BonePose(rotation_degrees=(-38.0, 5.0, -18.0)),
            "hand.R": BonePose(rotation_degrees=(12.0, -12.0, 8.0)),
            "head": BonePose(rotation_degrees=(2.5, 0.0, 0.5)),
        },
    )
    smoke_lift = builder.merge_pose(
        smoking_relaxed,
        {
            "upper_arm.R": BonePose(rotation_degrees=(8.0, -8.0, 48.0)),
            "forearm.R": BonePose(rotation_degrees=(5.0, -28.0, 60.0)),
            "hand.R": BonePose(rotation_degrees=(0.0, -28.0, 10.0)),
            "chest": BonePose(rotation_degrees=(3.2, 0.0, -1.4)),
            "head": BonePose(rotation_degrees=(0.5, 0.0, -0.6)),
        },
    )
    smoke_mouth = solve_contact(builder, smoking_relaxed, common)
    smoke_light = builder.merge_pose(
        smoke_mouth,
        {
            "upper_arm.L": BonePose(
                rotation_degrees=(10.0, 10.0, -70.0),
            ),
            "forearm.L": BonePose(
                rotation_degrees=(5.0, 38.0, -88.0),
            ),
            "hand.L": BonePose(rotation_degrees=(-5.0, 35.0, -10.0)),
        },
    )
    smoke_first_inhale = builder.merge_pose(
        smoke_light,
        {
            "spine": BonePose(rotation_degrees=(-2.4, 0.0, 1.2)),
            "chest": BonePose(rotation_degrees=(4.4, 0.0, -1.4)),
            "upper_arm.L": BonePose(
                rotation_degrees=(8.0, 8.0, -62.0),
            ),
            "forearm.L": BonePose(
                rotation_degrees=(4.0, 32.0, -78.0),
            ),
        },
    )
    smoke_first_inhale = solve_contact(builder, smoke_first_inhale, common)

    smoke_inhale = builder.merge_pose(
        smoke_mouth,
        {
            "spine": BonePose(rotation_degrees=(-2.4, 0.0, 1.2)),
            "chest": BonePose(rotation_degrees=(4.4, 0.0, -1.4)),
        },
    )
    smoke_inhale = solve_contact(builder, smoke_inhale, common)

    smoke_breath_hold = builder.merge_pose(
        smoke_mouth,
        {
            "spine": BonePose(rotation_degrees=(-2.1, 0.0, 1.0)),
            "chest": BonePose(rotation_degrees=(4.0, 0.0, -1.3)),
        },
    )
    smoke_breath_hold = solve_contact(builder, smoke_breath_hold, common)

    smoke_lower = builder.merge_pose(
        smoking_relaxed,
        {
            "upper_arm.R": BonePose(rotation_degrees=(7.0, -7.0, 42.0)),
            "forearm.R": BonePose(rotation_degrees=(2.0, -22.0, 52.0)),
            "hand.R": BonePose(rotation_degrees=(2.0, -20.0, 8.0)),
            "chest": BonePose(rotation_degrees=(3.4, 0.0, -1.5)),
        },
    )
    smoke_exhale = builder.merge_pose(
        smoke_rest,
        {
            "chest": BonePose(rotation_degrees=(2.2, 0.0, -1.6)),
            "neck": BonePose(rotation_degrees=(-0.5, 0.0, 2.0)),
            "head": BonePose(rotation_degrees=(2.8, 0.0, 3.8)),
        },
    )
    smoke_inspect = builder.merge_pose(
        smoke_draw_low,
        {
            "forearm.R": BonePose(rotation_degrees=(-24.0, 2.0, -12.0)),
            "hand.R": BonePose(rotation_degrees=(8.0, -16.0, 12.0)),
            "head": BonePose(rotation_degrees=(7.0, 0.0, 4.0)),
        },
    )
    smoke_extend = builder.merge_pose(
        smoke_settle,
        {
            "upper_arm.R": BonePose(target_direction=(-0.08, -0.26, -0.04)),
            "forearm.R": BonePose(rotation_degrees=(-12.0, -8.0, 12.0)),
            "hand.R": BonePose(rotation_degrees=(8.0, -12.0, 14.0)),
            "head": BonePose(rotation_degrees=(2.0, 0.0, -1.0)),
        },
    )
    smoke_flick = builder.merge_pose(
        smoke_extend,
        {
            "hand.R": BonePose(rotation_degrees=(28.0, -12.0, 24.0)),
            "head": BonePose(rotation_degrees=(4.0, 0.0, -0.5)),
        },
    )
    smoke_gaze_follow = builder.merge_pose(
        smoke_settle,
        {
            "upper_arm.R": BonePose(target_direction=(-0.06, -0.24, -0.10)),
            "forearm.R": BonePose(rotation_degrees=(-8.0, -6.0, 8.0)),
            "hand.R": BonePose(rotation_degrees=(12.0, -8.0, 10.0)),
            "neck": BonePose(rotation_degrees=(-1.0, 0.0, 2.0)),
            "head": BonePose(rotation_degrees=(8.0, 0.0, 5.0)),
        },
    )
    smoke_return = builder.merge_pose(
        smoke_rest,
        {
            "upper_arm.R": BonePose(rotation_degrees=(3.0, -3.0, 18.0)),
            "forearm.R": BonePose(rotation_degrees=(-2.0, -8.0, 18.0)),
            "hand.R": BonePose(rotation_degrees=(5.0, -8.0, 6.0)),
        },
    )
    builder._create_action(
        "SmokeEnter", "smoking", 4.0, False, 48, 12,
        (
            (0.0, relaxed),
            (0.16, smoke_settle),
            (0.30, smoke_reach),
            (0.40, smoke_draw_low),
            (0.55, smoke_lift),
            (0.68, smoke_mouth),
            (0.76, smoke_light),
            (0.84, smoke_first_inhale),
            (0.92, smoke_lower),
            (1.0, smoke_rest),
        ),
        extra_keyed_bones=("SOCKET_Cigarette.R",),
    )
    builder._create_action(
        "SmokeLoop", "smoking", 4.0, True, 24, 6,
        (
            (0.0, smoke_rest),
            (1.0 / 6.0, smoke_rest),
            (0.29, smoke_lift),
            (0.42, smoke_mouth),
            (0.50, smoke_inhale),
            (0.625, smoke_breath_hold),
            (0.70, smoke_lower),
            (0.83, smoke_exhale),
            (0.875, smoke_rest),
            (1.0, smoke_rest),
        ),
        extra_keyed_bones=("SOCKET_Cigarette.R",),
    )
    builder._create_action(
        "SmokeExit", "smoking", 2.0, False, 24, 12,
        (
            (0.0, smoke_rest),
            (0.20, smoke_inspect),
            (0.38, smoke_extend),
            (0.50, smoke_flick),
            (0.66, smoke_gaze_follow),
            (0.82, smoke_return),
            (1.0, relaxed),
        ),
        extra_keyed_bones=("SOCKET_Cigarette.R",),
    )


def contact_report(result):
    rows = []
    rig = result.rig
    animation = rig.animation_data_create()
    previous_action = animation.action
    previous_frame = bpy.context.scene.frame_current
    previous_basis = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    try:
        for name, times in (("SmokeEnter", (.68, .76, .84)), ("SmokeLoop", (.42, .50, .625))):
            record = result.actions[name]
            animation.action = record.action
            if len(record.action.slots):
                animation.action_slot = record.action.slots[0]
            for progress in times:
                bpy.context.scene.frame_set(round(record.action.frame_end * progress))
                bpy.context.view_layer.update()
                mouth, cigarette = (rig.pose.bones[bone] for bone in ("SOCKET_Mouth", "SOCKET_Cigarette.R"))
                delta = rig.matrix_world.to_3x3() @ (cigarette.head-mouth.head)
                wrist_bend, wrist_twist = wrist_angles(rig.pose.bones["hand.R"].rotation_quaternion)
                rows.append({"action": name, "progress": progress,
                    "wrist_bend_degrees": round(wrist_bend, 4), "wrist_twist_degrees": round(wrist_twist, 4),
                    "forearm_twist_degrees": round(wrist_angles(rig.pose.bones["forearm.R"].rotation_quaternion)[1], 4),
                    "distance_m": round(delta.length, 7), "vertical_error_m": round(delta.z, 7),
                    "outward_dot": round((cigarette.tail-cigarette.head).normalized().dot(
                        (mouth.tail-mouth.head).normalized()), 7)})
    finally:
        animation.action = previous_action
        bpy.context.scene.frame_set(previous_frame)
        for name, basis in previous_basis.items():
            rig.pose.bones[name].matrix_basis = basis
        bpy.context.view_layer.update()
    return rows


def refresh(module, config):
    """Publish only the smoking action bank, retaining all unrelated content."""
    from player_counter_seat_actions import load
    import player_hand_gestures
    original, builder = load(module, config)
    result = builder.result
    source_rest = {bone.name: (bone.parent.name if bone.parent else None,
        (bone.parent.matrix_local.inverted() @ bone.matrix_local) if bone.parent else bone.matrix_local.copy(),
        result.rig.matrix_world @ bone.matrix_local) for bone in result.rig.data.bones}
    print("SMOKING_BEFORE " + json.dumps(contact_report(result)), flush=True)
    if all(player_smoking_grip.SHAPE in record.obj.data.shape_keys.key_blocks
           for record in result.parts if record.obj.name in player_smoking_grip.NAMES):
        print("SMOKING_SHAFT_BEFORE " + json.dumps(player_smoking_grip.shaft_clearances(
            result,builder.scale,player_hand_grip)),flush=True)
    hashes = (original["face_atlas"]["sha256"], original["texture_bindings"][0]["sha256"],
              original["bare_skin_atlas"]["sha256"])
    canonical_gestures = original["hand_grip"]["gestures"]
    def preserved_gesture_validation(source, scale, grip):
        # These shapes are untouched by a bone-action refresh. Keep the
        # platform-independent published proof only after matching all points.
        parts = {part.obj.name: part.obj for part in source.parts}
        content = {name: {shape: [[round(float(c), 7) for c in point.co]
            for point in parts[name].data.shape_keys.key_blocks[shape].data]
            for shape in sorted(player_hand_gestures.shape_names(name))}
            for name in player_hand_gestures.NAMES}
        actual = hashlib.sha256(json.dumps(content, sort_keys=True).encode()).hexdigest()
        if actual != canonical_gestures["shape_signature"]:
            raise RuntimeError("Smoking refresh changed the canonical finite hand profile")
        return copy.deepcopy(canonical_gestures["validation"])
    def signature():
        result.rig.animation_data.action = None
        builder._reset_pose()
        previous_validate = player_hand_gestures.validate
        try:
            player_hand_gestures.validate = preserved_gesture_validation
            return module.content_signature(config, result, *hashes)
        finally:
            player_hand_gestures.validate = previous_validate
    baseline = signature()
    if baseline != original["content_signature_sha256"]:
        # Corruption-only publication deliberately retains the last full-model
        # signature after freezing export triangulation. Its two canonical
        # hashes are the stricter, current source contract (see that refresh).
        corruption = original.get("hero_corruption", {})
        source_matches = (module.player_corruption.preserved_signature(module, result) ==
                          corruption.get("preserved_source_sha256"))
        shapes_match = (module.player_corruption.manifest(result)["geometry_sha256"] ==
                        corruption.get("geometry_sha256"))
        if not source_matches or not shapes_match:
            raise RuntimeError("Production hero source differs from its manifest; refusing a stale smoking refresh")
        print("Smoking source verified against both post-corruption source/geometry hashes", flush=True)
    def old_shape_signature():
        rows = []
        for record in sorted(result.parts, key=lambda row: row.obj.name):
            keys = record.obj.data.shape_keys
            if keys is not None:
                rows.append((record.obj.name, [(key.name, key.value, key.mute, key.slider_min, key.slider_max,
                    key.interpolation, key.relative_key.name if key.relative_key else None,
                    [tuple(point.co) for point in key.data]) for key in keys.key_blocks
                    if key.name != player_smoking_grip.SHAPE]))
        return hashlib.sha256(repr(rows).encode()).hexdigest()
    frozen_shapes = old_shape_signature()
    def rebuild():
        result.rig.animation_data.action = None
        for name in NAMES:
            old = result.actions.pop(name, None)
            if old is not None:
                bpy.data.actions.remove(old.action)
        build(builder, module.common)
        for name in NAMES:
            action = result.actions[name].action
            action["bp_generator_version"] = module.V2_GENERATOR_VERSION
            action["bp_torso_skin"] = "pelvis_spine_chest_v1"
            action["bp_event_count"] = 0
    def validate():
        errors = []
        module.common.validate_smoking_pose(result, errors)
        if errors:
            raise RuntimeError("Smoking contact validation failed:\n" + "\n".join(errors))
        print("SMOKING_VALIDATED " + json.dumps(contact_report(result)), flush=True)
    # Hash the same complete bind state with the changed takes excluded;
    # this covers meshes, rig, UVs, skins and every other production action.
    complete = result.actions
    result.actions = {name: value for name, value in complete.items() if name not in NAMES}
    preserved = signature()
    result.actions = complete
    grip_proof = player_smoking_grip.author(result, builder.scale, player_hand_grip)
    print("SMOKING_GRIP " + json.dumps(grip_proof), flush=True)
    rebuild()
    validate()
    first = signature()
    player_smoking_grip.author(result, builder.scale, player_hand_grip)
    rebuild()
    validate()
    if signature() != first:
        raise RuntimeError("Smoking action regeneration is nondeterministic")
    complete = result.actions
    result.actions = {name: value for name, value in complete.items() if name not in NAMES}
    if signature() != preserved:
        raise RuntimeError("Smoking refresh changed unrelated hero content")
    result.actions = complete
    if old_shape_signature() != frozen_shapes:
        raise RuntimeError("Smoking grip changed an existing morph, its value or neutral Basis")
    original["smoking_grip"] = player_smoking_grip.manifest(result, builder.scale, player_hand_grip)
    proof = {"contact_limit_m": .025, "minimum_outward_dot": .85,
             "maximum_wrist_bend_degrees": MAXIMUM_WRIST_BEND_DEGREES,
             "maximum_wrist_twist_degrees": MAXIMUM_WRIST_TWIST_DEGREES,
             "contacts": contact_report(result), "fixed_root": True,
             "exact_loop_seam": True, "preserved_other_content": True}
    original["smoking"] = proof
    original["smoking_authoring_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    original["content_signature_sha256"] = first
    original["content_signature_scope"] = "current_source_model_rig_actions; current_face_pixels_use_face_atlas.sha256"
    result.root["bp_content_signature_sha256"] = first
    bpy.context.scene["bp_content_signature_sha256"] = first
    # A finite new morph requires a model export, keeping the production
    # modifier/custom-normal policy and the original neutral rest skeleton.
    # A report may sample a posed clip. Model export must always start from
    # the canonical rest rig, never that last presentation pose.
    result.rig.animation_data.action = None
    builder._reset_pose()
    if any(max(abs(bone.matrix_basis[row][column] - (1. if row == column else 0.))
               for row in range(4) for column in range(4)) > 1e-7 for bone in result.rig.pose.bones):
        raise RuntimeError("Model export requires every bone in canonical neutral basis")
    if signature() != first:
        raise RuntimeError("Neutral export preparation changed the verified source signature")
    module.export_model_fbx(config.fbx, result)
    module.common.export_animation_fbx(config.animation_fbx, result)
    result.rig.animation_data.action = None
    builder._reset_pose()
    module.common.save_blend(config.output)
    config.manifest.parent.mkdir(parents=True, exist_ok=True)
    config.manifest.write_text(json.dumps(original, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    # Inspect the actual FBX output for the new grip without retaining a
    # second model or changing the saved source's neutral/default state.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(config.fbx), use_anim=False)
    imported = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    if set(source_rest) != set(imported.data.bones.keys()):
        raise RuntimeError("Published model changed the canonical rest-bone set")
    maximum_local_error, maximum_world_position_error = 0., 0.
    for name, (parent, expected_local, expected_world) in source_rest.items():
        bone = imported.data.bones[name]
        if (bone.parent.name if bone.parent else None) != parent:
            raise RuntimeError("Published model changed a rest-bone parent: " + name)
        actual_local = bone.parent.matrix_local.inverted() @ bone.matrix_local if bone.parent else bone.matrix_local
        actual_world = imported.matrix_world @ bone.matrix_local
        local_error = max(abs(actual_local[row][column]-expected_local[row][column])
            for row in range(4) for column in range(4))
        world_error = (actual_world.translation-expected_world.translation).length
        maximum_local_error = max(maximum_local_error,local_error)
        maximum_world_position_error = max(maximum_world_position_error,world_error)
        if local_error > 1e-5 or world_error > 1e-5:
            raise RuntimeError(f"FBX changed canonical bone rest: {name} local={local_error:.9g} world={world_error:.9g}")
    print("SMOKING_MODEL_REST_VALIDATED " + json.dumps({"all_bones": len(source_rest),
        "maximum_parent_relative_matrix_error":maximum_local_error,
        "maximum_world_position_error_m":maximum_world_position_error}),flush=True)
    for name in player_smoking_grip.NAMES:
        obj = bpy.data.objects.get(name)
        if obj is None or obj.data.shape_keys is None or player_smoking_grip.SHAPE not in obj.data.shape_keys.key_blocks:
            raise RuntimeError("Published FBX lost its cigarette grip: " + name)
    print("Smoking grip FBX round trip: all six right-hand morphs present", flush=True)
    print("Smoking publication: deterministic mouth contacts; SmokingGrip plus SmokeEnter/Loop/Exit; Basis, rest rig, old morphs and other actions preserved", flush=True)
