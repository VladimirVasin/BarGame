#!/usr/bin/env python3
"""Build the authored Mountain Road cafe and its detail sheets.

The asset translates the diner composition of Edward Hopper's *Nighthawks*
into the already-canonical five-sided terminal footprint.  It does not copy
the painting's city, lettering or branding: the long luminous band, faceted
glass corner, counter rhythm and silent service tableau are the reference.

All dimensions in the build functions are Unity-local metres (+Y up, +Z
forward). ``bar_parts.to_source`` swaps Y/Z and reverses winding for Blender.
The exported FBX is passive: it contains no colliders, lights, cameras,
materials or animation. Unity owns those systems through the model manifest.

Run from the repository root with Blender 5::

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-mountain-road-cafe-3d-model.py -- --validate-only
    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-mountain-road-cafe-3d-model.py
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import sys
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Iterable, Sequence

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import interior_kit as kit  # noqa: E402
import bar_parts as bp  # noqa: E402


GENERATOR_VERSION = "1.2.1"
DESIGN_ID = "mountain_road_cafe_nighthawks_v1"
DISPLAY_NAME = "Bar Promenade Mountain Road Nighthawks Cafe"
EXPECTED_MESH_COUNT = 61
EXPECTED_TRIANGLE_COUNT = 5794
EXPECTED_ANCHOR_COUNT = 52
EXPECTED_PROP_COUNT = 7

# Logical footprint published by MountainRoadTerminalPlanner, translated so
# MountainRoadCafePlan.Center is the model origin. Do not alter these numbers
# without changing the terminal plan and its vehicle/access validators.
FOOTPRINT: tuple[tuple[float, float], ...] = (
    (-5.32, -4.56),
    (1.68, -4.56),
    (4.48, -1.76),
    (4.48, 5.44),
    (-5.32, 5.44),
)
HEIGHT = 4.4
DOOR_CENTER = (-3.52, 0.0, -4.56)
DOOR_WIDTH = 1.6
DOOR_HEIGHT = 2.28
GLASS_SILL = 0.62
GLASS_HEAD = 3.78
FASCIA_HEIGHT = 0.38
LUMINOUS_BAND_HEIGHT = 0.20
LUMINOUS_BAND_LOW = GLASS_HEAD - LUMINOUS_BAND_HEIGHT
WALL_THICKNESS = 0.24
GLASS_THICKNESS = 0.028
OPAQUE_DETAIL_CLEARANCE = 0.003
LIQUID_WALL_CLEARANCE = 0.0015
LIQUID_RIM_CLEARANCE = 0.002

STOOL_Z = -2.18
# The original five stations remain byte-for-byte at their old offsets. Two
# new empty stools turn with the inset counter return instead of lengthening
# the straight row through the lone patron's negative space.
STOOL_STATIONS = (
    (-1.50, STOOL_Z, 0.0, 1.0),
    (-0.38, STOOL_Z, 0.0, 1.0),
    (0.75, STOOL_Z, 0.0, 1.0),
    (1.80, STOOL_Z, 0.0, 1.0),
    (3.00, STOOL_Z, 0.0, 1.0),
    (4.08, -0.62, -0.9894, 0.1455),
    (4.16, 0.10, -0.9894, 0.1455),
)
CUP_STATIONS = {
    "PairMan": (0.75, 1.035, -1.48),
    "PairWoman": (1.80, 1.035, -1.48),
}
STOOL_SEAT_TOP_Y = 0.8175
LIQUID_EMPTY_LOCAL_Y = 0.022
LIQUID_FULL_LOCAL_Y = 0.101

# The hero's menu is authored as a passive dynamic prop. Its root is the
# delivery dock so runtime can reveal and move the whole assembly without
# reconstructing either the opened-page pose or its text plane.
MENU_DOCK = (-0.38, 1.02, -1.38)
MENU_COVER_WIDTH = 0.54
MENU_COVER_DEPTH = 0.32
MENU_COVER_THICKNESS = 0.006
MENU_COVER_GAP = 0.006
MENU_COVER_MIN_Y = 0.004
MENU_PAGE_WIDTH = 0.50
MENU_PAGE_DEPTH = 0.29
MENU_PAGE_THICKNESS = 0.004
MENU_PAGE_GAP = 0.010
MENU_PAGE_MIN_Y = 0.013
MENU_OPEN_ANGLE_DEGREES = 5.5
MENU_TEXT_CLEARANCE = 0.0015
MENU_GRIP_LOCAL = (0.0, 0.042, 0.130)
MENU_TEXT_X = 0.130
MENU_TEXT_POSITIONS = {
    "MenuText.Item.00": (MENU_TEXT_X, 0.035),
    "MenuText.Item.01": (MENU_TEXT_X, -0.020),
    "MenuText.Item.02": (MENU_TEXT_X, -0.075),
    "MenuText.Selection": (0.035, 0.035),
}
MENU_PAGE_FORWARD = (
    math.sin(math.radians(MENU_OPEN_ANGLE_DEGREES)),
    math.cos(math.radians(MENU_OPEN_ANGLE_DEGREES)),
    0.0,
)
MENU_PAGE_UP = (0.0, 0.0, 1.0)
HERO_COUNTER_DETAIL_Z = -0.88
HERO_SERVICE_RAIL = (-0.38, 0.0, -0.76)

# The rear service run finishes 3 mm before the visible inner lining. This is
# enough to avoid z-fighting without reading as a second aisle behind the
# furniture. It stops before the existing rear door on the right.
REAR_LINING_CENTER_Z = 5.29
REAR_LINING_DEPTH = 0.035
KITCHEN_WALL_GAP = 0.003
KITCHEN_CABINET_WALL_GAP = 0.020
REAR_LINING_FRONT_Z = REAR_LINING_CENTER_Z - REAR_LINING_DEPTH * 0.5
KITCHEN_BACK_Z = REAR_LINING_FRONT_Z - KITCHEN_WALL_GAP
SERVICE_CABINET_SIZE = (5.68, 0.86, 0.78)
SERVICE_CABINET_CENTER = (
    0.19,
    0.43,
    REAR_LINING_FRONT_Z - KITCHEN_CABINET_WALL_GAP -
    SERVICE_CABINET_SIZE[2] * 0.5,
)
SERVICE_WORKTOP_SIZE = (5.82, 0.10, 0.90)
SERVICE_WORKTOP_CENTER = (
    0.19, 0.90, KITCHEN_BACK_Z - SERVICE_WORKTOP_SIZE[2] * 0.5)
SERVICE_CABINET_FRONT_Z = (
    SERVICE_CABINET_CENTER[2] - SERVICE_CABINET_SIZE[2] * 0.5)
WORKTOP_TOP_Y = 0.95

STOVE_CENTER = (-1.90, SERVICE_WORKTOP_CENTER[2] - 0.02)
KITCHEN_EQUIPMENT_Z_SHIFT = STOVE_CENTER[1] - 3.88
STOVE_SIZE = (1.20, 0.66)
STOVE_PLATE_LOW_Y = WORKTOP_TOP_Y + OPAQUE_DETAIL_CLEARANCE
STOVE_PLATE_HIGH_Y = STOVE_PLATE_LOW_Y + 0.040
STOVE_BURNER_TOP_Y = STOVE_PLATE_HIGH_Y + 0.017
PAN_CENTER = (-2.18, STOVE_CENTER[1])
PAN_BOTTOM_Y = STOVE_BURNER_TOP_Y
CUTTING_BOARD_CENTER = (-0.38, STOVE_CENTER[1])
CUTTING_BOARD_SIZE = (0.72, 0.48)
CUTTING_BOARD_LOW_Y = WORKTOP_TOP_Y + OPAQUE_DETAIL_CLEARANCE
CUTTING_BOARD_HIGH_Y = CUTTING_BOARD_LOW_Y + 0.036

FRIDGE_SIZE = (1.12, 1.96, 0.72)
FRIDGE_CENTER = (
    -3.82, 0.98, KITCHEN_BACK_Z - FRIDGE_SIZE[2] * 0.5)
FRIDGE_Z_SHIFT = FRIDGE_CENTER[2] - 4.72
FRIDGE_DOOR_WIDTH = 1.0
FRIDGE_DOOR_HEIGHT = 1.86
FRIDGE_DOOR_DEPTH = 0.08
FRIDGE_DOOR_PIVOT = (
    -4.32,
    0.0,
    FRIDGE_CENTER[2] - FRIDGE_SIZE[2] * 0.5 - FRIDGE_DOOR_DEPTH * 0.5,
)
FRIDGE_GRIP_LOCAL = (0.88, 1.08, -0.13)

STOVE_TASK_FIXTURE_CENTER = (STOVE_CENTER[0], 2.35, STOVE_CENTER[1])
STOVE_TASK_LENS_CENTER = (STOVE_CENTER[0], 2.23, STOVE_CENTER[1])

DEFAULT_BLEND = (
    ROOT / "ArtSource" / "MountainRoad" / "Cafe" / "Blender" /
    "MountainRoadCafe3D.blend"
)
DEFAULT_FBX = (
    ROOT / "Assets" / "MountainRoad" / "Cafe" / "Models" /
    "MountainRoadCafe3D.fbx"
)
DEFAULT_MANIFEST = (
    ROOT / "Assets" / "MountainRoad" / "Cafe" / "Models" /
    "MountainRoadCafe3D.json"
)
DEFAULT_PREVIEW = (
    ROOT / "ArtSource" / "MountainRoad" / "Cafe" / "Preview" /
    "MountainRoadCafe3D.png"
)
TEXTURE_DIR = (
    ROOT / "Assets" / "Resources" / "MountainRoad" / "Cafe" / "Textures"
)

TEXTURE_FILES = {
    "CafeExteriorDetail": "MountainRoadCafeExteriorDetail.png",
    "CafeInteriorDetail": "MountainRoadCafeInteriorDetail.png",
    "CafeCounterDetail": "MountainRoadCafeCounterDetail.png",
    "CafeMetalDetail": "MountainRoadCafeMetalDetail.png",
    "CafePropsDetail": "MountainRoadCafePropsDetail.png",
    "CafeGlassDetail": "MountainRoadCafeGlassDetail.png",
}

SHEET_PITCH = {
    "CafeExteriorDetail": 2.4,
    "CafeInteriorDetail": 2.6,
    "CafeCounterDetail": 1.4,
    "CafeMetalDetail": 1.2,
    "CafePropsDetail": 0.45,
    "CafeGlassDetail": 2.0,
    "CafeWarmEmission": 1.0,
    "CafeCoffee": 0.25,
}

BASE_SURFACE = {
    "CafeExteriorDetail": "WallPaint",
    "CafeInteriorDetail": "InteriorPaint",
    "CafeCounterDetail": "Timber",
    "CafeMetalDetail": "PaleEnamel",
    "CafePropsDetail": "PaleEnamel",
    "CafeGlassDetail": "PaintedMetal",
    "CafeWarmEmission": "InteriorPaint",
    "CafeCoffee": "Timber",
}

PREVIEW_COLORS = {
    "CafeExteriorDetail": (0.035, 0.090, 0.078, 1.0),
    "CafeInteriorDetail": (0.72, 0.63, 0.39, 1.0),
    "CafeCounterDetail": (0.34, 0.095, 0.035, 1.0),
    "CafeMetalDetail": (0.48, 0.50, 0.43, 1.0),
    "CafePropsDetail": (0.72, 0.70, 0.58, 1.0),
    "CafeGlassDetail": (0.19, 0.43, 0.38, 0.30),
    "CafeWarmEmission": (1.0, 0.66, 0.24, 1.0),
    "CafeCoffee": (0.105, 0.045, 0.018, 1.0),
}

ALLOWED_SHEETS = frozenset(SHEET_PITCH)
APPLIANCE_ENAMEL_ROLES = frozenset({
    "stove",
    "frying_pan",
    "refrigerator_body",
    "refrigerator_cavity",
    "refrigerator_shelf",
    "fridge_door",
})
APPLIANCE_SURFACE_SHEETS = {
    "stove": "CafeMetalDetail",
    "frying_pan": "CafeMetalDetail",
    "refrigerator_body": "CafePropsDetail",
    "refrigerator_cavity": "CafePropsDetail",
    "refrigerator_shelf": "CafePropsDetail",
    "fridge_door": "CafePropsDetail",
}


@dataclass
class Part:
    obj: "bpy.types.Object"
    name: str
    role: str
    group: str
    sheet: str
    emissive: bool
    casts_shadows: bool
    local_geometry: kit.Geometry
    unity_origin: tuple[float, float, float]
    initially_visible: bool = True


@dataclass
class Anchor:
    obj: "bpy.types.Object"
    name: str
    role: str
    unity_position: tuple[float, float, float]
    unity_forward: tuple[float, float, float]
    unity_up: tuple[float, float, float]


@dataclass
class Prop:
    root: "bpy.types.Object"
    name: str
    role: str
    owner: str
    part_names: list[str] = field(default_factory=list)
    lift_root: "bpy.types.Object | None" = None
    liquid_part: str = ""
    empty_local_y: float = 0.0
    full_local_y: float = 0.0


@dataclass
class AssetBuild:
    root: "bpy.types.Object"
    collection: "bpy.types.Collection"
    materials: dict[str, "bpy.types.Material"]
    parts: list[Part] = field(default_factory=list)
    anchors: dict[str, Anchor] = field(default_factory=dict)
    props: dict[str, Prop] = field(default_factory=dict)


def stable(value: float) -> float:
    return round(float(value), 6)


def unity_to_source(point: Sequence[float]) -> tuple[float, float, float]:
    return float(point[0]), float(point[2]), float(point[1])


def translate_geometry(
    geometry: kit.Geometry,
    offset: Sequence[float],
) -> kit.Geometry:
    return kit.translated(geometry, offset)


def polygon_slab(
    footprint: Sequence[tuple[float, float]],
    low_y: float,
    high_y: float,
) -> kit.Geometry:
    """Closed Unity-space polygon slab with outward winding."""
    count = len(footprint)
    vertices = [(x, low_y, z) for x, z in footprint]
    vertices.extend((x, high_y, z) for x, z in footprint)
    # A CCW polygon in XZ faces -Y in Unity's left-handed coordinates.
    # It is therefore the bottom cap as-authored and must be reversed for
    # the top cap.
    faces: list[tuple[int, ...]] = [tuple(range(count))]
    faces.append(tuple(reversed(range(count, count * 2))))
    for index in range(count):
        following = (index + 1) % count
        faces.append((
            following,
            index,
            count + index,
            count + following,
        ))
    return vertices, faces


def segment_box(
    start: Sequence[float],
    end: Sequence[float],
    center_y: float,
    height: float,
    depth: float,
    chamfer: float = 0.008,
) -> kit.Geometry:
    """A Unity-space box whose long X axis follows an XZ segment."""
    dx = end[0] - start[0]
    dz = end[1] - start[1]
    length = math.hypot(dx, dz)
    geometry = bp.u_box((0.0, center_y, 0.0), (length, height, depth), chamfer)
    yaw = math.degrees(math.atan2(-dz, dx))
    geometry = bp.u_rotated(geometry, (0.0, yaw, 0.0))
    return translate_geometry(
        geometry,
        ((start[0] + end[0]) * 0.5, 0.0, (start[1] + end[1]) * 0.5),
    )


def polyline_strip(
    points: Sequence[tuple[float, float]],
    width: float,
    low_y: float,
    high_y: float,
) -> kit.Geometry:
    """One watertight, mitred wall/rail strip around an open XZ polyline."""
    if len(points) < 2:
        raise ValueError("A strip needs at least two points.")
    directions: list[tuple[float, float]] = []
    normals: list[tuple[float, float]] = []
    for first, second in zip(points, points[1:]):
        dx = second[0] - first[0]
        dz = second[1] - first[1]
        length = math.hypot(dx, dz)
        if length <= 1e-6:
            raise ValueError("A strip cannot contain a zero-length leg.")
        direction = (dx / length, dz / length)
        directions.append(direction)
        normals.append((-direction[1], direction[0]))

    half = width * 0.5
    offsets: list[tuple[float, float]] = []
    for index in range(len(points)):
        if index == 0:
            offsets.append((normals[0][0] * half, normals[0][1] * half))
            continue
        if index == len(points) - 1:
            offsets.append((normals[-1][0] * half, normals[-1][1] * half))
            continue
        nx = normals[index - 1][0] + normals[index][0]
        nz = normals[index - 1][1] + normals[index][1]
        length = math.hypot(nx, nz)
        if length <= 1e-6:
            offsets.append((normals[index][0] * half, normals[index][1] * half))
            continue
        nx /= length
        nz /= length
        denominator = nx * normals[index][0] + nz * normals[index][1]
        scale = half / max(0.25, abs(denominator))
        offsets.append((nx * scale, nz * scale))

    left = [(point[0] + offset[0], point[1] + offset[1])
            for point, offset in zip(points, offsets)]
    right = [(point[0] - offset[0], point[1] - offset[1])
             for point, offset in zip(points, offsets)]
    footprint = left + list(reversed(right))
    area_twice = sum(
        footprint[index][0] * footprint[(index + 1) % len(footprint)][1] -
        footprint[(index + 1) % len(footprint)][0] * footprint[index][1]
        for index in range(len(footprint))
    )
    if area_twice < 0.0:
        footprint.reverse()
    return polygon_slab(footprint, low_y, high_y)


def merge(items: Iterable[kit.Geometry]) -> kit.Geometry:
    return kit.merge_all(items)


def ring_tube(
    center: Sequence[float],
    major_radius: float,
    minor_radius: float,
    start_degrees: float,
    end_degrees: float,
    arc_segments: int = 10,
    tube_segments: int = 6,
) -> kit.Geometry:
    """Closed tube arc in Unity XY, used for cup and pot handles."""
    cx, cy, cz = center
    vertices: list[tuple[float, float, float]] = []
    rings: list[list[int]] = []
    for arc_index in range(arc_segments + 1):
        t = arc_index / arc_segments
        angle = math.radians(start_degrees + (end_degrees - start_degrees) * t)
        ring: list[int] = []
        radial = (math.cos(angle), math.sin(angle), 0.0)
        tangent_z = (0.0, 0.0, 1.0)
        for tube_index in range(tube_segments):
            tube_angle = math.tau * tube_index / tube_segments
            offset_radial = math.cos(tube_angle) * minor_radius
            offset_z = math.sin(tube_angle) * minor_radius
            ring.append(len(vertices))
            vertices.append((
                cx + math.cos(angle) * major_radius + radial[0] * offset_radial,
                cy + math.sin(angle) * major_radius + radial[1] * offset_radial,
                cz + tangent_z[2] * offset_z,
            ))
        rings.append(ring)
    faces: list[tuple[int, ...]] = []
    for lower, upper in zip(rings, rings[1:]):
        for index in range(tube_segments):
            following = (index + 1) % tube_segments
            faces.append((
                lower[index], lower[following],
                upper[following], upper[index],
            ))
    faces.append(tuple(reversed(rings[0])))
    faces.append(tuple(rings[-1]))
    return vertices, faces


def hollow_cup(
    height: float = 0.118,
    bottom_radius: float = 0.055,
    top_radius: float = 0.067,
    thickness: float = 0.008,
    sides: int = 12,
) -> kit.Geometry:
    """Hollow, watertight cup centred on its table dock origin."""
    bottom = 0.0
    top = height
    inner_bottom = 0.018
    outer_low: list[int] = []
    outer_high: list[int] = []
    inner_low: list[int] = []
    inner_high: list[int] = []
    vertices: list[tuple[float, float, float]] = []
    for radius, y, row in (
        (bottom_radius, bottom, outer_low),
        (top_radius, top, outer_high),
        (bottom_radius - thickness, inner_bottom, inner_low),
        (top_radius - thickness, top, inner_high),
    ):
        for index in range(sides):
            angle = math.tau * index / sides
            row.append(len(vertices))
            vertices.append((
                math.cos(angle) * radius,
                y,
                math.sin(angle) * radius,
            ))
    faces: list[tuple[int, ...]] = []
    for index in range(sides):
        following = (index + 1) % sides
        faces.append((
            outer_low[following], outer_low[index],
            outer_high[index], outer_high[following],
        ))
        faces.append((
            outer_high[following], outer_high[index],
            inner_high[index], inner_high[following],
        ))
        faces.append((
            inner_high[following], inner_high[index],
            inner_low[index], inner_low[following],
        ))
        faces.append((
            outer_low[index], outer_low[following],
            inner_low[following], inner_low[index],
        ))
    faces.append(tuple(reversed(inner_low)))
    return vertices, faces


def cup_geometry() -> kit.Geometry:
    handle = ring_tube(
        (0.061, 0.062, 0.0), 0.037, 0.007, -72.0, 72.0, 9, 6)
    return kit.merge(hollow_cup(), handle)


def saucer_geometry() -> kit.Geometry:
    lower = bp.u_cylinder((0.0, 0.005, 0.0), (0.162, 0.005, 0.162), 16)
    rim = bp.u_cylinder((0.0, 0.011, 0.0), (0.132, 0.004, 0.132), 16)
    return kit.merge(lower, rim)


def liquid_geometry() -> kit.Geometry:
    return bp.u_cylinder(
        (0.0, 0.0, 0.0),
        ((0.067 - 0.008 - LIQUID_WALL_CLEARANCE) * 2.0,
         0.004,
         (0.067 - 0.008 - LIQUID_WALL_CLEARANCE) * 2.0),
        16,
    )


def png_chunk(kind: bytes, data: bytes) -> bytes:
    checksum = zlib.crc32(kind)
    checksum = zlib.crc32(data, checksum) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", checksum)


def encode_png_rgba(width: int, height: int, pixels: bytes) -> bytes:
    if len(pixels) != width * height * 4:
        raise ValueError("RGBA pixel payload has the wrong size.")
    rows = bytearray()
    stride = width * 4
    for row in range(height):
        rows.append(0)  # deterministic None filter
        start = row * stride
        rows.extend(pixels[start:start + stride])
    return (
        b"\x89PNG\r\n\x1a\n" +
        png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
        png_chunk(b"IDAT", zlib.compress(bytes(rows), level=9)) +
        png_chunk(b"IEND", b"")
    )


def pixel_hash(x: int, y: int, salt: int) -> int:
    value = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    value = ((value ^ (value >> 13)) * 1274126177) & 0xFFFFFFFF
    return value ^ (value >> 16)


def clamp_byte(value: float) -> int:
    return max(0, min(255, int(round(value))))


def texture_pixel(kind: str, x: int, y: int, size: int) -> tuple[int, int, int, int]:
    u = x / max(1, size - 1)
    v = y / max(1, size - 1)
    noise = (pixel_hash(x, y, len(kind) * 97) & 255) / 255.0 - 0.5
    if kind == "CafeExteriorDetail":
        seam = min(y % 96, 95 - (y % 96)) < 2
        rivet = ((x - 24) % 128) ** 2 + ((y - 24) % 96) ** 2 < 9
        grime = 18.0 * (1.0 - v) + noise * 9.0
        base = (26 - grime, 67 - grime * 0.35, 58 - grime * 0.30)
        if seam:
            base = tuple(channel * 0.58 for channel in base)
        if rivet:
            base = (75, 83, 70)
        return *(clamp_byte(channel) for channel in base), 255
    if kind == "CafeInteriorDetail":
        tile_x = x % 128
        tile_y = y % 128
        joint = min(tile_x, 127 - tile_x, tile_y, 127 - tile_y) < 2
        stain = max(0.0, 1.0 - math.hypot(u - 0.72, v - 0.26) * 4.5)
        base = (190 + noise * 8 - stain * 24,
                166 + noise * 7 - stain * 18,
                101 + noise * 6 - stain * 7)
        if joint:
            base = tuple(channel * 0.78 for channel in base)
        return *(clamp_byte(channel) for channel in base), 255
    if kind == "CafeCounterDetail":
        grain = math.sin(y * 0.16 + math.sin(x * 0.031) * 2.2) * 12.0
        ring_a = abs(math.hypot(x - 120, y - 182) - 28) < 1.6
        ring_b = abs(math.hypot(x - 360, y - 332) - 22) < 1.4
        base = (93 + grain + noise * 8, 34 + grain * 0.28, 16 + grain * 0.12)
        if ring_a or ring_b:
            base = (145, 78, 39)
        return *(clamp_byte(channel) for channel in base), 255
    if kind == "CafeMetalDetail":
        brushed = math.sin(y * 0.42) * 4.0 + noise * 7.0
        panel = min(x % 128, 127 - x % 128, y % 128, 127 - y % 128) < 2
        screw = ((x - 14) % 128) ** 2 + ((y - 14) % 128) ** 2 < 7
        base = (126 + brushed, 132 + brushed, 119 + brushed * 0.8)
        if panel:
            base = tuple(channel * 0.66 for channel in base)
        if screw:
            base = (54, 61, 57)
        return *(clamp_byte(channel) for channel in base), 255
    if kind == "CafePropsDetail":
        band = 0.37 < v < 0.44
        fleck = (pixel_hash(x // 2, y // 2, 431) & 1023) < 9
        base = (190 + noise * 6, 184 + noise * 6, 148 + noise * 5)
        if band:
            base = (47, 94, 75)
        if fleck:
            base = (129, 122, 95)
        return *(clamp_byte(channel) for channel in base), 255
    if kind == "CafeGlassDetail":
        wipe = abs(math.sin(x * 0.021 + math.sin(y * 0.009))) > 0.96
        condensation = (pixel_hash(x // 3, y // 3, 997) & 255) < 9 and v < 0.56
        edge = max(0.0, (0.12 - min(u, 1.0 - u, v, 1.0 - v)) * 5.0)
        alpha = 48 + edge * 50 + (30 if wipe else 0) + (58 if condensation else 0)
        return 70, 125, 113, clamp_byte(alpha)
    raise ValueError(f"Unknown texture kind {kind!r}.")


def texture_payload(kind: str, size: int = 512) -> bytes:
    pixels = bytearray()
    for y in range(size):
        for x in range(size):
            pixels.extend(texture_pixel(kind, x, y, size))
    return encode_png_rgba(size, size, bytes(pixels))


def generate_textures(write: bool) -> list[dict]:
    records: list[dict] = []
    for kind, filename in TEXTURE_FILES.items():
        payload = texture_payload(kind)
        path = TEXTURE_DIR / filename
        if write:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(payload)
        records.append({
            "sheet": kind,
            "file": f"Assets/Resources/MountainRoad/Cafe/Textures/{filename}",
            "resource_path": f"MountainRoad/Cafe/Textures/{Path(filename).stem}",
            "width": 512,
            "height": 512,
            "wrap": "Clamp" if kind in {"CafePropsDetail", "CafeGlassDetail"} else "Repeat",
            "sha256": hashlib.sha256(payload).hexdigest(),
            "base_surface": BASE_SURFACE[kind],
        })
    return records


def create_material(sheet: str) -> "bpy.types.Material":
    material = bpy.data.materials.new(f"PREVIEW_MRC_{sheet}")
    color = PREVIEW_COLORS[sheet]
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = (
            0.12 if sheet == "CafeGlassDetail" else
            0.25 if sheet == "CafeMetalDetail" else 0.68
        )
        bsdf.inputs["Metallic"].default_value = 0.32 if sheet == "CafeMetalDetail" else 0.0
        if "Alpha" in bsdf.inputs:
            bsdf.inputs["Alpha"].default_value = color[3]
        if sheet == "CafeWarmEmission":
            emission = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
            strength = bsdf.inputs.get("Emission Strength")
            if emission is not None:
                emission.default_value = color
            if strength is not None:
                strength.default_value = 3.0
    material.surface_render_method = "DITHERED" if sheet == "CafeGlassDetail" else "DITHERED"
    return material


def uv_transform_for(name: str) -> tuple[int, float, float]:
    """Stable semantic variation without material or process-random state."""
    digest = hashlib.sha256(name.encode("utf-8")).digest()
    quarter_turns = digest[0] & 3
    offset_u = ((digest[1] << 8) | digest[2]) / 65535.0 * 7.0
    offset_v = ((digest[3] << 8) | digest[4]) / 65535.0 * 7.0
    return quarter_turns, offset_u, offset_v


def uses_appliance_panel_patch(part: Part) -> bool:
    return part.sheet == APPLIANCE_SURFACE_SHEETS.get(part.role)


def appliance_panel_region(part: Part) -> tuple[float, float, float, float]:
    """Choose a clean inset patch without sampling a band, grid or screw."""
    if part.sheet == "CafePropsDetail":
        return 0.05, 0.05, 0.90, 0.25
    digest = hashlib.sha256(
        ("appliance:" + part.name).encode("utf-8")
    ).digest()
    cell_u = (digest[0] & 3) * 0.25
    cell_v = (digest[1] & 3) * 0.25
    return cell_u + 0.055, cell_v + 0.055, 0.175, 0.175


def assign_uv(mesh: "bpy.types.Mesh", part: Part) -> None:
    layer = mesh.uv_layers.new(name="UVMap")
    low, high = kit.bounds(part.local_geometry)
    pitch = SHEET_PITCH[part.sheet]
    tiled = part.sheet not in {"CafePropsDetail", "CafeGlassDetail", "CafeWarmEmission", "CafeCoffee"}
    appliance_patch = uses_appliance_panel_patch(part)
    quarter_turns, offset_u, offset_v = uv_transform_for(part.name)
    appliance_u, appliance_v, appliance_span_u, appliance_span_v = (
        appliance_panel_region(part)
    )
    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        for loop_index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if appliance_patch:
                spans = (
                    max(1e-5, high[0] - low[0]),
                    max(1e-5, high[1] - low[1]),
                    max(1e-5, high[2] - low[2]),
                )
                if axis == 0:
                    normalized = (
                        (point.y - low[1]) / spans[1],
                        (point.z - low[2]) / spans[2],
                    )
                elif axis == 1:
                    normalized = (
                        (point.x - low[0]) / spans[0],
                        (point.z - low[2]) / spans[2],
                    )
                else:
                    normalized = (
                        (point.x - low[0]) / spans[0],
                        (point.y - low[1]) / spans[1],
                    )
                uv = (
                    appliance_u + normalized[0] * appliance_span_u,
                    appliance_v + normalized[1] * appliance_span_v,
                )
            elif tiled:
                if axis == 0:
                    uv = (point.y / pitch, point.z / pitch)
                elif axis == 1:
                    uv = (point.x / pitch, point.z / pitch)
                else:
                    uv = (point.x / pitch, point.y / pitch)
                for _ in range(quarter_turns):
                    uv = (-uv[1], uv[0])
                uv = (uv[0] + offset_u, uv[1] + offset_v)
            else:
                spans = (
                    max(1e-5, high[0] - low[0]),
                    max(1e-5, high[1] - low[1]),
                    max(1e-5, high[2] - low[2]),
                )
                if axis == 0:
                    uv = ((point.y - low[1]) / spans[1], (point.z - low[2]) / spans[2])
                elif axis == 1:
                    uv = ((point.x - low[0]) / spans[0], (point.z - low[2]) / spans[2])
                else:
                    uv = ((point.x - low[0]) / spans[0], (point.y - low[1]) / spans[1])
                uv = (0.02 + uv[0] * 0.96, 0.02 + uv[1] * 0.96)
            layer.data[loop_index].uv = uv


def add_empty(
    asset: AssetBuild,
    name: str,
    unity_position: Sequence[float],
    parent: "bpy.types.Object | None" = None,
) -> "bpy.types.Object":
    obj = bpy.data.objects.new(name, None)
    asset.collection.objects.link(obj)
    obj.parent = parent or asset.root
    obj.location = unity_to_source(unity_position)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.18
    return obj


def add_part(
    asset: AssetBuild,
    name: str,
    geometry: kit.Geometry,
    role: str,
    sheet: str,
    *,
    group: str = "static",
    emissive: bool = False,
    casts_shadows: bool = True,
    unity_origin: Sequence[float] = (0.0, 0.0, 0.0),
    parent: "bpy.types.Object | None" = None,
    initially_visible: bool = True,
) -> "bpy.types.Object":
    if sheet not in ALLOWED_SHEETS:
        raise SystemExit(f"Part '{name}' names unsupported sheet '{sheet}'.")
    source_geometry = bp.to_source(geometry)
    if not source_geometry[0] or not source_geometry[1]:
        raise SystemExit(f"Part '{name}' is empty.")
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(source_geometry[0], [], source_geometry[1])
    mesh.materials.append(asset.materials[sheet])
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = parent or asset.root
    obj.location = unity_to_source(unity_origin)
    obj["bp_role"] = role
    obj["bp_group"] = group
    obj["bp_sheet"] = sheet
    obj["bp_emissive"] = emissive
    obj["bp_casts_shadows"] = casts_shadows
    obj["bp_initially_visible"] = initially_visible
    part = Part(
        obj,
        name,
        role,
        group,
        sheet,
        emissive,
        casts_shadows,
        source_geometry,
        tuple(float(value) for value in unity_origin),
        initially_visible,
    )
    asset.parts.append(part)
    assign_uv(mesh, part)
    return obj


def add_anchor(
    asset: AssetBuild,
    name: str,
    role: str,
    unity_position: Sequence[float],
    unity_forward: Sequence[float] = (0.0, 0.0, 1.0),
    unity_up: Sequence[float] = (0.0, 1.0, 0.0),
    parent: "bpy.types.Object | None" = None,
) -> None:
    obj = add_empty(asset, f"ANCHOR_{name}", unity_position, parent)
    obj.empty_display_type = "ARROWS"
    obj.empty_display_size = 0.28
    obj["bp_role"] = role
    obj["bp_unity_forward"] = list(unity_forward)
    obj["bp_unity_up"] = list(unity_up)
    asset.anchors[name] = Anchor(
        obj,
        name,
        role,
        tuple(float(value) for value in unity_position),
        tuple(float(value) for value in unity_forward),
        tuple(float(value) for value in unity_up),
    )


def add_prop(
    asset: AssetBuild,
    name: str,
    role: str,
    owner: str,
    unity_position: Sequence[float],
) -> Prop:
    root = add_empty(asset, f"PROP_{name}", unity_position)
    root["bp_role"] = role
    root["bp_owner"] = owner
    prop = Prop(root, name, role, owner)
    asset.props[name] = prop
    return prop


def build_shell(asset: AssetBuild) -> None:
    inset_floor = tuple((x * 0.985, z * 0.985) for x, z in FOOTPRINT)
    add_part(
        asset,
        "Cafe_Floor",
        polygon_slab(inset_floor, 0.018, 0.075),
        "floor",
        "CafeInteriorDetail",
        group="shell",
    )
    add_part(
        asset,
        "Cafe_Roof",
        polygon_slab(FOOTPRINT, 4.18, HEIGHT),
        "roof",
        "CafeExteriorDetail",
        group="shell",
    )

    # Blind west and rear service walls.
    opaque = [
        polyline_strip(
            (FOOTPRINT[0], FOOTPRINT[4], FOOTPRINT[3]),
            WALL_THICKNESS, 0.02, 4.18),
        segment_box((-5.32, -4.56), (-4.32, -4.56), 2.10, 4.16, WALL_THICKNESS),
    ]
    add_part(asset, "Cafe_OpaqueWalls", merge(opaque), "opaque_wall",
             "CafeExteriorDetail", group="shell")

    # The entrance remains a full 1.6 m opening below the frame, while a
    # proper opaque lintel closes the facade from the frame head to the deep
    # fascia. Without this volume the preview exposed the interior as one
    # unintended storey-high notch above the open door.
    door_frame_top = DOOR_HEIGHT + 0.09
    door_header = segment_box(
        (-4.32, -4.56),
        (-2.72, -4.56),
        (door_frame_top + GLASS_HEAD) * 0.5,
        GLASS_HEAD - door_frame_top,
        WALL_THICKNESS,
        0.006,
    )
    add_part(
        asset,
        "Cafe_DoorHeaderWall",
        door_header,
        "door_header_wall",
        "CafeExteriorDetail",
        group="shell",
    )

    # Continuous low plinth and deep fascia define the Hopper silhouette.
    front_segments = [
        ((-5.32, -4.56), (-4.32, -4.56)),
        ((-2.72, -4.56), (1.68, -4.56)),
        ((1.68, -4.56), (4.48, -1.76)),
        ((4.48, -1.76), (4.48, 5.44)),
    ]
    frontage_chain = (
        front_segments[1][0], front_segments[1][1],
        front_segments[2][1], front_segments[3][1],
    )
    plinth = [
        segment_box(front_segments[0][0], front_segments[0][1],
                    GLASS_SILL * 0.5, GLASS_SILL, 0.28),
        polyline_strip(frontage_chain, 0.28, 0.0, GLASS_SILL),
    ]
    fascia = [
        segment_box(front_segments[0][0], front_segments[0][1],
                    GLASS_HEAD + FASCIA_HEIGHT * 0.5, FASCIA_HEIGHT, 0.34),
        segment_box(front_segments[0][1], front_segments[1][0],
                    GLASS_HEAD + FASCIA_HEIGHT * 0.5, FASCIA_HEIGHT, 0.34),
        polyline_strip(frontage_chain, 0.34, GLASS_HEAD,
                       GLASS_HEAD + FASCIA_HEIGHT),
    ]
    add_part(asset, "Cafe_GlazedPlinth", merge(plinth), "facade_plinth",
             "CafeExteriorDetail", group="frontage")
    add_part(asset, "Cafe_DeepFascia", merge(fascia), "facade_fascia",
             "CafeExteriorDetail", group="frontage")

    # One unlayered glass pane per panel. The chamfer is split into narrow
    # facets to read as the painting's rounded glass corner without changing
    # the logical five-point footprint.
    glass_segments: list[tuple[tuple[float, float], tuple[float, float]]] = []
    for a, b, divisions in (
        ((-2.72, -4.56), (1.68, -4.56), 3),
        ((1.68, -4.56), (4.48, -1.76), 5),
        ((4.48, -1.76), (4.48, 5.44), 4),
    ):
        for index in range(divisions):
            t0 = index / divisions
            t1 = (index + 1) / divisions
            start = (a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0)
            end = (a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1)
            glass_segments.append((start, end))
    for index, (start, end) in enumerate(glass_segments):
        add_part(
            asset,
            f"Cafe_Glass_{index:02d}",
            segment_box(
                start,
                end,
                (GLASS_SILL + LUMINOUS_BAND_LOW) * 0.5,
                LUMINOUS_BAND_LOW - GLASS_SILL - 0.08,
                GLASS_THICKNESS,
                0.001,
            ),
            "glass",
            "CafeGlassDetail",
            group="glazing",
            casts_shadows=False,
        )

    mullions: list[kit.Geometry] = []
    mullion_points = [glass_segments[0][0]] + [segment[1] for segment in glass_segments]
    for x, z in mullion_points:
        mullions.append(bp.u_box(
            (x, (GLASS_SILL + LUMINOUS_BAND_LOW) * 0.5, z),
            (0.095, LUMINOUS_BAND_LOW - GLASS_SILL - 0.18, 0.095),
            0.006,
        ))
    add_part(asset, "Cafe_Mullions", merge(mullions), "window_frame",
             "CafeMetalDetail", group="frontage")

    rails: list[kit.Geometry] = [
        segment_box(front_segments[0][0], front_segments[0][1],
                    GLASS_SILL + 0.045, 0.09, 0.13),
        segment_box(front_segments[0][0], front_segments[0][1],
                    LUMINOUS_BAND_LOW - 0.045, 0.09, 0.13),
        polyline_strip(frontage_chain, 0.13, GLASS_SILL, GLASS_SILL + 0.09),
        polyline_strip(
            frontage_chain,
            0.13,
            LUMINOUS_BAND_LOW - 0.09,
            LUMINOUS_BAND_LOW,
        ),
    ]
    add_part(asset, "Cafe_WindowRails", merge(rails), "window_frame",
             "CafeMetalDetail", group="frontage")

    # Door geometry stays inside the authored opening and carries no collider.
    door_root = add_empty(asset, "PROP_OpenDoor", (-4.30, 0.0, -4.42))
    leaf = bp.u_box((0.74, 1.14, 0.0), (1.26, 2.06, 0.032), 0.004)
    leaf = bp.u_rotated(leaf, (0.0, -68.0, 0.0))
    add_part(asset, "Cafe_OpenDoorGlass", leaf, "door_glass",
             "CafeGlassDetail", group="door", casts_shadows=False,
             parent=door_root)
    frame = merge([
        bp.u_box((0.05, 1.14, 0.0), (0.10, 2.28, 0.08), 0.005),
        bp.u_box((0.74, 2.23, 0.0), (1.28, 0.10, 0.08), 0.005),
        bp.u_box((1.43, 1.14, 0.0), (0.10, 2.28, 0.08), 0.005),
    ])
    frame = bp.u_rotated(frame, (0.0, -68.0, 0.0))
    add_part(asset, "Cafe_OpenDoorFrame", frame, "door_frame",
             "CafeMetalDetail", group="door", parent=door_root)
    door_jambs = merge([
        bp.u_box((-4.32, 1.14, -4.56), (0.11, 2.28, 0.30), 0.006),
        bp.u_box((-2.72, 1.14, -4.56), (0.11, 2.28, 0.30), 0.006),
        bp.u_box((-3.52, 2.32, -4.56), (1.49, 0.10, 0.30), 0.006),
    ])
    add_part(asset, "Cafe_DoorJambs", door_jambs, "door_frame",
             "CafeMetalDetail", group="door")

    # The luminous soffit is geometry only; Unity supplies the actual light.
    bands = [polyline_strip(
        frontage_chain,
        0.14,
        LUMINOUS_BAND_LOW - OPAQUE_DETAIL_CLEARANCE,
        GLASS_HEAD + OPAQUE_DETAIL_CLEARANCE,
    )]
    add_part(asset, "Cafe_LuminousBand", merge(bands), "practical_emission",
             "CafeWarmEmission", group="lighting", emissive=True,
             casts_shadows=False)


def build_interior(asset: AssetBuild) -> None:
    # Interior lining is inset enough to avoid competing with outer walls.
    add_part(
        asset,
        "Cafe_InteriorLining",
        merge([
            bp.u_box((-5.17, 2.10, 0.40), (0.035, 3.18, 9.10), 0.004),
            bp.u_box(
                (-0.35, 2.10, REAR_LINING_CENTER_Z),
                (9.15, 3.18, REAR_LINING_DEPTH),
                0.004,
            ),
        ]),
        "interior_wall",
        "CafeInteriorDetail",
        group="interior",
    )

    # Long mahogany counter and faceted return.
    counter_base = bp.u_box((0.62, 0.45, -1.15), (6.10, 0.90, 0.82), 0.024)
    counter_top = bp.u_box((0.62, 0.96, -1.15), (6.36, 0.12, 1.02), 0.025)
    return_start = (3.20, -1.15)
    return_end = (3.45, 0.55)
    return_base = segment_box(return_start, return_end, 0.447, 0.894, 0.62, 0.024)
    return_top = segment_box(return_start, return_end, 0.9605, 0.105, 0.72, 0.025)
    add_part(asset, "Cafe_CounterBase", kit.merge(counter_base, return_base),
             "counter_base", "CafeCounterDetail", group="counter")
    add_part(asset, "Cafe_CounterTop", kit.merge(counter_top, return_top),
             "counter_top", "CafeCounterDetail", group="counter")
    apron = bp.u_box((0.62, 0.70, -1.585), (6.18, 0.42, 0.045), 0.006)
    add_part(asset, "Cafe_CounterApron", apron, "counter_apron",
             "CafeCounterDetail", group="counter")
    rail_bar = bp.u_cylinder((0.0, 0.0, 0.0), (0.055, 3.02, 0.055), 12)
    rail_bar = bp.u_rotated(rail_bar, (0.0, 0.0, 90.0))
    rail_bar = translate_geometry(rail_bar, (0.62, 0.25, -1.73))
    foot_rail = merge([
        rail_bar,
        bp.u_cylinder((-2.35, 0.25, -1.73), (0.12, 0.25, 0.12), 10),
        bp.u_cylinder((3.59, 0.25, -1.73), (0.12, 0.25, 0.12), 10),
    ])
    add_part(asset, "Cafe_CounterFootRail", foot_rail, "counter_rail",
             "CafeMetalDetail", group="counter")

    # Seven real bar stools. Their old 0.4675 m dining-chair height left the
    # authored patrons visibly hovering 0.35 m above the seats; 0.8175 m meets
    # the measured underside of all three seated coats beside the 1.02 m bar.
    stool_metal: list[kit.Geometry] = []
    stool_seats: list[kit.Geometry] = []
    for index, (x, z, forward_x, forward_z) in enumerate(STOOL_STATIONS):
        stool_metal.extend([
            bp.u_cylinder((x, 0.390, z), (0.10, 0.390, 0.10), 10),
            bp.u_cylinder((x, 0.035, z), (0.34, 0.035, 0.34), 12),
        ])
        stool_seats.append(
            bp.u_cylinder((x, 0.790, z), (0.48, 0.0275, 0.48), 14)
        )
        add_anchor(asset, f"Stool.{index:02d}", "stool", (x, STOOL_SEAT_TOP_Y, z),
                   (forward_x, 0.0, forward_z))
    add_part(asset, "Cafe_StoolMetal", merge(stool_metal), "stool_metal",
             "CafeMetalDetail", group="furniture")
    add_part(asset, "Cafe_StoolSeats", merge(stool_seats), "stool_seat",
             "CafeCounterDetail", group="furniture")

    # Rear service wall: one grounded kitchen run. Its back edge meets the
    # lining with only a z-fighting clearance, while the right end stops before
    # the existing rear door. Separate fronts, handles and a shallow backsplash
    # make the run read as cabinetry instead of one anonymous metal box.
    add_part(
        asset,
        "Cafe_ServiceCabinet",
        bp.u_box(SERVICE_CABINET_CENTER, SERVICE_CABINET_SIZE, 0.018),
        "service_cabinet",
        "CafeMetalDetail",
        group="service",
    )
    add_part(
        asset,
        "Cafe_ServiceWorktop",
        bp.u_box(SERVICE_WORKTOP_CENTER, SERVICE_WORKTOP_SIZE, 0.018),
        "service_worktop",
        "CafeCounterDetail",
        group="service",
    )
    cabinet_fronts: list[kit.Geometry] = []
    cabinet_handles: list[kit.Geometry] = []
    for low_x, high_x in (
        (-2.55, -1.55),
        (-1.45, -0.45),
        (-0.35, 0.65),
        (0.75, 1.75),
        (1.85, 2.93),
    ):
        center_x = (low_x + high_x) * 0.5
        cabinet_fronts.append(bp.u_box(
            (center_x, 0.435, SERVICE_CABINET_FRONT_Z - 0.0215),
            (high_x - low_x, 0.68, 0.037),
            0.012,
        ))
        cabinet_handles.append(bp.u_box(
            (center_x, 0.68, SERVICE_CABINET_FRONT_Z - 0.066),
            (0.24, 0.035, 0.046),
            0.007,
        ))
    add_part(
        asset,
        "Cafe_KitchenCabinetFronts",
        merge(cabinet_fronts),
        "kitchen_cabinet_front",
        "CafeCounterDetail",
        group="service",
    )
    add_part(
        asset,
        "Cafe_KitchenHandles",
        merge(cabinet_handles),
        "kitchen_cabinet_handle",
        "CafeMetalDetail",
        group="service",
    )
    add_part(
        asset,
        "Cafe_KitchenBacksplash",
        bp.u_box(
            (0.23, 1.173, KITCHEN_BACK_Z - 0.021),
            (5.74, 0.44, 0.042),
            0.008,
        ),
        "kitchen_backsplash",
        "CafeInteriorDetail",
        group="service",
    )

    # A compact two-ring plate, an old shallow frying pan and a passive board
    # occupy distinct, measured zones. Nothing here has an interaction or an
    # animation; the authored docks only preserve future contact geometry.
    stove_parts: list[kit.Geometry] = [
        bp.u_box(
            (
                STOVE_CENTER[0],
                (STOVE_PLATE_LOW_Y + STOVE_PLATE_HIGH_Y) * 0.5,
                STOVE_CENTER[1],
            ),
            (STOVE_SIZE[0], STOVE_PLATE_HIGH_Y - STOVE_PLATE_LOW_Y,
             STOVE_SIZE[1]),
            0.012,
        ),
    ]
    burner_half_height = (STOVE_BURNER_TOP_Y - STOVE_PLATE_HIGH_Y) * 0.5
    for burner_x in (STOVE_CENTER[0] - 0.28, STOVE_CENTER[0] + 0.28):
        stove_parts.append(bp.u_cylinder(
            (
                burner_x,
                STOVE_PLATE_HIGH_Y + burner_half_height,
                STOVE_CENTER[1],
            ),
            (0.38, burner_half_height, 0.38),
            12,
        ))
    for knob_x in (STOVE_CENTER[0] - 0.24, STOVE_CENTER[0] + 0.24):
        stove_parts.append(bp.u_box(
            (knob_x, STOVE_PLATE_LOW_Y + 0.018, STOVE_CENTER[1] - 0.365),
            (0.13, 0.035, 0.045),
            0.006,
        ))
    add_part(asset, "Cafe_Stove", merge(stove_parts), "stove",
             "CafeMetalDetail", group="appliance")

    pan_bowl = translate_geometry(
        hollow_cup(
            height=0.075,
            bottom_radius=0.20,
            top_radius=0.27,
            thickness=0.035,
            sides=16,
        ),
        (PAN_CENTER[0], PAN_BOTTOM_Y, PAN_CENTER[1]),
    )
    pan_handle = segment_box(
        (PAN_CENTER[0] - 0.18, PAN_CENTER[1] - 0.18),
        (PAN_CENTER[0] - 0.55, PAN_CENTER[1] - 0.55),
        PAN_BOTTOM_Y + 0.052,
        0.070,
        0.11,
        0.012,
    )
    add_part(asset, "Cafe_FryingPan", kit.merge(pan_bowl, pan_handle),
             "frying_pan", "CafeMetalDetail", group="props")
    add_part(
        asset,
        "Cafe_CuttingBoard",
        bp.u_box(
            (
                CUTTING_BOARD_CENTER[0],
                (CUTTING_BOARD_LOW_Y + CUTTING_BOARD_HIGH_Y) * 0.5,
                CUTTING_BOARD_CENTER[1],
            ),
            (
                CUTTING_BOARD_SIZE[0],
                CUTTING_BOARD_HIGH_Y - CUTTING_BOARD_LOW_Y,
                CUTTING_BOARD_SIZE[1],
            ),
            0.018,
        ),
        "cutting_board",
        "CafeCounterDetail",
        group="props",
    )

    # The existing cold service light moves into a visible suspended task
    # fixture directly above the plate. Geometry adds no new Light or tone;
    # Unity continues to own the same three-light cafe contract.
    task_fixture = merge([
        bp.u_box(STOVE_TASK_FIXTURE_CENTER, (1.38, 0.20, 0.74), 0.028),
        bp.u_cylinder(
            (STOVE_CENTER[0] - 0.44, 3.31, STOVE_CENTER[1]),
            (0.05, 0.87, 0.05),
            10,
        ),
        bp.u_cylinder(
            (STOVE_CENTER[0] + 0.44, 3.31, STOVE_CENTER[1]),
            (0.05, 0.87, 0.05),
            10,
        ),
    ])
    add_part(asset, "Cafe_StoveTaskFixture", task_fixture,
             "stove_task_fixture", "CafeMetalDetail", group="lighting")
    add_part(
        asset,
        "Cafe_StoveTaskLens",
        bp.u_box(STOVE_TASK_LENS_CENTER, (1.02, 0.04, 0.46), 0.010),
        "stove_task_lens",
        "CafePropsDetail",
        group="lighting",
        emissive=True,
        casts_shadows=False,
    )

    # Refrigerator body and interior remain fixed. The closed leaf is a sixth
    # authored dynamic assembly whose root is the real hinge; no runtime driver
    # is added here. Opening it later reveals a recessed liner and two shelves.
    fridge_shell_z = FRIDGE_CENTER[2] - 0.02
    fridge_back_z = FRIDGE_CENTER[2] + 0.34
    fridge_body = merge([
        bp.u_box((-4.335, 0.98, fridge_shell_z), (0.09, 1.96, 0.68), 0.015),
        bp.u_box((-3.305, 0.98, fridge_shell_z), (0.09, 1.96, 0.68), 0.015),
        bp.u_box((-3.82, 1.915, fridge_shell_z), (0.94, 0.09, 0.68), 0.015),
        bp.u_box((-3.82, 0.045, fridge_shell_z), (0.94, 0.09, 0.68), 0.015),
        bp.u_box((-3.82, 0.98, fridge_back_z), (1.12, 1.96, 0.04), 0.010),
    ])
    add_part(asset, "Cafe_RefrigeratorBody", fridge_body,
             "refrigerator_body", "CafePropsDetail", group="appliance")
    add_part(
        asset,
        "Cafe_RefrigeratorCavity",
        bp.u_box(
            (-3.82, 0.98, 5.025 + FRIDGE_Z_SHIFT),
            (0.90, 1.70, 0.018),
            0.003,
        ),
        "refrigerator_cavity",
        "CafePropsDetail",
        group="appliance",
    )
    add_part(
        asset,
        "Cafe_RefrigeratorShelves",
        merge([
            bp.u_box((-3.82, 0.65, fridge_shell_z),
                     (0.84, 0.025, 0.56), 0.006),
            bp.u_box((-3.82, 1.25, fridge_shell_z),
                     (0.84, 0.025, 0.56), 0.006),
        ]),
        "refrigerator_shelf",
        "CafePropsDetail",
        group="appliance",
    )
    fridge_door = add_prop(
        asset,
        "FridgeDoor",
        "fridge_door",
        "FridgeDoor",
        FRIDGE_DOOR_PIVOT,
    )
    door_geometry = merge([
        bp.u_box(
            (FRIDGE_DOOR_WIDTH * 0.5, 0.98, 0.0),
            (FRIDGE_DOOR_WIDTH, FRIDGE_DOOR_HEIGHT, FRIDGE_DOOR_DEPTH),
            0.022,
        ),
        bp.u_box((0.04, 0.98, -0.055), (0.035, 1.72, 0.030), 0.005),
        bp.u_box((0.975, 0.98, -0.055), (0.030, 1.72, 0.030), 0.005),
        bp.u_box((0.50, 0.09, -0.055), (0.78, 0.045, 0.030), 0.005),
        bp.u_box((0.50, 1.87, -0.055), (0.78, 0.045, 0.030), 0.005),
        bp.u_box(
            (FRIDGE_GRIP_LOCAL[0], FRIDGE_GRIP_LOCAL[1], -0.085),
            (0.050, 1.15, 0.090),
            0.010,
        ),
    ])
    add_part(
        asset,
        "Cafe_FridgeDoor",
        door_geometry,
        "fridge_door",
        "CafePropsDetail",
        group="dynamic_prop",
        parent=fridge_door.root,
    )
    fridge_door.part_names.append("Cafe_FridgeDoor")
    add_anchor(
        asset,
        "Grip.FridgeDoor",
        "fridge_door_grip",
        FRIDGE_GRIP_LOCAL,
        (0.0, 0.0, -1.0),
        parent=fridge_door.root,
    )
    rear_door = merge([
        bp.u_box((3.75, 1.24, 5.25), (1.12, 2.38, 0.055), 0.008),
        bp.u_box((3.32, 1.24, 5.205), (0.055, 2.26, 0.035), 0.004),
        bp.u_box((4.18, 1.24, 5.205), (0.055, 2.26, 0.035), 0.004),
    ])
    add_part(asset, "Cafe_RearDoor", rear_door, "rear_door",
             "CafeInteriorDetail", group="service")

    # Twin urns with lids, gauges, taps and sight glasses.
    urn_metal: list[kit.Geometry] = []
    urn_glass: list[kit.Geometry] = []
    urn_z = 3.84 + KITCHEN_EQUIPMENT_Z_SHIFT
    for x in (1.55, 2.75):
        urn_metal.extend([
            bp.u_cylinder((x, 1.54, urn_z), (0.62, 0.59, 0.62), 14),
            bp.u_cylinder((x, 2.17, urn_z), (0.52, 0.045, 0.52), 14),
            bp.u_cylinder((x, 2.25, urn_z), (0.09, 0.055, 0.09), 10),
            bp.u_cylinder((x, 1.05, urn_z), (0.48, 0.055, 0.48), 14),
            bp.u_box((x, 1.42, 3.49 + KITCHEN_EQUIPMENT_Z_SHIFT),
                     (0.08, 0.32, 0.22), 0.006),
            bp.u_box((x, 1.23, 3.39 + KITCHEN_EQUIPMENT_Z_SHIFT),
                     (0.28, 0.06, 0.07), 0.005),
        ])
        urn_glass.append(bp.u_cylinder(
            (x - 0.19, 1.60, 3.50 + KITCHEN_EQUIPMENT_Z_SHIFT),
                                       (0.07, 0.34, 0.07), 10))
    add_part(asset, "Cafe_CoffeeUrns", merge(urn_metal), "coffee_urn",
             "CafeMetalDetail", group="appliance")
    add_part(asset, "Cafe_UrnSightGlass", merge(urn_glass), "urn_sight_glass",
             "CafeGlassDetail", group="appliance", casts_shadows=False)

    # Small counter equipment: napkin dispenser, sugar, salt and paper stack.
    details = merge([
        bp.u_box(
            (-0.25, 1.10, HERO_COUNTER_DETAIL_Z),
            (0.22, 0.22, 0.15),
            0.012,
        ),
        bp.u_cylinder(
            (0.10, 1.10, HERO_COUNTER_DETAIL_Z),
            (0.10, 0.10, 0.10),
            10,
        ),
        bp.u_cylinder(
            (0.30, 1.09, HERO_COUNTER_DETAIL_Z),
            (0.08, 0.09, 0.08),
            10,
        ),
        bp.u_box((2.43, 1.045, -1.18), (0.42, 0.025, 0.30), 0.004),
    ])
    add_part(asset, "Cafe_CounterDetails", details, "counter_details",
             "CafePropsDetail", group="props")


def menu_leaf_geometry(
    side: int,
    total_width: float,
    depth: float,
    thickness: float,
    spine_gap: float,
    minimum_y: float,
) -> tuple[kit.Geometry, float]:
    """Build one leaf and return its translated geometry/Y offset.

    Both leaves descend from the raised spine toward their outer edge. The
    explicit offset keeps the lowest cover point a few millimetres above the
    real counter plane while preserving one deterministic local page plane.
    """
    if side not in (-1, 1):
        raise ValueError("A menu leaf side must be -1 or +1.")
    leaf_width = (total_width - spine_gap) * 0.5
    inner_x = side * spine_gap * 0.5
    center_x = inner_x + side * leaf_width * 0.5
    geometry = bp.u_box(
        (center_x, thickness * 0.5, 0.0),
        (leaf_width, thickness, depth),
        min(0.001, thickness * 0.20),
    )
    geometry = bp.u_rotated_about(
        geometry,
        (0.0, 0.0, -side * MENU_OPEN_ANGLE_DEGREES),
        (inner_x, 0.0, 0.0),
    )
    low, _ = kit.bounds(geometry)
    y_offset = minimum_y - low[1]
    return translate_geometry(geometry, (0.0, y_offset, 0.0)), y_offset


def menu_right_page_surface_y(local_x: float, page_y_offset: float) -> float:
    """Height of the right page's upper plane in Menu.Hero local space."""
    angle = math.radians(MENU_OPEN_ANGLE_DEGREES)
    inner_x = MENU_PAGE_GAP * 0.5
    return (
        -(local_x - inner_x) * math.sin(angle)
        + MENU_PAGE_THICKNESS * math.cos(angle)
        + page_y_offset
    )


