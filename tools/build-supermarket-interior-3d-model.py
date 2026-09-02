#!/usr/bin/env python3
"""Build the passive authored supermarket interior.
Unity retains layout, collision, lights, products, interactions, CCTV logic,
and the Watcher Cashier. This generator owns only static visible geometry.
Dimensions use Unity-local X/right, Y/up, Z/rearward until mesh conversion.
Run through Blender; pass ``-- --validate-only`` for contract validation.
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
import bar_parts as bp  # noqa: E402
import interior_kit as kit  # noqa: E402

GENERATOR = "tools/build-supermarket-interior-3d-model.py"
GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "supermarket_interior_v1"
DISPLAY_NAME = "Bar Promenade Supermarket Interior"
ROOM_WIDTH = 16.0
ROOM_DEPTH = 11.0
ROOM_HEIGHT = 3.6
WALL_THICKNESS = 0.25
FLOOR_THICKNESS = 0.12
CEILING_THICKNESS = 0.12
ENTRANCE_WIDTH = 2.4
ENTRANCE_HEIGHT = 2.94
SKIRTING_HEIGHT = 0.135
SKIRTING_DEPTH = 0.026
SKIRTING_WALL_BURY = 0.003
SKIRTING_FLOOR_BURY = 0.003
HALF_WIDTH = ROOM_WIDTH * 0.5
HALF_DEPTH = ROOM_DEPTH * 0.5
DEFAULT_BLEND = ROOT / "ArtSource/Supermarket/Interior/Blender/SupermarketInterior3D.blend"
DEFAULT_FBX = ROOT / "Assets/Supermarket/Interior/Models/SupermarketInterior3D.fbx"
DEFAULT_MANIFEST = ROOT / "Assets/Supermarket/Interior/Models/SupermarketInterior3D.json"
DEFAULT_PREVIEW = ROOT / "ArtSource/Supermarket/Interior/Preview/SupermarketInterior3D.png"
TEXTURE_PATHS = {
    "Linoleum": ROOT / "Assets/Resources/Supermarket/Textures/SupermarketLinoleumAlbedo.png",
    "WallPaint": ROOT / "Assets/Resources/Supermarket/Textures/SupermarketWallPaintAlbedo.png",
    "Ceiling": ROOT / "Assets/Resources/Supermarket/Textures/SupermarketCeilingAlbedo.png",
    "ShelfMetal": ROOT / "Assets/Resources/Supermarket/Textures/SupermarketShelfMetalAlbedo.png",
    "Counter": ROOT / "Assets/Resources/Supermarket/Textures/SupermarketCounterAlbedo.png",
    "Cardboard": ROOT / "Assets/Resources/Supermarket/Textures/SupermarketCardboardAlbedo.png",
}
SHEET_PITCH = {
    "": 1.0,
    "Linoleum": 2.4,
    "WallPaint": 2.6,
    "Ceiling": 3.0,
    "ShelfMetal": 1.3,
    "Counter": 1.2,
    "Cardboard": 0.9,
}
ALLOWED_SHEETS = frozenset(SHEET_PITCH)
FLOOR = (0.255, 0.285, 0.265, 1.0)
FLOOR_PATCH = (0.165, 0.185, 0.170, 1.0)
WALL = (0.56, 0.57, 0.47, 1.0)
WALL_SHADOW = (0.275, 0.305, 0.265, 1.0)
GREEN_STRIPE = (0.29, 0.43, 0.34, 1.0)
GRIME = (0.19, 0.23, 0.18, 1.0)
CEILING = (0.40, 0.43, 0.38, 1.0)
SHELF_FRAME = (0.34, 0.36, 0.31, 1.0)
SHELF_SURFACE = (0.47, 0.48, 0.39, 1.0)
SHELF_RUST = (0.31, 0.105, 0.045, 1.0)
COLD_METAL = (0.38, 0.47, 0.45, 1.0)
COLD_INTERIOR = (0.245, 0.315, 0.305, 1.0)
CHECKOUT_BASE = (0.31, 0.38, 0.32, 1.0)
CHECKOUT_TRIM = (0.67, 0.61, 0.38, 1.0)
BELT = (0.075, 0.085, 0.075, 1.0)
CARDBOARD = (0.44, 0.34, 0.20, 1.0)
FLUORESCENT = (1.0, 1.0, 0.92, 1.0)
CAMERA_HOUSING = (0.155, 0.175, 0.165, 1.0)
CAMERA_BODY = (0.235, 0.255, 0.245, 1.0)
CAMERA_LENS = (0.028, 0.032, 0.035, 1.0)
RECORDING_LED = (0.85, 0.12, 0.08, 1.0)
DRY_SHELF = (-4.7, 0.0, 0.0, 1.0, 2.05, 5.30, 1.0)
PANTRY_SHELF = (1.35, 0.0, 0.0, 1.0, 2.05, 4.70, -1.0)
COLD_SHELF = (-0.05, 0.0, 4.575, 6.30, 2.25, 0.65)
CHECKOUT_CENTRE = (5.50, 0.0, -3.525)
CHECKOUT_SIZE = (2.80, 1.05, 0.95)
STOCKROOM_CENTRE = (5.725, 0.0, 4.575)
STOCKROOM_SIZE = (2.15, 2.35, 0.65)
CASHIER_POSITION = (5.28, 0.0, -2.82)
TUBE_X = (-5.20, -1.75, 1.75, 5.20)
CCTV_X = 7.13
CCTV_Z = 4.63
CCTV_HEAD_Y = 3.10
CCTV_MOUNT_Y = 3.60

@dataclass
class Part:
    obj: "bpy.types.Object"
    name: str; role: str; group: str; sheet: str
    emissive: bool; casts_shadows: bool
    base_color: tuple[float, float, float, float]
    geometry: kit.Geometry
    parent_anchor: str = ""

@dataclass
class AssetBuild:
    root: "bpy.types.Object"; collection: "bpy.types.Collection"
    parts: list[Part] = field(default_factory=list)
    anchors: dict[str, "bpy.types.Object"] = field(default_factory=dict)
    materials: dict[tuple, "bpy.types.Material"] = field(default_factory=dict)
ANCHOR_UNITY_POSITIONS = {
    "entrance": (0.0, 0.0, -HALF_DEPTH),
    "room_centre": (0.0, 0.0, 0.0),
    "shelf_dry": (-4.70, 0.0, 0.0),
    "shelf_pantry": (1.35, 0.0, 0.0),
    "shelf_cold": (-0.05, 0.0, 4.575),
    "checkout": CHECKOUT_CENTRE,
    "stockroom": STOCKROOM_CENTRE,
    "cctv_mount_01": (-CCTV_X, CCTV_MOUNT_Y, -CCTV_Z),
    "cctv_mount_02": (CCTV_X, CCTV_MOUNT_Y, -CCTV_Z),
    "cctv_mount_03": (-CCTV_X, CCTV_MOUNT_Y, CCTV_Z),
    "cctv_mount_04": (CCTV_X, CCTV_MOUNT_Y, CCTV_Z),
    "cctv_head_01": (-CCTV_X, CCTV_HEAD_Y, -CCTV_Z),
    "cctv_head_02": (CCTV_X, CCTV_HEAD_Y, -CCTV_Z),
    "cctv_head_03": (-CCTV_X, CCTV_HEAD_Y, CCTV_Z),
    "cctv_head_04": (CCTV_X, CCTV_HEAD_Y, CCTV_Z),
    "tube_01": (TUBE_X[0], 3.41, 0.0),
    "tube_02": (TUBE_X[1], 3.41, 0.0),
    "tube_03": (TUBE_X[2], 3.41, 0.0),
    "tube_04": (TUBE_X[3], 3.41, 0.0),
    "cashier": CASHIER_POSITION,
    "product_instant_noodles": (-4.40, 0.4125, -1.20),
    "product_day_old_loaf": (-4.40, 1.0125, 1.18),
    "product_vodka_bottle": (1.07, 1.6125, -1.00),
    "product_closed_stew_can": (1.06, 0.4125, 1.02),
    "product_chicken_egg": (0.40, 0.5725, 4.455),
}

def stable(value: float) -> float:
    return round(float(value), 6)

def unity_to_source_position(position: Sequence[float]) -> tuple[float, float, float]:
    x, y, z = position
    return float(x), float(z), float(y)

def source_to_unity_position(position: Sequence[float]) -> tuple[float, float, float]:
    x, y, z = position
    return float(x), float(z), float(y)

def merge(items: Iterable[kit.Geometry]) -> kit.Geometry:
    return kit.merge_all(list(items))

def boxes(
    specs: Iterable[tuple[Sequence[float], Sequence[float]]],
    chamfer: float = 0.012,
) -> kit.Geometry:
    return merge(bp.u_box(center, size, chamfer) for center, size in specs)

def cylinder_along_z(
    center: Sequence[float],
    diameter: float,
    length: float,
    sides: int = 10,
) -> kit.Geometry:
    geometry = bp.u_cylinder(
        center,
        (diameter, length * 0.5, diameter),
        sides,
    )
    return bp.u_rotated_about(geometry, (90.0, 0.0, 0.0), center)

def material_for(
    asset: AssetBuild,
    sheet: str,
    base_color: Sequence[float],
    emissive: bool,
) -> "bpy.types.Material":
    rgba = tuple(stable(value) for value in base_color)
    key = (sheet, rgba, emissive)
    existing = asset.materials.get(key)
    if existing is not None:
        return existing
    suffix = hashlib.sha1(repr(key).encode("utf-8")).hexdigest()[:8]
    material = bpy.data.materials.new(
        f"PREVIEW_SupermarketInterior_{sheet or 'Flat'}_{suffix}"
    )
    material.diffuse_color = rgba
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        base_input = bsdf.inputs.get("Base Color")
        roughness = bsdf.inputs.get("Roughness")
        metallic = bsdf.inputs.get("Metallic")
        if base_input is not None:
            base_input.default_value = rgba
        if roughness is not None:
            roughness.default_value = {
                "ShelfMetal": 0.66,
                "Counter": 0.72,
                "Linoleum": 0.78,
                "Ceiling": 0.91,
                "Cardboard": 0.94,
            }.get(sheet, 0.82)
        if metallic is not None:
            metallic.default_value = 0.24 if sheet == "ShelfMetal" else 0.0
        texture_path = TEXTURE_PATHS.get(sheet)
        if texture_path is not None and texture_path.exists() and base_input is not None:
            image = bpy.data.images.load(str(texture_path), check_existing=True)
            texture = material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image = image
            texture.interpolation = "Linear"
            texture.extension = "REPEAT"
            tint = material.node_tree.nodes.new("ShaderNodeRGB")
            tint.outputs[0].default_value = rgba
            multiply = material.node_tree.nodes.new("ShaderNodeMixRGB")
            multiply.blend_type = "MULTIPLY"
            multiply.inputs[0].default_value = 1.0
            material.node_tree.links.new(texture.outputs["Color"], multiply.inputs[1])
            material.node_tree.links.new(tint.outputs[0], multiply.inputs[2])
            material.node_tree.links.new(multiply.outputs[0], base_input)
        if emissive:
            emission = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
            strength = bsdf.inputs.get("Emission Strength")
            if emission is not None:
                emission.default_value = rgba
            if strength is not None:
                strength.default_value = 4.0
    asset.materials[key] = material
    return material

def assign_world_uv(mesh: "bpy.types.Mesh", pitch: float) -> None:
    layer = mesh.uv_layers.new(name="UVMap")
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
    name: str,
    geometry: kit.Geometry,
    role: str,
    sheet: str,
    base_color: Sequence[float],
    *,
    group: str = "fixed",
    emissive: bool = False,
    casts_shadows: bool = True,
    unity_space: bool = True,
    parent_anchor: str = "",
) -> "bpy.types.Object":
    if sheet not in ALLOWED_SHEETS:
        raise SystemExit(f"Part '{name}' names unsupported sheet '{sheet}'.")
    source_geometry = bp.to_source(geometry) if unity_space else geometry
    vertices, faces = source_geometry
    if not vertices or not faces:
        raise SystemExit(f"Part '{name}' is empty.")
    mesh_geometry = source_geometry
    parent = asset.root
    if parent_anchor:
        parent = asset.anchors.get(parent_anchor)
        if parent is None:
            raise SystemExit(
                f"Part '{name}' names missing parent anchor '{parent_anchor}'."
            )
        offset = tuple(-float(value) for value in parent.location)
        mesh_geometry = kit.translated(source_geometry, offset)
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(mesh_geometry[0], [], mesh_geometry[1])
    mesh.materials.append(material_for(asset, sheet, base_color, emissive))
    mesh.update(calc_edges=True)
    assign_world_uv(mesh, SHEET_PITCH[sheet])
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = parent
    obj["bp_role"] = role
    obj["bp_group"] = group
    obj["bp_sheet"] = sheet
    obj["bp_emissive"] = emissive
    obj["bp_casts_shadows"] = casts_shadows
    obj["bp_parent_anchor"] = parent_anchor
    rgba = tuple(float(value) for value in base_color)
    asset.parts.append(Part(
        obj,
        name,
        role,
        group,
        sheet,
        emissive,
        casts_shadows,
        rgba,
        source_geometry,
        parent_anchor,
    ))
    return obj

def add_anchor(asset: AssetBuild, name: str, unity_position: Sequence[float]) -> None:
    anchor = bpy.data.objects.new(f"ANCHOR_{name}", None)
    asset.collection.objects.link(anchor)
    anchor.parent = asset.root
    anchor.location = unity_to_source_position(unity_position)
    anchor.empty_display_type = "ARROWS"
    anchor.empty_display_size = 0.28 if name.startswith("cctv_head") else 0.42
    anchor["bp_role"] = name
    anchor["bp_unity_x"] = float(unity_position[0])
    anchor["bp_unity_y"] = float(unity_position[1])
    anchor["bp_unity_z"] = float(unity_position[2])
    asset.anchors[name] = anchor

def configure_scene() -> tuple["bpy.types.Collection", "bpy.types.Object"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene["bp_generator"] = GENERATOR
    scene["bp_generator_version"] = GENERATOR_VERSION
    scene["bp_design_id"] = DESIGN_ID
    scene["bp_source_forward"] = "+Y"
    collection = bpy.data.collections.new("SOURCE_SupermarketInterior3D")
    scene.collection.children.link(collection)
    root = bpy.data.objects.new("ROOT_SupermarketInterior3D", None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.65
    root["bp_export"] = True
    collection.objects.link(root)
    return collection, root

def build_shell(asset: AssetBuild) -> None:
    add_part(
        asset,
        "Floor",
        kit.floor_slab(ROOM_WIDTH, ROOM_DEPTH, FLOOR_THICKNESS, 0.018),
        "floor",
        "Linoleum",
        FLOOR,
        unity_space=False,
    )
    add_part(
        asset,
        "Ceiling",
        kit.box(
            (0.0, 0.0, ROOM_HEIGHT - CEILING_THICKNESS * 0.5),
            (ROOM_WIDTH, ROOM_DEPTH, CEILING_THICKNESS),
        ),
        "ceiling",
        "Ceiling",
        CEILING,
        unity_space=False,
    )
    walls = kit.rectangular_room_walls(
        ROOM_WIDTH,
        ROOM_DEPTH,
        ROOM_HEIGHT,
        WALL_THICKNESS,
        front_openings=[kit.Opening(0.0, ENTRANCE_WIDTH, ENTRANCE_HEIGHT)],
        chamfer=0.018,
    )
    for source_name, part_name, color in (
        ("front", "Front Wall", WALL_SHADOW),
        ("back", "Back Wall", WALL),
        ("left", "Left Wall", WALL_SHADOW),
        ("right", "Right Wall", WALL),
    ):
        add_part(
            asset,
            part_name,
            walls[source_name],
            "wall",
            "WallPaint",
            color,
            unity_space=False,
        )
    entrance_frame = kit.translated(
        kit.door_frame(
            ENTRANCE_WIDTH,
            ENTRANCE_HEIGHT,
            WALL_THICKNESS,
            jamb=0.085,
            architrave=0.0,
            chamfer=0.012,
        ),
        (0.0, -HALF_DEPTH, 0.0),
    )
    add_part(
        asset,
        "Entrance Frame",
        entrance_frame,
        "entrance_frame",
        "ShelfMetal",
        SHELF_FRAME,
        unity_space=False,
    )
    inner_x = HALF_WIDTH - WALL_THICKNESS * 0.5
    inner_z = HALF_DEPTH - WALL_THICKNESS * 0.5
    profile = kit.profile_skirting(
        SKIRTING_HEIGHT + SKIRTING_FLOOR_BURY,
        SKIRTING_DEPTH,
    )
    # Sweeps grow left of their path: run counter-clockwise into the room.
    # Trim corners and bury rear/bottom faces off neighbouring render planes.
    outer_x = inner_x + SKIRTING_WALL_BURY
    outer_z = inner_z + SKIRTING_WALL_BURY
    inside_x = outer_x - SKIRTING_DEPTH
    trim_paths = (
        ((outer_x, -outer_z), (outer_x, outer_z)),
        ((inside_x, outer_z), (-inside_x, outer_z)),
        ((-outer_x, outer_z), (-outer_x, -outer_z)),
        ((-inside_x, -outer_z),
         (-ENTRANCE_WIDTH * 0.5, -outer_z)),
        ((ENTRANCE_WIDTH * 0.5, -outer_z),
         (inside_x, -outer_z)),
    )
    trim = merge(
        kit.sweep(path, profile, z=-SKIRTING_FLOOR_BURY)
        for path in trim_paths
    )
    add_part(
        asset,
        "Perimeter Skirting",
        trim,
        "trim",
        "WallPaint",
        WALL_SHADOW,
        casts_shadows=False,
        unity_space=False,
    )
    stripe_y = 1.08
    stripe_height = 0.22
    stripe = boxes((
        ((0.0, stripe_y, inner_z - 0.006),
         (ROOM_WIDTH - 0.50, stripe_height, 0.025)),
        ((-inner_x + 0.006, stripe_y, 0.0),
         (0.025, stripe_height, ROOM_DEPTH - 0.50)),
        ((inner_x - 0.006, stripe_y, 0.0),
         (0.025, stripe_height, ROOM_DEPTH - 0.50)),
    ), 0.004)
    add_part(
        asset,
        "Bottle Green Wall Stripe",
        stripe,
        "trim",
        "WallPaint",
        GREEN_STRIPE,
        casts_shadows=False,
    )

def build_ceiling_grid(asset: AssetBuild) -> None:
    grid_parts: list[kit.Geometry] = []
    for x in (-6.0, -4.0, -2.0, 0.0, 2.0, 4.0, 6.0):
        grid_parts.append(kit.translated(
            kit.beam(ROOM_DEPTH - 0.34, 0.035, 0.035, 0.006),
            (x, 0.0, 3.505),
        ))
    for y in (-4.0, -2.0, 0.0, 2.0, 4.0):
        beam = kit.beam(ROOM_WIDTH - 0.34, 0.035, 0.035, 0.006)
        grid_parts.append(kit.translated(
            kit.rotated_z(beam, 90.0),
            (0.0, y, 3.505),
        ))
    add_part(
        asset,
        "Ceiling Grid",
        merge(grid_parts),
        "ceiling_grid",
        "Ceiling",
        WALL_SHADOW,
        casts_shadows=False,
        unity_space=False,
    )

def build_light_fixtures(asset: AssetBuild) -> None:
    for index, x in enumerate(TUBE_X, 1):
        housing = boxes((
            ((x, 3.47, 0.0), (0.34, 0.09, 4.70)),
            ((x - 0.145, 3.425, 0.0), (0.045, 0.13, 4.58)),
            ((x + 0.145, 3.425, 0.0), (0.045, 0.13, 4.58)),
            ((x, 3.445, -2.31), (0.30, 0.11, 0.08)),
            ((x, 3.445, 2.31), (0.30, 0.11, 0.08)),
        ), 0.008)
        add_part(
            asset,
            f"Fluorescent Housing {index:02d}",
            housing,
            "fluorescent_housing",
            "ShelfMetal",
            CAMERA_HOUSING,
        )
        add_part(
            asset,
            f"Fluorescent Tube {index:02d}",
            cylinder_along_z((x, 3.41, 0.0), 0.105, 4.34, 10),
            "fluorescent_tube",
            "",
            FLUORESCENT,
            emissive=True,
            casts_shadows=False,
        )

def build_grime(asset: AssetBuild) -> None:
    patches = (
        ((-6.10, 0.009, -3.40), (1.30, 0.018, 0.44)),
        ((-1.30, 0.009, 2.55), (0.82, 0.018, 0.66)),
        ((3.25, 0.009, 1.15), (1.15, 0.018, 0.38)),
        ((6.55, 0.009, -0.60), (0.62, 0.018, 1.05)),
    )
    add_part(
        asset,
        "Linoleum Repairs",
        boxes(patches, 0.004),
        "grime",
        "Linoleum",
        FLOOR_PATCH,
        casts_shadows=False,
    )
    back = HALF_DEPTH - WALL_THICKNESS * 0.5 - 0.016
    damp = boxes((
        ((-5.85, 0.62, back), (1.45, 0.72, 0.018)),
        ((3.82, 1.72, back), (0.36, 1.48, 0.018)),
    ), 0.003)
    add_part(
        asset,
        "Back Wall Damp",
        damp,
        "grime",
        "WallPaint",
        GRIME,
        casts_shadows=False,
    )

def build_gondola(
    asset: AssetBuild,
    label: str,
    role: str,
    specification: Sequence[float],
) -> None:
    cx, _, cz, width, height, depth, facing = specification
    half_x = width * 0.5
    half_z = depth * 0.5
    frame_specs = [
        ((cx, 0.09, cz), (width, 0.18, depth)),
        ((cx, height - 0.03, cz), (width, 0.06, depth)),
        ((cx, height * 0.51, cz), (0.095, height - 0.24, depth - 0.08)),
    ]
    for side in (-1.0, 1.0):
        end_z = cz + side * (half_z - 0.035)
        frame_specs.extend((
            ((cx - half_x + 0.04, height * 0.50, end_z),
             (0.075, height - 0.18, 0.07)),
            ((cx + half_x - 0.04, height * 0.50, end_z),
             (0.075, height - 0.18, 0.07)),
            ((cx, 0.24, end_z), (width - 0.10, 0.075, 0.07)),
            ((cx, 1.30, end_z), (width - 0.10, 0.055, 0.07)),
        ))
    add_part(
        asset,
        f"Shelf {label} Frame",
        boxes(frame_specs, 0.009),
        role,
        "ShelfMetal",
        SHELF_FRAME,
    )
    tier_parts: list[kit.Geometry] = []
    for tier_y in (0.38, 0.98, 1.58):
        y = min(height - 0.09, tier_y)
        tier_parts.append(bp.u_box(
            (cx, y, cz),
            (width - 0.06, 0.065, depth - 0.10),
            0.010,
        ))
        lip_x = cx + facing * (half_x - 0.025)
        tier_parts.append(bp.u_box(
            (lip_x, y + 0.022, cz),
            (0.045, 0.09, depth - 0.18),
            0.006,
        ))
    add_part(
        asset,
        f"Shelf {label} Tiers",
        merge(tier_parts),
        role,
        "ShelfMetal",
        SHELF_SURFACE,
    )
    rail_x = cx + facing * (half_x - 0.012)
    rails = [
        bp.u_box(
            (rail_x, min(height - 0.13, tier_y - 0.055), cz),
            (0.024, 0.072, depth - 0.24),
            0.004,
        )
        for tier_y in (0.38, 0.98, 1.58)
    ]
    rails.append(bp.u_box(
        (rail_x, 0.235, cz - depth * 0.12),
        (0.024, 0.075, depth * 0.56),
        0.004,
    ))
    add_part(
        asset,
        f"Shelf {label} Rails",
        merge(rails),
        role,
        "ShelfMetal",
        SHELF_RUST,
        casts_shadows=False,
    )

def build_cold_shelf(asset: AssetBuild) -> None:
    cx, _, cz, width, height, depth = COLD_SHELF
    half_x = width * 0.5
    half_z = depth * 0.5
    frame = boxes((
        ((cx, 0.09, cz), (width, 0.18, depth)),
        ((cx, height - 0.04, cz), (width, 0.08, depth)),
        ((cx - half_x + 0.05, height * 0.5, cz),
         (0.10, height, depth)),
        ((cx + half_x - 0.05, height * 0.5, cz),
         (0.10, height, depth)),
        ((cx, height * 0.5, cz + half_z - 0.05),
         (width, height, 0.10)),
    ), 0.012)
    add_part(
        asset,
        "Shelf Cold Cabinet",
        frame,
        "shelf_cold",
        "ShelfMetal",
        COLD_METAL,
    )
    cavity_z = cz - half_z + 0.022
    backing_z = cz + half_z - 0.105
    add_part(
        asset,
        "Shelf Cold Recess",
        bp.u_box(
            (cx, height * 0.53, backing_z),
            (width - 0.22, height - 0.30, 0.035),
            0.006,
        ),
        "shelf_cold",
        "ShelfMetal",
        COLD_INTERIOR,
    )
    tiers = []
    for y in (0.54, 1.14, 1.74):
        tiers.append(bp.u_box(
            (cx, y, cz),
            (width - 0.18, 0.065, depth - 0.02),
            0.009,
        ))
    for bay in (-2.0, -1.0, 0.0, 1.0, 2.0):
        x = cx + bay * (width - 0.24) / 6.0
        tiers.append(bp.u_box(
            (x, 1.16, cavity_z - 0.004),
            (0.032, height - 0.40, 0.026),
            0.004,
        ))
    add_part(
        asset,
        "Shelf Cold Tiers",
        merge(tiers),
        "shelf_cold",
        "ShelfMetal",
        SHELF_SURFACE,
    )
    add_part(
        asset,
        "Shelf Cold Interior Tube",
        bp.u_box(
            (cx, height - 0.13, cz - half_z + 0.045),
            (width - 0.30, 0.075, 0.055),
            0.009,
        ),
        "shelf_cold",
        "",
        FLUORESCENT,
        emissive=True,
        casts_shadows=False,
    )
    frost = boxes((
        ((cx - 2.15, 1.38, cz - half_z + 0.010),
         (0.72, 0.14, 0.012)),
        ((cx + 1.65, 0.42, cz - half_z + 0.010),
         (1.08, 0.10, 0.012)),
    ), 0.002)
    add_part(
        asset,
        "Shelf Cold Frost",
        frost,
        "shelf_cold",
        "",
        (0.60, 0.72, 0.69, 1.0),
        casts_shadows=False,
    )

def build_shelves(asset: AssetBuild) -> None:
    build_gondola(asset, "Dry", "shelf_dry", DRY_SHELF)
    build_gondola(asset, "Pantry", "shelf_pantry", PANTRY_SHELF)
    build_cold_shelf(asset)

def build_checkout(asset: AssetBuild) -> None:
    cx, _, cz = CHECKOUT_CENTRE
    width, height, depth = CHECKOUT_SIZE
    source_counter = kit.translated(
        kit.counter_run(
            width,
            depth,
            height,
            top_thickness=0.075,
            nosing=0.0,
            plinth_inset=0.075,
            chamfer=0.014,
        ),
        (cx, cz, 0.0),
    )
    add_part(
        asset,
        "Checkout Counter",
        source_counter,
        "checkout",
        "Counter",
        CHECKOUT_BASE,
        unity_space=False,
    )
    add_part(
        asset,
        "Checkout Conveyor Belt",
        bp.u_box(
            (cx - 0.33, height + 0.026, cz - 0.02),
            (width * 0.60, 0.052, depth * 0.73),
            0.011,
        ),
        "checkout",
        "",
        BELT,
    )
    front_z = cz - depth * 0.5 + 0.025
    panels = [
        bp.u_box(
            (cx - width * 0.31 + index * width * 0.31,
             height * 0.57,
             front_z),
            (width * 0.25, height * 0.58, 0.035),
            0.006,
        )
        for index in range(3)
    ]
    add_part(
        asset,
        "Checkout Front Panels",
        merge(panels),
        "checkout",
        "Counter",
        CHECKOUT_TRIM,
        casts_shadows=False,
    )
    register_center = (cx + width * 0.31, height + 0.15, cz + 0.03)
    register = boxes((
        (register_center, (0.48, 0.30, 0.42)),
        ((register_center[0], register_center[1] - 0.14,
          register_center[2] + 0.03), (0.58, 0.08, 0.48)),
    ), 0.018)
    add_part(
        asset,
        "Checkout Register",
        register,
        "register",
        "Counter",
        (0.29, 0.31, 0.27, 1.0),
    )
    screen_center = (
        register_center[0],
        register_center[1] + 0.075,
        register_center[2] - 0.216,
    )
    screen = bp.u_box(screen_center, (0.31, 0.11, 0.022), 0.004)
    screen = bp.u_rotated_about(screen, (-12.0, 0.0, 0.0), screen_center)
    add_part(
        asset,
        "Checkout Register Display",
        screen,
        "register",
        "",
        (0.74, 0.35, 0.16, 1.0),
        emissive=True,
        casts_shadows=False,
    )
    keys = []
    for row in range(2):
        for column in range(3):
            keys.append(bp.u_box(
                (
                    register_center[0] - 0.12 + column * 0.12,
                    register_center[1] - 0.07 + row * 0.07,
                    register_center[2] - 0.224,
                ),
                (0.065, 0.042, 0.014),
                0.003,
            ))
    add_part(
        asset,
        "Checkout Register Keys",
        merge(keys),
        "register",
        "",
        (0.50, 0.51, 0.43, 1.0),
        casts_shadows=False,
    )
    rack_x = cx + 1.15
    pole = bp.u_cylinder(
        (rack_x, 1.30, cz + 0.27),
        (0.055, 0.25, 0.055),
        10,
    )
    add_part(
        asset,
        "Checkout Bag Rack",
        pole,
        "bag_rack",
        "ShelfMetal",
        SHELF_FRAME,
    )
    hooks = boxes((
        ((rack_x - 0.11, 1.49, cz + 0.27), (0.22, 0.035, 0.035)),
        ((rack_x + 0.11, 1.49, cz + 0.27), (0.22, 0.035, 0.035)),
    ), 0.004)
    add_part(
        asset,
        "Checkout Bag Rack Hooks",
        hooks,
        "bag_rack",
        "ShelfMetal",
        SHELF_FRAME,
    )
    bags = [
        bp.u_box(
            (rack_x - index * 0.025, 1.22, cz + 0.23 - index * 0.012),
            (0.38, 0.30, 0.014),
            0.004,
        )
        for index in range(3)
    ]
    add_part(
        asset,
        "Checkout Empty Bags",
        merge(bags),
        "bag_rack",
        "",
        (0.62, 0.67, 0.57, 1.0),
        casts_shadows=False,
    )

def build_stockroom(asset: AssetBuild) -> None:
    cx, _, cz = STOCKROOM_CENTRE
    width, height, depth = STOCKROOM_SIZE
    front_z = cz - depth * 0.5 + 0.060
    door = boxes((
        ((cx, height * 0.5, front_z + 0.035),
         (width - 0.18, height - 0.15, 0.07)),
        ((cx, height - 0.075, front_z), (width, 0.15, 0.12)),
        ((cx - width * 0.5 + 0.075, height * 0.5, front_z),
         (0.15, height, 0.12)),
        ((cx + width * 0.5 - 0.075, height * 0.5, front_z),
         (0.15, height, 0.12)),
    ), 0.012)
    add_part(
        asset,
        "Stockroom Facade",
        door,
        "stockroom_facade",
        "ShelfMetal",
        (0.24, 0.30, 0.26, 1.0),
    )
    add_part(
        asset,
        "Stockroom Push Plate",
        bp.u_box(
            (cx, height * 0.52, front_z - 0.030),
            (0.62, 0.19, 0.025),
            0.004,
        ),
        "stockroom_facade",
        "ShelfMetal",
        SHELF_FRAME,
        casts_shadows=False,
    )
    cartons = (
        ((cx - 0.66, 0.28, cz - 0.49), (0.62, 0.56, 0.46)),
        ((cx - 0.58, 0.76, cz - 0.45), (0.48, 0.40, 0.40)),
    )
    add_part(
        asset,
        "Stockroom Empty Cartons",
        boxes(cartons, 0.018),
        "carton",
        "Cardboard",
        CARDBOARD,
    )
    tape = boxes((
        ((cx - 0.66, 0.56, cz - 0.49), (0.10, 0.014, 0.43)),
        ((cx - 0.58, 0.965, cz - 0.45), (0.085, 0.014, 0.37)),
    ), 0.002)
    add_part(
        asset,
        "Stockroom Carton Tape",
        tape,
        "carton",
        "Cardboard",
        (0.62, 0.51, 0.31, 1.0),
        casts_shadows=False,
    )

def cctv_positions() -> tuple[tuple[float, float, float], ...]:
    return (
        (-CCTV_X, CCTV_HEAD_Y, -CCTV_Z),
        (CCTV_X, CCTV_HEAD_Y, -CCTV_Z),
        (-CCTV_X, CCTV_HEAD_Y, CCTV_Z),
        (CCTV_X, CCTV_HEAD_Y, CCTV_Z),
    )

def build_cctv(asset: AssetBuild) -> None:
    for index, position in enumerate(cctv_positions(), 1):
        x, head_y, z = position
        mount = boxes((
            ((x, (head_y + CCTV_MOUNT_Y) * 0.5, z),
             (0.13, CCTV_MOUNT_Y - head_y, 0.13)),
            ((x, CCTV_MOUNT_Y - 0.025, z), (0.24, 0.05, 0.24)),
        ), 0.008)
        add_part(
            asset,
            f"CCTV Mount {index:02d}",
            mount,
            "cctv_mount",
            "ShelfMetal",
            CAMERA_HOUSING,
        )
        anchor_name = f"cctv_head_{index:02d}"
        body = boxes((
            ((x, head_y, z + 0.19), (0.27, 0.27, 0.62)),
            ((x, head_y + 0.16, z + 0.27), (0.31, 0.06, 0.58)),
        ), 0.012)
        add_part(
            asset,
            f"CCTV Head {index:02d}",
            body,
            "cctv_head",
            "ShelfMetal",
            CAMERA_BODY,
            parent_anchor=anchor_name,
        )
        lens = boxes((
            ((x, head_y, z + 0.545), (0.19, 0.19, 0.10)),
            ((x, head_y, z + 0.60), (0.10, 0.10, 0.015)),
        ), 0.006)
        add_part(
            asset,
            f"CCTV Head {index:02d} Lens",
            lens,
            "cctv_head",
            "",
            CAMERA_LENS,
            parent_anchor=anchor_name,
        )
        add_part(
            asset,
            f"CCTV Head {index:02d} LED",
            bp.u_box(
                (x + 0.09, head_y + 0.105, z - 0.08),
                (0.05, 0.05, 0.05),
                0.006,
            ),
            "cctv_head",
            "",
            RECORDING_LED,
            emissive=True,
            casts_shadows=False,
            parent_anchor=anchor_name,
        )

def build() -> AssetBuild:
    collection, root = configure_scene()
    asset = AssetBuild(root, collection)
    for name, position in ANCHOR_UNITY_POSITIONS.items():
        add_anchor(asset, name, position)
    build_shell(asset)
    build_ceiling_grid(asset)
    build_light_fixtures(asset)
    build_grime(asset)
    build_shelves(asset)
    build_checkout(asset)
    build_stockroom(asset)
    build_cctv(asset)
    return asset

def bounds_for_parts(parts: Iterable[Part]) -> tuple[tuple, tuple]:
    selected = list(parts)
    if not selected:
        raise ValueError("A bounds query needs at least one part.")
    return kit.bounds(merge(part.geometry for part in selected))

def validate_extent(
    problems: list[str],
    label: str,
    actual: tuple[Sequence[float], Sequence[float]],
    expected_low: Sequence[float],
    expected_high: Sequence[float],
    tolerance: float = 0.001,
) -> None:
    low, high = actual
    for axis, axis_name in enumerate(("X", "Z", "Y")):
        if (abs(low[axis] - expected_low[axis]) > tolerance or
                abs(high[axis] - expected_high[axis]) > tolerance):
            problems.append(
                f"{label} source {axis_name} bounds are "
                f"{low[axis]:.4f}..{high[axis]:.4f}, expected "
                f"{expected_low[axis]:.4f}..{expected_high[axis]:.4f}"
            )

def validate(asset: AssetBuild) -> dict:
    problems: list[str] = []
    names: set[str] = set()
    for part in asset.parts:
        if part.name in names:
            problems.append(f"duplicate part name '{part.name}'")
        names.add(part.name)
        if part.sheet not in ALLOWED_SHEETS:
            problems.append(f"'{part.name}' has unsupported sheet '{part.sheet}'")
        if bp.signed_volume(part.geometry) <= 1e-9:
            problems.append(f"'{part.name}' has inverted or open authored winding")
        if part.parent_anchor:
            expected_parent = asset.anchors.get(part.parent_anchor)
            if expected_parent is None or part.obj.parent != expected_parent:
                problems.append(
                    f"'{part.name}' is not parented to '{part.parent_anchor}'"
                )
        if part.obj.type != "MESH":
            problems.append(f"'{part.name}' is not a mesh")
        if len(part.obj.data.uv_layers) != 1:
            problems.append(f"'{part.name}' does not have exactly one UV set")
    required_roles = {
        "floor", "ceiling", "wall", "entrance_frame", "ceiling_grid",
        "fluorescent_housing", "fluorescent_tube", "grime", "trim",
        "shelf_dry", "shelf_pantry", "shelf_cold", "checkout",
        "register", "bag_rack", "stockroom_facade", "carton",
        "cctv_mount", "cctv_head",
    }
    roles = {part.role for part in asset.parts}
    for role in sorted(required_roles - roles):
        problems.append(f"the interior has no '{role}' geometry")
    used_sheets = {part.sheet for part in asset.parts}
    for sheet in sorted(ALLOWED_SHEETS - used_sheets):
        problems.append(f"the interior never uses required sheet '{sheet}'")
    if set(asset.anchors) != set(ANCHOR_UNITY_POSITIONS):
        problems.append("the semantic anchor set differs from the contract")
    for name, expected_unity in ANCHOR_UNITY_POSITIONS.items():
        anchor = asset.anchors.get(name)
        if anchor is None:
            continue
        expected_source = unity_to_source_position(expected_unity)
        actual = tuple(stable(value) for value in anchor.location)
        expected = tuple(stable(value) for value in expected_source)
        if actual != expected:
            problems.append(
                f"anchor '{name}' is at source {actual}, expected {expected}"
            )
        if anchor.name != f"ANCHOR_{name}" or anchor["bp_role"] != name:
            problems.append(f"anchor '{name}' lost its exported name or role")
    for index in range(1, 5):
        anchor_name = f"cctv_head_{index:02d}"
        children = [
            part for part in asset.parts
            if part.parent_anchor == anchor_name
        ]
        if len(children) != 3:
            problems.append(
                f"'{anchor_name}' owns {len(children)} head meshes, expected 3"
            )
        if not any(part.name == f"CCTV Head {index:02d}" for part in children):
            problems.append(f"'{anchor_name}' has no canonical head body mesh")
    for index in range(1, 5):
        if f"Fluorescent Tube {index:02d}" not in names:
            problems.append(f"fluorescent tube {index:02d} is missing")
    forbidden_words = (
        "vodka", "noodle", "loaf", "stew", "egg", "product", "price tag",
    )
    for part in asset.parts:
        lowered = part.name.lower()
        if any(word in lowered for word in forbidden_words):
            problems.append(f"'{part.name}' bakes forbidden product/text dressing")
    skirting = next(
        (part for part in asset.parts if part.name == "Perimeter Skirting"),
        None,
    )
    if skirting is None:
        problems.append("the interior has no perimeter skirting")
    else:
        inner_x = HALF_WIDTH - WALL_THICKNESS * 0.5
        inner_z = HALF_DEPTH - WALL_THICKNESS * 0.5
        validate_extent(
            problems,
            "perimeter skirting",
            kit.bounds(skirting.geometry),
            (-inner_x - SKIRTING_WALL_BURY,
             -inner_z - SKIRTING_WALL_BURY,
             -SKIRTING_FLOOR_BURY),
            (inner_x + SKIRTING_WALL_BURY,
             inner_z + SKIRTING_WALL_BURY,
             SKIRTING_HEIGHT),
        )
    shelf_contracts = {
        "shelf_dry": ((-5.20, -2.65, 0.0), (-4.20, 2.65, 2.05)),
        "shelf_pantry": ((0.85, -2.35, 0.0), (1.85, 2.35, 2.05)),
        "shelf_cold": ((-3.20, 4.25, 0.0), (3.10, 4.90, 2.25)),
    }
    for role, (expected_low, expected_high) in shelf_contracts.items():
        validate_extent(
            problems,
            role,
            bounds_for_parts(part for part in asset.parts if part.role == role),
            expected_low,
            expected_high,
        )
    checkout_parts = [
        part for part in asset.parts
        if part.role in {"checkout", "register", "bag_rack"}
    ]
    checkout_low, checkout_high = bounds_for_parts(checkout_parts)
    if (checkout_low[0] < 4.10 - 0.001 or checkout_high[0] > 6.90 + 0.001 or
            checkout_low[1] < -4.00 - 0.001 or
            checkout_high[1] > -3.05 + 0.001):
        problems.append("checkout dressing escapes its 2.80 x 0.95 m footprint")
    stock_parts = [
        part for part in asset.parts if part.role == "stockroom_facade"
    ]
    stock_low, stock_high = bounds_for_parts(stock_parts)
    if (stock_low[0] < 4.65 - 0.001 or stock_high[0] > 6.80 + 0.001 or
            stock_low[1] < 4.25 - 0.001 or stock_high[1] > 4.90 + 0.001 or
            stock_low[2] < -0.001 or stock_high[2] > 2.35 + 0.001):
        problems.append("stockroom facade escapes its plan fixture bounds")
    merged = merge(part.geometry for part in asset.parts)
    low, high = kit.bounds(merged)
    expected_low = (-HALF_WIDTH - WALL_THICKNESS * 0.5,
                    -HALF_DEPTH - WALL_THICKNESS * 0.5,
                    -FLOOR_THICKNESS)
    expected_high = (HALF_WIDTH + WALL_THICKNESS * 0.5,
                     HALF_DEPTH + WALL_THICKNESS * 0.5,
                     ROOM_HEIGHT)
    validate_extent(
        problems,
        "complete interior",
        (low, high),
        expected_low,
        expected_high,
    )
    triangles = kit.triangle_count(merged)
    if len(asset.parts) > 90:
        problems.append(f"the interior fragments into {len(asset.parts)} meshes")
    if triangles > 30000:
        problems.append(
            f"the interior costs {triangles} triangles against its 30000 cap"
        )
    source_types = {obj.type for obj in asset.collection.objects}
    if source_types - {"EMPTY", "MESH"}:
        problems.append(
            f"source collection contains forbidden object types {source_types}"
        )
    if bpy.data.actions:
        problems.append("the passive interior must contain no authored Actions")
    if problems:
        raise SystemExit(
            "Supermarket interior validation failed:\n  " +
            "\n  ".join(problems)
        )
    return {
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
        "unity_bounds_min": [
            stable(value) for value in source_to_unity_position(low)
        ],
        "unity_bounds_max": [
            stable(value) for value in source_to_unity_position(high)
        ],
        "mesh_count": len(asset.parts),
        "triangle_count": triangles,
    }

def signature_for(asset: AssetBuild) -> str:
    payload = {
        "design_id": DESIGN_ID,
        "generator_version": GENERATOR_VERSION,
        "dimensions": [ROOM_WIDTH, ROOM_DEPTH, ROOM_HEIGHT],
        "wall_thickness": WALL_THICKNESS,
        "entrance": [ENTRANCE_WIDTH, ENTRANCE_HEIGHT],
        "parts": [
            {
                "name": part.name,
                "role": part.role,
                "group": part.group,
                "sheet": part.sheet,
                "emissive": part.emissive,
                "casts_shadows": part.casts_shadows,
                "base_color": [stable(value) for value in part.base_color],
                "parent_anchor": part.parent_anchor,
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

def tint_payload(base_color: Sequence[float]) -> dict:
    return {
        "field": "",
        "rgb": [stable(value) for value in base_color[:3]],
        "scale": 1.0,
        "lerp_field": "",
        "lerp_rgb": [0.0, 0.0, 0.0],
        "lerp_t": 0.0,
    }

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
        "base_color": [stable(value) for value in part.base_color],
        "tint": tint_payload(part.base_color),
        "parent_anchor": part.parent_anchor,
        "colliders": [],
        "vertices": len(part.geometry[0]),
        "triangles": kit.triangle_count(part.geometry),
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
    }

def manifest_for(asset: AssetBuild, report: dict, signature: str) -> dict:
    return {
        "generator": GENERATOR,
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "dimensions_m": {
            "width": ROOM_WIDTH,
            "depth": ROOM_DEPTH,
            "height": ROOM_HEIGHT,
        },
        "wall_thickness_m": WALL_THICKNESS,
        "entrance_opening_m": {
            "width": ENTRANCE_WIDTH,
            "height": ENTRANCE_HEIGHT,
        },
        "authored_text": [],
        "baked_products": False,
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
            "origin": "room_footprint_centre_ground",
            "scale_mode": "fixed_meters",
            "source_forward_axis": "+Y",
            "unity_forward_axis": "+Z",
            "axis_conversion": "swap_y_z_and_reverse_winding",
            "runtime_wrapper_yaw_degrees": 0.0,
        },
        "runtime_wrapper_yaw_degrees": 0.0,
        "colliders": False,
        "lights": False,
        "cameras": False,
        "rigidbodies": False,
        "audio_sources": False,
        "materials": False,
        "animation_count": 0,
        "bounds_min": report["bounds_min"],
        "bounds_max": report["bounds_max"],
        "unity_bounds_min": report["unity_bounds_min"],
        "unity_bounds_max": report["unity_bounds_max"],
        "mesh_count": report["mesh_count"],
        "triangle_count": report["triangle_count"],
        "budgets": {
            "maximum_renderers": 90,
            "maximum_triangles": 30000,
            "imported_colliders": 0,
            "imported_lights": 0,
            "imported_cameras": 0,
            "imported_rigidbodies": 0,
        },
        "anchors": [
            {
                "name": name,
                "role": anchor["bp_role"],
                "local_position": [stable(value) for value in anchor.location],
                "unity_local_position": [
                    stable(value)
                    for value in source_to_unity_position(anchor.location)
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
    presentation = bpy.data.collections.new("PRESENTATION_SupermarketInterior3D")
    scene.collection.children.link(presentation)
    hidden = []
    for part in asset.parts:
        if part.name in {"Front Wall", "Ceiling"}:
            hidden.append((part.obj, part.obj.hide_render))
            part.obj.hide_render = True
    for name, location, target, energy, color, size in (
        ("Key", (-5.5, -6.0, 7.8), (0.0, 0.0, 1.1),
         1650, (0.70, 0.85, 0.76), 7.0),
        ("Fill", (8.0, -2.0, 5.2), (1.5, 0.8, 1.2),
         1200, (0.88, 0.73, 0.48), 5.0),
        ("Rear", (-4.0, 6.5, 4.8), (-0.5, 2.0, 1.2),
         1050, (0.45, 0.62, 0.70), 4.0),
    ):
        data = bpy.data.lights.new(f"PREVIEW_{name}", "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(f"PREVIEW_{name}", data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (
            Vector(target) - Vector(location)
        ).to_track_quat("-Z", "Y").to_euler()
    camera_data = bpy.data.cameras.new("PREVIEW_SupermarketInteriorCamera")
    camera = bpy.data.objects.new("PREVIEW_SupermarketInteriorCamera", camera_data)
    presentation.objects.link(camera)
    camera.location = (12.2, -14.6, 9.2)
    target = Vector((0.0, 0.4, 1.25))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 48
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.render.filepath = str(path)
    scene.render.image_settings.color_depth = "8"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("PREVIEW_SupermarketInteriorWorld")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.018, 0.026, 0.022, 1.0)
        background.inputs["Strength"].default_value = 0.28
    scene.world = world
    bpy.ops.render.render(write_still=True)
    for obj, previous in hidden:
        obj.hide_render = previous
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
            "SUPERMARKET INTERIOR 3D VALID: "
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
        "Supermarket interior written: "
        f"{report['mesh_count']} meshes / "
        f"{report['triangle_count']} triangles, "
        f"signature {signature[:16]}"
    )
    print(f"  Blender source: {args.blend}")
    print(f"  FBX: {args.fbx}")
    print(f"  Manifest: {args.manifest}")
    if not args.no_preview:
        print(f"  Preview: {args.preview}")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
