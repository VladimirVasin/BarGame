"""One opaque human volume carrying three physically fused motion phases."""
from __future__ import annotations

import math

import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

import crane_mutant_geometry as kit

HEAD_Z = 1.610
HEAD_CAST_PITCH_DEGREES = -23.
HIDDEN = ('GEO_Ear', 'GEO_EarFold', 'GEO_FaceSurface')
PHASES = ((0., 0., 0.), (-.070, .136, -.036), (-.140, .272, -.072))


def volume(vertices, faces):
    return sum(Vector(vertices[f[0]]).dot(Vector(vertices[f[i]]).cross(Vector(vertices[f[i + 1]]))) / 6
               for f in faces for i in range(1, len(f) - 1))


def smooth(value):
    value = max(0., min(1., value))
    return value * value * (3. - 2. * value)


def set_roles(obj, slots, roles=('emulsion', 'residue', 'flesh')):
    obj['long_exposure_surface_roles'] = list(roles)
    for polygon, slot in zip(obj.data.polygons, slots):
        polygon.material_index = slot
        polygon.use_smooth = True


def torso_envelope(point):
    """Star-shaped union of three overlapping elliptical motion sections.

    The original surface parametrizes one closed envelope. No copied bodies,
    interior faces, semantic splits, thin blades or runtime trails are created.
    """
    x, y, z = point
    radius_x, radius_y = .180, .102
    radial = Vector((x / radius_x, (y + .005) / radius_y))
    length = radial.length
    if length < 1e-7:
        return point.copy()
    direction = radial / length
    influence = smooth((z - .89) / .34) * (1. - .82 * smooth((z - 1.365) / .065))
    exits = []
    for phase, (dx, dy, dz) in enumerate(PHASES):
        # Later positions end slightly lower. Their overlapping shoulders form
        # long low contour ridges rather than repeated heads or separate limbs.
        taper = 1. - phase * .025 - phase * .33 * smooth((z - 1.320) / .10)
        centre = Vector((dx * influence / radius_x, dy * influence / radius_y))
        projection = centre.dot(direction)
        discriminant = taper * taper - centre.length_squared + projection * projection
        if discriminant >= 0.:
            # A distant phase first meets a ray tangentially. Ease it into the
            # existing mass so its onset cannot jump into a thin radial knife.
            exit = projection + math.sqrt(discriminant)
            blend = smooth(discriminant / max(taper * taper * .45, 1e-6))
            exits.append(1. + (exit - 1.) * blend)
    scale = max(exits)
    # The three low diagonal contours lead into the much larger swept shoulder
    # silhouette; these are broad successive positions rather than rib marks.
    front = smooth((-.005 - y) / .075)
    q = x + .36 * (z - 1.20)
    ridges = sum(.016 * math.exp(-((q - centre) / .039) ** 2)
                 for centre in (-.150, -.035, .080))
    return Vector((x * scale, -.005 + (y + .005) * scale - ridges * front * influence,
                   z - .014 * influence * smooth((y + .005) / .11)))


def preserve_weights(obj, original, weights, references):
    tree = KDTree(len(original))
    for index, point in enumerate(original):
        tree.insert(point, index)
    tree.balance()
    result = []
    for point in references:
        row = {}
        for _, index, distance in tree.find_n(point, min(4, len(original))):
            strength = 1. / max(distance * distance, 1e-10)
            for bone, weight in weights[index].items():
                row[bone] = row.get(bone, 0.) + weight * strength
        row = dict(sorted(row.items(), key=lambda item: (-item[1], item[0]))[:4])
        total = sum(row.values())
        result.append({bone: weight / total for bone, weight in row.items()})
    return result, tree


