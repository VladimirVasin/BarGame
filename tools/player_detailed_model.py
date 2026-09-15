"""Measured Hero V2 body, independent workwear and collar-length hair.

Only geometry/weights/metadata live here. The production generator continues to
own the original skeleton, action bank, export and expression atlases.
"""
from __future__ import annotations

import math

import bpy
from mathutils import Vector
import player_hand_frames


HAND_SIZE_SCALE = 1.20
HAND_GIRTH_SCALE = .85


HAIR_PATHS = {
    "HairBack": ((0, .071, 1.650), (0, .074, 1.609), (.002, .073, 1.565), (.004, .069, 1.522)),
    "HairLeft": ((.088, -.008, 1.668), (.095, -.006, 1.618), (.099, .004, 1.566), (.096, .014, 1.520)),
    "HairRight": ((-.088, -.005, 1.666), (-.095, -.003, 1.618), (-.099, .007, 1.568), (-.096, .017, 1.522)),
}
HAIR_BONES = tuple(prefix + suffix for prefix in HAIR_PATHS for suffix in (".00", ".01", ".02", ".Tip"))
BODY_COVERAGE = {
    "shirt": ("GEO_Torso",),
    "jacket": ("GEO_UpperArm.L", "GEO_UpperArm.R", "GEO_Forearm.L", "GEO_Forearm.R",
               "GEO_Shoulder.L", "GEO_Shoulder.R"),
    "trousers": ("GEO_Pelvis", "GEO_Thigh.L", "GEO_Thigh.R", "GEO_Shin.L", "GEO_Shin.R"),
    "boots": ("GEO_Foot.L", "GEO_Foot.R"),
}
CLOTHES_FROM_BODY = {
    "GEO_Torso": "CLO_ShirtBody", "GEO_Pelvis": "CLO_TrousersPelvis",
    **{f"GEO_{part}.{side}": f"CLO_Trousers{part}.{side}" for part in ("Thigh", "Shin") for side in ("L", "R")},
    "GEO_Foot.L": "CLO_Boot.L", "GEO_Foot.R": "CLO_Boot.R",
}
JACKET_PROFILES = ((.805,.168,.104,.017),(.895,.169,.110,.014),
    (.970,.170,.114,.010),(1.115,.176,.119,.002),(1.285,.185,.122,-.006),
    (1.360,.181,.108,-.010),(1.427,.188,.100,-.012),(1.477,.088,.071,-.014))


def hair_specs(builder, common):
    result = []
    for prefix, path in HAIR_PATHS.items():
        for i in range(3):
            result.append(common.BoneSpec(prefix + f".{i:02}", builder.v(*path[i]), builder.v(*path[i + 1]),
                                          "head" if i == 0 else prefix + f".{i - 1:02}"))
        tip = Vector(path[-1]); end = tip + (tip - Vector(path[-2])).normalized() * .025
        result.append(common.BoneSpec(prefix + ".Tip", builder.v(*tip), builder.v(*end), prefix + ".02", deform=False))
    return result


def _replace_mesh(obj, geometry):
    vertices, faces = geometry
    old = obj.data
    materials = list(old.materials)
    mesh = bpy.data.meshes.new(obj.name + "_DetailedMesh")
    mesh.from_pydata([tuple(v - obj.location) for v in vertices], [], faces)
    mesh.update(calc_edges=True)
    for material in materials: mesh.materials.append(material)
    obj.data = mesh
    bpy.data.meshes.remove(old)
    # Vertices in a replaced mesh must acquire the object's preserved bone.
    for group in list(obj.vertex_groups): obj.vertex_groups.remove(group)
    group = obj.vertex_groups.new(name=obj["bp_bone"])
    group.add(range(len(mesh.vertices)), 1., "REPLACE")
    return obj


def _duplicate(builder, obj, name, slot):
    source = next(p for p in builder.result.parts if p.obj == obj)
    copy = obj.copy(); copy.data = obj.data.copy(); copy.name = name
    builder.result.collections["clothing"].objects.link(copy)
    copy["bp_role"] = "clothing"; copy["bp_wardrobe_slot"] = slot
    builder.result.parts.append(type(source)(copy, "clothing", source.bone, source.sprite_part, source.side))
    return copy


def _slot(name):
    if name.startswith("CLO_Shirt"): return "shirt"
    if name.startswith("CLO_Trousers"): return "trousers"
    if name.startswith("CLO_Boot"): return "boots"
    return "jacket"


def _smooth(obj):
    for face in obj.data.polygons: face.use_smooth = len(face.vertices) == 4


def _solid_outline(points, depth):
    """Closed shallow panel, front outline clockwise when seen from -Y."""
    n = len(points)
    vertices = [Vector(p) for p in points] + [Vector(p) + Vector((0, depth, 0)) for p in points]
    faces = [tuple(reversed(range(n))), tuple(range(n, n * 2))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    # Outline authoring may be either handedness; make the closed volume outward.
    volume = sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]])) / 6
                 for f in faces for i in range(1, len(f)-1))
    if volume < 0: faces = [tuple(reversed(f)) for f in faces]
    return vertices, faces


