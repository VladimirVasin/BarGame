"""Old avalanche in expansion-local metres: a closed toe, fan and buried timber.

Unity +Z points uphill. ``surface`` parts follow terrain at every vertex;
``rigid`` parts receive the terrain delta at their one support point. The
footprint is also the runtime movement/forest/snow contract, never a hidden wall.
"""
from __future__ import annotations

import math
import bar_parts as bp
import interior_kit as kit
from village_abandoned_buildings import _beam

ORIGIN = (-158, 110)
FOOTPRINT = ((-10, 0), (-5, -1), (1, 0), (6, -.5), (9.5, 1.5),
             (9.5, 5), (18, 6.5), (19, 11), (19, 17), (19, 25),
             (-19, 25), (-19, 14), (-18, 8), (-12, 5))


def _merge(pieces):
    pieces = list(pieces)
    for piece in pieces:
        assert bp.signed_volume(piece) > 1e-9, "Inward avalanche component"
    return kit.merge_all(pieces)


def _outward(vertices, faces):
    g = vertices, faces
    if bp.signed_volume(g) < 0:
        g = vertices, [tuple(reversed(f)) for f in faces]
    assert bp.signed_volume(g) > 1e-9
    return g


def _height(x, z):
    # A steep compacted toe, broad irregular pressure ridges, then a thin scar
    # on the mountain. This is thickness ABOVE the sampled ground, not world Y.
    front = 4.55 + .53*math.sin(x*.72+z*.33) + .29*math.cos(x*1.43-z*.61)
    taper = max(.065, min(1, (25.8-z)/15.0))
    side = 1-.46*min(1, (abs(x)/19)**4)
    return max(.24, front*taper*side)


def _bed():
    boundary = []
    for a, b in zip(FOOTPRINT, FOOTPRINT[1:]+FOOTPRINT[:1]):
        count = math.ceil(math.dist(a, b)/1.7)
        boundary.extend((a[0]+(b[0]-a[0])*i/count,
                         a[1]+(b[1]-a[1])*i/count) for i in range(count))
    count = len(boundary)
    vertices = [(x, -1, z) for x, z in boundary]
    # The foot remains on the exact polygon, while crushed upper lobes recede
    # by different amounts. At waist height the setback remains <.6 m; the
    # broken face is steep enough to stop a walker and has no cut-slab wall.
    for band in (0, 1, 2):
        for x, z in boundary:
            scale, fraction = _wall_band(x,z,band)
            xx, zz = x*scale, 12+(z-12)*scale
            vertices.append((xx, .10 if band == 0 else _height(xx,zz)*fraction, zz))
    # Several measured rings avoid long polygons being warped across the
    # ground-to-wall bend. Every boundary XZ stays exactly on the plan polygon.
    for ring_scale in (1, .82, .64, .46, .28, .10):
        for x, z in boundary:
            scale = ring_scale*(.915+.028*math.sin(x*.63+z*.71))
            xx, zz = x*scale, 12+(z-12)*scale
            vertices.append((xx, _height(xx, zz), zz))
    faces = []
    for ring in range(9):
        outside, inside = ring*count, (ring+1)*count
        for i in range(count):
            j = (i+1) % count
            faces.extend(((outside+i, inside+i, inside+j),
                          (outside+i, inside+j, outside+j)))
    top = len(vertices)
    vertices.append((0, _height(0, 12), 12))
    bottom = len(vertices)
    vertices.append((0, -1, 12))
    for i in range(count):
        j = (i+1) % count
        faces.extend(((9*count+i, top, 9*count+j), (i, j, bottom)))
    return _outward(vertices, faces)


def _wall_band(x,z,band):
    if band == 0:
        return 1, 0
    variation = .5+.5*math.sin(x*.71+z*.64)
    return ((.982-.012*variation, .37) if band == 1 else
            (.952-.022*variation, .77))


def _dirty_face_strata():
    pieces=[]
    # Broad settled sediment in the toe is discontinuous, not a striped ice
    # glacier. Each patch follows the actual lobed face and has finite depth.
    for index,(a,b) in enumerate(zip(FOOTPRINT[:5],FOOTPRINT[1:6])):
        if index == 3:
            continue
        points=[]
        for band in (1,2):
            for x,z in (a,b):
                scale,fraction = _wall_band(x,z,band)
                xx,zz = x*scale,12+(z-12)*scale
                points.append((xx,_height(xx,zz)*fraction,zz))
        # Offset only two centimetres off the body; a back shell closes the
        # colour-bearing surface without affecting the movement contour.
        outer=[(x,y,z-.025) for x,y,z in points]
        vertices=outer+[(x,y,z+.035) for x,y,z in points]
        faces=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(1,5,7,3),(3,7,6,2),(2,6,4,0)]
        pieces.append(_outward(vertices,faces))
    return _merge(pieces)


