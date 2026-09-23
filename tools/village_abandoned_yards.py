"""Passive, fixed-metre yards for the abandoned Alpine Village settlement.

Authored in Unity metres, +Z front, sharing the owning building's ground
origin. The emitter is build-village-expansion-3d-model.py's add callback.
Old household sled/gate/bench/cart proportions are adapted from the existing
VillageLife and CityMisc donors without changing those working assets.
No text, lamps, actions or loose physics: only outward closed authored solids.
"""
from __future__ import annotations

import math

import bar_parts as bp
import interior_kit as kit

WOOD = (.295, .285, .25, 1)
BLEACHED = (.405, .385, .33, 1)
IRON = (.265, .245, .215, 1)
STONE = (.43, .44, .415, 1)
PALE_STONE = (.51, .505, .465, 1)

# Conservative XZ shelves, including a walkable margin. These are LEVEL ground
# reservations, never snow-clear rectangles: snow should approach every door.
YARD_GROUND_BOUNDS = {
    "TownHallYard": (-10.5, 10.5, -8., 16.),
    "SchoolYard": (-10.5, 10.5, -7.5, 16.),
    "ShopBakeryYard": (-9.5, 9.5, -10., 11.),
    "WorkshopYard": (-7.5, 10.5, -7., 15.),
    "MountainRescueYard": (-7.5, 11., -8., 14.),
    "HouseholdYardA": (-7.5, 7.5, -5.5, 10.),
    "HouseholdYardB": (-7.5, 7.5, -5.5, 10.),
    "HouseholdYardC": (-7.5, 7.5, -5.5, 10.),
}


def box(p, s, bevel=.012):
    return bp.u_box(p, s, min(bevel, min(s) * .22))


def put(g, p=(0, 0, 0), rotation=(0, 0, 0)):
    return kit.translated(bp.u_rotated(g, rotation), p)


def merge(parts):
    parts = list(parts)
    for part in parts:
        assert bp.signed_volume(part) > 1e-10, "Inward yard constituent"
    return kit.merge_all(parts)


def beam(a, b, width=.075, depth=None):
    delta = tuple(b[i] - a[i] for i in range(3))
    length = math.sqrt(sum(v * v for v in delta))
    assert length > 1e-6
    pitch = math.degrees(math.acos(max(-1., min(1., delta[1] / length))))
    yaw = math.degrees(math.atan2(delta[0], delta[2]))
    return put(box((0, 0, 0), (width, length, depth or width), .008),
               tuple((a[i] + b[i]) * .5 for i in range(3)), (pitch, yaw, 0))


def cylinder(p, diameter, height, sides=10):
    return bp.u_cylinder(p, (diameter, height * .5, diameter), sides)


def snow(p, width, depth, height=.11):
    """One weathered cap; no white tile grids on the old horizontal surfaces."""
    g = bp.u_tapered_cylinder((0, height * .5, 0),
                             (width, height * .5, depth), .84, 10)
    vertices = []
    for i, (x, y, z) in enumerate(g[0]):
        factor = 1 - .032 * ((i % 10) * 7 % 4)
        vertices.append((x * factor + p[0], y + p[1], z * factor + p[2]))
    return vertices, g[1]


def emit(add, kind, name, parts, surface="Timber", solid=True, tint=None):
    add(kind, name, merge(parts), surface, solid, tint)


def flagstones(x0, z0, columns, rows, pitch=1.1, top=.17):
    """Several exposed islands, with deterministic missing and sunken slabs."""
    result = []
    for row in range(rows):
        for column in range(columns):
            if (column * 3 + row * 5) % 8 in (0, 1, 2):
                continue
            x = x0 + column * pitch + (row % 2) * .13
            z = z0 + row * pitch
            h = top - .025 * ((row + column) % 3)
            result.append(put(box((0, h * .5, 0),
                                  (pitch * .91, h, pitch * .89), .04),
                              (x, 0, z), (0, ((row * 3 + column) % 5) - 2, 0)))
    return result


def bench(length=2.4, broken=False):
    """Stone feet outlast the replaceable slatted seat and back."""
    feet = [box((x, .26, 0), (.30, .52, .61), .045)
            for x in (-length * .36, length * .36)]
    timber = []
    for z in (-.18, 0, .18):
        run = length * (.39 if broken and z != 0 else 1)
        if broken and z == 0:
            continue
        timber.append(box((-length * .29 if broken else 0, .55, z),
                          (run, .075, .158), .014))
    if not broken:
        for x in (-length * .36, length * .36):
            timber.append(beam((x, .34, -.22), (x, 1.02, -.33), .09))
        timber += [box((0, y, -.315), (length, .16, .062), .014)
                   for y in (.78, .97)]
    return feet, timber


