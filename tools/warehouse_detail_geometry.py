"""Bounded constructive detail for the warehouse kit, in authored Unity metres.

These are passive members of the existing modules, not a second population of
runtime objects. Collision, anchors and the original outer bounds are immutable.
Static additions merge into an existing surface mesh; only the three mutually
exclusive room dressing groups deliberately add renderers.
"""
from __future__ import annotations

import math

import bar_parts as bp
import interior_kit as kit


class Detail:
    def __init__(self, item, rod_geometry, ring_geometry):
        self.item = item
        self.rod_geometry = rod_geometry
        self.ring_geometry = ring_geometry
        self.original_keys = set(item.parts)
        self.original_anchors = repr(item.anchors)
        self.original_collision = {key: repr(value) for key, value in item.parts.items()
                                   if key[0].startswith("Collision_")}
        points = [point for solids in item.parts.values() for vertices, _ in solids for point in vertices]
        self.low = tuple(min(point[axis] for point in points) for axis in range(3))
        self.high = tuple(max(point[axis] for point in points) for axis in range(3))
        self.solids = self.triangles = 0

    def add(self, role, geometry, group=None):
        if group is None:
            # Sets are not an authoring order. Choose a stable existing mesh.
            group = min(name for name, material in self.original_keys
                        if material == role and not name.startswith("Collision_"))
        variant = group in ("RoomVariant0", "RoomVariant1", "RoomVariant2")
        if (group, role) not in self.original_keys and not variant:
            raise RuntimeError(f"Warehouse detail must reuse a surface mesh: {self.item.name}/{group}/{role}")
        if variant and not any(material == role for _, material in self.original_keys):
            raise RuntimeError("Room dressing requires an existing surface")
        if group.startswith("Collision_"):
            raise RuntimeError("Warehouse detail cannot alter collision")
        for point in geometry[0]:
            if any(point[axis] < self.low[axis] - 1e-7 or point[axis] > self.high[axis] + 1e-7
                   for axis in range(3)):
                raise RuntimeError(f"Warehouse detail escaped original bounds: {self.item.name}/{group}: {point}")
        self.item.add(role, geometry, group)
        self.solids += 1
        self.triangles += kit.triangle_count(geometry)

    def box(self, role, p, size, group=None, chamfer=0):
        self.add(role, bp.u_box(p, size, min(chamfer, min(size) * .23)), group)

    def rod(self, role, a, b, radius, group=None, sides=6):
        self.add(role, self.rod_geometry(a, b, radius, sides), group)

    def beam(self, role, a, b, width, depth, group=None):
        dx, dy, dz = (b[axis] - a[axis] for axis in range(3))
        geometry = bp.u_box((0, 0, 0), (width, depth, math.dist(a, b)), 0)
        angles = (-math.degrees(math.atan2(dy, math.hypot(dx, dz))),
                  math.degrees(math.atan2(dx, dz)), 0)
        self.add(role, kit.translated(bp.u_rotated(geometry, angles),
                 tuple((a[axis] + b[axis]) * .5 for axis in range(3))), group)

    def ring(self, role, p, radius, bore, thickness, axis="Y", group=None, sides=10):
        half = thickness * .5
        geometry = self.ring_geometry([(-half, radius), (half, radius),
                                       (half, bore), (-half, bore)], sides)
        angles = {"X": (0, 0, 90), "Y": (0, 0, 0), "Z": (90, 0, 0)}[axis]
        self.add(role, kit.translated(bp.u_rotated(geometry, angles), p), group)

    def finish(self):
        if repr(self.item.anchors) != self.original_anchors or any(
                repr(self.item.parts[key]) != value for key, value in self.original_collision.items()):
            raise RuntimeError("Warehouse constructive detail changed collision or anchors")
        self.item.detail_geometry = {"added_solids": self.solids, "added_triangles": self.triangles,
                                     "original_bounds_retained": True, "collision_unchanged": True}


