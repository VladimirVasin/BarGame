#!/usr/bin/env python3
"""Build the two-storey fixed-camera interior of the mother's house.

The ground floor remains the quiet, ordinary lived-in room.  A real stair rises
behind its west sofa to a narrow upper corridor and exactly two furnished
rooms: the parents' bedroom to the north, still slept in, and the hero's
childhood room to the south, taken out of use and kept clean.  Story actors,
text, medicine, family photographs and the kettle are intentionally absent,
and neither bedroom carries a photograph, a letter, a named or dated object,
or anything that explains what the hero was like as a child.  The table kettle
is instantiated by Unity
from the existing kettle-head NPC prefab; this asset publishes only
``ANCHOR_TeapotDock`` for it.

Run from the repository root with Blender 5::

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-mothers-house-interior-3d-model.py -- --validate-only

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-mothers-house-interior-3d-model.py

Dimensions below are Unity-local metres: +X east, +Y up, +Z north.  Geometry
made through ``bar_parts`` is converted to Blender source space by swapping Y/Z
and re-winding every face.  Shell and profiled furniture made directly through
``interior_kit`` already use Blender's Z-up source frame.  Validation measures
both forms and rejects non-positive signed volume, mirrored anchors and drift
from the 10 x 8 m footprint contract.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Sequence

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import interior_kit as kit  # noqa: E402
import bar_parts as bp  # noqa: E402
GENERATOR_VERSION = "1.6.0"
DESIGN_ID = "mothers_house_interior_v1"
DISPLAY_NAME = "Bar Promenade Mother's House Interior"

ROOM_WIDTH = 10.0
ROOM_DEPTH = 8.0
ROOM_HEIGHT = 3.4
UPPER_FLOOR_ELEVATION = 3.54
UPPER_CEILING_HEIGHT = 5.90
WALL_THICKNESS = 0.24
FLOOR_THICKNESS = 0.18
CEILING_THICKNESS = 0.14
DOOR_CENTER_X = 0.0
DOOR_WIDTH = 1.30
DOOR_HEIGHT = 2.20
WINDOW_CENTERS_X = (-2.72, 2.72)
WINDOW_WIDTH = 1.35
WINDOW_SILL = 0.85
WINDOW_HEAD = 2.25

STAIR_CENTER_X = -4.0
STAIR_START_Z = 1.80
STAIR_DIRECTION_Z = -1.0
STAIR_STEP_COUNT = 19
STAIR_STEP_RISE = UPPER_FLOOR_ELEVATION / STAIR_STEP_COUNT
STAIR_STEP_DEPTH = 0.25
STAIR_WIDTH = 1.30
STAIR_OPENING = (-4.88, -3.05, -3.18, 1.82)
UPPER_PARTITION_X = -1.75
UPPER_PARTITION_THICKNESS = 0.16
UPPER_ROOM_DIVIDER_Z = 0.0
UPPER_DOOR_WIDTH = 1.20
UPPER_DOOR_HEIGHT = 2.20
UPPER_DOOR_CENTERS_Z = (-1.85, 1.85)

# Second storey.  The north room is the parents' bedroom and is still slept
# in; the south room is the hero's childhood room and has been taken out of
# use.  Heights below are measured from the upper floor, not from the ground.
UPPER_WINDOW_WIDTH = 1.00
UPPER_WINDOW_SILL = 0.92
UPPER_WINDOW_HEAD = 2.02
UPPER_NORTH_WINDOW_X = -1.10
UPPER_SOUTH_WINDOW_X = -0.90

WINDOW_LAYOUT_PATH = ROOT / "ArtSource/MothersHouse/WindowLayout.json"
WINDOW_RUNTIME_PATH = ROOT / "Assets/Scripts/Runtime/World/MothersHouseWindowLayout.cs"
WINDOW_LAYOUT = json.loads(WINDOW_LAYOUT_PATH.read_text(encoding="utf-8"))


def window_records() -> list[dict]:
    records = []
    for source in WINDOW_LAYOUT["windows"]:
        row = dict(source)
        wall = row["wall"]
        y = row["floor_elevation"] + (row["sill"] + row["head"]) * 0.5
        if wall in ("north", "south"):
            position = [row["across"], y, (1 if wall == "north" else -1) * 3.82]
        else:
            position = [(1 if wall == "east" else -1) * 4.82, y, row["across"]]
        row["center_unity"] = position
        row["height"] = row["head"] - row["sill"]
        row.setdefault("frame_part", "FIX_Window." + row["stable_id"] + ".Frame")
        row.setdefault("glass_part", "FIX_Window." + row["stable_id"] + ".Glass")
        records.append(row)
    return records


WINDOWS = window_records()


def window_runtime_source() -> str:
    def number(value: float) -> str:
        return f"{value:.6f}".rstrip("0").rstrip(".") + "f"

    rows = []
    for row in WINDOWS:
        x, y, z = row["center_unity"]
        rows.append(
            '            new MothersHouseWindowDescriptor(' +
            f'"{row["stable_id"]}", "{row["room"]}", ' +
            f'MothersHouseWindowWall.{row["wall"].title()},\n' +
            f'                new Vector3({number(x)}, {number(y)}, {number(z)}), ' +
            f'{number(row["width"])}, {number(row["floor_elevation"])}, ' +
            f'{number(row["sill"])}, {number(row["head"])},\n' +
            f'                "{row["frame_part"]}", "{row["glass_part"]}"),')
    return '''// Generated by tools/build-mothers-house-interior-3d-model.py.
// Edit ArtSource/MothersHouse/WindowLayout.json, then regenerate.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum MothersHouseWindowWall { North, South, East, West }

    public readonly struct MothersHouseWindowDescriptor
    {
        public MothersHouseWindowDescriptor(string stableId, string room,
            MothersHouseWindowWall wall, Vector3 centerPosition, float width,
            float floorElevation, float sill, float head,
            string framePartName, string glassPartName)
        {
            StableId = stableId;
            Room = room;
            Wall = wall;
            CenterPosition = centerPosition;
            Width = width;
            FloorElevation = floorElevation;
            Sill = sill;
            Head = head;
            FramePartName = framePartName;
            GlassPartName = glassPartName;
        }
        public string StableId { get; }
        public string Room { get; }
        public MothersHouseWindowWall Wall { get; }
        public Vector3 CenterPosition { get; }
        public float Width { get; }
        public float Height => Head - Sill;
        public float FloorElevation { get; }
        public float Sill { get; }
        public float Head { get; }
        public string FramePartName { get; }
        public string GlassPartName { get; }
        public Vector3 Outward => Wall == MothersHouseWindowWall.North ? Vector3.forward :
            Wall == MothersHouseWindowWall.South ? Vector3.back :
            Wall == MothersHouseWindowWall.East ? Vector3.right : Vector3.left;
    }

    public static class MothersHouseWindowLayout
    {
        public const int WindowCount = 16;
        public static IReadOnlyList<MothersHouseWindowDescriptor> Windows { get; } =
            Array.AsReadOnly(new[]
        {
''' + "\n".join(rows) + '''
        });
    }
}
'''

# The hearth flue stops dead at the interstorey slab.  Continuing it is what
# makes the north wall the warm one, and a real flue narrows as it climbs.
UPPER_CHIMNEY_WIDTH = 0.95
UPPER_CHIMNEY_DEPTH = 0.30
UPPER_CHIMNEY_CENTER_X = 0.0
UPPER_CHIMNEY_Z_MAX = 3.88

# Both beds stand clear of their room's own centre point: the play-mode
# test walks the controller to UpperFloorPlan.<Room>RoomCenter, and a
# capsule of radius 0.32 has to arrive there without touching furniture.
NORTH_BED_X = (2.15, 3.65)
NORTH_BED_Z = (1.85, 3.85)
NORTH_BEDSIDE_X = (1.62, 2.04)
NORTH_BEDSIDE_Z = (3.45, 3.85)
NORTH_CHEST_X = (-1.60, -0.60)
NORTH_CHEST_Z = (3.32, 3.87)
NORTH_CHEST_HEIGHT = 0.80
NORTH_RUG_X = (1.55, 2.10)
NORTH_RUG_Z = (2.00, 3.35)
UPPER_NORTH_LAMP_CENTER = (1.30, 1.70)

# The childhood room is lit too, but by a bare bulb on its flex: the shade
# came off it when the room went out of use and was never put back. Same
# wiring, different intonation.
UPPER_SOUTH_LAMP_CENTER = (1.20, -1.75)

SOUTH_BED_X = (2.15, 3.05)
SOUTH_BED_Z = (-3.80, -2.10)
SOUTH_CHAIR_X = (1.35, 1.77)
SOUTH_CHAIR_Z = (-3.55, -3.13)
SOUTH_SHELF_Z = (-3.20, -2.40)
SOUTH_SHELF_HEIGHT = 1.25
SOUTH_ROLLED_RUG_Z = (-3.60, -2.10)

# Second-storey furnishing, pass two. The divider wall at z = 0 stands almost
# square to both fixed cameras and fills a third of each frame, so it carries
# the tall pieces; the east third is the near foreground and carries shapes
# that read large. Every rect below is checked against the room centres the
# play-mode walk teleports to.
NORTH_WARDROBE_X = (2.45, 3.95)
NORTH_WARDROBE_Z = (0.14, 0.76)
NORTH_WARDROBE_HEIGHT = 1.95
NORTH_TRUNK_X = (2.25, 3.55)
NORTH_TRUNK_Z = (1.28, 1.75)
NORTH_TRUNK_HEIGHT = 0.52
NORTH_CHAIR_X = (0.72, 1.18)
NORTH_CHAIR_Z = (2.72, 3.18)
NORTH_CHAIR_HEIGHT = 0.92
NORTH_PEG_X = (0.90, 1.80)
NORTH_PEG_HEIGHT = 1.62

SOUTH_PRESS_X = (1.15, 2.25)
SOUTH_PRESS_Z = (-0.72, -0.18)
SOUTH_PRESS_HEIGHT = 1.15
SOUTH_TABLE_X = (-0.15, 0.85)
SOUTH_TABLE_Z = (-3.74, -3.16)
SOUTH_TABLE_HEIGHT = 0.62
SOUTH_TRUNK_X = (3.25, 4.15)
SOUTH_TRUNK_Z = (-3.55, -2.95)
SOUTH_TRUNK_HEIGHT = 0.55
SOUTH_BASKET_X = (3.35, 3.90)
SOUTH_BASKET_Z = (-1.60, -1.05)
SOUTH_BASKET_HEIGHT = 0.52

CORRIDOR_SHELF_Z = (-1.10, 1.10)
CORRIDOR_SHELF_HEIGHT = 1.94
CORRIDOR_PAIL_X = (-3.12, -2.72)
CORRIDOR_PAIL_Z = (-3.62, -3.28)
CORRIDOR_PAIL_HEIGHT = 0.46

# The ground floor carries a skirting and a cornice; the upper storey had
# neither, and the bare pool-of-floor-meets-wall line is what read as
# unfinished. No cornice up here - 2.36 m in the clear would only feel lower.
UPPER_SKIRTING_HEIGHT = 0.135
UPPER_SKIRTING_DEPTH = 0.022

CORRIDOR_RUNNER_X = (-3.05, -2.00)
CORRIDOR_RUNNER_Z = (-2.55, 2.40)
CORRIDOR_PEG_Z = (0.30, 1.20)
CORRIDOR_PEG_HEIGHT = 1.55
# The two upper rooms, mirrored either side of the divider.  These match
# MothersHouseInteriorLayoutPlanner.Upper{South,North}RoomBounds exactly.
UPPER_SOUTH_ROOM_X = (-1.67, 4.88)
UPPER_SOUTH_ROOM_Z = (-3.88, -0.08)
UPPER_NORTH_ROOM_X = (-1.67, 4.88)
UPPER_NORTH_ROOM_Z = (0.08, 3.88)

CORRIDOR_CHEST_X = (-3.05, -1.95)
CORRIDOR_CHEST_Z = (2.85, 3.55)
CORRIDOR_CHEST_HEIGHT = 0.55

TABLE_WIDTH = 1.45
TABLE_DEPTH = 0.90
TABLE_HEIGHT = 0.48
ROCKER_CENTER = (0.0, 0.0, 1.55)
SOFA_CENTER = (-2.48, 0.0, -0.08)
SPAWN_FORWARD_UNITY = (0.0, 0.0, 1.0)
ANCHORS_UNITY = {
    "ANCHOR_Entry": (DOOR_CENTER_X, 0.0, -3.86),
    "ANCHOR_Spawn": (DOOR_CENTER_X, 0.0, -2.45),
    "ANCHOR_Exit": (DOOR_CENTER_X, 0.0, -3.15),
    "ANCHOR_Camera": (5.80, 2.75, -2.80),
    "ANCHOR_CameraTarget": (-0.20, 0.80, 1.00),
    "ANCHOR_Fireplace": (0.0, 0.0, 3.61),
    "ANCHOR_FireLight": (0.0, 0.78, 3.28),
    "ANCHOR_Tabletop": (0.0, TABLE_HEIGHT, 0.0),
    "ANCHOR_TeapotDock": (0.18, TABLE_HEIGHT + 0.03, 0.05),
    "ANCHOR_FloorLampLight": (-1.72, 1.50, 1.45),
}

ANCHOR_ROLES = {
    "ANCHOR_Entry": "entry",
    "ANCHOR_Spawn": "spawn",
    "ANCHOR_Exit": "exit",
    "ANCHOR_Camera": "camera",
    "ANCHOR_CameraTarget": "camera_target",
    "ANCHOR_Fireplace": "fireplace",
    "ANCHOR_FireLight": "fire_light",
    "ANCHOR_Tabletop": "tabletop",
    "ANCHOR_TeapotDock": "teapot_dock",
    "ANCHOR_FloorLampLight": "floor_lamp_light",
}
DEFAULT_BLEND = (
    ROOT / "ArtSource" / "MothersHouse" / "Blender" /
    "MothersHouseInterior3D.blend"
)
DEFAULT_PREVIEW = (
    ROOT / "ArtSource" / "MothersHouse" / "Preview" /
    "MothersHouseInterior3D.png"
)
DEFAULT_FBX = (
    ROOT / "Assets" / "MothersHouse" / "Models" /
    "MothersHouseInterior3D.fbx"
)
DEFAULT_MANIFEST = (
    ROOT / "Assets" / "MothersHouse" / "Models" /
    "MothersHouseInterior3D.json"
)

# These names address cells in the mother's-house-only positive texture atlas.
# Blender keeps matching clean preview colours without embedding that runtime
# texture into the passive FBX.
SHEET_PITCH = {
    "Wallpaper": 1.90,
    "CeilingPlaster": 2.80,
    "PlankFloor": 1.60,
    "DarkWood": 1.10,
    "Upholstery": 0.85,
    "BedLinen": 0.90,
    "PaintedMetal": 1.00,
    "Concrete": 2.40,
    "Rug": 1.50,
    "Glass": 1.00,
    "Ceramic": 0.70,
    "Fire": 0.55,
    # The four reserved cells of the atlas' bottom row, drawn with the rest
    # of it and unused until the upper storey was furnished.
    "BookCloth": 0.85,
    "Wicker": 0.55,
    "TeaCloth": 0.95,
    "PaleWood": 1.15,
}

PREVIEW_COLORS = {
    "Wallpaper": (0.72, 0.64, 0.48, 1.0),
    "CeilingPlaster": (0.82, 0.77, 0.64, 1.0),
    "PlankFloor": (0.55, 0.30, 0.13, 1.0),
    "DarkWood": (0.38, 0.17, 0.07, 1.0),
    "Upholstery": (0.43, 0.58, 0.38, 1.0),
    "BedLinen": (0.69, 0.48, 0.32, 1.0),
    "PaintedMetal": (0.56, 0.44, 0.25, 1.0),
    "Concrete": (0.72, 0.66, 0.53, 1.0),
    "Rug": (0.68, 0.52, 0.32, 1.0),
    "Glass": (0.32, 0.52, 0.68, 0.62),
    "Ceramic": (0.88, 0.82, 0.69, 1.0),
    "Fire": (1.0, 0.34, 0.065, 1.0),
    "BookCloth": (0.46, 0.55, 0.66, 1.0),
    "Wicker": (0.78, 0.62, 0.34, 1.0),
    "TeaCloth": (0.86, 0.83, 0.74, 1.0),
    "PaleWood": (0.72, 0.55, 0.34, 1.0),
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
    tint: tuple[float, float, float, float]
    geometry: kit.Geometry


@dataclass
class AssetBuild:
    root: "bpy.types.Object"
    collection: "bpy.types.Collection"
    parts: list[Part] = field(default_factory=list)
    anchors: dict[str, "bpy.types.Object"] = field(default_factory=dict)


def stable(value: float) -> float:
    return round(float(value), 6)


def source_point(unity: Sequence[float]) -> tuple[float, float, float]:
    return float(unity[0]), float(unity[2]), float(unity[1])


def unity_point(source: Sequence[float]) -> tuple[float, float, float]:
    return float(source[0]), float(source[2]), float(source[1])


def merge(items: Iterable[kit.Geometry]) -> kit.Geometry:
    return kit.merge_all(items)


def ellipsoid_source(
    unity_center: Sequence[float],
    unity_radii: Sequence[float],
    segments: int = 8,
) -> kit.Geometry:
    """Closed ellipsoid with single poles and only triangular faces.

    A lathe's coincident zero-radius rows made Unity discard yarn polygons.
    """
    if segments < 5:
        raise ValueError("An ellipsoid needs at least five radial segments.")
    radius_x = float(unity_radii[0])
    radius_y = float(unity_radii[1])
    radius_z = float(unity_radii[2])
    stacks = 4
    vertices: list[tuple[float, float, float]] = [(0.0, 0.0, -radius_y)]
    rings: list[list[int]] = []
    for stack in range(1, stacks):
        latitude = -math.pi * 0.5 + math.pi * stack / stacks
        ring_scale = math.cos(latitude)
        row: list[int] = []
        for index in range(segments):
            angle = math.tau * index / segments
            row.append(len(vertices))
            vertices.append((math.cos(angle) * radius_x * ring_scale,
                             math.sin(angle) * radius_z * ring_scale,
                             math.sin(latitude) * radius_y))
        rings.append(row)
    top = len(vertices)
    vertices.append((0.0, 0.0, radius_y))
    faces: list[tuple[int, ...]] = []
    first = rings[0]
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((0, following + 1, index + 1))
    for lower, upper in zip(rings, rings[1:]):
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((lower[index], lower[following], upper[following]))
            faces.append((lower[index], upper[following], upper[index]))
    last = rings[-1]
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((top, last[index], last[following]))
    return kit.translated((vertices, faces), source_point(unity_center))


def create_material(sheet: str) -> "bpy.types.Material":
    material = bpy.data.materials.new(f"PREVIEW_MothersHouse_{sheet}")
    color = PREVIEW_COLORS[sheet]
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        base = bsdf.inputs.get("Base Color")
        roughness = bsdf.inputs.get("Roughness")
        metallic = bsdf.inputs.get("Metallic")
        alpha = bsdf.inputs.get("Alpha")
        if base is not None:
            base.default_value = color
        if roughness is not None:
            roughness.default_value = 0.82
        if metallic is not None:
            metallic.default_value = 0.32 if sheet == "PaintedMetal" else 0.0
        if alpha is not None:
            alpha.default_value = color[3]
        if sheet == "Fire":
            emission = bsdf.inputs.get("Emission Color") or bsdf.inputs.get(
                "Emission")
            strength = bsdf.inputs.get("Emission Strength")
            if emission is not None:
                emission.default_value = color
            if strength is not None:
                strength.default_value = 5.0
    if sheet == "Glass":
        if hasattr(material, "surface_render_method"):
            material.surface_render_method = "DITHERED"
        if hasattr(material, "use_transparency_overlap"):
            material.use_transparency_overlap = False
    return material


def assign_world_uv(mesh: "bpy.types.Mesh", pitch: float) -> None:
    layer = mesh.uv_layers.new(name="UVMap")
    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        for loop_index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if axis == 0:
                uv = (point.y / pitch, point.z / pitch)
            elif axis == 1:
                uv = (point.x / pitch, point.z / pitch)
            else:
                uv = (point.x / pitch, point.y / pitch)
            layer.data[loop_index].uv = uv


def add_part(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
    name: str,
    geometry: kit.Geometry,
    role: str,
    sheet: str,
    *,
    group: str = "fixed",
    emissive: bool = False,
    casts_shadows: bool = True,
    tint: Sequence[float] | None = None,
    unity_space: bool = True,
) -> "bpy.types.Object":
    if unity_space:
        geometry = bp.to_source(geometry)
    vertices, faces = geometry
    if not vertices or not faces:
        raise SystemExit(f"Part '{name}' is empty.")

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata([tuple(vertex) for vertex in vertices], [], faces)
    mesh.materials.append(materials[sheet])
    mesh.update(calc_edges=True)
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    assign_world_uv(mesh, SHEET_PITCH[sheet])

    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = asset.root
    obj["bp_role"] = role
    obj["bp_group"] = group
    obj["bp_sheet"] = sheet
    obj["bp_emissive"] = bool(emissive)
    obj["bp_casts_shadows"] = bool(casts_shadows)
    configured_tint = tuple(float(value) for value in (
        tint if tint is not None else PREVIEW_COLORS[sheet]))
    obj["bp_tint"] = list(configured_tint)
    asset.parts.append(Part(
        obj,
        name,
        role,
        group,
        sheet,
        emissive,
        casts_shadows,
        configured_tint,
        geometry,
    ))
    return obj


def add_anchor(
    asset: AssetBuild,
    name: str,
    role: str,
    unity_position: Sequence[float],
) -> None:
    anchor = bpy.data.objects.new(name, None)
    asset.collection.objects.link(anchor)
    anchor.parent = asset.root
    anchor.empty_display_type = "PLAIN_AXES"
    anchor.empty_display_size = 0.14
    anchor.location = source_point(unity_position)
    anchor["bp_role"] = role
    asset.anchors[name] = anchor


def configure_scene() -> tuple["bpy.types.Collection", "bpy.types.Object"]:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        if collection.name != "Collection":
            bpy.data.collections.remove(collection)
    base = bpy.data.collections.get("Collection")
    if base is not None:
        for obj in list(base.objects):
            base.objects.unlink(obj)

    collection = bpy.data.collections.new("MothersHouseInterior3D")
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new("MothersHouseInterior3D", None)
    collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root["bp_design_id"] = DESIGN_ID
    root["bp_generator_version"] = GENERATOR_VERSION
    return collection, root


def room_skirting_with_south_door() -> kit.Geometry:
    inset = WALL_THICKNESS * 0.5
    half_x = ROOM_WIDTH * 0.5 - inset
    half_z = ROOM_DEPTH * 0.5 - inset
    overlap = 0.026
    door_start = DOOR_CENTER_X - DOOR_WIDTH * 0.5
    door_end = DOOR_CENTER_X + DOOR_WIDTH * 0.5
    profile = kit.profile_skirting(0.15, 0.026)
    return merge((
        kit.sweep(
            ((-half_x, -half_z), (door_start - overlap, -half_z)),
            profile,
            overlap=overlap,
        ),
        kit.sweep(
            ((door_end + overlap, -half_z), (half_x, -half_z)),
            profile,
            overlap=overlap,
        ),
        kit.sweep(
            (
                (half_x, -half_z),
                (half_x, half_z),
                (-half_x, half_z),
                (-half_x, -half_z),
            ),
            profile,
            overlap=overlap,
        ),
    ))


def perimeter_wall(wall: str, floor: float, top: float, base_lift: float = 0.0) -> kit.Geometry:
    openings = [kit.Opening(row["across"], row["width"],
                            row["head"] - base_lift, row["sill"] - base_lift)
                for row in WINDOWS if row["wall"] == wall and
                abs(row["floor_elevation"] - floor) < 0.001]
    if wall == "south" and floor == 0.0:
        openings.append(kit.Opening(DOOR_CENTER_X, DOOR_WIDTH, DOOR_HEIGHT, 0.0))
    length = ROOM_WIDTH if wall in ("north", "south") else ROOM_DEPTH
    # The side cutaway split is a renderer boundary, not a construction
    # joint. Square contacting edges keep its complete wall flush; beveling
    # both contacts produces a dark horizontal groove across the wallpaper.
    geometry = kit.wall_run(length, top - floor - base_lift,
                            WALL_THICKNESS, openings,
                            chamfer=0.0 if wall in ("east", "west") else 0.008,
                            base=floor + base_lift)
    if wall in ("east", "west"):
        return kit.translated(kit.rotated_z(geometry, 90.0),
                              ((1 if wall == "east" else -1) * ROOM_WIDTH * 0.5, 0.0, 0.0))
    return kit.translated(geometry,
                          (0.0, (1 if wall == "north" else -1) * ROOM_DEPTH * 0.5, 0.0))


def build_shell(asset: AssetBuild, materials: dict) -> None:
    for wall, key in (("south", "Front"), ("north", "Back")):
        add_part(asset, materials, f"FIX_Wall.{key}",
                 perimeter_wall(wall, 0.0, ROOM_HEIGHT),
                 "room_wall", "Wallpaper", unity_space=False)

    # Complete side walls are authored with real reveals. Only their upper
    # renderers and matching frames/glass are hidden when a fixed camera is
    # outside that side. The low camera edge and player collision stay put.
    for wall, base_name in (("east", "EastCutaway"), ("west", "Left")):
        direction = 1 if wall == "east" else -1
        base = kit.chamfered_box((direction * ROOM_WIDTH * 0.5, 0.0, 0.31),
                                (WALL_THICKNESS, ROOM_DEPTH, 0.62), 0.0)
        add_part(asset, materials, f"FIX_Wall.{base_name}", base,
                 "camera_cutaway_wall" if wall == "east" else "room_wall",
                 "Wallpaper", unity_space=False)
        add_part(asset, materials, f"FIX_Wall.{wall.title()}Upper",
                 perimeter_wall(wall, 0.0, ROOM_HEIGHT, 0.62),
                 "room_wall", "Wallpaper", unity_space=False)

    floor = kit.floor_slab(
        ROOM_WIDTH,
        ROOM_DEPTH,
        FLOOR_THICKNESS,
        0.012,
    )
    add_part(
        asset,
        materials,
        "FIX_Floor",
        floor,
        "floor",
        "PlankFloor",
        unity_space=False,
    )
    add_part(
        asset,
        materials,
        "FIX_Skirting",
        room_skirting_with_south_door(),
        "skirting",
        "DarkWood",
        unity_space=False,
    )
    add_part(
        asset,
        materials,
        "FIX_Cornice",
        kit.cornice(
            ROOM_WIDTH,
            ROOM_DEPTH,
            ROOM_HEIGHT,
            0.105,
            WALL_THICKNESS,
        ),
        "cornice",
        "DarkWood",
        unity_space=False,
    )

    # Door lining in the south wall and a panelled leaf opened eastward, flat
    # against that wall.  The transition is runtime-owned; the authored leaf
    # is the still, readable interior side of the same real entrance.
    frame = kit.door_frame(
        DOOR_WIDTH,
        DOOR_HEIGHT,
        WALL_THICKNESS,
        jamb=0.085,
        architrave=0.0,
    )
    frame = kit.translated(
        frame,
        (DOOR_CENTER_X, -ROOM_DEPTH * 0.5, 0.0),
    )
    add_part(
        asset,
        materials,
        "FIX_Door.Frame",
        frame,
        "entrance_frame",
        "DarkWood",
        unity_space=False,
    )
    leaf_width = 1.19
    leaf_center_x = DOOR_CENTER_X + DOOR_WIDTH * 0.5 + leaf_width * 0.5
    leaf_center_z = -ROOM_DEPTH * 0.5 + 0.18
    knob = bp.u_rotated(
        bp.u_cylinder((0.0, 0.0, 0.0), (0.075, 0.040, 0.075), 8),
        (90.0, 0.0, 0.0),
    )
    knob = kit.translated(
        knob,
        (leaf_center_x + leaf_width * 0.40, 1.05, leaf_center_z + 0.08),
    )
    door_leaf = merge((
        bp.u_box(
            (leaf_center_x, 1.08, leaf_center_z),
            (leaf_width, 2.16, 0.065),
            0.014,
        ),
        bp.u_plate(
            (leaf_center_x, 0.62, leaf_center_z + 0.045),
            (0.88, 0.68, 0.014),
        ),
        bp.u_plate(
            (leaf_center_x, 1.54, leaf_center_z + 0.045),
            (0.88, 0.68, 0.014),
        ),
        knob,
    ))
    add_part(
        asset,
        materials,
        "FIX_Door.OpenLeaf",
        door_leaf,
        "entrance_door",
        "DarkWood",
    )


def u_box_limits(
    x_min: float,
    x_max: float,
    y_min: float,
    y_max: float,
    z_min: float,
    z_max: float,
    chamfer: float = 0.006,
) -> kit.Geometry:
    return bp.u_box(
        (
            (x_min + x_max) * 0.5,
            (y_min + y_max) * 0.5,
            (z_min + z_max) * 0.5,
        ),
        (x_max - x_min, y_max - y_min, z_max - z_min),
        chamfer,
    )



def build_upper_storey(asset: AssetBuild, materials: dict) -> None:
    opening_x_min, opening_z_min, opening_x_max, opening_z_max = (
        STAIR_OPENING
    )
    slab_regions = (
        (opening_x_max, ROOM_WIDTH * 0.5,
         -ROOM_DEPTH * 0.5, ROOM_DEPTH * 0.5),
        (-ROOM_WIDTH * 0.5, opening_x_max,
         opening_z_max, ROOM_DEPTH * 0.5),
        (-ROOM_WIDTH * 0.5, opening_x_max,
         -ROOM_DEPTH * 0.5, opening_z_min),
    )
    add_part(
        asset,
        materials,
        "FIX_InterstoreyCeiling",
        merge(
            u_box_limits(
                x_min,
                x_max,
                ROOM_HEIGHT,
                UPPER_FLOOR_ELEVATION,
                z_min,
                z_max,
            )
            for x_min, x_max, z_min, z_max in slab_regions
        ),
        "interstorey_ceiling",
        "CeilingPlaster",
    )
    add_part(
        asset,
        materials,
        "FIX_UpperFloor",
        merge(
            u_box_limits(
                x_min,
                x_max,
                UPPER_FLOOR_ELEVATION,
                UPPER_FLOOR_ELEVATION + 0.018,
                z_min,
                z_max,
                0.003,
            )
            for x_min, x_max, z_min, z_max in slab_regions
        ),
        "upper_floor",
        "PlankFloor",
    )

    stair_steps = []
    stair_west_closure = []
    stair_west_edge_x = STAIR_CENTER_X - STAIR_WIDTH * 0.5
    for index in range(STAIR_STEP_COUNT):
        height = (index + 1) * STAIR_STEP_RISE
        step_z = (
            STAIR_START_Z +
            STAIR_DIRECTION_Z * (index + 0.5) * STAIR_STEP_DEPTH
        )
        stair_steps.append(bp.u_box(
            (
                STAIR_CENTER_X,
                height * 0.5,
                step_z,
            ),
            (STAIR_WIDTH, height, STAIR_STEP_DEPTH + 0.012),
            0.008,
        ))
        # The walkable 1.30 m flight stays plan-owned and unmoved. This
        # matching stepped infill only closes its narrow west-side gap all
        # the way to the inner face of the exterior wall.
        stair_west_closure.append(bp.u_box(
            (
                (opening_x_min + stair_west_edge_x) * 0.5,
                height * 0.5,
                step_z,
            ),
            (
                stair_west_edge_x - opening_x_min + 0.012,
                height,
                STAIR_STEP_DEPTH + 0.012,
            ),
            0.008,
        ))
    stair_top_z = (
        STAIR_START_Z +
        STAIR_DIRECTION_Z * STAIR_STEP_COUNT * STAIR_STEP_DEPTH
    )
    south_wall_inner_z = -ROOM_DEPTH * 0.5 + WALL_THICKNESS * 0.5
    stair_south_closure = bp.u_box(
        (
            (opening_x_min + stair_west_edge_x + STAIR_WIDTH) * 0.5,
            UPPER_FLOOR_ELEVATION * 0.5,
            (south_wall_inner_z + stair_top_z) * 0.5,
        ),
        (
            stair_west_edge_x + STAIR_WIDTH - opening_x_min,
            UPPER_FLOOR_ELEVATION,
            stair_top_z - south_wall_inner_z + 0.012,
        ),
        0.008,
    )
    add_part(
        asset,
        materials,
        "FIX_Stair.Steps",
        merge((*stair_steps, *stair_west_closure, stair_south_closure)),
        "stair",
        "PlankFloor",
    )

    rail_x = STAIR_CENTER_X + STAIR_WIDTH * 0.5 + 0.07
    rail_height = 0.92
    rail_posts = []
    for index in range(0, STAIR_STEP_COUNT, 3):
        step_top = (index + 1) * STAIR_STEP_RISE
        rail_posts.append(bp.u_box(
            (
                rail_x,
                step_top + rail_height * 0.5,
                STAIR_START_Z +
                STAIR_DIRECTION_Z * (index + 0.5) * STAIR_STEP_DEPTH,
            ),
            (0.075, rail_height, 0.075),
            0.008,
        ))
    first_rail_z = (
        STAIR_START_Z + STAIR_DIRECTION_Z * STAIR_STEP_DEPTH * 0.5
    )
    last_rail_z = (
        STAIR_START_Z +
        STAIR_DIRECTION_Z *
        (STAIR_STEP_COUNT - 0.5) * STAIR_STEP_DEPTH
    )
    first_rail_y = STAIR_STEP_RISE + rail_height
    last_rail_y = UPPER_FLOOR_ELEVATION + rail_height
    rail_run = last_rail_z - first_rail_z
    rail_rise = last_rail_y - first_rail_y
    rail_length = math.hypot(rail_run, rail_rise)
    rail_pitch = math.degrees(math.atan2(rail_rise, rail_run))
    rail_top = bp.u_box(
        (0.0, 0.0, 0.0),
        (0.105, 0.105, rail_length),
        0.012,
    )
    rail_top = bp.u_rotated(rail_top, (-rail_pitch, 0.0, 0.0))
    rail_top = kit.translated(
        rail_top,
        (
            rail_x,
            (first_rail_y + last_rail_y) * 0.5,
            (first_rail_z + last_rail_z) * 0.5,
        ),
    )
    add_part(
        asset,
        materials,
        "FIX_Stair.Rail",
        merge((*rail_posts, rail_top)),
        "stair_guard",
        "DarkWood",
    )

    upper_wall_height = UPPER_CEILING_HEIGHT - UPPER_FLOOR_ELEVATION
    upper_wall_center_y = (
        UPPER_FLOOR_ELEVATION + UPPER_CEILING_HEIGHT
    ) * 0.5
    add_part(asset, materials, "FIX_UpperWalls",
             merge(perimeter_wall(wall, UPPER_FLOOR_ELEVATION, UPPER_CEILING_HEIGHT)
                   for wall in ("south", "north")),
             "upper_wall", "Wallpaper", unity_space=False)
    for wall in ("east", "west"):
        direction = 1 if wall == "east" else -1
        base = kit.chamfered_box(
            (direction * ROOM_WIDTH * 0.5, 0.0, UPPER_FLOOR_ELEVATION + 0.31),
            (WALL_THICKNESS, ROOM_DEPTH, 0.62), 0.0)
        add_part(asset, materials, f"FIX_UpperWall.{wall.title()}Base", base,
                 "upper_wall", "Wallpaper", unity_space=False)
        add_part(asset, materials, f"FIX_UpperWall.{wall.title()}Upper",
                 perimeter_wall(wall, UPPER_FLOOR_ELEVATION, UPPER_CEILING_HEIGHT, 0.62),
                 "upper_wall", "Wallpaper", unity_space=False)

    door_half = UPPER_DOOR_WIDTH * 0.5
    south_door_min = UPPER_DOOR_CENTERS_Z[0] - door_half
    south_door_max = UPPER_DOOR_CENTERS_Z[0] + door_half
    north_door_min = UPPER_DOOR_CENTERS_Z[1] - door_half
    north_door_max = UPPER_DOOR_CENTERS_Z[1] + door_half
    partition_segments = (
        (-ROOM_DEPTH * 0.5, south_door_min),
        (south_door_max, north_door_min),
        (north_door_max, ROOM_DEPTH * 0.5),
    )
    partition_geometry = [
        u_box_limits(
            UPPER_PARTITION_X - UPPER_PARTITION_THICKNESS * 0.5,
            UPPER_PARTITION_X + UPPER_PARTITION_THICKNESS * 0.5,
            UPPER_FLOOR_ELEVATION,
            UPPER_CEILING_HEIGHT,
            z_min,
            z_max,
        )
        for z_min, z_max in partition_segments
    ]
    lintel_y_min = UPPER_FLOOR_ELEVATION + UPPER_DOOR_HEIGHT
    for center_z in UPPER_DOOR_CENTERS_Z:
        partition_geometry.append(u_box_limits(
            UPPER_PARTITION_X - UPPER_PARTITION_THICKNESS * 0.5,
            UPPER_PARTITION_X + UPPER_PARTITION_THICKNESS * 0.5,
            lintel_y_min,
            UPPER_CEILING_HEIGHT,
            center_z - door_half,
            center_z + door_half,
        ))
    partition_geometry.append(u_box_limits(
        UPPER_PARTITION_X + UPPER_PARTITION_THICKNESS * 0.5,
        ROOM_WIDTH * 0.5,
        UPPER_FLOOR_ELEVATION,
        UPPER_CEILING_HEIGHT,
        UPPER_ROOM_DIVIDER_Z - UPPER_PARTITION_THICKNESS * 0.5,
        UPPER_ROOM_DIVIDER_Z + UPPER_PARTITION_THICKNESS * 0.5,
    ))
    add_part(
        asset,
        materials,
        "FIX_UpperPartitions",
        merge(partition_geometry),
        "upper_partition",
        "Wallpaper",
    )

    jamb = 0.09
    frame_depth = 0.24
    door_frames = []
    for center_z in UPPER_DOOR_CENTERS_Z:
        for side in (-1.0, 1.0):
            door_frames.append(bp.u_box(
                (
                    UPPER_PARTITION_X,
                    UPPER_FLOOR_ELEVATION + UPPER_DOOR_HEIGHT * 0.5,
                    center_z + side * (door_half + jamb * 0.5),
                ),
                (frame_depth, UPPER_DOOR_HEIGHT, jamb),
                0.008,
            ))
        door_frames.append(bp.u_box(
            (
                UPPER_PARTITION_X,
                UPPER_FLOOR_ELEVATION + UPPER_DOOR_HEIGHT + jamb * 0.5,
                center_z,
            ),
            (frame_depth, jamb, UPPER_DOOR_WIDTH + jamb * 2.0),
            0.008,
        ))
    add_part(
        asset,
        materials,
        "FIX_UpperDoorFrames",
        merge(door_frames),
        "upper_door_frame",
        "DarkWood",
    )

    guard_height = 1.0
    upper_guards = merge((
        bp.u_box(
            (
                opening_x_max,
                UPPER_FLOOR_ELEVATION + guard_height * 0.5,
                (-2.30 + opening_z_max) * 0.5,
            ),
            (0.105, guard_height, opening_z_max - (-2.30)),
            0.009,
        ),
        bp.u_box(
            (
                (opening_x_min + opening_x_max) * 0.5,
                UPPER_FLOOR_ELEVATION + guard_height * 0.5,
                opening_z_max,
            ),
            (opening_x_max - opening_x_min, guard_height, 0.105),
            0.009,
        ),
    ))
    add_part(
        asset,
        materials,
        "FIX_UpperStairGuards",
        upper_guards,
        "stair_guard",
        "DarkWood",
    )

    add_part(
        asset,
        materials,
        "FIX_UpperCeiling",
        bp.u_box(
            (0.0, UPPER_CEILING_HEIGHT + CEILING_THICKNESS * 0.5, 0.0),
            (ROOM_WIDTH, CEILING_THICKNESS, ROOM_DEPTH),
            0.006,
        ),
        "upper_ceiling",
        "CeilingPlaster",
    )


def build_windows(asset: AssetBuild, materials: dict) -> None:
    for index, x in enumerate(WINDOW_CENTERS_X):
        side = "West" if index == 0 else "East"
        frame = kit.door_frame(
            WINDOW_WIDTH,
            WINDOW_HEAD - WINDOW_SILL,
            WALL_THICKNESS + 0.045,
            jamb=0.075,
            architrave=0.012,
        )
        sill = kit.chamfered_box(
            (0.0, 0.0, -0.045),
            (WINDOW_WIDTH + 0.22, WALL_THICKNESS + 0.12, 0.09),
            0.012,
        )
        mullions = merge((
            kit.chamfered_box(
                (0.0, -0.160, (WINDOW_HEAD - WINDOW_SILL) * 0.5),
                (0.055, 0.055, WINDOW_HEAD - WINDOW_SILL - 0.08),
                0.008,
            ),
            kit.chamfered_box(
                (0.0, -0.160, (WINDOW_HEAD - WINDOW_SILL) * 0.52),
                (WINDOW_WIDTH - 0.08, 0.055, 0.055),
                0.008,
            ),
        ))
        full_frame = kit.translated(
            merge((frame, sill, mullions)),
            (x, ROOM_DEPTH * 0.5 - 0.06, WINDOW_SILL),
        )
        add_part(
            asset,
            materials,
            f"FIX_WindowFrame.{side}",
            full_frame,
            "window_frame",
            "DarkWood",
            unity_space=False,
        )
        add_part(
            asset,
            materials,
            f"FIX_WindowGlass.{side}",
            bp.u_plate(
                (x, (WINDOW_SILL + WINDOW_HEAD) * 0.5, 3.82),
                (1.20, WINDOW_HEAD - WINDOW_SILL - 0.13, 0.018),
            ),
            "window_glass",
            "Glass",
            casts_shadows=False,
            tint=(0.16, 0.24, 0.28, 0.65),
        )

        # Four uneven cloth folds per side, held open.  They communicate an
        # occupied old room without hiding the two required cold panes.
        folds: list[kit.Geometry] = []
        for direction in (-1.0, 1.0):
            edge_x = x + direction * (WINDOW_WIDTH * 0.5 + 0.09)
            for fold_index in range(2):
                offset = direction * fold_index * 0.075
                folds.append(bp.u_box(
                    (edge_x + offset, 1.55, 3.72 - fold_index * 0.015),
                    (0.095, 1.52 - fold_index * 0.07, 0.055),
                    0.014,
                ))
        add_part(
            asset,
            materials,
            f"DRESS_WindowCurtains.{side}",
            merge(folds),
            "window_curtain",
            "BedLinen",
            tint=(0.31, 0.25, 0.17, 1.0),
        )


def build_additional_windows(asset: AssetBuild, materials: dict) -> None:
    for row in WINDOWS:
        # The four preserved windows retain their curtains and semantic parts.
        if row["stable_id"] in {"ground-north-west", "ground-north-east",
                                "upper-north-west", "upper-south-west"}:
            continue
        width, height = row["width"], row["height"]
        frame = kit.door_frame(width, height, WALL_THICKNESS + 0.045,
                               jamb=0.075, architrave=0.012)
        sill = kit.chamfered_box((0.0, 0.0, -0.045),
                                 (width + 0.20, WALL_THICKNESS + 0.12, 0.09), 0.012)
        bars = merge((
            kit.chamfered_box((0.0, -0.160, height * 0.5),
                               (0.050, 0.055, height - 0.08), 0.006),
            kit.chamfered_box((0.0, -0.160, height * 0.52),
                               (width - 0.08, 0.055, 0.050), 0.006)))
        wall = row["wall"]
        angle = {"north": 0.0, "south": 180.0, "east": -90.0, "west": 90.0}[wall]
        if wall in ("north", "south"):
            origin = (row["across"], (1 if wall == "north" else -1) * 3.94,
                      row["floor_elevation"] + row["sill"])
        else:
            origin = ((1 if wall == "east" else -1) * 4.94, row["across"],
                      row["floor_elevation"] + row["sill"])
        def placed(geometry: kit.Geometry) -> kit.Geometry:
            return kit.translated(kit.rotated_z(geometry, angle), origin)
        upper = row["floor_elevation"] > 0.0
        add_part(asset, materials, row["frame_part"], placed(merge((frame, sill, bars))),
                 "upper_window_frame" if upper else "window_frame", "DarkWood",
                 unity_space=False)
        pane = kit.chamfered_box((0.0, -0.120, height * 0.5),
                                 (width - 0.13, 0.018, height - 0.13), 0.002)
        add_part(asset, materials, row["glass_part"], placed(pane),
                 "upper_window_glass" if upper else "window_glass", "Glass",
                 unity_space=False, casts_shadows=False,
                 tint=(0.16, 0.24, 0.28, 0.65))


def build_upper_windows(asset: AssetBuild, materials: dict) -> None:
    """Preserve the two original bedroom windows and their distinct curtains.

    Additional openings on these and the side walls are built from the shared
    window table by build_additional_windows; these two keep their semantic
    owners, original fabrics, sill objects and bounded practical lights.
    """

    # Each opening is authored as its own part. The atlas contract stretches
    # a sheet across the whole UV span of a mesh, so two windows seven metres
    # apart in one mesh would share a single smeared piece of timber.
    height = UPPER_WINDOW_HEAD - UPPER_WINDOW_SILL
    rooms = (
        ("North", UPPER_NORTH_WINDOW_X, 1.0),
        ("South", UPPER_SOUTH_WINDOW_X, -1.0),
    )
    for room, window_x, facing in rooms:
        frame = kit.door_frame(
            UPPER_WINDOW_WIDTH,
            height,
            WALL_THICKNESS + 0.045,
            jamb=0.070,
            architrave=0.012,
        )
        sill = kit.chamfered_box(
            (0.0, 0.0, -0.045),
            (UPPER_WINDOW_WIDTH + 0.20, WALL_THICKNESS + 0.12, 0.09),
            0.012,
        )
        mullion = kit.chamfered_box(
            (0.0, -0.160 * facing, height * 0.5),
            (0.050, 0.050, height - 0.07),
            0.008,
        )
        transom = kit.chamfered_box(
            (0.0, -0.160 * facing, height * 0.54),
            (UPPER_WINDOW_WIDTH - 0.07, 0.050, 0.050),
            0.008,
        )
        add_part(
            asset,
            materials,
            f"FIX_Upper{room}.WindowFrame",
            kit.translated(
                merge((frame, sill, mullion, transom)),
                (
                    window_x,
                    facing * (ROOM_DEPTH * 0.5 - 0.06),
                    UPPER_FLOOR_ELEVATION + UPPER_WINDOW_SILL,
                ),
            ),
            "upper_window_frame",
            "DarkWood",
            unity_space=False,
        )
        add_part(
            asset,
            materials,
            f"FIX_Upper{room}.WindowGlass",
            bp.u_plate(
                (
                    window_x,
                    UPPER_FLOOR_ELEVATION +
                    (UPPER_WINDOW_SILL + UPPER_WINDOW_HEAD) * 0.5,
                    facing * 3.82,
                ),
                (UPPER_WINDOW_WIDTH - 0.13, height - 0.13, 0.018),
            ),
            "upper_window_glass",
            "Glass",
            casts_shadows=False,
            tint=(0.16, 0.24, 0.28, 0.65),
        )

    # North is lived in and its cloth is held open at both jambs.  South is
    # out of use and half drawn, so the childhood room keeps one cold band
    # of daylight instead of an evenly lit floor.
    curtain_center_y = (
        UPPER_FLOOR_ELEVATION + UPPER_WINDOW_SILL + height * 0.52
    )
    north_curtains: list[kit.Geometry] = []
    south_curtains: list[kit.Geometry] = []
    for direction in (-1.0, 1.0):
        edge_x = UPPER_NORTH_WINDOW_X + direction * (
            UPPER_WINDOW_WIDTH * 0.5 + 0.08)
        for fold_index in range(2):
            north_curtains.append(bp.u_box(
                (
                    edge_x + direction * fold_index * 0.070,
                    curtain_center_y,
                    3.72 - fold_index * 0.015,
                ),
                (0.085, height + 0.16 - fold_index * 0.06, 0.050),
                0.012,
            ))

    south_curtains.append(bp.u_box(
        (
            UPPER_SOUTH_WINDOW_X - (UPPER_WINDOW_WIDTH * 0.5 + 0.08),
            curtain_center_y,
            -3.72,
        ),
        (0.085, height + 0.16, 0.050),
        0.012,
    ))
    for fold_index in range(3):
        south_curtains.append(bp.u_box(
            (
                UPPER_SOUTH_WINDOW_X + 0.10 + fold_index * 0.135,
                curtain_center_y,
                -3.73 + fold_index * 0.012,
            ),
            (0.150, height + 0.14 - fold_index * 0.03, 0.045),
            0.012,
        ))

    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Curtain",
        merge(north_curtains),
        "upper_window_curtain",
        "BedLinen",
        tint=(0.33, 0.27, 0.18, 1.0),
    )
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.Curtain",
        merge(south_curtains),
        "upper_window_curtain",
        "BedLinen",
        tint=(0.35, 0.30, 0.21, 1.0),
    )


def build_upper_north_room(asset: AssetBuild, materials: dict) -> None:
    """The parents' bedroom: still slept in, and the warm room of the house.

    The hearth flue stops at the interstorey slab downstairs.  Carrying it up
    is both the architectural truth and the ordinary reason the double bed
    stands against this wall and not another.
    """

    floor = UPPER_FLOOR_ELEVATION
    flue_z = UPPER_CHIMNEY_Z_MAX - UPPER_CHIMNEY_DEPTH * 0.5
    add_part(
        asset,
        materials,
        "FIX_UpperChimney",
        merge((
            u_box_limits(
                UPPER_CHIMNEY_CENTER_X - UPPER_CHIMNEY_WIDTH * 0.5,
                UPPER_CHIMNEY_CENTER_X + UPPER_CHIMNEY_WIDTH * 0.5,
                floor,
                UPPER_CEILING_HEIGHT,
                UPPER_CHIMNEY_Z_MAX - UPPER_CHIMNEY_DEPTH,
                UPPER_CHIMNEY_Z_MAX,
                0.020,
            ),
            bp.u_box(
                (UPPER_CHIMNEY_CENTER_X, floor + 0.14, flue_z - 0.020),
                (UPPER_CHIMNEY_WIDTH + 0.10, 0.14,
                 UPPER_CHIMNEY_DEPTH + 0.06),
                0.016,
            ),
        )),
        "upper_chimney",
        "Concrete",
        tint=(0.60, 0.55, 0.45, 1.0),
    )

    bed_x_min, bed_x_max = NORTH_BED_X
    bed_z_min, bed_z_max = NORTH_BED_Z
    bed_center_x = (bed_x_min + bed_x_max) * 0.5
    bed_center_z = (bed_z_min + bed_z_max) * 0.5
    bed_width = bed_x_max - bed_x_min
    bed_length = bed_z_max - bed_z_min
    frame: list[kit.Geometry] = [
        bp.u_box(
            (bed_center_x, floor + 0.31, bed_center_z),
            (bed_width, 0.22, bed_length),
            0.018,
        ),
        bp.u_box(
            (bed_center_x, floor + 0.72, bed_z_max - 0.04),
            (bed_width, 1.06, 0.080),
            0.016,
        ),
        bp.u_box(
            (bed_center_x, floor + 0.42, bed_z_min + 0.04),
            (bed_width, 0.62, 0.080),
            0.016,
        ),
    ]
    for leg_x in (bed_x_min + 0.09, bed_x_max - 0.09):
        for leg_z in (bed_z_min + 0.12, bed_z_max - 0.12):
            frame.append(bp.u_box(
                (leg_x, floor + 0.10, leg_z),
                (0.105, 0.20, 0.105),
                0.014,
            ))
    add_part(
        asset,
        materials,
        "FIX_UpperNorth.Bed",
        merge(frame),
        "upper_double_bed",
        "DarkWood",
        tint=(0.135, 0.048, 0.024, 1.0),
    )

    # Slept in on one side only.  This is the state of a bed, not a keepsake:
    # it states nothing about who is missing and carries no name or date.
    bedding: list[kit.Geometry] = [
        bp.u_box(
            (bed_center_x, floor + 0.51, bed_center_z),
            (bed_width - 0.07, 0.18, bed_length - 0.10),
            0.045,
        ),
        bp.u_box(
            (bed_center_x - 0.36, floor + 0.66, bed_z_max - 0.31),
            (0.62, 0.13, 0.36),
            0.055,
        ),
        bp.u_rotated_about(
            bp.u_box(
                (bed_center_x + 0.36, floor + 0.63, bed_z_max - 0.29),
                (0.62, 0.11, 0.36),
                0.055,
            ),
            (0.0, 0.0, -4.0),
            (bed_center_x + 0.36, floor + 0.63, bed_z_max - 0.29),
        ),
    ]
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Bedding",
        merge(bedding),
        "upper_double_bedding",
        "TeaCloth",
        tint=(0.72, 0.66, 0.55, 1.0),
    )

    # The coverlet is a separate cloth from the ticking under it, which is
    # the whole point: one half of the bed is still made and the other is
    # turned back. That is the state of a bed, not a keepsake - it names
    # nobody, carries no date and is not framed, lit or remarked on.
    coverlet: list[kit.Geometry] = [
        bp.u_plate(
            (bed_center_x - 0.33, floor + 0.625, bed_center_z - 0.12),
            (bed_width - 0.70, 0.050, bed_length - 0.36),
        ),
        bp.u_plate(
            (bed_x_min + 0.02, floor + 0.50, bed_center_z - 0.12),
            (0.045, 0.22, bed_length - 0.36),
        ),
    ]
    for fold_index in range(3):
        coverlet.append(bp.u_rotated_about(
            bp.u_plate(
                (
                    bed_center_x + 0.30 + fold_index * 0.030,
                    floor + 0.615,
                    bed_center_z - 0.60 + fold_index * 0.055,
                ),
                (0.56, 0.045, 0.50),
            ),
            (0.0, 0.0, 8.0 + fold_index * 4.0),
            (bed_center_x + 0.30, floor + 0.615, bed_center_z - 0.60),
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Coverlet",
        merge(coverlet),
        "upper_double_coverlet",
        "BookCloth",
        tint=(0.30, 0.36, 0.44, 1.0),
    )

    chest_x_min, chest_x_max = NORTH_CHEST_X
    chest_z_min, chest_z_max = NORTH_CHEST_Z
    chest_x = (chest_x_min + chest_x_max) * 0.5
    chest_z = (chest_z_min + chest_z_max) * 0.5
    chest_width = chest_x_max - chest_x_min
    chest_depth = chest_z_max - chest_z_min
    bedside_x_min, bedside_x_max = NORTH_BEDSIDE_X
    bedside_z_min, bedside_z_max = NORTH_BEDSIDE_Z
    bedside_x = (bedside_x_min + bedside_x_max) * 0.5
    bedside_z = (bedside_z_min + bedside_z_max) * 0.5
    chest: list[kit.Geometry] = [
        bp.u_box(
            (chest_x, floor + 0.44, chest_z),
            (chest_width, 0.66, chest_depth),
            0.016,
        ),
        bp.u_box(
            (chest_x, floor + 0.06, chest_z),
            (chest_width - 0.07, 0.12, chest_depth - 0.06),
            0.012,
        ),
        bp.u_box(
            (chest_x, floor + NORTH_CHEST_HEIGHT - 0.025, chest_z),
            (chest_width + 0.055, 0.050, chest_depth + 0.045),
            0.010,
        ),
    ]
    for drawer_y in (0.26, 0.55):
        chest.append(bp.u_plate(
            (chest_x, floor + drawer_y, chest_z_min + 0.012),
            (chest_width - 0.10, 0.20, 0.030),
        ))
        chest.append(bp.u_cylinder(
            (chest_x, floor + drawer_y, chest_z_min - 0.010),
            (0.055, 0.022, 0.055),
            8,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Chest",
        merge(chest),
        "upper_bedroom_chest",
        "PaleWood",
        tint=(0.42, 0.29, 0.16, 1.0),
    )

    bedside: list[kit.Geometry] = [
        bp.u_box(
            (bedside_x, floor + 0.34, bedside_z),
            (bedside_x_max - bedside_x_min, 0.48,
             bedside_z_max - bedside_z_min),
            0.014,
        ),
        bp.u_box(
            (bedside_x, floor + 0.60, bedside_z),
            (bedside_x_max - bedside_x_min + 0.05, 0.045,
             bedside_z_max - bedside_z_min + 0.04),
            0.010,
        ),
    ]
    for leg_x in (bedside_x_min + 0.05, bedside_x_max - 0.05):
        for leg_z in (bedside_z_min + 0.05, bedside_z_max - 0.05):
            bedside.append(bp.u_box(
                (leg_x, floor + 0.05, leg_z),
                (0.055, 0.10, 0.055),
                0.010,
            ))
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Bedside",
        merge(bedside),
        "upper_bedroom_bedside",
        "PaleWood",
        tint=(0.44, 0.31, 0.17, 1.0),
    )

    # A washstand set and one enamel cup: she pours water, and the stair is
    # nineteen risers, so what she needs at night stands beside the bed.
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Washstand",
        merge((
            bp.u_tapered_cylinder(
                (chest_x - 0.19, floor + NORTH_CHEST_HEIGHT + 0.055,
                 chest_z),
                (0.30, 0.055, 0.30),
                1.30,
                12,
            ),
            bp.u_tapered_cylinder(
                (chest_x + 0.26, floor + NORTH_CHEST_HEIGHT + 0.11,
                 chest_z),
                (0.20, 0.110, 0.20),
                0.72,
                10,
            ),
            bp.u_cylinder(
                (chest_x + 0.26, floor + NORTH_CHEST_HEIGHT + 0.245,
                 chest_z),
                (0.105, 0.030, 0.105),
                10,
            ),
            bp.u_cylinder(
                (bedside_x, floor + 0.675, bedside_z),
                (0.090, 0.048, 0.090),
                8,
            ),
        )),
        "upper_washstand",
        "PaintedMetal",
        tint=(0.60, 0.56, 0.44, 1.0),
    )

    rug_x_min, rug_x_max = NORTH_RUG_X
    rug_z_min, rug_z_max = NORTH_RUG_Z
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Rug",
        bp.u_plate(
            (
                (rug_x_min + rug_x_max) * 0.5,
                floor + 0.014,
                (rug_z_min + rug_z_max) * 0.5,
            ),
            (rug_x_max - rug_x_min, 0.024, rug_z_max - rug_z_min),
        ),
        "upper_bedside_rug",
        "Rug",
        tint=(0.33, 0.15, 0.085, 1.0),
    )

    lamp_x, lamp_z = UPPER_NORTH_LAMP_CENTER
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.CeilingLamp",
        merge((
            bp.u_cylinder(
                (lamp_x, UPPER_CEILING_HEIGHT - 0.12, lamp_z),
                (0.024, 0.120, 0.024),
                6,
            ),
            bp.u_cylinder(
                (lamp_x, UPPER_CEILING_HEIGHT - 0.025, lamp_z),
                (0.115, 0.025, 0.115),
                8,
            ),
            bp.u_tapered_cylinder(
                (lamp_x, UPPER_CEILING_HEIGHT - 0.30, lamp_z),
                (0.36, 0.060, 0.36),
                0.36,
                12,
            ),
        )),
        "upper_ceiling_lamp",
        "PaintedMetal",
        tint=(0.58, 0.53, 0.40, 1.0),
    )
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.LampBulb",
        bp.u_cylinder(
            (lamp_x, UPPER_CEILING_HEIGHT - 0.385, lamp_z),
            (0.095, 0.055, 0.095),
            8,
        ),
        "upper_lamp_bulb",
        "Ceramic",
        emissive=True,
        casts_shadows=False,
        tint=(1.0, 0.80, 0.52, 1.0),
    )


def build_upper_south_room(asset: AssetBuild, materials: dict) -> None:
    """The hero's childhood room, taken out of use but not abandoned.

    Nothing here explains what he was like: no photograph, no letter, no
    named or dated object.  One wooden toy is the already-allowed motif of a
    child's thing without a child.
    """

    floor = UPPER_FLOOR_ELEVATION
    bed_x_min, bed_x_max = SOUTH_BED_X
    bed_z_min, bed_z_max = SOUTH_BED_Z
    bed_x = (bed_x_min + bed_x_max) * 0.5
    bed_z = (bed_z_min + bed_z_max) * 0.5
    bed_width = bed_x_max - bed_x_min
    bed_length = bed_z_max - bed_z_min
    frame: list[kit.Geometry] = [
        bp.u_box(
            (bed_x, floor + 0.28, bed_z),
            (bed_width, 0.20, bed_length),
            0.016,
        ),
        bp.u_box(
            (bed_x, floor + 0.56, bed_z_min + 0.04),
            (bed_width, 0.90, 0.075),
            0.014,
        ),
        bp.u_box(
            (bed_x, floor + 0.37, bed_z_max - 0.04),
            (bed_width, 0.52, 0.075),
            0.014,
        ),
    ]
    for leg_x in (bed_x_min + 0.08, bed_x_max - 0.08):
        for leg_z in (bed_z_min + 0.11, bed_z_max - 0.11):
            frame.append(bp.u_box(
                (leg_x, floor + 0.09, leg_z),
                (0.090, 0.18, 0.090),
                0.012,
            ))
    add_part(
        asset,
        materials,
        "FIX_UpperSouth.Bed",
        merge(frame),
        "upper_child_bed",
        "DarkWood",
        tint=(0.145, 0.052, 0.026, 1.0),
    )

    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.Bedding",
        merge((
            bp.u_box(
                (bed_x, floor + 0.46, bed_z),
                (bed_width - 0.06, 0.16, bed_length - 0.09),
                0.040,
            ),
            bp.u_box(
                (bed_x, floor + 0.60, bed_z_min + 0.30),
                (0.56, 0.11, 0.32),
                0.048,
            ),
        )),
        "upper_child_bedding",
        "BedLinen",
        tint=(0.36, 0.30, 0.20, 1.0),
    )

    chair_x_min, chair_x_max = SOUTH_CHAIR_X
    chair_z_min, chair_z_max = SOUTH_CHAIR_Z
    chair_x = (chair_x_min + chair_x_max) * 0.5
    chair_z = (chair_z_min + chair_z_max) * 0.5
    chair_width = chair_x_max - chair_x_min
    chair_depth = chair_z_max - chair_z_min

    # A plain dust sheet over the bed and the chair.  The room is out of use
    # and kept clean; it is not damp, mouldy or derelict.
    sheet: list[kit.Geometry] = [
        bp.u_plate(
            (bed_x, floor + 0.565, bed_z + 0.02),
            (bed_width + 0.07, 0.030, bed_length - 0.32),
        ),
        bp.u_plate(
            (bed_x_min - 0.015, floor + 0.42, bed_z + 0.02),
            (0.035, 0.26, bed_length - 0.32),
        ),
        bp.u_plate(
            (bed_x_max + 0.015, floor + 0.42, bed_z + 0.02),
            (0.035, 0.26, bed_length - 0.32),
        ),
        bp.u_plate(
            (bed_x, floor + 0.45, bed_z_max + 0.06),
            (bed_width + 0.05, 0.24, 0.032),
        ),
        bp.u_plate(
            (chair_x, floor + 0.885, chair_z),
            (chair_width + 0.08, 0.026, chair_depth + 0.08),
        ),
        bp.u_plate(
            (chair_x, floor + 0.76, chair_z - (chair_depth * 0.5 + 0.04)),
            (chair_width + 0.06, 0.24, 0.030),
        ),
    ]
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.DustSheet",
        merge(sheet),
        "upper_dust_sheet",
        "TeaCloth",
        tint=(0.78, 0.75, 0.66, 1.0),
    )

    shelf_z_min, shelf_z_max = SOUTH_SHELF_Z
    shelf_z = (shelf_z_min + shelf_z_max) * 0.5
    shelf_x = UPPER_PARTITION_X + UPPER_PARTITION_THICKNESS * 0.5 + 0.11
    casework: list[kit.Geometry] = [
        bp.u_box(
            (shelf_x, floor + SOUTH_SHELF_HEIGHT, shelf_z),
            (0.22, 0.036, shelf_z_max - shelf_z_min),
            0.008,
        ),
        bp.u_box(
            (chair_x, floor + 0.44, chair_z),
            (chair_width, 0.045, chair_depth),
            0.010,
        ),
        bp.u_box(
            (chair_x, floor + 0.67, chair_z - (chair_depth * 0.5 - 0.03)),
            (chair_width, 0.44, 0.048),
            0.010,
        ),
    ]
    for bracket_z in (shelf_z_min + 0.11, shelf_z_max - 0.11):
        casework.append(bp.u_box(
            (shelf_x - 0.045, floor + SOUTH_SHELF_HEIGHT - 0.085, bracket_z),
            (0.130, 0.135, 0.040),
            0.008,
        ))
    for leg_x in (chair_x_min + 0.035, chair_x_max - 0.035):
        for leg_z in (chair_z_min + 0.035, chair_z_max - 0.035):
            casework.append(bp.u_box(
                (leg_x, floor + 0.21, leg_z),
                (0.040, 0.42, 0.040),
                0.008,
            ))
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.Casework",
        merge(casework),
        "upper_child_casework",
        "PaleWood",
        tint=(0.44, 0.31, 0.17, 1.0),
    )

    # One wooden top on the sill, standing in the room's only band of light.
    toy_y = floor + UPPER_WINDOW_SILL
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.WoodenToy",
        merge((
            bp.u_tapered_cylinder(
                (UPPER_SOUTH_WINDOW_X, toy_y + 0.045, -3.85),
                (0.048, 0.045, 0.048),
                2.45,
                10,
            ),
            bp.u_cylinder(
                (UPPER_SOUTH_WINDOW_X, toy_y + 0.122, -3.85),
                (0.024, 0.032, 0.024),
                6,
            ),
        )),
        "upper_wooden_toy",
        "PaleWood",
        tint=(0.52, 0.36, 0.19, 1.0),
    )

    # A bare bulb on its flex. The room is out of use, not unwired: someone
    # still comes up here to sweep it, and the shade that used to be on this
    # flex is simply not on it any more.
    lamp_x, lamp_z = UPPER_SOUTH_LAMP_CENTER
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.CeilingLamp",
        merge((
            bp.u_cylinder(
                (lamp_x, UPPER_CEILING_HEIGHT - 0.025, lamp_z),
                (0.105, 0.025, 0.105),
                8,
            ),
            bp.u_cylinder(
                (lamp_x, UPPER_CEILING_HEIGHT - 0.175, lamp_z),
                (0.022, 0.150, 0.022),
                6,
            ),
            bp.u_tapered_cylinder(
                (lamp_x, UPPER_CEILING_HEIGHT - 0.355, lamp_z),
                (0.075, 0.035, 0.075),
                0.80,
                8,
            ),
        )),
        "upper_child_lamp",
        "PaintedMetal",
        tint=(0.55, 0.50, 0.38, 1.0),
    )
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.LampBulb",
        bp.u_tapered_cylinder(
            (lamp_x, UPPER_CEILING_HEIGHT - 0.435, lamp_z),
            (0.115, 0.048, 0.115),
            0.55,
            10,
        ),
        "upper_child_lamp_bulb",
        "Ceramic",
        emissive=True,
        casts_shadows=False,
        tint=(1.0, 0.86, 0.62, 1.0),
    )

    rolled_z_min, rolled_z_max = SOUTH_ROLLED_RUG_Z
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.RolledRug",
        kit.translated(
            bp.u_rotated(
                bp.u_cylinder(
                    (0.0, 0.0, 0.0),
                    (0.26, (rolled_z_max - rolled_z_min) * 0.5, 0.26),
                    8,
                ),
                (90.0, 0.0, 0.0),
            ),
            (
                UPPER_PARTITION_X + UPPER_PARTITION_THICKNESS * 0.5 + 0.16,
                UPPER_FLOOR_ELEVATION + 0.135,
                (rolled_z_min + rolled_z_max) * 0.5,
            ),
        ),
        "upper_rolled_rug",
        "Rug",
        tint=(0.30, 0.14, 0.080, 1.0),
    )


def build_upper_corridor(asset: AssetBuild, materials: dict) -> None:
    """The corridor is a working passage, not a gallery."""

    floor = UPPER_FLOOR_ELEVATION
    runner_x_min, runner_x_max = CORRIDOR_RUNNER_X
    runner_z_min, runner_z_max = CORRIDOR_RUNNER_Z
    add_part(
        asset,
        materials,
        "DRESS_UpperCorridor.Runner",
        bp.u_plate(
            (
                (runner_x_min + runner_x_max) * 0.5,
                floor + 0.012,
                (runner_z_min + runner_z_max) * 0.5,
            ),
            (
                runner_x_max - runner_x_min,
                0.020,
                runner_z_max - runner_z_min,
            ),
        ),
        "upper_corridor_runner",
        "Rug",
        tint=(0.32, 0.16, 0.095, 1.0),
    )

    peg_z_min, peg_z_max = CORRIDOR_PEG_Z
    peg_face_x = UPPER_PARTITION_X - UPPER_PARTITION_THICKNESS * 0.5
    rail: list[kit.Geometry] = [
        bp.u_box(
            (
                peg_face_x - 0.028,
                floor + CORRIDOR_PEG_HEIGHT,
                (peg_z_min + peg_z_max) * 0.5,
            ),
            (0.055, 0.095, peg_z_max - peg_z_min),
            0.008,
        ),
    ]
    peg_positions = (peg_z_min + 0.16, (peg_z_min + peg_z_max) * 0.5,
                     peg_z_max - 0.16)
    for peg_z in peg_positions:
        rail.append(bp.u_box(
            (peg_face_x - 0.086, floor + CORRIDOR_PEG_HEIGHT - 0.018,
             peg_z),
            (0.075, 0.038, 0.038),
            0.008,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperCorridor.PegRail",
        merge(rail),
        "upper_peg_rail",
        "DarkWood",
        tint=(0.140, 0.050, 0.025, 1.0),
    )

    add_part(
        asset,
        materials,
        "DRESS_UpperCorridor.HangingShawl",
        merge((
            bp.u_box(
                (peg_face_x - 0.115, floor + CORRIDOR_PEG_HEIGHT - 0.38,
                 peg_positions[0]),
                (0.055, 0.72, 0.245),
                0.018,
            ),
            bp.u_box(
                (peg_face_x - 0.105, floor + CORRIDOR_PEG_HEIGHT - 0.27,
                 peg_positions[2]),
                (0.045, 0.50, 0.195),
                0.016,
            ),
        )),
        "upper_hanging_cloth",
        "BookCloth",
        tint=(0.28, 0.33, 0.40, 1.0),
    )

    chest_x_min, chest_x_max = CORRIDOR_CHEST_X
    chest_z_min, chest_z_max = CORRIDOR_CHEST_Z
    chest_x = (chest_x_min + chest_x_max) * 0.5
    chest_z = (chest_z_min + chest_z_max) * 0.5
    chest_width = chest_x_max - chest_x_min
    chest_depth = chest_z_max - chest_z_min
    chest: list[kit.Geometry] = [
        bp.u_box(
            (chest_x, floor + CORRIDOR_CHEST_HEIGHT * 0.5 - 0.035, chest_z),
            (chest_width, CORRIDOR_CHEST_HEIGHT - 0.07, chest_depth),
            0.020,
        ),
        bp.u_box(
            (chest_x, floor + CORRIDOR_CHEST_HEIGHT - 0.025, chest_z),
            (chest_width + 0.045, 0.055, chest_depth + 0.040),
            0.014,
        ),
    ]
    for band_y in (0.14, 0.30, 0.44):
        chest.append(bp.u_box(
            (chest_x, floor + band_y, chest_z),
            (chest_width + 0.022, 0.035, chest_depth + 0.022),
            0.010,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperCorridor.LinenChest",
        merge(chest),
        "upper_linen_chest",
        "Wicker",
        tint=(0.52, 0.38, 0.18, 1.0),
    )


def build_upper_north_extras(asset: AssetBuild, materials: dict) -> None:
    """What a bedroom actually needs beyond a bed.

    The divider wall stands square to this room's camera and filled a third
    of the frame with nothing, so the wardrobe goes there. Everything else
    follows the shapes the ground floor already established.
    """

    floor = UPPER_FLOOR_ELEVATION
    x_min, x_max = NORTH_WARDROBE_X
    z_min, z_max = NORTH_WARDROBE_Z
    x_mid = (x_min + x_max) * 0.5
    z_mid = (z_min + z_max) * 0.5
    width = x_max - x_min
    depth = z_max - z_min
    body_top = floor + NORTH_WARDROBE_HEIGHT - 0.09

    # Authored in Blender space, like the tea table, because the kit's
    # panelled leaf is: mixing the two conventions inside one merge is what
    # turns a wardrobe inside out. Blender (x, y, z) = Unity (x, z, y).
    wardrobe: list[kit.Geometry] = [
        kit.chamfered_box(
            (x_mid, z_mid, floor + 0.06),
            (width - 0.06, depth - 0.06, 0.12),
            0.012,
        ),
        kit.chamfered_box(
            (x_mid, z_mid, (floor + 0.12 + body_top) * 0.5),
            (width - 0.06, depth - 0.06, body_top - floor - 0.12),
            0.018,
        ),
        kit.chamfered_box(
            (x_mid, z_mid, body_top + 0.045),
            (width + 0.06, depth + 0.045, 0.09),
            0.014,
        ),
    ]

    # The one use of the kit's panelled leaf in the whole project. It hinges
    # on its own -X edge, so each leaf is just translated to its own stile.
    leaf_width = (width - 0.14) * 0.5
    for index, hinge_x in enumerate((x_min + 0.06, x_mid + 0.01)):
        wardrobe.append(kit.translated(
            kit.panelled_leaf(leaf_width, 1.62, 0.034, panels=2, stile=0.10),
            (hinge_x, z_max - 0.017, floor + 0.14),
        ))
        knob_x = (
            hinge_x + leaf_width - 0.07 if index == 0 else hinge_x + 0.07
        )
        wardrobe.append(kit.chamfered_box(
            (knob_x, z_max + 0.012, floor + 0.95),
            (0.052, 0.055, 0.052),
            0.010,
        ))
    add_part(
        asset,
        materials,
        "FIX_UpperNorth.Wardrobe",
        merge(wardrobe),
        "upper_wardrobe",
        "DarkWood",
        tint=(0.125, 0.045, 0.022, 1.0),
        unity_space=False,
    )

    x_min, x_max = NORTH_TRUNK_X
    z_min, z_max = NORTH_TRUNK_Z
    x_mid = (x_min + x_max) * 0.5
    z_mid = (z_min + z_max) * 0.5
    width = x_max - x_min
    depth = z_max - z_min
    trunk: list[kit.Geometry] = [
        bp.u_box(
            (x_mid, floor + NORTH_TRUNK_HEIGHT * 0.5 - 0.03, z_mid),
            (width, NORTH_TRUNK_HEIGHT - 0.06, depth),
            0.018,
        ),
        bp.u_box(
            (x_mid, floor + NORTH_TRUNK_HEIGHT - 0.022, z_mid),
            (width + 0.04, 0.048, depth + 0.036),
            0.012,
        ),
    ]
    for band_x in (x_min + 0.22, x_max - 0.22):
        trunk.append(bp.u_box(
            (band_x, floor + NORTH_TRUNK_HEIGHT * 0.5 - 0.03, z_mid),
            (0.045, NORTH_TRUNK_HEIGHT - 0.09, depth + 0.020),
            0.008,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Trunk",
        merge(trunk),
        "upper_bedroom_trunk",
        "PaleWood",
        tint=(0.40, 0.27, 0.15, 1.0),
    )

    linen: list[kit.Geometry] = []
    for index, lift in enumerate((0.0, 0.062, 0.118)):
        linen.append(bp.u_box(
            (
                x_mid - 0.24 + index * 0.026,
                floor + NORTH_TRUNK_HEIGHT + 0.032 + lift,
                z_mid + 0.012 * index,
            ),
            (0.46 - index * 0.03, 0.058, depth - 0.14 - index * 0.02),
            0.024,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.TrunkLinen",
        merge(linen),
        "upper_folded_linen",
        "TeaCloth",
        tint=(0.80, 0.77, 0.68, 1.0),
    )

    x_min, x_max = NORTH_CHAIR_X
    z_min, z_max = NORTH_CHAIR_Z
    x_mid = (x_min + x_max) * 0.5
    z_mid = (z_min + z_max) * 0.5
    width = x_max - x_min
    depth = z_max - z_min
    chair: list[kit.Geometry] = [
        bp.u_box(
            (x_mid, floor + 0.44, z_mid),
            (width, 0.048, depth),
            0.010,
        ),
        bp.u_box(
            (x_mid, floor + 0.70, z_max - 0.03),
            (width, 0.44, 0.050),
            0.010,
        ),
        bp.u_box(
            (x_mid, floor + 0.905, z_max - 0.03),
            (width + 0.03, 0.045, 0.065),
            0.010,
        ),
    ]
    for leg_x in (x_min + 0.035, x_max - 0.035):
        for leg_z in (z_min + 0.035, z_max - 0.035):
            chair.append(bp.u_box(
                (leg_x, floor + 0.21, leg_z),
                (0.042, 0.42, 0.042),
                0.008,
            ))
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Chair",
        merge(chair),
        "upper_bedroom_chair",
        "DarkWood",
        tint=(0.150, 0.055, 0.026, 1.0),
    )

    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.ChairClothes",
        merge((
            bp.u_box(
                (x_mid - 0.02, floor + 0.755, z_max - 0.055),
                (width - 0.06, 0.30, 0.075),
                0.030,
            ),
            bp.u_box(
                (x_mid + 0.03, floor + 0.495, z_mid + 0.03),
                (width - 0.10, 0.055, depth - 0.10),
                0.026,
            ),
        )),
        "upper_folded_clothes",
        "BookCloth",
        tint=(0.29, 0.34, 0.42, 1.0),
    )

    # Peg rail on the divider wall, the same fitting as the corridor's.
    peg_x_min, peg_x_max = NORTH_PEG_X
    peg_face_z = UPPER_ROOM_DIVIDER_Z + UPPER_PARTITION_THICKNESS * 0.5
    rail: list[kit.Geometry] = [
        bp.u_box(
            (
                (peg_x_min + peg_x_max) * 0.5,
                floor + NORTH_PEG_HEIGHT,
                peg_face_z + 0.028,
            ),
            (peg_x_max - peg_x_min, 0.095, 0.055),
            0.008,
        ),
    ]
    peg_positions = (
        peg_x_min + 0.16,
        (peg_x_min + peg_x_max) * 0.5,
        peg_x_max - 0.16,
    )
    for peg_x in peg_positions:
        rail.append(bp.u_box(
            (peg_x, floor + NORTH_PEG_HEIGHT - 0.018, peg_face_z + 0.086),
            (0.038, 0.038, 0.075),
            0.008,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.PegRail",
        merge(rail),
        "upper_bedroom_pegs",
        "DarkWood",
        tint=(0.140, 0.050, 0.025, 1.0),
    )

    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.HangingRobe",
        merge((
            bp.u_box(
                (peg_positions[1], floor + NORTH_PEG_HEIGHT - 0.42,
                 peg_face_z + 0.115),
                (0.285, 0.80, 0.055),
                0.020,
            ),
            bp.u_box(
                (peg_positions[1] - 0.015, floor + NORTH_PEG_HEIGHT - 0.80,
                 peg_face_z + 0.105),
                (0.22, 0.30, 0.045),
                0.018,
            ),
        )),
        "upper_hanging_robe",
        "BookCloth",
        tint=(0.31, 0.36, 0.44, 1.0),
    )

    rug_x_min, rug_x_max = NORTH_RUG_X
    slipper_z = NORTH_RUG_Z[0] + 0.34
    add_part(
        asset,
        materials,
        "DRESS_UpperNorth.Slippers",
        merge((
            bp.u_box(
                ((rug_x_min + rug_x_max) * 0.5 - 0.09, floor + 0.055,
                 slipper_z),
                (0.115, 0.085, 0.255),
                0.038,
            ),
            bp.u_box(
                ((rug_x_min + rug_x_max) * 0.5 + 0.075, floor + 0.055,
                 slipper_z + 0.045),
                (0.115, 0.085, 0.255),
                0.038,
            ),
        )),
        "upper_slippers",
        "BedLinen",
        tint=(0.33, 0.26, 0.17, 1.0),
    )


def build_upper_south_extras(asset: AssetBuild, materials: dict) -> None:
    """The childhood room's own furniture, plus what has moved into it.

    A room nobody sleeps in is where the things needed once a year end up.
    None of it says anything about the child: it is either the room's own
    furniture or the mother's household, which is exactly why it works.
    """

    floor = UPPER_FLOOR_ELEVATION
    x_min, x_max = SOUTH_PRESS_X
    z_min, z_max = SOUTH_PRESS_Z
    x_mid = (x_min + x_max) * 0.5
    z_mid = (z_min + z_max) * 0.5
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.LinenPress",
        kit.translated(
            kit.counter_run(
                x_max - x_min,
                z_max - z_min,
                SOUTH_PRESS_HEIGHT,
                top_thickness=0.046,
                nosing=0.026,
                plinth_inset=0.06,
            ),
            (x_mid, z_mid, floor),
        ),
        "upper_linen_press",
        "PaleWood",
        tint=(0.43, 0.30, 0.17, 1.0),
        unity_space=False,
    )

    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.PressSheet",
        merge((
            bp.u_plate(
                (x_mid, floor + SOUTH_PRESS_HEIGHT + 0.016, z_mid),
                (x_max - x_min + 0.075, 0.028, z_max - z_min + 0.070),
            ),
            bp.u_plate(
                (x_mid, floor + SOUTH_PRESS_HEIGHT - 0.13, z_max + 0.048),
                (x_max - x_min + 0.055, 0.28, 0.030),
            ),
        )),
        "upper_press_sheet",
        "TeaCloth",
        tint=(0.79, 0.76, 0.67, 1.0),
    )

    x_min, x_max = SOUTH_TABLE_X
    z_min, z_max = SOUTH_TABLE_Z
    x_mid = (x_min + x_max) * 0.5
    z_mid = (z_min + z_max) * 0.5
    width = x_max - x_min
    depth = z_max - z_min
    table = [
        kit.table_top(width, depth, 0.048, SOUTH_TABLE_HEIGHT, 0.012),
        kit.chamfered_box(
            (0.0, -depth * 0.5 + 0.055, SOUTH_TABLE_HEIGHT - 0.11),
            (width - 0.13, 0.048, 0.100),
            0.008,
        ),
        kit.chamfered_box(
            (0.0, depth * 0.5 - 0.055, SOUTH_TABLE_HEIGHT - 0.11),
            (width - 0.13, 0.048, 0.100),
            0.008,
        ),
    ]
    for leg_x in (-width * 0.5 + 0.055, width * 0.5 - 0.055):
        for leg_z in (-depth * 0.5 + 0.055, depth * 0.5 - 0.055):
            table.append(kit.chamfered_box(
                (leg_x, leg_z, (SOUTH_TABLE_HEIGHT - 0.048) * 0.5),
                (0.048, 0.048, SOUTH_TABLE_HEIGHT - 0.048),
                0.008,
            ))
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.Table",
        kit.translated(merge(table), (x_mid, z_mid, floor)),
        "upper_child_table",
        "PaleWood",
        tint=(0.45, 0.32, 0.18, 1.0),
        unity_space=False,
    )

    x_min, x_max = SOUTH_TRUNK_X
    z_min, z_max = SOUTH_TRUNK_Z
    x_mid = (x_min + x_max) * 0.5
    z_mid = (z_min + z_max) * 0.5
    width = x_max - x_min
    depth = z_max - z_min
    trunk: list[kit.Geometry] = [
        bp.u_box(
            (x_mid, floor + SOUTH_TRUNK_HEIGHT * 0.5 - 0.03, z_mid),
            (width, SOUTH_TRUNK_HEIGHT - 0.06, depth),
            0.018,
        ),
        bp.u_box(
            (x_mid, floor + SOUTH_TRUNK_HEIGHT - 0.022, z_mid),
            (width + 0.04, 0.048, depth + 0.036),
            0.012,
        ),
    ]
    for band_x in (x_min + 0.18, x_max - 0.18):
        trunk.append(bp.u_box(
            (band_x, floor + SOUTH_TRUNK_HEIGHT * 0.5 - 0.03, z_mid),
            (0.042, SOUTH_TRUNK_HEIGHT - 0.09, depth + 0.020),
            0.008,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.Trunk",
        merge(trunk),
        "upper_child_trunk",
        "DarkWood",
        tint=(0.140, 0.050, 0.025, 1.0),
    )

    blankets: list[kit.Geometry] = []
    for index, lift in enumerate((0.0, 0.070, 0.132)):
        blankets.append(bp.u_box(
            (
                x_mid + 0.02 - index * 0.022,
                floor + SOUTH_TRUNK_HEIGHT + 0.036 + lift,
                z_mid - 0.010 * index,
            ),
            (width - 0.20 - index * 0.03, 0.062, depth - 0.16),
            0.026,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.TrunkBlankets",
        merge(blankets),
        "upper_folded_blankets",
        "BookCloth",
        tint=(0.30, 0.35, 0.43, 1.0),
    )

    x_min, x_max = SOUTH_BASKET_X
    z_min, z_max = SOUTH_BASKET_Z
    x_mid = (x_min + x_max) * 0.5
    z_mid = (z_min + z_max) * 0.5
    width = x_max - x_min
    depth = z_max - z_min
    basket: list[kit.Geometry] = [
        bp.u_tapered_cylinder(
            (x_mid, floor + SOUTH_BASKET_HEIGHT * 0.5 - 0.02, z_mid),
            (width - 0.09, SOUTH_BASKET_HEIGHT * 0.5 - 0.02, depth - 0.09),
            1.18,
            10,
        ),
        bp.u_cylinder(
            (x_mid, floor + SOUTH_BASKET_HEIGHT - 0.025, z_mid),
            (width, 0.026, depth),
            10,
        ),
    ]
    for stave_x, stave_z in (
        (x_min + 0.035, z_mid),
        (x_max - 0.035, z_mid),
        (x_mid, z_min + 0.035),
        (x_mid, z_max - 0.035),
    ):
        basket.append(bp.u_box(
            (stave_x, floor + SOUTH_BASKET_HEIGHT * 0.5, stave_z),
            (0.032, SOUTH_BASKET_HEIGHT - 0.06, 0.032),
            0.006,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperSouth.Basket",
        merge(basket),
        "upper_laundry_basket",
        "Wicker",
        tint=(0.54, 0.40, 0.19, 1.0),
    )


def build_upper_corridor_extras(asset: AssetBuild, materials: dict) -> None:
    """The corridor is too narrow for furniture, so it uses its walls."""

    floor = UPPER_FLOOR_ELEVATION
    z_min, z_max = CORRIDOR_SHELF_Z
    z_mid = (z_min + z_max) * 0.5
    shelf_x = UPPER_PARTITION_X - UPPER_PARTITION_THICKNESS * 0.5 - 0.12
    shelf: list[kit.Geometry] = [
        bp.u_box(
            (shelf_x, floor + CORRIDOR_SHELF_HEIGHT, z_mid),
            (0.24, 0.038, z_max - z_min),
            0.008,
        ),
    ]
    for bracket_z in (z_min + 0.13, z_max - 0.13):
        shelf.append(bp.u_box(
            (
                shelf_x + 0.048,
                floor + CORRIDOR_SHELF_HEIGHT - 0.088,
                bracket_z,
            ),
            (0.135, 0.140, 0.042),
            0.008,
        ))
    add_part(
        asset,
        materials,
        "DRESS_UpperCorridor.HighShelf",
        merge(shelf),
        "upper_corridor_shelf",
        "PaleWood",
        tint=(0.42, 0.29, 0.16, 1.0),
    )

    stacks: list[kit.Geometry] = []
    for index, stack_z in enumerate((z_min + 0.34, z_mid + 0.16, z_max - 0.26)):
        for level in range(2 + (index % 2)):
            stacks.append(bp.u_box(
                (
                    shelf_x - 0.006 * level,
                    floor + CORRIDOR_SHELF_HEIGHT + 0.055 + level * 0.062,
                    stack_z,
                ),
                (0.205 - level * 0.012, 0.058, 0.40 - index * 0.05),
                0.024,
            ))
    add_part(
        asset,
        materials,
        "DRESS_UpperCorridor.ShelfLinen",
        merge(stacks),
        "upper_shelf_linen",
        "TeaCloth",
        tint=(0.81, 0.78, 0.69, 1.0),
    )

    x_min, x_max = CORRIDOR_PAIL_X
    z_min, z_max = CORRIDOR_PAIL_Z
    pail_x = x_min + 0.24
    pail_z = (z_min + z_max) * 0.5
    pail: list[kit.Geometry] = [
        bp.u_tapered_cylinder(
            (pail_x, floor + 0.145, pail_z),
            (0.30, 0.145, 0.30),
            1.14,
            10,
        ),
        bp.u_cylinder(
            (pail_x, floor + 0.282, pail_z),
            (0.335, 0.020, 0.335),
            10,
        ),
    ]
    for side in (-1.0, 1.0):
        pail.append(bp.u_rotated_about(
            bp.u_box(
                (pail_x + side * 0.10, floor + 0.40, pail_z),
                (0.026, 0.30, 0.026),
                0.006,
            ),
            (0.0, 0.0, side * 26.0),
            (pail_x, floor + 0.28, pail_z),
        ))
    # A broom leaning into the dead end beside it.
    pail.append(bp.u_rotated_about(
        bp.u_box(
            (x_max - 0.14, floor + 0.62, z_max - 0.10),
            (0.032, 1.24, 0.032),
            0.006,
        ),
        (7.0, 0.0, 5.0),
        (x_max - 0.14, floor, z_max - 0.10),
    ))
    pail.append(bp.u_box(
        (x_max - 0.20, floor + 0.055, z_max - 0.13),
        (0.085, 0.105, 0.30),
        0.010,
    ))
    add_part(
        asset,
        materials,
        "DRESS_UpperCorridor.Pail",
        merge(pail),
        "upper_corridor_pail",
        "PaintedMetal",
        tint=(0.56, 0.52, 0.40, 1.0),
    )


def build_upper_skirting(asset: AssetBuild, materials: dict) -> None:
    """The upper storey had no skirting at all.

    The ground floor carries one, and its absence up here is what left the
    floor-to-wall joint reading as a bare line. No cornice: at 2.36 m in the
    clear it would only press the ceiling down.
    """

    floor = UPPER_FLOOR_ELEVATION
    half = UPPER_SKIRTING_HEIGHT * 0.5
    depth = UPPER_SKIRTING_DEPTH
    door_half = UPPER_DOOR_WIDTH * 0.5
    room_face = UPPER_PARTITION_X + UPPER_PARTITION_THICKNESS * 0.5
    corridor_face = UPPER_PARTITION_X - UPPER_PARTITION_THICKNESS * 0.5
    divider_south = UPPER_ROOM_DIVIDER_Z - UPPER_PARTITION_THICKNESS * 0.5
    divider_north = UPPER_ROOM_DIVIDER_Z + UPPER_PARTITION_THICKNESS * 0.5
    east_face = ROOM_WIDTH * 0.5 - WALL_THICKNESS * 0.5
    north_face = ROOM_DEPTH * 0.5 - WALL_THICKNESS * 0.5
    runs: list[kit.Geometry] = []

    def run_x(x_from, x_to, z_face, outward):
        runs.append(u_box_limits(
            min(x_from, x_to), max(x_from, x_to),
            floor, floor + UPPER_SKIRTING_HEIGHT,
            z_face - depth if outward > 0 else z_face,
            z_face if outward > 0 else z_face + depth,
            0.005,
        ))

    def run_z(z_from, z_to, x_face, outward):
        runs.append(u_box_limits(
            x_face - depth if outward > 0 else x_face,
            x_face if outward > 0 else x_face + depth,
            floor, floor + UPPER_SKIRTING_HEIGHT,
            min(z_from, z_to), max(z_from, z_to),
            0.005,
        ))

    for room_z_far, divider_face, door_center in (
        (north_face, divider_north, UPPER_DOOR_CENTERS_Z[1]),
        (-north_face, divider_south, UPPER_DOOR_CENTERS_Z[0]),
    ):
        outward = 1.0 if room_z_far > 0 else -1.0
        run_x(room_face, east_face, room_z_far, outward)
        run_x(room_face, east_face, divider_face, -outward)
        run_z(divider_face, room_z_far, east_face, 1.0)
        run_z(divider_face, door_center - door_half, room_face, -1.0)
        run_z(door_center + door_half, room_z_far, room_face, -1.0)

    corridor_far_z = ROOM_DEPTH * 0.5
    run_z(-corridor_far_z, UPPER_DOOR_CENTERS_Z[0] - door_half,
          corridor_face, 1.0)
    run_z(UPPER_DOOR_CENTERS_Z[0] + door_half,
          UPPER_DOOR_CENTERS_Z[1] - door_half, corridor_face, 1.0)
    run_z(UPPER_DOOR_CENTERS_Z[1] + door_half, corridor_far_z,
          corridor_face, 1.0)

    add_part(
        asset,
        materials,
        "FIX_UpperSkirting",
        merge(runs),
        "upper_skirting",
        "DarkWood",
        tint=(0.135, 0.048, 0.024, 1.0),
    )


def build_fireplace(asset: AssetBuild, materials: dict) -> None:
    stones: list[kit.Geometry] = [
        bp.u_box((0.0, 0.10, 3.45), (2.35, 0.20, 1.08), 0.025),
        bp.u_box((-0.84, 0.78, 3.64), (0.42, 1.42, 0.62), 0.028),
        bp.u_box((0.84, 0.78, 3.64), (0.42, 1.42, 0.62), 0.028),
        bp.u_box((0.0, 1.48, 3.63), (2.10, 0.34, 0.66), 0.030),
        bp.u_box((0.0, 1.68, 3.62), (2.35, 0.16, 0.82), 0.026),
        bp.u_box((0.0, 2.53, 3.82), (1.62, 1.72, 0.38), 0.025),
    ]
    # Irregular faced blocks keep the broad mass old and maintained rather
    # than uniformly tiled or rust-coloured.
    for row, y in enumerate((0.34, 0.69, 1.04, 1.34)):
        for column, x in enumerate((-0.88, -0.47, 0.47, 0.88)):
            if abs(x) < 0.7 and y < 1.25:
                continue
            width = 0.34 + 0.045 * ((row + column) % 2)
            stones.append(bp.u_box(
                (x, y, 3.29),
                (width, 0.27, 0.15),
                0.018,
            ))
    add_part(
        asset,
        materials,
        "FIX_Fireplace.Stonework",
        merge(stones),
        "fireplace_stone",
        "Concrete",
        tint=(0.31, 0.275, 0.225, 1.0),
    )
    add_part(
        asset,
        materials,
        "FIX_Fireplace.Firebox",
        bp.u_box((0.0, 0.73, 3.285), (1.30, 1.05, 0.08), 0.016),
        "firebox",
        "DarkWood",
        tint=(0.035, 0.022, 0.017, 1.0),
    )

    logs = merge((
        kit.translated(
            bp.u_rotated(
                bp.u_cylinder((0.0, 0.0, 0.0), (0.15, 0.42, 0.15), 8),
                (0.0, 0.0, 90.0),
            ),
            (-0.18, 0.30, 3.19),
        ),
        kit.translated(
            bp.u_rotated(
                bp.u_cylinder((0.0, 0.0, 0.0), (0.13, 0.40, 0.13), 8),
                (0.0, 0.0, 90.0),
            ),
            (0.19, 0.34, 3.20),
        ),
    ))
    add_part(
        asset,
        materials,
        "FIX_Fire.Logs",
        logs,
        "fire_logs",
        "DarkWood",
        tint=(0.10, 0.035, 0.016, 1.0),
    )
    add_part(
        asset,
        materials,
        "FIX_Fire.Embers",
        merge((
            bp.u_cylinder((-0.24, 0.25, 3.16), (0.24, 0.055, 0.16), 7),
            bp.u_cylinder((0.08, 0.23, 3.14), (0.30, 0.05, 0.19), 7),
            bp.u_cylinder((0.33, 0.27, 3.18), (0.18, 0.045, 0.13), 7),
        )),
        "fire_embers",
        "Fire",
        emissive=True,
        casts_shadows=False,
        tint=(1.0, 0.18, 0.03, 1.0),
    )
    add_part(
        asset,
        materials,
        "FIX_Fire.Flame.Back",
        merge((
            bp.u_tapered_cylinder(
                (-0.20, 0.58, 3.20), (0.30, 0.34, 0.20), 0.08, 5),
            bp.u_tapered_cylinder(
                (0.22, 0.62, 3.19), (0.32, 0.39, 0.21), 0.06, 5),
        )),
        "fire_flame",
        "Fire",
        emissive=True,
        casts_shadows=False,
        tint=(1.0, 0.28, 0.045, 1.0),
    )
    add_part(
        asset,
        materials,
        "FIX_Fire.Flame.Front",
        bp.u_tapered_cylinder(
            (0.02, 0.53, 3.11), (0.36, 0.29, 0.23), 0.06, 5),
        "fire_flame",
        "Fire",
        emissive=True,
        casts_shadows=False,
        tint=(1.0, 0.48, 0.075, 1.0),
    )


def build_table_and_service(asset: AssetBuild, materials: dict) -> None:
    top = kit.table_top(
        TABLE_WIDTH,
        TABLE_DEPTH,
        0.065,
        TABLE_HEIGHT,
        0.014,
    )
    apron = merge((
        kit.chamfered_box((0.0, -0.37, 0.385), (1.18, 0.065, 0.15), 0.010),
        kit.chamfered_box((0.0, 0.37, 0.385), (1.18, 0.065, 0.15), 0.010),
        kit.chamfered_box((-0.62, 0.0, 0.385), (0.065, 0.66, 0.15), 0.010),
        kit.chamfered_box((0.62, 0.0, 0.385), (0.065, 0.66, 0.15), 0.010),
    ))
    legs: list[kit.Geometry] = []
    for x in (-0.58, 0.58):
        for z in (-0.31, 0.31):
            legs.append(kit.translated(
                kit.turned_leg(0.39, 0.055, 0.035, 0.064, 8),
                (x, z, 0.0),
            ))
    add_part(
        asset,
        materials,
        "FIX_Table.Top",
        top,
        "tea_table_top",
        "DarkWood",
        unity_space=False,
        tint=(0.22, 0.085, 0.034, 1.0),
    )
    add_part(
        asset,
        materials,
        "FIX_Table.Frame",
        merge((apron, merge(legs))),
        "tea_table_frame",
        "DarkWood",
        unity_space=False,
        tint=(0.16, 0.055, 0.024, 1.0),
    )

    # Tray leaves a measured empty dock for the exact existing NPC kettle.
    add_part(
        asset,
        materials,
        "DRESS_TeaService.Tray",
        bp.u_plate((0.16, 0.493, 0.04), (0.78, 0.025, 0.48)),
        "tea_tray",
        "PaintedMetal",
        tint=(0.22, 0.18, 0.12, 1.0),
    )
    service: list[kit.Geometry] = []
    for x, z, yaw in ((-0.38, -0.11, -15.0), (0.51, -0.17, 14.0)):
        cup = bp.u_tapered_cylinder(
            (x, 0.57, z), (0.16, 0.07, 0.16), 1.12, 8)
        handle = merge((
            bp.u_box((x + 0.105, 0.58, z), (0.08, 0.035, 0.035), 0.008),
            bp.u_box((x + 0.135, 0.61, z), (0.035, 0.07, 0.035), 0.008),
            bp.u_box((x + 0.105, 0.64, z), (0.08, 0.035, 0.035), 0.008),
        ))
        cup_set = merge((
            bp.u_rotated_about(cup, (0.0, yaw, 0.0), (x, 0.57, z)),
            handle,
            bp.u_cylinder((x, 0.515, z), (0.25, 0.012, 0.25), 10),
        ))
        service.append(cup_set)
    service.extend((
        bp.u_tapered_cylinder(
            (-0.10, 0.585, -0.19), (0.20, 0.075, 0.20), 0.88, 8),
        bp.u_cylinder((-0.10, 0.672, -0.19), (0.13, 0.018, 0.13), 8),
        bp.u_cylinder((-0.10, 0.706, -0.19), (0.055, 0.020, 0.055), 8),
    ))
    add_part(
        asset,
        materials,
        "DRESS_TeaService.CupsAndSugar",
        merge(service),
        "tea_service",
        "Ceramic",
        tint=(0.58, 0.49, 0.36, 1.0),
    )


def rocker_rail(x: float, z_center: float) -> kit.Geometry:
    pieces: list[kit.Geometry] = []
    points: list[tuple[float, float]] = []
    for index in range(7):
        t = -1.0 + 2.0 * index / 6.0
        z = z_center + t * 0.63
        y = 0.055 + 0.10 * t * t
        points.append((y, z))
    for first, second in zip(points, points[1:]):
        dy = second[0] - first[0]
        dz = second[1] - first[1]
        length = math.hypot(dy, dz) + 0.018
        angle = -math.degrees(math.atan2(dy, dz))
        section = bp.u_box((0.0, 0.0, 0.0), (0.065, 0.075, length), 0.014)
        section = bp.u_rotated(section, (angle, 0.0, 0.0))
        pieces.append(kit.translated(section, (
            x,
            (first[0] + second[0]) * 0.5,
            (first[1] + second[1]) * 0.5,
        )))
    return merge(pieces)


def build_rocking_chair(asset: AssetBuild, materials: dict) -> None:
    x, _, z = ROCKER_CENTER
    frame: list[kit.Geometry] = [
        rocker_rail(x - 0.31, z),
        rocker_rail(x + 0.31, z),
        bp.u_box((x - 0.29, 0.38, z - 0.22), (0.075, 0.62, 0.075), 0.012),
        bp.u_box((x + 0.29, 0.38, z - 0.22), (0.075, 0.62, 0.075), 0.012),
        bp.u_box((x - 0.29, 0.50, z + 0.27), (0.075, 0.88, 0.075), 0.012),
        bp.u_box((x + 0.29, 0.50, z + 0.27), (0.075, 0.88, 0.075), 0.012),
        bp.u_box((x, 0.42, z), (0.68, 0.10, 0.64), 0.014),
        bp.u_box((x - 0.36, 0.67, z), (0.065, 0.065, 0.66), 0.012),
        bp.u_box((x + 0.36, 0.67, z), (0.065, 0.065, 0.66), 0.012),
    ]
    for slat_x in (-0.25, -0.125, 0.0, 0.125, 0.25):
        slat = bp.u_box((0.0, 0.0, 0.0), (0.065, 0.88, 0.055), 0.011)
        slat = bp.u_rotated(slat, (12.0, 0.0, 0.0))
        frame.append(kit.translated(slat, (x + slat_x, 1.02, z + 0.38)))
    frame.append(bp.u_box((x, 1.49, z + 0.49), (0.72, 0.10, 0.09), 0.016))
    add_part(
        asset,
        materials,
        "FIX_RockingChair.Frame",
        merge(frame),
        "rocking_chair",
        "DarkWood",
        tint=(0.145, 0.05, 0.022, 1.0),
    )
    add_part(
        asset,
        materials,
        "FIX_RockingChair.Cushion",
        bp.u_box((x + 0.02, 0.505, z - 0.02), (0.58, 0.13, 0.54), 0.045),
        "rocking_chair_cushion",
        "Upholstery",
        tint=(0.29, 0.145, 0.095, 1.0),
    )


def build_sofa(asset: AssetBuild, materials: dict) -> None:
    x, _, z = SOFA_CENTER
    frame = merge((
        bp.u_box((x, 0.20, z), (0.82, 0.35, 2.24), 0.025),
        bp.u_box((x - 0.31, 0.78, z), (0.24, 1.10, 2.18), 0.030),
        bp.u_box((x + 0.02, 0.54, z - 1.02), (0.76, 0.55, 0.20), 0.030),
        bp.u_box((x + 0.02, 0.54, z + 1.02), (0.76, 0.55, 0.20), 0.030),
        bp.u_box((x - 0.20, 0.055, z - 0.82), (0.13, 0.11, 0.13), 0.018),
        bp.u_box((x - 0.20, 0.055, z + 0.82), (0.13, 0.11, 0.13), 0.018),
        bp.u_box((x + 0.22, 0.055, z - 0.82), (0.13, 0.11, 0.13), 0.018),
        bp.u_box((x + 0.22, 0.055, z + 0.82), (0.13, 0.11, 0.13), 0.018),
    ))
    add_part(
        asset,
        materials,
        "FIX_Sofa.Frame",
        frame,
        "sofa_frame",
        "DarkWood",
        tint=(0.12, 0.039, 0.020, 1.0),
    )
    cushions = merge((
        bp.u_box((x + 0.13, 0.45, z - 0.52), (0.62, 0.24, 0.91), 0.065),
        bp.u_box((x + 0.11, 0.44, z + 0.50), (0.62, 0.22, 0.94), 0.065),
        bp.u_rotated_about(
            bp.u_box((x - 0.10, 0.91, z - 0.52), (0.27, 0.78, 0.91), 0.065),
            (0.0, 0.0, -5.0),
            (x - 0.10, 0.91, z - 0.52),
        ),
        bp.u_box((x - 0.09, 0.90, z + 0.50), (0.27, 0.76, 0.94), 0.065),
    ))
    add_part(
        asset,
        materials,
        "FIX_Sofa.Cushions",
        cushions,
        "sofa_cushion",
        "Upholstery",
        tint=(0.255, 0.11, 0.078, 1.0),
    )
    throw = merge((
        bp.u_plate((x + 0.29, 0.59, z + 0.34), (0.035, 0.56, 0.72)),
        bp.u_plate((x - 0.22, 1.07, z + 0.34), (0.035, 0.62, 0.72)),
        bp.u_plate((x - 0.205, 1.08, z + 0.50), (0.018, 0.22, 0.24)),
    ))
    add_part(
        asset,
        materials,
        "DRESS_Sofa.PatchedThrow",
        throw,
        "patched_throw",
        "BedLinen",
        tint=(0.33, 0.27, 0.17, 1.0),
    )


def build_dressing(asset: AssetBuild, materials: dict) -> None:
    add_part(
        asset,
        materials,
        "DRESS_Rug",
        bp.u_plate((0.0, 0.018, 0.20), (3.65, 0.028, 3.15)),
        "worn_rug",
        "Rug",
        tint=(0.31, 0.135, 0.075, 1.0),
    )

    cupboard = merge((
        bp.u_box((2.78, 0.10, -3.63), (1.72, 0.20, 0.58), 0.018),
        bp.u_box((2.78, 1.02, -3.72), (1.65, 1.82, 0.43), 0.020),
        bp.u_plate((2.36, 1.08, -3.48), (0.70, 1.48, 0.028)),
        bp.u_plate((3.20, 1.08, -3.48), (0.70, 1.48, 0.028)),
        bp.u_box((2.78, 2.00, -3.67), (1.80, 0.10, 0.56), 0.018),
        bp.u_cylinder((2.66, 1.10, -3.44), (0.055, 0.025, 0.055), 8),
        bp.u_cylinder((2.90, 1.10, -3.44), (0.055, 0.025, 0.055), 8),
    ))
    add_part(
        asset,
        materials,
        "DRESS_Cupboard",
        cupboard,
        "old_cupboard",
        "DarkWood",
        tint=(0.145, 0.052, 0.025, 1.0),
    )

    # Quiet clock, no date and no readable text.
    clock_body = bp.u_rotated(
        bp.u_cylinder((0.0, 0.0, 0.0), (0.58, 0.038, 0.58), 12),
        (90.0, 0.0, 0.0),
    )
    clock_body = kit.translated(clock_body, (2.82, 2.31, -3.83))
    add_part(
        asset,
        materials,
        "DRESS_WallClock.Body",
        clock_body,
        "wall_clock",
        "DarkWood",
        tint=(0.10, 0.035, 0.018, 1.0),
    )
    hands = merge((
        bp.u_box((2.82, 2.39, -3.785), (0.025, 0.21, 0.025), 0.005),
        bp.u_rotated_about(
            bp.u_box((2.88, 2.31, -3.785), (0.18, 0.022, 0.025), 0.005),
            (0.0, 0.0, 22.0),
            (2.82, 2.31, -3.785),
        ),
    ))
    add_part(
        asset,
        materials,
        "DRESS_WallClock.Hands",
        hands,
        "wall_clock_hands",
        "PaintedMetal",
        casts_shadows=False,
        tint=(0.18, 0.15, 0.11, 1.0),
    )

    # Yarn basket and two balls are ordinary handiwork, not a medical cue.
    basket = merge((
        bp.u_tapered_cylinder((1.18, 0.24, 1.62), (0.48, 0.22, 0.42), 1.16, 10),
        bp.u_cylinder((1.18, 0.45, 1.62), (0.50, 0.025, 0.44), 10),
        bp.u_box((0.94, 0.31, 1.62), (0.035, 0.42, 0.035), 0.006),
        bp.u_box((1.42, 0.31, 1.62), (0.035, 0.42, 0.035), 0.006),
        bp.u_box((1.18, 0.31, 1.41), (0.035, 0.42, 0.035), 0.006),
        bp.u_box((1.18, 0.31, 1.83), (0.035, 0.42, 0.035), 0.006),
    ))
    add_part(
        asset,
        materials,
        "DRESS_YarnBasket",
        basket,
        "yarn_basket",
        "DarkWood",
        tint=(0.27, 0.12, 0.045, 1.0),
    )
    yarn = merge((
        ellipsoid_source((1.08, 0.49, 1.61), (0.14, 0.12, 0.13), 8),
        ellipsoid_source((1.29, 0.50, 1.64), (0.13, 0.13, 0.12), 8),
    ))
    add_part(
        asset,
        materials,
        "DRESS_YarnBasket.Yarn",
        yarn,
        "yarn",
        "BedLinen",
        unity_space=False,
        tint=(0.38, 0.25, 0.15, 1.0),
    )

    slippers = merge((
        bp.u_box((0.65, 0.075, 2.25), (0.24, 0.12, 0.42), 0.045),
        bp.u_box((0.94, 0.075, 2.18), (0.24, 0.12, 0.42), 0.045),
    ))
    add_part(
        asset,
        materials,
        "DRESS_Slippers",
        slippers,
        "slippers",
        "BedLinen",
        tint=(0.245, 0.17, 0.11, 1.0),
    )

    firewood: list[kit.Geometry] = []
    for row in range(3):
        for column in range(3 - (row % 2)):
            x = 1.40 + column * 0.23 + (0.10 if row % 2 else 0.0)
            y = 0.10 + row * 0.16
            log = bp.u_cylinder((0.0, 0.0, 0.0), (0.15, 0.28, 0.15), 8)
            log = bp.u_rotated(log, (0.0, 0.0, 90.0))
            firewood.append(kit.translated(log, (x, y, 3.46)))
    add_part(
        asset,
        materials,
        "DRESS_Firewood",
        merge(firewood),
        "firewood",
        "DarkWood",
        tint=(0.18, 0.065, 0.024, 1.0),
    )
    poker = merge((
        bp.u_box((2.04, 0.56, 3.60), (0.035, 1.08, 0.035), 0.006),
        bp.u_box((2.04, 0.10, 3.49), (0.035, 0.16, 0.26), 0.006),
    ))
    add_part(
        asset,
        materials,
        "DRESS_FirePoker",
        poker,
        "fireplace_tool",
        "PaintedMetal",
        tint=(0.10, 0.09, 0.075, 1.0),
    )


def build_floor_lamp(asset: AssetBuild, materials: dict) -> None:
    floor_x, _, floor_z = ANCHORS_UNITY["ANCHOR_FloorLampLight"]
    floor_metal = merge((
        bp.u_tapered_cylinder(
            (floor_x, 0.055, floor_z),
            (0.50, 0.055, 0.50),
            0.72,
            12,
        ),
        bp.u_cylinder(
            (floor_x, 0.72, floor_z),
            (0.060, 0.65, 0.060),
            10,
        ),
        bp.u_cylinder(
            (floor_x, 1.30, floor_z),
            (0.13, 0.035, 0.13),
            10,
        ),
    ))
    add_part(
        asset,
        materials,
        "DRESS_FloorLamp.Frame",
        floor_metal,
        "floor_lamp",
        "PaintedMetal",
        tint=(0.32, 0.23, 0.12, 1.0),
    )
    add_part(
        asset,
        materials,
        "DRESS_FloorLamp.Shade",
        bp.u_tapered_cylinder(
            (floor_x, 1.54, floor_z),
            (0.62, 0.27, 0.62),
            0.55,
            12,
        ),
        "floor_lamp_shade",
        "BedLinen",
        tint=(0.80, 0.66, 0.43, 1.0),
    )
    add_part(
        asset,
        materials,
        "DRESS_FloorLamp.Bulb",
        bp.u_tapered_cylinder(
            (floor_x, 1.30, floor_z),
            (0.14, 0.065, 0.14),
            0.72,
            10,
        ),
        "floor_lamp_bulb",
        "Ceramic",
        emissive=True,
        casts_shadows=False,
        tint=(1.0, 0.56, 0.20, 1.0),
    )

def build() -> AssetBuild:
    collection, root = configure_scene()
    materials = {sheet: create_material(sheet) for sheet in SHEET_PITCH}
    asset = AssetBuild(root, collection)
    build_shell(asset, materials)
    build_upper_storey(asset, materials)
    build_upper_windows(asset, materials)
    build_additional_windows(asset, materials)
    build_upper_north_room(asset, materials)
    build_upper_south_room(asset, materials)
    build_upper_corridor(asset, materials)
    build_upper_north_extras(asset, materials)
    build_upper_south_extras(asset, materials)
    build_upper_corridor_extras(asset, materials)
    build_upper_skirting(asset, materials)
    build_windows(asset, materials)
    build_fireplace(asset, materials)
    build_dressing(asset, materials)
    build_sofa(asset, materials)
    build_floor_lamp(asset, materials)
    build_table_and_service(asset, materials)
    build_rocking_chair(asset, materials)
    for name, unity_position in ANCHORS_UNITY.items():
        add_anchor(asset, name, ANCHOR_ROLES[name], unity_position)
    return asset


def source_bounds_to_unity(
    bounds_pair: tuple[Sequence[float], Sequence[float]],
) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    low, high = bounds_pair
    return unity_point(low), unity_point(high)


def centre_for(part: Part) -> tuple[float, float, float]:
    low, high = source_bounds_to_unity(kit.bounds(part.geometry))
    return tuple((low[index] + high[index]) * 0.5 for index in range(3))


def bounds_for(part: Part) -> tuple[
    tuple[float, float, float],
    tuple[float, float, float],
]:
    return source_bounds_to_unity(kit.bounds(part.geometry))


def validate_window_layout(asset: AssetBuild, problems: list[str]) -> None:
    from mathutils.bvhtree import BVHTree

    if (WINDOW_LAYOUT["room_width"] != ROOM_WIDTH or
            WINDOW_LAYOUT["room_depth"] != ROOM_DEPTH or
            WINDOW_LAYOUT["glass_inset"] != 0.18):
        problems.append("the shared window table changed the established room envelope")
    if len(WINDOWS) != 16 or len({row["stable_id"] for row in WINDOWS}) != 16:
        problems.append("the shared window table requires sixteen distinct openings")
    for wall in ("north", "south", "east", "west"):
        for floor in (0.0, UPPER_FLOOR_ELEVATION):
            if sum(row["wall"] == wall and row["floor_elevation"] == floor
                   for row in WINDOWS) != 2:
                problems.append(f"{wall} floor {floor} must have two real windows")

    by_name = {part.name: part for part in asset.parts}
    wall_vertices, wall_faces = merge(part.geometry for part in asset.parts
        if part.role in {"room_wall", "camera_cutaway_wall", "upper_wall"})
    wall_tree = BVHTree.FromPolygons(wall_vertices, wall_faces)
    directions = {"north": (0.0, 0.0, 1.0), "south": (0.0, 0.0, -1.0),
                  "east": (1.0, 0.0, 0.0), "west": (-1.0, 0.0, 0.0)}
    for row in WINDOWS:
        glass = by_name.get(row["glass_part"])
        frame = by_name.get(row["frame_part"])
        if glass is None or frame is None:
            problems.append(f"{row['stable_id']} has no matching frame and glass")
            continue
        if any(abs(a - b) > 0.002 for a, b in
               zip(centre_for(glass), row["center_unity"])):
            problems.append(f"{row['stable_id']} glass has left its shared plan position")
        if (row["sill"] < 0.8 or row["head"] >
                (ROOM_HEIGHT if row["floor_elevation"] == 0.0 else
                 UPPER_CEILING_HEIGHT - UPPER_FLOOR_ELEVATION) - 0.10):
            problems.append(f"{row['stable_id']} does not fit its floor and lintel")
        outward = Vector(directions[row["wall"]])
        along = Vector((1.0, 0.0, 0.0) if row["wall"] in ("north", "south")
                       else (0.0, 0.0, 1.0))
        centre = Vector(row["center_unity"])
        frame_tree = BVHTree.FromPolygons(*frame.geometry)
        glass_tree = BVHTree.FromPolygons(*glass.geometry)
        ray_origin = Vector(source_point(centre - outward * 0.50))
        ray_direction = Vector(source_point(outward))
        frame_hit = frame_tree.ray_cast(ray_origin, ray_direction, 0.70)
        glass_hit = glass_tree.ray_cast(ray_origin, ray_direction, 0.70)
        if (frame_hit[0] is None or glass_hit[0] is None or
                frame_hit[3] >= glass_hit[3] - 0.002):
            problems.append(f"{row['stable_id']} mullion is hidden behind its opaque glass")
        # Nine rays through each clear aperture prove that a full authored
        # wall actually has a hole. Hiding a near wall cannot satisfy this.
        for across in (-0.38, 0.0, 0.38):
            for vertical in (-0.38, 0.0, 0.38):
                sample = (centre + along * row["width"] * across +
                          Vector((0.0, row["height"] * vertical, 0.0)))
                origin = source_point(sample - outward * 0.30)
                hit = wall_tree.ray_cast(Vector(origin), Vector(source_point(outward)), 0.70)
                if hit[0] is not None:
                    problems.append(f"{row['stable_id']} has an opaque wall across its aperture")
                    break


def validate(asset: AssetBuild) -> dict:
    problems: list[str] = []
    names: set[str] = set()
    forbidden = (
        "medicine", "medication", "diagnosis", "hospital", "father",
        "family_photo", "photograph", "letter", "newspaper", "memorial",
        "shrine", "religious", "dementia",
    )
    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate part name '{part.name}'")
        names.add(part.name)
        if part.sheet not in SHEET_PITCH:
            problems.append(f"'{part.name}' uses unknown sheet '{part.sheet}'")
        if bp.signed_volume(part.geometry) <= 1e-8:
            problems.append(
                f"'{part.name}' has inverted/open winding "
                f"({bp.signed_volume(part.geometry):.8f})")
        semantic = f"{part.name} {part.role}".lower()
        for token in forbidden:
            if token in semantic:
                problems.append(f"'{part.name}' contains forbidden cue '{token}'")

    expected_names = {
        "FIX_WindowGlass.West", "FIX_WindowGlass.East",
        "FIX_Fire.Embers", "FIX_Fire.Flame.Back", "FIX_Fire.Flame.Front",
        "FIX_Table.Top", "FIX_RockingChair.Frame", "FIX_Sofa.Frame",
        "FIX_Door.Frame", "FIX_Door.OpenLeaf",
        "FIX_InterstoreyCeiling", "FIX_UpperFloor",
        "FIX_Stair.Steps", "FIX_Stair.Rail", "FIX_UpperWalls",
        "FIX_UpperPartitions", "FIX_UpperDoorFrames",
        "FIX_UpperStairGuards", "FIX_UpperCeiling",
        "DRESS_FloorLamp.Frame", "DRESS_FloorLamp.Shade",
        "DRESS_FloorLamp.Bulb",
        "FIX_UpperChimney",
        "FIX_UpperNorth.WindowFrame", "FIX_UpperNorth.WindowGlass",
        "FIX_UpperSouth.WindowFrame", "FIX_UpperSouth.WindowGlass",
        "DRESS_UpperNorth.Curtain", "DRESS_UpperSouth.Curtain",
        "FIX_UpperNorth.Bed", "DRESS_UpperNorth.Bedding", "DRESS_UpperNorth.Coverlet",
        "DRESS_UpperNorth.Chest", "DRESS_UpperNorth.Bedside",
        "DRESS_UpperNorth.Washstand",
        "DRESS_UpperNorth.Rug", "DRESS_UpperNorth.CeilingLamp",
        "DRESS_UpperNorth.LampBulb",
        "FIX_UpperSouth.Bed", "DRESS_UpperSouth.Bedding",
        "DRESS_UpperSouth.DustSheet", "DRESS_UpperSouth.Casework",
        "DRESS_UpperSouth.WoodenToy", "DRESS_UpperSouth.RolledRug",
        "DRESS_UpperSouth.CeilingLamp", "DRESS_UpperSouth.LampBulb",
        "DRESS_UpperCorridor.Runner", "DRESS_UpperCorridor.PegRail",
        "DRESS_UpperCorridor.HangingShawl",
        "DRESS_UpperCorridor.LinenChest",
        "FIX_UpperNorth.Wardrobe", "DRESS_UpperNorth.Trunk",
        "DRESS_UpperNorth.TrunkLinen", "DRESS_UpperNorth.Chair",
        "DRESS_UpperNorth.ChairClothes", "DRESS_UpperNorth.PegRail",
        "DRESS_UpperNorth.HangingRobe", "DRESS_UpperNorth.Slippers",
        "DRESS_UpperSouth.LinenPress", "DRESS_UpperSouth.PressSheet",
        "DRESS_UpperSouth.Table", "DRESS_UpperSouth.Trunk",
        "DRESS_UpperSouth.TrunkBlankets", "DRESS_UpperSouth.Basket",
        "DRESS_UpperCorridor.HighShelf", "DRESS_UpperCorridor.ShelfLinen",
        "DRESS_UpperCorridor.Pail", "FIX_UpperSkirting",
    }
    for missing in sorted(expected_names - names):
        problems.append(f"required semantic part '{missing}' is missing")

    validate_window_layout(asset, problems)

    required_roles = {
        "floor", "room_wall", "camera_cutaway_wall", "entrance_frame",
        "entrance_door", "window_frame", "window_glass",
        "fireplace_stone", "firebox", "fire_embers", "fire_flame",
        "tea_table_top", "tea_table_frame", "tea_service", "tea_tray",
        "rocking_chair", "sofa_frame", "sofa_cushion", "worn_rug",
        "old_cupboard", "wall_clock", "yarn_basket", "slippers",
        "firewood", "fireplace_tool", "floor_lamp", "floor_lamp_shade",
        "floor_lamp_bulb", "interstorey_ceiling", "upper_floor",
        "stair", "stair_guard", "upper_wall", "upper_partition",
        "upper_door_frame", "upper_ceiling",
        "upper_chimney", "upper_window_frame", "upper_window_glass",
        "upper_window_curtain", "upper_double_bed", "upper_double_bedding",
        "upper_double_coverlet",
        "upper_bedroom_chest", "upper_bedroom_bedside",
        "upper_washstand", "upper_bedside_rug",
        "upper_ceiling_lamp", "upper_lamp_bulb", "upper_child_bed",
        "upper_child_bedding", "upper_dust_sheet", "upper_child_casework",
        "upper_wooden_toy", "upper_rolled_rug",
        "upper_child_lamp", "upper_child_lamp_bulb",
        "upper_corridor_runner",
        "upper_peg_rail", "upper_hanging_cloth", "upper_linen_chest",
        "upper_wardrobe", "upper_bedroom_trunk", "upper_folded_linen",
        "upper_bedroom_chair", "upper_folded_clothes", "upper_bedroom_pegs",
        "upper_hanging_robe", "upper_slippers", "upper_linen_press",
        "upper_press_sheet", "upper_child_table", "upper_child_trunk",
        "upper_folded_blankets", "upper_laundry_basket",
        "upper_corridor_shelf", "upper_shelf_linen",
        "upper_corridor_pail", "upper_skirting",
    }
    roles = {part.role for part in asset.parts}
    for missing in sorted(required_roles - roles):
        problems.append(f"required role '{missing}' is absent")

    if set(asset.anchors) != set(ANCHORS_UNITY):
        problems.append("the exact ten-anchor contract drifted")
    for name, expected_unity in ANCHORS_UNITY.items():
        anchor = asset.anchors.get(name)
        if anchor is None:
            continue
        actual_unity = unity_point(anchor.location)
        if any(abs(actual_unity[i] - expected_unity[i]) > 1e-6
               for i in range(3)):
            problems.append(
                f"anchor '{name}' is {actual_unity}, expected {expected_unity}")

    merged = kit.merge_all(part.geometry for part in asset.parts)
    source_low, source_high = kit.bounds(merged)
    unity_low, unity_high = source_bounds_to_unity((source_low, source_high))
    expected_low = (-5.12, -0.18, -4.12)
    expected_high = (5.12, 6.04, 4.12)
    for index, label in enumerate(("Unity X", "Unity Y", "Unity Z")):
        if abs(unity_low[index] - expected_low[index]) > 0.002:
            problems.append(
                f"{label} low is {unity_low[index]:.4f}, "
                f"expected {expected_low[index]:.4f}")
        if abs(unity_high[index] - expected_high[index]) > 0.002:
            problems.append(
                f"{label} high is {unity_high[index]:.4f}, "
                f"expected {expected_high[index]:.4f}")

    by_name = {part.name: part for part in asset.parts}
    stair_low, stair_high = source_bounds_to_unity(
        kit.bounds(by_name["FIX_Stair.Steps"].geometry)
    )
    if stair_low[0] > STAIR_OPENING[0] + 0.002:
        problems.append("the stair's west closure no longer reaches the wall")
    expected_stair_south = -ROOM_DEPTH * 0.5 + WALL_THICKNESS * 0.5
    if stair_low[2] > expected_stair_south + 0.002:
        problems.append("the stair's south closure no longer reaches the wall")
    expected_stair_east = STAIR_CENTER_X + STAIR_WIDTH * 0.5
    if abs(stair_high[0] - expected_stair_east) > 0.002:
        problems.append("the fixed stair moved while extending its west closure")
    table = centre_for(by_name["FIX_Table.Top"])
    rocker = centre_for(by_name["FIX_RockingChair.Frame"])
    sofa = centre_for(by_name["FIX_Sofa.Frame"])
    fireplace = centre_for(by_name["FIX_Fireplace.Stonework"])
    west_window = centre_for(by_name["FIX_WindowGlass.West"])
    east_window = centre_for(by_name["FIX_WindowGlass.East"])
    door_frame = centre_for(by_name["FIX_Door.Frame"])
    door_leaf = centre_for(by_name["FIX_Door.OpenLeaf"])
    floor_lamp = centre_for(by_name["DRESS_FloorLamp.Shade"])
    skirting_vertices, skirting_faces = by_name["FIX_Skirting"].geometry
    south_skirting_x = []
    for vertex in skirting_vertices:
        unity_vertex = unity_point(vertex)
        if unity_vertex[2] < -3.80:
            south_skirting_x.append(unity_vertex[0])
    door_low = DOOR_CENTER_X - DOOR_WIDTH * 0.5
    door_high = DOOR_CENTER_X + DOOR_WIDTH * 0.5
    if (not any(value <= door_low + 0.002 for value in south_skirting_x) or
            not any(value >= door_high - 0.002 for value in south_skirting_x)):
        problems.append("south skirting no longer reaches both door jambs")
    if any(door_low + 0.002 < value < door_high - 0.002
           for value in south_skirting_x):
        problems.append("south skirting crosses the door clear width")
    for face in skirting_faces:
        unity_face = [unity_point(skirting_vertices[index]) for index in face]
        if (all(point[2] < -3.80 for point in unity_face) and
                min(point[0] for point in unity_face) < door_high - 0.002 and
                max(point[0] for point in unity_face) > door_low + 0.002):
            problems.append("a skirting face bridges the south door opening")
            break
    if abs(table[0]) > 0.02 or abs(table[2]) > 0.02:
        problems.append("the low tea table is no longer centred")
    if rocker[2] <= table[2] + 0.70:
        problems.append("the rocking chair is not north of the table")
    if sofa[0] >= table[0] - 1.20:
        problems.append("the sofa is not west of the table")
    if fireplace[2] < 3.0 or abs(fireplace[0]) > 0.08:
        problems.append("the fireplace left the centre of the north wall")
    if west_window[0] >= -2.0 or east_window[0] <= 2.0:
        problems.append("the two windows no longer flank the fireplace")
    if abs(west_window[2] - east_window[2]) > 0.001:
        problems.append("the north windows no longer share one wall plane")
    if abs(door_frame[0] - DOOR_CENTER_X) > 0.002 or door_frame[2] > -3.90:
        problems.append("the entrance frame is not centred in the south wall")
    if door_leaf[2] > -3.70 or door_leaf[0] <= DOOR_WIDTH * 0.5:
        problems.append("the open entrance leaf is not flat east of the south opening")

    # The furnished storey above.  The two rooms sit directly over the room
    # below and share every X/Z coordinate with it, so each check names the
    # room it means.
    double_bed = bounds_for(by_name["FIX_UpperNorth.Bed"])
    child_bed = bounds_for(by_name["FIX_UpperSouth.Bed"])
    flue = bounds_for(by_name["FIX_UpperChimney"])
    if double_bed[0][2] <= 0.0 or child_bed[1][2] >= 0.0:
        problems.append("a bed is standing in the wrong upper room")
    if double_bed[1][0] - double_bed[0][0] < 1.40:
        problems.append("the parents' bed is no longer a double")
    if child_bed[1][0] - child_bed[0][0] > 1.00:
        problems.append("the childhood bed is no longer a single")
    if child_bed[1][2] - child_bed[0][2] >= double_bed[1][2] - double_bed[0][2]:
        problems.append("the childhood bed is not shorter than the double")
    if abs(flue[0][0] + flue[1][0]) > 0.16:
        problems.append("the upper flue left the hearth's own centre line")
    if (abs(flue[0][1] - UPPER_FLOOR_ELEVATION) > 0.002 or
            abs(flue[1][1] - UPPER_CEILING_HEIGHT) > 0.002):
        problems.append("the upper flue no longer spans floor to ceiling")

    # The play-mode walk teleports the capsule to the exact centre of each
    # upper room. Furniture standing there does not fail an assert - it jams
    # the controller, so the clearance is pinned here where it is cheap.
    capsule_radius = 0.32
    capsule_height = 1.70
    blocking_roles = {
        "upper_chimney", "upper_double_bed", "upper_bedroom_chest",
        "upper_bedroom_bedside", "upper_child_bed", "upper_child_casework",
        "upper_wardrobe", "upper_bedroom_trunk", "upper_bedroom_chair",
        "upper_linen_press", "upper_child_table", "upper_child_trunk",
        "upper_laundry_basket",
    }
    room_centres = (
        ("south", UPPER_SOUTH_ROOM_X, UPPER_SOUTH_ROOM_Z),
        ("north", UPPER_NORTH_ROOM_X, UPPER_NORTH_ROOM_Z),
    )
    for room_name, span_x, span_z in room_centres:
        centre_x = (span_x[0] + span_x[1]) * 0.5
        centre_z = (span_z[0] + span_z[1]) * 0.5
        for part in asset.parts:
            if part.role not in blocking_roles:
                continue
            low, high = bounds_for(part)
            if low[2] > span_z[1] or high[2] < span_z[0]:
                continue
            if low[1] > UPPER_FLOOR_ELEVATION + capsule_height:
                continue
            near_x = min(max(centre_x, low[0]), high[0])
            near_z = min(max(centre_z, low[2]), high[2])
            gap = math.hypot(near_x - centre_x, near_z - centre_z)
            if gap < capsule_radius:
                problems.append(
                    f"'{part.name}' stands {gap:.3f} m from the "
                    f"{room_name} room centre the player walks to")

    entry = ANCHORS_UNITY["ANCHOR_Entry"]
    spawn = ANCHORS_UNITY["ANCHOR_Spawn"]
    exit_anchor = ANCHORS_UNITY["ANCHOR_Exit"]
    if any(abs(anchor[0] - DOOR_CENTER_X) > 1e-6
           for anchor in (entry, exit_anchor, spawn)):
        problems.append("entry, exit and spawn must share the south-door axis")
    if not entry[2] < exit_anchor[2] < spawn[2] < -2.0:
        problems.append("south-door anchors no longer lead safely north")
    if SPAWN_FORWARD_UNITY != (0.0, 0.0, 1.0):
        problems.append("the player spawn no longer faces north")

    floor_light = ANCHORS_UNITY["ANCHOR_FloorLampLight"]
    if (abs(floor_lamp[0] - floor_light[0]) > 0.002 or
            abs(floor_lamp[2] - floor_light[2]) > 0.002 or
            abs(floor_lamp[1] - 1.54) > 0.002):
        problems.append("the floor-lamp light left its visible shade")

    if ANCHORS_UNITY["ANCHOR_Camera"] != (5.80, 2.75, -2.80):
        problems.append("the approved fixed camera position changed")
    if ANCHORS_UNITY["ANCHOR_CameraTarget"] != (-0.20, 0.80, 1.00):
        problems.append("the approved fixed camera target changed")

    stair_run = STAIR_STEP_COUNT * STAIR_STEP_DEPTH
    stair_top_z = STAIR_START_Z + STAIR_DIRECTION_Z * stair_run
    stair_pitch = math.degrees(math.atan2(
        UPPER_FLOOR_ELEVATION,
        stair_run,
    ))
    opening_x_min, opening_z_min, opening_x_max, opening_z_max = (
        STAIR_OPENING
    )
    if abs(stair_top_z - (-2.95)) > 1e-6:
        problems.append("the stair no longer reaches its south landing")
    if STAIR_STEP_RISE >= 0.28 or stair_pitch >= 45.0:
        problems.append("the stair exceeds the player controller contract")
    if (STAIR_CENTER_X - STAIR_WIDTH * 0.5 < opening_x_min or
            STAIR_CENTER_X + STAIR_WIDTH * 0.5 > opening_x_max or
            min(STAIR_START_Z, stair_top_z) < opening_z_min or
            max(STAIR_START_Z, stair_top_z) > opening_z_max):
        problems.append("the stair no longer fits its real floor opening")
    if UPPER_PARTITION_X - opening_x_max < 1.20:
        problems.append("the upper corridor is narrower than 1.2 metres")
    if len(UPPER_DOOR_CENTERS_Z) != 2 or UPPER_DOOR_WIDTH < 1.20:
        problems.append("the upper storey requires exactly two clear doors")
    if UPPER_CEILING_HEIGHT - UPPER_FLOOR_ELEVATION < 2.20:
        problems.append("the upper rooms lost standing clearance")

    table_top = ANCHORS_UNITY["ANCHOR_Tabletop"]
    teapot = ANCHORS_UNITY["ANCHOR_TeapotDock"]
    if abs(teapot[1] - (TABLE_HEIGHT + 0.03)) > 1e-6:
        problems.append("the kettle dock no longer rests above the tabletop")
    if abs(teapot[0] - table_top[0]) > TABLE_WIDTH * 0.5 - 0.18:
        problems.append("the kettle dock leaves the tabletop width")
    if abs(teapot[2] - table_top[2]) > TABLE_DEPTH * 0.5 - 0.14:
        problems.append("the kettle dock leaves the tabletop depth")

    triangles = kit.triangle_count(merged)
    if len(asset.parts) > 136:
        problems.append(f"{len(asset.parts)} meshes exceed the 136-mesh cap")
    if triangles > 17000:
        problems.append(f"{triangles} triangles exceed the 17000 cap")

    if problems:
        raise SystemExit(
            "Mother's house interior validation failed:\n  " +
            "\n  ".join(problems))
    return {
        "bounds_min": [stable(value) for value in source_low],
        "bounds_max": [stable(value) for value in source_high],
        "unity_bounds_min": [stable(value) for value in unity_low],
        "unity_bounds_max": [stable(value) for value in unity_high],
        "mesh_count": len(asset.parts),
        "triangle_count": triangles,
    }


def signature_for(asset: AssetBuild) -> str:
    payload = {
        "design_id": DESIGN_ID,
        "generator_version": GENERATOR_VERSION,
        "dimensions": [ROOM_WIDTH, ROOM_DEPTH, ROOM_HEIGHT],
        "upper_storey": [
            UPPER_FLOOR_ELEVATION,
            UPPER_CEILING_HEIGHT,
            STAIR_OPENING,
            UPPER_DOOR_CENTERS_Z,
        ],
        "wall_thickness": WALL_THICKNESS,
        "door": ["south", DOOR_CENTER_X, DOOR_WIDTH, DOOR_HEIGHT],
        "spawn_forward_unity": list(SPAWN_FORWARD_UNITY),
        "windows": WINDOWS,
        "parts": [
            {
                "name": part.name,
                "role": part.role,
                "group": part.group,
                "sheet": part.sheet,
                "emissive": part.emissive,
                "casts_shadows": part.casts_shadows,
                "tint": [stable(value) for value in part.tint],
                "vertices": [
                    [stable(value) for value in vertex]
                    for vertex in part.geometry[0]
                ],
                "faces": [list(face) for face in part.geometry[1]],
            }
            for part in asset.parts
        ],
        "anchors": {
            name: {
                "role": anchor["bp_role"],
                "source": [stable(value) for value in anchor.location],
                "unity": [stable(value) for value in unity_point(anchor.location)],
            }
            for name, anchor in sorted(asset.anchors.items())
        },
    }
    encoded = json.dumps(
        payload,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def manifest_part(part: Part) -> dict:
    source_low, source_high = kit.bounds(part.geometry)
    unity_low, unity_high = source_bounds_to_unity((source_low, source_high))
    return {
        "name": part.name,
        "role": part.role,
        "group": part.group,
        "sheet": part.sheet,
        "emissive": part.emissive,
        "casts_shadows": part.casts_shadows,
        "tint": [stable(value) for value in part.tint],
        "colliders": [],
        "vertices": len(part.geometry[0]),
        "triangles": kit.triangle_count(part.geometry),
        "bounds_min": [stable(value) for value in source_low],
        "bounds_max": [stable(value) for value in source_high],
        "unity_bounds_min": [stable(value) for value in unity_low],
        "unity_bounds_max": [stable(value) for value in unity_high],
    }


def manifest_for(asset: AssetBuild, report: dict, signature: str) -> dict:
    return {
        "generator": "tools/build-mothers-house-interior-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "dimensions_m": {
            "width": ROOM_WIDTH,
            "depth": ROOM_DEPTH,
            "height": ROOM_HEIGHT,
        },
        "upper_storey_m": {
            "floor_elevation": UPPER_FLOOR_ELEVATION,
            "ceiling_height": UPPER_CEILING_HEIGHT,
            "stair_center_x": STAIR_CENTER_X,
            "stair_start_z": STAIR_START_Z,
            "stair_direction_z": STAIR_DIRECTION_Z,
            "stair_step_count": STAIR_STEP_COUNT,
            "stair_step_rise": STAIR_STEP_RISE,
            "stair_step_depth": STAIR_STEP_DEPTH,
            "stair_width": STAIR_WIDTH,
            "stair_opening": list(STAIR_OPENING),
            "corridor_clear_width": (
                UPPER_PARTITION_X - STAIR_OPENING[2]
            ),
            "door_width": UPPER_DOOR_WIDTH,
            "door_height": UPPER_DOOR_HEIGHT,
            "door_centers_z": list(UPPER_DOOR_CENTERS_Z),
            "room_count": 2,
            "furnished": True,
            "rooms": {
                "north": {
                    "use": "parents_bedroom",
                    "state": "lived_in",
                    "bed": "double",
                },
                "south": {
                    "use": "hero_childhood_room",
                    "state": "out_of_use",
                    "bed": "single",
                },
            },
            "windows": [
                {
                    "wall": "south",
                    "center_x": UPPER_SOUTH_WINDOW_X,
                    "width": UPPER_WINDOW_WIDTH,
                    "sill_above_floor": UPPER_WINDOW_SILL,
                    "head_above_floor": UPPER_WINDOW_HEAD,
                },
                {
                    "wall": "north",
                    "center_x": UPPER_NORTH_WINDOW_X,
                    "width": UPPER_WINDOW_WIDTH,
                    "sill_above_floor": UPPER_WINDOW_SILL,
                    "head_above_floor": UPPER_WINDOW_HEAD,
                },
            ],
            "ceiling_lamps": {
                "north": [
                    UPPER_NORTH_LAMP_CENTER[0],
                    UPPER_CEILING_HEIGHT - 0.385,
                    UPPER_NORTH_LAMP_CENTER[1],
                ],
                "south": [
                    UPPER_SOUTH_LAMP_CENTER[0],
                    UPPER_CEILING_HEIGHT - 0.435,
                    UPPER_SOUTH_LAMP_CENTER[1],
                ],
            },
        },
        "wall_thickness_m": WALL_THICKNESS,
        "floor_thickness_m": FLOOR_THICKNESS,
        "ceiling_thickness_m": CEILING_THICKNESS,
        "door_opening_m": {
            "wall": "south",
            "center_x": DOOR_CENTER_X,
            "width": DOOR_WIDTH,
            "height": DOOR_HEIGHT,
        },
        "windows_m": WINDOWS,
        "window_layout_source": "ArtSource/MothersHouse/WindowLayout.json",
        "north_windows_m": [
            {
                "center_x": x,
                "width": WINDOW_WIDTH,
                "sill": WINDOW_SILL,
                "head": WINDOW_HEAD,
            }
            for x in WINDOW_CENTERS_X
        ],
        "composition_m": {
            "table_center": [0.0, 0.0, 0.0],
            "table_size": [TABLE_WIDTH, TABLE_HEIGHT, TABLE_DEPTH],
            "rocking_chair_center": list(ROCKER_CENTER),
            "sofa_center": list(SOFA_CENTER),
            "fireplace_wall": "north",
            "floor_lamp_light": list(
                ANCHORS_UNITY["ANCHOR_FloorLampLight"]),
            "upper_room_count": 2,
            "upper_rooms_furnished": True,
        },
        "spawn_forward_unity": list(SPAWN_FORWARD_UNITY),
        # "mother" left this list on 2026-09-01: she is present in the chair
        # by an accepted architecture exception and a new §6 registry row.
        # Her PRESENCE is what was lifted - the event was not, so the dinner,
        # the news, the Cat and every line stay excluded, and so do the props
        # that would state a diagnosis.
        "excluded_story_content": [
            "cat", "dialogue", "dinner_event", "bidon",
            "medicine", "family_photographs", "readable_text",
        ],
        "kettle_contract": {
            "geometry_included": False,
            "runtime_source": "Pedestrians/KettleHatPedestrian3D",
            "dock_anchor": "ANCHOR_TeapotDock",
        },
        "source_axes": {"right": "+X", "forward": "+Y", "up": "+Z"},
        "unity_axes": {
            "right": "+X",
            "forward": "+Z",
            "up": "+Y",
            "fbx_axis_forward": "-Z",
            "fbx_axis_up": "Y",
            "bake_space_transform": False,
        },
        "root_contract": {
            "origin": "room_center_floor",
            "scale_mode": "fixed_meters",
            "axis_conversion": "swap_y_z_and_reverse_winding",
            "preserve_imported_root_scale": True,
            "anchor_access": "world_position",
        },
        "runtime_wrapper_yaw_degrees": 0.0,
        "colliders": False,
        "lights": False,
        "cameras": False,
        "animation_count": 0,
        **report,
        "anchors": [
            {
                "name": name,
                "role": anchor["bp_role"],
                "local_position": [stable(value) for value in anchor.location],
                "unity_local_position": [
                    stable(value) for value in unity_point(anchor.location)
                ],
            }
            for name, anchor in sorted(asset.anchors.items())
        ],
        "parts": [manifest_part(part) for part in asset.parts],
        "build_signature": signature,
    }


def write_manifest(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


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


def render_preview(asset: AssetBuild, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.new("PRESENTATION_MothersHouseInterior")
    scene.collection.children.link(presentation)

    camera_data = bpy.data.cameras.new("PreviewCamera")
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    presentation.objects.link(camera)
    camera.location = source_point(ANCHORS_UNITY["ANCHOR_Camera"])
    target = Vector(source_point(ANCHORS_UNITY["ANCHOR_CameraTarget"]))
    camera.rotation_euler = (target - camera.location).to_track_quat(
        "-Z", "Y").to_euler()
    camera_data.lens = 22.0
    camera_data.sensor_width = 36.0
    scene.camera = camera

    def add_light(
        name: str,
        kind: str,
        unity_position: Sequence[float],
        energy: float,
        color: Sequence[float],
        size: float,
    ) -> None:
        data = bpy.data.lights.new(name, kind)
        data.energy = energy
        data.color = tuple(color)
        if kind == "POINT":
            data.shadow_soft_size = size
        elif kind == "AREA":
            data.shape = "DISK"
            data.size = size
        obj = bpy.data.objects.new(name, data)
        presentation.objects.link(obj)
        obj.location = source_point(unity_position)
        if kind == "AREA":
            aim = Vector(source_point((0.0, 0.8, 0.0)))
            obj.rotation_euler = (aim - obj.location).to_track_quat(
                "-Z", "Y").to_euler()

    add_light(
        "PreviewFire",
        "POINT",
        ANCHORS_UNITY["ANCHOR_FireLight"],
        920.0,
        (1.0, 0.30, 0.075),
        0.55,
    )
    add_light(
        "PreviewFloorLamp",
        "POINT",
        ANCHORS_UNITY["ANCHOR_FloorLampLight"],
        135.0,
        (1.0, 0.48, 0.18),
        0.32,
    )
    add_light(
        "PreviewWindowFill",
        "AREA",
        (1.8, 2.8, 3.15),
        280.0,
        (0.32, 0.45, 0.56),
        4.5,
    )
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    scene.render.film_transparent = False
    scene.world.color = (0.012, 0.014, 0.018)
    scene.view_settings.look = "AgX - Medium High Contrast"
    bpy.ops.render.render(write_still=True)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blend", type=Path, default=DEFAULT_BLEND)
    parser.add_argument("--fbx", type=Path, default=DEFAULT_FBX)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--preview", type=Path, default=DEFAULT_PREVIEW)
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    effective = argv[argv.index("--") + 1:] if "--" in argv else []
    return parser.parse_args(effective)


def main() -> int:
    args = parse_args(list(sys.argv))
    asset = build()
    report = validate(asset)
    signature = signature_for(asset)
    if args.validate_only:
        if (not WINDOW_RUNTIME_PATH.is_file() or
                WINDOW_RUNTIME_PATH.read_text(encoding="utf-8") != window_runtime_source()):
            raise SystemExit("The generated runtime window contract does not match WindowLayout.json")
        print(
            "MOTHER'S HOUSE INTERIOR 3D VALID: "
            f"{report['mesh_count']} meshes / "
            f"{report['triangle_count']} triangles, "
            f"signature {signature[:16]}")
        return 0

    WINDOW_RUNTIME_PATH.write_text(window_runtime_source(), encoding="utf-8", newline="\n")
    manifest = manifest_for(asset, report, signature)
    write_manifest(args.manifest, manifest)
    export_fbx(asset, args.fbx)
    if not args.no_preview:
        render_preview(asset, args.preview)
    save_blend(args.blend)
    print(
        "Mother's house interior written: "
        f"{report['mesh_count']} meshes / "
        f"{report['triangle_count']} triangles, "
        f"signature {signature[:16]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
