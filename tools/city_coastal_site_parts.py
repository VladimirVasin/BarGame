"""Bounded cemetery entrance and closed boat-station joinery.

Unity-local metres, passive meshes only. Existing plans retain all collision,
door poses, water, lamps, actor docks and the single-sided pier railing.
"""

from __future__ import annotations

KINDS = (
    "CemeteryLodgeShell", "CoastalDoorLeaf", "CemeteryGatePillar",
    "CemeteryGateLeaf", "CemeteryGateArch", "SeacoastHutShell",
    "SeacoastPierDeckBoard", "SeacoastPierPile", "SeacoastTimberJoint",
    "SeacoastEsplanadeCoping",
)
PART_SUFFIXES = {
    "CemeteryLodgeShell": ("Plaster_Masonry_Stone", "Joinery_Timber", "Roof_Timber", "Hardware_Street_PaintedMetal"),
    "CoastalDoorLeaf": ("Panel_Timber", "Straps_Street_PaintedMetal"),
    "CemeteryGatePillar": ("DressedStone_Masonry_Stone",),
    "CemeteryGateLeaf": ("OpenIronwork_Street_PaintedMetal",),
    "CemeteryGateArch": ("ForgedFrame_Street_PaintedMetal",),
    "SeacoastHutShell": ("Plaster_Masonry_Stone", "Joinery_Timber", "Hardware_Street_PaintedMetal"),
    "SeacoastPierDeckBoard": ("Planks_Timber", "Nails_Street_PaintedMetal"),
    "SeacoastPierPile": ("Pile_Timber", "Band_Street_PaintedMetal"),
    "SeacoastTimberJoint": ("Sleeve_Street_PaintedMetal",),
    "SeacoastEsplanadeCoping": ("DressedStone_Masonry_Stone",),
}
PART_ROLES = {
    kind: tuple("Masonry_Stone" if "Masonry_Stone" in suffix else
                "Timber" if suffix.endswith("_Timber") else
                "Street_PaintedMetal" for suffix in suffixes)
    for kind, suffixes in PART_SUFFIXES.items()
}
VARIANTS = {kind: 2 if kind == "CemeteryLodgeShell" else 1 for kind in KINDS}
MAX_TRIANGLES = 16000


def _mirror(geometry):
    vertices, faces = geometry
    return [(-x, z, y) for x, z, y in vertices], [tuple(reversed(face)) for face in faces]


def _assembly(api, kind, groups, variant=0, references=()):
    parts = []
    for index, solids in enumerate(groups):
        for solid in solids:
            if api.signed_volume(solid) <= 1e-10:
                raise ValueError(f"{kind} contains an inward or degenerate solid")
        if variant:
            solids = [_mirror(solid) for solid in solids]
        parts.append(api.make_named_part(kind, variant, PART_SUFFIXES[kind][index],
                                        PART_ROLES[kind][index], *solids))
    return api.AssemblySpec(
        kind, variant, tuple(parts), coordinate_profile="root_local_direct",
        root_derivation="CityCemeteryPlan/CitySeacoastPlan.ExistingDescriptorFrame",
        canonical_reference=tuple(references), expected_source_min_z=0.,
        unity_owned_parts=("Collision", "DoorPose", "ActorDocks", "Water", "LightState"))


