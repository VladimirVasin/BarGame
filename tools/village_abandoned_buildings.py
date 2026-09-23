"""Fixed-metre passive remains of the village's former domestic/civic fabric.

All fronts face Unity +Z. Closed buildings have thick walls, inset opaque
windows and capped gables; the ruined types contain only the walls that remain.
The expansion generator owns export/material import and calls ``build_all``.
"""
from __future__ import annotations

import math
import sys
sys.dont_write_bytecode = True
import bar_parts as bp
import interior_kit as kit

KINDS = ("TownHall", "School", "ShopBakery", "Workshop", "MountainRescue",
         "AbandonedHouseA", "AbandonedHouseB", "WornHouseA", "WornHouseB",
         "RuinedHouse", "HouseholdFoundation", "AbandonedShed", "RuinedShed")


def _box(p, s, c=.012):
    # Sub-centimetre bevels on distant plank joins/backings buy no visible edge.
    # Retain bevels on broad masonry, structural wood and walk-up surfaces.
    return bp.u_box(p, s, 0 if c < .009 else c)


def _move(g, p):
    return kit.translated(g, p)


def _turn(g, e):
    return bp.u_rotated(g, e)


def _merge(pieces):
    pieces = list(pieces)
    for piece in pieces:
        assert bp.signed_volume(piece) > 1e-9, "Inward abandoned-building component"
    return kit.merge_all(pieces)


def _profile(points, depth):
    area = sum(a[0]*b[1]-b[0]*a[1]
               for a, b in zip(points, points[1:] + points[:1]))
    if area < 0:
        points = list(reversed(points))
    return bp.to_source(kit.prism(points, depth))


def _beam(a, b, width=.12, thickness=None):
    direction = tuple(b[i]-a[i] for i in range(3))
    length = math.sqrt(sum(v*v for v in direction))
    y_axis = tuple(v/length for v in direction)
    horizontal = math.hypot(y_axis[0], y_axis[2])
    x_axis = ((y_axis[2]/horizontal, 0, -y_axis[0]/horizontal)
              if horizontal > 1e-8 else (1, 0, 0))
    z_axis = (x_axis[1]*y_axis[2]-x_axis[2]*y_axis[1],
              x_axis[2]*y_axis[0]-x_axis[0]*y_axis[2],
              x_axis[0]*y_axis[1]-x_axis[1]*y_axis[0])
    raw = _box((0, 0, 0), (width, length, thickness or width))
    middle = tuple((a[i]+b[i])*.5 for i in range(3))
    return ([tuple(middle[j] + x*x_axis[j] + y*y_axis[j] + z*z_axis[j]
                   for j in range(3)) for x, y, z in raw[0]], raw[1])


class _Parts:
    def __init__(self, kind):
        self.kind = kind
        self.groups = {}

    def put(self, name, geometry, surface, solid=True, tint=None):
        assert bp.signed_volume(geometry) > 1e-9, (self.kind, name)
        key = name, surface, solid, tint
        self.groups.setdefault(key, []).append(geometry)

    def emit(self, add):
        names = [key[0] for key in self.groups]
        for (name, surface, solid, tint), geometries in self.groups.items():
            if names.count(name) > 1:
                name += "_" + surface
            add(self.kind, name, _merge(geometries), surface, solid, tint)


def _face(g, angle, offset):
    return _move(_turn(g, (0, angle, 0)), offset)


