#!/usr/bin/env python3
"""Child-native City fair character, three constructed outfits and quiet toy kit.

Unity metres: ground Y=0, forward +Z, anatomical left -X. Source swaps Y/Z.
The skeleton is authored at a child's proportions; no adult mesh is rescaled.
"""
from __future__ import annotations

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

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
import interior_kit as kit
import atlas_kit

_fair_spec = importlib.util.spec_from_file_location("fair_geometry", ROOT / "tools/build-city-fair-3d-model.py")
fair = importlib.util.module_from_spec(_fair_spec)
_fair_spec.loader.exec_module(fair)

VERSION = "1.0.1"
HEIGHT = 1.30
MODEL_DIR = ROOT / "Assets/Resources/City/FairChild"
SOURCE_DIR = ROOT / "ArtSource/City/FairChild"
OUTFITS = ("Raincoat", "Quilted", "Vest")


def source(p):
    return (p[0],p[2],p[1])


def bone_specs():
    """Stable reusable child rig contract; head/tail values are UNITY metres."""
    specs=[]
    def add(name,head,tail,parent=None,deform=True):
        specs.append(dict(name=name,head=tuple(head),tail=tuple(tail),parent=parent,deform=deform))
    add("root",(0,0,0),(0,.08,0),None,False)
    add("pelvis",(0,.63,0),(0,.77,0),"root")
    add("spine",(0,.77,0),(0,.91,0),"pelvis")
    add("chest",(0,.91,0),(0,1.03,.003),"spine")
    add("neck",(0,1.065,.005),(0,1.135,.004),"chest")
    add("head",(0,1.135,.004),(0,1.29,.004),"neck")
    for side,sign in (("L",-1), ("R",1)):
        shoulder=(sign*.151,.994,0); elbow=(sign*.235,.844,.006); wrist=(sign*.281,.703,.028)
        add("clavicle."+side,(0,.99,0),shoulder,"chest")
        add("upper_arm."+side,shoulder,elbow,"clavicle."+side)
        add("forearm."+side,elbow,wrist,"upper_arm."+side)
        add("hand."+side,wrist,(sign*.307,.639,.033),"forearm."+side)
        add("thigh."+side,(sign*.082,.615,0),(sign*.083,.338,.024),"pelvis")
        add("shin."+side,(sign*.083,.338,.024),(sign*.079,.065,0),"thigh."+side)
        add("foot."+side,(sign*.079,.065,0),(sign*.079,.036,.132),"shin."+side)
        add("SOCKET_Grip."+side,(sign*.300,.671,.044),(sign*.300,.671,.070),"hand."+side,False)
        direction=Vector((sign*.23,-.972,.045)).normalized()
        across=Vector((.972,sign*.23,0)).normalized()
        for index,(finger,length) in enumerate((("index",.040),("middle",.044),("ring",.041),("pinky",.032))):
            start=Vector((sign*.297,.663,.028))+across*(sign*(index-1.5)*.012)
            joint=start+direction*length*.56; tip=start+direction*length
            add(f"finger.{finger}.01.{side}",start,joint,"hand."+side)
            add(f"finger.{finger}.02.{side}",joint,tip,f"finger.{finger}.01.{side}")
        thumb=Vector((sign*.277,.684,.030)); joint=Vector((sign*.265,.670,.037));tip=Vector((sign*.257,.655,.040))
        add(f"finger.thumb.01.{side}",thumb,joint,"hand."+side)
        add(f"finger.thumb.02.{side}",joint,tip,f"finger.thumb.01.{side}")
    add("SOCKET_Mouth",(0,1.120,.095),(0,1.120,.118),"head",False)
    add("Hair.Fringe",(.025,1.264,.073),(.025,1.238,.080),"head")
    add("Cloth.Hem.L",(-.11,.655,0),(-.11,.585,.020),"pelvis")
    add("Cloth.Hem.R",(.11,.655,0),(.11,.585,.020),"pelvis")
    add("Cloth.Hood",(0,1.005,-.071),(0,.925,-.071),"chest")
    return specs


def create_rig():
    """Create only the reusable animation rig. Does not reset the caller's scene."""
    root=bpy.data.objects.new("FairChild",None);bpy.context.collection.objects.link(root)
    data=bpy.data.armatures.new("RIG_FairChild_Data")
    rig=bpy.data.objects.new("RIG_FairChild",data);bpy.context.collection.objects.link(rig)
    rig.parent=root;rig.show_in_front=True;rig.display_type="WIRE"
    bpy.context.view_layer.objects.active=rig;rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for spec in bone_specs():
        bone=data.edit_bones.new(spec["name"]);bone.head=source(spec["head"]);bone.tail=source(spec["tail"])
        bone.use_deform=spec["deform"]
    for spec in bone_specs():
        if spec["parent"]:data.edit_bones[spec["name"]].parent=data.edit_bones[spec["parent"]]
    bpy.ops.object.mode_set(mode="OBJECT");rig.select_set(False)
    for bone in rig.pose.bones: bone.rotation_mode="QUATERNION"
    return root,rig


def export_actions(path,root,rig):
    bpy.ops.object.select_all(action="DESELECT");root.select_set(True);rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"EMPTY","ARMATURE"},
        axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_space_transform=False,
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_simplify_factor=0,
        primary_bone_axis="Y",secondary_bone_axis="X")


def loft(profile,sides=20):
    """Closed child/garment surface from (height,halfwidth,halfdepth,forward)."""
    verts=[(math.cos(i*math.tau/sides)*rx,y,z+math.sin(i*math.tau/sides)*rz)
           for y,rx,rz,z in profile for i in range(sides)]
    faces=[tuple(range(sides)),tuple(reversed(range((len(profile)-1)*sides,len(profile)*sides)))]
    for row in range(len(profile)-1):
        for i in range(sides):
            j=(i+1)%sides;a=row*sides+i;b=row*sides+j
            faces.append((b,a,a+sides,b+sides))
    return verts,faces


