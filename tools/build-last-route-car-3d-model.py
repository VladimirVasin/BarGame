#!/usr/bin/env python3
"""Build the Last Route ferry car - the city's one parked vehicle.

Run through Blender 5, for example::

    blender --background --factory-startup --python \
      tools/build-last-route-car-3d-model.py

A period saloon, beaten but plainly still running: dents, rust blooms, a
cracked quarter light, one broken headlight, a door in the wrong colour and a
missing hubcap - with all four wheels on and the glass intact enough to drive
behind. The Ferryman leans on its passenger flank and waits.

The height is not a styling choice. The hero will later be seated in this car
by reusing the bus clips verbatim, and the shared rig's seated head clearance
band is 0.99-1.10 m above the pelvis. A modern saloon roof would give 0.92 m
and force a bespoke clip; this one gives 1.04 m and forces nothing.

Deterministic output: a meter-scale editable .blend, a hierarchy-preserving
FBX, a JSON contract for Unity's prefab builder and a review render. Blender
source space is Z-up with vehicle forward along -Y and source +X on the
DRIVER side. The Unity prefab builder rotates the imported model 180 degrees
so its local runtime forward is +Z.
"""

from __future__ import annotations

import argparse
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
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise RuntimeError(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "last_route_ferry_car_v1"
DISPLAY_NAME = "Last Route Ferry Car"
SEED = 470301

LENGTH = 4.83
WIDTH = 1.80
HEIGHT = 1.62
WHEELBASE = 2.70
WHEEL_RADIUS = 0.34
WHEEL_WIDTH = 0.19
MIN_TRIANGLES = 1200
MAX_TRIANGLES = 4200

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
ROOF_UNDERSIDE_Z = 1.56
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
PERCH_SEAT = (-0.28, -2.02, BONNET_Z)
PERCH_SOLES = (-0.28, -2.44, 0.575)
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

DOOR_HINGE_Y = -1.17    # the A-pillar base; the door opens forward of it
DOOR_HINGE_Z = 0.73
DOOR_LEAF_LENGTH = 1.51
# How far the shut leaf laps the jamb it sits against. The leaf is a skin
# hung OUTBOARD of the flank rather than a plug in the hole, so this only
# has to beat the grazing angle at which a coincident edge shows a seam.
DOOR_LEAF_LAP = 0.03
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

# Damage the user asked for, each its own mesh so a validator can prove it is
# still there and so it reads in silhouette rather than relying on texture.
#
# Anything that lands within a FRONT leaf's footprint has to ride that leaf
# instead - see DOOR_DENT_SPECS below. The rear-door crease stays here
# because the rear doors never open.
DENT_SPECS = (
    ((-0.902, 1.05, 0.80), (0.045, 0.46, 0.26)),
    ((0.55, REAR_Y + 0.02, 0.86), (0.60, 0.06, 0.22)),
    ((-0.40, FRONT_Y - 0.01, 0.70), (0.52, 0.05, 0.20)),
)

# How far proud of the leaf's own skin door-mounted damage sits. The skin's
# outer face is at local x 0.035; rust straddles it the way rust does, and
# the crease stands as far out as it once stood of the flank.
DOOR_RUST_PROUD_X = 0.035
DOOR_DENT_PROUD_X = 0.037

# The crease down the driver's door, keyed by the leaf it belongs to. It was
# drawn into the body's GEO_Dents, which made it invisible from the day the
# leaf was fitted over it and would have left it hanging in the empty doorway
# from the day the leaf could move - the stranded-handle bug for the third
# time. See the comment in `_build_doors`.
DOOR_DENT_SPECS = {
    "Driver": (((0.902, -0.20, 0.74), (0.045, 0.52, 0.30)),),
}
def _rust_run(
    x: float,
    start_y: float,
    patches: Sequence[tuple[float, float, float]],
) -> tuple[tuple[tuple[float, float, float], tuple[float, float, float]], ...]:
    """A bloom of rust as short patches at drifting heights.

    One long box reads as a painted stripe. Rust reaches along a seam in
    fits, so the silhouette of the run has to be ragged.
    """
    run = []
    cursor = start_y
    for length, height, lift in patches:
        run.append(
            (
                (x, cursor + length * 0.5, lift),
                (0.020, length, height),
            )
        )
        cursor += length + 0.045
    return tuple(run)


def _onto_door_leaf(
    boxes: Sequence[tuple[tuple[float, float, float], tuple[float, float, float]]],
    side: float,
    proud_x: float,
) -> tuple[tuple[tuple[float, float, float], tuple[float, float, float]], ...]:
    """Re-express flank decoration in a front door hinge's own space.

    Only the height and the length carry over; the lateral coordinate is
    replaced outright, because a box authored against the flank at +/-0.905
    ends up buried inside the leaf that was later hung over it at 0.880-0.930.
    """
    return tuple(
        (
            (side * proud_x, center[1] - DOOR_HINGE_Y, center[2] - DOOR_HINGE_Z),
            size,
        )
        for center, size in boxes
    )


RUST_SPECS = (
    # Around the rear arches, where the road throws everything.
    *_rust_run(0.905, 0.86, ((0.24, 0.13, 0.76), (0.19, 0.09, 0.72),
                             (0.30, 0.15, 0.78))),
    *_rust_run(-0.905, 0.92, ((0.27, 0.12, 0.74), (0.21, 0.08, 0.70))),
    # The boot lip, and a bloom creeping along the underside seam. The seam
    # run sits just UNDER the cabin floor: it used to be swallowed by the
    # rocker slab, and with that slab gone it would otherwise surface as a
    # rust stripe laid across the cabin carpet.
    ((0.24, REAR_Y - 0.02, 0.60), (0.34, 0.024, 0.09)),
    ((-0.34, REAR_Y - 0.02, 0.58), (0.26, 0.024, 0.07)),
    ((-0.58, 0.10, FLOOR_Z - 0.045), (0.30, 1.20, 0.020)),
)

# Along both sills, worst behind the front arches - which is to say, along
# the bottom of both front doors. These ride their leaves for the same
# reason the handles do.
DOOR_RUST_SPECS = {
    "Driver": _rust_run(0.905, -0.95, ((0.26, 0.10, 0.50), (0.17, 0.07, 0.53),
                                       (0.31, 0.12, 0.49), (0.14, 0.06, 0.55))),
    "Passenger": _rust_run(-0.905, -0.90, ((0.22, 0.09, 0.51),
                                           (0.28, 0.11, 0.48),
                                           (0.15, 0.06, 0.54))),
}

UV_TILE_METERS_DEFAULT = 1.10
UV_TILE_METERS = {
    "Body": 1.4,
    "AccentPaint": 1.4,
    "Rust": 0.55,
    "Trim": 0.9,
    "Chrome": 0.9,
    "Interior": 1.0,
    "Dashboard": 0.8,
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
    "Glass": ((0.085, 0.155, 0.160, 0.30), 0.04, 0.20, 0.0),
    "CrackedGlass": ((0.62, 0.66, 0.64, 0.55), 0.04, 0.46, 0.0),
    "BrokenGlass": ((0.055, 0.060, 0.058, 1.0), 0.10, 0.55, 0.0),
    "Interior": ((0.085, 0.085, 0.080, 1.0), 0.0, 0.80, 0.0),
    "Seat": ((0.135, 0.115, 0.085, 1.0), 0.0, 0.86, 0.0),
    "Dashboard": ((0.062, 0.060, 0.055, 1.0), 0.0, 0.74, 0.0),
    "Headlight": ((0.98, 0.94, 0.78, 1.0), 0.02, 0.18, 9.0),
    "TailLight": ((0.55, 0.020, 0.012, 1.0), 0.0, 0.30, 2.4),
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
    # The body shell's boxes as authored, kept so the validator can prove
    # the door openings are still openings. Once the shell is a mesh there
    # is no cheap way to ask whether a hole in it is still a hole, and this
    # design has already shipped once with both doorways bricked up.
    shell_boxes: tuple[
        tuple[tuple[float, float, float], tuple[float, float, float]], ...
    ] = ()


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
        cx, cy, cz = center
        hx, hy, hz = (component * 0.5 for component in size)
        base = len(self.vertices)
        self.vertices.extend(
            [
                (cx - hx, cy - hy, cz - hz),
                (cx + hx, cy - hy, cz - hz),
                (cx + hx, cy + hy, cz - hz),
                (cx - hx, cy + hy, cz - hz),
                (cx - hx, cy - hy, cz + hz),
                (cx + hx, cy - hy, cz + hz),
                (cx + hx, cy + hy, cz + hz),
                (cx - hx, cy + hy, cz + hz),
            ]
        )
        self.faces.extend(
            [
                (base + 0, base + 3, base + 2, base + 1),
                (base + 4, base + 5, base + 6, base + 7),
                (base + 0, base + 1, base + 5, base + 4),
                (base + 1, base + 2, base + 6, base + 5),
                (base + 2, base + 3, base + 7, base + 6),
                (base + 3, base + 0, base + 4, base + 7),
            ]
        )

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
        material.surface_render_method = "DITHERED"
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


def apply_box_uvs(mesh: bpy.types.Mesh, tile_meters: float) -> None:
    """Project deterministic per-face box UVs at world scale.

    Every loop takes its UV from the two vertex coordinates orthogonal to the
    face normal's dominant axis, divided by the slot's tile size, so tileable
    albedos land on the geometry without a hand unwrap and Unity materials can
    keep (1, 1) tiling.
    """
    uv_layer = mesh.uv_layers.new(name="UVMap")
    scale = 1.0 / tile_meters
    for polygon in mesh.polygons:
        normal = polygon.normal
        ax, ay, az = abs(normal.x), abs(normal.y), abs(normal.z)
        for loop_index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if az >= ax and az >= ay:
                uv = (vertex.x, vertex.y)
            elif ax >= ay:
                uv = (vertex.y, vertex.z)
            else:
                uv = (vertex.x, vertex.z)
            uv_layer.data[loop_index].uv = (uv[0] * scale, uv[1] * scale)


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
    apply_box_uvs(
        mesh,
        UV_TILE_METERS.get(material_slot, UV_TILE_METERS_DEFAULT),
    )
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    obj.data.materials.append(material)
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
        self.shell_boxes: tuple[
            tuple[tuple[float, float, float], tuple[float, float, float]], ...
        ] = ()
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
        self._build_damage()
        self._build_doors()
        self._build_wheels()
        self._build_lights_and_trim()
        self._build_interior()
        self._build_anchors()
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
            self.shell_boxes,
        )

    # ------------------------------------------------------------------
    # Geometry
    # ------------------------------------------------------------------

    def _build_shell(self) -> None:
        """The three-box saloon: rocker, body sides, bonnet and boot.

        Authored as separate slabs rather than one hull so the waistline is a
        real edge - it is the rail the Ferryman rests his forearm on, and the
        line the rust follows.
        """
        # Three cuts, all stated by what is left out, because a box cannot
        # be hollowed. Vertically the flank exists only between the arches
        # and at the overhangs, so the wheels stand in open arches instead of
        # behind a wall. Laterally the cabin is drawn as two side panels
        # rather than a slab, so the floor, the seats and the wheel are
        # inside a room instead of buried in solid bodywork - the hero will
        # be sitting in there looking out.
        #
        # And the third: each cabin panel STOPS at the doorway and starts
        # again behind it. For a whole build it did not, and the room was
        # sealed - the leaf swung open on thirteen centimetres of bodywork
        # filling the entire opening. The gap between these boxes IS the
        # door, exactly as the gaps in the bus's passenger flank are its
        # doors. The 0.13 m panel gives the cut edge a real jamb for free.
        midsection = ARCH_HALF_LENGTH  # half-length of the belt between arches
        overhang_length = REAR_Y - (REAR_AXLE_Y + ARCH_HALF_LENGTH)
        overhang_y = REAR_AXLE_Y + ARCH_HALF_LENGTH + overhang_length * 0.5
        lower_mid_z = (SILL_Z + WHEEL_TOP_Z) * 0.5
        upper_mid_z = (WHEEL_TOP_Z + WAIST_Z) * 0.5
        panel_thickness = 0.13
        panel_x = BODY_HALF_WIDTH - panel_thickness * 0.5
        bay_length = CABIN_FRONT_Y - FRONT_Y
        bay_y = (FRONT_Y + CABIN_FRONT_Y) * 0.5
        boot_length = REAR_Y - CABIN_REAR_Y
        boot_y = (CABIN_REAR_Y + REAR_Y) * 0.5
        # What is left of each cabin panel once the doorway is taken out of
        # it: a sliver of A-pillar root ahead of the opening, and the rear
        # door's own flank behind it. The rear doors never open, so that
        # half stays solid.
        jamb_front_length = DOORWAY_FRONT_Y - CABIN_FRONT_Y
        jamb_front_y = (CABIN_FRONT_Y + DOORWAY_FRONT_Y) * 0.5
        jamb_rear_length = CABIN_REAR_Y - DOORWAY_REAR_Y
        jamb_rear_y = (DOORWAY_REAR_Y + CABIN_REAR_Y) * 0.5
        # The lower belt only ever reached the arches, so its share of the
        # doorway is just the stretch behind it.
        lower_rear_length = midsection - DOORWAY_REAR_Y
        lower_rear_y = (DOORWAY_REAR_Y + midsection) * 0.5
        # The rocker, which used to be a slab of bodywork right across the
        # cabin from the floor line up to the sill. That slab was the thing
        # a sitter sank into: the drawn floor tops out at FLOOR_Z + 0.025
        # and the slab's face was 0.14 m ABOVE it, so a seated man's boots
        # went through it and the manifest's own `cabin_floor_drop_m` was
        # fiction. It is now what it is called - two rockers under the two
        # door openings, stopping inboard at the seat cushion.
        sill_outer_x = BODY_HALF_WIDTH - 0.02
        sill_inner_x = SEAT_HALF_SPAN_X + SEAT_CUSHION_WIDTH * 0.5
        sill_width = sill_outer_x - sill_inner_x
        sill_x = (sill_inner_x + sill_outer_x) * 0.5
        sill_length = midsection - ARCH_REAR_Y
        sill_y = (ARCH_REAR_Y + midsection) * 0.5
        sill_bottom_z = FLOOR_Z - 0.04
        front_lip_length = DOORWAY_FRONT_Y - (FRONT_AXLE_Y - ARCH_HALF_LENGTH)
        front_lip_y = (FRONT_AXLE_Y - ARCH_HALF_LENGTH) + front_lip_length * 0.5
        shell_boxes = (
            # Rockers under each doorway, and nothing between them.
            ((sill_x, sill_y, (sill_bottom_z + SILL_Z) * 0.5),
             (sill_width, sill_length, SILL_Z - sill_bottom_z)),
            ((-sill_x, sill_y, (sill_bottom_z + SILL_Z) * 0.5),
             (sill_width, sill_length, SILL_Z - sill_bottom_z)),
            # Lower flank between the arches, behind the doorway.
            ((panel_x, lower_rear_y, lower_mid_z),
             (panel_thickness, lower_rear_length, WHEEL_TOP_Z - SILL_Z)),
            ((-panel_x, lower_rear_y, lower_mid_z),
             (panel_thickness, lower_rear_length, WHEEL_TOP_Z - SILL_Z)),
            # Lower flank ahead of and behind the arches.
            ((0.0, -overhang_y, lower_mid_z),
             (WIDTH, overhang_length, WHEEL_TOP_Z - SILL_Z)),
            ((0.0, overhang_y, lower_mid_z),
             (WIDTH, overhang_length, WHEEL_TOP_Z - SILL_Z)),
            # Upper flank: solid over the engine bay and the boot,
            # side panels along the cabin - each in two pieces with the
            # door opening between them.
            ((0.0, bay_y, upper_mid_z),
             (WIDTH, bay_length, WAIST_Z - WHEEL_TOP_Z)),
            ((0.0, boot_y, upper_mid_z),
             (WIDTH, boot_length, WAIST_Z - WHEEL_TOP_Z)),
            ((panel_x, jamb_front_y, upper_mid_z),
             (panel_thickness, jamb_front_length, WAIST_Z - WHEEL_TOP_Z)),
            ((-panel_x, jamb_front_y, upper_mid_z),
             (panel_thickness, jamb_front_length, WAIST_Z - WHEEL_TOP_Z)),
            ((panel_x, jamb_rear_y, upper_mid_z),
             (panel_thickness, jamb_rear_length, WAIST_Z - WHEEL_TOP_Z)),
            ((-panel_x, jamb_rear_y, upper_mid_z),
             (panel_thickness, jamb_rear_length, WAIST_Z - WHEEL_TOP_Z)),
            # Bonnet and boot lid.
            ((0.0, -1.78, (WAIST_Z + BONNET_Z) * 0.5),
             (WIDTH - 0.14, 1.05, BONNET_Z - WAIST_Z)),
            ((0.0, 1.86, (WAIST_Z + BONNET_Z) * 0.5),
             (WIDTH - 0.14, 0.98, BONNET_Z - WAIST_Z)),
            # Nose and tail panels.
            ((0.0, FRONT_Y + 0.06, upper_mid_z),
             (WIDTH - 0.10, 0.16, WAIST_Z - WHEEL_TOP_Z)),
            ((0.0, REAR_Y - 0.06, upper_mid_z),
             (WIDTH - 0.10, 0.16, WAIST_Z - WHEEL_TOP_Z)),
            # Arch lips over each wheel. The FRONT pair stops at the door
            # shut line instead of running the full arch, which is both
            # what a saloon does and what keeps an eyebrow of bodywork from
            # crossing the open doorway at chest height.
            ((TRACK_HALF + 0.10, front_lip_y, WHEEL_TOP_Z + 0.04),
             (0.11, front_lip_length, 0.08)),
            ((-(TRACK_HALF + 0.10), front_lip_y, WHEEL_TOP_Z + 0.04),
             (0.11, front_lip_length, 0.08)),
            ((TRACK_HALF + 0.10, REAR_AXLE_Y, WHEEL_TOP_Z + 0.04),
             (0.11, ARCH_HALF_LENGTH * 2.0, 0.08)),
            ((-(TRACK_HALF + 0.10), REAR_AXLE_Y, WHEEL_TOP_Z + 0.04),
             (0.11, ARCH_HALF_LENGTH * 2.0, 0.08)),
            # Aprons closing the overhangs under the bumpers.
            ((0.0, FRONT_Y + 0.10, 0.40), (WIDTH - 0.16, 0.32, 0.20)),
            ((0.0, REAR_Y - 0.10, 0.40), (WIDTH - 0.16, 0.32, 0.20)),
        )
        self.shell_boxes = shell_boxes
        self.add_boxes(
            "GEO_BodyShell",
            shell_boxes,
            "body_shell",
            "Body",
        )

    def _build_greenhouse(self) -> None:
        """Roof, headers and six thin pillars.

        The pillars are cylinders rather than boxes because they rake, and a
        period saloon reads by how thin they are.
        """
        self.add_boxes(
            "GEO_Greenhouse",
            (
                ((0.0, 0.21, (ROOF_UNDERSIDE_Z + HEIGHT) * 0.5),
                 (1.72, 2.22, HEIGHT - ROOF_UNDERSIDE_Z)),
                ((0.0, -0.88, ROOF_UNDERSIDE_Z - 0.03), (1.66, 0.14, 0.10)),
                ((0.0, 1.28, ROOF_UNDERSIDE_Z - 0.03), (1.66, 0.14, 0.10)),
                ((0.0, 1.34, WAIST_Z + 0.04), (1.46, 0.50, 0.05)),
                ((0.0, -1.24, WAIST_Z + 0.03), (WIDTH - 0.18, 0.14, 0.07)),
            ),
            "greenhouse",
            "Body",
        )

        pillars = MeshAccumulator()
        for side in (1.0, -1.0):
            # A-pillar: from the scuttle up to the windscreen header, so the
            # screen has something to sit in and the roof has something to
            # stand on. Raked, which is why these are cylinders and not boxes.
            pillars.add_cylinder_between(
                (side * 0.84, -1.17, WAIST_Z),
                (side * 0.80, -0.88, ROOF_UNDERSIDE_Z),
                0.048,
                segments=6,
            )
            pillars.add_cylinder_between(
                (side * 0.84, 0.34, WAIST_Z),
                (side * 0.82, 0.34, ROOF_UNDERSIDE_Z),
                0.044,
                segments=6,
            )
            pillars.add_cylinder_between(
                (side * 0.84, 1.02, WAIST_Z),
                (side * 0.80, 1.28, ROOF_UNDERSIDE_Z),
                0.052,
                segments=6,
            )
        self.add_accumulator("GEO_Pillars", pillars, "pillars", "Body")

    def _build_glass(self) -> None:
        """Screens and side lights as double-sided quads.

        Double-sided on purpose: the hero will sit behind this glass, and a
        single winding would leave him looking out through nothing.
        """
        glass = MeshAccumulator()
        # Windscreen, raked back from the scuttle to the header.
        glass.add_double_quad(
            (0.82, -1.17, WAIST_Z + 0.04),
            (-0.82, -1.17, WAIST_Z + 0.04),
            (-0.78, -0.89, ROOF_UNDERSIDE_Z - 0.03),
            (0.78, -0.89, ROOF_UNDERSIDE_Z - 0.03),
        )
        # Rear screen, raked the other way.
        glass.add_double_quad(
            (0.80, 1.36, WAIST_Z + 0.04),
            (-0.80, 1.36, WAIST_Z + 0.04),
            (-0.78, 1.27, ROOF_UNDERSIDE_Z - 0.03),
            (0.78, 1.27, ROOF_UNDERSIDE_Z - 0.03),
        )
        # Rear side lights, between the B and C pillars.
        for side in (1.0, -1.0):
            glass.add_double_quad(
                (side * 0.84, 0.39, WAIST_Z + 0.03),
                (side * 0.84, 0.99, WAIST_Z + 0.03),
                (side * 0.82, 1.03, ROOF_UNDERSIDE_Z - 0.03),
                (side * 0.82, 0.39, ROOF_UNDERSIDE_Z - 0.03),
            )
        self.add_accumulator("GEO_Glass", glass, "glass", "Glass")

        # The driver's rear quarter light took a stone years ago. The crack
        # lines are separate slivers on the windscreen so the damage is a
        # silhouette read at 640x360, not a texture nobody will resolve.
        cracked = MeshAccumulator()
        cracked.add_double_quad(
            (0.845, 1.06, WAIST_Z + 0.04),
            (0.825, 1.27, WAIST_Z + 0.04),
            (0.815, 1.27, ROOF_UNDERSIDE_Z - 0.04),
            (0.835, 1.06, ROOF_UNDERSIDE_Z - 0.04),
        )
        for offset, span, height in (
            (-0.10, 0.30, 0.020),
            (0.06, 0.26, 0.016),
            (0.20, 0.22, 0.014),
            (-0.26, 0.18, 0.014),
            (0.34, 0.16, 0.012),
        ):
            cracked.add_box(
                (offset, -1.02, WAIST_Z + 0.24 + span * 0.35),
                (span, 0.02, height),
            )
        self.add_accumulator(
            "GEO_CrackedGlass", cracked, "cracked_glass", "CrackedGlass"
        )

    def _build_damage(self) -> None:
        """Dents pressed in, rust standing proud.

        Both are separate meshes with their own roles so the generator can
        assert the wreck is still a wreck after a later styling pass.
        """
        self.add_boxes(
            "GEO_Dents",
            DENT_SPECS,
            "dents",
            "Body",
        )
        self.add_boxes(
            "GEO_Rust",
            RUST_SPECS,
            "rust",
            "Rust",
        )

    def _build_doors(self) -> None:
        """Two front doors, each on its own hinge pivot.

        Authored in the pivot's space so "he opens the door" is later a
        rotation rather than a re-author. The passenger door is the one in the
        wrong colour.
        """
        for name, side, role, slot in DOOR_SPECS:
            hinge = self.add_pivot(
                f"PIVOT_Door{name}",
                role,
                self.body,
                (side * (BODY_HALF_WIDTH - 0.005), DOOR_HINGE_Y, DOOR_HINGE_Z),
                runtime_axis_local="+Z",
            )
            leaf_y = DOOR_LEAF_LENGTH * 0.5
            self.add_boxes(
                f"GEO_Door{name}Panel",
                (
                    ((side * 0.010, leaf_y, 0.0),
                     (0.050, DOOR_LEAF_LENGTH, 0.58)),
                    ((side * 0.016, leaf_y, 0.30),
                     (0.042, DOOR_LEAF_LENGTH, 0.035)),
                ),
                "door_panel",
                slot,
                parent=hinge,
            )
            self.add_boxes(
                f"GEO_Door{name}Glass",
                (((side * 0.006, 0.77, 0.54), (0.030, 1.40, 0.50)),),
                "door_glass",
                "Glass",
                parent=hinge,
            )
            # The handle rides the leaf. It used to be drawn into the
            # flank's trim mesh, which was harmless for exactly as long as
            # the door never moved: the moment it opens, a handle left on
            # the body hangs in the empty doorway with nothing behind it.
            # Same box, same place, expressed in the hinge's own space.
            self.add_boxes(
                f"GEO_Door{name}Handle",
                (
                    (
                        (side * 0.019, DOOR_HANDLE_Y, DOOR_HANDLE_Z),
                        (0.030, 0.15, 0.035),
                    ),
                ),
                "door_handle",
                "Chrome",
                parent=hinge,
            )

            # And so does the damage, for exactly the reason above. The sill
            # rust and the driver's crease were authored against the flank
            # at +/-0.905 and then a leaf was hung over them at 0.880-0.930,
            # which buried both; cutting the doorway would have gone one
            # worse and left them floating in the hole. Same runs, same
            # heights, re-expressed on the leaf and standing proud of it.
            self.add_boxes(
                f"GEO_Door{name}Rust",
                _onto_door_leaf(
                    DOOR_RUST_SPECS[name], side, DOOR_RUST_PROUD_X),
                "rust",
                "Rust",
                parent=hinge,
            )
            dents = DOOR_DENT_SPECS.get(name)
            if dents:
                self.add_boxes(
                    f"GEO_Door{name}Dent",
                    _onto_door_leaf(dents, side, DOOR_DENT_PROUD_X),
                    "dents",
                    slot,
                    parent=hinge,
                )

    def _build_wheels(self) -> None:
        """All four wheels are on. Three of the four hubcaps are not."""
        wheel_specs = (
            ("FL", "front_left_wheel", TRACK_HALF, FRONT_AXLE_Y, True),
            ("FR", "front_right_wheel", -TRACK_HALF, FRONT_AXLE_Y, False),
            ("RL", "rear_left_wheel", TRACK_HALF, REAR_AXLE_Y, True),
            ("RR", "rear_right_wheel", -TRACK_HALF, REAR_AXLE_Y, True),
        )
        for code, role, x, y, has_hubcap in wheel_specs:
            pivot = self.add_pivot(
                f"PIVOT_Wheel{code}",
                role,
                self.body,
                (x, y, WHEEL_RADIUS),
                runtime_axis_local="+X",
            )
            tyre = MeshAccumulator()
            tyre.add_cylinder_x((0.0, 0.0, 0.0), WHEEL_RADIUS, WHEEL_WIDTH, 12)
            self.add_accumulator(
                f"GEO_Wheel{code}", tyre, "wheel", "Rubber", parent=pivot
            )
            if not has_hubcap:
                continue
            cap = MeshAccumulator()
            outward = math.copysign(WHEEL_WIDTH * 0.5 + 0.012, x)
            cap.add_cylinder_x((outward, 0.0, 0.0), 0.125, 0.024, 12)
            self.add_accumulator(
                f"GEO_Hubcap{code}", cap, "hubcap", "Chrome", parent=pivot
            )

    def _build_lights_and_trim(self) -> None:
        """Two burning headlights, and the small metal around them.

        Everything else on this car is broken; the lamps are not. They are
        the only working thing on an abandoned lot, and at 640x360 through
        fog they are what reads first - a pair of lit lenses says someone is
        waiting in a way a parked shape never does. The lens is proud of its
        own rim so the emissive face is never coplanar with the bodywork.
        """
        headlight = MeshAccumulator()
        rims = MeshAccumulator()
        for lamp_x in (0.62, -0.62):
            headlight.add_cylinder_x(
                (lamp_x, FRONT_Y - 0.01, 0.86), 0.105, 0.14, 12
            )
            rims.add_cylinder_x(
                (lamp_x, FRONT_Y + 0.04, 0.86), 0.125, 0.06, 12
            )
        self.add_accumulator(
            "GEO_Headlight", headlight, "headlight", "Headlight"
        )
        self.add_accumulator(
            "GEO_HeadlightRims", rims, "headlight_rim", "Chrome"
        )

        self.add_boxes(
            "GEO_TailLights",
            (
                ((0.60, REAR_Y - 0.02, 0.88), (0.22, 0.06, 0.14)),
                ((-0.60, REAR_Y - 0.02, 0.88), (0.22, 0.06, 0.14)),
            ),
            "tail_light",
            "TailLight",
        )

        grille = MeshAccumulator()
        grille.add_box((0.0, FRONT_Y + 0.01, 0.80), (0.94, 0.05, 0.30))
        for index in range(5):
            grille.add_box(
                (0.0, FRONT_Y - 0.02, 0.68 + index * 0.062),
                (0.90, 0.03, 0.022),
            )
        self.add_accumulator("GEO_Grille", grille, "grille", "Chrome")

        # The front bumper's outer section was hit and hangs low; the rear one
        # is straight. A box cannot yaw, so the droop is stated by offset.
        self.add_boxes(
            "GEO_Bumpers",
            (
                ((0.18, FRONT_Y - 0.06, 0.52), (1.12, 0.10, 0.11)),
                ((-0.62, FRONT_Y - 0.02, 0.44), (0.52, 0.09, 0.10)),
                ((0.80, FRONT_Y - 0.05, 0.52), (0.28, 0.09, 0.10)),
                ((0.0, REAR_Y + 0.05, 0.54), (1.58, 0.10, 0.11)),
                ((0.0, REAR_Y + 0.03, 0.44), (1.20, 0.07, 0.06)),
            ),
            "bumper",
            "Chrome",
        )

        trim = MeshAccumulator()
        for side in (1.0, -1.0):
            # Waist strip - the line his forearm follows.
            trim.add_box(
                (side * (BODY_HALF_WIDTH + 0.006), 0.10, WAIST_Z - 0.03),
                (0.016, 3.60, 0.030),
            )
            # Drip rail.
            trim.add_box(
                (side * 0.74, 0.18, ROOF_UNDERSIDE_Z + 0.01),
                (0.030, 2.00, 0.026),
            )
            # Rear door shut lines. The rear doors never open, so they are
            # seams rather than leaves: the flank still reads as a saloon.
            for seam_y in (0.34, 1.02):
                trim.add_box(
                    (side * (BODY_HALF_WIDTH + 0.004), seam_y, 0.73),
                    (0.012, 0.020, 0.56),
                )
            # The REAR door handle only. The front one is not trim: it is
            # the thing the Ferryman pulls, so it belongs to the leaf that
            # swings and is built with it in `_build_doors`. Left in this
            # mesh it stayed bolted to the flank while the door opened -
            # a chrome bar hanging in an empty doorway - which nobody saw
            # for as long as nothing ever opened the door.
            trim.add_box(
                (side * (BODY_HALF_WIDTH + 0.014), 0.62, WAIST_Z - 0.11),
                (0.030, 0.15, 0.035),
            )
        # Wing mirror on the driver's side only, and two wipers parked low.
        trim.add_cylinder_between(
            (0.86, -1.05, WAIST_Z + 0.02),
            (1.00, -1.12, WAIST_Z + 0.12),
            0.014,
            segments=6,
        )
        trim.add_box((1.02, -1.13, WAIST_Z + 0.15), (0.035, 0.10, 0.09))
        for wiper_x in (0.34, -0.34):
            trim.add_box(
                (wiper_x, -1.14, WAIST_Z + 0.075), (0.44, 0.020, 0.014)
            )
        # The aerial snapped off years ago and never got replaced. Keeping the
        # stub under the roofline is also what lets the height bound stay a
        # tight contract instead of a tolerance widened for one whisker.
        trim.add_cylinder_between(
            (-0.80, -0.70, WAIST_Z + 0.02),
            (-0.84, -0.64, WAIST_Z + 0.34),
            0.008,
            segments=4,
        )
        self.add_accumulator("GEO_Trim", trim, "exterior_trim", "Trim")

        exhaust = MeshAccumulator()
        exhaust.add_cylinder_between(
            (-0.30, 1.10, 0.24), (-0.44, REAR_Y + 0.06, 0.20), 0.032, segments=6
        )
        exhaust.add_box((-0.37, 1.60, 0.30), (0.012, 0.012, 0.14))
        self.add_accumulator("GEO_Exhaust", exhaust, "exhaust", "Metal")

        # The front plate lost a fixing and hangs off one corner.
        self.add_boxes(
            "GEO_NumberPlate",
            (
                ((0.10, FRONT_Y - 0.07, 0.62), (0.44, 0.02, 0.11)),
                ((0.30, FRONT_Y - 0.06, 0.68), (0.03, 0.02, 0.05)),
            ),
            "number_plate",
            "Plate",
        )

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
        self.add_boxes(
            "GEO_SeatDriver",
            (
                ((SEAT_HALF_SPAN_X, SEAT_Y, 0.46), (0.52, 0.50, 0.12)),
                ((SEAT_HALF_SPAN_X, SEAT_Y + 0.30, 0.85), (0.52, 0.11, 0.66)),
                ((SEAT_HALF_SPAN_X, SEAT_Y + 0.28, 1.24), (0.30, 0.10, 0.16)),
            ),
            "driver_seat",
            "Seat",
        )
        self.add_boxes(
            "GEO_SeatPassenger",
            (
                ((-SEAT_HALF_SPAN_X, SEAT_Y, 0.46), (0.52, 0.50, 0.12)),
                ((-SEAT_HALF_SPAN_X, SEAT_Y + 0.30, 0.85), (0.52, 0.11, 0.66)),
                ((-SEAT_HALF_SPAN_X, SEAT_Y + 0.28, 1.24), (0.30, 0.10, 0.16)),
            ),
            "passenger_seat",
            "Seat",
        )
        self.add_boxes(
            "GEO_SeatRear",
            (
                ((0.0, 0.55, 0.48), (1.30, 0.52, 0.12)),
                ((0.0, 0.85, 0.84), (1.30, 0.11, 0.60)),
            ),
            "rear_bench",
            "Seat",
        )
        self.add_boxes(
            "GEO_Dashboard",
            (
                ((0.0, -1.05, 0.94), (1.46, 0.24, 0.20)),
                ((0.44, -1.06, 1.02), (0.34, 0.10, 0.12)),
            ),
            "dashboard",
            "Dashboard",
        )

        column = MeshAccumulator()
        column.add_cylinder_between(
            (0.44, -1.02, 0.80),
            STEERING_WHEEL_CENTER,
            0.026,
            segments=6,
        )
        self.add_accumulator(
            "GEO_SteeringColumn", column, "steering_column", "Metal"
        )
        self.add_boxes(
            "GEO_InteriorMirror",
            (((0.0, -0.88, ROOF_UNDERSIDE_Z - 0.09), (0.20, 0.03, 0.06)),),
            "interior_mirror",
            "Trim",
        )

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
            12,
            4,
        )
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
            "INT_SteeringWheel", rim, "steering_wheel", "Trim", parent=steering
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
    camera.location = (-5.6, -6.9, 2.9)
    target = Vector((-0.10, -0.35, 0.80))
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
    ground_material.diffuse_color = (0.022, 0.034, 0.030, 1.0)
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
    blender_separator = sys.argv.index("--") + 1 if "--" in sys.argv else len(sys.argv)
    args = parser.parse_args(sys.argv[blender_separator:])
    for field_name in ("output", "fbx", "manifest", "preview"):
        value = getattr(args, field_name)
        setattr(args, field_name, value.resolve())
    return args


