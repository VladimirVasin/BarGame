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
from mathutils import Euler, Matrix, Quaternion, Vector


GENERATOR_VERSION = "2.8.0"
CANONICAL_HEIGHT = 1.75
DEFAULT_SEED = 7301
MAX_TRIANGLES = 4500
ANIMATION_FPS = 24

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

# These bones are exported as ordinary transforms but never deform geometry.
# Keeping sockets inside the armature makes them deterministic across FBX
# imports and guarantees that props follow their owning hand/head animation.
REQUIRED_SOCKET_BONES = (
    "SOCKET_Grip.L",
    "SOCKET_Grip.R",
    "SOCKET_Cigarette.R",
    "SOCKET_Bottle.R",
    "SOCKET_Vessel.L",
    "SOCKET_Mouth",
)

REQUIRED_FACE_BONES = (
    "face.eye.L",
    "face.eye.R",
    "face.brow.L",
    "face.brow.R",
    "face.mouth",
)

# How far the supine sleeper's back and the back of his lifted head sit under
# the pelvis bone that runtime pins to the mattress. Mirrored by
# PlayerCharacterDimensions in the Unity runtime; validate_bed_support_contract
# measures both against the real posed meshes and refuses a silent drift.
BED_BODY_SUPPORT_OFFSET_M = 0.1377
BED_HEAD_SUPPORT_OFFSET_M = 0.0656
BED_SEATED_SUPPORT_OFFSET_M = 0.0109
BED_SEATED_PELVIS_LIFT_M = 0.0239
# Mirrors HomeInteriorWorldBuilder.BedMattressSurfaceHeight: how far the
# mattress he sits on stands above the floor his boots have to reach.
BED_MATTRESS_ABOVE_FLOOR_M = 0.56
# Mirror HomeBedInteractionPlan: the windows each transition spends sitting on
# the bedside edge, in normalized clip time.
BED_ENTER_SEAT_ARRIVAL = 0.28
BED_ENTER_SEAT_DEPARTURE = 0.44
BED_EXIT_SEAT_ARRIVAL = 0.50
BED_EXIT_SEAT_DEPARTURE = 0.88

# The bed Actions stagger their landmarks: the pelvis and legs drive a move
# and the chest, head, arms and face arrive at the same landmark a few frames
# behind. Splitting the rig this way is what allows overlap at all — a key
# that names every bone forces the whole body to turn on one frame.
BED_LEADING_BONES = (
    "pelvis",
    "spine",
    "thigh.L",
    "shin.L",
    "foot.L",
    "thigh.R",
    "shin.R",
    "foot.R",
)

BED_TRAILING_BONES = (
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
    "face.eye.L",
    "face.eye.R",
    "face.brow.L",
    "face.brow.R",
    "face.mouth",
)

REQUIRED_ACTIONS = (
    "Relaxed",
    "Idle",
    "Walk",
    "WalkBack",
    "TurnLeft",
    "TurnRight",
    "Face_Neutral",
    "Face_HalfBlink",
    "Face_ClosedBlink",
    "Face_Watchful",
    "Face_Tense",
    "FallLeft",
    "DownLeft",
    "RiseLeft",
    "FallRight",
    "DownRight",
    "RiseRight",
    "BedEnter",
    "BedSleepLoop",
    "BedExit",
    "SmokeEnter",
    "SmokeLoop",
    "SmokeExit",
    "CatFeedEnter",
    "CatFeedLoop",
    "CatFeedExit",
    "DoorUseEnter",
    "DoorUseLoop",
    "DoorUseExit",
    "BusBoardEnter",
    "BusRideLoop",
    "BusAlightExit",
    "ChessSeatEnter",
    "ChessSeatPlayLoop",
    "ChessSeatExit",
)


@dataclass(frozen=True)
class BuildConfig:
    output: Path
    preview: Path | None
    portrait: Path | None
    manifest: Path | None
    glb: Path | None
    fbx: Path | None
    animation_fbx: Path | None
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


@dataclass(frozen=True)
class BonePose:
    """A pose delta, optionally solved in explicit armature-space units."""

    rotation_degrees: tuple[float, float, float] = (0.0, 0.0, 0.0)
    location_m: tuple[float, float, float] = (0.0, 0.0, 0.0)
    scale: tuple[float, float, float] = (1.0, 1.0, 1.0)
    target_direction: tuple[float, float, float] | None = None
    armature_direction: tuple[float, float, float] | None = None
    armature_location_m: tuple[float, float, float] | None = None


# One authored landmark: normalized time, the full pose, and optionally the
# only bones that take a key there. Both endpoints must key the whole rig.
ActionKey = (
    tuple[float, dict[str, BonePose]]
    | tuple[float, dict[str, BonePose], tuple[str, ...]]
)


@dataclass(frozen=True)
class ActionRecord:
    action: bpy.types.Action
    category: str
    duration_seconds: float
    loop: bool
    source_frame_count: int
    source_fps: float


@dataclass
class BuildResult:
    root: bpy.types.Object
    rig: bpy.types.Object
    collections: dict[str, bpy.types.Collection]
    materials: dict[str, bpy.types.Material]
    parts: list[PartRecord] = field(default_factory=list)
    presentation_objects: list[bpy.types.Object] = field(default_factory=list)
    actions: dict[str, ActionRecord] = field(default_factory=dict)

    @property
    def export_objects(self) -> list[bpy.types.Object]:
        return [self.root, self.rig, *(record.obj for record in self.parts)]


