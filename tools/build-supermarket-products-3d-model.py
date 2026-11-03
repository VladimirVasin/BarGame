#!/usr/bin/env python3
"""Build the eight passive Blender-authored supermarket product models."""

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
GENERATOR_VERSION = "2.0.1"
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
    "beer_can",
    "wine_bottle",
)
ROOT_NAMES = {item_id: f"ITEM_{item_id}" for item_id in ITEM_IDS}
AVAILABLE_SIZES = {
    "beer_can": (.10, .18, .10),
    "wine_bottle": (.10, .32, .10),
    "instant_noodles": (0.48, 0.34, 0.30),
    "day_old_loaf": (0.56, 0.32, 0.34),
    "vodka_bottle": (0.27, 0.30, 0.25),
    "closed_stew_can": (0.42, 0.34, 0.36),
    "open_stew_can": (0.31, 0.48, 0.28),
    "chicken_egg": (0.48, 0.34, 0.36),
}
REQUIRED_ROLES = {
    "beer_can": {"can_body", "label", "rim", "cap", "can_seal", "liquid", "liquid_surface"},
    "wine_bottle": {"glass", "label", "cap", "liquid", "liquid_surface"},
    "instant_noodles": {"packet", "seal", "label"},
    "day_old_loaf": {"bread", "crumb", "score"},
    "vodka_bottle": {"glass", "label", "cap", "liquid", "liquid_surface"},
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
        packet_hull(0.306, 0.060, 0.238),
        "packet", "paper", PACKET_ORANGE,
    )
    seams = [
        bp.u_box((side * 0.164, 0.020, 0.0), (0.028, 0.040, 0.248), 0.010)
        for side in (-1.0, 1.0)
    ]
    for side in (-1.0, 1.0):
        for offset in (-0.064, 0.0, 0.064):
            seams.append(bp.u_box(
                (side * 0.166, 0.041, offset),
                (0.020, 0.010, 0.026),
                0.003,
            ))
    add_part(
        asset, item, "Instant Noodles Crimped Seals", merge(seams),
        "seal", "paper", PACKET_DARK,
    )
    add_part(
        asset, item, "Instant Noodles Blank Label",
        bp.u_box((0.0, 0.062, 0.0), (0.192, 0.004, 0.145), 0.006),
        "label", "paper", PAPER_PALE,
    )
    add_part(
        asset, item, "Instant Noodles Colour Band",
        bp.u_box((0.0, 0.065, -0.041), (0.136, 0.002, 0.025), 0.002),
        "label", "paper", BREAD_DARK,
    )


def build_day_old_loaf(asset: AssetBuild) -> None:
    item = create_item(asset, "day_old_loaf")
    # The cuts are depressions in the crust profile, not raised rectangular bars.
    stations = [(-.175,.020),(-.160,.050),(-.130,.072),(-.095,.078),(-.084,.065),
        (-.070,.079),(-.018,.081),(-.007,.067),(.007,.081),(.058,.079),(.070,.065),
        (.084,.077),(.130,.070),(.160,.044),(.175,.020)]
    verts=[]
    sides=12
    for x,radius in stations:
        for i in range(sides):
            a=math.tau*i/sides
            base = .081 if abs(x)<.14 else radius
            # A bread score cuts the crown only; the sides/bottom stay continuous.
            groove = max(0., base-radius) * max(0., math.cos(a))**4
            verts.append((x,.081+math.cos(a)*(base-groove),math.sin(a)*base*1.12))
    faces=[tuple(reversed(range(sides))),tuple((len(stations)-1)*sides+i for i in range(sides))]
    for row in range(len(stations)-1):
        for i in range(sides):
            j=(i+1)%sides
            faces.append((row*sides+i,row*sides+j,(row+1)*sides+j,(row+1)*sides+i))
    geom=(verts,faces)
    if bp.signed_volume(geom)<0: geom=(verts,[tuple(reversed(f)) for f in faces])
    add_part(asset,item,"Day Old Loaf Crust",geom,"bread","bread",BREAD_DARK)
    end=bp.u_cylinder((0.,.081,0.),(.034,.0015,.038),12)
    end=bp.u_rotated_about(end,(0.,0.,90.),(0.,.081,0.))
    add_part(asset,item,"Day Old Loaf Cut End",kit.translated(end,(-.176,0.,0.)),"crumb","bread",BREAD_CRUMB)
    cuts=[]
    for x in (-.084,-.007,.070):
        cuts.append(bp.u_box((x,.145,0.),(.012,.003,.063),.002))
    add_part(asset,item,"Day Old Loaf Scored Crumb",merge(cuts),"score","bread",BREAD_TOP)


