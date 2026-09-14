#!/usr/bin/env python3
"""Deterministic passive litter catalog for the accepted eastern roadside strip.

Every item is one rigid, ground-origin assembly in Unity-local metres. Actual
wall thickness makes empty vessels readable from either opening. Worn paint
and local corroded joints use shared roles; no brands, food, liquid or actions.
Run with tools/run-blender.py; --validate-only measures the published manifest.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bpy
from mathutils import Vector
import bar_parts as bp
import interior_kit as kit

_spec = importlib.util.spec_from_file_location("city_litter_reuse", ROOT / "tools/build-city-misc-3d-model.py")
misc = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = misc
_spec.loader.exec_module(misc)

VERSION = "1.0.0"
DESIGN_ID = "city_east_strip_litter_v1"
MODEL_DIR = ROOT / "Assets/Resources/City/EastExit"
SOURCE_DIR = ROOT / "ArtSource/City/EastExit"
COLORS = {
    "GlassGreen": (.16, .24, .16, 1), "GlassBrown": (.25, .16, .08, 1),
    "GlassClear": (.36, .42, .38, 1), "Steel": (.35, .37, .33, 1),
    "Rust": (.30, .17, .10, 1), "PaintGreen": (.19, .28, .23, 1),
    "PaintBlue": (.21, .29, .33, 1), "Rubber": (.065, .073, .065, 1),
    "Plastic": (.40, .42, .32, 1), "Paper": (.43, .37, .26, 1),
    "Timber": (.29, .25, .16, 1), "Brick": (.36, .24, .17, 1),
    "Dark": (.09, .10, .08, 1),
}
EXPORT_SETTINGS = {"axis_forward": "-Z", "axis_up": "Y",
                   "apply_scale_options": "FBX_SCALE_NONE", "bake_space_transform": False}
CATALOG = (
    ("BottleGreen", "glass"), ("BottleBrown", "glass"), ("BottleClear", "glass"),
    ("BottleShort", "glass"), ("BottleBrokenNeck", "glass"), ("BottleBrokenBase", "glass"),
    ("CanOpen", "metal"), ("CanOpenWide", "metal"), ("CanClosed", "metal"),
    ("CanCrushed", "metal"), ("CanRusty", "metal"), ("CanLid", "metal"),
    ("PetBottle", "plastic"), ("PetCrushed", "plastic"), ("Canister", "plastic"),
    ("CanisterDented", "plastic"), ("BagTied", "bag"), ("BagTorn", "bag"),
    ("SackFlattened", "bag"), ("CardboardFolded", "paper"), ("CardboardTorn", "paper"),
    ("CrateBroken", "wood"), ("PlankLong", "wood"), ("PlankShort", "wood"),
    ("BrickWhole", "masonry"), ("BrickBroken", "masonry"), ("Bucket", "metal"),
    ("BucketCrushed", "metal"), ("PipeShort", "metal"), ("PipeBent", "metal"),
    ("Tyre", "rubber"), ("BicycleRim", "metal"), ("BicycleNoWheels", "bicycle"),
    ("BicycleOneWheel", "bicycle"), ("BicycleBentFork", "bicycle"), ("BicycleNoSaddle", "bicycle"),
)
SOLID = {"CrateBroken", "Bucket", "BucketCrushed", "Tyre",
         "BicycleNoWheels", "BicycleOneWheel", "BicycleBentFork", "BicycleNoSaddle"}


def translated(geometry, offset):
    return ([tuple(p[i] + offset[i] for i in range(3)) for p in geometry[0]], geometry[1])


def outward(geometry):
    """Assert a solid, correcting only the helper's declared coordinate winding."""
    volume = bp.signed_volume(geometry)
    if abs(volume) < 1e-12:
        raise ValueError("Zero-volume component")
    if volume < 0:
        return geometry[0], [tuple(reversed(face)) for face in geometry[1]]
    return geometry


def source_to_unity(geometry):
    return bp.to_source(geometry)