@dataclass(frozen=True)
class ValidationReport:
    object_count: int
    mesh_count: int
    triangle_count: int
    action_count: int
    socket_count: int
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
        "--portrait",
        type=Path,
        help=(
            "Optional transparent 192x256 head/upper-torso inventory "
            "portrait rendered from the production rig."
        ),
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
        "--animation-fbx",
        type=Path,
        help=(
            "Optional armature-only FBX containing all generated Actions; "
            "use --fbx for the animation-free model export."
        ),
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
        default="apose",
        help=(
            "Bind pose (default: apose). Relaxed remains a compatibility "
            "preview; production exports should use apose."
        ),
    )
    args = parser.parse_args(user_args)

    if not 1.40 <= args.height <= 2.10:
        parser.error("--height must be between 1.40 and 2.10 metres")

    return BuildConfig(
        output=resolve_path(args.output),
        preview=resolve_optional_path(args.preview),
        portrait=resolve_optional_path(args.portrait),
        manifest=resolve_optional_path(args.manifest),
        glb=resolve_optional_path(args.glb),
        fbx=resolve_optional_path(args.fbx),
        animation_fbx=resolve_optional_path(args.animation_fbx),
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
        self.bone_specs: dict[str, BoneSpec] = {}

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
        self.bone_specs = {spec.name: spec for spec in bone_specs}
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
        self.build_actions()
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
        scene.render.fps = ANIMATION_FPS
        scene.render.fps_base = 1.0

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
        specs = [
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

        # Small deforming face controls keep expression Actions bone-only.
        # Their names are stable import identifiers; no shape keys or object
        # visibility curves are required for blink/watchful/tense states.
        specs.extend(
            (
                BoneSpec(
                    "face.eye.L",
                    self.v(0.052, -0.147, 1.581),
                    self.v(0.052, -0.147, 1.599),
                    "head",
                ),
                BoneSpec(
                    "face.eye.R",
                    self.v(-0.052, -0.147, 1.581),
                    self.v(-0.052, -0.147, 1.599),
                    "head",
                ),
                BoneSpec(
                    "face.brow.L",
                    self.v(0.082, -0.154, 1.627),
                    self.v(0.027, -0.157, 1.621),
                    "head",
                ),
                BoneSpec(
                    "face.brow.R",
                    self.v(-0.082, -0.154, 1.625),
                    self.v(-0.027, -0.157, 1.619),
                    "head",
                ),
                BoneSpec(
                    "face.mouth",
                    self.v(-0.036, -0.151, 1.477),
                    self.v(0.048, -0.151, 1.477),
                    "head",
                ),
            )
        )

        # Non-deforming sockets are real bones rather than generated empties.
        # They survive FBX axis conversion and inherit animation directly.
        grip_l = p["wrist.L"].lerp(p["hand.L"], 0.72)
        grip_r = p["wrist.R"].lerp(p["hand.R"], 0.72)
        specs.extend(
            (
                BoneSpec(
                    "SOCKET_Grip.L",
                    grip_l,
                    grip_l + self.v(0, -0.055, 0),
                    "hand.L",
                    deform=False,
                ),
                BoneSpec(
                    "SOCKET_Grip.R",
                    grip_r,
                    grip_r + self.v(0, -0.055, 0),
                    "hand.R",
                    deform=False,
                ),
                BoneSpec(
                    "SOCKET_Cigarette.R",
                    grip_r + self.v(0, -0.010, 0.012),
                    grip_r + self.v(0, -0.085, 0.012),
                    "hand.R",
                    deform=False,
                ),
                BoneSpec(
                    "SOCKET_Bottle.R",
                    grip_r,
                    grip_r + self.v(0, 0, -0.085),
                    "hand.R",
                    deform=False,
                ),
                BoneSpec(
                    "SOCKET_Vessel.L",
                    grip_l,
                    grip_l + self.v(0, 0, -0.085),
                    "hand.L",
                    deform=False,
                ),
                BoneSpec(
                    "SOCKET_Mouth",
                    self.v(0.006, -0.158, 1.477),
                    self.v(0.006, -0.218, 1.477),
                    "head",
                    deform=False,
                ),
            )
        )
        return specs

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
                f"face.eye.{side}",
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
                f"face.eye.{side}",
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
                f"face.brow.{side}",
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
            "face.mouth",
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

    @staticmethod
    def merge_pose(
        base: dict[str, BonePose],
        *overrides: dict[str, BonePose],
    ) -> dict[str, BonePose]:
        merged = dict(base)
        for override in overrides:
            merged.update(override)
        return merged

    def relaxed_pose(self) -> dict[str, BonePose]:
        """Return the ordinary weary stance as animation, never bind pose."""

        if self.config.pose == "relaxed":
            return {}
        return {
            "upper_arm.L": BonePose(
                target_direction=(0.059, -0.014, -0.334)
            ),
            "upper_arm.R": BonePose(
                target_direction=(-0.047, -0.010, -0.334)
            ),
            "forearm.L": BonePose(rotation_degrees=(-4.0, 3.0, -2.0)),
            "forearm.R": BonePose(rotation_degrees=(-5.0, -3.0, 2.0)),
            "hand.L": BonePose(rotation_degrees=(2.0, -5.0, 2.0)),
            "hand.R": BonePose(rotation_degrees=(2.0, 5.0, -2.0)),
            "spine": BonePose(rotation_degrees=(-2.0, 0.0, 1.2)),
            "chest": BonePose(rotation_degrees=(2.5, 0.0, -1.4)),
            "neck": BonePose(rotation_degrees=(-2.0, 0.0, 0.8)),
            "head": BonePose(rotation_degrees=(1.5, 0.0, -0.6)),
        }

    def _reset_pose(self) -> None:
        if self.result is None:
            raise RuntimeError("BuildResult has not been initialized")
        for pose_bone in self.result.rig.pose.bones:
            pose_bone.rotation_mode = "QUATERNION"
            pose_bone.location = (0.0, 0.0, 0.0)
            pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
            pose_bone.scale = (1.0, 1.0, 1.0)
        bpy.context.view_layer.update()

    def _apply_pose(self, pose: dict[str, BonePose]) -> None:
        if self.result is None:
            raise RuntimeError("BuildResult has not been initialized")
        rig = self.result.rig
        for bone_name in (*REQUIRED_BONES, *REQUIRED_FACE_BONES):
            transform = pose.get(bone_name)
            if transform is None:
                continue
            pose_bone = rig.pose.bones[bone_name]
            if transform.armature_direction is not None:
                # Ancestor deltas authored earlier in this pass must be
                # evaluated before solving an absolute armature-space axis.
                bpy.context.view_layer.update()
                target_direction = Vector(transform.armature_direction).normalized()
                rest_bone = pose_bone.bone
                rest_direction = (
                    rest_bone.tail_local - rest_bone.head_local
                ).normalized()
                target_rotation = (
                    rest_direction.rotation_difference(target_direction)
                    @ rest_bone.matrix_local.to_quaternion()
                )
                pose_bone.matrix = (
                    Matrix.Translation(pose_bone.head.copy())
                    @ target_rotation.to_matrix().to_4x4()
                )
                bpy.context.view_layer.update()
            elif transform.target_direction is not None:
                target_direction = Vector(transform.target_direction).normalized()
                rest_bone = pose_bone.bone
                rest_direction = (
                    rest_bone.tail_local - rest_bone.head_local
                ).normalized()
                target_rotation = (
                    rest_direction.rotation_difference(target_direction)
                    @ rest_bone.matrix_local.to_quaternion()
                )
                parent = pose_bone.parent
                if parent is not None:
                    parent_delta = (
                        parent.matrix.to_quaternion()
                        @ parent.bone.matrix_local.to_quaternion().inverted()
                    )
                    target_rotation = parent_delta @ target_rotation
                pose_bone.matrix = (
                    Matrix.Translation(pose_bone.head.copy())
                    @ target_rotation.to_matrix().to_4x4()
                )
                bpy.context.view_layer.update()
            else:
                pose_bone.rotation_quaternion = Euler(
                    tuple(math.radians(value) for value in transform.rotation_degrees),
                    "XYZ",
                ).to_quaternion()
            if transform.armature_location_m is not None:
                bpy.context.view_layer.update()
                pose_matrix = pose_bone.matrix.copy()
                pose_matrix.translation += (
                    Vector(transform.armature_location_m) * self.scale
                )
                pose_bone.matrix = pose_matrix
            else:
                pose_bone.location = Vector(transform.location_m) * self.scale
            pose_bone.scale = transform.scale
        bpy.context.view_layer.update()

    def _create_action(
        self,
        name: str,
        category: str,
        duration_seconds: float,
        loop: bool,
        source_frame_count: int,
        source_fps: float,
        keys: Sequence[ActionKey],
        interpolation: str = "LINEAR",
    ) -> None:
        if self.result is None:
            raise RuntimeError("BuildResult has not been initialized")
        if name in self.result.actions:
            raise ValueError(f"Duplicate Action {name}")
        if not keys or keys[0][0] != 0.0 or keys[-1][0] != 1.0:
            raise ValueError(f"Action {name} needs normalized 0 and 1 endpoints")
        if len(keys[0]) > 2 or len(keys[-1]) > 2:
            raise ValueError(
                f"Action {name} must key the whole rig on both endpoints"
            )

        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        action.use_frame_range = True
        action.frame_start = 0.0
        action.frame_end = float(max(1, round(duration_seconds * ANIMATION_FPS)))
        action.use_cyclic = loop
        action["bp_category"] = category
        action["bp_duration_seconds"] = duration_seconds
        action["bp_loop"] = loop
        action["bp_source_frame_count"] = source_frame_count
        action["bp_source_fps"] = source_fps
        action["bp_root_motion"] = False
        action["bp_generator_version"] = GENERATOR_VERSION

        rig = self.result.rig
        animation_data = rig.animation_data_create()
        animation_data.action = action
        keyed_bones = (*REQUIRED_BONES, *REQUIRED_FACE_BONES)
        previous_quaternions: dict[str, Quaternion] = {}
        for key in keys:
            normalized_time, pose = key[0], key[1]
            # A key may name a subset of bones. Bones left out keep
            # interpolating through this moment, which is the only way a
            # chest, head or hand can lag behind the pelvis that leads it.
            subset = key[2] if len(key) > 2 else None
            if subset is not None:
                unknown = tuple(
                    bone_name for bone_name in subset
                    if bone_name not in keyed_bones
                )
                if unknown:
                    raise ValueError(
                        f"Action {name} keys unknown bones {unknown}"
                    )
            self._reset_pose()
            self._apply_pose(pose)
            frame = round(action.frame_end * normalized_time)
            for bone_name in keyed_bones:
                if subset is not None and bone_name not in subset:
                    continue
                pose_bone = rig.pose.bones[bone_name]
                quaternion = pose_bone.rotation_quaternion.copy()
                previous_quaternion = previous_quaternions.get(bone_name)
                if previous_quaternion is not None:
                    quaternion.make_compatible(previous_quaternion)
                    pose_bone.rotation_quaternion = quaternion
                previous_quaternions[bone_name] = quaternion.copy()
                group_name = bone_name.split(".")[0]
                pose_bone.keyframe_insert(
                    data_path="location",
                    frame=frame,
                    group=group_name,
                )
                pose_bone.keyframe_insert(
                    data_path="rotation_quaternion",
                    frame=frame,
                    group=group_name,
                )
                pose_bone.keyframe_insert(
                    data_path="scale",
                    frame=frame,
                    group=group_name,
                )

        for fcurve in iter_action_fcurves(action):
            for keyframe in fcurve.keyframe_points:
                keyframe.interpolation = interpolation
                if interpolation == "BEZIER":
                    keyframe.handle_left_type = "AUTO_CLAMPED"
                    keyframe.handle_right_type = "AUTO_CLAMPED"
        animation_data.action = None
        self._reset_pose()
        self.result.actions[name] = ActionRecord(
            action=action,
            category=category,
            duration_seconds=duration_seconds,
            loop=loop,
            source_frame_count=source_frame_count,
            source_fps=source_fps,
        )

    def build_actions(self) -> None:
        """Author deterministic, in-place production Actions."""

        relaxed = self.relaxed_pose()
        idle_left_inhale = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(0.8, 0.5, -1.8)),
                "spine": BonePose(rotation_degrees=(-4.4, 0.0, 2.5)),
                "chest": BonePose(rotation_degrees=(5.8, 0.0, -2.8)),
                "neck": BonePose(rotation_degrees=(-2.8, 0.0, 1.2)),
                "head": BonePose(rotation_degrees=(2.8, 0.0, -1.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.063, -0.018, -0.330)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.044, -0.004, -0.336)
                ),
                "forearm.L": BonePose(rotation_degrees=(-9.0, 4.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-11.0, -3.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(-2.0, 0.0, 0.5)),
                "shin.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(1.0, 0.0, -0.5)),
                "shin.R": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
            },
        )
        idle_left_settle = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(-0.5, 0.0, -2.4)),
                "spine": BonePose(rotation_degrees=(-2.8, 0.0, 3.0)),
                "chest": BonePose(rotation_degrees=(3.2, 0.0, -3.5)),
                "neck": BonePose(rotation_degrees=(-1.5, 0.0, 1.6)),
                "head": BonePose(rotation_degrees=(0.4, 0.0, -1.3)),
                "upper_arm.L": BonePose(
                    target_direction=(0.061, -0.010, -0.334)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.045, -0.002, -0.336)
                ),
                "forearm.L": BonePose(rotation_degrees=(-7.0, 4.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-9.0, -3.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(-1.2, 0.0, 0.5)),
                "shin.L": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
            },
        )
        idle_right_inhale = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(0.8, -0.5, 1.7)),
                "spine": BonePose(rotation_degrees=(-4.2, 0.0, -0.3)),
                "chest": BonePose(rotation_degrees=(5.6, 0.0, 0.2)),
                "neck": BonePose(rotation_degrees=(-2.8, 0.0, -0.8)),
                "head": BonePose(rotation_degrees=(2.6, 0.0, 0.9)),
                "upper_arm.L": BonePose(
                    target_direction=(0.047, -0.004, -0.336)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.061, -0.018, -0.331)
                ),
                "forearm.L": BonePose(rotation_degrees=(-10.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-9.0, -4.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(1.0, 0.0, 0.5)),
                "shin.L": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-2.0, 0.0, -0.5)),
                "shin.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
            },
        )
        idle_right_settle = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(-0.4, 0.0, 2.2)),
                "spine": BonePose(rotation_degrees=(-2.7, 0.0, -0.7)),
                "chest": BonePose(rotation_degrees=(3.1, 0.0, 0.9)),
                "neck": BonePose(rotation_degrees=(-1.4, 0.0, -0.8)),
                "head": BonePose(rotation_degrees=(0.5, 0.0, 1.2)),
                "upper_arm.L": BonePose(
                    target_direction=(0.047, -0.002, -0.337)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.059, -0.010, -0.334)
                ),
                "forearm.L": BonePose(rotation_degrees=(-8.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-7.0, -4.0, 3.0)),
                "thigh.R": BonePose(rotation_degrees=(-1.2, 0.0, -0.5)),
                "shin.R": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
            },
        )
        self._create_action(
            "Relaxed", "locomotion", 1.0 / ANIMATION_FPS, False, 1, 24,
            ((0.0, relaxed), (1.0, relaxed)),
        )
        self._create_action(
            "Idle", "locomotion", 4.0, True, 96, 24,
            (
                (0.0, relaxed),
                (0.16, idle_left_inhale),
                (0.34, idle_left_settle),
                (0.50, relaxed),
                (0.66, idle_right_inhale),
                (0.84, idle_right_settle),
                (1.0, relaxed),
            ),
            interpolation="BEZIER",
        )

        walk_left_contact = self.merge_pose(
            relaxed,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.052, 0.145, -0.304)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.052, -0.135, -0.309)
                ),
                "forearm.L": BonePose(rotation_degrees=(-17.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-27.0, -4.0, 3.0)),
                "hand.L": BonePose(rotation_degrees=(4.0, -4.0, 2.0)),
                "hand.R": BonePose(rotation_degrees=(5.0, 6.0, -2.0)),
                "thigh.L": BonePose(rotation_degrees=(-26.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(1.0, 3.0, -2.2)),
                "spine": BonePose(rotation_degrees=(-4.0, 0.0, 1.4)),
                "chest": BonePose(rotation_degrees=(4.4, -1.5, -1.6)),
                "head": BonePose(rotation_degrees=(1.0, 0.0, 0.6)),
            },
        )
        walk_left_down = self.merge_pose(
            walk_left_contact,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.052, 0.105, -0.321)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.052, -0.103, -0.322)
                ),
                "forearm.L": BonePose(rotation_degrees=(-19.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-26.0, -4.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(42.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(2.0, 1.0, -1.3)),
                "spine": BonePose(rotation_degrees=(-4.8, 0.0, 0.8)),
                "chest": BonePose(rotation_degrees=(5.0, -1.0, -1.0)),
            },
        )
        walk_right_pass = self.merge_pose(
            relaxed,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.055, 0.035, -0.337)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.055, -0.030, -0.337)
                ),
                "forearm.L": BonePose(rotation_degrees=(-21.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-24.0, -4.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(52.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(0.0, -1.0, 0.8)),
                "spine": BonePose(rotation_degrees=(-3.8, 0.0, 0.2)),
                "chest": BonePose(rotation_degrees=(4.2, -0.4, -0.2)),
                "head": BonePose(rotation_degrees=(1.2, 0.0, -0.2)),
            },
        )
        walk_right_up = self.merge_pose(
            relaxed,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.052, -0.075, -0.329)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.052, 0.080, -0.328)
                ),
                "forearm.L": BonePose(rotation_degrees=(-25.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-20.0, -4.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-24.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(34.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(-1.0, -2.0, 1.5)),
                "spine": BonePose(rotation_degrees=(-3.5, 0.0, -0.7)),
                "chest": BonePose(rotation_degrees=(4.0, 0.8, 0.8)),
                "head": BonePose(rotation_degrees=(1.0, 0.0, -0.5)),
            },
        )
        walk_right_contact = self.merge_pose(
            relaxed,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.052, -0.135, -0.309)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.052, 0.145, -0.304)
                ),
                "forearm.L": BonePose(rotation_degrees=(-27.0, 4.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-17.0, -3.0, 3.0)),
                "hand.L": BonePose(rotation_degrees=(5.0, -6.0, 2.0)),
                "hand.R": BonePose(rotation_degrees=(4.0, 4.0, -2.0)),
                "thigh.L": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-26.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(1.0, -3.0, 2.2)),
                "spine": BonePose(rotation_degrees=(-4.0, 0.0, -1.4)),
                "chest": BonePose(rotation_degrees=(4.4, 1.5, 1.6)),
                "head": BonePose(rotation_degrees=(1.0, 0.0, -0.6)),
            },
        )
        walk_right_down = self.merge_pose(
            walk_right_contact,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.052, -0.103, -0.322)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.052, 0.105, -0.321)
                ),
                "forearm.L": BonePose(rotation_degrees=(-26.0, 4.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-19.0, -3.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(42.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(2.0, -1.0, 1.3)),
                "spine": BonePose(rotation_degrees=(-4.8, 0.0, -0.8)),
                "chest": BonePose(rotation_degrees=(5.0, 1.0, 1.0)),
            },
        )
        walk_left_pass = self.merge_pose(
            relaxed,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.055, -0.030, -0.337)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.055, 0.035, -0.337)
                ),
                "forearm.L": BonePose(rotation_degrees=(-24.0, 4.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-21.0, -3.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(52.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(0.0, 1.0, -0.8)),
                "spine": BonePose(rotation_degrees=(-3.8, 0.0, -0.2)),
                "chest": BonePose(rotation_degrees=(4.2, 0.4, 0.2)),
                "head": BonePose(rotation_degrees=(1.2, 0.0, 0.2)),
            },
        )
        walk_left_up = self.merge_pose(
            relaxed,
            {
                "upper_arm.L": BonePose(
                    target_direction=(0.052, 0.080, -0.328)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.052, -0.075, -0.329)
                ),
                "forearm.L": BonePose(rotation_degrees=(-20.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-25.0, -4.0, 3.0)),
                "thigh.L": BonePose(rotation_degrees=(-24.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(34.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
                "pelvis": BonePose(rotation_degrees=(-1.0, 2.0, -1.5)),
                "spine": BonePose(rotation_degrees=(-3.5, 0.0, 0.7)),
                "chest": BonePose(rotation_degrees=(4.0, -0.8, -0.8)),
                "head": BonePose(rotation_degrees=(1.0, 0.0, 0.5)),
            },
        )
        self._create_action(
            "Walk", "locomotion", 1.0, True, 24, 24,
            (
                (0.0, walk_left_contact),
                (0.125, walk_left_down),
                (0.25, walk_right_pass),
                (0.375, walk_right_up),
                (0.5, walk_right_contact),
                (0.625, walk_right_down),
                (0.75, walk_left_pass),
                (0.875, walk_left_up),
                (1.0, walk_left_contact),
            ),
            interpolation="BEZIER",
        )
        # The backpedal is the eight walk landmarks traversed in reverse
        # time. A real backward gait keeps the opposite-arm-to-leg
        # relationship, and the reversed cycle preserves it for free.
        self._create_action(
            "WalkBack", "locomotion", 1.0, True, 24, 24,
            (
                (0.0, walk_left_contact),
                (0.125, walk_left_up),
                (0.25, walk_left_pass),
                (0.375, walk_right_down),
                (0.5, walk_right_contact),
                (0.625, walk_right_up),
                (0.75, walk_right_pass),
                (0.875, walk_left_down),
                (1.0, walk_left_contact),
            ),
            interpolation="BEZIER",
        )

        # Turn-in-place: the torso winds into the turn while the inner
        # foot lifts and re-plants, then the outer foot drags after it.
        # Probe-verified signs: positive local Y on pelvis/spine/chest
        # yaws the character to his own left; negative local X on a
        # thigh lifts that leg forward.
        turn_left_wind = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(0.0, 3.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.0, 4.0, 0.0)),
                "chest": BonePose(rotation_degrees=(2.5, 7.0, -1.0)),
                "head": BonePose(rotation_degrees=(1.5, 5.0, 0.0)),
                "thigh.L": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
                "forearm.L": BonePose(rotation_degrees=(-8.0, 3.0, -2.0)),
                "forearm.R": BonePose(rotation_degrees=(-3.0, -3.0, 2.0)),
            },
        )
        turn_left_lift = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(1.0, 5.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.5, 5.0, 0.0)),
                "chest": BonePose(rotation_degrees=(3.0, 8.0, -1.0)),
                "head": BonePose(rotation_degrees=(1.5, 6.0, 0.0)),
                "thigh.L": BonePose(rotation_degrees=(-20.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(26.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "forearm.L": BonePose(rotation_degrees=(-10.0, 3.0, -2.0)),
                "forearm.R": BonePose(rotation_degrees=(-4.0, -3.0, 2.0)),
            },
        )
        turn_left_plant = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(1.0, 2.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.0, 1.0, 0.0)),
                "chest": BonePose(rotation_degrees=(2.5, 1.0, 0.0)),
                "head": BonePose(rotation_degrees=(1.5, 2.0, 0.0)),
                "thigh.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "forearm.L": BonePose(rotation_degrees=(-6.0, 3.0, -2.0)),
                "forearm.R": BonePose(rotation_degrees=(-6.0, -3.0, 2.0)),
            },
        )
        turn_left_drag = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(0.5, -2.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.0, -1.0, 0.0)),
                "chest": BonePose(rotation_degrees=(2.5, -2.0, 1.0)),
                "head": BonePose(rotation_degrees=(1.5, 1.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                "forearm.L": BonePose(rotation_degrees=(-4.0, 3.0, -2.0)),
                "forearm.R": BonePose(rotation_degrees=(-8.0, -3.0, 2.0)),
            },
        )
        self._create_action(
            "TurnLeft", "locomotion", 1.0, True, 24, 24,
            (
                (0.0, turn_left_wind),
                (0.25, turn_left_lift),
                (0.5, turn_left_plant),
                (0.75, turn_left_drag),
                (1.0, turn_left_wind),
            ),
            interpolation="BEZIER",
        )

        # Hand-authored mirror of TurnLeft, following the walk
        # convention: swap .L/.R, negate the Y and Z components on the
        # axial chain and the forearms.
        turn_right_wind = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(0.0, -3.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.0, -4.0, 0.0)),
                "chest": BonePose(rotation_degrees=(2.5, -7.0, 1.0)),
                "head": BonePose(rotation_degrees=(1.5, -5.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
                "forearm.R": BonePose(rotation_degrees=(-8.0, -3.0, 2.0)),
                "forearm.L": BonePose(rotation_degrees=(-3.0, 3.0, -2.0)),
            },
        )
        turn_right_lift = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(1.0, -5.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.5, -5.0, 0.0)),
                "chest": BonePose(rotation_degrees=(3.0, -8.0, 1.0)),
                "head": BonePose(rotation_degrees=(1.5, -6.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-20.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(26.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "forearm.R": BonePose(rotation_degrees=(-10.0, -3.0, 2.0)),
                "forearm.L": BonePose(rotation_degrees=(-4.0, 3.0, -2.0)),
            },
        )
        turn_right_plant = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(1.0, -2.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.0, -1.0, 0.0)),
                "chest": BonePose(rotation_degrees=(2.5, -1.0, 0.0)),
                "head": BonePose(rotation_degrees=(1.5, -2.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "forearm.R": BonePose(rotation_degrees=(-6.0, -3.0, 2.0)),
                "forearm.L": BonePose(rotation_degrees=(-6.0, 3.0, -2.0)),
            },
        )
        turn_right_drag = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(0.5, 2.0, 0.0)),
                "spine": BonePose(rotation_degrees=(-2.0, 1.0, 0.0)),
                "chest": BonePose(rotation_degrees=(2.5, 2.0, -1.0)),
                "head": BonePose(rotation_degrees=(1.5, -1.0, 0.0)),
                "thigh.L": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
                "shin.L": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                "forearm.R": BonePose(rotation_degrees=(-4.0, -3.0, 2.0)),
                "forearm.L": BonePose(rotation_degrees=(-8.0, 3.0, -2.0)),
            },
        )
        self._create_action(
            "TurnRight", "locomotion", 1.0, True, 24, 24,
            (
                (0.0, turn_right_wind),
                (0.25, turn_right_lift),
                (0.5, turn_right_plant),
                (0.75, turn_right_drag),
                (1.0, turn_right_wind),
            ),
            interpolation="BEZIER",
        )

        face_poses = {
            "Face_Neutral": {},
            "Face_HalfBlink": {
                "face.eye.L": BonePose(scale=(1.0, 0.48, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.48, 1.0)),
            },
            "Face_ClosedBlink": {
                "face.eye.L": BonePose(scale=(1.0, 0.08, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.08, 1.0)),
            },
            "Face_Watchful": {
                "face.eye.L": BonePose(scale=(1.0, 1.18, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 1.18, 1.0)),
                "face.brow.L": BonePose(
                    rotation_degrees=(0.0, 0.0, -5.0),
                    location_m=(0.0, 0.0, 0.006),
                ),
                "face.brow.R": BonePose(
                    rotation_degrees=(0.0, 0.0, 5.0),
                    location_m=(0.0, 0.0, 0.006),
                ),
            },
            "Face_Tense": {
                "face.eye.L": BonePose(scale=(1.0, 0.72, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.72, 1.0)),
                "face.brow.L": BonePose(rotation_degrees=(0.0, 0.0, 12.0)),
                "face.brow.R": BonePose(rotation_degrees=(0.0, 0.0, -12.0)),
                "face.mouth": BonePose(scale=(0.82, 1.0, 1.0)),
            },
        }
        for name, pose in face_poses.items():
            self._create_action(
                name, "facial", 1.0 / ANIMATION_FPS, False, 1, 24,
                ((0.0, pose), (1.0, pose)),
            )

        for side_name, sign in (("Left", 1.0), ("Right", -1.0)):
            lead_suffix, trail_suffix = (
                ("L", "R") if side_name == "Left" else ("R", "L")
            )
            lead_sign = 1.0 if lead_suffix == "L" else -1.0
            stumble = self.merge_pose(
                relaxed,
                {
                    "pelvis": BonePose(rotation_degrees=(4.0, 0.0, sign * 28.0)),
                    "spine": BonePose(rotation_degrees=(-10.0, 0.0, sign * 12.0)),
                    "chest": BonePose(rotation_degrees=(8.0, 0.0, sign * 10.0)),
                    "upper_arm.L": BonePose(
                        target_direction=(0.22, -0.08, -0.08)
                    ),
                    "upper_arm.R": BonePose(
                        target_direction=(-0.22, -0.08, -0.08)
                    ),
                },
            )
            down = self.merge_pose(
                relaxed,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(6.0, 0.0, sign * 88.0),
                        armature_location_m=(sign * 0.10, 0.0, -0.39),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(-8.0, 0.0, sign * 6.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(12.0, 0.0, sign * 5.0)
                    ),
                    "neck": BonePose(
                        rotation_degrees=(8.0, 0.0, -sign * 3.0)
                    ),
                    "head": BonePose(
                        rotation_degrees=(-8.0, 0.0, -sign * 7.0)
                    ),
                    f"upper_arm.{lead_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.12,
                            0.08,
                            -0.24,
                        )
                    ),
                    f"forearm.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.02,
                            0.10,
                            -0.14,
                        )
                    ),
                    f"hand.{lead_suffix}": BonePose(
                        armature_direction=(lead_sign * 0.06, 0.03, 0.02)
                    ),
                    f"upper_arm.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.07,
                            -0.22,
                            0.0,
                        )
                    ),
                    f"forearm.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.02,
                            -0.18,
                            0.0,
                        )
                    ),
                    f"hand.{trail_suffix}": BonePose(
                        armature_direction=(-lead_sign * 0.01, -0.08, 0.0)
                    ),
                    "thigh.L": BonePose(
                        armature_direction=(0.04, 0.34, -0.15)
                    ),
                    "thigh.R": BonePose(
                        armature_direction=(-0.04, 0.34, -0.15)
                    ),
                    "shin.L": BonePose(
                        armature_direction=(0.01, 0.24, 0.02)
                    ),
                    "shin.R": BonePose(
                        armature_direction=(-0.01, 0.24, 0.02)
                    ),
                    "foot.L": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                    "foot.R": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                },
            )
            down_breath = self.merge_pose(
                down,
                {"chest": BonePose(rotation_degrees=(14.0, 0.0, sign * 5.0))},
            )
            brace = self.merge_pose(
                down,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(28.0, 0.0, sign * 67.0),
                        armature_location_m=(sign * 0.08, 0.01, -0.29),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(-14.0, 0.0, sign * 4.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(18.0, 0.0, sign * 3.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                    "head": BonePose(
                        rotation_degrees=(-12.0, 0.0, -sign * 4.0)
                    ),
                    f"upper_arm.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.08,
                            -0.10,
                            -0.28,
                        )
                    ),
                    f"forearm.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.02,
                            -0.10,
                            -0.22,
                        )
                    ),
                    f"hand.{lead_suffix}": BonePose(
                        armature_direction=(lead_sign * 0.01, -0.08, -0.04)
                    ),
                    f"upper_arm.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.07,
                            -0.18,
                            -0.20,
                        )
                    ),
                    f"forearm.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.02,
                            -0.17,
                            -0.13,
                        )
                    ),
                    f"hand.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.01,
                            -0.08,
                            0.0,
                        )
                    ),
                    f"thigh.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.04,
                            0.27,
                            -0.27,
                        )
                    ),
                    f"shin.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.01,
                            0.24,
                            0.02,
                        )
                    ),
                    f"foot.{lead_suffix}": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                    f"thigh.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.04,
                            0.27,
                            -0.27,
                        )
                    ),
                    f"shin.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.01,
                            0.24,
                            0.02,
                        )
                    ),
                    f"foot.{trail_suffix}": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                },
            )
            prone_tuck = self.merge_pose(
                brace,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(66.0, 0.0, sign * 20.0),
                        armature_location_m=(sign * 0.045, 0.025, -0.33),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(-10.0, 0.0, sign * 2.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(10.0, 0.0, -sign * 2.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-30.0, 0.0, 0.0)),
                    "head": BonePose(rotation_degrees=(-24.0, 0.0, 0.0)),
                    "upper_arm.L": BonePose(
                        armature_direction=(0.07, -0.10, -0.29)
                    ),
                    "upper_arm.R": BonePose(
                        armature_direction=(-0.07, -0.10, -0.29)
                    ),
                    "forearm.L": BonePose(
                        armature_direction=(0.02, -0.10, -0.22)
                    ),
                    "forearm.R": BonePose(
                        armature_direction=(-0.02, -0.10, -0.22)
                    ),
                    "hand.L": BonePose(
                        armature_direction=(0.01, -0.08, -0.04)
                    ),
                    "hand.R": BonePose(
                        armature_direction=(-0.01, -0.08, -0.04)
                    ),
                    "thigh.L": BonePose(
                        armature_direction=(0.04, 0.27, -0.27)
                    ),
                    "thigh.R": BonePose(
                        armature_direction=(-0.04, 0.27, -0.27)
                    ),
                    "shin.L": BonePose(
                        armature_direction=(0.01, 0.24, 0.02)
                    ),
                    "shin.R": BonePose(
                        armature_direction=(-0.01, 0.24, 0.02)
                    ),
                    "foot.L": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                    "foot.R": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                },
            )
            all_fours = self.merge_pose(
                prone_tuck,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(68.0, 0.0, sign * 2.0),
                        armature_location_m=(sign * 0.015, 0.03, -0.38),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(-10.0, 0.0, -sign * 1.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(6.0, 0.0, sign * 1.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-38.0, 0.0, 0.0)),
                    "head": BonePose(rotation_degrees=(-22.0, 0.0, 0.0)),
                    "upper_arm.L": BonePose(
                        armature_direction=(0.06, -0.10, -0.30)
                    ),
                    "upper_arm.R": BonePose(
                        armature_direction=(-0.06, -0.10, -0.30)
                    ),
                    "forearm.L": BonePose(
                        armature_direction=(0.02, -0.10, -0.22)
                    ),
                    "forearm.R": BonePose(
                        armature_direction=(-0.02, -0.10, -0.22)
                    ),
                    "hand.L": BonePose(
                        armature_direction=(0.01, -0.08, -0.04)
                    ),
                    "hand.R": BonePose(
                        armature_direction=(-0.01, -0.08, -0.04)
                    ),
                    "thigh.L": BonePose(
                        armature_direction=(0.04, 0.29, -0.25)
                    ),
                    "thigh.R": BonePose(
                        armature_direction=(-0.04, 0.29, -0.25)
                    ),
                    "shin.L": BonePose(
                        armature_direction=(0.01, 0.25, 0.02)
                    ),
                    "shin.R": BonePose(
                        armature_direction=(-0.01, 0.25, 0.02)
                    ),
                    "foot.L": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                    "foot.R": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                },
            )
            all_fours_shift = self.merge_pose(
                all_fours,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(66.0, 0.0, -sign * 3.0),
                        armature_location_m=(-sign * 0.018, 0.025, -0.375),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(-8.0, 0.0, sign * 2.0)
                    ),
                    f"upper_arm.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.055,
                            -0.11,
                            -0.30,
                        )
                    ),
                    f"forearm.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.018,
                            -0.10,
                            -0.22,
                        )
                    ),
                    f"thigh.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.045,
                            0.285,
                            -0.25,
                        )
                    ),
                    f"thigh.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.035,
                            0.295,
                            -0.25,
                        )
                    ),
                },
            )
            foot_lift = self.merge_pose(
                all_fours_shift,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(48.0, 0.0, -sign * 4.0),
                        armature_location_m=(-sign * 0.022, 0.02, -0.295),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(5.0, 0.0, sign * 2.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(-2.0, 0.0, -sign * 1.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-20.0, 0.0, 0.0)),
                    "head": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
                    f"thigh.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.04,
                            0.10,
                            -0.30,
                        )
                    ),
                    f"shin.{lead_suffix}": BonePose(
                        armature_direction=(lead_sign * 0.01, -0.20, 0.10)
                    ),
                    f"foot.{lead_suffix}": BonePose(
                        armature_direction=(0.0, -0.20, 0.0)
                    ),
                },
            )
            half_kneel = self.merge_pose(
                all_fours_shift,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(30.0, 0.0, -sign * 5.0),
                        armature_location_m=(-sign * 0.025, 0.015, -0.22),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(18.0, 0.0, sign * 2.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(-10.0, 0.0, -sign * 2.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                    "head": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
                    "upper_arm.L": BonePose(
                        armature_direction=(0.055, -0.08, -0.30)
                    ),
                    "upper_arm.R": BonePose(
                        armature_direction=(-0.055, -0.08, -0.30)
                    ),
                    "forearm.L": BonePose(
                        armature_direction=(0.018, -0.09, -0.22)
                    ),
                    "forearm.R": BonePose(
                        armature_direction=(-0.018, -0.09, -0.22)
                    ),
                    "hand.L": BonePose(
                        armature_direction=(0.01, -0.08, -0.04)
                    ),
                    "hand.R": BonePose(
                        armature_direction=(-0.01, -0.08, -0.04)
                    ),
                    f"thigh.{lead_suffix}": BonePose(
                        armature_direction=(
                            lead_sign * 0.04,
                            -0.25,
                            -0.28,
                        )
                    ),
                    f"shin.{lead_suffix}": BonePose(
                        armature_direction=(lead_sign * 0.01, -0.18, -0.06)
                    ),
                    f"foot.{lead_suffix}": BonePose(
                        armature_direction=(0.0, -0.20, 0.0)
                    ),
                    f"thigh.{trail_suffix}": BonePose(
                        armature_direction=(
                            -lead_sign * 0.035,
                            0.15,
                            -0.38,
                        )
                    ),
                    f"shin.{trail_suffix}": BonePose(
                        armature_direction=(-lead_sign * 0.01, 0.24, 0.10)
                    ),
                    f"foot.{trail_suffix}": BonePose(
                        armature_direction=(0.0, 0.20, -0.02)
                    ),
                },
            )
            low_crouch = self.merge_pose(
                relaxed,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(12.0, 0.0, -sign * 3.0),
                        armature_location_m=(-sign * 0.015, 0.0, -0.235),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(20.0, 0.0, sign * 2.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(-11.0, 0.0, -sign * 2.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
                    "head": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
                    "upper_arm.L": BonePose(
                        armature_direction=(0.07, -0.05, -0.31)
                    ),
                    "upper_arm.R": BonePose(
                        armature_direction=(-0.07, -0.05, -0.31)
                    ),
                    "forearm.L": BonePose(
                        rotation_degrees=(-32.0, 2.0, 14.0)
                    ),
                    "forearm.R": BonePose(
                        rotation_degrees=(-32.0, -2.0, -14.0)
                    ),
                    "hand.L": BonePose(
                        rotation_degrees=(8.0, -8.0, 6.0)
                    ),
                    "hand.R": BonePose(
                        rotation_degrees=(8.0, 8.0, -6.0)
                    ),
                    "thigh.L": BonePose(
                        armature_direction=(0.03, -0.26, -0.30)
                    ),
                    "thigh.R": BonePose(
                        armature_direction=(-0.03, -0.26, -0.30)
                    ),
                    "shin.L": BonePose(
                        armature_direction=(0.01, 0.23, -0.09)
                    ),
                    "shin.R": BonePose(
                        armature_direction=(-0.01, 0.23, -0.09)
                    ),
                    "foot.L": BonePose(
                        armature_direction=(0.0, -0.20, 0.0)
                    ),
                    "foot.R": BonePose(
                        armature_direction=(0.0, -0.20, 0.0)
                    ),
                },
            )
            crouch_leg_lift = self.merge_pose(
                half_kneel,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(20.0, 0.0, -sign * 4.0),
                        armature_location_m=(-sign * 0.02, 0.008, -0.275),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(20.0, 0.0, sign * 2.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(-11.0, 0.0, -sign * 2.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                    "head": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
                    "thigh.L": BonePose(
                        armature_direction=(0.03, -0.10, -0.32)
                    ),
                    "thigh.R": BonePose(
                        armature_direction=(-0.03, -0.10, -0.32)
                    ),
                    "shin.L": BonePose(
                        armature_direction=(0.01, -0.20, 0.10)
                    ),
                    "shin.R": BonePose(
                        armature_direction=(-0.01, -0.20, 0.10)
                    ),
                    "foot.L": BonePose(
                        armature_direction=(0.0, -0.20, 0.0)
                    ),
                    "foot.R": BonePose(
                        armature_direction=(0.0, -0.20, 0.0)
                    ),
                },
            )
            near_upright = self.merge_pose(
                relaxed,
                {
                    "pelvis": BonePose(
                        rotation_degrees=(7.0, 0.0, -sign * 1.5),
                        armature_location_m=(0.0, 0.0, 0.037),
                    ),
                    "spine": BonePose(
                        rotation_degrees=(13.0, 0.0, sign * 1.0)
                    ),
                    "chest": BonePose(
                        rotation_degrees=(-7.0, 0.0, -sign * 1.0)
                    ),
                    "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                    "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
                    "upper_arm.L": BonePose(
                        armature_direction=(0.07, -0.02, -0.32)
                    ),
                    "upper_arm.R": BonePose(
                        armature_direction=(-0.07, -0.02, -0.32)
                    ),
                    "forearm.L": BonePose(
                        rotation_degrees=(-18.0, 2.0, 8.0)
                    ),
                    "forearm.R": BonePose(
                        rotation_degrees=(-18.0, -2.0, -8.0)
                    ),
                    "hand.L": BonePose(
                        rotation_degrees=(4.0, -6.0, 3.0)
                    ),
                    "hand.R": BonePose(
                        rotation_degrees=(4.0, 6.0, -3.0)
                    ),
                    "thigh.L": BonePose(
                        rotation_degrees=(-18.0, 0.0, 2.0)
                    ),
                    "thigh.R": BonePose(
                        rotation_degrees=(-19.0, 0.0, -2.0)
                    ),
                    "shin.L": BonePose(
                        rotation_degrees=(24.0, 0.0, 0.0)
                    ),
                    "shin.R": BonePose(
                        rotation_degrees=(25.0, 0.0, 0.0)
                    ),
                    "foot.L": BonePose(
                        rotation_degrees=(-6.0, 0.0, 0.0)
                    ),
                    "foot.R": BonePose(
                        rotation_degrees=(-6.0, 0.0, 0.0)
                    ),
                },
            )
            self._create_action(
                f"Fall{side_name}", "fall", 0.45, False, 14, 31.111,
                ((0.0, relaxed), (0.42, stumble), (1.0, down)),
            )
            self._create_action(
                f"Down{side_name}", "fall", 1.20, False, 36, 30,
                ((0.0, down), (0.5, down_breath), (1.0, down)),
            )
            self._create_action(
                f"Rise{side_name}", "fall", 50.0 / 30.0, False, 50, 30,
                (
                    (0.0, down),
                    (0.10, brace),
                    (0.24, prone_tuck),
                    (0.38, all_fours),
                    (0.48, all_fours_shift),
                    (0.56, foot_lift),
                    (0.64, half_kneel),
                    (0.72, crouch_leg_lift),
                    (0.80, low_crouch),
                    (0.92, near_upright),
                    (1.0, relaxed),
                ),
            )

        # A tired man shifts his weight before he folds. The lead-in gives
        # the sit something to come out of instead of starting at speed.
        bed_stand_shift = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-1.5, 0.0, -3.5),
                    location_m=(0.0, -0.012, 0.0),
                ),
                "spine": BonePose(rotation_degrees=(-4.5, 0.0, 3.0)),
                "chest": BonePose(rotation_degrees=(4.0, 0.0, -2.0)),
                "neck": BonePose(rotation_degrees=(-3.0, 0.0, 1.5)),
                "head": BonePose(rotation_degrees=(4.0, 0.0, -1.5)),
                "upper_arm.L": BonePose(
                    target_direction=(0.070, -0.020, -0.330)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.055, -0.004, -0.331)
                ),
                "forearm.L": BonePose(rotation_degrees=(-7.0, 3.0, -2.0)),
                "forearm.R": BonePose(rotation_degrees=(-8.0, -3.0, 2.0)),
                "thigh.L": BonePose(rotation_degrees=(-4.0, 0.0, 2.0)),
                "thigh.R": BonePose(rotation_degrees=(-2.0, 0.0, -2.0)),
                "shin.L": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
            },
        )
        bed_edge_bend = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
                "spine": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
                "thigh.L": BonePose(rotation_degrees=(-22.0, 0.0, 3.0)),
                "thigh.R": BonePose(rotation_degrees=(-20.0, 0.0, -3.0)),
                "shin.L": BonePose(rotation_degrees=(27.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(25.0, 0.0, 0.0)),
            },
        )
        bed_edge_sit = self.merge_pose(
            bed_edge_bend,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-4.0, 0.0, 0.0),
                    location_m=(0.0, 0.01, -0.08),
                ),
                "spine": BonePose(rotation_degrees=(5.0, 0.0, -1.0)),
                "chest": BonePose(rotation_degrees=(-3.0, 0.0, 2.0)),
                "neck": BonePose(rotation_degrees=(-1.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-2.0, 0.0, -1.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.09, 0.06, -0.28)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.09, 0.06, -0.28)
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(4.0, 0.0, 10.0)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(4.0, 0.0, -10.0)
                ),
                # The mattress stands higher than his knee, so he perches
                # with his heels drawn back under him. Straighter legs would
                # leave the boots hanging in the air.
                "thigh.L": BonePose(rotation_degrees=(-42.0, 0.0, 5.0)),
                "thigh.R": BonePose(rotation_degrees=(-47.0, 0.0, -5.0)),
                "shin.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
            },
        )
        # The mattress absorbs him: the hips land, the spine compresses and
        # the shoulders drop a beat later. Without this the sit is a snap.
        bed_edge_settle = self.merge_pose(
            bed_edge_sit,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-1.0, 0.0, 0.0),
                    location_m=(0.0, 0.004, -0.085),
                ),
                "spine": BonePose(rotation_degrees=(11.0, 0.0, -1.0)),
                "chest": BonePose(rotation_degrees=(-6.0, 0.0, 2.0)),
                "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(3.0, 0.0, -1.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.085, 0.020, -0.300)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.085, 0.020, -0.300)
                ),
                "forearm.L": BonePose(rotation_degrees=(2.0, 0.0, 8.0)),
                "forearm.R": BonePose(rotation_degrees=(2.0, 0.0, -8.0)),
                "thigh.L": BonePose(rotation_degrees=(-45.0, 0.0, 5.0)),
                "thigh.R": BonePose(rotation_degrees=(-49.0, 0.0, -5.0)),
                "shin.L": BonePose(rotation_degrees=(33.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(33.0, 0.0, 0.0)),
            },
        )
        bed_edge_scoot = self.merge_pose(
            bed_edge_sit,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-8.0, 0.0, 0.0),
                    location_m=(0.0, 0.01, -0.10),
                ),
                "spine": BonePose(rotation_degrees=(16.0, 0.0, -2.0)),
                "chest": BonePose(rotation_degrees=(-8.0, 0.0, 3.0)),
                "head": BonePose(rotation_degrees=(-4.0, 0.0, -2.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.10, 0.11, -0.24)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.10, 0.11, -0.24)
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(8.0, 0.0, 12.0)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(8.0, 0.0, -12.0)
                ),
            },
        )
        bed_leg_swing = self.merge_pose(
            bed_edge_scoot,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-18.0, 24.0, 27.0),
                    location_m=(0.0, 0.02, -0.18),
                ),
                "spine": BonePose(rotation_degrees=(-20.0, 2.0, -7.0)),
                "chest": BonePose(rotation_degrees=(13.0, -2.0, 7.0)),
                "neck": BonePose(rotation_degrees=(8.0, 0.0, -2.0)),
                "head": BonePose(rotation_degrees=(-2.0, 0.0, -5.0)),
                # The head-side arm is already reaching back for the bed
                # rather than swinging beside it.
                "upper_arm.R": BonePose(
                    target_direction=(-0.070, -0.050, -0.250)
                ),
                "forearm.R": BonePose(rotation_degrees=(-14.0, -4.0, 4.0)),
                "hand.R": BonePose(rotation_degrees=(16.0, 6.0, -3.0)),
                "thigh.L": BonePose(rotation_degrees=(-18.0, 4.0, 7.0)),
                "thigh.R": BonePose(rotation_degrees=(-22.0, -4.0, -7.0)),
                "shin.L": BonePose(rotation_degrees=(28.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
            },
        )
        # He does not simply fall over: the head-side hand takes the weight
        # on the mattress and the torso rides down that arm.
        bed_lower_brace = self.merge_pose(
            bed_leg_swing,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-24.0, 37.0, 45.0),
                    location_m=(0.0, 0.025, -0.24),
                ),
                "spine": BonePose(rotation_degrees=(-17.0, 3.0, -9.0)),
                "chest": BonePose(rotation_degrees=(13.0, -3.0, 8.0)),
                "neck": BonePose(rotation_degrees=(9.0, 0.0, -3.0)),
                "head": BonePose(rotation_degrees=(-4.0, 0.0, -6.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.090, 0.030, -0.255)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.070, -0.050, -0.250)
                ),
                "forearm.L": BonePose(rotation_degrees=(-6.0, 3.0, -3.0)),
                "forearm.R": BonePose(rotation_degrees=(-14.0, -4.0, 4.0)),
                "hand.L": BonePose(rotation_degrees=(6.0, -5.0, 2.0)),
                "hand.R": BonePose(rotation_degrees=(16.0, 6.0, -3.0)),
                "thigh.L": BonePose(rotation_degrees=(-3.0, 3.0, 8.0)),
                "thigh.R": BonePose(rotation_degrees=(-8.0, -4.0, -8.0)),
                "shin.L": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(11.0, 0.0, 0.0)),
                "face.eye.L": BonePose(scale=(1.0, 0.72, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.70, 1.0)),
            },
        )
        bed_lower_side = self.merge_pose(
            bed_leg_swing,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-30.0, 50.0, 60.0),
                    location_m=(0.0, 0.03, -0.30),
                ),
                "spine": BonePose(rotation_degrees=(-14.0, 4.0, -10.0)),
                "chest": BonePose(rotation_degrees=(12.0, -4.0, 9.0)),
                "neck": BonePose(rotation_degrees=(8.0, 0.0, -3.0)),
                "head": BonePose(rotation_degrees=(-8.0, 0.0, -7.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.08, -0.04, -0.23)
                ),
                # Still propped on the same arm, now folding under him.
                "upper_arm.R": BonePose(
                    target_direction=(-0.075, -0.060, -0.190)
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(-4.0, 3.0, -2.0)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(-25.0, -4.0, 4.0)
                ),
                "hand.R": BonePose(rotation_degrees=(12.0, 6.0, -3.0)),
                "thigh.L": BonePose(rotation_degrees=(12.0, 3.0, 8.0)),
                "thigh.R": BonePose(rotation_degrees=(6.0, -4.0, -8.0)),
                "shin.L": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
                "face.eye.L": BonePose(scale=(1.0, 0.45, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.45, 1.0)),
            },
        )
        bed_roll_back = self.merge_pose(
            bed_lower_side,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-70.0, 78.0, 20.0),
                    location_m=(0.0, 0.04, -0.41),
                ),
                "spine": BonePose(rotation_degrees=(-8.0, 1.0, -6.0)),
                "chest": BonePose(rotation_degrees=(10.0, -1.0, 6.0)),
                "head": BonePose(rotation_degrees=(-11.0, 0.0, -5.0)),
                "thigh.L": BonePose(rotation_degrees=(18.0, 0.0, 6.0)),
                "thigh.R": BonePose(rotation_degrees=(8.0, 0.0, -6.0)),
                "shin.L": BonePose(rotation_degrees=(-28.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
                "face.eye.L": BonePose(scale=(1.0, 0.22, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.22, 1.0)),
            },
        )
        lying = {
            "pelvis": BonePose(
                rotation_degrees=(-88.0, 90.0, 0.0),
                location_m=(0.0, 0.04, -0.45),
            ),
            "spine": BonePose(rotation_degrees=(-4.0, 0.0, -3.0)),
            "chest": BonePose(rotation_degrees=(7.0, 0.0, 4.0)),
            "neck": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
            "head": BonePose(rotation_degrees=(-15.0, 0.0, -4.0)),
            "upper_arm.L": BonePose(rotation_degrees=(18.0, -8.0, 10.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-12.0, 6.0, -12.0)),
            "forearm.L": BonePose(rotation_degrees=(-50.0, 0.0, 18.0)),
            "forearm.R": BonePose(rotation_degrees=(-42.0, 0.0, -14.0)),
            "thigh.L": BonePose(rotation_degrees=(10.0, 0.0, 4.0)),
            "thigh.R": BonePose(rotation_degrees=(-6.0, 0.0, -5.0)),
            "shin.L": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
            "shin.R": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
            "face.eye.L": BonePose(scale=(1.0, 0.08, 1.0)),
            "face.eye.R": BonePose(scale=(1.0, 0.08, 1.0)),
        }
        # The terminal landmark is the deepest the body ever goes, so the
        # settle arrives just short of it and eases the rest of the way in.
        # Only the trailing half of the rig takes this key, which is what
        # leaves the head a few frames behind the shoulders.
        bed_settle_press = self.merge_pose(
            lying,
            {
                "chest": BonePose(rotation_degrees=(4.5, 0.0, 4.0)),
                "neck": BonePose(rotation_degrees=(11.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-10.0, 0.0, -4.0)),
                "upper_arm.L": BonePose(rotation_degrees=(15.0, -8.0, 10.0)),
                "upper_arm.R": BonePose(rotation_degrees=(-14.0, 6.0, -12.0)),
                "forearm.L": BonePose(rotation_degrees=(-44.0, 0.0, 18.0)),
                "forearm.R": BonePose(rotation_degrees=(-38.0, 0.0, -14.0)),
            },
        )
        # Sleep is not a metronome: the inhale is short, the exhale long, and
        # the chest, shoulder, arm and hand all take part. The neutral pose is
        # the fully exhaled one, so every breath lifts him off the mattress
        # rather than pressing him into it.
        lying_inhale_rise = self.merge_pose(
            lying,
            {
                "chest": BonePose(rotation_degrees=(6.2, 0.0, 4.1)),
                "neck": BonePose(rotation_degrees=(15.2, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-15.1, 0.0, -4.1)),
                "upper_arm.R": BonePose(rotation_degrees=(-12.4, 6.2, -12.2)),
            },
        )
        lying_inhale = self.merge_pose(
            lying,
            {
                "spine": BonePose(rotation_degrees=(-4.4, 0.0, -3.0)),
                "chest": BonePose(rotation_degrees=(4.9, 0.0, 4.2)),
                "neck": BonePose(rotation_degrees=(15.6, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-15.4, 0.0, -4.3)),
                "upper_arm.L": BonePose(rotation_degrees=(17.4, -8.4, 10.4)),
                "upper_arm.R": BonePose(rotation_degrees=(-12.8, 6.4, -12.5)),
                "forearm.L": BonePose(rotation_degrees=(-50.6, 0.0, 18.3)),
                "forearm.R": BonePose(rotation_degrees=(-42.6, 0.0, -14.3)),
                "hand.L": BonePose(rotation_degrees=(1.4, -5.0, 2.0)),
            },
        )
        lying_exhale_fall = self.merge_pose(
            lying,
            {
                "chest": BonePose(rotation_degrees=(6.0, 0.0, 4.0)),
                "neck": BonePose(rotation_degrees=(15.3, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-15.2, 0.0, -4.1)),
                "upper_arm.L": BonePose(rotation_degrees=(17.8, -8.2, 10.2)),
                "forearm.R": BonePose(rotation_degrees=(-42.3, 0.0, -14.1)),
            },
        )
        lying_settle = self.merge_pose(
            lying,
            {
                "chest": BonePose(rotation_degrees=(6.8, 0.0, 3.9)),
                "neck": BonePose(rotation_degrees=(15.1, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-15.1, 0.0, -3.9)),
                "upper_arm.L": BonePose(rotation_degrees=(17.9, -7.9, 9.8)),
                "forearm.L": BonePose(rotation_degrees=(-50.2, 0.0, 17.8)),
                "thigh.R": BonePose(rotation_degrees=(-6.4, 0.0, -5.0)),
                "shin.R": BonePose(rotation_degrees=(15.6, 0.0, 0.0)),
            },
        )
        lying_drift = self.merge_pose(
            lying,
            {
                "chest": BonePose(rotation_degrees=(6.9, 0.0, 3.9)),
                "neck": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-14.9, 0.0, -3.8)),
                "hand.L": BonePose(rotation_degrees=(1.2, -5.4, 2.2)),
                "hand.R": BonePose(rotation_degrees=(2.6, 5.3, -2.2)),
            },
        )
        bed_wake_stir = self.merge_pose(
            lying,
            {
                "spine": BonePose(rotation_degrees=(-4.5, 0.0, -2.0)),
                "chest": BonePose(rotation_degrees=(6.5, 0.0, 3.0)),
                "neck": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-13.0, 0.0, -2.0)),
                "upper_arm.L": BonePose(
                    rotation_degrees=(18.0, -8.0, 10.0)
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(-56.0, 0.0, 20.0)
                ),
                "thigh.L": BonePose(rotation_degrees=(4.0, 0.0, 5.0)),
                "shin.L": BonePose(rotation_degrees=(-24.0, 0.0, 0.0)),
                "face.eye.L": BonePose(scale=(1.0, 0.42, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.42, 1.0)),
            },
        )
        # Waking starts at the head. The chest and the near arm answer it,
        # and only then does the body follow them over onto its side.
        bed_wake_head_lift = self.merge_pose(
            bed_wake_stir,
            {
                "spine": BonePose(rotation_degrees=(-5.0, 0.0, -2.0)),
                "chest": BonePose(rotation_degrees=(6.0, 0.0, 3.0)),
                "neck": BonePose(rotation_degrees=(32.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-14.0, 0.0, -2.0)),
                "upper_arm.L": BonePose(rotation_degrees=(18.0, -9.0, 10.0)),
                "forearm.L": BonePose(rotation_degrees=(-60.0, 0.0, 22.0)),
                "hand.L": BonePose(rotation_degrees=(10.0, -6.0, 3.0)),
                "face.eye.L": BonePose(scale=(1.0, 0.60, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.58, 1.0)),
            },
        )
        # Waking is a sit-up, not a roll. He curls onto his elbows, pushes up
        # onto his hands and arrives hunched on the mattress with both feet
        # drawn under him — then the legs go over the edge one at a time.
        bed_wake_elbows = self.merge_pose(
            lying,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-62.0, 64.0, 0.0),
                    location_m=(0.0, 0.03, -0.38),
                ),
                "spine": BonePose(rotation_degrees=(4.0, 0.0, -2.0)),
                "chest": BonePose(rotation_degrees=(2.0, 0.0, 3.0)),
                "neck": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-10.0, 0.0, -3.0)),
                "upper_arm.L": BonePose(rotation_degrees=(34.0, -14.0, 16.0)),
                "upper_arm.R": BonePose(rotation_degrees=(-30.0, 12.0, -18.0)),
                "forearm.L": BonePose(rotation_degrees=(-74.0, 0.0, 24.0)),
                "forearm.R": BonePose(rotation_degrees=(-72.0, 0.0, -20.0)),
                # The thighs have to fold as fast as the pelvis rises. Left
                # at their lying angle they would swing straight down through
                # the mattress as the torso comes up.
                "thigh.L": BonePose(rotation_degrees=(-28.0, 0.0, 4.0)),
                "thigh.R": BonePose(rotation_degrees=(-38.0, 0.0, -5.0)),
                "shin.L": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
                "face.eye.L": BonePose(scale=(1.0, 0.72, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.70, 1.0)),
            },
        )
        bed_wake_curl = self.merge_pose(
            bed_wake_elbows,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-32.0, 34.0, 0.0),
                    location_m=(0.0, 0.02, -0.24),
                ),
                "spine": BonePose(rotation_degrees=(14.0, 0.0, -1.0)),
                "chest": BonePose(rotation_degrees=(-4.0, 0.0, 2.0)),
                "neck": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(6.0, 0.0, -2.0)),
                "upper_arm.L": BonePose(rotation_degrees=(22.0, -10.0, 12.0)),
                "upper_arm.R": BonePose(rotation_degrees=(-20.0, 9.0, -13.0)),
                "forearm.L": BonePose(rotation_degrees=(-44.0, 0.0, 16.0)),
                "forearm.R": BonePose(rotation_degrees=(-42.0, 0.0, -14.0)),
                "thigh.L": BonePose(rotation_degrees=(-78.0, 0.0, 5.0)),
                "thigh.R": BonePose(rotation_degrees=(-86.0, 0.0, -6.0)),
                "shin.L": BonePose(rotation_degrees=(56.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(64.0, 0.0, 0.0)),
                "face.eye.L": BonePose(scale=(1.0, 0.88, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 0.86, 1.0)),
            },
        )
        # The half-crouch on the mattress: knees drawn up, both boots flat on
        # the bedding, weight forward over them, head still down.
        bed_wake_hunch = self.merge_pose(
            bed_wake_curl,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-4.0, 3.0, 0.0),
                    location_m=(0.0, 0.01, -0.10),
                ),
                "spine": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-10.0, 0.0, 1.0)),
                "neck": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(14.0, 0.0, -1.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.085, 0.070, -0.300)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.085, 0.070, -0.300)
                ),
                "forearm.L": BonePose(rotation_degrees=(-30.0, 3.0, 6.0)),
                "forearm.R": BonePose(rotation_degrees=(-31.0, -3.0, -6.0)),
                "hand.L": BonePose(rotation_degrees=(8.0, -5.0, 2.0)),
                "hand.R": BonePose(rotation_degrees=(8.0, 5.0, -2.0)),
                "thigh.L": BonePose(rotation_degrees=(-122.0, 0.0, 7.0)),
                "thigh.R": BonePose(rotation_degrees=(-128.0, 0.0, -7.0)),
                "shin.L": BonePose(rotation_degrees=(92.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(96.0, 0.0, 0.0)),
                "face.eye.L": BonePose(scale=(1.0, 1.0, 1.0)),
                "face.eye.R": BonePose(scale=(1.0, 1.0, 1.0)),
            },
        )
        # The right leg is the one nearest the door-side edge, so it goes
        # first. The knee opens outward before the foot ever leaves the bed.
        bed_wake_right_lead = self.merge_pose(
            bed_wake_hunch,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-2.0, -4.0, 0.0),
                    location_m=(0.0, 0.01, -0.09),
                ),
                "thigh.R": BonePose(rotation_degrees=(-122.0, -14.0, -18.0)),
                "shin.R": BonePose(rotation_degrees=(88.0, 0.0, 0.0)),
            },
        )
        bed_wake_right_down = self.merge_pose(
            bed_wake_hunch,
            {
                "pelvis": BonePose(
                    rotation_degrees=(0.0, -2.0, 0.0),
                    location_m=(0.0, 0.005, -0.08),
                ),
                "spine": BonePose(rotation_degrees=(20.0, 0.0, -1.0)),
                "chest": BonePose(rotation_degrees=(-8.0, 0.0, 2.0)),
                "head": BonePose(rotation_degrees=(11.0, 0.0, -2.0)),
                "thigh.R": BonePose(rotation_degrees=(-47.0, -4.0, -6.0)),
                "shin.R": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
            },
        )
        bed_wake_left_down = self.merge_pose(
            bed_wake_right_down,
            {
                "pelvis": BonePose(
                    rotation_degrees=(0.0, 0.0, 0.0),
                    location_m=(0.0, 0.0, -0.07),
                ),
                "spine": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-7.0, 0.0, 1.0)),
                "head": BonePose(rotation_degrees=(9.0, 0.0, -1.0)),
                "thigh.L": BonePose(rotation_degrees=(-42.0, 0.0, 5.0)),
                "shin.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
            },
        )
        bed_wake_sit = self.merge_pose(
            bed_edge_sit,
            {
                "pelvis": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
                "spine": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-2.0, 0.0, 1.0)),
                "neck": BonePose(rotation_degrees=(-1.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-2.0, 0.0, -1.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.06, -0.03, -0.31)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.06, -0.03, -0.31)
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(-14.0, 0.0, 8.0)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(-14.0, 0.0, -8.0)
                ),
            },
        )
        # One held beat on the edge with his hands on his knees. It is the
        # difference between a man waking up and a puppet being stood up.
        bed_wake_gather = self.merge_pose(
            bed_wake_sit,
            {
                "pelvis": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
                "spine": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-7.0, 0.0, 1.0)),
                "neck": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.075, 0.055, -0.315)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.075, 0.055, -0.315)
                ),
                "forearm.L": BonePose(rotation_degrees=(-26.0, 2.0, 6.0)),
                "forearm.R": BonePose(rotation_degrees=(-27.0, -2.0, -6.0)),
                "hand.L": BonePose(rotation_degrees=(10.0, -4.0, 2.0)),
                "hand.R": BonePose(rotation_degrees=(10.0, 4.0, -2.0)),
                "thigh.L": BonePose(rotation_degrees=(-37.0, 0.0, 4.0)),
                "thigh.R": BonePose(rotation_degrees=(-41.0, 0.0, -4.0)),
                "shin.L": BonePose(rotation_degrees=(43.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(42.0, 0.0, 0.0)),
            },
        )
        bed_wake_lean = self.merge_pose(
            bed_wake_sit,
            {
                "pelvis": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "spine": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-10.0, 0.0, 1.0)),
                "neck": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.059, -0.014, -0.334)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.047, -0.010, -0.334)
                ),
                "forearm.L": BonePose(rotation_degrees=(-4.0, 3.0, -2.0)),
                "forearm.R": BonePose(rotation_degrees=(-5.0, -3.0, 2.0)),
                "thigh.L": BonePose(rotation_degrees=(-39.0, 0.0, 4.0)),
                "thigh.R": BonePose(rotation_degrees=(-43.0, 0.0, -4.0)),
                "shin.L": BonePose(rotation_degrees=(45.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(44.0, 0.0, 0.0)),
            },
        )
        bed_wake_rise = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
                "spine": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
                "upper_arm.L": BonePose(
                    rotation_degrees=(18.0, -4.0, 9.0)
                ),
                "upper_arm.R": BonePose(
                    rotation_degrees=(-17.0, 4.0, -9.0)
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(-37.0, 0.0, 16.0)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(-38.0, 0.0, -15.0)
                ),
                "thigh.L": BonePose(rotation_degrees=(-32.0, 0.0, 3.0)),
                "thigh.R": BonePose(rotation_degrees=(-34.0, 0.0, -3.0)),
                "shin.L": BonePose(rotation_degrees=(38.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(39.0, 0.0, 0.0)),
            },
        )
        # Bed is the one place the hero gives his whole weight to the world,
        # so its three Actions carry eased curves and staggered keys. The
        # pelvis takes a landmark first and the chest, head and arms take the
        # same landmark a few frames later, which is what reads as a body
        # instead of a plank. Both endpoints still key the whole rig, so the
        # neutral and terminal seams stay exact.
        self._create_action(
            "BedEnter", "bed", 3.75, False, 45, 12,
            (
                (0.0, relaxed),
                (0.07, bed_stand_shift),
                (0.17, bed_edge_bend),
                (0.28, bed_edge_sit, BED_LEADING_BONES),
                (0.31, bed_edge_sit, BED_TRAILING_BONES),
                (0.36, bed_edge_settle),
                (0.44, bed_edge_scoot),
                (0.55, bed_leg_swing, BED_LEADING_BONES),
                (0.59, bed_leg_swing, BED_TRAILING_BONES),
                (0.67, bed_lower_brace),
                (0.78, bed_lower_side, BED_LEADING_BONES),
                (0.82, bed_lower_side, BED_TRAILING_BONES),
                (0.89, bed_roll_back, BED_LEADING_BONES),
                (0.93, bed_roll_back, BED_TRAILING_BONES),
                (0.97, bed_settle_press, BED_TRAILING_BONES),
                (1.0, lying),
            ),
            interpolation="BEZIER",
        )
        self._create_action(
            "BedSleepLoop", "bed", 4.0, True, 16, 4,
            (
                (0.0, lying),
                (0.16, lying_inhale_rise, BED_TRAILING_BONES),
                (0.40, lying_inhale),
                (0.58, lying_exhale_fall, BED_TRAILING_BONES),
                (0.78, lying_settle),
                (0.90, lying_drift, BED_TRAILING_BONES),
                (1.0, lying),
            ),
            interpolation="BEZIER",
        )
        # Waking has four beats and they are deliberately separate: he sits up
        # on the mattress into a half-crouch, drops the right leg over the
        # near edge, then the left, and only then stands. There is no roll —
        # a man getting out of bed on the door side has no reason to turn
        # onto his shoulder first, and the sit-up keeps his weight over the
        # bed the whole way, which is also what makes it checkable.
        self._create_action(
            "BedExit", "bed", 6.0, False, 72, 12,
            (
                (0.0, lying),
                (0.05, bed_wake_stir),
                (0.11, bed_wake_head_lift, BED_TRAILING_BONES),
                (0.20, bed_wake_elbows),
                (0.30, bed_wake_curl, BED_LEADING_BONES),
                (0.33, bed_wake_curl, BED_TRAILING_BONES),
                (0.43, bed_wake_hunch, BED_LEADING_BONES),
                (0.46, bed_wake_hunch, BED_TRAILING_BONES),
                (0.55, bed_wake_right_lead, BED_LEADING_BONES),
                (0.63, bed_wake_right_down, BED_LEADING_BONES),
                (0.66, bed_wake_right_down, BED_TRAILING_BONES),
                # The left leg is held up on the bedding while the right one
                # finds the floor. Without this key the two swings blur into
                # one and the whole point of the beat is lost.
                (0.68, bed_wake_hunch, ("thigh.L", "shin.L", "foot.L")),
                (0.74, bed_wake_left_down, BED_LEADING_BONES),
                (0.77, bed_wake_left_down, BED_TRAILING_BONES),
                (0.80, bed_wake_sit),
                (0.85, bed_wake_gather),
                (0.91, bed_wake_lean),
                (0.96, bed_wake_rise),
                (1.0, relaxed),
            ),
            interpolation="BEZIER",
        )

        smoke_rest = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(0.8, -0.4, -1.6)),
                "spine": BonePose(rotation_degrees=(-2.8, 0.0, 1.8)),
                "chest": BonePose(rotation_degrees=(3.2, 0.0, -1.8)),
                "neck": BonePose(rotation_degrees=(-1.5, 0.0, 1.0)),
                "head": BonePose(rotation_degrees=(1.0, 0.0, -0.8)),
            },
        )
        smoke_settle = self.merge_pose(
            smoke_rest,
            {
                "pelvis": BonePose(rotation_degrees=(1.2, -0.6, -2.2)),
                "spine": BonePose(rotation_degrees=(-3.4, 0.0, 2.4)),
                "head": BonePose(rotation_degrees=(2.0, 0.0, -1.4)),
            },
        )
        smoke_reach = self.merge_pose(
            smoke_settle,
            {
                "upper_arm.R": BonePose(target_direction=(0.03, -0.12, -0.22)),
                "forearm.R": BonePose(rotation_degrees=(-48.0, 10.0, -24.0)),
                "hand.R": BonePose(rotation_degrees=(14.0, -8.0, 6.0)),
                "head": BonePose(rotation_degrees=(5.0, 0.0, 1.8)),
            },
        )
        smoke_draw_low = self.merge_pose(
            smoke_settle,
            {
                "upper_arm.R": BonePose(target_direction=(-0.03, -0.12, -0.24)),
                "forearm.R": BonePose(rotation_degrees=(-38.0, 5.0, -18.0)),
                "hand.R": BonePose(rotation_degrees=(12.0, -12.0, 8.0)),
                "head": BonePose(rotation_degrees=(2.5, 0.0, 0.5)),
            },
        )
        smoke_lift = self.merge_pose(
            relaxed,
            {
                "upper_arm.R": BonePose(rotation_degrees=(8.0, -8.0, 48.0)),
                "forearm.R": BonePose(rotation_degrees=(5.0, -28.0, 60.0)),
                "hand.R": BonePose(rotation_degrees=(0.0, -28.0, 10.0)),
                "chest": BonePose(rotation_degrees=(3.2, 0.0, -1.4)),
                "head": BonePose(rotation_degrees=(0.5, 0.0, -0.6)),
            },
        )
        smoke_mouth = self.merge_pose(
            relaxed,
            {
                # These local rotations put the cigarette socket head at the
                # mouth while keeping its head-to-tail +Y axis pointing out.
                "upper_arm.R": BonePose(
                    rotation_degrees=(15.96, -13.89, 84.82),
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(11.05, -47.61, 104.20),
                ),
                "hand.R": BonePose(
                    rotation_degrees=(-8.73, -50.19, 13.66),
                ),
            },
        )
        smoke_light = self.merge_pose(
            smoke_mouth,
            {
                "upper_arm.L": BonePose(
                    rotation_degrees=(10.0, 10.0, -70.0),
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(5.0, 38.0, -88.0),
                ),
                "hand.L": BonePose(rotation_degrees=(-5.0, 35.0, -10.0)),
            },
        )
        smoke_first_inhale = self.merge_pose(
            smoke_light,
            {
                "spine": BonePose(rotation_degrees=(-2.4, 0.0, 1.2)),
                "chest": BonePose(rotation_degrees=(4.4, 0.0, -1.4)),
                "upper_arm.L": BonePose(
                    rotation_degrees=(8.0, 8.0, -62.0),
                ),
                "forearm.L": BonePose(
                    rotation_degrees=(4.0, 32.0, -78.0),
                ),
            },
        )
        smoke_inhale = self.merge_pose(
            smoke_mouth,
            {
                "spine": BonePose(rotation_degrees=(-2.4, 0.0, 1.2)),
                "chest": BonePose(rotation_degrees=(4.4, 0.0, -1.4)),
            },
        )
        smoke_breath_hold = self.merge_pose(
            smoke_mouth,
            {
                "spine": BonePose(rotation_degrees=(-2.1, 0.0, 1.0)),
                "chest": BonePose(rotation_degrees=(4.0, 0.0, -1.3)),
            },
        )
        smoke_lower = self.merge_pose(
            relaxed,
            {
                "upper_arm.R": BonePose(rotation_degrees=(7.0, -7.0, 42.0)),
                "forearm.R": BonePose(rotation_degrees=(2.0, -22.0, 52.0)),
                "hand.R": BonePose(rotation_degrees=(2.0, -20.0, 8.0)),
                "chest": BonePose(rotation_degrees=(3.4, 0.0, -1.5)),
            },
        )
        smoke_exhale = self.merge_pose(
            smoke_rest,
            {
                "chest": BonePose(rotation_degrees=(2.2, 0.0, -1.6)),
                "neck": BonePose(rotation_degrees=(-0.5, 0.0, 2.0)),
                "head": BonePose(rotation_degrees=(2.8, 0.0, 3.8)),
            },
        )
        smoke_inspect = self.merge_pose(
            smoke_draw_low,
            {
                "forearm.R": BonePose(rotation_degrees=(-24.0, 2.0, -12.0)),
                "hand.R": BonePose(rotation_degrees=(8.0, -16.0, 12.0)),
                "head": BonePose(rotation_degrees=(7.0, 0.0, 4.0)),
            },
        )
        smoke_extend = self.merge_pose(
            smoke_settle,
            {
                "upper_arm.R": BonePose(target_direction=(-0.08, -0.26, -0.04)),
                "forearm.R": BonePose(rotation_degrees=(-12.0, -8.0, 12.0)),
                "hand.R": BonePose(rotation_degrees=(8.0, -12.0, 14.0)),
                "head": BonePose(rotation_degrees=(2.0, 0.0, -1.0)),
            },
        )
        smoke_flick = self.merge_pose(
            smoke_extend,
            {
                "hand.R": BonePose(rotation_degrees=(28.0, -12.0, 24.0)),
                "head": BonePose(rotation_degrees=(4.0, 0.0, -0.5)),
            },
        )
        smoke_gaze_follow = self.merge_pose(
            smoke_settle,
            {
                "upper_arm.R": BonePose(target_direction=(-0.06, -0.24, -0.10)),
                "forearm.R": BonePose(rotation_degrees=(-8.0, -6.0, 8.0)),
                "hand.R": BonePose(rotation_degrees=(12.0, -8.0, 10.0)),
                "neck": BonePose(rotation_degrees=(-1.0, 0.0, 2.0)),
                "head": BonePose(rotation_degrees=(8.0, 0.0, 5.0)),
            },
        )
        smoke_return = self.merge_pose(
            smoke_rest,
            {
                "upper_arm.R": BonePose(rotation_degrees=(3.0, -3.0, 18.0)),
                "forearm.R": BonePose(rotation_degrees=(-2.0, -8.0, 18.0)),
                "hand.R": BonePose(rotation_degrees=(5.0, -8.0, 6.0)),
            },
        )
        self._create_action(
            "SmokeEnter", "smoking", 4.0, False, 48, 12,
            (
                (0.0, relaxed),
                (0.16, smoke_settle),
                (0.30, smoke_reach),
                (0.40, smoke_draw_low),
                (0.55, smoke_lift),
                (0.68, smoke_mouth),
                (0.76, smoke_light),
                (0.84, smoke_first_inhale),
                (0.92, smoke_lower),
                (1.0, smoke_rest),
            ),
        )
        self._create_action(
            "SmokeLoop", "smoking", 4.0, True, 24, 6,
            (
                (0.0, smoke_rest),
                (1.0 / 6.0, smoke_rest),
                (0.29, smoke_lift),
                (0.42, smoke_mouth),
                (0.50, smoke_inhale),
                (0.625, smoke_breath_hold),
                (0.70, smoke_lower),
                (0.83, smoke_exhale),
                (0.875, smoke_rest),
                (1.0, smoke_rest),
            ),
        )
        self._create_action(
            "SmokeExit", "smoking", 2.0, False, 24, 12,
            (
                (0.0, smoke_rest),
                (0.20, smoke_inspect),
                (0.38, smoke_extend),
                (0.50, smoke_flick),
                (0.66, smoke_gaze_follow),
                (0.82, smoke_return),
                (1.0, relaxed),
            ),
        )

        # Cat feeding is staged against the MiddleFlight rail cat, whose
        # authored feeding dip bottoms the muzzle out ~0.29 m above its
        # rail perch (head pivot 0.38 up, muzzle only 0.112 m from it),
        # so the tin can never rest on the rail itself: the hero HOLDS
        # it up to the cat for the whole loop. Torso pitch is
        # POSITIVE-X forward on this rig (probe-measured 2026-08-23;
        # the previous negative values arched the hero backward). The
        # offer pose solves both arm bones in absolute armature space
        # so the grip presents the tin at the dipped muzzle: ~0.36 m
        # forward, ~1.39 m up.
        feed_carry = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
                "spine": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
                "upper_arm.L": BonePose(target_direction=(-0.04, -0.45, -0.78)),
                "upper_arm.R": BonePose(target_direction=(0.04, -0.45, -0.78)),
                "forearm.L": BonePose(rotation_degrees=(-34.0, 0.0, 6.0)),
                "forearm.R": BonePose(rotation_degrees=(-34.0, 0.0, -6.0)),
            },
        )
        feed_offer = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
                "spine": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
                "upper_arm.L": BonePose(
                    armature_direction=(0.03, -0.58, -0.81)
                ),
                "upper_arm.R": BonePose(
                    armature_direction=(-0.03, -0.58, -0.81)
                ),
                "forearm.L": BonePose(
                    armature_direction=(-0.26, -0.34, 0.90)
                ),
                "forearm.R": BonePose(
                    armature_direction=(0.26, -0.34, 0.90)
                ),
            },
        )
        # The held tin breathes with the hero: a few millimetres of
        # forearm settle, nothing that would pull the rim off the
        # chewing muzzle.
        feed_offer_settle = self.merge_pose(
            feed_offer,
            {
                "chest": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
                "forearm.L": BonePose(
                    armature_direction=(-0.26, -0.36, 0.88)
                ),
                "forearm.R": BonePose(
                    armature_direction=(0.26, -0.36, 0.88)
                ),
            },
        )
        self._create_action(
            "CatFeedEnter", "cat_feeding", 2.0, False, 24, 12,
            (
                (0.0, relaxed),
                (0.35, feed_carry),
                (0.8, feed_offer),
                (1.0, feed_offer),
            ),
        )
        # Both loop endpoints hold the offer: the exit clip starts from
        # the same raised tin the enter clip delivered, and the cat's
        # 16-step timeline chews through the middle.
        self._create_action(
            "CatFeedLoop", "cat_feeding", 16.0 / 6.0, True, 16, 6,
            (
                (0.0, feed_offer),
                (0.5, feed_offer_settle),
                (1.0, feed_offer),
            ),
        )
        self._create_action(
            "CatFeedExit", "cat_feeding", 2.0, False, 24, 12,
            (
                (0.0, feed_offer),
                (0.45, feed_carry),
                (1.0, relaxed),
            ),
        )

        # A compact standing door gesture. The feet and pelvis stay on the
        # ordinary dock while the upper body inclines into one short physical-
        # right-hand press. Runtime aligns the fixed pelvis to each authored
        # doorway, so the source Action does not translate the gameplay root.
        door_anticipation = self.merge_pose(
            relaxed,
            {
                "spine": BonePose(rotation_degrees=(0.5, 0.0, 0.8)),
                "chest": BonePose(rotation_degrees=(1.0, 0.0, -0.8)),
                "neck": BonePose(rotation_degrees=(-1.0, 0.0, 0.4)),
                "head": BonePose(rotation_degrees=(1.0, 0.0, -0.3)),
                "upper_arm.R": BonePose(
                    target_direction=(-0.050, -0.090, -0.285)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(-26.0, -5.0, 8.0)
                ),
                "hand.R": BonePose(rotation_degrees=(5.0, -7.0, 6.0)),
            },
        )
        door_reach = self.merge_pose(
            relaxed,
            {
                "spine": BonePose(rotation_degrees=(4.0, 0.0, 0.6)),
                "chest": BonePose(rotation_degrees=(-0.5, 0.0, -0.6)),
                "neck": BonePose(rotation_degrees=(-1.5, 0.0, 0.2)),
                "head": BonePose(rotation_degrees=(2.5, 0.0, -0.2)),
                "upper_arm.R": BonePose(
                    target_direction=(-0.070, -0.215, -0.115)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(-18.0, -6.0, 10.0)
                ),
                "hand.R": BonePose(rotation_degrees=(6.0, -9.0, 8.0)),
            },
        )
        door_contact = self.merge_pose(
            relaxed,
            {
                "spine": BonePose(rotation_degrees=(7.0, 0.0, 0.4)),
                "chest": BonePose(rotation_degrees=(-2.0, 0.0, -0.4)),
                "neck": BonePose(rotation_degrees=(-2.0, 0.0, 0.1)),
                "head": BonePose(rotation_degrees=(3.5, 0.0, -0.1)),
                "upper_arm.R": BonePose(
                    target_direction=(-0.075, -0.215, -0.115)
                ),
                "forearm.R": BonePose(
                    rotation_degrees=(-16.0, -7.0, 11.0)
                ),
                "hand.R": BonePose(rotation_degrees=(7.0, -10.0, 9.0)),
            },
        )
        self._create_action(
            "DoorUseEnter", "door_use", 0.5, False, 6, 12,
            (
                (0.0, relaxed),
                (0.20, door_anticipation),
                (0.62, door_reach),
                (1.0, door_contact),
            ),
        )
        self._create_action(
            "DoorUseLoop", "door_use", 0.25, True, 2, 8,
            ((0.0, door_contact), (1.0, door_contact)),
        )
        self._create_action(
            "DoorUseExit", "door_use", 0.5, False, 6, 12,
            (
                (0.0, door_contact),
                (0.38, door_reach),
                (0.72, door_anticipation),
                (1.0, relaxed),
            ),
        )

        bus_board_step = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(5.0, -1.5, -2.0),
                    location_m=(0.0, -0.015, 0.025),
                ),
                "spine": BonePose(rotation_degrees=(13.0, 0.0, 2.0)),
                "chest": BonePose(rotation_degrees=(-6.0, 0.0, -2.0)),
                "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.5)),
                "head": BonePose(rotation_degrees=(-4.0, 0.0, -0.5)),
                "upper_arm.L": BonePose(
                    target_direction=(0.10, -0.23, -0.13)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.06, -0.15, -0.24)
                ),
                "forearm.L": BonePose(rotation_degrees=(-24.0, 2.0, 14.0)),
                "forearm.R": BonePose(rotation_degrees=(-18.0, -2.0, -8.0)),
                "hand.L": BonePose(rotation_degrees=(4.0, -8.0, 6.0)),
                "hand.R": BonePose(rotation_degrees=(3.0, 6.0, -4.0)),
                "thigh.L": BonePose(rotation_degrees=(-42.0, 0.0, 4.0)),
                "shin.L": BonePose(rotation_degrees=(62.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-16.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(9.0, 0.0, -2.0)),
                "shin.R": BonePose(rotation_degrees=(17.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
            },
        )
        bus_board_through = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(3.0, 1.0, 2.0),
                    location_m=(0.0, -0.025, 0.045),
                ),
                "spine": BonePose(rotation_degrees=(10.0, 0.0, -1.5)),
                "chest": BonePose(rotation_degrees=(-4.0, 0.0, 1.5)),
                "neck": BonePose(rotation_degrees=(-2.0, 0.0, -0.5)),
                "head": BonePose(rotation_degrees=(-3.0, 0.0, 0.5)),
                "upper_arm.L": BonePose(
                    target_direction=(0.08, -0.21, -0.17)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.11, -0.24, -0.11)
                ),
                "forearm.L": BonePose(rotation_degrees=(-19.0, 2.0, 10.0)),
                "forearm.R": BonePose(rotation_degrees=(-26.0, -2.0, -14.0)),
                "hand.L": BonePose(rotation_degrees=(3.0, -7.0, 4.0)),
                "hand.R": BonePose(rotation_degrees=(4.0, 8.0, -6.0)),
                "thigh.L": BonePose(rotation_degrees=(5.0, 0.0, 2.0)),
                "shin.L": BonePose(rotation_degrees=(19.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-38.0, 0.0, -4.0)),
                "shin.R": BonePose(rotation_degrees=(58.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-15.0, 0.0, 0.0)),
            },
        )
        bus_seat_prepare = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(7.0, 0.0, 0.0),
                    location_m=(0.0, 0.035, -0.045),
                ),
                "spine": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.09, -0.13, -0.25)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.09, -0.13, -0.25)
                ),
                "forearm.L": BonePose(rotation_degrees=(-18.0, 0.0, 10.0)),
                "forearm.R": BonePose(rotation_degrees=(-18.0, 0.0, -10.0)),
                "thigh.L": BonePose(rotation_degrees=(-31.0, 0.0, 3.0)),
                "thigh.R": BonePose(rotation_degrees=(-33.0, 0.0, -3.0)),
                "shin.L": BonePose(rotation_degrees=(36.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(37.0, 0.0, 0.0)),
            },
        )
        bus_seat_lower = self.merge_pose(
            bus_seat_prepare,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-2.0, 0.0, 0.0),
                    location_m=(0.0, 0.075, -0.19),
                ),
                "spine": BonePose(rotation_degrees=(11.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
                "thigh.L": BonePose(rotation_degrees=(-56.0, 0.0, 3.0)),
                "thigh.R": BonePose(rotation_degrees=(-58.0, 0.0, -3.0)),
                "shin.L": BonePose(rotation_degrees=(61.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(62.0, 0.0, 0.0)),
            },
        )
        bus_seated = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-6.0, 0.0, 0.0),
                    location_m=(0.0, 0.095, -0.285),
                ),
                "spine": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-1.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(1.5, 0.0, -0.4)),
                "upper_arm.L": BonePose(
                    target_direction=(0.075, -0.145, -0.245)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.075, -0.145, -0.245)
                ),
                "forearm.L": BonePose(rotation_degrees=(-31.0, 0.0, 11.0)),
                "forearm.R": BonePose(rotation_degrees=(-31.0, 0.0, -11.0)),
                "hand.L": BonePose(rotation_degrees=(5.0, -7.0, 3.0)),
                "hand.R": BonePose(rotation_degrees=(5.0, 7.0, -3.0)),
                "thigh.L": BonePose(rotation_degrees=(-68.0, 0.0, 3.0)),
                "thigh.R": BonePose(rotation_degrees=(-70.0, 0.0, -3.0)),
                "shin.L": BonePose(rotation_degrees=(73.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(74.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
            },
        )
        bus_ride_left = self.merge_pose(
            bus_seated,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-5.5, 0.4, -1.4),
                    location_m=(0.005, 0.095, -0.282),
                ),
                "spine": BonePose(rotation_degrees=(4.2, 0.0, 1.8)),
                "chest": BonePose(rotation_degrees=(-1.0, 0.0, -2.0)),
                "neck": BonePose(rotation_degrees=(-0.8, 0.0, 0.8)),
                "head": BonePose(rotation_degrees=(1.2, 0.0, -1.0)),
            },
        )
        bus_ride_breath = self.merge_pose(
            bus_seated,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-6.2, 0.0, 0.0),
                    location_m=(0.0, 0.095, -0.287),
                ),
                "spine": BonePose(rotation_degrees=(4.4, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(0.2, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-1.4, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(1.8, 0.0, -0.4)),
            },
        )
        bus_ride_right = self.merge_pose(
            bus_seated,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-5.5, -0.4, 1.4),
                    location_m=(-0.005, 0.095, -0.282),
                ),
                "spine": BonePose(rotation_degrees=(4.2, 0.0, -1.8)),
                "chest": BonePose(rotation_degrees=(-1.0, 0.0, 2.0)),
                "neck": BonePose(rotation_degrees=(-0.8, 0.0, -0.8)),
                "head": BonePose(rotation_degrees=(1.2, 0.0, 0.2)),
            },
        )
        bus_alight_lean = self.merge_pose(
            bus_seat_lower,
            {
                "pelvis": BonePose(
                    rotation_degrees=(5.0, 0.0, 0.0),
                    location_m=(0.0, 0.065, -0.17),
                ),
                "spine": BonePose(rotation_degrees=(19.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-9.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
            },
        )
        bus_alight_rise = self.merge_pose(
            bus_seat_prepare,
            {
                "pelvis": BonePose(
                    rotation_degrees=(6.0, 0.0, 0.0),
                    location_m=(0.0, 0.025, -0.025),
                ),
                "spine": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
            },
        )
        bus_alight_step = self.merge_pose(
            bus_board_through,
            {
                "pelvis": BonePose(
                    rotation_degrees=(4.0, -1.0, -1.0),
                    location_m=(0.0, -0.02, 0.02),
                ),
                "thigh.L": BonePose(rotation_degrees=(-34.0, 0.0, 3.0)),
                "shin.L": BonePose(rotation_degrees=(49.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-13.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(10.0, 0.0, -2.0)),
                "shin.R": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
            },
        )
        bus_alight_land = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(4.0, 0.0, 1.0),
                    location_m=(0.0, -0.01, -0.025),
                ),
                "spine": BonePose(rotation_degrees=(12.0, 0.0, -1.0)),
                "chest": BonePose(rotation_degrees=(-5.0, 0.0, 1.0)),
                "neck": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                "thigh.L": BonePose(rotation_degrees=(-16.0, 0.0, 2.0)),
                "thigh.R": BonePose(rotation_degrees=(-13.0, 0.0, -2.0)),
                "shin.L": BonePose(rotation_degrees=(23.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(21.0, 0.0, 0.0)),
            },
        )
        self._create_action(
            "BusBoardEnter", "bus_ride", 3.0, False, 36, 12,
            (
                (0.0, relaxed),
                (0.18, bus_board_step),
                (0.36, bus_board_through),
                (0.56, bus_seat_prepare),
                (0.76, bus_seat_lower),
                (1.0, bus_seated),
            ),
        )
        self._create_action(
            "BusRideLoop", "bus_ride", 2.0, True, 16, 8,
            (
                (0.0, bus_seated),
                (0.20, bus_ride_left),
                (0.50, bus_ride_breath),
                (0.80, bus_ride_right),
                (1.0, bus_seated),
            ),
        )
        self._create_action(
            "BusAlightExit", "bus_ride", 3.0, False, 36, 12,
            (
                (0.0, bus_seated),
                (0.20, bus_alight_lean),
                (0.38, bus_alight_rise),
                (0.60, bus_alight_step),
                (0.80, bus_alight_land),
                (1.0, relaxed),
            ),
        )

        # The park chess planks. A bus seat is backed onto from in
        # front; these have a stone table standing exactly there, so the
        # body goes in past the end of the plank instead and works its
        # way along the timber. He travels to his own left throughout,
        # which is why the left leg leads and the right one trails: the
        # hero's seat at either table is the one whose free end is the
        # gap between the two tables, and that gap is always on his
        # right as he sits down.
        # Rig handedness, measured rather than assumed: a negative `z`
        # on either thigh swings that leg towards `+X`, and a negative
        # `y` on the pelvis turns the whole body the same way. `+X` is
        # anatomical left, which is where he is going.
        chess_side_step = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(2.0, -6.0, 0.0),
                    location_m=(0.0, -0.015, 0.0),
                ),
                "spine": BonePose(rotation_degrees=(7.0, -2.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-2.0, -4.0, 0.0)),
                "head": BonePose(rotation_degrees=(6.0, -8.0, 0.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.19, -0.10, -0.26)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.09, -0.09, -0.32)
                ),
                "forearm.L": BonePose(rotation_degrees=(-20.0, 0.0, 12.0)),
                "forearm.R": BonePose(rotation_degrees=(-12.0, 0.0, -7.0)),
                "hand.L": BonePose(rotation_degrees=(5.0, -7.0, 4.0)),
                "hand.R": BonePose(rotation_degrees=(3.0, 6.0, -3.0)),
                "thigh.L": BonePose(rotation_degrees=(-14.0, 0.0, -30.0)),
                "shin.L": BonePose(rotation_degrees=(28.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(2.0, 0.0, 2.0)),
                "shin.R": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
            },
        )
        chess_side_over = self.merge_pose(
            relaxed,
            {
                "pelvis": BonePose(
                    rotation_degrees=(7.0, -12.0, 0.0),
                    location_m=(0.0, -0.040, 0.0),
                ),
                "spine": BonePose(rotation_degrees=(14.0, -3.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-3.0, -3.0, 0.0)),
                "head": BonePose(rotation_degrees=(7.0, -6.0, 0.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.20, -0.14, -0.21)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.13, -0.14, -0.26)
                ),
                "forearm.L": BonePose(rotation_degrees=(-30.0, 0.0, 14.0)),
                "forearm.R": BonePose(rotation_degrees=(-26.0, 0.0, -12.0)),
                "hand.L": BonePose(rotation_degrees=(8.0, -7.0, 5.0)),
                "hand.R": BonePose(rotation_degrees=(7.0, 7.0, -5.0)),
                "thigh.L": BonePose(rotation_degrees=(-30.0, 0.0, -22.0)),
                "shin.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-18.0, 0.0, -26.0)),
                "shin.R": BonePose(rotation_degrees=(34.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-9.0, 0.0, 0.0)),
            },
        )
        chess_side_perch = self.merge_pose(
            bus_seat_lower,
            {
                "pelvis": BonePose(
                    rotation_degrees=(3.0, -12.0, 0.0),
                    location_m=(0.0, 0.075, -0.19),
                ),
                "spine": BonePose(rotation_degrees=(14.0, -3.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-3.0, -2.0, 0.0)),
                "head": BonePose(rotation_degrees=(6.0, -5.0, 0.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.14, -0.17, -0.23)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.13, 0.03, -0.30)
                ),
                "forearm.L": BonePose(rotation_degrees=(-34.0, 0.0, 12.0)),
                "forearm.R": BonePose(rotation_degrees=(-8.0, 0.0, -6.0)),
                "hand.L": BonePose(rotation_degrees=(8.0, -6.0, 4.0)),
                "hand.R": BonePose(rotation_degrees=(-14.0, 6.0, -4.0)),
                "thigh.L": BonePose(rotation_degrees=(-60.0, 0.0, -16.0)),
                "shin.L": BonePose(rotation_degrees=(58.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
                "thigh.R": BonePose(rotation_degrees=(-50.0, 0.0, -20.0)),
                "shin.R": BonePose(rotation_degrees=(64.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
            },
        )
        chess_side_settle = self.merge_pose(
            bus_seated,
            {
                "pelvis": BonePose(
                    rotation_degrees=(-1.0, -6.0, 0.0),
                    location_m=(0.0, 0.095, -0.285),
                ),
                "spine": BonePose(rotation_degrees=(10.0, -1.5, 0.0)),
                "chest": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-2.0, -1.0, 0.0)),
                "head": BonePose(rotation_degrees=(7.0, -3.0, 0.0)),
                "upper_arm.L": BonePose(
                    target_direction=(0.080, -0.170, -0.220)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.090, -0.100, -0.260)
                ),
                "forearm.L": BonePose(rotation_degrees=(-42.0, 0.0, 10.0)),
                "forearm.R": BonePose(rotation_degrees=(-30.0, 0.0, -10.0)),
                "hand.L": BonePose(rotation_degrees=(8.0, -5.0, 3.0)),
                "hand.R": BonePose(rotation_degrees=(6.0, 6.0, -3.0)),
                "thigh.L": BonePose(rotation_degrees=(-69.0, 0.0, -6.0)),
                "thigh.R": BonePose(rotation_degrees=(-70.0, 0.0, -10.0)),
                "shin.L": BonePose(rotation_degrees=(74.0, 0.0, 0.0)),
                "shin.R": BonePose(rotation_degrees=(74.0, 0.0, 0.0)),
                "foot.L": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
                "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
            },
        )

        # Settled in the middle of the plank, over his own half of the
        # board. He sits forward rather than back: there is no rest
        # behind him and the game is a foot away.
        chess_seated = self.merge_pose(
            bus_seated,
            {
                "pelvis": BonePose(
                    rotation_degrees=(2.0, 0.0, 0.0),
                    location_m=(0.0, 0.095, -0.285),
                ),
                "spine": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(9.0, 0.0, -0.4)),
                "upper_arm.L": BonePose(
                    target_direction=(0.085, -0.185, -0.205)
                ),
                "upper_arm.R": BonePose(
                    target_direction=(-0.085, -0.185, -0.205)
                ),
                "forearm.L": BonePose(rotation_degrees=(-46.0, 0.0, 9.0)),
                "forearm.R": BonePose(rotation_degrees=(-46.0, 0.0, -9.0)),
                "hand.L": BonePose(rotation_degrees=(9.0, -5.0, 3.0)),
                "hand.R": BonePose(rotation_degrees=(9.0, 5.0, -3.0)),
            },
        )
        chess_study_lean = self.merge_pose(
            chess_seated,
            {
                "pelvis": BonePose(
                    rotation_degrees=(6.0, 0.0, 0.0),
                    location_m=(0.0, 0.095, -0.285),
                ),
                "spine": BonePose(rotation_degrees=(21.0, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(12.0, 0.0, -0.4)),
                "forearm.L": BonePose(rotation_degrees=(-52.0, 0.0, 9.0)),
                "forearm.R": BonePose(rotation_degrees=(-52.0, 0.0, -9.0)),
            },
        )
        chess_seated_breath = self.merge_pose(
            chess_seated,
            {
                "spine": BonePose(rotation_degrees=(12.5, 0.0, 0.0)),
                "chest": BonePose(rotation_degrees=(-3.5, 0.0, 0.0)),
                "neck": BonePose(rotation_degrees=(-1.5, 0.0, 0.0)),
                "head": BonePose(rotation_degrees=(8.0, -2.5, -0.4)),
            },
        )

        # A hand goes out over the board and comes back without moving
        # anything. Nobody is playing him; the board has been set up and
        # untouched for years, and this is the whole joke of the seat.
        chess_hand_hover = self.merge_pose(
            chess_seated,
            {
                "spine": BonePose(rotation_degrees=(16.0, 0.0, -1.5)),
                "chest": BonePose(rotation_degrees=(-6.0, 0.0, 1.5)),
                "head": BonePose(rotation_degrees=(10.0, -1.5, -0.4)),
                "upper_arm.R": BonePose(
                    target_direction=(-0.095, -0.235, -0.135)
                ),
                "forearm.R": BonePose(rotation_degrees=(-63.0, 0.0, -12.0)),
                "hand.R": BonePose(rotation_degrees=(14.0, 5.0, -4.0)),
            },
        )
        self._create_action(
            "ChessSeatEnter", "chess_seat", 3.0, False, 36, 12,
            (
                (0.0, relaxed),
                (0.14, chess_side_step),
                (0.34, chess_side_over),
                (0.52, chess_side_perch),
                (0.72, chess_side_settle),
                (1.0, chess_seated),
            ),
        )
        self._create_action(
            "ChessSeatPlayLoop", "chess_seat", 4.0, True, 32, 8,
            (
                (0.0, chess_seated),
                (0.22, chess_study_lean),
                (0.46, chess_hand_hover),
                (0.68, chess_study_lean),
                (0.86, chess_seated_breath),
                (1.0, chess_seated),
            ),
        )
        self._create_action(
            "ChessSeatExit", "chess_seat", 3.0, False, 36, 12,
            (
                (0.0, chess_seated),
                (0.22, chess_side_settle),
                (0.42, chess_side_perch),
                (0.62, chess_side_over),
                (0.82, chess_side_step),
                (1.0, relaxed),
            ),
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

        portrait_camera_data = bpy.data.cameras.new(
            "CAM_PlayerPortrait_Data"
        )
        portrait_camera = bpy.data.objects.new(
            "CAM_PlayerPortrait",
            portrait_camera_data,
        )
        collection.objects.link(portrait_camera)
        portrait_camera.location = self.v(0.62, -2.85, 1.52)
        portrait_camera_data.lens = 82.0
        portrait_camera_data.sensor_width = 36.0
        look_at(portrait_camera, self.v(0, -0.025, 1.28))
        portrait_camera["bp_export"] = False
        portrait_camera["bp_output"] = "inventory portrait 192x256 RGBA"
        self.result.presentation_objects.append(portrait_camera)

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
        scene["bp_runtime_integrated"] = True
        scene["bp_design_source"] = (
            "ArtSource/Player/PlayerDirectionalTurntable.png"
        )


def iter_action_fcurves(action: bpy.types.Action):
    """Yield curves from Blender legacy Actions and 4.4+ layered Actions."""

    legacy_curves = getattr(action, "fcurves", None)
    if legacy_curves is not None:
        yield from legacy_curves
        return
    for layer in action.layers:
        for strip in layer.strips:
            channelbags = getattr(strip, "channelbags", ())
            for channelbag in channelbags:
                yield from channelbag.fcurves


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


def validate_bed_support_contract(
    result: BuildResult,
    errors: list[str],
) -> None:
    """Measure and lock the plane the sleeper's weight actually rests on.

    Runtime pins the pelvis bone to one authored world point and nothing
    grounds the rig while a clip owns it, so how deep the body sits in the
    mattress is decided here and nowhere else. These offsets are mirrored in
    Assets/Scripts/Runtime/Player/PlayerCharacterDimensions.cs; the report
    below is the only honest source for those two numbers.
    """

    names = ("BedEnter", "BedSleepLoop", "BedExit")
    if any(result.actions.get(name) is None for name in names):
        return

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    previous_basis = {
        bone.name: bone.matrix_basis.copy()
        for bone in rig.pose.bones
    }

    head_bones = {"head", *REQUIRED_FACE_BONES}
    tolerance = 0.004

    def sample(action_name: str, normalized_time: float) -> None:
        record = result.actions[action_name]
        animation_data.action = record.action
        scene.frame_set(round(record.action.frame_end * normalized_time))
        bpy.context.view_layer.update()

    def lowest_named(selector) -> tuple[float, str]:
        depsgraph = bpy.context.evaluated_depsgraph_get()
        minimum = math.inf
        culprit = "?"
        for part in result.parts:
            if not selector(part):
                continue
            evaluated = part.obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            try:
                if mesh is None:
                    continue
                for vertex in mesh.vertices:
                    height = (evaluated.matrix_world @ vertex.co).z
                    if height < minimum:
                        minimum = height
                        culprit = part.obj.name
            finally:
                evaluated.to_mesh_clear()
        return minimum, culprit

    def lowest(selector) -> float:
        return lowest_named(selector)[0]

    def is_body(part: PartRecord) -> bool:
        # The satchel strap is soft goods pinned between his back and the
        # bedding; the plane is where his body rests, not where its buckle
        # presses in.
        return (
            part.bone not in head_bones
            and part.bone != "neck"
            and not part.obj.name.startswith("ACC_")
        )

    def is_head(part: PartRecord) -> bool:
        # Loose hair spikes are allowed to press into a soft pillow; the
        # skull and its cap are what must never disappear into one.
        return (
            part.bone in head_bones
            and not part.obj.name.startswith("GEO_HairTuft")
        )

    def pelvis_height() -> float:
        return (rig.matrix_world @ rig.pose.bones["pelvis"].head).z

    try:
        for name in names:
            for fcurve in iter_action_fcurves(result.actions[name].action):
                for keyframe in fcurve.keyframe_points:
                    if keyframe.interpolation != "BEZIER":
                        errors.append(
                            f"{name} must use eased Bezier keys so the body "
                            "does not change speed in a single frame"
                        )
                        break
                else:
                    continue
                break

        sample("BedEnter", 1.0)
        pelvis = pelvis_height()
        body_low, body_part = lowest_named(is_body)
        head_low, head_part = lowest_named(is_head)
        body_offset = pelvis - body_low
        head_offset = pelvis - head_low
        print(
            "  Bed support offsets: "
            f"body {body_offset:.4f} m ({body_part}), "
            f"head {head_offset:.4f} m ({head_part}), "
            f"head rides {body_offset - head_offset:.4f} m proud"
        )
        # Limbs may float over the mattress but must never break its plane;
        # naming the offender is what makes a failure here actionable.
        for probe in ("GEO_Hand.L", "GEO_Hand.R", "GEO_Foot.L", "GEO_Foot.R"):
            depth = pelvis - lowest(lambda p, n=probe: p.obj.name == n)
            if depth > BED_BODY_SUPPORT_OFFSET_M + tolerance:
                errors.append(
                    f"{probe} hangs {depth - BED_BODY_SUPPORT_OFFSET_M:.4f} m "
                    "below the mattress in the sleeping pose"
                )
        if abs(body_offset - BED_BODY_SUPPORT_OFFSET_M) > tolerance:
            errors.append(
                "The supine body rests "
                f"{body_offset:.4f} m under the pelvis bone, but "
                f"BED_BODY_SUPPORT_OFFSET_M declares "
                f"{BED_BODY_SUPPORT_OFFSET_M:.4f} m"
            )
        if abs(head_offset - BED_HEAD_SUPPORT_OFFSET_M) > tolerance:
            errors.append(
                "The supine head rests "
                f"{head_offset:.4f} m under the pelvis bone, but "
                f"BED_HEAD_SUPPORT_OFFSET_M declares "
                f"{BED_HEAD_SUPPORT_OFFSET_M:.4f} m"
            )
        if head_offset >= body_offset - 0.02:
            errors.append(
                "The sleeper's head must ride clearly above the body plane "
                "so a pillow can be built under it"
            )

        # Runtime slides the pelvis between the bedside seat and the sleeping
        # target on a smoothstep, so the mattress sits a moving distance under
        # the pelvis bone during both transitions. These mirror
        # PlayerAnimatedInteractionPelvisTransition; measuring against a fixed
        # offset would accuse an honest pose of clipping.
        def eased(span: float) -> float:
            span = min(1.0, max(0.0, span))
            return span * span * (3.0 - (2.0 * span))

        def entering_support_offset(normalized_time: float) -> float:
            if normalized_time <= BED_ENTER_SEAT_DEPARTURE:
                return BED_SEATED_PELVIS_LIFT_M
            return BED_SEATED_PELVIS_LIFT_M + eased(
                (normalized_time - BED_ENTER_SEAT_DEPARTURE) /
                (1.0 - BED_ENTER_SEAT_DEPARTURE)
            ) * (BED_BODY_SUPPORT_OFFSET_M - BED_SEATED_PELVIS_LIFT_M)

        def exiting_support_offset(normalized_time: float) -> float:
            if normalized_time >= BED_EXIT_SEAT_ARRIVAL:
                return BED_SEATED_PELVIS_LIFT_M
            return BED_BODY_SUPPORT_OFFSET_M + eased(
                normalized_time / BED_EXIT_SEAT_ARRIVAL
            ) * (BED_SEATED_PELVIS_LIFT_M - BED_BODY_SUPPORT_OFFSET_M)

        # Since the wake became a sit-up rather than a roll, his weight stays
        # on the bed from the first frame to the moment the first boot leaves
        # it, and every one of those samples is checkable. Only the lie-down
        # still rolls, so only its tail is asserted.
        # The arrival and the long-held sleep are exact. Waking gets the same
        # soft-goods allowance the bedside seat gets, because runtime starts
        # easing the pelvis down toward the edge the instant the wake begins
        # while he is still, correctly, lying flat and stirring.
        windows = (
            ("BedEnter", (0.95, 0.98, 1.0), entering_support_offset, tolerance),
            (
                "BedSleepLoop",
                (0.0, 0.16, 0.4, 0.58, 0.78, 0.9, 1.0),
                lambda _: BED_BODY_SUPPORT_OFFSET_M,
                tolerance,
            ),
            # The wake has no roll any more, so the whole climb from flat to
            # the half-crouch is checkable: he never leaves the mattress until
            # the first leg goes over the edge.
            (
                "BedExit",
                (0.0, 0.05, 0.11, 0.20, 0.26, 0.30, 0.36, 0.43, 0.46, 0.50),
                exiting_support_offset,
                0.02,
            ),
        )

        for name, times, offset_at, give in windows:
            for normalized_time in times:
                sample(name, normalized_time)
                plane = pelvis_height() - offset_at(normalized_time)
                low, culprit = lowest_named(is_body)
                if low < plane - give:
                    errors.append(
                        f"{name} at {normalized_time:.2f} pushes {culprit} "
                        f"{plane - low:.4f} m through the mattress plane"
                    )
                head_low, head_part = lowest_named(is_head)
                if head_low < plane - give:
                    errors.append(
                        f"{name} at {normalized_time:.2f} pushes {head_part} "
                        f"{plane - head_low:.4f} m into the bed"
                    )
                elif head_low < plane + 0.02:
                    errors.append(
                        f"{name} at {normalized_time:.2f} drops the head "
                        "below its pillow"
                    )

        # Sitting rests on a different part of him than lying does, so the
        # edge of the bed needs its own measured offset.
        sample("BedEnter", 0.36)
        seat_pelvis = pelvis_height()
        seat_low, seat_part = lowest_named(
            lambda part: part.bone == "pelvis"
        )
        seat_offset = seat_pelvis - seat_low
        seat_feet = seat_pelvis - lowest(
            lambda part: part.bone in ("foot.L", "foot.R")
        )
        print(
            f"  Bed seated support offset: {seat_offset:.4f} m ({seat_part}), "
            f"boots {seat_feet:.4f} m under the pelvis"
        )
        if abs(seat_offset - BED_SEATED_SUPPORT_OFFSET_M) > tolerance:
            errors.append(
                f"The seated hero rests {seat_offset:.4f} m under the pelvis "
                f"bone, but BED_SEATED_SUPPORT_OFFSET_M declares "
                f"{BED_SEATED_SUPPORT_OFFSET_M:.4f} m"
            )
        # Sitting on the edge only works if one pelvis height both plants his
        # boots on the floor and keeps his weight on the mattress. The boots
        # decide it, because a floating foot is the more visible lie; what is
        # left over is how much clearance stays under him.
        seat_lift = seat_feet - BED_MATTRESS_ABOVE_FLOOR_M
        print(
            f"  Bed seated pelvis lift: {seat_lift:.4f} m above the mattress, "
            f"leaving {seat_lift - seat_offset:.4f} m under him"
        )
        if abs(seat_lift - BED_SEATED_PELVIS_LIFT_M) > tolerance:
            errors.append(
                f"The seated landmark needs the pelvis {seat_lift:.4f} m over "
                "the mattress to plant both boots, but "
                f"BED_SEATED_PELVIS_LIFT_M declares "
                f"{BED_SEATED_PELVIS_LIFT_M:.4f} m"
            )
        if not 0.0 <= seat_lift - seat_offset <= 0.03:
            errors.append(
                "Planting the boots must not lift the seated hero off the "
                f"mattress or push him into it; clearance is "
                f"{seat_lift - seat_offset:.4f} m"
            )

        # Bedding gives under a man's weight, so the edge gets a soft-goods
        # allowance the long-held sleeping pose does not.
        seated_give = 0.025
        for name, times in (
            ("BedEnter", (BED_ENTER_SEAT_ARRIVAL + 0.03, 0.36, 0.44)),
            ("BedExit", (0.46, 0.55, 0.63, 0.74, 0.80, 0.85, 0.88)),
        ):
            for normalized_time in times:
                sample(name, normalized_time)
                seated_plane = pelvis_height() - BED_SEATED_SUPPORT_OFFSET_M
                if lowest(lambda part: part.bone == "pelvis") < (
                    seated_plane - seated_give
                ):
                    errors.append(
                        f"{name} at {normalized_time:.2f} sinks the seated "
                        "hero into the edge of the bed"
                    )

        # The wake's whole shape is that the legs leave one at a time, the
        # near one first. Nothing else in the pipeline can notice if that
        # order is lost, so it is stated here as a measurement: the right boot
        # is already down while the left is still up on the bedding, and both
        # are down before he stands.
        def boot_drop(normalized_time: float, side: str) -> float:
            sample("BedExit", normalized_time)
            floor = pelvis_height() - BED_SEATED_PELVIS_LIFT_M - (
                BED_MATTRESS_ABOVE_FLOOR_M
            )
            return lowest(lambda part: part.bone == f"foot.{side}") - floor

        both_up = (boot_drop(0.46, "L"), boot_drop(0.46, "R"))
        right_only = (boot_drop(0.68, "L"), boot_drop(0.68, "R"))
        both_down = (boot_drop(0.80, "L"), boot_drop(0.80, "R"))
        print(
            "  Bed wake boots over the floor: "
            f"crouch L{both_up[0]:+.3f} R{both_up[1]:+.3f}, "
            f"right down L{right_only[0]:+.3f} R{right_only[1]:+.3f}, "
            f"seated L{both_down[0]:+.3f} R{both_down[1]:+.3f}"
        )
        if min(both_up) < BED_MATTRESS_ABOVE_FLOOR_M - 0.05:
            errors.append(
                "BedExit must draw both boots up onto the bedding for the "
                "half-crouch before either leg leaves the bed"
            )
        if right_only[1] > 0.06:
            errors.append(
                "BedExit must have the right boot down by 0.68; it hangs "
                f"{right_only[1]:.3f} m over the floor"
            )
        if right_only[0] < right_only[1] + 0.20:
            errors.append(
                "BedExit must drop the right leg first and hold the left up "
                f"meanwhile; left {right_only[0]:.3f} m, right "
                f"{right_only[1]:.3f} m over the floor"
            )
        if max(both_down) > 0.03 or min(both_down) < -0.03:
            errors.append(
                "BedExit must plant both boots on the floor before he stands; "
                f"left {both_down[0]:.3f} m, right {both_down[1]:.3f} m"
            )

        # The lowering is only believable if a hand actually meets the bed.
        # The pelvis is still near seat height through this window, so the
        # seated plane is the honest thing to measure a reaching hand
        # against. This is a "reads as contact" band, not solved IK.
        reaches = []
        for normalized_time in (0.55, 0.60, 0.67, 0.74, 0.80):
            sample("BedEnter", normalized_time)
            plane = pelvis_height() - entering_support_offset(normalized_time)
            reaches.append(
                (
                    normalized_time,
                    lowest(lambda part: part.bone in ("hand.R", "forearm.R"))
                    - plane,
                )
            )
        print(
            "  Bed brace hand: "
            + ", ".join(f"{t:.2f}={reach:+.4f}" for t, reach in reaches)
        )
        deepest = min(reach for _, reach in reaches)
        closest = min(abs(reach) for _, reach in reaches)
        if deepest < -0.08:
            errors.append(
                "The bracing hand drops toward the floor while the torso "
                f"lowers, {-deepest:.4f} m under the bed"
            )
        if closest > 0.05:
            errors.append(
                "The head-side hand must reach the mattress while the torso "
                f"lowers; its closest approach is {closest:.4f} m"
            )
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        for bone_name, matrix_basis in previous_basis.items():
            rig.pose.bones[bone_name].matrix_basis = matrix_basis
        bpy.context.view_layer.update()


def validate_bed_sleep_pose(
    result: BuildResult,
    errors: list[str],
) -> None:
    """Verify the authored sleeper points toward the source-space headboard."""

    record = result.actions.get("BedSleepLoop")
    if record is None:
        return

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    previous_basis = {
        bone.name: bone.matrix_basis.copy()
        for bone in rig.pose.bones
    }
    try:
        animation_data.action = record.action
        scene.frame_set(round(record.action.frame_end * 0.5))
        bpy.context.view_layer.update()

        head = rig.matrix_world @ rig.pose.bones["head"].tail
        left_foot = rig.matrix_world @ rig.pose.bones["foot.L"].tail
        right_foot = rig.matrix_world @ rig.pose.bones["foot.R"].tail
        feet = (left_foot + right_foot) * 0.5
        pelvis = rig.matrix_world @ rig.pose.bones["pelvis"].head
        head_to_feet = feet - head
        if (
            head_to_feet.length < 1e-5
            or head_to_feet.normalized().dot(Vector((1.0, 0.0, 0.0))) < 0.95
        ):
            errors.append(
                "BedSleepLoop must place the head toward source -X and "
                "the feet toward source +X"
            )
        elif not head.x < pelvis.x < feet.x:
            errors.append(
                "BedSleepLoop pelvis must remain between the head and feet"
            )

        mouth = rig.pose.bones["SOCKET_Mouth"]
        face_direction = (
            (rig.matrix_world @ mouth.tail) -
            (rig.matrix_world @ mouth.head)
        )
        if (
            face_direction.length < 1e-5
            or face_direction.normalized().dot(Vector((0.0, 0.0, 1.0))) < 0.90
        ):
            errors.append("BedSleepLoop must keep the sleeper face-up")

        for eye_name in ("face.eye.L", "face.eye.R"):
            if rig.pose.bones[eye_name].scale.y > 0.15:
                errors.append(
                    f"BedSleepLoop must keep {eye_name} visibly closed"
                )
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        for bone_name, matrix_basis in previous_basis.items():
            rig.pose.bones[bone_name].matrix_basis = matrix_basis
        bpy.context.view_layer.update()


def validate_smoking_pose(
    result: BuildResult,
    errors: list[str],
) -> None:
    """Verify smoking contact, facing, loop seam and fixed-root contracts."""

    record = result.actions.get("SmokeLoop")
    smoking_records = tuple(
        result.actions.get(name)
        for name in ("SmokeEnter", "SmokeLoop", "SmokeExit")
    )
    if record is None or any(value is None for value in smoking_records):
        return

    rig = result.rig
    scene = bpy.context.scene
    animation_data = rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    previous_basis = {
        bone.name: bone.matrix_basis.copy()
        for bone in rig.pose.bones
    }

    def sample(action_record: ActionRecord, normalized_time: float) -> None:
        animation_data.action = action_record.action
        frame = round(action_record.action.frame_end * normalized_time)
        scene.frame_set(frame)
        bpy.context.view_layer.update()

    def armature_axis(bone_name: str) -> Vector:
        bone = rig.pose.bones[bone_name]
        return bone.tail - bone.head

    def socket_head(bone_name: str) -> Vector:
        return rig.matrix_world @ rig.pose.bones[bone_name].head

    def matrix_error(left: Matrix, right: Matrix) -> float:
        return max(
            abs(left[row][column] - right[row][column])
            for row in range(4)
            for column in range(4)
        )

    try:
        mouth_rest_bone = rig.data.bones["SOCKET_Mouth"]
        mouth_rest_axis = (
            mouth_rest_bone.tail_local - mouth_rest_bone.head_local
        )
        cigarette_rest_bone = rig.data.bones["SOCKET_Cigarette.R"]
        cigarette_rest_axis = (
            cigarette_rest_bone.tail_local - cigarette_rest_bone.head_local
        )
        source_forward = Vector((0.0, -1.0, 0.0))
        if (
            mouth_rest_axis.length < 1e-5
            or mouth_rest_axis.normalized().dot(source_forward) < 0.95
        ):
            errors.append(
                "SOCKET_Mouth local +Y must preserve source-space facing -Y"
            )
        if (
            cigarette_rest_axis.length < 1e-5
            or cigarette_rest_axis.normalized().dot(source_forward) < 0.95
        ):
            errors.append(
                "SOCKET_Cigarette.R local +Y must point outward along source -Y"
            )

        sample(record, 0.0)
        rest_distance = (
            socket_head("SOCKET_Cigarette.R") - socket_head("SOCKET_Mouth")
        ).length
        if rest_distance < 0.40:
            errors.append(
                "SmokeLoop rest must keep the cigarette at least 0.40 m "
                f"from the mouth (got {rest_distance:.4f} m)"
            )

        mouth_rest_pose_axis = armature_axis("SOCKET_Mouth")
        if (
            mouth_rest_pose_axis.length < 1e-5
            or mouth_rest_pose_axis.normalized().dot(source_forward) < 0.95
        ):
            errors.append("SmokeLoop rest must keep the face toward source -Y")

        seam_start = {
            bone.name: bone.matrix.copy()
            for bone in rig.pose.bones
        }

        identity = Matrix.Identity(4)
        for smoking_record in smoking_records:
            keyed_frames = sorted({
                round(keyframe.co.x)
                for fcurve in iter_action_fcurves(smoking_record.action)
                for keyframe in fcurve.keyframe_points
            })
            animation_data.action = smoking_record.action
            for frame in keyed_frames:
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                root_error = matrix_error(
                    rig.pose.bones["root"].matrix_basis,
                    identity,
                )
                if root_error > 1e-5:
                    errors.append(
                        f"{smoking_record.action.name} must keep the root "
                        "bone fixed at every authored phase"
                    )
                    break

        contact_samples = (
            (result.actions["SmokeEnter"], (0.68, 0.76, 0.84)),
            (record, (0.50, 0.625)),
        )
        for contact_record, normalized_times in contact_samples:
            for normalized_time in normalized_times:
                sample(contact_record, normalized_time)
                cigarette_head = socket_head("SOCKET_Cigarette.R")
                mouth_head = socket_head("SOCKET_Mouth")
                mouth_distance = (cigarette_head - mouth_head).length
                if mouth_distance > 0.025:
                    errors.append(
                        f"{contact_record.action.name} inhale must place the "
                        "cigarette socket within 0.025 m of the mouth "
                        f"(got {mouth_distance:.4f} m at "
                        f"t={normalized_time:.3f})"
                    )

                cigarette_axis = armature_axis("SOCKET_Cigarette.R")
                mouth_axis = armature_axis("SOCKET_Mouth")
                if cigarette_axis.length < 1e-5 or mouth_axis.length < 1e-5:
                    errors.append(
                        f"{contact_record.action.name} inhale socket axes "
                        "must be non-zero"
                    )
                    continue
                alignment = cigarette_axis.normalized().dot(
                    mouth_axis.normalized()
                )
                if alignment < 0.85:
                    errors.append(
                        f"{contact_record.action.name} inhale cigarette "
                        "local +Y must point outward with the mouth socket "
                        f"(dot {alignment:.4f})"
                    )

        sample(record, 1.0)
        for bone_name, start_matrix in seam_start.items():
            seam_error = matrix_error(
                rig.pose.bones[bone_name].matrix,
                start_matrix,
            )
            if seam_error > 1e-5:
                errors.append(
                    "SmokeLoop must have an exact full-rig seam "
                    f"({bone_name} differs by {seam_error:.7f})"
                )
                break
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        for bone_name, matrix_basis in previous_basis.items():
            rig.pose.bones[bone_name].matrix_basis = matrix_basis
        bpy.context.view_layer.update()


def validate_interaction_pose(
    result: BuildResult,
    errors: list[str],
    enter_name: str,
    loop_name: str,
    exit_name: str,
) -> None:
    """Verify one fixed-root interaction family and its full-rig seams."""

    action_names = (
        "Relaxed",
        enter_name,
        loop_name,
        exit_name,
    )
    if any(result.actions.get(name) is None for name in action_names):
        return

    rig = result.rig
    scene = bpy.context.scene
    animation_data = rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    previous_basis = {
        bone.name: bone.matrix_basis.copy()
        for bone in rig.pose.bones
    }
    full_rig_bones = tuple(bone.name for bone in rig.pose.bones)
    identity = Matrix.Identity(4)

    def matrix_error(left: Matrix, right: Matrix) -> float:
        return max(
            abs(left[row][column] - right[row][column])
            for row in range(4)
            for column in range(4)
        )

    def sample(
        action_record: ActionRecord,
        normalized_time: float,
    ) -> dict[str, Matrix]:
        animation_data.action = action_record.action
        scene.frame_set(round(action_record.action.frame_end * normalized_time))
        bpy.context.view_layer.update()
        return {
            bone_name: rig.pose.bones[bone_name].matrix.copy()
            for bone_name in full_rig_bones
        }

    try:
        records = {
            name: result.actions[name]
            for name in action_names
        }
        seam_samples = (
            (
                f"Relaxed->{enter_name}",
                sample(records["Relaxed"], 0.0),
                sample(records[enter_name], 0.0),
            ),
            (
                f"{enter_name}->{loop_name}",
                sample(records[enter_name], 1.0),
                sample(records[loop_name], 0.0),
            ),
            (
                f"{loop_name} seam",
                sample(records[loop_name], 0.0),
                sample(records[loop_name], 1.0),
            ),
            (
                f"{loop_name}->{exit_name}",
                sample(records[loop_name], 1.0),
                sample(records[exit_name], 0.0),
            ),
            (
                f"{exit_name}->Relaxed",
                sample(records[exit_name], 1.0),
                sample(records["Relaxed"], 1.0),
            ),
        )
        for seam_name, first_pose, second_pose in seam_samples:
            for bone_name in full_rig_bones:
                seam_error = matrix_error(
                    first_pose[bone_name],
                    second_pose[bone_name],
                )
                if seam_error > 1e-5:
                    errors.append(
                        f"{seam_name} must match across the full rig "
                        f"({bone_name} differs by {seam_error:.7f})"
                    )
                    break

        for action_name in action_names[1:]:
            action_record = records[action_name]
            keyed_frames = sorted({
                round(keyframe.co.x)
                for fcurve in iter_action_fcurves(action_record.action)
                for keyframe in fcurve.keyframe_points
            })
            animation_data.action = action_record.action
            for frame in keyed_frames:
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                root_error = matrix_error(
                    rig.pose.bones["root"].matrix_basis,
                    identity,
                )
                if root_error > 1e-5:
                    errors.append(
                        f"{action_name} must keep the root bone fixed at "
                        "every authored phase"
                    )
                    break
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        for bone_name, matrix_basis in previous_basis.items():
            rig.pose.bones[bone_name].matrix_basis = matrix_basis
        bpy.context.view_layer.update()


def validate_door_use_pose(
    result: BuildResult,
    errors: list[str],
) -> None:
    """Verify the planted standing press and its physical-right-hand reach."""

    enter_name = "DoorUseEnter"
    loop_name = "DoorUseLoop"
    exit_name = "DoorUseExit"
    validate_interaction_pose(
        result,
        errors,
        enter_name,
        loop_name,
        exit_name,
    )

    action_names = ("Relaxed", enter_name, loop_name, exit_name)
    if any(result.actions.get(name) is None for name in action_names):
        return

    rig = result.rig
    scene = bpy.context.scene
    animation_data = rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    previous_basis = {
        bone.name: bone.matrix_basis.copy()
        for bone in rig.pose.bones
    }
    scale = (
        float(result.root.get("bp_canonical_height_m", CANONICAL_HEIGHT))
        / CANONICAL_HEIGHT
    )

    def matrix_error(left: Matrix, right: Matrix) -> float:
        return max(
            abs(left[row][column] - right[row][column])
            for row in range(4)
            for column in range(4)
        )

    def sample_bone(
        action_record: ActionRecord,
        normalized_time: float,
        bone_name: str,
    ) -> Matrix:
        animation_data.action = action_record.action
        scene.frame_set(round(action_record.action.frame_end * normalized_time))
        bpy.context.view_layer.update()
        return rig.pose.bones[bone_name].matrix.copy()

    try:
        relaxed = result.actions["Relaxed"]
        contact = result.actions[loop_name]
        relaxed_grip = sample_bone(
            relaxed,
            0.0,
            "SOCKET_Grip.R",
        ).translation
        contact_grip = sample_bone(
            contact,
            0.0,
            "SOCKET_Grip.R",
        ).translation
        forward_reach = relaxed_grip.y - contact_grip.y
        minimum_reach = 0.16 * scale
        maximum_reach = 0.52 * scale
        if not minimum_reach <= forward_reach <= maximum_reach:
            errors.append(
                "DoorUse physical-right grip must make one short forward "
                f"press ({forward_reach:.4f} m, expected "
                f"{minimum_reach:.4f}..{maximum_reach:.4f} m)"
            )
        if contact_grip.x >= -0.01 * scale:
            errors.append(
                "DoorUse contact must stay on physical right/source -X "
                f"(grip X {contact_grip.x:.4f} m)"
            )

        relaxed_chest = sample_bone(
            relaxed,
            0.0,
            "chest",
        ).translation
        contact_chest = sample_bone(
            contact,
            0.0,
            "chest",
        ).translation
        torso_lean = relaxed_chest.y - contact_chest.y
        if not 0.015 * scale <= torso_lean <= 0.12 * scale:
            errors.append(
                "DoorUse chest must incline subtly toward source -Y "
                f"({torso_lean:.4f} m forward)"
            )

        relaxed_feet = {
            bone_name: sample_bone(relaxed, 0.0, bone_name)
            for bone_name in ("foot.L", "foot.R")
        }
        foot_tolerance = 1e-5 * max(1.0, scale)
        for action_name in action_names[1:]:
            action_record = result.actions[action_name]
            animation_data.action = action_record.action
            for frame in range(
                math.ceil(action_record.action.frame_start),
                math.floor(action_record.action.frame_end) + 1,
            ):
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                for bone_name, relaxed_matrix in relaxed_feet.items():
                    drift = matrix_error(
                        rig.pose.bones[bone_name].matrix,
                        relaxed_matrix,
                    )
                    if drift > foot_tolerance:
                        errors.append(
                            f"{action_name} frame {frame} must keep "
                            f"{bone_name} planted (matrix drift "
                            f"{drift:.7f})"
                        )
                        break
                else:
                    continue
                break
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        for bone_name, matrix_basis in previous_basis.items():
            rig.pose.bones[bone_name].matrix_basis = matrix_basis
        bpy.context.view_layer.update()


def validate_fall_recovery_pose(
    result: BuildResult,
    errors: list[str],
) -> None:
    """Verify full-rig fall seams and the supported recovery silhouette."""

    action_names = (
        "Relaxed",
        "FallLeft",
        "DownLeft",
        "RiseLeft",
        "FallRight",
        "DownRight",
        "RiseRight",
    )
    if any(result.actions.get(name) is None for name in action_names):
        return

    rig = result.rig
    scene = bpy.context.scene
    animation_data = rig.animation_data_create()
    previous_action = animation_data.action
    previous_frame = scene.frame_current
    previous_basis = {
        bone.name: bone.matrix_basis.copy()
        for bone in rig.pose.bones
    }
    identity = Matrix.Identity(4)
    full_rig_bones = tuple(bone.name for bone in rig.pose.bones)
    rise_times = (
        0.0,
        0.10,
        0.24,
        0.38,
        0.48,
        0.56,
        0.64,
        0.72,
        0.80,
        0.92,
        1.0,
    )

    def sample(action_record: ActionRecord, normalized_time: float) -> None:
        animation_data.action = action_record.action
        frame = round(action_record.action.frame_end * normalized_time)
        scene.frame_set(frame)
        bpy.context.view_layer.update()

    def pose_snapshot(
        action_record: ActionRecord,
        normalized_time: float,
    ) -> dict[str, Matrix]:
        sample(action_record, normalized_time)
        return {
            bone_name: rig.pose.bones[bone_name].matrix.copy()
            for bone_name in full_rig_bones
        }

    def matrix_error(left: Matrix, right: Matrix) -> float:
        return max(
            abs(left[row][column] - right[row][column])
            for row in range(4)
            for column in range(4)
        )

    def world_head(bone_name: str) -> Vector:
        return rig.matrix_world @ rig.pose.bones[bone_name].head

    def world_tail(bone_name: str) -> Vector:
        return rig.matrix_world @ rig.pose.bones[bone_name].tail

    def visible_minimum(
        bone_names: set[str] | None = None,
    ) -> tuple[float, PartRecord | None]:
        depsgraph = bpy.context.evaluated_depsgraph_get()
        minimum = math.inf
        minimum_part = None
        for part in result.parts:
            if bone_names is not None and part.bone not in bone_names:
                continue
            evaluated = part.obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            try:
                if mesh is None:
                    continue
                for vertex in mesh.vertices:
                    height = (evaluated.matrix_world @ vertex.co).z
                    if height < minimum:
                        minimum = height
                        minimum_part = part
            finally:
                evaluated.to_mesh_clear()
        return minimum, minimum_part

    def visible_min_height(
        bone_names: set[str] | None = None,
    ) -> float:
        return visible_minimum(bone_names)[0]

    try:
        for side_name in ("Left", "Right"):
            rise_record = result.actions[f"Rise{side_name}"]
            if (
                abs(rise_record.duration_seconds - (50.0 / 30.0)) > 1e-6
                or rise_record.source_frame_count != 50
                or abs(rise_record.source_fps - 30.0) > 1e-6
            ):
                errors.append(
                    f"Rise{side_name} must be authored as 50 frames at 30 FPS"
                )

            expected_frames = [
                round(rise_record.action.frame_end * normalized_time)
                for normalized_time in rise_times
            ]
            keyed_frames = sorted({
                round(keyframe.co.x)
                for fcurve in iter_action_fcurves(rise_record.action)
                for keyframe in fcurve.keyframe_points
            })
            if keyed_frames != expected_frames:
                errors.append(
                    f"Rise{side_name} must own exactly the recovery keys at "
                    "0/.10/.24/.38/.48/.56/.64/.72/.80/.92/1"
                )

        seam_pairs = (
            ("FallLeft", 1.0, "DownLeft", 0.0),
            ("DownLeft", 1.0, "RiseLeft", 0.0),
            ("RiseLeft", 1.0, "Relaxed", 0.0),
            ("FallRight", 1.0, "DownRight", 0.0),
            ("DownRight", 1.0, "RiseRight", 0.0),
            ("RiseRight", 1.0, "Relaxed", 0.0),
        )
        for (
            first_name,
            first_time,
            second_name,
            second_time,
        ) in seam_pairs:
            first_pose = pose_snapshot(
                result.actions[first_name],
                first_time,
            )
            second_pose = pose_snapshot(
                result.actions[second_name],
                second_time,
            )
            for bone_name in full_rig_bones:
                seam_error = matrix_error(
                    first_pose[bone_name],
                    second_pose[bone_name],
                )
                if seam_error > 1e-5:
                    errors.append(
                        f"Fall recovery seam {first_name}->{second_name} "
                        f"differs on {bone_name} by {seam_error:.7f}"
                    )
                    break

        recovery_names = (
            "FallLeft",
            "DownLeft",
            "RiseLeft",
            "FallRight",
            "DownRight",
            "RiseRight",
        )
        for action_name in recovery_names:
            record = result.actions[action_name]
            keyed_frames = sorted({
                round(keyframe.co.x)
                for fcurve in iter_action_fcurves(record.action)
                for keyframe in fcurve.keyframe_points
            })
            animation_data.action = record.action
            for frame in keyed_frames:
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                root_error = matrix_error(
                    rig.pose.bones["root"].matrix_basis,
                    identity,
                )
                if root_error > 1e-5:
                    errors.append(
                        f"{action_name} must keep the root fixed at every "
                        "authored recovery phase"
                    )
                    break

                finite = all(
                    math.isfinite(bone.matrix[row][column])
                    for bone in rig.pose.bones
                    for row in range(4)
                    for column in range(4)
                )
                if not finite:
                    errors.append(
                        f"{action_name} contains a non-finite authored pose"
                    )
                    break

        relaxed_record = result.actions["Relaxed"]
        sample(relaxed_record, 0.0)
        ground_height = (
            world_tail("foot.L").z + world_tail("foot.R").z
        ) * 0.5
        visible_ground_height = visible_min_height({"foot.L", "foot.R"})

        for side_name in ("Left", "Right"):
            rise_record = result.actions[f"Rise{side_name}"]
            down_record = result.actions[f"Down{side_name}"]
            for normalized_time in rise_times:
                sample(rise_record, normalized_time)
                stage_minimum = visible_min_height()
                if stage_minimum < visible_ground_height - 0.01:
                    errors.append(
                        f"Rise{side_name} t={normalized_time:.2f} visible "
                        "silhouette must not pass below the neutral floor "
                        f"(minimum {stage_minimum:.4f} m, floor "
                        f"{visible_ground_height:.4f} m)"
                    )
                elif stage_minimum > visible_ground_height + 0.04:
                    errors.append(
                        f"Rise{side_name} t={normalized_time:.2f} must keep "
                        "at least one visible support near the neutral floor "
                        f"(minimum {stage_minimum:.4f} m, floor "
                        f"{visible_ground_height:.4f} m)"
                    )

            dense_minimum = math.inf
            dense_minimum_frame = 0
            dense_minimum_part = None
            animation_data.action = rise_record.action
            for frame in range(
                math.ceil(rise_record.action.frame_start),
                math.floor(rise_record.action.frame_end) + 1,
            ):
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                frame_minimum, frame_minimum_part = visible_minimum()
                if frame_minimum < dense_minimum:
                    dense_minimum = frame_minimum
                    dense_minimum_frame = frame
                    dense_minimum_part = frame_minimum_part
            if dense_minimum < visible_ground_height - 0.01:
                culprit = (
                    f", part {dense_minimum_part.role}/"
                    f"{dense_minimum_part.bone}"
                    if dense_minimum_part is not None
                    else ""
                )
                errors.append(
                    f"Rise{side_name} frame {dense_minimum_frame} visible "
                    "silhouette must not pass below the neutral floor "
                    f"(minimum {dense_minimum:.4f} m, floor "
                    f"{visible_ground_height:.4f} m{culprit})"
                )

            for record, normalized_time in (
                (down_record, 0.0),
                (down_record, 0.5),
                (down_record, 1.0),
                (rise_record, 0.0),
            ):
                sample(record, normalized_time)
                for hand_side in ("L", "R"):
                    grip_height = world_head(
                        f"SOCKET_Grip.{hand_side}"
                    ).z
                    if (
                        grip_height < ground_height - 0.02
                        or grip_height > ground_height + 0.14
                    ):
                        errors.append(
                            f"{record.action.name} t={normalized_time:.2f} "
                            f"{hand_side} grip must remain a readable floor "
                            f"support (height {grip_height:.4f} m, floor "
                            f"{ground_height:.4f} m)"
                        )

                arm_minimum = visible_min_height({
                    "forearm.L",
                    "hand.L",
                    "forearm.R",
                    "hand.R",
                })
                if arm_minimum < visible_ground_height - 0.01:
                    errors.append(
                        f"{record.action.name} t={normalized_time:.2f} "
                        "forearms/hands must not pass below the neutral "
                        f"visible floor (minimum {arm_minimum:.4f} m, floor "
                        f"{visible_ground_height:.4f} m)"
                    )

                silhouette_minimum = visible_min_height()
                if silhouette_minimum < visible_ground_height - 0.01:
                    errors.append(
                        f"{record.action.name} t={normalized_time:.2f} "
                        "fallen silhouette must not pass below the neutral "
                        f"visible floor (minimum {silhouette_minimum:.4f} m, "
                        f"floor {visible_ground_height:.4f} m)"
                    )

            for normalized_time in (0.38, 0.48):
                sample(rise_record, normalized_time)
                contact_heights = {
                    "left hand": world_head("SOCKET_Grip.L").z,
                    "right hand": world_head("SOCKET_Grip.R").z,
                    "left knee": world_head("shin.L").z,
                    "right knee": world_head("shin.R").z,
                }
                floor_span = (
                    max(contact_heights.values()) -
                    min(contact_heights.values())
                )
                if floor_span > 0.12:
                    errors.append(
                        f"Rise{side_name} t={normalized_time:.2f} must keep "
                        "both grips and knees in one 0.12 m floor band "
                        f"(got {floor_span:.4f} m)"
                    )
                for contact_name, height in contact_heights.items():
                    floor_error = abs(height - ground_height)
                    if floor_error > 0.14:
                        errors.append(
                            f"Rise{side_name} t={normalized_time:.2f} "
                            f"{contact_name} must stay near the relaxed floor "
                            f"(error {floor_error:.4f} m)"
                        )

                pelvis_height = world_head("pelvis").z
                if pelvis_height < max(contact_heights.values()) + 0.18:
                    errors.append(
                        f"Rise{side_name} t={normalized_time:.2f} must place "
                        "the pelvis visibly above the four floor contacts"
                    )

            sample(rise_record, 0.80)
            foot_heights = (
                world_tail("foot.L").z,
                world_tail("foot.R").z,
            )
            if max(foot_heights) - min(foot_heights) > 0.08:
                errors.append(
                    f"Rise{side_name} low crouch must ground both feet in one "
                    "0.08 m band"
                )
            for side, height in zip(("left", "right"), foot_heights):
                floor_error = abs(height - ground_height)
                if floor_error > 0.12:
                    errors.append(
                        f"Rise{side_name} low crouch {side} foot must stay "
                        f"grounded (error {floor_error:.4f} m)"
                    )

            sample(rise_record, 0.92)
            near_upright_feet = (
                world_tail("foot.L").z,
                world_tail("foot.R").z,
            )
            for side, height in zip(
                ("left", "right"),
                near_upright_feet,
            ):
                floor_error = abs(height - ground_height)
                if floor_error > 0.05:
                    errors.append(
                        f"Rise{side_name} near-upright {side} foot must "
                        f"remain grounded (error {floor_error:.4f} m)"
                    )

            visible_foot_minimum = visible_min_height({"foot.L", "foot.R"})
            if visible_foot_minimum < visible_ground_height - 0.01:
                errors.append(
                    f"Rise{side_name} near-upright boots must not pass "
                    "below the neutral visible floor (minimum "
                    f"{visible_foot_minimum:.4f} m, floor "
                    f"{visible_ground_height:.4f} m)"
                )
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        for bone_name, matrix_basis in previous_basis.items():
            rig.pose.bones[bone_name].matrix_basis = matrix_basis
        bpy.context.view_layer.update()


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
    for bone_name in (*REQUIRED_BONES, *REQUIRED_FACE_BONES, *REQUIRED_SOCKET_BONES):
        bone = rig_bones.get(bone_name)
        if bone is None:
            errors.append(f"Missing required bone {bone_name}")
        elif bone.length <= 1e-5:
            errors.append(f"Bone {bone_name} has zero length")
    for bone_name in REQUIRED_SOCKET_BONES:
        bone = rig_bones.get(bone_name)
        if bone is not None and bone.use_deform:
            errors.append(f"Socket bone {bone_name} must not deform geometry")
    for bone_name in REQUIRED_FACE_BONES:
        bone = rig_bones.get(bone_name)
        if bone is not None and not bone.use_deform:
            errors.append(f"Face bone {bone_name} must deform its detail mesh")

    if set(result.actions) != set(REQUIRED_ACTIONS):
        missing_actions = sorted(set(REQUIRED_ACTIONS) - set(result.actions))
        extra_actions = sorted(set(result.actions) - set(REQUIRED_ACTIONS))
        if missing_actions:
            errors.append(f"Missing required Actions: {', '.join(missing_actions)}")
        if extra_actions:
            errors.append(f"Unexpected Actions: {', '.join(extra_actions)}")
    for name, record in result.actions.items():
        action = record.action
        curves = list(iter_action_fcurves(action))
        if not curves:
            errors.append(f"Action {name} has no bone curves")
            continue
        for fcurve in curves:
            if not fcurve.data_path.startswith('pose.bones["'):
                errors.append(
                    f"Action {name} contains non-bone curve {fcurve.data_path}"
                )
                break
        if abs(action.frame_start) > 1e-6:
            errors.append(f"Action {name} must start at frame zero")
        expected_end = max(1, round(record.duration_seconds * ANIMATION_FPS))
        if abs(action.frame_end - expected_end) > 1e-6:
            errors.append(
                f"Action {name} has frame end {action.frame_end}, "
                f"expected {expected_end}"
            )
        if bool(action.get("bp_root_motion", True)):
            errors.append(f"Action {name} must declare root motion disabled")
        if record.loop:
            for fcurve in curves:
                first = fcurve.evaluate(action.frame_start)
                last = fcurve.evaluate(action.frame_end)
                if abs(first - last) > 1e-4:
                    errors.append(
                        f"Loop Action {name} does not close on {fcurve.data_path}"
                    )
                    break

    validate_bed_sleep_pose(result, errors)
    validate_bed_support_contract(result, errors)
    validate_smoking_pose(result, errors)
    validate_door_use_pose(result, errors)
    validate_interaction_pose(
        result,
        errors,
        "BusBoardEnter",
        "BusRideLoop",
        "BusAlightExit",
    )
    validate_interaction_pose(
        result,
        errors,
        "ChessSeatEnter",
        "ChessSeatPlayLoop",
        "ChessSeatExit",
    )
    validate_fall_recovery_pose(result, errors)

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
        action_count=len(result.actions),
        socket_count=len(REQUIRED_SOCKET_BONES),
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


def render_inventory_portrait(path: Path, result: BuildResult) -> None:
    """Render the dedicated transparent UI portrait and validate its alpha."""

    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    camera = bpy.data.objects.get("CAM_PlayerPortrait")
    ground = bpy.data.objects.get("GEO_PreviewGround")
    relaxed = result.actions.get("Relaxed")
    if camera is None or camera.type != "CAMERA":
        raise RuntimeError("Missing deterministic inventory portrait camera")
    if relaxed is None:
        raise RuntimeError("Inventory portrait requires the Relaxed Action")

    previous_camera = scene.camera
    previous_filepath = scene.render.filepath
    previous_resolution = (
        scene.render.resolution_x,
        scene.render.resolution_y,
        scene.render.resolution_percentage,
    )
    previous_transparency = scene.render.film_transparent
    previous_frame = scene.frame_current
    animation_data = result.rig.animation_data_create()
    previous_action = animation_data.action
    previous_ground_hidden = ground.hide_render if ground is not None else False

    try:
        scene.camera = camera
        scene.render.resolution_x = 192
        scene.render.resolution_y = 256
        scene.render.resolution_percentage = 100
        scene.render.film_transparent = True
        scene.render.filepath = str(path)
        if ground is not None:
            ground.hide_render = True
        animation_data.action = relaxed.action
        scene.frame_set(0)
        bpy.context.view_layer.update()
        bpy.ops.render.render(write_still=True)

        portrait_image = bpy.data.images.load(str(path), check_existing=False)
        try:
            if tuple(portrait_image.size) != (192, 256):
                raise RuntimeError("Inventory portrait rendered at wrong size")
            pixels = list(portrait_image.pixels)
            alphas = pixels[3::4]
            visible = sum(alpha > 0.02 for alpha in alphas)
            coverage = visible / len(alphas)
            if not 0.12 <= coverage <= 0.78:
                raise RuntimeError(
                    "Inventory portrait alpha coverage is implausible: "
                    f"{coverage:.3f}"
                )
            width, height = portrait_image.size
            corner_indices = (
                0,
                width - 1,
                (height - 1) * width,
                height * width - 1,
            )
            if any(alphas[index] > 0.001 for index in corner_indices):
                raise RuntimeError(
                    "Inventory portrait corners must be transparent"
                )
        finally:
            bpy.data.images.remove(portrait_image)
    finally:
        animation_data.action = previous_action
        scene.frame_set(previous_frame)
        if previous_action is None:
            for pose_bone in result.rig.pose.bones:
                pose_bone.rotation_mode = "QUATERNION"
                pose_bone.location = (0.0, 0.0, 0.0)
                pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
                pose_bone.scale = (1.0, 1.0, 1.0)
        if ground is not None:
            ground.hide_render = previous_ground_hidden
        scene.camera = previous_camera
        scene.render.filepath = previous_filepath
        scene.render.resolution_x = previous_resolution[0]
        scene.render.resolution_y = previous_resolution[1]
        scene.render.resolution_percentage = previous_resolution[2]
        scene.render.film_transparent = previous_transparency
        bpy.context.view_layer.update()


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
            export_extras=True,
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
            use_armature_deform_only=False,
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
            use_custom_props=True,
        )
    except (AttributeError, RuntimeError, TypeError) as error:
        raise RuntimeError(
            "FBX export is unavailable in this Blender installation"
        ) from error


def export_animation_fbx(path: Path, result: BuildResult) -> None:
    """Export the complete Action library with skeleton, but without meshes."""

    path.parent.mkdir(parents=True, exist_ok=True)
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    result.root.select_set(True)
    result.rig.select_set(True)
    bpy.context.view_layer.objects.active = result.rig
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(path),
            use_selection=True,
            object_types={"EMPTY", "ARMATURE"},
            axis_forward="-Z",
            axis_up="Y",
            add_leaf_bones=False,
            bake_anim=True,
            bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=True,
            bake_anim_force_startend_keying=True,
            bake_anim_step=1.0,
            bake_anim_simplify_factor=0.0,
            use_armature_deform_only=False,
            use_custom_props=True,
        )
    except (AttributeError, RuntimeError, TypeError) as error:
        raise RuntimeError(
            "Animation FBX export is unavailable in this Blender installation"
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
        "runtime_integrated": True,
        "height_m": config.height,
        "pose": config.pose,
        "seed": config.seed,
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "object_count": report.object_count,
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "action_count": report.action_count,
        "socket_count": report.socket_count,
        "bounds_min": report.bounds_min,
        "bounds_max": report.bounds_max,
        "sprite_parts": list(SPRITE_PARTS),
        "bones": [bone.name for bone in result.rig.data.bones],
        "sockets": list(REQUIRED_SOCKET_BONES),
        # The Unity runtime places the sleeper from these measurements and
        # times his seat from these windows. Publishing them is what lets
        # Player3DAssetImportTests fail on drift instead of trusting a comment
        # in two files to stay in step.
        "bed_contract": {
            "supine_pelvis_offset_m": BED_BODY_SUPPORT_OFFSET_M,
            "supine_head_offset_m": BED_HEAD_SUPPORT_OFFSET_M,
            "seated_pelvis_lift_m": BED_SEATED_PELVIS_LIFT_M,
            "mattress_above_floor_m": BED_MATTRESS_ABOVE_FLOOR_M,
            "enter_seat_arrival": BED_ENTER_SEAT_ARRIVAL,
            "enter_seat_departure": BED_ENTER_SEAT_DEPARTURE,
            "exit_seat_arrival": BED_EXIT_SEAT_ARRIVAL,
            "exit_seat_departure": BED_EXIT_SEAT_DEPARTURE,
        },
        "actions": [
            {
                "name": name,
                "category": record.category,
                "duration_seconds": record.duration_seconds,
                "loop": record.loop,
                "source_frame_count": record.source_frame_count,
                "source_fps": record.source_fps,
                "frame_start": record.action.frame_start,
                "frame_end": record.action.frame_end,
                "root_motion": False,
            }
            for name, record in sorted(result.actions.items())
        ],
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
    print(f"  Actions: {report.action_count}")
    print(f"  Non-deforming sockets: {report.socket_count}")
    print(f"  Bounds min: {report.bounds_min}")
    print(f"  Bounds max: {report.bounds_max}")
    print(f"  Blend: {config.output}")
    if config.preview is not None:
        print(f"  Preview: {config.preview}")
    if config.portrait is not None:
        print(f"  Inventory portrait: {config.portrait}")
    if config.manifest is not None:
        print(f"  Manifest: {config.manifest}")
    if config.glb is not None:
        print(f"  GLB: {config.glb}")
    if config.fbx is not None:
        print(f"  FBX: {config.fbx}")
    if config.animation_fbx is not None:
        print(f"  Animation FBX: {config.animation_fbx}")


def main() -> None:
    config = parse_args()
    builder = CharacterBuilder(config)
    result = builder.build()
    report = validate_result(config, result)

    if config.preview is not None:
        render_preview(config.preview)
    if config.portrait is not None:
        render_inventory_portrait(config.portrait, result)
    if config.glb is not None:
        export_glb(config.glb, result)
    if config.fbx is not None:
        export_fbx(config.fbx, result)
    if config.animation_fbx is not None:
        export_animation_fbx(config.animation_fbx, result)
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