VODKA_SOURCE_SCALE = .65
VODKA_RADIAL_SCALE = .75
VODKA_GRIP_Y = .215 / VODKA_SOURCE_SCALE
VODKA_BODY_TOP_Y = .150 / VODKA_SOURCE_SCALE
VODKA_NECK_START_Y = .175 / VODKA_SOURCE_SCALE
VODKA_NECK_RADIUS = .0147796875 / (VODKA_SOURCE_SCALE * VODKA_RADIAL_SCALE)
VODKA_NECK_INNER_RADIUS = VODKA_NECK_RADIUS - .004
VODKA_INNER_PROFILE = ((.012, .060), (.020, .068), (VODKA_BODY_TOP_Y, .068),
    (VODKA_NECK_START_Y, VODKA_NECK_INNER_RADIUS), (.350, VODKA_NECK_INNER_RADIUS),
    (.430, VODKA_NECK_INNER_RADIUS), (.436, VODKA_NECK_INNER_RADIUS * .4 + .028 * .6))


def bottle_cell(profile, sector, sides=16):
    """Closed quarter-shell; lower cells include the physical glass bottom."""
    vertices = []
    steps = sides // 4
    for y, radius in profile:
        for index in range(steps + 1):
            angle = math.tau * (sector * steps + index) / sides
            vertices.append((math.cos(angle) * radius, y, math.sin(angle) * radius))
    width = steps + 1
    faces = []
    for row in range(len(profile)):
        following = (row + 1) % len(profile)
        for index in range(steps):
            faces.append((row * width + index + 1, row * width + index,
                following * width + index, following * width + index + 1))
    faces.append(tuple(row * width for row in reversed(range(len(profile)))))
    faces.append(tuple(row * width + steps for row in range(len(profile))))
    geometry = vertices, faces
    # Winding is checked before Blender's handedness conversion too.
    if bp.signed_volume(geometry) < 0:
        geometry = vertices, [tuple(reversed(face)) for face in faces]
    return geometry


def bottle_anchor(asset, item, name, point):
    anchor = bpy.data.objects.new(name, None)
    if anchor.name != name:
        raise ValueError(f"Duplicate globally-authored contact: {name} became {anchor.name}")
    asset.collection.objects.link(anchor)
    anchor.parent = item.root
    anchor.location = (point[0], point[2], point[1])
    anchor.empty_display_size = .025
    return anchor


