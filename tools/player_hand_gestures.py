"""Finite pointing-fist and MCP-only index poses on the original HeroV2 hand.

Every target retains the source topology, wrist, weights, materials and rig.
The index samples are interpolated adjacently, never added to a fist index curl.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

from mathutils import Quaternion, Vector
from mathutils.kdtree import KDTree

CONTRACT = "hero_hand_gestures_v1"
FIST = "PointingFist"
ANGLES = tuple(range(5, 61, 5))
INDEX = "GEO_Finger0.R"
NAMES = ("GEO_Hand.R", "GEO_Thumb.R", *(f"GEO_Finger{i}.R" for i in range(4)))
FINGER_ANGLES = (75., 95., 70.)


def index_shape(angle):
    return f"IndexTap{angle:02d}"


def shape_names(name):
    return {FIST, *(index_shape(angle) for angle in ANGLES)} if name in NAMES else set()


def _points(result, obj, shape="Basis"):
    matrix = result.rig.matrix_world.inverted() @ obj.matrix_world
    return [matrix @ vertex.co for vertex in obj.data.shape_keys.key_blocks[shape].data]


def _mean(points):
    return sum(points, Vector()) / len(points)


def _profiles(result, scale, grip):
    parts = {record.obj.name: record.obj for record in result.parts}
    wrist, direction, across, normal = grip._frame(result, "R")
    neutral = {name: _points(result, parts[name]) for name in NAMES}
    # Match NpcHandPose's geometric frame, including the imported handedness.
    distal = (_mean([_mean(neutral[f"GEO_Finger{i}.R"]) for i in range(4)]) -
              _mean(neutral["GEO_Hand.R"])).normalized()
    thumb = _mean(neutral["GEO_Thumb.R"]) - _mean(neutral[INDEX])
    thumb = (thumb - distal * thumb.dot(distal)).normalized()
    palm_normal = -distal.cross(thumb).normalized()
    if palm_normal.dot(normal) < .9:
        raise RuntimeError("Pointing gesture lost the anatomical right-palm frame")
    fist = {name: [point.copy() for point in values] for name, values in neutral.items()}
    segments = []
    for index in (1, 2, 3):
        name = f"GEO_Finger{index}.R"
        offset, length = ((-.023, .033), (-.007, .042), (.009, .039), (.024, .030))[index]
        start = wrist + (direction * .052 + across * offset) * 1.20 * scale
        end = wrist + (direction * (.052 + length) + across * offset + normal * .004) * 1.20 * scale
        fractions = (0., .42, .80, 1.)
        before = [start + (end - start) * fraction for fraction in fractions]
        centres, total = [start], 0.
        for degrees, first, last in zip(FINGER_ANGLES, before, before[1:]):
            total += degrees
            tangent = direction * math.cos(math.radians(total)) + normal * math.sin(math.radians(total))
            centres.append(centres[-1] + tangent * (last - first).length)
        fist[name] = grip._bend(neutral[name], start, end, centres)
        segments.append((name, before, centres))

    # Oppose the clenched fingers with a tucked two-link thumb. Its embedded
    # root and both original phalange lengths remain fixed.
    def thumb_local(along, width):
        size = 1 + .20 * min(1., along / .030)
        return wrist + (direction * along + across * width) * size * scale
    start = thumb_local(.018, -.022)
    end = thumb_local(.055, -.041) + normal * (.002 * 1.20 * scale)
    before = [start, start + (end - start) * .45, end]
    first, second = (before[1] - start).length, (end - before[1]).length
    target = wrist + (direction * .050 + across * -.002 + normal * .020) * scale
    delta = target - start
    distance = delta.length
    if not abs(first - second) < distance < first + second:
        raise RuntimeError("Pointing thumb is outside its original two-link reach")
    axis = delta.normalized()
    along = (first * first - second * second + distance * distance) / (2 * distance)
    height = math.sqrt(max(0., first * first - along * along))
    hint = -across
    bend = (hint - axis * hint.dot(axis)).normalized()
    centres = [start, start + axis * along + bend * height, target]
    fist["GEO_Thumb.R"] = grip._bend(neutral["GEO_Thumb.R"], start, end, centres)
    segments.append(("GEO_Thumb.R", before, centres))

    start = wrist + (direction * .052 + across * -.023) * 1.20 * scale
    end = wrist + (direction * (.052 + .033) + across * -.023 + normal * .004) * 1.20 * scale
    old_axis = (end - start).normalized()
    fractions = [(point - start).dot(old_axis) for point in neutral[INDEX]]
    minimum = min(fractions)
    root = _mean([point for point, value in zip(neutral[INDEX], fractions) if value - minimum < 1e-6 * scale])
    hinge = direction.cross(normal).normalized()
    profiles = {FIST: fist}
    for angle in ANGLES:
        rotation = Quaternion(hinge, math.radians(angle))
        profiles[index_shape(angle)] = {
            name: [root + rotation @ (point - root) for point in values] if name == INDEX
            else [point.copy() for point in values] for name, values in neutral.items()}
    return parts, neutral, profiles, segments, (wrist, palm_normal, root, hinge)


def author(result, scale, grip):
    parts, _, profiles, _, _ = _profiles(result, scale, grip)
    for shape, meshes in profiles.items():
        for name, points in meshes.items():
            _, matrix = grip._source_points(result, parts[name])
            grip._shape(parts[name], matrix, points, shape)


def _volume(obj, points):
    return sum(points[face.vertices[0]].dot(points[face.vertices[i]].cross(points[face.vertices[i + 1]])) / 6
               for face in obj.data.polygons for i in range(1, len(face.vertices) - 1))


def validate(result, scale, grip):
    parts, neutral, expected, segments, (wrist, normal, root, hinge) = _profiles(result, scale, grip)
    actual = {}
    for shape, meshes in expected.items():
        actual[shape] = {}
        for name, target in meshes.items():
            obj = parts[name]
            if shape not in obj.data.shape_keys.key_blocks:
                raise RuntimeError("Missing authored hand gesture: " + name + ":" + shape)
            points = _points(result, obj, shape)
            if max((point - wanted).length for point, wanted in zip(points, target)) > 1e-7 * scale:
                raise RuntimeError("Authored hand gesture differs from its finite profile: " + name + ":" + shape)
            if _volume(obj, points) <= 0:
                raise RuntimeError("Hand gesture reverses its closed source volume: " + name + ":" + shape)
            if obj.data.shape_keys.key_blocks[shape].value != 0:
                raise RuntimeError("Hand gesture is non-neutral by default: " + name + ":" + shape)
            actual[shape][name] = points
    segment_error = max(abs((a[i + 1] - a[i]).length - (b[i + 1] - b[i]).length)
                        for _, a, b in segments for i in range(len(a) - 1))
    if segment_error > 1e-6 * scale:
        raise RuntimeError("Pointing fist stretches the original finger or thumb links")
    depth = lambda point: (point - wrist).dot(normal)
    contact_depth = max(map(depth, actual[index_shape(60)][INDEX]))
    other_depth, other_name = max((depth(point), name) for name in NAMES if name != INDEX
                                  for point in actual[FIST][name])
    clearance = contact_depth - other_depth
    if clearance < .003 * scale:
        raise RuntimeError(f"Flat pointing fist enters the counter before its index: {other_name} {clearance:.7f} m")
    interpolation_error, lowest_surface_clearance = 0., float("inf")
    for angle in range(61):
        lower = angle // 5 * 5
        upper = min(60, lower + 5)
        fraction = (angle - lower) / 5
        first = neutral[INDEX] if lower == 0 else actual[index_shape(lower)][INDEX]
        last = actual[index_shape(upper)][INDEX]
        points = [a.lerp(b, fraction) for a, b in zip(first, last)]
        rotation = Quaternion(hinge, math.radians(angle))
        exact = [root + rotation @ (point - root) for point in neutral[INDEX]]
        interpolation_error = max(interpolation_error, max((a - b).length for a, b in zip(points, exact)))
        lowest_surface_clearance = min(lowest_surface_clearance, contact_depth - max(map(depth, points)))
    if interpolation_error > .0001 * scale or lowest_surface_clearance < -1e-6 * scale:
        raise RuntimeError("Index samples lose their finite arc or penetrate the contact plane")
    travel = contact_depth - max(map(depth, neutral[INDEX]))
    if travel < .020 * scale:
        raise RuntimeError("Index-only gesture has insufficient visible lift")
    # Source roots remain under the unchanged palm throughout the finger arc.
    palm = actual[FIST]["GEO_Hand.R"]
    palm_triangles = [tuple(palm[face.vertices[j]] for j in (0, i, i + 1))
                      for face in parts["GEO_Hand.R"].data.polygons for i in range(1, len(face.vertices) - 1)]
    root_gaps = {}
    for name in NAMES[1:]:
        source = neutral[name]
        # Match each original ring geometrically, independent of hull reordering.
        if name == "GEO_Thumb.R":
            before = next(before for label, before, _ in segments if label == name)
        else:
            index = int(name.split("Finger")[1][0])
            offset, length = ((-.023, .033), (-.007, .042), (.009, .039), (.024, .030))[index]
            _, direction, across, palmar = grip._frame(result, "R")
            start = wrist + (direction * .052 + across * offset) * 1.20 * scale
            end = wrist + (direction * (.052 + length) + across * offset + palmar * .004) * 1.20 * scale
            before = (start, end)
        axis = (before[-1] - before[0]).normalized()
        values = [(point - before[0]).dot(axis) for point in source]
        indices = [i for i, value in enumerate(values) if value - min(values) < 1e-6 * scale]
        shape = index_shape(60) if name == INDEX else FIST
        gap = min(grip._outside_surface_gap(actual[shape][name][i], palm_triangles) for i in indices)
        root_gaps[name] = round(gap, 8)
        if gap > .003 * scale:
            raise RuntimeError("Pointing digit separates from its original palm: " + name)
    return dict(contact_other_surface_clearance_m=round(clearance, 8), limiting_renderer=other_name,
                index_only_travel_m=round(travel, 8), maximum_arc_interpolation_error_m=round(interpolation_error, 9),
                maximum_link_length_error_m=round(segment_error, 9), minimum_index_contact_plane_clearance_m=round(lowest_surface_clearance, 9),
                closed_tip_root_chord_ratios={name: round((after[-1]-after[0]).length / (before[-1]-before[0]).length, 8)
                                             for name, before, after in segments},
                finger_root_surface_gaps_m=root_gaps, palm_unchanged=True, fixed_wrist=True,
                index_zero_in_fist=True, other_surfaces_zero_in_index_samples=True,
                closed_positive_volumes=True, neutral_weights=True)


def manifest(result, scale, grip):
    parts = {record.obj.name: record.obj for record in result.parts}
    if FIST not in parts[NAMES[0]].data.shape_keys.key_blocks:
        return None
    content = {name: {shape: [[round(float(c), 7) for c in point.co]
                             for point in parts[name].data.shape_keys.key_blocks[shape].data]
                      for shape in sorted(shape_names(name))} for name in NAMES}
    return dict(contract=CONTRACT, side="R", renderers=list(NAMES), fist_shape=FIST, index_renderer=INDEX,
                index_samples=[dict(angle_degrees=angle, shape_name=index_shape(angle)) for angle in ANGLES],
                contact_angle_degrees=60, interpolation="adjacent_linear_samples; zero degrees uses Basis",
                other_finger_joint_angles_degrees=list(FINGER_ANGLES),
                shape_signature=hashlib.sha256(json.dumps(content, sort_keys=True).encode()).hexdigest(),
                authoring_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                validation=validate(result, scale, grip))


def export_profile(result):
    parts = {record.obj.name: record.obj for record in result.parts}
    return {name: {shape: [parts[name].matrix_world @ vertex.co
                          for vertex in parts[name].data.shape_keys.key_blocks[shape].data]
                   for shape in sorted(shape_names(name))} for name in NAMES}


def validate_export(profile, objects):
    maximum = 0.
    for name, shapes in profile.items():
        obj = objects.get(name)
        keys = obj.data.shape_keys.key_blocks if obj is not None and obj.type == "MESH" and obj.data.shape_keys else None
        for shape, expected in shapes.items():
            if keys is None or shape not in keys:
                raise RuntimeError("Published hero FBX lost its hand gesture: " + name + ":" + shape)
            points = [obj.matrix_world @ vertex.co for vertex in keys[shape].data]
            def gap(first, second):
                tree = KDTree(len(second))
                for index, point in enumerate(second): tree.insert(point, index)
                tree.balance()
                return max(tree.find(point)[2] for point in first)
            maximum = max(maximum, gap(points, expected), gap(expected, points))
    if maximum > 1e-5:
        raise RuntimeError(f"Published hero FBX changes gesture geometry: {maximum:.9f} m")
    print(f"Hero FBX gesture round trip: all finite samples retained; maximum gap {maximum:.9f} m", flush=True)
