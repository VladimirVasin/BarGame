#!/usr/bin/env python3
"""Build the deterministic low-poly cemetery raven.

Run this with Blender, not CPython:

    blender --background --factory-startup --python \
        tools/build-cemetery-raven-3d-model.py

Two of these birds claim the first grave the hero seals: one on the
mound crown, one on clear ground a few metres off. The model follows
the stairwell cat's authoring contract exactly - no armature, pure
pivot empties (PIVOT_BodyRoot for weight shifts and the crouch,
PIVOT_Head for tracking and preening, PIVOT_Wing.L/R for the deploy
and the flap, PIVOT_Tail for the balance pitch), exported flat beside
the meshes with every pivot-bound mesh's origin ON its pivot so the
runtime adopt is exact. ANCHOR_FeetContact sits at the world origin:
the prefab origin IS the ground/perch contact point, the cat's
rail-contact rule carried over.

The rest pose is WINGS FOLDED - the perched look matters most, and
flight deploys the very same slabs by rotating them out about their
shoulder pivots up to `wing_fold_max_degrees` (70), the shared
contract the manifest records so the generator and the runtime pose
rules can never drift. There is no second wing mesh and no nonuniform
pivot scale.

Unlike the cat, the raven carries a 256 px detail atlas (the Kettle
Hat precedent through tools/atlas_kit.py). The atlas MULTIPLIES under
the flat palette colours, so every painted mark is a darkening stroke
in the 100-185 sRGB grey band; the pale accents that anchor the bird
in grayscale - beak and eye - are palette parts and stay untextured,
sampling the reserved pure-white cell at (0, 0).

Blender source space is metres, Z-up, forward -Y and anatomical left
+X. The feet sole at z = 0.
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
    from mathutils import Quaternion, Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "cemetery_raven_v1"
DISPLAY_NAME = "Cemetery Raven"
# The runtime models' deterministic seed; the model and its timelines are kin.
SEED = 0xCA11
MIN_TRIANGLES = 350
MAX_TRIANGLES = 700
SHARED_MATERIAL_ASSET = "Assets/Player3D/Materials/Player3DLit.mat"

# The wing deploy contract shared with the runtime pose rules: 0 degrees is
# the authored folded rest, this value is fully deployed for flight. The
# manifest carries it so a tuning change here reaches the pose maths.
WING_FOLD_MAX_DEGREES = 70.0

PIVOT_BODY_ROOT = "PIVOT_BodyRoot"
PIVOT_HEAD = "PIVOT_Head"
PIVOT_WING_L = "PIVOT_Wing.L"
PIVOT_WING_R = "PIVOT_Wing.R"
PIVOT_TAIL = "PIVOT_Tail"
ANCHOR_FEET = "ANCHOR_FeetContact"

PIVOT_LOCATIONS = {
    PIVOT_BODY_ROOT: (0.0, 0.005, 0.115),
    PIVOT_HEAD: (0.0, -0.095, 0.205),
    PIVOT_WING_L: (0.048, -0.015, 0.16),
    PIVOT_WING_R: (-0.048, -0.015, 0.16),
    PIVOT_TAIL: (0.0, 0.12, 0.135),
}
ANCHOR_LOCATIONS = {
    ANCHOR_FEET: (0.0, 0.0, 0.0),
}

# The bird's geometry constants, all in Blender source space. The body is
# pitched a few degrees nose-down so the silhouette reads chest-heavy, the
# way a perched corvid actually stands.
BODY_CENTER = (0.0, 0.010, 0.130)
BODY_RADII = (0.050, 0.170, 0.070)
BODY_PITCH_DEGREES = 8.0
HEAD_CENTER = (0.0, -0.120, 0.195)
HEAD_RADII = (0.037, 0.045, 0.042)
BEAK_ROOT = (0.0, -0.155, 0.190)
BEAK_TIP = (0.0, -0.210, 0.178)
WING_CENTER_L = (0.055, -0.005, 0.150)
WING_RADII = (0.014, 0.155, 0.034)
TAIL_ROOT = (0.0, 0.130, 0.135)
TAIL_TIP = (0.0, 0.245, 0.112)
# The folded-silhouette contract: with the wings at rest nothing of either
# wing may reach past this half-width, or the "folded" read is a lie.
FOLDED_WING_MAX_ABS_X = 0.085
STANDING_HEIGHT_MIN = 0.20
STANDING_HEIGHT_MAX = 0.28

# Flat colours a step above pure black, so the multiplied atlas still reads;
# the pale beak and eye are the grayscale anchors the art bible names.
# Playtesting proved the first pass too dark: multiplying pale greys under
# ~0.1 linear produced differences below what the eye resolves, and the bird
# read untextured. These tones are lifted one honest step - still «оперение
# почти чёрное» - so the darker stroke band below has room to register.
PALETTE = {
    "body_black": (0.150, 0.150, 0.168, 1.0),
    "wing_black": (0.128, 0.128, 0.146, 1.0),
    "head_black": (0.162, 0.162, 0.180, 1.0),
    "tail_black": (0.136, 0.136, 0.154, 1.0),
    "beak_grey": (0.34, 0.33, 0.31, 1.0),
    "leg_grey": (0.30, 0.29, 0.27, 1.0),
    "eye_pale": (0.66, 0.64, 0.58, 1.0),
}

DETAIL_ATLAS_NAME = "CemeteryRavenDetailAtlas.png"
DETAIL_ATLAS_SIZE = 256
DETAIL_ATLAS_UV_INSET_PX = 1
DETAIL_ATLAS_REGION_PROP = "bp_atlas_region"
# Parts without a UV layer (the eyes) sample texel (0, 0) - one material
# serves the whole model - so the bottom-left cell is reserved pure white
# and nothing is ever painted into it.
DETAIL_ATLAS_RESERVED_CELL = (0, 0, 64, 64)
DETAIL_ATLAS_WHITE = (255, 255, 255, 255)
# The atlas multiplies under near-black palette tones, so every mark must
# darken and stay legible: this closed grey band is the whole vocabulary.
# The band sits deliberately low (100-185): over the lifted plumage tones a
# grey above ~185 multiplies to a difference the eye cannot separate from
# the flat colour, which is exactly the "untextured" read playtesting hit.
DETAIL_ATLAS_GREY_MIN = 100
DETAIL_ATLAS_GREY_MAX = 185


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

sys.path.insert(0, str(Path(__file__).resolve().parent))

import atlas_kit  # noqa: E402  (after the sys.path fix)


@dataclass(frozen=True)
class RavenAtlasRegion:
    """One bottom-left pixel sub-rect of the detail atlas owned by one part.

    `kind` names the UV layout the part receives: `ring` for a closed
    frustum (`sides` stations x `rings` rings), `ellipsoid` for a
    pole-capped ellipsoid (`sides` segments x `rings` rings) and `box`
    for geometry whose faces split between a side panel and a front
    panel by normal.
    """

    name: str
    renderer: str
    x: int
    y: int
    width: int
    height: int
    kind: str
    sides: int = 0
    rings: int = 0

    @property
    def rect_px(self) -> tuple[int, int, int, int]:
        return (self.x, self.y, self.width, self.height)


# Eight regions on 64 px cells beside the reserved white cell; the upper
# half of the atlas stays white. The body and head are pole-capped
# ellipsoids, so they take the ellipsoid strip mapper - the ring mapper
# assumes a frustum's vertex order and would spiral a pole-capped mesh.
RAVEN_ATLAS_REGIONS = (
    RavenAtlasRegion("BodyFeathers", "GEO_Body", 64, 0, 128, 64, "ellipsoid", 12, 6),
    RavenAtlasRegion("BeakDetail", "GEO_Beak", 192, 0, 64, 64, "box"),
    RavenAtlasRegion("WingCoverts.L", "GEO_Wing.L", 0, 64, 64, 64, "box"),
    RavenAtlasRegion("WingCoverts.R", "GEO_Wing.R", 64, 64, 64, 64, "box"),
    RavenAtlasRegion("HeadFeathers", "GEO_Head", 128, 64, 64, 64, "ellipsoid", 10, 5),
    RavenAtlasRegion("TailBands", "GEO_Tail", 192, 64, 64, 64, "box"),
    RavenAtlasRegion("LegScale.L", "GEO_Leg.L", 0, 128, 64, 64, "ring", 7, 2),
    RavenAtlasRegion("LegScale.R", "GEO_Leg.R", 64, 128, 64, 64, "ring", 7, 2),
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(
            "ArtSource/Cemetery/Blender/CemeteryRaven3D.blend"
        ),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path("Assets/Cemetery/Raven/Models/CemeteryRaven3D.fbx"),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("Assets/Cemetery/Raven/Models/CemeteryRaven3D.json"),
    )
    parser.add_argument(
        "--atlas",
        type=Path,
        default=Path(
            "Assets/Cemetery/Raven/Textures/CemeteryRavenDetailAtlas.png"
        ),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path(
            "ArtSource/Cemetery/Blender/CemeteryRaven3D.png"
        ),
    )
    parser.add_argument(
        "--wings-preview",
        type=Path,
        default=Path(
            "ArtSource/Cemetery/Blender/CemeteryRaven3D-wings.png"
        ),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = (
        sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    )
    config = parser.parse_args(arguments)
    for field_name in (
        "output", "fbx", "manifest", "atlas", "preview", "wings_preview"
    ):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


@dataclass
class RavenPart:
    obj: bpy.types.Object
    pivot: str
    role: str
    palette_name: str
    color: tuple[float, float, float, float]


@dataclass
class RavenBuildResult:
    root: bpy.types.Object
    export_collection: bpy.types.Collection
    body_material: bpy.types.Material
    parts: list[RavenPart] = field(default_factory=list)
    pivots: dict[str, bpy.types.Object] = field(default_factory=dict)
    anchors: dict[str, bpy.types.Object] = field(default_factory=dict)


@dataclass(frozen=True)
class RavenAtlasReport:
    """The painted detail atlas as the validator and manifest see it."""

    path: Path
    sha256: str
    width: int
    height: int
    pixels: bytes


@dataclass(frozen=True)
class RavenValidationReport:
    mesh_count: int
    triangle_count: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]
    build_signature: str


def neutral_grey(value: int) -> tuple[int, int, int, int]:
    return (value, value, value, 255)


def paint_covert_rows(
    canvas: atlas_kit.PixelCanvas,
    region: RavenAtlasRegion,
    mirrored: bool,
) -> None:
    """Three rows of covert-feather laps, mirrored for the right wing.

    Each lap is a short dash with a trailing corner texel, reading as
    overlapping feather edges once the strip is multiplied under the
    wing black. The mirror flips the dash direction so both wings lap
    tail-ward when the box panels face outward.
    """

    rect = atlas_kit.atlas_rect_bottom_left
    for row_y, tone in ((14, 160), (28, 150), (42, 140)):
        for dash_start in range(4, 54, 10):
            if mirrored:
                dash_x = region.x + region.width - 1 - dash_start - 7
            else:
                dash_x = region.x + dash_start
            rect(
                canvas,
                dash_x, region.y + row_y,
                dash_x + 7, region.y + row_y + 2,
                neutral_grey(tone),
            )
            tip_x = dash_x if mirrored else dash_x + 5
            rect(
                canvas,
                tip_x, region.y + row_y + 2,
                tip_x + 2, region.y + row_y + 4,
                neutral_grey(tone),
            )


def paint_raven_detail_atlas() -> atlas_kit.PixelCanvas:
    """Paint the raven's detail atlas into a canvas.

    Pure white ground, alpha 255 everywhere, and only darkening greys in
    the 100-185 band - the atlas multiplies under near-black palette
    tones, so a light accent painted here would simply vanish; the pale
    anchors are palette parts. Every coordinate is a bottom-left pixel
    and every value is a literal, so the painter is a pure function and
    the atlas hash is stable across runs.
    """

    canvas = atlas_kit.PixelCanvas(DETAIL_ATLAS_SIZE, DETAIL_ATLAS_SIZE)
    canvas.rect(0, 0, canvas.width, canvas.height, DETAIL_ATLAS_WHITE)
    regions = {region.name: region for region in RAVEN_ATLAS_REGIONS}
    rect = atlas_kit.atlas_rect_bottom_left
    line = atlas_kit.atlas_line_bottom_left

    # Body feathers: sparse lap strokes over the back and flanks. The
    # ellipsoid strip runs u around the body and v belly to back, so the
    # rows sit in the upper half of the cell - the visible saddle.
    region = regions["BodyFeathers"]
    for row_y, tone, phase in (
        (22, 175, 0), (32, 165, 5), (42, 175, 2), (52, 160, 7),
    ):
        for dash_x in range(
            region.x + 4 + phase, region.x + region.width - 12, 12
        ):
            rect(
                canvas,
                dash_x, region.y + row_y,
                dash_x + 6, region.y + row_y + 2,
                neutral_grey(tone),
            )

    # Beak: the box mapper puts the side panel in the left half (u runs
    # back to front) and the forward/top faces in the right half (v runs
    # back to front). The nostril line sits near the root of the side
    # panel; the tip darkens on both panels at their tip edges.
    region = regions["BeakDetail"]
    line(
        canvas,
        region.x + 6, region.y + 44,
        region.x + 14, region.y + 42,
        neutral_grey(110),
        2,
    )
    rect(
        canvas,
        region.x + 24, region.y + 2,
        region.x + 31, region.y + 62,
        neutral_grey(145),
    )
    rect(
        canvas,
        region.x + 28, region.y + 2,
        region.x + 31, region.y + 62,
        neutral_grey(130),
    )
    rect(
        canvas,
        region.x + 33, region.y + 52,
        region.x + 63, region.y + 62,
        neutral_grey(145),
    )

    # Wing coverts: three lap rows per wing, the right cell a mirror of
    # the left so the laps run the same way on both flanks.
    paint_covert_rows(canvas, regions["WingCoverts.L"], mirrored=False)
    paint_covert_rows(canvas, regions["WingCoverts.R"], mirrored=True)

    # Head: fine crown strokes only - short vertical ticks over the top
    # rows of the strip, where the ellipsoid's upper latitudes land.
    region = regions["HeadFeathers"]
    for dash_x in range(region.x + 6, region.x + 58, 8):
        rect(
            canvas,
            dash_x, region.y + 44,
            dash_x + 2, region.y + 48,
            neutral_grey(170),
        )
        rect(
            canvas,
            dash_x + 3, region.y + 34,
            dash_x + 5, region.y + 38,
            neutral_grey(180),
        )

    # Tail: two shaft bands across the fan.
    region = regions["TailBands"]
    for band_y in (20, 40):
        rect(
            canvas,
            region.x + 3, region.y + band_y,
            region.x + 61, region.y + band_y + 2,
            neutral_grey(140),
        )

    # Leg scales: horizontal scute rings up the tarsus. The rings are
    # symmetric, so the right cell's mirror is the same picture.
    for name in ("LegScale.L", "LegScale.R"):
        region = regions[name]
        for ring_y in (10, 20, 30, 40, 50):
            rect(
                canvas,
                region.x + 2, region.y + ring_y,
                region.x + 62, region.y + ring_y + 2,
                neutral_grey(150),
            )

    return canvas


def write_detail_atlas(
    canvas: atlas_kit.PixelCanvas, path: Path
) -> RavenAtlasReport:
    """Write the atlas atomically and report exactly what was written.

    The in-memory PNG payload is hashed first and the bytes on disk are
    re-hashed after the atomic replace: the manifest must never record a
    hash the imported file cannot reproduce.
    """

    payload = canvas.png_bytes()
    width, height, pixels = atlas_kit.decode_generated_png(
        payload, str(path)
    )
    report = RavenAtlasReport(
        path,
        hashlib.sha256(payload).hexdigest(),
        width,
        height,
        pixels,
    )
    canvas.write_png(path)
    written = hashlib.sha256(path.read_bytes()).hexdigest()
    if written != report.sha256:
        raise RuntimeError(
            f"Detail atlas {path} hashes {written} on disk "
            f"but {report.sha256} in memory"
        )
    return report


def mirror_geometry_across_x(geometry):
    """Reflect a primitive payload across X=0 with the winding re-wound.

    A reflection turns every face inside out; reversing each face's loop
    order restores outward normals, which the signed-volume validation
    then proves on both wings.
    """

    vertices, faces = geometry
    mirrored_vertices = [
        Vector((-vertex.x, vertex.y, vertex.z)) for vertex in vertices
    ]
    mirrored_faces = [tuple(reversed(face)) for face in faces]
    return mirrored_vertices, mirrored_faces


class CemeteryRavenBuilder:
    def __init__(self, atlas_path: Path | None = None):
        # Where the painted detail atlas lives, for the review render
        # only: the Unity side binds the texture through the prefab,
        # never through the FBX material.
        self.atlas_path = atlas_path
        self.result: RavenBuildResult | None = None

    def build(self) -> RavenBuildResult:
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        raven = bpy.data.collections.new("BP_CemeteryRaven3D")
        scene_root.children.link(raven)
        export_collection = bpy.data.collections.new(
            "EXPORT_CemeteryRaven"
        )
        raven.children.link(export_collection)
        presentation = bpy.data.collections.new(
            "PRESENTATION_CemeteryRaven"
        )
        raven.children.link(presentation)

        body_material = self.create_body_material()
        self.attach_preview_atlas(body_material)

        root = bpy.data.objects.new("ROOT_CemeteryRaven", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = "tools/build-cemetery-raven-3d-model.py"
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design_id"] = DESIGN_ID
        root["bp_seed"] = SEED
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_has_own_animations"] = False
        root["bp_armature"] = False
        root["bp_wing_fold_max_degrees"] = WING_FOLD_MAX_DEGREES

        self.result = RavenBuildResult(
            root, export_collection, body_material
        )
        for name, location in PIVOT_LOCATIONS.items():
            self.create_empty(name, location, self.result.pivots)
        for name, location in ANCHOR_LOCATIONS.items():
            self.create_empty(name, location, self.result.anchors)

        self.build_body_and_legs()
        self.build_head()
        self.build_wings()
        self.build_tail()
        # The box panel layout reads world-space vertices, so the
        # depsgraph must see the finished objects before UVs are laid.
        bpy.context.view_layer.update()
        self.assign_atlas_uvs()
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
        scene.world = bpy.data.worlds.new("WORLD_CemeteryRavenPreview")
        scene.world.use_nodes = True
        background = scene.world.node_tree.nodes.get("Background")
        if background is not None:
            background.inputs["Color"].default_value = (
                0.012, 0.013, 0.016, 1
            )
            background.inputs["Strength"].default_value = 0.20

    @staticmethod
    def create_body_material() -> bpy.types.Material:
        material = bpy.data.materials.new("MAT_CemeteryRavenBody")
        material.use_nodes = True
        material.diffuse_color = PALETTE["body_black"]
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

    def attach_preview_atlas(
        self, material: bpy.types.Material
    ) -> None:
        """Multiply the object colour by the detail atlas in the review.

        The Image Texture samples with Closest/CLIP exactly like the
        Unity import (Point/Clamp), so what the preview shows is what
        the game draws; parts without UV0 fall on texel (0, 0), the
        reserved white cell, and stay flat colour. Nothing here reaches
        the FBX material contract - Unity imports no materials from
        these files.
        """

        if self.atlas_path is None or not self.atlas_path.is_file():
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
        image.name = "IMG_CemeteryRavenDetailAtlas"
        image.pack()
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = image
        texture.interpolation = "Closest"
        texture.extension = "CLIP"
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        factor = next(
            socket
            for socket in mix.inputs
            if socket.identifier == "Factor_Float"
        )
        factor.default_value = 1.0
        color_a = next(
            socket
            for socket in mix.inputs
            if socket.identifier == "A_Color"
        )
        color_b = next(
            socket
            for socket in mix.inputs
            if socket.identifier == "B_Color"
        )
        result = next(
            socket
            for socket in mix.outputs
            if socket.identifier == "Result_Color"
        )
        for link in list(links):
            if link.to_socket == shader.inputs["Base Color"]:
                links.remove(link)
        links.new(object_info.outputs["Color"], color_a)
        links.new(texture.outputs["Color"], color_b)
        links.new(result, shader.inputs["Base Color"])
        material["bp_detail_atlas"] = DETAIL_ATLAS_NAME

    def create_empty(
        self,
        name: str,
        location,
        registry: dict[str, bpy.types.Object],
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        if name in registry:
            raise ValueError(f"Duplicate cemetery raven empty {name}")
        empty = bpy.data.objects.new(name, None)
        self.result.export_collection.objects.link(empty)
        empty.parent = self.result.root
        empty.location = base.v(location)
        empty.empty_display_type = "PLAIN_AXES"
        empty.empty_display_size = 0.03
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
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        if pivot_name and pivot_name not in self.result.pivots:
            raise ValueError(f"Unknown cemetery raven pivot {pivot_name}")
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
        obj = bpy.data.objects.new(name, mesh)
        self.result.export_collection.objects.link(obj)
        obj.location = origin
        obj.color = color
        obj.data.materials.append(self.result.body_material)
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
            RavenPart(obj, pivot_name, role, palette_name, color)
        )
        return obj

    def build_body_and_legs(self) -> None:
        # The legs ride PIVOT_BodyRoot with the body: they do not
        # articulate on their own, so a crouch or a landing settle is a
        # single body-root delta and the feet still read planted.
        body_pitch = Quaternion(
            (1.0, 0.0, 0.0), math.radians(BODY_PITCH_DEGREES)
        )
        self.add_part(
            "GEO_Body",
            base.make_ellipsoid(
                BODY_CENTER, BODY_RADII, 12, 6, orientation=body_pitch
            ),
            "raven_body",
            "body_black",
            pivot_name=PIVOT_BODY_ROOT,
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"GEO_Leg.{side}",
                base.combine_geometry(
                    base.make_frustum_between(
                        (sign * 0.035, 0.005, 0.100),
                        (sign * 0.038, -0.005, 0.012),
                        0.010,
                        0.007,
                        7,
                    ),
                    base.make_box(
                        (sign * 0.038, -0.030, 0.006),
                        (0.030, 0.060, 0.012),
                    ),
                ),
                "raven_leg",
                "leg_grey",
                pivot_name=PIVOT_BODY_ROOT,
            )

    def build_head(self) -> None:
        self.add_part(
            "GEO_Head",
            base.make_ellipsoid(HEAD_CENTER, HEAD_RADII, 10, 5),
            "raven_head",
            "head_black",
            pivot_name=PIVOT_HEAD,
        )
        # Seven sides close the frustum at exactly 24 triangles and keep
        # the beak the sharpest -Y point of the whole bird - the min -Y
        # ownership the validator pins.
        self.add_part(
            "GEO_Beak",
            base.make_frustum_between(
                BEAK_ROOT, BEAK_TIP, 0.011, 0.0025, 7, flatten=0.80
            ),
            "raven_beak",
            "beak_grey",
            pivot_name=PIVOT_HEAD,
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"GEO_Eye.{side}",
                base.make_ellipsoid(
                    (sign * 0.032, -0.135, 0.205),
                    (0.007, 0.009, 0.008),
                    4,
                    2,
                ),
                "raven_eye",
                "eye_pale",
                pivot_name=PIVOT_HEAD,
            )

    def build_wings(self) -> None:
        # One folded wing per side, authored at rest along the flank.
        # The right wing is the left's exact reflection with every face
        # re-wound, so both keep outward normals and a positive signed
        # volume - a straight negative-scale mirror would export inside
        # out.
        left_geometry = base.make_ellipsoid(
            WING_CENTER_L, WING_RADII, 9, 5
        )
        self.add_part(
            "GEO_Wing.L",
            left_geometry,
            "raven_wing",
            "wing_black",
            pivot_name=PIVOT_WING_L,
        )
        self.add_part(
            "GEO_Wing.R",
            mirror_geometry_across_x(left_geometry),
            "raven_wing",
            "wing_black",
            pivot_name=PIVOT_WING_R,
        )

    def build_tail(self) -> None:
        # A flattened frustum widening toward the tip reads as the fan;
        # eleven sides land the part at exactly 40 triangles.
        self.add_part(
            "GEO_Tail",
            base.make_frustum_between(
                TAIL_ROOT, TAIL_TIP, 0.022, 0.048, 11, flatten=0.30
            ),
            "raven_tail",
            "tail_black",
            pivot_name=PIVOT_TAIL,
        )

    def assign_atlas_uvs(self) -> None:
        """Lay every declared region's part into its atlas sub-rect."""

        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        parts_by_name = {
            part.obj.name: part for part in self.result.parts
        }
        for region in RAVEN_ATLAS_REGIONS:
            part = parts_by_name.get(region.renderer)
            if part is None:
                raise RuntimeError(
                    f"Atlas region {region.name} names a missing part "
                    f"{region.renderer}"
                )
            rect_uv = atlas_kit.uv_rect_normalized(
                region.x, region.y, region.width, region.height,
                DETAIL_ATLAS_SIZE, DETAIL_ATLAS_UV_INSET_PX,
            )
            if region.kind == "ring":
                atlas_kit.assign_ring_strip_uv(
                    part.obj, rect_uv, region.sides, region.rings,
                    region.name, DETAIL_ATLAS_REGION_PROP,
                )
            elif region.kind == "ellipsoid":
                atlas_kit.assign_ellipsoid_strip_uv(
                    part.obj, rect_uv, region.sides, region.rings,
                    region.name, DETAIL_ATLAS_REGION_PROP,
                )
            elif region.kind == "box":
                atlas_kit.assign_box_panel_uv(
                    part.obj, region.rect_px, DETAIL_ATLAS_SIZE,
                    region.name, DETAIL_ATLAS_REGION_PROP,
                )
            else:
                raise RuntimeError(
                    f"Atlas region {region.name} has unknown layout "
                    f"{region.kind!r}"
                )

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-cemetery-raven-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = DESIGN_ID
        scene["bp_seed"] = SEED
        scene["bp_has_own_animations"] = False
        scene["bp_wing_fold_max_degrees"] = WING_FOLD_MAX_DEGREES


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


