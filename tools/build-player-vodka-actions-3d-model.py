"""Deterministic, bone-only equipped bottle action bank on the production Hero V2.

The lower body is constant in source; runtime's owned torso mask retains gait.
Run with tools/run-blender.py. --validate-only rebuilds the measured contract.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
sys.dont_write_bytecode = True
import bpy
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("vodka_hero_v2", ROOT / "tools/build-player-3d-model-v2.py")
hero = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = hero
spec.loader.exec_module(hero)
common, seat = hero.common, hero.player_counter_seat_actions
OUT = ROOT / "Assets/Resources/Player/PlayerVodkaActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/Player/Blender/PlayerVodkaActions.blend"
FPS = 24
CLIPS = (("VodkaCarry", 1., True), ("VodkaDraw", 1.5, False),
         ("VodkaOpen", 1.25, False), ("VodkaSip", 2., False), ("VodkaStow", .75, False), ("VodkaThrow", 1., False))
LOWER = ("root", "pelvis", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R")


def smooth(t):
    t = max(0., min(1., t))
    return t*t*(3.-2.*t)


def pose_dict(builder):
    return {bone.name: common.BonePose(location_m=tuple(bone.location / builder.scale),
        rotation_degrees=tuple(math.degrees(v) for v in bone.rotation_quaternion.to_euler("XYZ")),
        scale=tuple(bone.scale)) for bone in builder.result.rig.pose.bones}


def frame(axis, palm):
    axis=Vector(axis).normalized();palm=Vector(palm);palm=(palm-axis*palm.dot(axis)).normalized()
    return Matrix((palm.cross(axis),palm,axis)).transposed().to_quaternion()


def sample_stops(stops, second):
    index=next((i for i in range(len(stops)-1) if second<=stops[i+1][0]+1e-9),len(stops)-2)
    a,b=stops[index:index+2];amount=smooth((second-a[0])/(b[0]-a[0]))
    return a[1].lerp(b[1],amount),a[2].slerp(b[2],amount)


def solve_hand(rig, side, wrist, rotation, pole):
    upper,lower,hand=(rig.pose.bones[n+'.'+side] for n in ('upper_arm','forearm','hand'))
    seat.solve_chain(upper,lower,hand,wrist,rotation,upper.head+Vector(pole))
    # Same original-forearm twist distribution as CombatBuilder.solve_arm;
    # rotate the wrist without turning the palm into an isolated hinge.
    saved=hand.matrix.copy();axis=(hand.head-lower.head).normalized()
    rest=lower.bone.matrix_local.to_quaternion().inverted()@hand.bone.matrix_local.to_quaternion()
    turn=hand.matrix.to_quaternion()@rest.inverted()
    turn=(turn@Vector((0.,1.,0.))).rotation_difference(axis)@turn
    lower.matrix=Matrix.Translation(lower.head.copy())@turn.to_matrix().to_4x4()
    bpy.context.view_layer.update();hand.matrix=saved;bpy.context.view_layer.update()


def body_surfaces(rig):
    surfaces=[];depsgraph=bpy.context.evaluated_depsgraph_get()
    for name in ('GEO_Torso','GEO_Pelvis','GEO_Head','GEO_Neck','GEO_NeckStub'):
        obj=bpy.data.objects.get(name)
        if obj is None:continue
        obj.hide_set(False);bpy.context.view_layer.update()
        evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
        matrix=rig.matrix_world.inverted()@evaluated.matrix_world
        points=[matrix@v.co for v in mesh.vertices];faces=[tuple(f.vertices) for f in mesh.polygons]
        bounds=tuple(Vector(tuple(fn(v[axis] for v in points) for axis in range(3))) for fn in (min,max))
        surfaces.append((name,BVHTree.FromPolygons(points,faces),bounds));evaluated.to_mesh_clear()
    return surfaces


def build():
    bpy.ops.wm.open_mainfile(filepath=str(hero.DEFAULT_OUTPUT))
    rig=next(obj for obj in bpy.data.objects if obj.type=='ARMATURE')
    result=common.BuildResult(rig.parent,rig,{}, {},parts=[],actions={})
    config=common.BuildConfig(BLEND,None,None,MANIFEST,None,None,OUT,1.75,20261102,'apose')
    builder=hero.HeroV2Builder(config,hero.DEFAULT_FACE_ATLAS,hero.DEFAULT_CLOTHING_ATLAS);builder.result=result
    rig.animation_data.action=None;builder._reset_pose();builder._apply_pose(builder.relaxed_pose())
    neutral=seat.snapshot(result);baseline={b.name:b.matrix.copy() for b in rig.pose.bones}
    surfaces=body_surfaces(rig)
    rw,lw=(rig.pose.bones['hand.'+side].head.copy() for side in ('R','L'))
    rr,lr=(rig.pose.bones['hand.'+side].matrix.to_quaternion() for side in ('R','L'))
    re,le=(rig.pose.bones['forearm.'+side].head.copy() for side in ('R','L'))
    mouth=rig.pose.bones['SOCKET_Mouth'].head.copy()
    carry=rw+Vector((-.025,-.12,.19));drinking=mouth+Vector((-.10,-.27,-.12))
    throw_back=carry+Vector((-.14,.05,.16));throw_release=carry+Vector((-.26,-.11,.11))
    carry_rot=Quaternion(Vector((0.,1.,0.)),math.radians(-12))@rr
    drink_rot=Quaternion(Vector((1.,0.,0.)),math.radians(-72))@carry_rot
    seat.restore_mix(result,neutral,neutral,0.)
    seat.solve_chain(rig.pose.bones['upper_arm.R'],rig.pose.bones['forearm.R'],rig.pose.bones['hand.R'],carry,carry_rot,re+Vector((-.10,0.,-.06)))
    carry_pose=seat.snapshot(result);seat.restore_mix(result,neutral,neutral,0.)
    right_bones=('clavicle.R','upper_arm.R','forearm.R','hand.R')
    def mix_bones(first,last,amount,names):
        for bone_name in names:
            a,b=first[bone_name],last[bone_name];bone=rig.pose.bones[bone_name]
            bone.location=a[0].lerp(b[0],amount);bone.rotation_quaternion=a[1].slerp(b[1],amount);bone.scale=a[2].lerp(b[2],amount)
        bpy.context.view_layer.update()
    hand_frames={}
    for side in ('R','L'):
        wrist,distal,across,palm=hero.player_hand_grip._frame(result,side)
        centre=wrist+distal*(.035 if side=='R' else .060)+palm*(.0147796875+.015 if side=='R' else .030)
        inverse=rig.data.bones['hand.'+side].matrix_local.to_quaternion().inverted()
        hand_frames[side]=(inverse@(centre-wrist),inverse@(-across),inverse@palm)
    def contact(side, centre, axis=(0.,0.,1.), palm=None):
        offset,local_axis,local_palm=hand_frames[side]
        rotation=frame(axis,palm or ((1.,0.,0.) if side=='R' else (-1.,0.,0.)))@frame(local_axis,local_palm).inverted()
        return Vector(centre)-rotation@offset,rotation
    # The same validated left jacket approach as the production pistol. The
    # right hand is re-authored for an upright bottle neck, not pistol axes.
    pistol_source=ROOT/'ArtSource/Combat/WeaponCarryActions.blend'
    with bpy.data.libraries.load(str(pistol_source),link=False) as (available,selected):selected.actions=['PistolDraw']
    pistol=selected.actions[0];left_tracks=[]
    left_bones=('clavicle.L','upper_arm.L','forearm.L','hand.L')
    for frame_index in range(37):
        t=frame_index/36;rig.animation_data.action=pistol;at=pistol.frame_end*t
        bpy.context.scene.frame_set(math.floor(at),subframe=at%1);bpy.context.view_layer.update()
        captured=seat.snapshot(result)
        # Pistol Rest and bottle Carry have different endpoint hands. Release
        # the flap along the authored front route, then finish exactly neutral.
        amount=smooth((t-.76)/.24)
        left_tracks.append({name:(captured[name][0].lerp(neutral[name][0],amount),
            captured[name][1].slerp(neutral[name][1],amount),captured[name][2].lerp(neutral[name][2],amount)) for name in left_bones})
    rig.animation_data.action=None
    # All points below are NECK CENTRES in the source rig's metres, never wrists.
    draw_necks=((.20,(-.10,-.30,1.24)),(.46,(.08,-.17,1.32)),(.55,(.08,-.17,1.32)),
        (.82,(.08,-.17,1.54)),(.99,(.08,-.29,1.54)),(1.18,(-.095,-.335,1.10)))
    draw_stops=[(0.,rw,rr)]+[(second,*contact('R',point,palm=(0.,1.,0.))) for second,point in draw_necks]+[(1.70,carry,carry_rot)]
    front_neck=Vector((-.18,-.37,1.22));cap=front_neck+Vector((0.,0.,.07425))
    open_right=((0.,carry,carry_rot),(.28,*contact('R',front_neck)),(.95,*contact('R',front_neck)),(1.25,carry,carry_rot))
    open_left=((0.,lw,lr),(.16,*contact('L',(.34,-.20,1.08))),(.36,*contact('L',(.31,-.38,1.26))),
        (.54,*contact('L',cap)),(.83,*contact('L',cap)),(.875,*contact('L',cap+Vector((0.,0.,.035)))),
        (1.025,*contact('L',(.28,-.40,1.23))),(1.10,*contact('L',(.34,-.20,1.08))),(1.25,lw,lr))
    for obj in list(bpy.data.objects):
        if obj not in (result.root,rig):bpy.data.objects.remove(obj,do_unlink=True)
    for action in list(bpy.data.actions):bpy.data.actions.remove(action)
    for name,duration,loop in CLIPS:
        keys=[];count=round(duration*FPS)
        for frame_index in range(count+1):
            t=frame_index/count;second=t*duration;seat.restore_mix(result,neutral,neutral,0.)
            rpos,rrot=carry.copy(),carry_rot.copy()
            if name in ('VodkaDraw','VodkaStow'):
                draw_progress=t if name=='VodkaDraw' else 1.-t
                rpos,rrot=sample_stops(draw_stops,draw_progress*1.70)
                left=left_tracks[round(draw_progress*36)]
                for bone_name,(location,rotation,scale) in left.items():
                    b=rig.pose.bones[bone_name];b.location=location;b.rotation_quaternion=rotation;b.scale=scale
                bpy.context.view_layer.update()
            elif name=='VodkaOpen':
                rpos,rrot=sample_stops(open_right,second)
                lpos,lrot=sample_stops(open_left,second)
                if .54<=second<=.83:
                    twist=math.sin(smooth((second-.54)/.29)*math.pi)*22.
                    lrot=Quaternion(Vector((0.,0.,1.)),math.radians(twist))@lrot
                if (lpos-lw).length>1e-7:solve_hand(rig,'L',lpos,lrot,(.8,-.65,-.35))
            elif name=='VodkaSip':
                amount=smooth(t/.35)*(1.-smooth((t-.65)/.35))
                rpos=carry.lerp(drinking,amount);rrot=carry_rot.slerp(drink_rot,amount)
                rig.pose.bones['head'].rotation_quaternion=Quaternion(Vector((1.,0.,0.)),math.radians(-6*amount))@neutral['head'][1]
                bpy.context.view_layer.update()
            elif name=='VodkaThrow':
                if t<.35:rpos=carry.lerp(throw_back,smooth(t/.35))
                elif t<.60:rpos=throw_back.lerp(throw_release,smooth((t-.35)/.25))
                else:rpos=throw_release.lerp(rw,smooth((t-.60)/.40));rrot=carry_rot.slerp(rr,smooth((t-.60)/.40))
            if (rpos-rw).length>1e-7:
                pole=Vector((-.45,-.35,-.85))
                if name in ('VodkaDraw','VodkaStow'):
                    draw_second=(t if name=='VodkaDraw' else 1.-t)*1.70
                    pole=Vector((-.95,-.25,-.1)).lerp(pole,smooth((draw_second-.55)/.27))
                if name in ('VodkaDraw','VodkaStow','VodkaOpen'):solve_hand(rig,'R',rpos,rrot,pole)
                else:seat.solve_chain(rig.pose.bones['upper_arm.R'],rig.pose.bones['forearm.R'],rig.pose.bones['hand.R'],rpos,rrot,re+Vector((-.10,0.,-.06)))
            if name in ('VodkaDraw','VodkaStow'):
                progress=t if name=='VodkaDraw' else 1.-t
                current=seat.snapshot(result)
                if progress<.10:mix_bones(neutral,current,smooth(progress/.10),right_bones)
                elif progress>.76:mix_bones(current,carry_pose,smooth((progress-.76)/.24),right_bones)
            elif name=='VodkaOpen':
                current=seat.snapshot(result)
                if second<.18:mix_bones(carry_pose,current,smooth(second/.18),right_bones)
                elif second>.95:mix_bones(current,carry_pose,smooth((second-.95)/.30),right_bones)
                current=seat.snapshot(result)
                if second<.12:mix_bones(neutral,current,smooth(second/.12),left_bones)
                elif second>1.10:mix_bones(current,neutral,smooth((second-1.10)/.15),left_bones)
            keys.append((t,pose_dict(builder)))
        builder._create_action(name,'vodka_equipment',duration,loop,count,FPS,keys)
    def sample(name,t):
        seat.sample(result,name,t);return {b.name:b.matrix.copy() for b in rig.pose.bones}
    def error(a,b,names):return max(abs(a[n][i][j]-b[n][i][j]) for n in names for i in range(4) for j in range(4))
    lower=max(error(baseline,sample(n,i/(d*FPS)),LOWER) for n,d,_ in CLIPS for i in range(round(d*FPS)+1))
    seams=(('VodkaDraw',1.,'VodkaCarry',0.),('VodkaCarry',1.,'VodkaCarry',0.),('VodkaOpen',0.,'VodkaCarry',0.),
        ('VodkaOpen',1.,'VodkaCarry',0.),('VodkaCarry',0.,'VodkaSip',0.),('VodkaSip',1.,'VodkaCarry',0.),
        ('VodkaCarry',0.,'VodkaStow',0.),('VodkaCarry',0.,'VodkaThrow',0.))
    endpoint=max(error(sample(a,t),sample(b,u),baseline) for a,t,b,u in seams)
    endpoint=max(endpoint,error(baseline,sample('VodkaDraw',0.),baseline),error(baseline,sample('VodkaStow',1.),baseline),error(baseline,sample('VodkaThrow',1.),baseline))
    sample('VodkaSip',.5);wrist_error=(rig.pose.bones['hand.R'].head-drinking).length
    # Whole forearm/palm volumes, not just a reachable wrist, must remain
    # outside the actual torso/pelvis/neck/head throughout all three actions.
    arm_clearance={side:float('inf') for side in ('L','R')}
    phase_clearance={side:{} for side in ('L','R')};worst={}
    for name,duration,_ in CLIPS:
        if name not in ('VodkaDraw','VodkaOpen','VodkaStow'):continue
        for side in ('L','R'):
            minimum=float('inf')
            for frame_index in range(round(duration*120)+1):
                progress=frame_index/(duration*120);sample(name,progress)
                elbow=rig.pose.bones['forearm.'+side].head;wrist=rig.pose.bones['hand.'+side].head
                probes=[(elbow.lerp(wrist,i/10),.033+(.021-.033)*i/10) for i in range(11)]
                probes.extend((wrist.lerp(rig.pose.bones['hand.'+side].tail,i/4),.024) for i in range(5))
                for point,radius in probes:
                    for surface_name,surface,bounds in surfaces:
                        near,normal,_,distance=surface.find_nearest(point)
                        within=all(bounds[0][axis]<=point[axis]<=bounds[1][axis] for axis in range(3))
                        signed=-distance if within and (point-near).dot(normal)<0 else distance
                        value=signed-radius
                        if value<minimum:
                            minimum=value;worst[(side,name)]=(progress,surface_name,tuple(point),value)
            phase_clearance[side][name]=minimum;arm_clearance[side]=min(arm_clearance[side],minimum)
    if min(arm_clearance.values())<-.008:raise ValueError('Bottle arm crosses body: '+repr(worst))
    if max(lower,endpoint,wrist_error)>.00002:raise ValueError(f'Vodka action contract: {lower}, {endpoint}, {wrist_error}')
    curves=[[n,c.data_path,c.array_index,[[round(v,7) for v in p.co] for p in c.keyframe_points]] for n,_,_ in CLIPS for c in common.iter_action_fcurves(result.actions[n].action)]
    sample('VodkaDraw',.48/1.70)
    hand=rig.pose.bones['hand.R'];offset=hand_frames['R'][0]
    contact_error=(hand.head+hand.matrix.to_quaternion()@offset-Vector((.08,-.17,1.32))).length
    if contact_error>.003:raise ValueError('Under-jacket bottle contact misses authored neck dock: '+str(contact_error))
    payload=dict(generator='player_vodka_v1',rig='HeroV2',bone_count=len(rig.data.bones),fps=FPS,root_motion=False,animation_events=0,
        source_sha256=hashlib.sha256(hero.DEFAULT_OUTPUT.read_bytes()).hexdigest(),pistol_action_source_sha256=hashlib.sha256(pistol_source.read_bytes()).hexdigest(),
        maximum_lower_body_error=lower,maximum_endpoint_error=endpoint,maximum_drink_wrist_error_m=wrist_error,
        minimum_left_arm_body_clearance_m=arm_clearance['L'],left_arm_body_clearance_by_clip_m=phase_clearance['L'],
        minimum_right_arm_body_clearance_m=arm_clearance['R'],right_arm_body_clearance_by_clip_m=phase_clearance['R'],
        maximum_draw_neck_contact_error_m=contact_error,sip_commit_progress=.5,throw_release_progress=.6,draw_transfer_progress=.48/1.70,
        cap_contact_progress=.448,cap_twist_end_progress=.664,cap_detach_progress=.70,cap_release_progress=.90,cap_open_progress=.70,
        draw_neck_contact_source=[.08,-.17,1.32],open_neck_source=list(front_neck),open_cap_source=list(cap),
        carry_wrist_source=list(carry),drink_wrist_source=list(drinking),mouth_source=list(mouth),
        clips=[dict(name=n,duration_seconds=d,loop=loop) for n,d,loop in CLIPS],signature=hashlib.sha256(json.dumps(curves,separators=(',',':')).encode()).hexdigest())
    return builder,payload


def main():
    protected=[hero.DEFAULT_OUTPUT,ROOT/"Assets/Resources/Player/Player3DV2.prefab"]
    before={p:hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    builder,payload=build()
    if "--validate-only" in sys.argv:
        if json.loads(MANIFEST.read_text())!=payload: raise ValueError("Vodka actions differ from deterministic manifest")
    else:
        common.export_animation_fbx(OUT,builder.result)
        common.save_blend(BLEND)
        MANIFEST.write_text(json.dumps(payload,indent=2)+"\n")
        for path in (OUT,MANIFEST):
            meta=path.with_name(path.name+".meta")
            if not meta.exists(): meta.write_text("fileFormatVersion: 2\nguid: "+hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]+"\n")
    _,repeated=build()
    if repeated!=payload: raise ValueError("Vodka action bank is nondeterministic")
    if before!={p:hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}: raise ValueError("Vodka actions changed hero source")
    print("PLAYER VODKA ACTION CONTRACT OK "+json.dumps(payload),flush=True)

if __name__=="__main__": main()
