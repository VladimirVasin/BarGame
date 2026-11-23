"""Metre-authored municipal pipework in Unity XYZ, facing out of walls along +Z."""
import math

import bar_parts as bp
import interior_kit as kit


def box(center, size):
    return bp.u_box(center, size, min(.024, min(size) * .21))


def barrel(start, end, radius, sides=12):
    delta = [b-a for a, b in zip(start, end)]
    length = math.sqrt(sum(v*v for v in delta))
    pitch = math.degrees(math.acos(delta[1]/length))
    yaw = math.degrees(math.atan2(delta[0], delta[2]))
    bevel = min(.012, radius*.12, length*.17)
    profile = ((radius-bevel, -length/2), (radius, -length/2+bevel),
               (radius, length/2-bevel), (radius-bevel, length/2))
    tube = bp.u_rotated(kit.lathe(profile, sides), (-90, 0, 0))
    return kit.translated(bp.u_rotated(tube, (pitch, yaw, 0)),
                          tuple((a+b)/2 for a, b in zip(start, end)))


def water_parts():
    # The pipe module's local Z interval is exactly [0,1]. Supports are
    # independent fixed-metre pieces so stretching a span never stretches bolts.
    groups = {'WaterBarrel': {'SteelDark': barrel((0, 0, 0), (0, 0, 1), .145)}}
    groups['PipeSaddle'] = {
        'SteelLight': kit.merge(barrel((0, 0, -.045), (0, 0, .045), .171),
                                box((0, .27, 0), (.095, .33, .075))),
        'SteelDark': box((0, .45, 0), (.32, .055, .21))}
    groups['WallService'] = {
        'SteelDark': kit.merge_all([
            barrel((0, .36, -.06), (0, .36, .15), .074),
            barrel((0, -.22, .15), (0, .36, .15), .068),
            barrel((0, .30, .15), (0, .38, .15), .079)]),
        'SteelLight': kit.merge(barrel((0, .36, -.028), (0, .36, .03), .115),
                                barrel((0, .07, .15), (0, .115, .15), .083))}
    def lid(radius):
        disc = barrel((0, -.044, 0), (0, .006, 0), radius, 16)
        ribs = [box((x*radius*.28, .009, 0), (.018, .009, radius*(1.45-.14*abs(x))))
                for x in range(-2, 3)]
        return {'SteelDark': disc, 'SteelLight': kit.merge_all(ribs)}
    groups['StopCover'] = lid(.16)
    groups['SewerCover'] = lid(.31)
    # Isolating valve, wheel with an open centre, repair flanges, gauge and feet.
    steel = [barrel((-1.0, .57, 0), (1.0, .57, 0), .148),
             barrel((0, .57, 0), (0, .91, 0), .17),
             barrel((0, .90, 0), (0, 1.10, 0), .052)]
    metal = [barrel((x-.035, .57, 0), (x+.035, .57, 0), .205) for x in (-.85, -.31, .31, .85)]
    for i in range(12):
        a, b = math.tau*i/12, math.tau*(i+1)/12
        start = (.245*math.cos(a), 1.10, .245*math.sin(a))
        end = (.245*math.cos(b), 1.10, .245*math.sin(b))
        metal.append(barrel(start, end, .025, 8))
        if i % 4 == 0:
            metal.append(barrel((0, 1.10, 0), start, .023, 8))
    steel.extend([barrel((.60, .57, 0), (.60, .98, 0), .035),
                  barrel((.60, .99, -.035), (.60, .99, .035), .125)])
    metal.append(barrel((.60, .99, .034), (.60, .99, .046), .106))
    steel.append(barrel((.60, .99, .05), (.65, 1.04, .05), .009, 6))
    for x in (-1, 1):
        steel.append(barrel((x, -.16, 0), (x, .57, 0), .148))
    groups['PumpHeader'] = {'SteelDark': kit.merge_all(steel), 'SteelLight': kit.merge_all(metal),
                            'Concrete': kit.merge_all([box((x, .21, 0), (.37, .47, .41)) for x in (-.57, .57)])}
    groups['VentGrille'] = {'SteelDark': box((0, .38, .018), (.60, .43, .085)),
                            'SteelLight': kit.merge_all([box((0, .23+i*.074, .08), (.52, .032, .095))
                                                        for i in range(5)])}
    groups['StreetBarrel'] = {'SteelService': barrel((0, 0, 0), (0, 0, 1), .145)}
    groups['StreetBend'] = {'SteelService': elbow()}
    groups['PipeCollar'] = {'SteelLight': barrel((0, 0, -.048), (0, 0, .048), .177, 10)}
    groups['WallClamp'] = {'SteelLight': barrel((0, 0, -.041), (0, 0, .041), .164, 10),
                           'SteelDark': kit.merge(box((0, .219, 0), (.08, .145, .065)),
                                                  box((0, .287, 0), (.24, .04, .17)))}
    groups['GroundShoe'] = {'Concrete': box((0, -.385, 0), (.44, .54, .37)),
                            'SteelDark': box((0, -.144, 0), (.37, .047, .33))}
    # Offset hanger, bolted into the concrete outside the stair cheek. Local
    # +X points back to the stair and the angle stays plumb on a pitched run.
    groups['StairBracket'] = {'SteelDark': kit.merge_all([
        box((0,-.355,0),(.075,.49,.078)), box((.146,-.592,0),(.35,.070,.096)),
        box((.306,-.591,0),(.05,.24,.16))])}
    groups['LineValve'] = isolation_valve()
    groups['OutfallHead'] = outfall_head()
    groups['OutfallStream'] = {'Water': outfall_stream()}
    return groups


