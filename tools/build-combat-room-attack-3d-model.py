"""Bounded Room Inside tableau, retained-rig gestures and authored pointing shape.

Run through run-blender.py. Only the Room body's added shape changes its FBX;
all Basis vertices/UVs/skin and the existing injury banks remain unchanged.
--validate-only recomputes curve/geometry signatures without publishing assets.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import math
from pathlib import Path
import struct
import sys
import wave

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
sys.dont_write_bytecode = True
import interior_kit as interiors
import bar_parts


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


room = module('room_attack_surface_source', 'build-combat-room-3d-model.py')
common = room.zombie.common
FPS = 30
OUT = ROOT / 'Assets/Resources/Combat'
HERO_OUT = ROOT / 'Assets/Resources/Player'
SOURCE = ROOT / 'ArtSource/CombatRoom'
CLIPS = (('RoomPointHero', 2.), ('RoomPointChest', .45), ('RoomLaugh', 5.))
LAUGHS = ((.28,.72),(.64,.90),(1.03,1.),(1.35,.83),(1.72,1.),
          (2.02,.77),(2.38,1.),(2.77,.9),(3.12,.78),(3.52,.92),(3.91,.72),(4.28,.52))
CORRIDOR_SCALE = 14.
SOURCE_MOUTH_FLOOR = Vector((0., room.geometry.MOUTH['bottom'], room.geometry.MOUTH['depth']))


def source_to_tableau(point):
    """Unity-metre source frame, independent of the imported FBX unit factor."""
    return tuple((Vector(point)-SOURCE_MOUTH_FLOOR)*CORRIDOR_SCALE)


CHAIR_DEPTH = 5.10
SEATED_YAW = math.radians(14.)
SEAT_ABOVE_FLOOR = .3475
PELVIS_ABOVE_FLOOR = .4275
CHAIR_FLOOR = (room.geometry.corridor_at(room.geometry.MOUTH['depth'] + CHAIR_DEPTH / CORRIDOR_SCALE)[1] -
               room.geometry.MOUTH['bottom']) * CORRIDOR_SCALE
ANCHORS = {'HeroPelvis':(0., CHAIR_FLOOR+PELVIS_ABOVE_FLOOR, CHAIR_DEPTH),
           'HeroForward':(math.sin(SEATED_YAW),CHAIR_FLOOR+PELVIS_ABOVE_FLOOR,CHAIR_DEPTH-math.cos(SEATED_YAW)),
           'Camera':(.72,2.95,1.90), 'LookAt':(0.,CHAIR_FLOOR+1.05,CHAIR_DEPTH),
           'EntryCamera':(0.,2.85,.05), 'Light':(0.,3.30,3.80)}
# One-source-metre reference axes let runtime measure the import's actual
# scale through anchor world positions, rather than assuming the FBX's 100.
ANCHORS.update({'MouthFloor': (0., 0., 0.),
                'SourceOrigin': source_to_tableau((0., 0., 0.)),
                'SourceRight': source_to_tableau((1., 0., 0.)),
                'SourceUp': source_to_tableau((0., 1., 0.)),
                'SourceForward': source_to_tableau((0., 0., 1.))})


def digest(data): return hashlib.sha256(data).hexdigest()
def smooth(t):
    t = min(1., max(0., t))
    return t*t*(3.-2.*t)


def snapshot(builder):
    return {bone.name: common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in
        bone.rotation_quaternion.to_euler('XYZ'))) for bone in builder.result.rig.pose.bones}


def evaluated_pose(builder, pose):
    builder._reset_pose()
    builder._apply_pose(pose)
    return snapshot(builder)


def blend(builder, first, second, t):
    builder._reset_pose(); builder._apply_pose(first)
    before = {b.name:b.rotation_quaternion.copy() for b in builder.result.rig.pose.bones}
    builder._reset_pose(); builder._apply_pose(second)
    return {b.name:common.BonePose(rotation_degrees=tuple(math.degrees(v) for v in
        before[b.name].slerp(b.rotation_quaternion, t).to_euler('XYZ')))
        for b in builder.result.rig.pose.bones}


def arm_to(builder, side, wrist, direction, presented=False):
    """Solve authored rotations; runtime owns the Room's accepted limb lengths."""
    rig = builder.result.rig
    upper, fore, hand = (rig.pose.bones[name+'.'+side] for name in ('upper_arm','forearm','hand'))
    if presented:
        room.apply_presented_arm_lengths(rig)
        bpy.context.view_layer.update()
    shoulder = upper.head.copy()
    a = upper.bone.length * (room.geometry.UPPER_ARM_LENGTH_SCALE if presented else 1.)
    b = fore.bone.length * (room.geometry.FOREARM_LENGTH_SCALE if presented else 1.)
    target = Vector(wrist)
    delta = target-shoulder
    distance = delta.length
    if not abs(a-b)+.001 < distance < a+b-.001:
        raise ValueError(f'Unreachable {side} contact {distance}/{a+b}: {tuple(target)}')
    axis = delta.normalized()
    hint = Vector((.4 if side == 'L' else -.4, -.35, -.65))
    pole = (hint-axis*hint.dot(axis)).normalized()
    along = (a*a-b*b+distance*distance)/(2.*distance)
    elbow = shoulder+axis*along+pole*math.sqrt(max(0., a*a-along*along))
    for bone, origin, aim in ((upper,shoulder,elbow-shoulder),(fore,elbow,target-elbow),
                               (hand,target,Vector(direction))):
        rest = bone.bone
        rotation = (rest.tail_local-rest.head_local).normalized().rotation_difference(aim.normalized()) @ rest.matrix_local.to_quaternion()
        bone.matrix = Matrix.Translation(origin) @ rotation.to_matrix().to_4x4()
        bpy.context.view_layer.update()
    return snapshot(builder)


