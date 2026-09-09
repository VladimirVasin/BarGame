"""Optional Hero V2 outdoor household actions, on the unchanged production rig.

Run through tools/run-blender.py, with --preview or --validate-only after --.
All props and contacts are in ground-relative Unity metres. The FBX has only
bone animation, while runtime reads its matching sampled trajectories.
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

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("outdoor_hero_help",ROOT/"tools/build-village-workroom-player-actions.py")
help_source=importlib.util.module_from_spec(spec);sys.modules[spec.name]=help_source;spec.loader.exec_module(help_source)
hero,common=help_source.hero,help_source.common
FPS=24
CLIPS=(("BasketPickup",3,False),("BasketCarry",4,True),("BasketPlace",3,False),
       ("ShovelPickup",3,False),("ShovelCarry",4,True),("ShovelWork",4,True),("ShovelPutBack",3,False),
       ("LidEnter",2,False),("LidHold",4,True),("LidExit",2,False),
       ("GateEnter",1.5,False),("GateHold",4,True),("GateExit",1.5,False))
OUT=ROOT/"Assets/Resources/Player/VillageOutdoorPlayerActions.fbx"
MANIFEST=OUT.with_suffix(".json")
BLEND=ROOT/"ArtSource/Player/Blender/VillageOutdoorPlayerActions.blend"
LOWER=("root","pelvis","thigh.L","thigh.R","shin.L","shin.R","foot.L","foot.R")

def smooth(t):
    t=max(0,min(1,t));return t*t*(3-2*t)
def vec(p):return Vector(p)
def rotation(x=0,y=0,z=0):
    return Quaternion((0,1,0),math.radians(y)) @ Quaternion((1,0,0),math.radians(x)) @ Quaternion((0,0,1),math.radians(z))
def pose(p,q=None):return vec(p),q or rotation()
def mix(a,b,t):return a[0].lerp(b[0],t),a[1].slerp(b[1],t)
def point(p,local):return p[0]+p[1] @ vec(local)
def source(p):return vec((-p[0],-p[2]+.008,p[1]-.04))
def ground(p):return vec((-p[0],p[2]+.04,-p[1]+.008))
def encode_pose(p):return dict(position=[round(v,7) for v in p[0]],rotation=[round(v,7) for v in (p[1].x,p[1].y,p[1].z,p[1].w)])

def sample(action,t):
    f=dict(prop=pose((0,0,0)),left=vec((0,0,0)),right=vec((0,0,0)),left_weight=0.,right_weight=0.,
           held=False,blade_contact=False,_lean=(0.,0.,0.,0.))
    if action.startswith("Basket"):
        rest=pose((0,.38,.50));hold=pose((0,.56,.42))
        progress=1. if action=="BasketCarry" else max(0,min(1,(3-t if action=="BasketPlace" else t)/3))
        weight=smooth(progress*2);travel=smooth(progress*2-1)
        f.update(prop=mix(rest,hold,travel),left_weight=weight,right_weight=weight,held=progress>=.5,
                 _lean=(35-30*travel,0.,10-9*travel,0.))
        f["left"]=point(f["prop"],(-.29,.43,0));f["right"]=point(f["prop"],(.29,.43,0))
    elif action.startswith("Shovel"):
        rest=pose((0,0,.48));hold=pose((.10,.20,.34),rotation(-12));target=hold;lean=(27.,-4.,8.,0.)
        weight=1.;held=True
        if action in ("ShovelPickup","ShovelPutBack"):
            progress=max(0,min(1,(3-t if action=="ShovelPutBack" else t)/3))
            weight=smooth(progress*2);travel=smooth(progress*2-1);target=mix(rest,hold,travel);held=progress>=.5
            lean=tuple(a+(b-a)*travel for a,b in zip((65.,0.,15.,0.),lean))
        elif action=="ShovelWork":
            t=t%4;lower=pose((0,.025,.48));push=pose((0,.025,.57),rotation(4));lift=pose((-.10,.25,.39),rotation(-12,0,-18))
            keys=((0,hold,lean),(.7,lower,(62.,0.,15.,0.)),(1.65,push,(65.,0.,15.,0.)),
                  (2.45,lift,(30.,-4.,8.,0.)),(3.2,hold,lean),(4,hold,lean))
            for (a,p,l),(b,q,m) in zip(keys,keys[1:]):
                if a<=t<=b:
                    w=smooth((t-a)/(b-a));target=mix(p,q,w);lean=tuple(x+(y-x)*w for x,y in zip(l,m));break
            f["blade_contact"]=.7<=t<=1.65
        f.update(prop=target,left_weight=weight,right_weight=weight,held=held,_lean=lean)
        f["left"]=point(target,(0,.65,0));f["right"]=point(target,(0,1.05,0))
    elif action.startswith("Lid"):
        progress=1. if action=="LidHold" else max(0,min(1,(2-t if action=="LidExit" else t)/2))
        contact=smooth(progress*2);opening=smooth(progress*2-1)
        f.update(prop=pose((0,.80,1.02),rotation(y=180) @ rotation(-35*opening)),
                 left_weight=contact,right_weight=contact,held=progress>=.5,
                 _lean=(31-27*opening,0.,9-8*opening,0.))
        # Local lid X points opposite the hero, so anatomical grips swap.
        f["left"]=point(f["prop"],(.22,.035,.61));f["right"]=point(f["prop"],(-.22,.035,.61))
    elif action.startswith("Gate"):
        progress=1. if action=="GateHold" else max(0,min(1,(1.5-t if action=="GateExit" else t)/1.5))
        f.update(right=vec((.18,.82,.52)),right_weight=smooth(progress),held=progress>=1,
                 _lean=(65.,-3.,15.,0.),_left_rest=vec((-.22,.62,.10)))
    else:raise ValueError(action)
    return f


class OutdoorBuilder(help_source.HelpBuilder):
    def solve_contacts(self,f):
        B=common.BonePose;lean=f["_lean"]
        result=self.merge_pose(self.relaxed_pose(),{
            "spine":B(rotation_degrees=(lean[0],lean[1],0)),
            "chest":B(rotation_degrees=(lean[2],lean[3],0)),
            "neck":B(rotation_degrees=(-5.,0.,0.)),"head":B(rotation_degrees=(9.,0.,0.))})
        self._reset_pose();self._apply_pose(result);rig=self.result.rig
        for side,sign,key in (("L",1,"left"),("R",-1,"right")):
            if f[key+"_weight"]<=0 and "_"+key+"_rest" not in f:continue
            target=source(f.get("_"+key+"_rest",f[key]));hand_direction=vec((-sign*.06,-.05,-1)).normalized()
            grip_bone=rig.data.bones["SOCKET_Grip."+side];hand_bone=rig.data.bones["hand."+side]
            grip_distance=(grip_bone.head_local-hand_bone.head_local).length
            wrist=target-hand_direction*grip_distance;shoulder=rig.pose.bones["upper_arm."+side].head.copy()
            a=rig.data.bones["upper_arm."+side].length;b=rig.data.bones["forearm."+side].length
            delta=wrist-shoulder;distance=delta.length
            if distance>=a+b-.003:raise ValueError(f"{side} unreachable {tuple(f[key])}: {distance:.4f}/{a+b:.4f}; lean {lean}")
            axis=delta.normalized();hint=vec((sign*.45,.55,-.3));bend=(hint-axis*hint.dot(axis)).normalized()
            along=(a*a-b*b+distance*distance)/(2*distance);elbow=shoulder+axis*along+bend*math.sqrt(max(0,a*a-along*along))
            result["upper_arm."+side]=B(armature_direction=tuple(elbow-shoulder))
            result["forearm."+side]=B(armature_direction=tuple(wrist-elbow))
            result["hand."+side]=B(armature_direction=tuple(hand_direction))
        return result

    def build_actions(self):
        self.samples={};neutral=self.relaxed_pose()
        for action,duration,loop in CLIPS:
            keys=[];frames=[]
            for frame in range(round(duration*FPS)+1):
                seconds=frame/FPS;f=sample(action,seconds);w=max(f["left_weight"],f["right_weight"])
                try:held=self.solve_contacts(f)
                except ValueError as e:raise ValueError(f"{action} frame {frame}: {e}") from e
                value=neutral if w==0 else held if w==1 else self.blended(neutral,held,w)
                keys.append((seconds/duration,value))
                frames.append({key:encode_pose(value) if key=="prop" else [round(x,7) for x in value] if isinstance(value,Vector) else value
                    for key,value in f.items() if not key.startswith("_")})
            name="Village"+action
            self._create_action(name,"village_outdoor",duration,loop,round(duration*FPS),FPS,keys)
            self.samples[action]=frames
            print("Authored "+name,flush=True)


def validate(builder):
    rig=builder.result.rig;grip_error=0.;lower_error=0.;endpoint_error=0.;count=0
    def snapshot(action,progress):
        clip=builder.result.actions["Village"+action].action;rig.animation_data.action=clip
        frame=clip.frame_end*progress;bpy.context.scene.frame_set(int(frame),subframe=frame%1);bpy.context.view_layer.update()
        return {b.name:b.matrix.copy() for b in rig.pose.bones}
    neutral=snapshot("BasketPickup",0)
    for action,duration,loop in CLIPS:
        for index,f in enumerate(builder.samples[action]):
            current=snapshot(action,index/(duration*FPS));count+=1
            for side,key in (("L","left"),("R","right")):
                if f[key+"_weight"]>=.9999:
                    grip_error=max(grip_error,(rig.pose.bones["SOCKET_Grip."+side].head-source(f[key])).length)
            for bone in LOWER:
                lower_error=max(lower_error,max(abs(current[bone][i][j]-neutral[bone][i][j]) for i in range(4) for j in range(4)))
        for curve in common.iter_action_fcurves(builder.result.actions["Village"+action].action):
            if not curve.data_path.startswith('pose.bones['):raise ValueError("Object/root motion is forbidden")
    endpoints=(("BasketPickup",1,"BasketCarry",0),("BasketCarry",1,"BasketPlace",0),
        ("BasketPlace",1,"BasketPickup",0),("ShovelPickup",1,"ShovelCarry",0),
        ("ShovelCarry",1,"ShovelWork",0),("ShovelWork",1,"ShovelPutBack",0),
        ("ShovelPutBack",1,"BasketPickup",0),("LidEnter",1,"LidHold",0),("LidHold",1,"LidExit",0),
        ("LidExit",1,"BasketPickup",0),("GateEnter",1,"GateHold",0),("GateHold",1,"GateExit",0),
        ("GateExit",1,"BasketPickup",0))
    for a,t,b,u in endpoints:
        first=snapshot(a,t);second=snapshot(b,u)
        endpoint_error=max(endpoint_error,max(abs(first[n][i][j]-second[n][i][j]) for n in first for i in range(4) for j in range(4)))
    # The safe dock is uphill of the leaf: its physical handle can be lower
    # than the canonical frame. Reserve actual arm reach for the final IK,
    # instead of stretching a canonical upright pose across that terrain gap.
    snapshot("GateHold",0)
    upper=rig.pose.bones["upper_arm.R"];forearm=rig.pose.bones["forearm.R"]
    hand=rig.pose.bones["hand.R"];offset=rig.pose.bones["SOCKET_Grip.R"].head-hand.head
    limit=upper.length+forearm.length-.001
    gate_margin=min(limit-(source((.18,y,.52))-offset-upper.head).length for y in (.60,.618,.70,.82,.85))
    if gate_margin<.03:raise ValueError(f"Gate terrain reach margin too small: {gate_margin}")
    snapshot("BasketPickup",0);pelvis=list(ground(rig.pose.bones["pelvis"].head))
    rig.animation_data.action=None;builder._reset_pose()
    report=dict(sampled_frames=count,maximum_grip_error=grip_error,maximum_lower_body_error=lower_error,maximum_endpoint_error=endpoint_error,
                gate_handle_height_range=[.60,.85],minimum_gate_reach_margin=gate_margin)
    if grip_error>.0002 or lower_error>.00001 or endpoint_error>.00001:raise ValueError(str(report))
    return report,pelvis


def render_previews(builder):
    data=json.loads((ROOT/"Assets/Resources/VillageLife/VillageLifeProps3D.json").read_text(encoding="utf8"))
    definitions={p["kind"]:p for p in data["props"]}
    conversion=Matrix(((-1,0,0),(0,0,-1),(0,1,0))).to_4x4()
    swap=Matrix(((1,0,0),(0,0,1),(0,1,0))).to_4x4()
    def transform(p):return Matrix.Translation(vec((0,.008,-.04))) @ conversion @ Matrix.Translation(p[0]) @ p[1].to_matrix().to_4x4() @ swap
    for action,terrain in (("BasketCarry",False),("ShovelWork",False),("LidHold",False),("GateHold",False),("GateHold",True)):
        clip=builder.result.actions["Village"+action].action;builder.result.rig.animation_data.action=clip
        seconds=clip.frame_end*.30/24;bpy.context.scene.frame_set(round(.30*clip.frame_end));f=sample(action,seconds)
        if terrain:
            config=common.BuildConfig(BLEND,None,None,MANIFEST,None,None,OUT,1.75,20260909,"apose")
            poser=OutdoorBuilder(config,hero.DEFAULT_FACE_ATLAS,hero.DEFAULT_CLOTHING_ATLAS);poser.result=builder.result
            builder.result.rig.animation_data.action=None;f["right"].y=.618
            posed=poser.solve_contacts(f);poser._reset_pose();poser._apply_pose(posed)
        instances=[("Basket",f["prop"])] if action=="BasketCarry" else [("Shovel",f["prop"])] if action=="ShovelWork" else (
            [("StationCrate",pose((0,0,.72),rotation(y=180))),("StationLid",f["prop"])] if action=="LidHold" else
            [("GateLeaf",pose((-.79,-.202 if terrain else 0,.455)))])
        appended=[]
        for kind,p in instances:
            parts=definitions[kind]["parts"]
            with bpy.data.libraries.load(str(ROOT/"ArtSource/VillageLife/VillageLifeProps3D.blend"),link=False) as (src,dst):
                dst.objects=[part["mesh"] for part in parts]
            for obj,part in zip(dst.objects,parts):
                bpy.context.scene.collection.objects.link(obj);obj.parent=None;obj.matrix_world=transform(p);appended.append(obj)
                material=bpy.data.materials.new("Review "+part["surface"]);material.use_nodes=True
                shader=material.node_tree.nodes.get("Principled BSDF");shader.inputs["Base Color"].default_value=part["tint"]
                shader.inputs["Roughness"].default_value=.88;obj.data.materials.clear();obj.data.materials.append(material)
        scene=bpy.context.scene;scene.render.resolution_x=850;scene.render.resolution_y=900;scene.render.resolution_percentage=100
        scene.render.filepath=str(BLEND.with_name("VillageOutdoor"+action+("Terrain" if terrain else "")+".png"));bpy.ops.render.render(write_still=True)
        for obj in appended:bpy.data.objects.remove(obj,do_unlink=True)


def main():
    if "--probe-gate" in sys.argv:
        # Read-only reach envelope on the exported production rig, including
        # lower terrain-relative handles. Never exports or saves this scene.
        bpy.ops.wm.open_mainfile(filepath=str(BLEND))
        config=common.BuildConfig(BLEND,None,None,MANIFEST,None,None,OUT,1.75,20260909,"apose")
        builder=OutdoorBuilder(config,hero.DEFAULT_FACE_ATLAS,hero.DEFAULT_CLOTHING_ATLAS)
        rig=bpy.data.objects["RIG_Player"];rig.animation_data.action=None
        builder.result=common.BuildResult(rig.parent,rig,{},{});
        for spine,chest in ((35,10),(45,12),(50,13),(55,14),(60,15),(65,15),(70,15)):
            f=sample("GateHold",0);f["_lean"]=(spine,-3.,chest,0.)
            posed=builder.solve_contacts(f);builder._reset_pose();builder._apply_pose(posed)
            shoulder=rig.pose.bones["upper_arm.R"];arm=rig.pose.bones["forearm.R"];hand=rig.pose.bones["hand.R"]
            socket=rig.pose.bones["SOCKET_Grip.R"];offset=socket.head-hand.head;limit=shoulder.length+arm.length-.001
            margins={str(drop):round(limit-(source(f["right"]+vec((0,-drop,0)))-offset-shoulder.head).length,5)
                     for drop in (0,.10,.20,.25,.30)}
            print("Gate reach",spine,chest,"shoulder",[round(x,5) for x in ground(shoulder.head)],"margin",margins,flush=True)
        return
    if "--preview-only" in sys.argv:
        bpy.ops.wm.open_mainfile(filepath=str(BLEND))
        # The saved bank contains the same production review geometry.
        class Record:
            def __init__(self,action):self.action=action
        class Result:pass
        class Builder:pass
        builder=Builder();builder.result=Result();builder.result.rig=bpy.data.objects["RIG_Player"]
        builder.result.actions={a.name:Record(a) for a in bpy.data.actions if a.name.startswith("Village")}
        render_previews(builder);return
    protected=[ROOT/"Assets/Resources/Player/Player3DV2.prefab"]
    protected+=list((ROOT/"Assets/Resources/Player").glob("*.fbx"))
    protected+=list((ROOT/"Assets/Player3D/V2").rglob("*.fbx"))
    protected=[p for p in protected if p!=OUT]
    before={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    config=common.BuildConfig(BLEND,None,None,MANIFEST,None,None,OUT,1.75,20260909,"apose")
    builder=OutdoorBuilder(config,hero.DEFAULT_FACE_ATLAS,hero.DEFAULT_CLOTHING_ATLAS);builder.build()
    report,pelvis=validate(builder);tracks=[];signature=hashlib.sha256()
    for action,duration,loop in CLIPS:
        tracks.append(dict(name=action,clip="Village"+action,duration_seconds=duration,loop=loop,frames=builder.samples[action]))
        for c in common.iter_action_fcurves(builder.result.actions["Village"+action].action):
            signature.update(json.dumps([action,c.data_path,c.array_index,[[round(x,7) for x in p.co] for p in c.keyframe_points]],separators=(",",":")).encode())
    payload=dict(generator="village_outdoor_player_v1",rig="HeroV2",bone_count=31,fps=FPS,root_motion=False,animation_events=0,
        entry_ground_offset=[0,0,0],exit_ground_offset=[0,0,0],entry_facing=[0,0,1],exit_facing=[0,0,1],
        entry_pelvis_from_ground=pelvis,action_pelvis_from_ground=pelvis,exit_pelvis_from_ground=pelvis,
        pickup_contact_seconds=1.5,signature=signature.hexdigest(),tracks=tracks,preserved_sources=before,**report)
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text(encoding="utf8"))!=payload:raise ValueError("Non-deterministic outdoor bank")
    else:
        common.export_animation_fbx(OUT,builder.result);common.save_blend(BLEND)
        MANIFEST.write_text(json.dumps(payload,separators=(",",":"))+"\n",encoding="utf8")
        for p in (OUT,MANIFEST):
            meta=p.with_name(p.name+".meta")
            if not meta.exists():meta.write_text("fileFormatVersion: 2\nguid: "+hashlib.sha256(p.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]+"\n",encoding="utf8")
    if "--preview" in sys.argv:
        render_previews(builder)
    if before!={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in before}:raise ValueError("An existing hero asset changed")
    print(json.dumps(report),flush=True)

if __name__=="__main__":main()
