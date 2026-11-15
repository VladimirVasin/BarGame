#!/usr/bin/env python3
"""Fixed-metre industrial shells with separate staff and loading approaches."""

from __future__ import annotations

from city_building_parts import (
    FacadeAttachmentBounds, Geometry, PartSpec, PrototypeSpec,
    box, coastal_prototype, combine, cylinder_z,
    facade_bounds, facade_prism, gable_roof, merge, parts_for, shed_roof,
    slots_for_grid, trim_box, window_geometry,
)

PURPOSES = ("DryWarehouse", "ColdStorage", "RepairWorkshop", "MetalWorkshop",
            "PackagingWorkshop", "BoilerHouse", "CompressorHouse", "RepairGarage")
OPTIONAL_ROLES = {"CargoTimber": 1.6, "EquipmentConcrete": 2.0,
                  "EquipmentSteel": 1.8, "TaskLightLens": 1.0}

# Metres, mirrored by CityIndustrialEntranceGeometry. The side bay is out
# of the line of sight from the staff opening in the facade.
ENTRANCE_X = (2.5, 1.4, 2.8, 2.85, 1.55, 1.16, 2.34, -.90)
ENTRANCE_HALF_WIDTH = .70
ENTRANCE_HEIGHT = 2.4
ENTRANCE_TURN_FRONT = 2.50
ENTRANCE_DEPTH = 4.15
ENTRANCE_BAY_RIGHT = 3.20


def entrance_solids(solids, x, front):
    left, right = x - ENTRANCE_HALF_WIDTH, x + ENTRANCE_HALF_WIDTH
    bay_right = x + ENTRANCE_BAY_RIGHT
    back = front - ENTRANCE_DEPTH
    turn = front - ENTRANCE_TURN_FRONT
    result = []
    for low, high in solids:
        if high[0] <= left or low[0] >= bay_right or high[1] <= back or low[2] >= ENTRANCE_HEIGHT:
            result.append((low, high))
            continue
        assert low[0] < left < right < bay_right < high[0] and low[1] < back < turn < high[1]
        a, b, c = low
        d, e, f = high
        result.extend((((a, b, c), (left, e, f)),
                       ((bay_right, b, c), (d, e, f)),
                       ((left, b, c), (bay_right, back, f)),
                       ((right, turn, c), (bay_right, e, f)),
                       ((left, back, ENTRANCE_HEIGHT), (right, e, f)),
                       ((right, back, ENTRANCE_HEIGHT), (bay_right, turn, f))))
    return tuple(result)


def transform(geometry: Geometry, fn) -> Geometry:
    return Geometry(tuple(fn(*vertex) for vertex in geometry.vertices),
                    geometry.faces, geometry.face_slot_ids)


def tube_y(x: float, y: float, z: float, radius: float, length: float,
           sides: int = 10) -> Geometry:
    # Rotation, rather than a handedness-changing swap, preserves winding.
    return transform(cylinder_z((0, 0), -length * .5, length * .5, radius, sides),
                     lambda a, b, c: (x + a, y - c, z + b))


def slatted_pallet(x: float, y: float, bottom: float = .48,
                   width: float = 1.15, depth: float = .82) -> Geometry:
    pieces = [box((x + dx, y, bottom + .08), (.12, depth, .16))
              for dx in (-width * .38, 0, width * .38)]
    pieces.extend(box((x, y + (index - 2) * depth * .195, bottom + .20),
                      (width, depth * .155, .08)) for index in range(5))
    return merge(pieces)


def crate(x: float, y: float, bottom: float, width: float = .94,
          depth: float = .68, height: float = .76) -> Geometry:
    pieces = [box((x, y, bottom + .055), (width, depth, .11))]
    for level in range(3):
        z = bottom + .22 + level * (height - .28) * .5
        for edge_y in (-depth * .5 + .035, depth * .5 - .035):
            pieces.append(box((x, y + edge_y, z), (width, .07, .13)))
        for edge_x in (-width * .5 + .035, width * .5 - .035):
            pieces.append(box((x + edge_x, y, z + .015), (.07, depth - .14, .13)))
    return merge(pieces)


