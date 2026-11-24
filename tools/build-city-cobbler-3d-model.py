#!/usr/bin/env python3
"""Build the enterable retired midibus workshop, in Unity metres (+Z windscreen, +X passenger door)."""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import zlib

sys.dont_write_bytecode = True
import bpy
import numpy as np
from mathutils import Vector

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
OUT = ROOT / "Assets/Resources/City/Cobbler"
SOURCE = ROOT / "ArtSource/City/Cobbler"
spec = importlib.util.spec_from_file_location("cobbler_port", TOOLS / "build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
from city_building_coplanarity import _convex_intersection_area

# Each small atlas region carries the pigment and smoothness of a physical surface.
SWATCH = {
    "paint": (101, 130, 124, .29), "faded": (139, 157, 140, .27),
    "cream": (180, 177, 149, .27), "lining": (94, 103, 96, .18),
    "rubber": (49, 53, 50, .22), "iron": (75, 81, 77, .36),
    "zinc": (131, 137, 126, .47), "wear": (113, 96, 77, .21),
    "wood": (124, 102, 77, .24), "last": (163, 136, 99, .30),
    "leather": (91, 66, 52, .30), "oxblood": (111, 72, 62, .35),
    "sole": (63, 55, 46, .20), "brass": (163, 145, 104, .67),
    "shade": (74, 95, 84, .38), "cloth": (131, 125, 106, .16),
    "concrete": (139, 137, 122, .16), "straw": (150, 130, 91, .18),
    "red": (127, 69, 57, .34), "headlight": (161, 161, 139, .43),
    "lens": (244, 212, 156, .32), "glass": (181, 202, 193, .68),
    "signboard": (65, 79, 74, .13), "lettering": (206, 197, 162, .13),
    "lino": (104,108,93,.28), "mat": (64,65,56,.21), "worktop": (135,108,76,.30),
    "handle": (139,99,63,.43), "cut": (113,83,58,.23), "vinyl": (91,73,62,.34),
    "endgrain": (142,114,81,.25), "boot": (112,76,57,.39),
}
COLOR = {k: tuple(x / 255 for x in a[:3]) + (1,) for k, a in SWATCH.items()}
ANCHORS = {
    "Feet": (-.47, .48, 2.48), "Seat": (-.47, .92, 2.30), "AwlRest": (-.10, 1.215, 3.51), "Mug": (-.94, 1.10, .50),
    "Workbench": (-.46, 1.19, 3.32),
    "ShoeRest": (-.45, 1.30, 3.04),
    "HandRight": (-.202, 1.342, 3.095), "Lamp": (-.82, 1.84, 3.56),
    "LampAim": (-.44, 1.30, 3.04), "EntryOutside": (2.33, 0, 2.47),
    "SignLamp": (1.62, 2.99, 2.46), "SignAim": (1.242, 2.722, 2.47),
    "Step1": (2.07, .16, 2.47), "Step2": (1.71, .32, 2.47), "Threshold": (1.32, .48, 2.47),
    "EntryInside": (.53, .48, 2.47), "Lunch": (-.52, .48, .34),
}
BODY_SIZE = (2.38, 2.68, 8.25)
LAMP_SWITCH = (-.77, 1.215, 2.98)


# The lower half provides protected, proportioned charts for the floor and the hands' work area.
ATLAS_SIZE = 1024
CHART = {key: ((i%8)*128,(i//8)*128,128,128) for i,key in enumerate(SWATCH)}
CHART.update(lino=(0,512,1024,288),worktop=(0,800,416,224),mat=(416,800,296,224),boot=(712,800,312,224))
PAD = 4


def atlas_bytes():
    px = np.zeros((ATLAS_SIZE,ATLAS_SIZE,4),dtype=np.uint8)
    for i,(key,(r,g,b,gloss)) in enumerate(SWATCH.items()):
        x0,y0,w,h = CHART[key]
        rows,cols = h-2*PAD,w-2*PAD
        v,u = np.mgrid[0:rows,0:cols]
        u,v = u/(cols-1),v/(rows-1)
        rng = np.random.default_rng(5281+i)
        grain = rng.normal(0,1.5,u.shape)
        variation = 1.8*np.sin(17*u+3*np.sin(9*v))+1.7*np.cos(14*v-4*u)
        polish = np.zeros_like(u)
        if key in ("wood","last","worktop","handle","straw","endgrain"):
            fibres = 2*np.sin(5*u)+1.3*np.sin(11*u+4*v)
            phase = 85*np.hypot(.82*(u-.16),v-.44) if key=="endgrain" else 114*v+fibres
            variation += 4*np.sin(phase)+2*np.sin(2.8*phase)+1.8*np.sin(9*v+fibres)
            if key=="worktop":
                board = np.abs(v-.52-.001*np.sin(11*u))
                variation += np.where(v>.52,2,-2)-18*np.exp(-(board/.005)**2)
                for _ in range(55):
                    x,y=rng.uniform(.09,.68),rng.uniform(.07,.72)
                    scratch=((v-y)-.27*(u-x))/.0023
                    variation -= 8*np.exp(-scratch**2-((u-x)/.021)**2)
            if key=="handle":
                polish = .17*np.exp(-((u-.48)/.35)**2)
        elif key in ("boot","leather","oxblood","vinyl"):
            pores = np.maximum(0,-grain-1.3)
            variation += grain-2.8*pores
            fold = 105*u+5*np.sin(v*5)+1.2*np.sin(v*12)
            bending = np.exp(-((u-.69)/.18)**2)
            variation -= 9*bending*np.maximum(0,np.cos(fold))**9
            polish = .12*bending-.028*pores
        elif key=="cut":
            variation += 4.2*np.sin(152*v+1.7*np.sin(11*u))+grain
        elif key=="mat":
            variation += 1.9*np.sin(170*v)+grain
            for _ in range(95):
                x,y=rng.normal(.58,.17),rng.normal(.42,.17)
                nick=np.exp(-((u-x)/.005)**2-((v-y)/.009)**2)
                variation -= 24*nick
                polish -= .09*nick
        elif key=="lino":
            # +X is the passenger door, near +Z. Shoes polish the turn into the centre aisle.
            path = np.exp(-((v-.52)/.18)**2)*np.exp(-((u-.48)/.37)**4)
            entry = np.exp(-((u-.803)/.075)**2-((v-.80)/.27)**2)
            traffic = np.minimum(1,path+entry)
            variation += grain+7*traffic+2*np.sin(110*u+8*np.sin(13*v))
            for joint in (.29,.62,.805):
                variation -= 13*np.exp(-((u-joint)/.0016)**2)
            polish = .15*traffic
        elif key in ("iron","zinc","brass"):
            variation += .8*np.sin(185*v+.8*np.sin(9*u))
            polish += .04*np.cos(19*v)
        elif key in ("paint","cream","faded"):
            variation += 3*np.maximum(0,1-5*u)-9*np.maximum(0,.3-v)
        colour = np.array((r,g,b)) + (variation+grain)[:,:,None]
        response=np.clip((gloss+polish)*255-.5*variation,0,255)
        tile=np.dstack((np.clip(colour,0,255),response)).astype(np.uint8)
        px[y0:y0+h,x0:x0+w]=np.pad(tile,((PAD,PAD),(PAD,PAD),(0,0)),mode="edge")

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", ATLAS_SIZE, ATLAS_SIZE, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(b"".join(b"\0" + row.tobytes() for row in px), 9)) + chunk(b"IEND", b""))


class Parts:
    def __init__(self, root, material, role="Paint"):
        self.root, self.material, self.role = root, material, role
        self.g = port.Geometry()
        self.charts = {}

    def box(self, swatch, p, dims, bevel=0):
        if bevel:
            port.chamfer(self.g, p, dims, COLOR[swatch], bevel)
        else:
            self.g.box(p, dims, COLOR[swatch])

    def rod(self, swatch, a, b, radius, sides=8, end_radius=None):
        self.g.rod(a, b, radius, COLOR[swatch], sides, end_radius=end_radius)

    def rings(self, swatch, rings):
        count = len(rings[0])
        verts = [p for loop in rings for p in loop]
        faces = [tuple(reversed(range(count))), tuple(range(len(verts)-count, len(verts)))]
        for i in range(len(rings)-1):
            for j in range(count):
                a, b = i*count+j, i*count+(j+1)%count
                faces.append((a, b, b+count, a+count))
        self.g.add(verts, faces, COLOR[swatch])

    def map_since(self, first, projection):
        for face in range(first,len(self.g.faces)):
            self.charts[face]=projection

    def seal(self):
        o = self.g.object(self.root.name + "__" + self.role, self.root, self.material)
        keys = {value:key for key,value in COLOR.items()}
        layer = o.data.uv_layers.active.data
        for poly,color in zip(o.data.polygons,self.g.colors):
            key=keys[color]
            points=[Vector(port.source(o.data.vertices[i].co)) for i in poly.vertices]
            if poly.index in self.charts:
                coords=[self.charts[poly.index](p) for p in points]
            elif key=="lino":
                coords=[((p.z+3.99)/7.98,(p.x+1.15)/2.30) for p in points]
            else:
                axes=sorted(range(3),key=lambda a:max(p[a] for p in points)-min(p[a] for p in points),reverse=True)[:2]
                # Use one physical scale on small facets. Long timbers take their grain along the member.
                coords=[]
                for axis,pitch in zip(axes,(.86,.36)):
                    lo=min(p[axis] for p in points)
                    span=max(p[axis] for p in points)-lo
                    offset=((lo*.71)%1)*max(0,1-span/pitch)
                    coords.append([offset+(p[axis]-lo)/max(pitch,span) for p in points])
                coords=list(zip(*coords))
            x,y,w,h=CHART[key]
            for idx,(u,v) in zip(poly.loop_indices,coords):
                u,v=(max(0,min(1,t)) for t in (u,v))
                layer[idx].uv=((x+PAD+.5+u*(w-2*PAD-1))/ATLAS_SIZE,
                               1-(y+PAD+.5+v*(h-2*PAD-1))/ATLAS_SIZE)
        return o


def collider(root, material, name, center, size):
    # The runtime reads mesh bounds into a BoxCollider and hides the helper renderer.
    g = port.Geometry()
    g.box((0, 0, 0), size, COLOR["iron"])
    obj = g.object("COL_" + name, root, material)
    obj.location = port.source(center)
    obj.display_type = "WIRE"
    obj.hide_render = True


def skin(root, m, glass, material):
    # Broad shoulders and rolled crowns connect the original midibus glazing and ends.
    def deck(swatch, stations):
        rings = []
        for z, half, bottom, crown in stations:
            rings.append([(-half,bottom,z), (-half,crown-.10,z),(-half+.10,crown-.02,z),
                          (-half*.60,crown,z),(half*.60,crown,z),(half-.10,crown-.02,z),
                          (half,crown-.10,z), (half,bottom,z)])
        m.rings(swatch, rings)
    deck("faded", [(-4.07,.99,2.67,2.82),(-3.91,1.14,2.64,2.95),(-3.5,1.19,2.62,3.03),
                   (0,1.19,2.62,3.05),(3.54,1.19,2.62,3.03),(3.93,1.13,2.62,2.94),(4.02,1.01,2.64,2.80)])
    # The lino is the upper face of the structural floor itself. A separate skin
    # on the same 0.48 m plane flickered when seen along the length of the cabin.
    first_floor_face = len(m.g.faces)
    m.box("rubber", (0,.42,0), (2.30,.12,7.98), .035)
    for i in range(first_floor_face, len(m.g.faces)):
        if all(abs(port.source(m.g.vertices[k])[1] - .48) < 1e-5 for k in m.g.faces[i]):
            m.g.colors[i] = COLOR["lino"]
    collider(root, material, "Floor", (0,.425,0), (2.30,.11,8.00))
    collider(root, material, "Roof", (0,2.78,0), (2.31,.27,8.02))

    for sign in (-1,1):
        end = 4.03*sign
        deck("paint", [(end-sign*.24,1.16,.50,1.11), (end,1.09,.53,1.11)])
        m.box("rubber", (0,.55,sign*4.075), (2.20,.14,.10), .036)
        upper_width = .97
        # Large raked windshield reveals the entire tabletop below the worker's chin.
        zb, zt = sign*4.012, sign*3.825
        low, high = (1.105,2.65) if sign>0 else (1.27,2.65)
        if sign<0:
            m.box("cream", (0,1.185,-3.985), (2.22,.17,.07), .025)
        pts = [(-1.07,low,zb),(-upper_width,high,zt),(upper_width,high,zt),(1.07,low,zb)]
        glass.rings("glass", [pts, [(x,y,z-sign*.007) for x,y,z in pts]])
        for i in range(4):
            m.rod("rubber", pts[i], pts[(i+1)%4], .038, 8)
        for sx in (-1,1):
            m.rod("cream", (sx*1.115,low-.02,zb-sign*.06),(sx*1.045,high+.015,zt-sign*.02), .065, 7)
            m.box("iron", (sx*.86,.865,sign*4.023), (.30,.235,.049), .042)
            m.box("headlight" if sign>0 else "red", (sx*.86,.865,sign*4.056), (.23,.164,.026), .020)
        if sign>0:
            for x in (-.40,.39):
                m.rod("iron", (x-.13,1.106,4.038),(x+.165,1.297,4.015), .012, 6)
                m.rod("rubber", (x-.04,1.268,4.018),(x+.40,1.333,4.011), .014, 6)
            for i in range(5):
                m.box("iron", (0,.76+i*.045,4.042), (.58,.018,.042))
        collider(root, material, "EndFront" if sign>0 else "EndRear", (0,1.57,sign*4.0),(2.28,2.18,.06))

    for side in (-1,1):
        sx = side*1.152
        spans = [(-3.98,3.98)] if side<0 else [(-3.98,1.82),(3.12,3.98)]
        for z0,z1 in spans:
            # Each arched wheel opening is cut into the bottom edge of the original panels.
            cuts = [z0,z1]
            for hub in (-2.49,2.44):
                cuts += [hub+t for t in (-.61,-.50,-.32,0,.32,.50,.61) if z0<hub+t<z1]
            def edge(z):
                t = min(abs(z+2.49), abs(z-2.44))
                return .45 + .51*math.sqrt(max(0,1-(t/.61)**2))
            for za,zb in zip(sorted(cuts), sorted(cuts)[1:]):
                m.rings("paint", [[(sx+side*dx,y,z) for z,y in [(za,edge(za)),(zb,edge(zb)),(zb,1.29),(za,1.29)]]
                                 for dx in (-.038,.028)])
            m.box("cream", (sx,1.34,(z0+z1)/2), (.068,.12,z1-z0))
            m.box("iron", (sx+side*.027,1.255,(z0+z1)/2), (.021,.025,z1-z0))
            m.box("faded", (sx,2.59,(z0+z1)/2), (.068,.10,z1-z0))
            points = [z0] + [z for z in (-3.65,-2.37,-1.07,.26,1.60,2.86,3.73) if z0+.12<z<z1-.12] + [z1]
            for z in points:
                m.box("cream", (sx,1.96,z),(.072,1.24,.065),.009)
            for a,b in zip(points,points[1:]):
                if b-a < .18: continue
                glass.box("glass",(sx,1.967,(a+b)*.5),(.008,1.13,b-a-.074))
                m.box("rubber",(sx,1.414,(a+b)*.5),(.042,.029,b-a-.052))
                m.box("rubber",(sx,2.517,(a+b)*.5),(.042,.029,b-a-.052))
                if a<1: m.box("iron",(sx+side*.011,2.264,(a+b)*.5),(.025,.032,b-a-.05))
            # Weathered panel repairs and small patches of exposed primer at the old rub rail.
            for n in range(max(1, int((z1-z0)/.52))):
                z = z0+.29+n*.52
                if z>z1-.09:continue
                m.box("wear" if n%3 else "faded", (sx+side*.031,1.03+.055*(n%3),z), (.006,.016+.009*(n%2),.08+.055*(n%4)))
        if side<0:
            collider(root,material,"SideLeft",(-1.1525,1.58,0),(.075,2.20,8.06))
        else:
            for name,z0,z1 in [("SideRightFront",3.12,4.03),("SideRightRear",-4.03,1.82)]:
                collider(root,material,name,(1.1525,1.58,(z0+z1)*.5),(.075,2.20,z1-z0))


def shoe_point(origin, angle, scale):
    yaw = math.radians(angle)
    right, forward = Vector((math.cos(yaw),0,-math.sin(yaw))), Vector((math.sin(yaw),0,math.cos(yaw)))
    def point(x,y,z):
        return Vector(origin) + scale * (x*right + Vector((0,y,0)) + z*forward)
    return point


# Toe extends along +Z. Stations wrap the heel, narrow waist and asymmetric toe.
SHOE_OUTLINE = ((-.035,-.139),(.032,-.139),(.048,-.115),(.047,-.048),
                (.055,.038),(.058,.078),(.047,.130),(.022,.152),
                (-.023,.150),(-.049,.118),(-.063,.063),(-.052,.006),(-.048,-.108))
WORK_BOOT = ((-.45,1.214,3.045),74,1.12)
work_point = shoe_point(*WORK_BOOT)
WELT_POINT = work_point(SHOE_OUTLINE[8][0]*1.03,.029,SHOE_OUTLINE[8][1])
AWL_POINT = WELT_POINT - Vector(ANCHORS["HandRight"])
ANCHORS["HandLeft"] = tuple(round(v,5) for v in work_point(.033,.137,-.097))


def shoe(m, origin, angle, scale, upper="leather", work=False):
    point = shoe_point(origin,angle,scale)
    first_face=len(m.g.faces)
    right=point(1,0,0)-point(0,0,0)
    forward=point(0,0,1)-point(0,0,0)
    def loop(width, length, shift, height):
        return [point(x*width,height,z*length+shift) for x,z in SHOE_OUTLINE]

    heel = [(-.039,-.137),(.037,-.137),(.046,-.111),(.041,-.060),(-.046,-.061),(-.046,-.111)]
    m.rings("sole",[[point(x*k,y,z) for x,z in heel] for k,y in ((.93,0),(1,.004),(1,.015))])
    # The arch is lifted clear of the mat between the heel block and the forepart.
    bottom = [point(x,.001 if z>.025 else .013,z) for x,z in SHOE_OUTLINE]
    m.rings("sole",[bottom,loop(1.04,1.01,0,.020)])
    m.rings("cut",[loop(1.04,1.01,0,.020),loop(1.03,1,0,.029),loop(.99,1,0,.031)])
    height = .149 if work else .098
    # Turn the leather inward at the rim and descend into the opening.
    first = len(m.g.faces)
    m.rings(upper,[loop(.99,1,0,.031),loop(1,.96,-.001,.049),loop(.96,.86,-.010,.064),
                   loop(.89,.56,-.042,.089),loop(.81,.35,-.074,height-.008),
                   loop(.80,.34,-.074,height),loop(.69,.29,-.074,height-.003),
                   loop(.62,.265,-.072,height-.023),loop(.55,.23,-.071,height-.035)])
    m.g.colors[first+1] = COLOR["sole"]
    for face in range(len(m.g.faces)-len(SHOE_OUTLINE),len(m.g.faces)):
        m.g.colors[face] = COLOR["sole"]
    # Tongue runs from the vamp up the instep. Eyelets flank the actual lace path.
    tongue = [(.106,.074),(.047,.091),(.016,(.089+height-.008)/2+.002),(-.016,height+.002)]
    m.rings(upper, [[point(-w,y-.003,z),point(w,y-.003,z),point(w,y,z+.002),point(-w,y,z+.002)]
                    for (z,y),w in zip(tongue,(.025,.029,.024,.022))])
    for n,(z,y) in enumerate(tongue):
        for side in (-1,1):
            end = point(side*.027,y+.002,z)
            if work:
                m.rod("brass",point(side*.027,y-.001,z),end,.005*scale,8)
            if n+1 < len(tongue):
                z2,y2 = tongue[n+1]
                crown = point(0,(y+y2)*.5+.006+side*.001,(z+z2)*.5+.004)
                m.rod("cloth",end,crown,.0024*scale,5)
                m.rod("cloth",crown,point(-side*.027,y2+.003,z2),.0024*scale,5)
    if work:
        for i,(x,z) in enumerate(SHOE_OUTLINE):
            nx,nz = SHOE_OUTLINE[(i+1)%len(SHOE_OUTLINE)]
            for t in (.16,.64):
                m.rod("cloth",point(1.025*(x+(nx-x)*t),.030,z+(nz-z)*t),
                      point(1.025*(x+(nx-x)*(t+.20)),.030,z+(nz-z)*(t+.20)),.0013*scale,5)

    def shoe_uv(p):
        d=(p-Vector(origin))/scale
        return (.49+d.dot(forward)/(.31*scale),.12+d.y*4.2+d.dot(right)/(scale*.40))
    m.map_since(first_face,shoe_uv)


def hanging_last(m, x, z, side, index):
    size = (.83,.98,.89,1.03)[index%4]
    point = shoe_point((x,1.925,z),side*(12+7*(index%3)),size)
    toe_width = (.049,.057,.041)[index%3]
    stations = ((-.132,.035,.077),(-.097,.049,.121),(-.052,.050,.135),
                (.018,.047,.086),(.084,toe_width,.054),(.142,toe_width*.68,.036))
    m.rings("last",[[point(xx,yy,zz) for xx,yy in
             ((-w*.8,.012),(-w,.022),(-w*.88,h*.65),(-w*.48,h),(w*.48,h),
              (w*.88,h*.65),(w,.022),(w*.8,.012))] for zz,w,h in stations])
    eye = point(0,.135,-.052)
    rail = Vector((x,2.345,z))
    for a,b in ((eye+Vector((-.008,-.002,0)),rail+Vector((-.008,.009,0))),
                (rail+Vector((-.008,.009,0)),rail+Vector((.008,.009,0))),
                (rail+Vector((.008,.009,0)),eye+Vector((.008,-.002,0)))):
        m.rod("cloth",a,b,.0038,5)
    m.rod("brass",eye-Vector((0,.006,0)),eye,.012,8)


def interior(root, m, mat):
    # Footfall changes at the old lino; the work surface and shoes occupy the bright front third.
    first=len(m.g.faces)
    m.box("worktop",(-.43,1.155,3.395),(1.24,.07,.97),.018)
    m.map_since(first,lambda p: ((p.x+1.05)/1.24,(p.z-2.91)/.97))
    for x in (-.97,.10):
        m.box("wood",(x,1.093,3.386),(.052,.073,.812),.006)
        m.box("iron",(x,.689,3.386),(.023,.037,.756),.004)
    for z in (3.00,3.76):
        m.box("wood",(-.437,1.087,z),(1.025,.08,.048),.006)
    for x in (-.97,.10):
        for z in (3.00,3.76): m.box("iron",(x,.805,z),(.045,.65,.045),.003)
    collider(root,mat,"Workbench",(-.43,.836,3.395),(1.24,.712,.97))
    first=len(m.g.faces)
    m.box("mat",(-.45,1.202,3.09),(.46,.023,.29),.008)
    m.map_since(first,lambda p: ((p.x+.68)/.46,(p.z-2.945)/.29))
    shoe(m,*WORK_BOOT,upper="boot",work=True)
    port.empty("ANCHOR_Welt",root,WELT_POINT)
    port.empty("ANCHOR_CollarInside",root,work_point(-.018,.137,-.097))
    for i in range(3):
        x=-.86+i*.09
        m.rod("wood",(x,1.214,3.44),(x+.028,1.23,3.58),.017,7)
    m.rod("iron",(-.03,1.205,3.49),(.03,1.208,3.68),.009,6)
    m.rod("iron",(.00,1.211,3.64),(.09,1.211,3.63),.018,6)
    m.box("last",(-.47,.887,2.29),(.43,.066,.38),.024)
    for x in (-.63,-.31):
        for z in (2.14,2.44):
            m.rod("iron",(x,.49,z),(x,.86,z),.016)
    m.rod("iron",(-.64,.86,2.12),(-.64,1.21,2.06),.018)
    m.rod("iron",(-.30,.86,2.12),(-.30,1.21,2.06),.018)
    m.box("wood",(-.47,1.19,2.075),(.405,.105,.049),.012)
    for x in (-.63,-.31):
        m.rod("iron",(x,.60,2.14),(x,.60,2.44),.009,7)
        for z in (2.14,2.44):
            m.rod("rubber",(x,.481,z),(x,.505,z),.022,8,.019)
        m.rod("zinc",(x,1.19,2.045),(x,1.19,2.103),.009,8)
    collider(root,mat,"Chair",(-.47,.70,2.27),(.40,.44,.34))

    for side in (-1,1):
        for i,z in enumerate((-2.97,-1.60,-.26,1.00)):
            x=side*.96
            m.rod("iron",(x,2.345,z-.42),(x,2.345,z+.44),.012)
            for end in (z-.395,z+.415):
                m.rod("iron",(x,2.345,end),(x,2.68,end),.007,6)
                m.rod("iron",(x,2.345,end-.012),(x,2.345,end+.012),.017,8)
            for n in range(2 if i%2==0 else 1):
                at=z-.23+n*.29
                hanging_last(m,x,at,side,i+n)
            if i%2==0 or side<0:
                m.box("wood",(side*.94,1.444,z),(.30,.035,.96),.008)
                for station in (z-.33,z+.33):
                    m.rod("iron",(side*1.111,1.288,station),(side*.817,1.421,station),.011,7)
                    m.box("iron",(side*1.105,1.34,station),(.021,.164,.033),.003)
                    m.box("iron",(side*.965,1.418,station),(.30,.015,.032),.003)
                    m.rod("zinc",(side*1.084,1.277,station),(side*1.097,1.277,station),.008,7)
                for edge in (z-.471,z+.471):
                    m.box("endgrain",(side*.94,1.455,edge),(.297,.042,.016),.004)
                for n in range(2):
                    shoe(m,(side*.92,1.463,z-.20+n*.28),side*(30+n*6),.94,"oxblood" if i==2 else "leather")
    for y in (.72, .99):
        m.box("wood",(-.89,y,-2.61),(.40,.041,2.22),.008)
    for z in (-3.68,-1.54):
        m.box("iron",(-.96,.75,z),(.045,.52,.045))
    collider(root,mat,"Rack",(-.885,.75,-2.61),(.41,.54,2.22))
    m.box("wood",(-.52,.891,-.14),(.59,.058,.36),.017)
    for x in (-.74,-.3):m.box("iron",(x,.68,-.14),(.05,.40,.24))
    collider(root,mat,"LunchBench",(-.52,.70,-.14),(.57,.44,.36))
    # Enamel mug and closed lunch tin beside the bench leave the seat and route open.
    m.box("wood",(-.94,1.04,.36),(.30,.038,.45),.009)
    m.rod("cream",(-.94,1.06,.50),(-.94,1.161,.50),.039,9)
    m.box("zinc",(-.96,1.091,.25),(.124,.058,.162),.013)

    m.rod("shade",(-.84,1.19,3.67),(-.84,1.22,3.67),.106,12)
    m.rod("iron",(-.84,1.22,3.67),(-.87,1.68,3.76),.022)
    m.rod("brass",(-.87,1.68,3.76),(-.86,1.96,3.60),.018)
    for x,y,z in ((-.84,1.235,3.67),(-.87,1.68,3.76),(-.86,1.957,3.6)):
        m.rod("shade",(x-.027,y,z),(x+.026,y,z),.034,10)
        m.rod("brass",(x+.026,y,z),(x+.034,y,z),.015,8)
    source=Vector(ANCHORS["Lamp"]); target=Vector(ANCHORS["LampAim"])
    direction=(target-source).normalized()
    m.rod("shade",source-direction*.11,source,.040,12,.139)
    m.rod("shade",source-direction*.011,source+direction*.003,.141,16,.144)
    lens=Parts(root,mat,"Lens")
    lens.rod("lens",source,source+direction*.009,.122,14)
    lens.seal()
    # A spring-return button at the nearest end of the flexible cord. Its contact
    # belongs to the moving cap, while the ceramic switch body stays on the wood.
    x, y, z = LAMP_SWITCH
    m.box("cream", (x, 1.198, z), (.083, .016, .100), .007)
    m.rod("iron",(x,1.205,z),(x,1.208,z),.024,12)
    for dz in (-.034,.034):
        m.rod("zinc",(x,1.205,z+dz),(x,1.208,z+dz),.0045,7)
    cord = [(x,1.198,z+.040),(-.82,1.195,3.059),(-.922,1.195,3.112),
            (-.939,1.195,3.309),(-.892,1.195,3.535),(-.85,1.198,3.624)]
    for a,b in zip(cord,cord[1:]):
        m.rod("rubber",a,b,.004,6)
    switch=Parts(port.empty("MOVE_LampSwitch",root,LAMP_SWITCH),mat)
    switch.rod("brass",(0,-.012,0),(0,0,0),.018,10)
    switch.seal()
    port.empty("ANCHOR_LampSwitch",switch.root)
    awl=Parts(port.empty("PIVOT_Awl",root,ANCHORS["HandRight"]),mat)
    # A turned pear handle ends at a brass ferrule; the steel tapers to the actual welt.
    axis = AWL_POINT.normalized()
    handle = ((-.041,.011),(-.036,.020),(-.019,.023),(.005,.022),(.030,.017),(.042,.012))
    for (d,r),(dn,rn) in zip(handle,handle[1:]):
        awl.rod("handle",axis*d,axis*dn,r,12,rn)
    awl.map_since(0,lambda p: ((p.dot(axis)+.041)/.083,.5+p.z/.057))
    awl.rod("brass",axis*.042,axis*.056,.013,12,.010)
    awl.rod("zinc",axis*.056,axis*.080,.005,8,.0035)
    awl.rod("zinc",axis*.080,AWL_POINT,.0035,8,.00045)
    port.empty("ANCHOR_AwlTip",awl.root,AWL_POINT)
    awl.seal()
    broom=Parts(port.empty("PIVOT_Broom",root,(.89,1.18,-3.39)),mat)
    broom.rod("wood",(.04,.51,-.05),(-.046,-.52,.05),.016)
    broom.rod("straw",(-.046,-.50,.05),(-.058,-.67,.065),.048,7,.088)
    broom.rod("iron",(-.046,-.51,.05),(-.049,-.55,.053),.048,7)
    broom.seal()


def access(root,m,mat):
    for tag, x, width, height in (("Tread1",2.065,.35,.16),("Tread2",1.715,.35,.32),("Landing",1.34,.40,.48)):
        m.box("concrete",(x,height*.5,2.47),(width,height,1.25),.012)
        edge = x+width*.5-.016
        m.box("zinc",(edge,height+.002,2.47),(.029,.006,1.19),.002)
        m.box("iron",(edge+.014,height-.012,2.47),(.004,.023,1.18),.0015)
        for offset in (.041,.067):
            m.box("rubber",(edge-offset,height+.002,2.47),(.010,.004,1.15),.0015)
        for z in (1.932,2.47,3.008):
            m.rod("iron",(edge,height+.005,z),(edge,height+.0065,z),.006,7)
        collider(root,mat,tag,(x,height*.5,2.47),(width,height,1.25))
    for z in (1.797,3.143):
        m.box("cream",(1.148,1.46,z),(.075,1.96,.046),.008)
        m.box("rubber",(1.185,1.47,z),(.019,1.92,.027),.006)
        for y in (.63,1.50,2.32):
            m.rod("zinc",(1.184,y,z),(1.20,y,z),.010,8)
    m.box("rubber",(1.15,2.485,2.47),(.078,.09,1.38),.008)
    pivot=port.empty("PIVOT_Door",root,(1.15,1.46,2.47))
    leaf=Parts(pivot,mat)
    for z in (-.616,.616):
        leaf.box("paint",(0,0,z),(.051,1.95,.063),.007)
    leaf.box("paint",(0,-.641,0),(.051,.674,1.24),.014)
    leaf.box("cream",(0,.907,0),(.052,.139,1.24),.008)
    leaf.box("iron",(0,-.276,0),(.042,.043,1.24))
    for z in (.37,.54):
        leaf.rod("iron",(.021,-.157,z),(.093,-.157,z),.019)
    leaf.rod("brass",(.093,-.157,.37),(.093,-.157,.54),.025)
    for z in (-.51,.51):
        leaf.box("iron",(.029,-.775,z),(.010,.233,.09),.004)
        leaf.rod("zinc",(.034,-.775,z),(.041,-.775,z),.012,7)
    leaf.seal()
    glazing=Parts(pivot,mat,"Glass")
    glazing.box("glass",(0,.268,0),(.008,1.12,1.165))
    glazing.seal()
    collider(pivot,mat,"Door",(0,0,0),(.052,1.95,1.29))
    for ax, sx in [("L",-.78),("R",.78)]:
        for az,sz in [("F",2.62),("R",-2.62)]:
            key=az+ax
            support=port.empty("Support_"+key,root,(sx,.37,sz))
            blocks=Parts(support,mat)
            # The load plate sits on top of the pier. Its upper face is exposed
            # and the concrete stops at the plate's underside.
            blocks.box("concrete",(0,-.215,0),(.53,.37,.70),.027)
            blocks.box("iron",(0,-.015,0),(.41,.03,.47),.005)
            blocks.seal()
            collider(support,mat,"Support_"+key,(0,-.20,0),(.53,.40,.70))


def workshop_sign(root, mat):
    # A retired bus has no illuminated destination box here. Two mounting straps
    # hold a separately hand-lettered sheet above the actual passenger doorway.
    panel = Parts(port.empty("SignPanel", root), mat)
    for z in (1.68, 3.26):
        panel.box("iron", (1.173, 2.72, z), (.075, .32, .055), .006)
    panel.box("signboard", (1.214, 2.722, 2.47), (.050, .36, 1.98), .010)
    for z in (1.55, 3.39):
        for y in (2.585, 2.858):
            panel.rod("zinc", (1.239, y, z), (1.245, y, z), .011, 8)
    panel.seal()

    text = "РЕМОНТ ОБУВИ"
    curve = bpy.data.curves.new("Cobbler painted Cyrillic", "FONT")
    curve.body, curve.size, curve.extrude, curve.resolution_u = text, 1, .004, 3
    curve.font = bpy.data.fonts.load(str(ROOT / "Assets/Resources/Fonts/Roboto-Regular.ttf"), check_existing=True)
    obj = bpy.data.objects.new("Letter tracing", curve)
    bpy.context.collection.objects.link(obj)
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    coords = [v.co for v in mesh.vertices]
    x0, x1 = min(p.x for p in coords), max(p.x for p in coords)
    y0, y1 = min(p.y for p in coords), max(p.y for p in coords)
    letters = Parts(port.empty("SignLetters", root), mat)
    # Reader looks towards local -X, so text advances along +Z. The restrained
    # broad brush varies at the baseline without closing a glyph's counters.
    points = []
    for p in coords:
        u, v = (p.x-x0)/(x1-x0), (p.y-y0)/(y1-y0)
        baseline = .0027*math.sin(19*u) + .0014*math.sin(51*u)
        points.append((1.242+p.z*.22, 2.610+.217*v+baseline, 1.612+1.716*u))
    faces = [tuple(reversed(face.vertices[:])) for face in mesh.polygons]
    letters.g.add(points, faces, COLOR["lettering"], solid=False)
    letters.seal()
    bpy.data.meshes.remove(mesh)
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.curves.remove(curve)

    lamp = Parts(port.empty("SignLight",root), mat)
    # A folded hood covers the tube on its road side. Only the underside facing
    # the painted lettering transmits light; the windshield has a dark end cap.
    for z in (1.91,3.01):
        lamp.rod("iron",(1.20,2.935,z),(1.60,3.002,z),.014,7)
    lamp.box("iron",(1.608,3.025,2.46),(.148,.043,1.46),.013)
    lamp.box("shade",(1.671,2.995,2.46),(.020,.065,1.46),.006)
    for z in (1.738,3.182):lamp.box("iron",(1.604,2.998,z),(.138,.067,.015))
    lamp.seal()
    lens=Parts(lamp.root,mat,"SignLens")
    lens.rod("lens",(1.618,2.993,1.775),(1.618,2.993,3.145),.010,8)
    lens.seal()


def check_lower_skin(root):
    # Coincident same-facing triangles compete for depth even when joined in a
    # single renderer. Test the authored horizontal skins including the feet of
    # the four piers. Opposing faces at a physical contact do not compete.
    layers = []
    for obj in root.children_recursive:
        if obj.type != "MESH" or not obj.name.endswith("__Paint"):
            continue
        matrix = root.matrix_world.inverted() @ obj.matrix_world
        for poly in obj.data.polygons:
            if abs(poly.normal.z) < .99999:
                continue
            vertices = [matrix @ obj.data.vertices[i].co for i in poly.vertices]
            level = vertices[0].z
            if not -.04 <= level <= .62:
                continue
            footprint = [(p.x, p.y) for p in vertices]
            upward = poly.normal.z > 0
            for old_level, old_up, old_foot, label in layers:
                if old_up == upward and abs(level - old_level) < .0005:
                    if _convex_intersection_area(footprint, old_foot, 1e-8) > .0005:
                        raise ValueError(f"Overlapping horizontal bus skins at Y={level:.4f}: "
                                         f"{obj.name} face {poly.index} / {label}")
            layers.append((level, upward, footprint, f"{obj.name} face {poly.index}"))


def generate():
    port.base.reset()
    mat=port.base.material("CobblerPalette")
    root=port.empty("CobblerBus")
    paint, glass = Parts(root,mat), Parts(root,mat,"Glass")
    skin(root,paint,glass,mat)
    access(root,paint,mat)
    interior(root,paint,mat)
    workshop_sign(root,mat)
    paint.seal()
    glass.seal()
    for name,p in ANCHORS.items():port.empty("ANCHOR_"+name,root,p)
    bpy.context.view_layer.update()
    # The first surface on the working stroke must be the exposed welt, including on withdrawal.
    hand = Vector(ANCHORS["HandRight"])
    hit, point, _, _ = bpy.data.objects["CobblerBus__Paint"].ray_cast(
        Vector(port.source(hand)), Vector(port.source(WELT_POINT-hand)).normalized(), distance=AWL_POINT.length+.004)
    if not hit or (point-Vector(port.source(WELT_POINT))).length > .003:
        raise ValueError("Shaft crosses the upper or misses the welt on its way to the stitch")
    check_lower_skin(root)
    model=port.describe(root)
    boxes=[]
    for o in sorted(root.children_recursive,key=lambda obj:obj.name):
        if not o.name.startswith("COL_"):continue
        verts=[port.source(o.matrix_world @ v.co) for v in o.data.vertices]
        lo,hi=([func(v[i] for v in verts) for i in range(3)] for func in (min,max))
        boxes.append({"name":o.name,"center":[round((a+b)/2,5) for a,b in zip(lo,hi)],
                      "size":[round(b-a,5) for a,b in zip(lo,hi)]})
    model.update({"coordinateSystem":"Unity metres, +Z nose, +X passenger side, Y=0 ground",
                  "bodySize":list(BODY_SIZE),"signText":"РЕМОНТ ОБУВИ","boxes":boxes})
    reported={a["name"]:a["position"] for a in model["anchors"]}
    for name,want in {**ANCHORS, "LampSwitch":LAMP_SWITCH}.items():
        if tuple(reported["ANCHOR_"+name])!=want: raise ValueError("Anchor mismatch: "+name)
    if bpy.data.objects["ANCHOR_LampSwitch"].parent.name != "MOVE_LampSwitch":
        raise ValueError("Switch contact must follow the depressed button")
    # Inspect the actual baked static mesh. Open the leaf; the two-metre passage
    # must not hit any fixed jamb, glazing or forgotten section of side panel.
    for z in (1.91,2.47,3.02):
        for y in (.66,1.35,2.31):
            for name in ("CobblerBus__Paint","CobblerBus__Glass"):
                obj=bpy.data.objects[name]
                hit=obj.ray_cast(Vector(port.source((.98,y,z))),Vector(port.source((1,0,0))),distance=.30)
                if hit[0]: raise ValueError(f"Sealed entrance at y={y}, z={z} in {name}")
    for x,y in ((-.65,1.42),(-.25,1.74),(.62,2.18)):
        origin=Vector(port.source((x,y,3.80))); direction=Vector(port.source((0,0,1)))
        if bpy.data.objects["CobblerBus__Paint"].ray_cast(origin,direction,distance=.40)[0]:
            raise ValueError("Opaque windshield at "+str((x,y)))
        if not bpy.data.objects["CobblerBus__Glass"].ray_cast(origin,direction,distance=.40)[0]:
            raise ValueError("Missing windshield at "+str((x,y)))
    bmin,bmax=model["bounds_min"],model["bounds_max"]
    for got,want in zip(bmin+bmax,[-1.19,-.03,-4.125,2.24,3.05,4.125]):
        if abs(got-want)>.003:raise ValueError("Unexpected exterior dimensions "+str((bmin,bmax)))
    if model["triangles"]>28000:raise ValueError("Cobbler workshop exceeds art budget")
    return root,model


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check","--validate-only",action="store_true")
    parser.add_argument("--model-dir",type=Path,default=OUT)
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    root,model=generate()
    atlas=atlas_bytes()
    model["atlasSHA256"]=hashlib.sha256(atlas).hexdigest()
    modeldir=args.model_dir
    contract=modeldir/"CobblerBus.json"
    diffuse=modeldir/"CobblerSurface.png"
    fbx=modeldir/"CobblerBus.fbx"
    blend=args.source_dir/"CobblerBus.blend"
    if args.check:
        expected=json.loads(contract.read_text())
        if model!=expected or diffuse.read_bytes()!=atlas or not fbx.is_file() or not blend.is_file():
            raise ValueError("Generated cobbler assets differ from the published contract")
    else:
        modeldir.mkdir(parents=True,exist_ok=True)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        diffuse.write_bytes(atlas)
        contract.write_text(json.dumps(model,indent=2)+"\n")
        port.base.export(root,fbx)
        for obj in root.children_recursive:
            if obj.name.startswith("COL_"):obj.hide_set(True)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
    print(f"CobblerBus: {model['triangles']} triangles, {len(model['boxes'])} collision boxes; checked entrance, windshield and anchors.",flush=True)


if __name__=="__main__":
    main()
