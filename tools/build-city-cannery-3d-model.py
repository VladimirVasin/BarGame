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
import re
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
NAMES = ("Hall", "Equipment", "Truck", "Pallet", "RetortBasket", "CanTray", "CartonStack", "Trolley", "Yard", "Workwear")
# This bounded extension belongs to the cannery; rebuilding it does not change
# the existing port pack's material contract or geometry signatures.
port.SURFACE_TILES.update({"CanneryFloor":2.0,"WetFloor":2.0,"WashWall":1.5,"Stainless":1.5,
                          "Insulation":1.5,"Cardboard":1.0})
CONVEYOR_ROLLER_AXES=("Z","Z","X","X","Z","Z","X","X")
CONVEYOR_ROLLER_SIGNS=(-1,-1,1,1,1,1,1,1)
TRUCK_REAR, TRUCK_FRONT, TRUCK_HALF_WIDTH, TRUCK_WHEELBASE = -2.0, 4.5, 1.2, 3.3
TRUCK_CAB_OFFSET = -.9
CREW_SERVICE_ANCHORS = {
    "PreparationTidyWorker":(-6.65,.18,-2.35),
    "PreparationTidyHand":(-6.12,1.238,-2.50),
    "SeamerRestWorker":(-7.14,.18,-.35),
    "ReceiverRestWorker":(-2.9,.18,-6.5),
    "ReceiverScaleHand":(.39,1.12,7.38),
    "ServiceLight":(-7.35,2.56,.55),
    "ColdStoreSound":(-7.5,3.46,-5.4),
}


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


def dial_ticks(g, center, radius, normal=(0,0,1), count=9):
    """Unnumbered mechanical divisions, real face geometry with no added text."""
    normal=Vector(normal);vertical=Vector((0,1,0));horizontal=vertical.cross(normal)
    center=Vector(center);previous=g.role;g.role=None
    for i in range(count):
        angle=math.radians(-125+250*i/(count-1))
        direction=horizontal*math.sin(angle)+vertical*math.cos(angle)
        g.rod(center+direction*(radius*.72),center+direction*radius,.006,DARK,6)
    g.role=previous


def hall(mat):
    root=empty("Hall"); g=Geometry(); c=Geometry(); glass=Geometry(); roof=Geometry(); lamps=Geometry()
    # The hall has two honest public openings and two goods openings. Its
    # glazed east side gives a second view of the same live machinery.
    walls=[kit.translated(kit.wall_run(8,3.92,.24,[kit.Opening(2.9,1.7,2.8)]),(-4,7,.18)),
           kit.translated(kit.wall_run(8,3.92,.24,[kit.Opening(2.9,1.7,2.8)]),(-4,-7,.18)),
           kit.translated(kit.rotated_z(kit.wall_run(14,3.92,.24,
               [kit.Opening(2.65,1.4,2.8)]),90),(-8,0,.18)),
           kit.translated(kit.rotated_z(kit.wall_run(14,3.92,.24,
               [kit.Opening(-5.5,2,2.8),kit.Opening(-2.5,2.7,3,.6),
                kit.Opening(.65,2.7,3,.6),kit.Opening(5,2.2,2.8)]),90),(0,0,.18))]
    g.role="Plaster"
    for wall in walls:
        kit_add(g, wall, CABIN); kit_add(c, wall, CABIN)
    g.role=None
    g.role="CanneryFloor";chamfer(g,(-4,.13,0),(8,.1,14),CONCRETE,.008);g.role=None
    collision_box("HallFloor",root,mat,(-4,.13,0),(8,.1,14))
    for x in (-7.88,-.12):
        for z in (-6.85,-3.5,0,3.5,6.85):
            chamfer(g,(x,2.07,z),(.16,3.78,.18),METAL,.018)
    roof.role="Roof"
    chamfer(roof,(-4,4.19,0),(8.65,.18,14.55),METAL,.03)
    for z in range(-7,8): roof.box((-4,4.297,z),(8.56,.035,.045),DARK)
    # Real roof purlins keep the low hall structurally legible from its public
    # aisle. Nothing hangs into the pallet-jack or retort-door envelopes.
    for z in (-6.75,-3.5,0,3.5,6.75):
        chamfer(g,(-4,3.98,z),(7.75,.16,.10),METAL,.012)
    for x in (-8.18,.18):
        g.rod((x,4.05,-7.25),(x,4.05,7.25),.075,METAL,10)
        g.rod((x,4.02,-6.85),(x,.3,-6.85),.065,METAL,10)
        g.rod((x,.3,-6.85),(x,.17,-7.1),.065,METAL,10)
    # Short insulated cold room against the west wall; the east reveal opens
    # onto a real receiving lane. No fake door pasted over a filled box.
    cold=[kit.translated(kit.wall_run(2.6,2.72,.12),(-6.5,-6.85,.18)),
          kit.translated(kit.wall_run(2.6,2.72,.12),(-6.5,-2.9,.18)),
          kit.translated(kit.rotated_z(kit.wall_run(3.95,2.72,.12,[kit.Opening(0,1.5,2.25)]),90),(-5.2,-4.875,.18))]
    g.role="Insulation"
    for wall in cold:
        kit_add(g,wall,CABIN);kit_add(c,wall,CABIN)
    chamfer(g,(-6.5,2.98,-4.875),(2.73,.14,4.08),CABIN,.025)
    for zz in (-5.65,-4.10):chamfer(g,(-5.09,1.37,zz),(.13,2.37,.09),METAL,.008)
    # Folded insulated leaf is parked next to the opening, not across it.
    chamfer(g,(-5.02,1.34,-3.75),(.14,2.3,.45),CABIN,.025)
    g.role=None
    # Joints, gasket and a folded handle explain the washable insulated leaf.
    for z in (-6.32,-3.38):g.box((-5.132,1.52,z),(.013,2.55,.018),METAL)
    g.rod((-4.927,1.24,-3.88),(-4.927,1.60,-3.88),.025,METAL,8)
    g.box((-5.111,1.34,-3.95),(.018,2.23,.022),DARK)
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
        # A blank, modest goods label belongs to its actual opening.
        chamfer(g,(.13,3.48,z),(.045,.22,width*.72),METAL,.008)
    anchor("ReceiveSign",root,(.156,3.48,-5.5));anchor("FinishedSign",root,(.156,3.48,5))
    # The wet-process wall is washable only where work actually takes place.
    for low,high in ((-2.825,1.95),(3.35,5.725)):
        g.role="WashWall"
        chamfer(g,(-7.863,1.05,(low+high)*.5),(.033,1.72,high-low),CABIN,.012)
        g.role="Stainless"
        for y in (.22,1.94):chamfer(g,(-7.83,y,(low+high)*.5),(.035,.07,high-low),METAL,.008)
    g.role=None
    # Drains lead under prep and retort; no loose decorative pipe ends.
    for z in (-2.30,2.45):
        g.box((-4.9,.187,z),(3.9,.014,.19),DARK)
        for i in range(25):g.box((-6.75+i*.155,.20,z),(.035,.018,.18),METAL)
    g.role="WetFloor"
    for x,z,width,depth in ((-4.72,-2.46,.68,.32),(-5.68,2.26,.51,.27)):
        outline=[(-.50,-.22),(-.30,-.50),(.20,-.43),(.50,-.12),(.40,.37),(.08,.50),(-.38,.32)]
        vertices=[(x+dx*width,y,z+dz*depth) for y in (.182,.184) for dx,dz in outline]
        count=len(outline)
        faces=[tuple(reversed(range(count))),tuple(range(count,count*2))]
        faces.extend((i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count))
        g.add(vertices,faces,CONCRETE)
    g.role=None
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
    anchor("SteamOutlet",root,(-7.65,5.1,2.15))
    # One mounted hose and a hung squeegee explain wash-down, without loose
    # clutter, invented stock or a prop in a public/work route.
    chamfer(g,(-7.77,1.22,-.92),(.10,.22,.18),METAL,.02)
    g.role="Rubber"
    for offset in (0,.045,.09):ring(g,(-7.68+offset,1.07,-.92),.25,.018,DARK,(1,0,0),14)
    g.role=None
    g.rod((-7.56,1.06,-.69),(-7.56,1.41,-.60),.026,METAL,8)
    g.rod((-7.73,.41,-1.59),(-7.73,1.89,-1.59),.022,METAL,8)
    chamfer(g,(-7.73,.38,-1.59),(.08,.07,.58),METAL,.014)
    g.role="Rubber";g.box((-7.73,.344,-1.59),(.055,.025,.57),DARK);g.role=None
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


def can_body(g,x,y,z):
    """A closed bottom and open double wall; an unfilled can is visibly empty."""
    g.role="Stainless";g.rod((x,y,z),(x,y+.008,z),.052,CABIN,12)
    for i in range(12):
        a,b=i*math.tau/12,(i+1)*math.tau/12
        vertices=[(x+r*math.cos(t),yy,z+r*math.sin(t))
                  for yy in (y+.008,y+.095) for r in (.047,.052) for t in (a,b)]
        g.add(vertices,[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)],CABIN)
    for yy in (y+.009,y+.094):ring(g,(x,yy,z),.052,.0025,METAL,(0,1,0),12)
    g.role=None


def can_contents(g,x,y,z):
    g.role="Fish"
    g.rod((x,y+.01,z),(x,y+.08,z),.046,FISH,12)
    # Two plain cut pieces sit below the rim; no whole fish protrudes from
    # a sealed food can and no duplicated batch is baked into machinery.
    for dx,dz in ((-.015,-.013),(.017,.011)):
        g.rod((x+dx,y+.08,z+dz-.014),(x+dx,y+.08,z+dz+.014),.011,FISH,6)
    g.role=None


def can_lid(g,x,y,z):
    g.role="Stainless"
    g.rod((x,y+.095,z),(x,y+.102,z),.057,CABIN,12)
    ring(g,(x,y+.099,z),.050,.002,METAL,(0,1,0),12)
    # A pressed concentric bead reads as a manufactured lid, with no brand.
    ring(g,(x,y+.100,z),.034,.0015,METAL,(0,1,0),12)
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


def roller_run(g, root, mat, run_index, start, end, open_start=True, open_end=True):
    a,b=Vector((start[0],1.155,start[1])),Vector((end[0],1.155,end[1]))
    direction=(b-a).normalized();side=Vector((-direction.z,0,direction.x));length=(b-a).length
    left=.45 if open_start else 0;right=length-(.45 if open_end else 0)
    if right<=left:return
    distances=[left+i*.16 for i in range(int((right-left)/.16)+1)]
    driven=(0,len(distances)-1)
    for index,distance in enumerate(distances):
        p=a+direction*distance
        if index in driven:
            number=run_index*2+driven.index(index)
            pivot=empty("MOVE_ConveyorRoller"+str(number).zfill(2),root,tuple(p));roller=Geometry()
            roller.role="Stainless";roller.rod(-side*.39,side*.39,.035,METAL,10)
            # One axial highlight makes the small driven subset's rotation
            # observable, while the remaining support rollers stay passive.
            roller.role=None
            roller.rod(-side*.30+Vector((0,.034,0)),side*.30+Vector((0,.034,0)),.004,CABIN,6)
            obj(roller,"ConveyorRollerVisible"+str(number).zfill(2),pivot,mat)
        else:
            g.role="Stainless";g.rod(p-side*.39,p+side*.39,.035,METAL,10);g.role=None
        if index in driven:
            for sign in (-1,1):
                bearing=p+side*(sign*.412)
                chamfer(g,tuple(bearing-Vector((0,.015,0))),(.085,.11,.085),PAINT,.012)
    for sign in (-1,1):
        p=a+direction*left+side*(sign*.41);q=a+direction*right+side*(sign*.41)
        g.rod(p-Vector((0,.11,0)),q-Vector((0,.11,0)),.035,PAINT,8)
        g.rod(p+Vector((0,.085,0)),q+Vector((0,.085,0)),.018,METAL,8)
    mid=(a+b)*.5
    for sign in (-1,1):
        foot=mid+side*(sign*.32)
        g.rod((foot.x,.18,foot.z),(foot.x,1.07,foot.z),.032,METAL,8)
        chamfer(g,(foot.x,.205,foot.z),(.14,.05,.14),METAL,.01)