def _panel(builder, api, name, x, y, z, width, height, depth, material="Jacket", bone="chest", side="Center"):
    cut = min(width, height) * .13
    points = ((x-width/2+cut,y,z-height/2), (x+width/2-cut,y,z-height/2),
              (x+width/2,y,z-height/2+cut), (x+width/2,y,z+height/2-cut),
              (x+width/2-cut,y,z+height/2), (x-width/2+cut,y,z+height/2),
              (x-width/2,y,z+height/2-cut), (x-width/2,y,z-height/2+cut))
    # Each edge follows the actual curved shell, including the outer hip edge.
    # Flat tangent boxes stood centimetres clear of those edges.
    def attach(point):
        px,_,pz=point
        a,b=next((a,b) for a,b in zip(JACKET_PROFILES,JACKET_PROFILES[1:]) if a[0]<=pz<=b[0])
        t=(pz-a[0])/(b[0]-a[0]); rx=a[1]+(b[1]-a[1])*t; ry=a[2]+(b[2]-a[2])*t; cy=a[3]+(b[3]-a[3])*t
        surface=cy-ry*math.sqrt(max(0.,1-(px/rx)**2))
        offset=.008 if "Pocket" in name else .013
        return builder.v(px,surface-offset,pz)
    obj = builder.add_part(name, _solid_outline([attach(p) for p in points], builder.d(depth)),
                           material, "clothing", bone, "Body", "clothing", side)
    obj["bp_wardrobe_slot"] = "jacket"
    builder.skin_torso(obj); obj["bp_torso_weights"]=True
    return obj


def _open_jacket(builder, api, obj):
    profiles = api.subdivide_torso_profiles(JACKET_PROFILES)
    sides = 25; rings = len(profiles); vertices = []
    for inner in (False, True):
        for z,rx,ry,cy in profiles:
            gap = .24 + .20 * max(0., (z - 1.36) / .117)
            for j in range(sides):
                angle = -math.pi/2 + gap + (math.tau - 2*gap) * j/(sides-1)
                thickness = .004 if inner else 0.
                vertices.append(builder.v(math.cos(angle)*(rx-thickness), cy+math.sin(angle)*(ry-thickness),z))
    faces=[]; layer=rings*sides
    for k in range(2):
        for row in range(rings-1):
            for j in range(sides-1):
                a=k*layer+row*sides+j
                f=(a,a+1,a+sides+1,a+sides)
                faces.append(tuple(reversed(f)) if k else f)
    for row in range(rings-1):
        a=row*sides; b=a+sides
        faces.append((a,b,b+layer,a+layer))
        a+=sides-1; b+=sides-1
        faces.append((a,a+layer,b+layer,b))
    for j in range(sides-1):
        a=j; faces.append((a,a+layer,a+layer+1,a+1))
        a=(rings-1)*sides+j; faces.append((a,a+1,a+layer+1,a+layer))
    _replace_mesh(obj,(vertices,faces)); api.assign_jacket_body_uv(obj); builder.skin_torso(obj); _smooth(obj)
    obj["bp_real_front_opening"] = True; obj["bp_shell_thickness_m"] = .004
    # Constructed pockets and flaps: restrained bellows, not armoured plates.
    for side,sign in (("L",1),("R",-1)):
        for label,z,w,h in (("Chest",1.275,.094,.103),("Hip",.956,.109,.139)):
            x=sign*(.103 if label=="Chest" else .099)
            y=-.120 if label=="Chest" else -.102
            _panel(builder,api,f"CLO_JacketPocket{label}.{side}",x,y,z,w,h,.008)
            _panel(builder,api,f"CLO_JacketFlap{label}.{side}",x,y-.005,z+h*.40,w+.006,.032,.009,"JacketEdge")
        # A low folded standing collar, open toward the throat.
        points=[(sign*x,y,z) for x,y,z in ((.040,-.091,1.447),(.095,-.076,1.424),
                (.137,-.038,1.438),(.097,.006,1.477),(.063,-.017,1.491))]
        collar=builder.add_part(f"CLO_JacketCollar.{side}", _solid_outline([builder.v(*p) for p in points],builder.d(.006)),
                               "Jacket", "clothing", "chest", "Body", "clothing")
        collar["bp_wardrobe_slot"]="jacket"
        # Physical placket bound along the same torso field as the open body.
        points=[]
        for z,rx,ry,cy in profiles:
            gap=.24+.20*max(0.,(z-1.36)/.117)
            x=rx*math.sin(gap)
            for dx in (0,.008):
                px=x+dx
                points.append(builder.v(sign*px,cy-ry*math.sqrt(max(0.,1-(px/rx)**2))-.003,z))
        count=len(points); points+= [p+builder.v(0,.004,0) for p in points]
        faces=[]
        for row in range(len(profiles)-1):
            a=row*2
            faces.extend(((a,a+1,a+3,a+2),(a+count,a+count+2,a+count+3,a+count+1),
                (a,a+2,a+2+count,a+count),(a+1,a+1+count,a+3+count,a+3)))
        faces.extend(((0,count,count+1,1),(count-2,count-1,2*count-1,2*count-2)))
        volume=sum(points[f[0]].dot(points[f[i]].cross(points[f[i+1]]))/6 for f in faces for i in range(1,len(f)-1))
        if volume<0: faces=[tuple(reversed(f)) for f in faces]
        placket=builder.add_part(f"CLO_JacketPlacket.{side}",(points,faces),
                                "JacketDark","clothing","chest","Body","clothing")
        builder.skin_torso(placket); placket["bp_wardrobe_slot"]="jacket"; placket["bp_torso_weights"]=True


