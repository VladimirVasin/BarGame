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
import default_npc_surfaces as surfaces

_fair_spec = importlib.util.spec_from_file_location("fair_geometry", ROOT / "tools/build-city-fair-3d-model.py")
fair = importlib.util.module_from_spec(_fair_spec)
_fair_spec.loader.exec_module(fair)

VERSION = "2.0.0"
HEIGHT = 1.30
MODEL_DIR = ROOT / "Assets/Resources/City/FairChild"
SOURCE_DIR = ROOT / "ArtSource/City/FairChild"
OUTFITS = ("Raincoat", "Quilted", "Vest")
DETAIL_DIR = ROOT / "Assets/Resources/VillageLife"
SKIN = (183, 145, 115)

# Pigments use the same UV tiles as the shared normal and response maps.
FINISHES = {
    "ChildSkin": (("skin", SKIN), ("binding", SKIN), ("skin", SKIN), ("skin", SKIN)),
    "RaincoatAtlas": (("oilcloth", (153, 122, 59)), ("binding", (112, 91, 53)),
        ("workShirt", (142, 113, 60)), ("workJacket", (146, 116, 59)),
        ("workTrousers", (80, 79, 66)), ("winterBoot", (58, 58, 47)),
        ("shortHair", (69, 51, 37)), ("rubber", (44, 45, 39))),
    "QuiltedAtlas": (("quilt", (89, 108, 117)), ("binding", (65, 83, 91)),
        ("workJacket", (87, 105, 112)), ("shortJacket", (84, 104, 113)),
        ("winterTrousers", (80, 79, 70)), ("shoe", (72, 57, 44)),
        ("rib", (74, 84, 84)), ("rubber", (46, 44, 38)), ("shortHair", (52, 42, 35))),
    "VestAtlas": (("cardigan", (118, 61, 67)), ("binding", (90, 56, 54)),
        ("rib", (113, 61, 66)), ("flannel", (105, 106, 96)),
        ("workTrousers", (103, 77, 55)), ("shoe", (72, 57, 44)),
        ("vest", (101, 103, 96)), ("rubber", (48, 45, 39)), ("shortHair", (92, 68, 46))),
}

def finish_name(texture, tile):
    return FINISHES[texture][tile][0]


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


def limb_surface(nodes,widths,depths,sides=14,steps=5,fold=0.,fold_joint=1):
    """One closed garment surface across a bent joint, with no separate caps."""
    centers=[];radii=[]
    hinge = fold_joint*steps
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
            front = math.sin(angle)
            crease = fold * math.sin((row-hinge)*1.65 + .85*math.cos(angle))
            crease *= math.exp(-((row-hinge)/2.6)**2) * front**4
            vertices.append(tuple(center+across*(math.cos(angle)*width*.5)+normal*(front*(depth*.5+crease))))
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


HEAD_PROFILE = [
    (1.057,.026,.028,.008), (1.068,.044,.042,.011), (1.080,.058,.056,.010),
    (1.094,.072,.067,.007), (1.110,.083,.076,.005), (1.130,.092,.081,.003),
    (1.152,.095,.086,.000), (1.174,.095,.090,-.001), (1.196,.093,.090,-.003),
    (1.217,.089,.085,-.005), (1.238,.082,.078,-.007), (1.256,.071,.067,-.008),
    (1.272,.055,.051,-.009), (1.285,.034,.030,-.009), (1.292,.006,.006,-.009),
]


def face_u(angle):
    # The frontal half has most of the chart. The neutral rear meets at the seam.
    angle = (math.pi * .5 - angle + math.pi) % math.tau - math.pi
    front = math.pi * .5
    if abs(angle) <= front:
        return .50 + .36 * angle / front
    return .50 + math.copysign(.36 + .14 * (abs(angle) - front) / front, angle)


def head_surface():
    sides = 32
    geometry = loft(HEAD_PROFILE, sides)
    low, high = HEAD_PROFILE[0][0], HEAD_PROFILE[-1][0]
    uv = [(face_u(i * math.tau / sides), (y - low) / (high - low))
          for y, *_ in HEAD_PROFILE for i in range(sides)]
    return geometry, uv