def curve_signature(builder):
    signature = hashlib.sha256()
    for name, record in sorted(builder.result.actions.items()):
        for curve in common.iter_action_fcurves(record.action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError('Object/root motion forbidden')
            signature.update(json.dumps([name,curve.data_path,curve.array_index,
                [[round(v,6) for v in key.co] for key in curve.keyframe_points]],separators=(',',':')).encode())
    return signature.hexdigest()


def validate_actions(builder):
    rig = builder.result.rig
    fixed = ('root','pelvis','thigh.L','thigh.R','shin.L','shin.R','foot.L','foot.R')
    reference = None
    for name, record in builder.result.actions.items():
        rig.animation_data.action = record.action
        for frame in range(round(record.action.frame_end)+1):
            bpy.context.scene.frame_set(frame)
            values = {n:tuple(v for row in rig.pose.bones[n].matrix for v in row) for n in fixed}
            if reference is None: reference = values
            if any(max(abs(a-b) for a,b in zip(values[n],reference[n])) > .00003 for n in fixed):
                raise ValueError('Action moved planted lower body: '+name)
    rig.animation_data.action = None
    builder._reset_pose()


def npc_bank(publish):
    builder = room.zombie.load_builder()
    common.ANIMATION_FPS = FPS
    bpy.context.scene.render.fps = FPS
    base = evaluated_pose(builder, builder.relaxed_pose())
    builder._reset_pose(); builder._apply_pose(base)
    point = arm_to(builder,'R',(-.34,-.70,1.43),(0.,-1.,.02),True)
    builder._reset_pose(); builder._apply_pose(base)
    chest = arm_to(builder,'R',(-.44,-.47,1.30),(.72,.68,-.06),True)
    keys = [(frame/60.,blend(builder,base,point,smooth(frame/9.))) for frame in range(61)]
    builder._create_action('RoomPointHero','room_attack',2.,False,60,FPS,keys)
    # .45 seconds is intentionally not an integral frame at 30 Hz. The bank
    # uses 60 Hz so the authored move remains exactly .45 s after FBX import.
    common.ANIMATION_FPS = 60
    bpy.context.scene.render.fps = 60
    keys = [(frame/27.,blend(builder,point,chest,smooth(frame/27.))) for frame in range(28)]
    builder._create_action('RoomPointChest','room_attack',.45,False,27,60,keys)
    # Rebuild first action at the same export sampling rate.
    old = builder.result.actions.pop('RoomPointHero').action
    bpy.data.actions.remove(old)
    keys = [(frame/120.,blend(builder,base,point,smooth(frame/18.))) for frame in range(121)]
    builder._create_action('RoomPointHero','room_attack',2.,False,120,60,keys)
    keys=[]
    for frame in range(301):
        seconds=frame/60.
        envelope=smooth(seconds/.32)*smooth((5.-seconds)/.45)
        kick=sum(gain*math.exp(-((seconds-onset)/.095)**2) for onset,gain in LAUGHS)*envelope
        sway=math.sin(seconds*math.tau*.72)*envelope
        pose=dict(base)
        for name,rotation in {'spine':(8.+kick*11.,sway*2.,sway*4.),
                              'chest':(-13.-kick*17.,sway*4.,-sway*5.),
                              'neck':(-12.-kick*7.,0.,sway*4.),'head':(-15.-kick*8.,sway*8.,0.),
                              'clavicle.L':(0.,0.,-kick*7.),'clavicle.R':(0.,0.,kick*7.),
                              'upper_arm.L':(0.,-8.*envelope,14.*envelope),
                              'upper_arm.R':(0.,8.*envelope,-14.*envelope),
                              'forearm.L':(-34.*envelope,0.,0.),'forearm.R':(-42.*envelope,0.,0.)}.items():
            pose[name]=common.BonePose(rotation_degrees=tuple(v*envelope if name in ('spine','chest','neck','head') else v for v in rotation))
        laugh=evaluated_pose(builder,pose)
        if seconds<.35:laugh=blend(builder,chest,laugh,smooth(seconds/.35))
        if frame==300:laugh=base
        keys.append((frame/300.,laugh))
    builder._create_action('RoomLaugh','room_attack',5.,False,300,60,keys)
    validate_actions(builder)
    directions={}
    rig=builder.result.rig
    for name,expected in [('RoomPointHero',Vector((0.,-1.,.02))),('RoomPointChest',Vector((.72,.68,-.06)))]:
        rig.animation_data.action=builder.result.actions[name].action
        bpy.context.scene.frame_set(round(builder.result.actions[name].action.frame_end))
        room.apply_presented_arm_lengths(rig)
        hand=rig.pose.bones['hand.R']
        actual=(hand.tail-hand.head).normalized()
        error=math.degrees(actual.angle(expected.normalized()))
        if error>.02:raise ValueError('Room finger does not aim along authored direction '+name)
        directions[name]={'hand_axis_error_degrees':round(error,6),'wrist_metres':list(hand.head)}
    rig.animation_data.action=None;builder._reset_pose()
    data={'rig':'NpcHumanV2','root_motion':False,'animation_events':0,'fps':60,
          'signature':curve_signature(builder),'clips':[{'name':n,'duration':t,'loop':False} for n,t in CLIPS],
          'accepted_presented_lengths':[1.3,1.8,1.5],'fixed_lower_body':True,'point_directions':directions}
    if publish:
        common.export_animation_fbx(OUT/'RoomAttackNpcActions.fbx',builder.result)
        common.save_blend(SOURCE/'RoomAttackNpcActions.blend')
    return data


def hero_bank(publish):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Player/Blender/HomeShowerCurtainActions.blend'))
    rig=bpy.data.objects['RIG_Player']
    for obj in list(bpy.data.objects):
        if obj not in (rig,rig.parent):bpy.data.objects.remove(obj,do_unlink=True)
    for action in list(bpy.data.actions):bpy.data.actions.remove(action)
    builder=room.zombie.combat.CombatBuilder(common.BuildConfig(None,None,None,None,None,None,None,1.75,20261019,'apose'),
        room.zombie.combat.hero.DEFAULT_FACE_ATLAS,room.zombie.combat.hero.DEFAULT_CLOTHING_ATLAS)
    builder.result=common.BuildResult(root=rig.parent,rig=rig,collections={},materials={},parts=[])
    common.ANIMATION_FPS=60;bpy.context.scene.render.fps=60
    # The canonical anatomical left arm reaches the left temple. The hand's
    # long axis points upwards along the head, rather than through the face.
    base=evaluated_pose(builder,builder.relaxed_pose())
    pain=dict(base)
    pain.update({'spine':common.BonePose(rotation_degrees=(7.,0.,-2.)),
                 'chest':common.BonePose(rotation_degrees=(5.,0.,-3.)),
                 'head':common.BonePose(rotation_degrees=(13.,-3.,5.))})
    builder._reset_pose();builder._apply_pose(pain)
    target=rig.pose.bones['head'].head+Vector((.12,-.07,.13))
    pain=arm_to(builder,'L',target,(0.,-.1,1.))
    keys=[]
    for frame in range(121):
        t=frame/120.
        amount=smooth(t/.16)*smooth((1.-t)/.20)
        keys.append((t,blend(builder,base,pain,amount)))
    builder._create_action('RoomHeadPain','room_attack',2.,False,120,60,keys)
    validate_actions(builder)
    rig.animation_data.action=builder.result.actions['RoomHeadPain'].action
    bpy.context.scene.frame_set(60)
    head=rig.pose.bones['head'].head;hand=rig.pose.bones['hand.L'].head
    gap=(hand-head).length
    if not hand.x>head.x+.06 or not .13<gap<.23:
        raise ValueError('Hero anatomical left hand missed head '+str((tuple(head),tuple(hand))))
    rig.animation_data.action=None;builder._reset_pose()
    data={'rig':'HeroV2','root_motion':False,'animation_events':0,'fps':60,
          'signature':curve_signature(builder),'clips':[{'name':'RoomHeadPain','duration':2.,'loop':False}],
          'anatomical_hand':'L','entry_ground_offset':[0.,0.,0.],'exit_ground_offset':[0.,0.,0.],
          'fixed_lower_body':True,'neutral_endpoints':True,'left_wrist_to_head_m':round(gap,7)}
    if publish:
        common.export_animation_fbx(HERO_OUT/'RoomAttackHeroActions.fbx',builder.result)
        common.save_blend(SOURCE/'RoomAttackHeroActions.blend')
    return data


def body_basis_signature(objects):
    signature=hashlib.sha256()
    for obj in sorted(objects,key=lambda x:x.name):
        payload=[obj.name,[list(v.co) for v in obj.data.vertices],
                 [list(p.vertices) for p in obj.data.polygons],
                 [[list(loop.uv) for loop in layer.data] for layer in obj.data.uv_layers],
                 [[(g.group,round(g.weight,8)) for g in v.groups] for v in obj.data.vertices]]
        signature.update(json.dumps(payload,separators=(',',':')).encode())
    return signature.hexdigest()


def pointing_shape(publish):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'RoomBody.blend'))
    rig=bpy.data.objects['RIG_Player']
    rig.animation_data.action=None
    for bone in rig.pose.bones:
        bone.location=(0,0,0);bone.rotation_mode='QUATERNION';bone.rotation_quaternion=(1,0,0,0);bone.scale=(1,1,1)
    objects=[obj for obj in bpy.data.objects if obj.type=='MESH' and obj.name.startswith('GEO_')]
    before=body_basis_signature(objects)
    frame=rig.matrix_world @ rig.data.bones['hand.R'].matrix_local
    affected=[]
    for name in ['GEO_HandFinger1.R','GEO_HandFinger2.R','GEO_HandFinger3.R','GEO_HandThumb.R']:
        obj=bpy.data.objects[name]
        into=frame.inverted() @ obj.matrix_world
        out=obj.matrix_world.inverted() @ frame
        points=[into @ v.co for v in obj.data.vertices]
        if obj.data.shape_keys is None:obj.shape_key_add(name='Basis')
        shape=obj.data.shape_keys.key_blocks.get('RoomPoint') or obj.shape_key_add(name='RoomPoint')
        low=min(p.y for p in points);high=max(p.y for p in points)
        # Preserve embedded root rings. Distal stations curl into the same palm;
        # cross sections rotate rigidly, so thin fingers retain their volume.
        for vertex,p in zip(shape.data,points):
            fraction=max(0.,min(1.,(p.y-low)/(high-low)))
            angle=math.radians(145.)*fraction
            length=p.y-low
            radius=(high-low)/math.radians(145.)
            centre=Vector((p.x,low+radius*math.sin(angle),radius*(1.-math.cos(angle))))
            neutral_centre=Vector((p.x,p.y,0.))
            rotated=Matrix.Rotation(angle,3,'X')@(p-neutral_centre)
            vertex.co=out@(centre+rotated)
        shape.value=0.
        affected.append({'name':name,'vertices':len(points),'shape':'RoomPoint'})
    if body_basis_signature(objects)!=before:raise ValueError('Point shape changed original Room mesh/UV/skin')
    if publish:
        bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
        for obj in objects:obj.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.export_scene.fbx(filepath=str(OUT/'RoomBody.fbx'),use_selection=True,
            object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
            bake_anim=False,use_custom_props=True,use_mesh_modifiers=False,use_armature_deform_only=False,
            path_mode='STRIP',mesh_smooth_type='FACE')
        common.save_blend(SOURCE/'RoomBody.blend')
    return {'basis_signature':before,'shape':'RoomPoint','affected':affected,'extended_index':'GEO_HandFinger0.R'}