def _window(parts, x, sill, width, height, angle, offset, wear, index):
    """A backed opening, relieved frame and tired shutters, never luminous glass."""
    y = sill + height*.5
    def place(name, g, surface, solid=True):
        parts.put(name, _face(g, angle, offset), surface, solid)
    place("OpaqueWindowBacking", _box((x, y, -.07),
          (width+.025, height+.025, .08), .005), "DarkWindow")
    frame = [_box((x+side*(width*.5+.035), y, .175),
                   (.09, height+.17, .13)) for side in (-1, 1)]
    frame += [_box((x, sill+dy, .175), (width+.16, .09, .13))
              for dy in (-.035, height+.035)]
    frame += [_box((x, y, .205), (.047, height, .065), .004),
              _box((x, y+.10, .205), (width, .045, .065), .004),
              _box((x, sill-.09, .215), (width+.27, .12, .27))]
    place("WindowJoinery", _merge(frame), "AbandonedWood")
    # Some shutters remain closed, some are lost. The frame always has a backing.
    shutter_sides = (-1, 1) if wear == 0 or index % 3 else (-1,)
    for side in shutter_sides:
        closed = index % 4 == 1
        shutter_width = min(.43, width*.43)
        sx = x + side*(width*.255 if closed else width*.5+shutter_width*.53+.10)
        shutter = []
        for plank in range(3):
            ph = height + .06 - (.18 if wear and plank == 2 and index % 2 else 0)
            shutter.append(_box((sx+(plank-1)*shutter_width/3, y, .27),
                            (shutter_width/3-.014, ph, .085), .007))
        shutter += [_box((sx, y+dy, .325), (shutter_width+.02, .085, .045), .005)
                    for dy in (-height*.31, height*.31)]
        g = _merge(shutter)
        if wear and side == -1:
            g = bp.u_rotated_about(g, (0, 0, -5), (sx, y+height*.42, .27))
        place("ShuttersAndBoards", g, "AbandonedWood")
    if wear and index % 5 == 2:
        boards = [_turn(_box((0, 0, 0), (width+ .26, .15, .09)), (0, 0, tilt))
                  for tilt in (-6, 9)]
        place("ShuttersAndBoards", _merge([_move(board, (x, y+delta, .35))
              for board, delta in zip(boards, (-.24, .31))]), "AbandonedWood")


def _door(parts, x, width, height, angle, offset, double=False):
    def place(name, geometry, surface, solid=True):
        parts.put(name, _face(geometry, angle, offset), surface, solid)
    count = max(5, int(width/.19))
    leaf = [_box((x-width*.5+(i+.5)*width/count, height*.5, .015),
                 (width/count-.009, height, .18), .007) for i in range(count)]
    # A concealed continuous backing keeps the closed door opaque between planks.
    leaf.append(_box((x, height*.5, -.09), (width, height, .07), .005))
    leaf += [_box((x+side*(width*.5+.075), height*.5, .16),
                  (.15, height+.12, .20)) for side in (-1, 1)]
    leaf.append(_box((x, height+.065, .16), (width+.31, .16, .20)))
    for side in ((-1, 1) if double else (0,)):
        cx = x + side*width*.25
        span = width*.45 if double else width*.9
        leaf += [_box((cx, y, .14), (span, .13, .08), .008)
                 for y in (.30, height-.31)]
    place("ClosedDoorLeaves", _merge(leaf), "AbandonedWood")
    hardware = [_box((x + side*(width*.5-.15), y, .21),
                     (.31, .06, .035), .003)
                for side in (-1, 1) for y in (.42, height-.44)]
    hardware.append(_box((x+.08, height*.46, .22), (.08, .24, .05), .005))
    if double:
        hardware.append(_box((x, height*.49, .235), (.64, .075, .055), .005))
    place("IronHardware", _merge(hardware), "RustedIron", False)


