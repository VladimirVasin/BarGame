#!/usr/bin/env python3
"""Build the compact urban bar and its existing exterior.

Every visible interior object is authored here: the panelled L counter,
brass rails and taps, mirrored backbar, snug screens, booths, small pub
tables, carpet fields, music nook, grounded service markers, reusable
service props, jukebox, fan and practical lights.  Unity owns
only behaviour, placement state, logical collision and lighting.

The late-Victorian exterior remains fixed; the ``11.4 x 7.8 x 3.0 m`` room
fits its main ground-floor range. The interior is a compact, worn urban public
house rather than a themed attraction: there are no flags, brands, pub
name, legible advertising or tourist props.  The manifest records every
fixed-metre part and semantic anchor so the Unity contract can be proven
against the layout plan.

Run with Blender 5 from the repository root::

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-bar-3d-model.py

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-bar-3d-model.py -- --validate-only

Source space is metres, Z up, forward -Y.  Unity's axes are reached by
SWAPPING the last two - see `tools/bar_parts.py`, which also explains why
that reflection forces every face to be re-wound.  The mapping is
asserted by `BarModelContractTests` against the layout plan's own
stations rather than trusted; an imported basis has misled this project
eight times, once in this file.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

sys.dont_write_bytecode = True

try:
    import bpy
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import interior_kit as kit  # noqa: E402  (after the sys.path fix)
import bar_parts as bp  # noqa: E402
import bar_exterior as exterior  # noqa: E402
import bar_compact_interior as compact  # noqa: E402
import bar_washroom as washroom  # noqa: E402

INTERIOR_GENERATOR_VERSION = "4.1.1"
DESIGN_ID = "bar_interior_v3"
DISPLAY_NAME = "Bar Promenade Bar Interior"

# --- the layout contract, mirrored from BarInteriorLayoutPlanner --------
ROOM_WIDTH = 11.4
ROOM_DEPTH = 7.8
ROOM_HEIGHT = 3.0
WALL_THICKNESS = 0.3
DOOR_WIDTH = exterior.DOOR_WIDTH
DOOR_HEIGHT = exterior.DOOR_HEIGHT

FLOOR_THICKNESS = 0.24
CEILING_THICKNESS = 0.20
CEILING_FAN_PIVOT = (0.0, 2.55, -0.65)
CEILING_FAN_BEAM_UNDERSIDE_Y = ROOM_HEIGHT - .18 - .34 * .5
# bp.u_cylinder uses half-height, matching the original Unity primitive.
CEILING_FAN_HUB_TOP_OFFSET_Y = .10 * .35

#: `plan.CounterPosition` / `plan.CounterSize`.
# Remove exactly the leftmost 0.90 m stool bay; keep the right end fixed.
COUNTER_POS = (0.875, 0.43, 1.95)
COUNTER_SIZE = (5.95, 0.86, 1.0)
COUNTER_STATION = (-0.75, 0.9, 0.55)
COUNTER_RETURN_POS = (3.57, 0.43, 1.55)
COUNTER_RETURN_SIZE = (1.04, 0.86, 1.8)
COUNTER_SURFACE_Y = (
    COUNTER_POS[1] + COUNTER_SIZE[1] * 0.5 + 0.16)
HERO_SEAT = (-0.75, 0.8175, 0.91)
HERO_APPROACH = (-0.75, 0.0, -0.27)
HERO_STAND = (-0.75, 0.0, 0.27)
BARTENDER_POSITION = (-0.15, 0.0, 2.85)
ACTIVITY_STATION = (2.7, 0.9, -0.45)
STOOL_XS = (-1.65, -0.75, 0.15, 1.05, 1.95)

#: The three decorative beer taps form one compact bank at the right end of
#: the main counter, above its overlap with the return. That end has no stool
#: beneath it; the last customer stool remains clear at x = 1.95.
BEER_TAP_CLUSTER_CENTER_X = COUNTER_RETURN_POS[0]
BEER_TAP_SPACING = 0.33
BEER_TAP_XS = tuple(
    BEER_TAP_CLUSTER_CENTER_X +
    (index - 1) * BEER_TAP_SPACING
    for index in range(3))
BEER_TAP_Z = COUNTER_POS[2]
BEER_TAP_STEM_TOP_Y = 1.58
BEER_TAP_BRANCH_Y = 1.52
BEER_TAP_SPOUT_Z = 2.33
BEER_TAP_SPOUT_Y = 1.45
BEER_TAP_HANDLE_TOP_Y = 1.88
BEER_TAP_SERVER_DOCK = (
    BEER_TAP_CLUSTER_CENTER_X, 0.0, 2.92)
BEER_TAP_VESSEL_DOCK = (
    BEER_TAP_CLUSTER_CENTER_X, COUNTER_SURFACE_Y + 0.005,
    BEER_TAP_SPOUT_Z)
BEER_TAP_STREAM_ORIGIN = (
    BEER_TAP_CLUSTER_CENTER_X, BEER_TAP_SPOUT_Y,
    BEER_TAP_SPOUT_Z)
BEER_TAP_HANDLE_PIVOT = (
    BEER_TAP_CLUSTER_CENTER_X, BEER_TAP_STEM_TOP_Y,
    BEER_TAP_Z)
BEER_TAP_HANDLE_GRIP = (
    BEER_TAP_CLUSTER_CENTER_X,
    BEER_TAP_STEM_TOP_Y +
    (BEER_TAP_HANDLE_TOP_Y - BEER_TAP_STEM_TOP_Y) * 0.72,
    BEER_TAP_Z)
SERVICE_MENU_DOCK_Z = 2.16
SERVICE_VESSEL_DOCK_Z = 1.63
SERVICE_PROP_MINIMUM_GAP = 0.08

#: Mountain Road Cafe stool silhouette, dimensions and palette. Every Bar
#: stool, including the hero's, is the same 0.8175 m stool rather than an
#: interaction prop with a different profile.
CAFE_STOOL_STEM_DIAMETER = 0.10
CAFE_STOOL_STEM_SEAT_OVERLAP = 0.0175
CAFE_STOOL_FOOT_DIAMETER = 0.34
CAFE_STOOL_FOOT_HALF_HEIGHT = 0.035
CAFE_STOOL_SEAT_DIAMETER = 0.48
CAFE_STOOL_SEAT_HALF_HEIGHT = 0.0275
CAFE_STOOL_METAL_RGB = (0.48, 0.50, 0.43)
CAFE_STOOL_SEAT_RGB = (0.34, 0.095, 0.035)

#: Furniture footprints, from `BarInteriorLayoutPlanner.CreateFurniture`.
#: Rects are (xMin, yMin, width, height) in the plan's XZ.
BACKBAR_RECT = (-3.0, 3.25, 6.85, 0.45)
BACKBAR_HEIGHT = 1.30
STAGE_RECT = (3.05, -3.525, 2.1, 1.65)
STAGE_HEIGHT = 0.12
BOOTH_ZS = (-2.15, 0.65)
BOOTH_RECT_WIDTH = 2.05
BOOTH_RECT_HEIGHT = 2.8
BOOTH_RECT_X = -5.4
PUB_TABLES = ((-1.75, -2.8), (1.75, -2.8))

ACTIVITY_RECTS = {
    "BeerPong": (3.85, -1.75, 0.76, 1.83),
}

#: `BarDistrictMood`. The group key is the enum name so the runtime
#: can resolve it with a bare ToString; the PARTS keep their spoken
#: names, which is why the Escape set is dressed in neon called
#: "Nightlife".
DISTRICT_MOODS = ("Memory", "Household", "AfterShift", "Escape")
ACTIVITY_KINDS = ("BeerPong", "Cocktail", "SplitTheG", "TinctureMatch")

DEFAULT_BLEND = ROOT / "ArtSource" / "Bar" / "Blender" / "Bar3D.blend"
DEFAULT_FBX = ROOT / "Assets" / "Bar" / "Models" / "BarInterior3D.fbx"
DEFAULT_MANIFEST = ROOT / "Assets" / "Bar" / "Models" / "Bar3D.json"
SERVICE_FBX = ROOT / "Assets" / "Bar" / "Models" / "BarServiceProps3D.fbx"
SERVICE_MANIFEST = (
    ROOT / "Assets" / "Bar" / "Models" / "BarServiceProps3D.json")
SERVICE_GENERATOR_VERSION = "1.4.1"
SERVICE_DESIGN_ID = "bar_service_props_v1"
SERVICE_DISPLAY_NAME = "Bar Promenade Authored Service Props"
FACADE_FBX = ROOT / "Assets" / "Bar" / "Models" / "BarFacade3D.fbx"
FACADE_MANIFEST = (
    ROOT / "Assets" / "Bar" / "Models" / "BarFacade3D.json")

#: Sheet -> the metres-per-tile it is measured at, from
#: `ArtSource/Bar/bar-textures.json` and read back by
#: `BarSurfaceAppearance.GetRecipe`.  UVs are baked at these pitches so
#: the Unity material tiles at (1, 1); the runtime world-metre path
#: cannot be used, because it derives its scale from the mesh bounding
#: box and a profiled mesh has the wrong one.
SHEET_PITCH = {
    "WornPlank": 1.5,
    "Wallpaper": 1.8,
    "DarkWood": 1.1,
    "WornLeather": 0.9,
    "CeilingPlaster": 2.4,
    "PolishedWood": 0.75,
    "AgedBrass": 0.42,
    "MirrorGlass": 1.35,
    "PatternedGlass": 0.72,
    "PubCarpet": 1.15,
    "WornFabric": 0.68,
    "PaintedMetal": 0.82,
    "Paper": 0.55,
    "BottleGlass": 0.36,
    "Ceramic": 0.42,
    "ExteriorBrick": 1.2,
    "ExteriorPlaster": 2.6,
    "CityRoof": 4.0,
    "": 1.0,
}

PREVIEW_COLORS = {
    "WornPlank": (0.14, 0.06, 0.042, 1.0),
    "Wallpaper": (0.29, 0.075, 0.075, 1.0),
    "DarkWood": (0.075, 0.024, 0.017, 1.0),
    "WornLeather": (0.30, 0.035, 0.045, 1.0),
    "CeilingPlaster": (0.18, 0.14, 0.11, 1.0),
    "PolishedWood": (0.22, 0.095, 0.045, 1.0),
    "AgedBrass": (0.62, 0.34, 0.13, 1.0),
    "MirrorGlass": (0.22, 0.34, 0.38, 1.0),
    "PatternedGlass": (0.18, 0.28, 0.30, 1.0),
    "PubCarpet": (0.22, 0.055, 0.052, 1.0),
    "WornFabric": (0.30, 0.035, 0.045, 1.0),
    "PaintedMetal": (0.18, 0.20, 0.19, 1.0),
    "Paper": (0.74, 0.66, 0.47, 1.0),
    "BottleGlass": (0.28, 0.20, 0.11, 1.0),
    "Ceramic": (0.82, 0.12, 0.10, 1.0),
    "ExteriorBrick": (0.30, 0.12, 0.075, 1.0),
    "ExteriorPlaster": (0.48, 0.44, 0.34, 1.0),
    "CityRoof": (0.095, 0.105, 0.115, 1.0),
    "": (0.34, 0.29, 0.13, 1.0),
    "emissive": (1.0, 0.62, 0.28, 1.0),
}

PREVIEW_SURFACE_PROPERTIES = {
    "WornPlank": (0.08, 0.0),
    "Wallpaper": (0.04, 0.0),
    "DarkWood": (0.12, 0.0),
    "WornLeather": (0.06, 0.0),
    "CeilingPlaster": (0.025, 0.0),
    "PolishedWood": (0.34, 0.0),
    "AgedBrass": (0.42, 0.72),
    "MirrorGlass": (0.78, 0.12),
    "PatternedGlass": (0.64, 0.04),
    "PubCarpet": (0.015, 0.0),
    "WornFabric": (0.02, 0.0),
    "PaintedMetal": (0.20, 0.30),
    "Paper": (0.025, 0.0),
    "BottleGlass": (0.68, 0.02),
    "Ceramic": (0.48, 0.04),
    "ExteriorBrick": (0.04, 0.0),
    "ExteriorPlaster": (0.035, 0.0),
}


def rect_center(rect: Sequence[float]) -> tuple[float, float]:
    return rect[0] + rect[2] * 0.5, rect[1] + rect[3] * 0.5


def rect_max(rect: Sequence[float]) -> tuple[float, float]:
    return rect[0] + rect[2], rect[1] + rect[3]


def cafe_stool_geometry(
    x: float,
    z: float,
    seat_top_y: float,
) -> tuple[kit.Geometry, kit.Geometry, kit.Geometry]:
    """Return the cafe's pedestal, disk foot and seat at a Bar seat height."""
    seat_center_y = seat_top_y - CAFE_STOOL_SEAT_HALF_HEIGHT
    seat_bottom_y = seat_top_y - CAFE_STOOL_SEAT_HALF_HEIGHT * 2.0
    stem_top_y = seat_bottom_y + CAFE_STOOL_STEM_SEAT_OVERLAP
    stem_half_height = stem_top_y * 0.5
    return (
        bp.u_cylinder(
            (x, stem_half_height, z),
            (CAFE_STOOL_STEM_DIAMETER,
             stem_half_height,
             CAFE_STOOL_STEM_DIAMETER),
            sides=10),
        bp.u_cylinder(
            (x, CAFE_STOOL_FOOT_HALF_HEIGHT, z),
            (CAFE_STOOL_FOOT_DIAMETER,
             CAFE_STOOL_FOOT_HALF_HEIGHT,
             CAFE_STOOL_FOOT_DIAMETER),
            sides=12),
        bp.u_cylinder(
            (x, seat_center_y, z),
            (CAFE_STOOL_SEAT_DIAMETER,
             CAFE_STOOL_SEAT_HALF_HEIGHT,
             CAFE_STOOL_SEAT_DIAMETER),
            sides=14),
    )


@dataclass
class Part:
    obj: "bpy.types.Object"
    name: str
    role: str
    group: str
    sheet: str
    emissive: bool
    shadows: bool
    tint: dict
    colliders: list
    geometry: kit.Geometry


@dataclass
class AssetBuild:
    root: "bpy.types.Object"
    collection: "bpy.types.Collection"
    parts: list[Part] = field(default_factory=list)
    anchors: dict[str, "bpy.types.Object"] = field(default_factory=dict)
    groups: dict[str, "bpy.types.Object"] = field(default_factory=dict)


def stable(value: float) -> float:
    return round(float(value), 6)


def anchor_unity_position(
    anchor: "bpy.types.Object",
) -> tuple[float, float, float]:
    """Returns an authored anchor position after the source Y/Z swap."""
    return (
        float(anchor.location[0]),
        float(anchor.location[2]),
        float(anchor.location[1]),
    )