def mesh_object(name,geometry,parent,role):
    points,faces=bar_parts.to_source(geometry)
    if bar_parts.signed_volume(geometry)<=0:raise ValueError('Inward or zero solid '+name)
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(points,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.parent=parent
    obj['surface_role']=role
    material=bpy.data.materials.get('RoomAttack '+role) or bpy.data.materials.new('RoomAttack '+role)
    mesh.materials.append(material)
    uv=mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        normal=face.normal
        axes=(1,2) if abs(normal.x)>.5 else (0,2) if abs(normal.y)>.5 else (0,1)
        for index in face.loop_indices:
            p=mesh.vertices[mesh.loops[index].vertex_index].co
            uv.data[index].uv=((p[axes[0]]*1.5)%1.,(p[axes[1]]*1.5)%1.)
    return obj


def tableau(publish):
    # Reuse the actual visible chest corridor, including its original UV0;
    # this is an enlarged insert of that one place, not a second invented room.
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'RoomBody.blend'))
    torso=bpy.data.objects['GEO_Torso']
    slot=list(torso['room_surface_roles']).index('plaster')
    source_polygons=[face for face in torso.data.polygons if face.material_index==slot]
    source_points=[];source_faces=[];source_uv=[];remap={}
    for face in source_polygons:
        indices=[]
        for loop in face.loop_indices:
            index=torso.data.loops[loop].vertex_index
            if index not in remap:
                point=torso.matrix_world @ torso.data.vertices[index].co
                remap[index]=len(source_points)
                source_points.append((point.x*CORRIDOR_SCALE,
                    (point.y-room.geometry.MOUTH['depth'])*CORRIDOR_SCALE,
                    (point.z-room.geometry.MOUTH['bottom'])*CORRIDOR_SCALE))
            indices.append(remap[index]);source_uv.append(tuple(torso.data.uv_layers[0].data[loop].uv))
        source_faces.append(tuple(indices))
    for obj in list(bpy.data.objects):bpy.data.objects.remove(obj,do_unlink=True)
    root=bpy.data.objects.new('RoomAttackTableau',None);bpy.context.collection.objects.link(root)
    box=bar_parts.u_box
    parts=[]
    mesh=bpy.data.meshes.new('GEO_RoomAttackOriginalCorridor')
    mesh.from_pydata(source_points,[],source_faces);mesh.update()
    corridor=bpy.data.objects.new(mesh.name,mesh);bpy.context.collection.objects.link(corridor);corridor.parent=root
    corridor['surface_role']='original_room_plaster'
    mesh.materials.append(bpy.data.materials.get('RoomAttack original plaster') or bpy.data.materials.new('RoomAttack original plaster'))
    uv=mesh.uv_layers.new(name='UVMap')
    for loop,point in zip(uv.data,source_uv):loop.uv=point
    parts.append(corridor)
    # A simple worn chair uses individually grounded legs on the actual
    # corridor's rising floor. The seat stays level and beyond the fourth frame.
    def floor(depth):
        return (room.geometry.corridor_at(room.geometry.MOUTH['depth']+depth/CORRIDOR_SCALE)[1]-
                room.geometry.MOUTH['bottom'])*CORRIDOR_SCALE
    seat=CHAIR_FLOOR+SEAT_ABOVE_FLOOR
    # A simple worn chair with profiled legs and mortise-like cross rails.
    chair=[box((0.,seat-.035,CHAIR_DEPTH),(.48,.07,.45),.008),
           box((0.,seat+.33,CHAIR_DEPTH+.21),(.45,.43,.045),.008)]
    for x in (-.19,.19):
        for z in (.18,-.18):
            bottom=floor(CHAIR_DEPTH+z)
            source=interiors.turned_leg(seat-.07-bottom,.025,.021,.028,8)
            leg=([(a,c,b) for a,b,c in source[0]],[tuple(reversed(f)) for f in source[1]])
            chair.append(interiors.translated(leg,(x,bottom,CHAIR_DEPTH+z)))
        chair.append(box((x,seat+.22,CHAIR_DEPTH+.20),(.04,.62,.04),.004))
        chair.append(box((x,CHAIR_FLOOR+.20,CHAIR_DEPTH),(.035,.04,.40),.004))
    chair.append(box((0.,CHAIR_FLOOR+.20,CHAIR_DEPTH+.18),(.40,.04,.035),.004))
    parts.append(mesh_object('GEO_RoomAttackWoodChair',interiors.merge_all(chair),root,'wood'))
    for name,position in ANCHORS.items():
        obj=bpy.data.objects.new('ANCHOR_'+name,None);bpy.context.collection.objects.link(obj);obj.parent=root
        obj.location=(position[0],position[2],position[1])
    tree=BVHTree.FromPolygons([Vector(v) for v in source_points],source_faces)
    for height in (2.25,2.65,3.05):
        if tree.ray_cast(Vector((0.,-.5,height)),Vector((0.,1.,0.)),6.5)[0] is not None:
            raise ValueError('Original corridor axial sightline is blocked')
    volume=bar_parts.signed_volume((source_points,source_faces))
    if volume<=0.:raise ValueError('Original corridor is inward wound')
    bounds=np.array(source_points)[:,(0,2,1)]
    for axis, direction in (('Right', (1., 0., 0.)), ('Up', (0., 1., 0.)), ('Forward', (0., 0., 1.))):
        actual=Vector(ANCHORS['Source'+axis])-Vector(ANCHORS['SourceOrigin'])
        if (actual-Vector(direction)*CORRIDOR_SCALE).length > .000001:
            raise ValueError('Source metre frame is inconsistent: '+axis)
    payload={'geometry_signature':body_basis_signature(parts),'anchors':ANCHORS,
             'bounds_min':list(bounds.min(axis=0)),'bounds_max':list(bounds.max(axis=0)),
             'source':'RoomBody/GEO_Torso/plaster','original_uv0':True,
             'corridor_uniform_scale':CORRIDOR_SCALE,'nested_frame_count':4,
             'source_frame':{'space':'unity_metres','origin':list(SOURCE_MOUTH_FLOOR),
                             'ruler_distance_source_metres':1.,
                             'tableau_from_source':'(source - origin) * corridor_uniform_scale'},
             'front_opening':[room.geometry.MOUTH['half_width']*2*CORRIDOR_SCALE,
                              (room.geometry.MOUTH['top']-room.geometry.MOUTH['bottom'])*CORRIDOR_SCALE],
             'chair_floor_height':CHAIR_FLOOR,'chair_seat_height':seat,
             'seated_yaw_degrees':14.,'hero_pelvis_above_seat':.08,
             'vertices':sum(len(o.data.vertices) for o in parts),
             'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in parts),
             'material_slots_per_mesh':1}
    if publish:
        bpy.ops.object.select_all(action='DESELECT');root.select_set(True)
        for obj in root.children:obj.select_set(True)
        bpy.ops.export_scene.fbx(filepath=str(OUT/'RoomAttackTableau.fbx'),use_selection=True,
            object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
            bake_anim=False,use_custom_props=True,path_mode='STRIP',mesh_smooth_type='FACE')
        common.save_blend(SOURCE/'RoomAttackTableau.blend')
    return payload


