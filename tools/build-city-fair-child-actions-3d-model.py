#!/usr/bin/env python3
"""Child-native fair actions on the independently authored 1.30 m skeleton.

No adult character or adult action bank is loaded. Pose contacts, planted
endpoints and the seated support are measured from the exported child's bones.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector, Quaternion, Matrix

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("fair_child_body", ROOT / "tools/build-city-fair-child-3d-model.py")
body = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = body
spec.loader.exec_module(body)
OUT = ROOT / "Assets/Resources/City/FairChild/ChildActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/City/FairChild/ChildActions.blend"
FPS = 24
CLIPS = (("Idle",4.,True),("Walk",1.25,True),("Turn",1.,False),("Stop",.5,False),
         ("GoodsEnter",1.25,False),("GoodsLoop",3.,True),("GoodsExit",1.25,False),
         ("ToyEnter",1.5,False),("ToyLoop",4.,True),("ToyExit",1.5,False),
         ("SitEnter",2.,False),("SitLoop",4.,True),("SitExit",2.,False),
         ("AdjustSleeve",2.5,False),("AdjustCap",2.5,False))


def smooth(t):
    t=max(0.,min(1.,t));return t*t*(3.-2.*t)
def src(p):return Vector(body.source(p))
def unity(p):return Vector((p.x,p.z,p.y))
def reset(rig):
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
def rotate(rig,name,axis,degrees):
    b=rig.pose.bones[name];head=b.head.copy()
    q=Quaternion(src(axis),math.radians(degrees))
    b.matrix=Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ b.matrix
    bpy.context.view_layer.update()
def aim(rig,name,direction):
    b=rig.pose.bones[name];head=b.head.copy()
    q=(b.tail-head).rotation_difference(direction)
    b.matrix=Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ b.matrix
    bpy.context.view_layer.update()
def move_pelvis(rig,target):
    b=rig.pose.bones["pelvis"];m=b.matrix.copy();m.translation=src(target);b.matrix=m
    bpy.context.view_layer.update()
def pose_copy(rig):return {b.name:(b.location.copy(),b.rotation_quaternion.copy()) for b in rig.pose.bones}
def pose_apply(rig,pose):
    for name,(p,q) in pose.items():rig.pose.bones[name].location=p;rig.pose.bones[name].rotation_quaternion=q
    bpy.context.view_layer.update()
def blend(rig,a,b,t):
    pose_apply(rig,{name:(a[name][0].lerp(b[name][0],t),a[name][1].slerp(b[name][1],t)) for name in a})


def arm(rig,side,target,hand_direction=(0,-.08,1)):
    shoulder=rig.pose.bones["upper_arm."+side].head.copy()
    grip=rig.data.bones["SOCKET_Grip."+side];hand=rig.data.bones["hand."+side]
    offset=grip.head_local-hand.head_local
    direction=src(hand_direction).normalized()
    wrist=src(target)-direction*offset.length
    a=rig.data.bones["upper_arm."+side].length;b=rig.data.bones["forearm."+side].length
    delta=wrist-shoulder;distance=delta.length
    if distance>=a+b-.0005 or distance<=abs(a-b)+.0005:
        raise ValueError(f"Child {side} grip {tuple(target)} unreachable: wristdistance={distance:.6f}, arm={a+b:.6f}, shoulder={tuple(unity(shoulder))}")
    axis=delta.normalized();hint=src((-.4 if side=="L" else .4,-.2,-.4))
    pole=(hint-axis*hint.dot(axis)).normalized()
    along=(a*a-b*b+distance*distance)/(2*distance)
    elbow=shoulder+axis*along+pole*math.sqrt(max(0,a*a-along*along))
    aim(rig,"upper_arm."+side,elbow-shoulder)
    aim(rig,"forearm."+side,wrist-rig.pose.bones["forearm."+side].head)
    rotation=offset.rotation_difference(direction) @ hand.matrix_local.to_quaternion()
    rig.pose.bones["hand."+side].matrix=Matrix.LocRotScale(wrist,rotation,Vector((1,1,1)))
    bpy.context.view_layer.update()
    error=(rig.pose.bones["SOCKET_Grip."+side].head-src(target)).length
    if error>.0002:raise ValueError(f"Child {side} IK residual {error:.6f} at {tuple(target)}")


def held(rig,kind,t=0.):
    reset(rig);right_direction=(0,-.08,1)
    if kind=="Goods":
        rotate(rig,"spine",(1,0,0),-13)
        rotate(rig,"spine",(0,0,1),.7*math.sin(2*math.pi*t))
        rotate(rig,"head",(0,1,0),3*math.sin(2*math.pi*t))
        targets={"L":(-.17,.97,.32),"R":(.17,.97,.32)}
    elif kind=="Toy":
        rotate(rig,"spine",(1,0,0),-70)
        rotate(rig,"chest",(1,0,0),-10)
        rotate(rig,"head",(1,0,0),15)
        lift=max(0.,math.sin((t-.5)*2*math.pi)) if t>.5 else 0.
        yaw=math.radians(24*lift)
        right_direction=(math.sin(yaw),-.08,math.cos(yaw))
        targets={"L":(-.15,.565,.32),"R":(.09,.63+.065*lift,.40+.08*math.sin(2*math.pi*t))}
    elif kind=="Sit":
        move_pelvis(rig,(0,.57,-.54))
        for side in ("L","R"):
            aim(rig,"thigh."+side,src((0,-.04,.274)))
            aim(rig,"shin."+side,src((0,-.273,.02)))
            rotate(rig,"shin."+side,(1,0,0),(7 if side=="L" else -7)*math.sin(2*math.pi*t))
            foot=rig.pose.bones["foot."+side]
            foot.matrix=Matrix.LocRotScale(foot.head.copy(),rig.data.bones[foot.name].matrix_local.to_quaternion(),Vector((1,1,1)))
            bpy.context.view_layer.update()
        targets={"L":(-.13,.68,-.31),"R":(.13,.68,-.31)}
    else:raise ValueError(kind)
    for side,target in targets.items():arm(rig,side,target, right_direction if side=="R" else (0,-.08,1))
    if kind=="Sit":
        # A quiet sleeve adjustment remains seated; the pelvis never leaves its
        # support and both hands return exactly to the lap at the loop seam.
        gesture=math.sin(math.pi*t)**2
        left=Vector(targets["L"]).lerp(Vector((-.06,.80,-.30)),gesture)
        arm(rig,"L",left)
        cuff=unity(rig.pose.bones["forearm.L"].head*.2+rig.pose.bones["forearm.L"].tail*.8)
        cuff.x+=.035;cuff.z+=.035
        right=Vector(targets["R"]).lerp(cuff,gesture)
        arm(rig,"R",right)
        targets={"L":left,"R":right}
    return targets


def construct(rig,name,phase):
    reset(rig);neutral=pose_copy(rig);contact=0.;targets=None
    if name.startswith(("Goods","Toy","Sit")):
        kind=next(k for k in ("Goods","Toy","Sit") if name.startswith(k))
        if name.endswith("Loop"):
            targets=held(rig,kind,phase);contact=1.
        else:
            targets=held(rig,kind);target_pose=pose_copy(rig)
            contact=smooth(1-phase if name.endswith("Exit") else phase)
            blend(rig,neutral,target_pose,contact)
    elif name=="Idle":
        rotate(rig,"chest",(1,0,0),.45*math.sin(phase*2*math.pi))
        rotate(rig,"head",(0,1,0),1.5*math.sin(phase*2*math.pi))
    elif name=="Walk":
        wave=math.sin(2*math.pi*phase)
        for side,sign in (("L",1),("R",-1)):
            swing=wave*sign
            rotate(rig,"thigh."+side,(1,0,0),19*swing)
            rotate(rig,"shin."+side,(1,0,0),-25*max(0,-swing))
            rotate(rig,"upper_arm."+side,(1,0,0),-14*swing)
            rotate(rig,"forearm."+side,(1,0,0),-7*abs(wave))
        move_pelvis(rig,(0,.63+.008*wave*wave,0))
    elif name in ("Turn","Stop"):
        wave=math.sin(math.pi*phase)**2
        rotate(rig,"chest",(0,1,0),(-6 if name=="Turn" else 0)*wave)
        rotate(rig,"thigh.L",(1,0,0),6*wave)
        rotate(rig,"shin.L",(1,0,0),-9*wave)
    elif name in ("AdjustSleeve","AdjustCap"):
        amount=math.sin(math.pi*phase)**2
        if name=="AdjustSleeve":
            rotate(rig,"spine",(1,0,0),-5)
            arm(rig,"L",(-.11,.87,.19))
            target=unity(rig.pose.bones["forearm.L"].head*.2+rig.pose.bones["forearm.L"].tail*.8)
            target.x+=.035;target.z+=.035
            arm(rig,"R",target)
        else:
            rotate(rig,"head",(1,0,0),8)
            arm(rig,"R",(.10,1.235,.115),hand_direction=(0,.8,.6))
        adjusted=pose_copy(rig);blend(rig,neutral,adjusted,amount)
    else:raise ValueError(name)
    return contact,targets


def snapshot(rig):
    def point(name):return [round(v,7) for v in unity(rig.pose.bones[name].head)]
    return dict(pelvis=point("pelvis"),left=point("SOCKET_Grip.L"),right=point("SOCKET_Grip.R"),
                left_foot=point("foot.L"),right_foot=point("foot.R"),head=point("head"))


def main():
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    root,rig=body.create_rig();rig.animation_data_create()
    bpy.context.scene.render.fps=FPS
    tracks=[];poses={};maximum_error=0.;lower_error=0.;signature=hashlib.sha256()
    lower=("root","pelvis","thigh.L","thigh.R","shin.L","shin.R","foot.L","foot.R")
    reset(rig);neutral={b.name:b.matrix.copy() for b in rig.pose.bones}
    for name,duration,loop in CLIPS:
        action=bpy.data.actions.new("FairChild"+name);rig.animation_data.action=action
        frames=[];end=round(duration*FPS)
        for frame in range(end+1):
            contact,targets=construct(rig,name,frame/end)
            if contact==1. and targets:
                for side,target in targets.items():maximum_error=max(maximum_error,(rig.pose.bones["SOCKET_Grip."+side].head-src(target)).length)
            if name.startswith(("Goods","Toy")):
                for bone in lower:lower_error=max(lower_error,max(abs(rig.pose.bones[bone].matrix[i][j]-neutral[bone][i][j]) for i in range(4) for j in range(4)))
            payload=snapshot(rig);payload["contact_weight"]=round(contact,7);frames.append(payload)
            for bone in rig.pose.bones:
                bone.keyframe_insert("location",frame=frame,group=bone.name)
                bone.keyframe_insert("rotation_quaternion",frame=frame,group=bone.name)
                signature.update(json.dumps([name,frame,bone.name,[round(v,7) for v in bone.location],
                    [round(v,7) for v in bone.rotation_quaternion]],separators=(",",":")).encode())
            if frame in (0,end):poses[(name,frame==end)]=pose_copy(rig)
        action.use_frame_range=True;action.frame_start=0;action.frame_end=end
        tracks.append(dict(name=name,clip="FairChild"+name,duration_seconds=duration,loop=loop,frames=frames))
        print("Authored FairChild"+name,flush=True)
    endpoint_error=0.
    for kind in ("Goods","Toy","Sit"):
        for a,end,b,start in ((kind+"Enter",True,kind+"Loop",False),(kind+"Loop",True,kind+"Exit",False),
                              (kind+"Exit",True,"Idle",False),(kind+"Enter",False,"Idle",False)):
            for bone,(pos,rot) in poses[(a,end)].items():
                other=poses[(b,start)][bone]
                endpoint_error=max(endpoint_error,(pos-other[0]).length,abs(rot.rotation_difference(other[1]).angle))
    if maximum_error>.0002 or lower_error>.00001 or endpoint_error>.0001:
        raise ValueError(f"Child action contracts: grip={maximum_error:.8f}, planted lowerbody={lower_error:.8f}, endpoints={endpoint_error:.8f}")
    payload=dict(generator="city_fair_child_actions_v1",rig="FairChild",height=1.30,fps=FPS,
        bones=[s["name"] for s in body.bone_specs()],bone_count=len(body.bone_specs()),root_motion=False,animation_events=0,
        maximum_grip_error=maximum_error,maximum_lower_body_error=lower_error,maximum_endpoint_error=endpoint_error,
        signature=signature.hexdigest(),tracks=tracks)
    if "--validate-only" in sys.argv:
        previous=json.loads(MANIFEST.read_text(encoding="utf-8"))
        if previous!=payload:raise ValueError("Child action deterministic reconstruction differs from recorded manifest")
    else:
        OUT.parent.mkdir(parents=True,exist_ok=True);BLEND.parent.mkdir(parents=True,exist_ok=True)
        rig.animation_data.action=None;reset(rig)
        body.export_actions(OUT,root,rig)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
        MANIFEST.write_text(json.dumps(payload,separators=(",",":"))+"\n",encoding="utf-8")
    print(json.dumps({k:v for k,v in payload.items() if k not in ("tracks","bones")}),flush=True)


if __name__=="__main__":main()
