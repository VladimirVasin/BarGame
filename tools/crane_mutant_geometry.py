"""Independent industrial-mutant sculpt on the shared anatomical bind rig.

All visible body vertices are authored here in metres. Semantic GEO names and
bone identities connect the sculpt to the shared wounds/dismemberment system.
"""
import math

import bpy
from mathutils import Vector


def skin(obj,assignments):
    for group in list(obj.vertex_groups):obj.vertex_groups.remove(group)
    names=sorted({name for row in assignments for name,w in row.items() if w>0})
    groups={name:obj.vertex_groups.new(name=name) for name in names}
    for i,row in enumerate(assignments):
        total=sum(row.values())
        if total<=0:raise ValueError('Empty Crane skin weight: '+obj.name)
        for name,w in row.items():
            if w>0:groups[name].add([i],w/total,'REPLACE')


def trunk_weight(z):
    if z<1.10:
        t=max(0.,min(1.,(z-.94)/.16));return {'pelvis':1-t,'spine':t}
    t=max(0.,min(1.,(z-1.10)/.22));return {'spine':1-t,'chest':t}


def append_mesh(target, vertices, faces, weights, addition, assignment):
    offset=len(vertices)
    points,polygons=addition
    vertices.extend(points)
    faces.extend(tuple(i+offset for i in face) for face in polygons)
    weights.extend(assignment(point) if callable(assignment) else assignment for point in points)


