#!/usr/bin/env python3
"""Five private, earth-covered vegetable stores. Local +Z faces the common court."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import re
import runpy
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
import bar_parts as parts
import interior_kit as kit

# The garage atlas already carries worn wood, paint, concrete and corrugated iron.
garage = runpy.run_path(str(TOOLS / 'build-city-ridge-garages-3d-model.py'))
Assembly = garage['Assembly']
NAME = 'RidgeCellars'
BAY = 3.52
DOORS = (-7.04, -3.52, 0, 3.52, 7.04)
TABLE = (7.10, 4.50, 9.35, 5.42)
CART = (8.18, 5.95, 9.36, 7.75)
POTATO_GRIP = (7.88, 1.00, 5.18)
LIGHTS = {
    'Work': ((7.84,1.93,.46), (8.22,.86,4.99)),
    'Entry': ((-2.45,1.93,.54), (-1.25,.13,6.95)),
    'Wall': ((-9.05,1.90,1.60), (1.95,.90,.12)),
}
LANE_LIGHT = {'Lane': ((.64,2.35,.11), (2.14,.10,4.11))}
MODEL = ROOT / 'Assets/Resources/City' / NAME
SOURCE = ROOT / 'ArtSource/City' / NAME
SOIL_ORIGIN_XZ = (170., -368.)
GROUND_OWNER = ROOT / 'Assets/Scripts/Runtime/World/CityExteriorAppearance.cs'
SOIL_PITCH = float(re.search(r'\bGroundTextureTileSize\s*=\s*([\d.]+)f?\s*;', GROUND_OWNER.read_text())[1])


def face_z(x):
    return max(0, (-1.76 - x) * .0824)


def against_slope(shape):
    vertices, faces = shape
    return [(x, y, z + face_z(x)) for x, y, z in vertices], faces


def add_bay(model, number, center):
    bay = Assembly()
    label = 'Cellar' + str(number + 1)
    door = center + (-.13, .07, -.15, .06, -.04)[number]
    left, right = center - BAY / 2, center + BAY / 2
    jamb_left, jamb_right = door - .54, door + .54
    # A recessed door, two reveals and a separate lintel cap the real opening.
    for start, end in ((left, jamb_left), (jamb_right, right)):
        bay.box(label, 'felt', ((start + end) / 2, 1.025, -.32),
                (end-start, 2.27, .52), .025)
        for row in range(4):
            bottom = row * .51 - .035
            splits = [start, end]
            if row % 2 == number % 2:
                splits.insert(1, start + (end - start) * (.39 + .07 * ((row+number) % 3)))
            for i, (a, b) in enumerate(zip(splits, splits[1:])):
                finish = 'concrete' if (number+row+i) % 5 else 'slab'
                bay.box(label, finish, ((a+b)/2, bottom+.253, -.065),
                        (b-a-.011, .50, .16), .018)
        bay.add('COL_Front', 'concrete', kit.box(((start+end)/2, 1.035, -.26),
                                                 (end-start, 2.35, .55)))
    bay.box(label, 'concrete', (door, 2.025, -.24), (1.081, .27, .56), .023)
    bay.box(label, 'case', (door, .94, -.345), (1.08, 1.94, .10), .013)
    bay.add('COL_Front', 'iron', kit.box((door, 1.03, -.29), (1.08, 2.30, .11)))
    bay.box(label, 'slab', (center, 2.19, -.23), (BAY-.012, .15, .74), .033)
    # Steel frame and worn threshold turn back into the reveals.
    for x in (jamb_left + .033, jamb_right - .033):
        bay.box(label, 'iron', (x, .962, -.13), (.061, 1.906, .22), .008)
    bay.box(label, 'iron', (door, 1.872, -.155), (1.00, .083, .18), .006)
    sill = kit.joint_box((door, -.028, .16), (1.28, .118, .99), .024)
    bay.add(label, 'slab', sill)
    bay.add('COL_Sills', 'slab', kit.box((door,-.028,.16),(1.28,.118,.99)))

    # Repairs are different even without colour. Individual boards retain their bevels.
    for plank in range(6):
        x = door + (plank-2.5)*.156
        finish = ('sage', 'dado', 'timber', 'iron', 'timber')[number]
        bay.box(label, finish, (x, .947, -.227), (.154, 1.792, .114), .006)
        if number == 2:
            foot = .37 + .069 * ((plank+1) % 3)
            bay.box(label, 'repair', (x, .05+foot/2, -.161), (.151, foot, .027), .007)
        if number in (0,1) and plank in (0,3,4):
            # Paint has worn through on the bottom arris of a few planks.
            foot = .061 + (plank+number)%4 * .014
            bay.box(label, 'timber', (x, .053+foot/2, -.169), (.147, foot, .009), .003)
    if number == 3:
        bay.box(label, 'zinc', (door,.948,-.157), (.93,1.79,.027), .009)
        for offset in (-.405,-.234,-.063,.108,.279,.419):
            bay.box(label, 'case', (door+offset,.955,-.137), (.018,1.746,.025), .005)
    swing = -1 if number % 2 == 0 else 1
    for y in (.41, 1.47):
        pivot = door + swing*.43
        bay.box(label, 'iron', (pivot-swing*.11,y,-.122), (.31,.06,.027), .008)
        bay.rod(label, 'oxide', (pivot,y-.061,-.096),(pivot,y+.061,-.096), .027, 8)
        for distance in (.10,.21):
            x = pivot - swing*distance
            bay.rod(label, 'zinc', (x,y,-.110),(x,y,-.094), .014, 6)
    lock_x = door - swing*.35
    apply_finish = 'zinc' if number == 4 else 'iron'
    bay.box(label, apply_finish, (lock_x,.95,-.111), (.205,.073,.042), .007)
    bay.path(label, 'zinc', [(lock_x-.029,.914,-.07),(lock_x-.025,.962,-.07),
                              (lock_x+.025,.962,-.07),(lock_x+.029,.914,-.07)], .009)
    bay.box(label, 'brass' if number == 1 else 'iron', (lock_x,.884,-.072), (.081,.067,.047), .010)
    for group, solids in bay.meshes.items():
        for finish, shape in solids:
            model.add(group, finish, against_slope(shape))


def embankment(model):
    # Closed sectional mound. The shoulders taper into the native earth at each end.
    xs = (-9.75,-9.36,-8.80,-7.1,-5.4,-3.5,-1.76,.1,1.85,3.5,5.34,7.04,8.8,9.36,9.75)
    rings = []
    for x in xs:
        taper = min(1, (9.75-abs(x))/.95)
        variance = math.cos(x*1.49) * .065 + math.sin(x*1.13)*.052
        front = face_z(x) - .365
        rings.append([(x,-.22,front),(x,taper*2.19-.026,front),
                      (x,taper*(2.51+variance),front-.89),
                      (x,taper*(2.72+variance),-2.18+variance),
                      (x,taper*(2.22+variance),-3.62+variance),
                      (x,taper*.80,-4.93+variance*.33),
                      (x,-.08,-5.65),(x,-.22,-5.65)])
    count = len(rings[0])
    points = [point for ring in rings for point in ring]
    faces = [tuple(reversed(range(count))), tuple(range(len(points)-count,len(points)))]
    for i in range(len(rings)-1):
        for j in range(count):
            a = i*count+j
            b = i*count+(j+1)%count
            faces.extend(((a,b,b+count),(a,b+count,a+count)))
    if parts.signed_volume((points,faces)) < 0:
        faces = [tuple(reversed(face)) for face in faces]
    model.add('EarthBank','felt',(points,faces))
    model.add('COL_Bank','felt',(points,faces))
    # Broken strips of turf accent the edge between wall coping and soil.
    rng = random.Random(68319)
    for index in range(48):
        x = rng.uniform(-8.7,8.7)
        z = rng.uniform(-1.82,-1.28)+face_z(x)
        tufty = 2.52+.065*math.cos(x*1.49)+.052*math.sin(x*1.13)
        for dx in (-.027,.032):
            h = rng.uniform(.075,.18)
            model.rod('Tufts','sage' if index%3 else 'canvas', (x+dx,tufty-.035,z),
                      (x+dx*1.9,tufty+h,z-.025),.021,3,.002)


def vents(model):
    for index, (x,z,length) in enumerate(((-6.58,-1.88,.86),(-.68,-2.06,.73),(5.87,-1.75,.94))):
        top = 2.52 + length
        finish = 'case' if index == 1 else 'iron'
        model.rod('VentStacks',finish,(x,2.24,z),(x,top,z),.125,12)
        for y in (2.75, top-.11):
            model.rod('VentStacks','zinc',(x,y-.023,z),(x,y+.023,z),.143,12)
        # A little air gap, bridge struts and folded pyramid cover over each stack.
        for dx in (-.092,.092):
            model.box('VentStacks','iron',(x+dx,top+.061,z),(.026,.143,.027),.004)
        model.add('VentStacks','zinc',parts.u_tapered_cylinder((x,top+.155,z),(.447,.054,.447),.20,8))
        model.rod('VentStacks','iron',(x,top-.005,z),(x,top+.002,z),.11,12)


def place_optics(model,optical,source,aim):
    rotation = Vector((0,0,1)).rotation_difference((Vector(aim)-Vector(source)).normalized())
    for group, solids in optical.meshes.items():
        for finish,(vertices,faces) in solids:
            model.add(group,finish,([tuple(Vector(source)+rotation@Vector(p)) for p in vertices],faces))


def wall_light(model):
    source, aim = LIGHTS['Wall']
    # The end of the retaining wall stops at -8.80. Bolt the arm through the
    # actual last masonry block and carry the fixture beyond the end of the row.
    x = -8.58
    z = face_z(x)+.029
    model.box('Conduit','iron',(x,1.968,z),(.225,.282,.074),.015)
    for dx in (-.078,.078):
        for dy in (-.097,.097):
            model.rod('Conduit','zinc',(x+dx,1.968+dy,z+.035),(x+dx,1.968+dy,z+.050),.015,6)
    model.path('Conduit','sage',[(x,2.04,z+.042),(-8.72,2.083,.83),
               (-9.17,2.083,1.616),(-9.17,2.00,1.616)],.029,8)
    model.path('Conduit','iron',[(x,1.88,z+.036),(-9.02,2.078,1.35)],.019,8)
    optical = Assembly()
    optical.add('Shades','oxide',kit.lathe([(.081,-.201),(.141,-.162),(.149,-.110),(.139,-.090)],14))
    optical.add('Shades','zinc',kit.lathe([(.141,-.113),(.148,-.109),(.145,-.092)],14))
    optical.add('LensWall','glass',kit.lathe([(.124,-.095),(.127,-.080),(.102,-.054),(.055,-.035)],14))
    optical.box('Shades','sage',(0,.128,-.107),(.303,.027,.192),.013)
    # Two thin cage wires protect the glass while leaving its optical axis clear.
    for dx in (-.081,.081):
        optical.path('Shades','iron',[(dx,-.104,-.102),(dx,-.074,-.070),
                     (dx,0,-.047),(dx,.078,-.071),(dx,.105,-.096)],.0055,6)
    place_optics(model,optical,source,aim)


def lane_lamp():
    model = Assembly()
    source, aim = LANE_LIGHT['Lane']
    model.box('LaneFittings','concrete',(0,.082,0),(.30,.164,.30),.020)
    model.box('LaneFittings','oxide',(0,.178,0),(.181,.094,.181),.009)
    post = kit.joint_box((0,1.373,0),(.152,2.454,.145),.009)
    vertices,faces = post
    model.add('LaneFittings','timber',([(x*(1-.16*y/2.6),y,z*(1-.13*y/2.6))
                                        for x,y,z in vertices],faces))
    for y in (.235,2.218,2.522):
        model.box('LaneFittings','oxide',(0,y,0),(.160,.045,.155),.007)
        model.rod('LaneFittings','zinc',(-.095,y,.004),(.092,y,.004),.013,8)
    model.path('LaneFittings','sage',[(.051,2.502,.006),(.507,2.538,.082),
                                      (.599,2.49,.076),(.601,2.428,.033)],.029,8)
    model.path('LaneFittings','iron',[(.062,2.219,.010),(.439,2.529,.069)],.017,8)
    model.path('LaneFittings','rubber',[(-.033,.281,.078),(-.038,2.370,.066),
               (.019,2.488,.069),(.22,2.480,.075),(.498,2.508,.073),(.603,2.432,.037)],.009,7)
    for y in (.54,1.24,1.88):
        model.box('LaneFittings','zinc',(-.035,y,.079),(.044,.012,.014),.003)
    optical = Assembly()
    optical.add('LaneFittings','iron',kit.scaled(kit.lathe(
        [(.076,-.218),(.162,-.158),(.170,-.110),(.153,-.090)],14),(1,.77,1)))
    optical.add('LensLane','glass',kit.scaled(kit.lathe(
        [(.142,-.099),(.150,-.079),(.121,-.054),(.068,-.035)],14),(1,.77,1)))
    optical.box('LaneFittings','sage',(0,.120,-.112),(.365,.028,.255),.011)
    for side in (-1,1):
        optical.box('LaneFittings','sage',(side*.169,.074,-.117),(.024,.086,.207),.006)
    place_optics(model,optical,source,aim)

    # One watertight measured collider for the shoe and shaft. The bracket
    # and shade are above head height and outside this ground occupation.
    rows = [(0,.15,.15),(.164,.15,.15),(.165,.091,.091),(.232,.091,.091),
            (.233,.076,.0725),(2.60,.064,.064)]
    points = [(x,y,z) for y,wx,wz in rows for x,z in ((-wx,-wz),(wx,-wz),(wx,wz),(-wx,wz))]
    faces = [(0,1,2,3),(23,22,21,20)]
    for row in range(len(rows)-1):
        for col in range(4):
            a = row*4+col
            b = row*4+(col+1)%4
            faces.append((b,a,a+4,b+4))
    model.add('COL_LanePole','iron',(points,faces))
    return model


def light_fittings(model):
    for name in ('Work','Entry'):
        source,aim = LIGHTS[name]
        x,y,z = source
        face = face_z(x)
        model.box('Conduit','iron',(x,y+.017,face+.055),(.23,.28,.10),.022)
        for dx in (-.077,.077):
            for dy in (-.081,.081):
                model.rod('Conduit','zinc',(x+dx,y+dy,face+.100),(x+dx,y+dy,face+.119),.014,6)
        model.path('Conduit','case',[(x,y+.087,face+.095),(x,y+.087,z-.19),
                                     (x,y+.017,z-.14)],.032,10)
        # Lamp parts face along local +Z. The light starts beyond the cage, with
        # an actual overhanging enamel brow and opal diffuser behind its origin.
        optical = Assembly()
        if name == 'Work':
            def oval(group,finish,profile):
                optical.add(group,finish,kit.scaled(kit.lathe(profile,18),(1,.67,1)))
            oval('Shades','sage',[(.155,-.229),(.203,-.178),(.205,-.111),(.185,-.094)])
            oval('Shades','iron',[(.193,-.117),(.198,-.106),(.195,-.093)])
            oval('LensWork','glass',[(.176,-.103),(.179,-.084),(.158,-.058),(.114,-.043),(.065,-.040)])
            for dx in (-.091,0,.091):
                optical.path('Shades','iron',[(dx,-.110,-.098),(dx,-.082,-.050),
                                              (dx,-.032,-.025),(dx,.054,-.025),
                                              (dx,.093,-.061),(dx,.110,-.100)],.0065,6)
            optical.box('Shades','sage',(0,.120,-.096),(.405,.029,.183),.012)
        else:
            optical.box('Shades','iron',(0,.006,-.151),(.467,.218,.128),.033)
            optical.box('Shades','case',(0,-.001,-.099),(.455,.198,.061),.020)
            optical.box('LensEntry','glass',(0,-.003,-.062),(.385,.141,.050),.021)
            optical.box('Shades','dado',(0,.121,-.091),(.523,.033,.254),.014)
            for side in (-1,1):
                optical.box('Shades','sage',(side*.243,.069,-.098),(.024,.092,.223),.010)
                optical.box('Shades','iron',(side*.206,-.002,-.060),(.013,.12,.039),.005)
        place_optics(model,optical,source,aim)

    # A junction and a continuous armoured supply. The clamps stand off the face
    # enough for the cable to run through, including the bend between two bays.
    xs = (-8.58,-5.54,-2.45,-1.76,-.15,1.62,3.16,5.31,7.84)
    model.path('Conduit','iron',[(x,2.062,face_z(x)+.048) for x in xs],.013,7)
    for x in (-8.58,-2.45,7.84):
        model.rod('Conduit','iron',(x,1.95,face_z(x)+.048),(x,2.07,face_z(x)+.048),.012,7)
    for i in range(-8,13):
        x = -2.08+i*.79
        z = face_z(x)+.027
        model.path('Conduit','zinc',[(x,2.038,z),(x,2.044,z+.031),
                                      (x,2.08,z+.031),(x,2.087,z)],.005,6)
    model.box('Conduit','oxide',(5.20,2.062,.052),(.134,.104,.077),.012)
    wall_light(model)


def wall_repair(model):
    # An old square of missing render was pointed back with short brick courses.
    repair = Assembly()
    x = -4.70
    repair.box('MasonryRepair','slab',(x,.882,.016),(.76,.47,.026),.014)
    for row in range(4):
        cuts = [-.349,-.115,.111,.349] if row%2 else [-.349,-.222,.008,.237,.349]
        for i,(a,b) in enumerate(zip(cuts,cuts[1:])):
            repair.box('MasonryRepair','oxide' if (row+i)%3 else 'enamel',
                       (x+(a+b)/2,.721+row*.102,.036),(b-a-.012,.090,.027),.007)
    # Folded drip flashing over one coping joint, two mineral trails beneath it.
    repair.box('MasonryRepair','zinc',(x+.28,2.268,-.035),(.235,.027,.37),.006)
    repair.box('MasonryRepair','case',(x+.28,2.224,.145),(.235,.093,.020),.005)
    for dx,length in ((.205,.346),(.338,.201)):
        outline = [(-.025,0),(-.019,-.68*length),(-.005,-length),
                   (.007,-.91*length),(.011,-.29*length),(.018,0)]
        streak = garage['plate'](outline,.005)
        repair.add('MasonryRepair','dado',kit.translated(streak,(x+dx,1.978,.020)))
    for group, solids in repair.meshes.items():
        for finish,shape in solids:
            model.add(group,finish,against_slope(shape))


def potato(model, group, position, size, angle, finish):
    blank = kit.lathe([(.038,-.050),(.063,-.013),(.058,.032),(.025,.051)],7)
    shape = parts.u_rotated(kit.scaled(blank,(1.04*size,.83*size,size)),(83,angle,10))
    model.add(group,finish,kit.translated(shape,position))


def crate(model, position):
    x,y,z = position
    for i in range(4):
        model.box('SortingCorner','timber',(x+(i-1.5)*.155,y+.032,z),(.150,.044,.412),.006)
    for dx in (-.289,.289):
        for dz in (-.193,.193):
            model.box('SortingCorner','repair',(x+dx,y+.117,z+dz),(.045,.231,.048),.005)
    for tier in (.105,.198):
        for dz in (-.211,.211):
            model.box('SortingCorner','repair' if tier < .15 else 'timber',
                      (x,y+tier,z+dz),(.64,.058,.025),.005)
        for dx in (-.312,.312):
            model.box('SortingCorner','timber',(x+dx,y+tier,z),(.025,.058,.425),.005)


def fill_crate(model, position, small):
    x,y,z = position
    rng = random.Random(11461 + int(small))
    # Two layers of smaller seed potatoes. The large potatoes fill their shallow
    # crate in one layer. Leave the two facing corners for the worker's hand.
    cols, rows, size = (5,3,.82) if small else (3,2,1.46)
    for ix in range(cols):
        for iz in range(rows):
            dx = (ix-(cols-1)/2)*(.109 if small else .176)
            dz = (iz-(rows-1)/2)*(.111 if small else .154)
            seed = ix*37+iz*11
            center = (x+dx+rng.uniform(-.005,.005), y+(.099 if small else .124), z+dz)
            potato(model,'SortingCorner',center,size,seed,'card' if seed%3 else 'endgrain')
            if small and iz<rows-1 and ix<cols-1:
                potato(model,'SortingCorner',(center[0]+.050,center[1]+.064,center[2]+.050),
                       .86,seed+59,'card')
    if small:
        potato(model,'SortingCorner',(7.868,.940,5.135),.92,61,'endgrain')
        potato(model,'SortingPotato',POTATO_GRIP,.91,77,'card')


def sorting_scale(model, x, z):
    # Low mechanical scale, a weathered sage body, a round dial and a dished pan.
    base = .821
    model.box('SortingCorner','sage',(x,base+.074,z),(.295,.141,.271),.016)
    model.box('SortingCorner','sage',(x,base+.138,z+.035),(.21,.155,.153),.019)
    y = base+.148
    model.rod('SortingCorner','zinc',(x,y,z+.092),(x,y,z+.122),.089,16)
    model.rod('SortingCorner','canvas',(x,y,z+.122),(x,y,z+.125),.079,16)
    for i in range(7):
        a = math.radians(-54+18*i)
        model.rod('SortingCorner','iron',(x+.057*math.sin(a),y+.057*math.cos(a),z+.127),
                  (x+.070*math.sin(a),y+.070*math.cos(a),z+.127),.0025,4)
    model.rod('SortingCorner','oxide',(x,y,z+.130),(x-.030,y+.049,z+.130),.003,5)
    model.rod('SortingCorner','iron',(x,base+.208,z),(x,base+.246,z),.037,8)
    # Open pan formed from a silvered sheet with a rim turned over the edge.
    pan = kit.lathe([(.057,.000),(.130,.018),(.147,.038),(.145,.043),
                     (.137,.040),(.122,.024),(.055,.009)],16)
    pan = parts.u_rotated(pan,(-90,0,0))
    model.add('SortingCorner','zinc',kit.translated(pan,(x,base+.237,z)))


def work_corner(model):
    x1,z1,x2,z2 = TABLE
    cx,cz = (x1+x2)/2,(z1+z2)/2
    for i in range(5):
        model.box('SortingCorner','worktop' if i%3 else 'timber',
                  (cx,.783,z1+(i+.5)*(z2-z1)/5),(x2-x1,.075,(z2-z1)/5-.008),.012)
    for dx in (-.92,.92):
        for dz in (-.33,.33):
            model.box('SortingCorner','timber',(cx+dx,.38,cz+dz),(.091,.76,.091),.009)
        model.box('SortingCorner','repair',(cx+dx,.667,cz),(.070,.14,.76),.006)
    model.box('SortingCorner','timber',(cx,.259,cz-.28),(1.92,.088,.057),.006)
    for position, small in [((cx-.59,.821,cz+.035),True),((cx+.43,.821,cz-.012),False)]:
        crate(model,position)
        fill_crate(model,position,small)
    # Splayed boxes with open slat sides are stored between the table legs.
    crate(model,(cx-.48,.006,cz+.072))
    crate(model,(cx-.46,.240,cz+.086))
    sorting_scale(model,9.157,4.988)
    model.add('COL_Table','timber',kit.box((cx,.409,cz),(x2-x1,.819,z2-z1)))

    # A two-wheeled produce cart with a slatted floor, spoked wheels and a rest.
    cx,cz = (CART[0]+CART[2])/2, 6.48
    for i in range(5):
        model.box('Handcart','timber',(cx+(i-2)*.144,.51,cz),(.140,.064,.945),.007)
    for dx in (-.355,.355):
        for dz in (-.449,.449):
            model.box('Handcart','iron',(cx+dx,.66,cz+dz),(.042,.355,.040),.006)
        for y in (.565,.708):
            model.box('Handcart','repair',(cx+dx,y,cz),(.046,.103,.958),.007)
    for dz in (-.446,.446):
        for y in (.565,.708):
            model.box('Handcart','timber',(cx,y,cz+dz),(.713,.103,.035),.006)
    for dx in (-.32,.32):
        model.rod('Handcart','sage',(cx+dx,.45,cz-.45),(cx+dx,.73,7.63),.034,8)
        model.rod('Handcart','timber',(cx+dx,.701,7.49),(cx+dx,.752,7.716),.041,8)
        model.rod('Handcart','iron',(cx+dx,.02,cz+.405),(cx+dx,.46,cz+.297),.025,8)
    model.rod('Handcart','iron',(cx-.54,.342,cz-.075),(cx+.54,.342,cz-.075),.034,10)
    for side in (-1,1):
        hub = (cx+side*.49,.342,cz-.075)
        model.add('Handcart','rubber',garage['rim'](hub,[(-.046,.298),(-.045,.325),(-.029,.342),
                     (.029,.342),(.045,.325),(.046,.298)],22))
        model.add('Handcart','oxide',garage['rim'](hub,[(-.031,.272),(-.03,.302),(.03,.302),(.031,.272)],22))
        model.rod('Handcart','zinc',(hub[0]-.061,hub[1],hub[2]),(hub[0]+.061,hub[1],hub[2]),.059,10)
        for spoke in range(8):
            angle = math.tau * spoke / 8
            model.rod('Handcart','iron',hub,(hub[0],hub[1]+.277*math.cos(angle),hub[2]+.277*math.sin(angle)),.012,5)
    model.add('COL_Cart','timber',kit.box((cx,.40,cz), (1.08,.80,1.01)))
    model.add('COL_Cart','iron',kit.box((cx,.58,7.305),(.69,.29,.88)))


def create_model():
    model = Assembly()
    for number, center in enumerate(DOORS):
        add_bay(model,number,center)
    embankment(model)
    vents(model)
    light_fittings(model)
    wall_repair(model)
    work_corner(model)
    return model


def paving():
    drain = Assembly()
    # The inner half is a dished open channel. Its two slopes meet at the low line.
    profile = [(-.485,-.07),(.13,-.07),(.13,.358),(.114,.397),
               (.091,.401),(-.091,.401),(-.118,.388),(-.13,.363),
               (-.13,.042),(-.198,.033),(-.337,.005),(-.450,.041),(-.485,.04)]
    def section(outline):
        n = len(outline)
        vertices = [(x,y,z) for z in (0,1) for x,y in outline]
        faces = [tuple(reversed(range(n))),tuple(range(n,n*2))]
        faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        if parts.signed_volume((vertices,faces)) < 0:
            faces = [tuple(reversed(face)) for face in faces]
        return vertices,faces
    drain.add('DrainSegment','concrete',section(profile))
    tread = Assembly()
    tread.box('StairSlab','slab',(0,-.07,.24),(2.2,.14,.48),.013)
    grate = Assembly()
    for x in (-.471,-.143):
        grate.box('Grille','iron',(x,.046,.325),(.018,.026,.65),.004)
    for end in (.011,.639):
        grate.box('Grille','oxide',(-.307,.046,end),(.310,.026,.022),.004)
    for index in range(5):
        grate.box('Grille','iron',(-.307,.047,.085+.12*index),(.31,.023,.036),.004)
    return {'CellarDrain':drain,'CellarTread':tread,'CellarGrate':grate}


def measure(model):
    info = {}
    for name, solids in model.meshes.items():
        mesh = kit.merge_all(shape for _,shape in solids)
        info[name] = {'bounds':kit.bounds(mesh),'triangles':kit.triangle_count(mesh)}
    return info


def soil_uv(source_vertex):
    # Blender XY is Unity XZ. Use the identical world projection as the hillside mesh.
    return tuple((offset + coordinate) / SOIL_PITCH
                 for offset,coordinate in zip(SOIL_ORIGIN_XZ,source_vertex[:2]))


def write_model(path,model,anchors,source=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new(path.stem,None)
    bpy.context.collection.objects.link(root)
    material = bpy.data.materials.new('Ridge finishes')
    material.use_nodes = True
    texture = material.node_tree.nodes.new('ShaderNodeTexImage')
    texture.image = bpy.data.images.load(str(ROOT/'Assets/Resources/City/RidgeGarages/RidgeGaragesSurface.png'))
    texture.image.pack()
    material.node_tree.links.new(texture.outputs['Color'],material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    for name, solids in model.meshes.items():
        if name == 'SortingPotato':
            solids = [(finish,kit.translated(shape,tuple(-a for a in POTATO_GRIP))) for finish,shape in solids]
        obj = garage['mesh_object'](root,name,solids,None if name.startswith('COL_') else material)
        if name == 'SortingPotato':
            x,y,z = POTATO_GRIP
            obj.location = (x,z,y)
        if name == 'EarthBank':
            soil = material.copy()
            soil.name = 'Residential soil'
            soil_texture = soil.node_tree.nodes.get(texture.name)
            soil_texture.image = bpy.data.images.load(str(ROOT/'Assets/Resources/Textures/CityGroundSoilAlbedo.png'))
            soil_texture.image.filepath = '//../../../Assets/Resources/Textures/CityGroundSoilAlbedo.png'
            obj.data.materials[0] = soil
            uv = obj.data.uv_layers.active.data
            for loop in obj.data.loops:
                uv[loop.index].uv = soil_uv(obj.data.vertices[loop.vertex_index].co)
    for name,(x,y,z) in anchors.items():
        anchor = bpy.data.objects.new(name,None)
        bpy.context.collection.objects.link(anchor)
        anchor.parent = root
        anchor.location = (x,z,y)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,axis_forward='-Z',axis_up='Y',
                            apply_scale_options='FBX_SCALE_ALL',bake_anim=False,add_leaf_bones=False,path_mode='STRIP')
    if source:
        for obj in root.children:
            if obj.name.startswith('COL_'):
                obj.hide_set(True)
                obj.hide_render=True
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(source),compress=True,relative_remap=False)


def verify_file(path,info,anchors,lights):
    # Measure the delivered FBX after an independent import (including collision meshes).
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    meshes = {o.name:o for o in bpy.context.scene.objects if o.type=='MESH'}
    assert meshes.keys() == info.keys()
    for name, measurement in info.items():
        obj = meshes[name]
        points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        lo, hi = measurement['bounds']
        for axis, source_axis in enumerate((0,2,1)):
            assert abs(min(p[source_axis] for p in points)-lo[axis]) < .002, name
            assert abs(max(p[source_axis] for p in points)-hi[axis]) < .002, name
        if not name.startswith('COL_'):
            assert obj.data.uv_layers.active, name
        if name == 'EarthBank':
            uvs = obj.data.uv_layers.active.data
            for loop in obj.data.loops:
                expected = soil_uv(points[loop.vertex_index])
                assert max(abs(a-b) for a,b in zip(uvs[loop.index].uv,expected)) < .0001, name
    for name,(x,y,z) in anchors.items():
        actual = bpy.context.scene.objects[name].matrix_world.translation
        assert max(abs(a-b) for a,b in zip(actual,(x,z,y)))<.002
    if 'SortingPotato' in meshes:
        x,y,z = POTATO_GRIP
        actual = meshes['SortingPotato'].matrix_world.translation
        assert max(abs(a-b) for a,b in zip(actual,(x,z,y))) < .002
    for role,(source,aim) in lights.items():
        lens = meshes['Lens'+role]
        assert lens.parent == bpy.context.scene.objects['Light'+role].parent
        assert lens.parent == bpy.context.scene.objects['Aim'+role].parent
        direction = (Vector(aim)-Vector(source)).normalized()
        distances = []
        for vertex in lens.data.vertices:
            x,z,y = lens.matrix_world @ vertex.co
            distances.append((Vector((x,y,z))-Vector(source)).dot(direction))
        assert -.065 < max(distances) < -.014, role
        assert direction.y < 0 and (direction.x > .95 if role == 'Wall' else direction.z > .8), role
    if 'COL_LanePole' in meshes:
        (x0,y0,z0),(x1,y1,z1) = info['COL_LanePole']['bounds']
        assert y0 == 0 and 2.58 < y1 < 2.62
        assert max(abs(c) for c in (x0,z0,x1,z1)) <= .15


def generation(args):
    models = {NAME:create_model(),'CellarLaneLamp':lane_lamp(),**paving()}
    info = {name:measure(model) for name,model in models.items()}
    doors = [(x+shift,1.0,face_z(x+shift)-.17) for x,shift in zip(DOORS,(-.13,.07,-.15,.06,-.04))]
    anchors = {name:{} for name in models}
    anchors[NAME].update({f'Door{i+1:02d}':point for i,point in enumerate(doors)})
    lamp_models = {NAME:LIGHTS,'CellarLaneLamp':LANE_LIGHT}
    for name,lights in lamp_models.items():
        for role,(source,aim) in lights.items():
            anchors[name]['Light'+role] = source
            anchors[name]['Aim'+role] = aim
    manifest = {'model':NAME,'units':'Unity metres, +Z into yard','doors':doors,
                'earthUvWorldOriginXZ':SOIL_ORIGIN_XZ,'earthUvWorldTileMetres':SOIL_PITCH,
                'lamps':LIGHTS,'laneLamp':LANE_LIGHT['Lane'],'sortingPotatoPivot':POTATO_GRIP,
                'tableBoundsXZ':TABLE,'cartBoundsXZ':CART,'models':info,
                'geometrySHA256':hashlib.sha256(json.dumps(
                    {key:model.meshes for key,model in models.items()},separators=(',',':')).encode()).hexdigest()}
    record = json.dumps(manifest,indent=2)+'\n'
    blend = args.source_dir / (NAME+'.blend')
    json_path = args.model_dir / (NAME+'.json')
    if args.validate_only:
        assert json_path.read_text() == record, 'Generated geometry or footprint has changed'
        assert blend.is_file()
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        for name,model in models.items():
            write_model(args.model_dir/(name+'.fbx'),model,anchors[name],blend if name==NAME else None)
        json_path.write_text(record)
    for name in models:
        verify_file(args.model_dir/(name+'.fbx'),info[name],anchors[name],lamp_models.get(name,{}))
    for name,bounds in [('SortingCorner',TABLE),('Handcart',CART)]:
        lo, hi = info[NAME][name]['bounds']
        assert bounds[0]-.001 <= lo[0] <= hi[0] <= bounds[2]+.001
        assert bounds[1]-.001 <= lo[2] <= hi[2] <= bounds[3]+.001
    print(f'{NAME}: closed solids, export bounds, frontage, UV and work-corner footprint passed.')


if __name__=='__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--validate-only',action='store_true')
    parser.add_argument('--model-dir',type=Path,default=MODEL)
    parser.add_argument('--source-dir',type=Path,default=SOURCE)
    generation(parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []))