def crew_service_dressing(root,mat):
    """Finite service objects stay against real walls, outside working routes."""
    shelf=Geometry();shelf.role="Stainless"
    # A shallow wall rack keeps hand tools and spare sealing rings beside the
    # seamer. It remains west of the retort worker's 58 cm body corridor.
    for y in (1.70,2.18):
        chamfer(shelf,(-7.70,y,.25),(.22,.035,.95),METAL,.009)
        for z in (-.18,.68):
            shelf.rod((-7.80,y-.20,z),(-7.80,y+.03,z),.013,METAL,6)
            shelf.rod((-7.80,y-.18,z),(-7.60,y-.025,z),.014,METAL,6)
        for z in (-.225,.725):shelf.box((-7.70,y+.027,z),(.22,.025,.012),METAL)
    shelf.role=None
    for z,length in ((-.10,.20),(.12,.15)):
        shelf.rod((-7.73,1.737,z),(-7.73,1.737,z+length),.015,METAL,8)
        ring(shelf,(-7.73,1.737,z),.035,.012,METAL,(0,1,0),8)
        ring(shelf,(-7.73,1.737,z+length),.041,.012,METAL,(0,1,0),8)
    shelf.role="Rubber"
    for index in range(3):ring(shelf,(-7.69,2.21+index*.015,.52),.064,.006,DARK,(0,1,0),12)
    shelf.role="Fabric"
    for index in range(3):chamfer(shelf,(-7.70,2.21+index*.020,.02),(.17,.018,.23),CABIN,.005)
    shelf.role=None
    obj(shelf,"ServiceShelf",root,mat)

    stool=Geometry();cx,cz=-7.52,.75
    stool.role="Timber"
    chamfer(stool,(cx,.672,cz),(.42,.055,.40),WOOD,.035)
    stool.role=None
    for dx in (-1,1):
        for dz in (-1,1):
            stool.rod((cx+dx*.155,.18,cz+dz*.145),(cx+dx*.115,.646,cz+dz*.105),.018,METAL,8)
    for x in (cx-.14,cx+.14):stool.rod((x,.35,cz-.13),(x,.35,cz+.13),.012,METAL,6)
    stool.rod((cx-.14,.35,cz),(cx+.14,.35,cz),.012,METAL,6)
    obj(stool,"ServiceStool",root,mat)
    collision_box("ServiceStool",root,mat,(cx,.44,cz),(.42,.52,.40))
    anchor("ServiceStoolSeat",root,(cx,.70,cz))

    # A jacket on the cold-room's real north wall belongs to this shift.
    # The hanger, empty second hook and soft hems avoid a locker-shaped block.
    coat=Geometry();coat.role=None
    chamfer(coat,(-7.40,2.10,-2.81),(.76,.055,.045),METAL,.01)
    for x in (-7.58,-7.22):
        coat.rod((x,2.1,-2.79),(x,2.1,-2.735),.012,METAL,6)
        coat.rod((x,2.1,-2.735),(x,2.135,-2.735),.012,METAL,6)
    coat.rod((-7.58,2.1,-2.74),(-7.40,1.94,-2.76),.009,METAL,6)
    for x in (-7.64,-7.16):coat.rod((-7.40,1.94,-2.76),(x,1.87,-2.76),.009,METAL,6)
    coat.role="Fabric"
    outline=[(-.12,.05),(-.27,.02),(-.38,-.22),(-.29,-.26),(-.20,-.10),
             (-.20,-.58),(.20,-.58),(.20,-.10),(.29,-.26),(.38,-.22),(.27,.02),(.12,.05),(0,-.025)]
    vertices=[(-7.4+x,1.85+y,z) for z in (-2.81,-2.755) for x,y in outline]
    count=len(outline)
    faces=[tuple(reversed(range(count))),tuple(range(count,count*2))]
    faces.extend((i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count))
    coat.add(vertices,faces,PAINT)
    for x in (-7.53,-7.27):coat.rod((x,1.33,-2.747),(x,1.72,-2.747),.008,PAINT,6)
    coat.rod((-7.40,1.3,-2.742),(-7.40,1.82,-2.742),.005,DARK,6)
    obj(coat,"StoredWorkJacket",root,mat)

    # The cloth straddles the supported west rim, clear of the raw crate.
    cloth=empty("MOVE_PreparationCloth",root,CREW_SERVICE_ANCHORS["PreparationTidyHand"])
    fabric=Geometry();fabric.role="Fabric"
    chamfer(fabric,(0,.004,0),(.15,.008,.16),CABIN,.003)
    for z in (-.065,.055):fabric.rod((-.07,.009,z),(.07,.009,z+.004),.003,CABIN,6)
    obj(fabric,"PreparationWipeCloth",cloth,mat)

    lamp=Geometry()
    chamfer(lamp,(-7.78,2.74,.55),(.08,.20,.16),METAL,.014)
    lamp.rod((-7.74,2.74,.55),(-7.35,2.68,.55),.022,METAL,8)
    lamp.rod((-7.35,2.64,.55),(-7.35,2.71,.55),.13,METAL,12,end_radius=.065)
    obj(lamp,"ServiceLampHousing",root,mat)
    lens=Geometry();lens.rod((-7.35,2.628,.55),(-7.35,2.638,.55),.111,LAMP,12)
    obj(lens,"CanneryLampGlass",root,mat)
    for name,position in CREW_SERVICE_ANCHORS.items():anchor(name,root,position)


def shipping_scale(root,mat):
    # The existing complete scale now occupies the outdoor pocket north of
    # the shipping opening. Rotate its original metre geometry as one object,
    # including the zero wheel and the dial, which now faces the worker east.
    scale=empty("ShippingScale",root);g=Geometry()
    chamfer(g,(-3.5,.30,-5.45),(1.25,.24,1.45),METAL,.04)
    g.rod((-4.1,.42,-5.95),(-4.1,1.62,-5.95),.045,METAL,8)
    # The physical adjustment wheel remains attached to the relocated stand.
    g.rod((-4.1,1.22,-5.95),(-3.947,1.22,-5.98),.018,METAL,8)
    ring(g,(-3.945,1.22,-5.98),.044,.015,METAL,(1,0,0),10)
    for offset in (-.03,.03):
        g.rod((-3.945,1.22,-5.98),(-3.945,1.22+offset,-5.98),.008,METAL,6)
    g.rod((-4.11,1.67,-6.05),(-4.11,1.67,-5.93),.19,METAL,16)
    g.rod((-4.11,1.67,-5.929),(-4.11,1.67,-5.915),.155,CABIN,16)
    dial_ticks(g,(-4.11,1.67,-5.907),.132)
    ring(g,(-4.11,1.67,-5.914),.17,.015,METAL,(0,0,1),16)
    for i,vertex in enumerate(g.vertices):
        x,y,z=source(vertex)
        g.vertices[i]=source((.92+(z+5.45),y-.10,6.95-(x+3.5)))
    obj(g,"ShippingScaleVisible",scale,mat)
    dial=empty("MOVE_ScaleNeedle",root,(.47,1.57,7.56));d=Geometry()
    d.rod((0,0,0),(0,.07,-.095),.009,DARK,6);obj(d,"ScaleNeedle",dial,mat)
    collision_box("ShippingScalePlatform",root,mat,(.92,.20,6.95),(1.45,.24,1.25))
    collision_box("ShippingScaleStand",root,mat,(.42,1.02,7.55),(.14,1.40,.38))
    anchor("WeighingLoad",root,(1.20,.32,6.95))
    anchor("WeighingWorker",root,(2.0,.08,6.95))
    anchor("ShippingScaleWorker",root,(2.0,.08,6.95))
    anchor("ShippingRestWorker",root,(2.0,.08,4.3))
    anchor("WeighingDial",root,(.47,1.57,7.56))
    # Retain the old target name for import compatibility, with the real
    # platform contact; no receiving-stage load is created by this anchor.
    anchor("ReceivingLoad",root,(1.20,.32,6.95))