def shutter(x: float, wall_y: float, bottom: float, width: float,
            height: float, split: bool = False, shallow: bool = False) -> Geometry:
    # 55 mm door relief and 95 mm guides avoid nearly coincident wall skins.
    scale = .28 if shallow else 1.
    pieces = [box((x, wall_y + .055 * scale, bottom + height * .5),
                  (width, .11 * scale, height))]
    for edge in (-1, 1):
        pieces.append(box((x + edge * (width * .5 + .09), wall_y + .125 * scale,
                           bottom + height * .5), (.14, .17 * scale, height)))
    pieces.append(box((x, wall_y + .15 * scale, bottom + height + .12),
                      (width + .38, .20 * scale, .16)))
    if split:
        pieces.append(box((x, wall_y + (.062 if shallow else .145), bottom + height * .5),
                          (.075, .010 if shallow else .07, height - .08)))
    else:
        for index in range(1, int(height / .42)):
            pieces.append(box((x, wall_y + .13 * scale, bottom + index * .42),
                              (width - .10, .04 * scale, .055)))
    return merge(pieces)


def louvre(x: float, wall_y: float, z: float, width: float,
           height: float) -> Geometry:
    pieces = [box((x, wall_y + .07, z), (width + .16, .14, height + .16))]
    count = max(3, int(height / .22))
    pieces.extend(box((x, wall_y + .175, z - height * .5 + (i + .5) * height / count),
                      (width, .07, .075)) for i in range(count))
    return merge(pieces)


def equipment_bench(x: float, y: float, bottom: float = .48) -> Geometry:
    pieces = [box((x, y, bottom + .83), (1.75, .66, .12)),
              box((x, y, bottom + .25), (1.55, .52, .08))]
    pieces.extend(box((x + dx, y + dy, bottom + .40), (.07, .07, .80))
                  for dx in (-.72, .72) for dy in (-.23, .23))
    return merge(pieces)