def segment(start,end,width,depth,sides=12,rings=7):
    a,b=Vector(start),Vector(end);direction=(b-a).normalized()
    across=direction.cross(Vector((0,0,1))).normalized()
    if across.length<.1:across=Vector((1,0,0))
    normal=direction.cross(across).normalized()
    verts=[]
    for row in range(rings):
        t=row/(rings-1);p=a.lerp(b,t)
        curve=.80+.20*math.sin(t*math.pi)
        for i in range(sides):
            angle=i*math.tau/sides
            verts.append(tuple(p+across*(math.cos(angle)*width*.5*curve)+normal*(math.sin(angle)*depth*.5*curve)))
    faces=[tuple(reversed(range(sides))),tuple(range((rings-1)*sides,rings*sides))]
    for row in range(rings-1):
        for i in range(sides):
            a=row*sides+i;b=row*sides+(i+1)%sides;faces.append((a,b,b+sides,a+sides))
    return verts,faces


def moved(geom,p):return kit.translated(geom,p)


def limb_surface(nodes,widths,depths,sides=14,steps=7):
    """One closed garment surface across a bent joint, with no separate caps."""
    centers=[];radii=[]
    for section in range(len(nodes)-1):
        for row in range(steps+(section==len(nodes)-2)):
            t=row/steps
            centers.append(Vector(nodes[section]).lerp(Vector(nodes[section+1]),t))
            radii.append((widths[section]*(1-t)+widths[section+1]*t,
                          depths[section]*(1-t)+depths[section+1]*t))
    vertices=[]
    for row,(center,(width,depth)) in enumerate(zip(centers,radii)):
        direction=(centers[min(row+1,len(centers)-1)]-centers[max(0,row-1)]).normalized()
        across=direction.cross(Vector((0,0,1))).normalized();normal=direction.cross(across).normalized()
        for i in range(sides):
            angle=i*math.tau/sides
            vertices.append(tuple(center+across*(math.cos(angle)*width*.5)+normal*(math.sin(angle)*depth*.5)))
    faces=[tuple(reversed(range(sides))),tuple(range((len(centers)-1)*sides,len(centers)*sides))]
    for row in range(len(centers)-1):
        for i in range(sides):
            a=row*sides+i;b=row*sides+(i+1)%sides;faces.append((a,b,b+sides,a+sides))
    return vertices,faces


class Part:
    def __init__(self,name,texture,outfit=None,visible=False):
        self.name,self.texture,self.outfit,self.visible=name,texture,outfit,visible
        self.components=[]

    def add(self,geometry,bone,tile=0,uv=None):
        volume=bp.signed_volume(geometry)
        if volume<=1e-12:raise RuntimeError(f"Inward child solid {self.name}: {volume}")
        self.components.append((geometry,bone,tile,uv))
        return self

    def box(self,p,size,bone,tile=0):return self.add(bp.u_box(p,size,min(.005,min(size)*.20)),bone,tile)
    def ball(self,p,size,bone,tile=0,sides=14,rows=8):return self.add(fair.ellipsoid(p,size,sides,rows),bone,tile)
    def rod(self,a,b,r,bone,tile=0,sides=8):return self.add(fair.rod(a,b,r,sides),bone,tile)


HEAD_PROFILE=[(1.057,.031,.029,.010),(1.068,.048,.042,.010),(1.080,.061,.057,.008),
    (1.094,.074,.068,.006),(1.110,.083,.076,.004),(1.130,.089,.082,.003),
    (1.152,.094,.087,.001),(1.174,.095,.090,0),(1.196,.094,.089,-.002),
    (1.217,.090,.085,-.003),(1.238,.083,.078,-.005),(1.256,.072,.067,-.006),
    (1.272,.057,.051,-.007),(1.285,.036,.031,-.007),(1.292,.006,.006,-.007)]


def head_section(y):
    for a,b in zip(HEAD_PROFILE,HEAD_PROFILE[1:]):
        if a[0]<=y<=b[0]:
            t=(y-a[0])/(b[0]-a[0]);return tuple(a[i]*(1-t)+b[i]*t for i in (1,2,3))
    return HEAD_PROFILE[-1][1:]


def face_surface():
    cols,rows=12,10;front=[];uv=[]
    for row in range(rows+1):
        t=row/rows;y=1.080+t*.186;rx,rz,zc=head_section(y)
        for col in range(cols+1):
            s=col/cols;x=(s*2-1)*rx*.83
            z=zc+rz*math.sqrt(max(.01,1-(x/rx)**2))+.0015
            front.append((x,y,z));uv.append((s,t))
    count=len(front);verts=front+[(x,y,z-.001) for x,y,z in front];uv=uv+uv
    faces=[]
    for row in range(rows):
        for col in range(cols):
            a=row*(cols+1)+col;b=a+1;c=b+cols+1;d=a+cols+1
            faces.extend(((a,b,c,d),(d+count,c+count,b+count,a+count)))
    edge=list(range(cols+1))+[row*(cols+1)+cols for row in range(1,rows+1)]+list(range(rows*(cols+1)+cols-1,rows*(cols+1)-1,-1))+[row*(cols+1) for row in range(rows-1,0,-1)]
    for a,b in zip(edge,edge[1:]+edge[:1]):faces.append((b,a,a+count,b+count))
    return (verts,faces),uv