def fence_segment(length, missing=(), fallen=False):
    timber = [box((x, .64, 0), (.12, 1.28, .15), .018)
              for x in (-length * .5, length * .5)]
    for y in (.40, .93):
        timber.append(box((0, y, -.03), (length, .10, .09), .012))
    count = max(2, round(length / .24))
    for i in range(count):
        if i in missing:
            continue
        h = .95 - (i % 4) * .018
        timber.append(box((-length * .5 + .13 + i * (length - .26) / (count - 1),
                           .22 + h * .5, .035), (.15, h, .044), .008))
    if fallen:
        return [put(merge(timber), (0, .08, 0), (77, 0, 0))]
    return timber


def gate_leaf(width=2.7):
    timber = [box((width * .5, y, 0), (width, .12, .10)) for y in (.37, 1.13)]
    timber += [box((.12 + i * (width - .24) / 8, .81, .055), (.16, 1.35, .07))
               for i in range(9) if i != 6]
    timber.append(beam((.07, .38, -.075), (width - .08, 1.13, -.075), .095, .055))
    return timber


def cart():
    """Empty, collapsed-side adaptation of the City's old mason cart."""
    wood = [box((-.40 + i * .16, .68, 0), (.145, .09, 1.42)) for i in range(6)]
    wood += [box((x, .82, .08), (.08, .23, 1.22)) for x in (-.51, .51)]
    wood += [beam((x, .64, -.63), (x, .26, -1.47), .07) for x in (-.41, .41)]
    metal = [beam((-.65, .36, .25), (.65, .36, .25), .08),
             box((0, .57, .25), (1.12, .12, .14))]
    for x in (-.62, .62):
        wheel = put(cylinder((0, 0, 0), .67, .075, 12), (x, .355, .25), (0, 0, 90))
        metal.append(wheel)
        metal.append(put(cylinder((0, 0, 0), .16, .098, 8), (x, .355, .25), (0, 0, 90)))
    return wood, metal


def empty_tray():
    tray = [box((x, .042, 0), (.10, .065, .72), .006) for x in (-.24, -.12, 0, .12, .24)]
    tray += [box((x, .14, 0), (.045, .22, .79), .007) for x in (-.315, .315)]
    tray += [box((0, .14, z), (.59, .22, .045), .007) for z in (-.37, .37)]
    return merge(tray)


def sled():
    """VillageLife utility-sled proportions, one missing deck board/runner tip."""
    wood, iron = [], []
    for x in (-.29, .29):
        iron.append(beam((x, .055, -.63), (x, .055, .52), .045, .08))
        if x > 0:
            iron.append(beam((x, .055, .52), (x, .19, .72), .045, .07))
        for z in (-.42, .35):
            wood.append(beam((x, .095, z), (x, .27, z), .055))
    for x in (-.27, .09, .27):
        wood.append(box((x, .284, -.05), (.16, .045, 1.04)))
    wood += [box((0, .23, z), (.70, .075, .06)) for z in (-.43, .34)]
    return wood, iron


