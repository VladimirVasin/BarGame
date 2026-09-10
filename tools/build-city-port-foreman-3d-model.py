#!/usr/bin/env python3
"""One stout elderly dock foreman, on the unchanged production NpcHumanV2 rig.

The shared resident builder owns anatomy, skinning, UVs and export. This file
owns his barrel coat, age/face, stool and seven grounded seated actions. No world
placement, speech text or gameplay is authored into the passive asset.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("port_foreman_resident",Path(__file__).with_name("build-village-residents-3d-model.py"))
resident=importlib.util.module_from_spec(spec)
sys.modules[spec.name]=resident
spec.loader.exec_module(resident)
base=resident.base
VERSION="1.3.0"
FPS=24
CLIPS=(("SeatedIdle",4.0,True),("SeatedGrumble",4.0,True),
       ("CarrotBite1",4.0,False),("CarrotBite2",4.0,False),("CarrotBite3",4.0,False),
       ("CarrotDiscard",2.4,False),("CarrotTake",2.4,False))
SEAT_TOP=.50
PELVIS_HEIGHT=base.NPC_PELVIS_HEIGHT+SEAT_TOP-(.910-.132)
FOOT_X=.37
FOOT_Y=-.32
HAND_GRIP=Vector(base.BONE_BY_NAME["SOCKET_Grip.L"].head)
HAND_AXIS=(Vector(base.BONE_BY_NAME["hand.L"].tail)-Vector(base.BONE_BY_NAME["hand.L"].head)).normalized()
PALM_NORMAL=(Vector((0,-1,0))-HAND_AXIS*Vector((0,-1,0)).dot(HAND_AXIS)).normalized()
# The shared socket lies inside the neutral hand. Food is held on its volar
# surface, between the opposed thumb and curled fingers, never through its back.
CARROT_GRIP=HAND_GRIP+PALM_NORMAL*.055
CARROT_TIP=CARROT_GRIP+Vector((0,0,.205))
CARROT_TIPS=(.205,.140,.075)
BITE_MOUTH=Vector((0,-.146,1.533))
BITE_COMMIT=1.65
POCKET=Vector((.18,-.318,1.125))
BIN_TARGET=Vector((.80,.05,.18))


def atlas(path,validate_only=False):
    c=base.atlas_kit.PixelCanvas(256,256)
    c.rect(0,0,256,256,(247,246,241,255))
    # The same resident UV cells: quilted/stitched coat, knit, boot leather,
    # plain cloth and a 64-pixel drawn face. All values multiply body colours.
    for y in range(128):
        for x in range(128):
            value=235+(x*17+y*13)%15
            c.put(x,y,(value,value,value,255))
    c.line(63,3,63,124,(147,151,143,255),1)
    for y in (28,50,72,95):c.ellipse(67,y,2,2,(107,112,102,255))
    for x in (14,84):
        c.line(x,62,x+30,64,(143,151,139,255))
        c.line(x,64,x,89,(185,191,178,255))
        c.line(x,89,x+30,89,(185,191,178,255))
    c.rect(85,106,111,119,(203,207,193,255))
    for x in range(87,112,4):c.line(x,104,x+1,108,(145,153,138,255))
    for y in range(4,124,5):c.line(132,y,252,y,(216,220,209,255))
    for y in (203,217,237):c.line(7,y,119,y,(139,145,132,255),1)
    for y in range(154,197,8):
        c.line(43,y,80,y+7,(161,164,151,255))
        c.line(80,y,43,y+7,(184,188,173,255))
    # Heavy brows slope down toward the nose. Bags, folded lids, creases and
    # downturned mouth carry discontent without theatrical anger or symbols.
    c.rect(192,192,256,256,(248,241,232,255))
    for y in (202,206,209):c.line(209,y,238,y+1,(184,174,154,255))
    for left,right in ((204,218),(230,244)):
        mid=(left+right)//2
        c.line(left,215 if left==204 else 219,right,219 if left==204 else 215,(111,113,105,255),1)
        c.line(left,222,right,222,(151,139,120,255))
        c.rect(mid-1,221,mid+2,224,(47,50,44,255))
        c.line(left,227,right,228,(155,145,127,255))
        c.line(left-1,224,left-3,228,(176,162,140,255))
    c.line(225,221,222,233,(154,136,115,255))
    c.line(221,233,228,234,(120,105,87,255))
    c.line(210,236,207,245,(158,141,121,255))
    c.line(239,236,242,245,(158,141,121,255))
    c.line(213,245,220,241,(104,91,76,255))
    c.line(220,241,232,242,(104,91,76,255))
    c.line(232,242,238,246,(104,91,76,255))
    c.line(216,250,235,250,(179,158,137,255))
    for y in range(238,253):
        for x in range(202,247):
            if (x*3+y*7)%19==0:c.put(x,y,(188,183,166,255))
    # The previously unused lower-right middle cell gives the carrot a quiet
    # rough skin with fine root rings, rather than painting orange hand cloth.
    c.rect(192,128,256,192,(244,240,225,255))
    for y in range(131,191,9):
        for x in (195,218,240):
            c.line(x,y,x+10,y+1,(190,185,166,255))
    for y in range(130,191):
        for x in range(193,255):
            if (x*7+y*11)%43==0:c.put(x,y,(224,219,198,255))
    data=c.png_bytes()
    if validate_only:
        if path.read_bytes()!=data:raise RuntimeError("Foreman deterministic atlas differs")
    else:
        path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
    return hashlib.sha256(data).hexdigest()


class ForemanBuilder(resident.ResidentBuilder):
    def __init__(self):
        super().__init__(False)
        self.canonical=False

    def remap_geometry_point(self,point,bone_name,role,name):
        return Vector(point) if self.canonical else super().remap_geometry_point(point,bone_name,role,name)

    def remove(self,*prefixes):
        for part in list(self.result.parts):
            if part.obj.name.startswith(prefixes):
                self.result.parts.remove(part);bpy.data.objects.remove(part.obj,do_unlink=True)

    def build(self):
        result=super().build()
        self.remove("CLO_WinterCoat","CLO_Scarf","CLO_KnitCap","CLO_CapFold","CLO_CapEarFlap",
                    "CLO_Collar","CLO_PocketFlap","HAIR_Temple")
        palette={"f_skin":(.61,.47,.385,1),"f_coat":(.205,.275,.25,1),
                 "f_dark":(.12,.175,.155,1),"f_pants":(.195,.205,.20,1),
                 "f_hair":(.46,.47,.415,1),"f_boot":(.12,.13,.12,1),
                 "f_knit":(.32,.35,.31,1),"f_wood":(.34,.28,.22,1),
                 "f_metal":(.19,.23,.205,1),"f_mouth":(.20,.15,.125,1),
                 "f_carrot":(.70,.31,.105,1),"f_greens":(.235,.31,.16,1)}
        base.PALETTE.update(palette)
        color_map={"v_skin":"f_skin","v_coat":"f_coat","v_dark":"f_dark",
                   "v_pants":"f_pants","v_boot":"f_boot","v_knit":"f_knit",
                   "v_glove":"f_skin","v_hair":"f_hair","v_trim":"f_dark"}
        bpy.context.view_layer.update()
        for part in result.parts:
            key=color_map.get(part.palette_name,"f_skin")
            if part.obj.name.startswith(("GEO_Hand","GEO_Thumb")):key="f_skin"
            part.color=palette[key];part.obj.color=part.color;part.palette_name=key
            if part.obj.name in ("GEO_Head","GEO_FaceSurface"):
                for vertex in part.obj.data.vertices:
                    vertex.co.x*=1.19 if part.obj.name=="GEO_Head" else 1.13
            elif part.bone.startswith(("thigh.","shin.","upper_arm.","forearm.")):
                # Thicker clothed limbs retain their original joints and
                # rigid weights; the seated clips remain fully compatible.
                bone=base.BONE_BY_NAME[part.bone]
                start=Vector(bone.head);axis=(Vector(bone.tail)-start).normalized()
                inv=part.obj.matrix_world.inverted()
                for vertex in part.obj.data.vertices:
                    point=part.obj.matrix_world@vertex.co
                    along=start+axis*(point-start).dot(axis)
                    vertex.co=inv@(along+(point-along)*(1.40 if part.bone.startswith("thigh") else 1.24))
        self.canonical=True
        self.remove("GEO_Thumb.L")
        # The ordinary palm remains the same NpcHumanV2 anatomy. Four compact
        # fingers curl around the carrot from its far edge, and an opposing
        # thumb closes the grip. All follow hand.L, without extra rig bones.
        fingers=[]
        for height in (-.028,-.016,-.004,.008):
            points=[CARROT_GRIP+Vector((.0345*math.cos(math.radians(angle)),
                                       .0345*math.sin(math.radians(angle)),height))
                    for angle in (45,0,-40,-85,-130)]
            fingers.extend(base.make_frustum_between(a,b,.0068,.0066,7) for a,b in zip(points,points[1:]))
        self.add_part("GEO_ForemanGripFingers.L",base.combine_geometry(*fingers),"hand.L","body_detail","f_skin")
        thumb=[CARROT_GRIP+Vector(p) for p in ((-.049,.039,.014),(-.044,.006,.013),(-.029,-.025,.012),(-.014,-.032,.006))]
        self.add_part("GEO_Thumb.L",base.combine_geometry(*(base.make_frustum_between(a,b,.011-i*.001,.010-i*.001,8)
            for i,(a,b) in enumerate(zip(thumb,thumb[1:])))),"hand.L","body_detail","f_skin")
        # A true convex barrel, widest and deepest below the chest. The coat
        # is one skin, continuously weighted through belly, waist and chest.
        stations=((.88,.305,.225,.020),(1.00,.354,.266,-.035),(1.115,.360,.277,-.040),
                  (1.25,.322,.232,-.020),(1.385,.290,.180,-.002),(1.43,.247,.153,-.003))
        shell=self.add_part("CLO_ForemanBarrelCoat",base.make_vertical_shell(stations,20),"chest","clothing","f_coat")
        bpy.context.view_layer.update()
        for group in list(shell.vertex_groups):shell.vertex_groups.remove(group)
        groups={name:shell.vertex_groups.new(name=name) for name in ("pelvis","spine","chest")}
        for vertex in shell.data.vertices:
            height=(shell.matrix_world@vertex.co).z
            pair=("pelvis","spine") if height<1.12 else ("spine","chest")
            blend=max(0,min(1,(height-(.92 if height<1.12 else 1.12))/.20))
            groups[pair[0]].add([vertex.index],1-blend,"REPLACE")
            groups[pair[1]].add([vertex.index],blend,"REPLACE")
        self.add_part("CLO_ForemanSeat",base.make_ellipsoid((0,.050,.910),(.310,.235,.132),16,8),"pelvis","clothing","f_dark")
        self.add_part("CLO_ForemanCollar",base.make_vertical_shell(((1.393,.117,.108,-.015),(1.453,.104,.092,-.020)),16),"neck","clothing","f_knit")
        for side,sign in (("L",1), ("R",-1)):
            if side=="R":
                self.add_part("CLO_ForemanPocket."+side,base.make_tapered_box((sign*.205,-.240,1.07),(sign*.205,-.244,1.115),(.14,.018,0),(.155,.019,0)),"spine","clothing","f_dark")
            self.add_part("HAIR_ForemanTemple."+side,base.make_ellipsoid((sign*.094,.013,1.610),(.023,.058,.059),10,5),"head","hair","f_hair")
            self.add_part("GEO_ForemanJowl."+side,base.make_ellipsoid((sign*.067,-.020,1.525),(.050,.072,.055),12,6),"head","body_detail","f_skin")
            self.add_part("FACE_ForemanBrow."+side,base.make_frustum_between((sign*.018,-.128,1.642),(sign*.078,-.120,1.654),.009,.012,8),"head","face_detail","f_hair")
        self.add_part("GEO_ForemanChin",base.make_ellipsoid((0,-.049,1.486),(.094,.070,.034),14,6),"head","body_detail","f_skin")
        self.add_part("HAIR_ForemanBack",base.make_ellipsoid((0,.062,1.602),(.101,.038,.055),14,5),"head","hair","f_hair")
        self.add_part("FACE_ForemanLowerLip",base.make_ellipsoid((0,-.133,1.523),(.038,.010,.006),12,4),"face.mouth","face_detail","f_skin")
        for a,b in (((-.038,-.134,1.525),(0,-.140,1.533)),((0,-.140,1.533),(.038,-.134,1.525))):
            self.add_part("FACE_ForemanMouth"+str(len(result.parts)),base.make_frustum_between(a,b,.003,.003,6),"face.mouth","face_detail","f_mouth")
        # A usable open patch pocket follows the convex coat. Its opaque front,
        # two gussets and bottom conceal the fresh carrot before withdrawal.
        pocket_faces=[]
        for z0,z1 in ((.90,1.04),(1.04,1.14)):
            pocket_faces.append(base.make_box((.18,-.360,(z0+z1)/2),(.196,.015,z1-z0)))
            for x in (.082,.278):
                pocket_faces.append(base.make_box((x,-.317,(z0+z1)/2),(.014,.100,z1-z0)))
        pocket_faces.append(base.make_box((.18,-.316,.90),(.196,.10,.014)))
        self.add_part("CLO_ForemanPocket.L",base.combine_geometry(*pocket_faces),"spine","clothing","f_dark")
        self.add_part("CLO_ForemanPocketRim",base.make_box((.18,-.364,1.145),(.214,.023,.027)),"spine","clothing","f_coat")
        self.create_bone_anchor("ANCHOR_ForemanPocket","spine",POCKET,(0,0,1))

        # Three separately closed pieces vanish only at their bite commits.
        # Their exposed caps become the next genuine food/mouth contact.
        def carrot_section(a,ra,b,rb,offset=CARROT_GRIP):
            return base.make_frustum_between(offset+Vector((0,0,a)),offset+Vector((0,0,b)),ra,rb,12)
        carrots=[]
        for index,(a,ra,b,rb) in enumerate(((.140,.0148,.205,.0045),(.075,.0218,.140,.0148),(.010,.0272,.075,.0218)),1):
            carrots.append(self.add_part("FOOD_ForemanCarrotBite"+str(index),carrot_section(a,ra,b,rb),"hand.L","food","f_carrot"))
        stem_geometry=base.combine_geometry(carrot_section(-.033,.024,-.012,.029),carrot_section(-.012,.029,.010,.0272))
        carrots.append(self.add_part("FOOD_ForemanCarrotStem",stem_geometry,"hand.L","food","f_carrot"))
        stems=[]
        for dx,dy,length in ((-.010,.002,.055),(.009,.004,.041),(0,-.010,.048)):
            stems.append(base.make_frustum_between(CARROT_GRIP+Vector((dx,dy,-.029)),
                CARROT_GRIP+Vector((dx*1.7,dy*1.7,-.029-length)),.0045,.002,6))
        self.add_part("FOOD_ForemanCarrotGreens",base.combine_geometry(*stems),"hand.L","food","f_greens")
        self.create_bone_anchor("ANCHOR_ForemanCarrotTip","hand.L",CARROT_TIP,(0,0,1))
        for index,height in ((2,.140),(3,.075)):
            self.create_bone_anchor("ANCHOR_ForemanCarrotTip"+str(index),"hand.L",CARROT_GRIP+Vector((0,0,height)),(0,0,1))
        self.create_bone_anchor("ANCHOR_ForemanCarrotStemTip","hand.L",CARROT_GRIP+Vector((0,0,.010)),(0,0,1))
        self.create_bone_anchor("ANCHOR_ForemanStemGrip","hand.L",CARROT_GRIP,(0,0,1))
        self.create_bone_anchor("ANCHOR_ForemanBiteMouth","head",BITE_MOUTH,(0,-1,0))
        # Static throw copy is centred on the exact same grip frame as the
        # held stub. Runtime moves this imported host; it creates no geometry.
        thrown=self.create_pivot("MOVE_ForemanThrownStem",(0,0,0))
        bpy.context.view_layer.update()
        grip_inverse=result.anchors["ANCHOR_ForemanStemGrip"].matrix_world.inverted()
        for name,geometry,color in (("FOOD_ForemanThrownStem",stem_geometry,"f_carrot"),
                                    ("FOOD_ForemanThrownGreens",base.combine_geometry(*stems),"f_greens")):
            vertices,faces=geometry
            part=self.add_part(name,([tuple(grip_inverse@Vector(v)) for v in vertices],faces),"root","food",color)
            part.parent=thrown
            if color=="f_carrot":carrots.append(part)
        # Small worn open pail on his left. The hollow interior, rolled rim,
        # dark bottom and folded handle provide a visible destination for stems.
        pail=[];count=16;vertices=[]
        for radius,z in ((.133,.015),(.166,.345),(.153,.350),(.121,.035)):
            vertices.extend((.80+radius*math.cos(i*math.tau/count),.05+radius*math.sin(i*math.tau/count),z) for i in range(count))
        faces=[]
        for layer in range(3):
            for i in range(count):
                j=(i+1)%count;faces.append((layer*count+i,layer*count+j,(layer+1)*count+j,(layer+1)*count+i))
        for i in range(count):
            j=(i+1)%count;faces.append((3*count+i,3*count+j,j,i))
        self.add_part("GEO_ForemanStemPailShell",(vertices,faces),"root","furniture","f_metal")
        self.add_part("GEO_ForemanStemPailBottom",base.make_frustum_between((.80,.05,.015),(.80,.05,.035),.133,.121,16),"root","furniture","f_dark")
        for i in range(count):
            a=i*math.tau/count;b=(i+1)*math.tau/count
            pail.append(base.make_frustum_between((.80+.16*math.cos(a),.05+.16*math.sin(a),.35),(.80+.16*math.cos(b),.05+.16*math.sin(b),.35),.009,.009,6))
        self.add_part("GEO_ForemanStemPailRim",base.combine_geometry(*pail),"root","furniture","f_metal")
        for sign in (-1,1):
            self.add_part("GEO_ForemanStemPailHandle"+str(sign),base.make_frustum_between((.80+sign*.157,.05,.31),(.80+sign*.122,-.115,.22),.007,.007,6),"root","furniture","f_metal")
        self.add_part("GEO_ForemanStemPailHandleBar",base.make_frustum_between((.678,-.115,.22),(.922,-.115,.22),.007,.007,6),"root","furniture","f_metal")
        bin_anchor=bpy.data.objects.new("ANCHOR_ForemanBinTarget",None)
        result.export_collection.objects.link(bin_anchor);bin_anchor.parent=result.root;bin_anchor.location=BIN_TARGET
        result.anchors[bin_anchor.name]=bin_anchor
        # Broad plain timber stool: bevelled circular seat, four splayed legs,
        # cross stretchers, bolted brackets. It has no armature modifier.
        self.add_part("GEO_ForemanStool_Seat",base.make_vertical_shell(((.425,.274,.255,.05),(.44,.292,.272,.05),(.487,.292,.272,.05),(.50,.276,.256,.05)),20),"root","furniture","f_wood")
        for x in (-1,1):
            for y in (-1,1):
                a=(x*.244,.05+y*.215,.032);b=(x*.185,.05+y*.164,.446)
                self.add_part(f"GEO_ForemanStool_Leg{x}_{y}",base.make_frustum_between(a,b,.040,.043,8),"root","furniture","f_wood")
                self.add_part(f"GEO_ForemanStool_Foot{x}_{y}",base.make_box((a[0],a[1],.013),(.081,.080,.026)),"root","furniture","f_boot")
        for side in (-1,1):
            self.add_part("GEO_ForemanStool_RungX"+str(side),base.make_frustum_between((-.215,.05+side*.190,.225),(.215,.05+side*.190,.225),.021,.021,8),"root","furniture","f_wood")
            self.add_part("GEO_ForemanStool_RungY"+str(side),base.make_frustum_between((side*.215,-.140,.255),(side*.215,.240,.255),.021,.021,8),"root","furniture","f_wood")
        for part in result.parts:
            if len(part.obj.data.uv_layers)==0:self.uv(part)
        bpy.context.view_layer.update()
        for carrot in carrots:
            for loop in carrot.data.loops:
                offset=Vector((0,0,0)) if "Thrown" in carrot.name else CARROT_GRIP
                point=carrot.matrix_world@carrot.data.vertices[loop.vertex_index].co-offset
                u=.5+math.atan2(point.y,point.x)/math.tau
                v=max(0,min(1,(point.z+.033)/.238))
                carrot.data.uv_layers.active.data[loop.index].uv=((193+u*62)/256,(65+v*62)/256)
        # Coat front owns the same stitched cell as the shared resident coat.
        coat=next(p for p in result.parts if p.obj.name=="CLO_ForemanBarrelCoat")
        for loop in coat.obj.data.uv_layers.active.data:
            loop.uv.x=(loop.uv.x*256-130)/60*123/256+2/256
            loop.uv.y+=.5
        anchor=bpy.data.objects.new("ANCHOR_ForemanSeat",None)
        result.export_collection.objects.link(anchor);anchor.parent=result.root;anchor.location=(0,.05,SEAT_TOP)
        anchor["bp_export"]=True
        result.anchors[anchor.name]=anchor
        seated_pose(result,"CarrotDiscard",1.0/2.4)
        release=bpy.data.objects.new("ANCHOR_ForemanThrowRelease",None)
        result.export_collection.objects.link(release);release.parent=result.root
        release.matrix_world=result.anchors["ANCHOR_ForemanStemGrip"].matrix_world.copy()
        result.anchors[release.name]=release
        base.reset_pose(result.rig)
        self.canonical=False
        return result


def aim_bone(bone,tip):
    rest=(bone.bone.tail_local-bone.bone.head_local).normalized()
    rotation=rest.rotation_difference((Vector(tip)-bone.head).normalized())@bone.bone.matrix_local.to_quaternion()
    bone.matrix=Matrix.Translation(bone.head.copy())@rotation.to_matrix().to_4x4()
    bpy.context.view_layer.update()


def solve_legs(result):
    rig=result.rig
    for side,sign in (("L",1), ("R",-1)):
        upper=rig.pose.bones["thigh."+side];lower=rig.pose.bones["shin."+side];foot=rig.pose.bones["foot."+side]
        yaw=Matrix.Rotation(math.radians(sign*17),4,"Z")
        foot_rotation=yaw.to_quaternion()@foot.bone.matrix_local.to_quaternion()
        # Determine each actual sole offset under the intended yaw; grounding
        # uses mesh vertices, so boot changes cannot silently sink the feet.
        rest_head=foot.bone.head_local
        q=yaw.to_quaternion()
        points=[q@(p.obj.matrix_world@v.co-rest_head) for p in result.parts if p.bone=="foot."+side for v in p.obj.data.vertices]
        ankle=Vector((sign*FOOT_X,FOOT_Y,-min(p.z for p in points)))
        hip=upper.head.copy();line=ankle-hip;distance=line.length
        a,b=upper.length,lower.length
        if not abs(a-b)<distance<a+b:raise RuntimeError("Unreachable seated foreman ankle")
        axis=line.normalized();pole=Vector((sign*.31,-.235,.465))-hip
        bend=(pole-axis*pole.dot(axis)).normalized()
        along=(a*a-b*b+distance*distance)/(2*distance)
        knee=hip+axis*along+bend*math.sqrt(max(0,a*a-along*along))
        aim_bone(upper,knee);aim_bone(lower,ankle)
        foot.matrix=Matrix.Translation(ankle)@foot_rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()


def eating_state(seconds):
    lift=chew=0.0;contact=1.3<=seconds<BITE_COMMIT
    if .4<=seconds<1.3:lift=resident.smooth((seconds-.4)/.9)
    elif 1.3<=seconds<=BITE_COMMIT:lift=1.0
    elif BITE_COMMIT<seconds<2.5:lift=1-resident.smooth((seconds-BITE_COMMIT)/(2.5-BITE_COMMIT))
    if BITE_COMMIT<=seconds<3.3:
        envelope=resident.smooth((seconds-BITE_COMMIT)/.18)*(1-resident.smooth((seconds-2.95)/.35))
        chew=envelope*(.5+.5*math.sin((seconds-BITE_COMMIT)*math.tau*2.8))
    return lift,chew,contact


def duration(name):return next(seconds for key,seconds,_ in CLIPS if key==name)


def visible_part(part,name,seconds):
    key=part.obj.name
    if "Thrown" in key:return False
    if not key.startswith("FOOD_ForemanCarrot"):return True
    if name=="CarrotDiscard":return seconds<1.0 and (key.endswith("Stem") or key.endswith("Greens"))
    if name=="CarrotTake":return seconds>=1.2
    if name.startswith("CarrotBite"):
        ordinal=int(name[-1])+(1 if seconds>=BITE_COMMIT else 0)
        return "Bite" not in key or int(key[-1])>=ordinal
    return True


def seated_pose(result,name,phase):
    rig=result.rig;base.reset_pose(rig)
    seconds=phase*duration(name)
    eating,chew,contact=eating_state(seconds) if name.startswith("CarrotBite") else (0.0,0.0,False)
    breath=math.sin(phase*math.tau)**2
    base.apply_pose(rig,{"spine":base.BonePose(rotation_degrees=(3+.65*breath,0,0)),
                         "chest":base.BonePose(rotation_degrees=(-2-.35*breath,0,0)),
                         "neck":base.BonePose(rotation_degrees=(-3,0,0)),
                         "head":base.BonePose(rotation_degrees=(4+1.3*eating,0,0))})
    pelvis=rig.pose.bones["pelvis"];matrix=pelvis.matrix.copy();matrix.translation.z=PELVIS_HEIGHT;pelvis.matrix=matrix
    bpy.context.view_layer.update();solve_legs(result)
    lift=math.sin(math.pi*phase)**2 if name=="SeatedGrumble" else 0
    shake=math.sin(phase*math.tau*4)*lift
    # Left palm rests over the thigh; the right hand rises toward chest height
    # and shakes away from the body while speaking, returning exactly to rest.
    # The wrist rotates the carrot away from the belly while lowered, and
    # points its tapered end back/up at the mouth while raised. Solve onto the
    # actual prop offset, so neither the carrot nor hand teleports at contact.
    rest_hand=rig.data.bones["hand.L"].matrix_local.to_quaternion()
    low_rotation=Vector((0,0,1)).rotation_difference(Vector((.45,-.62,.64)).normalized())
    # Wider bitten ends approach almost horizontally, keeping the whole cut
    # surface in front of the lips rather than pushing a rim into the face.
    bite_axis=(0,.82,.57) if name!="CarrotBite2" and name!="CarrotBite3" else (0,.96,.28) if name=="CarrotBite2" else (0,.995,.10)
    bite_rotation=Vector((0,0,1)).rotation_difference(Vector(bite_axis).normalized())
    delta=low_rotation.slerp(bite_rotation,eating)
    neutral=Vector((.34,-.29,.74))
    left=neutral.copy()
    if name=="CarrotDiscard":
        keys=((0,neutral),(.35,neutral),(.72,Vector((.36,-.28,.87))),
              (1.0,Vector((.61,-.14,.98))),(1.3,Vector((.66,-.05,1.06))),
              (2.1,neutral),(2.4,neutral))
        for (a,p),(b,q) in zip(keys,keys[1:]):
            if a<=seconds<=b:left=p.lerp(q,resident.smooth((seconds-a)/(b-a)));break
    elif name=="CarrotTake":
        spine=rig.pose.bones["spine"]
        mapping=spine.matrix@spine.bone.matrix_local.inverted()
        pocket=mapping@POCKET;up=mapping.to_quaternion()@Vector((0,0,1))
        pocket_rotation=Vector((0,0,1)).rotation_difference(-up)
        if seconds<1.05:
            blend=resident.smooth((seconds-.35)/.70)
            left=neutral.lerp(pocket,blend);delta=low_rotation.slerp(pocket_rotation,blend)
        elif seconds<1.35:left=pocket;delta=pocket_rotation
        elif seconds<1.75:
            left=pocket+up*.27*resident.smooth((seconds-1.35)/.40);delta=pocket_rotation
        elif seconds<2.0:
            left=pocket+up*.27;delta=pocket_rotation.slerp(low_rotation,resident.smooth((seconds-1.75)/.25))
        else:left=(pocket+up*.27).lerp(neutral,resident.smooth((seconds-2.0)/.40))
    hand=rig.pose.bones["hand.L"]
    hand.matrix=Matrix.Translation(hand.head.copy())@(delta@rest_hand).to_matrix().to_4x4()
    bpy.context.view_layer.update()
    head=rig.pose.bones["head"]
    mouth=head.matrix@head.bone.matrix_local.inverted()@BITE_MOUTH
    tip_height=CARROT_TIPS[int(name[-1])-1] if name.startswith("CarrotBite") else CARROT_TIPS[0]
    bite_grip=mouth-delta@Vector((0,0,tip_height))
    if name.startswith("CarrotBite"):left=neutral.lerp(bite_grip,eating)
    # During lifting, a shallow outward bow carries the hand around the round
    # jacket before it approaches the face. It vanishes at both endpoints.
    left.y-=.075*math.sin(math.pi*eating)
    # The shared solver positions its centreline socket; compensate the true
    # palm contact so every existing food/pocket/mouth trajectory stays exact.
    targets={"L":left-delta@(CARROT_GRIP-HAND_GRIP),"R":(-.34-.085*lift-.045*shake,-.235-.13*lift,.72+.31*lift+.034*shake)}
    resident.solve_grips(rig,targets)
    jaw=rig.pose.bones["face.mouth"];matrix=jaw.matrix.copy()
    matrix.translation.z-=.007*chew;matrix.translation.y-=.0015*chew;jaw.matrix=matrix
    bpy.context.view_layer.update()
    return contact


def evaluated_points(result,select=lambda p:True):
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();points=[]
    for part in result.parts:
        if not select(part):continue
        evaluated=part.obj.evaluated_get(deps);mesh=evaluated.to_mesh()
        points.extend(evaluated.matrix_world@v.co for v in mesh.vertices);evaluated.to_mesh_clear()
    return points


def evaluated_bvh(result,select):
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();points=[];faces=[]
    for part in result.parts:
        if not select(part):continue
        evaluated=part.obj.evaluated_get(deps);mesh=evaluated.to_mesh();start=len(points)
        points.extend(evaluated.matrix_world@v.co for v in mesh.vertices)
        faces.extend([start+i for i in face.vertices] for face in mesh.polygons)
        evaluated.to_mesh_clear()
    return BVHTree.FromPolygons(points,faces)


def grip_geometry(result):
    """Measure the real skin/food surfaces, independently of food anchors."""
    def points(name):return evaluated_points(result,lambda p:p.obj.name==name)
    def centre(values):return sum(values,Vector())/len(values)
    palm=points("GEO_Hand.L");thumb=points("GEO_Thumb.L")
    fingers=points("GEO_ForemanGripFingers.L");stem=points("FOOD_ForemanCarrotStem")
    hand=result.rig.pose.bones["hand.L"]
    deformation=hand.matrix.to_3x3()@hand.bone.matrix_local.to_3x3().inverted()
    normal=(deformation@PALM_NORMAL).normalized()
    across=(deformation@Vector((1,0,0))).normalized()
    signed=(centre(stem)-centre(palm)).dot(normal)
    stem_bvh=evaluated_bvh(result,lambda p:p.obj.name=="FOOD_ForemanCarrotStem")
    gap=lambda values:min(stem_bvh.find_nearest(point)[3] for point in values)
    thumb_gap,finger_gap,palm_gap=gap(thumb),gap(fingers),gap(palm)
    opposed=(centre(thumb)-centre(stem)).dot(across)<-.015 and (centre(fingers)-centre(stem)).dot(across)>.004
    if signed<.040 or not opposed or thumb_gap>.006 or finger_gap>.006 or palm_gap>.012:
        raise RuntimeError(f"Foreman carrot lacks a true palm grip: signed={signed}, opposed={opposed}, thumb={thumb_gap}, fingers={finger_gap}, palm={palm_gap}")
    return {"stem_palm_signed_m":round(signed,7),"thumb_stem_gap_m":round(thumb_gap,7),
            "fingers_stem_gap_m":round(finger_gap,7),"palm_stem_gap_m":round(palm_gap,7)}


def make_actions(result):
    rig=result.rig;report=[];tracks=[];max_ground=0;max_seat=0;hand_positions=[];pose_bounds=[]
    max_bite_error=0;min_talking_clearance=10;max_anchor_error=0;mouth_travel=0;contact_samples=0
    max_pickup_error=0;release_error=0;neutral_poses={};release_rotation_error=0
    transfer_mesh_error=0;pocket_body_outside=0;grip_report=grip_geometry(result)
    scene=bpy.context.scene;scene.render.fps=FPS
    for name,seconds,loop in CLIPS:
        action=bpy.data.actions.new(name);action.use_fake_user=True;action.use_frame_range=True
        action.frame_start=0;action.frame_end=seconds*FPS;action.use_cyclic=loop
        rig.animation_data_create().action=action;prior={}
        frames=sorted(set(float(f) for f in range(math.floor(seconds*FPS)+1))|{seconds*FPS})
        for frame in frames:
            phase=frame/(seconds*FPS);at=frame/FPS;contact=seated_pose(result,name,phase)
            for bone in rig.pose.bones:
                if bone.name in prior:bone.rotation_quaternion.make_compatible(prior[bone.name])
                prior[bone.name]=bone.rotation_quaternion.copy()
                for channel in ("location","rotation_quaternion","scale"):bone.keyframe_insert(channel,frame=frame,group=bone.name)
            if frame%6==0 or frame==frames[-1]:
                for side in ("L","R"):
                    feet=evaluated_points(result,lambda p:p.bone=="foot."+side)
                    max_ground=max(max_ground,abs(min(p.z for p in feet)))
                butt=evaluated_points(result,lambda p:p.obj.name=="CLO_ForemanSeat")
                max_seat=max(max_seat,abs(min(p.z for p in butt)-SEAT_TOP))
                if name=="SeatedGrumble":hand_positions.append(tuple(rig.pose.bones["SOCKET_Grip.R"].head))
                pose_bounds.extend(evaluated_points(result,lambda p:visible_part(p,name,at)))
                ordinal=int(name[-1]) if name.startswith("CarrotBite") else 1
                suffix=str(ordinal) if ordinal>1 else ""
                tip=result.anchors["ANCHOR_ForemanCarrotTip"+suffix].matrix_world.translation
                mouth=result.anchors["ANCHOR_ForemanBiteMouth"].matrix_world.translation
                actual_tip=rig.pose.bones["hand.L"].matrix@rig.data.bones["hand.L"].matrix_local.inverted()@(CARROT_GRIP+Vector((0,0,CARROT_TIPS[ordinal-1])))
                max_anchor_error=max(max_anchor_error,(tip-actual_tip).length)
                if contact:
                    max_bite_error=max(max_bite_error,(tip-mouth).length);contact_samples+=1
                if name=="SeatedGrumble":min_talking_clearance=min(min_talking_clearance,(tip-mouth).length)
                head_bvh=evaluated_bvh(result,lambda p:p.bone in ("head","face.mouth"))
                hand_bvh=evaluated_bvh(result,lambda p:p.bone=="hand.L" and not p.obj.name.startswith("FOOD_"))
                if hand_bvh.overlap(head_bvh):raise RuntimeError(f"Foreman left hand enters face during {name} at {frame/FPS:.3f}s")
                has_food=any(p.obj.name.startswith("FOOD_ForemanCarrot") and visible_part(p,name,at) for p in result.parts)
                if has_food:
                    carrot_bvh=evaluated_bvh(result,lambda p:p.obj.name.startswith("FOOD_ForemanCarrot") and visible_part(p,name,at))
                    if carrot_bvh.overlap(head_bvh):raise RuntimeError(f"Foreman carrot cuts through face during {name} at {frame/FPS:.3f}s")
                jaw=rig.pose.bones["face.mouth"]
                rest_jaw=(jaw.parent.matrix@jaw.parent.bone.matrix_local.inverted()@jaw.bone.matrix_local).translation
                jaw_shift=(jaw.matrix.translation-rest_jaw).length
                if not name.startswith("CarrotBite") and jaw_shift>.0001:
                    raise RuntimeError("Foreman chews outside a committed bite")
                mouth_travel=max(mouth_travel,jaw_shift)
                if name=="CarrotTake" and 1.05<=at<=1.35:
                    pickup=result.anchors["ANCHOR_ForemanPocket"].matrix_world.translation
                    max_pickup_error=max(max_pickup_error,(pickup-result.anchors["ANCHOR_ForemanStemGrip"].matrix_world.translation).length)
                    to_pocket=rig.data.bones["spine"].matrix_local@rig.pose.bones["spine"].matrix.inverted()
                    for point in evaluated_points(result,lambda p:p.obj.name.startswith("FOOD_ForemanCarrotBite")):
                        p=to_pocket@point
                        pocket_body_outside=max(pocket_body_outside,.089-p.x,p.x-.271,-.3525-p.y,p.y+.267,.907-p.z,p.z-1.14)
                if name=="CarrotDiscard" and abs(at-1.0)<.001:
                    release=result.anchors["ANCHOR_ForemanThrowRelease"].matrix_world
                    grip=result.anchors["ANCHOR_ForemanStemGrip"].matrix_world
                    release_error=(release.translation-grip.translation).length
                    release_rotation_error=release.to_quaternion().rotation_difference(grip.to_quaternion()).angle
                    for held,thrown in (("FOOD_ForemanCarrotStem","FOOD_ForemanThrownStem"),("FOOD_ForemanCarrotGreens","FOOD_ForemanThrownGreens")):
                        held_points=evaluated_points(result,lambda p:p.obj.name==held)
                        thrown_points=evaluated_points(result,lambda p:p.obj.name==thrown)
                        transfer_mesh_error=max(transfer_mesh_error,max((a-release@b).length for a,b in zip(held_points,thrown_points)))
            if frame in (0.0,frames[-1]):
                neutral_poses[(name,frame==0.0)]={b.name:b.matrix.copy() for b in rig.pose.bones}
        for curve in base.iter_action_fcurves(action):
            for key in curve.keyframe_points:key.interpolation="LINEAR"
            tracks.append((name,curve.data_path,curve.array_index,[[round(k.co.x,7),round(k.co.y,7)] for k in curve.keyframe_points]))
        rig.animation_data.action=None
        report.append({"name":name,"duration_seconds":seconds,"loop":loop})
    base.reset_pose(rig)
    if max_ground>.001 or max_seat>.003:raise RuntimeError(f"Foreman feet/seat contact failed: {max_ground}/{max_seat}")
    if max_bite_error>.002 or max_anchor_error>.001 or contact_samples<3 or min_talking_clearance<.20 or mouth_travel<.005:
        raise RuntimeError(f"Foreman carrot contact failed: mouth={max_bite_error}, anchor={max_anchor_error}, samples={contact_samples}, speech={min_talking_clearance}")
    neutral=neutral_poses[("SeatedIdle",True)];neutral_error=0
    for matrices in neutral_poses.values():
        neutral_error=max(neutral_error,max(abs(matrices[name][i][j]-neutral[name][i][j]) for name in neutral for i in range(4) for j in range(4)))
    if max_pickup_error>.002 or release_error>.002 or release_rotation_error>.001 or neutral_error>.002 or transfer_mesh_error>.002 or pocket_body_outside>.002:
        raise RuntimeError(f"Foreman prop transfer endpoints differ: pickup={max_pickup_error}, release={release_error}, rotation={release_rotation_error}, neutral={neutral_error}, mesh={transfer_mesh_error}, pocket={pocket_body_outside}")
    thrown_radius=max(p.length for p in evaluated_points(result,lambda p:"Thrown" in p.obj.name))
    if thrown_radius>.115 or BIN_TARGET.z+thrown_radius>=.345 or BIN_TARGET.z-thrown_radius<=.035:
        raise RuntimeError("Foreman discarded stem does not fit below the pail rim")
    reach=max(p[2] for p in hand_positions)-min(p[2] for p in hand_positions)
    if reach<.27:raise RuntimeError("Foreman grumble lacks its visible raised-hand gesture")
    seated_pose(result,"SeatedIdle",0)
    feet={}
    for side in ("L","R"):
        points=evaluated_points(result,lambda p:p.bone=="foot."+side)
        feet[side]={"bounds_min":[round(min(p[i] for p in points),6) for i in range(3)],
                    "bounds_max":[round(max(p[i] for p in points),6) for i in range(3)]}
    base.reset_pose(rig)
    measured={"max_sole_error_m":round(max_ground,7),"max_seat_error_m":round(max_seat,7),
              "right_hand_height_range_m":round(reach,7),
              "carrot_mouth_max_error_m":round(max_bite_error,7),
              "carrot_anchor_max_error_m":round(max_anchor_error,7),
              "carrot_speech_min_distance_m":round(min_talking_clearance,7),
              "chewing_lower_lip_travel_m":round(mouth_travel,7),
              "pocket_pickup_error_m":round(max_pickup_error,7),"throw_release_error_m":round(release_error,7),
              "throw_release_rotation_error_rad":round(release_rotation_error,7),"neutral_endpoint_error":round(neutral_error,7),
              "throw_mesh_transfer_error_m":round(transfer_mesh_error,7),"pocket_body_outside_m":round(pocket_body_outside,7),
              "thrown_stem_radius_m":round(thrown_radius,7),
              "palm_grip":grip_report,
              "seated_foot_bounds":feet,
              "posed_bounds_min":[round(min(p[i] for p in pose_bounds),6) for i in range(3)],
              "posed_bounds_max":[round(max(p[i] for p in pose_bounds),6) for i in range(3)],
              "curve_signature":hashlib.sha256(json.dumps(tracks,separators=(",",":")).encode()).hexdigest()}
    return {"generator":Path(__file__).name,"version":VERSION,"anatomy_standard":"NpcHumanV2",
            "bone_count":len(rig.data.bones),"fps":FPS,"root_motion":False,"clips":report,
            "bite_contract":{"raise":.4,"contact":1.3,"commit":BITE_COMMIT,"lowered":2.5,"chew_end":3.3},
            "discard_contract":{"release":1.0,"inside_bin":1.65,"duration":2.4},
            "take_contract":{"pickup":1.2,"withdrawn":2.0,"duration":2.4},
            "carrot_tip_anchor":"ANCHOR_ForemanCarrotTip","mouth_contact_anchor":"ANCHOR_ForemanBiteMouth","validation":measured}


def manifest(result,texture_hash):
    metrics=resident.measured(result)
    if len(result.rig.data.bones)!=31:raise RuntimeError("Foreman lost the canonical31 bones")
    coat=next(p for p in result.parts if p.obj.name=="CLO_ForemanBarrelCoat")
    vertices=[coat.obj.matrix_world@v.co for v in coat.obj.data.vertices]
    width=max(v.x for v in vertices)-min(v.x for v in vertices)
    depth=max(v.y for v in vertices)-min(v.y for v in vertices)
    if width<.70 or depth<.54:raise RuntimeError("Foreman lost the stout belly silhouette")
    return {"generator":Path(__file__).name,"version":VERSION,"role":"PortForeman","anatomy_standard":"NpcHumanV2",
            "coordinate_system":"Blender metres +Z up / -Y facing; import swaps YZ, placement resolves facing",
            "height_scale":1.0,**metrics,"seat_top_m":SEAT_TOP,"coat_width_m":round(width,6),"coat_depth_m":round(depth,6),
            "anchor_seat_unity":[0,SEAT_TOP,.05],"atlas_sha256":texture_hash,
            "carrot_length_m":.238,"carrot_hand":"hand.L","carrot_tip_heights_from_grip_m":list(CARROT_TIPS),
            "palm_normal_source":list(PALM_NORMAL),"carrot_grip_offset_source":list(CARROT_GRIP-HAND_GRIP),
            "carrot_visible_parts":["FOOD_ForemanCarrotBite1","FOOD_ForemanCarrotBite2","FOOD_ForemanCarrotBite3","FOOD_ForemanCarrotStem","FOOD_ForemanCarrotGreens"],
            "thrown_stem_host":"MOVE_ForemanThrownStem","throw_release_anchor":"ANCHOR_ForemanThrowRelease",
            "pocket_anchor":"ANCHOR_ForemanPocket","bin_target_anchor":"ANCHOR_ForemanBinTarget",
            "bin_rim_height_m":.359,"bin_inside_target_source":list(BIN_TARGET),
            "parts":[{"name":p.obj.name,"bone":p.bone,"color":list(p.color)} for p in result.parts],
            "geometry_signature":resident.geometry_signature(result)}


def preview(result,path,texture,clip="SeatedIdle",seconds=0,grip_view=None):
    for obj in list(bpy.data.objects):
        if obj.name.startswith(("ForemanReviewCamera","ForemanReviewLight")):
            bpy.data.objects.remove(obj,do_unlink=True)
    mat=result.material;nodes=mat.node_tree.nodes;links=mat.node_tree.links
    shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    info=nodes.new("ShaderNodeObjectInfo");image=nodes.new("ShaderNodeTexImage")
    image.image=bpy.data.images.load(str(texture),check_existing=True);image.interpolation="Closest"
    image.image.pack()
    mix=nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY";mix.inputs[0].default_value=1
    links.new(info.outputs["Color"],mix.inputs[1]);links.new(image.outputs["Color"],mix.inputs[2]);links.new(mix.outputs[0],shader.inputs["Base Color"])
    shader.inputs["Roughness"].default_value=.85
    seated_pose(result,clip,seconds/duration(clip))
    for part in result.parts:part.obj.hide_render=not visible_part(part,clip,seconds)
    thrown=result.pivots["MOVE_ForemanThrownStem"];thrown.matrix_world=Matrix.Identity(4)
    if clip=="CarrotDiscard" and 1.0<=seconds<1.65:
        release=result.anchors["ANCHOR_ForemanThrowRelease"].matrix_world
        fraction=(seconds-1.0)/.65
        position=release.translation.lerp(BIN_TARGET,fraction)+Vector((0,0,.22*4*fraction*(1-fraction)))
        thrown.matrix_world=Matrix.Translation(position)@Matrix.Rotation(math.radians(150)*fraction,4,"X")@release.to_quaternion().to_matrix().to_4x4()
        for part in result.parts:
            if "Thrown" in part.obj.name:part.obj.hide_render=False
    scene=bpy.context.scene;scene.render.engine="CYCLES";scene.cycles.samples=16
    scene.render.resolution_x=720;scene.render.resolution_y=820;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("ForemanReviewWorld");scene.world.color=(.20,.23,.22)
    for i,(position,power,size) in enumerate((((-3,-4,4),700,4),((3,-1,3),450,3),((0,3,4),600,3))):
        data=bpy.data.lights.new("ForemanReviewLight"+str(i),"AREA");data.energy=power;data.size=size
        light=bpy.data.objects.new(data.name,data);scene.collection.objects.link(light);light.location=position
        light.rotation_euler=(Vector((0,0,.70))-light.location).to_track_quat("-Z","Y").to_euler()
    camera_data=bpy.data.cameras.new("ForemanReviewCamera");camera=bpy.data.objects.new(camera_data.name,camera_data)
    scene.collection.objects.link(camera);camera.location=(2.0,-4.2,1.90)
    camera.rotation_euler=(Vector((0,-.06,.70))-camera.location).to_track_quat("-Z","Y").to_euler()
    camera_data.type="ORTHO";camera_data.ortho_scale=2.02;scene.camera=camera
    if grip_view is not None:
        hand=result.rig.pose.bones["hand.L"]
        deformation=hand.matrix.to_3x3()@hand.bone.matrix_local.to_3x3().inverted()
        contact=result.anchors["ANCHOR_ForemanStemGrip"].matrix_world.translation
        normal=(deformation@PALM_NORMAL).normalized()
        side=(deformation@Vector((1,0,0))).normalized()
        up=(deformation@Vector((0,0,1))).normalized()
        target=contact+up*.05
        camera.location=target+normal*(1 if grip_view=="palm" else .30)+side*(.22 if grip_view=="palm" else 1)+up*.22
        camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
        camera_data.ortho_scale=.48
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
    thrown.matrix_world=Matrix.Identity(4)
    for part in result.parts:part.obj.hide_render=not visible_part(part,"SeatedIdle",0)
    base.reset_pose(result.rig)


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/City/Port/Foreman")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/City/Port/Foreman")
    parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    texture=args.model_dir/"PortForemanAtlas.png"
    texture_hash=atlas(texture,args.validate_only)
    builder=ForemanBuilder();result=builder.build();body=manifest(result,texture_hash)
    if not args.validate_only:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        base.export_fbx(args.model_dir/"PortForeman.fbx",result)
    actions=make_actions(result)
    if args.validate_only:
        for stem,payload in (("PortForeman",body),("PortForemanActions",actions)):
            if json.loads((args.model_dir/(stem+".json")).read_text(encoding="utf-8"))!=payload:
                raise RuntimeError("Foreman deterministic manifest differs: "+stem)
    else:
        for stem,payload in (("PortForeman",body),("PortForemanActions",actions)):
            (args.model_dir/(stem+".json")).write_text(json.dumps(payload,indent=2)+"\n",encoding="utf-8")
        base.export_animation_fbx(args.model_dir/"PortForemanActions.fbx",result)
        if not args.no_preview:
            preview(result,args.source_dir/"PortForeman.png",texture)
            preview(result,args.source_dir/"PortForemanEating.png",texture,"CarrotBite1",1.45)
            preview(result,args.source_dir/"PortForemanThirdBite.png",texture,"CarrotBite3",1.45)
            preview(result,args.source_dir/"PortForemanDiscard.png",texture,"CarrotDiscard",1.3)
            preview(result,args.source_dir/"PortForemanTake.png",texture,"CarrotTake",1.75)
            preview(result,args.source_dir/"PortForemanGrumble.png",texture,"SeatedGrumble",1.76)
            preview(result,args.source_dir/"PortForemanGrip.png",texture,grip_view="palm")
            preview(result,args.source_dir/"PortForemanGripSide.png",texture,grip_view="side")
        bpy.context.preferences.filepaths.save_version=0
        seated_pose(result,"SeatedIdle",0)
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"PortForeman.blend"),check_existing=False)
    result=ForemanBuilder().build();repeat=manifest(result,texture_hash);repeat_actions=make_actions(result)
    if body!=repeat or actions!=repeat_actions:raise RuntimeError("Foreman deterministic rebuild differs")
    print("PORT FOREMAN ART CONTRACT OK: canonical31 bones, stool/soles, three carrot contacts, clear face, pocket pickup, matched stem throw, raised-hand grumble",flush=True)
    print(json.dumps({"body":{k:body[k] for k in ("mesh_count","triangle_count","bounds_min","bounds_max","seat_top_m","coat_width_m","coat_depth_m")},"actions":actions["validation"]}),flush=True)


if __name__=="__main__":main()
