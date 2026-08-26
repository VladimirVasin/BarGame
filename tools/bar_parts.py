#!/usr/bin/env python3
"""Unity-space authoring helpers for the bar generator.

The bar's layout was authored in Unity coordinates - the plan publishes
them, the tests assert them, and every number in
`BarInteriorWorldBuilder` was written in them.  Re-deriving each one by
hand into Blender's frame is how a one-to-one migration stops being one
to one, so this module lets the generator keep writing Unity numbers and
converts whole solids at the end.

The conversion is a SWAP of the last two axes, which is a reflection.
A reflection reverses face winding, so `to_source` reverses every face
as well; `signed_volume` exists to prove it did, because a model whose
normals all point inward looks perfectly correct in a wireframe and
perfectly wrong under a light.

Unity conventions reproduced here exactly, because the primitives being
replaced used them:

* a box's ``size`` is its full extent on each axis;
* a cylinder's ``size`` is (diameter, HALF height, diameter) - the mesh
  spans ``y = -1..1`` at radius ``0.5`` and is scaled, so the drawn
  height is twice ``size.y``;
* the low-poly cylinder has 8 sides;
* Euler angles are applied Z, then X, then Y.
"""

from __future__ import annotations

import math
from typing import Sequence

import interior_kit as kit

Geometry = kit.Geometry

CYLINDER_SIDES = 8


# ------------------------------------------------------ Unity solids --


def u_box(
    center: Sequence[float],
    size: Sequence[float],
    chamfer: float = 0.01,
) -> Geometry:
    """A Unity `CreateBox`, relieved on the edges a box cannot relieve."""
    return kit.chamfered_box(center, size, chamfer)


def u_plate(center: Sequence[float], size: Sequence[float]) -> Geometry:
    """A thin card - poster, panel, sign. Chamfered to suit its thinness."""
    thinnest = min(abs(value) for value in size)
    return kit.chamfered_box(center, size, min(0.008, thinnest * 0.24))


def u_cylinder(
    center: Sequence[float],
    size: Sequence[float],
    sides: int = CYLINDER_SIDES,
) -> Geometry:
    """A Unity `CreateCylinder`: diameter `size.x`, height `2 * size.y`."""
    cx, cy, cz = center
    radius = size[0] * 0.5
    half = size[1]
    ring_low: list[int] = []
    ring_high: list[int] = []
    vertices: list[tuple[float, float, float]] = []
    for index in range(sides):
        angle = math.tau * index / sides
        x = cx + math.cos(angle) * radius
        z = cz + math.sin(angle) * (size[2] * 0.5)
        ring_low.append(len(vertices))
        vertices.append((x, cy - half, z))
    for index in range(sides):
        angle = math.tau * index / sides
        x = cx + math.cos(angle) * radius
        z = cz + math.sin(angle) * (size[2] * 0.5)
        ring_high.append(len(vertices))
        vertices.append((x, cy + half, z))

    #  Wound the other way round than `interior_kit.lathe` is, and the
    #  difference is not a mistake: that one sweeps its ring through XY
    #  about Z, this one through XZ about Y, and the same (cos, sin)
    #  traversal runs the opposite way in the second plane. Getting it
    #  wrong turns every cylinder in the bar inside out, which is why
    #  `signed_volume` checks each solid rather than trusting this.
    faces: list[tuple[int, ...]] = [tuple(ring_low),
                                    tuple(reversed(ring_high))]
    for index in range(sides):
        following = (index + 1) % sides
        faces.append((
            ring_low[following], ring_low[index],
            ring_high[index], ring_high[following],
        ))
    return vertices, faces


