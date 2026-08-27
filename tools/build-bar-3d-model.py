#!/usr/bin/env python3
"""Build the bar interior and its complete old-pub exterior.

Every visible thing in the bar - the shell, the counter, the backbar and
its bottles, the booths, the stage, the tables and stools, the dressing,
the four activity sets, the four district variants, the jukebox, the fan
and the practical lights - is authored here.  Before this the room was
`89` `RuntimePrimitiveFactory` calls composed at runtime.

The room is deliberately NOT re-designed.  Every dimension is the one
`BarInteriorLayoutPlanner` publishes or the one the primitive it replaces
used, and the manifest records them so an EditMode test can prove the two
still agree.  What changes is only what the geometry is made of: edges
are relieved, cups and shades taper, the doorway has reveals, and the
floor meets the wall at a skirting instead of at one black line.

Run with Blender 5 from the repository root::

    blender --background --factory-startup --python \
      tools/build-bar-3d-model.py

    blender --background --factory-startup --python \
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
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

try:
    import bpy
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import interior_kit as kit  # noqa: E402  (after the sys.path fix)
import bar_parts as bp  # noqa: E402
import bar_exterior as exterior  # noqa: E402

INTERIOR_GENERATOR_VERSION = "2.0.0"
DESIGN_ID = "bar_interior_v2"
DISPLAY_NAME = "Bar Promenade Bar Interior"

# --- the layout contract, mirrored from BarInteriorLayoutPlanner --------
ROOM_WIDTH = 22.0
ROOM_DEPTH = 16.0
ROOM_HEIGHT = 4.8
WALL_THICKNESS = 0.3
DOOR_WIDTH = 3.2

FLOOR_THICKNESS = 0.24
CEILING_THICKNESS = 0.20

#: `plan.CounterPosition` / `plan.CounterSize`.
COUNTER_POS = (0.0, 0.7, 5.75)
COUNTER_SIZE = (11.2, 1.4, 1.0)
COUNTER_STATION = (-1.15, 0.9, 4.75)
ACTIVITY_STATION = (5.1, 0.9, -1.55)

#: Furniture footprints, from `BarInteriorLayoutPlanner.CreateFurniture`.
#: Rects are (xMin, yMin, width, height) in the plan's XZ.
BACKBAR_RECT = (-5.55, 7.125, 11.1, 0.55)
BACKBAR_HEIGHT = 1.44
STAGE_RECT = (-10.0, 4.75, 3.8, 2.35)
STAGE_HEIGHT = 0.32
BOOTH_ZS = (-3.9, -0.35, 3.15)
BOOTH_RECT_WIDTH = 3.07
BOOTH_RECT_HEIGHT = 2.3
BOOTH_RECT_X = -10.66
HIGH_TOPS = ((-3.5, -3.65), (3.5, -3.65), (-3.5, 2.5), (3.5, 2.5))
COAT_RACK = (-8.85, -6.45)

ACTIVITY_RECTS = {
    "BeerPong": (5.825, -1.545, 2.35, 4.25),
    "Cocktail": (5.825, -0.075, 2.65, 1.05),
    "SplitTheG": (5.825, -0.075, 2.65, 1.05),
    "TinctureMatch": (5.825, -0.075, 2.65, 1.05),
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
    "ExteriorBrick": (0.30, 0.12, 0.075, 1.0),
    "ExteriorPlaster": (0.48, 0.44, 0.34, 1.0),
    "CityRoof": (0.095, 0.105, 0.115, 1.0),
    "": (0.34, 0.29, 0.13, 1.0),
    "emissive": (1.0, 0.62, 0.28, 1.0),
}


def rect_center(rect: Sequence[float]) -> tuple[float, float]:
    return rect[0] + rect[2] * 0.5, rect[1] + rect[3] * 0.5


def rect_max(rect: Sequence[float]) -> tuple[float, float]:
    return rect[0] + rect[2], rect[1] + rect[3]


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


# ------------------------------------------------------------ scene ---


def create_material(key: str) -> "bpy.types.Material":
    material = bpy.data.materials.new(f"PREVIEW_Bar_{key or 'Flat'}")
    color = PREVIEW_COLORS[key]
    material.diffuse_color = color
    material.roughness = 0.3 if key == "emissive" else 0.78
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


def add_anchor(
    asset: AssetBuild,
    name: str,
    role: str,
    unity_position: Sequence[float],
) -> None:
    anchor = bpy.data.objects.new(f"ANCHOR_{name}", None)
    asset.collection.objects.link(anchor)
    anchor.parent = asset.root
    anchor.empty_display_type = "ARROWS"
    anchor.empty_display_size = 0.5
    x, y, z = unity_position
    anchor.location = (float(x), float(z), float(y))
    anchor["bp_role"] = role
    asset.anchors[name] = anchor


# ------------------------------------------------------- the shell ----


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
        front_openings=[kit.Opening(0.0, DOOR_WIDTH, ROOM_HEIGHT)])
    segment = (ROOM_WIDTH - DOOR_WIDTH) * 0.5
    offset = DOOR_WIDTH * 0.5 + segment * 0.5
    #  The front wall is ONE mesh with the doorway cut through it, so
    #  it takes a collider per pier rather than one across the opening -
    #  a single box here would be an invisible door.
    wall_colliders = {
        "Front Wall": [
            ((-offset, ROOM_HEIGHT * 0.5, -half_depth),
             (segment, ROOM_HEIGHT, WALL_THICKNESS)),
            ((offset, ROOM_HEIGHT * 0.5, -half_depth),
             (segment, ROOM_HEIGHT, WALL_THICKNESS)),
        ],
        "Back Wall": [((0.0, ROOM_HEIGHT * 0.5, half_depth),
                       (ROOM_WIDTH, ROOM_HEIGHT, WALL_THICKNESS))],
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
        kit.skirting(ROOM_WIDTH, ROOM_DEPTH, wall_thickness=WALL_THICKNESS),
        "skirting", bp.ident("DarkWoodTint"), sheet="DarkWood",
        shadows=False, unity_space=False)

    for label, sign in (("Left", -1.0), ("Right", 1.0)):
        add_part(
            asset, materials, f"Entrance {label} Post",
            bp.u_box((sign * 1.72, 2.25, -half_depth + 0.04),
                     (0.28, 4.5, 0.42)),
            "entrance_frame", bp.ident("MetalTint"))
    add_part(
        asset, materials, "Entrance Lintel",
        bp.u_box((0.0, 4.20, -half_depth + 0.04), (3.7, 0.30, 0.42)),
        "entrance_frame", bp.ident("MetalTint"))

    wainscot = [
        bp.u_box((0.0, 0.82, half_depth - 0.19),
                 (ROOM_WIDTH - 0.55, 1.58, 0.10)),
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
        bp.u_box((0.0, 1.64, half_depth - 0.26),
                 (ROOM_WIDTH - 0.40, 0.10, 0.12), 0.006),
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
        for x in (-9.0, -6.0, -3.0, 0.0, 3.0, 6.0, 9.0)
    ]
    add_part(
        asset, materials, "Ceiling Cross Beams", kit.merge_all(cross),
        "beam", bp.ident("DarkWoodTint"), sheet="DarkWood", shadows=False)

    long_beams = [
        bp.u_box((x, ROOM_HEIGHT - 0.28, 0.0),
                 (0.32, 0.50, ROOM_DEPTH - 0.30))
        for x in (-5.4, 5.4)
    ]
    add_part(
        asset, materials, "Ceiling Long Beams", kit.merge_all(long_beams),
        "beam", bp.ident("DarkWoodTint"), sheet="DarkWood", shadows=False)


# ---------------------------------------------------- fixed furniture -


def build_counter(asset: AssetBuild, materials: dict) -> None:
    cx, cy, cz = COUNTER_POS
    sx, sy, sz = COUNTER_SIZE

    add_part(
        asset, materials, "Bar Counter",
        kit.translated(
            kit.counter_run(sx, sz, sy, top_thickness=0.06, nosing=0.02),
            (cx, cz, cy - sy * 0.5)),
        "counter", bp.ident("CounterWoodTint"), sheet="DarkWood",
        colliders=[((cx, cy, cz), (sx, sy, sz))], unity_space=False)
    add_part(
        asset, materials, "Counter Top",
        bp.u_box((cx, cy + sy * 0.5 + 0.08, cz),
                 (sx + 0.45, 0.16, sz + 0.32), 0.02),
        "counter_top", bp.ident("MetalTint"))
    add_part(
        asset, materials, "Counter Foot Rail",
        bp.u_box((cx, cy - sy * 0.29, cz - sz * 0.62),
                 (sx - 0.45, 0.10, 0.10), 0.008),
        "counter_rail", bp.ident("MetalTint"))

    panels = []
    panel_width = (sx - 0.65) / 7.0
    for index in range(7):
        x = -sx * 0.5 + 0.33 + panel_width * (index + 0.5)
        panels.append(bp.u_box(
            (cx + x, cy, cz - sz * 0.51),
            (panel_width - 0.11, sy - 0.20, 0.08), 0.006))
    add_part(
        asset, materials, "Counter Front Panels", kit.merge_all(panels),
        "counter_panel", bp.ident("WoodTint"), sheet="DarkWood",
        shadows=False)

    #  The stool beside the counter station is left out, exactly as the
    #  loop that built these left it out: the bartender serves across
    #  that gap and a stool in it blocks the transaction.
    stool_z = cz - sz * 0.5 - 0.72
    for index, x in enumerate((-4.25, -2.55, -0.85, 0.85, 2.55, 4.25)):
        dx = x - COUNTER_STATION[0]
        dz = stool_z - COUNTER_STATION[2]
        if dx * dx + dz * dz < 1.35 * 1.35:
            continue

        name = f"Bar Stool {index + 1}"
        add_part(
            asset, materials, f"{name} Leg",
            bp.u_cylinder((x, 0.42, stool_z), (0.12, 0.42, 0.12)),
            "stool_leg", bp.rgb(0.075, 0.024, 0.017), sheet="DarkWood")
        add_part(
            asset, materials, name,
            bp.u_tapered_cylinder((x, 0.87, stool_z), (0.48, 0.09, 0.48),
                                  0.94),
            "stool_seat", bp.rgb(0.30, 0.035, 0.045), sheet="WornLeather")

    for index in range(5):
        x = -2.2 + index * 1.1
        add_part(
            asset, materials, f"Beer Tap Stem {index + 1}",
            bp.u_cylinder((x, cy + sy * 0.5 + 0.34, cz),
                          (0.08, 0.26, 0.08)),
            "tap_stem", bp.ident("MetalTint"))
        add_part(
            asset, materials, f"Beer Tap Handle {index + 1}",
            bp.u_tapered_cylinder((x, cy + sy * 0.5 + 0.63, cz),
                                  (0.13, 0.15, 0.13), 0.72),
            "tap_handle",
            bp.ident("UpholsteryTint") if index % 2 == 0
            else bp.ident("GlassTint"))


def build_backbar(asset: AssetBuild, materials: dict) -> None:
    center_x, center_z = rect_center(BACKBAR_RECT)
    back_z = rect_max(BACKBAR_RECT)[1] - 0.025

    add_part(
        asset, materials, "Backbar Cabinet",
        bp.u_box((center_x, BACKBAR_HEIGHT * 0.5, center_z),
                 (BACKBAR_RECT[2], BACKBAR_HEIGHT, BACKBAR_RECT[3])),
        "backbar", bp.ident("CounterWoodTint"), sheet="DarkWood",
        colliders=[((center_x, BACKBAR_HEIGHT * 0.5, center_z), (BACKBAR_RECT[2], BACKBAR_HEIGHT, BACKBAR_RECT[3]))])

    mirrors = [
        bp.u_box((-4.25 + index * 2.125, 2.72, back_z - 0.04),
                 (1.82, 2.55, 0.055), 0.01)
        for index in range(5)
    ]
    add_part(
        asset, materials, "Backbar Mirror Panels", kit.merge_all(mirrors),
        "backbar_mirror", bp.ident("GlassTint"), shadows=False)

    shelves = [
        bp.u_box((0.0, 1.62 + row * 0.72, back_z - 0.12),
                 (10.7, 0.10, 0.36), 0.008)
        for row in range(3)
    ]
    shelves += [
        bp.u_box((-5.15 + column * 2.06, 2.52, back_z - 0.13),
                 (0.09, 2.58, 0.34), 0.008)
        for column in range(6)
    ]
    add_part(
        asset, materials, "Backbar Shelves", kit.merge_all(shelves),
        "backbar_shelf", bp.ident("MetalTint"), shadows=False)

    build_bottles(asset, materials, back_z - 0.28)

    add_part(
        asset, materials, "Backbar Crown",
        bp.u_box((0.0, 4.14, back_z - 0.12), (11.35, 0.34, 0.42)),
        "backbar_crown", bp.ident("DarkWoodTint"), sheet="DarkWood")
    add_part(
        asset, materials, "Backbar Amber Sign",
        bp.u_box((0.0, 4.12, back_z - 0.35), (4.6, 0.18, 0.08), 0.01),
        "sign_glow", bp.ident("SignGlowColor"),
        emissive=True, shadows=False)


def build_bottles(asset: AssetBuild, materials: dict, z: float) -> None:
    colors = (
        bp.rgb(0.72, 0.22, 0.07),
        bp.rgb(0.12, 0.38, 0.25),
        bp.rgb(0.46, 0.15, 0.40),
        bp.rgb(0.72, 0.62, 0.32),
    )
    batches: list[list[kit.Geometry]] = [[] for _ in colors]
    for row in range(3):
        for column in range(18):
            x = -4.85 + column * 0.57
            #  The lower central shelf stays empty: the drink service
            #  puts nine individually selectable retail bottles there.
            if row == 0 and -4.35 <= x <= 2.0:
                continue

            height = 0.25 + ((column * 7 + row * 3) % 4) * 0.045
            #  A bottle, not a block: the neck is the whole silhouette
            #  and the primitives could not have one.
            body = bp.u_box((x, 1.83 + row * 0.72 - height * 0.16, z),
                            (0.15, height * 0.68, 0.14), 0.008)
            neck = bp.u_tapered_cylinder(
                (x, 1.83 + row * 0.72 + height * 0.38, z),
                (0.075, height * 0.18, 0.07), 0.62)
            batches[(column + row * 2) % len(colors)].append(
                kit.merge(body, neck))

    for index, batch in enumerate(batches):
        add_part(
            asset, materials, f"Bottle Silhouettes {index + 1}",
            kit.merge_all(batch), "bottle", colors[index], shadows=False)


def build_booths(asset: AssetBuild, materials: dict) -> None:
    for booth_index, center_z in enumerate(BOOTH_ZS, start=1):
        rect = (BOOTH_RECT_X, center_z - 1.15,
                BOOTH_RECT_WIDTH, BOOTH_RECT_HEIGHT)
        z = rect_center(rect)[1]
        base_x = -9.86
        table_x = rect_max(rect)[0] - 0.59
        back_x = -10.32
        depth = rect[3] - 0.02

        add_part(
            asset, materials, f"Booth Base {booth_index}",
            bp.u_box((base_x, 0.20, z), (0.78, 0.40, depth)),
            "booth_base", bp.ident("CounterWoodTint"), sheet="DarkWood",
            colliders=[((base_x, 0.20, z), (0.78, 0.40, depth))])
        add_part(
            asset, materials, f"Booth Cushion {booth_index}",
            bp.u_box((base_x + 0.02, 0.435, z),
                     (0.76, 0.09, depth - 0.10), 0.022),
            "booth_cushion", bp.ident("UpholsteryTint"),
            sheet="WornLeather")
        add_part(
            asset, materials, f"Booth Back {booth_index}",
            bp.u_box((back_x, 0.90, z), (0.18, 0.95, rect[3]), 0.02),
            "booth_back", bp.ident("UpholsteryTint"), sheet="WornLeather",
            colliders=[((back_x, 0.90, z), (0.18, 0.95, rect[3]))])
        add_part(
            asset, materials, f"Booth Table Top {booth_index}",
            bp.u_box((table_x, 0.88, z), (1.18, 0.12, 1.48), 0.018),
            "booth_table", bp.ident("MetalTint"),
            colliders=[((table_x, 0.88, z), (1.18, 0.12, 1.48))])
        add_part(
            asset, materials, f"Booth Table Leg {booth_index}",
            bp.u_cylinder((table_x, 0.43, z), (0.18, 0.43, 0.18)),
            "booth_table_leg", bp.ident("DarkWoodTint"), sheet="DarkWood",
            colliders=[((table_x, 0.43, z), (0.18, 0.86, 0.18))])


def build_stage(asset: AssetBuild, materials: dict) -> None:
    center_x, center_z = rect_center(STAGE_RECT)
    x_min, x_max = STAGE_RECT[0], rect_max(STAGE_RECT)[0]
    curtain_z = rect_max(STAGE_RECT)[1] + 0.24

    add_part(
        asset, materials, "Small Stage",
        bp.u_box((center_x, STAGE_HEIGHT * 0.5, center_z),
                 (STAGE_RECT[2], STAGE_HEIGHT, STAGE_RECT[3]), 0.016),
        "stage", bp.ident("CounterWoodTint"), sheet="WornPlank",
        colliders=[((center_x, STAGE_HEIGHT * 0.5, center_z), (STAGE_RECT[2], STAGE_HEIGHT, STAGE_RECT[3]))])

    #  A curtain hangs in folds. Three tapering slabs cost eight
    #  triangles more than the box did and stop it reading as a plank.
    for label, x in (("Left", x_min + 0.16), ("Right", x_max - 0.16)):
        folds = [
            bp.u_box((x + offset * 0.11, 2.55, curtain_z + offset * 0.05),
                     (0.16, 4.25, 0.30), 0.012)
            for offset in (-1, 0, 1)
        ]
        add_part(
            asset, materials, f"Stage {label} Curtain",
            kit.merge_all(folds), "stage_curtain",
            bp.ident("UpholsteryTint"), sheet="WornLeather")

    add_part(
        asset, materials, "Stage Valance",
        bp.u_box((center_x, 4.34, curtain_z),
                 (STAGE_RECT[2] + 0.12, 0.62, 0.35), 0.014),
        "stage_curtain", bp.ident("UpholsteryTint"), sheet="WornLeather")

    for label, x in (("Left", x_min + 0.55), ("Right", x_max - 0.55)):
        add_part(
            asset, materials, f"Stage {label} Speaker",
            bp.u_box((x, 0.92, center_z + 0.42), (0.72, 1.45, 0.62)),
            "stage_speaker", bp.rgb(0.035, 0.035, 0.04),
            colliders=[((x, 0.92, center_z + 0.42), (0.72, 1.45, 0.62))])

    add_part(
        asset, materials, "Stage Microphone Stand",
        bp.u_cylinder((center_x, 0.95, center_z - 0.55),
                      (0.055, 0.80, 0.055)),
        "stage_mic", bp.ident("MetalTint"))
    add_part(
        asset, materials, "Stage Microphone",
        bp.u_tapered_cylinder((center_x, 1.78, center_z - 0.60),
                              (0.12, 0.11, 0.12), 0.68),
        "stage_mic", bp.rgb(0.06, 0.06, 0.065))


def build_tables_and_bay(asset: AssetBuild, materials: dict) -> None:
    add_part(
        asset, materials, "Activity Bay Rug",
        bp.u_box((7.00, 0.018, 0.55), (6.15, 0.035, 5.65), 0.006),
        "bay_rug", bp.rgb(0.12, 0.11, 0.20), shadows=False)

    border = [
        bp.u_box((7.0, 0.045, -2.28), (6.22, 0.06, 0.08), 0.006),
        bp.u_box((7.0, 0.045, 3.38), (6.22, 0.06, 0.08), 0.006),
        bp.u_box((3.92, 0.045, 0.55), (0.08, 0.06, 5.58), 0.006),
        bp.u_box((10.08, 0.045, 0.55), (0.08, 0.06, 5.58), 0.006),
    ]
    add_part(
        asset, materials, "Activity Bay Border", kit.merge_all(border),
        "bay_border", bp.rgb(0.86, 0.46, 0.14), shadows=False)

    for index, (x, z) in enumerate(HIGH_TOPS, start=1):
        name = f"Social High Table {index}"
        add_part(
            asset, materials, f"{name} Leg",
            bp.u_cylinder((x, 0.47, z), (0.17, 0.47, 0.17)),
            "table_leg", bp.rgb(0.075, 0.024, 0.017), sheet="DarkWood",
            colliders=[((x, 0.47, z), (0.17, 0.94, 0.17))])
        add_part(
            asset, materials, name,
            bp.u_cylinder((x, 0.98, z), (0.90, 0.08, 0.90), sides=12),
            "table_top", bp.rgb(0.86, 0.46, 0.14),
            colliders=[((x, 0.98, z), (0.90, 0.16, 0.90))])


def build_dressing(asset: AssetBuild, materials: dict) -> None:
    rack_x, rack_z = COAT_RACK
    add_part(
        asset, materials, "Coat Rack",
        bp.u_cylinder((rack_x, 0.92, rack_z), (0.12, 0.92, 0.12)),
        "coat_rack", bp.ident("MetalTint"),
        colliders=[((rack_x, 0.92, rack_z), (0.12, 1.84, 0.12))])
    for index in range(4):
        add_part(
            asset, materials, f"Coat Rack Hook {index + 1}",
            kit.translated(
                bp.u_rotated(
                    bp.u_box((0.0, 0.0, 0.0), (0.52, 0.08, 0.08), 0.008),
                    (0.0, index * 45.0, 18.0)),
                (rack_x, 1.70, rack_z)),
            "coat_hook", bp.ident("MetalTint"))

    add_part(
        asset, materials, "Service Door",
        bp.u_box((9.65, 1.25, 7.76), (1.65, 2.50, 0.12), 0.012),
        "service_door", bp.ident("GlassTint"))
    add_part(
        asset, materials, "Service Door Frame",
        bp.u_box((9.65, 2.57, 7.70), (1.92, 0.14, 0.20), 0.01),
        "service_door_frame", bp.ident("MetalTint"))

    posters = (
        ("Burgundy Poster", (10.78, 2.55, -4.30),
         bp.lerp(bp.ident("WallTint"), bp.ident("SignAccentColor"), 0.68)),
        ("Teal Poster", (10.78, 2.55, 4.25), bp.ident("GlassTint", 1.35)),
        ("Entrance Notice", (-10.78, 2.45, -6.20),
         bp.ident("SignAccentColor")),
    )
    for name, position, tint in posters:
        x, y, z = position
        add_part(
            asset, materials, f"{name} Frame",
            bp.u_box((x, y, z), (0.08, 1.72, 1.20), 0.008),
            "poster_frame", bp.rgb(0.86, 0.46, 0.14), shadows=False)
        add_part(
            asset, materials, name,
            bp.u_plate((x - 0.055, y, z), (0.06, 1.50, 0.98)),
            "poster", tint, shadows=False)


def build_ceiling_fan(asset: AssetBuild, materials: dict) -> None:
    group = "pivot:Slow Ceiling Fan"
    add_part(
        asset, materials, "Fan Hub",
        bp.u_cylinder((0.0, 0.0, 0.0), (0.28, 0.10, 0.28)),
        "fan_hub", bp.ident("MetalTint"), group=group, shadows=False)
    for index in range(4):
        #  Each blade is placed by rotating its own offset, exactly as
        #  the runtime did; a blade rotated in place would sit inside
        #  the hub.
        blade = bp.u_box((1.10, -0.05, 0.0), (1.75, 0.08, 0.34), 0.01)
        add_part(
            asset, materials, f"Fan Blade {index + 1}",
            bp.u_rotated(blade, (0.0, index * 90.0, 0.0)),
            "fan_blade", bp.ident("DarkWoodTint"), sheet="DarkWood",
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


def wall_cards(y: float, count: int, spacing: float,
               height: float, width: float) -> list[kit.Geometry]:
    center = (count - 1) * 0.5
    return [
        bp.u_plate((10.69, y, (index - center) * spacing),
                   (0.05, height, width))
        for index in range(count)
    ]


def build_districts(asset: AssetBuild, materials: dict) -> None:
    memory = "district:Memory"
    add_part(
        asset, materials, "Old Town Ledger Field",
        bp.u_plate((10.76, 2.68, 0.0), (0.06, 2.32, 4.80)),
        "district_field",
        bp.lerp(bp.ident("WallTint"), bp.rgb(0.48, 0.31, 0.15), 0.46),
        group=memory, shadows=False)
    add_part(
        asset, materials, "Old Town Missing Portraits",
        kit.merge_all(wall_cards(2.88, 3, 1.22, 0.72, 0.62)),
        "district_cards", bp.ident("SignAccentColor"),
        group=memory, shadows=False)

    household = "district:Household"
    add_part(
        asset, materials, "Residential Curtain Field",
        bp.u_plate((10.76, 2.68, 0.0), (0.06, 2.35, 4.80)),
        "district_field", bp.ident("UpholsteryTint"),
        group=household, sheet="WornLeather", shadows=False)
    add_part(
        asset, materials, "Residential Curtain Pleats",
        kit.merge_all(wall_cards(2.68, 7, 0.64, 2.18, 0.14)),
        "district_cards", bp.ident("WallPanelTint"),
        group=household, shadows=False)

    shift = "district:AfterShift"
    add_part(
        asset, materials, "Industrial Safety Band",
        bp.u_plate((10.76, 1.82, 0.0), (0.06, 0.34, 6.25)),
        "district_field", bp.ident("SignAccentColor"),
        group=shift, shadows=False)
    add_part(
        asset, materials, "Industrial Utility Pipes",
        kit.merge_all([
            bp.u_cylinder((10.69, 2.78, (index - 1.5) * 1.28),
                          (0.16, 1.60, 0.16))
            for index in range(4)
        ]),
        "district_cards", bp.ident("MetalTint"),
        group=shift, shadows=False)

    night = "district:Escape"
    for label, z, pitch, tint in (
        ("Cyan", -1.05, 24.0, bp.ident("PendantColor", 2.8)),
        ("Magenta", 1.05, -24.0, bp.ident("SignAccentColor", 2.8)),
    ):
        add_part(
            asset, materials, f"Nightlife Neon {label}",
            kit.translated(
                bp.u_rotated(
                    bp.u_box((0.0, 0.0, 0.0), (0.06, 0.16, 2.65), 0.008),
                    (pitch, 0.0, 0.0)),
                (10.73, 2.72, z)),
            "neon", tint, group=night, emissive=True, shadows=False)


def activity_console(
    asset: AssetBuild,
    materials: dict,
    group: str,
    name: str,
    accent: dict,
    rect: Sequence[float],
) -> tuple[float, float, float]:
    center_x, center_z = rect_center(rect)
    center = (center_x, 0.64, center_z)
    add_part(
        asset, materials, name,
        bp.u_box(center, (rect[2], 1.20, rect[3])),
        "activity_console", bp.rgb(0.075, 0.024, 0.017),
        group=group, sheet="DarkWood",
        colliders=[(center, (rect[2], 1.20, rect[3]))])
    add_part(
        asset, materials, f"{name} Top",
        bp.u_box((center_x, center[1] + 0.66, center_z),
                 (2.85, 0.12, 1.22), 0.016),
        "activity_console_top", bp.rgb(0.86, 0.46, 0.14), group=group)
    add_part(
        asset, materials, f"{name} Accent",
        bp.u_plate((center_x, center[1], center_z - 0.56),
                   (2.20, 0.52, 0.06)),
        "activity_accent", accent, group=group,
        emissive=True, shadows=False)
    return center_x, center[1] + 0.78, center_z


def build_activities(asset: AssetBuild, materials: dict) -> None:
    # --- Beer pong ------------------------------------------------
    group = "activity:BeerPong"
    rect = ACTIVITY_RECTS["BeerPong"]
    center_x, center_z = rect_center(rect)
    center = (center_x, 0.92, center_z)
    add_part(
        asset, materials, "Beer Pong Table",
        bp.u_box(center, (rect[2], 0.14, rect[3]), 0.014),
        "activity_table", bp.rgb(0.055, 0.26, 0.29), group=group,
        colliders=[(center, (rect[2], 0.14, rect[3]))])
    leg_x = rect[2] * 0.5 - 0.275
    leg_z = rect[3] * 0.5 - 0.445
    for index, (dx, dz) in enumerate(
            ((-leg_x, -leg_z), (leg_x, -leg_z),
             (-leg_x, leg_z), (leg_x, leg_z)), start=1):
        leg_center = (center_x + dx, 0.92 - 0.49, center_z + dz)
        add_part(
            asset, materials, f"Beer Pong Table Leg {index}",
            bp.u_box(leg_center, (0.16, 0.86, 0.16), 0.01),
            "activity_table_leg", bp.rgb(0.075, 0.024, 0.017),
            group=group, sheet="DarkWood",
            colliders=[(leg_center, (0.16, 0.86, 0.16))])
    add_part(
        asset, materials, "Beer Pong Center Line",
        bp.u_plate((center_x, 1.00, center_z),
                   (rect[2] - 0.25, 0.025, 0.06)),
        "activity_line", bp.rgb(0.86, 0.46, 0.14), group=group,
        shadows=False)
    for index, (dx, dz) in enumerate((
            (0.0, 1.02), (-0.27, 1.32), (0.27, 1.32),
            (-0.54, 1.62), (0.0, 1.62), (0.54, 1.62)), start=1):
        add_part(
            asset, materials, f"Beer Pong Cup {index}",
            #  A cup tapers. This is the shape the primitive could not
            #  make and the reason six of them read as cups now.
            bp.u_tapered_cylinder(
                (center_x + dx, 1.15, center_z + dz),
                (0.22, 0.16, 0.22), 0.74),
            "activity_cup", bp.rgb(0.82, 0.12, 0.10), group=group,
            shadows=False)

    # --- Cocktail -------------------------------------------------
    group = "activity:Cocktail"
    x, y, z = activity_console(
        asset, materials, group, "Cocktail Service Cart",
        bp.rgb(0.10, 0.24, 0.22), ACTIVITY_RECTS["Cocktail"])
    add_part(
        asset, materials, "Cocktail Shaker",
        bp.u_tapered_cylinder((x, y, z), (0.20, 0.31, 0.20), 0.62),
        "activity_prop", bp.rgb(0.64, 0.68, 0.66), group=group)
    add_part(
        asset, materials, "Cocktail Glass",
        bp.u_tapered_cylinder((x + 0.55, y - 0.04, z),
                              (0.24, 0.25, 0.24), 1.55),
        "activity_prop", bp.rgb(0.24, 0.58, 0.62), group=group)

    # --- Split the G ----------------------------------------------
    group = "activity:SplitTheG"
    x, y, z = activity_console(
        asset, materials, group, "Split the G Tap Cart",
        bp.rgb(0.10, 0.18, 0.13), ACTIVITY_RECTS["SplitTheG"])
    add_part(
        asset, materials, "Split the G Coaster",
        bp.u_cylinder((x, y, z), (0.38, 0.035, 0.38)),
        "activity_prop", bp.rgb(0.075, 0.024, 0.017), group=group,
        sheet="DarkWood")
    add_part(
        asset, materials, "Split the G Pint",
        bp.u_tapered_cylinder((x, y + 0.30, z), (0.25, 0.30, 0.25), 1.18),
        "activity_prop", bp.rgb(0.36, 0.16, 0.055), group=group)
    add_part(
        asset, materials, "Split the G Foam",
        bp.u_cylinder((x, y + 0.61, z), (0.295, 0.045, 0.295)),
        "activity_prop", bp.rgb(0.94, 0.83, 0.61), group=group)
    add_part(
        asset, materials, "Split the G Target",
        bp.u_plate((x, y + 0.32, z - 0.26), (0.31, 0.045, 0.025)),
        "activity_prop", bp.rgb(0.86, 0.46, 0.14), group=group,
        shadows=False)

    # --- Tincture match -------------------------------------------
    group = "activity:TinctureMatch"
    x, y, z = activity_console(
        asset, materials, group, "Tincture Apothecary Cart",
        bp.rgb(0.16, 0.08, 0.22), ACTIVITY_RECTS["TinctureMatch"])
    add_part(
        asset, materials, "Tincture Match Tray",
        bp.u_box((x, y, z), (2.15, 0.08, 0.62), 0.01),
        "activity_prop", bp.rgb(0.075, 0.024, 0.017), group=group,
        sheet="DarkWood")
    shot_colors = (
        bp.rgb(0.66, 0.08, 0.10), bp.rgb(0.94, 0.44, 0.08),
        bp.rgb(0.20, 0.12, 0.48), bp.rgb(0.13, 0.48, 0.24),
        bp.rgb(0.74, 0.57, 0.20),
    )
    for index, tint in enumerate(shot_colors, start=1):
        add_part(
            asset, materials, f"Tincture Shot {index}",
            bp.u_tapered_cylinder(
                (x - 0.76 + (index - 1) * 0.38, y + 0.18, z),
                (0.22, 0.16, 0.22), 1.22),
            "activity_prop", tint, group=group, shadows=False)

    bottle = (x + 1.55, y + 0.29, z)
    add_part(
        asset, materials, "Tincture XXX Bottle",
        bp.u_tapered_cylinder(bottle, (0.34, 0.34, 0.34), 0.52),
        "activity_prop", bp.rgb(0.70, 0.82, 0.78), group=group)
    add_part(
        asset, materials, "Tincture XXX Bottle Neck",
        bp.u_cylinder((bottle[0], bottle[1] + 0.42, bottle[2]),
                      (0.16, 0.12, 0.16)),
        "activity_prop", bp.rgb(0.70, 0.82, 0.78), group=group)

    sign = (bottle[0], bottle[1] + 0.02, bottle[2] - 0.22)
    add_part(
        asset, materials, "Tincture XXX Sign",
        bp.u_plate(sign, (0.74, 0.38, 0.035)),
        "activity_prop", bp.rgb(0.86, 0.46, 0.14), group=group,
        shadows=False)
    marks = []
    for x_index in range(3):
        mark_x = sign[0] - 0.22 + x_index * 0.22
        for stroke in range(2):
            marks.append(kit.translated(
                bp.u_rotated(
                    bp.u_plate((0.0, 0.0, 0.0), (0.055, 0.29, 0.025)),
                    (0.0, 0.0, 38.0 if stroke == 0 else -38.0)),
                (mark_x, sign[1], sign[2] - 0.03)))
    add_part(
        asset, materials, "Tincture XXX Marks", kit.merge_all(marks),
        "activity_prop", bp.rgb(0.16, 0.08, 0.04), group=group,
        shadows=False)


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


def build() -> tuple[AssetBuild, AssetBuild]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials = {key: create_material(key) for key in PREVIEW_COLORS}
    return build_interior(materials), build_facade(materials)


def build_interior(materials: dict) -> AssetBuild:
    collection = bpy.data.collections.new("Interior")
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new("ROOT_BarInterior3D", None)
    root.empty_display_type = "PLAIN_AXES"
    collection.objects.link(root)
    asset = AssetBuild(root, collection)

    build_shell(asset, materials)
    build_counter(asset, materials)
    build_backbar(asset, materials)
    build_booths(asset, materials)
    build_stage(asset, materials)
    build_tables_and_bay(asset, materials)
    build_dressing(asset, materials)
    build_ceiling_fan(asset, materials)
    build_jukebox(asset, materials)
    build_practical_prefab(asset, materials)
    build_districts(asset, materials)
    build_activities(asset, materials)

    add_anchor(asset, "Entrance", "entrance", (0.0, 0.0, -ROOM_DEPTH * 0.5))
    add_anchor(asset, "RoomCentre", "room_centre", (0.0, 0.0, 0.0))
    add_anchor(asset, "CounterStation", "counter_station", COUNTER_STATION)
    add_anchor(asset, "ActivityStation", "activity_station",
               ACTIVITY_STATION)
    add_anchor(asset, "CeilingFan", "ceiling_fan_pivot", (0.0, 4.35, 0.75))
    add_anchor(asset, "Jukebox", "jukebox_pivot", (6.4, 0.0, -6.78))
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

    for part in asset.parts:
        if part.name != "Front Wall":
            continue
        reveal = min(abs(vertex[0]) for vertex in part.geometry[0])
        if abs(reveal * 2.0 - DOOR_WIDTH) > 0.02:
            problems.append(
                f"the doorway reads {reveal * 2.0:.3f} m against "
                f"{DOOR_WIDTH:.3f}")

    for kind in ACTIVITY_KINDS:
        if not any(part.group == f"activity:{kind}"
                   for part in asset.parts):
            problems.append(f"activity set '{kind}' is missing")
    for mood in DISTRICT_MOODS:
        if not any(part.group == f"district:{mood}"
                   for part in asset.parts):
            problems.append(f"district set '{mood}' is missing")

    if problems:
        raise SystemExit(
            "Bar interior failed validation:\n  " + "\n  ".join(problems))


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
            name: [stable(value) for value in anchor.location]
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
    door_opening: Sequence[float] = (DOOR_WIDTH, ROOM_HEIGHT),
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
            }
            for name, anchor in sorted(asset.anchors.items())
        ],
        "parts": [manifest_part(part) for part in asset.parts],
        "build_signature": signature_for(
            asset,
            design_id,
            generator_version),
    }
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
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    return parser.parse_args(argv)


def main() -> None:
    args = parse_args(list(sys.argv))
    interior, facade = build()
    validate(interior)
    validate_facade(facade)
    total = kit.triangle_count(
        kit.merge_all([part.geometry for part in interior.parts]))
    facade_total = kit.triangle_count(
        kit.merge_all([part.geometry for part in facade.parts]))
    if args.validate_only:
        print(
            f"Bar validates: interior {len(interior.parts)} parts / "
            f"{total} triangles, facade {len(facade.parts)} parts / "
            f"{facade_total} triangles.")
        return

    manifest = write_manifest(interior, args.manifest)
    export_fbx(interior, args.fbx)
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
    bpy.ops.wm.save_as_mainfile(filepath=str(args.blend))
    print(
        f"Bar interior written: {manifest['mesh_count']} parts, "
        f"{manifest['triangle_count']} triangles, "
        f"signature {manifest['build_signature'][:12]}.")
    print(
        f"Bar exterior written: {facade_manifest['mesh_count']} parts, "
        f"{facade_manifest['triangle_count']} triangles, "
        f"signature {facade_manifest['build_signature'][:12]}.")


if __name__ == "__main__":
    main()
