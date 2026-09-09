#!/usr/bin/env python3
"""Additional, fixed-metre workroom actions. Never re-export a resident body.

Use build-village-residents-3d-model.py --workroom through run-blender.py.
Prop trajectories and hand contacts are emitted from the same sampled source
as the animation bank; the runtime does not duplicate their authoring maths.
"""
from __future__ import annotations
import hashlib
import json
import math
from pathlib import Path
import bpy
from mathutils import Matrix, Quaternion, Vector

CLIPS=(("RepairTakeTool",3.,False),("RepairWork",4.,True),("RepairPutTool",3.,False),
       ("SewingEnter",3.,False),("SewingUnpack",6.,False),("SewingWork",4.,True),
       ("SewingFold",3.,False),("SewingStow",5.,False),("SewingExit",3.,False))
HAMMER_STRIKE=Vector((-.095,.265,0))
CHAIR_JOINT=Vector((-.16,.90,.48))
CHAIR_SUPPORT=Vector((-.27,.90,.48))
BOX_TABLE=Vector((.37,.666,.35))
BOX_REST=Vector((.55,.46,.12))
CLOTH_TABLE=Vector((0,.674,.38))
BOX_PACKET=Vector((-.09,.04,0))
MITTEN_PACKET=Vector((.08,.016,0))
MITTEN_GRIP=Vector((-.065,.015,0))
MITTEN_WORK=Vector((-.035,.695,.38))
BOX_GRIPS={"L":Vector((-.14,.10,-.16)),"R":Vector((.14,.10,-.16))}
PACKET_GRIPS={"L":Vector((.15,.0475,-.08)),"R":Vector((.15,.0475,.08))}
HINGE=Vector((0,.15,.18))
LID_GRIP=Vector((0,.015,-.31))
FOLD_HINGE=Vector((0,.025,0))
FOLD_GRIP=Vector((-.15,-.0225,-.08))
CONTACT={"None":0,"Hammer":1,"Chair":2,"Mitten":3,"Cloth":4,"Box":5,"BoxLid":6,"ClothFlap":7,"Thread":8}


def smooth(t):
    t=max(0,min(1,t));return t*t*(3-2*t)


def between(t,start,end):return smooth((t-start)/(end-start))
def lerp(a,b,t):return Vector(a).lerp(Vector(b),t)
def rot(x=0.,y=0.,z=0.):
    return Quaternion((0,1,0),math.radians(y)) @ Quaternion((1,0,0),math.radians(x)) @ Quaternion((0,0,1),math.radians(z))
def pose(point,rotation=None):return (Vector(point),rotation or rot())
def mix_pose(a,b,t):return a[0].lerp(b[0],t),a[1].slerp(b[1],t)
def point(p,local):return p[0]+p[1] @ Vector(local)
def out_pose(p):return {"position":[round(x,7) for x in p[0]],"rotation":[round(x,7) for x in (p[1].x,p[1].y,p[1].z,p[1].w)]}


def frame_default():
    box=pose(BOX_REST,rot(y=90));cloth=pose(point(box,BOX_PACKET),box[1])
    return {"hammer":pose((.18,.90,.40),rot(90)),"box":box,"cloth":cloth,
        "mitten":pose(point(cloth,MITTEN_PACKET),cloth[1]),"fold_degrees":-180.,"lid_degrees":0.,
        "right_contact":Vector((0,0,0)),"left_contact":Vector((0,0,0)),
        "right_weight":0.,"left_weight":0.,"right_kind":0,"left_kind":0,
        "hammer_held":False,"mitten_held":False,"cloth_held":False,"box_held":False,
        "_sit":1.,"_lean":14.,"_side":0.,"_hammer_wrist":0.,"_reach_wrist":0.}


def contact(f,side,target,kind,weight=1.):
    prefix="left" if side=="L" else "right"
    f[prefix+"_contact"]=Vector(target);f[prefix+"_weight"]=weight;f[prefix+"_kind"]=CONTACT[kind]


def lap(f):
    for side,sign in (("L",-1),("R",1)):contact(f,side,(sign*.14,.57,.11),"None",1.)


