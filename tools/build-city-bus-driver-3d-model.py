#!/usr/bin/env python3
"""Build the deterministic low-poly City bus driver.

Run this with Blender, not CPython:

    blender --background --factory-startup --python \
        tools/build-city-bus-driver-3d-model.py

The driver is an animation-free model on the NpcHumanV2-compatible 31-bone
A-pose skeleton. Runtime presentation can therefore seat and pose the torso,
limbs, grip sockets, head and eye bones procedurally without an Animator
controller or duplicated clips.

Blender source space is metres, Z-up, forward -Y and anatomical left +X.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "2.0.0"
DESIGN_ID = "long_eyes_driver_v1"
DISPLAY_NAME = "Long-Eyed Route Driver"
SEED = 241103
CANONICAL_HEIGHT = 1.75
MIN_TRIANGLES = 900
MAX_TRIANGLES = 1800
SHARED_MATERIAL_ASSET = "Assets/Player3D/Materials/Player3DLit.mat"
SIGNATURE_ANATOMY = ("long_horizontal_eyes",)


def load_character_build_base():
    source_path = Path(__file__).with_name(
        "build-city-pedestrian-3d-model.py"
    )
    spec = importlib.util.spec_from_file_location(
        "bp_city_character_build_base",
        source_path,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load shared rig helpers from {source_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


base = load_character_build_base()
base.NPC_PROFILE_KEY = "city_bus_driver"

PALETTE = {
    # `coat` is the helper material's neutral source preview color. Runtime
    # colors still come from each driver's explicit palette binding below.
    "coat": (0.090, 0.150, 0.165, 1.0),
    "skin": (0.515, 0.390, 0.315, 1.0),
    "skin_light": (0.610, 0.475, 0.385, 1.0),
    "skin_shadow": (0.335, 0.240, 0.205, 1.0),
    "eye": (0.705, 0.720, 0.655, 1.0),
    "pupil": (0.020, 0.026, 0.024, 1.0),
    "hair": (0.038, 0.045, 0.041, 1.0),
    "uniform": (0.090, 0.150, 0.165, 1.0),
    "uniform_light": (0.145, 0.225, 0.225, 1.0),
    "uniform_dark": (0.045, 0.082, 0.095, 1.0),
    "shirt": (0.390, 0.405, 0.345, 1.0),
    "tie": (0.305, 0.105, 0.085, 1.0),
    "trousers": (0.065, 0.085, 0.098, 1.0),
    "leather": (0.042, 0.036, 0.032, 1.0),
    "sole": (0.016, 0.018, 0.017, 1.0),
    "button": (0.525, 0.415, 0.220, 1.0),
}

# The helper owns the NpcHumanV2-compatible skeleton and deterministic
# geometry/export implementation. Override only source identity, budget and
# palette.
base.GENERATOR_VERSION = GENERATOR_VERSION
base.DESIGN_ID = DESIGN_ID
base.SEED = SEED
base.CANONICAL_HEIGHT = CANONICAL_HEIGHT
base.MIN_TRIANGLES = MIN_TRIANGLES
base.MAX_TRIANGLES = MAX_TRIANGLES
base.PALETTE = PALETTE


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(
            "ArtSource/Vehicles/Drivers/Blender/CityBusDriver3D.blend"
        ),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path(
            "Assets/Vehicles/Drivers/Models/CityBusDriver3D.fbx"
        ),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path(
            "Assets/Vehicles/Drivers/Models/CityBusDriver3D.json"
        ),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path(
            "ArtSource/Vehicles/Drivers/Blender/CityBusDriver3D.png"
        ),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    config = parser.parse_args(arguments)
    for field_name in ("output", "fbx", "manifest", "preview"):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


class DriverBuilder(base.PedestrianBuilder):
    def __init__(self):
        super().__init__(spec=None)

    def build(self):
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        driver_collection = bpy.data.collections.new("BP_CityBusDriver3D")
        scene_root.children.link(driver_collection)
        export_collection = bpy.data.collections.new("EXPORT_CityBusDriver")
        driver_collection.children.link(export_collection)
        presentation = bpy.data.collections.new("PRESENTATION_CityBusDriver")
        driver_collection.children.link(presentation)

        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = "tools/build-city-bus-driver-3d-model.py"
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design_id"] = DESIGN_ID
        root["bp_seed"] = SEED
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_pose"] = "apose"
        root["bp_has_own_animations"] = False

        rig = self.create_armature(export_collection, root)
        self.result = base.BuildResult(root, rig, export_collection, material)
        self.build_body()
        self.build_face()
        self.build_uniform()
        self.configure_scene_metadata()
        return self.result

    @staticmethod
    def reset_scene() -> None:
        base.PedestrianBuilder.reset_scene()
        bpy.context.scene.world.name = "WORLD_CityBusDriverPreview"

    @staticmethod
    def create_shared_material():
        material = base.PedestrianBuilder.create_shared_material()
        material["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        return material

    def build_body(self) -> None:
        # A recognizable, slightly lopsided human head. Its top vertex owns the
        # canonical 1.75 m height; nothing is worn over or substituted for it.
        self.add_part(
            "GEO_Head",
            base.make_ellipsoid(
                (0.008, -0.032, 1.590),
                (0.154, 0.124, 0.160),
                12,
                6,
            ),
            "head",
            "human_head",
            "skin",
        )
        self.add_part(
            "GEO_Neck",
            base.make_frustum_between(
                (0, -0.010, 1.325),
                (0, -0.025, 1.450),
                0.070,
                0.062,
                12,
            ),
            "neck",
            "body",
            "skin_shadow",
        )
        self.add_part(
            "GEO_Torso",
            base.make_tapered_box(
                (0, 0.010, 0.790),
                (0, -0.004, 1.335),
                (0.310, 0.185, 0),
                (0.355, 0.205, 0),
            ),
            "chest",
            "body",
            "uniform_dark",
        )
        self.add_part(
            "GEO_Pelvis",
            base.make_tapered_box(
                (0, 0.012, 0.665),
                (0, 0.010, 0.850),
                (0.295, 0.185, 0),
                (0.315, 0.185, 0),
            ),
            "pelvis",
            "body",
            "trousers",
        )

        arm_points = {
            "L": (
                (0.208, -0.004, 1.292),
                (0.470, -0.010, 1.175),
                (0.680, -0.018, 1.075),
                (0.755, -0.022, 1.035),
            ),
            "R": (
                (-0.208, 0.004, 1.292),
                (-0.470, -0.010, 1.175),
                (-0.680, -0.018, 1.075),
                (-0.755, -0.022, 1.035),
            ),
        }
        leg_points = {
            "L": (
                (0.083, 0.012, 0.750),
                (0.103, -0.012, 0.354),
                (0.112, -0.026, 0.095),
            ),
            "R": (
                (-0.083, -0.004, 0.750),
                (-0.103, 0.012, 0.354),
                (-0.112, 0.018, 0.095),
            ),
        }
        for side in ("L", "R"):
            shoulder, elbow, wrist, hand = arm_points[side]
            hip, knee, ankle = leg_points[side]
            self.add_part(
                f"GEO_UpperArm.{side}",
                base.make_frustum_between(
                    shoulder,
                    elbow,
                    0.071 if side == "L" else 0.068,
                    0.057,
                    12,
                ),
                f"upper_arm.{side}",
                "body",
                "uniform",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                base.make_frustum_between(
                    elbow,
                    wrist,
                    0.059,
                    0.045,
                    12,
                ),
                f"forearm.{side}",
                "body",
                "uniform" if side == "L" else "uniform_light",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                base.make_ellipsoid(
                    tuple((base.v(wrist) + base.v(hand)) * 0.5),
                    (0.047, 0.034, 0.060),
                    10,
                    5,
                ),
                f"hand.{side}",
                "hand_palm",
                "skin",
            )
            thumb_center = (
                (0.727, -0.057, 1.045)
                if side == "L"
                else (-0.727, -0.057, 1.045)
            )
            self.add_part(
                f"GEO_Thumb.{side}",
                base.make_tapered_box(
                    thumb_center,
                    (
                        thumb_center[0] + (0.030 if side == "L" else -0.030),
                        thumb_center[1] - 0.010,
                        thumb_center[2] - 0.035,
                    ),
                    (0.032, 0.028, 0),
                    (0.026, 0.024, 0),
                ),
                f"hand.{side}",
                "hand_thumb",
                "skin_shadow",
            )
            self.add_part(
                f"GEO_Thigh.{side}",
                base.make_frustum_between(hip, knee, 0.092, 0.073, 12),
                f"thigh.{side}",
                "body",
                "trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                base.make_frustum_between(knee, ankle, 0.075, 0.058, 12),
                f"shin.{side}",
                "body",
                "trousers",
            )

        self.add_part(
            "GEO_Foot.L",
            base.make_tapered_box(
                (0.112, -0.102, 0.000),
                (0.112, -0.068, 0.150),
                (0.170, 0.255, 0),
                (0.140, 0.185, 0),
            ),
            "foot.L",
            "body",
            "leather",
        )
        self.add_part(
            "GEO_Foot.R",
            base.make_tapered_box(
                (-0.112, -0.088, 0.000),
                (-0.112, -0.058, 0.142),
                (0.160, 0.238, 0),
                (0.132, 0.178, 0),
            ),
            "foot.R",
            "body",
            "leather",
        )

    def build_face(self) -> None:
        # Oversized horizontal eye whites are fixed to the human head. The
        # distinct dark pupils are rigid to the canonical eye bones so runtime
        # can add a small doorward glance beneath the larger head turn.
        self.add_part(
            "FACE_Ear.L",
            base.make_ellipsoid(
                (0.157, -0.030, 1.583),
                (0.030, 0.022, 0.052),
                8,
                4,
            ),
            "head",
            "human_face",
            "skin_shadow",
        )
        self.add_part(
            "FACE_Ear.R",
            base.make_ellipsoid(
                (-0.141, -0.025, 1.570),
                (0.026, 0.020, 0.047),
                8,
                4,
            ),
            "head",
            "human_face",
            "skin_shadow",
        )
        for side, x, z in (
            ("L", 0.071, 1.602),
            ("R", -0.062, 1.596),
        ):
            self.add_part(
                f"FACE_EyeWhite.{side}",
                base.make_ellipsoid(
                    (x, -0.157, z),
                    (0.064 if side == "L" else 0.060, 0.014, 0.027),
                    8,
                    4,
                ),
                "head",
                "long_horizontal_eye",
                "eye",
            )
            self.add_part(
                f"FACE_Pupil.{side}",
                base.make_ellipsoid(
                    (x + (0.006 if side == "L" else -0.004), -0.170, z),
                    (0.012, 0.007, 0.020),
                    8,
                    4,
                ),
                f"face.eye.{side}",
                "visible_eye_pupil",
                "pupil",
            )
        self.add_part(
            "FACE_Nose",
            base.make_ellipsoid(
                (0.012, -0.166, 1.548),
                (0.025, 0.030, 0.043),
                8,
                4,
            ),
            "head",
            "human_face",
            "skin_light",
        )
        self.add_part(
            "FACE_Mouth",
            base.make_tapered_box(
                (-0.036, -0.158, 1.498),
                (0.046, -0.160, 1.500),
                (0.012, 0.008, 0),
                (0.014, 0.008, 0),
            ),
            "head",
            "human_face",
            "skin_shadow",
        )
        self.add_part(
            "FACE_Brow.L",
            base.make_tapered_box(
                (0.018, -0.169, 1.638),
                (0.126, -0.160, 1.646),
                (0.014, 0.010, 0),
                (0.018, 0.010, 0),
            ),
            "head",
            "human_face",
            "hair",
        )
        self.add_part(
            "FACE_Brow.R",
            base.make_tapered_box(
                (-0.118, -0.160, 1.633),
                (-0.010, -0.169, 1.636),
                (0.019, 0.010, 0),
                (0.013, 0.010, 0),
            ),
            "head",
            "human_face",
            "hair",
        )
        self.add_part(
            "HAIR_SweptTuft.L",
            base.make_tapered_box(
                (0.014, -0.104, 1.679),
                (0.122, -0.070, 1.738),
                (0.090, 0.045, 0),
                (0.065, 0.050, 0),
            ),
            "head",
            "hair",
            "hair",
        )
        self.add_part(
            "HAIR_FlatTuft.R",
            base.make_tapered_box(
                (-0.105, -0.060, 1.670),
                (-0.030, -0.092, 1.728),
                (0.070, 0.055, 0),
                (0.090, 0.045, 0),
            ),
            "head",
            "hair",
            "hair",
        )

    def build_uniform(self) -> None:
        # Uneven front panels, offset creases and mismatched cuffs sell a
        # rumpled working uniform without compromising the clean limb shapes.
        self.add_part(
            "CLO_JacketFront.L",
            base.make_tapered_box(
                (0.082, -0.103, 0.770),
                (0.074, -0.116, 1.300),
                (0.157, 0.032, 0),
                (0.174, 0.040, 0),
            ),
            "chest",
            "rumpled_uniform",
            "uniform",
        )
        self.add_part(
            "CLO_JacketFront.R",
            base.make_tapered_box(
                (-0.082, -0.101, 0.790),
                (-0.074, -0.114, 1.300),
                (0.158, 0.032, 0),
                (0.174, 0.040, 0),
            ),
            "chest",
            "rumpled_uniform",
            "uniform_light",
        )
        self.add_part(
            "CLO_Collar.L",
            base.make_tapered_box(
                (0.086, -0.126, 1.245),
                (0.022, -0.132, 1.385),
                (0.105, 0.022, 0),
                (0.052, 0.024, 0),
            ),
            "chest",
            "rumpled_uniform",
            "uniform_dark",
        )
        self.add_part(
            "CLO_Collar.R",
            base.make_tapered_box(
                (-0.090, -0.123, 1.235),
                (-0.018, -0.131, 1.385),
                (0.112, 0.022, 0),
                (0.050, 0.024, 0),
            ),
            "chest",
            "rumpled_uniform",
            "uniform_dark",
        )
        self.add_part(
            "CLO_BroadCuff.L",
            base.make_frustum_between(
                (0.603, -0.016, 1.113),
                (0.685, -0.019, 1.071),
                0.058,
                0.050,
                12,
            ),
            "forearm.L",
            "mismatched_cuff",
            "uniform_light",
        )
        self.add_part(
            "CLO_TightCuff.R",
            base.make_frustum_between(
                (-0.630, -0.017, 1.100),
                (-0.687, -0.019, 1.071),
                0.049,
                0.043,
                12,
            ),
            "forearm.R",
            "mismatched_cuff",
            "uniform_dark",
        )
        self.add_part(
            "CLO_ShirtBib",
            base.make_tapered_box(
                (0.002, -0.126, 1.205),
                (0.004, -0.127, 1.365),
                (0.120, 0.020, 0),
                (0.095, 0.020, 0),
            ),
            "chest",
            "uniform_underlayer",
            "shirt",
        )
        self.add_part(
            "CLO_CrookedTie",
            base.make_tapered_box(
                (-0.004, -0.142, 1.105),
                (0.018, -0.144, 1.330),
                (0.045, 0.016, 0),
                (0.025, 0.016, 0),
            ),
            "chest",
            "uniform_detail",
            "tie",
        )
        self.add_part(
            "CLO_ChestPocket.R",
            base.make_tapered_box(
                (-0.110, -0.136, 1.135),
                (-0.102, -0.136, 1.245),
                (0.105, 0.018, 0),
                (0.098, 0.018, 0),
            ),
            "chest",
            "uniform_detail",
            "uniform_dark",
        )
        self.add_part(
            "CLO_Belt",
            base.make_box((0, -0.107, 0.806), (0.315, 0.026, 0.045)),
            "pelvis",
            "uniform_detail",
            "leather",
        )
        self.add_part(
            "CLO_BeltBuckle",
            base.make_box((0.015, -0.125, 0.806), (0.050, 0.014, 0.052)),
            "pelvis",
            "uniform_detail",
            "button",
        )
        for index, (lower, upper, side) in enumerate(
            (
                ((0.036, -0.140, 0.915), (0.118, -0.139, 1.010), "L"),
                ((-0.128, -0.137, 0.905), (-0.035, -0.141, 1.030), "R"),
                ((0.030, -0.141, 1.145), (0.128, -0.138, 1.205), "L"),
            ),
            start=1,
        ):
            self.add_part(
                f"CLO_Wrinkle.{side}.{index:02d}",
                base.make_tapered_box(
                    lower,
                    upper,
                    (0.012, 0.009, 0),
                    (0.016, 0.009, 0),
                ),
                "chest",
                "rumpled_uniform_crease",
                "uniform_dark",
            )
        for index, (x, z) in enumerate(
            ((-0.012, 0.945), (0.014, 1.070)),
            start=1,
        ):
            self.add_part(
                f"CLO_JacketButton.{index:02d}",
                base.make_ellipsoid(
                    (x, -0.139, z),
                    (0.015, 0.008, 0.015),
                    8,
                    4,
                ),
                "chest",
                "uniform_detail",
                "button",
            )
        self.add_part(
            "CLO_ShoeSole.L",
            base.make_box((0.112, -0.102, 0.011), (0.180, 0.265, 0.022)),
            "foot.L",
            "footwear_detail",
            "sole",
        )
        self.add_part(
            "CLO_ShoeSole.R",
            base.make_box((-0.112, -0.088, 0.010), (0.170, 0.248, 0.020)),
            "foot.R",
            "footwear_detail",
            "sole",
        )

    @staticmethod
    def configure_scene_metadata() -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-city-bus-driver-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = DESIGN_ID
        scene["bp_seed"] = SEED
        scene["bp_has_own_animations"] = False
        scene["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        scene["bp_anatomy_standard"] = base.NPC_ANATOMY_STANDARD
        scene["bp_rest_pelvis_height_m"] = base.NPC_PELVIS_HEIGHT
        scene["bp_signature_anatomy"] = json.dumps(
            list(SIGNATURE_ANATOMY), separators=(",", ":")
        )
        scene["bp_head_design"] = "ordinary low-poly human head"
        scene["bp_eye_design"] = "two long horizontal eyes with separate pupils"


def validate_driver_result(result):
    """Standalone NpcHumanV2 contract check for the bespoke driver."""

    bpy.context.view_layer.update()
    errors: list[str] = []

    bones = list(result.rig.data.bones)
    if [bone.name for bone in bones] != [
        spec.name for spec in base.SKELETON
    ]:
        errors.append("Bone order/names diverge from NpcHumanV2")
    for bone_spec in base.SKELETON:
        bone = result.rig.data.bones.get(bone_spec.name)
        if bone is None:
            continue
        actual_parent = (
            bone.parent.name if bone.parent is not None else None
        )
        if actual_parent != bone_spec.parent:
            errors.append(
                f"{bone_spec.name} parent is {actual_parent!r}, "
                f"expected {bone_spec.parent!r}"
            )
        if (bone.head_local - base.v(bone_spec.head)).length > 0.000001:
            errors.append(
                f"{bone_spec.name} head diverges from NpcHumanV2 A-pose"
            )
        if (bone.tail_local - base.v(bone_spec.tail)).length > 0.000001:
            errors.append(
                f"{bone_spec.name} tail diverges from NpcHumanV2 A-pose"
            )
        if bone.use_deform != bone_spec.deform:
            errors.append(
                f"{bone_spec.name} deform flag diverges from NpcHumanV2"
            )

    if bpy.data.actions:
        errors.append("Driver model must contain no authored Actions")
    if (
        result.rig.animation_data is not None
        and result.rig.animation_data.action is not None
    ):
        errors.append("Driver rig has an active animation")
    if result.pivots:
        errors.append(
            f"Driver must contain no mechanism pivots, got "
            f"{tuple(result.pivots)!r}"
        )

    parts = {part.obj.name: part for part in result.parts}
    required = {
        "GEO_Head",
        "FACE_EyeWhite.L",
        "FACE_EyeWhite.R",
        "FACE_Pupil.L",
        "FACE_Pupil.R",
        "GEO_Hand.L",
        "GEO_Hand.R",
        "CLO_BroadCuff.L",
        "CLO_TightCuff.R",
    }
    missing = sorted(required.difference(parts))
    if missing:
        errors.append(f"Missing required driver design parts: {missing}")
    forbidden = ("lampshade", "hood", "facevoid", "head_object")
    for name in parts:
        if any(fragment in name.lower() for fragment in forbidden):
            errors.append(f"{name} replaces or conceals the human head")

    for side in ("L", "R"):
        eye = parts.get(f"FACE_EyeWhite.{side}")
        pupil = parts.get(f"FACE_Pupil.{side}")
        if eye is not None:
            coordinates = [
                eye.obj.matrix_world @ vertex.co
                for vertex in eye.obj.data.vertices
            ]
            width = max(point.x for point in coordinates) - min(
                point.x for point in coordinates
            )
            height = max(point.z for point in coordinates) - min(
                point.z for point in coordinates
            )
            if width < height * 1.9:
                errors.append(f"{side} eye is not visibly long and horizontal")
        if pupil is not None and pupil.bone != f"face.eye.{side}":
            errors.append(f"{side} pupil must be rigid to face.eye.{side}")

    mesh_count = len(result.parts)
    triangle_count = 0
    world_vertices: list[Vector] = []
    signature_parts = []
    seen_meshes: set[int] = set()
    for part in sorted(result.parts, key=lambda item: item.obj.name):
        obj = part.obj
        mesh = obj.data
        if mesh.as_pointer() in seen_meshes:
            errors.append(f"{obj.name} reuses another part's mesh")
        seen_meshes.add(mesh.as_pointer())
        if (
            len(mesh.materials) != 1
            or mesh.materials[0] != result.material
        ):
            errors.append(
                f"{obj.name} does not use the one shared material"
            )
        if (
            len(obj.vertex_groups) != 1
            or obj.vertex_groups[0].name != part.bone
        ):
            errors.append(
                f"{obj.name} must have one rigid group for {part.bone}"
            )

        rigid_weight_error = False
        part_vertices = []
        for vertex in mesh.vertices:
            world_vertex = obj.matrix_world @ vertex.co
            world_vertices.append(world_vertex)
            part_vertices.append(
                [
                    base.stable_float(component)
                    for component in world_vertex
                ]
            )
            weights = [
                group
                for group in vertex.groups
                if group.weight > 0.000001
            ]
            if (
                len(weights) != 1
                or abs(weights[0].weight - 1.0) > 0.000001
            ):
                rigid_weight_error = True
        if rigid_weight_error:
            errors.append(f"{obj.name} is not rigidly weighted")

        triangles = base.triangulated_count(mesh)
        triangle_count += triangles
        signature_parts.append(
            {
                "name": obj.name,
                "bone": part.bone,
                "role": part.role,
                "palette_name": part.palette_name,
                "color": [
                    base.stable_float(component)
                    for component in part.color
                ],
                "vertices": part_vertices,
                "triangles": triangles,
            }
        )

    if not MIN_TRIANGLES <= triangle_count <= MAX_TRIANGLES:
        errors.append(
            f"Triangle budget is {triangle_count}; expected "
            f"{MIN_TRIANGLES}-{MAX_TRIANGLES}"
        )
    if mesh_count < 24 or mesh_count > 52:
        errors.append(
            f"Mesh count is {mesh_count}; expected 24-52 parts"
        )

    if not world_vertices:
        errors.append("Driver contains no mesh vertices")
        bounds_min = Vector((0, 0, 0))
        bounds_max = Vector((0, 0, 0))
    else:
        bounds_min = Vector(
            tuple(
                min(vertex[axis] for vertex in world_vertices)
                for axis in range(3)
            )
        )
        bounds_max = Vector(
            tuple(
                max(vertex[axis] for vertex in world_vertices)
                for axis in range(3)
            )
        )
        if abs(bounds_min.z) > 0.00001:
            errors.append(
                f"Footwear must ground at z=0, got {bounds_min.z:.6f}"
            )
        if abs(bounds_max.z - CANONICAL_HEIGHT) > 0.00001:
            errors.append(
                f"Silhouette must preserve {CANONICAL_HEIGHT} m height, "
                f"got {bounds_max.z:.6f}"
            )
        if bounds_max.x - bounds_min.x > 1.65:
            errors.append("A-pose width exceeds the NpcHumanV2 envelope")

    if result.material.get("bp_emissive", True):
        errors.append("Shared source material must be non-emissive")
    if any(
        obj.type in {"LIGHT", "CAMERA"}
        for obj in result.export_collection.objects
    ):
        errors.append("Export collection contains a light or camera")

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(f"City bus driver validation failed:\n{formatted}")

    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "seed": SEED,
        "anatomy_standard": base.NPC_ANATOMY_STANDARD,
        "rest_pelvis_height_m": base.NPC_PELVIS_HEIGHT,
        "signature_anatomy": list(SIGNATURE_ANATOMY),
        "skeleton": [
            {
                "name": spec.name,
                "head": list(spec.head),
                "tail": list(spec.tail),
                "parent": spec.parent,
                "connected": spec.connected,
                "deform": spec.deform,
            }
            for spec in base.SKELETON
        ],
        "parts": signature_parts,
        "pivots": [],
    }
    signature = hashlib.sha256(
        json.dumps(
            signature_payload, sort_keys=True, separators=(",", ":")
        ).encode("utf-8")
    ).hexdigest()

    return base.ValidationReport(
        mesh_count,
        triangle_count,
        tuple(base.stable_float(component) for component in bounds_min),
        tuple(base.stable_float(component) for component in bounds_max),
        signature,
    )


def render_preview(path: Path, result) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.get("PRESENTATION_CityBusDriver")
    if presentation is None:
        raise RuntimeError("Driver preview collection is missing")

    camera_data = bpy.data.cameras.new("CAM_CityBusDriverPreview")
    camera = bpy.data.objects.new("CAM_CityBusDriverPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = (2.45, -4.15, 2.02)
    target = Vector((0, -0.015, 0.98))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 58
    scene.camera = camera

    for name, location, energy, color, radius in (
        ("Key", (-2.3, -3.1, 4.0), 920.0, (0.72, 0.80, 0.72), 3.0),
        ("Rim", (2.5, 1.0, 3.1), 590.0, (0.32, 0.48, 0.47), 2.2),
        ("Face", (0.0, -2.2, 2.1), 300.0, (0.83, 0.58, 0.38), 1.4),
    ):
        light_data = bpy.data.lights.new(f"LIGHT_{name}", "AREA")
        light_data.energy = energy
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = radius
        light = bpy.data.objects.new(f"LIGHT_{name}", light_data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (
            target - light.location
        ).to_track_quat("-Z", "Y").to_euler()

    ground_mesh = bpy.data.meshes.new("DriverPreviewGround_Mesh")
    vertices, faces = base.make_box((0, 0.35, -0.035), (5.0, 5.0, 0.07))
    ground_mesh.from_pydata(vertices, [], faces)
    ground = bpy.data.objects.new("DriverPreviewGround", ground_mesh)
    presentation.objects.link(ground)
    ground_material = bpy.data.materials.new("MAT_DriverPreviewGround")
    ground_material.diffuse_color = (0.024, 0.037, 0.036, 1)
    ground.data.materials.append(ground_material)

    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def write_manifest(path: Path, result, report) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-bus-driver-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "height_m": CANONICAL_HEIGHT,
        "anatomy_standard": base.NPC_ANATOMY_STANDARD,
        "rest_pelvis_height_m": base.stable_float(
            base.NPC_PELVIS_HEIGHT
        ),
        "signature_anatomy": list(SIGNATURE_ANATOMY),
        "pose": "apose",
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "bounds_min": list(report.bounds_min),
        "bounds_max": list(report.bounds_max),
        "material_asset": SHARED_MATERIAL_ASSET,
        "emissive": False,
        "colliders": False,
        "lights": False,
        "rigidbodies": False,
        "animation_count": 0,
        "animations": [],
        "build_signature": report.build_signature,
        "head_design": "ordinary_low_poly_human",
        "eye_design": "two_long_horizontal_eyes_with_visible_pupils",
        "bones": [
            {
                "name": spec.name,
                "parent": spec.parent or "",
                "head": list(spec.head),
                "tail": list(spec.tail),
                "deform": spec.deform,
            }
            for spec in base.SKELETON
        ],
        "parts": [
            {
                "name": part.obj.name,
                "role": part.role,
                "bone": part.bone,
                "palette_name": part.palette_name,
                "base_color": [
                    base.stable_float(component) for component in part.color
                ],
                "vertices": len(part.obj.data.vertices),
                "triangles": base.triangulated_count(part.obj.data),
            }
            for part in sorted(result.parts, key=lambda item: item.obj.name)
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
    result = DriverBuilder().build()
    report = validate_driver_result(result)
    if not config.no_preview:
        render_preview(config.preview, result)
    base.export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    base.save_blend(config.output)
    print("CITY BUS DRIVER 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(
        f"  Skeleton bones: {len(base.SKELETON)} "
        "(NpcHumanV2-compatible 31-bone hierarchy)"
    )
    print(f"  Meshes: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{MAX_TRIANGLES}")
    print("  Own animations: 0")
    print(f"  Signature: {report.build_signature}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")


if __name__ == "__main__":
    main()