def anchor_signature(anchor: "bpy.types.Object") -> list | dict:
    position = [stable(value) for value in anchor.location]
    right = [stable(value) for value in anchor.get(
        "bp_unity_right", (1.0, 0.0, 0.0))]
    up = [stable(value) for value in anchor.get(
        "bp_unity_up", (0.0, 1.0, 0.0))]
    forward = [stable(value) for value in anchor.get(
        "bp_unity_forward", (0.0, 0.0, 1.0))]
    if (right == [1.0, 0.0, 0.0] and
            up == [0.0, 1.0, 0.0] and
            forward == [0.0, 0.0, 1.0]):
        # Preserve existing interior/facade signatures when only the service
        # pack starts publishing oriented anchors.
        return position
    return {
        "position": position,
        "unity_right": right,
        "unity_up": up,
        "unity_forward": forward,
    }


# ------------------------------------------------------------ scene ---


def create_material(key: str) -> "bpy.types.Material":
    material = bpy.data.materials.new(f"PREVIEW_Bar_{key or 'Flat'}")
    color = PREVIEW_COLORS[key]
    material.diffuse_color = color
    smoothness, metallic = PREVIEW_SURFACE_PROPERTIES.get(
        key, (0.70 if key == "emissive" else 0.22, 0.0))
    material.roughness = 1.0 - smoothness
    material.metallic = metallic

    texture_path = (
        ROOT / "Assets" / "Resources" / "Bar" / "Textures" /
        f"Bar{key}Albedo.png")
    if key and key != "emissive" and texture_path.is_file():
        material.use_nodes = True
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        principled = nodes.get("Principled BSDF")
        if principled is not None:
            image_node = nodes.new("ShaderNodeTexImage")
            image_node.name = f"{key} Albedo"
            image_node.label = f"{key} measured albedo"
            image_node.image = bpy.data.images.load(
                str(texture_path), check_existing=True)
            image_node.interpolation = "Linear"
            image_node.extension = "REPEAT"
            multiply = nodes.new("ShaderNodeMixRGB")
            multiply.blend_type = "MULTIPLY"
            multiply.inputs[0].default_value = 1.0
            multiply.inputs[2].default_value = color
            links.new(image_node.outputs["Color"], multiply.inputs[1])
            links.new(multiply.outputs["Color"],
                      principled.inputs["Base Color"])
            principled.inputs["Roughness"].default_value = 1.0 - smoothness
            principled.inputs["Metallic"].default_value = metallic
    return material


def assign_world_uv(mesh: "bpy.types.Mesh", scale: float) -> None:
    """World-planar UVs at a fixed metre pitch, per dominant face normal.

    Neighbouring parts therefore share a coordinate frame, so a run of
    wall reads as one continuous paper rather than as a strip per box -
    and the projection the primitives had to be told (`BoxXY`, `BoxXZ`,
    `BoxZY`) is now simply a consequence of which way a face points.
    """
    layer = mesh.uv_layers.new(name="UVMap")
    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        for loop_index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if axis == 0:
                uv = (point.y / scale, point.z / scale)
            elif axis == 1:
                uv = (point.x / scale, point.z / scale)
            else:
                uv = (point.x / scale, point.y / scale)
            layer.data[loop_index].uv = uv


def ensure_group(
    asset: AssetBuild,
    name: str,
) -> "bpy.types.Object | None":
    """Deliberately returns nothing: there are NO group empties.

    An empty exported to FBX and re-imported carries a unit-scale factor
    with it, and every mesh parented to one arrives in Unity a hundred
    times too small - a district dressing 4.80 m tall measured 0.048 m.
    Anchor empties are unaffected because nothing is parented to them and
    only their position is read.

    So grouping is DATA, not hierarchy: each part records `bp_group` and
    the manifest repeats it, and the runtime builds whatever containers
    it needs. That is also the better design - the room's containers are
    a runtime concern, and the model has no business dictating them.
    """
    return None


def resolve_default_sheet(name: str, role: str) -> str:
    """Give every visible pub part its own physical material family.

    The previous model left roughly half of the interior on tint-only
    materials.  The mapping is role-driven so rebuilding Blender/FBX cannot
    silently turn a detailed pub back into flat-colour primitives.
    """
    by_role = {
        "ceiling": "CeilingPlaster",
        "entrance_frame": "PolishedWood",
        "wainscot_rail": "AgedBrass",
        "counter_top": "PolishedWood",
        "counter_rail": "AgedBrass",
        "hero_stool_footring": "AgedBrass",
        "tap_stem": "AgedBrass",
        "tap_spout": "AgedBrass",
        "tap_handle": "Ceramic",
        "backbar_mirror": "MirrorGlass",
        "backbar_patterned_glass": "AgedBrass",
        "backbar_shelf": "PolishedWood",
        "backbar_glass_rack": "AgedBrass",
        "bottle": "BottleGlass",
        "booth_table": "PolishedWood",
        "snug_divider_glass": "PatternedGlass",
        "stage_speaker": "PaintedMetal",
        "stage_mic": "PaintedMetal",
        "pub_carpet": "PubCarpet",
        "pub_carpet_border": "PubCarpet",
        "pub_carpet_pattern": "PubCarpet",
        "bay_rug": "PubCarpet",
        "bay_border": "AgedBrass",
        "service_door": "PaintedMetal",
        "service_door_frame": "PolishedWood",
        "poster_frame": "PolishedWood",
        "poster": "Paper",
        "entrance_curtain_rail": "AgedBrass",
        "fan_hub": "AgedBrass",
        "fan_support": "AgedBrass",
        "fan_blade": "PaintedMetal",
        "jukebox_grille": "PaintedMetal",
        "jukebox_key": "AgedBrass",
        "practical_cable": "PaintedMetal",
        "practical_shade": "PaintedMetal",
        "activity_table": "PolishedWood",
        "activity_line": "Paper",
        "activity_cup": "Ceramic",
        "activity_console_top": "PolishedWood",
        "service_bottle_body": "BottleGlass",
        "service_bottle_closure": "PaintedMetal",
        "service_bottle_label": "Paper",
        "service_vessel_shell": "BottleGlass",
        "service_vessel_liquid": "BottleGlass",
        "service_menu_pages": "Paper",
        "service_menu_relief": "AgedBrass",
        "service_pour_stream": "BottleGlass",
    }
    if role in by_role:
        return by_role[role]
    if role in ("district_field", "district_cards"):
        if "Curtain" in name or "Pleats" in name:
            return "WornFabric"
        if "Industrial" in name:
            return "PaintedMetal"
        return "Paper"
    if role == "activity_prop":
        if any(token in name for token in (
                "Glass", "Pint", "Shot", "Bottle")):
            return "BottleGlass"
        if "Foam" in name:
            return "Ceramic"
        if "Target" in name or "Sign" in name or "Marks" in name:
            return "Paper"
        return "PaintedMetal"
    return ""


def add_part(
    asset: AssetBuild,
    materials: dict,
    name: str,
    geometry: kit.Geometry,
    role: str,
    tint: dict,
    *,
    group: str = "fixed",
    sheet: str = "",
    emissive: bool = False,
    shadows: bool = True,
    colliders: list | None = None,
    unity_space: bool = True,
) -> "bpy.types.Object":
    """Adds one authored part.

    `unity_space` is the default because almost everything here is lifted
    straight out of `BarInteriorWorldBuilder`, which is written in Unity
    coordinates. Only the shell, which comes from `interior_kit`, is
    already in Blender's frame.
    """
    if unity_space:
        geometry = bp.to_source(geometry)

    if not emissive and not sheet:
        sheet = resolve_default_sheet(name, role)

    vertices, faces = geometry
    if not vertices or not faces:
        raise SystemExit(f"Part '{name}' is empty.")

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata([tuple(v) for v in vertices], [], [tuple(f) for f in faces])
    key = "emissive" if emissive else sheet
    mesh.materials.append(materials[key])
    mesh.update(calc_edges=True)
    assign_world_uv(mesh, SHEET_PITCH[sheet])

    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = ensure_group(asset, group) or asset.root
    obj["bp_role"] = role
    obj["bp_group"] = group
    asset.parts.append(Part(
        obj, name, role, group, sheet, emissive, shadows,
        tint, list(colliders or []), geometry))
    return obj


def add_pivoted_part(
    asset: AssetBuild,
    materials: dict,
    name: str,
    geometry: kit.Geometry,
    pivot: Sequence[float],
    role: str,
    tint: dict,
    *,
    group: str = "fixed",
    sheet: str = "",
    emissive: bool = False,
    shadows: bool = True,
) -> "bpy.types.Object":
    """Adds a part whose imported Transform origin is an authored pivot.

    ``Part.geometry`` remains in absolute source space so signatures, bounds
    and validators keep the same deterministic contract as ordinary parts.
    Only the Blender mesh vertices are made pivot-local; the object transform
    puts them back at their exact authored position for FBX export.
    """
    source_geometry = bp.to_source(geometry)
    pivot_x, pivot_y, pivot_z = pivot
    source_pivot = (float(pivot_x), float(pivot_z), float(pivot_y))
    vertices, faces = source_geometry
    local_vertices = [
        (vertex[0] - source_pivot[0],
         vertex[1] - source_pivot[1],
         vertex[2] - source_pivot[2])
        for vertex in vertices
    ]

    if not emissive and not sheet:
        sheet = resolve_default_sheet(name, role)
    if not local_vertices or not faces:
        raise SystemExit(f"Part '{name}' is empty.")

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(local_vertices, [], [tuple(face) for face in faces])
    key = "emissive" if emissive else sheet
    mesh.materials.append(materials[key])
    mesh.update(calc_edges=True)
    assign_world_uv(mesh, SHEET_PITCH[sheet])

    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = ensure_group(asset, group) or asset.root
    obj.location = source_pivot
    obj["bp_role"] = role
    obj["bp_group"] = group
    obj["bp_pivot"] = True
    asset.parts.append(Part(
        obj, name, role, group, sheet, emissive, shadows,
        tint, [], source_geometry))
    return obj


def add_anchor(
    asset: AssetBuild,
    name: str,
    role: str,
    unity_position: Sequence[float],
    unity_right: Sequence[float] = (1.0, 0.0, 0.0),
    unity_up: Sequence[float] = (0.0, 1.0, 0.0),
    unity_forward: Sequence[float] = (0.0, 0.0, 1.0),
) -> None:
    expected_name = f"ANCHOR_{name}"
    anchor = bpy.data.objects.new(expected_name, None)
    if anchor.name != expected_name:
        raise SystemExit(
            f"Duplicate semantic anchor '{expected_name}' was renamed by Blender "
            f"to '{anchor.name}'. Give each exported asset a distinct anchor name.")
    asset.collection.objects.link(anchor)
    anchor.parent = asset.root
    anchor.empty_display_type = "ARROWS"
    anchor.empty_display_size = 0.5
    x, y, z = unity_position
    anchor.location = (float(x), float(z), float(y))
    # Positions pass through the same Y/Z swap as all authored geometry.
    # Conjugating the Unity rotation by that swap gives Blender the source
    # rotation whose imported Transform axes recover the exact requested
    # right/up/forward basis. Identity remains identity for ordinary anchors.
    from mathutils import Matrix
    swap_yz = Matrix(((1.0, 0.0, 0.0),
                      (0.0, 0.0, 1.0),
                      (0.0, 1.0, 0.0)))
    unity_rotation = Matrix((
        (unity_right[0], unity_up[0], unity_forward[0]),
        (unity_right[1], unity_up[1], unity_forward[1]),
        (unity_right[2], unity_up[2], unity_forward[2]),
    ))
    source_rotation = swap_yz @ unity_rotation @ swap_yz
    anchor.rotation_mode = "QUATERNION"
    anchor.rotation_quaternion = source_rotation.to_quaternion()
    anchor["bp_role"] = role
    anchor["bp_unity_right"] = [float(value) for value in unity_right]
    anchor["bp_unity_up"] = [float(value) for value in unity_up]
    anchor["bp_unity_forward"] = [float(value) for value in unity_forward]
    asset.anchors[name] = anchor


# ------------------------------------------------------- the shell ----


def entrance_skirting() -> kit.Geometry:
    """Runs the room skirting up to, but never across, the front door."""
    inset = WALL_THICKNESS * 0.5
    half_width = ROOM_WIDTH * 0.5 - inset
    half_depth = ROOM_DEPTH * 0.5 - inset
    overlap = 0.022
    half_door = DOOR_WIDTH * 0.5
    profile = kit.profile_skirting(0.14, 0.022)
    return kit.merge_all([
        kit.sweep(
            ((-half_width, -half_depth),
             (-half_door - overlap, -half_depth)),
            profile,
            overlap=overlap),
        kit.sweep(
            ((half_door + overlap, -half_depth),
             (half_width, -half_depth)),
            profile,
            overlap=overlap),
        kit.sweep(
            ((half_width, -half_depth),
             (half_width, half_depth),
             (washroom.DOOR_X + washroom.DOOR_WIDTH * .5 + .055, half_depth)),
            profile,
            overlap=overlap),
        kit.sweep(
            ((washroom.DOOR_X - washroom.DOOR_WIDTH * .5 - .055, half_depth),
             (-half_width, half_depth),
             (-half_width, -half_depth)),
            profile,
            overlap=overlap),
    ])