def make_body():
    parts=[]
    def part(name,texture="ChildSkin",visible=False):
        p=Part(name,texture,visible=visible);parts.append(p);return p
    part("GEO_Head",visible=True).add(loft(HEAD_PROFILE,20),"head")
    surface,uv=face_surface();part("GEO_Face","ChildFace",True).add(surface,"head",uv=uv)
    ears=part("GEO_Ears",visible=True)
    for sign in (-1,1):
        ears.ball((sign*.097,1.173,.002),(.032,.060,.030),"head",1,12,7)
        ears.ball((sign*.107,1.174,.010),(.018,.036,.020),"head",2,10,6)
        ears.ball((sign*.098,1.151,.014),(.020,.019,.020),"head",1,8,5)
    # The painted face rests on a rounded child's cranium; the nose has a bridge,
    # soft tip and two nostril wings rather than an adult's long wedge.
    nose=part("GEO_Neck",visible=True)
    nose.add(loft([(1.015,.031,.028,.002),(1.037,.029,.027,.003),
                  (1.062,.028,.027,.004),(1.086,.033,.031,.003)],16),"neck")
    # Nose is grouped into the visible head, not the neck, so nods remain honest.
    head=parts[0]
    head.ball((0,1.168,.086),(.025,.045,.022),"head",0,10,6)
    head.ball((0,1.151,.095),(.027,.020,.025),"head",0,10,6)
    for sign in (-1,1):head.ball((sign*.013,1.149,.092),(.013,.015,.017),"head",2,8,5)
    hair=part("GEO_Hair","ChildHair",True)
    hair.add(loft([(1.239,.086,.081,-.007),(1.257,.076,.071,-.007),(1.274,.060,.055,-.007),
                   (1.288,.041,.037,-.007),(1.300,.009,.008,-.007)],20),"head")
    for i in range(9):
        angle=math.tau*i/9;x=math.cos(angle)*.077;z=math.sin(angle)*.072-.009
        # Short side and nape locks, clear of the child's eyes and ears.
        y=1.219 if z<.04 else 1.243
        tuft=fair.ellipsoid((0,0,0),(.040,.054,.025),8,5)
        hair.add(moved(bp.u_rotated(tuft,(0,-math.degrees(angle),(-1 if x<0 else 1)*12)),(x,y,z)),"Hair.Fringe" if z>.04 else "head")
    torso=part("GEO_Torso")
    torso.add(loft([(.66,.097,.068,0),(.72,.093,.064,0),(.79,.097,.062,0),
                   (.86,.111,.063,0),(.92,.127,.060,0),(.99,.126,.052,0),(1.02,.071,.038,0)],20),"torso")
    part("GEO_Pelvis").add(loft([(.565,.087,.064,0),(.595,.109,.069,0),(.65,.109,.070,0),(.69,.097,.066,0)],20),"pelvis")
    specs={s["name"]:s for s in bone_specs()}
    for side,sign in (("L",-1),("R",1)):
        arm=part("GEO_Arm."+side)
        arm.add(segment(specs["upper_arm."+side]["head"],specs["upper_arm."+side]["tail"],.071,.065,14,9),"upper_arm."+side)
        arm.add(segment(specs["forearm."+side]["head"],specs["forearm."+side]["tail"],.055,.050,14,9),"forearm."+side)
        hand=part("GEO_Hand."+side,visible=True)
        palm=segment((sign*.283,.700,.029),(sign*.299,.660,.030),.049,.022,12,6)
        hand.add(palm,"hand."+side)
        hand.ball((sign*.281,.692,.033),(.023,.033,.024),"hand."+side,1,10,6)
        for finger in ("index","middle","ring","pinky","thumb"):
            for number in (1,2):
                name=f"finger.{finger}.{number:02d}.{side}";s=specs[name]
                width=.009 if finger!="pinky" else .0077
                hand.add(segment(s["head"],s["tail"],width,width*.90,8,4),name,1)
                if number==2:
                    tip=Vector(s["tail"]);base=Vector(s["head"]);p=base.lerp(tip,.67)+Vector((0,0,.004))
                    nail=fair.ellipsoid(tuple(p),(.006,.010,.0017),6,3)
                    hand.add(nail,name,3)
        leg=part("GEO_Leg."+side)
        leg.add(segment(specs["thigh."+side]["head"],specs["thigh."+side]["tail"],.110,.102,16,9),"thigh."+side)
        leg.add(segment(specs["shin."+side]["head"],specs["shin."+side]["tail"],.075,.072,16,9),"shin."+side)
        part("GEO_Foot."+side).ball((sign*.079,.047,.055),(.082,.080,.18),"foot."+side,0,16,9)
    return parts


def torus_y(center,major,minor,sides=20,ring_sides=8,scale=(1,1,1)):
    verts=[]
    for i in range(sides):
        a=i*math.tau/sides
        for j in range(ring_sides):
            b=j*math.tau/ring_sides;r=major+minor*math.cos(b)
            verts.append((center[0]+math.cos(a)*r*scale[0],center[1]+minor*math.sin(b)*scale[1],center[2]+math.sin(a)*r*scale[2]))
    faces=[]
    for i in range(sides):
        for j in range(ring_sides):
            a=i*ring_sides+j;b=((i+1)%sides)*ring_sides+j;c=((i+1)%sides)*ring_sides+(j+1)%ring_sides;d=i*ring_sides+(j+1)%ring_sides
            faces.append((d,c,b,a))
    return verts,faces


