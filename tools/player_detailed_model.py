"""Measured Hero V2 body, independent workwear and collar-length hair.

Only geometry/weights/metadata live here. The production generator continues to
own the original skeleton, action bank, export and expression atlases.
"""
from __future__ import annotations

import math

import bpy
import character_joint_surfaces
from mathutils import Vector
import player_hand_frames
import player_hand_grip
import player_boots


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
    "belt": (),
}
CLOTHES_FROM_BODY = {
    "GEO_Torso": "CLO_ShirtBody", "GEO_Pelvis": "CLO_TrousersPelvis",
    **{f"GEO_{part}.{side}": f"CLO_Trousers{part}.{side}" for part in ("Thigh", "Shin") for side in ("L", "R")},
    "GEO_Foot.L": "CLO_Boot.L", "GEO_Foot.R": "CLO_Boot.R",
}
# Waist and upper ribcage remain lean; the chest now actually flares toward
# the unchanged shoulder joints. The last ring slopes into the neck.
BODY_TORSO_PROFILES = ((.878,.132,.070,.012),(.970,.133,.074,.010),
    (1.105,.145,.081,.004),(1.250,.172,.089,-.004),
    (1.350,.190,.091,-.008),(1.415,.193,.085,-.010))
PELVIS_PROFILES = ((.775,.109,.070,.013,0.),(.815,.145,.094,.018,.020),
    (.850,.151,.102,.017,.034),(.878,.148,.100,.014,.029),
    (.920,.140,.082,.012,.012),(.972,.124,.067,.010,0.))
PELVIS_SIDES=24
# Flat cloth panels hang from the yoke, draw in mildly at the waist, and
# open into a straight free hem. Rear ease is gradual, never a gluteal bump.
JACKET_PROFILES = ((.805,.191,.139,.017),(.895,.188,.136,.014),
    (.970,.181,.135,.010),(1.115,.188,.132,.002),(1.285,.204,.127,-.006),
    (1.360,.210,.123,-.010),(1.452,.204,.110,-.012),(1.477,.088,.071,-.014))


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
    if name.startswith("CLO_Belt"): return "belt"
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
        surface=_jacket_y(px,pz,rx,ry,cy,False)
        offset=.008 if "Pocket" in name else .013
        return builder.v(px,surface-offset,pz)
    obj = builder.add_part(name, _solid_outline([attach(p) for p in points], builder.d(depth)),
                           material, "clothing", bone, "Body", "clothing", side)
    obj["bp_wardrobe_slot"] = "jacket"
    builder.skin_torso(obj); obj["bp_torso_weights"]=True
    return obj


def _jacket_y(x,z,rx,ry,cy,back):
    """Shared panel cut for shell, constructed pockets and opening plackets."""
    shoulder=max(0.,min(1.,(z-1.360)/.117))
    exponent=(.65 if back else .78)*(1-shoulder)+shoulder
    section=math.sqrt(max(0.,1-(x/rx)**2))**exponent
    front_ease=1. if back else 1.-.10*character_joint_surfaces._smooth((z-1.0)/.2)
    # Long gravity folds live near the side panels, away from the throat.
    fold=.0028*math.exp(-((abs(x)-.155)/.017)**2)*(1-shoulder)
    return cy+(1 if back else -1)*(ry*front_ease*section+fold)


