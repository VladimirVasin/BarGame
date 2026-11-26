#!/usr/bin/env python3
"""Export City playground equipment as independent models with metric contacts."""
from dataclasses import replace
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("city_misc", ROOT / "tools/build-city-misc-3d-model.py")
misc = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = misc
spec.loader.exec_module(misc)

from playground_geometry import (BEAM_END, BEAM_TOP, RUNG_END, RUNG_Y, RUNGS,
                                 STEP_FIRST, STEP_LAST, STEP_TOP, SLIDE_END, SLIDE_STEPS, SLIDE_RAIL,
                                 SLIDE_DECK, SLIDE_PROFILE, BRIDGE_LANE, SEESAW_PIVOT, SEESAW_DOCK,
                                 SEESAW_SEAT, SEESAW_GRIP, SEESAW_FOOT, BALANCE_STEP_TOP)

MODELS = ROOT / "Assets/Resources/City/Playground"
MANIFEST = MODELS / "PlaygroundModules.json"
SOURCE = ROOT / "ArtSource/City/PlaygroundModules.blend"


def role(part, name, pitch, grain=False):
    return replace(part, mesh=name, authored_uvs=[
        (u / pitch, v / pitch) for u, v in misc.box_projected_uvs(part.geometry, grain_along_x=grain)
    ])


def slide_channel():
    # The folded sheet keeps a soft lip at both sides and an underside.
    section = ((-.33, -.026), (.33, -.026), (.33, .071), (.305, .074),
               (.27, .012), (.235, 0), (-.235, 0), (-.27, .012),
               (-.305, .074), (-.33, .071))
    vertices = [(x, z, y + rim) for z, y in SLIDE_PROFILE for x, rim in section]
    width = len(section)
    faces = [tuple(range(width))]
    for index in range(len(SLIDE_PROFILE) - 1):
        base = index * width
        for side in range(width):
            a, b = base + side, base + (side + 1) % width
            faces.append((a, a + width, b + width, b))
    faces.append(tuple(reversed(range(len(vertices) - width, len(vertices)))))
    return vertices, faces


def at_socket(part, z, y=0.):
    vertices, faces = part.geometry
    return replace(part, geometry=([(x, forward-z, height-y) for x, forward, height in vertices], faces))


