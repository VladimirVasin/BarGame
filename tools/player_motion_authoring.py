"""Cyclic in-place strides and secondary weight shifts for the production hero."""

import hashlib
import json
from collections import defaultdict
from math import cos, degrees, floor, radians, sin, sqrt, tau
from pathlib import Path
from tempfile import TemporaryDirectory

import bpy
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree


SECONDARY_GAITS = {
    "Idle": (1.6, .5, .8),
    "TurnLeft": (1.9, -3.8, 2.1),
    "TurnRight": (1.9, 3.8, -2.1),
    "SnowWalk": (2.5, 2.6, 1.4),
    "SnowWalkBackward": (2.1, 2.0, 1.1),
}

# One leg's cycle (fore/aft metres, sole lift in metres, boot roll in
# degrees). The second leg follows half a cycle later. A negative travel
# value leads in Blender's -Y forward space.
WALK_FOOT = (
    (-.254, 0., -12), (-.171, 0., -4), (-.024, 0., 0), (.114, 0., 7),
    (.247, 0., 18), (.280, .030, 22), (.074, .126, 4), (-.184, .054, -9),
)
# Let the hip rise above each supporting leg. The mirrored steps have
# separate heights to fit the small left/right differences in the bind rig.
WALK_RISE = (-.0595, -.037, -.008, 0., -.043, -.025, -.007, -.005)
RUN_FOOT = (
    (-.32, 0., -15), (-.13, 0., -1), (.13, 0., 12), (.35, .052, 32),
    (.37, .227, 26), (.23, .323, 6), (-.085, .234, -9), (-.30, .083, -15),
)
# The pelvis rises through stance and toe-off, then descends to the next heel.
RUN_RISE = (-.084, -.079, -.040, -.005, .015, .022, .028, 0., -.053)
STRIDE_TIMINGS = {"Walk": (24, False), "WalkBack": (24, True), "Run": (18, False)}
SOURCE_FPS = 24


def _cycle(values, phase):
    """Periodic cubic Hermite with monotone tangents between landmarks."""
    count = len(values)
    frame = (phase % 1.) * count
    index = floor(frame)
    t = frame - index
    previous, start, end, following = (values[(index + offset) % count]
                                       for offset in (-1, 0, 1, 2))

    def tangent(before, after):
        return 2 * before * after / (before + after) if before * after > 0 else 0.

    outgoing = tangent(start - previous, end - start)
    incoming = tangent(end - start, following - end)
    return (start + (end - start) * t * t * (3 - 2 * t)
            + t * (1 - t) * (outgoing * (1 - t) - incoming * t))


def _direction(side, pitch, spread):
    pitch, spread = radians(pitch), radians(spread)
    return Vector((side * sin(spread), -cos(spread) * sin(pitch), -cos(spread) * cos(pitch)))


def _stride_pose(relaxed, bone_pose, phase, running, upper_rest):
    angle = tau * phase
    load = cos(2 * (angle - .60))
    sway = cos(angle - .48)
    hip_turn = cos(angle + .24)
    rib_turn = cos(angle - .29)
    # The alternating offset fits the small difference in left/right reach.
    vertical = (_cycle(RUN_RISE, 2 * phase) - .012 * cos(angle + .32)
                if running else _cycle(WALK_RISE, phase))
    pose = {
        **relaxed,
        "pelvis": bone_pose(
            rotation_degrees=((3.6 if running else 1.0) + .9 * load, 2.6 * hip_turn, -1.5 * sway),
            armature_location_m=((.012 if running else .017) * sway, 0., vertical),
        ),
        "spine": bone_pose(rotation_degrees=(
            (5.8 if running else -3.4) + 1.1 * sin(2 * angle - .45),
            -1.8 * rib_turn, 1.05 * cos(angle - .68))),
        "chest": bone_pose(rotation_degrees=(
            (5.0 if running else 3.5) + 1.5 * sin(2 * angle - .84),
            (-4.5 if running else -3.7) * rib_turn, 1.05 * sway)),
        "neck": bone_pose(rotation_degrees=(
            (-6.3 if running else -1.5) - .8 * sin(2 * angle - .63),
            1.15 * rib_turn, -.60 * sway)),
        "head": bone_pose(rotation_degrees=(
            (-4.4 if running else 1.3) - 1.0 * sin(2 * angle - .52),
            1.0 * cos(angle - .50), -.45 * sway)),
    }

    for suffix, side, leg_phase in ((".L", 1, phase), (".R", -1, phase + .5)):
        # The shoulder leads, the elbow closes, then the wrist follows.
        # Both directions are authored in the chest's frame. A fixed local
        # bend on the A-pose forearm aims its length back into the jacket.
        arm_angle = tau * leg_phase
        swing = -cos(arm_angle - .12)
        flex = .5 - .5 * cos(arm_angle - .32)
        pitch = (30. if running else 22.) * swing - (5. if running else 2.)
        upper_direction = _direction(side, pitch, (16. if running else 11.) + 2. * flex)
        forearm_direction = _direction(
            side, pitch + (66. + 13. * flex if running else 15. + 10. * flex),
            2.5 - 4. * flex)
        upper_delta = upper_rest[suffix].rotation_difference(upper_direction)
        pose["upper_arm" + suffix] = bone_pose(target_direction=tuple(upper_direction))
        pose["forearm" + suffix] = bone_pose(
            target_direction=tuple(upper_delta.inverted() @ forearm_direction))
        pose["hand" + suffix] = bone_pose(rotation_degrees=(
            3. + 3. * sin(arm_angle - .52), -side * (5. + 2. * flex), side * 2.))
    return pose