def _hands(builder, api, common):
    for side,sign in (("L",1.),("R",-1.)):
        wrist=builder.points[f"wrist.{side}"]; end=builder.points[f"hand.{side}"]
        direction=(end-wrist).normalized()
        # Ulnar edge points backward; palmar surface points toward the body.
        # The old global -Y depth turned the broad palm toward the viewer and
        # made both thumbs point out. These axes rotate with the unchanged hand.
        across=(Vector((0,1,0))-direction*direction.y).normalized()
        depth=direction.cross(across).normalized()*(-sign)

        def local(along, width=0., thickness=0., thin_palm=False):
            # The cuff/wrist ring stays put; the visible palm and fingers grow
            # together. Thin their cross-sections independently of that reach;
            # keep the original skeleton and grip/socket frames.
            blend=max(0.,min(1.,along/.030))
            size=1+(HAND_SIZE_SCALE-1)*blend
            if thin_palm:
                thickness*=1+(HAND_GIRTH_SCALE-1)*blend
            return wrist+(direction*builder.d(along)+across*builder.d(width)+
                          depth*builder.d(thickness))*size

        palm= bpy.data.objects[f"GEO_Hand.{side}"]
        vertices=[]; sides=10
        stations=((-.012,.020,.012),(.012,.030,.017),(.042,.032,.018),(.060,.025,.015))
        for along,width,thickness in stations:
            for j in range(sides):
                angle=math.tau*j/sides
                vertices.append(local(along,math.cos(angle)*width,math.sin(angle)*thickness,thin_palm=True))
        faces=[tuple(reversed(range(sides)))]
        faces += [(r*sides+j,r*sides+(j+1)%sides,(r+1)*sides+(j+1)%sides,(r+1)*sides+j)
                  for r in range(len(stations)-1) for j in range(sides)]
        faces.append(tuple((len(stations)-1)*sides+j for j in range(sides)))
        volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6 for f in faces for i in range(1,len(f)-1))
        if volume<0: faces=[tuple(reversed(f)) for f in faces]
        _replace_mesh(palm,(vertices,faces)); _smooth(palm)
        palm["bp_palm_normal_bind"]=list(depth)
        palm["bp_thumb_direction_bind"]=list(-across)
        palm["bp_hand_size_scale"]=HAND_SIZE_SCALE
        palm["bp_hand_girth_scale"]=HAND_GIRTH_SCALE
        for index,(offset,length,radius) in enumerate(((-.023,.033,.0085),(-.007,.042,.009),(.009,.039,.0085),(.024,.030,.0075))):
            start=local(.052,offset)
            tip=local(.052+length,offset,.004)
            radius*=HAND_SIZE_SCALE*HAND_GIRTH_SCALE
            obj=builder.add_part(f"GEO_Finger{index}.{side}",api.make_profiled_segment_geometry(start,tip,
                ((0,builder.d(radius),.85),(.42,builder.d(radius*1.03),.85),(.80,builder.d(radius*.90),.82),(1,builder.d(radius*.67),.80)),sides=7),
                "Skin","core",f"hand.{side}",f"{'Left' if side=='L' else 'Right'}LowerArm","body_detail","Left" if side=="L" else "Right",origin=wrist)
            _smooth(obj)
        thumb=bpy.data.objects[f"GEO_Thumb.{side}"]
        start=local(.018,-.022)
        tip=local(.055,-.041,.002)
        _replace_mesh(thumb,api.make_profiled_segment_geometry(start,tip,((0,builder.d(.013*HAND_SIZE_SCALE*HAND_GIRTH_SCALE),.8),(.45,builder.d(.013*HAND_SIZE_SCALE*HAND_GIRTH_SCALE),.8),(1,builder.d(.009*HAND_SIZE_SCALE*HAND_GIRTH_SCALE),.8)),sides=7))
        _smooth(thumb)


