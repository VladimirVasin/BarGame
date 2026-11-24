#!/usr/bin/env python3
"""Build one fixed-metre City laundry and its finite movable props.

The L-shaped shell leaves the east service court under a real canopy. Shared
interior_kit walls own the same physical openings as the collision donors.
All coordinates below are Unity metres: +Y up, public frontage +Z, floor Y=0.
No animation, rig, Light, camera, texture or material asset is exported.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import laundry_finishes as finishes

spec = importlib.util.spec_from_file_location("laundry_port_kit", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
port.SURFACE_TILES["Glass"] = 2.0

DESIGN_ID = "city_household_laundry_v1"
VERSION = "2.0.0"
MODEL_DIR = ROOT / "Assets/Resources/City/Laundry"
SOURCE_DIR = ROOT / "ArtSource/City/Laundry"
SURFACES = ("Concrete", "Plaster", "Steel", "SteelDark", "SteelLight", "Timber", "Fabric", "Rubber", "Roof", "Plain", "Glass", "Sign", "LaundryFinish")
BOUND_MIN = (-4.3, -.15, -3.3)
BOUND_MAX = (4.3, 4.2, 3.3)
MAX_TRIANGLES = 18000
MAX_RENDERERS = 30
SOLID_FOOTPRINTS = (
    ("front-wall-and-closed-door", (-4, 2.88, 1.5, 3.12)),
    ("rear-wall", (-4, -3.12, 4, -2.88)),
    ("west-wall", (-4.12, -3, -3.88, 3)),
    ("side-wall-before-opening", (1.38, 0, 1.62, .3)),
    ("side-wall-after-opening", (1.38, 1.9, 1.62, 3)),
    ("service-east-wall", (3.88, -3, 4.12, 0)),
    ("service-front-wall", (1.5, -.12, 4, .12)),
    ("sorting-table", (-3.5, -.4, -1.8, .8)),
    ("shelf", (-3.81, -2.275, -3.03, -.625)),
    ("machine-a", (-1.36, -2.67, -.24, -1.57)),
    ("machine-b", (-.06, -2.67, 1.06, -1.57)),
    ("trolley", (2.54, .60, 3.46, 1.475)),
    ("canopy-foot-rear", (3.93, .06, 4.17, .30)),
    ("canopy-foot-front", (3.93, 2.78, 4.17, 3.02)),
)

ANCHORS = {
    "ANCHOR_MainDoor": (-2.8, 0, 3),
    "ANCHOR_ExteriorApproach": (-2.8, 0, 4.3),
    "ANCHOR_ServiceDoor": (1.5, 0, 1.1),
    "ANCHOR_Worker": (-2.65, 0, 1.20),
    "ANCHOR_TableLeft": (-2.95, .97, .38),
    "ANCHOR_TableRight": (-2.35, .97, .38),
    "ANCHOR_TableSurface": (-2.65, .96, .12),
    "ANCHOR_ClothRest": (-2.65, 1.34, .50),
    "ANCHOR_BasketRest": (-2.65, 1.15, .50),
    "ANCHOR_BasketGripLeft": (-2.98, 1.35, .50),
    "ANCHOR_BasketGripRight": (-2.32, 1.35, .50),
    "ANCHOR_CarryWaypointA": (.7, 0, 1.1),
    "ANCHOR_CarryWaypointB": (2.25, 0, 1.1),
    "ANCHOR_CanopyWorker": (2.25, 0, 1.1),
    "ANCHOR_CanopyHandoff": (2.9, 1.02, 1.1),
    "ANCHOR_CanopyBasketGripLeft": (2.9, 1.22, 1.43),
    "ANCHOR_CanopyBasketGripRight": (2.9, 1.22, .77),
    "ANCHOR_DrumA": (-.8, .78, -1.56),
    "ANCHOR_DrumB": (.5, .78, -1.56),
    "ANCHOR_WorkLight": (-2.5, 3.215, .25),
    "ANCHOR_MachinesLight": (-.15, 3.215, -1.3),
    "ANCHOR_CourtLight": (2.6, 2.675, 1.1),
    "ANCHOR_EntranceLight": (-2.65, 3.365, 3.26),
    "ANCHOR_Ventilation": (2.7, 3.65, -1.65),
    "ANCHOR_MachineAudio": (-.15, .78, -2.0),
}


def geometry(role=None):
    result = port.Geometry()
    result.role = role
    return result


def box(g, center, size, color=port.METAL, bevel=.012):
    port.chamfer(g, center, size, color, bevel)


FINISHED_PARTS = {
    "LaundryMachines","LaundryMachineFittings","LaundryFurniture","LaundryFurnitureIron",
    "LaundryCanopyStructure","LaundryFrames","LaundryRubberFittings","LaundryServicePipesAndLightBody",
    "CarryBasket","WorkCloth","WorkClothLeft","WorkClothRight","LaundryStoredCloth",
    "DrumA","DrumB","DrumClothA","DrumClothB"}


def finish_key(name,color,centre,normal):
    if name.startswith("WorkCloth") or name.startswith("DrumCloth") or name=="LaundryStoredCloth":
        return {port.ICE:"towel",port.ROPE:"canvas",port.CABIN:"blue"}.get(color,"linen")
    if name in ("DrumA","DrumB"): return "drum" if color==port.EDGE else "steel"
    if name=="LaundryMachines": return "enamel" if color==port.ICE else "dark" if color==port.DARK else "panel"
    if name=="LaundryFurniture":
        grain_axis=2 if centre.x < -3.0 and centre.z < -.55 else 0
        if abs(normal[grain_axis])>.94: return "end"
        return "cartwood" if centre.x>0 else "wood"
    if name=="CarryBasket":
        return "grip" if color==port.CABIN else "steel" if color==port.EDGE else "paint"
    if name=="LaundryRubberFittings" or (name=="LaundryMachineFittings" and color==port.DARK): return "rubber"
    return "zinc" if color==port.EDGE else "paint" if color==port.PAINT else "dark"


def paint_fittings(g,name,root,material):
    o=g.object(name+"__LaundryFinish",root,material)
    uv=o.data.uv_layers.active.data
    for poly,color in zip(o.data.polygons,g.colors):
        centre=Vector(port.source(poly.center)); normal=Vector(port.source(poly.normal))
        key=finish_key(name,color,centre,normal)
        axes=sorted(range(3),key=lambda a:abs(normal[a]))[:2]
        # Keep each physical grain direction across neighbouring bevels.
        axes.sort(key=lambda a:(a!=(2 if name=="LaundryFurniture" and centre.z<-.55 else 0),a))
        width,height=finishes.SURFACES[key][2]
        points=[Vector(port.source(o.data.vertices[i].co)) for i in poly.vertices]
        def fit(axis,length):
            lo=min(p[axis] for p in points); span=max(p[axis] for p in points)-lo
            offset=((lo*.71)%1)*max(0,1-span/length)
            return [offset+(p[axis]-lo)/max(length,span) for p in points]
        coords=list(zip(fit(axes[0],width),fit(axes[1],height)))
        for loop,p,(u,v) in zip(poly.loop_indices,points,coords):
            if key=="towel" and name.startswith("WorkCloth"):
                offset={"WorkClothLeft":-.132,"WorkClothRight":.132}.get(name,0)
                u,v=.5+(p.x+offset)/.54,.5+p.z/.36
            elif key=="wood" and centre.z>-.45 and abs(normal.y)>.78:
                u,v=(p.x+3.50)/1.70,(p.z+.4)/1.20
            elif key=="cartwood" and abs(normal.y)>.78: u,v=(p.x-2.54)/.92,(p.z-.725)/.75
            elif key in ("panel","enamel") and abs(normal.z)>.7:
                u,v=(p.x-(-.8 if centre.x<-.15 else .5)) /1.12+.5,1-p.y/1.32
            elif key=="grip":
                side=1 if centre.x>0 else -1
                u=.5+(p-Vector((side*.33,.20,0))).dot(handle_axis(side))/.16
                v=.5+(p.y-.20)/.05
            elif key=="drum":
                # Unwrap each shell strip about its own station so the seam uses one edge of the chart.
                a=math.atan2(centre.y,centre.x)
                phi=math.atan2(p.y,p.x)
                u=.5+((a+math.pi)%(math.pi/2)-math.pi/4+math.remainder(phi-a,math.tau))/(math.pi/2+.21)
                v=(-p.z-.036)/.50
            uv[loop].uv=finishes.chart_uv(key,u,v)
    return o


def emit(g, name, root, material):
    if not g.faces: return None
    if name in FINISHED_PARTS: return paint_fittings(g,name,root,material)
    group=port.obj(g,name,root,material)
    if name=="LaundryFloor":
        o=group.children[0]
        for loop in o.data.loops:
            p=o.data.vertices[loop.vertex_index].co
            o.data.uv_layers.active.data[loop.index].uv=((p.x+4.3)/8.6,(p.y+3.3)/6.6)
    return group


def add_kit(g, shape, color):
    port.kit_add(g, shape, color)


def walls():
    """Main room and the attached rear service block, with real door/window holes."""
    pieces = [
        kit.translated(kit.wall_run(5.5, 3.45, .24,
            (kit.Opening(-1.55, 1.2, 2.45), kit.Opening(1.0, 2.6, 2.65, .85))), (-1.25, 3, 0)),
        kit.translated(kit.wall_run(8, 3.45, .24), (0, -3, 0)),
        kit.translated(kit.rotated_z(kit.wall_run(6, 3.45, .24,
            (kit.Opening(0, 2.2, 2.65, 1.0),)), 90), (-4, 0, 0)),
        kit.translated(kit.rotated_z(kit.wall_run(3, 3.45, .24,
            (kit.Opening(-.4, 1.6, 2.45),)), 90), (1.5, 1.5, 0)),
        kit.translated(kit.rotated_z(kit.wall_run(3, 3.45, .24), 90), (4, -1.5, 0)),
        kit.translated(kit.wall_run(2.5, 3.45, .24), (2.75, 0, 0)),
    ]
    return kit.merge_all(pieces)


def shell(root, material):
    wall = geometry("Plaster")
    add_kit(wall, walls(), port.CABIN)
    emit(wall, "LaundryShell", root, material)
    collision = geometry()
    add_kit(collision, walls(), port.CABIN)
    emit(collision, "COL_Walls", root, material)
    floor = geometry("Concrete")
    box(floor, (0, -.075, 0), (8.6, .15, 6.6), port.CONCRETE, .015)
    emit(floor, "LaundryFloor", root, material)
    collision = geometry()
    box(collision, (0, -.075, 0), (8.6, .15, 6.6), port.CONCRETE, .015)
    emit(collision, "COL_Floor", root, material)
    # The roof stops over the main room and service block. Its lower canopy
    # covers only the open L-notch, rather than creating a second closed room.
    roof = geometry("Roof")
    box(roof, (-1.25, 3.55, 0), (5.95, .20, 6.6), port.DARK, .035)
    box(roof, (2.75, 3.55, -1.5), (3.1, .20, 3.6), port.DARK, .035)
    for z in (-3.2, 3.2):
        box(roof, (-1.25, 3.46, z), (5.85, .14, .12), port.DARK, .015)
    box(roof, (2.75, 2.85, 1.5), (3.1, .15, 3.6), port.DARK, .025)
    emit(roof, "LaundryRoof", root, material)
    structure = geometry("SteelDark")
    coll = geometry()
    for target in (structure, coll):
        for z in (.18, 2.9):
            box(target, (4.05, 1.4, z), (.11, 2.8, .11), port.METAL)
            box(target, (4.05, .035, z), (.24, .07, .24), port.METAL)
            target.rod((4.05, 2.25, z), (3.5, 2.77, z), .035, port.METAL)
        box(target, (2.77, 2.75, 2.9), (2.7, .12, .12), port.METAL)
    for z in (.18,2.90):
        for dx in (-.075,.075):
            for dz in (-.075,.075):
                structure.rod((4.05+dx,.070,z+dz),(4.05+dx,.084,z+dz),.013,port.EDGE,6)
        box(structure,(4.05,2.74,z),(.20,.08,.19),port.METAL,.008)
    emit(structure, "LaundryCanopyStructure", root, material)
    box(coll, (-1.25, 3.55, 0), (5.95, .20, 6.6), port.DARK, .035)
    box(coll, (2.75, 3.55, -1.5), (3.1, .20, 3.6), port.DARK, .035)
    box(coll, (2.75, 2.85, 1.5), (3.1, .15, 3.6), port.DARK, .025)
    emit(coll, "COL_Canopy", root, material)
    frames = geometry("Steel")
    # Front window and west window have actual open apertures; glazing is
    # separate so Unity can reuse its existing transparent window surface.
    for x in (-1.57, 1.07):
        box(frames, (x, 1.75, 3.03), (.055, 1.84, .10), port.PAINT)
    for y in (.85, 2.65):
        box(frames, (-.25, y, 3.03), (2.7, .065, .10), port.PAINT)
    box(frames, (-.25, 1.75, 3.03), (.055, 1.84, .10), port.PAINT)
    for z in (-1.12, 1.12):
        box(frames, (-4.03, 1.825, z), (.10, 1.70, .055), port.PAINT)
    for y in (1, 2.65):
        box(frames, (-4.03, y, 0), (.10, .065, 2.3), port.PAINT)
    # Visible doorway jambs and head, no fake opaque door over either route.
    for x in (-3.42, -2.18):
        box(frames, (x, 1.225, 3.04), (.06, 2.45, .13), port.PAINT)
    box(frames, (-2.8, 2.46, 3.04), (1.3, .08, .13), port.PAINT)
    for z in (.28, 1.92):
        box(frames, (1.54, 1.225, z), (.13, 2.45, .06), port.PAINT)
    box(frames, (1.54, 2.46, 1.1), (.13, .08, 1.7), port.PAINT)
    box(frames,(1.52,.013,1.10),(.25,.026,1.56),port.EDGE,.009)
    for z in (.40,.86,1.34,1.80):
        frames.rod((1.58,.026,z),(1.58,.028,z),.008,port.METAL,7)
    for p,d in (((-.25,.853,3.115),(2.79,.028,.193)),((-4.124,.994,0),(.196,.028,2.35)),
                ((-.25,.834,3.207),(2.79,.040,.015)),((-4.218,.976,0),(.014,.042,2.35))):
        box(frames,p,d,port.EDGE,.005)
    emit(frames, "LaundryFrames", root, material)
    door = geometry("Steel")
    add_kit(door, kit.translated(kit.panelled_leaf(1.18, 2.43, .07), (-3.39, 3, .02)), port.PAINT)
    emit(door, "LaundryClosedFrontDoor", root, material)
    collision = geometry()
    box(collision, (-2.8, 1.235, 3), (1.18, 2.43, .07), port.METAL)
    emit(collision, "COL_Door", root, material)
    glass = geometry("Glass")
    box(glass, (-.25, 1.75, 3.015), (2.56, 1.76, .012), port.GLASS, .001)
    box(glass, (-4.015, 1.825, 0), (.012, 1.61, 2.16), port.GLASS, .001)
    emit(glass, "LaundryWindows", root, material)
    trim = geometry("Concrete")
    box(trim, (-.25, .83, 3.075), (2.8, .08, .22), port.EDGE)
    box(trim, (-4.075, .97, 0), (.22, .08, 2.4), port.EDGE)
    # A flush, floor-level service drain remains outside every walking dock.
    box(trim, (3.65, .008, -.25), (.30, .016, .75), port.METAL, .003)
    for z in (-.52, -.38, -.24, -.10, .04):
        box(trim, (3.65, .019, z), (.24, .008, .018), port.DARK, .001)
    emit(trim, "LaundrySillsAndDrain", root, material)


def circle(x,y,z,r,count=32):
    return [(x+r*math.cos(j*math.tau/count),y+r*math.sin(j*math.tau/count),z) for j in range(count)]


def rolled(g,x,y,profile,color):
    loft(g,[circle(x,y,z,r) for r,z in profile],color,True)


def machines(root, material):
    casing,fittings,glass=geometry("SteelLight"),geometry("SteelDark"),geometry("Glass")
    coll=geometry()
    for index,x in enumerate((-.8,.5)):
        box(coll,(x,.66,-2.12),(1.12,1.32,1.10),port.ICE)
        for dx in (-.51,.51):
            box(casing,(x+dx,.64,-2.12),(.092,1.20,1.09),port.ICE,.013)
        box(casing,(x,.065,-2.14),(1.11,.13,1.04),port.DARK,.015)
        box(casing,(x,.67,-2.635),(1.1,1.22,.06),port.ICE,.010)
        box(casing,(x,1.289,-2.12),(1.12,.066,1.1),port.CABIN,.015)
        box(casing,(x,1.184,-1.61),(1.105,.139,.083),port.CABIN,.012)
        # Pressed fascia surrounds a real circular opening, including the four square corners.
        angles=sorted([j*math.tau/32 for j in range(32)]+[
            math.atan2(y,dx)%math.tau for y in (-.665,.376) for dx in (-.55,.55)])
        perimeter=[]
        for a in angles:
            xx,yy=math.cos(a),math.sin(a)
            t=min(.55/max(abs(xx),1e-8),(.376 if yy>0 else .665)/max(abs(yy),1e-8))
            perimeter.append((x+xx*t,.78+yy*t,-1.579))
        def aperture(z):
            return [(x+.344*math.cos(a),.78+.344*math.sin(a),z) for a in angles]
        loft(casing,[perimeter,aperture(-1.579),aperture(-1.611),
                     [(a,b,c-.032) for a,b,c in perimeter]],port.ICE,True)
        box(fittings,(x,.254,-1.575),(.92,.198,.022),port.METAL,.011)
        box(casing,(x,.254,-1.560),(.9,.182,.016),port.CABIN,.008)
        for dx in (-.389,.389):
            fittings.rod((x+dx,.255,-1.556),(x+dx,.255,-1.546),.011,port.EDGE,8)
        for n in range(5):
            box(fittings,(x-.238+n*.088,.265,-1.550),(.053,.012,.006),port.DARK,.003)
        rolled(fittings,x,.78,((.377,-1.59),(.390,-1.563),(.386,-1.531),(.369,-1.513),
                               (.343,-1.516),(.336,-1.535),(.347,-1.573)),port.EDGE)
        rolled(fittings,x,.78,((.342,-1.546),(.345,-1.519),(.336,-1.506),(.322,-1.515),(.319,-1.55)),port.DARK)
        # Shallow convex window fixed to the door, independent of the rotor.
        loft(glass,[circle(x,.78,z,r) for r,z in ((.319,-1.532),(.323,-1.521),(.292,-1.490),(.22,-1.470),(.075,-1.461))],port.GLASS)
        for y in (.65,.88):
            box(fittings,(x-.384,y,-1.552),(.085,.055,.066),port.METAL,.009)
        fittings.rod((x-.405,.644,-1.526),(x-.405,.906,-1.526),.017,port.EDGE,10)
        for y in (.72,.84):
            fittings.rod((x+.361,y,-1.540),(x+.392,y,-1.478),.014,port.EDGE,8)
        fittings.rod((x+.392,.72,-1.478),(x+.392,.84,-1.478),.020,port.DARK,10)
        box(fittings,(x-.29,1.18,-1.562),(.25,.071,.032),port.EDGE,.006)
        box(fittings,(x-.29,1.17,-1.536),(.152,.018,.025),port.METAL,.004)
        for dx in (.13,.31):
            fittings.rod((x+dx,1.186,-1.573),(x+dx,1.186,-1.525),.043,port.DARK,12)
            box(fittings,(x+dx,1.197,-1.523),(.012,.026,.008),port.EDGE,.003)
        pivot=port.empty("MOVE_Drum"+"AB"[index],root,ANCHORS["ANCHOR_Drum"+"AB"[index]])
        drum=geometry("SteelDark")
        rolled(drum,0,0,((.322,-.53),(.327,-.49),(.327,-.05),(.315,-.036),(.302,-.047),(.309,-.50)),port.EDGE)
        drum.rod((0,0,-.53),(0,0,-.50),.315,port.METAL,32)
        for n in range(3):
            a=n*math.tau/3
            radial,tangent=Vector((math.cos(a),math.sin(a),0)),Vector((-math.sin(a),math.cos(a),0))
            loft(drum,[[radial*r+tangent*t+Vector((0,0,z)) for r,t in
                       ((.315,-.037),(.277,-.025),(.254,0),(.277,.025),(.315,.037))]
                       for z in (-.46,-.09)],port.CABIN)
        emit(drum,"Drum"+"AB"[index],pivot,material)
        cloth=geometry("Fabric")
        # Two loose rolls show their spiralling hems at the glass; a flopped towel
        # spans the lifters. All three rotate with the barrel and clear the fixed boot.
        for n,(cx,cy,r,swatch) in enumerate(((-.140,-.136,.104,port.CABIN),(.055,-.166,.109,port.ROPE))):
            sections=[]
            for j in range(31):
                t=j/30
                angle=t*math.tau*1.58+n*.8
                radius=.018+(r-.018)*t
                xx,yy=math.cos(angle),math.sin(angle)
                sections.append([(cx+(radius+thick)*xx,cy+(radius+thick)*yy*.82,
                                  z+.013*math.sin(2*angle+index)) for thick,z in
                                 ((-.004,-.142),(.005,-.142),(.005,-.377),(-.004,-.377))])
            loft(cloth,sections,swatch)
        flap=[]
        for j in range(16):
            t=j/15
            xx=-.216+t*.389
            yy=-.078+.035*math.sin(t*7)+.018*math.cos(t*13+index)
            z=-.156-.021*math.sin(t*8)
            flap.append([(xx,yy-.009,z),(xx,yy+.002,z+.011),(xx,yy+.008,z-.054),
                         (xx,yy-.005,z-.155),(xx,yy-.017,z-.155),(xx,yy-.003,z-.054)])
        loft(cloth,flap,port.ICE)
        emit(cloth,"DrumCloth"+"AB"[index],pivot,material)
    emit(casing,"LaundryMachines",root,material)
    emit(fittings,"LaundryMachineFittings",root,material)
    emit(glass,"LaundryMachineGlass",root,material)
    emit(coll,"COL_Machines",root,material)


def loft(g, rings, color, closed=False):
    count = len(rings[0])
    vertices = [point for ring in rings for point in ring]
    faces = []
    for index in range(len(rings) if closed else len(rings)-1):
        start, end = index*count, ((index+1)%len(rings))*count
        faces.extend((start+j,start+(j+1)%count,end+(j+1)%count,end+j) for j in range(count))
    if not closed:
        faces.extend((tuple(reversed(range(count))),tuple(range(len(vertices)-count,len(vertices)))))
    g.add(vertices,faces,color)


def outline(x,z,w,d,cut):
    a,b = w/2,d/2
    return [(x+i,z+j) for i,j in ((-a+cut,-b),(a-cut,-b),(a,-b+cut),(a,b-cut),
            (a-cut,b),(-a+cut,b),(-a,b-cut),(-a,-b+cut))]


def textile(g, x,z,width,depth,y,thickness,color, left_hem=True,right_hem=True, fullness=.009):
    # One continuous woven panel; the flat inner ends seat at the cloth hinges.
    xs=(0,.045,.14,.34,.54,.76,.94,1)
    zs=(0,.045,.19,.42,.65,.89,.964,1)
    top=[]
    for u in xs:
        for v in zs:
            fall=math.sin(math.pi*u)
            ripple=fullness*fall*(.46*math.sin(9*v+4*u)+.37*math.sin(17*u-3*v)+.42)
            hem = .0028*fall if v in (.045,.964) else 0
            if (left_hem and u==.045) or (right_hem and u==.94): hem+=.0023
            top.append((x+(u-.5)*width,y+ripple+hem,z+(v-.5)*depth+.004*fall*math.sin(7*u)))
    count=len(top)
    points=top+[(px,py-thickness,pz) for px,py,pz in top]
    n=len(zs)
    faces=[]
    for i in range(len(xs)-1):
        for j in range(n-1):
            t=i*n+j
            face=(t,t+1,t+n+1,t+n)
            faces.extend((face,tuple(count+k for k in reversed(face))))
    edge=[i*n for i in range(len(xs))]+list(range(count-n+1,count))+[i*n+n-1 for i in reversed(range(len(xs)-1))]+list(reversed(range(1,n-1)))
    faces.extend((a,b,b+count,a+count) for a,b in zip(edge,edge[1:]+edge[:1]))
    g.add(points,faces,color)


def handle_axis(side):
    # Splayed handholds let the knuckles continue the forearm on each side of the load.
    return Vector((side*.8660254,0,.5))


def basket_geometry():
    basket=geometry("Steel")
    base=outline(0,0,.56,.37,.037)
    lip=outline(0,0,.62,.43,.045)
    loft(basket,[[(x,y,z) for x,z in base] for y in (-.19,-.17)],port.PAINT)
    for y in (-.124,-.010,.134):
        t=(y+.17)/.32
        loop=[(a+(x-a)*t,b+(z-b)*t) for (a,b),(x,z) in zip(base,lip)]
        loft(basket,[[(x*k,y+dy,z*k) for x,z in loop]
                     for k,dy in ((1,-.011),(1.008,-.007),(1.008,.008),(1,.014),(.968,.012),(.968,-.010))],port.PAINT,True)
    for i in range(8):
        for t in ((.22,.5,.78) if i%2==0 else (.5,)):
            def along(loop):
                a,b=loop[i],loop[(i+1)%8]
                return tuple((1-t)*u+t*v for u,v in zip(a,b))
            x,z=along(base); xx,zz=along(lip)
            basket.rod((x,-.17,z),(xx,.147,zz),.007,port.METAL,6)
    for side in (-1,1):
        centre=Vector((.33*side,.20,0))
        axis=handle_axis(side)
        for end in (-1,1):
            z=.10*end
            box(basket,(side*.307,.108,z),(.016,.064,.046),port.PAINT,.004)
            basket.rod((side*.301,.113,z),(side*.325,.113,z),.013,port.EDGE,8)
            shoulder=centre+axis*(.075*end)
            knee=Vector((shoulder.x,.164,z*.92))
            basket.rod((side*.319,.12,z),knee,.013,port.METAL,8)
            basket.rod(knee,shoulder,.013,port.METAL,8)
        basket.rod(centre-axis*.075,centre+axis*.075,.017,port.EDGE,12)
        basket.rod(centre-axis*.060,centre+axis*.060,.022,port.CABIN,12)
    return basket


def work_fittings(root, material):
    wood, steel, coll, fabric = geometry("Timber"), geometry("SteelDark"), geometry(), geometry("Fabric")
    for i in range(5):
        box(wood,(-2.65,.927,-.280+i*.24),(1.7,.066,.239),port.WOOD,.005)
    for z in (-.30,.70):
        box(wood,(-2.65,.849,z),(1.55,.088,.044),port.WOOD,.006)
    for x in (-3.36, -1.94):
        for z in (-.27, .67):
            box(steel, (x, .44, z), (.065, .88, .065), port.METAL)
    for x in (-3.36,-1.94):
        steel.rod((x,.23,-.27),(x,.23,.67),.014,port.METAL)
        for z in (-.27,.67):
            steel.rod((x,.002,z),(x,.028,z),.042,port.DARK,8)
    box(coll, (-2.65, .48, .20), (1.7, .96, 1.20), port.METAL)
    # The storage sits behind the table, so it never becomes a lane obstacle.
    for y in (.35, 1.2, 2.05):
        box(wood, (-3.42, y, -1.45), (.75, .055, 1.6), port.WOOD)
        for z in (-2.11,-.79):
            box(steel,(-3.42,y-.043,z),(.69,.034,.044),port.METAL,.004)
            steel.rod((-3.72,y-.18,z),(-3.43,y-.051,z),.013,port.METAL,6)
    for x in (-3.74, -3.1):
        for z in (-2.16, -.74):
            box(steel, (x, 1.05, z), (.055, 2.1, .055), port.METAL)
    box(coll, (-3.42, 1.07, -1.45), (.78, 2.14, 1.65), port.METAL)
    for y, z in ((.42, -1.8), (.43, -.98), (1.28, -1.85), (2.13, -1.1)):
        for i in range(2):
            loft(fabric,[[(x,yy,zp) for x,zp in outline(-3.42+.011*i,z,.54*k,.43*k,.024)] for k,yy in
                        ((.94,y-.027+i*.051),(1,y-.020+i*.051),(1,y+.012+i*.051),(.94,y+.022+i*.051))],
                        port.CABIN if i else port.ROPE)
    # The outdoor trolley is a stable receipt surface, not a second inventory.
    for i in range(4):
        box(wood,(3,.805,.825+i*.183),(.92,.05,.181),port.WOOD,.005)
    for x in (2.60,3.4):
        box(steel,(x,.746,1.1),(.04,.068,.73),port.METAL,.006)
    for z in (.758,1.443):
        box(steel,(3,.748,z),(.85,.061,.031),port.METAL,.005)
    box(coll, (3, .415, 1.1), (.92, .83, .75), port.WOOD)
    for x in (2.64, 3.36):
        for z in (.82, 1.38):
            steel.rod((x,.349,z),(x,.776,z),.023,port.METAL,8)
            for side in (-1,1):
                box(steel,(x+.051*side,.251,z),(.018,.181,.048),port.METAL,.005)
            steel.rod((x-.074,.17,z),(x+.074,.17,z),.024,port.EDGE,10)
            steel.rod((x-.049,.17,z),(x+.049,.17,z),.092,port.EDGE,12)
            box(steel,(x,.347,z),(.123,.029,.064),port.METAL,.004)
    for x in (2.63, 3.37):
        steel.rod((x, .43, .76), (x, .97, .62), .025, port.METAL)
    steel.rod((2.63, .97, .62), (3.37, .97, .62), .025, port.METAL)
    emit(wood, "LaundryFurniture", root, material)
    emit(steel, "LaundryFurnitureIron", root, material)
    emit(fabric, "LaundryStoredCloth", root, material)
    emit(coll, "COL_Furniture", root, material)
    pivot = port.empty("MOVE_CarryBasket", root, ANCHORS["ANCHOR_BasketRest"])
    emit(basket_geometry(), "CarryBasket", pivot, material)
    for side,label in ((-1,"Left"),(1,"Right")):
        centre=Vector((.33*side,.20,0))
        port.empty("ANCHOR_Basket"+label,pivot,centre)
        port.empty("ANCHOR_HandleAxis"+label,pivot,centre+.055*handle_axis(side))
    pivot = port.empty("MOVE_ClothA", root, ANCHORS["ANCHOR_ClothRest"])
    cloth = geometry("Fabric")
    # Folded stock inside the basket supports the piece being sorted.
    for i in range(7):
        w,d=.42+i*.008,.30+i*.007
        loft(cloth,[[(x,yy,z) for x,z in outline(0,0,w*k,d*k,.026)] for k,yy in
                    ((.94,-.367+i*.052),(1,-.356+i*.052),(1,-.327+i*.052),(.96,-.315+i*.052))],
                    (port.CABIN,port.ROPE,port.ICE)[i%3])
    textile(cloth,0,0,.264,.35,0,.006,port.ICE,False,False)
    emit(cloth, "WorkCloth", pivot, material)
    for label,side in (("Left",-1),("Right",1)):
        hinge=port.empty("MOVE_Cloth"+label,pivot,(side*.132,0,0))
        leaf=geometry("Fabric")
        textile(leaf,side*.064,0,.128,.35,0,.006,port.ICE,side<0,side>0)
        emit(leaf,"WorkCloth"+label,hinge,material)
        port.empty("ANCHOR_Hem"+label,hinge,(side*.121,.001,.109 if side<0 else .065))


def service_details(root, material):
    steel = geometry("SteelDark")
    # Ventilator, weather cap, continuous machine inlet and drain connections.
    steel.rod((2.7, 3.45, -1.65), (2.7, 4.05, -1.65), .19, port.METAL, 12)
    steel.rod((2.7, 4.14, -1.65), (2.7, 4.20, -1.65), .32, port.METAL, 12)
    for dx in (-.16, .16):
        steel.rod((2.7+dx, 4, -1.65), (2.7+dx, 4.16, -1.65), .014, port.METAL)
    for x in (-.8, .5):
        steel.rod((x, .40, -2.7), (x, .40, -2.82), .04, port.METAL)
        steel.rod((x, .40, -2.82), (x, .12, -2.82), .04, port.METAL)
    steel.rod((-.8, .12, -2.82), (1.0, .12, -2.82), .045, port.METAL)
    steel.rod((1.0, .12, -2.82), (1.0, .12, -3.12), .045, port.METAL)
    box(steel, (-2.5, 3.28, .25), (.66, .08, .20), port.METAL)
    box(steel, (-.15, 3.28, -1.3), (.80, .08, .20), port.METAL)
    box(steel, (2.6, 2.73, 1.1), (.55, .08, .20), port.METAL)
    box(steel, (-2.65, 3.40, 3.26), (.55, .06, .07), port.METAL)
    # A modest enamel plate above the original front door, within the same
    # facade envelope. Its graphic owns unique full-face UVs, never tile UVs.
    box(steel, (-2.65, 3.04, 3.155), (2.4, .76, .055), port.METAL, .015)
    for x in (-3.78,-1.52):
        for y in (2.711,3.37):
            steel.rod((x,y,3.18),(x,y,3.198),.014,port.EDGE,8)
    for z in (-2.8, 2.8):
        box(steel, (1.42, 2.75, z), (.10, .10, .25), port.METAL)
    emit(steel, "LaundryServicePipesAndLightBody", root, material)
    for suffix, position, size in (
            ("Work", (-2.5, 3.215, .25), (.56, .025, .14)),
            ("Machines", (-.15, 3.215, -1.3), (.70, .025, .14)),
            ("Court", (2.6, 2.675, 1.1), (.45, .025, .14)),
            ("Entrance", (-2.65, 3.365, 3.26), (.44, .018, .06))):
        lens = geometry()
        box(lens, position, size, port.LAMP, .004)
        emit(lens, "LaundryLampLens"+suffix, root, material)
    artwork = geometry("Sign")
    artwork.add([(-3.76, 2.67, 3.19), (-1.54, 2.67, 3.19),
                 (-1.54, 3.41, 3.19), (-3.76, 3.41, 3.19)],
                [(0, 1, 2, 3)], (1, 1, 1, 1), [(1, 0), (0, 0), (0, 1), (1, 1)], solid=False)
    artwork.object("LaundrySignArtwork__Sign", root, material)
    rubber = geometry("Rubber")
    for x in (2.64, 3.36):
        for z in (.82, 1.38):
            loft(rubber,[[(x+dx,.17+r*math.cos(i*math.tau/16),z+r*math.sin(i*math.tau/16)) for i in range(16)]
                         for dx,r in ((-.041,.087),(-.042,.153),(-.029,.170),(.029,.170),(.042,.153),(.041,.087))],port.DARK,True)
    emit(rubber, "LaundryRubberFittings", root, material)


def build():
    material = port.base.material("LaundryVertexPaint")
    root = port.empty("StaticLaundry")
    shell(root, material)
    machines(root, material)
    work_fittings(root, material)
    service_details(root, material)
    for name, point in ANCHORS.items():
        port.empty(name, root, point)
    bpy.context.view_layer.update()
    return root


def manifest(root):
    measured = port.describe(root)
    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    renderers = [o for o in meshes if not o.name.startswith("COL_")]
    colliders = [o for o in meshes if o.name.startswith("COL_")]
    if measured["triangles"] > MAX_TRIANGLES or len(renderers) > MAX_RENDERERS:
        raise RuntimeError("Laundry geometry exceeds its bounded triangle/renderer budget")
    for key, wanted in (("bounds_min", BOUND_MIN), ("bounds_max", BOUND_MAX)):
        if any(abs(actual-expected) > .001 for actual, expected in zip(measured[key], wanted)):
            raise RuntimeError(f"Laundry fixed envelope mismatch: {key}={measured[key]}, expected {wanted}")
    for o in renderers:
        role = o.name.rsplit("__", 1)[-1].split(".")[0] if "__" in o.name else "Plain"
        if role not in SURFACES:
            raise RuntimeError("Unknown shared laundry surface: "+role)
        if not o.data.uv_layers or not o.data.color_attributes:
            raise RuntimeError("Laundry mesh lacks metre UVs or retained vertex paint")
    # A real window cannot silently be patched with an opaque wall by later edits.
    wall = next(o for o in colliders if o.name == "COL_Walls")
    for x, y, z in ((-2.8, 1.2, 3), (-.25, 1.7, 3), (1.5, 1.2, 1.1)):
        origin = Vector(port.source((x, y, z-.4)))
        direction = Vector(port.source((0, 0, 1)))
        if x == 1.5:
            origin = Vector(port.source((x-.4, y, z)))
            direction = Vector(port.source((1, 0, 0)))
        if wall.ray_cast(origin, direction, distance=.8)[0]:
            raise RuntimeError("Laundry door/window aperture was sealed")
    fascia=next(o for o in renderers if o.name.startswith("LaundryMachines__"))
    for cx in (-.8,.5):
        for dx,dy in ((-.48,-.41),(.48,-.41),(-.495,.305),(.495,.305),(0,-.44),(.48,0),(-.48,0)):
            if not fascia.ray_cast(Vector(port.source((cx+dx,.78+dy,-1.44))),
                                   Vector(port.source((0,0,-1))),distance=.20)[0]:
                raise ValueError("Missing sheet metal around the front-loading door")
        if fascia.ray_cast(Vector(port.source((cx,.78,-1.44))),Vector(port.source((0,0,-1))),distance=.24)[0]:
            raise ValueError("Front fascia covers the moving drum")
    closed_door = next(o for o in colliders if o.name == "COL_Door")
    if not closed_door.ray_cast(Vector(port.source((-2.8, 1.2, 2.7))),
                                Vector(port.source((0, 0, 1))), distance=.6)[0]:
        raise RuntimeError("Laundry front leaf must physically close its authored opening")
    return {
        "design_id": DESIGN_ID, "version": VERSION,
        "generator": Path(__file__).name,
        "sign_resource": "City/Laundry/LaundrySign",
        "sign_source_sha256": hashlib.sha256((MODEL_DIR / "LaundrySign.png").read_bytes()).hexdigest(),
        "sign_text": "ПРАЧЕЧНАЯ", "sign_uv": "unique full face 0..1",
        "coordinate_system": "Unity metres +Y up/+Z frontage; floor Y=0; swap YZ/reverse winding for Blender",
        "geometry_signature_includes": ["vertices", "faces", "colors", "uv0", "transforms"],
        "bounds_min": measured["bounds_min"], "bounds_max": measured["bounds_max"],
        "body_size": [8, 3.65, 6], "main_room_bounds": [-4, -3, 1.5, 3],
        "service_court_bounds": [1.5, 0, 4, 3],
        "floor_walk_bounds": [-4.3, -3.3, 4.3, 3.3],
        "solid_footprints": [{"id": name, "rect_xz": list(rect)} for name, rect in SOLID_FOOTPRINTS],
        "front_door_closed": True, "entry_side": "east service court",
        "public_door_width": 1.2, "service_door_width": 1.6,
        "floor_height": 0, "table_height": .96,
        "triangles": measured["triangles"], "maximum_triangles": MAX_TRIANGLES,
        "renderer_count": len(renderers), "maximum_renderers": MAX_RENDERERS,
        "geometry_signature": measured["geometry_signature"],
        "semantic_uv_tiles_metres": {r: port.SURFACE_TILES.get(r, 0) for r in SURFACES},
        "anchors": measured["anchors"],
        "moving_parts": ["MOVE_DrumA", "MOVE_DrumB", "MOVE_CarryBasket", "MOVE_ClothA",
                         "MOVE_ClothLeft", "MOVE_ClothRight"],
        "collider_parts": [o.name for o in colliders],
        "material_roles": sorted({o.name.rsplit("__", 1)[-1] if "__" in o.name else "Plain" for o in renderers}),
        "assets": ["StaticLaundry.fbx"],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=MODEL_DIR)
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    port.base.reset()
    root = build()
    result = manifest(root)
    maps=finishes.maps()
    result["finish_sha256"]={k:hashlib.sha256(v).hexdigest() for k,v in maps.items()}
    if args.validate_only:
        saved = json.loads((args.model_dir / "CityLaundry3D.json").read_text())
        if saved != result or any((args.model_dir/(k+".png")).read_bytes()!=v for k,v in maps.items()):
            raise RuntimeError("Laundry authored contract differs from the committed manifest")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        args.source_dir.mkdir(parents=True, exist_ok=True)
        for key,data in maps.items(): (args.model_dir/(key+".png")).write_bytes(data)
        port.base.export(root, args.model_dir / "StaticLaundry.fbx")
        (args.model_dir / "CityLaundry3D.json").write_text(json.dumps(result, indent=2)+"\n")
        port.source_surface_materials([root])
        for key,suffix in (("LaundryFittings","__LaundryFinish"),("LaundryFloor","LaundryFloor__Concrete")):
            review=bpy.data.materials.new(key);review.use_nodes=True
            nodes=review.node_tree.nodes;links=review.node_tree.links
            paint=nodes.new("ShaderNodeTexImage")
            paint.image=bpy.data.images.load(str(args.model_dir/(key+"Albedo.png")))
            paint.image.pack()
            shader=nodes.get("Principled BSDF")
            links.new(paint.outputs["Color"],shader.inputs["Base Color"])
            shader.inputs["Roughness"].default_value=.73
            for part in root.children_recursive:
                if part.type=="MESH" and part.name.endswith(suffix):part.data.materials[0]=review
        sign_mat = bpy.data.materials.new("LaundrySignReview")
        sign_mat.use_nodes = True
        sign_shader = sign_mat.node_tree.nodes.get("Principled BSDF")
        sign_tex = sign_mat.node_tree.nodes.new("ShaderNodeTexImage")
        sign_tex.image = bpy.data.images.load(str(MODEL_DIR / "LaundrySign.png"), check_existing=True)
        sign_tex.image.pack()
        sign_mat.node_tree.links.new(sign_tex.outputs["Color"], sign_shader.inputs["Base Color"])
        sign_shader.inputs["Roughness"].default_value = .72
        next(o for o in root.children_recursive if o.name == "LaundrySignArtwork__Sign").data.materials[0] = sign_mat
        # Source glass is merely a review material; Unity binds an existing
        # shared City material by the __Glass role and creates none per instance.
        for mesh in [o for o in root.children_recursive if o.type == "MESH" and o.name.endswith("__Glass")]:
            glass = bpy.data.materials.get("LaundryReviewGlass")
            if glass is None:
                glass = bpy.data.materials.new("LaundryReviewGlass")
                glass.use_nodes = True
                shader = glass.node_tree.nodes.get("Principled BSDF")
                shader.inputs["Base Color"].default_value = (.35, .45, .43, 1)
                shader.inputs["Roughness"].default_value = .18
                shader.inputs["Transmission Weight"].default_value = 1
                shader.inputs["IOR"].default_value = 1.45
            mesh.data.materials[0] = glass
        for mesh in [o for o in root.children_recursive if o.type == "MESH" and o.name.startswith("COL_")]:
            mesh.hide_render = True
            mesh.hide_set(True)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir / "CityLaundry3D.blend"), check_existing=False)
    for mesh in bpy.data.objects:
        mesh.hide_set(False)
    port.base.reset()
    repeated = manifest(build())
    repeated["finish_sha256"]=result["finish_sha256"]
    if repeated != result:
        raise RuntimeError("Laundry deterministic rebuild mismatch")
    print("CITY LAUNDRY ART CONTRACT OK: fixed metres, real openings/collision, finite props, shared surfaces and deterministic geometry", flush=True)
    print(json.dumps({k: result[k] for k in ("bounds_min", "bounds_max", "triangles", "renderer_count", "geometry_signature")}))


if __name__ == "__main__":
    main()
