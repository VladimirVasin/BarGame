#!/usr/bin/env python3
"""Build the deterministic low-poly supermarket Watcher Cashier.

Run this with Blender, not CPython:

    blender --background --factory-startup --python \
        tools/build-supermarket-cashier-3d-model.py

The cashier is an animation-free model on the exact 31-bone production
Player Generic A-pose skeleton. His signature is a grotesquely long neck
built from five rigid segments anchored on PIVOT_Neck.01..05 empties:
the runtime re-parents the segments under the pivots (the wheelchair
mechanism pattern) and stretches, bends and retracts the chain
procedurally, so the shared Avatar and every 31-bone validator stay
untouched. The undersized head and the enormous asymmetric watcher eyes
ride the chain tip through the ordinary head/eye bones.

Blender source space is metres, Z-up, forward -Y and anatomical left +X.
"""

from __future__ import annotations

import argparse
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


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "watcher_cashier_v1"
DISPLAY_NAME = "Watcher Cashier"
SEED = 731209
# The neck raises the resting silhouette well above the shared 1.75 m
# canon; the validator below owns the exact resting height instead.
TOTAL_HEIGHT = 2.05
MIN_TRIANGLES = 1100
MAX_TRIANGLES = 2200
SHARED_MATERIAL_ASSET = "Assets/Player3D/Materials/Player3DLit.mat"

NECK_BASE = (0.0, -0.015, 1.335)
NECK_SEGMENT_COUNT = 5
NECK_SEGMENT_HEIGHT = 0.11
NECK_REST_LENGTH = NECK_SEGMENT_COUNT * NECK_SEGMENT_HEIGHT
# The pursuit solver stretches the chain to reach the hero across the
# shop: up to 4.5 m of neck from 0.55 m at rest.
NECK_MAX_STRETCH_RATIO = 8.2
NECK_RADIUS_BOTTOM = 0.075
NECK_RADIUS_TOP = 0.065
NECK_RING_EXTRA_RADIUS = 0.012
NECK_PIVOT_NAMES = tuple(
    f"PIVOT_Neck.{index + 1:02d}" for index in range(NECK_SEGMENT_COUNT)
)

HEAD_CENTER = (0.006, -0.028, 1.960)
HEAD_RADII = (0.085, 0.078, 0.090)