def build_menu_prop(asset: AssetBuild) -> None:
    menu = add_prop(asset, "Menu.Hero", "menu_booklet", "HeroMenu", MENU_DOCK)
    left_cover, _ = menu_leaf_geometry(
        -1,
        MENU_COVER_WIDTH,
        MENU_COVER_DEPTH,
        MENU_COVER_THICKNESS,
        MENU_COVER_GAP,
        MENU_COVER_MIN_Y,
    )
    right_cover, _ = menu_leaf_geometry(
        1,
        MENU_COVER_WIDTH,
        MENU_COVER_DEPTH,
        MENU_COVER_THICKNESS,
        MENU_COVER_GAP,
        MENU_COVER_MIN_Y,
    )
    left_page, _ = menu_leaf_geometry(
        -1,
        MENU_PAGE_WIDTH,
        MENU_PAGE_DEPTH,
        MENU_PAGE_THICKNESS,
        MENU_PAGE_GAP,
        MENU_PAGE_MIN_Y,
    )
    right_page, right_page_y_offset = menu_leaf_geometry(
        1,
        MENU_PAGE_WIDTH,
        MENU_PAGE_DEPTH,
        MENU_PAGE_THICKNESS,
        MENU_PAGE_GAP,
        MENU_PAGE_MIN_Y,
    )
    add_part(
        asset,
        "Cafe_MenuCover",
        merge((left_cover, right_cover)),
        "menu_cover",
        "CafeCounterDetail",
        group="dynamic_prop",
        parent=menu.root,
        initially_visible=False,
    )
    add_part(
        asset,
        "Cafe_MenuPages",
        merge((left_page, right_page)),
        "menu_pages",
        "CafePropsDetail",
        group="dynamic_prop",
        parent=menu.root,
        initially_visible=False,
    )
    menu.part_names.extend(("Cafe_MenuCover", "Cafe_MenuPages"))

    add_anchor(asset, "MenuDock.Hero", "menu_dock", MENU_DOCK)
    add_anchor(
        asset,
        "Grip.HeroMenu",
        "menu_grip",
        MENU_GRIP_LOCAL,
        (0.0, 0.0, -1.0),
        parent=menu.root,
    )
    for name, (local_x, local_z) in MENU_TEXT_POSITIONS.items():
        local_y = (
            menu_right_page_surface_y(local_x, right_page_y_offset)
            + MENU_TEXT_CLEARANCE
        )
        role = (
            "menu_text_selection"
            if name == "MenuText.Selection"
            else "menu_text_item"
        )
        add_anchor(
            asset,
            name,
            role,
            (local_x, local_y, local_z),
            MENU_PAGE_FORWARD,
            MENU_PAGE_UP,
            parent=menu.root,
        )