def box_hands(f,weight=1.):
    contact(f,"L",(-.14,.57,.11),"None")
    contact(f,"R",point(f["box"],BOX_GRIPS["R"]),"Box",weight)


def packet_hands(f,weight=1.):
    for side in ("L","R"):contact(f,side,point(f["cloth"],PACKET_GRIPS[side]),"Cloth",weight)


def lid_hand(f,weight=1.):
    contact(f,"R",point(f["box"],HINGE+rot(f["lid_degrees"]) @ LID_GRIP),"BoxLid",weight)
    contact(f,"L",(-.14,.57,.11),"None")


def flap_hand(f,weight=1.):
    contact(f,"L",point(f["cloth"],FOLD_HINGE+rot(z=f["fold_degrees"]) @ FOLD_GRIP),"ClothFlap",weight)
    contact(f,"R",point(f["cloth"],(.135,.014,.09)),"Cloth",weight)


def work_hands(f,time=0.):
    contact(f,"L",point(f["mitten"],MITTEN_GRIP),"Mitten")
    # Large, legible pulls at the thick mitten's rim. No microscopic needle.
    pull=math.sin(math.pi*(time%2)/2)**2
    contact(f,"R",point(f["mitten"],(.055,.030+.035*pull,.020+.025*pull)),"Thread")


def move_hands(f,first,last,weight):
    first(f);initial={s:f[s+"_contact"].copy() for s in ("left","right")}
    last(f)
    for side in ("left","right"):
        f[side+"_contact"]=lerp(initial[side],f[side+"_contact"],weight)
        f[side+"_weight"]=1.
        if weight<.9999:f[side+"_kind"]=0


