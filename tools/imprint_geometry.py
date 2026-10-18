"""Closed negative human casts on the retained NpcHumanV2 anatomical rig."""
from __future__ import annotations

import math

import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

import crane_mutant_geometry as kit

HEAD_Z = 1.610
HEAD_RX, HEAD_RZ = .106, .145
HEAD_RIM_Y = -.102
HEAD_CAST_PITCH_DEGREES = -25.


def volume(vertices, faces):
    return sum(Vector(vertices[f[0]]).dot(Vector(vertices[f[i]]).cross(Vector(vertices[f[i + 1]]))) / 6
               for f in faces for i in range(1, len(f) - 1))


def closed_disk_surface(front, back, radial=12, sides=48):
    """Two matched polar disks and a closed rim, with no intersecting anatomy."""
    vertices, faces, slots = [], [], []
    for function in (front, back):
        vertices.append(tuple(function(0., 0., 0.)))
        for row in range(1, radial + 1):
            radius = row / radial
            for side in range(sides):
                angle = side * math.tau / sides
                vertices.append(tuple(function(radius * math.cos(angle), radius * math.sin(angle), radius)))
    count = 1 + radial * sides
    for disk in range(2):
        offset = disk * count
        for side in range(sides):
            faces.append((offset, offset + 1 + side, offset + 1 + (side + 1) % sides))
            slots.append(0)
        for row in range(radial - 1):
            for side in range(sides):
                a = offset + 1 + row * sides + side
                b = offset + 1 + row * sides + (side + 1) % sides
                faces.append((a, a + sides, b + sides, b))
                slots.append(1 if disk == 0 and row >= radial - 2 else 0)
    start = 1 + (radial - 1) * sides
    middle_start = len(vertices)
    for side in range(sides):
        # Only the anterior 12 mm edge is glass; the rest remains pale chalk.
        front_point = Vector(vertices[start + side])
        back_point = Vector(vertices[start + side + count])
        depth = (back_point - front_point).length
        vertices.append(tuple(front_point.lerp(back_point, min(.012 / max(depth, 1e-6), .45))))
    for side in range(sides):
        a, b = start + side, start + (side + 1) % sides
        ma, mb = middle_start + side, middle_start + (side + 1) % sides
        faces.append((a, ma, mb, b))
        slots.append(1)
        faces.append((ma, a + count, b + count, mb))
        slots.append(0)
    # Reverse the back disk relative to the front, then choose global winding.
    disk_faces = sides + (radial - 1) * sides
    for i in range(disk_faces, disk_faces * 2):
        faces[i] = tuple(reversed(faces[i]))
    if volume(vertices, faces) < 0:
        faces = [tuple(reversed(f)) for f in faces]
    return vertices, faces, slots


def negative_face(x, z):
    """Positive values go INTO the head, away from its anterior rim."""
    nose = .083 * math.exp(-((x / .018) ** 2 + ((z - 1.607) / .026) ** 2))
    bridge = .028 * math.exp(-((x / .012) ** 2 + ((z - 1.642) / .037) ** 2))
    forehead = .044 * math.exp(-((x / .070) ** 2 + ((z - 1.692) / .038) ** 2))
    lips = .049 * math.exp(-((x / .039) ** 4 + ((z - 1.556) / .010) ** 2))
    eyes = sum(.016 * math.exp(-(((x - sign * .038) / .021) ** 2 + ((z - 1.650) / .014) ** 2))
               for sign in (-1, 1))
    chin = .016 * math.exp(-((x / .046) ** 2 + ((z - 1.512) / .024) ** 2))
    return nose + bridge + forehead + lips + eyes + chin


def head_front(u, v, radius):
    x, z = u * HEAD_RX, HEAD_Z + v * HEAD_RZ
    # The boundary is the foremost support. Features never protrude past it.
    inset = (.023 * (1 - radius * radius) + negative_face(x, z)) * (1 - radius ** 12)
    return x, HEAD_RIM_Y + inset, z


