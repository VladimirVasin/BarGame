#!/usr/bin/env python3
"""Build the provincial Gothic Catholic church from one Blender source.

The source owns two isolated export collections.  The exterior FBX is the
City landmark; the interior FBX is the passive visual shell used by the
ChurchInterior runtime plan.  Gameplay collision, lights and cameras are not
exported.  Semantic empties and the JSON manifest are the only bridge between
the authored geometry and Unity.

Run with Blender 5 from the repository root::

    blender --background --factory-startup --python \
      tools/build-church-3d-model.py

The generator validates geometry before writing anything.  A direct in-memory
validation pass is also available with ``-- --validate-only``.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Sequence

try:
    import bpy
    from mathutils import Matrix, Vector
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error


ROOT = Path(__file__).resolve().parents[1]
GENERATOR_VERSION = "1.2.0"
DESIGN_ID = "provincial_catholic_gothic_basilica_v1"
DISPLAY_NAME = "Catholic Church of the Northern Cemetery"
WIDTH = 23.0
LENGTH = 44.0
HEIGHT = 32.0
DOOR_WIDTH = 2.8
DOOR_HEIGHT = 4.2

DEFAULT_BLEND = ROOT / "ArtSource" / "Church" / "Blender" / "Church3D.blend"
DEFAULT_EXT_PREVIEW = ROOT / "ArtSource" / "Church" / "Blender" / "ChurchExterior3D.png"
DEFAULT_INT_PREVIEW = ROOT / "ArtSource" / "Church" / "Blender" / "ChurchInterior3D.png"
DEFAULT_EXT_FBX = ROOT / "Assets" / "Church" / "Models" / "ChurchExterior3D.fbx"
DEFAULT_INT_FBX = ROOT / "Assets" / "Church" / "Models" / "ChurchInterior3D.fbx"
DEFAULT_MANIFEST = ROOT / "Assets" / "Church" / "Models" / "Church3D.json"
TEXTURE_DIR = ROOT / "Assets" / "Church" / "Textures"

TEXTURES = {
    "Plaster": "ChurchPlasterAlbedo.png",
    "Stone": "ChurchStoneAlbedo.png",
    "Wood": "ChurchWoodAlbedo.png",
    "Roof": "ChurchMetalAlbedo.png",
    "Iron": "ChurchMetalAlbedo.png",
    "Gold": "ChurchMetalAlbedo.png",
    "Floor": "ChurchFloorAlbedo.png",
    "Textile": "ChurchTextileAlbedo.png",
    "SacredArt": "ChurchSacredArtAtlasAlbedo.png",
    "Mural": "ChurchMuralAtlasAlbedo.png",
    "GlassCold": "ChurchGlassAtlasAlbedo.png",
    "GlassWarm": "ChurchGlassAtlasAlbedo.png",
    "CandleFlame": "ChurchGlassAtlasAlbedo.png",
}
EMISSIVE_SLOTS = {"GlassCold", "GlassWarm", "CandleFlame"}
ATLAS_SLOTS = {"SacredArt", "Mural", "GlassCold", "GlassWarm", "CandleFlame"}
COLORS = {
    "Plaster": (0.78, 0.78, 0.70, 1.0),
    "Stone": (0.50, 0.52, 0.50, 1.0),
    "Wood": (0.35, 0.19, 0.10, 1.0),
    "Roof": (0.12, 0.18, 0.16, 1.0),
    "Iron": (0.08, 0.09, 0.08, 1.0),
    "Gold": (0.72, 0.46, 0.10, 1.0),
    "Floor": (0.43, 0.41, 0.37, 1.0),
    "Textile": (0.40, 0.08, 0.07, 1.0),
    "SacredArt": (0.86, 0.72, 0.45, 1.0),
    "Mural": (0.54, 0.60, 0.59, 1.0),
    "GlassCold": (0.26, 0.50, 0.58, 1.0),
    "GlassWarm": (0.95, 0.55, 0.20, 1.0),
    "CandleFlame": (1.0, 0.38, 0.08, 1.0),
}

EXTERIOR_ANCHORS = {
    "ANCHOR_Exterior.Entrance": ((0.0, -22.05, 0.0), (0.0, 0.0, 180.0), "entrance_threshold"),
    "ANCHOR_Exterior.Approach": ((0.0, -27.0, 0.0), (0.0, 0.0, 180.0), "street_approach"),
    "ANCHOR_Exterior.Return": ((0.0, -24.4, 0.0), (0.0, 0.0, 180.0), "city_return"),
}
INTERIOR_ANCHORS = {
    "ANCHOR_Interior.Spawn": ((0.0, -18.8, 0.0), (0.0, 0.0, 0.0), "player_spawn"),
    "ANCHOR_Interior.Exit": ((0.0, -21.0, 0.0), (0.0, 0.0, 180.0), "exit_interaction"),
    "ANCHOR_Interior.NarthexLight": ((0.0, -15.8, 4.2), (0.0, 0.0, 0.0), "light_narthex"),
    "ANCHOR_Interior.NaveLight": ((0.0, -1.5, 8.8), (0.0, 0.0, 0.0), "light_nave"),
    "ANCHOR_Interior.SanctuaryLight": ((0.0, 15.7, 5.0), (0.0, 0.0, 180.0), "light_sanctuary"),
}

# The narthex must open directly into the nave.  Its floor is deliberately
# below this volume and the choir loft is above it, but no authored polygon
# may close or obstruct the player-height passage.
INTERIOR_NARTHEX_NAVE_WALK_VOLUME = (
    (-8.2, -15.6, .05),
    (8.2, -14.8, 2.2),
)

AISLE_WINDOW_YS = (-11.0, -6.0, 0.0, 6.0, 11.0)
AISLE_WINDOW_WIDTH = 1.25

# The aisle wall is two leaves with a step between them, and the step
# IS the reveal. The outer leaf carries the true aperture - the hole the
# sun actually comes through, exactly the size of the glass - and the
# inner leaf carries a wider one, so the opening splays into the room
# the way a thick wall's embrasure does. Before this the wall was one
# unbroken 0.32 m slab with a decorative pane glued to its inside face:
# there was no hole, and no light could ever have passed through a
# window in this building.
AISLE_WALL_INNER_X = 11.09
AISLE_WALL_OUTER_X = 11.41
AISLE_LEAF_THICKNESS = 0.16
AISLE_WALL_TOP_Z = 10.0
# Outer leaf: the aperture proper. Its width IS the window's width -
# one census, not two.
LANCET_APERTURE_WIDTH = AISLE_WINDOW_WIDTH
LANCET_APERTURE_SILL_Z = 3.85
LANCET_APERTURE_HEAD_Z = 6.95
# Inner leaf: the splay, 0.20 m clear of the aperture on all four sides.
LANCET_SPLAY_WIDTH = 1.65
LANCET_SPLAY_SILL_Z = 3.65
LANCET_SPLAY_HEAD_Z = 7.15

# The vault springs here and the aisle lean-to takes over outboard of
# it, carrying the roof to the wall head.
VAULT_SPRING_X = 8.0
VAULT_SPRING_Z = 9.6
VAULT_RIDGE_Z = 14.0
ROOF_THICKNESS = 0.30
AISLE_ROOF_EAVE_Z = 8.3
# The lean-to dies INSIDE the wall rather than at its outer face, so
# that the slab's own thickness does not push the model wider than the
# 22.82 m the layout contract measures.
AISLE_ROOF_EAVE_X = 11.28
# The shell now runs the full length, roofing the narthex and the
# sanctuary, which stood open to the sky - the narthex being exactly
# where the hero opens his eyes.
SHELL_END_Y = 21.85
SHELL_TOP_Z = 14.0
STATION_YS = (-13.8, -8.5, -4.2, -1.8, 1.8, 4.2, 8.5)
STATION_WIDTH = 1.2
STATION_HEIGHT = 1.75
# The backrest sits on the NEAR side of the bench so the sitter faces
# the sanctuary at +y. Negative is the whole point; see validate_pews.
PEW_BACKREST_OFFSET = -0.27

# .78 and not more: the north and south aisle routes begin at
# x 6.3, and a pier centred at 5.5 may not reach them.
VOTIVE_STAND_CENTERS = ((-8.8, 10.5), (8.8, 10.5))
VOTIVE_CANDLE_COUNT = 16
VOTIVE_CLUSTER_RADIUS = .28
VOTIVE_RING_TOP = 1.02
# The flames are NOT authored here. They are the one part of this model
# that has to move, so ChurchInteriorAtmosphere builds and animates them
# at this height from the same ring rule; the geometry below keeps them
# only to declare the fixture's vertical envelope.
VOTIVE_FLAME_HEIGHT = 1.28

PIER_FLARE_RADIUS = .78
PIER_FOOTPRINT = (PIER_FLARE_RADIUS * 2, PIER_FLARE_RADIUS * 2)

PEW_CENTER_XS = (-2.9, 2.9)
PEW_ROW_YS = (-8.5, -6.95, -5.4, -3.85, -2.3,
              -0.75, 0.8, 2.35, 3.9, 5.45)
PEW_FOOTPRINT = (3.8, .72)
PEW_VERTICAL_ENVELOPE = (0.0, 1.5)
ALTAR_TABLE_CENTER = (0.0, 15.7)
ALTAR_TABLE_FOOTPRINT = (2.75, 1.55)
ALTAR_TABLE_VERTICAL_ENVELOPE = (0.0, 1.14)
CHOIR_SUPPORT_CENTERS = (
    (-8.0, -18.2), (-5.3, -18.2),
    (5.3, -18.2), (8.0, -18.2),
)
CHOIR_SUPPORT_FOOTPRINT = (.32, .32)
CHOIR_SUPPORT_VERTICAL_ENVELOPE = (0.0, 4.4)
ORGAN_CENTER = (0.0, -20.3)
ORGAN_FOOTPRINT = (12.0, 1.6)
ORGAN_VERTICAL_ENVELOPE = (4.8, 11.7)

INTERIOR_LAYOUT_CONTRACT = [
    {"name": "nave_piers", "count": 4,
     "centers_xz": [[-5.5, -3.5], [5.5, -3.5], [-5.5, 5.5], [5.5, 5.5]],
     "footprint_xz_m": list(PIER_FOOTPRINT),
     "vertical_envelope_m": [0.0, 9.6]},
    {"name": "pew_halves",
     "count": len(PEW_ROW_YS) * len(PEW_CENTER_XS),
     "centers_xz": [[x, z] for z in PEW_ROW_YS for x in PEW_CENTER_XS],
     "footprint_xz_m": list(PEW_FOOTPRINT),
     "vertical_envelope_m": list(PEW_VERTICAL_ENVELOPE)},
    {"name": "communion_rail", "count": 1, "centers_xz": [[0.0, 12.4]],
     "footprint_xz_m": [21.6, .4], "vertical_envelope_m": [0.0, .92]},
    {"name": "altar_table", "count": 1,
     "centers_xz": [list(ALTAR_TABLE_CENTER)],
     "footprint_xz_m": list(ALTAR_TABLE_FOOTPRINT),
     "vertical_envelope_m": list(ALTAR_TABLE_VERTICAL_ENVELOPE)},
    {"name": "high_altar", "count": 1, "centers_xz": [[0.0, 18.0]],
     "footprint_xz_m": [4.2, 2.5], "vertical_envelope_m": [0.0, 6.2]},
    {"name": "crucifix", "count": 1, "centers_xz": [[0.0, 20.65]],
     "footprint_xz_m": [2.5, .35], "vertical_envelope_m": [3.5, 8.2]},
    {"name": "confessionals", "count": 2,
     "centers_xz": [[-9.7, 7.3], [9.7, 7.3]],
     "footprint_xz_m": [1.8, 3.3], "vertical_envelope_m": [0.0, 3.15]},
    {"name": "votive_stands", "count": 2,
     "centers_xz": [[-8.8, 10.5], [8.8, 10.5]],
     "footprint_xz_m": [.8, .8], "vertical_envelope_m": [0.0, 1.35]},
    {"name": "baptismal_font", "count": 1,
     "centers_xz": [[-8.8, -16.8]], "footprint_xz_m": [1.1, 1.1],
     "vertical_envelope_m": [0.0, 1.11]},
    {"name": "choir_loft", "count": 1, "centers_xz": [[0.0, -18.4]],
     "footprint_xz_m": [17.0, 4.2], "vertical_envelope_m": [4.4, 4.8]},
    {"name": "choir_loft_supports", "count": 4,
     "centers_xz": [list(center) for center in CHOIR_SUPPORT_CENTERS],
     "footprint_xz_m": list(CHOIR_SUPPORT_FOOTPRINT),
     "vertical_envelope_m": list(CHOIR_SUPPORT_VERTICAL_ENVELOPE)},
    {"name": "pipe_organ", "count": 1,
     "centers_xz": [list(ORGAN_CENTER)],
     "footprint_xz_m": list(ORGAN_FOOTPRINT),
     "vertical_envelope_m": list(ORGAN_VERTICAL_ENVELOPE)},
]
AUDITED_INTERIOR_LAYOUT_NAMES = {
    contract["name"] for contract in INTERIOR_LAYOUT_CONTRACT
}


Geometry = tuple[list[tuple[float, float, float]], list[tuple[int, ...]]]


@dataclass
class Part:
    obj: bpy.types.Object
    role: str
    material_slot: str


@dataclass
class AssetBuild:
    key: str
    root: bpy.types.Object
    collection: bpy.types.Collection
    parts: list[Part] = field(default_factory=list)
    anchors: dict[str, bpy.types.Object] = field(default_factory=dict)
    layout_geometry: dict[str, list[Geometry]] = field(default_factory=dict)


@dataclass(frozen=True)
class AssetReport:
    mesh_count: int
    triangle_count: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]


@dataclass
class BuildResult:
    exterior: AssetBuild
    interior: AssetBuild
    presentation: bpy.types.Collection
    materials: dict[str, bpy.types.Material]


def stable(value: float) -> float:
    return round(float(value), 6)


def merge(*items: Geometry) -> Geometry:
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, ...]] = []
    for item_vertices, item_faces in items:
        offset = len(vertices)
        vertices.extend(item_vertices)
        faces.extend(tuple(index + offset for index in face) for face in item_faces)
    return vertices, faces


def transformed(geometry: Geometry, matrix: Matrix) -> Geometry:
    vertices, faces = geometry
    converted = []
    for vertex in vertices:
        point = matrix @ Vector(vertex)
        converted.append((point.x, point.y, point.z))
    return converted, list(faces)


def box(center: Sequence[float], size: Sequence[float]) -> Geometry:
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


def gable_roof(
    center_y: float,
    width: float,
    length: float,
    eave_z: float,
    ridge_z: float,
) -> Geometry:
    x = width * 0.5
    y0, y1 = center_y - length * 0.5, center_y + length * 0.5
    vertices = [
        (-x, y0, eave_z), (x, y0, eave_z), (0, y0, ridge_z),
        (-x, y1, eave_z), (x, y1, eave_z), (0, y1, ridge_z),
        (-x, y0, eave_z - 0.28), (x, y0, eave_z - 0.28),
        (-x, y1, eave_z - 0.28), (x, y1, eave_z - 0.28),
    ]
    faces = [
        (0, 1, 2), (5, 4, 3), (0, 2, 5, 3), (1, 4, 5, 2),
        (6, 8, 9, 7), (0, 6, 7, 1), (3, 4, 9, 8),
        (0, 3, 8, 6), (1, 7, 9, 4),
    ]
    return vertices, faces


def sloped_slab(
    x0: float,
    z0: float,
    x1: float,
    z1: float,
    y0: float,
    y1: float,
    thickness: float,
) -> Geometry:
    """A SOLID ramp. The quoted line is the underside - what you see
    standing in the room - and the slab stands `thickness` above it,
    measured perpendicular to the pitch.

    Solid is the entire point. The nave vault this replaces was six
    vertices and two quads with no thickness at all, and its faces
    pointed DOWN into the room. URP's ShadowCaster pass culls back
    faces, so from the sun's side there was nothing there: the roof of
    this church has never once stopped a ray of sunlight, and the
    interior's daylight has been arriving over the walls the whole
    time. Always pass x0 < x1 so the computed normal points up.
    """
    run, rise = x1 - x0, z1 - z0
    length = math.hypot(run, rise)
    if length <= 0.0:
        raise ValueError("a sloped slab needs a run")
    offset_x, offset_z = -rise / length * thickness, run / length * thickness
    vertices = [
        (x0, y0, z0), (x1, y0, z1), (x1, y1, z1), (x0, y1, z0),
        (x0 + offset_x, y0, z0 + offset_z),
        (x1 + offset_x, y0, z1 + offset_z),
        (x1 + offset_x, y1, z1 + offset_z),
        (x0 + offset_x, y1, z0 + offset_z),
    ]
    # The same winding as box(): underside, top, then the four flanks.
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    return vertices, faces


def cylinder(
    center: Sequence[float],
    radius: float,
    depth: float,
    segments: int = 16,
) -> Geometry:
    cx, cy, cz = center
    bottom, top = cz - depth * 0.5, cz + depth * 0.5
    vertices = []
    for z in (bottom, top):
        vertices.extend(
            (cx + math.cos(math.tau * i / segments) * radius,
             cy + math.sin(math.tau * i / segments) * radius, z)
            for i in range(segments)
        )
    faces: list[tuple[int, ...]] = []
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((index, following, segments + following, segments + index))
    faces.append(tuple(reversed(range(segments))))
    faces.append(tuple(range(segments, segments * 2)))
    return vertices, faces


def elliptical_cylinder(
    center: Sequence[float],
    radius_x: float,
    radius_y: float,
    depth: float,
    segments: int = 24,
) -> Geometry:
    cx, cy, cz = center
    bottom, top = cz - depth * 0.5, cz + depth * 0.5
    vertices = []
    for z in (bottom, top):
        vertices.extend(
            (cx + math.cos(math.tau * i / segments) * radius_x,
             cy + math.sin(math.tau * i / segments) * radius_y, z)
            for i in range(segments)
        )
    faces = []
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((index, following, segments + following, segments + index))
    faces.extend((tuple(reversed(range(segments))), tuple(range(segments, segments * 2))))
    return vertices, faces


def cone(
    center: Sequence[float],
    bottom_radius: float,
    top_radius: float,
    depth: float,
    segments: int = 16,
) -> Geometry:
    cx, cy, cz = center
    bottom, top = cz - depth * 0.5, cz + depth * 0.5
    vertices = []
    for radius, z in ((bottom_radius, bottom), (top_radius, top)):
        vertices.extend(
            (cx + math.cos(math.tau * i / segments) * radius,
             cy + math.sin(math.tau * i / segments) * radius, z)
            for i in range(segments)
        )
    faces = []
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((index, following, segments + following, segments + index))
    faces.extend((tuple(reversed(range(segments))), tuple(range(segments, segments * 2))))
    return vertices, faces


def lathe(
    center: Sequence[float],
    profile: Sequence[tuple[float, float]],
    segments: int,
) -> Geometry:
    cx, cy, base_z = center
    vertices = []
    for radius, z in profile:
        vertices.extend(
            (cx + math.cos(math.tau * i / segments) * radius,
             cy + math.sin(math.tau * i / segments) * radius,
             base_z + z)
            for i in range(segments)
        )
    faces = []
    for ring in range(len(profile) - 1):
        lower = ring * segments
        upper = (ring + 1) * segments
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((lower + index, lower + following,
                          upper + following, upper + index))
    faces.append(tuple(reversed(range(segments))))
    last = (len(profile) - 1) * segments
    faces.append(tuple(last + index for index in range(segments)))
    return vertices, faces


def elliptical_lathe(
    center: Sequence[float],
    profile: Sequence[tuple[float, float]],
    y_scale: float,
    segments: int,
) -> Geometry:
    cx, cy, base_z = center
    vertices = []
    for radius, z in profile:
        vertices.extend(
            (cx + math.cos(math.tau * i / segments) * radius,
             cy + math.sin(math.tau * i / segments) * radius * y_scale,
             base_z + z)
            for i in range(segments)
        )
    faces = []
    for ring in range(len(profile) - 1):
        lower, upper = ring * segments, (ring + 1) * segments
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((lower + index, lower + following,
                          upper + following, upper + index))
    faces.extend((tuple(reversed(range(segments))),
                  tuple((len(profile) - 1) * segments + index for index in range(segments))))
    return vertices, faces


def torus(
    center: Sequence[float],
    major_radius: float,
    minor_radius: float,
    major_segments: int = 24,
    minor_segments: int = 8,
) -> Geometry:
    cx, cy, cz = center
    vertices = []
    for major in range(major_segments):
        angle = math.tau * major / major_segments
        for minor in range(minor_segments):
            cross = math.tau * minor / minor_segments
            radius = major_radius + math.cos(cross) * minor_radius
            vertices.append((cx + math.cos(angle) * radius,
                             cy + math.sin(angle) * radius,
                             cz + math.sin(cross) * minor_radius))
    faces = []
    for major in range(major_segments):
        following_major = (major + 1) % major_segments
        for minor in range(minor_segments):
            following_minor = (minor + 1) % minor_segments
            faces.append((major * minor_segments + minor,
                          following_major * minor_segments + minor,
                          following_major * minor_segments + following_minor,
                          major * minor_segments + following_minor))
    return vertices, faces


def diamond(center: Sequence[float], radius: float, height: float) -> Geometry:
    cx, cy, cz = center
    vertices = [(cx, cy, cz + height * 0.5), (cx, cy, cz - height * 0.5)]
    vertices.extend((cx + math.cos(math.tau * i / 6) * radius,
                     cy + math.sin(math.tau * i / 6) * radius, cz)
                    for i in range(6))
    faces = []
    for index in range(6):
        following = (index + 1) % 6
        faces.append((0, 2 + index, 2 + following))
        faces.append((1, 2 + following, 2 + index))
    return vertices, faces


def between(a: Sequence[float], b: Sequence[float], radius: float, segments: int = 10) -> Geometry:
    start, end = Vector(a), Vector(b)
    delta = end - start
    geometry = cylinder((0, 0, 0), radius, delta.length, segments)
    matrix = Matrix.Translation((start + end) * 0.5) @ delta.to_track_quat("Z", "Y").to_matrix().to_4x4()
    return transformed(geometry, matrix)


def create_material(slot: str) -> bpy.types.Material:
    material = bpy.data.materials.new(f"PREVIEW_Church{slot}")
    material.diffuse_color = COLORS[slot]
    material.use_nodes = True
    node = material.node_tree.nodes.get("Principled BSDF")
    if node is not None:
        if "Base Color" in node.inputs:
            node.inputs["Base Color"].default_value = COLORS[slot]
        if "Roughness" in node.inputs:
            node.inputs["Roughness"].default_value = 0.72 if slot not in {"Gold", "GlassCold", "GlassWarm"} else 0.3
        if "Metallic IOR Level" in node.inputs and slot in {"Roof", "Iron", "Gold"}:
            node.inputs["Metallic IOR Level"].default_value = 0.65
        if slot in EMISSIVE_SLOTS:
            if "Emission Color" in node.inputs:
                node.inputs["Emission Color"].default_value = COLORS[slot]
            if "Emission Strength" in node.inputs:
                node.inputs["Emission Strength"].default_value = 3.0 if slot == "CandleFlame" else 1.5
    return material


def assign_world_uv(
        mesh: bpy.types.Mesh,
        scale: float = 2.0,
        wrap_upright: bool = False) -> None:
    """Planar world projection, choosing the axis per polygon.

    On a box that is exactly right. On a CYLINDER it is not: the side
    normals sweep the whole XY circle, so every facet past 45 degrees
    picks a different projection axis than its neighbour and the sheet
    mirrors at each of those seams - the blotching reported on the nave
    piers. `wrap_upright` maps those upright curved faces by angle
    instead, per connected component so a merged part of four piers
    still wraps around each one rather than around their common centre.
    """
    layer = mesh.uv_layers.new(name="UVMap")
    axis_centres: dict[int, tuple[float, float, float]] = {}
    if wrap_upright:
        for component in polygon_components(mesh):
            xs, ys, radii = [], [], []
            for polygon_index in component:
                for vertex_index in mesh.polygons[polygon_index].vertices:
                    coordinate = mesh.vertices[vertex_index].co
                    xs.append(coordinate.x)
                    ys.append(coordinate.y)
            centre_x = (min(xs) + max(xs)) * 0.5
            centre_y = (min(ys) + max(ys)) * 0.5
            for polygon_index in component:
                for vertex_index in mesh.polygons[polygon_index].vertices:
                    coordinate = mesh.vertices[vertex_index].co
                    radii.append(math.hypot(coordinate.x - centre_x,
                                            coordinate.y - centre_y))
            radius = max(sum(radii) / len(radii), 1e-4)
            for polygon_index in component:
                axis_centres[polygon_index] = (centre_x, centre_y, radius)

    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        wrapped = wrap_upright and axis != 2 and polygon.index in axis_centres
        centre_x, centre_y, radius = axis_centres.get(
            polygon.index, (0.0, 0.0, 1.0))
        angles = []
        if wrapped:
            for vertex_index in polygon.vertices:
                coordinate = mesh.vertices[vertex_index].co
                angles.append(math.atan2(coordinate.y - centre_y,
                                         coordinate.x - centre_x))
            # Keep one facet's corners on the same turn of the circle,
            # or the seam facet spans the whole sheet.
            reference = angles[0]
            angles = [reference + wrapped_delta(angle - reference)
                      for angle in angles]
        for offset, loop_index in enumerate(polygon.loop_indices):
            coordinate = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if wrapped:
                uv = (angles[offset] * radius / scale, coordinate.z / scale)
            elif axis == 0:
                uv = (coordinate.y / scale, coordinate.z / scale)
            elif axis == 1:
                uv = (coordinate.x / scale, coordinate.z / scale)
            else:
                uv = (coordinate.x / scale, coordinate.y / scale)
            layer.data[loop_index].uv = uv


def wrapped_delta(angle: float) -> float:
    while angle > math.pi:
        angle -= math.tau
    while angle < -math.pi:
        angle += math.tau
    return angle


def polygon_components(mesh: bpy.types.Mesh) -> list[list[int]]:
    by_vertex: dict[int, list[int]] = {}
    for polygon in mesh.polygons:
        for vertex_index in polygon.vertices:
            by_vertex.setdefault(vertex_index, []).append(polygon.index)
    remaining = {polygon.index for polygon in mesh.polygons}
    components = []
    while remaining:
        seed = min(remaining)
        pending = [seed]
        component = []
        remaining.remove(seed)
        while pending:
            polygon_index = pending.pop()
            component.append(polygon_index)
            for vertex_index in mesh.polygons[polygon_index].vertices:
                for neighbour in by_vertex[vertex_index]:
                    if neighbour in remaining:
                        remaining.remove(neighbour)
                        pending.append(neighbour)
        components.append(sorted(component))
    return components


def assign_face_region_uv(
        mesh: bpy.types.Mesh,
        layer: bpy.types.MeshUVLoopLayer,
        polygon: bpy.types.MeshPolygon,
        region: Sequence[float]) -> None:
    u0, v0, u1, v1 = region
    dominant_axis = max(
        range(3), key=lambda axis: abs(polygon.normal[axis]))
    projection_axes = ((1, 2), (0, 2), (0, 1))[dominant_axis]
    coordinates = [
        mesh.vertices[mesh.loops[loop_index].vertex_index].co
        for loop_index in polygon.loop_indices
    ]
    minima = [min(point[axis] for point in coordinates)
              for axis in projection_axes]
    maxima = [max(point[axis] for point in coordinates)
              for axis in projection_axes]
    spans = [maxima[index] - minima[index] for index in range(2)]
    for loop_index, coordinate in zip(polygon.loop_indices, coordinates):
        normalized = [
            ((coordinate[axis] - minima[index]) / spans[index]
             if spans[index] > 1e-8 else .5)
            for index, axis in enumerate(projection_axes)
        ]
        layer.data[loop_index].uv = (
            u0 + normalized[0] * (u1 - u0),
            v0 + normalized[1] * (v1 - v0),
        )


def assign_atlas_uv(mesh: bpy.types.Mesh, slot: str) -> None:
    layer = mesh.uv_layers.new(name="UVMap")
    if slot == "SacredArt":
        components = polygon_components(mesh)
        if len(components) > 16:
            raise RuntimeError(
                "SacredArt supports at most sixteen deterministic panels")
        cell, inset = .25, 8.0 / 512.0
        for component_index, polygon_indices in enumerate(components):
            column, row = component_index % 4, component_index // 4
            region = (
                column * cell + inset,
                row * cell + inset,
                (column + 1) * cell - inset,
                (row + 1) * cell - inset,
            )
            for polygon_index in polygon_indices:
                assign_face_region_uv(
                    mesh, layer, mesh.polygons[polygon_index], region)
        return
    if slot == "Mural":
        inset = 5.0 / 512.0
        for component_index, polygon_indices in enumerate(
                polygon_components(mesh)):
            column, row = component_index % 4, (component_index // 4) % 2
            region = (
                column * .25 + inset,
                row * .5 + inset,
                (column + 1) * .25 - inset,
                (row + 1) * .5 - inset,
            )
            for polygon_index in polygon_indices:
                assign_face_region_uv(
                    mesh, layer, mesh.polygons[polygon_index], region)
        return
    if slot == "CandleFlame":
        # Stable warm pane from the shared 8x8 glass atlas.
        region = (1.0 / 8.0 + .008, .008, 2.0 / 8.0 - .008,
                  1.0 / 8.0 - .008)
    else:
        # Each disconnected lancet/rose geometry receives one complete pane
        # grid; individual faces are normalized and never world-tiled.
        region = (0.0, 0.0, 1.0, 1.0)
    for polygon in mesh.polygons:
        assign_face_region_uv(mesh, layer, polygon, region)


def assign_material_uv(
        mesh: bpy.types.Mesh,
        slot: str,
        world_scale: float,
        wrap_upright: bool = False) -> None:
    if slot in ATLAS_SLOTS:
        assign_atlas_uv(mesh, slot)
    else:
        assign_world_uv(mesh, world_scale, wrap_upright)


def add_part(
    asset: AssetBuild,
    name: str,
    geometry: Geometry,
    role: str,
    slot: str,
    materials: dict[str, bpy.types.Material],
    uv_scale: float = 2.0,
    wrap_upright: bool = False,
) -> bpy.types.Object:
    vertices, faces = geometry
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(materials[slot])
    mesh.update(calc_edges=True)
    assign_material_uv(mesh, slot, uv_scale, wrap_upright)
    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = asset.root
    obj["bp_role"] = role
    obj["bp_material_slot"] = slot
    asset.parts.append(Part(obj, role, slot))
    return obj


def add_anchor(
    asset: AssetBuild,
    name: str,
    location: Sequence[float],
    rotation_degrees: Sequence[float],
    role: str,
) -> None:
    anchor = bpy.data.objects.new(name, None)
    asset.collection.objects.link(anchor)
    anchor.parent = asset.root
    anchor.empty_display_type = "ARROWS"
    anchor.empty_display_size = 0.65
    anchor.location = location
    anchor.rotation_euler = tuple(math.radians(value) for value in rotation_degrees)
    anchor["bp_role"] = role
    asset.anchors[name] = anchor


def rotated_box(
    center: Sequence[float],
    size: Sequence[float],
    rotation_x: float = 0.0,
    rotation_y: float = 0.0,
    rotation_z: float = 0.0,
) -> Geometry:
    matrix = (Matrix.Translation(center) @
              Matrix.Rotation(math.radians(rotation_z), 4, "Z") @
              Matrix.Rotation(math.radians(rotation_y), 4, "Y") @
              Matrix.Rotation(math.radians(rotation_x), 4, "X"))
    return transformed(box((0, 0, 0), size), matrix)


def vertical_torus(
    center: Sequence[float],
    radius: float,
    thickness: float,
    major_segments: int = 24,
    minor_segments: int = 8,
) -> Geometry:
    matrix = Matrix.Translation(center) @ Matrix.Rotation(math.radians(90), 4, "X")
    return transformed(
        torus((0, 0, 0), radius, thickness, major_segments, minor_segments),
        matrix,
    )


def cross_geometry(center: Sequence[float], height: float, width: float) -> Geometry:
    cx, cy, cz = center
    return merge(
        box((cx, cy, cz), (0.16, 0.16, height)),
        box((cx, cy, cz + height * 0.19), (width, 0.16, 0.16)),
    )


def build_exterior(asset: AssetBuild, materials: dict[str, bpy.types.Material]) -> None:
    add_part(asset, "EXT_Foundation", merge(
        box((0, 0, .32), (WIDTH, LENGTH, .64)),
        box((0, -20.55, .18), (5.8, 2.8, .36)),
        box((0, -21.35, .09), (7.2, 1.3, .18)),
    ), "foundation_and_west_steps", "Stone", materials, 1.3)

    add_part(asset, "EXT_BasilicaMasses", merge(
        box((0, 1.4, 5.05), (15.4, 36.6, 9.45)),
        box((0, 1.0, 3.75), (22.2, 34.0, 6.85)),
        box((0, -18.0, 6.1), (18.0, 7.6, 11.55)),
        elliptical_cylinder((0, 19.0, 4.55), 7.45, 3.0, 8.45, 32),
        box((0, -17.8, 10.0), (8.2, 8.2, 19.35)),
    ), "whitewashed_gothic_basilica_mass", "Plaster", materials, 2.2)

    stonework = [
        box((0, 1.0, .95), (22.55, 34.2, 1.25)),
        box((0, -18.0, .95), (18.3, 7.85, 1.25)),
        elliptical_cylinder((0, 19.0, .95), 7.7, 3.1, 1.25, 32),
    ]
    for side in (-1, 1):
        x = side * 10.9
        for y in (-12.0, -6.0, 0.0, 6.0, 12.0):
            stonework.extend((
                box((x, y, 2.4), (.62, 1.15, 4.8)),
                box((x, y, .75), (1.1, 1.65, 1.5)),
                cone((x, y, 5.05), .42, .08, 1.2, 8),
            ))
    for x in (-8.75, 8.75, -4.0, 4.0):
        stonework.extend((box((x, -21.48, 3.0), (.72, .55, 6.0)),
                          cone((x, -21.48, 6.45), .48, .05, 1.2, 8)))
    add_part(asset, "EXT_StoneButtresses", merge(*stonework), "stone_plinth_and_flying_buttresses", "Stone", materials, 1.0)

    apse_profile = ((7.6, 0), (7.5, .3), (6.8, .9), (5.7, 1.55),
                    (4.2, 2.15), (2.4, 2.65), (.2, 2.95))
    roofs = [
        gable_roof(1.2, 16.6, 37.0, 9.7, 14.2),
        gable_roof(-18.0, 19.0, 8.0, 11.9, 16.1),
        rotated_box((-9.05, 1.0, 7.55), (4.2, 34.3, .32), rotation_y=-18),
        rotated_box((9.05, 1.0, 7.55), (4.2, 34.3, .32), rotation_y=18),
        elliptical_lathe((0, 19.0, 8.55), apse_profile, .40, 32),
    ]
    add_part(asset, "EXT_GabledRoofs", merge(*roofs), "steep_oxidized_gabled_roofs", "Roof", materials, 1.7)

    add_part(asset, "EXT_BellSpire", merge(
        cone((0, -17.8, 24.45), 4.1, .18, 9.5, 8),
        cone((-3.45, -21.05, 20.9), .72, .04, 3.0, 8),
        cone((3.45, -21.05, 20.9), .72, .04, 3.0, 8),
        cone((-3.45, -14.55, 20.9), .72, .04, 3.0, 8),
        cone((3.45, -14.55, 20.9), .72, .04, 3.0, 8),
    ), "central_bell_spire_and_pinnacles", "Roof", materials, .9)
    add_part(asset, "EXT_LatinCross", cross_geometry((0, -17.8, 30.6), 2.8, 1.6), "spire_latin_cross", "Gold", materials, .4)

    add_part(asset, "EXT_WestDoors", merge(
        box((0, -21.84, 2.1), (DOOR_WIDTH, .24, DOOR_HEIGHT)),
        box((-0.72, -21.98, 2.1), (.10, .10, 3.95)),
        box((.72, -21.98, 2.1), (.10, .10, 3.95)),
        between((-1.55, -21.98, 4.15), (0, -21.98, 5.25), .12, 10),
        between((1.55, -21.98, 4.15), (0, -21.98, 5.25), .12, 10),
    ), "central_west_double_door", "Wood", materials, .75)

    glass, tracery = [], []
    for side in (-1, 1):
        x = side * 11.14
        for y in (-10.0, -4.0, 2.0, 8.0, 14.0):
            glass.append(box((x, y, 4.85), (.08, 1.25, 3.25)))
            tracery.extend((
                between((x, y - .72, 3.2), (x, y - .72, 5.9), .09, 10),
                between((x, y + .72, 3.2), (x, y + .72, 5.9), .09, 10),
                between((x, y - .72, 5.9), (x, y, 7.05), .09, 10),
                between((x, y + .72, 5.9), (x, y, 7.05), .09, 10),
                between((x, y, 3.2), (x, y, 6.8), .07, 8),
            ))
    # Bell openings and paired facade lancets.
    for x in (-2.0, 2.0):
        glass.append(box((x, -21.94, 14.65), (1.25, .08, 3.0)))
        tracery.extend((
            between((x - .7, -22.0, 13.2), (x - .7, -22.0, 15.7), .10, 10),
            between((x + .7, -22.0, 13.2), (x + .7, -22.0, 15.7), .10, 10),
            between((x - .7, -22.0, 15.7), (x, -22.0, 16.75), .10, 10),
            between((x + .7, -22.0, 15.7), (x, -22.0, 16.75), .10, 10),
        ))
    add_part(asset, "EXT_StainedGlass", merge(*glass), "stained_lancet_windows", "GlassWarm", materials, .7)
    add_part(asset, "EXT_LancetTracery", merge(*tracery), "pointed_window_stone_tracery", "Stone", materials, .55)

    rose = [box((0, -21.96, 9.0), (5.5, .07, 5.5)),
            vertical_torus((0, -22.02, 9.0), 2.85, .18, 32, 10),
            vertical_torus((0, -22.04, 9.0), 1.35, .12, 24, 8)]
    rose_frame = []
    for index in range(16):
        angle = math.tau * index / 16
        rose_frame.append(between(
            (math.cos(angle) * .4, -22.08, 9.0 + math.sin(angle) * .4),
            (math.cos(angle) * 2.72, -22.08, 9.0 + math.sin(angle) * 2.72),
            .07, 8))
    add_part(asset, "EXT_RoseGlass", rose[0], "west_rose_stained_glass", "GlassWarm", materials, .5)
    add_part(asset, "EXT_RoseTracery", merge(*rose[1:], *rose_frame), "west_rose_stone_tracery", "Stone", materials, .45)

    trim = []
    for y in [value * 1.1 - 15.0 for value in range(29)]:
        trim.append(box((0, y, 11.8), (.11, .16, 4.2)))
    for z in (7.2, 9.5, 19.5):
        trim.append(box((0, -21.55 if z < 19 else -17.8, z),
                        (18.3 if z < 19 else 8.5, .22, .22)))
    for x in (-4.05, 4.05):
        trim.append(box((x, -17.8, 14.8), (.18, 8.3, 9.2)))
    for index in range(20):
        angle = math.tau * index / 20
        trim.append(cone((math.cos(angle) * 7.55,
                          19.0 + math.sin(angle) * 3.0,
                          10.0), .10, .04, 2.8, 8))
    add_part(asset, "EXT_CornicesAndRoofSeams", merge(*trim), "gothic_cornices_pilasters_and_roof_seams", "Iron", materials, .65)


def pew_geometry(center_x: float, center_y: float) -> Geometry:
    # +y is the sanctuary. The BACKREST must sit on the near side of
    # the bench so the sitter faces the altar; it used to sit on the
    # +y side, which turned the whole nave around to face the door.
    items = [
        box((center_x, center_y, .55), (*PEW_FOOTPRINT, .18)),
        box((center_x, center_y + PEW_BACKREST_OFFSET, 1.025),
            (PEW_FOOTPRINT[0], .16, .95)),
        box((center_x, center_y + .30, .48), (3.55, .10, .14)),
    ]
    for x in (center_x - 1.65, center_x + 1.65):
        items.extend((
            box((x, center_y, .275), (.16, .52, .55)),
            box((x, center_y + PEW_BACKREST_OFFSET, .6),
                (.16, .16, 1.2))))
    return merge(*items)


def votive_candle_xy(center_x: float, center_y: float,
                     index: int, count: int,
                     radius: float) -> tuple[float, float]:
    """Three concentric rings, offset so no two candles line up.

    ChurchInteriorAtmosphere.VotiveFlamePositions mirrors this exactly;
    the flames it animates have to stand on the wicks authored here.
    """
    ring = index % 3
    angle = math.tau * index / count + ring * .31
    distance = radius * (0.3 + ring * .32)
    return (center_x + math.cos(angle) * distance,
            center_y + math.sin(angle) * distance)


def candle_cluster(center_x: float, center_y: float, count: int, radius: float) -> tuple[Geometry, Geometry]:
    candles, flames = [], []
    for index in range(count):
        x, y = votive_candle_xy(center_x, center_y, index, count, radius)
        height = .15 + (index % 5) * .015
        candles.append(
            cylinder((x, y, VOTIVE_RING_TOP + height * .5), .03, height, 8))
        flames.append(diamond((x, y, VOTIVE_FLAME_HEIGHT), .04, .14))
    return merge(*candles), merge(*flames)


def votive_stand_geometry(center_x: float, center_y: float) -> Geometry:
    """Foot on the FLOOR. The plate used to be centred at z .36 with a
    height of .12, so the whole stand hung thirty centimetres in the
    air; the stem is now the part that makes up the difference and the
    ring and its candles have not moved."""
    return merge(
        cylinder((center_x, center_y, .06), .375, .12, 18),
        cylinder((center_x, center_y, .57), .08, .90, 10),
        torus((center_x, center_y, VOTIVE_RING_TOP), .28, .055, 20, 7),
    )


def aisle_wall_leaf(
    center_x: float,
    opening_width: float,
    sill_z: float,
    head_z: float,
    y0: float = -22.0,
    y1: float = 22.0,
) -> list[Geometry]:
    """One leaf of an aisle wall, built as the masonry around its
    openings rather than as a slab with panes stuck to it: a sill
    course under every lancet, a head course over them, and the piers
    that stand between.

    The spans are DERIVED from AISLE_WINDOW_YS. There is no second
    census of where the windows are, because this file has already been
    bitten three times by a number that could have been derived and was
    not.
    """
    half = opening_width * 0.5
    pieces = [
        box((center_x, (y0 + y1) * 0.5, sill_z * 0.5),
            (AISLE_LEAF_THICKNESS, y1 - y0, sill_z)),
        box((center_x, (y0 + y1) * 0.5, (head_z + AISLE_WALL_TOP_Z) * 0.5),
            (AISLE_LEAF_THICKNESS, y1 - y0, AISLE_WALL_TOP_Z - head_z)),
    ]
    edges = [y0]
    for window_y in AISLE_WINDOW_YS:
        edges.extend((window_y - half, window_y + half))
    edges.append(y1)
    for start, end in zip(edges[0::2], edges[1::2]):
        pieces.append(
            box((center_x, (start + end) * 0.5, (sill_z + head_z) * 0.5),
                (AISLE_LEAF_THICKNESS, end - start, head_z - sill_z)))
    return pieces


def build_interior(asset: AssetBuild, materials: dict[str, bpy.types.Material]) -> None:
    add_part(asset, "INT_Floor", merge(
        box((0, 0, -.12), (22.6, 43.6, .24)),
        box((0, 16.8, .04), (21.6, 8.6, .32)),
    ), "stone_floor_and_raised_sanctuary", "Floor", materials, 1.25)

    walls = []
    inner_leaf_x = AISLE_WALL_INNER_X + AISLE_LEAF_THICKNESS * 0.5
    outer_leaf_x = AISLE_WALL_OUTER_X - AISLE_LEAF_THICKNESS * 0.5
    for side in (-1, 1):
        walls.extend(aisle_wall_leaf(
            side * inner_leaf_x,
            LANCET_SPLAY_WIDTH,
            LANCET_SPLAY_SILL_Z,
            LANCET_SPLAY_HEAD_Z))
        walls.extend(aisle_wall_leaf(
            side * outer_leaf_x,
            LANCET_APERTURE_WIDTH,
            LANCET_APERTURE_SILL_Z,
            LANCET_APERTURE_HEAD_Z))

    # The end walls now reach the ridge. They stopped at z 10 under a
    # vault that ridges at 14, and the west front carried an open
    # rectangle over the door on top of that, so the sun came in over
    # both ends of the building as well as over the aisles.
    roof_y = SHELL_END_Y + 0.16
    walls.extend((
        box((0, SHELL_END_Y, SHELL_TOP_Z * 0.5), (22.5, .32, SHELL_TOP_Z)),
        box((-6.325, -SHELL_END_Y, SHELL_TOP_Z * 0.5),
            (9.85, .32, SHELL_TOP_Z)),
        box((6.325, -SHELL_END_Y, SHELL_TOP_Z * 0.5),
            (9.85, .32, SHELL_TOP_Z)),
        box((0.0, -SHELL_END_Y, (DOOR_HEIGHT + SHELL_TOP_Z) * 0.5),
            (DOOR_WIDTH, .32, SHELL_TOP_Z - DOOR_HEIGHT)),
    ))

    # Aisle lean-tos. The vault covers only |x| <= 8, so without these
    # both side aisles ran the whole length of the building open to the
    # sky - which is where the daylight the player currently reads as
    # "light through the windows" has actually been coming from.
    walls.extend((
        sloped_slab(
            -AISLE_ROOF_EAVE_X, AISLE_ROOF_EAVE_Z,
            -VAULT_SPRING_X, VAULT_SPRING_Z,
            -roof_y, roof_y, ROOF_THICKNESS),
        sloped_slab(
            VAULT_SPRING_X, VAULT_SPRING_Z,
            AISLE_ROOF_EAVE_X, AISLE_ROOF_EAVE_Z,
            -roof_y, roof_y, ROOF_THICKNESS),
    ))
    add_part(asset, "INT_PlasteredShell", merge(*walls), "narthex_nave_aisles_and_apse_shell", "Plaster", materials, 2.0)
    add_part(asset, "INT_WestDoor",
             box((0, -21.66, 2.1), (DOOR_WIDTH, .18, DOOR_HEIGHT)),
             "west_exit_door", "Wood", materials, .8)

    piers, capitals, pier_groups = [], [], []
    for y in (-3.5, 5.5):
        for x in (-5.5, 5.5):
            # Base, shaft, capital - three solids meeting end to end.
            # The shaft used to run the FULL 9.6 m with base and
            # capital of its own .70 radius buried inside it, so a
            # metre of curved wall was coincident with a curved wall
            # and the two fought for the depth buffer. That is the
            # flicker that was reported on the columns.
            pier = cylinder((x, y, 4.77), .70, 8.66, 24)
            pier_capitals = (
                cylinder((x, y, .22), PIER_FLARE_RADIUS, .44, 24),
                cylinder((x, y, 9.35), PIER_FLARE_RADIUS, .50, 24),
            )
            piers.append(pier)
            capitals.extend(pier_capitals)
            pier_groups.append(merge(pier, *pier_capitals))
    asset.layout_geometry["nave_piers"] = pier_groups
    add_part(asset, "INT_NavePiers", merge(*piers), "four_nave_piers", "Plaster", materials, .9,
             wrap_upright=True)
    add_part(asset, "INT_PierCapitals", merge(*capitals), "pier_bases_and_carved_capitals", "Stone", materials, .7,
             wrap_upright=True)

    # Two SOLID pitches running the whole length of the building, plus
    # a cap over the ridge. What stood here was six vertices and two
    # quads - no thickness, faces pointing down into the room - and it
    # spanned only y -15..18, so it neither covered the narthex and the
    # sanctuary nor cast a shadow over the part it did cover. The
    # undersides are at exactly the heights the old shell had, so the
    # ceiling a player sees, and the ribs hung under it, are unchanged.
    vault_shell = [
        sloped_slab(
            -VAULT_SPRING_X, VAULT_SPRING_Z, 0.0, VAULT_RIDGE_Z,
            -roof_y, roof_y, ROOF_THICKNESS),
        sloped_slab(
            0.0, VAULT_RIDGE_Z, VAULT_SPRING_X, VAULT_SPRING_Z,
            -roof_y, roof_y, ROOF_THICKNESS),
        # The two pitches touch along one LINE at the ridge and their
        # upper faces splay apart above it, which from overhead is a
        # 29 cm slot straight down the middle of the nave. The cap is
        # what shuts it.
        box((0.0, 0.0, VAULT_RIDGE_Z + 0.14),
            (0.4, roof_y * 2.0, 0.28)),
    ]
    ribs = []
    for y in (-13.0, -8.5, -3.5, 1.0, 5.5, 10.0):
        ribs.extend((
            between((-7.7, y, 9.5), (0, y, 13.9), .12, 10),
            between((7.7, y, 9.5), (0, y, 13.9), .12, 10),
            between((-7.7, y, 9.5), (0, y + 2.0, 12.7), .08, 8),
            between((7.7, y, 9.5), (0, y + 2.0, 12.7), .08, 8),
        ))
    for y0, y1 in zip((-13.0, -8.5, -3.5, 1.0, 5.5), (-8.5, -3.5, 1.0, 5.5, 10.0)):
        ribs.append(between((0, y0, 13.9), (0, y1, 13.9), .10, 10))
    add_part(asset, "INT_RibbedVault", merge(*vault_shell), "pointed_nave_vault", "Mural", materials, 1.25)
    add_part(asset, "INT_VaultRibs", merge(*ribs), "gothic_vault_ribs_and_transverse_arches", "Stone", materials, .55)

    pews = []
    for y in PEW_ROW_YS:
        pews.extend(pew_geometry(x, y) for x in PEW_CENTER_XS)
    asset.layout_geometry["pew_halves"] = pews
    add_part(asset, "INT_Pews", merge(*pews), "twelve_nave_pew_halves", "Wood", materials, .75)

    confessionals = []
    for x in (-9.7, 9.7):
        confessionals.append(merge(
            box((x, 7.3, 1.4), (1.8, 3.0, 2.8)),
            box((x, 5.82, 1.45), (1.35, .08, 2.5)),
            between((x - .55, 5.75, 2.5), (x, 5.75, 3.05), .07, 8),
            between((x + .55, 5.75, 2.5), (x, 5.75, 3.05), .07, 8),
        ))
    asset.layout_geometry["confessionals"] = confessionals
    add_part(asset, "INT_Confessionals", merge(*confessionals), "paired_wall_confessionals", "Wood", materials, .65)

    font_geometry = merge(
        cylinder((-8.8, -16.8, .55), .55, 1.1, 20),
        cylinder((-8.8, -16.8, 1.02), .42, .18, 20),
    )
    altar_table_geometry = merge(
        box((*ALTAR_TABLE_CENTER, .525), (2.6, 1.4, 1.05)),
        box((*ALTAR_TABLE_CENTER, 1.08), (*ALTAR_TABLE_FOOTPRINT, .12)),
    )
    altar_cloth_geometry = box((0, 15.0, .62), (2.2, .06, .78))
    asset.layout_geometry["baptismal_font"] = [font_geometry]
    asset.layout_geometry["altar_table"] = [merge(
        altar_table_geometry, altar_cloth_geometry)]
    add_part(asset, "INT_StoneFurnishings",
             merge(font_geometry, altar_table_geometry),
             "baptismal_font_and_altar_table", "Stone", materials, .6)
    add_part(asset, "INT_AltarCloth", altar_cloth_geometry,
             "altar_frontal", "Textile", materials, .45)

    stands, candles, flames, votive_groups = [], [], [], []
    for x, y in VOTIVE_STAND_CENTERS:
        stand_geometry = votive_stand_geometry(x, y)
        stands.append(stand_geometry)
        cluster_candles, cluster_flames = candle_cluster(
            x, y, VOTIVE_CANDLE_COUNT, VOTIVE_CLUSTER_RADIUS)
        candles.append(cluster_candles)
        flames.append(cluster_flames)
        # The flames stay in the LAYOUT group, which is what declares
        # the fixture's envelope, and out of the rendered parts.
        votive_groups.append(merge(
            stand_geometry, cluster_candles, cluster_flames))
    asset.layout_geometry["votive_stands"] = votive_groups
    add_part(asset, "INT_VotiveStands", merge(*stands), "paired_votive_candle_stands", "Gold", materials, .45)
    add_part(asset, "INT_VotiveCandles", merge(*candles), "votive_wax_candles", "Plaster", materials, .25)

    rail = [box((0, 12.4, .22), (21.6, .40, .44)),
            box((0, 12.4, .84), (21.6, .16, .15))]
    for x in (-10.55, -9.0, -7.5, -6.0, -4.5, -3.0, -1.0, 0,
              1.0, 3.0, 4.5, 6.0, 7.5, 9.0, 10.55):
        rail.append(cylinder((x, 12.4, .55), .055, .68, 8))
    rail_geometry = merge(*rail)
    asset.layout_geometry["communion_rail"] = [rail_geometry]
    add_part(asset, "INT_CommunionRail", rail_geometry, "closed_communion_rail_and_center_gate", "Gold", materials, .4)

    high_altar = [
        box((0, 18.4, 1.3), (4.0, 1.6, 2.6)),
        box((0, 19.0, 3.0), (3.2, .42, 3.4)),
        box((-1.55, 18.95, 3.35), (.45, .45, 2.9)),
        box((1.55, 18.95, 3.35), (.45, .45, 2.9)),
        between((-2.0, 18.95, 4.75), (0, 18.95, 6.1), .12, 10),
        between((2.0, 18.95, 4.75), (0, 18.95, 6.1), .12, 10),
    ]
    high_altar_geometry = merge(*high_altar)
    add_part(asset, "INT_HighAltar", high_altar_geometry, "high_altar_reredos", "Wood", materials, .6)
    tabernacle_geometry = merge(
        box((0, 17.55, 2.1), (1.15, .50, 1.35)),
        cone((0, 17.55, 3.0), .72, .08, .55, 8),
    )
    asset.layout_geometry["high_altar"] = [merge(
        high_altar_geometry, tabernacle_geometry)]
    add_part(asset, "INT_Tabernacle", tabernacle_geometry,
             "gilded_tabernacle", "Gold", materials, .35)
    crucifix_geometry = merge(
        box((0, 20.65, 3.65), (.8, .35, .3)),
        cross_geometry((0, 20.65, 5.85), 4.7, 2.5),
    )
    asset.layout_geometry["crucifix"] = [crucifix_geometry]
    add_part(asset, "INT_Crucifix", crucifix_geometry,
             "sanctuary_latin_crucifix", "Wood", materials, .45)

    # Seven a side, hung in the PIERS OF WALL BETWEEN the lancets at
    # y -11, -6, 0, 6 and 11. The old even spacing put four of the
    # seven straight across a window, one of them dead centre on it.
    stations = []
    for side in (-1, 1):
        x = side * 11.02
        for y in STATION_YS:
            stations.append(
                box((x, y, 6.75),
                    (.07, STATION_WIDTH, STATION_HEIGHT)))
    add_part(asset, "INT_StationsOfCross", merge(*stations), "fourteen_stations_of_cross", "SacredArt", materials, .45)

    # Gameplay intentionally tracks only the walkable loft slab here.  Its
    # supports and the pipe organ have their own complete layout groups.
    choir_slab = box((0, -18.4, 4.6), (17.0, 4.2, .4))
    choir = [choir_slab, box((0, -16.35, 5.2), (17.0, .18, 1.2))]
    choir_supports = [
        box((*center, 2.2), (*CHOIR_SUPPORT_FOOTPRINT, 4.4))
        for center in CHOIR_SUPPORT_CENTERS
    ]
    choir.extend(choir_supports)
    asset.layout_geometry["choir_loft"] = [choir_slab]
    asset.layout_geometry["choir_loft_supports"] = choir_supports
    organ_case = box((*ORGAN_CENTER, 6.1), (*ORGAN_FOOTPRINT, 2.6))
    organ_wood = [*choir, organ_case]
    add_part(asset, "INT_ChoirAndOrganCase", merge(*organ_wood), "choir_loft_and_pipe_organ_case", "Wood", materials, .75)
    pipes = []
    for index in range(19):
        x = (index - 9) * .55
        height = 2.0 + (1.0 - abs(index - 9) / 9.0) * 2.6
        pipes.append(cylinder((x, ORGAN_CENTER[1] - .05,
                               7.1 + height * .5), .13, height, 12))
    asset.layout_geometry["pipe_organ"] = [merge(organ_case, *pipes)]
    add_part(asset, "INT_OrganPipes", merge(*pipes), "pipe_organ_rank", "Iron", materials, .4)

    # The panes move OUT of the room and into the aperture they belong
    # to, and split by side. The side is the one thing about a lancet
    # that varies through the day - the sun is on the south wall from
    # mid-morning to late afternoon and never on the north one at all -
    # so it is the seam the runtime needs in order to let one wall
    # blaze while the other stays cold.
    glass = {-1: [], 1: []}
    frames = []
    pane_inset = 0.01
    for side in (-1, 1):
        pane_x = side * outer_leaf_x
        frame_x = side * (AISLE_WALL_INNER_X - 0.09)
        for y in AISLE_WINDOW_YS:
            glass[side].append(
                box((pane_x, y, 5.4),
                    (.06,
                     LANCET_APERTURE_WIDTH - pane_inset * 2,
                     LANCET_APERTURE_HEAD_Z - LANCET_APERTURE_SILL_Z -
                     pane_inset * 2)))
            # The tracery now frames the SPLAY, not the old decorative
            # pane: it stands on the inner leaf's wider opening.
            frames.extend((
                between((frame_x, y - .825, LANCET_SPLAY_SILL_Z),
                        (frame_x, y - .825, 6.35), .08, 8),
                between((frame_x, y + .825, LANCET_SPLAY_SILL_Z),
                        (frame_x, y + .825, 6.35), .08, 8),
                between((frame_x, y - .825, 6.35),
                        (frame_x, y, LANCET_SPLAY_HEAD_Z), .08, 8),
                between((frame_x, y + .825, 6.35),
                        (frame_x, y, LANCET_SPLAY_HEAD_Z), .08, 8),
                between((frame_x, y, LANCET_SPLAY_SILL_Z),
                        (frame_x, y, 7.0), .06, 8),
            ))
    add_part(asset, "INT_StainedGlassNorth", merge(*glass[-1]), "north_aisle_stained_glass", "GlassCold", materials, .55)
    add_part(asset, "INT_StainedGlassSouth", merge(*glass[1]), "south_aisle_stained_glass", "GlassCold", materials, .55)
    add_part(asset, "INT_WindowTracery", merge(*frames), "interior_lancet_tracery", "Stone", materials, .45)


def create_asset(key: str, collection_name: str, root_name: str) -> AssetBuild:
    collection = bpy.data.collections.new(collection_name)
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new(root_name, None)
    collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 1.0
    return AssetBuild(key, root, collection)


def configure_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene["bp_generator"] = "tools/build-church-3d-model.py"
    scene["bp_generator_version"] = GENERATOR_VERSION
    scene["bp_design_id"] = DESIGN_ID
    scene["bp_exterior_forward"] = "-Y"
    scene["bp_interior_entrance_to_altar"] = "+Y"
    scene.render.resolution_x = 960
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.image_settings.color_mode = "RGBA"
    scene.world.color = (0.025, 0.032, 0.03)


def build() -> BuildResult:
    configure_scene()
    materials = {slot: create_material(slot) for slot in COLORS}
    presentation = bpy.data.collections.new("PRESENTATION_Church3D")
    bpy.context.scene.collection.children.link(presentation)
    exterior = create_asset("Exterior", "SOURCE_ChurchExterior3D", "ROOT_ChurchExterior3D")
    interior = create_asset("Interior", "SOURCE_ChurchInterior3D", "ROOT_ChurchInterior3D")
    build_exterior(exterior, materials)
    build_interior(interior, materials)
    for name, (location, rotation, role) in EXTERIOR_ANCHORS.items():
        add_anchor(exterior, name, location, rotation, role)
    for name, (location, rotation, role) in INTERIOR_ANCHORS.items():
        add_anchor(interior, name, location, rotation, role)
    return BuildResult(exterior, interior, presentation, materials)


def object_bounds(obj: bpy.types.Object) -> Iterable[Vector]:
    return (obj.matrix_world @ vertex.co for vertex in obj.data.vertices)


def geometry_bounds(geometry: Geometry) -> tuple[Vector, Vector]:
    vertices, _ = geometry
    if not vertices:
        raise RuntimeError("Cannot validate empty layout geometry")
    return (
        Vector(tuple(min(vertex[axis] for vertex in vertices)
                     for axis in range(3))),
        Vector(tuple(max(vertex[axis] for vertex in vertices)
                     for axis in range(3))),
    )


def triangulated_count(mesh: bpy.types.Mesh) -> int:
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def polygon_aabb_intersects(
        obj: bpy.types.Object,
        polygon: bpy.types.MeshPolygon,
        volume_min: Sequence[float],
        volume_max: Sequence[float]) -> bool:
    points = [obj.matrix_world @ obj.data.vertices[index].co
              for index in polygon.vertices]
    polygon_min = [min(point[axis] for point in points) for axis in range(3)]
    polygon_max = [max(point[axis] for point in points) for axis in range(3)]
    epsilon = 1e-6
    return all(polygon_min[axis] < volume_max[axis] - epsilon and
               polygon_max[axis] > volume_min[axis] + epsilon
               for axis in range(3))


def validate_audited_layout_geometry(
        asset: AssetBuild,
        problems: list[str]) -> None:
    contracts = {item["name"]: item for item in INTERIOR_LAYOUT_CONTRACT}
    if set(asset.layout_geometry) != AUDITED_INTERIOR_LAYOUT_NAMES:
        problems.append(
            "audited layout geometry set differs from "
            f"{sorted(AUDITED_INTERIOR_LAYOUT_NAMES)}")
        return
    for name in sorted(AUDITED_INTERIOR_LAYOUT_NAMES):
        contract = contracts.get(name)
        geometries = asset.layout_geometry[name]
        if contract is None:
            problems.append(f"audited layout contract {name} is missing")
            continue
        if contract["count"] != len(geometries) or \
                len(contract["centers_xz"]) != len(geometries):
            problems.append(
                f"{name} count differs between geometry and manifest contract")
            continue
        for index, (geometry, expected_center) in enumerate(zip(
                geometries, contract["centers_xz"])):
            bounds_min, bounds_max = geometry_bounds(geometry)
            footprint = contract["footprint_xz_m"]
            vertical = contract["vertical_envelope_m"]
            nominal_min = (
                expected_center[0] - footprint[0] * .5,
                expected_center[1] - footprint[1] * .5,
                vertical[0],
            )
            nominal_max = (
                expected_center[0] + footprint[0] * .5,
                expected_center[1] + footprint[1] * .5,
                vertical[1],
            )
            actual_min = tuple(bounds_min)
            actual_max = tuple(bounds_max)
            if any(actual_min[axis] < nominal_min[axis] - .00001 or
                   actual_max[axis] > nominal_max[axis] + .00001
                   for axis in range(3)):
                problems.append(
                    f"{name}[{index}] geometry AABB "
                    f"{tuple(stable(value) for value in actual_min)}.."
                    f"{tuple(stable(value) for value in actual_max)} escapes "
                    f"gameplay envelope "
                    f"{tuple(stable(value) for value in nominal_min)}.."
                    f"{tuple(stable(value) for value in nominal_max)}")


def validate_atlas_uv(part: Part, problems: list[str]) -> None:
    mesh = part.obj.data
    layer = mesh.uv_layers.active
    if layer is None or len(layer.data) != len(mesh.loops):
        problems.append(f"{part.obj.name} has no complete atlas UV layer")
        return
    epsilon = 1e-6
    for loop_index, loop_uv in enumerate(layer.data):
        uv = loop_uv.uv
        if uv.x < -epsilon or uv.x > 1.0 + epsilon or \
                uv.y < -epsilon or uv.y > 1.0 + epsilon:
            problems.append(
                f"{part.obj.name} atlas UV loop {loop_index} is "
                f"({stable(uv.x)}, {stable(uv.y)}), outside [0,1]")
            return
    limits = {
        "SacredArt": (.25, .25),
        "Mural": (.25, .5),
        "CandleFlame": (.125, .125),
        "GlassCold": (1.0, 1.0),
        "GlassWarm": (1.0, 1.0),
    }
    components = polygon_components(mesh)
    sacred_cells = set()
    for component_index, polygon_indices in enumerate(components):
        uvs = [
            layer.data[loop_index].uv
            for polygon_index in polygon_indices
            for loop_index in mesh.polygons[polygon_index].loop_indices
        ]
        minimum = (min(uv.x for uv in uvs), min(uv.y for uv in uvs))
        maximum = (max(uv.x for uv in uvs), max(uv.y for uv in uvs))
        span = (maximum[0] - minimum[0], maximum[1] - minimum[1])
        limit = limits[part.material_slot]
        if span[0] > limit[0] + epsilon or span[1] > limit[1] + epsilon:
            problems.append(
                f"{part.obj.name} atlas component {component_index} UV span "
                f"{tuple(stable(value) for value in span)} exceeds "
                f"{limit}")
        if part.material_slot == "SacredArt":
            center = ((minimum[0] + maximum[0]) * .5,
                      (minimum[1] + maximum[1]) * .5)
            sacred_cells.add((min(3, int(center[0] * 4)),
                              min(3, int(center[1] * 4))))
    if part.material_slot == "SacredArt" and (
            len(components) != 14 or len(sacred_cells) != len(components)):
        problems.append(
            f"{part.obj.name} must use fourteen distinct SacredArt cells")


def validate_asset(asset: AssetBuild) -> AssetReport:
    problems = []
    names = [part.obj.name for part in asset.parts]
    if len(names) != len(set(names)):
        problems.append("mesh names are not unique")
    if not asset.parts:
        problems.append("contains no mesh parts")
    vertices = []
    triangle_count = 0
    for part in asset.parts:
        if part.obj.type != "MESH" or len(part.obj.data.materials) != 1:
            problems.append(f"{part.obj.name} lost its one-mesh/one-material contract")
        if part.material_slot not in TEXTURES:
            problems.append(f"{part.obj.name} has unknown material slot {part.material_slot}")
        elif part.material_slot in ATLAS_SLOTS:
            validate_atlas_uv(part, problems)
        triangle_count += triangulated_count(part.obj.data)
        vertices.extend(object_bounds(part.obj))
    if any(obj.type in {"LIGHT", "CAMERA", "ARMATURE"} for obj in asset.collection.objects):
        problems.append("export collection contains Light, Camera or Armature")
    if bpy.data.actions:
        problems.append("church source must contain no Actions")
    expected = EXTERIOR_ANCHORS if asset.key == "Exterior" else INTERIOR_ANCHORS
    if tuple(asset.anchors) != tuple(expected):
        problems.append("semantic anchor set/order differs from the contract")
    for name, (location, rotation, role) in expected.items():
        anchor = asset.anchors.get(name)
        if anchor is None or anchor.parent != asset.root or anchor.type != "EMPTY":
            problems.append(f"anchor {name} is missing or not direct-root Empty")
            continue
        if (anchor.location - Vector(location)).length > .00001:
            problems.append(f"anchor {name} moved")
        actual_rotation = tuple(round(math.degrees(value), 4) for value in anchor.rotation_euler)
        if any(abs(a - b) > .001 for a, b in zip(actual_rotation, rotation)):
            problems.append(f"anchor {name} rotation changed")
        if anchor.get("bp_role") != role:
            problems.append(f"anchor {name} role changed")
    if not vertices:
        bounds_min = bounds_max = Vector((0, 0, 0))
    else:
        bounds_min = Vector(tuple(min(value[index] for value in vertices) for index in range(3)))
        bounds_max = Vector(tuple(max(value[index] for value in vertices) for index in range(3)))
    if asset.key == "Exterior":
        dimensions = bounds_max - bounds_min
        if not (22.95 <= dimensions.x <= 23.05 and
                43.95 <= dimensions.y <= 44.40 and
                31.95 <= dimensions.z <= 32.05):
            problems.append(f"exterior bounds are {tuple(round(v, 3) for v in dimensions)}, expected 23x44x32 m")
        if not 4500 <= triangle_count <= 12000:
            problems.append(f"exterior triangle budget is {triangle_count}, expected 4500-12000")
        if len(asset.parts) > 18:
            problems.append(f"exterior renderer budget is {len(asset.parts)}, maximum 18")
    else:
        if not 7000 <= triangle_count <= 22000:
            problems.append(f"interior triangle budget is {triangle_count}, expected 7000-22000")
        if len(asset.parts) > 24:
            problems.append(f"interior renderer budget is {len(asset.parts)}, maximum 24")
        if bounds_min.y > -21.8 or bounds_max.y < 21.8 or bounds_min.x > -11.2 or bounds_max.x < 11.2:
            problems.append("interior shell lost its full authored footprint")
        validate_audited_layout_geometry(asset, problems)
        validate_church_furniture(problems)
        validate_lancet_apertures(asset, problems)
        validate_interior_is_sealed_above(asset, problems)
        walk_min, walk_max = INTERIOR_NARTHEX_NAVE_WALK_VOLUME
        for part in asset.parts:
            blocking_polygon = next((
                polygon.index for polygon in part.obj.data.polygons
                if polygon_aabb_intersects(
                    part.obj, polygon, walk_min, walk_max)
            ), None)
            if blocking_polygon is not None:
                problems.append(
                    "protected narthex-to-nave walk volume intersects "
                    f"{part.obj.name} polygon {blocking_polygon}")
    if problems:
        raise RuntimeError(f"{asset.key} church validation failed:\n  - " + "\n  - ".join(problems))
    return AssetReport(len(asset.parts), triangle_count,
                       tuple(stable(v) for v in bounds_min),
                       tuple(stable(v) for v in bounds_max))


def _shell_part(asset: AssetBuild, name: str):
    return next((part for part in asset.parts if part.obj.name == name), None)


def _polygon_covers_column(obj, polygon, x: float, y: float) -> bool:
    """Does this polygon, seen from straight overhead, stand over the
    point (x, y)? Convex containment in projection."""
    points = [obj.matrix_world @ obj.data.vertices[index].co
              for index in polygon.vertices]
    sign = 0
    count = len(points)
    for index in range(count):
        ax, ay = points[index][0], points[index][1]
        bx, by = points[(index + 1) % count][0], points[(index + 1) % count][1]
        cross = (bx - ax) * (y - ay) - (by - ay) * (x - ax)
        if abs(cross) < 1e-9:
            continue
        step = 1 if cross > 0 else -1
        if sign == 0:
            sign = step
        elif step != sign:
            return False
    return sign != 0


def validate_lancet_apertures(asset: AssetBuild, problems: list[str]) -> None:
    """Every lancet must be a real HOLE, and the masonry must still
    close around it.

    The aisle wall used to be one unbroken 0.32 m slab with a
    decorative pane glued to its inside face. There was no opening at
    all, so no light could ever have entered this building through a
    window - and no check that read part NAMES would have noticed,
    because the pane was present and correctly named the whole time.
    This one probes the geometry from both sides: empty where the
    aperture is, solid immediately around it.
    """
    shell = _shell_part(asset, "INT_PlasteredShell")
    if shell is None:
        problems.append("the interior has no plastered shell to open")
        return

    polygons = list(shell.obj.data.polygons)
    depth_lo = AISLE_WALL_INNER_X - 0.02
    depth_hi = AISLE_WALL_OUTER_X + 0.02

    def occupied(side: int, y_lo: float, y_hi: float,
                 z_lo: float, z_hi: float) -> bool:
        xs = sorted((side * depth_lo, side * depth_hi))
        volume_min = (xs[0], y_lo, z_lo)
        volume_max = (xs[1], y_hi, z_hi)
        return any(
            polygon_aabb_intersects(shell.obj, polygon, volume_min, volume_max)
            for polygon in polygons)

    margin = 0.05
    half = LANCET_APERTURE_WIDTH * 0.5
    for side in (-1, 1):
        wall = "south" if side > 0 else "north"
        for window_y in AISLE_WINDOW_YS:
            if occupied(side,
                        window_y - half + margin, window_y + half - margin,
                        LANCET_APERTURE_SILL_Z + margin,
                        LANCET_APERTURE_HEAD_Z - margin):
                problems.append(
                    f"the {wall} lancet at y {window_y} has no aperture: "
                    f"the wall is solid where the light must pass")
            jambs = (
                ("left jamb",
                 window_y - half - 0.20, window_y - half - 0.06,
                 LANCET_APERTURE_SILL_Z + margin,
                 LANCET_APERTURE_HEAD_Z - margin),
                ("right jamb",
                 window_y + half + 0.06, window_y + half + 0.20,
                 LANCET_APERTURE_SILL_Z + margin,
                 LANCET_APERTURE_HEAD_Z - margin),
                ("sill",
                 window_y - half + margin, window_y + half - margin,
                 LANCET_APERTURE_SILL_Z - 0.30,
                 LANCET_APERTURE_SILL_Z - 0.06),
                ("head",
                 window_y - half + margin, window_y + half - margin,
                 LANCET_APERTURE_HEAD_Z + 0.06,
                 LANCET_APERTURE_HEAD_Z + 0.30),
            )
            for label, y_lo, y_hi, z_lo, z_hi in jambs:
                if not occupied(side, y_lo, y_hi, z_lo, z_hi):
                    problems.append(
                        f"the {wall} lancet at y {window_y} has no "
                        f"{label}: the opening runs into open wall")


def validate_interior_is_sealed_above(
        asset: AssetBuild,
        problems: list[str]) -> None:
    """Nothing but the lancets may be open to the sky.

    Two defects hid here for the life of the model. The side aisles,
    the narthex and the sanctuary had no roof at all - the vault spans
    only |x| <= 8 - and the vault itself was a single-sided shell whose
    faces pointed down into the room, which the ShadowCaster pass culls,
    so it never blocked a ray either. Requiring TWO covering polygons
    per column is what catches the second one: a solid has an underside
    AND a top, a shell has one face and casts nothing.
    """
    roof_parts = [
        part for part in (
            _shell_part(asset, "INT_PlasteredShell"),
            _shell_part(asset, "INT_RibbedVault"),
        ) if part is not None
    ]
    if len(roof_parts) != 2:
        problems.append("the interior is missing a shell or a vault")
        return

    candidates = []
    for part in roof_parts:
        for polygon in part.obj.data.polygons:
            points = [part.obj.matrix_world @ part.obj.data.vertices[i].co
                      for i in polygon.vertices]
            if min(point[2] for point in points) > 8.0:
                candidates.append((part.obj, polygon))

    step = 0.7
    open_columns = []
    x = -AISLE_WALL_INNER_X + 0.24
    while x <= AISLE_WALL_INNER_X - 0.24:
        y = -SHELL_END_Y + 0.24
        while y <= SHELL_END_Y - 0.24:
            covers = sum(
                1 for obj, polygon in candidates
                if _polygon_covers_column(obj, polygon, x, y))
            if covers < 2:
                open_columns.append((stable(x), stable(y), covers))
            y += step
        x += step

    if open_columns:
        sample = open_columns[:4]
        problems.append(
            f"{len(open_columns)} interior roof columns are open to the "
            f"sky or covered by a single-sided shell, first at {sample}")


def validate_church_furniture(problems: list[str]) -> None:
    """The two defects that shipped in the first church, each now a
    build-time failure rather than something noticed in a screenshot.

    Stations of the Cross were evenly spaced down a wall whose lancets
    are NOT evenly spaced, so four of the seven a side crossed a window
    and one sat dead centre on it. And the pews' backrests were on the
    sanctuary side, which turned the whole nave around to face the door.
    """
    reach = (STATION_WIDTH + AISLE_WINDOW_WIDTH) * 0.5
    for station in STATION_YS:
        for window in AISLE_WINDOW_YS:
            if abs(station - window) < reach:
                problems.append(
                    f"station of the cross at y {station} overlaps the "
                    f"lancet at y {window}")

    for center_x, center_y in VOTIVE_STAND_CENTERS:
        vertices, _ = votive_stand_geometry(center_x, center_y)
        lowest = min(vertex[2] for vertex in vertices)
        if lowest > 1e-4:
            problems.append(
                f"votive stand at {center_x}, {center_y} floats "
                f"{lowest:.3f} m above the floor")

    if PEW_BACKREST_OFFSET >= 0.0:
        problems.append(
            "pew backrests sit toward the sanctuary, so the nave faces "
            "the door instead of the altar")

    # Read back from the authored geometry rather than the constant:
    # the backrest is the only part of a pew above 0.8 m, and its
    # vertices must all lie behind the seat centre.
    vertices, _ = pew_geometry(0.0, 0.0)
    backrest = [v for v in vertices if v[2] > 0.8]
    if not backrest:
        problems.append("pew geometry has no backrest above 0.8 m")
    elif max(v[1] for v in backrest) > 0.0:
        problems.append(
            "pew backrest geometry reaches past the seat centre toward "
            "the sanctuary")


def texture_records() -> list[dict[str, str]]:
    records = []
    for slot, name in sorted(TEXTURES.items()):
        path = TEXTURE_DIR / name
        if not path.exists():
            raise RuntimeError(f"Missing generated church texture {path}; run tools/build-church-textures.py first")
        records.append({
            "material_slot": slot,
            "asset_path": f"Assets/Church/Textures/{name}",
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "base_material_asset": (
                "Assets/Resources/Materials/CityNoirEmission.mat"
                if slot in EMISSIVE_SLOTS else
                "Assets/Resources/Materials/RuntimePrimitiveLit.mat"
            ),
        })
    return records


def signature_for(result: BuildResult, reports: dict[str, AssetReport], textures: list[dict[str, str]]) -> str:
    assets = []
    for asset in (result.exterior, result.interior):
        assets.append({
            "key": asset.key,
            "parts": [{
                "name": part.obj.name,
                "role": part.role,
                "material_slot": part.material_slot,
                "vertices": [[stable(value) for value in vertex.co] for vertex in part.obj.data.vertices],
                "faces": [list(polygon.vertices) for polygon in part.obj.data.polygons],
                "uv": [[stable(loop.uv.x), stable(loop.uv.y)]
                       for loop in part.obj.data.uv_layers.active.data],
            } for part in asset.parts],
            "anchors": [{
                "name": name,
                "role": anchor.get("bp_role"),
                "location": [stable(value) for value in anchor.location],
                "rotation": [stable(math.degrees(value)) for value in anchor.rotation_euler],
            } for name, anchor in asset.anchors.items()],
            "triangles": reports[asset.key].triangle_count,
        })
    payload = {"version": GENERATOR_VERSION, "design": DESIGN_ID,
               "assets": assets, "textures": textures,
               "interior_layout_contract": INTERIOR_LAYOUT_CONTRACT}
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def serialized_layout_contracts() -> list[dict]:
    records = []
    for contract in INTERIOR_LAYOUT_CONTRACT:
        record = dict(contract)
        record["centers_xz_flat"] = [
            coordinate for center in contract["centers_xz"]
            for coordinate in center
        ]
        records.append(record)
    return records


def manifest_asset(asset: AssetBuild, report: AssetReport, wrapper_yaw: float) -> dict:
    payload = {
        "kind": asset.key,
        "root_name": asset.root.name,
        "runtime_wrapper_yaw_degrees": wrapper_yaw,
        "mesh_count": report.mesh_count,
        "renderer_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "bounds_min": list(report.bounds_min),
        "bounds_max": list(report.bounds_max),
        "anchors": [{
            "name": name,
            "role": anchor.get("bp_role"),
            "local_position": [stable(value) for value in anchor.location],
            "local_rotation_degrees": [stable(math.degrees(value)) for value in anchor.rotation_euler],
        } for name, anchor in asset.anchors.items()],
        "parts": [{
            "name": part.obj.name,
            "role": part.role,
            "material_slot": part.material_slot,
            "vertices": len(part.obj.data.vertices),
            "triangles": triangulated_count(part.obj.data),
        } for part in asset.parts],
    }
    if asset.key == "Interior":
        payload["layout_contract"] = serialized_layout_contracts()
    return payload


def write_manifest(path: Path, result: BuildResult, reports: dict[str, AssetReport], textures: list[dict[str, str]], signature: str) -> None:
    payload = {
        "generator": "tools/build-church-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "dimensions_m": {"width": WIDTH, "length": LENGTH, "height": HEIGHT},
        "door_opening_m": {"width": DOOR_WIDTH, "height": DOOR_HEIGHT},
        "blender_forward_axis": "-Y",
        "unity_exterior_entrance_outward_axis": "+Z",
        "unity_interior_entrance_to_altar_axis": "+Z",
        "colliders": False,
        "lights": False,
        "cameras": False,
        "animation_count": 0,
        "textures": textures,
        "assets": [
            manifest_asset(result.exterior, reports["Exterior"], 180.0),
            manifest_asset(result.interior, reports["Interior"], 0.0),
        ],
        "build_signature": signature,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def select_asset(asset: AssetBuild) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    asset.root.select_set(True)
    for part in asset.parts:
        part.obj.select_set(True)
    for anchor in asset.anchors.values():
        anchor.select_set(True)
    bpy.context.view_layer.objects.active = asset.root


def export_fbx(path: Path, asset: AssetBuild) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    select_asset(asset)
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True,
        object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True,
        mesh_smooth_type="FACE", use_custom_props=True,
    )


def add_preview_stage(collection: bpy.types.Collection) -> None:
    ground_mesh = bpy.data.meshes.new("ChurchPreviewGround_Mesh")
    vertices, faces = box((0, 0, -.32), (90, 90, .5))
    ground_mesh.from_pydata(vertices, [], faces)
    ground = bpy.data.objects.new("ChurchPreviewGround", ground_mesh)
    collection.objects.link(ground)
    ground_material = bpy.data.materials.new("PREVIEW_ChurchGround")
    ground_material.diffuse_color = (.055, .065, .06, 1)
    ground.data.materials.append(ground_material)
    for name, location, target, energy, color, size in (
        ("Key", (-28, -34, 40), (0, 0, 8), 6200, (.72, .82, .76), 14),
        ("Rim", (24, 18, 32), (0, 0, 9), 4800, (.30, .43, .48), 12),
        ("Warm", (0, -16, 14), (0, 0, 5), 2400, (.95, .54, .25), 8),
        ("FrontFill", (10, -42, 24), (0, -18, 10), 4600, (.72, .77, .72), 10),
        ("NaveFill", (-3, -5, 9), (0, 5, 3.5), 3000, (.78, .76, .64), 6),
        ("SanctuaryFill", (3, 11, 9), (0, 15, 3.5), 2600, (.94, .63, .35), 5),
    ):
        data = bpy.data.lights.new(f"LIGHT_Church{name}", "AREA")
        data.energy, data.color, data.shape, data.size = energy, color, "DISK", size
        light = bpy.data.objects.new(f"LIGHT_Church{name}", data)
        collection.objects.link(light)
        light.location = location
        light.rotation_euler = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y").to_euler()


def move_asset_to_presentation(asset: AssetBuild, presentation: bpy.types.Collection) -> None:
    for obj in [asset.root, *(part.obj for part in asset.parts), *asset.anchors.values()]:
        if obj.name not in presentation.objects:
            presentation.objects.link(obj)


def render_preview(path: Path, result: BuildResult, asset: AssetBuild, camera_location: Sequence[float], target: Sequence[float], hide_other: AssetBuild) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    for obj in asset.collection.objects:
        obj.hide_render = False
    for obj in hide_other.collection.objects:
        obj.hide_render = True
    camera_data = bpy.data.cameras.new(f"CAM_{asset.key}Preview")
    camera = bpy.data.objects.new(f"CAM_{asset.key}Preview", camera_data)
    result.presentation.objects.link(camera)
    camera.location = camera_location
    camera.rotation_euler = (Vector(target) - Vector(camera_location)).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 42 if asset.key == "Exterior" else 28
    scene.camera = camera
    scene.render.filepath = str(path)
    scene.render.film_transparent = False
    bpy.ops.render.render(write_still=True)
    result.presentation.objects.unlink(camera)
    bpy.data.objects.remove(camera)
    bpy.data.cameras.remove(camera_data)
    for obj in hide_other.collection.objects:
        obj.hide_render = False


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blend", type=Path, default=DEFAULT_BLEND)
    parser.add_argument("--exterior-fbx", type=Path, default=DEFAULT_EXT_FBX)
    parser.add_argument("--interior-fbx", type=Path, default=DEFAULT_INT_FBX)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--exterior-preview", type=Path, default=DEFAULT_EXT_PREVIEW)
    parser.add_argument("--interior-preview", type=Path, default=DEFAULT_INT_PREVIEW)
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    return parser.parse_args(argv)


def main() -> int:
    config = parse_args()
    result = build()
    reports = {asset.key: validate_asset(asset) for asset in (result.exterior, result.interior)}
    textures = texture_records()
    signature = signature_for(result, reports, textures)
    if config.validate_only:
        print("CHURCH 3D DIRECT VALIDATION OK")
        print(f"  Exterior: {reports['Exterior'].mesh_count} meshes, {reports['Exterior'].triangle_count} triangles")
        print(f"  Interior: {reports['Interior'].mesh_count} meshes, {reports['Interior'].triangle_count} triangles")
        print(f"  Signature: {signature}")
        return 0
    if not config.no_preview:
        add_preview_stage(result.presentation)
        move_asset_to_presentation(result.exterior, result.presentation)
        move_asset_to_presentation(result.interior, result.presentation)
        render_preview(config.exterior_preview, result, result.exterior,
                       (-46, -64, 27), (0, -1.0, 11.0), result.interior)
        render_preview(config.interior_preview, result, result.interior,
                       (0, -14.2, 4.2), (0, 14.0, 4.4), result.exterior)
    export_fbx(config.exterior_fbx, result.exterior)
    export_fbx(config.interior_fbx, result.interior)
    write_manifest(config.manifest, result, reports, textures, signature)
    save_blend(config.blend)
    print("CHURCH 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Exterior: {reports['Exterior'].mesh_count} meshes, {reports['Exterior'].triangle_count}/12000 triangles")
    print(f"  Interior: {reports['Interior'].mesh_count} meshes, {reports['Interior'].triangle_count}/22000 triangles")
    print(f"  Signature: {signature}")
    print(f"  Blend: {config.blend}")
    print(f"  Exterior FBX: {config.exterior_fbx}")
    print(f"  Interior FBX: {config.interior_fbx}")
    print(f"  Manifest: {config.manifest}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