def town_hall(add):
    kind = "TownHallYard"
    emit(add, kind, "OldPlazaBed", [box((0, .045, 10.0), (18.7, .09, 10.2), .04)], "LayeredStone", False, STONE)
    emit(add, kind, "ExposedPaving", flagstones(-8.25, 5.6, 16, 9, 1.03), "Masonry", False, PALE_STONE)
    walls = [box((x, .36, 10.1), (.42, .72, 8.8), .05) for x in (-9.52, 9.52)]
    walls += [box((x, .26, 14.9), (5.5, .52, .48), .05) for x in (-6.48, 6.48)]
    emit(add, kind, "RetainingEdges", walls, "LayeredStone", True, STONE)
    feet, boards = [], []
    for p, yaw, broken in (((-8.2, 0, 9.3), 90, False), ((8.2, 0, 9.6), -90, False),
                           ((-5.5, 0, 13.9), 0, True)):
        f, b = bench(2.7, broken)
        feet += [put(g, p, (0, yaw, 0)) for g in f]
        boards += [put(g, p, (0, yaw, 0)) for g in b]
    emit(add, kind, "BenchStoneFeet", feet, "Masonry", True, PALE_STONE)
    emit(add, kind, "WeatheredBenchSlats", boards, "Timber", True, BLEACHED)
    posts, panes = [], []
    for x in (-3.7, 3.7):
        posts += [box((x, .10, 6.35), (.39, .20, .39), .035),
                  cylinder((x, 1.41, 6.35), .105, 2.62),
                  box((x, 2.72, 6.35), (.36, .09, .36)),
                  box((x, 3.15, 6.35), (.47, .10, .47), .035)]
        posts += [box((x + dx, 2.94, 6.35 + dz), (.04, .40, .04), .005)
                  for dx in (-.15, .15) for dz in (-.15, .15)]
        panes.append(box((x, 2.93, 6.35), (.27, .34, .27), .008))
    emit(add, kind, "DeadLanternFrames", posts, "RustedIron", True, IRON)
    emit(add, kind, "UnlitLanternInsets", panes, "RustedIron", False, (.20, .235, .235, 1))
    emit(add, kind, "RearAnnexFoot", [box((4.8, .24, -6.4), (4.1, .48, 2.5), .05),
        box((6.65, .57, -6.4), (.37, .67, 2.5), .045)], "LayeredStone", True, STONE)
    emit(add, kind, "AnnexFallenTimbers", [beam((3.0, .55, -5.45), (6.0, .66, -7.4), .17),
        beam((4.0, .53, -7.25), (6.2, .78, -5.4), .14),
        box((6.4, 1.07, -5.38), (.17, 1.5, .18))], "Timber", True, WOOD)
    emit(add, kind, "SettledLooseBoards", [put(box((0, 0, 0), (1.27, .065, .20)),
        (3.9 + i * .32, .54 + (i % 2) * .06, -6.5 + (i % 3) * .23),
        (0, 22 + i * 11, 3)) for i in range(6)], "Timber", False, BLEACHED)
    emit(add, kind, "OldHardware", [box((x, .6, 14.96), (.12, .15, .045))
        for x in (-8.3, -4.6, 4.6, 8.3)], "RustedIron", False, IRON)
    emit(add, kind, "SettledSnow", [snow((-5.45, .60, 13.9), 2.4, .52),
        snow((4.9, .79, -6.1), 2.9, 1.2), snow((9.51, .725, 9.7), .40, 3.3),
        snow((-9.52, .725, 12.2), .40, 3.5)], "WindSnow", False)


