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
spec = importlib.util.spec_from_file_location("workroom_door_source", ROOT / "tools/build-village-resident-doors-3d-model.py")
doors = importlib.util.module_from_spec(spec); sys.modules[spec.name] = doors; spec.loader.exec_module(doors)
VERSION = "1.0.0"
DESIGN = "village_workroom_08_v1"
ANCHORS = {
    "RoomEntry":(-.004,0,1.60), "RepairDock":(-2.02,0,-.97), "RepairJoin":(-1.86,.90,-1.45),
    "RepairLeftSupport":(-1.75,.90,-1.45), "HammerRest":(-2.20,.90,-1.37),
    "RepairRailRest":(-1.86,.90,-1.45), "RepairRailFixed":(-1.875,.90,-1.45),
    "SewingDock":(-2.10,0,.98), "SewingSeat":(-2.10,.42,.78), "ClothRest":(-2.10,.674,1.36),
    "MittenRest":(-2.02,.690,1.36), "SewingBox":(-1.73,.666,1.33), "BoxStow":(-1.73,.706,1.42), "BoxRest":(-1.55,.46,1.10),
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


def u(g): return bp.to_source(g)
def at(g, p): return kit.translated(g,p)
def merge(items): return kit.merge_all(items)
def box(p,s,c=.008): return bp.u_box(p,s,c)


def table(cx,cz,width,depth,height,top=.065):
    parts=[box((cx,height-top*.5,cz),(width,top,depth),.012),
           box((cx,height-top-.105,cz-depth*.5+.08),(width-.12,.17,.055)),
           box((cx,height-top-.105,cz+depth*.5-.08),(width-.12,.17,.055))]
    for x in (-1,1):
        for z in (-1,1):
            # Shared turned-leg profile is authored vertically in Blender then swapped to Unity.
            source=kit.turned_leg(height-top,.042,.029,.036,8)
            leg=([(a,c,b) for a,b,c in source[0]],[tuple(reversed(f)) for f in source[1]])
            parts.append(at(leg,(cx+x*(width*.5-.10),0,cz+z*(depth*.5-.10))))
    return merge(parts)


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
        part=doors.part_row(obj,role or name,surface,list(COLORS[surface]))
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
              box((across,-.0575,2.65),(1.04,.115,.29),.002)]
    floor_obj=add("Floor",merge(floor))
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
        glass=box((0,0,0),(w,h,.008),.001)
        if side: geom=bp.u_rotated(geom,(0,90,0));glass=bp.u_rotated(glass,(0,90,0))
        add(name+"Frame",at(geom,center));add(name+"Glass",at(glass,center),"Glass")
    window("FrontWindow",ANCHORS["FrontWindow"],1.20,.86)
    window("LeftWindow",ANCHORS["LeftWindow"],.72,.80,True)
    add("Workbench",table(-2.08,-1.82,2,.82,.86,.09))
    add("SewingTable",table(-2.10,1.65,1.40,.70,.66))
    add("BoxShelf",table(-1.55,1.10,.46,.40,.46,.045))
    # The seat centre follows the resident's actual pelvis, twenty centimetres behind her ground dock.
    stool=[table(-2.10,.78,.46,.44,.42,.055),box((-2.10,.68,.60),(.45,.46,.042)),
           box((-2.10,.49,.597),(.32,.08,.027))]
    add("SewingSeat",merge(stool))
    bench=[box((-2.99,.4125,-.10),(.46,.055,1.35),.013),box((-3.205,.72,-.10),(.052,.56,1.35),.013)]
    for z in (-.60,.40):
        for x in (-3.15,-2.83): bench.append(box((x,.19,z),(.058,.38,.068)))
    bench.append(box((-2.99,.16,-.10),(.055,.065,1.13)))
    add("Bench",merge(bench))
    cabinet=[box((.76,.72,1.72),(.58,1.44,.68)),box((.76,.74,1.365),(.51,1.31,.035)),
             box((.93,.78,1.338),(.055,.055,.045))]
    add("Cabinet",merge(cabinet))
    shelves=[]
    for y in (1.43,1.83):
        shelves.extend([box((-2.12,y,-2.11),(1.68,.055,.25)),box((-2.78,y-.13,-2.19),(.055,.26,.07)),box((-1.44,y-.13,-2.19),(.055,.26,.07))])
    add("Shelves",merge(shelves))
    clutter=[]
    for i in range(3):
        clutter.append(box((-2.62+i*.24,1.485,-2.11),(.19,.06,.18),.007))
    clutter.extend([box((-1.65,1.57,-2.14),(.28,.22,.14)),box((-2.26,1.92,-2.13),(.34,.12,.15))])
    add("ShelfStock",merge(clutter),"Cloth")
    stand=[box((.77,.35,.45),(.55,.06,.55))]
    for x in (.56,.98):
        for z in (.24,.66):stand.append(box((x,.16,z),(.055,.32,.055)))
    add("BasketStand",merge(stand))
    # One old chair lies on the bench. The pegged repair is lower than the two hand rails held by a helper.
    # A recognizable side-laid chair: upright seat in the bench frame, four legs pointing right,
    # and a slatted back to the left. No chair leg passes through the worktop.
    chair=[box((-1.50,1.14,-1.70),(.055,.50,.50)),box((-1.91,.86,-1.4775),(.07,.08,.055))]
    for y in (.91,1.37):
        for z in (-1.92,-1.48):chair.append(box((-1.265,y,z),(.47,.05,.05)))
        chair.append(box((-1.755,y,-1.92),(.56,.048,.05)))
    for x in (-1.70,-1.91):chair.append(box((x,1.14,-1.92),(.075,.46,.035)))
    add("ChairFrame",merge(chair))
    rail=merge([box((.41,-.04,-.0275),(.82,.08,.055),.004),box((.82,.072,-.25),(.055,.056,.52),.004)])
    add("RepairRail",rail,position=ANCHORS["RepairRailRest"],anchors={"Join":(0,0,0),"LeftSupport":(.11,0,0),
        "ChairPartLeftGrip":(.82,.10,-.47),"ChairPartRightGrip":(.82,.10,-.03)})
    add("RepairClamp",merge([box((-1.95,.87,-1.57),(.035,.20,.04)),box((-1.95,.975,-1.51),(.035,.035,.16)),
        box((-1.95,.79,-1.51),(.035,.035,.16))]),"RustedIron")
    add("Hammer",box((0,.098,0),(.033,.336,.034),.006),position=ANCHORS["HammerRest"],euler=(90,180,0),
        anchors={"Grip":(0,0,0),"StrikeFace":(-.095,.265,0)})
    add("HammerHead",merge([box((0,.265,0),(.19,.041,.053),.008),box((.068,.269,0),(.10,.043,.039),.005)]),
        "RustedIron",parent="Hammer")
    add("Cloth",box((.09,.0025,0),(.18,.005,.26),.002),"Cloth",position=ANCHORS["ClothRest"],
        anchors={"Grip":(.09,.02,0),"FoldHinge":(0,.025,0),"PacketLeftGrip":(.15,.0475,-.08),
                 "PacketRightGrip":(.15,.0475,.08),"HoldEdge":(.135,.014,.09)})
    add("ClothFlap",box((-.09,-.0225,0),(.18,.005,.26),.002),"Cloth",position=(0,.025,0),parent="Cloth",
        anchors={"EdgeGrip":(-.15,-.0225,-.08)})
    mitten=merge([box((.005,0,0),(.125,.025,.13),.011),box((-.055,0,.034),(.043,.025,.067),.010),box((.068,-.002,0),(.040,.024,.102),.008)])
    add("Mitten",mitten,"Cloth",position=ANCHORS["MittenRest"],anchors={"Grip":(-.065,.015,0),"Stitch":(.025,.015,.015)})
    sewingbox=merge([box((0,.011,0),(.43,.022,.36)),box((-.203,.085,0),(.024,.15,.36)),box((.203,.085,0),(.024,.15,.36)),
                     box((0,.085,-.168),(.39,.15,.024)),box((0,.085,.168),(.39,.15,.024))])
    add("Box",sewingbox,position=ANCHORS["SewingBox"],euler=(0,90,0),anchors={"Stow":(-.09,.04,0),"LeftGrip":(-.14,.10,-.16),"RightGrip":(.14,.10,-.16)})
    add("BoxLid",box((0,.008,-.18),(.43,.016,.36),.005),position=(0,.15,.18),euler=(105,0,0),parent="Box",anchors={"LidGrip":(0,.015,-.31)})
    add("Thread",bp.u_cylinder((0,.5,0),(.003,.5,.003),6),"Cloth",position=ANCHORS["MittenRest"],
        solid=False,anchors={"Start":(0,0,0),"End":(0,1,0)},scale=(1,.06,1))
    # Each lighting anchor has a visible, plain household practical.
    for name in ("RoomLamp","RepairLamp","SewingLamp"):
        p=ANCHORS[name]
        shade=kit.lathe([(.12,-.075),(.12,-.058),(.045,.065),(.027,.073)],10)
        shade_u=([(a,c,b) for a,b,c in shade[0]],[tuple(reversed(f)) for f in shade[1]])
        add(name+"Shade",at(shade_u,p),"Lamp")
        add(name+"Bulb",at(bp.u_cylinder((0,-.03,0),(.045,.025,.045),8),p),"Lamp")
        if name=="RoomLamp": stem=box((p[0],2.23,p[2]),(.022,.14,.022),.003)
        elif name=="RepairLamp":stem=merge([box((p[0],p[1]+.05,-2.20),(.12,.20,.035)),box((p[0],p[1]+.05,-2.14),(.025,.025,.12),.003)])
        else:stem=merge([box((p[0],.69,p[2]),(.18,.035,.16)),box((p[0],.935,p[2]),(.023,.49,.023),.003)])
        add(name+"Stem",stem,"RustedIron")

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
