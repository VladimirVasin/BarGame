#!/usr/bin/env python3
"""Deterministic fixed-metre working fishing port kit, Unity +Z bow / +Y up.

The pack keeps separate load-bearing and animated parts; runtime only places
these authored meshes. Semantic surfaces use physical metre UVs and shared
textures; small hardware retains the palette in the shared PS1 Lit shader.
"""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
ACCESS_LAYOUT_PATH = ROOT / "Assets/Resources/City/Port/PortAccessLayout.json"
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit

spec = importlib.util.spec_from_file_location("offshore_art", ROOT / "tools/build-city-offshore-boats-3d-model.py")
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
empty, source = base.empty, base.source


class Geometry(base.Geometry):
    def __init__(self):
        super().__init__()
        self.role=None
        self.face_roles=[]

    def add(self,vertices,faces,color,uvs=None,solid=True):
        super().add(vertices,faces,color,uvs,solid)
        self.face_roles.extend([self.role]*len(faces))

CONCRETE = (.42,.43,.405,1)
EDGE = (.52,.52,.475,1)
DARK = (.095,.12,.125,1)
METAL = (.235,.275,.26,1)
PAINT = (.285,.385,.37,1)
RUST = (.34,.245,.20,1)
CABIN = (.60,.615,.57,1)
WOOD = (.32,.28,.23,1)
ROPE = (.44,.425,.355,1)
GLASS = (.18,.26,.265,1)
FISH = (.59,.65,.62,1)
ICE = (.78,.82,.80,1)
LAMP = (.66,.47,.29,1)
PALETTE = [CONCRETE,EDGE,DARK,METAL,PAINT,RUST,CABIN,WOOD,ROPE,GLASS,FISH,ICE,LAMP]
SURFACE_TILES = {"Concrete":2.0,"ConcreteWall":2.0,"Steel":1.5,"SteelDark":1.5,"SteelLight":1.5,"SteelRust":1.5,"Plaster":2.0,"Timber":1.0,
                 "Roof":2.0,"Deck":1.0,"Tare":.75,"Fish":.5,"Ice":.5,"Rubber":.5,"Fabric":1.0,"Asphalt":12.0,"RoadMarking":2.0}
ASPHALT_WORLD_ORIGIN=(-67.0,202.0)
WAREHOUSE_LIGHTS = {"A":(0,4.58,-13.7),"B":(.8,4.58,-17.45),"C":(-6.6,4.58,-15.25)}
WAREHOUSE_REFRIGERATION = (3.12,4.02,-17.1)


def surface_role(name,color,face=None,geom=None,index=0):
    if name.startswith(("COL_","WarehouseLampGlass")) or name in ("CabinGlass","WorkLampGlass","SearchlightGlass"):
        return "Plain"
    if geom is not None and index<len(geom.face_roles) and geom.face_roles[index]:
        return geom.face_roles[index]
    if color in (CONCRETE,EDGE):
        if name.startswith(("Trawler","Hatch")):return "SteelLight"
        if face is not None:
            a,b,c=[Vector(geom.vertices[i]) for i in face[:3]]
            if abs((b-a).cross(c-a).normalized().z)<.65:return "ConcreteWall"
        return "Concrete"
    if color==CABIN:return "Plaster" if name.startswith("Dock") else "SteelLight"
    if color==METAL:return "SteelDark"
    if color==RUST:return "SteelRust"
    if color==PAINT:return "Steel"
    if color==WOOD:return "Timber"
    return "Plain"


def obj(geom, name, parent, mat):
    face_roles=[surface_role(name,c,f,geom,i) for i,(f,c) in enumerate(zip(geom.faces,geom.colors))]
    roles=sorted(set(face_roles))
    if roles==["Plain"]:
        result=geom.object(name,parent,mat)
        for polygon,color in zip(result.data.polygons,geom.colors):
            for loop in polygon.loop_indices:
                result.data.uv_layers.active.data[loop].uv=((PALETTE.index(color)+.5)/len(PALETTE),.5)
        return result
    group=empty(name,parent)
    for role in roles:
        # Separate semantic renderers keep one shared material per real surface;
        # no generated texture is hidden in the deterministic geometry signature.
        subset=Geometry()
        for face,color,face_role in zip(geom.faces,geom.colors,face_roles):
            if face_role!=role:continue
            start=len(subset.vertices)
            subset.vertices.extend(geom.vertices[i] for i in face)
            subset.faces.append(tuple(range(start,start+len(face))))
            subset.colors.append(color)
            subset.uvs.extend(geom.uvs[i] for i in face)
        part=subset.object(name+"__"+role,group,mat)
        for polygon,color in zip(part.data.polygons,subset.colors):
            # Blender Z is Unity up. The dominant face normal chooses a
            # stable metre projection and gives every cap/wall nonzero UV area.
            normal=polygon.normal
            axis=max(range(3),key=lambda i:abs(normal[i]))
            axes=((1,2),(0,2),(0,1))[axis]
            for loop in polygon.loop_indices:
                v=part.data.vertices[part.data.loops[loop].vertex_index].co
                part.data.uv_layers.active.data[loop].uv=(((PALETTE.index(color)+.5)/len(PALETTE),.5)
                    if role=="Plain" else (((v.x+ASPHALT_WORLD_ORIGIN[0])/SURFACE_TILES[role],
                    (v.y+ASPHALT_WORLD_ORIGIN[1])/SURFACE_TILES[role]) if role=="Asphalt" and axis==2
                    else (v[axes[0]]/SURFACE_TILES[role],v[axes[1]]/SURFACE_TILES[role])))
    return group


def kit_add(geom, geometry, color):
    verts, faces = geometry
    geom.add([source(v) for v in verts], [tuple(reversed(f)) for f in faces], color)


def chamfer(geom, center, size, color, bevel=.035):
    kit_add(geom, kit.chamfered_box(source(center),source(size),bevel),color)


def ring(geom, center, radius, thickness, color, normal=(0,1,0), steps=16):
    normal = Vector(normal).normalized()
    tangent = normal.cross(Vector((0,0,1)))
    if tangent.length < .1:
        tangent = normal.cross(Vector((1,0,0)))
    tangent.normalize()
    second = normal.cross(tangent)
    points = [Vector(center)+radius*(math.cos(i*math.tau/steps)*tangent+math.sin(i*math.tau/steps)*second) for i in range(steps)]
    for i in range(steps):
        geom.rod(points[i],points[(i+1)%steps],thickness,color,6)


def bollard(geom,x,y,z):
    chamfer(geom,(x,y+.05,z),(.62,.1,.56),METAL)
    geom.rod((x,y+.1,z),(x,y+.52,z),.16,DARK,8)
    geom.rod((x-.35,y+.46,z),(x+.35,y+.46,z),.13,METAL,8)