def _roof(parts, width, depth, wall_top, rise, wear=0):
    rw, rd = width+1.05, depth+1.0
    half = rd*.5
    length = math.hypot(half, rise+.12)
    angle = math.degrees(math.atan2(rise+.12, half))
    for side in (-1, 1):
        center = (0, wall_top-.10+(rise+.12)*.5, side*half*.5)
        deck = _face(_box((0, 0, 0), (rw, .18, length)), 0, (0, 0, 0))
        deck = _move(_turn(deck, (side*angle, 0, 0)), center)
        parts.put("RoofDeck", deck, "AbandonedRoof")
        for panel in range(5):
            if wear and side == 1 and panel == (1 if wear == 1 else 3):
                continue
            px = (panel-2)*rw/5
            covering = _box((px, .105, 0), (rw/5-.025, .055, length-.025), .006)
            snow = _box((px, .192, -.04*side),
                        (rw/5-.052, .13, length-.16), .024)
            parts.put("RoofCovering", _move(_turn(covering, (side*angle, 0, 0)), center),
                      "AbandonedRoof", False)
            parts.put("RoofSnow", _move(_turn(snow, (side*angle, 0, 0)), center),
                      "WindSnow", False)
        # Weathered purlin ends visibly bear the roof rather than floating decoration.
        for x in (-width*.44, 0, width*.44):
            parts.put("BearingTimbers", _beam((x, wall_top-.13, side*(depth*.5-.18)),
                       (x, wall_top-.13, side*(depth*.5+.41)), .15), "AbandonedWood")
        parts.put("EavesAndGutters", _box((0, wall_top-.10, side*(depth*.5+.42)),
                  (rw-.06, .18, .16)), "AbandonedWood", False)
        gutter_pieces = ((-rw*.25, rw*.48), (rw*.28, rw*.38)) if wear else ((0, rw-.18),)
        for x, span in gutter_pieces:
            parts.put("IronHardware", _box((x, wall_top-.14, side*(depth*.5+.53)),
                      (span, .085, .10), .008), "RustedIron", False)
    parts.put("RidgeCap", _box((0, wall_top+rise+.14, 0),
              (rw+.035, .19, .25)), "AbandonedRoof", False)
    for side in (-1, 1):
        gable = _profile([(-depth*.5, 0), (depth*.5, 0), (0, rise)], .31)
        parts.put("ClosedGables", _move(_turn(gable, (0, 90, 0)),
                  (side*(width*.5-.155), wall_top, 0)), "AbandonedWood")
        for end in (-1, 1):
            parts.put("BearingTimbers", _beam((side*(width*.5+.02), wall_top, end*depth*.5),
                      (side*(width*.5+.02), wall_top+rise, 0), .16), "AbandonedWood")


def _courses(parts, width, depth, base):
    for angle, length, offset in ((0, width, (0, 0, depth*.5+.007)),
                                  (180, width, (0, 0, -depth*.5-.007)),
                                  (90, depth, (width*.5+.007, 0, 0)),
                                  (-90, depth, (-width*.5-.007, 0, 0))):
        count = max(3, int(length/.86))
        for row in range(2):
            for i in range(count):
                if (i+row) % 4 == 1:
                    continue
                x = -length*.5+(i+.5)*length/count
                parts.put("PlinthStoneCourses", _face(_box((x, base*(.24+.49*row), 0),
                          (length/count-.055, base*.43, .045), .01), angle, offset), "LayeredStone", False)