def _hair(builder, api, common):
    # Remove the four blunt top spikes and replace their separate silhouette
    # with two swept curtains around an open, slightly uneven centre part.
    retired=[p for p in builder.result.parts if p.obj.name.startswith("GEO_HairTuft")]
    for p in retired:
        builder.result.parts.remove(p); mesh=p.obj.data
        bpy.data.objects.remove(p.obj,do_unlink=True); bpy.data.meshes.remove(mesh)
    # Break the uniform crown's reflection into swept clumps without uncovering
    # the scalp. The part is retained; no isolated crown spikes are introduced.
    cap=bpy.data.objects["GEO_HairCap"]
    for vertex in cap.data.vertices:
        p=vertex.co+cap.location
        a=math.atan2(p.y/builder.scale+.020,p.x/builder.scale)
        z=p.z/builder.scale
        amount=.0026*math.sin(5*a+.8)+.0014*math.sin(3*a-1.1)
        p.x+=builder.d(math.cos(a)*amount)
        p.y+=builder.d(math.sin(a)*amount)
        p.z+=builder.d(.002*math.sin(3*a+.5)*max(0.,min(1.,(z-1.65)/.08)))
        vertex.co=p-cap.location
    cap.data.update()
    for side,sign in (("L",1.),("R",-1.)):
        # Two full, swept bangs descend over the forehead. Their rounded inner
        # edges cover the scalp cap's coarse zigzag while leaving the brows and
        # eyes readable through the centre part and joining the temple hair.
        path=((sign*.010,-.083,1.731),(sign*.020,-.111,1.707),
              (sign*.032,-.127,1.685),(sign*.048,-.128,1.663),
              (sign*.071,-.110,1.642),(sign*.095,-.062,1.606))
        widths=(.014,.026,.031,.030,.022,.005)
        depths=(.010,.013,.015,.015,.013,.004)
        outer_overlap=(0.,.020,.026,.017,.006,0.)
        vertices=[]; sides=12
        for row,p in enumerate(path):
            for j in range(sides):
                a=j*math.tau/sides
                vertices.append(builder.v(p[0]+math.cos(a)*widths[row]+
                                          sign*max(0.,sign*math.cos(a))*outer_overlap[row],
                                          p[1]+math.sin(a)*depths[row],p[2]))
        faces=[tuple(range(sides))]
        faces += [(r*sides+j,(r+1)*sides+j,(r+1)*sides+(j+1)%sides,r*sides+(j+1)%sides)
                  for r in range(len(path)-1) for j in range(sides)]
        faces.append(tuple(reversed(tuple((len(path)-1)*sides+j for j in range(sides)))))
        obj=builder.add_part(f"GEO_HairCurtain.{side}",(vertices,faces),"Hair","details","head","Body","hair")
        _smooth(obj)
        for index,offset in enumerate((-.010,.012)):
            points=[Vector((p[0]+sign*offset*min(.85,widths[row]/.026),p[1]-depths[row]-.0003,p[2]+.001))
                    for row,p in enumerate(path)]
            _hair_strand(builder,f"GEO_HairCrownStrand{index}.{side}",points,
                         Vector((0,-1,0)),None,tuple(3*row/(len(path)-1) for row in range(len(path))))
    # A closed back/side scalp shell continues down to the natural nape.
    # Its width overlaps the three free masses; no skin windows or tied tail.
    rows=((1.713,.060,.062,-.018),(1.681,.087,.085,-.018),
          (1.640,.095,.085,-.018),(1.598,.092,.079,-.018),(1.557,.083,.071,-.018))
    sides=25; vertices=[]
    for inner in (False,True):
        for z,rx,ry,cy in rows:
            for j in range(sides):
                a=math.radians(-135+270*j/(sides-1)); inset=.005 if inner else 0.
                edge=.012*(.5+.5*math.cos(9*a+.4)) if z==rows[-1][0] else 0.
                vertices.append(builder.v(math.sin(a)*(rx-inset),cy+math.cos(a)*(ry-inset),z+edge))
    faces=[]; layer=len(rows)*sides
    for k in range(2):
        for r in range(len(rows)-1):
            for j in range(sides-1):
                a=k*layer+r*sides+j; f=(a,a+1,a+sides+1,a+sides)
                faces.append(tuple(reversed(f)) if k else f)
    for r in range(len(rows)-1):
        a=r*sides; b=a+sides; faces.append((a,b,b+layer,a+layer))
        a+=sides-1; b+=sides-1; faces.append((a,a+layer,b+layer,b))
    for j in range(sides-1):
        faces.append((j,j+layer,j+layer+1,j+1))
        a=(len(rows)-1)*sides+j; faces.append((a,a+1,a+layer+1,a+layer))
    volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6 for f in faces for i in range(1,len(f)-1))
    if volume<0: faces=[tuple(reversed(f)) for f in faces]
    nape=builder.add_part("GEO_HairNape",(vertices,faces),"Hair","details","head","Body","hair"); _smooth(nape)
    for prefix,path in HAIR_PATHS.items():
        vertices=[]; sides=20 if prefix=="HairBack" else 26
        # Closely spaced rings distribute bending through each authored link.
        stations=[]
        for i in range(3):
            stations += [(i+t,Vector(path[i]).lerp(Vector(path[i+1]),t)) for t in (0,.5)]
        stations.append((3.,Vector(path[-1])))
        for position,p in stations:
            for j in range(sides):
                column=j/sides if prefix=="HairBack" else (j if j<13 else 25-j)/12
                point,_=_hair_surface(prefix,position,column,prefix!="HairBack" and j>=13)
                vertices.append(builder.v(*point))
        faces=[tuple(range(sides))]
        faces += [(r*sides+j,(r+1)*sides+j,(r+1)*sides+(j+1)%sides,r*sides+(j+1)%sides)
                  for r in range(len(stations)-1) for j in range(sides)]
        faces.append(tuple(reversed(tuple((len(stations)-1)*sides+j for j in range(sides)))))
        volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6 for f in faces for i in range(1,len(f)-1))
        if volume<0: faces=[tuple(reversed(f)) for f in faces]
        obj=builder.add_part("GEO_"+prefix+"Length",(vertices,faces),"Hair","details",prefix+".00","Body","hair")
        _hair_weights(obj,prefix,[s[0] for s in stations],sides); _smooth(obj)
        for index,column in enumerate((.10,.25,.40) if prefix=="HairBack" else (.09,.40,.72)):
            positions=(.25,.95,1.85,2.85-.13*(index%2))
            points=[]; normals=[]
            for position in positions:
                point,normal=_hair_surface(prefix,position,column+.018*math.sin(position+index))
                points.append(point+normal*.0008); normals.append(normal)
            _hair_strand(builder,f"GEO_{prefix}Strand{index}",points,normals,prefix,positions)