def shell(d):
    # Rolled column flanges, splice plates and anchored feet give the frame a
    # construction rhythm. All fittings remain at the existing perimeter steel.
    for side in (-1, 1):
        for z in (-9, -3, 3, 9):
            for dz in (-.13, .13):
                d.box("Steel", (side * 14.88, 5., z + dz), (.23, 9.85, .035), "Structure")
            for y in (.16, 3.2, 6.4, 9.72):
                d.box("Steel", (side * 14.75, y, z), (.035, .28, .30), "Structure")
                for dz in (-.095, .095):
                    d.rod("WornSteel", (side * 14.72, y, z + dz),
                          (side * 14.69, y, z + dz), .025, "Structure")
        # An open ladder tray bears on real wall brackets above the room roofs.
        for offset in (-.15, .15):
            d.box("Steel", (side * (14.54 + offset), 9.68, 0), (.025, .11, 22.8), "Structure")
        for z in range(-11, 12):
            d.box("Steel", (side * 14.54, 9.63, z), (.34, .025, .055), "Structure")
        for z in (-9, -3, 3, 9):
            d.beam("Steel", (side * 14.95, 9.39, z),
                   (side * 14.39, 9.61, z), .045, .045, "Structure")
        for offset in (-.065, .065):
            d.rod("Steel", (side * (14.54 + offset), 9.69, -11.3),
                  (side * (14.54 + offset), 9.69, 11.3), .022, "Structure")
    # Beam end cleats read from the upper gallery without filling the central void.
    for x in (-14.7, 14.7):
        for z in (-9, -5.5, 5.5, 9):
            d.box("Rust", (x, 9.90, z), (.23, .25, .32), "Joists")
            d.beam("Steel", (x, 9.81, z), (x * .94, 10.03, z), .055, .075, "Joists")


def platform(d):
    # Four flat connection straps only: this module is placed 118 times.
    for x in (-1.32, 1.32):
        for z in (-1.24, 1.24):
            d.box("Steel", (x, -.265, z), (.24, .045, .32), "DeckBearers")


def stair(d):
    for side in (-1, 1):
        for index in (1, 5, 9, 13, 17):
            y, z = (index + 1) * .16 - .085, (index + .5) * .30
            d.rod("Steel", (side * 1.18, y, z), (side * 1.225, y, z), .031, "Stringers")
        for y, z in ((.16, .16), (3.1, 5.78)):
            d.box("Steel", (side * 1.12, y, z), (.23, .07, .30), "Stringers")
        # Braced handrail feet and short end sleeves keep the real guard intact.
        for index in (0, 8, 19):
            z, y = (index + .5) * .30, (index + 1) * .16
            d.box("Steel", (side * 1.245, y + .09, z), (.055, .13, .11), "Guard")


def rail(d):
    for x in (-.435, .435):
        d.box("WornSteel", (x, .07, 0), (.09, .08, .085), "Shoes")


def rack(d):
    for x in (-1.405, 1.405):
        for z in (-.505, .505):
            d.box("Steel", (x, 1.1, z - .032), (.12, 2.08, .024), "Posts")
            d.rod("WornSteel", (x, .052, z), (x, .07, z), .028, "Shoes")
    for y in (.18, 1.03, 1.93):
        for x in (-1.30, 1.30):
            for z in (-.51, .51):
                d.box("Steel", (x, y - .095, z), (.21, .15, .045), "ShelfLips")
        d.box("Timber", (0, y - .065, 0), (.13, .08, 1.10), "Shelves")
    # Folded label holders are blank hardware, not a new readable sign.
    for x in (-.86, .16, 1.04):
        d.box("Steel", (x, .965, -.586), (.30, .09, .018), "ShelfLips")
    for x, y in ((-.95, .51), (.1, .51), (.95, 1.33), (-.5, 1.33)):
        for dx in (-.25, .25):
            d.box("Timber", (x + dx, y + .281, 0), (.024, .004, .82), "CartonSeams")


def cargo(d):
    for x in (-.83, 0, .83):
        for z in (-.48, .48):
            d.box("Timber", (x, .071, z), (.23, .10, .19), "PalletSkids")
            d.box("Rubber", (x, .182, z), (.026, .004, .026), "LoadStraps")
    for x in (-.62, .62):
        d.box("Rubber", (x, .87, -.682), (.085, .09, .022), "LoadStraps")
        d.box("Rubber", (x, .87, -.696), (.038, .055, .006), "LoadStraps")
    # Compressed corner protectors articulate the strapped stack.
    for x in (-.91, .91):
        for z in (-.62, .62):
            d.box("Cardboard", (x, 1.14, z), (.05, .16, .09), "Load")


