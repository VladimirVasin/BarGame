#!/usr/bin/env python3
"""The ordinary-worker-v1 body and its interchangeable civilian wardrobe.

The NpcHumanV2 bind rig is unchanged. This generator owns StationWorker only;
the village generator continues to own its other residents and action banks.
Run through run-blender.py; --validate-only reconstructs without publishing.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import itertools
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("default_npc_resident", Path(__file__).with_name("build-village-residents-3d-model.py"))
resident = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = resident
spec.loader.exec_module(resident)
base = resident.base
from player_3d_model_common import hex_to_linear_rgba
from atlas_kit import PixelCanvas, read_generated_png

VERSION = "2.0.0"
HAND_SIZE_SCALE = 1.20
HAND_GIRTH_SCALE = .85
HAND_GRIP_SHAPE = "CylindricalGrip"
HAND_GRIP_WORLD_RADIUS = .022
HAND_GRIP_RADIUS = HAND_GRIP_WORLD_RADIUS / (1.78 / 1.75)
MODEL = "StationWorker"
ASSETS = ROOT / "Assets/Resources/VillageLife"
SOURCE = ROOT / "ArtSource/VillageLife"
COLORS = {
    "skin": "AD8C74", "skin_shadow": "9C7B64", "white": "FFFFFF",
    "hair": "64625A", "hair_dark": "4A4841", "seam": "4C4D42",
    "shirt_everyday": "B5AD95", "shirt_work": "7A897E", "shirt_warm": "9A9682",
    "outer_everyday": "706657", "outer_work": "73796B", "outer_warm": "546966",
    "pants_everyday": "494B49", "pants_work": "67695C", "pants_warm": "595B54",
    "boots_everyday": "544639", "boots_work": "494739", "boots_warm": "4B443B",
    "sole": "34342D", "cap_everyday": "655F50", "cap_work": "686D62", "cap_warm": "767764",
    "trim": "858271", "button": "514C40", "scarf": "9D9479", "glove": "77694E", "apron": "8C8974",
}
SLOTS = ("shirt", "outerwear", "trousers", "boots", "headwear", "scarf", "gloves", "apron")
OUTFITS = ("everyday", "work", "warm")
HAIR_COLORS = {"gray":"64625A", "brunette":"423023", "blond":"AE9668"}
HAIR_MASKS = {}


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def publish(path, data, verify):
    if verify:
        if not path.is_file() or path.read_bytes() != data:
            raise RuntimeError("Deterministic asset differs: " + str(path))
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    return hashlib.sha256(data).hexdigest()


def outward(geometry):
    vertices, faces = geometry
    vertices = [Vector(p) for p in vertices]
    volume = sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i + 1]])) / 6
                 for f in faces for i in range(1, len(f) - 1))
    return vertices, [tuple(reversed(f)) for f in faces] if volume < 0 else faces


def tube(points, radii, sides=12, depth=1.):
    """Continuous cross-sections through an elbow/knee, with rounded profiles."""
    points = [Vector(p) for p in points]
    vertices = []
    for i, (p, radius) in enumerate(zip(points, radii)):
        direction = (points[min(i + 1, len(points) - 1)] - points[max(0, i - 1)]).normalized()
        across = (Vector((0, 1, 0)) - direction * direction.y).normalized()
        normal = direction.cross(across).normalized()
        for j in range(sides):
            a = j * math.tau / sides
            vertices.append(p + across * math.cos(a) * radius * depth + normal * math.sin(a) * radius)
    faces = [tuple(reversed(range(sides)))]
    faces += [(r*sides+j, r*sides+(j+1)%sides, (r+1)*sides+(j+1)%sides, (r+1)*sides+j)
              for r in range(len(points)-1) for j in range(sides)]
    faces.append(tuple((len(points)-1)*sides+j for j in range(sides)))
    return outward((vertices, faces))


def cloth_panel(geometry, thickness=.002):
    """Real front, back and bound edges for a thin sewn cloth panel."""
    vertices,faces=outward(geometry);normals=[Vector() for _ in vertices];edges={}
    for face in faces:
        normal=(vertices[face[1]]-vertices[face[0]]).cross(vertices[face[2]]-vertices[face[0]])
        for i in face:normals[i]+=normal
        for a,b in zip(face,face[1:]+face[:1]):
            key=tuple(sorted((a,b)));edges.setdefault(key,[]).append((a,b))
    count=len(vertices)
    inside=[v-n.normalized()*thickness for v,n in zip(vertices,normals)]
    output=list(faces)+[tuple(i+count for i in reversed(face)) for face in faces]
    for occurrences in edges.values():
        if len(occurrences)==1:
            a,b=occurrences[0];output.append((a,a+count,b+count,b))
    return outward((vertices+inside,output))


def atlas(path, verify, face_index=0, hair_id="gray"):
    c = PixelCanvas(512, 512)
    c.rect(0, 0, 512, 512, (255, 255, 255, 255))
    # Three distinct textile regions: woven cotton, brushed wool, padded canvas.
    # The subdued thread, seams and wear are deterministic, not random damage.
    for kind in range(3):
        ox = kind * 128
        for y in range(256):
            for x in range(128):
                noise = (x*13+y*17+kind*19)%9-4
                weave = -7 if ((x + (y%2)) % (3 if kind == 1 else 5) == 0) else 0
                quilt = -18 * math.exp(-((y%51-25)/2.5)**2) if kind == 2 else 0
                wear = 8*math.exp(-((x-40)/33)**2-((y-174)/35)**2)
                n = max(185, min(255, round(241+noise+weave+quilt+wear)))
                c.put(ox+x, y, (n,n,n,255))
        c.line(ox+6, 8, ox+7, 247, (191,193,184,255))
        c.line(ox+9, 8, ox+10, 247, (228,227,214,255))
        c.line(ox+6, 246, ox+121, 246, (196,199,183,255))
        for y in range(14,242,7): c.line(ox+11,y,ox+13,y,(217,216,204,255))
    # Leather below: rubbed toes, lacing recesses and quiet grain.
    for y in range(256,512):
        for x in range(256):
            n = 226+(x*3+y*11)%15
            n += round(13*math.exp(-((x%128-63)/42)**2-((y-450)/40)**2))
            c.put(x,y,(min(255,n),min(255,n),min(255,n),255))
    # A 128 px face: an ordinary older worker, quiet eyes, receding grey hair,
    # fuller cheek and blunt nose. No hero hair, scar, tattoo, slogan or symbol.
    f=PixelCanvas(128,128);hair=PixelCanvas(128,128)
    skin=(173,140,116)
    for y in range(128):
        for x in range(128):
            light=6*math.exp(-((x-54)/42)**2-((y-45)/43)**2)
            shade=12*(abs(x-63.5)/64)**2+4*max(0,(y-103)/25)
            grain=((x*17+y*7)%23==0)-((x*3+y*13)%19==0)
            f.put(x,y,tuple(round(v+light-shade+grain) for v in skin)+(255,))
    for y in ((27,32) if face_index != 3 else (24,29,34)):
        f.line(43,y,84,y-1,(158,126,103,255))
    eye_centres=((41,87),(40,88),(42,86),(40,88))[face_index]
    for side,cx in enumerate(eye_centres):
        y=57+side
        brow=((94,87,71,255),(100,87,67,255),(81,75,65,255),(107,106,94,255))[face_index]
        arch=(2,4,0,3)[face_index];thickness=(2,1,3,2)[face_index]
        hair.line(cx-12,47+side,cx-1,47-arch+side,brow,thickness)
        hair.line(cx-1,47-arch+side,cx+10,47+side,brow,thickness)
        eye_height=(3,3,2,2)[face_index];eye_width=(9,8,10,9)[face_index]
        f.ellipse(cx,y,eye_width,eye_height,(175,167,143,255))
        iris=((99,112,100,255),(110,92,64,255),(90,103,104,255),(119,123,106,255))[face_index]
        f.ellipse(cx,y,3,eye_height,iris);f.line(cx,y-eye_height+1,cx,y+eye_height-1,(48,57,49,255))
        f.put(cx-1,y-1,(196,191,164,255))
        f.line(cx-10,y-3,cx+9,y-2,(113,91,73,255))
        f.line(cx-9,y+7,cx+8,y+6,(148,115,93,255))
        f.line(cx-8,y+10,cx+7,y+9,(163,129,105,255))
        sx=-1 if side==0 else 1
        f.line(cx+sx*12,y+3,cx+sx*16,y+6,(155,120,95,255))
    f.line(59,61,56,78,(151,113,87,255));f.line(69,61,73,78,(183,149,120,255))
    f.ellipse(64,80,10,5,(170,126,101,255))
    f.line(54,81,59,83,(105,75,57,255));f.line(69,83,75,81,(105,75,57,255))
    f.line(59,77,67,77,(193,155,121,255))
    f.line(50,84,46,95,(154,118,94,255));f.line(78,84,82,96,(154,118,94,255))
    f.line(51,100,65,101 if face_index!=1 else 100,(113,76,58,255));f.line(65,101 if face_index!=1 else 100,80,99 if face_index!=3 else 101,(113,76,58,255))
    f.line(55,104,74,104,(170,117,94,255));f.line(57,111,74,110,(154,120,94,255))
    for y in range(99,124):
        for x in range(26,103):
            if (x*7+y*17)%31==0: hair.put(x,y,(145,126,105,255))
    if face_index==1:
        # Fine brown-grey moustache, narrower eyes and a clean chin.
        for x in range(45,84):
            centre=64;top=90+int(abs(x-centre)/12);bottom=96+int(abs(x-centre)/15)
            for y in range(top,bottom):
                if abs(x-64)>2:hair.put(x,y,(106+(x+y)%11,94+(x+y)%10,74+(x+y)%8,255))
        hair.line(45,96,49,94,(115,98,78,255));hair.line(79,94,83,96,(115,98,78,255))
    elif face_index==2:
        # Short, uneven greying beard belongs to an ordinary worker. The
        # lip and cheek notches stay readable rather than a painted mask.
        for y in range(84,126):
            for x in range(24,105):
                edge=84+round(19*math.exp(-((x-64)/23)**2))+(x*7)%3
                if y>=edge and not (49<x<82 and 97<y<105):
                    n=(x*7+y*11)%17
                    hair.put(x,y,(110+n,103+n,87+n,255))
        for x in range(47,82):
            for y in range(91,96):
                if abs(x-64)>2:hair.put(x,y,(94+(x+y)%12,90+(x+y)%12,78+(x+y)%12,255))
    elif face_index==3:
        # Weathered clean-shaven face: paler brows, fine crow's feet and a
        # blunt reddish nose, with no scars or narrative marks.
        f.ellipse(64,80,8,3,(175,123,101,255))
        for side,cx in enumerate(eye_centres):
            sign=-1 if side==0 else 1
            for offset in (0,4,8):f.line(cx+sign*11,60+offset,cx+sign*18,63+offset,(155,120,98,255))
        f.line(38,86,34,99,(153,119,96,255));f.line(91,86,95,99,(153,119,96,255))
        f.line(48,106,45,113,(152,116,94,255));f.line(83,105,86,113,(152,116,94,255))
    # Only explicitly authored hair pixels take the hair palette. Skin,
    # freckles/creases, eye colour and every textile pixel stay identical.
    tint=tuple(int(HAIR_COLORS[hair_id][i:i+2],16) for i in (0,2,4))
    mask=[]
    for i in range(128*128):
        offset=i*4
        if not hair.pixels[offset+3]:continue
        rgb=tuple(hair.pixels[offset:offset+3]);mask.append(i)
        if hair_id!="gray":
            shade=(rgb[0]*.2126+rgb[1]*.7152+rgb[2]*.0722)/98
            rgb=tuple(max(20,min(220,round(v*shade))) for v in tint)
        f.put(i%128,i//128,rgb+(255,))
    HAIR_MASKS[face_index]=set(mask)
    for y in range(128):
        offset=((384+y)*512+384)*4
        c.pixels[offset:offset+512]=f.pixels[y*512:(y+1)*512]
    # Reserved white texels keep skin and trim free from textile noise.
    c.rect(500,0,512,12,(255,255,255,255))
    return publish(path,c.png_bytes(),verify)


class WorkerBuilder(base.PedestrianBuilder):
    def __init__(self):
        super().__init__(None)
        self.items=[]
        self.active_item=None
        self.hand_grip_frames=[]
        self.hand_grip_segments=[]

    def remap_geometry_point(self,point,bone_name,role,name):
        return Vector(point)

    def add(self,name,geometry,bone,color,role="clothing",smooth=True):
        obj=self.add_part(name,outward(geometry),bone,role,color)
        for p in obj.data.polygons: p.use_smooth=smooth and len(p.vertices)==4
        if self.active_item:
            self.active_item["renderers"].append(name)
            obj["bp_wardrobe_item"]=self.active_item["id"]
        return obj

    def item(self,item_id,covered=()):
        item={"id":item_id,"slot":item_id.split(".")[0],"renderers":[],"covered_renderers":list(covered)}
        self.items.append(item);self.active_item=item
        return item

    def weights(self,obj,assignments):
        for group in list(obj.vertex_groups): obj.vertex_groups.remove(group)
        names=sorted({name for assignment in assignments for name in assignment})
        groups={n:obj.vertex_groups.new(name=n) for n in names}
        for i,assignment in enumerate(assignments):
            total=sum(assignment.values())
            for name,w in assignment.items():
                if w>0: groups[name].add([i],w/total,"REPLACE")

    @staticmethod
    def trunk_weight(z):
        if z < 1.10:
            t=max(0,min(1,(z-.94)/.16));return {"pelvis":1-t,"spine":t}
        t=max(0,min(1,(z-1.10)/.22));return {"spine":1-t,"chest":t}

    def trunk(self,name,rows,color,role="clothing",sides=20):
        obj=self.add(name,base.make_vertical_shell(rows,sides),"chest",color,role)
        self.weights(obj,[self.trunk_weight(v.co.z+obj.location.z) for v in obj.data.vertices])
        return obj

    def arm(self,name,side,radii,color,role="clothing",sides=12):
        upper="upper_arm."+side;lower="forearm."+side
        a=Vector(base.BONE_BY_NAME[upper].head);b=Vector(base.BONE_BY_NAME[lower].head);c=Vector(base.BONE_BY_NAME[lower].tail)
        points=[a.lerp(b,t) for t in (-.08,.07,.37,.76,1.)]+[b.lerp(c,t) for t in (.22,.53,.82,1.)]
        # A sewn shoulder is a rounded dome recessed into the torso. Starting
        # a full-radius cylinder at the shoulder produces pointed epaulettes
        # as soon as the A-pose arms hang down.
        radii=(radii[0]*.15,radii[1]*.70)+tuple(radii[2:])
        obj=self.add(name,tube(points,radii,sides,.91),upper,color,role)
        assignments=[]
        for i in range(len(points)):
            lower_weight=max(0,min(1,(i-3)/2))
            chest_weight=.30 if i==0 else .12 if i==1 else 0
            assignments.extend([{upper:(1-lower_weight)*(1-chest_weight),lower:lower_weight,"chest":chest_weight}]*sides)
        self.weights(obj,assignments)
        # Fit the sewn shoulder in its working hanging pose, then inverse
        # skin it back into the unchanged shared bind frame. Contacts and
        # bone matrices remain identical; only the garment's upper dome fits.
        rig=self.result.rig;base.reset_pose(rig);base.apply_pose(rig,base.CITIZEN_HANGING_ARMS);bpy.context.view_layer.update()
        matrices={n:rig.pose.bones[n].matrix@rig.data.bones[n].matrix_local.inverted() for n in (upper,lower,"chest")}
        sign=1 if side=="L" else -1
        for vertex,assignment in zip(obj.data.vertices,assignments):
            row=vertex.index//sides
            if row>2:continue
            matrix=sum((matrices[n]*w for n,w in assignment.items()),Matrix(((0,0,0,0),)*4))
            target=matrix@(vertex.co+obj.location)
            amount=(1.,.65,.20)[row]
            target.x=target.x*(1-.35*amount)+sign*.192*(.35*amount)
            target.z-=.036*amount
            vertex.co=matrix.inverted()@target-obj.location
        base.reset_pose(rig);bpy.context.view_layer.update()
        return obj

    def hands(self,gloves=False):
        for side,sign in (("L",1),("R",-1)):
            bone="hand."+side;a=Vector(base.BONE_BY_NAME[bone].head);end=Vector(base.BONE_BY_NAME[bone].tail)
            direction=(end-a).normalized();across=(Vector((0,1,0))-direction*direction.y).normalized()
            normal=direction.cross(across).normalized()*-sign
            # Match the production hero's local hand basis. The thumb is on
            # the forward edge of the palm, never an outward second finger.
            factor=1.11 if gloves else 1.
            prefix="CLO_Glove" if gloves else "GEO_Hand"
            color="glove" if gloves else "skin"
            centre=a+direction*.063+normal*.030
            if not gloves:
                anchors={}
                for label,point in (("Centre",centre),("Axis",centre-across*.05),("Palm",centre+normal*.05)):
                    name="ANCHOR_HandGrip"+label+"."+side
                    anchor=bpy.data.objects.new(name,None);self.result.export_collection.objects.link(anchor)
                    anchor.parent=self.result.rig;anchor.parent_type="BONE";anchor.parent_bone=bone
                    bpy.context.view_layer.update();anchor.matrix_world=Matrix.Translation(point)
                    self.result.anchors[name]=anchor;anchors[label.lower()+"_anchor"]=name
                self.hand_grip_frames.append({"side":side,"bone":bone,**anchors,
                    "source_centre":list(centre),"source_axis":list(-across),"source_palm":list(normal)})
            def shape(obj,closed):
                obj.shape_key_add(name="Basis");key=obj.shape_key_add(name=HAND_GRIP_SHAPE)
                for vertex,point in zip(key.data,closed):vertex.co=point-obj.location
                key.value=0.
            def local(along,x,z):
                # Enlarge the visible palm/fingers around the fixed wrist,
                # preserving the first ring at the sleeve and blending into
                # the larger hand. Rig, grips and animation frames do not move.
                blend=max(0,min(1,along/.030))
                size=1+(HAND_SIZE_SCALE-1)*blend
                depth=1+(HAND_GIRTH_SCALE-1)*blend
                return a+(direction*along+across*x+normal*z*depth)*size
            stations=((-.011,.021,.013),(.013,.031,.018),(.043,.033,.019),(.063,.026,.015))
            vertices=[];faces=[];n=10
            for along,width,depth in stations:
                for j in range(n):
                    angle=j*math.tau/n;vertices.append(local(along,width*factor*math.cos(angle),depth*factor*math.sin(angle)))
            faces=[tuple(reversed(range(n)))]+[(r*n+j,r*n+(j+1)%n,(r+1)*n+(j+1)%n,(r+1)*n+j) for r in range(3) for j in range(n)]
            faces.append(tuple(30+j for j in range(n)))
            palm=self.add(prefix+"Palm."+side,(vertices,faces),bone,color,"clothing" if gloves else "body")
            # The distal palm cups behind the rim. Its wrist rings and back
            # surface stay put; the contact face is a tangent to the cylinder.
            closed=[]
            for point in vertices:
                along=(point-a).dot(direction);depth=(point-a).dot(normal)
                inset=max(0,depth-(.030-HAND_GRIP_RADIUS-.001)) if along>.038 else 0
                closed.append(point-normal*inset)
            shape(palm,closed)
            for i,(offset,length,radius) in enumerate(((-.023,.035,.0088),(-.007,.044,.0093),(.009,.041,.0088),(.024,.032,.0078))):
                points=[local(.055,offset,0),local(.065+length*.35,offset,.002),local(.055+length,offset,.005)]
                radii=tuple(radius*f*factor*HAND_SIZE_SCALE*HAND_GIRTH_SCALE for f in (1,.96,.70))
                obj=self.add(prefix+"Finger"+str(i)+"."+side,tube(points,radii,7,.9),bone,color,"clothing" if gloves else "body_detail")
                # Preserve both phalanx lengths. Moving the already buried
                # knuckle to the palm edge permits a real wrap without claws.
                ring_radius=HAND_GRIP_RADIUS+radii[0]+.003
                angles=[math.radians(30)]
                for p,q in zip(points,points[1:]):
                    angles.append(angles[-1]+2*math.asin((q-p).length/(2*ring_radius)))
                closed_points=[centre+across*(offset*HAND_SIZE_SCALE)+direction*(ring_radius*math.sin(t))-normal*(ring_radius*math.cos(t)) for t in angles]
                shape(obj,tube(closed_points,radii,7,.9)[0])
                self.hand_grip_segments.append((obj.name,points,closed_points,radii,7))
            points=[local(.018,-.023,0),local(.037,-.037,.002),local(.056,-.041,.004)]
            radii=tuple(r*factor*HAND_SIZE_SCALE*HAND_GIRTH_SCALE for r in (.014,.013,.009))
            obj=self.add(prefix+"Thumb."+side,tube(points,radii,8,.82),bone,color,"clothing" if gloves else "body_detail")
            closed_points=[points[0]]
            for index,(along,width,depth) in enumerate(((.026,-.040,.020),(.033,-.041,.044))):
                wanted=a+direction*along+across*width+normal*depth
                closed_points.append(closed_points[-1]+(wanted-closed_points[-1]).normalized()*(points[index+1]-points[index]).length)
            shape(obj,tube(closed_points,radii,8,.82)[0])
            self.hand_grip_segments.append((obj.name,points,closed_points,radii,8))

    def head(self):
        # A single adjoining face/back surface, not floating eyes on a skull.
        rows=((1.465,.035,.044,-.021),(1.486,.056,.060,-.024),(1.512,.076,.074,-.025),
              (1.541,.092,.084,-.026),(1.570,.102,.093,-.025),(1.598,.105,.095,-.023),
              (1.625,.104,.094,-.020),(1.654,.101,.091,-.015),(1.684,.093,.082,-.008),
              (1.710,.074,.067,-.001),(1.728,.042,.039,.003),(1.735,.005,.006,.005))
        def surface(angles,face):
            vertices=[]
            for r,(z,rx,ry,cy) in enumerate(rows):
                for angle in angles:
                    x=rx*math.sin(angle);y=cy-ry*math.cos(angle)
                    if face:
                        nose=(.004,.002,.008,.023,.029,.017,.004,0,0,0,0,0)[r]
                        y-=nose*max(0,1-abs(math.sin(angle))/.38)**1.5
                        if r in (3,4): y-=.005*math.exp(-((abs(x)-.055)/.025)**2)
                    vertices.append((x,y,z))
            n=len(angles);faces=[(r*n+j,r*n+j+1,(r+1)*n+j+1,(r+1)*n+j) for r in range(len(rows)-1) for j in range(n-1)]
            return vertices,faces
        face_angles=[math.radians(-72+i*144/12) for i in range(13)]
        rear_angles=[math.radians(72+i*216/14) for i in range(15)]
        face=self.add("GEO_FaceSurface",surface(face_angles,True),"head","white","facial_atlas")
        uv=face.data.uv_layers.new(name="UVMap")
        for loop in face.data.loops:
            row=loop.vertex_index//13;col=loop.vertex_index%13
            # Face painting has a rectangular forehead-to-chin layout. The
            # tiny cranial cap above its top fades into scalp instead.
            height=(rows[row][0]-rows[0][0])/(rows[-1][0]-rows[0][0])
            uv.data[loop.index].uv=((385+col/12*126)/512,(1+height*126)/512)
        rear=self.add("GEO_Head",surface(rear_angles,False),"head","skin","body")
        # The face and rear skull share positions and boundary normals. They
        # are separate renderers only for appearance ownership, not two shells
        # whose lighting makes a visible glued-on face seam.
        for obj,count in ((face,13),(rear,15)):
            normals=[]
            for vertex in obj.data.vertices:
                row=vertex.index//count;column=vertex.index%count
                if obj==rear or column in (0,count-1):
                    point=vertex.co+obj.location;z,rx,ry,cy=rows[row]
                    normals.append(Vector((point.x/max(.005,rx),(point.y-cy)/max(.005,ry),(z-1.602)/.15)).normalized())
                else:normals.append(vertex.normal.copy())
            obj.data.normals_split_custom_set_from_vertices(normals)
        self.trunk("GEO_Neck",((1.387,.079,.068,-.002),(1.42,.074,.063,-.010),(1.46,.065,.058,-.018),(1.50,.055,.054,-.022)),"skin","body",16)
        neck=bpy.data.objects["GEO_Neck"]
        self.weights(neck,[{"neck":max(0,min(1,(v.co.z+neck.location.z-1.41)/.05)),"chest":1-max(0,min(1,(v.co.z+neck.location.z-1.41)/.05))} for v in neck.data.vertices])
        for side,sign in (("L",1),("R",-1)):
            self.add("GEO_Ear."+side,base.make_ellipsoid((sign*.105,-.001,1.598),(.020,.022,.037),10,6),"head","skin","body_detail")
            self.add("GEO_EarFold."+side,tube(((sign*.116,-.018,1.618),(sign*.120,-.023,1.606),(sign*.119,-.021,1.588)),(.003,.005,.003),6),"head","skin_shadow","body_detail")
        # Cropped greying hair has receding temples and a shaped back edge.
        angles=[math.radians(68+i*224/18) for i in range(19)]
        vertices=[]
        for r in range(5):
            t=r/4
            for angle in angles:
                lower=1.565+.052*abs(math.cos(angle));z=lower+(1.727-lower)*t
                lo,hi=next((lo,hi) for lo,hi in zip(rows,rows[1:]) if lo[0]<=z<=hi[0])
                s=(z-lo[0])/(hi[0]-lo[0]);rx=lo[1]+(hi[1]-lo[1])*s+.004;ry=lo[2]+(hi[2]-lo[2])*s+.004;cy=lo[3]+(hi[3]-lo[3])*s
                vertices.append((rx*math.sin(angle),cy-ry*math.cos(angle),z))
        faces=[(r*19+j,r*19+j+1,(r+1)*19+j+1,(r+1)*19+j) for r in range(4) for j in range(18)]
        self.add("HAIR_Sides",(vertices,faces),"head","hair","hair")
        self.add("HAIR_Crown",base.make_ellipsoid((0,.015,1.704),(.082,.079,.034),16,5),"head","hair","hair")

    def trousers(self,outfit=None):
        """Joined legs, crotch and waist; separately covered lower cuffs."""
        skin=outfit is None;prefix="GEO_Legs" if skin else "CLO_Trousers_"+outfit
        color="skin" if skin else "pants_"+outfit
        factor=.82 if skin else 1. if outfit=="everyday" else 1.045 if outfit=="work" else 1.065
        sides=16;vertices=[];faces=[];assignments=[];tops={}
        stations=((.255,.096,.053,.061),(.35,.094,.060,.068),(.47,.091,.052,.060),(.55,.089,.059,.067),
                  (.65,.09,.075,.082),(.77,.093,.088,.097),(.865,.094,.090,.100))
        for side,sign in (("L",1),("R",-1)):
            start=len(vertices);cy=-.005 if side=="L" else .013
            for z,cx,rx,ry in stations:
                for j in range(sides):
                    angle=j*math.tau/sides
                    vertices.append((sign*cx+rx*factor*math.cos(angle),cy+ry*factor*math.sin(angle),z))
                    knee=max(0,min(1,(z-.43)/.12));hip=max(0,min(1,(z-.77)/.17))
                    assignments.append({"shin."+side:1-knee,"thigh."+side:knee*(1-hip),"pelvis":knee*hip})
            for r in range(len(stations)-1):
                for j in range(sides):
                    a=start+r*sides+j;b=start+r*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
            faces.append(tuple(reversed([start+j for j in range(sides)])))
            tops[side]=[start+(len(stations)-1)*sides+j for j in range(sides)]
        # Outer arcs traverse both legs; the remaining inside arcs make a
        # genuine crotch bridge, with no floating rectangular pelvis cover.
        boundary=[tops["L"][j%sides] for j in range(10,23)]+[tops["R"][j%sides] for j in range(2,15)]
        li=[tops["L"][j] for j in range(6,11)];ri=[tops["R"][j%sides] for j in range(18,13,-1)]
        for j in range(4): faces.append((li[j],li[j+1],ri[j+1],ri[j]))
        previous=boundary
        angles=[math.atan2((vertices[i][1]-.004)/.105,vertices[i][0]/.185) for i in boundary]
        for z,rx,ry in ((.915,.18,.11),(.965,.174,.109),(1.005,.167,.102)):
            ring=[]
            for angle in angles:
                ring.append(len(vertices));vertices.append((rx*factor*math.cos(angle),.004+ry*factor*math.sin(angle),z));assignments.append({"pelvis":1.})
            for i in range(len(ring)):
                j=(i+1)%len(ring);faces.append((previous[i],previous[j],ring[j],ring[i]))
            previous=ring
        faces.append(tuple(previous))
        obj=self.add(prefix+"Upper",(vertices,faces),"pelvis",color,"body" if skin else "clothing")
        self.weights(obj,assignments)
        for side,sign in (("L",1),("R",-1)):
            cy=-.005 if side=="L" else .013
            rows=((.11,.096,.045,.055),(.18,.096,.05,.058),(.255,.096,.053,.061))
            geo=base.make_vertical_shell(tuple((z,rx*factor,ry*factor,cy) for z,cx,rx,ry in rows),16)
            geo=([(x+sign*.096,y,z) for x,y,z in geo[0]],geo[1])
            self.add(prefix+"Lower."+side,geo,"shin."+side,color,"body" if skin else "clothing")
        if skin:return
        self.add(prefix+"Waistband",base.make_vertical_shell(((.981,.17*factor,.106*factor,.004),(1.009,.168*factor,.105*factor,.004)),20),"pelvis","seam")
        if outfit=="work":
            for side,sign in (("L",1),("R",-1)):
                vertices=[];cy=-.005 if side=="L" else .013
                for z in (.415,.47,.515,.555):
                    lo,hi=next((lo,hi) for lo,hi in zip(stations,stations[1:]) if lo[0]<=z<=hi[0])
                    t=(z-lo[0])/(hi[0]-lo[0]);cx=lo[1]+(hi[1]-lo[1])*t
                    rx=(lo[2]+(hi[2]-lo[2])*t)*factor;ry=(lo[3]+(hi[3]-lo[3])*t)*factor
                    for j in range(7):
                        x=(j-3)*.012;y=cy-ry*math.sqrt(max(.01,1-(x/rx)**2))-.006
                        vertices.append((sign*cx+x,y,z))
                faces=[(r*7+j,r*7+j+1,(r+1)*7+j+1,(r+1)*7+j) for r in range(3) for j in range(6)]
                obj=self.add(prefix+"KneePatch."+side,(vertices,faces),"shin."+side,"trim")
                self.weights(obj,[{"shin."+side:1-max(0,min(1,(v.co.z+obj.location.z-.43)/.12)),"thigh."+side:max(0,min(1,(v.co.z+obj.location.z-.43)/.12))} for v in obj.data.vertices])

    def boots(self,outfit=None):
        skin=outfit is None;color="skin" if skin else "boots_"+outfit
        prefix="GEO_Foot" if skin else "CLO_Boot_"+outfit
        for side,sign in (("L",1),("R",-1)):
            x=sign*.096;cy=-.022 if side=="L" else .014;toe=-.212 if side=="L" else -.18
            # Eight-sided horizontal cross-sections create an actual toe box,
            # instep and ankle. The lower sole stays exactly on the floor.
            rows=((.006,.052,toe-.027,.068),(.026,.059,toe-.031,.073),(.065,.058,toe-.028,.062),
                  (.10,.052,toe+.016,.054),(.138,.043,cy-.052,.048))
            if skin:rows=tuple((z*.60,w*.82,front+.01,rear*.85) for z,w,front,rear in rows)
            vertices=[]
            for z,width,front,rear in rows:
                points=((-width*.65,front),(-width,front+.032),(-width,cy+rear*.55),(-width*.65,cy+rear),
                        (width*.65,cy+rear),(width,cy+rear*.55),(width,front+.032),(width*.65,front))
                vertices.extend((x+xx,yy,z) for xx,yy in points)
            faces=[tuple(reversed(range(8)))]+[(r*8+j,r*8+(j+1)%8,(r+1)*8+(j+1)%8,(r+1)*8+j) for r in range(4) for j in range(8)]
            faces.append(tuple(32+j for j in range(8)))
            self.add(prefix+"Shoe."+side,(vertices,faces),"foot."+side,color,"body" if skin else "clothing")
            if skin:continue
            height={"everyday":.155,"work":.19,"warm":.30}[outfit]
            upper_cy=-.005 if side=="L" else .013
            upper_width=.068 if outfit=="warm" else .061
            upper_depth=.074 if outfit=="warm" else .066
            shaft=base.make_vertical_shell(((.096,.055,.057,cy),(.15,.058,.063,cy),(height,upper_width,upper_depth,upper_cy)),16)
            shaft=([(xx+x,yy,z) for xx,yy,z in shaft[0]],shaft[1])
            obj=self.add(prefix+"Shaft."+side,shaft,"shin."+side,color)
            self.weights(obj,[{"foot."+side:1-max(0,min(1,(v.co.z+obj.location.z-.095)/.09)),"shin."+side:max(0,min(1,(v.co.z+obj.location.z-.095)/.09))} for v in obj.data.vertices])
            sole=([(xx,yy,(z-.006)*.14) for xx,yy,z in vertices],faces)
            self.add(prefix+"Sole."+side,sole,"foot."+side,"sole")
            for i in range(3):
                y=cy-.071-i*.023;z=.112-i*.015
                self.add(prefix+"Lace"+str(i)+"."+side,tube(((x-.031,y,z),(x+.031,y-.007,z)),(.0025,.0025),5),"foot."+side,"trim")

    def shirt(self,outfit):
        prefix="CLO_Shirt_"+outfit;color="shirt_"+outfit
        rows=((.976,.175,.113,.003),(1.035,.175,.113,.001),(1.12,.18,.115,-.001),(1.22,.191,.121,-.004),
              (1.32,.20,.119,-.008),(1.388,.19,.104,-.01),(1.422,.085,.075,-.015))
        self.trunk(prefix+"Body",rows,color)
        for side in ("L","R"):
            self.arm(prefix+"Sleeve."+side,side,(.052,.070,.069,.056,.05,.052,.045,.036,.031),color)
        if outfit=="warm":
            self.add(prefix+"Collar",base.make_vertical_shell(((1.394,.087,.078,-.013),(1.454,.079,.072,-.018)),16),"neck",color)
        else:
            for side,sign in (("L",1),("R",-1)):
                self.add(prefix+"Collar."+side,base.make_tapered_box((sign*.041,-.102,1.361),(sign*.046,-.074,1.441),(.067,.008,0),(.055,.008,0)),"chest",color)
            self.trunk(prefix+"Placket",((1.01,.009,.115,-.002),(1.35,.009,.125,-.004)),color, sides=8)

    def outer(self,outfit):
        prefix="CLO_Outer_"+outfit;color="outer_"+outfit
        if outfit=="everyday":
            rows=((.966,.187,.127,.002),(1.03,.19,.129,.002),(1.16,.203,.139,-.003),(1.28,.215,.143,-.008),(1.390,.205,.123,-.009),(1.434,.102,.093,-.014))
            vertices=[];sides=25
            for row,(z,rx,ry,cy) in enumerate(rows):
                opening=(.055,.055,.075,.25,.49,.59)[row]
                for j in range(sides):
                    angle=-math.pi/2+opening+j*(math.tau-2*opening)/(sides-1)
                    vertices.append((rx*math.cos(angle),cy+ry*math.sin(angle),z))
            faces=[(r*sides+j,r*sides+j+1,(r+1)*sides+j+1,(r+1)*sides+j) for r in range(len(rows)-1) for j in range(sides-1)]
            # The border follows the actual V, so its hem and opening are
            # smooth sewn edges instead of missing rectangular surface cells.
            obj=self.add(prefix+"Vest",cloth_panel((vertices,faces)),"chest",color)
            self.weights(obj,[self.trunk_weight(v.co.z+obj.location.z) for v in obj.data.vertices])
        else:
            width=.213 if outfit=="work" else .225;depth=.142 if outfit=="work" else .151
            rows=((.935,width*.90,depth*.92,.008),(1.01,width*.94,depth,.006),(1.12,width*.94,depth,-.001),
                  (1.23,width,depth,-.005),(1.335,width,depth*.94,-.009),(1.394,.194,depth*.78,-.012),(1.428,.096,.087,-.013))
            self.trunk(prefix+"Body",rows,color, sides=24)
            for side in ("L","R"):
                r=(.058,.079,.078,.064,.060,.061,.053,.043,.036) if outfit=="work" else (.067,.09,.089,.076,.067,.07,.064,.05,.038)
                self.arm(prefix+"Sleeve."+side,side,r,color, sides=14)
        # Pocket flaps and front fasteners are attached to the cloth surface.
        depth=.133 if outfit=="everyday" else .148 if outfit=="work" else .158
        width=.19 if outfit=="everyday" else .21 if outfit=="work" else .222
        for side,sign in (("L",1),("R",-1)):
            for label,z0,z1,extra,tint in (("Pocket",1.035,1.113,.002,color),("PocketFlap",1.095,1.12,.006,"seam")):
                vertices=[]
                for z in (z0,z1):
                    for i in range(5):
                        x=sign*(.073+i*.021);y=-depth*math.sqrt(max(.01,1-(x/width)**2))-.004-extra
                        vertices.append((x,y,z))
                faces=[(i,i+1,i+6,i+5) for i in range(4)]
                self.add(prefix+label+"."+side,(vertices,faces),"spine",tint)
        for i in range(4):
            z=1.015+i*.088
            self.add(prefix+"Button"+str(i),base.make_ellipsoid((.010,-depth-.001,z),(.005,.003,.005),6,3),"spine" if z<1.2 else "chest","button")

    def headwear(self,outfit):
        prefix="CLO_Hat_"+outfit;color="cap_"+outfit
        if outfit=="everyday":
            self.add(prefix+"Crown",base.make_ellipsoid((0,-.021,1.711),(.116,.132,.049),16,6),"head",color)
            self.add(prefix+"Band",base.make_vertical_shell(((1.674,.109,.109,-.009),(1.698,.113,.118,-.012)),16),"head","seam")
            self.add(prefix+"Peak",base.make_ellipsoid((0,-.100,1.677),(.102,.073,.008),16,3),"head",color)
        elif outfit=="work":
            self.add(prefix+"Crown",base.make_ellipsoid((0,.005,1.71),(.11,.112,.066),16,7),"head",color)
            self.add(prefix+"Fold",base.make_vertical_shell(((1.66,.112,.106,-.002),(1.689,.117,.113,-.001),(1.706,.116,.113,0)),16),"head",color)
        else:
            self.add(prefix+"Crown",base.make_ellipsoid((0,.002,1.711),(.116,.12,.065),16,7),"head",color)
            self.add(prefix+"FrontFold",base.make_tapered_box((0,-.118,1.655),(0,-.102,1.71),(.192,.022,0),(.197,.020,0)),"head","trim")
            for side,sign in (("L",1),("R",-1)):
                self.add(prefix+"EarFlap."+side,base.make_ellipsoid((sign*.113,.001,1.601),(.025,.061,.068),12,6),"head",color)

    def accessories(self):
        self.item("scarf.warm",["GEO_Neck"]+[p.obj.name for p in self.result.parts if p.obj.name.startswith("CLO_Shirt_") and "Collar" in p.obj.name])
        self.add("CLO_ScarfNeck",base.make_vertical_shell(((1.391,.113,.111,-.004),(1.428,.113,.105,-.012),(1.47,.088,.083,-.02)),16),"neck","scarf")
        self.trunk("CLO_ScarfTail",((1.075,.041,.014,-.181),(1.205,.046,.015,-.18),(1.34,.049,.014,-.162),(1.429,.038,.014,-.12)),"scarf",sides=8)
        hands=[p.obj.name for p in self.result.parts if p.obj.name.startswith("GEO_Hand")]
        self.item("gloves.work",hands);self.hands(gloves=True)
        self.item("apron.work")
        # The apron is cut over the largest padded jacket; its curved panel
        # has the same torso weights and no skin recolour shortcut.
        vertices=[]
        rows=((.64,.164,-.164),(.82,.183,-.176),(1.0,.185,-.179),(1.15,.146,-.177),(1.31,.124,-.172))
        for z,width,y in rows:
            for j in range(9):
                x=(j-4)/4*width;vertices.append((x,y+.055*(x/width)**2,z))
        faces=[(r*9+j,r*9+j+1,(r+1)*9+j+1,(r+1)*9+j) for r in range(4) for j in range(8)]
        obj=self.add("CLO_ApronPanel",cloth_panel((vertices,faces)),"chest","apron")
        self.weights(obj,[self.trunk_weight(v.co.z+obj.location.z) for v in obj.data.vertices])
        for side,sign in (("L",1),("R",-1)):
            self.add("CLO_ApronStrap."+side,tube(((sign*.104,-.17,1.30),(sign*.073,-.113,1.397),(sign*.055,.07,1.422),(sign*.101,.152,1.27)),(.011,.011,.011,.011),6,.35),"chest","apron")
        vertices=[]
        for z in (.895,1.02):
            for j in range(9):
                x=(j-4)*.026;vertices.append((x,-.183+.055*(x/.185)**2,z))
        self.add("CLO_ApronPocket",(vertices,[(j,j+1,j+10,j+9) for j in range(8)]),"pelvis","apron")

    def uv(self,part):
        obj=part.obj
        if obj.data.uv_layers:return
        mesh=obj.data;layer=mesh.uv_layers.new(name="UVMap")
        points=[v.co+obj.location for v in mesh.vertices]
        low=[min(p[i] for p in points) for i in range(3)];high=[max(p[i] for p in points) for i in range(3)]
        name=obj.name
        region=1 if any(s in name for s in ("warm","Scarf","Hat_work")) else 0
        if name.startswith("CLO_Outer_warm"):region=2
        for loop in mesh.loops:
            p=points[loop.vertex_index];u=(p.x-low[0])/max(.001,high[0]-low[0]);v=(p.z-low[2])/max(.001,high[2]-low[2])
            if part.role in ("body","body_detail","hair"):
                coord=(505/512,505/512)
            elif "Boot" in name:
                coord=((2+u*123)/512,(2+v*250)/512)
            else:coord=((region*128+2+u*123)/512,(258+v*251)/512)
            layer.data[loop.index].uv=coord

    def build(self):
        self.reset_scene();collection=bpy.data.collections.new("EXPORT_DefaultNpc");bpy.context.scene.collection.children.link(collection)
        root=bpy.data.objects.new("ROOT_Player",None);collection.objects.link(root)
        material=self.create_shared_material();rig=self.create_armature(collection,root)
        self.result=base.BuildResult(root,rig,collection,material)
        base.PALETTE.update({k:hex_to_linear_rgba(v) for k,v in COLORS.items()})
        self.head()
        self.trunk("GEO_Torso",((.91,.153,.09,.004),(1.01,.15,.093,.001),(1.12,.161,.098,0),(1.22,.177,.105,-.003),(1.32,.18,.100,-.007),(1.393,.173,.085,-.009),(1.42,.072,.060,-.012)),"skin","body")
        for side in ("L","R"):
            self.arm("GEO_Arm."+side,side,(.042,.061,.057,.043,.039,.045,.035,.026,.023),"skin","body")
        self.hands();self.trousers();self.boots()
        body_names=[p.obj.name for p in self.result.parts]
        for outfit in OUTFITS:
            self.item("shirt."+outfit,["GEO_Torso","GEO_Arm.L","GEO_Arm.R"]);self.shirt(outfit)
        for outfit in OUTFITS:
            covers=[]
            if outfit!="everyday":
                covers=[p.obj.name for p in self.result.parts if p.obj.name.startswith("CLO_Shirt_") and "Collar" not in p.obj.name]
            self.item("outerwear."+outfit,covers);self.outer(outfit)
        for outfit in OUTFITS:
            self.item("trousers."+outfit,[n for n in body_names if n.startswith("GEO_Legs")]);self.trousers(outfit)
        for outfit in OUTFITS:
            covers=[n for n in body_names if n.startswith("GEO_Foot")]
            if outfit=="warm":covers += [p.obj.name for p in self.result.parts if p.obj.name.startswith("CLO_Trousers_") and "Lower." in p.obj.name]
            self.item("boots."+outfit,covers);self.boots(outfit)
        for outfit in OUTFITS:
            self.item("headwear."+outfit,["HAIR_Crown"]);self.headwear(outfit)
        self.accessories();self.active_item=None
        for part in self.result.parts:self.uv(part)
        bpy.context.view_layer.update()
        return self.result


def wardrobe(builder):
    outfits=[]
    for name in OUTFITS:
        items=[slot+"."+name for slot in SLOTS[:5]]
        if name=="work":items += ["gloves.work","apron.work"]
        if name=="warm":items += ["scarf.warm","gloves.work"]
        outfits.append({"id":name,"items":items})
    return {"default_outfit_id":"warm","items":builder.items,"outfits":outfits}


def visible_names(result,wardrobe_data,item_ids):
    all_clothes={n for item in wardrobe_data["items"] for n in item["renderers"]}
    selected=[item for item in wardrobe_data["items"] if item["id"] in item_ids]
    covered={n for item in selected for n in item["covered_renderers"]}
    return ({p.obj.name for p in result.parts}-all_clothes | {n for item in selected for n in item["renderers"]})-covered


def validate(builder,data):
    result=builder.result;parts={p.obj.name:p.obj for p in result.parts}
    if len(parts)!=len(result.parts):raise RuntimeError("Duplicate part names")
    if len({p.data.as_pointer() for p in parts.values()})!=len(parts):raise RuntimeError("Source meshes must not be shared between items")
    expected=base.npc_v2_bone_specs()
    if len(result.rig.data.bones)!=31:raise RuntimeError("NpcHumanV2 bone count differs")
    for b in expected:
        actual=result.rig.data.bones[b.name]
        if (actual.head_local-Vector(b.head)).length>1e-6 or (actual.tail_local-Vector(b.tail)).length>1e-6:
            raise RuntimeError("Bind skeleton changed: "+b.name)
    geometry=[];triangles={}
    for name,obj in parts.items():
        triangles[name]=sum(len(p.vertices)-2 for p in obj.data.polygons)
        for v in obj.data.vertices:
            if abs(sum(g.weight for g in v.groups)-1)>1e-5:raise RuntimeError("Invalid skin weights: "+name)
            if not all(math.isfinite(c) for c in v.co):raise RuntimeError("Nonfinite geometry: "+name)
        if len(obj.data.uv_layers)!=1:raise RuntimeError("One UV channel required: "+name)
        for uv in obj.data.uv_layers[0].data:
            if not all(0<=n<=1 for n in uv.uv):raise RuntimeError("UV outside atlas: "+name)
        geometry.append((name,triangles[name]))
        # Every closed source shell must face outwards. An open facial/hair
        # or stitched overlay uses its explicit front winding instead.
        edges={}
        for polygon in obj.data.polygons:
            face=list(polygon.vertices)
            for a,b in zip(face,face[1:]+face[:1]):
                key=tuple(sorted((a,b)));edges[key]=edges.get(key,0)+1
        if all(count==2 for count in edges.values()):
            vertices=[v.co for v in obj.data.vertices]
            volume=sum(vertices[p.vertices[0]].dot(vertices[p.vertices[i]].cross(vertices[p.vertices[i+1]]))/6 for p in obj.data.polygons for i in range(1,len(p.vertices)-1))
            if volume<=0:raise RuntimeError("Inward or degenerate solid: "+name)
    ids=[item["id"] for item in data["items"]]
    if len(ids)!=len(set(ids)):raise RuntimeError("Duplicate wardrobe IDs")
    for item in data["items"]:
        if item["slot"] not in SLOTS or not item["renderers"]:raise RuntimeError("Invalid wardrobe item")
        if set(item["renderers"]) & set(item["covered_renderers"]):raise RuntimeError("An item hides itself")
        if (set(item["renderers"])|set(item["covered_renderers"]))-set(parts):raise RuntimeError("Missing renderer reference: "+item["id"])
    outfit_triangles={o["id"]:sum(triangles[n] for n in visible_names(result,data,o["items"])) for o in data["outfits"]}
    max_triangles=0;max_items=[]
    choices=[[None]+[i["id"] for i in data["items"] if i["slot"]==slot] for slot in SLOTS]
    for combination in itertools.product(*choices):
        selected=[i for i in combination if i]
        count=sum(triangles[n] for n in visible_names(result,data,selected))
        if count>max_triangles:max_triangles=count;max_items=selected
    if max_triangles>8000:raise RuntimeError(f"Visible wardrobe budget exceeded: {max_triangles} {max_items}")
    return {"outfit_triangle_counts":outfit_triangles,"maximum_visible_triangles":max_triangles,
            "maximum_visible_items":max_items,"unchanged_bind_bones":31,"unique_mesh_data":True,
            "normalized_skin_weights":True,"outward_solid_winding":True,"all_combinations_measured":True,
            "cylindrical_grip":validate_grip(builder)}


def hand_grip_manifest(builder):
    hands=[]
    for frame in builder.hand_grip_frames:
        names=[p.obj.name for p in builder.result.parts if p.obj.name.startswith(("GEO_Hand","CLO_Glove")) and p.obj.name.endswith("."+frame["side"])]
        hands.append({**frame,"renderers":names})
    return {"shape_name":HAND_GRIP_SHAPE,"cylinder_radius_m":HAND_GRIP_WORLD_RADIUS,
            "source_cylinder_radius_m":HAND_GRIP_RADIUS,"hands":hands,
            "shape_signature":digest([(p.obj.name,[[round(float(c),7) for c in v.co] for v in p.obj.data.shape_keys.key_blocks[HAND_GRIP_SHAPE].data])
                for p in builder.result.parts if p.obj.data.shape_keys])}


def validate_grip(builder):
    def segment_distance(p,q):
        edge=q-p;t=max(0,min(1,-p.dot(edge)/edge.length_squared)) if edge.length_squared>1e-14 else 0
        return (p+edge*t).length
    def distance(triangle):
        p,q,r=triangle
        def cross(a,b):return a.x*b.y-a.y*b.x
        signs=[cross(p,q),cross(q,r),cross(r,p)]
        if abs(cross(q-p,r-p))>1e-12 and (min(signs)>=0 or max(signs)<=0):return 0.
        return min(segment_distance(p,q),segment_distance(q,r),segment_distance(r,p))
    clearances={};segment_error=0.;radius_error=0.;parts={p.obj.name:p.obj for p in builder.result.parts}
    for name,neutral,closed,radii,sides in builder.hand_grip_segments:
        for i in range(2):segment_error=max(segment_error,abs((neutral[i+1]-neutral[i]).length-(closed[i+1]-closed[i]).length))
        obj=parts[name];keys=obj.data.shape_keys.key_blocks
        for i in range(3):
            before=max((keys["Basis"].data[i*sides+j].co+obj.location-neutral[i]).length for j in range(sides))
            after=max((keys[HAND_GRIP_SHAPE].data[i*sides+j].co+obj.location-closed[i]).length for j in range(sides))
            radius_error=max(radius_error,abs(before-after))
    for frame in builder.hand_grip_frames:
        centre=Vector(frame["source_centre"]);axis=Vector(frame["source_axis"]);normal=Vector(frame["source_palm"])
        direction=Vector(base.BONE_BY_NAME[frame["bone"]].tail)-Vector(base.BONE_BY_NAME[frame["bone"]].head);direction.normalize()
        for label,wanted in (("centre",centre),("axis",centre+axis*.05),("palm",centre+normal*.05)):
            anchor=builder.result.anchors[frame[label+"_anchor"]]
            if (anchor.matrix_world.translation-wanted).length>1e-5:raise RuntimeError("Grip anchor frame drift: "+anchor.name)
        for name,obj in parts.items():
            if not name.startswith(("GEO_Hand","CLO_Glove")) or not name.endswith("."+frame["side"]):continue
            keys=obj.data.shape_keys.key_blocks
            if len(keys)!=2 or HAND_GRIP_SHAPE not in keys:raise RuntimeError("Missing authored grip: "+name)
            if any((v.co-keys["Basis"].data[v.index].co).length>1e-8 for v in obj.data.vertices):raise RuntimeError("Grip changed neutral mesh")
            vertices=[v.co+obj.location for v in keys[HAND_GRIP_SHAPE].data]
            volume=sum(vertices[p.vertices[0]].dot(vertices[p.vertices[i]].cross(vertices[p.vertices[i+1]]))/6 for p in obj.data.polygons for i in range(1,len(p.vertices)-1))
            if volume<=0:raise RuntimeError("Grip solid winding flipped: "+name)
            projected=[Vector(((v-centre).dot(direction),(v-centre).dot(normal))) for v in vertices]
            gap=min(distance([projected[p.vertices[j]] for j in (0,i,i+1)]) for p in obj.data.polygons for i in range(1,len(p.vertices)-1))-HAND_GRIP_RADIUS
            clearances[name]=round(gap*(1.78/1.75),6)
            if gap*(1.78/1.75)<-.0025 or gap*(1.78/1.75)>.008:
                raise RuntimeError(f"Grip surface misses cylinder: {name} {gap*(1.78/1.75):.6f}m")
            if "Palm" in name and any((keys[HAND_GRIP_SHAPE].data[i].co-keys["Basis"].data[i].co).length>1e-8 for i in range(20)):
                raise RuntimeError("Grip moved wrist/palm base: "+name)
        for name,neutral,closed,radii,sides in builder.hand_grip_segments:
            if not name.endswith("."+frame["side"]):continue
            if (closed[-1]-centre).dot(normal)<=HAND_GRIP_RADIUS*.20:raise RuntimeError("Finger does not curl onto far side: "+name)
            radial=(closed[-1]-centre).dot(direction)
            if ("Thumb" in name and radial>=-.01) or ("Finger" in name and radial<=.01):raise RuntimeError("Grip thumb is not opposed: "+name)
    if segment_error>1e-6 or radius_error>1e-6:raise RuntimeError("Grip stretches finger lengths or girth")
    return {"surface_clearances_m":clearances,"maximum_segment_length_error_m":round(segment_error,8),
            "maximum_cross_section_radius_error_m":round(radius_error,8),"wrist_unchanged":True,"opposed_thumb":True}


def export_model(path,result):
    # Applying modifiers during FBX export discards authored shape keys.
    # The armature remains a normal skin binding; all neutral vertices stay
    # identical and the shared skeleton/export axes are unchanged.
    base.select_export_objects(result)
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"EMPTY","ARMATURE","MESH"},
        axis_forward="-Z",axis_up="Y",add_leaf_bones=False,bake_anim=False,use_armature_deform_only=False,
        use_mesh_modifiers=False,mesh_smooth_type="FACE",use_custom_props=True)


def previews(result,data,texture,face_data):
    # One consistent neutral studio is used for every outfit and close-up.
    mat=result.material;mat.use_nodes=True;nodes=mat.node_tree.nodes
    shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    info=nodes.new("ShaderNodeObjectInfo");tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(texture));tex.interpolation="Closest"
    mix=nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY";mix.inputs[0].default_value=1
    mat.node_tree.links.new(info.outputs["Color"],mix.inputs[1]);mat.node_tree.links.new(tex.outputs["Color"],mix.inputs[2]);mat.node_tree.links.new(mix.outputs[0],shader.inputs["Base Color"])
    shader.inputs["Roughness"].default_value=.88
    scene=bpy.context.scene;scene.render.engine="BLENDER_EEVEE";scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("DefaultNpcReview");scene.world.color=(.19,.20,.20)
    scene.view_settings.view_transform="Standard";scene.view_settings.look="None"
    for location,power,size in (((-3,-4,5),650,5),((3,-1,3),350,4),((0,3,3),600,3)):
        light=bpy.data.lights.new("ReviewSoftbox","AREA");light.energy=power;light.shape="DISK";light.size=size
        ob=bpy.data.objects.new("ReviewSoftbox",light);scene.collection.objects.link(ob);ob.location=location;ob.rotation_euler=(Vector((0,0,1))-ob.location).to_track_quat("-Z","Y").to_euler()
    camera=bpy.data.cameras.new("ReviewCamera");cam=bpy.data.objects.new("ReviewCamera",camera);scene.collection.objects.link(cam);camera.type="ORTHO";scene.camera=cam
    combinations={o["id"]:o["items"] for o in data["outfits"]}
    combinations["mixed"]=["shirt.warm","outerwear.everyday","trousers.work","boots.everyday","headwear.work","scarf.warm"]
    for label,items in combinations.items():
        visible=visible_names(result,data,items)
        for part in result.parts:part.obj.hide_render=part.obj.name not in visible
        base.reset_pose(result.rig);base.apply_pose(result.rig,base.CITIZEN_HANGING_ARMS)
        views=[("Front",(0,-4,1.0),(0,0,.9),2.0),("ThreeQuarter",(2.8,-5,2.1),(0,0,.9),2.0)]
        if label=="everyday":views += [("Head",(.9,-2.7,1.70),(0,-.035,1.60),.47),("Hands",(1.8,-.6,.93),(.275,-.02,.88),.34)]
        for suffix,location,focus,size in views:
            cam.location=location;cam.rotation_euler=(Vector(focus)-cam.location).to_track_quat("-Z","Y").to_euler();camera.ortho_scale=size
            scene.render.resolution_x=700;scene.render.resolution_y=900 if size>1 else 700
            scene.render.filepath=str(SOURCE/(MODEL+"_"+label+suffix+".png"));bpy.ops.render.render(write_still=True)
            if label=="warm" and suffix=="Front":
                # Preserve the existing curated preview path used by village
                # source reviews; it always shows the current default outfit.
                (SOURCE/(MODEL+".png")).write_bytes(Path(scene.render.filepath).read_bytes())
        if label=="work":
            base.reset_pose(result.rig);base.apply_pose(result.rig,resident.action_pose("StationWork",.45));bpy.context.view_layer.update()
            cam.location=(2.5,-4,1.9);cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat("-Z","Y").to_euler();camera.ortho_scale=2.
            scene.render.resolution_x=700;scene.render.resolution_y=900;scene.render.filepath=str(SOURCE/(MODEL+"_WorkPose.png"));bpy.ops.render.render(write_still=True)
    visible=visible_names(result,data,combinations["everyday"])
    for part in result.parts:part.obj.hide_render=part.obj.name not in visible
    base.reset_pose(result.rig);base.apply_pose(result.rig,base.CITIZEN_HANGING_ARMS)
    cam.location=(.9,-2.7,1.70);cam.rotation_euler=(Vector((0,-.035,1.60))-cam.location).to_track_quat("-Z","Y").to_euler();camera.ortho_scale=.47
    scene.render.resolution_x=700;scene.render.resolution_y=700
    for face in face_data["items"]:
        tex.image=bpy.data.images.load(str(ASSETS/face["texture"]))
        scene.render.filepath=str(SOURCE/(MODEL+"_"+face["id"]+".png"));bpy.ops.render.render(write_still=True)
    # A bare-headed bearded face makes both the mesh hair and painted facial
    # hair visible in the same pose, so colour agreement is reviewable.
    visible=visible_names(result,data,[i for i in combinations["everyday"] if not i.startswith("headwear.")])
    for part in result.parts:part.obj.hide_render=part.obj.name not in visible
    bearded=face_data["items"][2]
    for hair_id,color in HAIR_COLORS.items():
        key="texture" if hair_id=="gray" else hair_id+"_texture"
        tex.image=bpy.data.images.load(str(ASSETS/bearded[key]))
        for part in result.parts:
            if part.obj.name.startswith("HAIR_"):part.obj.color=hex_to_linear_rgba(color)
        scene.render.filepath=str(SOURCE/(MODEL+"_hair-"+hair_id+".png"));bpy.ops.render.render(write_still=True)
    for part in result.parts:
        if part.obj.name.startswith("HAIR_"):part.obj.color=part.color
    tex.image=bpy.data.images.load(str(texture))
    base.reset_pose(result.rig)
    visible=visible_names(result,data,next(o["items"] for o in data["outfits"] if o["id"]=="warm"))
    for part in result.parts:part.obj.hide_render=part.obj.name not in visible


def grip_previews(result,data,texture):
    mat=result.material;mat.use_nodes=True;nodes=mat.node_tree.nodes
    shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    info=nodes.new("ShaderNodeObjectInfo");tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(texture));tex.interpolation="Closest"
    mix=nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY";mix.inputs[0].default_value=1
    mat.node_tree.links.new(info.outputs["Color"],mix.inputs[1]);mat.node_tree.links.new(tex.outputs["Color"],mix.inputs[2]);mat.node_tree.links.new(mix.outputs[0],shader.inputs["Base Color"])
    shader.inputs["Roughness"].default_value=.88
    scene=bpy.context.scene;scene.render.engine="BLENDER_EEVEE";scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("GripReview");scene.world.color=(.16,.17,.18)
    scene.view_settings.view_transform="Standard";scene.view_settings.look="None"
    base.reset_pose(result.rig);base.apply_pose(result.rig,base.CITIZEN_HANGING_ARMS);bpy.context.view_layer.update()
    centre=result.anchors["ANCHOR_HandGripCentre.L"].matrix_world.translation
    axis=(result.anchors["ANCHOR_HandGripAxis.L"].matrix_world.translation-centre).normalized()
    normal=(result.anchors["ANCHOR_HandGripPalm.L"].matrix_world.translation-centre).normalized()
    bone=result.rig.pose.bones["hand.L"];direction=(bone.tail-bone.head).normalized()
    for location,power,size in ((centre+normal*1.2-axis*.6+Vector((0,0,1)),110,1),
                                (centre-normal*.7+axis*.5+Vector((0,0,.5)),80,.7)):
        light=bpy.data.lights.new("GripReviewLight","AREA");light.energy=power;light.shape="DISK";light.size=size
        obj=bpy.data.objects.new(light.name,light);scene.collection.objects.link(obj);obj.location=location;obj.rotation_euler=(centre-obj.location).to_track_quat("-Z","Y").to_euler()
    mesh=bpy.data.meshes.new("GripReviewCylinderMesh");vertices,faces=base.make_frustum_between(centre-axis*.09,centre+axis*.09,HAND_GRIP_RADIUS,HAND_GRIP_RADIUS,32,1.)
    mesh.from_pydata(vertices,[],faces);mesh.update();cylinder=bpy.data.objects.new("GripReviewCylinder",mesh);scene.collection.objects.link(cylinder)
    rubber=bpy.data.materials.new("GripReviewRubber");rubber.diffuse_color=(.045,.052,.058,1);cylinder.data.materials.append(rubber)
    camera=bpy.data.cameras.new("GripReviewCamera");cam=bpy.data.objects.new(camera.name,camera);scene.collection.objects.link(cam);camera.type="ORTHO";camera.ortho_scale=.245;scene.camera=cam
    scene.render.resolution_x=900;scene.render.resolution_y=800
    for part in result.parts:
        if part.obj.data.shape_keys:part.obj.data.shape_keys.key_blocks[HAND_GRIP_SHAPE].value=1.
    for label,extra in (("Bare",[]),("Glove",["gloves.work"])):
        items=next(o["items"] for o in data["outfits"] if o["id"]=="everyday")+extra
        visible=visible_names(result,data,items)
        for part in result.parts:part.obj.hide_render=part.obj.name not in visible
        for suffix,offset in (("Front",normal*.25+axis*.16-direction*.08),("Profile",-axis*.25+normal*.09-direction*.035)):
            cam.location=centre+offset;cam.rotation_euler=(-offset).to_track_quat("-Z","Y").to_euler()
            scene.render.filepath=str(SOURCE/(MODEL+"_Grip"+label+suffix+".png"));bpy.ops.render.render(write_still=True)
    for part in result.parts:
        if part.obj.data.shape_keys:part.obj.data.shape_keys.key_blocks[HAND_GRIP_SHAPE].value=0.
    cylinder.hide_render=True;base.reset_pose(result.rig)
    visible=visible_names(result,data,next(o["items"] for o in data["outfits"] if o["id"]=="warm"))
    for part in result.parts:part.obj.hide_render=part.obj.name not in visible


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true");parser.add_argument("--grip-preview-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    builder=WorkerBuilder();result=builder.build();data=wardrobe(builder);checks=validate(builder,data)
    texture=ASSETS/(MODEL+"Atlas.png");texture_hash=atlas(texture,args.validate_only)
    face_data={"default_face_id":"face-01","items":[]}
    face_hashes=[]
    for i in range(4):
        name=MODEL+("" if i==0 else "Face"+str(i+1).zfill(2))+"Atlas.png"
        hashed=texture_hash if i==0 else atlas(ASSETS/name,args.validate_only,i)
        face={"id":"face-"+str(i+1).zfill(2),"texture":name}
        for hair_id in ("brunette","blond"):
            variant=MODEL+"Face"+str(i+1).zfill(2)+hair_id.title()+"Atlas.png"
            face[hair_id+"_texture"]=variant
            atlas(ASSETS/variant,args.validate_only,i,hair_id)
        face_data["items"].append(face)
        face_hashes.append(hashed)
    if len(set(face_hashes))!=4:raise RuntimeError("All four authored faces must differ")
    reference=read_generated_png(texture)[2]
    for face in face_data["items"][1:]:
        width,height,pixels=read_generated_png(ASSETS/face["texture"])
        if (width,height)!=(512,512):raise RuntimeError("Face atlas dimensions differ")
        for y in range(512):
            end=384 if y>=384 else 512
            start=y*512*4
            if pixels[start:start+end*4]!=reference[start:start+end*4]:
                raise RuntimeError("Face variant changes clothing or body pixels")
    hair_hashes=[]
    for index,face in enumerate(face_data["items"]):
        reference=read_generated_png(ASSETS/face["texture"])[2]
        for hair_id in ("brunette","blond"):
            path=ASSETS/face[hair_id+"_texture"];width,height,pixels=read_generated_png(path)
            if (width,height)!=(512,512):raise RuntimeError("Hair atlas dimensions differ")
            changed=0
            for i in range(512*512):
                if pixels[i*4:i*4+4]==reference[i*4:i*4+4]:continue
                x=i%512-384;y=i//512-384
                if not 0<=x<128 or not 0<=y<128 or y*128+x not in HAIR_MASKS[index]:
                    raise RuntimeError("Hair palette changes a non-hair pixel")
                changed+=1
            if not changed:raise RuntimeError("Hair palette must visibly differ")
            hair_hashes.append({"face_id":face["id"],"hair_id":hair_id,"sha256":hashlib.sha256(path.read_bytes()).hexdigest()})
    metrics=resident.measured(result)
    payload={"generator":Path(__file__).name,"version":VERSION,"role":MODEL,"catalog_model_id":"ordinary-worker-v1","anatomy_standard":"NpcHumanV2",
             "height_scale":1.78/1.75,**metrics,"atlas_sha256":texture_hash,"atlas_size":[512,512],"palette_encoding":"linear",
             "parts":[{"name":p.obj.name,"color":list(p.color)} for p in result.parts],"wardrobe":data,"faces":face_data,"face_atlas_sha256":face_hashes,
             "geometry_signature":resident.geometry_signature(result),"validation":checks}
    payload["hair_colors"]=[{"id":name,"color":list(hex_to_linear_rgba(color))} for name,color in HAIR_COLORS.items()]
    payload["hand_size_scale"]=HAND_SIZE_SCALE
    payload["hand_girth_scale"]=HAND_GIRTH_SCALE
    payload["hand_grip"]=hand_grip_manifest(builder)
    payload["hair_atlas_sha256"]=hair_hashes
    payload["uv_signature"]=digest([(p.obj.name,[[round(float(v),7) for v in uv.uv] for uv in p.obj.data.uv_layers[0].data]) for p in result.parts])
    payload["signature"]=digest(payload)
    publish(ASSETS/(MODEL+".json"),(json.dumps(payload,indent=2)+"\n").encode(),args.validate_only)
    if not args.validate_only:
        export_model(ASSETS/(MODEL+".fbx"),result)
        if not args.no_preview:
            if not args.grip_preview_only:previews(result,data,texture,face_data)
            grip_previews(result,data,texture)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(MODEL+".blend")))
    print(json.dumps({"model":MODEL,**metrics,**checks,"validated_only":args.validate_only}),flush=True)


if __name__=="__main__":main()