def build_shell(asset: AssetBuild, materials: dict) -> None:
    half_depth = ROOM_DEPTH * 0.5

    add_part(
        asset, materials, "Floor",
        kit.floor_slab(ROOM_WIDTH, ROOM_DEPTH, FLOOR_THICKNESS),
        "floor", bp.ident("FloorTint"), sheet="WornPlank",
        colliders=[((0.0, -0.12, 0.0), (ROOM_WIDTH, 0.24, ROOM_DEPTH))],
        unity_space=False)
    add_part(
        asset, materials, "Ceiling",
        kit.ceiling_slab(
            ROOM_WIDTH, ROOM_DEPTH, CEILING_THICKNESS, ROOM_HEIGHT),
        "ceiling", bp.ident("CeilingTint"), unity_space=False)

    # Unity's front wall - the entrance, at -Z - is Blender's -Y wall,
    # so the generator's forward axis points out through the door.
    walls = kit.rectangular_room_walls(
        ROOM_WIDTH, ROOM_DEPTH, ROOM_HEIGHT, WALL_THICKNESS,
        front_openings=[kit.Opening(0.0, DOOR_WIDTH, DOOR_HEIGHT)],
        back_openings=[kit.Opening(washroom.DOOR_X, washroom.DOOR_WIDTH, washroom.DOOR_HEIGHT)])
    _, washroom_opening_collision = washroom.split_door_wall(
        -ROOM_WIDTH*.5, ROOM_WIDTH*.5, half_depth, WALL_THICKNESS)
    segment = (ROOM_WIDTH - DOOR_WIDTH) * 0.5
    offset = DOOR_WIDTH * 0.5 + segment * 0.5
    header_height = ROOM_HEIGHT - DOOR_HEIGHT
    # The front wall is one mesh with a standard-height door cut through
    # it, so its collision follows the two piers and the lintel separately.
    wall_colliders = {
        "Front Wall": [
            ((-offset, ROOM_HEIGHT * 0.5, -half_depth),
             (segment, ROOM_HEIGHT, WALL_THICKNESS)),
            ((offset, ROOM_HEIGHT * 0.5, -half_depth),
             (segment, ROOM_HEIGHT, WALL_THICKNESS)),
            ((0.0, DOOR_HEIGHT + header_height * 0.5, -half_depth),
             (DOOR_WIDTH, header_height, WALL_THICKNESS)),
        ],
        "Back Wall": washroom_opening_collision,
        "Left Wall": [((-ROOM_WIDTH * 0.5, ROOM_HEIGHT * 0.5, 0.0),
                       (WALL_THICKNESS, ROOM_HEIGHT, ROOM_DEPTH))],
        "Right Wall": [((ROOM_WIDTH * 0.5, ROOM_HEIGHT * 0.5, 0.0),
                        (WALL_THICKNESS, ROOM_HEIGHT, ROOM_DEPTH))],
    }
    for source_name, part_name in (
        ("front", "Front Wall"), ("back", "Back Wall"),
        ("left", "Left Wall"), ("right", "Right Wall"),
    ):
        add_part(
            asset, materials, part_name, walls[source_name], "wall",
            bp.ident("WallTint"), sheet="Wallpaper",
            colliders=wall_colliders.get(part_name), unity_space=False)

    add_part(
        asset, materials, "Skirting",
        entrance_skirting(),
        "skirting", bp.ident("DarkWoodTint"), sheet="DarkWood",
        shadows=False, unity_space=False)

    door_face_z = -half_depth + WALL_THICKNESS * 0.5 + 0.045
    frame_z = -half_depth + WALL_THICKNESS * 0.5
    frame_bar = 0.12
    add_part(
        asset, materials, "Entrance Door Frame",
        kit.merge_all([
            bp.u_box(
                (side * (DOOR_WIDTH * 0.5 + frame_bar * 0.5),
                 (DOOR_HEIGHT + frame_bar) * 0.5,
                 frame_z),
                (frame_bar, DOOR_HEIGHT + frame_bar, 0.20), 0.010)
            for side in (-1.0, 1.0)
        ] + [
            bp.u_box(
                (0.0, DOOR_HEIGHT + frame_bar * 0.5, frame_z),
                (DOOR_WIDTH + frame_bar * 2.0, frame_bar, 0.20),
                0.010)
        ]),
        "entrance_frame", bp.ident("DarkWoodTint"), sheet="DarkWood")
    add_part(
        asset, materials, "Entrance Door",
        bp.u_box(
            (0.0, DOOR_HEIGHT * 0.5, door_face_z),
            (DOOR_WIDTH, DOOR_HEIGHT, 0.08), 0.014),
        "entrance_door", bp.ident("DarkWoodTint"), sheet="DarkWood")
    add_part(
        asset, materials, "Entrance Door Panels",
        kit.merge_all([
            bp.u_plate(
                (x, y, door_face_z + 0.052),
                (0.54, height, 0.025))
            for x in (-0.36, 0.36)
            for y, height in ((0.62, 0.72), (1.55, 0.66))
        ]),
        "entrance_door_panel", bp.ident("SignAccentColor"),
        sheet="DarkWood", shadows=False)
    add_part(
        asset, materials, "Entrance Door Transom Glass",
        bp.u_plate(
            (0.0, 2.13, door_face_z + 0.053),
            (1.28, 0.28, 0.026)),
        "entrance_door_glass", bp.ident("GlassTint"),
        sheet="PatternedGlass", shadows=False)
    door_knob = kit.translated(
        bp.u_rotated(
            bp.u_cylinder((0.0, 0.0, 0.0), (0.10, 0.045, 0.10), 10),
            (90.0, 0.0, 0.0)),
        (0.43, 1.13, door_face_z + 0.10))
    add_part(
        asset, materials, "Entrance Door Furniture",
        kit.merge_all([
            bp.u_plate(
                (0.43, 1.34, door_face_z + 0.056),
                (0.12, 0.22, 0.030)),
            door_knob,
        ]),
        "entrance_door_furniture", bp.ident("MetalTint"),
        sheet="AgedBrass", shadows=False)

    wainscot = [
        washroom.rear_trim(.82, 1.58, .10, half_depth - .19),
        bp.u_box((-ROOM_WIDTH * 0.5 + 0.19, 0.82, 0.0),
                 (0.10, 1.58, ROOM_DEPTH - 0.55)),
        bp.u_box((ROOM_WIDTH * 0.5 - 0.19, 0.82, 0.0),
                 (0.10, 1.58, ROOM_DEPTH - 0.55)),
    ]
    add_part(
        asset, materials, "Wall Wainscot", kit.merge_all(wainscot),
        "wainscot", bp.ident("WallPanelTint"), sheet="DarkWood",
        shadows=False)

    rails = [
        washroom.rear_trim(1.64, .10, .12, half_depth - .26),
        bp.u_box((-ROOM_WIDTH * 0.5 + 0.26, 1.64, 0.0),
                 (0.12, 0.10, ROOM_DEPTH - 0.40), 0.006),
        bp.u_box((ROOM_WIDTH * 0.5 - 0.26, 1.64, 0.0),
                 (0.12, 0.10, ROOM_DEPTH - 0.40), 0.006),
    ]
    add_part(
        asset, materials, "Wall Brass Rails", kit.merge_all(rails),
        "wainscot_rail", bp.ident("MetalTint"), shadows=False)

    cross = [
        bp.u_box((x, ROOM_HEIGHT - 0.18, 0.0),
                 (0.22, 0.34, ROOM_DEPTH - 0.35))
        for x in (-4.5, -2.25, 0.0, 2.25, 4.5)
    ]
    add_part(
        asset, materials, "Ceiling Cross Beams", kit.merge_all(cross),
        "beam", bp.ident("DarkWoodTint"), sheet="DarkWood", shadows=False)

    long_beams = [
        bp.u_box((x, ROOM_HEIGHT - 0.28, 0.0),
                 (0.32, 0.50, ROOM_DEPTH - 0.30))
        for x in (-3.1, 3.1)
    ]
    add_part(
        asset, materials, "Ceiling Long Beams", kit.merge_all(long_beams),
        "beam", bp.ident("DarkWoodTint"), sheet="DarkWood", shadows=False)


# ---------------------------------------------------- fixed furniture -


def build_counter(asset: AssetBuild, materials: dict) -> None:
    cx, cy, cz = COUNTER_POS
    sx, sy, sz = COUNTER_SIZE
    rx, ry, rz = COUNTER_RETURN_POS
    rsx, rsy, rsz = COUNTER_RETURN_SIZE

    add_part(
        asset, materials, "Bar Counter",
        kit.translated(
            kit.counter_run(sx, sz, sy, top_thickness=0.06, nosing=0.02),
            (cx, cz, cy - sy * 0.5)),
        "counter", bp.ident("CounterWoodTint"), sheet="DarkWood",
        colliders=[((cx, cy, cz), (sx, sy, sz))], unity_space=False)
    add_part(
        asset, materials, "Bar Counter Return",
        bp.u_box((rx, ry, rz), (rsx, rsy, rsz), 0.026),
        "counter_return", bp.ident("CounterWoodTint"), sheet="DarkWood",
        colliders=[((rx, ry, rz), (rsx, rsy, rsz))])
    add_part(
        asset, materials, "Counter Top",
        bp.u_box((cx, cy + sy * 0.5 + 0.08, cz),
                 (sx + 0.45, 0.16, sz + 0.32), 0.02),
        "counter_top", bp.ident("MetalTint"))
    add_part(
        asset, materials, "Counter Return Top",
        bp.u_box((rx, ry + rsy * 0.5 + 0.08, rz - 0.08),
                 (rsx + 0.32, 0.16, rsz + 0.16), 0.02),
        "counter_top", bp.ident("MetalTint"))

    main_rail = kit.translated(
        bp.u_rotated(
            bp.u_cylinder(
                (0.0, 0.0, 0.0),
                (0.10, (sx - 0.45) * 0.5, 0.10),
                sides=12),
            (0.0, 0.0, 90.0)),
        (cx, cy - sy * 0.29, cz - sz * 0.62))
    add_part(
        asset, materials, "Counter Foot Rail", main_rail,
        "counter_rail", bp.ident("MetalTint"))
    return_rail = kit.translated(
        bp.u_rotated(
            bp.u_cylinder(
                (0.0, 0.0, 0.0),
                (0.10, (rsz - 0.38) * 0.5, 0.10),
                sides=12),
            (90.0, 0.0, 0.0)),
        (rx - rsx * 0.62, ry - rsy * 0.29, rz - 0.12))
    add_part(
        asset, materials, "Counter Return Foot Rail", return_rail,
        "counter_rail", bp.ident("MetalTint"))

    panels = []
    panel_count = len(STOOL_XS) + 1
    panel_width = (sx - 0.65) / panel_count
    for index in range(panel_count):
        x = -sx * 0.5 + 0.33 + panel_width * (index + 0.5)
        panels.append(bp.u_box(
            (cx + x, cy, cz - sz * 0.51),
            (panel_width - 0.11, sy - 0.20, 0.08), 0.006))
    add_part(
        asset, materials, "Counter Front Panels", kit.merge_all(panels),
        "counter_panel", bp.ident("WoodTint"), sheet="DarkWood",
        shadows=False)

    return_panels = []
    for index in range(2):
        panel_z = rz - rsz * 0.5 + 0.43 + index * 0.82
        return_panels.append(bp.u_box(
            (rx - rsx * 0.51, ry, panel_z),
            (0.08, rsy - 0.20, 0.72), 0.006))
    add_part(
        asset, materials, "Counter Return Panels",
        kit.merge_all(return_panels), "counter_panel",
        bp.ident("WoodTint"), sheet="DarkWood", shadows=False)

    # The regular stools leave the service bay clear. The hero stool occupies
    # that opening but otherwise uses the exact same cafe geometry.
    stool_z = HERO_SEAT[2]
    for index, x in enumerate(STOOL_XS, start=2):
        dx = x - COUNTER_STATION[0]
        dz = stool_z - COUNTER_STATION[2]
        if abs(x - HERO_SEAT[0]) < 0.001:
            continue

        name = f"Bar Stool {index}"
        pedestal, foot, seat = cafe_stool_geometry(
            x, stool_z, HERO_SEAT[1])
        add_part(
            asset, materials, f"{name} Leg",
            kit.merge_all([pedestal, foot]),
            "stool_leg", bp.rgb(*CAFE_STOOL_METAL_RGB),
            sheet="PaintedMetal")
        add_part(
            asset, materials, name, seat,
            "stool_seat", bp.rgb(*CAFE_STOOL_SEAT_RGB),
            sheet="PolishedWood",
            colliders=[((x,
                         HERO_SEAT[1] - CAFE_STOOL_SEAT_HALF_HEIGHT,
                         stool_z),
                        (CAFE_STOOL_SEAT_DIAMETER,
                         CAFE_STOOL_SEAT_HALF_HEIGHT * 2.0,
                         CAFE_STOOL_SEAT_DIAMETER))])

    hero_x, hero_top, hero_z = HERO_SEAT
    hero_pedestal, hero_foot, hero_seat = cafe_stool_geometry(
        hero_x, hero_z, hero_top)
    add_part(
        asset, materials, "Bar Drink Service Stool Legs",
        hero_pedestal,
        "hero_stool_legs", bp.rgb(*CAFE_STOOL_METAL_RGB),
        sheet="PaintedMetal")
    add_part(
        asset, materials, "Bar Drink Service Stool Footring",
        hero_foot,
        "hero_stool_footring", bp.rgb(*CAFE_STOOL_METAL_RGB),
        sheet="PaintedMetal")
    add_part(
        asset, materials, "Bar Drink Service Stool",
        hero_seat,
        "hero_stool_seat", bp.rgb(*CAFE_STOOL_SEAT_RGB),
        sheet="PolishedWood",
        colliders=[((hero_x,
                     hero_top - CAFE_STOOL_SEAT_HALF_HEIGHT,
                     hero_z),
                    (CAFE_STOOL_SEAT_DIAMETER,
                     CAFE_STOOL_SEAT_HALF_HEIGHT * 2.0,
                     CAFE_STOOL_SEAT_DIAMETER))])

    for index, x in enumerate(BEER_TAP_XS):
        stem_center_y = (
            COUNTER_SURFACE_Y + BEER_TAP_STEM_TOP_Y) * 0.5
        add_part(
            asset, materials, f"Beer Tap Stem {index + 1}",
            bp.u_cylinder(
                (x, stem_center_y, BEER_TAP_Z),
                (0.08,
                 (BEER_TAP_STEM_TOP_Y - COUNTER_SURFACE_Y) * 0.5,
                 0.08),
                sides=12),
            "tap_stem", bp.ident("MetalTint"))
        spout_run = BEER_TAP_SPOUT_Z - BEER_TAP_Z
        spout = kit.merge_all([
            kit.translated(
                bp.u_rotated(
                    bp.u_cylinder(
                        (0.0, 0.0, 0.0),
                        (0.062, spout_run * 0.5, 0.062),
                        sides=12),
                    (90.0, 0.0, 0.0)),
                (x, BEER_TAP_BRANCH_Y,
                 BEER_TAP_Z + spout_run * 0.5)),
            bp.u_cylinder(
                (x,
                 (BEER_TAP_BRANCH_Y + BEER_TAP_SPOUT_Y) * 0.5,
                 BEER_TAP_SPOUT_Z),
                (0.050,
                 (BEER_TAP_BRANCH_Y - BEER_TAP_SPOUT_Y) * 0.5,
                 0.050),
                sides=12),
        ])
        add_part(
            asset, materials, f"Beer Tap Spout {index + 1}", spout,
            "tap_spout", bp.ident("MetalTint"), shadows=False)
        handle_pivot = (x, BEER_TAP_STEM_TOP_Y, BEER_TAP_Z)
        add_pivoted_part(
            asset, materials, f"Beer Tap Handle {index + 1}",
            bp.u_tapered_cylinder(
                (x,
                 (BEER_TAP_STEM_TOP_Y + BEER_TAP_HANDLE_TOP_Y) * 0.5,
                 BEER_TAP_Z),
                (0.13,
                 (BEER_TAP_HANDLE_TOP_Y - BEER_TAP_STEM_TOP_Y) * 0.5,
                 0.13),
                0.72,
                sides=12),
            handle_pivot,
            "tap_handle",
            bp.ident("UpholsteryTint") if index % 2 == 0
            else bp.ident("GlassTint"))


def build_ceiling_fan(asset: AssetBuild, materials: dict) -> None:
    group = "pivot:Slow Ceiling Fan"
    # Bake the compact 1.3825 m fan into its meshes; the shared pivot and
    # runtime rotation retain their original unit transforms.
    scale = (0.35, 0.35, 0.35)
    support_bottom = CEILING_FAN_PIVOT[1] + CEILING_FAN_HUB_TOP_OFFSET_Y
    support_height = CEILING_FAN_BEAM_UNDERSIDE_Y - support_bottom
    add_part(
        asset, materials, "Fan Suspension Stem",
        bp.u_cylinder((CEILING_FAN_PIVOT[0], support_bottom + support_height * .5,
                       CEILING_FAN_PIVOT[2]), (.018, support_height * .5, .018)),
        "fan_support", bp.ident("MetalTint"), shadows=False)
    add_part(
        asset, materials, "Fan Hub",
        kit.scaled(bp.u_cylinder((0.0, 0.0, 0.0), (0.28, 0.10, 0.28)), scale),
        "fan_hub", bp.ident("MetalTint"), group=group, shadows=False)
    for index in range(4):
        #  Each blade is placed by rotating its own offset, exactly as
        #  the runtime did; a blade rotated in place would sit inside
        #  the hub.
        blade = bp.u_box((1.10, -0.05, 0.0), (1.75, 0.08, 0.34), 0.01)
        add_part(
            asset, materials, f"Fan Blade {index + 1}",
            kit.scaled(bp.u_rotated(blade, (0.0, index * 90.0, 0.0)), scale),
            "fan_blade", bp.rgb(0.68, 0.58, 0.38),
            group=group, shadows=False)


