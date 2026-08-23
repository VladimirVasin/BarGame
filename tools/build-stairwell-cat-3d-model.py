#!/usr/bin/env python3
"""Build the deterministic low-poly Cheshire stairwell cat.

Run this with Blender, not CPython:

    blender --background --factory-startup --python \
        tools/build-stairwell-cat-3d-model.py

The cat is the last sprite conversion: a sitting, near-black shaggy
cat perched on the middle-landing back rail, seen mostly from behind
by the fixed MiddleFlight camera. It carries no armature at all - the
first character without the 31-bone Player rig. Articulation is pure
pivot empties (PIVOT_Chest breathing, PIVOT_Head tracking and the
over-shoulder grin turn, PIVOT_Ear.L/R twitches, PIVOT_Tail.01..03
flicks), exported flat beside the meshes exactly like the wheelchair
mechanism: every pivot-bound mesh has its origin ON its pivot so the
runtime adopt is exact.

Its trickster signature is ACC_Grin: a comically wide crescent of
teeth, wider than the head itself, floating just in front of the
muzzle. The mesh bakes normalized arc length into UV x (0 at the left
tip, 1 at the right, 0.5 at the smile's center) so the runtime shader
can draw the smile in from the middle outward. The renderer ships
disabled; by default the grin does not exist.

Blender source space is metres, Z-up, forward -Y and anatomical left +X.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import os
from dataclasses import dataclass, field
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
DESIGN_ID = "cheshire_stairwell_cat_v1"
DISPLAY_NAME = "Cheshire Stairwell Cat"
# The idle model's deterministic seed; the model and its timeline are kin.
SEED = 0x0C47
MIN_TRIANGLES = 400
MAX_TRIANGLES = 1600
SHARED_MATERIAL_ASSET = "Assets/Player3D/Materials/Player3DLit.mat"
GRIN_MATERIAL_ASSET = "Assets/Resources/Materials/StairwellCatGrin.mat"

# The perch contract: the back rail is 0.10 m deep and the cat's origin
# sits on its top face. Ground-contact geometry must stay near the rail;
# the haunches may overhang the back and the toes the front, as a real
# cat's do.
RAIL_DEPTH = 0.10
MAX_CONTACT_FORWARD = -0.10   # toes may reach this far past the origin (-Y)
MAX_CONTACT_BACK = 0.05       # nothing grounded behind the rail's rear edge

PIVOT_CHEST = "PIVOT_Chest"
PIVOT_HEAD = "PIVOT_Head"
PIVOT_EAR_L = "PIVOT_Ear.L"
PIVOT_EAR_R = "PIVOT_Ear.R"
PIVOT_TAIL = ("PIVOT_Tail.01", "PIVOT_Tail.02", "PIVOT_Tail.03")
ANCHOR_MUZZLE = "ANCHOR_Muzzle"

PIVOT_LOCATIONS = {
    PIVOT_CHEST: (0.0, -0.02, 0.12),
    PIVOT_HEAD: (0.0, -0.035, 0.38),
    PIVOT_EAR_L: (0.05, -0.045, 0.515),
    PIVOT_EAR_R: (-0.05, -0.045, 0.515),
    PIVOT_TAIL[0]: (0.07, 0.075, 0.10),
    PIVOT_TAIL[1]: (0.09, 0.10, -0.05),
    PIVOT_TAIL[2]: (0.10, 0.105, -0.20),
}
ANCHOR_LOCATIONS = {
    ANCHOR_MUZZLE: (0.0, -0.13, 0.44),
}

HEAD_CENTER = (0.0, -0.06, 0.46)
HEAD_RADII = (0.085, 0.075, 0.070)
HEAD_WIDTH = HEAD_RADII[0] * 2.0

# The joke measured: the grin chord is wider than the whole head.
GRIN_TIP_X = 0.15
GRIN_WIDTH = GRIN_TIP_X * 2.0
GRIN_TIP_Z = 0.47
GRIN_CENTER_Z = 0.40
GRIN_FRONT_Y = -0.155
GRIN_THICKNESS = 0.02
GRIN_BAND_HALF = 0.024
GRIN_SEGMENTS = 24
GRIN_TOOTH_COUNT = 9
GRIN_UV_ARC = "arclength_u_v1"

PALETTE = {
    "fur": (0.052, 0.054, 0.050, 1.0),
    "fur_light": (0.085, 0.088, 0.082, 1.0),
    "fur_dark": (0.033, 0.034, 0.032, 1.0),
    "fur_tail": (0.040, 0.042, 0.039, 1.0),
    "paw": (0.060, 0.062, 0.058, 1.0),
    "eye_green": (0.30, 0.55, 0.24, 1.0),
    "grin_teeth": (0.92, 0.90, 0.80, 1.0),
}


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
            f"Could not load shared mesh helpers from {source_path}"
        )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


base = load_character_build_base()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(
            "ArtSource/Stairwell/Cat/Blender/StairwellCat3D.blend"
        ),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path("Assets/Stairwell/Cat/Models/StairwellCat3D.fbx"),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("Assets/Stairwell/Cat/Models/StairwellCat3D.json"),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path(
            "ArtSource/Stairwell/Cat/Blender/StairwellCat3D.png"
        ),
    )
    parser.add_argument(
        "--face-preview",
        type=Path,
        default=Path(
            "ArtSource/Stairwell/Cat/Blender/StairwellCat3D-face.png"
        ),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = (
        sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    )
    config = parser.parse_args(arguments)
    for field_name in (
        "output", "fbx", "manifest", "preview", "face_preview"
    ):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


@dataclass
class CatPart:
    obj: bpy.types.Object
    pivot: str  # empty string for a static part
    role: str
    palette_name: str
    color: tuple[float, float, float, float]


@dataclass
class CatBuildResult:
    root: bpy.types.Object
    export_collection: bpy.types.Collection
    body_material: bpy.types.Material
    grin_material: bpy.types.Material
    parts: list[CatPart] = field(default_factory=list)
    pivots: dict[str, bpy.types.Object] = field(default_factory=dict)
    anchors: dict[str, bpy.types.Object] = field(default_factory=dict)


@dataclass(frozen=True)
class CatValidationReport:
    mesh_count: int
    triangle_count: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]
    build_signature: str


def grin_geometry():
    """The crescent-of-teeth band with its arc-length UV contract.

    Returns (vertices, faces, loops_uv) where loops_uv maps a face's
    loop order to (u, v) pairs, matching faces one-to-one.
    """

    half_chord = GRIN_TIP_X
    sagitta = GRIN_TIP_Z - GRIN_CENTER_Z
    radius = (half_chord * half_chord + sagitta * sagitta) / (
        2.0 * sagitta
    )
    center_z = GRIN_CENTER_Z + radius
    tip_angle = math.atan2(GRIN_TIP_Z - center_z, half_chord)
    # Sweep from the left tip through the bottom of the smile to the
    # right tip; atan2 keeps tip_angle negative, so the mirrored left
    # tip sits below -pi/2:
    left_angle = -math.pi - tip_angle

    upper_radius = radius - GRIN_BAND_HALF
    lower_radius = radius + GRIN_BAND_HALF
    back_y = GRIN_FRONT_Y + GRIN_THICKNESS

    samples = GRIN_SEGMENTS + 1
    rail_points: list[tuple[float, float, float, float]] = []
    for index in range(samples):
        u = index / GRIN_SEGMENTS
        angle = left_angle + (tip_angle - left_angle) * u
        x = math.cos(angle)
        z = math.sin(angle)
        rail_points.append((u, x, z, angle))

    vertices: list[tuple[float, float, float]] = []
    uv_by_vertex: list[tuple[float, float]] = []

    def add_vertex(x, y, z, u, v_coord):
        vertices.append((x, y, z))
        uv_by_vertex.append((u, v_coord))
        return len(vertices) - 1

    front_lower = []
    front_upper = []
    back_lower = []
    back_upper = []
    for u, x_dir, z_dir, _ in rail_points:
        lower = (
            lower_radius * x_dir,
            center_z + lower_radius * z_dir,
        )
        upper = (
            upper_radius * x_dir,
            center_z + upper_radius * z_dir,
        )
        front_lower.append(
            add_vertex(lower[0], GRIN_FRONT_Y, lower[1], u, 0.0)
        )
        front_upper.append(
            add_vertex(upper[0], GRIN_FRONT_Y, upper[1], u, 1.0)
        )
        back_lower.append(
            add_vertex(lower[0], back_y, lower[1], u, 0.0)
        )
        back_upper.append(
            add_vertex(upper[0], back_y, upper[1], u, 1.0)
        )

    faces: list[tuple[int, ...]] = []
    for index in range(GRIN_SEGMENTS):
        fl0, fl1 = front_lower[index], front_lower[index + 1]
        fu0, fu1 = front_upper[index], front_upper[index + 1]
        bl0, bl1 = back_lower[index], back_lower[index + 1]
        bu0, bu1 = back_upper[index], back_upper[index + 1]
        faces.append((fl0, fl1, fu1, fu0))  # front, normal -Y
        faces.append((bu0, bu1, bl1, bl0))  # back, normal +Y
        faces.append((fu0, fu1, bu1, bu0))  # top edge, normal +Z-ish
        faces.append((fl1, fl0, bl0, bl1))  # bottom edge, normal -Z-ish
    faces.append(
        (front_lower[0], front_upper[0], back_upper[0], back_lower[0])
    )  # left tip cap, normal -X
    last = GRIN_SEGMENTS
    faces.append(
        (
            front_lower[last],
            back_lower[last],
            back_upper[last],
            front_upper[last],
        )
    )  # right tip cap, normal +X

    return vertices, faces, uv_by_vertex


class StairwellCatBuilder:
    def __init__(self):
        self.result: CatBuildResult | None = None

    def build(self) -> CatBuildResult:
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        cat = bpy.data.collections.new("BP_StairwellCat3D")
        scene_root.children.link(cat)
        export_collection = bpy.data.collections.new(
            "EXPORT_StairwellCat"
        )
        cat.children.link(export_collection)
        presentation = bpy.data.collections.new(
            "PRESENTATION_StairwellCat"
        )
        cat.children.link(presentation)

        body_material = self.create_body_material()
        grin_material = self.create_grin_material()

        root = bpy.data.objects.new("ROOT_StairwellCat", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = "tools/build-stairwell-cat-3d-model.py"
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design_id"] = DESIGN_ID
        root["bp_seed"] = SEED
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_has_own_animations"] = False
        root["bp_armature"] = False

        self.result = CatBuildResult(
            root, export_collection, body_material, grin_material
        )
        for name, location in PIVOT_LOCATIONS.items():
            self.create_empty(name, location, self.result.pivots)
        for name, location in ANCHOR_LOCATIONS.items():
            self.create_empty(name, location, self.result.anchors)

        self.build_body()
        self.build_head()
        self.build_tail()
        self.build_grin()
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
        scene.world = bpy.data.worlds.new("WORLD_StairwellCatPreview")
        scene.world.use_nodes = True
        background = scene.world.node_tree.nodes.get("Background")
        if background is not None:
            background.inputs["Color"].default_value = (
                0.010, 0.016, 0.014, 1
            )
            background.inputs["Strength"].default_value = 0.18

    @staticmethod
    def create_body_material() -> bpy.types.Material:
        material = bpy.data.materials.new("MAT_StairwellCatBody")
        material.use_nodes = True
        material.diffuse_color = PALETTE["fur"]
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        object_info = nodes.new("ShaderNodeObjectInfo")
        shader.inputs["Roughness"].default_value = 0.92
        shader.inputs["Metallic"].default_value = 0.0
        emission = (
            shader.inputs.get("Emission Color")
            or shader.inputs.get("Emission")
        )
        if emission is not None:
            emission.default_value = (0, 0, 0, 1)
        links.new(
            object_info.outputs["Color"], shader.inputs["Base Color"]
        )
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])
        material["bp_runtime_material"] = SHARED_MATERIAL_ASSET
        material["bp_emissive"] = False
        return material

    @staticmethod
    def create_grin_material() -> bpy.types.Material:
        # Preview-only glow; Unity swaps this slot for the dedicated
        # arc-reveal shader material at prefab-build time.
        material = bpy.data.materials.new("M_StairwellCatGrin")
        material.use_nodes = True
        material.diffuse_color = PALETTE["grin_teeth"]
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        shader.inputs["Base Color"].default_value = PALETTE["grin_teeth"]
        shader.inputs["Roughness"].default_value = 0.35
        emission = (
            shader.inputs.get("Emission Color")
            or shader.inputs.get("Emission")
        )
        if emission is not None:
            emission.default_value = (0.42, 0.62, 0.38, 1)
        strength = shader.inputs.get("Emission Strength")
        if strength is not None:
            strength.default_value = 0.9
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])
        material["bp_runtime_material"] = GRIN_MATERIAL_ASSET
        material["bp_emissive"] = True
        return material

    def create_empty(
        self,
        name: str,
        location,
        registry: dict[str, bpy.types.Object],
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        if name in registry:
            raise ValueError(f"Duplicate stairwell cat empty {name}")
        empty = bpy.data.objects.new(name, None)
        self.result.export_collection.objects.link(empty)
        empty.parent = self.result.root
        empty.location = base.v(location)
        empty.empty_display_type = "PLAIN_AXES"
        empty.empty_display_size = 0.05
        empty["bp_export"] = True
        empty["bp_pivot"] = True
        registry[name] = empty
        return empty

    def add_part(
        self,
        name: str,
        geometry,
        role: str,
        palette_name: str,
        pivot_name: str = "",
        uv_by_vertex=None,
        grin: bool = False,
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        if pivot_name and pivot_name not in self.result.pivots:
            raise ValueError(f"Unknown stairwell cat pivot {pivot_name}")
        color = PALETTE[palette_name]
        vertices, faces = geometry[0], geometry[1]
        origin = (
            Vector(tuple(self.result.pivots[pivot_name].location))
            if pivot_name
            else Vector((0.0, 0.0, 0.0))
        )
        mesh = bpy.data.meshes.new(f"{name}_Mesh")
        mesh.from_pydata(
            [tuple(base.v(vertex) - origin) for vertex in vertices],
            [],
            faces,
        )
        mesh.update(calc_edges=True)
        for polygon in mesh.polygons:
            polygon.use_smooth = False
        if uv_by_vertex is not None:
            uv_layer = mesh.uv_layers.new(name="UVMap")
            for loop in mesh.loops:
                uv_layer.data[loop.index].uv = uv_by_vertex[
                    loop.vertex_index
                ]
        obj = bpy.data.objects.new(name, mesh)
        self.result.export_collection.objects.link(obj)
        obj.location = origin
        obj.color = color
        obj.data.materials.append(
            self.result.grin_material
            if grin
            else self.result.body_material
        )
        obj.parent = self.result.root
        obj.matrix_parent_inverse = (
            self.result.root.matrix_world.inverted()
        )
        triangulate = obj.modifiers.new("Triangulate", "TRIANGULATE")
        triangulate.quad_method = "FIXED"
        triangulate.ngon_method = "CLIP"
        obj["bp_export"] = True
        obj["bp_role"] = role
        obj["bp_pivot"] = pivot_name
        obj["bp_palette"] = palette_name
        obj["bp_base_color"] = list(color)
        obj["bp_generator_version"] = GENERATOR_VERSION
        self.result.parts.append(
            CatPart(obj, pivot_name, role, palette_name, color)
        )
        return obj

    def build_body(self) -> None:
        self.add_part(
            "GEO_Haunches",
            base.make_ellipsoid(
                (0.0, 0.02, 0.115), (0.10, 0.09, 0.12), 12, 6
            ),
            "cat_body",
            "fur",
        )
        self.add_part(
            "GEO_Torso",
            base.make_frustum_between(
                (0.0, -0.005, 0.14),
                (0.0, -0.032, 0.405),
                0.090,
                0.066,
                12,
                flatten=0.85,
            ),
            "cat_chest",
            "fur",
            pivot_name=PIVOT_CHEST,
        )
        self.add_part(
            "GEO_ChestFur",
            base.make_ellipsoid(
                (0.0, -0.075, 0.30), (0.05, 0.03, 0.06), 8, 4
            ),
            "cat_chest",
            "fur_light",
            pivot_name=PIVOT_CHEST,
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"GEO_FrontLeg.{side}",
                base.make_frustum_between(
                    (sign * 0.045, -0.06, 0.30),
                    (sign * 0.052, -0.05, 0.012),
                    0.026,
                    0.020,
                    8,
                ),
                "cat_leg",
                "fur_dark",
            )
            self.add_part(
                f"GEO_Paw.{side}",
                base.make_box(
                    (sign * 0.052, -0.05, 0.015),
                    (0.05, 0.07, 0.03),
                ),
                "cat_paw",
                "paw",
            )

    def build_head(self) -> None:
        self.add_part(
            "GEO_Head",
            base.make_ellipsoid(HEAD_CENTER, HEAD_RADII, 12, 6),
            "cat_head",
            "fur",
            pivot_name=PIVOT_HEAD,
        )
        self.add_part(
            "GEO_Muzzle",
            base.make_ellipsoid(
                (0.0, -0.125, 0.44), (0.035, 0.028, 0.024), 8, 4
            ),
            "cat_muzzle",
            "fur_light",
            pivot_name=PIVOT_HEAD,
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"FACE_Eye.{side}",
                base.make_ellipsoid(
                    (sign * 0.036, -0.128, 0.478),
                    (0.016, 0.008, 0.012),
                    8,
                    4,
                ),
                "cheshire_eye",
                "eye_green",
                pivot_name=PIVOT_HEAD,
            )
            self.add_part(
                f"GEO_Ear.{side}",
                base.make_tapered_box(
                    (sign * 0.05, -0.045, 0.52),
                    (sign * 0.06, -0.038, 0.585),
                    (0.055, 0.030, 0),
                    (0.014, 0.008, 0),
                ),
                "cat_ear",
                "fur_dark",
                pivot_name=(
                    PIVOT_EAR_L if side == "L" else PIVOT_EAR_R
                ),
            )

    def build_tail(self) -> None:
        self.add_part(
            "TAIL_Segment.01",
            base.make_frustum_between(
                PIVOT_LOCATIONS[PIVOT_TAIL[0]],
                PIVOT_LOCATIONS[PIVOT_TAIL[1]],
                0.020,
                0.017,
                8,
            ),
            "cat_tail",
            "fur_tail",
            pivot_name=PIVOT_TAIL[0],
        )
        self.add_part(
            "TAIL_Segment.02",
            base.make_frustum_between(
                PIVOT_LOCATIONS[PIVOT_TAIL[1]],
                PIVOT_LOCATIONS[PIVOT_TAIL[2]],
                0.017,
                0.014,
                8,
            ),
            "cat_tail",
            "fur_tail",
            pivot_name=PIVOT_TAIL[1],
        )
        self.add_part(
            "TAIL_Segment.03",
            base.combine_geometry(
                base.make_frustum_between(
                    PIVOT_LOCATIONS[PIVOT_TAIL[2]],
                    (0.093, 0.096, -0.295),
                    0.014,
                    0.009,
                    8,
                ),
                base.make_ellipsoid(
                    (0.091, 0.093, -0.305),
                    (0.012, 0.012, 0.016),
                    8,
                    4,
                ),
            ),
            "cat_tail",
            "fur_dark",
            pivot_name=PIVOT_TAIL[2],
        )

    def build_grin(self) -> None:
        vertices, faces, uv_by_vertex = grin_geometry()
        self.add_part(
            "ACC_Grin",
            (vertices, faces),
            "cheshire_grin",
            "grin_teeth",
            pivot_name=PIVOT_HEAD,
            uv_by_vertex=uv_by_vertex,
            grin=True,
        )

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-stairwell-cat-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = DESIGN_ID
        scene["bp_seed"] = SEED
        scene["bp_has_own_animations"] = False
        scene["bp_grin_design"] = "arc_growth_crescent"
        scene["bp_grin_uv_arc"] = GRIN_UV_ARC


def object_world_bounds(obj):
    coordinates = [
        obj.matrix_world @ vertex.co for vertex in obj.data.vertices
    ]
    bounds_min = Vector(
        tuple(min(point[axis] for point in coordinates) for axis in range(3))
    )
    bounds_max = Vector(
        tuple(max(point[axis] for point in coordinates) for axis in range(3))
    )
    return bounds_min, bounds_max


def validate_cat_result(result: CatBuildResult) -> CatValidationReport:
    bpy.context.view_layer.update()
    errors: list[str] = []

    if bpy.data.actions:
        errors.append("Cat model must contain no authored Actions")
    if any(
        obj.type in {"LIGHT", "CAMERA", "ARMATURE"}
        for obj in result.export_collection.objects
    ):
        errors.append(
            "Export collection contains a light, camera or armature"
        )

    expected_pivots = tuple(PIVOT_LOCATIONS)
    if tuple(result.pivots) != expected_pivots:
        errors.append(
            f"Pivots are {tuple(result.pivots)!r}; "
            f"expected {expected_pivots!r}"
        )
    for name, pivot in {**result.pivots, **result.anchors}.items():
        if pivot.type != "EMPTY" or pivot.parent != result.root:
            errors.append(
                f"{name} must be an Empty directly below "
                "ROOT_StairwellCat"
            )

    parts = {part.obj.name: part for part in result.parts}
    required = {
        "GEO_Haunches",
        "GEO_Torso",
        "GEO_Head",
        "GEO_Muzzle",
        "FACE_Eye.L",
        "FACE_Eye.R",
        "GEO_Ear.L",
        "GEO_Ear.R",
        "TAIL_Segment.01",
        "TAIL_Segment.02",
        "TAIL_Segment.03",
        "ACC_Grin",
    }
    missing = sorted(required.difference(parts))
    if missing:
        errors.append(f"Missing required cat design parts: {missing}")

    for part in result.parts:
        obj = part.obj
        expected_material = (
            result.grin_material
            if part.obj.name == "ACC_Grin"
            else result.body_material
        )
        if (
            len(obj.data.materials) != 1
            or obj.data.materials[0] != expected_material
        ):
            errors.append(
                f"{obj.name} does not use its designated material"
            )
        if part.pivot:
            pivot = result.pivots.get(part.pivot)
            if pivot is None:
                errors.append(f"{obj.name} names unknown pivot")
            elif (
                Vector(tuple(obj.location))
                - Vector(tuple(pivot.location))
            ).length > 0.000001:
                errors.append(
                    f"{obj.name} origin must sit exactly on "
                    f"{part.pivot} for the runtime adopt"
                )

    grin = parts.get("ACC_Grin")
    if grin is not None:
        grin_min, grin_max = object_world_bounds(grin.obj)
        grin_width = grin_max.x - grin_min.x
        if grin_width <= HEAD_WIDTH:
            errors.append(
                f"The grin must be wider than the head: "
                f"{grin_width:.3f} <= {HEAD_WIDTH:.3f}"
            )
        uv_layer = grin.obj.data.uv_layers.active
        if uv_layer is None:
            errors.append("ACC_Grin has no arc-length UV layer")
        else:
            for datum in uv_layer.data:
                if not (
                    -0.0001 <= datum.uv.x <= 1.0001
                    and -0.0001 <= datum.uv.y <= 1.0001
                ):
                    errors.append(
                        "ACC_Grin UV left the [0,1] arc contract"
                    )
                    break
            # Arc-length u must grow monotonically with world x on
            # each lip rail (v = 0 and v = 1 separately; the two
            # rails run at different radii and interleave in x).
            per_vertex_uv: dict[int, tuple[float, float]] = {}
            for loop in grin.obj.data.loops:
                datum = uv_layer.data[loop.index]
                per_vertex_uv[loop.vertex_index] = (
                    datum.uv.x,
                    datum.uv.y,
                )
            for rail_v in (0.0, 1.0):
                samples = sorted(
                    (grin.obj.data.vertices[index].co.x, u)
                    for index, (u, v_coord) in per_vertex_uv.items()
                    if abs(v_coord - rail_v) < 0.0001
                )
                for (x_a, u_a), (x_b, u_b) in zip(
                    samples, samples[1:]
                ):
                    if x_b - x_a > 0.0001 and u_b < u_a - 0.0001:
                        errors.append(
                            "ACC_Grin arc-length u is not monotonic "
                            "along the smile"
                        )
                        break

    haunches = parts.get("GEO_Haunches")
    if haunches is not None:
        haunch_min, _ = object_world_bounds(haunches.obj)
        if not (-0.02 <= haunch_min.z <= 0.005):
            errors.append(
                f"Haunches must settle onto the rail top, "
                f"bottom at {haunch_min.z:.4f}"
            )
    for side in ("L", "R"):
        paw = parts.get(f"GEO_Paw.{side}")
        if paw is None:
            continue
        paw_min, paw_max = object_world_bounds(paw.obj)
        if abs(paw_min.z) > 0.005:
            errors.append(
                f"GEO_Paw.{side} must ground at z=0, "
                f"got {paw_min.z:.4f}"
            )
        if (
            paw_min.y < MAX_CONTACT_FORWARD
            or paw_max.y > MAX_CONTACT_BACK
        ):
            errors.append(
                f"GEO_Paw.{side} leaves the rail perch contract"
            )

    mesh_count = len(result.parts)
    triangle_count = 0
    world_vertices: list[Vector] = []
    seen_meshes: set[int] = set()
    for part in sorted(result.parts, key=lambda item: item.obj.name):
        obj = part.obj
        if obj.data.as_pointer() in seen_meshes:
            errors.append(f"{obj.name} reuses another part's mesh")
        seen_meshes.add(obj.data.as_pointer())
        for vertex in obj.data.vertices:
            world_vertices.append(obj.matrix_world @ vertex.co)
        triangle_count += base.triangulated_count(obj.data)

    if not MIN_TRIANGLES <= triangle_count <= MAX_TRIANGLES:
        errors.append(
            f"Triangle budget is {triangle_count}; expected "
            f"{MIN_TRIANGLES}-{MAX_TRIANGLES}"
        )
    if mesh_count < 10 or mesh_count > 24:
        errors.append(
            f"Mesh count is {mesh_count}; expected 10-24 parts"
        )

    if not world_vertices:
        errors.append("Cat contains no mesh vertices")
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
        if not 0.50 <= bounds_max.z <= 0.62:
            errors.append(
                f"Ear tips must top out between 0.50 and 0.62 m, "
                f"got {bounds_max.z:.4f}"
            )
        if bounds_min.z < -0.35:
            errors.append(
                f"The hanging tail reaches too far down: "
                f"{bounds_min.z:.4f}"
            )

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(
            f"Stairwell cat validation failed:\n{formatted}"
        )

    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "seed": SEED,
        "parts": [
            {
                "name": part.obj.name,
                "pivot": part.pivot,
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
        "anchors": [
            {
                "name": name,
                "location": [
                    base.stable_float(value)
                    for value in anchor.location
                ],
            }
            for name, anchor in result.anchors.items()
        ],
        "grin": {
            "width_m": base.stable_float(GRIN_WIDTH),
            "tooth_count": GRIN_TOOTH_COUNT,
            "uv_arc": GRIN_UV_ARC,
        },
    }
    signature = hashlib.sha256(
        json.dumps(
            signature_payload, sort_keys=True, separators=(",", ":")
        ).encode("utf-8")
    ).hexdigest()

    return CatValidationReport(
        mesh_count,
        triangle_count,
        tuple(base.stable_float(component) for component in bounds_min),
        tuple(base.stable_float(component) for component in bounds_max),
        signature,
    )


def build_preview_stage(presentation):
    # The rail cross-section under the cat: a believable perch beats a
    # floating model in every review.
    rail_mesh = bpy.data.meshes.new("CatPreviewRail_Mesh")
    vertices, faces = base.make_box((0, 0, -0.58), (1.6, 0.10, 1.16))
    rail_mesh.from_pydata(
        [tuple(vertex) for vertex in vertices], [], faces
    )
    rail = bpy.data.objects.new("CatPreviewRail", rail_mesh)
    presentation.objects.link(rail)
    rail_material = bpy.data.materials.new("MAT_CatPreviewRail")
    rail_material.diffuse_color = (0.055, 0.062, 0.058, 1)
    rail.data.materials.append(rail_material)

    for name, location, energy, color, radius in (
        ("Key", (-1.4, -1.8, 2.2), 420.0, (0.72, 0.82, 0.72), 1.8),
        ("Rim", (1.3, 1.0, 1.8), 300.0, (0.35, 0.48, 0.42), 1.2),
        ("Face", (0.0, -1.4, 1.0), 160.0, (0.83, 0.62, 0.40), 0.8),
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
            Vector((0, 0, 0.30)) - Vector(location)
        ).to_track_quat("-Z", "Y").to_euler()


def render_preview(path: Path, camera_location, target) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.get("PRESENTATION_StairwellCat")
    if presentation is None:
        raise RuntimeError("Cat preview collection is missing")

    camera_data = bpy.data.cameras.new("CAM_StairwellCatPreview")
    camera = bpy.data.objects.new(
        "CAM_StairwellCatPreview", camera_data
    )
    presentation.objects.link(camera)
    camera.location = camera_location
    camera.rotation_euler = (
        Vector(target) - Vector(camera_location)
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 56
    scene.camera = camera

    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)

    presentation.objects.unlink(camera)
    bpy.data.objects.remove(camera)
    bpy.data.cameras.remove(camera_data)


def export_fbx(path: Path, result: CatBuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    result.root.select_set(True)
    for obj in result.export_collection.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = result.root
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


def write_manifest(
    path: Path, result: CatBuildResult, report: CatValidationReport
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-stairwell-cat-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "sitting_height_m": report.bounds_max[2],
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "triangle_budget": [MIN_TRIANGLES, MAX_TRIANGLES],
        "pivot_names": list(result.pivots),
        "anchor_names": list(result.anchors),
        "bounds_min": list(report.bounds_min),
        "bounds_max": list(report.bounds_max),
        "material_asset": SHARED_MATERIAL_ASSET,
        "grin_material_asset": GRIN_MATERIAL_ASSET,
        "grin_width_m": base.stable_float(GRIN_WIDTH),
        "head_width_m": base.stable_float(HEAD_WIDTH),
        "grin_tooth_count": GRIN_TOOTH_COUNT,
        "grin_uv_arc": GRIN_UV_ARC,
        "emissive": False,
        "colliders": False,
        "lights": False,
        "rigidbodies": False,
        "animation_count": 0,
        "animations": [],
        "build_signature": report.build_signature,
        "parts": [
            {
                "name": part.obj.name,
                "pivot": part.pivot,
                "role": part.role,
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


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if (
        bpy.context.object is not None
        and bpy.context.object.mode != "OBJECT"
    ):
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(
        filepath=str(path), check_existing=False
    )


def main() -> None:
    config = parse_args()
    result = StairwellCatBuilder().build()
    report = validate_cat_result(result)
    if not config.no_preview:
        presentation = bpy.data.collections.get(
            "PRESENTATION_StairwellCat"
        )
        build_preview_stage(presentation)
        # Back-quarter shot matching the MiddleFlight framing, then a
        # face shot: the grin only exists from the front, and a review
        # render that cannot see the design's whole point is no review.
        render_preview(
            config.preview, (1.05, 1.30, 1.10), (0, 0, 0.28)
        )
        render_preview(
            config.face_preview, (0.35, -1.45, 0.75), (0, 0, 0.34)
        )
    export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report)
    save_blend(config.output)
    print("STAIRWELL CAT 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print("  Armature: none (pivot empties only)")
    print(f"  Pivots: {len(result.pivots)}")
    print(f"  Meshes: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{MAX_TRIANGLES}")
    print(
        f"  Grin: {GRIN_WIDTH:.2f} m over a {HEAD_WIDTH:.2f} m head, "
        f"{GRIN_TOOTH_COUNT} teeth"
    )
    print(f"  Signature: {report.build_signature}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")
        print(f"  Face preview: {config.face_preview}")


if __name__ == "__main__":
    main()