def laugh_audio():
    """Finite vowel-formant/glottal score; no words, random generator or runtime synth."""
    rate=24000;values=[];state=7131;phase=0.;previous=0.
    for frame in range(rate*5):
        t=frame/rate;state=(state*1664525+1013904223)&0xffffffff
        noise=(state/2147483648.-1.)
        envelope=sum(gain*math.sin(math.pi*(t-start)/.27)**1.3 for start,gain in LAUGHS if 0.<t-start<.27)
        pitch=104.+14.*math.sin(t*5.1)+7.*math.sin(t*11.7)
        phase+=pitch/rate
        voice=sum(math.sin(math.tau*harmonic*phase)/harmonic*
                  (math.exp(-((harmonic*pitch-710.)/280.)**2)+.65*math.exp(-((harmonic*pitch-1120.)/360.)**2)+
                   .22*math.exp(-((harmonic*pitch-2400.)/550.)**2)) for harmonic in range(1,29))
        breath=max(0.,1.-abs((t%.36)-.05)/.08)*.07
        previous=previous*.62+noise*.38
        sample=math.tanh((voice*.72+previous*.14)*envelope+previous*breath)
        values.append(int(max(-.98,min(.98,sample))*.79*32767.))
    stream=io.BytesIO()
    with wave.open(stream,'wb') as output:
        output.setnchannels(1);output.setsampwidth(2);output.setframerate(rate)
        output.writeframes(struct.pack('<'+'h'*len(values),*values))
    return stream.getvalue()