def build_jukebox(asset: AssetBuild, materials: dict) -> None:
    group = "pivot:Bar Jukebox"
    add_part(
        asset, materials, "Jukebox Corpus",
        bp.u_box((0.0, 0.72, 0.0), (0.56, 1.44, 0.92), 0.016),
        "jukebox_body", bp.rgb(0.24, 0.075, 0.045), group=group,
        sheet="DarkWood")
    add_part(
        asset, materials, "Jukebox Crown",
        #  An arch, which is what a jukebox crown is; three stacked
        #  courses cost nothing and the box never read as one.
        kit.merge_all([
            bp.u_box((-0.03, 1.50 + level * 0.08, 0.0),
                     (0.50 - level * 0.06, 0.09, 0.78 - level * 0.10),
                     0.012)
            for level in range(3)
        ]),
        "jukebox_body", bp.rgb(0.30, 0.10, 0.055), group=group,
        sheet="DarkWood")
    add_part(
        asset, materials, "Jukebox Glow Panel",
        bp.u_plate((0.285, 1.12, 0.0), (0.035, 0.34, 0.62)),
        "jukebox_panel", bp.rgb(1.35, 0.78, 0.30), group=group,
        emissive=True, shadows=False)
    for side in (-1, 1):
        add_part(
            asset, materials, f"Jukebox Glow Tube {side}",
            bp.u_cylinder((0.27, 0.86, side * 0.40), (0.05, 0.575, 0.05)),
            "jukebox_tube", bp.rgb(1.30, 0.34, 0.42), group=group,
            emissive=True, shadows=False)
    add_part(
        asset, materials, "Jukebox Grille",
        kit.merge_all([
            bp.u_box((0.275, 0.42, -0.26 + slot * 0.075),
                     (0.03, 0.42, 0.038), 0.004)
            for slot in range(8)
        ]),
        "jukebox_grille", bp.rgb(0.055, 0.035, 0.030), group=group,
        shadows=False)
    for index in range(4):
        add_part(
            asset, materials, f"Jukebox Key {index + 1}",
            bp.u_box((0.29, 0.78, -0.21 + index * 0.14),
                     (0.025, 0.05, 0.09), 0.004),
            "jukebox_key", bp.rgb(0.62, 0.58, 0.48), group=group,
            shadows=False)


def build_practical_prefab(asset: AssetBuild, materials: dict) -> None:
    """One pendant, instanced per light anchor by the runtime.

    Authored once rather than seven times so the layout plan stays the
    only place a light's position is written down. The cable is scaled
    to reach the ceiling from whatever height its anchor sits at.
    """
    group = "prefab:Practical"
    add_part(
        asset, materials, "Practical Cable",
        bp.u_box((0.0, 0.5, 0.0), (0.035, 1.0, 0.035), 0.004),
        "practical_cable", bp.ident("DarkWoodTint"), group=group,
        shadows=False)
    add_part(
        asset, materials, "Practical Shade",
        bp.u_tapered_cylinder((0.0, 0.10, 0.0), (0.58, 0.14, 0.58), 0.42),
        "practical_shade", bp.ident("MetalTint"), group=group,
        shadows=False)
    add_part(
        asset, materials, "Practical Bulb",
        bp.u_tapered_cylinder((0.0, -0.10, 0.0), (0.19, 0.18, 0.19), 0.55),
        "practical_bulb", bp.ident("PendantColor", 2.2), group=group,
        emissive=True, shadows=False)


# --------------------------------------------------------- variants ---


SERVICE_BOTTLE_STYLES = (
    "WaterBottle", "BeerLongneck", "WineBottle", "VodkaBottle", "CognacBottle",
)

SERVICE_MENU_COVER_WIDTH = 0.58
SERVICE_MENU_COVER_DEPTH = 0.50
SERVICE_MENU_COVER_THICKNESS = 0.006
SERVICE_MENU_COVER_GAP = 0.006
SERVICE_MENU_COVER_MIN_Y = 0.004
SERVICE_MENU_PAGE_WIDTH = 0.54
SERVICE_MENU_PAGE_DEPTH = 0.46
SERVICE_MENU_PAGE_THICKNESS = 0.004
SERVICE_MENU_PAGE_GAP = 0.010
SERVICE_MENU_PAGE_MIN_Y = 0.013
SERVICE_MENU_OPEN_ANGLE_DEGREES = 5.5
SERVICE_MENU_TEXT_CLEARANCE = 0.0015
SERVICE_MENU_TEXT_X = 0.145
SERVICE_MENU_SELECTION_X = 0.035
SERVICE_MENU_LEFT_ROW_Z = (0.14, 0.07, 0.0, -0.07, -0.14)
SERVICE_MENU_RIGHT_ROW_Z = (0.105, 0.035, -0.035, -0.105)
SERVICE_MENU_GRIP = (0.0, 0.046, 0.215)
SERVICE_MENU_BASIS_LENGTH = 0.10

# The runtime enum and authored group retain the historical ``Pint`` name,
# but the visible beer vessel is an ordinary half-litre handled mug. These
# measurements are in Unity metres. The current 1.07 scale keeps it compact
# beside the hero's hand while making the body and handle a little easier to
# read in the first-person drinking shot: 155 mm high, 103 mm across the rim
# and 149 mm across the complete handled silhouette.
SERVICE_PINT_MUG_SCALE = 1.07
SERVICE_PINT_MUG_HEIGHT = 0.145 * SERVICE_PINT_MUG_SCALE
SERVICE_PINT_MUG_BOTTOM_RADIUS = 0.043 * SERVICE_PINT_MUG_SCALE
SERVICE_PINT_MUG_RIM_RADIUS = 0.048 * SERVICE_PINT_MUG_SCALE
SERVICE_PINT_MUG_HANDLE_CENTER = (
    0.037 * SERVICE_PINT_MUG_SCALE,
    0.074 * SERVICE_PINT_MUG_SCALE,
    0.0,
)
SERVICE_PINT_MUG_HANDLE_MAJOR_RADIUS = 0.047 * SERVICE_PINT_MUG_SCALE
SERVICE_PINT_MUG_HANDLE_MINOR_RADIUS = 0.007 * SERVICE_PINT_MUG_SCALE
SERVICE_PINT_MUG_GRIP = (
    0.084 * SERVICE_PINT_MUG_SCALE,
    0.074 * SERVICE_PINT_MUG_SCALE,
    0.0,
)
SERVICE_PINT_MUG_DRINK_RIM = (
    0.0,
    SERVICE_PINT_MUG_HEIGHT,
    -SERVICE_PINT_MUG_RIM_RADIUS,
)

SERVICE_VESSEL_PROFILES = {
    "Tumbler": {
        "glass": ((0.13, 0.0), (0.14, 0.27), (0.15, 0.30)),
        "liquid": ((0.115, 0.0), (0.134, 0.245)),
        "liquid_base": 0.025,
        "grip": (0.145, 0.16, 0.0),
        "drink_rim": (0.0, 0.30, -0.15),
    },
    "Pint": {
        "glass": (
            (SERVICE_PINT_MUG_BOTTOM_RADIUS, 0.0),
            (0.044 * SERVICE_PINT_MUG_SCALE,
             0.018 * SERVICE_PINT_MUG_SCALE),
            (0.046 * SERVICE_PINT_MUG_SCALE,
             0.125 * SERVICE_PINT_MUG_SCALE),
            (SERVICE_PINT_MUG_RIM_RADIUS, SERVICE_PINT_MUG_HEIGHT),
        ),
        "liquid": (
            (0.038 * SERVICE_PINT_MUG_SCALE, 0.0),
            (0.043 * SERVICE_PINT_MUG_SCALE,
             0.108 * SERVICE_PINT_MUG_SCALE),
        ),
        "liquid_base": 0.012 * SERVICE_PINT_MUG_SCALE,
        "pour_target_y": 0.120 * SERVICE_PINT_MUG_SCALE,
        "handle": {
            "center": SERVICE_PINT_MUG_HANDLE_CENTER,
            "major_radius": SERVICE_PINT_MUG_HANDLE_MAJOR_RADIUS,
            "minor_radius": SERVICE_PINT_MUG_HANDLE_MINOR_RADIUS,
            "start_degrees": -82.0,
            "end_degrees": 82.0,
            "arc_segments": 12,
            "tube_segments": 6,
        },
        "grip": SERVICE_PINT_MUG_GRIP,
        "drink_rim": SERVICE_PINT_MUG_DRINK_RIM,
    },
    "WineGlass": {
        "glass": (
            (0.16, 0.0), (0.16, 0.018), (0.025, 0.025),
            (0.025, 0.15), (0.07, 0.16), (0.145, 0.25),
            (0.13, 0.37),
        ),
        "liquid": ((0.058, 0.0), (0.132, 0.085), (0.116, 0.172)),
        "liquid_base": 0.165,
        "grip": (0.0, 0.25, 0.0),
        "drink_rim": (0.0, 0.37, -0.13),
    },
    "ShotGlass": {
        "glass": ((0.065, 0.0), (0.09, 0.145)),
        "liquid": ((0.052, 0.0), (0.076, 0.112)),
        "liquid_base": 0.018,
        "grip": (0.0, 0.075, 0.0),
        "drink_rim": (0.0, 0.145, -0.09),
    },
    "Snifter": {
        "glass": (
            (0.14, 0.0), (0.14, 0.018), (0.027, 0.025),
            (0.027, 0.10), (0.07, 0.11), (0.16, 0.18),
            (0.15, 0.25), (0.105, 0.32),
        ),
        "liquid": ((0.058, 0.0), (0.145, 0.075), (0.098, 0.175)),
        "liquid_base": 0.115,
        "grip": (0.0, 0.18, 0.0),
        "drink_rim": (0.0, 0.32, -0.105),
    },
}