def _sample(name,t):
    f=frame_default()
    if name.startswith("Repair"):
        f["_sit"]=0.;f["_lean"]=35.
        held=pose(CHAIR_JOINT-rot(z=125) @ HAMMER_STRIKE,rot(z=125))
        rest=f["hammer"]
        p=t if name=="RepairTakeTool" else 3-t
        weight=1.
        if name=="RepairWork":
            t=t%4
            # Three measured strikes with a brief rest at the joint after each.
            lift=0.
            for begin,peak,hit in ((0.,.45,.8),(1.05,1.5,1.85),(2.1,2.55,2.9)):
                if begin<=t<peak:lift=between(t,begin,peak)
                elif peak<=t<hit:lift=1-between(t,peak,hit)
            f["hammer"]=pose(held[0],rot(z=125-40*lift))
        else:
            weight=between(p,0,1.5);f["_lean"]*=weight
            f["hammer"]=mix_pose(rest,held,between(p,1.5,3.))
        f["hammer_held"]=name=="RepairWork" or p>=1.5
        f["_hammer_wrist"]=between(p,1.5,3.) if name!="RepairWork" else 1.
        contact(f,"R",f["hammer"][0],"Hammer",weight)
        contact(f,"L",CHAIR_SUPPORT,"Chair",weight)
        return f
    if name in ("SewingEnter","SewingExit"):
        p=t if name=="SewingEnter" else 3-t
        f["_sit"]=between(p,0,2.3);f["_lean"]=14.*f["_sit"]
        lap(f)
        for side in ("right","left"):f[side+"_weight"]=between(p,.7,2.3)
        return f
    f["box"]=pose(BOX_TABLE,rot(y=90));f["cloth"]=pose(CLOTH_TABLE)
    f["mitten"]=pose(MITTEN_WORK);f["lid_degrees"]=105.;f["fold_degrees"]=0.;f["_lean"]=30.
    if name=="SewingWork":
        f["mitten_held"]=True;work_hands(f,t);return f
    if name=="SewingFold":
        f["mitten"]=pose(lerp(MITTEN_WORK,CLOTH_TABLE+MITTEN_PACKET,between(t,0,1.)))
        f["mitten_held"]=t<1.
        if t<=1.:
            work_hands(f)
        elif t<1.4:
            move_hands(f,work_hands,flap_hand,between(t,1.,1.4))
        elif t<2.7:
            f["fold_degrees"]=-180*between(t,1.4,2.7);flap_hand(f)
        else:
            f["fold_degrees"]=-180.;flap_hand(f)
            before={side:f[side+"_contact"].copy() for side in ("left","right")}
            packet_hands(f)
            for side in ("left","right"):f[side+"_contact"]=lerp(before[side],f[side+"_contact"],between(t,2.7,3.))
        return f
    if name=="SewingUnpack":
        f["_reach_wrist"]=between(t,1.25,1.4)*(1-between(t,2.1,2.7))
        f["_lean"]=14+16*between(t,0,.5)
        f["_side"]=20*between(t,0,.5)*(1-between(t,3.,4.))
        f["box"]=pose(lerp(BOX_REST,BOX_TABLE,between(t,.5,1.25)),rot(y=90))
        f["lid_degrees"]=105*between(t,1.4,2.1)
        box_packet=pose(point(f["box"],BOX_PACKET),f["box"][1])
        f["cloth"]=mix_pose(box_packet,pose(CLOTH_TABLE),between(t,2.7,3.65))
        f["fold_degrees"]=-180*(1-between(t,4.,4.8))
        f["mitten"]=mix_pose(pose(point(f["cloth"],MITTEN_PACKET),f["cloth"][1]),pose(MITTEN_WORK),between(t,5.2,6.))
        f["box_held"]=.5<=t<1.25;f["cloth_held"]=2.7<=t<3.65;f["mitten_held"]=t>=5.2
        if t<.5:box_hands(f,between(t,0,.5))
        elif t<1.25:box_hands(f)
        elif t<1.4:move_hands(f,box_hands,lid_hand,between(t,1.25,1.4))
        elif t<2.1:lid_hand(f)
        elif t<2.7:move_hands(f,lid_hand,packet_hands,between(t,2.1,2.7))
        elif t<3.65:packet_hands(f)
        elif t<4.:move_hands(f,packet_hands,flap_hand,between(t,3.65,4.))
        elif t<4.8:flap_hand(f)
        else:
            if t<5.2:move_hands(f,flap_hand,work_hands,between(t,4.8,5.2))
            else:work_hands(f)
        return f
    if name=="SewingStow":
        f["_reach_wrist"]=between(t,1.3,1.8)*(1-between(t,2.55,3.3))
        f["_side"]=20*between(t,0,1.)*(1-between(t,4.3,5.))
        f["_lean"]=30-16*between(t,4.3,5.)
        f["box"]=pose(lerp(BOX_TABLE,BOX_REST,between(t,3.3,4.3)),rot(y=90))
        f["cloth"]=mix_pose(pose(CLOTH_TABLE),pose(point(f["box"],BOX_PACKET),f["box"][1]),between(t,0,1.3))
        f["mitten"]=pose(point(f["cloth"],MITTEN_PACKET),f["cloth"][1])
        f["fold_degrees"]=-180.;f["lid_degrees"]=105*(1-between(t,1.8,2.55))
        f["cloth_held"]=t<1.3;f["box_held"]=3.3<=t<4.3
        if t<1.3:packet_hands(f)
        elif t<1.8:move_hands(f,packet_hands,lid_hand,between(t,1.3,1.8))
        elif t<2.55:lid_hand(f)
        elif t<3.3:move_hands(f,lid_hand,box_hands,between(t,2.55,3.3))
        elif t<4.3:box_hands(f)
        else:
            move_hands(f,box_hands,lap,between(t,4.3,5.))
        return f
    raise ValueError(name)


def sample(name,t):
    f=_sample(name,t)
    f["thread_start"]=point(f["mitten"],(.055,.022,.020))
    f["thread_end"]=f["right_contact"].copy() if f["right_kind"]==CONTACT["Thread"] else f["thread_start"]+Vector((0,.008,0))
    f["thread_visible"]=f["mitten_held"] and f["right_kind"]==CONTACT["Thread"]
    return f


