#!/usr/bin/env python3
"""Build the first authored Mountain Road roadside-prop library.

The library contains local-space archetypes, never world placements.  The
runtime's ``MountainRoadMiscDescriptor`` remains the owner of position,
rotation, size, stable ID and collision.  Most assemblies occupy the
normalized descriptor cube: their origin is the descriptor centre, their
ground plane is source ``Z=-0.5`` (Unity ``Y=-0.5``), and Unity scales them
component-wise by the descriptor size.

Dead trees are the deliberate exception.  The descriptor's narrow X/Z values
describe the trunk, while the existing branches reach several metres.  Their
source height is still exactly one unit from ``Z=-0.5`` to ``Z=+0.5``, but the
whole tree is scaled uniformly by descriptor height.  The JSON manifest names
that contract as ``uniform_by_height``.

Source space is Blender metres, X right, +Y forward, Z up.  The FBX export
bakes axes and units so bare mesh sub-assets import into Unity with X right,
+Z forward and Y up.  Every visible material role is a separate mesh because
the runtime combines meshes per Mountain Road surface/tint bucket.

Run through Blender 5 from the repository root::

    blender --background --factory-startup --python \
      tools/build-mountain-road-misc-3d-model.py

    blender --background --factory-startup --python \
      tools/build-mountain-road-misc-3d-model.py -- --validate-only
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
GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "mountain_road_misc_wave1_v1"
DISPLAY_NAME = "Mountain Road Misc Wave 1"

DEFAULT_BLEND = (
    ROOT / "ArtSource" / "MountainRoad" / "Blender" /
    "MountainRoadMisc3D.blend"
)
DEFAULT_PREVIEW = (
    ROOT / "ArtSource" / "MountainRoad" / "Blender" /
    "MountainRoadMisc3D.png"
)
DEFAULT_FBX = (
    ROOT / "Assets" / "MountainRoad" / "Models" /
    "MountainRoadMisc3D.fbx"
)
DEFAULT_MANIFEST = (
    ROOT / "Assets" / "MountainRoad" / "Models" /
    "MountainRoadMisc3D.json"
)

SOURCE_COLLECTION = "SOURCE_MountainRoadMisc3D"
PRESENTATION_COLLECTION = "PRESENTATION_MountainRoadMisc3D"
ROOT_NAME = "ROOT_MountainRoadMisc3D"

NORMALIZED_MIN = (-0.5, -0.5, -0.5)
NORMALIZED_MAX = (0.5, 0.5, 0.5)
BOUNDS_EPSILON = 1e-6
UV_EPSILON = 1e-6
MAX_TRIANGLES = 16000

Vec2 = tuple[float, float]
Vec3 = tuple[float, float, float]
Face = tuple[int, ...]
Geometry = tuple[list[Vec3], list[Face]]


PREVIEW_COLORS = {
    "SnowPoleBody": (0.48, 0.10, 0.055, 1.0),
    "SnowPoleBand": (0.72, 0.69, 0.58, 1.0),
    "DeadWood": (0.20, 0.105, 0.052, 1.0),
    "Rust": (0.27, 0.105, 0.038, 1.0),
    "MirrorFace": (0.42, 0.55, 0.54, 1.0),
    "Cabinet": (0.18, 0.255, 0.22, 1.0),
    "Iron": (0.095, 0.080, 0.065, 1.0),
}

# Representative descriptor sizes expressed in source axes (X, Y-forward,
# Z-up).  The source meshes themselves remain normalized; only the review
# copies use these ratios, otherwise a snow pole reads as a hydrant and a
# guard rail as a table in the contact sheet.
PREVIEW_DESCRIPTOR_SIZES = {
    "SnowPole": (0.14, 0.14, 3.0),
    "FallenLog": (0.68, 4.6, 0.68),
    "Stump": (0.88, 0.88, 1.05),
    "DeadTree": (1.0, 1.0, 1.0),
    "GuardRail": (0.22, 6.4, 1.05),
    "ConvexMirror": (1.05, 0.28, 3.0),
    "UtilityCabinet": (1.2, 0.75, 1.8),
    "AbandonedChair": (0.82, 0.82, 1.1),
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
        tuple(reversed(range(count))),
        tuple(range(count, count * 2)),
    ]
    for index in range(count):
        following = (index + 1) % count
        faces.append((
            index, following, count + following, count + index,
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


def build_snow_pole() -> AssemblySpec:
    body = merge(
        tube_between((0.0, 0.0, -0.5), (0.0, 0.0, -0.10),
                     0.265, 0.25, 8),
        tube_between((0.0, 0.0, -0.10), (-0.018, 0.012, 0.24),
                     0.25, 0.235, 8),
        tube_between((-0.018, 0.012, 0.24), (0.035, 0.008, 0.43),
                     0.235, 0.19, 8),
    )
    band = irregular_tube_z((
        (0.145, -0.006, 0.006, 0.47, 0.47, 0.0),
        (0.255, -0.014, 0.009, 0.455, 0.455, 0.0),
    ), 8)
    return AssemblySpec(
        "SnowPole", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_MRM_SnowPole_Body", "SnowPole", 0,
                     "Body", "PaleEnamel", "SnowPoleBody", body),
            PartSpec("GEO_MRM_SnowPole_Band", "SnowPole", 0,
                     "Band", "PaleEnamel", "SnowPoleBand", band),
        ),
    )


def build_fallen_log(variant: int) -> AssemblySpec:
    profiles = (
        (
            (-0.5, 0.00, -0.080, 0.40, 0.40, 0.02),
            (-0.31, 0.018, -0.075, 0.43, 0.41, 0.05),
            (-0.08, -0.012, -0.082, 0.44, 0.42, 0.00),
            (0.18, 0.020, -0.090, 0.42, 0.40, 0.04),
            (0.39, -0.010, -0.085, 0.39, 0.38, 0.08),
            (0.5, 0.006, -0.082, 0.36, 0.36, 0.12),
        ),
        (
            (-0.5, -0.015, -0.075, 0.37, 0.39, 0.10),
            (-0.34, 0.012, -0.087, 0.41, 0.41, 0.06),
            (-0.12, 0.030, -0.092, 0.43, 0.42, 0.02),
            (0.10, -0.018, -0.080, 0.41, 0.40, 0.05),
            (0.32, -0.032, -0.075, 0.38, 0.38, 0.09),
            (0.5, 0.00, -0.070, 0.34, 0.35, 0.14),
        ),
        (
            (-0.5, 0.00, -0.090, 0.35, 0.38, 0.03),
            (-0.37, -0.020, -0.086, 0.39, 0.40, 0.08),
            (-0.15, 0.025, -0.078, 0.42, 0.41, 0.11),
            (0.09, 0.036, -0.085, 0.44, 0.42, 0.06),
            (0.31, -0.012, -0.092, 0.40, 0.40, 0.02),
            (0.5, -0.025, -0.088, 0.36, 0.37, 0.12),
        ),
    )
    pieces: list[Geometry] = [irregular_tube_y(profiles[variant], 9)]
    if variant == 0:
        pieces.append(tube_between(
            (0.30, -0.05, 0.18), (0.45, -0.01, 0.31),
            0.075, 0.045, 7))
    elif variant == 1:
        pieces.append(tube_between(
            (-0.31, 0.12, 0.14), (-0.44, 0.18, 0.27),
            0.070, 0.035, 7))
    else:
        pieces.extend((
            tube_between((0.22, -0.12, 0.15), (0.39, -0.17, 0.27),
                         0.062, 0.032, 7),
            tube_between((-0.18, 0.17, 0.12), (-0.29, 0.24, 0.22),
                         0.050, 0.026, 7),
        ))
    mesh_name = f"GEO_MRM_FallenLog_Variant{variant + 1:02d}_Wood"
    return AssemblySpec(
        "FallenLog", variant, "normalized_to_descriptor",
        (PartSpec(mesh_name, "FallenLog", variant, "Wood",
                  "BarkAndDeadwood", "DeadWood", grounded(merge(*pieces))),),
    )


def build_stump(variant: int) -> AssemblySpec:
    phases = (0.0, 0.11, 0.22, 0.31)
    lean = ((0.0, 0.0), (0.018, -0.012), (-0.020, 0.010),
            (0.012, 0.020))[variant]
    rings = (
        (-0.5, 0.0, 0.0, 0.33, 0.32, phases[variant]),
        (-0.31, 0.0, 0.0, 0.31, 0.30, phases[variant] + 0.025),
        (0.05, lean[0] * 0.5, lean[1] * 0.5,
         0.27 + variant * 0.006, 0.265, phases[variant] - 0.018),
        (0.41, lean[0], lean[1], 0.245, 0.24,
         phases[variant] + 0.020),
        (0.5, lean[0] * 1.1, lean[1] * 1.1,
         0.235, 0.23, phases[variant] + 0.045),
    )
    pieces: list[Geometry] = [irregular_tube_z(rings, 9)]
    root_counts = (5, 6, 4, 7)
    for index in range(root_counts[variant]):
        angle = (index / root_counts[variant] * math.tau +
                 phases[variant] * 3.0)
        reach = 0.43 + ((index + variant * 2) % 3) * 0.025
        pieces.append(triangular_root(
            angle, reach, 0.065 + (index % 2) * 0.012,
            0.14 + ((index + variant) % 3) * 0.025))
    mesh_name = f"GEO_MRM_Stump_Variant{variant + 1:02d}_Wood"
    return AssemblySpec(
        "Stump", variant, "normalized_to_descriptor",
        (PartSpec(mesh_name, "Stump", variant, "Wood",
                  "BarkAndDeadwood", "DeadWood", merge(*pieces)),),
    )


def build_dead_tree(variant: int) -> AssemblySpec:
    phase = (0.0, 0.12, 0.23)[variant]
    lean = ((0.0, 0.0), (0.012, -0.009), (-0.010, 0.012))[variant]
    trunk = irregular_tube_z((
        (-0.5, 0.0, 0.0, 0.045, 0.043, phase),
        (-0.15, lean[0] * 0.3, lean[1] * 0.3, 0.039, 0.037,
         phase + 0.02),
        (0.18, lean[0] * 0.7, lean[1] * 0.7, 0.031, 0.030,
         phase - 0.02),
        (0.5, lean[0], lean[1], 0.020, 0.019, phase + 0.03),
    ), 8)
    branch_sets = (
        (
            ((0.006, 0.0, 0.02), (0.125, 0.100, 0.20), 0.018, 0.010),
            ((0.008, -0.002, 0.20), (-0.105, -0.110, 0.34), 0.016, 0.008),
            ((0.004, 0.0, 0.34), (0.070, -0.090, 0.46), 0.012, 0.006),
        ),
        (
            ((0.0, 0.0, -0.02), (-0.120, 0.100, 0.18), 0.018, 0.009),
            ((0.006, -0.003, 0.16), (0.105, -0.105, 0.31), 0.016, 0.008),
            ((0.010, -0.005, 0.31), (-0.055, 0.105, 0.44), 0.012, 0.006),
        ),
        (
            ((0.0, 0.0, 0.00), (0.112, -0.110, 0.15), 0.018, 0.009),
            ((-0.002, 0.004, 0.13), (-0.115, 0.100, 0.28), 0.016, 0.008),
            ((-0.006, 0.008, 0.27), (0.080, 0.110, 0.41), 0.013, 0.006),
            ((-0.008, 0.010, 0.38), (-0.045, -0.090, 0.47), 0.010, 0.005),
        ),
    )
    pieces: list[Geometry] = [trunk]
    for start, end, first_radius, last_radius in branch_sets[variant]:
        pieces.append(tube_between(
            start, end, first_radius, last_radius, 7))
    mesh_name = f"GEO_MRM_DeadTree_Variant{variant + 1:02d}_Wood"
    return AssemblySpec(
        "DeadTree", variant, "uniform_by_height",
        (PartSpec(mesh_name, "DeadTree", variant, "Wood",
                  "BarkAndDeadwood", "DeadWood", merge(*pieces)),),
    )


def build_guard_rail() -> AssemblySpec:
    rail_profile = (
        (-0.46, -0.070), (-0.18, -0.085), (-0.06, -0.015),
        (0.12, -0.090), (0.46, -0.055), (0.46, 0.060),
        (0.14, 0.090), (0.04, 0.020), (-0.20, 0.085),
        (-0.46, 0.060),
    )
    pieces: list[Geometry] = [
        translated(prism_y(rail_profile, -0.5, 0.5), (0.0, 0.0, 0.245)),
    ]
    for y in (-0.38, 0.0, 0.38):
        pieces.append(chamfered_box(
            (0.0, y, -0.145), (0.66, 0.042, 0.71), 0.016))
        # A visible road-side bolt head, kept inside the descriptor thickness.
        pieces.append(tube_between(
            (-0.455, y, 0.245), (-0.49, y, 0.245),
            0.055, 0.052, 7))
    part = PartSpec(
        "GEO_MRM_GuardRail_Iron", "GuardRail", 0, "Iron",
        "RustedIron", "Rust", merge(*pieces))
    return AssemblySpec(
        "GuardRail", 0, "normalized_to_descriptor", (part,))


def build_convex_mirror() -> AssemblySpec:
    pole = merge(
        irregular_tube_z((
            (-0.5, 0.0, 0.0, 0.048, 0.175, 0.0),
            (0.26, 0.0, 0.0, 0.043, 0.155, 0.0),
        ), 8),
        chamfered_box((0.0, 0.0, -0.455), (0.24, 0.46, 0.09), 0.015),
    )
    frame = annulus_y(
        (0.0, -0.36, 0.32), 0.49, 0.175, 0.415, 0.137, 0.18, 20)
    face = convex_disc_y(
        (0.0, -0.40, 0.32), 0.405, 0.132, 20)
    return AssemblySpec(
        "ConvexMirror", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_MRM_ConvexMirror_Pole", "ConvexMirror", 0,
                     "Pole", "RustedIron", "Rust", pole),
            PartSpec("GEO_MRM_ConvexMirror_Frame", "ConvexMirror", 0,
                     "Frame", "RustedIron", "Rust", frame),
            PartSpec("GEO_MRM_ConvexMirror_Face", "ConvexMirror", 0,
                     "Face", "PaleEnamel", "MirrorFace", face),
        ),
    )


def build_utility_cabinet() -> AssemblySpec:
    body = merge(
        chamfered_box((0.0, 0.0, -0.01), (0.90, 0.82, 0.98), 0.035),
        chamfered_box((0.0, 0.0, -0.47), (0.98, 0.90, 0.06), 0.018),
    )
    trim = merge(
        chamfered_box((-0.38, -0.425, 0.0), (0.035, 0.035, 0.76), 0.008),
        chamfered_box((0.38, -0.425, 0.0), (0.035, 0.035, 0.76), 0.008),
        chamfered_box((0.0, -0.425, 0.36), (0.79, 0.035, 0.035), 0.008),
        chamfered_box((0.0, -0.425, -0.36), (0.79, 0.035, 0.035), 0.008),
        chamfered_box((0.26, -0.456, 0.0), (0.055, 0.035, 0.16), 0.009),
        tube_between((-0.31, -0.455, -0.09),
                     (-0.31, -0.455, 0.09), 0.025, 0.025, 7),
    )
    return AssemblySpec(
        "UtilityCabinet", 0, "normalized_to_descriptor",
        (
            PartSpec("GEO_MRM_UtilityCabinet_Body", "UtilityCabinet", 0,
                     "Body", "PaintedMetal", "Cabinet", body),
            PartSpec("GEO_MRM_UtilityCabinet_Trim", "UtilityCabinet", 0,
                     "Trim", "RustedIron", "Iron", trim),
        ),
    )


def build_abandoned_chair() -> AssemblySpec:
    pieces: list[Geometry] = []
    # Three uneven seat planks and a recessed apron read as furniture rather
    # than the six runtime boxes this object replaces.
    for index, x in enumerate((-0.285, 0.0, 0.285)):
        pieces.append(chamfered_box(
            (x, (index - 1) * 0.012, -0.075 + index * 0.006),
            (0.255, 0.72, 0.115), 0.020))
    pieces.extend((
        chamfered_box((0.0, -0.31, -0.17), (0.78, 0.075, 0.18), 0.018),
        chamfered_box((0.0, 0.31, -0.17), (0.78, 0.075, 0.18), 0.018),
    ))
    for x in (-0.34, 0.34):
        for y in (-0.28, 0.28):
            pieces.append(tube_between(
                (x * 1.10, y * 1.08, -0.5),
                (x, y, -0.13), 0.050, 0.060, 7))
    # Back posts, cross rails and two narrow splats.  One post leans slightly,
    # the small asymmetry that tells an abandoned chair from a new prop.
    pieces.extend((
        tube_between((-0.35, 0.31, -0.12), (-0.39, 0.31, 0.49),
                     0.052, 0.045, 7),
        tube_between((0.35, 0.31, -0.12), (0.32, 0.31, 0.47),
                     0.052, 0.043, 7),
        chamfered_box((-0.035, 0.31, 0.40), (0.68, 0.075, 0.09), 0.018),
        chamfered_box((-0.02, 0.31, 0.13), (0.66, 0.070, 0.075), 0.016),
        chamfered_box((-0.16, 0.31, 0.265), (0.085, 0.060, 0.25), 0.015),
        chamfered_box((0.13, 0.31, 0.255), (0.082, 0.060, 0.23), 0.015),
    ))
    part = PartSpec(
        "GEO_MRM_AbandonedChair_Wood", "AbandonedChair", 0, "Wood",
        "BarkAndDeadwood", "DeadWood", grounded(merge(*pieces)))
    return AssemblySpec(
        "AbandonedChair", 0, "normalized_to_descriptor", (part,))


def make_assemblies() -> tuple[AssemblySpec, ...]:
    return (
        build_snow_pole(),
        *(build_fallen_log(index) for index in range(3)),
        *(build_stump(index) for index in range(4)),
        *(build_dead_tree(index) for index in range(3)),
        build_guard_rail(),
        build_convex_mirror(),
        build_utility_cabinet(),
        build_abandoned_chair(),
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


def validate_assemblies(assemblies: Sequence[AssemblySpec]) -> None:
    problems: list[str] = []
    expected_variants = {
        "SnowPole": 1,
        "FallenLog": 3,
        "Stump": 4,
        "DeadTree": 3,
        "GuardRail": 1,
        "ConvexMirror": 1,
        "UtilityCabinet": 1,
        "AbandonedChair": 1,
    }
    expected_roles = {
        ("SnowPole", 0): ("Body", "Band"),
        **{("FallenLog", index): ("Wood",) for index in range(3)},
        **{("Stump", index): ("Wood",) for index in range(4)},
        **{("DeadTree", index): ("Wood",) for index in range(3)},
        ("GuardRail", 0): ("Iron",),
        ("ConvexMirror", 0): ("Pole", "Frame", "Face"),
        ("UtilityCabinet", 0): ("Body", "Trim"),
        ("AbandonedChair", 0): ("Wood",),
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
        expected_mode = (
            "uniform_by_height" if assembly.kind == "DeadTree"
            else "normalized_to_descriptor"
        )
        if assembly.scale_mode != expected_mode:
            problems.append(f"{key} has scale mode {assembly.scale_mode}")
        for part in assembly.parts:
            if part.mesh in names:
                problems.append(f"duplicate mesh name {part.mesh}")
            names.add(part.mesh)
            validate_geometry(part, problems)

        low, high = combined_bounds(assembly.parts)
        if abs(low[2] + 0.5) > 1e-6:
            problems.append(
                f"{key} ground is Z={low[2]:.6f}, expected -0.5")
        if high[2] > 0.5 + BOUNDS_EPSILON:
            problems.append(f"{key} rises above the normalized envelope")
        if not (low[0] <= 0.0 <= high[0] and low[1] <= 0.0 <= high[1]):
            problems.append(f"{key} does not straddle its descriptor origin")
        if assembly.kind == "DeadTree":
            width = high[0] - low[0]
            depth = high[1] - low[1]
            if not (0.20 <= width <= 0.30 and 0.20 <= depth <= 0.30):
                problems.append(
                    f"{key} branch envelope is {width:.3f}x{depth:.3f}; "
                    "expected 0.20..0.30 of height")

    for kind, count in expected_variants.items():
        actual = sorted(variant for item_kind, variant in keys
                        if item_kind == kind)
        if actual != list(range(count)):
            problems.append(f"{kind} variants are {actual}, expected 0..{count - 1}")
    if len(names) != 19:
        problems.append(f"mesh count is {len(names)}, expected 19")
    total_triangles = sum(triangle_count(part.geometry)
                          for assembly in assemblies
                          for part in assembly.parts)
    if total_triangles <= 0 or total_triangles > MAX_TRIANGLES:
        problems.append(
            f"triangle count {total_triangles} is outside 1..{MAX_TRIANGLES}")
    if problems:
        raise RuntimeError(
            "Mountain Road misc art contract violated:\n  - " +
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
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("MRM_PreviewWorld")
    world.color = (0.025, 0.030, 0.033)
    scene.world = world
    return source, presentation


def create_material(tint_role: str) -> bpy.types.Material:
    material = bpy.data.materials.new(f"PREVIEW_MRM_{tint_role}")
    color = PREVIEW_COLORS[tint_role]
    material.diffuse_color = color
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
        "generator": "tools/build-mountain-road-misc-3d-model.py",
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
    ground_material = bpy.data.materials.new("PREVIEW_MRM_Ground")
    ground_material.diffuse_color = (0.055, 0.061, 0.059, 1.0)
    ground_mesh = bpy.data.meshes.new("MRM_PreviewGround_Mesh")
    ground_geometry = box((0.0, 0.0, -0.56), (13.0, 8.4, 0.10))
    ground_mesh.from_pydata(ground_geometry[0], [], ground_geometry[1])
    ground = bpy.data.objects.new("MRM_PreviewGround", ground_mesh)
    collection.objects.link(ground)
    ground.data.materials.append(ground_material)

    columns = 5
    spacing_x = 2.25
    spacing_y = 2.10
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
        data = bpy.data.lights.new(f"PREVIEW_MRM_{name}", "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(f"PREVIEW_MRM_{name}", data)
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
    camera_data = bpy.data.cameras.new("CAM_MountainRoadMiscPreview")
    camera = bpy.data.objects.new("CAM_MountainRoadMiscPreview", camera_data)
    result.presentation.objects.link(camera)
    camera.location = (8.8, -12.8, 8.2)
    target = Vector((0.0, 0.0, 0.20))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 11.8
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
        raise RuntimeError("Non-deterministic Mountain Road misc signature.")

    total_parts = sum(len(assembly.parts) for assembly in assemblies)
    total_triangles = sum(
        triangle_count(part.geometry)
        for assembly in assemblies for part in assembly.parts)
    if config.validate_only:
        print("MOUNTAIN ROAD MISC 3D DIRECT VALIDATION OK")
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
    print("MOUNTAIN ROAD MISC 3D BUILD OK")
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