def service_vessel_handle_geometry(recipe: dict) -> kit.Geometry:
    """Builds a closed tube arc in source XZ around a right-side handle.

    Vessel shells are already in Blender's Z-up source frame when they reach
    ``add_part``. Handle recipes stay in Unity coordinates with their Grip
    and drink-rim peers, so the Y/Z mapping is applied explicitly here.
    """
    handle = recipe["handle"]
    center_x, center_y, center_z = handle["center"]
    major_radius = handle["major_radius"]
    minor_radius = handle["minor_radius"]
    start_degrees = handle["start_degrees"]
    end_degrees = handle["end_degrees"]
    arc_segments = handle["arc_segments"]
    tube_segments = handle["tube_segments"]

    vertices: list[tuple[float, float, float]] = []
    rings: list[list[int]] = []
    for arc_index in range(arc_segments + 1):
        progress = arc_index / arc_segments
        angle = math.radians(
            start_degrees + (end_degrees - start_degrees) * progress)
        radial_x = math.cos(angle)
        radial_height = math.sin(angle)
        ring: list[int] = []
        for tube_index in range(tube_segments):
            tube_angle = math.tau * tube_index / tube_segments
            radial_offset = math.cos(tube_angle) * minor_radius
            depth_offset = math.sin(tube_angle) * minor_radius
            ring.append(len(vertices))
            vertices.append((
                center_x + radial_x * (major_radius + radial_offset),
                center_z + depth_offset,
                center_y + radial_height * (major_radius + radial_offset),
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


def service_vessel_highlight_geometry(recipe: dict) -> kit.Geometry:
    """Thin authored rails surrounding a vessel without hiding its liquid."""
    radius = max(point[0] for point in recipe["glass"])
    height = recipe["glass"][-1][1]
    rail = 0.008
    left = -radius - 0.014
    right = radius + 0.014
    if "handle" in recipe:
        handle = recipe["handle"]
        right = max(
            right,
            handle["center"][0] + handle["major_radius"] +
            handle["minor_radius"] + 0.014,
        )
    center_x = (left + right) * 0.5
    width = right - left
    depth = radius + 0.014
    rails = [
        bp.u_box(
            (x, height * 0.5, 0.0),
            (rail, height + rail * 2.0, rail),
            rail * 0.25)
        for x in (left, right)
    ] + [
        bp.u_box(
            (center_x, height * 0.5, side * depth),
            (rail, height + rail * 2.0, rail),
            rail * 0.25)
        for side in (-1.0, 1.0)
    ]
    for y in (-rail * 0.5, height + rail * 0.5):
        rails.extend([
            bp.u_box(
                (center_x, y, side * depth),
                (width + rail, rail, rail),
                rail * 0.25)
            for side in (-1.0, 1.0)
        ])
        rails.extend([
            bp.u_box(
                (x, y, 0.0),
                (rail, rail, depth * 2.0 + rail),
                rail * 0.25)
            for x in (left, right)
        ])
    return kit.merge_all(rails)


def add_service_bottle(
    asset: AssetBuild,
    materials: dict,
    style: str,
) -> None:
    group = f"service:bottle:{style}"
    bottle = bp.rgb(0.28, 0.20, 0.11)
    closure = bp.rgb(0.52, 0.48, 0.36)
    label = bp.rgb(0.72, 0.66, 0.48)
    body: list[kit.Geometry]
    total_height: float
    width: float
    depth: float

    if style == "WaterBottle":
        width, depth, total_height = 0.21, 0.21, 0.68
        body = [
            bp.u_cylinder((0.0, 0.24, 0.0), (0.21, 0.24, 0.21), sides=12),
            bp.u_tapered_cylinder(
                (0.0, 0.52, 0.0), (0.18, 0.04, 0.18), 0.48, sides=12),
            bp.u_cylinder((0.0, 0.61, 0.0), (0.085, 0.05, 0.085), sides=12),
        ]
        cap = bp.u_cylinder(
            (0.0, 0.66, 0.0), (0.098, 0.02, 0.098), sides=12)
    elif style == "BeerLongneck":
        width, depth, total_height = 0.21, 0.21, 0.72
        body = [
            bp.u_cylinder((0.0, 0.22, 0.0), (0.21, 0.22, 0.21), sides=12),
            bp.u_tapered_cylinder(
                (0.0, 0.475, 0.0), (0.17, 0.035, 0.17),
                0.49, sides=12),
            bp.u_cylinder((0.0, 0.61, 0.0), (0.083, 0.10, 0.083), sides=12),
        ]
        cap = bp.u_cylinder(
            (0.0, 0.7025, 0.0), (0.098, 0.0175, 0.098), sides=12)
    elif style == "WineBottle":
        width, depth, total_height = 0.23, 0.23, 0.82
        body = [
            bp.u_cylinder((0.0, 0.25, 0.0), (0.23, 0.25, 0.23), sides=12),
            bp.u_tapered_cylinder(
                (0.0, 0.535, 0.0), (0.18, 0.035, 0.18),
                0.45, sides=12),
            bp.u_cylinder((0.0, 0.685, 0.0), (0.08, 0.115, 0.08), sides=12),
        ]
        cap = bp.u_cylinder(
            (0.0, 0.8025, 0.0), (0.088, 0.0175, 0.088), sides=12)
        closure = bp.rgb(0.48, 0.30, 0.16)
    elif style == "VodkaBottle":
        width, depth, total_height = 0.25, 0.18, 0.67
        body = [
            bp.u_box((0.0, 0.22, 0.0), (0.25, 0.44, 0.18), 0.018),
            bp.u_tapered_cylinder(
                (0.0, 0.475, 0.0), (0.18, 0.035, 0.15),
                0.58, sides=12),
            bp.u_cylinder((0.0, 0.58, 0.0), (0.105, 0.07, 0.105), sides=12),
        ]
        cap = bp.u_cylinder(
            (0.0, 0.645, 0.0), (0.12, 0.025, 0.12), sides=12)
        bottle = bp.rgb(0.62, 0.76, 0.78)
    elif style == "CognacBottle":
        width, depth, total_height = 0.30, 0.24, 0.61
        body = [
            bp.u_tapered_cylinder(
                (0.0, 0.18, 0.0), (0.30, 0.18, 0.24), 0.80, sides=12),
            bp.u_tapered_cylinder(
                (0.0, 0.40, 0.0), (0.24, 0.04, 0.20), 0.50, sides=12),
            bp.u_cylinder((0.0, 0.505, 0.0), (0.10, 0.065, 0.10), sides=12),
        ]
        cap = bp.u_cylinder(
            (0.0, 0.59, 0.0), (0.125, 0.02, 0.125), sides=12)
        closure = bp.rgb(0.38, 0.19, 0.08)
    else:
        raise ValueError(f"unsupported service bottle style '{style}'")

    add_part(
        asset, materials, f"Service {style} Body", kit.merge_all(body),
        "service_bottle_body", bottle, group=group, shadows=False)
    add_part(
        asset, materials, f"Service {style} Closure", cap,
        "service_bottle_closure", closure, group=group, shadows=False)
    label_y = min(0.18, total_height * 0.27)
    label_z = -depth * 0.5 - 0.011
    label_geometry = kit.merge_all([
        bp.u_box(
            (0.0, label_y, label_z),
            (width * 0.76, min(0.18, total_height * 0.27), 0.018), 0.004),
        bp.u_box(
            (0.0, label_y + 0.035, label_z - 0.002),
            (width * 0.52, 0.026, 0.021), 0.004),
    ])
    add_part(
        asset, materials, f"Service {style} Blank Label", label_geometry,
        "service_bottle_label", label, group=group, shadows=False)
    add_anchor(
        asset, f"{style}Mouth", f"service_bottle_mouth:{style}",
        (0.0, total_height, 0.0))


def service_menu_leaf_geometry(
    side: int,
    total_width: float,
    depth: float,
    thickness: float,
    spine_gap: float,
    minimum_y: float,
) -> tuple[kit.Geometry, float]:
    """Build one cafe-language menu leaf and return its vertical offset."""
    if side not in (-1, 1):
        raise ValueError("A service menu leaf side must be -1 or +1.")
    leaf_width = (total_width - spine_gap) * 0.5
    inner_x = side * spine_gap * 0.5
    center_x = inner_x + side * leaf_width * 0.5
    geometry = bp.u_box(
        (center_x, thickness * 0.5, 0.0),
        (leaf_width, thickness, depth),
        min(0.001, thickness * 0.20))
    geometry = bp.u_rotated_about(
        geometry,
        (0.0, 0.0, -side * SERVICE_MENU_OPEN_ANGLE_DEGREES),
        (inner_x, 0.0, 0.0))
    low, _ = kit.bounds(geometry)
    y_offset = minimum_y - low[1]
    return kit.translated(geometry, (0.0, y_offset, 0.0)), y_offset


def service_menu_page_surface_y(
    side: int,
    local_x: float,
    page_y_offset: float,
) -> float:
    """Upper surface of either tilted page in menu-local Unity space."""
    angle = math.radians(SERVICE_MENU_OPEN_ANGLE_DEGREES)
    inner_x = side * SERVICE_MENU_PAGE_GAP * 0.5
    return (
        -side * (local_x - inner_x) * math.sin(angle)
        + SERVICE_MENU_PAGE_THICKNESS * math.cos(angle)
        + page_y_offset)


def service_menu_row_basis(
    side: int,
) -> tuple[tuple[float, float, float],
           tuple[float, float, float],
           tuple[float, float, float]]:
    """Cafe/TMP pose: +X line-right, +Y page-up, -Z paper-outward."""
    angle = math.radians(SERVICE_MENU_OPEN_ANGLE_DEGREES)
    cosine = math.cos(angle)
    sine = math.sin(angle)
    right = (cosine, -side * sine, 0.0)
    up = (0.0, 0.0, 1.0)
    # TMP's visible face is local -Z. Keeping +Z in the paper makes the
    # transform right-handed while its -forward remains the physical normal.
    forward = (-side * sine, -cosine, 0.0)
    return right, up, forward


def service_menu_relief_geometry(
    left_page_y_offset: float,
    right_page_y_offset: float,
) -> kit.Geometry:
    """Unreadable page borders and a raised binding; text stays runtime."""
    details: list[kit.Geometry] = [
        bp.u_box(
            (0.0, 0.030, 0.0),
            (0.014, 0.012, SERVICE_MENU_PAGE_DEPTH - 0.025),
            0.003),
    ]
    for side, y_offset in (
        (-1, left_page_y_offset),
        (1, right_page_y_offset),
    ):
        inner_x = side * SERVICE_MENU_PAGE_GAP * 0.5
        page_details = kit.merge_all([
            bp.u_box(
                (side * 0.145,
                 SERVICE_MENU_PAGE_THICKNESS + 0.0015,
                 0.205),
                (0.185, 0.003, 0.007), 0.001),
            bp.u_box(
                (side * 0.145,
                 SERVICE_MENU_PAGE_THICKNESS + 0.0015,
                 -0.205),
                (0.185, 0.003, 0.007), 0.001),
            bp.u_box(
                (side * 0.252,
                 SERVICE_MENU_PAGE_THICKNESS + 0.0015,
                 0.0),
                (0.007, 0.003, 0.330), 0.001),
        ])
        page_details = bp.u_rotated_about(
            page_details,
            (0.0, 0.0, -side * SERVICE_MENU_OPEN_ANGLE_DEGREES),
            (inner_x, 0.0, 0.0))
        details.append(kit.translated(page_details, (0.0, y_offset, 0.0)))
    return kit.merge_all(details)


def build_service_props(materials: dict) -> AssetBuild:
    collection = bpy.data.collections.new("ServiceProps")
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new("ROOT_BarServiceProps3D", None)
    root.empty_display_type = "PLAIN_AXES"
    collection.objects.link(root)
    asset = AssetBuild(root, collection)

    for style in SERVICE_BOTTLE_STYLES:
        add_service_bottle(asset, materials, style)

    for vessel, recipe in SERVICE_VESSEL_PROFILES.items():
        group = f"service:vessel:{vessel}"
        glass = kit.lathe(recipe["glass"], segments=12)
        if "handle" in recipe:
            glass = kit.merge(
                glass,
                service_vessel_handle_geometry(recipe),
            )
        liquid = kit.translated(
            kit.lathe(recipe["liquid"], segments=12),
            (0.0, 0.0, recipe["liquid_base"]))
        add_part(
            asset, materials, f"Service {vessel} Shell", glass,
            "service_vessel_shell", bp.rgb(0.62, 0.82, 0.86),
            group=group, shadows=False, unity_space=False)
        add_part(
            asset, materials, f"Service {vessel} Liquid", liquid,
            "service_vessel_liquid", bp.rgb(0.90, 0.58, 0.18),
            group=group, shadows=False, unity_space=False)
        add_part(
            asset, materials, f"Service {vessel} Highlight",
            service_vessel_highlight_geometry(recipe),
            "service_vessel_highlight", bp.rgb(1.0, 0.72, 0.05),
            group=group, emissive=True, shadows=False)
        glass_height = recipe["glass"][-1][1]
        add_anchor(
            asset, f"{vessel}PourTarget",
            f"service_vessel_target:{vessel}",
            (0.0, recipe.get("pour_target_y", glass_height - 0.025), 0.0))
        add_anchor(
            asset, f"{vessel}LiquidBase",
            f"service_vessel_liquid_base:{vessel}",
            (0.0, recipe["liquid_base"], 0.0))
        add_anchor(
            asset, f"{vessel}Grip",
            f"service_vessel_grip:{vessel}",
            recipe["grip"])
        if "drink_rim" in recipe:
            add_anchor(
                asset, f"{vessel}DrinkRim",
                f"service_vessel_drink_rim:{vessel}",
                recipe["drink_rim"])

    menu_group = "service:menu"
    left_cover, _ = service_menu_leaf_geometry(
        -1,
        SERVICE_MENU_COVER_WIDTH,
        SERVICE_MENU_COVER_DEPTH,
        SERVICE_MENU_COVER_THICKNESS,
        SERVICE_MENU_COVER_GAP,
        SERVICE_MENU_COVER_MIN_Y)
    right_cover, _ = service_menu_leaf_geometry(
        1,
        SERVICE_MENU_COVER_WIDTH,
        SERVICE_MENU_COVER_DEPTH,
        SERVICE_MENU_COVER_THICKNESS,
        SERVICE_MENU_COVER_GAP,
        SERVICE_MENU_COVER_MIN_Y)
    left_page, left_page_y_offset = service_menu_leaf_geometry(
        -1,
        SERVICE_MENU_PAGE_WIDTH,
        SERVICE_MENU_PAGE_DEPTH,
        SERVICE_MENU_PAGE_THICKNESS,
        SERVICE_MENU_PAGE_GAP,
        SERVICE_MENU_PAGE_MIN_Y)
    right_page, right_page_y_offset = service_menu_leaf_geometry(
        1,
        SERVICE_MENU_PAGE_WIDTH,
        SERVICE_MENU_PAGE_DEPTH,
        SERVICE_MENU_PAGE_THICKNESS,
        SERVICE_MENU_PAGE_GAP,
        SERVICE_MENU_PAGE_MIN_Y)
    add_part(
        asset, materials, "Service Menu Cover",
        kit.merge_all((left_cover, right_cover)),
        "service_menu_cover", bp.rgb(0.11, 0.045, 0.025),
        group=menu_group, sheet="DarkWood", shadows=False)
    add_part(
        asset, materials, "Service Menu Pages",
        kit.merge_all((left_page, right_page)),
        "service_menu_pages", bp.rgb(0.74, 0.66, 0.47),
        group=menu_group, shadows=False)
    add_part(
        asset, materials, "Service Menu Blank Relief",
        service_menu_relief_geometry(
            left_page_y_offset,
            right_page_y_offset),
        "service_menu_relief", bp.rgb(0.47, 0.25, 0.08),
        group=menu_group, shadows=False)

    add_anchor(
        asset, "BarMenu.Origin", "service_menu_origin", (0.0, 0.0, 0.0))
    add_anchor(
        asset, "Grip.BarMenu", "service_menu_grip", SERVICE_MENU_GRIP)

    # The common focus basis is centred on the open spread. Each point is
    # exactly 0.10 m from the origin, making camera framing independent of
    # imported Empty scale or Blender axis conversion.
    spread_y = max(
        service_menu_page_surface_y(-1, -SERVICE_MENU_PAGE_GAP * 0.5,
                                    left_page_y_offset),
        service_menu_page_surface_y(1, SERVICE_MENU_PAGE_GAP * 0.5,
                                    right_page_y_offset),
    ) + SERVICE_MENU_TEXT_CLEARANCE
    spread_origin = (0.0, spread_y, 0.0)
    add_anchor(
        asset, "BarMenuPage.Origin", "service_menu_page_origin",
        spread_origin)
    add_anchor(
        asset, "BarMenuPage.Right", "service_menu_page_right",
        (SERVICE_MENU_BASIS_LENGTH, spread_y, 0.0))
    add_anchor(
        asset, "BarMenuPage.Up", "service_menu_page_up",
        (0.0, spread_y, SERVICE_MENU_BASIS_LENGTH))
    add_anchor(
        asset, "BarMenuPage.Normal", "service_menu_page_normal",
        (0.0, spread_y + SERVICE_MENU_BASIS_LENGTH, 0.0))

    # Five legacy sockets sit on the left leaf and four on the right. Runtime
    # maps its four visible blocks to 00/04 and 05/08, leaving enough vertical
    # room for descriptions. Their imported Transform axes are the authored
    # cafe/TMP pose: +X reads along the row, +Y toward the page head and local
    # -Z points out of the paper.
    row_index = 0
    for side, row_positions in (
        (-1, SERVICE_MENU_LEFT_ROW_Z),
        (1, SERVICE_MENU_RIGHT_ROW_Z),
    ):
        local_x = side * SERVICE_MENU_TEXT_X
        local_y = service_menu_page_surface_y(
            side, local_x,
            left_page_y_offset if side < 0 else right_page_y_offset)
        right, up, forward = service_menu_row_basis(side)
        for local_z in row_positions:
            add_anchor(
                asset,
                f"BarMenuText.Item.{row_index:02d}",
                f"service_menu_text_item:{row_index:02d}",
                (local_x, local_y + SERVICE_MENU_TEXT_CLEARANCE, local_z),
                right,
                up,
                forward)
            row_index += 1

    selection_side = -1
    selection_x = -SERVICE_MENU_SELECTION_X
    selection_y = service_menu_page_surface_y(
        selection_side, selection_x, left_page_y_offset)
    selection_right, selection_up, selection_forward = (
        service_menu_row_basis(selection_side))
    add_anchor(
        asset,
        "BarMenuText.Selection",
        "service_menu_text_selection",
        (selection_x,
         selection_y + SERVICE_MENU_TEXT_CLEARANCE,
         0.14),
        selection_right,
        selection_up,
        selection_forward)

    stream_group = "service:pour_stream"
    add_part(
        asset, materials, "Service Pour Stream",
        # Unity's cylinder primitive is two units tall. Keeping that exact
        # centred contract lets BarDrinkServiceView place the midpoint and
        # scale Y by length * 0.5 for an exact start-to-end stream.
        bp.u_cylinder((0.0, 0.0, 0.0), (0.026, 1.0, 0.026), sides=8),
        "service_pour_stream", bp.rgb(0.90, 0.58, 0.18),
        group=stream_group, shadows=False)
    add_anchor(asset, "PourStreamOrigin", "service_pour_stream_origin",
               (0.0, 0.0, 0.0))
    return asset


def validate_service_props(asset: AssetBuild) -> None:
    problems: list[str] = []
    names: set[str] = set()
    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate service prop name '{part.name}'")
        names.add(part.name)
        if not part.emissive and not part.sheet:
            problems.append(
                f"'{part.name}' has no authored physical surface")
        elif part.sheet not in SHEET_PITCH:
            problems.append(
                f"'{part.name}' names an unmeasured sheet '{part.sheet}'")
        if part.colliders:
            problems.append(
                f"'{part.name}' carries collision; runtime owns prop state")
        volume = bp.signed_volume(part.geometry)
        if volume <= 0.0:
            problems.append(
                f"'{part.name}' has inverted normals "
                f"(signed volume {volume:.5f})")

    groups = {part.group for part in asset.parts}
    for style in SERVICE_BOTTLE_STYLES:
        group = f"service:bottle:{style}"
        if group not in groups:
            problems.append(f"service bottle '{style}' is missing")
    for vessel in SERVICE_VESSEL_PROFILES:
        group = f"service:vessel:{vessel}"
        if group not in groups:
            problems.append(f"service vessel '{vessel}' is missing")
            continue
        vessel_parts = [part for part in asset.parts if part.group == group]
        highlights = [
            part for part in vessel_parts
            if part.role == "service_vessel_highlight"
        ]
        if len(highlights) != 1:
            problems.append(
                f"service vessel '{vessel}' needs one authored highlight")
        grip_role = f"service_vessel_grip:{vessel}"
        grip = next((
            anchor for anchor in asset.anchors.values()
            if anchor.get("bp_role") == grip_role), None)
        if grip is None:
            problems.append(
                f"service vessel '{vessel}' has no authored grip")
        else:
            expected_grip = SERVICE_VESSEL_PROFILES[vessel]["grip"]
            actual_grip = anchor_unity_position(grip)
            if any(abs(actual_grip[axis] - expected_grip[axis]) > 0.001
                   for axis in range(3)):
                problems.append(
                    f"service vessel '{vessel}' grip left its surface")

        recipe = SERVICE_VESSEL_PROFILES[vessel]
        handle_recipe = recipe.get("handle")
        if vessel == "Pint" and handle_recipe is None:
            problems.append("the Pint compatibility group has no mug handle")
        elif handle_recipe is not None:
            handle = service_vessel_handle_geometry(recipe)
            if bp.signed_volume(handle) <= 0.0:
                problems.append(
                    f"service vessel '{vessel}' handle has inverted normals")
            handle_low, handle_high = kit.bounds(handle)
            body_radius = max(point[0] for point in recipe["glass"])
            if (handle_low[0] > body_radius or
                    handle_high[0] < body_radius + 0.035):
                problems.append(
                    f"service vessel '{vessel}' handle is not joined on +X")
            expected_handle_grip_x = (
                handle_recipe["center"][0] +
                handle_recipe["major_radius"])
            if abs(recipe["grip"][0] - expected_handle_grip_x) > 0.001:
                problems.append(
                    f"service vessel '{vessel}' grip left its handle arc")

        rim_role = f"service_vessel_drink_rim:{vessel}"
        rims = [
            anchor for anchor in asset.anchors.values()
            if anchor.get("bp_role") == rim_role
        ]
        expected_rim = recipe.get("drink_rim")
        if expected_rim is None:
            problems.append(
                f"service vessel '{vessel}' has no authored drink rim")
        else:
            expected_edge = (
                0.0,
                recipe["glass"][-1][1],
                -recipe["glass"][-1][0],
            )
            if any(abs(expected_rim[axis] - expected_edge[axis]) > 0.001
                   for axis in range(3)):
                problems.append(
                    f"service vessel '{vessel}' drink rim is not on its "
                    "top -Z edge")
            if len(rims) != 1:
                problems.append(
                    f"service vessel '{vessel}' needs one authored drink rim")
            else:
                actual_rim = anchor_unity_position(rims[0])
                if any(abs(actual_rim[axis] - expected_rim[axis]) > 0.001
                       for axis in range(3)):
                    problems.append(
                        f"service vessel '{vessel}' drink rim left its edge")
                if actual_rim[2] >= 0.0:
                    problems.append(
                        f"service vessel '{vessel}' drink rim is not on -Z")
                identity_basis = {
                    "bp_unity_right": (1.0, 0.0, 0.0),
                    "bp_unity_up": (0.0, 1.0, 0.0),
                    "bp_unity_forward": (0.0, 0.0, 1.0),
                }
                for key, expected_axis in identity_basis.items():
                    actual_axis = rims[0].get(key)
                    if actual_axis is None or any(
                            abs(actual_axis[axis] - expected_axis[axis]) >
                            0.001 for axis in range(3)):
                        problems.append(
                            f"service vessel '{vessel}' drink rim lost its "
                            "identity basis")
                        break

        if vessel == "Pint":
            shells = [
                part for part in vessel_parts
                if part.role == "service_vessel_shell"
            ]
            if len(shells) != 1:
                problems.append(
                    "the Pint compatibility group needs one mug shell")
            else:
                shell_low, shell_high = kit.bounds(shells[0].geometry)
                expected_low = (
                    -SERVICE_PINT_MUG_RIM_RADIUS,
                    -SERVICE_PINT_MUG_RIM_RADIUS,
                    0.0,
                )
                expected_high = (
                    SERVICE_PINT_MUG_HANDLE_CENTER[0] +
                    SERVICE_PINT_MUG_HANDLE_MAJOR_RADIUS +
                    SERVICE_PINT_MUG_HANDLE_MINOR_RADIUS,
                    SERVICE_PINT_MUG_RIM_RADIUS,
                    SERVICE_PINT_MUG_HEIGHT,
                )
                if any(abs(shell_low[axis] - expected_low[axis]) > 0.001
                       for axis in range(3)) or any(
                        abs(shell_high[axis] - expected_high[axis]) > 0.001
                        for axis in range(3)):
                    problems.append(
                        "the Pint compatibility mug left its measured bounds")
    for group in ("service:menu", "service:pour_stream"):
        if group not in groups:
            problems.append(f"service group '{group}' is missing")

    menu_roles = {
        part.role for part in asset.parts if part.group == "service:menu"
    }
    for role in (
        "service_menu_cover",
        "service_menu_pages",
        "service_menu_relief",
    ):
        if role not in menu_roles:
            problems.append(f"the authored menu has no '{role}' geometry")

    required_menu_anchors = {
        "BarMenu.Origin": "service_menu_origin",
        "Grip.BarMenu": "service_menu_grip",
        "BarMenuPage.Origin": "service_menu_page_origin",
        "BarMenuPage.Right": "service_menu_page_right",
        "BarMenuPage.Up": "service_menu_page_up",
        "BarMenuPage.Normal": "service_menu_page_normal",
        "BarMenuText.Selection": "service_menu_text_selection",
    }
    for index in range(9):
        required_menu_anchors[f"BarMenuText.Item.{index:02d}"] = (
            f"service_menu_text_item:{index:02d}")
    for name, role in required_menu_anchors.items():
        anchor = asset.anchors.get(name)
        if anchor is None or anchor.get("bp_role") != role:
            problems.append(
                f"the authored menu anchor '{name}' / '{role}' is missing")

    row_anchors = [
        asset.anchors.get(f"BarMenuText.Item.{index:02d}")
        for index in range(9)
    ]
    if all(anchor is not None for anchor in row_anchors):
        for index, anchor in enumerate(row_anchors):
            right = tuple(anchor.get("bp_unity_right", ()))
            up = tuple(anchor.get("bp_unity_up", ()))
            forward = tuple(anchor.get("bp_unity_forward", ()))
            if (len(right) != 3 or len(up) != 3 or len(forward) != 3 or
                    forward[1] >= -0.98 or up != (0.0, 0.0, 1.0)):
                problems.append(
                    f"menu row {index:02d} lost its cafe/TMP page basis")

    stream = next((
        part for part in asset.parts
        if part.role == "service_pour_stream"), None)
    if stream is None:
        problems.append("the authored pour stream is missing")
    else:
        stream_low, stream_high = kit.bounds(stream.geometry)
        if (abs(stream_low[2] + 1.0) > 1e-6 or
                abs(stream_high[2] - 1.0) > 1e-6):
            problems.append(
                "the authored pour stream is not a centred two-unit "
                "Unity cylinder")

    if problems:
        raise SystemExit(
            "Bar service props failed validation:\n  " +
            "\n  ".join(problems))


# ------------------------------------------------------------ facade --


def build_facade(materials: dict) -> AssetBuild:
    """The complete two-storey pub, authored once around its front door."""
    collection = bpy.data.collections.new("Exterior")
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new("ROOT_BarExterior3D", None)
    root.empty_display_type = "PLAIN_AXES"
    collection.objects.link(root)
    asset = AssetBuild(root, collection)

    for recipe in exterior.build_parts():
        add_part(
            asset,
            materials,
            recipe.name,
            recipe.geometry,
            recipe.role,
            recipe.tint,
            group=recipe.group,
            sheet=recipe.sheet,
            emissive=recipe.emissive,
            shadows=recipe.shadows)

    add_anchor(asset, "Door", "exterior_door", exterior.DOOR_ANCHOR)
    add_anchor(asset, "SignMarker", "sign_pivot", exterior.SIGN_PIVOT)
    for suffix, point in (("InnerMin", (washroom.INNER[0], 0., washroom.INNER[1])),
                          ("InnerMax", (washroom.INNER[2], washroom.HEIGHT, washroom.INNER[3])),
                          ("Door", (washroom.DOOR_X, 0., washroom.DOOR_Z))):
        add_anchor(asset, "ExteriorWashroom" + suffix, "washroom_" + suffix.lower(),
                   washroom.to_exterior(point))
    asset.washroom_contract = washroom.contract()
    return asset


def validate_facade(asset: AssetBuild) -> None:
    problems: list[str] = []
    names: set[str] = set()
    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate part name '{part.name}'")
        names.add(part.name)
        if part.colliders:
            problems.append(
                f"'{part.name}' declares collision; the bar entrance "
                "trigger belongs to CityWorldBuilder")
        volume = bp.signed_volume(part.geometry)
        if volume <= 0.0:
            problems.append(
                f"'{part.name}' has inverted normals "
                f"(signed volume {volume:.5f})")

    required_roles = {
        "exterior_masonry",
        "exterior_plaster",
        "exterior_roof",
        "exterior_wood",
        "exterior_window_ground",
        "exterior_window_upper_warm",
        "exterior_window_upper_dark",
        "exterior_door",
        "exterior_metal",
        "sign_part",
    }
    roles = {part.role for part in asset.parts}
    for role in sorted(required_roles - roles):
        problems.append(f"the exterior has no '{role}' geometry")
    if not any(part.group.startswith("pivot:") for part in asset.parts):
        problems.append("the exterior has no hanging sign")

    reveal = next(
        (part for part in asset.parts
         if part.name == "Bar Entrance Reveal Panels"),
        None)
    if reveal is None:
        problems.append("the recessed entrance has no reveal panels")
    else:
        reveal_low, reveal_high = kit.bounds(reveal.geometry)
        # Geometry is in Blender source space: source Y is Unity Z and
        # source Z is Unity Y. Both jambs must bridge from the recessed door
        # to behind the projecting trim and pilasters on both sides of the
        # opening without burying the cream frames.
        if reveal_low[0] > -0.23 or reveal_high[0] < 0.095 or \
                reveal_low[1] > -1.005 or reveal_high[1] < 1.005 or \
                reveal_high[2] < 2.77:
            problems.append(
                "the entrance reveals no longer seal the recessed portal")

    flanks = next(
        (part for part in asset.parts
         if part.name == "Bar Entrance Flanking Panels"),
        None)
    if flanks is None:
        problems.append("the entrance has no flanking infill panels")
    else:
        flank_low, flank_high = kit.bounds(flanks.geometry)
        # The panels bridge the open strips between the inner pilasters and
        # the faceted bay returns. Source Y is Unity Z; source Z is Unity Y.
        if flank_low[0] > -0.065 or flank_high[0] < 0.225 or \
                flank_low[1] > -1.635 or flank_high[1] < 1.635 or \
                flank_low[2] > 0.225 or flank_high[2] < 3.135:
            problems.append(
                "the entrance flanking panels no longer close the shopfront")

    outer_flanks = next(
        (part for part in asset.parts
         if part.name == "Bar Outer Bay Flanking Panels"),
        None)
    if outer_flanks is None:
        problems.append("the outer bay edges have no flanking panels")
    else:
        outer_vertices, _ = outer_flanks.geometry
        for side in (-1, 1):
            side_vertices = [
                vertex for vertex in outer_vertices
                if vertex[1] * side > 0.0
            ]
            side_low, side_high = kit.bounds((side_vertices, []))
            common_gap = (
                side_low[0] > -0.135 or side_high[0] < 0.075 or
                side_low[2] > 0.225 or side_high[2] < 3.135
            )
            frontage_gap = (
                side < 0 and
                (side_low[1] > -5.53 or side_high[1] < -5.14)
            ) or (
                side > 0 and
                (side_low[1] > 5.14 or side_high[1] < 5.53)
            )
            if not side_vertices or common_gap or frontage_gap:
                problems.append(
                    "the outer bay flanking panels no longer close both "
                    "shopfront edges")
                break

    fixed = [
        part.geometry for part in asset.parts
        if not part.group.startswith("pivot:")
    ]
    low, high = kit.bounds(kit.merge_all(fixed))
    if low[0] > -exterior.DEPTH + 0.20 or high[0] < 0.9:
        problems.append(
            "the exterior no longer spans its rear service wing and canopy")
    # `Part.geometry` is already in Blender source space here: Unity Y/Z
    # arrive as source Z/Y respectively.
    if high[2] > exterior.HEIGHT + 0.001:
        problems.append(
            f"the exterior is {high[2]:.4f} m high against its "
            f"{exterior.HEIGHT:.4f} m envelope")
    if high[2] < exterior.HEIGHT - 0.01:
        problems.append(
            f"the chimney pots stop at {high[2]:.4f} m instead of "
            f"{exterior.HEIGHT:.4f} m")
    # Repair skins and rain goods may sit up to eight centimetres proud of
    # the masonry footprint; the roof and structural shell stay inside it.
    if low[1] < -exterior.HALF_WIDTH - 0.08 or \
            high[1] > exterior.HALF_WIDTH + 0.08:
        problems.append(
            f"the exterior spans Z {low[1]:.4f}..{high[1]:.4f} against "
            f"+/-{exterior.HALF_WIDTH:.4f}")

    if problems:
        raise SystemExit(
            "Bar exterior failed validation:\n  " + "\n  ".join(problems))


# ------------------------------------------------------------ build ---


def build() -> tuple[AssetBuild, AssetBuild, AssetBuild]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials = {key: create_material(key) for key in PREVIEW_COLORS}
    return (
        build_interior(materials),
        build_service_props(materials),
        build_facade(materials),
    )


def build_interior(materials: dict) -> AssetBuild:
    collection = bpy.data.collections.new("Interior")
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new("ROOT_BarInterior3D", None)
    root.empty_display_type = "PLAIN_AXES"
    collection.objects.link(root)
    asset = AssetBuild(root, collection)

    build_shell(asset, materials)
    build_counter(asset, materials)
    compact.build(sys.modules[__name__], asset, materials)
    washroom.build(sys.modules[__name__], asset, materials)
    build_ceiling_fan(asset, materials)
    build_jukebox(asset, materials)
    build_practical_prefab(asset, materials)

    add_anchor(asset, "Entrance", "entrance", (0.0, 0.0, -ROOM_DEPTH * 0.5))
    add_anchor(asset, "RoomCentre", "room_centre", (0.0, 0.0, 0.0))
    add_anchor(asset, "CounterStation", "counter_station", COUNTER_STATION)
    add_anchor(asset, "ActivityStation", "activity_station",
               ACTIVITY_STATION)
    add_anchor(asset, "HeroSeat", "hero_seat", HERO_SEAT)
    add_anchor(asset, "HeroApproach", "hero_approach", HERO_APPROACH)
    add_anchor(asset, "HeroStand", "hero_stand", HERO_STAND)
    add_anchor(asset, "HeroCamera", "hero_camera",
               (HERO_SEAT[0], HERO_SEAT[1] + 0.80, HERO_SEAT[2] + 0.12))
    add_anchor(asset, "HeroCameraLook", "hero_camera_look",
               (HERO_SEAT[0], HERO_SEAT[1] + 0.90, 3.395))
    add_anchor(asset, "ServicePosition", "bartender_service_position",
               BARTENDER_POSITION)
    add_anchor(
        asset, "BeerTapServerDock", "beer_tap_server_dock",
        BEER_TAP_SERVER_DOCK,
        unity_right=(-1.0, 0.0, 0.0),
        unity_forward=(0.0, 0.0, -1.0))
    add_anchor(
        asset, "BeerTapVesselDock", "beer_tap_vessel_dock",
        BEER_TAP_VESSEL_DOCK)
    add_anchor(
        asset, "BeerTapSpout", "beer_tap_spout",
        BEER_TAP_STREAM_ORIGIN,
        unity_up=(0.0, 0.0, 1.0),
        unity_forward=(0.0, -1.0, 0.0))
    add_anchor(
        asset, "BeerTapHandlePivot", "beer_tap_handle_pivot",
        BEER_TAP_HANDLE_PIVOT)
    add_anchor(
        asset, "BeerTapHandleGrip", "beer_tap_handle_grip",
        BEER_TAP_HANDLE_GRIP)
    add_anchor(asset, "BartenderPlatformTop", "bartender_platform_top",
               BARTENDER_POSITION)
    add_anchor(asset, "MenuDock", "service_menu_dock",
               (COUNTER_STATION[0],
                COUNTER_SURFACE_Y + 0.025,
                SERVICE_MENU_DOCK_Z))
    add_anchor(asset, "VesselDock", "service_vessel_dock",
               (COUNTER_STATION[0], COUNTER_SURFACE_Y + 0.015,
                SERVICE_VESSEL_DOCK_Z))
    add_anchor(asset, "PourTarget", "service_pour_target",
               (COUNTER_STATION[0], COUNTER_SURFACE_Y + 0.015,
                SERVICE_VESSEL_DOCK_Z))
    add_anchor(asset, "CeilingFan", "ceiling_fan_pivot", CEILING_FAN_PIVOT)
    add_anchor(asset, "Jukebox", "jukebox_pivot", (5.0, 0.0, -0.65))
    compact.add_frame_anchors(sys.modules[__name__], asset)
    asset.containment_contract = compact.containment_contract()
    asset.containment_contract["washroom"] = washroom.contract()
    return asset


# --------------------------------------------------------- validation -


def validate(asset: AssetBuild) -> None:
    problems: list[str] = []
    names: set[str] = set()

    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate part name '{part.name}'")
        names.add(part.name)
        if part.sheet not in SHEET_PITCH:
            problems.append(
                f"'{part.name}' names an unmeasured sheet '{part.sheet}'")
        if not part.emissive and not part.sheet:
            problems.append(
                f"'{part.name}' has no authored physical surface")

        #  A reflection reverses winding. If `to_source` ever stops
        #  re-winding, every solid here turns inside out - lit from
        #  within, invisible from without - and nothing but a rendered
        #  frame would show it. A negative volume says so instead.
        volume = bp.signed_volume(part.geometry)
        if volume <= 0.0:
            problems.append(
                f"'{part.name}' has inverted normals "
                f"(signed volume {volume:.5f})")

    fixed = [
        part for part in asset.parts
        if part.group == "fixed" or part.group.startswith("collision")
    ]
    merged = kit.merge_all([part.geometry for part in fixed])
    low, high = kit.bounds(merged)
    if abs(high[0] - low[0] - (ROOM_WIDTH + WALL_THICKNESS)) > 0.02:
        problems.append(
            f"room width reads {high[0] - low[0]:.3f} m against "
            f"{ROOM_WIDTH + WALL_THICKNESS:.3f}")
    if low[2] > -FLOOR_THICKNESS + 0.001:
        problems.append("the floor slab is missing its thickness")
    # Verify the entire fixed shell against the MAIN exterior range. Its
    # asymmetric rear store is deliberately not used to inflate this bound.
    for part in fixed:
        for x, z, y in part.geometry[0]:
            exterior_x = -z - 4.06
            if not ((-8.58 - .001 <= exterior_x <= .001 and
                    -6.001 <= x <= 6.001 and y <= 3.28 + .001) or
                    washroom.contains((x, y, z))):
                problems.append(f"'{part.name}' leaves the facade's ground-floor contour")
                break
    door_part = next(part for part in exterior.build_parts()
                     if part.role == "exterior_door")
    door_low, door_high = kit.bounds(door_part.geometry)
    measured_door_x = (door_low[0] + door_high[0]) * .5
    if abs(measured_door_x - (-(-ROOM_DEPTH * .5) - 4.06)) > .001:
        problems.append("interior entrance does not meet the measured facade door")

    for part in asset.parts:
        if part.name != "Front Wall":
            continue
        reveal = min(
            abs(vertex[0])
            for vertex in part.geometry[0]
            if vertex[2] < DOOR_HEIGHT - 0.01)
        if abs(reveal * 2.0 - DOOR_WIDTH) > 0.02:
            problems.append(
                f"the doorway reads {reveal * 2.0:.3f} m against "
                f"{DOOR_WIDTH:.3f}")
        door_head = min(
            vertex[2]
            for vertex in part.geometry[0]
            if abs(vertex[0]) < DOOR_WIDTH * 0.5 - 0.01)
        if abs(door_head - DOOR_HEIGHT) > 0.02:
            problems.append(
                f"the doorway head reads {door_head:.3f} m against "
                f"{DOOR_HEIGHT:.3f}")

    standard_door_parts = {
        "Entrance Door Frame",
        "Entrance Door",
        "Entrance Door Panels",
        "Entrance Door Transom Glass",
        "Entrance Door Furniture",
    }
    missing_door_parts = standard_door_parts - names
    for part_name in sorted(missing_door_parts):
        problems.append(f"the standard entrance has no '{part_name}'")
    if "Entrance Heavy Curtains" in names or \
            "Entrance Curtain Brass Rail" in names:
        problems.append("the former full-height entrance portal returned")

    if not any(part.group == "activity:BeerPong" for part in asset.parts):
        problems.append("the retained beer-pong table is missing")
    if any(part.group in {"activity:Cocktail", "activity:SplitTheG",
                          "activity:TinctureMatch"} for part in asset.parts):
        problems.append("a removed three-vessel activity console returned")
    for mood in DISTRICT_MOODS:
        if not any(part.group == f"district:{mood}"
                   for part in asset.parts):
            problems.append(f"district set '{mood}' is missing")

    tap_stems = [
        part for part in asset.parts if part.role == "tap_stem"
    ]
    tap_handles = [
        part for part in asset.parts if part.role == "tap_handle"
    ]
    tap_spouts = [
        part for part in asset.parts if part.role == "tap_spout"
    ]
    if (len(tap_stems) != 3 or len(tap_handles) != 3 or
            len(tap_spouts) != 3):
        problems.append(
            "the counter must carry exactly three complete beer taps")

    tap_return_min = COUNTER_RETURN_POS[0] - COUNTER_RETURN_SIZE[0] * 0.5
    tap_return_max = COUNTER_RETURN_POS[0] + COUNTER_RETURN_SIZE[0] * 0.5
    tap_handle_radius = 0.13
    if (BEER_TAP_XS[0] - tap_handle_radius < tap_return_min or
            BEER_TAP_XS[-1] + tap_handle_radius > tap_return_max):
        problems.append(
            "the beer-tap bank leaves the seat-free counter-return edge")
    tap_steps = [
        BEER_TAP_XS[index + 1] - BEER_TAP_XS[index]
        for index in range(len(BEER_TAP_XS) - 1)
    ]
    if any(abs(step - BEER_TAP_SPACING) > 0.001 for step in tap_steps):
        problems.append("the three beer taps are not evenly clustered")
    if BEER_TAP_SPACING > 0.36:
        problems.append("the beer taps are spaced too far apart")
    if BEER_TAP_XS[0] - STOOL_XS[-1] < 0.70:
        problems.append("the beer-tap bank intrudes into the last stool bay")

    tap_anchor_contract = {
        "BeerTapServerDock": (
            "beer_tap_server_dock", BEER_TAP_SERVER_DOCK),
        "BeerTapVesselDock": (
            "beer_tap_vessel_dock", BEER_TAP_VESSEL_DOCK),
        "BeerTapSpout": (
            "beer_tap_spout", BEER_TAP_STREAM_ORIGIN),
        "BeerTapHandlePivot": (
            "beer_tap_handle_pivot", BEER_TAP_HANDLE_PIVOT),
        "BeerTapHandleGrip": (
            "beer_tap_handle_grip", BEER_TAP_HANDLE_GRIP),
    }
    for anchor_name, (role, expected) in tap_anchor_contract.items():
        anchor = asset.anchors.get(anchor_name)
        if anchor is None or anchor.get("bp_role") != role:
            problems.append(
                f"the beer tap has no '{anchor_name}' / '{role}' anchor")
            continue
        actual = anchor_unity_position(anchor)
        if any(abs(actual[axis] - expected[axis]) > 0.001
               for axis in range(3)):
            problems.append(
                f"beer-tap anchor '{anchor_name}' left its authored dock")

    if len(tap_handles) == 3:
        center_handle_origin = anchor_unity_position(tap_handles[1].obj)
        if any(abs(center_handle_origin[axis] -
                   BEER_TAP_HANDLE_PIVOT[axis]) > 0.001
               for axis in range(3)):
            problems.append(
                "the centre beer-tap handle is not pivoted at its base")
    pint_rim_y = (
        BEER_TAP_VESSEL_DOCK[1] +
        SERVICE_VESSEL_PROFILES["Pint"]["glass"][-1][1])
    if BEER_TAP_STREAM_ORIGIN[1] - pint_rim_y < 0.025:
        problems.append(
            "the centre beer-tap spout does not clear an upright pint")

    if "Bartender Duckboard" in names or any(
            part.role == "bartender_duckboard" for part in asset.parts):
        problems.append(
            "the obsolete six-arm bartender duckboard returned")

    menu_dock = asset.anchors.get("MenuDock")
    vessel_dock = asset.anchors.get("VesselDock")
    pour_target = asset.anchors.get("PourTarget")
    if menu_dock is None:
        problems.append("the bar has no canonical menu dock")
    elif (abs(anchor_unity_position(menu_dock)[0] - HERO_SEAT[0]) >
          0.001):
        problems.append("the menu dock is not centred before the hero")
    elif (abs(anchor_unity_position(menu_dock)[2] -
              SERVICE_MENU_DOCK_Z) > 0.001):
        problems.append("the closed menu left its canonical counter depth")
    if vessel_dock is None or pour_target is None:
        problems.append("the bar has no canonical vessel/pour dock")
    else:
        vessel_position = anchor_unity_position(vessel_dock)
        pour_position = anchor_unity_position(pour_target)
        if (abs(vessel_position[2] - SERVICE_VESSEL_DOCK_Z) > 0.001 or
                any(abs(vessel_position[axis] - pour_position[axis]) > 0.001
                    for axis in range(3))):
            problems.append(
                "the canonical vessel and pour docks no longer coincide")

    if menu_dock is not None and vessel_dock is not None:
        counter_min_z = COUNTER_POS[2] - COUNTER_SIZE[2] * 0.5
        counter_max_z = COUNTER_POS[2] + COUNTER_SIZE[2] * 0.5
        menu_z = anchor_unity_position(menu_dock)[2]
        vessel_z = anchor_unity_position(vessel_dock)[2]
        menu_half_depth = SERVICE_MENU_COVER_DEPTH * 0.5
        pint_radius = max(
            radius for radius, _ in
            SERVICE_VESSEL_PROFILES["Pint"]["glass"])
        menu_min_z = menu_z - menu_half_depth
        menu_max_z = menu_z + menu_half_depth
        pint_min_z = vessel_z - pint_radius
        pint_max_z = vessel_z + pint_radius
        if (menu_min_z < counter_min_z - 0.001 or
                menu_max_z > counter_max_z + 0.001 or
                pint_min_z < counter_min_z - 0.001 or
                pint_max_z > counter_max_z + 0.001):
            problems.append(
                "the closed menu or pint leaves the main counter top")
        if menu_min_z - pint_max_z < SERVICE_PROP_MINIMUM_GAP - 0.001:
            problems.append(
                "the closed menu and pint have less than 0.08 m clearance")

    asset.ceiling_fan_contract = validate_ceiling_fan(asset, problems)
    washroom.validate(asset, problems)
    if problems:
        raise SystemExit(
            "Bar interior failed validation:\n  " + "\n  ".join(problems))


def validate_ceiling_fan(asset: AssetBuild, problems: list[str]) -> dict:
    """Measure the full rotor sweep against each actual detached ceiling beam."""
    pivot = anchor_unity_position(asset.anchors["CeilingFan"])
    rotor = [part for part in asset.parts if part.group == "pivot:Slow Ceiling Fan"]
    points = [(x, y, z) for part in rotor for x, z, y in part.geometry[0]]
    radius = max(math.hypot(x, z) for x, y, z in points)
    top = pivot[1] + max(y for x, y, z in points)
    bottom = pivot[1] + min(y for x, y, z in points)
    clearances = []
    for part in (part for part in asset.parts if part.role == "beam"):
        vertices, faces = part.geometry
        parents = list(range(len(vertices)))
        def find(index):
            while parents[index] != index:
                parents[index] = parents[parents[index]]
                index = parents[index]
            return index
        for face in faces:
            for index in face[1:]:
                parents[find(index)] = find(face[0])
        components = {}
        for index, (x, z, y) in enumerate(vertices):
            components.setdefault(find(index), []).append((x, y, z))
        for component in components.values():
            low = [min(point[axis] for point in component) for axis in range(3)]
            high = [max(point[axis] for point in component) for axis in range(3)]
            nearest_x = max(low[0], min(pivot[0], high[0]))
            nearest_z = max(low[2], min(pivot[2], high[2]))
            if math.hypot(nearest_x - pivot[0], nearest_z - pivot[2]) > radius:
                continue
            gap = low[1] - top
            clearances.append(gap)
            if gap < .05 - 1e-6:
                problems.append(f"the full ceiling-fan sweep has only {gap:.4f} m clearance below {part.name}")
    if not clearances or bottom < 2.45:
        problems.append("the ceiling fan lost its supported overhead clearance")
    stem = next(part for part in asset.parts if part.name == "Fan Suspension Stem")
    stem_points = [(x, y, z) for x, z, y in stem.geometry[0]]
    stem_low = min(point[1] for point in stem_points)
    stem_high = max(point[1] for point in stem_points)
    beam_bottom = top + min(clearances) if clearances else float("inf")
    if stem.group != "fixed" or stem.shadows or abs(stem_low - top) > 1e-6 or abs(stem_high - beam_bottom) > 1e-6:
        problems.append("the stationary fan stem no longer meets both the hub and beam underside")
    if any(part.shadows for part in rotor) or len(rotor) != 5:
        problems.append("the compact five-part rotor changed its no-shadow contract")
    return {"pivot_unity_m": [stable(value) for value in pivot], "full_sweep_radius_m": stable(radius),
            "rotor_top_y_m": stable(top), "rotor_bottom_y_m": stable(bottom),
            "minimum_beam_clearance_m": stable(min(clearances)) if clearances else 0.,
            "support_part": stem.name, "support_bottom_y_m": stable(stem_low),
            "support_top_y_m": stable(stem_high), "support_diameter_m": .018,
            "support_stationary": True, "rotor_casts_shadows": False}


def signature_for(
    asset: AssetBuild,
    design_id: str = DESIGN_ID,
    generator_version: str = INTERIOR_GENERATOR_VERSION,
) -> str:
    used_sheets = sorted({part.sheet for part in asset.parts})
    payload = {
        "design_id": design_id,
        "generator_version": generator_version,
        "sheet_pitch": {
            key: stable(SHEET_PITCH[key]) for key in used_sheets
        },
        "parts": [
            {
                "name": part.name,
                "role": part.role,
                "group": part.group,
                "sheet": part.sheet,
                "emissive": part.emissive,
                "shadows": part.shadows,
                "tint": part.tint,
                "colliders": part.colliders,
                "vertices": [
                    [stable(value) for value in vertex]
                    for vertex in part.geometry[0]
                ],
                "faces": [list(face) for face in part.geometry[1]],
            }
            for part in asset.parts
        ],
        "anchors": {
            name: anchor_signature(anchor)
            for name, anchor in sorted(asset.anchors.items())
        },
    }
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def manifest_part(part: Part) -> dict:
    entry = {
        "name": part.name,
        "role": part.role,
        "group": part.group,
        "sheet": part.sheet,
        "emissive": part.emissive,
        "shadows": part.shadows,
        "tint": part.tint,
        "colliders": [
            {
                "center": [stable(v) for v in center],
                "size": [stable(v) for v in size],
            }
            for center, size in part.colliders
        ],
        "vertices": len(part.geometry[0]),
        "triangles": kit.triangle_count(part.geometry),
    }
    if part.obj.get("bp_text") is not None:
        entry["text"] = str(part.obj["bp_text"])
    return entry


def write_manifest(
    asset: AssetBuild,
    path: Path,
    design_id: str = DESIGN_ID,
    *,
    generator_version: str = INTERIOR_GENERATOR_VERSION,
    display_name: str = DISPLAY_NAME,
    dimensions: Sequence[float] = (ROOM_WIDTH, ROOM_DEPTH, ROOM_HEIGHT),
    wall_thickness: float = WALL_THICKNESS,
    door_opening: Sequence[float] = (DOOR_WIDTH, DOOR_HEIGHT),
    unity_outward_axis: str = "-Z",
    activity_kinds: Sequence[str] = ACTIVITY_KINDS,
    district_moods: Sequence[str] = DISTRICT_MOODS,
) -> dict:
    merged = kit.merge_all([part.geometry for part in asset.parts])
    low, high = kit.bounds(merged)
    manifest = {
        "generator": "tools/build-bar-3d-model.py",
        "generator_version": generator_version,
        "blender_version": bpy.app.version_string,
        "design_id": design_id,
        "display_name": display_name,
        "dimensions_m": {
            "width": dimensions[0],
            "depth": dimensions[1],
            "height": dimensions[2],
        },
        "wall_thickness_m": wall_thickness,
        "door_opening_m": {
            "width": door_opening[0],
            "height": door_opening[1],
        },
        "blender_forward_axis": "-Y",
        "unity_entrance_outward_axis": unity_outward_axis,
        "runtime_wrapper_yaw_degrees": 0.0,
        "colliders": False,
        "lights": False,
        "cameras": False,
        "animation_count": 0,
        "activity_kinds": list(activity_kinds),
        "district_moods": list(district_moods),
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
        "mesh_count": len(asset.parts),
        "triangle_count": kit.triangle_count(merged),
        "anchors": [
            {
                "name": name,
                "role": anchor["bp_role"],
                "local_position": [stable(v) for v in anchor.location],
                "unity_right": [stable(v) for v in anchor.get(
                    "bp_unity_right", (1.0, 0.0, 0.0))],
                "unity_up": [stable(v) for v in anchor.get(
                    "bp_unity_up", (0.0, 1.0, 0.0))],
                "unity_forward": [stable(v) for v in anchor.get(
                    "bp_unity_forward", (0.0, 0.0, 1.0))],
            }
            for name, anchor in sorted(asset.anchors.items())
        ],
        "parts": [manifest_part(part) for part in asset.parts],
        "build_signature": signature_for(
            asset,
            design_id,
            generator_version),
    }
    if getattr(asset, "city_surface_contract", None) is not None:
        manifest["city_surface_contract"] = asset.city_surface_contract
    if getattr(asset, "interior_surface_contract", None) is not None:
        manifest["interior_surface_contract"] = asset.interior_surface_contract
    if getattr(asset, "containment_contract", None) is not None:
        manifest["containment_contract"] = asset.containment_contract
    if getattr(asset, "ceiling_fan_contract", None) is not None:
        manifest["ceiling_fan_contract"] = asset.ceiling_fan_contract
    if getattr(asset, "washroom_contract", None) is not None:
        manifest["washroom_contract"] = asset.washroom_contract
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(manifest, indent=2, sort_keys=False) + "\n",
        encoding="utf-8")
    return manifest


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
        add_leaf_bones=False,
        bake_anim=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_custom_props=True)


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blend", type=Path, default=DEFAULT_BLEND)
    parser.add_argument("--fbx", type=Path, default=DEFAULT_FBX)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--city-facade-surfaces", action="store_true",
                        help="Author only the City/Home facade's guarded UVs; protect interior/service outputs")
    parser.add_argument("--layout-only", action="store_true",
                        help="With --city-facade-surfaces, publish the chart contract without model outputs")
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    return parser.parse_args(argv)


