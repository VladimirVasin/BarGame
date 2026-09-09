#!/usr/bin/env python3
"""Finite water pail and two additional NPC actions; existing cast/banks are read-only."""
from __future__ import annotations
import argparse, hashlib, importlib.util, json, math, sys
from pathlib import Path
import bpy
from mathutils import Matrix, Quaternion, Vector
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
def module(name,file):
    spec=importlib.util.spec_from_file_location(name,ROOT/'tools'/file)
    result=importlib.util.module_from_spec(spec);sys.modules[name]=result;spec.loader.exec_module(result);return result
p=module('errand_props','build-village-life-props-3d-model.py')
v=module('errand_people','build-village-residents-3d-model.py')
w=module('errand_workroom','build-village-workroom-actions-3d-model.py')

def meta(path):
    out=path.with_name(path.name+'.meta')
    if not out.exists():out.write_text('fileFormatVersion: 2\nguid: '+hashlib.sha256(path.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]+'\n',encoding='utf8')

def vessel():
    vertices=[];faces=[];n=16
    for radius,height in ((.19,0),(.245,.33),(.227,.33),(.173,.022)):
        for i in range(n):
            a=i*math.tau/n;vertices.append((radius*math.cos(a),height,radius*math.sin(a)))
    for ring in range(3):
        for i in range(n):
            j=(i+1)%n;faces.append((ring*n+i,ring*n+j,(ring+1)*n+j,(ring+1)*n+i))
    faces.append(tuple(range(n-1,-1,-1)));faces.append(tuple(3*n+i for i in range(n)))
    geometry=(vertices,faces)
    if p.bp.signed_volume(p.bp.to_source(geometry))<0:geometry=(vertices,[tuple(reversed(f)) for f in faces])
    return geometry

def make_props():
    handles=[]
    for sign in (-1,1):
        points=[(sign*.24,.25,-.065),(sign*.285,.375,-.065),(sign*.29,.43,0),(sign*.285,.375,.065),(sign*.24,.25,.065)]
        handles.extend(p.beam(a,b,.021) for a,b in zip(points,points[1:]))
    rows=[]
    for name,geom,tint,surface in (('BucketBody',vessel(),(.34,.335,.29,1),'RustedIron'),
        ('BucketHandles',p.kit.merge_all(handles),(.235,.225,.20,1),'RustedIron'),
        ('Water',p.cylinder((0,.068,0),.358,.006,16),(.30,.40,.38,1),'Water')):
        low,high=p.kit.bounds(geom)
        rows.append({'mesh':name,'role':name,'surface':surface,'tint':tint,'geometry':geom,'bounds_min':low,'bounds_max':high})
    return rows