def solve_leg(base,rig,side,target):
    thigh=rig.pose.bones["thigh."+side];shin=rig.pose.bones["shin."+side];foot=rig.pose.bones["foot."+side]
    hip=thigh.head.copy();delta=target-hip;distance=delta.length;a=thigh.length;b=shin.length
    if distance>a+b+.0001:raise RuntimeError(f"Seated {side} ankle is unreachable: {distance}/{a+b}")
    distance=min(distance,a+b-.00001);axis=delta.normalized();pole=Vector((0,-1,0))
    bend=(pole-axis*pole.dot(axis)).normalized();along=(a*a-b*b+distance*distance)/(2*distance)
    knee=hip+axis*along+bend*math.sqrt(max(0,a*a-along*along))
    for bone,tip in ((thigh,knee),(shin,target)):
        direction=(tip-bone.head).normalized();rest=(bone.bone.tail_local-bone.bone.head_local).normalized()
        q=rest.rotation_difference(direction) @ bone.bone.matrix_local.to_quaternion()
        bone.matrix=Matrix.Translation(bone.head.copy()) @ q.to_matrix().to_4x4();bpy.context.view_layer.update()
    foot.matrix=Matrix.Translation(target) @ foot.bone.matrix_local.to_quaternion().to_matrix().to_4x4()
    bpy.context.view_layer.update()


def author_frame(v,result,name,seconds,overrides=None):
    f=sample(name,seconds);base=v.base;rig=result.rig;repair=name.startswith("Repair")
    if overrides:f.update(overrides)
    scale=v.LIFE_HEIGHTS["RepairNeighbor" if repair else "SewingWoman"]/1.75
    body=dict(base.CITIZEN_HANGING_ARMS)
    body["spine"]=base.BonePose(rotation_degrees=(f["_lean"],0,f["_side"]))
    body["neck"]=base.BonePose(rotation_degrees=(6 if repair else 4,0,0))
    base.reset_pose(rig);base.apply_pose(rig,body)
    if not repair and f["_sit"]>0:
        pelvis=rig.pose.bones["pelvis"];m=pelvis.matrix.copy()
        target=v.world_target((0,.50,-.20),scale)
        m.translation=m.translation.lerp(target,f["_sit"]);pelvis.matrix=m;bpy.context.view_layer.update()
        for side in ("L","R"):
            solve_leg(base,rig,side,rig.data.bones["foot."+side].head_local.copy())
    deps=bpy.context.evaluated_depsgraph_get()
    low=min(base.evaluated_part_min_z(p,deps) for p in result.parts if p.bone.startswith("foot."))
    pelvis=rig.pose.bones["pelvis"];m=pelvis.matrix.copy();m.translation.z-=low;pelvis.matrix=m;bpy.context.view_layer.update()
    if repair and f["_hammer_wrist"]>0:
        # Transform the actual mitten and thumb around the handle as it turns.
        conversion=Matrix(((-1,0,0),(0,0,-1),(0,1,0)))
        delta=f["hammer"][1] @ rot(90).inverted()
        q=(conversion @ delta.to_matrix() @ conversion.inverted()).to_quaternion()
        hand=rig.pose.bones["hand.R"];hand.matrix=Matrix.Translation(hand.head.copy()) @ (q @ hand.matrix.to_quaternion()).to_matrix().to_4x4()
        bpy.context.view_layer.update()
    if not repair and f["_reach_wrist"]>0:
        hand=rig.pose.bones["hand.R"];socket=rig.pose.bones["SOCKET_Grip.R"]
        current=socket.head-hand.head;desired=v.world_target(f["right_contact"],scale)-rig.pose.bones["upper_arm.R"].head
        current_q=hand.matrix.to_quaternion();aimed=current.rotation_difference(desired) @ current_q
        hand.matrix=Matrix.Translation(hand.head.copy()) @ current_q.slerp(aimed,f["_reach_wrist"]).to_matrix().to_4x4()
        bpy.context.view_layer.update()
    for side,prefix in (("L","left"),("R","right")):
        weight=f[prefix+"_weight"]
        if weight<=0 and (repair or f["_sit"]<.9999):continue
        desired=v.world_target(f[prefix+"_contact"],scale)
        initial=v.world_target(((-1 if side=="L" else 1)*.14,.57,.11),scale) if not repair and f["_sit"]>=.9999 else rig.pose.bones["SOCKET_Grip."+side].head
        target=initial.lerp(desired,weight)
        if not repair:
            # A hand reaching across to a box turns with its forearm. Forcing
            # the idle mitten to point down makes a reachable edge appear to
            # require an overlong arm. Aim only as much as the wrist needs,
            # with the anatomical upper/lower lengths left unchanged.
            hand=rig.pose.bones["hand."+side];socket=rig.pose.bones["SOCKET_Grip."+side]
            upper=rig.pose.bones["upper_arm."+side];forearm=rig.pose.bones["forearm."+side]
            q=hand.matrix.to_quaternion();local=q.inverted() @ (socket.head-hand.head)
            reach=upper.length+forearm.length-.008
            if (target-q @ local-upper.head).length>reach:
                aimed=(q @ local).rotation_difference(target-upper.head) @ q
                lo=0.;hi=1.
                for iteration in range(18):
                    mid=(lo+hi)/2;candidate=q.slerp(aimed,mid)
                    if (target-candidate @ local-upper.head).length>reach:lo=mid
                    else:hi=mid
                hand.matrix=Matrix.Translation(hand.head.copy()) @ q.slerp(aimed,hi).to_matrix().to_4x4()
                bpy.context.view_layer.update()
        v.solve_grips(rig,{side:target},weight>=.9999)
    return f,scale