REQUIRED_MESH_ROLES = (
    "body_shell", "greenhouse", "pillars", "glass", "cracked_glass",
    "dents", "rust", "door_panel", "door_glass", "door_handle", "wheel",
    "hubcap",
    "headlight", "headlight_rim", "tail_light", "grille", "bumper",
    "exterior_trim", "exhaust", "number_plate", "cabin_floor",
    "interior_shell",
    "driver_seat", "passenger_seat", "rear_bench", "dashboard",
    "steering_column", "steering_wheel", "interior_mirror",
)

REQUIRED_PIVOT_ROLES = (
    "front_left_wheel", "front_right_wheel",
    "rear_left_wheel", "rear_right_wheel",
    "driver_door_leaf", "passenger_door_leaf",
    "steering_wheel", "left_steering_grip", "right_steering_grip",
    "driver_seat_anchor", "passenger_seat_anchor",
    "driver_door_entry", "passenger_door_entry",
    "perch_soles", "perch_seat",
)

# The damage the design is FOR. Asserting the counts is what stops a later
# styling pass from quietly repairing the car.
DAMAGE_FEATURES = (
    "dents", "rust", "cracked_glass", "mismatched_door", "missing_hubcap",
)


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
    if roles.count("headlight") != 1:
        errors.append("The two headlights share one lens mesh")
    if "broken_headlight" in role_set:
        errors.append(
            "Both headlights work now; a broken lens contradicts the design"
        )
    # Both front doorways have to be HOLES. This design shipped with the
    # cabin flank drawn as one unbroken panel from the scuttle to the parcel
    # shelf, so opening a leaf revealed 0.13 m of bodywork filling the whole
    # opening and the "visible interior" the manifest advertises was a room
    # with no way into it. A hole in a merged mesh cannot be asked about
    # afterwards, so the shell's boxes are checked as authored.
    doorway = (
        (DOORWAY_FRONT_Y, DOORWAY_REAR_Y),
        (SILL_Z, WAIST_Z),
    )
    for side, name in ((1.0, "driver"), (-1.0, "passenger")):
        near_x = min(side * SEAT_HALF_SPAN_X + side * SEAT_CUSHION_WIDTH * 0.5,
                     side * BODY_HALF_WIDTH)
        far_x = max(side * SEAT_HALF_SPAN_X + side * SEAT_CUSHION_WIDTH * 0.5,
                    side * BODY_HALF_WIDTH)
        for center, size in result.shell_boxes:
            spans = (
                (center[0] - size[0] * 0.5, center[0] + size[0] * 0.5),
                (center[1] - size[1] * 0.5, center[1] + size[1] * 0.5),
                (center[2] - size[2] * 0.5, center[2] + size[2] * 0.5),
            )
            limits = ((near_x, far_x), doorway[0], doorway[1])
            if all(
                min(span[1], limit[1]) - max(span[0], limit[0]) > 1e-4
                for span, limit in zip(spans, limits)
            ):
                errors.append(
                    f"Body shell box at {center} blocks the {name} doorway"
                )

    # And the room itself has to stay empty. `cabin_floor_drop_m` promises a
    # seated pelvis 0.22 m of leg down to FLOOR_Z, and the Ferryman's own
    # archetype is validated against that number - but for a whole build the
    # rocker was a slab of bodywork right across the cabin whose face sat
    # 0.14 m ABOVE the drawn floor, so both sitters went through it to the
    # knee and the promise was fiction. Nothing structural belongs between
    # the rockers, above the floor and below the waist.
    cabin_clear = (
        (-(SEAT_HALF_SPAN_X + SEAT_CUSHION_WIDTH * 0.5),
         SEAT_HALF_SPAN_X + SEAT_CUSHION_WIDTH * 0.5),
        (CABIN_FRONT_Y, CABIN_REAR_Y),
        (FLOOR_Z + 0.03, WAIST_Z),
    )
    for center, size in result.shell_boxes:
        spans = (
            (center[0] - size[0] * 0.5, center[0] + size[0] * 0.5),
            (center[1] - size[1] * 0.5, center[1] + size[1] * 0.5),
            (center[2] - size[2] * 0.5, center[2] + size[2] * 0.5),
        )
        if all(
            min(span[1], limit[1]) - max(span[0], limit[0]) > 1e-4
            for span, limit in zip(spans, cabin_clear)
        ):
            errors.append(
                f"Body shell box at {center} stands inside the cabin, "
                "where the seats and the sitters are"
            )

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
        if abs(perch_seat.obj.location.z - BONNET_Z) > 1e-6:
            errors.append("The perch seat must rest on the bonnet skin")
        if not (FRONT_Y < perch_seat.obj.location.y < CABIN_FRONT_Y):
            errors.append("The perch seat must sit over the bonnet, not the roof")
    if perch_soles is not None and perch_seat is not None:
        drop = perch_seat.obj.location.z - perch_soles.obj.location.z
        if abs(drop - PERCH_DROP_M) > 1e-6:
            errors.append("The perch drop drifted from the shared contract")
        if perch_soles.obj.location.y >= perch_seat.obj.location.y:
            errors.append("His boots must be ahead of his seat, on the bumper")

    # Headroom is validated against the HERO's seated band, not the
    # Ferryman's: the hero reuses the bus clips verbatim, so his pose is the
    # one that actually has to fit under this roof.
    headroom = ROOF_UNDERSIDE_Z - SEAT_PELVIS_Z
    if headroom < 0.99 or headroom > 1.10:
        errors.append(
            f"Seated headroom {headroom:.3f}m is outside the shared rig's 0.99-1.10m band"
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
        for required in ("door_panel", "door_glass", "door_handle"):
            if child_roles.count(required) != 1:
                errors.append(
                    f"Door leaf pivot {name} must own exactly one "
                    f"{required} - it is what swings"
                )
        # Anything ELSE the leaf owns has to be damage. The rule used to be
        # an exact list of three, which was right until the flank's dents
        # and rust had to move onto the leaves for the same reason the
        # handle did: a doorway is a hole now, and a mark drawn on the body
        # inside that hole hangs in mid-air the moment the door opens.
        for role in child_roles:
            if role not in (
                "door_panel", "door_glass", "door_handle", "dents", "rust"
            ):
                errors.append(
                    f"Door leaf pivot {name} owns an unexpected {role}; "
                    "only the leaf itself and its damage may swing"
                )

    bounds_min, bounds_max = mesh_world_bounds(part.obj for part in result.parts)
    size = bounds_max - bounds_min
    if abs(bounds_min.z) > 0.005 or abs(bounds_max.z - HEIGHT) > 0.015:
        errors.append(
            f"Ground/height bounds are {bounds_min.z:.3f}..{bounds_max.z:.3f}, expected 0..{HEIGHT}"
        )
    # The upper tolerance is the bumpers, which overhang the body on purpose.
    if size.y < LENGTH - 0.08 or size.y > LENGTH + 0.30:
        errors.append(f"Visual length {size.y:.3f}m differs from {LENGTH:.2f}m")
    if size.x < WIDTH or size.x > WIDTH + 0.30:
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
