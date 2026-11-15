#!/usr/bin/env python3
"""Build the Ferryman's working sedan, its passive Unity model and measured art contract.

Blender metres, Z-up, forward -Y. The Unity prefab rotates the imported model
to the runtime's +Z, keeping the pivots and contact anchors intact.
"""

from __future__ import annotations

import argparse
import bmesh
import hashlib
import json
import math
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence

try:
    import bpy
    from mathutils import Matrix, Vector
    from mathutils.bvhtree import BVHTree
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise RuntimeError(
        "This generator must run through Blender's bundled Python."
    ) from error


sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import last_route_car_forms as forms
from last_route_car_cabin_atlas import PATCHES, PATCH_GRID, cabin_patch

GENERATOR_VERSION = "2.0.0"
DESIGN_ID = "last_route_ferry_car_v2"
DISPLAY_NAME = "Last Route Ferry Car"
SEED = 470301

LENGTH = 4.83
WIDTH = 1.80
HEIGHT = 1.57
WHEELBASE = 2.70
WHEEL_RADIUS = 0.34
WHEEL_WIDTH = 0.19
MIN_TRIANGLES = 7000
MAX_TRIANGLES = 24000

SOURCE_COLLECTION = "SOURCE_LastRouteCar3D"
PRESENTATION_COLLECTION = "PRESENTATION_LastRouteCar3D"
ROOT_NAME = "SRC_LastRouteCar3D"
BODY_NAME = "ROOT_Body"

# The body plan, in source metres. Front is -Y, driver side is +X.
FRONT_Y = -LENGTH * 0.5
REAR_Y = LENGTH * 0.5
BODY_HALF_WIDTH = WIDTH * 0.5
TRACK_HALF = 0.74
FRONT_AXLE_Y = -WHEELBASE * 0.5
REAR_AXLE_Y = WHEELBASE * 0.5

WHEEL_TOP_Z = WHEEL_RADIUS * 2.0
ARCH_HALF_LENGTH = 0.62  # half the wheel-arch opening along the body

CABIN_FRONT_Y = -1.24   # the scuttle; ahead of it is engine bay
CABIN_REAR_Y = 1.10     # the parcel shelf; behind it is boot

FLOOR_Z = 0.30          # cabin floor: the plane a seated root stands on
SILL_Z = 0.44           # top of the rocker
WAIST_Z = 1.02          # waistline; the rail he rests his forearm on
BONNET_Z = 1.08
ROOF_UNDERSIDE_Z = 1.505
SEAT_PELVIS_Z = 0.52
SEAT_Y = -0.40          # pelvis row, centred in the door opening
SEAT_HALF_SPAN_X = 0.44
SEAT_CUSHION_WIDTH = 0.52  # also where the rocker has to stop, inboard

# The contact contract shared with tools/build-city-pedestrian-3d-model.py.
# The Ferryman does not lean on this car, he sits on its bonnet with his
# boots on the bumper and watches whoever is walking up - which is why the
# car is parked nose-out at the way in. His authored pose has to put his
# backside and his soles exactly on these two points, and one EditMode test
# reads both manifests and fails the moment either generator moves.
#
# The drop between them is the contract the pedestrian rig calls
# `perch_seat_height_m`: the distance from a sitter's seat to his own soles.
PERCH_SEAT = (-0.28, -2.222, BONNET_Z - .0036)
PERCH_SOLES = (-0.28, -2.485, 0.575)
PERCH_DROP_M = PERCH_SEAT[2] - PERCH_SOLES[2]

# The steering wheel is authored flat in its pivot's space and raked by the
# pivot, the way the bus does it, so a later turn of the rim carries the grips
# and therefore the driver's hand IK with it.
STEERING_WHEEL_CENTER = (0.44, -0.88, 0.92)
STEERING_WHEEL_RAKE_DEGREES = -65.0
STEERING_WHEEL_MAJOR_RADIUS = 0.175
STEERING_WHEEL_MINOR_RADIUS = 0.019
STEERING_GRIP_X = 0.135
STEERING_GRIP_TOP = math.sqrt(
    STEERING_WHEEL_MAJOR_RADIUS ** 2 - STEERING_GRIP_X ** 2
)
STEERING_GRIP_AXIS_OFFSET = 0.018

# The dashboard, and the two things on it the passenger can touch. The
# slab's rear face is the plane every control sits on. The glovebox is a
# HOLE in that slab - aperture by omission, the doorway's rule - with a lid
# hinged along the aperture's bottom edge that drops towards the sitter;
# the radio is a chrome frame round a dial with a knob either side of it.
# Everything else drawn in `_build_dashboard` is furniture the hero looks at
# and never uses. All of it is measured off the passenger's own eye, source
# (-0.44, -0.52, 1.30), because that is the only place any of it is seen
# from: the glovebox lid sits 41 degrees below that eye, the radio 33
# degrees below and 47 degrees towards the driver.
DASH_SLAB_Y = -1.05
DASH_SLAB_DEPTH = 0.24
DASH_REAR_Y = -0.93       # the face; DASH_SLAB_Y + DASH_SLAB_DEPTH / 2
DASH_BOTTOM_Z = 0.84
DASH_TOP_Z = 1.04
DASH_HALF_WIDTH = 0.73
# The glovebox aperture, on the passenger side (-X). The lid's height is
# capped so that, hinged at the aperture's bottom edge and dropped fully
# open, it stays a hand's breadth above a seated passenger's knees.
GLOVEBOX_X = (-0.62, -0.30)
GLOVEBOX_Z = (0.88, 1.023)
GLOVEBOX_HINGE = (
    (GLOVEBOX_X[0] + GLOVEBOX_X[1]) * 0.5,
    DASH_REAR_Y,
    GLOVEBOX_Z[0],
)
GLOVEBOX_LID_HEIGHT = GLOVEBOX_Z[1] - GLOVEBOX_Z[0] + .005
GLOVEBOX_LID_LAP = 0.01
GLOVEBOX_LID_OPEN_DEGREES = 78.0
# The radio, dead centre. The dial is a strip the needle slides along from
# the passenger's end towards the driver's; the power knob is on the
# driver's side - the passenger's LEFT hand - and the tuning knob on the
# passenger's, the way a radio is laid out.
RADIO_CENTER = (0.0, DASH_REAR_Y, 0.91)
RADIO_DIAL_WIDTH = 0.14
RADIO_DIAL_Z = 0.93
RADIO_NEEDLE_TRAVEL_M = 0.12
RADIO_KNOB_X = 0.09
RADIO_KNOB_Z = 0.893
# The binnacle. The speedometer sits inboard so the passenger sees it past
# the rim; its needle is the one moving thing on the dash the hero cannot
# touch. The fuel gauge is outboard and its needle is drawn at "empty".
SPEEDO_CENTER = (0.385, -0.995, 1.02)
FUEL_GAUGE_X = 0.495

# The plafond over the front seats, and the bulb in the glovebox. Both are
# drawn because this world's light has visible causes: the summit's every
# fixture carries its own halo, the bus's cabin lamps are pendant bulbs you
# can see, and a cone arriving from nothing is the one thing the art bible
# does not allow.
#
# The plafond hangs on the centreline, between the two heads rather than
# over either of them - a seated crown reaches z 1.51-1.62 but only over
# x -0.70..-0.18 and 0.18..0.70, so the middle of the roof is the one place
# a lamp can hang without being worn as a hat. It is FORWARD of the mirror
# (y -0.895..-0.865) and clear of the header rail (y -0.95..-0.81) by five
# centimetres, and its housing bites 3 cm up into the roof slab so no lit
# face is ever coplanar with the headlining - the seam rule the car's own
# headlights and the bus's cabin strip both paid for.
CABIN_LAMP_Y = -0.720
CABIN_LAMP_HOUSING = ((0.0, CABIN_LAMP_Y, 1.520), (0.170, 0.110, 0.050))
CABIN_LAMP_LENS = ((0.0, CABIN_LAMP_Y, 1.4845), (0.120, 0.076, 0.041))
# And the bulb, in the roof of the compartment just inside the opening, so
# that dropping the lid shows the lamp itself and not only its pool. That
# is what was asked for; a real glovebox hides its bulb behind the lip.
GLOVEBOX_BULB = ((-0.460, -0.960, 0.984), (0.048, 0.032, 0.044))

DOOR_HINGE_Y = -1.17    # the A-pillar base; the door opens forward of it
DOOR_HINGE_Z = 0.73
DOOR_LEAF_LENGTH = 1.68
B_PILLAR_Y = DOOR_HINGE_Y + DOOR_LEAF_LENGTH
# How far the shut leaf laps the jamb it sits against. The leaf is a skin
# hung OUTBOARD of the flank rather than a plug in the hole, so this only
# has to beat the grazing angle at which a coincident edge shows a seam.
DOOR_LEAF_LAP = 0.010
# The hole in the bodywork that the leaf covers - stated here, in the same
# breath as the leaf, because the two drifting apart is exactly the failure
# this is fixing. For a whole build the flank had no hole at all: opening a
# door revealed thirteen centimetres of bodywork filling the whole doorway.
DOORWAY_FRONT_Y = DOOR_HINGE_Y + DOOR_LEAF_LAP
DOORWAY_REAR_Y = DOOR_HINGE_Y + DOOR_LEAF_LENGTH - DOOR_LEAF_LAP
# The front-lower corner of that doorway opens onto the front wheel arch,
# so cutting it also has to line the arch - see `_build_interior`.
ARCH_REAR_Y = FRONT_AXLE_Y + ARCH_HALF_LENGTH
# The front handle, in the hinge's own space, so it swings with the leaf it
# belongs to. Derived from the waist line rather than typed twice.
DOOR_HANDLE_Y = -0.62 - DOOR_HINGE_Y
DOOR_HANDLE_Z = (WAIST_Z - 0.11) - DOOR_HINGE_Z
DOOR_SPECS = (
    ("Driver", 1.0, "driver_door_leaf", "Body"),
    # The passenger door came off another car and never got repainted. It is
    # the loudest thing about the wreck and the cheapest to state: one slot.
    ("Passenger", -1.0, "passenger_door_leaf", "AccentPaint"),
)

# Coordinates in the painted skin, where the metal is actually displaced.
DENTS = (
    (1., -.28, .75, .29, .21, .036),
    (-1., .84, .71, .23, .19, .032),
    (1., 1.72, .86, .25, .16, .022),
)

def smooth01(value):
    t = min(1., max(0., value))
    return t*t*(3.-2.*t)


def apron_blend(y):
    return 1. - smooth01((y - FRONT_Y) / .46)


def lower_wing_retract(y, z):
    return .133 * apron_blend(y) * (1.-smooth01((z-.35)/.27))


def flank_x(side, y, z):
    # Tuck the ends and the sill. A continuous shoulder catches a broad,
    # restrained highlight across the doors and the fenders.
    end_taper = .104 * (abs(y) / (LENGTH * .5)) ** 5
    shoulder = .9 - .039 * ((z - .82) / .55) ** 2 - end_taper
    fender = .014 * sum(math.exp(-((y - axle) / .41) ** 4) *
                        math.exp(-((z - .73) / .14) ** 2)
                        for axle in (FRONT_AXLE_Y, REAR_AXLE_Y))
    dent = sum(depth * math.exp(-((y - yy) / w) ** 2 - ((z - zz) / h) ** 2)
               for sign, yy, zz, w, h, depth in DENTS if sign == side)
    tucked = .098 * apron_blend(y) * (1.-smooth01((z-.34)/.26))
    return min(.902, shoulder + fender - dent - tucked)


def hood_z(x, y):
    crown = .057
    # Reference height at the actual perch, including the stamping's crown.
    base = BONNET_Z - crown * (1. - (.28 / .79) ** 2)
    return base + crown * (1. - (x / .79) ** 2) + .018 * (y + 2.02)


def arch_base(y):
    return max(
        forms.arch_floor(y, FRONT_AXLE_Y, .318), forms.arch_floor(y, REAR_AXLE_Y, .318))


def section_ys(first, last):
    arch_points = forms.arch_stations(FRONT_AXLE_Y, .318) + forms.arch_stations(REAR_AXLE_Y, .318)
    dents = [y + d for _, y, _, _, _, _ in DENTS for d in (-.30, -.19, -.09, 0., .09, .19, .30)]
    return [first, last] + [y for y in arch_points + dents if first < y < last]


UV_TILE_METERS_DEFAULT = 1.10
UV_TILE_METERS = {
    "Body": 1.4,
    "AccentPaint": 1.4,
    "Rust": 0.55,
    "Trim": 0.9,
    "Chrome": 0.9,
    "Interior": 1.0,
    "Dashboard": 0.8,
    "RadioDial": 0.3,
    "Seat": 0.6,
    "Metal": 0.9,
    "Rubber": 0.5,
}


