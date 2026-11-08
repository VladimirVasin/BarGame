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
}
COLOR = {k: tuple(x / 255 for x in a[:3]) + (1,) for k, a in SWATCH.items()}
ANCHORS = {
    "Feet": (-.47, .48, 2.48), "Seat": (-.47, .92, 2.30), "AwlRest": (-.10, 1.215, 3.51), "Mug": (-.94, 1.10, .50),
    "Workbench": (-.46, 1.19, 3.32),
    "ShoeRest": (-.45, 1.30, 3.04), "HandLeft": (-.60, 1.34, 3.00),
    "HandRight": (-.27, 1.37, 3.01), "Lamp": (-.82, 1.84, 3.56),
    "LampAim": (-.44, 1.30, 3.04), "EntryOutside": (2.33, 0, 2.47),
    "SignLamp": (1.62, 2.99, 2.46), "SignAim": (1.242, 2.722, 2.47),
    "Step1": (2.07, .16, 2.47), "Step2": (1.71, .32, 2.47), "Threshold": (1.32, .48, 2.47),
    "EntryInside": (.53, .48, 2.47), "Lunch": (-.52, .48, .34),
}
BODY_SIZE = (2.38, 2.68, 8.25)
LAMP_SWITCH = (-.77, 1.215, 2.98)


def atlas_bytes():
    cell = 64
    px = np.zeros((4 * cell, 8 * cell, 4), dtype=np.uint8)
    v, u = np.mgrid[0:cell, 0:cell] / cell
    for i, (key, (r, g, b, gloss)) in enumerate(SWATCH.items()):
        row, col = divmod(i, 8)
        rng = np.random.default_rng(5281 + i)
        grain = rng.normal(0, 2.8 if key == "concrete" else 1.4, u.shape)
        mottling = 2.6 * np.sin(15 * u + 2 * np.sin(9 * v)) + 2 * np.cos(19 * v - 3 * u)
        if key in ("paint", "cream", "faded"):
            mottling += np.maximum(0, 1 - 5 * u) * 3 - np.maximum(0, .3 - v) * 9
        elif key in ("wood", "last", "straw"):
            mottling += 4.7 * np.sin(72 * u + 3 * np.sin(5 * v + 2 * u))
        colour = np.array((r, g, b)) + (grain + mottling)[:, :, None]
        px[row*cell:(row+1)*cell, col*cell:(col+1)*cell] = np.dstack(
            (np.clip(colour, 0, 255), np.clip(gloss * 255 - mottling * .7, 0, 255))).astype(np.uint8)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8*cell, 4*cell, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(b"".join(b"\0" + row.tobytes() for row in px), 9)) + chunk(b"IEND", b""))


class Parts:
    def __init__(self, root, material, role="Paint"):
        self.root, self.material, self.role = root, material, role
        self.g = port.Geometry()

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

    def seal(self):
        o = self.g.object(self.root.name + "__" + self.role, self.root, self.material)
        tiles = {COLOR[k]: i for i, k in enumerate(SWATCH)}
        layer = o.data.uv_layers.active.data
        for poly, color in zip(o.data.polygons, self.g.colors):
            row, col = divmod(tiles[color], 8)
            axes = [i for i in range(3) if i != max(range(3), key=lambda j: abs(poly.normal[j]))]
            cords = [tuple(o.data.vertices[i].co[a] for a in axes) for i in poly.vertices]
            lo = [min(p[a] for p in cords) for a in range(2)]
            span = [max(.00001, max(p[a] for p in cords)-lo[a]) for a in range(2)]
            for idx, q in zip(poly.loop_indices, cords):
                u, v = [(.07 + .86 * (q[a]-lo[a])/span[a]) for a in range(2)]
                layer[idx].uv = ((col+u)/8, (3-row+v)/4)
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
            m.g.colors[i] = COLOR["lining"]
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


def shoe(m, p, angle, scale, upper="leather", last=False):
    x,y,z = p
    a = math.radians(angle)
    def profile(stations):
        rings=[]
        for zz,w,h,b in stations:
            cross=[(-w*.75,b),(-w,b+.007),(-w*.92,b+h*.48),(-w*.6,b+h*.90),(0,b+h),
                   (w*.6,b+h*.90),(w*.92,b+h*.48),(w,b+.007),(w*.75,b)]
            rings.append([(x+scale*(xx*math.cos(a)+zz*math.sin(a)),y+scale*hh,
                           z+scale*(zz*math.cos(a)-xx*math.sin(a))) for xx,hh in cross])
        return rings
    sections=[(-.135,.038,.084,.015),(-.095,.051,.106,.016),(-.035,.056,.095,.017),
              (.043,.057,.065,.018),(.105,.054,.042,.020),(.145,.036,.028,.023)]
    if not last:
        m.rings("sole",profile([(s,w+.003,.018,.003) for s,w,h,b in sections]))
    m.rings("last" if last else upper,profile(sections))
    if not last:
        for i in range(3):
            dz=-.006+i*.014
            m.rod("cloth",(x+scale*(-.028*math.cos(a)+dz*math.sin(a)), y+scale*(.100-i*.004),
                           z+scale*(dz*math.cos(a)+.028*math.sin(a))),
                  (x+scale*(.028*math.cos(a)+dz*math.sin(a)), y+scale*(.100-i*.004),
                   z+scale*(dz*math.cos(a)-.028*math.sin(a))), .0036*scale,5)


