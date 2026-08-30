#!/usr/bin/env python3
"""Build the complete authored exterior of the neighbourhood supermarket.

The reference is the compact street-facing convenience-store type: one low,
heavy volume, a broad striped fascia, brick end piers, a recessed glazed run
and a small blade sign.  The result is deliberately not a 7-Eleven replica:
there is no 7, logo, price, slogan, roadside pylon or exact corporate livery.
The only authored text is the already-canonical Russian shop word
``ПРОДУКТЫ``, assembled from deterministic block strokes rather than a font.

All input dimensions below are Unity-local metres with +Z facing the street.
``bar_parts.to_source`` converts them to Blender source space by swapping Y/Z
and re-winding every face.  The exterior remains passive: Unity owns its
logical collider, entrance trigger, transition and yard spotlight.

Run from the repository root with Blender 5::

    blender --background --factory-startup --python \
      tools/build-supermarket-exterior-3d-model.py -- --validate-only
    blender --background --factory-startup --python \
      tools/build-supermarket-exterior-3d-model.py
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender entry point.
    raise SystemExit("Run this generator through Blender's Python.") from error


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import interior_kit as kit  # noqa: E402
import bar_parts as bp  # noqa: E402
import city_building_parts as city_parts  # noqa: E402
from city_building_coplanarity import (  # noqa: E402
    find_axis_aligned_coplanar_overlaps,
    find_near_coplanar_visible_overlaps,
    validate_coplanarity_audit_contract,
)


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "supermarket_exterior_v1"
DISPLAY_NAME = "Bar Promenade Neighbourhood Supermarket Exterior"

WIDTH = 15.5
DEPTH = 15.5
HEIGHT = 6.4
HALF_WIDTH = WIDTH * 0.5
HALF_DEPTH = DEPTH * 0.5
STOREFRONT_WIDTH = 8.4
CANOPY_WIDTH = 9.2
DOOR_WIDTH = 1.9
DOOR_HEIGHT = 2.4
DOOR_ANCHOR = (0.0, 0.0, HALF_DEPTH)
OPAQUE_OVERLAY_CLEARANCE = 0.03
RUNTIME_FOUNDATION_INSET = 0.14

DEFAULT_BLEND = (
    ROOT / "ArtSource" / "Supermarket" / "Blender" /
    "SupermarketExterior3D.blend"
)
DEFAULT_FBX = (
    ROOT / "Assets" / "Supermarket" / "Models" /
    "SupermarketExterior3D.fbx"
)
DEFAULT_MANIFEST = (
    ROOT / "Assets" / "Supermarket" / "Models" /
    "SupermarketExterior3D.json"
)
DEFAULT_PREVIEW = (
    ROOT / "ArtSource" / "Supermarket" / "Preview" /
    "SupermarketExterior3D.png"
)

TEXTURE_PATHS = {
    "ExteriorWallAtlas": (
        ROOT / "Assets" / "Resources" / "Supermarket" /
        "ExteriorTextures" / "SupermarketExteriorWallAtlas.png"
    ),
    "ExteriorFasciaAtlas": (
        ROOT / "Assets" / "Resources" / "Supermarket" /
        "ExteriorTextures" / "SupermarketExteriorFasciaAtlas.png"
    ),
    "ExteriorBrick": (
        ROOT / "Assets" / "Resources" / "Supermarket" /
        "ExteriorTextures" / "SupermarketExteriorBrickAlbedo.png"
    ),
    "ExteriorMetal": (
        ROOT / "Assets" / "Resources" / "Supermarket" /
        "ExteriorTextures" / "SupermarketExteriorMetalAlbedo.png"
    ),
    "ExteriorRoof": ROOT / "Assets" / "Resources" / "Textures" / "CityRoofAlbedo.png",
    "ExteriorGlass": ROOT / "Assets" / "Resources" / "Textures" / "CityWindowAlbedo.png",
    "ExteriorMat": (
        ROOT / "Assets" / "Resources" / "Supermarket" /
        "ExteriorTextures" / "SupermarketExteriorMetalAlbedo.png"
    ),
}

SHEET_PITCH = {
    "ExteriorWallAtlas": 15.5,
    "ExteriorFasciaAtlas": 15.5,
    "ExteriorBrick": 1.2,
    "ExteriorRoof": 4.0,
    "ExteriorMetal": 1.35,
    "ExteriorGlass": 1.0,
    "ExteriorInteriorDark": 1.8,
    "ExteriorInteriorLight": 1.0,
    "ExteriorSignHousing": 1.0,
    "ExteriorSignGlow": 1.0,
    "ExteriorMat": 0.8,
}

PREVIEW_COLORS = {
    "ExteriorWallAtlas": (0.50, 0.48, 0.40, 1.0),
    "ExteriorFasciaAtlas": (0.20, 0.32, 0.27, 1.0),
    "ExteriorBrick": (0.22, 0.11, 0.08, 1.0),
    "ExteriorRoof": (0.08, 0.09, 0.10, 1.0),
    "ExteriorMetal": (0.31, 0.34, 0.32, 1.0),
    "ExteriorGlass": (0.16, 0.28, 0.27, 0.48),
    "ExteriorInteriorDark": (0.055, 0.065, 0.060, 1.0),
    "ExteriorInteriorLight": (1.0, 0.68, 0.31, 1.0),
    "ExteriorSignHousing": (0.11, 0.22, 0.18, 1.0),
    "ExteriorSignGlow": (0.96, 0.71, 0.29, 1.0),
    "ExteriorMat": (0.10, 0.10, 0.085, 1.0),
}

ALLOWED_SHEETS = frozenset(SHEET_PITCH)


@dataclass
class Part:
    obj: "bpy.types.Object"
    name: str
    role: str
    group: str
    sheet: str
    emissive: bool
    casts_shadows: bool
    tint: dict
    geometry: kit.Geometry
    atlas_side: str = ""


@dataclass
class AssetBuild:
    root: "bpy.types.Object"
    collection: "bpy.types.Collection"
    parts: list[Part] = field(default_factory=list)
    anchors: dict[str, "bpy.types.Object"] = field(default_factory=dict)


def stable(value: float) -> float:
    return round(float(value), 6)


def merged_boxes(
    boxes: Sequence[tuple[Sequence[float], Sequence[float]]],
    *,
    chamfer: float = 0.01,
) -> kit.Geometry:
    return kit.merge_all(bp.u_box(center, size, chamfer) for center, size in boxes)


def mirror_unity_x(geometry: kit.Geometry) -> kit.Geometry:
    """Mirror a front-facing graphic so it reads from outside at +Z.

    An observer in front of Unity's +Z facade faces toward -Z, so their screen
    right is local -X. Mirroring both vertices and winding preserves outward
    volume while making authored text read left-to-right from the street.
    """
    vertices, faces = geometry
    return (
        [(-x, y, z) for x, y, z in vertices],
        [tuple(reversed(face)) for face in faces],
    )


def create_material(sheet: str) -> "bpy.types.Material":
    material = bpy.data.materials.new(f"PREVIEW_Supermarket_{sheet}")
    material.diffuse_color = PREVIEW_COLORS[sheet]
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        base_input = bsdf.inputs.get("Base Color")
        rough_input = bsdf.inputs.get("Roughness")
        metallic_input = bsdf.inputs.get("Metallic")
        alpha_input = bsdf.inputs.get("Alpha")
        if base_input is not None:
            base_input.default_value = PREVIEW_COLORS[sheet]
        if rough_input is not None:
            rough_input.default_value = 0.16 if sheet == "ExteriorGlass" else 0.72
        if metallic_input is not None:
            metallic_input.default_value = 0.28 if sheet == "ExteriorMetal" else 0.0
        if alpha_input is not None:
            alpha_input.default_value = PREVIEW_COLORS[sheet][3]

        texture_path = TEXTURE_PATHS.get(sheet)
        if texture_path is not None and texture_path.exists():
            image = bpy.data.images.load(str(texture_path), check_existing=True)
            texture = material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image = image
            texture.interpolation = "Linear"
            texture.extension = "REPEAT" if sheet in {
                "ExteriorBrick", "ExteriorMetal", "ExteriorRoof", "ExteriorMat"
            } else "EXTEND"
            if base_input is not None:
                material.node_tree.links.new(texture.outputs["Color"], base_input)

        if sheet in {"ExteriorInteriorLight", "ExteriorSignGlow"}:
            emission = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
            strength = bsdf.inputs.get("Emission Strength")
            if emission is not None:
                emission.default_value = PREVIEW_COLORS[sheet]
            if strength is not None:
                strength.default_value = 3.2 if sheet == "ExteriorSignGlow" else 5.0

    if sheet == "ExteriorGlass":
        material.diffuse_color = PREVIEW_COLORS[sheet]
        if hasattr(material, "surface_render_method"):
            material.surface_render_method = "DITHERED"
        if hasattr(material, "use_transparency_overlap"):
            material.use_transparency_overlap = False
    return material


def atlas_uv(source_point: Sequence[float], side: str) -> tuple[float, float]:
    x, y, z = source_point  # source Y is Unity Z; source Z is Unity Y.
    if side == "front":
        local_u = (x + HALF_WIDTH) / WIDTH
        quadrant = (0.0, 0.5)
    elif side == "right":
        local_u = (HALF_DEPTH - y) / DEPTH
        quadrant = (0.5, 0.5)
    elif side == "rear":
        local_u = (HALF_WIDTH - x) / WIDTH
        quadrant = (0.0, 0.0)
    elif side == "left":
        local_u = (y + HALF_DEPTH) / DEPTH
        quadrant = (0.5, 0.0)
    else:
        raise RuntimeError(f"Atlas surface has no valid side: {side!r}")
    local_v = z / HEIGHT
    pad = 0.006
    local_u = pad + max(0.0, min(1.0, local_u)) * (1.0 - pad * 2.0)
    local_v = pad + max(0.0, min(1.0, local_v)) * (1.0 - pad * 2.0)
    return quadrant[0] + local_u * 0.5, quadrant[1] + local_v * 0.5


def assign_uv(mesh: "bpy.types.Mesh", part: Part) -> None:
    layer = mesh.uv_layers.new(name="UVMap")
    low, high = kit.bounds(part.geometry)
    pitch = SHEET_PITCH[part.sheet]
    atlas = part.sheet in {"ExteriorWallAtlas", "ExteriorFasciaAtlas"}
    tiled = part.sheet in {"ExteriorBrick", "ExteriorRoof", "ExteriorMetal", "ExteriorMat"}
    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        for loop_index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if atlas:
                uv = atlas_uv(point, part.atlas_side)
            elif tiled:
                if axis == 0:
                    uv = (point.y / pitch, point.z / pitch)
                elif axis == 1:
                    uv = (point.x / pitch, point.z / pitch)
                else:
                    uv = (point.x / pitch, point.y / pitch)
            else:
                if axis == 0:
                    first = (point.y - low[1]) / max(1e-5, high[1] - low[1])
                    second = (point.z - low[2]) / max(1e-5, high[2] - low[2])
                elif axis == 1:
                    first = (point.x - low[0]) / max(1e-5, high[0] - low[0])
                    second = (point.z - low[2]) / max(1e-5, high[2] - low[2])
                else:
                    first = (point.x - low[0]) / max(1e-5, high[0] - low[0])
                    second = (point.y - low[1]) / max(1e-5, high[1] - low[1])
                uv = (first, second)
            layer.data[loop_index].uv = uv


def add_part(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
    name: str,
    geometry: kit.Geometry,
    role: str,
    sheet: str,
    tint: dict,
    *,
    group: str = "fixed",
    emissive: bool = False,
    casts_shadows: bool = True,
    atlas_side: str = "",
) -> "bpy.types.Object":
    if sheet not in ALLOWED_SHEETS:
        raise SystemExit(f"Part '{name}' names unsupported sheet '{sheet}'.")
    source_geometry = bp.to_source(geometry)
    vertices, faces = source_geometry
    if not vertices or not faces:
        raise SystemExit(f"Part '{name}' is empty.")
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(materials[sheet])
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = asset.root
    obj["bp_role"] = role
    obj["bp_group"] = group
    obj["bp_sheet"] = sheet
    obj["bp_emissive"] = emissive
    obj["bp_casts_shadows"] = casts_shadows
    part = Part(
        obj, name, role, group, sheet, emissive, casts_shadows,
        tint, source_geometry, atlas_side,
    )
    asset.parts.append(part)
    assign_uv(mesh, part)
    return obj


def add_anchor(
    asset: AssetBuild,
    name: str,
    role: str,
    unity_position: Sequence[float],
) -> None:
    anchor = bpy.data.objects.new(f"ANCHOR_{name}", None)
    asset.collection.objects.link(anchor)
    anchor.parent = asset.root
    anchor.empty_display_type = "ARROWS"
    anchor.empty_display_size = 0.5
    x, y, z = unity_position
    anchor.location = (float(x), float(z), float(y))
    anchor["bp_role"] = role
    asset.anchors[name] = anchor


def build_shell(asset: AssetBuild, materials: dict[str, "bpy.types.Material"]) -> None:
    # Side walls stop short of the front reveal.  Their uninterrupted middle
    # at Y ~= 3.5 is the canonical mount zone for the yard spotlight.
    add_part(
        asset, materials, "Left Rendered Wall",
        bp.u_box((-7.50, 2.18, -0.01), (0.34, 4.36, 14.64), 0.012),
        "exterior_wall", "ExteriorWallAtlas", bp.rgb(0.72, 0.70, 0.61),
        atlas_side="left",
    )
    add_part(
        asset, materials, "Right Rendered Wall",
        bp.u_box((7.50, 2.18, -0.01), (0.34, 4.36, 14.64), 0.012),
        "exterior_wall", "ExteriorWallAtlas", bp.rgb(0.72, 0.70, 0.61),
        atlas_side="right",
    )

    rear_wall_boxes = (
        ((-2.665, 2.18, -7.50), (9.33, 4.36, 0.34)),
        ((5.44, 2.18, -7.50), (3.78, 4.36, 0.34)),
        ((2.80, 3.455, -7.50), (1.50, 1.81, 0.34)),
    )
    add_part(
        asset, materials, "Rear Rendered Wall",
        merged_boxes(rear_wall_boxes, chamfer=0.008),
        "exterior_wall", "ExteriorWallAtlas", bp.rgb(0.68, 0.67, 0.60),
        atlas_side="rear",
    )

    # The 8.40 m storefront is held exactly between these two heavy wings.
    front_wings = (
        ((-5.935, 2.18, 7.48), (3.47, 4.36, 0.34)),
        ((5.935, 2.18, 7.48), (3.47, 4.36, 0.34)),
    )
    add_part(
        asset, materials, "Front Brick Wings",
        merged_boxes(front_wings, chamfer=0.016),
        "exterior_masonry", "ExteriorBrick", bp.rgb(0.52, 0.34, 0.27),
        group="frontage",
    )

    # One UV-authored fascia surface per elevation; the ochre/green/oxblood
    # motif lives in the atlas, never in coplanar stripe plates.
    fascia = (
        ("Front Fascia", (0.0, 5.02, 7.47), (15.5, 1.32, 0.30), "front"),
        ("Left Fascia", (-7.50, 5.02, 0.01), (0.34, 1.32, 14.68), "left"),
        ("Right Fascia", (7.50, 5.02, 0.01), (0.34, 1.32, 14.68), "right"),
        ("Rear Fascia", (0.0, 5.02, -7.50), (14.66, 1.32, 0.34), "rear"),
    )
    for name, center, size, side in fascia:
        add_part(
            asset, materials, name, bp.u_box(center, size, 0.012),
            "exterior_fascia", "ExteriorFasciaAtlas",
            bp.rgb(0.78, 0.75, 0.64), group="fascia", atlas_side=side,
        )

    plinth_boxes = (
        ((-7.72, 0.22, -0.03), (0.06, 0.44, 15.44)),
        ((7.72, 0.22, -0.03), (0.06, 0.44, 15.44)),
        ((0.0, 0.22, -7.72), (15.38, 0.44, 0.06)),
        ((-5.935, 0.22, 7.66), (3.47, 0.44, 0.12)),
        ((5.935, 0.22, 7.66), (3.47, 0.44, 0.12)),
    )
    add_part(
        asset, materials, "Dark Brick Plinth",
        merged_boxes(plinth_boxes, chamfer=0.008),
        "exterior_masonry", "ExteriorBrick", bp.rgb(0.43, 0.29, 0.24),
    )

    add_part(
        asset, materials, "Flat Membrane Roof",
        bp.u_box((0.0, 5.49, -0.08), (14.86, 0.18, 14.86), 0.016),
        "exterior_roof", "ExteriorRoof", bp.rgb(0.20, 0.22, 0.23),
        group="roof",
    )
    cap_boxes = (
        ((0.0, 5.75, 7.48), (15.50, 0.14, 0.30)),
        ((0.0, 5.75, -7.56), (15.38, 0.14, 0.18)),
        ((-7.58, 5.75, -0.07), (0.18, 0.14, 14.80)),
        ((7.58, 5.75, -0.07), (0.18, 0.14, 14.80)),
    )
    add_part(
        asset, materials, "Parapet Metal Cap",
        merged_boxes(cap_boxes, chamfer=0.008),
        "exterior_metal", "ExteriorMetal", bp.rgb(0.70, 0.70, 0.64),
        group="roof",
    )


def build_storefront(asset: AssetBuild, materials: dict[str, "bpy.types.Material"]) -> None:
    glass_y_center = (0.62 + 3.84) * 0.5
    glass_height = 3.84 - 0.62
    panes = (
        ((-3.425, glass_y_center, 7.235), (1.41, glass_height, 0.045)),
        ((-1.875, glass_y_center, 7.235), (1.41, glass_height, 0.045)),
        ((1.875, glass_y_center, 7.235), (1.41, glass_height, 0.045)),
        ((3.425, glass_y_center, 7.235), (1.41, glass_height, 0.045)),
    )
    add_part(
        asset, materials, "Storefront Glass Bays",
        merged_boxes(panes, chamfer=0.003),
        "exterior_glass", "ExteriorGlass", bp.rgb(0.62, 0.75, 0.70),
        group="frontage", casts_shadows=False,
    )

    vertical_positions = (-4.14, -2.625, -1.05, 1.05, 2.625, 4.14)
    frame_boxes = [
        ((x, 2.20, 7.285), (0.12, 3.96, 0.10))
        for x in vertical_positions
    ]
    frame_boxes.extend((
        ((0.0, 0.56, 7.285), (STOREFRONT_WIDTH, 0.12, 0.10)),
        ((0.0, 3.91, 7.285), (STOREFRONT_WIDTH, 0.14, 0.10)),
    ))
    add_part(
        asset, materials, "Storefront Frame",
        merged_boxes(frame_boxes, chamfer=0.008),
        "exterior_metal", "ExteriorMetal", bp.rgb(0.64, 0.67, 0.62),
        group="frontage",
    )

    kick_boxes = (
        ((-3.425, 0.34, 7.38), (1.41, 0.36, 0.10)),
        ((-1.875, 0.34, 7.38), (1.41, 0.36, 0.10)),
        ((1.875, 0.34, 7.38), (1.41, 0.36, 0.10)),
        ((3.425, 0.34, 7.38), (1.41, 0.36, 0.10)),
    )
    add_part(
        asset, materials, "Storefront Kick Panels",
        merged_boxes(kick_boxes, chamfer=0.006),
        "exterior_metal", "ExteriorMetal", bp.rgb(0.43, 0.47, 0.43),
        group="frontage",
    )

    reveal_boxes = (
        ((-4.19, 2.18, 7.49), (0.14, 4.36, 0.40)),
        ((4.19, 2.18, 7.49), (0.14, 4.36, 0.40)),
        ((0.0, 4.12, 7.49), (8.40, 0.18, 0.40)),
        ((-1.00, 1.26, 7.49), (0.10, 2.52, 0.36)),
        ((1.00, 1.26, 7.49), (0.10, 2.52, 0.36)),
        ((0.0, 2.58, 7.49), (1.90, 0.12, 0.36)),
    )
    add_part(
        asset, materials, "Storefront Recess And Door Reveals",
        merged_boxes(reveal_boxes, chamfer=0.006),
        "exterior_metal", "ExteriorMetal", bp.rgb(0.58, 0.60, 0.54),
        group="frontage",
    )

    # The frame describes the unchanged 1.90 x 2.40 m gameplay-facing door.
    door_frame_boxes = (
        ((-0.91, 1.32, 7.285), (0.08, DOOR_HEIGHT, 0.10)),
        ((0.91, 1.32, 7.285), (0.08, DOOR_HEIGHT, 0.10)),
        ((0.0, 0.16, 7.285), (DOOR_WIDTH, 0.08, 0.10)),
        ((0.0, 2.48, 7.285), (DOOR_WIDTH, 0.08, 0.10)),
        ((0.0, 1.32, 7.285), (0.07, DOOR_HEIGHT, 0.10)),
    )
    add_part(
        asset, materials, "Double Door Frame",
        merged_boxes(door_frame_boxes, chamfer=0.006),
        "exterior_door_frame", "ExteriorMetal", bp.rgb(0.66, 0.68, 0.62),
        group="frontage",
    )
    door_glass = (
        ((-0.475, 1.32, 7.235), (0.79, 2.20, 0.045)),
        ((0.475, 1.32, 7.235), (0.79, 2.20, 0.045)),
        ((0.0, 3.20, 7.235), (1.88, 1.14, 0.045)),
    )
    add_part(
        asset, materials, "Double Door And Transom Glass",
        merged_boxes(door_glass, chamfer=0.003),
        "exterior_door", "ExteriorGlass", bp.rgb(0.62, 0.75, 0.70),
        group="frontage", casts_shadows=False,
    )
    handles = (
        ((-0.22, 1.28, 7.33), (0.045, 0.62, 0.055)),
        ((0.22, 1.28, 7.33), (0.045, 0.62, 0.055)),
        ((-0.29, 1.58, 7.31), (0.18, 0.045, 0.055)),
        ((0.29, 1.58, 7.31), (0.18, 0.045, 0.055)),
    )
    add_part(
        asset, materials, "Double Door Pulls",
        merged_boxes(handles, chamfer=0.006),
        "exterior_metal", "ExteriorMetal", bp.rgb(0.78, 0.76, 0.66),
        group="frontage",
    )
    add_part(
        asset, materials, "Worn Entrance Threshold",
        bp.u_box((0.0, 0.055, 7.49), (2.15, 0.11, 0.48), 0.012),
        "exterior_metal", "ExteriorMetal", bp.rgb(0.34, 0.35, 0.32),
        group="frontage",
    )
    add_part(
        asset, materials, "Old Entrance Mat",
        bp.u_box((0.0, 0.025, 7.17), (1.72, 0.05, 0.46), 0.008),
        "exterior_dressing", "ExteriorMat", bp.rgb(0.20, 0.20, 0.17),
        group="frontage", casts_shadows=False,
    )

    # Exactly 9.20 m wide.  It is shallow because the unchanged door anchor
    # remains on the 15.5 m lot face; the glazing itself supplies the recess.
    add_part(
        asset, materials, "Storefront Canopy",
        bp.u_box((0.0, 4.18, 7.55), (CANOPY_WIDTH, 0.16, 0.40), 0.014),
        "exterior_metal", "ExteriorMetal", bp.rgb(0.76, 0.74, 0.66),
        group="frontage",
    )


def build_interior_proxies(asset: AssetBuild, materials: dict[str, "bpy.types.Material"]) -> None:
    closure_boxes = (
        ((0.0, 2.05, 5.18), (8.05, 3.90, 0.12)),
        ((-4.03, 2.05, 6.18), (0.12, 3.90, 2.12)),
        ((4.03, 2.05, 6.18), (0.12, 3.90, 2.12)),
        ((0.0, 3.98, 6.18), (8.05, 0.12, 2.12)),
        ((0.0, 0.11, 6.18), (8.05, 0.12, 2.12)),
    )
    add_part(
        asset, materials, "Storefront Interior Shadow Box",
        merged_boxes(closure_boxes, chamfer=0.008),
        "exterior_interior", "ExteriorInteriorDark", bp.rgb(0.16, 0.18, 0.16),
        group="interior_proxy",
    )

    shelf_boxes: list[tuple[Sequence[float], Sequence[float]]] = []
    for x in (-2.75, 2.75):
        shelf_boxes.extend((
            ((x - 1.18, 1.38, 5.62), (0.09, 2.55, 0.72)),
            ((x + 1.18, 1.38, 5.62), (0.09, 2.55, 0.72)),
        ))
        for y in (0.36, 1.02, 1.69, 2.36):
            shelf_boxes.append(((x, y, 5.62), (2.27, 0.08, 0.72)))
    add_part(
        asset, materials, "Dark Shelf Silhouettes",
        merged_boxes(shelf_boxes, chamfer=0.006),
        "exterior_interior", "ExteriorInteriorDark", bp.rgb(0.20, 0.22, 0.19),
        group="interior_proxy",
    )

    light_boxes = (
        ((-2.85, 3.91, 6.14), (1.55, 0.035, 0.10)),
        ((-0.95, 3.91, 6.14), (1.30, 0.035, 0.10)),
        ((0.95, 3.91, 6.14), (1.30, 0.035, 0.10)),
        ((2.85, 3.91, 6.14), (1.55, 0.035, 0.10)),
    )
    add_part(
        asset, materials, "Interior Fluorescent Strips",
        merged_boxes(light_boxes, chamfer=0.003),
        "exterior_interior_light", "ExteriorInteriorLight",
        bp.rgb(0.94, 0.74, 0.43), group="interior_proxy",
        emissive=True, casts_shadows=False,
    )


def glyph_geometry(
    letter: str,
    x: float,
    y: float,
    z: float,
    width: float,
    height: float,
    depth: float,
) -> kit.Geometry:
    stroke = 0.075
    half_w = width * 0.5
    half_h = height * 0.5

    def bar(dx: float, dy: float, sx: float, sy: float, angle: float = 0.0) -> kit.Geometry:
        center = (x + dx, y + dy, z)
        geometry = bp.u_box(center, (sx, sy, depth), 0.003)
        return bp.u_rotated_about(geometry, (0.0, 0.0, angle), center) if angle else geometry

    full_v = height
    full_h = width
    top_y = half_h - stroke * 0.5
    bottom_y = -half_h + stroke * 0.5
    left_x = -half_w + stroke * 0.5
    right_x = half_w - stroke * 0.5
    items: list[kit.Geometry]
    if letter == "П":
        items = [bar(left_x, 0, stroke, full_v), bar(right_x, 0, stroke, full_v), bar(0, top_y, full_h, stroke)]
    elif letter == "Р":
        items = [bar(left_x, 0, stroke, full_v), bar(0, top_y, full_h, stroke), bar(0, 0.02, full_h, stroke), bar(right_x, height * 0.25, stroke, height * 0.48)]
    elif letter == "О":
        items = [bar(left_x, 0, stroke, full_v), bar(right_x, 0, stroke, full_v), bar(0, top_y, full_h, stroke), bar(0, bottom_y, full_h, stroke)]
    elif letter == "Д":
        items = [bar(left_x + 0.035, 0.015, stroke, height * 0.82), bar(right_x - 0.035, 0.015, stroke, height * 0.82), bar(0, top_y, width * 0.76, stroke), bar(0, bottom_y + 0.035, full_h, stroke), bar(-half_w + 0.025, bottom_y - 0.045, stroke, 0.16), bar(half_w - 0.025, bottom_y - 0.045, stroke, 0.16)]
    elif letter == "У":
        items = [bar(-0.095, height * 0.25, stroke, height * 0.52, -28.0), bar(0.095, height * 0.25, stroke, height * 0.52, 28.0), bar(0.0, -height * 0.23, stroke, height * 0.56, -8.0)]
    elif letter == "К":
        items = [bar(left_x, 0, stroke, full_v), bar(0.055, height * 0.20, stroke, height * 0.58, -42.0), bar(0.055, -height * 0.20, stroke, height * 0.58, 42.0)]
    elif letter == "Т":
        items = [bar(0, top_y, full_h, stroke), bar(0, -stroke * 0.18, stroke, height - stroke)]
    elif letter == "Ы":
        items = [bar(left_x, 0, stroke, full_v), bar(-0.03, bottom_y, width * 0.62, stroke), bar(-0.03, -0.02, width * 0.62, stroke), bar(width * 0.12, -height * 0.25, stroke, height * 0.48), bar(right_x, 0, stroke, full_v)]
    else:
        raise RuntimeError(f"No deterministic glyph recipe for {letter!r}")
    return kit.merge_all(items)


def build_signs(asset: AssetBuild, materials: dict[str, "bpy.types.Material"]) -> None:
    add_part(
        asset, materials, "Main Shop Sign Field",
        bp.u_box((0.0, 5.03, 7.66), (6.05, 0.90, 0.08), 0.012),
        "exterior_sign_housing", "ExteriorSignHousing",
        bp.rgb(0.13, 0.29, 0.23), group="signage",
    )

    word = "ПРОДУКТЫ"
    widths = {letter: (0.52 if letter == "Ы" else 0.46) for letter in word}
    spacing = 0.17
    total = sum(widths[letter] for letter in word) + spacing * (len(word) - 1)
    cursor = -total * 0.5
    glyphs: list[kit.Geometry] = []
    for letter in word:
        width = widths[letter]
        center_x = cursor + width * 0.5
        glyphs.append(glyph_geometry(letter, center_x, 5.03, 7.74, width, 0.50, 0.02))
        cursor += width + spacing
    lettering = mirror_unity_x(kit.merge_all(glyphs))
    add_part(
        asset, materials, "Authored ПРОДУКТЫ Lettering",
        lettering,
        "exterior_sign_glow", "ExteriorSignGlow",
        bp.rgb(0.92, 0.68, 0.28), group="signage",
        emissive=True, casts_shadows=False,
    )

    blade_housing = (
        ((4.42, 4.90, 7.29), (0.12, 1.12, 0.80)),
        ((4.31, 4.90, 7.29), (0.10, 0.13, 0.88)),
        ((4.25, 5.35, 7.35), (0.22, 0.10, 0.10)),
        ((4.25, 4.45, 7.35), (0.22, 0.10, 0.10)),
    )
    add_part(
        asset, materials, "Two Sided Blade Sign Housing",
        merged_boxes(blade_housing, chamfer=0.010),
        "exterior_sign_housing", "ExteriorSignHousing",
        bp.rgb(0.19, 0.32, 0.25), group="signage",
    )


def build_service_and_roof(asset: AssetBuild, materials: dict[str, "bpy.types.Material"]) -> None:
    door_panel = (
        ((2.80, 1.22, -7.64), (1.42, 2.42, 0.06)),
        ((2.80, 0.23, -7.69), (1.35, 0.34, 0.05)),
    )
    add_part(
        asset, materials, "Closed Rear Service Door",
        merged_boxes(door_panel, chamfer=0.010),
        "exterior_service", "ExteriorMetal", bp.rgb(0.32, 0.36, 0.32),
        group="service",
    )
    service_frame = (
        ((2.065, 1.28, -7.715), (0.08, 2.56, 0.05)),
        ((3.535, 1.28, -7.715), (0.08, 2.56, 0.05)),
        ((2.80, 2.52, -7.715), (1.55, 0.08, 0.05)),
        ((3.30, 1.20, -7.715), (0.06, 0.28, 0.05)),
    )
    add_part(
        asset, materials, "Rear Service Door Frame And Pull",
        merged_boxes(service_frame, chamfer=0.005),
        "exterior_service", "ExteriorMetal", bp.rgb(0.61, 0.61, 0.54),
        group="service",
    )

    add_part(
        asset, materials, "Rear Louver Housing",
        bp.u_box((-2.65, 2.70, -7.69), (2.05, 1.36, 0.04), 0.006),
        "exterior_service", "ExteriorMetal", bp.rgb(0.28, 0.33, 0.31),
        group="service",
    )
    slats = [
        ((-2.65, 2.18 + index * 0.17, -7.735), (1.86, 0.07, 0.03))
        for index in range(7)
    ]
    add_part(
        asset, materials, "Rear Service Louver Slats",
        merged_boxes(slats, chamfer=0.004),
        "exterior_service", "ExteriorMetal", bp.rgb(0.52, 0.55, 0.50),
        group="service",
    )

    pipes = kit.merge_all((
        bp.u_cylinder((-7.705, 2.42, -5.72), (0.09, 2.18, 0.09), sides=10),
        bp.u_cylinder((7.705, 2.42, -5.72), (0.09, 2.18, 0.09), sides=10),
        bp.u_box((-7.705, 4.55, -5.57), (0.09, 0.12, 0.38), 0.006),
        bp.u_box((7.705, 4.55, -5.57), (0.09, 0.12, 0.38), 0.006),
    ))
    add_part(
        asset, materials, "Rear Corner Downpipes",
        pipes, "exterior_service", "ExteriorMetal", bp.rgb(0.55, 0.56, 0.51),
        group="service",
    )

    rooftop_cases = (
        ((-2.75, 5.91, -1.60), (2.30, 0.62, 1.42)),
        ((2.15, 5.86, 1.25), (1.82, 0.52, 1.18)),
        ((0.10, 5.82, 3.20), (1.25, 0.40, 0.90)),
    )
    add_part(
        asset, materials, "Rooftop Refrigeration Cases",
        merged_boxes(rooftop_cases, chamfer=0.035),
        "exterior_roof_equipment", "ExteriorMetal", bp.rgb(0.49, 0.52, 0.48),
        group="roof",
    )
    fan_geometry = kit.merge_all((
        bp.u_cylinder((-2.75, 6.27, -1.60), (0.82, 0.055, 0.82), sides=12),
        bp.u_cylinder((2.15, 6.17, 1.25), (0.66, 0.050, 0.66), sides=12),
    ))
    add_part(
        asset, materials, "Rooftop Fan Guards",
        fan_geometry, "exterior_roof_equipment", "ExteriorMetal",
        bp.rgb(0.42, 0.45, 0.42), group="roof",
    )
    exhaust = kit.merge_all((
        bp.u_cylinder((4.65, 6.085, -2.70), (0.24, 0.285, 0.24), sides=10),
        bp.u_cylinder((4.65, 6.385, -2.70), (0.36, 0.015, 0.36), sides=10),
    ))
    add_part(
        asset, materials, "Rooftop Exhaust Stack",
        exhaust, "exterior_roof_equipment", "ExteriorMetal",
        bp.rgb(0.48, 0.49, 0.45), group="roof",
    )

    add_part(
        asset, materials, "Old Front Bin",
        bp.u_box((5.05, 0.46, 7.34), (0.62, 0.92, 0.52), 0.035),
        "exterior_dressing", "ExteriorMetal", bp.rgb(0.24, 0.29, 0.26),
        group="frontage",
    )


def configure_scene() -> tuple["bpy.types.Collection", "bpy.types.Object"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene["bp_generator"] = "tools/build-supermarket-exterior-3d-model.py"
    scene["bp_generator_version"] = GENERATOR_VERSION
    scene["bp_design_id"] = DESIGN_ID
    scene["bp_source_forward"] = "+Y"
    source = bpy.data.collections.new("SOURCE_SupermarketExterior3D")
    scene.collection.children.link(source)
    root = bpy.data.objects.new("ROOT_SupermarketExterior3D", None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.65
    source.objects.link(root)
    return source, root


def build() -> AssetBuild:
    collection, root = configure_scene()
    materials = {sheet: create_material(sheet) for sheet in ALLOWED_SHEETS}
    asset = AssetBuild(root, collection)
    build_shell(asset, materials)
    build_storefront(asset, materials)
    build_interior_proxies(asset, materials)
    build_signs(asset, materials)
    build_service_and_roof(asset, materials)
    add_anchor(asset, "ExteriorDoor", "exterior_door", DOOR_ANCHOR)
    return asset


def validate(asset: AssetBuild) -> dict:
    problems: list[str] = []
    names: set[str] = set()
    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate part name '{part.name}'")
        names.add(part.name)
        if part.sheet not in ALLOWED_SHEETS:
            problems.append(f"'{part.name}' has unsupported sheet '{part.sheet}'")
        if bp.signed_volume(part.geometry) <= 0.0:
            problems.append(f"'{part.name}' has inverted or open authored winding")

    required_roles = {
        "exterior_wall", "exterior_fascia", "exterior_masonry",
        "exterior_roof", "exterior_metal", "exterior_glass",
        "exterior_door", "exterior_interior", "exterior_interior_light",
        "exterior_sign_housing", "exterior_sign_glow", "exterior_service",
        "exterior_roof_equipment", "exterior_dressing",
    }
    roles = {part.role for part in asset.parts}
    for role in sorted(required_roles - roles):
        problems.append(f"the exterior has no '{role}' geometry")
    used_sheets = {part.sheet for part in asset.parts}
    for sheet in sorted(ALLOWED_SHEETS - used_sheets):
        problems.append(f"the exterior never uses required sheet '{sheet}'")

    merged = kit.merge_all(part.geometry for part in asset.parts)
    low, high = kit.bounds(merged)
    expected_low = (-HALF_WIDTH, -HALF_DEPTH, 0.0)
    expected_high = (HALF_WIDTH, HALF_DEPTH, HEIGHT)
    for axis, label in enumerate(("source X / Unity X", "source Y / Unity Z", "source Z / Unity Y")):
        if abs(low[axis] - expected_low[axis]) > 0.001 or abs(high[axis] - expected_high[axis]) > 0.001:
            problems.append(
                f"{label} bounds are {low[axis]:.4f}..{high[axis]:.4f}, "
                f"expected {expected_low[axis]:.4f}..{expected_high[axis]:.4f}"
            )

    anchor = asset.anchors.get("ExteriorDoor")
    if anchor is None:
        problems.append("the exterior_door anchor is missing")
    elif tuple(stable(value) for value in anchor.location) != (0.0, 7.75, 0.0):
        problems.append(f"the exterior_door source anchor is {tuple(anchor.location)}")

    frame = next((part for part in asset.parts if part.name == "Storefront Frame"), None)
    canopy = next((part for part in asset.parts if part.name == "Storefront Canopy"), None)
    door_frame = next((part for part in asset.parts if part.name == "Double Door Frame"), None)
    if frame is None or abs((kit.bounds(frame.geometry)[1][0] - kit.bounds(frame.geometry)[0][0]) - STOREFRONT_WIDTH) > 0.001:
        problems.append("the authored storefront no longer spans exactly 8.40 m")
    if canopy is None or abs((kit.bounds(canopy.geometry)[1][0] - kit.bounds(canopy.geometry)[0][0]) - CANOPY_WIDTH) > 0.001:
        problems.append("the authored canopy no longer spans exactly 9.20 m")
    if door_frame is None:
        problems.append("the centred double-door frame is missing")
    else:
        door_low, door_high = kit.bounds(door_frame.geometry)
        if abs(door_high[0] - door_low[0] - DOOR_WIDTH) > 0.001:
            problems.append("the authored door no longer spans exactly 1.90 m")
        if abs(door_high[2] - door_low[2] - DOOR_HEIGHT) > 0.001:
            problems.append("the authored door no longer rises exactly 2.40 m")

    sign_panel_front = 7.66 + 0.08 * 0.5
    glyph_back = 7.74 - 0.02 * 0.5
    if glyph_back - sign_panel_front + 1e-6 < OPAQUE_OVERLAY_CLEARANCE:
        problems.append("sign glyphs violate the 0.03 m opaque-overlay clearance")
    if len(asset.parts) > 64:
        problems.append(f"the exterior fragments into {len(asset.parts)} meshes")
    triangles = kit.triangle_count(merged)
    if triangles > 12000:
        problems.append(f"the exterior costs {triangles} triangles against its 12000 cap")

    # Reuse the ordinary-building scanner that guards the same City camera.
    # Each authored mesh name becomes an audit role so intentional tiny rail
    # interlocks can be distinguished from competing surfaces across parts.
    validate_coplanarity_audit_contract()
    audited_parts: list[city_parts.PartSpec] = []
    for part in asset.parts:
        if part.sheet in {"ExteriorGlass", "ExteriorInteriorLight"}:
            continue
        geometry = city_parts.Geometry(
            tuple(tuple(value for value in vertex) for vertex in part.geometry[0]),
            tuple(tuple(index for index in face) for face in part.geometry[1]),
            (0,) * len(part.geometry[1]),
        )
        audited_parts.append(city_parts.PartSpec(
            part.name,
            part.name,
            part.sheet,
            "supermarket_authored_uv0",
            SHEET_PITCH[part.sheet],
            geometry,
        ))
    audit = city_parts.PrototypeSpec(
        DESIGN_ID,
        "Residential",
        "NeighbourhoodSupermarket",
        WIDTH,
        DEPTH,
        HEIGHT,
        (0.0, HALF_DEPTH, 0.0),
        (-4.5, -4.5, 5.5),
        (4.5, 4.5, HEIGHT),
        (),
        (),
        tuple(audited_parts),
    )

    def faces_outward(axis: int, coordinate: float, normal_sign: int) -> bool:
        if axis == 2:
            return normal_sign > 0
        # For the exterior shell, the origin is inside the building. A face
        # whose normal points back toward it is a buried back-face, not a
        # competing presentation layer. The same rule keeps rear service
        # frame backs out while retaining its street-visible negative face.
        return coordinate * normal_sign > 0.0

    for overlap in find_axis_aligned_coplanar_overlaps(audit):
        if overlap.has_opposing_normals:
            continue
        if not faces_outward(
                overlap.plane_axis,
                overlap.plane_coordinate,
                overlap.first_normal_sign):
            continue
        # Chamfered rails and wall returns meet through tiny edge keys. The
        # 0.02 m2 ceiling is far below the shared audit's 0.05 m2 broad-layer
        # threshold, while still rejecting every panel-sized competing face.
        tiny_edge_interlock = overlap.area < 0.02
        if tiny_edge_interlock:
            continue
        problems.append(
            "visible coplanar overlap: "
            f"{overlap.first_role}[{overlap.first_face_index}] / "
            f"{overlap.second_role}[{overlap.second_face_index}] on source "
            f"axis {overlap.plane_axis} at {overlap.plane_coordinate:.5f} "
            f"({overlap.area:.5f} m2)"
        )
    for overlap in find_near_coplanar_visible_overlaps(audit):
        if not faces_outward(
                overlap.plane_axis,
                overlap.first_plane_coordinate,
                overlap.normal_sign) or not faces_outward(
                    overlap.plane_axis,
                    overlap.second_plane_coordinate,
                    overlap.normal_sign):
            continue
        tiny_edge_interlock = overlap.area < 0.02
        if tiny_edge_interlock:
            continue
        problems.append(
            "near-coplanar opaque overlap: "
            f"{overlap.first_role}[{overlap.first_face_index}] / "
            f"{overlap.second_role}[{overlap.second_face_index}] on source "
            f"axis {overlap.plane_axis}, {overlap.separation:.5f} m apart "
            f"({overlap.area:.5f} m2)"
        )

    if problems:
        raise SystemExit("Supermarket exterior validation failed:\n  " + "\n  ".join(problems))
    return {
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
        "mesh_count": len(asset.parts),
        "triangle_count": triangles,
    }


def signature_for(asset: AssetBuild) -> str:
    payload = {
        "design_id": DESIGN_ID,
        "generator_version": GENERATOR_VERSION,
        "dimensions": [WIDTH, DEPTH, HEIGHT],
        "storefront_width": STOREFRONT_WIDTH,
        "canopy_width": CANOPY_WIDTH,
        "door": [DOOR_WIDTH, DOOR_HEIGHT],
        "parts": [
            {
                "name": part.name,
                "role": part.role,
                "group": part.group,
                "sheet": part.sheet,
                "emissive": part.emissive,
                "casts_shadows": part.casts_shadows,
                "atlas_side": part.atlas_side,
                "tint": part.tint,
                "vertices": [[stable(value) for value in vertex] for vertex in part.geometry[0]],
                "faces": [list(face) for face in part.geometry[1]],
            }
            for part in asset.parts
        ],
        "anchors": {
            name: [stable(value) for value in anchor.location]
            for name, anchor in sorted(asset.anchors.items())
        },
    }
    encoded = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def manifest_part(part: Part) -> dict:
    low, high = kit.bounds(part.geometry)
    return {
        "name": part.name,
        "role": part.role,
        "group": part.group,
        "sheet": part.sheet,
        "emissive": part.emissive,
        "casts_shadows": part.casts_shadows,
        "shadows": part.casts_shadows,
        "tint": part.tint,
        "colliders": [],
        "vertices": len(part.geometry[0]),
        "triangles": kit.triangle_count(part.geometry),
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
    }


def manifest_for(asset: AssetBuild, report: dict, signature: str) -> dict:
    return {
        "generator": "tools/build-supermarket-exterior-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "dimensions_m": {"width": WIDTH, "depth": DEPTH, "height": HEIGHT},
        "storefront_width_m": STOREFRONT_WIDTH,
        "canopy_width_m": CANOPY_WIDTH,
        "door_opening_m": {"width": DOOR_WIDTH, "height": DOOR_HEIGHT},
        "authored_text": ["ПРОДУКТЫ"],
        "brand_marks": False,
        "source_axes": {"right": "+X", "forward": "+Y", "up": "+Z"},
        "unity_axes": {
            "right": "+X", "forward": "+Z", "up": "+Y",
            "fbx_axis_forward": "-Z", "fbx_axis_up": "Y",
            "bake_space_transform": False,
        },
        "root_contract": {
            "origin": "footprint_center_ground",
            "scale_mode": "fixed_meters",
            "source_forward_axis": "+Y",
            "unity_forward_axis": "+Z",
            "axis_conversion": "swap_y_z_and_reverse_winding",
        },
        "runtime_wrapper_yaw_degrees": 0.0,
        "colliders": False,
        "lights": False,
        "cameras": False,
        "animation_count": 0,
        "surface_clearance_contract": {
            "opaque_overlay_min_clearance_m": OPAQUE_OVERLAY_CLEARANCE,
            "runtime_foundation_inset_m": RUNTIME_FOUNDATION_INSET,
            "fascia_bands": "authored_uv_atlas_no_overlay_geometry",
        },
        "yard_spotlight_mount_zones": [
            {"side": "left", "unity_center": [-7.67, 3.5, 0.0], "clear_width_m": 2.4, "clear_height_m": 1.4},
            {"side": "right", "unity_center": [7.67, 3.5, 0.0], "clear_width_m": 2.4, "clear_height_m": 1.4},
        ],
        "bounds_min": report["bounds_min"],
        "bounds_max": report["bounds_max"],
        "mesh_count": report["mesh_count"],
        "triangle_count": report["triangle_count"],
        "anchors": [
            {
                "name": name,
                "role": anchor["bp_role"],
                "local_position": [stable(value) for value in anchor.location],
                "unity_local_position": [
                    stable(anchor.location.x),
                    stable(anchor.location.z),
                    stable(anchor.location.y),
                ],
            }
            for name, anchor in sorted(asset.anchors.items())
        ],
        "parts": [manifest_part(part) for part in asset.parts],
        "build_signature": signature,
    }


def write_manifest(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def export_fbx(asset: AssetBuild, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in asset.collection.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = asset.root
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,
        add_leaf_bones=False,
        bake_anim=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_custom_props=True,
        path_mode="STRIP",
        embed_textures=False,
    )


def render_preview(asset: AssetBuild, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.new("PRESENTATION_SupermarketExterior3D")
    scene.collection.children.link(presentation)

    ground_mesh = bpy.data.meshes.new("PreviewGround_Mesh")
    ground_geometry = kit.box((0.0, 0.0, -0.18), (34.0, 34.0, 0.30))
    ground_mesh.from_pydata(*ground_geometry[:1], [], ground_geometry[1])
    ground = bpy.data.objects.new("PreviewGround", ground_mesh)
    presentation.objects.link(ground)
    ground_material = bpy.data.materials.new("PREVIEW_SupermarketGround")
    ground_material.diffuse_color = (0.055, 0.065, 0.062, 1.0)
    ground.data.materials.append(ground_material)

    for name, location, target, energy, colour, size in (
        ("Key", (-15.0, 21.0, 19.0), (0.0, 1.0, 2.8), 2100, (0.72, 0.82, 0.77), 9.0),
        ("Front", (12.0, 18.0, 9.0), (0.0, 5.0, 2.6), 1500, (0.96, 0.58, 0.31), 7.0),
        ("Rim", (11.0, -13.0, 13.0), (0.0, 0.0, 3.2), 1900, (0.32, 0.47, 0.56), 8.0),
    ):
        light_data = bpy.data.lights.new(f"PREVIEW_{name}", "AREA")
        light_data.energy = energy
        light_data.color = colour
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(f"PREVIEW_{name}", light_data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y").to_euler()

    camera_data = bpy.data.cameras.new("PREVIEW_SupermarketCamera")
    camera = bpy.data.objects.new("PREVIEW_SupermarketCamera", camera_data)
    presentation.objects.link(camera)
    camera.location = (18.5, 23.0, 11.8)
    target = Vector((0.0, 1.2, 2.75))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 50
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("PREVIEW_SupermarketWorld")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.025, 0.031, 0.032, 1.0)
        background.inputs["Strength"].default_value = 0.35
    scene.world = world
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)

    scene.camera = None
    for obj in list(presentation.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(presentation)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blend", type=Path, default=DEFAULT_BLEND)
    parser.add_argument("--fbx", type=Path, default=DEFAULT_FBX)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--preview", type=Path, default=DEFAULT_PREVIEW)
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    effective = argv[argv.index("--") + 1:] if "--" in argv else []
    return parser.parse_args(effective)


def main() -> int:
    args = parse_args(list(sys.argv))
    asset = build()
    report = validate(asset)
    signature = signature_for(asset)
    if args.validate_only:
        print(
            "SUPERMARKET EXTERIOR 3D VALID: "
            f"{report['mesh_count']} meshes / {report['triangle_count']} triangles, "
            f"signature {signature[:16]}"
        )
        return 0
    payload = manifest_for(asset, report, signature)
    write_manifest(args.manifest, payload)
    export_fbx(asset, args.fbx)
    if not args.no_preview:
        render_preview(asset, args.preview)
    save_blend(args.blend)
    print(
        "Supermarket exterior written: "
        f"{report['mesh_count']} meshes / {report['triangle_count']} triangles, "
        f"signature {signature[:16]}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
