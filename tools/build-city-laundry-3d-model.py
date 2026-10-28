#!/usr/bin/env python3
"""Build one fixed-metre City laundry and its finite movable props.

The L-shaped shell leaves the east service court under a real canopy. Shared
interior_kit walls own the same physical openings as the collision donors.
All coordinates below are Unity metres: +Y up, public frontage +Z, floor Y=0.
No animation, rig, Light, camera, texture or material asset is exported.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit

spec = importlib.util.spec_from_file_location("laundry_port_kit", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
port.SURFACE_TILES["Glass"] = 2.0

DESIGN_ID = "city_household_laundry_v1"
VERSION = "1.0.0"
MODEL_DIR = ROOT / "Assets/Resources/City/Laundry"
SOURCE_DIR = ROOT / "ArtSource/City/Laundry"
SURFACES = ("Concrete", "Plaster", "Steel", "SteelDark", "SteelLight", "Timber", "Fabric", "Rubber", "Roof", "Plain", "Glass", "Sign")
BOUND_MIN = (-4.3, -.15, -3.3)
BOUND_MAX = (4.3, 4.2, 3.3)
MAX_TRIANGLES = 18000
MAX_RENDERERS = 30
SOLID_FOOTPRINTS = (
    ("front-wall-and-closed-door", (-4, 2.88, 1.5, 3.12)),
    ("rear-wall", (-4, -3.12, 4, -2.88)),
    ("west-wall", (-4.12, -3, -3.88, 3)),
    ("side-wall-before-opening", (1.38, 0, 1.62, .3)),
    ("side-wall-after-opening", (1.38, 1.9, 1.62, 3)),
    ("service-east-wall", (3.88, -3, 4.12, 0)),
    ("service-front-wall", (1.5, -.12, 4, .12)),
    ("sorting-table", (-3.5, -.4, -1.8, .8)),
    ("shelf", (-3.81, -2.275, -3.03, -.625)),
    ("machine-a", (-1.36, -2.67, -.24, -1.57)),
    ("machine-b", (-.06, -2.67, 1.06, -1.57)),
    ("trolley", (2.54, .60, 3.46, 1.475)),
    ("canopy-foot-rear", (3.93, .06, 4.17, .30)),
    ("canopy-foot-front", (3.93, 2.78, 4.17, 3.02)),
)

ANCHORS = {
    "ANCHOR_MainDoor": (-2.8, 0, 3),
    "ANCHOR_ExteriorApproach": (-2.8, 0, 4.3),
    "ANCHOR_ServiceDoor": (1.5, 0, 1.1),
    "ANCHOR_Worker": (-2.65, 0, 1.20),
    "ANCHOR_TableLeft": (-2.95, .97, .38),
    "ANCHOR_TableRight": (-2.35, .97, .38),
    "ANCHOR_TableSurface": (-2.65, .96, .12),
    "ANCHOR_ClothRest": (-2.65, 1.34, .50),
    "ANCHOR_BasketRest": (-2.65, 1.15, .50),
    "ANCHOR_BasketGripLeft": (-2.98, 1.35, .50),
    "ANCHOR_BasketGripRight": (-2.32, 1.35, .50),
    "ANCHOR_CarryWaypointA": (.7, 0, 1.1),
    "ANCHOR_CarryWaypointB": (2.25, 0, 1.1),
    "ANCHOR_CanopyWorker": (2.25, 0, 1.1),
    "ANCHOR_CanopyHandoff": (2.9, 1.02, 1.1),
    "ANCHOR_CanopyBasketGripLeft": (2.9, 1.22, 1.43),
    "ANCHOR_CanopyBasketGripRight": (2.9, 1.22, .77),
    "ANCHOR_DrumA": (-.8, .78, -1.56),
    "ANCHOR_DrumB": (.5, .78, -1.56),
    "ANCHOR_WorkLight": (-2.5, 3.215, .25),
    "ANCHOR_MachinesLight": (-.15, 3.215, -1.3),
    "ANCHOR_CourtLight": (2.6, 2.675, 1.1),
    "ANCHOR_EntranceLight": (-2.65, 3.365, 3.26),
    "ANCHOR_Ventilation": (2.7, 3.65, -1.65),
    "ANCHOR_MachineAudio": (-.15, .78, -2.0),
}


def geometry(role=None):
    result = port.Geometry()
    result.role = role
    return result


def box(g, center, size, color=port.METAL, bevel=.012):
    port.chamfer(g, center, size, color, bevel)


def emit(g, name, root, material):
    if g.faces:
        return port.obj(g, name, root, material)
    return None


def add_kit(g, shape, color):
    port.kit_add(g, shape, color)


def walls():
    """Main room and the attached rear service block, with real door/window holes."""
    pieces = [
        kit.translated(kit.wall_run(5.5, 3.45, .24,
            (kit.Opening(-1.55, 1.2, 2.45), kit.Opening(1.0, 2.6, 2.65, .85))), (-1.25, 3, 0)),
        kit.translated(kit.wall_run(8, 3.45, .24), (0, -3, 0)),
        kit.translated(kit.rotated_z(kit.wall_run(6, 3.45, .24,
            (kit.Opening(0, 2.2, 2.65, 1.0),)), 90), (-4, 0, 0)),
        kit.translated(kit.rotated_z(kit.wall_run(3, 3.45, .24,
            (kit.Opening(-.4, 1.6, 2.45),)), 90), (1.5, 1.5, 0)),
        kit.translated(kit.rotated_z(kit.wall_run(3, 3.45, .24), 90), (4, -1.5, 0)),
        kit.translated(kit.wall_run(2.5, 3.45, .24), (2.75, 0, 0)),
    ]
    return kit.merge_all(pieces)


def shell(root, material):
    wall = geometry("Plaster")
    add_kit(wall, walls(), port.CABIN)
    emit(wall, "LaundryShell", root, material)
    collision = geometry()
    add_kit(collision, walls(), port.CABIN)
    emit(collision, "COL_Walls", root, material)
    floor = geometry("Concrete")
    box(floor, (0, -.075, 0), (8.6, .15, 6.6), port.CONCRETE, .015)
    emit(floor, "LaundryFloor", root, material)
    collision = geometry()
    box(collision, (0, -.075, 0), (8.6, .15, 6.6), port.CONCRETE, .015)
    emit(collision, "COL_Floor", root, material)
    # The roof stops over the main room and service block. Its lower canopy
    # covers only the open L-notch, rather than creating a second closed room.
    roof = geometry("Roof")
    box(roof, (-1.25, 3.55, 0), (5.95, .20, 6.6), port.DARK, .035)
    box(roof, (2.75, 3.55, -1.5), (3.1, .20, 3.6), port.DARK, .035)
    for z in (-3.2, 3.2):
        box(roof, (-1.25, 3.46, z), (5.85, .14, .12), port.DARK, .015)
    box(roof, (2.75, 2.85, 1.5), (3.1, .15, 3.6), port.DARK, .025)
    emit(roof, "LaundryRoof", root, material)
    structure = geometry("SteelDark")
    coll = geometry()
    for target in (structure, coll):
        for z in (.18, 2.9):
            box(target, (4.05, 1.4, z), (.11, 2.8, .11), port.METAL)
            box(target, (4.05, .035, z), (.24, .07, .24), port.METAL)
            target.rod((4.05, 2.25, z), (3.5, 2.77, z), .035, port.METAL)
        box(target, (2.77, 2.75, 2.9), (2.7, .12, .12), port.METAL)
    emit(structure, "LaundryCanopyStructure", root, material)
    box(coll, (-1.25, 3.55, 0), (5.95, .20, 6.6), port.DARK, .035)
    box(coll, (2.75, 3.55, -1.5), (3.1, .20, 3.6), port.DARK, .035)
    box(coll, (2.75, 2.85, 1.5), (3.1, .15, 3.6), port.DARK, .025)
    emit(coll, "COL_Canopy", root, material)
    frames = geometry("Steel")
    # Front window and west window have actual open apertures; glazing is
    # separate so Unity can reuse its existing transparent window surface.
    for x in (-1.57, 1.07):
        box(frames, (x, 1.75, 3.03), (.055, 1.84, .10), port.PAINT)
    for y in (.85, 2.65):
        box(frames, (-.25, y, 3.03), (2.7, .065, .10), port.PAINT)
    box(frames, (-.25, 1.75, 3.03), (.055, 1.84, .10), port.PAINT)
    for z in (-1.12, 1.12):
        box(frames, (-4.03, 1.825, z), (.10, 1.70, .055), port.PAINT)
    for y in (1, 2.65):
        box(frames, (-4.03, y, 0), (.10, .065, 2.3), port.PAINT)
    # Visible doorway jambs and head, no fake opaque door over either route.
    for x in (-3.42, -2.18):
        box(frames, (x, 1.225, 3.04), (.06, 2.45, .13), port.PAINT)
    box(frames, (-2.8, 2.46, 3.04), (1.3, .08, .13), port.PAINT)
    for z in (.28, 1.92):
        box(frames, (1.54, 1.225, z), (.13, 2.45, .06), port.PAINT)
    box(frames, (1.54, 2.46, 1.1), (.13, .08, 1.7), port.PAINT)
    emit(frames, "LaundryFrames", root, material)
    door = geometry("Steel")
    add_kit(door, kit.translated(kit.panelled_leaf(1.18, 2.43, .07), (-3.39, 3, .02)), port.PAINT)
    emit(door, "LaundryClosedFrontDoor", root, material)
    collision = geometry()
    box(collision, (-2.8, 1.235, 3), (1.18, 2.43, .07), port.METAL)
    emit(collision, "COL_Door", root, material)
    glass = geometry("Glass")
    box(glass, (-.25, 1.75, 3.015), (2.56, 1.76, .012), port.GLASS, .001)
    box(glass, (-4.015, 1.825, 0), (.012, 1.61, 2.16), port.GLASS, .001)
    emit(glass, "LaundryWindows", root, material)
    trim = geometry("Concrete")
    box(trim, (-.25, .83, 3.075), (2.8, .08, .22), port.EDGE)
    box(trim, (-4.075, .97, 0), (.22, .08, 2.4), port.EDGE)
    # A flush, floor-level service drain remains outside every walking dock.
    box(trim, (3.65, .008, -.25), (.30, .016, .75), port.METAL, .003)
    for z in (-.52, -.38, -.24, -.10, .04):
        box(trim, (3.65, .019, z), (.24, .008, .018), port.DARK, .001)
    emit(trim, "LaundrySillsAndDrain", root, material)


def machines(root, material):
    casing, fittings, glass = geometry("SteelLight"), geometry("SteelDark"), geometry("Glass")
    coll = geometry()
    for index, x in enumerate((-.8, .5)):
        for g in (casing,):
            box(g, (x, .10, -2.12), (1.12, .20, 1.10), port.ICE)
            box(g, (x, 1.24, -2.12), (1.12, .17, 1.10), port.ICE)
            for dx in (-.51, .51):
                box(g, (x+dx, .67, -2.12), (.10, 1.05, 1.10), port.ICE)
            box(g, (x, .68, -2.63), (1.1, 1.18, .08), port.ICE)
        box(coll, (x, .66, -2.12), (1.12, 1.32, 1.10), port.ICE)
        port.ring(fittings, (x, .78, -1.55), .375, .037, port.METAL, (0, 0, 1), 20)
        # Door glass is fixed; the open drum and visible cloth rotate behind it.
        glass.rod((x, .78, -1.52), (x, .78, -1.508), .335, port.GLASS, 20)
        for dx in (-.33, .31):
            fittings.rod((x+dx, 1.20, -1.55), (x+dx, 1.20, -1.50), .055, port.DARK, 10)
        pivot = port.empty("MOVE_Drum"+"AB"[index], root, ANCHORS["ANCHOR_Drum"+"AB"[index]])
        drum = geometry("SteelDark")
        drum.rod((0, 0, -.55), (0, 0, -.51), .32, port.METAL, 20)
        for n in range(16):
            a = n*math.tau/16
            xx, yy = math.cos(a)*.31, math.sin(a)*.31
            drum.rod((xx, yy, -.52), (xx, yy, -.05), .015, port.METAL, 5)
        port.ring(drum, (0, 0, -.04), .315, .018, port.METAL, (0, 0, 1), 20)
        emit(drum, "Drum"+"AB"[index], pivot, material)
        cloth = geometry("Fabric")
        for n in range(3):
            angle = n*math.tau/3+.3
            box(cloth, (.12*math.cos(angle), .12*math.sin(angle), -.18-n*.07),
                (.22, .13, .18), port.ROPE, .025)
        emit(cloth, "DrumCloth"+"AB"[index], pivot, material)
    emit(casing, "LaundryMachines", root, material)
    emit(fittings, "LaundryMachineFittings", root, material)
    emit(glass, "LaundryMachineGlass", root, material)
    emit(coll, "COL_Machines", root, material)


def basket_geometry():
    basket = geometry("Steel")
    box(basket, (0, -.165, 0), (.62, .03, .42), port.PAINT, .008)
    for y in (-.10, .02, .14):
        for x in (-.295, .295):
            box(basket, (x, y, 0), (.025, .035, .42), port.PAINT, .006)
        for z in (-.195, .195):
            box(basket, (0, y, z), (.62, .035, .025), port.PAINT, .006)
    for x in (-.295, 0, .295):
        for z in (-.195, .195):
            box(basket, (x, -.01, z), (.025, .32, .025), port.PAINT, .006)
    for x in (-.33, .33):
        for z in (-.09, .09):
            basket.rod((x, .13, z), (x, .20, z), .018, port.PAINT)
        basket.rod((x, .20, -.09), (x, .20, .09), .018, port.PAINT)
    return basket


def work_fittings(root, material):
    wood, steel, coll, fabric = geometry("Timber"), geometry("SteelDark"), geometry(), geometry("Fabric")
    box(wood, (-2.65, .92, .20), (1.7, .08, 1.20), port.WOOD, .018)
    for x in (-3.36, -1.94):
        for z in (-.27, .67):
            box(steel, (x, .44, z), (.065, .88, .065), port.METAL)
    box(coll, (-2.65, .48, .20), (1.7, .96, 1.20), port.METAL)
    # The storage sits behind the table, so it never becomes a lane obstacle.
    for y in (.35, 1.2, 2.05):
        box(wood, (-3.42, y, -1.45), (.75, .055, 1.6), port.WOOD)
    for x in (-3.74, -3.1):
        for z in (-2.16, -.74):
            box(steel, (x, 1.05, z), (.055, 2.1, .055), port.METAL)
    box(coll, (-3.42, 1.07, -1.45), (.78, 2.14, 1.65), port.METAL)
    for y, z in ((.42, -1.8), (.43, -.98), (1.28, -1.85), (2.13, -1.1)):
        for i in range(2):
            box(fabric, (-3.42, y+i*.055, z), (.55, .05, .45), port.ROPE, .015)
    # The outdoor trolley is a stable receipt surface, not a second inventory.
    box(wood, (3, .78, 1.1), (.92, .10, .75), port.WOOD)
    box(coll, (3, .415, 1.1), (.92, .83, .75), port.WOOD)
    for x in (2.64, 3.36):
        for z in (.82, 1.38):
            steel.rod((x, .25, z), (x, .78, z), .025, port.METAL)
    for x in (2.63, 3.37):
        steel.rod((x, .43, .76), (x, .97, .62), .025, port.METAL)
    steel.rod((2.63, .97, .62), (3.37, .97, .62), .025, port.METAL)
    emit(wood, "LaundryFurniture", root, material)
    emit(steel, "LaundryFurnitureIron", root, material)
    emit(fabric, "LaundryStoredCloth", root, material)
    emit(coll, "COL_Furniture", root, material)
    pivot = port.empty("MOVE_CarryBasket", root, ANCHORS["ANCHOR_BasketRest"])
    emit(basket_geometry(), "CarryBasket", pivot, material)
    port.empty("ANCHOR_BasketLeft", pivot, (-.33, .20, 0))
    port.empty("ANCHOR_BasketRight", pivot, (.33, .20, 0))
    pivot = port.empty("MOVE_ClothA", root, ANCHORS["ANCHOR_ClothRest"])
    cloth = geometry("Fabric")
    box(cloth, (0, 0, 0), (.20, .012, .40), port.ROPE, .003)
    emit(cloth, "WorkCloth", pivot, material)
    for label, side in (("Left", -1), ("Right", 1)):
        hinge = port.empty("MOVE_Cloth"+label, pivot, (side*.10, 0, 0))
        leaf = geometry("Fabric")
        box(leaf, (side*.10, 0, 0), (.20, .012, .40), port.ROPE, .003)
        emit(leaf, "WorkCloth"+label, hinge, material)


def service_details(root, material):
    steel = geometry("SteelDark")
    # Ventilator, weather cap, continuous machine inlet and drain connections.
    steel.rod((2.7, 3.45, -1.65), (2.7, 4.05, -1.65), .19, port.METAL, 12)
    steel.rod((2.7, 4.14, -1.65), (2.7, 4.20, -1.65), .32, port.METAL, 12)
    for dx in (-.16, .16):
        steel.rod((2.7+dx, 4, -1.65), (2.7+dx, 4.16, -1.65), .014, port.METAL)
    for x in (-.8, .5):
        steel.rod((x, .40, -2.7), (x, .40, -2.82), .04, port.METAL)
        steel.rod((x, .40, -2.82), (x, .12, -2.82), .04, port.METAL)
    steel.rod((-.8, .12, -2.82), (1.0, .12, -2.82), .045, port.METAL)
    steel.rod((1.0, .12, -2.82), (1.0, .12, -3.12), .045, port.METAL)
    box(steel, (-2.5, 3.28, .25), (.66, .08, .20), port.METAL)
    box(steel, (-.15, 3.28, -1.3), (.80, .08, .20), port.METAL)
    box(steel, (2.6, 2.73, 1.1), (.55, .08, .20), port.METAL)
    box(steel, (-2.65, 3.40, 3.26), (.55, .06, .07), port.METAL)
    # A modest enamel plate above the original front door, within the same
    # facade envelope. Its graphic owns unique full-face UVs, never tile UVs.
    box(steel, (-2.65, 3.04, 3.155), (2.4, .76, .055), port.METAL, .015)
    for z in (-2.8, 2.8):
        box(steel, (1.42, 2.75, z), (.10, .10, .25), port.METAL)
    emit(steel, "LaundryServicePipesAndLightBody", root, material)
    for suffix, position, size in (
            ("Work", (-2.5, 3.215, .25), (.56, .025, .14)),
            ("Machines", (-.15, 3.215, -1.3), (.70, .025, .14)),
            ("Court", (2.6, 2.675, 1.1), (.45, .025, .14)),
            ("Entrance", (-2.65, 3.365, 3.26), (.44, .018, .06))):
        lens = geometry()
        box(lens, position, size, port.LAMP, .004)
        emit(lens, "LaundryLampLens"+suffix, root, material)
    artwork = geometry("Sign")
    artwork.add([(-3.76, 2.67, 3.19), (-1.54, 2.67, 3.19),
                 (-1.54, 3.41, 3.19), (-3.76, 3.41, 3.19)],
                [(0, 1, 2, 3)], (1, 1, 1, 1), [(1, 0), (0, 0), (0, 1), (1, 1)], solid=False)
    artwork.object("LaundrySignArtwork__Sign", root, material)
    rubber = geometry("Rubber")
    for x in (2.64, 3.36):
        for z in (.82, 1.38):
            rubber.rod((x-.04, .17, z), (x+.04, .17, z), .17, port.DARK, 10)
    for x in (-.8, .5):
        port.ring(rubber, (x, .78, -1.538), .337, .012, port.DARK, (0, 0, 1), 20)
    emit(rubber, "LaundryRubberFittings", root, material)


def build():
    material = port.base.material("LaundryVertexPaint")
    root = port.empty("StaticLaundry")
    shell(root, material)
    machines(root, material)
    work_fittings(root, material)
    service_details(root, material)
    for name, point in ANCHORS.items():
        port.empty(name, root, point)
    bpy.context.view_layer.update()
    return root


def manifest(root):
    measured = port.describe(root)
    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    renderers = [o for o in meshes if not o.name.startswith("COL_")]
    colliders = [o for o in meshes if o.name.startswith("COL_")]
    if measured["triangles"] > MAX_TRIANGLES or len(renderers) > MAX_RENDERERS:
        raise RuntimeError("Laundry geometry exceeds its bounded triangle/renderer budget")
    for key, wanted in (("bounds_min", BOUND_MIN), ("bounds_max", BOUND_MAX)):
        if any(abs(actual-expected) > .001 for actual, expected in zip(measured[key], wanted)):
            raise RuntimeError(f"Laundry fixed envelope mismatch: {key}={measured[key]}, expected {wanted}")
    for o in renderers:
        role = o.name.rsplit("__", 1)[-1].split(".")[0] if "__" in o.name else "Plain"
        if role not in SURFACES:
            raise RuntimeError("Unknown shared laundry surface: "+role)
        if not o.data.uv_layers or not o.data.color_attributes:
            raise RuntimeError("Laundry mesh lacks metre UVs or retained vertex paint")
    # A real window cannot silently be patched with an opaque wall by later edits.
    wall = next(o for o in colliders if o.name == "COL_Walls")
    for x, y, z in ((-2.8, 1.2, 3), (-.25, 1.7, 3), (1.5, 1.2, 1.1)):
        origin = Vector(port.source((x, y, z-.4)))
        direction = Vector(port.source((0, 0, 1)))
        if x == 1.5:
            origin = Vector(port.source((x-.4, y, z)))
            direction = Vector(port.source((1, 0, 0)))
        if wall.ray_cast(origin, direction, distance=.8)[0]:
            raise RuntimeError("Laundry door/window aperture was sealed")
    closed_door = next(o for o in colliders if o.name == "COL_Door")
    if not closed_door.ray_cast(Vector(port.source((-2.8, 1.2, 2.7))),
                                Vector(port.source((0, 0, 1))), distance=.6)[0]:
        raise RuntimeError("Laundry front leaf must physically close its authored opening")
    return {
        "design_id": DESIGN_ID, "version": VERSION,
        "generator": Path(__file__).name,
        "sign_resource": "City/Laundry/LaundrySign",
        "sign_source_sha256": hashlib.sha256((MODEL_DIR / "LaundrySign.png").read_bytes()).hexdigest(),
        "sign_text": "ПРАЧЕЧНАЯ", "sign_uv": "unique full face 0..1",
        "coordinate_system": "Unity metres +Y up/+Z frontage; floor Y=0; swap YZ/reverse winding for Blender",
        "geometry_signature_includes": ["vertices", "faces", "colors", "uv0", "transforms"],
        "bounds_min": measured["bounds_min"], "bounds_max": measured["bounds_max"],
        "body_size": [8, 3.65, 6], "main_room_bounds": [-4, -3, 1.5, 3],
        "service_court_bounds": [1.5, 0, 4, 3],
        "floor_walk_bounds": [-4.3, -3.3, 4.3, 3.3],
        "solid_footprints": [{"id": name, "rect_xz": list(rect)} for name, rect in SOLID_FOOTPRINTS],
        "front_door_closed": True, "entry_side": "east service court",
        "public_door_width": 1.2, "service_door_width": 1.6,
        "floor_height": 0, "table_height": .96,
        "triangles": measured["triangles"], "maximum_triangles": MAX_TRIANGLES,
        "renderer_count": len(renderers), "maximum_renderers": MAX_RENDERERS,
        "geometry_signature": measured["geometry_signature"],
        "semantic_uv_tiles_metres": {r: port.SURFACE_TILES.get(r, 0) for r in SURFACES},
        "anchors": measured["anchors"],
        "moving_parts": ["MOVE_DrumA", "MOVE_DrumB", "MOVE_CarryBasket", "MOVE_ClothA",
                         "MOVE_ClothLeft", "MOVE_ClothRight"],
        "collider_parts": [o.name for o in colliders],
        "material_roles": sorted({o.name.rsplit("__", 1)[-1] if "__" in o.name else "Plain" for o in renderers}),
        "assets": ["StaticLaundry.fbx"],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    port.base.reset()
    root = build()
    result = manifest(root)
    if args.validate_only:
        saved = json.loads((args.model_dir / "CityLaundry3D.json").read_text())
        if saved != result:
            raise RuntimeError("Laundry authored contract differs from the committed manifest")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        port.base.export(root, args.model_dir / "StaticLaundry.fbx")
        (args.model_dir / "CityLaundry3D.json").write_text(json.dumps(result, indent=2)+"\n")
        port.source_surface_materials([root])
        sign_mat = bpy.data.materials.new("LaundrySignReview")
        sign_mat.use_nodes = True
        sign_shader = sign_mat.node_tree.nodes.get("Principled BSDF")
        sign_tex = sign_mat.node_tree.nodes.new("ShaderNodeTexImage")
        sign_tex.image = bpy.data.images.load(str(MODEL_DIR / "LaundrySign.png"), check_existing=True)
        sign_tex.image.pack()
        sign_mat.node_tree.links.new(sign_tex.outputs["Color"], sign_shader.inputs["Base Color"])
        sign_shader.inputs["Roughness"].default_value = .72
        next(o for o in root.children_recursive if o.name == "LaundrySignArtwork__Sign").data.materials[0] = sign_mat
        # Source glass is merely a review material; Unity binds an existing
        # shared City material by the __Glass role and creates none per instance.
        for mesh in [o for o in root.children_recursive if o.type == "MESH" and o.name.endswith("__Glass")]:
            glass = bpy.data.materials.get("LaundryReviewGlass")
            if glass is None:
                glass = bpy.data.materials.new("LaundryReviewGlass")
                glass.use_nodes = True
                shader = glass.node_tree.nodes.get("Principled BSDF")
                shader.inputs["Base Color"].default_value = (.35, .45, .43, 1)
                shader.inputs["Roughness"].default_value = .18
                shader.inputs["Transmission Weight"].default_value = 1
                shader.inputs["IOR"].default_value = 1.45
            mesh.data.materials[0] = glass
        for mesh in [o for o in root.children_recursive if o.type == "MESH" and o.name.startswith("COL_")]:
            mesh.hide_render = True
            mesh.hide_set(True)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityLaundry3D.blend"), check_existing=False)
    for mesh in bpy.data.objects:
        mesh.hide_set(False)
    port.base.reset()
    repeated = manifest(build())
    if repeated != result:
        raise RuntimeError("Laundry deterministic rebuild mismatch")
    print("CITY LAUNDRY ART CONTRACT OK: fixed metres, real openings/collision, finite props, shared surfaces and deterministic geometry", flush=True)
    print(json.dumps({k: result[k] for k in ("bounds_min", "bounds_max", "triangles", "renderer_count", "geometry_signature")}))


if __name__ == "__main__":
    main()