def _fit_boots(builder, bone_pose, pose, phase, profile, soles, name):
    rig = builder.result.rig
    scale = builder.scale
    builder._reset_pose()
    builder._apply_pose(pose)
    for suffix, side, leg_phase in ((".L", 1, phase), (".R", -1, phase + .5)):
        along, lift, roll = (_cycle(track, leg_phase) for track in zip(*profile))
        thigh, shin, foot = (rig.pose.bones[name + suffix] for name in ("thigh", "shin", "foot"))
        boot_rotation = Quaternion((1., 0., 0.), radians(roll))
        sole_below_ankle = min((boot_rotation @ point).z for point in soles[suffix])
        ankle = foot.bone.head_local.copy()
        ankle.x += side * .012 * scale
        ankle.y += along * scale
        ankle.z = lift * scale - sole_below_ankle

        # Solve the reach with the anatomical knee pole used by the snow gait.
        # The boot rolls over the actual low point of its sole as it pitches.
        hip = thigh.head.copy()
        delta = ankle - hip
        length = delta.length
        a, b = thigh.length, shin.length
        if length >= a + b - .001 * scale:
            raise RuntimeError(f"{name} {phase:.4f} {suffix} cannot reach its sole: {length:.4f}/{a+b:.4f} m")
        axis = delta.normalized()
        pole = Vector((side * .10, -1., 0.))
        bend = (pole - axis * pole.dot(axis)).normalized()
        along_leg = (a * a - b * b + length * length) / (2. * length)
        knee = hip + axis * along_leg + bend * sqrt(max(0., a * a - along_leg * along_leg))
        pose["thigh" + suffix] = bone_pose(armature_direction=tuple(knee - hip))
        pose["shin" + suffix] = bone_pose(armature_direction=tuple(ankle - knee))
        pose["foot" + suffix] = bone_pose(armature_direction=tuple(
            boot_rotation @ (foot.bone.tail_local - foot.bone.head_local)))


def build_strides(builder, common, generator_version, takes=STRIDE_TIMINGS):
    result = builder.result
    relaxed = builder.relaxed_pose()
    bones = result.rig.data.bones
    upper_rest = {side: (bones["upper_arm" + side].tail_local
                         - bones["upper_arm" + side].head_local).normalized()
                  for side in (".L", ".R")}
    animation = result.rig.animation_data_create()
    animation.action = None
    builder._reset_pose()
    into_rig = result.rig.matrix_world.inverted()
    parts = {part.obj.name: part.obj for part in result.parts}
    soles = {}
    for suffix in upper_rest:
        sole = parts["CLO_BootSole" + suffix]
        transform = into_rig @ sole.matrix_world
        ankle = bones["foot" + suffix].head_local
        soles[suffix] = [transform @ point.co - ankle for point in sole.data.vertices]

    for name in takes:
        frames, reverse = STRIDE_TIMINGS[name]
        old = result.actions.pop(name, None)
        if old is not None:
            bpy.data.actions.remove(old.action)
        keys = []
        running = name == "Run"
        profile = RUN_FOOT if running else WALK_FOOT
        for frame in range(frames + 1):
            phase = ((-frame if reverse else frame) % frames) / frames
            pose = _stride_pose(relaxed, common.BonePose, phase, running, upper_rest)
            _fit_boots(builder, common.BonePose, pose, phase, profile, soles, name)
            keys.append((frame / frames, pose))
        builder._create_action(name, "locomotion", frames / SOURCE_FPS, True,
                               frames, SOURCE_FPS, keys, interpolation="BEZIER")
        action = result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            # With a repeat modifier, the auto handles on either side of
            # frame zero use the neighbouring pose across the loop seam.
            curve.modifiers.new("CYCLES")
            curve.update()
        action["bp_generator_version"] = generator_version
        action["bp_torso_skin"] = "pelvis_spine_chest_v1"
        action["bp_event_count"] = 0
        if running:
            action["bp_gait_style"] = "heavy_weary"
            action["bp_landmark_count"] = len(RUN_FOOT)
            action["bp_short_flight"] = True


