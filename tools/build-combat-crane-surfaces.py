"""Independent Crane industrial-mutant sculpt and ImageGen-derived surfaces.

Run with run-blender.py and staged --output-dir/--source-dir. The canonical bind
rig is retained; every anatomical surface and grip is independently authored.
Reference-pose unwrap and physical wear are baked offline, never per frame.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import zlib

import bpy
import numpy as np
from mathutils import Vector

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import crane_mutant_geometry
SIZE, RESPONSE_SIZE = 2048, 1024
OUT = ROOT / 'Assets/Resources/Combat'
SOURCE = ROOT / 'ArtSource/CombatCrane'
SOURCE_IMAGE = SOURCE / 'Textures/CraneSurface.source.png'
HIDDEN = ('GEO_Ear', 'GEO_EarFold', 'GEO_HandFinger', 'GEO_HandThumb')
BONES = {}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def png(array):
    array = np.ascontiguousarray(np.clip(array * 255 + .5, 0, 255).astype(np.uint8))
    h, w, channels = array.shape
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    # Blender UV rows begin at the bottom; PNG rows begin at the top.
    data = b''.join(b'\0' + row.tobytes() for row in array[::-1])
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6 if channels == 4 else 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(data, 9)) + chunk(b'IEND', b'')


def load_crane():
    spec = importlib.util.spec_from_file_location('crane_surface_actions', ROOT / 'tools/build-combat-crane-3d-model.py')
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def source_pixels():
    image = bpy.data.images.load(str(SOURCE_IMAGE), check_existing=True)
    image.colorspace_settings.name = 'Non-Color'
    values = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(values)
    values = values.reshape(image.size[1], image.size[0], 4)[..., :3]
    h, w = values.shape[:2]
    regions=(values[h//2:, :w//2], values[h//2:, w//2:], values[:h//2, :w//2], values[:h//2, w//2:])
    # Give each physical material its own value range. Fine source variation
    # stays subordinate to the large anatomical shapes at gameplay distance.
    targets=((.48,.46,.39),(.31,.33,.27),(.34,.27,.22),(.22,.24,.22))
    gains=(.28,.34,.31,.48)
    return tuple(np.clip((image-image.mean(axis=(0,1)))*gain+target,.012,.85)
        for image,target,gain in zip(regions,targets,gains))


def sample(image, uv):
    # Mirrored source coordinates have no discontinuity at repetition boundaries.
    uv = 1 - np.abs(np.mod(uv, 2) - 1)
    xy = uv * np.array((image.shape[1] - 3, image.shape[0] - 3)) + 1
    a = np.floor(xy).astype(int)
    b = xy - a
    return ((image[a[..., 1], a[..., 0]] * (1-b[..., 0, None]) + image[a[..., 1], a[..., 0]+1] * b[..., 0, None]) * (1-b[..., 1, None]) +
            (image[a[..., 1]+1, a[..., 0]] * (1-b[..., 0, None]) + image[a[..., 1]+1, a[..., 0]+1] * b[..., 0, None]) * b[..., 1, None])


def field(points, normals, name, sources, height_only=False):
    # Large sculpted plates own midtones; tendon is confined to actual joints.
    p=points;x,y,z=(p[...,i] for i in range(3))
    material=np.zeros_like(x,dtype=np.int8)
    if not name.startswith('GEO_'):material[:]=3
    elif name.startswith(('GEO_Torso','GEO_LegsUpper','GEO_Hand','GEO_Foot')):
        material[:]=1
        if name=='GEO_Torso':material[z<1.115]=2
        if name.startswith(('GEO_Hand','GEO_Foot')):material[:]=3
    elif name=='GEO_Neck':material[:]=2
    elif name.startswith('GEO_Arm'):
        side=name[-1]
        a,b=BONES['upper_arm.'+side];_,c=BONES['forearm.'+side]
        def distance_to_segment(start,end):
            direction=end-start
            t=np.clip(((p-start)*direction).sum(axis=-1)/np.dot(direction,direction),0,1)
            return ((p-start-t[...,None]*direction)**2).sum(axis=-1)
        material[:]=1
        axis=(c-a)/np.linalg.norm(c-a)
        elbow=np.abs(((p-b)*axis).sum(axis=-1))<.025
        shoulder=np.linalg.norm(p-a,axis=-1)<.075
        material[elbow|shoulder]=2
    elif name.startswith('GEO_LegsLower'):
        a,b=BONES['shin.'+name[-1]]
        axis=(b-a)/np.linalg.norm(b-a)
        knee=np.abs(((p-a)*axis).sum(axis=-1))<.028
        ankle=np.abs(((p-b)*axis).sum(axis=-1))<.020
        material[knee|ankle]=2
    elif name in ('GEO_Head','GEO_FaceSurface'):
        material[:]=0
        material[(z>1.69)|(z<1.53)]=1
    grain=np.sin(x*101+y*79+z*23)
    ribs=.5+.5*np.sin(z*96+np.sin(x*25+y*18)*.7)
    heights=(.003*grain,.010*np.maximum(0,np.sin(z*46+x*19))**6,.012*ribs+.003*grain,.003*grain)
    height=np.choose(material,heights)
    if height_only:return height
    weights=np.abs(normals)**5
    weights/=np.maximum(weights.sum(axis=-1,keepdims=True),1e-9)
    projections=(p[...,(1,2)],p[...,(0,2)],p[...,(0,1)])
    rgb=np.zeros(p.shape,dtype=np.float64)
    for index in np.unique(material):
        surface=sum(sample(sources[index],coords*(3.2 if index<3 else .8)+(.17,.31))*weights[...,axis,None]
            for axis,coords in enumerate(projections))
        selected=material==index
        rgb[selected]=surface[selected]
    # Sparse blind seams follow the physical brow, without human painted eyes.
    if name=='GEO_Head':
        slit=(y<-.045)&(np.abs(z-(1.626+np.abs(x)*.11))<.004)&(np.abs(x)>.046)
        rgb[slit]*=.28
    roughness=np.choose(material,(.74,.61,.46,.38))+.035*grain
    cavity=np.choose(material,(.96,.94,.83+.08*ribs,.90))
    return np.clip(rgb,0,1),height,roughness,cavity


def unwrap(objects, coordinates, rectangle):
    helpers = []
    for obj in objects:
        mesh = bpy.data.meshes.new('CraneUnwrap')
        mesh.from_pydata(coordinates[obj.name], [], [list(poly.vertices) for poly in obj.data.polygons])
        helper = bpy.data.objects.new('Unwrap_' + obj.name, mesh)
        bpy.context.collection.objects.link(helper)
        helpers.append(helper)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in helpers: obj.select_set(True)
    bpy.context.view_layer.objects.active = helpers[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(70), island_margin=.008, area_weight=1., correct_aspect=True, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    x, y, w, h = rectangle
    for obj, helper in zip(objects, helpers):
        old = obj.data.uv_layers.active
        if old: old.name = 'CraneSource'
        else: old = obj.data.uv_layers.new(name='CraneSource')
        values = [v.uv.copy() for v in old.data]
        # Channel order is contract: unique UV0, original UV1.
        for layer in list(obj.data.uv_layers): obj.data.uv_layers.remove(layer)
        atlas = obj.data.uv_layers.new(name='CraneAtlas')
        source = obj.data.uv_layers.new(name='CraneSource')
        for index, value in enumerate(helper.data.uv_layers.active.data):
            atlas.data[index].uv = (x+value.uv.x*w, y+value.uv.y*h)
            source.data[index].uv = values[index]
        obj.data.uv_layers.active_index=0
        atlas.active_render=True
        bpy.data.objects.remove(helper, do_unlink=True)


def paint(objects, sources, size, response=False):
    channels=4 if response else 3
    canvas=np.zeros((size,size,channels),dtype=np.float32)
    occupied=np.zeros((size,size),dtype=bool)
    if response: canvas[:]=(.5,.5,.85,.95)
    else: canvas[:]=(.11,.09,.065)
    for obj in objects:
        mesh=obj.data;mesh.calc_loop_triangles()
        vertices=np.array([obj.matrix_world@v.co for v in mesh.vertices])
        for tri in mesh.loop_triangles:
            uv=np.array([mesh.uv_layers[0].data[i].uv[:] for i in tri.loops])
            points=vertices[list(tri.vertices)]
            normal=np.cross(points[1]-points[0],points[2]-points[0]);length=np.linalg.norm(normal)
            determinant=np.linalg.det(uv[1:]-uv[0])
            if abs(determinant)<1e-12 or length<1e-12:continue
            normal/=length
            low=np.maximum(np.floor(uv.min(axis=0)*size-.5).astype(int),0)
            high=np.minimum(np.ceil(uv.max(axis=0)*size+.5).astype(int),size-1)
            xx,yy=np.meshgrid(np.arange(low[0],high[0]+1),np.arange(low[1],high[1]+1))
            pixel=np.stack((xx,yy),axis=-1)
            bary=( (pixel+.5)/size-uv[0])@np.linalg.inv(uv[1:]-uv[0])
            inside=(bary[...,0]>=-1e-6)&(bary[...,1]>=-1e-6)&(bary.sum(axis=-1)<=1+1e-6)
            if not inside.any():continue
            position=points[0]+bary@(points[1:]-points[0])
            rgb,height,roughness,cavity=field(position,normal,obj.name,sources)
            if response:
                basis=np.linalg.solve(uv[1:]-uv[0],points[1:]-points[0])
                basis/=np.maximum(np.linalg.norm(basis,axis=1,keepdims=True),1e-9)
                slopes=[]
                for direction in basis:
                    plus=field(position+direction*.0007,normal,obj.name,sources,True)
                    minus=field(position-direction*.0007,normal,obj.name,sources,True)
                    slopes.append(np.clip((plus-minus)/.0014*.07,-.55,.55))
                output=np.stack((.5+slopes[0]*.5,.5+slopes[1]*.5,roughness,cavity),axis=-1)
            else:output=rgb
            target=np.s_[low[1]:high[1]+1,low[0]:high[0]+1]
            canvas[target][inside]=output[inside]
            occupied[target][inside]=True
    # Mip-safe island gutters, with response kept registered to the same UV0.
    for _ in range(8 if response else 16):
        fresh=occupied.copy()
        for axis,shift in ((0,-1),(0,1),(1,-1),(1,1)):
            mask=np.roll(occupied,shift,axis)&~fresh
            canvas[mask]=np.roll(canvas,shift,axis)[mask]
            fresh|=mask
        occupied=fresh
    return png(canvas)


def geometry_signature(objects):
    return digest(json.dumps([dict(name=o.name,vertices=[list(v.co) for v in o.data.vertices],
        faces=[list(p.vertices) for p in o.data.polygons],
        weights=[[(o.vertex_groups[g.group].name,round(g.weight,7)) for g in v.groups] for v in o.data.vertices],
        uv=[[list(v.uv) for v in layer.data] for layer in o.data.uv_layers]) for o in objects],sort_keys=True).encode())


def source_material(objects, output):
    material=bpy.data.materials.new('Crane authored shell');material.use_nodes=True
    shader=material.node_tree.nodes.get('Principled BSDF')
    for name,space in (('CraneBodyAtlas','sRGB'),('CraneMaterialResponse','Non-Color')):
        image=bpy.data.images.load(str(output/(name+'.png')),check_existing=False)
        image.colorspace_settings.name=space;image.pack()
        image.filepath='//../../Assets/Resources/Combat/'+name+'.png'
        texture=material.node_tree.nodes.new('ShaderNodeTexImage');texture.image=image
        if name=='CraneBodyAtlas':material.node_tree.links.new(texture.outputs['Color'],shader.inputs['Base Color'])
        else:
            channels=material.node_tree.nodes.new('ShaderNodeSeparateColor')
            material.node_tree.links.new(texture.outputs['Color'],channels.inputs['Color'])
            material.node_tree.links.new(channels.outputs['Blue'],shader.inputs['Roughness'])
            encoded=material.node_tree.nodes.new('ShaderNodeCombineColor')
            encoded.inputs['Blue'].default_value=1.
            for channel in ('Red','Green'):
                invert=material.node_tree.nodes.new('ShaderNodeMath');invert.operation='SUBTRACT';invert.inputs[0].default_value=1.
                material.node_tree.links.new(channels.outputs[channel],invert.inputs[1])
                material.node_tree.links.new(invert.outputs[0],encoded.inputs[channel])
            normal=material.node_tree.nodes.new('ShaderNodeNormalMap');normal.uv_map='CraneAtlas';normal.inputs['Strength'].default_value=.32
            material.node_tree.links.new(encoded.outputs['Color'],normal.inputs['Color'])
            material.node_tree.links.new(normal.outputs['Normal'],shader.inputs['Normal'])
    for obj in objects:
        obj.data.materials.clear();obj.data.materials.append(material)
    return material


def build(output, source_dir, preview_dir=None, publish=True):
    global BONES
    crane=load_crane();builder=crane.zombie.load_builder(retain_meshes=True);rig=builder.result.rig
    hardware=crane_mutant_geometry.sculpt(builder)
    objects=sorted((o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('GEO_')),key=lambda o:o.name)
    # The runtime collision profile must cover the new shield without retaining
    # the old pendant skull sphere, which touched the upper gantry cassette.
    head_radius=0.
    for kind,duration,loop in crane.CLIPS:
        for step in range(17):
            crane.pose(builder,kind,duration*step/16.)
            bpy.context.view_layer.update();graph=bpy.context.evaluated_depsgraph_get()
            centre=rig.matrix_world@rig.pose.bones['head'].head
            for name in ('GEO_Head','GEO_FaceSurface'):
                evaluated=bpy.data.objects[name].evaluated_get(graph);mesh=evaluated.to_mesh()
                head_radius=max(head_radius,max((evaluated.matrix_world@v.co-centre).length for v in mesh.vertices))
                evaluated.to_mesh_clear()
    if head_radius>.40*crane.BODY_SCALE:raise ValueError('Crane shield escaped its runtime clearance sphere: '+str(head_radius))
    print('CRANE SHIELD CLEARANCE RADIUS '+str(round(head_radius,6)),flush=True)
    crane.pose(builder,'CraneIdle',.5)
    bpy.context.view_layer.update();depsgraph=bpy.context.evaluated_depsgraph_get()
    coordinates={}
    for obj in objects:
        evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
        coordinates[obj.name]=[list(evaluated.matrix_world@v.co) for v in mesh.vertices]
        evaluated.to_mesh_clear()
    builder._reset_pose();bpy.context.view_layer.update()
    BONES={bone.name:(np.array(rig.matrix_world@bone.head_local),np.array(rig.matrix_world@bone.tail_local)) for bone in rig.data.bones}
    unwrap(objects,coordinates,(.012,.16,.976,.828))
    mechanical=crane.passive_assets()
    static=sorted([obj for root in mechanical for obj in root.children_recursive if obj.type=='MESH']+[o for o,bone in hardware],key=lambda o:o.name)
    unwrap(static,{o.name:[list(o.matrix_world@v.co) for v in o.data.vertices] for o in static},(.012,.012,.976,.132))
    print('CRANE REFERENCE POSE UV0 / SOURCE UV1 AUTHORED',flush=True)
    sources=source_pixels()
    all_objects=objects+static
    colour=paint(all_objects,sources,SIZE)
    print('CRANE UNIQUE ALBEDO BAKED',flush=True)
    response=paint(all_objects,sources,RESPONSE_SIZE,True)
    print('CRANE REGISTERED RESPONSE BAKED',flush=True)
    def bounds(obj):
        points=np.array([obj.matrix_world@v.co for v in obj.data.vertices])[:,(0,2,1)]
        return dict(bounds_min=[round(float(v),7) for v in points.min(axis=0)],bounds_max=[round(float(v),7) for v in points.max(axis=0)])
    def role(name):
        if name.startswith(('GEO_Hand','GEO_Foot')):return 'steel'
        return 'flesh' if name.startswith('GEO_Neck') else 'carapace'
    payload=dict(design_id='crane_body_surface_v1',generator=Path(__file__).name,rig='NpcHumanV2',
        geometry_owner='independent_sculpt',bounds_space='unity_metres',
        retained_rest_skeleton=True,uv_channel=0,source_uv_channel=1,
        source_sha256=digest(SOURCE_IMAGE.read_bytes()),
        source_geometry_sha256=digest((ROOT/'ArtSource/VillageLife/StationWorker.blend').read_bytes()),
        signature=geometry_signature(all_objects),
        meshes=[dict(name=o.name,visible=not o.name.startswith(HIDDEN) and not (o.name.startswith('GEO_Hand') and o.name.endswith('.R')),
                     vertices=len(o.data.vertices),triangles=sum(len(p.vertices)-2 for p in o.data.polygons),surfaces=[role(o.name)],**bounds(o)) for o in objects],
        hardware=[dict(name=o.name,bone=bone) for o,bone in hardware],
        passive_meshes=[dict(name=o.name,model=root.name) for root in mechanical for o in root.children_recursive if o.type=='MESH'],
        textures=[dict(name='CraneBodyAtlas',size=SIZE,sRGB=True,sha256=digest(colour)),
                  dict(name='CraneMaterialResponse',size=RESPONSE_SIZE,sRGB=False,sha256=digest(response))],
        estimated_gpu_bytes=sum(sum(max(1,(s>>m)+3>>2)**2*16 for m in range(int(math.log2(s))+1)) for s in (SIZE,RESPONSE_SIZE)))
    if not publish:return payload
    output.mkdir(parents=True,exist_ok=True);source_dir.mkdir(parents=True,exist_ok=True)
    (output/'CraneBodyAtlas.png').write_bytes(colour)
    (output/'CraneMaterialResponse.png').write_bytes(response)
    (output/'CraneBody.json').write_text(json.dumps(payload,indent=2)+'\n',encoding='utf8')
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    for obj in objects:obj.select_set(True)
    for obj,bone in hardware:obj.select_set(True)
    for root in mechanical:
        root.select_set(True)
        for obj in root.children_recursive:obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(output/'CraneBody.fbx'),use_selection=True,object_types={'EMPTY','ARMATURE','MESH'},
        axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,use_custom_props=True,use_mesh_modifiers=False,
        use_armature_deform_only=False,path_mode='STRIP',mesh_smooth_type='FACE')
    # A compact editable source contains just the exported body and kit.
    selected=set(bpy.context.selected_objects)
    for obj in list(bpy.data.objects):
        if obj not in selected and obj.type in ('MESH','CAMERA','LIGHT'):bpy.data.objects.remove(obj,do_unlink=True)
    source_material(all_objects,output)
    visible={m['name'] for m in payload['meshes'] if m['visible']}
    for obj in objects:obj.hide_render=obj.name not in visible
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir/'CraneBody.blend'))
    if preview_dir:preview(objects,static,rig,crane,builder,output,preview_dir,payload)
    return payload


def preview(objects,static,rig,crane,builder,output,directory,payload):
    material=objects[0].data.materials[0]
    visible={m['name'] for m in payload['meshes'] if m['visible']}
    for obj in objects+static:
        obj.data.materials.clear();obj.data.materials.append(material)
        obj.hide_render=obj.name not in visible and not obj.name.startswith('CraneFerrule_')
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16
    scene.render.resolution_x=1100;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.world.color=(.035,.035,.035)
    camera_data=bpy.data.cameras.new('Crane surface review');camera=bpy.data.objects.new('Crane surface review',camera_data)
    scene.collection.objects.link(camera);scene.camera=camera
    for position,power in (((1,-3,4),550),((-2,1,3),330)):
        data=bpy.data.lights.new('Review area','AREA');data.energy=power;data.shape='DISK';data.size=2.5
        lamp=bpy.data.objects.new('Review area',data);scene.collection.objects.link(lamp);lamp.location=position
        lamp.rotation_euler=(Vector((0,-.2,1.3))-lamp.location).to_track_quat('-Z','Y').to_euler()
    directory.mkdir(parents=True,exist_ok=True)
    for name,position,target,scale in (('CraneSurfaceFront',(3,-4,2.5),(0,-.3,1.3),3.4),
        ('CraneSurfaceRear',(-3,4,2.2),(0,-.3,1.3),3.4),('CraneSurfaceHead',(1.4,-2.8,2.0),(.10,-.5,1.4),1.05)):
        crane.pose(builder,'CraneIdle',0.);bpy.context.view_layer.update()
        camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        camera_data.type='ORTHO';camera_data.ortho_scale=scale
        scene.render.filepath=str(directory/(name+'.png'));bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir',type=Path,default=OUT)
    parser.add_argument('--source-dir',type=Path,default=SOURCE)
    parser.add_argument('--preview-dir',type=Path)
    parser.add_argument('--validate-only',action='store_true')
    parser.add_argument('--refresh-source',action='store_true',help='Refresh packed editable materials without republishing runtime assets')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    if args.refresh_source:
        bpy.ops.wm.open_mainfile(filepath=str(args.source_dir/'CraneBody.blend'))
        source_material([o for o in bpy.data.objects if o.type=='MESH'],args.output_dir)
        payload=json.loads((args.output_dir/'CraneBody.json').read_text(encoding='utf8'))
        visible={m['name'] for m in payload['meshes'] if m['visible']}
        for obj in bpy.data.objects:
            if obj.name.startswith('GEO_'):obj.hide_render=obj.name not in visible
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/'CraneBody.blend'))
        print('CRANE EDITABLE SURFACES REFRESHED',flush=True)
        return
    if args.validate_only:
        measured=build(args.output_dir,args.source_dir,publish=False)
        expected=json.loads((args.output_dir/'CraneBody.json').read_text(encoding='utf8'))
        if measured!=expected:raise ValueError('Crane unique painting differs from deterministic source')
        for row in measured['textures']:
            if digest((args.output_dir/(row['name']+'.png')).read_bytes())!=row['sha256']:raise ValueError('Changed Crane map '+row['name'])
    else:
        measured=build(args.output_dir.resolve(),args.source_dir.resolve(),args.preview_dir.resolve() if args.preview_dir else None)
    if measured['estimated_gpu_bytes']>7*1024*1024:raise ValueError('Crane texture bank exceeds 7 MiB')
    print('CRANE UNIQUE SURFACES OK '+measured['signature'],flush=True)


if __name__=='__main__':main()