def build_vodka_bottle(asset: AssetBuild) -> None:
    item = create_item(asset, "vodka_bottle")
    profiles = (
        ((0., .064), (.010, .073), (.218, .073), (.218, .069),
         (.020, .068), (.012, .060), (.012, 0.), (0., 0.)),
        ((.218, .073), (VODKA_BODY_TOP_Y, .073), (VODKA_NECK_START_Y, VODKA_NECK_RADIUS),
         (.350, VODKA_NECK_RADIUS), (.430, VODKA_NECK_RADIUS), (.440, .032),
         (.440, .028), (.430, VODKA_NECK_INNER_RADIUS), (.350, VODKA_NECK_INNER_RADIUS),
         (VODKA_NECK_START_Y, VODKA_NECK_INNER_RADIUS), (VODKA_BODY_TOP_Y, .068), (.218, .069)),
    )
    for band, profile in enumerate(profiles):
        for sector in range(4):
            add_part(asset, item, f"Vodka Bottle Glass Cell {band * 4 + sector:02}",
                bottle_cell(profile, sector), "glass", "glass", GLASS_GREEN)
    add_part(asset, item, "Vodka Bottle Generic Paper Band",
        bp.u_cylinder((0., .165, 0.), (.151, .050, .151), 16),
        "label", "paper", PAPER_PALE)
    add_part(asset, item, "Vodka Bottle Faded Label Stripe",
        bp.u_cylinder((0., .165, 0.), (.153, .010, .153), 16),
        "label", "paper", (.205, .285, .34, 1.))
    add_part(asset, item, "Vodka Bottle Clear Contents",
        ring_solid(tuple((y, radius, radius) for y, radius in VODKA_INNER_PROFILE), 16),
        "liquid", "glass", LIQUID_CLEAR)
    add_part(asset, item, "Vodka Bottle Liquid Surface",
        bp.u_cylinder((0., .280, 0.), (.136, .0002, .136), 32),
        "liquid_surface", "glass", LIQUID_CLEAR)
    add_part(asset, item, "Vodka Bottle Metal Cap",
        bp.u_cylinder((0., .445, 0.), (.065, .015, .065), 16),
        "cap", "metal", METAL)
    # A movable cap pivot must be at the cap, while manifest geometry remains metres.
    cap = item.parts[-1].obj
    offset = Vector((0., 0., .445))
    for vertex in cap.data.vertices:
        vertex.co -= offset
    cap.location = offset
    bottle_anchor(asset, item, "VodkaGrip", (0., VODKA_GRIP_Y, 0.))
    bottle_anchor(asset, item, "VodkaMouth", (0., .440, 0.))
    # Scale authored metres, never the imported runtime wrapper. Other products remain untouched.
    for part in item.parts:
        source_scale = Vector((VODKA_SOURCE_SCALE * VODKA_RADIAL_SCALE,
            VODKA_SOURCE_SCALE * VODKA_RADIAL_SCALE, VODKA_SOURCE_SCALE))
        for vertex in part.obj.data.vertices:
            vertex.co = Vector(tuple(vertex.co[axis] * source_scale[axis] for axis in range(3)))
        part.obj.location = Vector(tuple(part.obj.location[axis] * source_scale[axis] for axis in range(3)))
        part.geometry = ([tuple(vertex[axis] * source_scale[axis] for axis in range(3))
            for vertex in part.geometry[0]], part.geometry[1])
    for name in ("VodkaGrip", "VodkaMouth"):
        bpy.data.objects[name].location *= VODKA_SOURCE_SCALE


def movable_pivot(part, unity_point):
    offset = Vector((unity_point[0], unity_point[2], unity_point[1]))
    for vertex in part.obj.data.vertices:
        vertex.co -= offset
    part.obj.location = offset


def build_wine_bottle(asset):
    item = create_item(asset, "wine_bottle")
    profiles = (
        ((0., .032), (.008, .0385), (.120, .0385), (.120, .035),
         (.016, .035), (.009, .031), (.009, 0.), (0., 0.)),
        ((.120, .0385), (.184, .0385), (.216, .0147796875), (.284, .0147796875),
         (.294, .0165), (.294, .0125), (.284, .0125), (.216, .0125), (.184, .035), (.120, .035)),
    )
    for band, profile in enumerate(profiles):
        for sector in range(4):
            add_part(asset, item, f"Wine Bottle Glass Cell {band*4+sector:02}",
                bottle_cell(profile, sector), "glass", "glass", (.16,.27,.13,1.))
    add_part(asset, item, "Wine Bottle Faded Paper Label", bp.u_cylinder((0.,.115,0.),(.0778,.047,.0778),16),
        "label", "paper", (.48,.42,.28,1.))
    add_part(asset, item, "Wine Bottle Label Rule", bp.u_cylinder((0.,.115,0.),(.0782,.008,.0782),16),
        "label", "paper", (.24,.12,.10,1.))
    inner = ((.009,.031),(.016,.035),(.184,.035),(.216,.0125),(.249,.0125),(.284,.0125),(.292,.0125))
    add_part(asset,item,"Wine Bottle Contents",ring_solid(tuple((y,r,r) for y,r in inner),16),
        "liquid","glass",(.30,.035,.025,.78))
    add_part(asset,item,"Wine Bottle Liquid Surface",bp.u_cylinder((0.,.175,0.),(.07,.0002,.07),32),
        "liquid_surface","glass",(.34,.045,.028,.88))
    add_part(asset,item,"Wine Bottle Screw Cap",bp.u_cylinder((0.,.297,0.),(.035,.008,.035),16),
        "cap","metal",(.20,.16,.12,1.))
    movable_pivot(item.parts[-1], (0.,.297,0.))
    bottle_anchor(asset,item,"WineBottleGrip",(0.,.232,0.))
    bottle_anchor(asset,item,"WineBottleMouth",(0.,.294,0.))