MATERIALS: dict[str, tuple[tuple[float, float, float, float], float, float, float]] = {
    # slot: (rgba, metallic, roughness, emission strength)
    "Body": ((0.155, 0.150, 0.135, 1.0), 0.04, 0.72, 0.0),
    "AccentPaint": ((0.115, 0.165, 0.130, 1.0), 0.03, 0.78, 0.0),
    "Rust": ((0.150, 0.083, 0.052, 1.0), 0.02, 0.95, 0.0),
    "Trim": ((0.040, 0.042, 0.040, 1.0), 0.05, 0.80, 0.0),
    "Chrome": ((0.42, 0.44, 0.43, 1.0), 0.72, 0.34, 0.0),
    "Rubber": ((0.017, 0.019, 0.018, 1.0), 0.0, 0.93, 0.0),
    "Metal": ((0.20, 0.22, 0.21, 1.0), 0.55, 0.42, 0.0),
    "Glass": ((0.16, 0.23, 0.24, 0.22), 0.04, 0.20, 0.0),
    "CrackedGlass": ((0.62, 0.66, 0.64, 0.55), 0.04, 0.46, 0.0),
    "BrokenGlass": ((0.055, 0.060, 0.058, 1.0), 0.10, 0.55, 0.0),
    "Interior": ((0.085, 0.085, 0.080, 1.0), 0.0, 0.80, 0.0),
    "Seat": ((0.135, 0.115, 0.085, 1.0), 0.0, 0.86, 0.0),
    "Dashboard": ((0.062, 0.060, 0.055, 1.0), 0.0, 0.74, 0.0),
    # The radio's dial. Warm amber and emissive in the preview; in Unity the
    # material keeps its emission keyword and the runtime writes the colour
    # black until the radio is switched on.
    "RadioDial": ((0.93, 0.62, 0.22, 1.0), 0.0, 0.55, 1.2),
    # The cabin's own lit surfaces: the plafond lens over the seats, the
    # glovebox bulb and the two instrument faces. ONE slot for all three,
    # because the authored value only fixes the colour and keeps Unity's
    # emission keyword alive - the runtime writes each renderer's actual
    # level through its own property block. The faces used to sit on
    # `Plate`, which is ALSO the number plate hanging off the nose of the
    # car: lighting the dials on that slot would have set the plate
    # glowing in the dark.
    "CabinLamp": ((0.949, 0.808, 0.620, 1.0), 0.0, 0.34, 1.05),
    "Headlight": ((0.98, 0.86, 0.68, 1.0), 0.02, 0.18, 1.85),
    "TailLight": ((0.45, 0.043, 0.018, 1.0), 0.0, 0.30, 0.4),
    "MarkerLens": ((0.44, 0.20, 0.038, 1.0), 0.0, 0.64, 0.0),
    "InstrumentInk": ((0.092, 0.079, 0.064, 1.0), 0.0, 0.52, 0.0),
    "Carpet": ((0.105, 0.094, 0.087, 1.0), 0.0, 0.94, 0.0),
    "Plate": ((0.60, 0.60, 0.55, 1.0), 0.05, 0.62, 0.0),
}

@dataclass(frozen=True)
class Part:
    obj: bpy.types.Object
    role: str
    material_slot: str


@dataclass(frozen=True)
class Pivot:
    obj: bpy.types.Object
    role: str
    runtime_axis_local: str
    travel_m: float


@dataclass(frozen=True)
class BuildResult:
    root: bpy.types.Object
    body: bpy.types.Object
    parts: tuple[Part, ...]
    pivots: tuple[Pivot, ...]
    source_objects: tuple[bpy.types.Object, ...]
    # The dashboard slab's boxes as authored, for the same reason: the
    # glovebox aperture is a hole that only exists as an omission.
    dash_boxes: tuple[
        tuple[tuple[float, float, float], tuple[float, float, float]], ...
    ] = ()
    # Everything `_build_dashboard` drew, so the validator can prove none
    # of it stands inside a sitter or across the windscreen.
    dashboard_parts: tuple[Part, ...] = ()


@dataclass(frozen=True)
class ValidationReport:
    mesh_count: int
    triangle_count: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]
    build_signature: str


class MeshAccumulator:
    def __init__(self) -> None:
        self.vertices: list[tuple[float, float, float]] = []
        self.faces: list[tuple[int, ...]] = []

    def add_box(
        self,
        center: Sequence[float],
        size: Sequence[float],
    ) -> None:
        # Roll the edges in the source, before UVs and imported normals.
        # Thickness determines the bevel on a vent or a switch; a large
        # surface never receives a furniture-scale fillet.
        box = bmesh.new()
        try:
            bmesh.ops.create_cube(box, size=1.0)
            for vertex in box.verts:
                vertex.co = Vector(tuple(c + v * d for c, v, d in zip(center, vertex.co, size)))
            bmesh.ops.bevel(box, geom=list(box.edges), offset=min(.020, min(size) * .19),
                           segments=1, affect="EDGES", clamp_overlap=True)
            box.verts.index_update()
            forms.append(self, [v.co for v in box.verts],
                         [tuple(v.index for v in face.verts) for face in box.faces])
        finally:
            box.free()

    def add_cylinder_x(
        self,
        center: Sequence[float],
        radius: float,
        depth: float,
        segments: int = 12,
    ) -> None:
        cx, cy, cz = center
        base = len(self.vertices)
        for x_offset in (-depth * 0.5, depth * 0.5):
            for segment in range(segments):
                angle = (math.tau * segment) / segments
                self.vertices.append(
                    (
                        cx + x_offset,
                        cy + math.cos(angle) * radius,
                        cz + math.sin(angle) * radius,
                    )
                )
        self.faces.append(tuple(base + index for index in reversed(range(segments))))
        self.faces.append(tuple(base + segments + index for index in range(segments)))
        for segment in range(segments):
            next_segment = (segment + 1) % segments
            self.faces.append(
                (
                    base + segment,
                    base + next_segment,
                    base + segments + next_segment,
                    base + segments + segment,
                )
            )

    def add_cylinder_between(
        self,
        start: Sequence[float],
        end: Sequence[float],
        radius: float,
        segments: int = 8,
    ) -> None:
        start_vector = Vector(start)
        end_vector = Vector(end)
        axis = end_vector - start_vector
        if axis.length < 1e-6:
            raise ValueError("Cylinder endpoints overlap")
        direction = axis.normalized()
        reference = Vector((0.0, 0.0, 1.0))
        if abs(direction.dot(reference)) > 0.94:
            reference = Vector((0.0, 1.0, 0.0))
        tangent = direction.cross(reference).normalized()
        bitangent = direction.cross(tangent).normalized()
        base = len(self.vertices)
        for endpoint in (start_vector, end_vector):
            for segment in range(segments):
                angle = (math.tau * segment) / segments
                point = endpoint + radius * (
                    math.cos(angle) * tangent +
                    math.sin(angle) * bitangent
                )
                self.vertices.append(tuple(point))
        self.faces.append(tuple(base + index for index in reversed(range(segments))))
        self.faces.append(tuple(base + segments + index for index in range(segments)))
        for segment in range(segments):
            next_segment = (segment + 1) % segments
            self.faces.append(
                (
                    base + segment,
                    base + next_segment,
                    base + segments + next_segment,
                    base + segments + segment,
                )
            )

    def add_torus_y(
        self,
        center: Sequence[float],
        major_radius: float,
        minor_radius: float,
        major_segments: int = 12,
        minor_segments: int = 4,
    ) -> None:
        cx, cy, cz = center
        base = len(self.vertices)
        for major in range(major_segments):
            major_angle = math.tau * major / major_segments
            radial_x = math.cos(major_angle)
            radial_z = math.sin(major_angle)
            for minor in range(minor_segments):
                minor_angle = math.tau * minor / minor_segments
                radial_distance = major_radius + minor_radius * math.cos(minor_angle)
                self.vertices.append(
                    (
                        cx + radial_x * radial_distance,
                        cy + minor_radius * math.sin(minor_angle),
                        cz + radial_z * radial_distance,
                    )
                )
        for major in range(major_segments):
            next_major = (major + 1) % major_segments
            for minor in range(minor_segments):
                next_minor = (minor + 1) % minor_segments
                self.faces.append(
                    (
                        base + major * minor_segments + minor,
                        base + next_major * minor_segments + minor,
                        base + next_major * minor_segments + next_minor,
                        base + major * minor_segments + next_minor,
                    )
                )

    def add_torus_z(
        self,
        center: Sequence[float],
        major_radius: float,
        minor_radius: float,
        major_segments: int = 12,
        minor_segments: int = 4,
    ) -> None:
        cx, cy, cz = center
        base = len(self.vertices)
        for major in range(major_segments):
            major_angle = math.tau * major / major_segments
            radial_x = math.cos(major_angle)
            radial_y = math.sin(major_angle)
            for minor in range(minor_segments):
                minor_angle = math.tau * minor / minor_segments
                radial_distance = major_radius + minor_radius * math.cos(minor_angle)
                self.vertices.append(
                    (
                        cx + radial_x * radial_distance,
                        cy + radial_y * radial_distance,
                        cz + minor_radius * math.sin(minor_angle),
                    )
                )
        for major in range(major_segments):
            next_major = (major + 1) % major_segments
            for minor in range(minor_segments):
                next_minor = (minor + 1) % minor_segments
                self.faces.append(
                    (
                        base + major * minor_segments + minor,
                        base + next_major * minor_segments + minor,
                        base + next_major * minor_segments + next_minor,
                        base + major * minor_segments + next_minor,
                    )
                )

    def add_quad(
        self,
        a: Sequence[float],
        b: Sequence[float],
        c: Sequence[float],
        d: Sequence[float],
    ) -> None:
        """One flat quad from four explicit corners.

        Boxes are axis-aligned, and a car's screens rake. This is the cheapest
        way to state a raked pane: two triangles, no rotation, no hull.
        """
        base = len(self.vertices)
        self.vertices.extend(
            [tuple(a), tuple(b), tuple(c), tuple(d)]
        )
        self.faces.append((base, base + 1, base + 2, base + 3))

    def add_double_quad(
        self,
        a: Sequence[float],
        b: Sequence[float],
        c: Sequence[float],
        d: Sequence[float],
    ) -> None:
        """A pane visible from both sides - the hero sits behind this glass."""
        self.add_quad(a, b, c, d)
        self.add_quad(d, c, b, a)

def reset_scene() -> tuple[bpy.types.Collection, bpy.types.Collection]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.resolution_x = 960
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.look = "AgX - Medium High Contrast"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World_LastRouteCarPreview")
    scene.world.color = (0.018, 0.026, 0.024)

    source = bpy.data.collections.new(SOURCE_COLLECTION)
    scene.collection.children.link(source)
    presentation = bpy.data.collections.new(PRESENTATION_COLLECTION)
    scene.collection.children.link(presentation)
    return source, presentation


def create_material(name: str, spec: tuple) -> bpy.types.Material:
    rgba, metallic, roughness, emission_strength = spec
    material = bpy.data.materials.new(f"MAT_LastRouteCar{name}")
    material.diffuse_color = rgba
    material.use_nodes = True
    material.node_tree.nodes.clear()
    output = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
    shader = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Base Color"].default_value = rgba
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Alpha"].default_value = rgba[3]
    emission_color = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
    if emission_color is not None:
        emission_color.default_value = rgba
    emission_input = shader.inputs.get("Emission Strength")
    if emission_input is not None:
        emission_input.default_value = emission_strength
    material.node_tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    if rgba[3] < 1.0:
        material.use_transparency_overlap = False
        material.use_backface_culling = True
        material.surface_render_method = "DITHERED"
    family = {
        "Body": "Paint", "AccentPaint": "Paint", "Glass": "Glass", "Seat": "Interior",
        "Interior": "Interior", "Dashboard": "Interior", "Carpet": "Interior",
        "Rubber": "Rubber",
    }.get(name)
    if family:
        tree = material.node_tree
        path = Path(__file__).resolve().parents[1] / "Assets/Vehicles/Textures"
        def map_node(label, data=False):
            image = bpy.data.images.load(str(path / ("LastRouteCar"+label+".png")), check_existing=True)
            if data: image.colorspace_settings.name = "Non-Color"
            image.pack()
            node = tree.nodes.new("ShaderNodeTexImage")
            node.image = image
            node.extension = "EXTEND" if family in ("Paint", "Glass") else "REPEAT"
            return node

        texture = map_node(family+"Albedo")
        tint = tree.nodes.new("ShaderNodeMixRGB")
        tint.blend_type = "MULTIPLY"
        tint.inputs[0].default_value = 1.
        tint.inputs[1].default_value = (1.,1.,1.,1.) if family == "Interior" else rgba
        tree.links.new(texture.outputs["Color"], tint.inputs[2])
        tree.links.new(tint.outputs["Color"], shader.inputs["Base Color"])
        if name == "Glass":
            tree.links.new(texture.outputs["Alpha"], shader.inputs["Alpha"])
        else:
            surface = family
            response = map_node(surface+"Response", True)
            normal = tree.nodes.new("ShaderNodeNormalMap")
            normal.inputs["Strength"].default_value = .68 if surface=="Paint" else .82
            tree.links.new(map_node(surface+"Normal", True).outputs["Color"], normal.inputs["Color"])
            tree.links.new(normal.outputs["Normal"], shader.inputs["Normal"])
            rough = tree.nodes.new("ShaderNodeMath")
            rough.operation = "MULTIPLY_ADD"
            rough.inputs[1].default_value = -.94 if surface=="Paint" else -.39
            rough.inputs[2].default_value = 1.
            tree.links.new(response.outputs["Alpha"], rough.inputs[0])
            tree.links.new(rough.outputs[0], shader.inputs["Roughness"])
    material["bp_material_slot"] = name
    return material


def create_empty(
    name: str,
    collection: bpy.types.Collection,
    parent: bpy.types.Object | None,
    location: Sequence[float] = (0.0, 0.0, 0.0),
    rotation_euler: Sequence[float] = (0.0, 0.0, 0.0),
    display_size: float = 0.14,
) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = location
    obj.rotation_euler = rotation_euler
    obj.scale = (1.0, 1.0, 1.0)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = display_size
    return obj