def signed_volume(obj) -> float:
    """Signed volume of a closed mesh via the divergence theorem.

    Translation-invariant only for closed meshes, which both wings are;
    positive means outward winding. The right wing is a reflection of
    the left, so this is the check that its faces really were re-wound.
    """

    total = 0.0
    mesh = obj.data
    for polygon in mesh.polygons:
        indices = polygon.vertices
        anchor = mesh.vertices[indices[0]].co
        for index_a, index_b in zip(indices[1:], indices[2:]):
            vertex_a = mesh.vertices[index_a].co
            vertex_b = mesh.vertices[index_b].co
            total += anchor.dot(vertex_a.cross(vertex_b)) / 6.0
    return total


def validate_raven_result(
    result: RavenBuildResult, atlas: RavenAtlasReport
) -> RavenValidationReport:
    bpy.context.view_layer.update()
    errors: list[str] = []

    if bpy.data.actions:
        errors.append("Raven model must contain no authored Actions")
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
    expected_anchors = tuple(ANCHOR_LOCATIONS)
    if tuple(result.anchors) != expected_anchors:
        errors.append(
            f"Anchors are {tuple(result.anchors)!r}; "
            f"expected {expected_anchors!r}"
        )
    for name, pivot in {**result.pivots, **result.anchors}.items():
        if pivot.type != "EMPTY" or pivot.parent != result.root:
            errors.append(
                f"{name} must be an Empty directly below "
                "ROOT_CemeteryRaven"
            )

    parts = {part.obj.name: part for part in result.parts}
    required = {
        "GEO_Body",
        "GEO_Head",
        "GEO_Beak",
        "GEO_Wing.L",
        "GEO_Wing.R",
        "GEO_Tail",
        "GEO_Leg.L",
        "GEO_Leg.R",
        "GEO_Eye.L",
        "GEO_Eye.R",
    }
    missing = sorted(required.difference(parts))
    if missing:
        errors.append(f"Missing required raven design parts: {missing}")

    for part in result.parts:
        obj = part.obj
        if (
            len(obj.data.materials) != 1
            or obj.data.materials[0] != result.body_material
        ):
            errors.append(
                f"{obj.name} does not use the shared raven material"
            )
        if not part.pivot:
            errors.append(
                f"{obj.name} must be bound to a pivot - every raven "
                "part articulates through the adopt"
            )
            continue
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

    # Both feet must sole at z = 0: the prefab origin is the contact
    # point, so a hovering or buried foot would follow the bird onto
    # every perch.
    for side in ("L", "R"):
        leg = parts.get(f"GEO_Leg.{side}")
        if leg is None:
            continue
        leg_min, _ = object_world_bounds(leg.obj)
        if abs(leg_min.z) > 0.005:
            errors.append(
                f"GEO_Leg.{side} must sole at z=0, "
                f"got {leg_min.z:.4f}"
            )

    left_wing = parts.get("GEO_Wing.L")
    right_wing = parts.get("GEO_Wing.R")
    if left_wing is not None and right_wing is not None:
        left_world = [
            left_wing.obj.matrix_world @ vertex.co
            for vertex in left_wing.obj.data.vertices
        ]
        right_world = [
            right_wing.obj.matrix_world @ vertex.co
            for vertex in right_wing.obj.data.vertices
        ]
        if len(left_world) != len(right_world):
            errors.append(
                "Wing meshes disagree on vertex count and cannot mirror"
            )
        else:
            for left_vertex, right_vertex in zip(
                left_world, right_world
            ):
                mirrored = Vector(
                    (-left_vertex.x, left_vertex.y, left_vertex.z)
                )
                if (right_vertex - mirrored).length > 0.00001:
                    errors.append(
                        "GEO_Wing.R does not mirror GEO_Wing.L "
                        "across X=0 within 1e-5"
                    )
                    break
        for wing in (left_wing, right_wing):
            volume = signed_volume(wing.obj)
            if volume <= 0.0:
                errors.append(
                    f"{wing.obj.name} has non-positive signed volume "
                    f"{volume:.9f}; its faces are wound inside out"
                )
            wing_max_abs_x = max(
                abs((wing.obj.matrix_world @ vertex.co).x)
                for vertex in wing.obj.data.vertices
            )
            if wing_max_abs_x > FOLDED_WING_MAX_ABS_X:
                errors.append(
                    f"{wing.obj.name} breaks the folded silhouette: "
                    f"|x| {wing_max_abs_x:.4f} > "
                    f"{FOLDED_WING_MAX_ABS_X:.3f}"
                )

    # The beak must own the forwardmost (-Y) vertex of the whole bird:
    # the head model yaws about PIVOT_Head, and a body or wing point
    # ahead of the beak would swing wrong under tracking.
    forwardmost_owner = ""
    forwardmost_y = None
    for part in result.parts:
        for vertex in part.obj.data.vertices:
            world = part.obj.matrix_world @ vertex.co
            if forwardmost_y is None or world.y < forwardmost_y:
                forwardmost_y = world.y
                forwardmost_owner = part.obj.name
    if forwardmost_owner != "GEO_Beak":
        errors.append(
            f"The beak must own the min -Y vertex; "
            f"{forwardmost_owner} does at y={forwardmost_y:.4f}"
        )

    validate_detail_atlas(result, atlas, errors)

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
    if mesh_count < 8 or mesh_count > 12:
        errors.append(
            f"Mesh count is {mesh_count}; expected 8-12 parts"
        )

    if not world_vertices:
        errors.append("Raven contains no mesh vertices")
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
        if not STANDING_HEIGHT_MIN <= bounds_max.z <= STANDING_HEIGHT_MAX:
            errors.append(
                f"Standing height must land between "
                f"{STANDING_HEIGHT_MIN} and {STANDING_HEIGHT_MAX} m, "
                f"got {bounds_max.z:.4f}"
            )
        if bounds_min.z < -0.005:
            errors.append(
                f"Geometry reaches below the sole plane: "
                f"{bounds_min.z:.4f}"
            )

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(
            f"Cemetery raven validation failed:\n{formatted}"
        )

    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "seed": SEED,
        "wing_fold_max_degrees": base.stable_float(
            WING_FOLD_MAX_DEGREES
        ),
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
        "atlas": {
            "file": DETAIL_ATLAS_NAME,
            "size": DETAIL_ATLAS_SIZE,
            "sha256": atlas.sha256,
            "regions": [
                {
                    "name": region.name,
                    "renderer": region.renderer,
                    "cell": list(region.rect_px),
                    "layout": region.kind,
                }
                for region in sorted(
                    RAVEN_ATLAS_REGIONS, key=lambda item: item.name
                )
            ],
        },
    }
    signature = hashlib.sha256(
        json.dumps(
            signature_payload, sort_keys=True, separators=(",", ":")
        ).encode("utf-8")
    ).hexdigest()

    return RavenValidationReport(
        mesh_count,
        triangle_count,
        tuple(base.stable_float(component) for component in bounds_min),
        tuple(base.stable_float(component) for component in bounds_max),
        signature,
    )