def build_cup_props(asset: AssetBuild) -> None:
    for owner, position in CUP_STATIONS.items():
        # User-approved reversal: both handles face the opposite side from
        # the previous cafe build. The animated limb stays the same; the
        # pickup/release keys are re-fitted to each new Grip position.
        handle_negative_x = owner == "PairWoman"
        prop_name = f"Cup.{owner}"
        prop = add_prop(asset, prop_name, "cup_assembly", owner, position)
        lift = add_empty(asset, f"LIFT_Cup_{owner}", (0.0, 0.0, 0.0), prop.root)
        lift["bp_role"] = "cup_lift_root"
        prop.lift_root = lift
        ceramic_name = f"Cup_{owner}_Ceramic"
        saucer_name = f"Cup_{owner}_Saucer"
        liquid_name = f"Cup_{owner}_Liquid"
        ceramic = cup_geometry()
        if handle_negative_x:
            ceramic = bp.u_rotated(ceramic, (0.0, 180.0, 0.0))
        add_part(asset, ceramic_name, ceramic, "cup_ceramic",
                 "CafePropsDetail", group="dynamic_prop", parent=lift)
        add_part(asset, saucer_name, saucer_geometry(), "cup_saucer",
                 "CafePropsDetail", group="dynamic_prop", parent=prop.root)
        liquid_y = LIQUID_FULL_LOCAL_Y
        add_part(asset, liquid_name, liquid_geometry(), "coffee_liquid",
                 "CafeCoffee", group="dynamic_prop",
                 unity_origin=(0.0, liquid_y, 0.0), parent=lift,
                 casts_shadows=False)
        prop.part_names.extend((ceramic_name, saucer_name, liquid_name))
        prop.liquid_part = liquid_name
        prop.empty_local_y = LIQUID_EMPTY_LOCAL_Y
        prop.full_local_y = LIQUID_FULL_LOCAL_Y
        add_anchor(asset, f"Cup.{owner}", "cup_dock", position)
        add_anchor(asset, f"PourTarget.{owner}", "pour_target",
                   (position[0], position[1] + 0.095, position[2]))
        # The handle points toward the hand that actually owns this cup.
        # Keeping the authored Grip on that handle lets the hand arrive over
        # the saucer at pickup/release instead of visibly docking beside it.
        grip_x = -0.092 if handle_negative_x else 0.092
        add_anchor(asset, f"Grip.{owner}", "cup_grip",
                   (grip_x, 0.062, 0.0), (1.0, 0.0, 0.0),
                   parent=lift)


