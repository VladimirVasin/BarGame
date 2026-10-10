"""Apply the shared deforming-joint contract to the legacy NPC substrates.

The pass runs after a design's named-part replacements. It retains renderer,
material, rig and identity ownership, adding a bounded transition station only
to the old two-ring limb solids. Detail UVs are interpolated from the existing
source surface instead of remapped to another atlas region.
"""
from __future__ import annotations

from bisect import bisect_right
import math

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

import character_joint_surfaces as joints
import npc_detail_geometry as detail


def _world_points(obj):
    return [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]


def _loft_rings(obj):
    """Recognise only the generator's contiguous, closed ring loft format."""
    mesh = obj.data
    caps = [p for p in mesh.polygons if len(p.vertices) > 4]
    if len(caps) != 2 or len(caps[0].vertices) != len(caps[1].vertices):
        return None
    sides = len(caps[0].vertices)
    if len(mesh.vertices) % sides or len(mesh.vertices) < sides * 2:
        return None
    count = len(mesh.vertices) // sides
    expected = {frozenset(range(sides)), frozenset(range((count - 1) * sides, count * sides))}
    if {frozenset(p.vertices) for p in caps} != expected:
        return None
    if any(len(p.vertices) != 4 for p in mesh.polygons if p not in caps):
        return None
    return [list(range(row * sides, (row + 1) * sides)) for row in range(count)]


def _rounded_rings(obj):
    """Retain an ellipsoid's source stations while opening its limb pole."""
    mesh = obj.data
    sides = sum(len(p.vertices) == 3 and 0 in p.vertices for p in mesh.polygons)
    if sides < 6 or (len(mesh.vertices) - 2) % sides:
        return None
    points = _world_points(obj)
    rings = [points[start:start + sides] for start in range(1, len(points) - 1, sides)]
    rig = obj.find_armature()
    bone = rig.data.bones[str(obj.get("bp_bone"))]
    direction = (bone.tail_local - bone.head_local).normalized()
    poles = [points[0], points[-1]]
    if poles[0].dot(direction) > poles[1].dot(direction):
        rings.reverse(); poles.reverse()
    ends = []
    for ring, pole in ((rings[0], poles[0]), (rings[-1], poles[1])):
        centre = sum(ring, Vector()) / len(ring)
        ends.append([pole + (point - centre) * .08 for point in ring])
    return [ends[0], *rings[1:-1], ends[1]]


def _replace_loft(obj, target_sides, *, transition_endpoint=None, band_m=.07):
    rings = _loft_rings(obj)
    old = obj.data
    points = _world_points(obj)
    source_rings = ([[points[index] for index in ring] for ring in rings]
                    if rings is not None else _rounded_rings(obj))
    if source_rings is None:
        return False
    centres = [sum(ring, Vector()) / len(ring) for ring in source_rings]
    distances = [0.]
    for a, b in zip(centres, centres[1:]):
        distances.append(distances[-1] + (b - a).length)
    if distances[-1] <= 1e-6:
        return False
    stations = list(distances)
    # Detailed profiles already carry their anatomical stations. Only the
    # two-ring cylinders need a row local to the bend, not along the whole arm.
    if len(source_rings) == 2 and transition_endpoint is not None:
        station = min(band_m, distances[-1] * .25)
        if transition_endpoint == "end":
            station = distances[-1] - station
        stations.append(station)
        stations.sort()
    if rings is not None and target_sides == len(rings[0]) and stations == distances:
        return False
    old.calc_loop_triangles()
    triangles = [tuple(triangle.vertices) for triangle in old.loop_triangles]
    tree = BVHTree.FromPolygons(points, triangles, all_triangles=True)
    source_uvs = {}
    for layer in old.uv_layers:
        source_uvs[layer.name] = [[layer.data[index].uv.copy() for index in triangle.loops]
                                 for triangle in old.loop_triangles]
    result_rings = []
    sides = len(source_rings[0])
    for station in stations:
        row = max(0, min(len(distances) - 2, bisect_right(distances, station) - 1))
        fraction = (station - distances[row]) / (distances[row + 1] - distances[row])
        ring = []
        for side in range(target_sides):
            index = side * sides / target_sides
            left = math.floor(index) % sides
            right = (left + 1) % sides
            angular = index - math.floor(index)
            a = source_rings[row][left].lerp(source_rings[row][right], angular)
            b = source_rings[row + 1][left].lerp(source_rings[row + 1][right], angular)
            ring.append(a.lerp(b, fraction))
        result_rings.append(ring)
    vertices, faces = detail.loft(result_rings)
    inverse = obj.matrix_world.inverted()
    mesh = bpy.data.meshes.new(old.name + "_Joints")
    mesh.from_pydata([inverse @ point for point in vertices], [], faces)
    mesh.update(calc_edges=True)
    for material in old.materials:
        mesh.materials.append(material)
    smooth = any(p.use_smooth for p in old.polygons)
    for polygon in mesh.polygons:
        polygon.use_smooth = smooth
    for name, uv_triangles in source_uvs.items():
        layer = mesh.uv_layers.new(name=name)
        for loop in mesh.loops:
            point = vertices[loop.vertex_index]
            nearest, _, triangle_index, _ = tree.find_nearest(point)
            triangle = triangles[triangle_index]
            uv = barycentric_transform(nearest, *[points[i] for i in triangle],
                                       *[Vector((p.x, p.y, 0)) for p in uv_triangles[triangle_index]])
            layer.data[loop.index].uv = (uv.x, uv.y)
    obj.data = mesh
    bone = str(obj.get("bp_bone", ""))
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)
    group = obj.vertex_groups.new(name=bone)
    group.add(range(len(mesh.vertices)), 1., "REPLACE")
    if old.users == 0:
        bpy.data.meshes.remove(old)
    return True


