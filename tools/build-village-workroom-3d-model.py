#!/usr/bin/env python3
"""The occupied masonry workroom in house 08; all other house packs stay untouched."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp
from bar_furniture import turned, shell
spec = importlib.util.spec_from_file_location("workroom_door_source", ROOT / "tools/build-village-resident-doors-3d-model.py")
doors = importlib.util.module_from_spec(spec); sys.modules[spec.name] = doors; spec.loader.exec_module(doors)
VERSION = "1.0.0"
DESIGN = "village_workroom_08_v1"
ANCHORS = {
    "RoomEntry":(-.004,0,1.60), "RepairDock":(-2.02,0,-.97), "RepairJoin":(-1.86,.90,-1.45),
    "RepairLeftSupport":(-1.75,.90,-1.45), "HammerRest":(-2.20,.90,-1.37),
    "RepairRailRest":(-1.86,.90,-1.45), "RepairRailFixed":(-1.875,.90,-1.45),
    "SewingDock":(-2.10,0,.98), "SewingSeat":(-2.10,.42,.78), "ClothRest":(-2.165,.674,1.385),
    "MittenRest":(-2.085,.690,1.385), "SewingBox":(-1.67,.666,1.49), "BoxStow":(-1.67,.752,1.58), "BoxRest":(-1.55,.46,1.10),
    "PlayerHelpDock":(-.62,0,-1.70), "ChairPartLeftGrip":(-1.04,1,-1.92),
    "ChairPartRightGrip":(-1.04,1,-1.48), "BenchSeat":(-2.99,.44,-.10),
    "BenchApproach":(-2.24,0,-.10), "BasketStand":(.77,0,.45), "BasketRest":(.77,.38,.45),
    "BasketDock":(.12,0,.45), "FrontWindow":(-2.10,1.50,2.674), "LeftWindow":(-3.70,1.50,-1.70),
    "Floor":(0,0,0), "RoomLamp":(-1.05,2.10,.13), "AmbientLight":(-1.05,2.10,.13),
    "RepairLamp":(-2.70,1.78,-2.10), "SewingLamp":(-2.65,1.20,1.85),
    "Interior":(-.25,0,-1.65), "Turn0":(2.26,0,-1.65), "Turn1":(3.22,0,-1.65),
    "Hidden0":(2.26,0,.85), "Hidden1":(3.22,0,.85)
}
COLORS = {"Timber":(.30,.245,.175,1), "Masonry":(.48,.455,.385,1), "LayeredStone":(.29,.30,.27,1),
          "RustedIron":(.24,.235,.215,1), "Cloth":(.37,.305,.225,1), "Glass":(.55,.52,.44,.12),
          "Lamp":(.58,.40,.21,1)}

FINISHES = {"Workbench": (.345,.276,.195,1), "RepairRail": (.385,.318,.225,1),
            "ChairFrame": (.332,.267,.190,1), "Hammer": (.355,.259,.166,1),
            "Box": (.390,.312,.225,1), "BoxLid": (.390,.312,.225,1),
            "SewingTable": (.320,.270,.205,1), "Cabinet": (.272,.237,.190,1),
            "Cloth": (.385,.358,.293,1), "ClothFlap": (.385,.358,.293,1),
            "Mitten": (.292,.307,.262,1), "Thread": (.399,.375,.313,1)}


def u(g): return bp.to_source(g)
def at(g, p): return kit.translated(g,p)
def merge(items): return kit.merge_all(items)
def box(p,s,c=.008): return bp.u_box(p,s,c)


def boards(cx, top, cz, width, depth, thickness, count):
    """Separate planks cut to a single support plane, with narrow eased seams."""
    pitch = depth / count
    return [box((cx,top-thickness/2,cz-depth/2+(i+.5)*pitch),
                (width,thickness,pitch-.0015), min(.0035,thickness/5)) for i in range(count)]


def table(cx,cz,width,depth,height,top=.065, heavy=False):
    parts = boards(cx,height,cz,width,depth,top, 4 if heavy else 3)
    shoulder = height-top
    leg = .112 if heavy else .066
    end = width/2-.096
    side = depth/2-.072
    for sign in (-1,1):
        parts.append(box((cx,shoulder-.064,cz+sign*side),(2*end,.128,leg*.64)))
        parts.append(box((cx+sign*end,shoulder-.064,cz),(leg*.64,.128,2*side)))
        parts.append(box((cx+sign*end,.17,cz),(leg*.70,.085,2*side)))
        for direction in (-1,1):
            x,z=cx+sign*end,cz+direction*side
            parts.append(box((x,shoulder/2,z),(leg,shoulder,leg),.008))
    parts.append(box((cx,.17,cz),(2*end+.034,.085,leg*.8)))
    return merge(parts)


def spindle(position, length, radius, rotation=(0,0,0)):
    profile=[(.72*radius,0),(radius,.06*length),(radius,.16*length),
             (.85*radius,.19*length),(.78*radius,.72*length),
             (.93*radius,.76*length),(.93*radius,.86*length),(.68*radius,length)]
    return at(bp.u_rotated(turned((0,0,0),profile,12),rotation),position)


def severin_bench(add):
    add("Workbench",table(-2.08,-1.82,2,.82,.86,.09,heavy=True))
    # The side-laid seat and both bottom legs rest on the bench at .86 m.
    chair=[box((-1.50,1.125,-1.865+i*.164),(.055,.53,.162),.006) for i in range(3)]
    for y in (.889,1.355):
        for z in (-1.919,-1.481):
            chair.append(spindle((-1.480,y,z),.465,.033,(0,0,-90)))
        chair.append(box((-1.746,y,-1.919),(.535,.053,.058),.008))
    for x,w in ((-1.652,.055),(-1.825,.071),(-1.978,.082)):
        chair.append(box((x,1.122,-1.919),(w,.466,.036),.008))
    chair.append(box((-1.897,.878,-1.478),(.079,.036,.056),.003))
    add("ChairFrame",merge(chair))
    rail=[box((.398,-.020,-.0275),(.796,.040,.055),.003),
          box((.799,.021,-.0275),(.045,.112,.049),.004),
          box((.82,.072,-.25),(.054,.056,.52),.007),
          turned((.015,-.005,-.005),[(.012,0),(.012,.005),(.010,.006)],12)]
    add("RepairRail",merge(rail),position=ANCHORS["RepairRailRest"],anchors={
        "Join":(0,0,0),"LeftSupport":(.11,0,0),"ChairPartLeftGrip":(.82,.10,-.47),
        "ChairPartRightGrip":(.82,.10,-.03)})
    # The fixed pad sits on the rail and the screw presses the underside of the
    # worktop. Only the spine of the C passes outside its front edge (z=-1.41).
    cx, jaw, spine = -1.79, -1.462, -1.375
    clamp=[box((cx,.816,spine),(.042,.242,.034),.007),
           box((cx,.923,(spine+jaw)/2),(.044,.028,.116),.006),
           box((cx,.711,(spine+jaw)/2),(.044,.031,.116),.006),
           turned((cx,.900,jaw),[(.021,0),(.023,.003),(.020,.009)],14),
           turned((cx,.663,jaw),[(.011,0),(.013,.008),(.009,.014),(.009,.098),
                                (.020,.103),(.020,.107)],14),
           spindle((cx-.0415,.670,jaw),.083,.006,(0,0,-90))]
    for i in range(6):
        clamp.append(turned((cx,.732+i*.005,jaw),[(.009,0),(.011,.001),(.009,.003)],10))
    add("RepairClamp",merge(clamp),"RustedIron")
    grip=turned((0,-.070,0),[(.015,0),(.021,.007),(.020,.026),(.016,.117),
                              (.014,.178),(.019,.277),(.014,.335)],14)
    add("Hammer",kit.scaled(grip,(1,1,.80)),position=ANCHORS["HammerRest"],euler=(90,180,0),
        anchors={"Grip":(0,0,0),"StrikeFace":(-.095,.265,0)})
    head=turned((0,0,0),[(.023,0),(.026,.003),(.024,.027),(.018,.042),(.021,.070),
                          (.026,.086),(.026,.108),(.019,.133),(.011,.170),(.005,.188)],8)
    add("HammerHead",at(bp.u_rotated(head,(0,0,-90)),(-.095,.265,0)),"RustedIron",parent="Hammer")


def fabric_panel(left, right, y):
    # A stitched doubled edge and slight waves share the same fold hinge.
    depth=.26
    outline=[(left+.010,-depth/2),(right-.012,-depth/2+.001),(right,-.122),
             (right,.118),(right-.011,.13),(left+.009,.129),(left,.120),(left,-.121)]
    body=shell([[(x,y,z) for x,z in outline],[(x,y+.004+.001*math.cos(z*18+x*23),z) for x,z in outline]])
    hems=[box(((left+right)/2,y+.005,z),(right-left-.018,.003,.006),.001)
          for z in (-.124,.124)]
    return merge([body]+hems)


def marta_fittings(add):
    add("SewingTable",table(-2.10,1.65,1.40,.70,.66))
    add("BoxShelf",table(-1.55,1.10,.46,.40,.46,.045))
    stool=[table(-2.10,.78,.46,.44,.42,.055)]
    for x in (-2.292,-1.908):
        stool.append(box((x,.644,.593),(.042,.465,.047),.007))
    for y,h in ((.57,.070),(.766,.142)):
        stool.append(box((-2.10,y,.594),(.36,h,.032),.007))
    add("SewingSeat",merge(stool))
    add("Cloth",fabric_panel(0,.18,0),"Cloth",position=ANCHORS["ClothRest"],
        anchors={"Grip":(.09,.02,0),"FoldHinge":(0,.025,0),"PacketLeftGrip":(.15,.0475,-.08),
                 "PacketRightGrip":(.15,.0475,.08),"HoldEdge":(.135,.014,.09)})
    add("ClothFlap",fabric_panel(-.18,0,-.025),"Cloth",position=(0,.025,0),parent="Cloth",
        anchors={"EdgeGrip":(-.15,-.0225,-.08)})
    # Thumb and palm share one outline so their seam remains closed in silhouette.
    outline=[(-.064,-.056),(.072,-.056),(.084,-.044),(.082,.057),(.065,.067),
             (.053,.062),(.035,.073),(-.019,.073),(-.033,.055),(-.053,.067),
             (-.074,.058),(-.079,.045),(-.060,.014)]
    mitten=shell([[(x*.91,.001,z*.94) for x,z in outline],[(x,.011,z) for x,z in outline],
                  [(x*.95,.024,z*.97) for x,z in outline]])
    add("Mitten",kit.merge(mitten,box((.002,.014,-.047),(.140,.030,.033),.006)),"Cloth",
        position=ANCHORS["MittenRest"],anchors={"Grip":(-.065,.015,0),"Stitch":(.025,.015,.015)})
    sewingbox=[box((0,.018,0),(.256,.017,.304),.003),
               box((0,.081,0),(.244,.010,.288),.002)]
    # A side pull and an overhanging lid grip keep fingers clear of the wood.
    for z in (-.110,-.040):
        sewingbox.append(box((.176,.104,z),(.094,.016,.017),.003))
    sewingbox.append(box((.222,.104,-.075),(.017,.020,.087),.004))
    for sign in (-1,1):
        sewingbox.extend([box((sign*.132,.075,0),(.018,.150,.320),.003),
                          box((0,.075,sign*.152),(.246,.150,.016),.003)])
    add("Box",merge(sewingbox),position=ANCHORS["SewingBox"],euler=(0,90,0),
        anchors={"Stow":(-.09,.086,0),"LeftGrip":(-.132,.10,-.15),"RightGrip":(.222,.104,-.075)})
    lid=boards(0,.016,-.16,.282,.32,.016,3)
    for x in (-.088,.088):
        lid.append(box((x,-.002,-.16),(.022,.006,.263),.002))
    for z in (-.161,-.091):
        lid.append(box((.164,.010,z),(.066,.016,.014),.004))
    lid.append(box((.191,.010,-.126),(.018,.020,.086),.005))
    add("BoxLid",merge(lid),position=(0,.15,.16),euler=(105,0,0),parent="Box",anchors={"LidGrip":(.191,.010,-.126)})
    fixed,moving=[],[]
    for x in (-.082,.082):
        fixed.append(box((x,.137,.161),(.056,.034,.005),.002))
        moving.append(box((x,.008,-.023),(.055,.005,.041),.002))
        for offset in (-.020,.020):
            fixed.append(at(bp.u_rotated(turned((0,0,0),[(.0046,0),(.005,.001),(.005,.011)],12),
                            (0,0,-90)),(x+offset-.0055,.15,.16)))
        moving.append(at(bp.u_rotated(turned((0,0,0),[(.0045,0),(.005,.001),(.005,.026)],12),
                         (0,0,-90)),(x-.013,0,0)))
    add("BoxHinges",merge(fixed),"RustedIron",parent="Box",solid=False)
    add("LidHinges",merge(moving),"RustedIron",parent="BoxLid",solid=False)


def lining_reveal_bridge(width,height,sill=True):
    """Continue aperture returns across the hidden gap without narrowing it."""
    rim=.012
    parts=[box((side*(width+rim)*.5,0,0),(rim,height,.02),0) for side in (-1,1)]
    parts.append(box((0,(height+rim)*.5,0),(width+rim*2,rim,.02),0))
    if sill:parts.append(box((0,-(height+rim)*.5,0),(width+rim*2,rim,.02),0))
    return merge(parts)


def validate_lining_clearance(objects):
    """Measure the actual back of every exposed lining face against the shell."""
    found={obj.name.removeprefix("GEO_Workroom_"):obj for obj in objects if obj.type=="MESH"}
    trees={name:BVHTree.FromPolygons(*doors.geometry(obj),all_triangles=False) for name,obj in found.items()}
    shells=[trees[name] for name in ("ShellWalls","ShellPlinth","ShellTimber")]
    # Source space is Blender XYZ = room XZY. Inner planes never move.
    surfaces=(("FrontLining",1,2.53,Vector((0,1,0)),.04),
              ("LeftLining",0,-3.30,Vector((-1,0,0)),.04),
              ("RearLining",1,-2.27,Vector((0,-1,0)),.04),
              ("PrivatePartition",0,1.20,Vector((1,0,0)),.16),
              ("Ceiling",2,2.30,Vector((0,0,1)),.10))
    counts={};minimum_gap=float("inf")
    for name,axis,plane,outward,thickness in surfaces:
        obj=found[name];count=0
        for face in obj.data.polygons:
            points=[obj.data.vertices[i].co.copy() for i in face.vertices]
            if face.normal.dot(outward)>-.999 or any(abs(p[axis]-plane)>.00002 for p in points):continue
            center=sum(points,Vector())/len(points)
            # Centroid plus inset corners also sample narrow spans beside apertures.
            for point in [center]+[center.lerp(p,.60) for p in points]:
                back=trees[name].ray_cast(point+outward*.0001,outward,.30)
                assert back[0] is not None and abs(back[3]+.0001-thickness)<.009,("Missing lining back",name,tuple(point),back)
                for shell in shells:
                    hit=shell.ray_cast(point-outward*.01,outward,6)
                    if hit[0] is None:continue
                    gap=hit[3]-.01-thickness
                    assert gap>=.015,("Shell overlaps or crowds lining",name,tuple(point),"gap",gap)
                    minimum_gap=min(minimum_gap,gap)
                count+=1
        assert count>=5,("No exposed lining face sampled",name)
        counts[name]=count
    for name in ("Floor","PrivateFloor"):
        obj=found[name];count=0
        for face in obj.data.polygons:
            if face.normal.z>-.999:continue
            points=[obj.data.vertices[i].co.copy() for i in face.vertices]
            center=sum(points,Vector())/len(points)
            if center.z>=0:continue
            for point in [center]+[center.lerp(p,.60) for p in points]:
                # Start above the finished floor, not inside a foundation that
                # would hide an overlap by returning only its far bottom face.
                origin=Vector((point.x,point.y,.01))
                for shell in shells:
                    hit=shell.ray_cast(origin,Vector((0,0,-1)),1)
                    if hit[0] is None:continue
                    gap=point.z-hit[0].z
                    assert gap>=.015,("Shell intersects floor underside",name,tuple(point),"gap",gap)
                    minimum_gap=min(minimum_gap,gap)
                count+=1
        assert count>=5,("No floor underside sampled",name)
        counts[name]=count
    # At the aperture perimeter, a same-plane continuation of the return
    # closes the air gap. These rays check both exact aperture size and cover.
    apertures=(("FrontLining",Vector((-2.10,2.58,1.50)),Vector((1,0,0)),Vector((0,1,0)),.60,.43,True),
               ("FrontLining",Vector((ANCHORS["RoomEntry"][0],2.58,1.085)),Vector((1,0,0)),Vector((0,1,0)),.52,1.085,False),
               ("LeftLining",Vector((-3.35,-1.70,1.50)),Vector((0,1,0)),Vector((1,0,0)),.36,.40,True))
    count=0
    for name,center,horizontal,depth_axis,half_width,half_height,sill in apertures:
        directions=[(horizontal,half_width),(-horizontal,half_width),(Vector((0,0,1)),half_height)]
        if sill:directions.append((Vector((0,0,-1)),half_height))
        for depth in (-.0095,0,.0095):
            for direction,distance in directions:
                hit=trees[name].ray_cast(center+depth_axis*depth,direction,distance+.02)
                assert hit[0] is not None and abs(hit[3]-distance)<.0001,("Open or narrowed aperture return",name,depth,tuple(direction),hit)
                count+=1
    counts["ApertureReturns"]=count
    print("WORKROOM SURFACE CLEARANCE:",counts,"minimum shell gap",round(minimum_gap,6),"m")


def build_pack():
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
    mats={}
    for name,color in COLORS.items():
        mat=bpy.data.materials.get(name) or bpy.data.materials.new(name); mat.diffuse_color=color; mats[name]=mat
    source_root=bpy.data.objects.new("VillageWorkroom08",None); bpy.context.scene.collection.objects.link(source_root)
    objects=[]; rows=[]
    def add(name,g,surface="Timber",position=(0,0,0),euler=(0,0,0),parent="",anchors=None,solid=True,source=False,role="",scale=(1,1,1)):
        obj=doors.mesh_object("GEO_Workroom_"+name,g if source else u(g),source_root,mats[surface])
        part=doors.part_row(obj,role or name,surface,list(FINISHES.get(name,COLORS[surface])))
        part.update(name=name,position=position,euler=euler,parent=parent,solid=solid,scale=scale,
                    anchors=[dict(name=k,position=v) for k,v in (anchors or {}).items()])
        rows.append(part); objects.append(obj); return obj
    _,width,depth,height,across=doors.HOUSES[1]; wall=depth*.43
    # This pack alone replaces 08. Existing 04/11 FBX data and their route anchors are never rewritten.
    for original in doors.village.build_village_house(1).parts:
        if original.part_role not in doors.ROLES: continue
        obj=doors.mesh_object("GEO_Workroom_Shell"+original.part_role,original.geometry,source_root,mats["Timber"],True)
        for v in obj.data.vertices:
            x,y,z=v.co;v.co=(x*width,y*depth,(z+.5)*height)
        envelope=kit.bounds(doors.geometry(obj))
        if original.part_role=="Plinth":
            # A real foundation below the unchanged facade envelope supports
            # the masonry where the interior soil bed ends beneath the wall.
            base=doors.mesh_object("buried-foundation",kit.box((0,0,-.0995),(width*.95,depth*.89,.201)),None,mats["Timber"])
            modifier=obj.modifiers.new("grounded-foundation","BOOLEAN")
            modifier.operation="UNION";modifier.solver="EXACT";modifier.use_self=True;modifier.object=base
            bpy.context.view_layer.objects.active=obj;bpy.ops.object.modifier_apply(modifier=modifier.name)
            bpy.data.objects.remove(base,do_unlink=True)
            envelope=(tuple(envelope[0][:2])+(-.20,),envelope[1])
        for name,center,size in (
            # Only the hidden shell cavity grows: 20 mm behind the 40 mm
            # linings, the 160 mm partition and the 100 mm ceiling slab.
            # All room-facing planes, apertures and floor datums stay fixed.
            ("room",(-.99,.13,1.1425),(4.74,4.92,2.555)),
            ("private",(2.35,-.435,1.1425),(2.30,3.67,2.555)),
            ("entry",(across,wall-.04,1.0175),(1.04,.64,2.305)),
            ("front-window",(-2.10,2.67,1.50),(1.20,.60,.86)),
            ("left-window",(-3.53,-1.70,1.50),(.86,.72,.80))):
            doors.cut_box(obj,name,center,size)
        after=kit.bounds(doors.geometry(obj))
        assert all(abs(a-b)<.00002 for p,q in zip(envelope,after) for a,b in zip(p,q)),("House08 exterior envelope changed",original.part_role,envelope,after)
        part=doors.part_row(obj,original.part_role,original.surface_kind,list(COLORS["Timber"]))
        part.update(name="Shell"+original.part_role,position=(0,0,0),euler=(0,0,0),scale=(1,1,1),parent="",solid=True,anchors=[])
        rows.append(part); objects.append(obj)

    floor=[box((-3.175+i*.25,-.0575,.13),(.247,.115,4.79),.003) for i in range(18)]
    # Real boarding below the three-millimetre seams, and the same level
    # threshold through the masonry reveal. No terrain is an indoor floor.
    floor += [box((-1.05,-.05,.13),(4.5,.06,4.8),.002),
              box((across,-.060,2.65),(1.04,.110,.29),.002)]
    # The sill is flush at zero. Timber either side carries a pinned metal strip.
    floor += boards(across,0,2.65,1.04,.29,.005,2)
    floor_obj=add("Floor",merge(floor))
    saddles = [box((across,.001,2.657),(1.005,.002,.040),.0008)]
    for x in (-.42,-.14,.14,.42):
        saddles.append(turned((across+x,.002,2.657),[(.006,0),(.0055,.001)],12))
    add("ThresholdStrip",merge(saddles),"RustedIron",solid=False)
    floor_tree=BVHTree.FromPolygons(*doors.geometry(floor_obj),all_triangles=False)
    for key in ("RoomEntry","RepairDock","SewingDock","PlayerHelpDock","BenchApproach"):
        x,_,z=ANCHORS[key]
        point,normal,_,_=floor_tree.ray_cast(Vector((x,z,1)),Vector((0,0,-1)),1.10)
        assert point is not None and -.055 <= point.z <= .00001 and normal.z > .99,("No upright floor at",key)
    # The ground blend lies beneath the real foundation. Even at the front
    # floor edge its worst diagonal interpolation stays below the underboards;
    # the outer threshold meets the original outdoor shelf flush at zero.
    cell_diagonal=math.sqrt(2)*.125
    assert .178 > cell_diagonal,"Lowered terrain can escape the real plinth"
    clearance=min(width*.475-3.50,depth*.445-2.53)-.178
    assert .16*clearance/cell_diagonal > .025,"Terrain intersects the occupied floor underboards"
    add("PrivateFloor",box((2.35,-.055,-.435),(2.30,.11,3.67)),"Timber")
    add("Ceiling",merge([box((-1.05,2.35,.13),(4.5,.10,4.8)),box((2.35,2.35,-.435),(2.30,.10,3.67))]),"Masonry")
    front=kit.wall_run(4.5,2.30,.04,[kit.Opening(-1.05,1.20,1.93,1.07),kit.Opening(1.046,1.04,2.17)],.003)
    front=merge([at(front,(-1.05,2.55,0)),u(at(lining_reveal_bridge(1.20,.86),(-2.10,1.50,2.58))),
                 u(at(lining_reveal_bridge(1.04,2.17,False),(ANCHORS["RoomEntry"][0],1.085,2.58)))])
    add("FrontLining",front,"Masonry",source=True)
    left=kit.wall_run(4.8,2.30,.04,[kit.Opening(-1.83,.72,1.90,1.10)],.003)
    left=merge([at(kit.rotated_z(left,90),(-3.32,.13,0)),
                u(at(bp.u_rotated(lining_reveal_bridge(.72,.80),(0,90,0)),(-3.35,1.50,-1.70)))])
    add("LeftLining",left,"Masonry",source=True)
    add("RearLining",box((-1.05,1.15,-2.29),(4.5,2.3,.04)),"Masonry")
    partition=kit.wall_run(4.8,2.30,.16,[kit.Opening(-1.78,.96,2.12)],.005)
    add("PrivatePartition",at(kit.rotated_z(partition,90),(1.28,.13,0)),"Masonry",source=True)
    add("PrivateScreen",box((1.80,1.15,.15),(.14,2.3,2.50)),"Masonry")
    validate_lining_clearance(objects)
    # Frames and clear panes occupy the very same openings on both sides of each wall.
    def window(name,center,w,h,side=False):
        x,y,z=center
        geom=merge([box((-w*.5-.028,0,0),(.056,h+.112,.12)),box((w*.5+.028,0,0),(.056,h+.112,.12)),
                    box((0,h*.5+.028,0),(w,.056,.12)),box((0,-h*.5-.028,0),(w,.056,.12)),
                    box((0,0,0),(.037,h,.05)),box((0,-h*.5-.07,.005),(w+.19,.065,.25))])
        # Glazing beads follow the frame. A separate nosing and apron show its depth.
        trim=[box((sign*(w/2+.014),0,-.065),(.022,h+.040,.016),.003) for sign in (-1,1)]
        trim.extend([box((0,sign*(h/2+.012),-.065),(w,.022,.016),.003) for sign in (-1,1)])
        trim.extend([box((0,-h/2-.101,-.084),(w+.13,.044,.055),.006),
                     box((0,-h/2-.071,-.129),(w+.18,.039,.034),.007)])
        geom=merge([geom]+trim)
        glass=box((0,0,0),(w,h,.008),.001)
        if side: geom=bp.u_rotated(geom,(0,90,0));glass=bp.u_rotated(glass,(0,90,0))
        add(name+"Frame",at(geom,center));add(name+"Glass",at(glass,center),"Glass")
    window("FrontWindow",ANCHORS["FrontWindow"],1.20,.86)
    window("LeftWindow",ANCHORS["LeftWindow"],.72,.80,True)
    severin_bench(add)
    marta_fittings(add)
    bench=boards(-.10,.44,2.99,1.35,.46,.048,3)
    bench=[bp.u_rotated(board,(0,-90,0)) for board in bench]
    for z in (-.62,.42):
        for x in (-3.156,-2.824):
            bench.append(box((x,.196,z),(.069,.392,.074),.006))
        bench.extend([box((-2.99,.16,z),(.36,.071,.053)),
                      box((-3.201,.66,z),(.047,.52,.060),.006)])
    for y,h in ((.61,.13),(.806,.16)):
        bench.append(box((-3.20,y,-.10),(.043,h,1.35),.007))
    bench.append(box((-2.99,.16,-.10),(.055,.072,1.10)))
    add("Bench",merge(bench))
    cabinet=[box((.76,.706,1.72),(.56,1.40,.64)),box((.76,1.424,1.72),(.59,.04,.68)),
             box((.76,.735,1.382),(.51,1.32,.030))]
    for x in (.541,.979):
        cabinet.append(box((x,.735,1.357),(.060,1.302,.028),.004))
    for y in (.125,.774,1.347):
        cabinet.append(box((.76,y,1.357),(.382,.065,.028),.005))
    add("Cabinet",merge(cabinet))
    handle=[box((.925,y,1.331),(.019,.023,.052),.003) for y in (.723,.809)]
    handle.append(box((.925,.766,1.306),(.022,.106,.022),.006))
    add("CabinetPull",merge(handle),"RustedIron",solid=False)
    shelves=[]
    for y in (1.43,1.83):
        shelves.extend(boards(-2.12,y+.0275,-2.11,1.68,.25,.055,2))
        for x in (-2.78,-1.44):
            shelves.append(box((x,y-.13,-2.19),(.06,.26,.07)))
            shelves.append(at(bp.u_rotated(box((0,0,0),(.046,.199,.046)),(38,0,0)),(x,y-.11,-2.116)))
    add("Shelves",merge(shelves))
    stock=[]
    for i in range(3):
        for layer in range(i+1):
            stock.append(box((-2.62+i*.24,1.470+layer*.020,-2.11),(.19-.004*layer,.023,.17),.007))
    for x,y in ((-1.67,1.458),(-2.27,1.858)):
        stock.append(at(bp.u_rotated(turned((0,0,0),[(.048,0),(.058,.008),(.060,.24),(.053,.25)],14),
                        (0,0,-90)),(x-.12,y+.060,-2.13)))
    add("ShelfStock",merge(stock),"Cloth")
    stand=boards(.77,.38,.45,.55,.55,.046,3)
    for x in (.56,.98):
        for z in (.24,.66):
            stand.append(box((x,.168,z),(.054,.336,.060)))
        stand.append(box((x,.13,.45),(.036,.051,.44)))
    add("BasketStand",merge(stand))
    add("Thread",bp.u_cylinder((0,.5,0),(.003,.5,.003),6),"Cloth",position=ANCHORS["MittenRest"],
        solid=False,anchors={"Start":(0,0,0),"End":(0,1,0)},scale=(1,.06,1))
    # Each lighting anchor has a visible, plain household practical.
    for name in ("RoomLamp","RepairLamp","SewingLamp"):
        p=ANCHORS[name]
        shade=turned(p,[(.040,.070),(.046,.062),(.056,.040),(.105,-.033),(.123,-.055),
                        (.126,-.068),(.123,-.075),(.116,-.075),(.112,-.062),(.049,.034),(.033,.053)][::-1],20)
        add(name+"Shade",shade,"Lamp")
        bulb=turned((0,0,0),[(.005,-.069),(.022,-.065),(.033,-.053),(.036,-.033),
                             (.031,-.014),(.017,.007),(.014,.028)],16)
        add(name+"Bulb",at(bulb,p),"Lamp")
        socket=turned(p,[(.018,.025),(.022,.030),(.022,.050),(.030,.053),(.030,.074),(.021,.079)],14)
        if name=="RoomLamp":
            stem=[box((p[0],2.23,p[2]),(.018,.14,.018),.003),
                  turned((p[0],2.275,p[2]),[(.042,0),(.056,.011),(.058,.025)],14)]
        elif name=="RepairLamp":
            stem=[box((p[0],p[1]+.075,-2.251),(.13,.17,.038),.007),
                  box((p[0],p[1]+.087,-2.168),(.032,.035,.160),.005)]
        else:
            stem=[turned((p[0],.660,p[2]),[(.093,0),(.093,.010),(.085,.019),(.048,.027),(.021,.039)],16),
                  spindle((p[0],.697,p[2]),.577,.014)]
        stem.append(socket)
        add(name+"Stem",merge(stem),"RustedIron")

    # Exact real geometry must leave the common doorway, both window openings and shared circulation clear.
    solids=[obj for obj,row in zip(objects,rows) if row["solid"] and row["position"]==(0,0,0) and not row["parent"] and row["surface"]!="Glass"]
    trees=[BVHTree.FromPolygons(*doors.geometry(obj),all_triangles=False) for obj in solids]
    def hit(a,b):
        a=Vector((a[0],a[2],a[1]));b=Vector((b[0],b[2],b[1]));d=b-a
        return any(tree.ray_cast(a,d.normalized(),d.length)[0] is not None for tree in trees)
    for y in (.35,1.10,1.75):
        assert not hit((across,y,wall+.50),(across,y,1.20)),"Blocked workroom entry"
        for key in ("Hidden0","Hidden1"):
            target=ANCHORS[key]
            for x,z in ((-3.05,-2.0),(-2,1.8),(.6,.4),(.6,-1.65)):
                assert hit((x,y,z),(target[0],y,target[2])),("Visible private parking",key,x,z)
    for slot in (0,1):
        parked=Vector(ANCHORS["Hidden"+str(1-slot)])
        route=[Vector(ANCHORS[n]) for n in ("Interior","Turn"+str(slot),"Hidden"+str(slot))]
        for a,b in zip(route,route[1:]):
            axis=b-a;t=max(0,min(1,(parked-a).dot(axis)/axis.length_squared))
            assert (a+axis*t-parked).length>.90,"Private routes intersect the other resident"
    signature=hashlib.sha256(json.dumps(rows,sort_keys=True).encode())
    for obj in objects: signature.update(json.dumps(doors.geometry(obj),separators=(",",":")).encode())
    data=dict(generator_version=VERSION,design_id=DESIGN,scale_mode="fixed_metres",build_signature=signature.hexdigest(),
              house_id="village-house-08",room_min=(-3.3,0,-2.27),room_max=(1.2,2.3,2.53),
              mesh_count=len(objects),triangle_count=sum(r["triangles"] for r in rows),
              anchors=[dict(name=n,position=p) for n,p in ANCHORS.items()],parts=rows)
    return data,objects


def preview(path,objects,data):
    rows={p["name"]:p for p in data["parts"]}; moved={}
    # Review renders alone omit two near walls/ceiling; exports and runtime collision always retain them.
    for obj,row in zip(objects,data["parts"]):
        obj.hide_render=row["name"] in ("Ceiling","FrontLining","LeftLining","ShellWalls","ShellTimber","ShellPlinth")
        p=Vector(row["position"])
        if row["parent"]: obj.parent=moved[row["parent"]]
        obj.location=(p.x,p.z,p.y)
        # Convert the complete local transform, retaining parented lid/flap/head articulation.
        basis=bp.u_rotated(([(1,0,0),(0,1,0),(0,0,1)],[]),row["euler"])[0]
        swap=Matrix(((1,0,0),(0,0,1),(0,1,0)))
        rotation=swap @ Matrix(basis).transposed() @ swap
        obj.rotation_mode="QUATERNION";obj.rotation_quaternion=rotation.to_quaternion()
        s=row["scale"];obj.scale=(s[0],s[2],s[1])
        moved[row["name"]]=obj
    scene=bpy.context.scene
    camera=bpy.data.objects.new("Review Camera",bpy.data.cameras.new("Review Camera"));scene.collection.objects.link(camera)
    camera.location=(-5.4,5.1,3.1);camera.rotation_euler=(Vector((-.8,.1,.9))-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.lens=34;scene.camera=camera;scene.render.engine="BLENDER_WORKBENCH"
    scene.display.shading.light="STUDIO";scene.display.shading.color_type="MATERIAL"
    scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.world.color=(.15,.14,.12)
    scene.render.resolution_x=1440;scene.render.resolution_y=960;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG";scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/VillageLife")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/VillageLife")
    parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    first,_=build_pack();data,objects=build_pack();assert first==data,"Non-deterministic workroom geometry"
    from village_unique_surfaces import apply as apply_unique, DESIGN as UNIQUE_DESIGN
    apply_unique(objects, data["parts"], "Workroom")
    data["unique_surface_design"] = UNIQUE_DESIGN
    if args.validate_only: assert json.loads((args.model_dir/"VillageWorkroom3D.json").read_text())==json.loads(json.dumps(data))
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(args.model_dir/"VillageWorkroom3D.fbx"),use_selection=True,object_types={"EMPTY","MESH"},
            axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_space_transform=True,add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE")
        (args.model_dir/"VillageWorkroom3D.json").write_text(json.dumps(data,indent=2)+"\n",encoding="utf-8")
        bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"VillageWorkroom3D.blend"))
        if not args.no_preview:preview(args.source_dir/"VillageWorkroom3D.png",objects,data)
    print("VILLAGE WORKROOM VALIDATION OK: true room and openings, two occluded night docks; "+str(data["mesh_count"])+" meshes; "+data["build_signature"])

if __name__=="__main__":main()