def _standing(kind, width, depth, wall_height, rise, levels=1, wear=0,
              wood=True, front=None, back=None, window_width=1.14, window_height=1.15):
    parts = _Parts(kind)
    base = .62 if levels == 2 else .48
    parts.put("RootedStonePlinth", _box((0, base*.5, 0), (width, base, depth), .045), "LayeredStone")
    _courses(parts, width, depth, base)
    floor_height = wall_height/levels
    for level in range(levels):
        y0 = base+level*floor_height
        h = min(window_height, floor_height-.56)
        sill = max(.38, (floor_height-h)*.45)
        default_x = [-width*.32, width*.32]
        if width >= 12:
            default_x = [-width*.365, -width*.20, width*.20, width*.365]
        layouts = ((0, width, (0, y0, depth*.5-.16), front),
                   (180, width, (0, y0, -depth*.5+.16), back),
                   (90, depth-.64, (width*.5-.16, y0, 0), None),
                   (-90, depth-.64, (-width*.5+.16, y0, 0), None))
        for face_index, (angle, span, offset, special) in enumerate(layouts):
            # Entries are kind, centre, width, sill, height. Only the ground facade has doors.
            if special is not None and level == 0:
                layout = special
            else:
                xs = default_x if face_index < 2 else [-depth*.23, depth*.23]
                layout = [("window", x, window_width, sill, h) for x in xs]
                if level == 0 and face_index == 0:
                    layout.append(("door", 0, 1.28, 0, min(2.18, floor_height-.13)))
                if level > 0 and width >= 12:
                    layout.append(("window", 0, window_width, sill, h))
            openings = [kit.Opening(x, w, bottom+height, bottom)
                        for _, x, w, bottom, height in layout]
            shell = bp.to_source(kit.wall_run(span, floor_height, .32, openings, .012))
            surface = "AbandonedWood" if wood and (level > 0 or levels == 1) else "AbandonedPlaster"
            parts.put("TimberWalls" if surface == "AbandonedWood" else "PlasterWalls",
                      _face(shell, angle, offset), surface)
            for i, (entry, x, w, bottom, height) in enumerate(layout):
                if entry == "window":
                    _window(parts, x, bottom, w, height, angle, offset,
                            wear, i+face_index*3+level*7)
                else:
                    _door(parts, x, w, height, angle, offset, w > 1.8)
            # Wide belt timbers/cornices and corner posts show the old construction.
            bands = [_box((0, floor_height-.09, .205), (span+.10, .18, .13))]
            if surface == "AbandonedWood":
                bands += [_box((x, floor_height*.5, .20), (.14, floor_height, .15))
                          for x in (-span*.5+.13, span*.5-.13)]
                for row in range(1, int(floor_height/.30)):
                    # Short log ends leave the actual window/door reveals free.
                    bands += [_box((side*(span*.5-.35), row*.30, .18),
                              (.47, .035, .07), .004) for side in (-1, 1)]
            parts.put("FacadeBearingBands", _face(_merge(bands), angle, offset),
                      "AbandonedWood" if wood else "Masonry", False)
            if surface == "AbandonedPlaster":
                patches = []
                for j in range(3):
                    px = -span*.40+j*span*.38
                    points = [(-.32, 0), (.37, .03), (.31, .19), (.10, .25), (-.29, .17)]
                    patches.append(_move(_profile(points, .025), (px, .12+j*.03, .17)))
                parts.put("WeatherExposedMasonry", _face(_merge(patches), angle, offset), "Masonry", False)
    _roof(parts, width, depth, base+wall_height, rise, wear)
    return parts


def _steps(parts, z, width=2.4, reach=1.3, x=0):
    for index in range(3):
        height = .16*(index+1)
        depth = reach*(1-index*.23)
        parts.put("WornEntranceSteps", _box((x, height*.5, z+depth*.5-.04),
                  (width-index*.08, height, depth), .035), "LayeredStone")


def _porch(parts, z, width, wear=0):
    _steps(parts, z, width+.3, 1.4)
    roof = _turn(_box((0, 0, 0), (width+.35, .16, 1.6)), (-5, 0, -3 if wear else 0))
    parts.put("EntranceCanopy", _move(roof, (0, 2.98, z+.55)), "AbandonedRoof")
    for side in (-1, 1):
        top = 2.90 + (-.09 if wear and side < 0 else 0)
        parts.put("EntrancePosts", _beam((side*width*.45, .43, z+1.11),
                  (side*width*.45, top, z+1.11), .16), "AbandonedWood")
        parts.put("EntrancePosts", _beam((side*width*.45, top-.56, z+1.11),
                  (side*width*.29, top, z+1.11), .115), "AbandonedWood")
    parts.put("RoofSnow", _move(_turn(_box((0, 0, 0),
              (width+.27, .10, 1.49)), (-5, 0, -3 if wear else 0)), (0, 3.105, z+.55)), "WindSnow", False)


