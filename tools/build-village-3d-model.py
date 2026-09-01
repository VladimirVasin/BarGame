#!/usr/bin/env python3
"""Build the authored village library for the area above the cableway.

The library contains local-space archetypes, never world placements.  The
runtime's ``AlpineVillagePlotDescriptor`` remains the owner of position,
facing, footprint, height, stable ID and collision.  Every assembly occupies
the normalized descriptor cube: its origin is the descriptor centre, its
ground plane is source ``Z=-0.5`` (Unity ``Y=-0.5``), and Unity scales it
component-wise by the plot size.

It deliberately ships **no doors and no window panes**.  Both scale with the
descriptor, and the plots run from a four-metre cottage to the seven-metre
house at the top of the lane - a door normalized into that cube is either a
hatch or a barn opening.  They are drawn at real metre scale by the world
builder instead, which is the church's doctrine: the imported model owns mass
and material, the plan owns every opening a person uses.

It also introduces **no new surface sheet**.  Art bible 10g requires the
village to raise no new material family, so every part here wears one of the
fifteen the mountain area already prints - timber, masonry, layered stone,
rusted iron, bark.  What makes the village warm is its light, not its
substance.

Source space is Blender metres, X right, +Y forward, Z up.  The FBX export
bakes axes and units so bare mesh sub-assets import into Unity with X right,
+Z forward and Y up.  Every visible material role is a separate mesh because
the runtime combines meshes per surface/tint bucket.

Run through Blender 5 from the repository root::

    blender --background --factory-startup --python       tools/build-village-3d-model.py

    blender --background --factory-startup --python       tools/build-village-3d-model.py -- --validate-only
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error


ROOT = Path(__file__).resolve().parents[1]
GENERATOR_VERSION = "3.0.0"
DESIGN_ID = "village_house_archetypes_v3"
DISPLAY_NAME = "Village House Archetypes 3.0.0"

DEFAULT_BLEND = (
    ROOT / "ArtSource" / "Village" / "Blender" / "Village3D.blend"
)
DEFAULT_PREVIEW = (
    ROOT / "ArtSource" / "Village" / "Blender" / "Village3D.png"
)
DEFAULT_FBX = (
    ROOT / "Assets" / "Village" / "Models" / "Village3D.fbx"
)
DEFAULT_MANIFEST = (
    ROOT / "Assets" / "Village" / "Models" / "Village3D.json"
)

SOURCE_COLLECTION = "SOURCE_Village3D"
PRESENTATION_COLLECTION = "PRESENTATION_Village3D"
ROOT_NAME = "ROOT_Village3D"

NORMALIZED_MIN = (-0.5, -0.5, -0.5)
NORMALIZED_MAX = (0.5, 0.5, 0.5)
BOUNDS_EPSILON = 1e-6
UV_EPSILON = 1e-6
SIGNED_VOLUME_EPSILON = 1e-9
MAX_TRIANGLES = 16000

Vec2 = tuple[float, float]
Vec3 = tuple[float, float, float]
Face = tuple[int, ...]
Geometry = tuple[list[Vec3], list[Face]]


PREVIEW_COLORS = {
    "HouseWallA": (0.33, 0.185, 0.105, 1.0),
    "HouseWallB": (0.28, 0.155, 0.088, 1.0),
    "HouseWallC": (0.37, 0.215, 0.120, 1.0),
    "HouseWallD": (0.30, 0.170, 0.098, 1.0),
    "HouseRoof": (0.150, 0.115, 0.090, 1.0),
    "RoofSnow": (0.690, 0.710, 0.690, 1.0),
    "HousePlinth": (0.235, 0.225, 0.200, 1.0),
    "HouseChimney": (0.265, 0.215, 0.185, 1.0),
    "TopHouseWall": (0.425, 0.385, 0.315, 1.0),
    "TopHouseTimber": (0.285, 0.160, 0.090, 1.0),
    "ChapelWhitewash": (0.560, 0.525, 0.455, 1.0),
    "ChapelRoof": (0.135, 0.105, 0.082, 1.0),
    "CartIron": (0.215, 0.105, 0.048, 1.0),
    "CartWheel": (0.120, 0.072, 0.040, 1.0),
    "AditTimber": (0.225, 0.135, 0.075, 1.0),
    "AditRubble": (0.195, 0.190, 0.170, 1.0),
    "GraveStone": (0.290, 0.285, 0.258, 1.0),
    "Firewood": (0.205, 0.120, 0.062, 1.0),
    "ShutterA": (0.225, 0.255, 0.205, 1.0),
    "ShutterB": (0.285, 0.180, 0.125, 1.0),
    "ShutterC": (0.185, 0.220, 0.235, 1.0),
    "RepairWhitewash": (0.470, 0.435, 0.365, 1.0),
    "RepairIron": (0.170, 0.095, 0.052, 1.0),
    "FenceTimber": (0.205, 0.125, 0.072, 1.0),
    "MineCable": (0.125, 0.070, 0.042, 1.0),
    "RailIron": (0.155, 0.082, 0.044, 1.0),
    "RailSleeper": (0.185, 0.115, 0.065, 1.0),
    "SourceStone": (0.300, 0.295, 0.270, 1.0),
}

# Representative plot sizes in source axes (X, Y-forward, Z-up).  The source
# meshes stay normalized; only the review copies use these ratios, otherwise a
# grave marker reads the size of a house in the contact sheet.
PREVIEW_DESCRIPTOR_SIZES = {
    "House": (7.6, 6.6, 5.4),
    "Chapel": (5.0, 6.5, 4.2),
    "MineCart": (1.05, 1.70, 1.00),
    "AditFrame": (3.10, 0.90, 2.60),
    "GraveMarker": (0.42, 0.18, 0.80),
    "Firewood": (1.60, 0.95, 0.90),
    "TopHouse": (11.0, 9.0, 7.0),
    "FacadeDetail": (1.85, 0.18, 1.25),
    "GarlandPost": (0.38, 0.38, 3.10),
    "CableGate": (3.20, 0.42, 1.25),
    "RailBridge": (1.35, 3.20, 0.32),
    "SourceBowl": (1.15, 0.75, 0.55),
}


@dataclass(frozen=True)
class PartSpec:
    mesh: str
    kind: str
    variant: int
    part_role: str
    surface_kind: str
    tint_role: str
    geometry: Geometry


@dataclass(frozen=True)
class AssemblySpec:
    kind: str
    variant: int
    scale_mode: str
    parts: tuple[PartSpec, ...]


@dataclass
class BuildResult:
    root: bpy.types.Object
    source: bpy.types.Collection
    presentation: bpy.types.Collection
    assemblies: tuple[AssemblySpec, ...]
    objects: dict[str, bpy.types.Object]
    materials: dict[str, bpy.types.Material]


def stable(value: float) -> float:
    return round(float(value) + 0.0, 6)


def add(a: Vec3, b: Vec3) -> Vec3:
    return a[0] + b[0], a[1] + b[1], a[2] + b[2]


def subtract(a: Vec3, b: Vec3) -> Vec3:
    return a[0] - b[0], a[1] - b[1], a[2] - b[2]


def multiply(vector: Vec3, amount: float) -> Vec3:
    return vector[0] * amount, vector[1] * amount, vector[2] * amount


def dot(a: Vec3, b: Vec3) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a: Vec3, b: Vec3) -> Vec3:
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def length(vector: Vec3) -> float:
    return math.sqrt(dot(vector, vector))


def normalized(vector: Vec3) -> Vec3:
    magnitude = length(vector)
    if magnitude <= 1e-10:
        raise ValueError("Cannot normalize a zero vector.")
    return multiply(vector, 1.0 / magnitude)


def merge(*geometries: Geometry) -> Geometry:
    vertices: list[Vec3] = []
    faces: list[Face] = []
    for item_vertices, item_faces in geometries:
        offset = len(vertices)
        vertices.extend(item_vertices)
        faces.extend(tuple(index + offset for index in face)
                     for face in item_faces)
    return vertices, faces


def translated(geometry: Geometry, offset: Vec3) -> Geometry:
    vertices, faces = geometry
    return [add(vertex, offset) for vertex in vertices], list(faces)


def grounded(geometry: Geometry) -> Geometry:
    """Places the authored lowest vertex on the normalized ground plane."""
    low, _ = geometry_bounds(geometry)
    return translated(geometry, (0.0, 0.0, -0.5 - low[2]))


def ground_all(*geometries: Geometry) -> list[Geometry]:
    """Drop several parts by ONE shared offset so the assembly's lowest point
    is exactly Z=-0.5.

    Grounding each part on its own would put the roof on the floor; this is
    the multi-part form, and it is needed wherever the lowest thing in an
    assembly is a curve rather than a flat face - an inscribed polygon never
    quite reaches the radius its author had in mind.
    """
    lowest = min(
        vertex[2]
        for vertices, _ in geometries
        for vertex in vertices)
    offset = -0.5 - lowest
    return [translated(geometry, (0.0, 0.0, offset))
            for geometry in geometries]


def box(center: Vec3, size: Vec3) -> Geometry:
    cx, cy, cz = center
    hx, hy, hz = (value * 0.5 for value in size)
    vertices = [
        (cx - hx, cy - hy, cz - hz),
        (cx + hx, cy - hy, cz - hz),
        (cx + hx, cy + hy, cz - hz),
        (cx - hx, cy + hy, cz - hz),
        (cx - hx, cy - hy, cz + hz),
        (cx + hx, cy - hy, cz + hz),
        (cx + hx, cy + hy, cz + hz),
        (cx - hx, cy + hy, cz + hz),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7),
        (0, 1, 5, 4), (1, 2, 6, 5),
        (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    return vertices, faces


def chamfered_box(
    center: Vec3,
    size: Vec3,
    chamfer: float = 0.025,
) -> Geometry:
    """Low-poly worked box with top and bottom highlight arrises."""
    cx, cy, cz = center
    hx, hy, hz = (value * 0.5 for value in size)
    cut = min(chamfer, hx * 0.45, hy * 0.45, hz * 0.45)
    if cut <= 1e-6:
        return box(center, size)

    levels = (
        (cz - hz, cut),
        (cz - hz + cut, 0.0),
        (cz + hz - cut, 0.0),
        (cz + hz, cut),
    )
    vertices: list[Vec3] = []
    rings: list[tuple[int, int, int, int]] = []
    for z, inset in levels:
        start = len(vertices)
        x = hx - inset
        y = hy - inset
        vertices.extend((
            (cx - x, cy - y, z), (cx + x, cy - y, z),
            (cx + x, cy + y, z), (cx - x, cy + y, z),
        ))
        rings.append((start, start + 1, start + 2, start + 3))

    faces: list[Face] = [
        tuple(reversed(rings[0])), rings[-1],
    ]
    for lower, upper in zip(rings, rings[1:]):
        for side in range(4):
            following = (side + 1) % 4
            faces.append((
                lower[side], lower[following],
                upper[following], upper[side],
            ))
    return vertices, faces


def prism_y(profile_xz: Sequence[tuple[float, float]], y0: float,
            y1: float) -> Geometry:
    count = len(profile_xz)
    vertices = [(x, y0, z) for x, z in profile_xz]
    vertices.extend((x, y1, z) for x, z in profile_xz)
    faces: list[Face] = [
        tuple(range(count)),
        tuple(reversed(range(count, count * 2))),
    ]
    for index in range(count):
        following = (index + 1) % count
        faces.append((
            index, count + index, count + following, following,
        ))
    return vertices, faces


def tube_between(
    start: Vec3,
    end: Vec3,
    start_radius: float,
    end_radius: float,
    sides: int = 8,
) -> Geometry:
    axis = normalized(subtract(end, start))
    helper = (0.0, 0.0, 1.0)
    if abs(dot(axis, helper)) > 0.92:
        helper = (1.0, 0.0, 0.0)
    first = normalized(cross(axis, helper))
    second = normalized(cross(axis, first))
    vertices: list[Vec3] = []
    for center, radius in ((start, start_radius), (end, end_radius)):
        for side in range(sides):
            angle = side / sides * math.tau
            radial = add(
                multiply(first, math.cos(angle) * radius),
                multiply(second, math.sin(angle) * radius),
            )
            vertices.append(add(center, radial))
    faces: list[Face] = [tuple(reversed(range(sides)))]
    faces.append(tuple(range(sides, sides * 2)))
    for side in range(sides):
        following = (side + 1) % sides
        faces.append((
            side, following, sides + following, sides + side,
        ))
    return vertices, faces


def irregular_tube_y(
    stations: Sequence[tuple[float, float, float, float, float, float]],
    sides: int = 9,
) -> Geometry:
    """Rings are (y, centre-x, centre-z, radius-x, radius-z, phase)."""
    vertices: list[Vec3] = []
    rings: list[tuple[int, ...]] = []
    for y, cx, cz, rx, rz, phase in stations:
        start = len(vertices)
        for side in range(sides):
            angle = side / sides * math.tau + phase
            jitter = 0.94 + ((side * 7 + len(rings) * 11) % 5) * 0.025
            vertices.append((
                cx + math.cos(angle) * rx * jitter,
                y,
                cz + math.sin(angle) * rz * (1.02 - (jitter - 0.94)),
            ))
        rings.append(tuple(range(start, start + sides)))

    faces: list[Face] = [tuple(reversed(rings[0])), rings[-1]]
    for lower, upper in zip(rings, rings[1:]):
        for side in range(sides):
            following = (side + 1) % sides
            faces.append((
                lower[side], lower[following],
                upper[following], upper[side],
            ))
    return vertices, faces


def irregular_tube_z(
    rings_data: Sequence[tuple[float, float, float, float, float, float]],
    sides: int = 9,
) -> Geometry:
    """Rings are (z, centre-x, centre-y, radius-x, radius-y, phase)."""
    vertices: list[Vec3] = []
    rings: list[tuple[int, ...]] = []
    for z, cx, cy, rx, ry, phase in rings_data:
        start = len(vertices)
        for side in range(sides):
            angle = side / sides * math.tau + phase
            jitter = 0.94 + ((side * 5 + len(rings) * 13) % 6) * 0.018
            vertices.append((
                cx + math.cos(angle) * rx * jitter,
                cy + math.sin(angle) * ry * (1.02 - (jitter - 0.94)),
                z,
            ))
        rings.append(tuple(range(start, start + sides)))

    faces: list[Face] = [tuple(reversed(rings[0])), rings[-1]]
    for lower, upper in zip(rings, rings[1:]):
        for side in range(sides):
            following = (side + 1) % sides
            faces.append((
                lower[side], lower[following],
                upper[following], upper[side],
            ))
    return vertices, faces


def annulus_y(
    center: Vec3,
    outer_x: float,
    outer_z: float,
    inner_x: float,
    inner_z: float,
    depth: float,
    sides: int = 18,
) -> Geometry:
    cx, cy, cz = center
    vertices: list[Vec3] = []
    rings: list[tuple[int, ...]] = []
    for y in (cy - depth * 0.5, cy + depth * 0.5):
        for rx, rz in ((outer_x, outer_z), (inner_x, inner_z)):
            start = len(vertices)
            vertices.extend((
                cx + math.cos(side / sides * math.tau) * rx,
                y,
                cz + math.sin(side / sides * math.tau) * rz,
            ) for side in range(sides))
            rings.append(tuple(range(start, start + sides)))
    back_outer, back_inner, front_outer, front_inner = rings
    faces: list[Face] = []
    for side in range(sides):
        following = (side + 1) % sides
        faces.extend((
            (back_outer[side], back_outer[following],
             front_outer[following], front_outer[side]),
            (back_inner[following], back_inner[side],
             front_inner[side], front_inner[following]),
            (back_outer[following], back_outer[side],
             back_inner[side], back_inner[following]),
            (front_outer[side], front_outer[following],
             front_inner[following], front_inner[side]),
        ))
    return vertices, faces


def convex_disc_y(
    center: Vec3,
    radius_x: float,
    radius_z: float,
    sides: int = 18,
) -> Geometry:
    cx, cy, cz = center
    vertices: list[Vec3] = []
    outer: list[int] = []
    middle: list[int] = []
    back: list[int] = []
    for side in range(sides):
        angle = side / sides * math.tau
        cos = math.cos(angle)
        sin = math.sin(angle)
        outer.append(len(vertices))
        vertices.append((cx + cos * radius_x, cy - 0.015,
                         cz + sin * radius_z))
        middle.append(len(vertices))
        vertices.append((cx + cos * radius_x * 0.58, cy - 0.055,
                         cz + sin * radius_z * 0.58))
        back.append(len(vertices))
        vertices.append((cx + cos * radius_x, cy + 0.018,
                         cz + sin * radius_z))
    front_center = len(vertices)
    vertices.append((cx, cy - 0.075, cz))
    back_center = len(vertices)
    vertices.append((cx, cy + 0.018, cz))

    faces: list[Face] = []
    for side in range(sides):
        following = (side + 1) % sides
        faces.extend((
            (outer[side], outer[following],
             middle[following], middle[side]),
            (middle[side], middle[following], front_center),
            (back[following], back[side], back_center),
            (outer[following], outer[side],
             back[side], back[following]),
        ))
    return vertices, faces


def triangular_root(angle: float, reach: float, width: float,
                    height: float) -> Geometry:
    tangent = (-math.sin(angle), math.cos(angle))
    outward = (math.cos(angle), math.sin(angle))
    inner = 0.24
    points = [
        (outward[0] * inner + tangent[0] * width,
         outward[1] * inner + tangent[1] * width, -0.5),
        (outward[0] * inner - tangent[0] * width,
         outward[1] * inner - tangent[1] * width, -0.5),
        (outward[0] * reach,
         outward[1] * reach, -0.5),
        (outward[0] * inner, outward[1] * inner, -0.5 + height),
    ]
    faces = [
        (0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3),
    ]
    return points, faces


HOUSE_ARCHETYPE_COUNT = 2
HOUSE_WALL_TINTS = ("HouseWallA", "HouseWallB")
HOUSE_FACADE_PLANES = {
    ("House", 0): 0.405,
    ("House", 1): 0.430,
    ("TopHouse", 0): 0.415,
}
HOUSE_FACADE_SUPPORT_Z = (-0.420, -0.160, 0.050)


def gable_profile(
    left_eave: float,
    right_eave: float,
    apex_x: float,
    lean: float,
    apex_z: float = 0.40,
    half_width: float = 0.46,
) -> tuple[tuple[float, float], ...]:
    """The front elevation of a crooked house, counter-clockwise in XZ.

    Extruded along Y this is the whole shell.  The crookedness lives in the
    profile rather than in a rotation, because a rotated box stops being a
    box the moment two of them have to meet at a ridge.
    """
    return (
        (-half_width, -0.5),
        (half_width, -0.5),
        (half_width + lean, right_eave),
        (apex_x, apex_z),
        (-half_width + lean, left_eave),
    )


def roof_surface_height(
    x: float,
    left_x: float,
    left_z: float,
    apex_x: float,
    apex_z: float,
    right_x: float,
    right_z: float,
) -> float:
    """Height of one broken roof line, used by its lying snow skin."""
    if x <= apex_x:
        amount = (x - left_x) / (apex_x - left_x)
        return left_z + (apex_z - left_z) * amount
    amount = (x - apex_x) / (right_x - apex_x)
    return apex_z + (right_z - apex_z) * amount


def sloped_skin(
    x0: float,
    x1: float,
    y0: float,
    y1: float,
    height_at,
    thickness: float = 0.014,
    far_left_inset: float = 0.0,
    far_right_inset: float = 0.0,
) -> Geometry:
    """A thin, optionally ragged layer following rather than replacing roof."""
    lift = 0.004
    corners = (
        (x0, y0, height_at(x0) + lift),
        (x1, y0, height_at(x1) + lift),
        (x1 - far_right_inset, y1,
         height_at(x1 - far_right_inset) + lift),
        (x0 + far_left_inset, y1,
         height_at(x0 + far_left_inset) + lift),
    )
    vertices = list(corners)
    vertices.extend(
        (x, y, z + thickness) for x, y, z in corners)
    faces = [
        (3, 2, 1, 0),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7),
    ]
    return vertices, faces


def broken_roof_snow(
    left_x: float,
    left_z: float,
    apex_x: float,
    apex_z: float,
    right_x: float,
    right_z: float,
    depth: float,
    pattern: int,
) -> Geometry:
    """Wind-broken roof cover: one lee patch and two incomplete remnants.

    A continuous white cap would turn the village into a postcard.  These
    deterministic strips leave timber exposed at the ridge, eaves and one
    end of every roof, while still making settled snow legible at lane range.
    """
    def height_at(x: float) -> float:
        return roof_surface_height(
            x,
            left_x,
            left_z,
            apex_x,
            apex_z,
            right_x,
            right_z)

    left_span = apex_x - left_x
    right_span = right_x - apex_x
    if pattern % 2 == 0:
        primary = (
            left_x + left_span * 0.08,
            apex_x - left_span * 0.08,
        )
        remnant = (
            apex_x + right_span * 0.52,
            right_x - right_span * 0.08,
        )
    else:
        primary = (
            apex_x + right_span * 0.08,
            right_x - right_span * 0.08,
        )
        remnant = (
            left_x + left_span * 0.08,
            apex_x - left_span * 0.54,
        )

    return merge(
        sloped_skin(
            primary[0], primary[1],
            -depth * 0.92, depth * 0.20,
            height_at,
            far_left_inset=(primary[1] - primary[0]) * 0.07,
            far_right_inset=(primary[1] - primary[0]) * 0.04),
        sloped_skin(
            primary[0] + (primary[1] - primary[0]) * 0.22,
            primary[1] - (primary[1] - primary[0]) * 0.06,
            depth * 0.34, depth * 0.88,
            height_at,
            0.011,
            far_left_inset=(primary[1] - primary[0]) * 0.05,
            far_right_inset=(primary[1] - primary[0]) * 0.11),
        sloped_skin(
            remnant[0], remnant[1],
            -depth * 0.28, depth * 0.58,
            height_at,
            0.010,
            far_left_inset=(remnant[1] - remnant[0]) * 0.16,
            far_right_inset=(remnant[1] - remnant[0]) * 0.03),
    )


def gabled_roof(
    left_x: float,
    left_eave: float,
    apex_x: float,
    apex_z: float,
    right_x: float,
    right_eave: float,
    half_depth: float,
    thickness: float,
    snow_pattern: int,
) -> tuple[Geometry, Geometry]:
    """One old roof and its discontinuous lee-side snow cover."""
    eave_drop = 0.038
    left_base = left_eave - eave_drop
    right_base = right_eave - eave_drop
    roof = merge(
        prism_y(
            (
                (left_x, left_base),
                (apex_x, apex_z),
                (apex_x, apex_z + thickness),
                (left_x, left_base + thickness),
            ),
            -half_depth,
            half_depth),
        prism_y(
            (
                (apex_x, apex_z),
                (right_x, right_base),
                (right_x, right_base + thickness),
                (apex_x, apex_z + thickness),
            ),
            -half_depth,
            half_depth),
        chamfered_box(
            (apex_x, 0.0, apex_z + thickness * 0.5),
            (0.072, half_depth * 2.0, thickness * 0.88),
            0.012),
    )
    snow = broken_roof_snow(
        left_x,
        left_base + thickness,
        apex_x,
        apex_z + thickness,
        right_x,
        right_base + thickness,
        half_depth,
        snow_pattern)
    return roof, snow


def build_heide_house() -> AssemblySpec:
    """Late-medieval Heidehüs: timber-heavy, squat and irregular.

    The reference's identity is structural rather than decorative: a dark
    block rises from a serious stone foot, the gable stays low and log ends
    remain legible on both side walls.  Doors and windows are deliberately
    absent because the runtime still owns those at real human scale.
    """
    variant = 0
    wall_depth = 0.405
    left_eave = 0.118
    right_eave = 0.098
    apex_x = -0.042
    apex_z = 0.355
    walls = prism_y(
        (
            (-0.455, -0.285),
            (0.455, -0.285),
            (0.462, right_eave),
            (apex_x, apex_z),
            (-0.448, left_eave),
        ),
        -wall_depth,
        wall_depth)

    # Short projecting log ends break both side elevations without disturbing
    # the flat +/-Y facade planes used by plan-owned doors and windows.
    log_ends: list[Geometry] = []
    for side in (-1.0, 1.0):
        x = side * 0.456
        for index, y in enumerate((-0.29, -0.10, 0.10, 0.29)):
            z = -0.205 + index * 0.092
            log_ends.append(chamfered_box(
                (x, y, z),
                (0.026, 0.115, 0.024),
                0.005))
    walls = merge(walls, *log_ends)

    roof, snow = gabled_roof(
        -0.5,
        left_eave,
        apex_x,
        apex_z,
        0.5,
        right_eave,
        0.465,
        0.052,
        variant)

    # The deep base is the lower storey, not a token course under a box.  Its
    # central forward stone closes the real facade plane behind the door.
    plinth = merge(
        chamfered_box(
            (0.0, 0.0, -0.385),
            (0.940, 0.790, 0.230),
            0.018),
        chamfered_box(
            (0.0, 0.395, -0.385),
            (0.340, 0.020, 0.230),
            0.005),
        chamfered_box(
            (-0.325, -0.385, -0.392),
            (0.205, 0.040, 0.180),
            0.008),
    )

    chimney = merge(
        chamfered_box(
            (0.205, -0.185, 0.330),
            (0.112, 0.120, 0.330),
            0.013),
        chamfered_box(
            (0.205, -0.185, 0.485),
            (0.148, 0.154, 0.030),
            0.009),
    )
    return village_house_assembly(
        variant, walls, roof, plinth, chimney, snow)


def build_untergommer_house() -> AssemblySpec:
    """Untergoms Renaissance house: high stone base, timber upper block.

    The upper floor projects laterally over the narrower hall/cellar storey.
    Three pairs of square timber brackets make that construction readable at
    lane range while the front and rear wall planes remain continuous.
    """
    variant = 1
    wall_depth = 0.430
    left_eave = 0.154
    right_eave = 0.132
    apex_x = 0.052
    apex_z = 0.405
    walls = prism_y(
        (
            (-0.455, -0.105),
            (0.455, -0.105),
            (0.448, right_eave),
            (apex_x, apex_z),
            (-0.462, left_eave),
        ),
        -wall_depth,
        wall_depth)

    brackets: list[Geometry] = []
    for side in (-1.0, 1.0):
        for y in (-0.285, 0.0, 0.285):
            brackets.append(tube_between(
                (side * 0.385, y, -0.205),
                (side * 0.452, y, -0.072),
                0.014,
                0.014,
                4))
    walls = merge(walls, *brackets)

    roof, snow = gabled_roof(
        -0.5,
        left_eave,
        apex_x,
        apex_z,
        0.5,
        right_eave,
        0.480,
        0.045,
        variant)

    # The masonry storey reaches the facade plane.  Narrow side piers support
    # the overhanging timber and keep the plan-owned side windows grounded in
    # a real wall rather than hanging outside the recessed base.
    plinth = merge(
        chamfered_box(
            (0.0, 0.0, -0.2925),
            (0.790, 0.860, 0.415),
            0.018),
        chamfered_box(
            (-0.430, 0.0, -0.2925),
            (0.090, 0.350, 0.415),
            0.014),
        chamfered_box(
            (0.430, 0.0, -0.2925),
            (0.090, 0.350, 0.415),
            0.014),
        chamfered_box(
            (0.0, 0.0, -0.470),
            (0.890, 0.890, 0.060),
            0.012),
    )

    chimney = merge(
        chamfered_box(
            (-0.185, -0.205, 0.340),
            (0.118, 0.125, 0.310),
            0.013),
        chamfered_box(
            (-0.185, -0.205, 0.485),
            (0.154, 0.160, 0.030),
            0.009),
    )
    return village_house_assembly(
        variant, walls, roof, plinth, chimney, snow)


def village_house_assembly(
    variant: int,
    walls: Geometry,
    roof: Geometry,
    plinth: Geometry,
    chimney: Geometry,
    snow: Geometry,
) -> AssemblySpec:
    tint = HOUSE_WALL_TINTS[variant]
    return AssemblySpec(
        "House", variant, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_House_Variant%02d_Walls" % variant,
                     "House", variant, "Walls", "Timber", tint, walls),
            PartSpec("GEO_VIL_House_Variant%02d_Roof" % variant,
                     "House", variant, "Roof", "Timber", "HouseRoof", roof),
            PartSpec("GEO_VIL_House_Variant%02d_Plinth" % variant,
                     "House", variant, "Plinth", "LayeredStone",
                     "HousePlinth", plinth),
            PartSpec("GEO_VIL_House_Variant%02d_Chimney" % variant,
                     "House", variant, "Chimney", "Masonry",
                     "HouseChimney", chimney),
            PartSpec("GEO_VIL_House_Variant%02d_Snow" % variant,
                     "House", variant, "Snow", "WindSnow",
                     "RoofSnow", snow),
        ),
    )


def build_village_house(variant: int) -> AssemblySpec:
    if variant == 0:
        return build_heide_house()
    if variant == 1:
        return build_untergommer_house()
    raise ValueError(f"Unsupported village house archetype {variant}")


def build_chapel() -> AssemblySpec:
    """The cover over the source.

    Deliberately the plainest thing in the kit.  No cross, no bell, no niche,
    no carving: story bible 12 is explicit that it stands over the spring the
    way a cover stands over a well, and every ornament anyone adds here turns
    it into the shrine the whole section forbids.
    """
    depth = 0.40
    walls = prism_y(
        gable_profile(0.115, 0.095, 0.020, 0.008, 0.34, 0.44),
        -depth,
        depth)

    thickness = 0.06
    roof = merge(
        prism_y(
            (
                (-0.49, 0.070),
                (0.020, 0.34),
                (0.020, 0.34 + thickness),
                (-0.49, 0.070 + thickness),
            ),
            -depth - 0.055,
            depth + 0.055),
        prism_y(
            (
                (0.020, 0.34),
                (0.49, 0.050),
                (0.49, 0.050 + thickness),
                (0.020, 0.34 + thickness),
            ),
            -depth - 0.055,
            depth + 0.055),
        chamfered_box((0.020, 0.0, 0.34 + thickness * 0.5),
                      (0.07, (depth + 0.055) * 2.0, thickness * 0.9), 0.012),
    )
    snow = broken_roof_snow(
        -0.49, 0.070 + thickness,
        0.020, 0.34 + thickness,
        0.49, 0.050 + thickness,
        depth + 0.055,
        4)

    plinth = merge(
        chamfered_box((0.0, 0.0, -0.5 + 0.045),
                      (0.955, depth * 2.0 + 0.06, 0.09), 0.018),
        # The pipe leaves the north wall at ankle height and goes downhill.
        # It is the only thing here that says what the building is for.
        tube_between((0.0, -depth - 0.01, -0.40),
                     (0.0, -depth - 0.05, -0.42), 0.033, 0.031, 7),
    )

    return AssemblySpec(
        "Chapel", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_Chapel_Walls", "Chapel", 0, "Walls",
                     "Masonry", "ChapelWhitewash", walls),
            PartSpec("GEO_VIL_Chapel_Roof", "Chapel", 0, "Roof",
                     "Timber", "ChapelRoof", roof),
            PartSpec("GEO_VIL_Chapel_Plinth", "Chapel", 0, "Plinth",
                     "LayeredStone", "HousePlinth", plinth),
            PartSpec("GEO_VIL_Chapel_Snow", "Chapel", 0, "Snow",
                     "WindSnow", "RoofSnow", snow),
        ),
    )


def build_mine_cart() -> AssemblySpec:
    """Standing in a yard with firewood in it.

    An ore cart used as a woodshed - story bible 12's rule for the whole mine,
    that its leftovers are village utensils now and never an industrial
    landscape.
    """
    floor = chamfered_box((0.0, 0.0, -0.085), (0.72, 0.94, 0.075), 0.016)
    sides = [
        chamfered_box((-0.345, 0.0, 0.115), (0.055, 0.94, 0.42), 0.014),
        chamfered_box((0.345, 0.0, 0.115), (0.055, 0.94, 0.42), 0.014),
        chamfered_box((0.0, -0.455, 0.115), (0.74, 0.055, 0.42), 0.014),
        chamfered_box((0.0, 0.455, 0.115), (0.74, 0.055, 0.42), 0.014),
    ]
    # One corner is stoved in; a cart nobody repairs is the point.
    dent = chamfered_box((0.330, 0.355, 0.245), (0.09, 0.20, 0.13), 0.012)
    body = merge(floor, *sides, dent)

    wheels = []
    for y in (-0.30, 0.30):
        wheels.append(tube_between(
            (-0.40, y, -0.34), (0.40, y, -0.34), 0.048, 0.048, 7))
        for x in (-0.375, 0.375):
            wheels.append(tube_between(
                (x - 0.035, y, -0.34), (x + 0.035, y, -0.34),
                0.160, 0.160, 10))
    body, wheel_set = ground_all(body, merge(*wheels))
    return AssemblySpec(
        "MineCart", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_MineCart_Body", "MineCart", 0, "Body",
                     "RustedIron", "CartIron", body),
            PartSpec("GEO_VIL_MineCart_Wheels", "MineCart", 0, "Wheels",
                     "RustedIron", "CartWheel", wheel_set),
        ),
    )


def build_adit_frame() -> AssemblySpec:
    """A hole in the slope behind the houses, and the timber holding it open.

    There is no building here on purpose.  The mine is a mouth, an overgrown
    spoil heap and nothing else.
    """
    posts = [
        tube_between((-0.34, 0.0, -0.492), (-0.315, 0.0, 0.30),
                     0.085, 0.078, 7),
        tube_between((0.34, 0.02, -0.492), (0.325, 0.02, 0.31),
                     0.085, 0.078, 7),
        chamfered_box((0.005, 0.0, 0.345), (0.80, 0.155, 0.10), 0.018),
        # A second, older lintel behind the first, half rotted away.
        chamfered_box((-0.05, 0.20, 0.29), (0.52, 0.10, 0.070), 0.014),
    ]
    rubble = merge(
        chamfered_box((-0.375, 0.02, -0.375), (0.23, 0.62, 0.25), 0.026),
        chamfered_box((0.385, -0.08, -0.395), (0.21, 0.54, 0.21), 0.024),
        chamfered_box((0.03, 0.34, -0.430), (0.52, 0.26, 0.14), 0.020),
        chamfered_box((-0.14, -0.29, -0.455), (0.30, 0.20, 0.09), 0.016),
    )
    timber, rubble = ground_all(merge(*posts), rubble)
    return AssemblySpec(
        "AditFrame", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_AditFrame_Timber", "AditFrame", 0, "Timber",
                     "Timber", "AditTimber", timber),
            PartSpec("GEO_VIL_AditFrame_Rubble", "AditFrame", 0, "Rubble",
                     "LayeredStone", "AditRubble", rubble),
        ),
    )


GRAVE_VARIANTS = (
    # lean, top taper, shoulder height
    (0.030, 0.86, 0.30),
    (-0.055, 0.72, 0.16),
    (0.012, 0.94, 0.40),
)


def build_grave_marker(variant: int) -> AssemblySpec:
    lean, taper, shoulder = GRAVE_VARIANTS[variant]
    stone = prism_y(
        (
            (-0.42, -0.5),
            (0.42, -0.5),
            (0.42 * taper + lean, shoulder),
            (0.0 + lean * 1.6, 0.5),
            (-0.42 * taper + lean, shoulder),
        ),
        -0.30,
        0.30)
    footing = chamfered_box(
        (0.0, 0.0, -0.5 + 0.045), (0.96, 0.72, 0.09), 0.016)
    return AssemblySpec(
        "GraveMarker", variant, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_GraveMarker_Variant%02d_Stone" % variant,
                     "GraveMarker", variant, "Stone", "LayeredStone",
                     "GraveStone", merge(stone, footing)),
        ),
    )


def build_firewood() -> AssemblySpec:
    """A split stack.  Goes in the cart, or against a wall."""
    logs = []
    rows = ((-0.34, 5), (-0.10, 5), (0.14, 4), (0.36, 3))
    for row_index, row in enumerate(rows):
        z, count = row
        span = 0.72
        for index in range(count):
            amount = 0.0 if count == 1 else index / (count - 1.0)
            x = -span * 0.5 + span * amount
            # Alternate rows are stacked the other way about, as a real stack
            # is, so the ends of the logs face out on every other course.
            wobble = 0.018 if (row_index + index) % 2 == 0 else -0.014
            logs.append(tube_between(
                (x + wobble, -0.46, z),
                (x - wobble * 0.5, 0.46, z),
                0.098, 0.092, 7))
    return AssemblySpec(
        "Firewood", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_Firewood_Wood", "Firewood", 0, "Wood",
                     "BarkAndDeadwood", "Firewood",
                     grounded(merge(*logs))),
        ),
    )


def build_top_house() -> AssemblySpec:
    """Jost-Sigristen-inspired terminal house, stripped of prestige signs.

    A broad cared-for timber block remains the lane's culmination.  The low
    weathered masonry wing on local +X makes it a third architectural type,
    not a larger copy of either cottage.  There is no fresco, heraldry,
    balcony or museum ornament: mass and upkeep do all the work.

    The five-role import contract is intentionally retained.  ``Walls`` is
    the timber main block; the masonry material bucket called ``Chimney``
    carries both the closed side wing and its actual chimney.
    """
    left_eave = 0.108
    right_eave = 0.090
    apex_x = -0.142
    apex_z = 0.370
    wall_depth = 0.415

    # The main block is deliberately asymmetric in X, leaving the right third
    # of the descriptor to a real attached wing.  Its +/-Y faces remain flat
    # and share one plane with that wing.
    walls = prism_y(
        (
            (-0.460, -0.290),
            (0.205, -0.290),
            (0.205, right_eave),
            (apex_x, apex_z),
            (-0.460, left_eave),
        ),
        -wall_depth,
        wall_depth)

    main_roof, main_snow = gabled_roof(
        -0.5,
        left_eave,
        apex_x,
        apex_z,
        0.265,
        right_eave,
        0.470,
        0.050,
        5)

    # The masonry return is a complete four-sided wedge under its own low
    # lean-to.  It overlaps the timber block slightly, so no oblique view can
    # reveal an empty seam between the two closed masses.
    side_wing = prism_y(
        (
            (0.180, -0.5),
            (0.470, -0.5),
            (0.470, 0.040),
            (0.180, 0.100),
        ),
        -0.205,
        wall_depth)
    wing_roof = prism_y(
        (
            (0.140, 0.083),
            (0.5, 0.023),
            (0.5, 0.075),
            (0.140, 0.143),
        ),
        -0.250,
        0.465)

    def wing_roof_height(x: float) -> float:
        amount = (x - 0.140) / (0.5 - 0.140)
        return 0.143 + (0.075 - 0.143) * amount

    wing_snow = sloped_skin(
        0.185,
        0.470,
        -0.190,
        0.395,
        wing_roof_height,
        0.011,
        0.014,
        0.028)
    roof = merge(main_roof, wing_roof)
    snow = merge(main_snow, wing_snow)

    plinth = merge(
        chamfered_box(
            (-0.1275, 0.0, -0.385),
            (0.695, 0.830, 0.230),
            0.018),
        # Two unmatched foundation repairs say "maintained", not "new".
        chamfered_box(
            (-0.355, 0.405, -0.392),
            (0.180, 0.020, 0.175),
            0.006),
        chamfered_box(
            (0.105, -0.405, -0.410),
            (0.150, 0.020, 0.130),
            0.006),
    )

    chimney_x = 0.335
    chimney = merge(
        side_wing,
        chamfered_box(
            (chimney_x, -0.045, 0.315),
            (0.120, 0.125, 0.330),
            0.014),
        chamfered_box(
            (chimney_x, -0.045, 0.485),
            (0.158, 0.162, 0.030),
            0.010),
        # One old collar is enough to keep the whitewashed stack imperfect.
        chamfered_box(
            (chimney_x + 0.008, -0.045, 0.235),
            (0.145, 0.142, 0.050),
            0.010),
    )

    return AssemblySpec(
        "TopHouse", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_TopHouse_Walls", "TopHouse", 0, "Walls",
                     "Timber", "TopHouseTimber", walls),
            PartSpec("GEO_VIL_TopHouse_Roof", "TopHouse", 0, "Roof",
                     "Timber", "HouseRoof", roof),
            PartSpec("GEO_VIL_TopHouse_Plinth", "TopHouse", 0, "Plinth",
                     "LayeredStone", "HousePlinth", plinth),
            PartSpec("GEO_VIL_TopHouse_Chimney", "TopHouse", 0,
                     "Chimney", "Masonry", "TopHouseWall", chimney),
            PartSpec("GEO_VIL_TopHouse_Snow", "TopHouse", 0, "Snow",
                     "WindSnow", "RoofSnow", snow),
        ),
    )


FACADE_DETAIL_VARIANTS = (
    # left lean, right lean, right lower edge, repair side
    (0.020, -0.012, -0.50, -1.0),
    (-0.025, 0.018, -0.31, 1.0),
    (0.012, 0.030, -0.43, -1.0),
)


def shutter_panel(
    centre_x: float,
    width: float,
    bottom: float,
    top: float,
    lean: float,
) -> Geometry:
    half = width * 0.5
    shell = prism_y(
        (
            (centre_x - half, bottom),
            (centre_x + half, bottom),
            (centre_x + half + lean, top - 0.018),
            (centre_x - half + lean, top),
        ),
        -0.038,
        0.038)
    bars = merge(
        chamfered_box(
            (centre_x + lean * 0.20, 0.0, bottom + (top - bottom) * 0.24),
            (width + 0.028, 0.090, 0.052),
            0.008),
        chamfered_box(
            (centre_x + lean * 0.76, 0.0, bottom + (top - bottom) * 0.74),
            (width + 0.028, 0.090, 0.052),
            0.008),
    )
    return merge(shell, bars)


def build_facade_detail(variant: int) -> AssemblySpec:
    """One real-metre facade dressing placed around a runtime-owned pane.

    No pane or door is included.  The assembly supplies only old shutters,
    one plain repair and a visible iron hook for a garland wire.  That keeps
    every opening at its authored metre size and makes decoration causal.
    """
    left_lean, right_lean, right_bottom, repair_side = (
        FACADE_DETAIL_VARIANTS[variant])
    shutters = merge(
        shutter_panel(-0.335, 0.245, -0.50, 0.315, left_lean),
        shutter_panel(0.335, 0.245, right_bottom, 0.305, right_lean),
    )

    patch_x = repair_side * 0.335
    repair = prism_y(
        (
            (patch_x - 0.135, -0.46),
            (patch_x + 0.125, -0.445),
            (patch_x + 0.105, -0.10 + variant * 0.025),
            (patch_x - 0.150, -0.145),
        ),
        0.040,
        0.064)

    hook_x = -0.18 + variant * 0.18
    bracket = merge(
        tube_between(
            (hook_x, -0.015, 0.335),
            (hook_x, 0.165, 0.355),
            0.018, 0.017, 7),
        tube_between(
            (hook_x, 0.155, 0.355),
            (hook_x + 0.055, 0.205, 0.315),
            0.017, 0.014, 7),
    )

    return AssemblySpec(
        "FacadeDetail", variant, "normalized_to_descriptor",
        (
            PartSpec(
                "GEO_VIL_FacadeDetail_Variant%02d_Shutters" % variant,
                "FacadeDetail", variant, "Shutters", "Timber",
                ("ShutterA", "ShutterB", "ShutterC")[variant],
                shutters),
            PartSpec(
                "GEO_VIL_FacadeDetail_Variant%02d_Repair" % variant,
                "FacadeDetail", variant, "Repair", "Masonry",
                "RepairWhitewash", repair),
            PartSpec(
                "GEO_VIL_FacadeDetail_Variant%02d_Bracket" % variant,
                "FacadeDetail", variant, "Bracket", "RustedIron",
                "RepairIron", bracket),
        ),
    )


def build_garland_post() -> AssemblySpec:
    """A repaired timber post for spans that cannot reach a house eave."""
    timber = merge(
        tube_between(
            (-0.035, 0.0, -0.43),
            (0.030, 0.0, 0.385),
            0.065, 0.050, 7),
        chamfered_box(
            (0.005, 0.0, -0.035),
            (0.155, 0.145, 0.205),
            0.014),
    )
    bracket = merge(
        tube_between(
            (0.028, -0.025, 0.355),
            (0.028, 0.330, 0.385),
            0.023, 0.019, 7),
        tube_between(
            (0.025, 0.075, 0.275),
            (0.028, 0.270, 0.378),
            0.019, 0.016, 7),
        # Rusted splice bands explain why the old post is still standing.
        chamfered_box(
            (0.003, 0.0, -0.045),
            (0.178, 0.165, 0.035),
            0.008),
    )
    timber, bracket = ground_all(timber, bracket)
    return AssemblySpec(
        "GarlandPost", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_GarlandPost_Timber", "GarlandPost", 0,
                     "Timber", "Timber", "FenceTimber", timber),
            PartSpec("GEO_VIL_GarlandPost_Bracket", "GarlandPost", 0,
                     "Bracket", "RustedIron", "RepairIron", bracket),
        ),
    )


def build_cable_gate() -> AssemblySpec:
    """Two yard posts tied shut with cable left from the closed mine."""
    timber = merge(
        tube_between(
            (-0.425, 0.0, -0.43),
            (-0.395, 0.008, 0.345),
            0.065, 0.052, 7),
        tube_between(
            (0.425, 0.0, -0.43),
            (0.405, -0.010, 0.315),
            0.065, 0.052, 7),
    )
    cable_points = (
        (-0.405, 0.018, 0.215),
        (-0.210, -0.006, 0.105),
        (0.020, 0.012, 0.055),
        (0.235, -0.010, 0.115),
        (0.405, 0.005, 0.205),
    )
    cable_segments = [
        tube_between(first, second, 0.016, 0.016, 7)
        for first, second in zip(cable_points, cable_points[1:])
    ]
    cable_segments.extend((
        tube_between(
            (-0.425, -0.025, 0.255),
            (-0.390, 0.055, 0.170),
            0.015, 0.014, 7),
        tube_between(
            (0.425, 0.025, 0.245),
            (0.390, -0.052, 0.165),
            0.015, 0.014, 7),
    ))
    timber, cable = ground_all(timber, merge(*cable_segments))
    return AssemblySpec(
        "CableGate", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_CableGate_Timber", "CableGate", 0,
                     "Timber", "Timber", "FenceTimber", timber),
            PartSpec("GEO_VIL_CableGate_Cable", "CableGate", 0,
                     "Cable", "RustedIron", "MineCable", cable),
        ),
    )


def build_rail_bridge() -> AssemblySpec:
    """A short drainage crossing made from mine rail and rough sleepers."""
    rails = merge(
        chamfered_box(
            (-0.305, 0.0, -0.245),
            (0.078, 0.965, 0.105),
            0.010),
        chamfered_box(
            (0.310, -0.012, -0.238),
            (0.078, 0.940, 0.105),
            0.010),
        chamfered_box(
            (-0.305, 0.0, -0.315),
            (0.145, 0.965, 0.030),
            0.008),
        chamfered_box(
            (0.310, -0.012, -0.308),
            (0.145, 0.940, 0.030),
            0.008),
    )
    sleepers = []
    for index, y in enumerate((-0.405, -0.205, 0.005, 0.218, 0.410)):
        sleepers.append(chamfered_box(
            ((-0.018 if index == 3 else 0.0), y, -0.385),
            ((0.88 if index == 3 else 0.92), 0.105, 0.105),
            0.012))
    rails, sleeper_set = ground_all(rails, merge(*sleepers))
    return AssemblySpec(
        "RailBridge", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_RailBridge_Rails", "RailBridge", 0,
                     "Rails", "RustedIron", "RailIron", rails),
            PartSpec("GEO_VIL_RailBridge_Sleepers", "RailBridge", 0,
                     "Sleepers", "Timber", "RailSleeper", sleeper_set),
        ),
    )


def build_source_bowl() -> AssemblySpec:
    """An ordinary open stone catch basin beside the source chapel.

    Four unmatched rim blocks sit on one heavy bed.  There is no inscription,
    symbol, offering, candle, icon or authored water surface: it is municipal
    village plumbing, and the runtime sound merely gains a visible owner.
    """
    stone = merge(
        chamfered_box(
            (-0.012, 0.0, -0.445),
            (0.92, 0.78, 0.11),
            0.018),
        chamfered_box(
            (0.0, -0.405, 0.015),
            (0.96, 0.15, 0.87),
            0.025),
        chamfered_box(
            (-0.010, 0.395, 0.0),
            (0.93, 0.15, 0.82),
            0.023),
        chamfered_box(
            (-0.405, -0.006, 0.020),
            (0.15, 0.64, 0.84),
            0.022),
        chamfered_box(
            (0.405, 0.004, 0.040),
            (0.15, 0.63, 0.92),
            0.022),
    )
    return AssemblySpec(
        "SourceBowl", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_VIL_SourceBowl_Stone", "SourceBowl", 0,
                     "Stone", "LayeredStone", "SourceStone", stone),
        ),
    )


def make_assemblies() -> tuple[AssemblySpec, ...]:
    return (
        *(build_village_house(index)
          for index in range(HOUSE_ARCHETYPE_COUNT)),
        build_chapel(),
        build_mine_cart(),
        build_adit_frame(),
        *(build_grave_marker(index) for index in range(3)),
        build_firewood(),
        build_top_house(),
        *(build_facade_detail(index) for index in range(3)),
        build_garland_post(),
        build_cable_gate(),
        build_rail_bridge(),
        build_source_bowl(),
    )


def geometry_bounds(geometry: Geometry) -> tuple[Vec3, Vec3]:
    vertices, _ = geometry
    if not vertices:
        return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
    return (
        tuple(min(vertex[axis] for vertex in vertices) for axis in range(3)),
        tuple(max(vertex[axis] for vertex in vertices) for axis in range(3)),
    )  # type: ignore[return-value]


def combined_bounds(parts: Sequence[PartSpec]) -> tuple[Vec3, Vec3]:
    vertices = [vertex for part in parts for vertex in part.geometry[0]]
    return geometry_bounds((vertices, []))


def source_to_unity(vector: Sequence[float]) -> list[float]:
    return [stable(vector[0]), stable(vector[2]), stable(vector[1])]


def triangle_count(geometry: Geometry) -> int:
    return sum(len(face) - 2 for face in geometry[1])


def is_closed_geometry(geometry: Geometry) -> bool:
    """True when every undirected edge belongs to exactly two faces."""
    _, faces = geometry
    edge_uses: dict[tuple[int, int], int] = {}
    for face in faces:
        for index, first in enumerate(face):
            second = face[(index + 1) % len(face)]
            edge = (min(first, second), max(first, second))
            edge_uses[edge] = edge_uses.get(edge, 0) + 1
    return bool(edge_uses) and all(count == 2 for count in edge_uses.values())


def signed_volume(geometry: Geometry) -> float:
    """Oriented volume from the same deterministic fan triangulation as FBX."""
    vertices, faces = geometry
    volume = 0.0
    for face in faces:
        origin = vertices[face[0]]
        for index in range(1, len(face) - 1):
            volume += dot(
                origin,
                cross(vertices[face[index]], vertices[face[index + 1]]))
    return volume / 6.0


def face_normal(vertices: Sequence[Vec3], face: Face) -> Vec3:
    origin = vertices[face[0]]
    for index in range(1, len(face) - 1):
        first = subtract(vertices[face[index]], origin)
        second = subtract(vertices[face[index + 1]], origin)
        normal = cross(first, second)
        if length(normal) > 1e-10:
            return normalized(normal)
    return 0.0, 0.0, 0.0


def uv_for_face_vertex(normal: Vec3, vertex: Vec3) -> Vec2:
    absolute = [abs(component) for component in normal]
    dominant = absolute.index(max(absolute))
    if dominant == 0:
        uv = vertex[1] + 0.5, vertex[2] + 0.5
    elif dominant == 1:
        uv = vertex[0] + 0.5, vertex[2] + 0.5
    else:
        uv = vertex[0] + 0.5, vertex[1] + 0.5
    return stable(uv[0]), stable(uv[1])


def geometry_uvs(geometry: Geometry) -> list[Vec2]:
    vertices, faces = geometry
    result: list[Vec2] = []
    for face in faces:
        normal = face_normal(vertices, face)
        result.extend(uv_for_face_vertex(normal, vertices[index])
                      for index in face)
    return result


def uv_bounds(geometry: Geometry) -> tuple[Vec2, Vec2]:
    uvs = geometry_uvs(geometry)
    return (
        (min(uv[0] for uv in uvs), min(uv[1] for uv in uvs)),
        (max(uv[0] for uv in uvs), max(uv[1] for uv in uvs)),
    )


def validate_geometry(part: PartSpec, problems: list[str]) -> None:
    vertices, faces = part.geometry
    if not vertices or not faces:
        problems.append(f"{part.mesh} is empty")
        return
    for vertex in vertices:
        if not all(math.isfinite(value) for value in vertex):
            problems.append(f"{part.mesh} has a non-finite vertex")
            break
    for face in faces:
        if len(face) < 3 or any(index < 0 or index >= len(vertices)
                                for index in face):
            problems.append(f"{part.mesh} has an invalid face")
            continue
        if length(face_normal(vertices, face)) < 0.5:
            problems.append(f"{part.mesh} has a degenerate face")

    if is_closed_geometry(part.geometry):
        volume = signed_volume(part.geometry)
        if not math.isfinite(volume) or volume <= SIGNED_VOLUME_EPSILON:
            problems.append(
                f"{part.mesh} has non-positive signed volume {volume:.9f}; "
                "its closed surface is wound inward")

    low, high = geometry_bounds(part.geometry)
    if any(low[axis] < NORMALIZED_MIN[axis] - BOUNDS_EPSILON or
           high[axis] > NORMALIZED_MAX[axis] + BOUNDS_EPSILON
           for axis in range(3)):
        problems.append(
            f"{part.mesh} bounds {low}..{high} leave the normalized envelope")

    uvs = geometry_uvs(part.geometry)
    if len(uvs) != sum(len(face) for face in faces):
        problems.append(f"{part.mesh} has incomplete UV loops")
    if any(not math.isfinite(value) or value < -UV_EPSILON or
           value > 1.0 + UV_EPSILON for uv in uvs for value in uv):
        problems.append(f"{part.mesh} has UVs outside [0,1]")
    uv_low, uv_high = uv_bounds(part.geometry)
    if uv_high[0] - uv_low[0] < 0.01 or uv_high[1] - uv_low[1] < 0.01:
        problems.append(f"{part.mesh} has a collapsed UV span")


def face_covers_front_point(
    geometry: Geometry,
    plane_y: float,
    point_x: float,
    point_z: float,
) -> bool:
    """Conservative proof that a plan-owned facade piece has wall behind it.

    House wall and base faces are convex in XZ, and the tested point is on the
    centreline, so the bounds of a coplanar face are sufficient here.  This is
    intentionally an authoring assertion, not a general polygon query.
    """
    vertices, faces = geometry
    for face in faces:
        points = [vertices[index] for index in face]
        if not all(abs(point[1] - plane_y) <= BOUNDS_EPSILON
                   for point in points):
            continue
        if (min(point[0] for point in points) - BOUNDS_EPSILON <= point_x <=
                max(point[0] for point in points) + BOUNDS_EPSILON and
                min(point[2] for point in points) - BOUNDS_EPSILON <= point_z <=
                max(point[2] for point in points) + BOUNDS_EPSILON):
            return True
    return False


def validate_house_architecture(
    assembly: AssemblySpec,
    problems: list[str],
) -> None:
    key = assembly.kind, assembly.variant
    plane_y = HOUSE_FACADE_PLANES.get(key)
    if plane_y is None:
        return

    # Every role is a union of closed positive-volume solids.  A roof skin or
    # material patch may not stand in for one of the four opaque wall sides.
    for part in assembly.parts:
        if not is_closed_geometry(part.geometry):
            problems.append(f"{key} role {part.part_role} is not closed")

    walls = next(part for part in assembly.parts
                 if part.part_role == "Walls")
    _, wall_high = geometry_bounds(walls.geometry)
    if abs(wall_high[1] - plane_y) > BOUNDS_EPSILON:
        problems.append(
            f"{key} wall face is Y={wall_high[1]:.6f}, "
            f"expected flat facade Y={plane_y:.6f}")

    facade_parts = [
        part for part in assembly.parts
        if part.part_role in {"Walls", "Plinth", "Chimney"}
    ]
    for point_z in HOUSE_FACADE_SUPPORT_Z:
        if not any(face_covers_front_point(
                part.geometry, plane_y, 0.0, point_z)
                   for part in facade_parts):
            problems.append(
                f"{key} has no centre facade support at "
                f"Y={plane_y:.3f}, Z={point_z:.3f}")


def validate_assemblies(assemblies: Sequence[AssemblySpec]) -> None:
    problems: list[str] = []
    expected_variants = {
        "House": HOUSE_ARCHETYPE_COUNT,
        "Chapel": 1,
        "MineCart": 1,
        "AditFrame": 1,
        "GraveMarker": 3,
        "Firewood": 1,
        "TopHouse": 1,
        "FacadeDetail": 3,
        "GarlandPost": 1,
        "CableGate": 1,
        "RailBridge": 1,
        "SourceBowl": 1,
    }
    expected_roles = {
        **{("House", index):
           ("Walls", "Roof", "Plinth", "Chimney", "Snow")
           for index in range(HOUSE_ARCHETYPE_COUNT)},
        ("Chapel", 0): ("Walls", "Roof", "Plinth", "Snow"),
        ("MineCart", 0): ("Body", "Wheels"),
        ("AditFrame", 0): ("Timber", "Rubble"),
        **{("GraveMarker", index): ("Stone",) for index in range(3)},
        ("Firewood", 0): ("Wood",),
        ("TopHouse", 0):
            ("Walls", "Roof", "Plinth", "Chimney", "Snow"),
        **{("FacadeDetail", index):
           ("Shutters", "Repair", "Bracket")
           for index in range(3)},
        ("GarlandPost", 0): ("Timber", "Bracket"),
        ("CableGate", 0): ("Timber", "Cable"),
        ("RailBridge", 0): ("Rails", "Sleepers"),
        ("SourceBowl", 0): ("Stone",),
    }
    names: set[str] = set()
    keys: set[tuple[str, int]] = set()
    for assembly in assemblies:
        key = assembly.kind, assembly.variant
        if key in keys:
            problems.append(f"duplicate assembly {key}")
        keys.add(key)
        if tuple(part.part_role for part in assembly.parts) != expected_roles.get(key):
            problems.append(f"{key} part topology drifted")
        # Every village archetype fills its plot: a house IS its footprint,
        # unlike the mountain's dead trees whose branches reach past theirs.
        expected_mode = "normalized_to_descriptor"
        if assembly.scale_mode != expected_mode:
            problems.append(f"{key} has scale mode {assembly.scale_mode}")
        for part in assembly.parts:
            if part.mesh in names:
                problems.append(f"duplicate mesh name {part.mesh}")
            names.add(part.mesh)
            validate_geometry(part, problems)

        validate_house_architecture(assembly, problems)

        low, high = combined_bounds(assembly.parts)
        if abs(low[2] + 0.5) > 1e-6:
            problems.append(
                f"{key} ground is Z={low[2]:.6f}, expected -0.5")
        if high[2] > 0.5 + BOUNDS_EPSILON:
            problems.append(f"{key} rises above the normalized envelope")
        if not (low[0] <= 0.0 <= high[0] and low[1] <= 0.0 <= high[1]):
            problems.append(f"{key} does not straddle its descriptor origin")
    for kind, count in expected_variants.items():
        actual = sorted(variant for item_kind, variant in keys
                        if item_kind == kind)
        if actual != list(range(count)):
            problems.append(f"{kind} variants are {actual}, expected 0..{count - 1}")

    # These are architectural families, not four cosmetic roof skews.  The
    # Renaissance house must devote materially more height to its masonry
    # storey than the timber-heavy Heidehüs.
    house_plinth_tops = {}
    for assembly in assemblies:
        if assembly.kind != "House":
            continue
        plinth = next(part for part in assembly.parts
                      if part.part_role == "Plinth")
        house_plinth_tops[assembly.variant] = geometry_bounds(
            plinth.geometry)[1][2]
    if (house_plinth_tops.get(1, -1.0) -
            house_plinth_tops.get(0, -1.0) < 0.12):
        problems.append(
            "House archetypes do not differ enough in masonry-storey height")

    top_house = next(
        (assembly for assembly in assemblies
         if (assembly.kind, assembly.variant) == ("TopHouse", 0)),
        None)
    if top_house is not None:
        top_walls = next(part for part in top_house.parts
                         if part.part_role == "Walls")
        top_masonry = next(part for part in top_house.parts
                           if part.part_role == "Chimney")
        _, timber_high = geometry_bounds(top_walls.geometry)
        masonry_low, masonry_high = geometry_bounds(top_masonry.geometry)
        if (timber_high[0] > 0.22 or masonry_low[2] > -0.5 + BOUNDS_EPSILON or
                masonry_high[0] < 0.46):
            problems.append(
                "TopHouse lost its timber-main/masonry-+X-wing composition")
    if len(names) != 43:
        problems.append(f"mesh count is {len(names)}, expected 43")
    total_triangles = sum(triangle_count(part.geometry)
                          for assembly in assemblies
                          for part in assembly.parts)
    if total_triangles <= 0 or total_triangles > MAX_TRIANGLES:
        problems.append(
            f"triangle count {total_triangles} is outside 1..{MAX_TRIANGLES}")
    if problems:
        raise RuntimeError(
            "Village art contract violated:\n  - " +
            "\n  - ".join(problems))


def signature_payload(assemblies: Sequence[AssemblySpec]) -> dict:
    return {
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "root_contract": {
            "origin": "descriptor_center",
            "source_ground_value": -0.5,
            "normalized_descriptor_min": list(NORMALIZED_MIN),
            "normalized_descriptor_max": list(NORMALIZED_MAX),
        },
        "assemblies": [
            {
                "kind": assembly.kind,
                "variant": assembly.variant,
                "scale_mode": assembly.scale_mode,
                "parts": [
                    {
                        "mesh": part.mesh,
                        "part_role": part.part_role,
                        "surface_kind": part.surface_kind,
                        "tint_role": part.tint_role,
                        "vertices": [[stable(value) for value in vertex]
                                     for vertex in part.geometry[0]],
                        "faces": [list(face) for face in part.geometry[1]],
                        "uv": [[stable(value) for value in uv]
                               for uv in geometry_uvs(part.geometry)],
                    }
                    for part in assembly.parts
                ],
            }
            for assembly in assemblies
        ],
    }


def signature_for(assemblies: Sequence[AssemblySpec]) -> str:
    encoded = json.dumps(
        signature_payload(assemblies),
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def reset_scene() -> tuple[bpy.types.Collection, bpy.types.Collection]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    source = bpy.data.collections.new(SOURCE_COLLECTION)
    presentation = bpy.data.collections.new(PRESENTATION_COLLECTION)
    scene.collection.children.link(source)
    scene.collection.children.link(presentation)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("VIL_PreviewWorld")
    world.color = (0.025, 0.030, 0.033)
    scene.world = world
    return source, presentation


def create_material(tint_role: str) -> bpy.types.Material:
    material = bpy.data.materials.new(f"PREVIEW_VIL_{tint_role}")
    color = PREVIEW_COLORS[tint_role]
    material.diffuse_color = color
    material.use_backface_culling = True
    material.use_nodes = True
    node = material.node_tree.nodes.get("Principled BSDF")
    if node is not None:
        node.inputs["Base Color"].default_value = color
        node.inputs["Roughness"].default_value = 0.76
        node.inputs["Metallic"].default_value = (
            0.22 if tint_role in {"Rust", "Iron"} else 0.0)
    return material


def assign_uv(mesh: bpy.types.Mesh, geometry: Geometry) -> None:
    layer = mesh.uv_layers.new(name="UVMap")
    vertices, faces = geometry
    face_uvs = []
    for face in faces:
        normal = face_normal(vertices, face)
        face_uvs.append([
            uv_for_face_vertex(normal, vertices[index]) for index in face
        ])
    for polygon, values in zip(mesh.polygons, face_uvs):
        if len(polygon.loop_indices) != len(values):
            raise RuntimeError(f"UV loop drift on '{mesh.name}'.")
        for loop_index, uv in zip(polygon.loop_indices, values):
            layer.data[loop_index].uv = uv


def create_part_object(
    part: PartSpec,
    root: bpy.types.Object,
    source: bpy.types.Collection,
    materials: dict[str, bpy.types.Material],
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(part.mesh)
    mesh.from_pydata(part.geometry[0], [], part.geometry[1])
    mesh.update(calc_edges=True)
    assign_uv(mesh, part.geometry)
    mesh.materials.append(materials[part.tint_role])
    obj = bpy.data.objects.new(part.mesh, mesh)
    source.objects.link(obj)
    obj.parent = root
    obj["bp_kind"] = part.kind
    obj["bp_variant"] = part.variant
    obj["bp_part_role"] = part.part_role
    obj["bp_surface_kind"] = part.surface_kind
    obj["bp_tint_role"] = part.tint_role
    return obj


def build_scene(assemblies: tuple[AssemblySpec, ...]) -> BuildResult:
    source, presentation = reset_scene()
    materials = {
        role: create_material(role) for role in sorted(PREVIEW_COLORS)
    }
    root = bpy.data.objects.new(ROOT_NAME, None)
    source.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.25
    root["bp_design_id"] = DESIGN_ID
    root["bp_origin_contract"] = "descriptor_center"
    root["bp_ground_source_z"] = -0.5
    objects: dict[str, bpy.types.Object] = {}
    for assembly in assemblies:
        for part in assembly.parts:
            obj = create_part_object(part, root, source, materials)
            obj["bp_scale_mode"] = assembly.scale_mode
            objects[part.mesh] = obj
    return BuildResult(
        root, source, presentation, assemblies, objects, materials)


def manifest_for(
    assemblies: Sequence[AssemblySpec],
    signature: str,
) -> dict:
    parts = [part for assembly in assemblies for part in assembly.parts]
    assembly_records = []
    for assembly in assemblies:
        low, high = combined_bounds(assembly.parts)
        assembly_records.append({
            "kind": assembly.kind,
            "variant": assembly.variant,
            "scale_mode": assembly.scale_mode,
            "part_meshes": [part.mesh for part in assembly.parts],
            "bounds_min_source": [stable(value) for value in low],
            "bounds_max_source": [stable(value) for value in high],
            "bounds_min_unity": source_to_unity(low),
            "bounds_max_unity": source_to_unity(high),
        })
    part_records = []
    for part in parts:
        low, high = geometry_bounds(part.geometry)
        uv_low, uv_high = uv_bounds(part.geometry)
        part_records.append({
            "mesh": part.mesh,
            "kind": part.kind,
            "variant": part.variant,
            "part_role": part.part_role,
            "surface_kind": part.surface_kind,
            "tint_role": part.tint_role,
            "vertices": len(part.geometry[0]),
            "triangles": triangle_count(part.geometry),
            "bounds_min_source": [stable(value) for value in low],
            "bounds_max_source": [stable(value) for value in high],
            "bounds_min_unity": source_to_unity(low),
            "bounds_max_unity": source_to_unity(high),
            "uv_min": [stable(value) for value in uv_low],
            "uv_max": [stable(value) for value in uv_high],
        })
    return {
        "generator": "tools/build-village-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "source_axes": {
            "right": "+X", "forward": "+Y", "up": "+Z",
        },
        "unity_axes": {
            "right": "+X", "forward": "+Z", "up": "+Y",
            "fbx_axis_forward": "-Z", "fbx_axis_up": "+Y",
            "bake_space_transform": True,
        },
        "root_contract": {
            "origin": "descriptor_center",
            "source_ground_axis": "Z",
            "source_ground_value": -0.5,
            "unity_ground_axis": "Y",
            "unity_ground_value": -0.5,
            "normalized_descriptor_min": list(NORMALIZED_MIN),
            "normalized_descriptor_max": list(NORMALIZED_MAX),
        },
        "colliders": False,
        "lights": False,
        "cameras": False,
        "animation_count": 0,
        "mesh_count": len(parts),
        "assembly_count": len(assemblies),
        "triangle_count": sum(triangle_count(part.geometry) for part in parts),
        "assemblies": assembly_records,
        "parts": part_records,
        "build_signature": signature,
    }


def write_manifest(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


def export_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    result.root.select_set(True)
    for obj in result.objects.values():
        obj.select_set(True)
    bpy.context.view_layer.objects.active = result.root
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,
        add_leaf_bones=False,
        bake_anim=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_custom_props=True,
    )


def add_preview_stage(result: BuildResult) -> None:
    collection = result.presentation
    ground_material = bpy.data.materials.new("PREVIEW_VIL_Ground")
    ground_material.diffuse_color = (0.055, 0.061, 0.059, 1.0)
    ground_mesh = bpy.data.meshes.new("VIL_PreviewGround_Mesh")
    ground_geometry = box((0.0, 0.0, -0.56), (19.0, 10.2, 0.10))
    ground_mesh.from_pydata(ground_geometry[0], [], ground_geometry[1])
    ground = bpy.data.objects.new("VIL_PreviewGround", ground_mesh)
    collection.objects.link(ground)
    ground.data.materials.append(ground_material)

    columns = 7
    spacing_x = 2.35
    spacing_y = 2.35
    for index, assembly in enumerate(result.assemblies):
        column = index % columns
        row = index // columns
        placement = bpy.data.objects.new(
            f"PREVIEW_{assembly.kind}_{assembly.variant:02d}", None)
        collection.objects.link(placement)
        descriptor_size = PREVIEW_DESCRIPTOR_SIZES[assembly.kind]
        largest = max(descriptor_size)
        preview_scale = tuple(value / largest for value in descriptor_size)
        placement.location = (
            (column - (columns - 1) * 0.5) * spacing_x,
            (1 - row) * spacing_y,
            preview_scale[2] * 0.5,
        )
        placement.scale = preview_scale
        for part in assembly.parts:
            source = result.objects[part.mesh]
            duplicate = source.copy()
            duplicate.data = source.data
            collection.objects.link(duplicate)
            duplicate.parent = placement

    for name, location, energy, color, size in (
        ("Key", (-6.0, -6.0, 10.0), 1900, (0.76, 0.82, 0.78), 7.0),
        ("Rim", (6.0, 3.0, 8.0), 1450, (0.32, 0.45, 0.52), 5.0),
        ("Warm", (0.0, -2.0, 6.0), 900, (0.92, 0.56, 0.30), 4.0),
    ):
        data = bpy.data.lights.new(f"PREVIEW_VIL_{name}", "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(f"PREVIEW_VIL_{name}", data)
        collection.objects.link(light)
        light.location = location
        light.rotation_euler = (
            Vector((0.0, 0.0, 0.2)) - light.location
        ).to_track_quat("-Z", "Y").to_euler()


def render_preview(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    add_preview_stage(result)
    for obj in result.source.objects:
        obj.hide_render = True
    camera_data = bpy.data.cameras.new("CAM_VillagePreview")
    camera = bpy.data.objects.new("CAM_VillagePreview", camera_data)
    result.presentation.objects.link(camera)
    camera.location = (10.2, -14.2, 9.2)
    target = Vector((0.0, 0.0, 0.20))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 17.0
    bpy.context.scene.camera = camera
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for obj in result.source.objects:
        obj.hide_render = False


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blend", type=Path, default=DEFAULT_BLEND)
    parser.add_argument("--fbx", type=Path, default=DEFAULT_FBX)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--preview", type=Path, default=DEFAULT_PREVIEW)
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    return parser.parse_args(argv)


def main() -> int:
    config = parse_args()
    assemblies = make_assemblies()
    validate_assemblies(assemblies)
    signature = signature_for(assemblies)

    # Re-run the pure authoring pass before touching the filesystem.  Any
    # accidental dependence on Blender scene state or unordered iteration
    # changes this complete geometry/UV/role signature.
    rerun = make_assemblies()
    validate_assemblies(rerun)
    if signature_for(rerun) != signature:
        raise RuntimeError("Non-deterministic village signature.")

    total_parts = sum(len(assembly.parts) for assembly in assemblies)
    total_triangles = sum(
        triangle_count(part.geometry)
        for assembly in assemblies for part in assembly.parts)
    if config.validate_only:
        print("VILLAGE 3D DIRECT VALIDATION OK")
        print(f"  Assemblies: {len(assemblies)}")
        print(f"  Meshes: {total_parts}")
        print(f"  Triangles: {total_triangles}/{MAX_TRIANGLES}")
        print(f"  Signature: {signature}")
        print("  Determinism: repeated signatures match")
        return 0

    result = build_scene(assemblies)
    if not config.no_preview:
        render_preview(config.preview, result)
    export_fbx(config.fbx, result)
    payload = manifest_for(assemblies, signature)
    write_manifest(config.manifest, payload)
    save_blend(config.blend)
    print("VILLAGE 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Assemblies: {len(assemblies)}")
    print(f"  Meshes: {total_parts}")
    print(f"  Triangles: {total_triangles}/{MAX_TRIANGLES}")
    print(f"  Signature: {signature}")
    print("  Determinism: repeated signatures match")
    print(f"  Blend: {config.blend}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