def build_service_props(asset: AssetBuild) -> None:
    pot_position = (2.55, 1.02, 3.72 + KITCHEN_EQUIPMENT_Z_SHIFT)
    pot = add_prop(asset, "ServicePot", "service_pot", "Attendant", pot_position)
    body = kit.merge(
        bp.u_tapered_cylinder((0.0, 0.15, 0.0), (0.30, 0.15, 0.30), 0.86, 14),
        ring_tube((0.17, 0.17, 0.0), 0.13, 0.018, -78.0, 78.0, 10, 6),
        bp.u_tapered_cylinder((0.24, 0.24, 0.0), (0.20, 0.07, 0.16), 0.35, 10),
    )
    add_part(asset, "Service_Pot", body, "service_pot",
             "CafeMetalDetail", group="dynamic_prop", parent=pot.root,
             initially_visible=False)
    add_part(asset, "Service_PotLid",
             bp.u_cylinder((0.0, 0.325, 0.0), (0.25, 0.025, 0.25), 14),
             "service_pot_lid", "CafeMetalDetail", group="dynamic_prop",
             parent=pot.root, initially_visible=False)
    pot.part_names.extend(("Service_Pot", "Service_PotLid"))
    add_anchor(asset, "PotDock", "pot_dock", pot_position)
    add_anchor(asset, "PotSpout", "pot_spout", (0.34, 0.27, 0.0),
               parent=pot.root)

    towel_position = (2.10, 1.035, -0.52)
    towel = add_prop(asset, "ServiceTowel", "service_towel", "Attendant",
                     towel_position)
    towel_geometry = bp.u_box((0.0, 0.018, 0.0), (0.42, 0.036, 0.28), 0.008)
    add_part(asset, "Service_Towel", towel_geometry, "service_towel",
             "CafePropsDetail", group="dynamic_prop", parent=towel.root,
             initially_visible=False)
    towel.part_names.append("Service_Towel")

    stream_position = (0.0, -10.0, 0.0)
    stream = add_prop(asset, "PourStream", "pour_stream", "Attendant",
                      stream_position)
    stream_geometry = bp.u_tapered_cylinder(
        (0.0, -0.12, 0.0), (0.020, 0.12, 0.020), 0.55, 8)
    add_part(asset, "Service_PourStream", stream_geometry, "pour_stream",
             "CafeCoffee", group="dynamic_prop", parent=stream.root,
             casts_shadows=False, initially_visible=False)
    stream.part_names.append("Service_PourStream")

    # Reachable strip directly in front of the attendant's dock. These marks
    # sit 10 mm above the real 1.02 m counter top; the old cup-aligned marks
    # were more than a metre away and described a wipe no human arm could
    # make, even though runtime did not yet consume them.
    for index, x in enumerate((2.25, 2.45, 2.65)):
        add_anchor(asset, f"WipePatch.{index:02d}", "wipe_patch",
                   (x, 1.03, -0.70))
    for index, position in enumerate((
        (2.10, 0.0, -0.16),
        # A right-handed server stands 0.30 m to the cup's right, while the
        # service mark is pulled 0.24 m toward the counter. This keeps the
        # arm clear of the torso and puts the animated spout directly above
        # the cup instead of asking a diagonal stream to bridge the gap.
        (1.05, 0.0, -0.76),
        (2.10, 0.0, -0.76),
    )):
        add_anchor(asset, f"ServiceRail.{index:02d}", "service_rail", position)
    add_anchor(asset, "ServiceRail.Hero", "service_rail", HERO_SERVICE_RAIL)