def hair_surface(outfit):
    sides = 24
    # A low nape and shaped hairline are part of the scalp surface.
    # The cap has its own low-profile hair underneath the knitted band.
    cap = outfit == "Quilted"
    vertices = []
    for row in range(11):
        t = row / 10
        for i in range(sides):
            a = i * math.tau / sides
            front = max(0., math.sin(a))
            rear = max(0., -math.sin(a))
            if cap:
                start = 1.186 + .049 * front - .015 * rear
                crown, lift = 1.255, .002
            elif outfit == "Raincoat":
                start = 1.203 + .033 * front - .025 * rear + .010 * front * math.cos(a)
                crown, lift = HEIGHT, .009
            else:
                start = 1.186 + .056 * front - .022 * rear - .016 * front * max(0., math.cos(a) * 2)
                crown, lift = HEIGHT, .011
            y = start + (crown - start) * t
            sample = min(y, HEAD_PROFILE[-1][0])
            for low, high in zip(HEAD_PROFILE, HEAD_PROFILE[1:]):
                if sample <= high[0]:
                    blend = (sample - low[0]) / (high[0] - low[0])
                    rx, rz, zc = [a + (b - a) * blend for a, b in zip(low[1:], high[1:])]
                    break
            if y > sample:
                rx *= 1 - .6 * t
                rz *= 1 - .6 * t
            ridge = lift * math.sin(math.pi * t) + .004 + .003 * (1 - t)
            if not cap:
                ridge += .002 * math.sin(a * (5 if outfit == "Vest" else 3) + 2*t) * (1 - t)
            vertices.append(((rx + ridge) * math.cos(a), y, zc + (rz + ridge) * math.sin(a)))
    faces = [tuple(range(sides)), tuple(reversed(range(10*sides, 11*sides)))]
    for row in range(10):
        for i in range(sides):
            a = row*sides + i
            b = row*sides + (i + 1) % sides
            faces.append((b, a, a+sides, b+sides))
    return vertices, faces


def make_body():
    parts = []
    def part(name, texture="ChildSkin", visible=False):
        p = Part(name, texture, visible=visible)
        parts.append(p)
        return p
    head, uv = head_surface()
    part("GEO_Head", "ChildFace", True).add(head, "head", uv=uv)
    ears = part("GEO_Ears", visible=True)
    for sign in (-1, 1):
        ear = fair.ellipsoid((sign*.097, 1.170, .002), (.027, .054, .029), 12, 8)
        ears.add(bp.u_rotated_about(ear, (0, 0, sign*12), (sign*.097, 1.170, .002)), "head")
        ears.ball((sign*.104, 1.168, .010), (.012, .031, .017), "head", 0, 10, 6)
    part("GEO_Neck", visible=True).add(loft([
        (1.015,.034,.030,.002), (1.035,.030,.028,.003), (1.055,.029,.027,.004),
        (1.075,.031,.028,.004), (1.091,.034,.030,.003)], 18), "neckblend")
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
        hand.add(limb_surface([
            (sign*.277,.714,.029), (sign*.289,.690,.031), (sign*.298,.665,.030),
            (sign*.303,.654,.029)],
            [.029,.053,.048,.039], [.018,.024,.021,.014], sides=12, steps=3), "wrist."+side)
        for finger in ("index", "middle", "ring", "pinky", "thumb"):
            base = f"finger.{finger}.01.{side}"
            tip = f"finger.{finger}.02.{side}"
            start, joint, end = [Vector(p) for p in (specs[base]["head"], specs[base]["tail"], specs[tip]["tail"])]
            width = .011 if finger == "thumb" else .0088 if finger == "pinky" else .010
            hand.add(limb_surface([start, joint, joint.lerp(end, .83), end],
                [width, width*.90, width*.76, width*.38],
                [width*.90, width*.82, width*.64, width*.35], sides=8, steps=2), base)
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


