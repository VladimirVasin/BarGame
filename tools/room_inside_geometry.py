"""An independent low-poly human coat framing a real tapered torso corridor.

Geometry stays in the retained NpcHumanV2 renderer frames. The doorway is an
actual opening: closed annular solids surround it and no old torso remains.
"""
from __future__ import annotations

import math

import bpy
from mathutils import Vector

import crane_mutant_geometry as kit

MOUTH = dict(depth=-.182, half_width=.155, bottom=1.015, top=1.385)
FAR = dict(depth=.22, half_width=.035, bottom=1.165, top=1.265)
MIN_TORSO_THICKNESS = .018
FRAME_DEPTHS = (-.17, -.065, .045, .15)
HEAD_SCALE = 1.35
HEAD_ANCHOR_BLENDER = (0., -.009, 1.490)
PRESENTATION_SCALE = 1.2
UPPER_ARM_LENGTH_SCALE = 1.3
FOREARM_LENGTH_SCALE = 1.8
HAND_LENGTH_SCALE = 1.5
HAND_LONGITUDINAL_AXIS = 'local_y'
ARM_UPPER_SAMPLES = (0., .10, .34, .65, .90, 1.)
ARM_FOREARM_SAMPLES = (.08, .26, .53, .78, .94, 1.)


def outward(vertices, faces):
    volume = sum(Vector(vertices[f[0]]).dot(Vector(vertices[f[i]]).cross(Vector(vertices[f[i + 1]]))) / 6
                 for f in faces for i in range(1, len(f) - 1))
    if abs(volume) < 1e-10:
        raise ValueError('Room has a zero-volume solid')
    return [tuple(reversed(f)) for f in faces] if volume < 0 else faces


def rect(width, bottom, top):
    return ((-width, bottom), (width, bottom), (width, top), (-width, top))


def ring(front, back, depth_front, depth_back):
    """Closed annular prism; each section is (outer rectangle, inner rectangle)."""
    vertices = [(point[0], depth + (point[2] if len(point) > 2 else 0.), point[1])
                for section, depth in ((front, depth_front), (back, depth_back))
                for contour in section for point in contour]
    n = len(front[0])
    if any(len(contour) != n for section in (front, back) for contour in section):
        raise ValueError('Room annulus has unmatched perimeter rings')
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.extend(((i, j, n + j, n + i), (2 * n + i, 3 * n + i, 3 * n + j, 2 * n + j),
                      (i, 2 * n + i, 2 * n + j, j), (n + i, n + j, 3 * n + j, 3 * n + i)))
    return vertices, outward(vertices, faces)


def corridor_at(depth):
    t = (depth - MOUTH['depth']) / (FAR['depth'] - MOUTH['depth'])
    return (MOUTH['half_width'] * (1 - t) + FAR['half_width'] * t,
            MOUTH['bottom'] * (1 - t) + FAR['bottom'] * t,
            MOUTH['top'] * (1 - t) + FAR['top'] * t)


