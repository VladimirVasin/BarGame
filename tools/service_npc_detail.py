"""Constructed clothes and anatomy for the ordinary clerk and route driver.

Recipes stay in the generators' original source space. Their existing anatomy
remap, renderer names and rigid bindings remain the owners of runtime motion.
"""
from __future__ import annotations

import math

import npc_detail_geometry as detail
from mathutils import Vector


def cloth_panel(geometry, bulge=.008):
    """Tailored closed panel with a curved face and a tucked lower edge."""
    points, _ = geometry
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    rings = []
    for t, width in ((0, .90), (.06, .98), (.22, 1), (.45, .94),
                     (.68, .97), (.87, 1), (1, .91)):
        z = lo.z + (hi.z - lo.z) * t
        ring = []
        for back in (False, True):
            for j in (range(7) if not back else reversed(range(7))):
                u = j / 6
                x = (lo.x + hi.x) * .5 + (u - .5) * (hi.x - lo.x) * width
                curve = bulge * math.sin(math.pi * u) * math.sin(math.pi * t)
                y = (hi.y if back else lo.y) - curve
                ring.append((x, y, z))
        rings.append(ring)
    return detail.loft(rings)


def _limb(start, end, lower, upper, *, trouser=False):
    profile = ((0, 1), (.06, 1.04), (.20, 1.09), (.36, 1.02),
               (.54, .99), (.70, 1.04), (.83, .97), (.94, 1.02), (1, .98))
    return detail.segment(start, end,
        tuple((t, (lower + (upper - lower) * t) * swell,
               .90 if trouser else .94) for t, swell in profile), 16)


def _head(geometry):
    points, _ = geometry
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center = (lo + hi) * .5
    # Leave a thin physical allowance for the combed hair cap at the crown.
    hi.z -= .003
    rx, ry = (hi.x - lo.x) * .5, (hi.y - lo.y) * .5
    stations = ((0, .19, .40), (.07, .44, .66), (.18, .71, .81),
                (.32, .87, .91), (.48, .98, 1), (.62, 1, .99),
                (.76, .96, .92), (.88, .79, .77), (.96, .44, .44),
                (1, .015, .015))
    points, faces = detail.shell(tuple((lo.z + (hi.z - lo.z) * t,
        rx * x, ry * y, center.y) for t, x, y in stations), 20)
    return [p + Vector((center.x, 0, 0)) for p in points], faces


def _hair_cap(cashier):
    """A scalp-following comb-over with a high front hairline and covered back."""
    if cashier:
        cx, cy, bottom, height, rx, ry = .006, -.028, 1.5196, .2304, .08925, .0819
    else:
        cx, cy, bottom, height, rx, ry = .008, -.032, 1.430, .320, .154, .124
    profile = ((.62, 1, .99), (.76, .96, .92), (.88, .79, .77),
               (.96, .44, .44), (1, .015, .015))

    def width_at(t, axis):
        for left, right in zip(profile, profile[1:]):
            if t <= right[0]:
                q = (t-left[0]) / (right[0]-left[0])
                return left[axis] + (right[axis]-left[axis])*q
        return profile[-1][axis]

    rings = []
    for index, fraction in enumerate((.67, .82, .91, .97, .995, 1)):
        ring = []
        for j in range(20):
            angle = math.tau * j / 20
            front = max(0, -math.sin(angle))
            # A high swept forehead edge, lower behind the ears; all rings
            # follow the cranium so the retained hairstyle never disappears.
            t = fraction + (.105 * front if index == 0 else 0)
            ridge = .002 * math.sin(angle*2 + .4) if index in (1, 2) else 0
            pad = (.003 if cashier else .012) if index < 5 else 0
            ring.append((cx + (rx*width_at(t, 1)+pad)*math.cos(angle),
                         cy + (ry*width_at(t, 2)+pad)*math.sin(angle),
                         bottom + height*t + ridge))
        rings.append(ring)
    return detail.loft(rings)


def _vest_front(sign):
    rings = []
    for z, rx, ry, cy in ((.780, .157, .102, .007), (.815, .168, .107, .006),
                         (.940, .165, .112, .001), (1.075, .164, .120, -.002),
                         (1.205, .180, .122, -.003), (1.310, .185, .113, -.004)):
        ring = []
        for inner in (False, True):
            for j in (range(9) if not inner else reversed(range(9))):
                u = j / 8
                x = sign * (.009 + u * (rx-.009))
                y = cy - ry * math.sqrt(max(0, 1-(x/rx)**2))
                neckline = .082 * (1-u)**2 if z > 1.30 else 0
                ring.append((x, y + (.008 if inner else 0), z-neckline))
        rings.append(ring)
    return detail.loft(rings)


def _conform_front(geometry, rx=.171, ry=.117, cy=.001, inset=.006):
    points, faces = geometry
    low = min(p[1] for p in points)
    points = [Vector((p[0], cy - ry * math.sqrt(max(.01, 1-(p[0]/rx)**2))
                      - inset + (p[1]-low), p[2])) for p in points]
    return detail.outward((points, faces))