def build_beer_can(asset):
    item = create_item(asset, "beer_can")
    wall = ((0.,.029),(.004,.033),(.012,.037),(.130,.037),(.145,.032),(.149,.032),
        (.149,.030),(.140,.033),(.120,.0355),(.012,.0355),(.006,.030),(.006,0.),(0.,0.))
    add_part(asset,item,"Beer Can Drawn Aluminium",merge(bottle_cell(wall,i,16) for i in range(4)),
        "can_body","metal",(.34,.37,.32,1.))
    add_part(asset,item,"Beer Can Faded Sleeve",bp.u_cylinder((0.,.076,0.),(.0742,.045,.0742),16),
        "label","paper",(.37,.39,.20,1.))
    add_part(asset,item,"Beer Can Blank Band",bp.u_cylinder((0.,.076,0.),(.0744,.008,.0744),16),
        "label","paper",(.55,.50,.34,1.))
    # A real off-centre opening. The top sheet connects its outer perimeter to the aperture.
    vertices, faces = annulus(.149,.032,.0095,.0015,16)
    vertices = [(x,y,z-.012 if index>=32 else z) for index,(x,y,z) in enumerate(vertices)]
    lid = (vertices,faces)
    add_part(asset,item,"Beer Can Rolled Top",merge((lid,annulus(.150,.033,.0305,.004,16))),
        "rim","metal",METAL_LIGHT)
    add_part(asset,item,"Beer Can Sealed Aperture",bp.u_cylinder((0.,.149,-.012),(.019,.0008,.019),16),
        "can_seal","metal",METAL)
    tab = kit.translated(annulus(.152,.007,.0035,.0015,12),(0.,0.,.006))
    add_part(asset,item,"Beer Can Retained Ring",tab,"cap","metal",METAL_LIGHT)
    movable_pivot(item.parts[-1], (0.,.152,0.))
    inner=((.006,.030),(.012,.0355),(.035,.0355),(.075,.0355),(.120,.0355),(.140,.033),(.148,.030))
    add_part(asset,item,"Beer Can Contents",ring_solid(tuple((y,r,r) for y,r in inner),16),
        "liquid","glass",(.48,.28,.065,.95))
    add_part(asset,item,"Beer Can Liquid Surface",bp.u_cylinder((0.,.125,0.),(.071,.0002,.071),32),
        "liquid_surface","glass",(.63,.43,.14,.96))
    bottle_anchor(asset,item,"BeerCanGrip",(0.,.080,0.))
    bottle_anchor(asset,item,"BeerCanMouth",(0.,.149,-.012))


def scale_item(item, factors):
    # Convert geometry, not the runtime root. Bottom pivots stay measured metres.
    scale=Vector((factors[0],factors[2],factors[1]))
    for part in item.parts:
        for vertex in part.obj.data.vertices:
            vertex.co=Vector(tuple(vertex.co[a]*scale[a] for a in range(3)))
        part.obj.location=Vector(tuple(part.obj.location[a]*scale[a] for a in range(3)))
        part.geometry=([tuple(v[a]*scale[a] for a in range(3)) for v in part.geometry[0]],part.geometry[1])


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
    scale_item(asset.items["chicken_egg"], (.40,.40,.40))
    scale_item(asset.items["closed_stew_can"], (.46,.46,.46))
    scale_item(asset.items["open_stew_can"], (.46,.46,.46))
    scale_item(asset.items["instant_noodles"], (.62,.72,.62))
    scale_item(asset.items["day_old_loaf"], (.85,.85,.85))
    build_beer_can(asset)
    build_wine_bottle(asset)
    return asset


def item_geometry(item: Item) -> kit.Geometry:
    return merge(part.geometry for part in item.parts)


