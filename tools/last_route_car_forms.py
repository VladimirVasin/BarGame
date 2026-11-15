"""Measured section meshes for the Last Route sedan, in Blender metres."""

from __future__ import annotations

import math

from mathutils import Vector


def append(mesh, vertices, faces, closed=True):
    vertices = [Vector(v) for v in vertices]
    if closed:
        volume = sum(vertices[f[0]].dot(vertices[f[k]].cross(vertices[f[k + 1]]))
                     for f in faces for k in range(1, len(f) - 1))
        if abs(volume) < 1.e-9:
            raise ValueError("A closed car section has no volume")
        if volume < 0:
            faces = [tuple(reversed(f)) for f in faces]
    base = len(mesh.vertices)
    mesh.vertices.extend(tuple(v) for v in vertices)
    mesh.faces.extend(tuple(base + i for i in f) for f in faces)


def loft(mesh, sections, loop=False):
    """Connect equal rings and close the ends, or wrap a hollow revolve."""
    faces = []
    size = len(sections[0])
    for i in range(len(sections) if loop else len(sections) - 1):
        a, b = i * size, ((i + 1) % len(sections)) * size
        for j in range(size):
            k = (j + 1) % size
            faces.append((a + j, a + k, b + k, b + j))
    if not loop:
        last = (len(sections) - 1) * size
        faces.extend((tuple(reversed(range(size))), tuple(last + j for j in range(size))))
    append(mesh, [v for section in sections for v in section], faces)


def revolve(mesh, center, axis, profile, segments, loop=False):
    axis = Vector(axis).normalized()
    across = axis.cross(Vector((0, 0, 1))).normalized()
    if across.length_squared < .1:
        across = Vector((1, 0, 0))
    up = axis.cross(across)
    center = Vector(center)
    circle = [across * math.cos(math.tau * i / segments) + up * math.sin(math.tau * i / segments)
              for i in range(segments)]
    loft(mesh, [[center + axis * d + radial * r for radial in circle]
                for d, r in profile], loop)


def deck(mesh, stations, thickness, underside=False):
    """A pressed skin with a shallow crown and narrow rolled shoulders."""
    strips = (-1., -.98, -.84, -.44, 0., .44, .84, .98, 1.)
    rings = []
    for y, half, crown, edge in stations:
        section = [(s * half, y, edge + crown * (1. - s * s) - .012 * abs(s) ** 16)
                   for s in strips]
        rings.append(section + [(x, y, z - thickness) for x, y, z in reversed(section)])
    loft(mesh, list(reversed(rings)) if underside else rings)


def chamfered_ring(cx, z, half_width, half_height, radius, y):
    radius = min(radius, .8 * half_width, .8 * half_height)
    return [(cx + x, y, z + h) for x, h in (
        (-half_width + radius, -half_height), (half_width - radius, -half_height),
        (half_width, -half_height + radius), (half_width, half_height - radius),
        (half_width - radius, half_height), (-half_width + radius, half_height),
        (-half_width, half_height - radius), (-half_width, -half_height + radius))]


def trim_path(mesh, path, radius, sides=6):
    points = [Vector(p) for p in path]
    rings = []
    for i, p in enumerate(points):
        axis = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        a = axis.cross(Vector((1, 0, 0)))
        if a.length_squared < .1:
            a = axis.cross(Vector((0, 0, 1)))
        a.normalize()
        b = axis.cross(a)
        rings.append([p + radius * (a * math.cos(math.tau * j / sides) + b * math.sin(math.tau * j / sides))
                      for j in range(sides)])
    loft(mesh, rings)


def arch_floor(y, axle, base, radius=.425, centre=.335):
    """The cutoff meets the underside at the two arch ends without a step."""
    d = y - axle
    span = math.sqrt(radius * radius - (centre - base) ** 2)
    return centre + math.sqrt(max(0., radius * radius - d * d)) if abs(d) < span else base


def arch_stations(axle, floor, radius=.425, centre=.335):
    first = math.asin((floor - centre) / radius)
    steps = 18
    return [axle + radius * math.cos(first + (math.pi - 2 * first) * n / steps)
            for n in range(steps + 1)]


def side_skin(mesh, side, ys, top, bottom, contour, offset=(0., 0., 0.), thickness=.026, retract=None):
    """A slab with a continuous curved face, rolled returns and a solid back."""
    rings = []
    for y in sorted(set(ys)):
        floor = bottom(y)
        width = top - floor
        if width <= .008:
            raise ValueError("A car side contour has crossed its top")
        rows = (0., .045, .25, .54, .80, .955, 1.)
        front = []
        for i, v in enumerate(rows):
            z = floor + v * width
            x = contour(y, z) - (.008 if i in (0, len(rows) - 1) else 0.)
            section_y = y if retract is None else y + retract(y, z)
            front.append((side * x - offset[0], section_y - offset[1], z - offset[2]))
        rings.append(front + [(x - side * thickness, y, z) for x, y, z in reversed(front)])
    loft(mesh, rings)


def pane(mesh, corners, crown=.0, outward=(0, -1, 0)):
    """Window grid, with both windings for interior and exterior views."""
    a, b, c, d = (Vector(v) for v in corners)
    normal = Vector(outward)
    vertices, faces = [], []
    for j in range(4):
        v = j / 3
        for i in range(7):
            u = i / 6
            p = a.lerp(b, u).lerp(d.lerp(c, u), v)
            vertices.append(p + normal * (crown * 4 * u * (1. - u)))
    for j in range(3):
        for i in range(6):
            n = j * 7 + i
            faces.append((n, n + 1, n + 8, n + 7))
    append(mesh, vertices, faces, False)
    append(mesh, vertices, [tuple(reversed(f)) for f in faces], False)


def body_uv(x, y, z, normal):
    """Five authored atlas regions (the long flank strips include the sills)."""
    if abs(normal.x) > max(abs(normal.y), abs(normal.z)):
        return .025 + (y + 2.65) * .95 / 5.3, (.022 if normal.x > 0 else .225) + z * .107
    if abs(normal.z) >= abs(normal.y):
        return .025 + (y + 2.65) * .95 / 5.3, .462 + (x + 1.05) * .30 / 2.10
    return (.035 if normal.y < 0 else .525) + (x + 1.05) * .44 / 2.10, .804 + z * .103