def render_previews(v,assets,sources):
    """Review the unchanged bodies with actual workroom meshes and sampled poses.

    This branch writes PNGs only: it never exports bodies, clips or prop assets.
    """
    data=json.loads((assets/"VillageWorkroom3D.json").read_text(encoding="utf8"))
    rows={p["name"]:p for p in data["parts"]}
    swap=Matrix(((1,0,0),(0,0,1),(0,1,0))).to_4x4()
    conversion=Matrix(((-1,0,0),(0,0,-1),(0,1,0))).to_4x4()
    def matrix(p):return Matrix.Translation(p[0]) @ p[1].to_matrix().to_4x4()
    for action,seconds,label in (("RepairWork",.8,"RepairStrike"),("RepairWork",.35,"RepairLift"),
            ("SewingWork",.5,"SewingWork"),("SewingFold",2.,"SewingFold"),("SewingStow",3.8,"SewingBox")):
        repair=action.startswith("Repair");role="RepairNeighbor" if repair else "SewingWoman"
        result=v.LifeResidentBuilder(role).build();frame,scale=author_frame(v,result,action,seconds)
        result.root.scale=(scale,scale,scale)
        selected=("Workbench","ChairFrame","RepairRail","RepairClamp","Hammer","HammerHead") if repair else (
            "SewingTable","SewingSeat","BoxShelf","Box","BoxLid","Cloth","ClothFlap","Mitten","Thread")
        with bpy.data.libraries.load(str(sources/"VillageWorkroom3D.blend"),link=False) as (source,target):
            target.objects=[rows[name]["mesh"] for name in selected]
        dock=pose((-2.02,0,-.97),rot(y=180)) if repair else pose((-2.10,0,.98))
        transforms={}
        for name,obj in zip(selected,target.objects):
            row=rows[name];bpy.context.scene.collection.objects.link(obj);obj.parent=None
            dynamic={"Hammer":"hammer","Mitten":"mitten","Cloth":"cloth","Box":"box"}
            if name in dynamic:transform=matrix(frame[dynamic[name]])
            elif name=="HammerHead":transform=transforms["Hammer"]
            elif name=="BoxLid":transform=transforms["Box"] @ matrix(pose(HINGE,rot(frame["lid_degrees"])))
            elif name=="ClothFlap":transform=transforms["Cloth"] @ matrix(pose(FOLD_HINGE,rot(z=frame["fold_degrees"])))
            elif name=="Thread":
                delta=frame["thread_end"]-frame["thread_start"]
                transform=matrix(pose(frame["thread_start"],Vector((0,1,0)).rotation_difference(delta))) @ Matrix.Diagonal((1,delta.length,1,1))
                obj.hide_render=not frame["thread_visible"]
            else:transform=matrix(dock).inverted() @ matrix(pose(row["position"],rot(*row["euler"])))
            transforms[name]=transform;obj.matrix_world=conversion @ transform @ swap
            for mat in obj.data.materials:
                mat.use_nodes=True;shader=mat.node_tree.nodes.get("Principled BSDF")
                if shader:shader.inputs["Base Color"].default_value=mat.diffuse_color;shader.inputs["Roughness"].default_value=.9
        mat=result.material;nodes=mat.node_tree.nodes;shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
        tint=nodes.new("ShaderNodeObjectInfo");texture=nodes.new("ShaderNodeTexImage")
        texture.image=bpy.data.images.load(str(assets/(role+"Atlas.png")));texture.interpolation="Closest"
        mix=nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY";mix.inputs[0].default_value=1
        mat.node_tree.links.new(tint.outputs["Color"],mix.inputs[1]);mat.node_tree.links.new(texture.outputs["Color"],mix.inputs[2])
        mat.node_tree.links.new(mix.outputs[0],shader.inputs["Base Color"])
        scene=bpy.context.scene;scene.render.resolution_x=960;scene.render.resolution_y=960
        scene.world.color=(.22,.24,.26)
        for location,power,size in (((-3,-4,5),800,5),((3,-1,3),500,4),((0,3,3),700,3)):
            light=bpy.data.lights.new("ReviewSoftbox","AREA");light.energy=power;light.shape="DISK";light.size=size
            obj=bpy.data.objects.new("ReviewSoftbox",light);scene.collection.objects.link(obj);obj.location=location
            obj.rotation_euler=(Vector((0,0,.9))-obj.location).to_track_quat("-Z","Y").to_euler()
        camera=bpy.data.objects.new("ReviewCamera",bpy.data.cameras.new("ReviewCamera"));scene.collection.objects.link(camera)
        camera.location=(-3.2,-1.0,2.1) if repair else (2.6,-3.5,1.9)
        camera.rotation_euler=(Vector((-.12,-.20,.85 if repair else .70))-camera.location).to_track_quat("-Z","Y").to_euler()
        camera.data.type="ORTHO";camera.data.ortho_scale=2.1 if repair else 1.65;scene.camera=camera
        scene.render.filepath=str(sources/("VillageWorkroom"+label+".png"));bpy.ops.render.render(write_still=True)
        print("Rendered workroom "+label,flush=True)


