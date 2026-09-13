#!/usr/bin/env python3
"""The cannery's adult seamer: authored silhouette, painted face, canonical rig.

Geometry is authored directly in the NpcHumanV2 metre rest pose. The shared
Blender builder/exporter and PixelCanvas are the production hero pipeline;
expressions remain drawings on one continuous curved cranial surface.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
from dataclasses import replace
from collections import Counter

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).parent))
spec = importlib.util.spec_from_file_location("cannery_resident_base", Path(__file__).with_name("build-village-residents-3d-model.py"))
resident = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = resident
spec.loader.exec_module(resident)
base = resident.base
from player_3d_model_common import hex_to_linear_rgba
from atlas_kit import PixelCanvas

VERSION = "1.0.0"
FPS = 24
FACE_NAME = "GEO_FaceSurface"
CLIPS = (("CanneryWomanIdle", 8., True), ("CanneryWomanWalk", 1.375, True),
         ("CanneryWomanWork", 6., True), ("CanneryWomanListen", 5., True),
         ("CanneryWomanBreak", 11., True))
COLORS = {"skin":"B79A87", "jacket":"596F78", "jacket_dark":"485C65",
          "jacket_edge":"72858A", "trousers":"51585C", "shirt":"C3C1AE",
          "longsleeve":"C7C4B8", "denim":"3C4E65", "belt":"554234", "buckle":"858276",
          "kerchief":"667F88",
          "hair":"6A4432", "hair_light":"8C6044", "hair_dark":"4A3029",
          "shoe":"403D38", "sole":"292A27", "button":"77796E", "white":"FFFFFF"}
SKIN = (183, 154, 135, 255)
HAIR_PATHS = {
    "HairBack":((0,.060,1.681),(0,.156,1.480),(0,.158,1.300),(.004,.145,1.115)),
    "HairLeft":((.070,.008,1.680),(.114,.077,1.486),(.119,.117,1.294),(.085,.119,1.154)),
    "HairRight":((-.071,.014,1.678),(-.115,.083,1.474),(-.123,.120,1.283),(-.088,.116,1.128)),
}


def woman_skeleton():
    """A woman's measured rest anatomy, retaining every body bone identity.

    Animation export owns this rig's rest translations; copying the hero's
    shoulder envelope would silently bring back the masculine silhouette.
    """
    updated=[]
    for bone in base.SKELETON:
        def point(p):
            x,y,z=p
            if abs(x)>.15 and z>1.0:
                if abs(x)<.25:return (x*.85,y,z-.024)
                return (x*.923,y,z-.015)
            if bone.name.startswith(("thigh.","shin.","foot.")):
                if z>.80:return (x*1.14,y,z)
                return (x*.89,y,z)
            return p
        updated.append(replace(bone,head=point(bone.head),tail=point(bone.tail)))
    for prefix,path in HAIR_PATHS.items():
        for i in range(3):updated.append(base.BoneSpec(prefix+f".{i:02}",path[i],path[i+1],"head" if i==0 else prefix+f".{i-1:02}"))
        last=Vector(path[-1]);tip=last+(last-Vector(path[-2])).normalized()*.035
        updated.append(base.BoneSpec(prefix+".Tip",tuple(last),tuple(tip),prefix+".02",deform=False))
    return tuple(updated)


base.SKELETON=woman_skeleton()
base.BONE_BY_NAME={bone.name:bone for bone in base.SKELETON}


def hash_json(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def outward_box(center,size):
    vertices,faces=base.make_box(center,size)
    return vertices,[tuple(reversed(face)) for face in faces]


def waist_factor(z):
    return 1-.16*max(0,1-abs(z-1.105)/.14)


def cloth_front_depth(z):
    """An explicit rounded side profile, with the fuller part below its upper slope."""
    stations=((1.055,.084),(1.105,.089),(1.160,.100),(1.185,.118),
              (1.210,.148),(1.240,.165),(1.265,.169),(1.290,.158),
              (1.320,.136),(1.350,.109),(1.380,.089),(1.405,.074),
              (1.430,.062),(1.441,.057))
    for i,(a,b) in enumerate(zip(stations,stations[1:])):
        if a[0]<=z<=b[0]:
            slope=(b[1]-a[1])/(b[0]-a[0])
            before=(a[1]-stations[i-1][1])/(a[0]-stations[i-1][0]) if i else slope
            after=(stations[i+2][1]-b[1])/(stations[i+2][0]-b[0]) if i+2<len(stations) else slope
            tangent0=2*before*slope/(before+slope) if before*slope>0 else 0.
            tangent1=2*after*slope/(after+slope) if after*slope>0 else 0.
            t=(z-a[0])/(b[0]-a[0]);span=b[0]-a[0]
            return (2*t**3-3*t*t+1)*a[1]+(t**3-2*t*t+t)*span*tangent0+(-2*t**3+3*t*t)*b[1]+(t**3-t*t)*span*tangent1
    return stations[0][1] if z<stations[0][0] else stations[-1][1]


def publish_bytes(path, data, validate_only):
    if validate_only:
        if not path.is_file() or path.read_bytes() != data:
            raise RuntimeError("Deterministic asset differs: " + str(path))
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    return hashlib.sha256(data).hexdigest()


def face_tile(expression=0, mouth=0, warmth=0):
    """A restrained adult face; no cosmetic outline, lashes or makeup mask.

    Pixel clusters model the cheek planes, warm amber-brown eyes, slight brow
    asymmetry and the tiny unequal lift at the corners of a rare smile.
    """
    c = PixelCanvas(64, 64)
    for y in range(64):
        for x in range(64):
            side = abs(x - 31.5) / 31.5
            # Broad painted planes with sparse stable dither, never random skin.
            light = 4 * math.exp(-((x-27)/19)**2 - ((y-24)/35)**2)
            shade = 5 * max(0, (y-52)/12) * (1-side)**2
            blush = 4 * math.exp(-((abs(x-32)-17)/8)**2-((y-37)/6)**2)
            dither = (1 if (x*11+y*7)%9==0 else 0) - (1 if (x*7+y*13)%17==0 else 0)
            c.put(x, y, SKIN if x<3 or x>60 else (round(183+light-shade+blush+dither), round(154+light-shade+dither), round(135+light-shade+dither),255))
    # Warm edge of a natural swept hairline, deliberately above the brows.
    for x in range(4,60):
        y = 3 + round(abs(x-26)*.13)
        c.line(x,0,x,y,(97,61,45,255))
        if x%4==0:c.put(x,y+1,(130,82,55,255))
    for side, center in enumerate((20,43)):
        brow=(77,49,38,255); lid=(79,60,50,255)
        eyebrow_y = 19 + side
        if expression==3:eyebrow_y-=1
        if expression==4:eyebrow_y+=1 if side==0 else 0
        c.line(center-7,eyebrow_y+1,center-2,eyebrow_y-1,brow)
        c.line(center-2,eyebrow_y-1,center+5,eyebrow_y,brow)
        c.line(center-5,eyebrow_y+1,center+4,eyebrow_y+1,(111,82,62,255))
        eye_y=27+side
        if expression==2:
            c.line(center-6,eye_y+1,center,eye_y+2,lid)
            c.line(center,eye_y+2,center+6,eye_y,lid)
            continue
        height=1 if expression==1 else 2
        if expression==4:height=2
        c.ellipse(center,eye_y+1,6,height,(191,183,164,255))
        c.ellipse(center,eye_y+1,2,height,(135,96,51,255))
        c.line(center,eye_y,center,eye_y+height,(51,39,27,255))
        c.put(center-1,eye_y,(218,193,139,255))
        c.line(center-6,eye_y,center-2,eye_y-2 if height==3 else eye_y-1,lid)
        c.line(center-2,eye_y-2 if height==3 else eye_y-1,center+5,eye_y-1,lid)
        c.put(center+6,eye_y,(111,81,64,255))
        c.line(center-4,eye_y+4,center+4,eye_y+4,(159,128,111,255))
        if warmth:c.line(center-5,eye_y+5,center+3,eye_y+5,(173,138,116,255))
    # Nose is painted, not a separate mesh or animated facial bone.
    c.line(32,29,30,37,(171,137,116,255))
    c.line(30,37,31,39,(159,122,103,255))
    c.line(33,32,34,38,(194,164,140,255))
    c.line(29,40,31,41,(139,103,87,255))
    c.line(34,40,35,40,(149,111,92,255))
    c.line(31,41,34,41,(176,135,114,255))
    c.put(32,45,(163,123,106,255))
    lip=(153,104,97,255); lip_light=(177,126,116,255); crease=(109,74,70,255)
    if mouth==0:
        c.line(24,49,29,48,lip)
        c.line(29,48,32,49,lip)
        c.line(32,49,35,48,lip)
        c.line(35,48,40,49-warmth,lip)
        c.line(24,50,31,50,crease)
        c.line(31,50,40,50-warmth,crease)
        c.line(27,51,36,51,lip_light)
        c.line(29,52,34,52,(187,148,128,255))
        if warmth==2:
            c.put(23,48,crease);c.put(41,47,crease)
    else:
        rx,ry=((7,2),(6,4),(4,4),(8,3),(7,2))[mouth-1]
        c.ellipse(32,50,rx+1,ry+1,lip)
        c.ellipse(32,50,rx,ry,(69,43,42,255))
        if mouth in (2,4,5):c.line(32-rx+2,49-ry//2,32+rx-2,49-ry//2,(199,186,161,255))
        if mouth in (2,4):c.line(29,50+ry-1,35,50+ry-1,(160,99,93,255))
        c.line(28,52+ry,35,52+ry,(185,141,123,255))
    c.line(28,57,36,57,(170,139,119,255))
    return c


def face_atlas(path, validate_only=False):
    atlas=PixelCanvas(512,256)
    tiles=[face_tile(i//6,i%6) for i in range(30)]+[face_tile(warmth=1),face_tile(warmth=2)]
    for i,tile in enumerate(tiles):
        for y in range(64):
            dst=(((i//8)*64+y)*512+(i%8)*64)*4
            atlas.pixels[dst:dst+256]=tile.pixels[y*256:(y+1)*256]
    return publish_bytes(path,atlas.png_bytes(),validate_only)


def wardrobe_atlas(path, validate_only=False):
    c=PixelCanvas(256,256);c.rect(0,0,256,256,(255,255,255,255))
    # Top-left is fine jersey; top-right is swept dark hair detail. The
    # lower two cells draw the front and back of one full high-rise jean.
    for y in range(128):
        for x in range(128):
            n=246+((x*11+y*17)%8)
            if x<64:
                px=(x-2)/60*.304-.152;z=1.055+(125-y)/123*.386
                underside=1.213+.018*(px/.12)**2
                shadow=8*math.exp(-(px/.135)**4-((z-underside)/.034)**2)
                tension=4*math.exp(-((abs(px)-(.11+(z-1.17)*.22))/.018)**2-((z-1.195)/.060)**2)
                light=2*math.exp(-((abs(px)-.063)/.050)**2-((z-1.267)/.060)**2)
                n=round(min(255,n-shadow-tension+light))
            c.put(x,y,(n,n,n,255))
    for side in (0,128):
        for y in range(128,256):
            for x in range(128):
                fade=round(7*math.exp(-((abs(x-64)-32)/12)**2-((y-204)/38)**2))
                n=232+((x+2*y)%9)+fade;c.put(side+x,y,(n,n,n,255))
        # Waistband, long outer seams and low-contrast thread topstitch.
        for y in (134,139):c.line(side+6,y,side+121,y,(201,204,195,255))
        for x in (10,118):
            c.line(side+x,143,side+x,253,(211,214,204,255))
            for y in range(145,253,4):c.put(side+x+2,y,(239,237,216,255))
    c.line(63,140,63,169,(167,179,185,255));c.line(67,140,67,168,(212,216,207,255))
    for x,sign in ((18,1),(109,-1)):
        c.line(x,144,x+sign*8,154,(191,204,206,255));c.line(x+sign*8,154,x+sign*15,158,(191,204,206,255))
        c.put(x,144,(240,225,189,255))
    for x in (145,211):
        c.line(x,151,x+27,151,(189,201,201,255));c.line(x,151,x+2,172,(194,204,202,255))
        c.line(x+2,172,x+15,177,(208,214,199,255));c.line(x+15,177,x+27,170,(208,214,199,255))
        c.line(x+27,170,x+27,151,(194,204,202,255))
        c.line(x+3,160,x+13,165,(226,224,204,255));c.line(x+13,165,x+25,159,(226,224,204,255))
    # Ordinary rear centre seam and shallow V yoke define how denim is sewn.
    c.line(191,139,191,164,(167,183,192,255));c.line(194,140,194,163,(218,218,198,255))
    c.line(135,143,192,153,(176,191,196,255));c.line(192,153,248,143,(176,191,196,255))
    c.line(137,145,192,155,(220,219,200,255));c.line(192,155,247,145,(220,219,200,255))
    for x in range(133,252,7):
        for y in range(3,125):
            shift=round(3*math.sin(y/124*math.tau+x*.03))
            c.put(x+shift,y,(213,202,187,255));c.put(x+shift+2,y,(241,231,214,255))
    c.rect(0,0,4,4,(255,255,255,255))
    return publish_bytes(path,c.png_bytes(),validate_only)


class WomanBuilder(base.PedestrianBuilder):
    def __init__(self):super().__init__(None)

    def remap_geometry_point(self, point, bone_name, role, name):
        # Every coordinate below is already the canonical V2 metre coordinate.
        return Vector(point)

    def weight_height(self,obj,stations):
        bpy.context.view_layer.update()
        for vg in list(obj.vertex_groups):obj.vertex_groups.remove(vg)
        groups={name:obj.vertex_groups.new(name=name) for _,name in stations}
        for vertex in obj.data.vertices:
            z=(obj.matrix_world@vertex.co).z
            pair=(stations[0],stations[0]) if z<=stations[0][0] else (stations[-1],stations[-1])
            for a,b in zip(stations,stations[1:]):
                if a[0]<=z<=b[0]:pair=a,b;break
            a,b=pair;t=0 if a[0]==b[0] else (z-a[0])/(b[0]-a[0])
            groups[a[1]].add([vertex.index],1-t,"REPLACE")
            if a!=b:groups[b[1]].add([vertex.index],t,"REPLACE")

    def add(self,name,geometry,bone,color,role="clothing"):
        return self.add_part(name,geometry,bone,role,color)

    def head(self):
        # One egg-shaped skull. Face and side/back share identical boundary
        # vertices; no raised sticker, facial feature mesh or hidden full head.
        rows=((1.486,.028,.037,-.014),(1.506,.047,.057,-.014),
              (1.529,.063,.074,-.014),(1.552,.077,.084,-.014),
              (1.575,.087,.092,-.014),(1.599,.091,.098,-.014),
              (1.623,.091,.098,-.014),(1.647,.089,.094,-.014),
              (1.671,.082,.086,-.014),(1.695,.070,.074,-.014),
              (1.719,.049,.055,-.014))
        face_angles=[math.radians(-68+i*136/8) for i in range(9)]
        back_angles=[math.radians(68+i*224/18) for i in range(19)]
        def surface(angles):
            vertices=[(rx*math.sin(a),cy-ry*math.cos(a),z) for z,rx,ry,cy in rows for a in angles]
            width=len(angles)
            faces=[(j*width+i,j*width+i+1,(j+1)*width+i+1,(j+1)*width+i)
                   for j in range(len(rows)-1) for i in range(width-1)]
            return vertices,faces
        obj=self.add(FACE_NAME,surface(face_angles),"head","white","facial_atlas")
        uv=obj.data.uv_layers.new(name="UVMap")
        for loop in obj.data.loops:
            uv.data[loop.index].uv=(loop.vertex_index%9/8,loop.vertex_index//9/10)
        obj["bp_face_atlas_renderer"]=True
        obj["bp_uv_contract"]="local_0_1_runtime_cell_scale_offset"
        self.add("GEO_Head",surface(back_angles),"head","skin","body")
        for part in self.result.parts:
            # Both meshes share analytic cranial normals at their exact seam.
            for polygon in part.obj.data.polygons:polygon.use_smooth=True
            points=[part.obj.location+v.co for v in part.obj.data.vertices]
            part.obj.data.normals_split_custom_set_from_vertices([Vector((p.x/.09,(p.y+.014)/.10,(p.z-1.61)/.13)).normalized() for p in points])
        self.add("GEO_Neck",base.make_vertical_shell(((1.39,.061,.054,-.006),(1.421,.060,.051,-.008),(1.452,.050,.045,-.012),(1.486,.042,.042,-.010),(1.51,.040,.041,-.008)),16),"neck","skin","body")
        for side,sign in (("L",1),("R",-1)):
            self.add("GEO_Ear."+side,base.make_ellipsoid((sign*.090,-.007,1.591),(.014,.018,.031),10,6),"head","skin","body_detail")
        # Swept crown locks follow the cranium and preserve its
        # asymmetric part, covering ears at the rear but keeping eyes open.
        self.add("HAIR_Crown",base.make_ellipsoid((0,.002,1.687),(.099,.100,.065),20,8),"head","hair","hair")
        # Short attached hair covers the occiput independently of the loose
        # lengths. Without this scalp layer a head tilt exposes bare skull
        # whenever the physical locks fall away from their upright rest pose.
        nape=[];nape_faces=[];columns=8
        for level in range(5):
            t=level/4
            for column in range(columns):
                degrees=84+192*column/(columns-1);angle=math.radians(degrees)
                lower=1.505+.074*(abs(degrees-180)/96)**1.7
                z=lower+(1.685-lower)*t
                a,b=next((a,b) for a,b in zip(rows,rows[1:]) if a[0]<=z<=b[0])
                blend=(z-a[0])/(b[0]-a[0])
                rx=a[1]+(b[1]-a[1])*blend;ry=a[2]+(b[2]-a[2])*blend
                # Seven millimetres accommodates the coarse curved facets;
                # it is measured again against the real skull surface below.
                nape.append(((rx+.007)*math.sin(angle),-.014-(ry+.007)*math.cos(angle),z))
        for level in range(4):
            for column in range(columns-1):
                i=level*columns+column;nape_faces.append((i,i+1,i+columns+1,i+columns))
        self.add("HAIR_NapeScalp",(nape,nape_faces),"head","hair","hair")
        for polygon in self.result.parts[-1].obj.data.polygons:polygon.use_smooth=True
        for side,sign in (("L",1),("R",-1)):
            points=[(sign*.046,-.085,1.717),(sign*.080,-.084,1.682),(sign*.093,-.052,1.642),(sign*.094,-.009,1.602),(sign*.075,.045,1.576)]
            self.add("HAIR_SweptSide."+side,self.tube_path(points,(.014,.020,.019,.015,.009),10),"head","hair","hair")
        self.add("HAIR_PartSweep",self.tube_path(((-.027,-.079,1.726),(.013,-.096,1.715),(.050,-.102,1.695),(.071,-.082,1.673)),(.014,.017,.015,.007),10),"head","hair","hair")
        # Three overlapping broad layered masses read as one loose, long
        # hairstyle. Their roots stay on the skull; live physics owns the
        # twelve appended bones and the strands follow those same world bones.
        for prefix,path in HAIR_PATHS.items():self.long_hair(prefix,path)
        # A small folded kerchief worn as a narrow hairband, with a rear knot;
        # the long hairstyle and the open face remain the principal silhouette.
        band_vertices=[];band_faces=[];steps=18
        for i in range(steps+1):
            a=-math.pi/4+math.pi*1.5*i/steps;x=.101*math.cos(a);z=1.687+.068*math.sin(a);y=.016
            band_vertices.extend(((x,y-.018,z),(x,y+.018,z),(x*.975,y-.018,z-.002),(x*.975,y+.018,z-.002)))
        for i in range(steps):
            a=i*4;b=(i+1)*4
            band_faces.extend(((a,b,b+1,a+1),(a+2,a+3,b+3,b+2),(a,a+2,b+2,b),(a+1,b+1,b+3,a+3)))
        band_faces.extend(((0,1,3,2),(steps*4+2,steps*4+3,steps*4+1,steps*4)))
        self.add("CLO_HairKerchief",(band_vertices,[tuple(reversed(face)) for face in band_faces]),"head","kerchief","headwear")
        for side,sign in (("L",1),("R",-1)):
            self.add("CLO_KerchiefFold."+side,self.tube_path(((sign*.071,.016,1.639),(sign*.061,.117,1.630),(sign*.014,.147,1.615)),(.012,.014,.010),6),"head","kerchief","headwear")
        self.add("CLO_KerchiefKnot",base.make_ellipsoid((0,.151,1.615),(.024,.014,.014),10,5),"head","kerchief","headwear")
        for side,sign in (("L",1),("R",-1)):
            z=1.565 if sign>0 else 1.555
            self.add("CLO_KerchiefTuckedEnd."+side,base.make_tapered_box((sign*.030,.168,z),(sign*.008,.162,1.611),(.020,.003,0),(.014,.005,0)),"head","kerchief","headwear")

    def long_hair(self,prefix,path):
        widths=(.075,.104,.091,.063) if prefix=="HairBack" else (.021,.044,.036,.022)
        depths=(.025,.025,.021,.004) if prefix=="HairBack" else (.019,.022,.018,.004)
        sides=16;vertices=[]
        for row in range(13):
            progress=row/12;i=min(2,int(progress*3));t=progress*3-i
            p1=Vector(path[i]);p2=Vector(path[i+1])
            p0=Vector(path[i-1]) if i else p1*2-p2
            p3=Vector(path[i+2]) if i<2 else p2*2-p1
            center=.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)
            center.y+=.011*math.sin(progress*math.tau)*math.sin(progress*math.pi)
            center.x+=(.008 if prefix!="HairRight" else -.008)*math.sin(progress*math.tau)*math.sin(progress*math.pi)
            rx=widths[i]+(widths[i+1]-widths[i])*t;ry=depths[i]+(depths[i+1]-depths[i])*t
            for j in range(sides):
                a=j*math.tau/sides
                # Quiet large waves and unequal layered ends, never rope rings.
                wave=.0035*math.sin(a*3+progress*9)*math.sin(progress*math.pi)
                offset={"HairBack":.8,"HairLeft":1.7,"HairRight":-.4}[prefix]
                z=center.z+(.032*math.sin(a*2+offset)+.008*math.sin(a*5+offset) if row==12 else 0)
                vertices.append((center.x+(rx+wave)*math.cos(a),center.y+(ry+wave*.4)*math.sin(a),z))
        faces=[tuple(reversed(range(sides)))]
        for row in range(12):
            for j in range(sides):
                a=row*sides+j;b=row*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
        faces.append(tuple(12*sides+j for j in range(sides)))
        # These rings descend; their winding is the inverse of an ascending
        # vertical shell. This is visible only under actual backface culling.
        obj=self.add("HAIR_Long"+prefix.removeprefix("Hair"),(vertices,[tuple(reversed(face)) for face in faces]),"head","hair","hair")
        for vg in list(obj.vertex_groups):obj.vertex_groups.remove(vg)
        names=("head",prefix+".00",prefix+".01",prefix+".02")
        groups={n:obj.vertex_groups.new(name=n) for n in names}
        stations=((0.,"head"),(.17,prefix+".00"),(.50,prefix+".01"),(.83,prefix+".02"),(1.,prefix+".02"))
        for vertex in obj.data.vertices:
            t=(vertex.index//sides)/12
            for a,b in zip(stations,stations[1:]):
                if a[0]<=t<=b[0]:
                    blend=(t-a[0])/(b[0]-a[0]);groups[a[1]].add([vertex.index],1-blend,"REPLACE")
                    if a[1]!=b[1]:groups[b[1]].add([vertex.index],blend,"REPLACE")
                    else:groups[a[1]].add([vertex.index],1.,"REPLACE")
                    break
        for polygon in obj.data.polygons:polygon.use_smooth=True

    @staticmethod
    def tube_path(points,radii,sides,initial_direction=None,ellipticity=None):
        vertices=[]
        for i,(p,r) in enumerate(zip(points,radii)):
            direction=Vector(initial_direction) if initial_direction is not None and i<2 else Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
            q=direction.to_track_quat("Z","Y")
            for j in range(sides):
                a=j*math.tau/sides
                oval=ellipticity[i] if ellipticity is not None else .62
                vertices.append(tuple(Vector(p)+q@Vector((r*math.cos(a),r*oval*math.sin(a),0))))
        faces=[tuple(reversed(range(sides)))]
        for i in range(len(points)-1):
            for j in range(sides):
                k=i*sides+j;n=i*sides+(j+1)%sides
                faces.append((k,n,n+sides,k+sides))
        faces.append(tuple((len(points)-1)*sides+j for j in range(sides)))
        return vertices,faces

    def limb(self,name,bones,radii,color,sides=14):
        upper,lower=bones
        a=Vector(base.BONE_BY_NAME[upper].head);b=Vector(base.BONE_BY_NAME[lower].head);c=Vector(base.BONE_BY_NAME[lower].tail)
        points=[a.lerp(b,i/4) for i in range(4)]+[b]+[b.lerp(c,i/4) for i in range(1,5)]
        elbow_row=4
        sleeve=name.startswith("CLO_Sleeve")
        if sleeve:
            axis=(b-a).normalized()
            # The curved sleeve shoulder grows out of the torso with closed
            # narrow roots, not a capped pipe or a separate round shoulder pad.
            points=[a+axis*.010,a+axis*.025,a+axis*.050,a+axis*.075,a.lerp(b,.40),a.lerp(b,.68),b]+[b.lerp(c,t) for t in (.125,.25,.375,.50,.75,1.)]
            # The hidden root sits well inside the chest. Keeping its ring
            # normal on the arm axis avoids the old sharp kink/cap that
            # became an open triangular shoulder when the arm lowered.
            points[0]=Vector((math.copysign(.118,a.x),a.y,a.z-.002))
            # Quiet anatomical changes remain visible through fitted jersey:
            # rounded shoulder/upper-arm mass, elbow narrowing, the fuller
            # proximal forearm, then the long taper into the existing cuff.
            radii=(.026,.040,.050,.053,.058,.049,.036,.043,.047,.045,.041,.032,.030)
            elbow_row=6
        oval=(.62,.70,.75,.78,.78,.76,.80,.77,.74,.71,.68,.65,.64) if sleeve else None
        obj=self.add(name,self.tube_path(points,radii,sides,(b-a).normalized() if sleeve else None,oval),upper,color)
        for vg in list(obj.vertex_groups):obj.vertex_groups.remove(vg)
        ga=obj.vertex_groups.new(name=upper);gb=obj.vertex_groups.new(name=lower)
        gc=obj.vertex_groups.new(name="chest") if sleeve else None
        for vertex in obj.data.vertices:
            row=vertex.index//sides;t=max(0,min(1,(row-elbow_row+1)/2))
            shoulder=1. if sleeve and row==0 else .35 if sleeve and row==1 else 0.
            ga.add([vertex.index],(1-t)*(1-shoulder),"REPLACE");gb.add([vertex.index],t,"REPLACE")
            if shoulder:gc.add([vertex.index],shoulder,"REPLACE")
        if sleeve:
            # Draft the inset sleeve seam with the arm hanging naturally,
            # then invert its exact skin matrices back into the export pose.
            # This keeps the fixed chest ring from making a rigid top ledge
            # when the original diagonal arm rest pose lowers into idle.
            rig=self.result.rig;base.reset_pose(rig);base.apply_pose(rig,base.CITIZEN_HANGING_ARMS)
            bpy.context.view_layer.update()
            transforms={n:rig.pose.bones[n].matrix@rig.data.bones[n].matrix_local.inverted() for n in (upper,"chest")}
            arm=transforms[upper];orientation=arm.to_3x3()@(b-a).to_track_quat("Z","Y").to_matrix()
            shoulder_head=arm@a
            for vertex in obj.data.vertices:
                row=vertex.index//sides
                if row>1:continue
                weight=1. if row==0 else .35
                skin=transforms["chest"]*weight+arm*(1-weight)
                center=Vector((math.copysign(.105 if row==0 else .153,a.x),shoulder_head.y,shoulder_head.z-(.004 if row==0 else .011)))
                angle=(vertex.index%sides)*math.tau/sides;radius=.025 if row==0 else .033
                desired=center+orientation@Vector((radius*math.cos(angle),radius*.95*math.sin(angle),0))
                vertex.co=obj.matrix_world.inverted()@skin.inverted()@desired
            base.reset_pose(rig);bpy.context.view_layer.update()
        return obj

    def hand(self,side):
        bone="hand."+side;sign=1 if side=="L" else -1
        start=Vector(base.BONE_BY_NAME[bone].head);end=Vector(base.BONE_BY_NAME[bone].tail)
        q=(end-start).to_track_quat("Z","Y")
        scale=1.10
        def world(p):return tuple(start+q@(Vector(p)*.92*scale))
        self.add("GEO_Palm."+side,base.make_ellipsoid(world((0,0,.035)),tuple(v*scale for v in (.030,.018,.044)),12,7,orientation=q),bone,"skin","body")
        # Four tapered digits with knuckle bends, thumb opposed around the
        # canonical grip. They share the hand bone (31-bone runtime contract).
        for i in range(4):
            x=(i-1.5)*.014
            length=(.042,.053,.049,.037)[i]
            pts=[world((x,0,.058)),world((x,-.002,.073)),world((x,-.011,.073+length*.52)),world((x,-.019,.063+length))]
            self.add("GEO_Finger"+str(i)+"."+side,self.tube_path(pts,tuple(v*scale for v in (.0082,.0081,.007,.0058)),6),bone,"skin","body_detail")
        thumb=[world((sign*.025,-.002,.017)),world((sign*.043,-.008,.032)),world((sign*.044,-.018,.052)),world((sign*.035,-.023,.065))]
        self.add("GEO_Thumb."+side,self.tube_path(thumb,tuple(v*scale for v in (.011,.010,.009,.007)),8),bone,"skin","body_detail")

    def jeans(self):
        """One manifold branched trouser shell, with shared hip/leg vertices.

        The two upper-leg outer arcs form the actual pelvis boundary. Their
        inner arcs join across the crotch; no separate seat panel overlays the
        thighs and the width grows at every station above the knee.
        """
        sides=24;vertices=[];faces=[];weights=[];tops={}
        # z, centre-x, width, depth; deliberate continuous knee-to-hip flare.
        stations=((.118,.085,.044,.043),(.235,.084,.050,.051),(.340,.083,.058,.057),
                  (.485,.082,.052,.054),(.575,.086,.067,.066),(.665,.092,.081,.079),
                  (.755,.098,.092,.094),(.820,.104,.098,.105),(.855,.105,.097,.109))
        for side,sign in (("L",1),("R",-1)):
            start=len(vertices)
            for z,cx,rx,ry in stations:
                for j in range(sides):
                    angle=math.tau*j/sides
                    vertices.append((sign*cx+rx*math.cos(angle),.006+ry*math.sin(angle),z))
                    knee=max(0,min(1,(z-.415)/.14));hip=max(0,min(1,(z-.76)/.15))
                    weights.append({"shin."+side:1-knee,"thigh."+side:knee*(1-hip),"pelvis":knee*hip})
            for row in range(len(stations)-1):
                for j in range(sides):
                    a=start+row*sides+j;b=start+row*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
            faces.append(tuple(reversed([start+j for j in range(sides)])))
            tops[side]=[start+(len(stations)-1)*sides+j for j in range(sides)]
        # Left outside:225..495deg; right outside45..315deg. Join the back
        # and front with shared surface faces instead of a horizontal seat lip.
        boundary=[tops["L"][j%sides] for j in range(15,34)]+[tops["R"][j%sides] for j in range(3,22)]
        left_inner=[tops["L"][j] for j in range(9,16)]
        right_inner=[tops["R"][j%sides] for j in range(27,20,-1)]
        for i in range(6):faces.append((left_inner[i],left_inner[i+1],right_inner[i+1],right_inner[i]))
        previous=boundary
        angles=[math.atan2((vertices[index][1]-.006)/.109,vertices[index][0]/.202) for index in boundary]
        for z,rx,ry in ((.900,.200,.116),(.95,.194,.115),(.995,.180,.108),(1.040,.159,.102),(1.078,.144,.098),(1.105,.140,.096)):
            ring=[]
            for angle in angles:
                cy=.006-.01*max(0,min(1,(z-.995)/.11))
                ring.append(len(vertices));vertices.append((rx*math.cos(angle)*waist_factor(z),cy+ry*math.sin(angle),z));weights.append({"pelvis":1.})
            for i in range(len(ring)):
                j=(i+1)%len(ring);faces.append((previous[i],previous[j],ring[j],ring[i]))
            previous=ring
        faces.append(tuple(previous))
        # Modest rear cloth shaping: two connected soft lobes under the same
        # jeans surface, preserving the outer hip width and continuous crotch.
        for i,(x,y,z) in enumerate(vertices):
            if y>.025:
                rear=max(0,min(1,(y-.025)/.065))
                lobes=.053*math.exp(-((abs(x)-.081)/.056)**2-((z-.912)/.095)**2)
                seam=.006*math.exp(-(x/.035)**2-((z-.915)/.080)**2)
                vertices[i]=(x,y+rear*(lobes-seam),z)
        obj=self.add("CLO_HighWaistJeans",(vertices,faces),"pelvis","denim")
        for vg in list(obj.vertex_groups):obj.vertex_groups.remove(vg)
        groups={name:obj.vertex_groups.new(name=name) for name in ("pelvis","thigh.L","thigh.R","shin.L","shin.R")}
        for i,assignment in enumerate(weights):
            for name,weight in assignment.items():
                if weight>0:groups[name].add([i],weight,"REPLACE")
        for polygon in obj.data.polygons:polygon.use_smooth=len(polygon.vertices)==4
        # An ordinary leather belt at the waist, not down at the hip line.
        self.add("CLO_LeatherBelt",base.make_vertical_shell(((1.077,.147*waist_factor(1.077),.102,-.002),(1.100,.143*waist_factor(1.100),.101,-.003)),32),"pelvis","belt")
        for x,z,w,h in ((-.015,1.089,.004,.026),(.015,1.089,.004,.026),(0,1.101,.030,.004),(0,1.077,.030,.004),(0,1.089,.025,.003)):
            self.add("CLO_BeltBuckle"+str(len(self.result.parts)),outward_box((x,-.108,z),(w,.005,h)),"pelvis","buckle")
        for angle in (-math.pi/2-.60,-math.pi/2+.60,0,math.pi/2,math.pi):
            x=.146*math.cos(angle)*waist_factor(1.09);y=-.003+.105*math.sin(angle)
            self.add("CLO_BeltLoop"+str(len(self.result.parts)),outward_box((x,y,1.09),(.012,.008,.036)),"pelvis","denim")

    def build(self):
        self.reset_scene()
        collection=bpy.data.collections.new("EXPORT_CanneryWoman");bpy.context.scene.collection.children.link(collection)
        base.PALETTE.update({name:hex_to_linear_rgba(value) for name,value in COLORS.items()})
        material=self.create_shared_material();root=bpy.data.objects.new("ROOT_Player",None);collection.objects.link(root)
        rig=self.create_armature(collection,root);self.result=base.BuildResult(root,rig,collection,material)
        self.head()
        # A fitted everyday long-sleeve tee, tucked into high-rise jeans.
        stations=((1.055,.129,.084,.001),(1.075,.131,.086,.001),(1.105,.133,.088,-.001),
                  (1.16,.140,.097,-.004),(1.215,.157,.112,-.007),(1.270,.166,.125,-.008),
                  (1.315,.159,.119,-.005),(1.355,.159,.104,-.002),(1.390,.154,.091,-.001),
                  (1.414,.140,.078,-.003),(1.430,.094,.060,-.008),(1.441,.060,.048,-.010))
        rounded=[]
        for a,b in zip(stations,stations[1:]):
            rounded.append(a)
            subdivisions=3 if 1.21<=a[0]<1.31 else 2 if 1.15<=a[0]<1.35 else 1
            for i in range(1,subdivisions):rounded.append(tuple(x+(y-x)*i/subdivisions for x,y in zip(a,b)))
        stations=tuple(rounded+[stations[-1]])
        stations=tuple((z,rx*waist_factor(z),ry,cy) for z,rx,ry,cy in stations)
        jacket=self.add("CLO_LongSleeveTop",base.make_vertical_shell(stations,32),"chest","longsleeve")
        bpy.context.view_layer.update()
        for vertex in jacket.data.vertices:
            point=jacket.matrix_world@vertex.co
            original=point.copy()
            upper=max(0,min(1,(point.z-1.17)/.07))*max(0,min(1,(1.448-point.z)/.03))
            point.x*=1-.09*upper
            point.y*=1-(.08 if point.y<0 else .10)*upper
            row=min(stations,key=lambda value:abs(value[0]-point.z))
            facing=(row[3]-original.y)/row[2]
            if facing>0:
                # Each horizontal section rounds around the torso. An explicit
                # gently lowered profile replaces the old forward bump; the
                # cloth bridges the two volumes with only a shallow centre.
                across=abs(point.x)/max(.001,row[1]*(1-.09*upper))
                middle=max(0.,1-(point.x/.054)**2)**2
                outside=max(0.,min(1.,(abs(point.x)-.085)/.065))**2
                drape=max(0.,1-((point.z-1.270)/.150)**2)**2
                # Raise the lower contour between and outside the volumes;
                # the two rounded low points no longer share a flat shelf.
                lift=(.027*middle+.022*outside)*drape
                depth=cloth_front_depth(point.z-lift)*math.sqrt(max(0.,1-.65*across*across))
                lower_shape=max(0.,1-((point.z-1.264)/.109)**2)**2
                center=.023*lower_shape*middle
                blend=max(0.,min(1.,(facing-.10)/.60));blend=blend*blend*(3-2*blend)
                point.y=point.y*(1-blend)+(-depth+center)*blend
            vertex.co=jacket.matrix_world.inverted()@point
        # The entire tucked region follows the same pelvis as jeans and belt.
        # A spine-weighted hem otherwise slides out as a visible white flap.
        self.weight_height(jacket,((1.105,"pelvis"),(1.180,"spine"),(1.28,"chest")))
        self.jeans()
        self.add("CLO_CrewNeckRib",base.make_vertical_shell(((1.435,.063,.051,-.010),(1.445,.059,.049,-.010)),24),"chest","longsleeve")
        for side,sign in (("L",1),("R",-1)):
            self.limb("CLO_Sleeve."+side,("upper_arm."+side,"forearm."+side),(.050,.052,.048,.044,.043,.046,.041,.035,.033),"longsleeve",14)
            wrist=Vector(base.BONE_BY_NAME["hand."+side].head);elbow=Vector(base.BONE_BY_NAME["forearm."+side].head)
            start=wrist.lerp(elbow,.14)
            self.add("CLO_Cuff."+side,base.make_frustum_between(start,wrist,.036,.034,14,.72),"forearm."+side,"longsleeve")
            self.hand(side)
            ankle=base.BONE_BY_NAME["foot."+side].head
            self.add("GEO_WorkShoe."+side,base.make_vertical_shell(((.025,.060,.123,-.073),(.044,.063,.124,-.073),(.067,.060,.110,-.063),(.092,.049,.080,-.034),(.118,.046,.047,-.010)),16),"foot."+side,"shoe","footwear")
            for vertex in self.result.parts[-1].obj.data.vertices:vertex.co.x+=ankle[0]
            self.add("GEO_ShoeWelt."+side,base.make_vertical_shell(((.008,.062,.122,-.077),(.018,.065,.127,-.077),(.034,.063,.123,-.077)),16),"foot."+side,"sole","footwear")
            welt=self.result.parts[-1].obj
            for vertex in welt.data.vertices:vertex.co.x+=ankle[0]
            self.add("GEO_ShoeHeel."+side,base.make_vertical_shell(((0,.044,.030,.008),(.022,.046,.031,.008)),12),"foot."+side,"sole","footwear")
            for vertex in self.result.parts[-1].obj.data.vertices:vertex.co.x+=ankle[0]
        for part in self.result.parts:
            if part.obj.name!=FACE_NAME:self.uv(part)
            if part.obj.name.startswith(("CLO_LongSleeveTop","CLO_Sleeve")):
                for polygon in part.obj.data.polygons:polygon.use_smooth=len(polygon.vertices)==4
        return self.result

    def uv(self,part):
        bpy.context.view_layer.update()
        obj=part.obj;mesh=obj.data;layer=mesh.uv_layers.new(name="UVMap")
        points=[obj.matrix_world@v.co for v in mesh.vertices]
        low=[min(p[i] for p in points) for i in range(3)];high=[max(p[i] for p in points) for i in range(3)]
        for loop in mesh.loops:
            p=points[loop.vertex_index];u=(p.x-low[0])/max(.001,high[0]-low[0]);v=(p.z-low[2])/max(.001,high[2]-low[2])
            if obj.name=="CLO_HighWaistJeans":
                front=mesh.polygons[loop_polygon(mesh,loop.index)].normal.y<0
                uv=((2+u*123+(0 if front else 128))/256,(2+v*123)/256)
            elif part.role=="hair":uv=((130+u*123)/256,(130+v*123)/256)
            elif obj.name=="CLO_LongSleeveTop":
                front=mesh.polygons[loop_polygon(mesh,loop.index)].normal.y<0
                uv=((2+u*60+(0 if front else 64))/256,(130+v*123)/256)
            else:uv=(.005,.995)
            layer.data[loop.index].uv=uv


def loop_polygon(mesh,index):
    return next(p.index for p in mesh.polygons if p.loop_start<=index<p.loop_start+p.loop_total)


def mesh_clearance(result):
    """Addressed visible seams; buried caps and the upper hair roots may overlap."""
    deps=bpy.context.evaluated_depsgraph_get();meshes={}
    wanted={"CLO_LongSleeveTop","CLO_HighWaistJeans","CLO_Sleeve.L","CLO_Sleeve.R","GEO_Neck","GEO_Head","HAIR_Crown","HAIR_NapeScalp","CLO_HairKerchief"}
    wanted.update("HAIR_Long"+name for name in ("Back","Left","Right"))
    for part in result.parts:
        if part.obj.name not in wanted:continue
        obj=part.obj.evaluated_get(deps);mesh=obj.to_mesh()
        vertices=[obj.matrix_world@v.co for v in mesh.vertices]
        faces=[tuple(p.vertices) for p in mesh.polygons]
        meshes[part.obj.name]=(vertices,BVHTree.FromPolygons(vertices,faces),faces)
        obj.to_mesh_clear()
    head_inverse=(result.rig.matrix_world@result.rig.pose.bones["head"].matrix).inverted()
    local_hair={}
    for name,(vertices,_,faces) in meshes.items():
        if name.startswith("HAIR_Long"):
            points=[head_inverse@p for p in vertices]
            local_hair[name]=(points,BVHTree.FromPolygons(points,faces),faces)
    def signed(point,name,head_local=False):
        tree=(local_hair if head_local else meshes)[name][1];_,_,_,distance=tree.find_nearest(point)
        inside=0
        # A grazing ray at a warped low-poly tip can touch the triangulated
        # cap twice. Three oblique rays make the inside sign independent of
        # that one direction as the complete hairstyle rotates with the head.
        for ray in ((.8123,.3317,.4799),(-.3711,.8973,.2287),(.2719,-.4427,.8543)):
            direction=Vector(ray).normalized();origin=point.copy();crossings=0
            for _ in range(64):
                hit,_,_,_=tree.ray_cast(origin,direction,10.)
                if hit is None:break
                crossings+=1;origin=hit+direction*.00001
            inside+=crossings%2
        return -distance if inside>=2 else distance
    top=next(p.obj for p in result.parts if p.obj.name=="CLO_LongSleeveTop")
    hem=[v.index for v in top.data.vertices if (top.matrix_world@v.co).z<=1.076]
    hem_clearance=min(-signed(meshes[top.name][0][i],"CLO_HighWaistJeans") for i in hem)
    root_clearance=min(-signed(p,"CLO_LongSleeveTop") for side in ("L","R") for p in meshes["CLO_Sleeve."+side][0][:14])
    hair_clearance=10.;clothing_clearance=10.;worst=None
    band=meshes["CLO_HairKerchief"][0]
    kerchief_clearance=min(signed(point,"HAIR_Crown") for i,point in enumerate(band) if i%4<2)
    scalp,scalp_tree,scalp_faces=meshes["HAIR_NapeScalp"]
    scalp_samples=list(scalp)
    for face in scalp_faces:
        # Actual triangular surface interiors, not only the offset vertices.
        for triangle in ((face[0],face[i],face[i+1]) for i in range(1,len(face)-1)):
            a,b,c=(scalp[i] for i in triangle)
            for u in range(1,5):
                for v in range(1,6-u):scalp_samples.append(a+(b-a)*(u/6)+(c-a)*(v/6))
    scalp_clearance=10.;scalp_body_clearance=10.
    for point in scalp_samples:
        nearest,normal,_,distance=meshes["GEO_Head"][1].find_nearest(point)
        scalp_clearance=min(scalp_clearance,distance if (point-nearest).dot(normal)>0 else -distance)
        for other in ("GEO_Neck","CLO_LongSleeveTop","CLO_Sleeve.L","CLO_Sleeve.R"):
            scalp_body_clearance=min(scalp_body_clearance,signed(point,other))
    hairs=["HAIR_Long"+name for name in ("Back","Left","Right")]
    scalp_free_clearance=10.
    for name in hairs:
        vertices,_,faces=meshes[name]
        samples=list(vertices[3*16:])
        # Surface centres catch broad face crossings between sampled rings;
        # the independent Unity triangle oracle checks final simulated poses.
        samples.extend(sum((vertices[i] for i in face),Vector())/len(face) for face in faces if min(face)>=3*16)
        scalp_free_clearance=min(scalp_free_clearance,min(scalp_tree.find_nearest(point)[3] for point in samples))
        # The short fixed layer may meet hidden attachment rows. Every edge
        # of the free length must stay outside it, in either authored pose.
        free_edges={tuple(sorted((face[i],face[(i+1)%len(face)]))) for face in faces
                    for i in range(len(face)) if min(face)>=3*16}
        for a,b in free_edges:
            start=vertices[a];delta=vertices[b]-start;length=delta.length
            hit,_,_,distance=scalp_tree.ray_cast(start,delta.normalized(),length)
            if hit is not None and .0001<distance<length-.0001:
                raise RuntimeError("Free hair crosses fixed nape scalp: "+name+" edge "+str((a,b)))
        for point in samples:
            for other in hairs:
                if other!=name:hair_clearance=min(hair_clearance,signed(head_inverse@point,other,True))
            for other in ("CLO_LongSleeveTop","CLO_HighWaistJeans","CLO_Sleeve.L","CLO_Sleeve.R","GEO_Neck"):
                value=signed(point,other)
                if value<clothing_clearance:clothing_clearance=value;worst=(name,other,tuple(point))
    if not getattr(mesh_clearance,"reported_scalp_samples",False):
        print("Nape/free hair sampled surface clearance",scalp_free_clearance,"m; all free edges also checked",flush=True)
        mesh_clearance.reported_scalp_samples=True
    if clothing_clearance<0:print("Layer penetration",worst,clothing_clearance,flush=True)
    return {"hem_inside_jeans_m":hem_clearance,"sleeve_root_inside_top_m":root_clearance,"kerchief_outer_to_crown_m":kerchief_clearance,
            "free_hair_between_layers_m":hair_clearance,"free_hair_to_clothing_m":clothing_clearance,
            "nape_scalp_to_skull_m":scalp_clearance,"nape_scalp_to_body_m":scalp_body_clearance}


def hair_envelopes(result):
    output=[]
    for prefix in HAIR_PATHS:
        obj=next(p.obj for p in result.parts if p.obj.name=="HAIR_Long"+prefix.removeprefix("Hair"));rings=[]
        for row in range(13):
            points=[obj.matrix_world@v.co for v in obj.data.vertices[row*16:(row+1)*16]]
            center=sum(points,Vector())/16
            extents=[max(abs(p[i]-center[i]) for p in points) for i in range(3)]
            weights=[{"bone":obj.vertex_groups[group.group].name,"value":round(group.weight,7)} for group in obj.data.vertices[row*16].groups if group.weight>0]
            rings.append({"progress":row/12,"center_blender":[round(v,7) for v in center],
                          "half_width":round(extents[0],7),"half_depth":round(extents[1],7),"half_height":round(extents[2],7),"weights":weights})
        output.append({"renderer":obj.name,"chain":prefix,"free_from_progress":.25,"rings":rings})
    return output


def action_pose(result,name,phase):
    short=name.removeprefix("CanneryWoman")
    pose=resident.action_pose("Walk" if short=="Walk" else "Idle",phase)
    wave=math.sin(phase*math.tau)
    if short=="Walk":
        # Stable modest stride and reciprocal shoulders, with no swaying hips.
        for key,value in list(pose.items()):
            if key.startswith(("thigh.","shin.","foot.")):
                pose[key]=base.BonePose(rotation_degrees=tuple(v*.87 for v in value.rotation_degrees),location_m=value.location_m,target_direction=value.target_direction)
        pose["chest"]=base.BonePose(rotation_degrees=(.6,0,-wave*.75))
        pose["head"]=base.BonePose(rotation_degrees=(-.6,0,wave*.3))
    else:
        pose["spine"]=base.BonePose(rotation_degrees=(.6+wave*.18,0,0))
        pose["chest"]=base.BonePose(rotation_degrees=(.45*math.sin(phase*math.tau*2),0,0))
        if short=="Listen":
            pulse=math.sin(math.pi*phase)**2
            pose["head"]=base.BonePose(rotation_degrees=(1.5*pulse,0,-4*pulse))
            pose["neck"]=base.BonePose(rotation_degrees=(.5*pulse,0,-2*pulse))
        elif short=="Break":
            glance=math.sin(math.pi*phase)**4
            pose["head"]=base.BonePose(rotation_degrees=(-.8*glance,0,5*glance))
            pose["hand.L"]=base.BonePose(rotation_degrees=(2+4*glance,-5,2))
            pose["hand.R"]=base.BonePose(rotation_degrees=(2,5+4*glance,-2))
        elif short=="Work":
            pose["spine"]=base.BonePose(rotation_degrees=(13+wave*.25,0,0))
            pose["chest"]=base.BonePose(rotation_degrees=(7,0,0))
            pose["head"]=base.BonePose(rotation_degrees=(2+wave*.4,0,0))
    base.reset_pose(result.rig);base.apply_pose(result.rig,pose)


def make_actions(result):
    rig=result.rig;curves=[];max_ground=0.;max_grip=0.;endpoints={};clearance=mesh_clearance(result)
    for name,seconds,loop in CLIPS:
        action=bpy.data.actions.new(name);action.use_fake_user=True;action.use_frame_range=True
        frames=round(seconds*FPS);action.frame_start=0;action.frame_end=frames;action.use_cyclic=loop
        rig.animation_data_create().action=action
        previous={};endpoint=[]
        for frame in range(frames+1):
            phase=frame/frames;action_pose(result,name,phase)
            deps=bpy.context.evaluated_depsgraph_get()
            foot=min(base.evaluated_part_min_z(p,deps) for p in result.parts if p.bone.startswith("foot."))
            pelvis=rig.pose.bones["pelvis"];mat=pelvis.matrix.copy();mat.translation.z-=foot;pelvis.matrix=mat;bpy.context.view_layer.update()
            targets=None
            if name=="CanneryWomanWork":
                pulse=math.sin(phase*math.tau)**2
                targets={side:(sign*.17,-.54,1.02+.035*pulse) for side,sign in (("L",1),("R",-1))}
                resident.solve_grips(rig,targets)
                max_grip=max(max_grip,max((rig.pose.bones["SOCKET_Grip."+side].head-Vector(point)).length for side,point in targets.items()))
            elif name=="CanneryWomanBreak":
                # Brief cuff check: the free right fingers meet the left cuff,
                # after the arms travel there continuously, then release it.
                blend=math.sin(math.pi*max(0,min(1,(phase-.36)/.38)))**2 if .36<phase<.74 else 0
                if blend:
                    targets={"L":(.115,-.215,1.03),"R":(.100,-.231,1.088)}
                    targets={side:rig.pose.bones["SOCKET_Grip."+side].head.lerp(Vector(point),blend) for side,point in targets.items()}
                    resident.solve_grips(rig,targets,blend>=.9999)
            if frame in {round(frames*i/8) for i in range(9)}:
                for key,value in mesh_clearance(result).items():clearance[key]=min(clearance[key],value)
            for bone in rig.pose.bones:
                bone.rotation_mode="QUATERNION"
                if bone.name in previous:bone.rotation_quaternion.make_compatible(previous[bone.name])
                previous[bone.name]=bone.rotation_quaternion.copy()
                bone.keyframe_insert("location",frame=frame,group=bone.name)
                bone.keyframe_insert("rotation_quaternion",frame=frame,group=bone.name)
                bone.keyframe_insert("scale",frame=frame,group=bone.name)
            if frame in (0,frames):endpoint.append({b.name:[round(v,6) for row in b.matrix for v in row] for b in rig.pose.bones})
            max_ground=max(max_ground,abs(min(base.evaluated_part_min_z(p,bpy.context.evaluated_depsgraph_get()) for p in result.parts if p.bone.startswith("foot."))))
        endpoints[name]=max(abs(a-b) for n in endpoint[0] for a,b in zip(endpoint[0][n],endpoint[1][n]))
        for curve in base.iter_action_fcurves(action):
            for key in curve.keyframe_points:key.interpolation="LINEAR"
            curves.append((name,curve.data_path,curve.array_index,[[round(k.co.x,6),round(k.co.y,6)] for k in curve.keyframe_points]))
        rig.animation_data.action=None
        print("Authored",name,flush=True)
    base.reset_pose(rig)
    if max_ground>.002 or max_grip>.002 or max(endpoints.values())>.002:
        raise RuntimeError(f"Cannery woman action contacts differ: ground={max_ground},grip={max_grip},loops={endpoints}")
    limits={"hem_inside_jeans_m":.002,"sleeve_root_inside_top_m":.001,"kerchief_outer_to_crown_m":.001,
            "free_hair_between_layers_m":.002,"free_hair_to_clothing_m":.001}
    if any(clearance[key]<limit for key,limit in limits.items()):
        raise RuntimeError("Authored garment/hair layer clearance differs: "+json.dumps(clearance))
    return {"generator":Path(__file__).name,"version":VERSION,"bone_count":len(base.SKELETON),"fps":FPS,"root_motion":False,
            "clips":[{"name":n,"duration_seconds":s,"loop":l} for n,s,l in CLIPS],
            "work_grips_unity":{"L":[.17,1.02,.54],"R":[-.17,1.02,.54]},
            "validation":{"max_ground_error_m":round(max_ground,7),"max_grip_error_m":round(max_grip,7),
                          "max_loop_endpoint_error":round(max(endpoints.values()),7),"layer_clearance_min_m":{k:round(v,7) for k,v in clearance.items()},"curve_signature":hash_json(curves)}}


def manifest(result,wardrobe_hash,face_hash):
    metrics=resident.measured(result)
    if not 6000<=metrics["triangle_count"]<=8000:raise RuntimeError("Meaningful character triangle budget 6000..8000: "+str(metrics))
    if len(result.rig.data.bones)!=43:raise RuntimeError("31 body + twelve physical hair bones required")
    if any(p.bone.startswith("face.") for p in result.parts):raise RuntimeError("Facial features must remain painted sprites")
    closed_parts=[];negative=[];surface_payload=[]
    for part in result.parts:
        mesh=part.obj.data
        edges=Counter(tuple(sorted((p.vertices[i],p.vertices[(i+1)%len(p.vertices)]))) for p in mesh.polygons for i in range(len(p.vertices)))
        if edges and all(n==2 for n in edges.values()):
            volume=0.
            for polygon in mesh.polygons:
                points=[mesh.vertices[i].co for i in polygon.vertices]
                for i in range(1,len(points)-1):volume+=points[0].dot(points[i].cross(points[i+1]))/6
            if volume<=0:negative.append((part.obj.name,volume))
            closed_parts.append(part.obj.name)
        elif part.obj.name=="CLO_HighWaistJeans":raise RuntimeError("Continuous jeans have an open seam")
        for vertex in mesh.vertices:
            if abs(sum(g.weight for g in vertex.groups)-1)>.00001:raise RuntimeError("Non-normalized weights: "+part.obj.name)
        surface_payload.append((part.obj.name,[[round(v.uv.x,7),round(v.uv.y,7)] for v in mesh.uv_layers.active.data]))
    if negative:raise RuntimeError("Inward closed meshes: "+str(negative))
    face=next(p.obj for p in result.parts if p.obj.name==FACE_NAME)
    uv=[v.uv for v in face.data.uv_layers.active.data]
    if [min(v[i] for v in uv) for i in range(2)]!=[0,0] or [max(v[i] for v in uv) for i in range(2)]!=[1,1]:raise RuntimeError("Face must retain the complete local UV square")
    payload={"generator":Path(__file__).name,"version":VERSION,"role":"CanneryWoman","anatomy_standard":"NpcHumanV2",
             "height_scale":1.63/(metrics["bounds_max"][2]-metrics["bounds_min"][2]),"height_m":1.63,"bone_count":43,**metrics,"triangle_budget":[6000,8000],
             "body_bone_count":31,"hair_chains":[{"name":name,"bones":[name+f".{i:02}" for i in range(3)]+[name+".Tip"],"points_blender":list(path)} for name,path in HAIR_PATHS.items()],
             "hair_envelopes":hair_envelopes(result),
             "closed_mesh_count":len(closed_parts),"surface_signature":hash_json(surface_payload),
             "atlas_sha256":wardrobe_hash,"face_atlas_sha256":face_hash,
             "face":{"renderer":FACE_NAME,"texture":"CanneryWomanFaceAtlas.png","width":512,"height":256,
                     "columns":8,"rows":4,"cell_size":64,"uv_contract":"local_0_1_runtime_cell_scale_offset",
                     "expressions":["Rest","HalfBlink","Blink","Emphasis","Focused"],
                     "mouths":["Closed","Narrow","Open","Round","Wide","Teeth"],"softened_cell":30,"warm_smile_cell":31},
             "parts":[{"name":p.obj.name,"color":list(p.color),"role":p.role,
                       "triangles":base.triangulated_count(p.obj.data)} for p in result.parts],
             "geometry_signature":resident.geometry_signature(result)}
    slots={name:[] for name in ("top","sleeves","cuffs","jeans","belt","buckle","belt_loops","shoes","kerchief","knot","ends")}
    for part in result.parts:
        name=part.obj.name
        if name.startswith(("CLO_LongSleeveTop","CLO_CrewNeckRib")):slot="top"
        elif name.startswith("CLO_Sleeve"):slot="sleeves"
        elif name.startswith("CLO_Cuff"):slot="cuffs"
        elif name=="CLO_HighWaistJeans":slot="jeans"
        elif name=="CLO_LeatherBelt":slot="belt"
        elif name.startswith("CLO_BeltBuckle"):slot="buckle"
        elif name.startswith("CLO_BeltLoop"):slot="belt_loops"
        elif part.role=="footwear":slot="shoes"
        elif name.startswith(("CLO_HairKerchief","CLO_KerchiefFold")):slot="kerchief"
        elif name=="CLO_KerchiefKnot":slot="knot"
        elif name.startswith("CLO_KerchiefTuckedEnd"):slot="ends"
        else:continue
        slots[slot].append(name)
    payload["outfit"]={"id":"cannery_workwear","atlas":"CanneryWomanAtlas.png","parts":[{"slot":slot,"renderers":names} for slot,names in slots.items() if names]}
    declared=[name for names in slots.values() for name in names]
    garments=[p.obj.name for p in result.parts if p.role in ("clothing","headwear","footwear")]
    if sorted(declared)!=sorted(garments):raise RuntimeError("Every garment needs exactly one wardrobe slot")
    payload["signature"]=hash_json(payload)
    return payload


def previews(result,sources,wardrobe,faces,pose_only=False):
    scene=bpy.context.scene;scene.view_settings.view_transform="Standard"
    metrics=resident.measured(result);height_scale=1.63/(metrics["bounds_max"][2]-metrics["bounds_min"][2])
    result.root.scale=(height_scale,)*3;bpy.context.view_layer.update()
    scene.view_settings.look="Medium High Contrast" if "Medium High Contrast" in [i.identifier for i in scene.view_settings.bl_rna.properties["look"].enum_items] else "None"
    scene.world.use_nodes=True;bg=scene.world.node_tree.nodes.get("Background");bg.inputs["Color"].default_value=(.11,.13,.14,1);bg.inputs["Strength"].default_value=.55
    nodes=result.material.node_tree.nodes;shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(wardrobe));tex.interpolation="Closest"
    info=nodes.new("ShaderNodeObjectInfo");mix=nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY";mix.inputs[0].default_value=1
    links=result.material.node_tree.links;links.new(info.outputs["Color"],mix.inputs[1]);links.new(tex.outputs["Color"],mix.inputs[2]);links.new(mix.outputs[0],shader.inputs["Base Color"])
    material=result.material.copy();material.name="PREVIEW_PaintedFace";nodes=material.node_tree.nodes;links=material.node_tree.links
    shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED");tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(faces));tex.interpolation="Closest"
    coords=nodes.new("ShaderNodeTexCoord");mapping=nodes.new("ShaderNodeVectorMath");mapping.operation="MULTIPLY_ADD";mapping.inputs[1].default_value=(.125,.25,1);mapping.inputs[2].default_value=(0,.75,0)
    links.new(coords.outputs["UV"],mapping.inputs[0]);links.new(mapping.outputs["Vector"],tex.inputs["Vector"]);links.new(tex.outputs["Color"],shader.inputs["Base Color"])
    face=next(p.obj for p in result.parts if p.obj.name==FACE_NAME);face.data.materials.clear();face.data.materials.append(material)
    for loc,power,size in (((-3,-4,4),500,4),((3,-1,3),260,4),((1,3,4),500,3)):
        light=bpy.data.lights.new("ReviewSoftbox","AREA");light.energy=power;light.shape="DISK";light.size=size
        obj=bpy.data.objects.new("ReviewSoftbox",light);scene.collection.objects.link(obj);obj.location=loc;obj.rotation_euler=(Vector((0,0,1.2))-obj.location).to_track_quat("-Z","Y").to_euler()
    camera=bpy.data.cameras.new("ReviewCamera");obj=bpy.data.objects.new("ReviewCamera",camera);scene.collection.objects.link(obj);scene.camera=obj;camera.type="ORTHO"
    for name,location,focus,size,clip,seconds,cell in (
            ("CanneryWoman",(2,-4,2.1),(0,0,.89),1.98,"CanneryWomanIdle",0,0),
            ("BodyFront",(0,-4,1.55),(0,0,.89),1.98,"CanneryWomanIdle",0,0),
            ("BodyBack",(1.4,4,1.9),(0,.04,.89),1.98,"CanneryWomanIdle",0,0),
            ("BodyProfile",(4,-.12,1.60),(0,0,.89),1.98,"CanneryWomanIdle",0,0),
            ("FaceFront",(0,-4,1.63),(0,-.02,1.62),.43,"CanneryWomanIdle",0,0),
            ("FaceThreeQuarter",(.9,-2,1.66),(0,-.02,1.62),.43,"CanneryWomanIdle",0,0),
            ("FaceProfile",(3,-.40,1.64),(0,0,1.62),.43,"CanneryWomanIdle",0,0),
            ("FaceSmile",(0,-4,1.63),(0,-.02,1.62),.43,"CanneryWomanIdle",0,31),
            ("NapeCoverage",(1.7,3.8,2.3),(0,.02,1.61),.59,"CanneryWomanIdle",0,0),
            ("Working",(2,-4,2.3),(0,-.1,1),1.98,"CanneryWomanWork",2,24),
            ("CuffCheck",(1.5,-4,2),(0,-.05,1.1),1.8,"CanneryWomanBreak",6,0)):
        if pose_only:
            action_pose(result,clip,seconds/next(s for n,s,_ in CLIPS if n==clip))
            if clip=="CanneryWomanWork":resident.solve_grips(result.rig,{side:(sign*.17,-.54,1.02) for side,sign in (("L",1),("R",-1))})
        else:
            result.rig.animation_data.action=bpy.data.actions[clip];scene.frame_set(round(seconds*FPS));bpy.context.view_layer.update()
        if name=="NapeCoverage":
            if result.rig.animation_data:result.rig.animation_data.action=None
            action_pose(result,"CanneryWomanIdle",0)
            base.apply_pose(result.rig,{"head":base.BonePose(rotation_degrees=(48,0,0))})
            # Deliberately reveal the short permanent layer in this source
            # diagnostic; game previews retain all three simulated lengths.
            for part in result.parts:
                if part.obj.name.startswith("HAIR_Long"):part.obj.hide_render=True
        obj.location=location;obj.rotation_euler=(Vector(focus)*height_scale-obj.location).to_track_quat("-Z","Y").to_euler();camera.ortho_scale=size
        mapping.inputs[2].default_value=((cell%8)*.125,(3-cell//8)*.25,0)
        scene.render.resolution_x=600;scene.render.resolution_y=780 if name in ("CanneryWoman","BodyFront","BodyBack","BodyProfile","Working","CuffCheck") else 600
        scene.render.filepath=str(sources/(name+".png"));bpy.ops.render.render(write_still=True)
        for part in result.parts:
            if part.obj.name.startswith("HAIR_Long"):part.obj.hide_render=False
    if result.rig.animation_data:result.rig.animation_data.action=None
    base.reset_pose(result.rig)
    result.root.scale=(1,1,1);bpy.context.view_layer.update()


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/City/Cannery/Woman")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/City/CanneryWoman")
    parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true")
    parser.add_argument("--preview-only",action="store_true",help="Review changed geometry immediately, without republishing FBX/action banks.")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    wardrobe=args.model_dir/"CanneryWomanAtlas.png";faces=args.model_dir/"CanneryWomanFaceAtlas.png"
    # Preview textures belong to the editable source package, so art review
    # can run without rewriting any files Unity may currently be importing.
    if args.preview_only:wardrobe=args.source_dir/wardrobe.name;faces=args.source_dir/faces.name
    wh=wardrobe_atlas(wardrobe,args.validate_only);fh=face_atlas(faces,args.validate_only)
    result=WomanBuilder().build();body=manifest(result,wh,fh)
    print("CanneryWoman geometry",body["triangle_count"],"triangles",body["mesh_count"],"meshes",flush=True)
    if args.preview_only:
        args.source_dir.mkdir(parents=True,exist_ok=True)
        print("Layer clearance rest",json.dumps(mesh_clearance(result)),flush=True)
        for name,_,_ in CLIPS:
            for sample in range(9):
                action_pose(result,name,sample/8)
                if name=="CanneryWomanWork":resident.solve_grips(result.rig,{side:(sign*.17,-.54,1.02) for side,sign in (("L",1),("R",-1))})
                data=mesh_clearance(result)
                if min(data.values())<.001:print("Layer clearance concern",name,sample/8,json.dumps(data),flush=True)
        base.reset_pose(result.rig)
        previews(result,args.source_dir,wardrobe,faces,pose_only=True)
        return
    if not args.validate_only:base.export_fbx(args.model_dir/"CanneryWoman.fbx",result)
    actions=make_actions(result)
    for name,payload in (("CanneryWoman",body),("CanneryWomanActions",actions)):
        publish_bytes(args.model_dir/(name+".json"),(json.dumps(payload,indent=2)+"\n").encode(),args.validate_only)
    if not args.validate_only:
        base.export_animation_fbx(args.model_dir/"CanneryWomanActions.fbx",result)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        if not args.no_preview:previews(result,args.source_dir,wardrobe,faces)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"CanneryWoman.blend"),check_existing=False)
        for asset in sorted(args.model_dir.iterdir()):
            if asset.suffix not in (".png",".fbx",".json"):continue
            meta=asset.with_suffix(asset.suffix+".meta")
            if not meta.exists():
                guid=hashlib.sha256(asset.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]
                meta.write_text("fileFormatVersion: 2\nguid: "+guid+"\n",encoding="utf8")
    print("CANNERY WOMAN ART CONTRACT OK: 31 compatible body bones + 12 hair bones, painted 32-cell face, articulated work hands, grounded seamless loops",flush=True)
    print(json.dumps(actions["validation"]),flush=True)


if __name__=="__main__":main()
