#!/usr/bin/env python3
"""Build the authored ``player_home_exterior_v1`` house exterior.

The model translates the restrained Georgian 209-1 housing type into the
game's fixed player-home envelope: a compact two-storey rendered block, a
real pitched roof, a deep supported gallery, a recessed ground entrance and
the hero's upper half-loggia.  The architectural balcony is aligned to the
existing gameplay contract at tangent ``-1.45 m``, ``3.90 x 2.30 m`` and
floor elevation ``4.70 m``.

All authoring dimensions are Unity-local metres with +Z facing the street.
``bar_parts.to_source`` swaps Unity Y/Z and reverses winding, yielding Blender
source +Y forward and +Z up.  The exported FBX therefore arrives in Unity
with +Z forward without a wrapper rotation.

The asset is passive.  It contains no collider, trigger, light, camera or
animation.  Unity may keep those gameplay-owned objects and align the model
through the source-space door anchor at ``(0, 6, 0)``.

Run from the repository root with Blender 5::

    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-player-home-exterior-3d-model.py -- --validate-only
    blender --background --factory-startup --python-exit-code 1 --python \
      tools/build-player-home-exterior-3d-model.py
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Sequence

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

GENERATOR_VERSION = "1.1.0"
DESIGN_ID = "player_home_exterior_v1"
DISPLAY_NAME = "Bar Promenade Player Home Exterior"
ROOT_OBJECT_NAME = DESIGN_ID

WIDTH = 13.0
DEPTH = 12.0
HEIGHT = 8.8
HALF_WIDTH = WIDTH * 0.5
HALF_DEPTH = DEPTH * 0.5
WALL_HALF_WIDTH = 6.25
WALL_THICKNESS = 0.28
WALL_FRONT_Y = HALF_DEPTH - WALL_THICKNESS * 0.5
WALL_REAR_Y = -HALF_DEPTH + WALL_THICKNESS
WALL_EAVE = 6.82
ROOF_EAVE = 7.0
ROOF_RIDGE = HEIGHT
BALCONY_CENTER_X = -1.45
BALCONY_WIDTH = 3.90
BALCONY_DEPTH = 2.30
BALCONY_FLOOR = 4.70
BALCONY_SLAB_THICKNESS = 0.18
BALCONY_BACK_Y = HALF_DEPTH
BALCONY_OUTER_Y = BALCONY_BACK_Y + BALCONY_DEPTH
BALCONY_LEFT = BALCONY_CENTER_X - BALCONY_WIDTH * 0.5
BALCONY_RIGHT = BALCONY_CENTER_X + BALCONY_WIDTH * 0.5
BALCONY_DOOR_CENTER_X = -0.50
BALCONY_DOOR_WIDTH = 1.36
BALCONY_DOOR_HEIGHT = 2.34
BALCONY_WINDOW_CENTER_X = -2.35
BALCONY_WINDOW_CENTER_Z = BALCONY_FLOOR + 2.08
BALCONY_WINDOW_WIDTH = 1.48
BALCONY_WINDOW_HEIGHT = 1.10
ENTRY_LEFT = -1.0
ENTRY_RIGHT = 0.8
ENTRY_REAR_Y = 4.96
ENTRY_TOP = 3.20
ENTRY_DOOR_WIDTH = 1.15
ENTRY_DOOR_HEIGHT = 2.30
DOOR_ANCHOR_SOURCE = (0.0, HALF_DEPTH, 0.0)
OPAQUE_OVERLAY_CLEARANCE = 0.03
RUNTIME_FOUNDATION_INSET = 0.08
UV_EPSILON = 1e-6
DEFAULT_BLEND = (
    ROOT / "ArtSource" / "PlayerHome" / "Blender" /
    "player_home_exterior_v1.blend"
)
DEFAULT_PREVIEW = (
    ROOT / "ArtSource" / "PlayerHome" / "Blender" /
    "player_home_exterior_v1.png"
)
DEFAULT_FBX = (
    ROOT / "Assets" / "PlayerHome" / "Models" /
    "PlayerHomeExterior3D.fbx"
)
DEFAULT_MANIFEST = (
    ROOT / "Assets" / "PlayerHome" / "Models" /
    "PlayerHomeExterior3D.json"
)
TEXTURE_BASENAMES = {
    "StuccoPrimary": "PlayerHomeExteriorStuccoPrimaryAlbedo",
    "StuccoRepair": "PlayerHomeExteriorStuccoRepairAlbedo",
    "BrickPlinth": "PlayerHomeExteriorBrickPlinthAlbedo",
    "RoofSlate": "PlayerHomeExteriorRoofSlateAlbedo",
    "PaintedWood": "PlayerHomeExteriorPaintedWoodAlbedo",
    "PaintedMetal": "PlayerHomeExteriorPaintedMetalAlbedo",
    "WindowFrame": "PlayerHomeExteriorWindowFrameAlbedo",
    "WindowGlass": "PlayerHomeExteriorWindowGlassAlbedo",
    "Concrete": "PlayerHomeExteriorConcreteAlbedo",
}
TEXTURE_PATHS = {
    sheet: (
        ROOT / "Assets" / "Resources" / "PlayerHome" /
        "ExteriorTextures" / f"{basename}.png"
    )
    for sheet, basename in TEXTURE_BASENAMES.items()
}
SHEET_PITCH = {
    "StuccoPrimary": 2.4,
    "StuccoRepair": 1.8,
    "BrickPlinth": 1.2,
    "RoofSlate": 2.4,
    "PaintedWood": 1.0,
    "PaintedMetal": 1.2,
    "WindowFrame": 0.8,
    "WindowGlass": 1.0,
    "Concrete": 1.5,
}
PREVIEW_COLORS = {
    "StuccoPrimary": (0.49, 0.50, 0.44, 1.0),
    "StuccoRepair": (0.58, 0.56, 0.48, 1.0),
    "BrickPlinth": (0.26, 0.16, 0.13, 1.0),
    "RoofSlate": (0.12, 0.15, 0.17, 1.0),
    "PaintedWood": (0.23, 0.34, 0.30, 1.0),
    "PaintedMetal": (0.24, 0.29, 0.28, 1.0),
    "WindowFrame": (0.66, 0.65, 0.56, 1.0),
    "WindowGlass": (0.14, 0.23, 0.25, 0.52),
    "Concrete": (0.39, 0.40, 0.38, 1.0),
}
ALLOWED_SHEETS = tuple(SHEET_PITCH)
REQUIRED_GROUPS = {
    "frontage", "side_left", "side_right", "rear",
    "roof", "balcony", "entry",
}
HOME_VIEW_GROUPS = ("frontage", "balcony", "entry")
LIT_FRONT_WINDOW_PART_NAME = "Front Lit Window Glass"

@dataclass(frozen=True)
class WindowSpec:
    center: float
    center_z: float
    width: float
    height: float

    @property
    def opening(self) -> tuple[float, float, float, float]:
        return (
            self.center - self.width * 0.5,
            self.center + self.width * 0.5,
            self.center_z - self.height * 0.5,
            self.center_z + self.height * 0.5,
        )


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


LIT_FRONT_WINDOW = WindowSpec(2.15, 5.36, 1.45, 1.55)
FRONT_WINDOWS = (
    WindowSpec(-5.10, 1.90, 1.35, 1.60),
    WindowSpec(2.15, 1.90, 1.45, 1.60),
    WindowSpec(4.75, 1.90, 1.35, 1.60),
    WindowSpec(-5.10, 5.36, 1.35, 1.55),
    LIT_FRONT_WINDOW,
    WindowSpec(4.75, 5.36, 1.35, 1.55),
)
SIDE_WINDOWS = tuple(
    WindowSpec(forward, height, 1.35, 1.55)
    for height in (1.90, 5.36)
    for forward in (-3.55, -0.40, 2.80)
)
REAR_WINDOWS = tuple(
    WindowSpec(horizontal, height, 1.35, 1.55)
    for height in (1.90, 5.36)
    for horizontal in (-4.70, -1.60, 1.60, 4.70)
)


def stable(value: float) -> float:
    return round(float(value), 6)


def merged(items: Iterable[kit.Geometry]) -> kit.Geometry:
    return kit.merge_all(tuple(items))


def u_prism_x(
    profile_height_forward: Sequence[tuple[float, float]],
    x0: float,
    x1: float,
) -> kit.Geometry:
    """Extrude a counter-clockwise Unity Y/Z profile along local X."""
    if x1 <= x0 or len(profile_height_forward) < 3:
        raise ValueError("A prism requires increasing X and a closed profile.")
    count = len(profile_height_forward)
    vertices = [
        (x0, height, forward)
        for height, forward in profile_height_forward
    ]
    vertices.extend(
        (x1, height, forward)
        for height, forward in profile_height_forward
    )
    faces: list[tuple[int, ...]] = [
        tuple(reversed(range(count))),
        tuple(range(count, count * 2)),
    ]
    for index in range(count):
        following = (index + 1) % count
        faces.append((
            following,
            count + following,
            count + index,
            index,
        ))
    geometry = vertices, faces
    if bp.signed_volume(geometry) <= 0.0:
        raise RuntimeError("u_prism_x produced inverted winding.")
    return geometry


def merge_intervals(
    intervals: Sequence[tuple[float, float]],
) -> list[tuple[float, float]]:
    result: list[tuple[float, float]] = []
    for low, high in sorted(intervals):
        if not result or low > result[-1][1] + 1e-8:
            result.append((low, high))
        else:
            result[-1] = (result[-1][0], max(result[-1][1], high))
    return result


def rectangular_wall_boxes(
    side: str,
    coordinate: float,
    horizontal_min: float,
    horizontal_max: float,
    bottom: float,
    top: float,
    thickness: float,
    openings: Sequence[tuple[float, float, float, float]],
) -> list[kit.Geometry]:
    """Partition an elevation into closed boxes around true openings."""
    clipped = [
        (
            max(horizontal_min, left),
            min(horizontal_max, right),
            max(bottom, low),
            min(top, high),
        )
        for left, right, low, high in openings
        if right > horizontal_min and left < horizontal_max
        and high > bottom and low < top
    ]
    vertical_edges = {bottom, top}
    for _, _, low, high in clipped:
        vertical_edges.add(low)
        vertical_edges.add(high)
    edges = sorted(vertical_edges)
    boxes: list[kit.Geometry] = []
    for low, high in zip(edges, edges[1:]):
        if high - low <= 1e-6:
            continue
        middle = (low + high) * 0.5
        voids = merge_intervals([
            (left, right)
            for left, right, opening_low, opening_high in clipped
            if opening_low - 1e-8 <= middle <= opening_high + 1e-8
        ])
        cursor = horizontal_min
        solids: list[tuple[float, float]] = []
        for void_low, void_high in voids:
            if void_low > cursor + 1e-6:
                solids.append((cursor, void_low))
            cursor = max(cursor, void_high)
        if cursor < horizontal_max - 1e-6:
            solids.append((cursor, horizontal_max))
        for solid_low, solid_high in solids:
            span = solid_high - solid_low
            center = (solid_low + solid_high) * 0.5
            vertical_center = (low + high) * 0.5
            vertical_span = high - low
            if side in {"front", "rear"}:
                boxes.append(kit.box(
                    (center, vertical_center, coordinate),
                    (span, vertical_span, thickness),
                ))
            elif side in {"left", "right"}:
                boxes.append(kit.box(
                    (coordinate, vertical_center, center),
                    (thickness, vertical_span, span),
                ))
            else:
                raise ValueError(f"Unknown wall side {side!r}.")
    return boxes


def wall_patch(
    side: str,
    coordinate: float,
    bounds: tuple[float, float, float, float],
) -> kit.Geometry:
    left, right, bottom, top = bounds
    if side in {"front", "rear"}:
        center = ((left + right) * 0.5, (bottom + top) * 0.5, coordinate)
        size = (right - left, top - bottom, WALL_THICKNESS)
    else:
        center = (coordinate, (bottom + top) * 0.5, (left + right) * 0.5)
        size = (WALL_THICKNESS, top - bottom, right - left)
    return kit.box(center, size)


def create_material(sheet: str) -> "bpy.types.Material":
    material = bpy.data.materials.new(f"PREVIEW_PlayerHome_{sheet}")
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
            rough_input.default_value = (
                0.18 if sheet == "WindowGlass" else 0.76
            )
        if metallic_input is not None:
            metallic_input.default_value = (
                0.24 if sheet == "PaintedMetal" else 0.0
            )
        if alpha_input is not None:
            alpha_input.default_value = PREVIEW_COLORS[sheet][3]
        texture_path = TEXTURE_PATHS[sheet]
        if texture_path.exists():
            image = bpy.data.images.load(str(texture_path), check_existing=True)
            texture = material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image = image
            texture.interpolation = "Linear"
            texture.extension = "REPEAT"
            if base_input is not None:
                material.node_tree.links.new(texture.outputs["Color"], base_input)
    if sheet == "WindowGlass":
        if hasattr(material, "surface_render_method"):
            material.surface_render_method = "DITHERED"
        if hasattr(material, "use_transparency_overlap"):
            material.use_transparency_overlap = False
    return material


def create_lit_window_material() -> "bpy.types.Material":
    """Make the one warm pane legible in the deterministic Blender preview."""
    material = create_material("WindowGlass")
    material.name = "PREVIEW_PlayerHome_WindowGlass_Lit"
    material.diffuse_color = (0.95, 0.62, 0.29, 1.0)
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        emission_input = (
            bsdf.inputs.get("Emission Color") or
            bsdf.inputs.get("Emission")
        )
        if emission_input is not None:
            emission_input.default_value = (0.95, 0.42, 0.12, 1.0)
        strength_input = bsdf.inputs.get("Emission Strength")
        if strength_input is not None:
            strength_input.default_value = 2.6
        alpha_input = bsdf.inputs.get("Alpha")
        if alpha_input is not None:
            alpha_input.default_value = 1.0
    material["bp_sheet"] = "WindowGlass"
    material["bp_emissive"] = True
    return material


def assign_uv(mesh: "bpy.types.Mesh", sheet: str) -> None:
    """World-metre projection keeps every elevation and trim at one density."""
    layer = mesh.uv_layers.new(name="UVMap")
    pitch = SHEET_PITCH[sheet]
    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        for loop_index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if axis == 0:
                uv = (point.y / pitch, point.z / pitch)
            elif axis == 1:
                uv = (point.x / pitch, point.z / pitch)
            else:
                uv = (point.x / pitch, point.y / pitch)
            layer.data[loop_index].uv = uv


def add_part(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
    name: str,
    unity_geometry: kit.Geometry,
    role: str,
    sheet: str,
    tint: dict,
    *,
    group: str,
    emissive: bool = False,
    casts_shadows: bool = True,
) -> "bpy.types.Object":
    if sheet not in ALLOWED_SHEETS:
        raise SystemExit(f"Part '{name}' names unsupported sheet '{sheet}'.")
    source_geometry = bp.to_source(unity_geometry)
    vertices, faces = source_geometry
    if not vertices or not faces:
        raise SystemExit(f"Part '{name}' is empty.")
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    material_key = (
        "WindowGlassLit"
        if sheet == "WindowGlass" and emissive
        else sheet
    )
    mesh.materials.append(materials[material_key])
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = asset.root
    obj["bp_role"] = role
    obj["bp_group"] = group
    obj["bp_sheet"] = sheet
    obj["bp_emissive"] = emissive
    obj["bp_casts_shadows"] = casts_shadows
    obj["bp_uv_scheme"] = "world_metre_projected"
    part = Part(
        obj, name, role, group, sheet, emissive, casts_shadows,
        tint, source_geometry,
    )
    asset.parts.append(part)
    assign_uv(mesh, sheet)
    return obj


def add_anchor_source(
    asset: AssetBuild,
    name: str,
    role: str,
    source_position: Sequence[float],
) -> None:
    anchor = bpy.data.objects.new(f"ANCHOR_{name}", None)
    asset.collection.objects.link(anchor)
    anchor.parent = asset.root
    anchor.empty_display_type = "ARROWS"
    anchor.empty_display_size = 0.5
    anchor.location = tuple(float(value) for value in source_position)
    anchor["bp_role"] = role
    asset.anchors[name] = anchor


def window_geometry(
    side: str,
    wall_coordinate: float,
    specs: Sequence[WindowSpec],
) -> tuple[kit.Geometry, kit.Geometry, kit.Geometry]:
    frames: list[kit.Geometry] = []
    glass: list[kit.Geometry] = []
    sills: list[kit.Geometry] = []
    outward = 1.0 if side in {"front", "right"} else -1.0
    wall_outer = wall_coordinate + outward * WALL_THICKNESS * 0.5
    glass_plane = wall_outer + outward * 0.022
    frame_plane = wall_outer + outward * 0.060
    # Sills stay inside the fixed roof envelope.  Their front face remains
    # visibly proud of the wall, but not by the old primitive's 0.245 m.
    sill_plane = wall_outer + outward * 0.020
    frame_depth = 0.075
    glass_depth = 0.028
    bar = 0.11
    for spec in specs:
        vertical_span = spec.height + bar * 0.12
        if side in {"front", "rear"}:
            for horizontal in (
                spec.center - spec.width * 0.5 + bar * 0.5,
                spec.center + spec.width * 0.5 - bar * 0.5,
                spec.center,
            ):
                frames.append(bp.u_box(
                    (horizontal, spec.center_z, frame_plane),
                    (bar if horizontal != spec.center else bar * 0.72,
                     vertical_span, frame_depth),
                    0.005,
                ))
            for vertical in (
                spec.center_z - spec.height * 0.5 + bar * 0.5,
                spec.center_z + spec.height * 0.5 - bar * 0.5,
            ):
                frames.append(bp.u_box(
                    (spec.center, vertical, frame_plane),
                    (spec.width, bar, frame_depth),
                    0.005,
                ))
            glass.append(bp.u_box(
                (spec.center, spec.center_z, glass_plane),
                (spec.width - bar * 1.35,
                 spec.height - bar * 1.35,
                 glass_depth),
                0.002,
            ))
            sills.append(bp.u_box(
                (spec.center,
                 spec.center_z - spec.height * 0.5 - 0.090,
                 sill_plane),
                (spec.width + 0.24, 0.12, 0.16),
                0.008,
            ))
        else:
            for horizontal in (
                spec.center - spec.width * 0.5 + bar * 0.5,
                spec.center + spec.width * 0.5 - bar * 0.5,
                spec.center,
            ):
                frames.append(bp.u_box(
                    (frame_plane, spec.center_z, horizontal),
                    (frame_depth,
                     vertical_span,
                     bar if horizontal != spec.center else bar * 0.72),
                    0.005,
                ))
            for vertical in (
                spec.center_z - spec.height * 0.5 + bar * 0.5,
                spec.center_z + spec.height * 0.5 - bar * 0.5,
            ):
                frames.append(bp.u_box(
                    (frame_plane, vertical, spec.center),
                    (frame_depth, bar, spec.width),
                    0.005,
                ))
            glass.append(bp.u_box(
                (glass_plane, spec.center_z, spec.center),
                (glass_depth,
                 spec.height - bar * 1.35,
                 spec.width - bar * 1.35),
                0.002,
            ))
            sills.append(bp.u_box(
                (sill_plane,
                 spec.center_z - spec.height * 0.5 - 0.090,
                 spec.center),
                (0.16, 0.12, spec.width + 0.24),
                0.008,
            ))
    return merged(frames), merged(glass), merged(sills)


def build_shell(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
) -> None:
    front_repair = (3.45, 4.45, 3.28, 4.28)
    front_openings = [spec.opening for spec in FRONT_WINDOWS]
    front_openings.extend((
        (ENTRY_LEFT, ENTRY_RIGHT, 0.45, ENTRY_TOP),
        (BALCONY_LEFT, BALCONY_RIGHT,
         BALCONY_FLOOR - BALCONY_SLAB_THICKNESS, WALL_EAVE),
        front_repair,
    ))
    add_part(
        asset, materials, "Front Primary Stucco",
        merged(rectangular_wall_boxes(
            "front", WALL_FRONT_Y,
            -WALL_HALF_WIDTH, WALL_HALF_WIDTH,
            0.45, WALL_EAVE, WALL_THICKNESS, front_openings,
        )),
        "exterior_stucco_primary", "StuccoPrimary",
        bp.rgb(0.49, 0.50, 0.44), group="frontage",
    )
    add_part(
        asset, materials, "Front Stucco Repair",
        wall_patch("front", WALL_FRONT_Y, front_repair),
        "exterior_stucco_repair", "StuccoRepair",
        bp.rgb(0.58, 0.56, 0.48), group="frontage",
    )

    side_repairs = {
        "left": (4.08, 5.18, 3.20, 4.24),
        "right": (-5.08, -4.00, 3.18, 4.20),
    }
    for side, coordinate, group in (
        ("left", -WALL_HALF_WIDTH, "side_left"),
        ("right", WALL_HALF_WIDTH, "side_right"),
    ):
        repair = side_repairs[side]
        openings = [spec.opening for spec in SIDE_WINDOWS]
        openings.append(repair)
        add_part(
            asset, materials,
            f"{'Left' if side == 'left' else 'Right'} Primary Stucco",
            merged(rectangular_wall_boxes(
                side, coordinate,
                WALL_REAR_Y, WALL_FRONT_Y,
                0.45, WALL_EAVE, WALL_THICKNESS, openings,
            )),
            "exterior_stucco_primary", "StuccoPrimary",
            bp.rgb(0.47, 0.49, 0.44), group=group,
        )
        add_part(
            asset, materials,
            f"{'Left' if side == 'left' else 'Right'} Stucco Repair",
            wall_patch(side, coordinate, repair),
            "exterior_stucco_repair", "StuccoRepair",
            bp.rgb(0.56, 0.55, 0.49), group=group,
        )

    rear_repair = (-0.68, 0.92, 3.18, 4.30)
    rear_openings = [spec.opening for spec in REAR_WINDOWS]
    rear_openings.append(rear_repair)
    add_part(
        asset, materials, "Rear Primary Stucco",
        merged(rectangular_wall_boxes(
            "rear", WALL_REAR_Y,
            -WALL_HALF_WIDTH, WALL_HALF_WIDTH,
            0.45, WALL_EAVE, WALL_THICKNESS, rear_openings,
        )),
        "exterior_stucco_primary", "StuccoPrimary",
        bp.rgb(0.46, 0.48, 0.43), group="rear",
    )
    add_part(
        asset, materials, "Rear Stucco Repair",
        wall_patch("rear", WALL_REAR_Y, rear_repair),
        "exterior_stucco_repair", "StuccoRepair",
        bp.rgb(0.55, 0.54, 0.48), group="rear",
    )

    front_gable_top = 8.58 - (
        (8.58 - 6.82) * WALL_FRONT_Y / BALCONY_OUTER_Y
    )
    gable_profile = (
        (WALL_EAVE, WALL_REAR_Y),
        (8.54, 0.0),
        (front_gable_top, WALL_FRONT_Y),
        (WALL_EAVE, WALL_FRONT_Y),
    )
    for label, x0, x1, group in (
        ("Left", -6.39, -6.11, "side_left"),
        ("Right", 6.11, 6.39, "side_right"),
    ):
        add_part(
            asset, materials, f"{label} Stucco Gable",
            u_prism_x(gable_profile, x0, x1),
            "exterior_stucco_primary", "StuccoPrimary",
            bp.rgb(0.47, 0.49, 0.44), group=group,
        )


def build_plinth_and_roof(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
) -> None:
    front_plinth = (
        ((-3.695, 0.325, 6.07), (5.39, 0.65, 0.14)),
        ((3.595, 0.325, 6.07), (5.59, 0.65, 0.14)),
    )
    add_part(
        asset, materials, "Front Brick Plinth",
        merged(bp.u_box(center, size, 0.004) for center, size in front_plinth),
        "exterior_masonry", "BrickPlinth",
        bp.rgb(0.26, 0.16, 0.13), group="frontage",
    )
    add_part(
        asset, materials, "Left Brick Plinth",
        bp.u_box((-6.43, 0.325, 0.0), (0.14, 0.65, 11.72), 0.004),
        "exterior_masonry", "BrickPlinth",
        bp.rgb(0.25, 0.15, 0.13), group="side_left",
    )
    add_part(
        asset, materials, "Right Brick Plinth",
        bp.u_box((6.43, 0.325, 0.0), (0.14, 0.65, 11.72), 0.004),
        "exterior_masonry", "BrickPlinth",
        bp.rgb(0.25, 0.15, 0.13), group="side_right",
    )
    add_part(
        asset, materials, "Rear Brick Plinth",
        bp.u_box((0.0, 0.325, -5.93), (12.72, 0.65, 0.14), 0.004),
        "exterior_masonry", "BrickPlinth",
        bp.rgb(0.24, 0.15, 0.13), group="rear",
    )

    front_roof = u_prism_x((
        (8.58, 0.0),
        (ROOF_RIDGE, 0.0),
        (ROOF_EAVE, BALCONY_OUTER_Y),
        (6.82, BALCONY_OUTER_Y),
    ), -HALF_WIDTH, HALF_WIDTH)
    rear_roof = u_prism_x((
        (6.82, -HALF_DEPTH),
        (ROOF_EAVE, -HALF_DEPTH),
        (ROOF_RIDGE, 0.0),
        (8.58, 0.0),
    ), -HALF_WIDTH, HALF_WIDTH)
    add_part(
        asset, materials, "Pitched Slate Roof",
        merged((front_roof, rear_roof)),
        "exterior_roof", "RoofSlate",
        bp.rgb(0.12, 0.15, 0.17), group="roof",
    )
    add_part(
        asset, materials, "Front Eave Fascia",
        bp.u_box((0.0, 6.875, BALCONY_OUTER_Y - 0.16),
                 (12.96, 0.18, 0.20), 0.006),
        "exterior_wood", "PaintedWood",
        bp.rgb(0.23, 0.34, 0.30), group="frontage",
    )
    add_part(
        asset, materials, "Rear Eave Fascia",
        bp.u_box((0.0, 6.875, -5.84), (12.96, 0.18, 0.20), 0.006),
        "exterior_wood", "PaintedWood",
        bp.rgb(0.22, 0.32, 0.29), group="roof",
    )


def add_window_elevation(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
    label: str,
    side: str,
    coordinate: float,
    specs: Sequence[WindowSpec],
    group: str,
    lit_spec: WindowSpec | None = None,
) -> None:
    frames, glass, sills = window_geometry(side, coordinate, specs)
    add_part(
        asset, materials, f"{label} Window Frames", frames,
        "exterior_window_frame", "WindowFrame",
        bp.rgb(0.66, 0.65, 0.56), group=group,
    )
    if lit_spec is None:
        add_part(
            asset, materials, f"{label} Window Glass", glass,
            "exterior_glass", "WindowGlass",
            bp.rgb(0.14, 0.23, 0.25), group=group,
            casts_shadows=False,
        )
    else:
        if lit_spec not in specs:
            raise SystemExit(
                f"The lit {label.lower()} window is absent from its elevation."
            )
        dark_specs = tuple(spec for spec in specs if spec != lit_spec)
        _, dark_glass, _ = window_geometry(side, coordinate, dark_specs)
        _, lit_glass, _ = window_geometry(side, coordinate, (lit_spec,))
        add_part(
            asset, materials, f"{label} Window Glass", dark_glass,
            "exterior_glass", "WindowGlass",
            bp.rgb(0.14, 0.23, 0.25), group=group,
            casts_shadows=False,
        )
        add_part(
            asset, materials, LIT_FRONT_WINDOW_PART_NAME, lit_glass,
            "exterior_glass", "WindowGlass",
            bp.rgb(0.95, 0.62, 0.29), group=group,
            emissive=True, casts_shadows=False,
        )
    add_part(
        asset, materials, f"{label} Concrete Sills", sills,
        "exterior_concrete", "Concrete",
        bp.rgb(0.39, 0.40, 0.38), group=group,
    )


def build_windows(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
) -> None:
    add_window_elevation(
        asset, materials, "Front", "front", WALL_FRONT_Y,
        FRONT_WINDOWS, "frontage", LIT_FRONT_WINDOW,
    )
    add_window_elevation(
        asset, materials, "Left", "left", -WALL_HALF_WIDTH,
        SIDE_WINDOWS, "side_left",
    )
    add_window_elevation(
        asset, materials, "Right", "right", WALL_HALF_WIDTH,
        SIDE_WINDOWS, "side_right",
    )
    add_window_elevation(
        asset, materials, "Rear", "rear", WALL_REAR_Y,
        REAR_WINDOWS, "rear",
    )


def build_entry(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
) -> None:
    reveal_front = WALL_FRONT_Y + WALL_THICKNESS * 0.5 - 0.06
    reveal_depth = reveal_front - ENTRY_REAR_Y
    reveal_center = (reveal_front + ENTRY_REAR_Y) * 0.5
    soffit_bottom = ENTRY_TOP - 0.14
    reveals = (
        ((ENTRY_LEFT, soffit_bottom * 0.5, reveal_center),
         (0.14, soffit_bottom, reveal_depth)),
        ((ENTRY_RIGHT, soffit_bottom * 0.5, reveal_center),
         (0.14, soffit_bottom, reveal_depth)),
    )
    add_part(
        asset, materials, "Recessed Entry Reveals",
        merged(bp.u_box(center, size, 0.006) for center, size in reveals),
        "exterior_stucco_repair", "StuccoRepair",
        bp.rgb(0.55, 0.54, 0.48), group="entry",
    )
    add_part(
        asset, materials, "Recessed Entry Soffit",
        bp.u_box((
            (ENTRY_LEFT + ENTRY_RIGHT) * 0.5,
            ENTRY_TOP - 0.07,
            reveal_center,
        ), (
            ENTRY_RIGHT - ENTRY_LEFT,
            0.14,
            reveal_depth,
        ), 0.006),
        "exterior_concrete", "Concrete",
        bp.rgb(0.38, 0.39, 0.37), group="entry",
    )

    side_width = ((ENTRY_RIGHT - ENTRY_LEFT) - ENTRY_DOOR_WIDTH) * 0.5
    rear_wall = (
        ((ENTRY_LEFT + side_width * 0.5, soffit_bottom * 0.5,
          ENTRY_REAR_Y),
         (side_width, soffit_bottom, 0.16)),
        ((ENTRY_RIGHT - side_width * 0.5, soffit_bottom * 0.5,
          ENTRY_REAR_Y),
         (side_width, soffit_bottom, 0.16)),
        (((ENTRY_LEFT + ENTRY_RIGHT) * 0.5,
          (ENTRY_DOOR_HEIGHT + soffit_bottom) * 0.5,
          ENTRY_REAR_Y),
         (ENTRY_DOOR_WIDTH,
          soffit_bottom - ENTRY_DOOR_HEIGHT,
          0.16)),
    )
    add_part(
        asset, materials, "Entry Recess Back Wall",
        merged(bp.u_box(center, size, 0.004) for center, size in rear_wall),
        "exterior_stucco_primary", "StuccoPrimary",
        bp.rgb(0.48, 0.49, 0.44), group="entry",
    )

    door_center = (ENTRY_LEFT + ENTRY_RIGHT) * 0.5
    door_plane = ENTRY_REAR_Y + 0.095
    add_part(
        asset, materials, "Player Home Entrance Door",
        bp.u_box((door_center, ENTRY_DOOR_HEIGHT * 0.5, door_plane),
                 (ENTRY_DOOR_WIDTH, ENTRY_DOOR_HEIGHT, 0.10), 0.010),
        "exterior_door", "PaintedWood",
        bp.rgb(0.20, 0.30, 0.27), group="entry",
    )
    frame_bar = 0.12
    door_frame = (
        ((door_center - ENTRY_DOOR_WIDTH * 0.5 - frame_bar * 0.5,
          (ENTRY_DOOR_HEIGHT + frame_bar * 2.0) * 0.5,
          door_plane + 0.065),
         (frame_bar, ENTRY_DOOR_HEIGHT + frame_bar * 2.0, 0.08)),
        ((door_center + ENTRY_DOOR_WIDTH * 0.5 + frame_bar * 0.5,
          (ENTRY_DOOR_HEIGHT + frame_bar * 2.0) * 0.5,
          door_plane + 0.065),
         (frame_bar, ENTRY_DOOR_HEIGHT + frame_bar * 2.0, 0.08)),
        ((door_center, ENTRY_DOOR_HEIGHT + frame_bar * 0.5,
          door_plane + 0.065),
         (ENTRY_DOOR_WIDTH + frame_bar * 2.0, frame_bar, 0.08)),
    )
    add_part(
        asset, materials, "Player Home Entrance Door Frame",
        merged(bp.u_box(center, size, 0.005) for center, size in door_frame),
        "exterior_window_frame", "WindowFrame",
        bp.rgb(0.64, 0.63, 0.55), group="entry",
    )
    add_part(
        asset, materials, "Player Home Entrance Threshold",
        bp.u_box((door_center, 0.06, 5.47),
                 (ENTRY_DOOR_WIDTH + 0.30, 0.12, 1.06), 0.008),
        "exterior_concrete", "Concrete",
        bp.rgb(0.35, 0.36, 0.35), group="entry",
    )


def build_balcony(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
) -> None:
    balcony_door = (
        BALCONY_DOOR_CENTER_X - BALCONY_DOOR_WIDTH * 0.5,
        BALCONY_DOOR_CENTER_X + BALCONY_DOOR_WIDTH * 0.5,
        BALCONY_FLOOR,
        BALCONY_FLOOR + BALCONY_DOOR_HEIGHT,
    )
    balcony_window = (
        BALCONY_WINDOW_CENTER_X - BALCONY_WINDOW_WIDTH * 0.5,
        BALCONY_WINDOW_CENTER_X + BALCONY_WINDOW_WIDTH * 0.5,
        BALCONY_WINDOW_CENTER_Z - BALCONY_WINDOW_HEIGHT * 0.5,
        BALCONY_WINDOW_CENTER_Z + BALCONY_WINDOW_HEIGHT * 0.5,
    )
    recess_wall_coordinate = BALCONY_BACK_Y - WALL_THICKNESS * 0.5
    add_part(
        asset, materials, "Upper Half Loggia Back Wall",
        merged(rectangular_wall_boxes(
            "front", recess_wall_coordinate,
            BALCONY_LEFT, BALCONY_RIGHT,
            BALCONY_FLOOR, 7.44, WALL_THICKNESS,
            (balcony_door, balcony_window),
        )),
        "exterior_stucco_primary", "StuccoPrimary",
        bp.rgb(0.47, 0.49, 0.44), group="balcony",
    )
    add_part(
        asset, materials, "Upper Half Loggia Concrete Deck",
        bp.u_box((
            BALCONY_CENTER_X,
            BALCONY_FLOOR - BALCONY_SLAB_THICKNESS * 0.5,
            BALCONY_BACK_Y + BALCONY_DEPTH * 0.5,
        ), (
            BALCONY_WIDTH,
            BALCONY_SLAB_THICKNESS,
            BALCONY_DEPTH,
        ), 0.008),
        "exterior_concrete", "Concrete",
        bp.rgb(0.38, 0.39, 0.37), group="balcony",
    )

    balcony_window_spec = WindowSpec(
        BALCONY_WINDOW_CENTER_X,
        BALCONY_WINDOW_CENTER_Z,
        BALCONY_WINDOW_WIDTH,
        BALCONY_WINDOW_HEIGHT,
    )
    frames, glass, sills = window_geometry(
        "front", recess_wall_coordinate, (balcony_window_spec,)
    )
    add_part(
        asset, materials, "Balcony Window Frame", frames,
        "exterior_window_frame", "WindowFrame",
        bp.rgb(0.66, 0.65, 0.56), group="balcony",
    )
    add_part(
        asset, materials, "Balcony Window Glass", glass,
        "exterior_glass", "WindowGlass",
        bp.rgb(0.14, 0.23, 0.25), group="balcony",
        casts_shadows=False,
    )
    add_part(
        asset, materials, "Balcony Window Concrete Sill", sills,
        "exterior_concrete", "Concrete",
        bp.rgb(0.39, 0.40, 0.38), group="balcony",
    )

    door_outer_y = BALCONY_BACK_Y + 0.06
    door_glass_height = 1.44
    door_glass_center = BALCONY_FLOOR + 1.43
    door_wood = (
        ((BALCONY_DOOR_CENTER_X, BALCONY_FLOOR + 0.38, door_outer_y),
         (BALCONY_DOOR_WIDTH - 0.28, 0.76, 0.09)),
        ((BALCONY_DOOR_CENTER_X - BALCONY_DOOR_WIDTH * 0.5 + 0.07,
          BALCONY_FLOOR + BALCONY_DOOR_HEIGHT * 0.5, door_outer_y + 0.04),
         (0.14, BALCONY_DOOR_HEIGHT, 0.08)),
        ((BALCONY_DOOR_CENTER_X + BALCONY_DOOR_WIDTH * 0.5 - 0.07,
          BALCONY_FLOOR + BALCONY_DOOR_HEIGHT * 0.5, door_outer_y + 0.04),
         (0.14, BALCONY_DOOR_HEIGHT, 0.08)),
        ((BALCONY_DOOR_CENTER_X,
          BALCONY_FLOOR + BALCONY_DOOR_HEIGHT - 0.07,
          door_outer_y + 0.04),
         (BALCONY_DOOR_WIDTH - 0.28, 0.14, 0.08)),
        ((BALCONY_DOOR_CENTER_X, BALCONY_FLOOR + 0.82,
          door_outer_y + 0.04),
         (BALCONY_DOOR_WIDTH - 0.28, 0.14, 0.08)),
    )
    add_part(
        asset, materials, "Balcony Painted Wood Door",
        merged(bp.u_box(center, size, 0.005) for center, size in door_wood),
        "exterior_balcony_door", "PaintedWood",
        bp.rgb(0.22, 0.32, 0.28), group="balcony",
    )
    add_part(
        asset, materials, "Balcony Door Glass",
        bp.u_box((BALCONY_DOOR_CENTER_X, door_glass_center,
                  door_outer_y + 0.012),
                 (BALCONY_DOOR_WIDTH - 0.28,
                  door_glass_height, 0.028), 0.002),
        "exterior_glass", "WindowGlass",
        bp.rgb(0.14, 0.23, 0.25), group="balcony",
        casts_shadows=False,
    )

    gallery_y = BALCONY_OUTER_Y - 0.26
    gallery_posts = [
        bp.u_box((x, 3.48, gallery_y), (0.12, 6.84, 0.12), 0.006)
        for x in (BALCONY_LEFT + 0.08, BALCONY_CENTER_X, BALCONY_RIGHT - 0.08)
    ]
    gallery_posts.extend((
        bp.u_box((BALCONY_CENTER_X, 6.78, gallery_y),
                 (BALCONY_WIDTH, 0.16, 0.14), 0.006),
        bp.u_box((BALCONY_LEFT + 0.08, 6.36,
                  BALCONY_BACK_Y + BALCONY_DEPTH * 0.50),
                 (0.12, 0.16, BALCONY_DEPTH - 0.20), 0.006),
        bp.u_box((BALCONY_RIGHT - 0.08, 6.36,
                  BALCONY_BACK_Y + BALCONY_DEPTH * 0.50),
                 (0.12, 0.16, BALCONY_DEPTH - 0.20), 0.006),
    ))
    add_part(
        asset, materials, "Two Storey Gallery Timber",
        merged(gallery_posts),
        "exterior_wood", "PaintedWood",
        bp.rgb(0.23, 0.34, 0.30), group="balcony",
    )

    rail_height = 1.05
    rail_y = BALCONY_OUTER_Y - 0.10
    metal = [
        bp.u_box((BALCONY_CENTER_X,
                  BALCONY_FLOOR + rail_height - 0.045,
                  rail_y),
                 (BALCONY_WIDTH - 0.22, 0.09, 0.08), 0.004),
    ]
    for index in range(9):
        x = BALCONY_LEFT + 0.16 + index * (BALCONY_WIDTH - 0.32) / 8.0
        metal.append(bp.u_box((
            x,
            BALCONY_FLOOR + rail_height * 0.5,
            rail_y,
        ), (0.055, rail_height - 0.10, 0.055), 0.003))
    for side_x in (BALCONY_LEFT + 0.08, BALCONY_RIGHT - 0.08):
        metal.append(bp.u_box((
            side_x,
            BALCONY_FLOOR + rail_height - 0.045,
            (BALCONY_BACK_Y + rail_y) * 0.5,
        ), (0.08, 0.09, rail_y - BALCONY_BACK_Y), 0.004))
        for index in range(4):
            forward = BALCONY_BACK_Y + 0.20 + index * (
                rail_y - BALCONY_BACK_Y - 0.40
            ) / 3.0
            metal.append(bp.u_box((
                side_x,
                BALCONY_FLOOR + rail_height * 0.5,
                forward,
            ), (0.055, rail_height - 0.10, 0.055), 0.003))
    add_part(
        asset, materials, "Upper Half Loggia Metal Railing",
        merged(metal),
        "exterior_metal", "PaintedMetal",
        bp.rgb(0.24, 0.29, 0.28), group="balcony",
    )


def build_rain_goods(
    asset: AssetBuild,
    materials: dict[str, "bpy.types.Material"],
) -> None:
    for label, x, group in (
        ("Left", -6.43, "side_left"),
        ("Right", 6.43, "side_right"),
    ):
        add_part(
            asset, materials, f"{label} Galvanized Downpipe",
            bp.u_cylinder((x, 3.28, -4.92), (0.10, 3.28, 0.10), sides=10),
            "exterior_metal", "PaintedMetal",
            bp.rgb(0.25, 0.30, 0.29), group=group,
        )


def configure_scene() -> tuple["bpy.types.Collection", "bpy.types.Object"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene["bp_generator"] = "tools/build-player-home-exterior-3d-model.py"
    scene["bp_generator_version"] = GENERATOR_VERSION
    scene["bp_design_id"] = DESIGN_ID
    scene["bp_source_forward"] = "+Y"
    source = bpy.data.collections.new("SOURCE_player_home_exterior_v1")
    scene.collection.children.link(source)
    root = bpy.data.objects.new(ROOT_OBJECT_NAME, None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.65
    source.objects.link(root)
    return source, root


def build() -> AssetBuild:
    collection, root = configure_scene()
    materials = {
        sheet: create_material(sheet) for sheet in ALLOWED_SHEETS
    }
    materials["WindowGlassLit"] = create_lit_window_material()
    asset = AssetBuild(root, collection)
    build_shell(asset, materials)
    build_plinth_and_roof(asset, materials)
    build_windows(asset, materials)
    build_entry(asset, materials)
    build_balcony(asset, materials)
    build_rain_goods(asset, materials)
    add_anchor_source(
        asset, "ExteriorDoor", "exterior_door", DOOR_ANCHOR_SOURCE
    )
    return asset


def validate_uv_density(part: Part, problems: list[str]) -> None:
    layer = part.obj.data.uv_layers.get("UVMap")
    if layer is None:
        problems.append(f"'{part.name}' has no authored UV0")
        return
    for polygon in part.obj.data.polygons:
        if polygon.area < 0.01:
            continue
        values = [layer.data[index].uv for index in polygon.loop_indices]
        span_u = max(value.x for value in values) - min(value.x for value in values)
        span_v = max(value.y for value in values) - min(value.y for value in values)
        if span_u <= UV_EPSILON or span_v <= UV_EPSILON:
            problems.append(
                f"'{part.name}' face {polygon.index} collapses a visible UV axis"
            )
            return
        if max(span_u, span_v) > 24.0:
            problems.append(
                f"'{part.name}' face {polygon.index} has implausible UV stretch"
            )
            return


def validate(asset: AssetBuild) -> dict:
    problems: list[str] = []
    names: set[str] = set()
    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate part name '{part.name}'")
        names.add(part.name)
        if part.sheet not in ALLOWED_SHEETS:
            problems.append(f"'{part.name}' has unsupported sheet '{part.sheet}'")
        if part.group not in REQUIRED_GROUPS:
            problems.append(f"'{part.name}' has unsupported group '{part.group}'")
        if bp.signed_volume(part.geometry) <= 0.0:
            problems.append(f"'{part.name}' has inverted or open authored winding")
        validate_uv_density(part, problems)

    required_roles = {
        "exterior_stucco_primary", "exterior_stucco_repair",
        "exterior_masonry", "exterior_roof", "exterior_wood",
        "exterior_metal", "exterior_window_frame", "exterior_glass",
        "exterior_concrete", "exterior_door", "exterior_balcony_door",
    }
    roles = {part.role for part in asset.parts}
    for role in sorted(required_roles - roles):
        problems.append(f"the exterior has no '{role}' geometry")
    used_sheets = {part.sheet for part in asset.parts}
    for sheet in sorted(set(ALLOWED_SHEETS) - used_sheets):
        problems.append(f"the exterior never uses required sheet '{sheet}'")
    groups = {part.group for part in asset.parts}
    for group in sorted(REQUIRED_GROUPS - groups):
        problems.append(f"the exterior never publishes group '{group}'")

    emissive_parts = [part for part in asset.parts if part.emissive]
    if len(emissive_parts) != 1:
        problems.append(
            "the exterior must publish exactly one emissive window-glass part"
        )
    else:
        lit_window = emissive_parts[0]
        if lit_window.name != LIT_FRONT_WINDOW_PART_NAME or \
                lit_window.role != "exterior_glass" or \
                lit_window.group != "frontage" or \
                lit_window.sheet != "WindowGlass" or \
                lit_window.casts_shadows:
            problems.append(
                "the only emissive part must be the non-shadow-casting "
                "frontage WindowGlass pane named 'Front Lit Window Glass'"
            )
        lit_low, lit_high = kit.bounds(lit_window.geometry)
        lit_center = tuple(
            (lit_low[axis] + lit_high[axis]) * 0.5 for axis in range(3)
        )
        expected_lit_center = (
            LIT_FRONT_WINDOW.center,
            HALF_DEPTH + 0.022,
            LIT_FRONT_WINDOW.center_z,
        )
        if any(
                abs(lit_center[axis] - expected_lit_center[axis]) > 0.001
                for axis in range(3)):
            problems.append(
                "the lit pane drifted from source center "
                "(+2.15, +6.022, +5.36) m"
            )
    if any(
            part.emissive
            for part in asset.parts
            if part.sheet == "WindowGlass" and
            part.name != LIT_FRONT_WINDOW_PART_NAME):
        problems.append("every other authored WindowGlass pane must remain dark")

    merged_geometry = merged(part.geometry for part in asset.parts)
    low, high = kit.bounds(merged_geometry)
    expected_low = (-HALF_WIDTH, -HALF_DEPTH, 0.0)
    expected_high = (HALF_WIDTH, BALCONY_OUTER_Y, HEIGHT)
    for axis, label in enumerate((
        "source X / Unity X",
        "source Y / Unity Z",
        "source Z / Unity Y",
    )):
        if abs(low[axis] - expected_low[axis]) > 0.001 or \
                abs(high[axis] - expected_high[axis]) > 0.001:
            problems.append(
                f"{label} bounds are {low[axis]:.4f}..{high[axis]:.4f}, "
                f"expected {expected_low[axis]:.4f}..{expected_high[axis]:.4f}"
            )

    anchor = asset.anchors.get("ExteriorDoor")
    if anchor is None:
        problems.append("the exterior_door anchor is missing")
    elif tuple(stable(value) for value in anchor.location) != \
            DOOR_ANCHOR_SOURCE:
        problems.append(
            f"the exterior_door source anchor is {tuple(anchor.location)}"
        )

    deck = next((
        part for part in asset.parts
        if part.name == "Upper Half Loggia Concrete Deck"
    ), None)
    if deck is None:
        problems.append("the authored upper half-loggia deck is missing")
    else:
        deck_low, deck_high = kit.bounds(deck.geometry)
        expected_deck_low = (
            BALCONY_LEFT,
            BALCONY_BACK_Y,
            BALCONY_FLOOR - BALCONY_SLAB_THICKNESS,
        )
        expected_deck_high = (
            BALCONY_RIGHT,
            BALCONY_OUTER_Y,
            BALCONY_FLOOR,
        )
        for axis in range(3):
            if abs(deck_low[axis] - expected_deck_low[axis]) > 0.001 or \
                    abs(deck_high[axis] - expected_deck_high[axis]) > 0.001:
                problems.append(
                    "the authored half-loggia drifted from "
                    "-1.45 / 3.90 x 2.30 / 4.70 m"
                )
                break

    roof = next((
        part for part in asset.parts if part.name == "Pitched Slate Roof"
    ), None)
    if roof is None:
        problems.append("the pitched roof is missing")
    else:
        roof_low, roof_high = kit.bounds(roof.geometry)
        if abs(roof_high[2] - ROOF_RIDGE) > 0.001:
            problems.append("the roof ridge no longer reaches exactly 8.80 m")
        rear_eave_vertices = [
            vertex for vertex in roof.geometry[0]
            if abs(vertex[1] + HALF_DEPTH) <= 0.001
        ]
        front_eave_vertices = [
            vertex for vertex in roof.geometry[0]
            if abs(vertex[1] - BALCONY_OUTER_Y) <= 0.001
        ]
        if not rear_eave_vertices or not front_eave_vertices or \
                max(vertex[2] for vertex in rear_eave_vertices) > \
                ROOF_EAVE + 0.001 or \
                max(vertex[2] for vertex in front_eave_vertices) > \
                ROOF_EAVE + 0.001:
            problems.append("the pitched roof eaves no longer hold at 7.00 m")
        if roof_low[0] != -HALF_WIDTH or roof_high[0] != HALF_WIDTH:
            problems.append("the pitched roof no longer fixes the 13 m width")

    if len(asset.parts) > 64:
        problems.append(f"the exterior fragments into {len(asset.parts)} meshes")
    triangles = kit.triangle_count(merged_geometry)
    if triangles > 16000:
        problems.append(
            f"the exterior costs {triangles} triangles against its 16000 cap"
        )

    validate_coplanarity_audit_contract()
    audited_parts: list[city_parts.PartSpec] = []
    for part in asset.parts:
        if part.sheet == "WindowGlass":
            continue
        geometry = city_parts.Geometry(
            tuple(tuple(value for value in vertex)
                  for vertex in part.geometry[0]),
            tuple(tuple(index for index in face)
                  for face in part.geometry[1]),
            (0,) * len(part.geometry[1]),
        )
        audited_parts.append(city_parts.PartSpec(
            part.name,
            part.name,
            part.sheet,
            "player_home_authored_uv0",
            SHEET_PITCH[part.sheet],
            geometry,
        ))
    audit = city_parts.PrototypeSpec(
        DESIGN_ID,
        "Residential",
        "Georgian209_1Adaptation",
        WIDTH,
        DEPTH,
        HEIGHT,
        DOOR_ANCHOR_SOURCE,
        (-HALF_WIDTH, -HALF_DEPTH, WALL_EAVE),
        (HALF_WIDTH, BALCONY_OUTER_Y, HEIGHT),
        (),
        (),
        tuple(audited_parts),
    )

    def faces_outward(axis: int, coordinate: float, normal_sign: int) -> bool:
        if axis == 2:
            return normal_sign > 0
        return coordinate * normal_sign > 0.0

    for overlap in find_axis_aligned_coplanar_overlaps(audit):
        if overlap.has_opposing_normals:
            continue
        if not faces_outward(
                overlap.plane_axis,
                overlap.plane_coordinate,
                overlap.first_normal_sign):
            continue
        if overlap.area < 0.02:
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
        if overlap.area < 0.02:
            continue
        problems.append(
            "near-coplanar opaque overlap: "
            f"{overlap.first_role}[{overlap.first_face_index}] / "
            f"{overlap.second_role}[{overlap.second_face_index}] on source "
            f"axis {overlap.plane_axis}, {overlap.separation:.5f} m apart "
            f"({overlap.area:.5f} m2)"
        )

    if problems:
        raise SystemExit(
            "Player-home exterior validation failed:\n  - "
            + "\n  - ".join(problems)
        )
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
        "roof": [ROOF_EAVE, ROOF_RIDGE],
        "balcony": [
            BALCONY_CENTER_X, BALCONY_WIDTH, BALCONY_DEPTH, BALCONY_FLOOR,
        ],
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
                "vertices": [
                    [stable(value) for value in vertex]
                    for vertex in part.geometry[0]
                ],
                "faces": [list(face) for face in part.geometry[1]],
            }
            for part in asset.parts
        ],
        "anchors": {
            name: [stable(value) for value in anchor.location]
            for name, anchor in sorted(asset.anchors.items())
        },
    }
    encoded = json.dumps(
        payload,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )
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
        "generator": "tools/build-player-home-exterior-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "dimensions_m": {
            "width": WIDTH,
            "depth": DEPTH,
            "height": HEIGHT,
        },
        "dimensions_scope": (
            "logical_body_envelope_excludes_authored_front_overhang"
        ),
        "visual_overhang_m": {
            "front": BALCONY_DEPTH,
            "rear": 0.0,
            "left": 0.0,
            "right": 0.0,
        },
        "door_opening_m": {
            "width": ENTRY_DOOR_WIDTH,
            "height": ENTRY_DOOR_HEIGHT,
        },
        "balcony_contract": {
            "center_tangent_m": BALCONY_CENTER_X,
            "width_m": BALCONY_WIDTH,
            "depth_m": BALCONY_DEPTH,
            "floor_elevation_m": BALCONY_FLOOR,
            "back_source_y_m": BALCONY_BACK_Y,
            "door_center_tangent_m": BALCONY_DOOR_CENTER_X,
            "window_center_tangent_m": BALCONY_WINDOW_CENTER_X,
        },
        "lit_window_contract": {
            "count": 1,
            "part_name": LIT_FRONT_WINDOW_PART_NAME,
            "sheet": "WindowGlass",
            "view": "upper_left_of_balcony_when_seen_from_street",
            "source_center_m": [
                LIT_FRONT_WINDOW.center,
                stable(HALF_DEPTH + 0.022),
                LIT_FRONT_WINDOW.center_z,
            ],
            "unity_local_center_m": [
                LIT_FRONT_WINDOW.center,
                LIT_FRONT_WINDOW.center_z,
                stable(HALF_DEPTH + 0.022),
            ],
            "all_other_window_glass_emissive": False,
        },
        "roof_contract": {
            "type": "pitched_gable",
            "eave_height_m": ROOF_EAVE,
            "ridge_height_m": ROOF_RIDGE,
            "front_eave_source_y_m": BALCONY_OUTER_Y,
            "rear_eave_source_y_m": -HALF_DEPTH,
        },
        "authored_text": [],
        "brand_marks": False,
        "source_axes": {"right": "+X", "forward": "+Y", "up": "+Z"},
        "unity_axes": {
            "right": "+X",
            "forward": "+Z",
            "up": "+Y",
            "fbx_axis_forward": "-Z",
            "fbx_axis_up": "Y",
            "bake_space_transform": False,
        },
        "root_contract": {
            "name": ROOT_OBJECT_NAME,
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
            "facade_uv": "authored_per_elevation_no_whole_building_stretch",
            "openings": "separate_geometry_not_baked",
            "home_view_groups": list(HOME_VIEW_GROUPS),
            "visible_bounds": (
                "body_plus_canonical_outward_balcony_and_roof_overhang"
            ),
        },
        "texture_bindings": [
            {
                "sheet": sheet,
                "basename": basename,
                "resource_path": (
                    f"PlayerHome/ExteriorTextures/{basename}"
                ),
                "uv_scheme": "world_metre_projected",
                "meters_per_tile": SHEET_PITCH[sheet],
            }
            for sheet, basename in TEXTURE_BASENAMES.items()
        ],
        "bounds_min": report["bounds_min"],
        "bounds_max": report["bounds_max"],
        "mesh_count": report["mesh_count"],
        "triangle_count": report["triangle_count"],
        "anchors": [
            {
                "name": name,
                "role": anchor["bp_role"],
                "local_position": [
                    stable(value) for value in anchor.location
                ],
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
    presentation = bpy.data.collections.new(
        "PRESENTATION_player_home_exterior_v1"
    )
    scene.collection.children.link(presentation)

    ground_mesh = bpy.data.meshes.new("PreviewGround_Mesh")
    ground_geometry = kit.box((0.0, 0.0, -0.18), (32.0, 32.0, 0.30))
    ground_mesh.from_pydata(ground_geometry[0], [], ground_geometry[1])
    ground = bpy.data.objects.new("PreviewGround", ground_mesh)
    presentation.objects.link(ground)
    ground_material = bpy.data.materials.new("PREVIEW_PlayerHomeGround")
    ground_material.diffuse_color = (0.052, 0.061, 0.061, 1.0)
    ground.data.materials.append(ground_material)

    for name, location, target, energy, colour, size in (
        ("Key", (-14.0, 20.0, 18.0), (0.0, 1.5, 4.2),
         2200, (0.72, 0.82, 0.78), 8.0),
        ("Front", (12.0, 17.0, 10.0), (-1.0, 4.5, 4.0),
         1350, (0.92, 0.59, 0.36), 6.0),
        ("Rim", (11.0, -13.0, 13.0), (0.0, 0.0, 4.8),
         1800, (0.33, 0.46, 0.55), 8.0),
    ):
        light_data = bpy.data.lights.new(f"PREVIEW_{name}", "AREA")
        light_data.energy = energy
        light_data.color = colour
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(f"PREVIEW_{name}", light_data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (
            Vector(target) - Vector(location)
        ).to_track_quat("-Z", "Y").to_euler()

    camera_data = bpy.data.cameras.new("PREVIEW_PlayerHomeCamera")
    camera = bpy.data.objects.new("PREVIEW_PlayerHomeCamera", camera_data)
    presentation.objects.link(camera)
    # The canonical balcony and front roof add a 2.30 m visual overhang to
    # the 12 m body.  Frame that complete silhouette instead of cropping the
    # eave as the old body-only preview camera did.
    camera.location = (19.5, 25.5, 13.5)
    target = Vector((-0.45, 1.45, 4.20))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 52
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("PREVIEW_PlayerHomeWorld")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (
            0.025, 0.031, 0.032, 1.0
        )
        background.inputs["Strength"].default_value = 0.34
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
            "PLAYER-HOME EXTERIOR 3D VALID: "
            f"{report['mesh_count']} meshes / "
            f"{report['triangle_count']} triangles, "
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
        "Player-home exterior written: "
        f"{report['mesh_count']} meshes / "
        f"{report['triangle_count']} triangles, "
        f"signature {signature[:16]}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