def validate_detail_atlas(
    result: RavenBuildResult,
    atlas: RavenAtlasReport,
    errors: list[str],
) -> None:
    """The atlas half of validation: regions, UVs and the PNG itself."""

    if atlas.width != DETAIL_ATLAS_SIZE or atlas.height != DETAIL_ATLAS_SIZE:
        errors.append(
            f"Detail atlas is {atlas.width}x{atlas.height}; "
            f"expected {DETAIL_ATLAS_SIZE}"
        )
        return

    occupied: list[tuple[int, int, int, int]] = []
    reserved_x, reserved_y, reserved_w, reserved_h = (
        DETAIL_ATLAS_RESERVED_CELL
    )
    for region in RAVEN_ATLAS_REGIONS:
        if (
            region.x < 0 or region.y < 0
            or region.x + region.width > DETAIL_ATLAS_SIZE
            or region.y + region.height > DETAIL_ATLAS_SIZE
        ):
            errors.append(
                f"Atlas region {region.name} leaves the "
                f"{DETAIL_ATLAS_SIZE} px atlas"
            )
        for other in occupied:
            if (
                region.x < other[0] + other[2]
                and other[0] < region.x + region.width
                and region.y < other[1] + other[3]
                and other[1] < region.y + region.height
            ):
                errors.append(
                    f"Atlas region {region.name} overlaps another region"
                )
        if (
            region.x < reserved_x + reserved_w
            and reserved_x < region.x + region.width
            and region.y < reserved_y + reserved_h
            and reserved_y < region.y + region.height
        ):
            errors.append(
                f"Atlas region {region.name} overlaps the reserved "
                "white cell"
            )
        occupied.append(region.rect_px)

    # The reserved cell must stay untouched white: the un-UV'd eyes
    # sample it, and any stray stroke there would tint them.
    white_count = atlas_kit.count_rect_color(
        atlas.pixels,
        atlas.width,
        atlas.height,
        DETAIL_ATLAS_RESERVED_CELL,
        {DETAIL_ATLAS_WHITE},
    )
    if white_count != reserved_w * reserved_h:
        errors.append(
            f"Reserved atlas cell has {reserved_w * reserved_h - white_count} "
            "non-white pixels"
        )

    # Every painted mark must be a neutral darkening grey in the closed
    # band - the multiply pipeline renders nothing lighter than white
    # and nothing darker than the band survives over near-black.
    for pixel_y in range(atlas.height):
        for pixel_x in range(atlas.width):
            pixel = atlas_kit.png_pixel_bottom_left(
                atlas.pixels, atlas.width, atlas.height,
                pixel_x, pixel_y,
            )
            if pixel == DETAIL_ATLAS_WHITE:
                continue
            red, green, blue, alpha = pixel
            if (
                alpha != 255
                or red != green
                or green != blue
                or not DETAIL_ATLAS_GREY_MIN <= red <= DETAIL_ATLAS_GREY_MAX
            ):
                errors.append(
                    f"Atlas pixel ({pixel_x}, {pixel_y}) is {pixel}; "
                    f"only white or neutral greys "
                    f"{DETAIL_ATLAS_GREY_MIN}-{DETAIL_ATLAS_GREY_MAX} "
                    "are allowed"
                )
                return

    parts_by_name = {part.obj.name: part for part in result.parts}
    textured = {region.renderer for region in RAVEN_ATLAS_REGIONS}
    for region in RAVEN_ATLAS_REGIONS:
        part = parts_by_name.get(region.renderer)
        if part is None:
            errors.append(
                f"Atlas region {region.name} names a missing part "
                f"{region.renderer}"
            )
            continue
        uv_layer = part.obj.data.uv_layers.active
        if uv_layer is None:
            errors.append(f"{region.renderer} has no atlas UV layer")
            continue
        u0, v0, u1, v1 = atlas_kit.uv_rect_normalized(
            region.x, region.y, region.width, region.height,
            DETAIL_ATLAS_SIZE, DETAIL_ATLAS_UV_INSET_PX,
        )
        tolerance = 0.0001
        for datum in uv_layer.data:
            if not (
                u0 - tolerance <= datum.uv.x <= u1 + tolerance
                and v0 - tolerance <= datum.uv.y <= v1 + tolerance
            ):
                errors.append(
                    f"{region.renderer} UV ({datum.uv.x:.4f}, "
                    f"{datum.uv.y:.4f}) leaves the inset "
                    f"{region.name} cell"
                )
                break
    for part in result.parts:
        if (
            part.obj.name not in textured
            and part.obj.data.uv_layers.active is not None
        ):
            errors.append(
                f"{part.obj.name} carries a UV layer but no atlas "
                "region; it would sample painted texels"
            )