def _hair_surface(prefix,position,column,inner=False):
    path=HAIR_PATHS[prefix]; phase=position/3
    index=min(2,int(position)); p=Vector(path[index]).lerp(Vector(path[index+1]),position-index)
    if prefix=="HairBack":
        a=column*math.tau; width=.071+.014*(1-phase)
        ridge=.003*(.5+.5*math.cos(10*a+.5*phase))
        point=Vector((p.x+math.cos(a)*(width+ridge),
                      p.y+math.sin(a)*(.012+ridge)-.030*abs(math.cos(a))**1.5,p.z))
        normal=Vector((math.cos(a)*.28,math.sin(a),0)).normalized()
        point.z+=(.002+.018*(.5+.5*math.cos(10*a+.6))+.004*math.sin(3*a))*phase**5
        return point,normal
    # Thin curved curtain with longitudinal ridges and several tapered ends.
    # Ridges stay connected across the temple/ear instead of forming tentacles.
    a=math.radians(39+7*phase+(113-9*phase)*column)
    ridge=(.003+.003*phase)*(.5+.5*math.cos(8*math.pi*column+.65*phase))**2
    rx=.083+.024*math.sin(math.pi*min(1.,phase*1.25))+.012*phase+ridge
    ry=.093-.017*phase+ridge
    inset=.006 if inner else 0.; sign=1. if prefix=="HairLeft" else -1.
    point=Vector((sign*math.sin(a)*(rx-inset),-.018+.005*phase-math.cos(a)*(ry-inset),p.z))
    point.z+=.028*(1-phase)**4
    point.z+=(.001+.022*(.5+.5*math.cos(8*math.pi*column+.4))+.004*math.sin(3*a))*phase**5
    return point,Vector((sign*math.sin(a),-math.cos(a),0)).normalized()


def _hair_weights(obj,prefix,positions,sides):
    for g in list(obj.vertex_groups): obj.vertex_groups.remove(g)
    groups=[obj.vertex_groups.new(name=prefix+f".{i:02}") for i in range(3)]
    for row,position in enumerate(positions):
        index=min(2,int(position)); t=0. if index==2 else position-index
        ids=list(range(row*sides,(row+1)*sides))
        groups[index].add(ids,1-t,"REPLACE")
        if t>0: groups[index+1].add(ids,t,"REPLACE")
    obj["bp_secondary_hair"]=True; obj["bp_hair_chain"]=prefix


def _hair_strand(builder,name,points,normals,prefix,positions):
    """Sparse raised tonal strands: their material remains distinct in Unity."""
    if isinstance(normals,Vector): normals=[normals]*len(points)
    vertices=[]
    for row,(point,normal) in enumerate(zip(points,normals)):
        direction=(points[min(row+1,len(points)-1)]-points[max(0,row-1)]).normalized()
        across=direction.cross(normal).normalized()
        widths=(.0010,.0030,.0023,.00025)
        phase=3*row/(len(points)-1); index=min(2,int(phase))
        width=widths[index]+(widths[index+1]-widths[index])*(phase-index)
        for lateral,depth in ((-1,0),(0,1),(1,0),(0,-1)):
            vertices.append(builder.v(*(point+across*width*lateral+normal*.0007*depth)))
    faces=[(3,2,1,0)]
    faces += [(r*4+j,r*4+(j+1)%4,(r+1)*4+(j+1)%4,(r+1)*4+j)
              for r in range(len(points)-1) for j in range(4)]
    faces.append(tuple((len(points)-1)*4+j for j in range(4)))
    volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6 for f in faces for i in range(1,len(f)-1))
    if volume<0: faces=[tuple(reversed(f)) for f in faces]
    obj=builder.add_part(name,(vertices,faces),"HairHighlight","details",prefix+".00" if prefix else "head","Body","hair")
    if prefix: _hair_weights(obj,prefix,positions,4)
    _smooth(obj)


def _boots(builder, api):
    for side in ("L","R"):
        ankle=builder.points[f"ankle.{side}"]; toe=builder.points[f"toe.{side}"]
        # Keep every original support extreme, including the 12 mm raised heel.
        # Extra surface stations refine shading/UVs without changing how a
        # rotated boot plants on the floor in the existing action bank.
        reference,_=api.make_adult_boot_geometry(ankle.x,ankle.y,toe.y,builder.scale)
        vertices=[]
        for row in range(6):
            bl,br,tl,tr=reference[row*4:row*4+4]
            vertices.extend((bl,bl.lerp(br,.5),br,br.lerp(tr,.5),
                             tr,tr.lerp(tl,.5),tl,tl.lerp(bl,.5)))
        faces=[tuple(range(8))]
        for r in range(5):
            for j in range(8): faces.append((r*8+j,(r+1)*8+j,(r+1)*8+(j+1)%8,r*8+(j+1)%8))
        faces.append(tuple(reversed(tuple(5*8+j for j in range(8)))))
        volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6 for f in faces for i in range(1,len(f)-1))
        if volume<0: faces=[tuple(reversed(f)) for f in faces]
        boot=bpy.data.objects[f"CLO_Boot.{side}"]
        _replace_mesh(boot,(vertices,faces)); api.assign_boot_uv(boot,"BootLeft" if side=="L" else "BootRight")
        _smooth(boot)


