"""Deterministic Unity-space mainland relief and its shared road/traffic datum.

This is authored geometry: all functions are ordinary Python, and the existing
east-exit Blender generator owns export, measured manifests and source files.
"""
from __future__ import annotations

import math
from functools import lru_cache

VERSION = "3.0.0"
ROAD_WIDTH = 6.0
SHOULDER_WIDTH = 10.0
GATE_X = -88.0
FLAT_END_X = -78.0
REAL_END_X = -56.0
XS = (-88, -84, -78, -72, -66, -61, -56, -50, -44, -36, -28, -20, -10,
      0, 6, 14, 24, 36, 50, 68, 90, 115, 145,
      180, 220, 265, 315, 370, 430, 495, 570, 650, 740, 840, 950,
      1070, 1200, 1340, 1490, 1650, 1820, 2000, 2200, 2420, 2660,
      2920, 3200, 3500, 3820, 4160, 4520, 4900, 5300, 5720, 6160,
      6620, 7100, 7600, 8100, 8620, 9160, 9720, 10300, 11000,
      11800, 12600, 13600)
ROAD_KEYS = ((-88, 0), (-78, 0), (-66, -1.7), (-56, -5.0), (-44, -8.0),
             (-20, -10.0), (0, -11.0), (40, -18.0), (90, -27), (180, -40),
             (320, -20), (500, 30), (950, -250),
             (1400, -570), (2050, -240), (3000, 410), (4100, 330),
             (5400, -240), (6600, -150), (7800, 110), (10500, 110),
             (13600, 0))
GRADE_KEYS = ((-88, 0), (-78, 0), (-66, -.25), (-56, -.80), (-44, -1.45),
              (-20, -3.2), (0, -4.3), (40, -6.8), (90, -9.4), (180, -14.5),
              (320, -21), (550, -33), (900, -50), (1400, -74), (2200, -111),
              (3500, -157), (5000, -207), (6800, -250), (7800, -270),
              (9600, -280), (13600, -284))