def apply_uvs(mesh, name, role, material_slot, parent):
    uv_layer = mesh.uv_layers.new(name="UVMap")
    scale = 1.0 / UV_TILE_METERS.get(material_slot, UV_TILE_METERS_DEFAULT)
    painted = material_slot in ("Body", "AccentPaint")
    offset = parent.location if painted and parent and parent.name.startswith("PIVOT_Door") else Vector()
    for polygon in mesh.polygons:
        normal = polygon.normal
        ax, ay, az = (abs(value) for value in normal)
        patch = cabin_patch(name, role, normal.z > .69) if material_slot in ("Interior","Seat","Dashboard","Carpet") else None
        if patch is not None:
            col, row = PATCHES.index(patch) % PATCH_GRID, PATCHES.index(patch) // PATCH_GRID
        for index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[index].vertex_index].co
            if material_slot == "Glass":
                grid = mesh.loops[index].vertex_index % 28
                uv = (grid % 7 / 6., grid // 7 / 3.)
            elif painted:
                uv = forms.body_uv(*(vertex + offset), normal)
            elif patch is not None:
                px, py, pz = vertex
                if role == "door_card":
                    pu, pv = .12 + py / 2.15, .06 + (pz + .32) / .74
                elif role in ("dashboard", "dash_lip", "glovebox_compartment", "glovebox_lid"):
                    pu = .5 + px / 1.66
                    pv = (.5 + (py + 1.05) * 2.5 if az > ay else .5 + (pz - .92) * 2.15)
                    if role == "glovebox_lid":
                        pu, pv = .5 + px * 2.7, .08 + pz * 4.5
                elif az >= ay and az >= ax:
                    pu, pv = .5 + px * .59, .48 + py * .37
                else:
                    pu, pv = .5 + (px if ay >= ax else py) * .58, .06 + pz * .76
                uv = tuple((cell + .04 + .92 * max(0., min(1., value))) / PATCH_GRID
                           for cell, value in ((col, pu), (row, pv)))
            elif az >= ax and az >= ay:
                uv = (vertex.x * scale, vertex.y * scale)
            elif ax >= ay:
                uv = (vertex.y * scale, vertex.z * scale)
            else:
                uv = (vertex.x * scale, vertex.z * scale)
            uv_layer.data[index].uv = uv


def create_part(
    name: str,
    accumulator: MeshAccumulator,
    role: str,
    material_slot: str,
    collection: bpy.types.Collection,
    parent: bpy.types.Object,
    material: bpy.types.Material,
) -> Part:
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(accumulator.vertices, [], accumulator.faces)
    mesh.validate(clean_customdata=False)
    mesh.update(calc_edges=True)
    apply_uvs(mesh, name, role, material_slot, parent)
    smooth = material_slot not in ("Glass", "CrackedGlass", "Preview", "InstrumentInk")
    if smooth:
        for polygon in mesh.polygons:
            polygon.use_smooth = True
        mesh.set_sharp_from_angle(angle=math.radians(40.))
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    obj.data.materials.append(material)
    if smooth:
        weighted = obj.modifiers.new("Pressed surface normals", "WEIGHTED_NORMAL")
        weighted.keep_sharp = True
        weighted.weight = 25
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=weighted.name)

    obj["bp_role"] = role
    obj["bp_material_slot"] = material_slot
    return Part(obj, role, material_slot)