def _posed_vertices(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    return [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]


def _validate_strides(result, common, scale):
    rig = result.rig
    scene = bpy.context.scene
    animation = rig.animation_data
    previous = animation.action, scene.frame_current, scene.frame_subframe
    parts = {part.obj.name: part.obj for part in result.parts}
    sides = ((".L", 1), (".R", -1))
    boots = [parts["CLO_BootSole" + suffix] for suffix, _ in sides]
    coat = [obj for name, obj in parts.items()
            if name.startswith(("CLO_JacketBody", "CLO_JacketPocket", "CLO_JacketFlap"))]
    arms = {suffix: [obj for name, obj in parts.items()
                    if name.endswith(suffix) and name.startswith((
                        "CLO_JacketForearm", "CLO_JacketCuff", "GEO_Hand", "GEO_Thumb", "GEO_Finger"))]
            for suffix, _ in sides}
    evidence = {}
    try:
        for name, (frames, reverse) in STRIDE_TIMINGS.items():
            action = result.actions[name].action
            animation.action = action
            for curve in common.iter_action_fcurves(action):
                first, last = curve.evaluate(0), curve.evaluate(frames)
                outgoing = (curve.evaluate(.01) - first) / .01
                incoming = (last - curve.evaluate(frames - .01)) / .01
                if abs(first - last) > 1e-5 or abs(outgoing - incoming) > .003:
                    raise RuntimeError(f"{name} breaks its loop at {curve.data_path}[{curve.array_index}]")

            min_clearance = float("inf")
            low_sole, high_sole = float("inf"), float("-inf")
            max_support_flex = 0.
            running = name == "Run"
            support_start, support_end, flex_limit = (.15, .30, 32.) if running else (.18, .32, 27.)
            for tick in range(frames * 4):
                frame = tick / 4
                scene.frame_set(floor(frame), subframe=frame % 1.)
                depsgraph = bpy.context.evaluated_depsgraph_get()
                sole = min(point.z for obj in boots for point in _posed_vertices(obj, depsgraph))
                low_sole, high_sole = min(low_sole, sole), max(high_sole, sole)
                if tick in (0, frames * 2):
                    left = rig.pose.bones["foot.L"].head
                    right = rig.pose.bones["foot.R"].head
                    lead = right.y - left.y if tick == 0 else left.y - right.y
                    if lead < .20 * scale or abs(sole) > .006 * scale:
                        raise RuntimeError(f"{name} lost its planted heel at cycle {frame / frames:g}")

                vertices, polygons = [], []
                for obj in coat:
                    polygons.extend(tuple(len(vertices) + index for index in face.vertices)
                                    for face in obj.data.polygons)
                    vertices.extend(_posed_vertices(obj, depsgraph))
                surface = BVHTree.FromPolygons(vertices, polygons)
                for suffix, side in sides:
                    leg_phase = ((-frame if reverse else frame) / frames + (0.5 if side < 0 else 0.)) % 1.
                    if support_start <= leg_phase <= support_end:
                        hip, knee, ankle = (rig.pose.bones[bone + suffix].head for bone in ("thigh", "shin", "foot"))
                        flex = degrees((knee - hip).angle(ankle - knee))
                        max_support_flex = max(max_support_flex, flex)
                    reach = Vector((-side, 0., 0.))
                    for obj in arms[suffix]:
                        for point in _posed_vertices(obj, depsgraph):
                            hit, *_ = surface.ray_cast(Vector((side * scale, point.y, point.z)), reach, scale)
                            if hit is None:
                                continue
                            gap = side * (point.x - hit.x)
                            if gap < .005 * scale:
                                raise RuntimeError(
                                    f"{name} {obj.name} loses jacket clearance at frame {frame:g}: {gap:.4f} m")
                            min_clearance = min(min_clearance, gap)

            if low_sole < -.006 * scale or (not running and high_sole > .006 * scale):
                raise RuntimeError(f"{name} lost ground support: {low_sole:.4f} to {high_sole:.4f} m")
            if running and not .035 * scale < high_sole < .090 * scale:
                raise RuntimeError(f"{name} lost its short flight: {high_sole:.4f} m")
            if not 0. < max_support_flex <= flex_limit:
                raise RuntimeError(f"{name} stays crouched over its support: {max_support_flex:.1f} degrees")
            evidence[name] = {
                "minimum_arm_to_jacket_side_gap_m": round(min_clearance, 5),
                "minimum_lower_sole_m": round(low_sole, 5),
                "maximum_lower_sole_m": round(high_sole, 5),
                "maximum_midstance_knee_flex_degrees": round(max_support_flex, 2),
            }
    finally:
        animation.action = previous[0]
        scene.frame_set(previous[1], subframe=previous[2])
    return evidence


def attach_stride_metadata(payload, result, common, scale):
    payload["stride_authoring_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    payload["ordinary_strides"] = _validate_strides(result, common, scale)


def _fbx_stride_curves(root):
    objects = next(elem for elem in root.elems if elem.id == b"Objects")
    connections = next(elem for elem in root.elems if elem.id == b"Connections")
    by_id = {elem.props[0]: elem for elem in objects.elems}
    incoming, outgoing = defaultdict(list), defaultdict(list)
    for link in connections.elems:
        _, child, parent, *channel = link.props
        incoming[parent].append((child, channel))
        outgoing[child].append((parent, channel))

    curves = {}
    for stack in objects.elems:
        if stack.id != b"AnimationStack":
            continue
        name = stack.props[1].split(b"\0")[0].decode().rsplit("|", 1)[-1]
        if name not in STRIDE_TIMINGS:
            continue
        (layer, _), = incoming[stack.props[0]]
        for node, _ in incoming[layer]:
            (target, path), = [(target, channel) for target, channel in outgoing[node] if channel]
            target_name = by_id[target].props[1]
            for curve, (axis,) in incoming[node]:
                key = (name, target_name, *path, axis)
                if key in curves or by_id[curve].id != b"AnimationCurve":
                    raise RuntimeError(f"Unexpected stride curve {key}")
                curves[key] = by_id[curve]
    return curves


def _export_strides(previous_path, output_path, result, common, takes):
    from io_scene_fbx import encode_bin, parse_fbx

    # Bake the new takes with Blender. Keep the published samples for every
    # other action, since recooking Euler curves can change their trajectories.
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=output_path.parent) as staging:
        baked_path = Path(staging) / output_path.name
        common.export_animation_fbx(baked_path, result)
        baked, baked_version = parse_fbx.parse(baked_path)
    bank, version = parse_fbx.parse(previous_path)
    previous = _fbx_stride_curves(bank)
    updated = _fbx_stride_curves(baked)
    if version != baked_version or previous.keys() != updated.keys() or {key[0] for key in updated} != set(STRIDE_TIMINGS):
        raise RuntimeError("The stride FBX must retain the published skeleton and animation channels")
    for key, curve in previous.items():
        if key[0] not in takes:
            continue
        replacement = updated[key]
        # Curve IDs are referenced by the published bank's connections.
        curve.props[1:] = replacement.props[1:]
        curve.props_type[1:] = replacement.props_type[1:]
        curve.elems[:] = replacement.elems

    writers = {ord(code): "add_" + method for code, method in (
        ("B", "bool"), ("C", "char"), ("Z", "int8"), ("Y", "int16"), ("I", "int32"), ("L", "int64"),
        ("F", "float32"), ("D", "float64"), ("R", "bytes"), ("S", "string"),
        ("b", "bool_array"), ("c", "byte_array"), ("i", "int32_array"), ("l", "int64_array"),
        ("f", "float32_array"), ("d", "float64_array"))}

    def encode(elem):
        binary = encode_bin.FBXElem(elem.id)
        for kind, value in zip(elem.props_type, elem.props, strict=True):
            getattr(binary, writers[kind])(value)
        binary.elems.extend(encode(child) for child in elem.elems)
        return binary

    encode_bin.write(output_path, encode(bank), version)


def refresh_strides(module, config, takes):
    """Replace selected ordinary takes in the existing production source."""
    original, builder = module.player_counter_seat_actions.load(module, config)
    if (config.height, config.seed, config.pose) != (original["height_m"], original["seed"], original["pose"]):
        raise ValueError("Stride refresh requires the production height, seed and bind pose")
    result = builder.result
    common = module.common
    hashes = (original["face_atlas"]["sha256"], original["texture_bindings"][0]["sha256"],
              original["bare_skin_atlas"]["sha256"])

    def signature(preserved_only=False):
        result.rig.animation_data.action = None
        builder._reset_pose()
        actions = result.actions
        try:
            if preserved_only:
                result.actions = {name: record for name, record in actions.items() if name not in takes}
            return module.content_signature(config, result, *hashes)
        finally:
            result.actions = actions

    if signature() != original["content_signature_sha256"]:
        raise RuntimeError("Production hero source differs from its manifest")
    preserved = signature(preserved_only=True)
    build_strides(builder, common, module.V2_GENERATOR_VERSION, takes)
    first = signature()
    build_strides(builder, common, module.V2_GENERATOR_VERSION, takes)
    if signature() != first or signature(preserved_only=True) != preserved:
        raise RuntimeError("Stride refresh changed unrelated content or produced nondeterministic curves")
    attach_stride_metadata(original, result, common, builder.scale)
    original["content_signature_sha256"] = first
    result.root["bp_content_signature_sha256"] = first
    bpy.context.scene["bp_content_signature_sha256"] = first
    _export_strides(module.DEFAULT_ANIMATION_FBX, config.animation_fbx, result, common, takes)
    result.rig.animation_data.action = None
    builder._reset_pose()
    config.output.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(config.output), compress=True, relative_remap=False)
    config.manifest.parent.mkdir(parents=True, exist_ok=True)
    config.manifest.write_text(json.dumps(original, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("Hero strides validated: periodic motion, heel contacts, run flight and jacket clearance.", flush=True)


def _rotation_tracks(action, bone, common):
    path = f'pose.bones["{bone}"].rotation_quaternion'
    tracks = sorted((curve for curve in common.iter_action_fcurves(action)
                     if curve.data_path == path), key=lambda curve: curve.array_index)
    if len(tracks) != 4:
        raise ValueError(f"{action.name} lacks complete rotations for {bone}")
    return tracks


def _offset_rotation(record, bone, profile, common):
    action = record.action
    channels = _rotation_tracks(action, bone.name, common)
    inverse = bone.matrix_local.to_quaternion().inverted()
    right, up = inverse @ Vector((1., 0., 0.)), inverse @ Vector((0., 0., 1.))
    for keys in zip(*(curve.keyframe_points for curve in channels), strict=True):
        frame = keys[0].co.x
        if any(abs(key.co.x - frame) > 1e-5 for key in keys):
            raise ValueError(f"{action.name} has misaligned quaternion keys: {bone.name}")
        t = (frame - action.frame_start) / (action.frame_end - action.frame_start)
        pitch, yaw = profile(0. if t >= 1. else t)
        rotation = Quaternion(tuple(key.co.y for key in keys))
        posed = rotation @ Quaternion(right, radians(pitch)) @ Quaternion(up, radians(yaw))
        posed.make_compatible(rotation)
        for key, value in zip(keys, posed):
            key.co.y = value
    for curve in channels:
        curve.update()


def author(result, common):
    rig = result.rig
    for name, (load, counter, lag) in SECONDARY_GAITS.items():
        record = result.actions[name]
        # Two loading pulses per stride, with one opposing twist on each side.
        # At phase zero Idle still meets the original Relaxed pose exactly.
        _offset_rotation(record, rig.data.bones["spine"],
                         lambda t: (load * .5 * (1 - cos(2 * tau * t)),
                                    -counter * .42 * sin(tau * t)), common)
        _offset_rotation(record, rig.data.bones["chest"],
                         lambda t: (load * .45 * sin(2 * tau * t),
                                    counter * sin(tau * t)), common)
        _offset_rotation(record, rig.data.bones["head"],
                         lambda t: (-load * .34 * sin(2 * tau * t),
                                    -lag * (sin(tau * t - .45) + sin(.45))), common)