def slide_modules():
    metal = "Residential_PaintedMetal"
    wood = "Masonry_Timber"
    stairs = misc.make_part("SlideStair", 0, wood, *(
        misc.recipe_box(0, top - .032, z, .66, .064, .27, chamfer=.018)
        for z, top in SLIDE_STEPS
    ))
    stair_frames = []
    stair_guards = []
    deck_guards = {}
    deck_posts = []
    (low_z, low_y), (high_z, high_y) = SLIDE_RAIL
    for side in (-1, 1):
        x = side * .34
        stair_frames.append(misc.recipe_tube((x, .025, .23), (x, .70, 1.29), .038))
        stair_guards.append(misc.recipe_tube((side * .29, low_y, low_z),
                                            (side * .29, high_y, high_z), .022))
        guards = deck_guards[side] = []
        for z, y in ((low_z, low_y), (high_z, high_y)):
            stair_frames.append(misc.recipe_tube((side * .29, -.09, z),
                                                 (side * .29, y, z), .028))
        for z in (1.30, 1.95):
            deck_posts.append(misc.recipe_tube((side * .36, -.12, z),
                                                (side * .36, SLIDE_DECK, z), .042))
            guards.append(misc.recipe_tube((x, SLIDE_DECK, z), (x, high_y, z), .023))
        for offset in (0., -.29):
            guards.append(misc.recipe_tube((x, high_y + offset, 1.30),
                                          (x, high_y + offset, 1.99), .024))
    stair_frame = misc.make_part("SlideStair", 0, metal, *stair_frames)
    deck = misc.make_part("SlideDeck", 0, wood,
                          misc.recipe_box(0, SLIDE_DECK - .045, 1.615, .80, .09, .63, chamfer=.023))
    stand = misc.make_part("SlideDeck", 0, metal, *deck_posts)
    def guard_module(name, rails):
        return [role(misc.make_part(name, 0, metal, *rails), "GuardPaint", 1.2)]

    rail_anchors = {"SlideRailIn": (0, low_y, low_z), "SlideRailOut": (0, high_y, high_z)}
    frame = misc.make_part("ClimbFrame", 0, metal, *stair_frames, *(
        misc.recipe_tube((-.34, top - .036, z), (.34, top - .036, z), .036)
        for z, top in SLIDE_STEPS
    ))
    bridge_width = BRIDGE_LANE - .80
    bridge = misc.make_part("DeckBridge", 0, wood,
                            misc.recipe_box(0, -.045, 0, bridge_width + .012, .09, .56, chamfer=.018))
    bridge_rails = []
    for z in (-.292, .292):
        for x in (-bridge_width / 2, bridge_width / 2):
            bridge_rails.append(misc.recipe_tube((x, -.06, z), (x, .72, z), .025))
        for y in (.43, .72):
            bridge_rails.append(misc.recipe_tube((-bridge_width/2-.06, y, z),
                                                 (bridge_width/2+.06, y, z), .024))
    backstop = misc.make_part("DeckBridgeBackstop", 0, metal, *(
        [misc.recipe_tube((x, -.07, 0), (x, .72, 0), .022) for x in (-.34, .34)] +
        [misc.recipe_tube((-.34, y, 0), (.34, y, 0), .022) for y in (.43, .72)]
    ))
    surface = slide_channel()
    if misc.signed_volume(surface) <= 0.:
        raise ValueError("Slide channel has inward-facing surfaces")
    slide = misc.make_part("SlideChute", 0, "Street_PaintedMetal", surface)
    slide_supports = misc.make_part("SlideChute", 0, metal, *(
        misc.recipe_tube((side * .27, -.10, z), (side * .27, y-.026, z), .027)
        for side in (-1, 1) for z, y in (SLIDE_PROFILE[3], SLIDE_PROFILE[-2])
    ))
    return {
        "SlideStair": ([role(stairs, "StairTimber", 1.4, True), role(stair_frame, "StairPaint", 1.2)], {
            "SlideEntry": (0, 0, 0),
            "SlideDeckSocket": (0, 0, 1.30),
            **{f"SlideStep{i}": (0, y, z) for i, (z, y) in enumerate(SLIDE_STEPS)},
        }),
        "SlideDeck": ([role(at_socket(deck, 1.30), "DeckTimber", 1.4),
                       role(at_socket(stand, 1.30), "DeckPaint", 1.2)], {
            "SlideDeckTop": (0, SLIDE_DECK, .315),
            "SlideChuteSocket": (0, SLIDE_DECK, SLIDE_PROFILE[0][0] - 1.30),
        }),
        "ClimbFrame": ([role(frame, "ClimbPaint", 1.2)], {
            "ClimbEntry": (0, 0, 0), "ClimbDeckSocket": (0, 0, 1.30),
            **{f"ClimbStep{i}": (0, y, z) for i, (z, y) in enumerate(SLIDE_STEPS)},
        }),
        "SlideGuards": (guard_module("SlideGuards",
                        [stair_guards[0], *deck_guards[-1], stair_guards[1], *deck_guards[1]]), rail_anchors),
        "SlideGuardsLeft": (guard_module("SlideGuardsLeft", stair_guards + deck_guards[-1]),
                            {key + "Left": value for key, value in rail_anchors.items()}),
        "SlideGuardsRight": (guard_module("SlideGuardsRight", stair_guards + deck_guards[1]),
                             {key + "Right": value for key, value in rail_anchors.items()}),
        "DeckBridge": ([role(bridge, "BridgeTimber", 1.4, True),
                        *guard_module("DeckBridge", bridge_rails)], {}),
        "DeckBridgeBackstop": ([role(backstop, "BackstopPaint", 1.2)], {}),
        "SlideChute": ([role(at_socket(slide, SLIDE_PROFILE[0][0], SLIDE_DECK), "ChuteMetal", 1.2),
                        role(at_socket(slide_supports, SLIDE_PROFILE[0][0], SLIDE_DECK), "ChutePaint", 1.2)], {
            "SlideExit": (0, -SLIDE_DECK, SLIDE_END - SLIDE_PROFILE[0][0]),
            **{f"SlideGuide{i}": (0, y - SLIDE_DECK, z - SLIDE_PROFILE[0][0])
               for i, (z, y) in enumerate(SLIDE_PROFILE)},
        }),
    }