def school(add):
    kind = "SchoolYard"
    walls = [box((x, .22, 14.7), (6.9, .44, .42), .045) for x in (-5.3, 5.3)]
    walls += [box((x, .20, 10.2), (.42, .40, 8.6), .045) for x in (-8.94, 8.94)]
    emit(add, kind, "LowCourtyardWalls", walls, "LayeredStone", True, STONE)
    emit(add, kind, "GateStonePiers", [box((x, .78, 14.7), (.54, 1.56, .54), .05)
        for x in (-1.5, 1.5)], "Masonry", True, PALE_STONE)
    fence = []
    for p, yaw, length, lost in (((-6.2, .40, 14.7), 0, 4.9, (3, 4, 11)),
                                ((8.94, .40, 10.7), 90, 5.0, (2, 3, 12, 15)),
                                ((-8.94, .40, 7.5), 90, 2.6, (4, 5))):
        fence += [put(g, p, (0, yaw, 0)) for g in fence_segment(length, lost)]
    emit(add, kind, "BrokenBoundaryFence", fence, "Timber", True, WOOD)
    emit(add, kind, "CrookedOpenGate", [put(g, (-1.50, .025, 14.5), (0, 69, -4))
        for g in gate_leaf(2.3)], "Timber", True, BLEACHED)
    frame = [beam((-7.1, .07, z), (-5.4, 2.64, z), .105) for z in (8.9, 11.1)]
    frame += [beam((-3.7, .07, z), (-5.4, 2.64, z), .105) for z in (8.9, 11.1)]
    frame += [beam((-5.4, 2.64, 8.78), (-5.4, 2.64, 11.22), .125),
              beam((-6.7, .72, 8.9), (-4.1, .72, 8.9), .055),
              beam((-6.7, .72, 11.1), (-4.1, .72, 11.1), .055)]
    emit(add, kind, "OldSwingFrame", frame, "RustedIron", True, IRON)
    emit(add, kind, "BrokenSwingSuspension", [beam((-5.4, 2.56, 9.4), (-5.65, .61, 9.45), .021),
        beam((-5.4, 2.56, 10.5), (-5.35, 2.03, 10.46), .021)], "RustedIron", False, IRON)
    emit(add, kind, "TiltedSwingSeat", [put(box((0, 0, 0), (.42, .07, .80)),
        (-5.62, .45, 9.82), (27, 0, -4))], "Timber", False, BLEACHED)
    bars = [cylinder((x, .53, z), .067, 1.06) for x in (3.9, 5.2) for z in (8.6, 11.0)]
    bars += [beam((x, 1.03, 8.6), (x, 1.03, 11.0), .06) for x in (3.9, 5.2)]
    emit(add, kind, "LowExerciseBars", bars, "RustedIron", True, IRON)
    f, b = bench(3.3)
    emit(add, kind, "LongBenchStone", [put(g, (3.9, 0, 13.4)) for g in f], "Masonry", True, STONE)
    emit(add, kind, "LongBenchTimber", [put(g, (3.9, 0, 13.4)) for g in b], "Timber", True, BLEACHED)
    emit(add, kind, "RearShedRemains", [box((4.2, .24, -5.55), (3.6, .48, 2.3), .045),
        box((5.85, .63, -5.55), (.31, .78, 2.3), .03)], "LayeredStone", True, STONE)
    emit(add, kind, "ShedRoofFallen", [beam((2.8, .56, -4.55), (5.4, .64, -6.45), .14),
        beam((3.2, .60, -6.35), (5.6, .92, -4.65), .17)] +
        [put(box((0, 0, 0), (.22, .06, 1.5)), (3.3 + i * .30, .58 + (i % 2) * .05, -5.6),
             (0, -21, 6)) for i in range(6)], "Timber", False, WOOD)
    emit(add, kind, "EntrancePavingRemnants", flagstones(-1.02, 6.0, 3, 4, 1.0), "Masonry", False, PALE_STONE)
    emit(add, kind, "SnowAtIdleFixtures", [snow((3.9, .595, 13.4), 3.1, .51),
        snow((4.4, .86, -5.5), 2.8, 1.5), snow((-5.65, .55, 9.9), .36, .7)], "WindSnow", False)


