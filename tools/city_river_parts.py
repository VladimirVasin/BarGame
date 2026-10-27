"""Passive metre-authored river fittings for the shared City misc catalog.

The caller supplies its established source-space primitives and catalog types.
All positions below are Unity local metres: X right, Y up, Z forward. Coping
and stair origins name the existing physical top; those skins stay below it.
No fitting owns collision, water, a lamp lens, an interaction or a material.
"""

from __future__ import annotations


KINDS = (
    "RiverQuayRepair", "RiverWorksFitting", "RiverMouthCap",
    "RiverTimberJoint", "RiverMooringBollard", "RiverLanternShell",
    "RiverStairNosing",
)
PART_SUFFIXES = {
    "RiverQuayRepair": ("Coping_Masonry_Stone", "Stitches_Street_PaintedMetal"),
    "RiverWorksFitting": ("PlateAndBolts_Street_PaintedMetal",),
    "RiverMouthCap": ("Coping_Masonry_Stone",),
    "RiverTimberJoint": ("CollarAndBolts_Street_PaintedMetal",),
    "RiverMooringBollard": ("Casting_Street_PaintedMetal",),
    "RiverLanternShell": ("OpenHousing_Street_PaintedMetal",),
    "RiverStairNosing": ("WornNosing_Masonry_Stone",),
}
PART_ROLES = {
    kind: tuple("Masonry_Stone" if "Masonry_Stone" in suffix
                else "Street_PaintedMetal" for suffix in suffixes)
    for kind, suffixes in PART_SUFFIXES.items()
}
# Unity min/max bounds are a placement contract, not a visual sample.
BOUNDS = {
    "RiverQuayRepair": ((-.28, -.10, -.60), (.28, 0., .60)),
    "RiverWorksFitting": ((-.19, 0., -.19), (.19, .18, .19)),
    "RiverMouthCap": ((-.60, -.10, -.23), (.60, 0., .23)),
    "RiverTimberJoint": ((-.11, 0., -.11), (.11, .18, .11)),
    "RiverMooringBollard": ((-.22, 0., -.22), (.22, .52, .22)),
    "RiverLanternShell": ((-.18, -.16, -.18), (.18, .24, .18)),
    "RiverStairNosing": ((-.50, -.05, -.04), (.50, 0., .04)),
}


def _part(api, kind, index, solids):
    # Validate each closed piece before merging. An inward bolt must not hide
    # behind the larger positive volume of its base plate.
    for geometry in solids:
        if api.signed_volume(geometry) <= 1e-10:
            raise ValueError(f"{kind} contains a non-positive closed solid")
    return api.make_named_part(kind, 0, PART_SUFFIXES[kind][index],
                               PART_ROLES[kind][index], *solids)


def _assembly(api, kind, parts):
    low, high = BOUNDS[kind]
    return api.AssemblySpec(
        kind, 0, tuple(parts),
        root_derivation=f"CityRiverPlan.{kind}.AuthoredTopOrFixtureFrame",
        placement_contract=("anchor_forward_frame" if kind == "RiverLanternShell"
                            else "ground_forward_frame"),
        coordinate_profile="root_local_direct",
        expected_source_min_z=low[1],
        canonical_reference=(
            ("width_m", high[0] - low[0]),
            ("height_m", high[1] - low[1]),
            ("depth_m", high[2] - low[2])),
        unity_owned_parts=("Placement", "Collision", "SharedSurface", "LightState"),
    )


