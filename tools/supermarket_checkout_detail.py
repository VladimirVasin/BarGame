"""Static, text-free checkout dressing in Unity metres; no runtime primitives."""
from __future__ import annotations

import math

import bar_parts as bp
import interior_kit as kit


def boxes(specs, chamfer=.008):
    return kit.merge_all([bp.u_box(c, s, chamfer) for c, s in specs])


def pillow(center, size):
    """A sealed inflated wrapper, with a folded waist and thin heat-sealed ends."""
    x, y, z = center
    depth, height, width = size
    vertices, rings = [], []
    for level, broad, thick in ((0, .78, .13), (.055, .86, .16),
                                (.20, 1, .86), (.62, .96, 1),
                                (.86, .94, .70), (.945, .82, .16),
                                (1, .78, .13)):
        ring = []
        for i in range(8):
            angle = math.tau * i / 8
            ring.append(len(vertices))
            vertices.append((x + math.cos(angle) * depth * thick / 2,
                             y + (level - .5) * height,
                             z + math.sin(angle) * width * broad / 2))
        rings.append(ring)
    faces = [tuple(rings[0]), tuple(reversed(rings[-1]))]
    for low, high in zip(rings, rings[1:]):
        for i in range(8):
            j = (i + 1) % 8
            faces.append((low[j], low[i], high[i], high[j]))
    geometry = vertices, faces
    assert bp.signed_volume(geometry) > 0
    return geometry


