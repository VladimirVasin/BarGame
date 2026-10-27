#!/usr/bin/env python3
"""Build the provincial Gothic Catholic church from one Blender source.

The source owns two isolated export collections.  The exterior FBX is the
City landmark; the interior FBX is the passive visual shell used by the
ChurchInterior runtime plan.  Gameplay collision, lights and cameras are not
exported.  Semantic empties and the JSON manifest are the only bridge between
the authored geometry and Unity.

Run with Blender 5 from the repository root::

    blender --background --factory-startup --python-exit-code 1 --python \
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
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import church_metric_geometry as metric
GENERATOR_VERSION = "2.0.0"
DESIGN_ID = "provincial_catholic_gothic_basilica_v1"
DISPLAY_NAME = "Catholic Church of the Northern Cemetery"
WIDTH = 23.0
LENGTH = 44.0
HEIGHT = 32.0
DOOR_WIDTH = metric.DOOR_WIDTH / metric.EXTERIOR_SCALE
DOOR_HEIGHT = metric.DOOR_HEIGHT / metric.EXTERIOR_SCALE
FOUNDATION_DEPTH = .32 / metric.EXTERIOR_SCALE
THRESHOLD_WIDTH = 2.7 / metric.EXTERIOR_SCALE
THRESHOLD_DEPTH = .92 / metric.EXTERIOR_SCALE
THRESHOLD_OUTER_TOP = (.012 - .08) / metric.EXTERIOR_SCALE
LAMP_POSITION = (0.0, -22.05 - .44 / metric.EXTERIOR_SCALE,
                 (2.98 - .08) / metric.EXTERIOR_SCALE)

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
    "ANCHOR_Exterior.DoorLamp": (LAMP_POSITION, (0.0, 0.0, 0.0), "door_lamp_glow"),
}
# The historic exterior source is uniformly placed at .55. Interior geometry
# is now measured in real metres inside that placed building, never scaled
# furniture. One contract owns all openings, fixtures and semantic contacts.
INTERIOR_ANCHORS = metric.ANCHORS
for name,point,role in (("Rose",metric.ROSE_CENTER,"rose_aperture"),
        ("Rose.Radius",(metric.ROSE_RADIUS,metric.ROSE_CENTER[1],metric.ROSE_CENTER[2]),"rose_aperture_radius")):
    EXTERIOR_ANCHORS["ANCHOR_Exterior."+name] = (
        tuple(value/metric.EXTERIOR_SCALE for value in point),(0,0,0),role)
INTERIOR_LAYOUT_CONTRACT = metric.LAYOUT
AUDITED_INTERIOR_LAYOUT_NAMES = {item["name"] for item in INTERIOR_LAYOUT_CONTRACT}
for side, sign in (("North", -1), ("South", 1)):
    for index, depth in enumerate(metric.WINDOW_DEPTHS):
        EXTERIOR_ANCHORS[f"ANCHOR_Exterior.Window.{side}.{index}"] = (
            (sign * metric.WINDOW_X / metric.EXTERIOR_SCALE,
             depth / metric.EXTERIOR_SCALE,
             metric.WINDOW_HEIGHT / metric.EXTERIOR_SCALE),
            (0.0, 0.0, 0.0), "window_aperture")


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
    clearance_reports: list[dict] = field(default_factory=list)


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


def hollow_narthex_roof() -> Geometry:
    """Keep the established outer roof line without a solid flat-bottom wedge.

    Its old 11.62-source-metre flat underside cut the rose although the
    outside eaves were above it. Thickness now goes inward from each
    unchanged outer pitch; two finite gables seal their ends.
    """
    pieces=[]
    for x0,z0,x1,z1 in ((-9.5,11.9,0,16.1),(0,16.1,9.5,11.9)):
        run,rise=x1-x0,z1-z0
        length=math.hypot(run,rise)
        ox,oz=-rise/length*.28,run/length*.28
        pieces.append(kit.translated(sloped_slab(x0,z0,x1,z1,-22,-14,.28),(-ox,0,-oz)))
    for depth in (-22,-14):
        pieces.append(kit.translated(kit.prism([(-9.5,11.9),(9.5,11.9),(0,16.1)],.18),(0,depth,0)))
    return kit.merge_all(pieces)


def build_exterior(asset: AssetBuild, materials: dict[str, bpy.types.Material]) -> None:
    # The visible foundation meets the logical foundation's TOP, not a
    # second raised plinth above it. The threshold exactly fills the paving
    # reservation, grading the .08 m door datum into the .012 m forecourt.
    half = THRESHOLD_WIDTH * .5
    inner, outer = -22.05, -22.05 - THRESHOLD_DEPTH
    ramp = ([(-half, outer, -FOUNDATION_DEPTH),
             (half, outer, -FOUNDATION_DEPTH),
             (half, inner, -FOUNDATION_DEPTH),
             (-half, inner, -FOUNDATION_DEPTH),
             (-half, outer, THRESHOLD_OUTER_TOP),
             (half, outer, THRESHOLD_OUTER_TOP),
             (half, inner, 0.0), (-half, inner, 0.0)],
            [(0,3,2,1),(4,5,6,7),(0,1,5,4),
             (1,2,6,5),(2,3,7,6),(3,0,4,7)])
    foundation = kit.merge(kit.chamfered_box(
        (0, 0, -FOUNDATION_DEPTH * .5),
        (WIDTH, LENGTH, FOUNDATION_DEPTH), .012, top=False), ramp)
    add_part(asset, "EXT_Foundation", foundation,
        "grounded_foundation_and_flush_threshold_ramp", "Stone", materials, 1.3)


    add_part(asset, "EXT_BasilicaMasses", merge(
        box((0, 1.4, 5.05), (15.4, 36.6, 9.45)),
        kit.merge(*[
            kit.translated(kit.rotated_z(kit.wall_run(34.0,6.85,.30/metric.EXTERIOR_SCALE,
                [kit.Opening(depth/metric.EXTERIOR_SCALE-1.0,
                    metric.WINDOW_WIDTH/metric.EXTERIOR_SCALE,
                    metric.WINDOW_HEAD/metric.EXTERIOR_SCALE-.325,
                    metric.WINDOW_SILL/metric.EXTERIOR_SCALE-.325)
                 for depth in metric.WINDOW_DEPTHS],.012,base=.325),90),
                (side*5.9/metric.EXTERIOR_SCALE,1.0,0))
            for side in (-1,1)]),
        # The west wall/rose is shared with the interior. The former
        # solid narthex box made every rose a pane pasted onto plaster.
        kit.scaled(metric.rose_facade_geometry(6.53125), (1/metric.EXTERIOR_SCALE,)*3),
        kit.scaled(kit.merge(*[
            kit.chamfered_box((side*4.8,-9.955,3.265625),(.30,4.29,6.53125),.007)
            for side in (-1,1)]), (1/metric.EXTERIOR_SCALE,)*3),
        elliptical_cylinder((0, 19.0, 4.55), 7.45, 3.0, 8.45, 32),
        # Only the upper tower walls stand above the narthex. A solid
        # lower tower previously backed the rose immediately behind it.
        kit.scaled(kit.merge(
            *[kit.chamfered_box((side*2.105,-9.79,8.67625),(.30,4.51,4.29),.007)
              for side in (-1,1)],
            kit.chamfered_box((0,-7.685,8.67625),(4.51,.30,4.29),.007),
            metric.tower_front_geometry()), (1/metric.EXTERIOR_SCALE,)*3),
    ), "whitewashed_gothic_basilica_mass", "Plaster", materials, 2.2)

    stonework = [
        kit.chamfered_box((0, 1.0, .7875), (22.55, 34.2, 1.575), .012, bottom=False),
        kit.chamfered_box((0, -18.0, .7875), (18.3, 7.85, 1.575), .012, bottom=False),
        elliptical_cylinder((0, 19.0, .7875), 7.7, 3.1, 1.575, 32),
        # The old rectangular nave projects beyond the curved apse base at
        # two corners. Its own low stone course supports those corners
        # inside the existing wall footprint, with no new silhouette.
        kit.chamfered_box((0, 1.4, .25), (15.4, 36.6, .50), .008, bottom=False),
    ]
    for side in (-1, 1):
        stonework.append(kit.chamfered_box(
            (side*(DOOR_WIDTH*.5+.12/metric.EXTERIOR_SCALE),-22.03,(DOOR_HEIGHT+.18/metric.EXTERIOR_SCALE)*.5),
            (.20/metric.EXTERIOR_SCALE,.12/metric.EXTERIOR_SCALE,DOOR_HEIGHT+.18/metric.EXTERIOR_SCALE),.012))
    stonework.append(kit.chamfered_box((0,-22.03,DOOR_HEIGHT+.09/metric.EXTERIOR_SCALE),
        (DOOR_WIDTH+.44/metric.EXTERIOR_SCALE,.12/metric.EXTERIOR_SCALE,.18/metric.EXTERIOR_SCALE),.012))
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
        hollow_narthex_roof(),
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

    scale = metric.EXTERIOR_SCALE
    portal = kit.translated(kit.panelled_leaf(DOOR_WIDTH, DOOR_HEIGHT, .13 / scale,
        panels=3, stile=.10 / scale, chamfer=.009 / scale),
        (-DOOR_WIDTH / 2, -22.05, 0))
    add_part(asset, "EXT_WestDoors", portal, "metric_panelled_west_double_door", "Wood", materials, .75 / scale)


    glass, tracery = [], []
    for side in (-1, 1):
        x = side * 11.14
        for y in (depth / metric.EXTERIOR_SCALE for depth in metric.WINDOW_DEPTHS):
            glass.append(box((x, y, 4.85), (.08, 1.25, 3.25)))
            tracery.extend((
                between((x, y - .72, 3.2), (x, y - .72, 5.9), .09, 10),
                between((x, y + .72, 3.2), (x, y + .72, 5.9), .09, 10),
                between((x, y - .72, 5.9), (x, y, 7.05), .09, 10),
                between((x, y + .72, 5.9), (x, y, 7.05), .09, 10),
                between((x, y, 3.2), (x, y, 6.8), .07, 8),
            ))
    # The upper pair sits above the whole narthex ridge, not partly
    # behind its roof. Both instances come from one measured pointed donor.
    for centre in metric.TOWER_LANCET['centres_x_m']:
        pane,frame=metric.tower_lancet_geometry(centre,cylinder,between)
        glass.append(kit.scaled(pane,(1/metric.EXTERIOR_SCALE,)*3))
        tracery.append(kit.scaled(frame,(1/metric.EXTERIOR_SCALE,)*3))
    add_part(asset, "EXT_StainedGlass", merge(*glass), "stained_lancet_windows", "GlassWarm", materials, .7)
    add_part(asset, "EXT_LancetTracery", merge(*tracery), "pointed_window_stone_tracery", "Stone", materials, .55)

    rose = [kit.scaled(metric.rose_glass_geometry(cylinder),
                (1/metric.EXTERIOR_SCALE,)*3),
            vertical_torus((0, -22.02, 9.0),
                metric.ROSE_EXTERIOR_RIM['mean_radius_m']/metric.EXTERIOR_SCALE,
                metric.ROSE_EXTERIOR_RIM['tube_radius_m']/metric.EXTERIOR_SCALE,32,10),
            vertical_torus((0, -22.04, 9.0), 1.35, .12, 24, 8)]
    rose_frame = []
    for index in range(16):
        angle = math.tau * index / 16
        rose_frame.append(between(
            (math.cos(angle) * .4, -22.08, 9.0 + math.sin(angle) * .4),
            (math.cos(angle) * metric.ROSE_EXTERIOR_RIM['spoke_outer_radius_m']/metric.EXTERIOR_SCALE,
             -22.08, 9.0 + math.sin(angle) * metric.ROSE_EXTERIOR_RIM['spoke_outer_radius_m']/metric.EXTERIOR_SCALE),
            .07, 8))
    add_part(asset, "EXT_RoseGlass", rose[0], "west_rose_stained_glass", "GlassWarm", materials, .5)
    add_part(asset, "EXT_RoseTracery", merge(*rose[1:], *rose_frame), "west_rose_stone_tracery", "Stone", materials, .45)

    trim = [kit.chamfered_box((side*.28/metric.EXTERIOR_SCALE,-22.15,(1.06-.08)/metric.EXTERIOR_SCALE),
                (.055/metric.EXTERIOR_SCALE,.035/metric.EXTERIOR_SCALE,.24/metric.EXTERIOR_SCALE),.008)
            for side in (-1,1)]
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
    lamp_x, lamp_y, lamp_h = LAMP_POSITION
    scale = metric.EXTERIOR_SCALE
    trim.extend((
        kit.chamfered_box((0,-22.05-.30/scale,(3.12-.08)/scale),
            (.07/scale,.36/scale,.07/scale),.006/scale),
        kit.chamfered_box((lamp_x,lamp_y,(3.105-.08)/scale),
            (.28/scale,.28/scale,.04/scale),.008/scale),
    ))
    add_part(asset, "EXT_DoorLampWarm", kit.chamfered_box(LAMP_POSITION,
        (.20/scale,.20/scale,.24/scale),.008/scale),
        "always_lit_west_entrance_lamp", "GlassWarm", materials, .25/scale)
    add_part(asset, "EXT_CornicesAndRoofSeams", merge(*trim), "gothic_cornices_pilasters_and_roof_seams", "Iron", materials, .65)


def build_interior(asset: AssetBuild, materials: dict[str, bpy.types.Material]) -> None:
    parts, audited = metric.geometry({
        "cylinder": cylinder, "between": between,
        "sloped_slab": sloped_slab, "cross_geometry": cross_geometry,
        "cone": cone, "vertical_torus": vertical_torus,
    })
    asset.layout_geometry.update(audited)
    for name, (slot, role, geometry) in parts.items():
        # Projection remains in actual metres. Atlas panels retain isolated
        # cells, while wood/stone/plaster keep an explicit physical tile pitch.
        pitch = {"Wood": .75, "Floor": 1.25, "Stone": .75,
                 "Iron": .40, "Gold": .35, "Plaster": 1.7}.get(slot, .55)
        add_part(asset, name, geometry, role, slot, materials, pitch)


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
        expected_min_y = -22.05 - THRESHOLD_DEPTH
        if not (22.95 <= dimensions.x <= 23.05 and
                abs(bounds_min.y - expected_min_y) < .0001 and
                abs(bounds_min.z + FOUNDATION_DEPTH) < .0001 and
                abs(bounds_max.z - HEIGHT) < .0001 and
                abs(bounds_max.y - 22.1) < .001):
            problems.append(f"exterior body/threshold/foundation bounds drifted: {tuple(bounds_min)}..{tuple(bounds_max)}")
        support = _shell_part(asset, "EXT_Foundation")
        support_top = max(v.co.z for v in support.obj.data.vertices)
        if abs(support_top) > .00001:
            problems.append("foundation top must meet local zero / world ground +.08 m")
        # Read back the ramp corners: front has the forecourt's datum;
        # inside has the exact authoring-root floor and no extra step.
        for side in (-1,1):
            for depth,top in ((-22.05-THRESHOLD_DEPTH,THRESHOLD_OUTER_TOP),(-22.05,0.0)):
                target=Vector((side*THRESHOLD_WIDTH*.5,depth,top))
                if not any((v.co-target).length < .00001 for v in support.obj.data.vertices):
                    problems.append(f"threshold corner misses reserved paving extent/datum {tuple(target)}")
        # Prove the LOWER contact separately from the upper enclosure:
        # every actual body/wall base vertex must be carried by masonry
        # whose connected solid itself reaches the foundation datum.
        masonry = _shell_part(asset,"EXT_StoneButtresses")
        body = _shell_part(asset,"EXT_BasilicaMasses")
        bearing_faces=[]
        for component in polygon_components(masonry.obj.data):
            indices={i for index in component for i in masonry.obj.data.polygons[index].vertices}
            minimum=min(masonry.obj.data.vertices[i].co.z for i in indices)
            if abs(minimum) < .00001:
                bearing_faces.extend(masonry.obj.data.polygons[index] for index in component
                    if masonry.obj.data.polygons[index].normal.z > .5)
        unsupported=[]
        for vertex in body.obj.data.vertices:
            point=body.obj.matrix_world @ vertex.co
            if abs(point.z-.325)>.00001: continue
            if not any(max(masonry.obj.data.vertices[i].co.z for i in face.vertices)>=point.z-.00001
                       and _polygon_covers_column(masonry.obj,face,point.x,point.y)
                       for face in bearing_faces):
                unsupported.append(tuple(round(v,5) for v in point))
        if unsupported:
            problems.append(f"wall/mass lower vertices float above grounded masonry: {unsupported[:8]}")
        lamp = _shell_part(asset,"EXT_DoorLampWarm")
        if lamp is None:
            problems.append("authored door lamp is missing")
        elif (lamp.obj.location-Vector((0,0,0))).length > .00001:
            problems.append("door lamp geometry must remain in the shared root frame")
        if not 4500 <= triangle_count <= 12000:
            problems.append(f"exterior triangle budget is {triangle_count}, expected 4500-12000")
        if len(asset.parts) > 18:
            problems.append(f"exterior renderer budget is {len(asset.parts)}, maximum 18")
    else:
        if not 7000 <= triangle_count <= 22000:
            problems.append(f"interior triangle budget is {triangle_count}, expected 7000-22000")
        if len(asset.parts) > 24:
            problems.append(f"interior renderer budget is {len(asset.parts)}, maximum 24")
        if bounds_min.y > -12.0 or bounds_max.y < 11.6 or bounds_min.x > -5.9 or bounds_max.x < 5.9:
            problems.append("interior shell lost its measured metric footprint")
        validate_audited_layout_geometry(asset, problems)
        validate_lancet_apertures(asset, problems)
        validate_interior_is_sealed_above(asset, problems)
        walk_min, walk_max = ((-2.5, -8.7, .05), (2.5, -8.4, 2.2))
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
    validate_rose_aperture(asset, problems)
    if asset.key == "Exterior":
        validate_upper_lancet_clearances(asset, problems)
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
    shell = _shell_part(asset, "INT_PlasteredShell")
    if shell is None:
        problems.append("metric shell is missing")
        return
    # A real aperture has no masonry in the centre ray through both wall
    # leaves; checking polygon AABBs catches a hidden solid wall just as it
    # caught the first version's decorative panes glued onto an uncut slab.
    for side in (-1, 1):
        for depth in metric.WINDOW_DEPTHS:
            low = (side * 5.9 - .2, depth - .15, metric.WINDOW_SILL + .1)
            high = (side * 5.9 + .2, depth + .15, metric.WINDOW_HEAD - .1)
            if any(polygon_aabb_intersects(shell.obj, p, low, high)
                   for p in shell.obj.data.polygons):
                problems.append(f"metric lancet at {side}, {depth} is obstructed")


def validate_rose_aperture(asset: AssetBuild, problems: list[str]) -> None:
    """Measure glass and prove a round opening through both actual shells."""
    from mathutils.geometry import intersect_ray_tri
    scale=1/metric.EXTERIOR_SCALE if asset.key=="Exterior" else 1.0
    masonry_names=("EXT_BasilicaMasses","EXT_GabledRoofs") if asset.key=="Exterior" else ("INT_PlasteredShell",)
    masonry=[_shell_part(asset,name) for name in masonry_names]
    for radial in (0.0,.35,.65,.96):
        for index in range(16):
            angle=math.tau*index/16
            x=metric.ROSE_RADIUS*radial*math.cos(angle)
            h=metric.ROSE_CENTER[2]+metric.ROSE_RADIUS*radial*math.sin(angle)
            origin=Vector((x*scale,(metric.ROSE_CENTER[1]-.35)*scale,h*scale))
            for part in masonry:
                part.obj.data.calc_loop_triangles()
                for triangle in part.obj.data.loop_triangles:
                    vertices=[part.obj.matrix_world @ part.obj.data.vertices[i].co for i in triangle.vertices]
                    hit=intersect_ray_tri(*vertices,Vector((0,1,0)),origin,True)
                    if hit is not None and 0<=(hit-origin).y<=.80*scale:
                        problems.append(f"{part.obj.name} blocks the shared round rose at {round(x,4)}, {round(h,4)}")
                        return
    # The wall still exists immediately beyond each cardinal opening edge.
    shell=masonry[0]
    for x,h in ((metric.ROSE_RADIUS+.08,4.95),(-metric.ROSE_RADIUS-.08,4.95),
                (0,4.95+metric.ROSE_RADIUS+.08),(0,4.95-metric.ROSE_RADIUS-.08)):
        if not point_inside_authored_solid(shell.obj,(x*scale,-12.075*scale,h*scale)):
            problems.append(f"rose lost surrounding masonry at {x}, {h}")
    glass=_shell_part(asset,"EXT_RoseGlass" if asset.key=="Exterior" else "INT_StainedGlassNorth")
    roses=[]
    for component in polygon_components(glass.obj.data):
        indices={i for p in component for i in glass.obj.data.polygons[p].vertices}
        points=[glass.obj.matrix_world @ glass.obj.data.vertices[i].co for i in indices]
        minimum=Vector(tuple(min(p[a] for p in points) for a in range(3)))
        maximum=Vector(tuple(max(p[a] for p in points) for a in range(3)))
        centre=(minimum+maximum)*.5
        if abs(centre.y/scale-metric.ROSE_CENTER[1])<.0001:
            roses.append((centre,(maximum.x-minimum.x)*.5/scale,(maximum.z-minimum.z)*.5/scale))
    if len(roses)!=1 or (roses[0][0]/scale-Vector(metric.ROSE_CENTER)).length>.0001 or \
            abs(roses[0][1]-metric.ROSE_RADIUS)>.0001 or abs(roses[0][2]-metric.ROSE_RADIUS)>.0001:
        problems.append("actual round glass differs from shared rose centre/radius anchors")
    if asset.key=="Interior":
        # These were the two seams a raised pocket could leave open: its
        # longitudinal side and its junction with the lower vault cap.
        for contact in ((1.8,-10.3,6.50),(-1.8,-10.3,6.50),(1.6,-8.55,6.55),(-1.6,-8.55,6.55)):
            if not point_inside_authored_solid(shell.obj,contact):
                problems.append(f"raised rose ceiling pocket is open at {contact}")


def _component_points(obj, component):
    indices={i for p in component for i in obj.data.polygons[p].vertices}
    return [obj.matrix_world @ obj.data.vertices[i].co for i in indices]


def _measured_triangle_mesh(obj, polygons=None):
    from mathutils.bvhtree import BVHTree
    obj.data.calc_loop_triangles()
    selected=set(polygons) if polygons is not None else None
    vertices=[obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    triangles=[tuple(t.vertices) for t in obj.data.loop_triangles
               if selected is None or t.polygon_index in selected]
    indices=sorted({i for triangle in triangles for i in triangle})
    edges=sorted({tuple(sorted((a,b))) for triangle in triangles
                  for a,b in zip(triangle,triangle[1:]+triangle[:1])})
    return vertices,indices,edges,BVHTree.FromPolygons(vertices,triangles,all_triangles=True)


def _segment_distance(a,b,c,d):
    u,v,r=b-a,d-c,a-c
    aa,ee,ff=u.dot(u),v.dot(v),v.dot(r)
    cc,bb=u.dot(r),u.dot(v)
    denom=aa*ee-bb*bb
    clamp=lambda value:max(0.0,min(1.0,value))
    ss=clamp((bb*ff-cc*ee)/denom) if abs(denom)>1e-12 else 0.0
    tt=(bb*ss+ff)/ee
    if tt<0: tt=0.0;ss=clamp(-cc/aa)
    elif tt>1: tt=1.0;ss=clamp((bb-cc)/aa)
    return ((a+u*ss)-(c+v*tt)).length


def _triangle_surface_clearance(detail,roof):
    """Closed triangle surface separation: vertex/face and edge/edge cases."""
    av,ai,ae,abvh=detail;bv,bi,be,bbvh=roof
    distance=min([bbvh.find_nearest(av[i])[3] for i in ai]+
                 [abvh.find_nearest(bv[i])[3] for i in bi])
    intersections=0
    for vertices,edges,other in ((av,ae,bbvh),(bv,be,abvh)):
        for start,end in edges:
            delta=vertices[end]-vertices[start];length=delta.length
            if length<1e-9: continue
            hit=other.ray_cast(vertices[start],delta/length,length)
            if hit[0] is not None: intersections+=1
    # Test edge distances only where their AABB lower bound can improve the
    # current exact vertex/triangle distance. No whole-part AABB is proof.
    for i,j in ae:
        a,b=av[i],av[j]
        for k,l in be:
            c,d=bv[k],bv[l]
            lower=sum(max(0.0,min(a[q],b[q])-max(c[q],d[q]),
                          min(c[q],d[q])-max(a[q],b[q]))**2 for q in range(3))
            if lower<distance*distance:
                distance=min(distance,_segment_distance(a,b,c,d))
    return distance,intersections


def validate_upper_lancet_clearances(asset: AssetBuild, problems: list[str]) -> None:
    from mathutils.geometry import intersect_ray_tri
    scale=metric.EXTERIOR_SCALE;data=metric.TOWER_LANCET
    body=_shell_part(asset,"EXT_BasilicaMasses")
    glass=_shell_part(asset,"EXT_StainedGlass")
    frame=_shell_part(asset,"EXT_LancetTracery")
    rose=_shell_part(asset,"EXT_RoseTracery")
    roof=_shell_part(asset,"EXT_GabledRoofs")
    roof_mesh=_measured_triangle_mesh(roof.obj)
    roof_top=max(v.z for v in roof_mesh[0])*scale
    body.obj.data.calc_loop_triangles()
    checks=[]
    for centre in data['centres_x_m']:
        # Direct rays traverse the actual .30 m wall/reveals. Upper samples
        # narrow with the pointed head, so a rectangular hole cannot pass.
        for height in (9.20,9.60,9.90,10.10,10.25):
            half=data['clear_width_m']/2
            if height>data['shoulder_height_m']:
                half*=((data['tip_height_m']-height)/
                       (data['tip_height_m']-data['shoulder_height_m']))
            for fraction in (-.70,0,.70):
                origin=Vector(((centre+fraction*half)/scale,-12.12/scale,height/scale))
                for triangle in body.obj.data.loop_triangles:
                    points=[body.obj.matrix_world @ body.obj.data.vertices[i].co for i in triangle.vertices]
                    hit=intersect_ray_tri(*points,Vector((0,1,0)),origin,True)
                    if hit is not None and 0<=(hit-origin).y<=.45/scale:
                        problems.append(f"upper lancet {centre} aperture is blocked at {height}")
                        break
        for x,h in ((centre-data['clear_width_m']/2-.04,9.60),
                    (centre+data['clear_width_m']/2+.04,9.60),
                    (centre,data['sill_height_m']-.04),(centre,data['tip_height_m']+.04)):
            if not point_inside_authored_solid(body.obj,(x/scale,data['wall_centre_y_m']/scale,h/scale)):
                problems.append(f"upper lancet {centre} lost its finite masonry reveal at {x}, {h}")
        glass_components=[];frame_components=[]
        for part,target in ((glass,glass_components),(frame,frame_components)):
            for component in polygon_components(part.obj.data):
                points=_component_points(part.obj,component)
                low=min(v.z for v in points)*scale
                mid=(min(v.x for v in points)+max(v.x for v in points))*.5*scale
                if low>=8.99 and abs(mid-centre)<.50: target.append(component)
        if len(glass_components)!=1 or len(frame_components)!=6:
            problems.append(f"upper lancet {centre} component population differs: glass {len(glass_components)}, frame {len(frame_components)}")
            continue
        pane_points=_component_points(glass.obj,glass_components[0])
        pane_min=Vector(tuple(min(v[q] for v in pane_points)*scale for q in range(3)))
        pane_max=Vector(tuple(max(v[q] for v in pane_points)*scale for q in range(3)))
        if abs((pane_max.x-pane_min.x)-data['clear_width_m'])>.0001 or \
                abs(pane_min.z-data['sill_height_m'])>.0001 or abs(pane_max.z-data['tip_height_m'])>.0001:
            problems.append(f"actual upper lancet pane differs from its shared pointed donor: {centre}")
        for name,part,components in (("Glass",glass,glass_components),("Frame",frame,frame_components)):
            polygons=[p for component in components for p in component]
            detail=_measured_triangle_mesh(part.obj,polygons)
            minimum=min(detail[0][i].z for i in detail[1])*scale
            maximum=max(detail[0][i].z for i in detail[1])*scale
            distance,hits=_triangle_surface_clearance(detail,roof_mesh)
            gap=minimum-roof_top
            if hits or gap<.14 or maximum>data['tower_top_height_m']-.25:
                problems.append(f"upper lancet {centre} {name} clips roof/tower: hits {hits}, roof gap {gap}, top {maximum}")
            checks.append(dict(detail=f"TowerLancet.{centre:+.2f}.{name}",
                roof_surface_clearance_m=stable(distance*scale),
                vertical_gap_above_all_roof_triangles_m=stable(gap),
                height_min_m=stable(minimum),height_max_m=stable(maximum),
                roof_edge_intersections=hits))
    # Guard only the actual outer ring, not unrelated facade lancets. Its
    # reduced dressed section keeps the fixed glass diameter and joins both
    # masonry and all existing spoke ends without crossing the front gable.
    components=polygon_components(rose.obj.data)
    ring=max(components,key=lambda c:max(v.x for v in _component_points(rose.obj,c))-
                                   min(v.x for v in _component_points(rose.obj,c)))
    distance,hits=_triangle_surface_clearance(_measured_triangle_mesh(rose.obj,ring),roof_mesh)
    ring_top=max(v.z for v in _component_points(rose.obj,ring))*scale
    if hits or distance*scale<.015:
        problems.append(f"outer rose rim intersects/presses into roof triangles: {hits}, clearance {distance*scale}")
    for x in (-1.53,1.53):
        point=(x/scale,-12.095/scale,4.95/scale)
        if not point_inside_authored_solid(rose.obj,point,ring) or not point_inside_authored_solid(body.obj,point):
            problems.append(f"dressed rose rim misses its facade support at {x}")
    spokes=[]
    for component in components:
        points=_component_points(rose.obj,component)
        cx=(min(v.x for v in points)+max(v.x for v in points))*.5*scale
        ch=(min(v.z for v in points)+max(v.z for v in points))*.5*scale-4.95
        if math.hypot(cx,ch)>.4:
            spokes.append(component)
            angle=math.atan2(ch,cx)
            point=(1.51*math.cos(angle)/scale,-12.126/scale,(4.95+1.51*math.sin(angle))/scale)
            if not point_inside_authored_solid(rose.obj,point,ring) or not point_inside_authored_solid(rose.obj,point,component):
                problems.append("rose spoke ends do not join the dressed outer rim")
    if len(spokes)!=16: problems.append("rose lost its sixteen connected spokes")
    checks.append(dict(detail="Rose.OuterRing",roof_surface_clearance_m=stable(distance*scale),
        height_max_m=stable(ring_top),roof_edge_intersections=hits,attached_spoke_count=len(spokes)))
    asset.clearance_reports=checks


def point_inside_authored_solid(obj, point, only_polygons=None) -> bool:
    """Actual closed-component union, using triangle crossings per solid.

    Grouped render meshes intentionally contain overlapping mouldings and
    wall runs. One parity over the entire batch would subtract overlaps;
    membership is the union of each independently closed component.
    """
    from mathutils.geometry import intersect_ray_tri
    origin=Vector(point)
    direction=Vector((.813,.317,.489)).normalized()
    obj.data.calc_loop_triangles()
    for component in ([only_polygons] if only_polygons is not None else polygon_components(obj.data)):
        polygons=set(component)
        indices={i for index in component for i in obj.data.polygons[index].vertices}
        points=[obj.matrix_world @ obj.data.vertices[i].co for i in indices]
        if any(origin[axis]<min(p[axis] for p in points)-1e-6 or
               origin[axis]>max(p[axis] for p in points)+1e-6 for axis in range(3)):
            continue
        hits=[]
        for triangle in obj.data.loop_triangles:
            if triangle.polygon_index not in polygons: continue
            vertices=[obj.matrix_world @ obj.data.vertices[i].co for i in triangle.vertices]
            hit=intersect_ray_tri(*vertices,direction,origin,True)
            if hit is not None:
                distance=(hit-origin).dot(direction)
                if distance>1e-6 and not any(abs(distance-old)<1e-5 for old in hits):
                    hits.append(distance)
        if len(hits)%2==1: return True
    return False


def validate_interior_is_sealed_above(asset: AssetBuild, problems: list[str]) -> None:
    roof_parts = [_shell_part(asset, n) for n in ("INT_PlasteredShell", "INT_RibbedVault")]
    candidates = [(part.obj, polygon) for part in roof_parts if part is not None
                  for polygon in part.obj.data.polygons
                  if min((part.obj.matrix_world @ part.obj.data.vertices[i].co).z
                         for i in polygon.vertices) > 3.0]
    columns = [(x, y) for x in (-5.5, -4.5, -3, 0, 3, 4.5, 5.5)
               for y in (-8.0, -4, 0, 4, 8.9)]
    columns += [(x,y) for x in (-4.2,0,4.2) for y in (-11.5,-9.0)]
    columns += [(x,10.5) for x in (-3,0,3)]
    for x,y in columns:
        covers = sum(_polygon_covers_column(obj, poly, x, y) for obj,poly in candidates)
        if covers < 2:
            problems.append(f"metric roof open or single-sided over {x}, {y}: {covers}")
    # Test actual triangle solids at deliberately interior contact points:
    # cap -> overhead bearing -> spring rib -> closed vault. This catches
    # a pier that passes its fixture bounds while supporting only air.
    piers=_shell_part(asset,"INT_NavePiers")
    capitals=_shell_part(asset,"INT_PierCapitals")
    ribs=_shell_part(asset,"INT_VaultRibs")
    vault=_shell_part(asset,"INT_RibbedVault")
    for bearing in metric.OVERHEAD_BEARINGS:
        pairs=((piers,capitals,bearing['cap_contact_xyz']),
               (capitals,ribs,bearing['rib_contact_xyz']),
               (ribs,vault,bearing['vault_contact_xyz']))
        for first,second,point in pairs:
            if not point_inside_authored_solid(first.obj,point) or not point_inside_authored_solid(second.obj,point):
                problems.append(f"{bearing['id']} misses actual {first.obj.name} -> {second.obj.name} contact at {point}")
    wax=_shell_part(asset,"INT_VotiveCandles")
    components=polygon_components(wax.obj.data) if wax is not None else []
    expected=metric.candle_contacts()
    if len(components)!=len(expected):
        problems.append(f"passive wax count {len(components)} differs from contacts {len(expected)}")
    for component,contact in zip(components,expected):
        indices={i for p in component for i in wax.obj.data.polygons[p].vertices}
        vertices=[wax.obj.data.vertices[i].co for i in indices]
        centre=((min(p.x for p in vertices)+max(p.x for p in vertices))/2,
                (min(p.y for p in vertices)+max(p.y for p in vertices))/2,
                max(p.z for p in vertices))
        if max(abs(a-b) for a,b in zip(centre,contact))>.0001:
            problems.append(f"passive wax wick {centre} differs from shared flame contact {contact}")
    for name,pos in metric.CORONAS.items():
        top=metric.ANCHORS['ANCHOR_Interior.CoronaChain.'+name][0][2]
        expected_top=3.0 if name=='Narthex' else 7.3
        if abs(top-expected_top)>.00001:
            problems.append(f"{name} corona chain misses its ceiling/loft support")
    escaped=[]
    for part in asset.parts:
        for vertex in part.obj.data.vertices:
            point=part.obj.matrix_world @ vertex.co
            if point.z > .01 and point.z > metric.exterior_roof_height(point.x,point.y)+.025:
                escaped.append((part.obj.name,tuple(round(v,4) for v in point),
                                round(metric.exterior_roof_height(point.x,point.y),4)))
                break
    if escaped:
        problems.append(f"interior vertices escape placed exterior envelope: {escaped}")
    # Contract verifies all independent closed solids are wound outward. The
    # source-to-Unity conversion is handled by FBX and the known wrapper,
    # rather than a manual reflection that would reverse the face winding.
    for part in asset.parts:
        for component in polygon_components(part.obj.data):
            volume = 0.0
            for index in component:
                polygon = part.obj.data.polygons[index]
                vertices = [part.obj.data.vertices[i].co for i in polygon.vertices]
                for j in range(1,len(vertices)-1):
                    volume += vertices[0].dot(vertices[j].cross(vertices[j+1])) / 6
            if volume < -1e-5:
                problems.append(f"{part.obj.name} contains inverted solid volume {volume}")
                break


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
    payload["dimensions_m"] = ({"width": WIDTH, "length": LENGTH, "height": HEIGHT}
        if asset.key == "Exterior" else {"width": metric.ROOM_WIDTH,
            "length": metric.ROOM_LENGTH, "height": 7.55})
    payload["door_opening_m"] = ({"width": DOOR_WIDTH, "height": DOOR_HEIGHT}
        if asset.key == "Exterior" else {"width": metric.DOOR_WIDTH,
            "height": metric.DOOR_HEIGHT})
    if asset.key == "Exterior":
        payload["upper_lancet_contract"] = metric.TOWER_LANCET
        payload["rose_outer_rim_contract"] = metric.ROSE_EXTERIOR_RIM
        payload["roof_clearance_checks"] = asset.clearance_reports
    if asset.key == "Interior":
        payload["layout_contract"] = serialized_layout_contracts()
        payload["overhead_trim_contract"] = metric.OVERHEAD_BEARINGS
    return payload


def write_manifest(path: Path, result: BuildResult, reports: dict[str, AssetReport], textures: list[dict[str, str]], signature: str) -> None:
    payload = {
        "generator": "tools/build-church-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "dimensions_m": {"width": WIDTH, "length": LENGTH, "height": HEIGHT},
        "shell_contract": metric.SHELL,
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
