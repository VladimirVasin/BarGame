#!/usr/bin/env python3
"""Build and validate the deterministic City building prototype catalog.

The FBX is intentionally passive: it contains only Empty roots and mesh
children, with no materials, colliders, lights, cameras, armatures or Actions.
Source space is Blender metres (X right, +Y frontage, Z up); FBX export bakes
that frame into Unity X right, +Z forward and Y up.

Run through Blender 5 from the repository root:

    blender --background --factory-startup --python \
      tools/build-city-buildings-3d-model.py -- --validate-only

    blender --background --factory-startup --python \
      tools/build-city-buildings-3d-model.py
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

from city_building_parts import (  # noqa: E402
    PART_ROLES,
    Geometry,
    PartSpec,
    PrototypeSpec,
    box,
    build_prototypes,
    combine,
    cylinder_z,
)
from city_building_coplanarity import (  # noqa: E402
    find_axis_aligned_coplanar_overlaps,
    find_near_coplanar_visible_overlaps,
    validate_coplanarity_audit_contract,
)


GENERATOR_VERSION = "2.1.0"
DESIGN_ID = "city_buildings_prototypes_v2"
DISPLAY_NAME = "City Buildings 3D Prototype Catalog"
FBX_ASSET_PATH = "Assets/City/Models/CityBuildings3D.fbx"

DEFAULT_BLEND = ROOT / "ArtSource" / "City" / "Blender" / "CityBuildings3D.blend"
DEFAULT_PREVIEW = ROOT / "ArtSource" / "City" / "Blender" / "CityBuildings3D.png"
DEFAULT_FBX = ROOT / FBX_ASSET_PATH
DEFAULT_MANIFEST = ROOT / "Assets" / "City" / "Models" / "CityBuildings3D.json"

CATALOG_ROOT_NAME = "ROOT_CityBuildings3D"
SOURCE_COLLECTION_NAME = "SOURCE_CityBuildings3D"
PRESENTATION_COLLECTION_NAME = "PRESENTATION_CityBuildings3D"
MAX_TRIANGLES_PER_PROTOTYPE = 3500
BOUNDS_EPSILON = 1e-5
FACADE_OVERHANG_M = 0.07
ATTACHMENT_EPSILON = 1e-5
UV2_DIVISOR = 256.0
UV2_LAYER_NAME = "UV2_SlotId"
UV2_SCHEME = "u_centered_uint8"
UV2_ZERO_MEANS = "non_window_geometry"
UV0_WINDOW_SCHEME = "per_window_face_projected_0_1"
UV0_SIDE_ATLAS_SCHEME = "building_side_atlas_0_1"
UV0_FULL_FACE_SCHEME = "full_face_projected_0_1"
UV0_METRIC_SCHEME = "world_metre_projected"
SIDE_ATLAS_GUTTER = 0.004
FBX_AXIS_FORWARD = "-Z"
FBX_AXIS_UP = "+Y"
# Unity's importer bakes the FBX axis conversion into each mesh.  Keeping the
# exporter transform unbaked is essential for the two-level catalog hierarchy:
# baking here instead strands a +90-degree correction on the catalog Empty and
# leaves bare Mesh.bounds in Blender's X/Y/-Z frame.
BAKE_SPACE_TRANSFORM = False

EXPECTED_PROTOTYPES = (
    ("old-town-prototype-01", "OldTown", "FragmentedPerimeter",
     14.0, 13.5, 42.0),
    ("residential-prototype-01", "Residential", "SetbackCourtyard",
     11.5, 11.5, 40.0),
    ("industrial-prototype-01", "Industrial", "LowWideProcess",
     14.0, 13.5, 36.0),
    ("nightlife-prototype-01", "Nightlife", "TallDense",
     12.5, 12.0, 48.0),
)

PREVIEW_PALETTE = {
    "OldTown": {
        "FacadePrimary": (0.30, 0.19, 0.14, 1.0),
        "FacadeSecondary": (0.58, 0.47, 0.34, 1.0),
        "Plinth": (0.22, 0.20, 0.17, 1.0),
        "Roof": (0.095, 0.15, 0.15, 1.0),
        "Metal": (0.10, 0.11, 0.105, 1.0),
        "WindowFrame": (0.42, 0.36, 0.27, 1.0),
        "WindowGlass": (0.12, 0.26, 0.29, 1.0),
    },
    "Residential": {
        "FacadePrimary": (0.20, 0.31, 0.32, 1.0),
        "FacadeSecondary": (0.46, 0.54, 0.50, 1.0),
        "Plinth": (0.19, 0.22, 0.21, 1.0),
        "Roof": (0.09, 0.13, 0.15, 1.0),
        "Metal": (0.12, 0.16, 0.17, 1.0),
        "WindowFrame": (0.62, 0.61, 0.50, 1.0),
        "WindowGlass": (0.16, 0.34, 0.37, 1.0),
    },
    "Industrial": {
        "FacadePrimary": (0.25, 0.27, 0.25, 1.0),
        "FacadeSecondary": (0.49, 0.38, 0.16, 1.0),
        "Plinth": (0.20, 0.21, 0.20, 1.0),
        "Roof": (0.12, 0.14, 0.14, 1.0),
        "Metal": (0.18, 0.21, 0.20, 1.0),
        "WindowFrame": (0.42, 0.40, 0.29, 1.0),
        "WindowGlass": (0.18, 0.31, 0.31, 1.0),
    },
    "Nightlife": {
        "FacadePrimary": (0.19, 0.13, 0.25, 1.0),
        "FacadeSecondary": (0.55, 0.12, 0.38, 1.0),
        "Plinth": (0.12, 0.10, 0.15, 1.0),
        "Roof": (0.08, 0.09, 0.13, 1.0),
        "Metal": (0.11, 0.11, 0.16, 1.0),
        "WindowFrame": (0.45, 0.22, 0.49, 1.0),
        "WindowGlass": (0.18, 0.30, 0.48, 1.0),
    },
}


@dataclass
class BuildResult:
    source: bpy.types.Collection
    presentation: bpy.types.Collection
    catalog_root: bpy.types.Object
    prototype_roots: dict[str, bpy.types.Object]
    objects: dict[str, bpy.types.Object]


def stable(value: float) -> float:
    return round(float(value) + 0.0, 6)


def subtract(a: Sequence[float], b: Sequence[float]) -> tuple[float, float, float]:
    return a[0] - b[0], a[1] - b[1], a[2] - b[2]


def cross(a: Sequence[float], b: Sequence[float]) -> tuple[float, float, float]:
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def vector_length(value: Sequence[float]) -> float:
    return math.sqrt(sum(component * component for component in value))


def geometry_bounds(geometry: Geometry) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    if not geometry.vertices:
        return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
    return (
        tuple(min(vertex[axis] for vertex in geometry.vertices)
              for axis in range(3)),
        tuple(max(vertex[axis] for vertex in geometry.vertices)
              for axis in range(3)),
    )


def prototype_bounds(prototype: PrototypeSpec) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    vertices = tuple(vertex for part in prototype.parts
                     for vertex in part.geometry.vertices)
    return geometry_bounds(Geometry(vertices, (), ()))


def source_to_unity(value: Sequence[float]) -> list[float]:
    return [stable(value[0]), stable(value[2]), stable(value[1])]


def triangle_count(geometry: Geometry) -> int:
    return sum(len(face) - 2 for face in geometry.faces)


def prototype_triangle_count(prototype: PrototypeSpec) -> int:
    return sum(triangle_count(part.geometry) for part in prototype.parts)


def face_normal(vertices: Sequence[Sequence[float]], face: Sequence[int]) -> tuple[float, float, float]:
    origin = vertices[face[0]]
    for index in range(1, len(face) - 1):
        first = subtract(vertices[face[index]], origin)
        second = subtract(vertices[face[index + 1]], origin)
        normal = cross(first, second)
        magnitude = vector_length(normal)
        if magnitude > 1e-10:
            return tuple(value / magnitude for value in normal)
    return 0.0, 0.0, 0.0


def metric_uv0_values(
    geometry: Geometry,
    meters_per_tile: float,
) -> list[tuple[float, float]]:
    if meters_per_tile <= 0.0:
        raise ValueError("Metric UVs need a positive metre scale.")
    values: list[tuple[float, float]] = []
    for face in geometry.faces:
        normal = face_normal(geometry.vertices, face)
        dominant = max(range(3), key=lambda axis: abs(normal[axis]))
        for vertex_index in face:
            x, y, z = geometry.vertices[vertex_index]
            if dominant == 0:
                projected = (y, z)
            elif dominant == 1:
                projected = (x, z)
            else:
                projected = (x, y)
            values.append((
                stable(projected[0] / meters_per_tile + 0.5),
                stable(projected[1] / meters_per_tile + 0.5),
            ))
    return values


def clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def side_atlas_uv0_values(
    prototype: PrototypeSpec,
    geometry: Geometry,
) -> list[tuple[float, float]]:
    """Pack the four authored building sides into non-repeating columns.

    Front, rear, left and right each own one quarter of the albedo.  Vertical
    placement is measured against the full prototype height, so panel seams,
    repairs, damp and ground grime stay aligned across separate semantic
    meshes instead of restarting on every box or being tiled every few metres.
    """
    column_width = 0.25
    usable_width = column_width - SIDE_ATLAS_GUTTER * 2.0
    usable_height = 1.0 - SIDE_ATLAS_GUTTER * 2.0
    values: list[tuple[float, float]] = []
    for face in geometry.faces:
        normal = face_normal(geometry.vertices, face)
        dominant = max(range(3), key=lambda axis: abs(normal[axis]))
        for vertex_index in face:
            x, y, z = geometry.vertices[vertex_index]
            if dominant == 1:
                if normal[1] >= 0.0:
                    column = 0
                    across = x / prototype.frontage_width_m + 0.5
                else:
                    column = 1
                    across = -x / prototype.frontage_width_m + 0.5
                vertical = z / prototype.height_m
            elif dominant == 0:
                if normal[0] < 0.0:
                    column = 2
                    across = y / prototype.depth_m + 0.5
                else:
                    column = 3
                    across = -y / prototype.depth_m + 0.5
                vertical = z / prototype.height_m
            else:
                # Hidden caps remain deterministic and sample only the quiet
                # lower strip of the front column; visible roofs own a
                # dedicated metric surface instead.
                column = 0
                across = x / prototype.frontage_width_m + 0.5
                vertical = y / prototype.depth_m + 0.5

            values.append((
                stable(column * column_width + SIDE_ATLAS_GUTTER +
                       clamp01(across) * usable_width),
                stable(SIDE_ATLAS_GUTTER +
                       clamp01(vertical) * usable_height),
            ))
    return values


def per_face_uv0_values(geometry: Geometry) -> list[tuple[float, float]]:
    """Map every face independently over the full 0..1 sheet.

    WindowGlass needs a complete atlas window per pane. Plinth needs every
    authored box face to consume the complete non-repeating base treatment.
    Per-face projection preserves both contracts on combined role meshes.
    """
    values: list[tuple[float, float]] = []
    for face in geometry.faces:
        normal = face_normal(geometry.vertices, face)
        dominant = max(range(3), key=lambda axis: abs(normal[axis]))
        projected: list[tuple[float, float]] = []
        for vertex_index in face:
            x, y, z = geometry.vertices[vertex_index]
            if dominant == 0:
                projected.append((y, z))
            elif dominant == 1:
                projected.append((x, z))
            else:
                projected.append((x, y))

        low_u = min(value[0] for value in projected)
        high_u = max(value[0] for value in projected)
        low_v = min(value[1] for value in projected)
        high_v = max(value[1] for value in projected)
        span_u = max(high_u - low_u, 1e-6)
        span_v = max(high_v - low_v, 1e-6)
        values.extend(
            (stable((u - low_u) / span_u),
             stable((v - low_v) / span_v))
            for u, v in projected
        )
    return values


def uv0_values_for_part(
    prototype: PrototypeSpec,
    part: PartSpec,
) -> list[tuple[float, float]]:
    if part.uv_scheme == UV0_WINDOW_SCHEME:
        return per_face_uv0_values(part.geometry)
    if part.uv_scheme == UV0_SIDE_ATLAS_SCHEME:
        return side_atlas_uv0_values(prototype, part.geometry)
    if part.uv_scheme == UV0_FULL_FACE_SCHEME:
        return per_face_uv0_values(part.geometry)
    if part.uv_scheme == UV0_METRIC_SCHEME:
        return metric_uv0_values(part.geometry, part.meters_per_tile)
    raise ValueError(
        f"Unknown UV scheme '{part.uv_scheme}' on {part.object_name}.")


def uv0_bounds(
    prototype: PrototypeSpec,
    part: PartSpec,
) -> tuple[tuple[float, float], tuple[float, float]]:
    values = uv0_values_for_part(prototype, part)
    return (
        (min(value[0] for value in values),
         min(value[1] for value in values)),
        (max(value[0] for value in values),
         max(value[1] for value in values)),
    )


def uv2_values(geometry: Geometry) -> list[tuple[float, float]]:
    values: list[tuple[float, float]] = []
    for face, slot_id in zip(geometry.faces, geometry.face_slot_ids):
        encoded = stable((slot_id + 0.5) / UV2_DIVISOR)
        values.extend((encoded, 0.5) for _ in face)
    return values


def validate_geometry(
    prototype: PrototypeSpec,
    part: PartSpec,
    problems: list[str],
) -> None:
    geometry = part.geometry
    if not geometry.vertices or not geometry.faces:
        problems.append(f"{part.object_name} is empty")
        return
    if len(geometry.face_slot_ids) != len(geometry.faces):
        problems.append(f"{part.object_name} face/UV2 slot count differs")
    if any(not math.isfinite(value) for vertex in geometry.vertices
           for value in vertex):
        problems.append(f"{part.object_name} contains a non-finite vertex")
    for face_index, face in enumerate(geometry.faces):
        if len(face) < 3 or any(index < 0 or index >= len(geometry.vertices)
                                for index in face):
            problems.append(f"{part.object_name} face {face_index} is invalid")
            continue
        origin = geometry.vertices[face[0]]
        area_found = False
        for index in range(1, len(face) - 1):
            first = subtract(geometry.vertices[face[index]], origin)
            second = subtract(geometry.vertices[face[index + 1]], origin)
            if vector_length(cross(first, second)) > 1e-9:
                area_found = True
                break
        if not area_found:
            problems.append(f"{part.object_name} face {face_index} is degenerate")
    uv0 = uv0_values_for_part(prototype, part)
    uv2 = uv2_values(geometry)
    loop_count = sum(len(face) for face in geometry.faces)
    if len(uv0) != loop_count or len(uv2) != loop_count:
        problems.append(f"{part.object_name} has incomplete UV data")
    if part.uv_scheme != UV0_METRIC_SCHEME and any(
            value < -1e-6 or value > 1.0 + 1e-6
            for uv in uv0 for value in uv):
        problems.append(f"{part.object_name} atlas UV0 escapes [0,1]")
    if part.uv_scheme == UV0_METRIC_SCHEME:
        low_u = min(value[0] for value in uv0)
        high_u = max(value[0] for value in uv0)
        low_v = min(value[1] for value in uv0)
        high_v = max(value[1] for value in uv0)
        if max(high_u - low_u, high_v - low_v) < 0.02:
            problems.append(
                f"{part.object_name} metric UV footprint is implausibly small")
    if part.uv_scheme in {UV0_WINDOW_SCHEME, UV0_FULL_FACE_SCHEME}:
        face_label = (
            "plinth face"
            if part.uv_scheme == UV0_FULL_FACE_SCHEME
            else "window face"
        )
        cursor = 0
        for face_index, face in enumerate(geometry.faces):
            face_uv = uv0[cursor:cursor + len(face)]
            cursor += len(face)
            low_u = min(value[0] for value in face_uv)
            high_u = max(value[0] for value in face_uv)
            low_v = min(value[1] for value in face_uv)
            high_v = max(value[1] for value in face_uv)
            if (abs(low_u) > 1e-6 or abs(low_v) > 1e-6 or
                    abs(high_u - 1.0) > 1e-6 or
                    abs(high_v - 1.0) > 1e-6):
                problems.append(
                    f"{part.object_name} {face_label} {face_index} does not "
                    "span UV0 0..1")


def validate_prototypes(prototypes: Sequence[PrototypeSpec]) -> None:
    problems: list[str] = []
    expected = {record[0]: record for record in EXPECTED_PROTOTYPES}
    if len(prototypes) != len(EXPECTED_PROTOTYPES):
        problems.append(
            f"prototype count is {len(prototypes)}, expected {len(EXPECTED_PROTOTYPES)}")
    ids = [prototype.stable_id for prototype in prototypes]
    if len(ids) != len(set(ids)):
        problems.append("prototype stable IDs are not unique")
    object_names: set[str] = set()
    for prototype in prototypes:
        contract = expected.get(prototype.stable_id)
        if contract is None:
            problems.append(f"unexpected prototype {prototype.stable_id}")
            continue
        _, district, grammar, width, depth, height = contract
        actual_contract = (
            prototype.district,
            prototype.grammar,
            prototype.frontage_width_m,
            prototype.depth_m,
            prototype.height_m,
        )
        if actual_contract != (district, grammar, width, depth, height):
            problems.append(
                f"{prototype.stable_id} contract is {actual_contract}, expected "
                f"{(district, grammar, width, depth, height)}")
        actual_roles = tuple(part.role for part in prototype.parts)
        if actual_roles != PART_ROLES:
            problems.append(
                f"{prototype.stable_id} roles are {actual_roles}, expected {PART_ROLES}")
        for part in prototype.parts:
            expected_name = f"{prototype.stable_id}__{part.role}"
            if part.object_name != expected_name:
                problems.append(
                    f"{part.object_name} should be named {expected_name}")
            expected_scheme = (
                UV0_SIDE_ATLAS_SCHEME
                if part.role in {"FacadePrimary", "FacadeSecondary"}
                else UV0_FULL_FACE_SCHEME
                if part.role == "Plinth"
                else UV0_WINDOW_SCHEME
                if part.role == "WindowGlass"
                else UV0_METRIC_SCHEME
            )
            if part.surface_kind != part.role:
                problems.append(
                    f"{part.object_name} surface kind must match its role")
            if part.uv_scheme != expected_scheme:
                problems.append(
                    f"{part.object_name} uses {part.uv_scheme}, expected "
                    f"{expected_scheme}")
            if (expected_scheme == UV0_METRIC_SCHEME) != \
                    (part.meters_per_tile > 0.0):
                problems.append(
                    f"{part.object_name} has invalid metres-per-tile "
                    f"{part.meters_per_tile}")
            if part.object_name in object_names:
                problems.append(f"duplicate object name {part.object_name}")
            object_names.add(part.object_name)
            validate_geometry(prototype, part, problems)

        for overlap in find_axis_aligned_coplanar_overlaps(prototype):
            same_facing = not overlap.has_opposing_normals
            exterior_facing = (
                overlap.plane_axis != 2 or
                overlap.first_normal_sign > 0
            )
            if same_facing and exterior_facing:
                problems.append(
                    f"{prototype.stable_id} has visible coplanar overlap "
                    f"{overlap.first_role}[{overlap.first_face_index}] / "
                    f"{overlap.second_role}[{overlap.second_face_index}] "
                    f"on axis {overlap.plane_axis} at "
                    f"{stable(overlap.plane_coordinate)} "
                    f"({stable(overlap.area)} m2)")

        for overlap in find_near_coplanar_visible_overlaps(prototype):
            problems.append(
                f"{prototype.stable_id} has near-coplanar visible overlap "
                f"{overlap.first_role}[{overlap.first_face_index}] / "
                f"{overlap.second_role}[{overlap.second_face_index}] "
                f"on axis {overlap.plane_axis} at "
                f"{stable(overlap.first_plane_coordinate)} / "
                f"{stable(overlap.second_plane_coordinate)} "
                f"({stable(overlap.separation)} m apart, "
                f"{stable(overlap.area)} m2)")

        triangles = prototype_triangle_count(prototype)
        if triangles <= 0 or triangles > MAX_TRIANGLES_PER_PROTOTYPE:
            problems.append(
                f"{prototype.stable_id} has {triangles} triangles, expected "
                f"1..{MAX_TRIANGLES_PER_PROTOTYPE}")
        low, high = prototype_bounds(prototype)
        nominal_low = (-width * 0.5, -depth * 0.5, 0.0)
        nominal_high = (width * 0.5, depth * 0.5, height)
        if abs(low[2]) > BOUNDS_EPSILON or abs(high[2] - height) > BOUNDS_EPSILON:
            problems.append(
                f"{prototype.stable_id} vertical bounds are {low[2]}..{high[2]}, "
                f"expected 0..{height}")
        if any(low[axis] < nominal_low[axis] - FACADE_OVERHANG_M or
               high[axis] > nominal_high[axis] + FACADE_OVERHANG_M
               for axis in (0, 1)):
            problems.append(
                f"{prototype.stable_id} horizontal bounds {low}..{high} escape "
                f"the footprint plus {FACADE_OVERHANG_M} m facade allowance")
        if prototype.front_anchor_source != (0.0, depth * 0.5, 0.0):
            problems.append(f"{prototype.stable_id} front anchor moved")

        slot_ids = [slot.slot_id for slot in prototype.window_slots]
        if slot_ids != list(range(1, len(slot_ids) + 1)):
            problems.append(
                f"{prototype.stable_id} window slot IDs are not contiguous from 1")
        slot_contracts = [(slot.side, slot.floor, slot.bay)
                          for slot in prototype.window_slots]
        if len(slot_contracts) != len(set(slot_contracts)):
            problems.append(
                f"{prototype.stable_id} repeats a side/floor/bay window slot")
        opening_kinds = {"Window", "BalconyDoor"}
        for slot in prototype.window_slots:
            if slot.opening_kind not in opening_kinds:
                problems.append(
                    f"{prototype.stable_id} window slot {slot.slot_id} has "
                    f"unknown opening kind {slot.opening_kind!r}")

        window_by_id = {slot.slot_id: slot
                        for slot in prototype.window_slots}
        balcony_ids: set[str] = set()
        referenced_door_ids: list[int] = []
        expected_outward = {
            "Front": (0.0, 1.0, 0.0),
            "Rear": (0.0, -1.0, 0.0),
            "Left": (-1.0, 0.0, 0.0),
            "Right": (1.0, 0.0, 0.0),
        }
        for balcony in prototype.balcony_slots:
            label = f"{prototype.stable_id} balcony {balcony.stable_id!r}"
            if not balcony.stable_id or not balcony.stable_id.strip() or \
                    balcony.stable_id in balcony_ids:
                problems.append(f"{label} has an invalid or duplicate stable ID")
            else:
                balcony_ids.add(balcony.stable_id)
            if balcony.floor <= 0 or balcony.side not in expected_outward:
                problems.append(f"{label} has an invalid floor or side")

            deck_low = balcony.deck_bounds_min_source
            deck_high = balcony.deck_bounds_max_source
            dock = balcony.npc_dock_source
            outward = balcony.outward_source
            vectors = (deck_low, deck_high, dock, outward)
            if any(len(value) != 3 or
                   any(not math.isfinite(component) for component in value)
                   for value in vectors):
                problems.append(f"{label} contains non-finite vector metadata")
                continue
            if any(deck_low[axis] >= deck_high[axis]
                   for axis in range(3)):
                problems.append(f"{label} deck bounds are invalid")
                continue
            if any(deck_low[axis] < low[axis] - ATTACHMENT_EPSILON or
                   deck_high[axis] > high[axis] + ATTACHMENT_EPSILON
                   for axis in range(3)):
                problems.append(f"{label} deck escapes the authored prototype")
            if any(dock[axis] < deck_low[axis] - ATTACHMENT_EPSILON or
                   dock[axis] > deck_high[axis] + ATTACHMENT_EPSILON
                   for axis in range(3)) or \
                    abs(dock[2] - deck_high[2]) > ATTACHMENT_EPSILON:
                problems.append(
                    f"{label} NPC dock must sit inside the deck top")
            side_outward = expected_outward.get(balcony.side)
            if side_outward is None or \
                    abs(vector_length(outward) - 1.0) > ATTACHMENT_EPSILON or \
                    any(abs(outward[axis] - side_outward[axis]) >
                        ATTACHMENT_EPSILON for axis in range(3)):
                problems.append(f"{label} outward vector disagrees with its side")

            door = window_by_id.get(balcony.door_slot_id)
            referenced_door_ids.append(balcony.door_slot_id)
            if door is None or door.opening_kind != "BalconyDoor" or \
                    door.floor != balcony.floor or door.side != balcony.side:
                problems.append(f"{label} does not reference its matching door")
                continue
            if abs(
                    door.center_source[2] - door.size_m[1] * 0.5 -
                    deck_high[2]) > ATTACHMENT_EPSILON:
                problems.append(f"{label} door threshold misses the deck top")
            if not (deck_low[0] - ATTACHMENT_EPSILON <=
                    door.center_source[0] <=
                    deck_high[0] + ATTACHMENT_EPSILON and
                    deck_low[1] - ATTACHMENT_EPSILON <=
                    door.center_source[1] <=
                    deck_high[1] + ATTACHMENT_EPSILON):
                problems.append(f"{label} door is outside the deck footprint")

        declared_doors = {
            slot.slot_id for slot in prototype.window_slots
            if slot.opening_kind == "BalconyDoor"
        }
        if len(referenced_door_ids) != len(set(referenced_door_ids)) or \
                set(referenced_door_ids) != declared_doors:
            problems.append(
                f"{prototype.stable_id} balcony doors are not paired one-to-one")
        if prototype.district == "Residential":
            expected_balcony_levels = {1: 7.0, 2: 12.0, 3: 17.0, 4: 22.0}
            if len(prototype.balcony_slots) != 8:
                problems.append(
                    f"{prototype.stable_id} must have eight balcony slots")
            for floor, deck_level in expected_balcony_levels.items():
                floor_balconies = [item for item in prototype.balcony_slots
                                   if item.floor == floor]
                if len(floor_balconies) != 2 or any(
                        item.side != "Front" or
                        abs(item.deck_bounds_max_source[2] - deck_level) >
                        ATTACHMENT_EPSILON or
                        abs(item.deck_bounds_max_source[0] -
                            item.deck_bounds_min_source[0] - 2.5) >
                        ATTACHMENT_EPSILON or
                        abs(item.deck_bounds_max_source[1] -
                            item.deck_bounds_min_source[1] - 1.2) >
                        ATTACHMENT_EPSILON
                        for item in floor_balconies):
                    problems.append(
                        f"{prototype.stable_id} floor {floor} balcony layout "
                        "differs from the residential contract")
        elif prototype.balcony_slots:
            problems.append(
                f"{prototype.stable_id} non-residential prototype has balconies")
        declared_slots = set(slot_ids)
        for part in prototype.parts:
            used = set(part.geometry.face_slot_ids)
            if not used.issubset(declared_slots | {0}):
                problems.append(
                    f"{part.object_name} references undeclared UV2 slot IDs "
                    f"{sorted(used - declared_slots - {0})}")
            if part.role in {"WindowFrame", "WindowGlass"} and \
                    used != declared_slots:
                problems.append(
                    f"{part.object_name} UV2 IDs differ from window slots")

        if len(prototype.facade_attachment_bounds) != 4 or \
                tuple(item.side for item in prototype.facade_attachment_bounds) != \
                ("Front", "Rear", "Left", "Right"):
            problems.append(
                f"{prototype.stable_id} facade attachment bounds are incomplete")
        roof_low = prototype.roof_attachment_bounds_min_source
        roof_high = prototype.roof_attachment_bounds_max_source
        if any(roof_low[axis] >= roof_high[axis] for axis in range(3)):
            problems.append(
                f"{prototype.stable_id} roof attachment bounds are invalid")

    if set(ids) != set(expected):
        problems.append("prototype stable-ID set differs from the contract")
    if len(object_names) != len(EXPECTED_PROTOTYPES) * len(PART_ROLES):
        problems.append(
            f"mesh count is {len(object_names)}, expected "
            f"{len(EXPECTED_PROTOTYPES) * len(PART_ROLES)}")
    if problems:
        raise RuntimeError(
            "City building art contract violated:\n  - " +
            "\n  - ".join(problems))


def prototype_signature_record(prototype: PrototypeSpec) -> dict:
    return {
        "stable_id": prototype.stable_id,
        "district": prototype.district,
        "grammar": prototype.grammar,
        "frontage_width_m": stable(prototype.frontage_width_m),
        "depth_m": stable(prototype.depth_m),
        "height_m": stable(prototype.height_m),
        "front_anchor_source": [stable(value)
                                for value in prototype.front_anchor_source],
        "roof_attachment_bounds_min_source": [
            stable(value)
            for value in prototype.roof_attachment_bounds_min_source],
        "roof_attachment_bounds_max_source": [
            stable(value)
            for value in prototype.roof_attachment_bounds_max_source],
        "facade_attachment_bounds": [{
            "side": item.side,
            "bounds_min_source": [stable(value)
                                  for value in item.bounds_min_source],
            "bounds_max_source": [stable(value)
                                  for value in item.bounds_max_source],
        } for item in prototype.facade_attachment_bounds],
        "window_slots": [{
            "slot_id": slot.slot_id,
            "side": slot.side,
            "floor": slot.floor,
            "bay": slot.bay,
            "opening_kind": slot.opening_kind,
            "center_source": [stable(value)
                              for value in slot.center_source],
            "size_m": [stable(value) for value in slot.size_m],
            "uv2_slot_id": slot.slot_id,
        } for slot in prototype.window_slots],
        "balcony_slots": [{
            "stable_id": slot.stable_id,
            "floor": slot.floor,
            "side": slot.side,
            "door_slot_id": slot.door_slot_id,
            "deck_bounds_min_source": [
                stable(value) for value in slot.deck_bounds_min_source],
            "deck_bounds_max_source": [
                stable(value) for value in slot.deck_bounds_max_source],
            "npc_dock_source": [
                stable(value) for value in slot.npc_dock_source],
            "outward_source": [
                stable(value) for value in slot.outward_source],
        } for slot in prototype.balcony_slots],
        "parts": [{
            "object_name": part.object_name,
            "role": part.role,
            "surface_kind": part.surface_kind,
            "uv_scheme": part.uv_scheme,
            "meters_per_tile": stable(part.meters_per_tile),
            "vertices": [[stable(value) for value in vertex]
                         for vertex in part.geometry.vertices],
            "faces": [list(face) for face in part.geometry.faces],
            "face_slot_ids": list(part.geometry.face_slot_ids),
        } for part in prototype.parts],
    }


def signature_for(prototypes: Sequence[PrototypeSpec]) -> str:
    payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "fbx_asset_path": FBX_ASSET_PATH,
        "source_axes": {"right": "+X", "forward": "+Y", "up": "+Z"},
        "unity_axes": {
            "right": "+X",
            "forward": "+Z",
            "up": "+Y",
            "fbx_axis_forward": FBX_AXIS_FORWARD,
            "fbx_axis_up": FBX_AXIS_UP,
            "bake_space_transform": BAKE_SPACE_TRANSFORM,
        },
        "uv2_encoding": {
            "channel_index": 1,
            "layer_name": UV2_LAYER_NAME,
            "scheme": UV2_SCHEME,
            "divisor": UV2_DIVISOR,
            "zero_means": UV2_ZERO_MEANS,
        },
        "uv0_encoding": {
            "window_glass_scheme": UV0_WINDOW_SCHEME,
            "building_side_atlas_scheme": UV0_SIDE_ATLAS_SCHEME,
            "full_face_surface_scheme": UV0_FULL_FACE_SCHEME,
            "metric_surface_scheme": UV0_METRIC_SCHEME,
        },
        "unit_factor": 1.0,
        "origin": "footprint_center_ground",
        "scale_mode": "fixed_meters",
        "prototypes": [prototype_signature_record(prototype)
                       for prototype in prototypes],
    }
    encoded = json.dumps(
        payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def manifest_for(
    prototypes: Sequence[PrototypeSpec],
    signature: str,
) -> dict:
    records: list[dict] = []
    for prototype in prototypes:
        low, high = prototype_bounds(prototype)
        parts: list[dict] = []
        for part in prototype.parts:
            part_low, part_high = geometry_bounds(part.geometry)
            uv_low, uv_high = uv0_bounds(prototype, part)
            parts.append({
                "object_name": part.object_name,
                "role": part.role,
                "surface_kind": part.surface_kind,
                "uv_scheme": part.uv_scheme,
                "meters_per_tile": stable(part.meters_per_tile),
                "vertices": len(part.geometry.vertices),
                "triangles": triangle_count(part.geometry),
                "bounds_min_source": [stable(value) for value in part_low],
                "bounds_max_source": [stable(value) for value in part_high],
                "bounds_min_unity": source_to_unity(part_low),
                "bounds_max_unity": source_to_unity(part_high),
                "uv0_min": [stable(value) for value in uv_low],
                "uv0_max": [stable(value) for value in uv_high],
                "uv2_slot_ids": sorted(set(part.geometry.face_slot_ids)),
            })
        records.append({
            "stable_id": prototype.stable_id,
            "district": prototype.district,
            "grammar": prototype.grammar,
            "root_name": f"ROOT_{prototype.stable_id}",
            "frontage_width_m": stable(prototype.frontage_width_m),
            "depth_m": stable(prototype.depth_m),
            "height_m": stable(prototype.height_m),
            "triangle_count": prototype_triangle_count(prototype),
            "bounds_min_source": [stable(value) for value in low],
            "bounds_max_source": [stable(value) for value in high],
            "bounds_min_unity": source_to_unity(low),
            "bounds_max_unity": source_to_unity(high),
            "front_anchor": {
                "position_source": [stable(value)
                                    for value in prototype.front_anchor_source],
                "forward_source": [0.0, 1.0, 0.0],
                "position_unity": source_to_unity(
                    prototype.front_anchor_source),
                "forward_unity": [0.0, 0.0, 1.0],
            },
            "roof_attachment_bounds_min_source": [
                stable(value)
                for value in prototype.roof_attachment_bounds_min_source],
            "roof_attachment_bounds_max_source": [
                stable(value)
                for value in prototype.roof_attachment_bounds_max_source],
            "facade_attachment_bounds": [{
                "side": item.side,
                "bounds_min_source": [stable(value)
                                      for value in item.bounds_min_source],
                "bounds_max_source": [stable(value)
                                      for value in item.bounds_max_source],
            } for item in prototype.facade_attachment_bounds],
            "window_slots": [{
                "slot_id": slot.slot_id,
                "side": slot.side,
                "floor": slot.floor,
                "bay": slot.bay,
                "opening_kind": slot.opening_kind,
                "center_source": [stable(value)
                                  for value in slot.center_source],
                "size_m": [stable(value) for value in slot.size_m],
                "uv2_slot_id": slot.slot_id,
            } for slot in prototype.window_slots],
            "balcony_slots": [{
                "stable_id": slot.stable_id,
                "floor": slot.floor,
                "side": slot.side,
                "door_slot_id": slot.door_slot_id,
                "deck_bounds_min_source": [
                    stable(value) for value in slot.deck_bounds_min_source],
                "deck_bounds_max_source": [
                    stable(value) for value in slot.deck_bounds_max_source],
                "npc_dock_source": [
                    stable(value) for value in slot.npc_dock_source],
                "outward_source": [
                    stable(value) for value in slot.outward_source],
            } for slot in prototype.balcony_slots],
            "parts": parts,
        })
    mesh_count = sum(len(prototype.parts) for prototype in prototypes)
    total_triangles = sum(prototype_triangle_count(prototype)
                          for prototype in prototypes)
    return {
        "generator": "tools/build-city-buildings-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "fbx_asset_path": FBX_ASSET_PATH,
        "source_axes": {
            "right": "+X", "forward": "+Y", "up": "+Z",
        },
        "unity_axes": {
            "right": "+X", "forward": "+Z", "up": "+Y",
            "fbx_axis_forward": FBX_AXIS_FORWARD,
            "fbx_axis_up": FBX_AXIS_UP,
            "bake_space_transform": BAKE_SPACE_TRANSFORM,
        },
        "unit_factor": 1.0,
        "root_contract": {
            "catalog_root": CATALOG_ROOT_NAME,
            "origin": "footprint_center_ground",
            "scale_mode": "fixed_meters",
            "source_ground_axis": "Z",
            "source_ground_value": 0.0,
            "unity_ground_axis": "Y",
            "unity_ground_value": 0.0,
            "source_forward_axis": "+Y",
            "unity_forward_axis": "+Z",
        },
        "passive": {
            "colliders": False,
            "lights": False,
            "cameras": False,
            "materials": False,
            "animation_count": 0,
        },
        "uv2_encoding": {
            "channel_index": 1,
            "layer_name": UV2_LAYER_NAME,
            "scheme": UV2_SCHEME,
            "divisor": UV2_DIVISOR,
            "zero_means": UV2_ZERO_MEANS,
        },
        "uv0_encoding": {
            "window_glass_scheme": UV0_WINDOW_SCHEME,
            "building_side_atlas_scheme": UV0_SIDE_ATLAS_SCHEME,
            "full_face_surface_scheme": UV0_FULL_FACE_SCHEME,
            "metric_surface_scheme": UV0_METRIC_SCHEME,
        },
        "prototype_count": len(prototypes),
        "mesh_count": mesh_count,
        "triangle_count": total_triangles,
        "prototypes": records,
        "build_signature": signature,
    }


def reset_scene() -> tuple[bpy.types.Collection, bpy.types.Collection]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    source = bpy.data.collections.new(SOURCE_COLLECTION_NAME)
    presentation = bpy.data.collections.new(PRESENTATION_COLLECTION_NAME)
    scene.collection.children.link(source)
    scene.collection.children.link(presentation)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 2200
    scene.render.resolution_y = 1100
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = "RGBA"
    world = bpy.data.worlds.new("CityBuildings3D_PreviewWorld")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.035, 0.043, 0.052, 1.0)
        background.inputs["Strength"].default_value = 0.28
    scene.world = world
    return source, presentation


def assign_uv_layers(
    mesh: bpy.types.Mesh,
    prototype: PrototypeSpec,
    part: PartSpec,
) -> None:
    uv0 = mesh.uv_layers.new(name="UV0")
    uv2 = mesh.uv_layers.new(name=UV2_LAYER_NAME)
    values0 = uv0_values_for_part(prototype, part)
    values2 = uv2_values(part.geometry)
    cursor = 0
    for polygon in mesh.polygons:
        for loop_index in polygon.loop_indices:
            uv0.data[loop_index].uv = values0[cursor]
            uv2.data[loop_index].uv = values2[cursor]
            cursor += 1
    if cursor != len(values0) or cursor != len(values2):
        raise RuntimeError(f"UV loop drift on {mesh.name}.")


def create_part_object(
    prototype: PrototypeSpec,
    part: PartSpec,
    root: bpy.types.Object,
    source: bpy.types.Collection,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{part.object_name}_Mesh")
    mesh.from_pydata(part.geometry.vertices, [], part.geometry.faces)
    mesh.update(calc_edges=True)
    assign_uv_layers(mesh, prototype, part)
    obj = bpy.data.objects.new(part.object_name, mesh)
    source.objects.link(obj)
    obj.parent = root
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    obj["bp_stable_id"] = prototype.stable_id
    obj["bp_district"] = prototype.district
    obj["bp_grammar"] = prototype.grammar
    obj["bp_part_role"] = part.role
    obj["bp_surface_kind"] = part.surface_kind
    obj["bp_uv_scheme"] = part.uv_scheme
    obj["bp_meters_per_tile"] = stable(part.meters_per_tile)
    obj["bp_scale_mode"] = "fixed_meters"
    obj["bp_source_forward_axis"] = "+Y"
    return obj


def build_scene(prototypes: Sequence[PrototypeSpec]) -> BuildResult:
    source, presentation = reset_scene()
    catalog_root = bpy.data.objects.new(CATALOG_ROOT_NAME, None)
    source.objects.link(catalog_root)
    catalog_root.empty_display_type = "PLAIN_AXES"
    catalog_root.empty_display_size = 0.45
    catalog_root["bp_design_id"] = DESIGN_ID
    catalog_root["bp_origin_contract"] = "footprint_center_ground"
    catalog_root["bp_scale_mode"] = "fixed_meters"
    catalog_root["bp_source_forward_axis"] = "+Y"
    prototype_roots: dict[str, bpy.types.Object] = {}
    objects: dict[str, bpy.types.Object] = {}
    for prototype in prototypes:
        root_name = f"ROOT_{prototype.stable_id}"
        root = bpy.data.objects.new(root_name, None)
        source.objects.link(root)
        root.parent = catalog_root
        root.location = (0.0, 0.0, 0.0)
        root.rotation_euler = (0.0, 0.0, 0.0)
        root.scale = (1.0, 1.0, 1.0)
        root.empty_display_type = "PLAIN_AXES"
        root.empty_display_size = 0.35
        root["bp_stable_id"] = prototype.stable_id
        root["bp_district"] = prototype.district
        root["bp_grammar"] = prototype.grammar
        root["bp_origin_contract"] = "footprint_center_ground"
        root["bp_frontage_width_m"] = prototype.frontage_width_m
        root["bp_depth_m"] = prototype.depth_m
        root["bp_height_m"] = prototype.height_m
        prototype_roots[prototype.stable_id] = root
        for part in prototype.parts:
            objects[part.object_name] = create_part_object(
                prototype, part, root, source)
    return BuildResult(
        source, presentation, catalog_root, prototype_roots, objects)


def validate_source_scene(
    result: BuildResult,
    prototypes: Sequence[PrototypeSpec],
) -> None:
    problems: list[str] = []
    expected_meshes = len(prototypes) * len(PART_ROLES)
    if len(result.objects) != expected_meshes:
        problems.append(
            f"source scene has {len(result.objects)} meshes, expected {expected_meshes}")
    for prototype in prototypes:
        root = result.prototype_roots[prototype.stable_id]
        if root.parent != result.catalog_root or tuple(root.location) != (0.0, 0.0, 0.0):
            problems.append(f"{root.name} moved from footprint-centre ground")
        if tuple(root.scale) != (1.0, 1.0, 1.0):
            problems.append(f"{root.name} uses non-identity scale")
        for part in prototype.parts:
            obj = result.objects[part.object_name]
            if obj.parent != root or tuple(obj.location) != (0.0, 0.0, 0.0) or \
                    tuple(obj.scale) != (1.0, 1.0, 1.0):
                problems.append(f"{obj.name} lost its root-local identity transform")
            if len(obj.data.materials) != 0:
                problems.append(f"{obj.name} source mesh gained a material")
            if len(obj.data.uv_layers) != 2 or \
                    tuple(layer.name for layer in obj.data.uv_layers) != \
                    ("UV0", UV2_LAYER_NAME):
                problems.append(f"{obj.name} UV layer contract changed")
    if any(obj.type in {"LIGHT", "CAMERA", "ARMATURE"}
           for obj in result.source.objects):
        problems.append("source collection contains a Light, Camera or Armature")
    if bpy.data.actions:
        problems.append("source catalog must contain no Actions")
    if problems:
        raise RuntimeError(
            "City building Blender scene contract violated:\n  - " +
            "\n  - ".join(problems))


def preview_material(district: str, role: str) -> bpy.types.Material:
    material = bpy.data.materials.new(f"PREVIEW_CityBuildings_{district}_{role}")
    color = PREVIEW_PALETTE[district][role]
    material.diffuse_color = color
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    if shader is not None:
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Roughness"].default_value = (
            0.38 if role in {"Metal", "WindowFrame"} else 0.72)
        shader.inputs["Metallic"].default_value = (
            0.32 if role == "Metal" else 0.0)
        if role == "WindowGlass":
            shader.inputs["Roughness"].default_value = 0.22
            emission = shader.inputs.get("Emission Color") or \
                shader.inputs.get("Emission")
            if emission is not None:
                emission.default_value = tuple(
                    min(1.0, channel * 1.5) for channel in color[:3]) + (1.0,)
            strength = shader.inputs.get("Emission Strength")
            if strength is not None:
                strength.default_value = 0.45
    return material


def add_preview_stage(
    result: BuildResult,
    prototypes: Sequence[PrototypeSpec],
) -> None:
    collection = result.presentation
    ground_material = bpy.data.materials.new("PREVIEW_CityBuildings_Ground")
    ground_material.diffuse_color = (0.055, 0.065, 0.068, 1.0)
    ground_material.use_nodes = True
    ground_shader = ground_material.node_tree.nodes.get("Principled BSDF")
    if ground_shader is not None:
        ground_shader.inputs["Base Color"].default_value = \
            ground_material.diffuse_color
        ground_shader.inputs["Roughness"].default_value = 0.88
    ground_mesh = bpy.data.meshes.new("CityBuildingsPreviewGround_Mesh")
    ground_vertices = (
        (-42.0, -10.0, -0.24), (42.0, -10.0, -0.24),
        (42.0, 12.0, -0.24), (-42.0, 12.0, -0.24),
        (-42.0, -10.0, 0.0), (42.0, -10.0, 0.0),
        (42.0, 12.0, 0.0), (-42.0, 12.0, 0.0),
    )
    ground_faces = (
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    )
    ground_mesh.from_pydata(ground_vertices, [], ground_faces)
    ground = bpy.data.objects.new("CityBuildingsPreviewGround", ground_mesh)
    collection.objects.link(ground)
    ground.data.materials.append(ground_material)

    placements = (-25.5, -8.5, 8.5, 25.5)
    scale = 0.65
    materials = {
        (prototype.district, role): preview_material(prototype.district, role)
        for prototype in prototypes for role in PART_ROLES
    }
    figure_geometry = combine(
        box((-0.09, 0.0, 0.42), (0.12, 0.16, 0.84)),
        box((0.09, 0.0, 0.42), (0.12, 0.16, 0.84)),
        cylinder_z((0.0, 0.0), 0.78, 1.43, 0.19, 8),
        box((-0.245, 0.0, 1.11), (0.09, 0.13, 0.56)),
        box((0.245, 0.0, 1.11), (0.09, 0.13, 0.56)),
        cylinder_z((0.0, 0.0), 1.49, 1.75, 0.135, 8),
    )
    figure_mesh = bpy.data.meshes.new("CityBuildingsPreviewFigure_Mesh")
    figure_mesh.from_pydata(
        figure_geometry.vertices, [], figure_geometry.faces)
    figure_mesh.update(calc_edges=True)
    figure_material = bpy.data.materials.new("PREVIEW_CityBuildings_ScaleFigure")
    figure_material.diffuse_color = (1.0, 0.48, 0.055, 1.0)
    figure_material.use_nodes = True
    figure_shader = figure_material.node_tree.nodes.get("Principled BSDF")
    if figure_shader is not None:
        figure_shader.inputs["Base Color"].default_value = \
            figure_material.diffuse_color
        figure_shader.inputs["Roughness"].default_value = 0.72
        emission = figure_shader.inputs.get("Emission Color") or \
            figure_shader.inputs.get("Emission")
        if emission is not None:
            emission.default_value = (1.0, 0.16, 0.015, 1.0)
        strength = figure_shader.inputs.get("Emission Strength")
        if strength is not None:
            strength.default_value = 1.4

    label_material = bpy.data.materials.new("PREVIEW_CityBuildings_Labels")
    label_material.diffuse_color = (0.78, 0.84, 0.82, 1.0)
    label_material.use_nodes = True
    label_shader = label_material.node_tree.nodes.get("Principled BSDF")
    if label_shader is not None:
        label_shader.inputs["Base Color"].default_value = \
            label_material.diffuse_color
        emission = label_shader.inputs.get("Emission Color") or \
            label_shader.inputs.get("Emission")
        if emission is not None:
            emission.default_value = label_material.diffuse_color
        strength = label_shader.inputs.get("Emission Strength")
        if strength is not None:
            strength.default_value = 0.35

    for x, prototype in zip(placements, prototypes):
        placement = bpy.data.objects.new(
            f"PREVIEW_ROOT_{prototype.stable_id}", None)
        collection.objects.link(placement)
        placement.location = (x, 0.0, 0.0)
        placement.scale = (scale, scale, scale)
        placement.rotation_euler[2] = math.radians(-7.0 if x < 0.0 else 7.0)
        for part in prototype.parts:
            source = result.objects[part.object_name]
            duplicate = source.copy()
            duplicate.name = f"PREVIEW_{part.object_name}"
            duplicate.data = source.data.copy()
            duplicate.data.name = f"PREVIEW_{part.object_name}_Mesh"
            duplicate.data.materials.append(
                materials[(prototype.district, part.role)])
            collection.objects.link(duplicate)
            duplicate.parent = placement

        figure = bpy.data.objects.new(
            f"PREVIEW_ScaleFigure_{prototype.stable_id}", figure_mesh.copy())
        collection.objects.link(figure)
        figure.parent = placement
        figure.location = (
            prototype.frontage_width_m * 0.32,
            prototype.depth_m * 0.5 + 1.0,
            0.0,
        )
        figure.data.materials.append(figure_material)

        text_curve = bpy.data.curves.new(
            f"PREVIEW_Label_{prototype.stable_id}_Curve", type="FONT")
        text_curve.body = f"{prototype.district}\n{prototype.grammar}"
        text_curve.align_x = "CENTER"
        text_curve.align_y = "CENTER"
        text_curve.size = 0.88
        text_curve.space_line = 0.90
        text_curve.extrude = 0.012
        label = bpy.data.objects.new(
            f"PREVIEW_Label_{prototype.stable_id}", text_curve)
        collection.objects.link(label)
        label.location = (x, 9.4, 3.0)
        label.data.materials.append(label_material)

    for name, location, energy, color, size in (
        ("Key", (-34.0, 28.0, 48.0), 7200,
         (0.73, 0.83, 0.78), 18.0),
        ("Rim", (38.0, -5.0, 42.0), 5600,
         (0.28, 0.42, 0.58), 16.0),
        ("Front", (0.0, 38.0, 22.0), 4300,
         (0.90, 0.55, 0.34), 20.0),
    ):
        light_data = bpy.data.lights.new(
            f"PREVIEW_CityBuildings_{name}", "AREA")
        light_data.energy = energy
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(
            f"PREVIEW_CityBuildings_{name}", light_data)
        collection.objects.link(light)
        light.location = location
        light.rotation_euler = (
            Vector((0.0, 0.0, 15.0)) - light.location
        ).to_track_quat("-Z", "Y").to_euler()


def render_preview(
    path: Path,
    result: BuildResult,
    prototypes: Sequence[PrototypeSpec],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    add_preview_stage(result, prototypes)
    for obj in result.source.objects:
        obj.hide_render = True
    camera_data = bpy.data.cameras.new("CAM_CityBuildings3D_Preview")
    camera = bpy.data.objects.new("CAM_CityBuildings3D_Preview", camera_data)
    result.presentation.objects.link(camera)
    camera.location = (0.0, 78.0, 39.0)
    target = Vector((0.0, 0.0, 13.5))
    camera.rotation_euler = (
        target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 76.0
    bpy.context.scene.camera = camera
    for obj in result.presentation.objects:
        if obj.name.startswith("PREVIEW_Label_"):
            obj.rotation_euler = (
                camera.location - obj.location
            ).to_track_quat("Z", "Y").to_euler()
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for obj in result.source.objects:
        obj.hide_render = False


def select_source(result: BuildResult) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    result.catalog_root.select_set(True)
    for root in result.prototype_roots.values():
        root.select_set(True)
    for obj in result.objects.values():
        obj.select_set(True)
    bpy.context.view_layer.objects.active = result.catalog_root


def export_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    select_source(result)
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        axis_forward=FBX_AXIS_FORWARD,
        axis_up=FBX_AXIS_UP.removeprefix("+"),
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=BAKE_SPACE_TRANSFORM,
        add_leaf_bones=False,
        bake_anim=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_custom_props=True,
        path_mode="STRIP",
        embed_textures=False,
    )


def write_manifest(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


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


def print_report(
    heading: str,
    prototypes: Sequence[PrototypeSpec],
    signature: str,
) -> None:
    print(heading)
    for prototype in prototypes:
        print(
            f"  {prototype.stable_id}: {prototype_triangle_count(prototype)}/"
            f"{MAX_TRIANGLES_PER_PROTOTYPE} triangles, "
            f"{len(prototype.window_slots)} opening slots, "
            f"{len(prototype.balcony_slots)} balcony slots, "
            f"{prototype.frontage_width_m}x{prototype.depth_m}x"
            f"{prototype.height_m} m")
    print(f"  Meshes: {sum(len(prototype.parts) for prototype in prototypes)}")
    print(f"  Signature: {signature}")
    print("  Determinism: repeated signatures match")


def main() -> int:
    config = parse_args()
    validate_coplanarity_audit_contract()
    prototypes = build_prototypes()
    validate_prototypes(prototypes)
    signature = signature_for(prototypes)
    rerun = build_prototypes()
    validate_prototypes(rerun)
    if signature_for(rerun) != signature:
        raise RuntimeError("Non-deterministic City building signature.")

    if config.validate_only:
        print_report("CITY BUILDINGS 3D DIRECT VALIDATION OK", prototypes, signature)
        return 0

    result = build_scene(prototypes)
    validate_source_scene(result, prototypes)
    if not config.no_preview:
        render_preview(config.preview, result, prototypes)
    export_fbx(config.fbx, result)
    write_manifest(config.manifest, manifest_for(prototypes, signature))
    save_blend(config.blend)
    print_report("CITY BUILDINGS 3D BUILD OK", prototypes, signature)
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Blend: {config.blend}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