def pipes(d):
    for x in (-4.7, -.10, 4.7):
        for y in (4.54, 4.90):
            for offset in (-.075, .075):
                d.rod("WornSteel", (x - .032, y + offset, .20),
                      (x + .032, y + offset, .20), .016, "Body")
    for x in (-4.8, -2.4, 0, 2.4, 4.8):
        for y in (4.54, 4.90):
            d.box("Steel", (x, y, .30), (.13, .055, .035), "PipeSupports")
    for x in (-4.97, -4.63):
        for y in (.47, .86):
            d.rod("WornSteel", (x, y, .397), (x, y, .407), .015, "Equipment")


def room(d):
    for z in (-2.1, -1.4, -.7, 0, .7, 1.4, 2.1):
        d.box("Rust", (-2.59, 2.49, z), (.16, .07, .035), "CableTray")
    for x in (-2.68, -2.52):
        d.box("Rust", (x, 2.50, 0), (.018, .12, 4.45), "CableTray")
    # Surface service feeds stop above the base guard, away from the doorway.
    d.rod("Steel", (-2.77, .62, -2.2), (-2.77, 2.55, -2.2), .027, "EntryFrame")
    for y in (.75, 1.45, 2.35):
        d.box("Steel", (-2.76, y, -2.2), (.08, .055, .12), "EntryFrame")
    for z in (-2.77, 2.77):
        for x in (-2., 0, 2.):
            d.box("ConcreteDark", (x, .29, z), (.14, .52, .035), "LowerGuard")


def door(d):
    for x in (-1.84, 1.84):
        for y in (.45, 1.52, 2.57):
            d.rod("WornSteel", (x, y - .09, -.14), (x, y + .09, -.14), .032, "DoorFrame")
            for dx in (-.07, .07):
                d.box("Rust", (x + dx, y, -.123), (.075, .11, .025), "DoorReinforcement")
    for x in (-.17, .17):
        for y in (1.07, 1.42):
            d.box("WornSteel", (x, y, -.123), (.095, .085, .033), "Handle")


def gate(d):
    for x in (-2.08, 2.08):
        for y in (.66, 2.34):
            d.rod("WornSteel", (x - .07, y, -.13), (x + .07, y, -.13), .066, "LiftGuides", sides=10)
        for y in (.30, 1.5, 2.76):
            d.box("Steel", (x, y, -.12), (.22, .095, .05), "GuideSlots")
    for x in (-1.78, 1.78):
        for dx in (-.085, .085):
            d.rod("WornSteel", (x + dx, 3.19, -.247), (x + dx, 3.19, -.258), .019, "ShaftBearings")


def fan_rotor(d):
    d.ring("Steel", (0, -.105, 0), .43, .17, .045, group="Hub", sides=12)
    for index in range(6):
        angle = index * 60
        geometry = bp.u_box((1.04, .055, 0), (1.02, .035, .12), 0)
        d.add("WornSteel" if index % 2 else "Rust", bp.u_rotated(geometry, (0, angle, 0)), "Blades")
        for distance in (.65, 1.34):
            geometry = d.rod_geometry((distance, .074, 0), (distance, .10, 0), .027, 6)
            d.add("WornSteel" if index % 2 else "Rust", bp.u_rotated(geometry, (0, angle, 0)), "Blades")


def lift_frame(d):
    for x in (-1.43, 1.43):
        for z in (-1.33, 1.33):
            d.box("Steel", (x, 4.45, z), (.20, 8.65, .035), "Uprights")
        for y in (.48, 3.1, 6.3, 8.52):
            d.box("Rust", (x, y, 0), (.16, .21, .30), "Crossbearers")
            d.rod("Steel", (x - .035, y, .08), (x + .035, y, .08), .045, "Uprights")
    for x in (-.65, .65):
        for z in (-.85, .85):
            d.ring("Rust", (x, 8.32, z), .265, .16, .075, "Z", "HoistDrums", sides=12)
    d.box("Steel", (0, 8.49, .72), (.67, .32, .57), "HoistDeck")
    for x in (-.24, 0, .24):
        d.box("Steel", (x, 8.48, 1.02), (.055, .27, .025), "HoistDeck")


def lift_platform(d):
    for x in (-.76, 0, .76):
        d.box("Steel", (x, -.04, 0), (.12, .10, 2.10), "Carriage")
    for x in (-.90, .90):
        for z in (-.85, .85):
            d.box("Rust", (x, .18, z), (.18, .12, .18), "CarriageLip")