def _sleeves(builder, api):
    """Refine cloth cross-sections within the existing opposite-hand envelope."""
    for side in ("L","R"):
        shoulder=builder.points[f"shoulder.{side}"]; elbow=builder.points[f"elbow.{side}"]; wrist=builder.points[f"wrist.{side}"]
        anatomical="Left" if side=="L" else "Right"
        sleeve=bpy.data.objects[f"CLO_JacketSleeve.{side}"]
        start=shoulder-(elbow-shoulder).normalized()*builder.d(.008)
        end=elbow.lerp(wrist,.02)
        # Narrow upper sleeves remove the padded shoulder/biceps silhouette;
        # the lower sleeve and long cuff retain the loose borrowed-coat fit.
        profile=((0,.030,.82),(.14,.048,.86),(.30,.050,.87),(.70,.050,.86),(1,.050,.84))
        _replace_mesh(sleeve,api.make_profiled_segment_geometry(start,end,
            tuple((t,builder.d(radius),depth) for t,radius,depth in profile),sides=16))
        api.assign_ring_strip_uv(sleeve,"JacketSleeve"+anatomical,16,len(profile)); _smooth(sleeve)
        forearm=bpy.data.objects[f"CLO_JacketForearm.{side}"]
        profile=((0,.049,.86),(.26,.047,(.049*.86+.045*.85)/(.049+.045)),
                 (.52,.045,.85),(.76,.0405,(.045*.85+.036*.83)/(.045+.036)),(1,.036,.83))
        _replace_mesh(forearm,api.make_profiled_segment_geometry(elbow,wrist,
            tuple((t,builder.d(radius),depth) for t,radius,depth in profile),sides=16))
        api.assign_ring_strip_uv(forearm,"JacketForearm"+anatomical,16,len(profile)); _smooth(forearm)


