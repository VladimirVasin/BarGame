"""Solid pipe walls with an open bore. Points and winding use the caller's axes."""

import math

from mathutils import Vector


def pipe_wall(points, radius, thickness, sides=12):
    centers = [Vector(point) for point in points]
    axes = [(b - a).normalized() for a, b in zip(centers, centers[1:])]
    first = axes[0].cross(Vector((1, 0, 0)))
    if first.length_squared < .01:
        first = axes[0].cross(Vector((0, 1, 0)))
    first.normalize()
    vertices, faces = [], []
    for i, center in enumerate(centers):
        axis = axes[0] if i == 0 else axes[-1] if i == len(centers) - 1 else (axes[i - 1] + axes[i]).normalized()
        first = (first - first.dot(axis) * axis).normalized()
        second = axis.cross(first)
        for width in (radius, radius - thickness):
            for side in range(sides):
                angle = side * math.tau / sides
                vertices.append(tuple(center + width * (math.cos(angle) * first + math.sin(angle) * second)))
    step = 2 * sides
    for side in range(sides):
        following = (side + 1) % sides
        faces.append((following, side, side + sides, following + sides))
        for i in range(0, (len(centers) - 1) * step, step):
            a, b = i + side, i + following
            faces.append((a, b, b + step, a + step))
            faces.append((b + sides, a + sides, a + sides + step, b + sides + step))
        a, b = (len(centers) - 1) * step + side, (len(centers) - 1) * step + following
        faces.append((a, b, b + sides, a + sides))
    return vertices, faces