def drum(d):
    # Everything on the barrel goes into the existing animated DrumRotor group.
    for x in (-.69, .69):
        d.ring("Steel", (x, 1.1, 0), .62, .53, .05, "X", "DrumRotor", sides=12)
    for x in (-.96, .96):
        d.box("Steel", (x, 1.10, 0), (.27, .36, .36), "Supports")
        d.ring("WornSteel", (x, 1.10, 0), .20, .12, .21, "X", "Bearings", sides=10)
        for z in (-.13, .13):
            d.rod("WornSteel", (x - .14, .97, z), (x + .14, .97, z), .027, "Bearings")
        d.box("Steel", (x, .82, 0), (.23, .05, .43), "Supports")
    for x in (-1.31, 1.31):
        for y in (.35, 1.30, 2.35):
            d.box("Steel", (x, y, .60), (.17, .12, .13), "ChainGuides")
    d.box("Steel", (0, .31, -.72), (2.82, .09, .08), "Frame")
    for x in (-1.32, 1.32):
        d.beam("Steel", (x, .25, -.70), (x, .80, -.45), .07, .08, "Frame")


def pusher_frame(d):
    for x in (-.48, .48):
        d.ring("Rust", (x, .89, -.89), .11, .06, .08, "Z", "Cylinders")
        for dx in (-.07, .07):
            d.rod("WornSteel", (x + dx, .81, -1.25), (x + dx, .81, -1.17), .022, "Drive")
    for x in (-1.57, 1.57):
        for z in (-1.18, 1.18):
            d.box("Rust", (x, .34, z), (.23, .12, .19), "GuardPosts")
    # Fixed conduits hug the drive; no new sweep volume or physics is introduced.
    for x in (-.70, .70):
        d.rod("Steel", (x, .41, -.98), (x, .41, .55), .027, "LoadRails")
        d.rod("Steel", (x, .41, .55), (x, .72, .55), .027, "LoadRails")
    for y in (.63, .85, 1.08):
        d.box("WornSteel", (0, y, -1.267), (.48, .035, .025), "Drive")


def pusher_head(d):
    for x in (-.48, .48):
        d.ring("WornSteel", (x, .89, -.10), .064, .035, .07, "Z", "PushRods")
    for x in (-.60, .60):
        d.box("Steel", (x, .89, -.065), (.075, .90, .035), "PushPlate")
    for x in (-.45, .45):
        d.box("Timber", (x, .395, .32), (.11, .07, .44), "EndlessLoad")
    for x in (-.50, .50):
        d.box("Cardboard", (x, 1.49, .32), (.06, .07, .37), "EndlessLoad")


def lockers(d):
    for index in range(3):
        x = (index - 1) * .65
        for dx in (-.25, .25):
            for y in (.19, 1.68):
                d.rod("WornSteel", (x + dx, y, -.314), (x + dx, y, -.329), .017, "LockerDoors")
        for y in (.46, 1.39):
            d.box("Rust", (x - .285, y, -.317), (.035, .13, .028), "CorrodedHinge")
        d.box("WornSteel", (x + .21, .92, -.316), (.075, .24, .026), "LockerDoors")
        d.box("Steel", (x, 1.79, -.12), (.55, .065, .25), "LockerBodies")
    # Natural differences: extra door patches, spare peg caps, or a braced repair.
    for variant in range(3):
        group = f"RoomVariant{variant}"
        for index in range(3):
            x = (index - 1) * .65
            if variant == 0:
                d.box("WornSteel", (x, 1.32, -.327), (.23, .13, .018), group)
                d.rod("WornSteel", (x + .20, 1.12, -.32), (x + .20, 1.12, -.351), .025, group)
            elif variant == 1:
                d.box("WornSteel", (x, .59 + index * .13, -.327), (.34, .065, .02), group)
                d.box("WornSteel", (x - .18, 1.44, -.33), (.075, .19, .025), group)
            else:
                d.box("WornSteel", (x + .12, .57, -.328), (.14, .28, .022), group)
                d.box("WornSteel", (x, 1.52, -.328), (.43, .04, .022), group)