def repaint_clothing(canvas, api):
    """Cloth construction follows real geometry; pixel detail keeps the PS1 register."""
    rect=api.atlas_rect_bottom_left; line=api.atlas_line_bottom_left
    color={k:api.rgba_from_hex(v) for k,v in api.V2_PALETTE_HEX.items()}
    x,y,w,h=api.clothing_region("JacketBody")
    rect(canvas,x,y,x+w,y+h,color["Jacket"])
    for xx in (x,x+64):
        line(canvas,xx+3,y+10,xx+60,y+10,color["JacketEdge"])
        line(canvas,xx+3,y+105,xx+60,y+105,color["JacketDark"],2)
        line(canvas,xx+6,y+66,xx+59,y+66,color["JacketDark"])
    # Back action pleats and drawcord seam; front stitching follows the separate pockets.
    for xx in (x+78,x+112): line(canvas,xx,y+72,xx,y+106,color["JacketEdge"])
    for xx in (8,39):
        for bottom,top in ((69,90),(23,49)):
            line(canvas,x+xx,y+bottom,x+xx+17,y+bottom,color["JacketDark"])
            line(canvas,x+xx,y+bottom,x+xx,y+top,color["JacketEdge"])
    for px in range(w):
        for py in range(h):
            if (px*17+py*29)%157==0:
                base=color["Jacket"]
                rect(canvas,x+px,y+py,x+px+1,y+py+1,tuple(min(255,v+4) for v in base[:3])+(255,))
    # Continuous sleeves: construction follows a small seam, not a dark
    # armband. The right repair patch remains the only asymmetric marking.
    softened=tuple(round(color["Jacket"][i]*.7+color["JacketDark"][i]*.3) for i in range(3))+(255,)
    weave=tuple(round(color["Jacket"][i]*.7+color["JacketEdge"][i]*.3) for i in range(3))+(255,)
    for region in ("JacketSleeveLeft","JacketSleeveRight"):
        x,y,w,h=api.clothing_region(region)
        rect(canvas,x,y,x+w,y+h,color["Jacket"])
        # The seam drops onto the arm; the coat hangs beyond his narrower torso.
        line(canvas,x+3,y+14,x+w-4,y+14,softened)
        line(canvas,x+w//2,y+4,x+w//2,y+h-4,weave)
    x,y,w,h=api.clothing_region("JacketSleeveRight")
    rect(canvas,x+20,y+11,x+44,y+30,color["Patch"])
    line(canvas,x+20,y+11,x+44,y+11,color["JacketDark"])
    # Identical quiet diagonal folds, preserving clean lower-arm symmetry.
    for region in ("JacketForearmLeft","JacketForearmRight"):
        x,y,w,h=api.clothing_region(region)
        rect(canvas,x,y,x+w,y+h,color["Jacket"])
        line(canvas,x+2,y+h-4,x+w-3,y+h-4,color["JacketDark"])
        for px,py in ((5,11),(29,25),(14,38)):
            line(canvas,x+px,y+py,x+px+17,y+py+3,color["JacketDark"])
        for px in range(4,w-4,9):
            for py in range(5,h-12,11):
                line(canvas,x+px,y+py,x+px+1,y+py,weave)
    for region in ("JeansThighLeft","JeansThighRight","JeansShinLeft","JeansShinRight"):
        x,y,w,h=api.clothing_region(region)
        for py in (12,23,37):
            line(canvas,x+6,y+py,x+21,y+py+3,color["JeansEdge"])
            line(canvas,x+38,y+py+1,x+56,y+py-2,color["JeansEdge"])


def refine(builder, api, common):
    """Upgrade wearing geometry while keeping independent anatomy bindings."""
    records={p.obj.name:p for p in builder.result.parts}
    for name,target in CLOTHES_FROM_BODY.items():
        garment=_duplicate(builder,records[name].obj,target,_slot(target))
        garment["bp_default_visible"]=True
        if target.startswith("CLO_Trousers"):
            # Additional silhouette stations, including actual narrow knees.
            side=target[-1] if target[-2:][0]=='.' else None
            if side in ("L","R"):
                thigh="Thigh" in target
                start=builder.points[f"hip.{side}" if thigh else f"knee.{side}"]
                end=builder.points[f"knee.{side}" if thigh else f"ankle.{side}"]
                profile=((0,.083,.87),(.12,.087,.88),(.30,.086,.89),(.55,.078,.87),(.78,.065,.85),(1,.057,.84)) if thigh else \
                        ((0,.058,.84),(.12,.062,.86),(.30,.070,.88),(.48,.069,.88),(.70,.057,.85),(1,.044,.82))
                _replace_mesh(garment,api.make_profiled_segment_geometry(start,end,tuple((t,builder.d(r),d) for t,r,d in profile),sides=14))
                api.assign_ring_strip_uv(garment,('JeansThigh' if thigh else 'JeansShin')+('Left' if side=='L' else 'Right'),14,len(profile))
        _smooth(garment)
        body=records[name].obj; body.data.materials[0]=builder.result.materials["Skin"]
        body["bp_body_coverage"]=_slot(target); body["bp_default_visible"]=False
        # UV identity of existing bare-skin pixels remains stable.
        bare=next(k for k,v in api.BARE_SKIN_REGIONS.items() if v[0]==name)
        body[api.BARE_SKIN_ATLAS_REGION_PROP]=bare
        body.hide_render=True
        for vertex in body.data.vertices:
            p=vertex.co+body.location
            if name=="GEO_Torso":
                p.x*=.91; p.y=.004+(p.y-.004)*.90
            elif name=="GEO_Pelvis":
                p.x*=.91; p.y=.014+(p.y-.014)*.88
            elif "Foot" in name:
                # An actual bare foot: low arch and ankle, without a boot shaft.
                cx=builder.points[f"ankle.{name[-1]}"].x
                p.x=cx+(p.x-cx)*.83
                p.z*=.62
                p.y=builder.points[f"ankle.{name[-1]}"].y+(p.y-builder.points[f"ankle.{name[-1]}"].y)*.94
            else:
                bone=builder.result.rig.data.bones[records[name].bone]
                direction=(bone.tail_local-bone.head_local).normalized()
                axis=bone.head_local+direction*(p-bone.head_local).dot(direction)
                p=axis+(p-axis)*.89
            vertex.co=p-body.location
        body.data.update(); _smooth(body)
        if name=="GEO_Torso":
            body["bp_waist_half_width_m"]=.166*.91
            body["bp_chest_half_width_m"]=.187*.91
    # Upper/lower arms have true skin and stay independent of the jacket.
    for name in BODY_COVERAGE["jacket"]:
        if name not in records: continue
        obj=records[name].obj; obj.hide_render=True
        obj["bp_default_visible"]=False; obj["bp_body_coverage"]="jacket"
        _smooth(obj)
        if name.startswith(("GEO_UpperArm.","GEO_Forearm.")):
            # Match the genuinely slimmer arms underneath the revised sleeve.
            # Blend back to the unchanged wrist, preserving hand/socket frames.
            bone=builder.result.rig.data.bones[records[name].bone]
            direction=bone.tail_local-bone.head_local
            length=direction.length; direction.normalize()
            for vertex in obj.data.vertices:
                point=vertex.co+obj.location
                along=(point-bone.head_local).dot(direction)
                t=max(0.,min(1.,along/length))
                factor=(.82+.12*t) if "UpperArm" in name else (.85+.15*t)
                centre=bone.head_local+direction*along
                vertex.co=centre+(point-centre)*factor-obj.location
            obj.data.update()
    _open_jacket(builder,api,records["CLO_JacketBody"].obj)
    _sleeves(builder,api)
    _replace_mesh(records["GEO_Neck"].obj, common.make_frustum_between(builder.v(0,-.008,1.405),
        builder.v(0,-.020,1.522),builder.d(.074),builder.d(.0635),12,.84,0.))
    _smooth(records["GEO_Neck"].obj)
    _hands(builder,api,common)
    _hair(builder,api,common)
    _boots(builder,api)
    for part in builder.result.parts:
        if part.obj.name in ("GEO_Head","GEO_FaceSurface","GEO_HairCap","GEO_HairBack"):
            _smooth(part.obj)
    for side in ("L","R"):
        # Separate sleeve cuffs share the original shell's orientation.
        elbow=builder.points[f"elbow.{side}"]; wrist=builder.points[f"wrist.{side}"]
        cuff_end=wrist+(wrist-elbow).normalized()*builder.d(.004)
        cuff=builder.add_part(f"CLO_JacketCuff.{side}",api.make_profiled_segment_geometry(elbow.lerp(wrist,.86),cuff_end,
             ((0,builder.d(.0385),.84),(.35,builder.d(.0380),.84),(.68,builder.d(.0368),.84),(1,builder.d(.0345),.84)),sides=12),
             "Jacket","clothing",f"forearm.{side}",f"{'Left' if side=='L' else 'Right'}LowerArm","clothing","Left" if side=='L' else "Right")
        cuff["bp_wardrobe_slot"]="jacket"; _smooth(cuff)
        boot=bpy.data.objects[f"CLO_Boot.{side}"]
        # Beveled welt/heel geometry extends only within the original boot footprint.
        # Use a thin, fully enclosed welt, without coplanar collapsed faces.
        sole_geo=api.make_adult_boot_geometry(builder.points[f"ankle.{side}"].x,
                   builder.points[f"ankle.{side}"].y,builder.points[f"toe.{side}"].y,builder.scale)
        for row in range(6):
            bottom=sole_geo[0][row*4].z
            for p in sole_geo[0][row*4:row*4+4]:
                p.x=builder.points[f"ankle.{side}"].x+(p.x-builder.points[f"ankle.{side}"].x)*.998
                p.z=bottom+(p.z-bottom)*.12+.0003*builder.scale
        sole=builder.add_part(f"CLO_BootSole.{side}",sole_geo,"BootSole","clothing",f"foot.{side}",
                  f"{'Left' if side=='L' else 'Right'}LowerLeg","clothing","Left" if side=='L' else "Right")
        sole["bp_wardrobe_slot"]="boots"
    # The man is narrow inside a visibly roomy field jacket. Keep the coat's
    # established exterior/contact envelope; reduce actual skin and shirt,
    # including their depth, rather than pretending hidden anatomy changes it.
    for name in ("GEO_Torso","CLO_ShirtBody"):
        obj=bpy.data.objects[name]
        for vertex in obj.data.vertices:
            point=vertex.co+obj.location
            shoulder_blend=max(0.,min(1.,(point.z/builder.scale-1.35)/.065))
            point.x*=.88+.08*shoulder_blend
            point.y*=.87+.07*shoulder_blend
            vertex.co=point-obj.location
        for key in ("bp_waist_half_width_m","bp_chest_half_width_m"):
            obj[key]=float(obj[key])*.88
        obj.data.update()
    for side,sign in (("L",1.),("R",-1.)):
        shoulder=builder.add_part(f"GEO_Shoulder.{side}",common.make_ellipsoid_geometry(
            builder.v(sign*.184,-.003,1.407),builder.v(.043,.047,.052),12,6),
            "Skin","core",f"upper_arm.{side}",f"{'Left' if side=='L' else 'Right'}UpperArm",
            "body_part","Left" if side=='L' else "Right")
        shoulder["bp_body_coverage"]="jacket"; shoulder["bp_default_visible"]=False
        shoulder.hide_render=True; _smooth(shoulder)
    for part in builder.result.parts:
        obj=part.obj
        if part.role=="clothing":
            obj["bp_wardrobe_slot"]=_slot(obj.name); obj["bp_default_visible"]=True
        elif not obj.get("bp_body_coverage"):
            obj["bp_default_visible"]=True
    player_hand_frames.ensure_contact_convexity(builder)


def manifest(result):
    items=[]
    for slot,covered in BODY_COVERAGE.items():
        names=[p.obj.name for p in result.parts if p.role=="clothing" and p.obj.get("bp_wardrobe_slot")==slot]
        items.append({"id":"hero_"+slot,"slot":slot,"renderers":names,"covered_body_renderers":list(covered)})
    counts={}
    for part in result.parts:
        part.obj.data.calc_loop_triangles(); counts[part.obj.name]=len(part.obj.data.loop_triangles)
    hidden=set(name for values in BODY_COVERAGE.values() for name in values)
    return {"wardrobe":{"contract":"hero_outfit_v1","default_outfit_id":"hero_field_workwear","items":items},
            "body_bone_count":31,"hair_bone_count":len(HAIR_BONES),
            "hair":{"contract":"hero_collar_hair_v1","chains":[{"name":prefix,
                "bones":[prefix+suffix for suffix in (".00",".01",".02",".Tip")],
                "points_blender":[list(p) for p in path],
                "renderers":[p.obj.name for p in result.parts if p.obj.get("bp_hair_chain")==prefix],
                "contact_radius_m":.012} for prefix,path in HAIR_PATHS.items()]},
            "quality":{"worn_triangle_count":sum(c for name,c in counts.items() if name not in hidden),
                "hidden_body_triangle_count":sum(counts[name] for name in hidden),
                "complete_triangle_count":sum(counts.values()),"worn_triangle_budget":[0,8000]},
            "fit":{"body_torso_width_factor":.88,"shirt_torso_width_factor":.88,
                "hand_size_scale":HAND_SIZE_SCALE,"hand_girth_scale":HAND_GIRTH_SCALE,
                "hand_wrist_blend_m":.030,
                "torso_depth_factor":.87,"upper_sleeve_max_radius_m":.050,
                "shoulder_shell_half_width_m":.188,
                "jacket":"narrow shoulders and upper sleeves; loose body, hem and long cuffs"},
            "body_coverage":[{"renderer":name,"region":slot} for slot,names in BODY_COVERAGE.items() for name in names]}


def validate(result, errors):
    player_hand_frames.validate_neutral(result, errors)
    player_hand_frames.validate_contact_convexity(result, errors)
    data=manifest(result); rows={p.obj.name:p for p in result.parts}
    clothing=[p.obj.name for p in result.parts if p.role=="clothing"]
    declared=[name for item in data["wardrobe"]["items"] for name in item["renderers"]]
    if sorted(clothing)!=sorted(declared) or len(declared)!=len(set(declared)):
        errors.append("Every independently wearable garment must have exactly one slot")
    for item in data["wardrobe"]["items"]:
        for name in item["covered_body_renderers"]:
            if name not in rows or rows[name].role!="body_part": errors.append("Clothing coverage requires independent anatomy: "+name)
    for p in result.parts:
        for vertex in p.obj.data.vertices:
            if abs(sum(g.weight for g in vertex.groups)-1.)>1e-5:
                errors.append("Normalized detailed-character weights required: "+p.obj.name); break
    worn=data["quality"]["worn_triangle_count"]
    if worn>8000: errors.append(f"Worn character exceeds 8000-triangle budget: {worn}")
    if not rows["CLO_JacketBody"].obj.get("bp_real_front_opening"):
        errors.append("Independent shirt must be revealed through real open jacket geometry")
    if data["quality"]["complete_triangle_count"]>11000:
        errors.append("Complete character including independent hidden body exceeds 11000 triangles")
    if set(HAIR_BONES)-set(result.rig.data.bones.keys()): errors.append("Missing authored collar-length hair joints")
