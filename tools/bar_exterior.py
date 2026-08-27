#!/usr/bin/env python3
"""Authored geometry recipe for the old neighbourhood pub exterior.

The exterior uses Unity-space metres because its origin is the gameplay door:
``+X`` points out to the street, ``Y`` points up and ``Z`` runs across the
frontage.  ``build-bar-3d-model.py`` converts the finished solids to Blender's
source frame when it creates the FBX objects.

This is deliberately a real little building, not a decorated lot-sized box:
the main two-storey range has a pitched roof and gable ends, the rear beer-store
is lower and asymmetrical, the shopfront projects in three facets, and every
window is an individual sash or pub pane.  Collision, lights and interaction
remain plan-owned in Unity.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Sequence

import bar_parts as bp
import interior_kit as kit


DESIGN_ID = "bar_exterior_v2"
DISPLAY_NAME = "Bar Promenade Old Neighbourhood Pub Exterior"
GENERATOR_VERSION = "2.1.0"

# The canonical generated lot occupied by the production pub.  The hanging
# sign and canopy project past the footprint, as real frontage furniture does.
WIDTH = 12.2645
DEPTH = 13.5237
HEIGHT = 9.3435
DOOR_WIDTH = 1.45
DOOR_HEIGHT = 2.34
WALL_THICKNESS = 0.28

HALF_WIDTH = WIDTH * 0.5
MAIN_REAR_X = -8.58
MAIN_EAVE_Y = 6.12
RIDGE_X = -4.29
RIDGE_Y = 8.78
SERVICE_REAR_X = -13.34

DOOR_ANCHOR = (0.0, 0.0, 0.0)
# Mount the blade sign on the solid pier between the centre and right sash.
# Keeping it off the door/window axis prevents the bracket from reading as if
# it emerges through the centre upper glazing in oblique City views.
SIGN_PIER_Z = 1.86
SIGN_PIVOT = (0.88, 3.78, SIGN_PIER_Z)

BRICK = bp.rgb(0.30, 0.12, 0.075)
BRICK_DARK = bp.rgb(0.215, 0.075, 0.047)
PLASTER = bp.rgb(0.48, 0.44, 0.34)
PLASTER_REPAIR = bp.rgb(0.36, 0.34, 0.29)
GREEN_WOOD = bp.rgb(0.055, 0.145, 0.095)
GREEN_WOOD_DARK = bp.rgb(0.028, 0.072, 0.052)
OXBLOOD = bp.rgb(0.19, 0.045, 0.035)
CREAM_TRIM = bp.rgb(0.62, 0.49, 0.29)
ROOF = bp.rgb(0.095, 0.105, 0.115)
METAL = bp.rgb(0.07, 0.078, 0.075)
GLASS_WARM = bp.rgb(0.82, 0.48, 0.20)
GLASS_DARK = bp.rgb(0.055, 0.065, 0.072)
BRASS = bp.rgb(0.49, 0.31, 0.10)

SIGN_OUTLINE = bp.rgb(0.137, 0.071, 0.114)
SIGN_FIELD = bp.rgb(0.357, 0.086, 0.169)
SIGN_PALE = bp.rgb(0.976, 0.914, 0.722)
SIGN_DRINK = bp.rgb(0.871, 0.545, 0.165)


@dataclass(frozen=True)
class ExteriorPart:
    name: str
    role: str
    tint: dict
    geometry: kit.Geometry
    sheet: str = ""
    group: str = "fixed"
    emissive: bool = False
    shadows: bool = True


def _merged(items: Sequence[kit.Geometry]) -> kit.Geometry:
    return kit.merge_all(items)


def _rotated_box(
    center: Sequence[float],
    size: Sequence[float],
    euler: Sequence[float],
    chamfer: float = 0.01,
) -> kit.Geometry:
    geometry = bp.u_box((0.0, 0.0, 0.0), size, chamfer)
    return kit.translated(bp.u_rotated(geometry, euler), center)


def _extruded_profile_z(
    profile_xy: Sequence[tuple[float, float]],
    z_center: float,
    depth: float,
) -> kit.Geometry:
    """Extrude a counter-clockwise Unity XY profile along local Z."""
    half = depth * 0.5
    count = len(profile_xy)
    vertices = [
        (x, y, z_center - half) for x, y in profile_xy
    ] + [
        (x, y, z_center + half) for x, y in profile_xy
    ]
    faces: list[tuple[int, ...]] = [
        tuple(reversed(range(count))),
        tuple(range(count, count * 2)),
    ]
    for index in range(count):
        following = (index + 1) % count
        faces.append((
            index,
            following,
            count + following,
            count + index,
        ))
    return vertices, faces


def _front_upper_wall() -> list[kit.Geometry]:
    """Rendered street wall with three actual sash openings."""
    front_x = -0.12
    thickness = 0.28
    opening_bottom = 3.78
    opening_top = 5.52
    wall_bottom = 3.28
    wall_top = MAIN_EAVE_Y
    result = [
        bp.u_box(
            (front_x, (wall_bottom + opening_bottom) * 0.5, 0.0),
            (thickness, opening_bottom - wall_bottom, 12.0),
            0.012),
        bp.u_box(
            (front_x, (opening_top + wall_top) * 0.5, 0.0),
            (thickness, wall_top - opening_top, 12.0),
            0.012),
    ]
    # Solid intervals left between the three sash openings.
    for low, high in (
        (-6.0, -4.47),
        (-3.03, -0.74),
        (0.74, 3.03),
        (4.47, 6.0),
    ):
        result.append(bp.u_box(
            (front_x, (opening_bottom + opening_top) * 0.5,
             (low + high) * 0.5),
            (thickness, opening_top - opening_bottom, high - low),
            0.012))
    return result


def _roof_planes() -> tuple[kit.Geometry, kit.Geometry]:
    overhang_x = 0.20
    front_eave = (overhang_x, MAIN_EAVE_Y)
    rear_eave = (MAIN_REAR_X - overhang_x, MAIN_EAVE_Y)
    front_run = front_eave[0] - RIDGE_X
    rear_run = RIDGE_X - rear_eave[0]
    rise = RIDGE_Y - MAIN_EAVE_Y
    front_angle = -math.degrees(math.atan2(rise, front_run))
    rear_angle = math.degrees(math.atan2(rise, rear_run))
    front_length = math.hypot(front_run, rise) + 0.18
    rear_length = math.hypot(rear_run, rise) + 0.18
    front = _rotated_box(
        ((front_eave[0] + RIDGE_X) * 0.5,
         (front_eave[1] + RIDGE_Y) * 0.5,
         0.0),
        (front_length, 0.22, WIDTH - 0.03),
        (0.0, 0.0, front_angle),
        0.012)
    rear = _rotated_box(
        ((rear_eave[0] + RIDGE_X) * 0.5,
         (rear_eave[1] + RIDGE_Y) * 0.5,
         0.0),
        (rear_length, 0.22, WIDTH - 0.03),
        (0.0, 0.0, rear_angle),
        0.012)
    return front, rear


def _bay_segment(
    center_z: float,
    side: int,
    y: float,
    height: float,
    thickness: float,
    width: float = 0.66,
) -> kit.Geometry:
    # Left facet advances toward the centre as Z increases; right facet is
    # its mirror.  Twenty-eight degrees remains shallow enough to read at the
    # game's oblique camera while throwing a real silhouette highlight.
    angle = 28.0 if side < 0 else -28.0
    return _rotated_box(
        (0.18, y, center_z + side * 1.39),
        (thickness, height, width),
        (0.0, angle, 0.0),
        min(0.008, thickness * 0.20))


def _bay_glass(center_z: float, y: float, height: float) -> list[kit.Geometry]:
    return [
        bp.u_plate((0.345, y, center_z), (0.055, height, 2.18)),
        _bay_segment(center_z, -1, y, height, 0.055),
        _bay_segment(center_z, 1, y, height, 0.055),
    ]


def _bay_panels(center_z: float) -> list[kit.Geometry]:
    return [
        bp.u_box((0.29, 0.49, center_z), (0.28, 0.78, 2.20), 0.012),
        _bay_segment(center_z, -1, 0.49, 0.78, 0.22),
        _bay_segment(center_z, 1, 0.49, 0.78, 0.22),
    ]


def _bay_frames(center_z: float) -> list[kit.Geometry]:
    result: list[kit.Geometry] = []
    # Horizontal rails on the broad central pane and both faceted returns.
    for y in (0.91, 2.64, 2.94):
        result.append(bp.u_box(
            (0.39, y, center_z), (0.12, 0.13, 2.34), 0.008))
        result.append(_bay_segment(center_z, -1, y, 0.13, 0.12, 0.72))
        result.append(_bay_segment(center_z, 1, y, 0.13, 0.12, 0.72))

    # Uprights at the outer edges, facet joints and central mullion.
    for z_offset, x in (
        (-1.72, 0.04), (-1.08, 0.34), (0.0, 0.40),
        (1.08, 0.34), (1.72, 0.04),
    ):
        result.append(bp.u_box(
            (x, 1.92, center_z + z_offset),
            (0.13, 2.03, 0.13),
            0.008))
    # Two quieter glazing bars keep the panes traditional rather than reading
    # as modern plate glass.
    for z_offset in (-0.54, 0.54):
        result.append(bp.u_box(
            (0.41, 1.82, center_z + z_offset),
            (0.10, 1.56, 0.075),
            0.006))
    return result


def _upper_window_frames() -> list[kit.Geometry]:
    result: list[kit.Geometry] = []
    for center_z in (-3.75, 0.0, 3.75):
        for z_offset in (-0.80, 0.80):
            result.append(bp.u_box(
                (0.055, 4.65, center_z + z_offset),
                (0.16, 1.96, 0.14),
                0.008))
        for y in (3.70, 4.65, 5.60):
            result.append(bp.u_box(
                (0.055, y, center_z),
                (0.16, 0.14, 1.72),
                0.008))
        result.append(bp.u_box(
            (0.07, 4.65, center_z),
            (0.12, 1.76, 0.09),
            0.006))
    return result


def build_parts() -> list[ExteriorPart]:
    parts: list[ExteriorPart] = []

    brick_shell = [
        # Long side walls and the rear wall of the two-storey range.
        bp.u_box((-4.29, 3.06, -5.99),
                 (8.58, MAIN_EAVE_Y, WALL_THICKNESS), 0.012),
        bp.u_box((-4.29, 3.06, 5.99),
                 (8.58, MAIN_EAVE_Y, WALL_THICKNESS), 0.012),
        bp.u_box((MAIN_REAR_X, 3.06, 0.0),
                 (WALL_THICKNESS, MAIN_EAVE_Y, 12.0), 0.012),
        # Ground-floor masonry piers hold the timber frontage.
        bp.u_box((-0.12, 1.64, -5.78), (0.30, 3.28, 0.44), 0.012),
        bp.u_box((-0.12, 1.64, 5.78), (0.30, 3.28, 0.44), 0.012),
        # The lower rear beer store is intentionally narrower and off-centre.
        bp.u_box((-10.96, 2.20, -0.55), (4.76, 4.40, 9.48), 0.016),
    ]
    gable_profile = (
        (MAIN_REAR_X, MAIN_EAVE_Y),
        (0.0, MAIN_EAVE_Y),
        (RIDGE_X, RIDGE_Y),
    )
    brick_shell.extend((
        _extruded_profile_z(gable_profile, -5.99, WALL_THICKNESS),
        _extruded_profile_z(gable_profile, 5.99, WALL_THICKNESS),
    ))
    parts.append(ExteriorPart(
        "Pub Brick Shell", "exterior_masonry", BRICK,
        _merged(brick_shell), "ExteriorBrick"))

    parts.append(ExteriorPart(
        "Pub Rendered Upper Storey", "exterior_plaster", PLASTER,
        _merged(_front_upper_wall()), "ExteriorPlaster"))

    # Local repairs prevent either side wall from reading as one untouched
    # texture slab.  Their shallow relief catches light without adding props to
    # the shared yard.
    parts.append(ExteriorPart(
        "Pub Masonry Repairs", "exterior_plaster", PLASTER_REPAIR,
        _merged([
            bp.u_plate((-6.48, 2.30, -6.145), (2.05, 1.18, 0.045)),
            bp.u_plate((-2.05, 4.62, 6.145), (1.42, 0.72, 0.045)),
            bp.u_plate((-12.52, 3.18, 4.22), (1.05, 0.62, 0.045)),
        ]),
        "ExteriorPlaster"))

    front_roof, rear_roof = _roof_planes()
    service_run = 4.86
    service_rise = 0.46
    service_angle = math.degrees(math.atan2(service_rise, service_run))
    service_roof = _rotated_box(
        (-10.93, 4.76, -0.55),
        (math.hypot(service_run, service_rise) + 0.18,
         0.20,
         9.82),
        (0.0, 0.0, service_angle),
        0.012)
    parts.append(ExteriorPart(
        "Pub Slate Roof", "exterior_roof", ROOF,
        _merged((front_roof, rear_roof, service_roof,
                 bp.u_box((RIDGE_X, RIDGE_Y + 0.055, 0.0),
                          (0.28, 0.18, 12.22), 0.012))),
        "CityRoof"))

    chimney_bodies = [
        bp.u_box((-5.72, 8.40, -3.75), (1.02, 1.56, 1.08), 0.018),
        bp.u_box((-2.52, 8.25, 3.52), (0.82, 1.42, 0.92), 0.018),
        bp.u_box((-5.72, 9.11, -3.75), (1.16, 0.16, 1.20), 0.012),
        bp.u_box((-2.52, 8.91, 3.52), (0.94, 0.14, 1.04), 0.012),
    ]
    parts.append(ExteriorPart(
        "Pub Brick Chimneys", "exterior_masonry", BRICK_DARK,
        _merged(chimney_bodies), "ExteriorBrick"))

    pots = []
    for z in (-4.01, -3.49):
        pots.append(bp.u_tapered_cylinder(
            (-5.72, 9.245, z), (0.20, 0.0985, 0.20), 1.12, 10))
    pots.append(bp.u_tapered_cylinder(
        (-2.52, 9.075, 3.52), (0.18, 0.10, 0.18), 1.10, 10))
    parts.append(ExteriorPart(
        "Pub Chimney Pots", "exterior_masonry", BRICK_DARK,
        _merged(pots), "ExteriorBrick"))

    # Bottle-green frontage: broad fascia, framed base, pilasters and the two
    # faceted display windows.  The door remains exactly at local zero.
    shopfront = [
        bp.u_box((0.03, 3.04, 0.0), (0.28, 0.52, 11.58), 0.016),
        bp.u_box((0.11, 3.36, 0.0), (0.42, 0.18, 11.96), 0.014),
        bp.u_box((0.05, 0.18, 0.0), (0.24, 0.36, 11.62), 0.012),
    ]
    for z in (-5.63, -1.12, 1.12, 5.63):
        shopfront.append(bp.u_box(
            (0.08, 1.68, z), (0.30, 2.92, 0.25), 0.012))
    for center_z in (-3.38, 3.38):
        shopfront.extend(_bay_panels(center_z))
        shopfront.extend(_bay_frames(center_z))
    parts.append(ExteriorPart(
        "Pub Bottle Green Shopfront", "exterior_wood", GREEN_WOOD,
        _merged(shopfront), "DarkWood"))

    # A subdued second paint generation is visible on the lower panels and
    # central muntins.  It reads as repair history, not decorative striping.
    accent = []
    for center_z in (-3.38, 3.38):
        for z_offset in (-0.72, 0.0, 0.72):
            accent.append(bp.u_plate(
                (0.445, 0.49, center_z + z_offset),
                (0.035, 0.55, 0.07)))
    parts.append(ExteriorPart(
        "Pub Oxblood Panel Details", "exterior_wood", OXBLOOD,
        _merged(accent), "DarkWood"))

    ground_glass: list[kit.Geometry] = []
    for center_z in (-3.38, 3.38):
        ground_glass.extend(_bay_glass(center_z, 1.83, 1.70))
        ground_glass.extend(_bay_glass(center_z, 2.78, 0.30))
    parts.append(ExteriorPart(
        "Pub Ground Floor Glass", "exterior_window_ground", GLASS_WARM,
        _merged(ground_glass), emissive=True, shadows=False))

    # The faceted bays stop short of the two inner shopfront pilasters. Fill
    # those interstitial strips with solid shopfront cheeks; otherwise the
    # rear wall of the empty building shell is visible beside the entrance.
    # Small overlaps tuck them under the plinth, fascia and neighbouring
    # timber members while their face stays aligned with the pilasters.
    entrance_flanks = [
        bp.u_box((0.08, 1.68, side * 1.42),
                 (0.30, 2.92, 0.44), 0.012)
        for side in (-1, 1)
    ]
    parts.append(ExteriorPart(
        "Bar Entrance Flanking Panels", "exterior_wood", GREEN_WOOD,
        _merged(entrance_flanks), "DarkWood"))

    # Mirror the same closure at both outer bay edges. The bay uprights stop
    # short of the end pilasters, so these recessed cheeks remove the last
    # two sightlines into the otherwise open ground-floor shell.
    outer_flanks = [
        bp.u_box((-0.03, 1.68, side * 5.335),
                 (0.22, 2.92, 0.40), 0.012)
        for side in (-1, 1)
    ]
    parts.append(ExteriorPart(
        "Bar Outer Bay Flanking Panels", "exterior_wood", GREEN_WOOD,
        _merged(outer_flanks), "DarkWood"))

    # The door is recessed behind the projecting shopfront. These full-depth
    # jamb returns and the upper soffit close that recess against oblique
    # gameplay views; thin face trim alone exposes the otherwise empty shell.
    portal_reveals = [
        bp.u_box((-0.07, 1.29, side * 0.86),
                 (0.34, 2.58, 0.30), 0.012)
        for side in (-1, 1)
    ]
    portal_reveals.append(
        bp.u_box((-0.07, 2.68, 0.0),
                 (0.34, 0.20, 2.02), 0.012))
    parts.append(ExteriorPart(
        "Bar Entrance Reveal Panels", "exterior_wood", GREEN_WOOD,
        _merged(portal_reveals), "DarkWood"))

    # Recessed panelled entrance and preserved semantic names.
    parts.append(ExteriorPart(
        "Bar Door", "exterior_door", GREEN_WOOD_DARK,
        bp.u_box((-0.16, 1.17, 0.0), (0.16, DOOR_HEIGHT, DOOR_WIDTH), 0.014),
        "DarkWood"))
    for side in (-1, 1):
        parts.append(ExteriorPart(
            f"Bar Door Frame {'Left' if side < 0 else 'Right'}",
            "exterior_wood", CREAM_TRIM,
            bp.u_box((0.035, 1.26, side * 0.84),
                     (0.22, 2.55, 0.17), 0.012),
            "DarkWood"))
    parts.append(ExteriorPart(
        "Bar Door Header", "exterior_wood", CREAM_TRIM,
        bp.u_box((0.035, 2.48, 0.0), (0.22, 0.20, 1.85), 0.012),
        "DarkWood"))
    parts.append(ExteriorPart(
        "Bar Door Panels", "exterior_wood", OXBLOOD,
        _merged([
            bp.u_plate((-0.055, 0.62, -0.36), (0.035, 0.72, 0.54)),
            bp.u_plate((-0.055, 0.62, 0.36), (0.035, 0.72, 0.54)),
            bp.u_plate((-0.055, 1.55, -0.36), (0.035, 0.66, 0.54)),
            bp.u_plate((-0.055, 1.55, 0.36), (0.035, 0.66, 0.54)),
        ]),
        "DarkWood"))
    parts.append(ExteriorPart(
        "Bar Door Transom Glass", "exterior_window_ground", GLASS_WARM,
        bp.u_plate((0.055, 2.20, 0.0), (0.05, 0.38, 1.28)),
        emissive=True, shadows=False))
    parts.append(ExteriorPart(
        "Bar Door Furniture", "exterior_metal", BRASS,
        _merged([
            _rotated_box((0.025, 1.13, 0.43), (0.11, 0.07, 0.18),
                         (0.0, 0.0, 0.0), 0.006),
            bp.u_plate((0.02, 1.35, 0.43), (0.045, 0.22, 0.12)),
        ]),
        shadows=False))

    parts.append(ExteriorPart(
        "Bar Entrance Canopy", "exterior_wood", GREEN_WOOD_DARK,
        bp.u_box((0.50, 2.70, 0.0), (1.12, 0.20, 2.72), 0.018),
        "DarkWood"))
    parts.append(ExteriorPart(
        "Bar Entrance Canopy Inset", "exterior_metal", METAL,
        bp.u_box((0.54, 2.63, 0.0), (1.02, 0.08, 2.48), 0.012),
        shadows=False))

    upper_glass_warm = []
    upper_glass_dark = []
    for center_z in (-3.75, 0.0, 3.75):
        target = upper_glass_dark if center_z == 0.0 else upper_glass_warm
        target.append(bp.u_plate(
            (0.025, 4.65, center_z), (0.055, 1.72, 1.42)))
    parts.append(ExteriorPart(
        "Pub Upper Sash Frames", "exterior_wood", CREAM_TRIM,
        _merged(_upper_window_frames()), "DarkWood"))
    parts.append(ExteriorPart(
        "Pub Upper Windows Warm", "exterior_window_upper_warm", GLASS_WARM,
        _merged(upper_glass_warm), emissive=True, shadows=False))
    parts.append(ExteriorPart(
        "Pub Upper Window Dark", "exterior_window_upper_dark", GLASS_DARK,
        _merged(upper_glass_dark), shadows=False))
    parts.append(ExteriorPart(
        "Pub Upper Stone Sills", "exterior_plaster", PLASTER_REPAIR,
        _merged([
            bp.u_box((0.08, 3.69, center_z), (0.34, 0.12, 1.92), 0.008)
            for center_z in (-3.75, 0.0, 3.75)
        ]),
        "ExteriorPlaster"))

    # Side-service clues are restrained so the left shared yard stays open.
    parts.append(ExteriorPart(
        "Pub Side Service Door", "exterior_wood", GREEN_WOOD_DARK,
        bp.u_box((-11.18, 1.08, -5.315), (1.28, 2.16, 0.10), 0.014),
        "DarkWood"))
    parts.append(ExteriorPart(
        "Pub Bricked Window Patch", "exterior_masonry", BRICK_DARK,
        bp.u_plate((-6.48, 2.30, -6.175), (1.56, 1.34, 0.045)),
        "ExteriorBrick"))
    parts.append(ExteriorPart(
        "Pub Cellar Hatch", "exterior_metal", METAL,
        bp.u_box((-13.405, 0.74, 2.22), (0.10, 1.18, 1.62), 0.012)))

    rain_goods = [
        kit.translated(
            bp.u_rotated(
                bp.u_cylinder((0.0, 0.0, 0.0), (0.17, 6.08, 0.17), 10),
                (90.0, 0.0, 0.0)),
            (0.12, 6.02, 0.0)),
        kit.translated(
            bp.u_rotated(
                bp.u_cylinder((0.0, 0.0, 0.0), (0.15, 4.84, 0.15), 10),
                (90.0, 0.0, 0.0)),
            (-10.93, 4.56, -0.55)),
        bp.u_cylinder((-0.02, 3.00, -5.86), (0.15, 3.0, 0.15), 10),
        bp.u_cylinder((-8.52, 2.86, 5.88), (0.14, 2.86, 0.14), 10),
        bp.u_cylinder((-13.39, 2.20, -5.18), (0.14, 2.20, 0.14), 10),
    ]
    parts.append(ExteriorPart(
        "Pub Gutters And Downpipes", "exterior_metal", METAL,
        _merged(rain_goods)))

    # The bracket remains on the fixed facade; everything after it is authored
    # around SIGN_PIVOT and regrouped beneath Bar Landmark Marker at runtime.
    parts.append(ExteriorPart(
        "Bar Sign Bracket", "exterior_metal", METAL,
        _merged([
            bp.u_box((0.47, 4.24, SIGN_PIER_Z),
                     (0.96, 0.10, 0.10), 0.008),
            _rotated_box((0.37, 4.04, SIGN_PIER_Z),
                         (0.08, 0.52, 0.08),
                         (0.0, 0.0, -28.0), 0.006),
        ])))

    sign_group = "pivot:Bar Landmark Marker"
    for label, along in (("Inner", -0.30), ("Outer", 0.30)):
        parts.append(ExteriorPart(
            f"Bar Sign Hanger {label}", "sign_part", SIGN_OUTLINE,
            bp.u_box((along, 0.46, 0.0), (0.06, 0.30, 0.05), 0.006),
            group=sign_group, shadows=False))
    for name, size, tint in (
        ("Bar Sign Panel", (0.90, 0.70, 0.10), SIGN_OUTLINE),
        ("Bar Sign Panel Frame", (0.84, 0.64, 0.12), CREAM_TRIM),
        ("Bar Sign Panel Field", (0.72, 0.52, 0.13), SIGN_FIELD),
    ):
        parts.append(ExteriorPart(
            name, "sign_part", tint,
            bp.u_box((0.0, 0.0, 0.0), size, 0.008),
            group=sign_group, shadows=False))
    for name, offset, size, tint in (
        ("Bar Sign Tankard", (-0.05, 0.0, 0.0),
         (0.26, 0.34, 0.15), SIGN_PALE),
        ("Bar Sign Tankard Fill", (-0.05, -0.04, 0.0),
         (0.18, 0.20, 0.16), SIGN_DRINK),
        ("Bar Sign Tankard Handle", (0.13, -0.02, 0.0),
         (0.09, 0.18, 0.14), SIGN_PALE),
    ):
        parts.append(ExteriorPart(
            name, "sign_part", tint,
            bp.u_box(offset, size, 0.006),
            group=sign_group, shadows=False))

    return parts