def industrial_prototype(variant: int) -> PrototypeSpec:
    if not 1 <= variant <= 8:
        raise ValueError("Industrial variant must be 1..8")
    purpose = PURPOSES[variant - 1]
    stable_id = f"industrial-prototype-{variant:02d}"
    if variant <= 5:
        old = coastal_prototype("Industrial", variant)
        width, depth, height = old.frontage_width_m, old.depth_m, old.height_m
        solids = old.collision_bounds
        grammar = old.grammar
        roof_low, roof_high = old.roof_attachment_bounds_min_source, old.roof_attachment_bounds_max_source
    else:
        width, depth, height = ((14., 13.5, 8.4) if variant == 6 else
                               (14., 13.5, 6.6) if variant == 7 else (15.5, 11.5, 7.8))
        body_top = height - 1.2
        solids = (((-width * .5, -depth * .5, 0.), (width * .5, depth * .5, body_top)),)
        grammar = "LowWideProcess" if variant in (6, 7) else "LongStreetBar"
        roof_low = (-width * .5 + .4, -depth * .5 + .4, body_top)
        roof_high = (width * .5 - .4, depth * .5 - .4, height)
    hw, hd = width * .5, depth * .5
    body_top = height - (2. if variant == 1 else 1.2)
    front = hd - 1.18
    entry_x = ENTRANCE_X[variant - 1]
    opening = .79
    bay_edge = .95
    cut_head = 2.51
    primary, secondary, metal, concrete, steel, timber = [], [], [], [], [], []

    def recessed_outline(left, right, pier):
        return ((left, -hd), (right, -hd), (right, hd), (right - pier, hd),
                (right - pier, front), (left + pier, front), (left + pier, hd), (left, hd))

    def staff_bay(outline, wall, top):
        def with_entrance(clear):
            result = []
            for i, point in enumerate(outline):
                following = outline[(i + 1) % len(outline)]
                result.append(point)
                if point[1] != wall or following[1] != wall or not point[0] > entry_x > following[0]:
                    continue
                if wall < hd:
                    result.extend(((entry_x + bay_edge, wall), (entry_x + bay_edge, hd)))
                if clear:
                    result.extend((
                        (entry_x + opening, hd),
                        (entry_x + opening, hd - ENTRANCE_TURN_FRONT + .04),
                        (entry_x + ENTRANCE_BAY_RIGHT + .04, hd - ENTRANCE_TURN_FRONT + .04),
                        (entry_x + ENTRANCE_BAY_RIGHT + .04, hd - ENTRANCE_DEPTH - .04),
                        (entry_x - opening, hd - ENTRANCE_DEPTH - .04),
                        (entry_x - opening, hd)))
                if wall < hd:
                    result.extend(((entry_x - bay_edge, hd), (entry_x - bay_edge, wall)))
            return tuple(result)

        primary.append(facade_prism(with_entrance(True), 0., cut_head))
        primary.append(facade_prism(with_entrance(False), cut_head, top))
        # Independent steel jambs and lintel project into the wider masonry opening.
        for sign in (-1, 1):
            steel.append(trim_box((entry_x + sign * .766, hd - .105, 1.242),
                                  (.132, .205, 2.484), .012))
        steel.append(trim_box((entry_x, hd - .107, 2.47), (1.646, .206, .12), .012))
        floor = ((entry_x - opening, hd - ENTRANCE_DEPTH - .04),
                 (entry_x + ENTRANCE_BAY_RIGHT + .04, hd - ENTRANCE_DEPTH - .04),
                 (entry_x + ENTRANCE_BAY_RIGHT + .04, hd - ENTRANCE_TURN_FRONT + .04),
                 (entry_x + opening, hd - ENTRANCE_TURN_FRONT + .04),
                 (entry_x + opening, hd),
                 (entry_x - opening, hd))
        concrete.append(Geometry(tuple((x, y, 0.) for x, y in floor), (tuple(range(len(floor))),), (0,)))

    def apron(left, right):
        # Loading stays on its raised deck. The staff opening has a separate
        # floor at the lot datum and a relief joint at either side.
        for start, end in ((left, min(right, entry_x - bay_edge - .02)),
                           (max(left, entry_x + bay_edge + .02), right)):
            if end - start > .08:
                concrete.append(trim_box(((start + end) * .5, hd - .59, .24),
                                         (end - start, 1.06, .48)))

    if variant == 4:
        primary.append(facade_prism(recessed_outline(-hw, -1.8, .42), 0., 4.2))
        staff_bay(recessed_outline(1.8, hw, .09), front, 4.2)
        apron(-hw + .08, -1.88)
        apron(1.88, hw - .08)
        primary.append(facade_prism(((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)), 4.2, body_top))
    elif variant == 3:
        polygon = ((-hw, -hd), (-hw + 5., -hd), (-hw + 5., 0.),
                   (hw, 0.), (hw, hd), (-hw, hd))
        staff_bay(polygon, hd, body_top)
        concrete.append(trim_box((2.2, -1.0, .16), (5.8, 1.8, .32)))
    else:
        staff_bay(recessed_outline(-hw, hw, .58), front, body_top)
        apron(-hw + .66, hw - .66)
    secondary.append(trim_box((0, hd - .05, body_top - .25), (width - .12, .18, .38)))
    plinth = trim_box((0, -hd + .04, .29), (width - .14 if variant != 3 else 4.86, .16, .58))
    if variant == 3:
        plinth = trim_box((-hw + 2.5, -hd + .04, .29), (4.86, .16, .58))
    slots = []
    def windows(side, fixed, bays, levels, size):
        slots.extend(slots_for_grid(len(slots) + 1, side, fixed, bays, levels, size))
    def gate(x, gate_width, gate_height, split=False, wall=front):
        steel.append(shutter(x, wall, .5 if variant != 3 else .06,
                             gate_width, gate_height, split, wall == hd))
    if variant == 1:
        roof = gable_roof((0, 0), width, depth, body_top, height)
        gate(-1.45, 4.6, 3.8)
        steel.append(box((-1.45, hd - .45, 4.70), (5.1, .85, .13)))
        timber.extend((slatted_pallet(4.4, hd - .62), crate(4.4, hd - .62, .73),
                       slatted_pallet(-5.1, hd - .62), slatted_pallet(-5.1, hd - .62, .76)))
        windows("Front", front, (2.5, 4.8), (5.45,), (1.35, .86))
        windows("Rear", -hd, (-4.7, 4.7), (5.45,), (1.15, .86))
    elif variant == 2:
        roof = trim_box((0, 0, body_top + .20), (width, depth, .40))
        gate(-2.5, 3.35, 4.25, True)
        steel.extend((box((-2.5, front + .25, 4.98), (4.1, .24, .34)),
                      louvre(4.5, front, 2.65, 2.65, 1.9)))
        # Roof condensers, fan guards, paired service lines, and insulated door
        # perimeter carry the refrigeration purpose without labels.
        for x in (-3.5, 2.0):
            steel.append(box((x, -2., body_top + .73), (3.4, 1.7, .66)))
            for dx in (-.8, .8):
                steel.append(cylinder_z((x + dx, -2.), body_top + 1.06, height, .53, 12))
        for z in (1.0, 1.45):
            metal.append(box((4.8, front + .17, z), (3.5, .13, .13)))
        windows("Front", hd, (1.4,), (3.05,), (.8, .8))
        windows("Rear", -hd, (-5.7,), (4.9,), (.85, 1.05))
    elif variant == 3:
        roof = combine(gable_roof((0, hd * .5), width, hd, body_top, height),
                       shed_roof((-hw + 2.5, -hd * .5), 5., hd, body_top,
                                 height - .35, False))
        gate(-3.3, 3.3, 3.8, True, hd)
        steel.append(equipment_bench(2.2, -1.0, .32))
        steel.extend((tube_y(2.1, -.95, 1.55, .34, .50),
                      box((2.1, -.95, 1.24), (.84, .66, .08))))
        timber.append(slatted_pallet(-.8, -2.6, .03))
        # Repair stock is sorted in the L-court, leaving the rear throat open.
        for x in (-1.3, -.65, 0.):
            steel.append(tube_y(x, -2.6, .49, .18, .64))
        windows("Front", hd, (1.0, 3.6, 6.0), (5.8,), (1.55, 1.18))
        windows("Rear", -hd, (-5.2,), (5.7,), (1.6, 1.2))
        windows("Left", -hw, (-3.6, 2.8), (5.7,), (1.35, 1.2))
    elif variant == 4:
        # Three long, asymmetric roof teeth make the fabrication hall distinct.
        roof = merge(shed_roof((-width / 3. + i * width / 3., 0.), width / 3.,
                    depth, body_top, height, True) for i in range(3))
        gate(-4.8, 3.1, 3.5, True)
        gate(5.65, 2.8, 3.5, True)
        for x in (-4.8, 5.65):
            steel.append(box((x, hd - .62, 1.1), (2.4, .64, .18)))
            for dx in (-.8, .8):
                steel.append(box((x + dx, hd - .62, .75), (.12, .55, .70)))
            for z in (1.27, 1.43, 1.59):
                steel.append(box((x, hd - .62, z), (2.1, .41, .10)))
        windows("Front", hd, (-4.8, 0., 4.8), (5.65,), (1.9, .95))
        windows("Rear", -hd, (-4.8, 4.8), (5.65,), (1.9, .95))
    elif variant == 5:
        roof = shed_roof((0, 0), width, depth, body_top, height, False)
        gate(-1.6, 3.7, 3.3, True)
        for x in (3.35,):
            timber.extend((slatted_pallet(x, hd - .62), crate(x, hd - .62, .73, 1.1),
                           crate(x, hd - .62, 1.53, 1.1)))
        windows("Front", front, (-3.65,), (3.75,), (.70, .8))
        windows("Rear", -hd, (-2.8, 2.8), (3.7,), (1.25, .85))
    elif variant == 6:
        roof = trim_box((0, 0, body_top + .15), (width, depth, .30))
        # A single restrained flue is the rare vertical among low masses.
        steel.extend((cylinder_z((-5.9, hd - .60), .48, height - .14, .45, 12),
                      cylinder_z((-5.9, hd - .60), height - .14, height, .53, 12),
                      louvre(-2.5, front, 2.55, 3.8, 2.2)))
        metal.append(box((-4.8, front + .35, 3.5), (2.0, .25, .25)))
        gate(3.5, 2.2, 3.0, True)
        for z in (1.15, 1.8):
            metal.append(box((-2.4, front + .23, z), (5.0, .25, .25)))
        steel.append(box((-4.9, front + .27, 1.48), (.24, .23, .91)))
        # Paired spare lagging shells are recognisable maintained stock.
        for x in (-5.0, -3.6):
            steel.append(tube_y(x, hd - .62, .88, .32, .68))
        windows("Front", front, (5.45,), (2.55,), (.80, 1.05))
        windows("Rear", -hd, (3.8,), (5.35,), (1.2, .9))
    elif variant == 7:
        roof = trim_box((0, 0, body_top + .19), (width, depth, .38))
        steel.append(louvre(-2.8, front, 2.8, 4.1, 2.8))
        gate(4.2, 1.6, 2.75, True)
        # Horizontal receiver and motor sit on a visible paired service skid.
        steel.append(tube_y(.8, hd - .62, 1.4, .56, .66, 12))
        for x in (.4, 1.2):
            steel.append(box((x, hd - .62, .75), (.12, .58, .52)))
        metal.extend((box((-.2, front + .31, 1.8), (1.5, .15, .15)),
                      box((-.9, front + .34, 2.3), (.15, .13, .95))))
        steel.append(box((-2.7, -2.4, body_top + .75), (3.6, 1.9, .74)))
        steel.append(cylinder_z((-2.7, -2.4), body_top + 1.12, height, .62, 12))
        windows("Front", front, (2.0,), (3.8,), (.75, .85))
        windows("Rear", -hd, (4.4,), (4.2,), (.85, .85))
    else:
        roof = merge(gable_roof((x, 0), width * .5, depth, body_top, height)
                     for x in (-width * .25, width * .25))
        gate(-3.8, 4.0, 3.8)
        gate(2.22, 3.7, 3.8, True)
        steel.append(equipment_bench(5.7, hd - .62))
        for x in (5.25, 5.9):
            steel.append(tube_y(x, hd - .62, 1.45, .31, .32, 12))
        steel.append(box((-6.5, hd - .62, .71), (.35, .65, .46)))
        windows("Front", front, (-3.8, 2.0), (5.7,), (2.1, .8))
        windows("Rear", -hd, (-4.9, .0, 4.9), (5.45,), (1.25, .9))
    # Visible utilities have connected routes. No decorative pipe forest.
    metal.extend((box((-hw + .14, hd + .02, body_top * .5), (.09, .10, body_top - .3)),
                  box((hw - .14, hd + .02, body_top * .5), (.09, .10, body_top - .3))))
    lamp_x = (-1.45 if variant == 1 else -2.5 if variant == 2 else
              -3.3 if variant == 3 else 0. if variant == 4 else
              -1.6 if variant == 5 else 3.5 if variant == 6 else
              4.2 if variant == 7 else -1.)
    lamp_z = min(body_top - .55, 5.15 if variant in (1, 2, 4, 6, 8) else 4.0)
    if variant == 3:
        lamp_z = 4.4
    lamp_y = hd - .06
    steel.extend((box((lamp_x, lamp_y - .11, lamp_z + .12), (.18, .22, .26)),
                  box((lamp_x, lamp_y, lamp_z), (.60, .25, .13))))
    # A distinct emitter on the fixture underside remains visible from ground
    # level. Its top seats 2 mm inside the housing; its front edge stays inset.
    lens = box((lamp_x, lamp_y + .053, lamp_z - .069), (.46, .13, .012))
    frame, glass = window_geometry(slots)
    role_geometry = {"FacadePrimary": merge(primary), "FacadeSecondary": merge(secondary),
                     "Plinth": plinth, "Roof": roof, "Metal": merge(metal),
                     "WindowFrame": frame, "WindowGlass": glass}
    parts = list(parts_for(stable_id, role_geometry, 1.8))
    for role, geometry in (("CargoTimber", merge(timber)), ("EquipmentConcrete", merge(concrete)),
                           ("EquipmentSteel", merge(steel)), ("TaskLightLens", lens)):
        if geometry.faces:
            parts.append(PartSpec(f"{stable_id}__{role}", role, role,
                                  "world_metre_projected", OPTIONAL_ROLES[role], geometry))
    attachments = list(facade_bounds(width, depth, height))
    attachments[0] = FacadeAttachmentBounds("Front", (-hw + .65, front - .2, .5),
                                          (hw - .65, front, body_top - .5))
    if variant == 3:
        attachments[0] = FacadeAttachmentBounds("Front", (-hw + .25, hd - .2, .5),
                                              (hw - .25, hd, body_top - .5))
        attachments[1] = FacadeAttachmentBounds("Rear", (-hw + .25, -hd, .5),
                                              (-hw + 4.75, -hd + .2, body_top - .5))
        attachments[3] = FacadeAttachmentBounds("Right", (hw - .2, .25, .5),
                                              (hw, hd - .25, body_top - .5))
    solids = entrance_solids(solids, entry_x, hd)
    return PrototypeSpec(stable_id, "Industrial", grammar, width, depth, height,
                         (0., hd, 0.), roof_low, roof_high, tuple(attachments), tuple(slots),
                         tuple(parts), (), solids, (lamp_x, lamp_y + .13, lamp_z - .09), purpose,
                         (entry_x, hd, 0.))
