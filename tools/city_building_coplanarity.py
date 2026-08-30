#!/usr/bin/env python3
"""Pure coplanarity audit for deterministic City building geometry.

The generator deliberately models several parts as collections of simple
solids.  This helper reports positive-area overlap between axis-aligned faces
without depending on Blender.  Line or point contact is not an overlap.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, Sequence

from city_building_parts import Geometry, PartSpec, PrototypeSpec, Vec2, Vec3


DEFAULT_TOLERANCE = 1e-7
MIN_VISIBLE_LAYER_SEPARATION = 0.03
MIN_VISIBLE_LAYER_OVERLAP_AREA = 0.05

_OPAQUE_ROLES = frozenset({
    "FacadePrimary",
    "FacadeSecondary",
    "Plinth",
    "Roof",
    "Metal",
    "WindowFrame",
})
_GLASS_FACADE_RELIEF = 0.018
_FRAME_GLASS_SEPARATION = 0.012


@dataclass(frozen=True)
class CoplanarFaceOverlap:
    """One positive-area overlap between two axis-aligned source faces."""

    prototype_id: str
    first_role: str
    first_face_index: int
    second_role: str
    second_face_index: int
    plane_axis: int
    plane_coordinate: float
    first_normal_sign: int
    second_normal_sign: int
    area: float

    @property
    def has_opposing_normals(self) -> bool:
        return self.first_normal_sign != self.second_normal_sign


@dataclass(frozen=True)
class NearCoplanarFaceOverlap:
    """A large same-facing opaque overlap below the safe depth relief."""

    prototype_id: str
    first_role: str
    first_face_index: int
    second_role: str
    second_face_index: int
    plane_axis: int
    first_plane_coordinate: float
    second_plane_coordinate: float
    normal_sign: int
    separation: float
    area: float


@dataclass(frozen=True)
class _PlanarFace:
    role: str
    face_index: int
    slot_id: int
    axis: int
    coordinate: float
    normal_sign: int
    polygon: tuple[Vec2, ...]


def find_axis_aligned_coplanar_overlaps(
        prototype: PrototypeSpec,
        tolerance: float = DEFAULT_TOLERANCE) -> tuple[CoplanarFaceOverlap, ...]:
    """Return every positive-area overlap; no semantic exclusions are made."""

    if tolerance <= 0.0:
        raise ValueError("Coplanarity tolerance must be positive.")

    faces = tuple(_axis_aligned_faces(prototype, tolerance))
    overlaps: list[CoplanarFaceOverlap] = []
    for first_index, first in enumerate(faces):
        for second in faces[first_index + 1:]:
            if first.axis != second.axis or abs(
                    first.coordinate - second.coordinate) > tolerance:
                continue

            area = _convex_intersection_area(
                first.polygon,
                second.polygon,
                tolerance)
            if area <= tolerance:
                continue

            overlaps.append(CoplanarFaceOverlap(
                prototype.stable_id,
                first.role,
                first.face_index,
                second.role,
                second.face_index,
                first.axis,
                (first.coordinate + second.coordinate) * 0.5,
                first.normal_sign,
                second.normal_sign,
                area))

    return tuple(overlaps)


def find_near_coplanar_visible_overlaps(
        prototype: PrototypeSpec,
        minimum_separation: float = MIN_VISIBLE_LAYER_SEPARATION,
        minimum_area: float = MIN_VISIBLE_LAYER_OVERLAP_AREA,
        tolerance: float = DEFAULT_TOLERANCE,
) -> tuple[NearCoplanarFaceOverlap, ...]:
    """Return large exterior layers closer than the safe depth relief.

    Opposing normals are structural contacts rather than competing fragments.
    Downward horizontal faces are undersides, not exterior presentation layers.
    Opaque pairs are always audited. Window panes own two narrowly identified
    exceptions: the 18 mm facade relief and the 12 mm frame-to-glass relief,
    tied to their slot IDs. Unexpected glass contacts remain reportable. Tiny
    overlaps below ``minimum_area`` cover interlocking rails and other contact
    patches, not broad surfaces visible from the street.
    """

    if tolerance <= 0.0:
        raise ValueError("Coplanarity tolerance must be positive.")
    if minimum_separation <= tolerance:
        raise ValueError("Visible layer separation must exceed tolerance.")
    if minimum_area <= tolerance:
        raise ValueError("Visible overlap area must exceed tolerance.")

    faces = tuple(_axis_aligned_faces(prototype, tolerance))
    overlaps: list[NearCoplanarFaceOverlap] = []
    for first_index, first in enumerate(faces):
        for second in faces[first_index + 1:]:
            if (first.axis != second.axis or
                    first.normal_sign != second.normal_sign or
                    (first.axis == 2 and first.normal_sign < 0)):
                continue

            separation = abs(first.coordinate - second.coordinate)
            if (separation <= tolerance or
                    separation >= minimum_separation - tolerance):
                continue

            both_opaque = (
                first.role in _OPAQUE_ROLES and
                second.role in _OPAQUE_ROLES)
            if (not both_opaque and
                    _is_allowed_window_layer_pair(
                        first,
                        second,
                        separation,
                        tolerance)):
                continue

            area = _convex_intersection_area(
                first.polygon,
                second.polygon,
                tolerance)
            if area < minimum_area - tolerance:
                continue

            overlaps.append(NearCoplanarFaceOverlap(
                prototype.stable_id,
                first.role,
                first.face_index,
                second.role,
                second.face_index,
                first.axis,
                first.coordinate,
                second.coordinate,
                first.normal_sign,
                separation,
                area))

    return tuple(overlaps)


def validate_coplanarity_audit_contract() -> None:
    """Exercise the guard with positive and negative synthetic controls.

    The authored catalog normally supplies only the desired zero-result case.
    These controls ensure that a broken scanner cannot silently pass every
    future build by returning an empty tuple for all input.
    """

    unsafe_gap = MIN_VISIBLE_LAYER_SEPARATION - 0.001
    safe_gap = MIN_VISIBLE_LAYER_SEPARATION

    _expect_near_count(
        "opaque gap below threshold",
        (_test_face("FacadePrimary", 0.0),
         _test_face("Plinth", unsafe_gap)),
        1)
    _expect_near_count(
        "opaque gap at threshold",
        (_test_face("FacadePrimary", 0.0),
         _test_face("Plinth", safe_gap)),
        0)
    _expect_near_count(
        "downward hidden contact",
        (_test_face("FacadePrimary", 0.0, axis=2, normal_sign=-1),
         _test_face(
             "Plinth",
             unsafe_gap,
             axis=2,
             normal_sign=-1)),
        0)
    _expect_near_count(
        "small metal interlock",
        (_test_face("Metal", 0.0, extent=0.2),
         _test_face("Metal", unsafe_gap, extent=0.2)),
        0)
    _expect_near_count(
        "authored facade glass relief",
        (_test_face("FacadePrimary", 0.0),
         _test_face(
             "WindowGlass",
             _GLASS_FACADE_RELIEF,
             slot_id=1)),
        0)
    _expect_near_count(
        "authored frame glass relief",
        (_test_face("WindowFrame", 0.0, slot_id=1),
         _test_face(
             "WindowGlass",
             _FRAME_GLASS_SEPARATION,
             slot_id=1)),
        0)
    _expect_near_count(
        "opaque frame collision",
        (_test_face("FacadeSecondary", 0.0),
         _test_face("WindowFrame", unsafe_gap, slot_id=1)),
        1)
    _expect_near_count(
        "unexpected glass collision",
        (_test_face("Plinth", 0.0),
         _test_face(
             "WindowGlass",
             _GLASS_FACADE_RELIEF,
             slot_id=1)),
        1)

    coplanar = _test_prototype((
        _test_face("FacadePrimary", 0.0),
        _test_face("Plinth", 0.0),
    ))
    if len(find_axis_aligned_coplanar_overlaps(coplanar)) != 1:
        raise RuntimeError(
            "Coplanarity audit contract failed: exact positive control.")


def _expect_near_count(
        label: str,
        parts: tuple[PartSpec, ...],
        expected: int) -> None:
    actual = len(find_near_coplanar_visible_overlaps(
        _test_prototype(parts)))
    if actual != expected:
        raise RuntimeError(
            f"Coplanarity audit contract failed for {label!r}: "
            f"found {actual}, expected {expected}.")


def _test_prototype(parts: tuple[PartSpec, ...]) -> PrototypeSpec:
    return PrototypeSpec(
        "coplanarity-contract-control",
        "Test",
        "Test",
        1.0,
        1.0,
        1.0,
        (0.0, 0.5, 0.0),
        (-0.5, -0.5, 0.0),
        (0.5, 0.5, 1.0),
        (),
        (),
        parts)


def _test_face(
        role: str,
        coordinate: float,
        axis: int = 1,
        normal_sign: int = 1,
        extent: float = 1.0,
        slot_id: int = 0) -> PartSpec:
    half = extent * 0.5
    if axis == 1:
        vertices: tuple[Vec3, ...] = (
            (-half, coordinate, -half),
            (-half, coordinate, half),
            (half, coordinate, half),
            (half, coordinate, -half),
        )
    elif axis == 2:
        vertices = (
            (-half, -half, coordinate),
            (half, -half, coordinate),
            (half, half, coordinate),
            (-half, half, coordinate),
        )
    else:
        raise ValueError("Synthetic audit faces support only Y and Z planes.")
    face = (0, 1, 2, 3)
    if normal_sign < 0:
        face = tuple(reversed(face))
    geometry = Geometry(vertices, (face,), (slot_id,))
    return PartSpec(
        f"control_{role}_{coordinate}_{slot_id}",
        role,
        role,
        "control",
        0.0,
        geometry)


def _axis_aligned_faces(
        prototype: PrototypeSpec,
        tolerance: float) -> Iterable[_PlanarFace]:
    for part in prototype.parts:
        geometry = part.geometry
        for face_index, face in enumerate(geometry.faces):
            vertices = tuple(geometry.vertices[index] for index in face)
            for axis in range(3):
                coordinate = sum(vertex[axis] for vertex in vertices) / len(
                    vertices)
                if any(abs(vertex[axis] - coordinate) > tolerance
                       for vertex in vertices):
                    continue

                projected_axes = tuple(
                    candidate for candidate in range(3)
                    if candidate != axis)
                polygon = tuple(
                    (vertex[projected_axes[0]], vertex[projected_axes[1]])
                    for vertex in vertices)
                signed_area = _signed_area(polygon)
                if abs(signed_area) <= tolerance:
                    break

                normal_sign = 1 if _normal_component(
                    vertices,
                    axis) > 0.0 else -1
                yield _PlanarFace(
                    part.role,
                    face_index,
                    geometry.face_slot_ids[face_index],
                    axis,
                    coordinate,
                    normal_sign,
                    polygon)
                break


def _is_allowed_window_layer_pair(
        first: _PlanarFace,
        second: _PlanarFace,
        separation: float,
        tolerance: float) -> bool:
    """Recognize only the two intentional per-window depth relationships."""

    roles = frozenset({first.role, second.role})
    if roles == frozenset({"FacadePrimary", "WindowGlass"}):
        glass = first if first.role == "WindowGlass" else second
        facade = second if glass is first else first
        return (
            glass.slot_id > 0 and
            facade.slot_id == 0 and
            abs(separation - _GLASS_FACADE_RELIEF) <= tolerance)

    if roles == frozenset({"WindowFrame", "WindowGlass"}):
        return (
            first.slot_id > 0 and
            first.slot_id == second.slot_id and
            abs(separation - _FRAME_GLASS_SEPARATION) <= tolerance)

    return False


def _normal_component(vertices: Sequence[Vec3], axis: int) -> float:
    origin = vertices[0]
    for index in range(1, len(vertices) - 1):
        first = tuple(vertices[index][value] - origin[value]
                      for value in range(3))
        second = tuple(vertices[index + 1][value] - origin[value]
                       for value in range(3))
        cross = (
            first[1] * second[2] - first[2] * second[1],
            first[2] * second[0] - first[0] * second[2],
            first[0] * second[1] - first[1] * second[0],
        )
        if abs(cross[axis]) > DEFAULT_TOLERANCE:
            return cross[axis]
    return 0.0


def _signed_area(polygon: Sequence[Vec2]) -> float:
    return sum(
        polygon[index][0] * polygon[(index + 1) % len(polygon)][1] -
        polygon[(index + 1) % len(polygon)][0] * polygon[index][1]
        for index in range(len(polygon))) * 0.5


def _convex_intersection_area(
        subject: Sequence[Vec2],
        clip: Sequence[Vec2],
        tolerance: float) -> float:
    output = list(subject)
    clip_sign = 1.0 if _signed_area(clip) >= 0.0 else -1.0
    for index, edge_start in enumerate(clip):
        if not output:
            return 0.0

        edge_end = clip[(index + 1) % len(clip)]
        source = output
        output = []
        previous = source[-1]
        previous_inside = _is_inside(
            previous,
            edge_start,
            edge_end,
            clip_sign,
            tolerance)
        for current in source:
            current_inside = _is_inside(
                current,
                edge_start,
                edge_end,
                clip_sign,
                tolerance)
            if current_inside != previous_inside:
                output.append(_line_intersection(
                    previous,
                    current,
                    edge_start,
                    edge_end))
            if current_inside:
                output.append(current)
            previous = current
            previous_inside = current_inside

    return abs(_signed_area(output)) if len(output) >= 3 else 0.0


def _is_inside(
        point: Vec2,
        edge_start: Vec2,
        edge_end: Vec2,
        clip_sign: float,
        tolerance: float) -> bool:
    cross = (
        (edge_end[0] - edge_start[0]) * (point[1] - edge_start[1]) -
        (edge_end[1] - edge_start[1]) * (point[0] - edge_start[0]))
    return cross * clip_sign >= -tolerance


def _line_intersection(
        first_start: Vec2,
        first_end: Vec2,
        second_start: Vec2,
        second_end: Vec2) -> Vec2:
    first_delta = (
        first_end[0] - first_start[0],
        first_end[1] - first_start[1])
    second_delta = (
        second_end[0] - second_start[0],
        second_end[1] - second_start[1])
    denominator = (
        first_delta[0] * second_delta[1] -
        first_delta[1] * second_delta[0])
    if abs(denominator) <= DEFAULT_TOLERANCE:
        return first_end

    start_delta = (
        second_start[0] - first_start[0],
        second_start[1] - first_start[1])
    amount = (
        start_delta[0] * second_delta[1] -
        start_delta[1] * second_delta[0]) / denominator
    return (
        first_start[0] + first_delta[0] * amount,
        first_start[1] + first_delta[1] * amount)