def seesaw_modules():
    metal = "Residential_PaintedMetal"
    x, y, z = SEESAW_GRIP
    foot_x, foot_y, foot_z = SEESAW_FOOT
    pivot = misc.make_part("SeesawStand", 0, metal,
        misc.recipe_box(0, .031, 0, .54, .14, .46, chamfer=.028),
        *(misc.recipe_tube((side*.19, .06, 0), (side*.19, SEESAW_PIVOT, 0), .051) for side in (-1,1)),
        misc.recipe_tube((-.24, SEESAW_PIVOT, 0), (.24, SEESAW_PIVOT, 0), .048))
    plank = misc.make_part("SeesawBoard", 0, "Masonry_Timber",
        misc.recipe_box(0, 0, 0, .22, .072, 2.56, chamfer=.023),
        *(misc.recipe_box(0, .051, side*SEESAW_SEAT, .31, .055, .34, chamfer=.026) for side in (-1,1)))
    rails = misc.make_part("SeesawBoard", 0, metal, *(
        [misc.recipe_tube((-.20, y-SEESAW_PIVOT, end*z), (.20, y-SEESAW_PIVOT, end*z), .021)
         for end in (-1,1)] +
        [misc.recipe_tube((side*x, .032, end*z), (side*x, y-SEESAW_PIVOT, end*z), .024)
         for side in (-1,1) for end in (-1,1)] +
        [misc.recipe_tube((0, -.036, end*foot_z), (0, foot_y-SEESAW_PIVOT-.024, end*foot_z), .027)
         for end in (-1,1)] +
        [misc.recipe_tube((-foot_x-.062, foot_y-SEESAW_PIVOT-.024, end*foot_z),
                          (foot_x+.062, foot_y-SEESAW_PIVOT-.024, end*foot_z), .024)
         for end in (-1,1)]
    ))
    return {
        "SeesawStand": ([role(pivot, "StandPaint", 1.2)], {"SeesawPivot": (0, SEESAW_PIVOT, 0)}),
        "SeesawBoard": ([role(plank, "BoardTimber", 1.4, True), role(rails, "HandlesPaint", 1.2)], {
            f"Seesaw{key}{end}": (px, py-SEESAW_PIVOT, sign*pz)
            for end, sign in (("A",-1), ("B",1)) for key, px, py, pz in (
                ("Dock",0,0,SEESAW_DOCK), ("Seat",0,SEESAW_PIVOT+.0785,SEESAW_SEAT),
                ("GripL",sign*x,y,z), ("GripR",-sign*x,y,z),
                ("FootL",sign*foot_x,foot_y,foot_z), ("FootR",-sign*foot_x,foot_y,foot_z))
        }),
    }