def make_outfit(outfit):
    parts=[];specs={s["name"]:s for s in bone_specs()}
    def part(role):
        p=Part("OUTFIT_"+outfit+"_"+role,outfit+"Atlas",outfit);parts.append(p);return p
    rain=outfit=="Raincoat";quilted=outfit=="Quilted"
    hem=.565 if rain else .695;coat=part("Body")
    profile=[(hem,.122,.075,-.003),(hem+.020,.128,.079,-.004),(.72,.121,.078,-.001),
        (.79,.112,.075,0),(.86,.123,.075,0),(.92,.141,.072,0),(.982,.145,.067,0),
        (1.019,.092,.049,0),(1.028,.048,.034,0)]
    profile=sorted(set(profile))
    coat.add(loft(profile,22),"raincoat" if rain else "torso",0)
    # Distinct constructed closures, not a colour variant of a single jacket.
    placket=part("Closure")
    placket.box((0,(hem+1.005)*.5,.081),(.022,1.005-hem,.012),"torso",1)
    for i in range(5 if rain else 4):
        y=.73+i*.054
        placket.add(moved(bp.u_rotated(fair.ring([(-.003,.005),(.003,.007),(.004,.005)],8),(90,0,0)),(0,y,.091)),"torso",7)
    collar=part("Collar")
    collar.add(torus_y((0,1.032,.003),.035,.010,18,6,(1.15,1.4,1)),"neck",2)
    pockets=part("Pockets")
    for sign in (-1,1):
        pockets.box((sign*.077,.760,.075),(.067,.083,.016),"torso",3)
        pockets.box((sign*.077,.804,.085),(.074,.023,.013),"torso",3)
        pockets.rod((sign*.11,.724,.088),(sign*.046,.724,.088),.0025,"torso",1)
        for y in (.738,.786):pockets.rod((sign*.107,y,.088),(sign*.107,y+.01,.088),.0018,"torso",1)
    if rain:
        hood=part("FoldedHood")
        # A visibly hollow hood folded behind the neck, with a doubled opening.
        hood.add(moved(loft([(-.080,.055,.029,0),(-.045,.071,.043,0),(0,.064,.039,0),
                       (.023,.047,.025,0),(.022,.036,.020,0),(-.038,.046,.020,0)],18),(0,1.005,-.071)),"Cloth.Hood",2)
        hood.add(torus_y((0,1.001,-.091),.054,.006,18,6,(1.02,.6,.54)),"Cloth.Hood",2)
    elif quilted:
        cap=part("KnittedCap")
        cap.add(loft([(1.227,.093,.089,-.006),(1.253,.091,.088,-.006),(1.278,.070,.066,-.006),
                      (1.294,.040,.035,-.006),(1.300,.008,.008,-.006)],20),"head",6)
        cap.add(torus_y((0,1.236,-.006),.088,.010,20,6,(1,.65,.96)),"head",6)
        # Soft horizontal channels are actual slight ridges over the quilted shell.
        for y,rx,rz in ((.744,.124,.078),(.861,.128,.076),(.921,.143,.073)):
            coat.add(torus_y((0,y,0),rx,.0027,16,6,(1,1,rz/rx)),"torso",1)
    else:
        vest=part("Vest")
        vest.add(loft([(.708,.124,.078,0),(.740,.126,.078,0),(.805,.119,.078,0),(.866,.130,.078,0),
                      (.935,.144,.074,0),(.985,.144,.068,0),(1.013,.079,.050,0)],22),"torso",6)
        vest.box((0,.835,.086),(.024,.285,.017),"torso",7)
        for sign in (-1,1):vest.box((sign*.079,.751,.084),(.069,.081,.011),"torso",6)
    # Real trousers continue to the child's waist beneath every short upper
    # garment. This pelvis remains dressed when a thigh folds up to sit.
    waist=part("TrouserWaist")
    waist_profile=[(.565,.111,.064,0),(.597,.116,.068,0),(.665,.110,.070,0),
                   (.727,.104,.068,0),(.739,.103,.068,0)] if rain else [
                   (.565,.117,.064,0),(.597,.143,.081,0),(.665,.128,.078,0),
                   (.727,.115,.074,0),(.739,.116,.075,0)]
    waist.add(loft(waist_profile,16),"pelvis",4)
    for side,sign in (("L",-1),("R",1)):
        sleeve=part("Sleeve."+side)
        upper=specs["upper_arm."+side];fore=specs["forearm."+side]
        sleeve.add(limb_surface([(sign*.125,1.000,0),upper["tail"],fore["tail"]],
                     [.110,.094,.075] if rain else [.103,.085,.065],
                     [.093,.084,.068] if rain else [.090,.077,.059]),"sleeve."+side,2)
        cuff=part("Cuff."+side)
        wrist=Vector(fore["tail"]);end=wrist+(Vector(fore["head"])-wrist).normalized()*.027
        cuff.add(segment(wrist,end,.079 if rain else .067,.068 if rain else .060,14,4),"forearm."+side,1)
        # A seam and small elbow folds follow their own limb, not the torso.
        sleeve.rod((sign*.184,.947,.043),(sign*.224,.869,.049),.0026,"upper_arm."+side,1)
        for i in range(3):
            p=Vector(fore["head"]).lerp(wrist,.15+i*.11)
            sleeve.add(moved(bp.u_rotated(bp.u_plate((0,0,0),(.060,.010,.008)),(0,0,sign*20)),tuple(p+Vector((0,0,.039)))),"forearm."+side,1)
        pants=part("Trousers."+side)
        hip=Vector(specs["thigh."+side]["head"]);knee=Vector(specs["shin."+side]["head"]);ankle=Vector(specs["shin."+side]["tail"])
        pants.add(limb_surface([(sign*.066 if rain else hip.x,.705,0),knee,ankle],
                       [.096 if rain else .128,.110,.088],[.096 if rain else .116,.105,.080]),"trouser."+side,4)
        for i in range(3):
            p=knee+Vector((0,.032-i*.025,.051))
            pants.add(moved(bp.u_rotated(bp.u_plate((0,0,0),(.071,.012,.006)),(0,0,(-1 if i%2 else 1)*9)),tuple(p)),"shin."+side,4)
        if not rain:
            pants.rod((sign*.136,.573,.012),(sign*.132,.379,.030),.0025,"thigh."+side,1)
        boots=part("Boots."+side)
        x=sign*.079
        boots.add(moved(loft([(.016,.050,.100,.040),(.043,.055,.103,.040),(.066,.049,.098,.043),
                      (.080,.043,.076,.025),(.110,.041,.041,-.002),(.16 if rain else .119,.043,.040,-.004),
                      (.20 if rain else .140,.043,.040,-.004)],14),(x,0,0)),"foot."+side,5)
        boots.add(moved(loft([(0,.051,.102,.039),(.012,.056,.107,.039),(.025,.056,.107,.039),(.030,.051,.102,.039)],14),(x,0,0)),"foot."+side,7)
        for i in range(5):
            boots.box((x,.007,-.038+i*.034),(.093,.013,.020),"foot."+side,7)
        if rain:
            boots.add(torus_y((x,.189,-.004),.040,.004,20,6,(1,1,1)),"foot."+side,5)
        else:
            for i in range(4):
                z=.022+i*.019;y=.105-i*.010
                boots.rod((x-.024,y,z),(x+.024,y-.008,z+.01),.0022,"foot."+side,1)
                boots.rod((x+.024,y,z),(x-.024,y-.008,z+.01),.0022,"foot."+side,1)
            boots.box((x,.127,-.032),(.018,.030,.014),"foot."+side,5)
    return parts