def head_back(u, v, radius):
    return u * HEAD_RX, .050 + .065 * (1 - radius * radius), HEAD_Z + v * HEAD_RZ


def set_roles(obj, slots, roles=('chalk', 'glass')):
    obj['imprint_surface_roles'] = list(roles)
    for polygon, slot in zip(obj.data.polygons, slots):
        polygon.material_index = slot
        polygon.use_smooth = True


def preserve_palm_shapes(obj, old_vertices, old_shapes, original_order):
    """Interpolate the original palm's grip deltas onto the new closed cast."""
    for name in original_order:
        key = obj.shape_key_add(name=name)
        if name == 'Basis':
            continue
        deltas = old_shapes[name]
        for target, vertex in zip(key.data, obj.data.vertices):
            closest = sorted(range(len(old_vertices)), key=lambda i: (old_vertices[i] - vertex.co).length_squared)[:4]
            weights = [1. / max((old_vertices[i] - vertex.co).length_squared, 1e-9) for i in closest]
            total = sum(weights)
            delta = sum((deltas[i] * weight for i, weight in zip(closest, weights)), Vector()) / total
            target.co = vertex.co + delta


def palm_cast(builder, side):
    rig = builder.result.rig
    obj = bpy.data.objects['GEO_HandPalm.' + side]
    original = [vertex.co.copy() for vertex in obj.data.vertices]
    keys = obj.data.shape_keys.key_blocks
    order = [key.name for key in keys]
    shapes = {key.name: [point.co - basis.co for point, basis in zip(key.data, keys[0].data)] for key in keys[1:]}
    if 'CylindricalGrip' not in order:
        raise ValueError('Imprint palm lost its production grip shape')
    bone = rig.data.bones['hand.' + side]
    wrist = rig.matrix_world @ bone.head_local
    fingers = (rig.matrix_world.to_3x3() @ (bone.tail_local - bone.head_local)).normalized()
    across = (Vector((0, 1, 0)) - fingers * fingers.y).normalized()
    normal = fingers.cross(across).normalized() * (-1 if side == 'L' else 1)
    points = [obj.matrix_world @ vertex for vertex in original]
    coordinates = [((p - wrist).dot(across), (p - wrist).dot(fingers), (p - wrist).dot(normal)) for p in points]
    low = [min(p[i] for p in coordinates) for i in range(3)]
    high = [max(p[i] for p in coordinates) for i in range(3)]
    centre = wrist + across * ((low[0] + high[0]) * .5) + fingers * ((low[1] + high[1]) * .5)
    half_width, half_length = (high[0] - low[0]) * .5, (high[1] - low[1]) * .5
    depth = min(.008, (high[2] - low[2]) * .35)
    def point(u, v, radius, front):
        shell = high[2] - depth * (1 - radius * radius) ** 2 if front else low[2]
        # Rounded rectangle keeps all four original finger roots embedded.
        u = math.copysign(abs(u) ** .40, u)
        v = math.copysign(abs(v) ** .40, v)
        return centre + across * (u * half_width) + fingers * (v * half_length) + normal * shell
    vertices, faces, slots = closed_disk_surface(lambda u, v, r: point(u, v, r, True),
        lambda u, v, r: point(u, v, r, False), 6, 24)
    result = kit.replace(builder, obj.name, (vertices, faces), {'hand.' + side: 1.})
    set_roles(result, slots)
    preserve_palm_shapes(result, original, shapes, order)
    origin = centre + normal * (high[2] + .002)
    return dict(name=result.name, recess_depth_m=depth, minimum_thickness_m=high[2] - low[2] - depth,
                original_grip_index=order.index('CylindricalGrip') - 1,
                probe=dict(name='palm_' + side, source_renderer=result.name,
                    origin=list(origin), direction=list(-normal), minimum_clear_distance_m=depth * .65))