def _patch(cx, cz, rx, rz, thickness, phase):
    """Closed small mantle, vertically following the same snow-bed function."""
    points = []
    for i in range(12):
        angle = i*math.tau/12
        irregular = 1+.13*math.sin(i*2.7+phase)
        points.append((cx+math.cos(angle)*rx*irregular,
                       cz+math.sin(angle)*rz*irregular))
    vertices = [(x, _height(x, z)-.12, z) for x, z in points]
    vertices += [(x, _height(x, z)+thickness*.35, z) for x, z in points]
    vertices.append((cx, _height(cx, cz)+thickness, cz))
    vertices.append((cx, _height(cx, cz)-.12, cz))
    # Neither cap is planar because it follows the avalanche relief. Explicit
    # centre fans keep the topology independent of the FBX/Unity n-gon solver.
    faces = []
    for i in range(12):
        j = (i+1) % 12
        faces += [(i,j,12+j),(i,12+j,12+i),(12+i,12+j,24),(j,i,25)]
    return _outward(vertices, faces)


def _tube(a, b, radius, tip, sides=8, bend=.06):
    """Irregular tapered, capped round timber; no cylinder-shaped cut ends."""
    delta = tuple(b[i]-a[i] for i in range(3))
    length = math.sqrt(sum(d*d for d in delta))
    along = tuple(d/length for d in delta)
    horizontal = math.hypot(along[0], along[2])
    side = (along[2]/horizontal, 0, -along[0]/horizontal) if horizontal > .001 else (1, 0, 0)
    up = (along[1]*side[2]-along[2]*side[1],
          along[2]*side[0]-along[0]*side[2],
          along[0]*side[1]-along[1]*side[0])
    vertices = []
    for ring, t in enumerate((0, .32, .71, 1)):
        r = radius+(tip-radius)*t
        center = tuple(a[j]+delta[j]*t+side[j]*math.sin(t*math.pi)*bend for j in range(3))
        for i in range(sides):
            angle = i*math.tau/sides
            rr = r*(1+.10*math.sin(i*2.1+ring*.53))
            vertices.append(tuple(center[j]+rr*(math.cos(angle)*side[j]+math.sin(angle)*up[j])
                                  for j in range(3)))
    faces = [tuple(reversed(range(sides))), tuple(range(3*sides, 4*sides))]
    for ring in range(3):
        for i in range(sides):
            j = (i+1) % sides
            faces.append((ring*sides+i, ring*sides+j, (ring+1)*sides+j, (ring+1)*sides+i))
    return _outward(vertices, faces)


def _rock(center, size, phase):
    vertices = []
    for layer, (height, radius) in enumerate(((-.5, .59), (-.17, 1), (.30, .84), (.50, .38))):
        for i in range(7):
            angle = i*math.tau/7
            r = radius*(1+.13*math.sin(i*2.29+phase+layer))
            vertices.append((center[0]+math.cos(angle)*size[0]*r*.5,
                             center[1]+height*size[1],
                             center[2]+math.sin(angle)*size[2]*r*.5))
    faces = [tuple(range(7)), tuple(reversed(range(21, 28)))]
    for layer in range(3):
        for i in range(7):
            j = (i+1) % 7
            faces.append((layer*7+i, (layer+1)*7+i, (layer+1)*7+j, layer*7+j))
    return _outward(vertices, faces)