def shop_bakery(add):
    kind = "ShopBakeryYard"
    emit(add, kind, "TradingPaving", flagstones(-5.15, 5.25, 11, 5, 1.0), "Masonry", False, PALE_STONE)
    emit(add, kind, "RearLoadingSlab", [box((-1.2, .20, -6.15), (8.0, .40, 3.2), .055)],
         "LayeredStone", True, STONE)
    canopy = [box((x, 1.38, 8.52), (.17, 2.76, .18), .018) for x in (-4.75, -.20)]
    canopy += [box((-2.46, 2.70, 8.52), (4.85, .20, .19)),
               box((0, 2.93, 4.68), (10.0, .17, .17))]
    canopy += [beam((x, 2.02, 8.52), (x, 2.79, 7.68), .115) for x in (-4.75, -.20)]
    canopy += [beam((x, 2.91, 4.68), (x, 2.73, 8.65), .14) for x in (-4.78, -2.52, -.19)]
    emit(add, kind, "SurvivingCanopyFrame", canopy, "Timber", True, WOOD)
    emit(add, kind, "CanopyWeatheredBoards", [put(box((0, 0, 0), (.31, .065, 4.1)),
        (-4.65 + i * .31, 2.90, 6.66), (2.6, 0, 0)) for i in range(15)], "Timber", True, BLEACHED)
    iron = [beam((x, 2.19, 4.65), (x, 2.85, 5.44), .045) for x in (-4.8, -2.5, 0, 2.5, 4.8)]
    iron += [box((x, 2.88, 4.70), (.15, .06, .42)) for x in (-4.8, -2.5, 0, 2.5, 4.8)]
    emit(add, kind, "SurvivingIronBrackets", iron, "RustedIron", False, IRON)
    emit(add, kind, "CollapsedCanopyEnd", [beam((4.75, .10, 8.5), (3.83, 1.18, 7.55), .17),
        beam((2.0, .11, 7.4), (4.1, .30, 8.8), .15)] +
        [put(box((0, 0, 0), (.30, .06, 1.65)), (3.25 + i * .24, .11 + (i % 2) * .035, 8.05),
             (0, -32, 2)) for i in range(5)], "Timber", False, WOOD)
    wood, iron = cart()
    emit(add, kind, "AbandonedBreadCartTimber", [put(g, (-3.6, .405, -6.65), (0, -12, 0)) for g in wood],
         "Timber", True, BLEACHED)
    emit(add, kind, "CartAxleAndWheels", [put(g, (-3.6, .405, -6.65), (0, -12, 0)) for g in iron],
         "RustedIron", True, IRON)
    emit(add, kind, "EmptyBreadTrays", [put(empty_tray(), (-.9 + i * .14, .41 + i * .19, -5.8),
        (0, i * 6, 0)) for i in range(3)], "Timber", False, BLEACHED)
    emit(add, kind, "WoodstoreFoot", [box((7.3, .18, -4.95), (2.9, .36, 3.5), .05)],
         "LayeredStone", True, STONE)
    woodstore = [box((x, 1.22, -3.4), (.13, 2.10, .14)) for x in (6.05, 8.55)]
    woodstore += [box((7.3, 2.18, -3.4), (2.75, .16, .15)),
                  beam((6.05, 2.13, -3.4), (6.05, .45, -6.3), .14),
                  beam((6.05, .43, -6.3), (8.4, .68, -6.0), .12)]
    woodstore += [put(box((0, 0, 0), (.30, .055, 2.8)), (6.2 + i * .34, .62 + i * .03, -5.0),
                      (8, 0, -4)) for i in range(7)]
    emit(add, kind, "CollapsedWoodstore", woodstore, "Timber", True, WOOD)
    logs = [put(cylinder((0, 0, 0), .14, .74, 8), (6.7 + (i % 3) * .21, .45 + (i // 3) * .12, -3.8),
                (0, 0, 90)) for i in range(7)]
    emit(add, kind, "RemainingDryWood", logs, "Timber", False, BLEACHED)
    emit(add, kind, "CanopyAndWoodstoreSnow", [put(snow((0, 0, 0), 4.55, 3.95, .16),
        (-2.4, 2.965, 6.64), (2.6, 0, 0)), snow((7.3, .83, -5.05), 2.0, 2.4),
        snow((-3.65, 1.125, -6.5), .80, 1.13)], "WindSnow", False)


def workshop(add):
    kind = "WorkshopYard"
    emit(add, kind, "OldWorkPaving", flagstones(-4.9, 5.0, 10, 8, 1.1, .20), "LayeredStone", False, STONE)
    emit(add, kind, "EntranceStonePosts", [box((x, .92, 13.55), (.60, 1.84, .62), .05)
        for x in (-4.7, 4.7)], "Masonry", True, PALE_STONE)
    gates = [put(g, (-4.65, .10, 13.55), (0, 71, -3)) for g in gate_leaf(3.45)]
    gates += [put(g, (5.02, .11, 10.0), (73, -90, 0)) for g in gate_leaf(3.45)]
    emit(add, kind, "CrookedAndFallenGates", gates, "Timber", True, WOOD)
    posts = [box((9.25, 1.40, z), (.19, 2.80, .20)) for z in (-1.6, 1.4, 4.4)]
    posts += [beam((5.07, 2.93, z), (9.30, 2.73, z), .19) for z in (1.4, 4.4)]
    posts += [box((9.25, 2.73, 2.9), (.19, .20, 3.25)),
              beam((9.25, 1.95, 4.4), (8.45, 2.77, 4.4), .12),
              beam((9.25, 1.94, 1.4), (9.25, 2.70, 2.22), .12)]
    emit(add, kind, "HalfCanopyStructure", posts, "Timber", True, WOOD)
    emit(add, kind, "HalfCanopyRoof", [put(box((0, 0, 0), (4.5, .07, .34)),
        (7.18, 2.96, 1.46 + i * .34), (0, 0, -2.7)) for i in range(9)], "Timber", True, BLEACHED)
    collapsed = [beam((5.15, 2.94, -1.6), (8.5, .27, -1.6), .17),
                 beam((8.45, .16, -2.0), (9.13, .47, 1.01), .19)]
    collapsed += [put(box((0, 0, 0), (.29, .07, 2.6)),
                      (6.4 + i * .34, .14 + (i % 3) * .05, -1.1), (0, 14, -3)) for i in range(7)]
    emit(add, kind, "FallenRoofAtRear", collapsed, "Timber", False, WOOD)
    workbench = [box((7.13, .93, 2.9), (2.95, .17, .86), .03)]
    for x in (5.88, 8.38):
        for z in (2.57, 3.23):
            workbench.append(beam((x, .07, z), (x, .85, z), .13))
    workbench += [box((7.13, .31, z), (2.7, .12, .085)) for z in (2.57, 3.23)]
    emit(add, kind, "HeavyBench", workbench, "Timber", True, BLEACHED)
    vise = [box((6.18, 1.08, 3.28), (.37, .22, .32)),
            box((6.18, 1.23, 3.24), (.45, .10, .105)),
            box((6.18, 1.20, 3.48), (.42, .16, .075)),
            beam((6.18, 1.15, 3.27), (6.18, 1.15, 3.72), .055),
            beam((6.18, .98, 3.74), (6.18, 1.39, 3.74), .033)]
    emit(add, kind, "RustingBenchVise", vise, "RustedIron", False, IRON)
    sawhorses = []
    for x in (7.0, 8.6):
        for z in (.22, 1.05):
            sawhorses += [beam((x - .31, .055, z), (x, .77, z), .09),
                          beam((x + .31, .055, z), (x, .77, z), .09)]
        sawhorses.append(box((x, .80, .64), (.14, .15, 1.05)))
    emit(add, kind, "IdleSawhorses", sawhorses, "Timber", True, WOOD)
    racks = [box((9.1, .68, z), (.10, 1.36, .12)) for z in (2.0, 3.7)]
    racks += [beam((9.10, .6, z), (8.60, .6, z), .07) for z in (2.0, 3.7)]
    racks += [beam((8.75 + i * .11, .68 + (i % 2) * .08, 1.7),
                   (8.78 + i * .11, .70 + (i % 2) * .08, 4.02), .07) for i in range(3)]
    emit(add, kind, "LongStockOnRack", racks, "Timber", True, WOOD)
    runners = [beam((6.28 + i * .25, .08, 4.05), (7.71 + i * .25, .14, 4.05), .065, .11)
               for i in range(2)]
    runners += [beam((7.71 + i * .25, .14, 4.05), (7.94 + i * .25, .31, 4.05), .06, .09)
                for i in range(2)]
    emit(add, kind, "OldSledRunners", runners, "RustedIron", False, IRON)
    emit(add, kind, "FootingsAndGateHinges", [box((9.25, .09, z), (.37, .18, .38))
        for z in (-1.6, 1.4, 4.4)], "LayeredStone", True, STONE)
    emit(add, kind, "CanopySnow", [put(snow((0, 0, 0), 4.25, 2.9, .14),
        (7.16, 3.01, 2.85), (0, 0, -2.7)), snow((7.2, .38, -1.0), 2.2, 1.8)], "WindSnow", False)


def rescue_sledge():
    """Thick, open evacuation shell with raised prow and a closed floor."""
    # XZ contour traverses the same clockwise-from-above order as u_cylinder.
    contour = [(-.24, -1.38, .55), (.24, -1.38, .55), (.46, -.96, .33),
               (.46, .89, .33), (.25, 1.37, .61), (-.25, 1.37, .61),
               (-.46, .89, .33), (-.46, -.96, .33)]
    contour.reverse()
    verts = []
    for role in range(4):
        for x, z, y in contour:
            inset = role >= 2
            verts.append((x * (.84 if inset else 1),
                          y + (.24 if role in (1, 2) else (.065 if inset else 0)),
                          z * (.965 if inset else 1)))
    count = len(contour)
    faces = [tuple(range(count)), tuple(reversed(range(3 * count, 4 * count)))]
    for ring in range(3):
        for i in range(count):
            j = (i + 1) % count
            faces.append((ring * count + j, ring * count + i,
                          (ring + 1) * count + i, (ring + 1) * count + j))
    result = verts, faces
    if bp.signed_volume(result) < 0:
        result = verts, [tuple(reversed(face)) for face in faces]
    return result


def mountain_rescue(add):
    kind = "MountainRescueYard"
    emit(add, kind, "OldForecourt", [box((0, .07, 8.2), (13.2, .14, 8.9), .055)],
         "LayeredStone", False, STONE)
    emit(add, kind, "ExposedForecourtEdges", [box((x, .16, 8.2), (.24, .32, 8.9), .035)
        for x in (-6.61, 6.61)] + flagstones(-4.8, 5.1, 9, 4, 1.15, .20), "Masonry", False, PALE_STONE)
    posts = [box((10.05, 1.37, z), (.18, 2.74, .18)) for z in (-3.15, 0, 3.15)]
    posts += [beam((6.08, 2.90, z), (10.15, 2.687, z), .17) for z in (-3.15, 0, 3.15)]
    posts += [box((10.05, 2.67, 0), (.18, .19, 6.45))]
    posts += [beam((10.05, 1.90, z), (9.26, 2.72, z), .11) for z in (-3.15, 3.15)]
    emit(add, kind, "SledgeShelterFrame", posts, "Timber", True, WOOD)
    roof = [put(box((0, 0, 0), (4.38, .065, .31)), (8.05, 2.92, -2.8 + i * .31),
                (0, 0, -3)) for i in range(19) if i not in (2, 3, 4)]
    emit(add, kind, "SledgeShelterRoof", roof, "Timber", True, BLEACHED)
    emit(add, kind, "SaggedRoofFragments", [beam((6.08, 2.99, -2.12), (9.2, 1.65, -2.12), .14),
        put(box((0, 0, 0), (2.8, .07, .43)), (8.12, 1.43, -2.28), (0, 0, -28))], "Timber", True, WOOD)
    # Ordinary faded rescue equipment; no badge, lettering or active vehicle.
    hull = put(rescue_sledge(), (8.35, .36, .47), (0, 7, 0))
    emit(add, kind, "WornEvacuationSledge", [hull], "RustedIron", True, (.48, .36, .22, 1))
    supports = [box((8.35, .30, z), (1.14, .60, .20)) for z in (-.34, 1.23)]
    emit(add, kind, "SledgeSupportBlocks", supports, "Timber", True, WOOD)
    metal = [beam((8.03, .63, -.80), (8.03, .63, 1.48), .06),
             beam((8.67, .63, -.80), (8.67, .72, .30), .06),
             beam((8.11, .89, 1.71), (8.02, 1.04, 2.82), .041),
             beam((8.58, .89, 1.65), (8.66, 1.01, 2.76), .041)]
    metal += [box((8.35, 1.015, z), (.76, .025, .075)) for z in (-.15, .88)]
    metal += [beam((x, .63, z), (x, .73, z), .065)
              for x in (8.03, 8.67) for z in (-.34, 1.23)]
    emit(add, kind, "DamagedSledgeRunnersAndHandles", metal, "RustedIron", False, IRON)
    rack = [box((6.20, y, .20), (.095, .12, 3.8)) for y in (.83, 1.89)]
    rack += [box((6.22, 1.31, z), (.10, 1.36, .11)) for z in (-1.5, 1.9)]
    emit(add, kind, "EmptyWallRack", rack, "Timber", True, WOOD)
    skis, hooks = [], []
    for i in range(3):
        z = -.85 + i * .29
        skis += [beam((6.58, .065, z), (6.33, 1.97, z), .13, .045),
                 beam((6.33, 1.97, z), (6.39, 2.18, z), .12, .042)]
        hooks.append(box((6.47, .79, z), (.08, .13, .15)))
    for z in (-1.35, -.30, .42, 1.12, 1.77):
        hooks += [beam((6.22, 1.93, z), (6.54, 1.93, z), .025),
                  beam((6.54, 1.93, z), (6.54, 1.99, z), .025)]
    hooks += [beam((6.60, .07, z), (6.30, 1.98, z), .026) for z in (.66, .87)]
    emit(add, kind, "RemainingOldSkis", skis, "Timber", False, (.39, .35, .285, 1))
    emit(add, kind, "EmptyHooksPolesAndBindings", hooks, "RustedIron", False, IRON)
    drying = [box((x, 1.18, -6.55), (.12, 2.36, .14)) for x in (.1, 4.7)]
    drying += [beam((.1, 2.33, -6.55), (4.7, 2.33, -6.55), .11),
               beam((.1, .75, -6.55), (.1, .06, -7.25), .085),
               beam((4.7, .75, -6.55), (4.7, .06, -7.25), .085)]
    emit(add, kind, "RearDryingFrame", drying, "Timber", True, WOOD)
    emit(add, kind, "ShelterFootings", [box((10.05, .105, z), (.37, .21, .37))
        for z in (-3.15, 0, 3.15)], "LayeredStone", True, STONE)
    emit(add, kind, "SnowInIdleEquipment", [put(snow((0, 0, 0), 3.95, 4.05, .15),
        (8.02, 2.98, .63), (0, 0, -3)), snow((8.36, .758, .44), .70, 1.5, .08),
        snow((2.4, 2.385, -6.55), 3.8, .13, .055)], "WindSnow", False)


def household(add, variant):
    kind = "HouseholdYard" + "ABC"[variant]
    side = -1 if variant == 1 else 1
    emit(add, kind, "OldYardStone", [box((side * 6.2, .23, 6.0), (.36, .46, 5.7), .05),
        box((-side * 4.85, .18, 8.5), (2.7, .36, .40), .04)], "LayeredStone", True, STONE)
    fencing = [put(g, (side * 6.2, .43, 6.0), (0, 90, 0))
               for g in fence_segment(4.8, (1, 4, 5, 9, 13))]
    emit(add, kind, "IncompleteFence", fencing, "Timber", True, WOOD)
    gate = [box((x, .68, 8.5), (.20, 1.36, .21)) for x in (-2.3, .8)]
    gate += [put(g, (-2.3, .06, 8.5), (0, 75, -3)) for g in gate_leaf(1.7)]
    emit(add, kind, "UnkeptOpenGate", gate, "Timber", True, BLEACHED)
    emit(add, kind, "PorchStoneRemains", [box((side * 4.95, .21, 2.8), (1.9, .42, 2.2), .045),
        box((side * 4.95, .10, 4.05), (1.95, .20, .65), .035)], "Masonry", True, PALE_STONE)
    porch = [beam((side * 4.1, .44, 2.1), (side * 5.6, .56, 3.6), .15)]
    porch += [put(box((0, 0, 0), (.23, .05, 1.50)), (side * (4.30 + i * .24), .465 + i * .012, 2.8),
                  (0, -8 + i * 4, 3)) for i in range(5)]
    emit(add, kind, "FallenPorchBoards", porch, "Timber", False, WOOD)
    wood, iron = [], []
    if variant == 0:
        swood, siron = sled()
        wood += [put(g, (-5.5, .05, 4.5), (0, 18, 0)) for g in swood]
        iron += [put(g, (-5.5, .05, 4.5), (0, 18, 0)) for g in siron]
        # Empty low wood rack once kept the sled's load off damp ground.
        wood += [box((-5.5, .82, z), (.10, 1.64, .12)) for z in (-3.25, -1.15)]
        wood += [box((-5.5, y, -2.2), (.08, .09, 2.25)) for y in (.3, 1.38)]
        wood += [box((-5.05, .22, z), (.96, .09, .13)) for z in (-3.25, -1.15)]
    elif variant == 1:
        cwood, ciron = cart()
        wood += [put(g, (5.3, .035, 5.6), (0, -24, 0)) for g in cwood]
        iron += [put(g, (5.3, .035, 5.6), (0, -24, 0)) for g in ciron]
        wood += [put(g, (4.9, .06, -3.6), (0, 13, 0)) for g in fence_segment(2.2, (2, 3, 4), True)]
    else:
        f, b = bench(1.85, True)
        wood += [put(g, (-5.4, .04, 5.4), (0, 90, 0)) for g in b]
        emit(add, kind, "RemainingBenchFeet", [put(g, (-5.4, .04, 5.4), (0, 90, 0)) for g in f],
             "Masonry", True, STONE)
        wood += [box((-5.7, .71, z), (.10, 1.42, .12)) for z in (-3.3, -.7)]
        wood += [box((-5.7, 1.33, -2.0), (.10, .12, 2.72))]
        iron += [beam((-5.65, 1.32, z), (-5.38, 1.32, z), .035) for z in (-2.9, -2.25, -1.4)]
    emit(add, kind, "HouseholdEquipmentTimber", wood, "Timber", True, BLEACHED)
    emit(add, kind, "HouseholdEquipmentIron", iron, "RustedIron", False, IRON)
    # A real empty tapered bucket is an open wall and bottom, not a solid drum.
    bucket = bp.to_source(kit.lathe([(.13, 0), (.155, .025), (.195, .35),
                                    (.18, .35), (.14, .038)], 10))
    bucket = put(bucket, (side * 5.2, .445, 3.3), (0, 0, 0))
    emit(add, kind, "EmptyBucket", [bucket], "RustedIron", False, IRON)
    emit(add, kind, "OldSnowAmongObjects", [snow((side * 5.0, .55, 2.6), 1.45, 1.3),
        snow((-side * 4.85, .37, 8.5), 2.30, .39)], "WindSnow", False)


def build_all(add):
    town_hall(add)
    school(add)
    shop_bakery(add)
    workshop(add)
    mountain_rescue(add)
    for variant in range(3):
        household(add, variant)