def smooth(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def keyed(keys, x):
    if x <= keys[0][0]:
        return keys[0][1]
    for (a, av), (b, bv) in zip(keys, keys[1:]):
        if x <= b:
            return av + (bv - av) * smooth((x - a) / (b - a))
    return keys[-1][1]


def road_center(x):
    return keyed(ROAD_KEYS, x)


def road_grade(x):
    return keyed(GRADE_KEYS, x)


# Long unequal crests, each with an authored section rather than radial cones.
# Entries are x, lateral crest position, height above valley, west/east flanks.
RIDGES = (
    ((-65, -38, 0, 45, 50), (-16, -48, 5, 80, 65),
     (90, -72, 20, 150, 80), (280, -125, 61, 220, 125),
     (550, -225, 120, 280, 180), (900, -400, 190, 380, 230),
     (1400, -670, 170, 490, 380), (2200, -1100, 0, 600, 500)),
    ((-65, 40, 0, 45, 50), (-16, 53, 8, 60, 90),
     (90, 82, 37, 85, 170), (270, 132, 105, 150, 260),
     (530, 165, 170, 260, 360), (840, 160, 195, 310, 460),
     (1200, 460, 160, 370, 520), (2000, 1250, 0, 450, 750)),
    ((450, -780, 0, 600, 650), (1100, -920, 130, 700, 600),
     (2100, -980, 375, 900, 730), (3100, -1550, 520, 1150, 900),
     (4700, -2150, 430, 1300, 1100), (6500, -3500, 0, 1600, 1300)),
    ((650, 850, 0, 650, 700), (1800, 1150, 270, 730, 1150),
     (2900, 1550, 430, 1050, 1300), (4300, 2250, 590, 1400, 1600),
     (5900, 3250, 340, 1200, 1600), (7300, 4300, 0, 1800, 1700)),
    ((2600, -3500, 0, 1800, 1600), (4300, -3300, 590, 1700, 1500),
     (5800, -3500, 760, 2200, 1850), (7100, -4700, 560, 2400, 1800),
     (9200, -6200, 0, 2200, 2000)),
    ((3100, 3600, 0, 1600, 1500), (4800, 3400, 690, 1450, 2000),
     (6200, 3850, 850, 1700, 2300), (7600, 5000, 530, 1500, 2400),
     (9600, 6400, 0, 1900, 2000)),
)
# The coastal approach has low shoulders, not a gorge. Broader lateral feet
# keep the first crests connected after moving them away from the view axis.
RIDGES = tuple(tuple((x, z * 1.6, height * .55, left * 1.35, right * 1.35)
                      for x, z, height, left, right in points) if index < 2 else points
               for index, points in enumerate(RIDGES))


def ridge_height(points, x, z):
    if x < points[0][0] or x > points[-1][0]:
        return 0.0
    center, height, left, right = (keyed(tuple((p[0], p[i]) for p in points), x)
                                   for i in range(1, 5))
    lateral = z - center
    fraction = abs(lateral) / (left if lateral < 0 else right)
    if fraction >= 1:
        return 0.0
    # A broad broken shoulder under a narrow rounded crest: large rock faces,
    # with low-frequency strata that never create a serrated noise horizon.
    section = keyed(((0, 1), (.18, .91), (.48, .50), (.79, .10), (1, 0)), fraction)
    fracture = 1 + .072 * math.sin(x / 151 + z / 213) + .035 * math.sin(x / 67 - z / 109)
    return height * section * fracture


def land_height(x, z):
    center = road_center(x)
    away = abs(z - center)
    datum = road_grade(x)
    valley = smooth((away - 8) / max(22, 40 + max(0, x) * .023))
    base = datum - .06 + valley * (1.3 * math.sin(x / 83 + z / 116) +
                                  1.0 * math.sin(x / 157 - z / 91))
    # One continuous field: overlapping flanks join as terrain, without floating
    # prefabs or independently intersecting mountain sheets.
    ridges = max((ridge_height(points, x, z) for points in RIDGES), default=0.0)
    # This continuous broad valley reserves a ~34% frame-wide low central
    # opening at the ordinary 53-degree vertical lens. It shapes the terrain,
    # not a view-dependent mask; sideways movement retains real parallax.
    forward = max(0, x + 95)
    skyline_valley = smooth((abs(z) - 18 - forward * .30) / (30 + forward * .14))
    ridges *= skyline_valley
    distance_crest = keyed(((9000, 0), (10300, 125), (11800, 430),
                            (12600, 385), (13600, 145)), x)
    distant_break = .73 + .19 * math.sin(z / 1100 + .4) + .08 * math.sin(z / 390)
    ridges = max(ridges, distance_crest * distant_break)
    # The final crest lies beyond the city; before it, the wide central valley
    # has enough room for both the road and the lower skyline.
    return base + ridges * valley


def offsets(x):
    # Constant cross-sections close to the road protect the shared shoulder
    # edge. Wider cuts then spread with perspective, retaining connected faces.
    width = 260 + max(0, x) * .66
    fixed = (5, 9, 15, 24, 36, 52, 74, 103)
    outer = tuple(128 + (width - 128) * t for t in
                  (0, .045, .10, .18, .29, .43, .60, .79, 1))
    positive = fixed + outer
    return tuple(-v for v in reversed(positive)) + positive


def normal(a, b, c):
    ab = tuple(b[i] - a[i] for i in range(3))
    ac = tuple(c[i] - a[i] for i in range(3))
    return (ab[1] * ac[2] - ab[2] * ac[1],
            ab[2] * ac[0] - ab[0] * ac[2],
            ab[0] * ac[1] - ab[1] * ac[0])


def terrain():
    parts = {role: ([], []) for role in ("DistanceLand", "DistanceRock", "DistanceVegetation")}
    rows = [[(x, land_height(x, road_center(x) + d), road_center(x) + d)
             for d in offsets(x)] for x in XS]
    for first, last in zip(rows, rows[1:]):
        for column in range(len(first) - 1):
            if column == len(first) // 2 - 1:
                continue  # The road/shoulder owns this actual opening.
            corners = (first[column], first[column + 1], last[column + 1], last[column])
            for order in ((0, 1, 2), (0, 2, 3)):
                a, b, c = (corners[i] for i in order)
                n = normal(a, b, c)
                if n[1] <= 0:
                    raise ValueError("Mainland terrain face is inverted")
                up = n[1] / math.sqrt(sum(v * v for v in n))
                x, _, z = tuple((a[i] + b[i] + c[i]) / 3 for i in range(3))
                rock = up < .94 and abs(z - road_center(x)) > 28
                patch = math.sin(x / 240 + z / 153) + .48 * math.sin(z / 87 - x / 461)
                role = "DistanceRock" if rock else "DistanceVegetation" if patch > .12 and x < 7400 else "DistanceLand"
                vertices, faces = parts[role]
                start = len(vertices)
                vertices.extend((a, b, c))
                faces.append((start, start + 1, start + 2))
    return parts


def road_parts():
    result = {}
    xs = [x for x in XS if x >= REAL_END_X]
    for role, bands in (("DistanceRoad", ((-3, 0), (0, 3))),
                        ("DistanceShoulder", ((-5, -3), (3, 5)))):
        vertices, faces = [], []
        for low, high in bands:
            first = len(vertices)
            for x in xs:
                for side in (low, high):
                    # Match the land's exact +/-5 edge, including its .06 m
                    # depression. The road has a gentle ordinary crossfall.
                    y = road_grade(x) - abs(side) * .006
                    if abs(side) == 5:
                        y = land_height(x, road_center(x) + side)
                    vertices.append((x, y, road_center(x) + side))
            for row in range(len(xs) - 1):
                a = first + row * 2
                faces.extend(((a, a + 1, a + 3), (a, a + 3, a + 2)))
        result[role] = vertices, faces
    return result


def surface_height(x, z):
    """Height on the exported triangular skin, including its road cutout."""
    for first, last in zip(XS, XS[1:]):
        if first <= x <= last:
            t = (x - first) / (last - first)
            first_z = [road_center(first) + d for d in offsets(first)]
            last_z = [road_center(last) + d for d in offsets(last)]
            across = [a + (b - a) * t for a, b in zip(first_z, last_z)]
            for col, (left, right) in enumerate(zip(across, across[1:])):
                if not left <= z <= right:
                    continue
                if col == len(across) // 2 - 1:
                    grade = road_grade(first) + (road_grade(last) - road_grade(first)) * t
                    center = road_center(first) + (road_center(last) - road_center(first)) * t
                    away = abs(z - center)
                    return grade - (away * .006 if away <= 3 else .018 + (away - 3) * .021)
                a = (first, land_height(first, first_z[col]), first_z[col])
                b = (first, land_height(first, first_z[col + 1]), first_z[col + 1])
                c = (last, land_height(last, last_z[col + 1]), last_z[col + 1])
                d = (last, land_height(last, last_z[col]), last_z[col])
                vertices = (a, b, c) if z >= a[2] + (c[2] - a[2]) * t else (a, c, d)
                va, vb, vc = vertices
                plane = normal(va, vb, vc)
                return va[1] - (plane[0] * (x - va[0]) + plane[2] * (z - va[2])) / plane[1]
    raise ValueError("Mainland support sample leaves the exported surface")


@lru_cache(maxsize=1)
def route_points():
    # Subdivide the actual mesh's centre edges, not a second spline that can
    # leave the asphalt at bends or float over its vertical chords.
    knots = [(x, road_grade(x), road_center(x)) for x in XS]
    points, distance = [], 0.0
    previous = None
    for a, b in zip(knots, knots[1:]):
        length = math.dist(a, b)
        steps = max(1, math.ceil(length / 12.0))
        for step in range(steps):
            t = step / steps
            point = tuple(a[i] + (b[i] - a[i]) * t for i in range(3))
            if previous is not None:
                distance += math.dist(previous, point)
            points.append(dict(zip(("x", "y", "z", "distance"),
                                   (round(v, 6) for v in (*point, distance)))))
            previous = point
    distance += math.dist(previous, knots[-1])
    points.append(dict(zip(("x", "y", "z", "distance"),
                           (round(v, 6) for v in (*knots[-1], distance)))))
    return points


def skyline(kit, bp):
    buildings, windows = [], []
    for rank, count in enumerate((37, 43, 31)):
        for i in range(count):
            seed = (i * 7919 + rank * 104729 + 37) % 65521
            z = -3500 + i * 7000 / (count - 1) + math.sin(seed) * 47
            x = 7800 + rank * 720 + seed % 240
            width, depth, height = 68 + seed % 107, 95 + seed % 135, 48 + seed % 113
            if i % 11 == 4:
                height, width = 250 + seed % 94, width * .70
            elif i % 6 == 1:
                height += 58
            # Sample the exported triangles, not the continuous function that
            # they approximate. A shallow buried footing removes floating box
            # edges while preserving the full skyline above ordinary ground.
            base = min(surface_height(x + dx, z + dz)
                       for dx in (-depth / 2, depth / 2) for dz in (-width / 2, width / 2)) - .5
            block = kit.box((x, base + height / 2, z), (depth, height, width))
            if bp.signed_volume(block) <= 0:
                raise ValueError("Mainland building has inverted normals")
            buildings.append(block)
            if i % 4 == 0:
                cap = 8 + seed % 19
                buildings.append(kit.box((x + depth * .13, base + height + cap / 2, z - width * .12),
                                         (depth * .62, cap, width * .60)))
            if rank < 2 and (i % 3 == 1 or abs(z) < 1300 and i % 4 == 0):
                for j in range(2 + seed % 3):
                    wz = z + ((seed + j * 17) % 31 - 15) * width / 75
                    windows.append(kit.box((x - depth / 2 - .2, base + height * (.24 + .15 * j), wz),
                                            (.22, 8 + seed % 5, 18 + (seed + j * 7) % 17)))
                if abs(z) < 1850:
                    windows.append(kit.box((x - depth / 2 - .3, base + 9 + seed % 7, z - width * .12),
                                            (.24, 9, 28 + seed % 17)))
    glow_vertices, glow_faces = [], []
    for i in range(33):
        lateral = -1 + i / 16
        z, x = lateral * 4500, 9560 + abs(lateral) * 40
        # The luminous air sits behind every city facade but before the rear
        # ridge. Its high soft dome can rise above roofs without lighting land.
        upper = 190 + 630 * (1 - lateral * lateral)
        city_shift = road_grade(7800) - 106
        glow_vertices.extend(((x, 70 + city_shift, z), (x, upper + city_shift, z)))
    for i in range(32):
        a = i * 2
        glow_faces.extend(((a, a + 3, a + 1), (a, a + 2, a + 3)))
    return {"DistanceCity": kit.merge_all(buildings), "DistanceWindows": kit.merge_all(windows),
            "DistanceGlow": (glow_vertices, glow_faces)}


def traffic(kit, bp):
    body = [kit.chamfered_box((0, .62, 0), (4.6, .64, 1.8), .14)]
    # A real sloping cabin profile: roof, rear deck, bonnet and both axles read
    # even when the vehicle is only a small moving shape below the ridge.
    profile = ((-1.45, .91), (-.83, 1.47), (.58, 1.5), (1.23, .91))
    vertices = [(x, y, side) for side in (-.70, .70) for x, y in profile]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1),
             (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    cabin = vertices, faces
    if bp.signed_volume(cabin) < 0:
        cabin = vertices, [tuple(reversed(face)) for face in faces]
    body.append(cabin)
    for x in (-1.44, 1.43):
        for z in (-.79, .79):
            wheel = bp.u_cylinder((0, 0, 0), (.60, .11, .60), 12)
            body.append(kit.translated(bp.u_rotated(wheel, (90, 0, 0)), (x, .30, z)))
    glass = []
    for side in (-1, 1):
        z = side * .706
        panes = [(-1.31, .96, -.81, 1.41, -.13, 1.423, -.13, .96),
                 (-.06, .96, -.06, 1.423, .54, 1.437, 1.10, .96)]
        for points in panes:
            v = [(points[i], points[i + 1], z) for i in range(0, 8, 2)]
            glass.append((v, [(0, 1, 2, 3)]))
    # Slightly separated sloping front/back panes retain body silhouette.
    glass.append(([(.64, 1.455, -.63), (1.16, .976, -.63),
                  (1.16, .976, .63), (.64, 1.455, .63)], [(0, 1, 2, 3)]))
    glass.append(([(-1.38, .979, -.63), (-.86, 1.426, -.63),
                  (-.86, 1.426, .63), (-1.38, .979, .63)], [(0, 1, 2, 3)]))
    head = [kit.box((2.265, .69, z), (.07, .16, .40)) for z in (-.52, .52)]
    tail = [kit.box((-2.265, .70, z), (.07, .13, .31)) for z in (-.56, .56)]
    return {"DistanceTrafficBody": kit.merge_all(body), "DistanceTrafficGlass": kit.merge_all(glass),
            "DistanceTrafficHead": kit.merge_all(head), "DistanceTrafficTail": kit.merge_all(tail)}


def route_frame(distance):
    points = route_points()
    point = route_point_at(points, distance)
    a = route_point_at(points, max(0, distance - .5))
    b = route_point_at(points, min(points[-1]["distance"], distance + .5))
    dx, dz = b[0] - a[0], b[2] - a[2]
    length = math.hypot(dx, dz)
    return point, (dx / length, dz / length)


def route_at_x(x):
    points = route_points()
    for a, b in zip(points, points[1:]):
        if a["x"] <= x <= b["x"]:
            t = (x - a["x"]) / (b["x"] - a["x"])
            return tuple(a[k] + (b[k] - a[k]) * t for k in ("y", "z", "distance"))
    raise ValueError("Road X sample leaves the source route")


@lru_cache(maxsize=1)
def road_lamps():
    result = []
    # One source rhythm continues across the near/far seam. The visible first
    # fixture uses the ordinary imported City model and shared realtime pool.
    for ordinal in range(260):
        distance = 10 + ordinal * 34
        center, tangent = route_frame(distance)
        if center[0] > 8000:
            break
        if ordinal == 0:
            tangent = (1.0, 0.0)
        side = 1
        outward = (-tangent[1] * side, tangent[0] * side)
        x, z = center[0] + outward[0] * 5.8, center[2] + outward[1] * 5.8
        y = surface_height(x, z)
        result.append({"x": round(x, 6), "y": round(y, 6), "z": round(z, 6),
                       "forward_x": round(-outward[0], 9), "forward_z": round(-outward[1], 9),
                       "real": ordinal == 0, "distance": float(distance)})
    return result


def lamp_template(kit, bp, detail):
    # The City's 5.3 m swept street-lamp silhouette: same taper, elbow, head
    # and 5.00 m/1.07 m lens location. Decorative distance removes fine trim.
    if detail == 0:
        return kit.merge_all((
            ([(0, 0, -.05), (0, 0, .05), (0, 4.72, .05), (0, 4.72, -.05)], [(0, 1, 2, 3)]),
            ([(-.05, 0, 0), (.05, 0, 0), (.05, 4.72, 0), (-.05, 4.72, 0)], [(0, 1, 2, 3)]),
            ([(0, 4.70, -.04), (0, 4.70, .04), (0, 5.30, 1.1), (0, 5.18, 1.1)], [(0, 1, 2, 3)])))
    sides = 8 if detail == 2 else 4
    body = [bp.u_cylinder((0, 2.445 if detail == 2 else 2.365, 0),
                          (.14, 2.285 if detail == 2 else 2.365, .14), sides)]
    for a, b in (((4.70, 0), (5.10, .36)), ((5.10, .36), (5.22, .91))):
        dy, dz = b[0] - a[0], b[1] - a[1]
        tube = bp.u_cylinder((0, 0, 0), (.11, math.hypot(dy, dz) / 2, .11), sides)
        body.append(kit.translated(bp.u_rotated(tube, (math.degrees(math.atan2(dz, dy)), 0, 0)),
                                   (0, (a[0] + b[0]) / 2, (a[1] + b[1]) / 2)))
    body.append(kit.box((0, 5.18, 1.04), (.62, .24, .54)))
    if detail == 2:
        body.append(kit.chamfered_box((0, .11, 0), (.4, .22, .4), .04))
    return kit.merge_all(body)


def lamp_parts(kit, bp):
    geometries = {name: [] for name in ("DistanceLampBody", "DistanceLampLens", "DistanceLampHalo", "DistanceLampPool")}
    templates = {level: lamp_template(kit, bp, level) for level in range(3)}
    for lamp in road_lamps():
        if lamp["real"]:
            continue
        distance = lamp["distance"]
        level = 2 if distance < 400 else 1 if distance < 1450 else 0
        fx, fz = lamp["forward_x"], lamp["forward_z"]

        def placed(geometry):
            vertices, faces = geometry
            return ([(lamp["x"] + x * fz + z * fx, lamp["y"] + y,
                      lamp["z"] - x * fx + z * fz) for x, y, z in vertices], faces)

        geometries["DistanceLampBody"].append(placed(templates[level]))
        if level:
            lens = kit.box((0, 5.00, 1.07), (.38, .15, .35))
        else:
            lens = ([(-.19, 5, .895), (.19, 5, .895), (.19, 5, 1.245), (-.19, 5, 1.245)], [(0, 1, 2, 3)])
        geometries["DistanceLampLens"].append(placed(lens))
        lx, ly, lz = lamp["x"] + fx * 1.07, lamp["y"] + 5, lamp["z"] + fz * 1.07
        half = 1.55
        geometries["DistanceLampHalo"].append((
            [(lx, ly - half, lz - half), (lx, ly - half, lz + half),
             (lx, ly + half, lz + half), (lx, ly + half, lz - half)], [(0, 1, 2, 3)]))
        if distance > 1450:
            continue
        vertices, faces = [], []
        # The pool covers the actual asphalt, subdividing along its centre
        # edge and crossfall instead of intersecting the descent as a plane.
        rows = sorted({distance + (row - 2) * 2.5 for row in range(5)} |
                      {route_at_x(x)[2] for x in XS if distance - 5 < route_at_x(x)[2] < distance + 5})
        for arc in rows:
            p = route_point_at(route_points(), arc)
            for column in range(5):
                lateral = (column - 2) * 1.35
                vertices.append((p[0], p[1] - abs(lateral) * .006 + .012, p[2] + lateral))
        for row in range(len(rows) - 1):
            for column in range(4):
                a = row * 5 + column
                faces.extend(((a, a + 1, a + 6), (a, a + 6, a + 5)))
        geometries["DistanceLampPool"].append((vertices, faces))
    return {name: kit.merge_all(items) for name, items in geometries.items()}


def make_parts(kit, bp):
    return {**terrain(), **road_parts(), **skyline(kit, bp), **traffic(kit, bp), **lamp_parts(kit, bp)}


def metadata():
    return {"panorama_version": 2, "road_width": ROAD_WIDTH, "shoulder_width": SHOULDER_WIDTH,
            "approach": {"gate_x": GATE_X, "flat_end_x": FLAT_END_X, "real_end_x": REAL_END_X,
                         "road_crossfall": .006, "road_ground_lift": .035, "outer_shoulder_offset": .06,
                         "ground_flat_half_width": 10, "ground_blend_half_width": 24},
            "road_route_points": route_points(),
            "road_lamps": road_lamps(),
            "traffic": {"body_length": 4.6, "body_width": 1.8, "body_height": 1.5,
                        "lane_offset": 1.45, "lane_height_offset": -1.45 * .006,
                        "start_distance": 980.0, "end_distance": 1740.0,
                        "source_forward": [1, 0, 0], "max_actors": 2,
                        "visible_witness_distance": 1120.0,
                        "occlusion_cameras": [[-95, 1.7, 0], [-95, 3, 0], [-105, 3, 8],
                                              [-105, 3, -8], [-102, 2.5, 20]],
                        "template_roles": ["DistanceTrafficBody", "DistanceTrafficGlass",
                                           "DistanceTrafficHead", "DistanceTrafficTail"]},
            "relief": {"back_extension": -88, "city_first_rank": 7800,
                       "central_valley_half_slope": .30,
                       "ridge_depths": [90, 2100, 5800], "background_crest": 11800,
                       "surface_contract": "one connected field; road and shoulders own a cutout, sharing edge vertices"},
            "skyline_visibility": {"central_half_width": 1850, "minimum_roof_fraction": .75,
                                   "minimum_lateral_witness_span": 3400,
                                   "cameras": [[-95, 1.7, 0], [-105, 3, -18], [-105, 3, 18]]}}


def route_point_at(points, distance):
    for a, b in zip(points, points[1:]):
        if a["distance"] <= distance <= b["distance"]:
            t = (distance - a["distance"]) / (b["distance"] - a["distance"])
            return tuple(a[k] + (b[k] - a[k]) * t for k in ("x", "y", "z"))
    raise ValueError("Traffic witness leaves the authored route")


def ray_blocked(origin, target, triangles):
    """Actual two-sided triangle intersection; no analytic-height shortcut."""
    ray = tuple(target[i] - origin[i] for i in range(3))
    for a, b, c, n in triangles:
        if min(a[0], b[0], c[0]) > target[0] or max(a[0], b[0], c[0]) < origin[0]:
            continue
        denominator = sum(n[i] * ray[i] for i in range(3))
        if abs(denominator) < 1e-9:
            continue
        t = sum(n[i] * (a[i] - origin[i]) for i in range(3)) / denominator
        if not .00001 < t < .99999:
            continue
        x, z = origin[0] + t * ray[0], origin[2] + t * ray[2]
        area = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
        if abs(area) < 1e-9:
            continue
        u = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / area
        v = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / area
        if u >= -1e-7 and v >= -1e-7 and u + v <= 1.0000001:
            return True
    return False


def surface_triangles(parts):
    triangles = []
    for role in ("DistanceLand", "DistanceRock", "DistanceVegetation", "DistanceRoad", "DistanceShoulder"):
        vertices, faces = parts[role]
        for face in faces:
            a, b, c = (vertices[i] for i in face)
            triangles.append((a, b, c, normal(a, b, c)))
    return triangles


def validate_traffic_occlusion(parts):
    triangles = surface_triangles(parts)
    contract = metadata()["traffic"]
    if contract["end_distance"] - contract["start_distance"] <= 653:
        raise ValueError("Traffic span must outlast the maximum deterministic starting phase")
    points = route_points()
    for distance in (contract["start_distance"], contract["end_distance"]):
        x, y, z = route_point_at(points, distance)
        for camera in contract["occlusion_cameras"]:
            for lane in (-contract["lane_offset"], contract["lane_offset"]):
                for along in (-contract["body_length"] / 2, contract["body_length"] / 2):
                    for side in (-contract["body_width"] / 2, contract["body_width"] / 2):
                        roof = (x + along, y + contract["body_height"], z + lane + side)
                        if not ray_blocked(camera, roof, triangles):
                            raise ValueError("A traffic endpoint can visibly appear/disappear: " + str((distance, camera)))
    x, y, z = route_point_at(points, contract["visible_witness_distance"])
    for lane in (-contract["lane_offset"], contract["lane_offset"]):
        if ray_blocked(contract["occlusion_cameras"][0], (x, y + .8, z + lane), triangles):
            raise ValueError("The traffic route has no visible body between its hidden ends")


def validate_skyline_opening(parts, kit):
    """Regression: correct bounds must not conceal the whole city behind hills."""
    triangles = surface_triangles(parts)
    contract = metadata()["skyline_visibility"]
    witnesses = []
    vertices = parts["DistanceCity"][0]
    # Each authored block is eight vertices; short rooftop caps cannot count
    # as an extra visible building. Test the western facade's true upper edge.
    for start in range(0, len(vertices), 8):
        low, high = kit.bounds((vertices[start:start + 8], []))
        z = (low[2] + high[2]) * .5
        if high[0] < 8250 and high[1] - low[1] > 45 and abs(z) <= contract["central_half_width"]:
            witnesses.append((low[0], high[1] - .5, z))
    witnesses.sort(key=lambda point: point[2])
    if len(witnesses) < 12 or witnesses[-1][2] - witnesses[0][2] < contract["minimum_lateral_witness_span"]:
        raise ValueError("The central skyline lacks a broad group of real building witnesses")
    for camera in contract["cameras"]:
        visible = [not ray_blocked(camera, point, triangles) for point in witnesses]
        if sum(visible) / len(visible) < contract["minimum_roof_fraction"] or not visible[0] or not visible[-1]:
            raise ValueError("Mainland hills conceal the central city: " + str((camera, sum(visible), len(visible))))


def validate_glow_dome(parts, kit):
    low, high = kit.bounds(parts["DistanceGlow"])
    _, city_high = kit.bounds(parts["DistanceCity"])
    if len(parts["DistanceGlow"][0]) != 66 or kit.triangle_count(parts["DistanceGlow"]) != 64:
        raise ValueError("The city glow keeps its single continuous 33-column strip")
    shift = road_grade(7800) - 106
    if low != (9560.0, 70 + shift, -4500.0) or high != (9600.0, 820.0 + shift, 4500.0):
        raise ValueError("The city glow must retain its wide raised atmospheric envelope")
    if low[0] <= city_high[0] or high[0] >= metadata()["relief"]["background_crest"]:
        raise ValueError("The glow must lie behind the city and in front of the background crest")
    triangles = surface_triangles(parts)
    for z in (-1800, 0, 1800):
        if ray_blocked((-95, 1.7, 0), (9560 + abs(z / 4500) * 40, 550 + shift, z), triangles):
            raise ValueError("Mainland relief hides the raised city light dome")


def validate(parts, kit, bp):
    required = {"DistanceLand", "DistanceRock", "DistanceVegetation", "DistanceRoad",
                "DistanceShoulder", "DistanceCity", "DistanceWindows", "DistanceGlow",
                "DistanceTrafficBody", "DistanceTrafficGlass", "DistanceTrafficHead", "DistanceTrafficTail",
                "DistanceLampBody", "DistanceLampLens", "DistanceLampHalo", "DistanceLampPool"}
    if set(parts) != required or sum(kit.triangle_count(g) for g in parts.values()) > 20000:
        raise ValueError("Mainland roles or triangle budget changed")
    for name, geometry in parts.items():
        vertices, faces = geometry
        if not vertices or not faces or any(not math.isfinite(v) for point in vertices for v in point):
            raise ValueError("Empty/nonfinite mainland mesh: " + name)
        for face in faces:
            if any(i < 0 or i >= len(vertices) for i in face):
                raise ValueError("Invalid mainland face index: " + name)
            if sum(v * v for v in normal(*(vertices[i] for i in face[:3]))) < 1e-12:
                raise ValueError("Degenerate mainland face: " + name)
        if name in ("DistanceTrafficBody", "DistanceTrafficHead", "DistanceTrafficTail") and bp.signed_volume(geometry) <= 0:
            raise ValueError("Traffic solid winding failed: " + name)
    for x in XS:
        if x < REAL_END_X:
            continue
        for side in (-5, 5):
            vertex = (x, land_height(x, road_center(x) + side), road_center(x) + side)
            if vertex not in parts["DistanceShoulder"][0]:
                raise ValueError("Shoulder and terrain do not share their authored edge")
        if (x, road_grade(x), road_center(x)) not in parts["DistanceRoad"][0]:
            raise ValueError("Route centre does not lie on the asphalt")
    if road_center(GATE_X) != 0 or road_grade(GATE_X) != 0:
        raise ValueError("The real road starts at the unchanged checkpoint datum")
    if min(v[0] for v in parts["DistanceRoad"][0]) != REAL_END_X:
        raise ValueError("The decorative road must meet the declared real-road end")
    body_low, body_high = kit.bounds(parts["DistanceTrafficBody"])
    if any(abs((body_high[i] - body_low[i]) - expected) > .001
           for i, expected in enumerate((4.6, 1.5, 1.8))):
        raise ValueError("Traffic template metre envelope changed")
    route = route_points()
    if any(b["distance"] <= a["distance"] for a, b in zip(route, route[1:])):
        raise ValueError("Traffic arc length must be strictly monotone")
    if any(b["y"] > a["y"] for a, b in zip(route, route[1:])):
        raise ValueError("The mainland road must descend continuously into the city basin")
    if route[0]["x"] != GATE_X or road_grade(FLAT_END_X) != 0 or road_grade(7800) > -200:
        raise ValueError("The road needs its unchanged stop apron and a genuinely lower city basin")
    lamps = road_lamps()
    if sum(lamp["real"] for lamp in lamps) != 1 or abs(lamps[0]["x"] - FLAT_END_X) > .001:
        raise ValueError("The first street lamp must belong to the real checkpoint approach")
    for previous, lamp in zip(lamps, lamps[1:]):
        if lamp["distance"] - previous["distance"] != 34:
            raise ValueError("Street lamps must keep one continuous metre rhythm")
    for lamp in lamps:
        if abs(lamp["y"] - surface_height(lamp["x"], lamp["z"])) > .001:
            raise ValueError("A mainland lamp is not grounded on the exported surface")
    for x, y, z in parts["DistanceLampPool"][0]:
        if abs(y - surface_height(x, z) - .012) > .0001:
            raise ValueError("A decorative light pool leaves the actual asphalt surface")
    validate_traffic_occlusion(parts)
    validate_skyline_opening(parts, kit)
    validate_glow_dome(parts, kit)
