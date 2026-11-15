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

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import city_bus_forms as forms
from city_bus_surfaces import paint_uv
from last_route_car_forms import round_headlight

try:
    import bpy
    import bmesh
    from mathutils import Matrix, Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise RuntimeError(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "2.0.0"
DESIGN_ID = "road_v2_midibus_v2"
DISPLAY_NAME = "Road v2 City Midibus"
SEED = 260811

LENGTH = 8.25
WIDTH = 2.38
HEIGHT = 2.95
WHEELBASE = 4.50
WHEEL_RADIUS = 0.43
WHEEL_WIDTH = 0.26
WHEEL_OFFSET = 1.07
WHEEL_HOUSE_OUTSIDE = 1.174
FRONT_WHEEL_HOUSE = ((0., .828, .982), (.11, .788, .970), (.22, .757, .945),
                     (.31, .756, .897), (.38, .787, .841), (.43, .872, .776), (.474, .985, .689))
REAR_WHEEL_HOUSE = ((0., .899, .977), (.20, .900, .954), (.32, .901, .895),
                    (.39, .916, .823), (.433, .950, .751), (.466, .991, .689))
MIN_TRIANGLES = 900
MAX_TRIANGLES = 22000
MAX_RENDERERS = 66
TEXTURE_DIR = Path(__file__).resolve().parents[1] / "Assets/Vehicles/Textures"
TEXTURE_FAMILY = {
    "Body": "Paint", "Accent": "Paint", "Interior": "Interior", "Dashboard": "Interior",
    "Seat": "Seat", "Metal": "Metal", "Rail": "Metal", "Rubber": "Rubber",
    "Floor": "Floor", "Glass": "Glass",
}

SOURCE_COLLECTION = "SOURCE_CityBus3D"
PRESENTATION_COLLECTION = "PRESENTATION_CityBus3D"
ROOT_NAME = "SRC_CityBus3D"
BODY_NAME = "ROOT_Body"

DOOR_SIDE_X = -1.205
DOOR_CENTER_Z = 1.58
DOOR_HINGE_OFFSET = 0.76
DOOR_HEADER_BOTTOM = 2.423
DOOR_SLIDE_RUN = .80
HEADLIGHT_CENTRES = (("L", .72, -4.125, .96), ("R", -.72, -4.125, .96))
MIRROR_Y, MIRROR_Z, MIRROR_DEPTH = -3.989, 2.23, .132
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
# Stop the steps in front of the two wheel envelopes on the boarding side.
STEP_SPANS = ((-3.764, -2.735), (.626, 1.765))
STEP_SECTION = ((1.079, .502, .111, .094), (1.177, .361, .101, .093))

# Source coordinates in metres. Seat 07 and the two doorways keep their
# original contacts. Indices 06 and 10 share the transverse rear bench.
REAR_BENCH_Y = 3.44
REAR_SEAT_SPAN = 0.68
REAR_BENCH_HALF_WIDTH = 0.65
SEAT_POSITIONS = (
    (0.66, -1.65), (0.66, -0.82), (0.66, 0.04),
    (0.66, 1.80), (0.66, 2.56), (REAR_SEAT_SPAN * 0.5, REAR_BENCH_Y),
    (-0.66, -0.72), (-0.66, 0.18), (-0.66, 2.52), (-REAR_SEAT_SPAN * 0.5, REAR_BENCH_Y),
)
CABIN_FLOOR_TOP = 0.67
STANDING_BAYS = ((0.69, -2.61), (-0.69, -1.78), (0.69, 0.74))
HAND_RING_HEIGHT = 2.18
HAND_RING_RADIUS = 0.09
HAND_RING_TUBE = 0.014
HAND_RING_SEGMENTS = 10
# The lower edge of the decagonal ring is horizontal. Its midpoint is
# the centre of the tube inside the grasp, not the empty centre of a ring.
HAND_GRIP_HEIGHT = HAND_RING_HEIGHT - HAND_RING_RADIUS * math.sin(2 * math.tau / HAND_RING_SEGMENTS)
STANDING_GRIPS = tuple((x - 0.25, y - 0.18, HAND_GRIP_HEIGHT) for x, y in STANDING_BAYS)
VERTICAL_RAILS = ((-0.98, -1.40), (-0.98, 0.40), (-0.98, 2.24),
                  (0.98, -1.55), (0.98, 1.15), (0.98, 2.45))

STEERING_WHEEL_CENTER = (0.68, -3.60, 1.57)
STEERING_COLUMN_TILT = math.radians(-65.0)
STEERING_WHEEL_MAJOR_RADIUS = 0.18
STEERING_WHEEL_MINOR_RADIUS = 0.022
DOOR_BUTTON_CENTER = (0.35, -3.59, 1.47)
DOOR_BUTTON_DEPTH = 0.045
DOOR_BUTTON_TRAVEL = 0.012
DRIVER_DOOR_LOOK_POSITION = (-0.90, -3.05, 2.12)
# Anatomical left is +X in source space. Contacts sit on the ball of each
# work shoe; the flat accelerator and wide left rest are separate assemblies.
DRIVER_FOOT_SUPPORTS = (("L", .82, -3.66, .701, .128), ("R", .54, -3.71, .711, .102))

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
    "Floor": 0.62,
}