class LastRouteCarBuilder:
    def __init__(self) -> None:
        self.collection, self.presentation = reset_scene()
        self.materials = {
            slot: create_material(slot, spec)
            for slot, spec in MATERIALS.items()
        }
        self.parts: list[Part] = []
        self.pivots: list[Pivot] = []
        self.dash_boxes: tuple[
            tuple[tuple[float, float, float], tuple[float, float, float]], ...
        ] = ()
        self.dashboard_parts: list[Part] = []
        self.root = create_empty(ROOT_NAME, self.collection, None, display_size=0.30)
        self.body = create_empty(BODY_NAME, self.collection, self.root, display_size=0.26)
        self.body["bp_role"] = "body_root"

    def add_boxes(
        self,
        name: str,
        boxes: Iterable[tuple[Sequence[float], Sequence[float]]],
        role: str,
        material_slot: str,
        parent: bpy.types.Object | None = None,
    ) -> Part:
        accumulator = MeshAccumulator()
        for center, size in boxes:
            accumulator.add_box(center, size)
        part = create_part(
            name,
            accumulator,
            role,
            material_slot,
            self.collection,
            parent or self.body,
            self.materials[material_slot],
        )
        self.parts.append(part)
        return part

    def add_accumulator(
        self,
        name: str,
        accumulator: MeshAccumulator,
        role: str,
        material_slot: str,
        parent: bpy.types.Object | None = None,
    ) -> Part:
        part = create_part(
            name,
            accumulator,
            role,
            material_slot,
            self.collection,
            parent or self.body,
            self.materials[material_slot],
        )
        self.parts.append(part)
        return part

    def add_pivot(
        self,
        name: str,
        role: str,
        parent: bpy.types.Object,
        location: Sequence[float],
        rotation_euler: Sequence[float] = (0.0, 0.0, 0.0),
        runtime_axis_local: str = "",
        travel_m: float = 0.0,
    ) -> bpy.types.Object:
        pivot = create_empty(
            name,
            self.collection,
            parent,
            location,
            rotation_euler,
            display_size=0.10,
        )
        pivot["bp_role"] = role
        if runtime_axis_local:
            pivot["bp_runtime_axis_local"] = runtime_axis_local
        if travel_m > 0.0:
            pivot["bp_travel_m"] = travel_m
        self.pivots.append(Pivot(pivot, role, runtime_axis_local, travel_m))
        return pivot

    def build(self) -> BuildResult:
        self._build_shell()
        self._build_greenhouse()
        self._build_glass()
        self._build_doors()
        self._build_wheels()
        self._build_lights_and_trim()
        self._build_interior()
        self._build_dashboard()
        self._build_anchors()
        self._combine_fixed_parts()
        self.root["bp_generator"] = "tools/build-last-route-car-3d-model.py"
        self.root["bp_generator_version"] = GENERATOR_VERSION
        self.root["bp_design_id"] = DESIGN_ID
        self.root["bp_dimensions_m"] = f"{LENGTH:.2f}x{WIDTH:.2f}x{HEIGHT:.2f}"
        self.root["bp_forward_axis"] = "-Y"
        self.root["bp_driver_side_axis"] = "+X"
        return BuildResult(
            self.root,
            self.body,
            tuple(sorted(self.parts, key=lambda part: part.obj.name)),
            tuple(sorted(self.pivots, key=lambda pivot: pivot.obj.name)),
            tuple(self.collection.objects),
            self.dash_boxes,
            tuple(self.dashboard_parts),
        )

    def _combine_fixed_parts(self):
        # Group only fixed, same-finish meshes with the same view lifetime.
        # Independently bound lamps, instrument faces and moving parts stay separate.
        for names in (
            ("GEO_BodyShell","GEO_Decks","GEO_Greenhouse","GEO_Pillars"),
            ("GEO_Trim","GEO_Underbody","GEO_RadiatorMouth","GEO_TailHousings"),
            ("GEO_HeadlightRims","GEO_Grille"),
            ("GEO_SeatWeltingDriver","GEO_SeatWeltingPassenger","GEO_SeatWeltingRear"),
            ("GEO_InstrumentBezels","GEO_IgnitionKey"),
        ):
            by_name = {p.obj.name:p for p in self.parts}
            parts = [by_name[name] for name in names]
            lead = parts[0]
            combined = MeshAccumulator()
            role, slot = lead.role, lead.material_slot
            for part in parts:
                obj=part.obj
                if obj.parent is not self.body or part.material_slot!=slot:
                    raise RuntimeError(f"Cannot merge moving or different finish: {obj.name}")
                forms.append(combined,[v.co for v in obj.data.vertices],
                             [tuple(p.vertices) for p in obj.data.polygons],False)
                if part in self.dashboard_parts:
                    self.dashboard_parts.remove(part)
                self.parts.remove(part)
                data = obj.data
                bpy.data.objects.remove(obj,do_unlink=True)
                bpy.data.meshes.remove(data)
            assembled=self.add_accumulator(names[0],combined,role,slot)
            if role in ("dashboard","dash_vents","pedal_stems","instrument_bezels"):
                self.dashboard_parts.append(assembled)

    # ------------------------------------------------------------------
    # Geometry
    # ------------------------------------------------------------------

    def _build_shell(self) -> None:
        shell = MeshAccumulator()
        for side in (1., -1.):
            contour = lambda y, z: flank_x(side, y, z)
            # The quarter behind the longer door is a single pressed panel.
            # It has neither a rear door gap nor hardware.
            fixed_y = section_ys(DOORWAY_REAR_Y, REAR_Y)
            forms.side_skin(shell, side, fixed_y, WAIST_Z, arch_base, contour)
            wing_y = section_ys(FRONT_Y, DOORWAY_FRONT_Y) + [-2.385, -2.325, -2.245, -2.14, -2.025]
            forms.side_skin(shell, side, wing_y, WAIST_Z,
                            lambda y: arch_base(y)+.088*apron_blend(y),
                            contour, retract=lower_wing_retract)
            # Structural sill bridges the doors only between the wheel tubs.
            forms.side_skin(shell, side, (-.92, -.86, -.25, .30, .83, .92),
                            SILL_Z, lambda y: .308, lambda y, z: .815, thickness=.095)
        # The lower ends of the wings roll inward under the bumper and
        # meet the recessed apron. The apron stops short of the arch and
        # follows a rounded, shallow U rather than two dangling tabs.
        apron_sections=[]
        for x in (-.728,-.701,-.65,-.51,-.33,0.,.33,.51,.65,.701,.728):
            edge=(abs(x)/.728)**2
            y=-2.373+.102*edge
            lower=.365+.067*edge
            upper=.596-.036*edge
            apron_sections.append([(x,y+dy,z) for dy,z in (
                (.044,lower+.003),(.012,lower),(-.006,lower+.018),(-.014,lower+.042),
                (-.009,upper-.035),(.014,upper),(.072,upper-.004),(.063,lower+.020))])
        forms.loft(shell,apron_sections)
        forms.loft(shell, [forms.chamfered_ring(0., .745, w, h, .065, y)
                           for y, w, h in ((FRONT_Y + .015, .767, .182), (FRONT_Y + .09, .806, .224),
                                           (FRONT_Y + .18, .818, .215))])
        forms.loft(shell, [forms.chamfered_ring(0., .755, w, h, .070, y)
                           for y, w, h in ((REAR_Y - .18, .808, .235), (REAR_Y - .07, .798, .234),
                                           (REAR_Y - .01, .769, .194))])
        self.add_accumulator("GEO_BodyShell", shell, "body_shell", "Body")

        bonnet = MeshAccumulator()
        forms.deck(bonnet, [(y, w, .057, hood_z(0, y) - .057)
                    for y, w in ((-2.408,.748),(-2.36,.776),(-2.02,.79),(-1.68,.798),(-1.25,.803))], .025)
        forms.deck(bonnet, [(y, w, .031, z)
                    for y, w, z in ((1.145,.787,1.032),(1.42,.80,1.039),(2.12,.776,1.027),
                                   (2.359,.750,1.018),(2.400,.728,.995))], .027)
        self.add_accumulator("GEO_Decks", bonnet, "deck_skins", "Body")

        under = MeshAccumulator()
        forms.loft(under, [forms.chamfered_ring(0., z, w, h, .023, y)
                           for y, z, w, h in ((-1.66,.310,.43,.026),(-1.36,.264,.57,.040),
                                               (-.94,.242,.64,.041),(1.52,.242,.64,.041),
                                               (1.76,.279,.51,.033))])
        for side in (-1., 1.):
            for axle in (FRONT_AXLE_Y, REAR_AXLE_Y):
                # Real black wheel tubs. The rolled returns under the metal
                # arch give the tyre clearance from its own silhouette.
                path=[]
                for i in range(19):
                    a=math.pi*i/18
                    path.append((side*.817, axle + .421*math.cos(a), .335+.421*math.sin(a)))
                forms.trim_path(under, path, .023, 6)
                under.add_box((side*.59, axle, .48), (.08,.71,.37))
        self.add_accumulator("GEO_Underbody", under, "underbody", "Trim")

    def _build_greenhouse(self) -> None:
        roof = MeshAccumulator()
        sections = ((-.875,.717,.028,1.501),(-.815,.737,.045,1.513),
                    (-.60,.750,.055,1.515),(0.0,.751,.055,1.515),
                    (.69,.722,.052,1.511),(.96,.676,.040,1.500),
                    (1.10,.632,.023,1.469))
        forms.deck(roof, sections, .028)
        self.add_accumulator("GEO_Greenhouse", roof, "greenhouse", "Body")

        liner = MeshAccumulator()
        forms.deck(liner, [(y, w - .027, c, h - .029) for y,w,c,h in sections], .009)
        self.add_accumulator("GEO_Headlining", liner, "headlining", "Interior")

        pillars = MeshAccumulator()
        for side in (1., -1.):
            for path, radius in (
                (((side*.822,-1.235,1.018),(side*.776,-1.10,1.221),(side*.692,-.857,1.474)),.028),
                (((side*.829,B_PILLAR_Y,1.017),(side*.793,B_PILLAR_Y-.022,1.232),(side*.705,B_PILLAR_Y-.032,1.479)),.025),
                (((side*.827,1.187,1.022),(side*.754,1.15,1.243),(side*.610,1.081,1.446)),.052),
            ):
                forms.trim_path(pillars, path, radius, 8)
        self.add_accumulator("GEO_Pillars", pillars, "pillars", "Body")

    def _build_glass(self) -> None:
        glass = (
            ("Windshield", ((.793,-1.230,1.044),(-.793,-1.230,1.044),
                            (-.703,-.858,1.484),(.703,-.858,1.484)), (0.,-1.,0.), .018),
            ("Backlight", ((-.788,1.294,1.063),(.788,1.294,1.063),
                           (.623,1.103,1.469),(-.623,1.103,1.469)), (0.,1.,0.), .022),
            ("RearDriver", ((.817,B_PILLAR_Y+.024,1.047),(.800,1.148,1.047),
                             (.627,1.065,1.463),(.712,B_PILLAR_Y+.010,1.480)), (1.,0.,0.), .008),
            ("RearPassenger", ((-.817,B_PILLAR_Y+.024,1.047),(-.800,1.148,1.047),
                                (-.627,1.065,1.463),(-.712,B_PILLAR_Y+.010,1.480)), (-1.,0.,0.), .008),
        )
        seals = MeshAccumulator()
        for name, corners, normal, crown in glass:
            pane = MeshAccumulator()
            forms.pane(pane, corners, crown, normal)
            self.add_accumulator("GEO_" + name, pane, "glass", "Glass")
            for a, b in zip(corners, corners[1:] + corners[:1]):
                middle = (Vector(a) + Vector(b)) * .5
                if name in ("Windshield","Backlight") and abs(a[0]-b[0]) > .3:
                    middle += Vector(normal) * crown
                forms.trim_path(seals, (a,middle,b), .011, 6)
        self.add_accumulator("GEO_WindowSeals", seals, "window_seals", "Rubber")

        # One hairline rooted in the driver's rear side pane. The vertices
        # are evaluated on the same ruled surface as the pane, not on a plate.
        corners = [Vector(p) for p in glass[2][1]]
        crack=MeshAccumulator()
        def at(u,v):
            p=corners[0].lerp(corners[1],u).lerp(corners[3].lerp(corners[2],u),v)
            return p+Vector((.009*u*(1-u)*4+.0014,0,0))
        for points in (((.83,.04),(.71,.20),(.75,.35),(.55,.69),(.50,.88)),
                       ((.71,.20),(.50,.27),(.43,.39)),((.75,.35),(.85,.57),(.86,.72)),
                       ((.55,.69),(.66,.71),(.73,.84))):
            forms.trim_path(crack, [at(u,v) for u,v in points], .0011, 5)
        self.add_accumulator("GEO_CrackedGlass", crack, "cracked_glass", "CrackedGlass")

    def _build_doors(self) -> None:
        for name, side, role, slot in DOOR_SPECS:
            origin = (side*.866, DOOR_HINGE_Y, DOOR_HINGE_Z)
            hinge = self.add_pivot(f"PIVOT_Door{name}", role, self.body, origin, runtime_axis_local="+Z")
            local = lambda x,y,z: (side*x-origin[0], y-origin[1], z-origin[2])
            outer = MeshAccumulator()
            first = DOORWAY_FRONT_Y + .006
            last = DOORWAY_REAR_Y - .006
            forms.side_skin(outer, side, section_ys(first, last), WAIST_Z-.006,
                            lambda y: max(SILL_Z+.006, arch_base(y)+.010),
                            lambda y,z: flank_x(side,y,z), origin, .029)
            self.add_accumulator(f"GEO_Door{name}Panel", outer, "door_panel", slot, hinge)

            corners = (local(.850,-1.148,1.037),local(.846,B_PILLAR_Y-.028,1.037),
                       local(.719,B_PILLAR_Y-.054,1.480),local(.704,-.869,1.478))
            pane=MeshAccumulator()
            forms.pane(pane, corners, .008, (side,0,0))
            self.add_accumulator(f"GEO_Door{name}Glass", pane, "door_glass", "Glass", hinge)
            rim = MeshAccumulator()
            for a,b in zip(corners,corners[1:]+corners[:1]):
                mid=(Vector(a)+Vector(b))*.5 + Vector((.008*side,0.,0.))
                forms.trim_path(rim, (a,mid,b), .014, 8)
            self.add_accumulator(f"GEO_Door{name}Frame", rim, "door_frame", slot, hinge)
            rubber=MeshAccumulator()
            pane_centre=sum((Vector(point) for point in corners),Vector())/4
            inset=[Vector(a).lerp(pane_centre,.035)-Vector((.005*side,0.,0.)) for a in corners]
            for a,b in zip(inset,inset[1:]+inset[:1]):
                forms.trim_path(rubber,(a,(a+b)*.5+Vector((.006*side,0.,0.)),b),.006,6)
            self.add_accumulator(f"GEO_Door{name}Seal",rubber,"door_seal","Rubber",hinge)

            card=MeshAccumulator()
            forms.side_skin(card, side, section_ys(first+.025,last-.022), .991,
                            lambda y: max(.485,arch_base(y)+.034),
                            lambda y,z: flank_x(side,y,z)-.034, origin,.042)
            arm=local(.793,-.291,.836)
            card.add_box(local(.817,.014,.65),(.049,.215,.090))
            self.add_accumulator(f"GEO_Door{name}Card", card, "door_card", "Interior", hinge)
            self.add_boxes(f"GEO_Door{name}Armrest", ((arm,(.086,.36,.080)),),
                           "door_armrest", "Interior", hinge)
            handles = MeshAccumulator()
            handle_y = B_PILLAR_Y - .146
            x=flank_x(side,handle_y,.922)
            forms.trim_path(handles, (local(x-.005,handle_y-.081,.921),local(x+.023,handle_y-.052,.924),
                                     local(x+.024,handle_y+.056,.923),local(x-.003,handle_y+.084,.918)),.011,8)
            forms.trim_path(handles, (local(.782,-.37,.901),local(.756,-.33,.919),
                                      local(.775,-.269,.916)),.010,8)
            self.add_accumulator(f"GEO_Door{name}Handle",handles,"door_handle","Chrome",hinge)
            self.add_pivot(f"ANCHOR_Door{name}Pull", f"{role}_outer_grip", hinge,
                           local(x+.025,handle_y,.924))
            self.add_pivot(f"ANCHOR_Door{name}InnerPull", f"{role}_inner_grip", hinge,
                           local(.756,-.322,.919))

    def _build_wheels(self) -> None:
        for code, role, x, y, cap in (
            ("FL","front_left_wheel",TRACK_HALF,FRONT_AXLE_Y,True),
            ("FR","front_right_wheel",-TRACK_HALF,FRONT_AXLE_Y,False),
            ("RL","rear_left_wheel",TRACK_HALF,REAR_AXLE_Y,True),
            ("RR","rear_right_wheel",-TRACK_HALF,REAR_AXLE_Y,True),
        ):
            pivot=self.add_pivot(f"PIVOT_Wheel{code}",role,self.body,(x,y,WHEEL_RADIUS),runtime_axis_local="+X")
            sign=math.copysign(1.,x)
            disk=MeshAccumulator()
            profile=((-0.083,.205),(-.098,.235),(-.104,.285),(-.086,.325),
                     (-.059,WHEEL_RADIUS),(.059,WHEEL_RADIUS),(.086,.325),(.104,.285),
                     (.098,.235),(.083,.205))
            forms.revolve(disk,(0,0,0),(1,0,0),profile,28,loop=True)
            self.add_accumulator(f"GEO_Wheel{code}",disk,"wheel","Rubber",pivot)
            steel=MeshAccumulator()
            forms.revolve(steel,(0,0,0),(sign,0,0),((-.090,.209),(.077,.209),(.101,.205),
                           (.104,.193),(.091,.177),(.069,.159),(.048,.118)),24)
            for n in range(5):
                a=math.tau*n/5
                point=(sign*.073, .076*math.cos(a), .076*math.sin(a))
                forms.revolve(steel,point,(sign,0,0),((-.015,.010),(.011,.010),(.016,.007)),6)
            self.add_accumulator(f"GEO_Rim{code}",steel,"wheel_rim","Metal",pivot)
            if cap:
                hub=MeshAccumulator()
                forms.revolve(hub,(0,0,0),(sign,0,0),((.094,.123),(.108,.123),(.119,.102),
                               (.130,.057),(.131,.023)),24)
                self.add_accumulator(f"GEO_Hubcap{code}",hub,"hubcap","Chrome",pivot)

    def _build_lights_and_trim(self) -> None:
        front, rear = FRONT_Y, REAR_Y
        bezels, grille, rear_trim, indicators = (MeshAccumulator() for _ in range(4))
        for x, side in ((.602, "Driver"),(-.602, "Passenger")):
            lamp = MeshAccumulator()
            forms.round_headlight(lamp,bezels,(x,front,.854))
            self.add_accumulator("GEO_Headlight"+side, lamp, "headlight", "Headlight")
            self.add_pivot("ANCHOR_Headlight"+side,"headlight_beam",self.body,(x,front-.074,.854))
            bezel = MeshAccumulator()
            forms.loft(bezel,[forms.chamfered_ring(x,.848,w,h,r,y)
                              for y,w,h,r in ((rear-.026,.151,.079,.022),(rear+.016,.144,.071,.018))])
            forms.loft(rear_trim,[forms.chamfered_ring(x,.848,w,h,r,y)
                                  for y,w,h,r in ((rear-.045,.166,.090,.020),(rear+.006,.166,.088,.020))])
            self.add_accumulator("GEO_TailLight"+side,bezel,"tail_light","TailLight")
            for yi, direction, z in ((front-.015,-1.,.684),(rear+.022,1.,.846)):
                xi = x if direction < 0 else x + math.copysign(.106,x)
                forms.loft(indicators,[forms.chamfered_ring(xi,z,w,.028,.012,yi+d*direction)
                                 for d,w in ((-.029,.040),(.003,.046),(.009,.040))])
        self.add_accumulator("GEO_HeadlightRims",bezels,"headlight_rim","Chrome")
        self.add_accumulator("GEO_TailHousings",rear_trim,"tail_housing","Trim")
        self.add_accumulator("GEO_Indicators",indicators,"indicator_lens","MarkerLens")

        # Deep dark aperture and thin horizontal blades with bright edges.
        mask = MeshAccumulator()
        forms.loft(mask,[forms.chamfered_ring(0.,.833,w,h,.025,y)
                     for y,w,h in ((front+.012,.457,.122),(front+.050,.486,.137))])
        self.add_accumulator("GEO_RadiatorMouth",mask,"radiator_mouth","Trim")
        for z in (.747,.790,.833,.876,.919):
            forms.trim_path(grille, ((-.446,front+.007,z),(-.29,front-.004,z),
                                    (0.,front-.009,z),(.29,front-.004,z),(.446,front+.007,z)),.007,6)
        self.add_accumulator("GEO_Grille",grille,"grille","Chrome")

        bumper = MeshAccumulator()
        def bumper_section(x,y,z,depth,height):
            return [(x,y+dy,z+dz) for dy,dz in (
                (-depth*.33,-height*.5),(depth*.34,-height*.5),(depth*.5,-height*.30),
                (depth*.5,height*.30),(depth*.34,height*.5),(-depth*.34,height*.5),
                (-depth*.5,height*.30),(-depth*.5,-height*.30))]
        for sign in (-1.,1.):
            # One bent end. The useful top beneath the Ferryman's boots
            # remains level at the authored contact.
            cross=[]
            for x in (-.832,-.777,-.62,-.36,-.13,.25,.64,.78,.832):
                d=.14-(.045 if abs(x)>.78 else 0)
                y=sign*(2.452-.100*(abs(x)/.832)**6)
                z=.523 - (.061 * max(0.,(x-.52)/.312) if sign < 0 else .007)
                cross.append(bumper_section(x,y,z,d,.104))
            forms.loft(bumper,cross)
        self.add_accumulator("GEO_Bumpers",bumper,"bumper","Chrome")

        trim = MeshAccumulator()
        plates = MeshAccumulator()
        for sign in (-1.,1.):
            y = front-.027 if sign<0 else rear+.041
            forms.loft(plates,[forms.chamfered_ring(.02,.66,w,h,.016,y+sign*d)
                               for d,w,h in ((-.014,.216,.048),(.002,.216,.048),(.009,.208,.042))])
            for x in (-.153,.190):
                forms.revolve(trim,(x,y+sign*.008,.687),(0,sign,0),((0.,.005),(.005,.005),(.007,.003)),6)
        self.add_accumulator("GEO_NumberPlate",plates,"number_plate","Plate")

        for side in (-1.,1.):
            for axle in (FRONT_AXLE_Y, REAR_AXLE_Y):
                # Exposed arch lip remains on the fixed panel. The forward
                # door's corner cuts into the opening and has its own return.
                path=[]
                for i in range(23):
                    a=math.pi*i/22
                    y,z= axle+.425*math.cos(a),.335+.425*math.sin(a)
                    if axle==FRONT_AXLE_Y and DOORWAY_FRONT_Y <= y <= DOORWAY_REAR_Y:
                        if len(path) > 1:
                            forms.trim_path(trim,path,.007,5)
                        path=[]
                        continue
                    path.append((side*(flank_x(side,y,z)+.002),y,z))
                if len(path)>1:
                    forms.trim_path(trim,path,.007,5)
        for wiper_x in (.36,-.36):
            forms.trim_path(trim, ((wiper_x+.220,-1.248,1.051),(wiper_x+.147,-1.227,1.085),
                                  (wiper_x-.13,-1.209,1.099)),.009,6)
        forms.trim_path(trim,((-.823,-.92,1.01),(-.842,-.91,1.13),(-.853,-.90,1.27)),.006,6)
        # Only the driver's exterior mirror survived. The inside mirror is
        # separate, so it never gets cloned into an expensive live camera.
        forms.trim_path(trim,((.848,-1.156,1.024),(.933,-1.142,1.118),
                              (.981,-1.162,1.135)),.014,8)
        forms.loft(trim,[forms.chamfered_ring(.976,1.139,w,h,.013,y)
                         for y,w,h in ((-1.225,.061,.038),(-1.168,.064,.045),(-1.145,.058,.038))])
        self.add_accumulator("GEO_Trim",trim,"exterior_trim","Trim")

        exhaust=MeshAccumulator()
        forms.trim_path(exhaust,((-.31,1.53,.253),(-.32,1.95,.252),(-.43,2.24,.251),(-.43,2.36,.229)),.028,12)
        forms.revolve(exhaust,(-.43,2.36,.229),(0,1,0),((-.035,.032),(.015,.032),(.019,.029),
                                                          (.019,.023),(-.035,.023)),12,loop=True)
        self.add_accumulator("GEO_Exhaust",exhaust,"exhaust","Metal")
        self.add_pivot("ANCHOR_Exhaust","exhaust_outlet",self.body,(-.43,2.39,.226))

    def _build_interior(self) -> None:
        """A real cabin: the glass is see-through and the hero will sit in it."""
        # The floor's TOP is FLOOR_Z, not its middle. That plane is where a
        # seated root stands - the drive pose hangs 0.2197 m of leg under a
        # pelvis at 0.52, so the lowest drawn point of a sitter lands on
        # 0.3003 - and the slab used to be centred a centimetre ABOVE it, so
        # every sitter's boots went 2.5 cm into the carpet. Small next to the
        # 14 cm the old rocker slab buried them by, and just as wrong.
        self.add_boxes(
            "GEO_CabinFloor",
            (((0.0, -0.10, FLOOR_Z - 0.015), (1.52, 2.30, 0.03)),),
            "cabin_floor",
            "Interior",
        )

        # The room a real doorway now looks into.
        #
        # Cutting the hole in the flank exposed three things that were only
        # ever hidden because the wall was solid: the front wheel arch,
        # straight through the bottom corner of the opening and out at the
        # road; the engine bay past the scuttle; and the OUTSIDE paint on
        # the inner face of the far panel. The bus solves the same problem
        # the same way, with an inner liner distinct from the outer shell.
        tub_x = TRACK_HALF - WHEEL_WIDTH * 0.5 - 0.03
        tub_thickness = 0.03
        tub_length = ARCH_REAR_Y - CABIN_FRONT_Y
        tub_y = (CABIN_FRONT_Y + ARCH_REAR_Y) * 0.5
        tub_top_z = WHEEL_TOP_Z + 0.04
        tub_mid_z = (FLOOR_Z + tub_top_z) * 0.5
        tub_height = tub_top_z - FLOOR_Z
        bulkhead_width = (tub_x + tub_thickness * 0.5) * 2.0
        liner_x = BODY_HALF_WIDTH - 0.13 - 0.015
        liner_length = CABIN_REAR_Y - DOORWAY_REAR_Y
        liner_y = (DOORWAY_REAR_Y + CABIN_REAR_Y) * 0.5
        self.add_boxes(
            "GEO_InteriorShell",
            (
                # Wheel arch tubs. The footwell narrows round each front
                # wheel, which is both what a saloon does and what stops
                # the doorway's front-lower corner opening onto the road.
                ((tub_x, tub_y, tub_mid_z),
                 (tub_thickness, tub_length, tub_height)),
                ((-tub_x, tub_y, tub_mid_z),
                 (tub_thickness, tub_length, tub_height)),
                # Scuttle bulkhead, between the tubs. Above the waist the
                # engine bay's own solid flank already closes it.
                ((0.0, CABIN_FRONT_Y + 0.02, tub_mid_z),
                 (bulkhead_width, 0.04, tub_height)),
                # And the parcel shelf behind the rear bench.
                ((0.0, CABIN_REAR_Y - 0.04, tub_mid_z),
                 (bulkhead_width, 0.04, tub_height)),
                # Door cards on what is left of each cabin panel, so the
                # far side of the room reads as trim rather than as the
                # back of the paintwork.
                ((liner_x, liner_y, (SILL_Z + WAIST_Z) * 0.5),
                 (0.03, liner_length, WAIST_Z - SILL_Z)),
                ((-liner_x, liner_y, (SILL_Z + WAIST_Z) * 0.5),
                 (0.03, liner_length, WAIST_Z - SILL_Z)),
            ),
            "interior_shell",
            "Interior",
        )
        self._build_seats()

        # Both footwells have removable mats above the textured floor.
        self.add_boxes("GEO_CarpetMats", tuple(
            ((side*.433,-.832,FLOOR_Z+.006),(.376,.64,.012)) for side in (-1.,1.)),
            "floor_mats","Carpet")
        # The dashboard itself is built around the glovebox aperture.
        column = MeshAccumulator()
        column.add_cylinder_between((.44,-1.02,.80),STEERING_WHEEL_CENTER,.026,10)
        self.add_accumulator("GEO_SteeringColumn",column,"steering_column","Metal")
        self.add_boxes("GEO_InteriorMirror",(((0.,-.934,1.414),(.215,.029,.061)),),"interior_mirror","Trim")
        self.add_boxes("GEO_InteriorMirrorSilver",(((0.,-.916,1.414),(.194,.010,.044)),),"mirror_surface","Chrome")

    def _build_seats(self):
        for side, name, role in ((1.,"Driver","driver_seat"),(-1.,"Passenger","passenger_seat"),
                                 (0.,"Rear","rear_bench")):
            cx=side*SEAT_HALF_SPAN_X
            fore=SEAT_Y if side else .63
            width=.232 if side else .613
            cloth, welting=MeshAccumulator(),MeshAccumulator()
            # Compressed centre with slightly higher rounded edge bolsters.
            forms.loft(cloth,[forms.chamfered_ring(cx,z,width*w,h,.028,fore+y)
                               for y,w,z,h in ((-.268,.83,.429,.028),(-.218,1.,.438,.057),
                                               (-.05,1.,.432,.054),(.149,1.,.431,.052),(.228,.91,.435,.039))])
            back=[]
            for w,y,z,d in ((1.,.18,.449,.062),(1.,.217,.523,.060),(.98,.248,.695,.066),
                             (.94,.283,.867,.059),(.82,.302,1.055,.051),(.67,.314,1.094,.035)):
                back.append([(cx+x,fore+yy,z) for x, yy in (
                    (-width*w+.033,y-d),(width*w-.033,y-d),(width*w,y-d+.021),(width*w,y+d-.034),
                    (width*w-.032,y+d),(-width*w+.032,y+d),(-width*w,y+d-.034),(-width*w,y-d+.021))])
            if not side:
                back=[[(x,y+.09,z-.065) for x,y,z in row] for row in back]
            forms.loft(cloth,back)
            if side:
                forms.loft(cloth,[forms.chamfered_ring(cx,1.167,w,h,.025,fore+y)
                                 for y,w,h in ((.227,.089,.047),(.257,.125,.067),(.326,.121,.061),(.347,.098,.043))])
                for x in (cx-.084,cx+.084):
                    forms.trim_path(welting,((x,fore+.283,1.058),(x,fore+.290,1.128)),.007,8)
            for sign in (-1.,1.):
                x=cx+width*.83*sign
                forms.trim_path(welting,((x,fore-.214,.492),(x,fore-.055,.485),(x,fore+.143,.483)),.003,5)
                if side:
                    forms.trim_path(welting,((x,fore+.158,.565),(x,fore+.188,.697),
                                              (cx+width*.68*sign,fore+.255,1.042)),.003,5)
            self.add_accumulator("GEO_Seat"+name,cloth,role,"Seat")
            self.add_accumulator("GEO_SeatWelting"+name,welting,"seat_welting","Interior")

    def _dash_boxes(
        self,
        name: str,
        boxes: Iterable[tuple[Sequence[float], Sequence[float]]],
        role: str,
        material_slot: str,
        parent: bpy.types.Object | None = None,
    ) -> Part:
        part = self.add_boxes(name, boxes, role, material_slot, parent)
        self.dashboard_parts.append(part)
        return part

    def _dash_accumulator(
        self,
        name: str,
        accumulator: MeshAccumulator,
        role: str,
        material_slot: str,
        parent: bpy.types.Object | None = None,
    ) -> Part:
        part = self.add_accumulator(name, accumulator, role, material_slot, parent)
        self.dashboard_parts.append(part)
        return part

    def _build_dashboard(self) -> None:
        """The dash, and the two things on it the passenger can touch.

        The slab is composed AROUND the glovebox aperture rather than drawn
        solid and cut afterwards, exactly as the flank is composed around
        the doorways: a hole in a merged mesh cannot be asked about, so the
        validator checks the boxes as authored. The lid hangs on its own
        pivot at the aperture's bottom edge, the radio's two knobs and its
        needle on theirs, the speedometer needle on its own - "he turns the
        knob" is a rotation at runtime, never a re-author, the doors' rule.

        Nothing here stands inside either sitter's legs or torso, and
        nothing crosses the windscreen between the dash top and the header;
        both are proved by `validate_result` over `dashboard_parts`, not
        assumed. The compartment behind the lid is EMPTY on purpose.
        """
        aperture_x = (GLOVEBOX_X[0] + GLOVEBOX_X[1]) * 0.5
        aperture_z = (GLOVEBOX_Z[0] + GLOVEBOX_Z[1]) * 0.5
        aperture_width = GLOVEBOX_X[1] - GLOVEBOX_X[0]
        aperture_height = GLOVEBOX_Z[1] - GLOVEBOX_Z[0]
        slab_mid_z = (DASH_BOTTOM_Z + DASH_TOP_Z) * 0.5
        slab_height = DASH_TOP_Z - DASH_BOTTOM_Z
        dash_boxes = (
            # The slab from the aperture's driver-side edge to the far end.
            (((GLOVEBOX_X[1] + DASH_HALF_WIDTH) * 0.5, DASH_SLAB_Y, slab_mid_z),
             (DASH_HALF_WIDTH - GLOVEBOX_X[1], DASH_SLAB_DEPTH, slab_height)),
            # The stub outboard of the aperture.
            (((GLOVEBOX_X[0] - DASH_HALF_WIDTH) * 0.5, DASH_SLAB_Y, slab_mid_z),
             (GLOVEBOX_X[0] + DASH_HALF_WIDTH, DASH_SLAB_DEPTH, slab_height)),
            # Above and below the aperture.
            ((aperture_x, DASH_SLAB_Y, (GLOVEBOX_Z[1] + DASH_TOP_Z) * 0.5),
             (aperture_width, DASH_SLAB_DEPTH, DASH_TOP_Z - GLOVEBOX_Z[1])),
            ((aperture_x, DASH_SLAB_Y, (DASH_BOTTOM_Z + GLOVEBOX_Z[0]) * 0.5),
             (aperture_width, DASH_SLAB_DEPTH, GLOVEBOX_Z[0] - DASH_BOTTOM_Z)),
            # The binnacle cowl over the driver.
            ((0.44, -1.06, 1.02), (0.34, 0.10, 0.12)),
        )
        self.dash_boxes = dash_boxes
        self._dash_boxes("GEO_Dashboard", dash_boxes, "dashboard", "Dashboard")

        # The box the lid opens onto: back wall, two sides and a floor,
        # lining the aperture so an open lid shows trim rather than the
        # engine bay. Empty inside.
        wall = 0.02
        self._dash_boxes(
            "GEO_GloveboxCompartment",
            (
                ((aperture_x, DASH_SLAB_Y - DASH_SLAB_DEPTH * 0.5 + 0.015, aperture_z),
                 (aperture_width, 0.03, aperture_height)),
                ((GLOVEBOX_X[0] + wall * 0.5, DASH_SLAB_Y, aperture_z),
                 (wall, DASH_SLAB_DEPTH, aperture_height)),
                ((GLOVEBOX_X[1] - wall * 0.5, DASH_SLAB_Y, aperture_z),
                 (wall, DASH_SLAB_DEPTH, aperture_height)),
                ((aperture_x, DASH_SLAB_Y, GLOVEBOX_Z[0] + wall * 0.5),
                 (aperture_width, DASH_SLAB_DEPTH, wall)),
            ),
            "glovebox_compartment",
            "Interior",
        )

        # The plafond: a dark bezel bitten into the headlining with a
        # frosted lens hanging proud of it. Two parts rather than one so
        # the housing can stay unlit trim while the lens is the thing that
        # glows - the same division the headlight rim and lens already use.
        self._dash_boxes(
            "GEO_CabinLampHousing",
            (CABIN_LAMP_HOUSING,),
            "cabin_lamp_housing",
            "Interior",
        )
        self._dash_boxes(
            "GEO_CabinLampLens",
            (CABIN_LAMP_LENS,),
            "cabin_lamp_lens",
            "CabinLamp",
        )
        # The glovebox bulb. NOT parented to PIVOT_GloveboxLid: that pivot
        # is asserted to own exactly the lid and its catch, and a bulb
        # riding the leaf would swing out of the box it is meant to light.
        self._dash_boxes(
            "GEO_GloveboxBulb",
            (GLOVEBOX_BULB,),
            "glovebox_bulb",
            "CabinLamp",
        )

        # The lid, authored in its hinge's space: the hinge is the origin,
        # the leaf stands up from it flush with the face and laps the
        # aperture by a centimetre each side, and the catch rides it.
        lid = self.add_pivot(
            "PIVOT_GloveboxLid",
            "glovebox_lid",
            self.body,
            GLOVEBOX_HINGE,
            runtime_axis_local="+X",
        )
        self._dash_boxes(
            "GEO_GloveboxLid",
            (((0.0, 0.01, GLOVEBOX_LID_HEIGHT * 0.5),
              (aperture_width + GLOVEBOX_LID_LAP * 2.0, 0.02, GLOVEBOX_LID_HEIGHT)),),
            "glovebox_lid",
            "Dashboard",
            parent=lid,
        )
        self._dash_boxes(
            "GEO_GloveboxCatch",
            (((0.0, 0.026, GLOVEBOX_LID_HEIGHT - 0.017), (0.04, 0.012, 0.016)),),
            "glovebox_catch",
            "Chrome",
            parent=lid,
        )

        # The radio: a chrome frame proud of the face, the dial inside it
        # and a knob either side. The dial is its own mesh on its own
        # material slot because it is the one thing in the cabin that
        # lights up - when the radio is switched on, and only then.
        radio_x, radio_y, radio_z = RADIO_CENTER
        frame_y = radio_y + 0.01
        self._dash_boxes(
            "GEO_RadioBezel",
            (
                ((radio_x, frame_y, radio_z + 0.04), (0.24, 0.02, 0.01)),
                ((radio_x, frame_y, radio_z - 0.04), (0.24, 0.02, 0.01)),
                ((radio_x + 0.115, frame_y, radio_z), (0.01, 0.02, 0.09)),
                ((radio_x - 0.115, frame_y, radio_z), (0.01, 0.02, 0.09)),
            ),
            "radio_bezel",
            "Chrome",
        )
        self._dash_boxes(
            "GEO_RadioDial",
            (((radio_x, radio_y + 0.005, RADIO_DIAL_Z),
              (RADIO_DIAL_WIDTH, 0.012, 0.03)),),
            "radio_dial",
            "RadioDial",
        )
        for name, role, sign in (
            ("Power", "radio_power_knob", 1.0),
            ("Tuning", "radio_tuning_knob", -1.0),
        ):
            knob_pivot = self.add_pivot(
                f"PIVOT_Radio{name}Knob",
                role,
                self.body,
                (sign * RADIO_KNOB_X, radio_y, RADIO_KNOB_Z),
                runtime_axis_local="+Y",
            )
            knob = MeshAccumulator()
            # The knob stands out of the face towards the sitter, which is
            # source +Y, and carries a pointer nub at twelve o'clock so a
            # turn reads as a turn.
            knob.add_cylinder_between((0.0, 0.0, 0.0), (0.0, 0.028, 0.0), 0.014, segments=8)
            knob.add_box((0.0, 0.024, 0.009), (0.004, 0.010, 0.010))
            self._dash_accumulator(
                f"GEO_Radio{name}Knob", knob, "radio_knob", "Interior", parent=knob_pivot
            )
        needle_pivot = self.add_pivot(
            "PIVOT_RadioNeedle",
            "radio_needle",
            self.body,
            (radio_x - RADIO_NEEDLE_TRAVEL_M * 0.5, radio_y, RADIO_DIAL_Z),
            runtime_axis_local="+X",
            travel_m=RADIO_NEEDLE_TRAVEL_M,
        )
        self._dash_boxes(
            "GEO_RadioNeedle",
            (((0.0, 0.015, 0.0), (0.003, 0.006, 0.026)),),
            "radio_needle",
            "Chrome",
            parent=needle_pivot,
        )

        # The binnacle: two dials recessed in the cowl's rear face, chrome
        # rings round them, and the fuel needle drawn at "empty".
        # The dials share a readable, quiet backlight. Thin indices are in
        # front of the faces and below the glass/horizon line.
        cowl_face_y = -1.01
        bezels = MeshAccumulator()
        faces = MeshAccumulator()
        for gauge_x in (SPEEDO_CENTER[0], FUEL_GAUGE_X):
            bezels.add_cylinder_between(
                (gauge_x, cowl_face_y - 0.002, SPEEDO_CENTER[2]),
                (gauge_x, cowl_face_y + 0.015, SPEEDO_CENTER[2]),
                0.038,
                segments=10,
            )
            faces.add_cylinder_between(
                (gauge_x, cowl_face_y, SPEEDO_CENTER[2]),
                (gauge_x, cowl_face_y + 0.012, SPEEDO_CENTER[2]),
                0.032,
                segments=10,
            )
        bezels.add_cylinder_between(
            (FUEL_GAUGE_X, cowl_face_y + 0.017, SPEEDO_CENTER[2]),
            (FUEL_GAUGE_X - 0.020, cowl_face_y + 0.017, SPEEDO_CENTER[2] - 0.018),
            0.002,
            segments=4,
        )
        self._dash_accumulator("GEO_InstrumentBezels", bezels, "instrument_bezels", "Chrome")
        # NOT `Plate`. That slot is shared with GEO_NumberPlate hanging off
        # the nose of the car, so a lit dial there is a glowing number
        # plate. See MATERIALS["CabinLamp"].
        self._dash_accumulator(
            "GEO_InstrumentFaces", faces, "instrument_faces", "CabinLamp"
        )
        markings = MeshAccumulator()
        for x in (SPEEDO_CENTER[0],FUEL_GAUGE_X):
            for i in range(9):
                a=math.radians(-45+33.75*i)
                r=.021 if i%2==0 else .025
                p=lambda radius: (x+math.cos(a)*radius,cowl_face_y+.0135,SPEEDO_CENTER[2]+math.sin(a)*radius)
                forms.trim_path(markings,(p(r),p(.0295)),.00135,4)
        for i in range(13):
            x=-.060+i*.010
            markings.add_box((x,radio_y+.0124,RADIO_DIAL_Z+.007),(.0013,.002,.010 if i%3==0 else .006))
        self._dash_accumulator("GEO_InstrumentMarkings", markings, "instrument_markings", "InstrumentInk")
        speedo = self.add_pivot(
            "PIVOT_SpeedoNeedle",
            "speedo_needle",
            self.body,
            SPEEDO_CENTER,
            runtime_axis_local="+Y",
        )
        needle = MeshAccumulator()
        # At rest on the zero mark, which is seven o'clock as the driver
        # sees it: his left is source +X.
        needle.add_cylinder_between(
            (0.0, 0.002, 0.0), (0.020, 0.002, -0.018), 0.002, segments=4
        )
        self._dash_accumulator(
            "GEO_SpeedoNeedle", needle, "speedo_needle", "Chrome", parent=speedo
        )

        # Furniture. Two slatted vents above the radio, an ashtray lid
        # beside it, and a lip along the dash top where the screen meets it.
        vents = []
        for side in (1.0, -1.0):
            vents.append(((side * 0.075, radio_y + 0.005, 0.995), (0.11, 0.014, 0.05)))
            for step in (-1, 0, 1):
                vents.append(
                    ((side * 0.075, radio_y + 0.012, 0.995 + step * 0.014),
                     (0.10, 0.006, 0.006))
                )
        self._dash_boxes("GEO_DashVents", tuple(vents), "dash_vents", "Interior")
        self._dash_boxes(
            "GEO_Ashtray",
            (
                ((-0.205, radio_y + 0.008, 0.905), (0.10, 0.016, 0.05)),
                ((-0.205, radio_y + 0.019, 0.925), (0.03, 0.006, 0.008)),
            ),
            "ashtray",
            "Interior",
        )
        self._dash_boxes(
            "GEO_DashLip",
            (((0.0, -1.14, DASH_TOP_Z + 0.015), (1.40, 0.04, 0.03)),),
            "dash_lip",
            "Dashboard",
        )

        # The tunnel between the seats with the gear lever and the
        # handbrake on it. Narrower than the gap between the cushions.
        self._dash_boxes(
            "GEO_TransmissionTunnel",
            (((0.0, -0.455, 0.36), (0.30, 1.49, 0.12)),),
            "transmission_tunnel",
            "Interior",
        )
        levers = MeshAccumulator()
        levers.add_cylinder_between((0.0, -0.72, 0.42), (0.0, -0.66, 0.66), 0.006, segments=6)
        levers.add_cylinder_between((0.05, -0.42, 0.44), (0.05, -0.66, 0.50), 0.006, segments=4)
        self._dash_accumulator("GEO_GearLever", levers, "gear_lever", "Metal")
        self._dash_boxes(
            "GEO_LeverKnobs",
            (
                ((0.0, -0.66, 0.67), (0.04, 0.04, 0.035)),
                ((0.05, -0.63, 0.50), (0.024, 0.09, 0.024)),
            ),
            "lever_knobs",
            "Interior",
        )

        # Pedals in the driver's footwell, on stems back to the scuttle -
        # the Ferryman has driven with his feet forward onto these since
        # the day his archetype was measured, and only now are they drawn.
        pedal_stems = MeshAccumulator()
        pedal_plates = []
        for pedal_x, plate_z, plate_height in ((0.30, 0.40, 0.07), (0.42, 0.40, 0.07), (0.53, 0.42, 0.09)):
            pedal_plates.append(((pedal_x, -1.15, plate_z), (0.06, 0.012, plate_height)))
            pedal_stems.add_cylinder_between(
                (pedal_x, -1.15, plate_z + 0.03), (pedal_x, -1.205, 0.55), 0.008, segments=4
            )
        self._dash_boxes("GEO_PedalPlates", tuple(pedal_plates), "pedal_plates", "Rubber")
        self._dash_accumulator("GEO_PedalStems", pedal_stems, "pedal_stems", "Metal")

        # On the column: an indicator stalk off to the driver's left, and
        # the ignition barrel with its key low on the right.
        stalk = MeshAccumulator()
        stalk.add_cylinder_between(
            (0.445, -0.965, 0.848), (0.62, -0.945, 0.862), 0.006, segments=4
        )
        self._dash_accumulator("GEO_ColumnStalk", stalk, "column_stalk", "Metal")
        ignition = MeshAccumulator()
        ignition.add_cylinder_between(
            (0.435, -0.975, 0.84), (0.395, -0.972, 0.838), 0.012, segments=6
        )
        ignition.add_box((0.378, -0.972, 0.828), (0.010, 0.004, 0.030))
        self._dash_accumulator("GEO_IgnitionKey", ignition, "ignition_key", "Chrome")

        # Overhead: two visors folded up under the header, and the stalk
        # that actually holds the mirror to the glass.
        self._dash_boxes(
            "GEO_SunVisors",
            tuple(
                ((side * SEAT_HALF_SPAN_X, -0.815, 1.433), (0.30, 0.12, 0.012))
                for side in (1.0, -1.0)
            ),
            "sun_visor",
            "Interior",
        )
        mirror_stalk = MeshAccumulator()
        mirror_stalk.add_cylinder_between(
            (0.0, -0.855, 1.469), (0.0, -0.939, 1.421), 0.007, segments=4
        )
        self._dash_accumulator("GEO_MirrorStalk", mirror_stalk, "mirror_stalk", "Trim")

    def _build_anchors(self) -> None:
        """Every transform the runtime will ever need, authored here.

        Nothing is found by name at runtime - the Unity prefab builder binds
        each of these into a serialized field, the way the bus does.
        """
        steering = self.add_pivot(
            "PIVOT_SteeringWheel",
            "steering_wheel",
            self.body,
            STEERING_WHEEL_CENTER,
            rotation_euler=(math.radians(STEERING_WHEEL_RAKE_DEGREES), 0.0, 0.0),
            runtime_axis_local="+Z",
        )
        rim = MeshAccumulator()
        rim.add_torus_z(
            (0.0, 0.0, 0.0),
            STEERING_WHEEL_MAJOR_RADIUS,
            STEERING_WHEEL_MINOR_RADIUS,
            32,
            8,
        )
        forms.revolve(rim, (0.,0.,0.), (0,0,1), ((-.012,.037),(.008,.047),(.016,.042)), 16)
        for spoke_angle in (math.radians(210.0), math.radians(330.0), math.radians(90.0)):
            rim.add_cylinder_between(
                (0.0, 0.0, 0.0),
                (
                    math.cos(spoke_angle) * STEERING_WHEEL_MAJOR_RADIUS,
                    math.sin(spoke_angle) * STEERING_WHEEL_MAJOR_RADIUS,
                    0.0,
                ),
                0.011,
                segments=4,
            )
        self.add_accumulator(
            "INT_SteeringWheel", rim, "steering_wheel", "Interior", parent=steering
        )

        # The grips are CHILDREN of the rim pivot, so a later turn of the wheel
        # carries the driver's hand targets with it instead of stranding them.
        for suffix, role, sign in (
            ("L", "left_steering_grip", 1.0),
            ("R", "right_steering_grip", -1.0),
        ):
            self.add_pivot(
                f"ANCHOR_SteeringGrip.{suffix}",
                role,
                steering,
                (
                    sign * STEERING_GRIP_X,
                    -STEERING_GRIP_TOP,
                    STEERING_GRIP_AXIS_OFFSET,
                ),
            )

        # Seat pelvis targets. The X coordinates are exact negations of each
        # other so "the two seats are on opposite sides" is provable to the
        # bit rather than measured with a tolerance.
        self.add_pivot(
            "ANCHOR_DriverSeat",
            "driver_seat_anchor",
            self.body,
            (SEAT_HALF_SPAN_X, SEAT_Y, SEAT_PELVIS_Z),
        )
        self.add_pivot(
            "ANCHOR_PassengerSeat",
            "passenger_seat_anchor",
            self.body,
            (-SEAT_HALF_SPAN_X, SEAT_Y, SEAT_PELVIS_Z),
        )

        # Entry docks sit on the cabin floor plane - that is the plane a
        # seated root stands on, which is what the later ride plan consumes.
        self.add_pivot(
            "ANCHOR_DriverDoorEntry",
            "driver_door_entry",
            self.body,
            (TRACK_HALF, SEAT_Y, FLOOR_Z),
        )
        self.add_pivot(
            "ANCHOR_PassengerDoorEntry",
            "passenger_door_entry",
            self.body,
            (-TRACK_HALF, SEAT_Y, FLOOR_Z),
        )

        # Where the Ferryman sits. The soles anchor is his stance root -
        # the rig measures a perched pose from the sitter's own soles - and
        # the seat anchor is where his backside lands on the bonnet. Both
        # sit slightly to the passenger side so he is not straddling the
        # bonnet's centre seam, and both face the nose, because the car is
        # parked nose-out and he is watching the way in.
        self.add_pivot(
            "ANCHOR_PerchSoles",
            "perch_soles",
            self.body,
            PERCH_SOLES,
            rotation_euler=(0.0, 0.0, math.radians(180.0)),
        )
        self.add_pivot(
            "ANCHOR_PerchSeat",
            "perch_seat",
            self.body,
            PERCH_SEAT,
        )

