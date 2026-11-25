#!/usr/bin/env python3
"""Four fixed-metre Ridge garages. Geometry, material charts, collision and lamp anchors share Unity metres."""
import argparse
from collections import defaultdict
import hashlib
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
import numpy as np
from mathutils import Vector

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
import bar_parts as parts
import interior_kit as kit
from atlas_kit import PixelCanvas
from city_garage_relief import floor_damp, relief_atlas, wood_grain, worktop_scores

MODEL_DIR = ROOT / 'Assets/Resources/City/RidgeGarages'
SOURCE_DIR = ROOT / 'ArtSource/City/RidgeGarages'
NAME = 'RidgeGarages'
# The clear coat lives in the albedo alpha, matching the city's workshop material.
FINISHES = {
    'concrete': ((142, 141, 131), .24), 'slab': ((156, 153, 142), .36),
    'felt': ((77, 80, 77), .25), 'floor': ((134, 132, 121), .18),
    'timber': ((117, 100, 77), .25), 'repair': ((143, 120, 87), .24),
    'endgrain': ((116, 98, 74), .22), 'worktop': ((140, 112, 81), .28),
    'sage': ((93, 112, 100), .35), 'oxide': ((105, 86, 75), .32),
    'iron': ((79, 85, 82), .37), 'zinc': ((152, 152, 139), .54),
    'enamel': ((130, 87, 68), .47), 'rubber': ((55, 57, 53), .23),
    'leather': ((79, 69, 61), .29), 'glass': ((220, 209, 169), .57),
    'limewash': ((190, 186, 165), .16), 'dado': ((116, 135, 124), .28),
    'canvas': ((147, 135, 106), .18), 'brass': ((165, 138, 88), .54),
    'case': ((139, 143, 137), .47), 'motor': ((138, 72, 56), .41),
    'orange': ((161, 118, 69), .32), 'card': ((148, 125, 94), .14),
}
GRID = 5
TILE = 1024 // GRID
GUTTER = 5
WORK_LIGHT = (5.72, 2.25, -5.31)
WORK_AIM = (5.67, .90, -5.05)
ENTRY_LIGHT = (4.28, 2.49, .43)
ENTRY_AIM = (2.30, .51, 1.0)
INSPECTION_LIGHT = (3.90, 1.46, -2.74)
INSPECTION_AIM = (5.06, .57, -2.56)
CEILING_LIGHT = (5.25, 2.31, -3.15)
LIGHT_ANCHORS = {'WorkLight':WORK_LIGHT, 'WorkAim':WORK_AIM, 'EntryLight':ENTRY_LIGHT,
                 'EntryAim':ENTRY_AIM, 'InspectionLight':INSPECTION_LIGHT, 'InspectionAim':INSPECTION_AIM,
                 'CeilingLight':CEILING_LIGHT}


def texture():
    image = np.full((1024, 1024, 4), 90, dtype=np.uint8)
    db = TILE - 2 * GUTTER
    v, u = np.mgrid[0:db, 0:db] / (db - 1)
    for i, (key, (rgb, gloss)) in enumerate(FINISHES.items()):
        rng = np.random.default_rng(60209 + i)
        noise = rng.normal(0, 1.5, u.shape)
        variation = noise + 1.9*np.sin(8*u+3*np.sin(7*v)) + 1.2*np.cos(11*v+2*u)
        polish = np.zeros_like(u)
        if key in ('concrete', 'slab', 'floor'):
            grit = rng.normal(0, 2.4, u.shape)
            pits = np.maximum(0, grit - 3)
            variation += grit - 2.1*pits + 1.2*np.sin(52*u+3*v)
            polish -= pits*.014
            if key == 'floor':
                # v starts at the dry back wall and ends at the rainward lip.
                damp = floor_damp(u,v)
                variation -= damp*23
                polish += damp*.37
            if key == 'slab':
                variation += 2.3*np.sin(21*u+7*v)
        elif key in ('timber', 'repair', 'endgrain', 'worktop'):
            phase = wood_grain(key,u,v)
            variation += 5*np.sin(phase)+2.7*np.sin(2.35*phase)+2.2*np.sin(20*v)
            if key == 'worktop':
                groove=worktop_scores(u,v)
                variation-=13*groove
                polish-=.10*groove
        elif key in ('sage', 'oxide', 'enamel', 'motor'):
            for _ in range(15):
                x,begin,length = rng.uniform(.03,.98), rng.uniform(0,.68), rng.uniform(.13,.6)
                streak=np.exp(-((u-x-.004*np.sin(v*14))/.012)**2)*np.clip((v-begin)*28,0,1)*np.clip((begin+length-v)*6,0,1)
                variation-=9*streak
                polish-=.075*streak
            chalk=np.clip((.18-np.minimum(u,1-u))*3,0,1)
            variation+=6*chalk
            polish-=.055*chalk
        elif key in ('limewash','dado'):
            grain=rng.normal(0,1.6,u.shape)
            brush=np.cos(60*u+5*v)+.6*np.sin(101*u+3*v)
            variation+=grain+2.5*brush+2*np.sin(12*v+3*u)
            polish-=np.abs(grain)*.014
        elif key in ('iron', 'zinc','case','brass'):
            variation += 1.4*np.sin(152*v + .5*np.cos(8*u))
            polish += .025*np.cos(22*v)
            if key=='case':
                pores=np.maximum(0,-noise-.8)
                variation+=noise-2.8*pores
                polish-=.04*pores
        elif key=='canvas':
            variation+=2.7*np.cos(123*v)*np.cos(102*u)
        elif key=='card':
            ripple=.012*np.sin(39*u+17*v)+.009*np.sin(15*u-33*v)
            oil=np.zeros_like(u)
            for x,y,rx,ry in ((.28,.53,.18,.12),(.41,.46,.094,.079),(.62,.38,.033,.040),(.67,.60,.023,.020)):
                edge=np.hypot((u-x)/rx,(v-y)/ry)+ripple*5
                oil=np.maximum(oil,np.clip((1.15-edge)*3.1,0,1))
            variation+=noise+1.6*np.sin(160*u)-48*oil-7*np.exp(-((v-.32-u*.16)/.011)**2)
            polish+=.095*oil
        elif key == 'felt':
            variation += rng.normal(0, 2.8, u.shape)
        elif key in ('rubber', 'leather'):
            variation += noise - 1.5*np.maximum(0, -noise-.7)
        rgba = np.dstack((np.clip(np.array(rgb)+variation[:,:,None],0,255),
                          np.clip((gloss+polish)*255-.34*variation,0,255))).astype(np.uint8)
        row, col = divmod(i, GRID)
        image[row*TILE:(row+1)*TILE, col*TILE:(col+1)*TILE] = np.pad(
            rgba, ((GUTTER,GUTTER),(GUTTER,GUTTER),(0,0)), mode='edge')
    canvas = PixelCanvas(1024,1024)
    canvas.pixels[:] = image.tobytes()
    return canvas.png_bytes()