def _find_part(parts, names):
    return next((parts[name] for name in names if name in parts), None)


def apply(result, triangle_budget=None):
    """Migrate ordinary anatomical motion without touching signature rigs."""
    if getattr(result, "joint_surfaces", None):
        return result.joint_surfaces
    bpy.context.view_layer.update()
    parts = {part.obj.name: part for part in result.parts}
    # Place extra stations symmetrically when a mechanism already occupies
    # most of the established art budget; exact seams still cover every hinge.
    current_triangles = sum(len(p.vertices) - 2 for part in result.parts for p in part.obj.data.polygons)
    potential = sum(2 * len(rings[0]) for part in result.parts
                    if (rings := _loft_rings(part.obj)) is not None and len(rings) == 2
                    and part.bone.split(".")[0] in {"upper_arm", "forearm", "thigh", "shin"})
    sparse_bands = triangle_budget is not None and triangle_budget[1] - current_triangles < potential
    upper_cost = sum(2 * len(rings[0]) for part in result.parts
                     if part.bone.startswith("upper_arm.") and (rings := _loft_rings(part.obj)) is not None and len(rings) == 2)
    sparse_stations = triangle_budget is None or triangle_budget[1] - current_triangles >= upper_cost
    for side in ("L", "R"):
        pairs = (
            ("elbow", "upper_arm", "forearm",
             (f"GEO_UpperArm.{side}", f"CLO_SleeveUpper.{side}", f"CLO_Sleeve.{side}"),
             (f"GEO_Forearm.{side}", f"CLO_SleeveLower.{side}"), .07),
            ("knee", "thigh", "shin",
             (f"GEO_Thigh.{side}", f"CLO_Thigh.{side}", f"CLO_Hose.{side}", f"CLO_TrouserUpper.{side}"),
             (f"GEO_Shin.{side}", f"CLO_Shin.{side}", f"CLO_Stocking.{side}", f"CLO_BootShaft.{side}"), .08),
        )
        for label, upper_bone, lower_bone, upper_names, lower_names, band in pairs:
            upper, lower = _find_part(parts, upper_names), _find_part(parts, lower_names)
            if upper is None or lower is None:
                continue
            upper_rings, lower_rings = _loft_rings(upper.obj), _loft_rings(lower.obj)
            upper_source = upper_rings if upper_rings is not None else _rounded_rings(upper.obj)
            lower_source = lower_rings if lower_rings is not None else _rounded_rings(lower.obj)
            if upper_source is None or lower_source is None:
                raise RuntimeError(f"{label}.{side} needs declared source loft rings")
            sides = max(len(upper_source[0]), len(lower_source[0]))
            _replace_loft(upper.obj, sides, transition_endpoint="end" if not sparse_bands or (label == "elbow" and sparse_stations) else None, band_m=band)
            _replace_loft(lower.obj, sides, transition_endpoint=None if sparse_bands else "start", band_m=band)
            upper_rings, lower_rings = _loft_rings(upper.obj), _loft_rings(lower.obj)
            joints.join_ring_seam(result, upper.obj, upper_rings[-1], lower.obj, lower_rings[0],
                                  upper_bone + "." + side, lower_bone + "." + side,
                                  name=label + "." + side, band_m=band,
                                  loose=False, corrective_name="JointVolume." + label.capitalize() + "." + side)
        upper = _find_part(parts, (f"GEO_UpperArm.{side}", f"CLO_SleeveUpper.{side}", f"CLO_Sleeve.{side}"))
        if upper is not None:
            joints.blend_attachment(result, upper.obj, "upper_arm." + side, "chest",
                                    name="shoulder." + side, band_m=.08, endpoint="start")
        thigh = _find_part(parts, (f"GEO_Thigh.{side}", f"CLO_Thigh.{side}", f"CLO_Hose.{side}", f"CLO_TrouserUpper.{side}"))
        if thigh is not None:
            joints.blend_attachment(result, thigh.obj, "thigh." + side, "pelvis",
                                    name="hip." + side, band_m=.09, endpoint="start")
        hand = _find_part(parts, (f"GEO_Hand.{side}",))
        if hand is not None:
            joints.blend_attachment(result, hand.obj, "hand." + side, "forearm." + side,
                                    name="wrist." + side, band_m=.025, endpoint="start")
        # Keep the shoe's authored sole/instep. The proximal shaft follows the
        # shin while the foot keeps its existing grounded extent.
        foot = _find_part(parts, (f"GEO_Foot.{side}", f"GEO_Shoe.{side}", f"GEO_Boot.{side}", f"GEO_Slipper.{side}"))
        if foot is not None:
            joints.blend_attachment(result, foot.obj, "foot." + side, "shin." + side,
                                    name="ankle." + side, band_m=.04, endpoint="start")
    neck = _find_part(parts, ("GEO_Neck", "GEO_NeckStub", "GEO_NeckBase"))
    if neck is not None:
        joints.blend_attachment(result, neck.obj, "neck", "chest",
                                name="neck.base", band_m=.035, endpoint="start")
        joints.blend_attachment(result, neck.obj, "neck", "head",
                                name="neck.head", band_m=.03, endpoint="end")
    return getattr(result, "joint_surfaces", None)