def geometry_for(base, name, geometry, *, cashier=False):
    """Replace a named ordinary part while retaining its external contract."""
    side = name.rsplit(".", 1)[-1]
    sign = 1 if side == "L" else -1
    shoulder = (sign * .208, -.004 if side == "L" else .004, 1.292)
    elbow = (sign * .470, -.010, 1.175)
    wrist = (sign * .680, -.018, 1.075)
    fingertip = (sign * .755, -.022, 1.035)
    hip = (sign * .083, .012 if side == "L" else -.004, .750)
    knee = (sign * .103, -.012 if side == "L" else .012, .354)
    ankle = (sign * .112, -.026 if side == "L" else .018, .095)
    if name == "GEO_Head":
        return _head(geometry)
    if name == "GEO_Torso":
        stations = ((.790, .151, .091, .010), (.835, .163, .098, .008),
                    (.930, .160, .102, .002), (1.06, .156, .110, -.002),
                    (1.20, .172, .113, -.003), (1.29, .211, .103, -.004),
                    (1.335, .173, .091, -.006))
        if cashier:
            stations += ((1.375, .110, .076, -.009), (1.405, .078, .067, -.010))
        torso = detail.shell(stations, 20, folds=.016, fold_count=5)
        return base.combine_geometry(torso, *(detail.outward(base.make_ellipsoid(
            (side * .181, -.004, 1.301), (.064, .084, .066), 12, 6))
            for side in (-1, 1)))
    if name == "GEO_Pelvis":
        return detail.shell(((.665, .137, .087, .012), (.695, .154, .098, .012),
            (.740, .165, .105, .011), (.80, .158, .099, .010),
            (.835, .155, .094, .010), (.850, .151, .090, .010)), 20)
    if name.startswith("GEO_UpperArm."):
        return _limb(shoulder, elbow, .069, .055)
    if name.startswith("GEO_Forearm."):
        return _limb(elbow, wrist, .057, .042)
    if name.startswith("GEO_Thigh."):
        return _limb(hip, knee, .090, .069, trouser=True)
    if name.startswith("GEO_Shin."):
        return _limb(knee, ankle, .071, .053, trouser=True)
    if name.startswith(("GEO_Hand.", "GEO_Thumb.")):
        hand = detail.hand(wrist, fingertip, side)
        if name.startswith("GEO_Thumb."):
            return hand["thumb"]
        return base.combine_geometry(hand["palm"], *(hand[f"finger{i}"] for i in range(4)))
    if name.startswith("GEO_Foot."):
        # Anatomical toe/instep/heel, with a sewn vamp and separate heel counter.
        shoe = base.make_cafe_shoe(sign * .112, .89, 1.10, .90,
                                  -.005 if side == "L" else .005)
        seams = []
        for offset in (-.044, .044):
            seams.append(detail.segment((sign * .112 + offset, -.157, .072),
                (sign * .112 + offset * .75, -.039, .118),
                ((0, .003, .70), (.5, .0034, .7), (1, .0026, .7)), 6))
        return detail.outward(base.combine_geometry(shoe, *seams))
    if name.startswith("CLO_ShoeSole."):
        return detail.outward(base.make_cafe_shoe(sign * .112, .91, 1.15, .15,
                                  -.005 if side == "L" else .005))
    if name.startswith(("CLO_VestFront.", "CLO_JacketFront.")):
        panel = _vest_front(sign)
        x = sign * .090
        pocket = _conform_front(cloth_panel(base.make_box((x, -.133, .989),
            (.084, .006, .079)), .003), inset=.009)
        welt = _conform_front(detail.segment(
            (x - .041, -.143, 1.025), (x + .041, -.143, 1.025),
            ((0, .0035, .8), (.08, .004, .8), (.92, .004, .8), (1, .0035, .8)), 6),
            inset=.011)
        return base.combine_geometry(panel, pocket, welt)
    if name == "CLO_ChestPocket.R":
        return _conform_front(cloth_panel(geometry, .003), rx=.180, ry=.122, cy=-.003)
    if name == "CLO_ShirtBib":
        # A cloth inset in the shirt, not a rigid plaque in front of it.
        points, faces = cloth_panel(geometry, 0)
        low = min(p.y for p in points)
        flattened = [(p.x, low + (p.y-low)*.06, p.z) for p in points]
        return _conform_front((flattened, faces), rx=.18, ry=.112, cy=-.003, inset=.001)
    if name == "CLO_VestBack":
        return cloth_panel(geometry, .004)
    if name == "CLO_Belt":
        return detail.shell(((.783, .162, .105, .01), (.788, .165, .108, .01),
                             (.824, .163, .105, .01), (.829, .159, .101, .01)), 20)
    if name == "HAIR_FlatCombover":
        return _hair_cap(True)
    if name == "HAIR_SweptTuft.L":
        points, faces = _hair_cap(False)
        # The driver predates Human V2: his skull uses its landmark map,
        # whereas hairwear retains a crown-relative map. Compensate in source
        # space so the new scalp follows the same final skull surface.
        return [Vector((p.x, p.y, 1.75 - (1.75 -
            base.remap_piecewise(p.z, base.NPC_HEAD_Z_LANDMARKS)) / .90))
            for p in points], faces
    return geometry


def validate_hands(result):
    """Each palm renderer contains five closed islands: palm + four fingers."""
    for side in ("L", "R"):
        part = next(p for p in result.parts if p.obj.name == f"GEO_Hand.{side}")
        mesh = part.obj.data
        neighbors = {i: set() for i in range(len(mesh.vertices))}
        for edge in mesh.edges:
            a, b = edge.vertices
            neighbors[a].add(b)
            neighbors[b].add(a)
        remaining, islands = set(neighbors), 0
        while remaining:
            pending = [remaining.pop()]
            islands += 1
            while pending:
                for other in neighbors[pending.pop()] & remaining:
                    remaining.remove(other)
                    pending.append(other)
        if islands != 5 or part.bone != f"hand.{side}":
            raise RuntimeError(f"{part.obj.name} lost its palm/four-finger rigid contract")
        thumb = next(p for p in result.parts if p.obj.name == f"GEO_Thumb.{side}")
        if thumb.bone != part.bone or len(thumb.obj.data.vertices) < 24:
            raise RuntimeError(f"{thumb.obj.name} lost its opposed articulated volume")
