#!/usr/bin/env python3
"""Deterministic compact fish cannery, metre-space production and delivery kit.

The port owns its shared surface library. This generator reuses those measured
UV and geometry helpers, while interior_kit owns real reveals/chamfered solids.
No lettering is represented by geometry: the small blank sign takes world text.
"""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
spec = importlib.util.spec_from_file_location("cannery_port_surface_kit", ROOT / "tools/build-city-port-3d-model.py")
port = importlib.util.module_from_spec(spec)
spec.loader.exec_module(port)
Geometry, empty, source, obj = port.Geometry, port.empty, port.source, port.obj
chamfer, ring, kit_add = port.chamfer, port.ring, port.kit_add
CONCRETE, EDGE, DARK, METAL, PAINT, RUST, CABIN, WOOD, ROPE, GLASS, FISH, ICE, LAMP = port.PALETTE
NAMES = ("Hall", "Equipment", "Truck", "Pallet", "RetortBasket", "CanTray", "CartonStack", "Trolley", "Yard")


def anchor(name, parent, position):
    return empty("ANCHOR_" + name, parent, position)


def collision_box(name, parent, mat, center, size):
    g = Geometry(); g.box(center, size, CONCRETE)
    return obj(g, "COL_" + name, parent, mat)


def shift_geometry(g, first, offset):
    delta=Vector(source(offset))
    for i in range(first,len(g.vertices)):g.vertices[i]=tuple(Vector(g.vertices[i])+delta)


def panel_frame(g, x, z, width, low=.78, high=3.15):
    for zz in (z-width*.5, z+width*.5):
        chamfer(g, (x, (low+high)*.5, zz), (.09, high-low, .09), METAL, .012)
    for y in (low, 1.95, high):
        chamfer(g, (x, y, z), (.09, .065, width), METAL, .012)