def make_assemblies(api):
    box, tube = api.local_box, api.local_tube
    # The lodge follows the exact existing 2.4 x 2.2 m wall footprint. Its
    # watch window and rear door are real voids, with recessed joinery, rather
    # than black planes on a box. Both handed layouts are authored, so no
    # reflected combine matrix can invert normals at runtime.
    plaster = [box(0, 1.225, -1.04, 2.4, 2.25, .12, .008),
               box(1.14, 1.225, 0, .12, 2.25, 2.2, .008),
               box(-.52, 1.225, 1.04, 1.36, 2.25, .12, .008),
               box(.62, 2.235, 1.04, .92, .23, .12, .007),
               box(-1.14, 1.225, -.8, .12, 2.25, .60, .007),
               box(-1.14, 1.225, .8, .12, 2.25, .60, .007),
               box(-1.14, .525, 0, .12, .85, 1, .006),
               box(-1.14, 2.1, 0, .12, .5, 1, .006)]
    timber = [box(0, .05, 0, 2.4, .1, 2.2, .01),
              box(-1.16, .975, 0, .18, .05, 1.12, .006),
              box(.62, .05, 1.275, .70, .1, .35, .009)]
    for z in (-.47, .47):
        timber.append(box(-1.145, 1.42, z, .11, .86, .045, .004))
    for y in (1.005, 1.835):
        timber.append(box(-1.145, y, 0, .11, .045, .94, .004))
    for x in (.18, 1.06):
        timber.append(box(x, 1.105, 1.055, .038, 1.94, .14, .004))
    timber.append(box(.62, 2.065, 1.055, .92, .038, .14, .004))
    # A simple stool at the existing watchman's place, with a real seat and
    # four legs; no new sittable offer or occupant is introduced.
    timber.append(box(-.4, .445, 1.35, .34, .07, .34, .01))
    for x in (-.52, -.28):
        for z in (1.23, 1.47):
            timber.append(box(x, .205, z, .045, .41, .045, .004))
    roof = []
    for index in range(13):
        x = -1.45 + (index + .5) * 2.9 / 13
        roof.append(box(x, 2.42, 0, 2.9 / 13 - .004, .14, 2.7, .006))
    # Fascia and underside runners stay within the original roof envelope.
    for z in (-1.30, 1.30):
        roof.append(box(0, 2.387, z, 2.86, .06, .07, .006))
    metal = [api.local_vertical_solid(.90, 2.42, 2.97, .85, .07, .07, sides=10)]
    for y in (2.49, 2.72):
        metal.append(box(.90, y, .85, .145, .025, .145, .004))
    for variant in range(2):
        yield _assembly(api, "CemeteryLodgeShell", (plaster, timber, roof, metal), variant,
                        (("floor_width_m", 2.4), ("floor_depth_m", 2.2),
                         ("door_width_m", .92), ("window_width_m", 1.0)))

    # Unit leaf: X is width, Y runs 0..1, Z is thickness. Runtime fits the
    # original descriptor, including the two different ajar door poses.
    wood = [box(x, .5, 0, .10, 1, 1, .012) for x in (-.45, .45)]
    wood += [box(0, y, 0, .8, .10, 1, .012) for y in (.05, .95)]
    wood += [box(0, .5, -.08, .80, .80, .70, .018)]
    iron = [box(x, y, .40, .13, .035, .20, .005)
            for x in (-.35, .35) for y in (.18, .82)]
    # The shallow raised latch is contained in the plan's old leaf thickness.
    iron.append(box(.30, .49, .455, .065, .12, .09, .006))
    yield _assembly(api, "CoastalDoorLeaf", (wood, iron))

    stone = [box(0, .06, 0, 1, .12, 1, .02),
             box(0, .51, 0, .84, .78, .84, .012),
             box(0, .92, 0, 1, .08, 1, .018),
             box(0, .98, 0, .91, .04, .91, .012)]
    yield _assembly(api, "CemeteryGatePillar", (stone,))

    # A transparent unit gate, local X thickness and Z leaf length. The
    # collision remains the old complete leaf, but there is no opaque sheet.
    gate = [box(0, y, 0, 1, .035, 1, .006) for y in (.0175, .9825)]
    gate += [box(0, .5, z, 1, .93, .027, .004) for z in (-.4865, .4865)]
    for i in range(7):
        gate.append(box(0, .50, -.39 + i * .13, .45, .90, .014, .002))
    for y in (.28, .73):
        gate.append(box(0, y, 0, .65, .018, .96, .003))
    yield _assembly(api, "CemeteryGateLeaf", (gate,))

    arch = [box(0, y, 0, 1, .10, 1, .012) for y in (.05, .95)]
    for z in (-.48, -.24, 0, .24, .48):
        arch.append(box(0, .5, z, .65, .8, .018, .004))
    # Shallow rising lower chord gives the old beam an ironwork profile
    # without lowering its existing physical clearance.
    for side in (-1, 1):
        arch.append(tube((0, .18, side * .47), (0, .72, 0), .035, sides=6))
    yield _assembly(api, "CemeteryGateArch", (arch,))

    # Rental hut: retained footprint, central 860 mm door and roof overhang.
    # A small boarded service hatch occupies the left jamb, never the door.
    plaster = [box(0, 1.335, -1.71, 2.8, 2.35, .18, .012),
               box(-1.31, 1.335, 0, .18, 2.35, 3.6, .012),
               box(1.31, 1.335, 0, .18, 2.35, 3.6, .012),
               box(.915, 1.335, 1.71, .97, 2.35, .18, .010),
               box(0, 2.31, 1.71, .86, .4, .18, .009),
               box(-1.285, 1.335, 1.71, .23, 2.35, .18, .008),
               box(-.545, 1.335, 1.71, .23, 2.35, .18, .008),
               box(-.915, .685, 1.71, .51, 1.05, .18, .008),
               box(-.915, 2.285, 1.71, .51, .45, .18, .008),
               box(0, .08, 2.02, 1.26, .16, .44, .012)]
    joinery = [box(0, .08, 0, 3.04, .16, 3.84, .01)]
    for i in range(15):
        joinery.append(box(-1.62 + (i + .5) * 3.24 / 15,
                           2.60, .24, 3.24 / 15 - .004, .18, 4.52, .006))
    for z in (-1.96, 2.44):
        joinery.append(box(0, 2.555, z, 3.20, .07, .07, .005))
    for x in (-.407, .407):
        joinery.append(box(x, 1.135, 1.735, .045, 1.95, .14, .005))
    joinery.append(box(0, 2.09, 1.735, .86, .045, .14, .005))
    for x in (-1.15, -.68):
        joinery.append(box(x, 1.635, 1.73, .04, .84, .14, .004))
    for y in (1.235, 2.035):
        joinery.append(box(-.915, y, 1.73, .51, .04, .14, .004))
    for y, angle in ((1.47, 7), (1.77, -5)):
        joinery.append(box(-.915, y, 1.825, .70, .14, .05, .006,
                           roll_forward_degrees=angle))
    hardware = [api.local_vertical_solid(.784, 2.66, 3.60, -.9, .08, .08, sides=10)]
    for x in (-1.16, -.67):
        for y in (1.47, 1.77):
            hardware.append(box(x, y, 1.855, .018, .018, .012, .002))
    yield _assembly(api, "SeacoastHutShell", (plaster, joinery, hardware), references=(
        ("floor_width_m", 3.04), ("floor_depth_m", 3.84),
        ("door_width_m", .86), ("wall_height_m", 2.35)))

    # Existing broad deck descriptors become three close-set boards, each
    # with dressed edges and flush nails. Their unchanged top is walkable.
    deck = []
    for i in range(3):
        start = -.5 + i / 3 + (.003 if i else 0)
        end = -.5 + (i + 1) / 3 - (.003 if i < 2 else 0)
        deck.append(box(0, .5, (start + end) * .5, 1, 1, end - start, .012))
    nails = [box(x, .988, z, .012, .024, .022, .003)
             for x in (-.42, .42) for z in (-1/3, 0, 1/3)]
    yield _assembly(api, "SeacoastPierDeckBoard", (deck, nails))
    pile = [api.local_vertical_solid(0, 0, 1, 0, .5, .5, sides=8)]
    # Four separated band faces leave the timber visible and meet its
    # existing envelope. A round pile does not acquire a larger collider.
    band = []
    for side in (-1, 1):
        band += [box(side * .485, .92, 0, .03, .045, .38, .006),
                 box(0, .92, side * .485, .38, .045, .03, .006)]
    yield _assembly(api, "SeacoastPierPile", (pile, band))

    # Fixed 140 x 160 x 140 mm open sleeve below the existing rail cap.
    sleeve = []
    for side in (-1, 1):
        sleeve += [box(side * .061, .08, 0, .018, .16, .14, .003),
                   box(0, .08, side * .061, .104, .16, .018, .003)]
    sleeve += [tube((-.07, .08, 0), (.07, .08, 0), .012, sides=6)]
    yield _assembly(api, "SeacoastTimberJoint", (sleeve,))

    coping = [box(0, .43, 0, 1, .86, .92, .008)]
    for x in (-.251, .251):
        coping.append(box(x, .93, 0, .498, .14, 1, .015))
    yield _assembly(api, "SeacoastEsplanadeCoping", (coping,))