def capture_wing_rest(result: RavenBuildResult) -> dict[str, tuple]:
    """Snapshot the wing transforms the deployed preview must restore.

    Both the wing meshes and their pivot empties are captured: the
    meshes are what the preview rotates, and asserting the empties too
    proves nothing else drifted while the stage was posed.
    """

    rest: dict[str, tuple] = {}
    for obj in wing_pose_objects(result):
        rest[obj.name] = (
            tuple(obj.location),
            tuple(obj.rotation_euler),
            tuple(obj.scale),
        )
    return rest


def wing_pose_objects(result: RavenBuildResult):
    parts = {part.obj.name: part.obj for part in result.parts}
    return (
        parts["GEO_Wing.L"],
        parts["GEO_Wing.R"],
        result.pivots[PIVOT_WING_L],
        result.pivots[PIVOT_WING_R],
    )


def pose_wings_deployed(result: RavenBuildResult) -> None:
    """Swing the folded wings out to the deploy limit for the review.

    Each wing mesh's origin sits exactly on its shoulder pivot, so a
    plain object rotation IS the runtime deploy about that pivot - the
    empties themselves move no geometry (the meshes export flat beside
    them and only Unity's adopt reparents), which is why the meshes are
    what gets rotated here. A negative yaw swings the left tip outward
    (+X), the mirrored positive yaw the right; the small roll flattens
    each slab toward the flight plane.
    """

    parts = {part.obj.name: part.obj for part in result.parts}
    deploy = math.radians(WING_FOLD_MAX_DEGREES)
    roll = math.radians(14.0)
    parts["GEO_Wing.L"].rotation_euler = (0.0, -roll, -deploy)
    parts["GEO_Wing.R"].rotation_euler = (0.0, roll, deploy)


