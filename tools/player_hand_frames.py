"""Geometry-derived hand anatomy for the production hero's authored poses.

The thumb names the radial edge; the hand bone names the fingers. Their cross
product names the actual palm with anatomical handedness. Bone roll, camera
axes and the old mitten's broad face are not anatomical evidence.
"""
from __future__ import annotations

from dataclasses import replace
import math

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Quaternion, Vector


def bind_frame(result, side):
    """Return fingers, radial/thumb edge and palm normal in rig bind space."""
    rows = {part.obj.name: part.obj for part in result.parts}
    rig_inverse = result.rig.matrix_world.inverted()

    def center(name):
        obj = rows[name]
        transform = rig_inverse @ obj.matrix_world
        return sum((transform @ vertex.co for vertex in obj.data.vertices), Vector()) / len(obj.data.vertices)

    bone = result.rig.data.bones["hand." + side]
    fingers = (bone.tail_local - bone.head_local).normalized()
    radial = center("GEO_Thumb." + side) - center("GEO_Hand." + side)
    radial = (radial - fingers * radial.dot(fingers)).normalized()
    palm = fingers.cross(radial).normalized() * (1. if side == "L" else -1.)
    return fingers, radial, palm


def posed_frame(result, side, frame=None):
    rig = result.rig
    bone = rig.pose.bones["hand." + side]
    skin = (bone.matrix @ bone.bone.matrix_local.inverted()).to_3x3()
    return tuple((skin @ vector).normalized() for vector in (frame or bind_frame(result, side)))


def face_surface(builder, pose, side, normal):
    """Roll the hand onto its contact without moving wrist, fingers or grip centers.

    The common BonePose already stores local rotations; convert the measured
    result back into that existing format instead of extending the rig format
    or rotating the imported rest bones and every unrelated action.
    """
    rig = builder.result.rig
    name = "hand." + side
    hand = rig.pose.bones[name]
    fingers, _, palm = posed_frame(builder.result, side)
    target = Vector(normal)
    target -= fingers * target.dot(fingers)
    if target.length_squared < 1e-10:
        return
    target.normalize()
    angle = math.atan2(fingers.dot(palm.cross(target)), palm.dot(target))
    rotation = Quaternion(fingers, angle) @ hand.matrix.to_quaternion()
    hand.matrix = Matrix.Translation(hand.head.copy()) @ rotation.to_matrix().to_4x4()
    bpy.context.view_layer.update()
    local = hand.rotation_quaternion.to_euler("XYZ")
    pose[name] = replace(pose[name], rotation_degrees=tuple(math.degrees(value) for value in local),
                         armature_direction=None, target_direction=None)


def validate_neutral(result, errors):
    """A source-frame proof: relaxed hands have thumbs forward and palms inward."""
    record = result.actions.get("Relaxed")
    if record is None:
        errors.append("The hand-frame check requires the authored Relaxed pose")
        return
    rig = result.rig
    animation = rig.animation_data_create()
    previous_action = animation.action
    scene = bpy.context.scene
    previous_frame, previous_subframe = scene.frame_current, scene.frame_subframe
    previous_basis = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    try:
        frames = {side: bind_frame(result, side) for side in ("L", "R")}
        animation.action = record.action
        scene.frame_set(0)
        bpy.context.view_layer.update()
        for side, sign in (("L", 1.), ("R", -1.)):
            fingers, radial, palm = posed_frame(result, side, frames[side])
            if radial.y > -.75 or palm.x * sign > -.75 or fingers.z > -.80:
                errors.append("Relaxed " + side + " hand needs thumbs forward, palms inward, fingers down: " +
                              repr({"radial": tuple(round(v, 4) for v in radial),
                                    "palm": tuple(round(v, 4) for v in palm),
                                    "fingers": tuple(round(v, 4) for v in fingers)}))
    finally:
        animation.action = previous_action
        scene.frame_set(previous_frame, subframe=previous_subframe)
        for name, matrix in previous_basis.items():
            rig.pose.bones[name].matrix_basis = matrix
        bpy.context.view_layer.update()


