#!/usr/bin/env python3
"""Generate an experimental low-poly 3D model of Bar Promenade's hero.

This script is meant to be executed by Blender, not regular CPython:

    blender --background --factory-startup \
      --python tools/build-player-3d-model.py -- \
      --output ArtSource/Player/Blender/PlayerCharacter3D.blend

The model follows the locked 2D character design while deliberately keeping
its geometry modular. Every anatomical segment is an independent mesh object,
and clothes/accessories remain separate as well. Objects are rigidly weighted
to one armature bone instead of being joined, so the generated .blend, FBX and
GLB retain editable body parts.

Coordinate contract
-------------------

* Blender Z is up and the character faces -Y.
* Anatomical left is +X when the character faces the preview camera.
* The bandage is therefore on .L/+X and the ochre patch is on .R/-X.
* Canonical visible height is 1.75 m, matching 84 visible atlas pixels at
  48 PPU. Shoulder, elbow, hip and knee heights follow the current puppet
  pivots rather than realistic 7.5-head anatomy.

The generator uses only Blender's bundled Python API and standard library.
It avoids Subdivision, booleans and smoothing so the result stays angular,
muted and appropriate for the project's restrained PS1 aesthetic.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import random
import sys
import traceback
import warnings
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

import bmesh
import bpy
from mathutils import Quaternion, Vector


GENERATOR_VERSION = "1.0.0"
CANONICAL_HEIGHT = 1.75
DEFAULT_SEED = 7301
MAX_TRIANGLES = 4500

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = (
    REPO_ROOT
    / "ArtSource"
    / "Player"
    / "Blender"
    / "PlayerCharacter3D.blend"
)

# These values are approximated from the locked turntable/reference atlas.
# They are kept here as readable sRGB hex values and converted to scene-linear
# before entering Principled BSDF Base Color.
PALETTE_HEX = {
    "Skin": "AE8D7B",
    "SkinShadow": "67504B",
    "SkinDark": "392D2E",
    "Hair": "08080B",
    "HairHighlight": "202129",
    "Jacket": "542B38",
    "JacketDark": "311A25",
    "JacketEdge": "704050",
    "Shirt": "2A2D30",
    "Jeans": "151C2A",
    "JeansEdge": "26334A",
    "BootLeather": "1E1A16",
    "BootSole": "09090A",
    "Bandage": "B6A899",
    "BandageDark": "746B61",
    "Patch": "99743A",
    "Strap": "211B18",
    "StrapEdge": "433127",
    "EyeWhite": "8F8780",
    "Eye": "141317",
    "Metal": "58514A",
    "Ground": "171B1B",
}

# Existing Unity compatibility group names. More granular Blender objects map
# back to one of these values through the bp_sprite_part custom property.
SPRITE_PARTS = (
    "Body",
    "LeftUpperArm",
    "LeftLowerArm",
    "RightUpperArm",
    "RightLowerArm",
    "LeftUpperLeg",
    "LeftLowerLeg",
    "RightUpperLeg",
    "RightLowerLeg",
)

REQUIRED_BODY_OBJECTS = (
    "GEO_Head",
    "GEO_Neck",
    "GEO_Torso",
    "GEO_Pelvis",
    "GEO_UpperArm.L",
    "GEO_Forearm.L",
    "GEO_Hand.L",
    "GEO_UpperArm.R",
    "GEO_Forearm.R",
    "GEO_Hand.R",
    "GEO_Thigh.L",
    "GEO_Shin.L",
    "GEO_Foot.L",
    "GEO_Thigh.R",
    "GEO_Shin.R",
    "GEO_Foot.R",
)

REQUIRED_BONES = (
    "root",
    "pelvis",
    "spine",
    "chest",
    "neck",
    "head",
    "clavicle.L",
    "upper_arm.L",
    "forearm.L",
    "hand.L",
    "clavicle.R",
    "upper_arm.R",
    "forearm.R",
    "hand.R",
    "thigh.L",
    "shin.L",
    "foot.L",
    "thigh.R",
    "shin.R",
    "foot.R",
)


@dataclass(frozen=True)
class BuildConfig:
    output: Path
    preview: Path | None
    manifest: Path | None
    glb: Path | None
    fbx: Path | None
    height: float
    seed: int
    pose: str


@dataclass(frozen=True)
class BoneSpec:
    name: str
    head: Vector
    tail: Vector
    parent: str | None = None
    connected: bool = False
    deform: bool = True


@dataclass
class PartRecord:
    obj: bpy.types.Object
    role: str
    bone: str
    sprite_part: str
    side: str


@dataclass
class BuildResult:
    root: bpy.types.Object
    rig: bpy.types.Object
    collections: dict[str, bpy.types.Collection]
    materials: dict[str, bpy.types.Material]
    parts: list[PartRecord] = field(default_factory=list)
    presentation_objects: list[bpy.types.Object] = field(default_factory=list)

    @property
    def export_objects(self) -> list[bpy.types.Object]:
        return [self.root, self.rig, *(record.obj for record in self.parts)]


@dataclass(frozen=True)
class ValidationReport:
    object_count: int
    mesh_count: int
    triangle_count: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]


def parse_args() -> BuildConfig:
    """Parse only arguments placed after Blender's conventional `--`."""

    user_args: list[str] = []
    if "--" in sys.argv:
        user_args = sys.argv[sys.argv.index("--") + 1 :]

    parser = argparse.ArgumentParser(
        description=(
            "Generate the modular low-poly Bar Promenade hero in Blender."
        )
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
        help="Destination .blend file.",
    )
    parser.add_argument(
        "--preview",
        type=Path,
        help="Optional PNG path; triggers a portrait render.",
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        help="Optional JSON manifest with objects, mappings and bounds.",
    )
    parser.add_argument(
        "--glb",
        type=Path,
        help="Optional selection-only GLB export path.",
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        help="Optional selection-only FBX export path.",
    )
    parser.add_argument(
        "--height",
        type=float,
        default=CANONICAL_HEIGHT,
        help="Visible crown-to-ground height in metres (default: 1.75).",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=DEFAULT_SEED,
        help="Seed for restrained asymmetric hair variation.",
    )
    parser.add_argument(
        "--pose",
        choices=("relaxed", "apose"),
        default="relaxed",
        help="Canonical relaxed stance or symmetric animation A-pose.",
    )
    args = parser.parse_args(user_args)

    if not 1.40 <= args.height <= 2.10:
        parser.error("--height must be between 1.40 and 2.10 metres")

    return BuildConfig(
        output=resolve_path(args.output),
        preview=resolve_optional_path(args.preview),
        manifest=resolve_optional_path(args.manifest),
        glb=resolve_optional_path(args.glb),
        fbx=resolve_optional_path(args.fbx),
        height=args.height,
        seed=args.seed,
        pose=args.pose,
    )


def resolve_path(path: Path) -> Path:
    if not path.is_absolute():
        path = REPO_ROOT / path
    return path.resolve()


def resolve_optional_path(path: Path | None) -> Path | None:
    return resolve_path(path) if path is not None else None


def srgb_channel_to_linear(value: float) -> float:
    if value <= 0.04045:
        return value / 12.92
    return ((value + 0.055) / 1.055) ** 2.4


def hex_to_linear_rgba(value: str) -> tuple[float, float, float, float]:
    value = value.lstrip("#")
    if len(value) != 6:
        raise ValueError(f"Expected six-digit RGB hex, got {value!r}")
    srgb = tuple(int(value[index : index + 2], 16) / 255.0 for index in (0, 2, 4))
    return (
        srgb_channel_to_linear(srgb[0]),
        srgb_channel_to_linear(srgb[1]),
        srgb_channel_to_linear(srgb[2]),
        1.0,
    )