def restore_wing_rest(
    result: RavenBuildResult, rest: dict[str, tuple]
) -> None:
    for obj in wing_pose_objects(result):
        location, rotation, scale = rest[obj.name]
        obj.location = location
        obj.rotation_euler = rotation
        obj.scale = scale


def assert_wings_at_rest(result: RavenBuildResult) -> None:
    """Refuse to export anything but the authored folded rest pose.

    The deployed preview is a temporary stage pose; if it ever leaked
    into the FBX, the runtime's rest-pose capture would treat spread
    wings as "folded" and every fold delta would double up.
    """

    for obj in wing_pose_objects(result):
        if tuple(obj.rotation_euler) != (0.0, 0.0, 0.0):
            raise RuntimeError(
                f"{obj.name} is not at rest rotation before export: "
                f"{tuple(obj.rotation_euler)!r}"
            )
        if tuple(obj.scale) != (1.0, 1.0, 1.0):
            raise RuntimeError(
                f"{obj.name} is not at rest scale before export: "
                f"{tuple(obj.scale)!r}"
            )
    for name, pivot_name in (
        ("GEO_Wing.L", PIVOT_WING_L),
        ("GEO_Wing.R", PIVOT_WING_R),
    ):
        parts = {part.obj.name: part.obj for part in result.parts}
        drift = (
            Vector(tuple(parts[name].location))
            - Vector(tuple(PIVOT_LOCATIONS[pivot_name]))
        ).length
        if drift > 0.000001:
            raise RuntimeError(
                f"{name} origin drifted {drift:.9f} m off "
                f"{pivot_name} before export"
            )