def _town_hall():
    front = [("window", x, 1.18, .67, 1.55) for x in (-5.05, -2.8, 2.8, 5.05)]
    front.append(("door", 0, 1.86, 0, 2.43))
    parts = _standing("TownHall", 14, 10, 5.6, 1.36, 2, 1, False,
                      front=front, window_height=1.48)
    _steps(parts, 5, 4.1, 2.15)
    # The entrance is emphatic through sound masonry, without heraldry or a grand tower.
    for side in (-1, 1):
        parts.put("EntranceStoneSurround", _box((side*1.16, 1.89, 5.10),
                  (.28, 2.58, .31), .03), "Masonry")
    parts.put("EntranceStoneSurround", _box((0, 3.24, 5.12),
              (2.78, .24, .40), .025), "Masonry")
    for z in (-5.06, 5.06):
        parts.put("StoneCornice", _box((0, 6.14, z), (14.25, .23, .26), .025), "Masonry")
    for x in (-6.94, 6.94):
        for z in (-4.94, 4.94):
            for row in range(8):
                parts.put("StoneCornerBlocks", _box((x, .99+row*.66, z),
                          (.42 if row % 2 else .60, .57, .60 if row % 2 else .42), .025), "LayeredStone", False)
    return parts


def _school():
    front = [("window", x, 1.46, 1.08, 1.86) for x in (-5.15, -2.95, 2.95, 5.15)]
    front.append(("door", 0, 1.52, 0, 2.45))
    parts = _standing("School", 14, 8, 3.76, 1.20, 1, 1, False,
                      front=front, window_width=1.44, window_height=1.82)
    _porch(parts, 4, 2.6, 1)
    # The school is recognisable by its wide window rhythm and quiet, worn entrance.
    for x in (-6.75, 6.75):
        parts.put("GableBracing", _beam((x, 4.27, -3.7), (x, 5.37, 0), .14), "AbandonedWood")
        parts.put("GableBracing", _beam((x, 4.27, 3.7), (x, 5.37, 0), .14), "AbandonedWood")
    return parts


def _shop_bakery():
    front = [("window", -3.65, 2.75, .61, 1.77),
             ("door", 0, 1.35, 0, 2.47), ("window", 3.55, 2.62, .61, 1.77)]
    back = [("window", -3.7, 1.12, 1.09, 1.17), ("door", 1.95, 2.48, 0, 2.51)]
    parts = _standing("ShopBakery", 12, 9, 3.16, 1.15, 1, 1, False, front, back)
    _steps(parts, 4.48, 2.05, .48)
    _steps(parts, -5.03, 2.8, .59, -1.95)
    # A thick masonry oven/chimney remains inside the footprint of the bakery wing.
    parts.put("BakeryChimney", _box((4.30, 3.42, -2.0), (1.20, 5.88, 1.08), .035), "LayeredStone")
    parts.put("BakeryChimney", _box((4.30, 6.43, -2.0), (1.39, .20, 1.27), .028), "Masonry")
    parts.put("ChimneyDarkMouth", _box((4.30, 6.537, -2.0), (.89, .025, .73), .005), "DarkWindow", False)
    soot = _box((4.31, 5.88, -1.451), (.80, .91, .026), .006)
    parts.put("OldChimneySoot", soot, "DarkWindow", False, (.23, .235, .22, 1))
    return parts


def _workshop():
    front = [("door", -1.0, 4.05, 0, 2.91), ("window", 3.3, 1.32, 1.01, 1.44)]
    parts = _standing("Workshop", 10, 8, 3.65, 1.12, 1, 2, True, front=front)
    _steps(parts, 4, 4.4, .9, -1)
    # The old rear annex has actually lost its roof and upper walls.
    parts.put("BrokenRearAnnex", _box((-2.4, .18, -4.97), (3.8, .36, 2.12), .04), "LayeredStone")
    for x, h in ((-4.11, 1.61), (-.70, .81)):
        parts.put("BrokenRearAnnex", _box((x, .36+h*.5, -5.03), (.25, h, 2.0)), "AbandonedWood")
    parts.put("CollapsedAnnexRafters", _beam((-4, .65, -5.86), (-.95, .50, -4.25), .19), "AbandonedWood")
    parts.put("CollapsedAnnexRafters", _beam((-3.73, .47, -4.29), (-1.1, .43, -5.62), .17), "AbandonedWood")
    return parts


