#!/usr/bin/env python3
"""Build the deterministic low-poly City pedestrian art library.

Run this with Blender, not CPython:

    blender --background --python tools/build-city-pedestrian-3d-model.py

The generator owns one editable source and animation-free FBX per authored
design, one shared animation-only locomotion FBX, manifests and review renders.
Every rider/walker and clip deliberately carries the exact Generic skeleton
names, parent hierarchy and A-pose rest transforms of PlayerCharacter3D.

Blender source space is metres, Z-up, forward -Y and anatomical left +X.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from dataclasses import dataclass, field
from pathlib import Path
import sys
from typing import Iterable, Sequence

try:
    import bpy
    from mathutils import Euler, Quaternion, Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


GENERATOR_VERSION = "3.1.0"
CANONICAL_HEIGHT = 1.75
SHARED_MATERIAL_NAME = "MAT_Player3DLit"
ANIMATION_FPS = 24
ANIMATION_SOURCE = "Assets/Pedestrians/Animations/CityPedestrianLocomotion.fbx"
PIPEBACK_PIVOT_NAMES = (
    "PIVOT_Wheel.L",
    "PIVOT_Wheel.R",
    "PIVOT_Caster.L",
    "PIVOT_Caster.R",
    "PIVOT_Bellows",
    "PIVOT_PipeBank",
)
PIPEBACK_WHEEL_CENTERS = {
    "L": (0.315, 0.105, 0.300),
    "R": (-0.315, 0.105, 0.300),
}
PIPEBACK_PUSH_RIM_RADIUS = 0.238
PIPEBACK_SEAT_TOP_M = 0.705


@dataclass(frozen=True)
class ArchetypeSpec:
    key: str
    design_id: str
    display_name: str
    seed: int
    blend_name: str
    model_name: str
    preview_name: str
    idle_clip: str
    walk_clip: str
    triangle_budget: tuple[int, int]
    # Optional per-archetype animated hand-to-ground band, as
    # (never_below_m, must_reach_within_m). Only designs whose hands are meant
    # to travel near the pavement declare it; for everyone else the hands sit
    # at chest height and the check is meaningless.
    hand_clearance_m: tuple[float, float] | None = None
    # Optional airborne allowance, as (min_apex_lift_m, max_apex_lift_m).
    # Declaring it replaces the every-frame sole contact rule with "never
    # penetrates, lands at least once, and reaches this apex band in at least
    # one clip", and switches the pelvis bake to a single constant offset so
    # the authored arc survives instead of being flattened onto the pavement.
    airborne_lift_m: tuple[float, float] | None = None
    # Optional authored seated loop for Route 01. A design without one stays
    # on the pavement: the runtime catalog declares the same absence, so a
    # walker is never seated in a posture nobody authored.
    sit_clip: str | None = None
    # Required alongside `sit_clip`, as (min_headroom_m, max_headroom_m)
    # measured from the seated pelvis to the top of the design, worn objects
    # included. The cabin gives 2.05 m from floor to ceiling and the cushion
    # sits 0.41 m up, so anything past 1.64 m would pass through the roof.
    seated_clearance_m: tuple[float, float] | None = None
    # The other way to be seated: on an open bench in the world, where there
    # is no roof to measure against. What has to be right instead is a single
    # distance - from the underside of the seated hips, which is what actually
    # rests on the plank, down to the soles, which have to reach the ground
    # beside it. That distance IS the height of the drawn seat, so the band is
    # declared in those terms and the world can be checked against it directly.
    # Most designs declare exactly one of this and `seated_clearance_m`,
    # because most designs have one seat. The Ferryman has two - he waits
    # on the bonnet of his own car and drives it away - so a design MAY
    # declare both, and then each seated clip says which of the two is
    # carrying it via ActionSpec.perched. Nothing is weakened by that:
    # every seated clip is still proved against exactly one band, and a
    # clip that cannot name its band is still an error.
    perch_seat_height_m: tuple[float, float] | None = None
    # How far anything may hang below a seated pelvis before it is through
    # the floor. This is a property of the VEHICLE, not of the sitter, and
    # the two vehicles in the game do not agree: Route 01 seats a passenger
    # 0.41 m above its deck, and the Ferryman's car seats its driver only
    # 0.22 m above its floor pan. That difference is the whole reason a
    # driving posture reaches its feet forward to the pedals instead of
    # letting them hang - so it is declared, and proved, per design.
    seated_floor_drop_m: float = 0.41
    # Optional single authored beat that is neither locomotion nor a ride: a
    # thing this design does, once, on top of its idle. The runtime blends it
    # over the idle on a two-input mixer, so the clip has to open and close on
    # the idle's own base pose or the seam shows.
    action_clip: str | None = None
    # A wheelchair stays grounded on its tyres rather than on the rider's
    # shoes. Declaring a radius switches the animation bake/validator to that
    # support contract and keeps the feet on the authored footrests.
    wheel_radius_m: float | None = None
    # Staged designs are built and validated with the production art library,
    # but their prefabs live outside Resources and are not eligible for the
    # runtime pedestrian catalog until a later accessibility milestone.
    staged: bool = False
    pool_eligible: bool = True


ARCHETYPES = {
    "lampshade": ArchetypeSpec(
        "lampshade", "lampshade_walker_v1", "Lampshade Walker", 190417,
        "CityPedestrian3D.blend", "CityPedestrian3D", "CityPedestrian3D.png",
        "LampshadeIdle", "LampshadeWalk", (800, 1400),
        sit_clip="LampshadeSit", seated_clearance_m=(0.99, 1.07),
    ),
    "chair_carrier": ArchetypeSpec(
        "chair_carrier", "chair_carrier_v1", "Chair Carrier", 241109,
        "ChairCarrierPedestrian3D.blend", "ChairCarrierPedestrian3D",
        "ChairCarrierPedestrian3D.png", "ChairCarrierIdle", "ChairCarrierWalk",
        (800, 1600),
        sit_clip="ChairCarrierSit", seated_clearance_m=(1.02, 1.10),
    ),
    "kettle_hat": ArchetypeSpec(
        "kettle_hat", "kettle_hat_walker_v1", "Kettle Hat Walker", 305521,
        "KettleHatPedestrian3D.blend", "KettleHatPedestrian3D",
        "KettleHatPedestrian3D.png", "KettleHatIdle", "KettleHatWalk",
        (800, 1600),
        sit_clip="KettleHatSit", seated_clearance_m=(1.01, 1.09),
    ),
    "long_arm": ArchetypeSpec(
        "long_arm", "long_arm_walker_v1", "Long-Arm Walker", 418833,
        "LongArmPedestrian3D.blend", "LongArmPedestrian3D",
        "LongArmPedestrian3D.png", "LongArmIdle", "LongArmWalk",
        (800, 1300), (0.020, 0.140),
        sit_clip="LongArmSit", seated_clearance_m=(1.01, 1.09),
    ),
    "helmet_lamp": ArchetypeSpec(
        "helmet_lamp", "helmet_lamp_hopper_v1", "Helmet Lamp Hopper", 527194,
        "HelmetLampPedestrian3D.blend", "HelmetLampPedestrian3D",
        "HelmetLampPedestrian3D.png", "HelmetLampIdle", "HelmetLampHop",
        (800, 1700), None, (0.080, 0.400),
    ),
    "pipeback_roller": ArchetypeSpec(
        "pipeback_roller", "pipeback_roller_v1", "Pipeback Roller", 631907,
        "PipebackRoller3D.blend", "PipebackRoller3D",
        "PipebackRoller3D.png", "PipebackIdle", "PipebackRoll",
        (1400, 2400),
        wheel_radius_m=0.30,
        staged=True,
        pool_eligible=False,
    ),
    # The drying-yard grandmother. One model serves all three authored
    # instances: two beat carpets with the Soviet plastic beater in the
    # right hand, one stands apart smoking. Both hand props ship on the
    # model and the runtime enables exactly one per role. The idle slot
    # carries the smoking loop and the walk slot carries the beating
    # loop, mirroring how the rider maps Idle/Roll onto the same pair.
    "yard_babushka": ArchetypeSpec(
        "yard_babushka", "yard_babushka_v1", "Yard Babushka", 715233,
        "YardBabushka3D.blend", "YardBabushka3D",
        "YardBabushka3D.png", "BabushkaSmoke", "BabushkaBeat",
        (900, 2000),
        staged=True,
        pool_eligible=False,
    ),
    # The cold-weighbridge attendant. One model serves both authored
    # instances: the weigher reads the tall indicator with a chalk
    # stub in the right hand, the weighed worker paces the deck with
    # free hands. The idle slot carries the weigher's check loop and
    # the walk slot carries the worker's pace, mirroring how the
    # babushka maps Smoke/Beat onto the same pair.
    "weigh_attendant": ArchetypeSpec(
        "weigh_attendant", "weigh_attendant_v1", "Weigh Attendant", 842519,
        "WeighbridgeAttendant3D.blend", "WeighbridgeAttendant3D",
        "WeighbridgeAttendant3D.png", "WeigherCheck", "WeighedPace",
        (900, 2000),
        staged=True,
        pool_eligible=False,
    ),
    # The cemetery mourner. One staged model for the scripted graveside
    # visit: a woman in deep mourning enters the gate with a bouquet
    # clasped to her chest, lays it on the chosen grave, cries and
    # leaves. The idle slot carries the whole graveside rite (lay the
    # flowers, thirty seconds of sobbing, wipe the eyes) and the walk
    # slot carries the grieving gait, mirroring how the babushka and
    # the attendant map their scripted loops onto the same pair.
    "cemetery_mourner": ArchetypeSpec(
        "cemetery_mourner", "cemetery_mourner_v1", "Cemetery Mourner", 918477,
        "CemeteryMourner3D.blend", "CemeteryMourner3D",
        "CemeteryMourner3D.png", "MournerMourn", "MournerWalk",
        (900, 2000),
        staged=True,
        pool_eligible=False,
    ),
    # The cemetery watchman. One staged model for the permanent post
    # at the gate lodge: an extremely snide old man who watches every
    # arrival from his window with his hands clasped behind his back.
    # The idle slot carries the smirking watch loop and the walk slot
    # a slow hands-behind-back shuffle (authored now, patrol later),
    # mirroring how every staged design maps onto the same pair.
    "cemetery_watchman": ArchetypeSpec(
        "cemetery_watchman", "cemetery_watchman_v1", "Cemetery Watchman", 963201,
        "CemeteryWatchman3D.blend", "CemeteryWatchman3D",
        "CemeteryWatchman3D.png", "WatchmanWatch", "WatchmanShuffle",
        (900, 2000),
        staged=True,
        pool_eligible=False,
    ),
    # The lake fisherman. One staged model for the permanent post at
    # the head of the boat-station pier: a hooded man in a yellow
    # oilskin standing at the end board, tipped out over it with a rod
    # in both hands and a smouldering pipe clenched in his teeth. The
    # idle slot carries the leaning fishing loop and the walk slot a
    # slow oilskin trudge reserved for a later pass, mirroring how
    # every staged design maps onto the same pair. He stands on his
    # own boots like every walker, so the ordinary sole bake grounds
    # him and he declares no exception.
    "lake_fisherman": ArchetypeSpec(
        "lake_fisherman", "lake_fisherman_v1", "Lake Fisherman", 1023877,
        "LakeFisherman3D.blend", "LakeFisherman3D",
        "LakeFisherman3D.png", "FishermanLean", "FishermanTrudge",
        (900, 2000),
        staged=True,
        pool_eligible=False,
    ),
    # The park chess player. One staged model for the permanent post at
    # the west park chess set: an old man alone at one of the two tables
    # with his elbows on the board rim and his head in both hands. The
    # chess reference is carried twice over, because colour alone is not
    # allowed to carry a read: the silhouette wears a king's crown where
    # a hat would be, and the cloth carries a check. The idle slot holds
    # the brooding loop and the walk slot a slow park trudge reserved for
    # a later pass, mirroring how every staged design maps onto the same
    # pair.
    #
    # He is the library's first bench sitter. That is not the bus cabin
    # contract - there is no roof over a park bench - so he declares
    # `perch_seat_height_m` instead of `seated_clearance_m`. The drawn
    # chess bench puts its plank 0.54 m over the lawn, so that is the
    # distance his authored seat has to keep from his own soles.
    # The Ferryman, who waits at the last route island with a car that
    # still runs. He is Charon in this register: not a robed skeleton but
    # a driver whose silhouette is a boatman's - a long oilcloth coat
    # standing in for the cloak, a peaked cap whose brim is the hood, and
    # one coil of mooring rope on a man beside a car, never explained.
    #
    # He is the library's second perched design. The bonnet he sits on is
    # 0.505 m above the bumper his boots rest on, which is the number
    # `perch_seat_height_m` pins - see PERCH_DROP_M in
    # tools/build-last-route-car-3d-model.py, and the cross-manifest test
    # that fails when either generator moves.
    #
    # The coat's long skirt is deliberately NOT authored here. It is a
    # runtime Cloth panel hung from his hips, so it drapes over the
    # bonnet edge and moves in the wind instead of being a rigid slab.
    # The geometry stops at a short hem stub that the cloth hangs from.
    "last_route_ferryman": ArchetypeSpec(
        "last_route_ferryman", "last_route_ferryman_v1", "Last Route Ferryman",
        1264099,
        "LastRouteFerryman3D.blend", "LastRouteFerryman3D",
        "LastRouteFerryman3D.png", "FerrymanWait", "FerrymanTrudge",
        (900, 2200),
        staged=True,
        pool_eligible=False,
        perch_seat_height_m=(0.50, 0.52),
        action_clip="FerrymanBoard",
        # And his second seat, the one he leaves in. Both numbers are
        # the car's own rather than the bus's, read off
        # tools/build-last-route-car-3d-model.py: ROOF_UNDERSIDE_Z 1.56
        # over SEAT_PELVIS_Z 0.52 leaves 1.04 m of head, and FLOOR_Z 0.30
        # under the same seat leaves only 0.22 m of leg. The second of
        # those is why he drives with his feet forward on the pedals
        # instead of hanging them like a bus passenger.
        sit_clip="FerrymanDrive",
        seated_clearance_m=(0.95, 1.04),
        seated_floor_drop_m=0.22,
    ),
    "park_chess_player": ArchetypeSpec(
        "park_chess_player", "park_chess_player_v1", "Park Chess Player",
        1104733,
        "ParkChessPlayer3D.blend", "ParkChessPlayer3D",
        "ParkChessPlayer3D.png", "ChessBrood", "ChessTrudge",
        (900, 2100),
        staged=True,
        pool_eligible=False,
        perch_seat_height_m=(0.53, 0.55),
        action_clip="ChessJeer",
    ),
    # The park checkers player. The second staged model for the same
    # chess set: the mirror seat at the other table, turned back across
    # the set so the two old men face each other with four metres of
    # lawn and two empty planks between them. Nobody sits across either
    # board - the absence §10 is built on is doubled rather than spent.
    #
    # Both games share one board, so the check cannot carry this design:
    # what differs between chess and draughts is the men, not the field.
    # Hence both channels are re-derived from the piece. The silhouette
    # wears one thick draught laid almost flat where the other wears a
    # king's tulle, and the cloth answers squares with circles run on
    # the diagonal, because a draught only ever travels one.
    #
    # He is the second design to declare `perch_seat_height_m`, and it
    # is the same 0.54 m plank at the same set, so the band is the same.
    # His body is authored identically to the chess player's on purpose:
    # the elbow-on-board and palm-under-cheek solve is a function of the
    # board height, the plank height and the skull, and all three are
    # shared, so keeping the geometry equal is what makes it transfer.
    "park_checkers_player": ArchetypeSpec(
        "park_checkers_player", "park_checkers_player_v1",
        "Park Checkers Player",
        1187419,
        "ParkCheckersPlayer3D.blend", "ParkCheckersPlayer3D",
        "ParkCheckersPlayer3D.png", "CheckersMull", "CheckersTrudge",
        (900, 2200),
        staged=True,
        pool_eligible=False,
        perch_seat_height_m=(0.53, 0.55),
        action_clip="CheckersJeer",
    ),
}


@dataclass(frozen=True)
class BoneSpec:
    name: str
    head: tuple[float, float, float]
    tail: tuple[float, float, float]
    parent: str | None = None
    connected: bool = False
    deform: bool = True


@dataclass
class PartRecord:
    obj: bpy.types.Object
    bone: str
    role: str
    palette_name: str
    color: tuple[float, float, float, float]


@dataclass
class BuildResult:
    root: bpy.types.Object
    rig: bpy.types.Object
    export_collection: bpy.types.Collection
    material: bpy.types.Material
    parts: list[PartRecord] = field(default_factory=list)
    pivots: dict[str, bpy.types.Object] = field(default_factory=dict)


@dataclass(frozen=True)
class BonePose:
    rotation_degrees: tuple[float, float, float] = (0.0, 0.0, 0.0)
    location_m: tuple[float, float, float] = (0.0, 0.0, 0.0)
    scale: tuple[float, float, float] = (1.0, 1.0, 1.0)


@dataclass(frozen=True)
class ActionSpec:
    name: str
    archetype: str
    duration_seconds: float
    frame_end: int
    authored_posture: str
    gait: str
    # A seated clip is not sole-grounded. Its feet leave the pavement plane by
    # design, so pinning the lowest sole would drag the whole model down until
    # the boots touched the floor; the runtime aligns the shared rest pelvis to
    # the cushion instead, and the declared headroom band is what gets proved.
    seated: bool = False
    # Which of the two seats carries a seated clip, for the one design that
    # has both. Ignored when its archetype declares a single band - the
    # band it declares is the only answer there is.
    perched: bool = False
    # And a clip that MOVES between two seats cannot be measured against
    # either of them. The Ferryman's board transition starts on a car
    # bonnet and ends behind its wheel: half way through his seat is
    # nowhere, by design. Such a clip stays seated - it must not be
    # sole-grounded - but its band check is skipped, and whatever it
    # arrives at is proved by the clip that follows it.
    leaves_seat: bool = False
    # A transition is not a loop and must not be asserted as one. Every
    # other clip in this library returns to its own first frame, because
    # every other clip repeats; the board transition ends behind a
    # steering wheel it did not start at. What holds it together instead
    # is that it opens on the exact base pose of the clip before it and
    # closes on the exact base pose of the clip after, so the runtime can
    # cross in and out of it without a seam - which the key grid states
    # by reusing those pose functions rather than re-typing numbers.
    one_shot: bool = False


@dataclass(frozen=True)
class ValidationReport:
    mesh_count: int
    triangle_count: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]
    build_signature: str


PALETTE = {
    "coat": (0.080, 0.155, 0.122, 1.0),
    "coat_light": (0.120, 0.215, 0.165, 1.0),
    "coat_dark": (0.040, 0.085, 0.068, 1.0),
    "trousers": (0.075, 0.105, 0.115, 1.0),
    # The Ferryman. His coat is the darkest value on the island and the
    # cap band and the coin are the only light ones, so the toss carries
    # the whole silhouette from across the lot.
    "ferry_coat": (0.055, 0.058, 0.062, 1.0),
    "ferry_coat_dark": (0.030, 0.032, 0.035, 1.0),
    "ferry_cap": (0.048, 0.050, 0.054, 1.0),
    "ferry_cap_band": (0.470, 0.445, 0.360, 1.0),
    "ferry_shadow": (0.012, 0.013, 0.015, 1.0),
    "ferry_rope": (0.360, 0.315, 0.225, 1.0),
    "ferry_boot": (0.058, 0.052, 0.046, 1.0),
    "ferry_skin": (0.470, 0.395, 0.330, 1.0),
    "glove": (0.080, 0.075, 0.065, 1.0),
    "hood": (0.245, 0.235, 0.205, 1.0),
    "hood_light": (0.345, 0.325, 0.275, 1.0),
    "hood_dark": (0.115, 0.112, 0.100, 1.0),
    "void": (0.008, 0.010, 0.009, 1.0),
    "amber": (0.525, 0.275, 0.075, 1.0),
    "bag": (0.130, 0.090, 0.065, 1.0),
    "bag_edge": (0.060, 0.045, 0.035, 1.0),
    "rubber": (0.035, 0.050, 0.045, 1.0),
    "leather": (0.055, 0.042, 0.035, 1.0),
    "sole": (0.018, 0.019, 0.017, 1.0),
    "button": (0.155, 0.145, 0.115, 1.0),
    "work_jacket": (0.245, 0.145, 0.075, 1.0),
    "work_jacket_light": (0.335, 0.205, 0.105, 1.0),
    "work_jacket_dark": (0.105, 0.070, 0.048, 1.0),
    "work_trousers": (0.105, 0.125, 0.118, 1.0),
    "skin": (0.355, 0.235, 0.165, 1.0),
    "chair_wood": (0.285, 0.115, 0.055, 1.0),
    "chair_edge": (0.095, 0.045, 0.028, 1.0),
    "chair_wear": (0.455, 0.265, 0.105, 1.0),
    "strap_cloth": (0.080, 0.095, 0.080, 1.0),
    "shoe": (0.045, 0.038, 0.032, 1.0),
    # Kettle Hat Walker. A muted plum coat keeps the stout mass distinct from
    # the Lampshade's green and the Chair Carrier's orange under City fog,
    # while the chipped enamel is the brightest value any walker owns.
    "stout_coat": (0.150, 0.085, 0.105, 1.0),
    "stout_coat_light": (0.215, 0.130, 0.150, 1.0),
    "stout_coat_dark": (0.080, 0.045, 0.058, 1.0),
    "stout_trousers": (0.090, 0.085, 0.105, 1.0),
    "kettle_enamel": (0.520, 0.545, 0.520, 1.0),
    "kettle_enamel_dark": (0.290, 0.315, 0.300, 1.0),
    "kettle_chip": (0.155, 0.105, 0.070, 1.0),
    "kettle_metal": (0.115, 0.120, 0.118, 1.0),
    # Long-Arm Walker. Cold steel blue is the last unused walker hue, and the
    # bare forearms are deliberately the brightest value on the model so the
    # eye lands on the arms first at any distance.
    "steel_coat": (0.075, 0.100, 0.135, 1.0),
    "steel_coat_light": (0.120, 0.155, 0.200, 1.0),
    "steel_coat_dark": (0.040, 0.055, 0.078, 1.0),
    "steel_trousers": (0.058, 0.070, 0.088, 1.0),
    "pale_skin": (0.520, 0.470, 0.430, 1.0),
    "pale_skin_dark": (0.330, 0.290, 0.265, 1.0),
    # Helmet Lamp Hopper. Ochre work wear is the last unused walker hue. The
    # lens is ordinary bright paint, not an emissive material: the shared
    # source material stays non-emissive and the real Spot does the lighting.
    "miner_ochre": (0.310, 0.205, 0.060, 1.0),
    "miner_ochre_light": (0.420, 0.290, 0.100, 1.0),
    "miner_ochre_dark": (0.150, 0.100, 0.032, 1.0),
    "miner_trousers": (0.105, 0.098, 0.078, 1.0),
    "miner_hivis": (0.640, 0.520, 0.180, 1.0),
    "miner_helmet": (0.430, 0.435, 0.415, 1.0),
    "miner_helmet_dark": (0.115, 0.120, 0.115, 1.0),
    "miner_rubber": (0.048, 0.044, 0.040, 1.0),
    "miner_cable": (0.062, 0.058, 0.056, 1.0),
    "lamp_lens": (0.880, 0.820, 0.640, 1.0),
    # Pipeback Roller. The person is sober oxblood and charcoal; all visual
    # absurdity belongs to the tarnished organ-chair mechanism behind them.
    "roller_coat": (0.205, 0.055, 0.070, 1.0),
    "roller_coat_light": (0.300, 0.090, 0.100, 1.0),
    "roller_coat_dark": (0.075, 0.025, 0.032, 1.0),
    "roller_trousers": (0.055, 0.060, 0.065, 1.0),
    "roller_skin": (0.405, 0.285, 0.215, 1.0),
    "roller_skin_dark": (0.235, 0.155, 0.115, 1.0),
    "chair_frame": (0.060, 0.066, 0.068, 1.0),
    "chair_frame_light": (0.135, 0.145, 0.145, 1.0),
    "chair_tyre": (0.018, 0.021, 0.020, 1.0),
    "chair_rim": (0.250, 0.225, 0.170, 1.0),
    "chair_seat": (0.090, 0.040, 0.045, 1.0),
    "pipe_brass": (0.390, 0.285, 0.105, 1.0),
    "pipe_brass_light": (0.570, 0.435, 0.180, 1.0),
    "pipe_brass_dark": (0.155, 0.105, 0.042, 1.0),
    "pipe_ivory": (0.585, 0.555, 0.465, 1.0),
    "bellows": (0.170, 0.075, 0.055, 1.0),
    # Yard Babushka. A muted mauve housecoat over a dark skirt, a rust
    # headscarf, and the one loud note every Soviet yard remembers: the
    # bright plastic carpet beater in her right hand.
    "gran_robe": (0.150, 0.090, 0.125, 1.0),
    "gran_robe_light": (0.215, 0.135, 0.170, 1.0),
    "gran_robe_dark": (0.075, 0.045, 0.065, 1.0),
    "gran_skirt": (0.085, 0.060, 0.085, 1.0),
    "gran_apron": (0.320, 0.300, 0.250, 1.0),
    "gran_scarf": (0.400, 0.135, 0.085, 1.0),
    "gran_scarf_dark": (0.210, 0.070, 0.048, 1.0),
    "gran_wool": (0.100, 0.095, 0.090, 1.0),
    "beater_plastic": (0.560, 0.165, 0.105, 1.0),
    "beater_plastic_dark": (0.300, 0.085, 0.055, 1.0),
    # Weigh Attendant. A quilted grey-green work jacket on the
    # Industrial cold axis: nothing bright, no authority markers —
    # the one near-white note is the chalk stub in the weigher's
    # right hand, the site's ongoing measurement given its author.
    "weigh_jacket": (0.130, 0.152, 0.138, 1.0),
    "weigh_jacket_light": (0.185, 0.212, 0.192, 1.0),
    "weigh_jacket_dark": (0.062, 0.076, 0.068, 1.0),
    "weigh_trousers": (0.072, 0.082, 0.088, 1.0),
    "weigh_cap": (0.098, 0.110, 0.120, 1.0),
    "chalk": (0.620, 0.635, 0.615, 1.0),
    # Cemetery Mourner. Deep mourning on purpose: a near-black coat and
    # veil separated only by cold charcoal steps, pale grieving skin,
    # and the one muted colour note the whole figure exists around —
    # the bouquet clasped to her chest.
    "mourner_coat": (0.052, 0.050, 0.058, 1.0),
    "mourner_coat_light": (0.082, 0.080, 0.090, 1.0),
    "mourner_coat_dark": (0.028, 0.027, 0.032, 1.0),
    "mourner_veil": (0.038, 0.036, 0.044, 1.0),
    "mourner_veil_dark": (0.020, 0.019, 0.024, 1.0),
    "mourner_stocking": (0.045, 0.044, 0.048, 1.0),
    "bouquet_stem": (0.055, 0.110, 0.060, 1.0),
    "bouquet_bloom": (0.290, 0.060, 0.075, 1.0),
    "bouquet_wrap": (0.330, 0.300, 0.240, 1.0),
    # Cemetery Watchman. A worn olive-grey telogreika over an old
    # shirt, an aerodrome flat cap, kirza boots and grey whiskers:
    # nothing bright, nothing official — the smirk is the uniform.
    "watch_coat": (0.140, 0.130, 0.095, 1.0),
    "watch_coat_light": (0.200, 0.190, 0.140, 1.0),
    "watch_coat_dark": (0.070, 0.065, 0.048, 1.0),
    "watch_trousers": (0.085, 0.090, 0.080, 1.0),
    "watch_cap": (0.110, 0.095, 0.080, 1.0),
    "watch_shirt": (0.230, 0.215, 0.180, 1.0),
    "watch_grey": (0.360, 0.355, 0.340, 1.0),
    # Lake Fisherman. The one saturated note anywhere on the boat
    # station is worn, not built: a municipal-yellow oilskin over the
    # dead green water. It is deliberately the brightest hue any City
    # design owns, because a lit hand lamp two metres away is the only
    # thing that will ever illuminate it, and because the whole read of
    # the pier at distance is one yellow mark at the far end of grey
    # boards. Everything else on him is wet rubber, briar and grey.
    "slicker": (0.455, 0.315, 0.045, 1.0),
    "slicker_light": (0.585, 0.425, 0.080, 1.0),
    "slicker_dark": (0.235, 0.158, 0.024, 1.0),
    "oilskin_trousers": (0.175, 0.140, 0.055, 1.0),
    "boot_rubber": (0.055, 0.058, 0.052, 1.0),
    "fisher_skin": (0.395, 0.265, 0.190, 1.0),
    "fisher_grey": (0.395, 0.390, 0.372, 1.0),
    "pipe_briar": (0.165, 0.098, 0.055, 1.0),
    "pipe_briar_dark": (0.082, 0.048, 0.028, 1.0),
    "rod_cane": (0.230, 0.150, 0.072, 1.0),
    "rod_cork": (0.330, 0.245, 0.145, 1.0),
    "rod_reel": (0.128, 0.132, 0.130, 1.0),
    # Park Chess Player. The park palette is deep black-green, sandy
    # grey-brown and cold bone, so the check is bone on black-green
    # rather than white on black: a true white square would be the
    # loudest value in the whole precinct and would say "costume".
    #
    # Every light value here is deliberately held near 0.6 and nowhere
    # near 1. He sits directly under the one burning lamp, and the
    # fisherman's slicker had to come down from 0.560 to 0.455 for
    # exactly this reason - a bright albedo two metres from a warm
    # practical clips to white and stops being a material.
    "chess_coat": (0.105, 0.128, 0.108, 1.0),
    "chess_coat_light": (0.155, 0.185, 0.155, 1.0),
    "chess_coat_dark": (0.052, 0.065, 0.055, 1.0),
    "chess_trousers": (0.078, 0.082, 0.075, 1.0),
    "chess_check_light": (0.615, 0.600, 0.545, 1.0),
    "chess_check_dark": (0.062, 0.082, 0.068, 1.0),
    "chess_crown": (0.600, 0.588, 0.535, 1.0),
    "chess_crown_cross": (0.640, 0.628, 0.572, 1.0),
    "chess_crown_dark": (0.255, 0.248, 0.225, 1.0),
    "chess_skin": (0.372, 0.262, 0.198, 1.0),
    "chess_grey": (0.385, 0.380, 0.362, 1.0),
    "chess_boot": (0.048, 0.045, 0.040, 1.0),
    # Park Checkers Player. He sits at the other table of the same set,
    # under the same one burning lamp, so every rule that set the chess
    # player's values applies unchanged - nothing white, every light
    # value held near 0.6.
    #
    # Two of them are copied rather than chosen. The light circle is the
    # chess player's light square to the last digit, because it is the
    # colour of the light field on the board both of them are sitting
    # at: it is what says the two men are playing on the same thing. And
    # the draught is turned from the crown's own three values, because
    # they are two pieces of one set.
    #
    # What actually differs is the cloth. The park runs on deep
    # black-green, sandy grey-brown and cold bone, and the chess player
    # took the black-green, so this coat takes the sandy grey-brown. The
    # two are deliberately matched in luminance (~0.12) and separated
    # only in hue, so a grayscale read cannot tell them apart by value
    # and the whole weight falls on the silhouette, exactly as §3.2
    # demands.
    "checkers_coat": (0.104, 0.094, 0.074, 1.0),
    "checkers_coat_light": (0.146, 0.132, 0.104, 1.0),
    "checkers_coat_dark": (0.050, 0.045, 0.036, 1.0),
    "checkers_trousers": (0.066, 0.062, 0.053, 1.0),
    "checkers_spot_light": (0.615, 0.600, 0.545, 1.0),
    "checkers_spot_dark": (0.070, 0.074, 0.060, 1.0),
    "checkers_disc": (0.600, 0.588, 0.535, 1.0),
    "checkers_disc_rim": (0.255, 0.248, 0.225, 1.0),
    "checkers_disc_ring": (0.640, 0.628, 0.572, 1.0),
    "checkers_skin": (0.352, 0.248, 0.192, 1.0),
    "checkers_grey": (0.372, 0.368, 0.352, 1.0),
    "checkers_boot": (0.046, 0.043, 0.038, 1.0),
}


def v(value: Sequence[float]) -> Vector:
    return Vector(tuple(float(component) for component in value))


def player_bone_specs() -> tuple[BoneSpec, ...]:
    """Canonical PlayerCharacter3D 2.5.0 A-pose skeleton contract."""

    points = {
        "hip.L": (0.083, 0.012, 0.750),
        "hip.R": (-0.083, -0.004, 0.750),
        "knee.L": (0.103, -0.012, 0.354),
        "knee.R": (-0.103, 0.012, 0.354),
        "ankle.L": (0.112, -0.026, 0.095),
        "ankle.R": (-0.112, 0.018, 0.095),
        "toe.L": (0.112, -0.230, 0.045),
        "toe.R": (-0.112, -0.188, 0.045),
        "shoulder.L": (0.208, -0.004, 1.292),
        "shoulder.R": (-0.208, 0.004, 1.292),
        "elbow.L": (0.470, -0.010, 1.175),
        "wrist.L": (0.680, -0.018, 1.075),
        "hand.L": (0.755, -0.022, 1.035),
        "elbow.R": (-0.470, -0.010, 1.175),
        "wrist.R": (-0.680, -0.018, 1.075),
        "hand.R": (-0.755, -0.022, 1.035),
    }

    return (
        BoneSpec("root", (0, 0, 0), (0, 0, 0.18), deform=False),
        BoneSpec("pelvis", (0, 0.008, 0.700), (0, 0.004, 0.900), "root"),
        BoneSpec("spine", (0, 0.004, 0.900), (0, 0, 1.120), "pelvis", True),
        BoneSpec("chest", (0, 0, 1.120), (0, -0.010, 1.335), "spine", True),
        BoneSpec("neck", (0, -0.010, 1.335), (0, -0.025, 1.430), "chest", True),
        BoneSpec("head", (0, -0.025, 1.430), (0, -0.050, 1.675), "neck", True),
        BoneSpec("face.eye.L", (0.052, -0.147, 1.581), (0.052, -0.147, 1.599), "head"),
        BoneSpec("face.eye.R", (-0.052, -0.147, 1.581), (-0.052, -0.147, 1.599), "head"),
        BoneSpec("face.brow.L", (0.082, -0.154, 1.627), (0.027, -0.157, 1.621), "head"),
        BoneSpec("face.brow.R", (-0.082, -0.154, 1.625), (-0.027, -0.157, 1.619), "head"),
        BoneSpec("face.mouth", (-0.036, -0.151, 1.477), (0.048, -0.151, 1.477), "head"),
        BoneSpec("SOCKET_Mouth", (0.006, -0.158, 1.477), (0.006, -0.218, 1.477), "head", deform=False),
        BoneSpec("clavicle.L", (0, -0.008, 1.325), points["shoulder.L"], "chest", deform=False),
        BoneSpec("upper_arm.L", points["shoulder.L"], points["elbow.L"], "clavicle.L", True),
        BoneSpec("forearm.L", points["elbow.L"], points["wrist.L"], "upper_arm.L", True),
        BoneSpec("hand.L", points["wrist.L"], points["hand.L"], "forearm.L", True),
        BoneSpec("SOCKET_Grip.L", (0.734, -0.02088, 1.0462), (0.734, -0.07588, 1.0462), "hand.L", deform=False),
        BoneSpec("SOCKET_Vessel.L", (0.734, -0.02088, 1.0462), (0.734, -0.02088, 0.9612), "hand.L", deform=False),
        BoneSpec("clavicle.R", (0, -0.008, 1.325), points["shoulder.R"], "chest", deform=False),
        BoneSpec("upper_arm.R", points["shoulder.R"], points["elbow.R"], "clavicle.R", True),
        BoneSpec("forearm.R", points["elbow.R"], points["wrist.R"], "upper_arm.R", True),
        BoneSpec("hand.R", points["wrist.R"], points["hand.R"], "forearm.R", True),
        BoneSpec("SOCKET_Grip.R", (-0.734, -0.02088, 1.0462), (-0.734, -0.07588, 1.0462), "hand.R", deform=False),
        BoneSpec("SOCKET_Cigarette.R", (-0.734, -0.03088, 1.0582), (-0.734, -0.10588, 1.0582), "hand.R", deform=False),
        BoneSpec("SOCKET_Bottle.R", (-0.734, -0.02088, 1.0462), (-0.734, -0.02088, 0.9612), "hand.R", deform=False),
        BoneSpec("thigh.L", points["hip.L"], points["knee.L"], "pelvis"),
        BoneSpec("shin.L", points["knee.L"], points["ankle.L"], "thigh.L", True),
        BoneSpec("foot.L", points["ankle.L"], points["toe.L"], "shin.L", True),
        BoneSpec("thigh.R", points["hip.R"], points["knee.R"], "pelvis"),
        BoneSpec("shin.R", points["knee.R"], points["ankle.R"], "thigh.R", True),
        BoneSpec("foot.R", points["ankle.R"], points["toe.R"], "shin.R", True),
    )


SKELETON = player_bone_specs()
BONE_BY_NAME = {bone.name: bone for bone in SKELETON}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=Path("ArtSource/Pedestrians/Blender"),
    )
    parser.add_argument(
        "--model-dir",
        type=Path,
        default=Path("Assets/Pedestrians/Models"),
    )
    parser.add_argument(
        "--animation-dir",
        type=Path,
        default=Path("Assets/Pedestrians/Animations"),
    )
    parser.add_argument(
        "--staged-model-dir",
        type=Path,
        default=Path("Assets/Pedestrians/Staged/Models"),
    )
    parser.add_argument(
        "--archetype",
        choices=("all", *ARCHETYPES),
        default="all",
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    config = parser.parse_args(arguments)
    # Blender resolves a relative render path against the unsaved startup
    # blend's `//` root (often the drive root), unlike Python file writes.
    # Resolve every output from the invocation cwd before touching Blender IO.
    for field_name in (
        "source_dir", "model_dir", "animation_dir", "staged_model_dir"
    ):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


def make_box(center: Sequence[float], size: Sequence[float]):
    c = v(center)
    half = v(size) * 0.5
    vertices = [
        c + Vector((sx * half.x, sy * half.y, sz * half.z))
        for sz in (-1, 1)
        for sy in (-1, 1)
        for sx in (-1, 1)
    ]
    faces = [
        (0, 1, 3, 2),
        (4, 6, 7, 5),
        (0, 4, 5, 1),
        (2, 3, 7, 6),
        (0, 2, 6, 4),
        (1, 5, 7, 3),
    ]
    return vertices, faces


def make_tapered_box(
    lower_center: Sequence[float],
    upper_center: Sequence[float],
    lower_size: Sequence[float],
    upper_size: Sequence[float],
):
    lower = v(lower_center)
    upper = v(upper_center)
    low_half = v(lower_size) * 0.5
    up_half = v(upper_size) * 0.5
    vertices = []
    for center, half in ((lower, low_half), (upper, up_half)):
        for sy in (-1, 1):
            for sx in (-1, 1):
                vertices.append(center + Vector((sx * half.x, sy * half.y, 0)))
    faces = [
        (0, 2, 3, 1),
        (4, 5, 7, 6),
        (0, 1, 5, 4),
        (2, 6, 7, 3),
        (0, 4, 6, 2),
        (1, 3, 7, 5),
    ]
    return vertices, faces


def make_frustum_between(
    start: Sequence[float],
    end: Sequence[float],
    radius_start: float,
    radius_end: float,
    sides: int = 12,
    flatten: float = 0.82,
):
    start_vector = v(start)
    end_vector = v(end)
    axis = end_vector - start_vector
    rotation = axis.to_track_quat("Z", "Y")
    basis_x = rotation @ Vector((1, 0, 0))
    basis_y = rotation @ Vector((0, 1, 0))
    vertices = []
    for center, radius in ((start_vector, radius_start), (end_vector, radius_end)):
        for index in range(sides):
            angle = 2.0 * math.pi * index / sides
            offset = (
                basis_x * math.cos(angle) * radius
                + basis_y * math.sin(angle) * radius * flatten
            )
            vertices.append(center + offset)
    faces: list[tuple[int, ...]] = []
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple(range(sides, sides * 2)))
    for index in range(sides):
        next_index = (index + 1) % sides
        faces.append((index, next_index, sides + next_index, sides + index))
    return vertices, faces


def make_ellipsoid(
    center: Sequence[float],
    radii: Sequence[float],
    segments: int = 12,
    rings: int = 6,
):
    center_vector = v(center)
    radius_vector = v(radii)
    vertices = [center_vector + Vector((0, 0, -radius_vector.z))]
    for ring in range(1, rings):
        phi = -math.pi * 0.5 + math.pi * ring / rings
        for segment in range(segments):
            theta = 2.0 * math.pi * segment / segments
            vertices.append(
                center_vector
                + Vector(
                    (
                        radius_vector.x * math.cos(phi) * math.cos(theta),
                        radius_vector.y * math.cos(phi) * math.sin(theta),
                        radius_vector.z * math.sin(phi),
                    )
                )
            )
    top_index = len(vertices)
    vertices.append(center_vector + Vector((0, 0, radius_vector.z)))
    faces: list[tuple[int, ...]] = []
    for segment in range(segments):
        next_segment = (segment + 1) % segments
        faces.append((0, 1 + next_segment, 1 + segment))
    for ring in range(rings - 2):
        first = 1 + ring * segments
        second = first + segments
        for segment in range(segments):
            next_segment = (segment + 1) % segments
            faces.append(
                (
                    first + segment,
                    first + next_segment,
                    second + next_segment,
                    second + segment,
                )
            )
    last_ring = 1 + (rings - 2) * segments
    for segment in range(segments):
        next_segment = (segment + 1) % segments
        faces.append((last_ring + segment, last_ring + next_segment, top_index))
    return vertices, faces


def make_torus_x(
    center: Sequence[float],
    major_radius: float,
    tube_radius: float,
    major_segments: int = 16,
    tube_segments: int = 6,
):
    """Low-poly torus whose axle runs along anatomical X."""

    center_vector = v(center)
    vertices: list[Vector] = []
    for major in range(major_segments):
        major_angle = 2.0 * math.pi * major / major_segments
        radial = Vector((
            0.0,
            math.cos(major_angle),
            math.sin(major_angle),
        ))
        for tube in range(tube_segments):
            tube_angle = 2.0 * math.pi * tube / tube_segments
            vertices.append(
                center_vector
                + radial * (major_radius + tube_radius * math.cos(tube_angle))
                + Vector((tube_radius * math.sin(tube_angle), 0.0, 0.0))
            )
    faces: list[tuple[int, int, int, int]] = []
    for major in range(major_segments):
        next_major = (major + 1) % major_segments
        for tube in range(tube_segments):
            next_tube = (tube + 1) % tube_segments
            faces.append((
                major * tube_segments + tube,
                next_major * tube_segments + tube,
                next_major * tube_segments + next_tube,
                major * tube_segments + next_tube,
            ))
    return vertices, faces


def combine_geometry(*items):
    """Join deterministic primitive payloads without a Blender operator."""

    vertices: list[Vector] = []
    faces: list[tuple[int, ...]] = []
    for item_vertices, item_faces in items:
        offset = len(vertices)
        vertices.extend(item_vertices)
        faces.extend(tuple(index + offset for index in face) for face in item_faces)
    return vertices, faces


class PedestrianBuilder:
    def __init__(self, spec: ArchetypeSpec):
        self.spec = spec
        self.result: BuildResult | None = None

    def build(self) -> BuildResult:
        self.reset_scene()
        scene_root = bpy.context.scene.collection
        pedestrian = bpy.data.collections.new(f"BP_{self.spec.model_name}")
        scene_root.children.link(pedestrian)
        export_collection = bpy.data.collections.new("EXPORT_CityPedestrian")
        pedestrian.children.link(export_collection)
        presentation = bpy.data.collections.new("PRESENTATION_CityPedestrian")
        pedestrian.children.link(presentation)

        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        export_collection.objects.link(root)
        root.empty_display_type = "PLAIN_AXES"
        root["bp_export"] = True
        root["bp_generator"] = "tools/build-city-pedestrian-3d-model.py"
        root["bp_generator_version"] = GENERATOR_VERSION
        root["bp_design_id"] = self.spec.design_id
        root["bp_seed"] = self.spec.seed
        root["bp_forward_axis"] = "-Y"
        root["bp_anatomical_left_axis"] = "+X"
        root["bp_shared_animation_source"] = ANIMATION_SOURCE
        root["bp_staged"] = self.spec.staged
        root["bp_pool_eligible"] = self.spec.pool_eligible

        rig = self.create_armature(export_collection, root)
        self.result = BuildResult(root, rig, export_collection, material)
        # An explicit per-archetype builder pair; a missing key is a build
        # error rather than a silent fallback to another design.
        builders = {
            "lampshade": (self.build_body, self.build_clothing_and_details),
            "chair_carrier": (
                self.build_chair_carrier_body,
                self.build_chair_carrier_details,
            ),
            "kettle_hat": (
                self.build_kettle_hat_body,
                self.build_kettle_hat_details,
            ),
            "long_arm": (
                self.build_long_arm_body,
                self.build_long_arm_details,
            ),
            "helmet_lamp": (
                self.build_helmet_lamp_body,
                self.build_helmet_lamp_details,
            ),
            "pipeback_roller": (
                self.build_pipeback_roller_body,
                self.build_pipeback_roller_chair,
            ),
            "yard_babushka": (
                self.build_yard_babushka_body,
                self.build_yard_babushka_details,
            ),
            "weigh_attendant": (
                self.build_weigh_attendant_body,
                self.build_weigh_attendant_details,
            ),
            "cemetery_mourner": (
                self.build_cemetery_mourner_body,
                self.build_cemetery_mourner_details,
            ),
            "cemetery_watchman": (
                self.build_cemetery_watchman_body,
                self.build_cemetery_watchman_details,
            ),
            "lake_fisherman": (
                self.build_lake_fisherman_body,
                self.build_lake_fisherman_details,
            ),
            "last_route_ferryman": (
                self.build_last_route_ferryman_body,
                self.build_last_route_ferryman_details,
            ),
            "park_chess_player": (
                self.build_park_chess_player_body,
                self.build_park_chess_player_details,
            ),
            "park_checkers_player": (
                self.build_park_checkers_player_body,
                self.build_park_checkers_player_details,
            ),
        }
        if self.spec.key not in builders:
            raise RuntimeError(
                f"No geometry builder is registered for '{self.spec.key}'"
            )
        for builder in builders[self.spec.key]:
            builder()
        self.configure_scene_metadata()
        return self.result

    @staticmethod
    def reset_scene() -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1.0
        scene.unit_settings.length_unit = "METERS"
        scene.render.resolution_x = 640
        scene.render.resolution_y = 800
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = "PNG"
        scene.render.film_transparent = False
        scene.render.fps = 24
        scene.render.fps_base = 1.0
        for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
            try:
                scene.render.engine = engine
                break
            except TypeError:
                continue
        scene.world = bpy.data.worlds.new("WORLD_PedestrianPreview")
        scene.world.use_nodes = True
        background = scene.world.node_tree.nodes.get("Background")
        if background is not None:
            background.inputs["Color"].default_value = (0.010, 0.016, 0.014, 1)
            background.inputs["Strength"].default_value = 0.18

    @staticmethod
    def create_shared_material() -> bpy.types.Material:
        material = bpy.data.materials.new(SHARED_MATERIAL_NAME)
        material.use_nodes = True
        material.diffuse_color = PALETTE["coat"]
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        object_info = nodes.new("ShaderNodeObjectInfo")
        shader.inputs["Roughness"].default_value = 0.86
        shader.inputs["Metallic"].default_value = 0.0
        emission = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
        if emission is not None:
            emission.default_value = (0, 0, 0, 1)
        links.new(object_info.outputs["Color"], shader.inputs["Base Color"])
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])
        material["bp_runtime_material"] = "Assets/Player3D/Materials/Player3DLit.mat"
        material["bp_emissive"] = False
        return material

    @staticmethod
    def create_armature(
        collection: bpy.types.Collection,
        root: bpy.types.Object,
    ) -> bpy.types.Object:
        armature_data = bpy.data.armatures.new("RIG_Player_Data")
        rig = bpy.data.objects.new("RIG_Player", armature_data)
        collection.objects.link(rig)
        rig.parent = root
        rig.show_in_front = True
        rig.display_type = "WIRE"
        rig["bp_export"] = True
        rig["bp_skeleton_contract"] = "PlayerCharacter3D exact A-pose v2.5.0"

        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        created = {}
        for spec in SKELETON:
            bone = armature_data.edit_bones.new(spec.name)
            bone.head = spec.head
            bone.tail = spec.tail
            bone.use_deform = spec.deform
            created[spec.name] = bone
        for spec in SKELETON:
            if spec.parent is None:
                continue
            bone = created[spec.name]
            bone.parent = created[spec.parent]
            bone.use_connect = spec.connected
        bpy.ops.object.mode_set(mode="OBJECT")
        rig.select_set(False)
        return rig

    def add_part(
        self,
        name: str,
        geometry,
        bone_name: str,
        role: str,
        palette_name: str,
        origin: Sequence[float] | None = None,
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        if bone_name not in BONE_BY_NAME:
            raise ValueError(f"Unknown canonical bone: {bone_name}")
        color = PALETTE[palette_name]
        vertices, faces = geometry
        origin_vector = v(origin or BONE_BY_NAME[bone_name].head)
        mesh = bpy.data.meshes.new(f"{name}_Mesh")
        mesh.from_pydata(
            [tuple(vertex - origin_vector) for vertex in vertices],
            [],
            faces,
        )
        mesh.update(calc_edges=True)
        for polygon in mesh.polygons:
            polygon.use_smooth = False
        obj = bpy.data.objects.new(name, mesh)
        self.result.export_collection.objects.link(obj)
        obj.location = origin_vector
        obj.color = color
        obj.data.materials.append(self.result.material)
        static_root_part = bone_name == "root"
        parent = self.result.root if static_root_part else self.result.rig
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()

        group = obj.vertex_groups.new(name=bone_name)
        group.add(range(len(mesh.vertices)), 1.0, "REPLACE")
        triangulate = obj.modifiers.new("Triangulate", "TRIANGULATE")
        triangulate.quad_method = "FIXED"
        triangulate.ngon_method = "CLIP"
        if not static_root_part:
            armature = obj.modifiers.new("Armature", "ARMATURE")
            armature.object = self.result.rig
            armature.use_deform_preserve_volume = False

        obj["bp_export"] = True
        obj["bp_role"] = role
        obj["bp_bone"] = bone_name
        obj["bp_palette"] = palette_name
        obj["bp_base_color"] = list(color)
        obj["bp_generator_version"] = GENERATOR_VERSION
        self.result.parts.append(
            PartRecord(obj, bone_name, role, palette_name, color)
        )
        return obj

    def create_pivot(
        self,
        name: str,
        location: Sequence[float],
    ) -> bpy.types.Object:
        if self.result is None:
            raise RuntimeError("Build has not been initialized")
        if name in self.result.pivots:
            raise ValueError(f"Duplicate pedestrian pivot {name}")
        pivot = bpy.data.objects.new(name, None)
        self.result.export_collection.objects.link(pivot)
        pivot.parent = self.result.root
        pivot.location = v(location)
        pivot.empty_display_type = "PLAIN_AXES"
        pivot.empty_display_size = 0.08
        pivot["bp_export"] = True
        pivot["bp_pivot"] = True
        self.result.pivots[name] = pivot
        return pivot

    def add_pivot_part(
        self,
        name: str,
        geometry,
        pivot_name: str,
        bone_name: str,
        role: str,
        palette_name: str,
    ) -> bpy.types.Object:
        if self.result is None or pivot_name not in self.result.pivots:
            raise ValueError(f"Unknown pedestrian pivot {pivot_name}")
        pivot = self.result.pivots[pivot_name]
        obj = self.add_part(
            name,
            geometry,
            bone_name,
            role,
            palette_name,
            origin=tuple(pivot.location),
        )
        # The Empty is an exported procedural anchor, not a transform parent.
        # Parenting a skinned FBX mesh through an auxiliary Empty introduces
        # a second centimetre conversion in Unity. The stable bp_pivot marker
        # keeps the intended binding explicit for the future presentation
        # driver without corrupting the current staged prefab.
        obj["bp_pivot"] = pivot_name
        return obj

    def build_pipeback_roller_body(self) -> None:
        """Build the sober seated rider around the unchanged Player rig.

        The source model remains in the canonical A-pose so it can copy the
        production Avatar exactly. PipebackIdle/PipebackRoll fold that body
        into the chair; the chair itself is rooted independently.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.045, 1.545), (0.105, 0.090, 0.132), 12, 6),
            "head", "body", "roller_skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between(
                (0, -0.008, 1.322), (0, -0.024, 1.438), 0.070, 0.061, 8
            ),
            "neck", "body", "roller_skin_dark",
        )
        self.add_part(
            "GEO_Torso",
            make_tapered_box(
                (0, 0.010, 0.805), (0, -0.002, 1.330),
                (0.300, 0.185, 0), (0.350, 0.205, 0),
            ),
            "chest", "body", "roller_coat_dark",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box(
                (0, 0.012, 0.670), (0, 0.010, 0.855),
                (0.310, 0.205, 0), (0.325, 0.205, 0),
            ),
            "pelvis", "body", "roller_coat",
        )
        arm_points = {
            "L": (
                (0.208, -0.004, 1.292), (0.470, -0.010, 1.175),
                (0.680, -0.018, 1.075), (0.755, -0.022, 1.035),
            ),
            "R": (
                (-0.208, 0.004, 1.292), (-0.470, -0.010, 1.175),
                (-0.680, -0.018, 1.075), (-0.755, -0.022, 1.035),
            ),
        }
        leg_points = {
            "L": ((0.083, 0.012, 0.750), (0.103, -0.012, 0.354), (0.112, -0.026, 0.095)),
            "R": ((-0.083, -0.004, 0.750), (-0.103, 0.012, 0.354), (-0.112, 0.018, 0.095)),
        }
        for side in ("L", "R"):
            shoulder, elbow, wrist, hand = arm_points[side]
            hip, knee, ankle = leg_points[side]
            sign = 1.0 if side == "L" else -1.0
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(shoulder, elbow, 0.075, 0.060, 8),
                f"upper_arm.{side}", "body", "roller_coat",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(wrist, elbow, 0.050, 0.062, 8),
                f"forearm.{side}", "body", "roller_coat_light",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid(hand, (0.070, 0.050, 0.060), 8, 4),
                f"hand.{side}", "hand", "roller_skin",
            )
            self.add_part(
                f"GEO_Thigh.{side}",
                make_frustum_between(hip, knee, 0.093, 0.073, 10),
                f"thigh.{side}", "body", "roller_trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                make_frustum_between(knee, ankle, 0.072, 0.058, 10),
                f"shin.{side}", "body", "roller_trousers",
            )
            self.add_part(
                f"GEO_Foot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.090, 0.0),
                    (sign * 0.112, -0.065, 0.135),
                    (0.155, 0.245, 0), (0.130, 0.180, 0),
                ),
                f"foot.{side}", "foot", "shoe",
            )
            self.add_part(
                f"ACC_ShoeSole.{side}",
                make_box((sign * 0.112, -0.090, 0.010), (0.162, 0.252, 0.020)),
                f"foot.{side}", "footwear_detail", "sole",
            )

        for side, x in (("L", 0.082), ("R", -0.082)):
            self.add_part(
                f"CLO_CoatFront.{side}",
                make_tapered_box(
                    (x, -0.110, 0.820), (x * 0.88, -0.112, 1.302),
                    (0.158, 0.036, 0), (0.175, 0.042, 0),
                ),
                "chest", "clothing",
                "roller_coat_light" if side == "L" else "roller_coat",
            )
        self.add_part(
            "CLO_CoatBack",
            make_tapered_box(
                (0, 0.110, 0.815), (0, 0.108, 1.305),
                (0.310, 0.040, 0), (0.350, 0.044, 0),
            ),
            "chest", "clothing", "roller_coat_dark",
        )
        self.add_part(
            "CLO_Collar",
            make_box((0, -0.014, 1.322), (0.325, 0.225, 0.052)),
            "chest", "clothing_detail", "roller_coat_light",
        )
        self.add_part(
            "ACC_Hair",
            make_tapered_box(
                (0, 0.010, 1.575), (0, -0.008, 1.700),
                (0.195, 0.175, 0), (0.155, 0.145, 0),
            ),
            "head", "clothing_detail", "roller_coat_dark",
        )
        for side, x in (("L", 0.052), ("R", -0.052)):
            self.add_part(
                f"ACC_Eye.{side}",
                make_box((x, -0.135, 1.572), (0.040, 0.018, 0.024)),
                "head", "face_detail", "void",
            )
        self.add_part(
            "ACC_Nose",
            make_tapered_box(
                (0, -0.145, 1.500), (0, -0.132, 1.548),
                (0.048, 0.056, 0), (0.035, 0.040, 0),
            ),
            "head", "face_detail", "roller_skin_dark",
        )
        self.add_part(
            "ACC_Mouth",
            make_box((0, -0.132, 1.472), (0.074, 0.022, 0.018)),
            "head", "face_detail", "void",
        )

    def build_pipeback_roller_chair(self) -> None:
        """Build the manual wheelchair and its impossible organ mechanism."""

        wheel_centers = PIPEBACK_WHEEL_CENTERS
        caster_centers = {
            "L": (0.205, -0.455, 0.065),
            "R": (-0.205, -0.455, 0.065),
        }
        for side in ("L", "R"):
            self.create_pivot(f"PIVOT_Wheel.{side}", wheel_centers[side])
        for side in ("L", "R"):
            self.create_pivot(f"PIVOT_Caster.{side}", caster_centers[side])
        self.create_pivot("PIVOT_Bellows", (0, 0.255, 0.650))
        self.create_pivot("PIVOT_PipeBank", (0, 0.292, 1.080))

        for side, center in wheel_centers.items():
            sign = 1.0 if side == "L" else -1.0
            pivot = f"PIVOT_Wheel.{side}"
            self.add_pivot_part(
                f"ACC_WheelTyre.{side}",
                make_torus_x(center, 0.272, 0.028, 12, 4),
                pivot, "root", "wheel_tyre", "chair_tyre",
            )
            self.add_pivot_part(
                f"ACC_PushRim.{side}",
                make_torus_x(
                    (center[0] + sign * 0.030, center[1], center[2]),
                    PIPEBACK_PUSH_RIM_RADIUS, 0.009, 10, 4,
                ),
                pivot, "root", "wheel_rim", "chair_rim",
            )
            spokes = []
            for index in range(6):
                angle = 2.0 * math.pi * index / 6
                end = (
                    center[0],
                    center[1] + math.cos(angle) * 0.250,
                    center[2] + math.sin(angle) * 0.250,
                )
                spokes.append(
                    make_frustum_between(center, end, 0.006, 0.004, 4, 1.0)
                )
            spokes.append(
                make_frustum_between(
                    (center[0] - 0.035, center[1], center[2]),
                    (center[0] + 0.035, center[1], center[2]),
                    0.030, 0.030, 8, 1.0,
                )
            )
            self.add_pivot_part(
                f"ACC_WheelSpokes.{side}",
                combine_geometry(*spokes),
                pivot, "root", "wheel_mechanism", "chair_frame_light",
            )

        for side, center in caster_centers.items():
            pivot = f"PIVOT_Caster.{side}"
            self.add_pivot_part(
                f"ACC_CasterTyre.{side}",
                make_torus_x(center, 0.052, 0.013, 10, 4),
                pivot, "root", "caster", "chair_tyre",
            )
            self.add_pivot_part(
                f"ACC_CasterHub.{side}",
                make_frustum_between(
                    (center[0] - 0.022, center[1], center[2]),
                    (center[0] + 0.022, center[1], center[2]),
                    0.022, 0.022, 8, 1.0,
                ),
                pivot, "root", "caster", "chair_frame_light",
            )

        frame_geometry = combine_geometry(
            make_frustum_between((-0.235, 0.170, 0.330), (-0.235, 0.205, 1.030), 0.020, 0.020, 8),
            make_frustum_between((0.235, 0.170, 0.330), (0.235, 0.205, 1.030), 0.020, 0.020, 8),
            make_frustum_between((-0.235, 0.155, 0.635), (-0.205, -0.455, 0.145), 0.018, 0.014, 8),
            make_frustum_between((0.235, 0.155, 0.635), (0.205, -0.455, 0.145), 0.018, 0.014, 8),
            make_frustum_between((-0.235, 0.175, 0.620), (0.235, 0.175, 0.620), 0.016, 0.016, 8),
            make_frustum_between((-0.235, -0.255, 0.315), (0.235, -0.255, 0.315), 0.014, 0.014, 8),
        )
        self.add_part(
            "ACC_ChairFrame", frame_geometry,
            "root", "wheelchair_frame", "chair_frame",
            origin=(0, 0.02, 0.60),
        )
        self.add_part(
            "ACC_SeatCushion",
            make_tapered_box(
                (0, -0.030, 0.635), (0, -0.020, 0.705),
                (0.440, 0.430, 0), (0.420, 0.410, 0),
            ),
            "root", "seat", "chair_seat", origin=(0, -0.025, 0.67),
        )
        self.add_part(
            "ACC_Backrest",
            make_tapered_box(
                (0, 0.215, 0.705), (0, 0.235, 1.055),
                (0.420, 0.055, 0), (0.385, 0.055, 0),
            ),
            "root", "seat", "chair_seat", origin=(0, 0.225, 0.88),
        )
        self.add_part(
            "ACC_Armrests",
            combine_geometry(
                make_box((0.255, -0.015, 0.825), (0.055, 0.410, 0.045)),
                make_box((-0.255, -0.015, 0.825), (0.055, 0.410, 0.045)),
            ),
            "root", "wheelchair_frame", "chair_frame_light",
            origin=(0, -0.015, 0.825),
        )
        self.add_part(
            "ACC_Footrests",
            combine_geometry(
                make_box((0.120, -0.505, 0.195), (0.190, 0.210, 0.030)),
                make_box((-0.120, -0.505, 0.195), (0.190, 0.210, 0.030)),
            ),
            "root", "footrest", "chair_frame_light",
            origin=(0, -0.505, 0.195),
        )
        self.add_part(
            "ACC_FootrestStems",
            combine_geometry(
                make_frustum_between((0.205, -0.240, 0.350), (0.120, -0.470, 0.220), 0.014, 0.012, 6),
                make_frustum_between((-0.205, -0.240, 0.350), (-0.120, -0.470, 0.220), 0.014, 0.012, 6),
            ),
            "root", "wheelchair_frame", "chair_frame",
            origin=(0, -0.35, 0.28),
        )
        self.add_part(
            "ACC_PushLevers",
            combine_geometry(*(
                geometry
                for sign in (1.0, -1.0)
                for geometry in (
                    make_frustum_between(
                        (sign * 0.395, -0.080, 0.750),
                        (sign * 0.250, 0.230, 1.000),
                        0.014, 0.014, 4, 1.0,
                    ),
                    make_frustum_between(
                        (sign * 0.250, 0.230, 1.000),
                        (sign * 0.720, 0.220, 1.180),
                        0.022, 0.020, 4, 1.0,
                    ),
                )
            )),
            "root", "drive_lever", "chair_rim",
            origin=(0, 0.10, 0.92),
        )

        bellows = combine_geometry(*(
            make_tapered_box(
                (0, 0.242 + index * 0.012, 0.555 + index * 0.035),
                (0, 0.242 + index * 0.012, 0.580 + index * 0.035),
                (0.300 - index * 0.018, 0.100, 0),
                (0.270 - index * 0.018, 0.092, 0),
            )
            for index in range(4)
        ))
        self.add_pivot_part(
            "ACC_Bellows", bellows, "PIVOT_Bellows",
            "pelvis", "bellows", "bellows",
        )

        pipe_specs = (
            (-0.205, 1.420, "pipe_brass_dark"),
            (-0.125, 1.610, "pipe_ivory"),
            (-0.040, 1.750, "pipe_brass_light"),
            (0.050, 1.560, "pipe_brass"),
            (0.135, 1.680, "pipe_ivory"),
            (0.215, 1.470, "pipe_brass_dark"),
        )
        for index, (x, top, palette) in enumerate(pipe_specs, start=1):
            bend = x + (0.022 if index % 2 == 0 else -0.018)
            geometry = combine_geometry(
                make_frustum_between(
                    (x * 0.72, 0.275, 0.760),
                    (bend, 0.305, 1.080),
                    0.025, 0.030, 8, 1.0,
                ),
                make_frustum_between(
                    (bend, 0.305, 1.080),
                    (x, 0.305, top - 0.090),
                    0.030, 0.032, 8, 1.0,
                ),
                make_frustum_between(
                    (x, 0.305, top - 0.090),
                    (x, 0.305, top),
                    0.052, 0.040, 10, 1.0,
                ),
            )
            self.add_pivot_part(
                f"ACC_OrganPipe.{index:02d}", geometry,
                "PIVOT_PipeBank", "chest", "signature_silhouette", palette,
            )
        self.add_pivot_part(
            "ACC_PipeManifold",
            make_tapered_box(
                (0, 0.285, 0.720), (0, 0.300, 0.825),
                (0.470, 0.130, 0), (0.420, 0.120, 0),
            ),
            "PIVOT_PipeBank", "chest", "pipe_manifold", "pipe_brass_dark",
        )

    def build_body(self) -> None:
        # A dark interior head exists only to give the open hood real depth.
        self.add_part(
            "GEO_FaceVoid",
            make_ellipsoid((0, -0.047, 1.555), (0.095, 0.078, 0.145), 12, 6),
            "head",
            "face_void",
            "void",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.325), (0, -0.025, 1.445), 0.072, 0.064),
            "neck",
            "body",
            "coat_dark",
        )
        self.add_part(
            "GEO_Torso",
            make_tapered_box((0, 0.010, 0.790), (0, -0.004, 1.335), (0.30, 0.18, 0), (0.34, 0.19, 0)),
            "chest",
            "body",
            "coat_dark",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box((0, 0.012, 0.665), (0, 0.010, 0.850), (0.29, 0.18, 0), (0.31, 0.18, 0)),
            "pelvis",
            "body",
            "coat",
        )

        limb_points = {
            "L": ((0.208, -0.004, 1.292), (0.470, -0.010, 1.175), (0.680, -0.018, 1.075), (0.755, -0.022, 1.035)),
            "R": ((-0.208, 0.004, 1.292), (-0.470, -0.010, 1.175), (-0.680, -0.018, 1.075), (-0.755, -0.022, 1.035)),
        }
        leg_points = {
            "L": ((0.083, 0.012, 0.750), (0.103, -0.012, 0.354), (0.112, -0.026, 0.095)),
            "R": ((-0.083, -0.004, 0.750), (-0.103, 0.012, 0.354), (-0.112, 0.018, 0.095)),
        }
        for side in ("L", "R"):
            shoulder, elbow, wrist, hand = limb_points[side]
            hip, knee, ankle = leg_points[side]
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(shoulder, elbow, 0.070, 0.058, 12),
                f"upper_arm.{side}",
                "body",
                "coat",
            )
            # The left sleeve is deliberately longer and heavier.
            forearm_end = hand if side == "L" else wrist
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(elbow, forearm_end, 0.062, 0.050 if side == "L" else 0.044, 12),
                f"forearm.{side}",
                "body",
                "coat_light" if side == "L" else "coat",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid(
                    tuple((v(wrist) + v(hand)) * 0.5),
                    (0.046, 0.035, 0.060),
                    10,
                    5,
                ),
                f"hand.{side}",
                "body",
                "glove",
            )
            self.add_part(
                f"GEO_Thigh.{side}",
                make_frustum_between(hip, knee, 0.092, 0.074, 12),
                f"thigh.{side}",
                "body",
                "trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                make_frustum_between(knee, ankle, 0.076, 0.060, 12),
                f"shin.{side}",
                "body",
                "trousers",
            )

        # Intentionally mismatched footwear: broad left rubber boot, narrow
        # right leather work shoe. Both keep the canonical 0 m sole contact.
        self.add_part(
            "GEO_Foot.L",
            make_tapered_box((0.112, -0.105, 0.000), (0.112, -0.070, 0.170), (0.205, 0.285, 0), (0.165, 0.205, 0)),
            "foot.L",
            "body",
            "rubber",
        )
        self.add_part(
            "GEO_Foot.R",
            make_tapered_box((-0.112, -0.080, 0.000), (-0.112, -0.055, 0.135), (0.150, 0.235, 0), (0.125, 0.175, 0)),
            "foot.R",
            "body",
            "leather",
        )

    def build_clothing_and_details(self) -> None:
        # Long coat panels stop above the knee swing envelope. Their broken
        # lower line reads as worn cloth without requiring skinning or cloth.
        self.add_part(
            "CLO_CoatFront.L",
            make_tapered_box((0.082, -0.105, 0.675), (0.072, -0.102, 1.300), (0.155, 0.035, 0), (0.170, 0.040, 0)),
            "chest",
            "clothing",
            "coat",
        )
        self.add_part(
            "CLO_CoatFront.R",
            make_tapered_box((-0.082, -0.105, 0.705), (-0.072, -0.102, 1.300), (0.155, 0.035, 0), (0.170, 0.040, 0)),
            "chest",
            "clothing",
            "coat_light",
        )
        self.add_part(
            "CLO_CoatBack",
            make_tapered_box((0, 0.105, 0.690), (0, 0.105, 1.305), (0.310, 0.038, 0), (0.335, 0.040, 0)),
            "chest",
            "clothing",
            "coat_dark",
        )
        self.add_part(
            "CLO_CoatCollar.L",
            make_tapered_box((0.082, -0.116, 1.245), (0.040, -0.122, 1.390), (0.105, 0.025, 0), (0.065, 0.025, 0)),
            "chest",
            "clothing_detail",
            "coat_dark",
        )
        self.add_part(
            "CLO_CoatCollar.R",
            make_tapered_box((-0.082, -0.116, 1.245), (-0.040, -0.122, 1.390), (0.105, 0.025, 0), (0.065, 0.025, 0)),
            "chest",
            "clothing_detail",
            "coat_dark",
        )
        self.add_part(
            "CLO_LongCuff.L",
            make_frustum_between((0.625, -0.017, 1.102), (0.735, -0.021, 1.048), 0.057, 0.054, 12),
            "forearm.L",
            "clothing_detail",
            "coat_dark",
        )
        self.add_part(
            "CLO_ShortCuff.R",
            make_frustum_between((-0.610, -0.017, 1.110), (-0.680, -0.018, 1.075), 0.052, 0.045, 12),
            "forearm.R",
            "clothing_detail",
            "coat_dark",
        )

        # Tall square backpack. It has no diagonal hero-like satchel strap.
        self.add_part(
            "ACC_TallBackpack",
            make_tapered_box((0, 0.155, 0.805), (0, 0.155, 1.435), (0.315, 0.155, 0), (0.285, 0.145, 0)),
            "chest",
            "accessory",
            "bag",
        )
        self.add_part(
            "ACC_BackpackCap",
            make_box((0, 0.157, 1.445), (0.285, 0.155, 0.055)),
            "chest",
            "accessory_detail",
            "bag_edge",
        )
        self.add_part(
            "ACC_BackpackSide.L",
            make_box((0.155, 0.160, 1.125), (0.035, 0.165, 0.500)),
            "chest",
            "accessory_detail",
            "bag_edge",
        )
        self.add_part(
            "ACC_BackpackSide.R",
            make_box((-0.155, 0.160, 1.125), (0.035, 0.165, 0.500)),
            "chest",
            "accessory_detail",
            "bag_edge",
        )

        # Crumpled asymmetric trapezoid hood / lampshade. The main solid sits
        # behind a recessed void plate so the face reads as absence, while the
        # single amber mark remains ordinary non-emissive paint.
        self.add_part(
            "ACC_LampshadeHood",
            make_tapered_box((0.015, -0.030, 1.405), (-0.020, -0.010, 1.750), (0.390, 0.330, 0), (0.235, 0.225, 0)),
            "head",
            "signature_silhouette",
            "hood",
        )
        self.add_part(
            "ACC_HoodDarkOpening",
            make_tapered_box((0.012, -0.198, 1.445), (0.000, -0.130, 1.670), (0.270, 0.018, 0), (0.165, 0.018, 0)),
            "head",
            "face_void",
            "void",
        )
        self.add_part(
            "ACC_HoodBentRim",
            make_tapered_box((0.018, -0.055, 1.395), (0.005, -0.052, 1.435), (0.425, 0.355, 0), (0.385, 0.325, 0)),
            "head",
            "signature_silhouette",
            "hood_dark",
        )
        self.add_part(
            "ACC_HoodCrease.L",
            make_tapered_box((0.118, -0.177, 1.485), (0.082, -0.126, 1.690), (0.035, 0.012, 0), (0.025, 0.012, 0)),
            "head",
            "surface_detail",
            "hood_light",
        )
        self.add_part(
            "ACC_HoodCrease.R",
            make_tapered_box((-0.132, -0.164, 1.470), (-0.085, -0.120, 1.655), (0.025, 0.012, 0), (0.032, 0.012, 0)),
            "head",
            "surface_detail",
            "hood_dark",
        )
        self.add_part(
            "ACC_AmberFaceMark",
            make_box((0.055, -0.210, 1.555), (0.058, 0.010, 0.046)),
            "head",
            "face_detail_non_emissive",
            "amber",
        )

        for index, z in enumerate((0.825, 0.980, 1.135), start=1):
            self.add_part(
                f"ACC_CoatButton.{index:02d}",
                make_ellipsoid((0.018, -0.132, z), (0.017, 0.010, 0.017), 8, 4),
                "chest",
                "clothing_detail",
                "button",
            )
        self.add_part(
            "ACC_LeftBootSole",
            make_box((0.112, -0.105, 0.012), (0.215, 0.295, 0.024)),
            "foot.L",
            "footwear_detail",
            "sole",
        )
        self.add_part(
            "ACC_RightBootSole",
            make_box((-0.112, -0.080, 0.010), (0.160, 0.245, 0.020)),
            "foot.R",
            "footwear_detail",
            "sole",
        )

    def build_chair_carrier_body(self) -> None:
        """Build compact workwear around the unchanged canonical A-pose rig."""

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.038, 1.555), (0.105, 0.090, 0.140), 12, 6),
            "head", "body", "skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.325), (0, -0.025, 1.445), 0.072, 0.064),
            "neck", "body", "skin",
        )
        self.add_part(
            "GEO_Torso",
            make_tapered_box((0, 0.010, 0.785), (0, -0.004, 1.335), (0.300, 0.190, 0), (0.360, 0.205, 0)),
            "chest", "body", "work_jacket_dark",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box((0, 0.012, 0.665), (0, 0.010, 0.850), (0.295, 0.185, 0), (0.315, 0.190, 0)),
            "pelvis", "body", "work_jacket",
        )
        limb_points = {
            "L": ((0.208, -0.004, 1.292), (0.470, -0.010, 1.175), (0.680, -0.018, 1.075), (0.755, -0.022, 1.035)),
            "R": ((-0.208, 0.004, 1.292), (-0.470, -0.010, 1.175), (-0.680, -0.018, 1.075), (-0.755, -0.022, 1.035)),
        }
        leg_points = {
            "L": ((0.083, 0.012, 0.750), (0.103, -0.012, 0.354), (0.112, -0.026, 0.095)),
            "R": ((-0.083, -0.004, 0.750), (-0.103, 0.012, 0.354), (-0.112, 0.018, 0.095)),
        }
        for side in ("L", "R"):
            shoulder, elbow, wrist, hand = limb_points[side]
            hip, knee, ankle = leg_points[side]
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(shoulder, elbow, 0.071, 0.057, 12),
                f"upper_arm.{side}", "body", "work_jacket",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(elbow, wrist, 0.059, 0.045, 12),
                f"forearm.{side}", "body", "work_jacket_light" if side == "L" else "work_jacket",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid(tuple((v(wrist) + v(hand)) * 0.5), (0.046, 0.035, 0.060), 10, 5),
                f"hand.{side}", "body", "skin",
            )
            self.add_part(
                f"GEO_Thigh.{side}", make_frustum_between(hip, knee, 0.092, 0.074, 12),
                f"thigh.{side}", "body", "work_trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}", make_frustum_between(knee, ankle, 0.076, 0.060, 12),
                f"shin.{side}", "body", "work_trousers",
            )
            x = 0.112 if side == "L" else -0.112
            self.add_part(
                f"GEO_Foot.{side}",
                make_tapered_box((x, -0.095, 0.0), (x, -0.065, 0.145), (0.170, 0.250, 0), (0.140, 0.185, 0)),
                f"foot.{side}", "body", "shoe",
            )

    def build_chair_carrier_details(self) -> None:
        # A faded waist-length jacket keeps the carrier more compact and
        # upright than the long-coated Lampshade Walker.
        for side, x in (("L", 0.084), ("R", -0.084)):
            self.add_part(
                f"CLO_JacketFront.{side}",
                make_tapered_box((x, -0.112, 0.815), (x * 0.88, -0.113, 1.300), (0.158, 0.036, 0), (0.178, 0.042, 0)),
                "chest", "clothing", "work_jacket_light" if side == "L" else "work_jacket",
            )
            self.add_part(
                f"ACC_ShoulderLoop.{side}",
                make_tapered_box((x * 1.22, -0.125, 0.975), (x * 1.42, -0.112, 1.315), (0.028, 0.018, 0), (0.035, 0.018, 0)),
                "chest", "load_harness", "strap_cloth",
            )
            self.add_part(
                f"ACC_ShoeSole.{side}",
                make_box((0.112 if side == "L" else -0.112, -0.095, 0.011), (0.180, 0.260, 0.022)),
                f"foot.{side}", "footwear_detail", "sole",
            )

        self.add_part(
            "CLO_JacketBack",
            make_tapered_box((0, 0.110, 0.805), (0, 0.112, 1.305), (0.310, 0.040, 0), (0.350, 0.044, 0)),
            "chest", "clothing", "work_jacket_dark",
        )
        self.add_part(
            "CLO_JacketHem",
            make_box((0, -0.010, 0.790), (0.335, 0.215, 0.055)),
            "chest", "clothing_detail", "work_jacket_dark",
        )
        self.add_part(
            "ACC_WorkCap",
            make_tapered_box((0, -0.015, 1.655), (0, -0.010, 1.750), (0.225, 0.205, 0), (0.190, 0.180, 0)),
            "head", "clothing_detail", "work_jacket_dark",
        )
        self.add_part(
            "ACC_CapPeak",
            make_box((0, -0.145, 1.668), (0.175, 0.115, 0.025)),
            "head", "clothing_detail", "work_jacket_dark",
        )
        self.add_part(
            "ACC_FaceShadow",
            make_box((0, -0.130, 1.560), (0.160, 0.020, 0.055)),
            "head", "face_detail", "void",
        )

        # The upside-down cafe chair is tied to the chest: the broad seat is
        # behind the shoulder blades and four narrow legs rise around the head
        # as a clear cage silhouette. Nothing is a separate simulated prop.
        self.add_part(
            "ACC_ChairSeat",
            make_tapered_box((0, 0.245, 1.245), (0, 0.265, 1.335), (0.545, 0.400, 0), (0.500, 0.360, 0)),
            "chest", "signature_silhouette", "chair_wood",
        )
        self.add_part(
            "ACC_ChairSeatWear",
            make_box((0.080, 0.062, 1.305), (0.175, 0.020, 0.050)),
            "chest", "surface_detail", "chair_wear",
        )
        leg_specs = (
            ("Front.L", (0.225, 0.075, 1.315), (0.245, 0.055, 1.735)),
            ("Front.R", (-0.225, 0.075, 1.315), (-0.245, 0.055, 1.735)),
            ("Back.L", (0.225, 0.420, 1.315), (0.285, 0.445, 1.725)),
            ("Back.R", (-0.225, 0.420, 1.315), (-0.285, 0.445, 1.725)),
        )
        for suffix, start, end in leg_specs:
            self.add_part(
                f"ACC_ChairLeg.{suffix}", make_frustum_between(start, end, 0.034, 0.027, 8, 0.88),
                "chest", "signature_silhouette", "chair_wood",
            )
        self.add_part(
            "ACC_ChairCrossbar",
            make_frustum_between((-0.285, 0.445, 1.650), (0.285, 0.445, 1.650), 0.027, 0.027, 8, 0.88),
            "chest", "signature_silhouette", "chair_edge",
        )
        self.add_part(
            "ACC_LoadBelt",
            make_box((0, -0.126, 1.035), (0.350, 0.024, 0.055)),
            "chest", "load_harness", "strap_cloth",
        )

    def build_kettle_hat_body(self) -> None:
        """Build a stout short-legged body under the canonical A-pose rig.

        The rig, its bone positions and the `1.75 m` envelope are shared with
        every other walker, so "short" is authored as proportion rather than
        scale: the human mass stops near `1.40 m`, an overhanging belly hides
        the upper legs, and the oversized kettle owns the rest of the height.
        Every part still sits close to its own bone head so the shared
        locomotion clips rotate it correctly.
        """

        # Small head sunk into the shoulders. It rides 0.13 m below the head
        # bone so the kettle can cap the silhouette while the face stays
        # visible beneath the tilted rim.
        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.030, 1.330), (0.142, 0.134, 0.124), 12, 6),
            "head", "body", "skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.012, 1.185), (0, -0.022, 1.275), 0.105, 0.098, 10),
            "neck", "body", "stout_coat_dark",
        )
        self.add_part(
            "GEO_Torso",
            make_tapered_box((0, -0.010, 1.000), (0, -0.016, 1.285), (0.400, 0.310, 0), (0.355, 0.275, 0)),
            "chest", "body", "stout_coat_dark",
        )
        # The signature mass. It is parented to the pelvis so it leads the
        # waddle while the kettle counter-swings on the head. Nothing else in
        # the silhouette is allowed to be wider.
        self.add_part(
            "GEO_Belly",
            make_ellipsoid((0, -0.070, 0.815), (0.350, 0.315, 0.265), 14, 7),
            "pelvis", "signature_silhouette", "stout_coat",
        )
        # Structural hips only: deliberately narrower and shorter than the
        # belly so no box edge can break its round overhang.
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box((0, 0.008, 0.620), (0, 0.004, 0.800), (0.300, 0.240, 0), (0.330, 0.260, 0)),
            "pelvis", "body", "stout_coat_dark",
        )

        limb_points = {
            "L": ((0.208, -0.004, 1.292), (0.470, -0.010, 1.175)),
            "R": ((-0.208, 0.004, 1.292), (-0.470, -0.010, 1.175)),
        }
        leg_points = {
            "L": ((0.083, 0.012, 0.750), (0.103, -0.012, 0.354), (0.112, -0.026, 0.095)),
            "R": ((-0.083, -0.004, 0.750), (-0.103, 0.012, 0.354), (-0.112, 0.018, 0.095)),
        }
        for side in ("L", "R"):
            shoulder, elbow = limb_points[side]
            hip, knee, ankle = leg_points[side]
            sign = 1.0 if side == "L" else -1.0
            # Stubby arms: each visible segment covers only part of its bone,
            # so the arms read as tiny without leaving their own pivots.
            forearm_start = (sign * 0.352, -0.007, 1.223)
            forearm_end = (sign * 0.512, -0.014, 1.166)
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(shoulder, forearm_start, 0.084, 0.074, 10),
                f"upper_arm.{side}", "body", "stout_coat",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(forearm_start, forearm_end, 0.072, 0.062, 10),
                f"forearm.{side}", "body",
                "stout_coat_light" if side == "L" else "stout_coat",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid((sign * 0.572, -0.017, 1.138), (0.054, 0.044, 0.056), 8, 4),
                f"hand.{side}", "body", "glove",
            )
            # Thick, closely spaced legs. Only the band between the coat hem
            # and the boot top stays visible.
            self.add_part(
                f"GEO_Thigh.{side}",
                make_frustum_between(hip, knee, 0.118, 0.102, 12),
                f"thigh.{side}", "body", "stout_trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                make_frustum_between(knee, ankle, 0.100, 0.086, 12),
                f"shin.{side}", "body", "stout_trousers",
            )
            x = sign * 0.112
            self.add_part(
                f"GEO_Foot.{side}",
                make_tapered_box((x, -0.088, 0.0), (x, -0.062, 0.190), (0.215, 0.290, 0), (0.185, 0.230, 0)),
                f"foot.{side}", "body", "leather",
            )

    def build_kettle_hat_details(self) -> None:
        # Grounded soles under the supported ShoeSole.L/R naming; these two
        # boxes own the exact z=0 contact for the whole silhouette.
        for side in ("L", "R"):
            x = 0.112 if side == "L" else -0.112
            self.add_part(
                f"ACC_ShoeSole.{side}",
                make_box((x, -0.088, 0.013), (0.222, 0.296, 0.026)),
                f"foot.{side}", "footwear_detail", "sole",
            )

        # A short coat that ends in a flared ring under the belly, so the
        # tiny legs read against a round overhang rather than a flat slab.
        # The hem tapers downward with the belly instead of flaring past it,
        # which would read as a plate rather than cloth.
        self.add_part(
            "CLO_CoatHem",
            make_frustum_between((0, -0.070, 0.650), (0, -0.070, 0.572), 0.288, 0.212, 14, 0.90),
            "pelvis", "clothing", "stout_coat_dark",
        )
        self.add_part(
            "CLO_CoatSeam",
            make_tapered_box((0.012, -0.352, 0.660), (0.008, -0.318, 1.020), (0.062, 0.055, 0), (0.070, 0.055, 0)),
            "pelvis", "clothing", "stout_coat_light",
        )
        self.add_part(
            "CLO_CoatBack",
            make_tapered_box((0, 0.222, 0.700), (0, 0.196, 1.030), (0.330, 0.055, 0), (0.290, 0.052, 0)),
            "pelvis", "clothing", "stout_coat_dark",
        )
        self.add_part(
            "CLO_CoatCollar",
            make_box((0, -0.018, 1.232), (0.340, 0.295, 0.058)),
            "chest", "clothing_detail", "stout_coat_light",
        )
        for index, (z, y) in enumerate(
            ((0.735, -0.336), (0.855, -0.352), (0.975, -0.330)), start=1
        ):
            self.add_part(
                f"ACC_CoatButton.{index:02d}",
                make_box((0.012, y, z), (0.038, 0.026, 0.038)),
                "pelvis", "clothing_detail", "button",
            )

        # Face. It reads only because the kettle rim clears the eyes; the
        # Lampshade deliberately hides its face, so this one must not.
        for side, x in (("L", 0.058), ("R", -0.058)):
            self.add_part(
                f"ACC_Eye.{side}",
                make_box((x, -0.148, 1.350), (0.042, 0.020, 0.032)),
                "head", "face_detail", "void",
            )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.164, 1.300), (0, -0.148, 1.352), (0.060, 0.072, 0), (0.046, 0.054, 0)),
            "head", "face_detail", "skin",
        )
        self.add_part(
            "ACC_Moustache",
            make_box((0, -0.150, 1.288), (0.124, 0.032, 0.026)),
            "head", "face_detail", "void",
        )

        # The oversized enamel kettle. Its axis is tilted so the rim sits low
        # on the left and clears the face on the right, and every piece is
        # bound to the head bone as one rigid hat.
        # Wide and squat rather than tall: the body is 2.3x the head radius,
        # and the tall handle arc carries the rest of the envelope. The tilt
        # drops the rim past the character's left temple while the front rim
        # still clears both eyes.
        self.add_part(
            "ACC_KettleBody",
            make_frustum_between((0.050, 0.024, 1.430), (0.005, -0.002, 1.585), 0.335, 0.270, 14, 0.94),
            "head", "signature_silhouette", "kettle_enamel",
        )
        self.add_part(
            "ACC_KettleRimBand",
            make_frustum_between((0.053, 0.026, 1.421), (0.046, 0.022, 1.453), 0.343, 0.339, 14, 0.94),
            "head", "signature_silhouette", "kettle_enamel_dark",
        )
        self.add_part(
            "ACC_KettleShoulder",
            make_frustum_between((0.005, -0.002, 1.583), (-0.004, -0.007, 1.618), 0.268, 0.175, 14, 0.94),
            "head", "signature_silhouette", "kettle_enamel",
        )
        self.add_part(
            "ACC_KettleLid",
            make_frustum_between((-0.004, -0.007, 1.615), (-0.010, -0.010, 1.646), 0.180, 0.138, 12, 0.94),
            "head", "signature_silhouette", "kettle_enamel_dark",
        )
        self.add_part(
            "ACC_KettleKnob",
            make_ellipsoid((-0.012, -0.011, 1.656), (0.046, 0.044, 0.030), 8, 4),
            "head", "surface_detail", "kettle_metal",
        )
        # Spout. It reaches mostly sideways rather than forward so it stays a
        # readable profile from the orbiting chase camera instead of pointing
        # at it; this is the detail that separates a kettle from a lampshade.
        self.add_part(
            "ACC_KettleSpout",
            make_frustum_between((0.255, -0.105, 1.448), (0.442, -0.190, 1.548), 0.082, 0.052, 10, 0.92),
            "head", "signature_silhouette", "kettle_enamel",
        )
        self.add_part(
            "ACC_KettleSpoutTip",
            make_frustum_between((0.442, -0.190, 1.548), (0.486, -0.212, 1.606), 0.052, 0.040, 8, 0.92),
            "head", "surface_detail", "kettle_enamel_dark",
        )
        # Handle arc. The flat top bar owns the exact 1.75 m envelope.
        for side, x in (("L", 0.232), ("R", -0.220)):
            self.add_part(
                f"ACC_KettleHandlePost.{side}",
                make_tapered_box((x, 0.010, 1.482), (x * 0.68, 0.000, 1.714), (0.056, 0.058, 0), (0.046, 0.052, 0)),
                "head", "signature_silhouette", "kettle_metal",
            )
        self.add_part(
            "ACC_KettleHandleTop",
            make_box((-0.008, 0.000, 1.7275), (0.330, 0.054, 0.045)),
            "head", "signature_silhouette", "kettle_metal",
        )
        for index, (x, y, z) in enumerate(
            ((0.216, -0.216, 1.516), (-0.196, -0.238, 1.470)), start=1
        ):
            self.add_part(
                f"ACC_KettleChip.{index:02d}",
                make_box((x, y, z), (0.070, 0.048, 0.056)),
                "head", "surface_detail", "kettle_chip",
            )

    def build_long_arm_body(self) -> None:
        """Build a narrow tall body whose forearms reach the pavement.

        This is the first walker whose strangeness is the body itself rather
        than a worn or carried object, so nothing here is a prop. The visible
        forearm hangs almost straight down from the elbow instead of following
        the outward A-pose bone axis: doubling the bone direction would push
        the rest silhouette past the `1.65 m` width guard, and hanging the
        segment below its own pivot is exactly what makes it swing as a
        pendulum once the shoulder rotates.
        """

        # Small skull sunk between raised shoulders. The hair cap is a box so
        # it can own the exact 1.75 m envelope.
        self.add_part(
            "GEO_Skull",
            make_ellipsoid((0, -0.022, 1.570), (0.108, 0.100, 0.115), 12, 6),
            "head", "body", "pale_skin",
        )
        # Matted hair lying flat on the skull and pushed back so the face
        # stays bare. It owns the exact 1.75 m envelope. It must never widen
        # past the skull: an overhanging brim would echo the Lampshade Walker,
        # which is the one silhouette this design has to stay clear of.
        self.add_part(
            "ACC_Hair",
            make_ellipsoid((0, 0.010, 1.612), (0.112, 0.100, 0.138), 10, 5),
            "head", "clothing_detail", "steel_coat_dark",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.006, 1.428), (0, -0.014, 1.492), 0.054, 0.050, 8),
            "neck", "body", "pale_skin_dark",
        )
        # Narrow slab of a torso: half the Kettle Hat Walker's width.
        self.add_part(
            "GEO_Torso",
            make_tapered_box((0, -0.005, 0.870), (0, -0.012, 1.410), (0.250, 0.185, 0), (0.330, 0.205, 0)),
            "chest", "body", "steel_coat",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box((0, 0.004, 0.640), (0, 0.000, 0.880), (0.215, 0.165, 0), (0.245, 0.180, 0)),
            "pelvis", "body", "steel_coat_dark",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            # Shoulders pulled up around the head, so there is barely a neck.
            self.add_part(
                f"GEO_ShoulderCap.{side}",
                make_ellipsoid((sign * 0.150, -0.008, 1.428), (0.100, 0.092, 0.084), 8, 4),
                "chest", "body", "steel_coat",
            )
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(
                    (sign * 0.208, -0.004, 1.292),
                    (sign * 0.470, -0.010, 1.175),
                    0.062, 0.052, 8,
                ),
                f"upper_arm.{side}", "body", "steel_coat",
            )
            # The signature: a bare forearm roughly 3.3x its bone length,
            # hanging from the elbow to just above the ankle.
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(
                    (sign * 0.470, -0.010, 1.175),
                    (sign * 0.556, -0.020, 0.292),
                    0.048, 0.041, 10,
                ),
                f"forearm.{side}", "signature_silhouette", "pale_skin",
            )
            # Heavy oversized hands finish the pendulum. Their resting height
            # is what the animated clearance band is measured against.
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid((sign * 0.570, -0.024, 0.210), (0.080, 0.064, 0.094), 10, 5),
                f"hand.{side}", "signature_silhouette", "pale_skin",
            )
            self.add_part(
                f"GEO_Thigh.{side}",
                make_frustum_between(
                    (sign * 0.083, 0.012 * sign, 0.750),
                    (sign * 0.103, -0.012 * sign, 0.354),
                    0.072, 0.058, 10,
                ),
                f"thigh.{side}", "body", "steel_trousers",
            )
            # Bare thin ankles below a short trouser cuff.
            self.add_part(
                f"GEO_Shin.{side}",
                make_frustum_between(
                    (sign * 0.103, -0.012 * sign, 0.354),
                    (sign * 0.112, -0.026 * sign, 0.095),
                    0.050, 0.038, 8,
                ),
                f"shin.{side}", "body", "pale_skin_dark",
            )
            self.add_part(
                f"GEO_Foot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.070, 0.0),
                    (sign * 0.112, -0.055, 0.072),
                    (0.115, 0.215, 0), (0.100, 0.180, 0),
                ),
                f"foot.{side}", "body", "shoe",
            )

    def build_long_arm_details(self) -> None:
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"ACC_ShoeSole.{side}",
                make_box((sign * 0.112, -0.070, 0.010), (0.122, 0.222, 0.020)),
                f"foot.{side}", "footwear_detail", "sole",
            )
            # The jacket sleeve stops at the elbow, so the length reads as an
            # arm rather than as cloth.
            self.add_part(
                f"CLO_SleeveCap.{side}",
                make_frustum_between(
                    (sign * 0.200, -0.004, 1.294),
                    (sign * 0.452, -0.010, 1.184),
                    0.076, 0.062, 8,
                ),
                f"upper_arm.{side}", "clothing",
                "steel_coat_light" if side == "L" else "steel_coat",
            )
            self.add_part(
                f"CLO_TrouserCuff.{side}",
                make_frustum_between(
                    (sign * 0.103, -0.012 * sign, 0.356),
                    (sign * 0.108, -0.020 * sign, 0.215),
                    0.070, 0.060, 8,
                ),
                f"shin.{side}", "clothing", "steel_trousers",
            )
            self.add_part(
                f"CLO_JacketFront.{side}",
                make_tapered_box(
                    (sign * 0.072, -0.108, 0.885),
                    (sign * 0.086, -0.116, 1.352),
                    (0.128, 0.032, 0), (0.150, 0.036, 0),
                ),
                "chest", "clothing",
                "steel_coat_light" if side == "L" else "steel_coat",
            )
        self.add_part(
            "CLO_JacketBack",
            make_tapered_box((0, 0.100, 0.880), (0, 0.108, 1.360), (0.262, 0.036, 0), (0.336, 0.038, 0)),
            "chest", "clothing", "steel_coat_dark",
        )
        self.add_part(
            "CLO_JacketHem",
            make_box((0, -0.006, 0.882), (0.290, 0.208, 0.046)),
            "chest", "clothing_detail", "steel_coat_dark",
        )
        self.add_part(
            "CLO_Collar",
            make_box((0, -0.010, 1.418), (0.286, 0.212, 0.050)),
            "chest", "clothing_detail", "steel_coat_light",
        )
        for index, z in enumerate((0.980, 1.130), start=1):
            self.add_part(
                f"ACC_JacketButton.{index:02d}",
                make_box((0.010, -0.126, z), (0.030, 0.022, 0.030)),
                "chest", "clothing_detail", "button",
            )
        # Eyes sit almost at the hairline and there is no mouth at all. The
        # low nose is what makes the eye placement read as wrong rather than
        # merely stylised.
        for side, x in (("L", 0.046), ("R", -0.046)):
            self.add_part(
                f"ACC_Eye.{side}",
                make_box((x, -0.098, 1.608), (0.038, 0.020, 0.026)),
                "head", "face_detail", "void",
            )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.118, 1.478), (0, -0.102, 1.530), (0.044, 0.056, 0), (0.034, 0.042, 0)),
            "head", "face_detail", "pale_skin",
        )

    def build_helmet_lamp_body(self) -> None:
        """Build a squat hopper in miner's work wear with oversized feet.

        Anthropomorphic throughout: an ordinary human rig, ordinary arms, an
        ordinary head. Only two things are wrong, and both are functional —
        the hind feet are long enough to launch a hop, and the helmet carries
        a lamp that is genuinely switched on. The Unity prefab hangs a real
        shadowless Spot off the head bone at the lens position.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.030, 1.478), (0.114, 0.106, 0.118), 12, 6),
            "head", "body", "skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.008, 1.318), (0, -0.018, 1.392), 0.070, 0.064, 8),
            "neck", "body", "miner_ochre_dark",
        )
        # Barrel chest and a low slung pelvis: the body is coiled even before
        # the crouch that the clips add on top.
        self.add_part(
            "GEO_Torso",
            make_tapered_box((0, -0.004, 0.860), (0, -0.014, 1.318), (0.335, 0.245, 0), (0.360, 0.250, 0)),
            "chest", "body", "miner_ochre",
        )
        self.add_part(
            "GEO_Pelvis",
            make_tapered_box((0, 0.006, 0.615), (0, 0.002, 0.870), (0.305, 0.225, 0), (0.330, 0.240, 0)),
            "pelvis", "body", "miner_trousers",
        )
        limb_points = {
            "L": ((0.208, -0.004, 1.292), (0.470, -0.010, 1.175), (0.680, -0.018, 1.075), (0.755, -0.022, 1.035)),
            "R": ((-0.208, 0.004, 1.292), (-0.470, -0.010, 1.175), (-0.680, -0.018, 1.075), (-0.755, -0.022, 1.035)),
        }
        leg_points = {
            "L": ((0.083, 0.012, 0.750), (0.103, -0.012, 0.354), (0.112, -0.026, 0.095)),
            "R": ((-0.083, -0.004, 0.750), (-0.103, 0.012, 0.354), (-0.112, 0.018, 0.095)),
        }
        for side in ("L", "R"):
            shoulder, elbow, wrist, hand = limb_points[side]
            hip, knee, ankle = leg_points[side]
            sign = 1.0 if side == "L" else -1.0
            self.add_part(
                f"GEO_UpperArm.{side}",
                make_frustum_between(shoulder, elbow, 0.074, 0.062, 10),
                f"upper_arm.{side}", "body", "miner_ochre",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(elbow, wrist, 0.064, 0.050, 10),
                f"forearm.{side}", "body",
                "miner_ochre_light" if side == "L" else "miner_ochre",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_ellipsoid(tuple((v(wrist) + v(hand)) * 0.5), (0.050, 0.040, 0.062), 8, 4),
                f"hand.{side}", "body", "miner_rubber",
            )
            # Powerful, heavily tapered legs to sell the launch.
            self.add_part(
                f"GEO_Thigh.{side}",
                make_frustum_between(hip, knee, 0.108, 0.078, 12),
                f"thigh.{side}", "body", "miner_trousers",
            )
            self.add_part(
                f"GEO_Shin.{side}",
                make_frustum_between(knee, ankle, 0.086, 0.062, 10),
                f"shin.{side}", "body", "miner_trousers",
            )
            # The signature: a `0.46 m` hind foot, more than twice the length
            # every other walker uses. It reads as a hopper standing still.
            x = sign * 0.112
            self.add_part(
                f"GEO_Foot.{side}",
                make_tapered_box(
                    (x, -0.150, 0.0), (x, -0.118, 0.158),
                    (0.152, 0.460, 0), (0.132, 0.372, 0),
                ),
                f"foot.{side}", "signature_silhouette", "miner_rubber",
            )

    def build_helmet_lamp_details(self) -> None:
        for side in ("L", "R"):
            x = 0.112 if side == "L" else -0.112
            self.add_part(
                f"ACC_ShoeSole.{side}",
                make_box((x, -0.150, 0.012), (0.158, 0.468, 0.024)),
                f"foot.{side}", "footwear_detail", "sole",
            )
            self.add_part(
                f"ACC_BootCuff.{side}",
                make_frustum_between(
                    (x, -0.024, 0.150), (x, -0.026, 0.235), 0.098, 0.088, 8,
                ),
                f"foot.{side}", "footwear_detail", "miner_ochre_dark",
            )
        self.add_part(
            "CLO_JacketHem",
            make_box((0, -0.004, 0.876), (0.352, 0.256, 0.052)),
            "chest", "clothing_detail", "miner_ochre_dark",
        )
        self.add_part(
            "CLO_HighVisBand",
            make_box((0, -0.006, 1.120), (0.368, 0.258, 0.058)),
            "chest", "clothing_detail", "miner_hivis",
        )
        self.add_part(
            "CLO_Collar",
            make_box((0, -0.010, 1.316), (0.290, 0.226, 0.046)),
            "chest", "clothing_detail", "miner_ochre_dark",
        )
        # Battery box on the belt, wired to the helmet. It is what makes the
        # lamp read as equipment rather than decoration.
        self.add_part(
            "ACC_BatteryBox",
            make_box((0.158, 0.126, 0.930), (0.108, 0.078, 0.146)),
            "pelvis", "load_harness", "miner_helmet_dark",
        )
        cable_points = (
            ((0.158, 0.150, 1.002), (0.132, 0.168, 1.176)),
            ((0.132, 0.168, 1.176), (0.086, 0.150, 1.336)),
            ((0.086, 0.150, 1.336), (0.040, 0.116, 1.470)),
        )
        for index, (start, end) in enumerate(cable_points, start=1):
            self.add_part(
                f"ACC_LampCable.{index:02d}",
                make_frustum_between(start, end, 0.016, 0.014, 6, 0.90),
                "chest" if index < 3 else "head", "surface_detail", "miner_cable",
            )

        # Battered helmet. The dome owns the exact 1.75 m envelope.
        self.add_part(
            "ACC_HelmetDome",
            make_ellipsoid((0, -0.020, 1.605), (0.136, 0.142, 0.145), 12, 6),
            "head", "signature_silhouette", "miner_helmet",
        )
        self.add_part(
            "ACC_HelmetBrim",
            make_tapered_box((0, -0.028, 1.556), (0, -0.026, 1.584), (0.298, 0.316, 0), (0.268, 0.286, 0)),
            "head", "signature_silhouette", "miner_helmet_dark",
        )
        self.add_part(
            "ACC_HelmetRidge",
            make_tapered_box((0, -0.020, 1.598), (0, -0.020, 1.746), (0.048, 0.268, 0), (0.036, 0.190, 0)),
            "head", "surface_detail", "miner_helmet_dark",
        )
        # Lamp housing and lens. The Unity Spot is anchored at the lens.
        self.add_part(
            "ACC_LampHousing",
            make_frustum_between((0, -0.118, 1.600), (0, -0.212, 1.596), 0.064, 0.060, 10, 0.96),
            "head", "signature_silhouette", "miner_helmet_dark",
        )
        self.add_part(
            "ACC_LampBezel",
            make_frustum_between((0, -0.212, 1.596), (0, -0.224, 1.596), 0.062, 0.058, 10, 0.96),
            "head", "surface_detail", "miner_helmet",
        )
        self.add_part(
            "ACC_LampLens",
            make_frustum_between((0, -0.224, 1.596), (0, -0.234, 1.596), 0.054, 0.048, 10, 0.96),
            "head", "signature_silhouette", "lamp_lens",
        )
        for side, x in (("L", 0.052), ("R", -0.052)):
            self.add_part(
                f"ACC_Eye.{side}",
                make_box((x, -0.128, 1.492), (0.038, 0.020, 0.026)),
                "head", "face_detail", "void",
            )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.140, 1.424), (0, -0.126, 1.470), (0.046, 0.058, 0), (0.036, 0.044, 0)),
            "head", "face_detail", "skin",
        )
        self.add_part(
            "ACC_Mouth",
            make_box((0, -0.128, 1.402), (0.078, 0.026, 0.020)),
            "head", "face_detail", "void",
        )

    def build_yard_babushka_body(self) -> None:
        """Stout grandmother in a housecoat; the stoop lives in the clips.

        The source stays on the canonical A-pose skeleton so the shared
        Avatar copies exactly; the hunched silhouette, the beating swing
        and the smoking stance are all authored in BabushkaBeat and
        BabushkaSmoke rather than in the geometry.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.038, 1.545), (0.104, 0.096, 0.128), 12, 6),
            "head", "body", "skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.320), (0, -0.026, 1.450), 0.075, 0.062, 10),
            "neck", "body", "skin",
        )
        self.add_part(
            "GEO_Bust",
            make_tapered_box((0, 0.010, 1.075), (0, -0.008, 1.340), (0.400, 0.270, 0), (0.440, 0.300, 0)),
            "chest", "body", "gran_robe",
        )
        self.add_part(
            "GEO_Waist",
            make_tapered_box((0, 0.026, 0.870), (0, 0.012, 1.095), (0.430, 0.310, 0), (0.405, 0.275, 0)),
            "spine", "body", "gran_robe",
        )
        self.add_part(
            "CLO_Skirt",
            make_tapered_box((0, 0.020, 0.360), (0, 0.024, 0.890), (0.530, 0.430, 0), (0.440, 0.330, 0)),
            "pelvis", "clothing", "gran_skirt",
        )
        self.add_part(
            "CLO_Apron",
            make_tapered_box((0, -0.212, 0.400), (0, -0.165, 0.860), (0.300, 0.024, 0), (0.250, 0.022, 0)),
            "pelvis", "clothing", "gran_apron",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.208, sign * -0.004, 1.292)
            elbow = (sign * 0.470, -0.010, 1.175)
            wrist = (sign * 0.680, -0.018, 1.075)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.074, 0.060, 10),
                f"upper_arm.{side}", "clothing", "gran_robe",
            )
            self.add_part(
                f"GEO_Forearm.{side}",
                make_frustum_between(elbow, wrist, 0.048, 0.038, 8),
                f"forearm.{side}", "body", "skin",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.436, -0.009, 1.190),
                    (sign * 0.504, -0.011, 1.160),
                    0.064, 0.058, 8,
                ),
                f"forearm.{side}", "clothing", "gran_robe_dark",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.718, -0.020, 1.055), (0.085, 0.070, 0.058)),
                f"hand.{side}", "body", "skin",
            )
            knee = (sign * 0.103, sign * -0.012, 0.354)
            ankle = (sign * 0.112, sign * -0.022, 0.095)
            self.add_part(
                f"CLO_Stocking.{side}",
                make_frustum_between(knee, ankle, 0.058, 0.047, 8),
                f"shin.{side}", "clothing", "gran_wool",
            )
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.085, 0.030),
                    (sign * 0.112, -0.052, 0.145),
                    (0.104, 0.260, 0),
                    (0.092, 0.190, 0),
                ),
                f"foot.{side}", "body", "shoe",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.112, -0.085, 0.012), (0.108, 0.268, 0.024)),
                f"foot.{side}", "body", "sole",
            )
        # The headscarf shell sits behind the face plane and owns the
        # silhouette; the wrap and knot close it under the chin.
        self.add_part(
            "CLO_Scarf",
            make_ellipsoid((0, 0.004, 1.555), (0.126, 0.132, 0.150), 12, 6),
            "head", "signature_silhouette", "gran_scarf",
        )
        # The folded crest of the headscarf owns the exact 1.75 m envelope.
        self.add_part(
            "CLO_ScarfCrown",
            make_tapered_box((0, 0.004, 1.688), (0, 0.008, 1.750), (0.152, 0.162, 0), (0.056, 0.060, 0)),
            "head", "signature_silhouette", "gran_scarf",
        )
        self.add_part(
            "CLO_ScarfWrap",
            make_frustum_between((0, -0.072, 1.438), (0, -0.030, 1.318), 0.076, 0.056, 8),
            "head", "clothing", "gran_scarf_dark",
        )
        self.add_part(
            "CLO_ScarfKnot",
            make_box((0, -0.118, 1.396), (0.056, 0.046, 0.066)),
            "head", "clothing", "gran_scarf_dark",
        )
        self.add_part(
            "CLO_ScarfTail",
            make_tapered_box((0, -0.124, 1.296), (0, -0.112, 1.382), (0.088, 0.030, 0), (0.054, 0.026, 0)),
            "head", "clothing", "gran_scarf",
        )
        for side, x in (("L", 0.045), ("R", -0.045)):
            self.add_part(
                f"ACC_Eye.{side}",
                make_box((x, -0.128, 1.548), (0.034, 0.018, 0.022)),
                "head", "face_detail", "void",
            )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.146, 1.494), (0, -0.130, 1.534), (0.040, 0.048, 0), (0.030, 0.040, 0)),
            "head", "face_detail", "skin",
        )
        self.add_part(
            "ACC_Mouth",
            make_box((0, -0.132, 1.462), (0.058, 0.020, 0.014)),
            "head", "face_detail", "void",
        )

    def build_yard_babushka_details(self) -> None:
        """Both authored hand props, enabled per role by the runtime.

        The beater is the classic bright Soviet plastic: a handle
        continuing the right hand's axis into a flattened teardrop
        paddle. The cigarette rides the same hand along the canonical
        SOCKET_Cigarette.R direction; a beating babushka shows only the
        beater and the smoking one only the cigarette.
        """

        self.add_part(
            "ACC_RobeButton.01",
            make_box((0, -0.158, 1.270), (0.024, 0.018, 0.024)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_RobeButton.02",
            make_box((0, -0.150, 1.150), (0.024, 0.018, 0.024)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_RobeButton.03",
            make_box((0, -0.152, 1.020), (0.024, 0.018, 0.024)),
            "spine", "surface_detail", "button",
        )

        # The beater points forward-down out of the fist: the A-pose
        # envelope allows barely 5 cm past the fingertips on X, so the
        # carry direction leans into -Y (the model's forward) instead
        # of along the arm. With the strike key extending the forearm
        # toward the carpet, this forward bias is what lands the paddle
        # on the hung carpet instead of folding it back into the skirt.
        direction = (0.0, -0.600, -0.800)
        grip = (-0.720, -0.021, 1.048)

        def along(distance: float) -> tuple[float, float, float]:
            return (
                grip[0] + direction[0] * distance,
                grip[1] + direction[1] * distance,
                grip[2] + direction[2] * distance,
            )

        self.add_part(
            "ACC_BeaterHandle",
            make_frustum_between(along(-0.050), along(0.240), 0.015, 0.013, 8, 0.95),
            "hand.R", "signature_silhouette", "beater_plastic_dark",
        )
        self.add_part(
            "ACC_BeaterNeck",
            make_frustum_between(along(0.240), along(0.320), 0.013, 0.011, 8, 0.95),
            "hand.R", "signature_silhouette", "beater_plastic",
        )
        self.add_part(
            "ACC_BeaterPaddleRise",
            make_frustum_between(along(0.320), along(0.440), 0.018, 0.085, 8, 0.26),
            "hand.R", "signature_silhouette", "beater_plastic",
        )
        self.add_part(
            "ACC_BeaterPaddleTip",
            make_frustum_between(along(0.440), along(0.580), 0.085, 0.012, 8, 0.26),
            "hand.R", "signature_silhouette", "beater_plastic",
        )
        self.add_part(
            "ACC_Cigarette",
            make_frustum_between(
                (-0.744, -0.052, 1.052),
                (-0.748, -0.126, 1.055),
                0.0068, 0.0060, 6, 1.0,
            ),
            "hand.R", "surface_detail", "pipe_ivory",
        )
        self.add_part(
            "ACC_CigaretteEmber",
            make_box((-0.748, -0.132, 1.055), (0.015, 0.014, 0.015)),
            "hand.R", "surface_detail", "amber",
        )

    def build_cemetery_mourner_body(self) -> None:
        """Woman in deep mourning; the grief itself lives in the clips.

        The source stays on the canonical A-pose skeleton so the shared
        Avatar copies exactly; the bowed head, the clasped bouquet and
        the graveside rite are all authored in MournerWalk and
        MournerMourn rather than in the geometry. The silhouette is a
        long near-black coat under a heavy veil that falls onto the
        shoulders — everything the babushka's housecoat is not.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.038, 1.545), (0.102, 0.094, 0.126), 12, 6),
            "head", "body", "pale_skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.320), (0, -0.026, 1.450), 0.070, 0.058, 10),
            "neck", "body", "pale_skin",
        )
        self.add_part(
            "GEO_Bust",
            make_tapered_box((0, 0.010, 1.075), (0, -0.008, 1.340), (0.380, 0.255, 0), (0.410, 0.280, 0)),
            "chest", "body", "mourner_coat",
        )
        self.add_part(
            "GEO_Waist",
            make_tapered_box((0, 0.026, 0.870), (0, 0.012, 1.095), (0.400, 0.290, 0), (0.385, 0.260, 0)),
            "spine", "body", "mourner_coat",
        )
        # The long mourning coat: one dark fall from the waist to the
        # boot shafts, with a near-void hem band closing it.
        self.add_part(
            "CLO_Coat",
            make_tapered_box((0, 0.020, 0.310), (0, 0.024, 0.890), (0.500, 0.400, 0), (0.420, 0.310, 0)),
            "pelvis", "clothing", "mourner_coat",
        )
        self.add_part(
            "CLO_CoatHem",
            make_tapered_box((0, 0.020, 0.256), (0, 0.020, 0.316), (0.512, 0.410, 0), (0.502, 0.402, 0)),
            "pelvis", "clothing", "mourner_coat_dark",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.208, sign * -0.004, 1.292)
            elbow = (sign * 0.470, -0.010, 1.175)
            wrist = (sign * 0.680, -0.018, 1.075)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.072, 0.058, 10),
                f"upper_arm.{side}", "clothing", "mourner_coat",
            )
            # Long sleeves down to the wrist: a mourner shows no bare
            # forearm, only the pale hands stay uncovered.
            self.add_part(
                f"CLO_SleeveLower.{side}",
                make_frustum_between(elbow, wrist, 0.056, 0.044, 8),
                f"forearm.{side}", "clothing", "mourner_coat",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.628, -0.016, 1.100),
                    (sign * 0.676, -0.018, 1.078),
                    0.048, 0.044, 8,
                ),
                f"forearm.{side}", "clothing", "mourner_coat_dark",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.718, -0.020, 1.055), (0.082, 0.068, 0.056)),
                f"hand.{side}", "body", "pale_skin",
            )
            knee = (sign * 0.103, sign * -0.012, 0.354)
            ankle = (sign * 0.112, sign * -0.022, 0.095)
            self.add_part(
                f"CLO_Stocking.{side}",
                make_frustum_between(knee, ankle, 0.056, 0.045, 8),
                f"shin.{side}", "clothing", "mourner_stocking",
            )
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.085, 0.030),
                    (sign * 0.112, -0.052, 0.145),
                    (0.102, 0.255, 0),
                    (0.090, 0.185, 0),
                ),
                f"foot.{side}", "body", "shoe",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.112, -0.085, 0.012), (0.106, 0.262, 0.024)),
                f"foot.{side}", "body", "sole",
            )
        # The heavy veil owns the silhouette: a shell behind the face
        # plane, a folded crest carrying the exact 1.75 m envelope, a
        # wrap closing under the chin and two drapes falling toward the
        # shoulders.
        self.add_part(
            "CLO_Veil",
            make_ellipsoid((0, 0.008, 1.550), (0.132, 0.140, 0.156), 12, 6),
            "head", "signature_silhouette", "mourner_veil",
        )
        self.add_part(
            "CLO_VeilCrown",
            make_tapered_box((0, 0.006, 1.690), (0, 0.010, 1.750), (0.150, 0.160, 0), (0.052, 0.056, 0)),
            "head", "signature_silhouette", "mourner_veil",
        )
        self.add_part(
            "CLO_VeilWrap",
            make_frustum_between((0, -0.072, 1.438), (0, -0.030, 1.318), 0.078, 0.058, 8),
            "head", "clothing", "mourner_veil_dark",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"CLO_VeilDrape.{side}",
                make_tapered_box(
                    (sign * 0.150, 0.052, 1.300),
                    (sign * 0.108, 0.030, 1.520),
                    (0.062, 0.150, 0),
                    (0.088, 0.190, 0),
                ),
                "head", "signature_silhouette", "mourner_veil",
            )
        self.add_part(
            "CLO_VeilTail",
            make_tapered_box((0, 0.118, 1.240), (0, 0.096, 1.470), (0.120, 0.036, 0), (0.170, 0.052, 0)),
            "head", "clothing", "mourner_veil_dark",
        )
        for side, x in (("L", 0.045), ("R", -0.045)):
            self.add_part(
                f"ACC_Eye.{side}",
                make_box((x, -0.126, 1.548), (0.034, 0.018, 0.020)),
                "head", "face_detail", "void",
            )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.144, 1.494), (0, -0.128, 1.532), (0.038, 0.046, 0), (0.028, 0.038, 0)),
            "head", "face_detail", "pale_skin",
        )
        self.add_part(
            "ACC_Mouth",
            make_box((0, -0.130, 1.462), (0.052, 0.020, 0.012)),
            "head", "face_detail", "void",
        )

    def build_cemetery_mourner_details(self) -> None:
        """Coat buttons and the clasped funeral bouquet.

        The bouquet rides the right hand the way the babushka's beater
        does: authored at the A-pose fist and pointing up-forward out
        of it, so the clasp pose of MournerWalk carries it against the
        chest. The runtime hides these ACC_Bouquet* renderers at the
        lay cue and places its own bouquet on the grave slab.
        """

        self.add_part(
            "ACC_CoatButton.01",
            make_box((0, -0.148, 1.250), (0.022, 0.016, 0.022)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_CoatButton.02",
            make_box((0, -0.142, 1.130), (0.022, 0.016, 0.022)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_CoatButton.03",
            make_box((0, -0.144, 1.000), (0.022, 0.016, 0.022)),
            "spine", "surface_detail", "button",
        )

        # Up and slightly forward out of the fist: with both forearms
        # folded to the chest in the authored clips, this direction
        # stands the blooms upright against the collarbone.
        direction = (0.0, -0.280, 0.960)
        grip = (-0.720, -0.021, 1.048)

        def along(distance: float) -> tuple[float, float, float]:
            return (
                grip[0] + direction[0] * distance,
                grip[1] + direction[1] * distance,
                grip[2] + direction[2] * distance,
            )

        self.add_part(
            "ACC_BouquetStems",
            make_frustum_between(along(-0.070), along(0.020), 0.011, 0.015, 6, 1.0),
            "hand.R", "surface_detail", "bouquet_stem",
        )
        self.add_part(
            "ACC_BouquetWrap",
            make_frustum_between(along(0.000), along(0.165), 0.028, 0.056, 8, 1.0),
            "hand.R", "signature_silhouette", "bouquet_wrap",
        )
        self.add_part(
            "ACC_BouquetBloomA",
            make_ellipsoid(along(0.210), (0.056, 0.052, 0.044), 8, 4),
            "hand.R", "signature_silhouette", "bouquet_bloom",
        )
        bloom_b = along(0.240)
        self.add_part(
            "ACC_BouquetBloomB",
            make_ellipsoid(
                (bloom_b[0] + 0.040, bloom_b[1] - 0.012, bloom_b[2] - 0.018),
                (0.040, 0.038, 0.034), 8, 4,
            ),
            "hand.R", "signature_silhouette", "bouquet_bloom",
        )
        bloom_c = along(0.228)
        self.add_part(
            "ACC_BouquetGreens",
            make_ellipsoid(
                (bloom_c[0] - 0.042, bloom_c[1] + 0.010, bloom_c[2] - 0.008),
                (0.036, 0.034, 0.040), 8, 4,
            ),
            "hand.R", "surface_detail", "bouquet_stem",
        )

    def build_last_route_ferryman_body(self) -> None:
        """The Ferryman: a driver built to read as a boatman.

        Everything here is the trick stated once. The coat is a taxi
        driver's oilcloth greatcoat, which from any distance is a cloak.
        The cap brim carries a separate near-black shadow part so the
        EYES ARE NEVER DRAWN - that shadow is the hood, and it is the
        cheapest way to keep a face unreadable at 640x360 under any
        light the island throws at it.

        The skirt of the coat stops at a hem stub below the pelvis. The
        rest of it is a runtime Cloth panel: he sits on a bonnet with the
        skirt hanging over the nose, and a rigid slab there would read as
        a plank rather than as cloth.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.030, 1.566), (0.098, 0.092, 0.122), 12, 6),
            "head", "body", "ferry_skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.006, 1.318), (0, -0.018, 1.472), 0.060, 0.052, 10),
            "neck", "body", "ferry_skin",
        )
        # High collar, turned up: the coat closes around the jaw and
        # takes over from the cap shadow.
        self.add_part(
            "CLO_Collar",
            make_frustum_between((0, -0.004, 1.322), (0, -0.020, 1.436), 0.108, 0.112, 10),
            "neck", "clothing", "ferry_coat_dark",
        )
        # Tall-shouldered and narrow: a spare man in a heavy coat.
        self.add_part(
            "CLO_CoatChest",
            make_tapered_box((0, 0.004, 1.075), (0, -0.006, 1.336), (0.372, 0.222, 0), (0.392, 0.238, 0)),
            "chest", "body", "ferry_coat",
        )
        self.add_part(
            "CLO_CoatWaist",
            make_tapered_box((0, 0.010, 0.880), (0, 0.006, 1.092), (0.352, 0.216, 0), (0.374, 0.224, 0)),
            "spine", "body", "ferry_coat",
        )
        # The hem stub. The cloth panel hangs from its underside, so it
        # has to be wide enough that no gap shows where the two meet.
        self.add_part(
            "CLO_CoatHem",
            make_tapered_box((0, 0.012, 0.762), (0, 0.010, 0.896), (0.392, 0.256, 0), (0.356, 0.220, 0)),
            "pelvis", "clothing", "ferry_coat_dark",
        )
        # The coat's front edge, so the skirt reads as opening rather
        # than as a tube even before the cloth takes over.
        self.add_part(
            "CLO_CoatFacing",
            make_box((0, -0.118, 1.040), (0.052, 0.026, 0.470)),
            "spine", "clothing", "ferry_coat_dark",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.196, sign * -0.002, 1.300)
            elbow = (sign * 0.462, -0.008, 1.180)
            wrist = (sign * 0.676, -0.016, 1.078)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.074, 0.058, 10),
                f"upper_arm.{side}", "clothing", "ferry_coat",
            )
            self.add_part(
                f"CLO_SleeveLower.{side}",
                make_frustum_between(elbow, wrist, 0.054, 0.044, 8),
                f"forearm.{side}", "clothing", "ferry_coat",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.628, -0.014, 1.102),
                    (sign * 0.690, -0.018, 1.070),
                    0.047, 0.042, 8,
                ),
                f"forearm.{side}", "clothing", "ferry_coat_dark",
            )
            # Large hands. He is thin everywhere else.
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.716, -0.019, 1.056), (0.086, 0.072, 0.058)),
                f"hand.{side}", "body", "ferry_skin",
            )
            hip = (sign * 0.094, 0.004, 0.734)
            knee = (sign * 0.100, -0.012, 0.356)
            self.add_part(
                f"CLO_TrouserUpper.{side}",
                make_frustum_between(hip, knee, 0.080, 0.058, 8),
                f"thigh.{side}", "clothing", "ferry_coat_dark",
            )
            self.add_part(
                f"CLO_TrouserLower.{side}",
                make_frustum_between(
                    (sign * 0.100, -0.012, 0.348),
                    (sign * 0.110, -0.020, 0.128),
                    0.058, 0.052, 8,
                ),
                f"shin.{side}", "clothing", "ferry_coat_dark",
            )
            # Boots muddy to the ankle - he waits outdoors.
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.110, -0.082, 0.032),
                    (sign * 0.110, -0.050, 0.150),
                    (0.106, 0.256, 0),
                    (0.094, 0.188, 0),
                ),
                f"foot.{side}", "body", "ferry_boot",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.110, -0.082, 0.012), (0.110, 0.264, 0.024)),
                f"foot.{side}", "body", "sole",
            )

    def build_last_route_ferryman_details(self) -> None:
        """Cap, the shadow under its brim, and one coil of rope.

        The rope is the only boat on him. It is never referred to and
        never used; a mooring coil on a man beside a car is odd enough
        to be noticed and small enough not to explain itself.
        """

        # Flat-topped service cap with a low brim.
        self.add_part(
            "ACC_CapCrown",
            make_tapered_box((0, -0.014, 1.668), (0, -0.014, 1.750), (0.214, 0.214, 0), (0.206, 0.204, 0)),
            "head", "signature_silhouette", "ferry_cap",
        )
        self.add_part(
            "ACC_CapBand",
            make_tapered_box((0, -0.014, 1.626), (0, -0.014, 1.670), (0.210, 0.210, 0), (0.214, 0.214, 0)),
            "head", "signature_silhouette", "ferry_cap_band",
        )
        self.add_part(
            "ACC_CapBrim",
            make_tapered_box((0, -0.120, 1.616), (0, -0.118, 1.634), (0.218, 0.150, 0), (0.206, 0.142, 0)),
            "head", "signature_silhouette", "ferry_cap",
        )
        # The hood. A near-black slab under the brim, sized to swallow
        # the eye line from every angle the player can reach.
        self.add_part(
            "ACC_BrowShadow",
            make_box((0, -0.064, 1.594), (0.178, 0.098, 0.034)),
            "head", "face_detail", "ferry_shadow",
        )
        # A grey stubble line and a flat mouth: the only face there is.
        self.add_part(
            "ACC_Jaw",
            make_box((0, -0.076, 1.494), (0.128, 0.052, 0.038)),
            "head", "face_detail", "ferry_skin",
        )
        self.add_part(
            "ACC_Mouth",
            make_box((0, -0.100, 1.508), (0.052, 0.014, 0.010)),
            "head", "face_detail", "ferry_coat_dark",
        )
        # The mooring coil, on the belt loop at his left hip.
        for index, radius in enumerate((0.070, 0.060, 0.050)):
            self.add_part(
                f"ACC_MooringRope{index + 1:02d}",
                make_torus_x((0.146, 0.020, 0.812 - index * 0.014), radius, 0.011, 10, 5),
                "pelvis", "surface_detail", "ferry_rope",
            )

    def build_cemetery_watchman_body(self) -> None:
        """Snide old cemetery watchman; the attitude lives in the clips.

        The source stays on the canonical A-pose skeleton so the shared
        Avatar copies exactly; the stoop, the clasped hands behind the
        back and the disapproving head shake are all authored in
        WatchmanWatch and WatchmanShuffle rather than in the geometry.
        The silhouette is a worn telogreika under a wide aerodrome
        flat cap; the face carries the permanent smirk: one raised
        brow, narrowed eyes, an off-centre mouth and grey whiskers.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.036, 1.560), (0.100, 0.094, 0.124), 12, 6),
            "head", "body", "skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.320), (0, -0.024, 1.470), 0.066, 0.056, 10),
            "neck", "body", "skin",
        )
        # The quilted telogreika: boxy, a little sunken at the chest —
        # an old man's jacket, not a worker's.
        self.add_part(
            "CLO_CoatChest",
            make_tapered_box((0, 0.008, 1.070), (0, -0.004, 1.340), (0.400, 0.245, 0), (0.405, 0.255, 0)),
            "chest", "body", "watch_coat",
        )
        self.add_part(
            "CLO_CoatWaist",
            make_tapered_box((0, 0.016, 0.870), (0, 0.010, 1.090), (0.395, 0.250, 0), (0.400, 0.248, 0)),
            "spine", "body", "watch_coat",
        )
        self.add_part(
            "CLO_CoatHem",
            make_tapered_box((0, 0.018, 0.690), (0, 0.016, 0.890), (0.415, 0.268, 0), (0.398, 0.252, 0)),
            "pelvis", "clothing", "watch_coat_dark",
        )
        # The open collar shows a sliver of the old shirt underneath.
        self.add_part(
            "CLO_Collar",
            make_frustum_between((0, -0.008, 1.330), (0, -0.014, 1.392), 0.102, 0.086, 10),
            "neck", "clothing", "watch_coat_dark",
        )
        self.add_part(
            "ACC_ShirtV",
            make_tapered_box((0, -0.132, 1.238), (0, -0.128, 1.320), (0.120, 0.020, 0), (0.052, 0.016, 0)),
            "chest", "surface_detail", "watch_shirt",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.208, sign * -0.004, 1.292)
            elbow = (sign * 0.470, -0.010, 1.175)
            wrist = (sign * 0.680, -0.018, 1.075)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.076, 0.062, 10),
                f"upper_arm.{side}", "clothing", "watch_coat",
            )
            self.add_part(
                f"CLO_SleeveLower.{side}",
                make_frustum_between(elbow, wrist, 0.056, 0.045, 8),
                f"forearm.{side}", "clothing", "watch_coat",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.630, -0.016, 1.100),
                    (sign * 0.694, -0.019, 1.068),
                    0.048, 0.043, 8,
                ),
                f"forearm.{side}", "clothing", "watch_coat_dark",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.718, -0.020, 1.055), (0.082, 0.068, 0.056)),
                f"hand.{side}", "body", "skin",
            )
            hip = (sign * 0.096, 0.004, 0.730)
            knee = (sign * 0.103, -0.012, 0.354)
            ankle = (sign * 0.112, -0.022, 0.095)
            # Trousers tucked into tall kirza boot shafts.
            self.add_part(
                f"CLO_TrouserUpper.{side}",
                make_frustum_between(hip, knee, 0.084, 0.060, 8),
                f"thigh.{side}", "clothing", "watch_trousers",
            )
            self.add_part(
                f"CLO_BootShaft.{side}",
                make_frustum_between(
                    (sign * 0.103, -0.014, 0.340),
                    (sign * 0.112, -0.022, 0.100),
                    0.064, 0.056, 8,
                ),
                f"shin.{side}", "clothing", "shoe",
            )
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.085, 0.030),
                    (sign * 0.112, -0.052, 0.145),
                    (0.104, 0.260, 0),
                    (0.092, 0.190, 0),
                ),
                f"foot.{side}", "body", "shoe",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.112, -0.085, 0.012), (0.108, 0.268, 0.024)),
                f"foot.{side}", "body", "sole",
            )
        # The wide aerodrome flat cap owns the silhouette and the
        # exact 1.75 m envelope: a shallow dome over a band with a
        # broad visor pushed slightly up — he looks at you from under
        # it anyway.
        self.add_part(
            "CLO_CapDome",
            make_ellipsoid((0, 0.000, 1.652), (0.146, 0.150, 0.076), 12, 6),
            "head", "signature_silhouette", "watch_cap",
        )
        self.add_part(
            "CLO_CapBand",
            make_frustum_between((0, -0.006, 1.566), (0, -0.004, 1.622), 0.116, 0.124, 12),
            "head", "clothing", "watch_cap",
        )
        self.add_part(
            "CLO_CapCrown",
            make_tapered_box((0, 0.002, 1.716), (0, 0.004, 1.750), (0.088, 0.092, 0), (0.034, 0.036, 0)),
            "head", "signature_silhouette", "watch_cap",
        )
        self.add_part(
            "CLO_CapVisor",
            make_tapered_box((0, -0.196, 1.586), (0, -0.116, 1.612), (0.128, 0.088, 0), (0.144, 0.104, 0)),
            "head", "signature_silhouette", "watch_cap",
        )
        # The permanent smirk: narrowed eyes, one raised grey brow,
        # a big nose, grey whiskers and a mouth pulled off centre.
        self.add_part(
            "ACC_Eye.L",
            make_box((0.045, -0.124, 1.560), (0.034, 0.018, 0.014)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Eye.R",
            make_box((-0.045, -0.124, 1.562), (0.034, 0.018, 0.012)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Brow.L",
            make_box((0.048, -0.128, 1.584), (0.044, 0.016, 0.014)),
            "head", "face_detail", "watch_grey",
        )
        self.add_part(
            "ACC_Brow.R",
            make_box((-0.050, -0.128, 1.600), (0.046, 0.016, 0.016)),
            "head", "face_detail", "watch_grey",
        )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.148, 1.502), (0, -0.126, 1.546), (0.044, 0.052, 0), (0.030, 0.040, 0)),
            "head", "face_detail", "skin",
        )
        self.add_part(
            "ACC_Moustache",
            make_box((0.004, -0.140, 1.482), (0.080, 0.024, 0.022)),
            "head", "face_detail", "watch_grey",
        )
        self.add_part(
            "ACC_Mouth",
            make_box((0.020, -0.128, 1.462), (0.048, 0.018, 0.012)),
            "head", "face_detail", "void",
        )

    def build_cemetery_watchman_details(self) -> None:
        """Quilt ridges, buttons and the whiskered chin.

        No hand props: both hands stay clasped behind the back in
        every authored loop, and no authority markers — the smirk is
        the whole uniform.
        """

        # Horizontal quilt ridges read as the telogreika's padding.
        for index, height in enumerate((1.280, 1.180, 1.080), start=1):
            self.add_part(
                f"ACC_QuiltSeam.{index:02d}",
                make_box((0, -0.146, height), (0.370, 0.014, 0.016)),
                "chest" if height > 1.1 else "spine",
                "surface_detail", "watch_coat_dark",
            )
        for index, height in enumerate((0.980, 0.895), start=4):
            self.add_part(
                f"ACC_QuiltSeam.{index:02d}",
                make_box((0, -0.140, height), (0.360, 0.014, 0.016)),
                "spine", "surface_detail", "watch_coat_dark",
            )
        self.add_part(
            "ACC_CoatButton.01",
            make_box((0.060, -0.152, 1.230), (0.022, 0.016, 0.022)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_CoatButton.02",
            make_box((0.058, -0.146, 1.110), (0.022, 0.016, 0.022)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_CoatButton.03",
            make_box((0.056, -0.148, 0.990), (0.022, 0.016, 0.022)),
            "spine", "surface_detail", "button",
        )
        # Grey stubble on the chin closes the whiskered face.
        self.add_part(
            "ACC_Stubble",
            make_tapered_box((0, -0.108, 1.436), (0, -0.120, 1.472), (0.096, 0.052, 0), (0.106, 0.062, 0)),
            "head", "surface_detail", "watch_grey",
        )


    def build_lake_fisherman_body(self) -> None:
        """Hooded man in a yellow oilskin; the fishing lives in the clips.

        The source stays on the canonical A-pose skeleton so the shared
        Avatar copies exactly; the lean out over the end board, the
        two-handed grip and the slow watch of the float are all authored
        in FishermanLean rather than in the geometry. The silhouette is
        the stiff hooded slicker: a storm yoke over the shoulders, a
        peaked hood that owns the top of the envelope, and a hem that
        stops at mid-thigh. Under it, oilskin trousers into tall rubber
        waders.

        The face is deliberately half-lost inside the hood. What reads
        at two metres is the grey beard and the pipe leaving the mouth,
        which is the only place on this design the player will look.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.034, 1.552), (0.098, 0.092, 0.122), 12, 6),
            "head", "body", "fisher_skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.318), (0, -0.024, 1.468), 0.066, 0.056, 10),
            "neck", "body", "fisher_skin",
        )
        # The slicker: boxier and squarer than any cloth coat on the
        # library, because proofed canvas holds its own shape.
        self.add_part(
            "CLO_SlickerChest",
            make_tapered_box((0, 0.008, 1.075), (0, -0.004, 1.345), (0.418, 0.258, 0), (0.428, 0.272, 0)),
            "chest", "body", "slicker",
        )
        self.add_part(
            "CLO_SlickerWaist",
            make_tapered_box((0, 0.014, 0.870), (0, 0.010, 1.095), (0.410, 0.262, 0), (0.420, 0.264, 0)),
            "spine", "body", "slicker",
        )
        # The hem stops at mid-thigh on purpose. A stiff skirt is bound
        # to the pelvis and cannot follow a knee, so a full-length one
        # would cut through the leading leg every time he takes a step
        # or braces against the board.
        self.add_part(
            "CLO_SlickerHem",
            make_tapered_box((0, 0.018, 0.581), (0, 0.016, 0.895), (0.455, 0.300, 0), (0.415, 0.266, 0)),
            "pelvis", "clothing", "slicker_dark",
        )
        # The storm yoke: the doubled cape across the shoulders that
        # says oilskin rather than raincoat at any distance.
        self.add_part(
            "CLO_StormYoke",
            make_tapered_box((0, 0.004, 1.140), (0, -0.004, 1.330), (0.470, 0.300, 0), (0.430, 0.276, 0)),
            "chest", "signature_silhouette", "slicker_light",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.208, sign * -0.004, 1.292)
            elbow = (sign * 0.470, -0.010, 1.175)
            wrist = (sign * 0.680, -0.018, 1.075)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.080, 0.066, 10),
                f"upper_arm.{side}", "clothing", "slicker",
            )
            self.add_part(
                f"CLO_SleeveLower.{side}",
                make_frustum_between(elbow, wrist, 0.060, 0.048, 8),
                f"forearm.{side}", "clothing", "slicker",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.628, -0.016, 1.102),
                    (sign * 0.696, -0.019, 1.066),
                    0.052, 0.046, 8,
                ),
                f"forearm.{side}", "clothing", "slicker_dark",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.720, -0.022, 1.052), (0.084, 0.070, 0.058)),
                f"hand.{side}", "body", "fisher_skin",
            )
            hip = (sign * 0.094, 0.004, 0.740)
            knee = (sign * 0.103, -0.012, 0.362)
            self.add_part(
                f"CLO_TrouserUpper.{side}",
                make_frustum_between(hip, knee, 0.088, 0.070, 8),
                f"thigh.{side}", "clothing", "oilskin_trousers",
            )
            # Wader tops, turned down at the knee. The shaft stops
            # exactly at the knee joint and not a centimetre above it:
            # this part rides the shin, and every centimetre of it
            # authored past its own bone head swings out through the
            # thigh as soon as the knee bends.
            self.add_part(
                f"CLO_BootShaft.{side}",
                make_frustum_between(
                    (sign * 0.103, -0.012, 0.356),
                    (sign * 0.112, -0.022, 0.100),
                    0.082, 0.070, 8,
                ),
                f"shin.{side}", "clothing", "boot_rubber",
            )
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.088, 0.026),
                    (sign * 0.112, -0.054, 0.148),
                    (0.110, 0.272, 0),
                    (0.098, 0.198, 0),
                ),
                f"foot.{side}", "body", "boot_rubber",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.112, -0.088, 0.012), (0.114, 0.280, 0.024)),
                f"foot.{side}", "body", "sole",
            )
        # The hood owns the silhouette and the exact 1.75 m envelope: a
        # deep shell well clear of the skull, a stiff peak pulled up at
        # the crown, and a brim thrown forward over the face.
        self.add_part(
            "CLO_HoodShell",
            make_ellipsoid((0, 0.012, 1.582), (0.148, 0.152, 0.130), 12, 6),
            "head", "signature_silhouette", "slicker",
        )
        self.add_part(
            "CLO_HoodPeak",
            make_tapered_box((0, 0.030, 1.688), (0, 0.020, 1.750), (0.132, 0.142, 0), (0.054, 0.060, 0)),
            "head", "signature_silhouette", "slicker",
        )
        self.add_part(
            "CLO_HoodBrim",
            make_tapered_box((0, -0.176, 1.594), (0, -0.122, 1.642), (0.182, 0.112, 0), (0.198, 0.132, 0)),
            "head", "signature_silhouette", "slicker_light",
        )
        self.add_part(
            "CLO_HoodCollar",
            make_frustum_between((0, 0.000, 1.328), (0, -0.006, 1.424), 0.132, 0.120, 12),
            "neck", "clothing", "slicker_dark",
        )
        # What survives the hood: narrowed weather eyes, a big nose and
        # a grey beard. No mouth is drawn - the pipe is the mouth.
        self.add_part(
            "ACC_Eye.L",
            make_box((0.044, -0.120, 1.556), (0.032, 0.018, 0.013)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Eye.R",
            make_box((-0.044, -0.120, 1.556), (0.032, 0.018, 0.013)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.144, 1.498), (0, -0.122, 1.542), (0.046, 0.054, 0), (0.032, 0.042, 0)),
            "head", "face_detail", "fisher_skin",
        )
        self.add_part(
            "ACC_Beard",
            make_tapered_box((0, -0.100, 1.408), (0, -0.124, 1.480), (0.128, 0.086, 0), (0.112, 0.070, 0)),
            "head", "signature_silhouette", "fisher_grey",
        )
        self.add_part(
            "ACC_Moustache",
            make_box((0, -0.136, 1.486), (0.086, 0.026, 0.020)),
            "head", "face_detail", "fisher_grey",
        )

    def build_lake_fisherman_details(self) -> None:
        """The two things he is actually doing: the pipe and the rod.

        Both are hard-mounted geometry rather than togglable props,
        because unlike the babushka he has exactly one role and never
        puts either down.

        The pipe leaves the mouth along the canonical SOCKET_Mouth
        direction, bends down and forward, and stands its bowl back up
        in front of the beard - the classic bent shape, chosen because
        a straight stem would read as a stick from the front. The ember
        is a separate flat part on top of the bowl and nothing else:
        the shared source material must stay non-emissive, so the glow,
        the light and the smoke are all raised by the runtime against
        this exact part.

        The rod is bound rigidly to the right hand, because it is one
        stick and this rig has one vertex group per part; the left hand
        is brought onto the same axis by the authored pose instead, and
        those angles were fitted against this geometry rather than set
        by eye. It leaves the fist forward and slightly up, so the line
        falls clear of the end board to the water the plan measured.
        """

        self.add_part(
            "ACC_SlickerSeam.01",
            make_box((0, -0.142, 1.262), (0.400, 0.014, 0.014)),
            "chest", "surface_detail", "slicker_dark",
        )
        self.add_part(
            "ACC_SlickerSeam.02",
            make_box((0, -0.136, 1.036), (0.392, 0.014, 0.014)),
            "spine", "surface_detail", "slicker_dark",
        )
        self.add_part(
            "ACC_SlickerSeam.03",
            make_box((0, -0.140, 0.898), (0.406, 0.014, 0.014)),
            "spine", "surface_detail", "slicker_dark",
        )
        self.add_part(
            "ACC_HoodCord",
            make_box((0, -0.146, 1.372), (0.176, 0.018, 0.016)),
            "neck", "surface_detail", "slicker_dark",
        )
        self.add_part(
            "ACC_SlickerClasp.01",
            make_box((0.052, -0.150, 1.196), (0.024, 0.018, 0.026)),
            "chest", "surface_detail", "rod_reel",
        )
        self.add_part(
            "ACC_SlickerClasp.02",
            make_box((0.050, -0.146, 1.086), (0.024, 0.018, 0.026)),
            "chest", "surface_detail", "rod_reel",
        )

        # The pipe, on the head bone, out of SOCKET_Mouth.
        self.add_part(
            "ACC_PipeStem",
            make_frustum_between(
                (0.008, -0.150, 1.474),
                (0.020, -0.246, 1.434),
                0.0074, 0.0086, 6, 1.0,
            ),
            "head", "signature_silhouette", "pipe_briar_dark",
        )
        self.add_part(
            "ACC_PipeBowl",
            make_frustum_between(
                (0.022, -0.252, 1.428),
                (0.024, -0.262, 1.492),
                0.026, 0.030, 8, 1.0,
            ),
            "head", "signature_silhouette", "pipe_briar",
        )
        # The one part the runtime drives. Its name is a contract:
        # LakeFishermanPipeEffect finds it by name and breathes the
        # ember, the point light and the plume off it.
        self.add_part(
            "ACC_PipeEmber",
            make_box((0.024, -0.262, 1.496), (0.036, 0.036, 0.010)),
            "head", "signature_silhouette", "amber",
        )

        # The rod, on the right hand. Forward and up, out of the fist.
        direction = (0.130, -0.960, 0.250)
        grip = (-0.730, -0.030, 1.048)

        def along(distance: float) -> tuple[float, float, float]:
            return (
                grip[0] + direction[0] * distance,
                grip[1] + direction[1] * distance,
                grip[2] + direction[2] * distance,
            )

        self.add_part(
            "ACC_RodGrip",
            make_frustum_between(along(-0.120), along(0.100), 0.021, 0.019, 8, 1.0),
            "hand.R", "signature_silhouette", "rod_cork",
        )
        self.add_part(
            "ACC_RodReel",
            make_box(
                (
                    along(0.128)[0],
                    along(0.128)[1],
                    along(0.128)[2] - 0.052,
                ),
                (0.056, 0.072, 0.072),
            ),
            "hand.R", "signature_silhouette", "rod_reel",
        )
        self.add_part(
            "ACC_RodButt",
            make_frustum_between(along(0.100), along(0.760), 0.0140, 0.0106, 6, 1.0),
            "hand.R", "signature_silhouette", "rod_cane",
        )
        self.add_part(
            "ACC_RodMid",
            make_frustum_between(along(0.760), along(1.450), 0.0106, 0.0070, 6, 1.0),
            "hand.R", "signature_silhouette", "rod_cane",
        )
        self.add_part(
            "ACC_RodTip",
            make_frustum_between(along(1.450), along(2.050), 0.0070, 0.0032, 6, 1.0),
            "hand.R", "signature_silhouette", "rod_cane",
        )

    def build_park_chess_player_body(self) -> None:
        """Old man in a worn overcoat under a chess king's crown.

        The source stays on the canonical A-pose skeleton so the shared
        Avatar copies exactly; the seat, the elbows on the board rim and
        the head sunk into both hands are all authored in ChessBrood
        rather than in the geometry.

        The chess reference is carried on two independent channels
        because the art bible refuses to let colour be the only proof.
        The silhouette owns the first: where every other design wears a
        hat, this one wears the tulle of a king - a band pulled down
        over the brow, a tapering body, a collar and a knop, and a small
        cross that has gone crooked. It is the whole read at 15-30 m, in
        fog and in grayscale. The cloth owns the second: a check on the
        scarf tails and on both lapels.

        Neither is white. The park runs on deep black-green, sandy
        grey-brown and cold bone, so the light square is bone and the
        dark one is the park's own black-green. He also sits directly
        under the one burning lamp on the wire, which is the second
        reason the light values stay near 0.6.

        The coat stops just below the hips on purpose. A stiff skirt is
        bound to the pelvis and cannot follow a knee, and this design
        spends its whole life with both knees folded up at a bench; a
        full-length hem would stand straight through both thighs.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.030, 1.528), (0.094, 0.090, 0.112), 12, 6),
            "head", "body", "chess_skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.012, 1.318), (0, -0.022, 1.452), 0.064, 0.054, 10),
            "neck", "body", "chess_skin",
        )
        self.add_part(
            "CLO_CoatChest",
            make_tapered_box((0, 0.006, 1.080), (0, -0.004, 1.340), (0.400, 0.246, 0), (0.412, 0.258, 0)),
            "chest", "body", "chess_coat",
        )
        self.add_part(
            "CLO_CoatWaist",
            make_tapered_box((0, 0.010, 0.900), (0, 0.006, 1.090), (0.392, 0.248, 0), (0.402, 0.250, 0)),
            "spine", "body", "chess_coat",
        )
        self.add_part(
            "CLO_CoatHem",
            make_tapered_box((0, 0.012, 0.655), (0, 0.010, 0.910), (0.424, 0.272, 0), (0.396, 0.252, 0)),
            "pelvis", "clothing", "chess_coat_dark",
        )
        self.add_part(
            "CLO_CoatYoke",
            make_tapered_box((0, 0.002, 1.180), (0, -0.004, 1.325), (0.444, 0.278, 0), (0.408, 0.256, 0)),
            "chest", "clothing", "chess_coat_light",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.208, sign * -0.004, 1.292)
            elbow = (sign * 0.470, -0.010, 1.175)
            wrist = (sign * 0.680, -0.018, 1.075)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.076, 0.062, 10),
                f"upper_arm.{side}", "clothing", "chess_coat",
            )
            self.add_part(
                f"CLO_SleeveLower.{side}",
                make_frustum_between(elbow, wrist, 0.058, 0.046, 8),
                f"forearm.{side}", "clothing", "chess_coat",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.632, -0.017, 1.096),
                    (sign * 0.694, -0.019, 1.068),
                    0.050, 0.045, 8,
                ),
                f"forearm.{side}", "clothing", "chess_coat_dark",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.722, -0.022, 1.048), (0.082, 0.068, 0.056)),
                f"hand.{side}", "body", "chess_skin",
            )
            # Both leg parts stop exactly at their own bone head. A shin
            # part authored above the knee swings out through the thigh
            # the moment the knee bends, and this design sits with both
            # knees folded to a right angle for its entire life.
            self.add_part(
                f"CLO_TrouserUpper.{side}",
                make_frustum_between(
                    (sign * 0.092, 0.006, 0.742),
                    (sign * 0.103, -0.012, 0.358),
                    0.084, 0.066, 8,
                ),
                f"thigh.{side}", "clothing", "chess_trousers",
            )
            self.add_part(
                f"CLO_TrouserLower.{side}",
                make_frustum_between(
                    (sign * 0.103, -0.012, 0.352),
                    (sign * 0.112, -0.024, 0.128),
                    0.070, 0.058, 8,
                ),
                f"shin.{side}", "clothing", "chess_trousers",
            )
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.086, 0.024),
                    (sign * 0.112, -0.052, 0.140),
                    (0.106, 0.264, 0),
                    (0.094, 0.190, 0),
                ),
                f"foot.{side}", "body", "chess_boot",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.112, -0.086, 0.012), (0.110, 0.272, 0.024)),
                f"foot.{side}", "body", "sole",
            )

        # The crown owns the silhouette and the exact 1.75 m envelope.
        # Only the cross reaches the ceiling, and it reaches it leaning:
        # a straight one would read as municipal signage rather than as
        # a chess piece that has been sat under for years.
        self.add_part(
            "CLO_CrownBand",
            make_frustum_between((0, -0.030, 1.590), (0, -0.030, 1.632), 0.116, 0.112, 12),
            "head", "signature_silhouette", "chess_crown_dark",
        )
        self.add_part(
            "CLO_CrownBody",
            make_frustum_between((0, -0.030, 1.632), (0, -0.030, 1.678), 0.110, 0.072, 12),
            "head", "signature_silhouette", "chess_crown",
        )
        self.add_part(
            "CLO_CrownCollar",
            make_frustum_between((0, -0.030, 1.678), (0, -0.030, 1.690), 0.084, 0.084, 12),
            "head", "signature_silhouette", "chess_crown_dark",
        )
        self.add_part(
            "CLO_CrownKnop",
            make_ellipsoid((0, -0.030, 1.700), (0.052, 0.050, 0.018), 10, 5),
            "head", "signature_silhouette", "chess_crown",
        )
        # The cross gets the whole top of the envelope. An earlier pass
        # gave it 24 mm and the review render showed why that is not
        # enough: at 24 mm it is one dark pixel through the PS1 composite
        # and the crown reads as a plain cap, which loses the entire
        # first channel of the design. Its upper face sits at exactly
        # 1.750, so this part sets the canonical height and nothing above
        # it may be authored.
        self.add_part(
            "ACC_CrownCross",
            make_tapered_box(
                (0, -0.030, 1.700),
                (0.024, -0.038, 1.750),
                (0.030, 0.030, 0),
                (0.024, 0.024, 0),
            ),
            "head", "signature_silhouette", "chess_crown_cross",
        )
        self.add_part(
            "ACC_CrownCrossArm",
            make_box((0.011, -0.0335, 1.7255), (0.078, 0.021, 0.018)),
            "head", "signature_silhouette", "chess_crown_cross",
        )

        # What is left of the face under the band: heavy brows, a big
        # nose and a grey moustache. The eyes are drawn because this is
        # the one design the player walks up to in order to look at.
        self.add_part(
            "ACC_Eye.L",
            make_box((0.042, -0.108, 1.548), (0.030, 0.018, 0.012)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Eye.R",
            make_box((-0.042, -0.108, 1.548), (0.030, 0.018, 0.012)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Brow",
            make_box((0, -0.112, 1.570), (0.136, 0.022, 0.016)),
            "head", "face_detail", "chess_grey",
        )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.130, 1.492), (0, -0.110, 1.536), (0.044, 0.050, 0), (0.030, 0.040, 0)),
            "head", "face_detail", "chess_skin",
        )
        self.add_part(
            "ACC_Moustache",
            make_box((0, -0.122, 1.470), (0.082, 0.026, 0.020)),
            "head", "face_detail", "chess_grey",
        )

    def build_park_chess_player_details(self) -> None:
        """The check, and the few things that say the coat is old.

        A check cannot be a texture here: every part is one flat colour
        on one shared material, exactly like the drawn board on the
        table, which is 64 boxes for the same reason. So the dark field
        is the cloth itself and the light squares are separate parts
        standing a few millimetres proud of it, alternating columns row
        by row so the pattern reads as a lattice rather than as stripes.

        The scarf carries it down the chest where a seated man folded
        over a board still shows cloth, and the lapels repeat it at the
        shoulders where the silhouette is widest.
        """

        self.add_part(
            "CLO_ScarfLoop",
            make_frustum_between((0, -0.008, 1.330), (0, -0.016, 1.406), 0.108, 0.098, 12),
            "neck", "clothing", "chess_check_dark",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"CLO_ScarfTail.{side}",
                make_tapered_box(
                    (sign * 0.052, -0.132, 1.010),
                    (sign * 0.058, -0.146, 1.330),
                    (0.086, 0.030, 0),
                    (0.092, 0.032, 0),
                ),
                "chest", "clothing", "chess_check_dark",
            )
            self.add_part(
                f"CLO_Lapel.{side}",
                make_tapered_box(
                    (sign * 0.088, -0.128, 1.086),
                    (sign * 0.132, -0.136, 1.318),
                    (0.076, 0.028, 0),
                    (0.104, 0.030, 0),
                ),
                "chest", "clothing", "chess_check_dark",
            )
            self.add_part(
                f"ACC_LapelCheck.{side}",
                make_box((sign * 0.112, -0.144, 1.240), (0.046, 0.024, 0.046)),
                "chest", "surface_detail", "chess_check_light",
            )

        # Three light squares per tail on a 0.042 lattice, columns
        # swapped every row. Two rows deep is all a 0.09 m tail can
        # carry and still be read as a check rather than as spots.
        check_rows = (
            (0.076, 1.268),
            (0.034, 1.226),
            (0.076, 1.184),
        )
        index = 0
        for side, sign in (("L", 1.0), ("R", -1.0)):
            for offset, height in check_rows:
                index += 1
                self.add_part(
                    f"ACC_ScarfCheck.{index:02d}",
                    make_box(
                        (sign * offset, -0.154, height),
                        (0.042, 0.026, 0.042),
                    ),
                    "chest", "surface_detail", "chess_check_light",
                )

        self.add_part(
            "ACC_CoatSeam",
            make_box((0, -0.126, 0.902), (0.392, 0.014, 0.014)),
            "spine", "surface_detail", "chess_coat_dark",
        )
        self.add_part(
            "ACC_CoatButton.01",
            make_box((0.016, -0.130, 1.040), (0.024, 0.018, 0.026)),
            "spine", "surface_detail", "chess_crown_dark",
        )
        self.add_part(
            "ACC_CoatButton.02",
            make_box((0.016, -0.128, 0.944), (0.024, 0.018, 0.026)),
            "spine", "surface_detail", "chess_crown_dark",
        )

    def build_park_checkers_player_body(self) -> None:
        """The same old man at the other table, under a draught.

        Everything below the neck is the chess player's geometry to the
        millimetre, and that is a requirement rather than a shortcut.
        His arm angles are a coordinate-descent solve against two
        measurements - an elbow on the board at `0.90` and a palm under
        the cheek - and both tables draw the same board over the same
        `0.54` plank. The solve therefore transfers exactly as long as
        the shoulder, the upper arm, the hand and the skull it reaches
        for are the same shapes in the same places. Change the coat and
        the solve has to be reopened; keep it and this design gets a
        fitted pose for free.

        `CLO_CoatHem` is the one part it would be most tempting to
        restyle and the one part that must not move: it rides the pelvis
        and its underside is exactly what the `perch_seat_height_m`
        validator measures down from, so it is the reason the runtime's
        `0.0651` pelvis lift is shared too.

        What changes is the read. Chess and draughts are played on one
        board, so the check is spent - it says "board", and both men are
        at one. What separates the games is the men, so both channels
        are re-derived from the piece. The silhouette owns the first:
        where the neighbour wears the tulle of a king, this one wears a
        single thick draught. The cloth owns the second: circles, run on
        the diagonal, in the neighbour's own light-square bone.

        The tilt of the draught is a measurement, not a mood. Every
        design is held to a `1.75 m` envelope to the last ten microns,
        and a cap lying flat on a skull that stops at `1.640` cannot
        reach it. So the draught is worn shoved back, and the angle is
        whatever puts its raised edge on the ceiling: where the king's
        cross takes the envelope standing straight up, the draught takes
        it lying almost flat, spending the whole allowance sideways.
        That is the silhouette inversion, and it also solves the fault
        three review renders found on the chess player - a bowed head
        with a wide brim in front of it - because a cap raked back off
        the brow leaves the face open from the park approach.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.030, 1.528), (0.094, 0.090, 0.112), 12, 6),
            "head", "body", "checkers_skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.012, 1.318), (0, -0.022, 1.452), 0.064, 0.054, 10),
            "neck", "body", "checkers_skin",
        )
        self.add_part(
            "CLO_CoatChest",
            make_tapered_box((0, 0.006, 1.080), (0, -0.004, 1.340), (0.400, 0.246, 0), (0.412, 0.258, 0)),
            "chest", "body", "checkers_coat",
        )
        self.add_part(
            "CLO_CoatWaist",
            make_tapered_box((0, 0.010, 0.900), (0, 0.006, 1.090), (0.392, 0.248, 0), (0.402, 0.250, 0)),
            "spine", "body", "checkers_coat",
        )
        self.add_part(
            "CLO_CoatHem",
            make_tapered_box((0, 0.012, 0.655), (0, 0.010, 0.910), (0.424, 0.272, 0), (0.396, 0.252, 0)),
            "pelvis", "clothing", "checkers_coat_dark",
        )
        self.add_part(
            "CLO_CoatYoke",
            make_tapered_box((0, 0.002, 1.180), (0, -0.004, 1.325), (0.444, 0.278, 0), (0.408, 0.256, 0)),
            "chest", "clothing", "checkers_coat_light",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.208, sign * -0.004, 1.292)
            elbow = (sign * 0.470, -0.010, 1.175)
            wrist = (sign * 0.680, -0.018, 1.075)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.076, 0.062, 10),
                f"upper_arm.{side}", "clothing", "checkers_coat",
            )
            self.add_part(
                f"CLO_SleeveLower.{side}",
                make_frustum_between(elbow, wrist, 0.058, 0.046, 8),
                f"forearm.{side}", "clothing", "checkers_coat",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.632, -0.017, 1.096),
                    (sign * 0.694, -0.019, 1.068),
                    0.050, 0.045, 8,
                ),
                f"forearm.{side}", "clothing", "checkers_coat_dark",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.722, -0.022, 1.048), (0.082, 0.068, 0.056)),
                f"hand.{side}", "body", "checkers_skin",
            )
            self.add_part(
                f"CLO_TrouserUpper.{side}",
                make_frustum_between(
                    (sign * 0.092, 0.006, 0.742),
                    (sign * 0.103, -0.012, 0.358),
                    0.084, 0.066, 8,
                ),
                f"thigh.{side}", "clothing", "checkers_trousers",
            )
            self.add_part(
                f"CLO_TrouserLower.{side}",
                make_frustum_between(
                    (sign * 0.103, -0.012, 0.352),
                    (sign * 0.112, -0.024, 0.128),
                    0.070, 0.058, 8,
                ),
                f"shin.{side}", "clothing", "checkers_trousers",
            )
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.086, 0.024),
                    (sign * 0.112, -0.052, 0.140),
                    (0.106, 0.264, 0),
                    (0.094, 0.190, 0),
                ),
                f"foot.{side}", "body", "checkers_boot",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.112, -0.086, 0.012), (0.110, 0.272, 0.024)),
                f"foot.{side}", "body", "sole",
            )

        # The draught. One piece, worn plate-flat and raked back off the
        # brow, and it owns both the silhouette and the exact 1.75 m
        # envelope the way the king's cross does next door.
        #
        # It is drawn as a turned edge rather than a plain disc, because
        # a plain disc worn on a head is a beret and the whole first
        # channel would be lost. The pale body is the piece; two dark
        # lips stand proud of it around the rim and stop short of each
        # other, so the pale body shows through between them as the
        # groove every draught is turned with. `flatten` goes to 1.0 -
        # the library default ovals a cross-section, which is right for
        # a limb and wrong for something that has to read as round.
        #
        # The band is what makes it worn rather than balanced. The first
        # review render had the draught alone on the skull and it read
        # as a plate resting on the back of a head, because nothing
        # joined the two. So the piece gets the same band the king's
        # tulle has next door - pulled down over the brow, dark, cut to
        # the skull - and the draught is the crown of a cap instead of
        # an object sitting on hair.
        self.add_part(
            "CLO_DiscBand",
            make_frustum_between(
                (0, -0.030, 1.556), (0, -0.030, 1.618), 0.118, 0.114, 12,
            ),
            "head", "signature_silhouette", "checkers_disc_rim",
        )
        # The rake is not authored by eye. Given the radius of the piece
        # and where it beds onto the band there is exactly one angle at
        # which its raised edge lands on 1.750, and this is it.
        #
        # It is also centred over the skull rather than hung off the
        # back of it, which the same render got wrong: shoved back far
        # enough to clear the brow is right, shoved back far enough to
        # leave the head is a hat falling off.
        # Radius and rake are both set by the face, not by taste. A
        # bench sitter's head is below the player's eye, so the player
        # always looks down onto this piece, and the third review render
        # showed a 0.39 m plate lying near-horizontal curtaining the
        # whole face from the only angle the game ever offers. What
        # governs that is the radius far more than the angle - the rake
        # lifts the near edge a little, the radius decides how far it
        # reaches out over the brow in the first place. So the draught
        # came down to 0.30 m across and went back to 40 degrees, which
        # puts its leading edge 29 mm forward of the brow and 147 mm
        # above it: 79 degrees up from the eye's own forward line, and
        # the face is open from every standing approach.
        #
        # It is still 2.6 times the width of the crown's band next door
        # and still perfectly flat, so nothing is lost from the read the
        # piece exists for.
        disc_bottom = (-0.004, -0.024, 1.612904)
        disc_top = (-0.00078, 0.00550, 1.648044)
        disc_lip_low_high = (-0.00303, -0.01515, 1.623444)
        disc_lip_high_low = (-0.00175, -0.00335, 1.637504)
        self.add_part(
            "CLO_DiscBody",
            make_frustum_between(
                disc_bottom, disc_top, 0.150, 0.150, 12, 1.0,
            ),
            "head", "signature_silhouette", "checkers_disc",
        )
        # Two dark lips around the rim that stop short of each other, so
        # the pale body shows between them as the groove every draught
        # is turned with. One part cheaper than drawing the groove, and
        # it survives the downsample the way an inset line would not.
        self.add_part(
            "CLO_DiscRimLower",
            make_frustum_between(
                disc_bottom, disc_lip_low_high, 0.158, 0.158, 12, 1.0,
            ),
            "head", "signature_silhouette", "checkers_disc_rim",
        )
        # The upper lip caps the outermost, highest edge of the whole
        # design. Its rim sits on 1.750 and nothing above it may be
        # authored.
        self.add_part(
            "CLO_DiscRimUpper",
            make_frustum_between(
                disc_lip_high_low, disc_top, 0.158, 0.158, 12, 1.0,
            ),
            "head", "signature_silhouette", "checkers_disc_rim",
        )
        # The turned centre on the top face. Small, but it is the one
        # mark that separates a draught from a plate at conversation
        # distance, which is the only distance it is asked to work at.
        self.add_part(
            "ACC_DiscPip",
            make_frustum_between(
                (-0.00064, 0.00678, 1.649574),
                (0.00020, 0.01448, 1.658744),
                0.050, 0.045, 10, 1.0,
            ),
            "head", "signature_silhouette", "checkers_disc_ring",
        )

        # The same face, a few years and one habit apart: no moustache
        # on this one, a week of grey stubble along the jaw instead, and
        # a lighter brow. The eyes are drawn for the same reason as the
        # neighbour's - this is a design the player walks up to.
        self.add_part(
            "ACC_Eye.L",
            make_box((0.042, -0.108, 1.548), (0.030, 0.018, 0.012)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Eye.R",
            make_box((-0.042, -0.108, 1.548), (0.030, 0.018, 0.012)),
            "head", "face_detail", "void",
        )
        self.add_part(
            "ACC_Brow",
            make_box((0, -0.112, 1.568), (0.128, 0.020, 0.012)),
            "head", "face_detail", "checkers_grey",
        )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.126, 1.494), (0, -0.108, 1.534), (0.040, 0.046, 0), (0.028, 0.038, 0)),
            "head", "face_detail", "checkers_skin",
        )
        self.add_part(
            "ACC_Stubble",
            make_tapered_box(
                (0, -0.088, 1.442),
                (0, -0.104, 1.492),
                (0.132, 0.150, 0),
                (0.152, 0.170, 0),
            ),
            "head", "face_detail", "checkers_grey",
        )

    def build_park_checkers_player_details(self) -> None:
        """Circles on the diagonal, and the same tired coat.

        The neighbour's check is a lattice of light squares standing
        proud of dark cloth, drawn as separate parts for the reason the
        table board is 64 boxes rather than a texture. This carries the
        identical construction, the identical pitch and the identical
        bone - and swaps the square for a circle and the lattice for a
        diagonal.

        Both halves of that swap are the game. A draught is round where
        a chessman is turned and individual, and it only ever travels
        the diagonal, on the dark squares alone. So a light circle
        stepping down and inward says draughts as precisely as an
        alternating square says board, and the two men can never be
        mistaken for one another at any distance - while the shared bone
        value keeps saying they are sitting at the same set.
        """

        self.add_part(
            "CLO_ScarfLoop",
            make_frustum_between((0, -0.008, 1.330), (0, -0.016, 1.406), 0.108, 0.098, 12),
            "neck", "clothing", "checkers_spot_dark",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"CLO_ScarfTail.{side}",
                make_tapered_box(
                    (sign * 0.052, -0.132, 1.010),
                    (sign * 0.058, -0.146, 1.330),
                    (0.086, 0.030, 0),
                    (0.092, 0.032, 0),
                ),
                "chest", "clothing", "checkers_spot_dark",
            )
            self.add_part(
                f"CLO_Lapel.{side}",
                make_tapered_box(
                    (sign * 0.088, -0.128, 1.086),
                    (sign * 0.132, -0.136, 1.318),
                    (0.076, 0.028, 0),
                    (0.104, 0.030, 0),
                ),
                "chest", "clothing", "checkers_spot_dark",
            )
            # One on each lapel, where the neighbour carries one square,
            # and deliberately off the tails' diagonal rather than on
            # it: a second run up here crowds the first into a knot.
            self.add_part(
                f"ACC_LapelSpot.{side}",
                make_frustum_between(
                    (sign * 0.118, -0.144, 1.258),
                    (sign * 0.118, -0.166, 1.258),
                    0.020, 0.020, 10, 1.0,
                ),
                "chest", "surface_detail", "checkers_spot_light",
            )

        # Three circles per tail, stepping down and inward together. The
        # circle is deliberately smaller than the neighbour's square
        # even though the pitch is the same, and that is the one place
        # the two patterns cannot be built alike: squares on a lattice
        # tile, so they may touch and still read as a check, but circles
        # at the same size touch and fuse into one chain of blobs. The
        # first review render showed exactly that. A gap is what makes
        # them count as separate men.
        #
        # The step across is smaller than the step down on purpose: a
        # true 45 degrees walks the bottom circle off the edge of a
        # 0.09 m tail, and a diagonal that leaves the cloth stops being
        # a diagonal.
        #
        # And the run goes down and OUTWARD rather than down and inward,
        # which the second review render settled. Mirrored inward runs
        # meet at the sternum, and the six circles plus the lapels pile
        # into one pale knot in the middle of the chest that reads as
        # spillage. Opening them downward spreads the same six across
        # the whole width of a folded man and lets each one be a piece.
        spot_rows = (
            (0.022, 1.278),
            (0.054, 1.232),
            (0.086, 1.186),
        )
        index = 0
        for side, sign in (("L", 1.0), ("R", -1.0)):
            for offset, height in spot_rows:
                index += 1
                self.add_part(
                    f"ACC_ScarfSpot.{index:02d}",
                    make_frustum_between(
                        (sign * offset, -0.148, height),
                        (sign * offset, -0.170, height),
                        0.016, 0.016, 10, 1.0,
                    ),
                    "chest", "surface_detail", "checkers_spot_light",
                )

        self.add_part(
            "ACC_CoatSeam",
            make_box((0, -0.126, 0.902), (0.392, 0.014, 0.014)),
            "spine", "surface_detail", "checkers_coat_dark",
        )
        self.add_part(
            "ACC_CoatButton.01",
            make_box((0.016, -0.130, 1.040), (0.024, 0.018, 0.026)),
            "spine", "surface_detail", "checkers_disc_rim",
        )
        self.add_part(
            "ACC_CoatButton.02",
            make_box((0.016, -0.128, 0.944), (0.024, 0.018, 0.026)),
            "spine", "surface_detail", "checkers_disc_rim",
        )

    def build_weigh_attendant_body(self) -> None:
        """Tired industrial worker in a quilted jacket and a knit cap.

        The source stays on the canonical A-pose skeleton so the shared
        Avatar copies exactly; the dial-reading gaze, the chalk crouch
        and the weighing standstill are all authored in WeigherCheck
        and WeighedPace rather than in the geometry. Deliberately no
        authority markers — no armband, no hi-vis, no baton — so the
        weighbridge never reads as a checkpoint.
        """

        self.add_part(
            "GEO_Head",
            make_ellipsoid((0, -0.036, 1.560), (0.100, 0.094, 0.124), 12, 6),
            "head", "body", "skin",
        )
        self.add_part(
            "GEO_Neck",
            make_frustum_between((0, -0.010, 1.320), (0, -0.024, 1.470), 0.068, 0.058, 10),
            "neck", "body", "skin",
        )
        # The quilted torso: boxy, widest at the chest, hem at the hips.
        self.add_part(
            "CLO_JacketChest",
            make_tapered_box((0, 0.006, 1.070), (0, -0.006, 1.345), (0.410, 0.250, 0), (0.430, 0.270, 0)),
            "chest", "body", "weigh_jacket",
        )
        self.add_part(
            "CLO_JacketWaist",
            make_tapered_box((0, 0.014, 0.870), (0, 0.008, 1.090), (0.400, 0.255, 0), (0.415, 0.255, 0)),
            "spine", "body", "weigh_jacket",
        )
        self.add_part(
            "CLO_JacketHem",
            make_tapered_box((0, 0.016, 0.700), (0, 0.014, 0.890), (0.420, 0.272, 0), (0.405, 0.258, 0)),
            "pelvis", "clothing", "weigh_jacket_dark",
        )
        self.add_part(
            "CLO_Collar",
            make_frustum_between((0, -0.008, 1.330), (0, -0.014, 1.395), 0.105, 0.088, 10),
            "neck", "clothing", "weigh_jacket_dark",
        )
        for side, sign in (("L", 1.0), ("R", -1.0)):
            shoulder = (sign * 0.208, sign * -0.004, 1.292)
            elbow = (sign * 0.470, -0.010, 1.175)
            wrist = (sign * 0.680, -0.018, 1.075)
            self.add_part(
                f"CLO_Sleeve.{side}",
                make_frustum_between(shoulder, elbow, 0.078, 0.064, 10),
                f"upper_arm.{side}", "clothing", "weigh_jacket",
            )
            self.add_part(
                f"CLO_SleeveLower.{side}",
                make_frustum_between(elbow, wrist, 0.058, 0.046, 8),
                f"forearm.{side}", "clothing", "weigh_jacket",
            )
            self.add_part(
                f"CLO_SleeveCuff.{side}",
                make_frustum_between(
                    (sign * 0.630, -0.016, 1.100),
                    (sign * 0.694, -0.019, 1.068),
                    0.050, 0.044, 8,
                ),
                f"forearm.{side}", "clothing", "weigh_jacket_dark",
            )
            self.add_part(
                f"GEO_Hand.{side}",
                make_box((sign * 0.718, -0.020, 1.055), (0.082, 0.068, 0.056)),
                f"hand.{side}", "body", "skin",
            )
            hip = (sign * 0.096, 0.004, 0.730)
            knee = (sign * 0.103, -0.012, 0.354)
            ankle = (sign * 0.112, -0.022, 0.095)
            self.add_part(
                f"CLO_TrouserUpper.{side}",
                make_frustum_between(hip, knee, 0.086, 0.062, 8),
                f"thigh.{side}", "clothing", "weigh_trousers",
            )
            self.add_part(
                f"CLO_TrouserLower.{side}",
                make_frustum_between(knee, ankle, 0.060, 0.050, 8),
                f"shin.{side}", "clothing", "weigh_trousers",
            )
            self.add_part(
                f"GEO_Boot.{side}",
                make_tapered_box(
                    (sign * 0.112, -0.085, 0.030),
                    (sign * 0.112, -0.052, 0.145),
                    (0.104, 0.260, 0),
                    (0.092, 0.190, 0),
                ),
                f"foot.{side}", "body", "shoe",
            )
            self.add_part(
                f"GEO_BootSole.{side}",
                make_box((sign * 0.112, -0.085, 0.012), (0.108, 0.268, 0.024)),
                f"foot.{side}", "body", "sole",
            )
        # The knit cap owns the silhouette and the exact 1.75 m envelope.
        self.add_part(
            "CLO_Cap",
            make_ellipsoid((0, -0.012, 1.638), (0.112, 0.108, 0.100), 12, 6),
            "head", "signature_silhouette", "weigh_cap",
        )
        self.add_part(
            "CLO_CapBand",
            make_frustum_between((0, -0.012, 1.556), (0, -0.012, 1.616), 0.118, 0.112, 12),
            "head", "clothing", "weigh_cap",
        )
        self.add_part(
            "CLO_CapCrown",
            make_tapered_box((0, -0.010, 1.700), (0, -0.008, 1.750), (0.074, 0.076, 0), (0.032, 0.034, 0)),
            "head", "signature_silhouette", "weigh_cap",
        )
        for side, x in (("L", 0.045), ("R", -0.045)):
            self.add_part(
                f"ACC_Eye.{side}",
                make_box((x, -0.124, 1.566), (0.034, 0.018, 0.022)),
                "head", "face_detail", "void",
            )
        self.add_part(
            "ACC_Nose",
            make_tapered_box((0, -0.142, 1.508), (0, -0.126, 1.548), (0.038, 0.046, 0), (0.028, 0.038, 0)),
            "head", "face_detail", "skin",
        )
        self.add_part(
            "ACC_Mouth",
            make_box((0, -0.128, 1.476), (0.056, 0.020, 0.014)),
            "head", "face_detail", "void",
        )

    def build_weigh_attendant_details(self) -> None:
        """Quilt seams, buttons and the one working prop: the chalk.

        The chalk stub rides the right fist along the canonical
        cigarette direction; the runtime shows it only on the weigher
        and hides it for the worker's free hands.
        """

        # Horizontal quilt seams read as the padded jacket's stitching.
        for index, height in enumerate((1.290, 1.190, 1.090), start=1):
            self.add_part(
                f"ACC_QuiltSeam.{index:02d}",
                make_box((0, -0.148, height), (0.380, 0.014, 0.016)),
                "chest" if height > 1.1 else "spine",
                "surface_detail", "weigh_jacket_dark",
            )
        for index, height in enumerate((0.990, 0.905), start=4):
            self.add_part(
                f"ACC_QuiltSeam.{index:02d}",
                make_box((0, -0.142, height), (0.370, 0.014, 0.016)),
                "spine", "surface_detail", "weigh_jacket_dark",
            )
        self.add_part(
            "ACC_JacketButton.01",
            make_box((0.062, -0.152, 1.240), (0.024, 0.018, 0.024)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_JacketButton.02",
            make_box((0.062, -0.148, 1.120), (0.024, 0.018, 0.024)),
            "chest", "surface_detail", "button",
        )
        self.add_part(
            "ACC_JacketButton.03",
            make_box((0.062, -0.146, 1.000), (0.024, 0.018, 0.024)),
            "spine", "surface_detail", "button",
        )
        # One patch pocket per hip: a working jacket, not a uniform.
        for side, sign in (("L", 1.0), ("R", -1.0)):
            self.add_part(
                f"ACC_HipPocket.{side}",
                make_box((sign * 0.130, -0.146, 0.790), (0.110, 0.018, 0.120)),
                "pelvis", "surface_detail", "weigh_jacket_dark",
            )
        self.add_part(
            "ACC_Chalk",
            make_frustum_between(
                (-0.740, -0.048, 1.050),
                (-0.744, -0.108, 1.053),
                0.012, 0.010, 6, 1.0,
            ),
            "hand.R", "surface_detail", "chalk",
        )

    def configure_scene_metadata(self) -> None:
        scene = bpy.context.scene
        scene["bp_generator"] = "tools/build-city-pedestrian-3d-model.py"
        scene["bp_generator_version"] = GENERATOR_VERSION
        scene["bp_design_id"] = self.spec.design_id
        scene["bp_seed"] = self.spec.seed
        scene["bp_has_own_animations"] = False
        scene["bp_runtime_material"] = "Assets/Player3D/Materials/Player3DLit.mat"


def triangulated_count(mesh: bpy.types.Mesh) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)


def stable_float(value: float) -> float:
    rounded = round(float(value), 6)
    return 0.0 if rounded == -0.0 else rounded


def validate_result(result: BuildResult, archetype: ArchetypeSpec) -> ValidationReport:
    # Parenting and armature setup are data-API operations; force the depsgraph
    # once before reading object matrices for deterministic source bounds.
    bpy.context.view_layer.update()
    errors: list[str] = []
    bones = list(result.rig.data.bones)
    if [bone.name for bone in bones] != [spec.name for spec in SKELETON]:
        errors.append("Generic bone order/names diverge from PlayerCharacter3D")
    for bone_spec in SKELETON:
        bone = result.rig.data.bones.get(bone_spec.name)
        if bone is None:
            continue
        actual_parent = bone.parent.name if bone.parent is not None else None
        if actual_parent != bone_spec.parent:
            errors.append(f"{bone_spec.name} parent is {actual_parent!r}, expected {bone_spec.parent!r}")
        if (bone.head_local - v(bone_spec.head)).length > 0.000001:
            errors.append(f"{bone_spec.name} head diverges from canonical Player A-pose")
        if (bone.tail_local - v(bone_spec.tail)).length > 0.000001:
            errors.append(f"{bone_spec.name} tail diverges from canonical Player A-pose")
        if bone.use_deform != bone_spec.deform:
            errors.append(f"{bone_spec.name} deform flag diverges from canonical Player rig")

    if bpy.data.actions:
        errors.append("Pedestrian model must contain no authored Actions")
    if result.rig.animation_data is not None and result.rig.animation_data.action is not None:
        errors.append("Pedestrian rig has an active animation")

    expected_pivots = PIPEBACK_PIVOT_NAMES if archetype.wheel_radius_m is not None else ()
    if tuple(result.pivots) != expected_pivots:
        errors.append(
            f"Mechanism pivots are {tuple(result.pivots)!r}; "
            f"expected {expected_pivots!r}"
        )
    signature_pivots = []
    for name, pivot in result.pivots.items():
        if pivot.type != "EMPTY" or pivot.parent != result.root:
            errors.append(f"{name} must be an Empty directly below ROOT_Player")
        if not bool(pivot.get("bp_pivot", False)):
            errors.append(f"{name} lacks its deterministic pivot marker")
        signature_pivots.append(
            {
                "name": name,
                "location": [stable_float(value) for value in pivot.location],
            }
        )

    forbidden_fragments = ("bandage", "shoulderpatch", "satchel")
    mesh_count = len(result.parts)
    triangle_count = 0
    world_vertices: list[Vector] = []
    signature_parts = []
    seen_meshes: set[int] = set()
    for part in sorted(result.parts, key=lambda item: item.obj.name):
        obj = part.obj
        mesh = obj.data
        if any(fragment in obj.name.lower() for fragment in forbidden_fragments):
            errors.append(f"{obj.name} reuses a forbidden player signature detail")
        if mesh.as_pointer() in seen_meshes:
            errors.append(f"{obj.name} reuses another part's mesh datablock")
        seen_meshes.add(mesh.as_pointer())
        if len(mesh.materials) != 1 or mesh.materials[0] != result.material:
            errors.append(f"{obj.name} does not use the one shared source material")
        if len(obj.vertex_groups) != 1 or obj.vertex_groups[0].name != part.bone:
            errors.append(f"{obj.name} must have one rigid group for {part.bone}")
        for vertex in mesh.vertices:
            weights = [group for group in vertex.groups if group.weight > 0.000001]
            if len(weights) != 1 or abs(weights[0].weight - 1.0) > 0.000001:
                errors.append(f"{obj.name} vertex {vertex.index} is not rigidly weighted")
                break
            world_vertices.append(obj.matrix_world @ vertex.co)
        triangles = triangulated_count(mesh)
        triangle_count += triangles
        signature_parts.append(
            {
                "name": obj.name,
                "bone": part.bone,
                "role": part.role,
                "palette_name": part.palette_name,
                "color": [stable_float(component) for component in part.color],
                "vertices": [
                    [stable_float(component) for component in (obj.matrix_world @ vertex.co)]
                    for vertex in mesh.vertices
                ],
                "triangles": triangles,
            }
        )

    min_triangles, max_triangles = archetype.triangle_budget
    if not min_triangles <= triangle_count <= max_triangles:
        errors.append(
            f"Triangle budget is {triangle_count}; expected {min_triangles}-{max_triangles}"
        )
    if mesh_count < 24 or mesh_count > 52:
        errors.append(f"Mesh count is {mesh_count}; expected 24-52 lightweight parts")
    if not world_vertices:
        errors.append("Pedestrian contains no mesh vertices")
        bounds_min = Vector((0, 0, 0))
        bounds_max = Vector((0, 0, 0))
    else:
        bounds_min = Vector(
            tuple(min(vertex[axis] for vertex in world_vertices) for axis in range(3))
        )
        bounds_max = Vector(
            tuple(max(vertex[axis] for vertex in world_vertices) for axis in range(3))
        )
        if abs(bounds_min.z) > 0.00001:
            errors.append(f"Footwear must ground at z=0, got {bounds_min.z:.6f}")
        if abs(bounds_max.z - CANONICAL_HEIGHT) > 0.00001:
            errors.append(
                f"Silhouette must preserve canonical 1.75 m height, got {bounds_max.z:.6f}"
            )
        if bounds_max.x - bounds_min.x > 1.65:
            errors.append("A-pose width unexpectedly exceeds the player's envelope")

    material = result.material
    emission = False
    if material.get("bp_emissive", True):
        emission = True
    if emission:
        errors.append("Shared source material must be explicitly non-emissive")
    if any(obj.type in {"LIGHT", "CAMERA"} for obj in result.export_collection.objects):
        errors.append("Export collection contains a light or camera")

    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "design_id": archetype.design_id,
        "seed": archetype.seed,
        "skeleton": [
            {
                "name": spec.name,
                "head": list(spec.head),
                "tail": list(spec.tail),
                "parent": spec.parent,
                "connected": spec.connected,
                "deform": spec.deform,
            }
            for spec in SKELETON
        ],
        "parts": signature_parts,
        "pivots": signature_pivots,
    }
    signature = hashlib.sha256(
        json.dumps(signature_payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()

    if errors:
        formatted = "\n".join(f"  - {error}" for error in errors)
        raise RuntimeError(f"City pedestrian validation failed:\n{formatted}")

    return ValidationReport(
        mesh_count,
        triangle_count,
        tuple(stable_float(component) for component in bounds_min),
        tuple(stable_float(component) for component in bounds_max),
        signature,
    )


def select_export_objects(result: BuildResult) -> None:
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    result.root.select_set(True)
    result.rig.select_set(True)
    for pivot in result.pivots.values():
        pivot.select_set(True)
    for part in result.parts:
        part.obj.select_set(True)
    bpy.context.view_layer.objects.active = result.rig


def export_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    select_export_objects(result)
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"EMPTY", "ARMATURE", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=False,
        use_armature_deform_only=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_custom_props=True,
    )


def render_preview(path: Path, result: BuildResult, spec: ArchetypeSpec) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    presentation = bpy.data.collections.get("PRESENTATION_CityPedestrian")
    if presentation is None:
        raise RuntimeError("Preview collection is missing")

    # A design whose whole content is its posture is useless in the bind
    # A-pose: the wheelchair rider would be standing through his own
    # chair and the chess player would be a man staring at nothing with
    # his arms out. Both are previewed in the stance they are built for.
    # Keyed by archetype rather than by contract: `perch_seat_height_m`
    # says a design is seated on world timber, not which posture it sits
    # in, and the second bench sitter proved the difference the moment
    # he existed.
    preview_pose = None
    if spec.wheel_radius_m is not None:
        preview_pose = pipeback_base_pose()
    elif spec.perch_seat_height_m is not None:
        preview_pose = PERCH_PREVIEW_POSES.get(
            spec.key, chess_player_base_pose
        )()
    posed_preview = preview_pose is not None
    perch_drop = 0.0
    if posed_preview:
        apply_pose(result.rig, preview_pose)
        bpy.context.view_layer.update()
    if spec.perch_seat_height_m is not None:
        # A perched design's boots stop above the plane the preview
        # ground is on, because in the world it is the seat that carries
        # him and not the lawn. Setting him down on the review floor is
        # what makes the pose readable instead of levitating.
        depsgraph = bpy.context.evaluated_depsgraph_get()
        perch_drop = min(
            evaluated_part_min_z(part, depsgraph) for part in result.parts
        )
        result.rig.location.z -= perch_drop
        bpy.context.view_layer.update()

    camera_data = bpy.data.cameras.new("CAM_PedestrianPreview")
    camera = bpy.data.objects.new("CAM_PedestrianPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = {
        "chair_carrier": (2.85, -4.60, 2.10),
        "kettle_hat": (2.55, -4.25, 1.90),
        "long_arm": (2.60, -4.35, 2.05),
        "helmet_lamp": (2.70, -4.30, 1.95),
        "pipeback_roller": (2.80, -4.65, 1.82),
        # From his own right, and from lower down. The rod leaves the
        # right fist along -Y, so the library's usual left-front camera
        # would look straight down two metres of it and see a dot.
        "lake_fisherman": (-4.30, -3.15, 1.95),
        # Lower and closer. He is seated and folded forward, so the
        # library's standing camera looks down onto a crown and misses
        # both the face in the hands and the check on the scarf.
        "park_chess_player": (2.35, -3.90, 1.60),
        # Deliberately the neighbour's camera, near enough to the metre.
        # The first pass mirrored it to -X to look along the rake of the
        # draught and put the model square in front of the key, which
        # flattened every value and made a design that is meant to be
        # judged beside the chess player impossible to compare with him.
        # Same side, same height, same lens: the pair is the subject.
        "park_checkers_player": (2.40, -3.88, 1.58),
    }.get(spec.key, (2.65, -4.40, 2.10))
    target = Vector((0, 0, 0.84 if posed_preview else 0.88))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 56
    scene.camera = camera

    for name, location, energy, color, radius in (
        ("Key", (-2.4, -3.0, 4.2), 900.0, (0.72, 0.82, 0.72), 3.0),
        ("Rim", (2.6, 1.2, 3.2), 650.0, (0.35, 0.48, 0.42), 2.0),
        ("Warm", (-1.0, -1.7, 1.0), 280.0, (0.85, 0.53, 0.25), 1.4),
    ):
        light_data = bpy.data.lights.new(f"LIGHT_{name}", "AREA")
        light_data.energy = energy
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = radius
        light = bpy.data.objects.new(f"LIGHT_{name}", light_data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()

    ground_mesh = bpy.data.meshes.new("PreviewGround_Mesh")
    vertices, faces = make_box((0, 0.35, -0.035), (5.5, 5.5, 0.07))
    ground_mesh.from_pydata(vertices, [], faces)
    ground = bpy.data.objects.new("PreviewGround", ground_mesh)
    presentation.objects.link(ground)
    ground_material = bpy.data.materials.new("MAT_PreviewGround")
    ground_material.diffuse_color = (0.025, 0.040, 0.034, 1)
    ground.data.materials.append(ground_material)

    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    if posed_preview:
        if perch_drop != 0.0:
            result.rig.location.z += perch_drop
        reset_pose(result.rig)
        bpy.context.view_layer.update()


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def write_manifest(
    path: Path,
    result: BuildResult,
    report: ValidationReport,
    spec: ArchetypeSpec,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-pedestrian-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": spec.design_id,
        "display_name": spec.display_name,
        "seed": spec.seed,
        "height_m": CANONICAL_HEIGHT,
        "pose": "apose",
        "forward_axis": "-Y",
        "anatomical_left_axis": "+X",
        "mesh_count": report.mesh_count,
        "triangle_count": report.triangle_count,
        "triangle_budget": list(spec.triangle_budget),
        "staged": spec.staged,
        "pool_eligible": spec.pool_eligible,
        "wheel_radius_m": spec.wheel_radius_m or 0.0,
        "pivot_names": list(result.pivots),
        "bounds_min": list(report.bounds_min),
        "bounds_max": list(report.bounds_max),
        "material_asset": "Assets/Player3D/Materials/Player3DLit.mat",
        "emissive": False,
        "colliders": False,
        "animation_count": 0,
        "animations": [],
        "shared_animation_source": ANIMATION_SOURCE,
        "shared_clips": (
            [spec.idle_clip, spec.walk_clip]
            + ([spec.sit_clip] if spec.sit_clip is not None else [])
            + ([spec.action_clip] if spec.action_clip is not None else [])
        ),
        "rides_bus": spec.sit_clip is not None,
        "seated_clearance_m": (
            list(spec.seated_clearance_m)
            if spec.seated_clearance_m is not None
            else None
        ),
        "perch_seat_height_m": (
            list(spec.perch_seat_height_m)
            if spec.perch_seat_height_m is not None
            else None
        ),
        "build_signature": report.build_signature,
        "bones": [
            {
                "name": spec.name,
                "parent": spec.parent or "",
                "head": list(spec.head),
                "tail": list(spec.tail),
                "deform": spec.deform,
            }
            for spec in SKELETON
        ],
        "parts": [
            {
                "name": part.obj.name,
                "role": part.role,
                "bone": part.bone,
                "palette_name": part.palette_name,
                "base_color": [stable_float(component) for component in part.color],
                "vertices": len(part.obj.data.vertices),
                "triangles": triangulated_count(part.obj.data),
            }
            for part in sorted(result.parts, key=lambda item: item.obj.name)
        ],
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


ACTION_SPECS = (
    ActionSpec(
        "LampshadeIdle", "lampshade_walker_v1", 2.0, 48,
        "persistent C-curve, withdrawn neck, bent knees",
        "weary asymmetric weight shift",
    ),
    ActionSpec(
        "LampshadeWalk", "lampshade_walker_v1", 1.25, 30,
        "persistent C-curve, withdrawn neck, bent knees",
        "short uneven steps, heavy left boot and quick right recovery",
    ),
    ActionSpec(
        "ChairCarrierIdle", "chair_carrier_v1", 1.5, 36,
        "upright load-balanced spine, hands fixed on shoulder loops",
        "small precise weight correction under chair load",
    ),
    ActionSpec(
        "ChairCarrierWalk", "chair_carrier_v1", 1.0, 24,
        "upright load-balanced spine, hands fixed on shoulder loops",
        "high-knee precise heel-led steps with minimal arm swing",
    ),
    ActionSpec(
        "KettleHatIdle", "kettle_hat_walker_v1", 1.75, 42,
        "low stout stance, belly forward, head sunk under the kettle",
        "slow settling weight roll with counter-phased belly and kettle",
    ),
    ActionSpec(
        "KettleHatWalk", "kettle_hat_walker_v1", 0.75, 18,
        "low stout stance, belly forward, head sunk under the kettle",
        "fast short steps with a constant waddle and counter-phased kettle",
    ),
    ActionSpec(
        "LongArmIdle", "long_arm_walker_v1", 2.5, 60,
        "narrow still body, raised shoulders, arms hanging to the ankles",
        "dead-still torso under a slow residual arm sway that never settles",
    ),
    ActionSpec(
        "LongArmWalk", "long_arm_walker_v1", 1.5, 36,
        "narrow still body, raised shoulders, arms hanging to the ankles",
        "slow shuffle with barely lifted feet and a lagging pendulum swing",
    ),
    ActionSpec(
        "LampshadeSit", "lampshade_walker_v1", 3.0, 72,
        "seated C-curve, shade tipped forward over folded knees",
        "settled seated breath that never straightens the spine",
        seated=True,
    ),
    ActionSpec(
        "ChairCarrierSit", "chair_carrier_v1", 2.5, 60,
        "upright seated spine, the inverted chair still shouldered",
        "small seated correction that keeps the chair balanced",
        seated=True,
    ),
    ActionSpec(
        "KettleHatSit", "kettle_hat_walker_v1", 2.75, 66,
        "stout seated stance, short legs hanging clear of the floor",
        "counter-phased belly and kettle sway with the legs at rest",
        seated=True,
    ),
    ActionSpec(
        "LongArmSit", "long_arm_walker_v1", 3.25, 78,
        "narrow seated torso, long forearms folded onto the knees",
        "residual arm sway that never settles, now over the knees",
        seated=True,
    ),
    ActionSpec(
        "HelmetLampIdle", "helmet_lamp_hopper_v1", 2.0, 48,
        "coiled crouch on both hind feet, forearms tucked like forepaws",
        "settled twitching crouch while the helmet beam sweeps side to side",
    ),
    ActionSpec(
        "HelmetLampHop", "helmet_lamp_hopper_v1", 1.0, 24,
        "coiled crouch on both hind feet, forearms tucked like forepaws",
        "two-footed rabbit hop: crouch, launch, tucked airborne apex, landing",
    ),
    ActionSpec(
        "PipebackIdle", "pipeback_roller_v1", 3.0, 72,
        "self-propelled seated posture, feet planted on twin footrests",
        "still head over a slow breath that pumps bellows under the pipe load",
    ),
    ActionSpec(
        "PipebackRoll", "pipeback_roller_v1", 2.0, 48,
        "self-propelled seated posture, hands following both raised push levers",
        "two-handed lever push, release and recovery under a swaying pipe load",
    ),
    ActionSpec(
        "BabushkaSmoke", "yard_babushka_v1", 4.0, 96,
        "hunched strolling stance, cigarette held ready in the right hand",
        "four shuffling steps under emphatic left-arm talk, one drag per lap",
    ),
    ActionSpec(
        "BabushkaBeat", "yard_babushka_v1", 1.5, 36,
        "hunched working stance squared to the hung carpet",
        "overhead wind-up, sharp forward beater strike and a rocking recovery",
    ),
    ActionSpec(
        "WeigherCheck", "weigh_attendant_v1", 6.0, 144,
        "planted stance beside the mechanism, chalk ready in the right hand",
        "look up at the dial, lean in to the linkage, crouch to chalk the deck edge",
    ),
    # The standstill window (normalized 0.36-0.64) must match
    # WeighbridgeAttendantPresentation.PauseStart/EndNormalized: the
    # runtime freezes the worker's corridor travel over exactly that
    # window, so the pose has to stand square through it.
    ActionSpec(
        "WeighedPace", "weigh_attendant_v1", 12.0, 288,
        "burdened walk down the deck axis with free hands",
        "shuffling steps, a square standstill at centre while the scale settles, steps resume",
    ),
    ActionSpec(
        "MournerWalk", "cemetery_mourner_v1", 1.5, 36,
        "bowed mourning stance, both hands cradling the bouquet at the chest",
        "short heavy grieving steps with no arm swing",
    ),
    # The phase boundaries (lay 0-3.5 s, sob 3.5-33.5 s, wipe to the
    # end) must match CemeteryMournerTimeline.Lay/Cry/WipeSeconds: the
    # runtime hides the hand bouquet on the lay cue and walks her out
    # exactly when this rite completes its single playback.
    ActionSpec(
        "MournerMourn", "cemetery_mourner_v1", 36.5, 876,
        "graveside grief from a standing bow",
        "lay the bouquet low, thirty seconds of shoulder-shaking sobs behind raised hands, wipe each eye and straighten",
    ),
    ActionSpec(
        "WatchmanWatch", "cemetery_watchman_v1", 6.0, 144,
        "rounded old shoulders, chin up under the cap, hands clasped behind the back",
        "slow weight transfers, a disapproving head shake and one smug chin jut with a shrug",
    ),
    ActionSpec(
        "WatchmanShuffle", "cemetery_watchman_v1", 1.5, 36,
        "rounded old shoulders, chin up under the cap, hands clasped behind the back",
        "short heavy shuffling steps with no arm swing and a mild capped-head bob",
    ),
    # Four breaths per lap, on an exact quarter-loop grid. The number
    # is a contract, not a rhythm choice: it must match
    # LakeFishermanPresentation.BreathsPerLoop, because the pipe ember
    # and the plume are driven from the clip's own normalized time
    # rather than from a second free-running timer. Anything that
    # re-times this loop has to re-time that constant with it or the
    # smoke stops belonging to the chest.
    ActionSpec(
        "FishermanLean", "lake_fisherman_v1", 8.0, 192,
        "tipped forward over the end board, weight on the front foot, both hands on the rod",
        "four slow breaths under the hood, one rod correction and a look down the line",
    ),
    ActionSpec(
        "FishermanTrudge", "lake_fisherman_v1", 1.5, 36,
        "hooded oilskin stance with the rod carried forward in both hands",
        "short heavy steps in waders with almost no arm swing",
    ),
    # The brooding loop is long on purpose. Nothing reads its phase, so
    # the grid is regular rather than contractual, but twelve seconds is
    # what it takes for a man who is not doing anything to stop looking
    # like a looping animation.
    # The Ferryman. Four seconds and one toss: the loop is short because
    # the toss is the content, and a longer wait between throws reads as
    # a man who has stopped doing the thing rather than one who keeps
    # doing it. The release and catch phases below are a CONTRACT - the
    # runtime coin reads them from LastRouteFerrymanPresentation and
    # arcs between them, so re-timing this grid without re-timing those
    # constants detaches the coin from his hand in mid-air.
    ActionSpec(
        "FerrymanWait", "last_route_ferryman_v1", 4.0, 96,
        "perched on the car's bonnet, right hand braced behind the hip, "
        "left forearm up with the palm open",
        "four slow breaths and one coin flicked up and caught, "
        "released at 1/16 of the loop and caught at 5/16",
        seated=True,
        perched=True,
    ),
    ActionSpec(
        "FerrymanTrudge", "last_route_ferryman_v1", 1.5, 36,
        "upright in the long coat with the collar up",
        "slow coat-heavy steps that do not hurry for anybody",
    ),
    # Getting into his own car. Deliberately fast - three quarters of a
    # second from the bonnet to behind the wheel - because the whole
    # point of the beat is that a man who has waited twenty years moves
    # immediately once somebody finally says yes. It opens on the exact
    # base pose of FerrymanWait and closes on the exact base pose of
    # FerrymanDrive, never re-typed numbers, so the runtime can cross
    # from the wait into this and out into the drive without a seam.
    ActionSpec(
        "FerrymanBoard", "last_route_ferryman_v1", 0.75, 18,
        "off the bonnet, one push from the bracing hand, into the seat",
        "a hard shove down off the metal, a half turn, and a drop into "
        "the driver's seat with both hands arriving on the wheel",
        seated=True,
        leaves_seat=True,
        one_shot=True,
    ),
    ActionSpec(
        "FerrymanDrive", "last_route_ferryman_v1", 3.0, 72,
        "seated at the wheel, both hands on the rim, chin level",
        "a settled seated breath and nothing else; he is waiting again, "
        "only now he is waiting inside the car",
        seated=True,
        # Carried by the car's cabin rather than by its bonnet, which is
        # why the archetype declares two bands and this clip does not say
        # `perched`. It is measured: a driver whose cap clears his seated
        # pelvis by more than the roof allows is wearing the roof, and
        # this is the only clip that would show it.
    ),
    ActionSpec(
        "ChessBrood", "park_chess_player_v1", 12.0, 288,
        "perched on the bench plank, both elbows on the board rim, the head sunk into both hands",
        "three slow breaths carried by the ribs alone and one deeper settle",
        seated=True,
        perched=True,
    ),
    ActionSpec(
        "ChessTrudge", "park_chess_player_v1", 1.5, 36,
        "stooped old stance with both hands buried in the overcoat pockets",
        "short flat park steps with the shoulders carried ahead of the hips",
    ),
    # And the thing he actually does. Two seconds, because a shout is
    # not a pose and the whole reason it reads is that it is over before
    # the brooding has visibly resumed. It opens and closes on the exact
    # base pose of ChessBrood - `chess_player_base_pose()` at both ends,
    # never re-typed numbers - so the runtime mixer can cross into it and
    # back out over a tenth of a second without a seam, and the loop
    # validator reads zero error on a clip that is not really a loop.
    #
    # Only the left arm leaves the board; the right elbow stays on the
    # rim throughout. The head does not get lifted out of the hands so
    # much as it turns out from under them, which is the difference
    # between a man standing up to argue and a man who has been having
    # this argument sitting down for eleven years.
    ActionSpec(
        "ChessJeer", "park_chess_player_v1", 2.0, 48,
        "perched on the plank, right elbow still on the board rim, "
        "left arm thrown up across the set",
        "head turned out from under the hands to the neighbour, a hard "
        "left-arm throw, the accusation held, and a collapse back into "
        "the palms slower than the throw was",
        seated=True,
    ),
    # The draughts player rides the chess player's posture exactly - the
    # board, the plank and the skull are the same, so the solve is - but
    # he cannot ride his clips. Clip names are the key of ACTION_BY_NAME
    # and actions are handed to a design by `design_id`, so a shared
    # name would either leave this archetype with nothing baked or
    # overwrite the neighbour's entry. His own pair also earns something
    # the sharing would have cost: the perch validator runs all 288
    # frames against his meshes rather than somebody else's.
    #
    # The settle lands at a different point in the loop and breathes a
    # little shallower. Two men under one lamp must not rise and fall
    # together, and separate rhythms hold where a phase offset gives
    # itself away to anyone who stands between them long enough.
    ActionSpec(
        "CheckersMull", "park_checkers_player_v1", 12.0, 288,
        "perched on the bench plank, both elbows on the board rim, the head sunk into both hands",
        "three shallow breaths carried by the ribs alone and one early settle",
        seated=True,
        perched=True,
    ),
    ActionSpec(
        "CheckersTrudge", "park_checkers_player_v1", 1.5, 36,
        "stooped old stance with both hands buried in the overcoat pockets",
        "short flat park steps with the shoulders carried ahead of the hips",
    ),
    # His answer: the same two seconds and, unmirrored, the same pose,
    # because they have been doing this for years and neither has
    # learned anything from it. The two seats are a 180-degree rotation
    # of each other about the middle of the set, which puts each man's
    # neighbour over the same shoulder - so the same body-relative turn
    # sends each of them at the other. See `park_jeer` for the working.
    ActionSpec(
        "CheckersJeer", "park_checkers_player_v1", 2.0, 48,
        "perched on the plank, right elbow still on the board rim, "
        "left arm thrown up back across the set",
        "head turned out from under the hands to the neighbour, a hard "
        "left-arm throw, the accusation held, and a collapse back into "
        "the palms slower than the throw was",
        seated=True,
    ),
)


SEATED_LEGS = {
    "pelvis": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
    "thigh.L": BonePose(rotation_degrees=(-79.0, 0.0, 3.0)),
    "shin.L": BonePose(rotation_degrees=(83.0, 0.0, 0.0)),
    "foot.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
    "thigh.R": BonePose(rotation_degrees=(-79.0, 0.0, -3.0)),
    "shin.R": BonePose(rotation_degrees=(83.0, 0.0, 0.0)),
    "foot.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
}


def seated_pose(base: dict[str, BonePose], *overrides: dict[str, BonePose]):
    """One seated leg shape over each design's own authored upper body.

    Every walker shares the hero's rig, so the knees and hips are identical
    across designs; what stays per-design is the posture the walker is known
    for, which is exactly what the base pose already carries.
    """

    return merge_pose(base, SEATED_LEGS, *overrides)


def merge_pose(
    base: dict[str, BonePose],
    *overrides: dict[str, BonePose],
) -> dict[str, BonePose]:
    merged = dict(base)
    for override in overrides:
        merged.update(override)
    return merged


def interpolate_pose(
    start: dict[str, BonePose],
    end: dict[str, BonePose],
    amount: float,
) -> dict[str, BonePose]:
    """Linear source-pose sampling for deterministic clearance validation."""

    amount = max(0.0, min(1.0, amount))
    sampled: dict[str, BonePose] = {}
    for name in set(start) | set(end):
        first = start.get(name, BonePose())
        second = end.get(name, BonePose())
        sampled[name] = BonePose(
            rotation_degrees=tuple(
                a + (b - a) * amount
                for a, b in zip(first.rotation_degrees, second.rotation_degrees)
            ),
            location_m=tuple(
                a + (b - a) * amount
                for a, b in zip(first.location_m, second.location_m)
            ),
            scale=tuple(
                a + (b - a) * amount
                for a, b in zip(first.scale, second.scale)
            ),
        )
    return sampled


def reset_pose(rig: bpy.types.Object) -> None:
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.location = (0.0, 0.0, 0.0)
        pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        pose_bone.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()


def apply_pose(rig: bpy.types.Object, pose: dict[str, BonePose]) -> None:
    for bone_name, transform in pose.items():
        pose_bone = rig.pose.bones.get(bone_name)
        if pose_bone is None:
            raise ValueError(f"Unknown animation bone {bone_name}")
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.location = transform.location_m
        pose_bone.rotation_quaternion = Euler(
            tuple(math.radians(value) for value in transform.rotation_degrees), "XYZ"
        ).to_quaternion()
        pose_bone.scale = transform.scale
    bpy.context.view_layer.update()


def iter_action_fcurves(action: bpy.types.Action):
    legacy_curves = getattr(action, "fcurves", None)
    if legacy_curves is not None:
        yield from legacy_curves
        return
    for layer in action.layers:
        for strip in layer.strips:
            for channelbag in getattr(strip, "channelbags", ()):
                yield from channelbag.fcurves


def create_action(
    rig: bpy.types.Object,
    spec: ActionSpec,
    keys: Sequence[tuple[float, dict[str, BonePose]]],
) -> bpy.types.Action:
    if not keys or keys[0][0] != 0.0 or keys[-1][0] != 1.0:
        raise ValueError(f"Action {spec.name} must own normalized 0 and 1 endpoints")
    action = bpy.data.actions.new(spec.name)
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start = 0.0
    action.frame_end = float(spec.frame_end)
    action.use_cyclic = True
    action["bp_archetype"] = spec.archetype
    action["bp_duration_seconds"] = spec.duration_seconds
    action["bp_loop"] = True
    action["bp_in_place"] = True
    action["bp_root_motion"] = False
    action["bp_authored_posture"] = spec.authored_posture
    action["bp_gait"] = spec.gait
    action["bp_generator_version"] = GENERATOR_VERSION
    animation_data = rig.animation_data_create()
    animation_data.action = action
    previous_quaternions: dict[str, Quaternion] = {}
    for normalized_time, pose in keys:
        reset_pose(rig)
        apply_pose(rig, pose)
        frame = round(spec.frame_end * normalized_time)
        for bone in rig.pose.bones:
            quaternion = bone.rotation_quaternion.copy()
            previous = previous_quaternions.get(bone.name)
            if previous is not None:
                quaternion.make_compatible(previous)
                bone.rotation_quaternion = quaternion
            previous_quaternions[bone.name] = quaternion.copy()
            group = bone.name.split(".")[0]
            bone.keyframe_insert("location", frame=frame, group=group)
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=group)
            bone.keyframe_insert("scale", frame=frame, group=group)
    for curve in iter_action_fcurves(action):
        for keyframe in curve.keyframe_points:
            keyframe.interpolation = "BEZIER"
            keyframe.handle_left_type = "AUTO_CLAMPED"
            keyframe.handle_right_type = "AUTO_CLAMPED"
    animation_data.action = None
    reset_pose(rig)
    return action


def offset_pipeback_hands(
    pose: dict[str, BonePose],
    left_x: float,
    right_x: float,
    y: float,
    z: float,
) -> dict[str, BonePose]:
    """Document a rim target while preserving the connected Player hands.

    The exact Player rig ignores translation on its connected hand bones, so
    the reachable rim path is authored with the shoulder/forearm rotations.
    Keeping the target in this helper makes that deliberate limitation visible
    beside the pose instead of looking like an omitted IK pass.
    """

    _ = left_x, right_x, y, z
    return dict(pose)


def lampshade_base_pose() -> dict[str, BonePose]:
    return {
        "pelvis": BonePose(rotation_degrees=(10.0, 0.0, -2.0), location_m=(0, 0.025, -0.055)),
        "spine": BonePose(rotation_degrees=(18.0, 0.0, 3.0)),
        "chest": BonePose(rotation_degrees=(13.0, 0.0, -4.0)),
        "neck": BonePose(rotation_degrees=(-15.0, 0.0, 2.0)),
        "head": BonePose(rotation_degrees=(8.0, 0.0, -2.0)),
        "clavicle.L": BonePose(rotation_degrees=(3.0, -4.0, 8.0)),
        "clavicle.R": BonePose(rotation_degrees=(3.0, 4.0, -8.0)),
        "upper_arm.L": BonePose(rotation_degrees=(13.0, 10.0, 26.0)),
        "upper_arm.R": BonePose(rotation_degrees=(11.0, -8.0, -25.0)),
        "forearm.L": BonePose(rotation_degrees=(-18.0, 4.0, -8.0)),
        "forearm.R": BonePose(rotation_degrees=(-15.0, -3.0, 7.0)),
        "thigh.L": BonePose(rotation_degrees=(-9.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(19.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-6.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    }


def chair_base_pose() -> dict[str, BonePose]:
    return {
        "pelvis": BonePose(rotation_degrees=(-1.5, 0.0, 0.0), location_m=(0, 0, -0.015)),
        "spine": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-1.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(1.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(0.0, -4.0, 4.0)),
        "clavicle.R": BonePose(rotation_degrees=(0.0, 4.0, -4.0)),
        "upper_arm.L": BonePose(rotation_degrees=(16.0, 8.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(16.0, -8.0, -30.0)),
        "forearm.L": BonePose(rotation_degrees=(-58.0, 4.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(-58.0, -4.0, 18.0)),
        "hand.L": BonePose(rotation_degrees=(8.0, -5.0, 3.0)),
        "hand.R": BonePose(rotation_degrees=(8.0, 5.0, -3.0)),
        "thigh.L": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
    }


def kettle_base_pose() -> dict[str, BonePose]:
    """Low stout stance: pelvis tipped back under the belly, head sunk.

    The arms are pushed outward by the body mass rather than swung, and both
    knees stay softly loaded so the walk can stay short and fast.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(-7.0, 0.0, 0.0), location_m=(0, 0.010, -0.030)),
        "spine": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-11.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(-4.0, -3.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(-4.0, 3.0, -6.0)),
        "upper_arm.L": BonePose(rotation_degrees=(9.0, 7.0, 34.0)),
        "upper_arm.R": BonePose(rotation_degrees=(9.0, -7.0, -34.0)),
        "forearm.L": BonePose(rotation_degrees=(-26.0, 5.0, -14.0)),
        "forearm.R": BonePose(rotation_degrees=(-26.0, -5.0, 14.0)),
        "hand.L": BonePose(rotation_degrees=(6.0, -4.0, 2.0)),
        "hand.R": BonePose(rotation_degrees=(6.0, 4.0, -2.0)),
        "thigh.L": BonePose(rotation_degrees=(-4.0, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-4.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
    }


def long_arm_base_pose() -> dict[str, BonePose]:
    """Narrow hanging stance with the shoulders pulled up around the head.

    The arms are brought down out of the A-pose on the upper arm, then the
    forearm counter-rotates so the long hanging segment ends near vertical
    beside the thigh rather than swinging across the body.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0.004, -0.008)),
        "spine": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -2.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 2.0, -6.0)),
        "upper_arm.L": BonePose(rotation_degrees=(0.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(0.0, -4.0, -30.0)),
        "forearm.L": BonePose(rotation_degrees=(0.0, 0.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(0.0, 0.0, 18.0)),
        "thigh.L": BonePose(rotation_degrees=(-2.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-2.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
    }


def helmet_lamp_base_pose() -> dict[str, BonePose]:
    """Coiled two-footed crouch with the forearms tucked like forepaws.

    Both legs stay symmetrical: this walker never takes an alternating step,
    so there is no left/right phase anywhere in its clips.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(10.0, 0.0, 0.0), location_m=(0, 0.020, -0.205)),
        "spine": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -3.0, 7.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 3.0, -7.0)),
        "upper_arm.L": BonePose(rotation_degrees=(18.0, 8.0, 42.0)),
        "upper_arm.R": BonePose(rotation_degrees=(18.0, -8.0, -42.0)),
        "forearm.L": BonePose(rotation_degrees=(-76.0, 6.0, -14.0)),
        "forearm.R": BonePose(rotation_degrees=(-76.0, -6.0, 14.0)),
        "hand.L": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
        "hand.R": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-58.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(88.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-32.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-58.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(88.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-32.0, 0.0, 0.0)),
    }


def pipeback_base_pose() -> dict[str, BonePose]:
    """Stable manual-chair posture with the hands resting on both rims."""

    return offset_pipeback_hands({
        "pelvis": BonePose(
            rotation_degrees=(0.0, 0.0, 0.0),
            location_m=(0.0, 0.0, 0.0),
        ),
        "spine": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -4.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 4.0, -6.0)),
        "upper_arm.L": BonePose(rotation_degrees=(38.0, 8.0, 38.0)),
        "upper_arm.R": BonePose(rotation_degrees=(38.0, -8.0, -38.0)),
        "forearm.L": BonePose(rotation_degrees=(-96.0, 4.0, -20.0)),
        "forearm.R": BonePose(rotation_degrees=(-96.0, -4.0, 20.0)),
        "hand.L": BonePose(rotation_degrees=(-12.0, -4.0, 4.0)),
        "hand.R": BonePose(rotation_degrees=(-12.0, 4.0, -4.0)),
        "thigh.L": BonePose(rotation_degrees=(-79.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(83.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-79.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(83.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
    }, 0.345, -0.345, 0.090, 0.515)


def babushka_base_pose() -> dict[str, BonePose]:
    """Hunched standing stance shared by the beating and smoking loops.

    Both loops keep the feet planted, so the ordinary walker sole bake
    grounds them; the age lives in the rounded spine, the sunk neck and
    the slightly bent knees.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(6.0, 0.0, 0.0), location_m=(0, 0.012, -0.045)),
        "spine": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -3.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 3.0, -6.0)),
        "upper_arm.L": BonePose(rotation_degrees=(10.0, 6.0, 34.0)),
        "upper_arm.R": BonePose(rotation_degrees=(10.0, -6.0, -34.0)),
        "forearm.L": BonePose(rotation_degrees=(-24.0, 4.0, -10.0)),
        "forearm.R": BonePose(rotation_degrees=(-24.0, -4.0, 10.0)),
        "hand.L": BonePose(rotation_degrees=(4.0, -3.0, 2.0)),
        "hand.R": BonePose(rotation_degrees=(4.0, 3.0, -2.0)),
        "thigh.L": BonePose(rotation_degrees=(-5.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-5.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    }


def weigh_attendant_base_pose() -> dict[str, BonePose]:
    """Upright but tired working stance shared by both weighbridge loops.

    Less hunched than the babushka — a worker mid-shift, not an
    elder — with the heavy arms hanging at the sides of the quilted
    jacket and both feet planted for the ordinary sole bake.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(3.0, 0.0, 0.0), location_m=(0, 0.008, -0.030)),
        "spine": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(1.0, -2.0, 4.0)),
        "clavicle.R": BonePose(rotation_degrees=(1.0, 2.0, -4.0)),
        "upper_arm.L": BonePose(rotation_degrees=(8.0, 5.0, 40.0)),
        "upper_arm.R": BonePose(rotation_degrees=(8.0, -5.0, -40.0)),
        "forearm.L": BonePose(rotation_degrees=(-16.0, 3.0, -8.0)),
        "forearm.R": BonePose(rotation_degrees=(-16.0, -3.0, 8.0)),
        "hand.L": BonePose(rotation_degrees=(3.0, -2.0, 2.0)),
        "hand.R": BonePose(rotation_degrees=(3.0, 2.0, -2.0)),
        "thigh.L": BonePose(rotation_degrees=(-3.0, 0.0, 2.0)),
        "shin.L": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-3.0, 0.0, -2.0)),
        "shin.R": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
    }


def mourner_base_pose() -> dict[str, BonePose]:
    """Bowed mourning stance shared by the walk and the graveside rite.

    Both clips keep the feet planted, so the ordinary walker sole bake
    grounds them; the grief lives in the sunk head, the rounded back
    and the two forearms folded up to the chest around the bouquet.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(7.0, 0.0, 0.0), location_m=(0, 0.010, -0.050)),
        "spine": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -3.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 3.0, -6.0)),
        "upper_arm.L": BonePose(rotation_degrees=(16.0, 8.0, 22.0)),
        "upper_arm.R": BonePose(rotation_degrees=(16.0, -8.0, -22.0)),
        "forearm.L": BonePose(rotation_degrees=(-88.0, 8.0, -14.0)),
        "forearm.R": BonePose(rotation_degrees=(-88.0, -8.0, 14.0)),
        "hand.L": BonePose(rotation_degrees=(-6.0, -4.0, 4.0)),
        "hand.R": BonePose(rotation_degrees=(-6.0, 4.0, -4.0)),
        "thigh.L": BonePose(rotation_degrees=(-5.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-5.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    }


def watchman_base_pose() -> dict[str, BonePose]:
    """Snide watch stance shared by the watch and shuffle loops.

    Both loops keep the feet planted, so the ordinary walker sole bake
    grounds them; the attitude lives in the rounded old shoulders, the
    chin carried up under the cap visor and both hands clasped behind
    the back — the eternal courtyard inspector.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(4.0, 0.0, 0.0), location_m=(0, 0.006, -0.035)),
        "spine": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(1.0, -4.0, 5.0)),
        "clavicle.R": BonePose(rotation_degrees=(1.0, 4.0, -5.0)),
        # Hands clasped behind the back: the arms swing rearward and
        # the forearms fold in toward the lumbar.
        "upper_arm.L": BonePose(rotation_degrees=(-20.0, 12.0, 44.0)),
        "upper_arm.R": BonePose(rotation_degrees=(-20.0, -12.0, -44.0)),
        "forearm.L": BonePose(rotation_degrees=(-36.0, 16.0, -22.0)),
        "forearm.R": BonePose(rotation_degrees=(-36.0, -16.0, 22.0)),
        "hand.L": BonePose(rotation_degrees=(2.0, -6.0, 4.0)),
        "hand.R": BonePose(rotation_degrees=(2.0, 6.0, -4.0)),
        "thigh.L": BonePose(rotation_degrees=(-4.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-4.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
    }


def fisherman_base_pose() -> dict[str, BonePose]:
    """Standing lean over the end board, both fists on the rod.

    He is not resting on the boards but on the parapet: the lean is
    authored from the hips and the ankles together, so his weight goes
    into the board in front of him rather than into a bow. The feet stay
    planted and the ordinary sole bake grounds him like any walker.

    Both hands really are on the stick. Only the right fist carries it -
    the rod is one rigid part on one vertex group - so the left arm has
    to be brought across onto the same line, and the angles below were
    fitted to that line rather than eyeballed: `ACC_RodGrip` and
    `ACC_RodTip` give the axis, and both arms were solved against it.
    Nudging any one of these six bones by eye takes the left hand off
    the rod, which is exactly the failure that looks like a man
    pretending to fish.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(5.0, 0.0, 0.0), location_m=(0, 0.008, -0.032)),
        "spine": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        # Head down, watching the float rather than the far bank.
        "neck": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -4.0, 5.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 4.0, -5.0)),
        "upper_arm.L": BonePose(rotation_degrees=(-83.0, 42.8, 125.5)),
        "forearm.L": BonePose(rotation_degrees=(-73.0, 60.5, -36.8)),
        "hand.L": BonePose(rotation_degrees=(11.2, 21.0, -1.5)),
        "upper_arm.R": BonePose(rotation_degrees=(-47.0, 10.0, 9.0)),
        "forearm.R": BonePose(rotation_degrees=(-118.0, 32.0, -9.2)),
        "hand.R": BonePose(rotation_degrees=(31.8, 22.5, -42.0)),
        # Braced: the forward leg takes the lean, the back one trails.
        "thigh.L": BonePose(rotation_degrees=(-13.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(19.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-5.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    }


def ferryman_base_pose() -> dict[str, BonePose]:
    """Perched on the car's bonnet, braced back on one hand, coin ready.

    The whole design is in this pose, so it is worth saying what each
    half is for. The RIGHT arm is furniture: it goes back and down onto
    the bonnet behind his hip and never moves again in the loop. The
    LEFT arm is the gesture: forearm up, palm open at chest height,
    and it is the only thing that moves apart from his breathing. One
    arm fixed and one arm working is what makes a toss legible from
    across the lot, where a two-handed fidget would read as noise.

    The lean is backwards, not forwards. A man bowed over his knees
    reads as tired; a man tipped back on his supporting hand with his
    chin up reads as someone enjoying the wait, which is the whole
    character. The head is turned very slightly off the coin so that
    from the approach he looks like he is watching the arriving hero
    instead - the shadow under the cap brim does the rest.

    The legs are the measured half, exactly as they are for the chess
    player. His boots rest on the car's front bumper, 0.505 m below the
    bonnet skin he sits on and 0.42 m ahead of it - both numbers come
    from PERCH_SEAT and PERCH_SOLES in
    tools/build-last-route-car-3d-model.py, and `perch_seat_height_m`
    is what pins the drop.
    """

    return {
        # Tipped back off the hips, chest open.
        "pelvis": BonePose(rotation_degrees=(-7.0, 0.0, 0.0), location_m=(0, 0.010, -0.004)),
        "spine": BonePose(rotation_degrees=(-6.0, 2.0, 0.0)),
        "chest": BonePose(rotation_degrees=(-3.0, 3.0, 0.0)),
        # Chin up and turned a few degrees toward whoever is walking up,
        # so the cap brim's shadow still covers the eyes.
        "neck": BonePose(rotation_degrees=(-4.0, -7.0, 0.0)),
        "head": BonePose(rotation_degrees=(-6.0, -6.0, 2.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -4.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(-4.0, 4.0, -10.0)),
        # Left: the coin hand. Elbow in at the ribs, forearm up, palm
        # open and level at chest height where the toss starts.
        "upper_arm.L": BonePose(rotation_degrees=(-150.0, 4.0, 74.0)),
        "forearm.L": BonePose(rotation_degrees=(-78.0, 40.0, 8.0)),
        "hand.L": BonePose(rotation_degrees=(10.0, 20.0, -10.0)),
        # Right: the brace. These three are a MEASURED result, not a
        # posed one. Nothing about this rig's shoulder is guessable -
        # raising |X| lifts the hand rather than lowering it, and Z
        # swings it across the body - so the angles were swept against
        # one target taken off the car: the heel of his hand on the
        # bonnet behind his hip. They land it 0.33 m behind him at
        # 0.79 m, which is that bonnet. Re-posing by eye will move it
        # off the metal and he will be bracing on air.
        "upper_arm.R": BonePose(rotation_degrees=(-20.0, -10.0, -70.0)),
        "forearm.R": BonePose(rotation_degrees=(-8.0, -6.0, -4.0)),
        "hand.R": BonePose(rotation_degrees=(-16.0, -6.0, 6.0)),
        # Legs hang down the nose of the car onto the bumper. The two
        # are deliberately different: the left boot is planted, the
        # right hangs looser and turned out, which is the difference
        # between sitting on a car and being posed on one.
        # Solved, not posed: swept against the car's own 0.505 m from
        # bonnet skin to bumper top and landing on 0.5077. Thigh angle
        # is what moves this number; bending the shin further barely
        # touches it, which is not obvious and cost a sweep to learn.
        "thigh.L": BonePose(rotation_degrees=(-62.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(42.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-56.0, 0.0, -8.0)),
        "shin.R": BonePose(rotation_degrees=(38.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(6.0, 0.0, -6.0)),
    }


def ferryman_stand_pose() -> dict[str, BonePose]:
    """The standing stance his walk is built on, for the trudge slot."""

    return {
        "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "spine": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -3.0, 5.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 3.0, -5.0)),
        "upper_arm.L": BonePose(rotation_degrees=(-8.0, 0.0, 62.0)),
        "upper_arm.R": BonePose(rotation_degrees=(-8.0, 0.0, -62.0)),
        "forearm.L": BonePose(rotation_degrees=(-16.0, 10.0, 4.0)),
        "forearm.R": BonePose(rotation_degrees=(-16.0, -10.0, -4.0)),
        "hand.L": BonePose(rotation_degrees=(4.0, 6.0, -4.0)),
        "hand.R": BonePose(rotation_degrees=(4.0, -6.0, 4.0)),
    }


def ferryman_drive_pose() -> dict[str, BonePose]:
    """Behind the wheel of his own car, waiting again.

    A cabin seat, not a bonnet, and a small car's cabin rather than the
    bus's: both halves of this pose are measured against
    tools/build-last-route-car-3d-model.py rather than eyeballed, and
    both were wrong when they were.

    Under him there is far less room than a bus gives. FLOOR_Z 0.30 sits
    only 0.22 m below SEAT_PELVIS_Z 0.52, where Route 01 allows 0.41, so
    a passenger's hanging shins put his boots a fifth of a metre through
    the floor pan. A driver does not hang his legs anyway - he reaches
    them forward onto the pedals - and that is what the angles below do:
    the thighs run slightly above level and the shins slope forward, for
    0.2197 m of leg under the pelvis against the 0.22 m there is.

    Over him there is less room too. ROOF_UNDERSIDE_Z 1.56 leaves 1.04 m
    of head, and sitting up straight he measures 1.0430 m to the crown of
    his cap - three millimetres of roof, worn. So he sits into the seat
    rather than on it, which is what a man who has waited twenty years
    does regardless, and clears it by twelve.

    Both hands come onto the rim - the bracing arm has nothing to brace
    on any more, and a driver with one hand down reads as parked rather
    than as about to leave.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "spine": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(3.0, -3.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(3.0, 3.0, -6.0)),
        # Both forearms up and in, hands meeting on the rim in front of
        # the chest. Mirrored exactly: a wheel is symmetric and so is
        # the grip on it.
        "upper_arm.L": BonePose(rotation_degrees=(-148.0, 4.0, 72.0)),
        "upper_arm.R": BonePose(rotation_degrees=(-148.0, -4.0, -72.0)),
        "forearm.L": BonePose(rotation_degrees=(-86.0, 42.0, 8.0)),
        "forearm.R": BonePose(rotation_degrees=(-86.0, -42.0, -8.0)),
        "hand.L": BonePose(rotation_degrees=(8.0, 24.0, -14.0)),
        "hand.R": BonePose(rotation_degrees=(8.0, -24.0, 14.0)),
        # Driving legs: thighs a little above level and the shins reaching
        # forward at the pedals, not hanging. Converged against the car's
        # 0.22 m of floor rather than chosen - see the docstring.
        "thigh.L": BonePose(rotation_degrees=(-86.0, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-86.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
    }


def chess_player_base_pose() -> dict[str, BonePose]:
    """Perched on the plank, elbows on the board rim, head in both hands.

    The legs are the measured half of this pose. The chess bench draws
    its plank 0.54 m over the lawn, which is high for a seat, so the
    thighs slope down rather than run level the way the bus cabin's do:
    a cabin-seated leg shape would hang his boots well clear of the
    grass. `perch_seat_height_m` is what pins that, and the angles below
    were converged against it rather than set by eye.

    The two legs are deliberately different, and that is a fix rather
    than a flourish. The shared Player rig is asymmetric on purpose
    (`toe.L` at -0.230, `toe.R` at -0.188), so identical left and right
    angles land the two soles about 32 mm apart and no single pelvis
    channel can level them. Giving one foot the flat plant and drawing
    the other back onto its toe makes one sole the contact, hides the
    difference in a raised heel, and reads as a bored man besides.
    """

    return {
        # The lean is not a mood setting, it is what lets him reach. A
        # seated shoulder is already at its height ceiling in the A-pose
        # rest, and with the torso upright the board is more than a
        # forearm below the jaw: he physically cannot prop his head with
        # his elbows down there. Folding the spine brings the head into
        # reach, exactly as it does for a real person.
        "pelvis": BonePose(rotation_degrees=(8.0, 0.0, 0.0), location_m=(0, 0.004, -0.010)),
        "spine": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
        # And the neck takes it back. Folding 42 degrees at the chest
        # without this would carry the crown over with it: a king's
        # tulle laid on its side stops being a chess piece and becomes a
        # bottle, and the face - the only content this design has -
        # ends up pointed at the grass. Countering here leaves the crown
        # 22 degrees off vertical, which reads as a bowed head, and
        # keeps the face up where somebody walking past can see it.
        "neck": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        # Old shoulders, pulled up and forward around the ears.
        "clavicle.L": BonePose(rotation_degrees=(4.0, -7.0, 9.0)),
        "clavicle.R": BonePose(rotation_degrees=(4.0, 7.0, -9.0)),
        # The six arm angles are a fitted result rather than a posed one,
        # for the same reason the fisherman's were: this reach does not
        # converge by eye. They were solved by coordinate descent against
        # two measurements taken off the drawn chess set - an elbow
        # resting on the board surface, and a palm under the jaw - to
        # 0.1 mm at the elbow and 0.2 mm at the wrist. The right side is
        # the left's own answer mirrored: the solver will happily find an
        # equally exact but visibly different wrist roll, and an old man
        # may be asymmetric by a few degrees but not by one hand turned
        # over.
        #
        # Where the elbow lands is derived, not chosen. Given the seated
        # shoulder and a 0.2869 m upper arm there is exactly one distance
        # forward at which an elbow reaches the board without shortening
        # the arm, and it puts them on the squares rather than on the
        # slab edge. Which is where a man brooding over a position
        # nobody is playing would have them anyway.
        "upper_arm.L": BonePose(rotation_degrees=(-157.8, 1.0, 80.0)),
        "upper_arm.R": BonePose(rotation_degrees=(-157.8, -1.0, -80.0)),
        "forearm.L": BonePose(rotation_degrees=(-115.6, 46.8, 11.0)),
        "forearm.R": BonePose(rotation_degrees=(-115.6, -46.8, -11.0)),
        "hand.L": BonePose(rotation_degrees=(14.0, 18.0, -8.0)),
        "hand.R": BonePose(rotation_degrees=(14.0, -18.0, 8.0)),
        # Left foot flat and forward - this is the grounding contact, and
        # the pair of angles that `perch_seat_height_m` converged on.
        "thigh.L": BonePose(rotation_degrees=(-47.3, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(47.3, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
        # Right foot drawn back under the plank, heel up. The knee bends
        # rather than the hip, which is how a foot actually gets tucked:
        # folding the shin swings the ankle back AND up, so this leg
        # cannot steal the ground contact from the left one.
        "thigh.R": BonePose(rotation_degrees=(-70.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(105.5, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
    }


def chess_player_stand_pose() -> dict[str, BonePose]:
    """The stooped stance the trudge is built on.

    Standing, so the ordinary walker sole bake grounds him like anyone
    else. Both hands go into the coat pockets, which keeps the arms out
    of the way of a design whose whole silhouette lives above the neck.
    """

    return {
        "pelvis": BonePose(rotation_degrees=(6.0, 0.0, 0.0), location_m=(0, 0.006, -0.030)),
        "spine": BonePose(rotation_degrees=(13.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(3.0, -6.0, 7.0)),
        "clavicle.R": BonePose(rotation_degrees=(3.0, 6.0, -7.0)),
        "upper_arm.L": BonePose(rotation_degrees=(-24.0, 6.0, 54.0)),
        "upper_arm.R": BonePose(rotation_degrees=(-24.0, -6.0, -54.0)),
        "forearm.L": BonePose(rotation_degrees=(-34.0, 12.0, -14.0)),
        "forearm.R": BonePose(rotation_degrees=(-34.0, -12.0, 14.0)),
        "hand.L": BonePose(rotation_degrees=(6.0, 8.0, -4.0)),
        "hand.R": BonePose(rotation_degrees=(6.0, -8.0, 4.0)),
        "thigh.L": BonePose(rotation_degrees=(-5.0, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-5.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
    }


def checkers_player_base_pose() -> dict[str, BonePose]:
    """The chess player's perch, unchanged, at the other table.

    Deliberately not a second solve. The six arm angles it inherits were
    converged by coordinate descent against an elbow on the board and a
    palm under the cheek, and every quantity that solve depends on - the
    board top at `0.90`, the plank at `0.54`, the shoulder, the length
    of the upper arm and the skull the hand reaches for - is identical
    at both tables and on both bodies, because the geometry was authored
    identical for exactly this reason. Re-fitting it could only find the
    same answer or a worse one.

    The mirror the design asks for is the seat, not the skeleton: the
    runtime sits him on the seat that is this one rotated 180 degrees
    about the middle of the set, and that is what turns the two men to
    face each other.
    """

    return chess_player_base_pose()


def checkers_player_stand_pose() -> dict[str, BonePose]:
    """The same stooped, hands-in-pockets stance the trudge is built on."""

    return chess_player_stand_pose()


# Which posture a perched design is previewed in. `perch_seat_height_m`
# says a model is seated on world timber; it does not say how, and two
# designs now declare it.
PERCH_PREVIEW_POSES = {
    "last_route_ferryman": ferryman_base_pose,
    "park_chess_player": chess_player_base_pose,
    "park_checkers_player": checkers_player_base_pose,
}


def ferry_breath(base: dict[str, BonePose], amount: float) -> dict[str, BonePose]:
    """One breath carried by the ribs, the fisherman's own idiom.

    The chest lifts and the shoulders follow it a little; nothing else
    moves. On a man sitting still this is the only thing that keeps the
    loop from reading as a freeze frame.
    """

    return merge_pose(
        base,
        {
            "chest": BonePose(
                rotation_degrees=(-3.0 - 1.8 * amount, 3.0, 0.0)
            ),
            "clavicle.L": BonePose(
                rotation_degrees=(2.0 + 1.4 * amount, -4.0, 6.0)
            ),
            "clavicle.R": BonePose(
                rotation_degrees=(-4.0 + 1.4 * amount, 4.0, -10.0)
            ),
        },
    )


def animation_keys() -> dict[str, tuple[tuple[float, dict[str, BonePose]], ...]]:
    lampshade = lampshade_base_pose()
    chair = chair_base_pose()
    kettle = kettle_base_pose()
    long_arm = long_arm_base_pose()
    helmet = helmet_lamp_base_pose()
    pipeback = pipeback_base_pose()
    lamp_idle_left = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(11.0, 1.0, -4.0), location_m=(0, 0.025, -0.058)),
        "spine": BonePose(rotation_degrees=(19.5, 0.0, 4.5)),
        "chest": BonePose(rotation_degrees=(14.0, 0.0, -5.5)),
        "head": BonePose(rotation_degrees=(9.0, 0.0, -3.0)),
    })
    lamp_idle_right = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(9.0, -1.0, 1.0), location_m=(0, 0.025, -0.052)),
        "spine": BonePose(rotation_degrees=(17.0, 0.0, 1.5)),
        "chest": BonePose(rotation_degrees=(12.0, 0.0, -1.5)),
        "head": BonePose(rotation_degrees=(7.0, 0.0, 0.5)),
    })
    lamp_left_contact = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(12.0, 4.0, -4.5), location_m=(0, 0.025, -0.075)),
        "thigh.L": BonePose(rotation_degrees=(-22.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(13.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(32.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(15.0, -2.0, -5.0)),
    })
    lamp_right_pass = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(9.0, -1.0, -0.5), location_m=(0, 0.025, -0.045)),
        "thigh.L": BonePose(rotation_degrees=(3.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(15.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-7.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-11.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(48.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(13.0, 0.0, 0.0)),
    })
    lamp_right_contact = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(9.0, -2.0, 2.5), location_m=(0, 0.025, -0.050)),
        "thigh.L": BonePose(rotation_degrees=(13.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(40.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-16.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
    })
    lamp_left_drag = merge_pose(lampshade, {
        "pelvis": BonePose(rotation_degrees=(13.0, 2.0, -1.5), location_m=(0, 0.025, -0.082)),
        "thigh.L": BonePose(rotation_degrees=(-8.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(34.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(5.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    })
    chair_idle_left = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-1.0, 1.0, -1.5), location_m=(0, 0, -0.018)),
        "chest": BonePose(rotation_degrees=(2.0, -0.5, 1.2)),
        "head": BonePose(rotation_degrees=(0.5, 0.0, -0.7)),
    })
    chair_idle_right = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-2.0, -1.0, 1.5), location_m=(0, 0, -0.012)),
        "chest": BonePose(rotation_degrees=(4.0, 0.5, -1.2)),
        "head": BonePose(rotation_degrees=(1.5, 0.0, 0.7)),
    })
    chair_left_contact = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-1.0, 2.0, -1.0), location_m=(0, 0, -0.028)),
        "thigh.L": BonePose(rotation_degrees=(-32.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(4.0, -1.0, 1.0)),
    })
    chair_right_pass = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-2.0, -1.0, 0.5), location_m=(0, 0, 0.004)),
        "thigh.L": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-25.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(60.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
    })
    chair_right_contact = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-1.0, -2.0, 1.0), location_m=(0, 0, -0.028)),
        "thigh.L": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-32.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(4.0, 1.0, -1.0)),
    })
    chair_left_pass = merge_pose(chair, {
        "pelvis": BonePose(rotation_degrees=(-2.0, 1.0, -0.5), location_m=(0, 0, 0.004)),
        "thigh.L": BonePose(rotation_degrees=(-25.0, 0.0, 0.0)),
        "shin.L": BonePose(rotation_degrees=(60.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
    })
    # The kettle walker keeps the belly on the pelvis and the kettle on the
    # head, then rolls them against each other: the head's local Y first
    # cancels the inherited pelvis roll and then overshoots the other way.
    kettle_idle_left = merge_pose(kettle, {
        "pelvis": BonePose(rotation_degrees=(-6.0, 3.0, -1.0), location_m=(0, 0.010, -0.034)),
        "chest": BonePose(rotation_degrees=(5.0, -1.0, 0.0)),
        "head": BonePose(rotation_degrees=(6.0, -5.0, 0.0)),
    })
    kettle_idle_right = merge_pose(kettle, {
        "pelvis": BonePose(rotation_degrees=(-8.0, -3.0, 1.0), location_m=(0, 0.010, -0.026)),
        "chest": BonePose(rotation_degrees=(3.0, 1.0, 0.0)),
        "head": BonePose(rotation_degrees=(6.0, 5.0, 0.0)),
    })
    kettle_left_contact = merge_pose(kettle, {
        "pelvis": BonePose(rotation_degrees=(-6.0, 6.0, -2.0), location_m=(0, 0.010, -0.040)),
        "chest": BonePose(rotation_degrees=(5.0, -2.0, 0.0)),
        "head": BonePose(rotation_degrees=(7.0, -9.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-16.0, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(11.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-9.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(13.0, 7.0, 33.0)),
        "upper_arm.R": BonePose(rotation_degrees=(5.0, -7.0, -35.0)),
    })
    kettle_right_pass = merge_pose(kettle, {
        "pelvis": BonePose(rotation_degrees=(-7.0, 0.0, 1.0), location_m=(0, 0.010, -0.022)),
        "head": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(4.0, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-9.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
    })
    kettle_right_contact = merge_pose(kettle, {
        "pelvis": BonePose(rotation_degrees=(-6.0, -6.0, 2.0), location_m=(0, 0.010, -0.040)),
        "chest": BonePose(rotation_degrees=(5.0, 2.0, 0.0)),
        "head": BonePose(rotation_degrees=(7.0, 9.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(11.0, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-9.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-16.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(5.0, 7.0, 35.0)),
        "upper_arm.R": BonePose(rotation_degrees=(13.0, -7.0, -33.0)),
    })
    kettle_left_pass = merge_pose(kettle, {
        "pelvis": BonePose(rotation_degrees=(-7.0, 0.0, -1.0), location_m=(0, 0.010, -0.022)),
        "head": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-9.0, 0.0, 4.0)),
        "shin.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(4.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
    })
    # The Long-Arm Walker's swing sits a quarter cycle behind the legs, so the
    # arms reach their extremes on the passing poses rather than on contact.
    # That lag is what makes the limbs read as pendulums the body is dragging
    # rather than as an ordinary counter-swing.
    long_idle_back = merge_pose(long_arm, {
        "upper_arm.L": BonePose(rotation_degrees=(5.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(3.0, -4.0, -30.0)),
        "head": BonePose(rotation_degrees=(7.0, 0.0, -3.0)),
    })
    long_idle_forward = merge_pose(long_arm, {
        "upper_arm.L": BonePose(rotation_degrees=(-4.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(-2.0, -4.0, -30.0)),
        "head": BonePose(rotation_degrees=(7.0, 0.0, 2.0)),
    })
    long_left_contact = merge_pose(long_arm, {
        "pelvis": BonePose(rotation_degrees=(2.0, 2.0, -1.0), location_m=(0, 0.004, -0.014)),
        "thigh.L": BonePose(rotation_degrees=(-11.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(8.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(11.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(0.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(0.0, -4.0, -30.0)),
    })
    long_right_pass = merge_pose(long_arm, {
        "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0.004, -0.004)),
        "thigh.L": BonePose(rotation_degrees=(2.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-7.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(13.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(-11.0, -4.0, -30.0)),
        "forearm.L": BonePose(rotation_degrees=(4.0, 0.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(-4.0, 0.0, 18.0)),
    })
    long_right_contact = merge_pose(long_arm, {
        "pelvis": BonePose(rotation_degrees=(2.0, -2.0, 1.0), location_m=(0, 0.004, -0.014)),
        "thigh.L": BonePose(rotation_degrees=(8.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(11.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-11.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(0.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(0.0, -4.0, -30.0)),
    })
    long_left_pass = merge_pose(long_arm, {
        "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0.004, -0.004)),
        "thigh.L": BonePose(rotation_degrees=(-7.0, 0.0, 1.0)),
        "shin.L": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(2.0, 0.0, -1.0)),
        "shin.R": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(-11.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(13.0, -4.0, -30.0)),
        "forearm.L": BonePose(rotation_degrees=(-4.0, 0.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(4.0, 0.0, 18.0)),
    })
    # The hopper is the only archetype that leaves the ground. Its cycle is
    # crouch -> launch -> tucked apex -> reach -> crouch, and both legs stay
    # symmetrical throughout; there is no left/right step anywhere in it.
    helmet_idle_settle = merge_pose(helmet, {
        "pelvis": BonePose(rotation_degrees=(11.0, 0.0, 0.0), location_m=(0, 0.020, -0.222)),
        "head": BonePose(rotation_degrees=(-4.0, 0.0, -9.0)),
        "thigh.L": BonePose(rotation_degrees=(-62.0, 0.0, 3.0)),
        "thigh.R": BonePose(rotation_degrees=(-62.0, 0.0, -3.0)),
        "shin.L": BonePose(rotation_degrees=(93.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(93.0, 0.0, 0.0)),
    })
    helmet_idle_scan = merge_pose(helmet, {
        "pelvis": BonePose(rotation_degrees=(9.0, 0.0, 0.0), location_m=(0, 0.020, -0.190)),
        "head": BonePose(rotation_degrees=(-3.0, 0.0, 10.0)),
    })
    helmet_launch = merge_pose(helmet, {
        "pelvis": BonePose(rotation_degrees=(4.0, 0.0, 0.0), location_m=(0, 0.014, -0.030)),
        "spine": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-9.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-16.0, 0.0, 3.0)),
        "thigh.R": BonePose(rotation_degrees=(-16.0, 0.0, -3.0)),
        "shin.L": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-38.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-38.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(30.0, 8.0, 42.0)),
        "upper_arm.R": BonePose(rotation_degrees=(30.0, -8.0, -42.0)),
    })
    helmet_apex = merge_pose(helmet, {
        "pelvis": BonePose(rotation_degrees=(14.0, 0.0, 0.0), location_m=(0, 0.020, 0.070)),
        "spine": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        # Heels tucked up under the body, the classic airborne hop shape.
        "thigh.L": BonePose(rotation_degrees=(-72.0, 0.0, 3.0)),
        "thigh.R": BonePose(rotation_degrees=(-72.0, 0.0, -3.0)),
        "shin.L": BonePose(rotation_degrees=(112.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(112.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(8.0, 8.0, 42.0)),
        "upper_arm.R": BonePose(rotation_degrees=(8.0, -8.0, -42.0)),
        "forearm.L": BonePose(rotation_degrees=(-92.0, 6.0, -14.0)),
        "forearm.R": BonePose(rotation_degrees=(-92.0, -6.0, 14.0)),
    })
    helmet_reach = merge_pose(helmet, {
        "pelvis": BonePose(rotation_degrees=(8.0, 0.0, 0.0), location_m=(0, 0.018, -0.062)),
        "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-42.0, 0.0, 3.0)),
        "thigh.R": BonePose(rotation_degrees=(-42.0, 0.0, -3.0)),
        "shin.L": BonePose(rotation_degrees=(56.0, 0.0, 0.0)),
        "shin.R": BonePose(rotation_degrees=(56.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-22.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-22.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(26.0, 8.0, 42.0)),
        "upper_arm.R": BonePose(rotation_degrees=(26.0, -8.0, -42.0)),
    })
    pipeback_idle_inhale = merge_pose(pipeback, {
        "pelvis": BonePose(rotation_degrees=(-6.0, 0.8, -0.6), location_m=(0, 0.004, 0.008)),
        "spine": BonePose(rotation_degrees=(6.5, -0.5, 0.8)),
        "chest": BonePose(rotation_degrees=(5.5, 0.8, -1.0)),
        # The neck cancels the body drift so the face stays unnaturally level.
        "neck": BonePose(rotation_degrees=(-7.5, -0.8, 0.8)),
        "head": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
    })
    pipeback_idle_exhale = merge_pose(pipeback, {
        "pelvis": BonePose(rotation_degrees=(-8.0, -0.8, 0.6), location_m=(0, -0.003, 0.002)),
        "spine": BonePose(rotation_degrees=(3.5, 0.5, -0.8)),
        "chest": BonePose(rotation_degrees=(2.5, -0.8, 1.0)),
        "neck": BonePose(rotation_degrees=(-4.5, 0.8, -0.8)),
        "head": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
    })
    pipeback_push = merge_pose(pipeback, {
        "pelvis": BonePose(rotation_degrees=(-2.0, 0.0, 0.0), location_m=(0, -0.018, 0.008)),
        "spine": BonePose(rotation_degrees=(11.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(9.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-15.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(5.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(50.0, 7.0, 35.0)),
        "upper_arm.R": BonePose(rotation_degrees=(50.0, -7.0, -35.0)),
        "forearm.L": BonePose(rotation_degrees=(-82.0, 4.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(-82.0, -4.0, 18.0)),
        "hand.L": BonePose(rotation_degrees=(-20.0, -4.0, 4.0)),
        "hand.R": BonePose(rotation_degrees=(-20.0, 4.0, -4.0)),
    })
    pipeback_release = merge_pose(pipeback, {
        "pelvis": BonePose(rotation_degrees=(-9.0, 0.0, 0.0), location_m=(0, 0.006, 0.003)),
        "spine": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(1.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(28.0, 9.0, 41.0)),
        "upper_arm.R": BonePose(rotation_degrees=(28.0, -9.0, -41.0)),
        "forearm.L": BonePose(rotation_degrees=(-108.0, 4.0, -22.0)),
        "forearm.R": BonePose(rotation_degrees=(-108.0, -4.0, 22.0)),
        "hand.L": BonePose(rotation_degrees=(-6.0, -4.0, 4.0)),
        "hand.R": BonePose(rotation_degrees=(-6.0, 4.0, -4.0)),
    })
    # Seated loops. Each keeps the design's own upper body over one shared
    # seated leg shape, then breathes between two settled poses so the clip
    # loops on itself without ever standing up.
    lamp_seated = seated_pose(lampshade, {
        "spine": BonePose(rotation_degrees=(21.0, 0.0, 2.0)),
        "chest": BonePose(rotation_degrees=(15.0, 0.0, -2.0)),
        "head": BonePose(rotation_degrees=(10.0, 0.0, -1.0)),
    })
    lamp_seated_breath = merge_pose(lamp_seated, {
        "spine": BonePose(rotation_degrees=(19.0, 0.0, 2.5)),
        "chest": BonePose(rotation_degrees=(17.0, 0.0, -2.5)),
        "head": BonePose(rotation_degrees=(8.5, 0.0, -1.5)),
    })
    chair_seated = seated_pose(chair, {
        "chest": BonePose(rotation_degrees=(1.0, 0.0, 0.0)),
    })
    chair_seated_breath = merge_pose(chair_seated, {
        "pelvis": BonePose(rotation_degrees=(-6.0, 1.0, -1.0)),
        "chest": BonePose(rotation_degrees=(3.0, -0.5, 1.0)),
        "head": BonePose(rotation_degrees=(1.0, 0.0, -0.6)),
    })
    kettle_seated = seated_pose(kettle)
    kettle_seated_breath = merge_pose(kettle_seated, {
        "pelvis": BonePose(rotation_degrees=(-6.0, -1.5, 1.5)),
        "chest": BonePose(rotation_degrees=(3.0, 1.0, -1.5)),
        "head": BonePose(rotation_degrees=(1.5, 0.0, 1.0)),
    })
    # The one design whose arms would otherwise pass through the cabin floor:
    # seated, the ground-reaching forearms fold onto the knees instead.
    long_seated = seated_pose(long_arm, {
        "upper_arm.L": BonePose(rotation_degrees=(24.0, 4.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(24.0, -4.0, -30.0)),
        "forearm.L": BonePose(rotation_degrees=(-68.0, 0.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(-68.0, 0.0, 18.0)),
    })
    long_seated_sway = merge_pose(long_seated, {
        "upper_arm.L": BonePose(rotation_degrees=(21.0, 4.0, 31.0)),
        "upper_arm.R": BonePose(rotation_degrees=(27.0, -4.0, -29.0)),
        "forearm.L": BonePose(rotation_degrees=(-64.0, 0.0, -18.0)),
        "forearm.R": BonePose(rotation_degrees=(-72.0, 0.0, 18.0)),
    })
    babushka = babushka_base_pose()
    # The strolling smoker: a slow four-step shuffle carried by the
    # legs while the left arm talks — palm-open sweep, inward chop,
    # open again — and the right hand keeps the cigarette ready at
    # chest height, taking one drag on the last two steps of the lap.
    def babushka_stroll_legs(
        left_forward: float,
        lean: float,
    ) -> dict[str, BonePose]:
        # Shuffle gait: barely lifted feet, short stride, the pelvis
        # rocking over the planted side.
        return {
            "pelvis": BonePose(
                rotation_degrees=(7.0, lean * 2.0, -lean * 2.5),
                location_m=(0, 0.012, -0.052)),
            "thigh.L": BonePose(
                rotation_degrees=(-5.0 - left_forward * 13.0, 0.0, 3.0)),
            "shin.L": BonePose(
                rotation_degrees=(10.0 + max(0.0, -left_forward) * 14.0, 0.0, 0.0)),
            "foot.L": BonePose(
                rotation_degrees=(-5.0 + left_forward * 4.0, 0.0, 0.0)),
            "thigh.R": BonePose(
                rotation_degrees=(-5.0 + left_forward * 13.0, 0.0, -3.0)),
            "shin.R": BonePose(
                rotation_degrees=(10.0 + max(0.0, left_forward) * 14.0, 0.0, 0.0)),
            "foot.R": BonePose(
                rotation_degrees=(-5.0 - left_forward * 4.0, 0.0, 0.0)),
        }

    babushka_cig_hold = {
        "upper_arm.R": BonePose(rotation_degrees=(16.0, -8.0, -22.0)),
        "forearm.R": BonePose(rotation_degrees=(-88.0, -8.0, 14.0)),
        "hand.R": BonePose(rotation_degrees=(-6.0, 4.0, -4.0)),
    }
    babushka_cig_drag = {
        "upper_arm.R": BonePose(rotation_degrees=(26.0, -12.0, -14.0)),
        "forearm.R": BonePose(rotation_degrees=(-128.0, -12.0, 20.0)),
        "hand.R": BonePose(rotation_degrees=(-16.0, 8.0, -8.0)),
        "neck": BonePose(rotation_degrees=(-13.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
    }
    babushka_gesture_open = {
        "upper_arm.L": BonePose(rotation_degrees=(28.0, 12.0, 8.0)),
        "forearm.L": BonePose(rotation_degrees=(-64.0, 10.0, -22.0)),
        "hand.L": BonePose(rotation_degrees=(-22.0, -8.0, 6.0)),
        "chest": BonePose(rotation_degrees=(7.0, 0.0, 5.0)),
        "head": BonePose(rotation_degrees=(7.0, 0.0, -4.0)),
    }
    babushka_gesture_chop = {
        "upper_arm.L": BonePose(rotation_degrees=(40.0, 6.0, 26.0)),
        "forearm.L": BonePose(rotation_degrees=(-98.0, 6.0, -30.0)),
        "hand.L": BonePose(rotation_degrees=(10.0, -6.0, 4.0)),
        "chest": BonePose(rotation_degrees=(9.0, 0.0, -4.0)),
        "head": BonePose(rotation_degrees=(8.0, 0.0, 3.0)),
    }
    babushka_stroll_l1 = merge_pose(
        babushka, babushka_stroll_legs(1.0, 1.0),
        babushka_cig_hold, babushka_gesture_open)
    babushka_stroll_p1 = merge_pose(
        babushka, babushka_stroll_legs(0.0, -0.4),
        babushka_cig_hold, babushka_gesture_open)
    babushka_stroll_r1 = merge_pose(
        babushka, babushka_stroll_legs(-1.0, -1.0),
        babushka_cig_hold, babushka_gesture_chop)
    babushka_stroll_p2 = merge_pose(
        babushka, babushka_stroll_legs(0.0, 0.4),
        babushka_cig_hold, babushka_gesture_chop)
    babushka_stroll_l2 = merge_pose(
        babushka, babushka_stroll_legs(1.0, 1.0),
        babushka_gesture_open, babushka_cig_drag)
    babushka_stroll_p3 = merge_pose(
        babushka, babushka_stroll_legs(0.0, -0.4),
        babushka_gesture_open, babushka_cig_drag)
    babushka_stroll_r2 = merge_pose(
        babushka, babushka_stroll_legs(-1.0, -1.0),
        babushka_gesture_open, babushka_cig_hold)
    babushka_stroll_p4 = merge_pose(
        babushka, babushka_stroll_legs(0.0, 0.4),
        babushka_cig_hold, babushka_gesture_open)
    # The carpet strike: wind the beater up over the right shoulder,
    # whip it forward into the hung carpet, recoil and lift again. The
    # left hand steadies the carpet edge at chest height throughout.
    babushka_beat_brace = {
        "upper_arm.L": BonePose(rotation_degrees=(26.0, 10.0, 30.0)),
        "forearm.L": BonePose(rotation_degrees=(-62.0, 6.0, -16.0)),
        "hand.L": BonePose(rotation_degrees=(-8.0, -4.0, 4.0)),
    }
    babushka_beat_windup = merge_pose(babushka, babushka_beat_brace, {
        # The body opens upward with the raised arm: the hunch eases,
        # the chest lifts, the weight rocks back an inch.
        "pelvis": BonePose(rotation_degrees=(2.0, 1.0, 2.0), location_m=(0, 0.020, -0.028)),
        "spine": BonePose(rotation_degrees=(2.0, 0.0, 3.0)),
        "chest": BonePose(rotation_degrees=(0.0, 0.0, 4.0)),
        "neck": BonePose(rotation_degrees=(-6.0, 0.0, -1.0)),
        "head": BonePose(rotation_degrees=(2.0, 0.0, -2.0)),
        # The backswing, probed on the real rig: the hand rises beside
        # the ear at (-0.55, -0.36, +1.32) and the paddle sweeps back
        # over the shoulder — direction (+0.43, +0.90, -0.05), tip
        # behind the back at (-0.31, +0.14, +1.29) — before whipping
        # forward. On this rig local X raises the sideways A-pose arm,
        # local Z swings it forward and local Y twists the paddle.
        "upper_arm.R": BonePose(rotation_degrees=(70.0, -70.0, 12.0)),
        "forearm.R": BonePose(rotation_degrees=(-60.0, 0.0, 5.0)),
        "hand.R": BonePose(rotation_degrees=(-55.0, -30.0, -4.0)),
    })
    babushka_beat_strike = merge_pose(babushka, babushka_beat_brace, {
        "pelvis": BonePose(rotation_degrees=(9.0, -1.0, -3.0), location_m=(0, -0.026, -0.055)),
        "spine": BonePose(rotation_degrees=(13.0, 0.0, -5.0)),
        "chest": BonePose(rotation_degrees=(11.0, 0.0, -7.0)),
        "neck": BonePose(rotation_degrees=(-12.0, 0.0, 2.0)),
        "head": BonePose(rotation_degrees=(9.0, 0.0, 3.0)),
        # The forward whack, probed on the real rig: the hand lands at
        # (-0.59, -0.41, +1.31) — forward and down out of the overhead
        # wind-up — and the paddle points almost straight forward
        # (direction y -0.96), its tip 0.94 m in front at carpet
        # height, into the hung cloth.
        "upper_arm.R": BonePose(rotation_degrees=(18.0, 35.0, 60.0)),
        "forearm.R": BonePose(rotation_degrees=(-15.0, 0.0, 5.0)),
        "hand.R": BonePose(rotation_degrees=(24.0, 3.0, -5.0)),
    })
    babushka_beat_recoil = merge_pose(babushka, babushka_beat_brace, {
        "pelvis": BonePose(rotation_degrees=(7.0, 0.0, -1.0), location_m=(0, -0.010, -0.050)),
        "spine": BonePose(rotation_degrees=(11.0, 0.0, -2.0)),
        "chest": BonePose(rotation_degrees=(9.0, 0.0, -3.0)),
        # The paddle bounces a little way back up off the carpet
        # (probed: tip just off the cloth at (-0.48, -0.76, +0.95)).
        "upper_arm.R": BonePose(rotation_degrees=(22.0, 22.0, 48.0)),
        "forearm.R": BonePose(rotation_degrees=(-28.0, 0.0, 5.0)),
        "hand.R": BonePose(rotation_degrees=(6.0, 3.0, -5.0)),
    })
    babushka_beat_lift = merge_pose(babushka, babushka_beat_brace, {
        "pelvis": BonePose(rotation_degrees=(5.0, 1.0, 1.0), location_m=(0, 0.010, -0.040)),
        "spine": BonePose(rotation_degrees=(6.0, 0.0, 2.0)),
        "chest": BonePose(rotation_degrees=(4.0, 0.0, 4.0)),
        # Halfway back up toward the backswing: the arm rises through
        # the front while the paddle already starts rotating rearward.
        "upper_arm.R": BonePose(rotation_degrees=(50.0, -25.0, 22.0)),
        "forearm.R": BonePose(rotation_degrees=(-45.0, 0.0, 5.0)),
        "hand.R": BonePose(rotation_degrees=(-25.0, -10.0, -4.0)),
    })
    weigh = weigh_attendant_base_pose()
    # The weigher's check: crane up at the dial four metres overhead,
    # lean toward the chest-height linkage, then crouch to pull one
    # chalk line along the deck edge and straighten back to the dial.
    weigh_check_read = merge_pose(weigh, {
        "neck": BonePose(rotation_degrees=(-16.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-18.0, 0.0, 2.0)),
        "spine": BonePose(rotation_degrees=(1.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
    })
    weigh_check_shift = merge_pose(weigh_check_read, {
        "pelvis": BonePose(rotation_degrees=(3.0, -1.0, 1.5), location_m=(0, 0.008, -0.034)),
        "head": BonePose(rotation_degrees=(-18.0, 0.0, -2.0)),
    })
    weigh_check_lean = merge_pose(weigh, {
        # Bent toward the linkage, the right hand reaching for it at
        # chest height while the eyes stay on the mechanism.
        "pelvis": BonePose(rotation_degrees=(8.0, 0.0, -1.0), location_m=(0, -0.010, -0.045)),
        "spine": BonePose(rotation_degrees=(14.0, 0.0, -2.0)),
        "chest": BonePose(rotation_degrees=(10.0, 0.0, -2.0)),
        "neck": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "upper_arm.R": BonePose(rotation_degrees=(20.0, 15.0, 30.0)),
        "forearm.R": BonePose(rotation_degrees=(-30.0, 0.0, 6.0)),
        "hand.R": BonePose(rotation_degrees=(8.0, 3.0, -4.0)),
    })
    weigh_check_crouch = merge_pose(weigh, {
        # A half squat at the deck edge; both soles stay planted so
        # the ordinary grounding bake holds, the pelvis drops and the
        # chalk hand reaches down in front of the boots.
        "pelvis": BonePose(rotation_degrees=(14.0, 0.0, -2.0), location_m=(0, -0.030, -0.250)),
        "spine": BonePose(rotation_degrees=(20.0, 0.0, -3.0)),
        "chest": BonePose(rotation_degrees=(14.0, 0.0, -3.0)),
        "neck": BonePose(rotation_degrees=(-8.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-58.0, 0.0, 6.0)),
        "shin.L": BonePose(rotation_degrees=(72.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-58.0, 0.0, -6.0)),
        "shin.R": BonePose(rotation_degrees=(72.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-12.0, 0.0, 0.0)),
        "upper_arm.R": BonePose(rotation_degrees=(30.0, 25.0, 52.0)),
        "forearm.R": BonePose(rotation_degrees=(-10.0, 0.0, 6.0)),
        "hand.R": BonePose(rotation_degrees=(16.0, 4.0, -4.0)),
        "upper_arm.L": BonePose(rotation_degrees=(14.0, 8.0, 44.0)),
        "forearm.L": BonePose(rotation_degrees=(-34.0, 4.0, -10.0)),
    })
    weigh_check_stroke = merge_pose(weigh_check_crouch, {
        # The chalk line itself: the crouch holds while the right
        # hand sweeps a hand-span sideways along the deck edge.
        "upper_arm.R": BonePose(rotation_degrees=(26.0, 34.0, 56.0)),
        "hand.R": BonePose(rotation_degrees=(20.0, -6.0, -8.0)),
        "chest": BonePose(rotation_degrees=(14.0, -3.0, -3.0)),
    })
    weigh_check_rise = merge_pose(weigh, {
        "pelvis": BonePose(rotation_degrees=(7.0, 0.0, 0.0), location_m=(0, -0.004, -0.090)),
        "spine": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-16.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-16.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(22.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
    })

    # The weighed worker's pace: heavy shuffling steps carried by the
    # legs with a tired counter-swing in the arms, and the square
    # standstill at the deck centre while the scale settles.
    def weigh_pace_step(
        left_forward: float,
        lean: float,
    ) -> dict[str, BonePose]:
        return {
            "pelvis": BonePose(
                rotation_degrees=(5.0, lean * 1.5, -lean * 2.0),
                location_m=(0, 0.006, -0.040)),
            "thigh.L": BonePose(
                rotation_degrees=(-3.0 - left_forward * 16.0, 0.0, 2.0)),
            "shin.L": BonePose(
                rotation_degrees=(6.0 + max(0.0, -left_forward) * 18.0, 0.0, 0.0)),
            "foot.L": BonePose(
                rotation_degrees=(-3.0 + left_forward * 5.0, 0.0, 0.0)),
            "thigh.R": BonePose(
                rotation_degrees=(-3.0 + left_forward * 16.0, 0.0, -2.0)),
            "shin.R": BonePose(
                rotation_degrees=(6.0 + max(0.0, left_forward) * 18.0, 0.0, 0.0)),
            "foot.R": BonePose(
                rotation_degrees=(-3.0 - left_forward * 5.0, 0.0, 0.0)),
            "upper_arm.L": BonePose(
                rotation_degrees=(8.0 - left_forward * 6.0, 5.0, 40.0)),
            "upper_arm.R": BonePose(
                rotation_degrees=(8.0 + left_forward * 6.0, -5.0, -40.0)),
        }

    weigh_pace_l = merge_pose(weigh, weigh_pace_step(1.0, 1.0))
    weigh_pace_pr = merge_pose(weigh, weigh_pace_step(0.0, -0.4))
    weigh_pace_r = merge_pose(weigh, weigh_pace_step(-1.0, -1.0))
    weigh_pace_pl = merge_pose(weigh, weigh_pace_step(0.0, 0.4))
    weigh_stand = merge_pose(weigh, {
        # Square and still at the deck centre, as if the platform
        # itself asked him to hold while the needle settles.
        "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0.004, -0.024)),
        "spine": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-5.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-2.0, 0.0, 2.0)),
        "shin.L": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-2.0, 0.0, -2.0)),
        "shin.R": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
    })
    weigh_stand_breath = merge_pose(weigh_stand, {
        "chest": BonePose(rotation_degrees=(4.0, 0.0, 0.5)),
        "clavicle.L": BonePose(rotation_degrees=(2.5, -2.0, 4.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.5, 2.0, -4.0)),
        "head": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
    })

    mourner = mourner_base_pose()

    # The mourner's grieving gait: short heavy steps with no arm swing,
    # both forearms staying folded to the chest around the bouquet.
    def mourner_walk_legs(
        left_forward: float,
        lean: float,
    ) -> dict[str, BonePose]:
        return {
            "pelvis": BonePose(
                rotation_degrees=(8.0, lean * 1.5, -lean * 2.0),
                location_m=(0, 0.008, -0.055)),
            "thigh.L": BonePose(
                rotation_degrees=(-6.0 - left_forward * 14.0, 0.0, 3.0)),
            "shin.L": BonePose(
                rotation_degrees=(10.0 + max(0.0, -left_forward) * 14.0, 0.0, 0.0)),
            "foot.L": BonePose(
                rotation_degrees=(-5.0 + left_forward * 4.0, 0.0, 0.0)),
            "thigh.R": BonePose(
                rotation_degrees=(-6.0 + left_forward * 14.0, 0.0, -3.0)),
            "shin.R": BonePose(
                rotation_degrees=(10.0 + max(0.0, left_forward) * 14.0, 0.0, 0.0)),
            "foot.R": BonePose(
                rotation_degrees=(-5.0 - left_forward * 4.0, 0.0, 0.0)),
        }

    mourner_walk_l = merge_pose(mourner, mourner_walk_legs(1.0, 1.0))
    mourner_walk_pr = merge_pose(mourner, mourner_walk_legs(0.0, -0.4))
    mourner_walk_r = merge_pose(mourner, mourner_walk_legs(-1.0, -1.0))
    mourner_walk_pl = merge_pose(mourner, mourner_walk_legs(0.0, 0.4))

    # The graveside rite, one authored pass: lay the bouquet low over
    # 3.5 s, sob for exactly 30 s behind raised hands, wipe each eye
    # with the veil edge over the last 3 s and fold the empty hands
    # back to the chest — the last key equals the first so the shared
    # library's loop contract holds even though the runtime plays the
    # rite exactly once per visit.
    mourner_lay_bow = merge_pose(mourner, {
        # Deep bow at the grave foot, both hands lowering the bouquet
        # toward the slab; the soles stay planted for the ordinary
        # grounding bake and the knees take the depth.
        "pelvis": BonePose(rotation_degrees=(26.0, 0.0, 0.0), location_m=(0, -0.030, -0.140)),
        "spine": BonePose(rotation_degrees=(28.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(22.0, 26.0, 58.0)),
        "upper_arm.R": BonePose(rotation_degrees=(22.0, -26.0, -58.0)),
        "forearm.L": BonePose(rotation_degrees=(-24.0, 4.0, -8.0)),
        "forearm.R": BonePose(rotation_degrees=(-24.0, -4.0, 8.0)),
        "hand.L": BonePose(rotation_degrees=(6.0, -3.0, 3.0)),
        "hand.R": BonePose(rotation_degrees=(6.0, 3.0, -3.0)),
        "thigh.L": BonePose(rotation_degrees=(-26.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(34.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-26.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(34.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
    })
    mourner_lay_rise = merge_pose(mourner, {
        # Straightening with the hands returning empty, hanging for a
        # breath before the grief pulls them up to the face.
        "pelvis": BonePose(rotation_degrees=(12.0, 0.0, 0.0), location_m=(0, -0.010, -0.070)),
        "spine": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(8.0, 5.0, 40.0)),
        "upper_arm.R": BonePose(rotation_degrees=(8.0, -5.0, -40.0)),
        "forearm.L": BonePose(rotation_degrees=(-12.0, 3.0, -6.0)),
        "forearm.R": BonePose(rotation_degrees=(-12.0, -3.0, 6.0)),
        "hand.L": BonePose(rotation_degrees=(2.0, -2.0, 2.0)),
        "hand.R": BonePose(rotation_degrees=(2.0, 2.0, -2.0)),
        "thigh.L": BonePose(rotation_degrees=(-12.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
        "foot.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-12.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
        "foot.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
    })
    mourner_cry_a = merge_pose(mourner, {
        # Hunched standing grief, both hands raised to the face, the
        # shoulders carried high on the caught breath.
        "pelvis": BonePose(rotation_degrees=(8.0, 0.0, 0.0), location_m=(0, 0.006, -0.052)),
        "spine": BonePose(rotation_degrees=(12.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(10.0, 0.0, 1.0)),
        "neck": BonePose(rotation_degrees=(-16.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "clavicle.L": BonePose(rotation_degrees=(7.0, -5.0, 9.0)),
        "clavicle.R": BonePose(rotation_degrees=(7.0, 5.0, -9.0)),
        "upper_arm.L": BonePose(rotation_degrees=(26.0, 10.0, 30.0)),
        "upper_arm.R": BonePose(rotation_degrees=(26.0, -10.0, -30.0)),
        "forearm.L": BonePose(rotation_degrees=(-122.0, 10.0, -22.0)),
        "forearm.R": BonePose(rotation_degrees=(-122.0, -10.0, 22.0)),
        "hand.L": BonePose(rotation_degrees=(-14.0, -6.0, 5.0)),
        "hand.R": BonePose(rotation_degrees=(-14.0, 6.0, -5.0)),
    })
    mourner_cry_b = merge_pose(mourner_cry_a, {
        # The exhale of the sob: the shoulders drop and the torso
        # sinks a touch — alternating with the high key shakes the
        # shoulders in the uneven rhythm of real crying.
        "pelvis": BonePose(rotation_degrees=(8.0, 0.0, 0.0), location_m=(0, 0.002, -0.058)),
        "spine": BonePose(rotation_degrees=(13.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(8.0, 0.0, -1.0)),
        "clavicle.L": BonePose(rotation_degrees=(1.0, -2.0, 4.0)),
        "clavicle.R": BonePose(rotation_degrees=(1.0, 2.0, -4.0)),
        "head": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
    })
    mourner_cry_deep = merge_pose(mourner_cry_a, {
        # Twice in the thirty seconds the grief folds her further
        # down — the deep drop that keeps the loop from reading as a
        # metronome.
        "pelvis": BonePose(rotation_degrees=(10.0, 0.0, 0.0), location_m=(0, -0.006, -0.070)),
        "spine": BonePose(rotation_degrees=(17.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(13.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
        "thigh.L": BonePose(rotation_degrees=(-9.0, 0.0, 3.0)),
        "shin.L": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
        "thigh.R": BonePose(rotation_degrees=(-9.0, 0.0, -3.0)),
        "shin.R": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
    })
    mourner_wipe_r = merge_pose(mourner_cry_a, {
        # The right hand wipes the eyes with the veil edge while the
        # left settles to the chest; the head tips into the wipe.
        "head": BonePose(rotation_degrees=(10.0, 0.0, -4.0)),
        "neck": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "upper_arm.R": BonePose(rotation_degrees=(28.0, -12.0, -26.0)),
        "forearm.R": BonePose(rotation_degrees=(-128.0, -12.0, 20.0)),
        "hand.R": BonePose(rotation_degrees=(-18.0, 8.0, -8.0)),
        "upper_arm.L": BonePose(rotation_degrees=(14.0, 6.0, 26.0)),
        "forearm.L": BonePose(rotation_degrees=(-70.0, 6.0, -12.0)),
        "hand.L": BonePose(rotation_degrees=(-6.0, -4.0, 4.0)),
        "clavicle.L": BonePose(rotation_degrees=(2.0, -3.0, 6.0)),
        "clavicle.R": BonePose(rotation_degrees=(4.0, 4.0, -7.0)),
    })
    mourner_wipe_l = merge_pose(mourner_cry_a, {
        "head": BonePose(rotation_degrees=(10.0, 0.0, 4.0)),
        "neck": BonePose(rotation_degrees=(-14.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(28.0, 12.0, 26.0)),
        "forearm.L": BonePose(rotation_degrees=(-128.0, 12.0, -20.0)),
        "hand.L": BonePose(rotation_degrees=(-18.0, -8.0, 8.0)),
        "upper_arm.R": BonePose(rotation_degrees=(14.0, -6.0, -26.0)),
        "forearm.R": BonePose(rotation_degrees=(-70.0, -6.0, 12.0)),
        "hand.R": BonePose(rotation_degrees=(-6.0, 4.0, -4.0)),
        "clavicle.R": BonePose(rotation_degrees=(2.0, 3.0, -6.0)),
        "clavicle.L": BonePose(rotation_degrees=(4.0, -4.0, 7.0)),
    })
    mourner_compose = merge_pose(mourner, {
        # The composed straightening before she turns to leave: taller
        # than any grieving key, the hands halfway back to the chest.
        "pelvis": BonePose(rotation_degrees=(6.0, 0.0, 0.0), location_m=(0, 0.010, -0.045)),
        "spine": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-10.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
        "upper_arm.L": BonePose(rotation_degrees=(14.0, 7.0, 26.0)),
        "upper_arm.R": BonePose(rotation_degrees=(14.0, -7.0, -26.0)),
        "forearm.L": BonePose(rotation_degrees=(-70.0, 7.0, -13.0)),
        "forearm.R": BonePose(rotation_degrees=(-70.0, -7.0, 13.0)),
    })

    # 36.5 s total: lay over 0-3.5 s, sob over 3.5-33.5 s at a slow
    # three-second breath with two authored deep drops, wipe and
    # compose over the last three seconds.
    mourner_total_seconds = 36.5
    mourner_mourn_keys: list[tuple[float, dict[str, BonePose]]] = [
        (0.0, mourner),
        (1.75 / mourner_total_seconds, mourner_lay_bow),
        (3.0 / mourner_total_seconds, mourner_lay_rise),
    ]
    for sob in range(21):
        seconds = 3.5 + sob * 1.5
        if sob in (10, 18):
            pose = mourner_cry_deep
        elif sob % 2 == 1:
            pose = mourner_cry_b
        else:
            pose = mourner_cry_a
        mourner_mourn_keys.append((seconds / mourner_total_seconds, pose))
    mourner_mourn_keys.extend((
        (34.0 / mourner_total_seconds, mourner_wipe_r),
        (35.0 / mourner_total_seconds, mourner_wipe_l),
        (35.7 / mourner_total_seconds, mourner_compose),
        (1.0, mourner),
    ))

    watchman = watchman_base_pose()

    # The watch loop: slow weight transfers under a disapproving head
    # shake, one chin jut with a raised-shoulder beat, the hands never
    # leaving the small of the back.
    def watchman_weight(lean: float) -> dict[str, BonePose]:
        return {
            "pelvis": BonePose(
                rotation_degrees=(4.0, lean * 1.5, -lean * 2.5),
                location_m=(lean * 0.015, 0.006, -0.037)),
            "spine": BonePose(rotation_degrees=(8.0, 0.0, lean * 1.5)),
            "chest": BonePose(rotation_degrees=(5.0, 0.0, -lean * 1.0)),
            "thigh.L": BonePose(
                rotation_degrees=(-4.0, 0.0, 3.0 + lean * 1.5)),
            "thigh.R": BonePose(
                rotation_degrees=(-4.0, 0.0, -3.0 + lean * 1.5)),
        }

    watchman_left = merge_pose(watchman, watchman_weight(1.0))
    watchman_right = merge_pose(watchman, watchman_weight(-1.0))
    watchman_shake_left = merge_pose(watchman, watchman_weight(1.0), {
        "head": BonePose(rotation_degrees=(-2.0, -10.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-12.0, -4.0, 0.0)),
    })
    watchman_shake_right = merge_pose(watchman, watchman_weight(0.4), {
        "head": BonePose(rotation_degrees=(-2.0, 9.0, 0.0)),
        "neck": BonePose(rotation_degrees=(-12.0, 3.0, 0.0)),
    })
    watchman_jut = merge_pose(watchman, watchman_weight(-0.6), {
        # The chin jut: the head tips back under the visor, the
        # shoulders shrug once — "ну-ну, посмотрим".
        "neck": BonePose(rotation_degrees=(-18.0, 0.0, 0.0)),
        "head": BonePose(rotation_degrees=(-10.0, 0.0, 2.0)),
        "clavicle.L": BonePose(rotation_degrees=(6.0, -7.0, 9.0)),
        "clavicle.R": BonePose(rotation_degrees=(6.0, 7.0, -9.0)),
        "chest": BonePose(rotation_degrees=(3.0, 0.0, 0.5)),
    })

    # The shuffle: short heavy old-man steps, hands staying clasped
    # behind the back, a mild bob of the capped head.
    def watchman_shuffle_legs(
        left_forward: float,
        lean: float,
    ) -> dict[str, BonePose]:
        return {
            "pelvis": BonePose(
                rotation_degrees=(5.0, lean * 1.5, -lean * 2.0),
                location_m=(0, 0.006, -0.040)),
            "head": BonePose(
                rotation_degrees=(-2.0 + abs(left_forward) * 2.0, 0.0, 0.0)),
            "thigh.L": BonePose(
                rotation_degrees=(-4.0 - left_forward * 13.0, 0.0, 3.0)),
            "shin.L": BonePose(
                rotation_degrees=(8.0 + max(0.0, -left_forward) * 13.0, 0.0, 0.0)),
            "foot.L": BonePose(
                rotation_degrees=(-4.0 + left_forward * 4.0, 0.0, 0.0)),
            "thigh.R": BonePose(
                rotation_degrees=(-4.0 + left_forward * 13.0, 0.0, -3.0)),
            "shin.R": BonePose(
                rotation_degrees=(8.0 + max(0.0, left_forward) * 13.0, 0.0, 0.0)),
            "foot.R": BonePose(
                rotation_degrees=(-4.0 - left_forward * 4.0, 0.0, 0.0)),
        }

    watchman_step_l = merge_pose(watchman, watchman_shuffle_legs(1.0, 1.0))
    watchman_step_pr = merge_pose(watchman, watchman_shuffle_legs(0.0, -0.4))
    watchman_step_r = merge_pose(watchman, watchman_shuffle_legs(-1.0, -1.0))
    watchman_step_pl = merge_pose(watchman, watchman_shuffle_legs(0.0, 0.4))

    # The fisherman. One leaning loop and one trudge, both off the same
    # hooded stance. The leaning loop is built on an exact quarter-loop
    # breath grid: rest at every quarter, full inhale at every eighth
    # between them, so `frac(normalized * 4)` is the breath phase and
    # phase 0.5 is the top of the draw. The ember and the plume read
    # exactly that, which is the whole reason the grid is regular
    # rather than expressive.
    fisher = fisherman_base_pose()

    def fisher_breath(amount: float) -> dict[str, BonePose]:
        """One inhale, as a fraction of the full draw on the pipe.

        Only the spine chain moves, and that is a rule rather than a
        simplification. Both clavicles hang off the chest, so breathing
        on the chest swings both arms and the rod together and the
        two-handed grip survives untouched; a breath authored on the
        clavicles would open his hands off the stick once per lap.
        """

        return {
            "spine": BonePose(rotation_degrees=(16.0 - 2.6 * amount, 0.0, 0.0)),
            "chest": BonePose(rotation_degrees=(10.0 - 3.4 * amount, 0.0, 0.0)),
            "neck": BonePose(rotation_degrees=(7.0 + 1.2 * amount, 0.0, 0.0)),
            "head": BonePose(rotation_degrees=(5.0 - 1.6 * amount, 0.0, 0.0)),
        }

    fisher_lean = fisher
    fisher_inhale = merge_pose(fisher, fisher_breath(1.0))
    # The one thing that happens in eight seconds: on the third breath
    # he comes off the board, lifts the tip and looks down the line,
    # then tips back onto it. Authored on the spine and the neck for
    # the same reason the breath is - the rod has to come up with both
    # hands still on it, and the only way to guarantee that is to move
    # everything above the waist as one piece.
    fisher_lift = merge_pose(fisher, {
        "pelvis": BonePose(rotation_degrees=(3.0, 0.0, 0.0), location_m=(0, 0.008, -0.026)),
        "spine": BonePose(rotation_degrees=(9.5, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(5.0, 0.0, -1.5)),
        "neck": BonePose(rotation_degrees=(3.0, 0.0, -4.0)),
        "head": BonePose(rotation_degrees=(2.0, 0.0, -3.0)),
    })
    fisher_lift_inhale = merge_pose(fisher_lift, {
        "spine": BonePose(rotation_degrees=(6.9, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(1.6, 0.0, -1.5)),
        "neck": BonePose(rotation_degrees=(4.2, 0.0, -4.0)),
        "head": BonePose(rotation_degrees=(0.4, 0.0, -3.0)),
    })

    def fisher_trudge_legs(
        left_forward: float,
        lean: float,
    ) -> dict[str, BonePose]:
        return {
            "pelvis": BonePose(
                rotation_degrees=(5.0 + lean * 1.5, 0.0, left_forward * 1.2),
                location_m=(0, 0.008, -0.032),
            ),
            "thigh.L": BonePose(rotation_degrees=(-13.0 - left_forward * 16.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(19.0 + max(0.0, -left_forward) * 24.0, 0.0, 0.0)),
            "foot.L": BonePose(rotation_degrees=(-7.0 + left_forward * 6.0, 0.0, 0.0)),
            "thigh.R": BonePose(rotation_degrees=(-5.0 + left_forward * 16.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(10.0 + max(0.0, left_forward) * 24.0, 0.0, 0.0)),
            "foot.R": BonePose(rotation_degrees=(-5.0 - left_forward * 6.0, 0.0, 0.0)),
        }

    fisher_step_l = merge_pose(fisher, fisher_trudge_legs(1.0, 1.0))
    fisher_step_pr = merge_pose(fisher, fisher_trudge_legs(0.0, -0.4))
    fisher_step_r = merge_pose(fisher, fisher_trudge_legs(-1.0, 1.0))
    fisher_step_pl = merge_pose(fisher, fisher_trudge_legs(0.0, -0.4))

    # The chess player. One brooding loop on the plank and one trudge,
    # off two different stances, because unlike every other staged
    # design his idle is seated and his walk is not.
    chess = chess_player_base_pose()
    # ------------------------------------------------------- ferryman
    ferry_wait = ferryman_base_pose()
    ferry_wait_inhale = ferry_breath(ferry_wait, 1.0)
    # The flick: the whole throw is in the wrist and a few degrees of
    # forearm. A big arm swing would read as a man throwing something
    # away rather than one amusing himself.
    ferry_wait_flick = merge_pose(
        ferry_wait,
        {
            "forearm.L": BonePose(rotation_degrees=(-70.0, 40.0, 8.0)),
            "hand.L": BonePose(rotation_degrees=(-16.0, 20.0, -10.0)),
            "head": BonePose(rotation_degrees=(-10.0, -6.0, 2.0)),
        },
    )
    # Hand raised to meet the coin at the top of its arc.
    ferry_wait_reach = merge_pose(
        ferry_wait,
        {
            "forearm.L": BonePose(rotation_degrees=(-84.0, 40.0, 8.0)),
            "hand.L": BonePose(rotation_degrees=(4.0, 20.0, -10.0)),
            "head": BonePose(rotation_degrees=(-12.0, -5.0, 2.0)),
        },
    )
    # And gives with the catch, the way a caught weight is absorbed.
    ferry_wait_catch = merge_pose(
        ferry_wait,
        {
            "forearm.L": BonePose(rotation_degrees=(-72.0, 40.0, 8.0)),
            "hand.L": BonePose(rotation_degrees=(20.0, 20.0, -10.0)),
            "head": BonePose(rotation_degrees=(-4.0, -7.0, 2.0)),
        },
    )
    # Once a loop he looks up from the coin at the island instead.
    ferry_wait_glance = merge_pose(
        ferry_wait,
        {
            "neck": BonePose(rotation_degrees=(-6.0, -14.0, 0.0)),
            "head": BonePose(rotation_degrees=(-4.0, -12.0, 3.0)),
        },
    )

    ferry_stand = ferryman_stand_pose()
    ferry_step_l = merge_pose(
        ferry_stand,
        {
            "thigh.L": BonePose(rotation_degrees=(-20.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
            "thigh.R": BonePose(rotation_degrees=(14.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-14.0, 0.0, 62.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-2.0, 0.0, -62.0)),
        },
    )
    ferry_step_r = merge_pose(
        ferry_stand,
        {
            "thigh.R": BonePose(rotation_degrees=(-20.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
            "thigh.L": BonePose(rotation_degrees=(14.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-14.0, 0.0, -62.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-2.0, 0.0, 62.0)),
        },
    )
    ferry_pass_r = merge_pose(
        ferry_stand,
        {
            "thigh.R": BonePose(rotation_degrees=(-6.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(26.0, 0.0, 0.0)),
            "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0, 0.008)),
        },
    )
    ferry_pass_l = merge_pose(
        ferry_stand,
        {
            "thigh.L": BonePose(rotation_degrees=(-6.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(26.0, 0.0, 0.0)),
            "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0, 0.008)),
        },
    )

    ferry_drive = ferryman_drive_pose()
    ferry_drive_breath = merge_pose(
        ferry_drive,
        {
            "chest": BonePose(rotation_degrees=(1.5, 0.0, 0.0)),
            "clavicle.L": BonePose(rotation_degrees=(3.0, -3.0, 6.0)),
            "clavicle.R": BonePose(rotation_degrees=(3.0, 3.0, -6.0)),
        },
    )
    # The push off the metal: the bracing arm straightens hard and the
    # hips leave the bonnet before anything else has moved.
    ferry_board_push = merge_pose(
        ferry_wait,
        {
            "pelvis": BonePose(rotation_degrees=(-2.0, 0.0, 0.0), location_m=(0, -0.04, 0.06)),
            "spine": BonePose(rotation_degrees=(6.0, 4.0, 0.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-6.0, -10.0, -76.0)),
            "thigh.L": BonePose(rotation_degrees=(-32.0, 0.0, 3.0)),
            "thigh.R": BonePose(rotation_degrees=(-28.0, 0.0, -8.0)),
            "head": BonePose(rotation_degrees=(-2.0, -18.0, 2.0)),
        },
    )
    ferry_board_turn = merge_pose(
        ferry_drive,
        {
            "pelvis": BonePose(rotation_degrees=(4.0, 22.0, 0.0), location_m=(0, -0.02, 0.05)),
            "spine": BonePose(rotation_degrees=(8.0, 14.0, 0.0)),
            "head": BonePose(rotation_degrees=(-2.0, -22.0, 2.0)),
        },
    )
    ferry_board_drop = merge_pose(
        ferry_drive,
        {
            "pelvis": BonePose(rotation_degrees=(6.0, 4.0, 0.0), location_m=(0, 0, -0.02)),
            "spine": BonePose(rotation_degrees=(7.0, 2.0, 0.0)),
        },
    )

    chess_stand = chess_player_stand_pose()

    def chess_breath(amount: float) -> dict[str, BonePose]:
        """One slow breath, as a fraction of a full one.

        Only the spine and the chest move, and here that is the exact
        inverse of the rule the fisherman needed. His hands had to stay
        on a rod carried by his own fist, so his breath could move the
        neck and the head freely. This design's hands hold his head:
        the skull rides chest -> neck -> head and both palms ride
        chest -> clavicle -> arm, so keying the chest carries all three
        as one rigid piece and the head stays cradled. A breath authored
        on the neck or the head instead would slide the face out of the
        palms once per lap, which is the whole illusion.
        """

        return {
            "spine": BonePose(rotation_degrees=(22.0 - 1.8 * amount, 0.0, 0.0)),
            "chest": BonePose(rotation_degrees=(12.0 - 2.2 * amount, 0.0, 0.0)),
        }

    chess_brood = chess
    chess_brood_inhale = merge_pose(chess, chess_breath(1.0))
    # The one thing that happens in twelve seconds: the back rounds a
    # little further, he settles, and it goes again. Authored on the same
    # three bones as the breath, and kept as small as it is for a reason
    # the fisherman never had to worry about - his elbows are resting on
    # a fixed board. Every degree at the chest slides them about five
    # millimetres across it, so a settle authored with any real amplitude
    # would push both sleeves through the squares once a loop. The legs
    # carry the rest of it: a man who rounds forward takes the weight off
    # his drawn-back foot.
    chess_sink = merge_pose(chess, {
        "pelvis": BonePose(rotation_degrees=(9.0, 0.0, 0.0), location_m=(0, 0.004, -0.014)),
        "spine": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(13.0, 0.0, -0.8)),
        "thigh.R": BonePose(rotation_degrees=(-72.0, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(110.0, 0.0, 0.0)),
    })
    chess_sink_inhale = merge_pose(chess_sink, {
        "spine": BonePose(rotation_degrees=(22.2, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(10.8, 0.0, -0.8)),
    })

    # The shout. One pose, taken by both men, because their bodies are
    # the same body: `checkers_player_base_pose()` returns the chess
    # player's, and the mirror the pair reads as lives in the seat the
    # runtime puts them on.
    #
    # That the two can share a pose at all is a fact about the drawn
    # set, not a convenience. The chess seat is the `-seat-a1` plank at
    # local `(-1.85, -1.10)` facing `+Forward`; the draughts seat is
    # `-seat-b2` at `(+1.85, +1.10)` facing `-Forward`. Since
    # `Tangent = (-Forward.z, 0, Forward.x)` points to the left of
    # anybody facing `+Forward`, the neighbour lies `2.2 m` ahead and
    # `3.7 m` to the LEFT of the chess player - and, working the same
    # projection on the far seat, `2.2 m` ahead and `3.7 m` to the left
    # of the draughts player too. Each of them sees the other over the
    # same shoulder. So both turn left, and the arm that goes up is the
    # left one for both, on the side the head went.
    #
    # `+Y` on a head, neck or chest bone is that left turn: the bone
    # runs up its own local Y, the body faces `-Y` in Blender source
    # space with anatomical left at `+X`, and a positive turn about the
    # vertical carries the face toward `+X`. The watchman's head shake
    # keys the same axis.
    #
    # The sum is `64`, short of the `73` degrees that would put his eyes
    # exactly on the neighbour. That is deliberate: an old neck does not
    # go there, and the read is the throw rather than the eyeline.
    def park_jeer(amount: float) -> dict[str, BonePose]:
        """The shout, from the brooding base at `0` to full throw at `1`.

        Linear on every channel so that a small negative amount is a
        real anticipation beat rather than a special case.
        """

        def to(base: float, thrown: float) -> float:
            return base + (thrown - base) * amount

        return {
            # He sits up out of the round back to do it.
            "pelvis": BonePose(
                rotation_degrees=(to(8.0, 5.0), 0.0, 0.0),
                location_m=(0.0, 0.004, to(-0.010, -0.004)),
            ),
            "spine": BonePose(rotation_degrees=(to(22.0, 16.5), 0.0, 0.0)),
            "chest": BonePose(
                rotation_degrees=(to(12.0, 10.5), to(0.0, 16.0), 0.0)),
            # Chin up and round to the left. Negative X lifts the chin,
            # the way the watchman's jut does.
            "neck": BonePose(
                rotation_degrees=(to(-8.0, -15.0), to(0.0, 22.0), 0.0)),
            "head": BonePose(
                rotation_degrees=(
                    to(-2.0, -9.0), to(0.0, 26.0), to(0.0, -3.0))),
            # The left shoulder hikes and the whole arm leaves the board:
            # the elbow comes off the squares, the forearm opens out of
            # the fold that was holding the skull, and the hand ends up
            # open and high on the side the neighbour is on.
            "clavicle.L": BonePose(
                rotation_degrees=(
                    to(4.0, 12.0), to(-7.0, -7.0), to(9.0, 9.0))),
            "upper_arm.L": BonePose(
                rotation_degrees=(
                    to(-157.8, 55.0), to(1.0, 0.0), to(80.0, -30.0))),
            "forearm.L": BonePose(
                rotation_degrees=(
                    to(-115.6, 8.0), to(46.8, 0.0), to(11.0, -12.0))),
            "hand.L": BonePose(
                rotation_degrees=(
                    to(14.0, 0.0), to(18.0, 0.0), to(-8.0, -8.0))),
            # The right elbow never leaves the rim. Only the palm under
            # the jaw is given up, and it is given up by the head moving
            # rather than by the hand: that is the whole gesture.
            "hand.R": BonePose(
                rotation_degrees=(
                    to(14.0, 2.0), to(-18.0, -8.0), to(8.0, 3.0))),
        }

    # Anticipation, throw, the accusation held, and a collapse that is
    # slower than the throw was, because nobody snaps back into their
    # own hands.
    chess_jeer_load = merge_pose(chess, park_jeer(-0.14))
    chess_jeer_throw = merge_pose(chess, park_jeer(1.0))
    chess_jeer_hold = merge_pose(chess, park_jeer(0.94))
    chess_jeer_press = merge_pose(chess, park_jeer(0.82))
    chess_jeer_fall = merge_pose(chess, park_jeer(0.28))

    def chess_trudge_legs(
        left_forward: float,
        lean: float,
    ) -> dict[str, BonePose]:
        return {
            "pelvis": BonePose(
                rotation_degrees=(6.0 + lean * 1.2, 0.0, left_forward * 1.4),
                location_m=(0, 0.006, -0.030),
            ),
            "thigh.L": BonePose(rotation_degrees=(-5.0 - left_forward * 15.0, 0.0, 4.0)),
            "shin.L": BonePose(rotation_degrees=(9.0 + max(0.0, -left_forward) * 22.0, 0.0, 0.0)),
            "foot.L": BonePose(rotation_degrees=(-4.0 + left_forward * 5.0, 0.0, 0.0)),
            "thigh.R": BonePose(rotation_degrees=(-5.0 + left_forward * 15.0, 0.0, -4.0)),
            "shin.R": BonePose(rotation_degrees=(9.0 + max(0.0, left_forward) * 22.0, 0.0, 0.0)),
            "foot.R": BonePose(rotation_degrees=(-4.0 - left_forward * 5.0, 0.0, 0.0)),
        }

    chess_step_l = merge_pose(chess_stand, chess_trudge_legs(1.0, 1.0))
    chess_step_pr = merge_pose(chess_stand, chess_trudge_legs(0.0, -0.4))
    chess_step_r = merge_pose(chess_stand, chess_trudge_legs(-1.0, 1.0))
    chess_step_pl = merge_pose(chess_stand, chess_trudge_legs(0.0, -0.4))

    # The draughts player. Same posture, same two stances, own pair of
    # clips - see ACTION_SPECS for why a shared clip name is not an
    # option. What is authored differently is only the rhythm.
    checkers = checkers_player_base_pose()
    checkers_stand = checkers_player_stand_pose()

    def checkers_breath(amount: float) -> dict[str, BonePose]:
        """A shallower breath than the neighbour's, on the same bones.

        The same rule and for the same reason: his hands hold his head,
        so the skull rides chest -> neck -> head and both palms ride
        chest -> clavicle -> arm, and keying the chest alone carries all
        three as one piece. A breath on the neck would slide his face
        out of his own palms once a lap.

        Smaller than the chess player's because it has to be. His elbows
        rest on the same fixed board, where a degree at the chest walks
        them about five millimetres across the squares, and the wider
        draught on his head magnifies every degree into a visible sweep.
        """

        return {
            "spine": BonePose(rotation_degrees=(22.0 - 1.3 * amount, 0.0, 0.0)),
            "chest": BonePose(rotation_degrees=(12.0 - 1.6 * amount, 0.0, 0.0)),
        }

    checkers_mull = checkers
    checkers_mull_inhale = merge_pose(checkers, checkers_breath(1.0))
    # The settle comes early rather than at the half, so the two loops
    # never line up even where their periods do.
    checkers_sink = merge_pose(checkers, {
        "pelvis": BonePose(rotation_degrees=(9.0, 0.0, 0.0), location_m=(0, 0.004, -0.013)),
        "spine": BonePose(rotation_degrees=(23.6, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(12.8, 0.0, 0.7)),
        "thigh.R": BonePose(rotation_degrees=(-71.6, 0.0, -4.0)),
        "shin.R": BonePose(rotation_degrees=(109.0, 0.0, 0.0)),
    })
    checkers_sink_inhale = merge_pose(checkers_sink, {
        "spine": BonePose(rotation_degrees=(22.4, 0.0, 0.0)),
        "chest": BonePose(rotation_degrees=(11.3, 0.0, 0.7)),
    })

    checkers_jeer_load = merge_pose(checkers, park_jeer(-0.14))
    checkers_jeer_throw = merge_pose(checkers, park_jeer(1.0))
    checkers_jeer_hold = merge_pose(checkers, park_jeer(0.94))
    checkers_jeer_press = merge_pose(checkers, park_jeer(0.82))
    checkers_jeer_fall = merge_pose(checkers, park_jeer(0.28))

    checkers_step_l = merge_pose(checkers_stand, chess_trudge_legs(1.0, 1.0))
    checkers_step_pr = merge_pose(checkers_stand, chess_trudge_legs(0.0, -0.4))
    checkers_step_r = merge_pose(checkers_stand, chess_trudge_legs(-1.0, 1.0))
    checkers_step_pl = merge_pose(checkers_stand, chess_trudge_legs(0.0, -0.4))

    return {
        # The Ferryman's wait. A quarter-loop breath grid, exactly the
        # fisherman's contract, plus the one thing he does. The coin
        # leaves his palm at 0.0625 and lands back in it at 0.3125; the
        # hand rises through the throw and drops to meet the fall,
        # because a hand that stays still while a coin arcs off it reads
        # as the coin being fired rather than flicked.
        "FerrymanWait": (
            (0.0, ferry_wait),
            (0.0625, ferry_wait_flick),
            (0.125, ferry_wait_inhale),
            (0.25, ferry_wait_reach),
            (0.3125, ferry_wait_catch),
            (0.375, ferry_wait_inhale),
            (0.5, ferry_wait),
            (0.625, ferry_wait_inhale),
            (0.75, ferry_wait_glance),
            (0.875, ferry_wait_inhale),
            (1.0, ferry_wait),
        ),
        "FerrymanTrudge": (
            (0.0, ferry_step_l),
            (0.25, ferry_pass_r),
            (0.5, ferry_step_r),
            (0.75, ferry_pass_l),
            (1.0, ferry_step_l),
        ),
        # Off the bonnet and into the seat. Front-loaded on purpose: the
        # shove is over in the first third and the rest is him arriving.
        "FerrymanBoard": (
            (0.0, ferry_wait),
            (0.22, ferry_board_push),
            (0.55, ferry_board_turn),
            (0.80, ferry_board_drop),
            (1.0, ferry_drive),
        ),
        "FerrymanDrive": (
            (0.0, ferry_drive),
            (0.5, ferry_drive_breath),
            (1.0, ferry_drive),
        ),
        "ChessBrood": (
            (0.0, chess_brood),
            (0.125, chess_brood_inhale),
            (0.25, chess_brood),
            (0.375, chess_brood_inhale),
            (0.5, chess_sink),
            (0.625, chess_sink_inhale),
            (0.75, chess_brood),
            (0.875, chess_brood_inhale),
            (1.0, chess_brood),
        ),
        "ChessTrudge": (
            (0.0, chess_step_l),
            (0.25, chess_step_pr),
            (0.5, chess_step_r),
            (0.75, chess_step_pl),
            (1.0, chess_step_l),
        ),
        # Both ends are the brooding base pose itself, so the runtime
        # mixer can cross in and out of this in a tenth of a second and
        # the loop validator reads zero error.
        "ChessJeer": (
            (0.0, chess_brood),
            (0.08, chess_jeer_load),
            (0.22, chess_jeer_throw),
            (0.45, chess_jeer_hold),
            (0.62, chess_jeer_press),
            (0.82, chess_jeer_fall),
            (1.0, chess_brood),
        ),
        "CheckersMull": (
            (0.0, checkers_mull),
            (0.125, checkers_mull_inhale),
            (0.25, checkers_sink),
            (0.375, checkers_sink_inhale),
            (0.5, checkers_mull),
            (0.625, checkers_mull_inhale),
            (0.75, checkers_mull),
            (0.875, checkers_mull_inhale),
            (1.0, checkers_mull),
        ),
        "CheckersTrudge": (
            (0.0, checkers_step_l),
            (0.25, checkers_step_pr),
            (0.5, checkers_step_r),
            (0.75, checkers_step_pl),
            (1.0, checkers_step_l),
        ),
        # The same shout, keyed onto his own base pose and under his own
        # name. The name is what makes it a second clip: clips are keyed
        # by name and handed to a design by `design_id`, so sharing one
        # would leave this archetype with nothing baked. The pose is not
        # mirrored, because the seat already is - see `park_jeer`.
        "CheckersJeer": (
            (0.0, checkers_mull),
            (0.08, checkers_jeer_load),
            (0.22, checkers_jeer_throw),
            (0.45, checkers_jeer_hold),
            (0.62, checkers_jeer_press),
            (0.82, checkers_jeer_fall),
            (1.0, checkers_mull),
        ),
        "WatchmanWatch": (
            (0.0, watchman),
            (0.12, watchman_left),
            (0.30, watchman_shake_left),
            (0.45, watchman_shake_right),
            (0.55, watchman_left),
            (0.62, watchman_jut),
            (0.78, watchman_right),
            (0.90, watchman_right),
            (1.0, watchman),
        ),
        "WatchmanShuffle": (
            (0.0, watchman_step_l),
            (0.25, watchman_step_pr),
            (0.5, watchman_step_r),
            (0.75, watchman_step_pl),
            (1.0, watchman_step_l),
        ),
        "MournerWalk": (
            (0.0, mourner_walk_l),
            (0.25, mourner_walk_pr),
            (0.5, mourner_walk_r),
            (0.75, mourner_walk_pl),
            (1.0, mourner_walk_l),
        ),
        "MournerMourn": tuple(mourner_mourn_keys),
        "WeigherCheck": (
            (0.0, weigh_check_read),
            (0.18, weigh_check_shift),
            (0.34, weigh_check_lean),
            (0.52, weigh_check_crouch),
            (0.62, weigh_check_stroke),
            (0.80, weigh_check_rise),
            (1.0, weigh_check_read),
        ),
        # The standstill keys at 0.36 and 0.64 mirror
        # WeighbridgeAttendantPresentation.PauseStart/EndNormalized:
        # the runtime holds the corridor position over exactly this
        # window, so the pose stands square through it.
        "WeighedPace": (
            (0.0, weigh_pace_l),
            (0.09, weigh_pace_pr),
            (0.18, weigh_pace_r),
            (0.27, weigh_pace_pl),
            (0.36, weigh_stand),
            (0.50, weigh_stand_breath),
            (0.64, weigh_stand),
            (0.71, weigh_pace_pr),
            (0.79, weigh_pace_r),
            (0.86, weigh_pace_pl),
            (1.0, weigh_pace_l),
        ),
        "BabushkaSmoke": (
            (0.0, babushka_stroll_l1),
            (0.125, babushka_stroll_p1),
            (0.25, babushka_stroll_r1),
            (0.375, babushka_stroll_p2),
            (0.5, babushka_stroll_l2),
            (0.625, babushka_stroll_p3),
            (0.75, babushka_stroll_r2),
            (0.875, babushka_stroll_p4),
            (1.0, babushka_stroll_l1),
        ),
        "BabushkaBeat": (
            (0.0, babushka_beat_windup),
            (0.28, babushka_beat_strike),
            (0.42, babushka_beat_recoil),
            (0.66, babushka_beat_lift),
            (1.0, babushka_beat_windup),
        ),
        "PipebackIdle": (
            (0.0, pipeback),
            (0.25, pipeback_idle_inhale),
            (0.5, pipeback),
            (0.75, pipeback_idle_exhale),
            (1.0, pipeback),
        ),
        "PipebackRoll": (
            (0.0, pipeback),
            (0.32, pipeback_push),
            (0.62, pipeback_release),
            (1.0, pipeback),
        ),
        # Quarter-loop breath grid; see LakeFishermanPresentation.
        "FishermanLean": (
            (0.0, fisher_lean),
            (0.125, fisher_inhale),
            (0.25, fisher_lean),
            (0.375, fisher_inhale),
            (0.5, fisher_lift),
            (0.625, fisher_lift_inhale),
            (0.75, fisher_lean),
            (0.875, fisher_inhale),
            (1.0, fisher_lean),
        ),
        "FishermanTrudge": (
            (0.0, fisher_step_l),
            (0.25, fisher_step_pr),
            (0.5, fisher_step_r),
            (0.75, fisher_step_pl),
            (1.0, fisher_step_l),
        ),
        "LampshadeSit": ((0.0, lamp_seated), (0.5, lamp_seated_breath), (1.0, lamp_seated)),
        "ChairCarrierSit": ((0.0, chair_seated), (0.5, chair_seated_breath), (1.0, chair_seated)),
        "KettleHatSit": ((0.0, kettle_seated), (0.5, kettle_seated_breath), (1.0, kettle_seated)),
        "LongArmSit": ((0.0, long_seated), (0.5, long_seated_sway), (1.0, long_seated)),
        "HelmetLampIdle": ((0.0, helmet), (0.25, helmet_idle_settle), (0.5, helmet), (0.75, helmet_idle_scan), (1.0, helmet)),
        "HelmetLampHop": ((0.0, helmet), (0.25, helmet_launch), (0.5, helmet_apex), (0.75, helmet_reach), (1.0, helmet)),
        "LongArmIdle": ((0.0, long_arm), (0.25, long_idle_back), (0.5, long_arm), (0.75, long_idle_forward), (1.0, long_arm)),
        "LongArmWalk": ((0.0, long_left_contact), (0.25, long_right_pass), (0.5, long_right_contact), (0.75, long_left_pass), (1.0, long_left_contact)),
        "LampshadeIdle": ((0.0, lampshade), (0.25, lamp_idle_left), (0.5, lampshade), (0.75, lamp_idle_right), (1.0, lampshade)),
        "LampshadeWalk": ((0.0, lamp_left_contact), (0.25, lamp_right_pass), (0.5, lamp_right_contact), (0.75, lamp_left_drag), (1.0, lamp_left_contact)),
        "ChairCarrierIdle": ((0.0, chair), (0.25, chair_idle_left), (0.5, chair), (0.75, chair_idle_right), (1.0, chair)),
        "ChairCarrierWalk": ((0.0, chair_left_contact), (0.25, chair_right_pass), (0.5, chair_right_contact), (0.75, chair_left_pass), (1.0, chair_left_contact)),
        "KettleHatIdle": ((0.0, kettle), (0.25, kettle_idle_left), (0.5, kettle), (0.75, kettle_idle_right), (1.0, kettle)),
        "KettleHatWalk": ((0.0, kettle_left_contact), (0.25, kettle_right_pass), (0.5, kettle_right_contact), (0.75, kettle_left_pass), (1.0, kettle_left_contact)),
    }


def measure_loop_error(
    curves: Iterable[bpy.types.FCurve],
    frame_end: int,
) -> float:
    """How far a clip's last frame is from its first, in pose terms.

    Comparing raw curve values is wrong for rotation, and it is wrong
    in a way that looks exactly like a real defect. A quaternion double
    covers the rotation group: q and -q are the SAME orientation, and a
    bake that passes through a large arc can perfectly legitimately land
    on the antipodal representation of the pose it started from. The
    chess and checkers jeers did exactly that - their left upper arm
    ends at -q of its own first frame - and a per-component check called
    a clip that loops to the millimetre broken by 1.5.

    So rotation channels are grouped per bone and compared as rotations,
    through the dot product, while everything else keeps the plain
    per-component distance. Nothing is loosened: an orientation that has
    really drifted still fails, because |dot| only reaches 1 for the
    same rotation.
    """

    quaternions: dict[str, dict[int, tuple[float, float]]] = {}
    error = 0.0
    for curve in curves:
        start = curve.evaluate(0.0)
        end = curve.evaluate(frame_end)
        if curve.data_path.endswith(".rotation_quaternion"):
            channel = quaternions.setdefault(curve.data_path, {})
            channel[curve.array_index] = (start, end)
            continue

        error = max(error, abs(start - end))

    for channel in quaternions.values():
        if len(channel) != 4:
            # An incomplete quaternion cannot be compared as a rotation;
            # fall back to the component distance rather than passing it.
            error = max(
                error,
                max(abs(start - end) for start, end in channel.values()),
            )
            continue

        dot = sum(
            channel[index][0] * channel[index][1] for index in range(4)
        )
        error = max(error, 1.0 - abs(dot))

    return error


def antipodal_loop_bones(
    curves: Iterable[bpy.types.FCurve],
    frame_end: int,
) -> list[str]:
    """Bones whose last frame writes -q of their first.

    Their orientation is the same; only the sign of the quaternion is
    not. `measure_loop_error` deliberately accepts that, so this is
    how the fact still reaches a human.
    """

    channels: dict[str, dict[int, tuple[float, float]]] = {}
    for curve in curves:
        if not curve.data_path.endswith(".rotation_quaternion"):
            continue

        channel = channels.setdefault(curve.data_path, {})
        channel[curve.array_index] = (
            curve.evaluate(0.0),
            curve.evaluate(frame_end),
        )

    flipped: list[str] = []
    for path, channel in channels.items():
        if len(channel) != 4:
            continue

        dot = sum(
            channel[index][0] * channel[index][1] for index in range(4)
        )
        if dot < 0.0:
            flipped.append(path.split(chr(34))[1])

    return sorted(flipped)


def validate_animation_library(
    rig: bpy.types.Object,
    actions: dict[str, bpy.types.Action],
    grounding: dict[str, dict[str, object]],
) -> tuple[str, list[dict]]:
    errors: list[str] = []
    manifest_clips: list[dict] = []
    if [bone.name for bone in rig.data.bones] != [spec.name for spec in SKELETON]:
        errors.append("Animation rig bone order/names diverge from canonical rig")
    for spec in ACTION_SPECS:
        action = actions.get(spec.name)
        if action is None:
            errors.append(f"Missing Action {spec.name}")
            continue
        curves = list(iter_action_fcurves(action))
        keyed_names = {
            curve.data_path.split('pose.bones["', 1)[1].split('"]', 1)[0]
            for curve in curves
            if curve.data_path.startswith('pose.bones["')
        }
        loop_error = measure_loop_error(curves, spec.frame_end)
        antipodal = antipodal_loop_bones(curves, spec.frame_end)
        if antipodal:
            # Not an error: the pose is identical, only its
            # representation is negated, and looping playback restarts
            # at frame zero rather than interpolating across the seam.
            # It is still worth saying out loud, because anything that
            # ever CROSS-FADES a clip into its own first frame would
            # take the long way round on these bones.
            print(
                f"    note: {spec.name} ends on the antipodal "
                f"quaternion for {', '.join(antipodal)}; same pose, "
                "negated representation"
            )
        root_curves = [
            curve for curve in curves
            if curve.data_path == 'pose.bones["root"].location'
        ]
        root_ranges = []
        for axis in range(3):
            curve = next((item for item in root_curves if item.array_index == axis), None)
            values = [curve.evaluate(frame) for frame in range(spec.frame_end + 1)] if curve else [0.0]
            root_ranges.append(stable_float(max(values) - min(values)))
        if len(keyed_names) != len(SKELETON):
            errors.append(f"{spec.name} keys {len(keyed_names)} bones, expected {len(SKELETON)}")
        if loop_error > 0.0001 and not spec.one_shot:
            errors.append(f"{spec.name} loop error is {loop_error:.7f}")
        if any(value > 0.000001 for value in root_ranges):
            errors.append(f"{spec.name} root translation is not in-place: {root_ranges}")
        if bool(action.get("bp_root_motion", True)):
            errors.append(f"{spec.name} does not disable root motion")
        clip_payload = {
            "name": spec.name,
            "archetype": spec.archetype,
            "duration_seconds": spec.duration_seconds,
            "frame_start": 0,
            "frame_end": spec.frame_end,
            # A transition is not a loop, and saying it is would invite a
            # consumer to repeat a man jumping off a bonnet forever.
            "loop": not spec.one_shot,
            "one_shot": spec.one_shot,
            "in_place": True,
            "authored_posture": spec.authored_posture,
            "gait": spec.gait,
            "keyed_bone_count": len(keyed_names),
            "loop_max_error": stable_float(loop_error),
            "root_translation_range_m": root_ranges,
        }
        clip_payload.update(grounding.get(spec.name, {}))
        manifest_clips.append(clip_payload)
    if errors:
        raise RuntimeError("Pedestrian animation validation failed:\n" + "\n".join(f"  - {item}" for item in errors))
    signature_payload = {
        "generator_version": GENERATOR_VERSION,
        "fps": ANIMATION_FPS,
        "bones": [bone.name for bone in SKELETON],
        "clips": manifest_clips,
    }
    signature = hashlib.sha256(
        json.dumps(signature_payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    return signature, manifest_clips


def evaluated_part_min_z(part: PartRecord, depsgraph) -> float:
    evaluated = part.obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        return min(
            (evaluated.matrix_world @ vertex.co).z
            for vertex in mesh.vertices
        )
    finally:
        evaluated.to_mesh_clear()


def evaluated_part_world_vertices(part: PartRecord, depsgraph) -> list[Vector]:
    evaluated = part.obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        return [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
    finally:
        evaluated.to_mesh_clear()


def point_segment_distance(point: Vector, start: Vector, end: Vector) -> float:
    segment = end - start
    length_squared = segment.length_squared
    if length_squared <= 0.0000001:
        return (point - start).length
    amount = max(0.0, min(1.0, (point - start).dot(segment) / length_squared))
    return (point - (start + segment * amount)).length


def validate_wheelchair_clips(
    result: BuildResult,
    actions: dict[str, bpy.types.Action],
    archetype: ArchetypeSpec,
    authored_keys: dict[str, tuple[tuple[float, dict[str, BonePose]], ...]],
) -> dict[str, dict[str, object]]:
    """Prove wheelchair support, rider clearance and manual rim reach.

    The chair is intentionally not pelvis-baked: its two main tyres own the
    support plane while the body stays seated and the shoes remain clear of
    it.  Both hands must remain close enough to their own push-rim during the
    authored push/recovery loop; this catches a seated-looking pose that
    cannot actually propel the chair.
    """

    if archetype.wheel_radius_m is None:
        raise RuntimeError("Wheelchair validation needs a declared wheel radius")
    tyres = {
        side: next(
            (
                part for part in result.parts
                if part.role == "wheel_tyre" and part.obj.name.endswith(f".{side}")
            ),
            None,
        )
        for side in ("L", "R")
    }
    hands = {
        side: next(
            (part for part in result.parts if part.obj.name == f"GEO_Hand.{side}"),
            None,
        )
        for side in ("L", "R")
    }
    footwear = {
        side: [part for part in result.parts if part.bone == f"foot.{side}"]
        for side in ("L", "R")
    }
    pelvis_parts = [part for part in result.parts if part.bone == "pelvis"]
    if any(item is None for item in tyres.values()):
        raise RuntimeError("Wheelchair validation needs both main tyre meshes")
    if any(item is None for item in hands.values()):
        raise RuntimeError("Wheelchair validation needs both hand meshes")
    if any(not parts for parts in footwear.values()) or not pelvis_parts:
        raise RuntimeError("Wheelchair validation needs feet and seated pelvis geometry")

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    reports: dict[str, dict[str, object]] = {}
    for action_name, action in actions.items():
        animation_data.action = None
        tyre_samples: list[float] = []
        foot_samples: list[float] = []
        hand_rim_samples: list[float] = []
        hand_rim_details: list[tuple[float, int, str, Vector]] = []
        seat_samples: list[float] = []
        for frame in range(round(action.frame_start), round(action.frame_end) + 1):
            normalized = frame / max(1, round(action.frame_end))
            keyframes = authored_keys[action_name]
            active_pose = keyframes[-1][1]
            for index in range(len(keyframes) - 1):
                start_time, start_pose = keyframes[index]
                end_time, end_pose = keyframes[index + 1]
                if normalized <= end_time:
                    blend = 0.0 if end_time == start_time else (
                        normalized - start_time
                    ) / (end_time - start_time)
                    active_pose = interpolate_pose(start_pose, end_pose, blend)
                    break
            reset_pose(rig)
            apply_pose(rig, active_pose)
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            for side in ("L", "R"):
                tyre_samples.append(evaluated_part_min_z(tyres[side], depsgraph))
                foot_samples.append(
                    min(
                        evaluated_part_min_z(part, depsgraph)
                        for part in footwear[side]
                    )
                )
                # The long connected Player arms meet the chair's two raised
                # push levers. The manifest keeps the historic `rim_hand`
                # field name because both lever bases drive conventional rims
                # at the hubs.
                hand_bone = rig.pose.bones[f"hand.{side}"]
                hand_center = rig.matrix_world @ (
                    (hand_bone.head + hand_bone.tail) * 0.5
                )
                sign = 1.0 if side == "L" else -1.0
                elbow = Vector((sign * 0.250, 0.230, 1.000))
                distance = min(
                    point_segment_distance(
                        hand_center,
                        Vector((sign * 0.395, -0.080, 0.750)),
                        elbow,
                    ),
                    point_segment_distance(
                        hand_center,
                        elbow,
                        Vector((sign * 0.720, 0.220, 1.180)),
                    ),
                )
                hand_rim_samples.append(distance)
                hand_rim_details.append(
                    (distance, frame, side, hand_center)
                )
            pelvis_anchor_z = (
                rig.matrix_world @ rig.pose.bones["pelvis"].head
            ).z
            seat_samples.append(pelvis_anchor_z)

        wheel_min = min(tyre_samples)
        wheel_gap = max(abs(value) for value in tyre_samples)
        foot_clearance = min(foot_samples)
        rim_distance = max(hand_rim_samples)
        seat_gap = max(abs(value - PIPEBACK_SEAT_TOP_M) for value in seat_samples)
        if wheel_min < -0.002:
            raise RuntimeError(
                f"{action_name} tyres penetrate ground at {wheel_min:.4f} m"
            )
        if wheel_gap > 0.002:
            raise RuntimeError(
                f"{action_name} tyres lose contact by {wheel_gap:.4f} m"
            )
        if foot_clearance < 0.030:
            raise RuntimeError(
                f"{action_name} feet leave their rests and approach the ground "
                f"at {foot_clearance:.4f} m"
            )
        if rim_distance > 0.100:
            _, worst_frame, worst_side, worst_vertex = max(hand_rim_details)
            raise RuntimeError(
                f"{action_name} hands miss their push-rims by {rim_distance:.4f} m "
                f"({worst_side}, frame {worst_frame}, nearest hand vertex "
                f"{tuple(round(value, 4) for value in worst_vertex)})"
            )
        if seat_gap > 0.080:
            raise RuntimeError(
                f"{action_name} rider loses seat contact by {seat_gap:.4f} m"
            )
        reports[action_name] = {
            "wheel_ground_min_m": stable_float(wheel_min),
            "wheel_ground_max_contact_gap_m": stable_float(wheel_gap),
            "footrest_min_clearance_m": stable_float(foot_clearance),
            "rim_hand_max_distance_m": stable_float(rim_distance),
            "seat_contact_max_gap_m": stable_float(seat_gap),
        }
    animation_data.action = None
    scene.frame_set(0)
    reset_pose(rig)
    return reports


def validate_animated_grounding(
    result: BuildResult,
    actions: dict[str, bpy.types.Action],
    archetype: ArchetypeSpec | None = None,
) -> dict[str, dict[str, object]]:
    """Sample every frame against each model's real deformed footwear.

    Archetypes that declare `hand_clearance_m` are additionally checked for
    hand-to-pavement travel. Footwear grounding alone cannot catch that: a
    design whose hands hang near the ankles will happily push them through
    the road while every sole still reports a perfect contact.
    """

    if archetype is not None and archetype.wheel_radius_m is not None:
        return validate_wheelchair_clips(
            result,
            actions,
            archetype,
            animation_keys(),
        )

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    footwear = {
        side: [part for part in result.parts if part.bone == f"foot.{side}"]
        for side in ("L", "R")
    }
    if any(not parts for parts in footwear.values()):
        raise RuntimeError("Grounding validation needs geometry on both foot bones")
    hand_band = archetype.hand_clearance_m if archetype is not None else None
    hands = [
        part for part in result.parts
        if part.bone in {"hand.L", "hand.R", "forearm.L", "forearm.R"}
    ]
    if hand_band is not None and not hands:
        raise RuntimeError(
            "Hand clearance validation needs geometry on the hand/forearm bones"
        )
    seated_band = archetype.seated_clearance_m if archetype is not None else None
    perch_band = archetype.perch_seat_height_m if archetype is not None else None
    floor_drop = (
        archetype.seated_floor_drop_m if archetype is not None else 0.41
    )
    reports: dict[str, dict[str, object]] = {}
    for action_name, action in actions.items():
        animation_data.action = action
        if is_seated_action(action_name):
            if leaves_seat_action(action_name):
                continue

            reports[action_name] = validate_seated_clip(
                result,
                action,
                action_name,
                seated_band,
                perch_band,
                floor_drop,
            )
            continue

        contact_gaps: list[float] = []
        lowest_samples: list[float] = []
        hand_samples: list[float] = []
        for frame in range(round(action.frame_start), round(action.frame_end) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            foot_minima = [
                min(evaluated_part_min_z(part, depsgraph) for part in footwear[side])
                for side in ("L", "R")
            ]
            lowest_samples.append(min(foot_minima))
            contact_gaps.append(min(abs(value) for value in foot_minima))
            if hand_band is not None:
                hand_samples.append(
                    min(evaluated_part_min_z(part, depsgraph) for part in hands)
                )
        lowest = min(lowest_samples)
        highest_contact_gap = max(contact_gaps)
        airborne = archetype.airborne_lift_m if archetype is not None else None
        # A baked pelvis correction keeps at least one rigid sole on the
        # pavement at every exported sample without moving the gameplay root.
        if lowest < -0.002:
            raise RuntimeError(
                f"{action_name} footwear penetrates ground at {lowest:.4f} m"
            )
        if airborne is None:
            if highest_contact_gap > 0.002:
                raise RuntimeError(
                    f"{action_name} loses grounded contact by "
                    f"{highest_contact_gap:.4f} m"
                )
        else:
            # An airborne archetype may leave the pavement, but every clip
            # still has to touch down: a clip that never lands is drifting,
            # not hopping.
            lowest_contact_gap = min(contact_gaps)
            if lowest_contact_gap > 0.002:
                raise RuntimeError(
                    f"{action_name} never lands; its closest sole contact is "
                    f"{lowest_contact_gap:.4f} m"
                )
        report: dict[str, object] = {
            "ground_min_m": stable_float(lowest),
            "ground_max_contact_gap_m": stable_float(highest_contact_gap),
        }
        if airborne is not None:
            report["apex_lift_m"] = stable_float(max(lowest_samples))
        if hand_band is not None:
            floor, ceiling = hand_band
            closest_hand = min(hand_samples)
            if closest_hand < floor:
                raise RuntimeError(
                    f"{action_name} hands reach {closest_hand:.4f} m, below the "
                    f"{floor:.3f} m pavement clearance floor"
                )
            if closest_hand > ceiling:
                raise RuntimeError(
                    f"{action_name} hands stay {closest_hand:.4f} m up; the design "
                    f"requires them within {ceiling:.3f} m of the pavement"
                )
            report["hand_min_clearance_m"] = stable_float(closest_hand)
        reports[action_name] = report
    animation_data.action = None
    scene.frame_set(0)
    reset_pose(rig)
    return reports


def evaluated_part_max_z(part: PartRecord, depsgraph) -> float:
    evaluated = part.obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        return max(
            (evaluated.matrix_world @ vertex.co).z
            for vertex in mesh.vertices
        )
    finally:
        evaluated.to_mesh_clear()


def validate_seated_clip(
    result: BuildResult,
    action: bpy.types.Action,
    action_name: str,
    seated_band: tuple[float, float] | None,
    perch_band: tuple[float, float] | None = None,
    floor_drop: float = 0.41,
) -> dict[str, object]:
    """Prove a seated clip against whatever is actually carrying it.

    Sole contact is meaningless here: a seated design deliberately lifts its
    boots off the ground plane, and the runtime aligns the design to its seat
    rather than pinning the lowest sole.

    There are two ways to be seated and they are proved against different
    things. A cabin rider is boxed in, so what can go wrong is vertical: a
    design whose worn objects rise too far above the seated pelvis passes
    through the roof, and one that folds below it passes through the floor.
    A bench sitter has no roof at all, and what can go wrong instead is that
    his boots never reach the ground beside the plank - so the distance from
    the underside of his hips down to his soles is measured, and it has to
    equal the height of the seat he was authored for. Both are measured on
    the real deformed meshes.
    """

    if seated_band is None and perch_band is None:
        raise RuntimeError(
            f"{action_name} is seated but its archetype declares neither a "
            "seated_clearance_m nor a perch_seat_height_m band"
        )
    if seated_band is not None and perch_band is not None:
        # A design with two seats: the clip has to name the one it is on.
        # Whichever it names, exactly one band survives to be proved
        # against below, so a clip is never measured twice or loosely.
        if perched_action(action_name):
            seated_band = None
        else:
            perch_band = None

    scene = bpy.context.scene
    rig = result.rig
    headrooms: list[float] = []
    drops: list[float] = []
    seat_contacts: list[float] = []
    # The underside of the hip geometry alone - the seat of the coat, which is
    # the part that physically rests on a plank. Thighs are excluded here on
    # purpose: on a high bench they slope down towards the knees, so including
    # them would report the knee rather than the seat.
    hip_parts = [part for part in result.parts if part.bone == "pelvis"]
    perch_heights: list[float] = []
    perch_lifts: list[float] = []
    perch_contacts: list[str] = []
    for frame in range(round(action.frame_start), round(action.frame_end) + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        pelvis_z = (rig.matrix_world @ rig.pose.bones["pelvis"].head).z
        top = max(evaluated_part_max_z(part, depsgraph) for part in result.parts)
        bottom = min(
            evaluated_part_min_z(part, depsgraph) for part in result.parts
        )
        seat_parts = [
            part for part in result.parts
            if part.bone in {"pelvis", "thigh.L", "thigh.R"}
        ]
        if seat_parts:
            seat_contacts.append(
                pelvis_z
                - min(evaluated_part_min_z(part, depsgraph) for part in seat_parts)
            )
        if perch_band is not None:
            if not hip_parts:
                raise RuntimeError(
                    "Perch validation needs geometry on the pelvis bone"
                )
            hip_bottom = min(
                evaluated_part_min_z(part, depsgraph) for part in hip_parts
            )
            perch_heights.append(hip_bottom - bottom)
            perch_lifts.append(pelvis_z - hip_bottom)
            # Which part actually reaches the ground. A seated design has
            # two candidates and they are not interchangeable: if the
            # tucked foot outreaches the planted one, the pose reads as a
            # man balanced on one toe with his other boot in the air, and
            # the measurement above silently describes the wrong leg.
            perch_contacts.append(
                min(
                    (
                        (evaluated_part_min_z(part, depsgraph), part.obj.name)
                        for part in result.parts
                    ),
                )[1]
            )
        headrooms.append(top - pelvis_z)
        drops.append(pelvis_z - bottom)

    drop = max(drops)
    if perch_band is not None:
        floor, ceiling = perch_band
        lowest = min(perch_heights)
        highest = max(perch_heights)
        if lowest < floor or highest > ceiling:
            raise RuntimeError(
                f"{action_name} keeps its seat {lowest:.4f}-{highest:.4f} m "
                f"above its own soles; the design is authored for a "
                f"{floor:.3f}-{ceiling:.3f} m seat"
            )
        # How far the pelvis bone rides above the underside of the hips. The
        # runtime lifts the model by exactly this to stand the seat of the
        # coat on the drawn plank instead of sinking it into the timber.
        perch_lift = max(perch_lifts)
        contacts = sorted(set(perch_contacts))
        print(
            f"    perched {action_name}: seat {lowest:.4f}-{highest:.4f} m "
            f"over the soles, pelvis lift {perch_lift:.4f} m, "
            f"ground contact {', '.join(contacts)}"
        )
        return {
            "seated": True,
            "perched": True,
            "perch_seat_height_min_m": stable_float(lowest),
            "perch_seat_height_max_m": stable_float(highest),
            "perch_pelvis_lift_m": stable_float(perch_lift),
            "perch_contact_parts": contacts,
            "seated_drop_m": stable_float(drop),
        }

    floor, ceiling = seated_band
    headroom = max(headrooms)
    print(
        f"    seated {action_name}: {headroom:.4f} m of head over the "
        f"pelvis, {drop:.4f} m of leg under it "
        f"(floor at {floor_drop:.3f} m)"
    )
    if headroom < floor or headroom > ceiling:
        raise RuntimeError(
            f"{action_name} rises {headroom:.4f} m above its seated pelvis; "
            f"the design declares {floor:.3f}-{ceiling:.3f} m"
        )

    # Whatever this design's cushion sits above its own floor, nothing may
    # hang further below the seated pelvis than that.
    if drop > floor_drop:
        raise RuntimeError(
            f"{action_name} hangs {drop:.4f} m below its seated pelvis; "
            f"its cushion is only {floor_drop:.3f} m above the floor, so "
            "it would pass through it"
        )

    # How far the underside of the seated hips and thighs sits below the
    # pelvis bone. The runtime aligns that bone to the cushion anchor, so this
    # is exactly the lift a design needs in order to rest ON the seat instead
    # of sinking into it.
    seat_contact = max(seat_contacts) if seat_contacts else 0.0
    return {
        "seated": True,
        "seated_headroom_m": stable_float(headroom),
        "seated_drop_m": stable_float(drop),
        "seated_contact_m": stable_float(seat_contact),
        "seated_floor_drop_limit_m": stable_float(floor_drop),
    }


def bake_constant_pelvis_offset(
    result: BuildResult,
    actions: dict[str, bpy.types.Action],
) -> None:
    """Lift each airborne clip by one constant offset, not per frame.

    An airborne clip must keep its arc. Correcting the pelvis frame by frame
    would pin the lowest sole to the pavement on every sample and silently
    flatten the hop into a shuffle, so the whole clip is raised by the single
    offset that grounds its lowest frame.
    """

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    footwear = [part for part in result.parts if part.bone in {"foot.L", "foot.R"}]
    if not footwear:
        raise RuntimeError("Grounding bake needs footwear geometry")
    for action in actions.values():
        animation_data.action = action
        frames = list(range(round(action.frame_start), round(action.frame_end) + 1))
        authored: dict[int, float] = {}
        for frame in frames:
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            authored[frame] = min(
                evaluated_part_min_z(part, depsgraph) for part in footwear
            )
        correction = -min(authored.values())
        # Inserting a key at frame N changes how neighbouring frames evaluate,
        # so each frame is driven to its own absolute target instead of having
        # a fixed delta added. Two passes settle the residual.
        for _ in range(2):
            for frame in frames:
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                depsgraph = bpy.context.evaluated_depsgraph_get()
                current = min(
                    evaluated_part_min_z(part, depsgraph) for part in footwear
                )
                pelvis = rig.pose.bones["pelvis"]
                pose_matrix = pelvis.matrix.copy()
                pose_matrix.translation.z += authored[frame] + correction - current
                pelvis.matrix = pose_matrix
                pelvis.keyframe_insert("location", frame=frame, group="pelvis")
            for curve in iter_action_fcurves(action):
                if curve.data_path == 'pose.bones["pelvis"].location':
                    for keyframe in curve.keyframe_points:
                        keyframe.interpolation = "LINEAR"
    animation_data.action = None
    scene.frame_set(0)
    reset_pose(rig)


def bake_grounded_pelvis(
    result: BuildResult,
    actions: dict[str, bpy.types.Action],
) -> None:
    """Bake per-frame pelvis lift so the lower sole touches z=0 exactly."""

    scene = bpy.context.scene
    rig = result.rig
    animation_data = rig.animation_data_create()
    footwear = [part for part in result.parts if part.bone in {"foot.L", "foot.R"}]
    if not footwear:
        raise RuntimeError("Grounding bake needs footwear geometry")
    for action in actions.values():
        animation_data.action = action
        corrections: list[tuple[int, float]] = []
        for frame in range(round(action.frame_start), round(action.frame_end) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            lowest = min(evaluated_part_min_z(part, depsgraph) for part in footwear)
            corrections.append((frame, -lowest))
        for frame, correction in corrections:
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            pelvis = rig.pose.bones["pelvis"]
            pose_matrix = pelvis.matrix.copy()
            pose_matrix.translation.z += correction
            pelvis.matrix = pose_matrix
            pelvis.keyframe_insert("location", frame=frame, group="pelvis")
        for curve in iter_action_fcurves(action):
            if curve.data_path == 'pose.bones["pelvis"].location':
                for keyframe in curve.keyframe_points:
                    keyframe.interpolation = "LINEAR"
        # Changing a key at frame N can alter the evaluated basis originally
        # sampled at later frames, so perform one deterministic residual pass.
        for frame in range(round(action.frame_start), round(action.frame_end) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            lowest = min(evaluated_part_min_z(part, depsgraph) for part in footwear)
            pose_matrix = rig.pose.bones["pelvis"].matrix.copy()
            pose_matrix.translation.z -= lowest
            rig.pose.bones["pelvis"].matrix = pose_matrix
            rig.pose.bones["pelvis"].keyframe_insert("location", frame=frame, group="pelvis")
    animation_data.action = None
    scene.frame_set(0)
    reset_pose(rig)


def actions_for_archetype(spec: ArchetypeSpec) -> tuple[ActionSpec, ...]:
    return tuple(item for item in ACTION_SPECS if item.archetype == spec.design_id)


ACTION_BY_NAME = {spec.name: spec for spec in ACTION_SPECS}


def is_seated_action(name: str) -> bool:
    spec = ACTION_BY_NAME.get(name)
    return spec is not None and spec.seated


def perched_action(name: str) -> bool:
    spec = ACTION_BY_NAME.get(name)
    return spec is not None and spec.perched


def leaves_seat_action(name: str) -> bool:
    spec = ACTION_BY_NAME.get(name)
    return spec is not None and spec.leaves_seat


def capture_pelvis_track(action: bpy.types.Action) -> list[tuple[int, tuple[float, float, float]]]:
    """Read the baked pelvis location channel as plain per-frame data.

    Every archetype is built in its own factory-reset scene, so a baked
    Action cannot survive until the shared library is assembled. Capturing
    the local pelvis basis is enough to reproduce the bake exactly, because
    the rest of the pose comes from the same deterministic `animation_keys`.
    """

    curves = {
        curve.array_index: curve
        for curve in iter_action_fcurves(action)
        if curve.data_path == 'pose.bones["pelvis"].location'
    }
    if not curves:
        raise RuntimeError(f"{action.name} has no baked pelvis location channel")
    frames = sorted(
        {
            round(keyframe.co.x)
            for curve in curves.values()
            for keyframe in curve.keyframe_points
        }
    )
    return [
        (
            frame,
            tuple(
                stable_float(curves[axis].evaluate(frame)) if axis in curves else 0.0
                for axis in range(3)
            ),
        )
        for frame in frames
    ]


def apply_pelvis_track(
    rig: bpy.types.Object,
    action: bpy.types.Action,
    track: Sequence[tuple[int, tuple[float, float, float]]],
) -> None:
    """Re-key a captured pelvis bake onto a freshly authored Action."""

    scene = bpy.context.scene
    animation_data = rig.animation_data_create()
    animation_data.action = action
    pelvis = rig.pose.bones["pelvis"]
    for frame, location in track:
        scene.frame_set(frame)
        pelvis.location = location
        pelvis.keyframe_insert("location", frame=frame, group="pelvis")
    for curve in iter_action_fcurves(action):
        if curve.data_path == 'pose.bones["pelvis"].location':
            for keyframe in curve.keyframe_points:
                keyframe.interpolation = "LINEAR"
    animation_data.action = None
    scene.frame_set(0)
    reset_pose(rig)


def ground_actions_per_archetype(
    keys: dict[str, tuple[tuple[float, dict[str, BonePose]], ...]],
) -> tuple[dict[str, list[tuple[int, tuple[float, float, float]]]], dict[str, dict[str, object]]]:
    """Bake and verify every clip against its own archetype's footwear.

    A clip grounded against another design's boots is not grounded at all:
    each archetype owns a different sole height, length and deformation, so
    the pelvis correction and the contact proof must both come from the model
    that actually plays the clip.
    """

    pelvis_tracks: dict[str, list[tuple[int, tuple[float, float, float]]]] = {}
    grounding: dict[str, dict[str, object]] = {}
    for spec in ARCHETYPES.values():
        owned = actions_for_archetype(spec)
        if not owned:
            raise RuntimeError(f"{spec.design_id} owns no locomotion Actions")
        result = PedestrianBuilder(spec).build()
        actions = {
            action_spec.name: create_action(result.rig, action_spec, keys[action_spec.name])
            for action_spec in owned
        }
        # A seated clip is never sole-pinned: the runtime aligns its pelvis to
        # the cushion, so baking it against the pavement would fold the pose.
        grounded_actions = {
            name: action for name, action in actions.items()
            if not is_seated_action(name)
        }
        if spec.wheel_radius_m is not None:
            pass
        elif spec.airborne_lift_m is None:
            bake_grounded_pelvis(result, grounded_actions)
        else:
            bake_constant_pelvis_offset(result, grounded_actions)
        reports = validate_animated_grounding(result, actions, spec)
        grounding.update(reports)
        if spec.airborne_lift_m is not None:
            floor, ceiling = spec.airborne_lift_m
            apex = max(
                float(report.get("apex_lift_m", 0.0)) for report in reports.values()
            )
            if not floor <= apex <= ceiling:
                raise RuntimeError(
                    f"{spec.design_id} reaches a {apex:.4f} m apex; the design "
                    f"requires {floor:.3f}-{ceiling:.3f} m in at least one clip"
                )
            print(f"  airborne {spec.design_id}: {apex:.3f} m apex lift")
        for name, action in actions.items():
            pelvis_tracks[name] = capture_pelvis_track(action)
        support = "its own tyres" if spec.wheel_radius_m is not None else "its own footwear"
        print(
            f"  grounded {spec.design_id}: "
            f"{', '.join(sorted(actions))} against {support}"
        )
    missing = [spec.name for spec in ACTION_SPECS if spec.name not in pelvis_tracks]
    if missing:
        raise RuntimeError(f"Clips never grounded against an archetype: {missing}")
    return pelvis_tracks, grounding


def export_animation_fbx(path: Path, result: BuildResult) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    result.root.select_set(True)
    result.rig.select_set(True)
    bpy.context.view_layer.objects.active = result.rig
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"EMPTY", "ARMATURE"},
        axis_forward="-Z", axis_up="Y", add_leaf_bones=False, bake_anim=True,
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        use_armature_deform_only=False, use_custom_props=True,
    )


def write_animation_manifest(path: Path, signature: str, clips: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "generator": "tools/build-city-pedestrian-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "skeleton_source": "PlayerCharacter3D exact A-pose v2.5.0",
        "bone_count": len(SKELETON),
        "fps": ANIMATION_FPS,
        "root_motion": False,
        "mesh_count": 0,
        "clip_count": len(clips),
        "clips": clips,
        "build_signature": signature,
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def setup_review_stage(result: BuildResult) -> tuple[bpy.types.Object, bpy.types.Object]:
    scene = bpy.context.scene
    presentation = bpy.data.collections.get("PRESENTATION_CityPedestrian")
    if presentation is None:
        raise RuntimeError("Animation review presentation collection is missing")
    camera_data = bpy.data.cameras.new("CAM_LocomotionReview")
    camera = bpy.data.objects.new("CAM_LocomotionReview", camera_data)
    presentation.objects.link(camera)
    camera.location = (2.45, -4.55, 1.85)
    camera.rotation_euler = (Vector((0, 0, 0.90)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 62
    scene.camera = camera
    for name, location, energy, color, radius in (
        ("ReviewKey", (-2.2, -3.2, 4.0), 850.0, (0.72, 0.82, 0.72), 3.0),
        ("ReviewRim", (2.5, 1.0, 3.0), 500.0, (0.35, 0.48, 0.42), 2.0),
    ):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = radius
        light = bpy.data.objects.new(name, data)
        presentation.objects.link(light)
        light.location = location
        light.rotation_euler = (Vector((0, 0, 0.90)) - light.location).to_track_quat("-Z", "Y").to_euler()
    vertices, faces = make_box((0, 0.25, -0.035), (4.0, 4.0, 0.07))
    ground_mesh = bpy.data.meshes.new("ReviewGround_Mesh")
    ground_mesh.from_pydata(vertices, [], faces)
    ground = bpy.data.objects.new("ReviewGround", ground_mesh)
    presentation.objects.link(ground)
    material = bpy.data.materials.new("MAT_ReviewGround")
    material.diffuse_color = (0.025, 0.040, 0.034, 1)
    ground.data.materials.append(material)
    scene.render.resolution_x = 320
    scene.render.resolution_y = 400
    scene.render.resolution_percentage = 100
    return camera, ground


TILE_WIDTH = 320
TILE_HEIGHT = 400
SHEET_COLUMNS = 3


def contact_sheet_samples() -> tuple[tuple[str, str, int], ...]:
    """One row per archetype: idle plus two opposite walk phases."""

    samples: list[tuple[str, str, int]] = []
    for key, spec in ARCHETYPES.items():
        walk = next(
            item for item in ACTION_SPECS if item.name == spec.walk_clip
        )
        samples.append((key, spec.idle_clip, 0))
        samples.append((key, spec.walk_clip, 0))
        samples.append((key, spec.walk_clip, round(walk.frame_end * 0.5)))
    return tuple(samples)


def render_animation_contact_sheet(
    path: Path,
    source_dir: Path,
) -> None:
    """Render one review row per archetype into a single contact sheet."""

    path.parent.mkdir(parents=True, exist_ok=True)
    tiles: list[Path] = []
    samples = contact_sheet_samples()
    keys = animation_keys()
    for index, (archetype_key, action_name, frame) in enumerate(samples):
        # Rebuilding swaps the actual production meshes while the action poses
        # are re-authored deterministically from the same source definitions.
        # Only the archetype's own clips are baked, so the review frame shows
        # the same grounding the exported library carries.
        spec = ARCHETYPES[archetype_key]
        result = PedestrianBuilder(spec).build()
        local_actions = {
            action_spec.name: create_action(result.rig, action_spec, keys[action_spec.name])
            for action_spec in actions_for_archetype(spec)
        }
        grounded_actions = {
            name: action for name, action in local_actions.items()
            if not is_seated_action(name)
        }
        if spec.wheel_radius_m is not None:
            pass
        elif spec.airborne_lift_m is None:
            bake_grounded_pelvis(result, grounded_actions)
        else:
            bake_constant_pelvis_offset(result, grounded_actions)
        setup_review_stage(result)
        result.rig.animation_data_create().action = local_actions[action_name]
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        tile = source_dir / f".locomotion-review-{index}.png"
        bpy.context.scene.render.filepath = str(tile)
        bpy.ops.render.render(write_still=True)
        tiles.append(tile)

    rows = math.ceil(len(tiles) / SHEET_COLUMNS)
    width = SHEET_COLUMNS * TILE_WIDTH
    height = rows * TILE_HEIGHT
    sheet = bpy.data.images.new(
        "CityPedestrianLocomotionContactSheet", width, height
    )
    pixels = [0.008, 0.012, 0.010, 1.0] * (width * height)
    for index, tile in enumerate(tiles):
        tile_image = bpy.data.images.load(str(tile), check_existing=False)
        tile_pixels = list(tile_image.pixels)
        column = index % SHEET_COLUMNS
        row = index // SHEET_COLUMNS
        # Blender image rows run bottom-up, so the first archetype row has to
        # land at the top of the sheet.
        destination_y = (rows - 1 - row) * TILE_HEIGHT
        for y in range(TILE_HEIGHT):
            source_start = y * TILE_WIDTH * 4
            destination_start = (
                (destination_y + y) * width + column * TILE_WIDTH
            ) * 4
            pixels[destination_start : destination_start + TILE_WIDTH * 4] = (
                tile_pixels[source_start : source_start + TILE_WIDTH * 4]
            )
        bpy.data.images.remove(tile_image)
    sheet.pixels = pixels
    sheet.filepath_raw = str(path)
    sheet.file_format = "PNG"
    sheet.save()
    for tile in tiles:
        try:
            tile.unlink()
        except FileNotFoundError:
            pass


def build_animation_library(config: argparse.Namespace) -> None:
    # Ground every clip against the model that actually plays it, then reuse a
    # freshly validated canonical rig, remove every model mesh, and author and
    # export only ROOT_Player + RIG_Player + bone Actions.
    keys = animation_keys()
    pelvis_tracks, grounding = ground_actions_per_archetype(keys)
    result = PedestrianBuilder(ARCHETYPES["lampshade"]).build()
    model_parts = list(result.parts)
    actions = {
        spec.name: create_action(result.rig, spec, keys[spec.name])
        for spec in ACTION_SPECS
    }
    for name, action in actions.items():
        apply_pelvis_track(result.rig, action, pelvis_tracks[name])
    for part in model_parts:
        bpy.data.objects.remove(part.obj, do_unlink=True)
    result.parts.clear()
    result.root["bp_design_id"] = "city_pedestrian_locomotion_v1"
    result.root["bp_animation_only"] = True
    result.root["bp_root_motion"] = False
    signature, clips = validate_animation_library(result.rig, actions, grounding)
    fbx_path = config.animation_dir / "CityPedestrianLocomotion.fbx"
    manifest_path = config.animation_dir / "CityPedestrianLocomotion.json"
    blend_path = config.source_dir / "CityPedestrianLocomotion.blend"
    export_animation_fbx(fbx_path, result)
    write_animation_manifest(manifest_path, signature, clips)
    save_blend(blend_path)
    if not config.no_preview:
        render_animation_contact_sheet(
            config.source_dir / "CityPedestrianLocomotionContactSheet.png",
            config.source_dir,
        )
    print(f"  locomotion: {len(actions)} Actions, 31 keyed bones, no meshes/root motion")
    print(f"    Signature: {signature}")
    print(f"    FBX: {fbx_path}")


def main() -> None:
    config = parse_args()
    selected = (
        tuple(ARCHETYPES.values())
        if config.archetype == "all"
        else (ARCHETYPES[config.archetype],)
    )
    print("CITY PEDESTRIAN ART BUILD")
    print(f"  Blender: {bpy.app.version_string}")
    reports: list[tuple[ArchetypeSpec, ValidationReport]] = []
    for spec in selected:
        result = PedestrianBuilder(spec).build()
        report = validate_result(result, spec)
        blend_path = config.source_dir / spec.blend_name
        model_dir = config.staged_model_dir if spec.staged else config.model_dir
        fbx_path = model_dir / f"{spec.model_name}.fbx"
        manifest_path = model_dir / f"{spec.model_name}.json"
        preview_path = config.source_dir / spec.preview_name
        if not config.no_preview:
            render_preview(preview_path, result, spec)
        export_fbx(fbx_path, result)
        write_manifest(manifest_path, result, report, spec)
        save_blend(blend_path)
        reports.append((spec, report))
        print(f"  {spec.design_id}: {report.mesh_count} meshes, {report.triangle_count} triangles")
        print(f"    Signature: {report.build_signature}")
        print(f"    Blend: {blend_path}")
        print(f"    FBX: {fbx_path}")
    if config.archetype == "all":
        build_animation_library(config)
        first_signatures = {
            spec.design_id: report.build_signature for spec, report in reports
        }
        # A second model-only build proves that source geometry and manifests
        # remain deterministic within the same Blender process.
        for spec, first_report in reports:
            rerun = PedestrianBuilder(spec).build()
            rerun_report = validate_result(rerun, spec)
            if rerun_report.build_signature != first_report.build_signature:
                raise RuntimeError(
                    f"Non-deterministic build signature for {spec.design_id}: "
                    f"{first_signatures[spec.design_id]} != {rerun_report.build_signature}"
                )
        print("  Determinism: repeated model signatures match")
    print("CITY PEDESTRIAN ART BUILD OK")


if __name__ == "__main__":
    main()