def build_preview_stage(presentation) -> None:
    # A soil block under the bird: the raven's whole life is standing on
    # a grave mound or bare cemetery ground, and a believable perch
    # beats a floating model in every review.
    soil_mesh = bpy.data.meshes.new("RavenPreviewSoil_Mesh")
    vertices, faces = base.make_box((0, 0, -0.06), (0.9, 0.7, 0.12))
    soil_mesh.from_pydata(
        [tuple(vertex) for vertex in vertices], [], faces
    )
    soil = bpy.data.objects.new("RavenPreviewSoil", soil_mesh)
    presentation.objects.link(soil)
    soil_material = bpy.data.materials.new("MAT_RavenPreviewSoil")
    soil_material.diffuse_color = (0.062, 0.052, 0.040, 1)
    soil.data.materials.append(soil_material)

    for name, location, energy, color, radius in (
        ("Key", (-1.2, -1.5, 1.8), 320.0, (0.74, 0.78, 0.80), 1.5),
        ("Rim", (1.1, 0.9, 1.4), 210.0, (0.40, 0.46, 0.55), 1.0),
        ("Face", (0.0, -1.2, 0.7), 110.0, (0.80, 0.72, 0.58), 0.7),
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
            Vector((0, 0, 0.12)) - Vector(location)
        ).to_track_quat("-Z", "Y").to_euler()