def break_room(d):
    for x in (-.79, .79):
        d.beam("Steel", (x, .28, .065), (x, .28, .49), .05, .05, "TableLegs")
    d.box("Steel", (0, .35, .29), (1.6, .08, .055), "TableLegs")
    for x in (-.58, .58):
        for dx in (-.14, .14):
            d.box("Steel", (x + dx, .24, -.44), (.035, .04, .30), "StoolLegs")
        for dx in (-.15, .15):
            d.rod("Steel", (x + dx, .499, -.44), (x + dx, .509, -.44), .013, "TinCup")
    # Empty wrappers, folded tray or a compressed stack stay on the table.
    for variant in range(3):
        group = f"RoomVariant{variant}"
        if variant == 0:
            for x, z in ((-.62, .19), (.06, .43)):
                d.box("Cardboard", (x, .85, z), (.24, .025, .17), group)
                d.box("Cardboard", (x, .876, z + .065), (.24, .045, .025), group)
        elif variant == 1:
            d.box("Cardboard", (-.66, .85, .36), (.27, .025, .23), group)
            for z in (.25, .47):
                d.box("Cardboard", (-.66, .878, z), (.27, .06, .022), group)
            d.box("Cardboard", (-.79, .875, .36), (.022, .055, .21), group)
        else:
            for layer in range(3):
                d.box("Cardboard", (-.64 + layer * .025, .849 + layer * .025, .35),
                      (.25, .02, .21), group)
            d.box("Cardboard", (.04, .851, .39), (.21, .03, .18), group)


def technical_room(d):
    for x, width, y, height in ((-.57, .68, .88, 1.76), (.39, .92, .65, 1.3)):
        for edge in (-1, 1):
            for yy in (y - height * .35, y + height * .35):
                d.rod("WornSteel", (x + edge * (width * .5 - .08), yy, -.166),
                      (x + edge * (width * .5 - .08), yy, -.185), .020, "ControlDoors")
        for yy in (y - height * .28, y + height * .28):
            d.box("Rust", (x - width * .5 + .04, yy, -.17), (.055, .15, .045), "Handles")
        d.box("Steel", (x, .17, .39), (width * .62, .19, .13), "ControlBodies")
        for dx in (-.14, .14):
            d.rod("Steel", (x + dx, .16, .39), (x + dx, .47, .39), .022, "ControlBodies")
    for x in (-.93, .88):
        for y in (.48, 1.08, 1.69):
            d.box("Steel", (x, y, .43), (.12, .06, .13), "PipeFeed")
    for variant in range(3):
        group = f"RoomVariant{variant}"
        if variant == 0:
            for x in (-.72, -.43):
                d.ring("Steel", (x, 1.40, -.19), .083, .056, .035, "Z", group)
            for x in (.16, .39, .62):
                d.rod("Steel", (x, .98, -.17), (x, .98, -.23), .035, group)
        elif variant == 1:
            d.box("Steel", (-.57, 1.43, -.19), (.44, .14, .035), group)
            for y in (.86, 1.02, 1.18):
                d.rod("Steel", (-.57, y, -.17), (-.57, y + .04, -.235), .031, group)
            d.ring("Steel", (.39, 1.06, -.19), .10, .068, .035, "Z", group)
        else:
            for x in (-.73, -.41):
                d.box("Steel", (x, 1.35, -.195), (.11, .35, .04), group)
                d.rod("Steel", (x, 1.34, -.22), (x, 1.40, -.255), .027, group)
            d.box("Steel", (.39, 1.08, -.19), (.52, .13, .035), group)


DETAILS = {"Shell": shell, "Platform": platform, "Stair": stair, "Rail": rail,
           "Rack": rack, "Cargo": cargo, "Pipes": pipes, "RoomShell": room,
           "MainDoor": door, "Gate": gate, "FanRotor": fan_rotor,
           "LiftFrame": lift_frame, "LiftPlatform": lift_platform, "ChainDrum": drum,
           "PusherFrame": pusher_frame, "PusherHead": pusher_head,
           "LockerProps": lockers, "BreakProps": break_room, "TechnicalProps": technical_room}


def enrich_items(items, rod_geometry, ring_geometry):
    for item in items:
        if item.name not in DETAILS:
            continue
        detail = Detail(item, rod_geometry, ring_geometry)
        DETAILS[item.name](detail)
        detail.finish()
    return items