def _fallen_tree(add, index, a, b, radius):
    support = ((a[0]+b[0])*.5, 0, (a[2]+b[2])*.5)
    delta = tuple(b[i]-a[i] for i in range(3))
    length = math.sqrt(sum(v*v for v in delta))
    horizontal = math.hypot(delta[0], delta[2])
    side = (delta[2]/horizontal, 0, -delta[0]/horizontal)
    timber = [_tube(a, b, radius, .09, 9, .16)]
    # Most branches are broken short. Remaining long ones preserve the full
    # tree scale without recreating a green ornamental conifer on its side.
    for i in range(13):
        t = .13+i*.060
        base = tuple(a[j]+delta[j]*t for j in range(3))
        sign = -1 if (i+index) % 2 else 1
        reach = (.65+(i % 4)*.40)*(1-t*.30)
        end = (base[0]+side[0]*reach*sign-delta[0]*.048,
               base[1]+.28+(i % 3)*.20,
               base[2]+side[2]*reach*sign-delta[2]*.048)
        timber.append(_tube(base, end, radius*(1-t)*.32, .024, 6))
        if i % 3 == 1:
            twig = (end[0]+side[0]*sign*.43, end[1]-.13, end[2]+side[2]*sign*.43-.42)
            timber.append(_tube(end, twig, .048, .012, 5))
    # A ragged root plate is attached to the uphill butt, with earth caught in
    # roots. It makes the trees read as uprooted, not stacked sawmill lumber.
    for i in range(8):
        angle = i*math.tau/8+index*.3
        end = (a[0]+side[0]*math.cos(angle)*1.15-delta[0]/length*.37,
               a[1]+math.sin(angle)*1.02,
               a[2]+side[2]*math.cos(angle)*1.15-delta[2]/length*.37)
        timber.append(_tube(a, end, radius*.35, .035, 6))
    add("Avalanche", "FallenConifer%02d" % index, _merge(timber), "AbandonedWood",
        True, (.255,.245,.205,1), terrain_fit="rigid", support=support)
    earth = _rock(a, (radius*3.0, radius*3.0, radius*2.8), index)
    add("Avalanche", "RootEarth%02d" % index, earth, "LayeredStone", True,
        (.255,.27,.25,1), terrain_fit="rigid", support=support)


