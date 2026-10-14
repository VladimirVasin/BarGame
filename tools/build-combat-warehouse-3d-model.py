#!/usr/bin/env python3
"""Deterministic passive warehouse kit for the isolated CombatTest scene.

Fixed Unity metres; runtime owns seed placement, collision components and light.
The shared interior kit supplies chamfered solids and real window/door reveals.
Every constituent solid is checked before and after the Unity/Blender axis swap;
published FBX bounds and anchors are measured after an independent import.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import bar_parts as bp
import interior_kit as interior
import warehouse_surface_authoring as appearance

# Reuse the established passive metric exporter, not a second export convention.
spec = importlib.util.spec_from_file_location("warehouse_passive_export", ROOT / "tools/build-city-fair-3d-model.py")
passive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(passive)
VERSION = "2.1.0"
MODEL_DIR = ROOT / "Assets/Resources/CombatWarehouse"
SOURCE_DIR = ROOT / "ArtSource/CombatWarehouse"
SURFACES = {
    "Concrete": (.38, .39, .34), "ConcreteDark": (.20, .23, .21),
    "Steel": (.20, .24, .22), "WornSteel": (.36, .39, .32),
    "Rust": (.39, .23, .13), "Timber": (.31, .25, .18),
    "Cardboard": (.39, .32, .23), "Glass": (.24, .30, .27),
    "Lamp": (.67, .57, .36), "Rubber": (.08, .10, .085),
    "WearDecal": (.21, .17, .11),
}
passive.SURFACES = SURFACES
passive.VERSION = VERSION
Item = passive.Item


def duplicate_collision(item, geometry, name):
    item.add("ConcreteDark", geometry, "Collision_" + name)


def box(item, role, p, size, group="Body", chamfer=.012, collision=False):
    geometry = bp.u_box(p, size, min(chamfer, min(size) * .23))
    item.add(role, geometry, group)
    if collision:
        duplicate_collision(item, geometry, group)


def rod(item, role, a, b, radius, group="Body", collision=False, sides=10):
    geometry = passive.rod(a, b, radius, sides)
    item.add(role, geometry, group)
    if collision:
        duplicate_collision(item, geometry, group)


def wall(item, length, position, turn, openings, group, height=10.2, thickness=.30):
    # interior_kit is Z-up; the same reflection helper converts it to Unity.
    geometry = bp.to_source(interior.wall_run(length, height, thickness, openings, .012))
    geometry = interior.translated(bp.u_rotated(geometry, (0, turn, 0)), position)
    item.add("Concrete", geometry, group)
    duplicate_collision(item, geometry, group)


def flange(item, center, axis=(1, 0, 0), radius=.15, group="Body"):
    a = tuple(p - v * .025 for p, v in zip(center, axis))
    b = tuple(p + v * .025 for p, v in zip(center, axis))
    rod(item, "WornSteel", a, b, radius, group, sides=12)


def beam(item, a, b, width, depth, group="Structure", collision=False):
    # A rectangular steel beam follows the incline without stairs made of cubes.
    dx, dy, dz = (b[i] - a[i] for i in range(3))
    length = math.sqrt(dx * dx + dy * dy + dz * dz)
    geometry = bp.u_box((0, 0, 0), (width, depth, length), .008)
    pitch = -math.degrees(math.atan2(dy, math.hypot(dx, dz)))
    yaw = math.degrees(math.atan2(dx, dz))
    geometry = interior.translated(bp.u_rotated(geometry, (pitch, yaw, 0)),
                                   tuple((a[i] + b[i]) * .5 for i in range(3)))
    item.add("Steel", geometry, group)
    if collision:
        duplicate_collision(item, geometry, group)


def wear_decal(item, p, width, height, normal, cell):
    """Explicit one-sided colour overlay, not a closed solid or collision mesh."""
    x, y, z = p
    if normal[1]:
        vertices = [(x - width / 2, y, z - height / 2), (x + width / 2, y, z - height / 2),
                    (x + width / 2, y, z + height / 2), (x - width / 2, y, z + height / 2)]
        face = (0, 3, 2, 1) if normal[1] > 0 else (0, 1, 2, 3)
    elif normal[2]:
        vertices = [(x - width / 2, y - height / 2, z), (x + width / 2, y - height / 2, z),
                    (x + width / 2, y + height / 2, z), (x - width / 2, y + height / 2, z)]
        face = (0, 1, 2, 3) if normal[2] > 0 else (0, 3, 2, 1)
    else:
        vertices = [(x, y - height / 2, z - width / 2), (x, y - height / 2, z + width / 2),
                    (x, y + height / 2, z + width / 2), (x, y + height / 2, z - width / 2)]
        face = (0, 3, 2, 1) if normal[0] > 0 else (0, 1, 2, 3)
    item.parts.setdefault(("WearDecals", "WearDecal"), []).append((vertices, [face]))
    if not hasattr(item, "wear_cells"):
        item.wear_cells = []
    item.wear_cells.append(cell)


def make_shell():
    item = Item("Shell")
    box(item, "Concrete", (0, -.12, 0), (30.6, .24, 24.6), "Floor", collision=True)
    wall(item, 30.6, (0, 0, 12.15), 0, [interior.Opening(0, 4., 3.)], "GoalWall")
    wall(item, 30.6, (0, 0, -12.15), 0, [], "LoadingWall")
    for sign in (-1, 1):
        wall(item, 24., (sign * 15.15, 0, 0), 90, [], "SideWall" + str(sign))
        for z in (-9, -3, 3, 9):
            # Flanged steel stanchions carry the ceiling joists; ends bear on shoes.
            box(item, "Steel", (sign * 14.88, 5.0, z), (.18, 10., .22), "Structure")
            box(item, "Rust", (sign * 14.88, .035, z), (.30, .07, .36), "Structure")
            box(item, "WornSteel", (sign * 14.87, .73, z), (.20, .12, .25), "Structure")
        box(item, "ConcreteDark", (sign * 14.985, .34, 0), (.03, .68, 24), "WallProtection")
    # The fan opens into a real roof recess, never a painted circle on a slab.
    for sign in (-1, 1):
        box(item, "ConcreteDark", (sign * 9.65, 10.29, 0), (11.30, .18, 24.6), "Roof", collision=True)
        box(item, "ConcreteDark", (0, 10.29, sign * 8.15), (8., .18, 8.30), "Roof", collision=True)
    for z in (-9, -5.5, 5.5, 9):
        box(item, "Steel", (0, 9.96, z), (29.8, .22, .10), "Joists")
        box(item, "Rust", (0, 10.085, z), (29.8, .055, .34), "Joists")
        box(item, "Steel", (0, 9.835, z), (29.8, .055, .34), "Joists")
    for x in (-12, -6, 6, 12):
        box(item, "Steel", (x, 10.12, 0), (.10, .06, 24), "CeilingRuns")
    # One maintenance patch, rubbed lower guards and sparse floor joints show use.
    for x in range(-12, 15, 3):
        box(item, "ConcreteDark", (x, .002, 0), (.013, .003, 23.7), "FloorJoints", .001)
    for z in range(-9, 12, 3):
        box(item, "ConcreteDark", (0, .002, z), (29.7, .003, .013), "FloorJoints", .001)
    for x, z, w, d in ((-8.7, -5.2, 1.8, .45), (7.6, 4.2, .8, 1.4), (1.3, -6.7, .7, .4)):
        box(item, "ConcreteDark", (x, .004, z), (w, .004, d), "FloorWear", .001)
    for x in (-6, 6):
        box(item, "Steel", (x, .007, -7.7), (.8, .008, .28), "FlushDrain", .001)
        for index in range(7):
            box(item, "ConcreteDark", (x - .30 + index * .10, .012, -7.7), (.028, .003, .22), "DrainSlots", .0005)
    # Stains have a physical source: wall steel, lift/drum/pusher or dragged load.
    # Eight sparse quads remain one mesh/material and inside the shell bounds.
    for p, width, height, normal, cell in (
            ((-9., 7.0, 11.989), 1.15, 2.3, (0, 0, -1), 0),
            ((9., 5.8, -11.989), 1.20, 2.5, (0, 0, 1), 0),
            ((-14.989, 4.1, -3.), 1.2, 2.2, (1, 0, 0), 0),
            ((14.989, 2.1, 3.4), 1.3, 1.8, (-1, 0, 0), 3),
            ((-10.5, .014, 7.5), 2.5, 2.0, (0, 1, 0), 1),
            ((-12., .014, .5), 1.8, 1.1, (0, 1, 0), 1),
            ((4.6, .014, -4.1), 3.2, .70, (0, 1, 0), 3),
            ((7.8, .014, 7.7), 2.4, 1.5, (0, 1, 0), 2)):
        wear_decal(item, p, width, height, normal, cell)
    item.anchor("FloorCenter", (0, 0, 0))
    item.anchor("CeilingCenter", (0, 10.2, 0))
    item.anchor("GoalDoor", (0, 0, 11.9))
    item.anchor("FanMount", (0, 10.2, 0))
    return item


def make_platform():
    item = Item("Platform")
    box(item, "Steel", (0, -.09, 0), (3, .18, 3), "Deck", collision=True)
    # Suspended decks leave lower corridors open; structural bays belong to Shell.
    for x in (-1.34, 0, 1.34):
        box(item, "Steel", (x, -.25, 0), (.14, .14, 2.84), "DeckBearers")
    for index in range(12):
        box(item, "WornSteel", (-1.38 + index * .25, .003, 0), (.014, .004, 2.88), "DeckSeams", .001)
    item.anchor("DeckCenter", (0, 0, 0))
    return item


def make_stair():
    item = Item("Stair")
    for index in range(20):
        top = (index + 1) * .16
        z = (index + .5) * .30
        box(item, "Steel", (0, top - .035, z), (2.4, .07, .30), "Treads", collision=True)
        box(item, "Rust", (0, top - .010, z - .14), (2.4, .035, .022), "TreadNosing", .004)
        for x in (-1.05, -.52, 0, .52, 1.05):
            box(item, "ConcreteDark", (x, top + .001, z), (.025, .002, .19), "TreadGrip", .0004)
    for x in (-1.12, 1.12):
        beam(item, (x, .035, .04), (x, 3.07, 5.97), .11, .22, "Stringers", collision=True)
        box(item, "Steel", (x, .02, .08), (.25, .04, .30), "FootShoes", collision=True)
    for x in (-1.245, 1.245):
        for index in (0, 4, 8, 12, 16, 19):
            z = (index + .5) * .30
            top = (index + 1) * .16
            rod(item, "Steel", (x, top, z), (x, top + .98, z), .035, "Guard", collision=True)
        for height in (.47, .98):
            rod(item, "Steel", (x, .16 + height, .15), (x, 3.2 + height, 5.85), .035, "Guard", collision=True)
    item.anchor("Foot", (0, 0, 0))
    item.anchor("Top", (0, 3.2, 6.))
    return item


def make_rail():
    item = Item("Rail")
    for x in (-.435, .435):
        box(item, "Steel", (x, .51, 0), (.065, 1.02, .065), "Posts", collision=True)
        box(item, "WornSteel", (x, .018, 0), (.13, .036, .13), "Shoes")
    for y in (.52, 1.015):
        rod(item, "Steel", (-.5, y, 0), (.5, y, 0), .035, "Bars", collision=True)
    box(item, "Steel", (0, .08, 0), (1, .13, .025), "ToeGuard", collision=True)
    return item


def cardboard(item, p, size, group="Load"):
    box(item, "Cardboard", p, size, group, .02)
    x, y, z = p
    w, h, d = size
    box(item, "Timber", (x, y + h * .5 + .001, z), (.035, .003, d - .015), "CartonSeams", .0005)
    for sign in (-1, 1):
        box(item, "ConcreteDark", (x, y + .08, z + sign * (d * .5 + .001)), (.20, .05, .003), "CartonHandholds", .0005)


def make_rack():
    item = Item("Rack")
    for x in (-1.405, 1.405):
        for z in (-.505, .505):
            box(item, "Steel", (x, 1.10, z), (.085, 2.20, .085), "Posts")
            box(item, "WornSteel", (x, .025, z), (.19, .05, .19), "Shoes")
    for y in (.18, 1.03, 1.93):
        box(item, "Timber", (0, y, 0), (2.94, .075, 1.13), "Shelves")
        for z in (-.56, .56):
            box(item, "Steel", (0, y - .05, z), (2.94, .12, .045), "ShelfLips")
    rod(item, "Steel", (-1.44, .15, -.55), (1.44, 2.12, -.55), .023, "BackBrace")
    rod(item, "Steel", (1.44, .15, -.55), (-1.44, 2.12, -.55), .023, "BackBrace")
    for x, y, z in ((-.95, .51, -.06), (.1, .51, .05), (.95, 1.33, -.02), (-.5, 1.33, .07)):
        cardboard(item, (x, y, z), (.75, .56, .85))
    duplicate_collision(item, bp.u_box((0, 1.1, 0), (3, 2.2, 1.2), .005), "Rack")
    return item


def make_cargo():
    item = Item("Cargo")
    for x in (-.83, 0, .83):
        box(item, "Timber", (x, .06, 0), (.14, .12, 1.4), "PalletSkids")
    for index in range(7):
        box(item, "Timber", (-.865 + index * (1.73 / 6), .145, 0), (.27, .07, 1.4), "PalletSlats")
    for x in (-.47, .47):
        for z in (-.34, .34):
            cardboard(item, (x, .705, z), (.91, 1.05, .65))
    for x in (-.62, .62):
        box(item, "Rubber", (x, 1.237, 0), (.035, .026, 1.30), "LoadStraps", .004)
        for z in (-.67, .67):
            box(item, "Rubber", (x, .71, z), (.035, 1.07, .015), "LoadStraps", .003)
    duplicate_collision(item, bp.u_box((0, .625, 0), (2, 1.25, 1.4), .01), "Cargo")
    return item


def make_pipe_straight():
    item = Item("PipeStraight")
    rod(item, "Steel", (-1.5, 0, 0), (1.5, 0, 0), .10)
    for x in (-1.47, 1.47):
        flange(item, (x, 0, 0))
    item.anchor("Start", (-1.5, 0, 0))
    item.anchor("End", (1.5, 0, 0))
    return item


def make_pipe_elbow():
    item = Item("PipeElbow")
    previous = None
    for index in range(13):
        angle = -math.pi * .5 + index * math.pi / 24
        p = (.4 * math.cos(angle), 0, .4 + .4 * math.sin(angle))
        if previous is not None:
            rod(item, "Steel", previous, p, .10)
        previous = p
    item.anchor("Start", (0, 0, 0))
    item.anchor("End", (.4, 0, .4))
    return item


def technical_end(item, p, group="Equipment"):
    x, y, z = p
    box(item, "Steel", (x, y, z), (.55, .70, .34), group)
    box(item, "WornSteel", (x, y, z + .18), (.46, .59, .035), group)
    for yy in (-.14, -.07, 0, .07, .14):
        box(item, "ConcreteDark", (x, y + yy, z + .203), (.28, .017, .015), "VentSlots", .003)
    rod(item, "Steel", (x, y + .35, z), (x, y + .60, z), .075, group)
    flange(item, (x, y + .45, z), (0, 1, 0), .115, group)
    item.add("Rust", interior.translated(bp.u_rotated(passive.ring([(-.017, .13), (.017, .13),
        (.017, .108), (-.017, .108)], 12), (90, 0, 0)), (x, y + .52, z + .15)), "ValveWheel")
    rod(item, "Steel", (x, y + .52, z), (x, y + .52, z + .16), .018, group)


def make_pipe_end():
    item = Item("PipeEnd")
    technical_end(item, (0, .35, .17))
    item.anchor("Connection", (0, .95, .17))
    return item


def make_pipes():
    item = Item("Pipes")
    for y, radius in ((4.90, .10), (4.54, .065)):
        rod(item, "Steel", (-4.8, y, .20), (4.8, y, .20), radius)
        for x in (-4.7, -.10, 4.7):
            flange(item, (x, y, .20), radius=radius + .035)
    rod(item, "Steel", (-4.8, 1.28, .20), (-4.8, 4.90, .20), .10)
    rod(item, "Steel", (4.8, .56, .20), (4.8, 4.54, .20), .065)
    technical_end(item, (-4.8, .68, .20))
    for x in (-4.8, -2.4, 0, 2.4, 4.8):
        box(item, "WornSteel", (x, 4.72, .04), (.10, .72, .065), "WallBrackets")
        for y in (4.54, 4.90):
            box(item, "Steel", (x, y, .13), (.09, .065, .20), "PipeSupports")
    for y in (1.6, 2.8, 4.1):
        box(item, "WornSteel", (-4.8, y, .055), (.24, .08, .08), "WallBrackets")
    box(item, "WornSteel", (4.8, .52, .20), (.27, .12, .27), "DrainConnection")
    item.anchor("Supply", (-4.8, 4.9, .20))
    return item


def make_lamp():
    item = Item("Lamp")
    for x in (-.45, .45):
        box(item, "Steel", (x, -.012, 0), (.15, .024, .16), "Mounts")
        rod(item, "Steel", (x, -.024, 0), (x, -.17, 0), .014, "Hangers")
    box(item, "Steel", (0, -.235, 0), (1.20, .13, .35), "Housing", .025)
    box(item, "Lamp", (0, -.326, 0), (1.04, .043, .25), "Lens", .01)
    for z in (-.16, .16):
        box(item, "WornSteel", (0, -.31, z), (1.18, .065, .035), "GuardRim", .005)
    for x in (-.40, 0, .40):
        rod(item, "Steel", (x, -.347, -.15), (x, -.347, .15), .006, "LensGuard", sides=6)
    item.anchor("Light", (0, -.37, 0))
    return item


def make_room_shell():
    item = Item("RoomShell")
    # Six-metre corner rooms connect through a true opening in their +X wall.
    # The shared ring deck supplies the floor; a second slab would z-fight.
    for turn, p, group, openings in (
            (0, (0, 0, -2.92), "RoomFront", []),
            (0, (0, 0, 2.92), "RoomBack", []),
            (90, (-2.92, 0, 0), "RoomLeft", []),
            (90, (2.92, 0, 0), "RoomEntry", [interior.Opening(0, 2.4, 2.55)])):
        wall(item, 6., p, turn, openings, group, height=3., thickness=.16)
    box(item, "ConcreteDark", (0, 3.02, 0), (6., .12, 6.), "RoomRoof", collision=True)
    for z in (-1.25, 1.25):
        box(item, "Steel", (2.81, 1.3, z), (.08, 2.6, .10), "EntryFrame")
    box(item, "Steel", (2.81, 2.6, 0), (.08, .10, 2.6), "EntryFrame")
    for z in (-2.80, 2.80):
        box(item, "ConcreteDark", (0, .27, z), (5.65, .54, .03), "LowerGuard")
    # Slanted upper bearers and a too-low abandoned cable tray suggest fatigue;
    # neither intrudes on the doorway or the usable standing volume.
    beam(item, (-2.6, 2.85, -1.8), (2.65, 2.66, -1.65), .10, .13, "SaggedBearer")
    box(item, "Rust", (-2.60, 2.45, 0), (.16, .075, 4.5), "CableTray")
    wear_decal(item, (-1.1, 1.15, -2.829), .75, 1.7, (0, 0, 1), 0)
    wear_decal(item, (-2.829, 1.0, 1.3), 1.25, 1.05, (1, 0, 0), 3)
    item.anchor("Entry", (3., 0, 0))
    item.anchor("RoomCenter", (0, 0, 0))
    return item


def make_main_door():
    item = Item("MainDoor")
    for x in (-1., 1.):
        box(item, "Steel", (x, 1.5, 0), (1.985, 3., .12), "DoorLeaf", collision=True)
        for y in (.35, 1.1, 2.25, 2.8):
            box(item, "Rust", (x, y, -.085), (1.92, .055, .055), "DoorReinforcement")
        for dx in (-.88, .88):
            box(item, "Steel", (x + dx, 1.5, -.075), (.07, 2.94, .045), "DoorReinforcement")
    for x in (-2.06, 2.06):
        box(item, "WornSteel", (x, 1.5, 0), (.12, 3., .22), "DoorFrame")
    box(item, "Rust", (0, 3.03, 0), (4.24, .12, .22), "DoorFrame")
    rod(item, "WornSteel", (-.17, 1.06, -.16), (-.17, 1.43, -.16), .026, "Handle")
    rod(item, "WornSteel", (.17, 1.06, -.16), (.17, 1.43, -.16), .026, "Handle")
    item.anchor("Approach", (0, 0, -1.3))
    return item


def make_gate():
    item = Item("Gate")
    for x in (-2., 2.):
        box(item, "Steel", (x, 1.5, 0), (.10, 3., .10), "GateFrame", collision=True)
    for y in (.05, 2.95):
        box(item, "Rust", (0, y, 0), (4.1, .10, .10), "GateFrame", collision=True)
    for index in range(17):
        x = -1.9 + index * .2375
        rod(item, "Steel", (x, .1, 0), (x, 2.9, 0), .026, "VerticalBars", collision=True, sides=6)
    for y in (.83, 1.73):
        box(item, "Rust", (0, y, 0), (3.94, .065, .055), "GateCrossbars", collision=True)
    # The physical latch is an unpowered future goal; no authored fiction text.
    box(item, "Steel", (.17, 1.15, -.065), (.22, .32, .085), "Latch", collision=True)
    rod(item, "Rust", (.16, .97, -.13), (.16, 1.18, -.13), .04, "Latch")
    item.anchor("Lock", (.17, 1.15, -.13))
    return item


def square_aperture(half_width, radius, bottom, top, sides=32):
    """A closed square plate with an actual circular central opening."""
    vertices = []
    for y in (bottom, top):
        for outside in (True, False):
            for index in range(sides):
                angle = index * math.tau / sides
                x, z = math.cos(angle), math.sin(angle)
                r = half_width / max(abs(x), abs(z)) if outside else radius
                vertices.append((x * r, y, z * r))
    faces = []
    for index in range(sides):
        following = (index + 1) % sides
        bo, bn = index, following
        bi, bj = sides + index, sides + following
        to, tn = 2 * sides + index, 2 * sides + following
        ti, tj = 3 * sides + index, 3 * sides + following
        faces.extend(((bo, bn, bj, bi), (to, ti, tj, tn),
                      (bo, to, tn, bn), (bi, bj, tj, ti)))
    geometry = vertices, faces
    if bp.signed_volume(geometry) < 0:
        geometry = vertices, [tuple(reversed(face)) for face in faces]
    return geometry


def annular_profile(profile, sides=28):
    """Sweep a closed wall profile around Y without ever capping its bore."""
    vertices = [(math.cos(index * math.tau / sides) * radius, y,
                 math.sin(index * math.tau / sides) * radius)
                for y, radius in profile for index in range(sides)]
    faces = []
    for row in range(len(profile)):
        following = (row + 1) % len(profile)
        for index in range(sides):
            next_index = (index + 1) % sides
            faces.append((row * sides + next_index, row * sides + index,
                          following * sides + index, following * sides + next_index))
    geometry = vertices, faces
    if bp.signed_volume(geometry) <= 1e-11:
        raise RuntimeError("Fan annular profile lost its closed positive wall volume")
    return geometry


def vertical_aperture_hit(geometry, x, z, bottom=-.8, top=.89):
    """Intersect a vertical bore ray with the actual authored face triangles."""
    vertices, faces = geometry
    for face in faces:
        first = vertices[face[0]]
        for index in range(1, len(face) - 1):
            second, third = vertices[face[index]], vertices[face[index + 1]]
            bx, bz = second[0] - first[0], second[2] - first[2]
            cx, cz = third[0] - first[0], third[2] - first[2]
            determinant = bx * cz - bz * cx
            # A vertical triangle cannot cross a ray strictly inside the bore.
            if abs(determinant) < 1e-12:
                continue
            px, pz = x - first[0], z - first[2]
            u = (px * cz - pz * cx) / determinant
            v = (bx * pz - bz * px) / determinant
            if u < -1e-9 or v < -1e-9 or u + v > 1 + 1e-9:
                continue
            y = first[1] + u * (second[1] - first[1]) + v * (third[1] - first[1])
            if bottom <= y <= top:
                return True
    return False


def validate_fan_aperture(item):
    # The central motor is deliberately excluded. Rim/duct/aperture geometry
    # must never cover the visible rotor; only DuctEnd closes the far recess.
    for (group, _), solids in item.parts.items():
        if group not in ("Rim", "Duct", "AperturePlate"):
            continue
        for geometry in solids:
            for x, z in ((0, 0), (1.5, 0), (0, -2.8), (2.6, 1.3)):
                if vertical_aperture_hit(geometry, x, z):
                    raise RuntimeError(f"Fan bore was capped by {group} at aperture ray {(x, z)}")


def make_fan_housing():
    item = Item("FanHousing")
    item.add("Steel", square_aperture(4., 3.57, 0, .16), "AperturePlate")
    for y, radius, inner in ((-.47, 3.85, 3.57), (.02, 3.88, 3.57), (.45, 3.78, 3.59)):
        item.add("Rust", annular_profile([(y, radius), (y + .09, radius),
                 (y + .09, inner), (y, inner)], 28), "Rim")
    item.add("Steel", annular_profile([(-.4, 3.72), (.45, 3.72),
             (.45, 3.65), (-.4, 3.65)], 28), "Duct")
    # Square roof aperture corners are solid in the housing, not four visible gaps.
    for sign in (-1, 1):
        for side in (-1, 1):
            box(item, "Steel", (sign * 3.73, .04, side * 3.73), (.54, .11, .54), "Mounts")
        box(item, "Steel", (sign * 3.93, .04, 0), (.14, .11, 8.), "Mounts")
        box(item, "Steel", (0, .04, sign * 3.93), (8., .11, .14), "Mounts")
    for index in range(8):
        angle = index * math.tau / 8
        x, z = math.cos(angle), math.sin(angle)
        beam(item, (x * .24, .43, z * .24), (x * 3.68, .43, z * 3.68), .065, .12, "MotorSupports")
        rod(item, "WornSteel", (x * 3.79, -.49, z * 3.79),
            (x * 3.79, -.33, z * 3.79), .035, "RimBolts", sides=6)
    rod(item, "Steel", (0, -.15, 0), (0, .76, 0), .24, "Motor", sides=16)
    rod(item, "WornSteel", (0, -.60, 0), (0, .12, 0), .08, "Spindle", sides=12)
    # A black capped duct above the recess prevents a view into the void.
    rod(item, "ConcreteDark", (0, .90, 0), (0, 1.02, 0), 3.65, "DuctEnd", sides=28)
    item.anchor("Rotor", (0, -.42, 0))
    validate_fan_aperture(item)
    return item


def blade_geometry():
    outline = [(.45, -.16), (.95, -.30), (2.2, -.48), (3.35, -.36),
               (3.5, 0), (3.3, .35), (1.7, .52), (.65, .33)]
    # Finite thickness and varying pitch produce a heavy, folded blade silhouette.
    points = [(x, .10 * z + layer, z) for layer in (-.035, .035) for x, z in outline]
    count = len(outline)
    faces = [tuple(range(count)), tuple(reversed(range(count, count * 2)))]
    faces.extend((i, (i + 1) % count, (i + 1) % count + count, i + count)
                 for i in range(count))
    geometry = points, faces
    if bp.signed_volume(geometry) < 0:
        geometry = points, [tuple(reversed(face)) for face in faces]
    return geometry


def make_fan_rotor():
    item = Item("FanRotor")
    rod(item, "Steel", (0, -.15, 0), (0, .15, 0), .47, "Hub", sides=16)
    for index in range(6):
        item.add("WornSteel" if index % 2 else "Rust",
                 bp.u_rotated(blade_geometry(), (0, index * 60, 0)), "Blades")
    item.anchor("Axis", (0, 0, 0))
    return item


def make_lift_frame():
    item = Item("LiftFrame")
    for x in (-1.43, 1.43):
        for z in (-1.33, 1.33):
            box(item, "Steel", (x, 4.45, z), (.14, 8.9, .14), "Uprights")
            box(item, "Rust", (x, .075, z), (.24, .15, .24), "Shoes")
        rod(item, "Steel", (x, .3, 0), (x, 8.55, 0), .07, "CarriageGuides")
    for y in (.15, 3.1, 6.3, 8.70):
        for z in (-1.33, 1.33):
            box(item, "Rust", (0, y, z), (3., .10, .10), "Crossbearers")
    # Actual cage bars remain visible; one thin collision panel per face makes
    # bullets/player never enter the decorative mover's simulation volume.
    for z in (-1.37, 1.37):
        for index in range(11):
            x = -1.35 + index * .27
            rod(item, "Steel", (x, .2, z), (x, 8.7, z), .014, "Cage", sides=6)
        for index in range(17):
            y = .4 + index * .5
            rod(item, "Steel", (-1.40, y, z), (1.40, y, z), .012, "Cage", sides=6)
        duplicate_collision(item, bp.u_box((0, 4.45, z), (3., 8.9, .05), .005), "Cage")
    for x in (-1.475, 1.475):
        duplicate_collision(item, bp.u_box((x, 4.45, 0), (.05, 8.9, 2.8), .005), "Cage")
        for z in (-.85, -.28, .28, .85):
            rod(item, "Steel", (x, .2, z), (x, 8.7, z), .014, "Cage", sides=6)
    box(item, "Steel", (0, 8.8, 0), (3., .2, 2.8), "HoistDeck")
    for x in (-.65, .65):
        rod(item, "Rust", (x, 8.32, -.85), (x, 8.32, .85), .23, "HoistDrums", sides=12)
        rod(item, "Steel", (x, .35, -.6), (x, 8.1, -.6), .027, "Chains", sides=6)
    item.anchor("PlatformLower", (0, .4, 0))
    item.anchor("PlatformUpper", (0, 6.6, 0))
    return item


def make_lift_platform():
    item = Item("LiftPlatform")
    box(item, "Steel", (0, 0, 0), (2.3, .2, 2.25), "Carriage")
    for x in (-1.03, 1.03):
        box(item, "Rust", (x, .19, 0), (.09, .18, 2.18), "CarriageLip")
    for z in (-1.03, 1.03):
        box(item, "Rust", (0, .19, z), (2.18, .18, .09), "CarriageLip")
    item.anchor("Axis", (0, 0, 0))
    return item


def make_chain_drum():
    item = Item("ChainDrum")
    item.anchor("DrumRotor", (0, 1.1, 0))
    for x in (-.96, .96):
        box(item, "Steel", (x, .68, 0), (.16, 1.36, .16), "Supports", collision=True)
        box(item, "Rust", (x, .055, 0), (.50, .11, 1.5), "Feet", collision=True)
        rod(item, "WornSteel", (x - .14, 1.1, 0), (x + .14, 1.1, 0), .16, "Bearings", sides=12)
    for z in (-.74, .74):
        box(item, "Steel", (0, .12, z), (3., .15, .10), "Frame", collision=True)
    rod(item, "Rust", (-.75, 1.1, 0), (.75, 1.1, 0), .53, "DrumRotor", sides=16)
    for x in (-.75, .75):
        rod(item, "Steel", (x - .045, 1.1, 0), (x + .045, 1.1, 0), .70, "DrumRotor", sides=16)
    for index in range(12):
        angle = index * math.tau / 12
        y, z = 1.1 + math.cos(angle) * .57, math.sin(angle) * .57
        rod(item, "Steel", (-.66, y, z), (.66, y, z), .026, "DrumRotor", sides=6)
    # The chain disappears into fixed guides instead of being a rotating rigid rope.
    for x in (-1.31, 1.31):
        box(item, "Steel", (x, 1.33, .52), (.11, 2.54, .13), "ChainGuides")
        rod(item, "Rust", (x, .2, .58), (x, 2.57, .58), .022, "HangingChains", sides=6)
    return item


def make_pusher_frame():
    item = Item("PusherFrame")
    box(item, "Steel", (0, .15, 0), (3.4, .3, 2.6), "Base", collision=True)
    for x in (-1.63, 1.63):
        for z in (-1.23, 1.23):
            box(item, "Rust", (x, 1.13, z), (.14, 2.14, .14), "GuardPosts")
        for y in (.55, 1.3, 2.12):
            rod(item, "Steel", (x, y, -1.25), (x, y, 1.25), .026, "GuardBars", sides=6)
        duplicate_collision(item, bp.u_box((x, 1.1, 0), (.07, 2.2, 2.6), .005), "Guard")
    for z in (-1.26, 1.26):
        for index in range(10):
            x = -1.53 + index * .34
            rod(item, "Steel", (x, .32, z), (x, 2.15, z), .017, "GuardBars", sides=6)
        duplicate_collision(item, bp.u_box((0, 1.1, z), (3.4, 2.2, .07), .005), "Guard")
    for x in (-.57, .57):
        rod(item, "Steel", (x, .42, -1.13), (x, .42, 1.13), .038, "LoadRails")
    box(item, "WornSteel", (0, .89, -1.14), (1.35, .94, .23), "Drive")
    for x in (-.48, .48):
        rod(item, "Rust", (x, .89, -1.05), (x, .89, -.60), .085, "Cylinders")
    item.anchor("HeadLower", (0, 0, .20))
    item.anchor("HeadUpper", (0, 0, .65))
    return item


def make_pusher_head():
    item = Item("PusherHead")
    box(item, "Steel", (0, .89, 0), (1.45, 1.15, .08), "PushPlate")
    for x in (-.48, .48):
        rod(item, "WornSteel", (x, .89, -1.05), (x, .89, -.025), .040, "PushRods")
    # The same saturated wet carton is carried back and forth without new output.
    cardboard(item, (0, 1.02, .32), (1.25, 1.05, .44), "EndlessLoad")
    box(item, "Timber", (0, .445, .32), (1.45, .10, .52), "EndlessLoad")
    item.anchor("Axis", (0, 0, 0))
    return item


def make_locker_props():
    item = Item("LockerProps")
    for index in range(3):
        x = (index - 1) * .65
        box(item, "Steel", (x, .925, 0), (.63, 1.85, .55), "LockerBodies", collision=True)
        box(item, "WornSteel", (x, .955, -.292), (.57, 1.70, .035), "LockerDoors")
        for y in (.27, 1.60):
            for row in range(3):
                box(item, "ConcreteDark", (x, y + row * .05, -.314), (.39, .017, .008), "Vents", .001)
        rod(item, "Rust", (x + .21, .84, -.35), (x + .21, 1.0, -.35), .016, "Handles", sides=6)
        box(item, "Rust", (x - .30, .25, -.295), (.05, .40, .045), "CorrodedHinge")
    return item


def make_break_props():
    item = Item("BreakProps")
    box(item, "Timber", (0, .79, .28), (1.9, .10, .65), "TableTop", collision=True)
    for x in (-.79, .79):
        for z in (.045, .515):
            beam(item, (x, .05, z), (x * .97, .74, z + .025), .07, .07, "TableLegs", collision=True)
    for x in (-.58, .58):
        box(item, "Timber", (x, .46, -.44), (.44, .075, .42), "StoolSeats", collision=True)
        for dx in (-.14, .14):
            for dz in (-.13, .13):
                beam(item, (x + dx * 1.15, .03, -.44 + dz * 1.2),
                     (x + dx, .425, -.44 + dz), .055, .055, "StoolLegs")
    rod(item, "Steel", (.47, .855, .24), (.47, 1.03, .24), .067, "TinCup", sides=10)
    box(item, "Cardboard", (-.35, .87, .33), (.39, .08, .30), "EmptyLunchWrapper", .01)
    return item


def make_technical_props():
    item = Item("TechnicalProps")
    for x, width, y, height in ((-.57, .68, .88, 1.76), (.39, .92, .65, 1.3)):
        box(item, "Steel", (x, y, .13), (width, height, .50), "ControlBodies", collision=True)
        box(item, "WornSteel", (x, y, -.14), (width - .07, height - .10, .035), "ControlDoors")
        for row in range(4):
            box(item, "ConcreteDark", (x, y - .2 + row * .09, -.166), (width * .60, .025, .008), "VentSlots", .001)
        rod(item, "Rust", (x + width * .32, y + .28, -.20),
            (x + width * .32, y + .43, -.20), .018, "Handles", sides=6)
    for x in (-.93, .88):
        rod(item, "Steel", (x, .1, .36), (x, 1.85, .36), .044, "PipeFeed")
        flange(item, (x, 1.65, .36), (0, 1, 0), .08, "PipeFeed")
    box(item, "Rust", (0, .035, .13), (2.15, .07, .90), "Plinth", collision=True)
    return item


def make_twisted_frame():
    item = Item("TwistedFrame")
    # Fixed closed-volume members suggest a buckled, impossible freight bay.
    # They are presentation only and placed away from all authoritative routes.
    for side in (-1, 1):
        points = [(side * 1.7, 0, 0), (side * 1.45, 1.65, .16),
                  (side * 1.88, 3.55, -.25), (side * 1.12, 5.7, .28)]
        for a, b in zip(points, points[1:]):
            beam(item, a, b, .16, .22, "BuckledPosts")
    for y, width, z in ((1.65, 2.9, .16), (3.55, 3.76, -.25), (5.7, 2.24, .28)):
        beam(item, (-width * .5, y, z), (width * .5, y - .13, z + .1), .14, .20, "BentBearers")
    for x in (-.50, .50):
        rod(item, "Rust", (x, 5.3, .1), (x + .07, 3.1, .13), .027, "DeadChain", sides=6)
    return item


def make_items():
    return [make_shell(), make_platform(), make_stair(), make_rail(), make_rack(),
            make_cargo(), make_pipes(), make_lamp(), make_pipe_straight(),
            make_pipe_elbow(), make_pipe_end(), make_room_shell(), make_main_door(),
            make_gate(), make_fan_housing(), make_fan_rotor(), make_lift_frame(),
            make_lift_platform(), make_chain_drum(), make_pusher_frame(),
            make_pusher_head(), make_locker_props(), make_break_props(),
            make_technical_props(), make_twisted_frame()]


def manifest(items, checksum):
    payload = passive.manifest(items, checksum)
    payload["generator"] = "tools/build-combat-warehouse-3d-model.py"
    payload["design_id"] = "combat_warehouse_industrial_nightmare_v2"
    payload["contract"] = {
        "shell_inner_dimensions": [30, 10.2, 24], "deck_tile_size": [3, .18, 3],
        "deck_top_local_y": 0, "platform_height": 3.2,
        "platform_posts_xz": [], "platform_post_width": 0,
        "platform_shoe_width": 0, "level_heights": [0, 3.2, 6.4],
        "stair_width": 2.4, "stair_run": 6., "stair_rise": 3.2,
        "stair_treads": 20, "stair_tread_depth": .3, "stair_tread_rise": .16,
        "room_dimensions": [6, 3, 6], "room_wall_thickness": .16,
        "room_door_width": 2.4, "room_door_height": 2.55,
        "room_door_local_direction": "+X", "room_floor_owned_by_deck": True,
        "main_door_size": [4, 3], "gate_size": [4, 3],
        "fan_diameter": 7., "fan_axis": "Y", "fan_rotor_anchor": [0, -.42, 0],
        "fan_aperture_validation": "vertical authored-face rays clear through rim/duct below DuctEnd",
        "lift_footprint": [3, 2.8], "lift_height": 8.9,
        "lift_platform_local_y": [.4, 6.6],
        "drum_rotor_anchor": [0, 1.1, 0], "drum_rotation_axis": "X",
        "pusher_footprint": [3.4, 2.6], "pusher_head_local_z": [.2, .65],
        "moving_parts_have_collision": False,
        "rail_length": 1, "rack_footprint": [3, 1.2], "cargo_footprint": [2, 1.4],
        "light_anchor": [0, -.37, 0], "collision_mesh_prefix": "Collision_",
        "authored_wear_quads": {"Shell": 8, "RoomShell": 2},
        "wear_collision": False, "wear_surface_role": "WearDecal",
    }
    if any(record["triangle_count"] <= 0 for record in payload["models"]):
        raise RuntimeError("Warehouse module lost measurable geometry")
    return payload


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    args.model_dir = args.model_dir.resolve()
    args.source_dir = args.source_dir.resolve()
    items = make_items()
    checksum = passive.signature(items)
    if passive.signature(make_items()) != checksum:
        raise RuntimeError("Warehouse repeated geometry differs")
    payload = manifest(items, checksum)
    roots = passive.build_objects(items)
    uv_cache = appearance.unwrap(roots, items)
    atlas_dir = args.source_dir / "Textures/Atlases"
    if not args.validate_only:
        atlas_dir.mkdir(parents=True, exist_ok=True)
    payload["surfaces"] = appearance.validate_layout(roots, uv_cache, None if args.validate_only else atlas_dir)
    # Rebuild and repack independently; geometry-only determinism cannot see UVs.
    roots = passive.build_objects(items)
    second_uv = appearance.unwrap(roots, items)
    if appearance.uv_signature(second_uv) != appearance.uv_signature(uv_cache):
        raise RuntimeError("Warehouse repeated unique UV pack differs")
    if args.validate_only:
        current = json.loads((args.model_dir / "CombatWarehouse3D.json").read_text(encoding="utf8"))
        payload["surfaces"]["base_sources"] = appearance.source_images()[1]
        payload["surfaces"]["atlases"] = current["surfaces"]["atlases"]
        if current != payload:
            raise RuntimeError("Published warehouse manifest differs from deterministic geometry")
        for record in current["surfaces"]["atlases"]:
            path = atlas_dir / (record["name"] + ".source.png")
            import hashlib
            if hashlib.sha256(path.read_bytes()).hexdigest() != record["sha256"]:
                raise RuntimeError("Published warehouse bake changed: " + record["name"])
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        sources, atlases = appearance.bake_atlases(roots, atlas_dir)
        payload["surfaces"]["base_sources"] = sources
        payload["surfaces"]["atlases"] = atlases
        # Blender object names are global. Export each module in a fresh scene
        # so common names such as Posts__Steel and Start never gain .001 suffixes.
        for item in items:
            root = passive.build_objects([item])[0]
            appearance.apply_uvs(root, uv_cache)
            passive.export(root, args.model_dir / (root.name + ".fbx"))
        (args.model_dir / "CombatWarehouse3D.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")
        roots = passive.build_objects(items)
        for root in roots:
            appearance.apply_uvs(root, uv_cache)
        appearance.source_materials(roots, atlas_dir)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CombatWarehouse3D.blend"), check_existing=False)
    passive.verify_fbx(args.model_dir, payload)
    appearance.verify_fbx_uvs(args.model_dir, uv_cache, [item.name for item in items])
    print("COMBAT WAREHOUSE ART CONTRACT OK " + json.dumps({"models": len(items),
          "triangles": payload["triangle_count"], "signature": checksum}), flush=True)


if __name__ == "__main__":
    main()
