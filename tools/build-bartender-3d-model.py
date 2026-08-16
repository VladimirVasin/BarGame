#!/usr/bin/env python3
"""Build the deterministic low-poly six-armed bar bartender.

Run this with Blender, not CPython:

    blender --background --factory-startup --python \
        tools/build-bartender-3d-model.py

The bartender is an animation-free model on the exact 31-bone
production Player Generic A-pose skeleton. His signature is the two
extra pairs of arms stacked below the canonical pair: each extra arm
is three rigid segments anchored on PIVOT_Arm{2,3}.{L,R} empties (the
wheelchair/cashier-neck mechanism pattern) — the runtime re-parents
the segments under the pivots and drives shoulder/elbow/wrist
rotations procedurally, so the shared Avatar and every 31-bone
validator stay untouched.

Blender source space is metres, Z-up, forward -Y and anatomical left
+X. He is presented from the waist up behind the counter, but the
canonical legs remain so the silhouette grounds like every other
character.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender-only entry.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "six_armed_bartender_v1"
DISPLAY_NAME = "Six-Armed Bartender"
SEED = 460917
TOTAL_HEIGHT = 2.00
MIN_TRIANGLES = 1400
MAX_TRIANGLES = 3400
SHARED_MATERIAL_ASSET = "Assets/Player3D/Materials/Player3DLit.mat"

EXTRA_ARM_PAIRS = (2, 3)
EXTRA_ARM_SIDES = ("L", "R")
PIVOT_SUFFIXES = ("Shoulder", "Elbow", "Wrist", "Grip")

# Rest joints of the two extra pairs, anatomical left; the right side
# mirrors X. Forward is -Y, so the arms rest angled slightly forward
# over the counter, each pair shorter and lower than the one above.
EXTRA_ARM_POINTS = {
    2: {
        "Shoulder": (0.195, -0.006, 1.130),
        "Elbow": (0.435, -0.030, 1.020),
        "Wrist": (0.620, -0.060, 0.940),
        "Grip": (0.665, -0.075, 0.925),
    },
    3: {
        "Shoulder": (0.185, -0.002, 0.985),
        "Elbow": (0.400, -0.040, 0.895),
        "Wrist": (0.565, -0.085, 0.830),
        "Grip": (0.605, -0.100, 0.815),
    },
}

EXTRA_ARM_RADII = {
    2: (0.062, 0.050, 0.040),
    3: (0.058, 0.047, 0.038),
}

ARM_PIVOT_NAMES = tuple(
    f"PIVOT_Arm{pair}.{side}.{suffix}"
    for pair in EXTRA_ARM_PAIRS
    for side in EXTRA_ARM_SIDES
    for suffix in PIVOT_SUFFIXES
)

HEAD_CENTER = (0.0, -0.030, 1.760)
HEAD_RADII = (0.105, 0.115, 0.120)


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
    "skin": (0.585, 0.520, 0.430, 1.0),
    "skin_shadow": (0.440, 0.385, 0.315, 1.0),
    "eye": (0.740, 0.760, 0.780, 1.0),
    "pupil": (0.018, 0.022, 0.024, 1.0),
    "hair": (0.075, 0.070, 0.060, 1.0),
    "moustache": (0.100, 0.090, 0.075, 1.0),
    "waistcoat": (0.165, 0.235, 0.185, 1.0),
    "waistcoat_dark": (0.105, 0.160, 0.125, 1.0),
    "shirt": (0.560, 0.520, 0.430, 1.0),
    "shirt_shadow": (0.470, 0.435, 0.355, 1.0),
    "apron": (0.160, 0.145, 0.110, 1.0),
    "trousers": (0.075, 0.082, 0.090, 1.0),
    "leather": (0.042, 0.036, 0.032, 1.0),
    "brass": (0.520, 0.360, 0.120, 1.0),
    "button": (0.500, 0.400, 0.210, 1.0),
}

# The helper owns the canonical skeleton and deterministic geometry
# and export implementation. Override only source identity/palette.
base.GENERATOR_VERSION = GENERATOR_VERSION
base.DESIGN_ID = DESIGN_ID
base.SEED = SEED
base.PALETTE = PALETTE


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("ArtSource/Bar/Bartender/Blender/BarBartender3D.blend"),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path("Assets/Bar/Bartender/Models/BarBartender3D.fbx"),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("Assets/Bar/Bartender/Models/BarBartender3D.json"),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path("ArtSource/Bar/Bartender/Blender/BarBartender3D.png"),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = (
        sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    )
    config = parser.parse_args(arguments)
    for field_name in ("output", "fbx", "manifest", "preview"):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


def mirrored(point, side: str):
    if side == "L":
        return point
    return (-point[0], point[1], point[2])


class BartenderBuilder(base.PedestrianBuilder):
    def __init__(self):
        super().__init__(spec=None)

    def build(self):
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        bartender = bpy.data.collections.new("BP_BarBartender3D")
        scene_root.children.link(bartender)
        export_collection = bpy.data.collections.new(
            "EXPORT_BarBartender"
        )
        bartender.children.link(export_collection)
        presentation = bpy.data.collections.new(
            "PRESENTATION_BarBartender"
        )
        bartender.children.link(presentation)

        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = "tools/build-bartender-3d-model.py"
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
        self.build_extra_arms()
        self.build_head()
        self.build_uniform()
        self.configure_scene_metadata()
        return self.result

    @staticmethod
    def reset_scene() -> None:
        base.PedestrianBuilder.reset_scene()
        bpy.context.scene.world.name = "WORLD_BarBartenderPreview"

    @staticmethod
    def create_shared_material():
        material = base.PedestrianBuilder.create_shared_material()
        material["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        return material

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-bartender-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = DESIGN_ID
        scene["bp_seed"] = SEED
        scene["bp_has_own_animations"] = False
        scene["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        scene["bp_arm_design"] = "stacked pivot arm pairs"

    def build_body(self) -> None:
        # A broad publican torso: heavier than a pedestrian, on the
        # canonical bones so the shared Avatar drives him unchanged.
        self.add_part(
            "GEO_Torso",
            base.make_tapered_box(
                (0, 0.010, 0.790),
                (0, -0.004, 1.335),
                (0.330, 0.200, 0),
                (0.370, 0.215, 0),
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
                (0.320, 0.200, 0),
                (0.340, 0.200, 0),
            ),
            "pelvis",
            "body",
            "trousers",
        )
        self.add_part(
            "GEO_NeckStub",
            base.make_frustum_between(
                (0, -0.015, 1.335),
                (0, -0.035, 1.660),
                0.082,
                0.068,
                10,
            ),
            "neck",
            "body",
            "skin",
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
                    shoulder, elbow, 0.072, 0.058, 12
                ),
                f"upper_arm.{side}",
                "body",
                "shirt",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                base.make_frustum_between(
                    elbow, wrist, 0.058, 0.044, 12
                ),
                f"forearm.{side}",
                "body",
                "shirt_shadow",
            )
            hand_center = (
                hand[0] * 0.985,
                hand[1] - 0.004,
                hand[2] + 0.002,
            )
            self.add_part(
                f"GEO_Hand.{side}",
                base.make_box(hand_center, (0.100, 0.078, 0.028)),
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
                base.make_frustum_between(hip, knee, 0.095, 0.074, 12),
                f"thigh.{side}",
                "body",
                "trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                base.make_frustum_between(knee, ankle, 0.076, 0.058, 12),
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
                (0.170, 0.245, 0),
                (0.140, 0.180, 0),
            ),
            "foot.R",
            "body",
            "leather",
        )

    def build_extra_arms(self) -> None:
        """Two extra rigid arm pairs on exported pivot anchors.

        The segments bind to the static root (the cashier-neck /
        wheelchair pattern), so Unity imports plain MeshRenderers the
        runtime re-parents under the pivots: shoulder and elbow (and
        the wrist for the hand block) become the procedural joints of
        each chain, and the grip empty rides the wrist as the CCD
        effector for bottles and glasses.
        """

        for pair in EXTRA_ARM_PAIRS:
            radii = EXTRA_ARM_RADII[pair]
            for side in EXTRA_ARM_SIDES:
                points = {
                    suffix: mirrored(
                        EXTRA_ARM_POINTS[pair][suffix], side
                    )
                    for suffix in PIVOT_SUFFIXES
                }
                for suffix in PIVOT_SUFFIXES:
                    self.create_pivot(
                        f"PIVOT_Arm{pair}.{side}.{suffix}",
                        points[suffix],
                    )

                self.add_pivot_part(
                    f"ARM{pair}_Upper.{side}",
                    base.make_frustum_between(
                        points["Shoulder"],
                        points["Elbow"],
                        radii[0],
                        radii[1],
                        12,
                    ),
                    f"PIVOT_Arm{pair}.{side}.Shoulder",
                    "root",
                    "extra_arm_upper",
                    "waistcoat" if pair == 2 else "shirt_shadow",
                )
                self.add_pivot_part(
                    f"ARM{pair}_Fore.{side}",
                    base.make_frustum_between(
                        points["Elbow"],
                        points["Wrist"],
                        radii[1],
                        radii[2],
                        12,
                    ),
                    f"PIVOT_Arm{pair}.{side}.Elbow",
                    "root",
                    "extra_arm_fore",
                    "shirt" if pair == 2 else "shirt_shadow",
                )
                wrist = points["Wrist"]
                grip = points["Grip"]
                hand_center = (
                    (wrist[0] + grip[0]) * 0.5,
                    (wrist[1] + grip[1]) * 0.5,
                    (wrist[2] + grip[2]) * 0.5,
                )
                self.add_pivot_part(
                    f"ARM{pair}_Hand.{side}",
                    base.make_box(
                        hand_center,
                        (0.088, 0.070, 0.026),
                    ),
                    f"PIVOT_Arm{pair}.{side}.Wrist",
                    "root",
                    "extra_arm_hand",
                    "skin",
                )

        # The dull brass band on the middle-right pouring arm — the
        # one asymmetric accent of the uniform.
        band_lower = mirrored(
            EXTRA_ARM_POINTS[2]["Elbow"], "R"
        )
        band_upper = (
            band_lower[0] * 0.96 - (-0.02),
            band_lower[1] - 0.004,
            band_lower[2] + 0.030,
        )
        self.add_pivot_part(
            "ARM2_BrassBand.R",
            base.make_frustum_between(
                band_lower,
                band_upper,
                EXTRA_ARM_RADII[2][1] + 0.010,
                EXTRA_ARM_RADII[2][1] + 0.010,
                10,
            ),
            "PIVOT_Arm2.R.Elbow",
            "root",
            "extra_arm_band",
            "brass",
        )

    def build_head(self) -> None:
        self.add_part(
            "GEO_Head",
            base.make_ellipsoid(HEAD_CENTER, HEAD_RADII, 12, 6),
            "head",
            "publican_head",
            "skin",
        )
        # The flat cap tops the exact resting silhouette.
        self.add_part(
            "HAIR_FlatCap",
            base.make_box(
                (0.000, -0.010, 1.970),
                (0.190, 0.200, 0.060),
            ),
            "head",
            "hair",
            "hair",
        )
        for side, x_sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"FACE_Ear.{side}",
                base.make_ellipsoid(
                    (x_sign * 0.104, -0.030, 1.752),
                    (0.020, 0.017, 0.038),
                    8,
                    4,
                ),
                "head",
                "human_face",
                "skin_shadow",
            )
            self.add_part(
                f"FACE_EyeWhite.{side}",
                base.make_ellipsoid(
                    (x_sign * 0.046, -0.132, 1.785),
                    (0.020, 0.012, 0.014),
                    8,
                    4,
                ),
                "head",
                "human_face",
                "eye",
            )
            self.add_part(
                f"FACE_Pupil.{side}",
                base.make_ellipsoid(
                    (x_sign * 0.046, -0.141, 1.785),
                    (0.008, 0.006, 0.008),
                    6,
                    3,
                ),
                f"face.eye.{side}",
                "human_face",
                "pupil",
            )

        self.add_part(
            "FACE_Nose",
            base.make_tapered_box(
                (0.0, -0.132, 1.748),
                (0.0, -0.156, 1.760),
                (0.034, 0.020, 0),
                (0.026, 0.014, 0),
            ),
            "head",
            "human_face",
            "skin_shadow",
        )
        # The heavy publican moustache is the face's one landmark.
        self.add_part(
            "FACE_Moustache",
            base.make_box(
                (0.0, -0.142, 1.716),
                (0.110, 0.030, 0.026),
            ),
            "head",
            "human_face",
            "moustache",
        )

    def build_uniform(self) -> None:
        # Waistcoat panels over the torso; the extra shoulder mounts
        # read as tailored slits rather than wounds.
        self.add_part(
            "CLO_WaistcoatFront",
            base.make_tapered_box(
                (0, -0.088, 0.820),
                (0, -0.102, 1.300),
                (0.300, 0.036, 0),
                (0.330, 0.036, 0),
            ),
            "chest",
            "uniform",
            "waistcoat",
        )
        self.add_part(
            "CLO_WaistcoatBack",
            base.make_tapered_box(
                (0, 0.104, 0.820),
                (0, 0.096, 1.300),
                (0.320, 0.036, 0),
                (0.350, 0.036, 0),
            ),
            "chest",
            "uniform",
            "waistcoat_dark",
        )
        self.add_part(
            "CLO_Apron",
            base.make_tapered_box(
                (0, -0.110, 0.560),
                (0, -0.096, 0.830),
                (0.270, 0.024, 0),
                (0.300, 0.024, 0),
            ),
            "pelvis",
            "uniform",
            "apron",
        )
        for index in range(3):
            self.add_part(
                f"CLO_Button.{index + 1}",
                base.make_box(
                    (0.0, -0.124, 1.215 - index * 0.120),
                    (0.026, 0.014, 0.026),
                ),
                "chest",
                "uniform",
                "button",
            )

        for pair in EXTRA_ARM_PAIRS:
            for side in EXTRA_ARM_SIDES:
                shoulder = mirrored(
                    EXTRA_ARM_POINTS[pair]["Shoulder"], side
                )
                self.add_part(
                    f"CLO_ArmMount{pair}.{side}",
                    base.make_box(
                        shoulder,
                        (0.086, 0.120, 0.096),
                    ),
                    "chest",
                    "uniform",
                    "waistcoat_dark",
                )


def validate_bartender_result(result):
    """Standalone contract check for the bespoke bartender.

    Mirrors the shared pedestrian validation but owns the bartender's
    numbers: the sixteen extra-arm pivots, the canonical resting
    height and the six-armed design details.
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
        errors.append("Bartender model must contain no authored Actions")

    if tuple(result.pivots) != ARM_PIVOT_NAMES:
        errors.append(
            f"Arm pivots are {tuple(result.pivots)!r}; "
            f"expected {ARM_PIVOT_NAMES!r}"
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
        "FACE_Moustache",
        "CLO_WaistcoatFront",
        "GEO_Hand.L",
        "GEO_Hand.R",
    } | {
        f"ARM{pair}_{segment}.{side}"
        for pair in EXTRA_ARM_PAIRS
        for side in EXTRA_ARM_SIDES
        for segment in ("Upper", "Fore", "Hand")
    }
    missing = sorted(required.difference(parts))
    if missing:
        errors.append(
            f"Missing required bartender design parts: {missing}"
        )

    for pair in EXTRA_ARM_PAIRS:
        for side in EXTRA_ARM_SIDES:
            expectations = (
                (f"ARM{pair}_Upper.{side}", "Shoulder"),
                (f"ARM{pair}_Fore.{side}", "Elbow"),
                (f"ARM{pair}_Hand.{side}", "Wrist"),
            )
            for part_name, suffix in expectations:
                segment = parts.get(part_name)
                if segment is None:
                    continue
                if segment.bone != "root":
                    errors.append(
                        f"{part_name} must bind to the static root so "
                        "the runtime can re-parent it under its pivot"
                    )
                pivot_name = f"PIVOT_Arm{pair}.{side}.{suffix}"
                if segment.obj.get("bp_pivot") != pivot_name:
                    errors.append(
                        f"{part_name} must carry marker {pivot_name}"
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
    if mesh_count < 30 or mesh_count > 72:
        errors.append(
            f"Mesh count is {mesh_count}; expected 30-72 parts"
        )

    if not world_vertices:
        errors.append("Bartender contains no mesh vertices")
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
            f"Bar bartender validation failed:\n{formatted}"
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
    camera_data = bpy.data.cameras.new("CAM_BartenderPreview")
    camera = bpy.data.objects.new("CAM_BartenderPreview", camera_data)
    scene.collection.objects.link(camera)
    camera.location = Vector((1.65, -2.35, 1.35))
    direction = (
        Vector((0.0, 0.0, 1.02)) - camera.location
    ).normalized()
    camera.rotation_euler = direction.to_track_quat(
        "-Z", "Y"
    ).to_euler()
    light_data = bpy.data.lights.new(
        "LIGHT_BartenderPreview", type="SUN"
    )
    light_data.energy = 3.4
    light = bpy.data.objects.new("LIGHT_BartenderPreview", light_data)
    scene.collection.objects.link(light)
    light.rotation_euler = (0.9, 0.25, 0.6)

    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 800
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)

    bpy.data.objects.remove(camera)
    bpy.data.cameras.remove(camera_data)
    bpy.data.objects.remove(light)
    bpy.data.lights.remove(light_data)


def write_manifest(path: Path, result, report) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-bartender-3d-model.py",
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
        "arm_design": "stacked_pivot_pairs_v1",
        "extra_arm_pairs": len(EXTRA_ARM_PAIRS),
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
    temporary.replace(path)


def main() -> None:
    config = parse_args()
    result = BartenderBuilder().build()
    report = validate_bartender_result(result)
    if not config.no_preview:
        render_preview(config.preview, result)
    base.export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    base.save_blend(config.output)
    print("BAR BARTENDER 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(
        f"  Skeleton bones: {len(base.SKELETON)} "
        "(exact Player Generic hierarchy)"
    )
    print(f"  Arm pivots: {len(result.pivots)}")
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