def build_avalanche(add):
    add("Avalanche", "CompactedSnowAndDebris", _bed(), "WindSnow", True,
        (.665,.69,.665,1), terrain_fit="surface")
    add("Avalanche", "OldSedimentInBrokenToe", _dirty_face_strata(), "LayeredStone", False,
        (.515,.53,.49,1), terrain_fit="surface")
    # Unequal ribbons leave dirt-dark folds exposed between old compacted snow
    # and the most recent snowfall; broad value shapes survive storm/PS1 fog.
    for i, (x,z,rx,rz) in enumerate(((-5,2.4,3.4,1.5),(2,3.0,4.1,1.9),
            (-8,7.5,4.2,2.4),(5.8,8.1,3.5,2.8),(-2,11.2,4.1,2.5),
            (-11,14.3,3.2,2.3),(10.4,15.2,3.6,3),(-2,17.3,5.4,2.7),
            (-8,21.5,3.4,2.4),(6,22,5.0,2.1))):
        add("Avalanche", "WindSnowMantle%02d" % i, _patch(x,z,rx,rz,.24,i),
            "WindSnow", False, (.825,.845,.825,1), terrain_fit="surface")
    for i, (x,z,rx,rz) in enumerate(((-1.3,5.8,2.1,.64),(9.4,10.7,2.5,.72),
                                    (-8.8,11.9,2.2,.58),(2.2,20.2,1.5,2.7))):
        add("Avalanche", "ExposedGravelSeam%02d" % i, _patch(x,z,rx,rz,.07,i),
            "LayeredStone", False, (.43,.45,.405,1), terrain_fit="surface")
    trees = (((-8.0,4.50,12.1),(6.8,4.65,2.4),.44),
             ((7.0,4.65,12.4),(-7.9,4.7,3.0),.38),
             ((-12.5,3.8,11.9),(8.3,4.3,6.9),.49),
             ((-8.3,3.7,10.7),(-1.9,4.9,.45),.38),
             ((10.6,3.1,12.0),(-3.1,4.35,8.1),.40))
    for index, (a,b,radius) in enumerate(trees):
        _fallen_tree(add,index,a,b,radius)
    # Large angular rocks embedded at the toe, with their dark undersides and
    # a few smaller fragments visible between snow lobes. No fresh bright cuts.
    for i, (x,y,z,sx,sy,sz) in enumerate(((-7.5,3.0,1.25,3.6,3.5,2.6),
            (-2.8,3.1,1.0,2.7,3.0,2.3),(3.4,3.05,1.7,3.0,3.8,2.8),
            (7.8,3.1,3.6,2.7,3.6,2.1),(-10.4,3.7,7.2,3.5,2.5,3.2),
            (12.6,3.2,8.8,4.8,2.8,3.5),(-3.4,4.5,8.4,2.2,1.7,2.5),
            (-12.8,3.4,9.6,3.5,2.1,3.2),(4.2,3.5,13,2.4,1.9,2.9))):
        add("Avalanche", "EmbeddedBoulder%02d" % i, _rock((x,y,z),(sx,sy,sz),i),
            "LayeredStone", True, (.34+(i%3)*.023,.365+(i%3)*.021,.335+(i%3)*.02,1),
            terrain_fit="rigid", support=(x,0,z))
    # Broken forest margins continue uphill; these are shortened remains, not
    # healthy trees planted in the avalanche track.
    for i,(x,z,h) in enumerate(((-13.1,8.7,2.8),(14.7,10.4,3.4),
                               (-16.6,16.8,2.4),(16.2,18.2,2.7),(-11.9,22,1.6))):
        base = _height(x,z)-.35
        timber = [_tube((x,base,z),(x-.18,base+h,z-.65),.34,.18,9)]
        for n in range(3):
            timber.append(_tube((x-.14,base+h*.76,z-.43),
                (x-.34+n*.17,base+h+.22*(n%2),z-.63-.11*n),.10,.018,5))
        add("Avalanche", "SnappedMarginTrunk%02d" % i, _merge(timber), "AbandonedWood",
            True, (.29,.28,.235,1), terrain_fit="rigid", support=(x,0,z))
    # An old upper tow pylon, driven downhill and kinked above its buried base.
    # The broken wheel remains recognizable beside the lower intact tow.
    mast = [_tube((7.0,2.6,4.0),(6.9,5.4,3.8),.19,.18,8),
            _tube((6.9,5.4,3.8),(4.4,5.7,1.85),.18,.14,8),
            _tube((3.25,5.55,2.9),(5.70,5.85,.9),.16,.16,8),
            _tube((6.85,4.95,3.75),(3.48,5.52,2.68),.058,.058,6)]
    # Rim, spokes and hub form an empty rope wheel instead of a solid disk.
    center = (4.48,5.78,1.89)
    rim = [(center[0]+math.cos(i*math.tau/16)*1.17,
            center[1]+math.sin(i*math.tau/16)*.40,
            center[2]+math.sin(i*math.tau/16)*1.10) for i in range(16)]
    mast += [_tube(a,b,.055,.055,6) for a,b in zip(rim,rim[1:]+rim[:1])]
    mast += [_tube(center,rim[i],.048,.048,6) for i in range(0,16,4)]
    add("Avalanche", "BuckledUpperTowAndWheel", _merge(mast), "RustedIron", True,
        (.29,.26,.215,1), terrain_fit="rigid", support=(7,0,4))
    cable = [(5.2,5.83,2.3),(5.7,5.55,2.8),(6.15,4.9,3.4),(6.0,4.4,4.9),
             (5.1,4.4,6.4),(3.6,4.2,7.1),(2.4,4.15,7.25),(2.0,4.1,6.85)]
    add("Avalanche", "SeveredTowCable", _merge(_tube(a,b,.027,.027,5)
        for a,b in zip(cable,cable[1:])), "RustedIron", False, (.19,.20,.185,1),
        terrain_fit="rigid", support=(7,0,4))
    beams = [bp.u_rotated(bp.u_box((0,0,0),(.24,.29,6.2),.018),(7,-42,-4)),
             bp.u_rotated(bp.u_box((0,0,0),(.17,.22,4.4),.015),(-5,58,6))]
    add("Avalanche", "BuriedRoofTimbers", _merge(kit.translated(g,p) for g,p in
        zip(beams,((1.3,4.8,4.9),(-5.3,4.9,5.1)))), "AbandonedWood", True,
        (.325,.305,.245,1), terrain_fit="rigid", support=(0,0,5))


