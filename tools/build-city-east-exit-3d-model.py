#!/usr/bin/env python3
"""Deterministic civilian east-exit kit, authored in Unity-local metres.

Passive model only: layout, shared surface materials and collision belong to
CityEastExitWorldBuilder. Assembly roots are all at ground origin; +X is the
outbound road, and all closure modules extend along Z. Keep the imported FBX
unit factor when extracting a template. No text, people, audio or animation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp
import city_east_distance as mainland
import bpy
from mathutils import Vector

VERSION = "1.1.1"
DRESSING_VERSION = "1.2.0"
EXPORT_SETTINGS = {
    "axis_forward": "-Z", "axis_up": "Y",
    "apply_scale_options": "FBX_SCALE_NONE", "bake_space_transform": False,
    "hierarchy_contract": "Preserve complete imported transform basis when cloning an assembly",
}
MODEL_DIR = ROOT / "Assets/Resources/City/EastExit"
SOURCE_DIR = ROOT / "ArtSource/City/EastExit"
COLORS = {
    "Ground": (.30, .28, .21, 1), "Asphalt": (.17, .19, .19, 1),
    "Paint": (.66, .60, .44, 1), "Stone": (.38, .37, .31, 1),
    "Wall": (.53, .49, .38, 1), "Roof": (.18, .22, .20, 1),
    "Metal": (.22, .29, .26, 1), "Glass": (.12, .21, .22, 1),
    "Dark": (.065, .080, .072, 1), "Wood": (.25, .22, .16, 1),
    "Glow": (1.0, .64, .27, 1), "Foliage": (.27, .31, .20, 1),
}
PITCH = {"Ground": 12, "Asphalt": 12, "Paint": 2, "Stone": 2,
         "Wall": 3, "Roof": 4, "Metal": 2, "Glass": 1,
         "Dark": 2, "Wood": 2, "Glow": 1, "Foliage": 2}
DRESSING_COLORS = {"Gravel": (.34, .32, .26, 1), "DryGrass": (.36, .34, .23, 1)}
DRESSING_PITCH = {"Gravel": 4, "DryGrass": 2}
PART_ROLES = {"MetalPostL": "Metal", "MetalPostR": "Metal",
              "StoneFootL": "Stone", "StoneFootR": "Stone"}
PART_NAMES = {"MetalPostL": "Metal_PostL", "MetalPostR": "Metal_PostR",
              "StoneFootL": "Stone_FootL", "StoneFootR": "Stone_FootR"}
SHEET_PARTS = {("GravelPatch", "Gravel"), ("RoadRepair", "Asphalt"),
               ("RoadRepair", "Dark"), ("DryDrain", "Ground"), ("DryDrain", "Dark"),
               ("CanopyApron", "Gravel"), ("FenceToe", "Gravel")}
DRESSING_LIGHT_ANCHORS = {"CanopyLamp": (0, -.065, 0),
                          "ServiceWallLamp": (0, -.105, -.195)}
LANDSCAPE_BOUNDS = {
    "LowShrub": ((1.55, .62, 1.20), 0),
    "CreepingScrub": ((2.40, .45, 1.70), 0),
    "BranchShrub": ((1.80, 1.05, 1.50), 0),
    "MattedGrass": ((2.80, .27, 1.50), 0),
    "TallWeeds": ((1.40, .85, 1.00), 0),
    "GravelScatter": ((2.10, .12, 1.20), -.025),
    "DrainInspection": ((1.30, .12, .85), -.015),
    "RepairStock": ((2.00, .45, .80), 0),
}
TERRAIN_CONFORM_ASSEMBLIES = (
    "GravelPatch", "CanopyApron", "FenceToe", "RoadRepair", "DryDrain",
    "GroundRidge", "EarthBank", "DryGrass", "LowShrub", "CreepingScrub",
    "BranchShrub", "MattedGrass", "TallWeeds", "GravelScatter")


def box(center, size, bevel=.012):
    return bp.u_box(center, size, bevel)


def road_asphalt():
    """One closed slab, split at its crown so runtime crossfall retains Y=.035."""
    original, polygons = box((0, -.075, 0), (10, .22, 6), .015)
    vertices, faces, intersections = list(original), [], {}

    def crossing(a, b):
        if abs(vertices[a][2]) < 1e-10:
            return a
        if abs(vertices[b][2]) < 1e-10:
            return b
        edge = tuple(sorted((a, b)))
        if edge not in intersections:
            va, vb = vertices[a], vertices[b]
            t = -va[2] / (vb[2] - va[2])
            intersections[edge] = len(vertices)
            vertices.append((va[0] + t * (vb[0] - va[0]),
                             va[1] + t * (vb[1] - va[1]), 0.0))
        return intersections[edge]

    for polygon in polygons:
        zs = [vertices[i][2] for i in polygon]
        if min(zs) >= 0 or max(zs) <= 0:
            faces.append(polygon)
            continue
        for side in (-1, 1):
            clipped = []
            for a, b in zip(polygon, polygon[1:] + polygon[:1]):
                if vertices[a][2] * side >= 0:
                    clipped.append(a)
                if vertices[a][2] * vertices[b][2] < 0:
                    clipped.append(crossing(a, b))
            faces.append(tuple(clipped))
    return vertices, faces


def cylinder(center, radius, height, sides=8):
    return bp.u_cylinder(center, (radius * 2, height / 2, radius * 2), sides)


def beam(a, b, width, depth=None):
    """Solid beam joining exact endpoints, with two distinct transverse axes."""
    direction = Vector(b) - Vector(a)
    length = direction.length
    axis = direction.normalized()
    auxiliary = Vector((0, 1, 0)) if abs(axis.y) < .95 else Vector((1, 0, 0))
    right = axis.cross(auxiliary).normalized()
    up = axis.cross(right).normalized()
    geometry = box((0, 0, 0), (width, depth or width, length), min(.006, width / 8))
    center = (Vector(a) + Vector(b)) / 2
    return ([tuple(center + right * x + up * y + axis * z) for x, y, z in geometry[0]],
            geometry[1])


def wall(length, openings=()):
    # interior_kit's wall_run is Z-up. Swap back to Unity before assembly.
    return bp.to_source(kit.wall_run(length, 2.66, .18, openings, chamfer=.012, base=.16))


def move(geometry, offset, yaw=0):
    return kit.translated(bp.u_rotated(geometry, (0, yaw, 0)), offset)


def berm():
    """An irregular closed earth mound, never a straight extruded wall."""
    rings = [(-.06, 1), (.24, .83), (.59, .51), (.83, .14)]
    count = 13
    vertices = []
    for level, scale in rings:
        for i in range(count):
            angle = math.tau * i / count
            jitter = 1 + .08 * math.sin(i * 7.7)
            vertices.append((math.cos(angle) * 3 * scale * jitter,
                             level + (.045 * math.sin(i * 5.3) if scale < 1 else 0),
                             math.sin(angle) * 1.5 * scale * jitter))
    faces = [tuple(range(count))]
    for ring in range(len(rings) - 1):
        for i in range(count):
            j = (i + 1) % count
            a, b = ring * count + i, ring * count + j
            c, d = (ring + 1) * count + j, (ring + 1) * count + i
            faces.extend(((b, a, d), (b, d, c)))
    faces.append(tuple(reversed(range((len(rings) - 1) * count, len(rings) * count))))
    return vertices, faces


def ground_sheet(length, width, rows, columns, elevation, seed=1):
    """Tessellated upward sheet with a broken outline, without a vertical skirt.

    The runtime adds sampled terrain height to each authored vertex. End caps
    narrow irregularly instead of ending in a rectangular platform edge.
    """
    vertices, faces = [], []
    for row in range(rows + 1):
        t = row / rows
        x = (t - .5) * length
        end = min(1, .48 + min(t, 1 - t) * 7)
        shift = width * .045 * math.sin(row * 2.17 + seed)
        for column in range(columns + 1):
            cross = column / columns * 2 - 1
            edge = 1 + .065 * math.sin(row * 3.31 + seed + (0 if cross < 0 else 1.4))
            z = cross * width * .5 * end * edge + shift
            y = elevation(t, cross)
            vertices.append((x, y, z))
    stride = columns + 1
    for row in range(rows):
        for column in range(columns):
            a = row * stride + column
            b, c, d = a + 1, a + stride + 1, a + stride
            # +Z cross +X points +Y before the source axis swap/rewind.
            faces.extend(((a, b, c), (a, c, d)))
    return vertices, faces


def folded_leaf(length, width, thickness=.024):
    """Closed six-edged leaf with a lifted midrib; no opaque canopy cone."""
    outline = ((-.50, 0), (-.22, -.46), (.21, -.38),
               (.50, .02), (.11, .50), (-.30, .35))
    vertices = [(x * length, 0, z * width) for x, z in outline]
    vertices += [(0, thickness, 0), (-.06 * length, -thickness * .32, 0)]
    faces = []
    for i in range(6):
        following = (i + 1) % 6
        faces.extend(((following, i, 6), (i, following, 7)))
    return vertices, faces


def open_pipe(length, radius, wall=.012, sides=8):
    """Short hollow stock along X: actual open ends and finite wall thickness."""
    vertices = []
    for x, r in ((-length / 2, radius), (length / 2, radius),
                 (-length / 2, radius - wall), (length / 2, radius - wall)):
        vertices += [(x, math.cos(i * math.tau / sides) * r,
                      math.sin(i * math.tau / sides) * r) for i in range(sides)]
    faces = []
    for i in range(sides):
        j = (i + 1) % sides
        faces.extend(((i, j, sides + j, sides + i),
                      (2 * sides + j, 2 * sides + i, 3 * sides + i, 3 * sides + j),
                      (j, i, 2 * sides + i, 2 * sides + j),
                      (sides + i, sides + j, 3 * sides + j, 3 * sides + i)))
    return vertices, faces


def make_dressing():
    """Small passive furnishings and continuous, terrain-following ground marks."""
    assemblies = {}

    def add(group, role, *geometries):
        for geometry in geometries:
            if (group, role) not in SHEET_PARTS and bp.signed_volume(geometry) <= 0:
                raise ValueError(f"Inward dressing solid: {group}/{role}")
            assemblies.setdefault(group, {}).setdefault(role, []).append(geometry)

    # A lean-to fastened to the booth at +Z, carried by two open front posts.
    # Posts and feet stay separate so the runtime can fit their bottoms to the
    # sampled slope while keeping the roof rigid and aligned to the booth.
    roof_height = lambda z: 2.50 + (z + 1.6) / 3.2 * .105
    for side, suffix in ((-1, "L"), (1, "R")):
        x, z = side * 1.55, -1.3
        add("Shelter", "StoneFoot" + suffix, box((x, .065, z), (.31, .13, .31), .025))
        add("Shelter", "MetalPost" + suffix,
            box((x, 1.245, z), (.075, 2.49, .075), .006))
        add("Shelter", "Metal", beam((x, 2.00, z), (x, 2.47, z + .49), .045),
            beam((x, 2.03, z), (x - side * .43, 2.47, z), .042))
    for z in (-1.3, 1.48):
        add("Shelter", "Metal", beam((-1.65, roof_height(z) - .042, z),
            (1.65, roof_height(z) - .042, z), .075, .055))
    for x in (-1.55, 0, 1.55):
        add("Shelter", "Metal", beam((x, roof_height(-1.54) - .025, -1.54),
            (x, roof_height(1.54) - .025, 1.54), .06, .045))
    roof = box((0, 0, 0), (3.60, .045, 3.20), .007)
    roof = ([tuple((x, y + roof_height(z), z)) for x, y, z in roof[0]], roof[1])
    add("Shelter", "Roof", roof)
    for x in (-1.25, -.63, .02, .65, 1.28):
        add("Shelter", "Roof", beam((x, roof_height(-1.57) + .024, -1.57),
            (x, roof_height(1.57) + .024, 1.57), .025, .018))
    add("Shelter", "Metal", beam((-1.79, 2.487, -1.585), (1.79, 2.487, -1.585), .04, .08),
        box((0, 2.586, 1.565), (3.48, .128, .07), .008))
    for x in (-1.5, -.5, .5, 1.5):
        add("Shelter", "Metal", box((x, 2.532, 1.57), (.09, .23, .06), .006))

    # Worn slats with real gaps, eased corners and a slightly reclining back.
    for x in (-.70, .70):
        for z in (-.215, .215):
            leg = beam((x, .015, z), (x, .438, z * .87), .045)
            add("Bench", "Metal", move(leg, (0, -kit.bounds(leg)[0][1], 0)))
        add("Bench", "Metal", beam((x, .393, -.27), (x, .393, .27), .05),
            beam((x, .07, .17), (x, .849, .298), .045))
    add("Bench", "Metal", beam((-.72, .215, .19), (.72, .215, .19), .037))
    for i, z in enumerate((-.208, -.069, .070, .209)):
        add("Bench", "Wood", box(((-.005 if i == 2 else 0), .452 + i % 2 * .001, z),
            (1.90 - (i % 3) * .017, .043, .121), .009))
    for y in (.641, .812):
        plank = box((0, y, .273 + (y - .641) * .16), (1.88, .134, .035), .009)
        add("Bench", "Wood", bp.u_rotated_about(plank, (9, 0, 0), (0, y, .273)))
        for x in (-.7, .7):
            add("Bench", "Dark", box((x, y, .249 + (y - .641) * .16), (.016, .016, .008), .002))
    # One rubbed edge and an old splice, rather than repeated decoration.
    add("Bench", "Wood", box((-.44, .476, -.263), (.36, .005, .017), .001))
    add("Bench", "Metal", box((.25, .416, .069), (.19, .014, .15), .003))

    # Closed electrical service cabinet: inset leaf, louvres, hinges and feet.
    add("UtilityCabinet", "Stone", box((0, .105, 0), (.82, .21, .50), .025))
    add("UtilityCabinet", "Metal", box((0, .81, .012), (.75, 1.22, .43), .025),
        box((0, 1.429, 0), (.82, .042, .50), .012))
    add("UtilityCabinet", "Dark", box((0, .81, -.211), (.665, 1.104, .018), .004))
    add("UtilityCabinet", "Metal", box((-.008, .812, -.23), (.63, 1.07, .031), .012),
        box((.218, .83, -.257), (.037, .136, .038), .006))
    for y in (.39, 1.21):
        add("UtilityCabinet", "Metal", box((-.345, y, -.231), (.039, .092, .035), .006))
    for y in (.40, .445, .49, .535, 1.085, 1.13, 1.175):
        add("UtilityCabinet", "Dark", box((0, y, -.249), (.45, .011, .012), .002))
        add("UtilityCabinet", "Metal", beam((-.23, y + .014, -.254), (.23, y + .014, -.254), .012, .025))
    add("UtilityCabinet", "Metal", box((.16, .252, -.248), (.17, .026, .012), .003))

    add("GravelPatch", "Gravel", ground_sheet(6, 3, 14, 6,
        lambda t, s: .008 + .009 * (1 - abs(s)) * math.sin(t * math.pi), 11))
    # A single worn surface under the canopy joins the foot approach to the
    # doorway. Its gently broken edges remain a ground mark, never a slab.
    add("CanopyApron", "Gravel", ground_sheet(4.8, 3.15, 18, 12,
        lambda t, s: .008 + .006 * (1 - abs(s)) * math.sin(t * math.pi), 19))
    add("FenceToe", "Gravel", ground_sheet(7.8, .48, 20, 4,
        lambda t, s: .008 + .008 * (1 - abs(s)), 31))
    add("RoadRepair", "Asphalt", ground_sheet(4.4, 2.7, 12, 6,
        lambda t, s: .010 + .004 * math.sin(t * math.pi) * (1 - abs(s)), 7))
    # Two short sealed cracks inside the patch. They stay thin and climb the
    # same terrain sample as the patch, with no freestanding plate underneath.
    crack = ground_sheet(1.27, .038, 7, 1, lambda t, s: .018, 3)
    add("RoadRepair", "Dark", move(crack, (-.53, 0, .38), 16),
        move(ground_sheet(.72, .033, 5, 1, lambda t, s: .018, 8), (.80, 0, -.31), -27))

    add("DryDrain", "Ground", ground_sheet(10, 1.2, 20, 8,
        lambda t, s: .004 + .075 * math.sin(abs(s) * math.pi) ** 2, 13))
    add("DryDrain", "Dark", ground_sheet(9.85, .25, 20, 2,
        lambda t, s: .012 + .01 * abs(s), 4))
    for i in range(9):
        x, side = -4.1 + i * 1.04, -1 if i % 2 else 1
        rock = kit.scaled(berm(), (.027 + i % 3 * .007, .074, .055))
        add("DryDrain", "Gravel", move(rock, (x, .035, side * (.32 + i % 2 * .05)), i * 29))

    ridge = kit.scaled(berm(), (.65, .51, .56))
    add("GroundRidge", "Ground", move(ridge, (0, .0306, 0)))
    for i, (x, z) in enumerate(((-1.15, -.28), (.87, .36), (.25, -.46))):
        add("GroundRidge", "Gravel", move(kit.scaled(berm(), (.060, .095, .073)),
            (x, .09, z), i * 53))

    # Broad low earth, with an asymmetric shoulder and an embedded toe. This
    # is a loose bank inside the fence rather than a new perimeter wall.
    bank = kit.scaled(berm(), (1.60, .65, .83))
    bank = ([(x + .14 * math.sin(z * 2.1), y,
              z + .15 * math.sin(x * .71) * max(0, y)) for x, y, z in bank[0]], bank[1])
    add("EarthBank", "Ground", bank)

    # One shallow, supported pedestrian drain crossing. The metal bears on
    # both low stone kerbs; every top stays within a normal walking step.
    for z in (-.43, .43):
        add("DrainCrossing", "Stone", box((0, .024, z), (1.30, .048, .15), .012))
    for x in (-.58, .58):
        add("DrainCrossing", "Metal", box((x, .042, 0), (.075, .030, .87), .005))
    for i in range(9):
        add("DrainCrossing", "Metal", box((-.49 + i * .1225, .045, 0), (.055, .026, .84), .004))

    # Worn individual post bases, not a new continuous concrete plinth.
    add("FenceFooting", "Stone", box((0, .048, 0), (.46, .096, .42), .035))
    add("FenceFooting", "Dark", box((0, .099, 0), (.15, .006, .15), .005))

    # Open irregular shrubs: the silhouette belongs to branches and separated
    # folded leaves. Sparse fork ends remain bare rather than meeting a solid
    # boulder-like canopy. All roots share the same terrain datum.
    for group, count, spread, height in (("LowShrub", 7, .57, .54),
                                        ("CreepingScrub", 10, 1.10, .36),
                                        ("BranchShrub", 7, .63, .94)):
        for i in range(count):
            angle = .43 + i * 2.399
            radius = spread * (.70 + i % 3 * .13)
            root = (math.cos(angle) * .10, .016, math.sin(angle) * .08)
            elbow = (math.cos(angle) * radius * .42,
                     height * (.30 + i % 3 * .07), math.sin(angle) * radius * .40)
            end = (math.cos(angle) * radius, height * (.68 + i % 4 * .085),
                   math.sin(angle) * radius * .72)
            add(group, "Wood", beam(root, elbow, .022 if group != "BranchShrub" else .033),
                beam(elbow, end, .016))
            for fork in range(2):
                t = .53 + fork * .22
                start = tuple(elbow[a] + (end[a] - elbow[a]) * t for a in range(3))
                fork_angle = angle + (-.85 if fork == 0 else .92)
                tip = (start[0] + math.cos(fork_angle) * .20,
                       start[1] + .065, start[2] + math.sin(fork_angle) * .19)
                add(group, "Wood", beam(start, tip, .011))
                if group == "BranchShrub" and (i + fork) % 3 == 0:
                    continue
                leaf_length = .31 if group != "CreepingScrub" else .43
                for side in (-1, 1):
                    leaf = bp.u_rotated(folded_leaf(leaf_length, leaf_length * .59),
                        (side * 19, math.degrees(fork_angle) + side * 36, -11 + i % 3 * 12))
                    add(group, "Foliage", move(leaf, (tip[0] + side * .055,
                        tip[1] - .005, tip[2] + side * .038)))

    # Flattened grass has long folded ribbons in staggered roots and broken
    # patches; the taller seed stems retain their own slender upright rhythm.
    for i in range(39):
        x = -1.21 + (i * 17 % 37) / 36 * 2.42
        z = -.60 + (i * 11 % 31) / 30 * 1.20
        angle = 25 + math.sin(i * 2.31) * 33
        blade = bp.u_rotated(folded_leaf(.28 + i % 5 * .068, .031 + i % 3 * .013, .012),
            (-8 + i % 3 * 9, angle, 9 + i % 4 * 6))
        add("MattedGrass", "DryGrass", move(blade, (x, -kit.bounds(blade)[0][1], z)))
    for i in range(15):
        angle = i * 2.399
        x, z = math.cos(angle) * (.17 + i % 3 * .105), math.sin(angle) * .26
        height = .49 + i % 5 * .087
        elbow = (x + math.cos(angle) * .055, height * .60, z + math.sin(angle) * .050)
        tip = (x + math.cos(angle) * .11, height, z + math.sin(angle) * .10)
        add("TallWeeds", "DryGrass", beam((x, .008, z), elbow, .008), beam(elbow, tip, .006))
        for side in (-1, 1):
            leaf = bp.u_rotated(folded_leaf(.27 + i % 3 * .025, .033, .010),
                (side * 12, i * 43 + side * 37, side * 28))
            add("TallWeeds", "DryGrass", move(leaf, (elbow[0], elbow[1] * .77, elbow[2])))
        if i % 3 != 0:
            add("TallWeeds", "DryGrass", move(bp.u_rotated(folded_leaf(.071, .027, .020),
                (0, i * 37, 72)), tip))

    # Only distinct stones break the ground: there is no shared dark pedestal.
    for i in range(17):
        angle = i * 2.399
        radius = .15 + (i * 7 % 13) / 12 * .85
        stone = box((0, 0, 0), (.13 + i % 4 * .045, .06 + i % 3 * .025,
            .10 + i % 5 * .026), .025)
        stone = bp.u_rotated(stone, (i % 3 * 9, i * 43, -7 + i % 4 * 5))
        add("GravelScatter", "Gravel", move(stone,
            (math.cos(angle) * radius, -.025 - kit.bounds(stone)[0][1], math.sin(angle) * radius * .53)))

    # A dry inspection grate visibly bears on four narrow old stone edges;
    # open slots show the terrain, not a broad opaque black backing plate.
    for x in (-.595, .595):
        add("DrainInspection", "Stone", box((x, .025, 0), (.11, .080, .85), .016))
    for z in (-.375, .375):
        add("DrainInspection", "Stone", box((0, .025, z), (1.14, .080, .10), .014))
        add("DrainInspection", "Metal", box((0, .083, z * .85), (1.13, .038, .055), .005))
    for i in range(12):
        add("DrainInspection", "Metal", box((-.52 + i * 1.04 / 11, .085, 0),
            (.045, .040, .67), .004))

    # A modest repair supply, physically carried by two timber bearers. Open
    # pipe ends and angle iron distinguish this from repeated decorative boxes.
    for x in (-.62, .62):
        add("RepairStock", "Wood", box((x, .060, 0), (.15, .12, .80), .012))
    for length, z, radius in ((2.0, -.25, .075), (1.81, -.07, .065), (1.93, .10, .070)):
        add("RepairStock", "Metal", move(open_pipe(length, radius), (0, .12 + radius, z)))
    add("RepairStock", "Metal", box((.02, .145, .295), (1.78, .05, .080), .004),
        box((.02, .195, .327), (1.78, .10, .016), .003))
    # One upper replacement rail rests across the three lower pipe crowns.
    for x in (-.52, .52):
        add("RepairStock", "Metal", box((x, .290, -.07), (.085, .075, .45), .006))
    add("RepairStock", "Metal", move(open_pipe(1.72, .061), (0, .3885, -.07)))

    # Passive measured fixtures. Runtime owns the two bounded practicals.
    add("CanopyLamp", "Metal", box((0, 0, 0), (.46, .10, .20), .018),
        box((-.15, .043, 0), (.07, .014, .16), .003),
        box((.15, .043, 0), (.07, .014, .16), .003))
    add("CanopyLamp", "Glow", box((0, -.049, 0), (.36, .012, .135), .004))
    add("ServiceWallLamp", "Metal", box((0, 0, -.027), (.16, .30, .055), .015),
        box((0, -.01, -.105), (.22, .20, .13), .028),
        box((0, .11, -.10), (.25, .04, .18), .014))
    add("ServiceWallLamp", "Glow", box((0, -.083, -.158), (.145, .047, .018), .007))

    # Three sparse, uneven bunches. Narrow closed blades survive a low-poly
    # silhouette without billboard transparency or a repeated hedge line.
    for cluster, (x, z) in enumerate(((-.41, -.14), (.29, .20), (.08, -.26))):
        for i in range(9):
            angle = i * 2.399 + cluster * .7
            height = .27 + ((i * 11 + cluster * 7) % 19) * .019
            spread = .12 + i % 3 * .055
            a = (x + math.cos(angle) * .035, 0, z + math.sin(angle) * .035)
            b = (x + math.cos(angle) * spread * .53, height * .58, z + math.sin(angle) * spread * .53)
            c = (x + math.cos(angle) * spread, height, z + math.sin(angle) * spread)
            add("DryGrass", "DryGrass", beam(a, b, .013, .031), beam(b, c, .008, .022))
        add("DryGrass", "Ground", move(kit.scaled(berm(), (.068, .045, .098)), (x, .004, z)))

    # The same closed four-metre fence, with one visibly replaced center field.
    # All bars remain present; repairs do not create a new passable gate.
    for z in (-2, 2):
        add("RepairedFence", "Stone", box((0, .11, z), (.25, .22, .25), .025))
        add("RepairedFence", "Metal", box((0, .96, z), (.11, 1.72, .11), .012),
            box((0, 1.83, z), (.14, .05, .14), .012))
    for y in (.19, 1.69):
        add("RepairedFence", "Metal", box((0, y, 0), (.065, .07, 4), .005))
    for i in range(1, 21):
        z = -2 + 4 * i / 21
        width = .046 if 8 <= i <= 13 else .035
        add("RepairedFence", "Metal", box((.012 if 8 <= i <= 13 else 0, .94, z),
            (width, 1.47, width), .004))
    for y in (.31, 1.52):
        add("RepairedFence", "Metal", box((-.052, y, .05), (.025, .063, 1.20), .005))
        for z in (-.49, .59):
            add("RepairedFence", "Dark", box((-.070, y, z), (.011, .020, .020), .002))
    add("RepairedFence", "Metal", beam((.045, .24, -.52), (.045, 1.62, .62), .035))
    result = {group: {role: kit.merge_all(items) for role, items in roles.items()}
              for group, roles in assemblies.items()}
    # Fix the authored metre envelopes once here. Runtime retains these actual
    # bounds and only applies its ordinary bounded variation and terrain fitting.
    for group, (size, floor) in LANDSCAPE_BOUNDS.items():
        low, high = kit.bounds(kit.merge_all(result[group].values()))
        scale = tuple(size[a] / (high[a] - low[a]) for a in range(3))
        origin = ((low[0] + high[0]) / 2, low[1], (low[2] + high[2]) / 2)
        for role, (vertices, faces) in result[group].items():
            result[group][role] = ([tuple((point[a] - origin[a]) * scale[a] +
                (floor if a == 1 else 0) for a in range(3)) for point in vertices], faces)
    return result


def make_assemblies():
    assemblies = {}
    def add(group, role, *geometries):
        for geometry in geometries:
            volume = bp.signed_volume(geometry)
            if volume <= 0:
                raise ValueError(f"Inward/degenerate solid: {group}/{role}: {volume}")
            assemblies.setdefault(group, {}).setdefault(role, []).append(geometry)

    # Matched full-width road and gravel shoulders; no curb sealing the walk-in.
    add("Road", "Asphalt", road_asphalt())
    for side in (-1, 1):
        add("Road", "Ground", box((0, -.09, side * 3.5), (10, .20, 1), .008))
        add("Road", "Paint", box((0, .042, side * 2.73), (10, .012, .10), .002))
    for x in (-3.4, 1.6):
        add("Road", "Paint", box((x, .042, 0), (2.3, .012, .10), .002))

    # Small masonry booth. Real openings leave deep opaque reveals around inset glass.
    add("Booth", "Stone", box((0, .08, 0), (3.18, .16, 3.58), .035))
    opening = kit.Opening(center=0, width=1.88, sill=1.06, head=2.12)
    add("Booth", "Wall", move(wall(3.4, [opening]), (-1.41, 0, 0), 90))
    add("Booth", "Wall", move(wall(3.4, [opening]), (1.41, 0, 0), 90))
    door_opening = kit.Opening(center=.48, width=.94, sill=0, head=2.22)
    add("Booth", "Wall", move(wall(2.64, [door_opening]), (0, 0, -1.61)),
        move(wall(2.64), (0, 0, 1.61)))
    # The window plane sits 7cm behind the outer wall, not on its face.
    for side in (-1, 1):
        x = side * 1.425
        add("Booth", "Glass", box((x, 1.75, 0), (.025, .91, 1.73), .003))
        for z in (-.915, .915):
            add("Booth", "Metal", box((side * 1.46, 1.75, z), (.055, 1.08, .07), .008))
        for y in (1.21, 2.29):
            add("Booth", "Metal", box((side * 1.46, y, 0), (.06, .07, 1.90), .008))
        add("Booth", "Metal", box((side * 1.46, 1.75, .28), (.065, 1.01, .055), .005))
        add("Booth", "Stone", box((side * 1.52, 1.19, 0), (.27, .07, 2.03), .015))
    add("Booth", "Dark", box((0, .2, 0), (2.62, .06, 3.02), .008))
    # Door is closed, recessed, with pressed lower panel and actual handle hardware.
    add("Booth", "Metal", box((.48, 1.27, -1.62), (.85, 2.18, .065), .018),
        box((.48, .73, -1.668), (.66, .86, .030), .018))
    add("Booth", "Glass", box((.48, 1.82, -1.66), (.62, .74, .025), .008))
    add("Booth", "Dark", box((.80, 1.22, -1.69), (.055, .20, .025), .007),
        box((.75, 1.25, -1.723), (.15, .03, .04), .006))
    add("Booth", "Stone", box((.48, .07, -1.93), (1.10, .14, .47), .025))
    # Asymmetric shallow metal roof with an actual drip edge, visible seams and back drain.
    roof = box((0, 2.94, 0), (3.44, .17, 3.84), .015)
    add("Booth", "Roof", bp.u_rotated_about(roof, (0, 0, -2.1), (0, 2.94, 0)))
    for z in (-1.91, 1.91):
        add("Booth", "Metal", box((0, 2.89, z), (3.46, .12, .05), .005))
    for z in (-1.15, -.39, .38, 1.14):
        add("Booth", "Roof", beam((-1.70, 3.086, z), (1.70, 2.962, z), .034, .018))
    add("Booth", "Metal", cylinder((1.48, 1.45, 1.66), .042, 2.6),
        beam((1.48, .19, 1.66), (1.65, .12, 1.76), .08))
    # Worn lower courses and one old repair: geometry lives away from wall planes.
    for x, z, sx, sz in ((-1.514, -.70, .016, .65), (-1.514, .84, .016, .45),
                          (.90, -1.714, .34, .016), (-.85, -1.714, .56, .016)):
        add("Booth", "Stone", box((x, .33, z), (sx, .25, sz), .003))
    add("Booth", "Metal", box((-.76, 1.42, -1.73), (.36, .52, .10), .02))
    for y in (1.25, 1.33, 1.41, 1.49, 1.57):
        add("Booth", "Dark", box((-.76, y, -1.787), (.26, .014, .01), .001))

    # Civilian boom with a hanging barred skirt; closure is readable below the beam.
    for z in (-3.12, 3.12):
        add("Barrier", "Stone", box((0, .10, z), (.50, .20, .50), .04))
        add("Barrier", "Metal", box((0, .69, z), (.32, 1.18, .33), .03))
    add("Barrier", "Paint", box((0, 1.19, 0), (.16, .18, 6.16), .014))
    add("Barrier", "Metal", box((0, .13, 0), (.07, .07, 6.10), .009))
    for i in range(25):
        z = -3 + i * .25
        add("Barrier", "Metal", box((0, .64, z), (.055, 1.02, .045), .005))
    for z in (-2.2, -.6, 1.0, 2.6):
        add("Barrier", "Metal", box((-.086, 1.19, z), (.014, .185, .52), .002))
    add("Barrier", "Metal", box((.13, 1.08, -3.12), (.24, .34, .48), .028))

    for group, length in (("Fence", 4), ("Gate", 1.4)):
        for z in (-length / 2, length / 2):
            add(group, "Stone", box((0, .11, z), (.25, .22, .25), .025))
            add(group, "Metal", box((0, .96, z), (.11, 1.72, .11), .012),
                box((0, 1.83, z), (.14, .05, .14), .012))
        for y in (.19, 1.69):
            add(group, "Metal", box((0, y, 0), (.065, .07, length), .005))
        count = round(length / .19)
        for i in range(1, count):
            z = -length / 2 + length * i / count
            add(group, "Metal", box((0, .94, z), (.035, 1.47, .035), .004))
        if group == "Gate":
            add(group, "Metal", beam((.04, .23, -.58), (.04, 1.64, .58), .035),
                box((-.05, 1.01, .45), (.09, .06, .21), .006))

    add("DrainCover", "Stone", box((0, -.095, 0), (1.2, .19, 8), .015))
    add("DrainCover", "Dark", box((0, .006, 0), (.83, .025, 7.74), .004))
    for i in range(44):
        add("DrainCover", "Metal", box((0, .026, -3.76 + i * .175), (.94, .025, .075), .005))
    for x in (-.48, .48):
        add("DrainCover", "Metal", box((x, .028, 0), (.075, .025, 7.8), .004))

    add("Mound", "Ground", berm())
    for i, (x, z) in enumerate(((-1.65, .05), (.6, -.3), (1.3, .4))):
        for j in range(4):
            a = j * 1.57 + i
            base = (x, .38, z)
            top = (x + math.cos(a) * .24, .70 + j * .045, z + math.sin(a) * .24)
            add("Mound", "Foliage", beam(base, top, .045, .11))

    for i in range(7):
        angle = i * 2.4
        radius = .20 + (i % 3) * .10
        endpoint = (math.cos(angle) * radius, .64 + (i % 3) * .14, math.sin(angle) * radius)
        add("Shrub", "Wood", beam((0, 0, 0), endpoint, .042))
        foliage = kit.scaled(berm(), (.19, .56, .25))
        add("Shrub", "Foliage", move(foliage, (endpoint[0], endpoint[1] - .25, endpoint[2])))

    # One hooded practical: small warm downward lens, no spotlight tower.
    add("Lamp", "Stone", box((0, .11, 0), (.46, .22, .46), .035))
    add("Lamp", "Metal", cylinder((0, 1.78, 0), .061, 3.34),
        beam((0, 3.35, 0), (.33, 3.44, 0), .07),
        box((.32, 3.43, 0), (.47, .13, .28), .035))
    add("Lamp", "Glow", box((.32, 3.355, 0), (.34, .025, .19), .008))
    return {group: {role: kit.merge_all(items) for role, items in roles.items()}
            for group, roles in assemblies.items()}


def manifest_for(assemblies):
    records = []
    for group, roles in assemblies.items():
        parts = []
        for role, geometry in roles.items():
            low, high = kit.bounds(geometry)
            parts.append({"mesh": f"EEX_{group}_{role}", "role": role,
                          "vertex_count": len(geometry[0]),
                          "triangle_count": kit.triangle_count(geometry),
                          "bounds_min_unity": [round(v, 6) for v in low],
                          "bounds_max_unity": [round(v, 6) for v in high]})
        low, high = kit.bounds(kit.merge_all(roles.values()))
        records.append({"name": group, "bounds_min_unity": [round(v, 6) for v in low],
                        "bounds_max_unity": [round(v, 6) for v in high], "parts": parts})
    digest = hashlib.sha256(json.dumps(assemblies, sort_keys=True,
                                      separators=(",", ":")).encode()).hexdigest()
    return {"generator": "tools/build-city-east-exit-3d-model.py", "version": VERSION,
            "design_id": "city_civilian_east_exit_v1", "signature": digest,
            "export_settings": EXPORT_SETTINGS,
            "units": "Unity local metres", "source_to_unity": "swap Y/Z and rewind faces",
            "root_scale_contract": "Preserve imported template lossyScale; FBX uses file units",
            "colliders": False, "lights": False, "animation_count": 0,
            "road_asphalt_width": 6, "road_shoulder_width": 8, "road_unit_length": 10,
            "barrier_clearance_below_grille": .095,
            "anchors": {"Lamp/LightAnchor": [.32, 3.335, 0]},
            "assemblies": records,
            "triangle_count": sum(p["triangle_count"] for r in records for p in r["parts"])}


def validate(assemblies, manifest):
    if len(assemblies) != 9 or manifest["triangle_count"] > 25000:
        raise ValueError("East-exit assembly or geometry budget mismatch")
    if manifest != manifest_for(make_assemblies()):
        raise ValueError("East-exit geometry is not deterministic")
    for group, roles in assemblies.items():
        for role, geometry in roles.items():
            if role not in COLORS or bp.signed_volume(bp.to_source(geometry)) <= 0:
                raise ValueError(f"Invalid role/handedness: {group}/{role}")
            if not all(math.isfinite(v) for vertex in geometry[0] for v in vertex):
                raise ValueError(f"Non-finite geometry: {group}/{role}")
    road = next(r for r in manifest["assemblies"] if r["name"] == "Road")
    if road["bounds_min_unity"][0] != -5 or road["bounds_max_unity"][2] != 4:
        raise ValueError("Road contract changed")
    asphalt = assemblies["Road"]["Asphalt"]
    original = box((0, -.075, 0), (10, .22, 6), .015)
    if kit.bounds(asphalt) != kit.bounds(original) or not math.isclose(
            bp.signed_volume(asphalt), bp.signed_volume(original), abs_tol=1e-9):
        raise ValueError("Road crown split changed the slab envelope/volume")
    vertices, faces = asphalt
    if any(min(vertices[i][2] for i in face) < -1e-9 and
           max(vertices[i][2] for i in face) > 1e-9 for face in faces):
        raise ValueError("Road slab has a face bridging both sides of the crown")
    # Sample actual face triangles after the same crossfall deformation as FitRoad.
    # The old uncut top lowered the centre by .018, despite an unchanged AABB.
    for x in (-4.8, 0, 4.8):
        for z in (-2.97, 0, 2.97):
            heights = []
            for face in faces:
                for j in range(1, len(face) - 1):
                    a, b, c = (vertices[i] for i in (face[0], face[j], face[j + 1]))
                    denominator = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
                    if abs(denominator) < 1e-10:
                        continue
                    u = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / denominator
                    v = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / denominator
                    w = 1 - u - v
                    if min(u, v, w) >= -1e-9:
                        heights.append(sum(weight * (point[1] - .006 * abs(point[2]))
                                           for weight, point in zip((u, v, w), (a, b, c))))
            if not heights or abs(max(heights) - (.035 - .006 * abs(z))) > 1e-9:
                raise ValueError(f"Road triangle crown/crossfall mismatch at {x}/{z}")


def dressing_manifest(assemblies):
    measured = manifest_for(assemblies)
    records = measured["assemblies"]
    for record in records:
        for part in record["parts"]:
            role = part["role"]
            part["mesh"] = f"EEX_{record['name']}_{PART_NAMES.get(role, role)}"
            part["role"] = PART_ROLES.get(role, role)
            part["surface_only"] = (record["name"], role) in SHEET_PARTS
    return {"generator": measured["generator"], "version": DRESSING_VERSION,
            "design_id": "city_east_exit_dressing_v1", "signature": measured["signature"],
            "export_settings": EXPORT_SETTINGS, "units": "Unity local metres",
            "source_to_unity": "swap Y/Z and rewind faces",
            "root_scale_contract": measured["root_scale_contract"],
            "colliders": False, "lights": False, "animation_count": 0,
            "surface_contract": "Runtime adds terrain sample to every authored vertex Y; preserve topology/UV",
            "anchors": {name + "/" + name + "LightAnchor": list(point) for name, point in DRESSING_LIGHT_ANCHORS.items()},
            "terrain_conform_assemblies": list(TERRAIN_CONFORM_ASSEMBLIES),
            "terrain_conform_max_local_y": {r["name"]: r["bounds_max_unity"][1] for r in records
                if r["name"] in TERRAIN_CONFORM_ASSEMBLIES},
            "shelter": {"back_edge": "+Z attaches to booth", "rigid_roof_height": 2.65,
                "front_post_xz": [[-1.55, -1.3], [1.55, -1.3]],
                "fit_posts": ["EEX_Shelter_Metal_PostL", "EEX_Shelter_Metal_PostR"],
                "fit_feet": ["EEX_Shelter_Stone_FootL", "EEX_Shelter_Stone_FootR"]},
            "assemblies": records, "triangle_count": measured["triangle_count"]}


def validate_dressing(assemblies, manifest):
    expected = {"Shelter", "Bench", "UtilityCabinet", "GravelPatch", "RoadRepair",
                "DryDrain", "GroundRidge", "DryGrass", "RepairedFence", "CanopyApron",
                "EarthBank", "DrainCrossing", "FenceFooting", "FenceToe", "LowShrub",
                "CanopyLamp", "ServiceWallLamp", "CreepingScrub", "BranchShrub",
                "MattedGrass", "TallWeeds", "GravelScatter", "DrainInspection", "RepairStock"}
    if set(assemblies) != expected or manifest["triangle_count"] > 16000:
        raise ValueError("Dressing assembly/budget contract mismatch")
    if manifest != dressing_manifest(make_dressing()):
        raise ValueError("Dressing geometry is not deterministic")
    for group, parts in assemblies.items():
        for part, geometry in parts.items():
            role = PART_ROLES.get(part, part)
            if role not in COLORS and role not in DRESSING_COLORS:
                raise ValueError(f"Unknown dressing material: {group}/{part}")
            if not all(math.isfinite(v) for point in geometry[0] for v in point):
                raise ValueError(f"Non-finite dressing geometry: {group}/{part}")
            if (group, part) in SHEET_PARTS:
                for face in geometry[1]:
                    a, b, c = (Vector(geometry[0][i]) for i in face[:3])
                    if (b - a).cross(c - a).y <= 1e-9:
                        raise ValueError(f"Inverted/degenerate ground sheet: {group}/{part}")
            elif bp.signed_volume(bp.to_source(geometry)) <= 0:
                raise ValueError(f"Inverted dressing solid: {group}/{part}")
    records = {r["name"]: r for r in manifest["assemblies"]}
    if records["Shelter"]["bounds_max_unity"][1] > 2.66 or records["Bench"]["bounds_min_unity"][1] != 0:
        raise ValueError("Shelter/bench ground or height contract changed")
    for part in records["Shelter"]["parts"]:
        if "_Post" in part["mesh"] or "_Foot" in part["mesh"]:
            if part["bounds_min_unity"][1] != 0:
                raise ValueError("Shelter fitted posts/feet must begin at Y=0")
    if records["EarthBank"]["bounds_max_unity"][1] > .60 or records["LowShrub"]["bounds_max_unity"][1] > .85:
        raise ValueError("Eastern landscape must remain low earth and open shrub groups")
    if records["DrainCrossing"]["bounds_max_unity"][1] > .065:
        raise ValueError("The shallow drain crossing must stay within walking height")
    for group, (size, floor) in LANDSCAPE_BOUNDS.items():
        low, high = records[group]["bounds_min_unity"], records[group]["bounds_max_unity"]
        if abs(low[1] - floor) > 1e-6 or any(abs(high[a] - low[a] - size[a]) > 1e-6 for a in range(3)):
            raise ValueError(f"Landscape grounding/metre envelope changed: {group}")
    for group in ("LowShrub", "CreepingScrub", "BranchShrub", "MattedGrass", "TallWeeds"):
        if "Ground" in assemblies[group] or "Dark" in assemblies[group]:
            raise ValueError(f"Vegetation cannot carry a broad ground pedestal: {group}")
    if records["DrainInspection"]["bounds_max_unity"][1] > .11:
        raise ValueError("The inspection grate must remain low and embedded")


def build_scene(assemblies, root_name="ROOT_CityEastExit3D"):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1
    root = bpy.data.objects.new(root_name, None)
    scene.collection.objects.link(root)
    groups = {}
    for group, roles in assemblies.items():
        assembly = bpy.data.objects.new(group, None)
        scene.collection.objects.link(assembly)
        assembly.parent = root
        groups[group] = assembly
        for role, geometry in roles.items():
            name = f"EEX_{group}_{PART_NAMES.get(role, role)}"
            material_role = PART_ROLES.get(role, role)
            mesh = bpy.data.meshes.new(name + "_Mesh")
            source = bp.to_source(geometry)
            mesh.from_pydata(source[0], [], source[1])
            mesh.update(calc_edges=True)
            obj = bpy.data.objects.new(name, mesh)
            scene.collection.objects.link(obj)
            obj.parent = assembly
            obj["role"] = material_role
            pitch = PITCH.get(material_role, DRESSING_PITCH.get(material_role))
            uv = mesh.uv_layers.new(name="UVMap")
            for polygon in mesh.polygons:
                axis = max(range(3), key=lambda i: abs(polygon.normal[i]))
                axes = ((1, 2), (0, 2), (0, 1))[axis]
                for loop in polygon.loop_indices:
                    point = mesh.vertices[mesh.loops[loop].vertex_index].co
                    uv.data[loop].uv = (point[axes[0]] / pitch, point[axes[1]] / pitch)
    if "Lamp" in groups:
        anchor = bpy.data.objects.new("LightAnchor", None)
        scene.collection.objects.link(anchor)
        anchor.parent = groups["Lamp"]
        anchor.location = (.32, 0, 3.335)
    for group, (x, y, z) in DRESSING_LIGHT_ANCHORS.items():
        if group not in groups:
            continue
        anchor = bpy.data.objects.new(group + "LightAnchor", None)
        scene.collection.objects.link(anchor)
        anchor.parent = groups[group]
        anchor.location = (x, z, y)
    return root, groups


def make_distance():
    return mainland.make_parts(kit, bp)


def distance_manifest(parts):
    records = []
    for name, geometry in parts.items():
        low, high = kit.bounds(geometry)
        uvs = [distance_uv(name, geometry, i) for i in range(len(geometry[0]))]
        records.append({"mesh": name, "role": name,
                        "vertex_count": len(geometry[0]), "triangle_count": kit.triangle_count(geometry),
                        "bounds_min_unity": [round(v, 6) for v in low],
                        "bounds_max_unity": [round(v, 6) for v in high],
                        "uv_min": [round(min(uv[a] for uv in uvs), 6) for a in range(2)],
                        "uv_max": [round(max(uv[a] for uv in uvs), 6) for a in range(2)],
                        "uv_signature": hashlib.sha256(json.dumps(uvs).encode()).hexdigest()})
    return {"generator": "tools/build-city-east-exit-3d-model.py", "version": mainland.VERSION,
            "design_id": "city_east_distance_v2", "parts": records,
            **mainland.metadata(),
            "export_settings": EXPORT_SETTINGS,
            "units": "virtual Unity metres, visual projection parameters only",
            "origin": "fixed anchor at former east road end; local +X outbound; Y=0 is checkpoint asphalt datum",
            "source_to_unity": "swap Y/Z and rewind faces",
            "colliders": False, "lights": False, "animation_count": 0,
            "triangle_count": sum(r["triangle_count"] for r in records),
            "signature": hashlib.sha256(json.dumps(parts, sort_keys=True,
                                                    separators=(",", ":")).encode()).hexdigest()}


def distance_uv(name, geometry, vertex_index):
    x, y, z = geometry[0][vertex_index]
    if name == "DistanceRoad":
        return max(0.0, min(1.0, (z - mainland.road_center(x) + 3) / 6)), mainland.route_at_x(x)[2]
    if name == "DistanceLampHalo":
        return ((0, 0), (1, 0), (1, 1), (0, 1))[vertex_index % 4]
    if name == "DistanceLampPool":
        _, center, arc = mainland.route_at_x(x)
        nearest = min((lamp for lamp in mainland.road_lamps() if not lamp["real"]),
                      key=lambda lamp: abs(lamp["distance"] - arc))
        return (max(0.0, min(1.0, (z - center + 2.7) / 5.4)),
                max(0.0, min(1.0, (arc - nearest["distance"] + 5) / 10)))
    if name == "DistanceCity":
        # Every source block is an authored eight-vertex box. Normalize its
        # own full height so low, ordinary buildings survive the same base haze
        # as the rare taller structures; a global /350 would erase most roofs.
        block = geometry[0][vertex_index // 8 * 8:vertex_index // 8 * 8 + 8]
        base, roof = min(v[1] for v in block), max(v[1] for v in block)
        return (z / 7000 + .5, (y - base) / max(.01, roof - base))
    if name == "DistanceWindows":
        return z / 7000 + .5, 1.0
    if name == "DistanceGlow":
        return z / 9000 + .5, float(vertex_index % 2)
    return x / 12, z / 12


def export_distance(parts, path, hide_source=True):
    root = bpy.data.objects.new("ROOT_CityEastDistance3D", None)
    bpy.context.scene.collection.objects.link(root)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for name, geometry in parts.items():
        source = bp.to_source(geometry)
        mesh = bpy.data.meshes.new(name + "_Mesh")
        mesh.from_pydata(source[0], [], source[1])
        mesh.update(calc_edges=True)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        obj["role"] = name
        obj["template_only"] = name.startswith("DistanceTraffic")
        uv = mesh.uv_layers.new(name="UVMap")
        # Buildings use base-to-roof UV; road uses normalized lateral/arc
        # metres, and each lamp halo/pavement pool owns one complete 0..1 UV.
        for polygon in mesh.polygons:
            for loop in polygon.loop_indices:
                vertex_index = mesh.loops[loop].vertex_index
                uv.data[loop].uv = distance_uv(name, geometry, vertex_index)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options=EXPORT_SETTINGS["apply_scale_options"],
        bake_space_transform=EXPORT_SETTINGS["bake_space_transform"], add_leaf_bones=False,
        bake_anim=False, use_mesh_modifiers=True, mesh_smooth_type="FACE", use_custom_props=True)
    # The combined historical source hides the panorama beside the near kit;
    # its dedicated source stays inspectable without unhiding every mesh.
    root.hide_viewport = hide_source
    for child in root.children:
        child.hide_render = hide_source or child.get("template_only", False)
        child.hide_viewport = hide_source or child.get("template_only", False)


def run_distance(args):
    parts = make_distance()
    mainland.validate(parts, kit, bp)
    manifest = distance_manifest(parts)
    if manifest != distance_manifest(make_distance()):
        raise ValueError("Mainland geometry/route determinism failed")
    path = args.model_dir / "CityEastDistance3D.json"
    if args.validate_only:
        if not path.is_file() or json.loads(path.read_text(encoding="utf-8")) != manifest:
            raise ValueError("Checked-in mainland manifest differs from deterministic geometry/route")
        print(f"CITY EAST DISTANCE VALIDATION OK: {manifest['triangle_count']} triangles; {manifest['signature']}")
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1
    export_distance(parts, args.model_dir / "CityEastDistance3D.fbx", hide_source=False)
    path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityEastDistance3D.blend"), check_existing=False)
    print(f"CITY EAST DISTANCE BUILD OK: {manifest['triangle_count']} triangles; {manifest['signature']}")


def preview(groups, output):
    scene = bpy.context.scene
    materials = {}
    for role, color in {**COLORS, **DRESSING_COLORS}.items():
        material = bpy.data.materials.new("PREVIEW_EEX_" + role)
        material.diffuse_color = color
        material.use_nodes = True
        shader = material.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Roughness"].default_value = .84
        if role == "Glow":
            shader.inputs["Emission Color"].default_value = color
            shader.inputs["Emission Strength"].default_value = 2
        materials[role] = material
    # Source meshes remain at their contract origins in the .blend. A disposable
    # presentation collection instantiates actual modules in one readable setting.
    stage = bpy.data.collections.new("PRESENTATION_EastExit")
    scene.collection.children.link(stage)
    dressing = "Shelter" in groups
    if dressing:
        placements = [("Shelter", (0, 0, 1.1), 0), ("Bench", (0, 0, 1.6), 0),
            ("UtilityCabinet", (2.8, 0, 1.2), -12), ("GravelPatch", (-.2, 0, -.7), 3),
            ("RoadRepair", (-4.0, 0, -2.3), 12), ("DryDrain", (1.0, 0, -3.3), 0),
            ("RepairedFence", (3.8, 0, 2.0), 0), ("GroundRidge", (5.1, 0, 2.0), 67),
            ("DryGrass", (4.4, 0, .6), 35), ("DryGrass", (5.7, 0, 3), -17),
            ("LowShrub", (-3.9, 0, 2.9), 23), ("BranchShrub", (4.9, 0, -1), 46),
            ("CreepingScrub", (4.4, 0, -3.3), -19), ("MattedGrass", (-2.9, 0, .2), 11),
            ("TallWeeds", (-4.8, 0, -.8), -34), ("GravelScatter", (1.3, 0, -1.5), 14),
            ("DrainInspection", (1, 0, -3.3), 0), ("RepairStock", (1.7, 0, 3.3), 0)]
    else:
        placements = [("Road", (x, 0, 0), 0) for x in (-15, -5, 5, 15)]
        placements += [("Booth", (-2, 0, -5.5), 0), ("Barrier", (0, .035, 0), 0),
                       ("Gate", (0, 0, -3.80), 0), ("Fence", (0, 0, -6.5), 0),
                       ("Fence", (0, 0, 5), 0), ("Fence", (0, 0, 9), 0),
                       ("Mound", (6, 0, 7), 21), ("Mound", (9, 0, -6), -16),
                       ("DrainCover", (-7, .004, 0), 0), ("Lamp", (-1, 0, -3.7), 0)]
    for group, position, yaw in placements:
        for child in groups[group].children:
            if child.type != "MESH":
                continue
            obj = child.copy()
            obj.data = child.data.copy()
            obj.parent = None
            obj.data.materials.append(materials[child["role"]])
            obj.location = (position[0], position[2], position[1])
            obj.rotation_euler.z = -math.radians(yaw)
            stage.objects.link(obj)
    for group in groups.values():
        for child in group.children:
            child.hide_render = True
    ground_geometry = bp.to_source(box((0, -.10 if dressing else -.19, 0), (100, .20, 100)))
    ground_mesh = bpy.data.meshes.new("PREVIEW_Ground_Mesh")
    ground_mesh.from_pydata(ground_geometry[0], [], ground_geometry[1])
    ground = bpy.data.objects.new("PREVIEW_Ground", ground_mesh)
    stage.objects.link(ground)
    ground.data.materials.append(materials["Ground"])
    world = scene.world or bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (.22, .28, .30, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = .6
    scene.world = world
    light_data = bpy.data.lights.new("PREVIEW_Sun", "SUN")
    light_data.energy = 2.0
    light = bpy.data.objects.new("PREVIEW_Sun", light_data)
    stage.objects.link(light)
    light.rotation_euler = (.48, -.62, -.40)
    camera_data = bpy.data.cameras.new("PREVIEW_Camera")
    camera = bpy.data.objects.new("PREVIEW_Camera", camera_data)
    stage.objects.link(camera)
    camera.location = (-11, -16, 9) if dressing else (-15, -20, 10)
    camera.rotation_euler = (Vector((.5, -.2, .8) if dressing else (-.5, -.6, 1.0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 14 if dressing else 23
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE_NEXT" if bpy.app.version < (4, 2, 0) else "CYCLES"
    if scene.render.engine == "CYCLES":
        scene.cycles.samples = 16
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)
    for group in groups.values():
        for child in group.children:
            child.hide_render = False
    stage.hide_render = True
    stage.hide_viewport = True


def run_dressing(args):
    assemblies = make_dressing()
    manifest = dressing_manifest(assemblies)
    validate_dressing(assemblies, manifest)
    manifest_path = args.model_dir / "CityEastExitDressing3D.json"
    if args.preview_only:
        root, groups = build_scene(assemblies, "ROOT_CityEastExitDressing3D")
        preview(groups, args.source_dir / "CityEastExitDressing3D.png")
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityEastExitDressing3D.blend"), check_existing=False)
        return
    if args.validate_only:
        if not manifest_path.is_file() or json.loads(manifest_path.read_text(encoding="utf-8")) != manifest:
            raise ValueError("Checked-in dressing manifest differs from measured deterministic geometry")
        print(f"CITY EAST DRESSING VALIDATION OK: {manifest['triangle_count']} triangles; {manifest['signature']}")
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    root, groups = build_scene(assemblies, "ROOT_CityEastExitDressing3D")
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / "CityEastExitDressing3D.fbx"),
        use_selection=True, object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options=EXPORT_SETTINGS["apply_scale_options"],
        bake_space_transform=EXPORT_SETTINGS["bake_space_transform"], add_leaf_bones=False,
        bake_anim=False, use_mesh_modifiers=True, mesh_smooth_type="FACE", use_custom_props=True)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    # The established direct-hierarchy import settings are also needed before
    # the first Unity import, especially isReadable for terrain conformity.
    for extension in ("fbx", "json"):
        meta_path = args.model_dir / ("CityEastExitDressing3D." + extension + ".meta")
        if meta_path.exists():
            continue
        template = (MODEL_DIR / ("CityEastExit3D." + extension + ".meta")).read_text(encoding="utf-8")
        guid = hashlib.md5(("City/EastExit/CityEastExitDressing3D." + extension).encode()).hexdigest()
        lines = ["guid: " + guid if line.startswith("guid:") else line for line in template.splitlines()]
        meta_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    if not args.no_preview:
        preview(groups, args.source_dir / "CityEastExitDressing3D.png")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityEastExitDressing3D.blend"), check_existing=False)
    print(f"CITY EAST DRESSING BUILD OK: {manifest['triangle_count']} triangles; {manifest['signature']}")
    for record in manifest["assemblies"]:
        print(record["name"], record["bounds_min_unity"], record["bounds_max_unity"], flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--preview-only", action="store_true",
                        help="Render the separate dressing source without republishing model files")
    parser.add_argument("--dressing-only", action="store_true",
                        help="Build/validate only the separate passive dressing asset; preserve near/far kit files")
    parser.add_argument("--distance-only", action="store_true",
                        help="Build/validate only mainland relief/road/skyline/traffic templates; preserve near/dressing")
    parser.add_argument("--near-only", action="store_true",
                        help="Export the near kit and measured manifests; preserve distance/dressing model binaries")
    args = parser.parse_args(argv)
    # Blender resolves relative render/save paths against its own file context,
    # unlike Python's file writes. Pin every output to the invocation workspace.
    args.model_dir = args.model_dir.resolve()
    args.source_dir = args.source_dir.resolve()
    if sum((args.dressing_only, args.distance_only, args.near_only)) > 1:
        parser.error("--dressing-only, --distance-only and --near-only are mutually exclusive")
    if args.distance_only:
        if args.preview_only:
            parser.error("--preview-only requires --dressing-only")
        return run_distance(args)
    if args.dressing_only:
        return run_dressing(args)
    if args.preview_only:
        parser.error("--preview-only requires --dressing-only")
    assemblies = make_assemblies()
    manifest = manifest_for(assemblies)
    validate(assemblies, manifest)
    manifest_path = args.model_dir / "CityEastExit3D.json"
    distance = make_distance()
    mainland.validate(distance, kit, bp)
    distance_payload = distance_manifest(distance)
    if distance_payload != distance_manifest(make_distance()) or distance_payload["triangle_count"] > 20000:
        raise ValueError("Distance geometry determinism/budget failed")
    distance_manifest_path = args.model_dir / "CityEastDistance3D.json"
    if args.validate_only:
        if not manifest_path.is_file() or json.loads(manifest_path.read_text(encoding="utf-8")) != manifest:
            raise ValueError("Checked-in manifest differs from measured deterministic geometry")
        if not distance_manifest_path.is_file() or json.loads(distance_manifest_path.read_text(encoding="utf-8")) != distance_payload:
            raise ValueError("Checked-in distance manifest differs from measured deterministic geometry")
        print(f"CITY EAST EXIT VALIDATION OK: {manifest['triangle_count']} triangles; {manifest['signature']}")
        print(f"CITY EAST DISTANCE VALIDATION OK: {distance_payload['triangle_count']} triangles; {distance_payload['signature']}")
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    root, groups = build_scene(assemblies)
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir / "CityEastExit3D.fbx"),
        use_selection=True, object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
        apply_scale_options=EXPORT_SETTINGS["apply_scale_options"],
        bake_space_transform=EXPORT_SETTINGS["bake_space_transform"], add_leaf_bones=False,
        bake_anim=False, use_mesh_modifiers=True, mesh_smooth_type="FACE", use_custom_props=True)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if not args.no_preview:
        preview(groups, args.source_dir / "CityEastExit3D.png")
    if not args.near_only:
        export_distance(distance, args.model_dir / "CityEastDistance3D.fbx")
    distance_manifest_path.write_text(json.dumps(distance_payload, indent=2) + "\n", encoding="utf-8")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityEastExit3D.blend"), check_existing=False)
    print(f"CITY EAST EXIT BUILD OK: {manifest['triangle_count']} triangles; {manifest['signature']}")


if __name__ == "__main__":
    main()
