#!/usr/bin/env python3
"""Build the deterministic low-poly City pedestrian art library.

Run this with Blender, not CPython:

    blender --background --python tools/build-city-pedestrian-3d-model.py

The generator owns two editable model .blends, two animation-free production
FBXs, one animation-only locomotion FBX, manifests and review renders.  Both
pedestrians and every clip deliberately carry the exact Generic skeleton names,
parent hierarchy and A-pose rest transforms of PlayerCharacter3D.

Blender source space is metres, Z-up, forward -Y and anatomical left +X.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from dataclasses import dataclass, field
from pathlib import Path
import sys
from typing import Iterable, Sequence

try:
    import bpy
    from mathutils import Euler, Quaternion, Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "2.0.0"
CANONICAL_HEIGHT = 1.75
SHARED_MATERIAL_NAME = "MAT_Player3DLit"
ANIMATION_FPS = 24
ANIMATION_SOURCE = "Assets/Pedestrians/Animations/CityPedestrianLocomotion.fbx"


@dataclass(frozen=True)
class ArchetypeSpec:
    key: str
    design_id: str
    display_name: str
    seed: int
    blend_name: str
    model_name: str
    preview_name: str
    idle_clip: str
    walk_clip: str
    triangle_budget: tuple[int, int]


ARCHETYPES = {
    "lampshade": ArchetypeSpec(
        "lampshade", "lampshade_walker_v1", "Lampshade Walker", 190417,
        "CityPedestrian3D.blend", "CityPedestrian3D", "CityPedestrian3D.png",
        "LampshadeIdle", "LampshadeWalk", (800, 1400),
    ),
    "chair_carrier": ArchetypeSpec(
        "chair_carrier", "chair_carrier_v1", "Chair Carrier", 241109,
        "ChairCarrierPedestrian3D.blend", "ChairCarrierPedestrian3D",
        "ChairCarrierPedestrian3D.png", "ChairCarrierIdle", "ChairCarrierWalk",
        (800, 1600),
    ),
}


@dataclass(frozen=True)
class BoneSpec:
    name: str
    head: tuple[float, float, float]
    tail: tuple[float, float, float]
    parent: str | None = None
    connected: bool = False
    deform: bool = True


@dataclass
class PartRecord:
    obj: bpy.types.Object
    bone: str
    role: str
    palette_name: str
    color: tuple[float, float, float, float]


@dataclass
class BuildResult:
    root: bpy.types.Object
    rig: bpy.types.Object
    export_collection: bpy.types.Collection
    material: bpy.types.Material
    parts: list[PartRecord] = field(default_factory=list)


@dataclass(frozen=True)
class BonePose:
    rotation_degrees: tuple[float, float, float] = (0.0, 0.0, 0.0)
    location_m: tuple[float, float, float] = (0.0, 0.0, 0.0)
    scale: tuple[float, float, float] = (1.0, 1.0, 1.0)


@dataclass(frozen=True)
class ActionSpec:
    name: str
    archetype: str
    duration_seconds: float
    frame_end: int
    authored_posture: str
    gait: str


@dataclass(frozen=True)
class ValidationReport:
    mesh_count: int
    triangle_count: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]
    build_signature: str


PALETTE = {
    "coat": (0.080, 0.155, 0.122, 1.0),
    "coat_light": (0.120, 0.215, 0.165, 1.0),
    "coat_dark": (0.040, 0.085, 0.068, 1.0),
    "trousers": (0.075, 0.105, 0.115, 1.0),
    "glove": (0.080, 0.075, 0.065, 1.0),
    "hood": (0.245, 0.235, 0.205, 1.0),
    "hood_light": (0.345, 0.325, 0.275, 1.0),
    "hood_dark": (0.115, 0.112, 0.100, 1.0),
    "void": (0.008, 0.010, 0.009, 1.0),
    "amber": (0.525, 0.275, 0.075, 1.0),
    "bag": (0.130, 0.090, 0.065, 1.0),
    "bag_edge": (0.060, 0.045, 0.035, 1.0),
    "rubber": (0.035, 0.050, 0.045, 1.0),
    "leather": (0.055, 0.042, 0.035, 1.0),
    "sole": (0.018, 0.019, 0.017, 1.0),
    "button": (0.155, 0.145, 0.115, 1.0),
    "work_jacket": (0.245, 0.145, 0.075, 1.0),
    "work_jacket_light": (0.335, 0.205, 0.105, 1.0),
    "work_jacket_dark": (0.105, 0.070, 0.048, 1.0),
    "work_trousers": (0.105, 0.125, 0.118, 1.0),
    "skin": (0.355, 0.235, 0.165, 1.0),
    "chair_wood": (0.285, 0.115, 0.055, 1.0),
    "chair_edge": (0.095, 0.045, 0.028, 1.0),
    "chair_wear": (0.455, 0.265, 0.105, 1.0),
    "strap_cloth": (0.080, 0.095, 0.080, 1.0),
    "shoe": (0.045, 0.038, 0.032, 1.0),
}


def v(value: Sequence[float]) -> Vector:
    return Vector(tuple(float(component) for component in value))


def player_bone_specs() -> tuple[BoneSpec, ...]:
    """Canonical PlayerCharacter3D 2.5.0 A-pose skeleton contract."""

    points = {
        "hip.L": (0.083, 0.012, 0.750),
        "hip.R": (-0.083, -0.004, 0.750),
        "knee.L": (0.103, -0.012, 0.354),
        "knee.R": (-0.103, 0.012, 0.354),
        "ankle.L": (0.112, -0.026, 0.095),
        "ankle.R": (-0.112, 0.018, 0.095),
        "toe.L": (0.112, -0.230, 0.045),
        "toe.R": (-0.112, -0.188, 0.045),
        "shoulder.L": (0.208, -0.004, 1.292),
        "shoulder.R": (-0.208, 0.004, 1.292),
        "elbow.L": (0.470, -0.010, 1.175),
        "wrist.L": (0.680, -0.018, 1.075),
        "hand.L": (0.755, -0.022, 1.035),
        "elbow.R": (-0.470, -0.010, 1.175),
        "wrist.R": (-0.680, -0.018, 1.075),
        "hand.R": (-0.755, -0.022, 1.035),
    }

    return (
        BoneSpec("root", (0, 0, 0), (0, 0, 0.18), deform=False),
        BoneSpec("pelvis", (0, 0.008, 0.700), (0, 0.004, 0.900), "root"),
        BoneSpec("spine", (0, 0.004, 0.900), (0, 0, 1.120), "pelvis", True),
        BoneSpec("chest", (0, 0, 1.120), (0, -0.010, 1.335), "spine", True),
        BoneSpec("neck", (0, -0.010, 1.335), (0, -0.025, 1.430), "chest", True),
        BoneSpec("head", (0, -0.025, 1.430), (0, -0.050, 1.675), "neck", True),
        BoneSpec("face.eye.L", (0.052, -0.147, 1.581), (0.052, -0.147, 1.599), "head"),
        BoneSpec("face.eye.R", (-0.052, -0.147, 1.581), (-0.052, -0.147, 1.599), "head"),
        BoneSpec("face.brow.L", (0.082, -0.154, 1.627), (0.027, -0.157, 1.621), "head"),
        BoneSpec("face.brow.R", (-0.082, -0.154, 1.625), (-0.027, -0.157, 1.619), "head"),
        BoneSpec("face.mouth", (-0.036, -0.151, 1.477), (0.048, -0.151, 1.477), "head"),
        BoneSpec("SOCKET_Mouth", (0.006, -0.158, 1.477), (0.006, -0.218, 1.477), "head", deform=False),
        BoneSpec("clavicle.L", (0, -0.008, 1.325), points["shoulder.L"], "chest", deform=False),
        BoneSpec("upper_arm.L", points["shoulder.L"], points["elbow.L"], "clavicle.L", True),
        BoneSpec("forearm.L", points["elbow.L"], points["wrist.L"], "upper_arm.L", True),
        BoneSpec("hand.L", points["wrist.L"], points["hand.L"], "forearm.L", True),
        BoneSpec("SOCKET_Grip.L", (0.734, -0.02088, 1.0462), (0.734, -0.07588, 1.0462), "hand.L", deform=False),
        BoneSpec("SOCKET_Vessel.L", (0.734, -0.02088, 1.0462), (0.734, -0.02088, 0.9612), "hand.L", deform=False),
        BoneSpec("clavicle.R", (0, -0.008, 1.325), points["shoulder.R"], "chest", deform=False),
        BoneSpec("upper_arm.R", points["shoulder.R"], points["elbow.R"], "clavicle.R", True),
        BoneSpec("forearm.R", points["elbow.R"], points["wrist.R"], "upper_arm.R", True),
        BoneSpec("hand.R", points["wrist.R"], points["hand.R"], "forearm.R", True),
        BoneSpec("SOCKET_Grip.R", (-0.734, -0.02088, 1.0462), (-0.734, -0.07588, 1.0462), "hand.R", deform=False),
        BoneSpec("SOCKET_Cigarette.R", (-0.734, -0.03088, 1.0582), (-0.734, -0.10588, 1.0582), "hand.R", deform=False),
        BoneSpec("SOCKET_Bottle.R", (-0.734, -0.02088, 1.0462), (-0.734, -0.02088, 0.9612), "hand.R", deform=False),
        BoneSpec("thigh.L", points["hip.L"], points["knee.L"], "pelvis"),
        BoneSpec("shin.L", points["knee.L"], points["ankle.L"], "thigh.L", True),
        BoneSpec("foot.L", points["ankle.L"], points["toe.L"], "shin.L", True),
        BoneSpec("thigh.R", points["hip.R"], points["knee.R"], "pelvis"),
        BoneSpec("shin.R", points["knee.R"], points["ankle.R"], "thigh.R", True),
        BoneSpec("foot.R", points["ankle.R"], points["toe.R"], "shin.R", True),
    )


SKELETON = player_bone_specs()
BONE_BY_NAME = {bone.name: bone for bone in SKELETON}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=Path("ArtSource/Pedestrians/Blender"),
    )
    parser.add_argument(
        "--model-dir",
        type=Path,
        default=Path("Assets/Pedestrians/Models"),
    )
    parser.add_argument(
        "--animation-dir",
        type=Path,
        default=Path("Assets/Pedestrians/Animations"),
    )
    parser.add_argument(
        "--archetype",
        choices=("all", *ARCHETYPES),
        default="all",
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    config = parser.parse_args(arguments)
    # Blender resolves a relative render path against the unsaved startup
    # blend's `//` root (often the drive root), unlike Python file writes.
    # Resolve every output from the invocation cwd before touching Blender IO.
    for field_name in ("source_dir", "model_dir", "animation_dir"):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


def make_box(center: Sequence[float], size: Sequence[float]):
    c = v(center)
    half = v(size) * 0.5
    vertices = [
        c + Vector((sx * half.x, sy * half.y, sz * half.z))
        for sz in (-1, 1)
        for sy in (-1, 1)
        for sx in (-1, 1)
    ]
    faces = [
        (0, 1, 3, 2),
        (4, 6, 7, 5),
        (0, 4, 5, 1),
        (2, 3, 7, 6),
        (0, 2, 6, 4),
        (1, 5, 7, 3),
    ]
    return vertices, faces


def make_tapered_box(
    lower_center: Sequence[float],
    upper_center: Sequence[float],
    lower_size: Sequence[float],
    upper_size: Sequence[float],
):
    lower = v(lower_center)
    upper = v(upper_center)
    low_half = v(lower_size) * 0.5
    up_half = v(upper_size) * 0.5
    vertices = []
    for center, half in ((lower, low_half), (upper, up_half)):
        for sy in (-1, 1):
            for sx in (-1, 1):
                vertices.append(center + Vector((sx * half.x, sy * half.y, 0)))
    faces = [
        (0, 2, 3, 1),
        (4, 5, 7, 6),
        (0, 1, 5, 4),
        (2, 6, 7, 3),
        (0, 4, 6, 2),
        (1, 3, 7, 5),
    ]
    return vertices, faces


def make_frustum_between(
    start: Sequence[float],
    end: Sequence[float],
    radius_start: float,
    radius_end: float,
    sides: int = 12,
    flatten: float = 0.82,
):
    start_vector = v(start)
    end_vector = v(end)
    axis = end_vector - start_vector
    rotation = axis.to_track_quat("Z", "Y")
    basis_x = rotation @ Vector((1, 0, 0))
    basis_y = rotation @ Vector((0, 1, 0))
    vertices = []
    for center, radius in ((start_vector, radius_start), (end_vector, radius_end)):
        for index in range(sides):
            angle = 2.0 * math.pi * index / sides
            offset = (
                basis_x * math.cos(angle) * radius
                + basis_y * math.sin(angle) * radius * flatten
            )
            vertices.append(center + offset)
    faces: list[tuple[int, ...]] = []
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple(range(sides, sides * 2)))
    for index in range(sides):
        next_index = (index + 1) % sides
        faces.append((index, next_index, sides + next_index, sides + index))
    return vertices, faces


def make_ellipsoid(
    center: Sequence[float],
    radii: Sequence[float],
    segments: int = 12,
    rings: int = 6,
):
    center_vector = v(center)
    radius_vector = v(radii)
    vertices = [center_vector + Vector((0, 0, -radius_vector.z))]
    for ring in range(1, rings):
        phi = -math.pi * 0.5 + math.pi * ring / rings
        for segment in range(segments):
            theta = 2.0 * math.pi * segment / segments
            vertices.append(
                center_vector
                + Vector(
                    (
                        radius_vector.x * math.cos(phi) * math.cos(theta),
                        radius_vector.y * math.cos(phi) * math.sin(theta),
                        radius_vector.z * math.sin(phi),
                    )
                )
            )
    top_index = len(vertices)
    vertices.append(center_vector + Vector((0, 0, radius_vector.z)))
    faces: list[tuple[int, ...]] = []
    for segment in range(segments):
        next_segment = (segment + 1) % segments
        faces.append((0, 1 + next_segment, 1 + segment))
    for ring in range(rings - 2):
        first = 1 + ring * segments
        second = first + segments
        for segment in range(segments):
            next_segment = (segment + 1) % segments
            faces.append(
                (
                    first + segment,
                    first + next_segment,
                    second + next_segment,
                    second + segment,
                )
            )
    last_ring = 1 + (rings - 2) * segments
    for segment in range(segments):
        next_segment = (segment + 1) % segments
        faces.append((last_ring + segment, last_ring + next_segment, top_index))
    return vertices, faces


class PedestrianBuilder:
    def __init__(self, spec: ArchetypeSpec):
        self.spec = spec
        self.result: BuildResult | None = None

    def build(self) -> BuildResult:
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        pedestrian = bpy.data.collections.new(f"BP_{self.spec.model_name}")
        scene_root.children.link(pedestrian)
        export_collection = bpy.data.collections.new("EXPORT_CityPedestrian")
        pedestrian.children.link(export_collection)
        presentation = bpy.data.collections.new("PRESENTATION_CityPedestrian")
        pedestrian.children.link(presentation)

        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = "tools/build-city-pedestrian-3d-model.py"
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design_id"] = self.spec.design_id
        root["bp_seed"] = self.spec.seed
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_shared_animation_source"] = ANIMATION_SOURCE

        rig = self.create_armature(export_collection, root)
        self.result = BuildResult(root, rig, export_collection, material)
        if self.spec.key == "lampshade":
            self.build_body()
            self.build_clothing_and_details()
        else:
            self.build_chair_carrier_body()
            self.build_chair_carrier_details()
        self.configure_scene_metadata()
        return self.result

    @staticmethod
    def reset_scene() -> None:
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
        scene.render.fps = 24
        scene.render.fps_base = 1.0
        for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
            try:
                scene.render.engine = engine
                break
            except TypeError:
                continue
        scene.world = bpy.data.worlds.new("WORLD_PedestrianPreview")
        scene.world.use_nodes = True
        background = scene.world.node_tree.nodes.get("Background")
        if background is not None:
            background.inputs["Color"].default_value = (0.010, 0.016, 0.014, 1)
            background.inputs["Strength"].default_value = 0.18

    @staticmethod
    def create_shared_material() -> bpy.types.Material:
        material = bpy.data.materials.new(SHARED_MATERIAL_NAME)
        material.use_nodes = True
        material.diffuse_color = PALETTE["coat"]
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        object_info = nodes.new("ShaderNodeObjectInfo")
        shader.inputs["Roughness"].default_value = 0.86
        shader.inputs["Metallic"].default_value = 0.0
        emission = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
        if emission is not None:
            emission.default_value = (0, 0, 0, 1)
        links.new(object_info.outputs["Color"], shader.inputs["Base Color"])
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])
        material["bp_runtime_material"] = "Assets/Player3D/Materials/Player3DLit.mat"
        material["bp_emissive"] = False
        return material

    @staticmethod
    def create_armature(
        collection: bpy.types.Collection,
        root: bpy.types.Object,
    ) -> bpy.types.Object:
        armature_data = bpy.data.armatures.new("RIG_Player_Data")
        rig = bpy.data.objects.new("RIG_Player", armature_data)
        collection.objects.link(rig)
        rig.parent = root
        rig.show_in_front = True
        rig.display_type = "WIRE"
        rig["bp_export"] = True
        rig["bp_skeleton_contract"] = "PlayerCharacter3D exact A-pose v2.5.0"

        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        created = {}
        for spec in SKELETON:
            bone = armature_data.edit_bones.new(spec.name)
            bone.head = spec.head
            bone.tail = spec.tail
            bone.use_deform = spec.deform
            created[spec.name] = bone
        for spec in SKELETON:
            if spec.parent is None:
                continue
            bone = created[spec.name]
            bone.parent = created[spec.parent]
            bone.use_connect = spec.connected
        bpy.ops.object.mode_set(mode="OBJECT")
        rig.select_set(False)
        return rig

    def add_part(
        self,
        name: str,
        geometry,
        bone_name: str,
        role: str,
        palette_name: str,
        origin: Sequence[float] | None = None,
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        if bone_name not in BONE_BY_NAME:
            raise ValueError(f"Unknown canonical bone: {bone_name}")
        color = PALETTE[palette_name]
        vertices, faces = geometry
        origin_vector = v(origin or BONE_BY_NAME[bone_name].head)
        mesh = bpy.data.meshes.new(f"{name}_Mesh")
        mesh.from_pydata(
            [tuple(vertex - origin_vector) for vertex in vertices],
            [],
            faces,
        )
        mesh.update(calc_edges=True)
        for polygon in mesh.polygons:
            polygon.use_smooth = False
        obj = bpy.data.objects.new(name, mesh)
        self.result.export_collection.objects.link(obj)
        obj.location = origin_vector
        obj.color = color
        obj.data.materials.append(self.result.material)
        obj.parent = self.result.rig
        obj.matrix_parent_inverse = self.result.rig.matrix_world.inverted()

        group = obj.vertex_groups.new(name=bone_name)
        group.add(range(len(mesh.vertices)), 1.0, "REPLACE")
        triangulate = obj.modifiers.new("Triangulate", "TRIANGULATE")
        triangulate.quad_method = "FIXED"
        triangulate.ngon_method = "CLIP"
        armature = obj.modifiers.new("Armature", "ARMATURE")
        armature.object = self.result.rig
        armature.use_deform_preserve_volume = False

        obj["bp_export"] = True
        obj["bp_role"] = role
        obj["bp_bone"] = bone_name
        obj["bp_palette"] = palette_name
        obj["bp_base_color"] = list(color)
        obj["bp_generator_version"] = GENERATOR_VERSION
        self.result.parts.append(
            PartRecord(obj, bone_name, role, palette_name, color)
        )
        return obj

    def build_body(self) -> None:
        # A dark interior head exists only to give the open hood real depth.
        self.add_part(
            "GEO_FaceVoid",
            make_ellipsoid((0, -0.047, 1.555), (0.095, 0.078, 0.145), 12, 6),
            "head",
            "face_void",
            "void",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.325), (0, -0.025, 1.445), 0.072, 0.064),
            "neck",
            "body",
            "coat_dark",
        )
        self.add_part(
            "GEO_Torso",
            make_tapered_box((0, 0.010, 0.790), (0, -0.004, 1.335), (0.30, 0.18, 0), (0.34, 0.19, 0)),
            "chest",
            "body",
            "coat_dark",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box((0, 0.012, 0.665), (0, 0.010, 0.850), (0.29, 0.18, 0), (0.31, 0.18, 0)),
            "pelvis",
            "body",
            "coat",
        )

        limb_points = {
            "L": ((0.208, -0.004, 1.292), (0.470, -0.010, 1.175), (0.680, -0.018, 1.075), (0.755, -0.022, 1.035)),
            "R": ((-0.208, 0.004, 1.292), (-0.470, -0.010, 1.175), (-0.680, -0.018, 1.075), (-0.755, -0.022, 1.035)),
        }
        leg_points = {
            "L": ((0.083, 0.012, 0.750), (0.103, -0.012, 0.354), (0.112, -0.026, 0.095)),
            "R": ((-0.083, -0.004, 0.750), (-0.103, 0.012, 0.354), (-0.112, 0.018, 0.095)),
        }
        for side in ("L", "R"):
            shoulder, elbow, wrist, hand = limb_points[side]
            hip, knee, ankle = leg_points[side]
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(shoulder, elbow, 0.070, 0.058, 12),
                f"upper_arm.{side}",
                "body",
                "coat",
            )
            # The left sleeve is deliberately longer and heavier.
            forearm_end = hand if side == "L" else wrist
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(elbow, forearm_end, 0.062, 0.050 if side == "L" else 0.044, 12),
                f"forearm.{side}",
                "body",
                "coat_light" if side == "L" else "coat",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid(
                    tuple((v(wrist) + v(hand)) * 0.5),
                    (0.046, 0.035, 0.060),
                    10,
                    5,
                ),
                f"hand.{side}",
                "body",
                "glove",
            )
            self.add_part(
                f"GEO_Thigh.{side}",
                make_frustum_between(hip, knee, 0.092, 0.074, 12),
                f"thigh.{side}",
                "body",
                "trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                make_frustum_between(knee, ankle, 0.076, 0.060, 12),
                f"shin.{side}",
                "body",
                "trousers",
            )

        # Intentionally mismatched footwear: broad left rubber boot, narrow
        # right leather work shoe. Both keep the canonical 0 m sole contact.
        self.add_part(
            "GEO_Foot.L",
            make_tapered_box((0.112, -0.105, 0.000), (0.112, -0.070, 0.170), (0.205, 0.285, 0), (0.165, 0.205, 0)),
            "foot.L",
            "body",
            "rubber",
        )
        self.add_part(
            "GEO_Foot.R",
            make_tapered_box((-0.112, -0.080, 0.000), (-0.112, -0.055, 0.135), (0.150, 0.235, 0), (0.125, 0.175, 0)),
            "foot.R",
            "body",
            "leather",
        )

    def build_clothing_and_details(self) -> None:
        # Long coat panels stop above the knee swing envelope. Their broken
        # lower line reads as worn cloth without requiring skinning or cloth.
        self.add_part(
            "CLO_CoatFront.L",
            make_tapered_box((0.082, -0.105, 0.675), (0.072, -0.102, 1.300), (0.155, 0.035, 0), (0.170, 0.040, 0)),
            "chest",
            "clothing",
            "coat",
        )
        self.add_part(
            "CLO_CoatFront.R",
            make_tapered_box((-0.082, -0.105, 0.705), (-0.072, -0.102, 1.300), (0.155, 0.035, 0), (0.170, 0.040, 0)),
            "chest",
            "clothing",
            "coat_light",
        )
        self.add_part(
            "CLO_CoatBack",
            make_tapered_box((0, 0.105, 0.690), (0, 0.105, 1.305), (0.310, 0.038, 0), (0.335, 0.040, 0)),
            "chest",
            "clothing",
            "coat_dark",
        )
        self.add_part(
            "CLO_CoatCollar.L",
            make_tapered_box((0.082, -0.116, 1.245), (0.040, -0.122, 1.390), (0.105, 0.025, 0), (0.065, 0.025, 0)),
            "chest",
            "clothing_detail",
            "coat_dark",
        )
        self.add_part(
            "CLO_CoatCollar.R",
            make_tapered_box((-0.082, -0.116, 1.245), (-0.040, -0.122, 1.390), (0.105, 0.025, 0), (0.065, 0.025, 0)),
            "chest",
            "clothing_detail",
            "coat_dark",
        )
        self.add_part(
            "CLO_LongCuff.L",
            make_frustum_between((0.625, -0.017, 1.102), (0.735, -0.021, 1.048), 0.057, 0.054, 12),
            "forearm.L",
            "clothing_detail",
            "coat_dark",
        )
        self.add_part(
            "CLO_ShortCuff.R",
            make_frustum_between((-0.610, -0.017, 1.110), (-0.680, -0.018, 1.075), 0.052, 0.045, 12),
            "forearm.R",
            "clothing_detail",
            "coat_dark",
        )

        # Tall square backpack. It has no diagonal hero-like satchel strap.
        self.add_part(
            "ACC_TallBackpack",
            make_tapered_box((0, 0.155, 0.805), (0, 0.155, 1.435), (0.315, 0.155, 0), (0.285, 0.145, 0)),
            "chest",
            "accessory",
            "bag",
        )
        self.add_part(
            "ACC_BackpackCap",
            make_box((0, 0.157, 1.445), (0.285, 0.155, 0.055)),
            "chest",
            "accessory_detail",
            "bag_edge",
        )
        self.add_part(
            "ACC_BackpackSide.L",
            make_box((0.155, 0.160, 1.125), (0.035, 0.165, 0.500)),
            "chest",
            "accessory_detail",
            "bag_edge",
        )
        self.add_part(
            "ACC_BackpackSide.R",
            make_box((-0.155, 0.160, 1.125), (0.035, 0.165, 0.500)),
            "chest",
            "accessory_detail",
            "bag_edge",
        )

        # Crumpled asymmetric trapezoid hood / lampshade. The main solid sits
        # behind a recessed void plate so the face reads as absence, while the
        # single amber mark remains ordinary non-emissive paint.
        self.add_part(
            "ACC_LampshadeHood",
            make_tapered_box((0.015, -0.030, 1.405), (-0.020, -0.010, 1.750), (0.390, 0.330, 0), (0.235, 0.225, 0)),
            "head",
            "signature_silhouette",
            "hood",
        )
        self.add_part(
            "ACC_HoodDarkOpening",
            make_tapered_box((0.012, -0.198, 1.445), (0.000, -0.130, 1.670), (0.270, 0.018, 0), (0.165, 0.018, 0)),
            "head",
            "face_void",
            "void",
        )
        self.add_part(
            "ACC_HoodBentRim",
            make_tapered_box((0.018, -0.055, 1.395), (0.005, -0.052, 1.435), (0.425, 0.355, 0), (0.385, 0.325, 0)),
            "head",
            "signature_silhouette",
            "hood_dark",
        )
        self.add_part(
            "ACC_HoodCrease.L",
            make_tapered_box((0.118, -0.177, 1.485), (0.082, -0.126, 1.690), (0.035, 0.012, 0), (0.025, 0.012, 0)),
            "head",
            "surface_detail",
            "hood_light",
        )
        self.add_part(
            "ACC_HoodCrease.R",
            make_tapered_box((-0.132, -0.164, 1.470), (-0.085, -0.120, 1.655), (0.025, 0.012, 0), (0.032, 0.012, 0)),
            "head",
            "surface_detail",
            "hood_dark",
        )
        self.add_part(
            "ACC_AmberFaceMark",
            make_box((0.055, -0.210, 1.555), (0.058, 0.010, 0.046)),
            "head",
            "face_detail_non_emissive",
            "amber",
        )

        for index, z in enumerate((0.825, 0.980, 1.135), start=1):
            self.add_part(
                f"ACC_CoatButton.{index:02d}",
                make_ellipsoid((0.018, -0.132, z), (0.017, 0.010, 0.017), 8, 4),
                "chest",
                "clothing_detail",
                "button",
            )
        self.add_part(
            "ACC_LeftBootSole",
            make_box((0.112, -0.105, 0.012), (0.215, 0.295, 0.024)),
            "foot.L",
            "footwear_detail",
            "sole",
        )
        self.add_part(
            "ACC_RightBootSole",
            make_box((-0.112, -0.080, 0.010), (0.160, 0.245, 0.020)),
            "foot.R",
            "footwear_detail",
            "sole",
        )

    def build_chair_carrier_body(self) -> None:
        """Build compact workwear around the unchanged canonical A-pose rig."""

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.038, 1.555), (0.105, 0.090, 0.140), 12, 6),
            "head", "body", "skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.325), (0, -0.025, 1.445), 0.072, 0.064),
            "neck", "body", "skin",
        )
        self.add_part(
            "GEO_Torso",
            make_tapered_box((0, 0.010, 0.785), (0, -0.004, 1.335), (0.300, 0.190, 0), (0.360, 0.205, 0)),
            "chest", "body", "work_jacket_dark",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box((0, 0.012, 0.665), (0, 0.010, 0.850), (0.295, 0.185, 0), (0.315, 0.190, 0)),
            "pelvis", "body", "work_jacket",
        )
        limb_points = {
            "L": ((0.208, -0.004, 1.292), (0.470, -0.010, 1.175), (0.680, -0.018, 1.075), (0.755, -0.022, 1.035)),
            "R": ((-0.208, 0.004, 1.292), (-0.470, -0.010, 1.175), (-0.680, -0.018, 1.075), (-0.755, -0.022, 1.035)),
        }
        leg_points = {
            "L": ((0.083, 0.012, 0.750), (0.103, -0.012, 0.354), (0.112, -0.026, 0.095)),
            "R": ((-0.083, -0.004, 0.750), (-0.103, 0.012, 0.354), (-0.112, 0.018, 0.095)),
        }
        for side in ("L", "R"):
            shoulder, elbow, wrist, hand = limb_points[side]
            hip, knee, ankle = leg_points[side]
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(shoulder, elbow, 0.071, 0.057, 12),
                f"upper_arm.{side}", "body", "work_jacket",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(elbow, wrist, 0.059, 0.045, 12),
                f"forearm.{side}", "body", "work_jacket_light" if side == "L" else "work_jacket",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid(tuple((v(wrist) + v(hand)) * 0.5), (0.046, 0.035, 0.060), 10, 5),
                f"hand.{side}", "body", "skin",
            )
            self.add_part(
                f"GEO_Thigh.{side}", make_frustum_between(hip, knee, 0.092, 0.074, 12),
                f"thigh.{side}", "body", "work_trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}", make_frustum_between(knee, ankle, 0.076, 0.060, 12),
                f"shin.{side}", "body", "work_trousers",
            )
            x = 0.112 if side == "L" else -0.112
            self.add_part(
                f"GEO_Foot.{side}",
                make_tapered_box((x, -0.095, 0.0), (x, -0.065, 0.145), (0.170, 0.250, 0), (0.140, 0.185, 0)),
                f"foot.{side}", "body", "shoe",
            )

    def build_chair_carrier_details(self) -> None:
        # A faded waist-length jacket keeps the carrier more compact and
        # upright than the long-coated Lampshade Walker.
        for side, x in (("L", 0.084), ("R", -0.084)):
            self.add_part(
                f"CLO_JacketFront.{side}",
                make_tapered_box((x, -0.112, 0.815), (x * 0.88, -0.113, 1.300), (0.158, 0.036, 0), (0.178, 0.042, 0)),
                "chest", "clothing", "work_jacket_light" if side == "L" else "work_jacket",
            )
            self.add_part(
                f"ACC_ShoulderLoop.{side}",
                make_tapered_box((x * 1.22, -0.125, 0.975), (x * 1.42, -0.112, 1.315), (0.028, 0.018, 0), (0.035, 0.018, 0)),
                "chest", "load_harness", "strap_cloth",
            )
            self.add_part(
                f"ACC_ShoeSole.{side}",
                make_box((0.112 if side == "L" else -0.112, -0.095, 0.011), (0.180, 0.260, 0.022)),
                f"foot.{side}", "footwear_detail", "sole",
            )

        self.add_part(
            "CLO_JacketBack",
            make_tapered_box((0, 0.110, 0.805), (0, 0.112, 1.305), (0.310, 0.040, 0), (0.350, 0.044, 0)),
            "chest", "clothing", "work_jacket_dark",
        )
        self.add_part(
            "CLO_JacketHem",
            make_box((0, -0.010, 0.790), (0.335, 0.215, 0.055)),
            "chest", "clothing_detail", "work_jacket_dark",
        )
        self.add_part(
            "ACC_WorkCap",
            make_tapered_box((0, -0.015, 1.655), (0, -0.010, 1.750), (0.225, 0.205, 0), (0.190, 0.180, 0)),
            "head", "clothing_detail", "work_jacket_dark",
        )
        self.add_part(
            "ACC_CapPeak",
            make_box((0, -0.145, 1.668), (0.175, 0.115, 0.025)),
            "head", "clothing_detail", "work_jacket_dark",
        )
        self.add_part(
            "ACC_FaceShadow",
            make_box((0, -0.130, 1.560), (0.160, 0.020, 0.055)),
            "head", "face_detail", "void",
        )

        # The upside-down cafe chair is tied to the chest: the broad seat is
        # behind the shoulder blades and four narrow legs rise around the head
        # as a clear cage silhouette. Nothing is a separate simulated prop.
        self.add_part(
            "ACC_ChairSeat",
            make_tapered_box((0, 0.245, 1.245), (0, 0.265, 1.335), (0.545, 0.400, 0), (0.500, 0.360, 0)),
            "chest", "signature_silhouette", "chair_wood",
        )
        self.add_part(
            "ACC_ChairSeatWear",
            make_box((0.080, 0.062, 1.305), (0.175, 0.020, 0.050)),
            "chest", "surface_detail", "chair_wear",
        )
        leg_specs = (
            ("Front.L", (0.225, 0.075, 1.315), (0.245, 0.055, 1.735)),
            ("Front.R", (-0.225, 0.075, 1.315), (-0.245, 0.055, 1.735)),
            ("Back.L", (0.225, 0.420, 1.315), (0.285, 0.445, 1.725)),
            ("Back.R", (-0.225, 0.420, 1.315), (-0.285, 0.445, 1.725)),
        )
        for suffix, start, end in leg_specs:
            self.add_part(
                f"ACC_ChairLeg.{suffix}", make_frustum_between(start, end, 0.034, 0.027, 8, 0.88),
                "chest", "signature_silhouette", "chair_wood",
            )
        self.add_part(
            "ACC_ChairCrossbar",
            make_frustum_between((-0.285, 0.445, 1.650), (0.285, 0.445, 1.650), 0.027, 0.027, 8, 0.88),
            "chest", "signature_silhouette", "chair_edge",
        )
        self.add_part(
            "ACC_LoadBelt",
            make_box((0, -0.126, 1.035), (0.350, 0.024, 0.055)),
            "chest", "load_harness", "strap_cloth",
        )

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-city-pedestrian-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = self.spec.design_id
        scene["bp_seed"] = self.spec.seed
        scene["bp_has_own_animations"] = False
        scene["bp_runtime_material"] = "Assets/Player3D/Materials/Player3DLit.mat"


def triangulated_count(mesh: bpy.types.Mesh) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)


def stable_float(value: float) -> float:
    rounded = round(float(value), 6)
    return 0.0 if rounded == -0.0 else rounded


def validate_result(result: BuildResult, archetype: ArchetypeSpec) -> ValidationReport:
    # Parenting and armature setup are data-API operations; force the depsgraph
    # once before reading object matrices for deterministic source bounds.
    bpy.context.view_layer.update()
    errors: list[str] = []
    bones = list(result.rig.data.bones)
    if [bone.name for bone in bones] != [spec.name for spec in SKELETON]:
        errors.append("Generic bone order/names diverge from PlayerCharacter3D")
    for bone_spec in SKELETON:
        bone = result.rig.data.bones.get(bone_spec.name)
        if bone is None:
            continue
        actual_parent = bone.parent.name if bone.parent is not None else None
        if actual_parent != bone_spec.parent:
            errors.append(f"{bone_spec.name} parent is {actual_parent!r}, expected {bone_spec.parent!r}")
        if (bone.head_local - v(bone_spec.head)).length > 0.000001:
            errors.append(f"{bone_spec.name} head diverges from canonical Player A-pose")
        if (bone.tail_local - v(bone_spec.tail)).length > 0.000001:
            errors.append(f"{bone_spec.name} tail diverges from canonical Player A-pose")
        if bone.use_deform != bone_spec.deform:
            errors.append(f"{bone_spec.name} deform flag diverges from canonical Player rig")

    if bpy.data.actions:
        errors.append("Pedestrian model must contain no authored Actions")
    if result.rig.animation_data is not None and result.rig.animation_data.action is not None:
        errors.append("Pedestrian rig has an active animation")

    forbidden_fragments = ("bandage", "shoulderpatch", "satchel")
    mesh_count = len(result.parts)
    triangle_count = 0
    world_vertices: list[Vector] = []
    signature_parts = []
    seen_meshes: set[int] = set()
    for part in sorted(result.parts, key=lambda item: item.obj.name):
        obj = part.obj
        mesh = obj.data
        if any(fragment in obj.name.lower() for fragment in forbidden_fragments):
            errors.append(f"{obj.name} reuses a forbidden player signature detail")
        if mesh.as_pointer() in seen_meshes:
            errors.append(f"{obj.name} reuses another part's mesh datablock")
        seen_meshes.add(mesh.as_pointer())
        if len(mesh.materials) != 1 or mesh.materials[0] != result.material:
            errors.append(f"{obj.name} does not use the one shared source material")
        if len(obj.vertex_groups) != 1 or obj.vertex_groups[0].name != part.bone:
            errors.append(f"{obj.name} must have one rigid group for {part.bone}")
        for vertex in mesh.vertices:
            weights = [group for group in vertex.groups if group.weight > 0.000001]
            if len(weights) != 1 or abs(weights[0].weight - 1.0) > 0.000001:
                errors.append(f"{obj.name} vertex {vertex.index} is not rigidly weighted")
                break
            world_vertices.append(obj.matrix_world @ vertex.co)
        triangles = triangulated_count(mesh)
        triangle_count += triangles
        signature_parts.append(
            {
                "name": obj.name,
                "bone": part.bone,
                "role": part.role,
                "palette_name": part.palette_name,
                "color": [stable_float(component) for component in part.color],
                "vertices": [
                    [stable_float(component) for component in (obj.matrix_world @ vertex.co)]
                    for vertex in mesh.vertices
                ],
                "triangles": triangles,
            }
        )

    min_triangles, max_triangles = archetype.triangle_budget
    if not min_triangles <= triangle_count <= max_triangles:
        errors.append(
            f"Triangle budget is {triangle_count}; expected {min_triangles}-{max_triangles}"
        )
    if mesh_count < 24 or mesh_count > 52:
        errors.append(f"Mesh count is {mesh_count}; expected 24-52 lightweight parts")
    if not world_vertices:
        errors.append("Pedestrian contains no mesh vertices")
        bounds_min = Vector((0, 0, 0))
        bounds_max = Vector((0, 0, 0))
    else:
        bounds_min = Vector(
            tuple(min(vertex[axis] for vertex in world_vertices) for axis in range(3))
        )
        bounds_max = Vector(
            tuple(max(vertex[axis] for vertex in world_vertices) for axis in range(3))
        )
        if abs(bounds_min.z) > 0.00001:
            errors.append(f"Footwear must ground at z=0, got {bounds_min.z:.6f}")
        if abs(bounds_max.z - CANONICAL_HEIGHT) > 0.00001:
            errors.append(
                f"Silhouette must preserve canonical 1.75 m height, got {bounds_max.z:.6f}"
            )
        if bounds_max.x - bounds_min.x > 1.65:
            errors.append("A-pose width unexpectedly exceeds the player's envelope")

    material = result.material
    emission = False
    if material.get("bp_emissive", True):
        emission = True
    if emission:
        errors.append("Shared source material must be explicitly non-emissive")
    if any(obj.type in {"LIGHT", "CAMERA"} for obj in result.export_collection.objects):
        errors.append("Export collection contains a light or camera")

    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": archetype.design_id,
        "seed": archetype.seed,
        "skeleton": [
            {
                "name": spec.name,
                "head": list(spec.head),
                "tail": list(spec.tail),
                "parent": spec.parent,
                "connected": spec.connected,
                "deform": spec.deform,
            }
            for spec in SKELETON
        ],
        "parts": signature_parts,
    }
    signature = hashlib.sha256(
        json.dumps(signature_payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(f"City pedestrian validation failed:\n{formatted}")

    return ValidationReport(
        mesh_count,
        triangle_count,
        tuple(stable_float(component) for component in bounds_min),
        tuple(stable_float(component) for component in bounds_max),
        signature,
    )


def select_export_objects(result: BuildResult) -> None:
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    result.root.select_set(True)
    result.rig.select_set(True)
    for part in result.parts:
        part.obj.select_set(True)
    bpy.context.view_layer.objects.active = result.rig


def export_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    select_export_objects(result)
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


def render_preview(path: Path, result: BuildResult, spec: ArchetypeSpec) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.get("PRESENTATION_CityPedestrian")
    if presentation is None:
        raise RuntimeError("Preview collection is missing")

    camera_data = bpy.data.cameras.new("CAM_PedestrianPreview")
    camera = bpy.data.objects.new("CAM_PedestrianPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = (2.85, -4.60, 2.10) if spec.key == "chair_carrier" else (2.65, -4.40, 2.10)
    target = Vector((0, 0, 0.88))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 56
    scene.camera = camera

    for name, location, energy, color, radius in (
        ("Key", (-2.4, -3.0, 4.2), 900.0, (0.72, 0.82, 0.72), 3.0),
        ("Rim", (2.6, 1.2, 3.2), 650.0, (0.35, 0.48, 0.42), 2.0),
        ("Warm", (-1.0, -1.7, 1.0), 280.0, (0.85, 0.53, 0.25), 1.4),
    ):
        light_data = bpy.data.lights.new(f"LIGHT_{name}", "AREA")
        light_data.energy = energy
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = radius
        light = bpy.data.objects.new(f"LIGHT_{name}", light_data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()

    ground_mesh = bpy.data.meshes.new("PreviewGround_Mesh")
    vertices, faces = make_box((0, 0.35, -0.035), (5.5, 5.5, 0.07))
    ground_mesh.from_pydata(vertices, [], faces)
    ground = bpy.data.objects.new("PreviewGround", ground_mesh)
    presentation.objects.link(ground)
    ground_material = bpy.data.materials.new("MAT_PreviewGround")
    ground_material.diffuse_color = (0.025, 0.040, 0.034, 1)
    ground.data.materials.append(ground_material)

    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def write_manifest(
    path: Path,
    result: BuildResult,
    report: ValidationReport,
    spec: ArchetypeSpec,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-pedestrian-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": spec.design_id,
        "display_name": spec.display_name,
        "seed": spec.seed,
        "height_m": CANONICAL_HEIGHT,
        "pose": "apose",
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "triangle_budget": list(spec.triangle_budget),
        "bounds_min": list(report.bounds_min),
        "bounds_max": list(report.bounds_max),
        "material_asset": "Assets/Player3D/Materials/Player3DLit.mat",
        "emissive": False,
        "colliders": False,
        "animation_count": 0,
        "animations": [],
        "shared_animation_source": ANIMATION_SOURCE,
        "shared_clips": [spec.idle_clip, spec.walk_clip],
        "build_signature": report.build_signature,
        "bones": [
            {
                "name": spec.name,
                "parent": spec.parent or "",
                "head": list(spec.head),
                "tail": list(spec.tail),
                "deform": spec.deform,
            }
            for spec in SKELETON
        ],
        "parts": [
            {
                "name": part.obj.name,
                "role": part.role,
                "bone": part.bone,
                "palette_name": part.palette_name,
                "base_color": [stable_float(component) for component in part.color],
                "vertices": len(part.obj.data.vertices),
                "triangles": triangulated_count(part.obj.data),
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


ACTION_SPECS = (
    ActionSpec(
        "LampshadeIdle", "lampshade_walker_v1", 2.0, 48,
        "persistent C-curve, withdrawn neck, bent knees",
        "weary asymmetric weight shift",
    ),
    ActionSpec(
        "LampshadeWalk", "lampshade_walker_v1", 1.25, 30,
        "persistent C-curve, withdrawn neck, bent knees",
        "short uneven steps, heavy left boot and quick right recovery",
    ),
    ActionSpec(
        "ChairCarrierIdle", "chair_carrier_v1", 1.5, 36,
        "upright load-balanced spine, hands fixed on shoulder loops",
        "small precise weight correction under chair load",
    ),
    ActionSpec(
        "ChairCarrierWalk", "chair_carrier_v1", 1.0, 24,
        "upright load-balanced spine, hands fixed on shoulder loops",
        "high-knee precise heel-led steps with minimal arm swing",
    ),
)


def merge_pose(
    base: dict[str, BonePose],
    *overrides: dict[str, BonePose],
) -> dict[str, BonePose]:
    merged = dict(base)
    for override in overrides:
        merged.update(override)
    return merged


def reset_pose(rig: bpy.types.Object) -> None:
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.location = (0.0, 0.0, 0.0)
        pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        pose_bone.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()


def apply_pose(rig: bpy.types.Object, pose: dict[str, BonePose]) -> None:
    for bone_name, transform in pose.items():
        pose_bone = rig.pose.bones.get(bone_name)
        if pose_bone is None:
            raise ValueError(f"Unknown animation bone {bone_name}")
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.location = transform.location_m
        pose_bone.rotation_quaternion = Euler(
            tuple(math.radians(value) for value in transform.rotation_degrees), "XYZ"
        ).to_quaternion()
        pose_bone.scale = transform.scale
    bpy.context.view_layer.update()


def iter_action_fcurves(action: bpy.types.Action):
    legacy_curves = getattr(action, "fcurves", None)
    if legacy_curves is not None:
        yield from legacy_curves
        return
    for layer in action.layers:
        for strip in layer.strips:
            for channelbag in getattr(strip, "channelbags", ()):
                yield from channelbag.fcurves


def create_action(
    rig: bpy.types.Object,
    spec: ActionSpec,
    keys: Sequence[tuple[float, dict[str, BonePose]]],
) -> bpy.types.Action:
    if not keys or keys[0][0] != 0.0 or keys[-1][0] != 1.0:
        raise ValueError(f"Action {spec.name} must own normalized 0 and 1 endpoints")
    action = bpy.data.actions.new(spec.name)
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start = 0.0
    action.frame_end = float(spec.frame_end)
    action.use_cyclic = True
    action["bp_archetype"] = spec.archetype
    action["bp_duration_seconds"] = spec.duration_seconds
    action["bp_loop"] = True
    action["bp_in_place"] = True
    action["bp_root_motion"] = False
    action["bp_authored_posture"] = spec.authored_posture
    action["bp_gait"] = spec.gait
    action["bp_generator_version"] = GENERATOR_VERSION
    animation_data = rig.animation_data_create()
    animation_data.action = action
    previous_quaternions: dict[str, Quaternion] = {}
    for normalized_time, pose in keys:
        reset_pose(rig)
        apply_pose(rig, pose)
        frame = round(spec.frame_end * normalized_time)
        for bone in rig.pose.bones:
            quaternion = bone.rotation_quaternion.copy()
            previous = previous_quaternions.get(bone.name)
            if previous is not None:
                quaternion.make_compatible(previous)
                bone.rotation_quaternion = quaternion
            previous_quaternions[bone.name] = quaternion.copy()
            group = bone.name.split(".")[0]
            bone.keyframe_insert("location", frame=frame, group=group)
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=group)
            bone.keyframe_insert("scale", frame=frame, group=group)
    for curve in iter_action_fcurves(action):
        for keyframe in curve.keyframe_points:
            keyframe.interpolation = "BEZIER"
            keyframe.handle_left_type = "AUTO_CLAMPED"
            keyframe.handle_right_type = "AUTO_CLAMPED"
    animation_data.action = None
    reset_pose(rig)
    return action


def lampshade_base_pose() -> dict[str, BonePose]:
    return {
        "pelvis": BonePose(rotation_degrees=(10.0, 0.0, -2.0), location_m=(0, 0.025, -0.055)),
        "spine": BonePose(rotation_degrees=(18.0, 0.0, 3.0)),
        "chest": BonePose(rotation_degrees=(13.0, 0.0, -4.0)),
        "neck": BonePose(rotation_degrees=(-15.0, 0.0, 2.0)),
        "head": BonePose(rotation_degrees=(8.0, 0.0, -2.0)),
        "clavicle.L": BonePose(rotation_degrees=(3.0, -4.0, 8.0)),
        "clavicle.R": BonePose(rotation_degrees=(3.0, 4.0, -8.0)),
        "upper_arm.L": BonePose(rotation_degrees=(13.0, 10.0, 26.0)),
        "upper_arm.R": BonePose(rotation_degrees=(11.0, -8.0, -25.0)),
        "forearm.L": BonePose(rotation_degrees=(-18.0, 4.0, -8.0)),
        "forearm.R": BonePose(rotation_degrees=(-15.0, -3.0, 7.0)),
        "thigh.L": BonePose(rotation_degrees=(-9.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(19.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-6.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    }


def chair_base_pose() -> dict[str, BonePose]:
    return {
        "pelvis": BonePose(rotation_degrees=(-1.5, 0.0, 0.0), location_m=(0, 0, -0.015)),
        "spine": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-1.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(1.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(0.0, -4.0, 4.0)),
        "clavicle.R": BonePose(rotation_degrees=(0.0, 4.0, -4.0)),
        "upper_arm.L": BonePose(rotation_degrees=(16.0, 8.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(16.0, -8.0, -30.0)),
        "forearm.L": BonePose(rotation_degrees=(-58.0, 4.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(-58.0, -4.0, 18.0)),
        "hand.L": BonePose(rotation_degrees=(8.0, -5.0, 3.0)),
        "hand.R": BonePose(rotation_degrees=(8.0, 5.0, -3.0)),
        "thigh.L": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
    }


def animation_keys() -> dict[str, tuple[tuple[float, dict[str, BonePose]], ...]]:
    lampshade = lampshade_base_pose()
    chair = chair_base_pose()
    lamp_idle_left = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(11.0, 1.0, -4.0), location_m=(0, 0.025, -0.058)),
        "spine": BonePose(rotation_degrees=(19.5, 0.0, 4.5)),
        "chest": BonePose(rotation_degrees=(14.0, 0.0, -5.5)),
        "head": BonePose(rotation_degrees=(9.0, 0.0, -3.0)),
    })
    lamp_idle_right = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(9.0, -1.0, 1.0), location_m=(0, 0.025, -0.052)),
        "spine": BonePose(rotation_degrees=(17.0, 0.0, 1.5)),
        "chest": BonePose(rotation_degrees=(12.0, 0.0, -1.5)),
        "head": BonePose(rotation_degrees=(7.0, 0.0, 0.5)),
    })
    lamp_left_contact = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(12.0, 4.0, -4.5), location_m=(0, 0.025, -0.075)),
        "thigh.L": BonePose(rotation_degrees=(-22.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(13.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(32.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(15.0, -2.0, -5.0)),
    })
    lamp_right_pass = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(9.0, -1.0, -0.5), location_m=(0, 0.025, -0.045)),
        "thigh.L": BonePose(rotation_degrees=(3.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-11.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(48.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(13.0, 0.0, 0.0)),
    })
    lamp_right_contact = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(9.0, -2.0, 2.5), location_m=(0, 0.025, -0.050)),
        "thigh.L": BonePose(rotation_degrees=(13.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(40.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-16.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
    })
    lamp_left_drag = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(13.0, 2.0, -1.5), location_m=(0, 0.025, -0.082)),
        "thigh.L": BonePose(rotation_degrees=(-8.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(34.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(5.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    })
    chair_idle_left = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-1.0, 1.0, -1.5), location_m=(0, 0, -0.018)),
        "chest": BonePose(rotation_degrees=(2.0, -0.5, 1.2)),
        "head": BonePose(rotation_degrees=(0.5, 0.0, -0.7)),
    })
    chair_idle_right = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-2.0, -1.0, 1.5), location_m=(0, 0, -0.012)),
        "chest": BonePose(rotation_degrees=(4.0, 0.5, -1.2)),
        "head": BonePose(rotation_degrees=(1.5, 0.0, 0.7)),
    })
    chair_left_contact = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-1.0, 2.0, -1.0), location_m=(0, 0, -0.028)),
        "thigh.L": BonePose(rotation_degrees=(-32.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(4.0, -1.0, 1.0)),
    })
    chair_right_pass = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-2.0, -1.0, 0.5), location_m=(0, 0, 0.004)),
        "thigh.L": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-25.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(60.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
    })
    chair_right_contact = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-1.0, -2.0, 1.0), location_m=(0, 0, -0.028)),
        "thigh.L": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-32.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(4.0, 1.0, -1.0)),
    })
    chair_left_pass = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-2.0, 1.0, -0.5), location_m=(0, 0, 0.004)),
        "thigh.L": BonePose(rotation_degrees=(-25.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(60.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
    })
    return {
        "LampshadeIdle": ((0.0, lampshade), (0.25, lamp_idle_left), (0.5, lampshade), (0.75, lamp_idle_right), (1.0, lampshade)),
        "LampshadeWalk": ((0.0, lamp_left_contact), (0.25, lamp_right_pass), (0.5, lamp_right_contact), (0.75, lamp_left_drag), (1.0, lamp_left_contact)),
        "ChairCarrierIdle": ((0.0, chair), (0.25, chair_idle_left), (0.5, chair), (0.75, chair_idle_right), (1.0, chair)),
        "ChairCarrierWalk": ((0.0, chair_left_contact), (0.25, chair_right_pass), (0.5, chair_right_contact), (0.75, chair_left_pass), (1.0, chair_left_contact)),
    }


def validate_animation_library(
    rig: bpy.types.Object,
    actions: dict[str, bpy.types.Action],
    grounding: dict[str, dict[str, object]],
) -> tuple[str, list[dict]]:
    errors: list[str] = []
    manifest_clips: list[dict] = []
    if [bone.name for bone in rig.data.bones] != [spec.name for spec in SKELETON]:
        errors.append("Animation rig bone order/names diverge from canonical rig")
    for spec in ACTION_SPECS:
        action = actions.get(spec.name)
        if action is None:
            errors.append(f"Missing Action {spec.name}")
            continue
        curves = list(iter_action_fcurves(action))
        keyed_names = {
            curve.data_path.split('pose.bones["', 1)[1].split('"]', 1)[0]
            for curve in curves
            if curve.data_path.startswith('pose.bones["')
        }
        loop_error = max(
            (abs(curve.evaluate(0.0) - curve.evaluate(spec.frame_end)) for curve in curves),
            default=0.0,
        )
        root_curves = [
            curve for curve in curves
            if curve.data_path == 'pose.bones["root"].location'
        ]
        root_ranges = []
        for axis in range(3):
            curve = next((item for item in root_curves if item.array_index == axis), None)
            values = [curve.evaluate(frame) for frame in range(spec.frame_end + 1)] if curve else [0.0]
            root_ranges.append(stable_float(max(values) - min(values)))
        if len(keyed_names) != len(SKELETON):
            errors.append(f"{spec.name} keys {len(keyed_names)} bones, expected {len(SKELETON)}")
        if loop_error > 0.0001:
            errors.append(f"{spec.name} loop error is {loop_error:.7f}")
        if any(value > 0.000001 for value in root_ranges):
            errors.append(f"{spec.name} root translation is not in-place: {root_ranges}")
        if bool(action.get("bp_root_motion", True)):
            errors.append(f"{spec.name} does not disable root motion")
        clip_payload = {
            "name": spec.name,
            "archetype": spec.archetype,
            "duration_seconds": spec.duration_seconds,
            "frame_start": 0,
            "frame_end": spec.frame_end,
            "loop": True,
            "in_place": True,
            "authored_posture": spec.authored_posture,
            "gait": spec.gait,
            "keyed_bone_count": len(keyed_names),
            "loop_max_error": stable_float(loop_error),
            "root_translation_range_m": root_ranges,
        }
        clip_payload.update(grounding.get(spec.name, {}))
        manifest_clips.append(clip_payload)
    if errors:
        raise RuntimeError("Pedestrian animation validation failed:\n" + "\n".join(f"  - {item}" for item in errors))
    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "fps": ANIMATION_FPS,
        "bones": [bone.name for bone in SKELETON],
        "clips": manifest_clips,
    }
    signature = hashlib.sha256(
        json.dumps(signature_payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    return signature, manifest_clips


def evaluated_part_min_z(part: PartRecord, depsgraph) -> float:
    evaluated = part.obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        return min(
            (evaluated.matrix_world @ vertex.co).z
            for vertex in mesh.vertices
        )
    finally:
        evaluated.to_mesh_clear()


def validate_animated_grounding(
    result: BuildResult,
    actions: dict[str, bpy.types.Action],
) -> dict[str, dict[str, object]]:
    """Sample every frame against each model's real deformed footwear."""

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    footwear = {
        side: [part for part in result.parts if part.bone == f"foot.{side}"]
        for side in ("L", "R")
    }
    if any(not parts for parts in footwear.values()):
        raise RuntimeError("Grounding validation needs geometry on both foot bones")
    reports: dict[str, dict[str, object]] = {}
    for action_name, action in actions.items():
        animation_data.action = action
        contact_gaps: list[float] = []
        lowest_samples: list[float] = []
        for frame in range(round(action.frame_start), round(action.frame_end) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            foot_minima = [
                min(evaluated_part_min_z(part, depsgraph) for part in footwear[side])
                for side in ("L", "R")
            ]
            lowest_samples.append(min(foot_minima))
            contact_gaps.append(min(abs(value) for value in foot_minima))
        lowest = min(lowest_samples)
        highest_contact_gap = max(contact_gaps)
        # A baked pelvis correction keeps at least one rigid sole on the
        # pavement at every exported sample without moving the gameplay root.
        if lowest < -0.002:
            raise RuntimeError(
                f"{action_name} footwear penetrates ground at {lowest:.4f} m"
            )
        if highest_contact_gap > 0.002:
            raise RuntimeError(
                f"{action_name} loses grounded contact by {highest_contact_gap:.4f} m"
            )
        reports[action_name] = {
            "ground_min_m": stable_float(lowest),
            "ground_max_contact_gap_m": stable_float(highest_contact_gap),
        }
    animation_data.action = None
    scene.frame_set(0)
    reset_pose(rig)
    return reports


def bake_grounded_pelvis(
    result: BuildResult,
    actions: dict[str, bpy.types.Action],
) -> None:
    """Bake per-frame pelvis lift so the lower sole touches z=0 exactly."""

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    footwear = [part for part in result.parts if part.bone in {"foot.L", "foot.R"}]
    if not footwear:
        raise RuntimeError("Grounding bake needs footwear geometry")
    for action in actions.values():
        animation_data.action = action
        corrections: list[tuple[int, float]] = []
        for frame in range(round(action.frame_start), round(action.frame_end) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            lowest = min(evaluated_part_min_z(part, depsgraph) for part in footwear)
            corrections.append((frame, -lowest))
        for frame, correction in corrections:
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            pelvis = rig.pose.bones["pelvis"]
            pose_matrix = pelvis.matrix.copy()
            pose_matrix.translation.z += correction
            pelvis.matrix = pose_matrix
            pelvis.keyframe_insert("location", frame=frame, group="pelvis")
        for curve in iter_action_fcurves(action):
            if curve.data_path == 'pose.bones["pelvis"].location':
                for keyframe in curve.keyframe_points:
                    keyframe.interpolation = "LINEAR"
        # Changing a key at frame N can alter the evaluated basis originally
        # sampled at later frames, so perform one deterministic residual pass.
        for frame in range(round(action.frame_start), round(action.frame_end) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            lowest = min(evaluated_part_min_z(part, depsgraph) for part in footwear)
            pose_matrix = rig.pose.bones["pelvis"].matrix.copy()
            pose_matrix.translation.z -= lowest
            rig.pose.bones["pelvis"].matrix = pose_matrix
            rig.pose.bones["pelvis"].keyframe_insert("location", frame=frame, group="pelvis")
    animation_data.action = None
    scene.frame_set(0)
    reset_pose(rig)


def export_animation_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    result.root.select_set(True)
    result.rig.select_set(True)
    bpy.context.view_layer.objects.active = result.rig
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"EMPTY", "ARMATURE"},
        axis_forward="-Z", axis_up="Y", add_leaf_bones=False, bake_anim=True,
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        use_armature_deform_only=False, use_custom_props=True,
    )


def write_animation_manifest(path: Path, signature: str, clips: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-pedestrian-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "skeleton_source": "PlayerCharacter3D exact A-pose v2.5.0",
        "bone_count": len(SKELETON),
        "fps": ANIMATION_FPS,
        "root_motion": False,
        "mesh_count": 0,
        "clip_count": len(clips),
        "clips": clips,
        "build_signature": signature,
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def setup_review_stage(result: BuildResult) -> tuple[bpy.types.Object, bpy.types.Object]:
    scene = bpy.context.scene
    presentation = bpy.data.collections.get("PRESENTATION_CityPedestrian")
    if presentation is None:
        raise RuntimeError("Animation review presentation collection is missing")
    camera_data = bpy.data.cameras.new("CAM_LocomotionReview")
    camera = bpy.data.objects.new("CAM_LocomotionReview", camera_data)
    presentation.objects.link(camera)
    camera.location = (2.45, -4.55, 1.85)
    camera.rotation_euler = (Vector((0, 0, 0.90)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 62
    scene.camera = camera
    for name, location, energy, color, radius in (
        ("ReviewKey", (-2.2, -3.2, 4.0), 850.0, (0.72, 0.82, 0.72), 3.0),
        ("ReviewRim", (2.5, 1.0, 3.0), 500.0, (0.35, 0.48, 0.42), 2.0),
    ):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = radius
        light = bpy.data.objects.new(name, data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (Vector((0, 0, 0.90)) - light.location).to_track_quat("-Z", "Y").to_euler()
    vertices, faces = make_box((0, 0.25, -0.035), (4.0, 4.0, 0.07))
    ground_mesh = bpy.data.meshes.new("ReviewGround_Mesh")
    ground_mesh.from_pydata(vertices, [], faces)
    ground = bpy.data.objects.new("ReviewGround", ground_mesh)
    presentation.objects.link(ground)
    material = bpy.data.materials.new("MAT_ReviewGround")
    material.diffuse_color = (0.025, 0.040, 0.034, 1)
    ground.data.materials.append(material)
    scene.render.resolution_x = 320
    scene.render.resolution_y = 400
    scene.render.resolution_percentage = 100
    return camera, ground


def render_animation_contact_sheet(
    path: Path,
    source_dir: Path,
) -> None:
    """Render idle plus two opposite walk phases for both archetypes."""

    path.parent.mkdir(parents=True, exist_ok=True)
    tiles: list[Path] = []
    samples = (
        ("lampshade", "LampshadeIdle", 0),
        ("lampshade", "LampshadeWalk", 0),
        ("lampshade", "LampshadeWalk", 15),
        ("chair_carrier", "ChairCarrierIdle", 0),
        ("chair_carrier", "ChairCarrierWalk", 0),
        ("chair_carrier", "ChairCarrierWalk", 12),
    )
    for index, (archetype_key, action_name, frame) in enumerate(samples):
        # Rebuilding swaps the actual production meshes while the action poses
        # are re-authored deterministically from the same source definitions.
        result = PedestrianBuilder(ARCHETYPES[archetype_key]).build()
        local_actions = {
            spec.name: create_action(result.rig, spec, animation_keys()[spec.name])
            for spec in ACTION_SPECS
        }
        bake_grounded_pelvis(result, local_actions)
        setup_review_stage(result)
        result.rig.animation_data_create().action = local_actions[action_name]
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        tile = source_dir / f".locomotion-review-{index}.png"
        bpy.context.scene.render.filepath = str(tile)
        bpy.ops.render.render(write_still=True)
        tiles.append(tile)

    sheet = bpy.data.images.new("CityPedestrianLocomotionContactSheet", 960, 800)
    pixels = [0.008, 0.012, 0.010, 1.0] * (960 * 800)
    for index, tile in enumerate(tiles):
        tile_image = bpy.data.images.load(str(tile), check_existing=False)
        tile_pixels = list(tile_image.pixels)
        column = index % 3
        row = index // 3
        destination_y = (1 - row) * 400
        for y in range(400):
            source_start = y * 320 * 4
            destination_start = ((destination_y + y) * 960 + column * 320) * 4
            pixels[destination_start : destination_start + 320 * 4] = tile_pixels[source_start : source_start + 320 * 4]
        bpy.data.images.remove(tile_image)
    sheet.pixels = pixels
    sheet.filepath_raw = str(path)
    sheet.file_format = "PNG"
    sheet.save()
    for tile in tiles:
        try:
            tile.unlink()
        except FileNotFoundError:
            pass


def build_animation_library(config: argparse.Namespace) -> None:
    # Reuse a freshly validated canonical rig, remove every model mesh, then
    # author and export only ROOT_Player + RIG_Player + bone Actions.
    result = PedestrianBuilder(ARCHETYPES["lampshade"]).build()
    model_parts = list(result.parts)
    keys = animation_keys()
    actions = {
        spec.name: create_action(result.rig, spec, keys[spec.name])
        for spec in ACTION_SPECS
    }
    bake_grounded_pelvis(result, actions)
    grounding = validate_animated_grounding(result, actions)
    for part in model_parts:
        bpy.data.objects.remove(part.obj, do_unlink=True)
    result.parts.clear()
    result.root["bp_design_id"] = "city_pedestrian_locomotion_v1"
    result.root["bp_animation_only"] = True
    result.root["bp_root_motion"] = False
    signature, clips = validate_animation_library(result.rig, actions, grounding)
    fbx_path = config.animation_dir / "CityPedestrianLocomotion.fbx"
    manifest_path = config.animation_dir / "CityPedestrianLocomotion.json"
    blend_path = config.source_dir / "CityPedestrianLocomotion.blend"
    export_animation_fbx(fbx_path, result)
    write_animation_manifest(manifest_path, signature, clips)
    save_blend(blend_path)
    if not config.no_preview:
        render_animation_contact_sheet(
            config.source_dir / "CityPedestrianLocomotionContactSheet.png",
            config.source_dir,
        )
    print(f"  locomotion: {len(actions)} Actions, 31 keyed bones, no meshes/root motion")
    print(f"    Signature: {signature}")
    print(f"    FBX: {fbx_path}")


def main() -> None:
    config = parse_args()
    selected = (
        tuple(ARCHETYPES.values())
        if config.archetype == "all"
        else (ARCHETYPES[config.archetype],)
    )
    print("CITY PEDESTRIAN ART BUILD")
    print(f"  Blender: {bpy.app.version_string}")
    reports: list[tuple[ArchetypeSpec, ValidationReport]] = []
    for spec in selected:
        result = PedestrianBuilder(spec).build()
        report = validate_result(result, spec)
        blend_path = config.source_dir / spec.blend_name
        fbx_path = config.model_dir / f"{spec.model_name}.fbx"
        manifest_path = config.model_dir / f"{spec.model_name}.json"
        preview_path = config.source_dir / spec.preview_name
        if not config.no_preview:
            render_preview(preview_path, result, spec)
        export_fbx(fbx_path, result)
        write_manifest(manifest_path, result, report, spec)
        save_blend(blend_path)
        reports.append((spec, report))
        print(f"  {spec.design_id}: {report.mesh_count} meshes, {report.triangle_count} triangles")
        print(f"    Signature: {report.build_signature}")
        print(f"    Blend: {blend_path}")
        print(f"    FBX: {fbx_path}")
    if config.archetype == "all":
        build_animation_library(config)
        first_signatures = {
            spec.design_id: report.build_signature for spec, report in reports
        }
        # A second model-only build proves that source geometry and manifests
        # remain deterministic within the same Blender process.
        for spec, first_report in reports:
            rerun = PedestrianBuilder(spec).build()
            rerun_report = validate_result(rerun, spec)
            if rerun_report.build_signature != first_report.build_signature:
                raise RuntimeError(
                    f"Non-deterministic build signature for {spec.design_id}: "
                    f"{first_signatures[spec.design_id]} != {rerun_report.build_signature}"
                )
        print("  Determinism: repeated model signatures match")
    print("CITY PEDESTRIAN ART BUILD OK")


if __name__ == "__main__":
    main()
