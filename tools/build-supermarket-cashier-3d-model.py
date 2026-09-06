#!/usr/bin/env python3
"""Build either deterministic low-poly supermarket cashier design.

Run this with Blender, not CPython:

    blender --background --factory-startup --python-exit-code 1 --python \
        tools/build-supermarket-cashier-3d-model.py -- --variant normal

    blender --background --factory-startup --python-exit-code 1 --python \
        tools/build-supermarket-cashier-3d-model.py -- --variant watcher

Both variants are animation-free models on the NpcHumanV2-compatible
31-bone A-pose skeleton and share the same cashier uniform, palette and
detail atlas. ``normal`` is the active ordinary clerk: a 1.75 m adult
with a human-proportioned head and one short neck mesh rigid to the
canonical neck bone. ``watcher`` preserves the former bizarre design:
five rigid segments anchored on PIVOT_Neck.01..05 empties, an undersized
head and the room-spanning procedural stretch contract.

Blender source space is metres, Z-up, forward -Y and anatomical left +X.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))

import atlas_kit  # noqa: E402
from supermarket_cashier_detail_atlas import (  # noqa: E402
    CASHIER_ATLAS_REGIONS,
    DETAIL_ATLAS_NAME,
    DETAIL_ATLAS_REGION_PROP,
    DETAIL_ATLAS_SIZE,
    DETAIL_ATLAS_UV_INSET_PX,
    paint_cashier_detail_atlas,
    texture_asset_path,
    write_detail_atlas,
)
from supermarket_cashier_variants import (  # noqa: E402
    CASHIER_VARIANTS,
    CashierVariant,
    NECK_BASE,
    NECK_PIVOT_NAMES,
    NECK_RADIUS_BOTTOM,
    NECK_RADIUS_TOP,
    NECK_RING_EXTRA_RADIUS,
    NECK_SEGMENT_COUNT,
    NECK_SEGMENT_HEIGHT,
    NORMAL_VARIANT,
    WATCHER_VARIANT,
    head_center,
    head_point,
    head_radii,
    head_size,
    make_collar_geometry,
    make_fixed_neck_geometry,
    make_normal_torso_geometry,
    neck_segment_radii,
)

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


# Runtime-facing identity is selected once after CLI parsing.
GENERATOR_VERSION = WATCHER_VARIANT.generator_version
DESIGN_ID = WATCHER_VARIANT.design_id
DISPLAY_NAME = WATCHER_VARIANT.display_name
TOTAL_HEIGHT = WATCHER_VARIANT.total_height
SIGNATURE_ANATOMY = WATCHER_VARIANT.signature_anatomy
SEED = 731209
MIN_TRIANGLES = 1100
MAX_TRIANGLES = 2200
SHARED_MATERIAL_ASSET = "Assets/Player3D/Materials/Player3DLit.mat"


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
# Both cashier variants author directly against the present-day V2 bone
# landmarks.  The historical profile name is load-bearing in the shared
# helper: it is the only spec-less path that skips the legacy-to-V2 remap.
base.NPC_PROFILE_KEY = "watcher_cashier"

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

# The helper owns the NpcHumanV2-compatible skeleton and deterministic
# geometry/export implementation.  One Blender process builds one variant,
# so activating it once before constructing the builder is deterministic and
# cannot leak state between outputs.
ACTIVE_VARIANT = WATCHER_VARIANT


def activate_variant(variant: CashierVariant) -> None:
    global ACTIVE_VARIANT
    global GENERATOR_VERSION, DESIGN_ID, DISPLAY_NAME
    global TOTAL_HEIGHT, SIGNATURE_ANATOMY

    ACTIVE_VARIANT = variant
    GENERATOR_VERSION = variant.generator_version
    DESIGN_ID = variant.design_id
    DISPLAY_NAME = variant.display_name
    TOTAL_HEIGHT = variant.total_height
    SIGNATURE_ANATOMY = variant.signature_anatomy

    base.GENERATOR_VERSION = GENERATOR_VERSION
    base.DESIGN_ID = DESIGN_ID
    base.SEED = SEED
    base.PALETTE = PALETTE


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--variant",
        choices=tuple(CASHIER_VARIANTS),
        default=NORMAL_VARIANT.key,
        help=(
            "normal builds the active fixed-neck clerk; watcher preserves "
            "the former segmented, extensible design"
        ),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=None,
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=None,
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=None,
    )
    parser.add_argument(
        "--atlas",
        type=Path,
        default=Path(
            "Assets/Supermarket/Cashier/Textures/"
            "SupermarketCashier3DDetailAtlas.png"
        ),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = (
        sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    )
    config = parser.parse_args(arguments)
    variant = CASHIER_VARIANTS[config.variant]
    source_root = Path("ArtSource/Supermarket/Cashier/Blender")
    model_root = Path("Assets/Supermarket/Cashier/Models")
    defaults = {
        "output": source_root / f"{variant.output_stem}.blend",
        "fbx": model_root / f"{variant.output_stem}.fbx",
        "manifest": model_root / f"{variant.output_stem}.json",
        "preview": source_root / f"{variant.output_stem}.png",
    }
    for field_name, default_path in defaults.items():
        if getattr(config, field_name) is None:
            setattr(config, field_name, default_path)
    config.variant_spec = variant
    for field_name in ("output", "fbx", "manifest", "atlas", "preview"):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


class CashierBuilder(base.PedestrianBuilder):
    def __init__(self, variant: CashierVariant, atlas_path=None):
        # `spec=None` IS LOAD BEARING and must stay that way. The base
        # class resolves `anatomy_profile_key` from `spec.key` when a spec
        # exists and from the module global `NPC_PROFILE_KEY` when it does
        # not - and `_remap_head_point` early-outs for the profile
        # "watcher_cashier", returning every head vertex untouched. Give
        # this builder a real ArchetypeSpec and the head goes through the
        # default NpcHumanV2 remap instead: the face collapses and the
        # ten-micron height assert fails. That is why the atlas below is
        # wired by hand rather than through `spec.texture_regions`, the
        # way the cemetery raven does it.
        super().__init__(spec=None)
        self.variant = variant
        self.atlas_path = atlas_path

    def attach_preview_atlas(self, material) -> None:
        """Multiply the object colour by the detail atlas in the review.

        Overridden because the base implementation names its image from
        `self.spec.model_name`, and this builder's spec is deliberately
        `None`. Samples Closest/CLIP exactly like the Unity import
        (Point/Clamp), so the preview shows what the game draws; parts
        with no UV0 fall on texel (0, 0), the reserved white cell, and
        stay flat colour. None of this reaches the FBX - Unity imports
        no materials from these files.
        """

        if self.atlas_path is None or not Path(self.atlas_path).is_file():
            return
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        shader = next(
            node for node in nodes if node.type == "BSDF_PRINCIPLED"
        )
        object_info = next(
            node for node in nodes if node.type == "OBJECT_INFO"
        )
        image = bpy.data.images.load(str(self.atlas_path))
        image.name = "IMG_SupermarketCashier3DDetailAtlas"
        image.pack()
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = image
        texture.interpolation = "Closest"
        texture.extension = "CLIP"
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        next(
            socket for socket in mix.inputs
            if socket.identifier == "Factor_Float"
        ).default_value = 1.0
        color_a = next(
            socket for socket in mix.inputs
            if socket.identifier == "A_Color"
        )
        color_b = next(
            socket for socket in mix.inputs
            if socket.identifier == "B_Color"
        )
        result = next(
            socket for socket in mix.outputs
            if socket.identifier == "Result_Color"
        )
        for link in list(links):
            if link.to_socket == shader.inputs["Base Color"]:
                links.remove(link)
        links.new(object_info.outputs["Color"], color_a)
        links.new(texture.outputs["Color"], color_b)
        links.new(result, shader.inputs["Base Color"])
        material["bp_detail_atlas"] = DETAIL_ATLAS_NAME

    def assign_atlas_uvs(self) -> None:
        """Lay every declared region's part into its atlas sub-rect."""

        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        parts_by_name = {
            part.obj.name: part for part in self.result.parts
        }
        for region in CASHIER_ATLAS_REGIONS:
            part = parts_by_name.get(region.renderer)
            if part is None:
                raise RuntimeError(
                    f"Atlas region {region.name} names a missing part "
                    f"{region.renderer}"
                )
            rect_uv = atlas_kit.uv_rect_normalized(
                region.x,
                region.y,
                region.width,
                region.height,
                DETAIL_ATLAS_SIZE,
                DETAIL_ATLAS_UV_INSET_PX,
            )
            if region.kind == "ring":
                atlas_kit.assign_ring_strip_uv(
                    part.obj,
                    rect_uv,
                    region.sides,
                    region.rings,
                    region.name,
                    DETAIL_ATLAS_REGION_PROP,
                )
            elif region.kind == "box":
                # The box mapper wants the PIXEL rect and the atlas size,
                # not the normalized rect the ring mapper takes.
                atlas_kit.assign_box_panel_uv(
                    part.obj,
                    (region.x, region.y, region.width, region.height),
                    DETAIL_ATLAS_SIZE,
                    region.name,
                    DETAIL_ATLAS_REGION_PROP,
                )
            else:
                raise RuntimeError(
                    f"Atlas region {region.name} has unknown kind "
                    f"{region.kind}"
                )

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
        if self.variant.key == WATCHER_VARIANT.key:
            self.build_neck_chain()
        else:
            self.build_fixed_neck()
        self.build_face()
        self.build_uniform()
        # After every part exists and before the metadata pass: the box
        # mapper reads WORLD-space normals, so parenting has to be
        # resolved first.
        bpy.context.view_layer.update()
        self.assign_atlas_uvs()
        if self.atlas_path is not None:
            self.attach_preview_atlas(material)
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
        torso = base.make_tapered_box(
            (0, 0.010, 0.790),
            (0, -0.004, 1.335),
            (0.300, 0.180, 0),
            (0.340, 0.198, 0),
        )
        if self.variant.key == NORMAL_VARIANT.key:
            # The Watcher's original torso ends flat at the mechanism's
            # foot.  Give the ordinary clerk a small sloping shoulder yoke
            # inside the same renderer, so only the last 12 cm read as neck
            # while the shared uniform and arm silhouette stay recognisable.
            torso = make_normal_torso_geometry(base, torso)
        self.add_part(
            "GEO_Torso",
            torso,
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

    def build_fixed_neck(self) -> None:
        """One ordinary neck rigid to the canonical NpcHumanV2 bone.

        This is deliberately not a collapsed version of the Watcher's
        mechanism.  It has no auxiliary pivot, no separately re-parented
        segment and no stretch marker for runtime code to mistake for a
        dormant periscope.
        """

        self.add_part(
            "GEO_Neck",
            make_fixed_neck_geometry(base),
            "neck",
            "body",
            "neck_skin",
        )

    def build_face(self) -> None:
        # The Watcher keeps his undersized high head; the ordinary variant
        # maps the same recognisable face down to a 1.75 m crown and proves
        # adult height/width ratios in the validator.  Both ride the same
        # canonical head/eye bones.
        self.add_part(
            "GEO_Head",
            base.make_ellipsoid(
                head_center(self.variant),
                head_radii(self.variant),
                12,
                6,
            ),
            "head",
            self.variant.head_role,
            "skin",
        )
        self.add_part(
            "HAIR_FlatCombover",
            base.make_box(
                head_point(self.variant, (0.000, 0.010, 2.020)),
                head_size(self.variant, (0.120, 0.130, 0.050)),
            ),
            "head",
            "hair",
            "hair",
        )
        self.add_part(
            "FACE_Ear.L",
            base.make_ellipsoid(
                head_point(self.variant, (0.088, -0.028, 1.952)),
                head_size(self.variant, (0.020, 0.016, 0.036)),
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
                head_point(self.variant, (-0.088, -0.024, 1.948)),
                head_size(self.variant, (0.020, 0.016, 0.034)),
                8,
                4,
            ),
            "head",
            "human_face",
            "skin_shadow",
        )
        # The recognisable curious eye whites are shared; the right one
        # runs 8% larger than the left on purpose.
        self.add_part(
            "FACE_EyeWhite.L",
            base.make_ellipsoid(
                head_point(self.variant, (0.041, -0.098, 1.972)),
                head_size(self.variant, (0.034, 0.014, 0.026)),
                8,
                4,
            ),
            "head",
            "wide_watcher_eye",
            "eye",
        )
        self.add_part(
            "FACE_EyeWhite.R",
            base.make_ellipsoid(
                head_point(self.variant, (-0.043, -0.098, 1.970)),
                head_size(self.variant, (0.0367, 0.0151, 0.0281)),
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
                head_point(self.variant, (0.041, -0.113, 1.972)),
                head_size(self.variant, (0.011, 0.007, 0.013)),
                8,
                4,
            ),
            "face.eye.L",
            "visible_eye_pupil",
            "pupil",
        )
        self.add_part(
            "FACE_Pupil.R",
            base.make_ellipsoid(
                head_point(self.variant, (-0.043, -0.114, 1.970)),
                head_size(self.variant, (0.011, 0.007, 0.013)),
                8,
                4,
            ),
            "face.eye.R",
            "visible_eye_pupil",
            "pupil",
        )
        # Permanently raised brows: the face is stuck mid-surprise.
        self.add_part(
            "FACE_Brow.L",
            base.make_tapered_box(
                head_point(self.variant, (0.012, -0.100, 2.004)),
                head_point(self.variant, (0.078, -0.096, 2.012)),
                head_size(self.variant, (0.012, 0.009, 0)),
                head_size(self.variant, (0.015, 0.009, 0)),
            ),
            "head",
            "human_face",
            "hair",
        )
        self.add_part(
            "FACE_Brow.R",
            base.make_tapered_box(
                head_point(self.variant, (-0.080, -0.095, 1.998)),
                head_point(self.variant, (-0.014, -0.100, 2.008)),
                head_size(self.variant, (0.015, 0.009, 0)),
                head_size(self.variant, (0.012, 0.009, 0)),
            ),
            "head",
            "human_face",
            "hair",
        )
        self.add_part(
            "FACE_Nose",
            base.make_ellipsoid(
                head_point(self.variant, (0.004, -0.108, 1.938)),
                head_size(self.variant, (0.016, 0.020, 0.026)),
                8,
                4,
            ),
            "head",
            "human_face",
            "skin_light",
        )
        # Barely a mouth; he never uses it.
        self.add_part(
            "FACE_Mouth",
            base.make_box(
                head_point(self.variant, (0.000, -0.104, 1.905)),
                head_size(self.variant, (0.020, 0.008, 0.006)),
            ),
            "head",
            "human_face",
            "skin_shadow",
        )

    def build_uniform(self) -> None:
        # The same too-tight uniform collar survives on both designs.  It
        # is clothing, not anatomy, and keeps its atlas region and role.
        self.add_part(
            "CLO_TightCollar",
            make_collar_geometry(base, self.variant),
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
        scene["bp_anatomy_standard"] = base.NPC_ANATOMY_STANDARD
        scene["bp_rest_pelvis_height_m"] = base.NPC_PELVIS_HEIGHT
        scene["bp_signature_anatomy"] = json.dumps(
            list(SIGNATURE_ANATOMY), separators=(",", ":")
        )
        scene["bp_neck_design"] = self.variant.neck_design
        scene["bp_eye_design"] = self.variant.eye_design


def validate_cashier_result(result, atlas, variant: CashierVariant):
    """Standalone contract check for either bespoke cashier variant.

    Mirrors the shared pedestrian validation but owns the variant split:
    the ordinary model must have one fixed human neck and adult head ratios,
    while the preserved Watcher must retain all five procedural pivots.
    """

    bpy.context.view_layer.update()
    errors: list[str] = []

    bones = list(result.rig.data.bones)
    if [bone.name for bone in bones] != [
        spec.name for spec in base.SKELETON
    ]:
        errors.append(
            "Bone order/names diverge from NpcHumanV2"
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
                f"{bone_spec.name} head diverges from NpcHumanV2 A-pose"
            )
        if (bone.tail_local - base.v(bone_spec.tail)).length > 0.000001:
            errors.append(
                f"{bone_spec.name} tail diverges from NpcHumanV2 A-pose"
            )

    if bpy.data.actions:
        errors.append("Cashier model must contain no authored Actions")

    expected_pivots = (
        NECK_PIVOT_NAMES
        if variant.key == WATCHER_VARIANT.key
        else ()
    )
    if tuple(result.pivots) != expected_pivots:
        errors.append(
            f"Neck pivots are {tuple(result.pivots)!r}; "
            f"expected {expected_pivots!r}"
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
    }
    if variant.key == WATCHER_VARIANT.key:
        required |= {
            f"NECK_Segment.{index + 1:02d}"
            for index in range(NECK_SEGMENT_COUNT)
        }
    else:
        required.add("GEO_Neck")
    missing = sorted(required.difference(parts))
    if missing:
        errors.append(f"Missing required cashier design parts: {missing}")

    if variant.key == WATCHER_VARIANT.key:
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
    else:
        neck = parts.get("GEO_Neck")
        if neck is not None and (
            neck.bone != "neck" or neck.role != "body"
        ):
            errors.append(
                "The ordinary cashier neck must be one body mesh rigid "
                "to the canonical neck bone"
            )
        forbidden = sorted(
            name for name in parts
            if name.startswith("NECK_Segment.")
        )
        if forbidden:
            errors.append(
                "The ordinary cashier still contains stretch segments: "
                f"{forbidden}"
            )
        stretch_parts = sorted(
            part.obj.name for part in result.parts
            if "stretch" in part.role
            or bool(part.obj.get("bp_pivot", False))
        )
        if stretch_parts:
            errors.append(
                "The ordinary cashier still contains stretch metadata: "
                f"{stretch_parts}"
            )
        if neck is not None:
            neck_points = [
                neck.obj.matrix_world @ vertex.co
                for vertex in neck.obj.data.vertices
            ]
            neck_height = max(point.z for point in neck_points) - min(
                point.z for point in neck_points
            )
            if not 0.10 <= neck_height <= 0.14:
                errors.append(
                    "The ordinary cashier neck is not visibly short: "
                    f"{neck_height:.3f} m"
                )

    head = parts.get("GEO_Head")
    if head is not None and (
        head.bone != "head" or head.role != variant.head_role
    ):
        errors.append(
            f"GEO_Head must use role {variant.head_role!r} on head"
        )

    eye_widths = {}
    for side in ("L", "R"):
        eye = parts.get(f"FACE_EyeWhite.{side}")
        pupil = parts.get(f"FACE_Pupil.{side}")
        if eye is not None:
            if eye.bone != "head" or eye.role != "wide_watcher_eye":
                errors.append(
                    f"{side} eye white must preserve the head-bound "
                    "wide_watcher_eye runtime contract"
                )
            coordinates = [
                eye.obj.matrix_world @ vertex.co
                for vertex in eye.obj.data.vertices
            ]
            eye_widths[side] = max(
                point.x for point in coordinates
            ) - min(point.x for point in coordinates)
        if pupil is not None:
            if (
                pupil.bone != f"face.eye.{side}"
                or pupil.role != "visible_eye_pupil"
            ):
                errors.append(
                    f"{side} pupil must preserve visible_eye_pupil on "
                    f"face.eye.{side}"
                )
            if max(pupil.color[:3]) >= 0.08:
                errors.append(f"{side} pupil is not properly dark")
    if "L" in eye_widths and "R" in eye_widths:
        if eye_widths["R"] < eye_widths["L"] * 1.05:
            errors.append(
                "The right eye must run visibly larger than the left"
            )
        if head is not None:
            head_width = head_radii(variant)[0] * 2.0
            if eye_widths["L"] + eye_widths["R"] < head_width * 0.80:
                errors.append(
                    "The combined eye width must preserve the cashier face"
                )

    collar = parts.get("CLO_TightCollar")
    if collar is not None and (
        collar.bone != "chest" or collar.role != "strangling_collar"
    ):
        errors.append(
            "CLO_TightCollar must preserve strangling_collar on chest"
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

    if variant.key == NORMAL_VARIANT.key and head is not None:
        head_vertices = [
            head.obj.matrix_world @ vertex.co
            for vertex in head.obj.data.vertices
        ]
        head_height = max(point.z for point in head_vertices) - min(
            point.z for point in head_vertices
        )
        head_width = max(point.x for point in head_vertices) - min(
            point.x for point in head_vertices
        )
        heads_tall = TOTAL_HEIGHT / head_height
        shoulder_width = abs(
            base.BONE_BY_NAME["upper_arm.L"].head[0] -
            base.BONE_BY_NAME["upper_arm.R"].head[0]
        )
        shoulder_to_head = shoulder_width / head_width
        if not 6.90 <= heads_tall <= 7.75:
            errors.append(
                "Ordinary cashier head ratio is "
                f"{heads_tall:.3f}; expected 6.90-7.75 heads tall"
            )
        if not 2.20 <= shoulder_to_head <= 2.65:
            errors.append(
                "Ordinary cashier shoulder/head ratio is "
                f"{shoulder_to_head:.3f}; expected 2.20-2.65"
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
        "anatomy_standard": base.NPC_ANATOMY_STANDARD,
        "rest_pelvis_height_m": base.NPC_PELVIS_HEIGHT,
        "signature_anatomy": list(SIGNATURE_ANATOMY),
        "detail_atlas_sha256": atlas.sha256,
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
                "vertices": [
                    [
                        base.stable_float(component)
                        for component in (
                            part.obj.matrix_world @ vertex.co
                        )
                    ]
                    for vertex in part.obj.data.vertices
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


def write_manifest(
    path: Path,
    result,
    report,
    atlas,
    variant: CashierVariant,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-supermarket-cashier-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "height_m": TOTAL_HEIGHT,
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
        "neck_design": variant.neck_design,
        "neck_segment_count": variant.neck_segment_count,
        "neck_rest_length_m": base.stable_float(
            variant.neck_rest_length
        ),
        "neck_segment_height_m": variant.neck_segment_height,
        "neck_max_stretch_ratio": variant.neck_max_stretch_ratio,
        "eye_design": variant.eye_design,
        "texture_bindings": [
            {
                "texture_asset": texture_asset_path(atlas.path),
                "sha256": atlas.sha256,
                "size": [atlas.width, atlas.height],
                "regions": [
                    {
                        "name": region.name,
                        "renderer": region.renderer,
                        "rect_px": [
                            region.x,
                            region.y,
                            region.width,
                            region.height,
                        ],
                        "kind": region.kind,
                    }
                    for region in CASHIER_ATLAS_REGIONS
                ],
            }
        ],
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
    variant = config.variant_spec
    activate_variant(variant)
    # Painted before the build so the Blender review render samples the
    # very file Unity imports.
    atlas = write_detail_atlas(paint_cashier_detail_atlas(), config.atlas)
    result = CashierBuilder(variant, atlas_path=config.atlas).build()
    report = validate_cashier_result(result, atlas, variant)
    if not config.no_preview:
        render_preview(config.preview, result)
    base.export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report, atlas, variant)
    base.save_blend(config.output)
    print("SUPERMARKET CASHIER 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Variant: {variant.key}")
    print(f"  Design: {DESIGN_ID}")
    print(
        f"  Skeleton bones: {len(base.SKELETON)} "
        "(NpcHumanV2-compatible 31-bone hierarchy)"
    )
    print(f"  Neck pivots: {len(result.pivots)}")
    print(f"  Meshes: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{MAX_TRIANGLES}")
    print("  Own animations: 0")
    print(f"  Signature: {report.build_signature}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    print(f"  Atlas: {config.atlas} ({atlas.sha256[:12]}...)")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")


if __name__ == "__main__":
    main()