def _open_jacket(builder, api, obj):
    profiles = api.subdivide_torso_profiles(JACKET_PROFILES)
    profiles=[p for p in profiles if all(abs(p[0]-z)>.00001 for z in (.850,.9325,1.0666666667,1.200))]
    sides = 25; rings = len(profiles); vertices = []
    for inner in (False, True):
        for z,rx,ry,cy in profiles:
            gap = .24 + .20 * max(0., (z - 1.36) / .117)
            for j in range(sides):
                angle = -math.pi/2 + gap + (math.tau - 2*gap) * j/(sides-1)
                thickness = .004 if inner else 0.
                x=math.cos(angle)*(rx-thickness)
                vertices.append(builder.v(x,_jacket_y(x,z,rx-thickness,ry-thickness,cy,math.sin(angle)>0),z))
    faces=[]; layer=rings*sides
    first=next(i for i,p in enumerate(profiles) if abs(p[0]-1.3225)<.00001)
    patches=((first,4),(first,17))
    for k in range(2):
        for row in range(rings-1):
            for j in range(sides-1):
                if any(r<=row<r+3 and c<=j<c+3 for r,c in patches):continue
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
    armholes=[]
    for r,c in patches:
        ring=[r*sides+j for j in range(c,c+4)]
        ring += [i*sides+c+3 for i in range(r+1,r+4)]
        ring += [(r+3)*sides+j for j in range(c+2,c-1,-1)]
        ring += [i*sides+c for i in range(r+2,r,-1)]
        armholes.append(ring)
        faces.extend((a,b,b+layer,a+layer) for a,b in zip(ring,ring[1:]+ring[:1]))
    used=sorted({i for face in faces for i in face});mapping={old:new for new,old in enumerate(used)}
    _replace_mesh(obj,([vertices[i] for i in used],[tuple(mapping[i] for i in face) for face in faces]))
    obj["bp_jacket_outer_grid"]=[mapping.get(i,-1) for i in range(layer)]
    for side,ring in zip(("L","R"),armholes):
        obj["bp_jacket_armhole."+side]=[mapping[i] for i in ring]
        obj["bp_jacket_armhole_inner."+side]=[mapping[i+layer] for i in ring]
    api.assign_jacket_body_uv(obj); builder.skin_torso(obj); _smooth(obj)
    obj["bp_real_front_opening"] = True; obj["bp_shell_thickness_m"] = .004
    obj["bp_hem_half_width_m"] = JACKET_PROFILES[0][1]
    obj["bp_waist_half_width_m"] = JACKET_PROFILES[1][1]
    obj["bp_chest_half_width_m"] = JACKET_PROFILES[5][1]
    obj["bp_yoke_half_width_m"] = JACKET_PROFILES[6][1]
    obj["bp_yoke_rise_m"] = JACKET_PROFILES[7][0]-JACKET_PROFILES[6][0]
    # Constructed pockets and flaps: restrained bellows, not armoured plates.
    for side,sign in (("L",1),("R",-1)):
        for label,z,w,h in (("Chest",1.275,.094,.103),("Hip",.956,.109,.139)):
            x=sign*(.103 if label=="Chest" else .099)
            y=-.120 if label=="Chest" else -.102
            _panel(builder,api,f"CLO_JacketPocket{label}.{side}",x,y,z,w,h,.008)
            _panel(builder,api,f"CLO_JacketFlap{label}.{side}",x,y-.005,z+h*.40,w+.006,.032,.009,"JacketEdge")
        # A low folded standing collar, open toward the throat.
        points=[(sign*x,y,z) for x,y,z in ((.040,-.091,1.467),(.095,-.076,1.456),
                (.137,-.038,1.466),(.097,.006,1.477),(.063,-.017,1.491))]
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
                points.append(builder.v(sign*px,_jacket_y(px,z,rx,ry,cy,False)-.003,z))
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
        start=shoulder-(elbow-shoulder).normalized()*builder.d(.012)
        end=elbow.lerp(wrist,.02)
        # Slight upper-sleeve room matches the free coat body without turning
        # the unchanged anatomical shoulders into padded caps.
        profile=((0,.055,.88),(.14,.060,.86),(.30,.055,.87),(.70,.049,.86),
                 (.86,.050,.8493333333),(.94,.050,.844),(1,.050,.84))
        _replace_mesh(sleeve,api.make_profiled_segment_geometry(start,end,
            tuple((t,builder.d(radius),depth) for t,radius,depth in profile),sides=12))
        jacket=bpy.data.objects["CLO_JacketBody"]
        source=list(jacket["bp_jacket_armhole."+side])
        points=[jacket.data.vertices[i].co+jacket.location for i in source]
        centre=sum(points,Vector())/12
        axis=(elbow-shoulder).normalized()
        reference=[(v.co+sleeve.location-start).normalized() for v in sleeve.data.vertices[:12]]
        radial=[(p-centre-axis*(p-centre).dot(axis)).normalized() for p in points]
        shift=min(range(12),key=lambda s:sum((reference[j]-radial[(j+s)%12]).length_squared for j in range(12)))
        source=source[shift:]+source[:shift]
        for vertex,index in zip(sleeve.data.vertices[:12],source):
            vertex.co=jacket.data.vertices[index].co+jacket.location-sleeve.location
        sleeve["bp_jacket_armhole_body_ids"]=source
        # Sleeve cloth has a small forward elbow reserve, rather than a taut
        # cylinder on a straight bone. Endpoint rings retain the exact seam.
        for row,(t,_,_) in enumerate(profile[1:-1],1):
            for vertex in sleeve.data.vertices[row*12:(row+1)*12]:
                vertex.co.y-=builder.d(.005*math.sin(math.pi*t))
        sleeve.data.update()
        api.assign_ring_strip_uv(sleeve,"JacketSleeve"+anatomical,12,len(profile)); _smooth(sleeve)
        _retain_strip_uv(sleeve,api,"JacketSleeve"+anatomical,12,(0,.25,.5,.75,.8833333333,.95,1))
        sleeve["bp_shoulder_overlap_m"]=0.;sleeve["bp_set_in_sleeve"]=True
        forearm=bpy.data.objects[f"CLO_JacketForearm.{side}"]
        profile=((0,.049,.86),(.07,.0484615385,.8587307692),(.15,.0478461538,.8572802198),
                 (.26,.047,(.049*.86+.045*.85)/(.049+.045)),
                 (.52,.045,.85),(.76,.0405,(.045*.85+.036*.83)/(.045+.036)),(1,.036,.83))
        _replace_mesh(forearm,api.make_profiled_segment_geometry(elbow,wrist,
            tuple((t,builder.d(radius),depth) for t,radius,depth in profile),sides=12))
        api.assign_ring_strip_uv(forearm,"JacketForearm"+anatomical,12,len(profile)); _smooth(forearm)
        _retain_strip_uv(forearm,api,"JacketForearm"+anatomical,12,(0,.07/.26*.25,.15/.26*.25,.25,.5,.75,1))