def trousers_surface(outfit):
    """Join the leg rings across the crotch and continue the outer contour to the waist."""
    sides = 16
    vertices,faces,tops = [],[],{}
    rain = outfit == "Raincoat"
    ease_width = 1.04 if outfit == "Quilted" else .94 if rain else 1.
    for sign in (1,-1):
        start = len(vertices)
        for y,cx,rx,rz,zc in ((.079,.079,.039,.033,.005),(.116,.080,.041,.036,.005),
            (.199,.081,.044,.042,.013),(.267,.082,.048,.045,.020),(.311,.083,.046,.044,.024),
            (.338,.083,.051,.048,.024),(.367,.083,.049,.047,.023),(.401,.083,.054,.052,.020),
            (.471,.082,.055,.055,.012),(.528,.079,.059,.062,.004),(.574,.069,.060,.066,0)):
            for i in range(sides):
                a = i*math.tau/sides
                pleat = .003 * math.sin((y-.338)*82 + math.cos(a)) * math.exp(-((y-.338)/.10)**2)
                vertices.append((sign*cx+rx*ease_width*math.cos(a),y,zc+(rz+pleat)*math.sin(a)))
        faces.append(tuple(range(start,start+sides)))
        end = len(vertices)-sides
        for row in range(start,end,sides):
            for i in range(sides):
                a,b = row+i,row+(i+1)%sides
                faces.append((b,a,a+sides,b+sides))
        tops[sign] = list(range(end,end+sides))
    right,left = tops[1],tops[-1]
    outside = [right[i%16] for i in range(10,23)] + [left[i] for i in range(2,15)]
    for i in range(4):
        faces.append((right[6+i],left[(18-i)%16],left[(17-i)%16],right[7+i]))
    angles = [math.atan2(vertices[i][2]/.071,vertices[i][0]/.126) for i in outside]
    previous = outside
    for y,rx,rz in ((.615,.122,.071),(.650,.115,.070),(.684,.106,.065),
                    (.705,.099,.060),(.726,.096,.057),(.737,.095,.057)):
        ring = []
        for angle in angles:
            ring.append(len(vertices))
            vertices.append((rx*ease_width*math.cos(angle),y,rz*math.sin(angle)))
        for i in range(len(ring)):
            j = (i+1)%len(ring)
            faces.append((previous[j],previous[i],ring[i],ring[j]))
        previous = ring
    faces.append(tuple(reversed(previous)))
    edges = {}
    for face in faces:
        for a,b in zip(face,face[1:]+face[:1]):
            key = (min(a,b),max(a,b))
            edges[key] = edges.get(key,0) + (1 if a<b else -1)
    if any(edges.values()):
        raise RuntimeError("Child trousers have an open seam or inconsistent winding.")
    return vertices,faces