def _mountain_rescue():
    front = [("door", -2.70, 4.2, 0, 3.03), ("window", 1.78, 1.38, 1.12, 1.37),
             ("door", 4.0, 1.18, 0, 2.33)]
    parts = _standing("MountainRescue", 12, 8, 3.74, 1.09, 1, 1, False, front=front)
    _steps(parts, 4, 4.55, .65, -2.70)
    _steps(parts, 4, 1.65, .83, 4)
    # Restrained service construction: a timber-banded garage beside the duty room.
    for x in (-5.13, -.27):
        parts.put("GaragePortalTimbers", _box((x, 2.08, 4.06), (.22, 3.22, .23)), "AbandonedWood")
    parts.put("GaragePortalTimbers", _box((-2.7, 3.71, 4.06), (5.10, .25, .24)), "AbandonedWood")
    return parts


def _house(kind, variant, wear):
    width, depth, wall, rise, levels = ((8, 7, 3.27, 1.17, 1) if variant == 0
                                       else (9, 8, 4.22, 1.25, 2))
    parts = _standing(kind, width, depth, wall, rise, levels, wear,
                      window_width=1.04, window_height=1.20 if levels == 1 else .98)
    _porch(parts, depth*.5, 1.83, wear)
    if variant == 1:
        # The tall type retains the old load-bearing upper-storey consoles.
        for x in (-3.75, -2.05, 2.05, 3.75):
            for side in (-1, 1):
                parts.put("UpperStoreyConsoles", _beam((x, 2.24, side*3.94),
                          (x, 2.69, side*4.31), .19), "AbandonedWood")
    if wear:
        for i, (x, z, yaw) in enumerate(((-2.7, 3.91, 19), (-2.1, 4.2, -12))):
            parts.put("FallenPorchBoards", _move(_turn(_box((0, 0, 0),
                      (1.49, .13, .27)), (0, yaw, 0)), (x, .12+i*.08, z)), "AbandonedWood")
    return parts