def interior(root, m, mat):
    # Footfall changes at the old lino; the work surface and shoes occupy the bright front third.
    m.box("wood",(-.43,1.155,3.395),(1.24,.07,.97),.018)
    for x in (-.97,.10):
        for z in (3.00,3.76): m.box("iron",(x,.805,z),(.045,.65,.045),.003)
    collider(root,mat,"Workbench",(-.43,.836,3.395),(1.24,.712,.97))
    m.box("rubber",(-.45,1.202,3.09),(.46,.023,.29),.008)
    shoe(m,(-.45,1.210,3.045),74,1.12)
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
    collider(root,mat,"Chair",(-.47,.70,2.27),(.40,.44,.34))

    for side in (-1,1):
        for i,z in enumerate((-2.97,-1.60,-.26,1.00)):
            x=side*.96
            m.rod("iron",(x,2.345,z-.42),(x,2.345,z+.44),.012)
            for n in range(2 if i%2==0 else 1):
                at=z-.23+n*.29
                m.rod("cloth",(x,2.34,at),(x-side*.015,2.03,at+.025),.0055,5)
                shoe(m,(x-side*.025,1.94,at+.03),8+n*9,.91,last=True)
            if i%2==0 or side<0:
                m.box("wood",(side*.94,1.444,z),(.30,.035,.96),.008)
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
    source=Vector(ANCHORS["Lamp"]); target=Vector(ANCHORS["LampAim"])
    direction=(target-source).normalized()
    m.rod("shade",source-direction*.11,source,.040,12,.139)
    lens=Parts(root,mat,"Lens")
    lens.rod("lens",source,source+direction*.007,.119,14)
    lens.seal()
    # A spring-return button at the nearest end of the flexible cord. Its contact
    # belongs to the moving cap, while the ceramic switch body stays on the wood.
    x, y, z = LAMP_SWITCH
    m.box("iron", (x, 1.198, z), (.077, .016, .093), .007)
    cord = [(x,1.198,z+.040),(-.82,1.195,3.059),(-.922,1.195,3.112),
            (-.939,1.195,3.309),(-.892,1.195,3.535),(-.85,1.198,3.624)]
    for a,b in zip(cord,cord[1:]):
        m.rod("rubber",a,b,.004,6)
    switch=Parts(port.empty("MOVE_LampSwitch",root,LAMP_SWITCH),mat)
    switch.rod("brass",(0,-.012,0),(0,0,0),.018,10)
    switch.seal()
    port.empty("ANCHOR_LampSwitch",switch.root)
    awl=Parts(port.empty("PIVOT_Awl",root,ANCHORS["HandRight"]),mat)
    awl.rod("wood",(.015,.013,-.007),(-.045,-.027,.016),.018,9,.012)
    awl.rod("iron",(-.045,-.027,.016),(-.12,-.08,.04),.004,6,.001)
    awl.seal()
    broom=Parts(port.empty("PIVOT_Broom",root,(.89,1.18,-3.39)),mat)
    broom.rod("wood",(.04,.51,-.05),(-.046,-.52,.05),.016)
    broom.rod("straw",(-.046,-.50,.05),(-.058,-.67,.065),.048,7,.088)
    broom.rod("iron",(-.046,-.51,.05),(-.049,-.55,.053),.048,7)
    broom.seal()


def access(root,m,mat):
    for tag, x, width, height in (("Tread1",2.065,.35,.16),("Tread2",1.715,.35,.32),("Landing",1.34,.40,.48)):
        m.box("concrete",(x,height*.5,2.47),(width,height,1.25),.012)
        m.box("zinc",(x+width*.5-.020,height+.002,2.47),(.026,.006,1.19))
        collider(root,mat,tag,(x,height*.5,2.47),(width,height,1.25))
    for z in (1.797,3.143):
        m.box("cream",(1.148,1.46,z),(.075,1.96,.046),.008)
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