def shell(rows, sides=20, relief=None):
    """Closed, bevel-ringed axial hull; rows are z,cx,cy,rx,ry."""
    vertices=[]
    for row,(z,cx,cy,rx,ry) in enumerate(rows):
        for i in range(sides):
            angle=i*math.tau/sides
            scale=1. if relief is None else relief(row,angle)
            vertices.append((cx+math.cos(angle)*rx*scale,cy+math.sin(angle)*ry*scale,z))
    faces=[]
    for row in range(len(rows)-1):
        for i in range(sides):
            a=row*sides+i;b=row*sides+(i+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.extend((tuple(reversed(range(sides))),tuple((len(rows)-1)*sides+i for i in range(sides))))
    return vertices,faces


def tube(points, radii, sides=12, flatten=.80, plated=True):
    points=[Vector(p) for p in points]
    vertices=[]
    for row,(point,radius) in enumerate(zip(points,radii)):
        axis=(points[min(row+1,len(points)-1)]-points[max(0,row-1)]).normalized()
        across=axis.cross(Vector((0.,-1.,0.))).normalized()
        if across.length<.1:across=axis.cross(Vector((1.,0.,0.))).normalized()
        front=across.cross(axis).normalized()
        for i in range(sides):
            angle=i*math.tau/sides
            # A dorsal ridge and broad sloping facets give plates real volume.
            ridge=1.+(.17*max(0.,math.sin(angle))**6 if plated and row%3!=0 else 0.)
            vertices.append(tuple(point+across*(math.cos(angle)*radius)+front*(math.sin(angle)*radius*flatten*ridge)))
    faces=[]
    for row in range(len(points)-1):
        for i in range(sides):
            a=row*sides+i;b=row*sides+(i+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.extend((tuple(reversed(range(sides))),tuple((len(points)-1)*sides+i for i in range(sides))))
    # Closed solids own outward winding, independent of sweep-frame handedness.
    volume=sum(Vector(vertices[f[0]]).dot(Vector(vertices[f[i]]).cross(Vector(vertices[f[i+1]])))/6
        for f in faces for i in range(1,len(f)-1))
    if volume<0:faces=[tuple(reversed(face)) for face in faces]
    return vertices,faces


def crane_hook(start,end,size):
    """Forged J silhouette: thick shank, round return and blunt hooked nose."""
    start,end=Vector(start),Vector(end)
    axis=(end-start).normalized()
    across=axis.cross(Vector((0.,-1.,0.))).normalized()
    if across.length<.1:across=Vector((1,0,0))
    path=((0.,0.,.16),(0.,.27,.16),(-.10,.49,.17),(-.20,.70,.18),
        (-.16,.89,.18),(.04,1.,.16),(.27,.93,.14),(.39,.73,.11),(.34,.54,.065))
    points=[start+(across*x+axis*y)*size for x,y,r in path]
    return tube(points,[r*size for x,y,r in path],12,.85,False)


def replace(builder,name,geometry,assignments,grip=False):
    obj=bpy.data.objects[name]
    vertices,faces=geometry
    matrix=obj.matrix_world.inverted()
    mesh=bpy.data.meshes.new(name+'MutantSculpt')
    mesh.from_pydata([matrix@Vector(p) for p in vertices],[],faces);mesh.update()
    old=obj.data;obj.data=mesh
    if old.users==0:bpy.data.meshes.remove(old)
    for poly in mesh.polygons:poly.use_smooth=True
    skin(obj,[assignments(p) if callable(assignments) else assignments for p in vertices])
    if grip:
        obj.shape_key_add(name='Basis')
        key=obj.shape_key_add(name='CylindricalGrip')
        # Shared hand owner retains its shape index. Steel is rigid; the tiny
        # translation keeps an explicit exported key without bending the hook.
        for target,point in zip(key.data,vertices):
            p=Vector(point)
            p.y+=.00001
            target.co=matrix@p
    return obj


def sculpt(builder):
    rig=builder.result.rig
    bones={b.name:(rig.matrix_world@b.head_local,rig.matrix_world@b.tail_local) for b in rig.data.bones}
    # Original garments/hair never enter the editable source or export.
    for obj in list(bpy.data.objects):
        if obj.type=='MESH' and not obj.name.startswith('GEO_'):
            bpy.data.objects.remove(obj,do_unlink=True)

    torso_rows=[(.91,0,.025,.072,.065),(.94,0,.025,.108,.083),
        (1.005,0,.020,.100,.091),(1.055,0,.012,.082,.075),
        (1.095,0,.008,.093,.086),(1.13,0,0,.119,.102),
        (1.16,0,0,.148,.117),(1.175,0,0,.150,.127),
        (1.192,0,0,.143,.108),(1.207,0,0,.169,.127),
        (1.25,0,0,.182,.137),(1.275,0,0,.183,.142),
        (1.292,0,0,.176,.120),(1.306,0,0,.193,.135),
        (1.355,0,.004,.200,.139),(1.38,0,.008,.175,.127),
        (1.408,0,.010,.095,.075),(1.419,0,.010,.060,.048)]
    def thorax_relief(row,angle):
        front=max(0.,-math.sin(angle))
        keel=.15*front**9
        seam=.10*(math.cos(angle*3)**8)*front
        return 1.+keel+seam
    replace(builder,'GEO_Torso',shell(torso_rows,24,thorax_relief),lambda p:trunk_weight(p[2]))
    replace(builder,'GEO_Neck',shell([(1.36,0,.012,.055,.050),(1.39,0,0,.047,.042),
        (1.42,0,-.005,.040,.035),(1.445,0,-.008,.037,.034),
        (1.47,0,-.010,.041,.037),(1.49,0,-.012,.047,.043)],16),
        lambda p:{'neck':max(0.,min(1.,(p[2]-1.36)/.08)),'chest':max(0.,1.-(p[2]-1.36)/.08)})
    # A single closed shield volume: broad brow, narrow mandibular point,
    # shallow depth. Every fracture sector has real head tissue behind it.
    replace(builder,'GEO_Head',shell([(1.473,0,-.006,.032,.035),
        (1.495,0,-.018,.055,.052),(1.535,0,-.016,.084,.064),
        (1.575,0,-.008,.127,.073),(1.63,0,.002,.196,.077),
        (1.658,0,.006,.216,.080),(1.683,0,.008,.204,.074),
        (1.710,0,.010,.153,.065),(1.735,0,.010,.040,.030)],24,
        lambda row,a:1.+(.055*math.cos(a*4) if row in (4,5,6) else 0.)),{'head':1.})
    replace(builder,'GEO_FaceSurface',shell([(1.493,0,-.067,.014,.013),
        (1.515,0,-.074,.036,.023),(1.55,0,-.076,.042,.022),
        (1.572,0,-.075,.030,.012)],12),{'head':1.})

    for side,sign in (('L',1),('R',-1)):
        upper='upper_arm.'+side;lower='forearm.'+side
        a,b=bones[upper][0],bones[lower][0];c=bones[lower][1]
        factors=(0.,.10,.30,.57,.77,.94,1.)
        points=[a.lerp(b,t) for t in factors]+[b.lerp(c,t) for t in (.10,.26,.48,.69,.88,1.)]
        radii=(.052,.075,.079,.071,.059,.043,.047,.050,.058,.053,.042,.031,.026)
        assignments=[]
        for row in range(len(points)):
            t=max(0.,min(1.,(row-5)/2))
            shoulder=.20 if row==0 else .08 if row==1 else 0.
            assignments.extend([{upper:(1-t)*(1-shoulder),lower:t*(1-shoulder),'chest':shoulder}]*12)
        geo=tube(points,radii)
        obj=replace(builder,'GEO_Arm.'+side,geo,{upper:1.})
        skin(obj,assignments)

        # Hip plates and leg tendons remain separate at the anatomical knee.
        thigh='thigh.'+side;shin='shin.'+side;foot='foot.'+side
        a,b=bones[thigh][0],bones[shin][0];c=bones[shin][1]
        leg_points=[a.lerp(b,t) for t in (0.,.10,.28,.49,.72,.90,1.)]
        leg=tube(leg_points,(.070,.085,.080,.064,.057,.048,.045),14,.88)
        # Both upper legs share the original semantic renderer.
        if side=='L':upper_vertices=[];upper_faces=[];upper_weights=[]
        append_mesh(None,upper_vertices,upper_faces,upper_weights,leg,{thigh:1.})
        calf=tube([b.lerp(c,t) for t in (0.,.08,.22,.45,.68,.86,1.)],
            (.048,.061,.064,.053,.039,.026,.023),12,.73)
        replace(builder,'GEO_LegsLower.'+side,calf,{shin:1.})
        ankle,toe=bones[foot]
        replace(builder,'GEO_FootShoe.'+side,crane_hook(ankle,toe,.23),{foot:1.})

        # Crane hooks replace hands. Hidden named stubs keep the shared
        # anatomical/grip bindings; no fingers or animal claws remain visible.
        hand='hand.'+side;a,b=bones[hand]
        direction=(b-a).normalized();across=direction.cross(Vector((0,-1,0))).normalized()
        palm=crane_hook(a,b,.245)
        replace(builder,'GEO_HandPalm.'+side,palm,{hand:1.},True)
        for i in range(4):
            start=a+across*((i-1.5)*.001)
            tip=start+direction*.008
            replace(builder,'GEO_HandFinger'+str(i)+'.'+side,
                tube([start,tip],(.003,.002),8,.78,False),{hand:1.},True)
        start=a+across*.004;tip=start+direction*.008
        replace(builder,'GEO_HandThumb.'+side,tube([start,tip],(.003,.002),8,.8,False),{hand:1.},True)
    obj=replace(builder,'GEO_LegsUpper',(upper_vertices,upper_faces),{'pelvis':1.})
    skin(obj,upper_weights)
    # Ears remain named hidden contract stubs, with no human ear surface.
    for name in ('GEO_Ear.L','GEO_Ear.R','GEO_EarFold.L','GEO_EarFold.R'):
        replace(builder,name,shell([(1.60,0,.025,.004,.004),(1.608,0,.025,.005,.005)],8),{'head':1.})
    return fittings(rig)


def fittings(rig):
    """Six embedded cable ferrules, authored directly in each bone frame."""
    result=[]
    for bone_name,offset,radius in (('head',.25,.025),('hand.L',0.,.023),
        ('hand.R',0.,.025),('pelvis',0.,.036),('foot.L',0.,.023),('foot.R',0.,.023)):
        vertices=[];faces=[]
        rows=[(-.018,radius*1.5),(-.010,radius*1.6),(.002,radius*1.12),(.024,radius),(.032,radius*.86)]
        # Bone localY follows the limb; the fitting's bore is visible as a
        # recessed annulus, with a fleshy flange seated inside the source mesh.
        n=12
        for y,r in rows:
            for i in range(n):
                a=i*math.tau/n;vertices.append((math.cos(a)*r,offset+y,math.sin(a)*r))
        for row in range(len(rows)-1):
            for i in range(n):
                a=row*n+i;b=row*n+(i+1)%n;faces.append((a,a+n,b+n,b))
        for y in (.032,.008):
            for i in range(n):
                a=i*math.tau/n;vertices.append((math.cos(a)*radius*.43,offset+y,math.sin(a)*radius*.43))
        for i in range(n):
            j=(i+1)%n;faces.extend(((4*n+i,5*n+i,5*n+j,4*n+j),(5*n+i,6*n+i,6*n+j,5*n+j)))
        faces.extend((tuple(reversed(range(n))),tuple(6*n+i for i in range(n))))
        mesh=bpy.data.meshes.new('CraneFerruleMesh');mesh.from_pydata(vertices,[],faces);mesh.update()
        obj=bpy.data.objects.new('CraneFerrule_'+bone_name,mesh);bpy.context.collection.objects.link(obj)
        obj.parent=rig;obj.parent_type='BONE';obj.parent_bone=bone_name
        # Blender's bone parenting origin is the tail, compensate to bonehead.
        obj.location=(0,-rig.data.bones[bone_name].length,0)
        for p in mesh.polygons:p.use_smooth=True
        result.append((obj,bone_name))
    bpy.context.view_layer.update()
    return result