def _ruin(kind="RuinedHouse", shed=False, foundation=False):
    width, depth = (6, 4) if shed else (8, 7)
    parts = _Parts(kind)
    # No hidden closed collision box: all visible remains are independent solids.
    parts.put("ExposedFloor", _box((0, .10, 0), (width, .20, depth), .035), "LayeredStone")
    height = .54 if foundation else (2.55 if shed else 3.83)
    thickness = .32
    top = lambda proportion: .20+(height-.20)*proportion
    left_profile = [(-depth*.5, .20), (depth*.5, .20), (depth*.5, top(.32)),
                    (depth*.23, top(.41)), (depth*.16, top(.78)),
                    (-depth*.07, top(.81)), (-depth*.12, height), (-depth*.5, height)]
    for side, scale in ((-1, 1), (1, .52)):
        profile = [(x, .20+(y-.20)*scale) for x, y in left_profile]
        parts.put("RemainingWalls", _move(_turn(_profile(profile, thickness),
                  (0, -90, 0)), (side*(width*.5-thickness*.5), 0, 0)), "LayeredStone")
    rear = [(-width*.5+.30, .20), (width*.5-.30, .20), (width*.5-.30, top(.57)),
            (width*.20, top(.62)), (width*.12, top(.90)), (-width*.12, top(.94)),
            (-width*.19, height), (-width*.5+.30, height)]
    parts.put("RemainingWalls", _move(_profile(rear, thickness),
              (0, 0, -depth*.5+.16)), "LayeredStone")
    for side in (-1, 1):
        parts.put("LowFrontWall", _box((side*width*.32, .40, depth*.5-.16),
                  (width*.34, .40, .32), .035), "LayeredStone")
    if not foundation:
        # A surviving roof bay and its broken truss keep the old house legible at distance.
        ridge_y = height + (.83 if shed else 1.02)
        for x in (-width*.42, -width*.16):
            parts.put("ExposedRoofRafters", _beam((x, height-.05, -depth*.47),
                      (x, ridge_y, 0), .18), "AbandonedWood")
            parts.put("ExposedRoofRafters", _beam((x, ridge_y, 0),
                      (x, height+.30, depth*.27), .16), "AbandonedWood")
        parts.put("ExposedRoofRafters", _beam((-width*.44, ridge_y, 0),
                  (-width*.09, ridge_y-.035, 0), .22), "AbandonedWood")
        partial_depth = depth*.53
        slope_length = math.hypot(partial_depth, ridge_y-height)
        angle = -math.degrees(math.atan2(ridge_y-height, partial_depth))
        roof_center = (-width*.30, (height+ridge_y)*.5, -partial_depth*.5)
        parts.put("SurvivingRoofBay", _move(_turn(_box((0, 0, 0),
                  (width*.34, .17, slope_length)), (angle, 0, 0)), roof_center), "AbandonedRoof")
        parts.put("RoofSnow", _move(_turn(_box((0, .135, 0),
                  (width*.32, .12, slope_length-.11)), (angle, 0, 0)), roof_center), "WindSnow", False)
        for i in range(5):
            x = -width*.15 + i*.52
            parts.put("CollapsedTimbers", _beam((x-.80, .28+i*.024, -depth*.13),
                      (x+.67, .29+i*.039, depth*.26), .15 if i % 2 else .20), "AbandonedWood")
    for i in range(13 if not foundation else 8):
        x = (-width*.38+(i%5)*width*.14)
        z = -depth*.22+(i//5)*.63
        stone = _turn(_box((0, 0, 0), (.38+(i%3)*.14, .22+(i%2)*.10, .35)),
                      (0, i*29 % 85, (-1 if i % 2 else 1)*7))
        parts.put("SettledWallDebris", _move(stone, (x, .35, z)), "LayeredStone")
    for x, z, sx, sz in ((width*.24, depth*.16, width*.34, depth*.35),
                          (-width*.15, -depth*.30, width*.34, depth*.20)):
        parts.put("SnowInside", _box((x, .225, z), (sx, .08, sz), .024), "WindSnow", False)
    if not shed:
        chimney_height = 1.72 if foundation else 3.1
        parts.put("SurvivingHearth", _box((-width*.32, chimney_height*.5+.20, -depth*.31),
                  (1.05, chimney_height, .94), .035), "Masonry")
        parts.put("OldHearthMouth", _box((-width*.32, .65, -depth*.31+.48),
                  (.59, .66, .03), .008), "DarkWindow", False)
    return parts


def _shed():
    front = [("door", 0, 1.55, 0, 1.92)]
    parts = _standing("AbandonedShed", 6, 4, 2.16, .78, 1, 1, True, front=front,
                      window_width=.58, window_height=.63)
    _steps(parts, 2, 1.8, .78)
    return parts


def build_all(add):
    """Emit the thirteen building kinds through the expansion's measured add hook."""
    for parts in (_town_hall(), _school(), _shop_bakery(), _workshop(), _mountain_rescue(),
                  _house("AbandonedHouseA", 0, 0), _house("AbandonedHouseB", 1, 0),
                  _house("WornHouseA", 0, 1), _house("WornHouseB", 1, 2),
                  _ruin(), _ruin("HouseholdFoundation", foundation=True), _shed(),
                  _ruin("RuinedShed", shed=True)):
        parts.emit(add)