def city_facade_surfaces(args) -> None:
    import city_bar_unique_surfaces as surfaces
    protected = (DEFAULT_BLEND, DEFAULT_FBX, DEFAULT_MANIFEST, SERVICE_FBX, SERVICE_MANIFEST,
                 Path(str(FACADE_FBX) + ".meta"), Path(str(FACADE_MANIFEST) + ".meta"))
    fingerprints = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in protected if path.exists()}
    # Build with the original naming context, but publish only the facade.
    # This preserves Blender's stable object/mesh names if another Bar part
    # ever shares a source name, without re-exporting the donor assets.
    _, _, facade = build()
    validate_facade(facade)
    previous = json.loads(FACADE_MANIFEST.read_text(encoding="utf8"))
    signature = signature_for(facade, exterior.DESIGN_ID, exterior.GENERATOR_VERSION)
    if previous["build_signature"] != signature:
        raise RuntimeError("City facade UV authoring must not change the accepted Bar geometry/anchors")
    surfaces.prepare(facade, write=not args.validate_only, apply=not args.layout_only)
    if not args.validate_only and not args.layout_only:
        surfaces.review_materials(facade)
        write_manifest(facade, FACADE_MANIFEST, exterior.DESIGN_ID,
            generator_version=exterior.GENERATOR_VERSION, display_name=exterior.DISPLAY_NAME,
            dimensions=(exterior.WIDTH, exterior.DEPTH, exterior.HEIGHT),
            wall_thickness=exterior.WALL_THICKNESS,
            door_opening=(exterior.DOOR_WIDTH, exterior.DOOR_HEIGHT), unity_outward_axis="+X",
            activity_kinds=(), district_moods=())
        export_fbx(facade, FACADE_FBX)
        # The editable source is City-owned; the shared interior/service blend
        # is protected just like its FBXs and manifests.
        for obj in list(bpy.data.objects):
            if obj.name not in facade.collection.objects: bpy.data.objects.remove(obj, do_unlink=True)
        surfaces.BLEND.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(surfaces.BLEND))
    if not args.layout_only:
        surfaces.verify_export(FACADE_FBX, facade)
    for path, checksum in fingerprints.items():
        if hashlib.sha256(path.read_bytes()).hexdigest() != checksum:
            raise RuntimeError("Facade-only authoring changed a protected donor: " + str(path))
    print("CITY BAR FACADE ONLY OK: original geometry/anchors and interior/service outputs retained.")