def load_character_build_base():
    source_path = Path(__file__).with_name(
        "build-city-pedestrian-3d-model.py"
    )
    spec = importlib.util.spec_from_file_location(
        "bp_city_character_build_base",
        source_path,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(
            f"Could not load shared rig helpers from {source_path}"
        )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


base = load_character_build_base()

PALETTE = {
    # `coat` is the helper material's neutral source preview color.
    "coat": (0.420, 0.360, 0.130, 1.0),
    "skin": (0.545, 0.505, 0.415, 1.0),
    "skin_light": (0.600, 0.560, 0.470, 1.0),
    "skin_shadow": (0.400, 0.365, 0.300, 1.0),
    # The neck is deliberately a shade paler than the face.
    "neck_skin": (0.615, 0.580, 0.500, 1.0),
    "eye": (0.760, 0.790, 0.825, 1.0),
    "pupil": (0.016, 0.020, 0.022, 1.0),
    "hair": (0.110, 0.100, 0.085, 1.0),
    "vest": (0.430, 0.365, 0.135, 1.0),
    "vest_light": (0.500, 0.430, 0.180, 1.0),
    "vest_dark": (0.330, 0.280, 0.100, 1.0),
    "shirt": (0.360, 0.370, 0.355, 1.0),
    "collar": (0.058, 0.066, 0.072, 1.0),
    "tag": (0.640, 0.180, 0.090, 1.0),
    "trousers": (0.072, 0.080, 0.090, 1.0),
    "leather": (0.042, 0.036, 0.032, 1.0),
    "sole": (0.016, 0.018, 0.017, 1.0),
    "button": (0.525, 0.415, 0.220, 1.0),
}

# The helper owns the canonical skeleton and deterministic geometry and
# export implementation. Override only source identity and palette.
base.GENERATOR_VERSION = GENERATOR_VERSION
base.DESIGN_ID = DESIGN_ID
base.SEED = SEED
base.PALETTE = PALETTE


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(
            "ArtSource/Supermarket/Cashier/Blender/SupermarketCashier3D.blend"
        ),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path(
            "Assets/Supermarket/Cashier/Models/SupermarketCashier3D.fbx"
        ),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path(
            "Assets/Supermarket/Cashier/Models/SupermarketCashier3D.json"
        ),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path(
            "ArtSource/Supermarket/Cashier/Blender/SupermarketCashier3D.png"
        ),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = (
        sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    )
    config = parser.parse_args(arguments)
    for field_name in ("output", "fbx", "manifest", "preview"):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


def neck_segment_radii(index: int) -> tuple[float, float]:
    span = NECK_RADIUS_BOTTOM - NECK_RADIUS_TOP
    lower = NECK_RADIUS_BOTTOM - span * index / NECK_SEGMENT_COUNT
    upper = NECK_RADIUS_BOTTOM - span * (index + 1) / NECK_SEGMENT_COUNT
    return lower, upper


class CashierBuilder(base.PedestrianBuilder):
    def __init__(self):
        super().__init__(spec=None)

    def build(self):
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        cashier = bpy.data.collections.new("BP_SupermarketCashier3D")
        scene_root.children.link(cashier)
        export_collection = bpy.data.collections.new(
            "EXPORT_SupermarketCashier"
        )
        cashier.children.link(export_collection)
        presentation = bpy.data.collections.new(
            "PRESENTATION_SupermarketCashier"
        )
        cashier.children.link(presentation)

        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = (
            "tools/build-supermarket-cashier-3d-model.py"
        )
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design_id"] = DESIGN_ID
        root["bp_seed"] = SEED
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_pose"] = "apose"
        root["bp_has_own_animations"] = False

        rig = self.create_armature(export_collection, root)
        self.result = base.BuildResult(
            root, rig, export_collection, material
        )
        self.build_body()
        self.build_neck_chain()
        self.build_face()
        self.build_uniform()
        self.configure_scene_metadata()
        return self.result

    @staticmethod
    def reset_scene() -> None:
        base.PedestrianBuilder.reset_scene()
        bpy.context.scene.world.name = "WORLD_SupermarketCashierPreview"

    @staticmethod
    def create_shared_material():
        material = base.PedestrianBuilder.create_shared_material()
        material["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        return material

    def build_body(self) -> None:
        self.add_part(
            "GEO_Torso",
            base.make_tapered_box(
                (0, 0.010, 0.790),
                (0, -0.004, 1.335),
                (0.300, 0.180, 0),
                (0.340, 0.198, 0),
            ),
            "chest",
            "body",
            "shirt",
        )
        self.add_part(
            "GEO_Pelvis",
            base.make_tapered_box(
                (0, 0.012, 0.665),
                (0, 0.010, 0.850),
                (0.290, 0.180, 0),
                (0.310, 0.180, 0),
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
                    shoulder, elbow, 0.068, 0.055, 12
                ),
                f"upper_arm.{side}",
                "body",
                "shirt",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                base.make_frustum_between(
                    elbow, wrist, 0.056, 0.043, 12
                ),
                f"forearm.{side}",
                "body",
                "shirt",
            )
            # Flat dead-still palms, authored to lie face-down on the
            # checkout top once the runtime folds the arms forward.
            hand_center = (
                (hand[0] * 0.985, hand[1] - 0.004, hand[2] + 0.002)
            )
            self.add_part(
                f"GEO_Hand.{side}",
                base.make_box(hand_center, (0.100, 0.078, 0.026)),
                f"hand.{side}",
                "hand_palm",
                "skin",
            )
            thumb_center = (
                (0.727, -0.062, 1.043)
                if side == "L"
                else (-0.727, -0.062, 1.043)
            )
            self.add_part(
                f"GEO_Thumb.{side}",
                base.make_tapered_box(
                    thumb_center,
                    (
                        thumb_center[0]
                        + (0.030 if side == "L" else -0.030),
                        thumb_center[1] - 0.010,
                        thumb_center[2] - 0.030,
                    ),
                    (0.030, 0.026, 0),
                    (0.024, 0.022, 0),
                ),
                f"hand.{side}",
                "hand_thumb",
                "skin_shadow",
            )
            self.add_part(
                f"GEO_Thigh.{side}",
                base.make_frustum_between(hip, knee, 0.090, 0.071, 12),
                f"thigh.{side}",
                "body",
                "trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                base.make_frustum_between(knee, ankle, 0.073, 0.056, 12),
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
        self.add_part(
            "CLO_ShoeSole.L",
            base.make_box(
                (0.112, -0.102, 0.011), (0.180, 0.265, 0.022)
            ),
            "foot.L",
            "footwear_detail",
            "sole",
        )
        self.add_part(
            "CLO_ShoeSole.R",
            base.make_box(
                (-0.112, -0.088, 0.010), (0.170, 0.248, 0.020)
            ),
            "foot.R",
            "footwear_detail",
            "sole",
        )

    def build_neck_chain(self) -> None:
        """Five rigid periscope segments on exported pivot anchors.

        The segments bind to the static root (the wheelchair chair
        pattern), so Unity imports plain MeshRenderers the runtime can
        re-parent under the pivots and scale individually. Each segment
        carries its own vertebra ring so the articulation stays legible
        at PS1 resolution.
        """

        for index in range(NECK_SEGMENT_COUNT):
            pivot_z = NECK_BASE[2] + index * NECK_SEGMENT_HEIGHT
            pivot_name = NECK_PIVOT_NAMES[index]
            self.create_pivot(
                pivot_name,
                (NECK_BASE[0], NECK_BASE[1], pivot_z),
            )
            lower_radius, upper_radius = neck_segment_radii(index)
            top_z = pivot_z + NECK_SEGMENT_HEIGHT
            segment = base.make_frustum_between(
                (NECK_BASE[0], NECK_BASE[1], pivot_z),
                (NECK_BASE[0], NECK_BASE[1], top_z),
                lower_radius,
                upper_radius,
                12,
            )
            ring = base.make_frustum_between(
                (NECK_BASE[0], NECK_BASE[1], top_z - 0.020),
                (NECK_BASE[0], NECK_BASE[1], top_z),
                upper_radius + NECK_RING_EXTRA_RADIUS,
                upper_radius + NECK_RING_EXTRA_RADIUS,
                10,
            )
            self.add_pivot_part(
                f"NECK_Segment.{index + 1:02d}",
                base.combine_geometry(segment, ring),
                pivot_name,
                "root",
                "stretch_neck_segment",
                "neck_skin",
            )

    def build_face(self) -> None:
        # A deliberately undersized head perched on the giant neck. The
        # head and face ride the canonical head/eye bones; the runtime
        # translates the head bone by the chain's extension delta, so
        # the authored offset above the bone rest stays constant.
        self.add_part(
            "GEO_Head",
            base.make_ellipsoid(HEAD_CENTER, HEAD_RADII, 12, 6),
            "head",
            "undersized_watcher_head",
            "skin",
        )
        self.add_part(
            "HAIR_FlatCombover",
            base.make_box((0.000, 0.010, 2.020), (0.120, 0.130, 0.050)),
            "head",
            "hair",
            "hair",
        )
        self.add_part(
            "FACE_Ear.L",
            base.make_ellipsoid(
                (0.088, -0.028, 1.952), (0.020, 0.016, 0.036), 8, 4
            ),
            "head",
            "human_face",
            "skin_shadow",
        )
        self.add_part(
            "FACE_Ear.R",
            base.make_ellipsoid(
                (-0.088, -0.024, 1.948), (0.020, 0.016, 0.034), 8, 4
            ),
            "head",
            "human_face",
            "skin_shadow",
        )
        # Enormous curious eye whites for such a small head; the right
        # one runs 8% larger than the left on purpose.
        self.add_part(
            "FACE_EyeWhite.L",
            base.make_ellipsoid(
                (0.041, -0.098, 1.972), (0.034, 0.014, 0.026), 8, 4
            ),
            "head",
            "wide_watcher_eye",
            "eye",
        )
        self.add_part(
            "FACE_EyeWhite.R",
            base.make_ellipsoid(
                (-0.043, -0.098, 1.970),
                (0.0367, 0.0151, 0.0281),
                8,
                4,
            ),
            "head",
            "wide_watcher_eye",
            "eye",
        )
        # Pinprick pupils rigid to the poseable eye bones. The bones
        # rest far below the authored face, so the runtime darts the
        # pupils with small bone translations, never rotations.
        self.add_part(
            "FACE_Pupil.L",
            base.make_ellipsoid(
                (0.041, -0.113, 1.972), (0.011, 0.007, 0.013), 8, 4
            ),
            "face.eye.L",
            "visible_eye_pupil",
            "pupil",
        )
        self.add_part(
            "FACE_Pupil.R",
            base.make_ellipsoid(
                (-0.043, -0.114, 1.970), (0.011, 0.007, 0.013), 8, 4
            ),
            "face.eye.R",
            "visible_eye_pupil",
            "pupil",
        )
        # Permanently raised brows: the face is stuck mid-surprise.
        self.add_part(
            "FACE_Brow.L",
            base.make_tapered_box(
                (0.012, -0.100, 2.004),
                (0.078, -0.096, 2.012),
                (0.012, 0.009, 0),
                (0.015, 0.009, 0),
            ),
            "head",
            "human_face",
            "hair",
        )
        self.add_part(
            "FACE_Brow.R",
            base.make_tapered_box(
                (-0.080, -0.095, 1.998),
                (-0.014, -0.100, 2.008),
                (0.015, 0.009, 0),
                (0.012, 0.009, 0),
            ),
            "head",
            "human_face",
            "hair",
        )
        self.add_part(
            "FACE_Nose",
            base.make_ellipsoid(
                (0.004, -0.108, 1.938), (0.016, 0.020, 0.026), 8, 4
            ),
            "head",
            "human_face",
            "skin_light",
        )
        # Barely a mouth; he never uses it.
        self.add_part(
            "FACE_Mouth",
            base.make_box((0.000, -0.104, 1.905), (0.020, 0.008, 0.006)),
            "head",
            "human_face",
            "skin_shadow",
        )

    def build_uniform(self) -> None:
        # The too-tight collar chokes the neck base: its top radius is
        # visibly narrower than the first neck segment above it.
        self.add_part(
            "CLO_TightCollar",
            base.make_frustum_between(
                (NECK_BASE[0], NECK_BASE[1], 1.318),
                (NECK_BASE[0], NECK_BASE[1], 1.352),
                0.084,
                0.062,
                12,
            ),
            "chest",
            "strangling_collar",
            "collar",
        )
        self.add_part(
            "CLO_VestFront.L",
            base.make_tapered_box(
                (0.084, -0.102, 0.780),
                (0.076, -0.114, 1.300),
                (0.150, 0.030, 0),
                (0.168, 0.038, 0),
            ),
            "chest",
            "watcher_vest",
            "vest",
        )
        self.add_part(
            "CLO_VestFront.R",
            base.make_tapered_box(
                (-0.084, -0.100, 0.790),
                (-0.076, -0.112, 1.300),
                (0.150, 0.030, 0),
                (0.168, 0.038, 0),
            ),
            "chest",
            "watcher_vest",
            "vest_light",
        )
        self.add_part(
            "CLO_VestBack",
            base.make_tapered_box(
                (0.000, 0.108, 0.790),
                (0.000, 0.100, 1.310),
                (0.310, 0.032, 0),
                (0.330, 0.040, 0),
            ),
            "chest",
            "watcher_vest",
            "vest_dark",
        )
        self.add_part(
            "CLO_ShirtBib",
            base.make_tapered_box(
                (0.000, -0.122, 1.200),
                (0.002, -0.124, 1.330),
                (0.110, 0.018, 0),
                (0.088, 0.018, 0),
            ),
            "chest",
            "uniform_underlayer",
            "shirt",
        )
        # The one saturated accent: a store name tag over the heart.
        self.add_part(
            "CLO_NameTag",
            base.make_box((0.098, -0.124, 1.235), (0.048, 0.010, 0.032)),
            "chest",
            "uniform_detail",
            "tag",
        )
        self.add_part(
            "CLO_Belt",
            base.make_box((0, -0.105, 0.806), (0.310, 0.026, 0.044)),
            "pelvis",
            "uniform_detail",
            "leather",
        )
        for index, (x, z) in enumerate(
            ((-0.010, 0.950), (0.012, 1.080)),
            start=1,
        ):
            self.add_part(
                f"CLO_VestButton.{index:02d}",
                base.make_ellipsoid(
                    (x, -0.136, z), (0.014, 0.008, 0.014), 8, 4
                ),
                "chest",
                "uniform_detail",
                "button",
            )

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = (
            "tools/build-supermarket-cashier-3d-model.py"
        )
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = DESIGN_ID
        scene["bp_seed"] = SEED
        scene["bp_has_own_animations"] = False
        scene["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        scene["bp_neck_design"] = "segmented periscope neck"
        scene["bp_eye_design"] = "wide asymmetric watcher eyes"


def validate_cashier_result(result):
    """Standalone contract check for the bespoke cashier.

    Mirrors the shared pedestrian validation but owns the cashier's
    numbers: the five neck pivots, the raised resting height and the
    surveillance design details.
    """

    bpy.context.view_layer.update()
    errors: list[str] = []

    bones = list(result.rig.data.bones)
    if [bone.name for bone in bones] != [
        spec.name for spec in base.SKELETON
    ]:
        errors.append(
            "Generic bone order/names diverge from PlayerCharacter3D"
        )
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
                f"{bone_spec.name} head diverges from canonical A-pose"
            )
        if (bone.tail_local - base.v(bone_spec.tail)).length > 0.000001:
            errors.append(
                f"{bone_spec.name} tail diverges from canonical A-pose"
            )

    if bpy.data.actions:
        errors.append("Cashier model must contain no authored Actions")

    if tuple(result.pivots) != NECK_PIVOT_NAMES:
        errors.append(
            f"Neck pivots are {tuple(result.pivots)!r}; "
            f"expected {NECK_PIVOT_NAMES!r}"
        )
    for name, pivot in result.pivots.items():
        if pivot.type != "EMPTY" or pivot.parent != result.root:
            errors.append(
                f"{name} must be an Empty directly below ROOT_Player"
            )
        if not bool(pivot.get("bp_pivot", False)):
            errors.append(f"{name} lacks its deterministic pivot marker")

    parts = {part.obj.name: part for part in result.parts}
    required = {
        "GEO_Head",
        "FACE_EyeWhite.L",
        "FACE_EyeWhite.R",
        "FACE_Pupil.L",
        "FACE_Pupil.R",
        "CLO_TightCollar",
        "CLO_NameTag",
        "GEO_Hand.L",
        "GEO_Hand.R",
    } | {
        f"NECK_Segment.{index + 1:02d}"
        for index in range(NECK_SEGMENT_COUNT)
    }
    missing = sorted(required.difference(parts))
    if missing:
        errors.append(f"Missing required cashier design parts: {missing}")

    for index in range(NECK_SEGMENT_COUNT):
        segment = parts.get(f"NECK_Segment.{index + 1:02d}")
        if segment is None:
            continue
        if segment.bone != "root":
            errors.append(
                f"{segment.obj.name} must bind to the static root so "
                "the runtime can re-parent it under its pivot"
            )
        if segment.obj.get("bp_pivot") != NECK_PIVOT_NAMES[index]:
            errors.append(
                f"{segment.obj.name} must carry its pivot marker"
            )

    eye_widths = {}
    for side in ("L", "R"):
        eye = parts.get(f"FACE_EyeWhite.{side}")
        pupil = parts.get(f"FACE_Pupil.{side}")
        if eye is not None:
            coordinates = [
                eye.obj.matrix_world @ vertex.co
                for vertex in eye.obj.data.vertices
            ]
            eye_widths[side] = max(
                point.x for point in coordinates
            ) - min(point.x for point in coordinates)
        if pupil is not None:
            if pupil.bone != f"face.eye.{side}":
                errors.append(
                    f"{side} pupil must be rigid to face.eye.{side}"
                )
            if max(pupil.color[:3]) >= 0.08:
                errors.append(f"{side} pupil is not properly dark")
    if "L" in eye_widths and "R" in eye_widths:
        if eye_widths["R"] < eye_widths["L"] * 1.05:
            errors.append(
                "The right eye must run visibly larger than the left"
            )
        head = parts.get("GEO_Head")
        if head is not None:
            head_width = HEAD_RADII[0] * 2.0
            if eye_widths["L"] + eye_widths["R"] < head_width * 0.80:
                errors.append(
                    "The combined eye width must dominate the tiny head"
                )

    mesh_count = len(result.parts)
    triangle_count = 0
    world_vertices: list[Vector] = []
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
        for vertex in mesh.vertices:
            world_vertices.append(obj.matrix_world @ vertex.co)
        triangle_count += base.triangulated_count(mesh)

    if not MIN_TRIANGLES <= triangle_count <= MAX_TRIANGLES:
        errors.append(
            f"Triangle budget is {triangle_count}; expected "
            f"{MIN_TRIANGLES}-{MAX_TRIANGLES}"
        )
    if mesh_count < 24 or mesh_count > 56:
        errors.append(
            f"Mesh count is {mesh_count}; expected 24-56 parts"
        )

    if not world_vertices:
        errors.append("Cashier contains no mesh vertices")
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
        if abs(bounds_max.z - TOTAL_HEIGHT) > 0.00001:
            errors.append(
                f"Resting silhouette must top out at {TOTAL_HEIGHT} m, "
                f"got {bounds_max.z:.6f}"
            )

    if any(
        obj.type in {"LIGHT", "CAMERA"}
        for obj in result.export_collection.objects
    ):
        errors.append("Export collection contains a light or camera")

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(
            f"Supermarket cashier validation failed:\n{formatted}"
        )

    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "seed": SEED,
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
        "parts": [
            {
                "name": part.obj.name,
                "bone": part.bone,
                "role": part.role,
                "palette_name": part.palette_name,
                "color": [
                    base.stable_float(component)
                    for component in part.color
                ],
                "triangles": base.triangulated_count(part.obj.data),
            }
            for part in sorted(
                result.parts, key=lambda item: item.obj.name
            )
        ],
        "pivots": [
            {
                "name": name,
                "location": [
                    base.stable_float(value)
                    for value in pivot.location
                ],
            }
            for name, pivot in result.pivots.items()
        ],
    }
    import hashlib

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
    presentation = bpy.data.collections.get(
        "PRESENTATION_SupermarketCashier"
    )
    if presentation is None:
        raise RuntimeError("Cashier preview collection is missing")

    camera_data = bpy.data.cameras.new("CAM_SupermarketCashierPreview")
    camera = bpy.data.objects.new(
        "CAM_SupermarketCashierPreview", camera_data
    )
    presentation.objects.link(camera)
    camera.location = (2.30, -4.05, 2.30)
    target = Vector((0, -0.015, 1.15))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 52
    scene.camera = camera

    for name, location, energy, color, radius in (
        ("Key", (-2.3, -3.1, 4.2), 920.0, (0.74, 0.80, 0.72), 3.0),
        ("Rim", (2.5, 1.0, 3.4), 590.0, (0.34, 0.46, 0.47), 2.2),
        ("Face", (0.0, -2.4, 2.4), 320.0, (0.83, 0.62, 0.40), 1.4),
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

    ground_mesh = bpy.data.meshes.new("CashierPreviewGround_Mesh")
    vertices, faces = base.make_box((0, 0.35, -0.035), (5.0, 5.0, 0.07))
    ground_mesh.from_pydata(vertices, [], faces)
    ground = bpy.data.objects.new("CashierPreviewGround", ground_mesh)
    presentation.objects.link(ground)
    ground_material = bpy.data.materials.new("MAT_CashierPreviewGround")
    ground_material.diffuse_color = (0.026, 0.036, 0.033, 1)
    ground.data.materials.append(ground_material)

    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def write_manifest(path: Path, result, report) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-supermarket-cashier-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "height_m": TOTAL_HEIGHT,
        "pose": "apose",
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "triangle_budget": [MIN_TRIANGLES, MAX_TRIANGLES],
        "pool_eligible": False,
        "pivot_names": list(result.pivots),
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
        "neck_design": "segmented_periscope_v1",
        "neck_segment_count": NECK_SEGMENT_COUNT,
        "neck_rest_length_m": base.stable_float(NECK_REST_LENGTH),
        "neck_segment_height_m": NECK_SEGMENT_HEIGHT,
        "neck_max_stretch_ratio": NECK_MAX_STRETCH_RATIO,
        "eye_design": "wide_watcher_asymmetric",
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
                    base.stable_float(component)
                    for component in part.color
                ],
                "vertices": len(part.obj.data.vertices),
                "triangles": base.triangulated_count(part.obj.data),
            }
            for part in sorted(
                result.parts, key=lambda item: item.obj.name
            )
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
    result = CashierBuilder().build()
    report = validate_cashier_result(result)
    if not config.no_preview:
        render_preview(config.preview, result)
    base.export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    base.save_blend(config.output)
    print("SUPERMARKET CASHIER 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(
        f"  Skeleton bones: {len(base.SKELETON)} "
        "(exact Player Generic hierarchy)"
    )
    print(f"  Neck pivots: {len(result.pivots)}")
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