def tube(a, b, radius, sides=6):
    return source_to_unity(misc.local_tube(a, b, radius, sides))


def ring(center, outer, inner, height, sides=12):
    return source_to_unity(misc.outward_annulus_z(
        misc.local_point(*center), outer, inner, height, sides))


def box(center, size, bevel=.007):
    return bp.u_box(center, size, min(bevel, min(size) * .18))


def hollow(profile, thickness=.006, sides=10, broken=0, bottom=True):
    """Closed material shell, with actual inner wall and optional cavity floor.

    The shell is continuous through its lip, including jagged broken edges.
    The radial deformation is shared by both sides, preserving wall thickness.
    """
    rings = [(y, radius, False) for y, radius in profile]
    rings += [(y if index else y + thickness, max(thickness, radius - thickness), True)
              for index, (y, radius) in reversed(list(enumerate(profile)))]
    verts = []
    for row, (y, radius, inner) in enumerate(rings):
        top = row in (len(profile) - 1, len(profile))
        for i in range(sides):
            a = math.tau * i / sides
            height = y + (broken * (.38 + .62 * math.sin(i * 2.13)) if top else 0)
            verts.append((math.cos(a) * radius, height, math.sin(a) * radius))
    faces = []
    for row in range(len(rings) - 1):
        for i in range(sides):
            n = (i + 1) % sides
            faces.append((row * sides + n, row * sides + i,
                          (row + 1) * sides + i, (row + 1) * sides + n))
    if bottom:
        faces += [tuple(range(sides)), tuple(reversed(range((len(rings) - 1) * sides, len(rings) * sides)))]
    else:
        last = (len(rings) - 1) * sides
        for i in range(sides):
            n = (i + 1) % sides
            faces.append((i, n, last + n, last + i))
    return outward((verts, faces))


def crush(geometry, severity=.4):
    lo, hi = kit.bounds(geometry)
    height = max(.001, hi[1] - lo[1])
    points = []
    for x, y, z in geometry[0]:
        t = (y - lo[1]) / height
        pinch = 1 - severity * math.sin(math.pi * t) ** 2
        points.append((x * pinch + severity * .035 * math.sin(t * 9),
                       y * (1 - severity * .38), z * (1 + severity * .22 * math.sin(t * 7))))
    return outward((points, geometry[1]))


def sheet(points, thickness=.008):
    """Extruded irregular cardboard/wood fragment, never a one-sided plane."""
    n = len(points)
    v = [(x, y - thickness / 2, z) for x, y, z in points]
    v += [(x, y + thickness / 2, z) for x, y, z in points]
    faces = [tuple(range(n)), tuple(reversed(range(n, n * 2)))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, n + i, n + j, j))
    return outward((v, faces))


def components(geometry):
    """Extract source helper solids without their neighbouring scene props."""
    vertices, faces = geometry
    parents = list(range(len(vertices)))
    def find(x):
        while parents[x] != x:
            parents[x] = parents[parents[x]]
            x = parents[x]
        return x
    for face in faces:
        for vertex in face[1:]:
            parents[find(vertex)] = find(face[0])
    groups = {}
    for face in faces:
        groups.setdefault(find(face[0]), []).append(face)
    result = []
    for block in groups.values():
        indices = sorted({v for f in block for v in f})
        remap = {v: i for i, v in enumerate(indices)}
        result.append(([vertices[v] for v in indices], [tuple(remap[v] for v in f) for f in block]))
    return result