def outward(vertices, faces):
    if bp.signed_volume(bp.to_source((vertices, faces))) < 0:
        faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def swept_round(rows, sides, closed=False):
    vertices = []
    for centre, radial, cross, radius in rows:
        for i in range(sides):
            a = math.tau*i/sides
            vertices.append(tuple(c + radius*(r*math.cos(a)+v*math.sin(a))
                                  for c, r, v in zip(centre, radial, cross)))
    faces = []
    for i in range(len(rows) if closed else len(rows)-1):
        a, b = i*sides, ((i+1) % len(rows))*sides
        for j in range(sides):
            k = (j+1) % sides
            faces.append((a+j, a+k, b+k, b+j))
    if not closed:
        faces.extend((tuple(reversed(range(sides))), tuple(range(len(vertices)-sides,len(vertices)))))
    return outward(vertices, faces)


def elbow():
    # Pivot is the intersection of the two pipe axes. Inlet +Z turns to +X.
    rows = []
    for i in range(7):
        a = math.pi*i/12
        c, s = math.cos(a), math.sin(a)
        rows.append(((.23*(1-c), 0, .23*(s-1)), (c,0,-s), (0,1,0), .145))
    return swept_round(rows, 12)


def isolation_valve():
    body = barrel((0,0,-.14),(0,0,.14),.185,10)
    bonnet = barrel((0,.09,0),(0,.30,0),.092,8)
    stem = barrel((0,.26,0),(0,.44,0),.029,8)
    wheel, spokes = [], []
    for i in range(12):
        a = math.tau*i/12
        c, s = math.cos(a), math.sin(a)
        centre = (.185*c,.435,.185*s)
        wheel.append((centre,(c,0,s),(0,1,0),.022))
        if i % 4 == 0:
            spokes.append(barrel((0,.435,0),centre,.017,6))
    return {'SteelDark': kit.merge(body,bonnet), 'SteelRust': kit.merge_all([swept_round(wheel,6,True),stem]+spokes)}


def annulus(profile, sides=32):
    # Each row gives axial distance, centre height and radius. Closing the
    # cross-section on its inner radius keeps the throat genuinely hollow.
    vertices = [(r*math.cos(i*math.tau/sides), y+r*math.sin(i*math.tau/sides), z)
                for z, y, r in profile for i in range(sides)]
    faces = []
    for row in range(len(profile)):
        a, b = row*sides, ((row+1) % len(profile))*sides
        for i in range(sides):
            j = (i+1) % sides
            faces.append((a+i, a+j, b+j, b+i))
    return outward(vertices, faces)