def build_anchors(asset: AssetBuild) -> None:
    add_anchor(asset, "Origin", "model_origin", (0.0, 0.0, 0.0))
    add_anchor(asset, "DoorThreshold", "door_threshold", DOOR_CENTER,
               (0.0, 0.0, -1.0))
    add_anchor(asset, "DoorApproach", "door_approach",
               (DOOR_CENTER[0], 0.0, DOOR_CENTER[2] - 1.25),
               (0.0, 0.0, 1.0))
    add_anchor(asset, "InteriorCenter", "interior_center", (0.0, 1.0, 0.30))
    add_anchor(asset, "CanonicalCameraTarget", "camera_target", (0.30, 1.65, -0.90))
    add_anchor(asset, "GlassCorner", "glass_corner", (3.08, 2.20, -3.16),
               (0.707107, 0.0, -0.707107))
    add_anchor(asset, "CounterStart", "counter_start", (-2.56, 1.02, -1.15))
    add_anchor(asset, "CounterCorner", "counter_corner", (3.20, 1.02, -1.15))
    add_anchor(asset, "CounterEnd", "counter_end", (3.45, 1.02, 0.55))
    add_anchor(asset, "FridgeDoorPivot", "fridge_door_pivot",
               FRIDGE_DOOR_PIVOT, (0.0, 0.0, -1.0))
    add_anchor(
        asset,
        "CuttingBoardDock",
        "cutting_board_dock",
        (CUTTING_BOARD_CENTER[0], CUTTING_BOARD_HIGH_Y,
         CUTTING_BOARD_CENTER[1]),
        (0.0, 0.0, -1.0),
    )
    add_anchor(
        asset,
        "StovePanDock",
        "stove_pan_dock",
        (PAN_CENTER[0], PAN_BOTTOM_Y, PAN_CENTER[1]),
        (0.0, 0.0, -1.0),
    )
    add_anchor(asset, "HeroSeat", "hero_seat", (-0.38, STOOL_SEAT_TOP_Y, STOOL_Z),
               (0.0, 0.0, 1.0))
    for name, position in (
        ("Cast.Lone", (-1.50, 0.0, STOOL_Z)),
        ("Cast.PairMan", (0.75, 0.0, STOOL_Z)),
        ("Cast.PairWoman", (1.80, 0.0, STOOL_Z)),
        ("Cast.Attendant", (2.10, 0.0, -0.16)),
    ):
        add_anchor(asset, name, "cast_mark", position, (0.0, 0.0, 1.0))
    for name, role, position in (
        ("Light.WarmCounter", "light_warm_counter", (0.60, 3.47, -0.65)),
        ("Light.ColdService", "light_cold_service", STOVE_TASK_LENS_CENTER),
        ("Light.ExteriorWash", "light_exterior_wash", (3.60, 5.00, -3.60)),
        ("Audio.Fridge", "audio_fridge",
         (FRIDGE_CENTER[0], 1.0, FRIDGE_CENTER[2])),
        ("Audio.Fixture", "audio_fixture", (0.60, 3.47, -0.65)),
        ("Audio.Boiler", "audio_boiler",
         (2.15, 1.55, 3.84 + KITCHEN_EQUIPMENT_Z_SHIFT)),
    ):
        add_anchor(asset, name, role, position)


