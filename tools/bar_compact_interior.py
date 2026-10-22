"""Fixed-metre furnishings for the pub's actual ground-floor range.

Furniture is rebuilt at ordinary human dimensions. No old hall-wide geometry
is scaled down: benches retain seat depth/height and the counter keeps its
service height, with fewer repeated joinery bays and a small music corner.
"""
from __future__ import annotations

import bar_parts as bp
import interior_kit as kit


def stage_point(point):
    """Quarter-turn the whole karaoke assembly toward the entrance side."""
    x, y, z = point
    return 4.525 - (z + 2.825), y, -2.7 + (x - 4.525)


def build(g, asset, materials):
    def part(name, geometry, role, tint=None, **kwargs):
        if role == "stage" or role.startswith("stage_"):
            vertices, faces = geometry
            geometry = [stage_point(vertex) for vertex in vertices], faces
            if "colliders" in kwargs:
                kwargs["colliders"] = [(stage_point(center), (size[2], size[1], size[0]))
                    for center, size in kwargs["colliders"]]
        return g.add_part(asset, materials, name, geometry, role,
                          tint or bp.ident("DarkWoodTint"), **kwargs)

    def box(name, position, size, role, tint=None, solid=False, **kwargs):
        return part(name, bp.u_box(position, size, 0.008), role, tint,
                    colliders=[(position, size)] if solid else [], **kwargs)

    # Lower cabinet fronts, recessed shelves, individual framed mirrors and
    # exposed cabinet handles keep detail visible at the shorter room scale.
    box("Backbar Cabinet", (0.425, .65, 3.475), (6.85, 1.3, .45),
        "backbar", bp.ident("CounterWoodTint"), solid=True, sheet="DarkWood")
    mirror_panels, mirror_frames, lattice = [], [], []
    for x in (-2.25, -.9, .45, 1.8, 3.15):
        mirror_panels.append(bp.u_box((x, 2.02, 3.635), (1.19, 1.30, .045), .006))
        for dx in (-.63, .63):
            mirror_frames.append(bp.u_box((x + dx, 2.02, 3.59), (.065, 1.4, .075), .004))
        for y in (1.35, 2.69):
            mirror_frames.append(bp.u_box((x, y, 3.59), (1.32, .065, .075), .004))
        for pitch in (-34, 34):
            lattice.append(kit.translated(bp.u_rotated(
                bp.u_box((0, 0, 0), (.88, .022, .026), .003),
                (0, 0, pitch)), (x, 2.02, 3.56)))
    part("Backbar Mirror Panels", kit.merge_all(mirror_panels), "backbar_mirror", bp.ident("GlassTint"), shadows=False)
    part("Backbar Mirror Frames", kit.merge_all(mirror_frames), "backbar_mirror_frame", sheet="DarkWood")
    part("Backbar Patterned Glass Lattice", kit.merge_all(lattice), "backbar_patterned_glass", bp.ident("MetalTint"), shadows=False)
    shelves = [bp.u_box((.425, y, 3.495), (6.5, .10, .36), .008) for y in (1.48, 2.10)]
    for x in (-2.86, -1.5, -.14, 1.22, 2.58, 3.66):
        shelves.append(bp.u_box((x, 2.0, 3.52), (.065, 1.35, .30), .004))
    part("Backbar Shelves", kit.merge_all(shelves), "backbar_shelf", bp.ident("MetalTint"), shadows=False)
    handles, panels = [], []
    for index in range(9):
        x = -2.65 + index * .76
        panels.append(bp.u_box((x, .65, 3.238), (.66, 1.08, .035), .006))
        handles.append(bp.u_box((x + .21, .93, 3.205), (.10, .025, .025), .004))
    part("Backbar Cabinet Panels", kit.merge_all(panels), "backbar_panel", bp.ident("WoodTint"), sheet="DarkWood", shadows=False)
    part("Backbar Cabinet Handles", kit.merge_all(handles), "backbar_handle", bp.ident("MetalTint"), sheet="AgedBrass", shadows=False)
    rack = [bp.u_box((x, 2.69, 3.30), (.025, .035, .40), .003) for x in (-1.6, -.8, 0, .8, 1.6, 2.4)]
    rack += [bp.u_box((.425, 2.69, z), (5.1, .035, .025), .003) for z in (3.10, 3.49)]
    part("Backbar Hanging Glass Rack", kit.merge_all(rack), "backbar_glass_rack", bp.ident("MetalTint"), shadows=False)
    box("Backbar Crown", (.425, 2.81, 3.53), (6.85, .18, .35), "backbar_crown", sheet="DarkWood")
    box("Backbar Amber Sign", (.425, 2.80, 3.33), (2.2, .07, .035), "sign_glow", bp.ident("SignGlowColor"), emissive=True, shadows=False)
    bottles = [[], [], [], []]
    for row, shelf in enumerate((1.55, 2.17)):
        for index in range(18):
            x = -2.74 + index * .37
            if row == 0 and -1.9 < x < .55:
                continue  # The four service bottles own this shelf opening.
            height = .23 + ((index * 7 + row * 3) % 4) * .025
            bottles[(index + row) % 4].append(kit.merge(
                bp.u_box((x, shelf + height * .34, 3.395), (.12, height * .68, .12), .006),
                bp.u_tapered_cylinder((x, shelf + height * .84, 3.395), (.055, height * .16, .055), .66)))
    for index, batch in enumerate(bottles):
        part(f"Bottle Silhouettes {index + 1}", kit.merge_all(batch), "bottle", bp.rgb(.18 + index * .07, .22, .12), shadows=False)

    dividers, glass = [], []
    for index, z in enumerate(g.BOOTH_ZS, 1):
        for side, sign in (("Near", -1), ("Far", 1)):
            box(f"Booth Base {index} {side}", (-4.38, .20, z + sign * .97), (1.72, .40, .62), "booth_base", bp.ident("CounterWoodTint"), solid=True, sheet="DarkWood")
            box(f"Booth Cushion {index} {side}", (-4.38, .435, z + sign * .95), (1.64, .09, .60), "booth_cushion", bp.ident("UpholsteryTint"), sheet="WornLeather")
            box(f"Booth Back {index} {side}", (-4.38, .90, z + sign * 1.31), (1.80, .95, .12), "booth_back", bp.ident("UpholsteryTint"), solid=True, sheet="WornLeather")
        box(f"Booth Table Top {index}", (-4.38, .79, z), (1.05, .08, .85), "booth_table", bp.ident("MetalTint"), solid=True, sheet="PolishedWood")
        part(f"Booth Table Leg {index}", bp.u_cylinder((-4.38, .375, z), (.12, .375, .12)), "booth_table_leg", colliders=[((-4.38, .375, z), (.12, .75, .12))], sheet="DarkWood")
    for z in (-3.55, -.75, 2.05):
        for x in (-5.36, -3.4):
            dividers.append(bp.u_box((x, .90, z), (.065, 1.8, .07), .004))
        for y in (.55, 1.75):
            dividers.append(bp.u_box((-4.38, y, z), (2.0, .065, .07), .004))
        dividers.append(bp.u_box((-4.38, .30, z), (1.95, .50, .055), .005))
        for x in (-5.02, -4.38, -3.74):
            glass.append(bp.u_box((x, 1.16, z), (.58, 1.06, .035), .004))
    part("Snug Divider Timber", kit.merge_all(dividers), "snug_divider_timber", sheet="DarkWood")
    part("Snug Divider Patterned Glass", kit.merge_all(glass), "snug_divider_glass", bp.ident("GlassTint"), shadows=False)

    # Extend the low, walkable floor into the former coat-rack pocket.
    # Coordinates here precede stage_point's quarter turn.
    box("Small Stage", (4.525, .06, -2.4), (1.65, .12, 2.1), "stage", bp.ident("CounterWoodTint"), solid=True, sheet="WornPlank")
    for label, x in (("Left", 3.82), ("Right", 5.22)):
        folds = [bp.u_box((x + dx, 1.43, -3.51 + dx * .4), (.10, 2.6, .14), .006) for dx in (-.06, 0, .06)]
        part(f"Stage {label} Curtain", kit.merge_all(folds), "stage_curtain", bp.ident("UpholsteryTint"), sheet="WornFabric")
        box(f"Stage {label} Speaker", (x, .50, -3.1), (.35, .76, .32), "stage_speaker", bp.rgb(.035, .035, .04), solid=True)
    box("Stage Valance", (4.525, 2.74, -3.51), (1.65, .20, .17), "stage_curtain", bp.ident("UpholsteryTint"), sheet="WornFabric")
    part("Stage Microphone Stand", bp.u_cylinder((4.525, .84, -2.05), (.03, .72, .03)), "stage_mic", bp.ident("MetalTint"))
    part("Stage Microphone", bp.u_tapered_cylinder((4.525, 1.6, -2.05), (.06, .07, .06), .7), "stage_mic", bp.rgb(.06, .06, .065))

    # A continuous central carpet plus booth strip: the runtime footstep
    # overlay uses these same physical rectangles, never renderer AABBs.
    carpet_rects = ((0, .018, -1.42, 6.6, .035, 4.15), (-4.38, .018, -.75, 2.04, .035, 5.6))
    part("Pub Carpet Fields", kit.merge_all([bp.u_box(v[:3], v[3:], .006) for v in carpet_rects]), "pub_carpet", bp.rgb(.22, .055, .052), shadows=False)
    border = []
    for x, y, z, w, h, d in carpet_rects:
        for side in (-1, 1):
            border += [bp.u_box((x + side * (w * .5 - .025), .04, z), (.045, .012, d), .003), bp.u_box((x, .04, z + side * (d * .5 - .025)), (w, .012, .045), .003)]
    part("Pub Carpet Borders", kit.merge_all(border), "pub_carpet_border", bp.rgb(.36, .27, .10), shadows=False)
    motifs = [kit.translated(bp.u_rotated(bp.u_box((0, 0, 0), (.15, .012, .15), .003), (0, 45, 0)), (x, .045, z)) for x in (-2.6, -1.3, 0, 1.3, 2.6) for z in (-3, -2, -1, 0)]
    part("Pub Carpet Diamond Motifs", kit.merge_all(motifs), "pub_carpet_pattern", bp.rgb(.42, .31, .12), shadows=False)
    box("Activity Bay Rug", (4.5, .018, -.65), (1.8, .035, 2.4), "bay_rug", bp.rgb(.16, .065, .055), shadows=False)
    edges = [bp.u_box((x, .04, -.65), (.04, .012, 2.4), .003) for x in (3.62, 5.38)] + [bp.u_box((4.5, .04, z), (1.8, .012, .04), .003) for z in (-1.83, .53)]
    part("Activity Bay Border", kit.merge_all(edges), "bay_border", bp.rgb(.36, .27, .10), shadows=False)
    for index, (x, z) in enumerate(g.PUB_TABLES, 1):
        part(f"Social Pub Table {index} Leg", kit.merge(bp.u_cylinder((x, .35, z), (.10, .35, .10)), bp.u_cylinder((x, .04, z), (.38, .04, .38))), "table_leg", sheet="DarkWood", colliders=[((x, .35, z), (.1, .7, .1))])
        part(f"Social Pub Table {index}", bp.u_cylinder((x, .78, z), (.65, .04, .65), sides=16), "table_top", bp.ident("WoodTint"), sheet="PolishedWood", colliders=[((x, .78, z), (.65, .08, .65))])
        # Modest ordinary tableware adds use, without new clues or text.
        part(f"Pub Table {index} Coaster", bp.u_cylinder((x + .15, .825, z + .10), (.11, .003, .11), sides=12), "table_coaster", bp.rgb(.42, .31, .18), sheet="Paper", shadows=False)

    box("Service Door", (4.92, 1.17, 3.71), (1.02, 2.34, .08), "service_door", bp.ident("GlassTint"))
    box("Service Door Frame", (4.92, 2.38, 3.66), (1.15, .09, .14), "service_door_frame", bp.ident("MetalTint"))
    for name, z, tint in (("Burgundy Poster", -1.7, bp.ident("SignAccentColor")), ("Teal Poster", 2.0, bp.ident("GlassTint"))):
        box(name + " Frame", (5.48, 1.9, z), (.065, .80, .62), "poster_frame", bp.ident("WoodTint"), shadows=False)
        box(name, (5.435, 1.9, z), (.025, .68, .50), "poster", tint, shadows=False)
    box("Entrance Notice Frame", (-2.4, 1.95, -3.71), (.60, .68, .05), "poster_frame", bp.ident("WoodTint"), shadows=False)
    box("Entrance Notice", (-2.4, 1.95, -3.67), (.50, .58, .025), "poster", bp.ident("SignAccentColor"), shadows=False)
    box("Exit Header Frame", (0, 2.66, -3.72), (1.64, .22, .12), "exit_header_frame", sheet="DarkWood", shadows=False)
    box("Exit Header", (0, 2.66, -3.64), (1.40, .06, .025), "exit_header", bp.ident("SignGlowColor", .58), emissive=True, shadows=False)

    # Retained district alternatives share the same bounded wall footprint.
    for mood, tint in (("Memory", bp.ident("SignAccentColor")), ("Household", bp.ident("UpholsteryTint")), ("AfterShift", bp.ident("MetalTint")), ("Escape", bp.ident("PendantColor"))):
        box(f"{mood} Wall Field", (5.47, 1.9, .4), (.045, 1.25, 1.20), "district_field", tint, group="district:" + mood, sheet="WornFabric" if mood == "Household" else "Wallpaper", shadows=False)
        pleats = [bp.u_box((5.43, 1.9, .4 + offset), (.035, 1.16, .045), .003) for offset in (-.48, -.24, 0, .24, .48)]
        part(f"{mood} Wall Detail", kit.merge_all(pleats), "district_cards", bp.ident("WallPanelTint"), group="district:" + mood, sheet="DarkWood", shadows=False)

    # Only BeerPong has a physical activity table. The three-vessel consoles
    # are removed; their former nook now holds the inward-facing jukebox.
    group = "activity:BeerPong"
    rect = g.ACTIVITY_RECTS["BeerPong"]
    x, z = g.rect_center(rect)
    box("Beer Pong Table", (x, .96, z), (rect[2], .08, rect[3]), "activity_console_top", bp.rgb(.10, .24, .22), solid=True, group=group)
    legs = [bp.u_box((x + dx, .46, z + dz), (.055, .92, .055), .004) for dx in (-.28, .28) for dz in (-.70, .70)]
    part("Beer Pong Table Legs", kit.merge_all(legs), "activity_console", bp.ident("MetalTint"), sheet="PaintedMetal", group=group)
    for end in (-1, 1):
        cups = [bp.u_tapered_cylinder((x + dx, 1.06, z + end * .60), (.07, .06, .07), .75) for dx in (-.12, 0, .12)]
        part(f"Beer Pong Cups {end}", kit.merge_all(cups), "activity_prop", bp.rgb(.60, .12, .08), group=group)


def add_frame_anchors(g, asset):
    """A shared exterior frame, entrance and measured shell contour."""
    g.add_anchor(asset, "ExteriorDoor", "exterior_door", (0, 0, -3.9),
                 unity_right=(0, 0, -1), unity_forward=(1, 0, 0))
    for name, x, z in (("FrontLeft", -5.85, -4.05), ("FrontRight", 5.85, -4.05),
                       ("RearLeft", -5.85, 4.05), ("RearRight", 5.85, 4.05)):
        g.add_anchor(asset, "RoomContour" + name, "room_contour", (x, 0, z))
    g.add_anchor(asset, "FirstFloorUnderside", "first_floor_underside", (0, 3.28, 0))


def containment_contract():
    return {"exterior_door_m": [-.16, 0, 0],
            "room_door_m": [0, 0, -3.9],
            "room_to_exterior_rotation_y_degrees": -90,
            "room_to_exterior_translation_m": [-4.06, 0, 0],
            "main_range_polygon_xz_m": [[-8.58, -6], [0, -6], [0, 6], [-8.58, 6]],
            "first_floor_underside_m": 3.28,
            "shell_height_m": 3.20}