def body(builder):
    rig = builder.result.rig
    names = ('GEO_Torso', 'GEO_Neck', 'GEO_Arm.L', 'GEO_Arm.R', 'GEO_LegsUpper',
             'GEO_LegsLower.L', 'GEO_LegsLower.R')
    maximum_displacement = 0.
    for name in names:
        obj = bpy.data.objects[name]
        original = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        old_weights = [{obj.vertex_groups[g.group].name: g.weight for g in vertex.groups if g.weight > 0.}
                       for vertex in obj.data.vertices]
        editable = bmesh.new()
        editable.from_mesh(obj.data)
        bmesh.ops.triangulate(editable, faces=list(editable.faces))
        if name == 'GEO_Torso':
            bmesh.ops.subdivide_edges(editable, edges=list(editable.edges), cuts=2, use_grid_fill=True)
        elif name == 'GEO_LegsUpper':
            bmesh.ops.subdivide_edges(editable, edges=list(editable.edges), cuts=1, use_grid_fill=True)
        bmesh.ops.triangulate(editable, faces=list(editable.faces))
        mesh = bpy.data.meshes.new('Long exposure retained human solid')
        editable.to_mesh(mesh)
        editable.free()
        references = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
        faces = [tuple(p.vertices) for p in mesh.polygons]
        bpy.data.meshes.remove(mesh)
        weights, tree = preserve_weights(obj, original, old_weights, references)
        points = []
        for index, reference in enumerate(references):
            point = reference.copy()
            if name == 'GEO_Torso':
                # Closed lower edge overlaps the production pelvis naturally.
                if point.z < .99:
                    point.z -= .023 * smooth((.99 - point.z) / .07)
                point = torso_envelope(point)
            elif name.startswith('GEO_Arm'):
                side = name[-1]
                bone = rig.data.bones['upper_arm.' + side]
                wrist = rig.matrix_world @ rig.data.bones['hand.' + side].head_local
                a = rig.matrix_world @ bone.head_local
                axis = (rig.matrix_world @ bone.tail_local - a).normalized()
                along = (reference - a).dot(axis)
                amount = smooth((.17 - along) / .17)
                point += Vector((-.035, .075, -.008)) * amount
                # Original upper dome was fitted under a shirt. Anchor its
                # closed first ring inside the chest through an actual strike.
                nearest = tree.find(reference)[1]
                ring = nearest // 12
                if len(original) != 108:
                    raise ValueError('Long exposure arm topology changed')
                centre = sum(original[ring * 12:(ring + 1) * 12], Vector()) / 12
                radial = reference - centre
                if ring <= 1:
                    sign = 1. if side == 'L' else -1.
                    target = Vector((sign * (.145 if ring == 0 else .210), -.004,
                                     1.340 if ring == 0 else 1.395))
                    target += radial.normalized() * (.060 if ring == 0 else .078)
                    # The leading torso phase already contains this root. The
                    # large trailing fan belongs to the torso, not this cap.
                    point = target
                    weights[index] = {'chest': 1.} if ring == 0 else {'chest': .55, 'upper_arm.' + side: .45}
            elif name == 'GEO_Neck':
                point += Vector((-.008, .016, 0.))
            elif name == 'GEO_LegsUpper':
                amount = smooth((point.z - .30) / .58)
                point += Vector((-.022, .045, -.008)) * amount
                if reference.z < .315:
                    point.z -= .025 * smooth((.315 - reference.z) / .060)
            elif name.startswith('GEO_LegsLower'):
                amount = smooth((point.z - .205) / .05)
                centre_x = rig.data.bones['shin.' + name[-1]].head_local.x
                point.x += (point.x - centre_x) * .18 * amount
                point.y += (point.y + .018) * .18 * amount
                point.z += .025 * amount
            maximum_displacement = max(maximum_displacement, (point - reference).length)
            points.append(point)
        result = kit.replace(builder, name, (points, faces), {'pelvis': 1.})
        kit.skin(result, weights)
        slots = []
        for polygon in result.data.polygons:
            p = sum((points[i] for i in polygon.vertices), Vector()) / len(polygon.vertices)
            # Residue occupies only the physically trailing contour, never
            # graphic stripes wrapped around each anatomical part.
            trailing = name == 'GEO_Torso' and (p.y > .16 or p.x < -.205) and p.z > 1.04
            slots.append(1 if trailing else 0)
        set_roles(result, slots)
    return maximum_displacement


