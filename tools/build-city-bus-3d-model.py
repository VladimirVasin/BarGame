#!/usr/bin/env python3
"""Build the production low-poly Road v2 City midibus.

Run through Blender 5, for example::

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-city-bus-3d-model.py

The deterministic output is a meter-scale editable .blend, a hierarchy-
preserving FBX, a JSON contract consumed by Unity's prefab builder and a
review render. Blender source space is Z-up with vehicle forward along -Y.
The Unity prefab builder rotates the imported model 180 degrees so its local
runtime forward is +Z.
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


GENERATOR_VERSION = "1.4.0"
DESIGN_ID = "road_v2_midibus_v1"
DISPLAY_NAME = "Road v2 City Midibus"
SEED = 260811

LENGTH = 8.25
WIDTH = 2.38
HEIGHT = 2.95
WHEELBASE = 4.50
WHEEL_RADIUS = 0.43
WHEEL_WIDTH = 0.26
MIN_TRIANGLES = 900
MAX_TRIANGLES = 12000

SOURCE_COLLECTION = "SOURCE_CityBus3D"
PRESENTATION_COLLECTION = "PRESENTATION_CityBus3D"
ROOT_NAME = "SRC_CityBus3D"
BODY_NAME = "ROOT_Body"

DOOR_SIDE_X = -1.205
DOOR_CENTER_Z = 1.58
DOOR_HINGE_OFFSET = 0.76
DOORWAY_SPECS = (
    (
        "Front",
        -3.05,
        (
            ("Forward", -1.0, "front_door_forward_leaf"),
            ("Rearward", 1.0, "front_door_rearward_leaf"),
        ),
    ),
    (
        "Rear",
        1.34,
        (
            ("Forward", -1.0, "rear_door_forward_leaf"),
            ("Rearward", 1.0, "rear_door_rearward_leaf"),
        ),
    ),
)

STEERING_WHEEL_CENTER = (0.60, -3.32, 1.57)
STEERING_WHEEL_MAJOR_RADIUS = 0.18
STEERING_WHEEL_MINOR_RADIUS = 0.022
STEERING_GRIP_X = 0.14
STEERING_GRIP_TOP = math.sqrt(
    STEERING_WHEEL_MAJOR_RADIUS ** 2 - STEERING_GRIP_X ** 2
)
STEERING_GRIP_AXIS_OFFSET = 0.020
DOOR_BUTTON_CENTER = (0.30, -3.335, 1.50)
DOOR_BUTTON_DEPTH = 0.045
DOOR_BUTTON_TRAVEL = 0.012
DRIVER_DOOR_LOOK_POSITION = (-0.90, -3.05, 2.12)

# Each wiper mesh is authored relative to its own base pivot so the runtime
# can sweep it across the windshield around the pivot's forward axis
# (source +Y). Rest geometry matches the pre-1.4.0 static wipers exactly.
WIPER_BASE_Y = -4.154
WIPER_BASE_Z = 1.65
WIPER_BASE_X = 0.62
WIPER_TIP_REACH = 0.52
WIPER_TIP_RISE = 0.60
WIPER_SPECS = (
    ("L", "left_wiper", -WIPER_BASE_X, WIPER_TIP_REACH),
    ("R", "right_wiper", WIPER_BASE_X, -WIPER_TIP_REACH),
)

# The two pendant cabin lamps hang on the aisle centreline at the exact
# source-Y positions mirrored by the runtime cabin Spots in
# CityBusPresentation (presentation +Z = source -Y, so -1.45 is "Front").
CABIN_LAMP_POSITIONS_Y = (-1.45, 1.45)
CABIN_LAMP_CEILING_Z = 2.72
CABIN_LAMP_STEM_RADIUS = 0.016
CABIN_LAMP_BULB_TOP_Z = 2.66
CABIN_LAMP_BULB_BOTTOM_Z = 2.56
CABIN_LAMP_BULB_RADIUS = 0.07

# Box-projected UV density per material slot, in metres per texture tile.
# Meshes carry world-scale UVs so Unity materials tile at (1, 1).
UV_TILE_METERS_DEFAULT = 1.25
UV_TILE_METERS = {
    "Body": 1.6,
    "Accent": 1.6,
    "Trim": 1.6,
    "Interior": 1.2,
    "Dashboard": 0.9,
    "Seat": 0.7,
    "Metal": 0.9,
    "Rail": 0.9,
    "Rubber": 0.55,
}


MATERIALS: dict[str, tuple[tuple[float, float, float, float], float, float, float]] = {
    # slot: (rgba, metallic, roughness, emission strength)
    "Body": ((0.27, 0.075, 0.085, 1.0), 0.05, 0.61, 0.0),
    "Accent": ((0.63, 0.36, 0.095, 1.0), 0.03, 0.58, 0.0),
    "Trim": ((0.035, 0.043, 0.042, 1.0), 0.05, 0.76, 0.0),
    "Rubber": ((0.015, 0.018, 0.017, 1.0), 0.0, 0.92, 0.0),
    "Metal": ((0.24, 0.27, 0.26, 1.0), 0.58, 0.35, 0.0),
    "Glass": ((0.075, 0.16, 0.17, 0.34), 0.04, 0.22, 0.0),
    "Interior": ((0.12, 0.13, 0.12, 1.0), 0.0, 0.73, 0.0),
    "Seat": ((0.07, 0.20, 0.17, 1.0), 0.0, 0.82, 0.0),
    "Rail": ((0.76, 0.46, 0.08, 1.0), 0.31, 0.31, 0.0),
    "Dashboard": ((0.055, 0.065, 0.062, 1.0), 0.0, 0.69, 0.0),
    "Headlight": ((0.82, 0.88, 0.70, 1.0), 0.05, 0.24, 4.5),
    "TailLight": ((0.58, 0.015, 0.008, 1.0), 0.0, 0.28, 3.2),
    "CabinLight": ((0.92, 0.58, 0.24, 1.0), 0.0, 0.36, 2.1),
    "Destination": ((0.96, 0.39, 0.045, 1.0), 0.0, 0.40, 3.6),
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

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate the production Road v2 City bus."
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("ArtSource/Vehicles/Blender/CityBus3D.blend"),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path("Assets/Vehicles/Models/CityBus3D.fbx"),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("Assets/Vehicles/Models/CityBus3D.json"),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path("ArtSource/Vehicles/Blender/CityBus3D.png"),
    )
    parser.add_argument("--no-preview", action="store_true")
    blender_separator = sys.argv.index("--") + 1 if "--" in sys.argv else len(sys.argv)
    args = parser.parse_args(sys.argv[blender_separator:])
    for field_name in ("output", "fbx", "manifest", "preview"):
        value = getattr(args, field_name)
        setattr(args, field_name, value.resolve())
    return args


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
        scene.world = bpy.data.worlds.new("World_CityBusPreview")
    scene.world.color = (0.018, 0.026, 0.024)

    source = bpy.data.collections.new(SOURCE_COLLECTION)
    scene.collection.children.link(source)
    presentation = bpy.data.collections.new(PRESENTATION_COLLECTION)
    scene.collection.children.link(presentation)
    return source, presentation


def create_material(name: str, spec: tuple) -> bpy.types.Material:
    rgba, metallic, roughness, emission_strength = spec
    material = bpy.data.materials.new(f"MAT_CityBus{name}")
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


class CityBusBuilder:
    def __init__(self) -> None:
        self.collection, self.presentation = reset_scene()
        self.materials = {
            slot: create_material(slot, spec)
            for slot, spec in MATERIALS.items()
        }
        self.parts: list[Part] = []
        self.pivots: list[Pivot] = []
        self.root = create_empty(ROOT_NAME, self.collection, None, display_size=0.35)
        self.body = create_empty(BODY_NAME, self.collection, self.root, display_size=0.30)
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
        self._build_glass()
        self._build_interior()
        self._build_doors()
        self._build_wheels()
        self._build_lights_and_details()
        self._build_anchors()
        self.root["bp_generator"] = "tools/build-city-bus-3d-model.py"
        self.root["bp_generator_version"] = GENERATOR_VERSION
        self.root["bp_design_id"] = DESIGN_ID
        self.root["bp_dimensions_m"] = f"{LENGTH:.2f}x{WIDTH:.2f}x{HEIGHT:.2f}"
        self.root["bp_forward_axis"] = "-Y"
        return BuildResult(
            self.root,
            self.body,
            tuple(sorted(self.parts, key=lambda part: part.obj.name)),
            tuple(sorted(self.pivots, key=lambda pivot: pivot.obj.name)),
            tuple(self.collection.objects),
        )

    def _build_shell(self) -> None:
        self.add_boxes(
            "GEO_BodyShell",
            [
                ((0.0, 0.0, 0.47), (2.30, 7.98, 0.24)),
                ((0.0, 0.0, 2.86), (2.30, 7.92, 0.18)),
                ((0.0, -4.045, 0.92), (2.28, 0.16, 0.92)),
                ((0.0, -4.045, 2.70), (2.28, 0.16, 0.42)),
                ((0.0, 4.045, 1.12), (2.28, 0.16, 1.32)),
                ((0.0, 4.045, 2.73), (2.28, 0.16, 0.36)),
                ((1.145, 0.0, 0.98), (0.10, 7.94, 0.94)),
                ((-1.145, -3.91, 0.98), (0.10, 0.16, 0.94)),
                ((-1.145, -0.84, 0.98), (0.10, 2.74, 0.94)),
                ((-1.145, 3.04, 0.98), (0.10, 2.00, 0.94)),
                ((1.145, 0.0, 2.66), (0.10, 7.90, 0.34)),
                ((-1.145, 0.0, 2.66), (0.10, 7.90, 0.34)),
            ],
            "body_shell",
            "Body",
        )
        self.add_boxes(
            "GEO_BodyAccent",
            [
                ((0.0, -4.137, 0.49), (2.38, 0.07, 0.20)),
                ((0.0, 4.137, 0.49), (2.38, 0.07, 0.20)),
                ((1.202, 0.0, 1.47), (0.055, 7.94, 0.18)),
                ((-1.202, -3.91, 1.47), (0.055, 0.16, 0.18)),
                ((-1.202, -0.84, 1.47), (0.055, 2.74, 0.18)),
                ((-1.202, 3.04, 1.47), (0.055, 2.00, 0.18)),
            ],
            "body_accent",
            "Accent",
        )
        pillar_boxes: list[tuple[Sequence[float], Sequence[float]]] = []
        for y in (-3.88, -3.00, -2.00, -1.00, 0.00, 1.00, 2.00, 3.00, 3.88):
            pillar_boxes.append(((1.175, y, 2.08), (0.11, 0.10, 1.12)))
        for y in (-3.88, -2.25, -1.15, 0.54, 2.12, 3.10, 3.88):
            pillar_boxes.append(((-1.175, y, 2.08), (0.11, 0.10, 1.12)))
        pillar_boxes.extend(
            [
                ((-1.05, -4.085, 2.08), (0.16, 0.10, 1.12)),
                ((1.05, -4.085, 2.08), (0.16, 0.10, 1.12)),
                ((-1.05, 4.085, 2.08), (0.16, 0.10, 1.12)),
                ((1.05, 4.085, 2.08), (0.16, 0.10, 1.12)),
            ]
        )
        self.add_boxes("GEO_WindowFrames", pillar_boxes, "window_frame", "Trim")
        self.add_boxes(
            "GEO_Underbody",
            [
                ((0.0, 0.25, 0.31), (1.64, 5.80, 0.20)),
                ((0.0, 3.15, 0.34), (1.18, 0.75, 0.22)),
                ((0.0, -3.18, 0.34), (1.10, 0.68, 0.20)),
            ],
            "underbody",
            "Trim",
        )

    def _build_glass(self) -> None:
        windows: list[tuple[Sequence[float], Sequence[float]]] = []
        for y in (-3.45, -2.50, -1.50, -0.50, 0.50, 1.50, 2.50, 3.45):
            windows.append(((1.199, y, 2.08), (0.025, 0.82, 0.92)))
        for y, length in ((-1.70, 0.90), (-0.65, 0.90), (3.02, 1.70)):
            windows.append(((-1.199, y, 2.08), (0.025, length, 0.92)))
        windows.extend(
            [
                ((0.0, -4.132, 2.08), (1.88, 0.025, 0.92)),
                ((0.0, 4.132, 2.14), (1.88, 0.025, 0.80)),
            ]
        )
        self.add_boxes("GLS_Windows", windows, "glass", "Glass")

    def _build_interior(self) -> None:
        self.add_boxes(
            "INT_InteriorSurfaces",
            [
                ((0.0, 0.0, 0.62), (2.08, 7.70, 0.10)),
                ((0.0, 0.0, 2.75), (2.06, 7.68, 0.06)),
                ((0.0, 3.76, 1.13), (2.05, 0.12, 0.95)),
                ((0.0, -3.70, 0.83), (2.05, 0.10, 0.40)),
                ((0.0, 3.35, 0.80), (2.04, 0.78, 0.34)),
            ],
            "interior_shell",
            "Interior",
        )

        seat_positions = [
            (0.66, -1.82), (0.66, -0.82), (0.66, 0.18),
            (0.66, 1.18), (0.66, 2.18), (0.66, 3.12),
            (-0.66, -0.72), (-0.66, 0.18),
            (-0.66, 2.52), (-0.66, 3.24),
            (-0.22, 3.48), (0.22, 3.48),
        ]
        seats = MeshAccumulator()
        frames = MeshAccumulator()
        for x, y in seat_positions:
            seats.add_box((x, y, 1.01), (0.52, 0.48, 0.14))
            seats.add_box((x, y + 0.20, 1.36), (0.52, 0.12, 0.62))
            frames.add_cylinder_between((x, y + 0.10, 0.67), (x, y + 0.10, 0.94), 0.028, 6)
            frames.add_cylinder_between((x, y - 0.10, 0.67), (x, y - 0.10, 0.94), 0.028, 6)
        self.add_accumulator("INT_PassengerSeats", seats, "passenger_seats", "Seat")
        self.add_accumulator("INT_SeatFrames", frames, "seat_frames", "Metal")

        self.add_boxes(
            "INT_DriverSeat",
            [
                ((0.68, -3.28, 1.00), (0.55, 0.50, 0.16)),
                ((0.68, -3.05, 1.40), (0.55, 0.15, 0.70)),
                ((0.68, -3.02, 1.78), (0.34, 0.14, 0.16)),
            ],
            "driver_seat",
            "Seat",
        )
        self.add_boxes(
            "INT_Dashboard",
            [
                ((0.0, -3.72, 1.22), (2.02, 0.48, 0.34)),
                ((0.57, -3.55, 1.43), (0.62, 0.36, 0.16)),
                ((-0.64, -3.48, 1.30), (0.36, 0.28, 0.55)),
                ((0.57, -3.37, 1.50), (0.28, 0.04, 0.13)),
            ],
            "dashboard",
            "Dashboard",
        )
        steering_column = MeshAccumulator()
        steering_column.add_cylinder_between(
            STEERING_WHEEL_CENTER,
            (0.60, -3.52, 1.36),
            0.035,
            8,
        )
        self.add_accumulator(
            "INT_SteeringColumn",
            steering_column,
            "steering_column",
            "Trim",
        )

        steering_pivot = self.add_pivot(
            "PIVOT_SteeringWheel",
            "steering_wheel",
            self.body,
            STEERING_WHEEL_CENTER,
            (-math.pi * 0.5, 0.0, 0.0),
            "+Z",
        )
        steering = MeshAccumulator()
        steering.add_torus_z(
            (0.0, 0.0, 0.0),
            STEERING_WHEEL_MAJOR_RADIUS,
            STEERING_WHEEL_MINOR_RADIUS,
            12,
            4,
        )
        steering.add_cylinder_between(
            (0.0, 0.0, -0.018),
            (0.0, 0.0, 0.018),
            0.045,
            8,
        )
        steering.add_cylinder_between(
            (0.0, 0.0, 0.0),
            (0.15, 0.0, 0.0),
            0.018,
            6,
        )
        steering.add_cylinder_between(
            (0.0, 0.0, 0.0),
            (-0.15, 0.0, 0.0),
            0.018,
            6,
        )
        steering.add_cylinder_between(
            (0.0, 0.0, 0.0),
            (0.0, 0.13, 0.0),
            0.018,
            6,
        )
        self.add_accumulator(
            "INT_SteeringWheel",
            steering,
            "steering_wheel",
            "Trim",
            steering_pivot,
        )
        for side, role_prefix, x in (
            ("L", "left", STEERING_GRIP_X),
            ("R", "right", -STEERING_GRIP_X),
        ):
            grip_angle = math.atan2(-STEERING_GRIP_TOP, x)
            self.add_pivot(
                f"ANCHOR_SteeringGrip.{side}",
                f"{role_prefix}_steering_grip",
                steering_pivot,
                (x, -STEERING_GRIP_TOP, STEERING_GRIP_AXIS_OFFSET),
                (0.0, 0.0, grip_angle),
            )

        door_button_pivot = self.add_pivot(
            "PIVOT_DoorButton",
            "door_button",
            self.body,
            DOOR_BUTTON_CENTER,
            (0.0, 0.0, math.pi),
            "+Y",
            DOOR_BUTTON_TRAVEL,
        )
        door_button = MeshAccumulator()
        door_button.add_box(
            (0.0, 0.0, 0.0),
            (0.10, DOOR_BUTTON_DEPTH, 0.10),
        )
        self.add_accumulator(
            "INT_DoorButton",
            door_button,
            "door_button",
            "Accent",
            door_button_pivot,
        )
        self.add_pivot(
            "ANCHOR_DoorButtonPress",
            "door_button_press",
            door_button_pivot,
            (0.0, -DOOR_BUTTON_DEPTH * 0.5 - 0.004, 0.0),
        )

        rails = MeshAccumulator()
        rails.add_cylinder_between((-0.50, -2.90, 2.54), (-0.50, 3.45, 2.54), 0.025, 8)
        rails.add_cylinder_between((0.50, -2.90, 2.54), (0.50, 3.45, 2.54), 0.025, 8)
        for y in (-2.82, -1.25, 0.40, 2.02, 3.28):
            rails.add_cylinder_between((-0.50, y, 2.54), (0.50, y, 2.54), 0.025, 8)
        for x, y in ((-0.70, -1.40), (-0.70, 0.58), (-0.70, 2.08), (0.70, -1.55), (0.70, 0.75), (0.70, 2.45)):
            rails.add_cylinder_between((x, y, 0.65), (x, y, 2.56), 0.027, 8)
        for y in (-2.45, -1.65, -0.85, -0.05, 0.75, 1.55, 2.35):
            rails.add_cylinder_between((-0.40, y, 2.52), (-0.40, y, 2.28), 0.018, 6)
            rails.add_torus_y((-0.40, y, 2.18), 0.09, 0.014, 10, 4)
        self.add_accumulator("INT_Handrails", rails, "handrails", "Rail")

    def _build_doors(self) -> None:
        for doorway_name, doorway_y, leaf_specs in DOORWAY_SPECS:
            self.add_boxes(
                f"GEO_{doorway_name}DoorOuterPosts",
                [
                    (
                        (DOOR_SIDE_X, doorway_y - DOOR_HINGE_OFFSET, 1.68),
                        (0.085, 0.07, 1.72),
                    ),
                    (
                        (DOOR_SIDE_X, doorway_y + DOOR_HINGE_OFFSET, 1.68),
                        (0.085, 0.07, 1.72),
                    ),
                ],
                "door_post",
                "Trim",
            )
            for leaf_name, direction, role in leaf_specs:
                hinge_y = direction * DOOR_HINGE_OFFSET
                panel_y = direction * 0.39 - hinge_y
                center_stile_y = direction * 0.02 - hinge_y
                pivot = self.add_pivot(
                    f"PIVOT_{doorway_name}Door{leaf_name}Leaf",
                    role,
                    self.body,
                    (DOOR_SIDE_X, doorway_y + hinge_y, DOOR_CENTER_Z),
                )
                self.add_boxes(
                    f"GEO_{doorway_name}Door{leaf_name}LeafPanels",
                    [
                        ((0.0, panel_y, -0.46), (0.07, 0.70, 0.58)),
                        ((0.0, panel_y, 0.69), (0.07, 0.70, 0.22)),
                    ],
                    "door_panel",
                    "Body",
                    pivot,
                )
                self.add_boxes(
                    f"GEO_{doorway_name}Door{leaf_name}LeafFrames",
                    [
                        ((0.0, center_stile_y, 0.10), (0.085, 0.04, 1.72)),
                        ((0.0, panel_y, -0.12), (0.085, 0.70, 0.07)),
                        ((0.0, panel_y, 0.80), (0.085, 0.70, 0.07)),
                    ],
                    "door_frame",
                    "Trim",
                    pivot,
                )
                self.add_boxes(
                    f"GLS_{doorway_name}Door{leaf_name}LeafGlass",
                    [
                        ((-0.006, panel_y, 0.33), (0.025, 0.61, 0.82)),
                    ],
                    "door_glass",
                    "Glass",
                    pivot,
                )

    def _build_wheels(self) -> None:
        wheel_specs = (
            ("FL", 1.07, -WHEELBASE * 0.5, True, "front_left"),
            ("FR", -1.07, -WHEELBASE * 0.5, True, "front_right"),
            ("RL", 1.07, WHEELBASE * 0.5, False, "rear_left"),
            ("RR", -1.07, WHEELBASE * 0.5, False, "rear_right"),
        )
        for code, x, y, steering, role_suffix in wheel_specs:
            parent = self.body
            if steering:
                parent = self.add_pivot(
                    f"PIVOT_Wheel{code}Steer",
                    f"{role_suffix}_steering",
                    self.body,
                    (x, y, WHEEL_RADIUS),
                )
                roll_location = (0.0, 0.0, 0.0)
            else:
                roll_location = (x, y, WHEEL_RADIUS)
            roll = self.add_pivot(
                f"PIVOT_Wheel{code}Roll",
                f"{role_suffix}_wheel",
                parent,
                roll_location,
            )
            tyre = MeshAccumulator()
            tyre.add_cylinder_x((0.0, 0.0, 0.0), WHEEL_RADIUS, WHEEL_WIDTH, 16)
            self.add_accumulator(f"GEO_Tire{code}", tyre, "wheel_tire", "Rubber", roll)
            hub = MeshAccumulator()
            hub.add_cylinder_x((0.0, 0.0, 0.0), 0.185, WHEEL_WIDTH + 0.025, 12)
            self.add_accumulator(f"GEO_Hub{code}", hub, "wheel_hub", "Metal", roll)

    def _build_lights_and_details(self) -> None:
        self.add_boxes(
            "LGT_Headlights",
            [
                ((-0.72, -4.134, 0.96), (0.38, 0.035, 0.22)),
                ((0.72, -4.134, 0.96), (0.38, 0.035, 0.22)),
            ],
            "headlight",
            "Headlight",
        )
        self.add_boxes(
            "LGT_TailLights",
            [
                ((-0.84, 4.134, 1.05), (0.24, 0.035, 0.34)),
                ((0.84, 4.134, 1.05), (0.24, 0.035, 0.34)),
            ],
            "tail_light",
            "TailLight",
        )
        # The strips must protrude below the 2.72 m interior ceiling panel;
        # centred any higher they disappear inside its thickness.
        self.add_boxes(
            "LGT_CabinStrips",
            [
                ((-0.43, -0.80, 2.705), (0.12, 4.65, 0.035)),
                ((0.43, -0.80, 2.705), (0.12, 4.65, 0.035)),
                ((0.0, 2.80, 2.705), (0.86, 0.12, 0.035)),
            ],
            "cabin_light",
            "CabinLight",
        )
        lamp_stems = MeshAccumulator()
        lamp_collars = MeshAccumulator()
        lamp_bulbs = MeshAccumulator()
        for lamp_y in CABIN_LAMP_POSITIONS_Y:
            lamp_stems.add_cylinder_between(
                (0.0, lamp_y, CABIN_LAMP_BULB_TOP_Z),
                (0.0, lamp_y, CABIN_LAMP_CEILING_Z),
                CABIN_LAMP_STEM_RADIUS,
                6,
            )
            lamp_collars.add_torus_z(
                (0.0, lamp_y, CABIN_LAMP_BULB_TOP_Z),
                CABIN_LAMP_BULB_RADIUS * 0.72,
                0.018,
                10,
                4,
            )
            lamp_bulbs.add_cylinder_between(
                (0.0, lamp_y, CABIN_LAMP_BULB_BOTTOM_Z),
                (0.0, lamp_y, CABIN_LAMP_BULB_TOP_Z),
                CABIN_LAMP_BULB_RADIUS,
                10,
            )
        self.add_accumulator(
            "GEO_CabinLampStems",
            lamp_stems,
            "cabin_lamp_mount",
            "Metal",
        )
        self.add_accumulator(
            "GEO_CabinLampCollars",
            lamp_collars,
            "cabin_lamp_mount",
            "Trim",
        )
        self.add_accumulator(
            "LGT_CabinLampBulbs",
            lamp_bulbs,
            "cabin_lamp_bulb",
            "CabinLight",
        )
        self.add_boxes(
            "LGT_DestinationSign",
            [((0.0, -4.137, 2.69), (1.48, 0.025, 0.27))],
            "destination_sign",
            "Destination",
        )
        self.add_boxes(
            "GEO_ExteriorTrim",
            [
                ((0.0, -4.158, 0.68), (1.72, 0.05, 0.10)),
                ((0.0, 4.158, 0.68), (1.72, 0.05, 0.10)),
                ((0.0, -4.158, 0.53), (0.48, 0.035, 0.13)),
                ((0.0, 4.158, 0.53), (0.48, 0.035, 0.13)),
                ((-1.27, -3.58, 2.20), (0.18, 0.22, 0.32)),
                ((1.27, -3.58, 2.20), (0.18, 0.22, 0.32)),
            ],
            "exterior_trim",
            "Trim",
        )
        mirror_arms = MeshAccumulator()
        mirror_arms.add_cylinder_between((-1.10, -3.67, 2.17), (-1.27, -3.58, 2.20), 0.025, 8)
        mirror_arms.add_cylinder_between((1.10, -3.67, 2.17), (1.27, -3.58, 2.20), 0.025, 8)
        self.add_accumulator("GEO_MirrorArms", mirror_arms, "mirror_arms", "Metal")
        for wiper_code, wiper_role, base_x, tip_reach in WIPER_SPECS:
            wiper_pivot = self.add_pivot(
                f"PIVOT_Wiper{wiper_code}",
                wiper_role,
                self.body,
                (base_x, WIPER_BASE_Y, WIPER_BASE_Z),
                runtime_axis_local="+Y",
            )
            wiper = MeshAccumulator()
            wiper.add_cylinder_between(
                (0.0, 0.0, 0.0),
                (tip_reach, 0.0, WIPER_TIP_RISE),
                0.012,
                6,
            )
            wiper.add_cylinder_between(
                (tip_reach * 0.30, 0.009, WIPER_TIP_RISE * 0.30),
                (tip_reach, 0.009, WIPER_TIP_RISE),
                0.008,
                6,
            )
            self.add_accumulator(
                f"GEO_Wiper{wiper_code}",
                wiper,
                "wiper",
                "Trim",
                wiper_pivot,
            )

    def _build_anchors(self) -> None:
        self.add_pivot(
            "ANCHOR_DriverSeat",
            "driver_seat_anchor",
            self.body,
            (0.68, -3.28, 1.08),
        )
        self.add_pivot(
            "ANCHOR_FrontDoorEntry",
            "front_door_entry",
            self.body,
            (-0.72, -3.05, 0.66),
        )
        self.add_pivot(
            "ANCHOR_RearDoorEntry",
            "rear_door_entry",
            self.body,
            (-0.72, 1.34, 0.66),
        )
        self.add_pivot(
            "ANCHOR_DriverDoorLook",
            "driver_door_look",
            self.body,
            DRIVER_DOOR_LOOK_POSITION,
        )
        seat_positions = [
            (0.66, -1.82), (0.66, -0.82), (0.66, 0.18),
            (0.66, 1.18), (0.66, 2.18), (0.66, 3.12),
            (-0.66, -0.72), (-0.66, 0.18),
            (-0.66, 2.52), (-0.66, 3.24),
            (-0.22, 3.48), (0.22, 3.48),
        ]
        for index, (x, y) in enumerate(seat_positions, start=1):
            self.add_pivot(
                f"ANCHOR_PassengerSeat_{index:02d}",
                "passenger_seat_anchor",
                self.body,
                (x, y, 1.08),
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
        errors.append("A registered bus part is not a mesh")
    if any(len(part.obj.data.materials) != 1 for part in result.parts):
        errors.append("Every bus mesh must carry one deterministic material slot")

    roles = {part.role for part in result.parts}
    for required_role in (
        "body_shell", "glass", "passenger_seats", "handrails", "dashboard",
        "steering_column", "steering_wheel", "door_button", "door_panel",
        "door_frame", "door_glass", "door_post", "headlight", "tail_light",
        "cabin_light", "cabin_lamp_bulb", "wiper",
    ):
        if required_role not in roles:
            errors.append(f"Missing required mesh role {required_role}")

    pivot_roles = {pivot.role for pivot in result.pivots}
    for required_role in (
        "front_door_forward_leaf", "front_door_rearward_leaf",
        "rear_door_forward_leaf", "rear_door_rearward_leaf",
        "front_left_steering", "front_right_steering",
        "front_left_wheel", "front_right_wheel", "rear_left_wheel", "rear_right_wheel",
        "steering_wheel", "left_steering_grip", "right_steering_grip",
        "door_button", "door_button_press", "driver_door_look",
        "driver_seat_anchor", "front_door_entry", "rear_door_entry",
        "left_wiper", "right_wiper",
    ):
        if required_role not in pivot_roles:
            errors.append(f"Missing required pivot role {required_role}")

    pivots_by_name = {pivot.obj.name: pivot for pivot in result.pivots}

    def validate_control_pivot(
        name: str,
        role: str,
        parent: bpy.types.Object,
        location: Sequence[float],
        rotation_euler: Sequence[float] = (0.0, 0.0, 0.0),
        runtime_axis_local: str = "",
        travel_m: float = 0.0,
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
        if abs(pivot.travel_m - travel_m) > 1e-6:
            errors.append(f"Control pivot {name} has a stale travel contract")
        return pivot

    steering_pivot = validate_control_pivot(
        "PIVOT_SteeringWheel",
        "steering_wheel",
        result.body,
        STEERING_WHEEL_CENTER,
        (-math.pi * 0.5, 0.0, 0.0),
        "+Z",
    )
    if steering_pivot is not None:
        wheel_parts = [
            part for part in result.parts
            if part.obj.parent is steering_pivot.obj and part.role == "steering_wheel"
        ]
        if len(wheel_parts) != 1 or wheel_parts[0].obj.name != "INT_SteeringWheel":
            errors.append(
                "PIVOT_SteeringWheel must directly own the steering-wheel mesh"
            )
        for side, role_prefix, x in (
            ("L", "left", STEERING_GRIP_X),
            ("R", "right", -STEERING_GRIP_X),
        ):
            grip_angle = math.atan2(-STEERING_GRIP_TOP, x)
            validate_control_pivot(
                f"ANCHOR_SteeringGrip.{side}",
                f"{role_prefix}_steering_grip",
                steering_pivot.obj,
                (x, -STEERING_GRIP_TOP, STEERING_GRIP_AXIS_OFFSET),
                (0.0, 0.0, grip_angle),
            )

    door_button_pivot = validate_control_pivot(
        "PIVOT_DoorButton",
        "door_button",
        result.body,
        DOOR_BUTTON_CENTER,
        (0.0, 0.0, math.pi),
        "+Y",
        DOOR_BUTTON_TRAVEL,
    )
    if door_button_pivot is not None:
        button_parts = [
            part for part in result.parts
            if part.obj.parent is door_button_pivot.obj and part.role == "door_button"
        ]
        if len(button_parts) != 1 or button_parts[0].obj.name != "INT_DoorButton":
            errors.append("PIVOT_DoorButton must directly own its visible mesh")
        validate_control_pivot(
            "ANCHOR_DoorButtonPress",
            "door_button_press",
            door_button_pivot.obj,
            (0.0, -DOOR_BUTTON_DEPTH * 0.5 - 0.004, 0.0),
        )

    validate_control_pivot(
        "ANCHOR_DriverDoorLook",
        "driver_door_look",
        result.body,
        DRIVER_DOOR_LOOK_POSITION,
    )

    for wiper_code, wiper_role, base_x, _tip_reach in WIPER_SPECS:
        wiper_pivot = validate_control_pivot(
            f"PIVOT_Wiper{wiper_code}",
            wiper_role,
            result.body,
            (base_x, WIPER_BASE_Y, WIPER_BASE_Z),
            runtime_axis_local="+Y",
        )
        if wiper_pivot is None:
            continue
        wiper_parts = [
            part for part in result.parts
            if part.obj.parent is wiper_pivot.obj
        ]
        if (
            len(wiper_parts) != 1
            or wiper_parts[0].role != "wiper"
            or wiper_parts[0].obj.name != f"GEO_Wiper{wiper_code}"
        ):
            errors.append(
                f"PIVOT_Wiper{wiper_code} must directly own its single wiper mesh"
            )

    expected_leaf_roles: dict[str, tuple[str, tuple[float, float, float]]] = {}
    for doorway_name, doorway_y, leaf_specs in DOORWAY_SPECS:
        for leaf_name, direction, role in leaf_specs:
            expected_leaf_roles[f"PIVOT_{doorway_name}Door{leaf_name}Leaf"] = (
                role,
                (
                    DOOR_SIDE_X,
                    doorway_y + direction * DOOR_HINGE_OFFSET,
                    DOOR_CENTER_Z,
                ),
            )
    for name, (role, expected_location) in expected_leaf_roles.items():
        pivot = pivots_by_name.get(name)
        if pivot is None:
            errors.append(f"Missing required door leaf pivot {name}")
            continue
        if pivot.role != role:
            errors.append(f"Door leaf pivot {name} has role {pivot.role}, expected {role}")
        if pivot.obj.parent is not result.body:
            errors.append(f"Door leaf pivot {name} must be parented to {BODY_NAME}")
        if any(
            abs(actual - expected) > 1e-6
            for actual, expected in zip(pivot.obj.location, expected_location)
        ):
            errors.append(f"Door leaf pivot {name} is not on its outer hinge")
        child_parts = [part for part in result.parts if part.obj.parent is pivot.obj]
        child_roles = sorted(part.role for part in child_parts)
        if child_roles != ["door_frame", "door_glass", "door_panel"]:
            errors.append(
                f"Door leaf pivot {name} must own one panel, frame and glass mesh"
            )
    for legacy_name in ("PIVOT_FrontDoor", "PIVOT_RearDoor"):
        if legacy_name in pivots_by_name:
            errors.append(f"Legacy central doorway pivot {legacy_name} is not allowed")
    door_posts = [part for part in result.parts if part.role == "door_post"]
    if len(door_posts) != 2 or any(
        part.obj.parent is not result.body for part in door_posts
    ):
        errors.append("Both fixed doorway-post meshes must be parented to ROOT_Body")
    if sum(pivot.role == "passenger_seat_anchor" for pivot in result.pivots) < 10:
        errors.append("Bus interior needs at least ten passenger seat anchors")

    bounds_min, bounds_max = mesh_world_bounds(part.obj for part in result.parts)
    size = bounds_max - bounds_min
    if abs(bounds_min.z) > 0.005 or abs(bounds_max.z - HEIGHT) > 0.015:
        errors.append(
            f"Ground/height bounds are {bounds_min.z:.3f}..{bounds_max.z:.3f}, expected 0..{HEIGHT}"
        )
    if size.y < LENGTH - 0.08 or size.y > LENGTH + 0.15:
        errors.append(f"Visual length {size.y:.3f}m differs from {LENGTH:.2f}m")
    if size.x < WIDTH or size.x > WIDTH + 0.40:
        errors.append(f"Visual width {size.x:.3f}m is outside body-plus-mirrors contract")
    if result.root.location.length > 1e-6 or result.body.location.length > 1e-6:
        errors.append("Bus source/body roots must remain at the world origin")
    if bpy.data.actions:
        errors.append("The presentation FBX must contain no authored animation")
    if any(obj.type in {"ARMATURE", "CAMERA", "LIGHT"} for obj in result.source_objects):
        errors.append("Source export hierarchy contains rig, camera or light objects")

    if errors:
        formatted = "\n".join(f"- {error}" for error in errors)
        raise RuntimeError(f"City bus 3D validation failed:\n{formatted}")
    return ValidationReport(
        mesh_count,
        triangle_count,
        tuple(stable_float(value) for value in bounds_min),
        tuple(stable_float(value) for value in bounds_max),
        build_signature(result),
    )


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
    camera_data = bpy.data.cameras.new("CAM_CityBusPreview")
    camera = bpy.data.objects.new("CAM_CityBusPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = (-9.4, -12.0, 6.4)
    target = Vector((0.0, 0.05, 1.35))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 55
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
    ground.add_box((0.0, 0.35, -0.045), (16.0, 15.0, 0.09))
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


def write_manifest(path: Path, result: BuildResult, report: ValidationReport) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-bus-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "forward_axis": "-Y",
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
        "passenger_seat_count": sum(
            pivot.role == "passenger_seat_anchor" for pivot in result.pivots
        ),
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
    result = CityBusBuilder().build()
    bpy.context.view_layer.update()
    report = validate_result(result)
    if not config.no_preview:
        render_preview(config.preview, result)
    export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    save_blend(config.output)
    print("CITY BUS 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(f"  Dimensions: {LENGTH:.2f} x {WIDTH:.2f} x {HEIGHT:.2f} m")
    print(f"  Wheelbase: {WHEELBASE:.2f} m")
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