def solid(points, thickness):
    vertices = [(x, y - thickness * .5, z) for x, y, z in points]
    vertices += [(x, y + thickness * .5, z) for x, y, z in points]
    n = len(points)
    faces = [tuple(reversed(range(n))), tuple(n + i for i in range(n))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    return vertices, outward(vertices, faces)


def sculpt(builder):
    rig = builder.result.rig
    bones = {b.name: (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones}
    # Wardrobe and hair cannot cover the newly authored negative space.
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and not obj.name.startswith('GEO_'):
            bpy.data.objects.remove(obj, do_unlink=True)
    vertices, faces, weights, roles = [], [], [], []

    def append(geometry, role='cloth'):
        points, polygons = geometry
        offset = len(vertices)
        vertices.extend(points)
        faces.extend(tuple(i + offset for i in p) for p in polygons)
        weights.extend(kit.trunk_weight(p[2]) for p in points)
        roles.extend([0 if role == 'cloth' else 1] * len(polygons))

    # The coat is a thick annulus, not a full chest behind a painted rectangle.
    def doorway_perimeter(width, low, high):
        return ((-width, low), (width, low), (width, low + (high - low) * .17),
                (width, low + (high - low) * .77), (width, high),
                (-width, high), (-width, low + (high - low) * .77),
                (-width, low + (high - low) * .17))
    # Sloped shoulder caps, waist taper and bevelled lower corners replace the
    # first block-shaped silhouette. The corridor rear stays inside this coat.
    outer_front = ((-.181, .975, .085), (.181, .975, .085), (.205, 1.065, .095),
                   (.246, 1.295, .100), (.208, 1.425, .105), (-.208, 1.425, .105),
                   (-.246, 1.295, .100), (-.205, 1.065, .095))
    outer_back = ((-.158, .978, -.140), (.158, .978, -.140), (.181, 1.065, -.140),
                  (.213, 1.282, -.140), (.183, 1.397, -.165), (-.183, 1.397, -.165),
                  (-.213, 1.282, -.140), (-.181, 1.065, -.140))
    bw, bl, bh = corridor_at(.21)
    coat_front = (outer_front, doorway_perimeter(.155, 1.015, 1.385))
    coat_back = (outer_back, doorway_perimeter(bw + .003, bl - .003, bh + .003))
    append(ring(coat_front, coat_back, -.17, .21))

    # A tapered corridor with physical inner walls/floor/ceiling and no cap.
    # Every wall is 20 mm solid, so source-bound wound tissue stays inside it.
    width, low, high = corridor_at(MOUTH['depth'])
    fw, fl, fh = corridor_at(FAR['depth'])
    append(ring((rect(width + .020, low - .020, high + .020), rect(width, low, high)),
                (rect(fw + .020, fl - .020, fh + .020), rect(fw, fl, fh)),
                MOUTH['depth'], FAR['depth']), 'plaster')
    # Four distinct architraves shrink into the body. Bevelled stepped profiles
    # produce real light breaks even after the low-resolution final composite.
    for depth in FRAME_DEPTHS:
        w, lo, hi = corridor_at(depth)
        outer = rect(w + .008, lo - .008, hi + .008)
        inner = rect(w - .018, lo + .018, hi - .018)
        append(ring((outer, inner), (outer, inner), depth - .010, depth + .010), 'plaster')
        outer2 = rect(w + .006, lo - .006, hi + .006)
        inner2 = rect(w - .012, lo + .012, hi - .012)
        append(ring((outer2, inner2), (outer2, inner2), depth - .026, depth - .008), 'plaster')

    # Long parted skirts and lifted lapels make an ordinary coat silhouette.
    # They never cross the doorway and share the same source-bound torso mesh.
    for sign in (-1, 1):
        append(solid([(sign * .052, -.126, .965), (sign * .235, -.103, 1.02),
                      (sign * .235, -.085, .668), (sign * .075, -.137, .625)], .024))
        append(solid([(sign * .160, -.197, 1.030), (sign * .239, -.182, 1.254),
                      (sign * .211, -.158, 1.430), (sign * .165, -.199, 1.380)], .022))
        # A folded collar continues the worn coat into the narrow human neck.
        append(solid([(sign * .045, -.100, 1.40), (sign * .164, -.180, 1.403),
                      (sign * .198, -.102, 1.451), (sign * .055, -.051, 1.464)], .020))
    torso = kit.replace(builder, 'GEO_Torso', (vertices, faces), {'pelvis': 1.})
    kit.skin(torso, weights)
    for poly, role in zip(torso.data.polygons, roles):
        poly.material_index = role
        poly.use_smooth = False
    torso['room_surface_roles'] = ['cloth', 'plaster']

    # The gaunt head is a real closed human tissue volume, not a painted mask.
    # A slight mid-face ridge catches the chest lamp without adding eyes/symbols.
    head_rows = [(1.475, 0., -.010, .031, .031), (1.505, 0., -.023, .043, .040),
                 (1.548, 0., -.025, .052, .047), (1.596, 0., -.020, .057, .050),
                 (1.648, 0., -.008, .056, .048), (1.699, 0., .002, .047, .041),
                 (1.727, 0., .004, .026, .026), (1.734, 0., .005, .003, .003)]
    head_vertices, head_faces = kit.shell(head_rows, 18,
        lambda row, a: 1. + (.045 * max(0., -math.sin(a)) ** 10 if row in (2, 3, 4) else 0.))
    anchor = Vector(HEAD_ANCHOR_BLENDER)
    head_vertices = [anchor + (Vector(point) - anchor) * HEAD_SCALE for point in head_vertices]
    head = kit.replace(builder, 'GEO_Head', (head_vertices, head_faces), {'head': 1.})
    head['room_surface_roles'] = ['flesh']
    neck = kit.replace(builder, 'GEO_Neck', kit.shell([
        (1.397, 0., .008, .042, .038), (1.423, 0., .004, .031, .029),
        (1.455, 0., -.001, .036, .034), (1.490, 0., -.009, .044, .041)], 14),
        lambda p: {'neck': max(0., min(1., (p[2] - 1.397) / .045)),
                   'chest': max(0., 1. - (p[2] - 1.397) / .045)})
    neck['room_surface_roles'] = ['flesh']

    for side in ('L', 'R'):
        upper, lower = 'upper_arm.' + side, 'forearm.' + side
        a, b, c = bones[upper][0], bones[lower][0], bones[lower][1]
        points = [a.lerp(b, t) for t in ARM_UPPER_SAMPLES]
        points += [b.lerp(c, t) for t in ARM_FOREARM_SAMPLES]
        arm = kit.replace(builder, 'GEO_Arm.' + side,
            kit.tube(points, (.057, .077, .078, .071, .060, .055, .056, .060, .056, .048, .039, .037), 12, .86, False),
            {upper: 1.})
        arm_weights = []
        for row in range(len(points)):
            shoulder = .14 if row == 0 else .04 if row == 1 else 0.
            if row < len(ARM_UPPER_SAMPLES):
                t = ARM_UPPER_SAMPLES[row]
                assignment = {upper: (1 - t) * (1 - shoulder), lower: t * (1 - shoulder), 'chest': shoulder}
            else:
                t = ARM_FOREARM_SAMPLES[row - len(ARM_UPPER_SAMPLES)]
                assignment = {lower: 1 - t, 'hand.' + side: t}
            arm_weights.extend([assignment] * 12)
        kit.skin(arm, arm_weights)
        arm['room_surface_roles'] = ['cloth']

    upper_vertices, upper_faces, upper_weights = [], [], []
    for side in ('L', 'R'):
        thigh, shin = 'thigh.' + side, 'shin.' + side
        a, b, c = bones[thigh][0], bones[shin][0], bones[shin][1]
        geo = kit.tube([a.lerp(b, t) for t in (0., .12, .35, .66, .88, 1.)],
                       (.074, .085, .078, .067, .055, .052), 12, .90, False)
        kit.append_mesh(None, upper_vertices, upper_faces, upper_weights, geo, {thigh: 1.})
        calf = kit.replace(builder, 'GEO_LegsLower.' + side,
            kit.tube([b.lerp(c, t) for t in (0., .12, .38, .70, .92, 1.)],
                     (.052, .061, .056, .047, .043, .041), 12, .85, False), {shin: 1.})
        calf['room_surface_roles'] = ['cloth']
    upper = kit.replace(builder, 'GEO_LegsUpper', (upper_vertices, upper_faces), {'pelvis': 1.})
    kit.skin(upper, upper_weights)
    upper['room_surface_roles'] = ['cloth']

    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not obj.name.startswith('GEO_'):
            continue
        if 'room_surface_roles' not in obj:
            obj['room_surface_roles'] = ['cloth' if obj.name.startswith('GEO_Foot') else 'flesh']
        if len(obj['room_surface_roles']) == 1:
            for polygon in obj.data.polygons:
                polygon.material_index = 0
    return dict(mouth=MOUTH, far=FAR, frame_depths=list(FRAME_DEPTHS),
                frame_count=len(FRAME_DEPTHS), minimum_torso_solid_thickness_m=MIN_TORSO_THICKNESS,
                corridor_depth_m=FAR['depth'] - MOUTH['depth'],
                open_entrance=True, open_far_end=True, original_torso_removed=True,
                source_bound_tissue_depth_m=.004)