def triangulated_count(mesh: bpy.types.Mesh) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)


def stable_float(value: float) -> float:
    rounded = round(float(value), 6)
    return 0.0 if rounded == -0.0 else rounded


def mesh_world_bounds(objects: Iterable[bpy.types.Object]) -> tuple[Vector, Vector]:
    minimum = Vector((math.inf, math.inf, math.inf))
    maximum = Vector((-math.inf, -math.inf, -math.inf))
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            minimum.x = min(minimum.x, world.x)
            minimum.y = min(minimum.y, world.y)
            minimum.z = min(minimum.z, world.z)
            maximum.x = max(maximum.x, world.x)
            maximum.y = max(maximum.y, world.y)
            maximum.z = max(maximum.z, world.z)
    return minimum, maximum


def build_signature(result: BuildResult) -> str:
    payload: list[object] = [
        GENERATOR_VERSION,
        DESIGN_ID,
        [LENGTH, WIDTH, HEIGHT, WHEELBASE, WHEEL_RADIUS],
    ]
    for part in result.parts:
        mesh = part.obj.data
        payload.append(
            {
                "name": part.obj.name,
                "role": part.role,
                "slot": part.material_slot,
                "parent": part.obj.parent.name if part.obj.parent else "",
                "vertices": [
                    [stable_float(value) for value in vertex.co]
                    for vertex in mesh.vertices
                ],
                "faces": [list(polygon.vertices) for polygon in mesh.polygons],
                "uv": [[stable_float(v) for v in loop.uv] for loop in mesh.uv_layers[0].data],
                "smooth": [polygon.use_smooth for polygon in mesh.polygons],
            }
        )
    for pivot in result.pivots:
        payload.append(
            {
                "name": pivot.obj.name,
                "role": pivot.role,
                "parent": pivot.obj.parent.name if pivot.obj.parent else "",
                "location": [stable_float(value) for value in pivot.obj.location],
                "rotation_euler": [
                    stable_float(value) for value in pivot.obj.rotation_euler
                ],
                "runtime_axis_local": pivot.runtime_axis_local,
                "travel_m": stable_float(pivot.travel_m),
            }
        )
    encoded = json.dumps(payload, separators=(",", ":"), sort_keys=True).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()