def convex_hull_geometry(vertices):
    """Retriangulate the original extreme points with deterministic indexing.

    BMesh's hull quantizes nearly collinear profile rings. Use a double precision
    incremental hull to choose support faces, then BMesh for the manifold mesh.
    The source coordinates are retained exactly; no outward offset hides contact.
    """
    coordinates = sorted(set(tuple(vertex) for vertex in vertices))
    cloud = np.asarray(coordinates, dtype=np.float64)
    # Profile interpolation happens before Blender stores float32 vertices.
    # Drop sub-micron collinear midpoints so rigid skinning cannot turn a zero
    # area sliver into an arbitrary SAT plane. Keep every axis support extreme.
    pair_a, pair_b = np.triu_indices(len(cloud), 1)
    starts, segments = cloud[pair_a], cloud[pair_b] - cloud[pair_a]
    squared_lengths = np.einsum("ij,ij->i", segments, segments)
    keep = []
    minimum, maximum = cloud.min(axis=0), cloud.max(axis=0)
    for index, point in enumerate(cloud):
        if np.any(point == minimum) or np.any(point == maximum):
            keep.append(index)
            continue
        fraction = np.einsum("ij,ij->i", point - starts, segments) / squared_lengths
        distances = np.linalg.norm(point - starts - fraction[:, None] * segments, axis=1)
        if not np.any((fraction > 1e-5) & (fraction < 1. - 1e-5) & (distances < 1e-7)):
            keep.append(index)
    coordinates = [coordinates[index] for index in keep]
    cloud = cloud[keep]
    first = 0
    second = int(np.argmax(np.linalg.norm(cloud - cloud[first], axis=1)))
    line = cloud[second] - cloud[first]
    third = int(np.argmax(np.linalg.norm(np.cross(cloud - cloud[first], line), axis=1)))
    normal = np.cross(line, cloud[third] - cloud[first])
    normal /= np.linalg.norm(normal)
    fourth = int(np.argmax(np.abs((cloud - cloud[first]) @ normal)))
    initial = (first, second, third, fourth)
    inside = cloud[list(initial)].mean(axis=0)

    def outward(a, b, c):
        normal = np.cross(cloud[b] - cloud[a], cloud[c] - cloud[a])
        return (a, c, b) if np.dot(inside - cloud[a], normal) > 0. else (a, b, c)

    faces = [outward(first, second, third), outward(first, fourth, second),
             outward(first, third, fourth), outward(second, fourth, third)]
    # Numerical zero only: excluding a merely near-coplanar visible face can
    # split the horizon and leave duplicate wedges on a profiled ring.
    epsilon = 1e-12
    for index, point in enumerate(cloud):
        if index in initial:
            continue
        visible = []
        for face in faces:
            a, b, c = cloud[list(face)]
            normal = np.cross(b - a, c - a)
            if np.dot(point - a, normal) > epsilon * np.linalg.norm(normal):
                visible.append(face)
        if not visible:
            continue
        boundary = {}
        for face in visible:
            for a, b in zip(face, face[1:] + face[:1]):
                edge = tuple(sorted((a, b)))
                if edge in boundary:
                    del boundary[edge]
                else:
                    boundary[edge] = (a, b)
        visible_set = set(visible)
        faces = [face for face in faces if face not in visible_set]
        faces.extend(outward(a, b, index) for a, b in sorted(boundary.values()))

    mesh = bmesh.new()
    try:
        hull_indices = sorted({index for face in faces for index in face})
        hull_vertices = {index: mesh.verts.new(coordinates[index]) for index in hull_indices}
        for face in faces:
            mesh.faces.new([hull_vertices[index] for index in face])
        bmesh.ops.recalc_face_normals(mesh, faces=list(mesh.faces))
        ordered = sorted(mesh.verts, key=lambda vertex: tuple(vertex.co))
        indices = {vertex: index for index, vertex in enumerate(ordered)}
        points = [vertex.co.copy() for vertex in ordered]
        faces = []
        for face in mesh.faces:
            cycle = tuple(indices[vertex] for vertex in face.verts)
            start = cycle.index(min(cycle))
            faces.append(cycle[start:] + cycle[:start])
        return points, sorted(faces)
    finally:
        mesh.free()


