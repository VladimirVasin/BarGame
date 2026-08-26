#!/usr/bin/env python3
"""Shared geometry for interior generators.

Every interior in this project is about to stop being a heap of runtime
boxes and start being an authored model.  This module is the part of that
work which is worth doing once: wall runs that actually have openings,
slabs that carry a skirting, mouldings swept along a path, and above all
CHAMFERS.

The chamfer is the whole argument.  A PS1-era flat-shaded box meets its
neighbour in a single hard edge, and the composite renderer draws that
edge as one black line; a real building has a two-millimetre arris on
everything a hand has ever touched, and it catches light instead.  That
difference is most of what separates "assembled from primitives" from
"modelled", and it cannot be expressed by a box at all.

Conventions, matching the existing generators:

* Blender source space: metres, **Z up**, forward **-Y**.
* Geometry is a plain ``(vertices, faces)`` tuple of Python floats.  No
  ``bpy``, no ``bmesh``, no modifiers - so this module imports cleanly
  outside Blender and can be exercised by an ordinary test.
* Faces wind counter-clockwise seen from outside.
* Nothing here knows about any particular room.  A value specific to one
  interior does not belong in this file; the next interior has to be able
  to import it unchanged, and that is the only test of whether it is
  general.

This is NOT a second RuntimePrimitiveFactory.  That one composes cubes at
runtime because it must; this one is an authoring library whose whole
purpose is to produce the profiled, relieved geometry a cube cannot.
"""

from __future__ import annotations

import math
from typing import Iterable, Sequence

Vec3 = tuple[float, float, float]
Face = tuple[int, ...]
Geometry = tuple[list[Vec3], list[Face]]

#: Default arris on a worked edge.  Small enough to read as a crisp
#: corner at 640x360 and large enough to catch a highlight.
DEFAULT_CHAMFER = 0.012

EMPTY: Geometry = ([], [])


# --------------------------------------------------------------- basics


def merge(*items: Geometry) -> Geometry:
    """Concatenates geometries, re-basing each one's face indices."""
    vertices: list[Vec3] = []
    faces: list[Face] = []
    for item_vertices, item_faces in items:
        offset = len(vertices)
        vertices.extend(item_vertices)
        faces.extend(
            tuple(index + offset for index in face) for face in item_faces
        )
    return vertices, faces


def merge_all(items: Iterable[Geometry]) -> Geometry:
    return merge(*items)


def translated(geometry: Geometry, offset: Sequence[float]) -> Geometry:
    dx, dy, dz = offset
    vertices, faces = geometry
    moved = [(x + dx, y + dy, z + dz) for x, y, z in vertices]
    return moved, list(faces)


def rotated_z(geometry: Geometry, degrees: float) -> Geometry:
    """Turns about the vertical.  Interiors never need any other axis."""
    radians = math.radians(degrees)
    cos, sin = math.cos(radians), math.sin(radians)
    vertices, faces = geometry
    turned = [
        (x * cos - y * sin, x * sin + y * cos, z) for x, y, z in vertices
    ]
    return turned, list(faces)


def scaled(geometry: Geometry, factors: Sequence[float]) -> Geometry:
    fx, fy, fz = factors
    vertices, faces = geometry
    return [(x * fx, y * fy, z * fz) for x, y, z in vertices], list(faces)


def bounds(geometry: Geometry) -> tuple[Vec3, Vec3]:
    vertices, _ = geometry
    if not vertices:
        return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
    xs = [vertex[0] for vertex in vertices]
    ys = [vertex[1] for vertex in vertices]
    zs = [vertex[2] for vertex in vertices]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def triangle_count(geometry: Geometry) -> int:
    _, faces = geometry
    return sum(max(0, len(face) - 2) for face in faces)