def seated_fit():
    """Measure the uncorrected clip against the base slope, before runtime IK.

    This baseline does not include the raised architrave thresholds. Runtime
    foot contacts and the focused Room test use the actual posed plaster mesh.
    """
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend'))
    rig=bpy.data.objects['RIG_Player'];action=bpy.data.actions['BusRideLoop'];rig.animation_data.action=action
    shoes=[obj for obj in bpy.data.objects if obj.type=='MESH' and ('Foot.' in obj.name or 'Boot' in obj.name)]
    report=[]
    for progress in (0.,.25,.5,.75,1.):
        frame=progress*action.frame_end;bpy.context.scene.frame_set(int(frame),subframe=frame%1.)
        bpy.context.view_layer.update();graph=bpy.context.evaluated_depsgraph_get()
        pelvis=rig.matrix_world @ rig.pose.bones['pelvis'].head
        record={}
        for side in ('L','R'):
            objects=[o for o in shoes if o.name.endswith('.'+side)]
            points=[o.evaluated_get(graph).matrix_world @ v.co for o in objects for v in o.evaluated_get(graph).data.vertices]
            if not points:raise ValueError('Missing production boot '+side)
            # Lowest point relative to the real sloping floor, not a horizontal
            # bounding box which would hide a toe penetrating the incline.
            pitch=(room.geometry.FAR['bottom']-room.geometry.MOUTH['bottom'])/(room.geometry.FAR['depth']-room.geometry.MOUTH['depth'])
            required=max(CHAIR_FLOOR+pitch*((p.x-pelvis.x)*math.sin(SEATED_YAW)+
                         (p.y-pelvis.y)*math.cos(SEATED_YAW))-(p.z-pelvis.z) for p in points)
            minimum=min(points,key=lambda p:p.z)
            record[side]={'minimum_relative_to_pelvis':list(minimum-pelvis),'pelvis_height_for_slope_contact':required}
        report.append(record)
    print('ROOM SEATED BOOT CONTACT '+json.dumps(report),flush=True)
    return report