def build_ruin_variant(add):
    """Only homestead-15: a hollow, supported ruin, impacted from local +X.

    This is deliberately assembled from vertical wall fragments. A deformed
    donor's continuous sloping profiles read as a solid wedge from its back;
    the roof now has visible bearings at both its low edge and surviving ridge.
    """
    kind = "AvalancheRuinedHouse"
    box = bp.u_box
    move = kit.translated
    turn = bp.u_rotated
    add(kind,"ExposedFloor",box((0,.09,0),(8,.18,7),.035),"LayeredStone")

    def window_wall(width, angle, offset):
        opening = 1.20
        flank = (width-opening)*.5
        pieces = [box((0,.68,0),(width,1.0,.36),.03),
                  box((0,2.85,0),(width,.48,.36),.035)]
        for sign in (-1,1):
            pieces.append(box((sign*(opening+flank)*.5,1.90,0),
                              (flank,1.44,.36),.03))
        # A genuinely empty opening with thick reveals, not a dark card.
        pieces += [box((x,3.17+(i%2)*.035,0),(.57,.19+(i%2)*.07,.37),.025)
                   for i,x in enumerate((-width*.34,-width*.08,width*.22))]
        return move(turn(_merge(pieces),(0,angle,0)),offset)

    add(kind,"RearWallWithOpenWindow",window_wall(3.48,0,(-2.10,0,-3.31)),"LayeredStone")
    add(kind,"WestWallWithOpenWindow",window_wall(3.18,90,(-3.82,0,-1.85)),"LayeredStone")
    # Other sides retain low, separately broken pieces. The mountain side has
    # a broad real gap; none of these walls crosses the empty centre of the house.
    fragments=[]
    for x,y,z,sx,sy,sz in ((1.13,.48,-3.30,2.90,.60,.36),
            (3.81,.64,-2.67,.36,.92,1.30),(3.80,.42,2.64,.36,.48,1.32),
            (2.50,.44,3.31,2.62,.52,.36),(-2.95,.67,3.31,1.76,.98,.36),
            (-3.82,.64,1.13,.36,.92,2.26),(-3.82,.44,2.78,.36,.52,.68)):
        fragments.append(box((x,y,z),(sx,sy,sz),.035))
    add(kind,"SeparateLowWallFragments",_merge(fragments),"LayeredStone")
    tops=[]
    for i,(x,y,z,sx,sy,sz) in enumerate(((-3.82,1.25,.28,.37,.31,.62),
            (-3.82,1.17,1.05,.38,.17,.47),(-3.15,1.31,3.31,.58,.30,.39),
            (-2.49,1.24,3.31,.54,.18,.37),(1.26,.91,-3.31,.72,.26,.38),
            (2.12,.83,-3.31,.57,.10,.36),(3.81,1.23,-2.85,.38,.26,.48),
            (3.82,1.14,-2.22,.40,.13,.39),(2.98,.83,3.31,.68,.26,.39),
            (1.77,.76,3.30,.59,.11,.39))):
        tops.append(move(turn(box((0,0,0),(sx,sy,sz),.028),(0,(i%3-1)*4,0)),(x,y,z)))
    add(kind,"BrokenMasonryTops",_merge(tops),"Masonry",True,(.47,.465,.425,1))
    # Shallow masonry joints and relieved corner stones prevent the remnants
    # from becoming one unbroken grey plane at the ground-level rear view.
    courses=[]
    for y in (.46,.88,1.30,1.72,2.16,2.59):
        for x in (-3.29,-.86):
            courses.append(box((x,y,-3.50),(.88,.027,.016),0))
        for z in (-2.98,-.68):
            courses.append(box((-4.01,y,z),(.016,.027,.67),0))
    add(kind,"OldMasonryJoints",_merge(courses),"LayeredStone",False,(.255,.275,.255,1))

    frames=[]
    # Empty decayed window frames, with only one broken half of each crossbar.
    for angle,offset in ((0,(-2.10,0,-3.505)),(90,(-4.015,0,-1.85))):
        frame=[box((x,1.89,0),(.08,1.49,.105),.008) for x in (-.635,.635)]
        frame += [box((0,y,0),(1.35,.08,.14),.008) for y in (1.16,2.64)]
        frame += [box((-.22,1.88,0),(.42,.065,.07),.005)]
        frames.append(move(turn(_merge(frame),(0,angle,0)),offset))
    add(kind,"EmptyWeatheredWindowFrames",_merge(frames),"AbandonedWood")

    timbers=[]
    # Low eave sits on masonry. Ridge bears on two full surviving posts; all
    # end grain visibly contacts a bearing rather than hanging over the void.
    timbers.append(box((-2.22,3.38,-3.31),(3.66,.22,.23),.018))
    for x in (-3.78,-.74):
        timbers.append(_beam((x,.18,-.12),(x,4.43,-.12),.22,.22))
        timbers.append(_beam((x,3.32,-3.38),(x,4.43,-.12),.18,.22))
        timbers.append(_beam((x,3.38,-3.33),(x,3.38,-.12),.17,.18))
        timbers.append(_beam((x,3.19,-.12),(x,3.94,-1.19),.12,.14))
    timbers.append(box((-2.26,4.42,-.12),(3.48,.22,.22),.018))
    # A short wall plate and the old front jamb still describe the vanished
    # opposite roof without pretending that a roof floats over that opening.
    timbers += [box((-3.82,1.33,1.09),(.21,.18,2.48),.018),
                _beam((-2.01,.18,3.29),(-2.03,2.31,3.25),.17,.18)]
    add(kind,"SupportedRoofFrameAndPosts",_merge(timbers),"AbandonedWood",True,(.29,.275,.23,1))

    roof=[];snow=[]
    slope=.329
    angle=-math.degrees(math.atan(slope))
    # Separate boards terminate irregularly on the broken +Z edge. Their
    # broad surviving quarter remains attached to the frame on every side.
    for i in range(11):
        x=-4.00+i*.335
        start=-3.69+(i%3)*.025
        end=.07-(i%4)*.125
        middle=(start+end)*.5
        y=4.52+slope*(middle+.12)
        length=math.hypot(end-start,slope*(end-start))
        board=move(turn(box((0,0,0),(.321,.115,length),.012),(angle,0,0)),(x,y,middle))
        roof.append(board)
        if i in (0,1,4,5,6,9):
            sz=-2.70+(i%3)*.44
            sy=4.60+slope*(sz+.12)
            snow.append(move(turn(box((0,0,0),(.31,.065,1.02+(i%2)*.35),.012),
                                  (angle,0,0)),(x,sy,sz)))
    add(kind,"AttachedBrokenRoofQuarter",_merge(roof),"AbandonedRoof")
    add(kind,"SnowOnSupportedRoof",_merge(snow),"WindSnow",False)

    # Hearth is another grounded vertical trace of a room; no broad chimney
    # mass closes the house's view or the open route across its centre.
    hearth=[box((-.45,1.11,-2.78),(1.04,1.86,.82),.04),
            box((-.45,2.23,-2.87),(.69,.41,.61),.03)]
    add(kind,"BrokenHearth",_merge(hearth),"Masonry")
    add(kind,"OldHearthMouth",box((-.45,.61,-2.357),(.58,.62,.025),.006),"DarkWindow",False)

    add(kind,"ImpactTreeThroughNorthWall",_merge((
        _tube((3.95,.86,-.3),(-3.6,.43,.5),.30,.20,9),
        _tube((1.8,.71,-.1),(.35,1.1,-1.6),.11,.025,6),
        _tube((-.3,.60,.2),(-1.5,.95,1.65),.085,.018,6))),"AbandonedWood")
    debris=[]
    for i,(x,z,sx,sy,sz) in enumerate(((2.65,-1.75,1.1,.53,.85),
            (1.8,-2.23,.70,.35,.67),(2.88,-2.53,.63,.38,.54),
            (3.35,1.72,.8,.46,.73),(2.74,2.12,.62,.31,.50),
            (-2.78,2.55,.59,.35,.43),(-3.24,1.67,.76,.42,.62),
            (.78,-2.93,.58,.28,.54))):
        debris.append(_rock((x,.19+sy*.5,z),(sx,sy,sz),i))
    add(kind,"SettledImpactRubble",_merge(debris),"LayeredStone")
    fallen=[]
    for a,b,w in (((2.91,.31,-2.54),(.83,.29,-1.54),.19),
                  ((3.02,.30,1.91),(1.64,.26,2.67),.15),
                  ((-2.9,.30,2.01),(-1.21,.25,2.88),.17)):
        fallen.append(_beam(a,b,w,w))
    add(kind,"SettledRoofBeams",_merge(fallen),"AbandonedWood")
    add(kind,"SnowInside",_merge((box((1.4,.205,1.57),(1.52,.05,.94),.015),
        box((2.85,.22,-1.03),(1.35,.06,.78),.015),
        box((-2.81,.22,1.71),(.95,.06,.73),.015))),"WindSnow",False)


def validate_avalanche(parts):
    avalanche = [p for p in parts if p["kind"] == "Avalanche"]
    assert avalanche and all(p.get("terrain_fit") in ("surface","rigid") for p in avalanche)
    assert all(len(p.get("support",())) == 3 for p in avalanche if p["terrain_fit"] == "rigid")
    bed = next(p for p in avalanche if p["name"] == "CompactedSnowAndDebris")
    lo,hi = kit.bounds(bed["geometry"])
    assert lo == (-19,-1,-1) and hi[0] == 19 and hi[2] == 25, (lo,hi)
    assert 4.8 < hi[1] < 6, "Avalanche lost its tall compacted toe"
    assert all((x,-1,z) in bed["geometry"][0] for x,z in FOOTPRINT), "Visible bed drifted from movement polygon"
    assert len([p for p in avalanche if p["name"].startswith("FallenConifer")]) == 5
    assert any(p["kind"] == "AvalancheRuinedHouse" for p in parts)