def prop_bank(assets,sources,validate):
    rows=make_props();signature=hashlib.sha256(json.dumps(rows,sort_keys=True).encode()).hexdigest()
    assert signature==hashlib.sha256(json.dumps(make_props(),sort_keys=True).encode()).hexdigest()
    for row in rows:
        geom=row['geometry'];edges={}
        for face in geom[1]:
            for a,b in zip(face,face[1:]+face[:1]):edges[a,b]=edges.get((a,b),0)+1
        assert all(n==edges.get((b,a),0) for (a,b),n in edges.items()),row['mesh']
        assert p.bp.signed_volume(p.bp.to_source(geom))>1e-8,row['mesh']
    manifest={'version':'1.0.0','scale_mode':'fixed_metres','build_signature':signature,
        'parts':[{k:value for k,value in row.items() if k not in ('geometry','role')} for row in rows],
        'anchors':[{'name':'LeftGrip','position':[-.29,.43,0]},{'name':'RightGrip','position':[.29,.43,0]},
                   {'name':'Rest','position':[0,0,0]},{'name':'Mouth','position':[0,.33,0]}]}
    out=assets/'VillageErrandProps.json'
    if validate:
        assert json.loads(out.read_text(encoding='utf8'))==json.loads(json.dumps(manifest));return
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    root=bpy.data.objects.new('WaterBucket',None);bpy.context.scene.collection.objects.link(root)
    for row in rows:p.make_mesh(row,root)
    for anchor in manifest['anchors']:
        a=bpy.data.objects.new('ANCHOR_'+anchor['name'],None);bpy.context.scene.collection.objects.link(a);a.parent=root
        x,y,z=anchor['position'];a.location=(x,z,y)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(assets/'VillageErrandProps.fbx'),use_selection=True,object_types={'EMPTY','MESH'},
        axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,add_leaf_bones=False,bake_anim=False)
    out.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf8')
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(sources/'VillageErrandProps.blend'))
    scene=bpy.context.scene;camera=bpy.data.objects.new('Review',bpy.data.cameras.new('Review'));scene.collection.objects.link(camera)
    camera.location=(1.1,1.3,1.0);camera.rotation_euler=(Vector((0,0,.22))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO';camera.data.ortho_scale=.90;scene.camera=camera;scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.color_type='MATERIAL';scene.display.shading.show_cavity=True
    scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
    scene.render.filepath=str(sources/'VillageErrandProps.png');bpy.ops.render.render(write_still=True)
    print('Bucket closed-solid metre signature '+signature,flush=True)

def sample(t):
    # Move over the actual rim before lowering the sideways mouth into the
    # shallow water, then lift clear before turning upright again.
    keys=((0,(0,.50,.44),0,0),(1.25,(0,.66,.44),0,0),
          (2.35,(0,.66,1.194),-90,.5),(3.25,(0,.265,1.194),-90,1),
          (4.75,(0,.265,1.194),-90,1),(5.65,(0,.66,1.194),-90,.5),
          (6.5,(0,.66,.44),0,0),(8,(0,.50,.44),0,0))
    t=max(0,min(8,t))
    for a,b in zip(keys,keys[1:]):
        if a[0]<=t<=b[0]:
            blend=v.smooth((t-a[0])/(b[0]-a[0]));position=Vector(a[1]).lerp(Vector(b[1]),blend)
            degrees=a[2]+(b[2]-a[2])*blend;weight=a[3]+(b[3]-a[3])*blend
            rotation=v.unity_rotation(degrees)
            return position,rotation,weight
    raise AssertionError(t)

def author(result,t):
    rig=result.rig;base=v.base;scale=v.LIFE_HEIGHTS['BasketVisitor']/1.75
    position,rotation,weight=sample(t)
    body=v.action_pose('Carry',0);body['spine']=base.BonePose(rotation_degrees=(16+52*weight,0,0))
    body['head']=base.BonePose(rotation_degrees=(-10*weight,0,0))
    base.reset_pose(rig);base.apply_pose(rig,body)
    if weight>0:
        pelvis=rig.pose.bones['pelvis'];m=pelvis.matrix.copy();m.translation.z-=.38*weight/scale;m.translation.y-=.15*weight/scale;pelvis.matrix=m;bpy.context.view_layer.update()
        for side in ('L','R'):w.solve_leg(base,rig,side,rig.data.bones['foot.'+side].head_local.copy())
    deps=bpy.context.evaluated_depsgraph_get();low=min(base.evaluated_part_min_z(part,deps) for part in result.parts if part.bone.startswith('foot.'))
    pelvis=rig.pose.bones['pelvis'];m=pelvis.matrix.copy();m.translation.z-=low;pelvis.matrix=m;bpy.context.view_layer.update()
    contacts={side:v.world_target(position+rotation @ Vector((sign*.29,.43,0)),scale) for side,sign in (('L',-1),('R',1))}
    for side,target in contacts.items():
        hand=rig.pose.bones['hand.'+side];socket=rig.pose.bones['SOCKET_Grip.'+side];upper=rig.pose.bones['upper_arm.'+side]
        q=hand.matrix.to_quaternion();aim=(socket.head-hand.head).rotation_difference(target-upper.head) @ q
        hand.matrix=Matrix.Translation(hand.head.copy()) @ q.slerp(aim,weight).to_matrix().to_4x4();bpy.context.view_layer.update()
    v.solve_grips(rig,contacts,True)
    return position,rotation,weight,contacts

def station_sample(t):
    weight=v.smooth(t/1.5) if t<1.5 else 1-v.smooth((t-4.5)/1.5)
    rotation=v.unity_rotation(z=math.sin(t*math.pi)*3*weight)
    forward=Vector((.66,0,-.175)).normalized();right=Vector((forward.z,0,-forward.x))
    dock=Vector((-.66,0,.49));strap=Vector((0,.8,.315))
    def actor(point):
        delta=point-dock;return Vector((delta.dot(right),delta.y,delta.dot(forward)))
    contacts={side:actor(strap+rotation @ Vector(point)) for side,point in
              (('R',(0,0,0)),('L',(0,-.19,.034)))}
    return weight,rotation,contacts


def author_station(result,t):
    rig=result.rig;base=v.base;scale=1.78/1.75
    weight,rotation,world_contacts=station_sample(t)
    body=dict(base.CITIZEN_HANGING_ARMS)
    body['spine']=base.BonePose(rotation_degrees=(60*weight,0,0))
    body['head']=base.BonePose(rotation_degrees=(-12*weight,0,0))
    base.reset_pose(rig);base.apply_pose(rig,body)
    if weight>0:
        pelvis=rig.pose.bones['pelvis'];m=pelvis.matrix.copy()
        m.translation.z-=.27*weight/scale;m.translation.y-=.065*weight/scale
        pelvis.matrix=m;bpy.context.view_layer.update()
        for side in ('L','R'):w.solve_leg(base,rig,side,rig.data.bones['foot.'+side].head_local.copy())
    deps=bpy.context.evaluated_depsgraph_get()
    low=min(base.evaluated_part_min_z(part,deps) for part in result.parts if part.bone.startswith('foot.'))
    pelvis=rig.pose.bones['pelvis'];m=pelvis.matrix.copy();m.translation.z-=low
    pelvis.matrix=m;bpy.context.view_layer.update()
    contacts={}
    for side,point in world_contacts.items():
        hand=rig.pose.bones['hand.'+side];socket=rig.pose.bones['SOCKET_Grip.'+side]
        target=v.world_target(point,scale);start=socket.head.copy()
        q=hand.matrix.to_quaternion();aim=(socket.head-hand.head).rotation_difference(target-rig.pose.bones['upper_arm.'+side].head) @ q
        hand.matrix=Matrix.Translation(hand.head.copy()) @ q.slerp(aim,weight).to_matrix().to_4x4()
        bpy.context.view_layer.update();contacts[side]=start.lerp(target,weight)
    if weight>0:v.solve_grips(rig,contacts,weight>.99999)
    return weight,rotation,world_contacts


def station_bank(result):
    rig=result.rig;action=bpy.data.actions.new('StationStrap');action.use_fake_user=True
    action.use_frame_range=True;action.frame_start=0;action.frame_end=144;action.use_cyclic=False
    rig.animation_data.action=action;previous={};curves=[]
    for frame in range(145):
        try:author_station(result,frame/24)
        except RuntimeError as error:raise RuntimeError(f'StationStrap frame {frame}: {error}') from error
        for bone in rig.pose.bones:
            bone.rotation_mode='QUATERNION'
            if bone.name in previous:bone.rotation_quaternion.make_compatible(previous[bone.name])
            previous[bone.name]=bone.rotation_quaternion.copy()
            for channel in ('location','rotation_quaternion','scale'):bone.keyframe_insert(channel,frame=frame,group=bone.name)
    for curve in v.base.iter_action_fcurves(action):
        for key in curve.keyframe_points:key.interpolation='LINEAR'
        curves.append((curve.data_path,curve.array_index,[[round(k.co.x,7),round(k.co.y,7)] for k in curve.keyframe_points]))
    error=0.;floor=0.;margin=10.;scale=1.78/1.75
    for frame in range(145):
        bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
        weight,_,contacts=station_sample(frame/24)
        if weight>.99999:
            for side,point in contacts.items():
                target=v.world_target(point,scale);hand=rig.pose.bones['hand.'+side]
                socket=rig.pose.bones['SOCKET_Grip.'+side];upper=rig.pose.bones['upper_arm.'+side];lower=rig.pose.bones['forearm.'+side]
                error=max(error,(socket.head-target).length*scale)
                wrist=target-(socket.head-hand.head)
                margin=min(margin,(upper.length+lower.length-(wrist-upper.head).length)*scale)
        deps=bpy.context.evaluated_depsgraph_get()
        floor=max(floor,abs(min(v.base.evaluated_part_min_z(part,deps) for part in result.parts if part.bone.startswith('foot.')))*scale)
    assert error<.002 and floor<.002 and margin>=.03,(error,floor,margin)
    return {'duration_seconds':6,'contact_start':1.5,'contact_end':4.5,
        'dock_crate_frame':[-.66,0,.49],'facing_crate_frame':[.66,0,-.175],
        'right_anchor':'Fastener','left_anchor':'Grip','max_grip_error_m':error,
        'max_floor_error_m':floor,'minimum_arm_reach_margin_m':margin,'sampled_frames':145,
        'curve_signature':hashlib.sha256(json.dumps(curves,separators=(',',':')).encode()).hexdigest()}


def action_bank(assets,sources,validate):
    protected=[f for f in assets.iterdir() if f.name.startswith(('StationWorker','WoodWoman','RepairNeighbor','SewingWoman','SnowNeighbor','BasketVisitor','VillageResidentActions.','VillageResidentLifeActions.','VillageResidentWorkroomActions.')) and f.suffix in ('.fbx','.json','.png')]
    hashes={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in protected}
    result=v.LifeResidentBuilder('BasketVisitor').build();rig=result.rig;action=bpy.data.actions.new('BucketFill');action.use_fake_user=True
    action.use_frame_range=True;action.frame_start=0;action.frame_end=192;action.use_cyclic=False
    rig.animation_data_create().action=action;frames=[];previous={};curves=[]
    for frame in range(193):
        try:position,rotation,weight,contacts=author(result,frame/24)
        except RuntimeError as error:raise RuntimeError(f'BucketFill frame {frame}: {error}') from error
        frames.append({'position':[round(x,7) for x in position],'rotation':[round(x,7) for x in (rotation.x,rotation.y,rotation.z,rotation.w)],'dip_weight':round(weight,7)})
        for bone in rig.pose.bones:
            bone.rotation_mode='QUATERNION'
            if bone.name in previous:bone.rotation_quaternion.make_compatible(previous[bone.name])
            previous[bone.name]=bone.rotation_quaternion.copy()
            for channel in ('location','rotation_quaternion','scale'):bone.keyframe_insert(channel,frame=frame,group=bone.name)
    for curve in v.base.iter_action_fcurves(action):
        for key in curve.keyframe_points:key.interpolation='LINEAR'
        curves.append((curve.data_path,curve.array_index,[[round(k.co.x,7),round(k.co.y,7)] for k in curve.keyframe_points]))
    error=0.;floor=0.;scale=v.LIFE_HEIGHTS['BasketVisitor']/1.75
    for frame in range(193):
        bpy.context.scene.frame_set(frame);bpy.context.view_layer.update();position,rotation,_=sample(frame/24)
        for side,sign in (('L',-1),('R',1)):
            target=v.world_target(position+rotation @ Vector((sign*.29,.43,0)),scale)
            error=max(error,(rig.pose.bones['SOCKET_Grip.'+side].head-target).length*scale)
        deps=bpy.context.evaluated_depsgraph_get();floor=max(floor,abs(min(v.base.evaluated_part_min_z(part,deps) for part in result.parts if part.bone.startswith('foot.')))*scale)
    assert error<.002 and floor<.002,(error,floor)
    validation={'max_grip_error_m':error,'max_floor_error_m':floor,'sampled_frames':193}
    manifest={'version':'1.0.0','fps':24,'bone_count':31,'duration_seconds':8,'frames':frames,'validation':validation,
        'curve_signature':hashlib.sha256(json.dumps(curves,separators=(',',':')).encode()).hexdigest()}
    manifest['station_strap']=station_bank(result)
    path=assets/'VillageResidentErrandActions.json'
    if validate:assert json.loads(path.read_text(encoding='utf8'))==manifest
    else:
        rig.animation_data.action=None;v.base.reset_pose(rig)
        v.base.export_animation_fbx(assets/'VillageResidentErrandActions.fbx',result)
        path.write_text(json.dumps(manifest,separators=(',',':'))+'\n',encoding='utf8')
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(sources/'VillageResidentErrandActions.blend'))
        render_pose(result,assets,sources)
        render_station(v.ResidentBuilder(False).build(),assets,sources)
    assert hashes=={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in protected},'Existing cast or bank was modified'
    print('BucketFill '+json.dumps(validation),flush=True)
    print('StationStrap '+json.dumps(manifest['station_strap']),flush=True)

