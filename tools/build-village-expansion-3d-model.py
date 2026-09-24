#!/usr/bin/env python3
"""Passive, fixed-metre village ski base, severed trade road and conserved repairs."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import tessellate_polygon

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp
from village_truck_wreck import rusted_truck
from village_chair_pile import chair_pile
from village_abandoned_buildings import build_all as abandoned_buildings
from village_abandoned_yards import build_all as abandoned_yards
from village_avalanche import (ORIGIN as AVALANCHE_ORIGIN, FOOTPRINT as AVALANCHE_FOOTPRINT,
    build_avalanche, build_ruin_variant, validate_avalanche)

VERSION = "1.5.0"
DESIGN = "village_forest_ski_base_old_road_v1"
COLORS = {"Timber": (.29,.255,.205,1), "Masonry": (.49,.485,.445,1),
          "LayeredStone": (.32,.345,.34,1), "RustedIron": (.30,.255,.21,1),
          "WindSnow": (.83,.85,.84,1), "Asphalt": (.24,.255,.255,1),
          "Concrete": (.47,.47,.43,1), "Canvas": (.39,.40,.35,1),
          "Glass": (.40,.44,.43,.16), "WreckRust": (1,1,1,1), "WreckPaint": (1,1,1,1)}
COLORS.update(AbandonedWood=(.34,.305,.26,1), AbandonedPlaster=(.55,.53,.47,1),
              AbandonedRoof=(.27,.275,.25,1), DarkWindow=(.075,.085,.08,1))

def box(p,s,c=.01): return bp.u_box(p,s,c)
def merge(parts):
    pieces=list(parts)
    for piece in pieces:
        assert bp.signed_volume(piece)>1e-9,"Inward or degenerate component before merge"
    return kit.merge_all(pieces)
def at(g,p): return kit.translated(g,p)
def rotate(g,e): return bp.u_rotated(g,e)
def u(g): return bp.to_source(g)

def roof(width,depth,wall,rise,thickness):
    """Two thick roof slopes: closed solids, with the ridge along X."""
    half=depth*.5; length=math.hypot(half,rise)
    return merge([at(rotate(box((0,0,0),(width,thickness,length),.015),
                          (side*math.degrees(math.atan2(rise,half)),0,0)),
                     (0,wall+rise*.5,side*half*.5)) for side in (-1,1)])

def gable(depth,rise,thickness):
    # Shared prism's section lies in source XZ and extrudes source Y.
    raw=kit.prism([(-depth*.5,0),(depth*.5,0),(0,rise)],thickness)
    verts=[(y,z,x) for x,y,z in raw[0]]
    return verts,raw[1]

def beam_between(a,b,width=.05):
    direction=Vector(b)-Vector(a);mid=(Vector(a)+Vector(b))*.5
    geometry=u(kit.beam(direction.length,width,width))
    rotation=Vector((0,0,1)).rotation_difference(direction.normalized())
    return [tuple(rotation @ Vector(v)+mid) for v in geometry[0]],geometry[1]

def section(profile,depth):
    """Closed Unity XY section, extruded in Z; all source polygons are CCW."""
    area=sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(profile,profile[1:]+profile[:1]))
    return u(kit.prism(profile if area>0 else list(reversed(profile)),depth))

def snow_patch(center,width,depth):
    mound=u(kit.lathe([(.68,0),(.77,.026),(.58,.072),(.16,.090)],9))
    vertices=[]
    for i,(x,y,z) in enumerate(mound[0]):
        irregular=1+.09*math.sin((i%9)*2.1)
        vertices.append((center[0]+x*width*.65*irregular,center[1]+y,
                         center[2]+z*depth*.65*irregular))
    return vertices,mound[1]

def trade_warehouse(add):
    """10 x 14 m closed shell; the +X facade owns the loading doors and dock."""
    kind="TradeWarehouse"
    add(kind,"StonePlinth",box((0,.40,0),(10,.8,14),.055),"LayeredStone")
    # A real wall opening receives the two shut leaves, instead of surface-applied doors.
    facade=u(kit.wall_run(14,3.2,.28,[kit.Opening(0,3.6,2.76)],.015))
    add(kind,"LoadingWall",at(rotate(facade,(0,90,0)),(4.86,.8,0)),"Timber")
    add(kind,"ClosedWalls",merge([box((-4.86,2.4,0),(.28,3.2,14),.015)]+
        [box((0,2.4,z),(9.44,3.2,.28),.015) for z in (-6.86,6.86)]),"Timber")
    add(kind,"Gables",merge([at(rotate(gable(10,1.30,.28),(0,90,0)),(0,4,z))
        for z in (-6.86,6.86)]),"Timber")
    add(kind,"Roof",rotate(roof(15.0,10.8,3.86,1.45,.20),(0,90,0)),"Timber")
    add(kind,"RoofSnow",rotate(roof(14.93,10.72,4.02,1.45,.13),(0,90,0)),"WindSnow",False)
    structure=[]
    for x in (-5.005,5.005):
        for z in (-6.83,-3.7,3.7,6.83):
            structure.append(box((x,2.43,z),(.16,3.3,.20),.022))
        structure += [box((x,y,0),(.17,.18,14.15),.018) for y in (.94,3.93)]
    for z in (-7.005,7.005):
        structure += [box((x,2.43,z),(.20,3.3,.16),.02) for x in (-4.82,0,4.82)]
        structure += [box((0,y,z),(10.12,.18,.16),.018) for y in (.94,3.93)]
    # Inset plank joints and irregular repairs remain subordinate to the broad closed mass.
    for z in (-6.45,-5.58,-4.71,-3.00,-2.13,2.13,3.00,4.71,5.58,6.45):
        structure.append(box((5.016,2.41,z),(.035,2.68,.045),.004))
    for z in (-7.016,7.016):
        structure += [box((x,2.4,z),(.048,2.74,.035),.004) for x in (-4.35,-3.5,-2.65,-1.8,-.95,.9,1.8,2.7,3.6,4.45)]
    add(kind,"StructuralTimbers",merge(structure),"Timber",True,(.22,.215,.18,1))
    door=[];iron=[]
    for sign in (-1,1):
        for i in range(8):
            door.append(box((4.98,2.15,sign*(.125+i*.218)),(.14,2.66,.209),.009))
        for y in (1.14,3.10):
            door.append(box((5.074,y,sign*.9),(.075,.15,1.72),.008))
        door.append(beam_between((5.12,1.18,sign*.14),(5.12,3.06,sign*1.63),.12))
        for y in (1.35,2.97):
            iron += [box((5.13,y,sign*1.56),(.045,.10,.40),.006),
                at(rotate(bp.u_cylinder((0,0,0),(.09,.12,.09),8),(0,0,0)),(5.14,y,sign*1.83))]
    door += [box((5.07,2.23,z),(.19,2.97,.18),.018) for z in (-1.91,1.91)]
    door += [box((5.07,3.67,0),(.19,.18,4.0),.018)]
    door += [box((4.98,2.15,z),(.14,2.68,.07),.007) for z in (-1.765,0,1.765)]
    iron += [box((5.20,2.03,0),(.06,.10,.66),.008),box((5.22,2.08,.26),(.075,.23,.10),.006)]
    add(kind,"ClosedLoadingDoors",merge(door),"Timber",True,(.245,.235,.19,1))
    add(kind,"DoorHardware",merge(iron),"RustedIron")
    add(kind,"LoadingDock",box((5.73,.4,0),(1.54,.8,5.6),.055),"LayeredStone")
    # Two well-supported old bumper timbers communicate repeated loading at the same height.
    bumpers=[box((6.52,.54,z),(.16,.31,1.52),.03) for z in (-1.68,1.68)]
    add(kind,"LoadingBumpers",merge(bumpers),"Timber",True,(.20,.205,.18,1))
    canopy=[at(rotate(box((0,0,0),(2.7,.13,6.55),.018),(0,0,-5.0)),(5.94,3.62,0))]
    add(kind,"LoadingCanopy",merge(canopy),"RustedIron")
    supports=[box((7.02,1.69,z),(.18,3.38,.18),.018) for z in (-3.03,3.03)]
    for z in (-3.03,3.03):
        supports += [beam_between((7.02,2.58,z),(6.33,3.54,z),.12),
                     beam_between((7.02,2.58,z),(7.02,3.43,z*.70),.12),
                     box((5.91,3.48,z),(2.5,.16,.17),.015)]
    add(kind,"CanopySupports",merge(supports),"Timber")
    add(kind,"CanopyFootings",merge([box((7.02,.12,z),(.34,.24,.34),.03)
        for z in (-3.03,3.03)]),"LayeredStone")
    add(kind,"CanopySnow",at(rotate(box((0,0,0),(2.63,.10,6.46),.025),
        (0,0,-5)),(5.94,3.73,0)),"WindSnow",False)
    # Patched ends, masonry courses and bracket fixings are functional age without labels.
    add(kind,"MendedBoards",merge([box((5.04,y,z),(.06,h,.28),.008) for y,z,h in
        ((1.45,-5.56,.78),(1.31,-5.26,.51),(1.55,4.68,.95))]),"Timber",True,(.36,.315,.24,1))
    courses=[]
    for x in (-5.013,5.013):
        for row in range(2):
            for i in range(13):
                z=-6.35+i*1.01+(.17 if row else 0)
                if x>0 and abs(z)<2.9:continue
                courses.append(box((x,.19+row*.39,z),(.035,.34,.95),.012))
    add(kind,"PlinthCourses",merge(courses),"LayeredStone")

def trade_yard_props(add):
    kind="TradeYardProps"
    # Three empty pallets in a small, uneven return stack; every slat has end grain and gaps.
    wood=[]
    for level in range(3):
        px=.91+(level%2)*.04;pz=.28-level*.025;base=.025+level*.19
        pallet=[]
        for x in (-.43,0,.43):pallet.append(box((x,.07,0),(.15,.12,.91),.01))
        for z in (-.39,0,.39):pallet.append(box((0,.015,z),(1.16,.03,.14),.005))
        for x in (-.48,-.24,0,.24,.48):pallet.append(box((x,.157,0),(.20,.055,.91),.007))
        wood.append(at(rotate(merge(pallet),(0,(-2 if level==1 else 0),0)),(px,base,pz)))
    # A low empty return crate is hollow and visibly made from reused unequal boards.
    crate=[]
    for i in range(5):crate.append(box((-.47+i*.235,.055,0),(.215,.08,.76),.007))
    for x in (-.54,.54):
        for z in (-.36,.36):crate.append(box((x,.29,z),(.065,.54,.065),.006))
    for y in (.18,.38):
        crate += [box((0,y,z),(1.14,.135,.045),.006) for z in (-.395,.395)]
        crate += [box((x,y,0),(.045,.135,.77),.006) for x in (-.575,.575)]
    wood.append(at(rotate(merge(crate),(0,6,0)),(.97,0,-.91)))
    add(kind,"EmptyPalletsAndReturnCrate",merge(wood),"Timber",True,(.33,.29,.225,1))
    # A stationary two-wheel platform trolley, resting on its rear feet, not abandoned mid-action.
    cart=[];frame=[];wheels=[]
    for i in range(5):cart.append(box((-1.00+i*.145,.48,.07),(.132,.055,1.28),.007))
    frame += [box((x,.415,.07),(.07,.10,1.37),.009) for x in (-1.09,-.30)]
    frame += [box((-.695,.405,z),(.86,.10,.07),.009) for z in (-.48,.64)]
    frame += [beam_between((x,.42,-.59),(x,1.20,-1.06),.055) for x in (-1.07,-.32)]
    frame += [beam_between((-1.07,1.20,-1.06),(-.32,1.20,-1.06),.055),
        beam_between((-1.07,.025,-.48),(-1.07,.42,-.48),.045),
        beam_between((-.32,.025,-.48),(-.32,.42,-.48),.045),
        box((-1.07,.015,-.48),(.09,.03,.09),.006),
        box((-.32,.015,-.48),(.09,.03,.09),.006),
        beam_between((-1.17,.25,.45),(-.22,.25,.45),.065)]
    for x in (-1.17,-.22):
        wheel=rotate(bp.u_cylinder((0,0,0),(.49,.065,.49),12),(0,0,90))
        wheels.append(at(wheel,(x,.25,.45)))
        wheels.append(at(rotate(bp.u_cylinder((0,0,0),(.14,.081,.14),8),(0,0,90)),(x,.25,.45)))
    add(kind,"TrolleyPlanks",merge(cart),"Timber")
    add(kind,"TrolleyFrame",merge(frame),"RustedIron")
    add(kind,"TrolleyWheels",merge(wheels),"RustedIron",True,(.19,.20,.185,1))

def conserved_repair(add):
    """Local origin is the near road cut; -Z faces the gap. Nothing bridges it."""
    kind="ConservedRepair"
    # The short buttresses stand outside the terrain cut, beside the projecting asphalt lip.
    # Exposed side caps rise above the asphalt, while the transverse tie remains beneath it.
    # They belong to this bank and do not span the remaining gap to the opposite road.
    support_offset=(0,0,-1.8)
    piers=[]
    profile=[(-.70,-3.2),(.70,-3.2),(.46,.60),(-.46,.60)]
    for x in (-3.65,3.65):piers.append(at(section(profile,1.16),(x,0,.75)))
    piers += [box((0,-1.17,.87),(8.28,.42,.91),.045),
              box((-3.65,.68,.77),(1.15,.20,1.22),.035)]
    add(kind,"UnfinishedButtresses",at(merge(piers),support_offset),"Concrete")
    # Horizontal casting seams and a few protected anchor ends distinguish construction from rubble.
    seams=[]
    for x in (-3.65,3.65):
        for y in (-3.38,-2.67,-1.95,-.48):
            seams.append(box((x,y+.8,.151),(.94,.045,.035),.003))
    add(kind,"FormworkSeams",at(merge(seams),support_offset),"LayeredStone",False)
    anchors=[]
    for x in (-3.95,-3.39,3.39,3.95):
        for z in (.32,1.15):
            anchors.append(bp.u_cylinder((x,.69,z),(.075,.10,.075),8))
            anchors.append(bp.u_cylinder((x,.785,z),(.115,.015,.115),8))
    add(kind,"CappedAnchors",at(merge(anchors),support_offset),"RustedIron",False)
    # One supported side work pocket, with a packed low foot and orderly reusable forms.
    add(kind,"WorkPocketFoot",box((-5.26,.04,4.55),(3.38,.08,4.90),.025),"LayeredStone",False)
    lumber=[]
    for z in (2.72,5.95):lumber.append(box((-5.24,.16,z),(2.61,.24,.20),.018))
    for layer in range(3):
        for i in range(4):
            lumber.append(box((-6.13+i*.56,.34+layer*.205,4.30+((i+layer)%2)*.06),
                (.48,.185,3.65-((i+layer)%3)*.11),.018))
    add(kind,"StoredRepairTimbers",merge(lumber),"Timber",True,(.325,.29,.23,1))
    # A thick folded section produces actual drape/end silhouette, not a flat floating tarp card.
    profile=[(-1.19,.32),(-1.13,.76),(-.97,.94),(-.36,.98),(.28,.93),(.98,.97),(1.15,.76),(1.19,.32),
             (1.22,.32),(1.18,.78),(1.00,1.00),(.28,.96),(-.36,1.01),(-.99,.97),(-1.16,.78),(-1.22,.32)]
    add(kind,"TiedProtectiveCover",at(section(profile,3.50),(-5.27,0,4.34)),"Canvas")
    ropes=[]
    for z in (3.05,5.61):
        path=[(-6.61,.18,z),(-6.48,.61,z),(-6.23,.96,z),(-5.62,1.035,z),
              (-4.97,.985,z),(-4.28,1.02,z),(-4.02,.62,z),(-3.92,.18,z)]
        ropes += [beam_between(a,b,.026) for a,b in zip(path,path[1:])]
        ropes += [box((x,.17,z),(.22,.12,.15),.018) for x in (-6.61,-3.92)]
    add(kind,"CoverTiesAndWeights",merge(ropes),"RustedIron",False)
    snow=[snow_patch((-5.5,1.003,4.13),1.05,2.29),
          snow_patch((-4.59,.998,4.57),.59,1.22)]
    add(kind,"OldSnowOnStoredMaterials",merge(snow),"WindSnow",False)

def roadside_rail(add):
    """Passive four-metre continuation of the same old iron barrier along local Z."""
    rails=[]
    for height in (.62,.94):
        vertices=[]
        for x,y,z in ((-.022,height+.008,-2),(0,height,-1.56),(0,height,1.56),(.025,height-.009,2)):
            vertices += [(x+dx,y+dy,z) for dx,dy in ((-.055,-.065),(.055,-.065),(.055,.065),(-.055,.065))]
        faces=[(3,2,1,0),(12,13,14,15)]
        for ring in range(3):
            for side in range(4):
                following=(side+1)%4
                faces.append((ring*4+side,ring*4+following,(ring+1)*4+following,(ring+1)*4+side))
        rails.append((vertices,faces))
    rails += [box((0,.53,z),(.12,1.06,.13),.006) for z in (-1.56,1.56)]
    # Joint straps and bolts join rail to post; there is no decorative warning paint.
    joints=[]
    for z in (-1.56,1.56):
        for y in (.62,.94):
            joints.append(box((.065,y,z),(.035,.15,.24),.004))
            joints.append(at(rotate(bp.u_cylinder((0,0,0),(.035,.013,.035),6),
                                   (0,0,90)),(.091,y,z)))
    add("RoadsideRail","OldGuardrail",merge(rails),"RustedIron",False)
    add("RoadsideRail","JointStraps",merge(joints),"RustedIron",False)

def brook_footbridge(add):
    """4.8 m ordinary timber crossing; local +Z is the unobstructed walking axis."""
    kind="BrookFootbridge"
    def height(z):return .18*min(1,(2.4-abs(z))/.6)
    # One watertight deck includes both shallow approaches. Small recessed
    # plank seams remain actual supporting wood, never open collision gaps.
    samples=[(-2.4,0)]
    for i in range(1,24):
        z=-2.4+i*.2
        samples.extend(((z-.004,0),(z,.004),(z+.004,0)))
    samples.append((2.4,0))
    vertices=[]
    for z,recess in samples:
        top=height(z)
        vertices.extend(((-.95,top-recess,z),(.95,top-recess,z),
                         (.95,top-.09,z),(-.95,top-.09,z)))
    faces=[(0,1,2,3)]
    for i in range(len(samples)-1):
        for side in range(4):
            following=(side+1)%4
            faces.append((i*4+side,(i+1)*4+side,(i+1)*4+following,i*4+following))
    end=(len(samples)-1)*4
    faces.append((end+3,end+2,end+1,end))
    add(kind,"DeckAndApproaches",(vertices,faces),"Timber",True,(.355,.325,.27,1))
    beams=[box((x,.005,0),(.18,.19,3.45),.009) for x in (-.67,.67)]
    beams += [box((0,-.1175,z),(1.78,.085,.28),.009) for z in (-1.5,1.5)]
    add(kind,"BearersAndBankSleepers",merge(beams),"Timber",True,(.245,.235,.20,1))
    rails=[]
    for sign in (-1,1):
        x=sign*.875
        rails += [box((x,.55,z),(.13,.94,.13),.009) for z in (-1.6,0,1.6)]
        rails += [box((x,.975,0),(.15,.09,3.64),.008),
                  box((x,.565,0),(.09,.09,3.36),.006)]
        rails += [beam_between((x,.23,sign*1.51),(x,.91,sign*.10),.065)]
    add(kind,"LowSideRails",merge(rails),"Timber",True,(.295,.28,.235,1))
    fasteners=[]
    for i in range(18):
        z=-1.7+i*.2
        for x in (-.67,.67):
            fasteners.append(bp.u_cylinder((x,.1807,z),(.021,.0007,.021),6))
    for x in (-.951,.951):
        for z in (-1.6,0,1.6):
            for y in (.565,.975):
                fasteners.append(at(rotate(bp.u_cylinder((0,0,0),(.026,.002,.026),6),
                                              (0,0,90)),(x,y,z)))
    add(kind,"OldFasteners",merge(fasteners),"RustedIron",False)

def create_parts():
    parts=[]
    def add(kind,name,g,surface,solid=True,tint=None,terrain_fit=None,support=None):
        if kind in ("Avalanche", "AvalancheRuinedHouse"):
            # Pin all new topology BEFORE measurement/export. In particular,
            # terrain-following snow caps are nonplanar and cannot be left to
            # independent Blender and Unity n-gon triangulators.
            vertices, faces = g
            triangles = []
            for face in faces:
                if len(face) == 3:
                    triangles.append(face)
                    continue
                points = [Vector(vertices[index]) for index in face]
                cooked = tessellate_polygon([points])
                assert len(cooked) == len(face)-2, (kind,name,"Degenerate authored polygon")
                triangles.extend(tuple(face[index] for index in tri) for tri in cooked)
            for a,b,c in triangles:
                assert (Vector(vertices[b])-Vector(vertices[a])).cross(
                    Vector(vertices[c])-Vector(vertices[a])).length_squared>1e-16, (kind,name,"Degenerate triangle")
            g = vertices,triangles
        # Validate EVERY component before merging; a positive total can hide an inverted piece.
        vol=bp.signed_volume(g)
        assert vol>1e-9,(kind,name,"inward or degenerate solid",vol)
        parts.append(dict(kind=kind,name=name,mesh="GEO_Expansion_"+kind+"_"+name,
                          surface=surface,solid=solid,tint=tint or COLORS[surface],geometry=g))
        if terrain_fit:
            parts[-1]["terrain_fit"] = terrain_fit
        if support is not None:
            parts[-1]["support"] = support
    lodge="SkiLodge"
    add(lodge,"Floor",box((0,-.07,0),(17.36,.18,11.36),.018),"Timber")
    add(lodge,"Foundation",box((0,-.34,0),(18,.36,12),.045),"LayeredStone")
    front=kit.wall_run(18,3.6,.32,[kit.Opening(-5.4,2.3,2.85,1.15),
        kit.Opening(0,2.6,2.75),kit.Opening(5.4,2.3,2.85,1.15)],.012)
    rear=kit.wall_run(18,3.6,.32,[kit.Opening(-5.4,2.3,2.85,1.15),
        kit.Opening(0,2.3,2.85,1.15),kit.Opening(5.4,2.3,2.85,1.15)],.012)
    sides=kit.wall_run(11.36,3.6,.32,[kit.Opening(-2.7,1.7,2.85,1.15),
        kit.Opening(2.7,1.7,2.85,1.15)],.012)
    add(lodge,"FrontWall",at(u(front),(0,0,-5.84)),"Masonry")
    add(lodge,"BackWall",at(u(rear),(0,0,5.84)),"Masonry")
    for sign in (-1,1):
        add(lodge,"SideWall"+str(sign),at(rotate(u(sides),(0,90,0)),(sign*8.84,0,0)),"Masonry")
        add(lodge,"Gable"+str(sign),at(gable(12,1.4,.32),(sign*8.84,3.6,0)),"Timber")
    add(lodge,"Roof",roof(19.3,13.3,3.48,1.55,.24),"Timber")
    add(lodge,"RoofSnow",roof(19.22,13.22,3.66,1.55,.17),"WindSnow",False)
    # Continuous structural timbers and porch lintel give the long low mass its working character.
    beams=[box((0,3.41,z),(18.3,.24,.22),.025) for z in (-5.9,5.9)]
    for x in (-8.78,-2.95,2.95,8.78):
        beams.extend([box((x,1.78,-6.018),(.19,3.56,.11)),box((x,1.78,6.018),(.19,3.56,.11)),
                      box((x,3.31,0),(.20,.22,11.6))])
    add(lodge,"WallAndCeilingTimbers",merge(beams),"Timber")
    frames=[];glass=[]
    def window(center,width,side=False):
        h=1.7
        frame=merge([box((x,0,0),(.095,h+.19,.13),.006) for x in (-width*.5-.045,width*.5+.045)]+
                    [box((0,y,0),(width,.095,.13),.006) for y in (-h*.5-.045,h*.5+.045)]+
                    [box((0,0,0),(.065,h,.08),.004),box((0,-h*.5-.12,0),(width+.32,.12,.48),.01)])
        pane=box((0,0,0),(width,h,.009),.001)
        if side:frame=rotate(frame,(0,90,0));pane=rotate(pane,(0,90,0))
        frames.append(at(frame,center));glass.append(at(pane,center))
    for z in (-5.84,5.84):
        for x in (-5.4,5.4) if z<0 else (-5.4,0,5.4):window((x,2.0,z),2.3)
    for x in (-8.84,8.84):
        for z in (-2.7,2.7):window((x,2.0,z),1.7,True)
    add(lodge,"WindowFrames",merge(frames),"Timber")
    add(lodge,"WindowGlass",merge(glass),"Glass",False)
    # Doors lie open against the outside wall; no runtime hinge/action is implied.
    doors=[]
    for side in (-1,1):
        for plank in range(6):doors.append(box((side*(1.40+plank*.20),1.32,-6.09),(.188,2.64,.075),.008))
        for y in (.4,2.2):doors.append(box((side*1.9,y,-6.145),(1.18,.075,.035),.004))
    add(lodge,"OpenDoorLeaves",merge(doors),"Timber")
    for x in (-1.72,1.72):add(lodge,"Vestibule"+str(x),box((x,1.4,-4.8),(.16,2.8,2.1)),"Timber")
    # Furniture footprints are mirrored in AlpineVillageExpansionPlan's blockers.
    benches=[]
    for x,z,length in ((-6.4,-.8,6.4),(6.4,-2.2,3.5)):
        benches += [box((x,.46,z),(.65,.095,length)),box((x+(-.25 if x<0 else .25),.82,z),(.075,.6,length))]
        for end in (-1,1):
            benches += [box((x,.23,z+end*(length*.5-.3)),(.58,.46,.12)),
                        box((x,.24,z),(.12,.12,length-.35))]
    add(lodge,"Benches",merge(benches),"Timber")
    racks=[box((0,.20,4.75),(11.8,.14,.8)),box((0,1.65,4.75),(11.8,.14,.55))]
    for i in range(19):racks.append(box((-5.65+i*.625,.96,5.03),(.085,1.88,.085)))
    add(lodge,"EmptyRentalRacks",merge(racks),"Timber")
    # A handful of old skis makes the use legible without labels or trophies.
    skis=[]
    for i in range(5):
        x=-5.1+i*.40
        skis += [box((x,1.18,4.64),(.12,1.94,.045),.016),box((x,.84,4.59),(.16,.16,.07),.008)]
    add(lodge,"RemainingSkis",merge(skis),"RustedIron")
    counter=merge([box((4.7,1.0,1),(3.8,.12,.8),.018),box((4.7,.5,1.31),(3.6,.94,.11)),
                   box((2.89,.48,1),(.11,.96,.65)),box((6.51,.48,1),(.11,.96,.65))])
    add(lodge,"RentalCounter",counter,"Timber")
    # Missing boards, patched skirting and exposed fastenings are passive age, not a recent event.
    patches=[box((x,.35,-6.012),(.52,.24,.04),.004) for x in (-7.4,-3.4,3.0,7.5)]
    add(lodge,"OldPatches",merge(patches),"Timber",True,(.355,.30,.23,1))
    shed="ServiceShed"
    add(shed,"Foundation",box((0,-.15,0),(8,.3,6),.03),"LayeredStone")
    add(shed,"Walls",merge([box((0,1.55,z),(8,3.1,.24)) for z in (-2.88,2.88)]+
        [box((x,1.55,0),(.24,3.1,5.52)) for x in (-3.88,3.88)]),"Timber")
    add(shed,"Roof",roof(8.8,6.8,3.0,.85,.18),"RustedIron")
    add(shed,"Snow",roof(8.76,6.76,3.16,.85,.15),"WindSnow",False)
    add(shed,"Gables",merge([at(gable(6,.76,.24),(x,3.1,0)) for x in (-3.88,3.88)]),"Timber")
    add(shed,"Door",box((0,1.2,-3.03),(2.6,2.4,.11)),"Timber",True,(.235,.24,.215,1))
    for kind in ("LiftBase","LiftTop"):
        add(kind,"Footing",box((0,.2,0),(.7,.4,.7),.04),"LayeredStone")
        iron=[box((0,2.65,0),(.26,4.9,.30)),box((0,4.82,0),(3.8,.24,.3))]
        # A pair of braces, plus the old rope wheel under the cross-arm.
        iron += [beam_between((0,3.4,0),(s*1.65,4.7,0),.09) for s in (-1,1)]
        wheel=bp.u_cylinder((0,4.9,0),(2.6,.085,2.6),16)
        add(kind,"Iron",merge(iron),"RustedIron")
        add(kind,"Wheel",wheel,"RustedIron")
        if kind=="LiftBase":
            add(kind,"DriveHousing",box((0,.8,0),(.65,.85,.65),.07),"RustedIron")
    barrier="RoadBarrier"
    rocks=[]
    for i in range(12):
        x=-5.48+i*.995;h=.50+(i%3)*.08
        rocks.append(box((x,h*.5,0),(1.12,h,.45),.10))
    add(barrier,"StoneBlockage",merge(rocks),"LayeredStone")
    rails=[box((0,.93,.15),(12,.13,.11)),box((0,.62,.15),(12,.12,.10))]
    rails += [box((x,.53,.15),(.12,1.06,.13)) for x in (-5.5,-2.75,0,2.75,5.5)]
    add(barrier,"OldGuardrail",merge(rails),"RustedIron")
    add("RoadSurface","Asphalt",box((0,-.04,0),(1,.08,1),0),"Asphalt",False)
    # Scaled only along the road length; lateral jagged lip remains its authored 5.4m width.
    rubble=[box((x,-.4,z),(w,.8,d),.10) for x,z,w,d in
            ((-2.15,-.08,1.1,1.5),(-.95,-.35,1.3,1.8),(.4,-.05,1.5,1.4),(1.8,.20,1.3,1.8))]
    add("RoadBrokenLip","Foundation",merge(rubble),"LayeredStone",False)
    add("RoadBrokenLip","Asphalt",merge([box((x,.005,z),(w,.1,d),.015) for x,z,w,d in
            ((-2.15,-.08,1.1,1.5),(-.95,-.35,1.3,1.8),(.4,-.05,1.5,1.4),(1.8,.20,1.3,1.8))]),"Asphalt",False)
    trade_warehouse(add)
    trade_yard_props(add)
    rusted_truck(add)
    chair_pile(add)
    conserved_repair(add)
    roadside_rail(add)
    brook_footbridge(add)
    abandoned_buildings(add)
    abandoned_yards(add)
    build_avalanche(add)
    build_ruin_variant(add)
    return parts

def validate(parts):
    assert len({p["mesh"] for p in parts}) == len(parts), "Duplicate exported part names"
    validate_avalanche(parts)
    # Albedo is a fixed authored input, with the exact image prompts and bytes retained.
    textures=json.loads((ROOT/"ArtSource/Village/Textures/generation.json").read_text(encoding="utf-8"))
    for texture in textures["images"]:
        raw=(ROOT/texture["asset"]).read_bytes()
        assert hashlib.sha256(raw).hexdigest()==texture["sha256"],"Wreck texture changed without provenance"
    # The entry and main circulation must be open in actual authored solids.
    trees=[BVHTree.FromPolygons(*p["geometry"],all_triangles=False) for p in parts
           if p["kind"]=="SkiLodge" and p["solid"]]
    for x in (-1.1,0,1.1):
        for y in (.2,1.1,2.3):
            start=Vector((x,y,-7));end=Vector((x,y,3.7));direction=end-start
            assert not any(t.ray_cast(start,direction.normalized(),direction.length)[0] is not None for t in trees),"Blocked lodge doorway/circulation"
    def bounds_for(kind):
        return kit.bounds(merge(p["geometry"] for p in parts if p["kind"]==kind))
    warehouse_lo,warehouse_hi=bounds_for("TradeWarehouse")
    assert warehouse_lo[1]>=0 and 5.40<=warehouse_hi[1]<=5.60,"Warehouse base/roof height"
    assert warehouse_hi[0]<=7.31 and warehouse_hi[0]>7.2,"Warehouse +X loading facade"
    assert warehouse_lo[0]>=-5.50 and warehouse_lo[2]>=-7.6 and warehouse_hi[2]<=7.6,"Warehouse footprint"
    yard_lo,yard_hi=bounds_for("TradeYardProps")
    assert yard_lo[0]>=-2 and yard_hi[0]<=2 and yard_lo[2]>=-1.5 and yard_hi[2]<=1.5,"Trade yard props escaped 4 x 3 m"
    assert yard_lo[1]>=0,"Trade yard props below ground"
    rail_lo,rail_hi=bounds_for("RoadsideRail")
    assert abs(rail_lo[2]+2)<1e-8 and abs(rail_hi[2]-2)<1e-8,"Roadside rail must remain four metres along Z"
    assert rail_lo[1]>=0 and rail_hi[1]<=1.061,"Roadside rail height"
    assert all(not p["solid"] for p in parts if p["kind"]=="RoadsideRail"),"Distant roadside rail must remain passive"
    bridge=[p for p in parts if p["kind"]=="BrookFootbridge"]
    bridge_lo,bridge_hi=bounds_for("BrookFootbridge")
    assert all(abs(a-b)<1e-6 for a,b in zip(bridge_lo,(-.953,-.16,-2.4))),("Footbridge minimum metre bounds",bridge_lo)
    assert all(abs(a-b)<1e-6 for a,b in zip(bridge_hi,(.953,1.02,2.4))),("Footbridge maximum metre bounds",bridge_hi)
    bridge_trees=[BVHTree.FromPolygons(*p["geometry"],all_triangles=False) for p in bridge if p["solid"]]
    for x in (-.74,0,.74):
        for y in (.23,.6,1.2):
            assert not any(t.ray_cast(Vector((x,y,-2.6)),Vector((0,0,1)),5.2)[0] is not None
                           for t in bridge_trees),"Blocked footbridge passage"
    deck=next(p for p in bridge if p["name"]=="DeckAndApproaches")
    deck_tree=BVHTree.FromPolygons(*deck["geometry"],all_triangles=False)
    for x in (-.78,0,.78):
        for i in range(97):
            z=max(-2.3999,min(2.3999,round(-2.4+i*.05,6)))
            hit=deck_tree.ray_cast(Vector((x,.5,z)),Vector((0,-1,0)),1)[0]
            expected=.18*min(1,(2.4-abs(z))/.6)
            assert hit is not None and expected-.0041<=hit.y<=expected+.0001,("Discontinuous footbridge deck/ramp",x,z,expected,tuple(hit) if hit else None)
    for p in parts:
        if p["kind"]=="ConservedRepair":
            support=p["name"] in ("UnfinishedButtresses","FormworkSeams","CappedAnchors")
            for x,y,z in p["geometry"][0]:
                if support:
                    assert -1.8<=z<=0 and -3.2-1e-8<=y<=.8+1e-8,"Repair support escaped the near side abutments"
                else:
                    assert z>=0,"Stored repair materials extend across the severed road"
                assert not (-3<=x<=3 and y>1e-8),"Repair obscures the road's central view"
                if y>0 and not support:
                    assert -7<=x<=-3.5 and 2<=z<=7,"Repair materials escaped the side pocket"
    # The closed loading facade must physically intercept a view through each leaf and its join.
    warehouse_trees=[BVHTree.FromPolygons(*p["geometry"],all_triangles=False) for p in parts
        if p["kind"]=="TradeWarehouse" and p["solid"]]
    for z in (-1.4,0,1.4):
        assert any(t.ray_cast(Vector((8,2.2,z)),Vector((-1,0,0)),4)[0] is not None
                   for t in warehouse_trees),"Open warehouse loading door"
    # The expanded library is shared by all placed households; these are source
    # triangles, not a fresh mesh/material allocation per world placement.
    assert sum(kit.triangle_count(p["geometry"]) for p in parts)<=165000,"Expansion triangle budget"
    for kind in ("TownHall", "School", "ShopBakery", "Workshop", "MountainRescue",
                 "AbandonedHouseA", "AbandonedHouseB", "WornHouseA", "WornHouseB"):
        lo,hi=bounds_for(kind)
        assert 4.5<=hi[1]<=8.0 and lo[1]>=-1e-6, "Abandoned building metre bounds " + kind
        solids=[BVHTree.FromPolygons(*p["geometry"],all_triangles=False) for p in parts
                if p["kind"]==kind and p["solid"]]
        assert any(t.ray_cast(Vector((0,1.8,hi[2]+2)),Vector((0,0,-1)),hi[2]-lo[2]+4)[0] is not None
                   for t in solids), "Open closed facade " + kind
    aged=json.loads((ROOT/"Assets/Resources/Village/Textures/VillageAbandonmentTextures.json").read_text(encoding="utf-8"))
    for sheet in aged["sheets"]:
        raw=(ROOT/"Assets/Resources/Village/Textures"/(sheet["name"]+".png")).read_bytes()
        assert hashlib.sha256(raw).hexdigest()==sheet["sha256"],"Stale abandoned material"
    first=json.dumps(parts,sort_keys=True,separators=(",",":"))
    assert first==json.dumps(create_parts(),sort_keys=True,separators=(",",":")),"Non-deterministic geometry"
    return hashlib.sha256(first.encode()).hexdigest()

def build(parts):
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    root=bpy.data.objects.new("VillageExpansion3D",None);bpy.context.scene.collection.objects.link(root)
    objects=[];rows=[]
    for p in parts:
        g=bp.to_source(p["geometry"]);assert bp.signed_volume(g)>1e-9
        mesh=bpy.data.meshes.new(p["mesh"]);mesh.from_pydata(g[0],[],g[1]);mesh.update()
        uv=mesh.uv_layers.new(name="UVMap")
        for face in mesh.polygons:
            axes=sorted(range(3),key=lambda a:abs(face.normal[a]))[:2]
            for i in face.loop_indices:
                v=mesh.vertices[mesh.loops[i].vertex_index].co;uv.data[i].uv=(v[axes[0]],v[axes[1]])
        obj=bpy.data.objects.new(p["mesh"],mesh);bpy.context.scene.collection.objects.link(obj);obj.parent=root
        mat=bpy.data.materials.new(p["mesh"]+"_Review")
        mat.diffuse_color={"WreckRust":(.27,.14,.085,1),"WreckPaint":(.38,.31,.22,1)}.get(p["surface"],p["tint"])
        mesh.materials.append(mat)
        objects.append(obj);lo,hi=kit.bounds(p["geometry"])
        row={k:v for k,v in p.items() if k!="geometry"};row.update(bounds_min=lo,bounds_max=hi,triangles=kit.triangle_count(g))
        rows.append(row)
    return objects,rows

def preview(path,objects,rows,kind="SkiLodge",location=(25,-26,15),target=(0,0,2),lens=43):
    for obj,row in zip(objects,rows):obj.hide_render=row["kind"]!=kind
    scene=bpy.context.scene
    camera=bpy.data.objects.new("ReviewCamera",bpy.data.cameras.new("ReviewCamera"));scene.collection.objects.link(camera)
    camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.lens=lens;scene.camera=camera;scene.render.engine="BLENDER_WORKBENCH"
    scene.display.shading.light="STUDIO";scene.display.shading.color_type="MATERIAL"
    scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.world.color=(.19,.21,.23)
    scene.render.resolution_x=1400;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG";scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/Village/Expansion")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/Village")
    parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true")
    parser.add_argument("--preview-kind",action="append",help="Render only these passive kinds; repeat to select several")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    parts=create_parts();signature=validate(parts);objects,rows=build(parts)
    data=dict(generator_version=VERSION,design_id=DESIGN,scale_mode="fixed_metres",uv_mode="projected_metres",
              build_signature=signature,mesh_count=len(rows),triangle_count=sum(p["triangles"] for p in rows),
              avalanche_origin=AVALANCHE_ORIGIN,
              avalanche_footprint=[value for point in AVALANCHE_FOOTPRINT for value in point],
              colliders=False,lights=False,cameras=False,animation_count=0,parts=rows)
    target=args.model_dir/"VillageExpansion3D.json"
    if args.validate_only:assert json.loads(target.read_text())==json.loads(json.dumps(data)),"Stale expansion manifest"
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(args.model_dir/"VillageExpansion3D.fbx"),use_selection=True,
            object_types={"EMPTY","MESH"},axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
            bake_space_transform=True,add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE")
        target.write_text(json.dumps(data,indent=2)+"\n",encoding="utf-8")
        bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"VillageExpansion3D.blend"))
        if not args.no_preview:
            reviews=[("SkiLodge","VillageExpansion3D.png",(25,-26,15),(0,0,2),43),
                ("TradeWarehouse","VillageTradeWarehouse3D.png",(24,-24,15),(0,0,2),43),
                ("TradeYardProps","VillageTradeYardProps3D.png",(5,-6,4.6),(0,0,.45),43),
                ("ConservedRepair","VillageConservedRepair3D.png",(12,-17,12),(-2,3.5,-1.6),43),
                ("RoadsideRail","VillageRoadsideRail3D.png",(5,-6,3.4),(0,0,.55),48),
                ("BrookFootbridge","VillageBrookFootbridge3D.png",(5,-7,3.7),(0,0,.3),48),
                ("Avalanche","VillageAvalanche3D.png",(30,-34,22),(0,8,2),38),
                ("AvalancheRuinedHouse","VillageAvalancheRuinedHouseFront3D.png",(10,12,5),(0,0,1.7),43),
                ("AvalancheRuinedHouse","VillageAvalancheRuinedHouseBack3D.png",(-10,-12,5),(0,0,1.7),43),
                ("RustedTruck","VillageTruckWreck3D.png",(8,10,6),(0,0,1),48),
                ("DiscardedChairPile","VillageChairPile3D.png",(8,-9,6),(0,0,1.3),48),
                ("DiscardedChairPile","VillageChairPileRear3D.png",(-8,9,6),(0,0,1.3),48)]
            assert not args.preview_kind or set(args.preview_kind)<=set(r[0] for r in reviews),"Unknown preview kind"
            for kind,name,location,target,lens in reviews:
                if not args.preview_kind or kind in args.preview_kind:
                    preview(args.source_dir/name,objects,rows,kind,location,target,lens)
    print("VILLAGE EXPANSION VALIDATION OK: outward solids, determinism, avalanche footprint/terrain fit, lodge aisle, closed loading facade, repair view and budgets; "+signature)

if __name__=="__main__":main()
