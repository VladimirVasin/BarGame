#!/usr/bin/env python3
"""Build the fixed-camera interior of the mother's house.

The model is a quiet, ordinary lived-in room: one broad cared-for room, the
fireplace and two windows on its north wall, the low tea table at its centre,
the rocking chair north of it, the sofa to its west, a centred south entrance
and one old warm practical floor lamp.  Story actors, text, medicine, family
photographs and the kettle are intentionally absent.  The table kettle is
instantiated by Unity from the existing kettle-head NPC prefab; this asset
publishes only ``ANCHOR_TeapotDock`` for it.

Run from the repository root with Blender 5::

    blender --background --factory-startup --python \
      tools/build-mothers-house-interior-3d-model.py -- --validate-only

    blender --background --factory-startup --python \
      tools/build-mothers-house-interior-3d-model.py

Dimensions below are Unity-local metres: +X east, +Y up, +Z north.  Geometry
made through ``bar_parts`` is converted to Blender source space by swapping Y/Z
and re-winding every face.  Shell and profiled furniture made directly through
``interior_kit`` already use Blender's Z-up source frame.  Validation measures
both forms and rejects non-positive signed volume, mirrored anchors and drift
from the 10 x 8 m usable room contract.
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
GENERATOR_VERSION = "1.3.0"
DESIGN_ID = "mothers_house_interior_v1"
DISPLAY_NAME = "Bar Promenade Mother's House Interior"

ROOM_WIDTH = 10.0
ROOM_DEPTH = 8.0
ROOM_HEIGHT = 3.4
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
    "ANCHOR_Camera": (5.80, 3.15, -2.80),
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


def build_shell(asset: AssetBuild, materials: dict) -> None:
    south_openings = [
        kit.Opening(DOOR_CENTER_X, DOOR_WIDTH, DOOR_HEIGHT, 0.0)
    ]
    north_openings = [
        kit.Opening(x, WINDOW_WIDTH, WINDOW_HEAD, WINDOW_SILL)
        for x in WINDOW_CENTERS_X
    ]
    walls = kit.rectangular_room_walls(
        ROOM_WIDTH,
        ROOM_DEPTH,
        ROOM_HEIGHT,
        WALL_THICKNESS,
        front_openings=south_openings,
        back_openings=north_openings,
    )
    for key in ("front", "back", "left"):
        add_part(
            asset,
            materials,
            f"FIX_Wall.{key.title()}",
            walls[key],
            "room_wall",
            "Wallpaper",
            unity_space=False,
        )

    # The east wall remains the fixed-camera near-wall cutaway, but it is now
    # continuous: the only entrance is centred in the south wall, directly
    # opposite the north fireplace.  Collision stays plan-owned.
    east_low = kit.chamfered_box(
        (ROOM_WIDTH * 0.5, 0.0, 0.52),
        (WALL_THICKNESS, ROOM_DEPTH, 1.04),
        0.012,
    )
    add_part(
        asset,
        materials,
        "FIX_Wall.EastCutaway",
        east_low,
        "camera_cutaway_wall",
        "Wallpaper",
        unity_space=False,
    )

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
    ceiling = kit.ceiling_slab(
        ROOM_WIDTH,
        ROOM_DEPTH,
        CEILING_THICKNESS,
        ROOM_HEIGHT,
    )
    add_part(
        asset,
        materials,
        "FIX_Ceiling",
        ceiling,
        "ceiling",
        "CeilingPlaster",
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
                (0.0, -0.045, (WINDOW_HEAD - WINDOW_SILL) * 0.5),
                (0.055, 0.055, WINDOW_HEAD - WINDOW_SILL - 0.08),
                0.008,
            ),
            kit.chamfered_box(
                (0.0, -0.045, (WINDOW_HEAD - WINDOW_SILL) * 0.52),
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
        "DRESS_FloorLamp.Frame", "DRESS_FloorLamp.Shade",
        "DRESS_FloorLamp.Bulb",
    }
    for missing in sorted(expected_names - names):
        problems.append(f"required semantic part '{missing}' is missing")

    required_roles = {
        "floor", "room_wall", "camera_cutaway_wall", "entrance_frame",
        "entrance_door", "window_frame", "window_glass",
        "fireplace_stone", "firebox", "fire_embers", "fire_flame",
        "tea_table_top", "tea_table_frame", "tea_service", "tea_tray",
        "rocking_chair", "sofa_frame", "sofa_cushion", "worn_rug",
        "old_cupboard", "wall_clock", "yarn_basket", "slippers",
        "firewood", "fireplace_tool", "floor_lamp", "floor_lamp_shade",
        "floor_lamp_bulb",
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
    expected_high = (5.12, 3.54, 4.12)
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

    if ANCHORS_UNITY["ANCHOR_Camera"] != (5.80, 3.15, -2.80):
        problems.append("the approved fixed camera position changed")
    if ANCHORS_UNITY["ANCHOR_CameraTarget"] != (-0.20, 0.80, 1.00):
        problems.append("the approved fixed camera target changed")

    table_top = ANCHORS_UNITY["ANCHOR_Tabletop"]
    teapot = ANCHORS_UNITY["ANCHOR_TeapotDock"]
    if abs(teapot[1] - (TABLE_HEIGHT + 0.03)) > 1e-6:
        problems.append("the kettle dock no longer rests above the tabletop")
    if abs(teapot[0] - table_top[0]) > TABLE_WIDTH * 0.5 - 0.18:
        problems.append("the kettle dock leaves the tabletop width")
    if abs(teapot[2] - table_top[2]) > TABLE_DEPTH * 0.5 - 0.14:
        problems.append("the kettle dock leaves the tabletop depth")

    triangles = kit.triangle_count(merged)
    if len(asset.parts) > 64:
        problems.append(f"{len(asset.parts)} meshes exceed the 64-mesh cap")
    if triangles > 14000:
        problems.append(f"{triangles} triangles exceed the 14000 cap")

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
        "wall_thickness": WALL_THICKNESS,
        "door": ["south", DOOR_CENTER_X, DOOR_WIDTH, DOOR_HEIGHT],
        "spawn_forward_unity": list(SPAWN_FORWARD_UNITY),
        "windows": [list(WINDOW_CENTERS_X), WINDOW_WIDTH, WINDOW_SILL,
                    WINDOW_HEAD],
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
        "wall_thickness_m": WALL_THICKNESS,
        "floor_thickness_m": FLOOR_THICKNESS,
        "ceiling_thickness_m": CEILING_THICKNESS,
        "door_opening_m": {
            "wall": "south",
            "center_x": DOOR_CENTER_X,
            "width": DOOR_WIDTH,
            "height": DOOR_HEIGHT,
        },
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
        },
        "spawn_forward_unity": list(SPAWN_FORWARD_UNITY),
        "excluded_story_content": [
            "mother", "cat", "dialogue", "dinner_event", "bidon",
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
        print(
            "MOTHER'S HOUSE INTERIOR 3D VALID: "
            f"{report['mesh_count']} meshes / "
            f"{report['triangle_count']} triangles, "
            f"signature {signature[:16]}")
        return 0

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