MATERIALS: dict[str, tuple[tuple[float, float, float, float], float, float, float]] = {
    # slot: (rgba, metallic, roughness, emission strength)
    "Body": ((0.27, 0.075, 0.085, 1.0), 0.05, 0.61, 0.0),
    "Accent": ((0.63, 0.36, 0.095, 1.0), 0.03, 0.58, 0.0),
    "Trim": ((0.035, 0.043, 0.042, 1.0), 0.05, 0.76, 0.0),
    "Rubber": ((0.015, 0.018, 0.017, 1.0), 0.0, 0.92, 0.0),
    "Metal": ((0.24, 0.27, 0.26, 1.0), 0.58, 0.35, 0.0),
    "Glass": ((0.18, 0.27, 0.26, 0.27), 0.04, 0.27, 0.0),
    "Interior": ((0.32, 0.32, 0.25, 1.0), 0.0, 0.73, 0.0),
    "Seat": ((0.07, 0.20, 0.17, 1.0), 0.0, 0.82, 0.0),
    "Rail": ((0.76, 0.46, 0.08, 1.0), 0.31, 0.31, 0.0),
    "Dashboard": ((0.055, 0.065, 0.062, 1.0), 0.0, 0.69, 0.0),
    "Floor": ((0.061, 0.066, 0.061, 1.0), 0.0, 0.84, 0.0),
    "InstrumentInk": ((0.68, 0.65, 0.48, 1.0), 0.0, 0.68, 0.0),
    "MarkerLens": ((0.72, 0.30, 0.042, 1.0), 0.0, 0.47, 0.0),
    "Headlight": ((1.0, 236/255, 209/255, 1.0), 0.02, 0.18, 1.85),
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
        bevel: float | None = None,
    ) -> None:
        if bevel != 0:
            mesh = bmesh.new()
            bmesh.ops.create_cube(mesh, size=1.)
            for v in mesh.verts:
                v.co = Vector(tuple(v.co[i]*size[i]+center[i] for i in range(3)))
            offset = min(.024 if bevel is None else bevel, min(size)*.22)
            bmesh.ops.bevel(mesh, geom=list(mesh.edges), offset=offset, segments=1, affect="EDGES")
            mesh.verts.index_update()
            base = len(self.vertices)
            self.vertices.extend(tuple(v.co) for v in mesh.verts)
            self.faces.extend(tuple(base+v.index for v in f.verts) for f in mesh.faces)
            mesh.free()
            return
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
    parser.add_argument("--validate-only", action="store_true")
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
    family = TEXTURE_FAMILY.get(name)
    if family:
        nodes = material.node_tree.nodes
        links = material.node_tree.links

        def image(suffix, linear=False):
            node = nodes.new("ShaderNodeTexImage")
            path = TEXTURE_DIR / f"CityBus{family}{suffix}.png"
            node.image = bpy.data.images.load(str(path), check_existing=True)
            if linear:
                node.image.colorspace_settings.name = "Non-Color"
            # The editable source remains usable outside this checkout.
            node.image.pack()
            return node

        tex = image("Albedo")
        multiply = nodes.new("ShaderNodeMixRGB")
        multiply.blend_type = "MULTIPLY"
        multiply.inputs[0].default_value = 1.
        multiply.inputs[1].default_value = rgba
        links.new(tex.outputs["Color"], multiply.inputs[2])
        links.new(multiply.outputs["Color"], shader.inputs["Base Color"])
        if family != "Glass":
            normal = nodes.new("ShaderNodeNormalMap")
            normal.inputs["Strength"].default_value = .65
            links.new(image("Normal", True).outputs["Color"], normal.inputs["Color"])
            links.new(normal.outputs["Normal"], shader.inputs["Normal"])
            response = image("Response", True)
            invert = nodes.new("ShaderNodeMath")
            invert.operation = "SUBTRACT"
            invert.inputs[0].default_value = 1.
            links.new(response.outputs["Alpha"], invert.inputs[1])
            links.new(invert.outputs[0], shader.inputs["Roughness"])
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