def _repair_rigid_contact_mesh(obj):
    mesh = obj.data
    original = [vertex.co.copy() for vertex in mesh.vertices]
    weights = {tuple(vertex.co): tuple((group.group, group.weight) for group in vertex.groups)
               for vertex in mesh.vertices}
    group_ids = {groups[0][0] for groups in weights.values() if len(groups) == 1}
    if len(group_ids) != 1 or any(len(groups) != 1 or abs(groups[0][1] - 1.) > 1e-6
                                  for groups in weights.values()):
        raise RuntimeError(obj.name + " hull repair requires one normalized rigid skin influence")
    group_name = obj.vertex_groups[next(iter(group_ids))].name
    source_faces = []
    for polygon in mesh.polygons:
        coordinates = [tuple(mesh.vertices[mesh.loops[i].vertex_index].co) for i in polygon.loop_indices]
        uvs = {layer.name: {coordinate: tuple(layer.data[loop].uv)
                           for coordinate, loop in zip(coordinates, polygon.loop_indices)}
               for layer in mesh.uv_layers}
        source_faces.append((set(coordinates), polygon.normal.copy(), polygon.material_index,
                             polygon.use_smooth, uvs))
    points, faces = convex_hull_geometry(original)
    for axis in range(3):
        if (abs(min(point[axis] for point in points) - min(point[axis] for point in original)) > 1e-8 or
                abs(max(point[axis] for point in points) - max(point[axis] for point in original)) > 1e-8):
            raise RuntimeError(obj.name + " convex repair changed its authored bounds")
    layer_names = [layer.name for layer in mesh.uv_layers]
    mesh.clear_geometry()
    mesh.from_pydata(points, [], faces)
    mesh.update()
    for name in layer_names:
        if mesh.uv_layers.get(name) is None:
            mesh.uv_layers.new(name=name)
    group = obj.vertex_groups.get(group_name) or obj.vertex_groups.new(name=group_name)
    group.add(list(range(len(points))), 1., "REPLACE")
    for polygon in mesh.polygons:
        coordinates = {tuple(mesh.vertices[index].co) for index in polygon.vertices}
        source = max(source_faces, key=lambda row: (len(row[0] & coordinates), row[1].dot(polygon.normal)))
        polygon.material_index, polygon.use_smooth = source[2], source[3]
        for loop_index in polygon.loop_indices:
            coordinate = tuple(mesh.vertices[mesh.loops[loop_index].vertex_index].co)
            owner = source if coordinate in source[0] else max(
                (row for row in source_faces if coordinate in row[0]),
                key=lambda row: (len(row[0] & coordinates), row[1].dot(polygon.normal)))
            for name in layer_names:
                mesh.uv_layers[name].data[loop_index].uv = owner[4][name][coordinate]
    obj["bp_contact_convex_hull"] = True
    mesh.update()


def ensure_contact_convexity(builder):
    """Fix only offending rigid contact meshes before the expensive action bank."""
    import player_cold_clearance
    objects = {part.obj.name: part.obj for part in builder.result.parts}
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for name in player_cold_clearance.LEFT_PARTS + player_cold_clearance.RIGHT_PARTS:
        obj = objects.get(name)
        if obj is None:
            raise RuntimeError("Missing authored arm contact mesh " + name)
        try:
            player_cold_clearance.evaluated_volume(obj, depsgraph, check_convex=True)
        except RuntimeError as error:
            if "is not convex" not in str(error):
                raise
            _repair_rigid_contact_mesh(obj)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            player_cold_clearance.evaluated_volume(obj, depsgraph, check_convex=True)


def validate_contact_convexity(result, errors):
    """Run the exact SAT precondition on every registered left/right arm surface."""
    import player_cold_clearance
    objects = {part.obj.name: part.obj for part in result.parts}
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for name in player_cold_clearance.LEFT_PARTS + player_cold_clearance.RIGHT_PARTS:
        obj = objects.get(name)
        if obj is None:
            errors.append("Missing authored arm contact mesh " + name)
            continue
        try:
            player_cold_clearance.evaluated_volume(obj, depsgraph, check_convex=True)
        except RuntimeError as error:
            errors.append(str(error))