def outfall_head():
    cy = .86
    concrete = annulus(((-4.6, -.85, .83), (-2.20, .45, .83), (-1.0, .80, .83),
                        (-.31, cy, .83), (-.26, cy, .88), (.17, cy, .88), (.21, cy, .85),
                        (.21, cy, .726), (.17, cy, .70), (-.31, cy, .70),
                        (-1.0, .80, .70), (-2.20, .45, .70), (-4.6, -.85, .70)))
    rim = annulus(((.025, cy, .70), (.045, cy, .746), (.102, cy, .746),
                   (.122, cy, .726), (.122, cy, .642), (.105, cy, .631), (.025, cy, .631)))
    bars = []
    for i in range(-3, 4):
        x = i*.166
        half = math.sqrt(.671**2-x*x)
        bars.append(barrel((x, cy-half, .055), (x, cy+half, .055), .027, 8))
    for offset in (-.32, .29):
        half = math.sqrt(.68**2-offset**2)
        bars.append(barrel((-half, cy+offset, -.016), (half, cy+offset, -.016), .024, 8))
    bolts = []
    for i in range(8):
        angle = (i+.5)*math.tau/8
        x, y = .691*math.cos(angle), cy+.691*math.sin(angle)
        bolts.append(barrel((x, y, .116), (x, y, .148), .030, 6))
    return {'Concrete': concrete, 'SteelDark': kit.merge(rim, kit.merge_all(bars)),
            'SteelLight': kit.merge_all(bolts),
            'Rubber': barrel((0, .47, -2.18), (0, .47, -2.12), .695, 24)}


def outfall_stream():
    # Solid cross-sections round off the underside of the free jet. The final
    # shallow sections spread across the receiving surface and feather in shader.
    # z, surface height, half-width, thickness at centre, lateral shift
    stations = ((-.56,.67,.565,.43,0), (-.18,.67,.585,.43,0), (.12,.657,.59,.42,0),
                (.30,.64,.567,.39,.005), (.50,.60,.535,.35,.008), (.68,.545,.502,.32,.015),
                (.85,.47,.478,.29,.024), (1.00,.355,.46,.25,.035), (1.13,.22,.48,.185,.048),
                (1.22,.11,.53,.09,.06), (1.34,.094,.68,.072,.069), (1.48,.079,.76,.066,.052),
                (1.70,.063,.77,.059,.03), (1.96,.055,.83,.053,-.045), (2.25,.06,.91,.05,-.12),
                (2.59,.055,.96,.05,-.14), (2.97,.06,1.03,.05,-.095), (3.42,.057,1.10,.05,.01),
                (3.95,.06,1.21,.05,.13), (4.54,.060,1.29,.05,.16), (5.1,.058,1.38,.05,.13))
    vertices, faces = [], []
    # 9 columns across each face, with winding continued under the jet.
    for z, y, width, thickness, cx in stations:
        for bottom in (False, True):
            for col in (range(8, -1, -1) if bottom else range(9)):
                u = (col-4)/4
                dome = math.sqrt(max(0, 1-u*u))
                x = cx + u*width*(1+.042*math.sin(6.4*z+u*2.3))
                top = y - min(.09, thickness*.48)*(1-dome) + .014*dome*math.sin(7*u+4*z)
                vertices.append((x, top-(thickness*(.12+.88*dome) if bottom else 0), z))
    stride = 18
    for row in range(len(stations)-1):
        for i in range(stride):
            a, b = row*stride+i, row*stride+(i+1)%stride
            faces.append((a,b,b+stride,a+stride))
    faces.extend((tuple(reversed(range(stride))),tuple(range(len(vertices)-stride,len(vertices)))))
    return outward(vertices, faces)