def head(builder):
    vertices, faces = [], []
    sides, rows = 40, 22
    centre = Vector((-.008, .015, HEAD_Z))
    pivot = builder.result.rig.matrix_world @ builder.result.rig.data.bones['head'].head_local
    rotation = Matrix.Rotation(math.radians(HEAD_CAST_PITCH_DEGREES), 3, 'X')
    def shape(theta, latitude):
        z = HEAD_Z + .145 * math.cos(latitude)
        radial = math.sin(latitude)
        x, y = .099 * radial * math.cos(theta), -.002 + .086 * radial * math.sin(theta)
        anterior = max(0., -math.sin(theta)) ** 8
        nose = .043 * math.exp(-((x / .020) ** 2 + ((z - 1.610) / .026) ** 2))
        bridge = .018 * math.exp(-((x / .014) ** 2 + ((z - 1.642) / .035) ** 2))
        mouth = .017 * math.exp(-((x / .037) ** 4 + ((z - 1.558) / .009) ** 2))
        brow = .013 * math.exp(-((x / .068) ** 4 + ((z - 1.665) / .010) ** 2))
        eyes = sum(.022 * math.exp(-(((x - sign * .037) / .023) ** 2 + ((z - 1.646) / .015) ** 2))
                   for sign in (-1., 1.))
        y -= (nose + bridge + mouth + brow - eyes) * anterior
        # One face stretches with its own skull into the same exposure sweep.
        sweep = smooth((z - 1.49) / .245) * (1. - .45 * anterior)
        x -= .035 * sweep
        y += .075 * sweep
        point = Vector((x + centre.x, y + centre.y, z))
        return pivot + rotation @ (point - pivot)
    vertices.append(shape(0., 0.))
    for row in range(1, rows):
        for side in range(sides):
            vertices.append(shape(math.tau * side / sides, math.pi * row / rows))
    bottom = len(vertices)
    vertices.append(shape(0., math.pi))
    for side in range(sides):
        faces.append((0, 1 + side, 1 + (side + 1) % sides))
    for row in range(rows - 2):
        for side in range(sides):
            a = 1 + row * sides + side
            b = 1 + row * sides + (side + 1) % sides
            faces.append((a, a + sides, b + sides, b))
    last = 1 + (rows - 2) * sides
    for side in range(sides):
        faces.append((last + side, bottom, last + (side + 1) % sides))
    if volume(vertices, faces) < 0:
        faces = [tuple(reversed(face)) for face in faces]
    result = kit.replace(builder, 'GEO_Head', (vertices, faces), {'head': 1.})
    slots = []
    for polygon in result.data.polygons:
        p = sum((vertices[i] for i in polygon.vertices), Vector()) / len(polygon.vertices)
        neutral = pivot + rotation.inverted() @ (p - pivot)
        slots.append(2 if neutral.y < .035 else 0)
    set_roles(result, slots)
    return dict(head_focus_blender=list(pivot + rotation @ (centre - pivot)),
                head_front_blender=list(rotation @ Vector((0., -1., 0.))),
                head_cast_pitch_degrees=HEAD_CAST_PITCH_DEGREES,
                head_nominal_core_front_back_m=.172)


def sculpt(builder):
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and not obj.name.startswith('GEO_'):
            bpy.data.objects.remove(obj, do_unlink=True)
    displacement = body(builder)
    head_contract = head(builder)
    for name in ('GEO_FaceSurface', 'GEO_Ear.L', 'GEO_Ear.R', 'GEO_EarFold.L', 'GEO_EarFold.R'):
        vertices, faces = kit.shell([(1.598, -.008, .055, .003, .003),
                                    (1.608, -.008, .055, .003, .003)], 8)
        result = kit.replace(builder, name, (vertices, faces), {'head': 1.})
        set_roles(result, [0] * len(faces), ('flesh',))
    for obj in bpy.data.objects:
        if obj.type == 'MESH' and obj.name.startswith('GEO_') and 'long_exposure_surface_roles' not in obj:
            role = 'flesh' if obj.name.startswith('GEO_Hand') else 'emulsion'
            set_roles(obj, [0] * len(obj.data.polygons), (role,))
    return dict(phases=3, phase_offsets_blender_metres=[list(p) for p in PHASES],
                space='blender_metres', opaque=True, one_head=True, source_closed=True,
                method='single closed anatomical envelopes; three fused elliptical phase sections',
                maximum_authored_displacement_m=round(displacement, 7),
                maximum_allowed_displacement_m=.45,
                unchanged_contact_renderers=[obj.name for obj in bpy.data.objects if obj.type == 'MESH'
                                             and obj.name.startswith(('GEO_Hand', 'GEO_Foot'))],
                **head_contract)