def make_outfit(outfit):
    parts=[];specs={s["name"]:s for s in bone_specs()}
    def part(role):
        p=Part("OUTFIT_"+outfit+"_"+role,outfit+"Atlas",outfit);parts.append(p);return p
    rain=outfit=="Raincoat";quilted=outfit=="Quilted"
    part("Hair").add(hair_surface(outfit), "hair", 6 if rain else 8)
    coat=part("Body")
    if rain:
        profile=[(.565,.126,.079,-.004),(.574,.132,.083,-.004),(.622,.133,.082,-.004),
            (.71,.123,.079,-.001),(.79,.113,.072,0),(.865,.128,.073,0),(.94,.144,.075,0),
            (.989,.148,.068,0),(1.020,.097,.050,0),(1.026,.049,.036,0)]
    elif quilted:
        profile=[(.695,.110,.071,0),(.711,.115,.075,0)]
        for y,rx,rz in ((.752,.118,.077),(.812,.121,.079),(.872,.134,.081),(.932,.144,.078),(.975,.147,.070)):
            profile.extend([(y-.022,rx-.003,rz-.004,0),(y,rx,rz,0),(y+.020,rx-.002,rz-.004,0)])
        profile.extend([(1.010,.114,.056,0),(1.031,.051,.038,0)])
    else:
        profile=[(.695,.104,.069,0),(.717,.111,.073,0),(.766,.110,.071,0),(.834,.119,.070,0),
            (.913,.132,.069,0),(.983,.136,.062,0),(1.014,.091,.047,0),(1.029,.048,.034,0)]
    coat.add(loft(profile,24),"raincoat" if rain else "torso",0)
    # Distinct constructed closures, not a colour variant of a single jacket.
    placket=part("Closure")
    if rain or quilted:
        placket.add(loft([(y,.010 if rain else .005,.004,z+depth)
                          for y,_,depth,z in profile[1:-1]], 8), "raincoat" if rain else "torso", 1)
        for y in ((.622,.71,.79,.865,.94) if rain else (.975,)):
            depth = next(p[2]+p[3] for p in profile if p[0] == y)
            placket.ball((0,y,depth+.004),(.012,.013,.008),"torso",7,8,5)
    else:
        placket.add(torus_y((0,1.023,0),.047,.006,20,6,(1,.7,.75)),"neckblend",2)
    collar=part("Collar")
    collar.add(torus_y((0,1.033,.003),.038,.008,20,6,(1.15,1.4,1)),"neckblend",2)
    if rain or quilted:
        pockets=part("Pockets")
        for sign in (-1,1):
            if rain:
                pockets.add(bp.u_rotated_about(bp.u_box((sign*.066,.747,.062),(.061,.076,.015),.005),
                    (0,sign*26,0),(sign*.066,.747,.062)),"raincoat",3)
                pockets.rod((sign*.094,.784,.052),(sign*.040,.790,.077),.003,"raincoat",1)
            else:
                pockets.rod((sign*.091,.755,.058),(sign*.057,.810,.078),.004,"torso",1)
    if rain:
        hood=part("FoldedHood")
        # A visibly hollow hood folded behind the neck, with a doubled opening.
        hood.add(moved(loft([(-.080,.055,.029,0),(-.045,.071,.043,0),(0,.064,.039,0),
                       (.023,.047,.025,0),(.022,.036,.020,0),(-.038,.046,.020,0)],18),(0,1.005,-.071)),"Cloth.Hood",2)
        hood.add(torus_y((0,1.001,-.091),.054,.006,18,6,(1.02,.6,.54)),"Cloth.Hood",2)
    elif quilted:
        cap=part("KnittedCap")
        cap.add(loft([(1.227,.093,.089,-.006),(1.244,.092,.088,-.006),(1.262,.084,.079,-.007),
                      (1.279,.064,.059,-.009),(1.292,.040,.035,-.009),(1.300,.010,.009,-.009)],24),"head",6)
        cap.add(torus_y((0,1.236,-.006),.088,.010,20,6,(1,.65,.96)),"head",6)
    else:
        vest=part("Vest")
        vest_profile = [(.710,.115,.076,0),(.738,.120,.078,0),(.805,.121,.077,0),(.866,.134,.078,0),
                        (.935,.144,.076,0),(.982,.144,.069,0),(1.013,.093,.050,0)]
        points, faces = loft(vest_profile, 24)
        # Lower the front neckline and sides of the top rings, leaving the
        # sweater visible at the opening and between the sleeves and shoulder.
        for i in range(4*24, len(points)):
            x,y,z = points[i]
            angle = i%24 * math.tau/24
            cut = .104 * max(0.,math.sin(angle))**7 + .039 * abs(math.cos(angle))**8
            points[i] = (x, y - cut * ((i//24-3)/3)**2, z)
        vest.add((points,faces),"torso",6)
        for sign in (-1,1):
            pocket = bp.u_box((sign*.057,.764,.070),(.060,.066,.012),.004)
            vest.add(bp.u_rotated_about(pocket,(0,sign*22,0),(sign*.057,.764,.070)),"torso",3)
    part("Trousers").add(trousers_surface(outfit), "trousers", 4)
    for side,sign in (("L",-1),("R",1)):
        sleeve=part("Sleeve."+side)
        upper=specs["upper_arm."+side];fore=specs["forearm."+side]
        sleeve.add(limb_surface([(sign*.106,.990,0),(sign*.164,.968,0),upper["tail"],fore["tail"]],
                     [.058,.111,.094,.075] if rain else [.058,.107,.089,.064] if quilted else [.053,.101,.082,.062],
                     [.063,.096,.083,.065] if rain else [.059,.091,.078,.058], steps=5, fold=.0035, fold_joint=2),
                   "sleeve."+side,2 if rain or not quilted else 0)
        cuff=part("Cuff."+side)
        wrist=Vector(fore["tail"]);end=wrist+(Vector(fore["head"])-wrist).normalized()*.027
        cuff.add(segment(wrist,end,.079 if rain else .067,.068 if rain else .060,14,4),"forearm."+side,1)
        boots=part("Boots."+side)
        x=sign*.079
        boots.add(moved(loft([(.016,.050,.100,.040),(.043,.055,.103,.040),(.066,.049,.098,.043),
                      (.080,.043,.076,.025),(.110,.041,.041,-.002),(.16 if rain else .119,.043,.040,-.004),
                      (.20 if rain else .140,.044,.041,-.004)],16),(x,0,0)),"boot."+side,5)
        boots.add(moved(loft([(0,.051,.102,.039),(.012,.056,.107,.039),(.025,.056,.107,.039),(.030,.051,.102,.039)],14),(x,0,0)),"foot."+side,7)
        for i in range(5):
            boots.box((x,.007,-.038+i*.034),(.093,.013,.020),"foot."+side,7)
        if rain:
            boots.add(torus_y((x,.189,-.004),.041,.004,20,6,(1,1,1)),"boot."+side,5)
        else:
            for i in range(4):
                z=.022+i*.019;y=.105-i*.010
                boots.rod((x-.024,y,z),(x+.024,y-.008,z+.01),.0022,"foot."+side,1)
                boots.rod((x+.024,y,z),(x-.024,y-.008,z+.01),.0022,"foot."+side,1)
            boots.box((x,.127,-.032),(.018,.030,.014),"foot."+side,5)
    return parts


def triangle_count(parts):
    return sum(kit.triangle_count(component[0]) for part in parts for component in part.components)


def face_atlas():
    canvas = atlas_kit.PixelCanvas(512,512)
    canvas.rect(0,0,512,512,(*SKIN,255))
    faces = [
        (43,85,64,83,5,18, (91,76,47), 1, True),
        (44,84,62,82,4,15, (78,87,73), -1, False),
        (41,87,65,84,5,16, (103,78,50), 0, True),
    ]
    for row,(left,right,nose,mouth,eye_height,mouth_width,iris,tilt,freckles) in enumerate(faces):
        for expression in range(4):
            ox,oy = expression*128,row*128
            def ellipse(x,y,w,h,rgb):
                canvas.ellipse(ox+x,oy+y,w,h,(*rgb,255))
            def line(x,y,x2,y2,rgb,thickness=1):
                canvas.line(ox+x,oy+y,ox+x2,oy+y2,(*rgb,255),thickness)
            for x,side in ((left,-1),(right,1)):
                ellipse(x+side*6,72,12,9,(186,137,112))
                line(x-8,49+side*tilt,x+7,49-side*tilt,(101,73,54),2)
                h = 1 if expression==2 else 2 if expression==1 else eye_height
                ellipse(x,57,9,h,(109,76,57))
                if expression != 2:
                    ellipse(x,58,8,max(1,h-1),(214,197,172))
                    pupil = x + (1 if expression==3 else 0)
                    ellipse(pupil,58,3,max(1,h-1),iris)
                    ellipse(pupil,58,1,max(1,h-2),(51,43,35))
                    if h>2:
                        canvas.put(ox+pupil-1,oy+57,(231,220,196,255))
            # Nose and lips are paint on the same closed head surface.
            ellipse(nose,69,3,6,(193,152,121))
            ellipse(nose,75,7,3,(173,127,103))
            ellipse(nose,74,4,2,(202,155,125))
            line(nose-6,75,nose-3,76,(149,104,82))
            line(nose+3,76,nose+6,75,(150,106,84))
            half = mouth_width//2
            line(64-half,mouth,63,mouth+1,(124,78,63))
            line(63,mouth+1,64+half,mouth,(124,78,63))
            line(61-half//2,mouth+3,67+half//2,mouth+3,(187,129,108))
            if freckles:
                for x,y in ((38,67),(43,69),(48,67),(80,68),(86,66),(92,68)):
                    canvas.put(ox+x+tilt,oy+y,(153,109,86,255))
    return canvas


def paint_texture(name):
    if name=="ChildFace":
        return face_atlas()
    if name in FINISHES:
        canvas = surfaces.albedo()
        for finish,pigment in dict(FINISHES[name]).items():
            x,bottom = surfaces.tile_rect(finish)
            y = surfaces.SIZE-bottom-surfaces.TILE
            for row in range(y,y+surfaces.TILE):
                for column in range(x,x+surfaces.TILE):
                    offset = (row*surfaces.SIZE+column)*4
                    tone = canvas.pixels[offset]/255
                    canvas.put(column,row,(*(round(channel*tone) for channel in pigment),255))
        return canvas
    colors=((139,101,61),(107,60,48),(65,67,61),(166,132,84))
    size,tile=256,128
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
        for y in range(8,tile,9):
            canvas.line(x0+3,y0+y,x0+tile-4,y0+y+rng.randrange(-3,4),dark)
        canvas.ellipse(x0+79,y0+46,12,4,dark);canvas.ellipse(x0+79,y0+46,7,2,light)
    return canvas


TEXTURES=("ChildSkin","ChildFace","RaincoatAtlas","QuiltedAtlas","VestAtlas","ToyWood")


def create_material(texture,path):
    mat=bpy.data.materials.new("FairChild_"+texture);mat.use_nodes=True
    shader=mat.node_tree.nodes.get("Principled BSDF");shader.inputs["Roughness"].default_value=.80
    node=mat.node_tree.nodes.new("ShaderNodeTexImage");node.image=bpy.data.images.load(str(path/(texture+".png")),check_existing=True)
    node.interpolation="Closest" if texture in ("ChildFace","ToyWood") else "Linear"
    if texture=="ChildFace":
        coord=mat.node_tree.nodes.new("ShaderNodeTexCoord")
        mapping=mat.node_tree.nodes.new("ShaderNodeVectorMath");mapping.operation="MULTIPLY_ADD"
        mapping.inputs[1].default_value=(.25,.25,1)
        mapping.inputs[2].default_value=(0,.75,0)
        mat.node_tree.links.new(coord.outputs["UV"],mapping.inputs[0]);mat.node_tree.links.new(mapping.outputs[0],node.inputs["Vector"])
    elif texture in FINISHES:
        normal, response = [mat.node_tree.nodes.new("ShaderNodeTexImage") for _ in surfaces.MAP_NAMES]
        for image_node, map_name in zip((normal,response),surfaces.MAP_NAMES):
            image_node.image = bpy.data.images.load(str(DETAIL_DIR/map_name),check_existing=True)
            image_node.image.colorspace_settings.name = "Non-Color"
        tangent = mat.node_tree.nodes.new("ShaderNodeNormalMap")
        tangent.inputs["Strength"].default_value = .65
        mat.node_tree.links.new(normal.outputs["Color"],tangent.inputs["Color"])
        mat.node_tree.links.new(tangent.outputs["Normal"],shader.inputs["Normal"])
        roughness = mat.node_tree.nodes.new("ShaderNodeMath")
        roughness.operation = "SUBTRACT"
        roughness.inputs[0].default_value = 1
        mat.node_tree.links.new(response.outputs["Alpha"],roughness.inputs[1])
        mat.node_tree.links.new(roughness.outputs[0],shader.inputs["Roughness"])
    mat.node_tree.links.new(node.outputs["Color"],shader.inputs["Base Color"])
    return mat


def ease(value):
    t = min(1.,max(0.,value))
    return t*t*(3-2*t)


def blend_weights(first,second,ratio):
    t = ease(ratio)
    return {first:1-t,second:t}


def vertex_weights(binding,p,specs):
    x,y,z = p
    side = binding.rsplit(".",1)[-1]
    if binding.startswith("sleeve."):
        if y>.925:
            return blend_weights("upper_arm."+side,"chest",(.168-abs(x))/.057)
        return blend_weights("upper_arm."+side,"forearm."+side,(.886-y)/.086)
    if binding=="trousers":
        if y<.55:
            side = "L" if x<0 else "R"
            return blend_weights("thigh."+side,"shin."+side,(.393-y)/.110)
        pelvis = ease((y-.55)/.138)
        right = ease((x+.036)/.072)*(1-pelvis)
        return {"pelvis":pelvis,"thigh.R":right,"thigh.L":1-pelvis-right}
    if binding.startswith("boot."):
        return blend_weights("foot."+side,"shin."+side,(y-.083)/.122)
    if binding.startswith("wrist."):
        return blend_weights("hand."+side,"forearm."+side,(y-.688)/.029)
    if binding.startswith("finger.") and ".01." in binding:
        first = specs[binding]
        start, joint = Vector(first["head"]), Vector(first["tail"])
        axis = joint-start
        t = (Vector(p)-start).dot(axis)/axis.length_squared
        return blend_weights(binding,binding.replace(".01.",".02."),(t-.76)/.48)
    if binding=="neckblend":
        if y < 1.049:
            return blend_weights("chest","neck",(y-1.015)/.034)
        return blend_weights("neck","head",(y-1.053)/.041)
    if binding=="hair":
        return blend_weights("head","Hair.Fringe",ease((z-.035)/.047)*ease((1.280-y)/.050)*.8)
    if binding=="raincoat":
        if y<.71:
            amount = ease((.71-y)/.105)
            left = ease((.040-x)/.080)*amount
            return {"pelvis":1-amount,"Cloth.Hem.L":left,"Cloth.Hem.R":amount-left}
        binding="torso"
    if binding!="torso":
        return {binding:1.}
    if y<.84:
        return blend_weights("pelvis","spine",(y-.74)/.10)
    return blend_weights("spine","chest",(y-.84)/.11)


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


def detail_uv(points,axes,bounds,finish):
    low,high = bounds
    u,v = axes
    return [surfaces.tile_uv(finish,(p[u]-low[u])/max(.001,high[u]-low[u]),
                           (p[v]-low[v])/max(.001,high[v]-low[v])) for p in points]


def make_mesh(part,rig,material,specs):
    vertices,faces,uvs,weights,smooth = [],[],[],[],[]
    for geom,binding,tile,custom in part.components:
        start = len(vertices)
        converted = bp.to_source(geom)
        vertices.extend(converted[0])
        weights.extend(vertex_weights(binding,p,specs) for p in geom[0])
        if any(abs(sum(w.values())-1.)>1e-6 or len(w)>4 for w in weights[start:]):
            raise RuntimeError("Invalid child skin weights: "+part.name)
        bounds = kit.bounds(geom)
        for face in converted[1]:
            faces.append(tuple(start+i for i in face))
            smooth.append(len(face) <= 4)
            points = [geom[0][i] for i in face]
            if custom is not None:
                chart = [custom[i] for i in face]
                # Rear head seam has identical pigment on both sides.
                if len(face)<=4 and max(u for u,_ in chart)-min(u for u,_ in chart)>.5:
                    chart = [(1. if u<.1 else u,v) for u,v in chart]
                uvs.append([(.012+u*.976,.012+v*.976) for u,v in chart])
            else:
                normal = (Vector(points[1])-Vector(points[0])).cross(Vector(points[2])-Vector(points[0]))
                axis = max(range(3),key=lambda index:abs(normal[index]))
                axes = ((2,1),(0,2),(0,1))[axis]
                uvs.append(detail_uv(points,axes,bounds,finish_name(part.texture,tile)))
    mesh = bpy.data.meshes.new(part.name+"_Mesh")
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for polygon,chart,rounded_faces in zip(mesh.polygons,uvs,smooth):
        polygon.use_smooth = rounded_faces
        for loop,point in zip(polygon.loop_indices,chart):
            uv.data[loop].uv = point
    mesh.materials.append(material)
    obj = bpy.data.objects.new(part.name,mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = rig
    for name in sorted({name for weight in weights for name in weight}):
        obj.vertex_groups.new(name=name)
    for i,weight in enumerate(weights):
        for name,value in weight.items():
            if value>0:
                obj.vertex_groups[name].add([i],value,"REPLACE")
    modifier=obj.modifiers.new("ChildSkeleton","ARMATURE");modifier.object=rig
    obj["bp_outfit"]=part.outfit or "shared_body";obj["bp_texture"]=part.texture
    obj["bp_visible_body"]=part.visible
    return obj


def build_model(parts,path,materials=None):
    root,rig=create_rig()
    materials=materials or {name:create_material(name,path) for name in TEXTURES}
    specs={bone["name"]:bone for bone in bone_specs()}
    objects=[make_mesh(part,rig,materials[part.texture],specs) for part in parts]
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
        if count>12000:raise RuntimeError(f"Child {outfit} exceeds the visible triangle ceiling: {count}")
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
        validation=dict(signed_volume="every authored solid outward",weights="up to four normalized influences",fbx="round-trip world meshes and skeleton anchors"))


def canonical(name):
    pieces=name.rsplit('.',1)
    return pieces[0] if len(pieces)==2 and pieces[1].isdigit() else name


def verify_fbx(path,payload,verify_props=True):
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
    if verify_props:
        fair.verify_fbx(path,{"models":[dict(p,triangle_count=p["triangles"]) for p in payload["props"]]})
    print("CITY FAIR CHILD EXPORTED MESH/SKELETON ROUND TRIP OK")


def select_outfit(objects,parts,outfit):
    for obj,part in zip(objects,parts):
        obj.hide_render=not(part.visible or part.outfit==outfit)


def render_preview(parts,path,root,objects,materials,prop_roots,output):
    root.location.x=-.72;select_outfit(objects,parts,"Raincoat")
    rigs=[next(obj for obj in root.children if obj.type=="ARMATURE")]
    for index,outfit in enumerate(OUTFITS[1:]):
        other,other_rig,meshes,_=build_model(parts,path,materials);rigs.append(other_rig)
        other.location.x=index*.72;select_outfit(meshes,parts,outfit)
        face_material = materials["ChildFace"].copy()
        next(node for node in face_material.node_tree.nodes if node.type=="VECT_MATH").inputs[2].default_value=(0,.5-index*.25,0)
        next(obj for obj in meshes if obj.name.startswith("GEO_Head")).data.materials[0]=face_material
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
    parser.add_argument("--body-only",action="store_true",help="Publish the child without re-exporting the passive toys.")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    parts=make_body()+[p for outfit in OUTFITS for p in make_outfit(outfit)]
    props=make_props();textures={name:paint_texture(name) for name in TEXTURES}
    payload=manifest(parts,props,textures)
    repeat=manifest(make_body()+[p for outfit in OUTFITS for p in make_outfit(outfit)],make_props(),
                    {name:paint_texture(name) for name in TEXTURES})
    if payload["build_signature"]!=repeat["build_signature"]:raise RuntimeError("Child generation is not deterministic")
    if args.verify_fbx:verify_fbx(args.model_dir,payload,not args.body_only);return
    if not args.validate_only:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        for name,canvas in textures.items():canvas.write_png(args.model_dir/(name+".png"))
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root,rig,objects,materials=build_model(parts,args.model_dir)
        prop_roots=build_props(props,materials["ToyWood"])
        export_model(root,args.model_dir/"FairChild.fbx")
        if not args.body_only:
            for prop in prop_roots:export_model(prop,args.model_dir/(prop.name+".fbx"))
        (args.model_dir/"FairChild3D.json").write_text(json.dumps(payload,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        select_outfit(objects,parts,"Raincoat")
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.file.pack_all()
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"FairChild3D.blend"),check_existing=False)
        if not args.no_preview:render_preview(parts,args.model_dir,root,objects,materials,prop_roots,args.source_dir/"FairChild3D.png")
        verify_fbx(args.model_dir,payload,not args.body_only)
    print("CITY FAIR CHILD MODEL CONTRACT OK")
    print("Bones",payload["bone_count"],"shared visible",payload["shared_visible_body_triangles"])
    for outfit in payload["outfits"]:print(outfit["name"],"visible triangles",outfit["visible_triangles"])
    print("Signature",payload["build_signature"])


if __name__=="__main__":main()