def surface_maps(publish):
    # Reuse the Room's accepted ImageGen source; plaster/wood are independent
    # repeating patches rather than random samples from a body unwrap.
    regions=room.source_pixels()
    maps={}
    for name,index in [('RoomAttackWood',3)]:
        region=regions[index]
        yy=np.linspace(0,region.shape[0]-1,256).astype(int)
        xx=np.linspace(0,region.shape[1]-1,256).astype(int)
        quarter=region[np.ix_(yy,xx)]
        half=np.concatenate((quarter,quarter[:,::-1]),axis=1)
        patch=np.concatenate((half,half[::-1]),axis=0)
        data=room.helpers.png(patch)
        maps[name]=digest(data)
        if publish:(OUT/(name+'.png')).write_bytes(data)
    return maps


def refresh_damage_hash(old,new):
    path=ROOT/'Assets/Resources/CombatGore/CombatRoomDamage3D.json'
    payload=json.loads(path.read_text(encoding='utf-8'))
    old=payload['source']['sha256']
    def update(value):
        if isinstance(value,dict):
            for key,item in value.items():
                if key in ('sha256','source_sha256') and item==old:value[key]=new
                else:update(item)
        elif isinstance(value,list):
            for item in value:update(item)
    update(payload)
    path.write_text(json.dumps(payload,indent=2)+'\n',encoding='utf-8')