def modules():
    frame, wood, metal = misc.build_park_playground().parts
    plank = misc.make_part("Swing", 0, "Masonry_Timber",
                           misc.recipe_box(0, -2.33, 0, .72, .10, .36, chamfer=.018))
    ropes = misc.make_part("Swing", 0, "Street_PaintedMetal", *(
        misc.recipe_tube((side * .30, -2.30, 0), (side * .30, 0, 0), .025, sides=8)
        for side in (-1, 1)
    ))
    bench = [replace(part, geometry=([(x, z - 3.60, height) for x, z, height in part.geometry[0]],
                                    part.geometry[1])) for part in (wood, metal)]
    beam = misc.make_part("BalanceBeam", 0, "Masonry_Timber",
                          misc.recipe_box(0, BEAM_TOP - .07, BEAM_END / 2, .23, .14, 2.23, chamfer=.026))
    blocks = misc.make_part("BalanceBeam", 0, "Street_PaintedMetal", *(
        misc.recipe_box(0, .013, z, .33, .14, .19, chamfer=.018)
        for z in (.47, 2.25)
    ))
    trail = misc.make_part("SteppingTrail", 0, "Masonry_Timber", *(
        misc.recipe_tube((0,.025,i*.34), (0,BEAM_TOP,i*.34), .15, sides=8)
        for i in range(1,8)
    ))
    trail_feet = misc.make_part("SteppingTrail", 0, "Residential_PaintedMetal", *(
        misc.recipe_box(0,.013,i*.34,.28,.14,.27,chamfer=.021) for i in range(1,8)
    ))
    board = misc.make_part("BalanceStepPlank", 0, "Masonry_Timber",
        misc.recipe_box(0, BALANCE_STEP_TOP-.035, .45, .40, .07, .50, chamfer=.025))
    board_legs = misc.make_part("BalanceStepPlank", 0, "Residential_PaintedMetal", *(
        misc.recipe_box(0,.042,z,.37,.16,.13,chamfer=.023) for z in (.30,.60)
    ))
    block = misc.make_part("BalanceStepBlock", 0, "Street_PaintedMetal",
        misc.recipe_box(0, .054, .45, .37, .19, .47, chamfer=.033))
    block_top = misc.make_part("BalanceStepBlock", 0, "Masonry_Timber",
        misc.recipe_tube((0,BALANCE_STEP_TOP-.046,.45), (0,BALANCE_STEP_TOP,.45), .245, sides=8))
    rails = misc.make_part("MonkeyBars", 0, "Residential_PaintedMetal", *(
        [misc.recipe_tube((x, -.08, z), (x, RUNG_Y + .03, z), .047)
         for x in (-.37, .37) for z in (RUNGS[0], RUNGS[-1])] +
        [misc.recipe_tube((x, RUNG_Y + .03, RUNGS[0] - .07), (x, RUNG_Y + .03, RUNGS[-1] + .07), .042)
         for x in (-.37, .37)] +
        [misc.recipe_tube((-.37, RUNG_Y, z), (.37, RUNG_Y, z), .021)
         for z in RUNGS]
    ))
    steps = misc.make_part("MonkeyBars", 0, "Masonry_Timber", *(
        misc.recipe_box(0, .075, z, .53, .21, .28, chamfer=.025)
        for z in (STEP_FIRST, STEP_LAST)
    ))
    return {
        **slide_modules(),
        **seesaw_modules(),
        "SteppingTrail": ([role(trail, "TrailTimber", 1.4, True), role(trail_feet, "TrailPaint", 1.2)], {
            "TrailEntry": (0,0,0), "TrailExit": (0,0,BEAM_END),
            **{f"TrailTop{i}": (0,BEAM_TOP,i*.34) for i in range(1,8)},
        }),
        "BalanceStepPlank": ([role(board, "StepTimber", 1.4, True), role(board_legs, "StepPaint", 1.2)], {
            "PlankEntry": (0,0,0), "PlankTop": (0,BALANCE_STEP_TOP,.45),
        }),
        "BalanceStepBlock": ([role(block_top, "BlockTimber", 1.4, True), role(block, "BlockPaint", 1.2)], {
            "BlockEntry": (0,0,0), "BlockTop": (0,BALANCE_STEP_TOP,.45),
        }),
        "BalanceBeam": ([role(beam, "BeamTimber", 1.4), role(blocks, "BeamFeet", 1.2)], {
            "BeamEntry": (0, 0, 0), "BeamExit": (0, 0, BEAM_END), "Top": (0, BEAM_TOP, BEAM_END / 2),
        }),
        "MonkeyBars": ([role(rails, "RungPaint", 1.2), role(steps, "StepTimber", 1.4, True)], {
            "BarsEntry": (0, 0, 0), "BarsExit": (0, 0, RUNG_END),
            "StepIn": (0, STEP_TOP, STEP_FIRST), "StepOut": (0, STEP_TOP, STEP_LAST),
            **{f"Rung{i}": (0, RUNG_Y, z) for i, z in enumerate(RUNGS)},
        }),
        "SwingFrame": ([role(frame, "FramePaint", 1.2)], {
            "PivotLeft": (-.98, 2.95, 0), "PivotRight": (.98, 2.95, 0),
        }),
        "SwingSeat": ([role(plank, "SeatTimber", 1.4, True), role(ropes, "Ropes", 1.2)], {
            "SeatCenter": (0, -2.33, 0), "Pelvis": (0, -2.21, 0),
            "GripLeft": (-.30, -1.89, 0), "GripRight": (.30, -1.89, 0),
        }),
        "Bench": ([role(bench[0], "BenchTimber", 1.4, True), role(bench[1], "BenchPaint", 1.2)], {
            "SeatTop": (0, .71, 0),
        }),
    }


def main():
    source, presentation = misc.reset_scene()
    payload = {}
    for name, (parts, anchors) in modules().items():
        assembly = misc.AssemblySpec(name, 0, tuple(parts))
        root = bpy.data.objects.new("Module_" + name, None)
        source.objects.link(root)
        objects = {p.mesh: misc.create_part_object(p, assembly, root, source) for p in parts}
        for anchor, (x, y, z) in anchors.items():
            item = bpy.data.objects.new(anchor, None)
            if item.name != anchor:
                raise ValueError(f"Duplicate imported playground anchor: {anchor}")
            source.objects.link(item)
            item.parent = root
            item.location = (x, z, y)
            objects[anchor] = item
        digest = hashlib.sha256()
        for part in parts:
            digest.update(repr((part.geometry, part.authored_uvs)).encode())
        payload[name] = {
            "anchors_unity_m": anchors,
            "bounds_blender_m": misc.combined_bounds(parts),
            "sha256": digest.hexdigest(),
        }
        if "--validate-only" not in sys.argv:
            misc.export_fbx(MODELS / (name + ".fbx"), misc.BuildResult(
                root, source, presentation, (assembly,), objects, {}))
    encoded = json.dumps(payload, indent=2, sort_keys=True) + "\n"
    if "--validate-only" in sys.argv:
        if encoded != MANIFEST.read_text():
            raise ValueError("Playground module geometry or contacts differ from the manifest")
    else:
        MANIFEST.write_text(encoded)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    print("City playground module contract: " + ", ".join(payload))


if __name__ == "__main__":
    main()