def main() -> None:
    args = parse_args(list(sys.argv))
    if args.city_facade_surfaces:
        city_facade_surfaces(args)
        return
    if args.layout_only:
        raise RuntimeError("--layout-only requires --city-facade-surfaces")
    interior, service_props, facade = build()
    validate(interior)
    validate_service_props(service_props)
    validate_facade(facade)
    import bar_interior_unique_surfaces as interior_surfaces
    interior_surfaces.prepare(interior, write=not args.validate_only)
    interior_surfaces.review_materials(interior)
    import city_bar_unique_surfaces as city_surfaces
    if city_surfaces.MANIFEST.exists():
        city_surfaces.prepare(facade, write=not args.validate_only)
        if not args.validate_only:
            # A geometry change repacks the facade's unique UVs. Publish the
            # matching pair in the same operation, preserving user import
            # settings and all unrelated City surface sheets.
            # Pillow is owned by the pinned project Python, not Blender's.
            publisher_code = "\n".join((
                "import importlib.util, pathlib, sys",
                "sys.path.insert(0, sys.argv[1])",
                "spec = importlib.util.spec_from_file_location('bar_city_surface_publish', pathlib.Path(sys.argv[1]) / 'build-city-surface-textures.py')",
                "publisher = importlib.util.module_from_spec(spec)",
                "spec.loader.exec_module(publisher)",
                "original = publisher.meta",
                "publisher.meta = lambda path, *a, **k: pathlib.Path(str(path) + '.meta').read_bytes().decode('utf8') if pathlib.Path(str(path) + '.meta').exists() else original(path, *a, **k)",
                "publisher.build(['CityBarFacade'])",
            ))
            subprocess.run(["python", "-B", "-c", publisher_code, str(ROOT / "tools")], cwd=ROOT, check=True)
            city_surfaces.review_materials(facade)
    total = kit.triangle_count(
        kit.merge_all([part.geometry for part in interior.parts]))
    facade_total = kit.triangle_count(
        kit.merge_all([part.geometry for part in facade.parts]))
    service_total = kit.triangle_count(
        kit.merge_all([part.geometry for part in service_props.parts]))
    if args.validate_only:
        print(
            f"Bar validates: interior {len(interior.parts)} parts / "
            f"{total} triangles, service props {len(service_props.parts)} "
            f"parts / {service_total} triangles, facade "
            f"{len(facade.parts)} parts / {facade_total} triangles.")
        return

    manifest = write_manifest(interior, args.manifest)
    export_fbx(interior, args.fbx)
    # Interior-only remodelling must not rewrite this shared passive pack:
    # FBX export embeds a timestamp and fresh IDs even for identical geometry.
    service_signature = signature_for(
        service_props, SERVICE_DESIGN_ID, SERVICE_GENERATOR_VERSION)
    preserve_service = False
    if SERVICE_MANIFEST.exists() and SERVICE_FBX.exists():
        try:
            previous_service = json.loads(
                SERVICE_MANIFEST.read_text(encoding="utf-8"))
            preserve_service = (
                previous_service.get("design_id") == SERVICE_DESIGN_ID and
                previous_service.get("build_signature") == service_signature)
        except (OSError, ValueError):
            preserve_service = False
    if preserve_service:
        service_manifest = previous_service
    else:
        service_manifest = write_manifest(
            service_props,
            SERVICE_MANIFEST,
            SERVICE_DESIGN_ID,
            generator_version=SERVICE_GENERATOR_VERSION,
            display_name=SERVICE_DISPLAY_NAME,
            dimensions=(SERVICE_MENU_COVER_WIDTH,
                        SERVICE_MENU_COVER_DEPTH,
                        2.0),
            wall_thickness=0.0,
            door_opening=(0.0, 0.0),
            activity_kinds=(),
            district_moods=())
        export_fbx(service_props, SERVICE_FBX)

    facade_signature = signature_for(
        facade, exterior.DESIGN_ID, exterior.GENERATOR_VERSION)
    preserve_facade = False
    if FACADE_MANIFEST.exists() and FACADE_FBX.exists():
        try:
            previous_facade = json.loads(
                FACADE_MANIFEST.read_text(encoding="utf-8"))
            preserve_facade = (
                previous_facade.get("design_id") == exterior.DESIGN_ID and
                previous_facade.get("build_signature") == facade_signature)
        except (OSError, ValueError):
            preserve_facade = False
    if preserve_facade:
        facade_manifest = previous_facade
    else:
        facade_manifest = write_manifest(
            facade,
            FACADE_MANIFEST,
            exterior.DESIGN_ID,
            generator_version=exterior.GENERATOR_VERSION,
            display_name=exterior.DISPLAY_NAME,
            dimensions=(exterior.WIDTH, exterior.DEPTH, exterior.HEIGHT),
            wall_thickness=exterior.WALL_THICKNESS,
            door_opening=(exterior.DOOR_WIDTH, exterior.DOOR_HEIGHT),
            unity_outward_axis="+X",
            activity_kinds=(),
            district_moods=())
        export_fbx(facade, FACADE_FBX)
    args.blend.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.blend))
    print(
        f"Bar interior written: {manifest['mesh_count']} parts, "
        f"{manifest['triangle_count']} triangles, "
        f"signature {manifest['build_signature'][:12]}.")
    print(
        f"Bar service props {'preserved' if preserve_service else 'written'}: "
        f"{service_manifest['mesh_count']} parts, "
        f"{service_manifest['triangle_count']} triangles, "
        f"signature {service_manifest['build_signature'][:12]}.")
    print(
        f"Bar exterior {'preserved' if preserve_facade else 'written'}: "
        f"{facade_manifest['mesh_count']} parts, "
        f"{facade_manifest['triangle_count']} triangles, "
        f"signature {facade_manifest['build_signature'][:12]}.")


if __name__ == "__main__":
    main()