def build(v,args,assets,sources):
    if args.preview_only:
        render_previews(v,assets,sources)
        return
    if getattr(args,"workroom_probe",False):
        result=v.LifeResidentBuilder("SewingWoman").build()
        for lean in (14,24,36,48):
            for side in (-45,-30,0,30,45):
                try:author_frame(v,result,"SewingUnpack",.5,{"_lean":lean,"_side":side});error="OK"
                except RuntimeError as issue:error=str(issue)
                shoulder=result.rig.pose.bones["upper_arm.L"].head
                print("Probe",lean,side,"LShoulder",tuple(round(a,4) for a in shoulder),error,flush=True)
        return
    protected=[assets/(role+suffix) for role in v.ROLE_NAMES+v.LIFE_ROLES for suffix in (".fbx",".json","Atlas.png")]
    protected += [assets/(bank+suffix) for bank in ("VillageResidentActions","VillageResidentLifeActions") for suffix in (".fbx",".json")]
    protected += [sources/(role+suffix) for role in v.ROLE_NAMES+v.LIFE_ROLES for suffix in (".blend",".png")]
    hashes={str(p.relative_to(v.ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    result=v.LifeResidentBuilder("SewingWoman").build();rig=result.rig;curves=[];tracks=[];samples=0;grip_error=0.;floor_error=0.
    for name,duration,loop in CLIPS:
        action=bpy.data.actions.new(name);action.use_fake_user=True;action.use_frame_range=True
        end=round(duration*24);action.frame_start=0;action.frame_end=end;action.use_cyclic=loop
        rig.animation_data_create().action=action;previous={};frames=[]
        for frame in range(end+1):
            t=frame/24
            try:f,scale=author_frame(v,result,name,t)
            except RuntimeError as error:raise RuntimeError(f"{name} frame{frame}: {error}") from error
            frames.append({key:(out_pose(value) if key in ("hammer","mitten","cloth","box") else
                [round(x,7) for x in value] if isinstance(value,Vector) else value)
                for key,value in f.items() if not key.startswith("_")})
            for bone in rig.pose.bones:
                bone.rotation_mode="QUATERNION"
                if bone.name in previous:bone.rotation_quaternion.make_compatible(previous[bone.name])
                previous[bone.name]=bone.rotation_quaternion.copy()
                for channel in ("location","rotation_quaternion","scale"):bone.keyframe_insert(channel,frame=frame,group=bone.name)
        for curve in v.base.iter_action_fcurves(action):
            for key in curve.keyframe_points:key.interpolation="LINEAR"
            curves.append((name,curve.data_path,curve.array_index,[[round(k.co.x,7),round(k.co.y,7)] for k in curve.keyframe_points]))
        for frame in range(end+1):
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update();f=sample(name,frame/24);samples+=1
            scale=v.LIFE_HEIGHTS["RepairNeighbor" if name.startswith("Repair") else "SewingWoman"]/1.75
            for side,prefix in (("L","left"),("R","right")):
                if f[prefix+"_weight"]>=.9999:
                    grip_error=max(grip_error,(rig.pose.bones["SOCKET_Grip."+side].head-v.world_target(f[prefix+"_contact"],scale)).length*scale)
            deps=bpy.context.evaluated_depsgraph_get()
            floor_error=max(floor_error,abs(min(v.base.evaluated_part_min_z(p,deps) for p in result.parts if p.bone.startswith("foot.")))*scale)
        tracks.append({"name":name,"duration_seconds":duration,"loop":loop,"frames":frames})
        rig.animation_data.action=None
        print(f"Validated {name}: {end+1} frames",flush=True)
    def snapshot(name,frame):
        rig.animation_data.action=bpy.data.actions[name];bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
        return {b.name:b.matrix.copy() for b in rig.pose.bones}
    endpoints=0.
    for a,af,b,bf in (("RepairTakeTool",72,"RepairWork",0),("RepairWork",96,"RepairPutTool",0),
        ("SewingEnter",72,"SewingUnpack",0),("SewingUnpack",144,"SewingWork",0),
        ("SewingWork",96,"SewingFold",0),("SewingFold",72,"SewingStow",0),("SewingStow",120,"SewingExit",0)):
        first=snapshot(a,af);last=snapshot(b,bf)
        endpoints=max(endpoints,max(abs(first[n][i][j]-last[n][i][j]) for n in first for i in range(4) for j in range(4)))
    rig.animation_data.action=None;v.base.reset_pose(rig)
    validation={"sampled_frames":samples,"max_grip_error_m":round(grip_error,7),"max_ground_error_m":round(floor_error,7),
        "max_endpoint_error":round(endpoints,7),"curve_signature":hashlib.sha256(json.dumps(curves,separators=(",",":")).encode()).hexdigest()}
    if grip_error>.002 or floor_error>.002 or endpoints>.002:raise RuntimeError(str(validation))
    manifest={"generator":Path(__file__).name,"version":"1.0.0","fps":24,"bone_count":31,
        "clips":[{k:t[k] for k in ("name","duration_seconds","loop")} for t in tracks],"tracks":tracks,
        "preserved_sources":hashes,"validation":validation}
    output=assets/"VillageResidentWorkroomActions.json"
    if args.validate_only:
        if json.loads(output.read_text(encoding="utf8"))!=manifest:raise RuntimeError("Workroom deterministic samples differ")
    else:
        v.base.export_animation_fbx(assets/"VillageResidentWorkroomActions.fbx",result)
        output.write_text(json.dumps(manifest,separators=(",",":"))+"\n",encoding="utf8")
        bpy.ops.wm.save_as_mainfile(filepath=str(sources/"VillageResidentWorkroomActions.blend"))
        for suffix in (".fbx",".json"):
            path=assets/("VillageResidentWorkroomActions"+suffix);meta=path.with_name(path.name+".meta")
            if not meta.exists():meta.write_text("fileFormatVersion: 2\nguid: "+hashlib.sha256(path.relative_to(v.ROOT).as_posix().encode()).hexdigest()[:32]+"\n",encoding="utf8")
    if hashes!={str(p.relative_to(v.ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}:raise RuntimeError("An existing resident or bank was modified")
    print(json.dumps(validation),flush=True)
    if not args.no_preview and not args.validate_only:render_previews(v,assets,sources)
