#!/usr/bin/env python3
"""Pure deterministic geometry recipes for the City building prototype catalog.

The module deliberately has no Blender dependency.  It owns the fixed-metre
source geometry and semantic opening/balcony metadata consumed by
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
    opening_kind: str = "Window"


@dataclass(frozen=True)
class BalconySlot:
    stable_id: str
    floor: int
    side: str
    door_slot_id: int
    deck_bounds_min_source: Vec3
    deck_bounds_max_source: Vec3
    npc_dock_source: Vec3
    outward_source: Vec3


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
    balcony_slots: tuple[BalconySlot, ...] = ()
    collision_bounds: tuple[tuple[Vec3, Vec3], ...] = ()


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
    opening_kind: str = "Window",
) -> tuple[WindowSlot, ...]:
    slots: list[WindowSlot] = []
    next_id = start_id
    for floor, height in enumerate(floor_heights, start=floor_offset):
        for bay, horizontal in enumerate(bays, start=bay_offset):
            center = ((horizontal, fixed_coordinate, height)
                      if side in {"Front", "Rear"}
                      else (fixed_coordinate, horizontal, height))
            slots.append(WindowSlot(
                next_id, side, floor, bay, center, size_m, opening_kind))
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
    width, depth, height = 14.0, 13.5, 18.0
    half_d = depth * 0.5
    plinth_relief = 0.035
    secondary_relief = 0.065
    facade_edge_clearance = 0.04
    rear_tower_relief = 0.04
    facade_primary = merge((
        box((-3.8, 0.0, 6.5), (6.4, depth, 13.0)),
        box((3.9, -0.8, 5.9), (6.2, depth - 1.6, 11.8)),
        box((0.0, -4.5 + rear_tower_relief, 7.5),
            (3.0, 4.5, 15.0)),
    ))
    plinth = merge((
        box((-3.8,
             half_d + plinth_relief - 0.20 * 0.5,
             0.5),
            (6.4 - facade_edge_clearance * 2.0, 0.20, 1.0)),
        box((3.9, half_d - 0.90, 0.5),
            (6.2 - facade_edge_clearance * 2.0, 0.20, 1.0)),
    ))
    secondary_parts: list[Geometry] = [
        box((-3.8,
             half_d + secondary_relief - 0.24 * 0.5,
             4.0),
            (6.4 - facade_edge_clearance * 2.0, 0.24, 0.32)),
        box((-3.8,
             half_d + secondary_relief - 0.24 * 0.5,
             10.0),
            (6.4 - facade_edge_clearance * 2.0, 0.24, 0.32)),
        box((3.9,
             half_d - 0.8 + secondary_relief - 0.24 * 0.5,
             3.7),
            (6.2 - facade_edge_clearance * 2.0, 0.24, 0.30)),
        box((3.9,
             half_d - 0.8 + secondary_relief - 0.24 * 0.5,
             9.5),
            (6.2 - facade_edge_clearance * 2.0, 0.24, 0.30)),
        box((-0.2,
             half_d + secondary_relief - 0.35 * 0.5,
             2.2),
            (1.8, 0.35, 0.25)),
    ]
    for x in (-6.88, -0.72, 0.82, 6.88):
        secondary_parts.append(
            box((x, -0.2, 6.5), (0.16, 0.24, 12.0)))
    facade_secondary = merge(secondary_parts)
    roof = merge((
        gable_roof((-3.8, 0.0), 6.4, depth, 13.0, 15.4),
        gable_roof((3.9, -0.8), 6.2, depth - 1.6, 11.8, 14.0),
        pyramid_roof(
            (0.0, -4.5 + rear_tower_relief),
            3.0,
            4.5,
            15.0,
            height),
    ))
    metal_parts: list[Geometry] = [
        cylinder_z((-5.0, -2.0), 13.0, 16.0, 0.30, 8),
        cylinder_z((5.2, -3.0), 11.8, 14.8, 0.28, 8),
        box((6.75, 1.4, 8.8), (0.16, 4.8, 0.18)),
        box((6.75, 1.4, 6.5), (0.18, 0.18, 5.2)),
        box((6.75, 3.7, 6.5), (0.18, 0.18, 5.2)),
    ]
    for z in (4.0, 6.4, 8.8):
        metal_parts.append(box((6.70, 1.4, z), (0.45, 4.66, 0.10)))
    metal = merge(metal_parts)

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", half_d, (-5.7, -3.8, -1.9),
        (2.5, 5.5, 8.5, 11.5), (1.05, 1.75)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Front", half_d - 0.8, (2.3, 4.0, 5.7),
        (2.2, 5.1, 8.0, 10.7), (0.95, 1.65), bay_offset=3))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -half_d, (-5.3, -2.6, 2.3, 5.2),
        (2.5, 5.5, 8.5, 11.0), (0.9, 1.6)))
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
        (0.0, half_d, 0.0), (-6.6, -6.35, 11.8),
        (6.6, 6.35, height), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.6))


def residential_prototype() -> PrototypeSpec:
    stable_id = "residential-prototype-01"
    width, depth, height = 11.5, 11.5, 15.0
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
        box((0.0, -3.65, 6.85), (width, 4.2, 13.7)),
        box_without_faces(
            (-4.35, 1.05, 6.85),
            (2.8, 5.2, 13.7),
            ("Rear",)),
        box_without_faces(
            (4.35, 1.05, 6.85),
            (2.8, 5.2, 13.7),
            ("Rear",)),
        box((0.0, -1.6, 7.35), (3.2, 3.2, 14.7)),
    ))
    plinth = merge((
        box((0.0,
             -half_d - plinth_relief + 0.25 * 0.5,
             0.35),
            (width - facade_edge_clearance * 2.0, 0.25, 0.7)),
        box((-4.35, 3.62, 0.35),
            (2.8 - facade_edge_clearance * 2.0, 0.25, 0.7)),
        box((4.35, 3.62, 0.35),
            (2.8 - facade_edge_clearance * 2.0, 0.25, 0.7)),
    ))
    balcony_deck_levels = (2.8, 5.6, 8.4, 11.2)
    balcony_centers = (-4.35, 4.35)
    balcony_width = 2.5
    balcony_depth = 1.2
    balcony_thickness = 0.18
    balcony_center_y = 4.15
    secondary_parts: list[Geometry] = [
        box((0.0, -0.02, 2.8), (3.8, 0.28, 0.22)),
    ]
    for side_x in balcony_centers:
        for deck_level in balcony_deck_levels:
            secondary_parts.append(
                box(
                    (side_x, balcony_center_y,
                     deck_level - balcony_thickness * 0.5),
                    (balcony_width, balcony_depth, balcony_thickness)))
    facade_secondary = merge(secondary_parts)
    roof = merge((
        box((0.0, -3.65, 13.85), (width, 4.2, 0.30)),
        box_without_faces(
            (-4.35, 1.05, 13.85),
            (2.8 - roof_edge_clearance * 2.0, 5.2, 0.30),
            ("Rear",)),
        box_without_faces(
            (4.35, 1.05, 13.85),
            (2.8 - roof_edge_clearance * 2.0, 5.2, 0.30),
            ("Rear",)),
        pyramid_roof((0.0, -1.6), 3.2, 3.2, 14.7, height),
    ))
    metal_parts: list[Geometry] = [
        cylinder_z((-4.35, -4.1), 13.7, 14.9, 0.16, 8),
        cylinder_z((4.35, -4.1), 13.7, 14.9, 0.16, 8),
        box((0.0, -5.25, 14.8), (5.0, 0.12, 0.12)),
    ]
    rail_front_y = balcony_center_y + balcony_depth * 0.5 - 0.04
    rail_back_y = 3.72
    rail_side_x = balcony_width * 0.5 - 0.04
    rail_height = 1.02
    for side_x in balcony_centers:
        for deck_level in balcony_deck_levels:
            rail_center_z = deck_level + rail_height * 0.5
            for offset_x in (-rail_side_x, 0.0, rail_side_x):
                metal_parts.append(box(
                    (side_x + offset_x, rail_front_y, rail_center_z),
                    (0.06, 0.06, rail_height)))
            for offset_x in (-rail_side_x, rail_side_x):
                metal_parts.append(box(
                    (side_x + offset_x, rail_back_y, rail_center_z),
                    (0.06, 0.06, rail_height)))
            metal_parts.append(box(
                (side_x, rail_front_y, deck_level + rail_height - 0.06),
                (balcony_width - 0.08, 0.05, 0.06)))
            for offset_x in (-rail_side_x, rail_side_x):
                metal_parts.append(box(
                    (side_x + offset_x,
                     (rail_back_y + rail_front_y) * 0.5,
                     deck_level + rail_height - 0.07),
                    (0.05, rail_front_y - rail_back_y, 0.05)))

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", 3.65, (-4.85, -3.85, 3.85, 4.85),
        (1.6,), (0.72, 1.55)))
    balcony_slots: list[BalconySlot] = []
    for floor, deck_level in enumerate(balcony_deck_levels, start=1):
        opening_top = deck_level + 2.20
        for side_name, side_x, door_x, window_x, door_bay, window_bay in (
            ("left", -4.35, -4.85, -3.85, 0, 1),
            ("right", 4.35, 4.85, 3.85, 3, 2),
        ):
            door_slot_id = len(slots) + 1
            slots.append(WindowSlot(
                door_slot_id,
                "Front",
                floor,
                door_bay,
                (door_x, 3.65, deck_level + 1.10),
                (0.82, 2.20),
                "BalconyDoor"))
            slots.append(WindowSlot(
                len(slots) + 1,
                "Front",
                floor,
                window_bay,
                (window_x, 3.65, opening_top - 1.55 * 0.5),
                (0.72, 1.55)))

            deck_min = (
                side_x - balcony_width * 0.5,
                balcony_center_y - balcony_depth * 0.5,
                deck_level - balcony_thickness)
            deck_max = (
                side_x + balcony_width * 0.5,
                balcony_center_y + balcony_depth * 0.5,
                deck_level)
            balcony_slots.append(BalconySlot(
                f"residential-front-{side_name}-floor-{floor:02d}",
                floor,
                "Front",
                door_slot_id,
                deck_min,
                deck_max,
                (side_x, balcony_center_y + 0.08, deck_level),
                (0.0, 1.0, 0.0)))

            handle_x = door_x + (-0.25 if side_name == "left" else 0.25)
            metal_parts.append(box(
                (door_x, 3.73, deck_level + 0.04),
                (0.92, 0.14, 0.08)))
            metal_parts.append(box(
                (handle_x, 3.70, deck_level + 1.02),
                (0.06, 0.12, 0.18)))

    metal = merge(metal_parts)
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -half_d,
        (-4.6, -2.3, 0.0, 2.3, 4.6),
        (1.6, 4.4, 7.2, 10.0, 12.8), (0.85, 1.7)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Left", -half_w, (-4.0, -1.8, 0.6),
        (1.8, 7.4, 12.7), (0.8, 1.55)))
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
        (0.0, half_d, 0.0), (-5.35, -5.35, 13.7),
        (5.35, 3.4, height), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.8),
        tuple(balcony_slots))


def industrial_prototype() -> PrototypeSpec:
    stable_id = "industrial-prototype-01"
    width, depth, height = 14.0, 13.5, 10.0
    half_w, half_d = width * 0.5, depth * 0.5
    plinth_relief = 0.035
    secondary_relief = 0.065
    facade_edge_clearance = 0.04
    facade_primary = merge((
        box((0.0, 0.0, 3.3), (width, depth, 6.6)),
        box((-4.5, -3.6, 4.05), (4.2, 4.0, 8.1)),
        box((4.8, -4.0, 3.7), (3.8, 3.2, 7.4)),
    ))
    plinth = box(
        (0.0,
         half_d + plinth_relief - 0.20 * 0.5,
         1.0),
        (width - facade_edge_clearance * 2.0, 0.20, 2.0))
    facade_secondary = merge((
        box((0.0,
             half_d + secondary_relief - 0.22 * 0.5,
             4.6),
            (width - facade_edge_clearance * 2.0, 0.22, 0.35)),
        # Door surrounds begin above the plinth. Their front planes share the
        # 6.5 cm secondary relief without becoming competing layers.
        box((-4.5,
             half_d + secondary_relief - 0.35 * 0.5,
             3.0),
            (3.2, 0.35, 1.9)),
        box((0.0,
             half_d + secondary_relief - 0.40 * 0.5,
             3.0),
            (4.6, 0.40, 1.9)),
        box((4.9,
             half_d + secondary_relief - 0.35 * 0.5,
             3.0),
            (3.0, 0.35, 1.9)),
        box((0.0,
             -half_d - secondary_relief + 0.22 * 0.5,
             4.6),
            (width - facade_edge_clearance * 2.0, 0.22, 0.35)),
    ))
    roof_parts: list[Geometry] = []
    bay_width = width / 4.0
    for index in range(4):
        center_x = -half_w + bay_width * (index + 0.5)
        roof_parts.append(shed_roof(
            (center_x, 0.0), bay_width, depth, 6.6,
            8.2 if index % 2 == 0 else 7.7,
            high_on_right=index % 2 == 0))
    roof_parts.extend((
        box((-4.5, -3.6, 8.25), (4.2, 4.0, 0.30)),
        box((4.8, -4.0, 7.55), (3.8, 3.2, 0.30)),
    ))
    roof = merge(roof_parts)
    metal_parts: list[Geometry] = [
        cylinder_z((-5.0, -4.5), 8.1, height, 0.34, 10),
        cylinder_z((5.1, -4.7), 7.4, 9.4, 0.30, 10),
        cylinder_z((2.5, 2.0), 6.6, 8.7, 0.24, 8),
    ]
    for x in (-5.2, -2.6, 0.0, 2.6, 5.2):
        metal_parts.append(box((x, 5.5, 3.0), (0.14, 0.14, 3.2)))
    for z in (1.5, 3.0, 4.5):
        metal_parts.append(box((0.0, 5.5, z), (10.4, 0.12, 0.14)))
    metal = merge(metal_parts)

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", half_d, (-5.6, -3.4, -1.2, 1.2, 3.4, 5.6),
        (5.5,), (1.25, 1.35)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -half_d,
        (-5.4, -2.7, 0.0, 2.7, 5.4),
        (3.0, 5.3), (1.35, 1.5)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Right", half_w, (-4.5, -1.5, 1.5, 4.5),
        (5.3,), (1.1, 1.45)))
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
        (0.0, half_d, 0.0), (-6.7, -6.45, 6.6),
        (6.7, 6.45, 8.4), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.8))


def nightlife_prototype() -> PrototypeSpec:
    stable_id = "nightlife-prototype-01"
    width, depth, height = 12.5, 12.0, 27.0
    half_w, half_d = width * 0.5, depth * 0.5
    plinth_relief = 0.035
    secondary_relief = 0.065
    facade_edge_clearance = 0.04
    facade_primary = merge((
        box((0.0, 0.0, 2.4), (width, depth, 4.8)),
        box((-0.6, -0.4, 12.5), (10.5, 10.6, 19.0)),
        box((1.1, -1.0, 23.0), (7.1, 8.0, 2.0)),
    ))
    plinth = box(
        (0.0,
         half_d + plinth_relief - 0.22 * 0.5,
         0.225),
        (width - facade_edge_clearance * 2.0, 0.22, 0.45))
    secondary_parts: list[Geometry] = [
        box((0.0,
             half_d + secondary_relief - 0.24 * 0.5,
             4.4),
            (width - facade_edge_clearance * 2.0, 0.24, 0.35)),
        box((-0.6, 4.9 + secondary_relief - 0.20 * 0.5, 21.5),
            (10.5 - facade_edge_clearance * 2.0, 0.20, 0.35)),
        box((0.0,
             half_d + secondary_relief - 0.32 * 0.5,
             2.8),
            (3.4, 0.32, 2.8)),
        box((3.9,
             half_d + secondary_relief - 0.30 * 0.5,
             2.8),
            (3.1, 0.30, 1.4)),
    ]
    for z in (7.5, 13.5, 19.5):
        secondary_parts.append(box(
            (-0.6, 4.9 + secondary_relief - 0.20 * 0.5, z),
            (10.5 - facade_edge_clearance * 2.0, 0.20, 0.25)))
    facade_secondary = merge(secondary_parts)
    roof = merge((
        box((-0.6, -0.4, 22.15),
            (10.5 - facade_edge_clearance * 2.0, 10.6, 0.30)),
        box((1.1, -1.0, 24.15), (7.1, 8.0, 0.30)),
        pyramid_roof((1.1, -1.0), 5.8, 6.5, 24.0, height),
    ))
    metal_parts: list[Geometry] = [
        box((5.55, -0.2, 20.0), (0.10, 7.8, 0.12)),
        box((5.55, -0.2, 10.0), (0.12, 0.12, 20.0)),
        box((5.55, 3.6, 10.0), (0.12, 0.12, 20.0)),
        box((-2.9, 3.1, 25.0), (5.2, 0.12, 0.14)),
        box((-5.3, 3.1, 23.8), (0.14, 0.14, 2.5)),
        box((-0.5, 3.1, 23.8), (0.14, 0.14, 2.5)),
    ]
    for z in (4.0, 7.0, 10.0, 13.0, 16.0, 19.0):
        metal_parts.append(box((5.55, -0.2, z), (0.35, 7.8, 0.10)))
    metal = merge(metal_parts)

    slots: list[WindowSlot] = []
    slots.extend(slots_for_grid(
        1, "Front", half_d, (-4.7, -2.3, 0.0, 2.3, 4.7),
        (1.5, 3.8), (1.15, 1.75)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Front", 4.9, (-4.1, -1.7, 0.7, 3.1),
        (6.0, 9.0, 12.0, 15.0, 18.0, 21.0), (1.15, 2.0),
        floor_offset=2))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Rear", -5.7, (-3.8, -1.3, 1.2, 3.7),
        (6.5, 12.5, 18.5), (1.05, 1.8)))
    slots.extend(slots_for_grid(
        len(slots) + 1, "Left", -5.85, (-3.7, -1.2, 1.3, 3.8),
        (6.0, 12.0, 18.0), (1.05, 1.75)))
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
        (0.0, half_d, 0.0), (-5.0, -5.1, 22.0),
        (5.0, 4.1, height), facade_bounds(width, depth, height),
        tuple(slots), parts_for(stable_id, role_geometry, 1.6))


def prism(polygon: Sequence[Vec2], bottom: float, top: float) -> Geometry:
    """Extrude a counterclockwise footprint without internal or doubled walls."""
    count = len(polygon)
    signed_area = sum(polygon[index][0] * polygon[(index + 1) % count][1] -
                      polygon[(index + 1) % count][0] * polygon[index][1]
                      for index in range(count)) * 0.5
    if signed_area <= 0.0 or top <= bottom:
        raise ValueError("Authored prism must have positive signed volume and outward winding.")
    vertices = tuple((x, y, z) for z in (bottom, top) for x, y in polygon)
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces.extend((index, (index + 1) % count,
                  (index + 1) % count + count, index + count)
                 for index in range(count))
    return Geometry(vertices, tuple(faces), (0,) * len(faces))


def collision_bounds_for(prototype: PrototypeSpec) -> tuple[tuple[Vec3, Vec3], ...]:
    """Use authored solids, excluding thin ground skins and the open courtyards."""
    if prototype.collision_bounds:
        return prototype.collision_bounds
    geometry = next(part.geometry for part in prototype.parts
                    if part.role == "FacadePrimary")
    adjacency = [set() for _ in geometry.vertices]
    for face in geometry.faces:
        for vertex in face:
            adjacency[vertex].update(face)
    remaining = set(range(len(geometry.vertices)))
    solids = []
    while remaining:
        pending = [min(remaining)]
        component = set()
        while pending:
            vertex = pending.pop()
            if vertex in component:
                continue
            component.add(vertex)
            pending.extend(adjacency[vertex] - component)
        remaining.difference_update(component)
        low = tuple(min(geometry.vertices[index][axis] for index in component)
                    for axis in range(3))
        high = tuple(max(geometry.vertices[index][axis] for index in component)
                     for axis in range(3))
        if high[2] - low[2] > 0.5:
            solids.append((low, high))
    return tuple(solids)


def massing_variant(district: str, variant: int) -> PrototypeSpec:
    """Fixed-metre street bar or L-shaped frontage, never a stretched old FBX."""
    prefixes = {"OldTown": "old-town", "Residential": "residential",
                "Industrial": "industrial", "Nightlife": "nightlife"}
    heights = {"OldTown": (16.2, 17.4), "Residential": (14.4, 15.6),
               "Industrial": (8.4, 9.3), "Nightlife": (24.6, 26.4)}
    wing = variant == 5
    corner = variant == 3
    passage = variant == 4
    height = ({"OldTown": 12.6, "Residential": 11.8,
               "Industrial": 6.9, "Nightlife": 18.6}[district] if wing else
              heights[district][1 if corner else 0])
    width, depth = ((10.0, 6.0) if wing else (15.0, 14.0) if corner else
                    (17.0, 9.5) if district == "Nightlife" else (22.0, 11.5))
    stable_id = f"{prefixes[district]}-prototype-{variant:02d}"
    half_w, half_d = width * 0.5, depth * 0.5
    front_y = half_d - (1.2 if district == "Residential" else 0.0)
    wing_width = 5.0
    inner_x = -half_w + wing_width
    body_top = height - (2.4 if district == "OldTown" else
                         0.75 if district == "Industrial" else 0.35)
    passage_width = 3.6
    passage_top = 4.2 if district == "Industrial" else 3.2
    if corner:
        polygon = ((-half_w, -half_d), (inner_x, -half_d),
                   (inner_x, 0.0), (half_w, 0.0),
                   (half_w, front_y), (-half_w, front_y))
        solids = (((-half_w, 0.0, 0.0), (half_w, front_y, body_top)),
                  ((-half_w, -half_d, 0.0), (inner_x, 0.0, body_top)))
    else:
        polygon = ((-half_w, -half_d), (half_w, -half_d),
                   (half_w, front_y), (-half_w, front_y))
        solids = (((-half_w, -half_d, 0.0),
                   (half_w, front_y, body_top)),)
    if passage:
        left = ((-half_w, -half_d), (-passage_width * 0.5, -half_d),
                (-passage_width * 0.5, front_y), (-half_w, front_y))
        right = ((passage_width * 0.5, -half_d), (half_w, -half_d),
                 (half_w, front_y), (passage_width * 0.5, front_y))
        # Lower wings plus one continuous upper body leave a genuine open
        # throat, including the soffit; no painted rectangle hides a wall.
        facade_primary = merge((prism(left, 0.0, passage_top),
            prism(right, 0.0, passage_top), prism(polygon, passage_top, body_top)))
        solids = (((-half_w, -half_d, 0.0),
                   (-passage_width * 0.5, front_y, passage_top)),
                  ((passage_width * 0.5, -half_d, 0.0),
                   (half_w, front_y, passage_top)),
                  ((-half_w, -half_d, passage_top), (half_w, front_y, body_top)))
        pier_width = half_w - passage_width * 0.5
        plinth = merge(box((sign * (half_w + passage_width * 0.5) * 0.5,
                           front_y - 0.085, 0.35),
                          (pier_width - 0.08, 0.24, 0.7)) for sign in (-1, 1))
        secondary = [box((sign * (half_w + passage_width * 0.5) * 0.5,
                          front_y - 0.055, 2.6),
                         (pier_width - 0.08, 0.24, 0.28)) for sign in (-1, 1)]
    else:
        facade_primary = prism(polygon, 0.0, body_top)
        plinth = box((0.0, front_y - 0.085, 0.35), (width - 0.08, 0.24, 0.7))
        secondary = [box((0.0, front_y - 0.055, 2.6),
                         (width - 0.08, 0.24, 0.28))]
    if district == "OldTown":
        roof = (merge((gable_roof((0.0, front_y * 0.5), width, front_y,
                                 body_top, height),
                       gable_roof((-half_w + wing_width * 0.5, -half_d * 0.5),
                                  wing_width, half_d, body_top, height - 0.6)))
                if corner else gable_roof((0.0, (front_y - half_d) * 0.5),
                    width, front_y + half_d, body_top, height))
    elif district == "Industrial" and not corner:
        roof = shed_roof((0.0, (front_y - half_d) * 0.5), width,
                         front_y + half_d, body_top, height, high_on_right=True)
    else:
        roof = prism(polygon, body_top, height)
    if district == "Industrial":
        # A continuous lintel stripe sits above the loading-gate surround.
        secondary = [box((0.0, front_y - 0.055, 4.85),
                         (width - 0.08, 0.24, 0.28))]
    metal = [cylinder_z((-half_w + 0.35, -half_d + 0.4),
                        body_top, height - 0.10, 0.14, 8)]
    slots: list[WindowSlot] = []
    balconies: list[BalconySlot] = []
    if district == "Residential":
        # Each deck is paired with its own door and neighbouring apartment pane.
        # The front is recessed by the exact 1.2 m deck depth, so no balcony
        # escapes the fixed envelope or consumes a pavement clearance.
        centers = (-width * 0.28, width * 0.28)
        deck_levels = tuple(level for level in (2.8, 5.6, 8.4, 11.2)
                            if level + 2.2 < body_top)
        for floor, deck_level in enumerate(deck_levels, start=1):
            for group, center_x in enumerate(centers):
                door_x, pane_x = center_x - 0.48, center_x + 0.48
                door_id = len(slots) + 1
                slots.append(WindowSlot(door_id, "Front", floor, group * 2,
                    (door_x, front_y, deck_level + 1.1), (0.82, 2.2), "BalconyDoor"))
                slots.append(WindowSlot(len(slots) + 1, "Front", floor, group * 2 + 1,
                    (pane_x, front_y, deck_level + 1.425), (0.72, 1.55)))
                low = (center_x - 1.25, front_y, deck_level - 0.18)
                high = (center_x + 1.25, half_d, deck_level)
                balconies.append(BalconySlot(
                    f"{stable_id}-front-{group}-floor-{floor:02d}", floor,
                    "Front", door_id, low, high,
                    (center_x, front_y + 0.68, deck_level), (0.0, 1.0, 0.0)))
                secondary.append(box((center_x, front_y + 0.6, deck_level - 0.09),
                                     (2.5, 1.2, 0.18)))
                for rail_x in (center_x - 1.21, center_x + 1.21):
                    metal.append(box((rail_x, half_d - 0.04, deck_level + 0.51),
                                     (0.06, 0.06, 1.02)))
                    metal.append(box((rail_x, front_y + 0.6, deck_level + 0.95),
                                     (0.05, 1.12, 0.06)))
                metal.append(box((center_x, half_d - 0.04, deck_level + 0.96),
                                 (2.42, 0.05, 0.06)))
        slots.extend(slots_for_grid(len(slots) + 1, "Front", front_y,
            tuple(value for center in centers for value in (center - 0.48, center + 0.48)),
            (1.6,), (0.72, 1.55)))
    else:
        floor_heights = ((2.1, 5.1, 8.1, 11.1) if district == "OldTown"
                         else (5.6,) if district == "Industrial"
                         else (1.9, 4.9, 7.9, 10.9, 13.9, 16.9, 19.9, 22.9))
        opening = ((1.05, 1.75) if district == "OldTown" else
                   (1.5, 1.35) if district == "Industrial" else (1.0, 1.9))
        floor_heights = tuple(level for level in floor_heights
                              if level + opening[1] * 0.5 < body_top)
        if district == "Industrial" and not floor_heights:
            floor_heights = (body_top - 1.0,)
        bays = tuple(width * fraction for fraction in
                     ((-0.36, -0.18, 0.18, 0.36) if passage else
                      (-0.36, -0.18, 0.0, 0.18, 0.36)))
        slots.extend(slots_for_grid(1, "Front", front_y, bays, floor_heights, opening))
        # Front trim follows real storeys; the back remains a quieter service face.
        if district == "OldTown":
            for level in (3.7, 9.7):
                secondary.append(box((0.0, front_y - 0.055, level),
                                     (width - 0.08, 0.24, 0.24)))
        elif district == "Industrial":
            secondary.append(box((-width * 0.26, front_y - 0.08, 3.0),
                                 (4.0, 0.24, 3.0)))
            for level in (4.5, 6.8):
                metal.append(box((0.0, front_y + 0.045, level),
                                 (width - 0.4, 0.05, 0.09)))
        else:
            secondary.append(box((width * 0.25, front_y - 0.055, 5.0),
                                 (3.0, 0.24, 1.1)))
            for level in (6.2, 12.2, 18.2):
                if level + .2 < body_top:
                    metal.append(box((half_w - 0.3, 1.2, level),
                                     (0.12, 1.8, 0.08)))
    rear_low, rear_high = (-half_w, inner_x) if corner else (-half_w, half_w)
    rear_bay_count = 2 if corner else (5 if district == "Residential" else 4)
    rear_bays = tuple(rear_low + (index + 0.5) * (rear_high - rear_low) / rear_bay_count
                      for index in range(rear_bay_count))
    rear_floors = ((1.6, 4.4, 7.2, 10.0, 12.8) if district == "Residential" else
                   (5.6,) if district == "Industrial" else
                   (2.1, 5.1, 8.1, 11.1) if district == "OldTown" else
                   (4.9, 10.9, 16.9, 22.9))
    if passage:
        rear_bays = tuple(value for value in rear_bays
                          if abs(value) > passage_width * 0.5 + 0.5)
    rear_floors = tuple(level for level in rear_floors if level + 0.8 < body_top)
    if district == "Industrial" and not rear_floors:
        rear_floors = (body_top - 1.0,)
    slots.extend(slots_for_grid(len(slots) + 1, "Rear", -half_d,
                               rear_bays, rear_floors, (0.85, 1.6)))
    side_floors = ((5.6,) if district == "Industrial" else
        (4.4, 10.0) if district == "Residential" else
        (5.1, 11.1) if district == "OldTown" else (7.9, 16.9))
    side_floors = tuple(level for level in side_floors if level + 0.775 < body_top)
    if district == "Industrial" and not side_floors:
        side_floors = (body_top - 1.0,)
    slots.extend(slots_for_grid(len(slots) + 1, "Left", -half_w,
        (-half_d * 0.55, front_y * 0.55), side_floors, (0.8, 1.55)))
    frame, glass = window_geometry(slots)
    attachments = list(facade_bounds(width, depth, height))
    if corner:
        attachments[1] = FacadeAttachmentBounds("Rear",
            (-half_w + 0.25, -half_d, 0.5), (inner_x - 0.25, -half_d + 0.2, body_top - 0.5))
        attachments[3] = FacadeAttachmentBounds("Right",
            (half_w - 0.2, 0.25, 0.5), (half_w, front_y - 0.25, body_top - 0.5))
    attachments[0] = FacadeAttachmentBounds("Front",
        (-half_w + 0.25, front_y - 0.2, 0.5), (half_w - 0.25, front_y, body_top - 0.5))
    role_geometry = {"FacadePrimary": facade_primary, "FacadeSecondary": merge(secondary),
        "Plinth": plinth, "Roof": roof, "Metal": merge(metal),
        "WindowFrame": frame, "WindowGlass": glass}
    return PrototypeSpec(stable_id, district,
        "CourtyardWing" if wing else "ThroughPassage" if passage else
        "CornerStreetWing" if corner else "LongStreetBar",
        width, depth, height, (0.0, half_d, 0.0),
        (-half_w + 0.4, 0.4 if corner else -half_d + 0.4, body_top),
        (half_w - 0.4, front_y - 0.4, height), tuple(attachments), tuple(slots),
        parts_for(stable_id, role_geometry, 1.6 if district == "OldTown" else 1.8),
        tuple(balconies), solids)


def build_prototypes() -> tuple[PrototypeSpec, ...]:
    return (old_town_prototype(), residential_prototype(), industrial_prototype(), nightlife_prototype(),
            *(massing_variant(district, variant) for variant in (2, 3, 4, 5)
              for district in ("OldTown", "Residential", "Industrial", "Nightlife")))