def _retain_strip_uv(obj,api,region,sides,stations):
    _,v0,_,v1=api.uv_region_normalized(region)
    for face in obj.data.polygons:
        if len({index//sides for index in face.vertices})==1:continue
        for loop in face.loop_indices:
            ring=obj.data.loops[loop].vertex_index//sides
            obj.data.uv_layers.active.data[loop].uv.y=v0+(v1-v0)*stations[ring]


def _torso_and_shoulders(builder, api, common):
    """A tapered trunk and sloping deltoid transitions, with separate anatomy.

    Keep the old ring rows/atlas coordinates, and the rig's shoulder pivots.
    Shoulder connectors end on the upper-arm seam instead of being balls
    placed between an abruptly narrowed chest and a detached arm.
    """
    profiles=api.subdivide_torso_profiles(BODY_TORSO_PROFILES)
    for name,clearance in (("GEO_Torso",0.),("CLO_ShirtBody",.004)):
        obj=bpy.data.objects[name]
        sides=PELVIS_SIDES if not clearance else api.TORSO_SIDES
        stations=([( .920,.140,.082,.012)]+[p for p in profiles if p[0]>=.970]) if not clearance else profiles
        rings=tuple((builder.d(z),builder.d(rx+clearance),builder.d(ry+clearance),builder.d(cy))
                    for z,rx,ry,cy in stations)
        vertices,faces=common.make_ringed_ellipsoid(builder.v(0,0,0),rings,sides)
        if not clearance:vertices[:sides]=_pelvis_points(builder,PELVIS_PROFILES[4:5])
        for point in vertices:
            z=point.z/builder.scale
            blend=max(0.,min(1.,(z-1.350)/.065))
            across=abs(point.x)/(builder.d(BODY_TORSO_PROFILES[-1][1]+clearance))
            centre_lift=.031 if not clearance else .045
            point.z+=builder.d(blend*(.025+centre_lift*(1-min(1.,across))))
        _replace_mesh(obj,(vertices,faces));builder.skin_torso(obj);_smooth(obj)
        # Both shapes share the existing bare-torso UV chart, including the
        # unchanged nipple/waist station rows; the shirt remains flat charcoal.
        api.assign_bare_skin_ring_strip_uv(obj,"BareTorso",sides,len(rings))
        _,v0,_,v1=api.atlas_kit.uv_rect_normalized(*api.bare_skin_region("BareTorso"),api.BARE_SKIN_ATLAS_SIZE,1.)
        for polygon in obj.data.polygons:
            if len({i//sides for i in polygon.vertices})==1 or any(i>=sides*len(stations) for i in polygon.vertices):continue
            for loop in polygon.loop_indices:
                ring=obj.data.loops[loop].vertex_index//sides
                obj.data.uv_layers.active.data[loop].uv.y=v0+(v1-v0)*api.torso_ring_v(stations[ring][0])
        obj["bp_torso_sides"]=sides
        obj["bp_waist_half_width_m"]=BODY_TORSO_PROFILES[1][1]+clearance
        obj["bp_chest_half_width_m"]=BODY_TORSO_PROFILES[4][1]+clearance
        obj["bp_upper_torso_half_width_m"]=BODY_TORSO_PROFILES[-1][1]+clearance
        obj["bp_torso_taper_contract"]="lean_waist_to_shoulder_v1"
    for side,sign in (("L",1.),("R",-1.)):
        pivot=builder.points[f"shoulder.{side}"]
        direction=(builder.points[f"elbow.{side}"]-pivot).normalized()
        # Use the upper arm's existing angular basis for a ten-point shared
        # seam. Broad, buried roots follow the ribcage rather than projecting
        # a spherical shoulder cap above it.
        reference,_=api.make_profiled_segment_geometry(pivot,pivot+direction*builder.d(.01),
            ((0,builder.d(.039),.86),(1,builder.d(.039),.86)),sides=10)
        radial=[point-pivot for point in reference[:10]]
        centres=(builder.v(sign*.140,pivot.y/builder.scale,1.400),
                 builder.v(sign*.187,pivot.y/builder.scale,1.424),pivot)
        radii=(.039,.044,.039)
        vertices=[centre+offset*(radius/.039) for centre,radius in zip(centres,radii) for offset in radial]
        faces=[tuple(reversed(range(10)))]
        faces.extend((row*10+j,row*10+(j+1)%10,(row+1)*10+(j+1)%10,(row+1)*10+j)
                     for row in range(2) for j in range(10))
        faces.append(tuple(range(20,30)))
        volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6
                   for f in faces for i in range(1,len(f)-1))
        if volume<0:faces=[tuple(reversed(face)) for face in faces]
        shoulder=builder.add_part(f"GEO_Shoulder.{side}",(vertices,faces),
            "Skin","core",f"upper_arm.{side}",f"{'Left' if side=='L' else 'Right'}UpperArm",
            "body_part","Left" if side=='L' else "Right")
        shoulder["bp_body_coverage"]="jacket";shoulder["bp_default_visible"]=False
        shoulder["bp_shoulder_connector"]=True;shoulder.hide_render=True;_smooth(shoulder)


def _pelvis_points(builder,profiles):
    points=[]
    for z,rx,ry,cy,lobe in profiles:
        row=PELVIS_PROFILES.index((z,rx,ry,cy,lobe))
        for j in range(PELVIS_SIDES):
            angle=j*math.tau/PELVIS_SIDES;x=rx*math.cos(angle)
            back=max(0.,math.sin(angle))
            front=(.070,.074,.077,.075,.073,.067)[row]
            y=cy+(ry if back>0 else front)*math.sin(angle)
            if back>0 and lobe:
                # Two elliptical posterior lobes, with an explicit central
                # valley. The surrounding side/crotch surface stays joined.
                rounded=.095*math.sqrt(max(0.,1-((abs(x)-.063)/.073)**2))
                target=max(y,cy+.038+rounded)-.035*math.exp(-(x/.020)**2)
                y+=(target-y)*.75*(lobe/.034)*back
            points.append(builder.v(x,y,z))
    return points


def _pelvis_and_hips(builder,api):
    """Modest male hips with paired posterior mass beneath independent pants."""
    rows={p.obj.name:p.obj for p in builder.result.parts}
    for name,clearance in (("GEO_Pelvis",0.),("CLO_TrousersPelvis",.008)):
        profiles=tuple(p for i,p in enumerate(PELVIS_PROFILES) if not clearance or i!=4)
        body_points=_pelvis_points(builder,profiles)
        faces=[tuple(reversed(range(PELVIS_SIDES)))]
        faces.extend((r*PELVIS_SIDES+j,r*PELVIS_SIDES+(j+1)%PELVIS_SIDES,
                      (r+1)*PELVIS_SIDES+(j+1)%PELVIS_SIDES,(r+1)*PELVIS_SIDES+j)
                     for r in range(len(profiles)-1) for j in range(PELVIS_SIDES))
        faces.append(tuple(range((len(profiles)-1)*PELVIS_SIDES,len(body_points))))
        obj=rows[name];points=[]
        for i,p in enumerate(body_points):
            angle=(i%PELVIS_SIDES)*math.tau/PELVIS_SIDES
            points.append(p+builder.v(math.cos(angle)*clearance,math.sin(angle)*clearance,0))
        if clearance:
            # The anatomical pelvis cap is hidden inside the belly. A garment
            # waistband follows the visible torso, rather than that buried cap.
            for j in range(PELVIS_SIDES):
                angle=j*math.tau/PELVIS_SIDES
                points[-PELVIS_SIDES+j]=builder.v(.142*math.cos(angle),.010+.084*math.sin(angle),.972)
        _replace_mesh(obj,(points,faces));_smooth(obj)
        if clearance:
            api.assign_ring_strip_uv(obj,"JeansPelvis",PELVIS_SIDES,len(profiles))
        else:
            api.assign_bare_skin_ring_strip_uv(obj,"BarePelvis",PELVIS_SIDES,len(profiles))
        # Inserted silhouette rows interpolate the existing atlas stations.
        old_z=(.775,.820,.878,.935,.972);stations=[]
        for profile in profiles:
            z=profile[0];index=next((i for i in range(4) if old_z[i]<=z<=old_z[i+1]),3)
            stations.append((index+(z-old_z[index])/(old_z[index+1]-old_z[index]))/4)
        region="JeansPelvis" if clearance else "BarePelvis"
        if clearance:_retain_strip_uv(obj,api,region,PELVIS_SIDES,stations)
        else:
            _,v0,_,v1=api.atlas_kit.uv_rect_normalized(*api.bare_skin_region(region),api.BARE_SKIN_ATLAS_SIZE,1.)
            for polygon in obj.data.polygons:
                if len({i//PELVIS_SIDES for i in polygon.vertices})==1:continue
                for loop in polygon.loop_indices:
                    ring=obj.data.loops[loop].vertex_index//PELVIS_SIDES
                    obj.data.uv_layers.active.data[loop].uv.y=v0+(v1-v0)*stations[ring]
        for vertex,p in zip(obj.data.vertices,body_points):
            x,z=p.x/builder.scale,p.z/builder.scale
            vertical=character_joint_surfaces._smooth((.870-z)/.095)
            lateral=character_joint_surfaces._smooth((abs(x)-.016)/.09)
            mix=.40*vertical*lateral;side="L" if x>=0 else "R"
            character_joint_surfaces._assign(obj,vertex.index,{"pelvis":1-mix,"thigh."+side:mix})
        obj["bp_pelvis_surface"]="male_pelvis_gluteal_v1"
        character_joint_surfaces._surface(builder.result,obj)
    # Seat thigh roots inside the hip envelope, retaining every lower station,
    # knee seam ID, and the authored slim upper-thigh girth.
    for side in ("L","R"):
        for stem,sides,factor in (("GEO_Thigh.",8,.65),("CLO_TrousersThigh.",12,.74)):
            obj=rows[stem+side];bone=builder.result.rig.data.bones["thigh."+side]
            axis=(bone.tail_local-bone.head_local).normalized()
            for vertex in obj.data.vertices[:sides]:
                p=vertex.co+obj.location;centre=bone.head_local+axis*(p-bone.head_local).dot(axis)
                embedded=Vector((centre.x,.013*builder.scale,centre.z+.030*builder.scale))
                vertex.co=embedded+(p-centre)*factor-obj.location
            for vertex in obj.data.vertices[sides:2*sides]:
                p=vertex.co+obj.location;centre=bone.head_local+axis*(p-bone.head_local).dot(axis)
                embedded=Vector((centre.x,.013*builder.scale,centre.z))
                vertex.co=embedded+(p-centre)*(.88 if stem.startswith("GEO") else .90)-obj.location
            obj.data.update()


def _belt(builder,api):
    trousers=next(p.obj for p in builder.result.parts if p.obj.name=="CLO_TrousersPelvis")
    lower=[v.co+trousers.location for v in trousers.data.vertices[-2*PELVIS_SIDES:-PELVIS_SIDES]]
    upper=[v.co+trousers.location for v in trousers.data.vertices[-PELVIS_SIDES:]]
    def outline(z,angle,clearance):
        u=angle/math.tau*PELVIS_SIDES;j=int(u)%PELVIS_SIDES;t=u-int(u)
        a=lower[j].lerp(lower[(j+1)%PELVIS_SIDES],t)
        b=upper[j].lerp(upper[(j+1)%PELVIS_SIDES],t)
        point=a.lerp(b,(z*builder.scale-a.z)/(b.z-a.z))
        return point+builder.v(math.cos(angle)*clearance,math.sin(angle)*clearance,0)
    sides=16;vertices=[]
    # Rectangular webbing section: inner-bottom, outer-bottom, outer-top, inner-top.
    for z,offset in ((.942,.004),(.942,.007),(.972,.007),(.972,.004)):
        vertices.extend(outline(z,j*math.tau/sides,offset) for j in range(sides))
    faces=[(r*sides+j,r*sides+(j+1)%sides,((r+1)%4)*sides+(j+1)%sides,((r+1)%4)*sides+j)
           for r in range(4) for j in range(sides)]
    volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6
               for f in faces for i in range(1,len(f)-1))
    if volume<0:faces=[tuple(reversed(face)) for face in faces]
    obj=builder.add_part("CLO_Belt",(vertices,faces),"JacketDark","clothing","pelvis","Body","clothing")
    obj["bp_wardrobe_slot"]="belt";obj["bp_independent_webbing"]=True
    obj["bp_width_m"]=.030;obj["bp_garment_clearance_m"]=.004;_smooth(obj)
    front=min(p.y for p in vertices)
    points=[builder.v(x,front/builder.scale-.003,z) for x,z in ((-.020,.939),(.020,.939),(.020,.975),(-.020,.975))]
    buckle=builder.add_part("CLO_BeltBuckle",_solid_outline(points,builder.d(.004)),
        "Metal","clothing","pelvis","Body","clothing")
    buckle["bp_wardrobe_slot"]="belt";buckle["bp_independent_buckle"]=True


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
    player_boots.paint(canvas,api)


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
                profile=((0,.083,.87),(.12,.087,.88),(.30,.086,.89),(.55,.078,.87),(.78,.065,.85),
                         (.90,.0606363636,.8445454545),(1,.057,.84)) if thigh else \
                        ((0,.058,.84),(.06,.060,.85),(.12,.062,.86),(.30,.070,.88),(.48,.069,.88),(1,.044,.82))
                _replace_mesh(garment,api.make_profiled_segment_geometry(start,end,tuple((t,builder.d(r),d) for t,r,d in profile),sides=12))
                region=('JeansThigh' if thigh else 'JeansShin')+('Left' if side=='L' else 'Right')
                api.assign_ring_strip_uv(garment,region,12,len(profile))
                _retain_strip_uv(garment,api,region,12,(0,.2,.4,.6,.8,.9090909091,1) if thigh else (0,.1,.2,.4,.6,1))
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
        # Palette shadows are supplied by lighting/skin data. A separate dark
        # upper-arm material made the shoulder seam read as a plugged limb.
        obj.data.materials[0]=builder.result.materials["Skin"]
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
    _replace_mesh(records["GEO_Neck"].obj, api.make_profiled_segment_geometry(builder.v(0,-.008,1.405),
        builder.v(0,-.020,1.522),((0,builder.d(.074),.84),(.35,builder.d(.069),.85),
            (.72,builder.d(.058),.87),(1,builder.d(.054),.87)),sides=12))
    records["GEO_Neck"].obj.data.materials[0]=builder.result.materials["Skin"]
    records["GEO_Neck"].obj["bp_top_width_m"]=.108
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
    player_boots.author(builder,api)
    _torso_and_shoulders(builder,api,common)
    _pelvis_and_hips(builder,api)
    _belt(builder,api)
    for part in builder.result.parts:
        obj=part.obj
        if part.role=="clothing":
            obj["bp_wardrobe_slot"]=_slot(obj.name); obj["bp_default_visible"]=True
        elif not obj.get("bp_body_coverage"):
            obj["bp_default_visible"]=True
    # Joint station loops are construction geometry, not redundant rigid-hull
    # vertices. Contacts use posed conservative envelopes after articulation.
    for side in ("L","R"):
        for stem in ("CLO_JacketSleeve.","CLO_JacketForearm."):
            bpy.data.objects[stem+side]["bp_joint_surface"]=character_joint_surfaces.CONTRACT
    player_hand_frames.ensure_contact_convexity(builder)
    player_hand_grip.author(builder.result, builder.scale)
    _joints(builder)


def _joints(builder):
    """Shared skin seams and a separately authored loose M-65 elbow surface."""
    result=builder.result; rows={p.obj.name:p.obj for p in result.parts}
    scale=builder.scale
    def seam(label,a,b,upper,lower,band,loose=False,corrective=None):
        bone_a=result.rig.data.bones[upper]; bone_b=result.rig.data.bones[lower]
        ring_a=character_joint_surfaces.terminal_ring(rows[a],bone_a.tail_local-bone_a.head_local,"end")
        ring_b=character_joint_surfaces.terminal_ring(rows[b],bone_b.tail_local-bone_b.head_local,"start")
        return character_joint_surfaces.join_ring_seam(result,rows[a],ring_a,rows[b],ring_b,upper,lower,
            name=label,band_m=band*scale,loose=loose,
            corrective_name=corrective or (("JacketElbowFold."+a[-1]) if loose else None))
    for side in ("L","R"):
        upper=rows["GEO_UpperArm."+side]
        axis=result.rig.data.bones["upper_arm."+side].tail_local-result.rig.data.bones["upper_arm."+side].head_local
        ring=character_joint_surfaces.terminal_ring(upper,axis,"start")
        character_joint_surfaces.join_ring_seam(result,rows["GEO_Shoulder."+side],list(range(20,30)),
            upper,ring,"chest","upper_arm."+side,name="Skin shoulder."+side,band_m=.060*scale)
        # The embedded collarbone side remains on the trunk; only the distal
        # ring shares the upper arm's field. This avoids moving the whole
        # shoulder root away from the chest when the arm rises.
        for index in range(20):
            mix=0. if index<10 else .25
            character_joint_surfaces._assign(rows["GEO_Shoulder."+side],index,
                {"chest":1-mix,"upper_arm."+side:mix})
        seam("Skin elbow."+side,"GEO_UpperArm."+side,"GEO_Forearm."+side,
             "upper_arm."+side,"forearm."+side,.055,corrective="JointVolume.Elbow."+side)
        seam("Jacket elbow."+side,"CLO_JacketSleeve."+side,"CLO_JacketForearm."+side,
             "upper_arm."+side,"forearm."+side,.110,True)
        seam("Skin knee."+side,"GEO_Thigh."+side,"GEO_Shin."+side,
             "thigh."+side,"shin."+side,.070,corrective="JointVolume.Knee."+side)
        seam("Trouser knee."+side,"CLO_TrousersThigh."+side,"CLO_TrousersShin."+side,
             "thigh."+side,"shin."+side,.120,True,corrective="TrouserKneeFold."+side)
        seam("Skin wrist."+side,"GEO_Forearm."+side,"GEO_Hand."+side,
             "forearm."+side,"hand."+side,.025,corrective="JointVolume.Wrist."+side)
        # Attached volumes meet a trunk or a shaped foot rather than a second
        # ring strip. Keep their existing overlap and original contact anchors,
        # with normalized parent transition fields; these are not welded seams.
        for renderer,bone,parent,band,endpoint in (
            ("GEO_Thigh.","thigh.","pelvis",.080,"start"),
            ("CLO_TrousersThigh.","thigh.","pelvis",.095,"start"),
            ("GEO_Shin.","shin.","foot."+side,.045,"end")):
            character_joint_surfaces.blend_attachment(result,rows[renderer+side],bone+side,parent,
                name=renderer+side+" attachment",band_m=band*scale,endpoint=endpoint)
        for stem,sides in (("GEO_Thigh.",8),("CLO_TrousersThigh.",12)):
            obj=rows[stem+side]
            # The hip socket stays seated in the parent volume. Its first
            # strip bends into the thigh, while the lower posterior pelvis
            # blends into the same leg; rotating a rigid cap cannot open it.
            for vertex in obj.data.vertices[:sides]:
                character_joint_surfaces._assign(obj,vertex.index,{"pelvis":.5,"thigh."+side:.5})
            character_joint_surfaces.correct_continuous_joint(result,obj,"pelvis","thigh."+side,
                name=("Skin" if stem.startswith("GEO") else "Trouser")+" hip."+side,
                band_m=(.115 if stem.startswith("GEO") else .140)*scale,
                corrective_name="JointVolume.Hip."+side,ring=list(range(sides)))
    neck=rows["GEO_Neck"]
    for vertex in neck.data.vertices:
        z=(vertex.co.z+neck.location.z)/scale
        def smooth(value):
            t=max(0.,min(1.,value));return t*t*(3-2*t)
        head=smooth((z-1.452)/.053)
        chest=1-smooth((z-1.420)/.038)
        character_joint_surfaces._assign(neck,vertex.index,
            {"head":head,"chest":(1-head)*chest,"neck":(1-head)*(1-chest)})
    character_joint_surfaces._surface(result,neck)
    character_joint_surfaces._data(result)["attachments"].extend([
        {"id":"Neck base","name":neck.name,"bone":"neck","parent_bone":"chest","band_m":.038*scale,"endpoint":"start"},
        {"id":"Neck jaw","name":neck.name,"bone":"neck","parent_bone":"head","band_m":.053*scale,"endpoint":"end"}])
    # One actual waist perimeter is partitioned into anatomical renderers.
    # The pelvis's upper overlap stays buried inside the torso volume.
    ring_a=list(range(4*PELVIS_SIDES,5*PELVIS_SIDES));ring_b=list(range(PELVIS_SIDES))
    for a,b in zip(ring_a,ring_b):
        character_joint_surfaces._assign(rows["GEO_Pelvis"],a,{"pelvis":1.})
        character_joint_surfaces._assign(rows["GEO_Torso"],b,{"pelvis":1.})
    character_joint_surfaces._data(result)["seams"].append({
        "id":"Skin waist","bone":"chest","renderers":["GEO_Pelvis","GEO_Torso"],
        "vertices_a":ring_a,"vertices_b":ring_b,"bones":["pelvis","chest"],
        "points_blender":[dict(zip(("x","y","z"),map(float,v.co+rows["GEO_Torso"].location)))
                           for v in rows["GEO_Torso"].data.vertices[:PELVIS_SIDES]],
        "band_m":.035*scale,"loose":False,"corrective_shape":"","corrective_angle_degrees":0.,"weight_curve":"none"})
    character_joint_surfaces._surface(result,rows["GEO_Torso"])
    jacket=rows["CLO_JacketBody"]
    for side in ("L","R"):
        sleeve=rows["CLO_JacketSleeve."+side];ring=list(sleeve["bp_jacket_armhole_body_ids"])
        points=[jacket.data.vertices[i].co+jacket.location for i in ring]
        fractions=[.18+.42*character_joint_surfaces._smooth((1.455-p.z/scale)/.145) for p in points]
        # Both thickness layers and the neighboring body strip share the
        # armhole's field; the seam remains sewn when the upper arm rises.
        for vertex in jacket.data.vertices:
            point=vertex.co+jacket.location
            nearest=min(range(12),key=lambda j:(point-points[j]).length_squared)
            distance=(point-points[nearest]).length
            if distance<.055*scale:
                mix=fractions[nearest]*character_joint_surfaces._smooth(1-distance/(.055*scale))
                weights={n:w*(1-mix) for n,w in character_joint_surfaces._weights(jacket,vertex.index).items()}
                weights["upper_arm."+side]=mix
                character_joint_surfaces._assign(jacket,vertex.index,weights)
        inner=dict(zip(list(jacket["bp_jacket_armhole."+side]),list(jacket["bp_jacket_armhole_inner."+side])))
        for index,body_index,fraction in zip(range(12),ring,fractions):
            weights={"chest":1-fraction,"upper_arm."+side:fraction}
            character_joint_surfaces._assign(jacket,body_index,weights)
            character_joint_surfaces._assign(jacket,inner[body_index],weights)
            character_joint_surfaces._assign(sleeve,index,weights)
            character_joint_surfaces._assign(sleeve,index+12,{"chest":.10,"upper_arm."+side:.90})
        character_joint_surfaces._data(result)["seams"].append({
            "id":"Jacket shoulder."+side,"bone":"upper_arm."+side,
            "renderers":[jacket.name,sleeve.name],"vertices_a":ring,"vertices_b":list(range(12)),
            "points_blender":[dict(zip(("x","y","z"),map(float,p))) for p in points],
            "bones":["chest","upper_arm."+side],"band_m":.055*scale,"loose":True,
            "corrective_shape":"","corrective_angle_degrees":0.,"weight_curve":"none"})
        character_joint_surfaces._surface(result,jacket);character_joint_surfaces._surface(result,sleeve)
    character_joint_surfaces.reconcile_normals(result)
    _blend_hip_normals(result)


def _blend_hip_normals(result):
    """Smooth the embedded hip overlap without changing the distal seams."""
    from mathutils.bvhtree import BVHTree
    from mathutils.geometry import barycentric_transform
    rows={p.obj.name:p.obj for p in result.parts}
    def normals(obj):
        values=[Vector() for _ in obj.data.vertices];counts=[0]*len(values)
        for loop,normal in zip(obj.data.loops,obj.data.corner_normals):
            values[loop.vertex_index]+=normal.vector;counts[loop.vertex_index]+=1
        return [value.normalized() for value in values]
    for parent_name,stem,sides in (("GEO_Pelvis","GEO_Thigh.",8),("CLO_TrousersPelvis","CLO_TrousersThigh.",12)):
        parent=rows[parent_name];parent.data.calc_loop_triangles()
        points=[v.co+parent.location for v in parent.data.vertices]
        triangles=[tuple(t.vertices) for t in parent.data.loop_triangles]
        tree=BVHTree.FromPolygons(points,triangles,all_triangles=True)
        parent_normals=normals(parent)
        for side in ("L","R"):
            obj=rows[stem+side];values=normals(obj)
            for vertex in obj.data.vertices[:2*sides]:
                point=vertex.co+obj.location;nearest,normal,index,distance=tree.find_nearest(point)
                a,b,c=triangles[index]
                parent_normal=barycentric_transform(nearest,points[a],points[b],points[c],
                    parent_normals[a],parent_normals[b],parent_normals[c]).normalized()
                mix=1. if vertex.index<sides else .35
                values[vertex.index]=(parent_normal*mix+values[vertex.index]*(1-mix)).normalized()
            obj.data.normals_split_custom_set_from_vertices([tuple(n) for n in values])


def manifest(result):
    items=[]
    for slot,covered in BODY_COVERAGE.items():
        names=[p.obj.name for p in result.parts if p.role=="clothing" and p.obj.get("bp_wardrobe_slot")==slot]
        items.append({"id":"hero_"+slot,"slot":slot,"renderers":names,"covered_body_renderers":list(covered)})
    counts={}
    for part in result.parts:
        part.obj.data.calc_loop_triangles(); counts[part.obj.name]=len(part.obj.data.loop_triangles)
    hidden=set(name for values in BODY_COVERAGE.values() for name in values)
    return {**player_boots.manifest(result),"joint_surfaces":character_joint_surfaces.manifest(result),
            "wardrobe":{"contract":"hero_outfit_v1","default_outfit_id":"hero_field_workwear","items":items},
            "body_bone_count":31,"hair_bone_count":len(HAIR_BONES),
            "hair":{"contract":"hero_collar_hair_v1","chains":[{"name":prefix,
                "bones":[prefix+suffix for suffix in (".00",".01",".02",".Tip")],
                "points_blender":[list(p) for p in path],
                "renderers":[p.obj.name for p in result.parts if p.obj.get("bp_hair_chain")==prefix],
                "contact_radius_m":.012} for prefix,path in HAIR_PATHS.items()]},
            "quality":{"worn_triangle_count":sum(c for name,c in counts.items() if name not in hidden),
                "hidden_body_triangle_count":sum(counts[name] for name in hidden),
                "complete_triangle_count":sum(counts.values()),"worn_triangle_budget":[0,8000]},
            "fit":{"torso_contract":"lean_waist_to_shoulder_v1",
                "body_torso_profiles_blender":[list(p) for p in BODY_TORSO_PROFILES],
                "shirt_clearance_m":.004,"shoulder_connector":"tapered_shared_upper_arm_ring",
                "neck_top_width_m":.108,"neck_base_width_m":.148,
                "neck_skin_field":"chest_to_neck_to_head; upper ring fully follows head",
                "pelvis_contract":"male_pelvis_gluteal_v1",
                "pelvis_profiles_blender":[list(p) for p in PELVIS_PROFILES],
                "trousers_pelvis_clearance_m":.008,
                "pelvis_measured":_pelvis_fit(result),
                "measured":_torso_fit(result),
                "hand_size_scale":HAND_SIZE_SCALE,"hand_girth_scale":HAND_GIRTH_SCALE,
                "hand_wrist_blend_m":.030,
                "upper_sleeve_max_radius_m":.060,
                "shoulder_shell_half_width_m":JACKET_PROFILES[6][1],
                "jacket_cut_contract":"m65_flat_panels_set_in_sleeves_v1",
                "jacket_armhole_ring_vertices":12,
                "jacket_hem_nodes_blender":_jacket_hem_nodes(result),
                "jacket":"flat hanging M65 panels, set-in curved armholes, shaped sleeves and long cuffs"},
            "body_coverage":[{"renderer":name,"region":slot} for slot,names in BODY_COVERAGE.items() for name in names]}


def _jacket_hem_nodes(result):
    """Eight open-chain controls lie on the actual authored hem perimeter."""
    obj=next(p.obj for p in result.parts if p.obj.name=="CLO_JacketBody")
    grid=list(obj["bp_jacket_outer_grid"])
    points=[obj.data.vertices[i].co+obj.location for i in grid[:25]]
    nodes=[]
    for index in range(8):
        coordinate=24*index/7; first=min(23,int(coordinate))
        point=points[first].lerp(points[first+1],coordinate-first)
        nodes.append(dict(zip(("x","y","z"),map(float,point))))
    return nodes


def _torso_fit(result):
    """Read dimensions from actual authored rings, rather than copied labels."""
    rows={p.obj.name:p.obj for p in result.parts};obj=rows["GEO_Torso"]
    station=[]
    sides=int(obj.get("bp_torso_sides",10))
    for start in range(0,len(obj.data.vertices)-2,sides):
        points=[v.co+obj.location for v in obj.data.vertices[start:start+sides]]
        station.append({"side_z_m":float(points[0].z),
                        "half_width_m":max(abs(p.x) for p in points),
                        "half_depth_m":(max(p.y for p in points)-min(p.y for p in points))*.5})
    waist=min(station,key=lambda p:abs(p["side_z_m"]-.970))
    chest=min(station,key=lambda p:abs(p["side_z_m"]-1.350))
    jacket=rows["CLO_JacketBody"]
    grid=list(jacket.get("bp_jacket_outer_grid",range(len(jacket.data.vertices)//2)))
    jacket_stations=[]
    for start in range(0,len(grid),25):
        points=[jacket.data.vertices[i].co+jacket.location for i in grid[start:start+25] if i>=0]
        back=jacket.data.vertices[grid[start+12]].co+jacket.location
        jacket_stations.append({"z_m":back.z,
                                "half_width_m":max(abs(p.x) for p in points),
                                "rear_extent_m":back.y})
    return {"body_stations":station,"jacket_stations":jacket_stations,
            "waist_half_width_m":waist["half_width_m"],"upper_ribcage_half_width_m":chest["half_width_m"],
            "upper_ribcage_to_waist_ratio":chest["half_width_m"]/waist["half_width_m"]}


def _pelvis_fit(result):
    rows={p.obj.name:p.obj for p in result.parts};body=rows["GEO_Pelvis"]
    points=[v.co+body.location for v in body.data.vertices[2*PELVIS_SIDES:3*PELVIS_SIDES]]
    left=max((p for p in points if p.x>.015 and p.y>0),key=lambda p:p.y)
    right=max((p for p in points if p.x<-.015 and p.y>0),key=lambda p:p.y)
    return {"hip_half_width_m":max(abs(p.x) for p in points),
        "posterior_extent_m":max(p.y for p in points),
        "central_cleft_depth_m":(left.y+right.y)*.5-points[PELVIS_SIDES//4].y,
        "left_crest_x_m":left.x,"right_crest_x_m":right.x,
        "waistband_half_width_m":max(abs(v.co.x+rows["CLO_TrousersPelvis"].location.x)
                                    for v in rows["CLO_TrousersPelvis"].data.vertices[-PELVIS_SIDES:])}


def _validate_pelvis_fit(result,errors):
    from mathutils.bvhtree import BVHTree
    rows={p.obj.name:p.obj for p in result.parts};data=_pelvis_fit(result)
    if not (.149<data["hip_half_width_m"]<.153 and .128<data["posterior_extent_m"]<.145):
        errors.append("Actual male hips must retain narrow width and modest posterior volume")
    if not (.030<data["central_cleft_depth_m"]<.060 and data["left_crest_x_m"]>.030 and data["right_crest_x_m"]<-.030):
        errors.append("Pelvis must contain two distinct rounded gluteal crests and a central cleft: "+str(data))
    pants=rows["CLO_TrousersPelvis"]
    rings=[[v.co+pants.location for v in pants.data.vertices[start:start+PELVIS_SIDES]]
           for start in range(0,len(pants.data.vertices),PELVIS_SIDES)]
    for vertex in rows["GEO_Pelvis"].data.vertices:
        point=vertex.co+rows["GEO_Pelvis"].location
        a,b=next((a,b) for a,b in zip(rings,rings[1:]) if a[0].z-.00001<=point.z<=b[0].z+.00001)
        t=(point.z-a[0].z)/(b[0].z-a[0].z);j=vertex.index%PELVIS_SIDES
        outer=a[j].lerp(b[j],t);angle=j*math.tau/PELVIS_SIDES
        if (outer-point).dot(Vector((math.cos(angle),math.sin(angle),0)))<.003:
            errors.append("Independent trousers must clear the actual paired pelvis");break
    jacket=rows["CLO_JacketBody"];jacket.data.calc_loop_triangles()
    jacket_tree=BVHTree.FromPolygons([v.co+jacket.location for v in jacket.data.vertices],
        [tuple(t.vertices) for t in jacket.data.loop_triangles],all_triangles=True)
    for obj in (pants,rows["CLO_TrousersThigh.L"],rows["CLO_TrousersThigh.R"]):
        for vertex in obj.data.vertices:
            point=vertex.co+obj.location
            if not .810<point.z<.970 or (point.y<.02 and abs(point.x)<.055):continue
            direction=Vector((0,1 if point.y>.017 else -1,0))
            hit,normal,index,distance=jacket_tree.ray_cast(point,direction,.2)
            if hit is None or distance<.001:
                errors.append("Authored lower jacket must clear trouser hips/thighs before cloth simulation: "+obj.name+" "+str(tuple(point)));break
    for vertex in rows["GEO_Torso"].data.vertices[:PELVIS_SIDES*2]:
        point=vertex.co+rows["GEO_Torso"].location
        if point.z<.940 or point.z>.973:continue
        angle=math.atan2((point.y-.010)/.084,point.x/.142)
        outer=Vector((.142*math.cos(angle),.010+.084*math.sin(angle),point.z))
        if (outer-point).length<.004:
            errors.append("Trouser waistband must cover the actual visible torso without skin holes");break
    for name,sides in (("GEO_Thigh.L",8),("GEO_Thigh.R",8)):
        parent=rows["GEO_Pelvis"];parent.data.calc_loop_triangles()
        tree=BVHTree.FromPolygons([v.co+parent.location for v in parent.data.vertices],
            [tuple(t.vertices) for t in parent.data.loop_triangles],all_triangles=True)
        for vertex in rows[name].data.vertices[:sides]:
            point=vertex.co+rows[name].location;nearest,normal,_,distance=tree.find_nearest(point)
            if (point-nearest).dot(normal)>-.00005:
                errors.append("Hip root cap must be embedded in its actual pelvis: "+name+" "+str(tuple(point))+" signed="+str((point-nearest).dot(normal)));break
    for row in character_joint_surfaces.manifest(result)["seams"]:
        if row["id"].startswith("Trouser knee") and (not row["loose"] or not row["corrective_shape"].startswith("TrouserKneeFold.")):
            errors.append("Independent trousers require their own loose knee field and fabric corrective")
    if rows["CLO_Belt"].get("bp_wardrobe_slot")!="belt" or rows["CLO_BeltBuckle"].get("bp_wardrobe_slot")!="belt":
        errors.append("Military webbing and buckle must remain independent belt garments")


def _validate_torso_fit(result,errors):
    from mathutils.bvhtree import BVHTree
    rows={p.obj.name:p.obj for p in result.parts};data=_torso_fit(result)
    jacket=rows["CLO_JacketBody"]
    for side in ("L","R"):
        outer=list(jacket.get("bp_jacket_armhole."+side,()))
        inner=list(jacket.get("bp_jacket_armhole_inner."+side,()))
        sleeve=rows["CLO_JacketSleeve."+side]
        if len(outer)!=12 or len(inner)!=12 or not sleeve.get("bp_set_in_sleeve"):
            errors.append("M65 sleeve must meet a twelve-point authored curved armhole: "+side)
            continue
        for a,b in zip(outer,inner):
            thickness=(jacket.data.vertices[a].co-jacket.data.vertices[b].co).length
            if thickness<.001 or character_joint_surfaces._weights(jacket,a)!=character_joint_surfaces._weights(jacket,b):
                errors.append("M65 armhole thickness rim must retain matching normalized skin fields: "+side);break
    hanging=[r["rear_extent_m"] for r in data["jacket_stations"] if .804<r["z_m"]<1.361]
    if len(hanging)<5 or any(b>a+.002 for a,b in zip(hanging,hanging[1:])):
        errors.append("Loose coat rear must fall continuously toward the hem without a local gluteal bulge")
    station=[r for r in data["body_stations"] if .969<r["side_z_m"]<1.351]
    if len(station)<3 or any(b["half_width_m"]<=a["half_width_m"] for a,b in zip(station,station[1:])):
        errors.append("Actual lean torso must expand continuously from waist into upper ribcage")
    if not 1.38<data["upper_ribcage_to_waist_ratio"]<1.48:
        errors.append("Actual lean torso must have a restrained waist-to-ribcage taper")
    for name in ("GEO_Torso","CLO_ShirtBody"):
        obj=rows[name]
        for vertex in obj.data.vertices:
            point=vertex.co+obj.location;z=float(point.z)
            if z>=1.453:continue # Sloping collarbone surface meets the open yoke.
            a,b=next((a,b) for a,b in zip(JACKET_PROFILES,JACKET_PROFILES[1:]) if a[0]<=z<=b[0])
            t=(z-a[0])/(b[0]-a[0]);rx=a[1]+(b[1]-a[1])*t-.004
            ry=a[2]+(b[2]-a[2])*t-.004;cy=a[3]+(b[3]-a[3])*t
            if (point.x/rx)**2+((point.y-cy)/ry)**2>=.985:
                errors.append("Independent jacket must clear the actual tapered body/shirt: "+name);break
    torso=rows["GEO_Torso"]
    top=[v.co+torso.location for v in torso.data.vertices[-int(torso.get("bp_torso_sides",10))-2:-2]]
    if max(p.z for p in top)-min(p.z for p in top)<.027:
        errors.append("Collarbone/trapezius must slope into shoulder rather than form a flat torso cap")
    neck=rows["GEO_Neck"]
    if len(neck.data.vertices)!=48 or neck.data.materials[0].name!="MAT_Skin":
        errors.append("Neck needs a tapered four-station skin surface")
    for vertex in neck.data.vertices[-12:]:
        if character_joint_surfaces._weights(neck,vertex.index)!={"head":1.}:
            errors.append("Upper neck ring must follow the jaw completely during head rotation");break
    def enclosure(obj):
        obj.data.calc_loop_triangles()
        return BVHTree.FromPolygons([v.co+obj.location for v in obj.data.vertices],
            [tuple(t.vertices) for t in obj.data.loop_triangles],all_triangles=True)
    head_tree=enclosure(rows["GEO_Head"]);torso_tree=enclosure(torso)
    for label,obj,vertices,tree in (
        ("Neck jaw",neck,neck.data.vertices[-12:],head_tree),
        ("Neck base",neck,neck.data.vertices[:12],torso_tree),
        *(("Shoulder root "+side,rows["GEO_Shoulder."+side],
           rows["GEO_Shoulder."+side].data.vertices[:10],torso_tree) for side in ("L","R"))):
        for vertex in vertices:
            point=vertex.co+obj.location;nearest,normal,_,distance=tree.find_nearest(point)
            if nearest is None or (point-nearest).dot(normal)>-.0001:
                errors.append(label+" must be embedded in its actual parent surface");break


def validate(result, errors):
    character_joint_surfaces.validate(result, errors)
    player_boots.validate(result,errors)
    _validate_torso_fit(result, errors)
    _validate_pelvis_fit(result, errors)
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