def build_checkout(asset, add_part, centre, size):
    x, _, z = centre
    width, height, length = size
    def add(name, geometry, sheet, tint, role="checkout", **kwargs):
        return add_part(asset, name, geometry, role, sheet, tint, **kwargs)

    # Three separate working zones, a recessed plinth, and a steel top with a
    # real relief edge. The open employee-side recess keeps knees off the case.
    add("Checkout Counter", boxes([
        ((x-.04, .53, z), (width-.08, .94, length-.04)),
        ((x, height-.035, z), (width, .07, length)),
        ((x-.015, .075, z), (width-.19, .15, length-.19)),
    ], .014), "Counter", (.31, .38, .32, 1))
    add("Checkout Front Panels", boxes([
        ((x-width/2+.014, .58, z+offset), (.026, .66, .91))
        for offset in (-1.03, 0, 1.03)
    ], .004), "Counter", (.53, .49, .34, 1))

    metal = [
        ((x-width/2+.018, height+.026, z), (.035, .052, length-.08)),
        ((x+width/2-.018, height+.026, z+.88), (.035, .052, 1.25)),
        ((x, height+.026, z+length/2-.03), (width-.04, .052, .045)),
        ((x, height+.026, z-length/2+.03), (width-.04, .052, .045)),
        ((x, height+.026, z-.58), (width-.035, .052, .04)),
        ((x, height+.026, z+.55), (width-.035, .052, .04)),
        # Scanner window frame, receipt printer, cash drawer, coin tray.
        ((x-.08, height+.035, z+.37), (.29, .055, .32)),
        ((x+.19, height+.11, z+.93), (.23, .16, .22)),
        ((x+.05, height+.045, z+.84), (.56, .07, .55)),
        ((x-.20, height+.045, z-.38), (.23, .06, .30)),
    ]
    # Bag rack at the packing end, with a clear hand contact zone at z=-2.25.
    for zz in (z-1.30, z-1.06):
        metal.extend([((x+.25, 1.22, zz), (.028, .34, .028)),
                      ((x+.13, 1.38, zz), (.24, .025, .025))])
    add("Checkout Rails Scanner Tray And Bag Rack", boxes(metal, .005),
        "ShelfMetal", (.47, .49, .44, 1), "bag_rack")
    add("Checkout Conveyor Belt", boxes([
        ((x-.005, height+.018, z+.99), (.73, .032, 1.10)),
        ((x-.005, height+.018, z-1.10), (.73, .032, .88)),
        ((x-.08, height+.066, z+.37), (.21, .014, .24)),
        ((x-.20, height+.080, z-.38), (.17, .014, .24)),
    ], .006), "", (.075, .085, .075, 1))

    # Operator keyboard is on +X, facing the cashier. Customer display faces -X.
    add("Checkout Register", boxes([
        ((x+.06, height+.16, z+.78), (.39, .23, .43)),
        ((x+.06, height+.30, z+.80), (.14, .14, .30)),
        ((x+.07, height+.42, z+.80), (.10, .27, .38)),
        ((x-.31, height+.23, z+.76), (.05, .30, .035)),
        ((x-.31, height+.38, z+.76), (.065, .13, .25)),
    ], .010), "Counter", (.23, .26, .24, 1), "register")
    add("Checkout Register Keys", boxes([
        ((x+.266, height+.102+row*.041, z+.65+column*.071),
         (.013, .025, .047))
        for row in range(3) for column in range(4)
    ], .002), "", (.56, .55, .45, 1), "register", casts_shadows=False)
    add("Checkout Register Display", boxes([
        ((x+.126, height+.42, z+.80), (.012, .18, .29)),
        ((x-.349, height+.38, z+.76), (.012, .075, .19)),
    ], .002), "", (.24, .31, .22, 1), "register", emissive=True,
        casts_shadows=False)
    # Paper has actual thickness and bends. No printed signs or readable text.
    paper = [((x+.195, height+.20, z+.93), (.17, .014, .15)),
             ((x+.195, height+.16, z+.835), (.17, .095, .014))]
    for i in range(4):
        paper.append(((x+.10-i*.018, 1.20, z-1.17), (.018, .30, .23)))
    paper.extend([((x+.22, .80, z-1.26), (.20, .18, .21)),
                  ((x+.22, .82, z-1.00), (.20, .18, .21))])
    add("Checkout Paper Bags And Till Rolls", boxes(paper, .003),
        "Cardboard", (.65, .64, .52, 1), "bag_rack", casts_shadows=False)

    # A low stepped impulse display below the cashier's face and away from palms.
    stands = []
    for i in range(3):
        stands.append(((x-.23+i*.105, 1.085+i*.043, z-1.04),
                       (.11, .045, .72)))
    # Chips hang against the customer panel inside the counter footprint.
    for zz in (z-.25, z+.25):
        stands.append(((x-.41, .64, zz), (.022, .63, .018)))
    for yy in (.35, .62, .89):
        stands.append(((x-.41, yy, z), (.022, .018, .56)))
    add("Checkout Decorative Snack Trays And Wire Rack", boxes(stands, .003),
        "ShelfMetal", (.24, .28, .25, 1), casts_shadows=False)
    snack_groups = [[], [], [], []]
    seals = []
    # Twenty-four bars/chocolate/gum packs and eight small chip bags.
    for row in range(3):
        for column in range(8):
            xx = x-.23+row*.105
            yy = 1.13+row*.043
            zz = z-1.34+column*.087
            centre_pack = (xx, yy, zz)
            geom = pillow(centre_pack, (.070, .085 if row == 0 else .065, .073))
            snack_groups[(row+column)%4].append(geom)
            seals.append(((xx-.036, yy, zz), (.008, .025, .058)))
    for row in range(2):
        for column in range(4):
            xx, yy, zz = x-.405, .50+row*.28, z-.225+column*.15
            snack_groups[(row+column+1)%4].append(pillow(
                (xx, yy, zz), (.065, .235, .142)))
            seals.append(((xx-.035, yy+.03, zz), (.008, .070, .095)))
    colors = ((.46, .29, .18, 1), (.57, .47, .23, 1),
              (.32, .40, .31, 1), (.43, .26, .27, 1))
    for i, (geometries, color) in enumerate(zip(snack_groups, colors), 1):
        add(f"Checkout Decorative Snack Wrappers {i:02d}",
            kit.merge_all(geometries), "Cardboard", color, casts_shadows=False)
    add("Checkout Decorative Wrapper Seals", boxes(seals, .001),
        "Cardboard", (.64, .59, .43, 1), casts_shadows=False)

    # Wear stays limited to contact edges, panel feet and register-side hardware.
    wear = [((x-width/2+.006, .17, z+offset), (.012, .055, .38))
            for offset in (-1.05, .20, 1.15)]
    wear.extend([((x+.39, 1.058, z-.13), (.11, .012, .38)),
                 ((x+.41, .84, z+.80), (.018, .06, .16))])
    add("Checkout Contact Wear And Cable Ports", boxes(wear, .002),
        "Counter", (.23, .25, .22, 1), casts_shadows=False)

    # A visible practical over the warm pool. It belongs to trim rather than the
    # four fluorescent rows; lighting is still owned by the scene's six sources.
    lamp = [((6.05, 2.70, -2.10), (.52, .07, .56)),
            ((6.05, 3.035, -2.10), (.035, .60, .035)),
            ((6.05, 3.36, -2.10), (.20, .05, .20))]
    for dz in (-.27, .27):
        lamp.append(((6.05, 2.62, -2.10+dz), (.52, .14, .025)))
    for dx in (-.25, .25):
        lamp.append(((6.05+dx, 2.62, -2.10), (.025, .14, .54)))
    add("Checkout Warm Practical Housing", boxes(lamp, .005),
        "ShelfMetal", (.27, .30, .27, 1), "trim")
    add("Checkout Warm Practical Diffuser", bp.u_box(
        (6.05, 2.635, -2.10), (.43, .025, .45), .003),
        "", (.76, .58, .32, 1), "trim", emissive=True, casts_shadows=False)


