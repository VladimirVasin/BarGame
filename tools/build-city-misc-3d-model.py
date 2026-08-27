#!/usr/bin/env python3
"""Build the deterministic citywide authored City misc mesh catalog.

The catalog replaces the visible primitive geometry selected for City misc
waves 1A, 1B and the static shell of 1C. Runtime plans remain responsible for
world positions, stable IDs, collision, sitting, interaction docks, lights,
halos and emissive state. Every exported object is a passive mesh sub-asset.

Source space is Blender metres: X right, +Y forward and Z up. Export swaps the
last two axes, producing Unity X right, +Z forward and Y up. Decoration recipes
historically authored their local X on CityDecorationWorldBuilder.Tangent,
which is Unity object-local -X under Quaternion.LookRotation(Forward). The
recipe helpers therefore reflect legacy recipe X before authoring. That extra
reflection cancels the handedness change from the Y/Z axis swap, so faces stay
outward; validation proves positive signed volume for every mesh.

Run through Blender 5 from the repository root:

    blender --background --factory-startup --python \
      tools/build-city-misc-3d-model.py -- --validate-only

    blender --background --factory-startup --python \
      tools/build-city-misc-3d-model.py
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
from typing import Sequence

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import interior_kit as kit  # noqa: E402


GENERATOR_VERSION = "4.2.0"
DESIGN_ID = "city_misc_citywide_v4"
DISPLAY_NAME = "City Misc Citywide Catalog + Special Buildings"
V2_GENERATOR_VERSION = "2.0.0"
V2_DESIGN_ID = "city_misc_all_decor_v2"
V2_COMPATIBILITY_SIGNATURE = (
    "8ec3ffe04ffbcfba94cbf708d9c8263afbe853aeea4ffdeabfe638857a043193"
)
WAVE1_COMPATIBILITY_SIGNATURE = (
    "dd2e814d906fd2c7a7855c6d75ee54fe912ebb90f7cd02633c95c558d752f9f6"
)

DEFAULT_BLEND = ROOT / "ArtSource" / "City" / "Blender" / "CityMisc3D.blend"
DEFAULT_PREVIEW = ROOT / "ArtSource" / "City" / "Blender" / "CityMisc3D.png"
DEFAULT_FBX = ROOT / "Assets" / "City" / "Models" / "CityMisc3D.fbx"
DEFAULT_MANIFEST = ROOT / "Assets" / "City" / "Models" / "CityMisc3D.json"

SOURCE_COLLECTION = "SOURCE_CityMisc3D"
PRESENTATION_COLLECTION = "PRESENTATION_CityMisc3D"
ROOT_NAME = "ROOT_CityMisc3D"
PLACEMENT_CONTRACT = "ground_forward_frame"
SCALE_MODE = "fixed_meters"
BOUNDS_EPSILON = 1e-5
UV_EPSILON = 1e-6
MAX_TRIANGLES = 240000
FORWARD_ANCHORED_KINDS = {
    "OldTownScaffolding",
    "NightlifeFireEscape",
}

Vec2 = tuple[float, float]
Vec3 = tuple[float, float, float]
Face = tuple[int, ...]
Geometry = kit.Geometry


PREVIEW_COLORS = {
    "Industrial": (0.23, 0.255, 0.25, 1.0),
    "Street": (0.07, 0.085, 0.10, 1.0),
    "Masonry": (0.36, 0.25, 0.14, 1.0),
    "Neon": (1.0, 0.12, 0.62, 1.0),
    "Bark": (0.20, 0.105, 0.052, 1.0),
    "Foliage": (0.12, 0.235, 0.15, 1.0),
    "Timber": (0.32, 0.205, 0.095, 1.0),
    "Residential": (0.16, 0.29, 0.31, 1.0),
    "BacklitSign": (0.74, 0.92, 0.72, 1.0),
    "Fixture": (0.085, 0.095, 0.12, 1.0),
    "Masonry_Stone": (0.39, 0.37, 0.34, 1.0),
    "Street_Stone": (0.105, 0.11, 0.115, 1.0),
    "Residential_Timber": (0.34, 0.22, 0.11, 1.0),
    "Masonry_Timber": (0.29, 0.17, 0.075, 1.0),
    "Street_Timber": (0.10, 0.075, 0.050, 1.0),
    "Street_PaintedMetal": (0.075, 0.10, 0.115, 1.0),
    "Residential_PaintedMetal": (0.18, 0.30, 0.31, 1.0),
}

SURFACE_KINDS = {
    "Industrial": "IndustrialMetal",
    "Street": "StreetMetal",
    "Masonry": "Masonry",
    "Neon": "Neon",
    "Bark": "Bark",
    "Foliage": "Foliage",
    "Timber": "Timber",
    "Residential": "ResidentialGlass",
    "BacklitSign": "BacklitSign",
    "Fixture": "FixtureMetal",
    "Masonry_Stone": "Stone",
    "Street_Stone": "Stone",
    "Residential_Timber": "Timber",
    "Masonry_Timber": "Timber",
    "Street_Timber": "Timber",
    "Street_PaintedMetal": "PaintedMetal",
    "Residential_PaintedMetal": "PaintedMetal",
}


@dataclass(frozen=True)
class ScaleParameterSpec:
    name: str
    source_axes: tuple[str, ...]
    reference: float
    minimum: float
    maximum: float


@dataclass(frozen=True)
class PartSpec:
    mesh: str
    kind: str
    variant: int
    part_role: str
    geometry: Geometry
    placement_mode: str = "recipe_local_rigid"
    normalized_height_m: float | None = None

    @property
    def surface_kind(self) -> str:
        return SURFACE_KINDS[self.part_role]

    @property
    def tint_role(self) -> str:
        return self.part_role


@dataclass(frozen=True)
class AssemblySpec:
    kind: str
    variant: int
    parts: tuple[PartSpec, ...]
    scale_mode: str = SCALE_MODE
    placement_contract: str = PLACEMENT_CONTRACT
    canonical_reference: tuple[tuple[str, float], ...] = ()
    scale_parameters: tuple[ScaleParameterSpec, ...] = ()
    unity_owned_parts: tuple[str, ...] = ()
    root_derivation: str = ""
    coordinate_profile: str = "legacy_recipe_reflected_x"
    expected_source_min_z: float | None = None


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
        faces.append((side, following, sides + following, sides + side))
    return vertices, faces


def ring_solid_z(
    rings_data: Sequence[tuple[float, float, float, float, float, float]],
    sides: int = 8,
) -> Geometry:
    """Closed faceted solid; rings are z, cx, cy, rx, ry and phase."""
    vertices: list[Vec3] = []
    rings: list[tuple[int, ...]] = []
    for z, cx, cy, rx, ry, phase in rings_data:
        start = len(vertices)
        for side in range(sides):
            angle = side / sides * math.tau + phase
            vertices.append((
                cx + math.cos(angle) * rx,
                cy + math.sin(angle) * ry,
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
    outer_radius: float,
    inner_radius: float,
    depth: float,
    sides: int = 12,
) -> Geometry:
    cx, cy, cz = center
    vertices: list[Vec3] = []
    rings: list[tuple[int, ...]] = []
    for y in (cy - depth * 0.5, cy + depth * 0.5):
        for radius in (outer_radius, inner_radius):
            start = len(vertices)
            vertices.extend((
                cx + math.cos(side / sides * math.tau) * radius,
                y,
                cz + math.sin(side / sides * math.tau) * radius,
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


def annulus_z(
    center: Vec3,
    outer_radius: float,
    inner_radius: float,
    height: float,
    sides: int = 16,
) -> Geometry:
    cx, cy, cz = center
    vertices: list[Vec3] = []
    rings: list[tuple[int, ...]] = []
    for z in (cz - height * 0.5, cz + height * 0.5):
        for radius in (outer_radius, inner_radius):
            start = len(vertices)
            vertices.extend((
                cx + math.cos(side / sides * math.tau) * radius,
                cy + math.sin(side / sides * math.tau) * radius,
                z,
            ) for side in range(sides))
            rings.append(tuple(range(start, start + sides)))
    bottom_outer, bottom_inner, top_outer, top_inner = rings
    faces: list[Face] = []
    for side in range(sides):
        following = (side + 1) % sides
        faces.extend((
            (bottom_outer[following], bottom_outer[side],
             top_outer[side], top_outer[following]),
            (bottom_inner[side], bottom_inner[following],
             top_inner[following], top_inner[side]),
            (bottom_outer[side], bottom_outer[following],
             bottom_inner[following], bottom_inner[side]),
            (top_outer[following], top_outer[side],
             top_inner[side], top_inner[following]),
        ))
    return vertices, faces


def prism_y(
    profile_xz: Sequence[tuple[float, float]],
    y0: float,
    y1: float,
) -> Geometry:
    count = len(profile_xz)
    vertices = [(x, y0, z) for x, z in profile_xz]
    vertices.extend((x, y1, z) for x, z in profile_xz)
    faces: list[Face] = [
        tuple(reversed(range(count))),
        tuple(range(count, count * 2)),
    ]
    for index in range(count):
        following = (index + 1) % count
        faces.append((index, following, count + following, count + index))
    return vertices, faces


def source_box(center: Vec3, size: Vec3, chamfer: float = 0.025) -> Geometry:
    return kit.chamfered_box(center, size, chamfer)


def recipe_point(
    x: float,
    height: float,
    forward: float,
    depth_scale: float = 1.0,
) -> Vec3:
    """Legacy recipe coordinates to source, including the X reflection."""
    return -x, forward * depth_scale, height


def recipe_box(
    x: float,
    height: float,
    forward: float,
    width: float,
    vertical_size: float,
    depth: float,
    depth_scale: float = 1.0,
    chamfer: float = 0.025,
) -> Geometry:
    return source_box(
        recipe_point(x, height, forward, depth_scale),
        (width, depth * depth_scale, vertical_size),
        chamfer,
    )


def recipe_tube(
    start: tuple[float, float, float],
    end: tuple[float, float, float],
    radius: float,
    depth_scale: float = 1.0,
    sides: int = 8,
    end_radius: float | None = None,
) -> Geometry:
    return tube_between(
        recipe_point(*start, depth_scale),
        recipe_point(*end, depth_scale),
        radius,
        radius if end_radius is None else end_radius,
        sides,
    )


def recipe_vertical_solid(
    x: float,
    y0: float,
    y1: float,
    forward: float,
    radius_x: float,
    radius_forward: float,
    depth_scale: float = 1.0,
    top_scale: float = 1.0,
    sides: int = 8,
) -> Geometry:
    sx, sy, _ = recipe_point(x, 0.0, forward, depth_scale)
    return ring_solid_z((
        (y0, sx, sy, radius_x, radius_forward * depth_scale, 0.0),
        (y1, sx, sy, radius_x * top_scale,
         radius_forward * depth_scale * top_scale, 0.08),
    ), sides)


def mesh_id(kind: str, variant: int, role: str) -> str:
    return f"GEO_CMM_{kind}_Variant{variant + 1:02d}_{role}"


def make_part(
    kind: str,
    variant: int,
    role: str,
    *geometries: Geometry,
    placement_mode: str = "recipe_local_rigid",
    normalized_height_m: float | None = None,
) -> PartSpec:
    return PartSpec(
        mesh_id(kind, variant, role),
        kind,
        variant,
        role,
        kit.merge(*geometries),
        placement_mode,
        normalized_height_m,
    )


def make_named_part(
    kind: str,
    variant: int,
    suffix: str,
    role: str,
    *geometries: Geometry,
    placement_mode: str = "recipe_local_rigid",
    normalized_height_m: float | None = None,
) -> PartSpec:
    return PartSpec(
        f"GEO_CMM_{kind}_Variant{variant + 1:02d}_{suffix}",
        kind,
        variant,
        role,
        kit.merge(*geometries),
        placement_mode,
        normalized_height_m,
    )


def scale_parameter(
    name: str,
    source_axes: tuple[str, ...],
    reference: float,
    minimum: float,
    maximum: float,
) -> ScaleParameterSpec:
    return ScaleParameterSpec(
        name, source_axes, reference, minimum, maximum)


def rotate_source(
    point: Vec3,
    pitch_x_degrees: float = 0.0,
    roll_forward_degrees: float = 0.0,
    yaw_up_degrees: float = 0.0,
) -> Vec3:
    """Rotate source X/forward/up like Unity local pitch/roll/yaw."""
    x, forward, up = point
    if pitch_x_degrees:
        angle = math.radians(pitch_x_degrees)
        cosine, sine = math.cos(angle), math.sin(angle)
        forward, up = (
            cosine * forward + sine * up,
            -sine * forward + cosine * up,
        )
    if roll_forward_degrees:
        angle = math.radians(roll_forward_degrees)
        cosine, sine = math.cos(angle), math.sin(angle)
        x, up = (
            cosine * x + sine * up,
            -sine * x + cosine * up,
        )
    if yaw_up_degrees:
        angle = math.radians(yaw_up_degrees)
        cosine, sine = math.cos(angle), math.sin(angle)
        x, forward = (
            cosine * x + sine * forward,
            -sine * x + cosine * forward,
        )
    return x, forward, up


def transform_geometry(
    geometry: Geometry,
    translation: Vec3 = (0.0, 0.0, 0.0),
    pitch_x_degrees: float = 0.0,
    roll_forward_degrees: float = 0.0,
    yaw_up_degrees: float = 0.0,
) -> Geometry:
    vertices, faces = geometry
    return (
        [
            add(rotate_source(
                vertex,
                pitch_x_degrees,
                roll_forward_degrees,
                yaw_up_degrees,
            ), translation)
            for vertex in vertices
        ],
        list(faces),
    )


def local_point(x: float, height: float, forward: float) -> Vec3:
    """Unity root-local x/y/z expressed in Blender source X/Y/Z."""
    return x, forward, height


def local_box(
    x: float,
    height: float,
    forward: float,
    width: float,
    vertical_size: float,
    depth: float,
    chamfer: float = 0.025,
    pitch_x_degrees: float = 0.0,
    roll_forward_degrees: float = 0.0,
    yaw_up_degrees: float = 0.0,
) -> Geometry:
    geometry = source_box(
        (0.0, 0.0, 0.0),
        (width, depth, vertical_size),
        min(chamfer, width * 0.20, vertical_size * 0.20, depth * 0.20),
    )
    return transform_geometry(
        geometry,
        local_point(x, height, forward),
        pitch_x_degrees,
        roll_forward_degrees,
        yaw_up_degrees,
    )


def local_tube(
    start: tuple[float, float, float],
    end: tuple[float, float, float],
    radius: float,
    sides: int = 8,
    end_radius: float | None = None,
) -> Geometry:
    return tube_between(
        local_point(*start),
        local_point(*end),
        radius,
        radius if end_radius is None else end_radius,
        sides,
    )


def local_vertical_solid(
    x: float,
    y0: float,
    y1: float,
    forward: float,
    radius_x: float,
    radius_forward: float,
    top_scale: float = 1.0,
    sides: int = 8,
) -> Geometry:
    return ring_solid_z((
        (y0, x, forward, radius_x, radius_forward, 0.0),
        (y1, x, forward,
         radius_x * top_scale, radius_forward * top_scale, 0.08),
    ), sides)


def local_faceted_mass(
    x: float,
    y0: float,
    y_mid: float,
    y1: float,
    forward: float,
    radius_x: float,
    radius_forward: float,
    top_scale: float = 0.18,
    sides: int = 8,
    phase: float = 0.0,
) -> Geometry:
    return ring_solid_z((
        (y0, x, forward, radius_x * 0.72,
         radius_forward * 0.72, phase),
        (y_mid, x, forward, radius_x, radius_forward, phase + 0.08),
        (y1, x, forward, radius_x * top_scale,
         radius_forward * top_scale, phase + 0.15),
    ), sides)


# ----------------------------------------------------- formal recipes --


def build_industrial_stacks_and_tanks() -> AssemblySpec:
    kind = "IndustrialStacksAndTanks"
    variant = 0
    industrial: list[Geometry] = []
    street: list[Geometry] = []

    for side in (-1, 1):
        stack_x = side * 2.15
        sx, sy, _ = recipe_point(stack_x, 0.0, -0.72)
        industrial.append(ring_solid_z((
            (0.0, sx, sy, 0.43, 0.43, 0.0),
            (0.26, sx, sy, 0.46, 0.46, 0.05),
            (4.76, sx + side * 0.035, sy, 0.34, 0.34, 0.11),
            (5.30, sx + side * 0.045, sy, 0.29, 0.29, 0.02),
        ), 10))
        for level, radius in ((0.24, 0.49), (2.15, 0.395), (4.72, 0.39)):
            street.append(ring_solid_z((
                (level - 0.055, sx, sy, radius, radius, 0.0),
                (level + 0.055, sx, sy, radius, radius, 0.0),
            ), 10))
        street.append(ring_solid_z((
            (5.28, sx + side * 0.045, sy, 0.53, 0.53, 0.0),
            (5.43, sx + side * 0.045, sy, 0.53, 0.53, 0.0),
        ), 10))

        tank_x = side * 1.05
        tx, ty, _ = recipe_point(tank_x, 0.0, 1.05)
        industrial.extend((
            ring_solid_z((
                (0.12, tx, ty, 0.78, 0.78, 0.0),
                (0.36, tx, ty, 0.78, 0.78, 0.0),
            ), 10),
            ring_solid_z((
                (0.34, tx, ty, 0.67, 0.67, 0.04),
                (2.22, tx, ty, 0.67, 0.67, 0.04),
                (2.52, tx, ty, 0.48, 0.48, 0.10),
            ), 10),
        ))
        street.extend((
            ring_solid_z((
                (2.48, tx, ty, 0.74, 0.74, 0.0),
                (2.66, tx, ty, 0.74, 0.74, 0.0),
            ), 10),
            recipe_tube((tank_x - 0.50, 0.42, 1.05),
                        (tank_x - 0.50, 2.20, 1.05), 0.035, sides=7),
        ))
        for rung in range(5):
            street.append(recipe_tube(
                (tank_x - 0.57, 0.62 + rung * 0.31, 1.05),
                (tank_x - 0.43, 0.62 + rung * 0.31, 1.05),
                0.025,
                sides=6,
            ))

    industrial.extend((
        recipe_tube((-1.15, 0.72, 0.96),
                    (1.15, 0.72, 0.96), 0.12, sides=8),
        recipe_tube((-1.82, 1.34, -0.68),
                    (1.82, 1.34, -0.68), 0.11, sides=8),
        recipe_tube((0.0, 0.72, 0.96),
                    (0.0, 1.34, -0.68), 0.10, sides=8),
    ))
    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Industrial", *industrial),
        make_part(kind, variant, "Street", *street),
    ))


def build_industrial_cargo(variant: int) -> AssemblySpec:
    kind = "IndustrialCargo"
    mirror = 1.0 if variant == 0 else -1.0
    depth_scale = 0.28
    industrial: list[Geometry] = []
    street: list[Geometry] = []
    masonry: list[Geometry] = []

    cargo_specs = (
        (-1.65 * mirror, 1.15, 0.0, 3.10, 2.30, 1.75),
        (1.35 * mirror, 0.82, 0.35, 2.40, 1.64, 1.55),
    )
    for cargo_index, (x, y, z, width, height, depth) in enumerate(cargo_specs):
        industrial.append(recipe_box(
            x, y, z, width, height, depth, depth_scale, 0.065))
        front = z + depth * 0.5
        rib_count = 7 if cargo_index == 0 else 5
        for rib in range(rib_count):
            rib_x = x + ((rib / (rib_count - 1)) - 0.5) * (width - 0.26)
            street.append(recipe_box(
                rib_x, y, front + 0.035,
                0.055, height - 0.18, 0.06,
                depth_scale, 0.008))
        street.extend((
            recipe_box(x, y + height * 0.42, front + 0.045,
                       width - 0.16, 0.08, 0.07,
                       depth_scale, 0.008),
            recipe_box(x, y - height * 0.42, front + 0.045,
                       width - 0.16, 0.08, 0.07,
                       depth_scale, 0.008),
        ))
        for side in (-1, 1):
            street.append(recipe_vertical_solid(
                x + side * (width * 0.5 - 0.12),
                0.18,
                height - 0.15,
                front + 0.075,
                0.045,
                0.045,
                depth_scale,
                sides=6,
            ))

    masonry.extend((
        recipe_box(-1.65 * mirror, 0.12, 0.0,
                   3.30, 0.24, 1.95, depth_scale, 0.035),
        recipe_box(1.35 * mirror, 0.12, 0.35,
                   2.60, 0.24, 1.72, depth_scale, 0.035),
    ))
    small_crates = (
        (-0.10 * mirror, 0.38, 1.65, 0.72, 0.76, 0.72),
        (0.72 * mirror, 0.28, 1.70, 0.54, 0.56, 0.54),
        (-0.72 * mirror, 0.22, 1.74, 0.44, 0.44, 0.44),
    )
    for index, values in enumerate(small_crates):
        x, y, z, width, height, depth = values
        masonry.append(recipe_box(
            x, y, z, width, height, depth, depth_scale, 0.035))
        street.extend((
            recipe_box(x, y, z + depth * 0.51,
                       width * 0.72, 0.045, 0.035,
                       depth_scale, 0.006),
            recipe_box(x + (0.08 if index & 1 else -0.08) * mirror,
                       y, z + depth * 0.52,
                       0.04, height * 0.72, 0.035,
                       depth_scale, 0.006),
        ))

    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Industrial", *industrial),
        make_part(kind, variant, "Street", *street),
        make_part(kind, variant, "Masonry", *masonry),
    ))


def build_nightlife_vending_and_queue() -> AssemblySpec:
    kind = "NightlifeVendingAndQueue"
    variant = 0
    depth_scale = 0.45
    street: list[Geometry] = []
    industrial: list[Geometry] = []
    neon: list[Geometry] = []

    for side in (-1, 1):
        x = side * 0.72
        street.extend((
            recipe_box(x, 1.02, -0.35,
                       1.15, 2.04, 0.72, depth_scale, 0.055),
            recipe_box(x, 1.96, -0.34,
                       0.94, 0.08, 0.60, depth_scale, 0.014),
        ))
        neon.append(recipe_box(
            x, 1.32, 0.03, 0.80, 0.72, 0.05,
            depth_scale, 0.006))
        for row in range(3):
            street.append(recipe_box(
                x, 1.08 + row * 0.24, 0.062,
                0.82, 0.035, 0.025, depth_scale, 0.004))
        for column in (-0.135, 0.135):
            street.append(recipe_box(
                x + column, 1.32, 0.064,
                0.035, 0.74, 0.025, depth_scale, 0.004))
        industrial.extend((
            recipe_box(x, 0.62, 0.04,
                       0.74, 0.16, 0.05, depth_scale, 0.008),
            recipe_box(x + side * 0.34, 0.94, 0.055,
                       0.10, 0.20, 0.05, depth_scale, 0.008),
        ))

    for post in range(4):
        x = -2.35 + post * 1.55
        street.extend((
            recipe_vertical_solid(
                x, 0.0, 1.0, 1.20, 0.08, 0.08,
                depth_scale, 0.88, 8),
            recipe_vertical_solid(
                x, 0.98, 1.08, 1.20, 0.13, 0.13,
                depth_scale, 0.82, 8),
        ))
    street.extend((
        recipe_tube((-2.28, 0.78, 1.20),
                    (-0.88, 0.78, 1.20), 0.04, depth_scale, 8),
        recipe_tube((0.82, 0.78, 1.20),
                    (2.22, 0.78, 1.20), 0.04, depth_scale, 8),
    ))
    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Street", *street),
        make_part(kind, variant, "Industrial", *industrial),
        make_part(kind, variant, "Neon", *neon),
    ))


def build_roadside_roadwork_and_bicycle(variant: int) -> AssemblySpec:
    kind = "RoadsideRoadworkAndBicycle"
    mirror = 1.0 if variant == 0 else -1.0
    depth_scale = 0.44
    street: list[Geometry] = []
    masonry: list[Geometry] = []
    industrial: list[Geometry] = []

    for side in (-1, 1):
        x = side * 1.45
        street.extend((
            recipe_vertical_solid(
                x, 0.0, 1.04, 0.0, 0.09, 0.09,
                depth_scale, 0.82, 8),
            recipe_box(x, 0.12, 0.0,
                       0.72, 0.14, 0.34, depth_scale, 0.018),
        ))
    masonry.extend((
        recipe_box(0.0, 0.82, 0.0,
                   3.20, 0.38, 0.16, depth_scale, 0.035),
        recipe_box(-0.80, 0.825, 0.09,
                   0.72, 0.17, 0.04, depth_scale, 0.008),
        recipe_box(0.80, 0.825, 0.09,
                   0.72, 0.17, 0.04, depth_scale, 0.008),
    ))
    for side in (-1, 1):
        cone_x = side * 1.65
        masonry.extend((
            recipe_box(cone_x, 0.045, 1.12,
                       0.50, 0.09, 0.50, depth_scale, 0.015),
            recipe_vertical_solid(
                cone_x, 0.09, 0.56, 1.12,
                0.20, 0.20, depth_scale, 0.20, 8),
        ))

    bike_x = mirror * 0.55
    wheel_centers = (bike_x - 0.62, bike_x + 0.62)
    for x in wheel_centers:
        street.append(annulus_y(
            recipe_point(x, 0.54, -1.15, depth_scale),
            0.47,
            0.39,
            0.075,
            12,
        ))
    crank = (bike_x, 0.58, -1.15)
    rear = (wheel_centers[0], 0.54, -1.15)
    front = (wheel_centers[1], 0.54, -1.15)
    seat_joint = (bike_x - 0.16 * mirror, 1.02, -1.15)
    handle_joint = (bike_x + 0.38 * mirror, 1.05, -1.15)
    industrial.extend((
        recipe_tube(rear, crank, 0.035, depth_scale, 7),
        recipe_tube(crank, seat_joint, 0.038, depth_scale, 7),
        recipe_tube(seat_joint, rear, 0.032, depth_scale, 7),
        recipe_tube(seat_joint, handle_joint, 0.034, depth_scale, 7),
        recipe_tube(handle_joint, front, 0.032, depth_scale, 7),
        recipe_tube(crank, handle_joint, 0.032, depth_scale, 7),
    ))
    street.extend((
        recipe_box(seat_joint[0] - 0.04 * mirror, 1.10, -1.15,
                   0.38, 0.08, 0.22, depth_scale, 0.015),
        recipe_tube(handle_joint,
                    (bike_x + 0.54 * mirror, 1.24, -1.15),
                    0.026, depth_scale, 7),
        recipe_tube((bike_x + 0.38 * mirror, 1.24, -1.34),
                    (bike_x + 0.38 * mirror, 1.24, -0.96),
                    0.022, depth_scale, 7),
        recipe_vertical_solid(
            bike_x, 0.50, 0.66, -1.15,
            0.10, 0.10, depth_scale, 0.90, 8),
    ))
    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Street", *street),
        make_part(kind, variant, "Masonry", *masonry),
        make_part(kind, variant, "Industrial", *industrial),
    ))


# -------------------------------------------------------- park kit --


def build_park_tree(variant: int) -> AssemblySpec:
    kind = "ParkTree"
    height = 2.80 + variant * 0.24
    phases = (0.00, 0.12, 0.23, 0.34)
    leans = ((0.00, 0.00), (0.08, -0.04), (-0.07, 0.06), (0.05, 0.08))
    phase = phases[variant]
    lean_x, lean_y = leans[variant]
    bark: list[Geometry] = [ring_solid_z((
        (0.0, 0.0, 0.0, 0.31, 0.30, phase),
        (0.28, 0.0, 0.0, 0.34, 0.32, phase + 0.04),
        (height * 0.58, lean_x * 0.45, lean_y * 0.45,
         0.235, 0.22, phase - 0.02),
        (height, lean_x, lean_y, 0.15, 0.14, phase + 0.07),
    ), 9)]

    branch_specs = (
        ((-0.02, 0.01, height * 0.60),
         (-0.82, 0.24, height + 0.28), 0.14, 0.065),
        ((0.02, -0.01, height * 0.68),
         (0.72, -0.38, height + 0.42), 0.13, 0.055),
        ((0.01, 0.02, height * 0.78),
         (0.28, 0.70, height + 0.55), 0.10, 0.045),
    )
    for index, (start, end, radius, end_radius) in enumerate(branch_specs):
        offset = (variant - 1.5) * 0.025
        adjusted_end = (
            end[0] + (offset if index != 1 else -offset),
            end[1] - offset * (index + 1),
            end[2] + ((variant + index) % 2) * 0.05,
        )
        bark.append(tube_between(start, adjusted_end, radius, end_radius, 7))

    foliage: list[Geometry] = []
    cluster_specs = (
        (0.0, 0.0, height + 1.18, 1.34, 1.26, 1.25),
        (-0.68, 0.28, height + 0.82, 0.88, 0.78, 0.86),
        (0.62, -0.38, height + 0.95, 0.92, 0.80, 0.92),
        (0.18, 0.70, height + 1.18, 0.75, 0.70, 0.78),
    )
    for index, (cx, cy, cz, rx, ry, rz) in enumerate(cluster_specs):
        shift = (variant - 1.5) * 0.035
        cx += shift * (1 if index & 1 else -1)
        cy += shift * (1 if index > 1 else -1)
        foliage.append(ring_solid_z((
            (cz - rz, cx, cy, rx * 0.18, ry * 0.18, phase),
            (cz - rz * 0.34, cx - 0.04, cy + 0.03,
             rx, ry, phase + 0.07),
            (cz + rz * 0.32, cx + 0.05, cy - 0.04,
             rx * 0.84, ry * 0.86, phase - 0.04),
            (cz + rz, cx, cy, rx * 0.12, ry * 0.12, phase + 0.10),
        ), 8))

    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Bark", *bark),
        make_part(kind, variant, "Foliage", *foliage),
    ))


def build_park_bench(variant: int) -> AssemblySpec:
    kind = "ParkBench"
    timber: list[Geometry] = []
    seat_width = 2.20
    seat_top = 0.71
    slat_depth = 0.105
    for slat in range(5):
        y = -0.235 + slat * 0.1175
        skew = (slat - 2) * (0.004 if variant == 0 else -0.006)
        timber.append(source_box(
            (skew, y, seat_top - 0.055),
            (seat_width - 0.08, slat_depth, 0.11),
            0.018,
        ))

    for side in (-1, 1):
        x = side * 0.72
        timber.extend((
            source_box((x, 0.0, 0.30), (0.18, 0.46, 0.60), 0.025),
            source_box((x, -0.255, 0.88), (0.16, 0.15, 1.02), 0.024),
            tube_between(
                (x, -0.21, 0.64),
                (x, -0.31, 1.28 + variant * 0.04),
                0.070,
                0.055,
                7,
            ),
        ))
    back_rows = 3 if variant == 0 else 4
    for row in range(back_rows):
        z = 0.87 + row * (0.17 if variant == 0 else 0.135)
        timber.append(source_box(
            ((row - 1) * (0.006 if variant == 1 else 0.0), -0.315, z),
            (2.02, 0.095, 0.105 if variant == 0 else 0.085),
            0.016,
        ))
    timber.extend((
        source_box((0.0, -0.02, 0.49), (1.82, 0.16, 0.13), 0.018),
        source_box((0.0, -0.29, 1.31 + variant * 0.035),
                   (2.12, 0.12, 0.12), 0.018),
    ))
    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Timber", *timber),
    ))


# ------------------------------------------------------ roadside kit --


def build_roadside_phone_booth() -> AssemblySpec:
    kind = "RoadsidePhoneBooth"
    variant = 0
    depth_scale = 0.82
    street: list[Geometry] = []
    residential: list[Geometry] = []
    backlit: list[Geometry] = []

    street.append(recipe_box(
        0.0, 0.10, 0.0, 1.38, 0.20, 1.28, depth_scale, 0.035))
    for x_side in (-1, 1):
        for z_side in (-1, 1):
            street.append(recipe_vertical_solid(
                x_side * 0.62, 0.18, 2.60, z_side * 0.56,
                0.08, 0.08, depth_scale, 0.90, 8))
    street.extend((
        recipe_box(0.0, 2.65, 0.0,
                   1.48, 0.22, 1.38, depth_scale, 0.04),
        recipe_box(0.0, 2.65, 0.66,
                   1.50, 0.30, 0.12, depth_scale, 0.025),
        recipe_box(0.0, 1.42, 0.59,
                   1.05, 1.90, 0.08, depth_scale, 0.025),
        recipe_box(-0.30, 1.46, 0.65,
                   0.28, 0.62, 0.10, depth_scale, 0.018),
        recipe_tube((-0.16, 1.64, 0.71),
                    (-0.16, 1.20, 0.71), 0.035, depth_scale, 7),
        recipe_tube((-0.16, 1.64, 0.71),
                    (0.06, 1.53, 0.71), 0.035, depth_scale, 7),
    ))
    for x in (-0.38, 0.0, 0.38):
        street.append(recipe_box(
            x, 1.54, -0.575, 0.055, 1.60, 0.045,
            depth_scale, 0.006))
    for z_side in (-1, 1):
        for x in (-0.63, 0.63):
            street.append(recipe_box(
                x, 1.54, z_side * 0.20,
                0.045, 1.60, 0.42, depth_scale, 0.006))

    residential.extend((
        recipe_box(0.0, 1.55, -0.57,
                   1.08, 1.65, 0.04, depth_scale, 0.005),
        recipe_box(-0.63, 1.55, 0.0,
                   0.04, 1.65, 0.92, depth_scale, 0.005),
        recipe_box(0.63, 1.55, 0.0,
                   0.04, 1.65, 0.92, depth_scale, 0.005),
    ))
    backlit.append(recipe_box(
        0.0, 2.65, 0.725,
        1.32, 0.20, 0.03, depth_scale, 0.004))
    for stroke in range(-3, 4):
        street.append(recipe_box(
            stroke * 0.165, 2.65, 0.748,
            0.045, 0.13 if stroke % 2 == 0 else 0.10, 0.015,
            depth_scale, 0.003))

    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Street", *street),
        make_part(kind, variant, "Residential", *residential),
        make_part(kind, variant, "BacklitSign", *backlit),
    ))


def build_roadside_dumpster_and_utility() -> AssemblySpec:
    kind = "RoadsideDumpsterAndUtility"
    variant = 0
    depth_scale = 0.72
    industrial: list[Geometry] = []
    street: list[Geometry] = []

    industrial.append(recipe_box(
        -0.65, 0.62, 0.0,
        2.45, 1.24, 1.35, depth_scale, 0.07))
    for rib_x in (-1.48, -1.08, -0.68, -0.28, 0.12):
        industrial.append(recipe_box(
            rib_x, 0.66, 0.685,
            0.075, 0.92, 0.055, depth_scale, 0.009))
    industrial.extend((
        recipe_box(1.35, 0.98, 0.16,
                   0.88, 1.42, 0.05, depth_scale, 0.012),
        recipe_box(1.35, 1.28, 0.20,
                   0.62, 0.08, 0.05, depth_scale, 0.008),
        recipe_box(1.35, 1.05, 0.20,
                   0.62, 0.08, 0.05, depth_scale, 0.008),
    ))

    street.extend((
        recipe_box(-1.25, 1.34, -0.08,
                   1.16, 0.16, 1.42, depth_scale, 0.025),
        recipe_box(-0.05, 1.34, -0.08,
                   1.16, 0.16, 1.42, depth_scale, 0.025),
        recipe_box(-1.55, 0.64, 0.71,
                   0.12, 0.92, 0.10, depth_scale, 0.012),
        recipe_box(0.25, 0.64, 0.71,
                   0.12, 0.92, 0.10, depth_scale, 0.012),
        recipe_box(1.35, 0.92, -0.22,
                   1.15, 1.84, 0.72, depth_scale, 0.055),
        recipe_box(1.35, 0.08, -0.22,
                   1.24, 0.16, 0.82, depth_scale, 0.025),
        recipe_tube((1.08, 1.54, 0.15),
                    (1.08, 1.68, 0.15), 0.028, depth_scale, 7),
    ))
    for wheel_x in (-1.42, 0.12):
        for wheel_z in (-0.55, 0.55):
            center = recipe_point(wheel_x, 0.16, wheel_z, depth_scale)
            street.append(tube_between(
                (center[0], center[1] - 0.07, center[2]),
                (center[0], center[1] + 0.07, center[2]),
                0.13,
                0.13,
                8,
            ))
    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Industrial", *industrial),
        make_part(kind, variant, "Street", *street),
    ))


def build_lot_ground_downpipe_outfall() -> AssemblySpec:
    """Where a facade downpipe finally reaches the ground.

    The art bible has promised this since the Old Town section — «кабели,
    водостоки и трубы образуют многолетнюю сеть» — and every downpipe in
    the city has so far ended in nothing. This is the other end: a cast
    shoe turning the pipe out of the wall, a splash block under it, and
    the runnel the water has cut across the strip of bare soil that
    rings every building. Forward is the wall's outward normal, so the
    shoe leans out of the facade and the runnel runs away from it.
    """
    kind, variant = "LotGroundDownpipeOutfall", 0
    street: list[Geometry] = [
        # The last length of pipe against the wall, and the shoe that
        # turns it out.
        local_vertical_solid(0.0, 0.10, 0.86, -0.06,
                             0.058, 0.058, top_scale=1.0, sides=9),
        local_tube((0.0, 0.115, -0.055), (0.0, 0.115, 0.175),
                   0.062, sides=9),
        local_box(0.0, 0.905, -0.055, 0.185, 0.075, 0.075, 0.010),
        # The strap that holds it to the brick.
        local_box(0.0, 0.560, -0.098, 0.215, 0.038, 0.030, 0.006),
    ]
    masonry: list[Geometry] = [
        # The splash block, and the runnel worn into the soil beyond it.
        local_box(0.0, 0.032, 0.235, 0.52, 0.064, 0.44, 0.014),
        local_box(0.0, 0.012, 0.640, 0.30, 0.024, 0.42, 0.008),
        local_box(0.0, 0.008, 0.980, 0.22, 0.016, 0.30, 0.006),
    ]
    return AssemblySpec(
        kind, variant, (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Masonry", *masonry),
        ),
        root_derivation="LotGroundDownpipeOutfall.WallFoot+OutwardNormal",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("pipe_height", 0.94), ("runnel_reach", 1.13)),
    )


def build_roadside_drain_and_cover() -> AssemblySpec:
    """A gutter grate and a valve lid, both flush with the pavement.

    The city dies of its water and shows none of it: until now the only
    municipal water anywhere on the ground was the one waterworks court.
    This is the network the rest of the time — walked over, never looked
    at. Everything sits within `56 mm` of the ground so nothing here is
    ever an obstacle; the runtime gives it no collider at all.
    """
    kind, variant = "RoadsideDrainAndCover", 0
    street: list[Geometry] = [
        # The grate: a frame sunk at the kerb line with five bars.
        local_box(0.0, 0.020, 0.0, 0.66, 0.040, 0.42, 0.008),
        *(local_box(bar_x, 0.048, 0.0, 0.052, 0.016, 0.34, 0.004)
          for bar_x in (-0.20, -0.10, 0.0, 0.10, 0.20)),
        # The lid, set apart the way a valve chamber always is.
        local_vertical_solid(0.62, 0.0, 0.034, 0.10,
                             0.155, 0.155, top_scale=0.94, sides=12),
        local_vertical_solid(0.62, 0.034, 0.050, 0.10,
                             0.048, 0.048, top_scale=0.80, sides=8),
    ]
    masonry: list[Geometry] = [
        # The concrete the ironwork is bedded into, proud by a
        # centimetre so the joint reads without becoming a lip.
        local_box(0.0, 0.014, 0.0, 0.80, 0.028, 0.56, 0.010),
        local_vertical_solid(0.62, 0.0, 0.022, 0.10,
                             0.205, 0.205, top_scale=0.96, sides=12),
    ]
    return AssemblySpec(
        kind, variant, (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Masonry", *masonry),
        ),
        root_derivation="RoadsideDrainAndCover.DescriptorGround+Forward",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("grate_width", 0.66), ("lid_offset", 0.62)),
    )


def build_roadside_capped_standpipe() -> AssemblySpec:
    """A street standpipe welded shut, over a trough gone dry.

    The municipal answer to the water happened years before the hero and
    was abandoned: the column was not removed, it was capped, and the
    trough it fed was left where it stood. It is the same municipal
    grammar as the working waterworks court at the other end of the
    pipe, which is the point — one is used, this one was given up on.
    """
    kind, variant = "RoadsideCappedStandpipe", 0
    street: list[Geometry] = [
        local_vertical_solid(0.0, 0.0, 0.09, 0.0,
                             0.145, 0.145, top_scale=0.82, sides=10),
        local_vertical_solid(0.0, 0.06, 0.96, 0.0,
                             0.085, 0.085, top_scale=0.86, sides=10),
        # The cap and its weld bead: not a lid, a plate run round.
        local_vertical_solid(0.0, 0.950, 0.978, 0.0,
                             0.098, 0.098, top_scale=1.06, sides=10),
        local_box(0.0, 1.000, 0.0, 0.215, 0.044, 0.215, 0.010),
        # The spout, cut off and blanked where it used to run.
        local_tube((0.0, 0.700, 0.02), (0.0, 0.700, 0.185),
                   0.036, sides=8),
        local_box(0.0, 0.700, 0.200, 0.092, 0.092, 0.020, 0.006),
        # The chain that held the cup, still on its eye.
        local_tube((0.072, 0.905, 0.055), (0.070, 0.745, 0.095),
                   0.011, sides=6),
    ]
    masonry: list[Geometry] = [
        local_box(0.0, 0.025, 0.0, 0.72, 0.050, 0.62, 0.015),
        local_box(0.0, 0.115, 0.44, 0.86, 0.180, 0.40, 0.020),
        local_box(0.0, 0.218, 0.44, 0.86, 0.026, 0.40, 0.010),
    ]
    return AssemblySpec(
        kind, variant, (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Masonry", *masonry),
        ),
        root_derivation="RoadsideCappedStandpipe.DescriptorGround+Forward",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("column_height", 1.022), ("trough_width", 0.86)),
    )


def build_street_lamp_shell() -> AssemblySpec:
    kind = "StreetLampShell"
    variant = 0
    fixture: list[Geometry] = [
        ring_solid_z((
            (0.0, 0.0, 0.0, 0.18, 0.18, 0.0),
            (0.10, 0.0, 0.0, 0.22, 0.22, 0.06),
            (0.22, 0.0, 0.0, 0.15, 0.15, 0.0),
        ), 10),
        tube_between((0.0, 0.0, 0.16), (0.0, 0.0, 4.73),
                     0.075, 0.055, 10),
        tube_between((0.0, 0.0, 4.70), (0.0, 0.36, 5.10),
                     0.060, 0.052, 9),
        tube_between((0.0, 0.36, 5.10), (0.0, 0.91, 5.22),
                     0.052, 0.045, 9),
        source_box((0.0, 0.94, 5.18), (0.62, 0.38, 0.14), 0.035),
        source_box((0.0, 1.10, 5.10), (0.54, 0.12, 0.22), 0.025),
        source_box((-0.275, 1.01, 5.04), (0.07, 0.30, 0.20), 0.018),
        source_box((0.275, 1.01, 5.04), (0.07, 0.30, 0.20), 0.018),
        source_box((0.0, 0.99, 5.25), (0.30, 0.25, 0.12), 0.022),
    ]
    return AssemblySpec(kind, variant, (
        make_part(kind, variant, "Fixture", *fixture),
    ))


# ----------------------------------------- remaining City decorations --


def build_old_town_chimneys_and_dormers(variant: int) -> AssemblySpec:
    kind = "OldTownChimneysAndDormers"
    mirror = 1.0 if variant == 0 else -1.0
    chimneys: list[Geometry] = []
    for side in (-1, 1):
        x = side * 2.40
        forward = side * 0.22
        chimneys.extend((
            recipe_vertical_solid(
                x, 0.0, 1.70, forward,
                0.34, 0.34, top_scale=0.82, sides=8),
            recipe_vertical_solid(
                x, 1.65, 1.83, forward,
                0.46, 0.46, top_scale=0.92, sides=8),
        ))
    dormer_x = mirror * 1.80
    source_x = -dormer_x
    dormer = [
        recipe_box(dormer_x, 0.62, 0.0,
                   1.65, 1.24, 1.05, chamfer=0.045),
        prism_y((
            (source_x - 0.95, 1.24),
            (source_x, 1.76),
            (source_x + 0.95, 1.24),
            (source_x + 0.82, 1.16),
            (source_x, 1.56),
            (source_x - 0.82, 1.16),
        ), -0.66, 0.66),
    ]
    window = [
        recipe_box(dormer_x, 0.69, 0.565,
                   0.62, 0.62, 0.07, chamfer=0.012),
        recipe_box(dormer_x, 0.69, 0.606,
                   0.055, 0.54, 0.018, chamfer=0.003),
        recipe_box(dormer_x, 0.69, 0.606,
                   0.54, 0.055, 0.018, chamfer=0.003),
    ]
    return AssemblySpec(
        kind,
        variant,
        (
            make_named_part(kind, variant, "Chimneys_Masonry",
                            "Masonry", *chimneys),
            make_named_part(kind, variant, "Dormer_Masonry",
                            "Masonry", *dormer),
            make_named_part(kind, variant, "Window_Street",
                            "Street", *window),
        ),
        canonical_reference=(
            ("lot_width", 16.0),
            ("chimney_spread", 2.4),
            ("dormer_offset", 1.8),
        ),
        scale_parameters=(
            scale_parameter("chimney_spread", ("X",), 2.4, 1.1, 2.4),
            scale_parameter("dormer_offset", ("X",), 1.8, 0.0, 1.8),
        ),
    )


def build_old_town_scaffolding() -> AssemblySpec:
    kind = "OldTownScaffolding"
    variant = 0
    width = 7.20
    height = 7.20
    industrial: list[Geometry] = []
    masonry: list[Geometry] = []
    for side in (-1, 1):
        x = side * width * 0.5
        for forward in (0.18, 0.82):
            industrial.append(recipe_tube(
                (x, 0.0, forward), (x, height, forward), 0.06, sides=8))
        industrial.append(recipe_tube(
            (x, 0.18, 0.18), (-x, height - 0.20, 0.82),
            0.045, sides=7))
    for level in range(1, 4):
        y = height * level * 0.25
        for plank in range(5):
            masonry.append(recipe_box(
                0.0, y, 0.18 + plank * 0.16,
                width + 0.35, 0.11, 0.14, chamfer=0.018))
        industrial.extend((
            recipe_tube((-width * 0.5, y + 0.54, 0.84),
                        (width * 0.5, y + 0.54, 0.84), 0.05, sides=7),
            recipe_tube((-width * 0.5, y + 0.10, 0.18),
                        (width * 0.5, y + 0.10, 0.18), 0.04, sides=7),
        ))
    industrial.append(recipe_tube(
        (-width * 0.5, height - 0.25, 0.20),
        (width * 0.5, height - 0.25, 0.20), 0.06, sides=8))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Industrial", *industrial),
            make_part(kind, variant, "Masonry", *masonry),
        ),
        canonical_reference=(
            ("resolved_width", width), ("resolved_height", height)),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 4.2, 7.2),
            scale_parameter("resolved_height", ("Z",), height, 4.8, 7.2),
        ),
        unity_owned_parts=("WindCloth", "WindRopes", "WindAnchors"),
    )


def build_old_town_street_market() -> AssemblySpec:
    kind = "OldTownStreetMarket"
    variant = 0
    width = 5.20
    street: list[Geometry] = []
    masonry: list[Geometry] = []
    residential: list[Geometry] = []
    for side in (-1, 1):
        for forward in (-0.38, 0.38):
            street.append(recipe_tube(
                (side * width * 0.44, 0.0, forward),
                (side * width * 0.44, 2.40, forward),
                0.07, sides=8))
    street.extend((
        recipe_tube((-width * 0.44, 2.38, -0.38),
                    (width * 0.44, 2.38, -0.38), 0.055, sides=7),
        recipe_tube((-width * 0.44, 2.38, 0.38),
                    (width * 0.44, 2.38, 0.38), 0.055, sides=7),
        recipe_box(0.0, 1.45, -0.48,
                   width * 0.62, 0.12, 0.28, chamfer=0.018),
    ))
    masonry.extend((
        recipe_box(0.0, 0.88, 0.10,
                   width * 0.70, 0.18, 0.64, chamfer=0.028),
        recipe_box(-width * 0.30, 0.32, 0.70,
                   0.72, 0.64, 0.72, chamfer=0.045),
        recipe_box(width * 0.27, 0.24, 0.74,
                   0.58, 0.48, 0.58, chamfer=0.04),
    ))
    residential.extend((
        recipe_box(0.0, 2.48, 0.0,
                   width, 0.18, 1.25, chamfer=0.035),
        recipe_box(0.0, 2.26, 0.64,
                   width, 0.34, 0.10, chamfer=0.018),
    ))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Masonry", *masonry),
            make_part(kind, variant, "Residential", *residential),
        ),
        canonical_reference=(("resolved_width", width),),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 3.4, 5.2),),
        unity_owned_parts=("WindCloth", "WindRopes", "WindAnchors"),
    )


def build_old_town_clock_tower() -> AssemblySpec:
    kind = "OldTownClockTower"
    variant = 0
    width = 4.0
    masonry = [
        recipe_box(0.0, 1.80, 0.0,
                   width, 3.60, width, chamfer=0.10),
        recipe_box(0.0, 4.35, 0.0,
                   width * 0.82, 1.50, width * 0.82, chamfer=0.08),
        recipe_box(0.0, 5.16, 0.0,
                   width * 1.02, 0.22, width * 1.02, chamfer=0.045),
    ]
    street = [
        recipe_vertical_solid(0.0, 5.25, 5.84, 0.0,
                              width * 0.38, width * 0.38,
                              top_scale=0.20, sides=8),
        recipe_vertical_solid(0.0, 5.82, 7.02, 0.0,
                              0.15, 0.15, top_scale=0.55, sides=8),
    ]
    residential: list[Geometry] = []
    offset = width * 0.425
    for x, forward, sx, depth in (
        (0.0, offset, 1.02, 0.08),
        (0.0, -offset, 1.02, 0.08),
        (offset, 0.0, 0.08, 1.02),
        (-offset, 0.0, 0.08, 1.02),
    ):
        residential.append(recipe_box(
            x, 4.36, forward, sx, 1.02, depth, chamfer=0.018))
    for face_forward in (-1, 1):
        street.extend((
            recipe_box(0.0, 4.36, face_forward * (offset + 0.05),
                       0.055, 0.70, 0.035, chamfer=0.006),
            recipe_box(0.12, 4.36, face_forward * (offset + 0.055),
                       0.48, 0.045, 0.03, chamfer=0.005),
        ))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Masonry", *masonry),
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Residential", *residential),
        ),
        canonical_reference=(("resolved_width", width),),
        scale_parameters=(
            scale_parameter("resolved_width", ("X", "Y"),
                            width, 2.8, 4.0),),
    )


def build_residential_balconies(variant: int) -> AssemblySpec:
    kind = "ResidentialBalconies"
    width = 4.80
    floor_count = 2 + variant
    residential: list[Geometry] = []
    street: list[Geometry] = []
    for floor in range(floor_count):
        y = 1.75 + floor * 2.15
        residential.append(recipe_box(
            0.0, y, 0.54, width, 0.16, 1.08, chamfer=0.035))
        street.append(recipe_tube(
            (-width * 0.5, y + 0.58, 1.02),
            (width * 0.5, y + 0.58, 1.02), 0.055, sides=8))
        for x in (-width * 0.48, width * 0.48):
            street.extend((
                recipe_tube((x, y + 0.05, 0.55),
                            (x, y + 1.10, 0.55), 0.05, sides=7),
                recipe_tube((x, y + 1.10, 0.55),
                            (x, y + 1.10, 1.02), 0.05, sides=7),
            ))
        for bar in range(7):
            x = -width * 0.42 + bar * width * 0.14
            street.append(recipe_tube(
                (x, y + 0.12, 1.02),
                (x, y + 0.58, 1.02), 0.025, sides=6))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Residential", *residential),
            make_part(kind, variant, "Street", *street),
        ),
        canonical_reference=(
            ("resolved_width", width),
            ("floor_count", float(floor_count)),
        ),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 2.8, 4.8),),
    )


def build_residential_laundry_and_antenna() -> AssemblySpec:
    kind = "ResidentialLaundryAndAntenna"
    variant = 0
    street: list[Geometry] = []
    for x, height, forward in (
        (0.0, 2.90, -0.65), (-2.0, 2.40, 0.42), (2.0, 2.40, 0.42)):
        street.append(recipe_tube(
            (x, 0.0, forward), (x, height, forward), 0.06, sides=8))
    street.extend((
        recipe_tube((-1.35, 2.25, -0.65),
                    (1.35, 2.25, -0.65), 0.05, sides=7),
        recipe_tube((-0.82, 2.62, -0.65),
                    (0.82, 2.62, -0.65), 0.04, sides=7),
        recipe_tube((-2.0, 2.17, 0.42),
                    (2.0, 2.17, 0.42), 0.03, sides=7),
    ))
    residential = [
        recipe_box(-1.18, 1.68, 0.43,
                   0.86, 0.92, 0.05, chamfer=0.012),
        recipe_box(1.12, 1.64, 0.43,
                   0.92, 1.00, 0.05, chamfer=0.012),
    ]
    masonry = [recipe_box(
        0.0, 1.72, 0.43, 0.72, 0.84, 0.05, chamfer=0.012)]
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Residential", *residential),
            make_part(kind, variant, "Masonry", *masonry),
        ),
        unity_owned_parts=("WindCloth", "WindRopes", "WindAnchors"),
    )


def build_residential_discarded_furniture(variant: int) -> AssemblySpec:
    kind = "ResidentialDiscardedFurniture"
    mirror = 1.0 if variant == 0 else -1.0
    depth_scale = 0.42
    couch_x = -0.55 * mirror
    residential: list[Geometry] = [
        recipe_box(couch_x, 0.48, 0.0,
                   2.20, 0.42, 0.92, depth_scale, 0.055),
        recipe_box(couch_x, 1.02, -0.37,
                   2.20, 0.78, 0.18, depth_scale, 0.055),
        recipe_box(-1.58 * mirror, 0.78, 0.0,
                   0.20, 0.82, 0.92, depth_scale, 0.035),
        recipe_box(0.48 * mirror, 0.78, 0.0,
                   0.20, 0.82, 0.92, depth_scale, 0.035),
    ]
    for cushion in (-0.48, 0.48):
        residential.append(recipe_box(
            couch_x + cushion * mirror, 0.66, 0.02,
            0.88, 0.12, 0.72, depth_scale, 0.04))
    street = [
        recipe_vertical_solid(-1.22 * mirror, 0.0, 0.28, 0.0,
                              0.08, 0.08, depth_scale, sides=7),
        recipe_vertical_solid(0.12 * mirror, 0.0, 0.28, 0.0,
                              0.08, 0.08, depth_scale, sides=7),
        recipe_box(1.25 * mirror, 0.24, 0.35,
                   1.12, 0.05, 1.92, depth_scale, 0.01),
    ]
    masonry = [recipe_box(
        1.25 * mirror, 0.11, 0.35,
        1.30, 0.22, 2.10, depth_scale, 0.045)]
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Residential", *residential),
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Masonry", *masonry),
        ),
        unity_owned_parts=("SitDock", "CollisionProxy", "WindAnchor"),
    )


def build_residential_rooftop_greenhouse() -> AssemblySpec:
    kind = "ResidentialRooftopGreenhouse"
    variant = 0
    width = 5.40
    depth = 4.00
    base = [recipe_box(
        0.0, 0.12, 0.0, width, 0.24, depth, chamfer=0.045)]
    frame: list[Geometry] = []
    for x in (-width * 0.47, width * 0.47):
        for forward in (-depth * 0.47, depth * 0.47):
            frame.append(recipe_tube(
                (x, 0.24, forward), (x, 2.18, forward), 0.06, sides=7))
    for forward in (-depth * 0.47, depth * 0.47):
        frame.append(recipe_tube(
            (-width * 0.47, 2.16, forward),
            (width * 0.47, 2.16, forward), 0.06, sides=7))
    for x in (-width * 0.47, width * 0.47):
        frame.append(recipe_tube(
            (x, 2.16, -depth * 0.47),
            (x, 2.16, depth * 0.47), 0.06, sides=7))
    roof = [
        prism_y(tuple(reversed((
            (-width * 0.5, 2.15), (0.0, 2.58),
            (0.0, 2.70), (-width * 0.5, 2.27),
        ))), -depth * 0.52, depth * 0.52),
        prism_y(tuple(reversed((
            (0.0, 2.58), (width * 0.5, 2.15),
            (width * 0.5, 2.27), (0.0, 2.70),
        ))), -depth * 0.52, depth * 0.52),
    ]
    hardware = [
        recipe_tube((0.0, 2.65, -depth * 0.55),
                    (0.0, 2.65, depth * 0.55), 0.06, sides=8),
        recipe_tube((width * 0.32, 2.68, 0.0),
                    (width * 0.32, 4.10, 0.0), 0.05, sides=8),
        recipe_tube((width * 0.32 - 0.55, 4.10, 0.0),
                    (width * 0.32 + 0.55, 4.10, 0.0), 0.04, sides=7),
    ]
    return AssemblySpec(
        kind, variant,
        (
            make_named_part(kind, variant, "Base_Masonry",
                            "Masonry", *base),
            make_named_part(kind, variant, "Frame_Residential",
                            "Residential", *frame),
            make_named_part(kind, variant, "Roof_Residential",
                            "Residential", *roof),
            make_named_part(kind, variant, "Hardware_Street",
                            "Street", *hardware),
        ),
        canonical_reference=(
            ("resolved_width", width), ("resolved_depth", depth)),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 3.4, 5.4),
            scale_parameter("resolved_depth", ("Y",), depth, 2.6, 4.0),
        ),
    )


def build_industrial_pipe_rack() -> AssemblySpec:
    kind = "IndustrialPipeRack"
    variant = 0
    width = 7.0
    street: list[Geometry] = []
    industrial: list[Geometry] = []
    for side in (-1, 1):
        x = side * width * 0.48
        for forward in (-0.75, 0.75):
            street.append(recipe_tube(
                (x, 0.0, forward), (x, 3.10, forward), 0.09, sides=8))
        street.extend((
            recipe_tube((x, 2.05, -0.85),
                        (x, 2.05, 0.85), 0.07, sides=8),
            recipe_tube((x, 0.25, -0.75),
                        (-x, 2.85, 0.75), 0.045, sides=7),
        ))
        industrial.append(recipe_tube(
            (-width * 0.5, 2.92, side * 0.75),
            (width * 0.5, 2.92, side * 0.75), 0.09, sides=8))
    for pipe in (-1, 0, 1):
        y = 1.85 + pipe * 0.46
        forward = pipe * 0.40
        industrial.extend((
            recipe_tube((-width * 0.52, y, forward),
                        (width * 0.52, y, forward), 0.10, sides=10),
            recipe_vertical_solid(-width * 0.42, y - 0.11, y + 0.11,
                                  forward, 0.15, 0.15,
                                  top_scale=1.0, sides=8),
        ))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Industrial", *industrial),
        ),
        canonical_reference=(("resolved_width", width),),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 4.5, 7.0),),
        unity_owned_parts=("WindCloth", "WindRopes", "WindAnchors"),
    )


def build_industrial_gantry(variant: int) -> AssemblySpec:
    kind = "IndustrialGantry"
    mirror = 1.0 if variant == 0 else -1.0
    width = 9.0
    depth = 5.0
    industrial: list[Geometry] = []
    street: list[Geometry] = []
    for x_side in (-1, 1):
        x = x_side * width * 0.48
        for z_side in (-1, 1):
            forward = z_side * depth * 0.45
            industrial.extend((
                recipe_vertical_solid(
                    x, 0.0, 5.40, forward, 0.15, 0.15,
                    top_scale=0.90, sides=8),
                recipe_tube((x, 0.35, forward),
                            (-x, 5.15, forward), 0.05, sides=7),
            ))
        industrial.append(recipe_tube(
            (x, 5.25, -depth * 0.5),
            (x, 5.25, depth * 0.5), 0.17, sides=8))
    industrial.append(recipe_tube(
        (-width * 0.53, 5.48, 0.0),
        (width * 0.53, 5.48, 0.0), 0.23, sides=10))
    hoist_x = mirror * width * 0.18
    industrial.extend((
        recipe_box(hoist_x, 5.12, 0.0,
                   0.78, 0.62, 0.92, chamfer=0.055),
        recipe_vertical_solid(hoist_x, 2.61, 2.83, 0.0,
                              0.21, 0.21, sides=9),
    ))
    street.extend((
        recipe_tube((-width * 0.5, 5.82, -0.72),
                    (width * 0.5, 5.82, -0.72), 0.08, sides=8),
        recipe_tube((-width * 0.5, 5.82, 0.72),
                    (width * 0.5, 5.82, 0.72), 0.08, sides=8),
        recipe_tube((hoist_x, 5.00, 0.0),
                    (hoist_x, 2.82, 0.0), 0.05, sides=8),
    ))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Industrial", *industrial),
            make_part(kind, variant, "Street", *street),
        ),
        canonical_reference=(
            ("resolved_width", width), ("resolved_depth", depth)),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 6.0, 9.0),
            scale_parameter("resolved_depth", ("Y",), depth, 3.2, 5.0),
        ),
    )


def build_nightlife_billboard() -> AssemblySpec:
    kind = "NightlifeBillboard"
    variant = 0
    width = 7.0
    street: list[Geometry] = []
    neon: list[Geometry] = []
    for side in (-1, 1):
        street.append(recipe_tube(
            (side * width * 0.32, 0.0, 0.0),
            (side * width * 0.32, 3.10, 0.0), 0.11, sides=8))
    street.extend((
        recipe_box(0.0, 3.48, 0.0,
                   width + 0.32, 2.45, 0.24, chamfer=0.055),
        recipe_box(0.0, 4.73, 0.02,
                   width + 0.58, 0.16, 0.32, chamfer=0.025),
        recipe_box(0.0, 2.23, 0.02,
                   width + 0.58, 0.16, 0.32, chamfer=0.025),
        recipe_box(-width * 0.51, 3.48, 0.02,
                   0.16, 2.66, 0.32, chamfer=0.025),
        recipe_box(width * 0.51, 3.48, 0.02,
                   0.16, 2.66, 0.32, chamfer=0.025),
    ))
    for side in (-1, 1):
        pane_x = side * width * 0.24
        neon.append(recipe_box(
            pane_x, 3.48, 0.15,
            width * 0.46, 1.82, 0.06, chamfer=0.012))
        street.extend((
            recipe_box(pane_x - side * width * 0.075, 4.02, 0.185,
                       width * 0.21, 0.30, 0.02, chamfer=0.004),
            recipe_box(pane_x, 3.56, 0.185,
                       width * 0.36, 0.10, 0.02, chamfer=0.004),
            recipe_box(pane_x + side * width * 0.02, 3.34, 0.185,
                       width * 0.30, 0.09, 0.02, chamfer=0.004),
            recipe_box(pane_x + side * width * 0.12, 2.95, 0.185,
                       width * 0.17, 0.46, 0.02, chamfer=0.004),
        ))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Neon", *neon),
        ),
        canonical_reference=(("resolved_width", width),),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 4.2, 7.0),),
    )


def build_nightlife_fire_escape() -> AssemblySpec:
    kind = "NightlifeFireEscape"
    variant = 0
    width = 4.40
    height = 7.20
    industrial: list[Geometry] = []
    street: list[Geometry] = []
    for floor in range(1, 4):
        y = height * floor * 0.25
        for slat in range(8):
            industrial.append(recipe_box(
                0.0, y, 0.12 + slat * 0.14,
                width, 0.11, 0.11, chamfer=0.012))
        street.append(recipe_tube(
            (-width * 0.5, y + 0.58, 1.14),
            (width * 0.5, y + 0.58, 1.14), 0.05, sides=7))
    for x in (-width * 0.48, width * 0.48):
        street.append(recipe_tube(
            (x, 0.0, 1.12), (x, height, 1.12), 0.05, sides=8))
    ladder_xs = (width * 0.22, width * 0.43)
    for x in ladder_xs:
        industrial.append(recipe_tube(
            (x, 0.0, 1.20),
            (x, height * 0.69, 1.20), 0.055, sides=7))
    for rung in range(8):
        y = 0.62 + rung * 0.58
        industrial.append(recipe_tube(
            (ladder_xs[0], y, 1.20),
            (ladder_xs[1], y, 1.20), 0.035, sides=6))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Industrial", *industrial),
            make_part(kind, variant, "Street", *street),
        ),
        canonical_reference=(
            ("resolved_width", width), ("resolved_height", height)),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 3.0, 4.4),
            scale_parameter("resolved_height", ("Z",), height, 5.2, 7.2),
        ),
        unity_owned_parts=("WindCloth", "WindRopes", "WindAnchors"),
    )


def build_nightlife_cinema(variant: int) -> AssemblySpec:
    kind = "NightlifeCinema"
    mirror = 1.0 if variant == 0 else -1.0
    width = 9.50
    street: list[Geometry] = [
        recipe_box(0.0, 3.20, 0.65,
                   width, 0.65, 1.45, chamfer=0.07),
    ]
    neon: list[Geometry] = [recipe_box(
        0.0, 3.14, 1.39,
        width * 0.88, 0.18, 0.06, chamfer=0.012)]
    masonry: list[Geometry] = []
    sign_x = mirror * width * 0.36
    street.append(recipe_box(
        sign_x, 6.25, 0.10, 1.10, 5.60, 0.32, chamfer=0.055))
    for line in range(3):
        neon.append(recipe_box(
            sign_x, 4.55 + line * 1.45, 0.28,
            0.72, 0.18, 0.05, chamfer=0.01))
    for side in (-1, 1):
        pillar_x = side * width * 0.36
        masonry.append(recipe_box(
            pillar_x, 1.42, 0.08,
            0.48, 2.84, 0.48, chamfer=0.055))
        poster_x = side * width * 0.18
        street.append(recipe_box(
            poster_x, 1.35, 0.22,
            1.65, 2.20, 0.18, chamfer=0.035))
        neon.append(recipe_box(
            poster_x, 1.35, 0.32,
            1.28, 1.75, 0.05, chamfer=0.012))
        street.extend((
            recipe_box(poster_x - side * 0.19, 1.58, 0.35,
                       0.46, 0.88, 0.02, chamfer=0.004),
            recipe_box(poster_x + side * 0.28, 1.30, 0.35,
                       0.34, 0.40, 0.02, chamfer=0.004),
            recipe_box(poster_x, 0.86, 0.35,
                       0.96, 0.10, 0.02, chamfer=0.004),
        ))
    masonry.append(recipe_box(
        0.0, 8.72, 0.0,
        width * 0.92, 0.28, 0.52, chamfer=0.045))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Street", *street),
            make_part(kind, variant, "Neon", *neon),
            make_part(kind, variant, "Masonry", *masonry),
        ),
        canonical_reference=(("resolved_width", width),),
        scale_parameters=(
            scale_parameter("resolved_width", ("X",), width, 6.0, 9.5),),
    )


def build_park_fountain_and_statue() -> AssemblySpec:
    kind = "ParkFountainAndStatue"
    variant = 0
    stone: list[Geometry] = [
        ring_solid_z(((0.0, 0.0, 0.0, 3.20, 3.20, 0.0),
                      (0.28, 0.0, 0.0, 3.20, 3.20, 0.0)), 20),
        annulus_z((0.0, 0.0, 0.48), 3.20, 2.72, 0.68, 20),
        ring_solid_z(((0.28, 0.0, 0.0, 0.74, 0.74, 0.0),
                      (1.44, 0.0, 0.0, 0.58, 0.58, 0.08)), 12),
        ring_solid_z(((1.44, 0.0, 0.0, 0.39, 0.39, 0.0),
                      (2.05, 0.0, 0.0, 0.31, 0.31, 0.08)), 10),
    ]
    statue: list[Geometry] = [
        tube_between((0.0, 0.0, 2.02), (0.0, 0.0, 3.55),
                     0.25, 0.18, 9),
        ring_solid_z(((3.53, 0.0, 0.0, 0.30, 0.27, 0.0),
                      (4.13, 0.0, 0.0, 0.24, 0.22, 0.08)), 9),
        tube_between((-0.05, 0.0, 3.08), (-0.72, 0.0, 3.30),
                     0.11, 0.07, 7),
        tube_between((0.05, 0.0, 3.08), (0.72, 0.0, 3.30),
                     0.11, 0.07, 7),
        tube_between((-0.11, 0.0, 2.16), (-0.28, 0.0, 1.78),
                     0.13, 0.09, 7),
        tube_between((0.11, 0.0, 2.16), (0.28, 0.0, 1.78),
                     0.13, 0.09, 7),
    ]
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Masonry_Stone", *stone),
            make_part(kind, variant, "Street_Stone", *statue),
        ),
        unity_owned_parts=(
            "FountainWater", "WaterSpouts", "CollisionProxy"),
    )


def build_park_bandstand() -> AssemblySpec:
    kind = "ParkBandstand"
    variant = 0
    width = 6.80
    stone = [
        ring_solid_z(((0.0, 0.0, 0.0, 3.72, 3.12, 0.0),
                      (0.36, 0.0, 0.0, 3.55, 2.95, 0.08)), 12),
    ]
    residential_timber = [
        ring_solid_z(((0.36, 0.0, 0.0, 3.22, 2.58, 0.0),
                      (0.54, 0.0, 0.0, 3.18, 2.54, 0.04)), 12),
        ring_solid_z(((4.00, 0.0, 0.0, 3.68, 3.08, 0.0),
                      (4.30, 0.0, 0.0, 3.50, 2.88, 0.05)), 12),
    ]
    masonry_timber: list[Geometry] = []
    for x_side in (-1, 1):
        for z_side in (-1, 1):
            masonry_timber.append(recipe_tube(
                (x_side * width * 0.43, 0.42, z_side * 2.25),
                (x_side * width * 0.43, 4.08, z_side * 2.25),
                0.14, sides=9, end_radius=0.11))
    painted: list[Geometry] = [
        ring_solid_z(((4.28, 0.0, 0.0, 2.66, 2.45, 0.0),
                      (4.82, 0.0, 0.0, 2.22, 2.00, 0.06),
                      (5.60, 0.0, 0.0, 0.17, 1.90, 0.0)), 12),
    ]
    for x in (-2.15, 0.0, 2.15):
        painted.append(recipe_tube(
            (x - 0.90, 1.14, -2.34),
            (x + 0.90, 1.14, -2.34), 0.06, sides=8))
    for x in (-3.05, -1.10, 1.10, 3.05):
        painted.append(recipe_tube(
            (x, 0.46, -2.34), (x, 1.16, -2.34), 0.045, sides=7))
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Masonry_Stone", *stone),
            make_part(kind, variant, "Residential_Timber",
                      *residential_timber),
            make_part(kind, variant, "Masonry_Timber", *masonry_timber),
            make_part(kind, variant, "Street_PaintedMetal", *painted),
        ),
        unity_owned_parts=("WindCloth", "WindRopes", "WindAnchors"),
    )


def build_park_chess_tables() -> AssemblySpec:
    kind = "ParkChessTables"
    variant = 0
    table_offset = 1.85
    table_slab: list[Geometry] = []
    board_light: list[Geometry] = []
    board_dark: list[Geometry] = []
    # Terrain-following supports are exported as single archetypes at the
    # support-local origin. Unity instances them at every sampled support and
    # scales the two vertical archetypes from their normalized 1 m height.
    table_footing = [recipe_box(
        0.0, 0.06, 0.0, 0.66, 0.12, 0.66, chamfer=0.025)]
    table_pedestal = [recipe_vertical_solid(
        0.0, 0.0, 1.0, 0.0, 0.17, 0.17,
        top_scale=0.84, sides=8)]
    bench_seat: list[Geometry] = []
    bench_pad = [recipe_box(
        0.0, 0.02, 0.0, 0.30, 0.04, 0.44, chamfer=0.012)]
    bench_leg = [recipe_box(
        0.0, 0.50, 0.0, 0.14, 1.0, 0.34, chamfer=0.02)]
    square_size = 0.15
    field_size = 1.20
    for table in (-1, 1):
        x = table * table_offset
        table_slab.append(recipe_box(
            x, 0.83, 0.0, 1.44, 0.14, 1.44, chamfer=0.045))
        board_light.append(recipe_box(
            x, 0.9175, 0.0, field_size, 0.035, field_size,
            chamfer=0.006))
        edge = 3.5
        for file in range(8):
            for rank in range(8):
                if ((file + rank) & 1) == 0:
                    continue
                board_dark.append(recipe_box(
                    x + (file - edge) * square_size,
                    0.9395,
                    (rank - edge) * square_size,
                    square_size,
                    0.009,
                    square_size,
                    chamfer=0.002,
                ))
        rim_center = (field_size + 0.07) * 0.5
        rim_outer = field_size + 0.14
        for side in (-1, 1):
            board_dark.extend((
                recipe_box(x, 0.944, side * rim_center,
                           rim_outer, 0.018, 0.07, chamfer=0.004),
                recipe_box(x + side * rim_center, 0.944, 0.0,
                           0.07, 0.018, field_size, chamfer=0.004),
            ))
        for side in (-1, 1):
            forward = side * 1.10
            bench_seat.append(recipe_box(
                x, 0.46, forward, 1.12, 0.16, 0.42, chamfer=0.028))
    parts = (
        make_named_part(kind, variant, "TableSlab_Masonry_Stone",
                        "Masonry_Stone", *table_slab),
        make_named_part(kind, variant, "BoardLight_Masonry_Timber",
                        "Masonry_Timber", *board_light),
        make_named_part(kind, variant, "BoardDarkAndRim_Street_Timber",
                        "Street_Timber", *board_dark),
        make_named_part(kind, variant, "TableFooting_Masonry_Stone",
                        "Masonry_Stone", *table_footing,
                        placement_mode="unity_per_support"),
        make_named_part(kind, variant, "TablePedestal_Masonry_Stone",
                        "Masonry_Stone", *table_pedestal,
                        placement_mode="unity_per_support",
                        normalized_height_m=1.0),
        make_named_part(kind, variant, "BenchSeat_Street_Timber",
                        "Street_Timber", *bench_seat),
        make_named_part(kind, variant, "BenchPad_Masonry_Stone",
                        "Masonry_Stone", *bench_pad,
                        placement_mode="unity_per_support"),
        make_named_part(kind, variant, "BenchLeg_Masonry_Stone",
                        "Masonry_Stone", *bench_leg,
                        placement_mode="unity_per_support",
                        normalized_height_m=1.0),
    )
    return AssemblySpec(
        kind, variant, parts,
        unity_owned_parts=(
            "ChessMen", "BoardGameState", "SitDocks",
            "CollisionProxy", "TerrainFootingAdjustment"),
    )


def build_park_playground() -> AssemblySpec:
    kind = "ParkPlayground"
    variant = 0
    painted_residential: list[Geometry] = []
    for x_side in (-1, 1):
        x = x_side * 2.05
        for z_side in (-1, 1):
            painted_residential.append(recipe_tube(
                (x, 0.02, z_side * 0.80),
                (x, 2.86, z_side * 0.28), 0.09, sides=8))
        painted_residential.append(recipe_tube(
            (x, 2.85, -0.98), (x, 2.85, 0.98), 0.11, sides=8))
    painted_residential.append(recipe_tube(
        (-2.30, 3.06, 0.0), (2.30, 3.06, 0.0), 0.11, sides=10))
    timber = [
        recipe_box(0.0, 0.62, 3.60,
                   3.75, 0.18, 0.42, chamfer=0.035),
    ]
    painted_street = [
        recipe_vertical_solid(0.0, 0.0, 0.68, 3.60,
                              0.21, 0.21, top_scale=0.88, sides=8),
        recipe_tube((-1.45, 0.68, 3.60),
                    (1.45, 0.68, 3.60), 0.055, sides=8),
    ]
    return AssemblySpec(
        kind, variant,
        (
            make_part(kind, variant, "Residential_PaintedMetal",
                      *painted_residential),
            make_part(kind, variant, "Masonry_Timber", *timber),
            make_part(kind, variant, "Street_PaintedMetal",
                      *painted_street),
        ),
        unity_owned_parts=(
            "SwingRopes", "SwingSeats", "SwingPhysics",
            "SitDock", "CollisionProxy"),
    )


# ---------------------------------------------- citywide v3 catalog --


def build_route01_shelter_shell() -> AssemblySpec:
    kind, variant = "Route01ShelterShell", 0
    x = -2.55
    fixture = [
        local_box(x, 1.32, -0.48, 4.25, 2.45, 0.10, 0.025),
        local_box(x, 2.62, 0.0, 4.65, 0.18, 1.18, 0.04),
        *(local_vertical_solid(x + side * 2.05, 0.0, 2.60, 0.0,
                              0.08, 0.08, top_scale=0.92, sides=8)
          for side in (-1, 1)),
        local_vertical_solid(x, 0.0, 2.60, -0.48,
                             0.08, 0.08, top_scale=0.92, sides=8),
        local_tube((x - 2.16, 2.58, -0.48),
                   (x + 2.16, 2.58, -0.48), 0.045, sides=8),
    ]
    timber = [
        local_box(x, 0.72, 0.05, 2.65, 0.16, 0.62, 0.035),
        local_box(x, 1.10, -0.20, 2.65, 0.62, 0.14, 0.028),
        local_box(x - 0.90, 0.34, 0.05, 0.14, 0.68, 0.14, 0.02),
        local_box(x + 0.90, 0.34, 0.05, 0.14, 0.68, 0.14, 0.02),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Fixture", *fixture),
         make_part(kind, variant, "Timber", *timber)),
        unity_owned_parts=("WaitSlots", "SitDock", "CollisionProxy"),
        root_derivation="CityBusStopRoot@stop.ShelterPosition",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_route01_pole_shell() -> AssemblySpec:
    kind, variant = "Route01PoleShell", 0
    fixture = [
        local_vertical_solid(0.0, 0.0, 2.40, 0.0,
                             0.06, 0.06, top_scale=0.86, sides=9),
        local_vertical_solid(0.0, 0.0, 0.16, 0.0,
                             0.11, 0.11, top_scale=0.82, sides=9),
        local_box(-0.16, 2.30, 0.075, 0.18, 0.045, 0.018, 0.003),
        local_box(-0.16, 2.06, 0.075, 0.18, 0.045, 0.018, 0.003),
        local_box(-0.25, 2.18, 0.075, 0.045, 0.28, 0.018, 0.003),
        local_box(-0.07, 2.18, 0.075, 0.045, 0.28, 0.018, 0.003),
        local_box(0.18, 2.18, 0.075, 0.05, 0.30, 0.018, 0.003),
    ]
    street = [local_box(
        0.0, 2.18, 0.0, 0.76, 0.56, 0.10, 0.025)]
    residential = [
        local_box(0.0, 2.18, 0.056, 0.58, 0.38, 0.025, 0.005),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Fixture", *fixture),
         make_part(kind, variant, "Street", *street),
         make_part(kind, variant, "Residential", *residential)),
        unity_owned_parts=("CollisionProxy",),
        root_derivation="CityBusStopRoot@stop.ShelterPosition",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_traffic_signal_shell() -> AssemblySpec:
    kind, variant = "TrafficSignalShell", 0
    fixture = [
        local_vertical_solid(0.0, 0.0, 2.56, 0.0,
                             0.075, 0.075, top_scale=0.88, sides=10),
        local_vertical_solid(0.0, 0.0, 0.18, 0.0,
                             0.13, 0.13, top_scale=0.78, sides=10),
        local_box(0.0, 2.55, 0.06, 0.50, 1.02, 0.28, 0.045),
        local_box(0.0, 3.075, 0.06, 0.58, 0.08, 0.32, 0.015),
        local_box(0.0, 2.025, 0.06, 0.58, 0.08, 0.32, 0.015),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        unity_owned_parts=(
            "RedLens", "AmberLens", "GreenLens", "AmberHalo",
            "SignalController", "CollisionProxy"),
        root_derivation="TrafficSignalDescriptor.Position",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_dead_tree() -> AssemblySpec:
    kind, variant = "YardDeadTree", 0
    bark = [
        local_vertical_solid(0.0, 0.0, 4.10, 0.0,
                             0.23, 0.23, top_scale=0.54, sides=9),
        local_tube((0.05, 2.96, 0.03), (1.17, 3.14, 0.09),
                   0.105, sides=8, end_radius=0.055),
        local_tube((-0.04, 3.50, -0.08), (-0.16, 3.70, -1.10),
                   0.09, sides=8, end_radius=0.045),
        local_tube((0.02, 4.02, 0.0), (0.18, 4.34, 0.10),
                   0.075, sides=7, end_radius=0.025),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Bark", *bark),),
        root_derivation="HomeYardSite.Center",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_bench() -> AssemblySpec:
    kind, variant = "YardBench", 0
    timber = [
        local_box(0.0, 0.47, 0.0, 1.85, 0.10, 0.52, 0.025),
        local_box(-0.74, 0.21, 0.0, 0.14, 0.42, 0.46, 0.018),
    ]
    fixture = [
        local_vertical_solid(0.74, 0.0, 0.42, 0.0,
                             0.05, 0.05, top_scale=0.92, sides=8),
        local_box(0.74, 0.08, 0.0, 0.22, 0.06, 0.22, 0.012),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Timber", *timber),
         make_part(kind, variant, "Fixture", *fixture)),
        unity_owned_parts=("SitDock", "CollisionProxy"),
        root_derivation="YardBenchResolvedSlotGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_carpet_frame() -> AssemblySpec:
    kind, variant = "YardCarpetFrame", 0
    fixture = [
        local_vertical_solid(-1.22, 0.0, 1.69, 0.0,
                             0.065, 0.065, top_scale=0.94, sides=8),
        local_vertical_solid(1.22, 0.0, 1.69, 0.0,
                             0.065, 0.065, top_scale=0.94, sides=8),
        local_tube((-1.275, 1.62, 0.0),
                   (1.275, 1.62, 0.0), 0.07, sides=8),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        unity_owned_parts=("CarpetCloth", "CarpetStrikeDriver"),
        root_derivation="YardCarpetFrameResolvedSlotGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_sandpit() -> AssemblySpec:
    kind, variant = "YardSandpit", 0
    timber = [
        local_box(0.0, 0.12, 0.0, 2.20, 0.24, 0.18, 0.025),
        local_box(0.0, 0.12, 2.02, 2.20, 0.24, 0.18, 0.025),
        local_box(-1.01, 0.12, 1.01, 0.18, 0.24, 1.84, 0.025),
        local_box(1.01, 0.12, 1.01, 0.18, 0.24, 1.84, 0.025),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Timber", *timber),),
        root_derivation="YardSandpitResolvedSlotGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_child_toy() -> AssemblySpec:
    kind, variant = "YardChildToy", 0
    toy = [
        local_box(0.0, 0.10, 0.0, 0.24, 0.12, 0.20, 0.018),
        local_box(0.055, 0.165, -0.015, 0.105, 0.07, 0.13, 0.012),
        *(local_vertical_solid(x, 0.0, 0.08, forward,
                              0.035, 0.035, sides=7)
          for x in (-0.085, 0.085) for forward in (-0.09, 0.09)),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Residential", *toy),),
        root_derivation="YardSandpitToyGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_dead_lamp() -> AssemblySpec:
    kind, variant = "YardDeadLamp", 0
    fixture = [
        local_vertical_solid(0.0, 0.0, 3.35, 0.0,
                             0.085, 0.085, top_scale=0.70, sides=9),
        local_tube((0.0, 3.28, 0.0), (0.30, 3.42, 0.0),
                   0.055, sides=8, end_radius=0.045),
        local_box(0.31, 3.34, 0.0, 0.54, 0.22, 0.34, 0.035),
        local_box(0.38, 3.27, 0.0, 0.32, 0.055, 0.22, 0.01),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        unity_owned_parts=("Light", "Lens", "Halo"),
        root_derivation="YardDeadLampResolvedSlotGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_bin() -> AssemblySpec:
    kind, variant = "YardBin", 0
    fixture = [
        local_vertical_solid(0.0, 0.0, 1.02, 0.0,
                             0.48, 0.34, top_scale=1.06, sides=8),
        local_box(0.06, 1.07, 0.0, 1.06, 0.10, 0.76, 0.025),
        local_box(0.06, 1.125, 0.0, 0.64, 0.018, 0.42, 0.004),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        root_derivation="YardBinResolvedWallSlotGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_bottle() -> AssemblySpec:
    kind, variant = "YardBottle", 0
    bottle = [
        local_vertical_solid(0.0, 0.0, 0.19, 0.0,
                             0.045, 0.045, top_scale=0.72, sides=9),
        local_vertical_solid(0.0, 0.19, 0.27, 0.0,
                             0.026, 0.026, top_scale=0.86, sides=9),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Timber", *bottle),),
        root_derivation="YardBottleBenchRelativeGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_yard_spotlight_wall_mount() -> AssemblySpec:
    kind, variant = "YardSpotlightWallMount", 0
    fixture = [
        local_box(0.0, 0.0, -0.14, 0.62, 0.42, 0.08, 0.028),
        local_box(0.0, -0.03, -0.015, 0.11, 0.11, 0.25, 0.018),
        local_tube((-0.20, 0.0, -0.09), (0.20, 0.0, -0.09),
                   0.026, sides=7),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        root_derivation="HomeYardSpotlight.MountPosition+FacadeNormal",
        coordinate_profile="root_local_direct",
        expected_source_min_z=-0.21,
        placement_contract="anchor_forward_frame",
    )


def build_yard_spotlight_head_shell() -> AssemblySpec:
    kind, variant = "YardSpotlightHeadShell", 0
    fixture = [
        local_box(0.0, 0.0, -0.20, 0.50, 0.32, 0.42, 0.045),
        local_box(0.0, 0.0, 0.005, 0.43, 0.25, 0.055, 0.012),
        local_box(-0.255, 0.0, -0.20, 0.04, 0.22, 0.32, 0.008),
        local_box(0.255, 0.0, -0.20, 0.04, 0.22, 0.32, 0.008),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        unity_owned_parts=("Lens", "Light", "Halo"),
        root_derivation="HomeYardSpotlight.MountPosition+AimDirection",
        coordinate_profile="root_local_direct",
        expected_source_min_z=-0.16,
        placement_contract="anchor_forward_frame",
    )


GRAVE_VARIANT_NAMES = (
    "ClassicStele",
    "ArchedHeadstone",
    "OrthodoxCross",
    "Obelisk",
    "FamilyMonument",
    "OvergrownSlab",
)


def build_cemetery_grave_slab(variant: int) -> AssemblySpec:
    kind = "CemeteryGraveSlab"
    dimensions = (
        (1.15, 0.15, 2.10),
        (1.10, 0.14, 2.00),
        (1.00, 0.12, 1.95),
        (1.15, 0.14, 2.05),
        (2.30, 0.16, 2.20),
        (1.05, 0.08, 1.90),
    )
    width, height, depth = dimensions[variant]
    slab = [local_box(
        0.0, height * 0.5, 0.0,
        width, height, depth,
        min(0.045, height * 0.24))]
    if variant != 5:
        slab.append(local_box(
            0.0, height + 0.012, -depth * 0.08,
            width * 0.72, 0.024, depth * 0.56, 0.006))
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Masonry", *slab),),
        unity_owned_parts=("StoneStyle", "PlotYaw", "CollisionProxy"),
        root_derivation="CityCemeteryPlotDescriptor.Ground+Yaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(("grave_variant", float(variant)),),
    )


def build_cemetery_grave_monument(variant: int) -> AssemblySpec:
    kind = "CemeteryGraveMonument"
    masonry: list[Geometry] = []
    if variant == 0:
        masonry.extend((
            local_box(0.0, 0.25, 0.86,
                      0.78, 0.20, 0.38, 0.035),
            local_box(0.0, 0.875, 0.88,
                      0.62, 1.05, 0.20, 0.045),
            local_box(0.0, 0.69, 0.765,
                      0.34, 0.22, 0.025, 0.006),
        ))
    elif variant == 1:
        masonry.extend((
            local_box(0.0, 0.615, 0.84,
                      0.78, 0.95, 0.24, 0.045),
            local_faceted_mass(
                0.0, 1.09, 1.19, 1.35, 0.84,
                0.26, 0.11, top_scale=0.55, sides=10),
            local_box(0.0, 0.68, 0.705,
                      0.38, 0.24, 0.025, 0.006),
        ))
    elif variant == 2:
        masonry.extend((
            local_box(0.0, 1.02, 0.84,
                      0.14, 1.80, 0.14, 0.022),
            local_box(0.0, 1.485, 0.84,
                      0.80, 0.13, 0.12, 0.018),
            local_box(0.0, 1.03, 0.84,
                      0.52, 0.10, 0.10, 0.014,
                      roll_forward_degrees=32.0),
            local_box(0.0, 0.20, 0.84,
                      0.38, 0.16, 0.34, 0.025),
        ))
    elif variant == 3:
        masonry.extend((
            local_box(0.0, 0.315, 0.84,
                      0.68, 0.35, 0.68, 0.04),
            local_vertical_solid(0.0, 0.49, 1.64, 0.84,
                                 0.20, 0.20, top_scale=0.62, sides=8),
            local_faceted_mass(
                0.0, 1.64, 1.76, 1.88, 0.84,
                0.16, 0.16, top_scale=0.08, sides=8),
        ))
    else:
        masonry.extend((
            local_box(0.0, 0.66, 0.88,
                      1.46, 1.00, 0.28, 0.055),
            local_box(-0.92, 0.80, 0.88,
                      0.18, 1.28, 0.18, 0.025),
            local_box(0.92, 0.80, 0.88,
                      0.18, 1.28, 0.18, 0.025),
            local_box(0.0, 1.52, 0.88,
                      2.02, 0.16, 0.24, 0.025),
            local_box(0.0, 0.72, 0.725,
                      0.72, 0.30, 0.025, 0.006),
        ))
    expected_ground = (0.15, 0.14, 0.12, 0.14, 0.16)[variant]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Masonry", *masonry),),
        unity_owned_parts=(
            "PlotYaw", "StoneStyle", "Tilt0To6Degrees", "Plaque",
            "MovingStone", "CollisionProxy"),
        root_derivation="CityCemeteryPlotDescriptor.Ground+Yaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=expected_ground,
        canonical_reference=(("grave_variant", float(variant)),),
    )


def build_cemetery_overgrown_mound() -> AssemblySpec:
    kind, variant = "CemeteryOvergrownMound", 0
    soil = [
        local_faceted_mass(
            0.0, 0.08, 0.23, 0.32, 0.05,
            0.40, 0.725, top_scale=0.16, sides=10, phase=0.12),
        local_box(0.0, 0.11, 0.05, 0.68, 0.06, 1.18, 0.018),
    ]
    tuft = [
        local_faceted_mass(-0.10, 0.08, 0.24, 0.38, 0.42,
                           0.18, 0.15, top_scale=0.08, sides=7),
        local_faceted_mass(0.13, 0.08, 0.21, 0.34, 0.48,
                           0.16, 0.18, top_scale=0.10, sides=7, phase=0.2),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Street", *soil),
         make_part(kind, variant, "Residential", *tuft)),
        unity_owned_parts=("PlotYaw", "CollisionProxy"),
        root_derivation="CityCemeteryPlotDescriptor.Ground+Yaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.08,
    )


def build_cemetery_grave_enclosure() -> AssemblySpec:
    kind, variant = "CemeteryGraveEnclosure", 0
    fixture: list[Geometry] = [
        local_box(-1.28, 0.45, 0.15, 0.06, 0.42, 3.10, 0.012),
        local_box(1.28, 0.45, 0.15, 0.06, 0.42, 3.10, 0.012),
        local_box(0.0, 0.45, -1.40, 2.62, 0.42, 0.06, 0.012),
        local_box(0.0, 0.45, 1.70, 2.62, 0.42, 0.06, 0.012),
    ]
    for x in (-1.28, 1.28):
        for forward in (-1.40, 1.70):
            fixture.extend((
                local_vertical_solid(x, 0.0, 0.68, forward,
                                     0.035, 0.035,
                                     top_scale=0.82, sides=7),
                local_faceted_mass(x, 0.68, 0.72, 0.77, forward,
                                   0.055, 0.055,
                                   top_scale=0.08, sides=7),
            ))
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        unity_owned_parts=("PlotYaw", "WreathRibbons", "CollisionProxy"),
        root_derivation="CityCemeteryPlotDescriptor.Ground+Yaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_cemetery_grave_offering() -> AssemblySpec:
    kind, variant = "CemeteryGraveOffering", 0
    flowers: list[Geometry] = [
        local_tube((0.22 + offset, 0.164, -0.45),
                   (0.22 + offset * 0.4, 0.344, -0.45 + offset * 0.2),
                   0.012, sides=6, end_radius=0.008)
        for offset in (-0.09, -0.03, 0.04, 0.10)
    ]
    for index, offset in enumerate((-0.09, -0.03, 0.04, 0.10)):
        flowers.append(local_faceted_mass(
            0.22 + offset * 0.4, 0.31, 0.35, 0.39,
            -0.45 + offset * 0.2,
            0.055, 0.055, top_scale=0.10, sides=7,
            phase=index * 0.18))
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Residential", *flowers),),
        unity_owned_parts=("PlotYaw", "MournerBouquet"),
        root_derivation="CityCemeteryPlotDescriptor.Ground+Yaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.160089,
    )


def build_cemetery_tree(variant: int) -> AssemblySpec:
    kind = "CemeteryTree"
    if variant == 0:
        bark = [
            local_vertical_solid(0.0, 0.0, 2.90, 0.0,
                                 0.15, 0.15, top_scale=0.55, sides=9),
            local_box(0.0, 1.45, 0.151, 0.045, 2.58, 0.018, 0.003),
            local_box(0.0, 1.45, -0.151, 0.045, 2.58, 0.018, 0.003),
        ]
        foliage = [
            local_faceted_mass(0.0, 2.50, 3.55, 4.60, 0.0,
                               0.775, 0.775, top_scale=0.10,
                               sides=9, phase=0.08),
            local_faceted_mass(0.0, 4.10, 4.75, 5.40, 0.0,
                               0.475, 0.475, top_scale=0.08,
                               sides=8, phase=0.2),
        ]
    else:
        bark = [local_vertical_solid(
            0.0, 0.0, 1.10, 0.0,
            0.17, 0.17, top_scale=0.62, sides=9)]
        foliage = [
            local_faceted_mass(0.0, 0.95, 1.70, 2.45, 0.0,
                               1.025, 1.025, top_scale=0.12,
                               sides=10, phase=0.05),
            local_faceted_mass(0.0, 2.15, 2.85, 3.55, 0.0,
                               0.725, 0.725, top_scale=0.10,
                               sides=9, phase=0.16),
            local_faceted_mass(0.0, 3.25, 3.90, 4.55, 0.0,
                               0.425, 0.425, top_scale=0.08,
                               sides=8, phase=0.26),
        ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Bark", *bark),
         make_part(kind, variant, "Foliage", *foliage)),
        unity_owned_parts=("ScatterTransform", "CollisionProxy"),
        root_derivation="CemeteryTreeDescriptor.Ground+CurrentRotation",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_cemetery_bush() -> AssemblySpec:
    kind, variant = "CemeteryBush", 0
    foliage = [
        local_faceted_mass(-0.12, 0.0, 0.31, 0.62, 0.02,
                           0.36, 0.38, top_scale=0.10, sides=8),
        local_faceted_mass(0.20, 0.0, 0.27, 0.54, -0.08,
                           0.28, 0.30, top_scale=0.10,
                           sides=7, phase=0.18),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Foliage", *foliage),),
        unity_owned_parts=("ScatterTransform",),
        root_derivation="CemeteryBushDescriptor.Ground+CurrentRotation",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_cemetery_bench() -> AssemblySpec:
    kind, variant = "CemeteryBench", 0
    timber = [
        local_box(0.0, 0.455, 0.0, 1.60, 0.07, 0.42, 0.022),
        local_box(0.0, 0.75, -0.225, 1.60, 0.50, 0.06, 0.016),
        local_box(0.0, 0.79, -0.260, 1.72, 0.06, 0.10, 0.014),
    ]
    fixture = [
        local_box(-0.64, 0.21, -0.02, 0.08, 0.42, 0.36, 0.014),
        local_box(0.64, 0.21, -0.02, 0.08, 0.42, 0.36, 0.014),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Timber", *timber),
         make_part(kind, variant, "Fixture", *fixture)),
        unity_owned_parts=("SitDock", "CollisionProxy"),
        root_derivation="CemeteryBenchDescriptor.Ground+CurrentRotation",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_seacoast_boat(variant: int) -> AssemblySpec:
    kind = "SeacoastBoat"
    length_value, beam, depth_of_hull = (
        (4.100, 1.350, 0.540),
        (3.526, 1.512, 0.600),
        (4.592, 1.188, 0.440),
        (4.100, 1.350, 0.480),
    )[variant]
    timber = [
        local_box(sign * length_value * 0.28, 0.09, 0.0,
                  0.22, 0.18, beam + 0.30, 0.025)
        for sign in (-1, 1)
    ]
    gunwale_y = 0.04
    ridge_y = depth_of_hull
    gunwale_z = beam * 0.5
    ridge_z = beam * 0.17
    rise_z = gunwale_z - ridge_z
    rise_y = ridge_y - gunwale_y
    panel_span = math.sqrt(rise_z * rise_z + rise_y * rise_y)
    panel_tilt = math.degrees(math.atan2(rise_y, rise_z))
    residential: list[Geometry] = []
    for sign in (-1, 1):
        residential.extend((
            local_box(
                0.0, 0.20 + depth_of_hull * 0.5,
                sign * beam * 0.335,
                length_value * 0.97, 0.08, panel_span, 0.018,
                pitch_x_degrees=sign * panel_tilt),
            local_box(0.0, 0.22, sign * gunwale_z,
                      length_value, 0.12, 0.11, 0.018),
        ))
        for rib in (-0.26, 0.0, 0.26):
            residential.append(local_box(
                rib * length_value,
                0.22 + depth_of_hull * 0.30,
                sign * beam * 0.30,
                0.055, 0.075, beam * 0.22, 0.010,
                pitch_x_degrees=sign * panel_tilt))
    residential.extend((
        local_box(0.0, 0.18 + depth_of_hull, 0.0,
                  length_value * 0.94, 0.09, beam * 0.34, 0.018),
        local_box(-length_value * 0.47,
                  0.20 + depth_of_hull * 0.5, 0.0,
                  0.09, depth_of_hull - 0.04, beam * 0.82, 0.018),
        local_box(length_value * 0.47,
                  0.18 + 0.52 * (0.04 + depth_of_hull), 0.0,
                  0.09, depth_of_hull - 0.04, beam * 0.42, 0.018),
    ))
    street = [
        local_box(0.0, 0.25 + depth_of_hull, 0.0,
                  length_value * 0.90, 0.08, 0.13, 0.016),
        local_box(-length_value * 0.18, 0.255, 0.0,
                  length_value * 0.14, 0.035, beam * 0.54, 0.008),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Residential", *residential),
         make_part(kind, variant, "Street", *street),
         make_part(kind, variant, "Timber", *timber)),
        unity_owned_parts=(
            "SandHeight", "BoatYawJitter", "CollisionProxy",
            "OptionalOar"),
        root_derivation="SeacoastBoat.SampledSandGround+BoatYaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("length", length_value), ("beam", beam),
            ("hull_depth", depth_of_hull)),
    )


def build_seacoast_oar() -> AssemblySpec:
    kind, variant = "SeacoastOar", 0
    street = [
        local_box(0.82, 0.477, 0.999,
                  1.90, 0.09, 0.13, 0.018,
                  roll_forward_degrees=71.0,
                  yaw_up_degrees=62.0),
        local_box(1.48, 1.15, 0.49,
                  0.42, 0.12, 0.22, 0.025,
                  roll_forward_degrees=71.0,
                  yaw_up_degrees=62.0),
    ]
    part = make_part(kind, variant, "Street", *street)
    low, _ = geometry_bounds(part.geometry)
    return AssemblySpec(
        kind, variant, (part,),
        unity_owned_parts=("BoatSelection", "SandHeight", "BoatYaw"),
        root_derivation="SeacoastBoatV01.SampledSandGround+BoatYaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=stable(low[2]),
    )


def build_seacoast_slipway_barrier() -> AssemblySpec:
    kind, variant = "SeacoastSlipwayBarrier", 0
    fixture = [
        local_vertical_solid(-1.80, -0.08, 0.96, 0.0,
                             0.12, 0.12, top_scale=0.78, sides=9),
        local_vertical_solid(1.80, -0.08, 0.96, 0.0,
                             0.12, 0.12, top_scale=0.78, sides=9),
        local_tube((-1.80, 0.78, 0.0), (0.0, 0.66, 0.0),
                   0.045, sides=8),
        local_tube((0.0, 0.66, 0.0), (1.80, 0.78, 0.0),
                   0.045, sides=8),
        *(local_vertical_solid(x, 0.91, 1.06, 0.0,
                              0.15, 0.15, top_scale=0.55, sides=8)
          for x in (-1.80, 1.80)),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        unity_owned_parts=(
            "SlipwayRamp", "MooringRopes", "CollisionProxy"),
        root_derivation="SeacoastSlipwayBarrier.BeachEdgeGround",
        coordinate_profile="root_local_direct",
        expected_source_min_z=-0.08,
    )


def build_seacoast_barge() -> AssemblySpec:
    kind, variant = "SeacoastBarge", 0
    roll = 3.5

    def barge_box(
        along: float,
        up: float,
        across: float,
        width: float,
        height: float,
        depth: float,
        chamfer: float = 0.025,
    ) -> Geometry:
        center = rotate_source(
            local_point(along, 0.0, across),
            roll_forward_degrees=roll)
        return local_box(
            center[0], up + center[2], center[1],
            width, height, depth, chamfer,
            roll_forward_degrees=roll)

    industrial = [
        barge_box(0.0, -0.20, -1.56, 11.60, 2.10, 0.28, 0.035),
        barge_box(0.0, -0.20, 1.56, 11.60, 2.10, 0.28, 0.035),
        barge_box(-5.66, -0.20, 0.0, 0.28, 2.10, 3.40, 0.035),
        barge_box(5.66, -0.20, 0.0, 0.28, 2.10, 3.40, 0.035),
        barge_box(-1.20, 1.07, 0.0, 4.60, 0.34, 2.20, 0.045),
        barge_box(3.90, 1.50, 0.0, 2.40, 1.30, 2.60, 0.065),
        barge_box(4.40, 2.60, -0.60, 0.50, 0.90, 0.50, 0.055),
        barge_box(4.40, 3.06, -0.60, 0.68, 0.12, 0.68, 0.025),
    ]
    street = [
        barge_box(0.0, 0.77, 0.0, 11.40, 0.16, 3.20, 0.035),
        barge_box(-1.20, 1.25, 0.0, 3.90, 0.08, 1.62, 0.018),
    ]
    parts = (
        make_part(kind, variant, "Industrial", *industrial),
        make_part(kind, variant, "Street", *street),
    )
    low, _ = combined_bounds(parts)
    return AssemblySpec(
        kind, variant, parts,
        unity_owned_parts=(
            "SeaHeight", "SeedYaw", "CollisionProxy", "Water"),
        root_derivation="SeacoastBarge.SeaTopCentre+Yaw",
        coordinate_profile="root_local_direct",
        expected_source_min_z=stable(low[2]),
        canonical_reference=(
            ("length", 11.60), ("beam", 3.40), ("roll_degrees", roll)),
    )


def build_seacoast_driftwood(variant: int) -> AssemblySpec:
    kind = "SeacoastDriftwood"
    length_value = 2.80
    street = [
        local_tube((-length_value * 0.5, 0.13, 0.0),
                   (length_value * 0.5, 0.13, 0.0),
                   0.13 - variant * 0.012,
                   sides=8 - (variant % 2),
                   end_radius=0.085 + variant * 0.008),
    ]
    if variant == 0:
        street.append(local_tube(
            (0.45, 0.18, 0.0), (0.86, 0.48, 0.24),
            0.07, sides=7, end_radius=0.025))
    elif variant == 1:
        street.extend((
            local_tube((-0.62, 0.17, 0.0), (-0.92, 0.38, -0.20),
                       0.06, sides=7, end_radius=0.022),
            local_tube((0.76, 0.15, 0.0), (1.04, 0.29, 0.17),
                       0.05, sides=7, end_radius=0.02),
        ))
    else:
        street.append(local_box(
            0.12, 0.07, 0.0, 2.36, 0.14, 0.23, 0.025,
            yaw_up_degrees=-5.0))
    part = make_part(kind, variant, "Street", *street)
    low, high = geometry_bounds(part.geometry)
    if abs(low[2]) > BOUNDS_EPSILON:
        part = PartSpec(
            part.mesh, part.kind, part.variant, part.part_role,
            transform_geometry(
                part.geometry, (0.0, 0.0, -low[2])),
            part.placement_mode, part.normalized_height_m)
        low, high = geometry_bounds(part.geometry)
    return AssemblySpec(
        kind, variant, (part,),
        unity_owned_parts=("SampledSandGround", "SeedYaw"),
        root_derivation="descriptor.Center-up*(descriptor.Size.y/2)",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(("resolved_length", length_value),),
        scale_parameters=(scale_parameter(
            "resolved_length", ("X",), length_value, 1.50, 2.80),),
    )


def build_fringe_utility_pole() -> AssemblySpec:
    kind, variant = "FringeUtilityPole", 0
    fixture = [
        local_vertical_solid(0.0, 0.0, 4.45, 0.0,
                             0.11, 0.11, top_scale=0.70, sides=9),
        local_tube((-0.725, 4.22, 0.0),
                   (0.725, 4.22, 0.0), 0.07, sides=8),
        *(local_vertical_solid(x, 4.18, 4.40, 0.0,
                              0.055, 0.055, top_scale=0.75, sides=7)
          for x in (-0.52, 0.0, 0.52)),
        local_vertical_solid(0.0, 0.0, 0.20, 0.0,
                             0.16, 0.16, top_scale=0.78, sides=9),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, "Fixture", *fixture),),
        unity_owned_parts=("UtilityCables", "Infrastructure", "CollisionProxy"),
        root_derivation="FringeUtilityPole.DescriptorGround+Tangent",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("pole_height", 4.45), ("crossarm_width", 1.45)),
    )


def build_fringe_repair_stock(variant: int) -> AssemblySpec:
    kind = "FringeRepairStock"
    height = 0.30 + variant * 0.08
    role = "Fixture" if variant == 2 else "Masonry"
    stock: list[Geometry] = [
        local_box(0.0, height * 0.5, 0.0,
                  0.42, height, 3.10, 0.035),
    ]
    if variant < 2:
        for forward in (-1.18, -0.40, 0.40, 1.18):
            stock.append(local_box(
                0.0, height + 0.018, forward,
                0.34, 0.036, 0.12, 0.008))
    else:
        stock.extend((
            local_tube((-0.15, 0.06, -1.42),
                       (-0.15, height - 0.04, 1.42),
                       0.035, sides=7),
            local_tube((0.15, 0.06, -1.42),
                       (0.15, height - 0.04, 1.42),
                       0.035, sides=7),
        ))
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, role, *stock),),
        unity_owned_parts=("TerrainEmbed", "CollisionProxy"),
        root_derivation="descriptor.Center-up*(descriptor.Size.y/2)",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("descriptor_width", 0.42),
            ("descriptor_height", height),
            ("descriptor_depth", 3.10)),
    )


def build_fringe_pipe_stock(variant: int) -> AssemblySpec:
    kind = "FringePipeStock"
    role = "Masonry" if variant == 0 else "Fixture"
    width, height, depth = (
        (0.34, 0.34, 5.75),
        (0.58, 0.38, 7.40),
    )[variant]
    stock = [
        local_box(0.0, height * 0.5, 0.0,
                  width, height, depth, min(0.08, height * 0.24)),
        local_box(0.0, height * 0.5 + 0.02, -depth * 0.36,
                  width + 0.04, height + 0.04, 0.12, 0.012),
        local_box(0.0, height * 0.5 + 0.02, depth * 0.36,
                  width + 0.04, height + 0.04, 0.12, 0.012),
    ]
    return AssemblySpec(
        kind, variant, (make_part(kind, variant, role, *stock),),
        unity_owned_parts=("TerrainEmbed", "CollisionProxy"),
        root_derivation="descriptor.Center-up*(descriptor.Size.y/2)",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("descriptor_width", width),
            ("descriptor_height", height),
            ("descriptor_depth", depth)),
    )


def build_fringe_utility_shed_shell() -> AssemblySpec:
    kind, variant = "FringeUtilityShedShell", 0
    industrial = [
        local_box(0.0, 1.60, 0.0, 5.20, 3.20, 7.20, 0.10),
        local_box(0.0, 3.26, 0.0, 5.54, 0.18, 7.54, 0.055),
        *(local_box(x, 1.62, forward, 0.07, 2.72, 0.06, 0.010)
          for x in (-2.30, 2.30)
          for forward in (-2.65, 0.0, 2.65)),
    ]
    fixture = [
        local_box(-2.635, 1.10, 0.0, 0.10, 2.20, 2.00, 0.025),
        local_box(-2.695, 1.10, 0.0, 0.035, 1.82, 1.62, 0.008),
        local_vertical_solid(-2.72, 1.04, 1.18, 0.52,
                             0.035, 0.035, sides=8),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Industrial", *industrial),
         make_part(kind, variant, "Fixture", *fixture)),
        unity_owned_parts=("TerrainEmbed", "CollisionProxy", "Infrastructure"),
        root_derivation="FringeUtilityShed.DescriptorGround+Tangent",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(
            ("descriptor_width", 5.20),
            ("descriptor_height", 3.20),
            ("descriptor_depth", 7.20)),
    )


def build_fringe_flood_gauge_shell(variant: int) -> AssemblySpec:
    kind = "FringeFloodGaugeShell"
    mirror = 1.0 if variant == 0 else -1.0
    industrial = [
        local_vertical_solid(0.0, 0.0, 4.15, 0.0,
                             0.08, 0.08, top_scale=0.78, sides=9),
        *(local_box(0.0, height, 0.082,
                   0.10 + level * 0.012, 0.035, 0.025, 0.004)
          for level, height in enumerate(
              (0.55, 0.95, 1.35, 1.75, 2.15, 2.55, 2.95))),
    ]
    fixture = [
        local_tube((0.0, 1.42, -0.46),
                   (0.0, 1.42, 0.46), 0.06, sides=8),
        local_tube((-0.46, 1.42, 0.0),
                   (0.46, 1.42, 0.0), 0.06, sides=8),
        local_box(mirror * 0.19, 3.36, 0.0,
                  0.38, 0.30, 0.44, 0.035,
                  yaw_up_degrees=mirror * 90.0),
        local_tube((0.0, 3.24, 0.0),
                   (mirror * 0.21, 3.36, 0.0), 0.045, sides=8),
    ]
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Industrial", *industrial),
         make_part(kind, variant, "Fixture", *fixture)),
        unity_owned_parts=(
            "PracticalLens", "FloodGaugeLamp", "Halo",
            "TerrainEmbed", "CollisionProxy"),
        root_derivation="FringeFloodGauge.DescriptorGround+Tangent",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
        canonical_reference=(("gauge_height", 4.15),),
    )


def poi_scale_metadata() -> tuple[
    tuple[tuple[str, float], ...], tuple[ScaleParameterSpec, ...]
]:
    return (
        (("public_width", 15.0),),
        (scale_parameter(
            "public_width", ("X", "Y"), 15.0, 10.8, 16.2),),
    )


def build_poi_old_town_waterworks_shell() -> AssemblySpec:
    kind, variant = "PoiOldTownWaterworksShell", 0
    masonry = [
        local_box(-0.80, 0.15, 0.40, 4.15, 0.30, 1.55, 0.055),
        local_box(-0.80, 0.43, 1.10, 4.35, 0.56, 0.24, 0.04),
        local_box(-0.80, 0.43, -0.30, 4.35, 0.56, 0.24, 0.04),
        local_box(-2.86, 0.43, 0.40, 0.24, 0.56, 1.18, 0.04),
        local_box(1.26, 0.43, 0.40, 0.24, 0.56, 1.18, 0.04),
        local_vertical_solid(0.55, 0.0, 0.90, 0.40,
                             0.54, 0.54, top_scale=0.88, sides=12),
        local_vertical_solid(0.55, 0.82, 0.98, 0.40,
                             0.62, 0.62, top_scale=0.82, sides=12),
    ]
    street = [
        local_vertical_solid(0.55, 0.46, 3.50, 0.40,
                             0.29, 0.29, top_scale=0.72, sides=10),
        local_vertical_solid(0.55, 3.43, 3.67, 0.40,
                             0.51, 0.51, top_scale=0.90, sides=10),
        local_box(0.55, 2.82, 0.98, 0.30, 0.28, 1.28, 0.035),
        local_box(0.55, 2.62, 1.58, 0.42, 0.58, 0.30, 0.04),
        local_box(0.98, 1.82, 0.40, 0.20, 2.20, 0.20, 0.028),
        local_box(0.77, 2.73, 0.40, 0.62, 0.18, 0.22, 0.025),
        local_box(0.55, 1.32, 0.40, 0.78, 0.15, 0.78, 0.028),
        local_box(0.55, 2.42, 0.40, 0.76, 0.15, 0.76, 0.028),
        local_box(-0.02, 2.05, 0.40, 0.95, 0.14, 0.14, 0.022),
        local_box(-0.47, 2.05, 0.40, 0.12, 0.72, 0.12, 0.018),
        local_box(-1.80, 0.075, -1.08, 0.16, 0.025, 2.00, 0.006),
        local_box(-0.95, 0.075, -1.28, 0.16, 0.025, 1.55, 0.006),
        local_box(-0.10, 0.075, -1.02, 0.16, 0.025, 1.95, 0.006),
    ]
    canonical, parameters = poi_scale_metadata()
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Masonry", *masonry),
         make_part(kind, variant, "Street", *street)),
        canonical_reference=canonical,
        scale_parameters=parameters,
        unity_owned_parts=(
            "Dark Water", "Working Lamp", "Waterworks Basin Collider"),
        root_derivation="POIRecipeRoot@descriptor.Center+ResolveForward",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_poi_residential_drying_yard_shell() -> AssemblySpec:
    kind, variant = "PoiResidentialDryingYardShell", 0
    painted: list[Geometry] = []
    for forward in (-3.0, 0.0, 3.0):
        painted.extend((
            local_vertical_solid(-4.55, 0.0, 2.70, forward,
                                 0.10, 0.10, top_scale=0.92, sides=8),
            local_vertical_solid(4.55, 0.0, 2.70, forward,
                                 0.10, 0.10, top_scale=0.92, sides=8),
            local_tube((-4.65, 2.66, forward),
                       (4.65, 2.66, forward), 0.10, sides=8),
            local_tube((-4.525, 2.34, forward - 0.16),
                       (4.525, 2.34, forward - 0.16), 0.023, sides=7),
            local_tube((-4.525, 2.20, forward + 0.16),
                       (4.525, 2.20, forward + 0.16), 0.023, sides=7),
        ))
    painted.extend((
        local_box(-4.02, 0.28, 4.45, 0.18, 0.50, 0.42, 0.022),
        local_box(-2.48, 0.28, 4.45, 0.18, 0.50, 0.42, 0.022),
        local_vertical_solid(-6.05, 0.0, 1.62, -1.35,
                             0.07, 0.07, top_scale=0.92, sides=8),
        local_vertical_solid(-6.05, 0.0, 1.62, 1.55,
                             0.07, 0.07, top_scale=0.92, sides=8),
        local_tube((-6.05, 1.62, -1.42),
                   (-6.05, 1.62, 1.62), 0.05, sides=8),
        local_vertical_solid(4.10, 0.0, 4.28, 4.55,
                             0.11, 0.11, top_scale=0.72, sides=10),
    ))
    aim_start = (4.10, 4.28, 4.55)
    aim_end = (0.0, 1.30, 0.20)
    aim_delta = (
        aim_end[0] - aim_start[0],
        aim_end[1] - aim_start[1],
        aim_end[2] - aim_start[2],
    )
    aim_length = math.sqrt(sum(value * value for value in aim_delta))
    aim_direction = tuple(value / aim_length for value in aim_delta)
    aim_horizontal = math.sqrt(
        aim_direction[0] ** 2 + aim_direction[2] ** 2)
    aim_yaw = math.degrees(math.atan2(
        aim_direction[0], aim_direction[2]))
    aim_pitch = math.degrees(math.atan2(
        -aim_direction[1], aim_horizontal))
    painted.extend((
        local_box(
            aim_start[0] - aim_direction[0] * 0.16,
            aim_start[1] - aim_direction[1] * 0.16,
            aim_start[2] - aim_direction[2] * 0.16,
            0.46, 0.30, 0.38, 0.04,
            pitch_x_degrees=aim_pitch,
            yaw_up_degrees=aim_yaw),
        local_tube((4.10, 4.12, 4.55),
                   (4.10, 4.28, 4.55), 0.055, sides=8),
    ))
    timber = [local_box(
        -3.25, 0.53, 4.45, 2.40, 0.18, 0.58, 0.035)]
    canonical, parameters = poi_scale_metadata()
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Residential_PaintedMetal", *painted),
         make_part(kind, variant, "Residential_Timber", *timber)),
        canonical_reference=canonical,
        scale_parameters=parameters,
        unity_owned_parts=(
            "Large Faded Blanket", "Blanket Repair Patch",
            "Cold Sheet", "Small Towel", "Beaten Carpet South",
            "Beaten Carpet North", "Carpet Cloth/Fold Overlays",
            "Floodlight Lens", "Drying Yard Floodlight Light",
            "Floodlight Source Halo", "NPCs", "CollisionProxies"),
        root_derivation="POIRecipeRoot@descriptor.Center+ResolveForward",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_poi_industrial_weighbridge_shell() -> AssemblySpec:
    kind, variant = "PoiIndustrialWeighbridgeShell", 0
    industrial = [
        local_box(0.0, 0.16, 0.0, 3.60, 0.22, 11.60, 0.055),
        local_box(3.25, 0.34, 0.20, 1.20, 0.56, 1.28, 0.055),
        local_box(3.25, 2.52, 0.20, 0.30, 4.35, 0.34, 0.035),
        local_box(3.25, 4.63, 0.20, 2.25, 0.82, 0.62, 0.065),
        local_box(2.65, 1.10, 0.20, 1.08, 0.20, 0.22, 0.028),
        *(local_box(side * 1.62, 0.19, forward,
                   0.42, 0.28, 0.72, 0.035)
          for side in (-1, 1) for forward in (-4.45, 4.45)),
    ]
    street = [
        local_box(-1.48, 0.285, 0.0, 0.20, 0.035, 10.80, 0.008),
        local_box(1.48, 0.285, 0.0, 0.20, 0.035, 10.80, 0.008),
        local_box(0.0, 0.305, 3.62, 3.05, 0.025, 0.20, 0.006),
        local_box(0.0, 0.305, -3.62, 3.05, 0.025, 0.20, 0.006),
        local_box(0.62, 0.31, -1.25, 1.05, 0.035, 1.45, 0.008),
        local_box(-0.92, 0.42, -5.10, 0.62, 0.26, 0.42, 0.03,
                  yaw_up_degrees=14.0),
        local_box(0.92, 0.42, -5.10, 0.62, 0.26, 0.42, 0.03,
                  yaw_up_degrees=-14.0),
    ]
    canonical, parameters = poi_scale_metadata()
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Industrial", *industrial),
         make_part(kind, variant, "Street", *street)),
        canonical_reference=canonical,
        scale_parameters=parameters,
        unity_owned_parts=(
            "Scale Indicator Face", "Scale Needle", "Cold Service Lamp",
            "IndicatorRegistry", "NPCs", "CollisionProxies"),
        root_derivation="POIRecipeRoot@descriptor.Center+ResolveForward",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.05,
    )


def build_poi_nightlife_last_route_island_shell() -> AssemblySpec:
    kind, variant = "PoiNightlifeLastRouteIslandShell", 0
    masonry = [
        local_vertical_solid(0.0, 0.03, 0.21, 0.0,
                             5.40, 5.40, top_scale=1.0, sides=24),
        local_vertical_solid(0.0, 0.20, 0.25, 0.0,
                             3.60, 3.60, top_scale=1.0, sides=22),
        local_vertical_solid(0.0, 0.235, 0.275, 0.0,
                             2.10, 2.10, top_scale=1.0, sides=20),
    ]
    street: list[Geometry] = []
    residential: list[Geometry] = []
    for index, angle in enumerate((48.0, 102.0, 168.0, 226.0, 292.0)):
        radians = math.radians(angle)
        x = math.sin(radians) * 4.70
        forward = math.cos(radians) * 4.70
        street.extend((
            local_vertical_solid(x, 0.0, 3.40, forward,
                                 0.15, 0.15, top_scale=0.82, sides=8),
            local_box(x, 3.36, forward,
                      3.25, 0.26, 0.42, 0.035,
                      yaw_up_degrees=angle),
            local_box(x, 3.58, forward,
                      3.45, 0.18, 1.25, 0.045,
                      yaw_up_degrees=angle),
        ))
        if index in (1, 4):
            plate_offset = 0.18
            residential.append(local_box(
                x + math.sin(radians) * plate_offset,
                2.42,
                forward + math.cos(radians) * plate_offset,
                0.62, 0.44, 0.07, 0.014,
                yaw_up_degrees=angle))
    street.extend((
        local_box(-2.75, 0.52, -1.25, 1.12, 0.78, 1.12, 0.055),
        local_box(-2.75, 3.35, -1.25, 0.34, 5.70, 0.34, 0.045),
        local_box(-2.75, 5.55, -1.25, 1.58, 1.55, 0.42, 0.06),
        local_box(-2.75, 4.18, -0.99, 0.78, 0.48, 0.18, 0.035),
        local_box(-2.75, 4.18, -0.885, 0.62, 0.32, 0.035, 0.008),
        local_box(2.45, 2.10, -2.55, 2.65, 1.10, 0.28, 0.055,
                  yaw_up_degrees=-12.0),
        local_box(1.61, 0.885, -2.73, 0.20, 1.33, 0.24, 0.025,
                  yaw_up_degrees=-12.0),
        local_box(3.29, 0.885, -2.37, 0.20, 1.33, 0.24, 0.025,
                  yaw_up_degrees=-12.0),
        local_box(1.61, 0.27, -2.73, 0.48, 0.12, 0.46, 0.02,
                  yaw_up_degrees=-12.0),
        local_box(3.29, 0.27, -2.37, 0.48, 0.12, 0.46, 0.02,
                  yaw_up_degrees=-12.0),
        local_box(2.85, 0.33, 2.55, 0.38, 0.66, 0.48, 0.03,
                  yaw_up_degrees=22.0),
        local_box(4.15, 0.71, 2.20, 0.72, 1.00, 0.72, 0.04,
                  yaw_up_degrees=8.0),
        local_box(4.15, 1.23, 2.20, 0.82, 0.08, 0.82, 0.018,
                  yaw_up_degrees=8.0),
        local_box(-2.575, 4.42, -1.15, 0.14, 0.14, 0.60, 0.018,
                  yaw_up_degrees=60.0),
        local_box(-2.40, 4.42, -1.21, 0.46, 0.30, 0.38, 0.04,
                  yaw_up_degrees=42.0),
    ))
    residential.extend((
        local_box(-2.75, 5.55, -1.02, 1.28, 1.20, 0.04, 0.008),
        local_box(-2.95, 5.66, -0.99, 0.64, 0.70, 0.025, 0.006,
                  yaw_up_degrees=-4.0),
        local_box(-2.52, 5.33, -0.98, 0.50, 0.43, 0.025, 0.006,
                  yaw_up_degrees=6.0),
        local_box(-2.71, 5.97, -0.97, 0.42, 0.20, 0.025, 0.006),
        local_box(2.45, 2.10, -2.39, 2.30, 0.78, 0.035, 0.008,
                  yaw_up_degrees=-12.0),
        local_box(2.45, 2.30, -2.365, 1.78, 0.07, 0.025, 0.006,
                  yaw_up_degrees=-12.0),
        local_box(2.45, 2.10, -2.365, 1.42, 0.07, 0.025, 0.006,
                  yaw_up_degrees=-12.0),
        local_box(2.45, 1.90, -2.365, 1.92, 0.07, 0.025, 0.006,
                  yaw_up_degrees=-12.0),
        local_vertical_solid(2.08, 0.22, 0.40, 3.82,
                             0.065, 0.065, top_scale=0.62, sides=8),
        local_box(1.72, 0.255, 3.68, 0.34, 0.07, 0.12, 0.012,
                  yaw_up_degrees=28.0),
        local_box(-1.20, 0.228, -3.65, 0.72, 0.025, 0.50, 0.006,
                  yaw_up_degrees=12.0),
    ))
    timber = [local_box(
        2.85, 0.66, 2.55, 2.50, 0.22, 0.72, 0.035,
        yaw_up_degrees=22.0)]
    canonical, parameters = poi_scale_metadata()
    return AssemblySpec(
        kind, variant,
        (make_part(kind, variant, "Masonry", *masonry),
         make_part(kind, variant, "Street", *street),
         make_part(kind, variant, "Residential", *residential),
         make_part(kind, variant, "Timber", *timber)),
        canonical_reference=canonical,
        scale_parameters=parameters,
        unity_owned_parts=(
            "Broken Canopy Segment * Rag Cloth", "Lost Scarf",
            "Island Floodlight Lens", "Island Mast Floodlight Light",
            "Island Floodlight Source Halo",
            "Ferryman Car Floodlight Assembly", "NPCs",
            "SitDock", "CollisionProxies"),
        root_derivation="POIRecipeRoot@descriptor.Center+ResolveForward",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def special_building_scale_metadata(
    frontage_width: float,
    depth: float,
    height: float,
) -> tuple[tuple[tuple[str, float], ...], tuple[ScaleParameterSpec, ...]]:
    canonical = (
        ("lot_frontage_width", frontage_width),
        ("lot_depth", depth),
        ("lot_height", height),
    )
    parameters = (
        scale_parameter(
            "lot_frontage_width", ("X",), frontage_width, 6.0, 24.0),
        scale_parameter(
            "lot_depth", ("Y",), depth, 6.0, 24.0),
        scale_parameter(
            "lot_height", ("Z",), height, 4.5, 14.0),
    )
    return canonical, parameters


def make_special_building(
    kind: str,
    frontage_width: float,
    depth: float,
    height: float,
    shell: Sequence[Geometry],
    roof: Sequence[Geometry],
    trim: Sequence[Geometry],
    unity_owned_parts: tuple[str, ...],
) -> AssemblySpec:
    canonical, parameters = special_building_scale_metadata(
        frontage_width, depth, height)
    return AssemblySpec(
        kind,
        0,
        (
            make_named_part(
                kind, 0, "Shell_Masonry", "Masonry", *shell),
            make_named_part(
                kind, 0, "Roof_Street", "Street", *roof),
            make_named_part(
                kind, 0, "Trim_Industrial", "Industrial", *trim),
        ),
        canonical_reference=canonical,
        scale_parameters=parameters,
        unity_owned_parts=("Terrain foundation skirt", *unity_owned_parts),
        root_derivation="BuildingLot.Center+FrontageDirection",
        coordinate_profile="root_local_direct",
        expected_source_min_z=0.0,
    )


def build_bar_building_shell() -> AssemblySpec:
    kind = "BarBuildingShell"
    width, depth, height = 12.2645, 13.5237, 9.3435
    shell = (
        local_box(0.0, height * 0.50, 0.0,
                  width, height, depth, 0.16),
        local_box(-width * 0.31, height * 0.36, -depth * 0.43,
                  width * 0.25, height * 0.58, depth * 0.14, 0.10),
    )
    roof = (
        local_box(0.0, height + 0.15, 0.0,
                  width + 0.34, 0.30, depth + 0.34, 0.08),
        local_box(-width * 0.20, height + 0.43, -depth * 0.16,
                  width * 0.52, 0.26, depth * 0.58, 0.07),
    )
    trim: list[Geometry] = [
        local_box(0.0, 0.18, 0.0,
                  width + 0.08, 0.36, depth + 0.08, 0.04),
        local_box(0.0, height * 0.74, depth * 0.496,
                  width, 0.24, 0.18, 0.035),
    ]
    for side in (-1, 1):
        trim.append(local_box(
            side * width * 0.465, height * 0.48, depth * 0.496,
            0.34, height * 0.88, 0.18, 0.035))
        trim.append(local_box(
            side * width * 0.465, height * 0.48, -depth * 0.496,
            0.34, height * 0.88, 0.18, 0.035))
    return make_special_building(
        kind, width, depth, height, shell, roof, trim,
        (
            "Building Mass collider", "Window bands", "Bar door",
            "Bar canopy", "Bar sign", "Entrance trigger",
        ),
    )


def build_supermarket_building_shell() -> AssemblySpec:
    kind = "SupermarketBuildingShell"
    width, depth, height = 15.5, 15.5, 6.4
    shell = (
        local_box(0.0, height * 0.50, 0.0,
                  width, height, depth, 0.14),
        local_box(0.0, height * 0.60, -depth * 0.43,
                  width * 0.52, height * 0.42, depth * 0.14, 0.08),
    )
    roof: list[Geometry] = [
        local_box(0.0, height + 0.14, 0.0,
                  width + 0.38, 0.28, depth + 0.38, 0.07),
    ]
    for lane in (-1, 0, 1):
        roof.append(local_box(
            lane * width * 0.235, height + 0.43, -depth * 0.06,
            width * 0.27, 0.22, depth * 0.70, 0.055,
            roll_forward_degrees=-7.5))
    trim: list[Geometry] = [
        local_box(0.0, 0.16, 0.0,
                  width + 0.08, 0.32, depth + 0.08, 0.035),
        local_box(0.0, height * 0.78, depth * 0.497,
                  width, 0.34, 0.16, 0.035),
    ]
    for side in (-1, 1):
        trim.append(local_box(
            side * width * 0.472, height * 0.48, depth * 0.497,
            0.30, height * 0.88, 0.16, 0.03))
    return make_special_building(
        kind, width, depth, height, shell, roof, trim,
        (
            "Building Mass collider", "Window bands", "Storefront glass",
            "Supermarket door", "Canopy", "Signs", "Entrance trigger",
        ),
    )


def build_player_home_building_shell() -> AssemblySpec:
    kind = "PlayerHomeBuildingShell"
    width, depth, height = 13.0, 12.0, 8.8
    shell = (
        local_box(0.0, height * 0.50, 0.0,
                  width, height, depth, 0.12),
        local_box(width * 0.22, height * 0.58, -depth * 0.41,
                  width * 0.26, height * 0.72, depth * 0.16, 0.07),
    )
    roof = (
        local_box(0.0, height + 0.20, 0.0,
                  width + 0.48, 0.40, depth + 0.48, 0.065),
        local_box(-width * 0.28, height + 0.88, depth * 0.20,
                  0.68, 1.36, 0.68, 0.055),
        local_box(-width * 0.28, height + 1.60, depth * 0.20,
                  0.82, 0.16, 0.82, 0.045),
    )
    trim: list[Geometry] = [
        local_box(0.0, 0.17, 0.0,
                  width + 0.06, 0.34, depth + 0.06, 0.035),
        local_box(0.0, height * 0.36, depth * 0.498,
                  width, 0.20, 0.15, 0.03),
        local_box(0.0, height * 0.68, depth * 0.498,
                  width, 0.20, 0.15, 0.03),
        local_box(width * 0.22, height * 0.49, depth * 0.499,
                  width * 0.18, height * 0.86, 0.16, 0.035),
    ]
    return make_special_building(
        kind, width, depth, height, shell, roof, trim,
        (
            "Building Mass collider", "Window bands", "Home balcony",
            "Home door", "Mailbox", "Entrance lamp", "House number",
            "Roof beacon", "Entrance trigger",
        ),
    )


def make_assemblies() -> tuple[AssemblySpec, ...]:
    return (
        # Wave 1 compatibility block. Keep these first 15 assemblies and their
        # 33 meshes in this exact order: the compatibility hash covers them.
        build_industrial_stacks_and_tanks(),
        *(build_industrial_cargo(index) for index in range(2)),
        build_nightlife_vending_and_queue(),
        *(build_roadside_roadwork_and_bicycle(index) for index in range(2)),
        *(build_park_tree(index) for index in range(4)),
        *(build_park_bench(index) for index in range(2)),
        build_roadside_phone_booth(),
        build_roadside_dumpster_and_utility(),
        build_street_lamp_shell(),
        # Remaining CityDecorationKind catalog (RoadsideBusShelter is excluded:
        # it has no legacy placements and will be replaced by a dedicated stop).
        *(build_old_town_chimneys_and_dormers(index) for index in range(2)),
        build_old_town_scaffolding(),
        build_old_town_street_market(),
        build_old_town_clock_tower(),
        *(build_residential_balconies(index) for index in range(2)),
        build_residential_laundry_and_antenna(),
        *(build_residential_discarded_furniture(index)
          for index in range(2)),
        build_residential_rooftop_greenhouse(),
        build_industrial_pipe_rack(),
        *(build_industrial_gantry(index) for index in range(2)),
        build_nightlife_billboard(),
        build_nightlife_fire_escape(),
        *(build_nightlife_cinema(index) for index in range(2)),
        build_park_fountain_and_statue(),
        build_park_bandstand(),
        build_park_chess_tables(),
        build_park_playground(),
        # Citywide v3 static shell/archetype catalog.
        build_route01_shelter_shell(),
        build_route01_pole_shell(),
        build_traffic_signal_shell(),
        build_yard_dead_tree(),
        build_yard_bench(),
        build_yard_carpet_frame(),
        build_yard_sandpit(),
        build_yard_child_toy(),
        build_yard_dead_lamp(),
        build_yard_bin(),
        build_yard_bottle(),
        build_yard_spotlight_wall_mount(),
        build_yard_spotlight_head_shell(),
        *(build_cemetery_grave_slab(index) for index in range(6)),
        *(build_cemetery_grave_monument(index) for index in range(5)),
        build_cemetery_overgrown_mound(),
        build_cemetery_grave_enclosure(),
        build_cemetery_grave_offering(),
        *(build_cemetery_tree(index) for index in range(2)),
        build_cemetery_bush(),
        build_cemetery_bench(),
        *(build_seacoast_boat(index) for index in range(4)),
        build_seacoast_oar(),
        build_seacoast_slipway_barrier(),
        build_seacoast_barge(),
        *(build_seacoast_driftwood(index) for index in range(3)),
        build_fringe_utility_pole(),
        *(build_fringe_repair_stock(index) for index in range(3)),
        *(build_fringe_pipe_stock(index) for index in range(2)),
        build_fringe_utility_shed_shell(),
        *(build_fringe_flood_gauge_shell(index) for index in range(2)),
        build_poi_old_town_waterworks_shell(),
        build_poi_residential_drying_yard_shell(),
        build_poi_industrial_weighbridge_shell(),
        build_poi_nightlife_last_route_island_shell(),
        build_bar_building_shell(),
        build_supermarket_building_shell(),
        build_player_home_building_shell(),
        # Citywide v4.1: the ground-level water network. Appended, never
        # inserted — both compatibility signatures cover prefixes.
        build_roadside_drain_and_cover(),
        build_roadside_capped_standpipe(),
        build_lot_ground_downpipe_outfall(),
    )


# --------------------------------------------------------- validation --


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


def signed_volume(geometry: Geometry) -> float:
    vertices, faces = geometry
    total = 0.0
    for face in faces:
        for index in range(1, len(face) - 1):
            a = vertices[face[0]]
            b = vertices[face[index]]
            c = vertices[face[index + 1]]
            total += dot(a, cross(b, c))
    return total / 6.0


def geometry_uvs(geometry: Geometry) -> list[Vec2]:
    vertices, faces = geometry
    raw: list[Vec2] = []
    for face in faces:
        normal = face_normal(vertices, face)
        dominant = max(range(3), key=lambda axis: abs(normal[axis]))
        for index in face:
            x, y, z = vertices[index]
            if dominant == 0:
                raw.append((y, z))
            elif dominant == 1:
                raw.append((x, z))
            else:
                raw.append((x, y))
    low_u = min(value[0] for value in raw)
    high_u = max(value[0] for value in raw)
    low_v = min(value[1] for value in raw)
    high_v = max(value[1] for value in raw)
    span_u = max(high_u - low_u, 1e-6)
    span_v = max(high_v - low_v, 1e-6)
    return [
        (stable((u - low_u) / span_u), stable((v - low_v) / span_v))
        for u, v in raw
    ]


def uv_bounds(geometry: Geometry) -> tuple[Vec2, Vec2]:
    uvs = geometry_uvs(geometry)
    return (
        (min(uv[0] for uv in uvs), min(uv[1] for uv in uvs)),
        (max(uv[0] for uv in uvs), max(uv[1] for uv in uvs)),
    )


EXPECTED_VARIANTS = {
    "IndustrialStacksAndTanks": 1,
    "IndustrialCargo": 2,
    "NightlifeVendingAndQueue": 1,
    "RoadsideRoadworkAndBicycle": 2,
    "ParkTree": 4,
    "ParkBench": 2,
    "RoadsidePhoneBooth": 1,
    "RoadsideDumpsterAndUtility": 1,
    "StreetLampShell": 1,
    "OldTownChimneysAndDormers": 2,
    "OldTownScaffolding": 1,
    "OldTownStreetMarket": 1,
    "OldTownClockTower": 1,
    "ResidentialBalconies": 2,
    "ResidentialLaundryAndAntenna": 1,
    "ResidentialDiscardedFurniture": 2,
    "ResidentialRooftopGreenhouse": 1,
    "IndustrialPipeRack": 1,
    "IndustrialGantry": 2,
    "NightlifeBillboard": 1,
    "NightlifeFireEscape": 1,
    "NightlifeCinema": 2,
    "ParkFountainAndStatue": 1,
    "ParkBandstand": 1,
    "ParkChessTables": 1,
    "ParkPlayground": 1,
    "Route01ShelterShell": 1,
    "Route01PoleShell": 1,
    "TrafficSignalShell": 1,
    "YardDeadTree": 1,
    "YardBench": 1,
    "YardCarpetFrame": 1,
    "YardSandpit": 1,
    "YardChildToy": 1,
    "YardDeadLamp": 1,
    "YardBin": 1,
    "YardBottle": 1,
    "YardSpotlightWallMount": 1,
    "YardSpotlightHeadShell": 1,
    "CemeteryGraveSlab": 6,
    "CemeteryGraveMonument": 5,
    "CemeteryOvergrownMound": 1,
    "CemeteryGraveEnclosure": 1,
    "CemeteryGraveOffering": 1,
    "CemeteryTree": 2,
    "CemeteryBush": 1,
    "CemeteryBench": 1,
    "SeacoastBoat": 4,
    "SeacoastOar": 1,
    "SeacoastSlipwayBarrier": 1,
    "SeacoastBarge": 1,
    "SeacoastDriftwood": 3,
    "FringeUtilityPole": 1,
    "FringeRepairStock": 3,
    "FringePipeStock": 2,
    "FringeUtilityShedShell": 1,
    "FringeFloodGaugeShell": 2,
    "PoiOldTownWaterworksShell": 1,
    "PoiResidentialDryingYardShell": 1,
    "PoiIndustrialWeighbridgeShell": 1,
    "PoiNightlifeLastRouteIslandShell": 1,
    "BarBuildingShell": 1,
    "SupermarketBuildingShell": 1,
    "PlayerHomeBuildingShell": 1,
    "RoadsideDrainAndCover": 1,
    "RoadsideCappedStandpipe": 1,
    "LotGroundDownpipeOutfall": 1,
}

EXPECTED_ROLES = {
    ("IndustrialStacksAndTanks", 0): ("Industrial", "Street"),
    **{("IndustrialCargo", index):
       ("Industrial", "Street", "Masonry") for index in range(2)},
    ("NightlifeVendingAndQueue", 0):
        ("Street", "Industrial", "Neon"),
    **{("RoadsideRoadworkAndBicycle", index):
       ("Street", "Masonry", "Industrial") for index in range(2)},
    **{("ParkTree", index):
       ("Bark", "Foliage") for index in range(4)},
    **{("ParkBench", index): ("Timber",) for index in range(2)},
    ("RoadsidePhoneBooth", 0):
        ("Street", "Residential", "BacklitSign"),
    ("RoadsideDumpsterAndUtility", 0): ("Industrial", "Street"),
    ("StreetLampShell", 0): ("Fixture",),
    **{("OldTownChimneysAndDormers", index):
       ("Masonry", "Masonry", "Street") for index in range(2)},
    ("OldTownScaffolding", 0): ("Industrial", "Masonry"),
    ("OldTownStreetMarket", 0):
        ("Street", "Masonry", "Residential"),
    ("OldTownClockTower", 0):
        ("Masonry", "Street", "Residential"),
    **{("ResidentialBalconies", index):
       ("Residential", "Street") for index in range(2)},
    ("ResidentialLaundryAndAntenna", 0):
        ("Street", "Residential", "Masonry"),
    **{("ResidentialDiscardedFurniture", index):
       ("Residential", "Street", "Masonry") for index in range(2)},
    ("ResidentialRooftopGreenhouse", 0):
        ("Masonry", "Residential", "Residential", "Street"),
    ("IndustrialPipeRack", 0): ("Street", "Industrial"),
    **{("IndustrialGantry", index):
       ("Industrial", "Street") for index in range(2)},
    ("NightlifeBillboard", 0): ("Street", "Neon"),
    ("NightlifeFireEscape", 0): ("Industrial", "Street"),
    **{("NightlifeCinema", index):
       ("Street", "Neon", "Masonry") for index in range(2)},
    ("ParkFountainAndStatue", 0):
        ("Masonry_Stone", "Street_Stone"),
    ("ParkBandstand", 0): (
        "Masonry_Stone", "Residential_Timber",
        "Masonry_Timber", "Street_PaintedMetal"),
    ("ParkChessTables", 0): (
        "Masonry_Stone", "Masonry_Timber", "Street_Timber",
        "Masonry_Stone", "Masonry_Stone", "Street_Timber",
        "Masonry_Stone", "Masonry_Stone"),
    ("ParkPlayground", 0): (
        "Residential_PaintedMetal", "Masonry_Timber",
        "Street_PaintedMetal"),
    ("Route01ShelterShell", 0): ("Fixture", "Timber"),
    ("Route01PoleShell", 0):
        ("Fixture", "Street", "Residential"),
    ("TrafficSignalShell", 0): ("Fixture",),
    ("YardDeadTree", 0): ("Bark",),
    ("YardBench", 0): ("Timber", "Fixture"),
    ("YardCarpetFrame", 0): ("Fixture",),
    ("YardSandpit", 0): ("Timber",),
    ("YardChildToy", 0): ("Residential",),
    ("YardDeadLamp", 0): ("Fixture",),
    ("YardBin", 0): ("Fixture",),
    ("YardBottle", 0): ("Timber",),
    ("YardSpotlightWallMount", 0): ("Fixture",),
    ("YardSpotlightHeadShell", 0): ("Fixture",),
    **{("CemeteryGraveSlab", index): ("Masonry",)
       for index in range(6)},
    **{("CemeteryGraveMonument", index): ("Masonry",)
       for index in range(5)},
    ("CemeteryOvergrownMound", 0): ("Street", "Residential"),
    ("CemeteryGraveEnclosure", 0): ("Fixture",),
    ("CemeteryGraveOffering", 0): ("Residential",),
    **{("CemeteryTree", index): ("Bark", "Foliage")
       for index in range(2)},
    ("CemeteryBush", 0): ("Foliage",),
    ("CemeteryBench", 0): ("Timber", "Fixture"),
    **{("SeacoastBoat", index):
       ("Residential", "Street", "Timber") for index in range(4)},
    ("SeacoastOar", 0): ("Street",),
    ("SeacoastSlipwayBarrier", 0): ("Fixture",),
    ("SeacoastBarge", 0): ("Industrial", "Street"),
    **{("SeacoastDriftwood", index): ("Street",)
       for index in range(3)},
    ("FringeUtilityPole", 0): ("Fixture",),
    ("FringeRepairStock", 0): ("Masonry",),
    ("FringeRepairStock", 1): ("Masonry",),
    ("FringeRepairStock", 2): ("Fixture",),
    ("FringePipeStock", 0): ("Masonry",),
    ("FringePipeStock", 1): ("Fixture",),
    ("FringeUtilityShedShell", 0): ("Industrial", "Fixture"),
    **{("FringeFloodGaugeShell", index): ("Industrial", "Fixture")
       for index in range(2)},
    ("PoiOldTownWaterworksShell", 0): ("Masonry", "Street"),
    ("PoiResidentialDryingYardShell", 0):
        ("Residential_PaintedMetal", "Residential_Timber"),
    ("PoiIndustrialWeighbridgeShell", 0): ("Industrial", "Street"),
    ("PoiNightlifeLastRouteIslandShell", 0):
        ("Masonry", "Street", "Residential", "Timber"),
    ("BarBuildingShell", 0):
        ("Masonry", "Street", "Industrial"),
    ("SupermarketBuildingShell", 0):
        ("Masonry", "Street", "Industrial"),
    ("PlayerHomeBuildingShell", 0):
        ("Masonry", "Street", "Industrial"),
    ("RoadsideDrainAndCover", 0): ("Street", "Masonry"),
    ("RoadsideCappedStandpipe", 0): ("Street", "Masonry"),
    ("LotGroundDownpipeOutfall", 0): ("Street", "Masonry"),
}


EXPECTED_MESH_SUFFIXES = {
    **{
        (kind, variant): roles
        for kind, count in EXPECTED_VARIANTS.items()
        for variant in range(count)
        if (roles := EXPECTED_ROLES.get((kind, variant))) is not None
    },
    **{("OldTownChimneysAndDormers", index): (
        "Chimneys_Masonry", "Dormer_Masonry", "Window_Street")
       for index in range(2)},
    ("ResidentialRooftopGreenhouse", 0): (
        "Base_Masonry", "Frame_Residential",
        "Roof_Residential", "Hardware_Street"),
    ("ParkChessTables", 0): (
        "TableSlab_Masonry_Stone",
        "BoardLight_Masonry_Timber",
        "BoardDarkAndRim_Street_Timber",
        "TableFooting_Masonry_Stone",
        "TablePedestal_Masonry_Stone",
        "BenchSeat_Street_Timber",
        "BenchPad_Masonry_Stone",
        "BenchLeg_Masonry_Stone",
    ),
    **{(kind, 0): (
        "Shell_Masonry", "Roof_Street", "Trim_Industrial")
       for kind in (
           "BarBuildingShell",
           "SupermarketBuildingShell",
           "PlayerHomeBuildingShell",
       )},
}


def validate_geometry(
    part: PartSpec,
    problems: list[str],
    allow_below_anchor: bool = False,
) -> None:
    vertices, faces = part.geometry
    if not vertices or not faces:
        problems.append(f"{part.mesh} is empty")
        return
    for vertex in vertices:
        if not all(math.isfinite(value) for value in vertex):
            problems.append(f"{part.mesh} has a non-finite vertex")
            break
    for face in faces:
        if len(face) < 3 or any(
                index < 0 or index >= len(vertices) for index in face):
            problems.append(f"{part.mesh} has an invalid face")
            continue
        if length(face_normal(vertices, face)) < 0.5:
            problems.append(f"{part.mesh} has a degenerate face")
    volume = signed_volume(part.geometry)
    if not math.isfinite(volume) or volume <= 1e-7:
        problems.append(
            f"{part.mesh} signed volume {volume:.8f} is not positive")

    low, high = geometry_bounds(part.geometry)
    if low[2] < -BOUNDS_EPSILON and not allow_below_anchor:
        problems.append(f"{part.mesh} extends below its ground plane")
    if any(high[axis] - low[axis] <= BOUNDS_EPSILON for axis in range(3)):
        problems.append(f"{part.mesh} has a collapsed bound")

    uvs = geometry_uvs(part.geometry)
    if len(uvs) != sum(len(face) for face in faces):
        problems.append(f"{part.mesh} has incomplete UV loops")
    if any(not math.isfinite(value) or value < -UV_EPSILON or
           value > 1.0 + UV_EPSILON for uv in uvs for value in uv):
        problems.append(f"{part.mesh} has UVs outside [0,1]")
    uv_low, uv_high = uv_bounds(part.geometry)
    if uv_high[0] - uv_low[0] < 0.98 or uv_high[1] - uv_low[1] < 0.98:
        problems.append(f"{part.mesh} has a collapsed UV span")


def validate_handedness(problems: list[str]) -> None:
    authored = recipe_point(1.25, 2.50, 3.00, 0.40)
    unity = source_to_unity(authored)
    expected = [-1.25, 2.50, 1.20]
    if unity != expected:
        problems.append(
            f"legacy recipe basis maps to {unity}, expected {expected}")


def validate_mirror_bounds(
    assemblies: Sequence[AssemblySpec],
    kind: str,
    problems: list[str],
) -> None:
    pair = [assembly for assembly in assemblies if assembly.kind == kind]
    if len(pair) != 2:
        problems.append(f"{kind} does not have one explicit mirror pair")
        return
    first_low, first_high = combined_bounds(pair[0].parts)
    second_low, second_high = combined_bounds(pair[1].parts)
    if abs(first_low[0] + second_high[0]) > 1e-5 or \
            abs(first_high[0] + second_low[0]) > 1e-5:
        problems.append(f"{kind} mirror variants do not reflect source X")
    for axis in (1, 2):
        if abs(first_low[axis] - second_low[axis]) > 1e-5 or \
                abs(first_high[axis] - second_high[axis]) > 1e-5:
            problems.append(
                f"{kind} mirror variants drift on source axis {axis}")


def wave1_compatibility_payload(
    assemblies: Sequence[AssemblySpec],
) -> list[dict]:
    """Geometry-only payload frozen when the 15/33 wave-one catalog shipped."""
    return [
        {
            "mesh": part.mesh,
            "kind": part.kind,
            "variant": part.variant,
            "role": part.part_role,
            "vertices": [
                [stable(value) for value in vertex]
                for vertex in part.geometry[0]
            ],
            "faces": [list(face) for face in part.geometry[1]],
        }
        for assembly in assemblies[:15]
        for part in assembly.parts
    ]


def wave1_compatibility_signature(
    assemblies: Sequence[AssemblySpec],
) -> str:
    encoded = json.dumps(
        wave1_compatibility_payload(assemblies),
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def validate_assemblies(assemblies: Sequence[AssemblySpec]) -> None:
    problems: list[str] = []
    names: set[str] = set()
    keys: set[tuple[str, int]] = set()
    validate_handedness(problems)
    for assembly in assemblies:
        key = assembly.kind, assembly.variant
        if key in keys:
            problems.append(f"duplicate assembly {key}")
        keys.add(key)
        expected_roles = EXPECTED_ROLES.get(key)
        actual_roles = tuple(part.part_role for part in assembly.parts)
        if actual_roles != expected_roles:
            problems.append(
                f"{key} roles are {actual_roles}, expected {expected_roles}")
        if assembly.scale_mode != SCALE_MODE:
            problems.append(f"{key} has scale mode {assembly.scale_mode}")
        if assembly.placement_contract not in {
                PLACEMENT_CONTRACT, "anchor_forward_frame"}:
            problems.append(
                f"{key} has placement contract {assembly.placement_contract}")
        if assembly.coordinate_profile not in {
                "legacy_recipe_reflected_x", "root_local_direct"}:
            problems.append(
                f"{key} has coordinate profile {assembly.coordinate_profile}")
        if assembly.coordinate_profile == "root_local_direct" and not \
                assembly.root_derivation:
            problems.append(f"{key} has no root derivation")
        expected_suffixes = EXPECTED_MESH_SUFFIXES.get(key)
        expected_names = tuple(
            mesh_id(assembly.kind, assembly.variant, suffix)
            for suffix in (expected_suffixes or ()))
        actual_names = tuple(part.mesh for part in assembly.parts)
        if actual_names != expected_names:
            problems.append(
                f"{key} meshes are {actual_names}, expected {expected_names}")
        for part in assembly.parts:
            if part.mesh in names:
                problems.append(f"duplicate mesh name {part.mesh}")
            names.add(part.mesh)
            if part.placement_mode not in {
                    "recipe_local_rigid", "unity_per_support"}:
                problems.append(
                    f"{part.mesh} has placement mode {part.placement_mode}")
            if part.placement_mode == "unity_per_support":
                part_low, part_high = geometry_bounds(part.geometry)
                if abs(part_low[0] + part_high[0]) > BOUNDS_EPSILON or \
                        abs(part_low[1] + part_high[1]) > BOUNDS_EPSILON or \
                        abs(part_low[2]) > BOUNDS_EPSILON:
                    problems.append(
                        f"{part.mesh} support archetype is not ground-centred")
                if part.normalized_height_m is not None and abs(
                        (part_high[2] - part_low[2]) -
                        part.normalized_height_m) > BOUNDS_EPSILON:
                    problems.append(
                        f"{part.mesh} normalized height does not match bounds")
            elif part.normalized_height_m is not None:
                problems.append(
                    f"{part.mesh} rigid mesh declares normalized height")
            expected_ground = assembly.expected_source_min_z
            allow_below_anchor = expected_ground is not None and \
                expected_ground < -BOUNDS_EPSILON
            validate_geometry(part, problems, allow_below_anchor)

        reference_names = [name for name, _ in assembly.canonical_reference]
        if len(reference_names) != len(set(reference_names)):
            problems.append(f"{key} has duplicate canonical references")
        for parameter in assembly.scale_parameters:
            if parameter.name not in reference_names:
                problems.append(
                    f"{key} scale parameter {parameter.name} has no reference")
            if not parameter.source_axes or any(
                    axis not in {"X", "Y", "Z"}
                    for axis in parameter.source_axes):
                problems.append(
                    f"{key} scale parameter {parameter.name} has bad axes")
            if not (0.0 <= parameter.minimum <= parameter.reference <=
                    parameter.maximum):
                problems.append(
                    f"{key} scale parameter {parameter.name} has bad range")

        low, high = combined_bounds(assembly.parts)
        expected_ground = assembly.expected_source_min_z
        if expected_ground is None:
            expected_ground = 1.67 if assembly.kind == \
                "ResidentialBalconies" else 0.0
        if abs(low[2] - expected_ground) > BOUNDS_EPSILON:
            problems.append(
                f"{key} ground is source Z={low[2]:.6f}, "
                f"expected {expected_ground:.2f}")
        if assembly.coordinate_profile == "legacy_recipe_reflected_x":
            if not (low[0] <= 0.0 <= high[0]):
                problems.append(f"{key} does not straddle its ground origin")
            if assembly.kind not in FORWARD_ANCHORED_KINDS and not (
                    low[1] <= 0.0 <= high[1]):
                problems.append(f"{key} does not straddle its forward origin")
        special_building = assembly.kind in {
            "BarBuildingShell",
            "SupermarketBuildingShell",
            "PlayerHomeBuildingShell",
        }
        height_budget = 12.0 if special_building else 10.0
        if high[2] > height_budget + BOUNDS_EPSILON:
            problems.append(
                f"{key} exceeds the {height_budget:g} m catalog height "
                "budget")

    for kind, count in EXPECTED_VARIANTS.items():
        actual = sorted(variant for item_kind, variant in keys
                        if item_kind == kind)
        if actual != list(range(count)):
            problems.append(
                f"{kind} variants are {actual}, expected 0..{count - 1}")
    if len(assemblies) != 97:
        problems.append(
            f"assembly count is {len(assemblies)}, expected 97")
    if len(names) != 192:
        problems.append(f"mesh count is {len(names)}, expected 192")
    validate_mirror_bounds(assemblies, "IndustrialCargo", problems)
    validate_mirror_bounds(
        assemblies, "RoadsideRoadworkAndBicycle", problems)
    for kind in (
        "OldTownChimneysAndDormers",
        "ResidentialDiscardedFurniture",
        "IndustrialGantry",
        "NightlifeCinema",
        "FringeFloodGaugeShell",
    ):
        validate_mirror_bounds(assemblies, kind, problems)
    wave1_signature = wave1_compatibility_signature(assemblies)
    if wave1_signature != WAVE1_COMPATIBILITY_SIGNATURE:
        problems.append(
            "wave-one geometry/ID compatibility changed: "
            f"{wave1_signature} != {WAVE1_COMPATIBILITY_SIGNATURE}")
    v2_signature = v2_compatibility_signature(assemblies)
    if v2_signature != V2_COMPATIBILITY_SIGNATURE:
        problems.append(
            "v2 catalog compatibility changed: "
            f"{v2_signature} != {V2_COMPATIBILITY_SIGNATURE}")
    total_triangles = sum(
        triangle_count(part.geometry)
        for assembly in assemblies for part in assembly.parts)
    if total_triangles <= 0 or total_triangles > MAX_TRIANGLES:
        problems.append(
            f"triangle count {total_triangles} is outside 1..{MAX_TRIANGLES}")
    if problems:
        raise RuntimeError(
            "City misc art contract violated:\n  - " +
            "\n  - ".join(problems))


def signature_payload(
    assemblies: Sequence[AssemblySpec],
    generator_version: str = GENERATOR_VERSION,
    design_id: str = DESIGN_ID,
) -> dict:
    return {
        "generator_version": generator_version,
        "design_id": design_id,
        "root_contract": {
            "origin": (
                "assembly_ground" if design_id == V2_DESIGN_ID
                else "per_assembly_root_derivation"
            ),
            "scale_mode": SCALE_MODE,
            "source_ground_axis": "Z",
            "source_ground_value": 0.0,
            "unity_ground_axis": "Y",
            "unity_ground_value": 0.0,
            "source_forward_axis": "+Y",
            "unity_forward_axis": "+Z",
            "legacy_recipe_x_to_unity_local_x": -1.0,
            **({
                "coordinate_profiles": {
                    "legacy_recipe_reflected_x": {
                        "source_x_to_unity_local_x": 1.0,
                        "legacy_recipe_x_to_source_x": -1.0,
                    },
                    "root_local_direct": {
                        "source_x_to_unity_local_x": 1.0,
                        "root_local_x_to_source_x": 1.0,
                    },
                },
            } if design_id != V2_DESIGN_ID else {}),
        },
        "assemblies": [
            {
                "kind": assembly.kind,
                "variant": assembly.variant,
                "scale_mode": assembly.scale_mode,
                "placement_contract": assembly.placement_contract,
                "canonical_reference": {
                    name: stable(value)
                    for name, value in assembly.canonical_reference
                },
                "scale_parameters": [
                    {
                        "name": parameter.name,
                        "source_axes": list(parameter.source_axes),
                        "reference": stable(parameter.reference),
                        "min": stable(parameter.minimum),
                        "max": stable(parameter.maximum),
                    }
                    for parameter in assembly.scale_parameters
                ],
                "unity_owned_parts": list(assembly.unity_owned_parts),
                **({"root_derivation": assembly.root_derivation}
                   if assembly.root_derivation else {}),
                **({"coordinate_profile": assembly.coordinate_profile}
                   if assembly.coordinate_profile !=
                   "legacy_recipe_reflected_x" else {}),
                **({"expected_source_min_z": stable(
                    assembly.expected_source_min_z)}
                   if assembly.expected_source_min_z is not None else {}),
                "parts": [
                    {
                        "mesh": part.mesh,
                        "part_role": part.part_role,
                        "surface_kind": part.surface_kind,
                        "tint_role": part.tint_role,
                        "placement_mode": part.placement_mode,
                        "normalized_height_m": (
                            stable(part.normalized_height_m)
                            if part.normalized_height_m is not None else None
                        ),
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


def v2_compatibility_signature(
    assemblies: Sequence[AssemblySpec],
) -> str:
    encoded = json.dumps(
        signature_payload(
            assemblies[:37],
            generator_version=V2_GENERATOR_VERSION,
            design_id=V2_DESIGN_ID,
        ),
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


# ----------------------------------------------------- Blender output --


def reset_scene() -> tuple[bpy.types.Collection, bpy.types.Collection]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    source = bpy.data.collections.new(SOURCE_COLLECTION)
    presentation = bpy.data.collections.new(PRESENTATION_COLLECTION)
    scene.collection.children.link(source)
    scene.collection.children.link(presentation)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 2400
    scene.render.resolution_y = 1500
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("CMM_PreviewWorld")
    world.color = (0.055, 0.062, 0.066)
    scene.world = world
    return source, presentation


def create_material(role: str) -> bpy.types.Material:
    material = bpy.data.materials.new(f"PREVIEW_CMM_{role}")
    color = PREVIEW_COLORS[role]
    material.diffuse_color = color
    material.use_nodes = True
    node = material.node_tree.nodes.get("Principled BSDF")
    if node is not None:
        node.inputs["Base Color"].default_value = color
        metal = role in {"Industrial", "Street", "Fixture"} or \
            "PaintedMetal" in role
        node.inputs["Roughness"].default_value = 0.48 if metal else 0.76
        node.inputs["Metallic"].default_value = 0.28 if metal else 0.0
        if role in {"Neon", "BacklitSign"}:
            emission = node.inputs.get("Emission Color") or \
                node.inputs.get("Emission")
            if emission is not None:
                emission.default_value = color
            strength = node.inputs.get("Emission Strength")
            if strength is not None:
                strength.default_value = 1.8
    return material


def assign_uv(mesh: bpy.types.Mesh, geometry: Geometry) -> None:
    layer = mesh.uv_layers.new(name="UVMap")
    face_uvs: list[list[Vec2]] = []
    all_uvs = geometry_uvs(geometry)
    cursor = 0
    for face in geometry[1]:
        face_uvs.append(all_uvs[cursor:cursor + len(face)])
        cursor += len(face)
    for polygon, values in zip(mesh.polygons, face_uvs):
        if len(polygon.loop_indices) != len(values):
            raise RuntimeError(f"UV loop drift on '{mesh.name}'.")
        for loop_index, uv in zip(polygon.loop_indices, values):
            layer.data[loop_index].uv = uv


def create_part_object(
    part: PartSpec,
    assembly: AssemblySpec,
    root: bpy.types.Object,
    source: bpy.types.Collection,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(part.mesh)
    mesh.from_pydata(part.geometry[0], [], part.geometry[1])
    mesh.update(calc_edges=True)
    assign_uv(mesh, part.geometry)
    obj = bpy.data.objects.new(part.mesh, mesh)
    source.objects.link(obj)
    obj.parent = root
    obj["bp_kind"] = part.kind
    obj["bp_variant"] = part.variant
    obj["bp_part_role"] = part.part_role
    obj["bp_surface_kind"] = part.surface_kind
    obj["bp_tint_role"] = part.tint_role
    obj["bp_scale_mode"] = SCALE_MODE
    obj["bp_placement_contract"] = assembly.placement_contract
    obj["bp_root_derivation"] = assembly.root_derivation
    obj["bp_coordinate_profile"] = assembly.coordinate_profile
    obj["bp_part_placement_mode"] = part.placement_mode
    if part.normalized_height_m is not None:
        obj["bp_normalized_height_m"] = part.normalized_height_m
    return obj


def build_scene(assemblies: tuple[AssemblySpec, ...]) -> BuildResult:
    source, presentation = reset_scene()
    materials = {
        role: create_material(role) for role in sorted(PREVIEW_COLORS)
    }
    root = bpy.data.objects.new(ROOT_NAME, None)
    source.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.35
    root["bp_design_id"] = DESIGN_ID
    root["bp_origin_contract"] = "per_assembly_root_derivation"
    root["bp_scale_mode"] = SCALE_MODE
    root["bp_source_forward_axis"] = "+Y"
    root["bp_legacy_recipe_x_to_unity_local_x"] = -1.0
    objects: dict[str, bpy.types.Object] = {}
    for assembly in assemblies:
        for part in assembly.parts:
            obj = create_part_object(part, assembly, root, source)
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
            "placement_contract": assembly.placement_contract,
            "canonical_reference": {
                name: stable(value)
                for name, value in assembly.canonical_reference
            },
            "scale_parameters": [
                {
                    "name": parameter.name,
                    "source_axes": list(parameter.source_axes),
                    "reference": stable(parameter.reference),
                    "min": stable(parameter.minimum),
                    "max": stable(parameter.maximum),
                }
                for parameter in assembly.scale_parameters
            ],
            "unity_owned_parts": list(assembly.unity_owned_parts),
            "root_derivation": assembly.root_derivation,
            "coordinate_profile": assembly.coordinate_profile,
            "expected_source_min_z": (
                stable(assembly.expected_source_min_z)
                if assembly.expected_source_min_z is not None else None
            ),
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
            "placement_mode": part.placement_mode,
            "normalized_height_m": (
                stable(part.normalized_height_m)
                if part.normalized_height_m is not None else None
            ),
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
        "generator": "tools/build-city-misc-3d-model.py",
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
            "origin": "per_assembly_root_derivation",
            "scale_mode": SCALE_MODE,
            "source_ground_axis": "Z",
            "source_ground_value": 0.0,
            "unity_ground_axis": "Y",
            "unity_ground_value": 0.0,
            "source_forward_axis": "+Y",
            "unity_forward_axis": "+Z",
            "legacy_recipe_x_to_unity_local_x": -1.0,
            "coordinate_profiles": {
                "legacy_recipe_reflected_x": {
                    "source_x_to_unity_local_x": 1.0,
                    "legacy_recipe_x_to_source_x": -1.0,
                },
                "root_local_direct": {
                    "source_x_to_unity_local_x": 1.0,
                    "root_local_x_to_source_x": 1.0,
                },
            },
        },
        "part_placement_contract": {
            "recipe_local_rigid": {
                "geometry_space": "assembly_recipe_local",
                "unity_action": "instantiate_mesh_once_at_assembly_root",
            },
            "unity_per_support": {
                "geometry_space": "support_local_ground_origin",
                "unity_action": "instantiate_mesh_at_each_sampled_support",
                "normalized_vertical_axis": "source_Z_unity_Y",
            },
        },
        "wave1_compatibility_signature": WAVE1_COMPATIBILITY_SIGNATURE,
        "v2_compatibility_signature": V2_COMPATIBILITY_SIGNATURE,
        "colliders": False,
        "lights": False,
        "cameras": False,
        "materials": False,
        "preview_materials_in_presentation_only": True,
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
    ground_material = bpy.data.materials.new("PREVIEW_CMM_Ground")
    ground_material.diffuse_color = (0.075, 0.082, 0.082, 1.0)
    ground_mesh = bpy.data.meshes.new("CMM_PreviewGround_Mesh")
    columns = 10
    rows = math.ceil(len(result.assemblies) / columns)
    spacing_x = 6.4
    spacing_y = 6.2
    ground_geometry = kit.box((
        0.0,
        0.0,
        -0.08,
    ), (
        columns * spacing_x + 2.0,
        rows * spacing_y + 2.0,
        0.16,
    ))
    ground_mesh.from_pydata(ground_geometry[0], [], ground_geometry[1])
    ground = bpy.data.objects.new("CMM_PreviewGround", ground_mesh)
    collection.objects.link(ground)
    ground.data.materials.append(ground_material)

    for index, assembly in enumerate(result.assemblies):
        column = index % columns
        row = index // columns
        low, high = combined_bounds(assembly.parts)
        width = max(high[0] - low[0], high[1] - low[1])
        height = high[2] - low[2]
        scale = min(1.0, 4.5 / max(width, 0.01), 4.7 / max(height, 0.01))
        placement = bpy.data.objects.new(
            f"PREVIEW_{assembly.kind}_{assembly.variant:02d}", None)
        collection.objects.link(placement)
        placement.location = (
            (column - (columns - 1) * 0.5) * spacing_x,
            ((rows - 1) * 0.5 - row) * spacing_y,
            max(0.0, -low[2] * scale),
        )
        placement.scale = (scale, scale, scale)
        for part in assembly.parts:
            source = result.objects[part.mesh]
            duplicate = source.copy()
            duplicate.data = source.data.copy()
            duplicate.data.materials.append(
                result.materials[part.part_role])
            collection.objects.link(duplicate)
            duplicate.parent = placement

    for name, location, energy, color, size in (
        ("Key", (-27.0, -25.0, 38.0), 15000,
         (0.76, 0.83, 0.79), 25.0),
        ("Rim", (30.0, 19.0, 34.0), 10500,
         (0.30, 0.44, 0.55), 23.0),
        ("Warm", (0.0, -12.0, 42.0), 7500,
         (0.95, 0.55, 0.28), 22.0),
    ):
        data = bpy.data.lights.new(f"PREVIEW_CMM_{name}", "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(f"PREVIEW_CMM_{name}", data)
        collection.objects.link(light)
        light.location = location
        light.rotation_euler = (
            Vector((0.0, 0.0, 1.4)) - light.location
        ).to_track_quat("-Z", "Y").to_euler()


def render_preview(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    add_preview_stage(result)
    for obj in result.source.objects:
        obj.hide_render = True
    camera_data = bpy.data.cameras.new("CAM_CityMiscPreview")
    camera = bpy.data.objects.new("CAM_CityMiscPreview", camera_data)
    result.presentation.objects.link(camera)
    camera.location = (34.0, -46.0, 72.0)
    target = Vector((0.0, 0.0, 2.1))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 114.0
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

    rerun = make_assemblies()
    validate_assemblies(rerun)
    if signature_for(rerun) != signature:
        raise RuntimeError("Non-deterministic City misc signature.")

    total_parts = sum(len(assembly.parts) for assembly in assemblies)
    total_triangles = sum(
        triangle_count(part.geometry)
        for assembly in assemblies for part in assembly.parts)
    if config.validate_only:
        print("CITY MISC 3D DIRECT VALIDATION OK")
        print(f"  Assemblies: {len(assemblies)}")
        print(f"  Meshes: {total_parts}")
        print(f"  Triangles: {total_triangles}/{MAX_TRIANGLES}")
        print(f"  Signature: {signature}")
        print(f"  V2 compatibility: {v2_compatibility_signature(assemblies)}")
        print("  Handedness: legacy recipe X -> Unity local -X")
        print("  Determinism: repeated signatures match")
        return 0

    result = build_scene(assemblies)
    if not config.no_preview:
        render_preview(config.preview, result)
    export_fbx(config.fbx, result)
    payload = manifest_for(assemblies, signature)
    write_manifest(config.manifest, payload)
    save_blend(config.blend)
    print("CITY MISC 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Assemblies: {len(assemblies)}")
    print(f"  Meshes: {total_parts}")
    print(f"  Triangles: {total_triangles}/{MAX_TRIANGLES}")
    print(f"  Signature: {signature}")
    print(f"  V2 compatibility: {v2_compatibility_signature(assemblies)}")
    print("  Handedness: legacy recipe X -> Unity local -X")
    print("  Determinism: repeated signatures match")
    print(f"  Blend: {config.blend}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