def apply_uvs(mesh: bpy.types.Mesh, slot: str, parent: bpy.types.Object | None) -> None:
    """Project deterministic per-face box UVs at world scale.

    Every loop takes its UV from the two vertex coordinates orthogonal to the
    face normal's dominant axis, divided by the slot's tile size, so tileable
    albedos land on the geometry without a hand unwrap and Unity materials can
    keep (1, 1) tiling.
    """
    uv_layer = mesh.uv_layers.new(name="UVMap")
    scale = 1.0 / UV_TILE_METERS.get(slot, UV_TILE_METERS_DEFAULT)
    painted = slot in ("Body", "Accent")
    matrix = Matrix.Identity(4)
    root = parent
    while painted and root is not None and root.name != BODY_NAME:
        matrix = (Matrix.Translation(root.location) @ root.rotation_euler.to_matrix().to_4x4()) @ matrix
        root = root.parent
    for polygon in mesh.polygons:
        normal = polygon.normal
        ax, ay, az = abs(normal.x), abs(normal.y), abs(normal.z)
        for loop_index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if painted:
                uv_layer.data[loop_index].uv = paint_uv(matrix @ vertex, matrix.to_3x3() @ normal)
                continue
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
    apply_uvs(mesh, material_slot, parent)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    obj.data.materials.append(material)
    if material_slot not in ("Glass", "InstrumentInk", "Preview"):
        # Smooth across the pressed facets while holding the broad planes.
        for polygon in mesh.polygons:
            polygon.use_smooth = True
        normals = obj.modifiers.new("Form-weighted normals", "WEIGHTED_NORMAL")
        normals.weight = 40
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
        skin = MeshAccumulator()
        forms.roof(skin)
        forms.end_panel(skin, -1, .54, 1.60, .15)
        forms.end_panel(skin, -1, 2.52, 2.80, .15)
        forms.end_panel(skin, 1, .54, 1.76, .16)
        forms.end_panel(skin, 1, 2.53, 2.80, .16)
        door_spans = ((-4.00, -3.82), (-2.28, .57), (2.11, 4.00))
        for side, spans in ((1, ((-4.00, 4.00),)), (-1, door_spans)):
            for start, end in spans:
                forms.side_panel(skin, side, start, end, 1.51, .43, (-2.25, 2.25))
            forms.side_panel(skin, side, -4.00, 4.00, 2.746, 2.49)
        # Continuous pressed quadrants bridge the recessed end skin and each
        # straight side. They sit behind the glazing and fixed door jambs.
        for side in (-1, 1):
            for end in (-1, 1):
                corner = ((1.006,4.092),(1.068,4.116),(1.106,4.093),
                          (1.167,4.017),(1.185,3.958),(1.189,3.853),
                          (1.140,3.842),(1.103,3.946),(1.021,4.036))
                forms.loft(skin, [[(side*(x-inset),end*(y-inset),z) for x,y in corner]
                    for z,inset in ((.55,.022),(.59,0.),(2.55,0.),(2.606,.020),(2.68,.048))])
        self.add_accumulator("GEO_BodyShell", skin, "body_shell", "Body")

        stripe = MeshAccumulator()
        for side, spans in ((1, ((-3.89, 3.89),)), (-1, door_spans)):
            for start, end in spans:
                forms.side_panel(stripe, side, start, end, 1.52, 1.397, outside=1.199, depth=.02)
        self.add_accumulator("GEO_BodyAccent", stripe, "body_accent", "Accent")

        trims = MeshAccumulator()
        for side in (-1, 1):
            forms.arch_rim(trims, side, -2.25, start=-2.28 if side < 0 else -math.inf)
            forms.arch_rim(trims, side, 2.25, start=2.11 if side < 0 else -math.inf)
        # Rubber step edges, sill rub rails and bumper corners read as separate pieces.
        for x in (-.94, .94):
            for end in (-1, 1):
                trims.add_box((x, end*4.039, .533), (.43, .207, .174))
        for x, start, end in ((1.19,-3.76,3.76),(-1.19,-2.26,.55),(-1.19,2.14,3.76)):
            trims.add_box((x, (start+end)*.5, 1.04), (.040,end-start,.073))
        self.add_accumulator("GEO_ArchAndSillMouldings", trims, "arch_and_sill", "Trim")

        pillar_boxes = []
        for x, start, end, mullions in (
            (1.179,-3.91,3.91,(-3.02,-2.02,-1.02,-.02,.98,1.98,2.98)),
            (-1.179,-2.31,.60,(-1.36,-.38)), (-1.179,2.08,3.91,(2.99,))):
            for z in (1.537, 2.494):
                pillar_boxes.append(((x, (start+end)*.5, z), (.060, end-start, .076)))
            for y in (start, *mullions, end):
                pillar_boxes.append(((x, y, 2.024), (.069, .094, 1.01)))
        for end in (-1, 1):
            y = end*4.116
            for z in ((1.604, 2.536) if end < 0 else (1.751, 2.536)):
                pillar_boxes.append(((0.,y,z),(2.151,.048,.068)))
            for x in (-1.05, 1.05):
                pillar_boxes.append(((x,y,2.080),(.112,.055,.93)))
        self.add_boxes("GEO_WindowFrames", pillar_boxes, "window_frame", "Trim")
        self.add_boxes("GEO_Underbody", [
            ((0., .25, .363),(1.62,6.84,.134)),
            ((0.,3.33,.360),(1.91,1.05,.255)),
            ((0.,-3.37,.323),(1.82,.99,.151)),
            *((((0.,y,.43),(1.91,.14,.16)) for y in (-2.25,2.25))),
        ], "underbody", "Trim")

    def _build_glass(self) -> None:
        windows = []
        for x, edges in (
            (1.179, (-3.93,-3.02,-2.02,-1.02,-.02,.98,1.98,2.98,3.93)),
            (-1.179, (-2.33,-1.36,-.38,.62)), (-1.179, (2.06,2.99,3.93))):
            for start, end in zip(edges, edges[1:]):
                windows.append(((x,(start+end)*.5,2.013), (.018,end-start-.057,.921)))
        # Preserve the swept windshield plane and the visible rear sill.
        windows.extend((((0.,-4.130,2.074),(2.094,.020,.922)),
                        ((0.,4.130,2.140),(2.094,.020,.782))))
        self.add_boxes("GLS_Windows", windows, "glass", "Glass")

    def _build_interior(self) -> None:
        floor = MeshAccumulator()
        floor.add_box((0., 0., CABIN_FLOOR_TOP-.05), (2.08, 7.70, .10))
        self.add_boxes(
            "INT_CabinPanels",
            [
                ((0.0, 0.0, 2.75), (2.06, 7.68, 0.06)),
                ((0.0, 3.76, 1.13), (2.05, 0.12, 0.95)),
                ((0.0, -3.97, 0.83), (2.05, 0.10, 0.40)),
                # The bench base begins behind its front lip. Shoes on the
                # rear row land on the same drawn floor as all other riders.
                ((0.0, REAR_BENCH_Y + 0.06, 0.805), (1.16, 0.32, 0.27)),
            ],
            "cabin_panels", "Interior",
        )
        lining = MeshAccumulator()
        for side in (-1, 1):
            for axle, profile in ((-WHEELBASE*.5, FRONT_WHEEL_HOUSE), (WHEELBASE*.5, REAR_WHEEL_HOUSE)):
                forms.wheel_housing(lining, side, axle, WHEEL_HOUSE_OUTSIDE, CABIN_FLOOR_TOP-.034, profile)
        for side, spans in ((1, ((-3.86,3.72),)), (-1, ((-2.28,.57),(2.11,3.72)))):
            for start,end in spans:
                lining.add_box((side*1.073,(start+end)*.5,1.133),(.059,end-start,.726))
                lining.add_box((side*1.105,(start+end)*.5,1.496),(.102,end-start,.072))
        for start, end in STEP_SPANS:
            y, run = (start+end)*.5, end-start
            # Non-slip caps share the deck finish; liners wrap the risers.
            for x, z, width, height in STEP_SECTION:
                lining.add_box((-x, y, z-.006), (width, run, height-.012))
                floor.add_box((-x, y, z+height*.5-.006), (width, run, .014), .004)
            lining.add_box((-1.125,y,.451), (.020,run,.140))
            lining.add_box((-1.038,y,.602), (.029,run,.139))
        self.add_accumulator("INT_InteriorSurfaces", floor, "interior_shell", "Floor")
        self.add_accumulator("INT_LiningAndSteps", lining, "lining_and_steps", "Interior")

        seats = MeshAccumulator()
        frames = MeshAccumulator()
        piping = MeshAccumulator()
        for x, y in SEAT_POSITIONS:
            if y == REAR_BENCH_Y:
                continue
            seats.add_box((x,y,1.01),(.52,.48,.14), .018)
            # The upholstered back follows a shallow lean over a rigid pan.
            forms.loft(seats, [[(x+dx,y+dy+lean,z) for dx,dy in
                ((-w,-.050),(w,-.050),(w+.019,-.020),(w+.019,.034),
                 (w,.055),(-w,.055),(-w-.019,.034),(-w-.019,-.020))]
                for z,w,lean in ((1.082,.214,.183),(1.137,.241,.184),
                    (1.552,.232,.231),(1.630,.211,.259),(1.656,.192,.260))])
            piping.add_box((x,y-.238,1.018),(.456,.013,.016))
            piping.add_box((x,y+.315,1.411),(.355,.018,.165))
            frames.add_box((x,y+.016,.930),(.403,.385,.081))
            for side in (-1,1):
                xx=x+side*.151
                frames.add_cylinder_between((xx,y+.127,.670),(xx,y+.115,.949),.020,8)
                frames.add_cylinder_between((xx,y-.107,.670),(xx,y-.073,.940),.018,8)
                frames.add_box((xx,y,.682),(.075,.321,.025))
        self.add_accumulator("INT_PassengerSeats", seats, "passenger_seats", "Seat")
        self._build_rear_bench()
        frames.add_box((.681,-3.25,.751), (.294,.303,.169))
        self.add_accumulator("INT_SeatFrames", frames, "seat_frames", "Metal")
        self.add_accumulator("INT_SeatEdging", piping, "seat_edging", "Trim")

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
                ((0.0,-3.86,1.434),(2.06,.33,.154)),
                ((.672,-3.916,1.533),(.660,.245,.171)),
                ((-.64,-3.585,1.33),(.335,.253,.429)),
                ((.35,-3.666,1.461),(.183,.105,.130)),
                ((.681,-3.25,.854),(.432,.427,.078)),
            ],
            "dashboard",
            "Dashboard",
        )
        controls = MeshAccumulator()
        markings = MeshAccumulator()
        for x,radius in ((.507,.052),(.671,.073),(.827,.057)):
            # Dial and raised bezel are seen from the driver's side.
            controls.add_torus_y((x,-3.786,1.53),radius,.008,14,4)
            controls.add_cylinder_between((x,-3.792,1.53),(x,-3.781,1.53),radius-.006,14)
            for i in range(7):
                a = math.radians(205-38*i)
                a0 = Vector((x+math.cos(a)*radius*.80,-3.780,1.53+math.sin(a)*radius*.80))
                a1 = Vector((x+math.cos(a)*radius*.62,-3.779,1.53+math.sin(a)*radius*.62))
                markings.add_cylinder_between(a0,a1,.0018,4)
            markings.add_cylinder_between((x,-3.777,1.53),(x-.027,-3.777,1.51),.0022,5)
        for x in (.135,.216,.881,.960):
            controls.add_box((x,-3.679,1.442),(.034,.036,.038))
        # The right shoe works the accelerator, the left has a separate floor rest.
        for _, x, y, z, width in DRIVER_FOOT_SUPPORTS:
            controls.add_box((x,y,z-.020),(width,.103,.036))
            for step in range(3):
                markings.add_box((x,y-.026+.026*step,z-.002),(width*.83,.004,.004))
        self.add_accumulator("INT_ControlHardware",controls,"control_hardware","Trim")
        self.add_accumulator("INT_InstrumentMarks",markings,"instrument_detail","InstrumentInk")
        steering_column = MeshAccumulator()
        steering_column.add_cylinder_between(
            STEERING_WHEEL_CENTER,
            (0.68, -3.84, 1.458),
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
            (STEERING_COLUMN_TILT, 0.0, 0.0),
            "+Z",
        )
        steering = MeshAccumulator()
        steering.add_torus_z(
            (0.0, 0.0, 0.0),
            STEERING_WHEEL_MAJOR_RADIUS,
            STEERING_WHEEL_MINOR_RADIUS,
            32,
            8,
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
            ("L", "left", STEERING_WHEEL_MAJOR_RADIUS),
            ("R", "right", -STEERING_WHEEL_MAJOR_RADIUS),
        ):
            grip_angle = 0.0 if x > 0 else math.pi
            self.add_pivot(
                f"ANCHOR_SteeringGrip.{side}",
                f"{role_prefix}_steering_grip",
                steering_pivot,
                (x, 0.0, 0.0),
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
        for x, y in VERTICAL_RAILS:
            rails.add_cylinder_between((x, y, 0.65), (x, y, 2.54), 0.027, 8)
            rails.add_cylinder_between((x, y, 2.54), (math.copysign(0.5, x), y, 2.54), 0.025, 8)
        rings = [(x, y) for x, y, _ in STANDING_GRIPS]
        rings.extend((-0.40, y) for y in (-0.85, -0.05, 1.55, 2.35))
        for x, y in rings:
            # A short transverse bracket seats each hanger on the longitudinal rail.
            rail_x = math.copysign(0.50, x)
            rails.add_cylinder_between((rail_x, y, 2.54), (x, y, 2.54), 0.018, 8)
            rails.add_cylinder_between((x, y, 2.54), (x, y, 2.26), 0.018, 8)
            rails.add_torus_y((x, y, HAND_RING_HEIGHT), HAND_RING_RADIUS, HAND_RING_TUBE, HAND_RING_SEGMENTS, 4)
        self.add_accumulator("INT_Handrails", rails, "handrails", "Rail")

    def _build_rear_bench(self) -> None:
        # Sweep one upholstered profile from the front bolster into the
        # sloped shared backrest. A shallow stitched channel divides the two
        # contact areas but never cuts through the cushion.
        profile = (
            (-0.25, 0.97), (-0.21, 0.94), (0.24, 0.94),
            (0.25, 1.59), (0.22, 1.66), (0.15, 1.68),
            (0.11, 1.64), (0.07, 1.16), (0.035, 1.08),
            (-0.20, 1.08), (-0.25, 1.05),
        )
        half = REAR_BENCH_HALF_WIDTH
        stations = (-half, -half + 0.035, -0.023, 0.0, 0.023, half - 0.035, half)
        shell = MeshAccumulator()
        count = len(profile)
        for x in stations:
            end = abs(x) == half
            for depth, height in profile:
                if end:
                    depth *= 0.90
                    height = 1.30 + (height - 1.30) * 0.93
                elif x == 0.0:
                    if height >= 1.05:
                        height -= 0.009
                    if height >= 1.15:
                        depth += 0.008
                shell.vertices.append((x, REAR_BENCH_Y + depth, height))
        shell.faces.append(tuple(reversed(range(count))))
        for row in range(len(stations) - 1):
            start = row * count
            for i in range(count):
                j = (i + 1) % count
                shell.faces.append((start + i, start + j, start + count + j, start + count + i))
        start = (len(stations) - 1) * count
        shell.faces.append(tuple(start + i for i in range(count)))
        self.add_accumulator("INT_RearBench", shell, "rear_bench", "Seat")

    def _build_doors(self) -> None:
        for doorway_name, doorway_y, leaf_specs in DOORWAY_SPECS:
            posts = [
                ((DOOR_SIDE_X, doorway_y + direction*DOOR_HINGE_OFFSET, 1.68), (.085,.09,1.72))
                for _, direction, _ in leaf_specs
            ]
            posts.append(((-1.180, doorway_y, (DOOR_HEADER_BOTTOM+2.558)*.5),
                          (.123,1.636,2.558-DOOR_HEADER_BOTTOM)))
            for leaf_name, direction, _ in leaf_specs:
                if doorway_name == "Front" and leaf_name == "Forward":
                    continue
                # The three top guides start above their jambs and run over
                # the window border where the parallel leaf parks outside.
                y = doorway_y + direction*(DOOR_HINGE_OFFSET + DOOR_SLIDE_RUN*.5)
                posts.append(((-1.242, y, 2.458), (.146,DOOR_SLIDE_RUN+.12,.058)))
            self.add_boxes(
                f"GEO_{doorway_name}DoorOuterPosts",
                posts,
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
                        ((0.0, panel_y, -0.60), (0.07, 0.70, 0.85)),
                        ((0.0, panel_y, 0.69), (0.07, 0.70, 0.22)),
                    ],
                    "door_panel",
                    "Body",
                    pivot,
                )
                self.add_boxes(
                    f"GEO_{doorway_name}Door{leaf_name}LeafFrames",
                    [
                        ((0.0, center_stile_y, -.095), (0.085, 0.04, 1.85)),
                        ((0.0, panel_y, -0.12), (0.085, 0.70, 0.07)),
                        ((0.0, panel_y, -1.008), (0.082, 0.70, 0.057)),
                        ((0.0, panel_y + direction*.331, .34), (.083,.035,.928)),
                        ((0.0, panel_y, .805), (0.085, 0.70, .061)),
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
            ("FL", WHEEL_OFFSET, -WHEELBASE * 0.5, True, "front_left"),
            ("FR", -WHEEL_OFFSET, -WHEELBASE * 0.5, True, "front_right"),
            ("RL", WHEEL_OFFSET, WHEELBASE * 0.5, False, "rear_left"),
            ("RR", -WHEEL_OFFSET, WHEELBASE * 0.5, False, "rear_right"),
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
            half = WHEEL_WIDTH*.5
            forms.rolling_profile(tyre, ((-half,.231),(-half,.365),
                (-.099,.413),(-.062,WHEEL_RADIUS),(.062,WHEEL_RADIUS),
                (.099,.413),(half,.365),(half,.231)))
            self.add_accumulator(f"GEO_Tire{code}", tyre, "wheel_tire", "Rubber", roll)
            hub = MeshAccumulator()
            side = math.copysign(1.,x)
            forms.rolling_profile(hub, [(side*offset,radius) for offset,radius in (
                (.113,.222),(.131,.222),(.139,.201),(.122,.174),
                (.125,.122),(.159,.108),(.160,.084))], 18)
            for i in range(6):
                a = math.tau*i/6
                hub.add_cylinder_x((side*.148,.144*math.cos(a),.144*math.sin(a)),.013,.039,6)
            self.add_accumulator(f"GEO_Hub{code}", hub, "wheel_hub", "Metal", roll)

    def _build_lights_and_details(self) -> None:
        surrounds = MeshAccumulator()
        markers = MeshAccumulator()
        hardware = MeshAccumulator()
        for end in (-1, 1):
            forms.end_panel(surrounds, end, .457, .652, .16, 1.162)
            y = end*4.122
            if end > 0:
                for x in (-.84,.84):
                    surrounds.add_box((x,y,1.05),(.311,.069,.406))
            for x in (-1.012,1.012):
                markers.add_box((x if end > 0 else math.copysign(.72,x),
                                 end*(4.107 if end > 0 else 4.126),
                                 .947 if end > 0 else .771),(.142,.065,.079))
            for x in (-.93,.93):
                markers.add_box((x,end*4.074,2.714),(.129,.066,.039))
            # Engine air and cooling live behind a shadowed recess. Separate
            # pressed metal slats catch light across the slot.
            grille_width = .816 if end < 0 else 1.198
            surrounds.add_box((0.,end*4.116,1.006),(grille_width,.051,.258))
            for row in range(6):
                hardware.add_box((0.,end*4.145,.906+row*.039),
                    (grille_width-.050,.020,.011))
        for x in (-1.27,1.27):
            hardware.add_box((x,MIRROR_Y+MIRROR_DEPTH*.5+.002,MIRROR_Z),(.151,.013,.258))
        for side,x,y,z in HEADLIGHT_CENTRES:
            lens = MeshAccumulator()
            round_headlight(lens, hardware, (x,y,z))
            self.add_accumulator(f"LGT_Headlight.{side}",lens,"headlight","Headlight")
            self.add_pivot(f"ANCHOR_Headlight.{side}",f"headlight_{side.lower()}",
                           self.body,(x,y-.052,z))
        self.add_accumulator("GEO_FasciaHousings", surrounds, "fascia_housings", "Trim")
        self.add_accumulator("GEO_GrilleAndMirrorFaces", hardware, "grille_and_mirrors", "Metal")
        self.add_accumulator("GEO_MarkerLenses", markers, "marker_lenses", "MarkerLens")
        self.add_boxes(
            "LGT_TailLights",
            [
                ((-0.84, 4.162, 1.05), (0.24, 0.035, 0.34)),
                ((0.84, 4.162, 1.05), (0.24, 0.035, 0.34)),
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
        self.add_boxes("GEO_CabinStripHousings", [
            ((x,-.80,2.723),(.166,4.69,.048)) for x in (-.43,.43)
        ] + [((0.,2.80,2.723),(.912,.164,.048))], "cabin_strip_housings", "Dashboard")
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
            [((0.0, -4.137, 2.671), (1.48, 0.025, 0.195))],
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
                ((-1.27, MIRROR_Y, MIRROR_Z), (.18, MIRROR_DEPTH, .32)),
                ((1.27, MIRROR_Y, MIRROR_Z), (.18, MIRROR_DEPTH, .32)),
                ((0.,-4.123,2.672),(1.590,.050,.234)),
            ],
            "exterior_trim",
            "Trim",
        )
        mirror_arms = MeshAccumulator()
        for side in (-1,1):
            mirror_arms.add_cylinder_between((side*1.115,-4.061,MIRROR_Z-.115),
                (side*1.27,MIRROR_Y,MIRROR_Z-.102),.023,8)
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
        for side, x, y, z, _ in DRIVER_FOOT_SUPPORTS:
            self.add_pivot(f"ANCHOR_DriverFoot.{side}", f"driver_foot_{side.lower()}",
                           self.body, (x,y,z))
        for index, (x, y) in enumerate(SEAT_POSITIONS, start=1):
            self.add_pivot(
                f"ANCHOR_PassengerSeat_{index:02d}",
                "passenger_seat_anchor",
                self.body,
                (x, y, 1.08),
            )

        for i, ((x, y), grip) in enumerate(zip(STANDING_BAYS, STANDING_GRIPS), start=1):
            self.add_pivot(f"ANCHOR_StandingFloor_{i:02d}", "standing_floor",
                           self.body, (x, y, CABIN_FLOOR_TOP))
            self.add_pivot(f"ANCHOR_StandingGrip_{i:02d}", "standing_grip",
                           self.body, grip)


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


def validate_wheel_clearance(result: BuildResult, errors: list[str]) -> None:
    lining = next(p.obj for p in result.parts if p.role == "lining_and_steps")
    floor = next(p.obj for p in result.parts if p.role == "interior_shell")
    down = Vector((0., 0., -1.))
    for axle, (key, step), steer_limit in zip((-WHEELBASE*.5, WHEELBASE*.5),
                                              zip(("FL", "RL"), STEP_SPANS), (28, 0)):
        tire = next(p.obj for p in result.parts if p.obj.name == f"GEO_Tire{key}")
        rings = {(round(v.co.x, 6), round(math.hypot(v.co.y, v.co.z), 6)) for v in tire.data.vertices}
        tread_min = min(x-width*.5 for x, _, width, _ in STEP_SECTION)
        tread_max = max(x+width*.5 for x, _, width, _ in STEP_SECTION)
        tread_bottom = min(z-height*.5 for _, z, _, height in STEP_SECTION)
        # The wheels are unsprung. Sweep the actual tire profile through roll and
        # steer, relative to the full body motion in CityBusPresentation.
        body_poses = [(Matrix.Rotation(math.radians(roll), 3, 'Y') @
                       Matrix.Rotation(math.radians(pitch), 3, 'X'), Vector((0.,0.,heave)))
                      for heave in (-.045, 0., .045) for pitch in (-.8, 0., .8) for roll in (-1., 0., 1.)]
        crown_gap = float('inf')
        step_clear = True
        centre = Vector((WHEEL_OFFSET, axle, WHEEL_RADIUS))
        for steer in range(-steer_limit, steer_limit+1, 7):
            steer_matrix = Matrix.Rotation(math.radians(steer), 3, 'Z')
            for x, radius in rings:
                for angle in range(0, 360, 10):
                    angle = math.radians(angle)
                    point = centre + steer_matrix @ Vector((x, radius*math.cos(angle), radius*math.sin(angle)))
                    for rotation, offset in body_poses:
                        p = rotation @ (point-offset)
                        # The boarding side is the mirror image; all extremal
                        # steer and body angles are included with both signs.
                        if tread_min <= p.x <= tread_max and step[0] <= p.y <= step[1] and tread_bottom <= p.z <= CABIN_FLOOR_TOP:
                            step_clear = False
                        if p.z > CABIN_FLOOR_TOP and p.x < WHEEL_HOUSE_OUTSIDE-.005:
                            for side in (-1, 1):
                                hit, contact, _, _ = lining.ray_cast(Vector((side*p.x, p.y, 1.66)), down)
                                crown_gap = min(crown_gap, contact.z-p.z if hit else -1.)
        if crown_gap < .002:
            errors.append(f"The {key} tire emerges through the cabin lining ({crown_gap:+.3f} m clearance)")
        if not step_clear:
            errors.append(f"The {key} axle intersects a boarding step")
    for (_, y, _), (start, end) in zip(DOORWAY_SPECS, STEP_SPANS):
        for x, z, _, height in STEP_SECTION:
            for longitudinal in (y-.18, y, y+.18):
                hit, contact, _, _ = floor.ray_cast(Vector((-x,longitudinal,.64)), down)
                if not start < longitudinal < end or not hit or abs(contact.z-(z+height*.5)) > .003:
                    errors.append(f"Boarding step near y={y:.2f} has no continuous tread under the doorway")
                    break


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
    if mesh_count > MAX_RENDERERS:
        errors.append(f"{mesh_count} visible parts exceed the {MAX_RENDERERS}-renderer budget")
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
        (STEERING_COLUMN_TILT, 0.0, 0.0),
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
            ("L", "left", STEERING_WHEEL_MAJOR_RADIUS),
            ("R", "right", -STEERING_WHEEL_MAJOR_RADIUS),
        ):
            grip_angle = 0.0 if x > 0 else math.pi
            validate_control_pivot(
                f"ANCHOR_SteeringGrip.{side}",
                f"{role_prefix}_steering_grip",
                steering_pivot.obj,
                (x, 0.0, 0.0),
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

    footwell = next(part.obj for part in result.parts if part.role == "control_hardware")
    for side, x, y, z, _ in DRIVER_FOOT_SUPPORTS:
        validate_control_pivot(f"ANCHOR_DriverFoot.{side}", f"driver_foot_{side.lower()}",
                               result.body, (x,y,z))
        hit, position, _, _ = footwell.ray_cast(Vector((x,y,z+.02)), Vector((0,0,-1)))
        if not hit or abs(position.z - z) > .003:
            errors.append(f"Driver foot support {side} misses the pedal surface")

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
            errors.append(f"Door leaf pivot {name} is not at its outer jamb")
        child_parts = [part for part in result.parts if part.obj.parent is pivot.obj]
        child_roles = sorted(part.role for part in child_parts)
        if child_roles != ["door_frame", "door_glass", "door_panel"]:
            errors.append(
                f"Door leaf pivot {name} must own one panel, frame and glass mesh"
            )
    # Test the full projected body, rather than just a clear centre point.
    # Keep the central 0.70 m passage and access from both doorways clear;
    # the seated envelope extends ahead of the cushion to the shins and shoes.
    body_radius = 0.30
    for i, ((x, y), grip) in enumerate(zip(STANDING_BAYS, STANDING_GRIPS), start=1):
        validate_control_pivot(f"ANCHOR_StandingFloor_{i:02d}", "standing_floor", result.body,
                               (x, y, CABIN_FLOOR_TOP))
        validate_control_pivot(f"ANCHOR_StandingGrip_{i:02d}", "standing_grip", result.body, grip)
        if abs(x) - body_radius < 0.35 or abs(x) + body_radius > 1.04:
            errors.append(f"Standing bay {i} encroaches on the aisle or cabin wall")
        # Both doorways cross to the aisle at their own longitudinal station.
        # Door panels park at the sides of the two public crossing paths.
        for _, door_y, _ in DOORWAY_SPECS:
            if x < 0 and abs(y - door_y) < DOOR_HINGE_OFFSET + body_radius:
                errors.append(f"Standing bay {i} overlaps the doorway")
        for sx, sy in SEAT_POSITIONS:
            dx = max(abs(x - sx) - 0.26, 0)
            dy = max(sy - 0.60 - y, y - sy - 0.26, 0)
            if math.hypot(dx, dy) < body_radius:
                errors.append(f"Standing bay {i} overlaps seated legs or a cushion")
        for px, py in VERTICAL_RAILS:
            if math.hypot(x - px, y - py) < body_radius + 0.027:
                errors.append(f"Standing bay {i} overlaps a pole")
        if math.hypot(max(abs(x - 0.68) - 0.275, 0), max(-3.54 - y, y + 2.975, 0)) < body_radius:
            errors.append(f"Standing bay {i} overlaps the driver")

    for legacy_name in ("PIVOT_FrontDoor", "PIVOT_RearDoor"):
        if legacy_name in pivots_by_name:
            errors.append(f"Legacy central doorway pivot {legacy_name} is not allowed")
    door_posts = [part for part in result.parts if part.role == "door_post"]
    if len(door_posts) != 2 or any(
        part.obj.parent is not result.body for part in door_posts
    ):
        errors.append("Both fixed doorway-post meshes must be parented to ROOT_Body")
    door_meshes = [part.obj for part in result.parts if part.role in {"door_frame", "door_panel"}]
    front_hinge = DOORWAY_SPECS[0][1] - DOOR_HINGE_OFFSET
    if MIRROR_Y + MIRROR_DEPTH*.5 + .01 >= front_hinge - .04:
        errors.append("Fixed mirrors must be ahead of the front doorway")
    if any((obj.matrix_world @ v.co).z >= DOOR_HEADER_BOTTOM - .004 for obj in door_meshes for v in obj.data.vertices):
        errors.append("Moving door skin must clear the stationary header through its sweep")
    opaque_body = [p.obj for p in result.parts if p.role in {"door_post", "body_shell", "window_frame"}]
    for _, y, _ in DOORWAY_SPECS:
        for offset in (-.70, -.34, 0., .34, .70):
            for z in (2.444, 2.485, 2.530):
                if not any(obj.ray_cast(Vector((-1.4,y+offset,z)), Vector((1,0,0)), distance=.33)[0]
                           for obj in opaque_body):
                    errors.append(f"Open seam above closed doorway at {y+offset:.2f}, {z:.3f}")
    validate_wheel_clearance(result, errors)
    lenses = {p.obj.name: p.obj for p in result.parts if p.role == "headlight"}
    for side,x,y,z in HEADLIGHT_CENTRES:
        validate_control_pivot(f"ANCHOR_Headlight.{side}",f"headlight_{side.lower()}",result.body,(x,y-.052,z))
        lens = lenses[f"LGT_Headlight.{side}"]
        hit, pos, _, _ = lens.ray_cast(Vector((x,y-.080,z)), Vector((0,1,0)))
        if not hit or abs(pos.y-(y-.048)) > .002:
            errors.append(f"Headlight {side} does not meet the visible lens and beam contract")
    seats = [p for p in result.pivots if p.role == "passenger_seat_anchor"]
    rear = [p.obj for p in seats if p.obj.location.y > 3.0]
    if len(seats) != 10 or {p.name for p in rear} != {"ANCHOR_PassengerSeat_06", "ANCHOR_PassengerSeat_10"}:
        errors.append("The cabin requires eight side seats and exactly two rear bench contacts")
    bench = next(part.obj for part in result.parts if part.role == "rear_bench")
    # Raycast the actual continuous cushion along both large occupants' hip
    # support areas and through the stitched centre. The edge of the seat has
    # room for the outside trouser leg; neither person needs to overlap the other.
    if len(rear) == 2:
        a, b = sorted(p.location.x for p in rear)
        if b - a < 0.66:
            errors.append("Rear bench lacks shoulder room for its two passengers")
        for x in (a - 0.26, a, a + 0.26, 0.0, b - 0.26, b, b + 0.26):
            hit, contact, normal, _ = bench.ray_cast(Vector((x, REAR_BENCH_Y, 1.20)), Vector((0, 0, -1)))
            if not hit or abs(contact.z - 1.08) > 0.012 or normal.z < 0.85:
                errors.append(f"Rear bench has no continuous cushion contact at {x:.3f} m")
    interior = next(part.obj for part in result.parts if part.role == "interior_shell")
    for seat in rear:
        for foot in (-0.12, 0.12):
            x = seat.location.x + foot
            hit, contact, _, _ = interior.ray_cast(Vector((x, REAR_BENCH_Y - 0.40, 1.05)), Vector((0, 0, -1)))
            if not hit or abs(contact.z - CABIN_FLOOR_TOP) > 0.003:
                errors.append(f"Raised rear panel blocks the bench footwell at {x:.3f} m")

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
        "standing_place_count": len(STANDING_BAYS),
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
    if config.validate_only:
        print(f"CITY BUS 3D CONTRACT OK: {report.mesh_count} meshes, {report.triangle_count} triangles, {report.build_signature}")
        return
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