def build_environment(asset, add_part):
    def add(name, geometry, sheet, tint, **kwargs):
        return add_part(asset, name, geometry, "trim", sheet, tint, **kwargs)
    # Recessed service routes explain the hardware instead of free-floating boxes.
    services = [((7.84, 2.03, -.40), (.06, .045, 7.20)),
                ((7.83, 1.16, -1.50), (.07, 1.77, .045)),
                ((7.80, .84, -1.50), (.13, .20, .20)),
                ((7.82, 2.60, 1.80), (.08, .46, .70)),
                ((7.82, 2.60, -4.20), (.08, .46, .70))]
    # Vented cover relief is shallow so the shop's protected paths do not change.
    for zz in (1.80, -4.20):
        for i in range(6):
            services.append(((7.765, 2.44+i*.059, zz), (.035, .025, .60)))
    services.extend([((6.87, 3.35, -2.10), (1.65, .035, .04)),
                     ((7.72, 2.71, -2.10), (.035, 1.29, .04))])
    add("Wall Service Conduit And Ventilation", boxes(services, .004),
        "ShelfMetal", (.35, .37, .32, 1))
    # Basket stack at the front-right edge, clear of the door and cross-aisle.
    basket_frames, basket_dark = [], []
    for i in range(5):
        xx, yy, zz = 3.10, .13+i*.072, -5.02
        basket_frames.extend([
            ((xx, yy, zz), (.54, .025, .39)),
            ((xx-.265, yy+.085, zz), (.025, .17, .39)),
            ((xx+.265, yy+.085, zz), (.025, .17, .39)),
            ((xx, yy+.085, zz-.185), (.51, .17, .025)),
            ((xx, yy+.085, zz+.185), (.51, .17, .025)),
        ])
        for j in range(5):
            basket_dark.append(((xx-.20+j*.10, yy+.09, zz-.201),
                                (.05, .070, .012)))
    add("Entrance Stacked Shopping Baskets", boxes(basket_frames, .006),
        "Counter", (.29, .35, .30, 1))
    add("Entrance Basket Openings And Handles", boxes(basket_dark+[
        ((3.10, .55, -4.84), (.43, .025, .025)),
        ((2.88, .47, -4.84), (.025, .16, .025)),
        ((3.32, .47, -4.84), (.025, .16, .025)),
    ], .003), "", (.12, .16, .13, 1), casts_shadows=False)