def validate(asset: AssetBuild) -> dict:
    problems: list[str] = []
    if asset.root.name != ROOT_NAME:
        problems.append(f"pack root must be '{ROOT_NAME}'")
    if set(asset.items) != set(ITEM_IDS):
        problems.append("pack item ids differ from the eight-item contract")
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
        if item_id == "vodka_bottle":
            if len([p for p in item.parts if p.role == "glass"]) != 8:
                problems.append("vodka requires eight closed authored glass cells")
            for name, y in (("VodkaGrip", VODKA_GRIP_Y * VODKA_SOURCE_SCALE), ("VodkaMouth", .440 * VODKA_SOURCE_SCALE)):
                anchor = bpy.data.objects.get(name)
                if anchor is None or anchor.parent != root or abs(anchor.location.z - y) > 1e-6:
                    problems.append(f"vodka lost measured {name}")
        contact_contracts = {
            "beer_can": (("BeerCanGrip", (0., .080, 0.)), ("BeerCanMouth", (0., .149, -.012))),
            "wine_bottle": (("WineBottleGrip", (0., .232, 0.)), ("WineBottleMouth", (0., .294, 0.))),
        }
        for name, unity_point in contact_contracts.get(item_id, ()):
            anchor = bpy.data.objects.get(name)
            expected = Vector((unity_point[0], unity_point[2], unity_point[1]))
            if anchor is None or anchor.parent != root or (anchor.location - expected).length > 1e-6:
                problems.append(f"{item_id} lost its exact named metre contact {name}")
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
        if item_id == "vodka_bottle" and unity_high[1] > 0.3001:
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
    if len(all_parts) > 64:
        problems.append(f"pack fragments into {len(all_parts)} meshes")
    if triangles > 12000:
        problems.append(
            f"pack costs {triangles} triangles against its 12000 cap"
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
        "drink_anchors": [{"name": name, "source_position":
            [stable(value) for value in bpy.data.objects[name].location]}
            for name in ("VodkaGrip", "VodkaMouth", "BeerCanGrip", "BeerCanMouth", "WineBottleGrip", "WineBottleMouth")],
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
        "vodka_contract": {"source_scale": VODKA_SOURCE_SCALE, "radial_scale": VODKA_RADIAL_SCALE,
            "grip_m": [0., VODKA_GRIP_Y * VODKA_SOURCE_SCALE, 0.],
            "neck_start_m": VODKA_NECK_START_Y * VODKA_SOURCE_SCALE,
            "neck_radius_m": VODKA_NECK_RADIUS * VODKA_SOURCE_SCALE * VODKA_RADIAL_SCALE,
            "mouth_m": [0., .440 * VODKA_SOURCE_SCALE, 0.],
            "cap_m": [0., .445 * VODKA_SOURCE_SCALE, 0.], "closed_glass_cells": 8,
            "inner_profile_m": [[y * VODKA_SOURCE_SCALE, radius * VODKA_SOURCE_SCALE * VODKA_RADIAL_SCALE]
                for y, radius in VODKA_INNER_PROFILE]},
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
            "maximum_renderers": 64,
            "maximum_triangles": 12000,
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
    positions = tuple((i - 3.5) * .48 for i in range(8))
    angles = (-12.0, 9.0, -8.0, 11.0, -9.0, 10.0, -12.0, 8.0)
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


def write_product_surface():
    # Shared neutral grain; per-role response and tint keep paper, metal and food distinct.
    # Periodic integer hashing keeps it deterministic and tileable without image dependencies.
    size=128
    pixels=[]
    for y in range(size):
        for x in range(size):
            noise=((x*73856093 ^ y*19349663 ^ (x*y)*83492791)&255)/255.
            fibre=.5+.5*math.sin(math.tau*y/16.+.3*math.sin(math.tau*x/32.))
            value=.83+.12*noise+.05*fibre
            pixels.extend((value,value,value,1.))
    image=bpy.data.images.new("ProductSurface",width=size,height=size,alpha=True)
    image.pixels.foreach_set(pixels)
    path=ROOT/"Assets/Resources/Supermarket/Products/ProductSurface.png"
    path.parent.mkdir(parents=True,exist_ok=True)
    image.filepath_raw=str(path); image.file_format="PNG"; image.save()
    bpy.data.images.remove(image)


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
    write_product_surface()
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