def validate(api, assemblies, problems):
    # The gazebo landing is this package's one deliberate correction to an
    # earlier mesh. Every other prior assembly stays frozen byte-for-byte.
    unchanged = tuple(a for a in assemblies[:134] if a.kind != "ParkBandstand")
    previous = api.hashlib.sha256(api.json.dumps(api.signature_payload(
        unchanged, generator_version="4.13.0"), sort_keys=True,
        separators=(",", ":")).encode("utf-8")).hexdigest()
    if previous != "002065fa8a2053e2dacbf3a6567b06ce18f131ab7ce468f661f32ef17261ad83":
        problems.append("Coastal site package changed a previously authored City misc mesh")
    selected = [a for a in assemblies if a.kind in KINDS]
    total = sum(api.triangle_count(p.geometry) for a in selected for p in a.parts)
    if total > MAX_TRIANGLES:
        problems.append(f"Coastal site additions exceed {MAX_TRIANGLES} triangles: {total}")
    lookup = {(a.kind, a.variant): a for a in selected}
    for kind, count in VARIANTS.items():
        for variant in range(count):
            if (kind, variant) not in lookup:
                problems.append(f"Missing coastal site assembly {kind}/{variant}")
    def crosses_aperture(geometry, plane_axis, interval, sample_axes, point):
        """Intersect a ray through a real aperture, including large faces
        whose vertices all lie outside it (a corner-only check misses them).
        """
        vertices, faces = geometry
        u, v = sample_axes
        for face in faces:
            for index in range(1, len(face) - 1):
                a, b, c = (vertices[i] for i in (face[0], face[index], face[index + 1]))
                denominator = (b[v] - c[v]) * (a[u] - c[u]) + (c[u] - b[u]) * (a[v] - c[v])
                if abs(denominator) < 1e-12:
                    continue
                first = ((b[v] - c[v]) * (point[0] - c[u]) + (c[u] - b[u]) * (point[1] - c[v])) / denominator
                second = ((c[v] - a[v]) * (point[0] - c[u]) + (a[u] - c[u]) * (point[1] - c[v])) / denominator
                third = 1 - first - second
                hit = first * a[plane_axis] + second * b[plane_axis] + third * c[plane_axis]
                if min(first, second, third) >= -1e-7 and interval[0] <= hit <= interval[1]:
                    return True
        return False

    # The two lodge variants retain actual watch-window and rear-door voids.
    for variant in range(2):
        assembly = lookup.get(("CemeteryLodgeShell", variant))
        if assembly is None:
            continue
        sign = -1 if variant else 1
        source = assembly.parts[0].geometry
        geometry = ([(x * sign, z, y) for x, z, y in source[0]], source[1])
        for point in ((0, 1.42), (-.35, 1.10), (.35, 1.70)):
            if crosses_aperture(geometry, 0, (-1.21, -1.07), (1, 2), point):
                problems.append("Cemetery lodge watch window was filled")
        if crosses_aperture(geometry, 1, (.97, 1.11), (0, 2), (.62, 1.1)):
            problems.append("Cemetery lodge rear doorway was filled")
    hut = lookup.get(("SeacoastHutShell", 0))
    if hut is not None:
        plaster = hut.parts[0].geometry
        if crosses_aperture(plaster, 1, (1.60, 1.81), (0, 2), (-.915, 1.63)):
            problems.append("Boat-station service hatch is a solid wall")
        if crosses_aperture(plaster, 1, (1.60, 1.81), (0, 2), (0, 1.1)):
            problems.append("Boat-station doorway was filled")
    # Unit members preserve measured descriptor bounds under nonuniform
    # fitting; this catches a decorative fixing shifting walking contacts.
    for kind in ("CoastalDoorLeaf", "CemeteryGatePillar", "CemeteryGateLeaf",
                 "CemeteryGateArch", "SeacoastPierDeckBoard", "SeacoastPierPile",
                 "SeacoastEsplanadeCoping"):
        assembly = lookup.get((kind, 0))
        if assembly is None:
            continue
        low, high = api.combined_bounds(assembly.parts)
        wanted_low, wanted_high = (-.5, -.5, 0), (.5, .5, 1)
        if any(abs(low[i] - wanted_low[i]) > 1e-5 or abs(high[i] - wanted_high[i]) > 1e-5
               for i in range(3)):
            problems.append(f"{kind} normalized descriptor envelope drift: {low}/{high}")
