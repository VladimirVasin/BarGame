"""Closed-throat human knots and the doubled self-carrier on the retained NPC rig.

Only Zombie uses this bank. Docker and the other authored enemies retain their
previous geometry/actions. All output is staged through tools/run-blender.py.
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
from mathutils import Vector, Quaternion, Matrix

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
sys.dont_write_bytecode = True
from atlas_kit import PixelCanvas, read_generated_png
import degraded_zombie_geometry as geometry

spec = importlib.util.spec_from_file_location('decay_ordinary_source', ROOT / 'tools/build-combat-ordinary-zombie-3d-model.py')
ordinary = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = ordinary
spec.loader.exec_module(ordinary)
common = ordinary.common
FPS = 100
PREPARE, ACTIVE, RECOVER, RISE = .95, .22, .95, 2.4
CLIPS = [('ZombieDecayIdle', 8., True), ('ZombieDecayWalk', 1.6, True),
         ('ZombieDecayAttackRight', PREPARE+ACTIVE+RECOVER, False),
         ('ZombieDecayAttackLeft', PREPARE+ACTIVE+RECOVER, False), ('ZombieDecayHit', .45, False)]
CLIPS += [(f'ZombieDecayDormant{i}', 1., True) for i in range(3)]
CLIPS += [(f'ZombieDecayRise{i}', RISE, False) for i in range(3)]
CLIPS += [('ZombieCarrierIdle',8.,True),('ZombieCarrierWalk',1.8,True),
          ('ZombieCarrierAttackRight',2.76,False),('ZombieCarrierAttackLeft',2.76,False),('ZombieCarrierHit',.45,False)]
OUT = ROOT / 'Assets/Resources/Combat'
SOURCE = ROOT / 'ArtSource/CombatZombie'
ROLE_ORDER = ('skin','trousers','boots','glass')


def role_colour(role, variant):
    return dict(skin=(1,1,1),trousers=(.29,.28,.24),boots=(.24,.21,.18),glass=(1,1,1))[role]


def role_for(name):
    if name.startswith('GEO_Legs'):return 'trousers'
    if name.startswith('GEO_Foot'):return 'boots'
    return 'skin'


def visible_names(variant):
    manifest=json.loads((ROOT/'Assets/Resources/VillageLife/StationWorker.json').read_text())
    return {p['name'] for p in manifest['parts'] if p['name'].startswith('GEO_')}


def smooth(t):
    return ordinary.smooth(t)


def pulse(t, a, b, c, d):
    return smooth((t-a)/(b-a))*(1.-smooth((t-c)/(d-c)))


def load_builder(retain_meshes=False):
    builder=ordinary.load_builder(retain_meshes)
    rig=builder.result.rig;bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    for bone in rig.data.edit_bones:bone.use_connect=False
    bpy.ops.object.mode_set(mode='OBJECT')
    return builder


def solve_long_arm(builder,side,target,carrier=False,grasp=0.):
    rig=builder.result.rig
    upper,fore,hand=(rig.pose.bones[n+'.'+side] for n in ('upper_arm','forearm','hand'))
    shoulder=upper.head.copy()
    a=upper.bone.length*(2.4 if carrier else 1.2)
    b=fore.bone.length*(2.6 if carrier else 1.6)
    delta=target-shoulder;distance=delta.length
    if distance>a+b-.008 or distance<abs(a-b)+.008:
        target=shoulder+delta.normalized()*max(abs(a-b)+.008,min(a+b-.008,distance));delta=target-shoulder;distance=delta.length
    axis=delta.normalized();sign=1. if side=='L' else -1.
    pole=Vector((sign*(.07 if carrier else .30),-1.,.1 if carrier else -.2));bend=(pole-axis*pole.dot(axis)).normalized()
    along=(a*a-b*b+distance*distance)/(2*distance)
    elbow=shoulder+axis*along+bend*math.sqrt(max(0,a*a-along*along))
    for bone,start,end in ((upper,shoulder,elbow),(fore,elbow,target)):
        rest=bone.bone
        q=(rest.tail_local-rest.head_local).normalized().rotation_difference((end-start).normalized())@rest.matrix_local.to_quaternion()
        bone.matrix=Matrix.Translation(start)@q.to_matrix().to_4x4();bpy.context.view_layer.update()
    fingers=Vector((-sign*(1-grasp),-.4-.7*grasp,.15 if not carrier else -.8)).normalized()
    rest=hand.bone;q=(rest.tail_local-rest.head_local).normalized().rotation_difference(fingers)@rest.matrix_local.to_quaternion()
    hand.matrix=Matrix.Translation(target)@q.to_matrix().to_4x4();bpy.context.view_layer.update()


def pose(builder,kind,seconds):
    rig=builder.result.rig;rig.animation_data.action=None;builder._reset_pose()
    dormant=kind.startswith('ZombieDecayDormant');rising=kind.startswith('ZombieDecayRise')
    variant=int(kind[-1]) if dormant or rising else 2 if kind.startswith('ZombieCarrier') else 0
    carrier=variant==2
    weight=1. if dormant else 1.-smooth((seconds-.15)/(RISE-.15)) if rising else 0.
    upper_weight=1. if dormant else 1.-smooth((seconds-.75)/(RISE-.75)) if rising and carrier else weight
    walking=kind.endswith('Walk');attack='Attack' in kind;idle=kind.endswith('Idle')
    windup,active,recovery=(1.25,.26,1.25) if carrier else (PREPARE,ACTIVE,RECOVER)
    walk_seconds=1.8 if carrier else 1.6
    wave=math.sin(seconds*math.tau/8.) if idle else 0.
    swallow=(pulse(seconds,.6,1.0,1.1,1.45)+.7*pulse(seconds,1.9,2.18,2.24,2.7)+pulse(seconds,5.1,5.4,5.5,6.0)) if idle else 0.
    prep=smooth(seconds/windup)*(1.-smooth((seconds-windup)/active)) if attack else 0.
    follow=smooth((seconds-windup)/active)*(1.-smooth((seconds-windup-active)/recovery)) if attack else 0.
    hit=smooth(seconds/.09)*(1.-smooth((seconds-.09)/.36)) if kind.endswith('Hit') else 0.
    sway=math.sin(seconds*math.tau/walk_seconds) if walking else 0.
    pelvis=rig.pose.bones['pelvis'];matrix=pelvis.matrix.copy()
    matrix.translation+=Vector((.012*sway,0.,-(.40 if carrier else .15)-(.12 if carrier else .38)*weight-.025*follow))
    pelvis.matrix=matrix;bpy.context.view_layer.update()
    ordinary.rotation(rig.pose.bones['spine'],Quaternion(Vector((1,0,0)),math.radians((5 if carrier else 8)+10*weight+2*wave-4*hit)))
    ordinary.rotation(rig.pose.bones['chest'],Quaternion(Vector((1,0,0)),math.radians((3 if carrier else 0)+(48 if carrier else 42)*upper_weight+3*swallow-6*prep+7*follow)))
    ordinary.rotation(rig.pose.bones['chest'],Quaternion(Vector((0,1,0)),math.radians((3 if carrier else -4)+2*sway)))
    chest=rig.pose.bones['chest'];delta=chest.matrix@chest.bone.matrix_local.inverted()
    head=rig.pose.bones['head']
    if carrier:
        # Upper head remains dragged close to the ground as the lower human rises.
        hanging=Matrix.Translation(Vector((.018*sway,-.02*follow,-.43-.018*swallow)))
        neck=rig.pose.bones['neck'];neck.matrix=hanging@neck.bone.matrix_local
        bpy.context.view_layer.update()
        head.matrix=hanging@head.bone.matrix_local
    else:
        head.matrix=Matrix.Translation(Vector((0.,-.008*swallow,-.009*swallow)))@delta@head.bone.matrix_local
    bpy.context.view_layer.update()
    for side in ('L','R'):
        sign=1. if side=='L' else -1.
        if carrier:
            clavicle=rig.pose.bones['clavicle.'+side];m=clavicle.matrix.copy()
            m.translation+=Vector((sign*.09,0.,.42*(1-.45*upper_weight)));clavicle.matrix=m;bpy.context.view_layer.update()
            target=Vector((sign*.38,-.43,.14+.025*(1+sign*sway)))
            if attack and side==('L' if kind.endswith('Left') else 'R'):
                target=target.lerp(Vector((sign*.48,-.13,1.32)),prep).lerp(Vector((sign*.17,-.85,.32)),follow)
            target.z+=.06*weight
        else:
            target=delta@Vector((sign*.24,-.18,1.40))
            target+=Vector((0.,-.005*swallow,.005*swallow))
            if attack and side==('L' if kind.endswith('Left') else 'R'):
                target=target.lerp(delta@Vector((sign*.33,-.18,1.30)),prep).lerp(delta@Vector((sign*.06,-.82,1.18)),follow)
        solve_long_arm(builder,side,target,carrier,follow)
    for side in ('L','R'):
        ankle=builder.support(side);rotation=builder.footwork_support_rotations[side]
        if walking:
            source_seconds=seconds/walk_seconds*1.6
            ankle,_,lift=ordinary.foot_target(builder,side,source_seconds)
            if carrier:
                baseline=builder.support(side);ankle.y=baseline.y+(ankle.y-baseline.y)*(.8/1.2)
                ankle.z=baseline.z+(ankle.z-baseline.z)*.65
            rotation=Quaternion(Vector((1,0,0)),math.radians(-7*lift))@rotation
        elif attack:
            original=(ordinary.PREPARE,ordinary.ACTIVE)
            ordinary.PREPARE,ordinary.ACTIVE=windup,active
            ankle,_,lift=ordinary.attack_foot(builder,side,kind.replace('Decay','Ordinary').replace('Carrier','Ordinary'),seconds)
            ordinary.PREPARE,ordinary.ACTIVE=original
            rotation=Quaternion(Vector((1,0,0)),math.radians(-7*lift))@rotation
        builder.recovery_leg(side,ankle,(0,-1,0),rotation)
    return builder.snapshot_pose()


def author_actions(output, source):
    builder = load_builder()
    common.ANIMATION_FPS = FPS
    bpy.context.scene.render.fps = FPS
    records = []
    signature = hashlib.sha256()
    for name, duration, loop in CLIPS:
        frames = round(duration*FPS)
        keys = []
        for frame in range(frames+1):
            keys.append((frame/frames,pose(builder,name,duration*frame/frames)))
            if frame and frame%100==0: print(name, frame, '/', frames, flush=True)
        builder._create_action(name,'combat_degraded_zombie',duration,loop,frames,FPS,keys)
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['): raise ValueError('Object motion in decay bank')
            signature.update(json.dumps([name,curve.data_path,curve.array_index,
                [[round(v,7) for v in key.co] for key in curve.keyframe_points]],separators=(',',':')).encode())
        max_support = 0.
        max_scale = 0.
        for second in (0.,duration*.25,duration*.5,duration*.75,duration):
            pose(builder,name,second)
            for side in ('L','R'):
                foot = builder.result.rig.pose.bones['foot.'+side]
                if 'Walk' not in name and 'Attack' not in name:
                    max_support = max(max_support,(foot.head-builder.support(side)).length)
            for bone in builder.result.rig.pose.bones:
                max_scale = max(max_scale,max(abs(v-1) for v in bone.scale))
        if max_support>.001 or max_scale>.0001: raise ValueError('Lost grounded contacts: '+name)
        records.append(dict(name=name,duration_seconds=duration,loop=loop,maximum_support_error_m=max_support,maximum_scale_error=max_scale))
        print('Validated '+name,flush=True)
    for i in range(3):
        first = pose(builder,f'ZombieDecayDormant{i}',0.)
        rise = pose(builder,f'ZombieDecayRise{i}',0.)
        last = pose(builder,f'ZombieDecayRise{i}',RISE)
        idle = pose(builder,'ZombieCarrierIdle' if i==2 else 'ZombieDecayIdle',0.)
        if first != rise or last != idle: raise ValueError('Discontinuous decay rise endpoint')
    builder.result.rig.animation_data.action=None
    builder._reset_pose()
    common.export_animation_fbx(output/'ZombieDecayActions.fbx',builder.result)
    common.save_blend(source/'ZombieDecayActions.blend')
    payload=dict(generator=Path(__file__).name,version='2.0.0',rig='NpcHumanV2',root_motion=False,
        animation_events=0,source_fps=FPS,walk_cycle_distance_m=1.2,walk_duration_seconds=1.6,
        prepare_seconds=PREPARE,active_seconds=ACTIVE,recovery_seconds=RECOVER,rise_seconds=RISE,
        carrier=dict(walk_cycle_distance_m=.8,walk_duration_seconds=1.8,prepare_seconds=1.25,active_seconds=.26,recovery_seconds=1.25),
        signature=signature.hexdigest(),clips=records)
    (output/'ZombieDecay3D.json').write_text(json.dumps(payload,indent=2)+'\n')


def atlas(variant):
    w,h,pixels=read_generated_png(ROOT/'Assets/Resources/VillageLife/StationWorkerAtlas.png')
    canvas=PixelCanvas(w,h);canvas.pixels[:]=pixels
    # Cloth's old pale weave retains its grain; pressure/sweat, rubbed folds
    # and stretched seams are local, asymmetric, unlettered and non-emissive.
    for y in range(512):
        for x in range(512):
            index=(y*512+x)*4
            if x>=384 and y>=384: continue
            if x>=500 and y<12: continue
            local=x%128
            stain=math.exp(-((local-(54+variant*13)%100)/29)**2-((y%256-85)/56)**2)
            seam=math.exp(-((local-12)/2.5)**2)
            fold=.06*math.sin(y*.18+local*.04)**8
            old=canvas.pixels[index:index+3]
            for c in range(3): canvas.pixels[index+c]=max(0,min(255,round(old[c]*(1-.35*stain-fold-.13*seam))))
    skin=((148,143,128),(143,141,130),(150,144,131))[variant]
    canvas.rect(500,0,512,12,skin+(255,))
    glass=((111,133,88),(130,105,63),(97,125,99))[variant]
    for y in range(256):
        for x in range(256):
            # Broad, broken source-shaped reflections remain readable in local
            # black lighting. This is pigment in opaque glass, never emission.
            sheen=57*math.exp(-((x-55)/8)**2)+26*math.exp(-((x-169)/18)**2)
            sheen*=.55+.45*math.sin(y*.013+1.2)**2
            soil=23*(math.sin(x*.09+y*.055)*math.sin(y*.073))**2
            soil+=32*math.exp(-((x-85)/38)**2-((y-142)/54)**2)
            soil+=24*math.exp(-((x-181)/27)**2-((y-71)/32)**2)
            scratch=14 if (x*17+y*3)%151<2 and y%67>12 else 0
            canvas.put(x,y,tuple(max(0,min(255,round(c+sheen-soil+scratch))) for c in glass)+(255,))
    canvas.rect(240,240,256,256,(19,24,14,255))
    for y in range(128):
        for x in range(256,384):
            fold=12*math.sin(x*.19+y*.16)+7*math.sin(x*.8-y*.45)
            mottling=25*math.sin(x*.085-y*.061)*math.sin(y*.13)
            bruise=29*math.exp(-((x-296)/19)**2-((y-65)/28)**2)
            soil=42*math.exp(-((x-318)/23)**2-((y-77)/13)**2)
            crease=22*math.exp(-((y-78-4*math.sin(x*.12))/2.1)**2)
            canvas.put(x,y,tuple(max(0,min(255,round(c+fold+mottling-bruise-soil-crease))) for c in skin)+(255,))
    for y in range(128):
        for x in range(128):
            hollow=sum(math.exp(-((x-cx)/15)**2-((y-61)/12)**2) for cx in (40,88))
            cheek=sum(math.exp(-((x-cx)/12)**2-((y-85)/15)**2) for cx in (30,98))
            mottled=4*math.sin(x*.28+y*.12)*math.sin(y*.37-x*.16)
            rgb=tuple(max(0,min(255,round(v-37*hollow-(19 if variant!=1 else 8)*cheek+mottled))) for v in skin)
            canvas.put(384+x,384+y,rgb+(255,))
    for cx in (40,88):
        canvas.ellipse(384+cx,384+58,11,4,(83,72,65,255))
        canvas.line(384+cx-9,384+58,384+cx+8,384+58,(126,118,101,255),2)
        canvas.ellipse(384+cx,384+58,2,2,(37,35,32,255))
        canvas.line(384+cx-11,384+49,384+cx+9,384+47,(87,79,67,255),2)
        canvas.line(384+cx-9,384+69,384+cx+9,384+69,(113,94,80,255))
    canvas.line(443,450,440,465,(123,100,82,255),2)
    canvas.line(451,451,456,465,(177,154,126,255),2)
    canvas.ellipse(448,467,8,4,(136,105,89,255))
    canvas.line(436,484,459,486,(78,63,57,255),3)
    canvas.line(439,489,457,489,(142,105,90,255))
    # Caked mouth, unshaven chin, fine split lips; no gore or iconography.
    for y in range(96,125):
        for x in range(29,102):
            if (x*13+y*7+variant)%23==0: canvas.put(384+x,384+y,(113,106,91,255))
    from combat_zombie_horror_surfaces import decay
    return decay(canvas, variant)


def build_bodies(output,source,preview):
    for variant in range(3):
        builder=load_builder(retain_meshes=True)
        rig=builder.result.rig
        canonical={b.name:tuple(v for row in b.matrix_local for v in row) for b in rig.data.bones}
        design=geometry.sculpt(builder,variant)
        objects=sorted((o for o in bpy.data.objects if o.type=='MESH'),key=lambda o:o.name)
        records=[];signature=hashlib.sha256()
        for obj in objects:
            if obj.data.shape_keys:
                for key in obj.data.shape_keys.key_blocks:key.value=0.
            points=[obj.matrix_world@v.co for v in obj.data.vertices]
            for p in points: signature.update(json.dumps([round(v,7) for v in p]).encode())
            records.append(dict(name=obj.name,vertices=len(points),triangles=sum(len(p.vertices)-2 for p in obj.data.polygons),
                bounds_min=[min(p[i] for p in points) for i in (0,2,1)],bounds_max=[max(p[i] for p in points) for i in (0,2,1)]))
        if canonical!={b.name:tuple(v for row in b.matrix_local for v in row) for b in rig.data.bones}: raise ValueError('Changed bind skeleton')
        name=f'ZombieDecayBody{variant}'
        texture=atlas(variant);(output/(name+'Atlas.png')).write_bytes(texture)
        bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
        for obj in objects: obj.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.export_scene.fbx(filepath=str(output/(name+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},
            axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,use_custom_props=True,
            use_mesh_modifiers=False,use_armature_deform_only=False,path_mode='STRIP',mesh_smooth_type='FACE')
        passive = export_passive(builder,objects,output,variant)
        presentation = measure_presentation(builder,objects,variant)
        payload=dict(generator=Path(__file__).name,variant=variant,rig='NpcHumanV2',retained_rest_skeleton=True,
            geometry_owner='authored_decay_sculpt',bounds_space='unity_metres',signature=signature.hexdigest(),
            atlas_sha256=hashlib.sha256(texture).hexdigest(),meshes=records,passive=passive,presentation=presentation,design=design)
        (output/(name+'.json')).write_text(json.dumps(payload,indent=2)+'\n')
        builder.result.rig.animation_data.action=None;builder._reset_pose()
        common.save_blend(source/(name+'.blend'))
        if preview: render_preview(builder,objects,output/name,preview,variant)
        print('VALIDATED DECAY BODY '+str(variant)+' '+signature.hexdigest(),flush=True)


def export_passive(builder,objects,output,variant):
    """Bake the same visible skin, UVs and contacts, never a proxy silhouette."""
    selected=visible_names(variant)
    records=[]
    for label,kind in [('Dormant',f'ZombieDecayDormant{variant}'),('Standing','ZombieCarrierIdle' if variant==2 else 'ZombieDecayIdle')]:
        pose(builder,kind,0.)
        depsgraph=bpy.context.evaluated_depsgraph_get()
        vertices=[];faces=[];uvs=[];roles=[]
        for obj in objects:
            if obj.name not in selected:continue
            evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
            start=len(vertices)
            vertices.extend(tuple(obj.matrix_world@v.co) for v in mesh.vertices)
            uv=mesh.uv_layers.active
            for polygon in mesh.polygons:
                role='glass' if obj.name=='GEO_Torso' and polygon.material_index==1 else role_for(obj.name)
                faces.append(tuple(start+i for i in polygon.vertices));roles.append(ROLE_ORDER.index(role))
                uvs.append([tuple(uv.data[i].uv) for i in polygon.loop_indices])
            evaluated.to_mesh_clear()
        mesh=bpy.data.meshes.new(f'Decay{label}{variant}');mesh.from_pydata(vertices,[],faces);mesh.update()
        uv=mesh.uv_layers.new(name='UVMap')
        for polygon,coords,role in zip(mesh.polygons,uvs,roles):
            polygon.material_index=role;polygon.use_smooth=True
            for index,value in zip(polygon.loop_indices,coords):uv.data[index].uv=value
        for role in ROLE_ORDER:mesh.materials.append(bpy.data.materials.new('Decay '+role))
        obj=bpy.data.objects.new(mesh.name,mesh);bpy.context.collection.objects.link(obj)
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
        path=output/f'ZombieDecay{label}{variant}.fbx'
        bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
            bake_anim=False,use_mesh_modifiers=False,path_mode='STRIP',mesh_smooth_type='FACE')
        records.append(dict(name=path.stem,clip=kind,sample_seconds=0.,surfaces=list(ROLE_ORDER),vertices=len(vertices),
            triangles=sum(len(f)-2 for f in faces),source_renderers=len(selected),
            bounds_min=[min(p[i] for p in vertices) for i in (0,2,1)],bounds_max=[max(p[i] for p in vertices) for i in (0,2,1)]))
        bpy.data.objects.remove(obj,do_unlink=True);bpy.data.meshes.remove(mesh)
    return records


def measure_presentation(builder,objects,variant):
    prefix='ZombieCarrier' if variant==2 else 'ZombieDecay'
    prepare,active=(1.25,.26) if variant==2 else (PREPARE,ACTIVE)
    records=[]
    for phase,seconds in [('anticipation',prepare),('strike_end',prepare+active)]:
        pose(builder,prefix+'AttackRight',seconds)
        depsgraph=bpy.context.evaluated_depsgraph_get();points=[]
        for obj in objects:
            evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
            points.extend(obj.matrix_world@v.co for v in mesh.vertices)
            evaluated.to_mesh_clear()
        records.append(dict(phase=phase,seconds=seconds,
            bounds_min=[min(p[i] for p in points) for i in (0,2,1)],
            bounds_max=[max(p[i] for p in points) for i in (0,2,1)],
            right_hand=[builder.result.rig.pose.bones['hand.R'].head[i] for i in (0,2,1)]))
    return records


def render_preview(builder,objects,name,directory,variant):
    directory.mkdir(parents=True,exist_ok=True)
    image=bpy.data.images.load(str(name)+'Atlas.png',check_existing=False)
    selected=visible_names(variant);materials={}
    for obj in objects:
        obj.hide_render=obj.name not in selected
        roles=('skin','glass') if obj.name=='GEO_Torso' else (role_for(obj.name),)
        indices=[p.material_index for p in obj.data.polygons]
        obj.data.materials.clear()
        for role in roles:
            if role not in materials:
                material=bpy.data.materials.new('Decay review '+role);material.use_nodes=True
                nodes=material.node_tree.nodes;links=material.node_tree.links
                shader=nodes.get('Principled BSDF');shader.inputs['Roughness'].default_value=.32 if role=='glass' else .86
                texture=nodes.new('ShaderNodeTexImage');texture.image=image;texture.interpolation='Closest'
                tint=nodes.new('ShaderNodeMixRGB');tint.blend_type='MULTIPLY';tint.inputs[0].default_value=1.
                tint.inputs[2].default_value=tuple(((c+.055)/1.055)**2.4 if c>.04045 else c/12.92 for c in role_colour(role,variant))+(1,)
                links.new(texture.outputs['Color'],tint.inputs[1]);links.new(tint.outputs[0],shader.inputs['Base Color'])
                materials[role]=material
            obj.data.materials.append(materials[role])
        for p,i in zip(obj.data.polygons,indices):p.material_index=i if obj.name=='GEO_Torso' else 0
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12
    scene.render.resolution_x=800;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.world.color=(.045,.045,.045)
    for old in list(bpy.data.objects):
        if old.type in ('CAMERA','LIGHT'):bpy.data.objects.remove(old,do_unlink=True)
    camera_data=bpy.data.cameras.new('Decay review');camera=bpy.data.objects.new('Decay review',camera_data)
    scene.collection.objects.link(camera);camera.location=(2.8,-4.2,2.05)
    camera.rotation_euler=(Vector((0,0,.9))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.type='ORTHO';camera_data.ortho_scale=2.5 if variant==2 else 2.1;scene.camera=camera
    for label,location,power,size,color in [('Key',(-2,-3,4),240,3,(.75,.82,.75)),('Rim',(2,1,3),150,2,(.56,.63,.68))]:
        data=bpy.data.lights.new(label,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
        obj=bpy.data.objects.new(label,data);scene.collection.objects.link(obj);obj.location=location
        obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
    prefix='ZombieCarrier' if variant==2 else 'ZombieDecay'
    for label,kind,seconds in [('idle',prefix+'Idle',1.7),('dormant',f'ZombieDecayDormant{variant}',0),('tell',prefix+'AttackRight',1.1 if variant==2 else .8),('side',prefix+'Idle',0)]:
        camera.location=(3.2,-.3,1.4) if label=='side' else (2.3,-4.2,1.65)
        camera.rotation_euler=(Vector((0,-.18,.95))-camera.location).to_track_quat('-Z','Y').to_euler()
        pose(builder,kind,seconds);scene.render.filepath=str(directory/f'variant{variant}-{label}.png');bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--output-dir',type=Path,default=OUT);parser.add_argument('--source-dir',type=Path,default=SOURCE)
    parser.add_argument('--preview-dir',type=Path);parser.add_argument('--bodies-only',action='store_true');parser.add_argument('--actions-only',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    args.output_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
    if not args.bodies_only:author_actions(args.output_dir,args.source_dir)
    if not args.actions_only:build_bodies(args.output_dir,args.source_dir,args.preview_dir)
    print('DEGRADED ZOMBIE GROUNDED ACTIONS AND THREE BODY SCULPTS OK',flush=True)


if __name__=='__main__':main()
