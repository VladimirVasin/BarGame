#!/usr/bin/env python3
"""Build the six passive Blender-authored supermarket product models."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
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

GENERATOR = "tools/build-supermarket-products-3d-model.py"
GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "supermarket_product_pack_v1"
DISPLAY_NAME = "Bar Promenade Supermarket Product Pack"
ROOT_NAME = "ROOT_SupermarketProducts3D"

DEFAULT_BLEND = ROOT / "ArtSource/Supermarket/Products/Blender/SupermarketProducts3D.blend"
DEFAULT_FBX = ROOT / "Assets/Supermarket/Products/Models/SupermarketProducts3D.fbx"
DEFAULT_MANIFEST = ROOT / "Assets/Supermarket/Products/Models/SupermarketProducts3D.json"
DEFAULT_PREVIEW = ROOT / "ArtSource/Supermarket/Products/Preview/SupermarketProducts3D.png"

ITEM_IDS = (
    "instant_noodles",
    "day_old_loaf",
    "vodka_bottle",
    "closed_stew_can",
    "open_stew_can",
    "chicken_egg",
)
ROOT_NAMES = {item_id: f"ITEM_{item_id}" for item_id in ITEM_IDS}
AVAILABLE_SIZES = {
    "instant_noodles": (0.48, 0.34, 0.30),
    "day_old_loaf": (0.56, 0.32, 0.34),
    "vodka_bottle": (0.42, 0.46, 0.38),
    "closed_stew_can": (0.42, 0.34, 0.36),
    "open_stew_can": (0.31, 0.48, 0.28),
    "chicken_egg": (0.48, 0.34, 0.36),
}
REQUIRED_ROLES = {
    "instant_noodles": {"packet", "seal", "label"},
    "day_old_loaf": {"bread", "crumb", "score"},
    "vodka_bottle": {"glass", "label", "cap"},
    "closed_stew_can": {"can_body", "label", "rim", "pull_tab"},
    "open_stew_can": {
        "can_body", "label", "rim", "stew", "lid", "pull_tab",
    },
    "chicken_egg": {"carton", "shell", "shell_mark"},
}

PACKET_ORANGE = (0.56, 0.205, 0.055, 1.0)
PACKET_DARK = (0.31, 0.085, 0.030, 1.0)
PAPER_PALE = (0.61, 0.535, 0.335, 1.0)
BREAD_DARK = (0.38, 0.185, 0.055, 1.0)
BREAD_TOP = (0.56, 0.315, 0.105, 1.0)
BREAD_CRUMB = (0.67, 0.50, 0.265, 1.0)
GLASS_GREEN = (0.32, 0.43, 0.37, 1.0)
GLASS_EDGE = (0.42, 0.52, 0.45, 1.0)
LIQUID_CLEAR = (0.64, 0.67, 0.56, 1.0)
METAL = (0.37, 0.39, 0.36, 1.0)
METAL_LIGHT = (0.52, 0.53, 0.47, 1.0)
DEEP_RUST = (0.20, 0.060, 0.025, 1.0)
CAN_LABEL = (0.42, 0.14, 0.060, 1.0)
STEW_DARK = (0.31, 0.135, 0.050, 1.0)
STEW_MEAT = (0.46, 0.225, 0.085, 1.0)
STEW_FAT = (0.61, 0.455, 0.235, 1.0)
CARTON = (0.39, 0.325, 0.205, 1.0)
CARTON_EDGE = (0.49, 0.405, 0.260, 1.0)
EGG_SHELL = (0.77, 0.68, 0.50, 1.0)
EGG_MARK = (0.42, 0.305, 0.20, 1.0)


@dataclass
class Part:
    obj: "bpy.types.Object"
    item_id: str
    name: str
    role: str
    surface: str
    base_color: tuple[float, float, float, float]
    geometry: kit.Geometry


@dataclass
class Item:
    item_id: str
    root: "bpy.types.Object"
    parts: list[Part] = field(default_factory=list)


@dataclass
class AssetBuild:
    root: "bpy.types.Object"
    collection: "bpy.types.Collection"
    items: dict[str, Item] = field(default_factory=dict)
    materials: dict[tuple, "bpy.types.Material"] = field(default_factory=dict)


def stable(value: float) -> float:
    return round(float(value), 6)


def merge(items: Iterable[kit.Geometry]) -> kit.Geometry:
    return kit.merge_all(list(items))


def source_to_unity(position: Sequence[float]) -> tuple[float, float, float]:
    x, y, z = position
    return float(x), float(z), float(y)


def configure_scene() -> tuple["bpy.types.Collection", "bpy.types.Object"]:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    collection = bpy.data.collections.new("SupermarketProducts3D_Source")
    scene.collection.children.link(collection)
    root = bpy.data.objects.new(ROOT_NAME, None)
    collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.24
    root["bp_design_id"] = DESIGN_ID
    root["bp_passive"] = True
    scene["bp_design_id"] = DESIGN_ID
    scene["bp_generator_version"] = GENERATOR_VERSION
    return collection, root


def create_item(asset: AssetBuild, item_id: str) -> Item:
    root = bpy.data.objects.new(ROOT_NAMES[item_id], None)
    asset.collection.objects.link(root)
    root.parent = asset.root
    root.empty_display_type = "CIRCLE"
    root.empty_display_size = 0.10
    root["bp_item_id"] = item_id
    root["bp_role"] = item_id
    root["bp_pivot"] = "bottom_centre"
    root["bp_passive"] = True
    item = Item(item_id, root)
    asset.items[item_id] = item
    return item


def material_for(
    asset: AssetBuild,
    surface: str,
    base_color: Sequence[float],
) -> "bpy.types.Material":
    rgba = tuple(float(value) for value in base_color)
    key = (surface,) + rgba
    cached = asset.materials.get(key)
    if cached is not None:
        return cached
    material = bpy.data.materials.new(
        f"PREVIEW_Product_{surface}_{len(asset.materials):02d}"
    )
    material.diffuse_color = rgba
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        base = bsdf.inputs.get("Base Color")
        roughness = bsdf.inputs.get("Roughness")
        metallic = bsdf.inputs.get("Metallic")
        if base is not None:
            base.default_value = rgba
        if roughness is not None:
            roughness.default_value = {
                "glass": 0.31,
                "metal": 0.48,
                "paper": 0.82,
                "bread": 0.88,
                "carton": 0.94,
                "shell": 0.72,
            }.get(surface, 0.80)
        if metallic is not None:
            metallic.default_value = 0.42 if surface == "metal" else 0.0
        if surface == "glass":
            transmission = bsdf.inputs.get("Transmission Weight")
            if transmission is not None:
                transmission.default_value = 0.16
    asset.materials[key] = material
    return material


def assign_uv(mesh: "bpy.types.Mesh") -> None:
    layer = mesh.uv_layers.new(name="UVMap")
    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        for loop_index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if axis == 0:
                uv = (point.y, point.z)
            elif axis == 1:
                uv = (point.x, point.z)
            else:
                uv = (point.x, point.y)
            layer.data[loop_index].uv = uv


def add_part(
    asset: AssetBuild,
    item: Item,
    name: str,
    geometry: kit.Geometry,
    role: str,
    surface: str,
    base_color: Sequence[float],
) -> None:
    source_geometry = bp.to_source(geometry)
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(source_geometry[0], [], source_geometry[1])
    mesh.materials.append(material_for(asset, surface, base_color))
    mesh.update(calc_edges=True)
    assign_uv(mesh)
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    obj = bpy.data.objects.new(name, mesh)
    asset.collection.objects.link(obj)
    obj.parent = item.root
    obj["bp_item_id"] = item.item_id
    obj["bp_role"] = role
    obj["bp_group"] = "render"
    obj["bp_surface"] = surface
    rgba = tuple(float(value) for value in base_color)
    item.parts.append(Part(
        obj, item.item_id, name, role, surface, rgba, source_geometry
    ))


def ring_solid(
    profile: Sequence[tuple[float, float, float]],
    sides: int = 12,
) -> kit.Geometry:
    """Create a Unity-Y-axis solid from (height, radius_x, radius_z) rings."""
    rows: list[list[int]] = []
    vertices: list[tuple[float, float, float]] = []
    for height, radius_x, radius_z in profile:
        row = []
        for index in range(sides):
            angle = math.tau * index / sides
            row.append(len(vertices))
            vertices.append((
                math.cos(angle) * radius_x,
                height,
                math.sin(angle) * radius_z,
            ))
        rows.append(row)
    faces: list[tuple[int, ...]] = [tuple(rows[0]), tuple(reversed(rows[-1]))]
    for lower, upper in zip(rows, rows[1:]):
        for index in range(sides):
            following = (index + 1) % sides
            faces.append((
                lower[following], lower[index],
                upper[index], upper[following],
            ))
    return vertices, faces


def annulus(
    centre_y: float,
    outer_radius: float,
    inner_radius: float,
    height: float,
    sides: int = 14,
) -> kit.Geometry:
    """Create a closed Unity-Y-axis rolled rim with a real centre opening."""
    vertices: list[tuple[float, float, float]] = []
    rows: list[list[int]] = []
    for y, radius in (
        (centre_y - height * 0.5, outer_radius),
        (centre_y + height * 0.5, outer_radius),
        (centre_y - height * 0.5, inner_radius),
        (centre_y + height * 0.5, inner_radius),
    ):
        row = []
        for index in range(sides):
            angle = math.tau * index / sides
            row.append(len(vertices))
            vertices.append((
                math.cos(angle) * radius,
                y,
                math.sin(angle) * radius,
            ))
        rows.append(row)
    outer_low, outer_high, inner_low, inner_high = rows
    faces = []
    for index in range(sides):
        following = (index + 1) % sides
        faces.extend((
            (outer_low[following], outer_low[index],
             outer_high[index], outer_high[following]),
            (inner_low[index], inner_low[following],
             inner_high[following], inner_high[index]),
            (outer_low[index], outer_low[following],
             inner_low[following], inner_low[index]),
            (outer_high[index], inner_high[index],
             inner_high[following], outer_high[following]),
        ))
    return vertices, faces


def packet_hull(width: float, height: float, depth: float) -> kit.Geometry:
    half_x, half_z = width * 0.5, depth * 0.5
    cut = 0.025
    perimeter = (
        (half_x, 0.0), (half_x, half_z - cut),
        (half_x - cut, half_z), (-half_x + cut, half_z),
        (-half_x, half_z - cut), (-half_x, -half_z + cut),
        (-half_x + cut, -half_z), (half_x - cut, -half_z),
        (half_x, -half_z + cut),
    )
    vertices: list[tuple[float, float, float]] = []
    rings: list[list[int]] = []
    for y, scale in (
        (0.006, 0.88), (0.018, 1.0),
        (height - 0.014, 1.0), (height, 0.89),
    ):
        row = []
        for x, z in perimeter:
            row.append(len(vertices))
            vertices.append((x * scale, y, z * scale))
        rings.append(row)
    faces: list[tuple[int, ...]] = [tuple(rings[0]), tuple(reversed(rings[-1]))]
    for lower, upper in zip(rings, rings[1:]):
        for index in range(len(perimeter)):
            following = (index + 1) % len(perimeter)
            faces.append((
                lower[following], lower[index],
                upper[index], upper[following],
            ))
    return vertices, faces


def egg_geometry(width: float, height: float, depth: float) -> kit.Geometry:
    normalized = (
        (0.0, 0.025), (0.10, 0.34), (0.34, 0.49),
        (0.62, 0.43), (0.84, 0.27), (1.0, 0.018),
    )
    profile = [
        (height * y, width * radius, depth * radius)
        for y, radius in normalized
    ]
    return ring_solid(profile, 12)


def build_instant_noodles(asset: AssetBuild) -> None:
    item = create_item(asset, "instant_noodles")
    add_part(
        asset, item, "Instant Noodles Packet",
        packet_hull(0.306, 0.080, 0.238),
        "packet", "paper", PACKET_ORANGE,
    )
    seams = [
        bp.u_box((side * 0.164, 0.032, 0.0), (0.028, 0.064, 0.248), 0.010)
        for side in (-1.0, 1.0)
    ]
    for side in (-1.0, 1.0):
        for offset in (-0.064, 0.0, 0.064):
            seams.append(bp.u_box(
                (side * 0.166, 0.066, offset),
                (0.020, 0.010, 0.026),
                0.003,
            ))
    add_part(
        asset, item, "Instant Noodles Crimped Seals", merge(seams),
        "seal", "paper", PACKET_DARK,
    )
    add_part(
        asset, item, "Instant Noodles Blank Label",
        bp.u_box((0.0, 0.084, 0.0), (0.192, 0.008, 0.145), 0.006),
        "label", "paper", PAPER_PALE,
    )
    add_part(
        asset, item, "Instant Noodles Colour Band",
        bp.u_box((0.0, 0.089, -0.041), (0.136, 0.004, 0.025), 0.002),
        "label", "paper", BREAD_DARK,
    )


def build_day_old_loaf(asset: AssetBuild) -> None:
    item = create_item(asset, "day_old_loaf")
    profile = (
        (-0.175, 0.040, 0.044),
        (-0.142, 0.078, 0.094),
        (0.115, 0.080, 0.100),
        (0.156, 0.064, 0.078),
        (0.175, 0.032, 0.038),
    )
    hull = ring_solid(profile, 12)
    hull = bp.u_rotated(hull, (0.0, 0.0, -90.0))
    hull = kit.translated(hull, (0.0, 0.080, 0.0))
    add_part(
        asset, item, "Day Old Loaf Crust", hull,
        "bread", "bread", BREAD_DARK,
    )
    add_part(
        asset, item, "Day Old Loaf Raised Top",
        bp.u_box((0.015, 0.131, 0.0), (0.286, 0.055, 0.176), 0.024),
        "bread", "bread", BREAD_TOP,
    )
    end = bp.u_cylinder((0.0, 0.080, 0.0), (0.112, 0.004, 0.146), 12)
    end = bp.u_rotated_about(
        end, (0.0, 0.0, 90.0), (0.0, 0.080, 0.0)
    )
    end = kit.translated(end, (-0.177, 0.0, 0.0))
    add_part(
        asset, item, "Day Old Loaf Cut End", end,
        "crumb", "bread", BREAD_CRUMB,
    )
    scores = []
    for x in (-0.075, 0.015, 0.105):
        score = bp.u_box((x, 0.161, 0.0), (0.022, 0.010, 0.128), 0.004)
        scores.append(bp.u_rotated_about(
            score, (0.0, 0.0, -16.0), (x, 0.161, 0.0)
        ))
    add_part(
        asset, item, "Day Old Loaf Scores", merge(scores),
        "score", "bread", (0.275, 0.115, 0.030, 1.0),
    )


def build_vodka_bottle(asset: AssetBuild) -> None:
    item = create_item(asset, "vodka_bottle")
    bottle = ring_solid((
        (0.000, 0.064, 0.064),
        (0.010, 0.073, 0.073),
        (0.282, 0.073, 0.073),
        (0.320, 0.066, 0.066),
        (0.350, 0.032, 0.032),
        (0.430, 0.027, 0.027),
        (0.440, 0.030, 0.030),
    ), 14)
    add_part(
        asset, item, "Vodka Bottle Glass", bottle,
        "glass", "glass", GLASS_GREEN,
    )
    add_part(
        asset, item, "Vodka Bottle Base Glass Ring",
        bp.u_cylinder((0.0, 0.012, 0.0), (0.149, 0.012, 0.149), 14),
        "glass", "glass", GLASS_EDGE,
    )
    add_part(
        asset, item, "Vodka Bottle Generic Paper Band",
        bp.u_cylinder((0.0, 0.190, 0.0), (0.151, 0.066, 0.151), 14),
        "label", "paper", PAPER_PALE,
    )
    add_part(
        asset, item, "Vodka Bottle Faded Label Stripe",
        bp.u_cylinder((0.0, 0.190, 0.0), (0.153, 0.010, 0.153), 14),
        "label", "paper", (0.205, 0.285, 0.34, 1.0),
    )
    add_part(
        asset, item, "Vodka Bottle Clear Contents",
        bp.u_cylinder((0.0, 0.075, 0.0), (0.126, 0.060, 0.126), 14),
        "liquid", "glass", LIQUID_CLEAR,
    )
    add_part(
        asset, item, "Vodka Bottle Metal Cap",
        bp.u_cylinder((0.0, 0.445, 0.0), (0.065, 0.015, 0.065), 14),
        "cap", "metal", METAL,
    )


def build_closed_stew_can(asset: AssetBuild) -> None:
    item = create_item(asset, "closed_stew_can")
    add_part(
        asset, item, "Closed Stew Can Body",
        bp.u_cylinder((0.0, 0.090, 0.0), (0.210, 0.084, 0.210), 14),
        "can_body", "metal", METAL,
    )
    add_part(
        asset, item, "Closed Stew Can Generic Paper Label",
        bp.u_cylinder((0.0, 0.095, 0.0), (0.214, 0.052, 0.214), 14),
        "label", "paper", CAN_LABEL,
    )
    add_part(
        asset, item, "Closed Stew Can Blank Label Band",
        bp.u_cylinder((0.0, 0.095, 0.0), (0.217, 0.010, 0.217), 14),
        "label", "paper", PAPER_PALE,
    )
    rims = merge((
        bp.u_cylinder((0.0, 0.006, 0.0), (0.224, 0.006, 0.224), 14),
        bp.u_cylinder((0.0, 0.184, 0.0), (0.224, 0.006, 0.224), 14),
    ))
    add_part(
        asset, item, "Closed Stew Can Rolled Rims", rims,
        "rim", "metal", METAL_LIGHT,
    )
    add_part(
        asset, item, "Closed Stew Can Sealed Lid",
        bp.u_cylinder((0.0, 0.181, 0.0), (0.202, 0.004, 0.202), 14),
        "rim", "metal", METAL,
    )
    add_part(
        asset, item, "Closed Stew Can Pull Tab",
        bp.u_box((0.0, 0.191, -0.026), (0.055, 0.006, 0.026), 0.005),
        "pull_tab", "metal", DEEP_RUST,
    )


def build_open_stew_can(asset: AssetBuild) -> None:
    item = create_item(asset, "open_stew_can")
    add_part(
        asset, item, "Open Stew Can Body",
        bp.u_cylinder((0.0, 0.090, 0.0), (0.210, 0.084, 0.210), 14),
        "can_body", "metal", METAL,
    )
    add_part(
        asset, item, "Open Stew Can Generic Paper Label",
        bp.u_cylinder((0.0, 0.095, 0.0), (0.214, 0.052, 0.214), 14),
        "label", "paper", (0.47, 0.22, 0.09, 1.0),
    )
    add_part(
        asset, item, "Open Stew Can Blank Label Band",
        bp.u_cylinder((0.0, 0.095, 0.0), (0.217, 0.010, 0.217), 14),
        "label", "paper", PAPER_PALE,
    )
    rims = merge((
        bp.u_cylinder((0.0, 0.006, 0.0), (0.224, 0.006, 0.224), 14),
        annulus(0.184, 0.112, 0.094, 0.012, 14),
    ))
    add_part(
        asset, item, "Open Stew Can Rolled Rims", rims,
        "rim", "metal", METAL_LIGHT,
    )
    add_part(
        asset, item, "Open Stew Can Visible Stew",
        bp.u_cylinder((0.0, 0.182, 0.0), (0.182, 0.005, 0.182), 14),
        "stew", "food", STEW_DARK,
    )
    chunks = merge((
        bp.u_box((-0.035, 0.191, -0.018), (0.050, 0.018, 0.040), 0.006),
        bp.u_box((0.034, 0.190, 0.025), (0.044, 0.016, 0.035), 0.005),
        bp.u_box((0.025, 0.195, -0.050), (0.030, 0.014, 0.026), 0.005),
    ))
    add_part(
        asset, item, "Open Stew Can Contents Chunks", chunks,
        "stew", "food", STEW_MEAT,
    )
    add_part(
        asset, item, "Open Stew Can Pale Fat Piece",
        bp.u_box((0.025, 0.198, -0.050), (0.016, 0.008, 0.014), 0.003),
        "stew", "food", STEW_FAT,
    )
    hinge = (0.0, 0.190, 0.100)
    lid = bp.u_box((0.0, 0.190, 0.060), (0.190, 0.006, 0.080), 0.018)
    lid = bp.u_rotated_about(lid, (42.0, 0.0, 0.0), hinge)
    add_part(
        asset, item, "Open Stew Can Peeled Bent Lid", lid,
        "lid", "metal", METAL_LIGHT,
    )
    tab = bp.u_box((0.0, 0.195, 0.062), (0.052, 0.006, 0.024), 0.005)
    tab = bp.u_rotated_about(tab, (42.0, 0.0, 0.0), hinge)
    add_part(
        asset, item, "Open Stew Can Lid Pull Tab", tab,
        "pull_tab", "metal", DEEP_RUST,
    )


def build_chicken_egg(asset: AssetBuild) -> None:
    item = create_item(asset, "chicken_egg")
    carton = [bp.u_box(
        (0.0, 0.016, 0.0), (0.200, 0.032, 0.160), 0.014
    )]
    for x in (-0.055, 0.055):
        carton.append(bp.u_tapered_cylinder(
            (x, 0.040, 0.0), (0.072, 0.026, 0.072), 1.22, 10
        ))
    add_part(
        asset, item, "Chicken Egg Carton Cup", merge(carton),
        "carton", "carton", CARTON,
    )
    add_part(
        asset, item, "Chicken Egg Carton Flaps", merge((
            bp.u_box(
                (-0.091, 0.050, 0.0), (0.018, 0.070, 0.160), 0.008
            ),
            bp.u_box(
                (0.091, 0.050, 0.0), (0.018, 0.070, 0.160), 0.008
            ),
        )),
        "carton", "carton", CARTON_EDGE,
    )
    egg = kit.translated(egg_geometry(0.105, 0.150, 0.105), (0.0, 0.035, 0.0))
    add_part(
        asset, item, "Chicken Egg Shell", egg,
        "shell", "shell", EGG_SHELL,
    )
    add_part(
        asset, item, "Chicken Egg Shell Mark",
        bp.u_box((-0.019, 0.139, -0.047), (0.013, 0.010, 0.005), 0.002),
        "shell_mark", "shell", EGG_MARK,
    )


def build() -> AssetBuild:
    collection, root = configure_scene()
    asset = AssetBuild(root, collection)
    build_instant_noodles(asset)
    build_day_old_loaf(asset)
    build_vodka_bottle(asset)
    build_closed_stew_can(asset)
    build_open_stew_can(asset)
    build_chicken_egg(asset)
    return asset


def item_geometry(item: Item) -> kit.Geometry:
    return merge(part.geometry for part in item.parts)


def validate(asset: AssetBuild) -> dict:
    problems: list[str] = []
    if asset.root.name != ROOT_NAME:
        problems.append(f"pack root must be '{ROOT_NAME}'")
    if set(asset.items) != set(ITEM_IDS):
        problems.append("pack item ids differ from the six-item contract")
    names: set[str] = set()
    item_reports = {}
    for item_id in ITEM_IDS:
        item = asset.items.get(item_id)
        if item is None:
            continue
        root = item.root
        if root.name != ROOT_NAMES[item_id] or root.parent != asset.root:
            problems.append(f"'{item_id}' lost its canonical source root")
        if (
            tuple(root.location) != (0.0, 0.0, 0.0)
            or tuple(root.rotation_euler) != (0.0, 0.0, 0.0)
            or tuple(root.scale) != (1.0, 1.0, 1.0)
        ):
            problems.append(f"'{item_id}' root is not an identity pivot")
        roles = set()
        for part in item.parts:
            roles.add(part.role)
            if part.name in names:
                problems.append(f"duplicate renderer name '{part.name}'")
            names.add(part.name)
            if part.obj.parent != root or part.obj.type != "MESH":
                problems.append(f"'{part.name}' escaped its item root")
            if len(part.obj.data.uv_layers) != 1:
                problems.append(f"'{part.name}' needs exactly one UV set")
            if bp.signed_volume(part.geometry) <= 1e-9:
                problems.append(f"'{part.name}' has open or inverted winding")
        missing_roles = REQUIRED_ROLES[item_id] - roles
        if missing_roles:
            problems.append(f"'{item_id}' lacks roles {sorted(missing_roles)}")
        geometry = item_geometry(item)
        low, high = kit.bounds(geometry)
        unity_low = source_to_unity(low)
        unity_high = source_to_unity(high)
        source_dimensions = tuple(
            high[index] - low[index] for index in range(3)
        )
        unity_dimensions = source_to_unity(source_dimensions)
        available = AVAILABLE_SIZES[item_id]
        if abs(unity_low[1]) > 1e-6:
            problems.append(f"'{item_id}' bottom pivot is {unity_low[1]:.6f}m")
        centre_x = (unity_low[0] + unity_high[0]) * 0.5
        centre_z = (unity_low[2] + unity_high[2]) * 0.5
        if abs(centre_x) > 0.012 or abs(centre_z) > 0.012:
            problems.append(f"'{item_id}' pivot is not centred under its bounds")
        for axis in range(3):
            if unity_dimensions[axis] > available[axis] + 1e-6:
                problems.append(
                    f"'{item_id}' exceeds slot axis {axis}: "
                    f"{unity_dimensions[axis]:.4f}>{available[axis]:.4f}"
                )
        if item_id == "vodka_bottle" and unity_high[1] > 0.475:
            problems.append("vodka bottle violates selected-state shelf clearance")
        triangles = kit.triangle_count(geometry)
        item_reports[item_id] = {
            "bounds_min": [stable(value) for value in low],
            "bounds_max": [stable(value) for value in high],
            "unity_bounds_min": [stable(value) for value in unity_low],
            "unity_bounds_max": [stable(value) for value in unity_high],
            "dimensions_m": [stable(value) for value in unity_dimensions],
            "mesh_count": len(item.parts),
            "triangle_count": triangles,
        }
    all_parts = [
        part for item in asset.items.values() for part in item.parts
    ]
    geometry = merge(part.geometry for part in all_parts)
    low, high = kit.bounds(geometry)
    triangles = kit.triangle_count(geometry)
    if len(all_parts) > 40:
        problems.append(f"pack fragments into {len(all_parts)} meshes")
    if triangles > 8000:
        problems.append(
            f"pack costs {triangles} triangles against its 8000 cap"
        )
    source_types = {obj.type for obj in asset.collection.objects}
    if source_types - {"EMPTY", "MESH"}:
        problems.append(
            f"source contains forbidden object types {source_types}"
        )
    if bpy.data.actions:
        problems.append("passive product pack must contain no Actions")
    if problems:
        raise SystemExit(
            "Supermarket product validation failed:\n  "
            + "\n  ".join(problems)
        )
    return {
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
        "unity_bounds_min": [
            stable(value) for value in source_to_unity(low)
        ],
        "unity_bounds_max": [
            stable(value) for value in source_to_unity(high)
        ],
        "mesh_count": len(all_parts),
        "triangle_count": triangles,
        "items": item_reports,
    }


def signature_for(asset: AssetBuild) -> str:
    payload = {
        "design_id": DESIGN_ID,
        "generator_version": GENERATOR_VERSION,
        "items": [
            {
                "id": item_id,
                "root": ROOT_NAMES[item_id],
                "parts": [
                    {
                        "name": part.name,
                        "role": part.role,
                        "surface": part.surface,
                        "base_color": [
                            stable(value) for value in part.base_color
                        ],
                        "vertices": [
                            [stable(value) for value in vertex]
                            for vertex in part.geometry[0]
                        ],
                        "faces": [
                            list(face) for face in part.geometry[1]
                        ],
                    }
                    for part in asset.items[item_id].parts
                ],
            }
            for item_id in ITEM_IDS
        ],
    }
    encoded = json.dumps(
        payload, sort_keys=True, separators=(",", ":")
    )
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def part_manifest(part: Part) -> dict:
    low, high = kit.bounds(part.geometry)
    return {
        "name": part.name,
        "item_id": part.item_id,
        "role": part.role,
        "group": "render",
        "surface": part.surface,
        "sheet": "",
        "base_color": [
            stable(value) for value in part.base_color
        ],
        "casts_shadows": True,
        "shadows": True,
        "vertices": len(part.geometry[0]),
        "triangles": kit.triangle_count(part.geometry),
        "bounds_min": [stable(value) for value in low],
        "bounds_max": [stable(value) for value in high],
        "unity_bounds_min": [
            stable(value) for value in source_to_unity(low)
        ],
        "unity_bounds_max": [
            stable(value) for value in source_to_unity(high)
        ],
    }


def manifest_for(asset: AssetBuild, report: dict, signature: str) -> dict:
    return {
        "generator": GENERATOR,
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "root_name": ROOT_NAME,
        "layout_mode": "coincident_identity_item_roots_for_extraction",
        "source_axes": {
            "right": "+X", "forward": "+Y", "up": "+Z"
        },
        "unity_axes": {
            "right": "+X",
            "forward": "+Z",
            "up": "+Y",
            "fbx_axis_forward": "-Z",
            "fbx_axis_up": "Y",
            "bake_space_transform": False,
        },
        "pivot_contract": "bottom_centre",
        "authored_text": [],
        "brands": [],
        "colliders": False,
        "materials": False,
        "lights": False,
        "cameras": False,
        "rigidbodies": False,
        "audio_sources": False,
        "animation_count": 0,
        "item_count": len(ITEM_IDS),
        "mesh_count": report["mesh_count"],
        "triangle_count": report["triangle_count"],
        "bounds_min": report["bounds_min"],
        "bounds_max": report["bounds_max"],
        "unity_bounds_min": report["unity_bounds_min"],
        "unity_bounds_max": report["unity_bounds_max"],
        "budgets": {
            "maximum_renderers": 40,
            "maximum_triangles": 8000,
        },
        "items": [
            {
                "id": item_id,
                "source_name": ROOT_NAMES[item_id],
                "role": item_id,
                "pivot": {
                    "kind": "bottom_centre",
                    "source_position": [0.0, 0.0, 0.0],
                    "unity_position": [0.0, 0.0, 0.0],
                },
                "available_size_m": list(AVAILABLE_SIZES[item_id]),
                **report["items"][item_id],
                "parts": [
                    part.name for part in asset.items[item_id].parts
                ],
            }
            for item_id in ITEM_IDS
        ],
        "parts": [
            part_manifest(part)
            for item_id in ITEM_IDS
            for part in asset.items[item_id].parts
        ],
        "build_signature": signature,
    }


def write_manifest(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, indent=2) + "\n",
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


def preview_material(
    name: str,
    color: Sequence[float],
) -> "bpy.types.Material":
    material = bpy.data.materials.new(name)
    material.diffuse_color = tuple(color)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = tuple(color)
        bsdf.inputs["Roughness"].default_value = 0.88
    return material


def render_preview(asset: AssetBuild, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.new(
        "PRESENTATION_SupermarketProducts3D"
    )
    scene.collection.children.link(presentation)
    saved_transforms = {}
    positions = (-1.68, -1.01, -0.34, 0.34, 1.01, 1.68)
    angles = (-12.0, 9.0, -8.0, 11.0, -9.0, 10.0)
    for item_id, x, angle in zip(ITEM_IDS, positions, angles):
        item_root = asset.items[item_id].root
        saved_transforms[item_id] = (
            item_root.location.copy(),
            item_root.rotation_euler.copy(),
        )
        item_root.location = (x, 0.0, 0.0)
        item_root.rotation_euler[2] = math.radians(angle)
    bpy.ops.mesh.primitive_cube_add(
        location=(0.0, 0.0, -0.05),
        scale=(2.16, 0.66, 0.05),
    )
    stage = bpy.context.object
    stage.name = "PREVIEW_ProductShelf"
    for collection in list(stage.users_collection):
        collection.objects.unlink(stage)
    presentation.objects.link(stage)
    stage.data.materials.append(preview_material(
        "PREVIEW_ProductShelfMaterial",
        (0.20, 0.235, 0.205, 1.0),
    ))
    target = Vector((0.0, 0.0, 0.16))
    for name, location, energy, color, size in (
        ("Key", (-2.7, -3.8, 3.5), 850, (0.77, 0.86, 0.78), 4.0),
        ("Fill", (3.2, -1.0, 2.3), 620, (0.91, 0.72, 0.46), 3.0),
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
            target - Vector(location)
        ).to_track_quat("-Z", "Y").to_euler()
    camera_data = bpy.data.cameras.new(
        "PREVIEW_SupermarketProductsCamera"
    )
    camera = bpy.data.objects.new(
        "PREVIEW_SupermarketProductsCamera", camera_data
    )
    presentation.objects.link(camera)
    camera.location = (2.75, -4.7, 2.25)
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 58
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 620
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.render.filepath = str(path)
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new(
        "PREVIEW_SupermarketProductsWorld"
    )
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (
            0.015, 0.021, 0.018, 1.0
        )
        background.inputs["Strength"].default_value = 0.22
    scene.world = world
    bpy.ops.render.render(write_still=True)
    for item_id, (location, rotation) in saved_transforms.items():
        asset.items[item_id].root.location = location
        asset.items[item_id].root.rotation_euler = rotation
    scene.camera = None
    scene.world = None
    for obj in list(presentation.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(presentation)
    bpy.data.worlds.remove(world)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(
        filepath=str(path),
        check_existing=False,
    )


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
            "SUPERMARKET PRODUCT PACK VALID: "
            f"{len(asset.items)} items / {report['mesh_count']} meshes / "
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
        "Supermarket product pack written: "
        f"{len(asset.items)} items / {report['mesh_count']} meshes / "
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