def box(center: Sequence[float], size: Sequence[float]) -> Geometry:
    """The unchamfered base case, kept for hidden and structural parts."""
    cx, cy, cz = center
    sx, sy, sz = (value * 0.5 for value in size)
    vertices = [
        (cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz),
        (cx + sx, cy + sy, cz - sz), (cx - sx, cy + sy, cz - sz),
        (cx - sx, cy - sy, cz + sz), (cx + sx, cy - sy, cz + sz),
        (cx + sx, cy + sy, cz + sz), (cx - sx, cy + sy, cz + sz),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    return vertices, faces


def chamfered_box(
    center: Sequence[float],
    size: Sequence[float],
    chamfer: float = DEFAULT_CHAMFER,
    top: bool = True,
    bottom: bool = True,
) -> Geometry:
    """A box whose horizontal edges are cut back.

    The four vertical arrises are always taken; the top and bottom rims
    are optional because a slab bedded into a wall wants its buried edge
    left square.  This is the workhorse: anything a person walks past or
    puts a glass on should be built with it rather than with `box`.
    """
    cx, cy, cz = center
    sx, sy, sz = (value * 0.5 for value in size)
    cut = min(chamfer, sx * 0.5, sy * 0.5, sz * 0.5)
    if cut <= 1e-6:
        return box(center, size)

    def ring(z: float, inset: float) -> list[Vec3]:
        x, y = sx - inset, sy - inset
        return [
            (cx - x + 0.0, cy - y, z), (cx + x, cy - y, z),
            (cx + x, cy + y, z), (cx - x, cy + y, z),
        ]

    # Octagonal in plan would double the vertex count for no visible gain
    # at this resolution; the plan stays square and only the vertical
    # extremes pull in, which is what reads as a relieved edge.
    low = cz - sz
    high = cz + sz
    levels: list[tuple[float, float]] = []
    levels.append((low, cut if bottom else 0.0))
    if bottom:
        levels.append((low + cut, 0.0))
    if top:
        levels.append((high - cut, 0.0))
    levels.append((high, cut if top else 0.0))

    vertices: list[Vec3] = []
    rings: list[list[int]] = []
    for z, inset in levels:
        start = len(vertices)
        vertices.extend(ring(z, inset))
        rings.append([start, start + 1, start + 2, start + 3])

    faces: list[Face] = []
    first, last = rings[0], rings[-1]
    faces.append((first[0], first[3], first[2], first[1]))
    faces.append((last[0], last[1], last[2], last[3]))
    for lower, upper in zip(rings, rings[1:]):
        for index in range(4):
            following = (index + 1) % 4
            faces.append((
                lower[index], lower[following],
                upper[following], upper[index],
            ))
    return vertices, faces


def prism(profile: Sequence[tuple[float, float]], depth: float) -> Geometry:
    """Extrudes a closed 2D profile in XZ along Y, centred on the origin.

    The profile is the cross-section of a moulding, a skirting, a nosing
    or a rail.  Points run counter-clockwise in (x, z).
    """
    half = depth * 0.5
    count = len(profile)
    vertices: list[Vec3] = []
    for y in (-half, half):
        vertices.extend((x, y, z) for x, z in profile)

    #  A profile wound counter-clockwise in XZ and pushed along Y comes
    #  out wound the opposite way from the same profile in XY pushed
    #  along Z - the two planes disagree about which direction is
    #  positive. This is the only routine here that extrudes across Y,
    #  and it is the only one that needs the reversal.
    faces: list[Face] = []
    for index in range(count):
        following = (index + 1) % count
        faces.append((
            following, index, count + index, count + following,
        ))
    faces.append(tuple(range(count)))
    faces.append(tuple(reversed(range(count, count * 2))))
    return vertices, faces


def lathe(
    profile: Sequence[tuple[float, float]],
    segments: int = 12,
) -> Geometry:
    """Turns a (radius, height) profile about Z - legs, posts, bottles."""
    rows: list[list[int]] = []
    vertices: list[Vec3] = []
    for radius, height in profile:
        row: list[int] = []
        for index in range(segments):
            angle = math.tau * index / segments
            row.append(len(vertices))
            vertices.append((
                math.cos(angle) * radius,
                math.sin(angle) * radius,
                height,
            ))
        rows.append(row)

    faces: list[Face] = []
    for lower, upper in zip(rows, rows[1:]):
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((
                lower[index], lower[following],
                upper[following], upper[index],
            ))
    faces.append(tuple(reversed(rows[0])))
    faces.append(tuple(rows[-1]))
    return vertices, faces


# ------------------------------------------------------------- profiles


def profile_rectangle(width: float, height: float) -> list[tuple[float, float]]:
    x, z = width * 0.5, height * 0.5
    return [(-x, -z), (x, -z), (x, z), (-x, z)]


def profile_chamfered(
    width: float,
    height: float,
    cut: float = DEFAULT_CHAMFER,
) -> list[tuple[float, float]]:
    x, z = width * 0.5, height * 0.5
    cut = min(cut, x * 0.5, z * 0.5)
    return [
        (-x + cut, -z), (x - cut, -z), (x, -z + cut), (x, z - cut),
        (x - cut, z), (-x + cut, z), (-x, z - cut), (-x, -z + cut),
    ]


def profile_skirting(
    height: float,
    depth: float,
    cove: float = 0.018,
) -> list[tuple[float, float]]:
    """A skirting: flat against the wall, a splayed top, a foot at the floor.

    The splay is what stops a floor/wall junction reading as one black
    line, which is exactly what it reads as when both are boxes.
    """
    return [
        (0.0, 0.0), (depth, 0.0), (depth, height - cove * 2.2),
        (depth * 0.45, height - cove * 0.6), (depth * 0.32, height),
        (0.0, height),
    ]


def profile_cornice(height: float, depth: float) -> list[tuple[float, float]]:
    """A cornice: the same idea run the other way up, at the ceiling."""
    return [
        (0.0, 0.0), (depth * 0.3, 0.0), (depth, height * 0.62),
        (depth, height), (0.0, height),
    ]


def profile_nosing(
    thickness: float,
    overhang: float,
) -> list[tuple[float, float]]:
    """A counter or table edge: a rounded-off bullnose in three facets."""
    z = thickness * 0.5
    return [
        (0.0, -z), (overhang * 0.62, -z), (overhang, -z * 0.45),
        (overhang, z * 0.45), (overhang * 0.62, z), (0.0, z),
    ]


# ------------------------------------------------------------ sweeping


def sweep(
    path: Sequence[Sequence[float]],
    profile: Sequence[tuple[float, float]],
    z: float = 0.0,
    closed: bool = False,
    overlap: float = 0.0,
) -> Geometry:
    """Runs a profile along a polyline in XY at height `z`.

    Corners are not mitred: each leg is extended by `overlap` so the legs
    interpenetrate. A true mitre costs vertices and solves a problem this
    project cannot see - the existing world already closes its lining
    joints by overlapping them rather than by cutting them.
    """
    points = [tuple(point[:2]) for point in path]
    if closed and points and points[0] != points[-1]:
        points.append(points[0])

    legs: list[Geometry] = []
    for start, end in zip(points, points[1:]):
        dx = end[0] - start[0]
        dy = end[1] - start[1]
        length = math.hypot(dx, dy)
        if length <= 1e-6:
            continue

        angle = math.degrees(math.atan2(dy, dx))
        leg = prism(list(profile), length + overlap * 2.0)
        leg = rotated_z(leg, 90.0)
        leg = rotated_z(leg, angle)
        legs.append(translated(leg, (
            (start[0] + end[0]) * 0.5,
            (start[1] + end[1]) * 0.5,
            z,
        )))

    return merge_all(legs) if legs else EMPTY


# ---------------------------------------------------------------- walls


class Opening:
    """A rectangular hole in a wall run, measured along the run.

    `sill` and `head` are heights above the run's own base, so a door is
    an opening whose sill is zero and a window is one whose sill is not.
    """

    def __init__(
        self,
        center: float,
        width: float,
        head: float,
        sill: float = 0.0,
    ) -> None:
        self.center = float(center)
        self.width = float(width)
        self.head = float(head)
        self.sill = float(sill)

    @property
    def start(self) -> float:
        return self.center - self.width * 0.5

    @property
    def end(self) -> float:
        return self.center + self.width * 0.5


def wall_run(
    length: float,
    height: float,
    thickness: float,
    openings: Sequence[Opening] = (),
    chamfer: float = DEFAULT_CHAMFER,
    base: float = 0.0,
) -> Geometry:
    """A straight wall with real openings, centred on the origin along X.

    Built the way a wall is actually built - piers between the holes,
    a lintel over each, an apron under each - rather than by cutting a
    slab. The reveals therefore have thickness and take light, which is
    the entire visual difference from a box with a gap left beside it.
    """
    ordered = sorted(openings, key=lambda item: item.start)
    for first, second in zip(ordered, ordered[1:]):
        if second.start < first.end - 1e-6:
            raise ValueError("Wall openings overlap.")

    half = length * 0.5
    pieces: list[Geometry] = []
    cursor = -half
    for opening in ordered:
        if opening.width <= 0.0 or opening.head <= opening.sill:
            raise ValueError("An opening needs a positive width and head.")
        if opening.start < -half - 1e-6 or opening.end > half + 1e-6:
            raise ValueError("An opening leaves its wall.")

        pier = opening.start - cursor
        if pier > 1e-6:
            pieces.append(chamfered_box(
                ((cursor + opening.start) * 0.5, 0.0, base + height * 0.5),
                (pier, thickness, height),
                chamfer,
            ))

        if opening.sill > 1e-6:
            pieces.append(chamfered_box(
                (opening.center, 0.0, base + opening.sill * 0.5),
                (opening.width, thickness, opening.sill),
                chamfer,
            ))

        lintel = height - opening.head
        if lintel > 1e-6:
            pieces.append(chamfered_box(
                (
                    opening.center,
                    0.0,
                    base + opening.head + lintel * 0.5,
                ),
                (opening.width, thickness, lintel),
                chamfer,
            ))

        cursor = opening.end

    tail = half - cursor
    if tail > 1e-6:
        pieces.append(chamfered_box(
            ((cursor + half) * 0.5, 0.0, base + height * 0.5),
            (tail, thickness, height),
            chamfer,
        ))

    return merge_all(pieces) if pieces else EMPTY


def rectangular_room_walls(
    width: float,
    depth: float,
    height: float,
    thickness: float,
    front_openings: Sequence[Opening] = (),
    back_openings: Sequence[Opening] = (),
    left_openings: Sequence[Opening] = (),
    right_openings: Sequence[Opening] = (),
    chamfer: float = DEFAULT_CHAMFER,
) -> dict[str, Geometry]:
    """The four walls of a box room, each returned separately.

    Separately, because a renderer is the unit a material and a tint are
    applied to, and an interior that wants one wall papered and another
    panelled needs them apart.
    """
    half_width = width * 0.5
    half_depth = depth * 0.5
    return {
        "front": translated(
            wall_run(width, height, thickness, front_openings, chamfer),
            (0.0, -half_depth, 0.0),
        ),
        "back": translated(
            wall_run(width, height, thickness, back_openings, chamfer),
            (0.0, half_depth, 0.0),
        ),
        "left": translated(
            rotated_z(
                wall_run(depth, height, thickness, left_openings, chamfer),
                90.0,
            ),
            (-half_width, 0.0, 0.0),
        ),
        "right": translated(
            rotated_z(
                wall_run(depth, height, thickness, right_openings, chamfer),
                90.0,
            ),
            (half_width, 0.0, 0.0),
        ),
    }


# ---------------------------------------------------------------- slabs


def floor_slab(
    width: float,
    depth: float,
    thickness: float,
    chamfer: float = DEFAULT_CHAMFER,
) -> Geometry:
    """The walking surface, its top edge relieved and its buried one square."""
    return chamfered_box(
        (0.0, 0.0, -thickness * 0.5),
        (width, depth, thickness),
        chamfer,
        top=True,
        bottom=False,
    )


def ceiling_slab(
    width: float,
    depth: float,
    thickness: float,
    height: float,
) -> Geometry:
    return box((0.0, 0.0, height + thickness * 0.5), (width, depth, thickness))


def perimeter_path(
    width: float,
    depth: float,
    inset: float = 0.0,
) -> list[tuple[float, float]]:
    x = width * 0.5 - inset
    y = depth * 0.5 - inset
    return [(-x, -y), (x, -y), (x, y), (-x, y)]


def skirting(
    width: float,
    depth: float,
    height: float = 0.14,
    thickness: float = 0.022,
    wall_thickness: float = 0.0,
) -> Geometry:
    """Runs a skirting round the inside face of a rectangular room."""
    inset = wall_thickness * 0.5
    return sweep(
        perimeter_path(width, depth, inset),
        profile_skirting(height, thickness),
        z=0.0,
        closed=True,
        overlap=thickness,
    )


def cornice(
    width: float,
    depth: float,
    height: float,
    size: float = 0.11,
    wall_thickness: float = 0.0,
) -> Geometry:
    inset = wall_thickness * 0.5
    return sweep(
        perimeter_path(width, depth, inset),
        profile_cornice(size, size * 0.8),
        z=height - size,
        closed=True,
        overlap=size,
    )


# ------------------------------------------------------------- openings


def door_frame(
    opening_width: float,
    opening_height: float,
    wall_thickness: float,
    jamb: float = 0.09,
    architrave: float = 0.0,
    chamfer: float = DEFAULT_CHAMFER,
) -> Geometry:
    """Lining for an opening, centred on the origin, running along X.

    A doorway left as a gap between two boxes has no reveal and reads as
    a hole cut in card. This is the piece that makes it read as a door.
    """
    half = opening_width * 0.5
    depth = wall_thickness + architrave * 2.0
    pieces = [
        chamfered_box(
            (-half - jamb * 0.5, 0.0, opening_height * 0.5),
            (jamb, depth, opening_height),
            chamfer,
        ),
        chamfered_box(
            (half + jamb * 0.5, 0.0, opening_height * 0.5),
            (jamb, depth, opening_height),
            chamfer,
        ),
        chamfered_box(
            (0.0, 0.0, opening_height + jamb * 0.5),
            (opening_width + jamb * 2.0, depth, jamb),
            chamfer,
        ),
    ]
    return merge_all(pieces)


def panelled_leaf(
    width: float,
    height: float,
    thickness: float,
    panels: int = 2,
    stile: float = 0.11,
    relief: float = 0.014,
    chamfer: float = DEFAULT_CHAMFER,
) -> Geometry:
    """A door leaf with recessed panels, hinged on its own -X edge.

    The pivot sits on the hinge stile rather than in the middle, so a
    caller can swing it by rotating about the origin instead of solving
    for an offset - which is the mistake that puts an ajar door through
    its own frame.
    """
    pieces = [chamfered_box(
        (width * 0.5, 0.0, height * 0.5),
        (width, thickness, height),
        chamfer,
    )]

    inner_width = width - stile * 2.0
    if inner_width > 0.05 and panels > 0:
        span = (height - stile * (panels + 1)) / panels
        for index in range(panels):
            base = stile * (index + 1) + span * index
            pieces.append(chamfered_box(
                (
                    width * 0.5,
                    thickness * 0.5 - relief * 0.5,
                    base + span * 0.5,
                ),
                (inner_width, relief, span),
                chamfer,
            ))

    return merge_all(pieces)


# ------------------------------------------------------------ furniture


def counter_run(
    length: float,
    depth: float,
    height: float,
    top_thickness: float = 0.052,
    nosing: float = 0.038,
    plinth_inset: float = 0.075,
    chamfer: float = DEFAULT_CHAMFER,
) -> Geometry:
    """A bar counter: recessed plinth, panelled body, overhanging top.

    Three planes instead of one, which is what gives a counter its own
    shadow line and stops it reading as a crate.
    """
    plinth_height = 0.12
    body_height = height - top_thickness - plinth_height
    pieces = [
        chamfered_box(
            (0.0, 0.0, plinth_height * 0.5),
            (length - 0.02, depth - plinth_inset * 2.0, plinth_height),
            chamfer,
            top=False,
        ),
        chamfered_box(
            (0.0, 0.0, plinth_height + body_height * 0.5),
            (length, depth, body_height),
            chamfer,
        ),
    ]

    top_depth = depth + nosing * 2.0
    top_z = height - top_thickness * 0.5
    pieces.append(chamfered_box(
        (0.0, 0.0, top_z),
        (length + nosing * 2.0, top_depth, top_thickness),
        chamfer * 1.6,
    ))
    return merge_all(pieces)


def table_top(
    width: float,
    depth: float,
    thickness: float,
    height: float,
    chamfer: float = DEFAULT_CHAMFER,
) -> Geometry:
    return chamfered_box(
        (0.0, 0.0, height - thickness * 0.5),
        (width, depth, thickness),
        chamfer * 1.6,
    )


def turned_leg(
    height: float,
    top_radius: float,
    waist_radius: float,
    foot_radius: float,
    segments: int = 10,
) -> Geometry:
    """A leg with a waist and a foot - four sections, not a cylinder."""
    return lathe(
        [
            (foot_radius, 0.0),
            (foot_radius, height * 0.045),
            (waist_radius, height * 0.12),
            (waist_radius * 0.86, height * 0.55),
            (top_radius, height * 0.9),
            (top_radius, height),
        ],
        segments,
    )


def beam(
    length: float,
    width: float,
    height: float,
    chamfer: float = DEFAULT_CHAMFER,
) -> Geometry:
    """A ceiling beam, chamfered on its two visible bottom arrises."""
    return chamfered_box(
        (0.0, 0.0, 0.0),
        (width, length, height),
        chamfer,
        top=False,
        bottom=True,
    )