def triangle_count(parts):
    return sum(kit.triangle_count(component[0]) for part in parts for component in part.components)


def paint_texture(name):
    if name=="ChildFace":
        canvas=atlas_kit.PixelCanvas(512,128)
        for expression in range(4):
            x0=expression*128
            canvas.rect(x0,0,x0+128,128,(183,145,115,255))
            canvas.ellipse(x0+64,93,45,28,(178,136,105,255))
            for x in (27,101):canvas.ellipse(x0+x,72,13,9,(189,137,114,255))
            for x in (40,88):
                canvas.line(x0+x-9,38,x0+x+8,37,(74,54,38,255),2)
                if expression==2:
                    canvas.line(x0+x-8,49,x0+x+8,49,(83,57,40,255),2)
                else:
                    eye_height=2 if expression==1 else 4
                    canvas.ellipse(x0+x,49,9,eye_height,(213,204,180,255))
                    canvas.ellipse(x0+x+(1 if expression==3 else 0),49,3,eye_height,(80,73,46,255))
                    canvas.ellipse(x0+x+(1 if expression==3 else 0),49,1,eye_height-1,(28,30,25,255))
                    canvas.line(x0+x-8,49-eye_height,x0+x+7,48-eye_height,(77,54,37,255))
            canvas.line(x0+57,102,x0+71,102,(117,69,60,255),1)
            canvas.line(x0+59,104,x0+70,104,(192,126,109,255),1)
            canvas.line(x0+59,82,x0+62,84,(125,88,64,255),1)
            canvas.line(x0+67,84,x0+70,82,(125,88,64,255),1)
            for x,y in ((34,66),(44,69),(83,67),(95,68)):canvas.put(x0+x,y,(153,107,78,255))
        return canvas
    if name=="ChildSkin":
        colors=((183,145,115),(181,139,106),(157,108,83),(207,170,139))
        size,tile=256,128
    elif name=="ChildHair":
        colors=((58,44,31),)*4;size,tile=256,128
    elif name=="ToyWood":
        colors=((139,101,61),(107,60,48),(65,67,61),(166,132,84));size,tile=256,128
    else:
        style=name.replace("Atlas","")
        main={"Raincoat":(153,122,59),"Quilted":(89,108,117),"Vest":(118,61,67)}[style]
        accent={"Raincoat":(132,110,61),"Quilted":(74,84,84),"Vest":(101,103,96)}[style]
        colors=(main,tuple(max(0,v-25) for v in main),main,tuple(min(255,v+8) for v in main),
            (80,79,66) if style!="Vest" else (103,77,55),
            (56,57,46) if style=="Raincoat" else (72,57,44),accent,(44,45,39))
        colors=colors+colors;size,tile=512,128
    canvas=atlas_kit.PixelCanvas(size,size)
    rng=random.Random("city-fair-child-"+name)
    columns=size//tile
    for index,color in enumerate(colors):
        x0=(index%columns)*tile;y0=(index//columns)*tile
        for y in range(tile):
            for x in range(tile):
                grain=rng.randrange(-5,6)+(1 if (x+y)%5==0 else 0)
                shade=-6 if x<6 or y>tile-8 else 0
                canvas.put(x0+x,y0+y,tuple(max(0,min(255,v+grain+shade)) for v in color)+(255,))
        light=tuple(min(255,v+16) for v in color)+(255,)
        dark=tuple(max(0,v-22) for v in color)+(255,)
        if name=="ChildHair":
            for n in range(25):
                x=rng.randrange(0,tile);y=rng.randrange(0,tile)
                canvas.line(x0+x,y0+y,x0+x+8,y0+min(tile-1,y+38),light,1)
        elif name=="ToyWood":
            for y in range(8,tile,9):
                canvas.line(x0+3,y0+y,x0+tile-4,y0+y+rng.randrange(-3,4),dark)
            canvas.ellipse(x0+79,y0+46,12,4,dark);canvas.ellipse(x0+79,y0+46,7,2,light)
        elif name.endswith("Atlas"):
            for x in (7,tile-8):
                canvas.line(x0+x,y0+2,x0+x,y0+tile-3,dark)
                for y in range(5,tile-5,5):canvas.line(x0+x+2,y0+y,x0+x+2,y0+y+2,light)
            for y in (7,tile-8):canvas.line(x0+5,y0+y,x0+tile-6,y0+y,dark)
            if name=="QuiltedAtlas" and index in (0,2,3):
                for y in range(24,tile-8,24):
                    canvas.line(x0+8,y0+y,x0+tile-9,y0+y,dark)
                    canvas.line(x0+8,y0+y+2,x0+tile-9,y0+y+2,light)
            if name=="VestAtlas" and index in (0,2):
                for y in range(13,tile-12,4):canvas.line(x0+12,y0+y,x0+tile-13,y0+y,light)
            for n in range(22):
                x=rng.randrange(8,tile-12);y=rng.randrange(8,tile-9)
                canvas.line(x0+x,y0+y,x0+x+rng.randrange(2,9),y0+y,light)
    return canvas


TEXTURES=("ChildSkin","ChildFace","ChildHair","RaincoatAtlas","QuiltedAtlas","VestAtlas","ToyWood")


def create_material(texture,path):
    mat=bpy.data.materials.new("FairChild_"+texture);mat.use_nodes=True
    shader=mat.node_tree.nodes.get("Principled BSDF");shader.inputs["Roughness"].default_value=.80
    node=mat.node_tree.nodes.new("ShaderNodeTexImage");node.image=bpy.data.images.load(str(path/(texture+".png")),check_existing=True)
    node.interpolation="Closest"
    if texture=="ChildFace":
        coord=mat.node_tree.nodes.new("ShaderNodeTexCoord")
        mapping=mat.node_tree.nodes.new("ShaderNodeVectorMath");mapping.operation="MULTIPLY"
        mapping.inputs[1].default_value=(.25,1,1)
        mat.node_tree.links.new(coord.outputs["UV"],mapping.inputs[0]);mat.node_tree.links.new(mapping.outputs[0],node.inputs["Vector"])
    mat.node_tree.links.new(node.outputs["Color"],shader.inputs["Base Color"])
    return mat


def vertex_weights(binding,p):
    if binding.startswith("sleeve."):
        side=binding.rsplit(".",1)[1];y=p[1]
        if y>.966:
            t=min(1,max(0,(y-.966)/.052))
            return {"upper_arm."+side:1-t,"chest":t}
        if y>.876:return {"upper_arm."+side:1.0}
        t=min(1,max(0,(.876-y)/.072))
        return {"upper_arm."+side:1-t,"forearm."+side:t}
    if binding.startswith("trouser."):
        side=binding.rsplit(".",1)[1];y=p[1]
        if y>.565:
            t=min(1,max(0,(y-.565)/.102))
            return {"thigh."+side:1-t,"pelvis":t}
        if y>.386:return {"thigh."+side:1.0}
        t=min(1,max(0,(.386-y)/.096))
        return {"thigh."+side:1-t,"shin."+side:t}
    if binding=="raincoat":
        if p[1]<.71:
            blend=min(1,max(0,(.71-p[1])/.095))
            return {"pelvis":1-blend,"Cloth.Hem.L" if p[0]<0 else "Cloth.Hem.R":blend}
        binding="torso"
    if binding!="torso":return {binding:1.0}
    y=p[1]
    if y<.75:return {"pelvis":1.0}
    if y<.84:
        t=(y-.75)/.09;return {"pelvis":1-t,"spine":t}
    if y<.95:
        t=(y-.84)/.11;return {"spine":1-t,"chest":t}
    return {"chest":1.0}


def component_uvs(geometry,tile,texture,custom):
    if custom is not None:return custom
    low,high=kit.bounds(geometry)
    spans=[max(.001,high[i]-low[i]) for i in range(3)]
    # Faces/cloth use their two major dimensions. The atlas has physical seams,
    # weave and wear; UVs cover their assigned rectangle, never a palette point.
    axes=sorted(range(3),key=lambda i:spans[i],reverse=True)[:2]
    if 1 in axes:axes=[next(i for i in axes if i!=1),1]
    columns=4 if texture.endswith("Atlas") else 2
    inset=.015
    return [((tile%columns+inset+(p[axes[0]]-low[axes[0]])/spans[axes[0]]*(1-2*inset))/columns,
             (columns-1-tile//columns+inset+(p[axes[1]]-low[axes[1]])/spans[axes[1]]*(1-2*inset))/columns)
            for p in geometry[0]]


def make_mesh(part,rig,material):
    vertices=[];faces=[];uvs=[];weights=[]
    for geom,binding,tile,custom in part.components:
        start=len(vertices);converted=bp.to_source(geom)
        vertices.extend(converted[0]);faces.extend(tuple(start+i for i in f) for f in converted[1])
        uvs.extend(component_uvs(geom,tile,part.texture,custom))
        weights.extend(vertex_weights(binding,p) for p in geom[0])
    mesh=bpy.data.meshes.new(part.name+"_Mesh");mesh.from_pydata(vertices,[],faces);mesh.update()
    uv=mesh.uv_layers.new(name="UVMap")
    for polygon in mesh.polygons:
        polygon.use_smooth=part.texture.startswith("Child")
        for loop in polygon.loop_indices:uv.data[loop].uv=uvs[mesh.loops[loop].vertex_index]
    mesh.materials.append(material)
    obj=bpy.data.objects.new(part.name,mesh);bpy.context.collection.objects.link(obj);obj.parent=rig
    for name in sorted({name for weight in weights for name in weight}):obj.vertex_groups.new(name=name)
    for i,weight in enumerate(weights):
        for name,value in weight.items():
            if value>0:obj.vertex_groups[name].add([i],value,"REPLACE")
    modifier=obj.modifiers.new("ChildSkeleton","ARMATURE");modifier.object=rig
    obj["bp_outfit"]=part.outfit or "shared_body";obj["bp_texture"]=part.texture
    obj["bp_visible_body"]=part.visible
    return obj


def build_model(parts,path,materials=None):
    root,rig=create_rig();materials=materials or {name:create_material(name,path) for name in TEXTURES}
    objects=[make_mesh(part,rig,materials[part.texture]) for part in parts]
    bpy.context.view_layer.update()
    return root,rig,objects,materials


def export_model(root,path):
    bpy.ops.object.select_all(action="DESELECT");root.select_set(True)
    for obj in root.children_recursive:obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"EMPTY","ARMATURE","MESH"},
        axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_space_transform=False,
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=False,use_custom_props=True,
        primary_bone_axis="Y",secondary_bone_axis="X",mesh_smooth_type="FACE")


def make_props():
    table=fair.Item("LowTable")
    for x in (-.33,.33):
        for z in (-.205,.205):
            table.box("TimberDark",(x,.255,z),(.055,.51,.055))
            table.box("TimberLight",(x,.07,z),(.07,.07,.07))
        table.box("Timber",(x,.424,0),(.047,.085,.46))
    for i in range(5):table.box("Timber",((i-2)*.16,.525,0),(.154,.05,.55))
    table.box("TimberDark",(0,.410,-.205),(.72,.07,.036))
    table.box("TimberDark",(0,.410,.205),(.72,.07,.036))
    table.anchor("TableTopAnchor",(0,.55,0))
    car=fair.Item("WoodenCar")
    car.box("Timber",(0,.050,0),(.126,.056,.276))
    car.box("TimberLight",(0,.077,-.024),(.098,.056,.120))
    car.box("TimberDark",(0,.091,.040),(.074,.028,.006))
    car.box("TimberDark",(0,.091,-.086),(.074,.027,.006))
    for label,z in (("Front",.085),("Rear",-.085)):
        car.anchor("Axle"+label,(0,.029,z))
        car.rod("Steel",(-.086,.029,z),(.086,.029,z),.008)
        for side,x in (("L",-.077),("R",.077)):
            group="WheelPivot_"+label[0]+side;car.anchor(group,(x,.029,z))
            geom=bp.u_rotated(fair.ring([(-.013,.029),(.013,.029)],14),(0,0,90))
            car.add("TimberDark",moved(geom,(x,.029,z)),group)
            hub=bp.u_rotated(fair.ring([(-.014,.010),(.014,.010)],10),(0,0,90))
            car.add("Brass",moved(hub,(x,.029,z)),group)
    car.anchor("SOCKET_Grip",(0,.080,0))
    return [table,car]


def build_props(items,material):
    roots=[]
    for item in items:
        root=fair.empty(item.name);roots.append(root);groups={"Body":root}
        for name,anchor in item.anchors.items():
            parent=groups.get(anchor["parent"],root)
            pp=item.anchors[anchor["parent"]]["position"] if anchor["parent"] else (0,0,0)
            groups[name]=fair.empty(name,parent,tuple(v-b for v,b in zip(anchor["position"],pp)))
        for (group,role),solids in item.parts.items():
            if group not in groups:groups[group]=fair.empty(group,root)
            offset=item.anchors[group]["position"] if group in item.anchors else (0,0,0)
            unity=moved(kit.merge_all(solids),tuple(-v for v in offset));geometry=bp.to_source(unity)
            mesh=bpy.data.meshes.new(item.name+"_"+group+"__"+role)
            mesh.from_pydata(geometry[0],[],geometry[1]);mesh.update()
            uvs=component_uvs(unity,{"Timber":0,"TimberDark":2,"TimberLight":3,"Steel":2,"Brass":1}.get(role,0),"ToyWood",None)
            layer=mesh.uv_layers.new(name="UVMap")
            for polygon in mesh.polygons:
                for loop in polygon.loop_indices:layer.data[loop].uv=uvs[mesh.loops[loop].vertex_index]
            mesh.materials.append(material)
            obj=bpy.data.objects.new(group+"__"+role,mesh);bpy.context.collection.objects.link(obj);obj.parent=groups[group]
    bpy.context.view_layer.update();return roots


def part_bounds(parts):
    return kit.bounds(kit.merge_all(component[0] for p in parts for component in p.components))


def rounded(values):return [round(float(v),6) for v in values]


def manifest(parts,props,textures):
    records=[]
    for part in parts:
        low,high=part_bounds([part])
        records.append(dict(name=part.name,texture=part.texture,outfit=part.outfit,visible_body=part.visible,
            triangles=triangle_count([part]),bounds_min=rounded(low),bounds_max=rounded(high)))
    variants=[]
    for outfit in OUTFITS:
        visible=[p for p in parts if p.visible or p.outfit==outfit]
        low,high=part_bounds(visible);count=triangle_count(visible)
        if not 9000<=count<=12000:raise RuntimeError(f"Child {outfit} visible budget outside 9-12k: {count}")
        if abs(high[1]-HEIGHT)>.002 or abs(low[1])>.003:raise RuntimeError("Child height/ground contract differs")
        variants.append(dict(name=outfit,visible_triangles=count,visible_parts=[p.name for p in visible],bounds_min=rounded(low),bounds_max=rounded(high)))
    prop_records=[]
    for prop in props:
        geom=kit.merge_all(g for solids in prop.parts.values() for g in solids);low,high=kit.bounds(geom)
        prop_records.append(dict(name=prop.name,bounds_min=rounded(low),bounds_max=rounded(high),triangles=kit.triangle_count(geom),
            anchors=[dict(name=n,position=a["position"],parent=a["parent"]) for n,a in prop.anchors.items()]))
    signature_data=dict(version=VERSION,bones=bone_specs(),parts=[dict(name=p.name,components=[
        dict(binding=c[1],tile=c[2],vertices=[rounded(v) for v in c[0][0]],faces=c[0][1],uv=c[3]) for c in p.components]) for p in parts],
        textures={name:hashlib.sha256(canvas.png_bytes()).hexdigest() for name,canvas in textures.items()})
    checksum=hashlib.sha256(json.dumps(signature_data,sort_keys=True,separators=(",",":")).encode()).hexdigest()
    return dict(generator="tools/build-city-fair-child-3d-model.py",generator_version=VERSION,blender_version=bpy.app.version_string,
        build_signature=checksum,height_m=HEIGHT,bone_count=len(bone_specs()),forward="Unity +Z / Blender +Y",anatomical_left="-X",
        shared_body_triangles=triangle_count([p for p in parts if p.outfit is None]),
        shared_visible_body_triangles=triangle_count([p for p in parts if p.visible]),
        bones=bone_specs(),parts=records,outfits=variants,props=prop_records,
        textures=[dict(name=name,width=canvas.width,height=canvas.height,sha256=hashlib.sha256(canvas.png_bytes()).hexdigest()) for name,canvas in textures.items()],
        validation=dict(signed_volume="every authored solid outward",weights="one or two normalized influences",fbx="round-trip world meshes and skeleton anchors"))


def canonical(name):
    pieces=name.rsplit('.',1)
    return pieces[0] if len(pieces)==2 and pieces[1].isdigit() else name


def verify_fbx(path,payload):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path/"FairChild.fbx"),use_anim=False)
    bpy.context.view_layer.update()
    objects={canonical(obj.name):obj for obj in bpy.context.scene.objects}
    for part in payload["parts"]:
        obj=objects.get(part["name"])
        if obj is None or obj.type!="MESH":raise RuntimeError("Child exported mesh missing: "+part["name"])
        points=[obj.matrix_world@v.co for v in obj.data.vertices]
        low=[min(source(p)[i] for p in points) for i in range(3)]
        high=[max(source(p)[i] for p in points) for i in range(3)]
        if any(abs(a-b)>.002 for a,b in zip(low+high,part["bounds_min"]+part["bounds_max"])):
            raise RuntimeError(f"Child exported mesh moved: {part['name']} {low} {high}")
    rig=next(obj for obj in bpy.context.scene.objects if obj.type=="ARMATURE")
    for spec in payload["bones"]:
        bone=rig.data.bones.get(spec["name"])
        if bone is None:raise RuntimeError("Child exported bone missing: "+spec["name"])
        point=rig.matrix_world@bone.head_local
        if any(abs(a-b)>.002 for a,b in zip(source(point),spec["head"])):raise RuntimeError("Child exported bone head moved: "+spec["name"])
    # Passive props share the independently verified hierarchical fair export.
    fair.verify_fbx(path,{"models":[dict(p,triangle_count=p["triangles"]) for p in payload["props"]]})
    print("CITY FAIR CHILD EXPORTED MESH/SKELETON/PROP ROUND TRIP OK")