def select_export_objects(result: BuildResult) -> None:
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in result.source_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = result.root


def export_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    select_export_objects(result)
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
        use_custom_props=True,
    )


def render_preview(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections[PRESENTATION_COLLECTION]
    camera_data = bpy.data.cameras.new("CAM_LastRouteCarPreview")
    camera = bpy.data.objects.new("CAM_LastRouteCarPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = (-5.8, -6.5, 2.8)
    target = Vector((-0.04, -0.06, 0.77))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 50
    scene.camera = camera

    for name, location, energy, color, size in (
        ("Key", (-5.0, -7.0, 8.0), 1700.0, (0.72, 0.84, 0.75), 5.0),
        ("Rim", (5.5, 4.0, 6.0), 1300.0, (0.28, 0.45, 0.42), 4.0),
        ("Warm", (-3.0, 1.0, 2.2), 750.0, (0.92, 0.50, 0.20), 3.0),
    ):
        light_data = bpy.data.lights.new(f"LIGHT_{name}", "AREA")
        light_data.energy = energy
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(f"LIGHT_{name}", light_data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()

    ground = MeshAccumulator()
    ground.add_box((0.0, 0.20, -0.045), (12.0, 11.0, 0.09))
    ground_material = bpy.data.materials.new("MAT_PreviewGround")
    ground_material.use_nodes = True
    ground_material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (.10,.106,.102,1.)
    ground_material.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = .85
    create_part(
        "PreviewGround",
        ground,
        "preview",
        "Preview",
        presentation,
        None,
        ground_material,
    )
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate the Last Route ferry car."
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("ArtSource/Vehicles/Blender/LastRouteCar3D.blend"),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path("Assets/Vehicles/Models/LastRouteCar3D.fbx"),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("Assets/Vehicles/Models/LastRouteCar3D.json"),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path("ArtSource/Vehicles/Blender/LastRouteCar3D.png"),
    )
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--model-dir", type=Path)
    parser.add_argument("--source-dir", type=Path)
    blender_separator = sys.argv.index("--") + 1 if "--" in sys.argv else len(sys.argv)
    args = parser.parse_args(sys.argv[blender_separator:])
    if args.model_dir:
        args.fbx, args.manifest = (args.model_dir / path.name for path in (args.fbx, args.manifest))
    if args.source_dir:
        args.output, args.preview = (args.source_dir / path.name for path in (args.output, args.preview))
    for field_name in ("output", "fbx", "manifest", "preview"):
        value = getattr(args, field_name)
        setattr(args, field_name, value.resolve())
    return args


REQUIRED_MESH_ROLES = (
    "body_shell", "glass", "cracked_glass", "window_seals", "door_panel", "door_glass",
    "door_handle", "door_card", "door_frame", "wheel", "wheel_rim", "hubcap",
    "headlight", "headlight_rim", "tail_light", "bumper", "exterior_trim", "exhaust",
    "number_plate", "interior_shell", "driver_seat", "passenger_seat", "rear_bench",
    "dashboard", "steering_wheel", "floor_mats",
    "glovebox_compartment", "glovebox_lid", "glovebox_catch", "radio_dial", "radio_knob",
    "radio_needle", "instrument_bezels", "instrument_faces", "instrument_markings", "speedo_needle",
    "cabin_lamp_housing", "cabin_lamp_lens", "glovebox_bulb", "dash_vents",
)

REQUIRED_PIVOT_ROLES = (
    "front_left_wheel", "front_right_wheel",
    "rear_left_wheel", "rear_right_wheel",
    "driver_door_leaf", "passenger_door_leaf",
    "steering_wheel", "left_steering_grip", "right_steering_grip",
    "driver_seat_anchor", "passenger_seat_anchor",
    "driver_door_entry", "passenger_door_entry",
    "perch_soles", "perch_seat",
    "glovebox_lid", "radio_power_knob", "radio_tuning_knob",
    "radio_needle", "speedo_needle",
)

# The damage the design is FOR. Asserting the counts is what stops a later
# styling pass from quietly repairing the car.
DAMAGE_FEATURES = (
    "dents", "rust", "cracked_glass", "mismatched_door", "missing_hubcap",
)


def _spans(
    center: Sequence[float], size: Sequence[float]
) -> tuple[tuple[float, float], tuple[float, float], tuple[float, float]]:
    return tuple(
        (center[axis] - size[axis] * 0.5, center[axis] + size[axis] * 0.5)
        for axis in range(3)
    )


def _overlaps(
    spans: Sequence[tuple[float, float]],
    limits: Sequence[tuple[float, float]],
) -> bool:
    return all(
        min(span[1], limit[1]) - max(span[0], limit[0]) > 1e-4
        for span, limit in zip(spans, limits)
    )


def _validate_dashboard(
    result: BuildResult,
    require_pivot,
    errors: list[str],
) -> None:
    """The dash: its two mechanisms, and that it stays out of everyone.

    Three promises the runtime and the seat's camera rely on. The glovebox
    aperture is a HOLE in the slab, or the lid opens onto bodywork; nothing
    drawn here stands inside either sitter, or a knee goes through a lever;
    and nothing crosses the windscreen between the dash top and the header,
    or the passenger's whole reason for being in the seat is blocked.
    """
    roles = [part.role for part in result.parts]
    if roles.count("radio_knob") != 2:
        errors.append("The radio has exactly two knobs")
    dials = [part for part in result.parts if part.role == "radio_dial"]
    if len(dials) != 1 or dials[0].material_slot != "RadioDial":
        errors.append("The radio has one dial, on its own lit slot")

    lid = require_pivot(
        "PIVOT_GloveboxLid", "glovebox_lid", result.body, GLOVEBOX_HINGE,
        runtime_axis_local="+X",
    )
    power = require_pivot(
        "PIVOT_RadioPowerKnob", "radio_power_knob", result.body,
        (RADIO_KNOB_X, RADIO_CENTER[1], RADIO_KNOB_Z), runtime_axis_local="+Y",
    )
    tuning = require_pivot(
        "PIVOT_RadioTuningKnob", "radio_tuning_knob", result.body,
        (-RADIO_KNOB_X, RADIO_CENTER[1], RADIO_KNOB_Z), runtime_axis_local="+Y",
    )
    needle = require_pivot(
        "PIVOT_RadioNeedle", "radio_needle", result.body,
        (RADIO_CENTER[0] - RADIO_NEEDLE_TRAVEL_M * 0.5, RADIO_CENTER[1], RADIO_DIAL_Z),
        runtime_axis_local="+X",
    )
    speedo = require_pivot(
        "PIVOT_SpeedoNeedle", "speedo_needle", result.body, SPEEDO_CENTER,
        runtime_axis_local="+Y",
    )
    if needle is not None:
        if abs(needle.travel_m - RADIO_NEEDLE_TRAVEL_M) > 1e-6:
            errors.append("The radio needle's travel drifted from its dial")
        if RADIO_NEEDLE_TRAVEL_M > RADIO_DIAL_WIDTH - 0.02:
            errors.append("The radio needle would run off the end of its dial")

    # Ownership: each pivot carries exactly the thing that moves with it.
    for pivot, expected in (
        (lid, ["glovebox_catch", "glovebox_lid"]),
        (power, ["radio_knob"]),
        (tuning, ["radio_knob"]),
        (needle, ["radio_needle"]),
        (speedo, ["speedo_needle"]),
    ):
        if pivot is None:
            continue
        child_roles = sorted(
            part.role for part in result.parts if part.obj.parent is pivot.obj
        )
        if child_roles != expected:
            errors.append(
                f"{pivot.obj.name} owns {child_roles}, expected {expected}"
            )

    # The aperture has to be a hole in the slab, checked on the boxes as
    # authored - the doorway's rule.
    aperture = (
        GLOVEBOX_X,
        (DASH_SLAB_Y - DASH_SLAB_DEPTH * 0.5, DASH_REAR_Y),
        GLOVEBOX_Z,
    )
    for center, size in result.dash_boxes:
        if _overlaps(_spans(center, size), aperture):
            errors.append(f"Dashboard box at {center} bricks up the glovebox")

    # And the lid has to be the size the knee-clearance promise was made
    # for: hinged at the aperture's bottom edge, no taller than the cap.
    lid_parts = [part for part in result.parts if part.role == "glovebox_lid"]
    if lid_parts:
        lid_min, lid_max = mesh_world_bounds(part.obj for part in lid_parts)
        if lid_min.z < GLOVEBOX_HINGE[2] - 0.005:
            errors.append("The glovebox lid hangs below its own hinge")
        if lid_max.z - lid_min.z > GLOVEBOX_LID_HEIGHT + 1e-4:
            errors.append(
                "The glovebox lid is taller than the knee clearance allows"
            )

    # Nothing on the dash inside either sitter. Legs reach forward from the
    # pelvis row to just short of the scuttle; the torso stands over the
    # cushion up to the roof. The tunnel is narrower than the gap between
    # the cushions and so needs no exemption.
    for side in (1.0, -1.0):
        seat_x = (
            min(side * SEAT_HALF_SPAN_X - SEAT_CUSHION_WIDTH * 0.5,
                side * SEAT_HALF_SPAN_X + SEAT_CUSHION_WIDTH * 0.5),
            max(side * SEAT_HALF_SPAN_X - SEAT_CUSHION_WIDTH * 0.5,
                side * SEAT_HALF_SPAN_X + SEAT_CUSHION_WIDTH * 0.5),
        )
        legs = (seat_x, (-0.95, SEAT_Y + 0.25), (FLOOR_Z + 0.03, 0.80))
        torso = (
            seat_x,
            (SEAT_Y - 0.20, SEAT_Y + 0.25),
            (FLOOR_Z + 0.03, ROOF_UNDERSIDE_Z - 0.02),
        )
        for part in result.dashboard_parts:
            part_min, part_max = mesh_world_bounds([part.obj])
            spans = tuple(zip(part_min, part_max))
            sitter = "driver" if side > 0 else "passenger"
            if _overlaps(spans, legs):
                errors.append(f"{part.obj.name} stands in the {sitter}'s legs")
            if _overlaps(spans, torso):
                errors.append(f"{part.obj.name} stands in the {sitter}'s body")

    # Nothing across the windscreen: anything ahead of the seat row is
    # either dashboard-height or up under the header with the mirror.
    for part in result.dashboard_parts:
        part_min, part_max = mesh_world_bounds([part.obj])
        ahead = min(part_max.y, SEAT_Y) - max(part_min.y, CABIN_FRONT_Y) > 1e-4
        if ahead and part_max.z > 1.10 and part_min.z < 1.41:
            errors.append(f"{part.obj.name} crosses the windscreen")


def validate_result(result: BuildResult) -> ValidationReport:
    errors: list[str] = []
    names = [obj.name for obj in result.source_objects]
    if len(names) != len(set(names)):
        errors.append("Source hierarchy contains duplicate names")
    numeric_suffix = re.compile(r"\.\d{3}$")
    for name in names:
        if numeric_suffix.search(name):
            errors.append(f"Unexpected Blender numeric suffix on {name}")

    mesh_count = len(result.parts)
    triangle_count = sum(triangulated_count(part.obj.data) for part in result.parts)
    if triangle_count < MIN_TRIANGLES or triangle_count > MAX_TRIANGLES:
        errors.append(
            f"Triangle count {triangle_count} is outside {MIN_TRIANGLES}-{MAX_TRIANGLES}"
        )
    if any(part.obj.type != "MESH" for part in result.parts):
        errors.append("A registered car part is not a mesh")
    if any(len(part.obj.data.materials) != 1 for part in result.parts):
        errors.append("Every car mesh must carry one deterministic material slot")

    roles = [part.role for part in result.parts]
    role_set = set(roles)
    for required_role in REQUIRED_MESH_ROLES:
        if required_role not in role_set:
            errors.append(f"Missing required mesh role {required_role}")

    # The wreck, asserted. A repaired car is a different design and has to say
    # so out loud rather than drifting in one styling pass at a time.
    if roles.count("wheel") != 4:
        errors.append("The car must keep all four wheels")
    if roles.count("hubcap") != 3:
        errors.append("Exactly one hubcap must be missing")
    if roles.count("headlight") != 2 or "broken_headlight" in role_set:
        errors.append("The two working headlights must have individually bound lenses")
    # Measure the real aperture, without either moving door. A nominal
    # box or the part's bounding box cannot prove that a person fits in it.
    fixed_roles = {"body_shell", "pillars", "underbody", "headlining", "deck_skins"}
    solids = []
    for part in result.parts:
        if part.role in fixed_roles:
            obj = part.obj
            solids.append(BVHTree.FromPolygons([obj.matrix_world @ v.co for v in obj.data.vertices],
                                                [tuple(p.vertices) for p in obj.data.polygons]))
    for side in (-1., 1.):
        for y in (-.66, -.44, -.20, .12):
            for z in (.505, .67, .85, 1.03, 1.24, 1.40):
                if any(tree.ray_cast(Vector((side*1.05, y,z)), Vector((-side,0.,0.)), .365)[0] is not None
                       for tree in solids):
                    errors.append(f"Fixed shell obstructs a door: {(side,y,z)}")
    # All three deformations must be sunk below their fair flank.
    for side,y,z,width,height,depth in DENTS:
        if flank_x(side, y, z) >= .89 or not .014 <= depth <= .06:
            errors.append("Dent has lost its shallow pressed form")
    for part in result.parts:
        if part.material_slot in ("Body", "AccentPaint"):
            if any(not 0. <= value <= 1. for loop in part.obj.data.uv_layers[0].data for value in loop.uv):
                errors.append(f"Paint UVs escape the car atlas: {part.obj.name}")

    door_panels = [part for part in result.parts if part.role == "door_panel"]
    if len(door_panels) != 2:
        errors.append("The car carries exactly two front door panels")
    elif len({part.material_slot for part in door_panels}) != 2:
        errors.append("One door must be in the wrong colour")

    pivot_roles = {pivot.role for pivot in result.pivots}
    for required_role in REQUIRED_PIVOT_ROLES:
        if required_role not in pivot_roles:
            errors.append(f"Missing required pivot role {required_role}")

    pivots_by_name = {pivot.obj.name: pivot for pivot in result.pivots}

    def require_pivot(
        name: str,
        role: str,
        parent: bpy.types.Object,
        location: Sequence[float],
        rotation_euler: Sequence[float] = (0.0, 0.0, 0.0),
        runtime_axis_local: str = "",
    ) -> Pivot | None:
        pivot = pivots_by_name.get(name)
        if pivot is None:
            errors.append(f"Missing required control pivot {name}")
            return None
        if pivot.role != role:
            errors.append(f"Control pivot {name} has role {pivot.role}, expected {role}")
        if pivot.obj.parent is not parent:
            errors.append(f"Control pivot {name} has the wrong parent")
        if any(
            abs(actual - expected) > 1e-6
            for actual, expected in zip(pivot.obj.location, location)
        ):
            errors.append(f"Control pivot {name} has a stale local position")
        if any(
            abs(actual - expected) > 1e-6
            for actual, expected in zip(pivot.obj.rotation_euler, rotation_euler)
        ):
            errors.append(f"Control pivot {name} has a stale local rotation")
        if pivot.runtime_axis_local != runtime_axis_local:
            errors.append(f"Control pivot {name} has a stale runtime axis")
        return pivot

    steering = require_pivot(
        "PIVOT_SteeringWheel",
        "steering_wheel",
        result.body,
        STEERING_WHEEL_CENTER,
        (math.radians(STEERING_WHEEL_RAKE_DEGREES), 0.0, 0.0),
        "+Z",
    )
    if steering is not None:
        for suffix, role, sign in (
            ("L", "left_steering_grip", 1.0),
            ("R", "right_steering_grip", -1.0),
        ):
            require_pivot(
                f"ANCHOR_SteeringGrip.{suffix}",
                role,
                steering.obj,
                (
                    sign * STEERING_GRIP_X,
                    -STEERING_GRIP_TOP,
                    STEERING_GRIP_AXIS_OFFSET,
                ),
            )
        rim_parts = [part for part in result.parts if part.obj.parent is steering.obj]
        if len(rim_parts) != 1 or rim_parts[0].role != "steering_wheel":
            errors.append("PIVOT_SteeringWheel must directly own its single rim mesh")

    driver_seat = require_pivot(
        "ANCHOR_DriverSeat",
        "driver_seat_anchor",
        result.body,
        (SEAT_HALF_SPAN_X, SEAT_Y, SEAT_PELVIS_Z),
    )
    passenger_seat = require_pivot(
        "ANCHOR_PassengerSeat",
        "passenger_seat_anchor",
        result.body,
        (-SEAT_HALF_SPAN_X, SEAT_Y, SEAT_PELVIS_Z),
    )
    if driver_seat is not None and passenger_seat is not None:
        driver_location = driver_seat.obj.location
        passenger_location = passenger_seat.obj.location
        # Exact negation, not a tolerance: the later ride plan rejects a seat
        # that shares a side with the driver, and this is that predicate
        # proved at asset level so it can never be met in a scene instead.
        if driver_location.x != -passenger_location.x or driver_location.x == 0.0:
            errors.append("The two seats must sit on exactly opposite sides")
        if (
            abs(driver_location.y - passenger_location.y) > 1e-6
            or abs(driver_location.z - passenger_location.z) > 1e-6
        ):
            errors.append("The two seats must share a row and a height")

    require_pivot(
        "ANCHOR_DriverDoorEntry",
        "driver_door_entry",
        result.body,
        (TRACK_HALF, SEAT_Y, FLOOR_Z),
    )
    require_pivot(
        "ANCHOR_PassengerDoorEntry",
        "passenger_door_entry",
        result.body,
        (-TRACK_HALF, SEAT_Y, FLOOR_Z),
    )
    perch_soles = require_pivot(
        "ANCHOR_PerchSoles",
        "perch_soles",
        result.body,
        PERCH_SOLES,
        (0.0, 0.0, math.radians(180.0)),
    )
    perch_seat = require_pivot(
        "ANCHOR_PerchSeat", "perch_seat", result.body, PERCH_SEAT
    )

    if perch_seat is not None:
        # He sits ON the bonnet, not in it and not above it.
        if abs(perch_seat.obj.location.z - hood_z(*perch_seat.obj.location.xy)) > .003:
            errors.append("The perch seat must rest on the bonnet skin")
        if not (FRONT_Y < perch_seat.obj.location.y < CABIN_FRONT_Y):
            errors.append("The perch seat must sit over the bonnet, not the roof")
    if perch_soles is not None and perch_seat is not None:
        drop = perch_seat.obj.location.z - perch_soles.obj.location.z
        if abs(drop - PERCH_DROP_M) > 1e-6:
            errors.append("The perch drop drifted from the shared contract")
        if perch_soles.obj.location.y >= perch_seat.obj.location.y:
            errors.append("His boots must be ahead of his seat, on the bumper")

    # The paired rig check verifies posed head and foot clearances.
    headroom = ROOF_UNDERSIDE_Z - SEAT_PELVIS_Z
    if headroom < 0.94 or headroom > 1.055:
        errors.append(
            f"Seated headroom {headroom:.3f}m is outside the sedan cabin envelope"
        )

    for name, expected_role in (
        ("PIVOT_DoorDriver", "driver_door_leaf"),
        ("PIVOT_DoorPassenger", "passenger_door_leaf"),
    ):
        pivot = pivots_by_name.get(name)
        if pivot is None:
            errors.append(f"Missing required door leaf pivot {name}")
            continue
        if pivot.role != expected_role:
            errors.append(f"Door leaf pivot {name} has role {pivot.role}")
        child_roles = sorted(
            part.role for part in result.parts if part.obj.parent is pivot.obj
        )
        required_parts = ("door_panel", "door_glass", "door_handle", "door_card", "door_armrest", "door_frame", "door_seal")
        for required in required_parts:
            if child_roles.count(required) != 1:
                errors.append(
                    f"Door leaf pivot {name} must own exactly one "
                    f"{required} - it is what swings"
                )
        if set(child_roles) != set(required_parts):
            errors.append(f"{name} must move as one finished door assembly")

    _validate_dashboard(result, require_pivot, errors)

    bounds_min, bounds_max = mesh_world_bounds(part.obj for part in result.parts)
    size = bounds_max - bounds_min
    if abs(bounds_min.z) > 0.005 or abs(bounds_max.z - HEIGHT) > 0.015:
        errors.append(
            f"Ground/height bounds are {bounds_min.z:.3f}..{bounds_max.z:.3f}, expected 0..{HEIGHT}"
        )
    # The upper tolerance is the bumpers, which overhang the body on purpose.
    if size.y < LENGTH - 0.08 or size.y > LENGTH + 0.30:
        errors.append(f"Visual length {size.y:.3f}m differs from {LENGTH:.2f}m")
    if size.x < WIDTH - .02 or size.x > WIDTH + 0.30:
        errors.append(f"Visual width {size.x:.3f}m is outside body-plus-mirror contract")
    if result.root.location.length > 1e-6 or result.body.location.length > 1e-6:
        errors.append("Car source/body roots must remain at the world origin")
    if bpy.data.actions:
        errors.append("The presentation FBX must contain no authored animation")
    if any(obj.type in {"ARMATURE", "CAMERA", "LIGHT"} for obj in result.source_objects):
        errors.append("Source export hierarchy contains rig, camera or light objects")

    if errors:
        formatted = "\n".join(f"- {error}" for error in errors)
        raise RuntimeError(f"Last Route car 3D validation failed:\n{formatted}")
    return ValidationReport(
        mesh_count,
        triangle_count,
        tuple(stable_float(value) for value in bounds_min),
        tuple(stable_float(value) for value in bounds_max),
        build_signature(result),
    )


def write_manifest(path: Path, result: BuildResult, report: ValidationReport) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-last-route-car-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "forward_axis": "-Y",
        "driver_side_axis": "+X",
        "unity_runtime_forward_axis": "+Z",
        "dimensions_m": {
            "length": LENGTH,
            "width": WIDTH,
            "height": HEIGHT,
            "wheelbase": WHEELBASE,
            "wheel_radius": WHEEL_RADIUS,
        },
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "bounds_min": list(report.bounds_min),
        "bounds_max": list(report.bounds_max),
        "colliders": False,
        "animation_count": 0,
        "animations": [],
        "visible_interior": True,
        "wheel_count": sum(part.role == "wheel" for part in result.parts),
        "hubcap_count": sum(part.role == "hubcap" for part in result.parts),
        "damage_features": list(DAMAGE_FEATURES),
        # Read by the Ferryman's own manifest check: his authored perch
        # pose puts his backside and his soles on these numbers, and one
        # EditMode test reads both files and fails if either moves.
        "perch_seat_z": PERCH_SEAT[2],
        "perch_soles_z": PERCH_SOLES[2],
        "perch_drop_m": stable_float(PERCH_DROP_M),
        "seated_headroom_m": stable_float(ROOF_UNDERSIDE_Z - SEAT_PELVIS_Z),
        # How much leg fits under a seated driver before it is through the
        # floor pan. Route 01 allows 0.41 m and this car allows barely half
        # of that, which is why its driver reaches his feet forward onto the
        # pedals instead of hanging them - and why the Ferryman's archetype
        # declares the same number back. The cross-manifest test is what
        # keeps the two from drifting apart.
        "cabin_floor_drop_m": stable_float(SEAT_PELVIS_Z - FLOOR_Z),
        # The dash, for the editor's own binding checks. The runtime reads
        # only the needle pivot's travel and measures the rest off the
        # drawn parts, the doors' rule.
        "dashboard_top_z": DASH_TOP_Z,
        "glovebox_lid_open_degrees": GLOVEBOX_LID_OPEN_DEGREES,
        "radio_dial_width_m": RADIO_DIAL_WIDTH,
        "build_signature": report.build_signature,
        "parts": [
            {
                "name": part.obj.name,
                "role": part.role,
                "material_slot": part.material_slot,
                "parent": part.obj.parent.name if part.obj.parent else "",
                "vertices": len(part.obj.data.vertices),
                "triangles": triangulated_count(part.obj.data),
            }
            for part in result.parts
        ],
        "pivots": [
            {
                "name": pivot.obj.name,
                "role": pivot.role,
                "parent": pivot.obj.parent.name if pivot.obj.parent else "",
                "local_position": [stable_float(value) for value in pivot.obj.location],
                "local_rotation_degrees": [
                    stable_float(math.degrees(value))
                    for value in pivot.obj.rotation_euler
                ],
                "runtime_axis_local": pivot.runtime_axis_local,
                "travel_m": stable_float(pivot.travel_m),
            }
            for pivot in result.pivots
        ],
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


def main() -> None:
    config = parse_args()
    result = LastRouteCarBuilder().build()
    bpy.context.view_layer.update()
    report = validate_result(result)
    if config.validate_only:
        existing = json.loads(config.manifest.read_text())
        if existing["build_signature"] != report.build_signature:
            raise RuntimeError("Last Route car asset differs from deterministic source")
        print("LAST ROUTE CAR 3D CONTRACT OK")
        return
    if not config.no_preview:
        render_preview(config.preview, result)
    export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    save_blend(config.output)
    print("LAST ROUTE CAR 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(f"  Dimensions: {LENGTH:.2f} x {WIDTH:.2f} x {HEIGHT:.2f} m")
    print(f"  Seated headroom: {ROOF_UNDERSIDE_Z - SEAT_PELVIS_Z:.3f} m")
    print(f"  Meshes: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{MAX_TRIANGLES}")
    print(f"  Signature: {report.build_signature}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")


if __name__ == "__main__":
    main()