def make_items():
    result = {}
    provenance = {}
    def item(name, roles, reuse="New geometry; shared bar_parts/interior_kit solids", rotation=(0, 0, 0)):
        merged = {}
        for role, geometries in roles.items():
            for geometry in geometries:
                if bp.signed_volume(outward(geometry)) <= 0:
                    raise ValueError(f"Inward component {name}/{role}")
            merged[role] = bp.u_rotated(kit.merge_all([outward(g) for g in geometries]), rotation)
        low, high = kit.bounds(kit.merge_all(merged.values()))
        offset = (-(low[0] + high[0]) / 2, -low[1], -(low[2] + high[2]) / 2)
        result[name] = {r: translated(g, offset) for r, g in merged.items()}
        provenance[name] = reuse

    # Reuse the YardBottle envelope, replacing its two capped solids with a
    # single continuous empty vessel. Preserve its measured body/neck widths.
    yard_parts = components(misc.build_yard_bottle().parts[0].geometry)
    yard_body = kit.bounds(source_to_unity(yard_parts[0]))
    yard_neck = kit.bounds(source_to_unity(yard_parts[1]))
    radius = (yard_body[1][0] - yard_body[0][0]) / 2
    neck = (yard_neck[1][0] - yard_neck[0][0]) / 2
    base_profile = [(0, radius * .91), (.016, radius), (.178, radius),
                    (.208, neck), (.265, neck * .90), (.274, neck)]
    bottle_specs = [
        ("BottleGreen", "GlassGreen", 1, 1, base_profile, 0),
        ("BottleBrown", "GlassBrown", 1.18, 1.22, base_profile, 0),
        ("BottleClear", "GlassClear", .87, 1.42, base_profile, 0),
        ("BottleShort", "GlassBrown", 1.27, .78, base_profile, 0),
        ("BottleBrokenNeck", "GlassGreen", 1.05, 1, base_profile[:4], .018),
        ("BottleBrokenBase", "GlassClear", 1.18, 1, [(0, radius), (.066, radius * 1.03)], .027),
    ]
    for n, role, sx, sy, profile, broken in bottle_specs:
        g = hollow([(y * sy, r * sx) for y, r in profile], .0045, 10, broken)
        item(n, {role: [g]}, "build-city-misc-3d-model.py:build_yard_bottle measured silhouette; continuous hollow reauthoring",
             rotation=(87 if broken else 93, 13, -7))

    for index, (n, radius, height, role) in enumerate((
            ("CanOpen", .054, .13, "Steel"), ("CanOpenWide", .076, .091, "Steel"),
            ("CanClosed", .052, .15, "PaintGreen"), ("CanCrushed", .060, .145, "Steel"),
            ("CanRusty", .061, .12, "Steel"))):
        profile = [(0, radius), (.006, radius * 1.045), (.012, radius),
                   (height * .40, radius), (height * .43, radius * 1.035),
                   (height * .46, radius), (height - .010, radius), (height, radius * 1.04)]
        shell = hollow(profile, .0032, 10)
        roles = {role: [shell]}
        if n == "CanCrushed":
            roles[role] = [crush(shell, .69)]
        if n == "CanClosed":
            roles["Steel"] = [bp.u_cylinder((0, height - .002, 0), (radius * 1.95, .002, radius * 1.95), 10)]
        if n == "CanRusty":
            roles["Rust"] = [ring((0, .012, 0), radius * 1.027, radius * .96, .012, 10),
                             ring((0, height - .012, 0), radius * 1.027, radius * .97, .009, 10)]
        item(n, roles, "build-city-misc-3d-model.py:NightlifeShelterClutter can proportions and outward_annulus_z; new ribbed hollow shell",
             rotation=(76 + index * 5, index * 17, 0))
    item("CanLid", {"Steel": [ring((0, .004, 0), .058, .049, .007, 10),
                                    bp.u_cylinder((0, .003, 0), (.106, .002, .106), 10)]},
         "build-city-misc-3d-model.py:outward_annulus_z")

    pet_profile = [(0, .048), (.018, .055), (.060, .055), (.068, .052),
                   (.105, .054), (.113, .051), (.170, .055), (.194, .033),
                   (.232, .022), (.241, .025)]
    for n, damage in (("PetBottle", 0), ("PetCrushed", .80)):
        g = hollow(pet_profile, .0035, 8)
        item(n, {"Plastic": [crush(g, damage) if damage else g]}, rotation=(94, -17, 7))
    for n, damage in (("Canister", False), ("CanisterDented", True)):
        body = box((0, .18, 0), (.27, .34, .14), .032)
        roles = {"Plastic": [crush(body, .43) if damage else body,
                              tube((-.092, .31, 0), (-.065, .405, 0), .024),
                              tube((-.065, .405, 0), (.075, .405, 0), .024),
                              tube((.075, .405, 0), (.092, .31, 0), .024),
                              translated(hollow([(0, .032), (.060, .029)], .006, 8, bottom=False), (.083, .32, 0))]}
        # Shallow embossed ribs are geometry on the broad faces; no symbols.
        for z in (-.072, .072):
            roles["Plastic"] += [tube((-.08, .10, z), (.065, .27, z), .006, 4)]
        item(n, roles, rotation=(88, 18 if damage else -12, 4))

    shelter_bags = components(misc.build_nightlife_shelter_clutter().parts[1].geometry)
    bag = source_to_unity(shelter_bags[0])
    bag = translated(bag, (.63, 0, -.12))
    item("BagTied", {"Dark": [bag, tube((0, .46, 0), (0, .555, 0), .021, 6),
                               box((.006, .535, 0), (.10, .026, .044), .008)]},
         "build-city-misc-3d-model.py:build_nightlife_shelter_clutter first Bags_Street component; standalone with tied neck",
         rotation=(63, 9, 12))
    torn = hollow([(0, .14), (.07, .23), (.21, .18), (.30, .10)], .008, 8, .061)
    item("BagTorn", {"Dark": [kit.scaled(torn, (1.18, .64, .9))],
                     "Plastic": [sheet([(-.06, .10, .10), (.12, .13, .08), (.10, .17, .19), (-.04, .16, .23)])]},
         rotation=(42, 0, -12))
    item("SackFlattened", {"Paper": [kit.scaled(bag, (1.1, .15, 1.6)),
                                     box((0, .049, .21), (.26, .014, .085), .009)]},
         "build-city-misc-3d-model.py:build_nightlife_shelter_clutter first Bags_Street component compressed into an empty sack")
    item("CardboardFolded", {"Paper": [
        sheet([(-.28, .005, -.18), (.24, .005, -.18), (.24, .005, .14), (-.28, .005, .14)]),
        sheet([(-.28, .005, .14), (.24, .005, .14), (.20, .09, .25), (-.25, .12, .25)]),
        sheet([(-.28, .005, -.18), (-.25, .055, -.30), (.23, .042, -.27), (.24, .005, -.18)])]})
    item("CardboardTorn", {"Paper": [sheet([(-.31, .01, -.13), (-.08, .045, -.19),
        (.05, .026, -.14), (.16, .010, -.19), (.31, .015, -.05), (.23, .035, .05),
        (.28, .045, .17), (-.20, .025, .21), (-.30, .01, .10)])]})
    planks = [box((x, .035, 0), (.105, .05, .44)) for x in (-.18, -.06, .065, .18)]
    planks += [box((-.23, .14, 0), (.045, .24, .42)),
               box((0, .13, -.23), (.49, .065, .037)),
               box((-.12, .22, -.23), (.25, .055, .037)),
               bp.u_rotated(box((.13, .055, .23), (.29, .040, .066)), (0, 13, -9))]
    item("CrateBroken", {"Timber": planks, "Rust": [box((-.231, .24, -.16), (.011, .016, .035), .002)]},
         "build-city-misc-3d-model.py:NightlifeShelterClutter slatted crate construction; reduced broken standalone crate")
    for n, length in (("PlankLong", 1.04), ("PlankShort", .48)):
        item(n, {"Timber": [sheet([(-length / 2, .025, -.071), (length / 2 - .035, .025, -.063),
                   (length / 2 - .07, .025, -.021), (length / 2, .025, .017),
                   (length / 2 - .023, .025, .068), (-length / 2, .025, .071)], .043)],
                 "Rust": [tube((-length / 3, .043, .022), (-length / 3 + .025, .062, .022), .004, 4)]})
    item("BrickWhole", {"Brick": [box((0, .039, 0), (.245, .078, .116), .008)]})
    item("BrickBroken", {"Brick": [sheet([(-.102, .035, -.057), (.023, .035, -.059),
        (.062, .035, -.025), (.031, .035, .007), (.084, .035, .040),
        (-.098, .035, .061)], .069)]})

    for n, damage in (("Bucket", False), ("BucketCrushed", True)):
        body = hollow([(0, .113), (.023, .114), (.225, .156), (.246, .16)], .006, 12)
        handle = [tube((-.158, .207, 0), (-.164, .34, 0), .006),
                  tube((-.164, .34, 0), (0, .408, 0), .006),
                  tube((0, .408, 0), (.164, .34, 0), .006),
                  tube((.164, .34, 0), (.158, .207, 0), .006)]
        roles = {"PaintBlue" if damage else "PaintGreen": [crush(body, .53) if damage else body],
                 "Rust": handle + [ring((0, .014, 0), .116, .109, .014)]}
        item(n, roles, rotation=(92, -16, 2))
    item("PipeShort", {"Steel": [hollow([(0, .058), (.52, .058)], .010, 10, bottom=False)],
                       "Rust": [ring((0, .018, 0), .060, .056, .030, 10)]}, rotation=(90, 0, 0))
    # Two visibly mated hollow sections, their join hidden inside a sleeve.
    item("PipeBent", {"PaintGreen": [hollow([(0, .051), (.39, .051)], .008, 10, bottom=False),
        translated(bp.u_rotated(hollow([(0, .050), (.25, .050)], .008, 10, bottom=False), (0, 0, 64)), (0, .35, 0))],
        "Rust": [translated(bp.u_rotated(ring((0, 0, 0), .060, .048, .066, 10), (0, 0, 32)), (0, .34, 0))]},
        rotation=(87, 16, -10))
    tyre_profile = [(0, .29), (.018, .33), (.068, .34), (.115, .32), (.13, .29)]
    # A tyre is an annular wall open through its centre, not a filled disc.
    item("Tyre", {"Rubber": [hollow(tyre_profile, .056, 16, bottom=False)]},
         "build-city-misc-3d-model.py:outward_annulus_z topology; new faceted rounded tyre cross-section")
    item("BicycleRim", {"Steel": [ring((0, .025, 0), .344, .327, .040, 16)],
                       "Rust": [ring((0, .025, 0), .327, .318, .022, 16)]},
         "build-city-misc-3d-model.py:outward_annulus_z; empty detached bicycle rim")

    # The source bicycle was upside down on its repair patch. Extract just its
    # eight connected frame tubes, turn upright, then lay the entire bicycle
    # onto its side. Never bring the repair crate/tools into the litter asset.
    frame_source = misc.build_courtyard_bicycle_repair().parts[0].geometry
    frame_tubes = [translated(bp.u_rotated(source_to_unity(g), (180, 0, 0)), (0, 1.22, 0))
                   for g in components(frame_source)]
    rear, front, crank, seat = (-.85, .40, 0), (.45, .40, 0), (-.18, .50, 0), (-.42, .86, 0)
    for index, n in enumerate(("BicycleNoWheels", "BicycleOneWheel", "BicycleBentFork", "BicycleNoSaddle")):
        painted = list(frame_tubes)
        if n == "BicycleBentFork":
            painted[4] = tube((.25, .86, 0), (.37, .60, .05), .030, 7)
            painted += [tube((.37, .60, .05), (.62, .41, .14), .027, 7)]
        # Paired stays/fork tines, bearing ends, crank and pedals preserve the
        # missing wheels' physical mounting points and recognisable silhouette.
        for depth in (-.065, .065):
            painted += [tube((rear[0], rear[1], depth), crank, .020, 6),
                        tube((rear[0], rear[1], depth), seat, .020, 6)]
        if n != "BicycleBentFork":
            painted += [tube((.25, .86, .055), (.45, .40, .055), .022, 6)]
        paint = "PaintGreen" if index % 2 == 0 else "PaintBlue"
        roles = {paint: painted,
                 "Rust": [tube((-.42, .80, 0), (-.42, .91, 0), .037, 7),
                          tube((-.23, .49, 0), (-.11, .51, 0), .040, 7),
                          tube((.27, .80, 0), (.30, .93, 0), .035, 7)],
                 "Steel": [tube((-.18, .5, -.115), (-.18, .5, .115), .025, 7),
                           tube((-.18, .50, .115), (-.03, .56, .115), .020, 6),
                           tube((-.18, .50, -.115), (-.34, .44, -.115), .020, 6)],
                 "Rubber": [box((-.025, .56, .16), (.12, .038, .13)),
                            box((-.34, .44, -.16), (.12, .038, .13))]}
        if n != "BicycleNoSaddle":
            roles["Steel"] += [tube(seat, (-.45, 1.115, 0), .024, 7)]
            roles["Rubber"] += [box((-.43, 1.145, 0), (.30, .058, .15), .025)]
        else:
            roles["Steel"] += [tube(seat, (-.44, 1.005, 0), .022, 7)]
        wheel_centers = [rear] if n in ("BicycleOneWheel", "BicycleNoSaddle") else []
        if n == "BicycleBentFork":
            wheel_centers = [rear]
        for center in wheel_centers:
            roles["Rubber"] += [translated(bp.u_rotated(ring((0, 0, 0), .395, .351, .068, 16), (90, 0, 0)), center)]
            roles["Steel"] += [translated(bp.u_rotated(ring((0, 0, 0), .351, .336, .034, 16), (90, 0, 0)), center)]
            for i in range(8):
                a = math.tau * i / 8
                roles["Steel"] += [tube(center, (center[0] + math.cos(a) * .337,
                                                        center[1] + math.sin(a) * .337, center[2]), .0038, 4)]
        item(n, roles, "build-city-misc-3d-model.py:build_courtyard_bicycle_repair BicycleFrame_Residential_PaintedMetal geometry and wheel radii; isolated, damaged and laid down",
             rotation=(83 if index % 2 == 0 else 96, 0, index * 3 - 4))
    if list(result) != [name for name, _ in CATALOG]:
        raise ValueError("Stable catalog order changed")
    return result, provenance