def select_outfit(objects,parts,outfit):
    for obj,part in zip(objects,parts):
        obj.hide_render=not(part.visible or part.outfit==outfit)


def render_preview(parts,path,root,objects,materials,prop_roots,output):
    root.location.x=-.72;select_outfit(objects,parts,"Raincoat")
    rigs=[next(obj for obj in root.children if obj.type=="ARMATURE")]
    for index,outfit in enumerate(OUTFITS[1:]):
        other,other_rig,meshes,_=build_model(parts,path,materials);rigs.append(other_rig)
        other.location.x=index*.72;select_outfit(meshes,parts,outfit)
    prop_roots[0].location=(1.38,0,0)
    prop_roots[1].location=(1.35,.02,.553)
    scene=bpy.context.scene;scene.render.engine="BLENDER_EEVEE"
    scene.render.resolution_x=1800;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("Child Review World");scene.world.color=(.13,.14,.14)
    for name,p,power,size in (("Key",(0,4,5),900,5),("Fill",(-4,1,3),600,4),("Rim",(2,-3,4),700,4)):
        data=bpy.data.lights.new(name,"AREA");data.energy=power;data.size=size
        obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);obj.location=p
        obj.rotation_euler=(Vector((0,0,.7))-obj.location).to_track_quat("-Z","Y").to_euler()
    data=bpy.data.cameras.new("Child Review Camera");cam=bpy.data.objects.new("Child Review Camera",data)
    bpy.context.collection.objects.link(cam);cam.location=(2.2,5.5,2.25)
    cam.rotation_euler=(Vector((.28,0,.68))-cam.location).to_track_quat("-Z","Y").to_euler()
    data.type="ORTHO";data.ortho_scale=3.35;scene.camera=cam
    scene.render.filepath=str(output);scene.render.image_settings.file_format="PNG"
    bpy.ops.render.render(write_still=True)
    # Review the same child-native action source used by the exported bank:
    # cloth must stay continuous when the pelvis and knees actually bend.
    spec=importlib.util.spec_from_file_location("fair_child_preview_actions",ROOT/"tools/build-city-fair-child-actions-3d-model.py")
    actions=importlib.util.module_from_spec(spec);spec.loader.exec_module(actions)
    for action,phase,label in (("SitLoop",.30,"Seated"),("Walk",.25,"Walk")):
        for rig in rigs:actions.construct(rig,action,phase)
        bpy.context.view_layer.update()
        scene.render.filepath=str(output.with_name(output.stem+"-"+label+".png"))
        bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=MODEL_DIR)
    parser.add_argument("--source-dir",type=Path,default=SOURCE_DIR)
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--verify-fbx",action="store_true")
    parser.add_argument("--no-preview",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    parts=make_body()+[p for outfit in OUTFITS for p in make_outfit(outfit)]
    props=make_props();textures={name:paint_texture(name) for name in TEXTURES}
    payload=manifest(parts,props,textures)
    repeat=manifest(make_body()+[p for outfit in OUTFITS for p in make_outfit(outfit)],make_props(),
                    {name:paint_texture(name) for name in TEXTURES})
    if payload["build_signature"]!=repeat["build_signature"]:raise RuntimeError("Child generation is not deterministic")
    if args.verify_fbx:verify_fbx(args.model_dir,payload);return
    if not args.validate_only:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        for name,canvas in textures.items():canvas.write_png(args.model_dir/(name+".png"))
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root,rig,objects,materials=build_model(parts,args.model_dir)
        prop_roots=build_props(props,materials["ToyWood"])
        export_model(root,args.model_dir/"FairChild.fbx")
        for prop in prop_roots:export_model(prop,args.model_dir/(prop.name+".fbx"))
        (args.model_dir/"FairChild3D.json").write_text(json.dumps(payload,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        select_outfit(objects,parts,"Raincoat")
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"FairChild3D.blend"),check_existing=False)
        if not args.no_preview:render_preview(parts,args.model_dir,root,objects,materials,prop_roots,args.source_dir/"FairChild3D.png")
        verify_fbx(args.model_dir,payload)
    print("CITY FAIR CHILD MODEL CONTRACT OK")
    print("Bones",payload["bone_count"],"shared visible",payload["shared_visible_body_triangles"])
    for outfit in payload["outfits"]:print(outfit["name"],"visible triangles",outfit["visible_triangles"])
    print("Signature",payload["build_signature"])


if __name__=="__main__":main()