COLLIDER_DESCRIPTORS = (
    {"id": "boundary-west", "shape": "box", "center": [-5.32, 2.08, 0.44], "size": [0.24, 4.16, 10.0], "yaw": 0.0},
    {"id": "boundary-rear", "shape": "box", "center": [-0.42, 2.08, 5.44], "size": [9.80, 4.16, 0.24], "yaw": 0.0},
    {"id": "boundary-south-left", "shape": "box", "center": [-4.82, 2.08, -4.56], "size": [1.0, 4.16, 0.24], "yaw": 0.0},
    {"id": "boundary-south-right", "shape": "box", "center": [-0.52, 2.08, -4.56], "size": [4.40, 4.16, 0.12], "yaw": 0.0},
    {"id": "boundary-chamfer", "shape": "box", "center": [3.08, 2.08, -3.16], "size": [3.96, 4.16, 0.12], "yaw": -45.0},
    {"id": "boundary-east", "shape": "box", "center": [4.48, 2.08, 1.84], "size": [0.12, 4.16, 7.20], "yaw": 0.0},
    {"id": "counter-main", "shape": "box", "center": [0.62, 0.45, -1.15], "size": [6.10, 0.90, 0.82], "yaw": 0.0},
    {"id": "counter-return", "shape": "box", "center": [3.325, 0.447, -0.30], "size": [1.718, 0.894, 0.62], "yaw": -81.634},
    {"id": "service-cabinet", "shape": "box", "center": list(SERVICE_CABINET_CENTER), "size": list(SERVICE_CABINET_SIZE), "yaw": 0.0},
    {"id": "fridge", "shape": "box", "center": list(FRIDGE_CENTER), "size": list(FRIDGE_SIZE), "yaw": 0.0},
    {"id": "stool-00", "shape": "capsule", "center": [-1.50, 0.40875, -2.18], "radius": 0.25, "height": 0.8175, "yaw": 0.0},
    {"id": "stool-01", "shape": "capsule", "center": [-0.38, 0.40875, -2.18], "radius": 0.25, "height": 0.8175, "yaw": 0.0},
    {"id": "stool-02", "shape": "capsule", "center": [0.75, 0.40875, -2.18], "radius": 0.25, "height": 0.8175, "yaw": 0.0},
    {"id": "stool-03", "shape": "capsule", "center": [1.80, 0.40875, -2.18], "radius": 0.25, "height": 0.8175, "yaw": 0.0},
    {"id": "stool-04", "shape": "capsule", "center": [3.00, 0.40875, -2.18], "radius": 0.25, "height": 0.8175, "yaw": 0.0},
    {"id": "stool-05", "shape": "capsule", "center": [4.08, 0.40875, -0.62], "radius": 0.25, "height": 0.8175, "yaw": -81.634},
    {"id": "stool-06", "shape": "capsule", "center": [4.16, 0.40875, 0.10], "radius": 0.25, "height": 0.8175, "yaw": -81.634},
)