def make_assemblies(api):
    box, tube = api.local_box, api.local_tube
    # Quay coping: separately dressed short stones, a recessed bed and two
    # flush cramps across the repair seam. The material stays the local quay.
    kind = "RiverQuayRepair"
    stone = [
        box(0, -.072, 0, .52, .056, 1.16, .008),
        # The cramps stand 2 mm proud of dressed stone; equal top planes
        # would flicker where the metal crosses either side of the joint.
        box(0, -.027, -.200, .56, .050, .800, .009),
        box(0, -.027, .403, .56, .050, .394, .013),
    ]
    stitches = [box(x, -.004, .203, .028, .008, .18, .003)
                for x in (-.175, .175)]
    yield _assembly(api, kind, (_part(api, kind, 0, stone),
                                _part(api, kind, 1, stitches)))

    # Works bridge: the square sole plate has an inset web, two gussets and
    # six-sided bolt heads; this reads as an actual fastened rail support.
    kind = "RiverWorksFitting"
    iron = [box(0, .016, 0, .38, .032, .38, .014),
            box(0, .106, 0, .16, .148, .16, .009)]
    for x in (-.13, .13):
        for z in (-.13, .13):
            iron.append(api.local_vertical_solid(x, .032, .060, z,
                                                 .027, .027, sides=6))
    for side in (-1, 1):
        iron.append(tube((side * .132, .035, 0),
                         (side * .073, .139, 0), .020, sides=4))
    yield _assembly(api, kind, (_part(api, kind, 0, iron),))

    # Mouth bridge: broad dressed granite with a narrower underside creates
    # a drip edge without adding a rail or concealing the road profile.
    kind = "RiverMouthCap"
    stone = [box(0, -.073, 0, 1.14, .054, .40, .008),
             box(0, -.027, 0, 1.20, .054, .46, .012)]
    yield _assembly(api, kind, (_part(api, kind, 0, stone),))

    # Timber bridge: open sleeve fits the existing 130 mm post; no hidden
    # solid core cuts through its timber. The through-bolts clear the sleeve.
    kind = "RiverTimberJoint"
    iron = []
    for side in (-1, 1):
        iron.extend((
            box(side * .083, .09, 0, .018, .18, .184, .004),
            box(0, .09, side * .083, .184, .18, .018, .004),
            tube((side * .085, .09, -.108),
                 (side * .085, .09, .108), .014, sides=6),
            tube((-.108, .09, side * .085),
                 (.108, .09, side * .085), .014, sides=6),
        ))
    # Flush end washers fix the footprint exactly at 220 mm.
    for side in (-1, 1):
        iron.extend((box(side * .103, .09, 0, .014, .058, .058, .004),
                     box(0, .09, side * .103, .058, .058, .014, .004)))
    yield _assembly(api, kind, (_part(api, kind, 0, iron),))

    # A turned cast-iron mooring bollard: foot flange, tapered neck and
    # mushroom head with twin horn shoulders. No rope or object enters water.
    kind = "RiverMooringBollard"
    iron = [box(0, .026, 0, .44, .052, .44, .026),
            api.ring_solid_z((
                (.045, 0, 0, .16, .16, 0),
                (.085, 0, 0, .16, .16, 0),
                (.115, 0, 0, .115, .115, 0),
                (.350, 0, 0, .105, .105, 0),
                (.405, 0, 0, .145, .125, 0),
                (.470, 0, 0, .170, .145, 0),
                (.510, 0, 0, .145, .125, 0),
                (.520, 0, 0, .115, .100, 0),
            ), sides=12),
            tube((-.17, .402, 0), (.17, .402, 0), .045, sides=8)]
    for x in (-.152, .152):
        for z in (-.152, .152):
            iron.append(api.local_vertical_solid(x, .052, .079, z,
                                                 .025, .025, sides=6))
    yield _assembly(api, kind, (_part(api, kind, 0, iron),))

    # Open lantern cage. Its origin is the runtime lens centre. The light,
    # lens and soft halo remain with their existing shared presentation owner.
    kind = "RiverLanternShell"
    iron = [box(0, -.145, 0, .36, .030, .36, .011),
            box(0, .145, 0, .36, .030, .36, .010),
            api.ring_solid_z((
                (.160, 0, 0, .18, .18, .7853981634),
                (.215, 0, 0, .10, .10, .7853981634),
                (.240, 0, 0, .035, .035, .7853981634),
            ), sides=4)]
    for x in (-.145, .145):
        for z in (-.145, .145):
            iron.append(box(x, -.002, z, .025, .256, .025, .004))
    yield _assembly(api, kind, (_part(api, kind, 0, iron),))

    # The nosing follows the old tread top. A shallow eased lip and unequal
    # side patches give the step a dressed front without a raised obstacle.
    kind = "RiverStairNosing"
    stone = [box(0, -.035, -.005, 1.0, .030, .07, .006),
             box(-.115, -.010, .002, .77, .020, .076, .005),
             box(.388, -.010, .002, .224, .020, .076, .004)]
    yield _assembly(api, kind, (_part(api, kind, 0, stone),))


def validate(api, assemblies, problems):
    by_kind = {assembly.kind: assembly for assembly in assemblies
               if assembly.kind in KINDS}
    for kind in KINDS:
        assembly = by_kind.get(kind)
        if assembly is None:
            problems.append(f"River kit is missing {kind}")
            continue
        low, high = api.combined_bounds(assembly.parts)
        actual = (api.source_to_unity(low), api.source_to_unity(high))
        for observed, expected in zip(actual, BOUNDS[kind]):
            if any(abs(a - b) > api.BOUNDS_EPSILON
                   for a, b in zip(observed, expected)):
                problems.append(f"{kind} fixed metre bounds drifted: {actual}")