def render_pose(result,assets,sources):
    result.rig.animation_data_create().action=None
    position,rotation,_,_=author(result,4)
    scale=v.LIFE_HEIGHTS['BasketVisitor']/1.75;result.root.scale=(scale,scale,scale)
    conversion=Matrix(((-1,0,0),(0,0,-1),(0,1,0))).to_4x4();swap=Matrix(((1,0,0),(0,0,1),(0,1,0))).to_4x4()
    root=bpy.data.objects.new('ReviewBucket',None);bpy.context.scene.collection.objects.link(root)
    root.matrix_world=conversion @ Matrix.Translation(position) @ rotation.to_matrix().to_4x4() @ swap
    for row in make_props():
        if row['mesh']!='Water':p.make_mesh(row,root)
    render_review(result,assets/'BasketVisitorAtlas.png',sources/'VillageResidentBucketFill.png')


def render_station(result,assets,sources):
    result.rig.animation_data_create().action=None;author_station(result,2.5)
    scale=1.78/1.75;result.root.scale=(scale,scale,scale)
    conversion=Matrix(((-1,0,0),(0,0,-1),(0,1,0))).to_4x4();swap=Matrix(((1,0,0),(0,0,1),(0,1,0))).to_4x4()
    forward=Vector((.66,0,-.175)).normalized();right=Vector((forward.z,0,-forward.x))
    actor_from_crate=Matrix((right,(0,1,0),forward)).to_4x4() @ Matrix.Translation((.66,0,-.49))
    props={row['kind']:row for row in p.make_props()}
    _,strap_rotation,_=station_sample(2.5)
    for kind,position,rotation in (('StationCrate',(0,0,0),v.unity_rotation()),
        ('StationLid',(0,.8,-.3),v.unity_rotation(-55)),('StationStrap',(0,.8,.315),strap_rotation)):
        root=bpy.data.objects.new('Review'+kind,None);bpy.context.scene.collection.objects.link(root)
        root.matrix_world=conversion @ actor_from_crate @ Matrix.Translation(position) @ rotation.to_matrix().to_4x4() @ swap
        for part in props[kind]['parts']:p.make_mesh(part,root)
    render_review(result,assets/'StationWorkerAtlas.png',sources/'VillageResidentStationStrap.png')


