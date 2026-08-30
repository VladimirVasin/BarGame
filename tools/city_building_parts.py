#!/usr/bin/env python3
"""Pure deterministic geometry recipes for the City building prototype catalog.

The module deliberately has no Blender dependency.  It owns the fixed-metre
source geometry and semantic window metadata consumed by
``build-city-buildings-3d-model.py``.  Source coordinates are Blender metres:
X is right, +Y is the authored frontage direction and Z is up.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Iterable, Sequence


Vec2 = tuple[float, float]
Vec3 = tuple[float, float, float]
Face = tuple[int, ...]

PART_ROLES = (
    "FacadePrimary",
    "FacadeSecondary",
    "Plinth",
    "Roof",
    "Metal",
    "WindowFrame",
    "WindowGlass",
)

_ROLE_SURFACE_CONTRACT = {
    "FacadePrimary": ("building_side_atlas_0_1", 0.0),
    "FacadeSecondary": ("building_side_atlas_0_1", 0.0),
    "Plinth": ("full_face_projected_0_1", 0.0),
    "Roof": ("world_metre_projected", 4.0),
    "Metal": ("world_metre_projected", 1.6),
    "WindowFrame": ("world_metre_projected", 0.8),
    "WindowGlass": ("per_window_face_projected_0_1", 0.0),
}


@dataclass(frozen=True)
class Geometry:
    vertices: tuple[Vec3, ...]
    faces: tuple[Face, ...]
    face_slot_ids: tuple[int, ...]


@dataclass(frozen=True)
class WindowSlot:
    slot_id: int
    side: str
    floor: int
    bay: int
    center_source: Vec3
    size_m: Vec2


@dataclass(frozen=True)
class FacadeAttachmentBounds:
    side: str
    bounds_min_source: Vec3
    bounds_max_source: Vec3


@dataclass(frozen=True)
class PartSpec:
    object_name: str
    role: str
    surface_kind: str
    uv_scheme: str
    meters_per_tile: float
    geometry: Geometry


@dataclass(frozen=True)
class PrototypeSpec:
    stable_id: str
    district: str
    grammar: str
    frontage_width_m: float
    depth_m: float
    height_m: float
    front_anchor_source: Vec3
    roof_attachment_bounds_min_source: Vec3
    roof_attachment_bounds_max_source: Vec3
    facade_attachment_bounds: tuple[FacadeAttachmentBounds, ...]
    window_slots: tuple[WindowSlot, ...]
    parts: tuple[PartSpec, ...]


def empty() -> Geometry:
    return Geometry((), (), ())


def combine(*geometries: Geometry) -> Geometry:
    vertices: list[Vec3] = []
    faces: list[Face] = []
    slot_ids: list[int] = []
    for geometry in geometries:
        offset = len(vertices)
        vertices.extend(geometry.vertices)
        faces.extend(tuple(index + offset for index in face)
                     for face in geometry.faces)
        slot_ids.extend(geometry.face_slot_ids)
    return Geometry(tuple(vertices), tuple(faces), tuple(slot_ids))


def merge(geometries: Iterable[Geometry]) -> Geometry:
    return combine(*tuple(geometries))


def box(center: Vec3, size: Vec3, slot_id: int = 0) -> Geometry:
    cx, cy, cz = center
    hx, hy, hz = (value * 0.5 for value in size)
    vertices = (
        (cx - hx, cy - hy, cz - hz),
        (cx + hx, cy - hy, cz - hz),
        (cx + hx, cy + hy, cz - hz),
        (cx - hx, cy + hy, cz - hz),
        (cx - hx, cy - hy, cz + hz),
        (cx + hx, cy - hy, cz + hz),
        (cx + hx, cy + hy, cz + hz),
        (cx - hx, cy + hy, cz + hz),
    )
    faces = (
        (0, 3, 2, 1),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7),
    )
    return Geometry(vertices, faces, (slot_id,) * len(faces))


_BOX_FACE_NAMES = (
    "Bottom",
    "Top",
    "Rear",
    "Right",
    "Front",
    "Left",
)


def box_without_faces(
    center: Vec3,
    size: Vec3,
    omitted_faces: Iterable[str],
    slot_id: int = 0,
) -> Geometry:
    """Build a box whose named flush-join faces are deliberately open.

    A joined mass needs only one face at a shared plane.  Keeping the face on
    both source boxes creates equal-depth fragments after export, because
    ``merge`` concatenates geometry rather than performing a boolean union.
    """

    omitted = frozenset(omitted_faces)
    unknown = omitted.difference(_BOX_FACE_NAMES)
    if unknown:
        raise ValueError(f"Unknown box faces: {sorted(unknown)}")

    geometry = box(center, size, slot_id)
    kept_indices = tuple(
        index for index, name in enumerate(_BOX_FACE_NAMES)
        if name not in omitted)
    return Geometry(
        geometry.vertices,
        tuple(geometry.faces[index] for index in kept_indices),
        tuple(geometry.face_slot_ids[index] for index in kept_indices))


def gable_roof(
    center_xy: Vec2,
    width: float,
    depth: float,
    eave_z: float,
    ridge_z: float,
) -> Geometry:
    cx, cy = center_xy
    left, right = cx - width * 0.5, cx + width * 0.5
    rear, front = cy - depth * 0.5, cy + depth * 0.5
    vertices = (
        (left, rear, eave_z),
        (right, rear, eave_z),
        (cx, rear, ridge_z),
        (left, front, eave_z),
        (right, front, eave_z),
        (cx, front, ridge_z),
    )
    faces = (
        (0, 3, 4, 1),
        (0, 2, 5, 3),
        (2, 1, 4, 5),
        (0, 1, 2),
        (3, 5, 4),
    )
    return Geometry(vertices, faces, (0,) * len(faces))


def shed_roof(
    center_xy: Vec2,
    width: float,
    depth: float,
    low_z: float,
    high_z: float,
    high_on_right: bool,
) -> Geometry:
    cx, cy = center_xy
    left, right = cx - width * 0.5, cx + width * 0.5
    rear, front = cy - depth * 0.5, cy + depth * 0.5
    low_top_z = low_z + 0.30
    left_top = high_z if not high_on_right else low_top_z
    right_top = high_z if high_on_right else low_top_z
    vertices = (
        (left, rear, low_z),
        (right, rear, low_z),
        (right, rear, right_top),
        (left, rear, left_top),
        (left, front, low_z),
        (right, front, low_z),
        (right, front, right_top),
        (left, front, left_top),
    )
    faces = (
        (0, 4, 5, 1),
        (3, 2, 6, 7),
        (0, 1, 2, 3),
        (4, 7, 6, 5),
        (0, 3, 7, 4),
        (1, 5, 6, 2),
    )
    return Geometry(vertices, faces, (0,) * len(faces))


def pyramid_roof(
    center_xy: Vec2,
    width: float,
    depth: float,
    eave_z: float,
    peak_z: float,
) -> Geometry:
    cx, cy = center_xy
    hx, hy = width * 0.5, depth * 0.5
    vertices = (
        (cx - hx, cy - hy, eave_z),
        (cx + hx, cy - hy, eave_z),
        (cx + hx, cy + hy, eave_z),
        (cx - hx, cy + hy, eave_z),
        (cx, cy, peak_z),
    )
    faces = (
        (0, 3, 2, 1),
        (0, 1, 4),
        (1, 2, 4),
        (2, 3, 4),
        (3, 0, 4),
    )
    return Geometry(vertices, faces, (0,) * len(faces))


def cylinder_z(
    center_xy: Vec2,
    bottom_z: float,
    top_z: float,
    radius: float,
    sides: int = 8,
    slot_id: int = 0,
) -> Geometry:
    cx, cy = center_xy
    vertices: list[Vec3] = []
    for z in (bottom_z, top_z):
        for side in range(sides):
            angle = side / sides * math.tau
            vertices.append((cx + math.cos(angle) * radius,
                             cy + math.sin(angle) * radius, z))
    faces: list[Face] = [tuple(reversed(range(sides))),
                         tuple(range(sides, sides * 2))]
    for side in range(sides):
        following = (side + 1) % sides
        faces.append((side, following, sides + following, sides + side))
    return Geometry(tuple(vertices), tuple(faces), (slot_id,) * len(faces))


def facade_panel(
    side: str,
    center: Vec3,
    width: float,
    height: float,
    outward_offset: float,
    slot_id: int,
) -> Geometry:
    x, y, z = center
    half_w, half_h = width * 0.5, height * 0.5
    if side == "Front":
        y += outward_offset
        vertices = ((x - half_w, y, z - half_h),
                    (x - half_w, y, z + half_h),
                    (x + half_w, y, z + half_h),
                    (x + half_w, y, z - half_h))
    elif side == "Rear":
        y -= outward_offset
        vertices = ((x - half_w, y, z - half_h),
                    (x + half_w, y, z - half_h),
                    (x + half_w, y, z + half_h),
                    (x - half_w, y, z + half_h))
    elif side == "Right":
        x += outward_offset
        vertices = ((x, y - half_w, z - half_h),
                    (x, y + half_w, z - half_h),
                    (x, y + half_w, z + half_h),
                    (x, y - half_w, z + half_h))
    elif side == "Left":
        x -= outward_offset
        vertices = ((x, y - half_w, z - half_h),
                    (x, y - half_w, z + half_h),
                    (x, y + half_w, z + half_h),
                    (x, y + half_w, z - half_h))
    else:
        raise ValueError(f"Unsupported facade side {side!r}.")
    return Geometry(vertices, ((0, 1, 2, 3),), (slot_id,))


def window_geometry(
    slots: Sequence[WindowSlot],
) -> tuple[Geometry, Geometry]:
    frame_parts: list[Geometry] = []
    glass_parts: list[Geometry] = []
    for slot in slots:
        width, height = slot.size_m
        bar = min(0.14, width * 0.14, height * 0.10)
        glass_parts.append(facade_panel(
            slot.side, slot.center_source, width - bar * 1.35,
            height - bar * 1.35, 0.018, slot.slot_id))
        horizontal_width = width
        vertical_height = max(0.05, height - bar * 2.0)
        if slot.side in {"Front", "Rear"}:
            cx, cy, cz = slot.center_source
            for offset_x in (-width * 0.5 + bar * 0.5,
                             width * 0.5 - bar * 0.5):
                frame_parts.append(facade_panel(
                    slot.side, (cx + offset_x, cy, cz), bar,
                    vertical_height, 0.030, slot.slot_id))
            for offset_z in (-height * 0.5 + bar * 0.5,
                             height * 0.5 - bar * 0.5):
                frame_parts.append(facade_panel(
                    slot.side, (cx, cy, cz + offset_z),
                    horizontal_width, bar, 0.030, slot.slot_id))
        else:
            cx, cy, cz = slot.center_source
            for offset_y in (-width * 0.5 + bar * 0.5,
                             width * 0.5 - bar * 0.5):
                frame_parts.append(facade_panel(
                    slot.side, (cx, cy + offset_y, cz), bar,
                    vertical_height, 0.030, slot.slot_id))
            for offset_z in (-height * 0.5 + bar * 0.5,
                             height * 0.5 - bar * 0.5):
                frame_parts.append(facade_panel(
                    slot.side, (cx, cy, cz + offset_z),
                    horizontal_width, bar, 0.030, slot.slot_id))
    return merge(frame_parts), merge(glass_parts)


def slots_for_grid(
    start_id: int,
    side: str,
    fixed_coordinate: float,
    bays: Sequence[float],
    floor_heights: Sequence[float],
    size_m: Vec2,
    floor_offset: int = 0,
    bay_offset: int = 0,
) -> tuple[WindowSlot, ...]:
    slots: list[WindowSlot] = []
    next_id = start_id
    for floor, height in enumerate(floor_heights, start=floor_offset):
        for bay, horizontal in enumerate(bays, start=bay_offset):
            center = ((horizontal, fixed_coordinate, height)
                      if side in {"Front", "Rear"}
                      else (fixed_coordinate, horizontal, height))
            slots.append(WindowSlot(
                next_id, side, floor, bay, center, size_m))
            next_id += 1
    return tuple(slots)


def facade_bounds(
    width: float,
    depth: float,
    height: float,
) -> tuple[FacadeAttachmentBounds, ...]:
    half_w, half_d = width * 0.5, depth * 0.5
    return (
        FacadeAttachmentBounds(
            "Front", (-half_w + 0.25, half_d - 0.20, 0.5),
            (half_w - 0.25, half_d, height - 0.5)),
        FacadeAttachmentBounds(
            "Rear", (-half_w + 0.25, -half_d, 0.5),
            (half_w - 0.25, -half_d + 0.20, height - 0.5)),
        FacadeAttachmentBounds(
            "Left", (-half_w, -half_d + 0.25, 0.5),
            (-half_w + 0.20, half_d - 0.25, height - 0.5)),
        FacadeAttachmentBounds(
            "Right", (half_w - 0.20, -half_d + 0.25, 0.5),
            (half_w, half_d - 0.25, height - 0.5)),
    )


def parts_for(
    stable_id: str,
    role_geometry: dict[str, Geometry],
    metal_meters_per_tile: float,
) -> tuple[PartSpec, ...]:
    parts: list[PartSpec] = []
    for role in PART_ROLES:
        uv_scheme, meters_per_tile = _ROLE_SURFACE_CONTRACT[role]
        if role == "Metal":
            meters_per_tile = metal_meters_per_tile
        parts.append(PartSpec(
            f"{stable_id}__{role}",
            role,
            role,
            uv_scheme,
            meters_per_tile,
            role_geometry[role]))
    return tuple(parts)


def old_town_prototype() -> PrototypeSpec:
    stable_id = "old-town-prototype-01"
    width, depth, height = 14.0, 13.5, 42.0
    half_d = depth * 0.5
    plinth_relief = 0.035
    secondary_relief = 0.065
    facade_edge_clearance = 0.04
    rear_tower_relief = 0.04
    facade_primary = merge((
        box((-3.8, 0.0, 15.0), (6.4, depth, 30.0)),
        box((3.9, -0.8, 13.5), (6.2, depth - 1.6, 27.0)),
        box((0.0, -4.5 + rear_tower_relief, 17.0),
            (3.0, 4.5, 34.0)),
    ))
    plinth = merge((
        box((-3.8,
             half_d + plinth_relief - 0.20 * 0.5,
             1.0),
            (6.4 - facade_edge_clearance * 2.0, 0.20, 2.0)),
        box((3.9, half_d - 0.90, 0.8),
            (6.2 - facade_edge_clearance * 2.0, 0.20, 1.6)),
    ))
    secondary_parts: list[Geometry] = [
        box((-3.8,
             half_d + secondary_relief - 0.24 * 0.5,
             11.0),
            (6.4 - facade_edge_clearance * 2.0, 0.24, 0.32)),
        box((-3.8,
             half_d + secondary_relief - 0.24 * 0.5,
             21.0),
            (6.4 - facade_edge_clearance * 2.0, 0.24, 0.32)),
        box((3.9,
             half_d - 0.8 + secondary_relief - 0.24 * 0.5,
             9.5),
            (6.2 - facade_edge_clearance * 2.0, 0.24, 0.30)),
        box((3.9,
             half_d - 0.8 + secondary_relief - 0.24 * 0.5,
             18.5),
            (6.2 - facade_edge_clearance * 2.0, 0.24, 0.30)),
        box((-0.2,
             half_d + secondary_relief - 0.35 * 0.5,
             2.2),
            (1.8, 0.35, 0.25)),
    ]
    for x in (-6.88, -0.72, 0.82, 6.88):
        secondary_parts.append(
            box((x, -0.2, 15.0), (0.16, 0.24, 29.0)))
    facade_secondary = merge(secondary_parts)
    roof = merge((
        gable_roof((-3.8, 0.0), 6.4, depth, 30.0, 36.8),
        gable_roof((3.9, -0.8), 6.2, depth - 1.6, 27.0, 33.0),
        pyramid_roof(
            (0.0, -4.5 + rear_tower_relief),
            3.0,
            4.5,
            34.0,
            height),
    ))
    metal_parts: list[Geometry] = [
        cylinder_z((-5.0, -2.0), 30.0, 39.0, 0.30, 8),
        cylinder_z((5.2, -3.0), 27.0, 35.0, 0.28, 8),
        box((6.75, 1.4, 18.0), (0.16, 4.8, 0.18)),
        box((6.75, 1.4, 15.5), (0.18, 0.18, 5.2)),
        box((6.75, 3.7, 15.5), (0.18, 0.18, 5.2)),
    ]
    for z in (12.8, 16.0, 19.2):
        metal_parts.append(box((6.70, 1.4, z), (0.45, 4.8, 0.10)))
    metal = merge(metal_parts)

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", half_d, (-5.7, -3.8, -1.9),
        (4.5, 9.0, 13.5, 18.0, 22.5, 27.0), (1.05, 1.75)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Front", half_d - 0.8, (2.3, 4.0, 5.7),
        (4.2, 8.6, 13.0, 17.4, 21.8), (0.95, 1.65), bay_offset=3))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -half_d, (-5.3, -2.6, 2.3, 5.2),
        (5.5, 15.5, 25.5), (0.9, 1.6)))
    frame, glass = window_geometry(slots)
    role_geometry = {
        "FacadePrimary": facade_primary,
        "FacadeSecondary": facade_secondary,
        "Plinth": plinth,
        "Roof": roof,
        "Metal": metal,
        "WindowFrame": frame,
        "WindowGlass": glass,
    }
    return PrototypeSpec(
        stable_id, "OldTown", "FragmentedPerimeter", width, depth, height,
        (0.0, half_d, 0.0), (-6.6, -6.35, 27.0),
        (6.6, 6.35, height), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.6))


def residential_prototype() -> PrototypeSpec:
    stable_id = "residential-prototype-01"
    width, depth, height = 11.5, 11.5, 40.0
    half_w, half_d = width * 0.5, depth * 0.5
    plinth_relief = 0.035
    facade_edge_clearance = 0.07
    roof_edge_clearance = 0.04
    ground_slab_edge_clearance = 0.03
    ground_slab_rear_clearance = 0.03
    facade_primary = merge((
        box((0.0, ground_slab_rear_clearance * 0.5, 0.08),
            (width - ground_slab_edge_clearance * 2.0,
             depth - ground_slab_rear_clearance,
             0.16)),
        box((0.0, -3.65, 15.0), (width, 4.2, 30.0)),
        box_without_faces(
            (-4.35, 1.05, 13.0),
            (2.8, 5.2, 26.0),
            ("Rear",)),
        box_without_faces(
            (4.35, 1.05, 13.0),
            (2.8, 5.2, 26.0),
            ("Rear",)),
        box((0.0, -1.6, 17.0), (3.2, 3.2, 34.0)),
    ))
    plinth = merge((
        box((0.0,
             -half_d - plinth_relief + 0.25 * 0.5,
             1.0),
            (width - facade_edge_clearance * 2.0, 0.25, 2.0)),
        box((-4.35, 3.62, 0.65),
            (2.8 - facade_edge_clearance * 2.0, 0.25, 1.3)),
        box((4.35, 3.62, 0.65),
            (2.8 - facade_edge_clearance * 2.0, 0.25, 1.3)),
    ))
    secondary_parts: list[Geometry] = [
        box((0.0, -0.02, 2.8), (3.8, 0.28, 0.22)),
    ]
    for side_x in (-4.35, 4.35):
        for z in (7.0, 12.0, 17.0, 22.0):
            secondary_parts.append(
                box((side_x, 3.68, z), (2.5, 0.75, 0.18)))
    facade_secondary = merge(secondary_parts)
    roof = merge((
        box((0.0, -3.65, 30.15), (width, 4.2, 0.30)),
        box_without_faces(
            (-4.35, 1.05, 26.15),
            (2.8 - roof_edge_clearance * 2.0, 5.2, 0.30),
            ("Rear",)),
        box_without_faces(
            (4.35, 1.05, 26.15),
            (2.8 - roof_edge_clearance * 2.0, 5.2, 0.30),
            ("Rear",)),
        pyramid_roof((0.0, -1.6), 3.2, 3.2, 34.0, height),
    ))
    metal_parts: list[Geometry] = [
        cylinder_z((-4.35, -4.1), 30.0, 35.0, 0.16, 8),
        cylinder_z((4.35, -4.1), 30.0, 35.0, 0.16, 8),
        box((0.0, -5.25, 34.0), (5.0, 0.12, 0.12)),
    ]
    for side_x in (-4.35, 4.35):
        for z in (7.55, 12.55, 17.55, 22.55):
            for offset in (-1.0, 0.0, 1.0):
                metal_parts.append(box((side_x + offset, 3.95, z),
                                       (0.06, 0.06, 1.0)))
            # Recess the rail faces 5 mm behind the posts.  The solids still
            # interlock, but their front/back planes no longer compete.
            metal_parts.append(box((side_x, 3.95, z + 0.48),
                                   (2.2, 0.05, 0.06)))
    metal = merge(metal_parts)

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", 3.65, (-4.85, -3.85, 3.85, 4.85),
        (3.8, 8.4, 13.0, 17.6, 22.2), (0.72, 1.55)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -half_d,
        (-4.6, -2.3, 0.0, 2.3, 4.6),
        (4.2, 9.2, 14.2, 19.2, 24.2), (0.85, 1.7)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Left", -half_w, (-4.0, -1.8, 0.6),
        (6.0, 16.0, 26.0), (0.8, 1.55)))
    frame, glass = window_geometry(slots)
    role_geometry = {
        "FacadePrimary": facade_primary,
        "FacadeSecondary": facade_secondary,
        "Plinth": plinth,
        "Roof": roof,
        "Metal": metal,
        "WindowFrame": frame,
        "WindowGlass": glass,
    }
    return PrototypeSpec(
        stable_id, "Residential", "SetbackCourtyard", width, depth, height,
        (0.0, half_d, 0.0), (-5.35, -5.35, 26.0),
        (5.35, 3.4, height), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.8))


def industrial_prototype() -> PrototypeSpec:
    stable_id = "industrial-prototype-01"
    width, depth, height = 14.0, 13.5, 36.0
    half_w, half_d = width * 0.5, depth * 0.5
    plinth_relief = 0.035
    secondary_relief = 0.065
    facade_edge_clearance = 0.04
    facade_primary = merge((
        box((0.0, 0.0, 12.0), (width, depth, 24.0)),
        box((-4.5, -3.6, 15.0), (4.2, 4.0, 30.0)),
        box((4.8, -4.0, 13.5), (3.8, 3.2, 27.0)),
    ))
    plinth = box(
        (0.0,
         half_d + plinth_relief - 0.20 * 0.5,
         1.0),
        (width - facade_edge_clearance * 2.0, 0.20, 2.0))
    facade_secondary = merge((
        box((0.0,
             half_d + secondary_relief - 0.22 * 0.5,
             10.0),
            (width - facade_edge_clearance * 2.0, 0.22, 0.35)),
        # Door surrounds begin above the plinth. Their front planes share the
        # 6.5 cm secondary relief without becoming competing layers.
        box((-4.5,
             half_d + secondary_relief - 0.35 * 0.5,
             5.275),
            (3.2, 0.35, 6.45)),
        box((0.0,
             half_d + secondary_relief - 0.40 * 0.5,
             4.625),
            (4.6, 0.40, 5.15)),
        box((4.9,
             half_d + secondary_relief - 0.35 * 0.5,
             4.9),
            (3.0, 0.35, 5.7)),
        box((0.0,
             -half_d - secondary_relief + 0.22 * 0.5,
             18.0),
            (width - facade_edge_clearance * 2.0, 0.22, 0.35)),
    ))
    roof_parts: list[Geometry] = []
    bay_width = width / 4.0
    for index in range(4):
        center_x = -half_w + bay_width * (index + 0.5)
        roof_parts.append(shed_roof(
            (center_x, 0.0), bay_width, depth, 24.0,
            29.0 if index % 2 == 0 else 27.5,
            high_on_right=index % 2 == 0))
    roof_parts.extend((
        box((-4.5, -3.6, 30.15), (4.2, 4.0, 0.30)),
        box((4.8, -4.0, 27.15), (3.8, 3.2, 0.30)),
    ))
    roof = merge(roof_parts)
    metal_parts: list[Geometry] = [
        cylinder_z((-5.0, -4.5), 30.0, height, 0.34, 10),
        cylinder_z((5.1, -4.7), 27.0, 34.5, 0.30, 10),
        cylinder_z((2.5, 2.0), 24.0, 31.0, 0.24, 8),
    ]
    for x in (-5.2, -2.6, 0.0, 2.6, 5.2):
        metal_parts.append(box((x, 5.5, 11.0), (0.14, 0.14, 6.2)))
    for z in (11.0, 14.0, 17.0):
        metal_parts.append(box((0.0, 5.5, z), (10.4, 0.12, 0.14)))
    metal = merge(metal_parts)

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", half_d, (-5.6, -3.4, -1.2, 1.2, 3.4, 5.6),
        (12.5, 17.0, 21.5), (1.25, 1.35)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -half_d,
        (-5.4, -2.7, 0.0, 2.7, 5.4),
        (8.0, 16.0, 22.0), (1.35, 1.5)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Right", half_w, (-4.5, -1.5, 1.5, 4.5),
        (12.0, 20.0), (1.1, 1.45)))
    frame, glass = window_geometry(slots)
    role_geometry = {
        "FacadePrimary": facade_primary,
        "FacadeSecondary": facade_secondary,
        "Plinth": plinth,
        "Roof": roof,
        "Metal": metal,
        "WindowFrame": frame,
        "WindowGlass": glass,
    }
    return PrototypeSpec(
        stable_id, "Industrial", "LowWideProcess", width, depth, height,
        (0.0, half_d, 0.0), (-6.7, -6.45, 24.0),
        (6.7, 6.45, 30.0), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.8))


def nightlife_prototype() -> PrototypeSpec:
    stable_id = "nightlife-prototype-01"
    width, depth, height = 12.5, 12.0, 48.0
    half_w, half_d = width * 0.5, depth * 0.5
    plinth_relief = 0.035
    secondary_relief = 0.065
    facade_edge_clearance = 0.04
    facade_primary = merge((
        box((0.0, 0.0, 5.0), (width, depth, 10.0)),
        box((-0.6, -0.4, 21.0), (10.5, 10.6, 32.0)),
        box((1.1, -1.0, 39.0), (7.1, 8.0, 4.0)),
    ))
    plinth = box(
        (0.0,
         half_d + plinth_relief - 0.22 * 0.5,
         1.2),
        (width - facade_edge_clearance * 2.0, 0.22, 2.4))
    secondary_parts: list[Geometry] = [
        box((0.0,
             half_d + secondary_relief - 0.24 * 0.5,
             8.8),
            (width - facade_edge_clearance * 2.0, 0.24, 0.35)),
        box((-0.6, 4.9 + secondary_relief - 0.20 * 0.5, 35.0),
            (10.5 - facade_edge_clearance * 2.0, 0.20, 0.35)),
        box((0.0,
             half_d + secondary_relief - 0.32 * 0.5,
             5.0),
            (3.4, 0.32, 4.8)),
        box((3.9,
             half_d + secondary_relief - 0.30 * 0.5,
             5.3),
            (3.1, 0.30, 1.8)),
    ]
    for z in (13.0, 19.5, 26.0, 32.5):
        secondary_parts.append(box(
            (-0.6, 4.9 + secondary_relief - 0.20 * 0.5, z),
            (10.5 - facade_edge_clearance * 2.0, 0.20, 0.25)))
    facade_secondary = merge(secondary_parts)
    roof = merge((
        box((-0.6, -0.4, 37.15),
            (10.5 - facade_edge_clearance * 2.0, 10.6, 0.30)),
        box((1.1, -1.0, 41.15), (7.1, 8.0, 0.30)),
        pyramid_roof((1.1, -1.0), 5.8, 6.5, 41.0, height),
    ))
    metal_parts: list[Geometry] = [
        box((5.55, -0.2, 22.0), (0.10, 7.8, 0.12)),
        box((5.55, -0.2, 11.0), (0.12, 0.12, 22.0)),
        box((5.55, 3.6, 11.0), (0.12, 0.12, 22.0)),
        box((-2.9, 3.1, 44.0), (5.2, 0.12, 0.14)),
        box((-5.3, 3.1, 41.8), (0.14, 0.14, 4.5)),
        box((-0.5, 3.1, 41.8), (0.14, 0.14, 4.5)),
    ]
    for z in (11.0, 15.0, 19.0, 23.0, 27.0, 31.0):
        metal_parts.append(box((5.55, -0.2, z), (0.35, 7.8, 0.10)))
    metal = merge(metal_parts)

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", half_d, (-4.7, -2.3, 0.0, 2.3, 4.7),
        (3.7, 7.2), (1.15, 1.75)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Front", 4.9, (-4.1, -1.7, 0.7, 3.1),
        (12.0, 17.5, 23.0, 28.5, 34.0), (1.15, 2.0),
        floor_offset=2))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -5.7, (-3.8, -1.3, 1.2, 3.7),
        (14.5, 22.5, 30.5), (1.05, 1.8)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Left", -5.85, (-3.7, -1.2, 1.3, 3.8),
        (14.0, 22.0, 30.0), (1.05, 1.75)))
    frame, glass = window_geometry(slots)
    role_geometry = {
        "FacadePrimary": facade_primary,
        "FacadeSecondary": facade_secondary,
        "Plinth": plinth,
        "Roof": roof,
        "Metal": metal,
        "WindowFrame": frame,
        "WindowGlass": glass,
    }
    return PrototypeSpec(
        stable_id, "Nightlife", "TallDense", width, depth, height,
        (0.0, half_d, 0.0), (-5.0, -5.1, 37.0),
        (5.0, 4.1, height), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.6))


def build_prototypes() -> tuple[PrototypeSpec, ...]:
    return (
        old_town_prototype(),
        residential_prototype(),
        industrial_prototype(),
        nightlife_prototype(),
    )