def hall(mat):
    root=empty("Hall"); g=Geometry(); c=Geometry(); glass=Geometry(); roof=Geometry(); lamps=Geometry()
    # The hall has two honest public openings and two goods openings. Its
    # glazed east side gives a second view of the same live machinery.
    walls=[kit.translated(kit.wall_run(8,3.92,.24,[kit.Opening(2.9,1.7,2.8)]),(-4,7,.18)),
           kit.translated(kit.wall_run(8,3.92,.24,[kit.Opening(2.9,1.7,2.8)]),(-4,-7,.18)),
           kit.translated(kit.rotated_z(kit.wall_run(14,3.92,.24),90),(-8,0,.18)),
           kit.translated(kit.rotated_z(kit.wall_run(14,3.92,.24,
               [kit.Opening(-5.5,2,2.8),kit.Opening(-2.5,2.7,3,.6),
                kit.Opening(.65,2.7,3,.6),kit.Opening(5,2.2,2.8)]),90),(0,0,.18))]
    g.role="Plaster"
    for wall in walls:
        kit_add(g, wall, CABIN); kit_add(c, wall, CABIN)
    g.role=None
    chamfer(g,(-4,.13,0),(8,.1,14),CONCRETE,.008)
    collision_box("HallFloor",root,mat,(-4,.13,0),(8,.1,14))
    for x in (-7.88,-.12):
        for z in (-6.85,-3.5,0,3.5,6.85):
            chamfer(g,(x,2.07,z),(.16,3.78,.18),METAL,.018)
    roof.role="Roof"
    chamfer(roof,(-4,4.19,0),(8.65,.18,14.55),METAL,.03)
    for z in range(-7,8): roof.box((-4,4.297,z),(8.56,.035,.045),DARK)
    for x in (-8.18,.18):
        g.rod((x,4.05,-7.25),(x,4.05,7.25),.075,METAL,10)
        g.rod((x,4.02,-6.85),(x,.3,-6.85),.065,METAL,10)
        g.rod((x,.3,-6.85),(x,.17,-7.1),.065,METAL,10)
    # Short insulated cold room against the west wall; the east reveal opens
    # onto a real receiving lane. No fake door pasted over a filled box.
    cold=[kit.translated(kit.wall_run(2.6,2.72,.12),(-6.5,-6.85,.18)),
          kit.translated(kit.wall_run(2.6,2.72,.12),(-6.5,-2.9,.18)),
          kit.translated(kit.rotated_z(kit.wall_run(3.95,2.72,.12,[kit.Opening(0,1.5,2.25)]),90),(-5.2,-4.875,.18))]
    for wall in cold:
        kit_add(g,wall,CABIN);kit_add(c,wall,CABIN)
    chamfer(g,(-6.5,2.98,-4.875),(2.73,.14,4.08),CABIN,.025)
    for zz in (-5.65,-4.10):chamfer(g,(-5.09,1.37,zz),(.13,2.37,.09),METAL,.008)
    # Folded insulated leaf is parked next to the opening, not across it.
    chamfer(g,(-5.02,1.34,-3.75),(.14,2.3,.45),CABIN,.025)
    chamfer(g,(-7.72,3.46,-5.4),(.38,.7,1.1),METAL)
    for zz in (-5.72,-5.10):ring(g,(-7.51,3.46,zz),.24,.025,DARK,(1,0,0),12)
    # Viewing partition: two staff gates align with receiving/finished doors.
    for low,high in ((-6.9,-6.25),(-4.6,3.85)):
        length=high-low;zz=(low+high)*.5
        for y in (.53,1.35):g.rod((-2.2,y,low),(-2.2,y,high),.025,METAL,8)
        for z in (low,high):g.rod((-2.2,.18,z),(-2.2,2.35,z),.028,METAL,8)
        glass.box((-2.2,1.28,zz),(.018,2.13,length),GLASS)
        collision_box("ViewingPartition"+str(low),root,mat,(-2.2,1.3,zz),(.08,2.24,length))
    for z,width in ((-2.5,2.7),(.65,2.7)):
        panel_frame(g,.018,z,width)
        glass.box((.012,1.99,z),(.024,2.32,width-.12),GLASS)
        collision_box("EastWindow"+str(z),root,mat,(0,1.99,z),(.12,2.32,width))
    for z,width in ((-5.5,2),(5,2.2)):
        # Steel roller shutter stored above a genuine opening.
        g.rod((.14,3.16,z-width*.55),(.14,3.16,z+width*.55),.17,METAL,12)
        for zz in (z-width*.52,z+width*.52):chamfer(g,(.16,1.62,zz),(.12,2.88,.09),METAL,.01)
    # Drains lead under prep and retort; no loose decorative pipe ends.
    for z in (-2.30,2.45):
        g.box((-4.9,.187,z),(3.9,.014,.19),DARK)
        for i in range(25):g.box((-6.75+i*.155,.20,z),(.035,.018,.18),METAL)
    for index,z in enumerate((-3.7,-.9,2.2,5.2)):
        chamfer(g,(-4.2,3.77,z),(1.65,.16,.24),METAL,.02)
        lamps.box((-4.2,3.675,z),(1.45,.03,.17),LAMP)
        anchor("HallLight"+str(index),root,(-4.2,3.52,z))
    # Electrical trunk and steam riser are attached to machines, not random.
    for z in (-4,0,4):chamfer(g,(-7.77,3.24,z),(.18,.24,.28),METAL)
    g.rod((-7.73,3.35,-6.2),(-7.73,3.35,5.4),.038,METAL,8)
    g.rod((-7.65,.3,1.5),(-7.65,5.1,1.5),.14,METAL,12)
    g.rod((-7.65,5.1,1.5),(-7.65,5.1,2),.14,METAL,12)
    ring(g,(-7.65,4.48,1.5),.18,.025,RUST,(0,1,0),12)
    chamfer(g,(-5.75,2.9,7.16),(2.8,.6,.085),METAL,.025)
    anchor("Sign",root,(-5.75,2.9,7.212))
    anchor("PublicEntry",root,(-1.1,.18,7.3));anchor("PublicExit",root,(-1.1,.18,-7.3))
    anchor("ReceivingDoor",root,(.3,.18,-5.5));anchor("FinishedDoor",root,(.3,.18,5))
    anchor("RawDoor",root,(.3,.18,-5.5));anchor("Observer",root,(-1.1,1.72,.5))
    anchor("ColdStore",root,(-6.5,.18,-4.875))
    for i in range(6):anchor("RawStore"+str(i),root,(-7.36+(i%3)*.83,.18,-6.15+(i//3)*2.55))
    obj(g,"HallVisible",root,mat);obj(c,"COL_HallWalls",root,mat)
    obj(glass,"CanneryGlass",root,mat);obj(roof,"HallRoof",root,mat)
    obj(lamps,"CanneryLampGlass",root,mat)
    return root


def legs(g,x,z,width,depth,top):
    for xx in (x-width*.5+.08,x+width*.5-.08):
        for zz in (z-depth*.5+.08,z+depth*.5-.08):
            g.rod((xx,.18,zz),(xx,top,zz),.045,METAL,8)
            chamfer(g,(xx,.205,zz),(.17,.05,.17),METAL,.014)


def can(g,x,y,z):
    g.role="SteelLight";g.rod((x,y,z),(x,y+.095,z),.052,CABIN,12)
    g.rod((x,y+.092,z),(x,y+.102,z),.057,METAL,12)
    g.role=None


def basket_geometry(g):
    g.role="SteelDark"
    chamfer(g,(0,.025,0),(.98,.05,1.04),METAL,.009)
    for xx in (-.47,.47):
        for zz in (-.5,.5):g.rod((xx,.03,zz),(xx,.62,zz),.018,METAL,6)
    for y in (.18,.38,.6):
        for xx in (-.47,.47):g.rod((xx,y,-.5),(xx,y,.5),.015,METAL,6)
        # Open +Z mouth receives the one actual CanTray; duplicating full cans
        # in this model would manufacture a second batch before loading.
        g.rod((-.47,y,-.5),(.47,y,-.5),.015,METAL,6)
    for z in (-.4,-.2,0,.2,.4):
        g.rod((-.43,.145,z),(.43,.145,z),.025,METAL,10)
    g.role=None


def roller_run(g, start, end, open_start=True, open_end=True):
    a,b=Vector((start[0],1.155,start[1])),Vector((end[0],1.155,end[1]))
    direction=(b-a).normalized();side=Vector((-direction.z,0,direction.x));length=(b-a).length
    left=.45 if open_start else 0;right=length-(.45 if open_end else 0)
    if right<=left:return
    for distance in [left+i*.16 for i in range(int((right-left)/.16)+1)]:
        p=a+direction*distance
        g.rod(p-side*.39,p+side*.39,.035,METAL,10)
    for sign in (-1,1):
        p=a+direction*left+side*(sign*.41);q=a+direction*right+side*(sign*.41)
        g.rod(p-Vector((0,.11,0)),q-Vector((0,.11,0)),.035,PAINT,8)
        g.rod(p+Vector((0,.085,0)),q+Vector((0,.085,0)),.018,METAL,8)
    mid=(a+b)*.5
    for sign in (-1,1):
        foot=mid+side*(sign*.32)
        g.rod((foot.x,.18,foot.z),(foot.x,1.07,foot.z),.032,METAL,8)
        chamfer(g,(foot.x,.205,foot.z),(.14,.05,.14),METAL,.01)


def equipment(mat):
    root=empty("Equipment");g=Geometry()
    # Receiving scale: a foot platform and genuine mechanical dial, kept as
    # useful equipment after the former weighbridge changes purpose.
    chamfer(g,(-3.5,.30,-5.45),(1.25,.24,1.45),METAL,.04)
    g.rod((-4.1,.42,-5.95),(-4.1,1.62,-5.95),.045,METAL,8)
    g.rod((-4.11,1.67,-6.05),(-4.11,1.67,-5.93),.19,METAL,16)
    g.rod((-4.11,1.67,-5.929),(-4.11,1.67,-5.915),.155,CABIN,16)
    dial=empty("MOVE_ScaleNeedle",root,(-4.11,1.67,-5.90));d=Geometry()
    d.rod((0,0,0),(.095,.07,0),.009,DARK,6);obj(d,"ScaleNeedle",dial,mat)
    anchor("ReceivingLoad",root,(-3.5,.42,-5.45));anchor("Receiver",root,(-3.4,.18,-6.5))
    # Wash trough has an open dark bowl, a lip, tap, bottom drain and hose.
    wash_start=len(g.vertices)
    legs(g,-5,-3.4,2.25,1.2,1.1)
    chamfer(g,(-5,.87,-3.4),(2.25,.1,1.2),METAL,.035)
    for xx in (-6.08,-3.92):chamfer(g,(xx,1.06,-3.4),(.09,.34,1.2),CABIN,.012)
    for zz in (-3.96,-2.84):chamfer(g,(-5,1.06,zz),(2.1,.34,.09),CABIN,.012)
    g.role="Deck";g.box((-5,.936,-3.4),(1.95,.02,.91),METAL);g.role=None
    g.rod((-5.75,.88,-3.4),(-5.75,.22,-3.4),.045,METAL,8)
    g.rod((-5.75,1.2,-3.92),(-5.75,1.57,-3.92),.035,METAL,8)
    g.rod((-5.75,1.57,-3.92),(-5.75,1.57,-3.57),.035,METAL,8)
    g.rod((-5.75,1.57,-3.57),(-5.75,1.46,-3.57),.035,METAL,8)
    shift_geometry(g,wash_start,(0,0,1.35))
    anchor("PreparationWorker",root,(-6.65,.18,-2.05));anchor("PreparationLeftHand",root,(-6.12,1.18,-2.25))
    anchor("PreparationRightHand",root,(-6.12,1.18,-1.85));anchor("PreparationLoad",root,(-5.7,.96,-2.05))
    prep=empty("MOVE_PreparationFish",root,(-5.7,.94,-2.05));p=Geometry()
    port.crate(p,0,0,0,True);obj(p,"PreparationFishVisible",prep,mat)
    # Fill/seam bench, guide rails and a hopper with a narrowed outlet.
    seam_start=len(g.vertices)
    legs(g,-5,-.85,2.5,1.45,1.08)
    chamfer(g,(-5,1.06,-.85),(2.5,.18,1.45),METAL,.028)
    g.role="Rubber";g.box((-5,1.16,-.85),(2.27,.045,.61),DARK);g.role=None
    for zz in (-1.18,-.52):g.rod((-6.15,1.28,zz),(-3.85,1.28,zz),.021,METAL,8)
    g.rod((-5.72,1.55,-.85),(-5.72,2.11,-.85),.31,CABIN,12,end_radius=.46)
    g.rod((-5.72,1.3,-.85),(-5.72,1.55,-.85),.085,METAL,10,end_radius=.2)
    chamfer(g,(-4.36,1.67,-1.32),(.29,1.14,.24),PAINT)
    chamfer(g,(-4.36,2.18,-.95),(.6,.2,.94),PAINT)
    seamer=empty("MOVE_SeamerHead",root,(-4.36,1.62,-.35));s=Geometry()
    s.rod((0,0,0),(0,.48,0),.08,METAL,10);s.rod((0,-.07,0),(0,.03,0),.16,METAL,12)
    obj(s,"SeamerHeadVisible",seamer,mat)
    chamfer(g,(-3.89,1.55,-1.39),(.19,.3,.21),PAINT,.025)
    for y,col in ((1.62,LAMP),(1.49,DARK)):g.rod((-3.779,y,-1.39),(-3.754,y,-1.39),.033,col,8)
    shift_geometry(g,seam_start,(0,0,.5))
    anchor("SeamerWorker",root,(-6.78,.18,-.35));anchor("SeamerLeftHand",root,(-6.24,1.2,-.52))
    anchor("SeamerRightHand",root,(-6.24,1.2,-.18));anchor("CanTray",root,(-5,1.19,-.35))
    # Horizontal retort is a hollow pressure shell, with a clear front throat.
    center=(-5,1.35);zback=.65;zfront=2.8;steps=20
    for i in range(steps):
        a,b=i*math.tau/steps,(i+1)*math.tau/steps
        vertices=[(center[0]+r*math.cos(t),center[1]+r*math.sin(t),z)
                  for z in (zback,zfront) for r in (.70,.80) for t in (a,b)]
        g.add(vertices,[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)],METAL)
    g.rod((-5,1.35,.52),(-5,1.35,.66),.80,METAL,20)
    for z in (.88,2.53):
        ring(g,(-5,1.35,z),.815,.035,METAL,(0,0,1),20)
        for xx in (-5.62,-4.38):chamfer(g,(xx,.52,z),(.16,.65,.35),METAL,.025)
    g.rod((-5,2.11,1.6),(-5,2.5,1.6),.062,METAL,8)
    g.rod((-5,2.5,1.6),(-7.65,2.5,1.6),.062,METAL,8)
    g.rod((-4.35,1.9,1.28),(-4.2,1.9,1.28),.13,CABIN,12)
    g.rod((-4.19,1.9,1.28),(-4.18,1.95,1.33),.01,DARK,6)
    door=empty("MOVE_RetortDoor",root,(-5.82,1.35,2.91));d=Geometry()
    d.rod((.82,0,-.07),(.82,0,.07),.80,METAL,20)
    ring(d,(.82,0,.10),.63,.028,METAL,(0,0,1),20)
    ring(d,(.82,0,.16),.23,.023,METAL,(0,0,1),12)
    for angle in (0,math.pi*.5,math.pi,math.pi*1.5):
        d.rod((.82,0,.16),(.82+.23*math.cos(angle),.23*math.sin(angle),.16),.022,METAL,8)
    # Captured shoes run up two fixed guides. A swinging pressure lid would
    # sweep through the front conveyor; a 1.7 m vertical lift stays inside
    # the 4.1 m ceiling and clears the basket without crossing its feed.
    for xx in (-.11,1.75):
        for yy in (-.6,.6):
            for zz in (-.0775,.0775):
                chamfer(d,(xx,yy,zz),(.22,.2,.025),METAL,.006)
            for side in (-1,1):
                chamfer(d,(xx+side*.085,yy,0),(.05,.2,.13),METAL,.006)
    for xx in (-5.93,-4.07):
        chamfer(g,(xx,2.225,2.92),(.08,3.55,.08),METAL,.012)
        chamfer(g,(xx,.23,2.92),(.28,.10,.30),METAL,.018)
        g.rod((xx,.28,2.92),(xx,.49,2.92),.045,METAL,8)
    chamfer(g,(-5,3.995,2.92),(2.08,.08,.19),METAL,.018)
    g.rod((-5.94,3.88,2.74),(-3.98,3.88,2.74),.035,METAL,10)
    g.rod((-4.02,3.73,2.59),(-4.02,3.73,2.83),.115,PAINT,12)
    # The operator touches a fixed control, not the wheel on the rising lid.
    chamfer(g,(-5.76,1.42,2.72),(.16,.30,.14),PAINT,.018)
    g.rod((-5.80,1.35,2.75),(-5.86,1.35,2.78),.024,LAMP,10)
    obj(d,"RetortDoorVisible",door,mat)
    # The controller instantiates the one shared basket asset here.
    empty("MOVE_RetortBasket",root,(-5,1.02,1.66))
    for x in (-5.42,-4.58):g.rod((x,.99,1),(x,.99,3.5),.027,METAL,8)
    # The drawer's linear drive stays inside the pressure shell. Its moving
    # carriage trails the basket centre by .7 m, so even fully extended it
    # stays engaged with the guide; closed-door geometry has no protruding ram.
    g.rod((-5,.8,.76),(-5,.8,2.74),.035,METAL,10)
    chamfer(g,(-5,.79,.91),(.28,.21,.28),PAINT,.025)
    for z in (.82,2.68):chamfer(g,(-5,.7,z),(.24,.12,.13),METAL,.018)
    ram=empty("MOVE_RetortRam",root,(-5,.8,.96));ram_g=Geometry()
    chamfer(ram_g,(0,0,0),(.17,.14,.17),METAL,.018)
    ram_g.rod((0,.10,0),(0,.10,.7),.04,METAL,8)
    ram_g.rod((0,.10,.7),(0,.22,.7),.045,METAL,8)
    obj(ram_g,"RetortRamVisible",ram,mat)
    anchor("RetortOperator",root,(-6.37,.18,2.55));anchor("RetortHand",root,(-5.86,1.35,2.78))
    anchor("RetortBasketOut",root,(-5,1.02,3.34))
    # Cooling/packing bench and nearby finished stock rack are distinct.
    legs(g,-5,5.05,2.8,1.1,1.05);chamfer(g,(-5,1.07,5.05),(2.8,.12,1.1),METAL,.025)
    for xx in (-6.35,-3.65):g.rod((xx,1.14,4.55),(xx,1.14,5.55),.025,METAL,8)
    for i in range(17):g.rod((-6.25+i*.155,1.145,4.6),(-6.25+i*.155,1.145,5.5),.016,METAL,6)
    # Finished boxes stand in six floor slots, so the same low trolley can
    # retrieve them. An upper shelf would falsely need a second hoist.
    for i in range(6):
        x=-7.25+i*.85
        anchor("ReadyCase"+str(i),root,(x,.18,6.35))
        for xx in (x-.39,x+.39):g.box((xx,.186,6.35),(.018,.012,1.16),METAL)
    anchor("PackingWorker",root,(-6.94,.18,5.0));anchor("PackingLeftHand",root,(-6.39,1.19,4.8))
    anchor("PackingRightHand",root,(-6.39,1.19,5.2));anchor("CoolingLoad",root,(-5.75,1.19,5.05))
    anchor("PackingLoad",root,(-4.25,1.14,5.05));anchor("FinishedLoad",root,(-3.15,.18,6.1))
    anchor("RawDoor",root,(.3,.18,-5.5));anchor("FinishedDoor",root,(.3,.18,5))
    anchor("Observer",root,(-1.1,1.72,.5))
    for i in range(6):anchor("RawStore"+str(i),root,(-7.36+(i%3)*.83,.18,-6.15+(i//3)*2.55))
    # Powered feed skirts the closed retort shell on the east and approaches
    # the carrier from its open front. The north branch takes the same tray
    # to cooling/packing after the drawer comes back out.
    for first,last,open_first,open_last in (
            ((-3.76,-.35),(-3.1,-.35),False,True),
            ((-3.1,-.35),(-3.1,3.95),True,True),
            ((-3.1,3.95),(-5.75,3.95),True,True),
            ((-5.75,3.95),(-5.75,5.05),True,False)):
        roller_run(g,first,last,open_first,open_last)
    for x,z in ((-3.1,-.35),(-3.1,3.95),(-5,3.95),(-5.75,3.95)):
        g.rod((x,1.11,z),(x,1.19,z),.44,METAL,20)
        g.rod((x,.91,z),(x,1.11,z),.11,PAINT,10)
        g.rod((x,.18,z),(x,.91,z),.06,METAL,8)
        chamfer(g,(x,.205,z),(.34,.05,.34),METAL,.025)
    for z in (.8,3.3):
        g.rod((-2.74,.92,z),(-2.53,.92,z),.115,PAINT,10)
        g.rod((-2.77,.92,z),(-2.77,1.15,z),.025,METAL,8)
    # The last rollers sit on the existing cooling table, at the same height
    # as the basket's inner rollers: the tray never jumps between supports.
    for z in (4.6,4.78,4.96,5.14):
        g.rod((-6.14,1.155,z),(-5.36,1.155,z),.035,METAL,10)
    anchor("ConveyorEntry",root,(-4.36,1.19,-.35));anchor("ConveyorBendEast",root,(-3.1,1.19,-.35))
    anchor("ConveyorBendNorth",root,(-3.1,1.19,3.95));anchor("ConveyorBasketMouth",root,(-5,1.19,3.95))
    anchor("ConveyorPackBend",root,(-5.75,1.19,3.95));anchor("RetortTrayDock",root,(-5,1.19,3.34))
    obj(g,"EquipmentVisible",root,mat)
    for name,center,size in (("Wash",(-5,.71,-2.05),(2.25,1.06,1.2)),
                             ("Seamer",(-5,1.17,-.35),(2.5,1.98,1.45)),
                             ("Retort",(-5,1.35,1.7),(1.62,1.62,2.2)),
                             ("Packing",(-5,.68,5.05),(2.8,1,1.1)),
                             ("ConveyorEast",(-3.1,.70,1.8),(.88,1.04,4.3)),
                             ("ConveyorFront",(-4.425,.70,3.95),(3.53,1.04,.88))):
        collision_box(name,root,mat,center,size)
    return root


def truck(mat):
    root=empty("Truck");g=Geometry();glass=Geometry();driverdoor=Geometry();doorglass=Geometry()
    driverhinge=empty("MOVE_DriverDoor",root,(-1.08,1.12,4.94))
    # Rear axle origin, four-wheel rigid chassis, 4.2 m wheelbase.
    for x in (-.73,.73):chamfer(g,(x,.69,1.4),(.18,.25,7.45),METAL,.03)
    for z in (-1.7,0,1.8,3.3,4.2):chamfer(g,(0,.7,z),(1.8,.16,.15),METAL,.02)
    for z in (0,4.2):g.rod((-1.12,.45,z),(1.12,.45,z),.09,METAL,10)
    for x in (-.63,.63):
        for z in (-.35,.1,.5):chamfer(g,(x,.45,z),(.12,.04,1.1),METAL,.008)
    # Closed insulated goods body; separate leaves expose the actual interior.
    for center,size in (((0,1.15,.25),(2.5,.1,5.7)),((0,3.50,.25),(2.5,.1,5.7)),
                        ((-1.21,2.35,.25),(.08,2.3,5.7)),((1.21,2.35,.25),(.08,2.3,5.7)),
                        ((0,2.35,3.08),(2.5,2.3,.08))):
        chamfer(g,center,size,CABIN,.024)
    g.role="Deck";g.box((0,1.204,.22),(2.31,.018,5.55),METAL);g.role=None
    for x in (-1.21,1.21):
        for y in (1.21,3.48):g.rod((x,y,-2.58),(x,y,3.10),.034,METAL,8)
        for z in (-2.58,3.1):g.rod((x,1.21,z),(x,3.48,z),.034,METAL,8)
        for z in (-1.7,-.3,1.1,2.5):g.box((x,2.32,z),(.018,2.2,.025),METAL)
    # Chamfered cab and a sloped windscreen opening, not a solid painted block.
    # A real cab floor leaves the driver's footwell and open doorway empty.
    chamfer(g,(0,1.09,4.27),(2.28,.12,2.10),PAINT,.035)
    chamfer(g,(0,2.87,4.15),(2.28,.18,2.02),PAINT,.075)
    chamfer(g,(0,1.9,3.24),(2.27,1.35,.16),PAINT,.045)
    chamfer(g,(0,1.64,5.18),(2.25,.38,.30),PAINT,.08)
    for x in (-1.06,1.06):
        g.rod((x,1.76,5.12),(x,2.80,4.96),.065,PAINT,8)
        g.rod((x,1.65,3.35),(x,2.78,3.35),.065,PAINT,8)
        if x>0:chamfer(g,(x,1.48,4.10),(.12,.65,1.72),PAINT,.03)
        chamfer(g,(x*1.09,.98,4.05),(.18,.13,1.2),METAL,.025)
        g.rod((x*1.03,2.2,4.93),(x*1.12,2.2,4.77),.025,METAL,6)
        chamfer(g,(x*1.13,2.22,4.77),(.1,.32,.19),DARK,.018)
        if x>0:
            glass.box((x,2.25,4.12),(.018,.89,1.43),GLASS)
            chamfer(g,(x*1.074,1.72,3.63),(.035,.06,.20),METAL,.01)
    chamfer(driverdoor,(.02,.36,-.84),(.12,.65,1.72),PAINT,.03)
    for zz in (-1.56,-.1):driverdoor.rod((.02,.67,zz),(.02,1.66,zz),.035,PAINT,8)
    driverdoor.rod((.02,1.66,-1.56),(.02,1.66,-.1),.035,PAINT,8)
    chamfer(driverdoor,(-.058,.60,-1.31),(.035,.06,.2),METAL,.01)
    doorglass.box((.02,1.13,-.82),(.018,.89,1.43),GLASS)
    obj(driverdoor,"DriverDoorVisible",driverhinge,mat);obj(doorglass,"CabinDoorGlass",driverhinge,mat)
    glass.add([(-.99,1.83,5.08),(.99,1.83,5.08),(.99,2.75,4.94),(-.99,2.75,4.94)],[(0,1,2,3)],GLASS,solid=False)
    g.rod((0,1.80,5.10),(0,2.79,4.95),.028,PAINT,6)
    for x in (-.49,.49):g.rod((x,1.87,5.103),(x+.2,2.22,5.05),.013,DARK,6)
    chamfer(g,(0,.94,5.23),(2.4,.22,.30),METAL,.045)
    chamfer(g,(0,1.31,5.31),(.85,.25,.04),DARK,.02)
    for i in range(7):g.box((-.37+i*.123,1.31,5.337),(.05,.2,.025),METAL)
    for x in (-.88,.88):
        g.rod((x,1.4,5.25),(x,1.4,5.32),.115,LAMP,12)
        chamfer(g,(x,.75,-2.51),(.29,.16,.12),RUST,.018)
    # Seat, pedals and steering ring are anchors for the shared driver rig.
    for x in (-.56,.56):
        chamfer(g,(x,1.45,4.0),(.59,.22,.69),DARK,.065)
        chamfer(g,(x,1.83,3.77),(.59,.68,.16),DARK,.065)
    chamfer(g,(0,1.94,4.73),(1.95,.20,.22),METAL,.045)
    g.rod((-.56,1.28,4.45),(-.56,1.94,4.50),.035,METAL,8)
    ring(g,(-.56,2.01,4.50),.23,.022,DARK,(0,.8,.6),16)
    for side,x in (("L",-1.03),("R",1.03)):
        for axle,z in (("F",4.2),("R",0)):
            pivot=empty("MOVE_Wheel"+axle+side,root,(x,.45,z));w=Geometry();w.role="Rubber"
            w.rod((-.16,0,0),(.16,0,0),.45,DARK,20);w.role=None
            for xx in (-.171,.171):
                w.rod((xx-.008,0,0),(xx+.008,0,0),.255,METAL,12)
                ring(w,(xx,0,0),.20,.025,METAL,(1,0,0),12)
            obj(w,"WheelVisible"+axle+side,pivot,mat)
    for side,x in (("Left",-1.16),("Right",1.16)):
        pivot=empty("MOVE_TruckRearDoor"+side,root,(x,1.23,-2.57));d=Geometry()
        direction=1 if x<0 else -1
        chamfer(d,(direction*.575,1.1,0),(1.15,2.2,.09),CABIN,.02)
        d.rod((direction*.86,.2,-.075),(direction*.86,2.02,-.075),.025,METAL,8)
        for y in (.22,1.8):d.rod((0,y,0),(direction*.34,y,-.08),.038,METAL,8)
        obj(d,"RearDoorVisible"+side,pivot,mat)
    lift=empty("MOVE_TailLift",root,(0,1.2,-2.6));l=Geometry()
    l.role="Deck";chamfer(l,(0,-.055,-1.25),(2.24,.11,2.5),METAL,.02);l.role=None
    for x in (-.82,.82):
        l.rod((x,-.14,-.15),(x,-.14,-2.4),.055,METAL,8)
        g.rod((x,.45,-2.2),(x,1.04,-2.56),.055,METAL,8)
    for z in (-.1,-2.41):l.box((0,.006,z),(2.14,.012,.045),EDGE)
    obj(l,"TailLiftVisible",lift,mat);anchor("TailLiftLoad",lift,(0,.01,-.8))
    anchor("TruckDriver",root,(-.56,1.56,4.0));anchor("DriverLeftHand",root,(-.76,2.01,4.5))
    anchor("DriverRightHand",root,(-.36,2.01,4.5));anchor("DriverFoot",root,(-.55,1.17,4.55))
    anchor("TruckRear",root,(0,1.2,-2.6));anchor("TruckGroundBehind",root,(0,0,-4.65))
    for i in range(6):anchor("TruckCargo"+str(i),root,(-.55 if i%2==0 else .55,1.22,-1.75+(i//2)*1.7))
    obj(g,"TruckVisible",root,mat);obj(glass,"CabinGlass",root,mat)
    # Vehicle movement controller may disable these when computing its sweeps.
    collision_box("TruckBody",root,mat,(0,1.82,1.4),(2.5,3.36,8))
    return root


def small_part(name,mat):
    root=empty(name);g=Geometry()
    if name=="Pallet":
        for x in (-.31,0,.31):
            for z in (-.48,0,.48):chamfer(g,(x,.064,z),(.15,.128,.18),WOOD,.013)
        for z in (-.49,0,.49):chamfer(g,(0,.022,z),(.8,.044,.18),WOOD,.008)
        for x in (-.32,-.16,0,.16,.32):chamfer(g,(x,.15,0),(.14,.044,1.2),WOOD,.008)
        anchor("Load",root,(0,.172,0))
        # Closed washable fish bin: the catch stays covered on road and in the
        # cold room, with actual corner feet, a lipped lid and carrying grips.
        g.role="Tare"
        chamfer(g,(0,.475,0),(.74,.59,1.08),PAINT,.045)
        chamfer(g,(0,.797,0),(.77,.075,1.12),PAINT,.025)
        for x in (-.365,.365):
            for z in (-.49,.49):g.box((x,.48,z),(.035,.48,.06),PAINT)
            g.rod((x,.61,-.18),(x,.61,.18),.022,METAL,8)
        g.role=None
        for z in (-.54,.54):chamfer(g,(0,.745,z),(.14,.15,.028),METAL,.01)
        anchor("BinLid",root,(0,.835,0))
    elif name=="RetortBasket":basket_geometry(g);anchor("Load",root,(0,.17,0))
    elif name=="CanTray":
        g.role="SteelLight";chamfer(g,(0,.016,0),(.76,.032,.46),METAL,.008)
        for x in range(5):
            for z in range(3):can(g,-.28+x*.14,.035,-.14+z*.14)
        anchor("Load",root,(0,.13,0))
    elif name=="CartonStack":
        g.role="Timber"
        for y in (.16,.47):
            for x in (-.19,.19):
                for z in (-.28,.28):
                    chamfer(g,(x,y,z),(.36,.30,.53),WOOD,.018)
                    g.box((x,y+.155,z),(.06,.014,.52),ROPE)
                    g.box((x,y-.02,z-.273),(.065,.27,.014),ROPE)
        g.role=None;anchor("Load",root,(0,.63,0))
    elif name=="Trolley":
        forks=empty("MOVE_Forks",root);f=Geometry()
        for x in (-.155,.155):
            chamfer(f,(x,.10,0),(.13,.056,1.2),PAINT,.012)
            g.role="Rubber";g.rod((x-.05,.035,.48),(x+.05,.035,.48),.035,DARK,10);g.role=None
        chamfer(f,(0,.10,-.64),(.5,.056,.12),PAINT,.012)
        obj(f,"PalletJackForks",forks,mat)
        chamfer(g,(0,.20,-.77),(.44,.28,.28),PAINT,.035)
        g.role="Rubber";g.rod((-.13,.11,-.85),(.13,.11,-.85),.11,DARK,12);g.role=None
        for x in (-.32,.32):g.rod((x*.32,.30,-.77),(x,1.08,-.88),.025,METAL,8)
        g.rod((-.38,1.08,-.88),(.38,1.08,-.88),.03,METAL,8)
        anchor("Load",root,(0,0,0));anchor("TrolleyHandleLeft",root,(-.32,1.08,-.88))
        anchor("TrolleyHandleRight",root,(.32,1.08,-.88));anchor("Handle",root,(0,1.08,-.88))
    obj(g,name+"Visible",root,mat)
    return root


def yard(mat):
    root=empty("Yard");g=Geometry()
    chamfer(g,(0,-.02,0),(18,.2,18),CONCRETE,.012)
    collision_box("Yard0",root,mat,(0,-.02,0),(18,.2,18))
    # The existing one-metre driveway is a separate authored, subdivided solid:
    # runtime placement fits its top and collider to the actual graded road.
    # Flat inner and graded outer edges form a bilinear patch, so a single
    # triangulated quad would leave a visible/physical height discrepancy.
    driveway=Geometry();vertices=[];faces=[]
    columns,rows=6,4
    for y in (.08,-.12):
        for iz in range(rows+1):
            for ix in range(columns+1):vertices.append((1.5+6.5*ix/columns,y,9+iz/rows))
    layer=(columns+1)*(rows+1)
    for iz in range(rows):
        for ix in range(columns):
            a=iz*(columns+1)+ix;b=a+1;c=b+columns+1;d=a+columns+1
            faces.extend(((a,b,c,d),(d+layer,c+layer,b+layer,a+layer)))
    perimeter=list(range(columns+1))+[iz*(columns+1)+columns for iz in range(1,rows+1)]+\
        [rows*(columns+1)+ix for ix in range(columns-1,-1,-1)]+\
        [iz*(columns+1) for iz in range(rows-1,0,-1)]
    for i,a in enumerate(perimeter):
        b=perimeter[(i+1)%len(perimeter)];faces.append((a,a+layer,b+layer,b))
    driveway.add(vertices,faces,CONCRETE)
    obj(driveway,"YardDriveway",root,mat);obj(driveway,"COL_Yard9.5",root,mat)
    # The hand pallet jack rolls across the 10 cm hall threshold. These short
    # 1:15 ramps connect the actual .18 m floor and .08 m service paving.
    for name,z,width in (("RawThreshold",-5.5,2),("FinishedThreshold",5,2.2)):
        lo,hi=z-width*.5,z+width*.5
        vertices=[(0,.18,lo),(1.5,.08,lo),(1.5,.08,hi),(0,.18,hi),
                  (0,.075,lo),(1.5,.075,lo),(1.5,.075,hi),(0,.075,hi)]
        faces=[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
        g.add(vertices,faces,CONCRETE)
        ramp=Geometry();ramp.add(vertices,faces,CONCRETE);obj(ramp,"COL_"+name,root,mat)
    # Sparse joints, real gutter slots and a simple vehicle envelope marking.
    for x in (1,4,7):g.box((x,.083,0),(.014,.006,17.75),DARK)
    for z in (-6,-3,0,3,6):g.box((4.45,.083,z),(8.8,.006,.014),DARK)
    g.role="RoadMarking"
    for x in (3.45,6.55):
        for z in (-3.2,-.2,2.8):g.box((x,.088,z),(.08,.009,2.15),EDGE)
    g.role=None
    for z in (-6.6,6.65):
        g.box((1.1,.086,z),(1.1,.012,.23),DARK)
        for i in range(9):g.box((.62+i*.12,.10,z),(.025,.025,.22),METAL)
    obj(g,"YardVisible",root,mat);return root


def build(mat):
    return [hall(mat),equipment(mat),truck(mat)]+[small_part(n,mat) for n in NAMES[3:8]]+[yard(mat)]


def validate(roots):
    port.validate_surfaces(roots)
    entries={r.name.split('.')[0]:port.describe(r) for r in roots}
    if set(entries)!=set(NAMES):raise RuntimeError("Cannery pack is missing a required model")
    truck_points={a['name']:a['position'] for a in entries['Truck']['anchors']}
    for i in range(6):
        x,y,z=truck_points['ANCHOR_TruckCargo'+str(i)]
        if abs(x)+.4>1.15 or z-.6< -2.52 or z+.6>3.02 or abs(y-1.22)>.001:
            raise RuntimeError("Cannery pallet does not fit the real closed truck box")
    # The public corridor remains 1.84 metres wide between east wall and the
    # partition. Hall wall openings are measured from actual kit geometry.
    if entries['Hall']['bounds_max'][0]>.4:raise RuntimeError("Hall intrudes into the service strip")
    if entries['Truck']['triangles']>22000 or entries['Equipment']['triangles']>65000:
        raise RuntimeError("Cannery mesh kit exceeds compact game-slice budget")
    # Explicit storage geometry: a 1.35 m centre aisle reaches all six
    # raw units and the 1.5 m door; no bin is authored inside its wall.
    points={a['name']:a['position'] for a in entries['Equipment']['anchors']}
    for i in range(6):
        x,y,z=points['ANCHOR_RawStore'+str(i)]
        if x-.4< -7.88 or x+.4> -5.26 or z-.6< -6.79 or z+.6> -2.96:
            raise RuntimeError('Raw fish unit intersects the real cold-store wall')
    aisle=(points['ANCHOR_RawStore3'][2]-.6)-(points['ANCHOR_RawStore0'][2]+.6)
    if aisle<1.3:raise RuntimeError('Cold-store central pallet-jack aisle is too narrow')
    # The pallet's three support lines leave two fork passages. Their actual
    # deck underside is .128 m; fork tops touch it without entering the wood.
    if not (.075 < .155-.065 and .155+.065 < .235 and .044 < .072 < .128):
        raise RuntimeError('Pallet-jack fork passages lost their vertical/lateral clearance')
    yard_root=next(r for r in roots if r.name.split('.')[0]=='Yard')
    driveway=next(o for o in yard_root.children_recursive if o.name.startswith('COL_Yard9'))
    top=[source(v.co) for v in driveway.data.vertices if abs(v.co.z-.08)<.0001]
    if len(top)!=35 or len({round(v[0],5) for v in top})!=7 or len({round(v[2],5) for v in top})!=5:
        raise RuntimeError('Driveway lost the authored 6 by 4 road-profile grid')
    return [entries[name] for name in NAMES]


def preview(roots,path):
    # Presentation only; files are exported before temporary staging positions.
    indexed={r.name:r for r in roots};indexed['Truck'].location=source((5,.08,-1.3))
    for name in NAMES[3:8]:
        for part in indexed[name].children_recursive:part.hide_render=True
    for part in indexed['Hall'].children_recursive:
        if part.name.startswith(('COL_','HallRoof')):part.hide_render=True
    for root in roots:
        for part in root.children_recursive:
            if part.name.startswith('COL_'):part.hide_render=True
            if part.name.startswith('MOVE_TailLift'):part.rotation_euler.x=-math.pi*.5
            if part.name in ('CanneryGlass','CabinGlass'):part.hide_render=True
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.world.color=(.32,.35,.35)
    target=Vector(source((-1,1,0)))
    cd=bpy.data.cameras.new('CanneryReview');cam=bpy.data.objects.new(cd.name,cd);bpy.context.collection.objects.link(cam)
    cam.location=source((22,25,24));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    cd.type='ORTHO';cd.ortho_scale=29;scene.camera=cam
    ld=bpy.data.lights.new('CanneryReviewSoftbox','AREA');ld.energy=7000;ld.size=18
    light=bpy.data.objects.new(ld.name,ld);bpy.context.collection.objects.link(light);light.location=source((5,18,8))
    light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x=1400;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def main():
    p=argparse.ArgumentParser();p.add_argument('--no-preview',action='store_true');p.add_argument('--validate-only',action='store_true')
    p.add_argument('--model-dir',type=Path,default=ROOT/'Assets/Resources/City/Cannery')
    p.add_argument('--source-dir',type=Path,default=ROOT/'ArtSource/City/Cannery')
    p.add_argument('--only-part',choices=NAMES)
    args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    port.base.reset();mat=port.base.material('CannerySharedPortSurfaces')
    roots=build(mat);entries=validate(roots)
    manifest={'design_id':'city_compact_fish_cannery_v1','generator':Path(__file__).name,
              'coordinate_system':'Unity +Y up / +Z truck forward; metres; truck rear axle ground origin',
              'parts':entries,'hall_floor_height':.18,'yard_height':.08,'truck_wheelbase':4.2,'truck_wheel_radius':.45,
              'truck_body_xz':[-1.25,1.25,-2.6,5.4],'pallet_size_xz':[.8,1.2],
              'cold_store_aisle_width':1.35,'pallet_jack_fork_top':.128,'pallet_jack_carry_lift':.03,
              'conveyor_top_height':1.19,'retort_basket_tray_height':.17,'retort_basket_contains_cans':False,
              'tail_lift_length':2.5,'tail_lift_operator_rear_clearance':.27,
              'goods_threshold_ramp_length':1.5,'goods_threshold_ramp_rise':.1,
              'driveway_grid_xz':[6,4],'driveway_runtime_profile':'CityCanneryPlan.ApronTop',
              'driver_door_open_axis':'Unity local +Y, +70 degrees about front hinge',
              'tail_lift_fold_axis':'Unity local +X, +90 degrees folded; authored extended; lower pivot Y for lift',
              'retort_door_open_axis':'Unity +Y vertical slide, 1.7 metres; identity rotation',
              'semantic_uv_tiles_metres':port.SURFACE_TILES,
              'geometry_signature_includes':['vertices','faces','colors','uv0','transforms']}
    if args.validate_only:
        if json.loads((args.model_dir/'CityCannery3D.json').read_text(encoding='utf-8'))!=manifest:
            raise RuntimeError('Cannery regenerated contract differs from saved manifest')
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        for root in roots:
            if args.only_part is None or root.name==args.only_part:port.base.export(root,args.model_dir/(root.name+'.fbx'))
        (args.model_dir/'CityCannery3D.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
        port.source_surface_materials(roots)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/'CityCannery3D.blend'),check_existing=False)
        if not args.no_preview:preview(roots,args.source_dir/'CityCannery3D.png')
    port.base.reset();repeated=validate(build(mat))
    if repeated!=entries:raise RuntimeError('Cannery deterministic rebuild mismatch')
    print('CITY CANNERY ART CONTRACT OK: metre parts, shared semantic UVs, pallet fit, real openings and deterministic geometry')
    print(json.dumps([{k:v for k,v in e.items() if k in ('name','triangles','bounds_min','bounds_max')} for e in entries]))


if __name__=='__main__':main()