def render_review(result,atlas,path):
    mat=result.material;mat.use_nodes=True;nodes=mat.node_tree.nodes;shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    info=nodes.new('ShaderNodeObjectInfo');tex=nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(atlas));tex.interpolation='Closest'
    mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1
    mat.node_tree.links.new(info.outputs['Color'],mix.inputs[1]);mat.node_tree.links.new(tex.outputs['Color'],mix.inputs[2]);mat.node_tree.links.new(mix.outputs[0],shader.inputs['Base Color'])
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE_NEXT' if bpy.app.version<(4,2,0) else 'CYCLES';scene.cycles.samples=16
    scene.world.color=(.22,.24,.26)
    for location,power in (((-3,-4,5),800),((3,0,4),600)):
        data=bpy.data.lights.new('ReviewSoftbox','AREA');data.energy=power;data.size=4
        light=bpy.data.objects.new('ReviewSoftbox',data);scene.collection.objects.link(light);light.location=location
        light.rotation_euler=(Vector((0,-.5,.7))-light.location).to_track_quat('-Z','Y').to_euler()
    camera=bpy.data.objects.new('ReviewCamera',bpy.data.cameras.new('ReviewCamera'));scene.collection.objects.link(camera)
    camera.location=(2.7,-3.8,1.9);camera.rotation_euler=(Vector((0,-.5,.65))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO';camera.data.ortho_scale=2.0;scene.camera=camera
    scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--validate-only',action='store_true');parser.add_argument('--props-only',action='store_true');parser.add_argument('--actions-only',action='store_true');parser.add_argument('--preview-only',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    assets=ROOT/'Assets/Resources/VillageLife';sources=ROOT/'ArtSource/VillageLife'
    assets.mkdir(parents=True,exist_ok=True);sources.mkdir(parents=True,exist_ok=True)
    if args.preview_only:
        render_pose(v.LifeResidentBuilder('BasketVisitor').build(),assets,sources);return
    if not args.actions_only:prop_bank(assets,sources,args.validate_only)
    if not args.props_only:action_bank(assets,sources,args.validate_only)
    if not args.validate_only:
        for base in ('VillageErrandProps','VillageResidentErrandActions'):
            for suffix in ('.fbx','.json'):
                path=assets/(base+suffix)
                if path.exists():meta(path)
if __name__=='__main__':main()
