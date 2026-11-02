"""A neck-bottle grip on the unchanged production right-hand geometry.

Exports only the original six hand surfaces with an additional authored morph.
Basis, topology, skin weights and previous shapes are retained byte-for-byte.
"""
from __future__ import annotations
import hashlib, importlib.util, json, math, sys
from pathlib import Path
sys.dont_write_bytecode=True
import bpy
from mathutils import Matrix, Vector
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
spec=importlib.util.spec_from_file_location('vodka_grip_hero',ROOT/'tools/build-player-3d-model-v2.py')
hero=importlib.util.module_from_spec(spec);sys.modules[spec.name]=hero;spec.loader.exec_module(hero)
common=hero.common;grip=hero.player_hand_grip
OUT=ROOT/'Assets/Resources/Player/PlayerVodkaGrip.fbx'; MANIFEST=OUT.with_suffix('.json')
BLEND=ROOT/'ArtSource/Player/Blender/PlayerVodkaGrip.blend'
NAMES=grip._names('R');RADIUS=.0147796875;SHAPE='BottleGrip'


def build():
    bpy.ops.wm.open_mainfile(filepath=str(hero.DEFAULT_OUTPUT))
    rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');rig.animation_data.action=None
    rows=json.loads(hero.DEFAULT_MANIFEST.read_text())['parts']
    result=common.BuildResult(rig.parent,rig,{}, {},parts=[common.PartRecord(bpy.data.objects[r['name']],r['role'],r['bone'],r['sprite_part'],r['side']) for r in rows if r['name'] in NAMES],actions={})
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    wrist,direction,across,normal=grip._frame(result,'R');axis=-across
    centre=wrist+direction*.035+normal*(RADIUS+.015)
    original={p.obj.name:{k.name:[tuple(v.co) for v in k.data] for k in p.obj.data.shape_keys.key_blocks} for p in result.parts}
    link_error=0.;thumb_alignment=0.
    for row in result.parts:
        obj=row.obj;points,matrix=grip._source_points(result,obj)
        if obj.name=='GEO_Hand.R':
            # The whole front of the actual palm lies behind a cylinder
            # tangent plane. Restricting this to the distal palm would leave
            # proximal flesh inside a cylinder centred under the knuckles.
            tangent=(centre-wrist).dot(normal)-RADIUS-.002
            target=[p-normal*max(0.,(p-wrist).dot(normal)-tangent) for p in points]
        elif 'Finger' in obj.name:
            i=int(obj.name.split('Finger')[1][0]);offset,length=((- .023,.033),(-.007,.042),(.009,.039),(.024,.030))[i]
            start=wrist+(direction*.052+across*offset)*1.20
            end=wrist+(direction*(.052+length)+across*offset+normal*.004)*1.20
            old=[start+(end-start)*t for t in (0.,.42,.80,1.)]
            radius=max((p-start-(end-start).normalized()*(p-start).dot((end-start).normalized())).length for p in points)
            centres=grip.hand_bowl_grip.finger_centres(old,centre,direction,normal,RADIUS,radius)
            target=grip._bend(points,start,end,centres)
            link_error=max(link_error,max(abs((old[j+1]-old[j]).length-(centres[j+1]-centres[j]).length) for j in range(3)))
        else:
            def local(along,width):return wrist+(direction*along+across*width)*(1+.20*min(1,along/.030))
            start=local(.018,-.022);end=local(.055,-.041)+normal*(.002*1.20)
            length=(end-start).length
            radial=centre-start;radial-=axis*radial.dot(axis);radial.normalize()
            tip=start+axis*(length*.70)+radial*(length*.15)
            delta=tip-start;distance=delta.length;forward=delta.normalized()
            a,b=length*.45,length*.55;along=(a*a-b*b+distance*distance)/(2*distance)
            bend=-normal+forward*normal.dot(forward);bend.normalize()
            centres=[start,start+forward*along+bend*math.sqrt(max(0.,a*a-along*along)),tip]
            old=[start,start+(end-start)*.45,end]
            link_error=max(link_error,max(abs((old[j+1]-old[j]).length-(centres[j+1]-centres[j]).length) for j in range(2)))
            target=grip._bend(points,start,end,centres)
            source_axis=(end-start).normalized()
            stations=[(p-start).dot(source_axis) for p in points]
            first=[target[i] for i,v in enumerate(stations) if v-min(stations)<1e-6]
            last=[target[i] for i,v in enumerate(stations) if max(stations)-v<1e-6]
            thumb_alignment=((sum(last,Vector())/len(last))-(sum(first,Vector())/len(first))).normalized().dot(axis)
        grip._shape(obj,matrix,target,SHAPE)
    result.authored_anchors=[]
    for label,point in (('Centre',centre),('Axis',centre+axis*.05),('Palm',centre+normal*.05)):
        anchor=bpy.data.objects.new('ANCHOR_BottleGrip'+label+'.R',None);result.root.users_collection[0].objects.link(anchor)
        anchor.parent=rig;anchor.parent_type='BONE';anchor.parent_bone='hand.R';bpy.context.view_layer.update()
        anchor.matrix_world=rig.matrix_world@Matrix.Translation(point);result.authored_anchors.append(anchor)
    for row in result.parts:
        for name,coords in original[row.obj.name].items():
            if any((v.co-Vector(p)).length>1e-8 for v,p in zip(row.obj.data.shape_keys.key_blocks[name].data,coords)):raise ValueError('Existing hand changed')
    clearance={}
    for row in result.parts:
        obj=row.obj;_,matrix=grip._source_points(result,obj)
        authored=[matrix@v.co for v in obj.data.shape_keys.key_blocks[SHAPE].data]
        radial=[Vector(((p-centre).dot(direction),(p-centre).dot(normal))) for p in authored]
        gap=min(grip._triangle_axis_distance(tuple(radial[face.vertices[j]] for j in (0,i,i+1)))
            for face in obj.data.polygons for i in range(1,len(face.vertices)-1))-RADIUS
        clearance[obj.name]=gap
    if clearance['GEO_Hand.R']<.0019:raise ValueError('Bottle passes through actual palm triangles: '+repr(clearance))
    if min(clearance.values())<-.0001:raise ValueError('Bottle passes through an authored finger/thumb surface: '+repr(clearance))
    if link_error>1e-6 or thumb_alignment<.95:raise ValueError('Bottle grip stretched fingers or lost actual thumb-up')
    keep={result.root,rig,*(p.obj for p in result.parts),*result.authored_anchors}
    for o in list(bpy.data.objects):
        if o not in keep:bpy.data.objects.remove(o,do_unlink=True)
    axial_bounds={}
    for row in result.parts:
        _,matrix=grip._source_points(result,row.obj)
        values=[((matrix@v.co)-centre).dot(axis) for v in row.obj.data.shape_keys.key_blocks[SHAPE].data]
        axial_bounds[row.obj.name]=[min(values),max(values)]
    # Empty transforms undergo different FBX bone-parent axis conversion.
    # Skinned marker triangles share the exact bind space of these surfaces.
    marker_points={'Centre':centre,'Axis':centre+axis*.05,'Palm':centre+normal*.05,
        'ThumbBase':local(.018,-.022),'ThumbTip':local(.055,-.041)+normal*(.002*1.20)}
    for label,point in marker_points.items():
        mesh=bpy.data.meshes.new('BottleGripMarker'+label)
        mesh.from_pydata([point+direction*.0001,point+axis*.0001,point-direction*.0001-axis*.0001],[],[(0,1,2)])
        obj=bpy.data.objects.new('MARKER_BottleGrip'+label,mesh);result.root.users_collection[0].objects.link(obj)
        obj.parent=result.root;obj.matrix_world=rig.matrix_world
        group=obj.vertex_groups.new(name='hand.R');group.add([0,1,2],1.,'REPLACE')
        modifier=obj.modifiers.new('Original hand bind','ARMATURE');modifier.object=rig
        result.parts.append(common.PartRecord(obj,'marker','hand.R','hand','R'))
    morphs={p.obj.name:[[round(v,8) for v in point.co] for point in p.obj.data.shape_keys.key_blocks[SHAPE].data] for p in result.parts if p.obj.name in NAMES}
    payload=dict(generator='player_vodka_grip_v1',rig='HeroV2',shape=SHAPE,radius_m=RADIUS,
        surfaces=NAMES,maximum_link_length_error_m=link_error,thumb_axis_alignment=thumb_alignment,
        cylinder_surface_clearances_m=clearance,palm_tangent_clearance_m=.002,axial_surface_bounds_m=axial_bounds,
        source_sha256=hashlib.sha256(hero.DEFAULT_OUTPUT.read_bytes()).hexdigest(),
        signature=hashlib.sha256(json.dumps(morphs,sort_keys=True).encode()).hexdigest())
    return result,payload


def main():
    result,payload=build()
    if '--validate-only' in sys.argv:
        if json.loads(MANIFEST.read_text())!=payload:raise ValueError('Grip differs from manifest')
    else:
        hero.export_model_fbx(OUT,result);common.save_blend(BLEND);MANIFEST.write_text(json.dumps(payload,indent=2)+'\n')
        for p in (OUT,MANIFEST):
            meta=p.with_name(p.name+'.meta')
            if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+hashlib.sha256(p.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]+'\n')
    _,again=build()
    if again!=payload:raise ValueError('Grip nondeterministic')
    print('PLAYER VODKA GRIP CONTRACT OK '+json.dumps(payload),flush=True)
if __name__=='__main__':main()
