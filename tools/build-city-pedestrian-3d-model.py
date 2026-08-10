#!/usr/bin/env python3
"""Build the deterministic low-poly City pedestrian "Lampshade Walker".

Run this with Blender, not CPython:

    blender --background --python tools/build-city-pedestrian-3d-model.py

The generator owns the editable .blend, the animation-free production FBX,
the Unity manifest and a review render.  The pedestrian deliberately carries
the exact Generic skeleton names, parent hierarchy and A-pose rest transforms
of PlayerCharacter3D so Unity can apply the player's shared Idle and Walk
clips without retargeting or duplicated animation data.

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
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "lampshade_walker_v1"
SEED = 190417
CANONICAL_HEIGHT = 1.75
MIN_TRIANGLES = 800
MAX_TRIANGLES = 1200
SHARED_MATERIAL_NAME = "MAT_Player3DLit"


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
        "--output",
        type=Path,
        default=Path("ArtSource/Pedestrians/Blender/CityPedestrian3D.blend"),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path("Assets/Pedestrians/Models/CityPedestrian3D.fbx"),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("Assets/Pedestrians/Models/CityPedestrian3D.json"),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path("ArtSource/Pedestrians/Blender/CityPedestrian3D.png"),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    config = parser.parse_args(arguments)
    # Blender resolves a relative render path against the unsaved startup
    # blend's `//` root (often the drive root), unlike Python file writes.
    # Resolve every output from the invocation cwd before touching Blender IO.
    for field_name in ("output", "fbx", "manifest", "preview"):
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
    def __init__(self):
        self.result: BuildResult | None = None

    def build(self) -> BuildResult:
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        pedestrian = bpy.data.collections.new("BP_CityPedestrian3D")
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
        root["bp_design_id"] = DESIGN_ID
        root["bp_seed"] = SEED
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_shared_animation_source"] = (
            "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx"
        )

        rig = self.create_armature(export_collection, root)
        self.result = BuildResult(root, rig, export_collection, material)
        self.build_body()
        self.build_clothing_and_details()
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

    @staticmethod
    def configure_scene_metadata() -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-city-pedestrian-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = DESIGN_ID
        scene["bp_seed"] = SEED
        scene["bp_has_own_animations"] = False
        scene["bp_runtime_material"] = "Assets/Player3D/Materials/Player3DLit.mat"


def triangulated_count(mesh: bpy.types.Mesh) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)


def stable_float(value: float) -> float:
    rounded = round(float(value), 6)
    return 0.0 if rounded == -0.0 else rounded


def validate_result(result: BuildResult) -> ValidationReport:
    # Parenting and armature setup are data-API operations; force the depsgraph
    # once before reading object matrices for deterministic source bounds.
    bpy.context.view_layer.update()
    errors: list[str] = []
    bones = list(result.rig.data.bones)
    if [bone.name for bone in bones] != [spec.name for spec in SKELETON]:
        errors.append("Generic bone order/names diverge from PlayerCharacter3D")
    for spec in SKELETON:
        bone = result.rig.data.bones.get(spec.name)
        if bone is None:
            continue
        actual_parent = bone.parent.name if bone.parent is not None else None
        if actual_parent != spec.parent:
            errors.append(f"{spec.name} parent is {actual_parent!r}, expected {spec.parent!r}")
        if (bone.head_local - v(spec.head)).length > 0.000001:
            errors.append(f"{spec.name} head diverges from canonical Player A-pose")
        if (bone.tail_local - v(spec.tail)).length > 0.000001:
            errors.append(f"{spec.name} tail diverges from canonical Player A-pose")
        if bone.use_deform != spec.deform:
            errors.append(f"{spec.name} deform flag diverges from canonical Player rig")

    if bpy.data.actions:
        errors.append("Pedestrian model must contain no authored Actions")
    if result.rig.animation_data is not None and result.rig.animation_data.action is not None:
        errors.append("Pedestrian rig has an active animation")

    forbidden_fragments = ("bandage", "shoulderpatch", "satchel", "strap")
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

    if not MIN_TRIANGLES <= triangle_count <= MAX_TRIANGLES:
        errors.append(
            f"Triangle budget is {triangle_count}; expected {MIN_TRIANGLES}-{MAX_TRIANGLES}"
        )
    if mesh_count < 24 or mesh_count > 48:
        errors.append(f"Mesh count is {mesh_count}; expected 24-48 lightweight parts")
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
                f"Hood must preserve canonical 1.75 m height, got {bounds_max.z:.6f}"
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


def render_preview(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.get("PRESENTATION_CityPedestrian")
    if presentation is None:
        raise RuntimeError("Preview collection is missing")

    camera_data = bpy.data.cameras.new("CAM_PedestrianPreview")
    camera = bpy.data.objects.new("CAM_PedestrianPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = (2.65, -4.40, 2.10)
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
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-pedestrian-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": "Lampshade Walker",
        "seed": SEED,
        "height_m": CANONICAL_HEIGHT,
        "pose": "apose",
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "bounds_min": list(report.bounds_min),
        "bounds_max": list(report.bounds_max),
        "material_asset": "Assets/Player3D/Materials/Player3DLit.mat",
        "emissive": False,
        "colliders": False,
        "animation_count": 0,
        "animations": [],
        "shared_animation_source": "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx",
        "shared_clips": ["Idle", "Walk"],
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


def main() -> None:
    config = parse_args()
    result = PedestrianBuilder().build()
    report = validate_result(result)
    if not config.no_preview:
        render_preview(config.preview, result)
    export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    save_blend(config.output)
    print("CITY PEDESTRIAN 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(f"  Skeleton bones: {len(SKELETON)} (exact Player Generic hierarchy)")
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