def retained_body(builder):
    """Keep each production anatomical solid closed; author no new joint caps."""
    rig = builder.result.rig
    names = ('GEO_Torso', 'GEO_Neck', 'GEO_Arm.L', 'GEO_Arm.R', 'GEO_LegsUpper',
             'GEO_LegsLower.L', 'GEO_LegsLower.R', 'GEO_FootShoe.L', 'GEO_FootShoe.R')
    total_triangles = 0
    chest_probe = None
    sternum_depth = .080
    def human_form(point):
        x, y, z = point
        shoulder = math.exp(-((z - 1.35) / .15) ** 4)
        return Vector((x * (1. + .065 * shoulder),
                       y + .016 * shoulder * max(0., min(1., y / .08)),
                       z - .010 * math.exp(-(((x - .17) / .13) ** 2 + ((z - 1.37) / .10) ** 2))))
    for name in names:
        obj = bpy.data.objects[name]
        original = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        original_weights = [{obj.vertex_groups[g.group].name: g.weight for g in vertex.groups if g.weight > 0.}
                            for vertex in obj.data.vertices]
        tree = KDTree(len(original))
        for i, point in enumerate(original):
            tree.insert(point, i)
        tree.balance()
        editable = bmesh.new()
        editable.from_mesh(obj.data)
        bmesh.ops.triangulate(editable, faces=list(editable.faces))
        # Dense support is local to the negative sternum/ribs. Existing closed
        # human shoulders, elbows, pelvis and knees retain their authored shape.
        if name == 'GEO_Torso':
            edges = [edge for edge in editable.edges if all(
                (obj.matrix_world @ v.co).z > 1.08 and (obj.matrix_world @ v.co).y < .025 for v in edge.verts)]
            bmesh.ops.subdivide_edges(editable, edges=edges, cuts=4, use_grid_fill=True)
            bmesh.ops.triangulate(editable, faces=list(editable.faces))
        replacement = bpy.data.meshes.new('Imprint retained human solid')
        editable.to_mesh(replacement)
        editable.free()
        points = [human_form(obj.matrix_world @ vertex.co) for vertex in replacement.vertices]
        reference = [obj.matrix_world @ vertex.co for vertex in replacement.vertices]
        faces = [tuple(poly.vertices) for poly in replacement.polygons]
        bpy.data.meshes.remove(replacement)
        shoulder_rows = None
        if name.startswith('GEO_Arm'):
            # The production arm has nine anatomical rings. Its first two
            # rings were a tiny dome tailored for a shirt. Give this bare cast
            # a shoulder rooted inside the chest, without adding a new cap.
            if len(original) != 108 or len(points) != len(original):
                raise ValueError('Imprint retained arm ring topology changed')
            shoulder_rows = [tree.find(point)[1] // 12 for point in reference]
            centres = [sum(original[row * 12:(row + 1) * 12], Vector()) / 12 for row in range(3)]
        for point, neutral in zip(points, reference):
            if name == 'GEO_Torso':
                shoulder = math.exp(-((neutral.z - 1.373) / .085) ** 4)
                side = max(0., min(1., (abs(neutral.x) - .105) / .072))
                point.x += math.copysign(.039 * shoulder * side, neutral.x)
                if neutral.z < .99:
                    point.z -= .023 * max(0., min(1., (.99 - neutral.z) / .07))
            elif name.startswith('GEO_Arm'):
                bone = rig.data.bones['upper_arm.' + name[-1]]
                a, b = rig.matrix_world @ bone.head_local, rig.matrix_world @ bone.tail_local
                axis = (b - a).normalized()
                along = (neutral - a).dot(axis)
                blend = max(0., min(1., 1. - along / .11))
                radial = neutral - a - axis * along
                point -= axis * (.043 * blend)
                point += radial * (.17 * blend)
            elif name == 'GEO_LegsUpper' and neutral.z < .315:
                point.z -= .025 * max(0., min(1., (.315 - neutral.z) / .060))
            elif name.startswith('GEO_LegsLower'):
                blend = max(0., min(1., (neutral.z - .205) / .05))
                centre_x = rig.data.bones['shin.' + name[-1]].head_local.x
                point.x += (neutral.x - centre_x) * .18 * blend
                point.y += (neutral.y + .018) * .18 * blend
                point.z += .025 * blend
        if shoulder_rows is not None:
            sign = 1. if name.endswith('.L') else -1.
            for index, row in enumerate(shoulder_rows):
                if row > 1:
                    continue
                # A chest-bound inner ring and a blended outer shoulder keep
                # the same closed anatomical surface connected during a reach.
                offset = reference[index] - centres[row]
                direction = offset.normalized()
                target = Vector((sign * (.145 if row == 0 else .210), -.004,
                                 1.340 if row == 0 else 1.395))
                points[index] = target + direction * (.060 if row == 0 else .078)
        if name == 'GEO_Torso':
            pristine = BVHTree.FromPolygons(points, faces)
            centre, _, _, _ = pristine.ray_cast(Vector((0., -.35, 1.253)), Vector((0., 1., 0.)), .8)
            rims = [pristine.ray_cast(Vector((x, -.35, 1.253)), Vector((0., 1., 0.)), .8)[0]
                    for x in (-.10, .10)]
            if centre is None or any(point is None for point in rims):
                raise ValueError('Imprint retained chest has no closed front surface')
            rim_y = sum(point.y for point in rims) / 2
            sternum_depth = max(.075, rim_y + .060 - centre.y)
            if sternum_depth > .105:
                raise ValueError('Imprint sternum would require an excessive displacement')
            chest_probe = dict(name='chest', source_renderer=name,
                origin=[0., float(centre.y) - .002, 1.253], direction=[0., 1., 0.],
                minimum_clear_distance_m=sternum_depth * .80)
        weights = []
        for point in reference:
            row = {}
            for _, index, distance in tree.find_n(point, min(4, len(original))):
                influence = 1. / max(distance * distance, 1e-10)
                for bone, weight in original_weights[index].items():
                    row[bone] = row.get(bone, 0.) + weight * influence
            row = dict(sorted(row.items(), key=lambda item: (-item[1], item[0]))[:4])
            total = sum(row.values())
            weights.append({bone: weight / total for bone, weight in row.items()})
        if shoulder_rows is not None:
            for index, ring in enumerate(shoulder_rows):
                if ring == 0:
                    weights[index] = {'chest': 1.}
                elif ring == 1:
                    weights[index] = {'chest': .55, 'upper_arm.' + name[-1]: .45}
        for point in points:
            x, y, z = point
            if y < .010 and 1.105 < z < 1.385:
                front = max(0., min(1., (.010 - y) / .050))
                sternum = sternum_depth * math.exp(-((x / .071) ** 2 + ((z - 1.253) / .115) ** 4))
                ribs = sum((.021 if x > 0. else .016) * math.exp(-(((abs(x) - .078) / .048) ** 4 +
                            ((z - level - abs(x) * .10 - (.008 if x > 0. else -.004)) / .014) ** 2))
                           for level in (1.151, 1.199, 1.248, 1.299))
                point.y += (sternum + ribs) * front
        result = kit.replace(builder, name, (points, faces), {'pelvis': 1.})
        kit.skin(result, weights)
        set_roles(result, [0] * len(result.data.polygons), ('chalk',))
        total_triangles += sum(len(poly.vertices) - 2 for poly in result.data.polygons)
    if total_triangles > 7800:
        raise ValueError('Retained Imprint anatomy exceeds body triangle cap: ' + str(total_triangles))
    return dict(source='retained_worker_closed_anatomy', new_internal_caps=False,
                common_human_field=True, original_semantic_regions=True,
                body_triangle_limit=7800, body_triangles=total_triangles,
                sternum_surface_displacement_m=sternum_depth,
                target_sternum_recess_relative_rim_m=.060, chest_probe=chest_probe)


def sculpt(builder):
    rig = builder.result.rig
    bones = {b.name: (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones}
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and not obj.name.startswith('GEO_'):
            bpy.data.objects.remove(obj, do_unlink=True)

    vertices, faces, slots = closed_disk_surface(head_front, head_back)
    head_pivot = rig.matrix_world @ rig.data.bones['head'].head_local
    head_rotation = Matrix.Rotation(math.radians(HEAD_CAST_PITCH_DEGREES), 3, 'X')
    def rotate_head(point):
        return head_pivot + head_rotation @ (Vector(point) - head_pivot)
    vertices = [tuple(rotate_head(point)) for point in vertices]
    head = kit.replace(builder, 'GEO_Head', (vertices, faces), {'head': 1.})
    set_roles(head, slots)
    # Hidden semantic stubs stay inside the actual closed head, so neither the
    # old face nor old ear anatomy can spoil the negative volume or its tissue.
    for name in ('GEO_FaceSurface', 'GEO_Ear.L', 'GEO_Ear.R', 'GEO_EarFold.L', 'GEO_EarFold.R'):
        points, polygons = kit.shell([(1.603, 0., .075, .003, .003),
            (1.610, 0., .075, .004, .004)], 8)
        stub = kit.replace(builder, name, ([tuple(rotate_head(p)) for p in points], polygons), {'head': 1.})
        set_roles(stub, [0] * len(stub.data.polygons), ('flesh',))

    body = retained_body(builder)
    palms = [palm_cast(builder, side) for side in ('L', 'R')]
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not obj.name.startswith('GEO_') or 'imprint_surface_roles' in obj:
            continue
        set_roles(obj, [0] * len(obj.data.polygons), ('chalk',))

    # Numerical source geometry checks name the actual concavity, not a render.
    nose_y = head_front(0., (1.607 - HEAD_Z) / HEAD_RZ, abs((1.607 - HEAD_Z) / HEAD_RZ))[1]
    lip_y = head_front(0., (1.556 - HEAD_Z) / HEAD_RZ, abs((1.556 - HEAD_Z) / HEAD_RZ))[1]
    forehead_y = head_front(0., (1.692 - HEAD_Z) / HEAD_RZ, abs((1.692 - HEAD_Z) / HEAD_RZ))[1]
    return dict(probes_space='blender_metres', front_axis_blender=list(head_rotation @ Vector((0, -1, 0))),
        head_face_center_blender=list(rotate_head((0., HEAD_RIM_Y + .025, HEAD_Z))),
        head_cast_pivot_blender=list(head_pivot), head_cast_pitch_degrees=HEAD_CAST_PITCH_DEGREES,
        facial_measurements_space='unrotated_face_frame_metres', head_rim_y_m=HEAD_RIM_Y,
        nose_y_m=nose_y, lips_y_m=lip_y, forehead_y_m=forehead_y,
        nose_recess_m=nose_y - HEAD_RIM_Y, lips_recess_m=lip_y - HEAD_RIM_Y,
        forehead_recess_m=forehead_y - HEAD_RIM_Y,
        minimum_head_front_to_back_m=.070, chest_maximum_recess_m=body['sternum_surface_displacement_m'] + .022, body=body,
        palms=palms, closed_source_volumes=True, source_bound_tissue_depth_m=.003,
        negative_probes=[dict(name=label, source_renderer='GEO_Head',
                             origin=list(rotate_head((0., HEAD_RIM_Y - .002, z))),
                             direction=list(head_rotation @ Vector((0., 1., 0.))), minimum_clear_distance_m=minimum)
                         for label, z, minimum in (('nose', 1.607, .060), ('lips', 1.556, .035), ('forehead', 1.692, .025))]
                        + [body['chest_probe']]
                        + [palm['probe'] for palm in palms])