def crate(geom,x,y,z,fish=False):
    previous_role=geom.role
    geom.role="Tare"
    chamfer(geom,(x,y+.06,z),(.55,.12,.68),PAINT,.015)
    for side in (-1,1):
        for yy in (.17,.30):
            geom.box((x+side*.26,y+yy,z),(.055,.10,.68),PAINT)
            geom.box((x,y+yy,z+side*.315),(.49,.10,.05),PAINT)
    if fish:
        geom.role="Ice"
        geom.box((x,y+.17,z),(.44,.10,.56),ICE)
        geom.role="Fish"
        for i in range(3):
            xx,zz = x-.13+i*.13,z-.16+(i%2)*.2
            geom.rod((xx,y+.24,zz-.15),(xx,y+.24,zz+.15),.043,FISH,6,end_radius=.015)
            geom.add([(xx-.055,y+.24,zz-.22),(xx+.055,y+.24,zz-.22),(xx,y+.25,zz-.10),
                      (xx-.055,y+.255,zz-.22),(xx+.055,y+.255,zz-.22),(xx,y+.265,zz-.10)],
                     [(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],FISH)
    geom.role=previous_role


def warehouse_interior(root,mat):
    """Passive cold-store fittings, outside both existing handling sweeps.

    All coordinates remain in the port's Unity metre frame. The west wall is
    the only floor furnishing bay: central turning space and the finite east
    fish buffer belong to the existing docker/driver choreography.
    """
    panels,structure,cooling,storage,bench,tools,wear,collision=[Geometry() for _ in range(8)]
    panels.role="SteelLight"
    # Thin insulated liners stop at the real reveals. Seams and folded lower
    # guards follow the walls; they never invent a second solid room shell.
    for i in range(10):
        x=-7.84+(i+.5)*1.168
        chamfer(panels,(x,3.25,-18.842),(1.14,3.42,.026),CABIN,.008)
    for x in (-7.84,3.84):
        for z in (-18.25,-17.05,-15.85,-14.65,-13.45):
            if x>0 and z>-16:continue
            chamfer(panels,(x,3.25,z),(.026,3.42,1.16),CABIN,.008)
    chamfer(panels,(-7.84,3.25,-12.50),(.026,3.42,.64),CABIN,.008)
    for x,length in ((-4.70,6.26),(2.70,2.26)):
        chamfer(panels,(x,3.25,-12.158),(length,3.42,.026),CABIN,.008)
    panels.role="SteelDark"
    for x in (-7.82,3.82):
        z,run=(-15.5,6.60) if x<0 else (-17.3,2.75)
        chamfer(panels,(x,1.75,z),(.026,.24,run),METAL,.006)
    chamfer(panels,(-2,1.75,-18.82),(11.58,.24,.026),METAL,.006)
    for x,length in ((-4.7,6.26),(2.7,2.26)):
        chamfer(panels,(x,1.75,-12.18),(length,.24,.026),METAL,.006)
    # Baffle keeps its opaque volume and west-side route. Low rubbed strips
    # give that freestanding cold-store return an ordinary protective purpose.
    for z in (-15.9,-16.1):
        chamfer(panels,(1.5,1.82,z),(4.42,.34,.022),METAL,.006)
    obj(panels,"DockWarehousePanels",root,mat)

    # Rolled ceiling joists bear on wall plates; a connected cable tray feeds
    # each pendant through one short conduit rather than floating wires.
    for x in (-7.73,3.73):
        chamfer(structure,(x,4.85,-15.5),(.16,.22,6.67),METAL,.015)
    for z in (-13.1,-15.5,-18.0):
        chamfer(structure,(-2,4.91,z),(11.5,.18,.19),METAL,.013)
        chamfer(structure,(-2,4.81,z),(11.5,.10,.065),METAL,.010)
    for x in (-7.58,-7.40):
        structure.rod((x,4.79,-18.25),(x,4.79,-12.55),.028,METAL)
    for i in range(16):
        z=-18.2+i*.37
        structure.rod((-7.62,4.79,z),(-7.36,4.79,z),.017,METAL)
    chamfer(structure,(-7.72,3.42,-12.73),(.12,.36,.28),METAL,.018)
    structure.rod((-7.70,3.6,-12.73),(-7.70,4.79,-12.73),.025,DARK)
    structure.rod((-7.70,4.79,-12.73),(-7.49,4.79,-12.73),.025,DARK)
    for suffix,(x,y,z) in WAREHOUSE_LIGHTS.items():
        structure.rod((-7.40,4.79,z),(x,4.79,z),.020,DARK)
        housing,lens=Geometry(),Geometry()
        for dx in (-.47,.47):
            housing.rod((x+dx,4.98,z),(x+dx,4.78,z),.018,METAL)
            chamfer(housing,(x+dx,4.98,z),(.16,.035,.14),METAL,.008)
        chamfer(housing,(x,4.74,z),(1.27,.12,.36),METAL,.035)
        for dz in (-.17,.17):
            chamfer(housing,(x,4.66,z+dz),(1.28,.12,.045),DARK,.012)
        for dx in (-.62,.62):
            chamfer(housing,(x+dx,4.65,z),(.065,.13,.35),METAL,.012)
        chamfer(lens,(x,4.635,z),(1.13,.070,.267),LAMP,.025)
        # Sparse retaining hoops read as a guarded industrial diffuser.
        for dx in (-.40,0,.40):
            housing.rod((x+dx,4.625,z-.175),(x+dx,4.59,z-.10),.009,METAL)
            housing.rod((x+dx,4.59,z-.10),(x+dx,4.59,z+.10),.009,METAL)
            housing.rod((x+dx,4.59,z+.10),(x+dx,4.625,z+.175),.009,METAL)
        obj(housing,"WarehouseLampHousing"+suffix,root,mat)
        obj(lens,"WarehouseLampGlass"+suffix,root,mat)
        empty("ANCHOR_WarehouseLight"+suffix,root,(x,y,z))
    obj(structure,"DockWarehouseStructure",root,mat)

    # One ceiling-height evaporator has two guarded fans, a finned coil and a
    # sloped condensate tray. Its lines cross the east wall into the existing
    # two outdoor condenser bodies; the drain ends in the wall-side grate.
    cooling.role="SteelLight"
    chamfer(cooling,(3.49,4.07,-17.1),(.62,.76,2.12),CABIN,.045)
    chamfer(cooling,(3.48,3.69,-17.1),(.69,.085,2.18),METAL,.025)
    cooling.role="SteelDark"
    for z in (-17.60,-16.60):
        cooling.rod((3.173,4.09,z),(3.162,4.09,z),.286,DARK,16)
        for r in (.10,.19,.29):ring(cooling,(3.145,4.09,z),r,.012,METAL,(1,0,0),16)
        for angle in (0,math.pi/3,math.pi*2/3):
            dy,dz=math.cos(angle)*.28,math.sin(angle)*.28
            cooling.rod((3.13,4.09-dy,z-dz),(3.13,4.09+dy,z+dz),.011,METAL)
        cooling.rod((3.15,4.09,z),(3.12,4.09,z),.057,METAL,10)
    for i in range(12):
        z=-18.05+i*.175
        cooling.box((3.165,3.80,z),(.018,.10,.055),DARK)
    for z in (-18.02,-16.20):
        chamfer(cooling,(3.5,4.71,z),(.09,.54,.15),METAL,.012)
        chamfer(cooling,(3.5,4.98,z),(.36,.045,.24),METAL,.012)
    for z,outside_z in ((-17.77,-17.3),(-16.44,-16.0)):
        cooling.role="Rubber"
        cooling.rod((3.77,4.18,z),(4.02,4.18,z),.045,DARK)
        cooling.rod((4.02,4.18,z),(4.36,4.18,z),.045,DARK)
        cooling.rod((4.36,4.18,z),(4.36,3.35,outside_z),.045,DARK)
        cooling.rod((4.36,3.35,outside_z),(4.36,3.23,outside_z),.045,DARK)
    cooling.role="SteelLight"
    cooling.rod((3.72,3.67,-18.06),(3.80,3.60,-18.28),.023,METAL)
    cooling.rod((3.80,3.60,-18.28),(3.80,1.56,-18.28),.023,METAL)
    for y in (2.0,2.8,3.5):
        chamfer(cooling,(3.825,y,-18.28),(.045,.075,.10),METAL,.008)
    obj(cooling,"DockWarehouseRefrigeration",root,mat)
    empty("ANCHOR_WarehouseRefrigeration",root,WAREHOUSE_REFRIGERATION)

    # A shallow bolted rack holds reusable lids, slatted inserts and only two
    # empty crates. The actual finite catch is never duplicated by the art.
    for x in (-7.75,-6.99):
        for z in (-18.32,-15.88):
            chamfer(storage,(x,2.55,z),(.055,2.10,.055),METAL,.010)
            chamfer(storage,(x,1.52,z),(.16,.035,.16),METAL,.008)
            for y in (1.70,2.38,3.08):
                storage.rod((x+.04,y,z),(x+.045,y,z),.015,DARK,6)
    for y in (1.70,2.38,3.08):
        chamfer(storage,(-7.37,y,-17.1),(.82,.045,2.5),METAL,.012)
        for x in (-7.79,-6.95):
            chamfer(storage,(x,y+.035,-17.1),(.035,.075,2.53),METAL,.008)
    storage.rod((-7.78,1.75,-18.28),(-7.78,3.54,-15.92),.018,METAL)
    storage.rod((-7.78,1.75,-15.92),(-7.78,3.54,-18.28),.018,METAL)
    for z in (-17.94,-16.93):crate(storage,-7.34,1.725,z)
    storage.role="Tare"
    for i in range(4):
        chamfer(storage,(-7.34,2.435+i*.054,-17.75),(.57,.035,.72),PAINT,.012)
        for z in (-18.045,-17.455):
            storage.box((-7.34,2.454+i*.054,z),(.49,.014,.021),METAL)
    storage.role="Timber"
    for y in (2.42,2.51):
        for z in (-16.72,-16.18):
            chamfer(storage,(-7.36,y,z),(.64,.055,.055),WOOD,.008)
        for i in range(5):
            chamfer(storage,(-7.60+i*.12,y+.045,-16.45),(.08,.035,.64),WOOD,.008)
    storage.role="Fabric"
    chamfer(storage,(-7.39,3.14,-17.86),(.57,.08,.58),ROPE,.025)
    chamfer(storage,(-7.34,3.20,-17.89),(.53,.045,.49),ROPE,.018)
    obj(storage,"DockWarehouseStorage",root,mat)
    # Conservative solid below the shelves: the shallow storage bay is not a
    # walk-through tunnel. All imported floor furniture stays west of -6.90.
    chamfer(collision,(-7.37,2.55,-17.1),(.90,2.10,2.60),METAL,.012)

    # Maintenance surface beside the entry: rounded timber top, braced frame,
    # real open tool tray, two spanners, rubber gloves and a wall hung squeegee.
    chamfer(bench,(-7.36,2.40,-13.67),(.88,.10,1.42),WOOD,.025)
    for x in (-7.69,-7.04):
        for z in (-14.23,-13.11):
            chamfer(bench,(x,1.94,z),(.07,.88,.07),METAL,.012)
        chamfer(bench,(x,1.75,-13.67),(.06,.06,1.18),METAL,.010)
    chamfer(bench,(-7.36,1.72,-13.67),(.71,.045,1.15),WOOD,.012)
    chamfer(collision,(-7.36,1.98,-13.67),(.88,.96,1.42),METAL,.012)
    obj(bench,"DockWarehouseBench",root,mat)
    chamfer(tools,(-7.29,2.48,-14.02),(.43,.035,.38),METAL,.010)
    for x in (-7.50,-7.08):tools.box((x,2.515,-14.02),(.026,.07,.38),METAL)
    for z in (-14.20,-13.84):tools.box((-7.29,2.515,z),(.43,.07,.026),METAL)
    for i in range(2):
        x,z=-7.40+i*.18,-14.05+i*.025
        tools.rod((x,2.53,z-.10),(x,2.53,z+.08),.013,METAL)
        ring(tools,(x,2.53,z+.105),.029,.009,METAL,(0,1,0),8)
        for dx in (-.022,.022):tools.rod((x+dx,2.53,z-.10),(x+dx,2.53,z-.14),.009,METAL)
    tools.role="Rubber"
    for i in range(2):
        x,z=-7.51+i*.27,-13.33+i*.035
        chamfer(tools,(x,2.475,z),(.15,.035,.16),DARK,.012)
        chamfer(tools,(x,2.475,z+.115),(.18,.04,.085),DARK,.012)
        for j in range(4):
            tools.rod((x-.052+j*.034,2.475,z-.06),
                      (x-.052+j*.034,2.475,z-.15-abs(j-1.5)*-.018),.014,DARK,6)
        tools.rod((x+.065,2.475,z),(x+.105,2.475,z-.05),.020,DARK,6)
    tools.role=None
    chamfer(tools,(-7.73,2.95,-14.93),(.045,.17,.62),WOOD,.012)
    for z in (-15.12,-14.83):
        tools.rod((-7.69,2.95,z),(-7.57,2.95,z),.018,METAL)
        tools.rod((-7.57,2.95,z),(-7.57,3.005,z),.018,METAL)
    tools.rod((-7.54,2.99,-15.12),(-7.37,1.68,-15.12),.022,WOOD,8)
    chamfer(tools,(-7.37,1.67,-15.12),(.11,.08,.60),METAL,.018)
    tools.role="Rubber"
    chamfer(tools,(-7.36,1.63,-15.12),(.12,.022,.62),DARK,.007)
    obj(tools,"DockWarehouseTools",root,mat)
    obj(collision,"COL_WarehouseFurniture",root,mat).hide_render=True

    # Flush drains and discontinuous wheel rubs imply washing and repeated
    # handling without loose rubbish, fresh damage, puddles or floor obstacles.
    wear.role="SteelDark"
    for x,z,width,length in ((3.65,-18.28,.28,.58),(-7.26,-14.72,.40,.32)):
        chamfer(wear,(x,1.505,z),(width,.009,length),METAL,.003)
        for i in range(6):
            wear.box((x-width*.36+i*width*.144,1.511,z),(.017,.002,length*.79),DARK)
    wear.role="ConcreteWall"
    for x in (-.51,.51):
        for i,(z,length) in enumerate(((-12.75,.72),(-13.8,.46),(-14.4,.31))):
            chamfer(wear,(x+i*.035,1.503,z),(.043,.004,length),CONCRETE,.001)
    for i in range(4):
        chamfer(wear,(-2.83+i*.31,1.503,-17.2-i*.14),(.26,.004,.035),CONCRETE,.001)
    obj(wear,"DockWarehouseFloorWear",root,mat)


def dock(mat):
    root = empty("Dock")
    g,cq,cb,cw,cr,rail,glow,awning = [Geometry() for _ in range(8)]
    for target in (g,cq):
        chamfer(target,(0,-.95,-5),(32,4.9,10),CONCRETE,.10)
        chamfer(target,(-3.5,-.95,-16),(45,4.9,12),CONCRETE)
        chamfer(target,(-21,-.95,-5),(10,4.9,10),CONCRETE)
        chamfer(target,(17.5,-.95,-5),(3,4.9,10),CONCRETE)
        # New portions of the L-shaped lorry yard do not overlap the old
        # quay's coplanar top; the asphalt wearing course is authored below.
        chamfer(target,(16,-.95,-28.5),(24,4.9,13),CONCRETE)
        chamfer(target,(23.5,-.95,-21),(9,4.9,2),CONCRETE)
        chamfer(target,(21,-.95,-15),(4,4.9,10),CONCRETE)
    g.role="Asphalt"
    chamfer(g,(16,1.508,-27.5),(24,.016,15),METAL,.004)
    chamfer(g,(13.5,1.508,-15),(19,.016,10),METAL,.004)
    g.role=None
    for target in (g,cb):
        chamfer(target,(-23,-.95,14),(4,4.9,28),CONCRETE,.13)
        chamfer(target,(-16,-.95,30),(18,4.9,4),CONCRETE,.13)
        # Continuous low parapets explain the walkable edge of the mole.
        # The southern root remains open onto the public west-side walk.
        chamfer(target,(-24.84,1.75,16),(.32,.5,32),CONCRETE,.045)
        chamfer(target,(-21.16,1.75,14),(.32,.5,28),CONCRETE,.045)
        chamfer(target,(-16,1.75,31.84),(17.4,.5,.32),CONCRETE,.045)
        chamfer(target,(-14,1.75,28.16),(13.4,.5,.32),CONCRETE,.045)
        chamfer(target,(-7.16,1.75,30),(.32,.5,3.4),CONCRETE,.045)
    # An inland promenade and broad side ramps preserve continuous access.
    for outer,inner in ((-30,-26),):
        verts=[(outer,.32,-22),(outer,.32,-19),(inner,1.5,-19),(inner,1.5,-22),
               (outer,-.2,-22),(outer,-.2,-19),(inner,-.2,-19),(inner,-.2,-22)]
        faces=[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
        for target in (g,cr): target.add(verts,faces,CONCRETE)
    # Quay courses, coping joints, damp tide band and purposeful drains.
    for x in range(-16,16,2):
        chamfer(g,(x+1,1.47,-.15),(1.95,.14,.55),EDGE,.025)
        for y in (-.6,.15,.85):
            g.box((x+(0.0 if y==.15 else .4),y,.013),(.024,.025,.018),DARK)
        g.box((x,0,.016),(1.4,.20,.026),METAL)
    for x in (-11,-3,5,13):
        g.box((x,1.509,-8.8),(1,.018,.25),DARK)
        for xx in range(9): g.box((x-.43+xx*.105,1.526,-8.8),(.035,.025,.24),METAL)
    rng=random.Random(791)
    for i in range(25):
        x,z=rng.uniform(-15,15),rng.uniform(-9,-1)
        for j in range(3):
            g.rod((x+j*.17,1.504,z+j*.11),(x+(j+1)*.17,1.504,z+(j+1)*.11+(.06 if j%2 else -.04)),.009,METAL,4)
    for x in (-11,11):
        bollard(g,x,1.5,-.3)
        empty("ANCHOR_Bollard"+("West" if x<0 else "East"),root,(x,2,-.3))
        empty("ANCHOR_Moor"+("Bow" if x<0 else "Stern"),root,(x,2,-.3))
    for x in (-12,-8,-4,0,4,8,12):
        g.role="Rubber"
        ring(g,(x,.65,.28),.39,.12,DARK,(0,0,1),12)
        g.role=None
        for dx in (-.2,.2): g.rod((x+dx,1.48,-.12),(x+dx,.92,.25),.025,METAL)
    for x in (-15,15):
        for dx in (-.27,.27): g.rod((x+dx,-1,.26),(x+dx,2,.26),.04,METAL)
        for j in range(9): g.rod((x-.27,-.8+j*.32,.28),(x+.27,-.8+j*.32,.28),.032,METAL)
    # Warehouse: physical reveals and a 2.8 m opening, no painted-on doorway.
    walls=[kit.translated(kit.wall_run(12,3.5,.28,[kit.Opening(2,2.8,2.85)]),(-2,-12,1.5)),
           kit.translated(kit.wall_run(12,3.5,.28),(-2,-19,1.5)),
           kit.translated(kit.rotated_z(kit.wall_run(7,3.5,.28),90),(-8,-15.5,1.5)),
           kit.translated(kit.rotated_z(kit.wall_run(7,3.5,.28,[kit.Opening(1.5,3,3)]),90),(4,-15.5,1.5))]
    for wall in walls:
        for target in (g,cw): kit_add(target,wall,CABIN)
    # The trolley enters, turns left behind this opaque cold-store baffle.
    for target in (g,cw): chamfer(target,(1.5,2.9,-16),(4.5,2.8,.18),CABIN)
    g.role="Roof"
    chamfer(g,(-2,5.12,-15.5),(12.65,.24,7.6),METAL,.07)
    for x in range(-8,5): g.box((x,5.275,-15.5),(.04,.05,7.5),DARK)
    g.role=None
    for side in (-1,1):
        x=-2+side*6.12
        g.rod((x,1.7,-18.8),(x,4.98,-18.8),.06,METAL)
    # Rolled insulated door above opening, service cabinet and refrigeration.
    g.rod((-1.5,4.53,-11.76),(1.5,4.53,-11.76),.22,METAL,12)
    for z in (-16,-17.3):
        chamfer(g,(4.33,2.8,z),(.45,1.05,.96),METAL)
        ring(g,(4.57,2.8,z),.32,.035,DARK,(1,0,0),12)
    chamfer(g,(-6,2.45,-11.79),(1,.95,.2),METAL)
    g.box((-6,2.42,-11.65),(.75,.68,.08),PAINT)
    # Insulated east loading leaf, guide rails and dock buffers. The truck's
    # rear bumper stops at x4.4; neither frame nor pipes intrudes into that box.
    east_door=empty("MOVE_EastLoadingDoor",root,(4.06,1.5,-14))
    leaf=Geometry();leaf.role="SteelLight"
    chamfer(leaf,(4.06,2.98,-14),(.08,2.95,2.9),CABIN,.025)
    for y in (1.9,2.4,2.9,3.4,3.9):leaf.box((4.112,y,-14),(.025,.025,2.83),METAL)
    # Project metre UVs at their previous absolute coordinates, then offset
    # only vertices under the new moving pivot. Closed geometry and albedo
    # phase stay exactly where the static leaf was originally authored.
    leaf_object=obj(leaf,"EastLoadingDoorVisible",east_door,mat)
    for part in [leaf_object]+list(leaf_object.children_recursive):
        if part.type!="MESH":continue
        for vertex in part.data.vertices:vertex.co-=Vector(source((4.06,1.5,-14)))
    for z in (-15.58,-12.42):
        chamfer(g,(4.17,3,-14+(z+14)),(.14,3.05,.12),METAL,.015)
    g.rod((4.12,4.73,-15.65),(4.12,4.73,-12.35),.20,METAL,12)
    # Fixed dock buffers belong to the masonry, outside the clear opening.
    # Inside the aperture they floated unsupported as soon as the leaf rose.
    for z in (-15.72,-12.28):
        chamfer(g,(4.16,1.98,z),(.06,.72,.36),METAL,.012)
        g.role="Rubber";chamfer(g,(4.24,1.98,z),(.16,.63,.28),DARK,.035);g.role=None
    empty("ANCHOR_EastLoadingDoor",root,(4.14,1.5,-14))
    # Utility routes have endpoints: gutter into downpipes, protected water
    # supply to the wash point, ventilation and a continuous warehouse plinth.
    for z in (-11.76,-19.24):
        g.rod((-8.25,5,z),(4.25,5,z),.075,METAL,10)
    for x in (-7.78,3.78):
        g.rod((x,4.98,-19.24),(x,1.65,-19.24),.067,METAL,10)
        g.rod((x,1.65,-19.24),(x,1.54,-19.5),.067,METAL,10)
    chamfer(g,(-2,1.72,-19.16),(12.1,.42,.14),CONCRETE,.025)
    g.rod((-7.4,1.8,-11.75),(-7.4,3.05,-11.75),.035,METAL)
    g.rod((-7.4,3.05,-11.75),(-6.65,3.05,-11.75),.035,METAL)
    ring(g,(-6.72,2.88,-11.65),.13,.023,METAL,(0,0,1),12)
    for i in range(4):ring(g,(-7.0,2.25,-11.6-i*.027),.30,.025,DARK,(0,0,1),18)
    chamfer(g,(-5.55,1.56,-11.1),(1.6,.10,.65),CONCRETE,.04)
    for x in range(10):g.box((-6.21+x*.145,1.616,-11.1),(.035,.018,.53),DARK)
    # Shallow working awning covers the empty tare stack, leaving the lane free.
    for target in (g,awning):
        target.role="Roof"
        chamfer(target,(9.5,4.4,-6.8),(8,.17,4.8),METAL)
        target.role=None
        for x in (6,13):
            for z in (-9,-4.6): target.rod((x,1.5,z),(x,4.35,z),.075,METAL,8)
    for i in range(9): crate(g,6+(i%3)*.72,1.5+(i//3)*.4,-7.6)
    for i in range(4):
        ring(g,(-11+i*.65,1.56,-7),.24,.028,ROPE)
    # Modest warm work fixtures on the building, no street poles on the beach.
    for i,x in enumerate((-4,2)):
        g.rod((x,4.25,-11.9),(x,4.25,-11.35),.045,METAL)
        chamfer(g,(x,4.18,-11.3),(.56,.16,.4),DARK)
        glow.box((x,4.085,-11.3),(.4,.03,.26),LAMP)
        empty("ANCHOR_WorkLight"+"AB"[i],root,(x,4,-11.3))
    # Public path passes behind the cold store and up the west breakwater;
    # the rail never cuts across the quay-to-warehouse trolley journey.
    for a,b in (((-18,-19.6),(3.5,-19.6)),((-18,-19.6),(-18,0)),
                ((23,-20),(23,-10)),((28,-35),(28,-29.5)),((28,-22.5),(28,-20)),
                ((4,-35),(28,-35)),((4,-35),(4,-22)),((23,-20),(28,-20)),
                ((17,-7.5),(17,0))):
        for target in (g,rail):
            count=math.ceil(math.dist(a,b)/2.5)
            for i in range(count+1):
                x=a[0]+(b[0]-a[0])*i/count;z=a[1]+(b[1]-a[1])*i/count
                target.rod((x,1.5,z),(x,2.55,z),.045,METAL)
            for y in (1.94,2.50):target.rod((a[0],y,a[1]),(b[0],y,b[1]),.045,METAL)
    obj(g,"DockVisible",root,mat)
    obj(glow,"WorkLampGlass",root,mat)
    for name,geom in (("COL_Quay",cq),("COL_Breakwater",cb),("COL_WarehouseWalls",cw),("COL_Ramps",cr),("COL_Rail",rail),("COL_Awning",awning)):
        obj(geom,name,root,mat).hide_render=True
    for name,p in {"WarehouseDoor":(0,1.5,-12),"WarehouseHandoff":(0,1.5,-17.5),"Visitor":(0,1.5,-20.5),
                   "RestWest":(-12,1.5,-4.8),"RestEast":(-10.5,1.5,-5.7),"RestQuay":(-12.2,1.5,-6.3)}.items():
        empty("ANCHOR_"+name,root,p)
    warehouse_interior(root,mat)
    return root


def ribbon_piece(geom,a_left,a_right,b_left,b_right,depth,color):
    top=[a_left,a_right,b_right,b_left]
    bottom=[(p[0],p[1]-depth,p[2]) for p in top]
    geom.add(top+bottom,[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],color)


def access_road(mat):
    layout=json.loads(ACCESS_LAYOUT_PATH.read_text(encoding="utf-8"))
    root=empty("AccessRoad");road,shoulder,collision=[Geometry() for _ in range(3)]
    road.role="Asphalt"
    def station(s,lateral):
        p,r=s["center"],s["right"]
        return (p["x"]+r["x"]*lateral,p["y"]+lateral*s["crossfall"],p["z"]+r["z"]*lateral)
    samples=layout["roadSamples"]
    for a,b in zip(samples,samples[1:]):
        al,ar=station(a,-a["halfWidth"]),station(a,a["halfWidth"])
        bl,br=station(b,-b["halfWidth"]),station(b,b["halfWidth"])
        ribbon_piece(road,al,ar,bl,br,.18,METAL)
        ribbon_piece(collision,al,ar,bl,br,.18,CONCRETE)
        # Flush shoulders keep the shared approach and flared street join
        # free of raised curbs or incidental rail posts.
        for sign in (-1,1):
            aa=station(a,sign*a["halfWidth"]);bb=station(b,sign*b["halfWidth"])
            ao=station(a,sign*(a["halfWidth"]+.5));bo=station(b,sign*(b["halfWidth"]+.5))
            ribbon_piece(shoulder,aa,ao,bb,bo,.23,CONCRETE)
            ribbon_piece(collision,aa,ao,bb,bo,.23,CONCRETE)
    obj(road,"AccessCarriageway",root,mat);obj(shoulder,"AccessShoulders",root,mat)
    obj(collision,"COL_AccessRoad",root,mat).hide_render=True
    for name,p in (("Street",samples[0]["center"]),("Gate",layout["gate"])):
        empty("ANCHOR_Access"+name,root,(p["x"],p["y"],p["z"]))
    for name,p in (("RoadStart",samples[0]["center"]),("RoadGate",layout["gate"]),
                   ("LoadingStop",layout["loadingRearAxle"])):
        empty("ANCHOR_"+name,root,(p["x"],p["y"],p["z"]))
    return root


def trawler(mat):
    root=empty("Trawler")
    g=Geometry()
    stations=[(-10,.66,1.6),(-9,.87,1.6),(-6,1,1.6),(0,1,1.6),(6,.93,1.7),(8,.6,2.0),(10,.025,2.45)]
    verts=[]
    for z,w,y in stations:
        w*=2.7
        verts.extend([(-w,y,z),(-w,.1,z),(-w*.72,-1.05,z),(-w*.24,-1.5,z),
                      (w*.24,-1.5,z),(w*.72,-1.05,z),(w,.1,z),(w,y,z)])
    faces=[tuple(reversed(range(8)))]
    for j in range(len(stations)-1):
        for k in range(7):
            a=j*8+k
            faces.append((a,a+1,a+9,a+8))
    faces.append(tuple(range((len(stations)-1)*8,len(stations)*8)))
    g.add(verts,faces,PAINT)
    # Deck is built around two true holds. There is deliberately no hull top
    # polygon across the openings and no cargo spawn hidden inside solid deck.
    g.role="Deck"
    for x in (-2.475,2.475): chamfer(g,(x,1.52,0),(.45,.16,12),METAL)
    for z,length in ((-5.475,1.05),(0,6.1),(5.475,1.05)):
        chamfer(g,(0,1.52,z),(4.5,.16,length),METAL)
    for a,b in ((0,1),(1,2),(4,5),(5,6)):
        za,wa,ya=stations[a];zb,wb,yb=stations[b]
        # aft and raised bow deck segments only, beyond both hatch apertures.
        if za>=6 or zb<=-6:
            vertices=[(-wa*2.7,ya,za),(wa*2.7,ya,za),(wb*2.7,yb,zb),(-wb*2.7,yb,zb),
                      (-wa*2.7,ya-.14,za),(wa*2.7,ya-.14,za),(wb*2.7,yb-.14,zb),(-wb*2.7,yb-.14,zb)]
            g.add(vertices,[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],METAL)
    g.role=None
    for i,z in enumerate((-4,4)):
        for x in (-2.27,2.27): chamfer(g,(x,1.64,z),(.12,.28,1.98),EDGE,.02)
        for zz in (-.96,.96): chamfer(g,(0,1.64,z+zz),(4.48,.28,.12),EDGE,.02)
        g.box((0,-.58,z),(4.42,.16,1.8),DARK)
        for x in (-2.25,2.25): g.box((x,.55,z),(.08,2.1,1.8),METAL)
        for zz in (-.94,.94): g.box((0,.55,z+zz),(4.42,2.1,.08),METAL)
        hinge=empty("Hatch"+"AB"[i],root,(2.33,1.82,z))
        leaf=Geometry()
        chamfer(leaf,(-2.33,0,0),(4.66,.13,2.04),PAINT,.04)
        for zz in (-.65,0,.65): leaf.box((-2.33,.09,zz),(4.4,.05,.065),METAL)
        for zz in (-.65,.65): leaf.rod((-4.4,.12,zz-.13),(-4.4,.12,zz+.13),.035,DARK)
        obj(leaf,"HatchLeaf"+"AB"[i],hinge,mat)
        empty("ANCHOR_Cargo"+"AB"[i],root,(0,1.6,z))
    # Raised continuous bulwarks and metal top rails follow the hull stations.
    for sign in (-1,1):
        for j in range(len(stations)-1):
            za,wa,ya=stations[j];zb,wb,yb=stations[j+1]
            aa=(sign*2.7*wa,ya+.45,za);bb=(sign*2.7*wb,yb+.45,zb)
            g.rod(aa,bb,.055,DARK,8)
            g.rod((aa[0],.15,za),(bb[0],.15,zb),.085,DARK,8)
            g.rod((aa[0],ya,za),aa,.045,METAL,6)
    # Wheelhouse has a real glazed front opening so its captain remains visible.
    cabinfront=kit.translated(kit.wall_run(3,2.55,.13,[kit.Opening(-.72,1.15,2.24,1.1),kit.Opening(.72,1.15,2.24,1.1)]),(0,-6.1,1.6))
    kit_add(g,cabinfront,CABIN)
    for x in (-1.5,1.5):
        wall=kit.translated(kit.rotated_z(kit.wall_run(3,2.55,.13,[kit.Opening(.35,1.65,2.24,1.1)]),90),(x,-7.6,1.6))
        kit_add(g,wall,CABIN)
    chamfer(g,(0,2.875,-9.1),(3,2.55,.13),CABIN)
    chamfer(g,(0,4.25,-7.6),(3.36,.2,3.46),DARK,.055)
    # Glazing is a separate semantically named renderer for shared glass material.
    glass=Geometry()
    for x in (-.72,.72): glass.box((x,3.27,-6.14),(1.12,1.1,.025),GLASS)
    for x in (-1.53,1.53): glass.box((x,3.27,-7.25),(.025,1.1,1.62),GLASS)
    obj(glass,"CabinGlass",root,mat)
    chamfer(g,(0,2.52,-6.65),(2.1,.24,.5),METAL)
    ring(g,(-.7,2.94,-6.88),.22,.025,DARK,(0,.6,1))
    g.rod((.7,4.35,-8),(.7,5.25,-8.2),.21,METAL,10)
    g.rod((0,4.35,-7.3),(0,7,-7.6),.08,METAL,8,end_radius=.04)
    g.rod((-.8,6.3,-7.48),(.8,6.3,-7.48),.04,METAL)
    g.box((0,6.4,-7.48),(1.7,.16,.2),CABIN)
    for x in (-1.1,1.1):
        g.rod((x,1.65,-9.3),(x,2.2,-9.3),.23,DARK,10)
        for j in range(5): ring(g,(x,1.8+j*.065,-9.3),.245,.025,ROPE)
    # Working nets stay folded outside the crane's hatch-to-quay corridors.
    for z in (-1.1,-.6,0,.6):
        for x in (-2.25,2.25):
            ring(g,(x,1.8,z),.22,.055,METAL,steps=10)
    for side in (-1,1):
        for z in (-8.8,8): bollard(g,side*(1.5 if z<0 else 1.4),1.65 if z<0 else 2.02,z)
    # Scuffs and welded repair plates are sparse and dull, never fresh damage.
    for side in (-1,1):
        for z in (-5,-2,1,3):
            chamfer(g,(side*2.706,.55,z),(.035,.28,.6),METAL,.012)
            g.box((side*2.73,.31,z+.1),(.014,.23,.035),RUST)
    # A forward anchor winch, chain run and hawse mouths give the bow a real
    # working purpose without crowding the port-side mooring path.
    chamfer(g,(0,2.17,8.35),(1.25,.22,.85),PAINT)
    g.rod((-.64,2.52,8.35),(.64,2.52,8.35),.27,METAL,12)
    for x in (-.49,0,.49):ring(g,(x,2.52,8.35),.29,.04,DARK,(1,0,0),12)
    for z in (8.65,8.84,9.03,9.22):ring(g,(0,2.40+(z-8.65)*.17,z),.08,.018,DARK,(1,0,0),10)
    for x in (-.75,.75):ring(g,(x,2.28,8.7),.15,.05,DARK,(0,1,0),10)
    # Layered wheelhouse glazing: sill, thin mullions, wiper and an actual
    # rear access leaf separate the cabin from the old generic box silhouette.
    for x in (-1.38,-.08,.08,1.38):
        chamfer(g,(x,3.27,-6.005),(.075,1.24,.12),METAL,.012)
    for y in (2.67,3.88):chamfer(g,(0,y,-6.005),(2.88,.075,.12),METAL,.012)
    for x in (-.73,.73):
        g.rod((x,2.8,-5.92),(x+.24,3.45,-5.92),.018,DARK,6)
        g.rod((x+.08,3.27,-5.905),(x+.40,3.65,-5.905),.022,DARK,6)
    chamfer(g,(.47,2.71,-9.20),(.92,2.1,.13),PAINT,.025)
    chamfer(g,(.47,3.27,-9.285),(.64,.68,.04),DARK,.015)
    g.rod((.16,2.55,-9.30),(.16,2.74,-9.30),.024,METAL)
    for x in (-1.17,1.17):
        g.rod((x,4.35,-7.5),(x,4.73,-7.5),.045,METAL)
        chamfer(g,(x,4.74,-7.4),(.32,.21,.36),DARK,.025)
    # A substantial old searchlight: raised yoke, deep drum and rolled rim.
    # Its authored short cone makes a working shaft readable in coastal fog;
    # it reaches only the foredeck and nearest water, never the closed beach.
    lamp_center=Vector((.2,4.96,-5.90))
    lamp_target=Vector((.2,0,13))
    lamp_direction=(lamp_target-lamp_center).normalized()
    chamfer(g,(.2,4.39,-6.08),(.65,.10,.50),METAL,.035)
    g.rod((.2,4.42,-6.08),(.2,4.74,-6.08),.082,METAL,8)
    for x in (-.19,.59):
        g.rod((x,4.64,-6.08),(x,4.96,-5.90),.049,PAINT,8)
        g.rod((x-.045,4.96,-5.90),(x+.045,4.96,-5.90),.078,METAL,10)
    rear=lamp_center-lamp_direction*.32
    lip=lamp_center+lamp_direction*.30
    g.rod(rear,lip,.325,PAINT,16)
    ring(g,lip,.326,.033,METAL,lamp_direction,16)
    ring(g,lip-lamp_direction*.10,.326,.015,RUST,lamp_direction,16)
    ring(g,rear,.328,.023,METAL,lamp_direction,16)
    ring(g,rear+lamp_direction*.065,.328,.013,RUST,lamp_direction,16)
    # The glass sits in front of the capped shell so the visible lens cannot
    # be hidden by the housing; the source is just beyond its front face.
    lens=Geometry()
    lens.rod(lip+lamp_direction*.010,lip+lamp_direction*.039,.290,LAMP,16)
    obj(lens,"SearchlightGlass",root,mat)
    lamp_source=lip+lamp_direction*.065
    empty("ANCHOR_Searchlight",root,tuple(lamp_source))
    empty("ANCHOR_SearchlightTarget",root,tuple(lamp_target))
    beam=Geometry();vertices=[];uv=[]
    across=Vector((1,0,0));up=lamp_direction.cross(across).normalized()
    rings=((0,.286),(3,.62),(9,1.40),(15,2.16),(22,3.05));segments=16
    for distance,radius in rings:
        center=lamp_source+lamp_direction*distance
        for i in range(segments):
            angle=i*math.tau/segments
            vertices.append(tuple(center+radius*(math.cos(angle)*across+math.sin(angle)*up)))
            uv.append((distance/22,i/segments))
    faces=[(r*segments+i,r*segments+(i+1)%segments,(r+1)*segments+(i+1)%segments,(r+1)*segments+i)
           for r in range(len(rings)-1) for i in range(segments)]
    beam.add(vertices,faces,LAMP,uv,solid=False)
    # Keep axial UVs: obj() intentionally replaces plain surfaces with palette UVs.
    beam.object("SearchlightBeam",root,mat)
    # Folded fishing net in its low bin, away from both working holds and
    # the continuous side aisles. Each strand rests on the stack beneath it.
    chamfer(g,(.2,1.7,.1),(1.8,.20,2.1),WOOD)
    for side in (-1,1):
        g.box((.2+side*.88,1.93,.1),(.06,.38,2.1),WOOD)
        g.box((.2,1.93,.1+side*1.03),(1.74,.38,.06),WOOD)
    for j in range(10):
        for k in range(3):
            y=1.83+k*.095
            z=-.8+j*.18
            g.rod((-.6,y,z),(1,y+.06*math.sin(j),z+.09),.018,METAL,5)
    for j in range(10):
        x=-.6+j*.17
        g.rod((x,2.10,-.8),(x+.08,2.1+.06*math.cos(j),.85),.018,ROPE,5)
    for z in (-.75,-.2,.35,.85):
        g.rod((.99,2.12,z),(1.08,2.12,z),.065,ROPE,8)
    # Working-deck panel seams and salt-dark drainage mouths stay subordinate
    # to the ship's silhouette; nothing seals a cargo extraction aperture.
    for z in (-8.6,-7.7,-6.8,-2.6,-1.6,1.7,2.6,6.6):
        for x in (-2.46,2.46):g.box((x,1.606,z),(.34,.012,.018),DARK)
    for x in (-2.71,2.71):
        for z in (-5.7,-2.4,2.4,5.7):g.box((x,1.3,z),(.018,.14,.31),DARK)
    # Propeller and rudder explain propulsion when the source is inspected.
    g.rod((0,-.85,-9),(0,-.85,-10.3),.10,METAL,8)
    for i in range(3):
        a=i*math.tau/3
        g.rod((0,-.85,-10.2),(.5*math.cos(a),-.85+.5*math.sin(a),-10.2),.11,METAL,6,end_radius=.035)
    chamfer(g,(0,-.7,-10.55),(.10,1,.42),METAL)
    obj(g,"TrawlerVisible",root,mat)
    anchors={"MooringBowPort":(-1.4,2.5,8),"MooringBowStarboard":(1.4,2.5,8),
             "MooringSternPort":(-1.5,2.13,-8.8),"MooringSternStarboard":(1.5,2.13,-8.8),
             "Captain":(-.7,1.6,-7.18),"HelmLeft":(-.86,2.94,-6.88),"HelmRight":(-.54,2.94,-6.88),
             "Deckhand":(-2.43,1.6,-4),"MoorBow":(-1.4,2.5,8),"MoorStern":(-1.5,2.13,-8.8),
             "Engine":(0,.1,-8),"Horn":(0,4.4,-6.8)}
    for name,p in anchors.items(): empty("ANCHOR_"+name,root,p)
    return root


def crane_base(mat):
    root=empty("CraneBase");g=Geometry()
    chamfer(g,(0,.22,0),(2.7,.44,2.7),CONCRETE,.10)
    g.rod((0,.44,0),(0,.70,0),1.05,METAL,16)
    g.rod((0,.7,0),(0,3.85,0),.38,PAINT,12)
    for x in (-.5,.5):
        g.rod((x,1.1,0),(x,3.72,0),.075,METAL)
    for y in (1,1.35,1.7,2.05,2.4,2.75,3.1,3.45):g.rod((-.3,y,-.4),(.3,y,-.4),.035,METAL)
    g.rod((1.9,0,-.15),(1.9,.65,-.15),.06,METAL)
    chamfer(g,(1.9,.78,-.15),(.7,.25,.35),PAINT)
    for side,x in (("Left",1.7),("Right",2.1)):
        pivot=empty("Lever"+side,root,(x,.9,-.15))
        lever=Geometry()
        lever.rod((0,0,0),(0,.12,-.05),.025,DARK)
        # Keep the previous hand-contact point exactly, with a readable grip.
        lever.rod((0,.095,-.040),(0,.14,-.058),.033,DARK,8)
        obj(lever,"ControlLever"+side,pivot,mat)
        empty("ANCHOR_Control"+side,pivot,(0,.12,-.05))
        ring(g,(x,.90,-.15),.045,.012,METAL,steps=10)
    for x in (-.78,.78):
        for z in (-.78,.78):g.rod((x,.65,z),(x,.8,z),.07,DARK,6)
    obj(g,"CraneBaseVisible",root,mat)
    # The complete head slews with the jib: bearing, counterweight, winch and
    # cheeks are one physical assembly rather than a stationary rear block.
    head=empty("CraneHead",root,(0,4,0));h=Geometry()
    h.rod((0,-.38,0),(0,-.12,0),.64,METAL,20)
    for i in range(16):
        a=i*math.tau/16
        h.rod((math.cos(a)*.56,-.12,math.sin(a)*.56),(math.cos(a)*.56,-.04,math.sin(a)*.56),.04,DARK,6)
    for x in (-.5,.5):
        chamfer(h,(x,.05,0),(.18,.70,.74),PAINT,.055)
        h.rod((x,-.12,0),(x,-.12,-2),.10,METAL,8)
        h.rod((x,.72,-.4),(x,-.12,-2),.075,METAL,8)
    chamfer(h,(0,-.22,-1.93),(1.95,.80,.8),CONCRETE,.07)
    for x in (-.64,.64):chamfer(h,(x,-.2,-1.93),(.08,.91,.94),METAL,.015)
    h.rod((-.62,.24,-1.07),(.62,.24,-1.07),.29,DARK,16)
    for x in (-.43,-.29,-.15,0,.15,.29,.43):ring(h,(x,.24,-1.07),.295,.026,ROPE,(1,0,0),14)
    for x in (-.62,.62):ring(h,(x,.24,-1.07),.37,.045,METAL,(1,0,0),16)
    h.rod((-.7,0,0),(.7,0,0),.17,DARK,16)
    chamfer(h,(.86,.1,-1.08),(.43,.42,.70),PAINT,.045)
    for z in (-1.3,-1.15,-1,-.85):h.box((1.08,.1,z),(.022,.25,.04),DARK)
    obj(h,"CraneHeadVisible",head,mat)
    empty("ANCHOR_HoistFeed",head,(0,.48,-.9))
    empty("ANCHOR_BoomPivot",root,(0,4,0));empty("ANCHOR_Operator",root,(1.9,0,-.7))
    return root


def crane_boom(mat):
    root=empty("CraneBoom");g=Geometry()
    for sign in (-1,1):
        g.rod((sign*.43,-.22,0),(sign*.17,-.13,8),.075,PAINT,8)
        g.rod((sign*.43,.42,0),(sign*.17,.22,8),.065,PAINT,8)
        for i in range(8):
            w=.43-.26*i/8;wn=.43-.26*(i+1)/8
            g.rod((sign*w,-.22,i),(sign*wn,.42-.2*(i+1)/8,i+1),.038,METAL)
            g.rod((sign*w,.42-.2*i/8,i),(sign*wn,-.22,i+1),.035,METAL)
    for i in range(9):
        w=.43-.26*i/8
        g.rod((-w,-.20,i),(w,-.20,i),.045,METAL)
    g.rod((-.5,0,0),(.5,0,0),.19,DARK,12)
    g.rod((-.2,0,7.92),(.2,0,7.92),.21,DARK,12)
    g.rod((0,.49,.1),(0,.29,7.88),.026,ROPE)
    obj(g,"CraneBoomVisible",root,mat)
    empty("ANCHOR_Tip",root,(0,0,8))
    empty("ANCHOR_Heel",root,(0,.49,.1))
    return root


def small_part(name,mat):
    root=empty(name);g=Geometry()
    if name=="Hook":
        g.rod((-.17,-.12,0),(.17,-.12,0),.16,DARK,12)
        for sign in (-1,1):chamfer(g,(sign*.17,-.18,0),(.06,.36,.30),METAL)
        # Open C-shaped forged hook, attachment at y=-.62.
        pts=[(0,-.26,0),(0,-.40,0),(.07,-.52,0),(.04,-.62,0),(-.09,-.66,0),(-.17,-.57,0),(-.16,-.50,0)]
        for a,b in zip(pts,pts[1:]):g.rod(a,b,.042,METAL,8)
        empty("ANCHOR_Load",root,(0,-.62,0))
    elif name=="Cargo":
        chamfer(g,(0,.07,0),(1.3,.14,1.5),METAL,.025)
        for x in (-.62,.62):
            for z in (-.72,.72):g.rod((x,.1,z),(x,1.03,z),.03,METAL)
        for y in (.35,.68,1):
            for x in (-.62,.62):g.rod((x,y,-.72),(x,y,.72),.025,METAL)
            for z in (-.72,.72):g.rod((-.62,y,z),(.62,y,z),.025,METAL)
        for x in (-.30,.30):
            for z in (-.37,.37):
                for y in (.15,.53):crate(g,x,y,z,True)
        for x in (-.6,.6):
            for z in (-.7,.7):g.rod((x,1,z),(0,1.7,0),.026,ROPE)
        ring(g,(0,1.72,0),.06,.018,METAL,(0,0,1),10)
        empty("ANCHOR_Lift",root,(0,1.7,0))
    elif name=="Trolley":
        chamfer(g,(0,.39,0),(1.55,.12,1.8),METAL)
        for x in (-.6,.6):
            g.role="Rubber"
            for z in (-.6,.6):g.rod((x-.05,.18,z),(x+.05,.18,z),.18,DARK,10)
            g.role=None
            g.rod((x,.42,-.8),(x,1.1,-1.05),.035,METAL)
        g.rod((-.6,1.1,-1.05),(.6,1.1,-1.05),.035,METAL)
        empty("ANCHOR_Load",root,(0,.45,0));empty("ANCHOR_Handle",root,(0,1.1,-1.05))
    elif name=="RopeSegment":
        g.rod((0,0,0),(0,0,1),.015,ROPE,6)
    obj(g,name+"Visible",root,mat)
    return root


def describe(root):
    bpy.context.view_layer.update()
    meshes=[o for o in root.children_recursive if o.type=="MESH"]
    vertices=[source(root.matrix_world.inverted() @ o.matrix_world @ v.co) for o in meshes if not o.name.startswith("COL_") for v in o.data.vertices]
    anchors=[{"name":o.name.split('.')[0],"position":[round(v,5) for v in source(root.matrix_world.inverted()@o.matrix_world.translation)]}
             for o in root.children_recursive if o.type=="EMPTY"]
    geometry=[(o.name.split('.')[0],[[round(v,6) for v in p.co] for p in o.data.vertices],
               [list(p.vertices) for p in o.data.polygons],[[round(v,6) for v in c.color] for c in o.data.color_attributes[0].data],
               [[round(v,6) for v in uv.uv] for uv in o.data.uv_layers.active.data],
               [[round(v,6) for v in row] for row in root.matrix_world.inverted()@o.matrix_world])
              for o in meshes]
    return {"name":root.name.split('.')[0],"bounds_min":[round(min(p[i] for p in vertices),5) for i in range(3)],
            "bounds_max":[round(max(p[i] for p in vertices),5) for i in range(3)],"anchors":anchors,
            "triangles":sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),
            "geometry_signature":hashlib.sha256(json.dumps(geometry,sort_keys=True).encode()).hexdigest()}


def build(mat):
    return [dock(mat),trawler(mat),crane_base(mat),crane_boom(mat)]+[small_part(n,mat) for n in ("Hook","Cargo","Trolley","RopeSegment")]+[access_road(mat)]


def validate_holds(roots):
    """Prove that all six cages pass through real deck holes, to their floor."""
    bpy.context.view_layer.update()
    ship=next(r for r in roots if r.name.split('.')[0]=="Trawler")
    hull=next(o for o in ship.children_recursive if o.name.split('.')[0]=="TrawlerVisible")
    vertices=[];faces=[]
    for part in [hull]+list(hull.children_recursive):
        if part.type!="MESH":continue
        start=len(vertices)
        vertices.extend(ship.matrix_world.inverted()@part.matrix_world@v.co for v in part.data.vertices)
        faces.extend([start+i for i in p.vertices] for p in part.data.polygons)
    tree=BVHTree.FromPolygons(vertices,faces)
    for z in (-4,4):
        for x in (-1.35,0,1.35):
            for dx,dz in ((-.65,-.75),(-.65,.75),(.65,-.75),(.65,.75),(0,0)):
                hit,_,_,_=tree.ray_cast(Vector(source((x+dx,2,z+dz))),Vector(source((0,-1,0))),3)
                if hit is None or abs(source(hit)[1]+.5)>.005:
                    raise RuntimeError(f"Cargo extraction shaft intersects deck or misses hold floor: {x+dx},{z+dz} -> {hit}")


def validate_landing(roots):
    """The right-hand cage turns to the cart's real heading before lowering."""
    cargo=next(r for r in roots if r.name.split('.')[0]=="Cargo")
    dock_root=next(r for r in roots if r.name.split('.')[0]=="Dock")
    cover=next(o for o in dock_root.children_recursive if o.name.split('.')[0]=="COL_Awning")
    yaw=math.atan2(-4,-4.5)
    points=[source(v.co) for o in cargo.children_recursive if o.type=="MESH" for v in o.data.vertices]
    rightmost=max(4+math.cos(yaw)*p[0]+math.sin(yaw)*p[2] for p in points)
    roof_start=min(source(v.co)[0] for v in cover.data.vertices)
    if roof_start-rightmost<.3:
        raise RuntimeError(f"Rotated cargo lowering envelope enters awning: {rightmost} versus {roof_start}")


def validate_surfaces(roots):
    for root in roots:
        for part in root.children_recursive:
            if part.type!="MESH" or "__" not in part.name or part.name.split(".")[0].endswith("__Plain"):continue
            uv=part.data.uv_layers.active.data
            for polygon in part.data.polygons:
                points=[uv[i].uv for i in polygon.loop_indices]
                area=abs(sum(a.x*b.y-b.x*a.y for a,b in zip(points,points[1:]+points[:1])))*.5
                if area<1e-10:raise RuntimeError(f"Semantic surface has degenerate texture UVs: {part.name}")


def validate_controls_and_light(roots):
    bpy.context.view_layer.update()
    crane=next(r for r in roots if r.name.split('.')[0]=="CraneBase")
    parts={p.name.split('.')[0]:p for p in crane.children_recursive}
    for side,x in (("Left",1.7),("Right",2.1)):
        lever,grip=parts["Lever"+side],parts["ANCHOR_Control"+side]
        point=Vector(source(crane.matrix_world.inverted()@grip.matrix_world.translation))
        if grip.parent!=lever or (point-Vector((x,1.02,-.2))).length>.00001:
            raise RuntimeError("Crane control lost its moving rest-position hand contact: "+side)
        if not any(p.type=="MESH" for p in lever.children_recursive):
            raise RuntimeError("Crane lever pivot has no authored geometry: "+side)
    ship=next(r for r in roots if r.name.split('.')[0]=="Trawler")
    parts={p.name.split('.')[0]:p for p in ship.children_recursive}
    lamp=parts["ANCHOR_Searchlight"].matrix_world.translation
    target=parts["ANCHOR_SearchlightTarget"].matrix_world.translation
    direction=(target-lamp).normalized()
    if source(direction)[1]>=-.1 or source(direction)[2]<.9:
        raise RuntimeError("Searchlight must aim forward and down, outside its own housing")
    vertices=[];faces=[]
    for part in ship.children_recursive:
        if part.type!="MESH" or part.name.split('.')[0]=="SearchlightBeam":continue
        start=len(vertices)
        vertices.extend(part.matrix_world@v.co for v in part.data.vertices)
        faces.extend([start+i for i in p.vertices] for p in part.data.polygons)
    hit,_,_,distance=BVHTree.FromPolygons(vertices,faces).ray_cast(lamp,direction,22)
    if hit is not None and distance<4:
        raise RuntimeError("Searchlight beam is masked by its housing or wheelhouse")
    beam=parts["SearchlightBeam"]
    beam_uv=[loop.uv.x for loop in beam.data.uv_layers.active.data]
    if min(beam_uv)!=0 or max(beam_uv)!=1:
        raise RuntimeError("Searchlight shaft lost its complete axial fade UVs")
    lengths=[(beam.matrix_world@v.co-lamp).dot(direction) for v in beam.data.vertices]
    if abs(min(lengths))>.001 or abs(max(lengths)-22)>.001:
        raise RuntimeError("Searchlight shaft must remain bounded to twenty-two metres")


def validate_warehouse_interior(roots):
    """Measure new art against the unchanged working room, not empty anchors.

    The reserve covers both trolleys' west turns and trailing people well
    beyond their centre lines. Runtime capture separately samples their actual
    imported bodies through the existing finite pickup/handoff states.
    """
    bpy.context.view_layer.update()
    dock_root=next(r for r in roots if r.name.split('.')[0]=="Dock")
    parts={p.name.split('.')[0]:p for p in dock_root.children_recursive}

    def measured(name):
        part=parts[name]
        meshes=([part] if part.type=="MESH" else [])+[
            p for p in part.children_recursive if p.type=="MESH"]
        points=[source(p.matrix_world@v.co) for p in meshes for v in p.data.vertices]
        if not points:raise RuntimeError("Warehouse part has no measured geometry: "+name)
        return points

    def extents(name):
        points=measured(name)
        return tuple(max(p[i] for p in points)-min(p[i] for p in points) for i in range(3))

    for name,minimum in (("DockWarehousePanels",(10,3,5)),
                         ("DockWarehouseStorage",(.7,2,2)),
                         ("DockWarehouseBench",(.7,.8,1.2)),
                         ("DockWarehouseRefrigeration",(.8,2,2))):
        if any(actual<expected for actual,expected in zip(extents(name),minimum)):
            raise RuntimeError("Warehouse fitting lost its authored readable volume: "+name)
    if any(x< -7.855 or x>3.855 or z< -18.86 or z> -12.14
           for x,y,z in measured("DockWarehousePanels")):
        raise RuntimeError("Warehouse insulated liners protrude through the existing exterior walls")
    for name in ("DockWarehouseStorage","DockWarehouseBench","DockWarehouseTools","COL_WarehouseFurniture"):
        points=measured(name)
        # Existing driver uses x=-3.2 with a three-metre lower turn; docker
        # uses x=-2.3. Reserve x>=-5.2 for the complete handling envelope.
        # Requiring the entire furnishing bay west of -6.85 leaves extra room
        # for the hero and means even its collider cannot cross those routes.
        if any(x>-6.85 or z< -18.45 or z> -12.85 or y<1.48 for x,y,z in points):
            raise RuntimeError("Warehouse furniture invades a wall or the reserved working floor: "+name)
    if max(p[1] for p in measured("DockWarehouseFloorWear"))>1.515:
        raise RuntimeError("Warehouse wash/wheel marks must remain flush, without floor obstacles")
    for x,y,z in measured("DockWarehouseRefrigeration"):
        if y<3.60 and x<3.72:
            raise RuntimeError("Warehouse refrigeration line leaves its east-wall strip near stored fish")
    actual=source(parts["ANCHOR_WarehouseRefrigeration"].matrix_world.translation)
    if math.dist(actual,WAREHOUSE_REFRIGERATION)>.0001:
        raise RuntimeError("Warehouse refrigeration sound lost its physical fan source")

    vertices=[];faces=[]
    for part in dock_root.children_recursive:
        if part.type!="MESH" or part.name.startswith("COL_"):continue
        start=len(vertices)
        vertices.extend(part.matrix_world@v.co for v in part.data.vertices)
        faces.extend([start+i for i in polygon.vertices] for polygon in part.data.polygons)
    tree=BVHTree.FromPolygons(vertices,faces)
    for suffix,position in WAREHOUSE_LIGHTS.items():
        actual=source(parts["ANCHOR_WarehouseLight"+suffix].matrix_world.translation)
        if math.dist(actual,position)>.0001:
            raise RuntimeError("Warehouse light lost its authored source: "+suffix)
        lens=measured("WarehouseLampGlass"+suffix)
        if min(p[1] for p in lens)<position[1]+.019 or extents("WarehouseLampGlass"+suffix)[0]<1.1:
            raise RuntimeError("Warehouse diffuser must sit above its source and retain a readable length: "+suffix)
        if min(p[1] for p in measured("WarehouseLampHousing"+suffix))<4.575:
            raise RuntimeError("Warehouse lamp hangs into the working headroom: "+suffix)
        hit,_,_,distance=tree.ray_cast(Vector(source(position)),Vector(source((0,-1,0))),1.0)
        if hit is not None:
            raise RuntimeError(f"Warehouse light {suffix} is blocked below its source at {distance:.4f} m")


def source_surface_materials(roots):
    """The editable source/review uses the same semantic images and tints.

    Images are packed only into the Blender source, after FBX export. Their
    generative pixels do not participate in deterministic mesh signatures.
    """
    specs={"Concrete":("Concrete",(.67,.71,.70)),"ConcreteWall":("ConcreteWall",(.68,.75,.72)),
           "Steel":("PaintedSteel",(.44,.61,.59)),"SteelDark":("PaintedSteel",(.38,.46,.43)),
           "SteelLight":("PaintedSteel",(.84,.88,.82)),"SteelRust":("PaintedSteel",(.59,.44,.35)),
           "Plaster":("WarehousePlaster",(.85,.90,.84)),"Timber":("Timber",(.74,.65,.55)),
           "Roof":("RoofMetal",(.57,.66,.64)),"Deck":("Deck",(.60,.68,.66)),
           "Tare":("Tare",(.54,.72,.69)),"Rubber":("Rubber",(.57,.66,.65)),
           "Fabric":("Fabric",(.64,.71,.68)),"Asphalt":("Asphalt",(1,1,1)),"RoadMarking":("RoadMarking",(1,1,1)),
           "Fish":("Fish",(1,1,1)),"Ice":("Ice",(1,1,1))}
    materials={}
    def linear(c):return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    for role,(stem,tint) in specs.items():
        texture=(ROOT/("Assets/Resources/Textures/CityRoad"+("Asphalt" if role=="Asphalt" else "Marking")+"Albedo.png") if role in ("Asphalt","RoadMarking")
                 else ROOT/("Assets/Resources/City/Port/Textures/Port"+stem+"Albedo.png"))
        if not texture.is_file():continue
        mat=bpy.data.materials.new("PortSurface_"+role);mat.use_nodes=True
        nodes=mat.node_tree.nodes;links=mat.node_tree.links;shader=nodes.get("Principled BSDF")
        tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(texture),check_existing=True);tex.image.pack()
        tex.extension="REPEAT";tex.interpolation="Linear"
        multiply=nodes.new("ShaderNodeMixRGB");multiply.blend_type="MULTIPLY";multiply.inputs[0].default_value=1
        multiply.inputs[2].default_value=tuple(linear(c) for c in tint)+(1,)
        links.new(tex.outputs["Color"],multiply.inputs[1]);links.new(multiply.outputs[0],shader.inputs["Base Color"])
        shader.inputs["Roughness"].default_value=.87;materials[role]=mat
    for root in roots:
        for part in root.children_recursive:
            if part.type!="MESH" or "__" not in part.name:continue
            role=part.name.rsplit("__",1)[1].split(".")[0]
            if role in materials:part.data.materials[0]=materials[role]


def preview(roots,path):
    indexed={r.name:r for r in roots}
    indexed["Trawler"].location=source((0,0,4.3))
    indexed["Trawler"].rotation_euler.z=math.pi/2
    indexed["CraneBase"].location=source((-5,1.5,-2))
    indexed["CraneBoom"].location=source((-5,5.5,-2))
    indexed["CraneBoom"].rotation_euler.x=.30
    indexed["Hook"].location=source((-5,6,5.6))
    indexed["Cargo"].location=source((-5,3.5,5.6))
    indexed["Trolley"].location=source((5,1.5,-3.3))
    indexed["RopeSegment"].hide_render=True
    for part in indexed["Trawler"].children:
        if part.name in ("HatchA","HatchB"):part.rotation_euler.y=math.radians(95)
    scene=bpy.context.scene
    scene.render.engine="CYCLES";scene.cycles.samples=24
    scene.world.color=(.3,.33,.34)
    target=Vector(source((12,1.3,-7)))
    camdata=bpy.data.cameras.new("PortReviewCamera");cam=bpy.data.objects.new(camdata.name,camdata);bpy.context.collection.objects.link(cam)
    cam.location=source((75,60,65));cam.rotation_euler=(target-cam.location).to_track_quat("-Z","Y").to_euler()
    camdata.type="ORTHO";camdata.ortho_scale=130;scene.camera=cam
    data=bpy.data.lights.new("PortReviewSoftbox","AREA");data.energy=15000;data.size=35
    light=bpy.data.objects.new(data.name,data);bpy.context.collection.objects.link(light);light.location=source((4,35,10))
    light.rotation_euler=(target-light.location).to_track_quat("-Z","Y").to_euler()
    scene.render.resolution_x=1500;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def main():
    p=argparse.ArgumentParser();p.add_argument("--no-preview",action="store_true");p.add_argument("--validate-only",action="store_true")
    p.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/City/Port")
    p.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/City/Port")
    p.add_argument("--only-part",action="append",choices=("Dock","Trawler","CraneBase","CraneBoom","Hook","Cargo","Trolley","RopeSegment","AccessRoad"))
    args=p.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    base.reset();mat=base.material("PortVertexPaint")
    roots=build(mat);validate_holds(roots);validate_landing(roots);validate_surfaces(roots);validate_controls_and_light(roots);validate_warehouse_interior(roots);entries=[describe(r) for r in roots]
    manifest={"design_id":"city_working_fishing_port_v1","generator":Path(__file__).name,
              "coordinate_system":"Unity +Y up / +Z bow; fixed metres; waterline origin",
              "parts":entries,"cargo_attachment_height":1.7,"crane_boom_length":8,"crane_pivot_height":4,
              "semantic_uv_tiles_metres":SURFACE_TILES,"geometry_signature_includes":["vertices","faces","colors","uv0","transforms"],
              "asphalt_uv_world_origin_xz":list(ASPHALT_WORLD_ORIGIN),"asphalt_texture":"Textures/CityRoadAsphaltAlbedo",
              "access_layout_sha256":hashlib.sha256(ACCESS_LAYOUT_PATH.read_bytes()).hexdigest(),
              "hatch_centres":[[0,1.6,-4],[0,1.6,4]],"hatch_clear_aperture":[4.4,1.8]}
    if args.validate_only:
        saved=json.loads((args.model_dir/"CityPort3D.json").read_text(encoding="utf-8"))
        if saved!=manifest:raise RuntimeError("Port authored contract differs from committed manifest")
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        for r in roots:
            if args.only_part is None or r.name in args.only_part:base.export(r,args.model_dir/(r.name+".fbx"))
        (args.model_dir/"CityPort3D.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
        # Generated byte images save their pixel channels verbatim; use visual
        # swatches here, while mesh color_srgb converts the vertex paint once.
        if args.only_part is None:
            palette=bpy.data.images.new("PortPalette",width=len(PALETTE),height=1,alpha=True)
            palette.pixels=[v for c in PALETTE for v in c]
            palette.filepath_raw=str(args.model_dir/"PortPalette.png");palette.file_format="PNG";palette.save()
        source_surface_materials(roots)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"CityPort3D.blend"),check_existing=False)
        if not args.no_preview:preview(roots,args.source_dir/"CityPort3D.png")
    base.reset();rebuilt=build(mat);validate_holds(rebuilt);validate_landing(rebuilt);validate_surfaces(rebuilt);validate_controls_and_light(rebuilt);validate_warehouse_interior(rebuilt);repeated=[describe(r) for r in rebuilt]
    if entries!=repeated:raise RuntimeError("Port deterministic rebuild mismatch")
    print("CITY PORT ART CONTRACT OK: fixed metre parts, real holds, semantic UVs, moving controls, warehouse fittings/clearances/lamps, forward searchlight and deterministic geometry")
    print(json.dumps([{k:v for k,v in e.items() if k in ("name","triangles","bounds_min","bounds_max")} for e in entries]))


if __name__=="__main__":main()