def render_preview(path: Path, camera_location, target) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.get(
        "PRESENTATION_CemeteryRaven"
    )
    if presentation is None:
        raise RuntimeError("Raven preview collection is missing")

    camera_data = bpy.data.cameras.new("CAM_CemeteryRavenPreview")
    camera = bpy.data.objects.new(
        "CAM_CemeteryRavenPreview", camera_data
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


def export_fbx(path: Path, result: RavenBuildResult) -> None:
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
    path: Path,
    result: RavenBuildResult,
    report: RavenValidationReport,
    atlas: RavenAtlasReport,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-cemetery-raven-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "standing_height_m": report.bounds_max[2],
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
        "detail_atlas_file": DETAIL_ATLAS_NAME,
        "detail_atlas_size": DETAIL_ATLAS_SIZE,
        "detail_atlas_sha256": atlas.sha256,
        "atlas_regions": [
            {
                "name": region.name,
                "renderer": region.renderer,
                "cell": list(region.rect_px),
                "layout": region.kind,
            }
            for region in sorted(
                RAVEN_ATLAS_REGIONS, key=lambda item: item.name
            )
        ],
        "wing_fold_max_degrees": base.stable_float(
            WING_FOLD_MAX_DEGREES
        ),
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
    # The atlas is painted and written before the build so the review
    # material can multiply the very file Unity will import.
    atlas_report = write_detail_atlas(
        paint_raven_detail_atlas(), config.atlas
    )
    result = CemeteryRavenBuilder(config.atlas).build()
    report = validate_raven_result(result, atlas_report)
    if not config.no_preview:
        presentation = bpy.data.collections.get(
            "PRESENTATION_CemeteryRaven"
        )
        build_preview_stage(presentation)
        # Perched three-quarter first - the pose the game shows for
        # minutes at a time - then the deployed wings, because a review
        # that never sees the flight silhouette cannot judge the one
        # articulated feature the bird has. The deploy is a temporary
        # stage pose, restored and asserted before anything exports.
        render_preview(
            config.preview, (0.40, -0.52, 0.30), (0.0, -0.02, 0.12)
        )
        wing_rest = capture_wing_rest(result)
        pose_wings_deployed(result)
        bpy.context.view_layer.update()
        render_preview(
            config.wings_preview, (0.05, -0.72, 0.42), (0.0, 0.0, 0.14)
        )
        restore_wing_rest(result, wing_rest)
        bpy.context.view_layer.update()
    assert_wings_at_rest(result)
    export_fbx(config.fbx, result)
    write_manifest(config.manifest, result, report, atlas_report)
    save_blend(config.output)
    print("CEMETERY RAVEN 3D BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print("  Armature: none (pivot empties only)")
    print(f"  Pivots: {len(result.pivots)}")
    print(f"  Meshes: {report.mesh_count}")
    print(f"  Triangles: {report.triangle_count}/{MAX_TRIANGLES}")
    print(f"  Standing height: {report.bounds_max[2]:.3f} m")
    print(f"  Wing fold limit: {WING_FOLD_MAX_DEGREES:.0f} deg")
    print(f"  Atlas: {config.atlas} ({atlas_report.sha256[:12]}...)")
    print(f"  Signature: {report.build_signature}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")
        print(f"  Wings preview: {config.wings_preview}")


if __name__ == "__main__":
    main()