def inspection_scene(camera_at,look_at):
    scene=bpy.context.scene
    for obj in list(bpy.data.objects):
        if obj.type in ('CAMERA','LIGHT'):bpy.data.objects.remove(obj,do_unlink=True)
    scene.render.engine='CYCLES';scene.cycles.samples=12
    scene.render.resolution_x=850;scene.render.resolution_y=950;scene.render.resolution_percentage=100
    if scene.world is None:scene.world=bpy.data.worlds.new('Room asset inspection world')
    scene.world.color=(.09,.09,.09)
    camera_data=bpy.data.cameras.new('Room asset inspection');camera=bpy.data.objects.new('Room asset inspection',camera_data)
    bpy.context.collection.objects.link(camera);scene.camera=camera;camera.location=camera_at
    camera.rotation_euler=(Vector(look_at)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.lens=45
    for location,power,size in (((-2,-3,4),450,3),((2,-1,2.5),250,2),((0,2,3),300,2)):
        data=bpy.data.lights.new('Room asset soft light','AREA');data.energy=power;data.shape='DISK';data.size=size
        light=bpy.data.objects.new(data.name,data);bpy.context.collection.objects.link(light);light.location=location
        light.rotation_euler=(Vector(look_at)-light.location).to_track_quat('-Z','Y').to_euler()
    return scene


def previews():
    destination=ROOT/'Captures/RoomAttackAssets';destination.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'RoomBody.blend'))
    rig=bpy.data.objects['RIG_Player']
    with bpy.data.libraries.load(str(SOURCE/'RoomAttackNpcActions.blend'),link=False) as (available,loaded):
        loaded.actions=[name for name in available.actions if name in dict(CLIPS)]
    actions={a.name.split('.')[0]:a for a in loaded.actions}
    scene=inspection_scene((1.25,-3.2,1.75),(0.,-.10,1.25))
    for name,frame in [('RoomPointHero',90),('RoomPointChest',27),('RoomLaugh',126)]:
        rig.animation_data.action=actions[name]
        scene.frame_set(frame);room.apply_presented_arm_lengths(rig)
        for obj in bpy.data.objects:
            if obj.type=='MESH' and obj.data.shape_keys and obj.data.shape_keys.key_blocks.get('RoomPoint'):
                obj.data.shape_keys.key_blocks['RoomPoint'].value=0. if name=='RoomLaugh' else 1.
        scene.render.filepath=str(destination/(name+'.png'));bpy.ops.render.render(write_still=True)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Player/Blender/HomeShowerCurtainActions.blend'))
    rig=bpy.data.objects['RIG_Player']
    with bpy.data.libraries.load(str(SOURCE/'RoomAttackHeroActions.blend'),link=False) as (available,loaded):
        loaded.actions=['RoomHeadPain']
    rig.animation_data.action=loaded.actions[0]
    scene=inspection_scene((1.2,-3.,1.65),(0.,0.,1.25));scene.frame_set(60)
    scene.render.filepath=str(destination/'RoomHeadPain.png');bpy.ops.render.render(write_still=True)
    tableau_preview()
    print('ROOM ATTACK AUTHORED STILL INSPECTION READY',flush=True)