def create_material(
    name: str,
    color_hex: str,
    roughness: float = 0.88,
    metallic: float = 0.0,
) -> bpy.types.Material:
    material = bpy.data.materials.new(f"MAT_{name}")
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", DeprecationWarning)
        material.use_nodes = True
    rgba = hex_to_linear_rgba(color_hex)
    material.diffuse_color = rgba
    material["bp_palette_hex"] = color_hex.upper()
    material["bp_generator_version"] = GENERATOR_VERSION

    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled is None:
        raise RuntimeError(f"MAT_{name} has no Principled BSDF node")

    base_color = principled.inputs.get("Base Color")
    if base_color is not None:
        base_color.default_value = rgba
    roughness_input = principled.inputs.get("Roughness")
    if roughness_input is not None:
        roughness_input.default_value = roughness
    metallic_input = principled.inputs.get("Metallic")
    if metallic_input is not None:
        metallic_input.default_value = metallic
    specular_input = (
        principled.inputs.get("Specular IOR Level")
        or principled.inputs.get("Specular")
    )
    if specular_input is not None:
        specular_input.default_value = 0.24
    return material


def make_box_geometry(
    center: Vector,
    dimensions: Vector,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    half = dimensions * 0.5
    vertices = [
        center + Vector((x * half.x, y * half.y, z * half.z))
        for z in (-1.0, 1.0)
        for y in (-1.0, 1.0)
        for x in (-1.0, 1.0)
    ]
    faces = [
        (0, 2, 3, 1),
        (4, 5, 7, 6),
        (0, 1, 5, 4),
        (2, 6, 7, 3),
        (0, 4, 6, 2),
        (1, 3, 7, 5),
    ]
    return vertices, faces


def segment_basis(start: Vector, end: Vector) -> tuple[Vector, Vector, Vector]:
    axis_z = end - start
    if axis_z.length <= 1e-6:
        raise ValueError("Segment endpoints must not coincide")
    axis_z.normalize()

    reference = Vector((0.0, 1.0, 0.0))
    if abs(axis_z.dot(reference)) > 0.96:
        reference = Vector((1.0, 0.0, 0.0))
    axis_x = reference.cross(axis_z).normalized()
    axis_y = axis_z.cross(axis_x).normalized()
    return axis_x, axis_y, axis_z


def make_tapered_box_between(
    start: Vector,
    end: Vector,
    start_width: float,
    start_depth: float,
    end_width: float,
    end_depth: float,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    axis_x, axis_y, _ = segment_basis(start, end)
    vertices: list[Vector] = []
    for center, width, depth in (
        (start, start_width, start_depth),
        (end, end_width, end_depth),
    ):
        half_x = width * 0.5
        half_y = depth * 0.5
        vertices.extend(
            (
                center - axis_x * half_x - axis_y * half_y,
                center + axis_x * half_x - axis_y * half_y,
                center + axis_x * half_x + axis_y * half_y,
                center - axis_x * half_x + axis_y * half_y,
            )
        )
    faces = [
        (0, 3, 2, 1),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7),
    ]
    return vertices, faces


def make_frustum_between(
    start: Vector,
    end: Vector,
    radius_start: float,
    radius_end: float,
    sides: int = 8,
    depth_scale: float = 0.88,
    phase: float = math.pi / 8.0,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    if sides < 3:
        raise ValueError("A frustum needs at least three sides")
    axis_x, axis_y, _ = segment_basis(start, end)
    vertices: list[Vector] = []
    for center, radius in ((start, radius_start), (end, radius_end)):
        for index in range(sides):
            angle = phase + index * math.tau / sides
            vertices.append(
                center
                + axis_x * (math.cos(angle) * radius)
                + axis_y * (math.sin(angle) * radius * depth_scale)
            )

    faces: list[tuple[int, ...]] = []
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple(range(sides, sides * 2)))
    for index in range(sides):
        following = (index + 1) % sides
        faces.append(
            (
                index,
                following,
                sides + following,
                sides + index,
            )
        )
    return vertices, faces


def make_ringed_ellipsoid(
    center: Vector,
    rings: Sequence[tuple[float, float, float, float]],
    sides: int = 10,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    """Build an angular head/cap from (z, radius_x, radius_y, y_offset)."""

    if len(rings) < 2:
        raise ValueError("A ringed ellipsoid needs at least two rings")

    vertices: list[Vector] = []
    for z, radius_x, radius_y, y_offset in rings:
        for index in range(sides):
            angle = index * math.tau / sides
            vertices.append(
                Vector(
                    (
                        center.x + math.cos(angle) * radius_x,
                        center.y + y_offset + math.sin(angle) * radius_y,
                        z,
                    )
                )
            )

    bottom_center_index = len(vertices)
    vertices.append(
        Vector((center.x, center.y + rings[0][3], rings[0][0]))
    )
    top_center_index = len(vertices)
    vertices.append(
        Vector((center.x, center.y + rings[-1][3], rings[-1][0]))
    )

    faces: list[tuple[int, ...]] = []
    for ring_index in range(len(rings) - 1):
        lower = ring_index * sides
        upper = (ring_index + 1) * sides
        for side_index in range(sides):
            following = (side_index + 1) % sides
            faces.append(
                (
                    lower + side_index,
                    lower + following,
                    upper + following,
                    upper + side_index,
                )
            )
    for index in range(sides):
        following = (index + 1) % sides
        faces.append((bottom_center_index, following, index))
        top_base = (len(rings) - 1) * sides
        faces.append(
            (
                top_center_index,
                top_base + index,
                top_base + following,
            )
        )
    return vertices, faces


def make_ellipsoid_geometry(
    center: Vector,
    radii: Vector,
    segments: int = 8,
    ring_count: int = 5,
    orientation: Quaternion | None = None,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    if segments < 3 or ring_count < 2:
        raise ValueError("Ellipsoid resolution is too low")
    orientation = orientation or Quaternion()

    vertices: list[Vector] = []
    for ring_index in range(1, ring_count):
        latitude = -math.pi / 2.0 + ring_index * math.pi / ring_count
        cos_latitude = math.cos(latitude)
        for segment_index in range(segments):
            longitude = segment_index * math.tau / segments
            local = Vector(
                (
                    math.cos(longitude) * cos_latitude * radii.x,
                    math.sin(longitude) * cos_latitude * radii.y,
                    math.sin(latitude) * radii.z,
                )
            )
            vertices.append(center + orientation @ local)

    bottom_index = len(vertices)
    vertices.append(center + orientation @ Vector((0.0, 0.0, -radii.z)))
    top_index = len(vertices)
    vertices.append(center + orientation @ Vector((0.0, 0.0, radii.z)))

    faces: list[tuple[int, ...]] = []
    ring_total = ring_count - 1
    for ring_index in range(ring_total - 1):
        lower = ring_index * segments
        upper = (ring_index + 1) * segments
        for segment_index in range(segments):
            following = (segment_index + 1) % segments
            faces.append(
                (
                    lower + segment_index,
                    lower + following,
                    upper + following,
                    upper + segment_index,
                )
            )
    for segment_index in range(segments):
        following = (segment_index + 1) % segments
        faces.append((bottom_index, following, segment_index))
        last_ring = (ring_total - 1) * segments
        faces.append(
            (
                top_index,
                last_ring + segment_index,
                last_ring + following,
            )
        )
    return vertices, faces


def make_boot_wedge(
    center_x: float,
    back_y: float,
    toe_y: float,
    bottom_z: float,
    ankle_z: float,
    toe_top_z: float,
    width: float,
) -> tuple[list[Vector], list[tuple[int, ...]]]:
    half_width = width * 0.5
    top_half_width = half_width * 0.82
    vertices = [
        Vector((center_x - half_width, toe_y, bottom_z)),
        Vector((center_x + half_width, toe_y, bottom_z)),
        Vector((center_x + half_width, back_y, bottom_z)),
        Vector((center_x - half_width, back_y, bottom_z)),
        Vector(
            (
                center_x - top_half_width,
                toe_y + width * 0.10,
                toe_top_z,
            )
        ),
        Vector(
            (
                center_x + top_half_width,
                toe_y + width * 0.10,
                toe_top_z,
            )
        ),
        Vector(
            (
                center_x + top_half_width,
                back_y - width * 0.035,
                ankle_z,
            )
        ),
        Vector(
            (
                center_x - top_half_width,
                back_y - width * 0.035,
                ankle_z,
            )
        ),
    ]
    faces = [
        (0, 3, 2, 1),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7),
    ]
    return vertices, faces


def mesh_bounds_world(obj: bpy.types.Object) -> tuple[Vector, Vector]:
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    if not points:
        raise RuntimeError(f"{obj.name} has no vertices")
    minimum = Vector(
        (
            min(point.x for point in points),
            min(point.y for point in points),
            min(point.z for point in points),
        )
    )
    maximum = Vector(
        (
            max(point.x for point in points),
            max(point.y for point in points),
            max(point.z for point in points),
        )
    )
    return minimum, maximum


def object_center_world(obj: bpy.types.Object) -> Vector:
    minimum, maximum = mesh_bounds_world(obj)
    return (minimum + maximum) * 0.5


def look_at(obj: bpy.types.Object, target: Vector) -> None:
    direction = target - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


class CharacterBuilder:
    def __init__(self, config: BuildConfig):
        self.config = config
        self.scale = config.height / CANONICAL_HEIGHT
        self.rng = random.Random(config.seed)
        self.result: BuildResult | None = None
        self.points: dict[str, Vector] = {}
        self.bone_heads: dict[str, Vector] = {}

    def v(self, x: float, y: float, z: float) -> Vector:
        return Vector((x, y, z)) * self.scale

    def d(self, value: float) -> float:
        return value * self.scale

    def build(self) -> BuildResult:
        self.reset_scene()
        collections = self.create_collections()
        materials = {
            name: create_material(name, color)
            for name, color in PALETTE_HEX.items()
        }
        self.points = self.create_pose_points()
        root = self.create_root(collections["export"])
        bone_specs = self.create_bone_specs()
        rig = self.create_armature(
            collections["rig"],
            root,
            bone_specs,
        )
        self.bone_heads = {spec.name: spec.head for spec in bone_specs}
        self.result = BuildResult(
            root=root,
            rig=rig,
            collections=collections,
            materials=materials,
        )

        self.build_core_anatomy()
        self.build_clothing()
        self.build_face_and_hair()
        self.build_asymmetric_details()
        self.build_presentation()
        self.configure_scene_metadata()
        return self.result

    def reset_scene(self) -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1.0
        scene.unit_settings.length_unit = "METERS"
        scene.render.resolution_x = 640
        scene.render.resolution_y = 800
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = "PNG"
        scene.render.film_transparent = False
        scene.render.image_settings.color_mode = "RGBA"
        scene.render.image_settings.color_depth = "8"
        scene.frame_start = 1
        scene.frame_end = 32
        scene.frame_set(1)

        for engine_name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
            try:
                scene.render.engine = engine_name
                break
            except TypeError:
                continue

        try:
            scene.view_settings.look = "AgX - Medium High Contrast"
        except TypeError:
            pass

        scene.world = bpy.data.worlds.new("WORLD_PlayerPreview")
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", DeprecationWarning)
            scene.world.use_nodes = True
        background = scene.world.node_tree.nodes.get("Background")
        if background is not None:
            background.inputs["Color"].default_value = (
                0.010,
                0.014,
                0.014,
                1.0,
            )
            background.inputs["Strength"].default_value = 0.22

    def create_collections(self) -> dict[str, bpy.types.Collection]:
        scene_root = bpy.context.scene.collection
        player = bpy.data.collections.new("BP_Player3D")
        scene_root.children.link(player)

        collections: dict[str, bpy.types.Collection] = {"player": player}
        for key, name in (
            ("export", "EXPORT_Player"),
            ("rig", "RIG_Player"),
            ("core", "GEO_CoreParts"),
            ("clothing", "GEO_Clothing"),
            ("details", "GEO_Details"),
            ("presentation", "PRESENTATION_Player"),
        ):
            collection = bpy.data.collections.new(name)
            player.children.link(collection)
            collections[key] = collection
        return collections

    def create_root(self, collection: bpy.types.Collection) -> bpy.types.Object:
        root = bpy.data.objects.new("ROOT_Player", None)
        collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root.empty_display_size = self.d(0.16)
        root["bp_export"] = True
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design"] = "Bar Promenade locked player prototype"
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_canonical_height_m"] = self.config.height
        root["bp_pose"] = self.config.pose
        root["bp_seed"] = self.config.seed
        return root

    def create_pose_points(self) -> dict[str, Vector]:
        # Heights are derived from the current 64x96/48-PPU puppet:
        # shoulder 1.292, elbow .958, hip .750 and knee .354 metres.
        points = {
            "hip.L": self.v(0.083, 0.012, 0.750),
            "hip.R": self.v(-0.083, -0.004, 0.750),
            "knee.L": self.v(0.103, -0.012, 0.354),
            "knee.R": self.v(-0.103, 0.012, 0.354),
            "ankle.L": self.v(0.112, -0.026, 0.095),
            "ankle.R": self.v(-0.112, 0.018, 0.095),
            "toe.L": self.v(0.112, -0.230, 0.045),
            "toe.R": self.v(-0.112, -0.188, 0.045),
            "shoulder.L": self.v(0.208, -0.004, 1.292),
            "shoulder.R": self.v(-0.208, 0.004, 1.292),
        }
        if self.config.pose == "apose":
            points.update(
                {
                    "elbow.L": self.v(0.470, -0.010, 1.175),
                    "wrist.L": self.v(0.680, -0.018, 1.075),
                    "hand.L": self.v(0.755, -0.022, 1.035),
                    "elbow.R": self.v(-0.470, -0.010, 1.175),
                    "wrist.R": self.v(-0.680, -0.018, 1.075),
                    "hand.R": self.v(-0.755, -0.022, 1.035),
                }
            )
        else:
            points.update(
                {
                    "elbow.L": self.v(0.267, -0.018, 0.958),
                    "wrist.L": self.v(0.252, -0.050, 0.690),
                    "hand.L": self.v(0.258, -0.064, 0.590),
                    "elbow.R": self.v(-0.255, -0.006, 0.958),
                    "wrist.R": self.v(-0.247, -0.042, 0.690),
                    "hand.R": self.v(-0.252, -0.056, 0.590),
                }
            )
        return points

    def create_bone_specs(self) -> list[BoneSpec]:
        p = self.points
        return [
            BoneSpec("root", self.v(0, 0, 0), self.v(0, 0, 0.18), deform=False),
            BoneSpec(
                "pelvis",
                self.v(0, 0.008, 0.700),
                self.v(0, 0.004, 0.900),
                "root",
            ),
            BoneSpec(
                "spine",
                self.v(0, 0.004, 0.900),
                self.v(0, 0.000, 1.120),
                "pelvis",
                True,
            ),
            BoneSpec(
                "chest",
                self.v(0, 0.000, 1.120),
                self.v(0, -0.010, 1.335),
                "spine",
                True,
            ),
            BoneSpec(
                "neck",
                self.v(0, -0.010, 1.335),
                self.v(0, -0.025, 1.430),
                "chest",
                True,
            ),
            BoneSpec(
                "head",
                self.v(0, -0.025, 1.430),
                self.v(0, -0.050, 1.675),
                "neck",
                True,
            ),
            BoneSpec(
                "clavicle.L",
                self.v(0, -0.008, 1.325),
                p["shoulder.L"],
                "chest",
                deform=False,
            ),
            BoneSpec(
                "upper_arm.L",
                p["shoulder.L"],
                p["elbow.L"],
                "clavicle.L",
                True,
            ),
            BoneSpec(
                "forearm.L",
                p["elbow.L"],
                p["wrist.L"],
                "upper_arm.L",
                True,
            ),
            BoneSpec(
                "hand.L",
                p["wrist.L"],
                p["hand.L"],
                "forearm.L",
                True,
            ),
            BoneSpec(
                "clavicle.R",
                self.v(0, -0.008, 1.325),
                p["shoulder.R"],
                "chest",
                deform=False,
            ),
            BoneSpec(
                "upper_arm.R",
                p["shoulder.R"],
                p["elbow.R"],
                "clavicle.R",
                True,
            ),
            BoneSpec(
                "forearm.R",
                p["elbow.R"],
                p["wrist.R"],
                "upper_arm.R",
                True,
            ),
            BoneSpec(
                "hand.R",
                p["wrist.R"],
                p["hand.R"],
                "forearm.R",
                True,
            ),
            BoneSpec(
                "thigh.L",
                p["hip.L"],
                p["knee.L"],
                "pelvis",
            ),
            BoneSpec(
                "shin.L",
                p["knee.L"],
                p["ankle.L"],
                "thigh.L",
                True,
            ),
            BoneSpec(
                "foot.L",
                p["ankle.L"],
                p["toe.L"],
                "shin.L",
                True,
            ),
            BoneSpec(
                "thigh.R",
                p["hip.R"],
                p["knee.R"],
                "pelvis",
            ),
            BoneSpec(
                "shin.R",
                p["knee.R"],
                p["ankle.R"],
                "thigh.R",
                True,
            ),
            BoneSpec(
                "foot.R",
                p["ankle.R"],
                p["toe.R"],
                "shin.R",
                True,
            ),
        ]

    def create_armature(
        self,
        collection: bpy.types.Collection,
        root: bpy.types.Object,
        specs: Sequence[BoneSpec],
    ) -> bpy.types.Object:
        armature_data = bpy.data.armatures.new("RIG_Player_Data")
        rig = bpy.data.objects.new("RIG_Player", armature_data)
        collection.objects.link(rig)
        rig.parent = root
        rig.show_in_front = True
        rig.display_type = "WIRE"
        armature_data.display_type = "OCTAHEDRAL"
        rig["bp_export"] = True
        rig["bp_generator_version"] = GENERATOR_VERSION
        rig["bp_rig_style"] = "separate rigid meshes, one-bone weights"

        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        edit_bones: dict[str, bpy.types.EditBone] = {}
        for spec in specs:
            bone = armature_data.edit_bones.new(spec.name)
            bone.head = spec.head
            bone.tail = spec.tail
            bone.use_deform = spec.deform
            edit_bones[spec.name] = bone
        for spec in specs:
            if spec.parent is None:
                continue
            bone = edit_bones[spec.name]
            bone.parent = edit_bones[spec.parent]
            bone.use_connect = spec.connected
        bpy.ops.object.mode_set(mode="OBJECT")
        rig.select_set(False)
        return rig

    def add_part(
        self,
        name: str,
        geometry: tuple[list[Vector], list[tuple[int, ...]]],
        material_name: str,
        collection_name: str,
        bone: str,
        sprite_part: str,
        role: str,
        side: str = "Center",
        origin: Vector | None = None,
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("BuildResult has not been initialized")
        if sprite_part not in SPRITE_PARTS:
            raise ValueError(f"Unknown sprite compatibility part {sprite_part}")
        if bone not in self.bone_heads:
            raise ValueError(f"Unknown armature bone {bone}")

        vertices, faces = geometry
        origin = origin.copy() if origin is not None else self.bone_heads[bone].copy()
        local_vertices = [vertex - origin for vertex in vertices]

        mesh = bpy.data.meshes.new(f"{name}_Mesh")
        mesh.from_pydata(
            [tuple(vertex) for vertex in local_vertices],
            [],
            faces,
        )
        mesh.update(calc_edges=True)
        for polygon in mesh.polygons:
            polygon.use_smooth = False

        obj = bpy.data.objects.new(name, mesh)
        self.result.collections[collection_name].objects.link(obj)
        obj.location = origin
        obj.data.materials.append(self.result.materials[material_name])
        obj.parent = self.result.rig
        obj.matrix_parent_inverse = self.result.rig.matrix_world.inverted()

        group = obj.vertex_groups.new(name=bone)
        group.add(range(len(mesh.vertices)), 1.0, "REPLACE")

        triangulate = obj.modifiers.new("Triangulate", "TRIANGULATE")
        triangulate.quad_method = "BEAUTY"
        triangulate.ngon_method = "BEAUTY"
        armature = obj.modifiers.new("Armature", "ARMATURE")
        armature.object = self.result.rig
        armature.use_deform_preserve_volume = False

        obj["bp_export"] = True
        obj["bp_role"] = role
        obj["bp_bone"] = bone
        obj["bp_sprite_part"] = sprite_part
        obj["bp_anatomical_side"] = side
        obj["bp_generator_version"] = GENERATOR_VERSION

        self.result.parts.append(
            PartRecord(
                obj=obj,
                role=role,
                bone=bone,
                sprite_part=sprite_part,
                side=side,
            )
        )
        return obj

    def build_core_anatomy(self) -> None:
        p = self.points

        # Body core: deliberately top-heavy and angular like the atlas.
        head_rings = tuple(
            (
                self.d(z),
                self.d(rx),
                self.d(ry),
                self.d(y_offset),
            )
            for z, rx, ry, y_offset in (
                (1.390, 0.072, 0.070, -0.005),
                (1.445, 0.116, 0.100, -0.010),
                (1.555, 0.148, 0.116, -0.015),
                (1.660, 0.142, 0.111, -0.010),
                (1.715, 0.086, 0.070, -0.003),
            )
        )
        self.add_part(
            "GEO_Head",
            make_ringed_ellipsoid(self.v(0, -0.020, 0), head_rings, 10),
            "Skin",
            "core",
            "head",
            "Body",
            "body_part",
            origin=self.bone_heads["head"],
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between(
                self.v(0, -0.008, 1.325),
                self.v(0, -0.020, 1.455),
                self.d(0.073),
                self.d(0.066),
                8,
                0.84,
            ),
            "SkinShadow",
            "core",
            "neck",
            "Body",
            "body_part",
        )
        self.add_part(
            "GEO_Torso",
            make_tapered_box_between(
                self.v(0, 0.010, 0.785),
                self.v(0, -0.004, 1.330),
                self.d(0.278),
                self.d(0.170),
                self.d(0.338),
                self.d(0.185),
            ),
            "Shirt",
            "core",
            "chest",
            "Body",
            "body_part",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box_between(
                self.v(0, 0.014, 0.690),
                self.v(0, 0.010, 0.835),
                self.d(0.275),
                self.d(0.165),
                self.d(0.300),
                self.d(0.175),
            ),
            "Jeans",
            "core",
            "pelvis",
            "Body",
            "body_part",
        )

        for side in ("L", "R"):
            anatomical = "Left" if side == "L" else "Right"
            upper_sprite = f"{anatomical}UpperArm"
            lower_sprite = f"{anatomical}LowerArm"
            upper_leg_sprite = f"{anatomical}UpperLeg"
            lower_leg_sprite = f"{anatomical}LowerLeg"

            shoulder = p[f"shoulder.{side}"]
            elbow = p[f"elbow.{side}"]
            wrist = p[f"wrist.{side}"]
            hand_tail = p[f"hand.{side}"]
            hip = p[f"hip.{side}"]
            knee = p[f"knee.{side}"]
            ankle = p[f"ankle.{side}"]

            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(
                    shoulder,
                    elbow,
                    self.d(0.052),
                    self.d(0.044),
                    8,
                ),
                "SkinShadow",
                "core",
                f"upper_arm.{side}",
                upper_sprite,
                "body_part",
                anatomical,
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(
                    elbow,
                    wrist,
                    self.d(0.048),
                    self.d(0.038),
                    8,
                ),
                "Skin",
                "core",
                f"forearm.{side}",
                lower_sprite,
                "body_part",
                anatomical,
            )
            hand_axis = hand_tail - wrist
            hand_rotation = hand_axis.to_track_quat("Z", "Y")
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid_geometry(
                    (wrist + hand_tail) * 0.5,
                    self.v(0.043, 0.034, 0.064),
                    8,
                    4,
                    hand_rotation,
                ),
                "Skin",
                "core",
                f"hand.{side}",
                lower_sprite,
                "body_part",
                anatomical,
            )
            self.add_part(
                f"GEO_Thigh.{side}",
                make_frustum_between(
                    hip,
                    knee,
                    self.d(0.092),
                    self.d(0.078),
                    8,
                    0.82,
                ),
                "Jeans",
                "core",
                f"thigh.{side}",
                upper_leg_sprite,
                "body_part",
                anatomical,
            )
            self.add_part(
                f"GEO_Shin.{side}",
                make_frustum_between(
                    knee,
                    ankle,
                    self.d(0.080),
                    self.d(0.067),
                    8,
                    0.82,
                ),
                "Jeans",
                "core",
                f"shin.{side}",
                lower_leg_sprite,
                "body_part",
                anatomical,
            )

            center_x = ankle.x
            foot_forward = p[f"toe.{side}"].y
            back_y = ankle.y + self.d(0.072)
            self.add_part(
                f"GEO_Foot.{side}",
                make_boot_wedge(
                    center_x,
                    back_y,
                    foot_forward,
                    self.d(0.018),
                    self.d(0.175),
                    self.d(0.095),
                    self.d(0.145),
                ),
                "BootLeather",
                "core",
                f"foot.{side}",
                lower_leg_sprite,
                "body_part",
                anatomical,
                origin=ankle,
            )

    def build_clothing(self) -> None:
        p = self.points
        self.add_part(
            "CLO_JacketBody",
            make_tapered_box_between(
                self.v(0, 0.014, 0.800),
                self.v(0, -0.001, 1.335),
                self.d(0.322),
                self.d(0.198),
                self.d(0.392),
                self.d(0.218),
            ),
            "Jacket",
            "clothing",
            "chest",
            "Body",
            "clothing",
        )
        self.add_part(
            "CLO_ShirtFront",
            make_box_geometry(
                self.v(0, -0.118, 1.075),
                self.v(0.174, 0.020, 0.465),
            ),
            "Shirt",
            "clothing",
            "chest",
            "Body",
            "clothing",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            anatomical = "Left" if side == "L" else "Right"
            upper_sprite = f"{anatomical}UpperArm"
            lower_sprite = f"{anatomical}LowerArm"
            shoulder = p[f"shoulder.{side}"]
            elbow = p[f"elbow.{side}"]
            sleeve_end = shoulder.lerp(elbow, 0.84)
            cuff_start = shoulder.lerp(elbow, 0.76)

            self.add_part(
                f"CLO_JacketSleeve.{side}",
                make_frustum_between(
                    shoulder,
                    sleeve_end,
                    self.d(0.080),
                    self.d(0.068),
                    8,
                    0.88,
                ),
                "Jacket",
                "clothing",
                f"upper_arm.{side}",
                upper_sprite,
                "clothing",
                anatomical,
            )
            self.add_part(
                f"CLO_JacketCuff.{side}",
                make_frustum_between(
                    cuff_start,
                    elbow.lerp(p[f"wrist.{side}"], 0.06),
                    self.d(0.073),
                    self.d(0.067),
                    8,
                    0.88,
                ),
                "JacketEdge",
                "clothing",
                f"upper_arm.{side}",
                upper_sprite,
                "clothing",
                anatomical,
            )

            # Open shirt panels and chest pockets remain individually editable.
            panel_center = self.v(sign * 0.112, -0.125, 1.070)
            self.add_part(
                f"CLO_JacketPanel.{side}",
                make_box_geometry(
                    panel_center,
                    self.v(0.105, 0.020, 0.490),
                ),
                "Jacket",
                "clothing",
                "chest",
                "Body",
                "clothing",
                anatomical,
            )
            pocket_center = self.v(sign * 0.118, -0.140, 1.105)
            self.add_part(
                f"ACC_JacketPocket.{side}",
                make_box_geometry(
                    pocket_center,
                    self.v(0.084, 0.014, 0.088),
                ),
                "JacketDark",
                "details",
                "chest",
                "Body",
                "clothing_detail",
                anatomical,
            )
            self.add_part(
                f"ACC_JacketPocketFlap.{side}",
                make_box_geometry(
                    pocket_center + self.v(0, -0.010, 0.046),
                    self.v(0.092, 0.012, 0.022),
                ),
                "JacketEdge",
                "details",
                "chest",
                "Body",
                "clothing_detail",
                anatomical,
            )

            # Jeans cuffs and boot soles make the heavy-work-boot silhouette.
            ankle = p[f"ankle.{side}"]
            knee = p[f"knee.{side}"]
            cuff_top = ankle.lerp(knee, 0.18)
            self.add_part(
                f"ACC_JeansCuff.{side}",
                make_frustum_between(
                    ankle,
                    cuff_top,
                    self.d(0.073),
                    self.d(0.080),
                    8,
                    0.82,
                ),
                "JeansEdge",
                "details",
                f"shin.{side}",
                lower_sprite,
                "clothing_detail",
                anatomical,
            )
            toe = p[f"toe.{side}"]
            sole_center_y = (toe.y + ankle.y + self.d(0.065)) * 0.5
            sole_length = abs(toe.y - (ankle.y + self.d(0.065)))
            self.add_part(
                f"ACC_BootSole.{side}",
                make_box_geometry(
                    Vector((ankle.x, sole_center_y, self.d(0.010))),
                    self.v(0.158, sole_length, 0.020),
                ),
                "BootSole",
                "details",
                f"foot.{side}",
                lower_sprite,
                "clothing_detail",
                anatomical,
                origin=ankle,
            )

        # Two angular lapels frame the charcoal undershirt.
        self.add_part(
            "CLO_Lapel.L",
            make_tapered_box_between(
                self.v(0.064, -0.143, 1.318),
                self.v(0.105, -0.143, 1.105),
                self.d(0.053),
                self.d(0.012),
                self.d(0.036),
                self.d(0.012),
            ),
            "JacketEdge",
            "clothing",
            "chest",
            "Body",
            "clothing_detail",
            "Left",
        )
        self.add_part(
            "CLO_Lapel.R",
            make_tapered_box_between(
                self.v(-0.064, -0.143, 1.318),
                self.v(-0.105, -0.143, 1.105),
                self.d(0.053),
                self.d(0.012),
                self.d(0.036),
                self.d(0.012),
            ),
            "JacketEdge",
            "clothing",
            "chest",
            "Body",
            "clothing_detail",
            "Right",
        )

    def build_face_and_hair(self) -> None:
        # All facial pieces are separate and head-weighted, making future
        # Neutral/HalfBlink/ClosedBlink/Watchful/Tense shape work practical.
        face_y = self.d(-0.147)
        for side, sign in (("L", 1.0), ("R", -1.0)):
            anatomical = "Left" if side == "L" else "Right"
            eye_center = Vector((self.d(sign * 0.052), face_y, self.d(1.590)))
            self.add_part(
                f"GEO_Eye.{side}",
                make_box_geometry(
                    eye_center,
                    self.v(0.047, 0.010, 0.018),
                ),
                "EyeWhite",
                "details",
                "head",
                "Body",
                "facial_detail",
                anatomical,
            )
            self.add_part(
                f"ACC_Pupil.{side}",
                make_box_geometry(
                    eye_center + self.v(0, -0.008, -0.001),
                    self.v(0.014, 0.008, 0.014),
                ),
                "Eye",
                "details",
                "head",
                "Body",
                "facial_detail",
                anatomical,
            )
            brow_z = 1.627 + (0.004 if side == "L" else -0.002)
            self.add_part(
                f"ACC_Brow.{side}",
                make_tapered_box_between(
                    self.v(sign * 0.082, -0.154, brow_z),
                    self.v(sign * 0.027, -0.157, brow_z - 0.006),
                    self.d(0.014),
                    self.d(0.008),
                    self.d(0.010),
                    self.d(0.008),
                ),
                "Hair",
                "details",
                "head",
                "Body",
                "facial_detail",
                anatomical,
            )
            self.add_part(
                f"ACC_UnderEye.{side}",
                make_box_geometry(
                    self.v(sign * 0.052, -0.151, 1.568),
                    self.v(0.052, 0.007, 0.012),
                ),
                "SkinShadow",
                "details",
                "head",
                "Body",
                "facial_detail",
                anatomical,
            )
            self.add_part(
                f"GEO_Ear.{side}",
                make_ellipsoid_geometry(
                    self.v(sign * 0.151, -0.022, 1.565),
                    self.v(0.024, 0.020, 0.049),
                    6,
                    3,
                ),
                "SkinShadow",
                "details",
                "head",
                "Body",
                "body_detail",
                anatomical,
            )

        nose_vertices = [
            self.v(-0.025, -0.145, 1.590),
            self.v(0.025, -0.145, 1.590),
            self.v(0.022, -0.145, 1.525),
            self.v(-0.022, -0.145, 1.525),
            self.v(0.000, -0.188, 1.542),
        ]
        nose_faces = [
            (4, 1, 0),
            (4, 2, 1),
            (4, 3, 2),
            (4, 0, 3),
            (1, 2, 3, 0),
        ]
        self.add_part(
            "GEO_Nose",
            (nose_vertices, nose_faces),
            "SkinShadow",
            "details",
            "head",
            "Body",
            "facial_detail",
        )
        self.add_part(
            "ACC_Mouth",
            make_box_geometry(
                self.v(0.006, -0.151, 1.477),
                self.v(0.084, 0.008, 0.012),
            ),
            "SkinDark",
            "details",
            "head",
            "Body",
            "facial_detail",
        )
        self.add_part(
            "ACC_Stubble",
            make_tapered_box_between(
                self.v(0, -0.136, 1.405),
                self.v(0, -0.149, 1.492),
                self.d(0.086),
                self.d(0.010),
                self.d(0.126),
                self.d(0.010),
            ),
            "SkinShadow",
            "details",
            "head",
            "Body",
            "facial_detail",
        )

        hair_rings = tuple(
            (
                self.d(z),
                self.d(rx),
                self.d(ry),
                self.d(y_offset),
            )
            for z, rx, ry, y_offset in (
                (1.545, 0.145, 0.105, 0.018),
                (1.625, 0.162, 0.126, 0.014),
                (1.700, 0.145, 0.116, 0.010),
                (1.742, 0.086, 0.072, 0.005),
                (1.750, 0.018, 0.016, 0.002),
            )
        )
        self.add_part(
            "GEO_HairCap",
            make_ringed_ellipsoid(self.v(0, -0.012, 0), hair_rings, 10),
            "Hair",
            "details",
            "head",
            "Body",
            "hair",
        )

        # Fixed asymmetric anchors plus seeded millimetre-scale jitter make a
        # messy silhouette without ever mirroring the completed character.
        tuft_specs = (
            ((-0.118, -0.095, 1.665), (-0.145, -0.135, 1.620), 0.030),
            ((-0.070, -0.125, 1.705), (-0.082, -0.160, 1.650), 0.026),
            ((-0.020, -0.135, 1.720), (-0.030, -0.172, 1.660), 0.028),
            ((0.030, -0.132, 1.716), (0.020, -0.174, 1.642), 0.030),
            ((0.082, -0.115, 1.690), (0.102, -0.158, 1.620), 0.030),
            ((0.132, -0.070, 1.650), (0.159, -0.102, 1.590), 0.026),
            ((-0.145, -0.020, 1.670), (-0.170, -0.012, 1.620), 0.026),
            ((0.150, 0.012, 1.680), (0.176, 0.020, 1.625), 0.025),
            ((-0.098, 0.088, 1.690), (-0.112, 0.125, 1.655), 0.028),
            ((0.090, 0.095, 1.705), (0.105, 0.130, 1.660), 0.027),
            ((-0.025, 0.112, 1.730), (-0.030, 0.145, 1.690), 0.026),
            ((0.045, 0.105, 1.725), (0.060, 0.140, 1.680), 0.026),
            ((-0.060, -0.015, 1.724), (-0.080, -0.025, 1.734), 0.025),
            ((0.068, -0.010, 1.722), (0.085, -0.020, 1.734), 0.024),
        )
        for index, (base_tuple, tip_tuple, radius) in enumerate(tuft_specs, 1):
            jitter = self.v(
                self.rng.uniform(-0.004, 0.004),
                self.rng.uniform(-0.003, 0.003),
                self.rng.uniform(-0.002, 0.002),
            )
            base = self.v(*base_tuple) + jitter
            tip = self.v(*tip_tuple) + jitter * 0.4
            tip.z = min(tip.z, self.d(CANONICAL_HEIGHT))
            self.add_part(
                f"GEO_HairTuft.{index:02d}",
                make_frustum_between(
                    base,
                    tip,
                    self.d(radius),
                    self.d(0.006),
                    5,
                    0.72,
                    0.0,
                ),
                "HairHighlight" if index in (2, 7, 11) else "Hair",
                "details",
                "head",
                "Body",
                "hair",
            )

    def build_asymmetric_details(self) -> None:
        p = self.points

        # Physical LEFT forearm (+X): one pale shell and five darker wrap bands.
        bandage_start = p["elbow.L"].lerp(p["wrist.L"], 0.08)
        bandage_end = p["elbow.L"].lerp(p["wrist.L"], 0.86)
        self.add_part(
            "CLO_Bandage.L",
            make_frustum_between(
                bandage_start,
                bandage_end,
                self.d(0.055),
                self.d(0.045),
                8,
                0.89,
            ),
            "Bandage",
            "clothing",
            "forearm.L",
            "LeftLowerArm",
            "signature_detail",
            "Left",
        )
        for index, fraction in enumerate((0.15, 0.31, 0.48, 0.66, 0.82), 1):
            center = bandage_start.lerp(bandage_end, fraction)
            axis = (bandage_end - bandage_start).normalized()
            half_length = self.d(0.007)
            self.add_part(
                f"ACC_BandageWrap.{index:02d}.L",
                make_frustum_between(
                    center - axis * half_length,
                    center + axis * half_length,
                    self.d(0.057),
                    self.d(0.057),
                    8,
                    0.89,
                ),
                "BandageDark",
                "details",
                "forearm.L",
                "LeftLowerArm",
                "signature_detail",
                "Left",
            )

        # Physical RIGHT shoulder (-X): independently authored ochre patch.
        patch_center = p["shoulder.R"].lerp(p["elbow.R"], 0.18)
        patch_center += self.v(-0.032, -0.058, 0.006)
        self.add_part(
            "ACC_ShoulderPatch.R",
            make_box_geometry(
                patch_center,
                self.v(0.066, 0.018, 0.072),
            ),
            "Patch",
            "details",
            "upper_arm.R",
            "RightUpperArm",
            "signature_detail",
            "Right",
            origin=p["shoulder.R"],
        )

        # The source guarantees a diagonal strap but not a large visible bag.
        # Generate front/back ribbons and a small buckle, deliberately no pouch.
        strap_front_start = self.v(0.145, -0.151, 1.315)
        strap_front_end = self.v(-0.155, -0.126, 0.805)
        self.add_part(
            "ACC_StrapFront",
            make_tapered_box_between(
                strap_front_start,
                strap_front_end,
                self.d(0.045),
                self.d(0.016),
                self.d(0.050),
                self.d(0.016),
            ),
            "Strap",
            "details",
            "chest",
            "Body",
            "signature_detail",
        )
        self.add_part(
            "ACC_StrapBack",
            make_tapered_box_between(
                self.v(0.145, 0.126, 1.310),
                self.v(-0.155, 0.112, 0.805),
                self.d(0.045),
                self.d(0.016),
                self.d(0.050),
                self.d(0.016),
            ),
            "Strap",
            "details",
            "chest",
            "Body",
            "signature_detail",
        )
        self.add_part(
            "ACC_StrapShoulder",
            make_tapered_box_between(
                self.v(0.145, -0.145, 1.315),
                self.v(0.145, 0.125, 1.310),
                self.d(0.046),
                self.d(0.020),
                self.d(0.046),
                self.d(0.020),
            ),
            "Strap",
            "details",
            "chest",
            "Body",
            "signature_detail",
            "Left",
        )
        buckle_center = strap_front_start.lerp(strap_front_end, 0.52)
        buckle_center += self.v(0, -0.012, 0)
        self.add_part(
            "ACC_StrapBuckle",
            make_box_geometry(
                buckle_center,
                self.v(0.064, 0.018, 0.060),
            ),
            "Metal",
            "details",
            "chest",
            "Body",
            "accessory",
        )

    def build_presentation(self) -> None:
        if self.result is None:
            raise RuntimeError("BuildResult has not been initialized")
        collection = self.result.collections["presentation"]
        ground_geometry = make_box_geometry(
            self.v(0, 0.18, -0.035),
            self.v(3.4, 3.4, 0.060),
        )
        vertices, faces = ground_geometry
        mesh = bpy.data.meshes.new("GEO_PreviewGround_Mesh")
        mesh.from_pydata([tuple(vertex) for vertex in vertices], [], faces)
        mesh.update(calc_edges=True)
        ground = bpy.data.objects.new("GEO_PreviewGround", mesh)
        collection.objects.link(ground)
        ground.data.materials.append(self.result.materials["Ground"])
        ground["bp_export"] = False
        self.result.presentation_objects.append(ground)

        camera_data = bpy.data.cameras.new("CAM_PlayerPreview_Data")
        camera = bpy.data.objects.new("CAM_PlayerPreview", camera_data)
        collection.objects.link(camera)
        camera.location = self.v(2.65, -4.65, 2.10)
        camera_data.lens = 66.0
        camera_data.sensor_width = 36.0
        look_at(camera, self.v(0, -0.015, 0.91))
        camera["bp_export"] = False
        bpy.context.scene.camera = camera
        self.result.presentation_objects.append(camera)

        light_specs = (
            (
                "LGT_KeyWarm",
                self.v(-2.8, -3.6, 3.5),
                (1.0, 0.54, 0.38),
                720.0,
                self.d(2.2),
            ),
            (
                "LGT_FillCold",
                self.v(2.8, -2.2, 2.4),
                (0.29, 0.44, 0.62),
                470.0,
                self.d(2.4),
            ),
            (
                "LGT_RimMuted",
                self.v(0.2, 2.7, 3.0),
                (0.50, 0.16, 0.22),
                610.0,
                self.d(1.8),
            ),
        )
        for name, location, color, energy, size in light_specs:
            light_data = bpy.data.lights.new(f"{name}_Data", "AREA")
            light_data.energy = energy
            light_data.color = color
            light_data.shape = "DISK"
            light_data.size = size
            light = bpy.data.objects.new(name, light_data)
            collection.objects.link(light)
            light.location = location
            look_at(light, self.v(0, 0, 0.95))
            light["bp_export"] = False
            self.result.presentation_objects.append(light)

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-player-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_character_height_m"] = self.config.height
        scene["bp_pose"] = self.config.pose
        scene["bp_seed"] = self.config.seed
        scene["bp_runtime_integrated"] = False
        scene["bp_design_source"] = (
            "ArtSource/Player/PlayerDirectionalTurntable.png"
        )


def validate_manifold(obj: bpy.types.Object) -> None:
    mesh = obj.data
    bm = bmesh.new()
    try:
        bm.from_mesh(mesh)
        non_manifold = [edge for edge in bm.edges if not edge.is_manifold]
        if non_manifold:
            raise RuntimeError(
                f"{obj.name} has {len(non_manifold)} non-manifold edges"
            )
        signed_volume = bm.calc_volume(signed=True)
        if signed_volume <= 1e-10:
            raise RuntimeError(
                f"{obj.name} has inward or degenerate face winding "
                f"(signed volume {signed_volume:.9g})"
            )
    finally:
        bm.free()


def validate_result(
    config: BuildConfig,
    result: BuildResult,
) -> ValidationReport:
    # Parenting and modifier relationships were created through the data API;
    # force one dependency-graph update before reading world-space matrices.
    bpy.context.view_layer.update()
    errors: list[str] = []
    records_by_name = {record.obj.name: record for record in result.parts}

    if len(records_by_name) != len(result.parts):
        errors.append("Export mesh object names are not unique")
    for required_name in REQUIRED_BODY_OBJECTS:
        if required_name not in records_by_name:
            errors.append(f"Missing required body part {required_name}")
    for name in records_by_name:
        if name.endswith(".001"):
            errors.append(f"Unexpected Blender numeric suffix on {name}")

    required_meshes = [
        records_by_name[name].obj
        for name in REQUIRED_BODY_OBJECTS
        if name in records_by_name
    ]
    unique_data = {obj.data.as_pointer() for obj in required_meshes}
    if len(unique_data) != len(required_meshes):
        errors.append("Required body parts share mesh datablocks")

    rig_bones = result.rig.data.bones
    for bone_name in REQUIRED_BONES:
        bone = rig_bones.get(bone_name)
        if bone is None:
            errors.append(f"Missing required bone {bone_name}")
        elif bone.length <= 1e-5:
            errors.append(f"Bone {bone_name} has zero length")

    triangle_count = 0
    all_minima: list[Vector] = []
    all_maxima: list[Vector] = []
    for record in result.parts:
        obj = record.obj
        mesh = obj.data
        if obj.type != "MESH" or not mesh.vertices or not mesh.polygons:
            errors.append(f"{obj.name} is not a non-empty mesh")
            continue
        if obj.parent is not result.rig:
            errors.append(f"{obj.name} is not parented to RIG_Player")
        if record.sprite_part not in SPRITE_PARTS:
            errors.append(f"{obj.name} has invalid sprite-part mapping")
        if obj.get("bp_bone") != record.bone:
            errors.append(f"{obj.name} lost its bp_bone metadata")
        if len(mesh.materials) != 1 or mesh.materials[0] is None:
            errors.append(f"{obj.name} must use exactly one material")

        armature_modifiers = [
            modifier
            for modifier in obj.modifiers
            if modifier.type == "ARMATURE"
        ]
        if (
            len(armature_modifiers) != 1
            or armature_modifiers[0].object is not result.rig
        ):
            errors.append(f"{obj.name} needs one RIG_Player armature modifier")

        group = obj.vertex_groups.get(record.bone)
        if group is None:
            errors.append(f"{obj.name} has no {record.bone} vertex group")
        else:
            for vertex in mesh.vertices:
                matching = [
                    assignment
                    for assignment in vertex.groups
                    if assignment.group == group.index
                ]
                if len(matching) != 1 or abs(matching[0].weight - 1.0) > 1e-5:
                    errors.append(
                        f"{obj.name} vertex {vertex.index} is not rigidly weighted"
                    )
                    break

        for vertex in mesh.vertices:
            if not all(math.isfinite(value) for value in vertex.co):
                errors.append(f"{obj.name} has non-finite vertex coordinates")
                break
        try:
            validate_manifold(obj)
        except RuntimeError as error:
            errors.append(str(error))

        mesh.calc_loop_triangles()
        triangle_count += len(mesh.loop_triangles)
        minimum, maximum = mesh_bounds_world(obj)
        all_minima.append(minimum)
        all_maxima.append(maximum)

    if triangle_count > MAX_TRIANGLES:
        errors.append(
            f"Triangle budget exceeded: {triangle_count} > {MAX_TRIANGLES}"
        )

    if not all_minima:
        errors.append("No export mesh bounds were produced")
        bounds_min = Vector()
        bounds_max = Vector()
    else:
        bounds_min = Vector(
            (
                min(value.x for value in all_minima),
                min(value.y for value in all_minima),
                min(value.z for value in all_minima),
            )
        )
        bounds_max = Vector(
            (
                max(value.x for value in all_maxima),
                max(value.y for value in all_maxima),
                max(value.z for value in all_maxima),
            )
        )
        measured_height = bounds_max.z - bounds_min.z
        if abs(bounds_min.z) > config.height * 0.012:
            errors.append(
                f"Feet do not meet Z=0 (minimum is {bounds_min.z:.4f} m)"
            )
        if abs(measured_height - config.height) > config.height * 0.010:
            errors.append(
                "Generated visible height differs from requested height: "
                f"{measured_height:.4f} m vs {config.height:.4f} m"
            )
        measured_width = bounds_max.x - bounds_min.x
        if config.pose == "relaxed" and not (
            config.height * 0.30 <= measured_width <= config.height * 0.50
        ):
            errors.append(
                f"Relaxed silhouette width is implausible: {measured_width:.4f} m"
            )

    bandage = bpy.data.objects.get("CLO_Bandage.L")
    patch = bpy.data.objects.get("ACC_ShoulderPatch.R")
    strap = bpy.data.objects.get("ACC_StrapFront")
    if bandage is None or object_center_world(bandage).x <= 0.0:
        errors.append("Bandage must remain on anatomical left (+X)")
    if patch is None or object_center_world(patch).x >= 0.0:
        errors.append("Shoulder patch must remain on anatomical right (-X)")
    if strap is None:
        errors.append("Missing diagonal front strap")
    else:
        strap_min, strap_max = mesh_bounds_world(strap)
        if not strap_min.x < 0.0 < strap_max.x:
            errors.append("Front strap must cross the torso centre line")

    for presentation in result.presentation_objects:
        if presentation.get("bp_export", True):
            errors.append(f"Presentation object {presentation.name} is exportable")

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(f"Player 3D model validation failed:\n{formatted}")

    return ValidationReport(
        object_count=len(result.export_objects),
        mesh_count=len(result.parts),
        triangle_count=triangle_count,
        bounds_min=tuple(round(value, 6) for value in bounds_min),
        bounds_max=tuple(round(value, 6) for value in bounds_max),
    )


def select_export_objects(result: BuildResult) -> None:
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in result.export_objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = result.rig


def render_preview(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def export_glb(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    select_export_objects(result)
    try:
        bpy.ops.export_scene.gltf(
            filepath=str(path),
            export_format="GLB",
            use_selection=True,
            export_animations=False,
            export_cameras=False,
            export_lights=False,
        )
    except (AttributeError, RuntimeError, TypeError) as error:
        raise RuntimeError(
            "GLB export is unavailable in this Blender installation"
        ) from error


def export_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    select_export_objects(result)
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(path),
            use_selection=True,
            object_types={"EMPTY", "ARMATURE", "MESH"},
            axis_forward="-Z",
            axis_up="Y",
            add_leaf_bones=False,
            bake_anim=False,
            use_armature_deform_only=True,
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
        )
    except (AttributeError, RuntimeError, TypeError) as error:
        raise RuntimeError(
            "FBX export is unavailable in this Blender installation"
        ) from error


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    # A generator rerun is itself the backup; do not litter authoring folders
    # with Blender's automatic .blend1/.blend2 copies.
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def write_manifest(
    path: Path,
    config: BuildConfig,
    result: BuildResult,
    report: ValidationReport,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-player-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_source": "ArtSource/Player/PlayerDirectionalTurntable.png",
        "runtime_integrated": False,
        "height_m": config.height,
        "pose": config.pose,
        "seed": config.seed,
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "object_count": report.object_count,
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "bounds_min": report.bounds_min,
        "bounds_max": report.bounds_max,
        "sprite_parts": list(SPRITE_PARTS),
        "parts": [
            {
                "name": record.obj.name,
                "role": record.role,
                "bone": record.bone,
                "sprite_part": record.sprite_part,
                "side": record.side,
                "material": record.obj.data.materials[0].name,
                "vertices": len(record.obj.data.vertices),
                "triangles": len(record.obj.data.loop_triangles),
            }
            for record in sorted(result.parts, key=lambda item: item.obj.name)
        ],
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


def print_report(
    config: BuildConfig,
    report: ValidationReport,
) -> None:
    print("BP3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Pose: {config.pose}")
    print(f"  Export objects: {report.object_count}")
    print(f"  Separate mesh parts: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{MAX_TRIANGLES}")
    print(f"  Bounds min: {report.bounds_min}")
    print(f"  Bounds max: {report.bounds_max}")
    print(f"  Blend: {config.output}")
    if config.preview is not None:
        print(f"  Preview: {config.preview}")
    if config.manifest is not None:
        print(f"  Manifest: {config.manifest}")
    if config.glb is not None:
        print(f"  GLB: {config.glb}")
    if config.fbx is not None:
        print(f"  FBX: {config.fbx}")


def main() -> None:
    config = parse_args()
    builder = CharacterBuilder(config)
    result = builder.build()
    report = validate_result(config, result)

    if config.preview is not None:
        render_preview(config.preview)
    if config.glb is not None:
        export_glb(config.glb, result)
    if config.fbx is not None:
        export_fbx(config.fbx, result)
    if config.manifest is not None:
        write_manifest(config.manifest, config, result, report)
    save_blend(config.output)
    print_report(config, report)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        raise