def turned(geometry, angle, origin):
    return kit.translated(parts.u_rotated(geometry, (0, angle, 0)), origin)


def pipe(a, b, radius, sides=10, end_radius=None):
    a, b = Vector(a), Vector(b)
    delta = b - a
    rotation = Vector((0,1,0)).rotation_difference(delta.normalized())
    geometry = parts.u_tapered_cylinder((0,0,0),(2*radius,delta.length*.5,2*radius),
                                        1 if end_radius is None else end_radius/radius,sides)
    vertices, faces = geometry
    return [tuple((a+b)*.5 + rotation @ Vector(p)) for p in vertices], faces


def rim(center, section, segments=28):
    # Closed revolution around the axle, preserving a real empty hole for the spokes.
    vertices = []
    for axial, radius in section:
        for i in range(segments):
            a = math.tau*i/segments
            vertices.append((center[0]+axial, center[1]+radius*math.sin(a), center[2]+radius*math.cos(a)))
    faces = []
    for j in range(len(section)):
        k = (j+1)%len(section)
        for i in range(segments):
            n = (i+1)%segments
            faces.append((j*segments+i,j*segments+n,k*segments+n,k*segments+i))
    if parts.signed_volume((vertices,faces)) < 0:
        faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def plate(outline, thickness):
    """A stamped part in XY, extruded about local Z; outline follows its cut edge."""
    n = len(outline)
    verts = [(x,y,z) for z in (-thickness/2,thickness/2) for x,y in outline]
    faces = [tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
    if parts.signed_volume((verts,faces)) < 0:
        faces = [tuple(reversed(f)) for f in faces]
    return verts,faces


def curved_guard(radius, start, end, steps=14):
    """Pressed mudguard following the rear wheel; the two lips roll toward the tyre."""
    cross=[(-.054,-.017),(-.050,.003),(-.027,.013),(.027,.013),(.050,.003),(.054,-.017),
           (.048,-.017),(.044,-.001),(.025,.007),(-.025,.007),(-.044,-.001),(-.048,-.017)]
    verts=[]
    for step in range(steps+1):
        angle=math.radians(start+(end-start)*step/steps)
        verts.extend((x,(radius+r)*math.cos(angle), (radius+r)*math.sin(angle)) for x,r in cross)
    n=len(cross)
    faces=[tuple(reversed(range(n))),tuple(range(steps*n,(steps+1)*n))]
    for row in range(steps):
        for i in range(n):
            a=row*n+i; b=row*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    if parts.signed_volume((verts,faces))<0:
        faces=[tuple(reversed(f)) for f in faces]
    return verts,faces


class Assembly:
    def __init__(self):
        self.meshes = defaultdict(list)
        self.boxes = {}

    def add(self, group, finish, solid):
        assert parts.signed_volume(solid) > 0, (group, finish)
        self.meshes[group].append((finish, solid))

    def box(self, group, finish, center, size, bevel=.012):
        self.add(group, finish, kit.joint_box(center, size, bevel))

    def rod(self, group, finish, a, b, r, sides=10, end=None):
        self.add(group, finish, pipe(a,b,r,sides,end))

    def path(self, group, finish, points, r, sides=6):
        for a,b in zip(points,points[1:]):
            self.rod(group,finish,a,b,r,sides)

    def collision(self, name, center, size):
        assert name not in self.boxes
        self.boxes[name] = (center, size)


def rail(m, label, a, b, intervals):
    group = 'Roof'
    x0, z0 = a
    x1, z1 = b
    dx, dz = abs(x1-x0), abs(z1-z0)
    m.box(group, 'concrete', ((x0+x1)/2,3.06,(z0+z1)/2), (max(.12,dx),.12,max(.12,dz)), .018)
    for i in range(intervals+1):
        t = i/intervals
        x, z = x0+t*(x1-x0), z0+t*(z1-z0)
        m.box(group,'zinc',(x,3.126,z),(.118,.018,.118),.005)
        m.box(group,'iron',(x,3.575,z),(.046,.90,.046),.004)
    for h, radius in ((3.54,.015),(4.025,.027)):
        m.rod(group,'iron',(x0,h,z0),(x1,h,z1),radius)
    m.collision('Guard'+label, ((x1+x0)/2,3.525,(z1+z0)/2),(max(dx,.12),1.05,max(dz,.12)))


def shell(m):
    # Floor panels meet the open door at Z=0; the roof tiles are the upper walkable datum.
    for i in range(4):
        cx = -5.25 + i*3.5
        m.box('Shell','floor',(cx,-.073,-3),(3.496,.146,6),.008)
    m.collision('Floor', (0,-.073,-3),(14,.146,6))
    m.box('Shell','concrete',(0,1.35,-5.89),(14,2.7,.22),.021)
    m.collision('RearWall',(0,1.35,-5.89),(14,2.7,.22))
    for i, x in enumerate((-6.89,-3.5,0,3.5,6.89)):
        m.box('Shell','concrete',(x,1.35,-2.885),(.22,2.7,5.77),.016)
        if i in (0,3,4):
            m.collision('Wall'+str(i),(x,1.35,-2.885),(.22,2.7,5.77))
    m.box('Shell','concrete',(0,2.575,-.119),(13.78,.25,.27),.014)
    m.collision('Lintel',(5.25,2.575,-.135),(3.28,.25,.27))
    m.collision('ClosedRow',(-1.75,1.34,-.27),(10.5,2.68,.20))
    m.box('Roof','concrete',(0,2.815,-3),(14,.23,6),.025)
    m.collision('Roof',(0,2.85,-3),(14,.30,6))
    # Two ninety-centimetre courses run from the upper courtyard across to the stair landing.
    # Membrane in front is laid as separate laps; water runs forward to the eaves.
    for row in range(2):
        for i in range(12):
            x = -7+(i+.5)*(14/12)
            m.box('Roof','slab',(x,2.964,-5.55+row*.90),(14/12-.006,.072,.895),.009)
    for i in range(7):
        m.box('Roof','felt',(-6+i*2,2.954,-2.10),(1.997,.048,4.199),.008)
    rail(m,'Front',(-6.87,-.14),(6.87,-.14),9)
    rail(m,'Left',(-6.87,-5.88),(-6.87,-.23),4)
    rail(m,'Right',(6.87,-4.20),(6.87,-.23),3)
    rail(m,'Rear',(-4.24,-5.88),(6.87,-5.88),7)
    # A folded gutter has a visible open trough and discharges into a collared downpipe.
    m.box('Shell','zinc',(0,2.755,.061),(13.96,.03,.136),.007)
    m.box('Shell','zinc',(0,2.798,.124),(13.96,.064,.018),.004)
    for x in (-6.85,-3.52,-.035,3.48,6.85):
        m.box('Shell','iron',(x,2.761,.065),(.044,.042,.14),.005)
    x = -6.86
    m.rod('Shell','zinc',(x,.20,.111),(x,2.75,.111),.053,12)
    m.rod('Shell','zinc',(x,.20,.111),(x,.105,.20),.053,12)
    for y in (.68,1.78,2.52):
        m.rod('Shell','iron',(x,y-.018,.111),(x,y+.018,.111),.059,12)
    # Sixteen removable grates bear on a continuous angle frame set in the
    # forecourt. Through the slots the bituminous channel lining sits below the ribs.
    drain_z = .32
    m.box('Shell','felt',(0,-.014,drain_z),(13.6,.016,.182),.003)
    for side in (-1,1):
        m.box('Shell','iron',(0,-.005,drain_z+side*.098),(13.6,.018,.020),.002)
    # Chamfer the two long leading edges of each bar. Ends are bedded under
    # the frame; a full twelve-edge hardware bevel on every tooth is wasted.
    cross_section = [(-.016,-.011),(.016,-.011),(.016,.002),(.014,.004),
                     (-.014,.004),(-.016,.002)]
    rib_shape = ([(x,y,drain_z+offset) for offset in (-.089,.089) for x,y in cross_section],
                 [tuple(reversed(range(6))),tuple(range(6,12))] +
                 [(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)])
    for panel in range(16):
        start = -6.8 + panel*.85
        for offset in (.015,.835):
            m.box('Shell','iron',(start+offset,-.006,drain_z),(.024,.020,.177),.002)
        for rib in range(10):
            m.add('Shell','iron',kit.translated(rib_shape,(start+.065+rib*.080,0,0)))


def doors(m):
    for i, center in enumerate((-5.25,-1.75,1.75)):
        for sign in (-1,1):
            cx = center+sign*.797
            if i == 0:
                for plank in range(8):
                    x = cx+(plank-3.5)*.194
                    # Old and new wood meet at slightly different repair heights.
                    join = .25+.044*((plank+2*(sign+1))%4)
                    top = 2.406
                    m.box('Doors','timber',(x,(join+top)/2,-.141),(.19,top-join,.079),.006)
                    m.box('Doors','repair',(x,(.061+join)/2,-.141),(.19,join-.067,.079),.005)
                m.box('Doors','repair',(cx,.40,-.083),(1.55,.090,.04),.006)
            else:
                finish = ('sage' if sign<0 else 'iron') if i == 1 else 'oxide'
                m.box('Doors',finish,(cx,1.234,-.144),(1.582,2.358,.078),.012)
                for h in (.18,2.28):
                    m.box('Doors',finish,(cx,h,-.096),(1.55,.064,.029),.004)
                if i==1:
                    m.box('Doors','iron',(cx-.25,1.23,-.100),(.017,2.275,.016),.004)
                if i==2 and sign==1:
                    m.box('Doors','iron',(cx,1.047,-.093),(.827,1.957,.037),.007)
                    m.box('Doors','oxide',(cx,1.047,-.069),(.778,1.902,.026),.007)
                    m.rod('Doors','zinc',(cx+.22,1.05,-.019),(cx+.34,1.05,-.019),.013)
                    m.box('Doors','iron',(cx+.34,1.05,-.039),(.032,.116,.037),.004)
            for y in (.38,1.98):
                x = cx+sign*.735
                m.rod('Doors','iron',(x,y-.073,-.081),(x,y+.073,-.081),.031)
                m.box('Doors','iron',(x-sign*.115,y,-.088),(.235,.045,.025),.004)
        m.box('Doors','zinc',(center,1.07,-.071),(.23,.052,.034),.005)
    # Fold both leaves into the open bay, latched against its own reveals.
    for sign in (-1,1):
        x = 5.2+sign*1.48
        m.box('Doors','sage',(x,1.224,-1.05),(.074,2.34,1.57),.013)
        for h in (.20,1.20,2.25):
            m.box('Doors','iron',(x-sign*.054,h,-1.05),(.035,.055,1.53),.006)
        m.add('Doors','iron',pipe((x-sign*.054,.25,-1.79),(x-sign*.054,2.19,-.33),.020,6))
        m.collision('OpenLeaf'+('L' if sign<0 else 'R'), (x,1.224,-1.05),(.114,2.34,1.57))


def exterior_fittings(m):
    def part(key,shape,x,y,z):
        m.add('Doors',key,kit.translated(shape,(x,y,z)))
    for i,cx in enumerate((-5.25,-1.75,1.75)):
        # Each pair wears at the knuckle, draw bolt and foot; its fasteners sit on real leaves.
        for side in (-1,1):
            x=cx+side*1.50
            for y in (.38,1.98):
                outline=[(0,-.046),(.37,-.020),(.41,0),(.37,.020),(0,.046)]
                part('iron',plate([(x*-side,y) for x,y in outline],.017),x,y,-.053)
                for dx in (.08,.30):
                    m.rod('Doors','zinc',(x-side*dx,y,-.043),(x-side*dx,y,-.031),.013,6)
        m.box('Doors','iron',(cx+.03,1.04,-.046),(.49,.054,.036),.006)
        for dx in (-.146,.125):
            m.box('Doors','zinc',(cx+dx,1.04,-.021),(.058,.065,.035),.007)
        m.rod('Doors','iron',(cx+.18,1.039,-.021),(cx+.18,.939,-.012),.015)
        # A worn pull is a bent bar with air visible between the return legs.
        m.path('Doors','zinc',[(cx-.30,1.19,-.091),(cx-.30,1.19,.012),
                              (cx-.30,.96,.012),(cx-.30,.96,-.091)],.016,8)
        # Folded drip cap stands proud of the reveal and ends inside its piers.
        m.box('Doors','zinc',(cx,2.432,-.04),(3.16,.04,.20),.005)
        m.box('Doors','zinc',(cx,2.407,.054),(3.16,.045,.014),.003)
    for s in (-1,1):
        cx=-5.25+s*.797
        brace=plate([(-.70,-.078),(.70,.65),(.70,.78),(-.70,.053)],.036)
        if s<0: brace=parts.u_rotated(brace,(0,180,0))
        part('timber',brace,cx,1.27,-.076)
    # A ventilated transom set into one leaf and a welded heel patch on the other.
    m.box('Doors','iron',(-2.55,1.88,-.087),(.73,.40,.052),.013)
    for row in range(4):
        vent=parts.u_rotated(kit.chamfered_box((0,0,0),(.65,.049,.053),.004),(20,0,0))
        part('sage',vent,-2.55,1.76+row*.08,-.045)
    shape=plate([(-.6,-.17),(.53,-.17),(.58,-.08),(.58,.17),(-.48,.17),(-.6,.12)],.016)
    part('dado',shape,-.98,.35,-.093)
    # Flaking bottom staves and legible zinc kick plate at the wicket door.
    m.box('Doors','zinc',(2.547,.21,-.043),(.735,.19,.019),.006)
    m.box('Doors','iron',(1.08,1.86,-.081),(.50,.31,.049),.011)
    for row in range(3):
        m.box('Doors','oxide',(1.08,1.77+row*.083,-.045),(.442,.045,.029),.005)
    # Surface cable enters in conduit, then turns onto the entrance hood's backplate.
    m.path('Shell','iron',[(3.49,.56,.034),(3.49,2.49,.034),(3.58,2.58,.034),
                           (4.27,2.58,.034),(4.27,2.64,.034)],.020,8)
    m.box('Shell','iron',(3.49,.84,.054),(.16,.235,.083),.013)
    m.box('Shell','sage',(3.49,.84,.104),(.149,.22,.024),.009)
    for y in (1.20,2.10):
        m.box('Shell','zinc',(3.49,y,.053),(.094,.025,.032),.004)
    m.box('Shell','iron',(4.27,2.63,.066),(.16,.17,.102),.012)
    # A broad, enamelled bell points away from the box toward the public apron.
    ray=(Vector(ENTRY_AIM)-Vector(ENTRY_LIGHT)).normalized()
    crown=Vector(ENTRY_LIGHT)-ray*.214
    m.path('Shell','iron',[(4.27,2.65,.098),(4.29,2.735,.28),
                           tuple(crown+Vector((0,.040,0))),tuple(crown)],.024,10)
    rotate=Vector((0,-1,0)).rotation_difference(ray)
    def hood(local):
        vs,fs=local
        return [tuple(Vector(ENTRY_LIGHT)+rotate@Vector(v)) for v in vs],fs
    m.add('Shell','sage',hood(parts.u_tapered_cylinder((0,.132,0),(.36,.083,.31),.32,16)))
    m.add('Shell','iron',hood(parts.u_cylinder((0,.050,0),(.365,.009,.315),20)))
    m.add('EntryLens','glass',hood(parts.u_tapered_cylinder((0,.027,0),(.285,.018,.244),1.12,20)))


def wheel(m, pose):
    def add(finish, geometry): m.add('Moped', finish, pose(geometry))
    def axle(finish, half, radius):
        add(finish, pipe((-half,0,0),(half,0,0),radius,16))
    add('rubber',rim((0,0,0),[(-.033,.227),(-.047,.246),(-.043,.272),
        (-.025,.286),(.024,.286),(.042,.271),(.047,.246),(.032,.227)]))
    add('zinc',rim((0,0,0),[(-.032,.231),(-.030,.220),(-.016,.207),(.019,.207),(.03,.220),(.033,.231)]))
    axle('iron',.061,.053)
    axle('case',.064,.064)
    add('zinc',rim((0,0,0),[(.064,.043),(.064,.018),(.070,.018),(.070,.043)],16))
    add('zinc',rim((0,0,0),[(-.064,.043),(-.064,.018),(-.070,.018),(-.070,.043)],16))
    for spoke in range(18):
        a = math.tau*spoke/18
        b = a+.31*(-1 if spoke%2 else 1)
        add('zinc',pipe((.05*(-1 if spoke%2 else 1),.047*math.sin(b),.047*math.cos(b)),
                        (0,.218*math.sin(a),.218*math.cos(a)),.0028,5))


def moped(m):
    origin, angle = (5.08,.0,-2.58), -27
    def posed(shape): return turned(shape, angle, origin)
    def add(finish, shape): m.add('Moped',finish,posed(shape))
    def rod(finish,a,b,r,sides=10): add(finish,pipe(a,b,r,sides))
    def box(finish, pos, size, bevel=.016): add(finish,kit.joint_box(pos,size,bevel))
    # The rear tyre and centre stand form three stable contacts. The bare fork points to the entrance.
    wheel(m,lambda shape: posed(kit.translated(shape,(0,.286,-.61))))
    add('dado',kit.translated(curved_guard(.31,-118,66),(0,.286,-.61)))
    for s in (-1,1):
        rod('sage',(s*.095,.295,-.61),(s*.072,.42,-.04),.024)
        rod('iron',(s*.115,.32,-.58),(s*.112,.72,-.39),.021)
        rod('zinc',(s*.115,.39,-.55),(s*.115,.64,-.43),.032)
        rod('sage',(s*.066,.36,-.02),(s*.062,.79,.44),.031)
        rod('zinc',(s*.094,.76,.45),(s*.094,.335,.62),.026)
        rod('iron',(s*.094,.54,.54),(s*.094,.291,.64),.016)
        rod('iron',(s*.061,.34,-.10),(s*.177,.03,-.24),.017)
        rod('rubber',(s*.19,.019,-.286),(s*.19,.019,-.193),.019)
        rod('iron',(s*.12,.58,-.60),(s*.14,.765,-.68),.012)
    rod('sage',(0,.38,-.05),(0,.77,-.27),.043)
    rod('enamel',(0,.72,-.27),(0,.78,.44),.043)
    # Shaped tank with a low knee cut and silver filler cap.
    tank = kit.lathe([(.03,-.22),(.105,-.18),(.134,-.10),(.134,.10),(.105,.19),(.04,.245)],16)
    add('motor',kit.translated(kit.scaled(tank,(1,.76,1)),(0,.761,.103)))
    box('zinc',(0,.866,.09),(.065,.016,.068),.007)
    box('leather',(0,.83,-.32),(.235,.107,.44),.037)
    box('iron',(0,.763,-.33),(.203,.046,.41),.01)
    for x in (-.12,0,.12):
        rod('zinc',(x,.73,-.78),(x,.73,-.54),.009)
    for z in (-.78,-.54):
        rod('zinc',(-.12,.73,z),(.12,.73,z),.01)
    box('iron',(0,.397,.012),(.20,.17,.25),.035)
    rod('iron',(.09,.437,.12),(.138,.29,.141),.019)
    rod('iron',(.138,.29,.141),(.143,.233,-.16),.021)
    rod('zinc',(.143,.233,-.16),(.143,.256,-.65),.037,12)
    rod('zinc',(0,.77,.44),(0,1.004,.39),.025)
    for s in (-1,1):
        rod('zinc',(0,1.0,.39),(s*.245,1.044,.364),.014)
        rod('rubber',(s*.19,1.04,.365),(s*.306,1.04,.31),.021)
        rod('zinc',(s*.175,1.01,.407),(s*.285,1.002,.368),.007,7)
        rod('rubber',(s*.12,.356,-.15),(s*.229,.356,-.15),.018)
    rod('iron',(0,.871,.452),(0,.871,.56),.069,16)
    rod('glass',(0,.871,.558),(0,.871,.572),.063,16)
    # Pressed chain case, kick crank and clutch cover break up the engine block in side view.
    case=plate([(-.34,-.043),(.23,-.058),(.28,-.031),(.28,.043),(.23,.071),(-.33,.044)],.028)
    add('dado',kit.translated(parts.u_rotated(case,(0,90,0)),(-.091,.312,-.30)))
    rod('case',(-.132,.405,-.036),(.133,.405,-.036),.104,16)
    rod('zinc',(.133,.405,-.036),(.147,.405,-.036),.069,16)
    rod('case',(-.141,.405,-.036),(-.133,.405,-.036),.077,16)
    for i in range(4):
        a=math.tau*i/4+.35
        y,z=.405+.080*math.cos(a),-.036+.080*math.sin(a)
        rod('iron',(.133,y,z),(.143,y,z),.009,6)
    rod('iron',(.142,.396,-.063),(.167,.312,-.172),.012)
    rod('rubber',(.158,.312,-.172),(.224,.312,-.172),.016)
    # Aluminium cylinder, plug, HT lead and carburettor sit between the down tubes.
    box('case',(0,.551,.066),(.124,.144,.127),.022)
    for y in (.501,.527,.553,.579,.605):
        box('case',(0,y,.066),(.158,.012,.152),.005)
    rod('brass',(.014,.603,.073),(.014,.638,.073),.017,6)
    rod('rubber',(.014,.635,.073),(.008,.661,.071),.017)
    for a,b in zip([(.01,.66,.07),(-.03,.696,.03),(-.085,.686,-.066)],
                   [(-.03,.696,.03),(-.085,.686,-.066),(-.087,.641,-.138)]):
        rod('rubber',a,b,.006,6)
    rod('case',(0,.56,-.01),(0,.557,-.12),.031)
    rod('iron',(0,.572,-.118),(0,.545,-.22),.045,12)
    rod('brass',(-.043,.657,.064),(-.072,.574,-.061),.005)
    # Guards, tank side pressings, pedal and rear lamp remain distinct from the unattached front hub.
    for s in (-1,1):
        padded=plate([(-.075,-.036),(.043,-.044),(.086,-.024),(.095,.022),(-.064,.033)],.010)
        add('rubber',kit.translated(parts.u_rotated(padded,(0,90,0)),(s*.125,.757,.116)))
        rod('zinc',(s*.105,.307,-.62),(s*.081,.501,-.842),.009,7)
        rod('zinc',(s*.22,1.03,.405),(s*.061,.90,.56),.005,6)
        rod('rubber',(s*.061,.90,.56),(s*.048,.677,.38),.005,6)
    box('iron',(0,.553,-.847),(.105,.092,.071),.009)
    box('motor',(0,.555,-.881),(.095,.052,.025),.008)
    rod('case',(0,.895,.438),(0,.936,.399),.044,14)
    rod('glass',(0,.936,.399),(0,.941,.394),.040,14)
    for z in (-.33,-.18):
        rod('brass',(-.018,.78,z),(.018,.78,z),.014,8)
    # Detached wheel leans against the front trestle and can be seen through the fork.
    def leaning(shape):
        upright = parts.u_rotated(shape,(0,79,0))
        return kit.translated(parts.u_rotated(upright,(-13,0,0)),(6.42,.285,-4.70))
    wheel(m,leaning)
    m.collision('MopedRear',(5.25,.421,-2.95),(.60,.842,.79))
    m.collision('MopedFront',(4.91,.527,-2.31),(.64,1.054,.77))
    m.collision('LooseWheel',(6.42,.285,-4.70),(.59,.57,.21))
    # Torn carton pushed under the clutch cover. The centre stand still bears on concrete.
    card=plate([(-.32,-.18),(-.16,-.185),(-.115,-.175),(.26,-.178),(.30,-.143),
                (.304,.153),(.247,.171),(.216,.153),(.203,.179),(-.29,.193),
                (-.315,.114),(-.304,.092)],.004)
    m.add('Workshop','card',kit.translated(parts.u_rotated(card,(90,-8,0)),(5.343,.0027,-2.47)))


def workshop(m):
    for x in (4.39,5.78,6.42):
        for z in (-4.78,-5.37):
            m.box('Workshop','iron',(x,.43,z),(.056,.86,.056),.005)
    for i in range(4):
        m.box('Workshop','worktop',(5.40,.874,-4.79-.18*i),(2.22,.066,.176),.008)
    m.box('Workshop','iron',(5.405,.754,-4.77),(2.13,.10,.044),.006)
    m.box('Workshop','timber',(5.405,.223,-5.07),(2.12,.048,.57),.006)
    m.collision('Bench',(5.40,.453,-5.06),(2.22,.906,.72))
    # Cast vise with separate registering jaws, screw barrel and sliding handle.
    m.box('Workshop','sage',(4.73,.944,-4.84),(.29,.072,.24),.021)
    m.box('Workshop','sage',(4.73,1.03,-4.855),(.17,.16,.21),.019)
    for z in (-4.91,-4.775):
        m.box('Workshop','zinc',(4.73,1.125,z),(.195,.048,.037),.005)
    m.rod('Workshop','iron',(4.73,1.03,-4.92),(4.73,1.03,-4.63),.034,12)
    m.rod('Workshop','zinc',(4.68,.962,-4.63),(4.81,1.14,-4.63),.009)
    m.rod('Workshop','zinc',(5.20,.929,-4.98),(5.44,.929,-5.02),.013,12)
    for x in (5.55,5.62):
        washer = rim((0,0,0),[(-.002,.021),(-.002,.009),(.002,.009),(.002,.021)],12)
        m.add('Workshop','zinc',kit.translated(parts.u_rotated(washer,(0,0,90)),(x,.918,-4.95)))
    m.rod('Workshop','zinc',(5.84,.915,-4.96),(6.04,.915,-5.09),.012,8)
    m.rod('Workshop','zinc',(6.04,.915,-5.09),(6.063,.915,-5.128),.014,7)
    m.rod('Workshop','zinc',(6.04,.915,-5.09),(6.087,.915,-5.092),.014,7)
    m.box('Workshop','iron',(5.72,2.43,-5.765),(.095,.12,.033),.01)
    m.rod('Workshop','zinc',(5.72,2.45,-5.75),(5.72,2.50,-5.31),.019)
    m.rod('Workshop','iron',(5.72,2.50,-5.31),(5.72,2.42,-5.31),.029)
    m.add('Workshop','sage',parts.u_tapered_cylinder((5.72,2.344,-5.31),(.28,.080,.28),.35,16))
    m.rod('Lens','glass',(5.72,2.252,-5.31),(5.72,2.265,-5.31),.127,16)


def bench_fittings(m):
    def box(key, center, size, cut=.007):
        m.box('Workshop',key,center,size,cut)
    def rod(key,a,b,r,sides=8):
        m.rod('Workshop',key,a,b,r,sides)
    def hung_plate(key,outline,t,center):
        m.add('Workshop',key,kit.translated(plate(outline,t),center))
    # Upper limewash catches the hood; the lower oil-painted dado survives dirty hands.
    # Coats stop at the actual opening and belong to the exposed inner faces only.
    for finish,y,height in [('limewash',1.82,1.75),('dado',.469,.943)]:
        box(finish,(5.195,y,-5.769),(3.17,height,.019),.004)
        box(finish,(3.618,y,-3.018),(.014,height,5.49),.003)
        box(finish,(6.770,y,-3.019),(.018,height,5.49),.003)
    # Long tools hang on pins between two battens. Their head sizes and shafts graduate.
    box('sage',(5.23,1.65,-5.713),(1.49,.78,.078),.013)
    for x in (4.53,5.93):
        for h in (1.33,1.98):
            rod('zinc',(x,h,-5.672),(x,h,-5.656),.013,6)
    for i,scale in enumerate((1.15,.99,.85,.73)):
        x=4.76+i*.194
        outline=[(-.013,-.21),(.013,-.21),(.017,.076),(.042,.101),(.052,.145),
                 (.036,.158),(.014,.132),(-.004,.123),(-.024,.144),(-.029,.165),
                 (-.046,.157),(-.057,.124),(-.043,.089),(-.017,.074)]
        hung_plate('zinc',[(a*scale,b*scale) for a,b in outline],.021,(x,1.665,-5.626))
        ring=rim((0,0,0),[(-.010,.032),(-.010,.013),(.010,.013),(.010,.032)],12)
        m.add('Workshop','zinc',kit.translated(parts.u_rotated(kit.scaled(ring,(1,scale,scale)),(0,90,0)),
                                             (x,1.665-.21*scale,-5.626)))
        hanging_y=1.665+.132*scale
        rod('iron',(x,hanging_y,-5.675),(x,hanging_y,-5.599),.009)
    for x,handle in ((5.56,'orange'),(5.76,'timber')):
        box('iron',(x,1.47,-5.632),(.065,.063,.061))
        rod('zinc',(x,1.34,-5.61),(x,1.66,-5.61),.009)
        rod(handle,(x,1.64,-5.61),(x,1.78,-5.61),.026,10)
    # A genuine folded tray contains the dismounted axle, nuts and washers.
    tx,tz=5.42,-4.985
    box('zinc',(tx,.911,tz),(.58,.009,.205),.003)
    for offset in (-.096,.096):
        box('zinc',(tx,.931,tz+offset),(.58,.035,.010),.003)
    for offset in (-.285,.285):
        box('zinc',(tx+offset,.931,tz),(.010,.035,.186),.002)
    for x in (5.52,5.66):
        ring=rim((0,0,0),[(-.009,.022),(-.009,.010),(.009,.010),(.009,.022)],6)
        m.add('Workshop','iron',kit.translated(parts.u_rotated(ring,(0,0,90)),(x,.925,-5.04)))
    # A handled, dented oil can and a fabric tool roll sit at the other end of the bench.
    box('orange',(6.28,1.055,-5.23),(.231,.28,.132),.026)
    m.path('Workshop','zinc',[(6.21,1.16,-5.23),(6.15,1.23,-5.23),(6.09,1.243,-5.24)],.014)
    m.path('Workshop','iron',[(6.37,1.135,-5.23),(6.423,1.142,-5.23),(6.425,1.037,-5.23),
                              (6.37,1.03,-5.23)],.012)
    rod('iron',(6.32,1.19,-5.23),(6.32,1.209,-5.23),.026,12)
    rod('canvas',(5.91,.979,-5.29),(6.14,.979,-5.29),.068,12)
    for x in (5.945,6.111):
        rod('leather',(x-.012,.979,-5.29),(x+.012,.979,-5.29),.071,12)
    for x in (4.95,5.73):
        box('sage',(x,.666,-5.16),(.72,.179,.48),.013)
        box('sage',(x,.669,-4.906),(.708,.177,.027),.009)
        m.path('Workshop','zinc',[(x-.079,.67,-4.886),(x-.079,.67,-4.85),
                                  (x+.079,.67,-4.85),(x+.079,.67,-4.886)],.009)
    # One wall shelf carries a utility tin, two capped bottles and a folded rag.
    box('timber',(6.60,1.45,-3.48),(.295,.044,1.15))
    for z in (-3.92,-3.06):
        m.path('Workshop','iron',[(6.753,1.195,z),(6.473,1.424,z),(6.753,1.424,z)],.013)
    box('motor',(6.593,1.56,-3.80),(.18,.167,.161),.018)
    rod('zinc',(6.593,1.642,-3.80),(6.593,1.67,-3.80),.033,10)
    for z in (-3.55,-3.39):
        rod('dado',(6.58,1.473,z),(6.58,1.66,z),.049,10)
        m.rod('Workshop','dado',(6.58,1.66,z),(6.58,1.69,z),.049,10,.021)
        rod('iron',(6.58,1.69,z),(6.58,1.716,z),.024,10)
    for i in range(2):
        box('canvas',(6.61,1.485+i*.023,-3.11+i*.022),(.196,.024,.194),.009)
    # Fixed power feed along the back wall to the bench hood and the caged handheld lamp.
    m.path('Workshop','zinc',[(6.52,1.29,-5.744),(6.52,2.42,-5.744),(5.79,2.42,-5.744)],
           .014)
    box('iron',(6.52,1.34,-5.711),(.164,.20,.091))
    rod('zinc',(6.52,1.34,-5.66),(6.52,1.34,-5.648),.045,12)
    m.path('Workshop','iron',[(6.52,2.42,-5.74),(3.694,2.42,-5.74),(3.656,2.39,-5.70),
                              (3.656,2.38,-2.766),(3.656,2.33,-2.72),(3.656,1.81,-2.72)],.013)
    box('dado',(3.678,1.80,-2.72),(.107,.17,.12))
    # The inspection lamp hangs vertically. The steel backing cup, diffuser and cage
    # point into the box; the realtime source sits beyond the outer wire.
    lx,ly,lz=INSPECTION_LIGHT
    m.path('Workshop','iron',[(3.626,1.76,lz),(3.822,1.76,lz),
                              (3.83,1.70,lz),(3.798,1.676,lz)],.014)
    m.path('Workshop','zinc',[(3.798,1.68,lz),(3.813,1.654,lz),(3.811,1.602,lz)],.009)
    m.path('Workshop','rubber',[(3.72,1.78,-2.714),(3.746,1.702,-2.61),(3.745,1.42,-2.586),
                                (3.76,1.244,-2.609),(3.79,1.222,-2.68),(3.802,1.30,lz)],.008)
    rod('rubber',(3.81,1.268,lz),(3.81,1.38,lz),.032,12)
    box('sage',(3.795,ly,lz),(.044,.234,.135),.023)
    m.box('InspectionLens','glass',(3.831,ly,lz),(.059,.168,.096),.020)
    for z in (lz-.048,lz,lz+.048):
        m.path('Workshop','iron',[(3.804,ly-.104,z),(3.875,ly-.083,z),
                                  (3.875,ly+.084,z),(3.804,ly+.104,z)],.0045)
    for h in (ly-.104,ly+.104):
        box('iron',(3.808,h,lz),(.082,.032,.139),.010)


def build_model():
    result = Assembly()
    shell(result)
    doors(result)
    exterior_fittings(result)
    moped(result)
    workshop(result)
    bench_fittings(result)
    # A suspended opal drum gives the room a broad luminous surface. The cap grips
    # the glass above the emitter centre, keeping the four sides and underside clear.
    x,y,z=CEILING_LIGHT
    result.path('Workshop','zinc',[(3.656,2.42,-3.15),(3.656,2.644,-3.15),
                                   (3.704,2.682,-3.15),(x,2.682,z)],.012,8)
    result.rod('Workshop','iron',(x,2.649,z),(x,2.696,z),.078,16)
    result.rod('Workshop','zinc',(x,2.455,z),(x,2.655,z),.018,12)
    result.add('Workshop','sage',parts.u_tapered_cylinder((x,2.438,z),(.325,.025,.325),.63,20))
    diffuser=kit.lathe([(.105,-.092),(.142,-.083),(.161,-.058),(.161,.064),
                        (.151,.089),(.130,.105)],20)
    result.add('CeilingLens','glass',kit.translated(parts.u_rotated(diffuser,(-90,0,0)),CEILING_LIGHT))
    return result


def uv_chart(key, points, normal, seed):
    # UVs use the local grain direction of the member, with a separate end chart on board ends.
    axes = sorted(range(3),key=lambda d:max(p[d] for p in points)-min(p[d] for p in points),reverse=True)
    x, y = axes[:2]
    # A sheet is taller than it is wide; grain-based projection would turn
    # rain streaks sideways when the camera faces the street-side leaves.
    if key in ('sage','oxide','iron') and abs(normal.z)>.96 and (
        max(p[1] for p in points)-min(p[1] for p in points)>1.50):
        x,y=0,1
    if key == 'floor' and abs(normal.y)>.9:
        lo = min(p[0] for p in points)
        uvs = [((p[0]-lo)/3.5,(p[2]+6)/6) for p in points]
    elif key == 'card' and abs(normal.y)>.9:
        minx,maxx=min(p[0] for p in points),max(p[0] for p in points)
        minz,maxz=min(p[2] for p in points),max(p[2] for p in points)
        uvs=[((p[0]-minx)/(maxx-minx),(p[2]-minz)/(maxz-minz)) for p in points]
    else:
        if key in ('timber','repair') and abs(normal.y)>.92:
            key = 'endgrain'
        lows = [min(p[d] for p in points) for d in (x,y)]
        span = [max(p[d] for p in points)-l for d,l in zip((x,y),lows)]
        pitch = (1.30,.44) if key in ('worktop','timber','repair') else (1.55,1.2)
        factors = [1/max(s,r) for s,r in zip(span,pitch)]
        offsets = [((seed*.618+i*.27)%1)*max(0,1-s*f) for i,(s,f) in enumerate(zip(span,factors))]
        uvs = [(offsets[0]+(p[x]-lows[0])*factors[0],offsets[1]+(p[y]-lows[1])*factors[1]) for p in points]
    row,col = divmod(list(FINISHES).index(key),GRID)
    return [((col*TILE+GUTTER+.5+u*(TILE-2*GUTTER-1))/1024,
             1-(row*TILE+GUTTER+.5+(1-v)*(TILE-2*GUTTER-1))/1024) for u,v in uvs]


def mesh_object(root, name, solids, material=None):
    vertices, faces, finish, seeds = [], [], [], []
    for i,(key,solid) in enumerate(solids):
        points, indices = parts.to_source(solid)
        offset = len(vertices)
        vertices.extend(points)
        faces.extend([tuple(offset+j for j in f) for f in indices])
        finish.extend([key]*len(indices))
        seeds.extend([i]*len(indices))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    obj = bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent=root
    if material:
        obj.data.materials.append(material)
        uv=mesh.uv_layers.new(name='UVMap').data
        for p in mesh.polygons:
            vertices_source=[mesh.vertices[i].co for i in p.vertices]
            points = [(a,c,b) for a,b,c in vertices_source]
            nx,nz,ny=p.normal
            for loop,coords in zip(p.loop_indices,uv_chart(finish[p.index],points,Vector((nx,ny,nz)),seeds[p.index])):
                uv[loop].uv = coords
    return obj


def check_export(path, boxes):
    # Reimport the delivered file: this also catches exporters dropping hidden collision meshes.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    objects = bpy.context.scene.objects
    expected = {'GEO_Shell','GEO_Doors','GEO_Roof','GEO_Workshop','GEO_Moped','GEO_Lens',
                'GEO_EntryLens','GEO_InspectionLens','GEO_CeilingLens'}
    assert {o.name for o in objects if o.name.startswith('GEO_')} == expected
    assert {o.name[4:] for o in objects if o.name.startswith('COL_')} == set(boxes)
    for name in expected:
        assert objects[name].data.uv_layers.active
    for key,(center,size) in boxes.items():
        obj = objects['COL_'+key]
        coords = [obj.matrix_world @ v.co for v in obj.data.vertices]
        for axis, blender_axis in enumerate([0,2,1]):
            low = min(v[blender_axis] for v in coords)
            high = max(v[blender_axis] for v in coords)
            assert abs(low-center[axis]+size[axis]/2)<.001, key
            assert abs(high-center[axis]-size[axis]/2)<.001, key
    for label,expected_pos in LIGHT_ANCHORS.items():
        x,z,y = objects['ANCHOR_'+label].matrix_world.translation
        assert (Vector((x,y,z))-Vector(expected_pos)).length < .001
    # Door leaves drain down their height in the delivered UVs, including on tall patches.
    leaves=objects['GEO_Doors']
    reliable_panels=0
    for poly in leaves.data.polygons:
        world=[leaves.matrix_world @ leaves.data.vertices[i].co for i in poly.vertices]
        if not all(-3.45<p.x<-.15 and abs(p.y+.105)<.003 for p in world):
            continue
        if max(p.z for p in world)-min(p.z for p in world)<1.8 or max(p.x for p in world)-min(p.x for p in world)<1.3:
            continue
        uv=[leaves.data.uv_layers.active.data[i].uv for i in poly.loop_indices]
        for i,a in enumerate(world):
            b=world[i-1]
            if abs(b.z-a.z)>1.8 and abs(b.x-a.x)<.005:
                assert abs(uv[i].y-uv[i-1].y)>.10 and abs(uv[i].x-uv[i-1].x)<.002, (poly.index,uv)
                reliable_panels+=1
    assert reliable_panels>=2, 'Street-side rain chart not found'
    # Sample an actual rib and the bottom of the next slot on a delivered section.
    shell_mesh = objects['GEO_Shell']
    inverse = shell_mesh.matrix_world.inverted()
    for x,top in ((-6.735,.004),(-6.695,-.006)):
        start = inverse @ Vector((x,.32,.05))
        end = inverse @ Vector((x,.32,-.03))
        hit,point,_,_ = shell_mesh.ray_cast(start,(end-start).normalized(),distance=(end-start).length)
        assert hit and abs((shell_mesh.matrix_world @ point).z-top)<.001, 'Drain profile lost'
    # Free lower arrival and reversible rooftop exits checked against shipped visible geometry.
    rays = [((4.28,.3,.36),(0,0,-1),1.65), ((4.28,1.72,.36),(0,0,-1),1.65),
            ((-5.5,3.4,-6.25),(0,0,1),1.30), ((7.20,3.85,-5.10),(-1,0,0),1.10)]
    for label in ('Work','Entry','Inspection'):
        source=Vector(LIGHT_ANCHORS[label+'Light'])
        rays.append((source,(Vector(LIGHT_ANCHORS[label+'Aim'])-source).normalized(),.32))
    for name in expected:
        obj = objects[name]
        inverse = obj.matrix_world.inverted()
        # Unity disables cast shadows on each luminous lens. Rays inside this
        # diffuser must exit sideways and down toward all four room surfaces.
        fill_rays=[] if name=='GEO_CeilingLens' else [
            (CEILING_LIGHT,(math.cos(a*math.tau/8),0,math.sin(a*math.tau/8)),.45) for a in range(8)]
        if name!='GEO_CeilingLens':
            fill_rays.append((CEILING_LIGHT,(0,-1,0),.45))
        for pos,direction,distance in rays+fill_rays:
            to_source = lambda xyz: Vector((xyz[0],xyz[2],xyz[1]))
            start = inverse @ to_source(pos)
            end = inverse @ to_source(Vector(pos)+Vector(direction)*distance)
            hit = obj.ray_cast(start,(end-start).normalized(),distance=(end-start).length)[0]
            assert not hit, f'{name} closes passage at {pos}'


def generate(model_dir, source_dir, check):
    model = build_model()
    digest = hashlib.sha256()
    triangles = 0
    for entries in model.meshes.values():
        digest.update(json.dumps(entries,separators=(',',':')).encode())
        triangles += sum(kit.triangle_count(shape) for _,shape in entries)
    assert triangles < 40000
    atlas = texture()
    relief = relief_atlas(FINISHES,GRID,TILE,GUTTER)
    manifest = {'name':NAME,'coordinates':'Unity metres, +Z street, +Y up',
        'geometrySHA256':digest.hexdigest(),'atlasSHA256':hashlib.sha256(atlas).hexdigest(),
        'normalSHA256':hashlib.sha256(relief).hexdigest(),'lightAnchors':LIGHT_ANCHORS,
        'triangles':triangles,'roofTop':3.0,'frontZ':0,'depth':6,'workLight':WORK_LIGHT,'workAim':WORK_AIM,
        'boxes':{k:{'center':v[0],'size':v[1]} for k,v in model.boxes.items()}}
    text = json.dumps(manifest,indent=2)+'\n'
    fbx = model_dir/(NAME+'.fbx')
    record = model_dir/(NAME+'.json')
    diffuse = model_dir/(NAME+'Surface.png')
    normal = model_dir/(NAME+'Normal.png')
    blend = source_dir/(NAME+'.blend')
    if check:
        assert record.read_text() == text and diffuse.read_bytes()==atlas and normal.read_bytes()==relief
        assert fbx.is_file() and blend.is_file()
        check_export(fbx,model.boxes)
        print('Verified published FBX dimensions, clearances, material charts and deterministic mesh signature.')
        return
    model_dir.mkdir(exist_ok=True,parents=True)
    source_dir.mkdir(exist_ok=True,parents=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root=bpy.data.objects.new(NAME,None)
    bpy.context.collection.objects.link(root)
    material=bpy.data.materials.new(NAME+'Surface')
    diffuse.write_bytes(atlas)
    normal.write_bytes(relief)
    image=bpy.data.images.load(str(diffuse))
    image.pack()
    material.use_nodes=True
    nodes=material.node_tree.nodes
    node=nodes.new('ShaderNodeTexImage')
    node.image=image
    material.node_tree.links.new(node.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])
    invert=nodes.new('ShaderNodeMath')
    invert.operation='SUBTRACT'
    invert.inputs[0].default_value=1
    material.node_tree.links.new(node.outputs['Alpha'],invert.inputs[1])
    material.node_tree.links.new(invert.outputs[0],nodes.get('Principled BSDF').inputs['Roughness'])
    relief_image=bpy.data.images.load(str(normal))
    relief_image.colorspace_settings.name='Non-Color'
    relief_image.pack()
    relief_texture=nodes.new('ShaderNodeTexImage')
    relief_texture.image=relief_image
    relief_node=nodes.new('ShaderNodeNormalMap')
    material.node_tree.links.new(relief_texture.outputs['Color'],relief_node.inputs['Color'])
    material.node_tree.links.new(relief_node.outputs['Normal'],nodes.get('Principled BSDF').inputs['Normal'])
    for group, entries in model.meshes.items():
        mesh_object(root,'GEO_'+group,entries,material)
    for name,(center,size) in model.boxes.items():
        obj=mesh_object(root,'COL_'+name,[(None,kit.box((0,0,0),size))])
        x,y,z=center
        obj.location=(x,z,y)
        obj.display_type='WIRE'
        obj.hide_render=True
    for name,position in LIGHT_ANCHORS.items():
        anchor=bpy.data.objects.new('ANCHOR_'+name,None)
        bpy.context.collection.objects.link(anchor)
        anchor.parent=root
        x,y,z=position
        anchor.location=(x,z,y)
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'EMPTY','MESH'},
        axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',bake_space_transform=False,
        add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE',path_mode='STRIP')
    for obj in root.children_recursive:
        if obj.name.startswith('COL_'):
            obj.hide_set(True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
    record.write_text(text)
    check_export(fbx,model.boxes)
    print(f'Authored {triangles} visible triangles and {len(model.boxes)} collision helpers.')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model-dir',type=Path,default=MODEL_DIR)
    parser.add_argument('--source-dir',type=Path,default=SOURCE_DIR)
    parser.add_argument('--validate-only',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    generate(args.model_dir,args.source_dir,args.validate_only)