def validate_weights(obj, bone, errors):
    """Rigid accessory weights remain mandatory; joint surfaces are bounded."""
    declared = bool(obj.get("bp_joint_surface", False))
    if not declared:
        if len(obj.vertex_groups) != 1 or obj.vertex_groups[0].name != bone:
            errors.append(f"{obj.name} must have one rigid group for {bone}")
    allowed = {b.name for b in obj.find_armature().data.bones} if declared else {bone}
    for vertex in obj.data.vertices:
        weights = [group for group in vertex.groups if group.weight > .000001]
        if (not weights or len(weights) > 4 or abs(sum(group.weight for group in weights) - 1.) > .00001
                or any(obj.vertex_groups[group.group].name not in allowed for group in weights)
                or (not declared and (len(weights) != 1 or abs(weights[0].weight - 1.) > .000001))):
            errors.append(f"{obj.name} vertex {vertex.index} has invalid skin weights")
            break


def correct_continuous(result):
    """Keep the current wardrobe topology and correct every blended hinge."""
    for part in result.parts:
        obj = part.obj
        groups = {group.name for group in obj.vertex_groups}
        for side in ("L", "R"):
            for label, proximal, distal, band in (("Elbow", "upper_arm", "forearm", .10),
                                                  ("Knee", "thigh", "shin", .12)):
                a, b = proximal + "." + side, distal + "." + side
                if a not in groups or b not in groups:
                    continue
                indices = {group.name: group.index for group in obj.vertex_groups}
                if not any({group.group for group in vertex.groups if group.weight > 1e-6}.issuperset({indices[a], indices[b]})
                           for vertex in obj.data.vertices):
                    continue
                joints.correct_continuous_joint(result, obj, a, b,
                                                name=label.lower() + "." + side + ":" + obj.name,
                                                band_m=band, corrective_name="JointVolume." + label + "." + side)
    return joints.manifest(result)
