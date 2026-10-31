"""Impossible recursive human anatomy, authored onto retained combat skin names.

The loop is a real swept neck and a rolled chest, not a pose of a worker.
The carrier owns a second closed torso/head/arm set inside GEO_Torso, while
the canonical head and hands remain the primary physical strike anatomy.
"""
from __future__ import annotations
import math
import bpy
from mathutils import Vector, Quaternion
import crane_mutant_geometry as kit

FLESH_UV=(.60,.88)


def catmull(points, samples=5):
    points=[Vector(p) for p in points];result=[]
    for i in range(len(points)-1):
        a,b,c,d=points[max(0,i-1)],points[i],points[i+1],points[min(len(points)-1,i+2)]
        for j in range(samples):
            t=j/samples
            result.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
    return result+[points[-1]]


def sweep(points,radius,sides=16,flatten=1.,rib=.0):
    path=catmull(points)
    radii=[]
    for i in range(len(path)):
        t=i/max(1,len(path)-1)
        constriction=1.
        if rib>.06:
            for centre in (.19,.47,.76):constriction*=1.-.28*math.exp(-((t-centre)/.035)**2)
        radii.append(radius*(1+rib*math.sin(i*1.45))*constriction)
    return kit.tube(path,radii,sides,flatten,False)


def flesh_uv(obj):
    uv=obj.data.uv_layers.new(name='UVMap')
    for polygon in obj.data.polygons:
        for index in polygon.loop_indices:
            point=obj.matrix_world@obj.data.vertices[obj.data.loops[index].vertex_index].co
            # Dedicated nonrepeating flesh sheet, independent from face UVs.
            u=max(0.,min(1.,(point.x+.6)/1.2));v=max(0.,min(1.,(point.z+.1)/2.8))
            uv.data[index].uv=(.51+.22*u,.77+.20*v)
    source=obj.data.uv_layers.new(name='SourceUV')
    for a,b in zip(source.data,uv.data):a.uv=b.uv
    obj.data.uv_layers.active_index=0


def replace(builder,name,geometry,weights):
    obj=kit.replace(builder,name,geometry,weights)
    flesh_uv(obj)
    return obj


def merged_geometry(parts):
    vertices=[];faces=[];weights=[]
    for geo,assignment in parts:
        kit.append_mesh(None,vertices,faces,weights,geo,assignment)
    return vertices,faces,weights


def head_objects():
    return [o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith(('GEO_Head','GEO_FaceSurface','GEO_Ear'))]


def relocate_head(centre,scale,tilt,yaw=0.):
    origin=Vector((0.,-.008,1.61))
    rotation=Quaternion(Vector((0,0,1)),math.radians(yaw))@Quaternion(Vector((1,0,0)),math.radians(tilt))
    for obj in head_objects():
        inverse=obj.matrix_world.inverted()
        sets=[key.data for key in obj.data.shape_keys.key_blocks] if obj.data.shape_keys else [obj.data.vertices]
        for points in sets:
            for vertex in points:
                point=obj.matrix_world@vertex.co
                vertex.co=inverse@(Vector(centre)+rotation@((point-origin)*scale))