def equipment(mat):
    root=empty("Equipment");g=Geometry()
    shipping_scale(root,mat)
    anchor("Receiver",root,(-3.4,.18,-6.5))
    # Wash trough has an open dark bowl, a lip, tap, bottom drain and hose.
    wash_start=len(g.vertices)
    g.role="Stainless"
    legs(g,-5,-3.4,2.25,1.2,1.1)
    chamfer(g,(-5,.87,-3.4),(2.25,.1,1.2),METAL,.035)
    for xx in (-6.08,-3.92):chamfer(g,(xx,1.06,-3.4),(.09,.34,1.2),CABIN,.012)
    for zz in (-3.96,-2.84):chamfer(g,(-5,1.06,zz),(2.1,.34,.09),CABIN,.012)
    g.box((-5,.936,-3.4),(1.95,.02,.91),METAL)
    g.rod((-5.75,.88,-3.4),(-5.75,.22,-3.4),.045,METAL,8)
    g.rod((-5.75,1.2,-3.92),(-5.75,1.57,-3.92),.035,METAL,8)
    g.rod((-5.75,1.57,-3.92),(-5.75,1.57,-3.57),.035,METAL,8)
    g.rod((-5.75,1.57,-3.57),(-5.75,1.46,-3.57),.035,METAL,8)
    ring(g,(-5.75,1.26,-3.92),.085,.016,METAL,(0,1,0),10)
    for dx,dz in ((-.075,0),(.075,0),(0,-.075),(0,.075)):
        g.rod((-5.75,1.26,-3.92),(-5.75+dx,1.26,-3.92+dz),.012,METAL,6)
    g.role=None
    # Sink drain and the visible water trap lead into the floor drain.
    g.rod((-4.35,.938,-3.38),(-4.35,.946,-3.38),.07,DARK,12)
    for dx in (-.035,0,.035):g.rod((-4.35+dx,.95,-3.425),(-4.35+dx,.95,-3.335),.007,METAL,6)
    g.rod((-5.75,.24,-3.4),(-5.75,.24,-3.65),.045,METAL,8)
    g.rod((-5.75,.24,-3.65),(-5.75,.185,-3.65),.045,METAL,8)
    shift_geometry(g,wash_start,(0,0,1.35))
    anchor("PreparationWorker",root,(-6.65,.18,-2.05));anchor("PreparationLeftHand",root,(-6.12,1.18,-2.25))
    anchor("PreparationRightHand",root,(-6.12,1.18,-1.85));anchor("PreparationLoad",root,(-5.7,.96,-2.05))
    prep=empty("MOVE_PreparationFish",root,(-5.7,.94,-2.05));p=Geometry()
    port.crate(p,0,0,0,True);obj(p,"PreparationFishVisible",prep,mat)
    # Fill/seam bench, guide rails and a hopper with a narrowed outlet.
    seam_start=len(g.vertices)
    g.role="Stainless"
    legs(g,-5,-.85,2.5,1.45,1.08)
    chamfer(g,(-5,1.06,-.85),(2.5,.18,1.45),METAL,.028)
    g.role="Rubber";g.box((-5,1.16,-.85),(2.27,.045,.61),DARK);g.role=None
    for zz in (-1.18,-.52):g.rod((-6.15,1.28,zz),(-3.85,1.28,zz),.021,METAL,8)
    g.rod((-5.72,1.55,-.85),(-5.72,2.11,-.85),.31,CABIN,12,end_radius=.46)
    g.rod((-5.72,1.3,-.85),(-5.72,1.55,-.85),.085,METAL,10,end_radius=.2)
    # A bolted feed flange and actual filler outlet join the hopper to line.
    g.role="Stainless"
    ring(g,(-5.72,1.57,-.85),.315,.025,METAL,(0,1,0),12)
    g.rod((-5.72,1.265,-.85),(-5.72,1.38,-.85),.055,METAL,10)
    g.role=None
    chamfer(g,(-4.36,1.67,-1.32),(.29,1.14,.24),PAINT)
    chamfer(g,(-4.36,2.18,-.95),(.6,.2,.94),PAINT)
    seamer=empty("MOVE_SeamerHead",root,(-4.36,1.62,-.35));s=Geometry()
    s.rod((0,0,0),(0,.48,0),.08,METAL,10);s.rod((0,-.07,0),(0,.03,0),.16,METAL,12)
    ring(s,(0,.06,0),.12,.018,METAL,(0,1,0),12)
    for side,label in ((-1,"Left"),(1,"Right")):
        # The two roll shafts belong to the existing descending seamer head.
        s.rod((side*.09,.12,0),(side*.19,.12,0),.035,PAINT,8)
        pivot=empty("MOVE_SeamerRoller"+label,seamer,(side*.19,-.018,0));roller=Geometry()
        roller.role="Stainless";roller.rod((0,-.05,0),(0,.05,0),.047,METAL,10)
        roller.role=None;roller.rod((.045,-.036,0),(.045,.036,0),.006,DARK,6)
        obj(roller,"SeamerRollerVisible"+label,pivot,mat)
    obj(s,"SeamerHeadVisible",seamer,mat)
    chamfer(g,(-3.89,1.55,-1.39),(.19,.3,.21),PAINT,.025)
    for y in (1.62,1.49):g.rod((-3.779,y,-1.39),(-3.754,y,-1.39),.036,DARK,8)
    shift_geometry(g,seam_start,(0,0,.5))
    # Indexed filling briefly carries the tray beyond the original west edge.
    # A small cantilevered roller shelf supports that sweep without changing
    # the station, worker route or its existing collision volume.
    g.role="Stainless"
    for z in (-.65,-.50,-.35,-.20,-.05):
        g.rod((-6.42,1.155,z),(-6.20,1.155,z),.035,METAL,10)
    for x in (-6.41,-6.21):g.rod((x,1.10,-.69),(x,1.10,-.01),.025,METAL,8)
    for z in (-.64,-.06):g.rod((-6.17,.89,z),(-6.41,1.10,z),.027,METAL,8)
    g.role=None
    # The original controls were at the far side of the bench. These two
    # physical buttons match the worker's existing reachable hand positions.
    chamfer(g,(-6.13,1.33,-.35),(.15,.22,.55),PAINT,.018)
    for name,z,color in (("FillControl",-.52,LAMP),("SealControl",-.18,DARK)):
        g.rod((-6.205,1.35,z),(-6.22,1.35,z),.034,color,10)
        anchor(name,root,(-6.22,1.35,z))
    indicator=Geometry();indicator.rod((-.014,0,0),(.014,0,0),.034,LAMP,10)
    indicator_root=empty("SeamerRunIndicator",root,(-3.741,1.62,-.89))
    obj(indicator,"SeamerIndicatorLens",indicator_root,mat)
    anchor("SeamerWorker",root,(-6.78,.18,-.35));anchor("SeamerLeftHand",root,(-6.24,1.2,-.52))
    anchor("SeamerRightHand",root,(-6.24,1.2,-.18));anchor("CanTray",root,(-5,1.19,-.35))
    # The finite empty-can and lid stock has named visibility groups. Runtime
    # exchanges these with the same tray during existing Fill/Seal phases.
    supply=Geometry()
    for x in range(5):
        for z in range(3):can_body(supply,-5.65-.28+x*.14,1.155,-.82-.14+z*.14)
    obj(supply,"CanSupply",root,mat)
    supply=Geometry()
    for stack in range(3):
        for level in range(5):can_lid(supply,-5.3+stack*.15,1.08+level*.011,.22)
    obj(supply,"LidSupply",root,mat)
    # Horizontal retort is a hollow pressure shell, with a clear front throat.
    center=(-5,1.35);zback=.65;zfront=2.8;steps=20;g.role="Stainless"
    for i in range(steps):
        a,b=i*math.tau/steps,(i+1)*math.tau/steps
        vertices=[(center[0]+r*math.cos(t),center[1]+r*math.sin(t),z)
                  for z in (zback,zfront) for r in (.70,.80) for t in (a,b)]
        g.add(vertices,[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)],METAL)
    g.rod((-5,1.35,.52),(-5,1.35,.66),.80,METAL,20)
    for z in (.88,2.53):
        ring(g,(-5,1.35,z),.815,.035,METAL,(0,0,1),20)
        for xx in (-5.62,-4.38):chamfer(g,(xx,.52,z),(.16,.65,.35),METAL,.025)
    g.role=None
    # Small backing pads, fasteners and drain valve are attached to the
    # pressure vessel, keeping its large uninterrupted barrel readable.
    for z in (.88,2.53):
        for side in (-1,1):
            g.rod((-5+side*.81,1.35,z-.045),(-5+side*.81,1.35,z+.045),.045,METAL,6)
    g.rod((-5,.61,1.8),(-5,.35,1.8),.045,METAL,8)
    ring(g,(-5,.40,1.8),.085,.014,METAL,(0,1,0),10)
    g.rod((-5,2.11,1.6),(-5,2.5,1.6),.062,METAL,8)
    g.rod((-5,2.5,1.6),(-7.65,2.5,1.6),.062,METAL,8)
    g.rod((-4.35,1.9,1.28),(-4.2,1.9,1.28),.155,METAL,16)
    g.rod((-4.199,1.9,1.28),(-4.19,1.9,1.28),.133,CABIN,16)
    ring(g,(-4.19,1.9,1.28),.143,.012,METAL,(1,0,0),16)
    dial_ticks(g,(-4.18,1.9,1.28),.114,(1,0,0))
    pressure=empty("MOVE_RetortPressureNeedle",root,(-4.17,1.9,1.28));needle=Geometry()
    needle.rod((0,-.012,0),(0,.085,.040),.007,DARK,6)
    needle.rod((-.009,0,0),(.009,0,0),.020,METAL,10)
    obj(needle,"RetortPressureNeedleVisible",pressure,mat)
    door=empty("MOVE_RetortDoor",root,(-5.82,1.35,2.91));d=Geometry()
    d.rod((.82,0,-.07),(.82,0,.07),.80,METAL,20)
    ring(d,(.82,0,.10),.63,.028,METAL,(0,0,1),20)
    ring(d,(.82,0,.16),.23,.023,METAL,(0,0,1),12)
    for angle in (0,math.pi*.5,math.pi,math.pi*1.5):
        d.rod((.82,0,.16),(.82+.23*math.cos(angle),.23*math.sin(angle),.16),.022,METAL,8)
    for index,angle in enumerate((0,math.pi*.5,math.pi,math.pi*1.5)):
        radial=Vector((math.cos(angle),math.sin(angle),0))
        location=Vector((.82,0,.16))+radial*.64
        lock=empty("MOVE_RetortLock"+str(index),door,tuple(location));clamp=Geometry()
        clamp.rod((0,0,-.03),(0,0,.035),.049,METAL,10)
        clamp.rod(-radial*.05+Vector((0,0,.033)),radial*.155+Vector((0,0,.033)),.033,METAL,8)
        clamp.rod(radial*.10+Vector((0,0,.01)),radial*.10+Vector((0,0,.095)),.020,DARK,8)
        obj(clamp,"RetortLockVisible"+str(index),lock,mat)
        lug=Vector((-5,1.35,2.795))+radial*.79
        chamfer(g,tuple(lug),(.105,.105,.11),METAL,.012)
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
    indicator=Geometry();indicator.rod((-.012,0,0),(.012,0,0),.027,LAMP,10)
    indicator_root=empty("RetortRunIndicator",root,(-5.855,1.49,2.72))
    obj(indicator,"RetortIndicatorLens",indicator_root,mat)
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
    # Runtime mechanisms resolve Equipment anchors, while the visible vent
    # belongs to the Hall shell; both markers identify the same real outlet.
    anchor("SteamOutlet",root,(-7.65,5.1,2.15))
    anchor("RetortBasketOut",root,(-5,1.02,3.34))
    # Cooling/packing bench and nearby finished stock rack are distinct.
    g.role="Stainless"
    legs(g,-5,5.05,2.8,1.1,1.05);chamfer(g,(-5,1.07,5.05),(2.8,.12,1.1),METAL,.025)
    for xx in (-6.35,-3.65):g.rod((xx,1.14,4.55),(xx,1.14,5.55),.025,METAL,8)
    for i in range(17):g.rod((-6.25+i*.155,1.145,4.6),(-6.25+i*.155,1.145,5.5),.016,METAL,6)
    g.role=None
    # A fixed tape dispenser is purposeful packaging equipment; no spare
    # cans, cartons or pallets pretend to be outside the finite stock cycle.
    chamfer(g,(-3.84,1.19,5.42),(.24,.10,.20),PAINT,.018)
    ring(g,(-3.84,1.31,5.42),.078,.028,ROPE,(1,0,0),12)
    g.rod((-3.94,1.31,5.42),(-3.74,1.31,5.42),.018,METAL,8)
    g.box((-3.84,1.26,5.30),(.19,.025,.045),METAL)
    # One open work carton fits west of the tray's cooling dock, with a real
    # gap between them. Kept only as an authored reference; runtime uses the
    # same independently movable CartonStack body from packing to shipment.
    carton=Geometry();carton.role="Cardboard"
    cx,cy,cz=-6.2725,1.14,5.05
    chamfer(carton,(cx,cy+.008,cz),(.255,.016,.70),WOOD,.004)
    for x in (cx-.1235,cx+.1235):chamfer(carton,(x,cy+.15,cz),(.008,.29,.70),WOOD,.003)
    for z in (cz-.346,cz+.346):chamfer(carton,(cx,cy+.15,z),(.239,.29,.008),WOOD,.003)
    # Creases and folded-back narrow flaps stay inside the bench footprint.
    for z in (cz-.32,cz+.32):
        chamfer(carton,(cx,cy+.298,z),(.237,.012,.045),WOOD,.003)
    obj(carton,"PackingCarton",root,mat)
    anchor("PackingBox",root,(cx,cy,cz))
    anchor("BoxPickupWorker",root,(-6.94,.18,5.0))
    for index in range(15):
        layer=index//10;slot=index%10
        x=cx+(-.058 if slot<5 else .058) if layer==0 else cx
        z=cz-.28+(slot%5)*.14
        # These are destinations for the actual CanUnit meshes. Baking a
        # second set here would double the batch during packing.
        anchor("PackingCan"+str(index).zfill(2),root,(x,cy+.018+layer*.104,z))
    anchor("PackingCartonLeftHand",root,(-6.40,1.47,4.89))
    anchor("PackingCartonRightHand",root,(-6.40,1.47,5.21))
    # Flat carton blanks rest at the unused east end, with no rendered words.
    g.role="Cardboard"
    for y in (1.145,1.163,1.181):
        chamfer(g,(-4.65,y,5.25),(.62,.016,.45),WOOD,.004)
        g.box((-4.65,y+.009,5.25),(.014,.002,.44),ROPE)
    g.role=None
    # A small feed lane takes each real can around the south of the carton
    # to a reachable pickup. Its support height equals the can's tray base.
    g.role="Stainless"
    for index in range(8):
        x=-5.40-index*.13
        if index==4:
            pivot=empty("MOVE_PackingFeedRoller",root,(x,1.19,4.62));roller=Geometry()
            roller.role="Stainless";roller.rod((0,0,-.064),(0,0,.064),.035,METAL,10)
            roller.role=None;roller.rod((0,.034,-.05),(0,.034,.05),.004,CABIN,6)
            obj(roller,"PackingFeedRollerVisible",pivot,mat)
        else:g.rod((x,1.19,4.556),(x,1.19,4.684),.035,METAL,10)
    for z in (4.548,4.692):
        g.rod((-5.39,1.20,z),(-6.32,1.20,z),.009,METAL,8)
        for x in (-5.4,-6.31):g.rod((x,1.135,z),(x,1.20,z),.018,METAL,8)
    g.role=None
    chamfer(g,(-5.50,1.177,4.62),(.16,.075,.15),PAINT,.012)
    anchor("PackingPickup",root,(-6.31,1.225,4.62))
    # Three empty pallet supports wait outside, south of the shipping ramp.
    # The receiver places each approved real carton here; the driver can
    # withdraw the jack east without occupying the public passage indoors.
    for i,z in enumerate((3.2,1.7,.2)):
        anchor("ReadyCase"+str(i),root,(.8,.08,z))
        anchor("ApprovedWorker"+str(i),root,(1.85,.08,z))
        for x in (.41,1.19):g.box((x,.086,z),(.018,.012,1.16),METAL)
    # The legacy capacity anchors are kept for passive import compatibility;
    # the finite delivery contract only materializes the three outdoor slots.
    for i in range(3,6):anchor("ReadyCase"+str(i),root,(-7.25+i*.85,.18,6.35))
    anchor("PackingWorker",root,(-6.94,.18,5.0));anchor("PackingLeftHand",root,(-6.39,1.19,4.8))
    anchor("PackingRightHand",root,(-6.39,1.19,5.2));anchor("CoolingLoad",root,(-5.75,1.19,5.05))
    anchor("PackingLoad",root,(-4.25,1.14,5.05));anchor("FinishedLoad",root,(-3.15,.18,6.1))
    anchor("RawDoor",root,(.3,.18,-5.5));anchor("FinishedDoor",root,(.3,.18,5))
    anchor("Observer",root,(-1.1,1.72,.5))
    for i in range(6):anchor("RawStore"+str(i),root,(-7.36+(i%3)*.83,.18,-6.15+(i//3)*2.55))
    # Powered feed skirts the closed retort shell on the east and approaches
    # the carrier from its open front. The north branch takes the same tray
    # to cooling/packing after the drawer comes back out.
    for run_index,(first,last,open_first,open_last) in enumerate((
            ((-3.76,-.35),(-3.1,-.35),False,True),
            ((-3.1,-.35),(-3.1,3.95),True,True),
            ((-3.1,3.95),(-5.75,3.95),True,True),
            ((-5.75,3.95),(-5.75,5.05),True,False))):
        roller_run(g,root,mat,run_index,first,last,open_first,open_last)
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
    crew_service_dressing(root,mat)
    obj(g,"EquipmentVisible",root,mat)
    for name,center,size in (("Wash",(-5,.71,-2.05),(2.25,1.06,1.2)),
                             ("Seamer",(-5,1.17,-.35),(2.5,1.98,1.45)),
                             ("Retort",(-5,1.35,1.7),(1.62,1.62,2.2)),
                             ("Packing",(-5,.68,5.05),(2.8,1,1.1)),
                             ("ConveyorEast",(-3.1,.70,1.8),(.88,1.04,4.3)),
                             ("ConveyorFront",(-4.425,.70,3.95),(3.53,1.04,.88))):
        collision_box(name,root,mat,center,size)
    return root


def aim_lift_cylinder(part, start, end):
    """Unity-space +Y bore; no mesh scaling, including the exported rest pose."""
    part.location = source(start)
    part.rotation_euler = Vector(source(Vector(end)-Vector(start))).to_track_quat('Z', 'Y').to_euler()


def pose_lift_fold_cylinders(root):
    parts={part.name.split('.')[0]:part for part in root.children_recursive}
    inverse=root.matrix_world.inverted()
    for side in ('Left','Right'):
        start=source(inverse@parts['ANCHOR_LiftFoldBase'+side].matrix_world.translation)
        end=source(inverse@parts['ANCHOR_LiftFoldTip'+side].matrix_world.translation)
        aim_lift_cylinder(parts['MOVE_LiftFoldBarrel'+side], start, end)
        aim_lift_cylinder(parts['MOVE_LiftFoldRod'+side], end, Vector(end)+(Vector(end)-Vector(start)))
    bpy.context.view_layer.update()


def truck_tail_lift_mechanism(root,lift,mat):
    # Column lift: the guides are bolted to the rear body/chassis frame. The
    # common sliding carriage carries the platform hinge, never a loose deck.
    fixed=Geometry()
    carriage=empty('MOVE_LiftCarriage',root,(0,1.2,-1.88));moving=Geometry()
    for side,sign in (('Left',-1),('Right',1)):
        x=sign*1.165
        anchor('LiftRailBottom'+side,root,(x,.14,-1.78))
        anchor('LiftRailTop'+side,root,(x,2.82,-1.78))
        # Open channel, visible polished running faces and end stops.
        chamfer(fixed,(x,1.48,-1.705),(.060,2.68,.028),METAL,.006)
        for dx in (-.021,.021):
            fixed.box((x+dx,1.48,-1.775),(.018,2.68,.145),METAL)
        for y in (.14,2.82):chamfer(fixed,(x,y,-1.775),(.067,.035,.15),METAL,.005)
        for y in (.70,1.15,2.05,2.76):
            # Flange and paired bolts show the load reaching the existing frame.
            chamfer(fixed,(sign*1.11,y,-1.725),(.13,.085,.045),METAL,.007)
            for xx in (sign*1.065,sign*1.155):
                fixed.rod((xx,y,-1.752),(xx,y,-1.770),.013,EDGE,6)
        # A fixed barrel long enough to contain the same solid rod throughout
        # its stroke. Hydraulic lines feed this fixed upper end.
        anchor('LiftCylinderBottom'+side,root,(x,1.43,-1.935))
        anchor('LiftCylinderTop'+side,root,(x,2.70,-1.935))
        fixed.rod((x,1.43,-1.935),(x,2.70,-1.935),.027,PAINT,10)
        for y in (1.445,2.675):
            fixed.rod((x,y-.019,-1.935),(x,y+.019,-1.935),.031,METAL,10)
        fixed.rod((x-.027,2.715,-1.935),(x+.027,2.715,-1.935),.029,METAL,8)
        fixed.rod((x,2.735,-1.935),(x,2.735,-1.75),.025,METAL,8)
        fixed.role='Rubber'
        for a,b in (((x,2.66,-1.925),(sign*1.11,2.64,-1.72)),
                    ((sign*1.11,2.64,-1.72),(sign*1.11,.68,-1.72)),
                    ((sign*1.11,.68,-1.72),(sign*.20,.68,-1.45))):
            fixed.rod(a,b,.012,DARK,6)
        fixed.role=None
        # The guide shoes and rollers are one rigid carriage with the hinge.
        chamfer(moving,(x,.16,.10),(.065,.34,.145),METAL,.008)
        for label,y in (('Lower',.06),('Upper',.26)):
            anchor('LiftGuide'+label+side,carriage,(x,y,.1))
            moving.rod((x-.029,y,.1),(x+.029,y,.1),.025,EDGE,8)
        chamfer(moving,(x,.16,.025),(.060,.080,.19),METAL,.006)
        moving.rod((x-.030,.16,-.055),(x+.030,.16,-.055),.028,METAL,10)
        anchor('LiftRamBase'+side,carriage,(x,.16,-.055))
        moving.rod((x,0,0),(sign*1.02,0,0),.040,METAL,10)
        anchor('LiftHinge'+side,carriage,(sign*1.02,0,0))
        # The small folding actuator is pinned to a forward carriage bracket.
        # It lives outside the walking deck, leaving the jack's full lane clear.
        moving.rod((x,.13,.1),(x,.15,.45),.028,METAL,8)
        moving.rod((x-.03,.15,.45),(x+.03,.15,.45),.031,METAL,8)
        anchor('LiftFoldBase'+side,carriage,(x,.15,.45))
        rod=empty('MOVE_LiftRam'+side,root,(x,1.36,-1.935));r=Geometry()
        r.rod((0,0,0),(0,1.24,0),.014,EDGE,10)
        obj(r,'LiftRamVisible'+side,rod,mat)
        anchor('LiftRamTop'+side,rod,(0,1.24,0))
        barrel=empty('MOVE_LiftFoldBarrel'+side,root);b=Geometry()
        b.rod((-.03,0,0),(.03,0,0),.030,METAL,8)
        b.rod((0,.04,0),(0,.48,0),.026,PAINT,10)
        b.rod((0,.465,0),(0,.5,0),.030,METAL,10)
        obj(b,'LiftFoldBarrelVisible'+side,barrel,mat)
        anchor('LiftFoldBarrelMouth'+side,barrel,(0,.5,0))
        rod=empty('MOVE_LiftFoldRod'+side,root);r=Geometry()
        r.rod((0,-.5,0),(0,-.035,0),.013,EDGE,10)
        r.rod((-.028,0,0),(.028,0,0),.030,METAL,8)
        r.rod((0,-.065,0),(0,0,0),.021,METAL,8)
        obj(r,'LiftFoldRodVisible'+side,rod,mat)
        anchor('LiftFoldRodEnd'+side,rod,(0,-.5,0))
        start=(x,1.35,-1.43);end=(x,1.12,-2.23)
        aim_lift_cylinder(barrel,start,end)
        aim_lift_cylinder(rod,end,Vector(end)+(Vector(end)-Vector(start)))
    moving.rod((-1.12,0,0),(1.12,0,0),.028,METAL,12)
    chamfer(fixed,(0,.63,-1.45),(.52,.25,.31),METAL,.025)
    for x in (-.16,.16):fixed.rod((x,.65,-1.62),(x,.65,-1.635),.024,METAL,8)
    obj(fixed,'LiftFrameVisible',root,mat)
    obj(moving,'LiftCarriageVisible',carriage,mat)
    bpy.context.view_layer.update()
    pose_lift_fold_cylinders(root)


def truck(mat):
    root=empty("Truck");g=Geometry();glass=Geometry();driverdoor=Geometry();doorglass=Geometry()
    driverhinge=empty("MOVE_DriverDoor",root,(-1.08,1.12,4.94+TRUCK_CAB_OFFSET))
    # Rear axle origin. The shorter chassis moves the full-sized cab as a unit;
    # the original seat, footwell and steering proportions are not scaled.
    for x in (-.73,.73):chamfer(g,(x,.69,1.25),(.18,.25,5.95),METAL,.03)
    for z in (-1.4,0,1.35,2.4,TRUCK_WHEELBASE):chamfer(g,(0,.7,z),(1.8,.16,.15),METAL,.02)
    for z in (0,TRUCK_WHEELBASE):g.rod((-1.12,.45,z),(1.12,.45,z),.09,METAL,10)
    for x in (-.63,.63):
        for z in (-.35,.1,.5):chamfer(g,(x,.45,z),(.12,.04,1.1),METAL,.008)
    # Closed insulated goods body; separate leaves expose the actual interior.
    g.role="Insulation"
    for center,size in (((0,1.15,.1),(2.4,.1,4.2)),((0,3.15,.1),(2.4,.1,4.2)),
                        ((-1.16,2.175,.45),(.08,1.95,3.5)),((1.16,2.175,.45),(.08,1.95,3.5)),
                        ((-1.16,2.335,-1.49),(.08,1.63,.38)),((1.16,2.335,-1.49),(.08,1.63,.38)),
                        ((0,2.175,2.18),(2.4,1.95,.08))):
        chamfer(g,center,size,CABIN,.024)
    g.role=None
    # Recess the rear posts instead of burying the lift inside insulation.
    # An inner steel liner keeps the cargo box closed; the service side exposes
    # both fixed lift barrels, guides and the lower folding-cylinder brackets.
    for sign in (-1,1):
        g.box((sign*1.105,2.175,-1.85),(.020,1.95,.30),METAL)
        g.box((sign*1.105,1.35,-1.50),(.020,.30,.40),METAL)
    g.role="Deck";g.box((0,1.204,.1),(2.21,.018,4.05),METAL);g.role=None
    for x in (-1.16,1.16):
        for y in (1.21,3.13):g.rod((x,y,-1.96),(x,y,2.20),.034,METAL,8)
        g.rod((x,1.21,2.2),(x,3.13,2.2),.034,METAL,8)
        inner=math.copysign(1.105,x)
        g.rod((inner,1.21,-1.965),(inner,3.13,-1.965),.022,METAL,8)
        for z in (-1.35,-.25,.85,1.95):g.box((x,2.16,z),(.018,1.88,.025),METAL)
    # The refrigeration head actually meets the insulated front wall. Fins,
    # guarded fan and short service lines stay inside the existing truck size.
    refrigerator_start=len(g.vertices)
    chamfer(g,(0,3.19,3.23),(1.18,.52,.29),CABIN,.045)
    for x in (-.25,.25):
        g.rod((x,3.20,3.38),(x,3.20,3.407),.175,DARK,12)
        ring(g,(x,3.20,3.414),.163,.012,METAL,(0,0,1),12)
        for offset in (-.09,-.045,0,.045,.09):
            g.rod((x+offset,3.07,3.42),(x+offset,3.33,3.42),.009,METAL,6)
    for x in (-.51,.51):
        g.rod((x,3.08,3.30),(x,2.98,3.30),.025,METAL,8)
        g.rod((x,2.98,3.30),(x,2.98,3.12),.025,METAL,8)
    shift_geometry(g,refrigerator_start,(0,-.33,TRUCK_CAB_OFFSET))
    # Chamfered cab and a sloped windscreen opening, not a solid painted block.
    # A real cab floor leaves the driver's footwell and open doorway empty.
    cab_start=len(g.vertices)
    chamfer(g,(0,1.09,4.27),(2.28,.12,2.10),PAINT,.035)
    chamfer(g,(0,2.87,4.15),(2.28,.18,2.02),PAINT,.075)
    chamfer(g,(0,1.9,3.24),(2.27,1.35,.16),PAINT,.045)
    # The cab nose closes from the bumper to the windscreen sill. The old
    # upper strip left daylight around the grille and headlamps; the short
    # side returns meet the door hinge line without filling the footwell.
    front=Geometry()
    chamfer(front,(0,1.44,5.19),(2.25,.78,.30),PAINT,.045)
    for x in (-1.065,1.065):
        chamfer(front,(x,1.44,5.075),(.12,.78,.27),PAINT,.025)
    shift_geometry(front,0,(0,0,TRUCK_CAB_OFFSET))
    obj(front,"TruckFrontPanel",root,mat)
    for x in (-1.06,1.06):
        g.rod((x,1.76,5.12),(x,2.80,4.96),.065,PAINT,8)
        g.rod((x,1.65,3.35),(x,2.78,3.35),.065,PAINT,8)
        if x>0:chamfer(g,(x,1.48,4.10),(.12,.65,1.72),PAINT,.03)
        chamfer(g,(x*1.04,.98,4.05),(.18,.13,1.2),METAL,.025)
        g.rod((x*1.03,2.2,4.93),(x*1.10,2.2,4.77),.025,METAL,6)
        chamfer(g,(x*1.08,2.22,4.77),(.1,.32,.19),DARK,.018)
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
    chamfer(g,(0,1.31,5.35),(.85,.25,.018),DARK,.008)
    for i in range(7):g.box((-.37+i*.123,1.31,5.371),(.05,.2,.02),METAL)
    for side,x in (("Left",-.88),("Right",.88)):
        # A recessed lens and solid gasket/bezel share one authored host.
        # Its front anchor stays outside the nose and inside the 5.4 m body
        # bound; runtime can drive this glass without relighting the grille.
        lamp=empty("Headlamp"+side,root,(x,1.4,5.37+TRUCK_CAB_OFFSET))
        housing=Geometry();housing.role="Rubber"
        housing.rod((0,0,-.028),(0,0,-.014),.149,DARK,16)
        housing.role=None
        ring(housing,(0,0,-.003),.129,.014,METAL,(0,0,1),16)
        obj(housing,"HeadlampHousing"+side,lamp,mat)
        lens=Geometry();lens.rod((0,0,-.025),(0,0,0),.115,LAMP,16)
        obj(lens,"TruckHeadlampGlass",lamp,mat)
        anchor("Headlamp"+side,root,(x,1.4,5.37+TRUCK_CAB_OFFSET))
    shift_geometry(g,cab_start,(0,0,TRUCK_CAB_OFFSET))
    shift_geometry(glass,0,(0,0,TRUCK_CAB_OFFSET))
    for x in (-.88,.88):
        chamfer(g,(x,.75,-1.91),(.29,.16,.12),RUST,.018)
    # Four formed wheel arches and flexible flaps fit the existing collision
    # envelope. The underbody remains open enough to see axles and suspension.
    for side in (-1,1):
        for z in (0,TRUCK_WHEELBASE):
            for index in range(8):
                a,b=index*math.pi/8,(index+1)*math.pi/8
                vertices=[(side*x,.45+r*math.sin(t),z+r*math.cos(t))
                          for x in (.86,1.195) for r in (.505,.56) for t in (a,b)]
                g.add(vertices,[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)],PAINT)
            g.role="Rubber";chamfer(g,(side*1.025,.27,z-.55),(.34,.39,.035),DARK,.01);g.role=None
        for z in (.70,1.85):g.rod((side*.76,.65,z),(side*1.10,.65,z),.035,METAL,8)
        chamfer(g,(side*1.10,.51,1.275),(.07,.17,1.4),METAL,.02)
    # Seat, pedals and steering ring are anchors for the shared driver rig.
    interior_start=len(g.vertices)
    for x in (-.56,.56):
        chamfer(g,(x,1.45,4.0),(.59,.22,.69),DARK,.065)
        chamfer(g,(x,1.83,3.77),(.59,.68,.16),DARK,.065)
    chamfer(g,(0,1.94,4.73),(1.95,.20,.22),METAL,.045)
    g.rod((-.56,1.28,4.45),(-.56,1.94,4.50),.035,METAL,8)
    ring(g,(-.56,2.01,4.50),.23,.022,DARK,(0,.8,.6),16)
    shift_geometry(g,interior_start,(0,0,TRUCK_CAB_OFFSET))
    for side,x in (("L",-1.0),("R",1.0)):
        for axle,z in (("F",TRUCK_WHEELBASE),("R",0)):
            pivot=empty("MOVE_Wheel"+axle+side,root,(x,.45,z));w=Geometry();w.role="Rubber"
            w.rod((-.16,0,0),(.16,0,0),.45,DARK,20);w.role=None
            for xx in (-.171,.171):
                w.rod((xx-.008,0,0),(xx+.008,0,0),.255,METAL,12)
                ring(w,(xx,0,0),.20,.025,METAL,(1,0,0),12)
                for angle in (0,math.pi/3,2*math.pi/3,math.pi,4*math.pi/3,5*math.pi/3):
                    y,zlocal=.145*math.cos(angle),.145*math.sin(angle)
                    w.rod((xx-.012,y,zlocal),(xx+.012,y,zlocal),.023,METAL,6)
            obj(w,"WheelVisible"+axle+side,pivot,mat)
    for side,x in (("Left",-1.02),("Right",1.02)):
        # Hinge inboard of the exposed columns: the opened leaf and its
        # hinge straps must clear the hydraulic barrel behind the rear post.
        for y in (1.42,2.785):
            g.rod((math.copysign(1.105,x),y,-1.80),(x,y,-1.86),.023,METAL,8)
        pivot=empty("MOVE_TruckRearDoor"+side,root,(x,1.23,-1.86));d=Geometry()
        direction=1 if x<0 else -1
        d.role="Insulation";chamfer(d,(direction*.575,1.1,0),(1.15,2.2,.09),CABIN,.02);d.role=None
        d.rod((direction*.86,.2,-.075),(direction*.86,2.02,-.075),.025,METAL,8)
        for y in (.22,1.8):d.rod((0,y,0),(direction*.34,y,-.08),.038,METAL,8)
        d.role="Rubber"
        for xlocal in (direction*.025,direction*1.125):d.box((xlocal,1.10,-.049),(.018,2.15,.013),DARK)
        for y in (.035,2.165):d.box((direction*.575,y,-.049),(1.1,.018,.013),DARK)
        d.role=None
        for y in (.22,1.8):
            d.rod((0,y-.09,-.03),(0,y+.09,-.03),.045,METAL,10)
            chamfer(d,(direction*.86,y,-.082),(.11,.15,.035),METAL,.008)
        d.rod((direction*.86,.85,-.095),(direction*.56,.77,-.095),.026,METAL,8)
        chamfer(d,(direction*.56,.77,-.105),(.11,.06,.048),DARK,.01)
        # Resize only the goods leaf; its mechanisms retain proper thickness.
        d.vertices=[(v[0]*1.01/1.15,v[1],v[2]*1.9/2.2) for v in d.vertices]
        obj(d,"RearDoorVisible"+side,pivot,mat)
    lift=empty("MOVE_TailLift",root,(0,1.2,-1.88));l=Geometry()
    l.role="Deck";chamfer(l,(0,-.055,-1),(2.24,.11,2),METAL,.02);l.role=None
    for x in (-.82,.82):
        l.rod((x,-.055,-.15),(x,-.055,-1.9),.035,METAL,8)
        l.rod((x-.055,-.03,-.16),(x+.055,-.03,-.16),.06,METAL,10)
    for side,sign in (('Left',-1),('Right',1)):
        x=sign*1.165
        l.rod((sign*.965,0,0),(sign*1.075,0,0),.046,METAL,10)
        anchor('LiftPlatformHinge'+side,lift,(sign*1.02,0,0))
        l.rod((sign*1.07,-.055,-.35),(x,-.08,-.35),.025,METAL,8)
        l.rod((x-.028,-.08,-.35),(x+.028,-.08,-.35),.030,METAL,10)
        anchor('LiftFoldTip'+side,lift,(x,-.08,-.35))
    for z in (-.1,-1.91):l.box((0,.006,z),(2.14,.012,.045),EDGE)
    obj(l,"TailLiftVisible",lift,mat);anchor("TailLiftLoad",lift,(0,.01,-.8))
    truck_tail_lift_mechanism(root,lift,mat)
    anchor("TruckDriver",root,(-.56,1.56,4.0+TRUCK_CAB_OFFSET));anchor("DriverLeftHand",root,(-.76,2.01,4.5+TRUCK_CAB_OFFSET))
    anchor("DriverRightHand",root,(-.36,2.01,4.5+TRUCK_CAB_OFFSET));anchor("DriverFoot",root,(-.55,1.17,4.55+TRUCK_CAB_OFFSET))
    anchor("TruckRear",root,(0,1.2,TRUCK_REAR));anchor("TruckGroundBehind",root,(0,0,-4.05))
    for i in range(6):anchor("TruckCargo"+str(i),root,(-.53 if i%2==0 else .53,1.22,-1.25+(i//2)*1.3))
    anchor("TruckEngine",root,(0,1.3,4.6+TRUCK_CAB_OFFSET))
    anchor("ReverseAlarm",root,(0,.9,-1.95))
    anchor("DriverExit",root,(-1.73,0,4.05+TRUCK_CAB_OFFSET))
    anchor("DriverRearWalk",root,(-1.73,0,-4.35))
    anchor("DriverReverseLeftHand",root,(-1.06,2.05,3.35+TRUCK_CAB_OFFSET))
    obj(g,"TruckVisible",root,mat);obj(glass,"CabinGlass",root,mat)
    # Vehicle movement controller may disable these when computing its sweeps.
    collision_box("TruckBody",root,mat,(0,1.67,1.25),(2.4,3.06,6.5))
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
        g.role="Stainless";chamfer(g,(0,.016,0),(.76,.032,.46),METAL,.008)
        for x in range(5):
            for z in range(3):
                index=x*3+z;xx,zz=-.28+x*.14,-.14+z*.14
                unit=empty("CanUnit"+str(index).zfill(2),root,(xx,.035,zz))
                body=Geometry();can_body(body,0,0,0)
                obj(body,"CanBody"+str(index).zfill(2),unit,mat)
                contents=Geometry();can_contents(contents,0,0,0)
                obj(contents,"CanContents"+str(index).zfill(2),unit,mat)
                lid=Geometry();can_lid(lid,0,0,0)
                obj(lid,"CanLid"+str(index).zfill(2),unit,mat)
        anchor("Load",root,(0,.13,0))
    elif name=="CartonStack":
        # One finite finished carton replaces the former eight-box proxy.
        # Its detachable empty support never adds another finished unit.
        support=empty("ShippingPallet",root);wood=Geometry()
        for x in (-.31,0,.31):
            for z in (-.48,0,.48):chamfer(wood,(x,.064,z),(.15,.128,.18),WOOD,.013)
        for z in (-.49,0,.49):chamfer(wood,(0,.022,z),(.8,.044,.18),WOOD,.008)
        for x in (-.32,-.16,0,.16,.32):chamfer(wood,(x,.15,0),(.14,.044,1.2),WOOD,.008)
        obj(wood,"ShippingPalletVisible",support,mat)
        anchor("ShippingPalletTop",support,(0,.172,0))
        body=Geometry();body.role="Cardboard"
        chamfer(body,(0,.008,0),(.255,.016,.70),WOOD,.004)
        for x in (-.1235,.1235):chamfer(body,(x,.15,0),(.008,.29,.70),WOOD,.003)
        for z in (-.346,.346):chamfer(body,(0,.15,z),(.239,.29,.008),WOOD,.003)
        obj(body,"CartonBody",root,mat)
        for name,sign in (("Left",-1),("Right",1)):
            flap=empty("MOVE_CartonFlap"+name,root,(sign*.1275,.300,0));panel=Geometry()
            panel.role="Cardboard"
            chamfer(panel,(-sign*.06375,0,0),(.1275,.008,.70),WOOD,.003)
            obj(panel,"CartonFlap"+name,flap,mat)
        seal=Geometry();seal.role="Cardboard"
        seal.box((0,.306,0),(.035,.004,.69),ROPE)
        obj(seal,"CartonSeal",root,mat)
        anchor("CartonSupport",root,(0,0,0))
        anchor("CartonLeftGrip",root,(0,.14,-.354))
        anchor("CartonRightGrip",root,(0,.14,.354))
        anchor("Load",root,(0,0,0))
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
    if g.faces:obj(g,name+"Visible",root,mat)
    return root


def workwear(mat):
    """Two thin cloth panels; runtime attaches them to the existing body rig.

    Each group is centered, Unity +Z faces away from the wearer. Its measured
    local coordinates remain metres; no hidden template lives in the hall.
    """
    root=empty("Workwear")
    for name,width,height in (("ApronBib",.38,.42),("ApronSkirt",.46,.52)):
        g=Geometry();g.role="Fabric";columns,rows=6,5;vertices=[]
        for layer in (0,1):
            for row in range(rows+1):
                t=row/rows;y=-height*.5+t*height
                spread=(1-.26*t) if name=="ApronBib" else (1-.14*t)
                for column in range(columns+1):
                    u=-1+2*column/columns
                    # Gentle body curvature and two broad folds produce cloth,
                    # with a six-millimetre thickness rather than a board.
                    z=.018*(1-u*u)+.006*math.cos(u*math.pi*2)*(1-t)-layer*.006
                    vertices.append((u*width*.5*spread,y,z))
        layer_size=(columns+1)*(rows+1);faces=[]
        for row in range(rows):
            for column in range(columns):
                a=row*(columns+1)+column;b=a+1;c=b+columns+1;d=a+columns+1
                faces.extend(((a,b,c,d),(d+layer_size,c+layer_size,b+layer_size,a+layer_size)))
        perimeter=list(range(columns+1))+[row*(columns+1)+columns for row in range(1,rows+1)]+\
            [rows*(columns+1)+column for column in range(columns-1,-1,-1)]+\
            [row*(columns+1) for row in range(rows-1,0,-1)]
        for index,a in enumerate(perimeter):
            b=perimeter[(index+1)%len(perimeter)];faces.append((a,a+layer_size,b+layer_size,b))
        g.add(vertices,faces,CABIN)
        # The hems are fine cloth seams. A shallow bib pocket gives a useful
        # silhouette at working distance and carries no arbitrary objects.
        if name=="ApronBib":
            for x in (-.087,.087):g.rod((x,-.11,.021),(x,-.01,.021),.004,CABIN,6)
            g.rod((-.087,-.11,.021),(.087,-.11,.021),.004,CABIN,6)
        obj(g,name,root,mat)
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
    # A few tyre scuffs belong to the existing straight parking envelope;
    # there are no new kerbs, wheel stops or props in the turning apron.
    g.role="Plain"
    for x in (3.97,6.03):
        for z,length in ((-3.2,.72),(-1.75,.51),(.7,.83),(2.6,.48)):
            chamfer(g,(x,.087,z),(.10,.003,length),METAL,.001)
    g.role=None
    obj(g,"YardVisible",root,mat);return root


def build(mat):
    return [hall(mat),equipment(mat),truck(mat)]+[small_part(n,mat) for n in NAMES[3:8]]+[yard(mat),workwear(mat)]


def validate_crew_service(root,points):
    for name,expected in CREW_SERVICE_ANCHORS.items():
        if math.dist(points['ANCHOR_'+name],expected)>.0001:
            raise RuntimeError('Cannery crew service anchor differs from its metre contact: '+name)
    if math.dist(points['MOVE_PreparationCloth'],CREW_SERVICE_ANCHORS['PreparationTidyHand'])>.0001:
        raise RuntimeError('Preparation cloth lost its resting hand contact')
    # Measure the new meshes against the body corridors actually used by
    # crew life and the pre-existing retort-to-packing route. Bounds are
    # conservative here: even empty space inside each fixture must stay clear.
    paths=[(points['ANCHOR_PreparationWorker'],points['ANCHOR_PreparationTidyWorker']),
           (points['ANCHOR_SeamerWorker'],points['ANCHOR_SeamerRestWorker']),
           (points['ANCHOR_Receiver'],points['ANCHOR_ReceiverRestWorker']),
           (points['ANCHOR_RetortOperator'],(-7.18,.18,2.55)),
           ((-7.18,.18,2.55),(-7.18,.18,5.0)),
           ((-7.18,.18,5.0),points['ANCHOR_PackingWorker'])]
    inverse=root.matrix_world.inverted()
    for name in ('ServiceShelf','ServiceStool','StoredWorkJacket','ServiceLampHousing'):
        group=next(part for part in root.children if part.name.split('.')[0]==name)
        vertices=[source(inverse@part.matrix_world@v.co)
                  for part in group.children_recursive if part.type=='MESH' for v in part.data.vertices]
        if not vertices:raise RuntimeError('Cannery service fixture has no measured mesh: '+name)
        low=[min(v[i] for v in vertices) for i in range(3)]
        high=[max(v[i] for v in vertices) for i in range(3)]
        if low[0]<-7.89 or high[0]>-6.80 or low[2]<-2.84 or high[2]>1.0:
            raise RuntimeError('Cannery service fixture leaves its wall pocket: '+name)
        if low[1]>2.08:continue
        for first,last in paths:
            for step in range(81):
                point=Vector(first).lerp(Vector(last),step/80)
                dx=max(low[0]-point.x,0,point.x-high[0])
                dz=max(low[2]-point.z,0,point.z-high[2])
                if math.hypot(dx,dz)<.29:
                    raise RuntimeError('Cannery service fixture crosses a crew body corridor: '+name)


def validate_shipping(equipment_root,carton_root,points):
    def vertices(group):
        return [source(equipment_root.matrix_world.inverted()@part.matrix_world@v.co)
                for part in [group]+list(group.children_recursive)
                if part.type=='MESH' for v in part.data.vertices]
    scale=next(p for p in equipment_root.children if p.name.split('.')[0]=='ShippingScale')
    mesh_points=vertices(scale)
    low=[min(p[i] for p in mesh_points) for i in range(3)]
    high=[max(p[i] for p in mesh_points) for i in range(3)]
    if low[0]<.17 or low[2]<6.30 or high[0]>1.67 or high[2]>7.80 or abs(low[1]-.08)>.0001:
        raise RuntimeError('The entire scale must stay in its outdoor shipping pocket, clear of wall/ramp')
    if abs(points['ANCHOR_WeighingLoad'][1]-.32)>.0001:
        raise RuntimeError('Weighed carton base does not contact the real scale platform')
    # Body circles on the actual rerouted east and north crew corridors.
    for first,last in (((2.75,.08,-2.7),(2.75,.08,8.1)),((2.75,.08,8.1),(-8.55,.08,8.1))):
        for step in range(101):
            p=Vector(first).lerp(Vector(last),step/100)
            dx=max(low[0]-p.x,0,p.x-high[0]);dz=max(low[2]-p.z,0,p.z-high[2])
            if math.hypot(dx,dz)<.29:
                raise RuntimeError('Outdoor scale obstructs the crew body corridor')
    # SAT against the real reverse/exit arc and straight bay, including the
    # nose and rear overhang. Parking-only checks miss a swept vehicle corner.
    scale_corners=[Vector((x,z)) for x in (low[0],high[0]) for z in (low[2],high[2])]
    plan_text=(ROOT/'Assets/Scripts/Runtime/World/CityCanneryPlan.cs').read_text(encoding='utf-8')
    radius=float(re.search(r'TurningRadius\s*=\s*([\d.]+)f',plan_text).group(1))+.25
    poses=[]
    for step in range(129):
        a=math.pi*.5*step/128
        poses.append((Vector((-1.25+radius*math.sin(a),6.75+radius*math.cos(a))),
                      Vector((-math.cos(a),math.sin(a))),Vector((math.sin(a),math.cos(a)))))
    poses.extend((Vector((5,6.75-8.05*step/128)),Vector((0,1)),Vector((1,0))) for step in range(129))
    for centre,forward,right in poses:
        corners=[centre+forward*z+right*x for x in (-TRUCK_HALF_WIDTH-.1,TRUCK_HALF_WIDTH+.1)
                 for z in (TRUCK_REAR-.1,TRUCK_FRONT+.1)]
        separated=False
        for axis in (Vector((1,0)),Vector((0,1)),forward,right):
            scale_projection=[p.dot(axis) for p in scale_corners]
            truck_projection=[p.dot(axis) for p in corners]
            if max(scale_projection)<min(truck_projection) or max(truck_projection)<min(scale_projection):
                separated=True;break
        if not separated:raise RuntimeError('Outdoor scale intersects the swept factory truck envelope')
    for i,z in enumerate((3.2,1.7,.2)):
        if math.dist(points['ANCHOR_ReadyCase'+str(i)],(.8,.08,z))>.0001:
            raise RuntimeError('Approved carton support left its finite outdoor slot')
        if z+.6>3.9 or .8-.4<.12:
            raise RuntimeError('Approved carton support crosses the shipping ramp or facade')
    carton_points={a['name']:a['position'] for a in port.describe(carton_root)['anchors']}
    required={'CartonBody','ShippingPallet','MOVE_CartonFlapLeft','MOVE_CartonFlapRight','CartonSeal'}
    if not required.issubset({p.name.split('.')[0] for p in carton_root.children}):
        raise RuntimeError('Single carton lost its independently moving body/flaps/support')
    for key,expected in (('ANCHOR_CartonSupport',(0,0,0)),('ANCHOR_CartonLeftGrip',(0,.14,-.354)),
                         ('ANCHOR_CartonRightGrip',(0,.14,.354)),('ANCHOR_ShippingPalletTop',(0,.172,0))):
        if math.dist(carton_points[key],expected)>.0001:
            raise RuntimeError('Single carton/support contact differs: '+key)
    for index in range(15):
        p=Vector(points['ANCHOR_PackingCan'+str(index).zfill(2)])-Vector(points['ANCHOR_PackingBox'])
        if p.x-.057<-.1195 or p.x+.057>.1195 or abs(p.z)+.057>.342 or p.y+.102>.295:
            raise RuntimeError('A real packing can is outside the continuous finished carton')


def validate(roots):
    port.validate_surfaces(roots)
    entries={r.name.split('.')[0]:port.describe(r) for r in roots}
    if set(entries)!=set(NAMES):raise RuntimeError("Cannery pack is missing a required model")
    truck_points={a['name']:a['position'] for a in entries['Truck']['anchors']}
    dimensions=(ROOT/'Assets/Scripts/Runtime/World/CityCanneryTruckDimensions.cs').read_text(encoding='utf-8')
    for name,expected in (('Rear',TRUCK_REAR),('Front',TRUCK_FRONT),('HalfWidth',TRUCK_HALF_WIDTH),
                          ('Wheelbase',TRUCK_WHEELBASE),('CabOffset',TRUCK_CAB_OFFSET)):
        actual=re.search(r'const float '+name+r'\s*=\s*([-\d.]+)f',dimensions)
        if actual is None or abs(float(actual.group(1))-expected)>.0001:
            raise RuntimeError('Truck authored dimensions disagree with the shared runtime '+name)
    for i in range(6):
        x,y,z=truck_points['ANCHOR_TruckCargo'+str(i)]
        if abs(x)+.4>1.1 or z-.6< -1.95 or z+.6>2.1 or abs(y-1.22)>.001:
            raise RuntimeError("Cannery pallet does not fit the real closed truck box")
    for name,position in (('TruckDriver',(-.56,1.56,3.1)),('DriverLeftHand',(-.76,2.01,3.6)),
                          ('DriverRightHand',(-.36,2.01,3.6)),('DriverFoot',(-.55,1.17,3.65))):
        if math.dist(truck_points['ANCHOR_'+name],position)>.001:
            raise RuntimeError('The compact truck must preserve the full-sized driver contact arrangement')
    truck_root=next(r for r in roots if r.name.split('.')[0]=='Truck')
    front=next(part for part in truck_root.children_recursive
               if part.type=='MESH' and part.name.split('.')[0]=='TruckFrontPanel__Steel')
    vertices=[truck_root.matrix_world.inverted()@front.matrix_world@v.co for v in front.data.vertices]
    faces=[list(p.vertices) for p in front.data.polygons]
    front_tree=port.BVHTree.FromPolygons(vertices,faces)
    for x in (-1.02,-.88,0,.88,1.02):
        for y in (1.12,1.32,1.58):
            hit,_,_,_=front_tree.ray_cast(Vector(source((x,y,5.42+TRUCK_CAB_OFFSET))),Vector(source((0,0,-1))),.5)
            if hit is None or not 5.335+TRUCK_CAB_OFFSET<source(hit)[2]<5.345+TRUCK_CAB_OFFSET:
                raise RuntimeError('Truck front panel leaves a daylight gap around the grille or headlamps')
    for side,x in (("Left",-.88),("Right",.88)):
        if math.dist(truck_points['ANCHOR_Headlamp'+side],(x,1.4,5.37+TRUCK_CAB_OFFSET))>.001:
            raise RuntimeError('Truck headlamp anchor does not match the front lens')
        lamp=next(part for part in truck_root.children_recursive if part.name.split('.')[0]=='Headlamp'+side)
        lenses=[part for part in lamp.children_recursive
                if part.type=='MESH' and part.name.split('.')[0]=='TruckHeadlampGlass']
        if len(lenses)!=1:
            raise RuntimeError('Truck headlamp lacks its independently driven glass')
    if entries['Truck']['bounds_max'][2]>TRUCK_FRONT+.0001 or entries['Truck']['bounds_max'][0]>TRUCK_HALF_WIDTH+.0001 or entries['Truck']['bounds_min'][0]<-TRUCK_HALF_WIDTH-.0001:
        raise RuntimeError('Truck nose or headlamp details exceed the existing vehicle envelope')
    # Verify the driving silhouette, including the folded working platform.
    # The ordinary export keeps it extended so its finite contact is measurable.
    lift=next(part for part in truck_root.children_recursive if part.name.split('.')[0]=='MOVE_TailLift')
    rest=lift.rotation_euler.copy()
    lift.rotation_euler.x=-math.pi*.5
    bpy.context.view_layer.update()
    pose_lift_fold_cylinders(truck_root)
    folded=port.describe(truck_root)
    lift.rotation_euler=rest
    bpy.context.view_layer.update()
    pose_lift_fold_cylinders(truck_root)
    entries['Truck']['folded_bounds_min']=folded['bounds_min']
    entries['Truck']['folded_bounds_max']=folded['bounds_max']
    if folded['bounds_min'][2]<TRUCK_REAR-.001 or folded['bounds_max'][1]>3.201:
        raise RuntimeError('Folded lift or rear fittings exceed the compact driving envelope')
    # The public corridor remains 1.84 metres wide between east wall and the
    # partition. Hall wall openings are measured from actual kit geometry.
    if entries['Hall']['bounds_max'][0]>.4:raise RuntimeError("Hall intrudes into the service strip")
    hall_root=next(r for r in roots if r.name.split('.')[0]=='Hall')
    vertices=[];faces=[];inverse=hall_root.matrix_world.inverted()
    for part in hall_root.children_recursive:
        if part.type!='MESH':continue
        first=len(vertices)
        vertices.extend(inverse@part.matrix_world@v.co for v in part.data.vertices)
        faces.extend(tuple(first+i for i in face.vertices) for face in part.data.polygons)
    hall_tree=port.BVHTree.FromPolygons(vertices,faces)
    # Test the real visible and collision meshes together: a cut in the
    # structural wall alone would leave the washable lining across the door.
    for z in (2.3,2.65,3.0):
        for y in (.3,.8,1.35,1.9,2.15):
            hit,_,_,_=hall_tree.ray_cast(Vector(source((-8.5,y,z))),Vector(source((1,0,0))),1.0)
            if hit is not None:raise RuntimeError('Cannery west staff doorway is obstructed by hall geometry')
    for z in (1.6,3.7):
        hit,_,_,_=hall_tree.ray_cast(Vector(source((-8.5,1.3,z))),Vector(source((1,0,0))),1.0)
        if hit is None:raise RuntimeError('Cannery west staff doorway lost its solid side wall')
    if entries['Truck']['triangles']>22000 or entries['Equipment']['triangles']>65000:
        raise RuntimeError("Cannery mesh kit exceeds compact game-slice budget")
    # Explicit storage geometry: a 1.35 m centre aisle reaches all six
    # raw units and the 1.5 m door; no bin is authored inside its wall.
    points={a['name']:a['position'] for a in entries['Equipment']['anchors']}
    validate_crew_service(next(r for r in roots if r.name.split('.')[0]=='Equipment'),points)
    validate_shipping(next(r for r in roots if r.name.split('.')[0]=='Equipment'),
                      next(r for r in roots if r.name.split('.')[0]=='CartonStack'),points)
    # The tidy stance faces the same cloth from clear floor. Reserve 10 cm
    # beyond the body radius for the walking/head pose along the approach;
    # the old endpoint put the body exactly against the cold-room wall.
    first,last=Vector(points['ANCHOR_PreparationWorker']),Vector(points['ANCHOR_PreparationTidyWorker'])
    for step in range(21):
        position=first.lerp(last,step/20)
        for height in (.45,1.0,1.6):
            sample=position+Vector((0,height,0))
            _,_,_,distance=hall_tree.find_nearest(Vector(source(sample)))
            if distance is None or distance<.39:
                raise RuntimeError('Preparation tidy approach lacks body/head clearance from hall geometry')
    for i in range(6):
        x,y,z=points['ANCHOR_RawStore'+str(i)]
        if x-.4< -7.88 or x+.4> -5.26 or z-.6< -6.79 or z+.6> -2.96:
            raise RuntimeError('Raw fish unit intersects the real cold-store wall')
    aisle=(points['ANCHOR_RawStore3'][2]-.6)-(points['ANCHOR_RawStore0'][2]+.6)
    if aisle<1.3:raise RuntimeError('Cold-store central pallet-jack aisle is too narrow')
    for index in range(15):
        x,y,z=points['ANCHOR_PackingCan'+str(index).zfill(2)]
        if x-.057 < -6.392 or x+.057 > -6.153 or z-.057 < 4.708 or z+.057 > 5.392 or y+.102>1.435:
            raise RuntimeError('Same-batch can does not fit inside the open work carton')
    for index in range(8):
        if 'MOVE_ConveyorRoller'+str(index).zfill(2) not in points:
            raise RuntimeError('Cannery driven roller subset is incomplete')
    tray_root=next(r for r in roots if r.name.split('.')[0]=='CanTray')
    units=[part for part in tray_root.children if part.name.startswith('CanUnit')]
    if len(units)!=15:raise RuntimeError('The finite tray must own exactly fifteen transferable cans')
    # Inspect the actual body mesh, independently of the switchable filling
    # and lid. A top-down ray must enter its open mouth and reach the bottom.
    for index,unit in enumerate(sorted(units,key=lambda part:part.name)):
        names={part.name.split('.')[0] for part in unit.children}
        expected={name+str(index).zfill(2) for name in ('CanBody','CanContents','CanLid')}
        if names!=expected:raise RuntimeError('A can lost one of its distinct finite states')
        body=next(part for part in unit.children if part.name.startswith('CanBody'))
        vertices=[];faces=[]
        for part in body.children_recursive:
            if part.type!='MESH':continue
            start=len(vertices)
            vertices.extend(unit.matrix_world.inverted()@part.matrix_world@v.co for v in part.data.vertices)
            faces.extend([start+i for i in polygon.vertices] for polygon in part.data.polygons)
        tree=port.BVHTree.FromPolygons(vertices,faces)
        hit,_,_,_=tree.ray_cast(Vector(source((0,.20,0))),Vector(source((0,-1,0))),.25)
        if hit is None or abs(source(hit)[1]-.008)>.0001:
            raise RuntimeError('An empty can has no real open mouth and closed inner bottom')
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
    for name in NAMES[3:8]+('Workwear',):
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


def source_surface_materials(roots):
    """New albedos are packed in the editable source after deterministic FBX export."""
    specs={"CanneryFloor":("Floor",(.61,.68,.65),.24,0),
           "WetFloor":("Floor",(.43,.52,.49),.58,0),
           "WashWall":("WashWall",(.74,.81,.76),.26,0),
           "Stainless":("Stainless",(.87,.93,.90),.40,.32),
           "Insulation":("Insulation",(.80,.85,.79),.21,0),
           "Cardboard":("Cardboard",(.78,.78,.73),.04,0)}
    materials={}
    def linear(c):return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    for role,(stem,tint,smoothness,metallic) in specs.items():
        texture=ROOT/'ArtSource/City/Cannery/Textures'/('Cannery'+stem+'Albedo.png')
        if not texture.is_file():raise RuntimeError('Missing cannery source surface: '+str(texture))
        material=bpy.data.materials.new('CannerySurface_'+role);material.use_nodes=True
        nodes=material.node_tree.nodes;links=material.node_tree.links;shader=nodes.get('Principled BSDF')
        tex=nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(texture),check_existing=True);tex.image.pack()
        tex.extension='REPEAT';tex.interpolation='Linear'
        multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
        multiply.inputs[2].default_value=tuple(linear(c) for c in tint)+(1,)
        links.new(tex.outputs['Color'],multiply.inputs[1]);links.new(multiply.outputs[0],shader.inputs['Base Color'])
        shader.inputs['Roughness'].default_value=1-smoothness;shader.inputs['Metallic'].default_value=metallic
        materials[role]=material
    for root in roots:
        for part in root.children_recursive:
            if part.type!='MESH' or '__' not in part.name:continue
            role=part.name.rsplit('__',1)[1].split('.')[0]
            if role in materials:part.data.materials[0]=materials[role]


def main():
    p=argparse.ArgumentParser();p.add_argument('--no-preview',action='store_true')
    modes=p.add_mutually_exclusive_group()
    modes.add_argument('--validate-only',action='store_true')
    modes.add_argument('--preview-only',action='store_true',
                       help='Validate against the saved manifest and render only the source review PNG')
    p.add_argument('--model-dir',type=Path,default=ROOT/'Assets/Resources/City/Cannery')
    p.add_argument('--source-dir',type=Path,default=ROOT/'ArtSource/City/Cannery')
    p.add_argument('--only-part',choices=NAMES,action='append',
                   help='Export only these changed parts; repeat for a coherent multi-part edit')
    args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.preview_only and (args.no_preview or args.only_part):
        p.error('--preview-only cannot be combined with --no-preview or --only-part')
    port.base.reset();mat=port.base.material('CannerySharedPortSurfaces')
    roots=build(mat);entries=validate(roots)
    manifest={'design_id':'city_compact_fish_cannery_v1','generator':Path(__file__).name,
              'coordinate_system':'Unity +Y up / +Z truck forward; metres; truck rear axle ground origin',
              'parts':entries,'hall_floor_height':.18,'yard_height':.08,'truck_wheelbase':TRUCK_WHEELBASE,'truck_wheel_radius':.45,
              'truck_body_xz':[-TRUCK_HALF_WIDTH,TRUCK_HALF_WIDTH,TRUCK_REAR,TRUCK_FRONT],'pallet_size_xz':[.8,1.2],
              'truck_body_top':3.2,'truck_cab_translation_z':TRUCK_CAB_OFFSET,'truck_cargo_row_spacing':1.3,
              'cold_store_aisle_width':1.35,'pallet_jack_fork_top':.128,'pallet_jack_carry_lift':.03,
              'conveyor_top_height':1.19,'retort_basket_tray_height':.17,'retort_basket_contains_cans':False,
              'tail_lift_length':2.0,'tail_lift_operator_rear_clearance':.20,
              'tail_lift_operator_offset':1.0,'ground_trolley_operator_offset':1.43,
              'goods_threshold_ramp_length':1.5,'goods_threshold_ramp_rise':.1,
              'driveway_grid_xz':[6,4],'driveway_runtime_profile':'CityCanneryPlan.ApronTop',
              'driver_door_open_axis':'Unity local +Y, +70 degrees about front hinge',
              'tail_lift_fold_axis':'Unity local +X, +90 degrees folded; authored extended; carriage translates along vehicle +Y guides',
              'tail_lift_mechanism':'two fixed column barrels, rigid sliding carriage/hinge, translating rods; two pinned telescoping fold cylinders',
              'tail_lift_main_rod_length':1.24,'tail_lift_fold_barrel_length':.5,'tail_lift_fold_rod_length':.5,
              'retort_door_open_axis':'Unity +Y vertical slide, 1.7 metres; identity rotation',
              'can_count':15,'can_unit_order':'x*3+z; body, contents and lid relative to each CanUnit base',
              'can_body_inner_bottom':.008,'can_rim_height':.0965,'can_lid_top':.102,
              'packing_carton_has_duplicate_cans':False,'packing_can_targets':'ANCHOR_PackingCan00..14',
              'finished_carton_dimensions':[.255,.30,.70],'finished_cartons_per_delivery':3,
              'finished_carton_bottom_origin':True,'shipping_pallet_top':.172,
              'finished_carton_flaps':'Closed rest; Unity Z, Left +110 / Right -110 degrees opens',
              'shipping_scale':'Whole scale outside shipping north jamb; Unity X needle axis, +X face',
              'shipping_scale_platform_xz':[.195,1.645,6.325,7.575],'shipping_scale_platform_top':.32,
              'shipping_scale_load':[1.20,.32,6.95],'shipping_crew_corridor_x':2.75,
              'conveyor_driven_roller_axes':list(CONVEYOR_ROLLER_AXES),'conveyor_driven_roller_signs':list(CONVEYOR_ROLLER_SIGNS),
              'retort_lock_axes':'Unity local Z; four pivots follow MOVE_RetortDoor',
              'retort_pressure_needle_axis':'Unity local X',
              'seamer_roller_axes':'Unity local Y; two pivots follow MOVE_SeamerHead',
              'packing_feed_roller_axis':'Unity local +Z; same-can feed to ANCHOR_PackingPickup',
              'crew_service_clearance_radius':.29,
              'crew_service_fixtures':['shallow tool and sealing-ring shelf','stored service stool','hung work jacket','preparation wipe cloth','local service lamp'],
              'preparation_wipe_rest':'MOVE_PreparationCloth matches ANCHOR_PreparationTidyHand; returns before release',
              'workwear_local_front':'Unity +Z; centered separate ApronBib and ApronSkirt groups',
              'semantic_uv_tiles_metres':port.SURFACE_TILES,
              'geometry_signature_includes':['vertices','faces','colors','uv0','transforms']}
    if args.validate_only or args.preview_only:
        if json.loads((args.model_dir/'CityCannery3D.json').read_text(encoding='utf-8'))!=manifest:
            raise RuntimeError('Cannery regenerated contract differs from saved manifest')
        if args.preview_only:
            # Review staging changes transforms and render visibility only.
            # This branch never exports assets, rewrites the manifest, or
            # saves a .blend containing those temporary staged transforms.
            port.source_surface_materials(roots)
            source_surface_materials(roots)
            args.source_dir.mkdir(parents=True,exist_ok=True)
            preview(roots,args.source_dir/'CityCannery3D.png')
            print('CITY CANNERY REVIEW PNG UPDATED: saved geometry contract unchanged')
            return
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        for root in roots:
            if args.only_part is None or root.name in args.only_part:port.base.export(root,args.model_dir/(root.name+'.fbx'))
        (args.model_dir/'CityCannery3D.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
        port.source_surface_materials(roots)
        source_surface_materials(roots)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/'CityCannery3D.blend'),check_existing=False)
        if not args.no_preview:preview(roots,args.source_dir/'CityCannery3D.png')
    port.base.reset();repeated=validate(build(mat))
    if repeated!=entries:raise RuntimeError('Cannery deterministic rebuild mismatch')
    print('CITY CANNERY ART CONTRACT OK: metre parts, shared semantic UVs, pallet fit, real openings and deterministic geometry')
    print(json.dumps([{k:v for k,v in e.items() if k in ('name','triangles','bounds_min','bounds_max')} for e in entries]))


if __name__=='__main__':main()