def manifest_for(items, provenance):
    records = []
    for name, category in CATALOG:
        roles = items[name]
        low, high = kit.bounds(kit.merge_all(roles.values()))
        parts = []
        for role, geometry in roles.items():
            lo, hi = kit.bounds(geometry)
            parts.append({"mesh": f"ELI_{name}_{role}", "role": role,
                          "triangle_count": kit.triangle_count(geometry),
                          "bounds_min_unity": [round(v, 6) for v in lo],
                          "bounds_max_unity": [round(v, 6) for v in hi]})
        records.append({"name": name, "category": category, "solid": name in SOLID,
                        "bounds_min_unity": [round(v, 6) for v in low],
                        "bounds_max_unity": [round(v, 6) for v in high],
                        "triangle_count": sum(p["triangle_count"] for p in parts),
                        "reuse_source": provenance[name], "parts": parts})
    signature = hashlib.sha256(json.dumps(items, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return {"schema_version": 1, "version": VERSION, "design_id": DESIGN_ID,
            "generator": "tools/build-city-litter-3d-model.py", "signature": signature,
            "units": "Unity local metres", "source_to_unity": "swap Y/Z and rewind faces",
            "root_scale_contract": "Preserve imported template lossyScale; FBX uses file units",
            "normals_contract": "Positive signed volume for every closed component and material part",
            "export_settings": EXPORT_SETTINGS, "palette": COLORS,
            "lights": False, "animation_count": 0, "colliders": False,
            "items": records, "triangle_count": sum(r["triangle_count"] for r in records)}


def validate(items, manifest):
    rebuilt, reuse = make_items()
    if manifest != manifest_for(rebuilt, reuse) or len(items) != 36:
        raise ValueError("Litter geometry is not deterministic or catalog changed")
    if manifest["triangle_count"] > 17000:
        raise ValueError("Litter library exceeds total triangle budget")
    for record in manifest["items"]:
        name = record["name"]
        lo, hi = record["bounds_min_unity"], record["bounds_max_unity"]
        if abs(lo[1]) > 1e-6 or abs(lo[0] + hi[0]) > 1e-6 or abs(lo[2] + hi[2]) > 1e-6:
            raise ValueError(f"Ungrounded or uncentered assembly: {name}")
        if record["triangle_count"] > (2000 if record["category"] == "bicycle" else 600):
            raise ValueError(f"Per-item triangle budget exceeded: {name}")
        if hi[1] > .65 or max(hi[0] - lo[0], hi[2] - lo[2]) > 2.4:
            raise ValueError(f"Litter has an implausible standing envelope: {name}")
        for role, geometry in items[name].items():
            if role not in COLORS or not all(math.isfinite(x) for p in geometry[0] for x in p):
                raise ValueError(f"Invalid material/vertex: {name}/{role}")
            if bp.signed_volume(bp.to_source(geometry)) <= 0:
                raise ValueError(f"Inward export normals: {name}/{role}")
            for component in components(geometry):
                if bp.signed_volume(component) <= 0:
                    raise ValueError(f"Inward component: {name}/{role}")
    no_wheels = items["BicycleNoWheels"]
    # Explicit regression: the shared frame extraction must never drag the
    # source repair crate, loose tools or either source wheel into this item.
    if any(kit.bounds(g)[1][1] > .65 for g in no_wheels.values()):
        raise ValueError("Wheel-less bicycle must lie on its side")


def build_scene(items):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1
    root = bpy.data.objects.new("ROOT_CityLitter3D", None)
    scene.collection.objects.link(root)
    groups = {}
    for name, roles in items.items():
        group = bpy.data.objects.new(name, None)
        scene.collection.objects.link(group)
        group.parent = root
        groups[name] = group
        for role, geometry in roles.items():
            mesh = bpy.data.meshes.new(f"ELI_{name}_{role}_Mesh")
            vertices, faces = bp.to_source(geometry)
            mesh.from_pydata(vertices, [], faces)
            mesh.update(calc_edges=True)
            obj = bpy.data.objects.new(f"ELI_{name}_{role}", mesh)
            scene.collection.objects.link(obj)
            obj.parent = group
            obj["role"] = role
            uv = mesh.uv_layers.new(name="UVMap")
            for polygon in mesh.polygons:
                axis = max(range(3), key=lambda i: abs(polygon.normal[i]))
                axes = ((1, 2), (0, 2), (0, 1))[axis]
                for loop in polygon.loop_indices:
                    point = mesh.vertices[mesh.loops[loop].vertex_index].co
                    uv.data[loop].uv = (point[axes[0]], point[axes[1]])
    return root, groups


def preview(groups, items, output):
    scene = bpy.context.scene
    stage = bpy.data.collections.new("PRESENTATION_CityLitter3D")
    scene.collection.children.link(stage)
    materials = {}
    for role, color in {**COLORS, "Backdrop": (.23, .25, .22, 1), "Label": (.80, .79, .68, 1)}.items():
        mat = bpy.data.materials.new("PREVIEW_ELI_" + role)
        mat.diffuse_color = color
        mat.use_nodes = True
        shader = mat.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Roughness"].default_value = .83
        materials[role] = mat
    for index, (name, _) in enumerate(CATALOG):
        x, y = (index % 6 - 2.5) * 2.8, (2.5 - index // 6) * 3.1
        low, high = kit.bounds(kit.merge_all(items[name].values()))
        scale = min(2.05 / max(high[0] - low[0], high[2] - low[2]), 8)
        for child in groups[name].children:
            obj = child.copy()
            obj.data = child.data.copy()
            obj.parent = None
            obj.scale = (scale,) * 3
            obj.location = (x, y + .15, 0)
            # Face mouths toward the contact-sheet camera so visual QA can
            # inspect the actual empty cavities rather than only their soles.
            if name.startswith(("Bottle", "Can", "Pet", "Bucket")) and name not in ("CanLid", "Canister", "CanisterDented"):
                obj.rotation_euler.z = math.pi
            obj.data.materials.append(materials[child["role"]])
            stage.objects.link(obj)
            child.hide_render = True
        label_data = bpy.data.curves.new("PREVIEW_Label_" + name, "FONT")
        label_data.body = f"{index + 1:02d} {name}"
        label_data.size = .15
        label_data.align_x = "CENTER"
        label = bpy.data.objects.new("PREVIEW_Label_" + name, label_data)
        stage.objects.link(label)
        label.location = (x, y - 1.24, .003)
        label.data.materials.append(materials["Label"])
    mesh = bpy.data.meshes.new("PREVIEW_Backdrop")
    v, f = bp.to_source(box((0, -.08, 0), (30, .16, 31)))
    mesh.from_pydata(v, [], f)
    ground = bpy.data.objects.new("PREVIEW_Backdrop", mesh)
    stage.objects.link(ground)
    mesh.materials.append(materials["Backdrop"])
    world = bpy.data.worlds.new("PREVIEW_World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (.31, .36, .34, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = .65
    scene.world = world
    light_data = bpy.data.lights.new("PREVIEW_Sun", "SUN")
    light_data.energy = 2.5
    light_data.angle = math.radians(10)
    light = bpy.data.objects.new("PREVIEW_Sun", light_data)
    stage.objects.link(light)
    light.rotation_euler = (.35, -.55, -.35)
    camera_data = bpy.data.cameras.new("PREVIEW_Camera")
    camera = bpy.data.objects.new("PREVIEW_Camera", camera_data)
    stage.objects.link(camera)
    camera.location = (0, -14, 30)
    camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 18.8
    scene.camera = camera
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1500
    scene.render.resolution_y = 1500
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--preview-only", action="store_true")
    args = parser.parse_args(argv)
    args.model_dir, args.source_dir = args.model_dir.resolve(), args.source_dir.resolve()
    items, provenance = make_items()
    manifest = manifest_for(items, provenance)
    validate(items, manifest)
    manifest_path = args.model_dir / "CityLitter3D.json"
    if args.validate_only:
        if not manifest_path.is_file() or json.loads(manifest_path.read_text(encoding="utf-8")) != json.loads(json.dumps(manifest)):
            raise ValueError("Published litter manifest differs from measured deterministic geometry")
        print(f"CITY LITTER VALIDATION OK: {len(items)} items; {manifest['triangle_count']} triangles; {manifest['signature']}")
        return
    args.model_dir.mkdir(parents=True, exist_ok=True)
    args.source_dir.mkdir(parents=True, exist_ok=True)
    root, groups = build_scene(items)
    if not args.preview_only:
        bpy.ops.object.select_all(action="SELECT")
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(filepath=str(args.model_dir / "CityLitter3D.fbx"),
            use_selection=True, object_types={"EMPTY", "MESH"}, **EXPORT_SETTINGS,
            add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True,
            mesh_smooth_type="FACE", use_custom_props=True)
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if not args.no_preview:
        preview(groups, items, args.source_dir / "CityLitter3D.png")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityLitter3D.blend"), check_existing=False)
    print(f"CITY LITTER BUILD OK: {len(items)} items; {manifest['triangle_count']} triangles; {manifest['signature']}")


if __name__ == "__main__":
    main()