def bottle_surface(obj,parts,part_index,rows,sides=28,cavity=False):
    """An opaque thick-glass surface, with its own authored cylindrical UVs."""
    obj.data.materials.clear()
    for role in ('skin','glass'):obj.data.materials.append(bpy.data.materials.new('Decay '+role))
    vertex_start=sum(len(g[0]) for g,w in parts[:part_index])
    face_start=sum(len(g[1]) for g,w in parts[:part_index])
    face_end=face_start+len(parts[part_index][0][1])
    for polygon in obj.data.polygons[face_start:face_end]:
        polygon.material_index=1
        inner=cavity and min((i-vertex_start)//sides for i in polygon.vertices)>=rows-3
        if inner:
            for index in polygon.loop_indices:obj.data.uv_layers[0].data[index].uv=(.48,.52)
            continue
        if len(polygon.vertices)>4:
            for index in polygon.loop_indices:obj.data.uv_layers[0].data[index].uv=(.20,.70)
            continue
        around=[(i-vertex_start)%sides/sides for i in polygon.vertices]
        seam=max(around)-min(around)>.5
        for index,u in zip(polygon.loop_indices,around):
            vertex=obj.data.loops[index].vertex_index-vertex_start
            if seam and u<.5:u+=1.
            obj.data.uv_layers[0].data[index].uv=(.02+.44*u,.53+.43*(vertex//sides)/(rows-1))
    for source,target in zip(obj.data.uv_layers[1].data,obj.data.uv_layers[0].data):source.uv=target.uv


def copy_lower_face(parts,uv_parts):
    """The same actual face/ears/skull, scaled into the carrier's lower human."""
    centre=Vector((.05,-.40,1.30));origin=Vector((0,-.008,1.61))
    for obj in head_objects():
        vertices=[centre+(obj.matrix_world@v.co-origin)*.61 for v in obj.data.vertices]
        faces=[tuple(p.vertices) for p in obj.data.polygons]
        parts.append(((vertices,faces),{'spine':.8,'pelvis':.2}))
        uv_parts.append([[tuple(obj.data.uv_layers.active.data[i].uv) for i in p.loop_indices] for p in obj.data.polygons])


def loop(builder,variant):
    side=-1. if variant else 1.
    lower=kit.shell([(.89,0,.025,.10,.09),(.96,0,.015,.18,.13),(1.05,0,.015,.19,.14)],24)
    profile=[(.925,0,-.015,.08,.065),(.905,0,-.015,.20,.165),(.90,0,-.015,.245,.205),(.92,0,-.015,.29,.24),(.955,0,-.015,.305,.25),
        (1.34,0,-.015,.305,.25),(1.40,0,-.015,.292,.24),(1.47,0,-.015,.24,.197),
        (1.53,0,-.015,.105,.086),(1.56,0,-.015,.086,.074),(1.73,0,-.015,.086,.074),
        (1.74,0,-.015,.103,.088),(1.77,0,-.015,.103,.088),(1.785,0,-.015,.09,.077),
        (1.785,0,-.015,.06,.051),(1.745,0,-.015,.058,.049),(1.68,0,-.015,.052,.044)]
    glass=kit.shell(profile,28)
    parts=[(lower,lambda p:kit.trunk_weight(p[2])),(glass,{'chest':1.})]
    vertices,faces,weights=merged_geometry(parts)
    torso=replace(builder,'GEO_Torso',(vertices,faces),{'chest':1.});kit.skin(torso,weights)
    bottle_surface(torso,parts,1,len(profile),cavity=True)
    path=[(0,.16,1.40),(side*.24,.23,1.65),(side*.35,.08,1.83),
        (side*.35,-.18,1.93),(side*.15,-.35,1.98),(-side*.09,-.30,1.90),(-side*.13,-.23,1.885)]
    neck=sweep(path,.062,18,.88,.10)
    replace(builder,'GEO_Neck',neck,{'chest':1.})
    relocate_head((-side*.06,-.163,1.885),1.05,15,side*135)
    return dict(kind='closed_loop',dominant_bottle=True,bottle_orientation='upright',real_neck_tube=True,
        face_at_bottle_lip=True,second_torso=False,neck_path_length_m=sum((b-a).length for a,b in zip(catmull(path),catmull(path)[1:])))


def carrier(builder):
    parts=[];uv_parts=[]
    # The lower human's closed torso and doubled forearms are real geometry.
    lower=kit.shell([(.89,0,.02,.11,.09),(.95,0,-.015,.17,.12),(1.06,0,-.03,.18,.11),
                     (1.17,0,-.01,.13,.085),(1.20,0,-.01,.07,.06)],20)
    profile=[(1.04,0,-.19,.14,.12),(1.075,0,-.19,.14,.12),(1.10,0,-.19,.112,.095),
        (1.34,0,-.13,.112,.095),(1.43,0,-.03,.19,.16),(1.55,0,.10,.34,.28),
        (1.65,0,.14,.42,.34),(2.20,0,.14,.42,.34),(2.25,0,.14,.45,.36),
        (2.29,0,.14,.45,.36),(2.32,0,.14,.38,.30),(2.29,0,.14,.30,.24),(2.27,0,.14,.11,.09)]
    upper=kit.shell(profile,28)
    parts.extend([(lower,{'pelvis':.5,'spine':.5}),(upper,{'chest':1.})]);uv_parts.extend([None,None])
    lower_neck=sweep([(0,-.03,1.17),(.06,-.25,1.23),(.05,-.40,1.30)],.043,12,.9,.08)
    parts.append((lower_neck,{'spine':1.}));uv_parts.append(None)
    for sign in (-1,1):
        small_arm=sweep([(sign*.16,-.02,1.15),(sign*.22,-.18,1.04),(sign*.11,-.30,1.02)],.038,12,.85)
        parts.append((small_arm,{'spine':1.}));uv_parts.append(None)
        # Four narrow fingers visibly clutch the burden above the small body.
        for i in range(4):
            finger=sweep([(sign*(.095+i*.013),-.30,1.02),(sign*(.09+i*.013),-.33,1.07),
                          (sign*(.088+i*.013),-.31,1.10)],.008,8,.85)
            parts.append((finger,{'spine':1.}));uv_parts.append(None)
    copy_lower_face(parts,uv_parts)
    vertices,faces,weights=merged_geometry(parts)
    torso=replace(builder,'GEO_Torso',(vertices,faces),{'chest':1.});kit.skin(torso,weights)
    bottle_surface(torso,parts,1,len(profile))
    # Restore the second face's exact original atlas patch after generic flesh UVs.
    polygon_offset=0
    for (geometry,_),patch in zip(parts,uv_parts):
        if patch:
            for polygon,coords in zip(torso.data.polygons[polygon_offset:polygon_offset+len(patch)],patch):
                for index,value in zip(polygon.loop_indices,coords):torso.data.uv_layers[0].data[index].uv=value
        polygon_offset+=len(geometry[1])
    for source,target in zip(torso.data.uv_layers[1].data,torso.data.uv_layers[0].data):source.uv=target.uv
    neck=sweep([(0,-.19,1.06),(0,-.34,1.04),(0,-.50,1.01),(0,-.61,.94)],.104,20,.9,.11)
    replace(builder,'GEO_Neck',neck,lambda p: {'chest':max(0.,min(1.,(p[1]+.61)/.42)),
                                            'neck':1.-max(0.,min(1.,(p[1]+.61)/.42))})
    relocate_head((0,-.64,.89),2.05,13)
    return dict(kind='self_carrier',dominant_bottle=True,bottle_orientation='inverted',second_torso=False,second_head=True,second_small_arms=True,
        primary_head_scale=2.05,lower_head_scale=.61)


def sculpt(builder,variant):
    canonical_groups={o.name:{g.name for g in o.vertex_groups} for o in bpy.data.objects if o.type=='MESH'}
    # No original outerwear/hair is allowed to disguise the new anatomy.
    for obj in list(bpy.data.objects):
        if obj.type=='MESH' and not obj.name.startswith('GEO_'):bpy.data.objects.remove(obj,do_unlink=True)
    result=carrier(builder) if variant==2 else loop(builder,variant)
    # Original leg topology keeps damage/feet; its colour is torn dark fabric.
    # The exposed upper arms have coarse longitudinal ridges, not shirt sleeves.
    rig=builder.result.rig
    for side in ('L','R'):
        upper,fore,hand=(rig.data.bones[n+'.'+side] for n in ('upper_arm','forearm','hand'))
        a,b,c=upper.head_local,fore.head_local,hand.head_local
        samples=(0.,.12,.33,.58,.82,1.)
        upper_length=2.4 if variant==2 else 1.2
        fore_length=2.6 if variant==2 else 1.6
        # Each rigid section is authored at its actual elongated length in
        # that bone's retained bind frame. Their posed ends meet exactly;
        # no new hand skin bone and no interpolated stretch ribbon are needed.
        points=[a+(b-a)*(t*upper_length) for t in samples]+[b+(c-b)*(t*fore_length) for t in samples]
        radii=[.059,.072,.069,.059,.050,.046,.046,.049,.046,.042,.035,.030]
        if variant==2:radii=[r*1.25 for r in radii]
        upper_geometry=kit.tube(points[:6],radii[:6],14,.85,False)
        lower_geometry=kit.tube(points[6:],radii[6:],14,.85,False)
        vertices,faces,weights=merged_geometry([(upper_geometry,{'upper_arm.'+side:1.}),
                                                (lower_geometry,{'forearm.'+side:1.})])
        arm=replace(builder,'GEO_Arm.'+side,(vertices,faces),{'upper_arm.'+side:1.})
        kit.skin(arm,weights)
    for obj in bpy.data.objects:
        if obj.type=='MESH' and not {g.name for g in obj.vertex_groups}.issubset(canonical_groups[obj.name]):
            raise ValueError('New anatomy introduced a noncanonical skin bone: '+obj.name)
    return result