def u_tapered_cylinder(
    center: Sequence[float],
    size: Sequence[float],
    top_scale: float,
    sides: int = CYLINDER_SIDES,
) -> Geometry:
    """A cylinder whose top ring is narrowed - a cup, a shade, a glass.

    The primitives could only ever be straight cylinders; anything that
    should read as tapered had to be faked with a colour. This is the
    cheapest thing the migration buys back.
    """
    cx, cy, cz = center
    half = size[1]
    rings: list[list[int]] = []
    vertices: list[tuple[float, float, float]] = []
    for level, scale in ((cy - half, 1.0), (cy + half, top_scale)):
        row: list[int] = []
        for index in range(sides):
            angle = math.tau * index / sides
            row.append(len(vertices))
            vertices.append((
                cx + math.cos(angle) * size[0] * 0.5 * scale,
                level,
                cz + math.sin(angle) * size[2] * 0.5 * scale,
            ))
        rings.append(row)

    faces: list[tuple[int, ...]] = [tuple(rings[0]),
                                    tuple(reversed(rings[1]))]
    for index in range(sides):
        following = (index + 1) % sides
        faces.append((
            rings[0][following], rings[0][index],
            rings[1][index], rings[1][following],
        ))
    return vertices, faces


# ---------------------------------------------------------- transform -


def u_rotated(geometry: Geometry, euler: Sequence[float]) -> Geometry:
    """Rotates about the ORIGIN using Unity's Z, then X, then Y order."""
    rx, ry, rz = (math.radians(value) for value in euler)
    cos_x, sin_x = math.cos(rx), math.sin(rx)
    cos_y, sin_y = math.cos(ry), math.sin(ry)
    cos_z, sin_z = math.cos(rz), math.sin(rz)

    def apply(point: Sequence[float]) -> tuple[float, float, float]:
        x, y, z = point
        # Z
        x, y = x * cos_z - y * sin_z, x * sin_z + y * cos_z
        # X
        y, z = y * cos_x - z * sin_x, y * sin_x + z * cos_x
        # Y
        x, z = x * cos_y + z * sin_y, -x * sin_y + z * cos_y
        return x, y, z

    vertices, faces = geometry
    return [apply(vertex) for vertex in vertices], list(faces)


def u_rotated_about(
    geometry: Geometry,
    euler: Sequence[float],
    pivot: Sequence[float],
) -> Geometry:
    moved = kit.translated(geometry, (-pivot[0], -pivot[1], -pivot[2]))
    return kit.translated(u_rotated(moved, euler), pivot)


def to_source(geometry: Geometry) -> Geometry:
    """Unity space -> Blender space: swap Y and Z, reverse the winding.

    The swap is a reflection. Leaving the winding alone would turn the
    whole model inside out - lit from within, backface-culled from
    without - which is a defect that survives every check except a
    rendered frame or a signed volume.
    """
    vertices, faces = geometry
    swapped = [(x, z, y) for x, y, z in vertices]
    return swapped, [tuple(reversed(face)) for face in faces]


def signed_volume(geometry: Geometry) -> float:
    """Six times the enclosed volume; negative means inverted normals."""
    vertices, faces = geometry
    total = 0.0
    for face in faces:
        for index in range(1, len(face) - 1):
            a = vertices[face[0]]
            b = vertices[face[index]]
            c = vertices[face[index + 1]]
            total += (
                a[0] * (b[1] * c[2] - b[2] * c[1])
                - a[1] * (b[0] * c[2] - b[2] * c[0])
                + a[2] * (b[0] * c[1] - b[1] * c[0])
            )
    return total / 6.0


# --------------------------------------------------------- tint specs -


def ident(field: str, scale: float = 1.0) -> dict:
    """A tint taken from `BarDistrictIdentity`, optionally amplified."""
    return {
        "field": field,
        "rgb": [0.0, 0.0, 0.0],
        "scale": scale,
        "lerp_field": "",
        "lerp_rgb": [0.0, 0.0, 0.0],
        "lerp_t": 0.0,
    }


def rgb(r: float, g: float, b: float, scale: float = 1.0) -> dict:
    """A fixed colour, as the primitive it replaces used."""
    return {
        "field": "",
        "rgb": [r, g, b],
        "scale": scale,
        "lerp_field": "",
        "lerp_rgb": [0.0, 0.0, 0.0],
        "lerp_t": 0.0,
    }


def lerp(first: dict, second: dict, t: float) -> dict:
    """Blends two tints. Only the second may be a fixed colour."""
    blended = dict(first)
    blended["lerp_field"] = second["field"]
    blended["lerp_rgb"] = list(second["rgb"])
    blended["lerp_t"] = t
    return blended