def configure_scene() -> tuple["bpy.types.Collection", "bpy.types.Object"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene["bp_generator"] = "tools/build-mountain-road-cafe-3d-model.py"
    scene["bp_generator_version"] = GENERATOR_VERSION
    scene["bp_design_id"] = DESIGN_ID
    scene["bp_source_forward"] = "+Y"
    source = bpy.data.collections.new("SOURCE_MountainRoadCafe3D")
    scene.collection.children.link(source)
    root = bpy.data.objects.new("ROOT_MountainRoadCafe3D", None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.65
    source.objects.link(root)
    return source, root


def build() -> AssetBuild:
    collection, root = configure_scene()
    materials = {sheet: create_material(sheet) for sheet in ALLOWED_SHEETS}
    asset = AssetBuild(root, collection, materials)
    build_shell(asset)
    build_interior(asset)
    build_menu_prop(asset)
    build_cup_props(asset)
    build_service_props(asset)
    build_anchors(asset)
    return asset


def world_source_geometry(part: Part) -> kit.Geometry:
    bpy.context.view_layer.update()
    vertices, faces = part.local_geometry
    transformed = [tuple(part.obj.matrix_world @ Vector(vertex)) for vertex in vertices]
    return transformed, list(faces)


def unity_world_bounds(
    part: Part,
) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    """Measured root-local bounds in the Unity axis convention."""
    low, high = kit.bounds(world_source_geometry(part))
    return (low[0], low[2], low[1]), (high[0], high[2], high[1])


def face_normal(
    vertices: Sequence[Sequence[float]],
    face: Sequence[int],
) -> tuple[float, float, float] | None:
    if len(face) < 3:
        return None
    origin = Vector(vertices[face[0]])
    for index in range(1, len(face) - 1):
        first = Vector(vertices[face[index]]) - origin
        second = Vector(vertices[face[index + 1]]) - origin
        normal = first.cross(second)
        if normal.length_squared > 1e-12:
            normal.normalize()
            return tuple(normal)
    return None


def projected_face_bounds(
    vertices: Sequence[Sequence[float]],
    face: Sequence[int],
    dropped_axis: int,
) -> tuple[float, float, float, float]:
    axes = [axis for axis in range(3) if axis != dropped_axis]
    first = [vertices[index][axes[0]] for index in face]
    second = [vertices[index][axes[1]] for index in face]
    return min(first), max(first), min(second), max(second)


def intentional_seam_reason(
    first_name: str,
    second_name: str,
    source_axis: int,
    source_plane: float,
) -> str:
    # Source Z is Unity Y. Downward faces at zero are buried in the plateau,
    # never sampled by the camera, and intentionally share the support plane.
    if source_axis == 2 and abs(source_plane) <= 0.0005:
        return "buried_ground_contact"
    pair = frozenset((first_name, second_name))
    if first_name == second_name == "Cafe_OpaqueWalls":
        return "blind_wall_pier_buried_join"
    allowed = {
        frozenset(("Cafe_Roof", "Cafe_OpaqueWalls")):
            "roof_bearing_contact",
        frozenset(("Cafe_OpaqueWalls", "Cafe_GlazedPlinth")):
            "south_pier_plinth_buried_end_cap",
        frozenset(("Cafe_OpaqueWalls", "Cafe_DeepFascia")):
            "south_pier_fascia_buried_end_cap",
        frozenset(("Cafe_OpaqueWalls", "Cafe_WindowRails")):
            "south_pier_rail_buried_end_cap",
        frozenset(("Cafe_Mullions", "Cafe_WindowRails")):
            "orthogonal_frame_tenon_contact",
        frozenset(("Cafe_GlazedPlinth", "Cafe_WindowRails")):
            "rail_on_plinth_bearing_contact",
        frozenset(("Cafe_DeepFascia", "Cafe_WindowRails")):
            "rail_under_fascia_bearing_contact",
        frozenset(("Cafe_WindowRails", "Cafe_InteriorLining")):
            "lining_frame_buried_return",
    }
    if pair in allowed:
        return allowed[pair]
    if ((first_name.startswith("Cafe_Glass_") and
         second_name == "Cafe_LuminousBand") or
        (second_name.startswith("Cafe_Glass_") and
         first_name == "Cafe_LuminousBand")):
        return "glass_band_buried_end_cap"
    return ""


def broad_coplanar_overlaps(asset: AssetBuild) -> tuple[list[dict], list[dict]]:
    """Reject same-facing broad layers; edge and point seams have zero area.

    This intentionally runs after Blender object parenting has been resolved,
    so a hidden local mesh cannot evade the audit by living below a prop root.
    Opposing faces are ordinary solid contacts and are not presentation-layer
    z-fighting. Hidden carry props are also excluded from the visible audit.
    """
    faces_by_part: list[tuple[Part, list[tuple]]] = []
    for part in asset.parts:
        if not part.initially_visible:
            continue
        geometry = world_source_geometry(part)
        vertices, faces = geometry
        records: list[tuple] = []
        for face_index, face in enumerate(faces):
            normal = face_normal(vertices, face)
            if normal is None:
                continue
            dominant = max(range(3), key=lambda axis: abs(normal[axis]))
            # Axis-aligned and faceted architectural planes are audited; tiny
            # curved prop facets cannot form a broad competing layer.
            if abs(normal[dominant]) < 0.965:
                continue
            plane = sum(vertices[index][dominant] for index in face) / len(face)
            bounds = projected_face_bounds(vertices, face, dominant)
            records.append((face_index, normal, dominant, plane, bounds))
        faces_by_part.append((part, records))

    overlaps: list[dict] = []
    allowed_seams: list[dict] = []
    for first_index in range(len(faces_by_part)):
        first_part, first_faces = faces_by_part[first_index]
        for second_index in range(first_index, len(faces_by_part)):
            second_part, second_faces = faces_by_part[second_index]
            for first_face, first_normal, first_axis, first_plane, first_bounds in first_faces:
                for second_face, second_normal, second_axis, second_plane, second_bounds in second_faces:
                    if first_index == second_index and second_face <= first_face:
                        continue
                    if first_axis != second_axis:
                        continue
                    dot = sum(a * b for a, b in zip(first_normal, second_normal))
                    if dot < 0.999:
                        continue
                    separation = abs(first_plane - second_plane)
                    if separation > 0.0005:
                        continue
                    overlap_first = min(first_bounds[1], second_bounds[1]) - max(first_bounds[0], second_bounds[0])
                    overlap_second = min(first_bounds[3], second_bounds[3]) - max(first_bounds[2], second_bounds[2])
                    if overlap_first <= 1e-5 or overlap_second <= 1e-5:
                        continue
                    area = overlap_first * overlap_second
                    if area <= 0.002:
                        continue
                    record = {
                        "first_part": first_part.name,
                        "first_face": first_face,
                        "second_part": second_part.name,
                        "second_face": second_face,
                        "source_axis": first_axis,
                        "plane": stable((first_plane + second_plane) * 0.5),
                        "overlap_area_m2": stable(area),
                    }
                    reason = intentional_seam_reason(
                        first_part.name,
                        second_part.name,
                        first_axis,
                        (first_plane + second_plane) * 0.5,
                    )
                    if reason:
                        record["reason"] = reason
                        allowed_seams.append(record)
                    else:
                        overlaps.append(record)
    return overlaps, allowed_seams


def validate(asset: AssetBuild, textures: Sequence[dict]) -> dict:
    problems: list[str] = []
    names: set[str] = set()
    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate part name '{part.name}'")
        names.add(part.name)
        if part.sheet not in ALLOWED_SHEETS:
            problems.append(f"'{part.name}' has unsupported sheet '{part.sheet}'")
        if bp.signed_volume(part.local_geometry) <= 0.0:
            problems.append(f"'{part.name}' has inverted or open authored winding")
        if part.obj.data.uv_layers.get("UVMap") is None:
            problems.append(f"'{part.name}' has no UVMap")

    required_roles = {
        "floor", "roof", "opaque_wall", "door_header_wall",
        "facade_plinth", "facade_fascia",
        "glass", "window_frame", "door_glass", "door_frame",
        "practical_emission", "interior_wall", "counter_base", "counter_top",
        "counter_rail", "stool_metal", "stool_seat", "coffee_urn",
        "cup_ceramic", "cup_saucer", "coffee_liquid", "service_pot",
        "service_towel", "pour_stream", "service_cabinet", "service_worktop",
        "kitchen_cabinet_front", "kitchen_cabinet_handle",
        "kitchen_backsplash", "stove", "frying_pan", "cutting_board",
        "stove_task_fixture", "stove_task_lens", "refrigerator_body",
        "refrigerator_cavity", "refrigerator_shelf", "fridge_door",
        "menu_cover", "menu_pages",
    }
    roles = {part.role for part in asset.parts}
    for role in sorted(required_roles - roles):
        problems.append(f"the cafe has no '{role}' geometry")

    required_part_names = {
        "Cafe_ServiceCabinet", "Cafe_ServiceWorktop",
        "Cafe_KitchenCabinetFronts", "Cafe_KitchenHandles",
        "Cafe_KitchenBacksplash", "Cafe_Stove", "Cafe_FryingPan",
        "Cafe_CuttingBoard", "Cafe_StoveTaskFixture",
        "Cafe_StoveTaskLens", "Cafe_RefrigeratorBody",
        "Cafe_RefrigeratorCavity", "Cafe_RefrigeratorShelves",
        "Cafe_FridgeDoor", "Cafe_CoffeeUrns", "Cafe_RearDoor",
        "Cafe_MenuCover", "Cafe_MenuPages",
    }
    for name in sorted(required_part_names - names):
        problems.append(f"required cafe part '{name}' is missing")

    if len(STOOL_STATIONS) != 7:
        problems.append("the authored counter must retain exactly seven stools")
    if len([prop for prop in asset.props.values() if prop.role == "cup_assembly"]) != 2:
        problems.append("the cafe must expose exactly two cup assemblies")
    if len(asset.parts) != EXPECTED_MESH_COUNT:
        problems.append(
            f"the cafe has {len(asset.parts)} meshes; expected "
            f"{EXPECTED_MESH_COUNT}"
        )
    if len(asset.props) != EXPECTED_PROP_COUNT:
        problems.append(
            f"the cafe has {len(asset.props)} dynamic props; expected "
            f"{EXPECTED_PROP_COUNT}"
        )
    if len(COLLIDER_DESCRIPTORS) != 17:
        problems.append("the passive model must publish exactly 17 collider descriptors")

    required_anchors = {
        "Origin", "DoorThreshold", "DoorApproach", "CanonicalCameraTarget",
        "GlassCorner",
        "CounterStart", "CounterCorner", "CounterEnd", "HeroSeat",
        "Cup.PairMan", "Cup.PairWoman", "PotDock", "PotSpout",
        "FridgeDoorPivot", "Grip.FridgeDoor", "CuttingBoardDock",
        "StovePanDock", "MenuDock.Hero", "Grip.HeroMenu",
        "MenuText.Item.00", "MenuText.Item.01", "MenuText.Item.02",
        "MenuText.Selection", "ServiceRail.Hero",
        "Cast.Lone", "Cast.PairMan", "Cast.PairWoman", "Cast.Attendant",
        "Light.WarmCounter", "Light.ColdService", "Light.ExteriorWash",
        "Audio.Fridge", "Audio.Fixture", "Audio.Boiler",
    }
    for name in sorted(required_anchors - set(asset.anchors)):
        problems.append(f"required anchor '{name}' is missing")
    if len(asset.anchors) != EXPECTED_ANCHOR_COUNT:
        problems.append(
            f"the cafe has {len(asset.anchors)} anchors; expected "
            f"{EXPECTED_ANCHOR_COUNT}"
        )

    door = asset.anchors.get("DoorThreshold")
    if door is None or door.unity_position != DOOR_CENTER:
        problems.append("door threshold moved away from the terminal plan")

    # Door-ready means a real hinge hierarchy and measured leaf, not runtime
    # behaviour. Grip.FridgeDoor is intentionally child-local so the current
    # importer can bind it to the same passive dynamic-prop pattern as cups.
    fridge_prop = asset.props.get("FridgeDoor")
    fridge_door_part = next(
        (part for part in asset.parts if part.name == "Cafe_FridgeDoor"), None)
    fridge_grip = asset.anchors.get("Grip.FridgeDoor")
    fridge_pivot = asset.anchors.get("FridgeDoorPivot")
    if (fridge_prop is None or fridge_prop.role != "fridge_door" or
            fridge_prop.owner != "FridgeDoor" or
            fridge_prop.part_names != ["Cafe_FridgeDoor"]):
        problems.append("FridgeDoor dynamic prop contract is incomplete")
    elif max(
        abs(actual - expected)
        for actual, expected in zip(
            fridge_prop.root.location,
            unity_to_source(FRIDGE_DOOR_PIVOT),
        )
    ) > 1e-6:
        problems.append("FridgeDoor root moved away from its authored hinge")
    if (fridge_door_part is None or fridge_prop is None or
            fridge_door_part.obj.parent is not fridge_prop.root):
        problems.append("Cafe_FridgeDoor is not parented directly to its hinge root")
    if (fridge_grip is None or fridge_prop is None or
            fridge_grip.obj.parent is not fridge_prop.root or
            fridge_grip.unity_position != FRIDGE_GRIP_LOCAL):
        problems.append("Grip.FridgeDoor is not a child-local handle contact")
    if (fridge_pivot is None or
            fridge_pivot.unity_position != FRIDGE_DOOR_PIVOT):
        problems.append("FridgeDoorPivot anchor moved away from the hinge")

    # Menu.Hero is geometry-only but has the complete hierarchy needed for a
    # later delivery/reveal pass. Page text sockets stay child-local to the
    # prop and publish the real tilted page normal, so presentation code does
    # not need to guess the authored open-book angle.
    menu_prop = asset.props.get("Menu.Hero")
    menu_cover = next(
        (part for part in asset.parts if part.name == "Cafe_MenuCover"), None)
    menu_pages = next(
        (part for part in asset.parts if part.name == "Cafe_MenuPages"), None)
    if (menu_prop is None or menu_prop.role != "menu_booklet" or
            menu_prop.owner != "HeroMenu" or
            menu_prop.part_names != ["Cafe_MenuCover", "Cafe_MenuPages"] or
            menu_prop.lift_root is not None or menu_prop.liquid_part):
        problems.append("Menu.Hero passive dynamic-prop contract is incomplete")
    elif max(
        abs(actual - expected)
        for actual, expected in zip(
            menu_prop.root.location,
            unity_to_source(MENU_DOCK),
        )
    ) > 1e-6:
        problems.append("Menu.Hero root moved away from its authored dock")
    for part, expected_sheet in (
        (menu_cover, "CafeCounterDetail"),
        (menu_pages, "CafePropsDetail"),
    ):
        if (part is None or menu_prop is None or
                part.obj.parent is not menu_prop.root or
                part.group != "dynamic_prop" or part.initially_visible or
                part.sheet != expected_sheet):
            problems.append(
                f"{part.name if part else 'menu part'} is not an initially "
                "hidden child of Menu.Hero"
            )

    menu_dock = asset.anchors.get("MenuDock.Hero")
    menu_grip = asset.anchors.get("Grip.HeroMenu")
    hero_service_rail = asset.anchors.get("ServiceRail.Hero")
    if (menu_dock is None or menu_dock.obj.parent is not asset.root or
            menu_dock.role != "menu_dock" or
            menu_dock.unity_position != MENU_DOCK):
        problems.append("MenuDock.Hero is not the root-level menu delivery dock")
    if (menu_grip is None or menu_prop is None or
            menu_grip.obj.parent is not menu_prop.root or
            menu_grip.role != "menu_grip" or
            menu_grip.unity_position != MENU_GRIP_LOCAL):
        problems.append("Grip.HeroMenu is not a child-local menu contact")
    if (hero_service_rail is None or
            hero_service_rail.obj.parent is not asset.root or
            hero_service_rail.role != "service_rail" or
            hero_service_rail.unity_position != HERO_SERVICE_RAIL):
        problems.append("ServiceRail.Hero moved away from the hero service mark")

    _, expected_page_y_offset = menu_leaf_geometry(
        1,
        MENU_PAGE_WIDTH,
        MENU_PAGE_DEPTH,
        MENU_PAGE_THICKNESS,
        MENU_PAGE_GAP,
        MENU_PAGE_MIN_Y,
    )
    for name, (local_x, local_z) in MENU_TEXT_POSITIONS.items():
        anchor = asset.anchors.get(name)
        expected_role = (
            "menu_text_selection"
            if name == "MenuText.Selection"
            else "menu_text_item"
        )
        expected_position = (
            local_x,
            menu_right_page_surface_y(local_x, expected_page_y_offset)
            + MENU_TEXT_CLEARANCE,
            local_z,
        )
        if (anchor is None or menu_prop is None or
                anchor.obj.parent is not menu_prop.root or
                anchor.role != expected_role or
                max(abs(actual - expected) for actual, expected in zip(
                    anchor.unity_position, expected_position)) > 1e-6 or
                max(abs(actual - expected) for actual, expected in zip(
                    anchor.unity_forward, MENU_PAGE_FORWARD)) > 1e-6 or
                anchor.unity_up != MENU_PAGE_UP):
            problems.append(f"{name} does not follow the authored right page")

    if menu_cover is not None and menu_pages is not None:
        cover_low, cover_high = unity_world_bounds(menu_cover)
        pages_low, pages_high = unity_world_bounds(menu_pages)
        menu_low = tuple(
            min(cover_low[index], pages_low[index]) for index in range(3))
        menu_high = tuple(
            max(cover_high[index], pages_high[index]) for index in range(3))
        support_gap = menu_low[1] - 1.02
        if not (0.0035 <= support_gap <= 0.0045):
            problems.append(f"Menu.Hero support gap is {support_gap:.4f} m")
        if (menu_low[0] < MENU_DOCK[0] - 0.28 or
                menu_high[0] > MENU_DOCK[0] + 0.28 or
                menu_low[2] < MENU_DOCK[2] - 0.161 or
                menu_high[2] > MENU_DOCK[2] + 0.161):
            problems.append("Menu.Hero leaves its 0.54 x 0.32 m authored footprint")

    counter_details = next(
        (part for part in asset.parts if part.name == "Cafe_CounterDetails"),
        None,
    )
    if counter_details is None:
        problems.append("Cafe_CounterDetails is missing")
    else:
        hero_detail_vertices = [
            vertex for vertex in counter_details.local_geometry[0]
            if vertex[0] < 0.5
        ]
        detail_center_z = (
            min(vertex[1] for vertex in hero_detail_vertices)
            + max(vertex[1] for vertex in hero_detail_vertices)
        ) * 0.5
        if abs(detail_center_z - HERO_COUNTER_DETAIL_Z) > 1e-6:
            problems.append("hero napkin/sugar/salt cluster left z=-0.88 m")

    parts_by_name = {part.name: part for part in asset.parts}
    if not (required_part_names - names):
        measured = {
            name: unity_world_bounds(parts_by_name[name])
            for name in required_part_names
        }
        cabinet_low, cabinet_high = measured["Cafe_ServiceCabinet"]
        worktop_low, worktop_high = measured["Cafe_ServiceWorktop"]
        stove_low, stove_high = measured["Cafe_Stove"]
        pan_low, pan_high = measured["Cafe_FryingPan"]
        board_low, board_high = measured["Cafe_CuttingBoard"]
        backsplash_low, backsplash_high = measured["Cafe_KitchenBacksplash"]
        fixture_low, fixture_high = measured["Cafe_StoveTaskFixture"]
        lens_low, lens_high = measured["Cafe_StoveTaskLens"]
        fridge_low, fridge_high = measured["Cafe_RefrigeratorBody"]
        cavity_low, cavity_high = measured["Cafe_RefrigeratorCavity"]
        shelves_low, shelves_high = measured["Cafe_RefrigeratorShelves"]
        authored_door_low, authored_door_high = measured["Cafe_FridgeDoor"]
        rear_door_low, _ = measured["Cafe_RearDoor"]
        urn_low, _ = measured["Cafe_CoffeeUrns"]

        if abs(cabinet_low[1]) > 0.001 or abs(fridge_low[1]) > 0.001:
            problems.append("kitchen cabinet and refrigerator must stand on the floor")
        for supported_name, supported_low in (
            ("stove", stove_low[1]),
            ("cutting board", board_low[1]),
        ):
            support_gap = supported_low - worktop_high[1]
            if not (OPAQUE_DETAIL_CLEARANCE - 0.0005 <= support_gap <= 0.004):
                problems.append(
                    f"{supported_name} support gap is {support_gap:.4f} m"
                )
        pan_support_gap = pan_low[1] - stove_high[1]
        if abs(pan_support_gap) > 0.001:
            problems.append(
                f"frying pan leaves its burner by {pan_support_gap:.4f} m"
            )

        for surface_name, surface_high, expected_gap in (
            ("service cabinet", cabinet_high, KITCHEN_CABINET_WALL_GAP),
            ("service worktop", worktop_high, KITCHEN_WALL_GAP),
            ("kitchen backsplash", backsplash_high, KITCHEN_WALL_GAP),
            ("refrigerator", fridge_high, KITCHEN_WALL_GAP),
        ):
            wall_gap = REAR_LINING_FRONT_Z - surface_high[2]
            if abs(wall_gap - expected_gap) > 0.001:
                problems.append(
                    f"{surface_name} wall gap is {wall_gap:.4f} m"
                )
        for surface_name, surface_high in (
            ("service cabinet", cabinet_high),
            ("service worktop", worktop_high),
            ("kitchen backsplash", backsplash_high),
        ):
            rear_door_gap = rear_door_low[0] - surface_high[0]
            if rear_door_gap < 0.08:
                problems.append(
                    f"{surface_name} leaves only {rear_door_gap:.3f} m "
                    "before the rear door"
                )

        if cabinet_low[2] < 3.50 - 0.001 or worktop_low[2] < 3.45 - 0.001:
            problems.append("expanded kitchen run intrudes into the old service aisle")
        if pan_low[2] < 3.25:
            problems.append("frying-pan handle intrudes into the service aisle")
        fridge_to_cabinet = cabinet_low[0] - fridge_high[0]
        if fridge_to_cabinet < 0.45:
            problems.append(
                f"fridge-to-cabinet clearance is {fridge_to_cabinet:.3f} m"
            )
        stove_to_board = board_low[0] - stove_high[0]
        board_to_urn = urn_low[0] - board_high[0]
        if stove_to_board < 0.30 or board_to_urn < 0.45:
            problems.append(
                "cutting-board prep zone has no clean appliance clearance"
            )
        if not (
            worktop_low[0] <= stove_low[0] <= stove_high[0] <= worktop_high[0]
            and worktop_low[2] <= stove_low[2] <= stove_high[2] <= worktop_high[2]
            and worktop_low[0] <= board_low[0] <= board_high[0] <= worktop_high[0]
            and worktop_low[2] <= board_low[2] <= board_high[2] <= worktop_high[2]
        ):
            problems.append("stove or cutting board leaves the authored worktop")

        fixture_center_x = (fixture_low[0] + fixture_high[0]) * 0.5
        fixture_center_z = (fixture_low[2] + fixture_high[2]) * 0.5
        stove_center_x = (stove_low[0] + stove_high[0]) * 0.5
        # Front controls make stove Z bounds asymmetric, so compare against
        # the authored burner centre rather than their decorative protrusion.
        if (abs(fixture_center_x - stove_center_x) > 0.015 or
                abs(fixture_center_z - STOVE_CENTER[1]) > 0.015 or
                lens_low[1] - pan_high[1] < 0.90 or
                abs(fixture_high[1] - 4.18) > 0.002):
            problems.append("task fixture is not supported directly above the stove")
        cold_anchor = asset.anchors.get("Light.ColdService")
        if cold_anchor is None or not all(
            low_value - 1e-6 <= value <= high_value + 1e-6
            for value, low_value, high_value in zip(
                cold_anchor.unity_position, lens_low, lens_high)
        ):
            problems.append("Light.ColdService is not inside the visible task lens")
        lens_part = parts_by_name["Cafe_StoveTaskLens"]
        if (not lens_part.emissive or lens_part.casts_shadows or
                lens_part.sheet != "CafePropsDetail"):
            problems.append(
                "stove task lens is not a shadowless cold-emission surface"
            )

        if not (
            fridge_low[0] + 0.08 < cavity_low[0] < cavity_high[0] < fridge_high[0] - 0.08
            and fridge_low[1] < cavity_low[1] < cavity_high[1] < fridge_high[1]
            and fridge_low[2] < shelves_low[2] < shelves_high[2] < cavity_high[2]
        ):
            problems.append("refrigerator cavity or shelves leave the body shell")
        if (abs(authored_door_high[2] - fridge_low[2]) > 0.002 or
                authored_door_low[0] < fridge_low[0] or
                authored_door_high[0] > fridge_high[0]):
            problems.append("closed refrigerator door does not seat against its body")

        # Prove the +90 degree outward leaf pose without authoring animation.
        # Unity yaw maps local (x,z) to (z,-x) at +90 degrees.
        if fridge_door_part is not None:
            open_vertices = []
            for source_x, source_y, source_z in fridge_door_part.local_geometry[0]:
                local_x, local_y, local_z = source_x, source_z, source_y
                open_vertices.append((
                    FRIDGE_DOOR_PIVOT[0] + local_z,
                    FRIDGE_DOOR_PIVOT[1] + local_y,
                    FRIDGE_DOOR_PIVOT[2] - local_x,
                ))
            open_low, open_high = kit.bounds((open_vertices, []))
            if (open_low[0] < -5.10 or open_low[2] < 3.20 or
                    open_high[0] > cabinet_low[0] - 0.45):
                problems.append("90-degree refrigerator door sweep loses clearance")

    static_geometry = merge(
        world_source_geometry(part)
        for part in asset.parts
        if part.group != "dynamic_prop"
    )
    low, high = kit.bounds(static_geometry)
    unity_low = (low[0], low[2], low[1])
    unity_high = (high[0], high[2], high[1])
    if unity_low[1] < -0.001 or unity_high[1] > HEIGHT + 0.001:
        problems.append(
            f"static Y bounds {unity_low[1]:.4f}..{unity_high[1]:.4f} leave 0..{HEIGHT}"
        )
    if len(asset.parts) > 90:
        problems.append(f"the cafe fragments into {len(asset.parts)} renderers against 90")
    triangles = sum(kit.triangle_count(part.local_geometry) for part in asset.parts)
    if triangles != EXPECTED_TRIANGLE_COUNT:
        problems.append(
            f"the cafe has {triangles} triangles; expected "
            f"{EXPECTED_TRIANGLE_COUNT}"
        )
    if triangles > 45000:
        problems.append(f"the cafe costs {triangles} triangles against 45000")

    for part in asset.parts:
        if part.role not in APPLIANCE_ENAMEL_ROLES:
            continue
        if not uses_appliance_panel_patch(part):
            problems.append(
                f"{part.name} does not use the grid-free appliance panel patch"
            )
            continue
        layer = part.obj.data.uv_layers.active
        region_u, region_v, region_span_u, region_span_v = (
            appliance_panel_region(part)
        )
        if layer is None or any(
            uv.uv.x < region_u - 1e-5 or
            uv.uv.x > region_u + region_span_u + 1e-5 or
            uv.uv.y < region_v - 1e-5 or
            uv.uv.y > region_v + region_span_v + 1e-5
            for uv in layer.data
        ):
            problems.append(
                f"{part.name} samples an appliance pattern edge"
            )

    overlaps, allowed_seams = broad_coplanar_overlaps(asset)
    for overlap in overlaps:
        problems.append(
            "visible broad coplanar overlap: "
            f"{overlap['first_part']}[{overlap['first_face']}] / "
            f"{overlap['second_part']}[{overlap['second_face']}] "
            f"axis={overlap['source_axis']} plane={overlap['plane']:.5f} "
            f"({overlap['overlap_area_m2']:.5f} m2)"
        )

    # Both liquids are separate, bounded inside their cups and below rim.
    for owner in CUP_STATIONS:
        prop = asset.props[f"Cup.{owner}"]
        liquid = next(part for part in asset.parts if part.name == prop.liquid_part)
        local_low, local_high = kit.bounds(liquid.local_geometry)
        liquid_center_y = liquid.unity_origin[1]
        if abs(liquid_center_y - LIQUID_FULL_LOCAL_Y) > 1e-6:
            problems.append(f"{owner} full fill is not the authored liquid height")
        if not (0.018 < LIQUID_EMPTY_LOCAL_Y < LIQUID_FULL_LOCAL_Y):
            problems.append(f"{owner} liquid fill range leaves the cup interior")
        liquid_top = liquid_center_y + local_high[2]
        if liquid_top > 0.118 - LIQUID_RIM_CLEARANCE + 1e-6:
            problems.append(f"{owner} liquid rises into the cup rim")
        radius = max(abs(local_low[0]), abs(local_high[0]))
        if radius > 0.067 - 0.008 - LIQUID_WALL_CLEARANCE + 1e-6:
            problems.append(f"{owner} liquid touches the cup wall")

    texture_names = {record["sheet"] for record in textures}
    if texture_names != set(TEXTURE_FILES):
        problems.append("detail texture record set drifted")
    for record in textures:
        if len(record["sha256"]) != 64:
            problems.append(f"texture {record['sheet']} has no SHA-256")

    if problems:
        raise SystemExit("Mountain Road cafe validation failed:\n  " + "\n  ".join(problems))
    return {
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
        "mesh_count": len(asset.parts),
        "triangle_count": triangles,
        "overlap_count": len(overlaps),
        "allowed_seam_contact_count": len(allowed_seams),
    }


def signature_for(asset: AssetBuild, textures: Sequence[dict]) -> str:
    payload = {
        "design_id": DESIGN_ID,
        "generator_version": GENERATOR_VERSION,
        "footprint": FOOTPRINT,
        "height": HEIGHT,
        "door": [DOOR_CENTER, DOOR_WIDTH, DOOR_HEIGHT],
        "stools": STOOL_STATIONS,
        "parts": [
            {
                "name": part.name,
                "role": part.role,
                "group": part.group,
                "sheet": part.sheet,
                "emissive": part.emissive,
                "casts_shadows": part.casts_shadows,
                "initially_visible": part.initially_visible,
                "unity_origin": part.unity_origin,
                "vertices": [[stable(value) for value in vertex]
                             for vertex in part.local_geometry[0]],
                "faces": [list(face) for face in part.local_geometry[1]],
            }
            for part in asset.parts
        ],
        "anchors": {
            name: {
                "position": anchor.unity_position,
                "forward": anchor.unity_forward,
                "up": anchor.unity_up,
            }
            for name, anchor in sorted(asset.anchors.items())
        },
        "props": {
            name: {
                "role": prop.role,
                "owner": prop.owner,
                "parts": prop.part_names,
                "lift_root": prop.lift_root.name if prop.lift_root else "",
                "liquid": prop.liquid_part,
                "empty_y": prop.empty_local_y,
                "full_y": prop.full_local_y,
                "root_source_position": [stable(value) for value in prop.root.location],
            }
            for name, prop in sorted(asset.props.items())
        },
        "colliders": COLLIDER_DESCRIPTORS,
        "textures": [{"sheet": item["sheet"], "sha256": item["sha256"]}
                     for item in textures],
    }
    encoded = json.dumps(payload, ensure_ascii=False, sort_keys=True,
                         separators=(",", ":"))
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def part_world_bounds(part: Part) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    return kit.bounds(world_source_geometry(part))


def manifest_for(
    asset: AssetBuild,
    report: dict,
    signature: str,
    textures: Sequence[dict],
) -> dict:
    return {
        "generator": "tools/build-mountain-road-cafe-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "reference_composition": "Nighthawks spatial translation; no copied branding or text",
        "logical_footprint_unity_xz": [[stable(x), stable(z)] for x, z in FOOTPRINT],
        "dimensions_m": {"width": 9.8, "depth": 10.0, "height": HEIGHT},
        "door_opening_m": {"width": DOOR_WIDTH, "height": DOOR_HEIGHT},
        "stool_count": len(STOOL_STATIONS),
        "cup_assembly_count": sum(
            prop.role == "cup_assembly" for prop in asset.props.values()
        ),
        "source_axes": {"right": "+X", "forward": "+Y", "up": "+Z"},
        "unity_axes": {
            "right": "+X", "forward": "+Z", "up": "+Y",
            "fbx_axis_forward": "-Z", "fbx_axis_up": "Y",
            "bake_space_transform": False,
        },
        "root_contract": {
            "origin": "terminal_cafe_plan_center_ground",
            "scale_mode": "fixed_meters",
            "axis_conversion": "swap_y_z_and_reverse_winding",
            "fbx_imported_root_scale": 100.0,
            "reparent_world_position_stays": True,
        },
        "colliders": False,
        "lights": False,
        "cameras": False,
        "materials": False,
        "animation_count": 0,
        "kitchen_contract": {
            "fridge_door_dynamic_prop": "FridgeDoor",
            "fridge_door_runtime_driver": False,
            "fridge_door_pivot_anchor": "FridgeDoorPivot",
            "fridge_door_grip_anchor": "Grip.FridgeDoor",
            "cutting_board_dock_anchor": "CuttingBoardDock",
            "stove_pan_dock_anchor": "StovePanDock",
            "cold_service_light_anchor": "Light.ColdService",
            "runtime_light_count_delta": 0,
            "visible_rear_lining_z_m": REAR_LINING_FRONT_Z,
            "worktop_wall_gap_m": KITCHEN_WALL_GAP,
            "cabinet_wall_gap_m": KITCHEN_CABINET_WALL_GAP,
            "rear_door_clearance_m": 0.08,
        },
        "menu_contract": {
            "dynamic_prop": "Menu.Hero",
            "owner": "HeroMenu",
            "initially_visible": False,
            "runtime_driver": False,
            "dock_anchor": "MenuDock.Hero",
            "grip_anchor": "Grip.HeroMenu",
            "service_rail_anchor": "ServiceRail.Hero",
            "item_anchors": [
                "MenuText.Item.00",
                "MenuText.Item.01",
                "MenuText.Item.02",
            ],
            "selection_anchor": "MenuText.Selection",
            "text_forward_local": [stable(value) for value in MENU_PAGE_FORWARD],
            "text_up_local": [stable(value) for value in MENU_PAGE_UP],
            "text_line_width_m": 0.18,
            "text_line_height_m": 0.028,
        },
        "surface_contract": {
            "opaque_detail_min_clearance_m": OPAQUE_DETAIL_CLEARANCE,
            "glass_layers_per_pane": 1,
            "liquid_wall_clearance_m": LIQUID_WALL_CLEARANCE,
            "liquid_rim_clearance_m": LIQUID_RIM_CLEARANCE,
            "detail_atlases_add_no_new_palette_family": True,
            "appliance_reference": "home_refrigerator_enamel_parameters",
            "appliance_pattern_edges_sampled": False,
            "broad_coplanar_overlap_count": report["overlap_count"],
            "allowed_buried_seam_contact_count":
                report["allowed_seam_contact_count"],
            "broad_overlap_min_area_m2": 0.002,
        },
        "budgets": {
            "maximum_renderers": 90,
            "maximum_triangles": 45000,
            "maximum_textures": 6,
            "runtime_collider_descriptors": 17,
            "imported_colliders": 0,
            "imported_lights": 0,
            "imported_cameras": 0,
        },
        "bounds_min": report["bounds_min"],
        "bounds_max": report["bounds_max"],
        "mesh_count": report["mesh_count"],
        "triangle_count": report["triangle_count"],
        "overlap_count": report["overlap_count"],
        "allowed_seam_contact_count": report["allowed_seam_contact_count"],
        "textures": list(textures),
        "collider_descriptors": list(COLLIDER_DESCRIPTORS),
        "anchors": [
            {
                "name": name,
                "role": anchor.role,
                "local_position": [stable(value) for value in anchor.obj.location],
                "unity_local_position": [stable(value) for value in anchor.unity_position],
                "unity_local_forward": [stable(value) for value in anchor.unity_forward],
                "unity_local_up": [stable(value) for value in anchor.unity_up],
            }
            for name, anchor in sorted(asset.anchors.items())
        ],
        "dynamic_props": [
            {
                "name": name,
                "root_name": prop.root.name,
                "lift_root_name": prop.lift_root.name if prop.lift_root else "",
                "role": prop.role,
                "owner": prop.owner,
                "part_names": list(prop.part_names),
                "liquid_part": prop.liquid_part,
                "empty_local_y": stable(prop.empty_local_y),
                "full_local_y": stable(prop.full_local_y),
            }
            for name, prop in sorted(asset.props.items())
        ],
        "parts": [
            {
                "name": part.name,
                "role": part.role,
                "group": part.group,
                "sheet": part.sheet,
                "base_surface": BASE_SURFACE[part.sheet],
                "uv_strategy": (
                    "single_inset_appliance_patch_without_pattern_edges"
                    if uses_appliance_panel_patch(part)
                    else "semantic_sha256_quarter_turn_plus_offset_metres"
                    if part.sheet not in {
                        "CafePropsDetail", "CafeGlassDetail",
                        "CafeWarmEmission", "CafeCoffee"
                    }
                    else "unique_clamped_0_1_with_2_percent_gutter"
                ),
                "uv_transform": {
                    "quarter_turns": uv_transform_for(part.name)[0],
                    "offset_u": stable(uv_transform_for(part.name)[1]),
                    "offset_v": stable(uv_transform_for(part.name)[2]),
                },
                "emissive": part.emissive,
                "shadows": part.casts_shadows,
                "initially_visible": part.initially_visible,
                "vertices": len(part.local_geometry[0]),
                "triangles": kit.triangle_count(part.local_geometry),
                "bounds_min": [stable(value) for value in part_world_bounds(part)[0]],
                "bounds_max": [stable(value) for value in part_world_bounds(part)[1]],
            }
            for part in asset.parts
        ],
        "build_signature": signature,
    }


def write_manifest(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8")


def export_fbx(asset: AssetBuild, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in asset.collection.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = asset.root
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,
        add_leaf_bones=False,
        bake_anim=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_custom_props=True,
        path_mode="STRIP",
        embed_textures=False,
    )


def aim_at(obj: "bpy.types.Object", target: Sequence[float]) -> None:
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def render_preview(asset: AssetBuild, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.new("PRESENTATION_MountainRoadCafe3D")
    scene.collection.children.link(presentation)
    ground_mesh = bpy.data.meshes.new("MRC_PreviewGround_Mesh")
    ground_geometry = kit.box((0.0, 0.0, -0.10), (24.0, 24.0, 0.18))
    ground_mesh.from_pydata(ground_geometry[0], [], ground_geometry[1])
    ground = bpy.data.objects.new("MRC_PreviewGround", ground_mesh)
    presentation.objects.link(ground)
    ground_material = bpy.data.materials.new("PREVIEW_MRC_Ground")
    ground_material.diffuse_color = (0.035, 0.045, 0.043, 1.0)
    ground.data.materials.append(ground_material)
    for name, energy, color, location, size in (
        ("WarmInterior", 1250.0, (1.0, 0.58, 0.20), (0.2, -0.4, 3.05), 5.0),
        ("ColdRim", 850.0, (0.38, 0.58, 0.70), (-4.5, -6.0, 5.8), 4.0),
        ("Service", 700.0, (0.70, 0.82, 0.72), (2.3, 3.6, 3.2), 2.5),
    ):
        data = bpy.data.lights.new(f"PREVIEW_MRC_{name}", "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(f"PREVIEW_MRC_{name}", data)
        presentation.objects.link(light)
        light.location = location
        aim_at(light, (0.0, 0.0, 1.3))
    camera_data = bpy.data.cameras.new("PREVIEW_MRC_Camera")
    camera = bpy.data.objects.new("PREVIEW_MRC_Camera", camera_data)
    presentation.objects.link(camera)
    camera.location = (-9.8, -12.8, 4.0)
    camera_data.lens = 48.0
    aim_at(camera, (0.15, -0.65, 1.55))
    scene.camera = camera
    world = bpy.data.worlds.new("PREVIEW_MRC_World")
    world.color = (0.008, 0.012, 0.014)
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 960
    scene.render.resolution_y = 540
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    scene.render.film_transparent = False
    bpy.ops.render.render(write_still=True)
    for obj in list(presentation.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(presentation)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend", type=Path, default=DEFAULT_BLEND)
    parser.add_argument("--fbx", type=Path, default=DEFAULT_FBX)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--preview", type=Path, default=DEFAULT_PREVIEW)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--skip-preview", action="store_true")
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(arguments)


def main() -> None:
    args = parse_args()
    textures = generate_textures(write=not args.validate_only)
    asset = build()
    report = validate(asset, textures)
    signature = signature_for(asset, textures)
    payload = manifest_for(asset, report, signature, textures)
    if not args.validate_only:
        export_fbx(asset, args.fbx)
        if not args.skip_preview:
            render_preview(asset, args.preview)
        save_blend(args.blend)
        write_manifest(args.manifest, payload)
    print("Mountain Road cafe authored model validated")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Meshes: {report['mesh_count']}")
    print(f"  Triangles: {report['triangle_count']}")
    print(f"  Anchors: {len(asset.anchors)}")
    print(f"  Dynamic props: {len(asset.props)}")
    print(f"  Signature: {signature}")
    if not args.validate_only:
        print(f"  FBX: {args.fbx}")
        print(f"  Manifest: {args.manifest}")
        print(f"  Blend: {args.blend}")
        if not args.skip_preview:
            print(f"  Preview: {args.preview}")


if __name__ == "__main__":
    main()