def tableau_preview():
    destination=ROOT/'Captures/RoomAttackAssets';destination.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend'))
    rig=bpy.data.objects['RIG_Player'];hero_root=rig.parent
    for obj in list(bpy.data.objects):
        ancestor=obj
        while ancestor is not None and ancestor!=hero_root:ancestor=ancestor.parent
        if ancestor is None:bpy.data.objects.remove(obj,do_unlink=True)
    rig.animation_data.action=bpy.data.actions['BusRideLoop'];bpy.context.scene.frame_set(0)
    hero_root.rotation_euler.z=SEATED_YAW;bpy.context.view_layer.update()
    target=ANCHORS['HeroPelvis'];pelvis=rig.matrix_world @ rig.pose.bones['pelvis'].head
    hero_root.location+=Vector((target[0],target[2],target[1]))-pelvis
    with bpy.data.libraries.load(str(SOURCE/'RoomAttackTableau.blend'),link=False) as (available,loaded):
        loaded.objects=list(available.objects)
    for obj in loaded.objects:
        if obj is not None:bpy.context.collection.objects.link(obj)
    at=ANCHORS['Camera'];focus=ANCHORS['LookAt']
    scene=inspection_scene((at[0],at[2],at[1]),(focus[0],focus[2],focus[1]))
    data=bpy.data.lights.new('Room ceiling lamp inspection','POINT');data.energy=130.;data.shadow_soft_size=.15
    lamp=bpy.data.objects.new(data.name,data);bpy.context.collection.objects.link(lamp)
    at=ANCHORS['Light'];lamp.location=(at[0],at[2],at[1])
    for obj in bpy.data.objects:
        if obj.type!='MESH' or not obj.name.startswith('GEO_RoomAttack'):continue
        texture=OUT/('RoomAttackWood.png' if 'Wood' in obj.name else 'RoomBodyAtlas.png')
        material=bpy.data.materials.get(texture.stem)
        if material is None:
            material=bpy.data.materials.new(texture.stem);material.use_nodes=True
            nodes=material.node_tree.nodes;shader=nodes.get('Principled BSDF');shader.inputs['Roughness'].default_value=.95
            node=nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(texture),check_existing=True)
            material.node_tree.links.new(node.outputs['Color'],shader.inputs['Base Color'])
        obj.data.materials.clear();obj.data.materials.append(material)
    scene.render.filepath=str(destination/'RoomTableau.png');bpy.ops.render.render(write_still=True)
    print('ROOM EXACT CORRIDOR, ORIGINAL UV AND SEATED HERO INSPECTION READY',flush=True)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--validate-only',action='store_true')
    parser.add_argument('--preview-only',action='store_true')
    parser.add_argument('--tableau-only',action='store_true')
    parser.add_argument('--measure-seated-only',action='store_true')
    parser.add_argument('--tableau-preview-only',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.preview_only:
        previews();return
    if args.tableau_preview_only:
        tableau_preview();return
    if args.measure_seated_only:
        seated_fit();return
    if args.tableau_only:
        model=tableau(not args.validate_only)
        manifest=OUT/'RoomAttack3D.json'
        payload=json.loads(manifest.read_text(encoding='utf-8'))
        if args.validate_only:
            if json.loads(json.dumps(model))!=payload['tableau']:raise ValueError('Room tableau differs')
        else:
            payload['tableau']=model
            payload['textures'].pop('RoomAttackPlaster',None)
            manifest.write_text(json.dumps(payload,indent=2)+'\n',encoding='utf-8')
        print('ROOM TABLEAU: outward solids, open entrance, finite material slots and measured anchors verified',flush=True)
        return
    publish=not args.validate_only
    OUT.mkdir(parents=True,exist_ok=True);SOURCE.mkdir(parents=True,exist_ok=True)
    old=digest((OUT/'RoomBody.fbx').read_bytes())
    body=pointing_shape(publish)
    npc=npc_bank(publish)
    hero=hero_bank(publish)
    model=tableau(publish)
    audio=laugh_audio()
    payload={'generator':Path(__file__).name,'version':1,'test_only':True,
             'pointing':body,'npc':npc,'hero':hero,'tableau':model,
             'textures':surface_maps(publish),'laugh_audio':{'name':'RoomLaugh','duration':5.,'sha256':digest(audio)},
             'laugh_impulses':LAUGHS}
    payload=json.loads(json.dumps(payload))
    manifest=OUT/'RoomAttack3D.json'
    if publish:
        (OUT/'RoomLaugh.wav').write_bytes(audio)
        manifest.write_text(json.dumps(payload,indent=2)+'\n',encoding='utf-8')
        refresh_damage_hash(old,digest((OUT/'RoomBody.fbx').read_bytes()))
    elif json.loads(manifest.read_text(encoding='utf-8'))!=payload:
        raise ValueError('Room attack deterministic manifest differs')
    print('ROOM ATTACK: retained basis/UV/skin; fixed roots/feet; named clips; open tableau; deterministic audio and surfaces verified',flush=True)


if __name__=='__main__':main()
