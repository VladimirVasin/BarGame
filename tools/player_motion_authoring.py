"""Secondary weight shifts for the hero's existing in-place foot phases."""

from math import cos, radians, sin, tau

from mathutils import Quaternion, Vector


# Spine, chest and head respond at different points in each stride. The
# pelvis and limbs retain their authored landmarks for the ground solver.
GAITS = {
    "Idle": (1.6, .5, .8),
    "Walk": (2.7, 3.5, 1.5),
    "WalkBack": (2.0, 2.1, 1.1),
    "Run": (3.3, 3.7, 1.7),
    "TurnLeft": (1.9, -3.8, 2.1),
    "TurnRight": (1.9, 3.8, -2.1),
    "SnowWalk": (2.5, 2.6, 1.4),
    "SnowWalkBackward": (2.1, 2.0, 1.1),
}


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
    for name, (load, counter, lag) in GAITS.items():
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
