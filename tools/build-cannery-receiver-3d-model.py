#!/usr/bin/env python3
"""Cannery receiver: measured athletic male anatomy, painted face and real glasses.

The shared production builder/exporter retains its body bone identities; this
actor owns his rest landmarks, continuous workwear and five in-place actions.
Run through tools/run-blender.py. No Unity geometry is assembled at runtime.
"""
from __future__ import annotations
import argparse
from collections import Counter
from dataclasses import replace
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).parent))
spec = importlib.util.spec_from_file_location("receiver_resident_base", Path(__file__).with_name("build-village-residents-3d-model.py"))
resident = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = resident
spec.loader.exec_module(resident)
base = resident.base
from player_3d_model_common import hex_to_linear_rgba
from atlas_kit import PixelCanvas

VERSION = "1.1.0"
FPS = 24
HEIGHT = 1.96
DRESSED_HEIGHT = 1.984
HAND_SCALE = 1.18
TROUSER_RISE_DROP = .055
TROUSER_FRONT_EASE = .018
FACE_NAME = "GEO_FaceSurface"
CLIPS = (("CanneryReceiverIdle", 8., True), ("CanneryReceiverWalk", 1.5, True),
         ("CanneryReceiverWork", 6., True), ("CanneryReceiverListen", 5., True),
         ("CanneryReceiverBreak", 11., True))
COLORS = {"skin":"BA9C85", "shirt":"687366", "shirt_edge":"596455",
          "pants":"615D50", "belt":"493C2F", "buckle":"898375",
          "hair":"514735", "hair_light":"625541", "hair_dark":"3E372D",
          "shoe":"403C33", "sole":"2E2D27", "glasses":"292B26", "beanie":"E58B3C", "beanie_cuff":"CF7430", "white":"FFFFFF"}
SKIN = (186,156,133,255)
GLASSES_BRIDGE = (0,-.165,1.805)


def receiver_skeleton():
    """A broad male rig in metres, not a scaled female body or animation bank."""
    def point(p):
        x,y,z=p
        if abs(x)>.15 and z>1:
            x=math.copysign(.25+(abs(x)-.21)*1.08,x)
        elif z<1.:
            x*=1.5
        else:x*=1.12
        return (x,y*1.16,z*1.12)
    return tuple(replace(b,head=point(b.head),tail=point(b.tail)) for b in base.npc_v2_bone_specs())


base.SKELETON=receiver_skeleton()
base.BONE_BY_NAME={b.name:b for b in base.SKELETON}


def hash_json(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(",", ":")).encode()).hexdigest()


def publish_bytes(path,data,validate_only):
    if validate_only:
        if not path.is_file() or path.read_bytes()!=data:raise RuntimeError("Deterministic asset differs: "+str(path))
    else:
        path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
    return hashlib.sha256(data).hexdigest()


def face_tile(expression=0,mouth=0,smug=0):
    """Heavy brow, blue-grey eyes, receding hair and beard are all painted.

    The 32 cells retain the shared blink/brow/mouth indexing. There is no
    independent facial geometry behind the spectacles or a duplicate skull.
    """
    c=PixelCanvas(64,64)
    for y in range(64):
        for x in range(64):
            light=4*math.exp(-((x-26)/22)**2-((y-19)/27)**2)
            shade=8*(abs(x-31.5)/32)**2+4*max(0,(y-52)/12)
            dither=((x*11+y*13)%13==0)-((x*7+y*3)%19==0)
            c.put(x,y,tuple(max(0,min(255,round(v+light-shade+dither))) for v in SKIN[:3])+(255,))
    # Unequal recession above the temples leaves a high forehead open.
    for x in range(64):
        depth=max(0,round((abs(x-30)-17)*.65))+int(x<5 or x>58)*3
        for y in range(depth):c.put(x,y,(77+(x+y)%7,66+(x+y)%7,49+(x+y)%7,255))
    for y in (9,12):c.line(20,y,43,y-1,(176,145,122,255))
    beard=(85,74,59,255);beard_light=(111,94,74,255)
    # A broad untrimmed beard reads as pigment on chin/jaw, with warm cheek
    # notches; sparse stable pixel clusters give coarse hair direction.
    for y in range(36,64):
        for x in range(3,61):
            edge=42+round(7*math.exp(-((x-32)/13)**2))
            jaw=abs(x-32)>23 and y>36
            if y>=edge or jaw:
                n=((x*5+y*11)%13)
                color=beard if n<7 else beard_light if n==12 else (97,81,63,255)
                c.put(x,y,color)
    for side,cx in enumerate((20,43)):
        by=21+side-(2 if expression==3 and side==1 else 0)+(1 if expression==4 else 0)
        c.line(cx-7,by,cx-1,by-1,(76,63,48,255),2)
        c.line(cx-1,by-1,cx+6,by+1,(76,63,48,255),2)
        ey=29+side
        if expression==2:
            c.line(cx-6,ey,cx,ey+1,(93,72,57,255));c.line(cx,ey+1,cx+6,ey,(93,72,57,255))
        else:
            h=1 if expression in (1,4) else 2
            c.ellipse(cx,ey,6,h,(194,184,163,255))
            c.ellipse(cx+1,ey,2,h,(99,125,128,255))
            c.line(cx+1,ey-h,cx+1,ey+h,(48,56,54,255))
            c.put(cx,ey-1,(189,197,183,255))
            c.line(cx-6,ey-2,cx+5,ey-2,(99,77,60,255))
            c.line(cx-5,ey+4,cx+5,ey+4,(159,126,102,255))
    # Large but wholly drawn nose; its tip is warm and blunt.
    c.line(31,30,29,39,(160,126,101,255));c.line(34,30,36,39,(198,166,138,255))
    c.ellipse(32,40,5,3,(176,133,108,255));c.line(28,41,30,42,(113,83,63,255))
    c.line(35,41,37,40,(113,83,63,255));c.line(31,39,34,39,(201,159,129,255))
    # Moustache leaves the mouth readable, including all six speech shapes.
    c.line(24,45,31,44,beard,2);c.line(33,44,41,45,beard,2)
    lip=(152,112,89,255);dark=(62,43,34,255)
    if mouth==0:
        lift=1+smug
        c.line(24,49,33,49,dark);c.line(33,49,42,49-lift,dark)
        c.line(27,51,36,51,lip);c.line(36,51,40,50-lift,lip)
        if smug:c.line(42,45,43,48-lift,(139,107,80,255))
    else:
        rx,ry=((7,2),(6,4),(4,4),(8,3),(7,2))[mouth-1]
        c.ellipse(32,50,rx+1,ry+1,lip);c.ellipse(32,50,rx,ry,dark)
        if mouth in (2,4,5):c.line(32-rx+2,49-ry//2,32+rx-2,49-ry//2,(201,190,165,255))
        if mouth in (2,4):c.line(29,50+ry-1,35,50+ry-1,(144,93,78,255))
    return c


def face_atlas(path,validate_only=False):
    c=PixelCanvas(512,256)
    tiles=[face_tile(i//6,i%6) for i in range(30)]+[face_tile(smug=1),face_tile(3,0,2)]
    for i,tile in enumerate(tiles):
        for y in range(64):
            offset=(((i//8)*64+y)*512+i%8*64)*4
            c.pixels[offset:offset+256]=tile.pixels[y*256:(y+1)*256]
    return publish_bytes(path,c.png_bytes(),validate_only)


def wardrobe_atlas(path,validate_only=False):
    c=PixelCanvas(256,256);c.rect(0,0,256,256,(255,255,255,255))
    # Shirt front/back: fine washed jersey, chest tension, armpit fans,
    # stretched rib neckline and uneven hem. No insignia or printed joke.
    for y in range(128):
        for x in range(128):
            local=x%64;z=1.09+(125-y)/123*.55;px=(local-32)/31*.29
            shade=5*math.exp(-((z-1.39)/.022)**2)*(1-(px/.32)**2)
            tension=7*math.exp(-((abs(px)-(.27-(1.60-z)*.40))/.016)**2-((z-1.47)/.13)**2)
            n=round(max(205,min(255,246+(x*11+y*17)%7-shade-tension)))
            c.put(x,y,(n,n,n,255))
    for start in (0,64):
        c.line(start+5,117,start+59,115,(199,201,187,255));c.line(start+6,119,start+59,117,(229,228,213,255))
        for sign in (-1,1):
            x=start+32+sign*25
            for d in (0,5,10):c.line(x,29+d,x-sign*(8+d//2),38+d,(218,220,206,255))
    # Hair upper right: thick disordered short strands with stable dither.
    for y in range(128):
        for x in range(128,256):
            n=221+(x*5+y*3)%26;c.put(x,y,(n,n,n,255))
    for x in range(132,254,7):
        for y in range(3,124):
            shift=round(5*math.sin(y*.033+x*.071));c.put(x+shift,y,(197,196,184,255))
    # Compact jersey-knit cell; repeated quiet ribs are drawn rather than
    # inflated into hundreds of decorative mesh strips. The lower cuff uses
    # the same stitches with its own folded cloth thickness and tint.
    for y in range(128):
        for x in range(192,256):
            rib=(x-192)%4;stitch=(y+(2 if rib>=2 else 0))%6
            n=212 if rib==0 else 236 if rib==3 else 249
            if stitch in (2,3) and rib in (1,2):n-=15
            c.put(x,y,(n,n,n,255))
    # Trousers front/back with side seams, fly, angled pockets, knee wear.
    for y in range(128,256):
        for x in range(256):
            u=x%128;wear=round(14*math.exp(-((abs(u-64)-32)/17)**2-((y-204)/15)**2))
            n=min(255,231+(x+y*2)%8+wear);c.put(x,y,(n,n,n,255))
    for start in (0,128):
        c.line(start+7,133,start+9,252,(183,185,172,255));c.line(start+120,132,start+119,252,(183,185,172,255))
        c.line(start+63,134,start+63,155,(163,166,149,255))
        c.line(start+65,135,start+65,154,(210,212,197,255))
        for sign in (-1,1):
            c.line(start+64+sign*13,148,start+64+sign*8,157,(208,210,195,255))
            c.line(start+64+sign*8,157,start+64+sign*3,160,(219,220,206,255))
        for x in (start+30,start+97):
            c.line(x-12,187,x+9,190,(202,205,188,255));c.line(x-8,233,x+11,235,(197,202,184,255))
    c.line(9,143,31,160,(169,174,151,255));c.line(118,143,96,160,(169,174,151,255))
    for x in (143,211):
        c.line(x,146,x+27,146,(164,173,151,255));c.line(x,146,x+2,172,(186,189,168,255))
        c.line(x+2,172,x+15,176,(189,191,170,255));c.line(x+15,176,x+27,171,(189,191,170,255))
        c.line(x+27,171,x+27,146,(186,189,168,255))
    c.rect(0,0,4,4,(255,255,255,255))
    # Permanent skin occupies separate cells beside the replaceable outfit.
    # Its tattoos remain on the body when another outfit is equipped later.
    atlas=PixelCanvas(512,256);atlas.rect(0,0,512,256,(255,255,255,255))
    for y in range(256):atlas.pixels[y*512*4:(y*512+256)*4]=c.pixels[y*256*4:(y+1)*256*4]
    ink=(88,125,155,255);faded=(145,170,184,255)
    # A crude outlined fist with a single raised finger, kept small and worn.
    x=256
    for a,b,cx,d in ((60,172,60,156),(60,156,64,155),(64,155,66,158),(66,158,66,175),
                     (60,171,56,170),(56,170,54,174),(54,174,54,186),(54,186,59,192),
                     (59,192,69,192),(69,192,74,186),(74,186,74,175),(74,175,70,172),
                     (70,172,67,175),(57,174,57,181),(62,176,62,182),(67,176,67,182),
                     (72,181,65,185),(65,185,63,189)):
        atlas.line(x+a,b,x+cx,d,ink,2)
    # Unequal angry brows and an exposed row of teeth form a coarse mug.
    x=384;atlas.ellipse(x+64,176,12,14,ink);atlas.ellipse(x+64,176,9,11,(255,255,255,255))
    atlas.line(x+56,168,x+62,173,ink,2);atlas.line(x+72,169,x+66,173,ink,2)
    atlas.rect(x+58,173,x+61,176,ink);atlas.rect(x+67,173,x+70,176,ink)
    atlas.line(x+63,174,x+62,179,ink);atlas.line(x+62,179,x+65,179,ink)
    atlas.rect(x+56,181,x+73,187,ink);atlas.rect(x+58,182,x+71,185,(255,255,255,255))
    for tooth in (61,65,68):atlas.line(x+tooth,182,x+tooth,185,ink)
    # Two smaller upper-arm marks sit well below the sleeve hem. The large
    # untouched band between them and the forearm drawings remains bare skin.
    white=(255,255,255,255);x=256
    atlas.ellipse(x+64,49,10,9,ink);atlas.ellipse(x+64,48,7,6,white)
    atlas.rect(x+58,54,x+71,61,ink);atlas.rect(x+60,55,x+69,59,white)
    atlas.ellipse(x+60,48,2,2,ink);atlas.ellipse(x+68,49,2,2,ink)
    atlas.line(x+64,51,x+63,54,ink);atlas.put(x+65,54,ink)
    for tooth in (62,65,68):atlas.line(x+tooth,56,x+tooth,60,ink)
    x=384;brick=(198,105,90,255)
    heart=((64,46),(61,43),(57,43),(54,46),(54,50),(57,54),
           (64,60),(71,54),(74,50),(74,46),(71,43),(67,43))
    for y in range(43,61):
        for local in range(54,75):
            inside=False
            for i,(ax,ay) in enumerate(heart):
                bx,by=heart[(i+1)%len(heart)]
                if (ay>y+.5)!=(by>y+.5) and local+.5<(bx-ax)*(y+.5-ay)/(by-ay)+ax:inside=not inside
            if inside:atlas.put(x+local,y,brick)
    for i,(ax,ay) in enumerate(heart):
        bx,by=heart[(i+1)%len(heart)];atlas.line(x+ax,ay,x+bx,by,ink)
    atlas.line(x+50,61,x+78,41,ink)
    atlas.line(x+72,42,x+78,41,ink);atlas.line(x+78,41,x+76,47,ink)
    atlas.line(x+50,61,x+49,56,ink);atlas.line(x+50,61,x+56,61,ink)
    atlas.line(x+53,59,x+52,54,ink);atlas.line(x+53,59,x+59,59,ink)
    original=bytes(atlas.pixels)
    for offset in (256,384):
        for y in range(30,74):
            for local in range(42,87):
                source_x=offset+round(64+(local-64)/1.5);source_y=round(51+(y-51)/1.5)
                pixel=(source_y*512+source_x)*4
                atlas.put(offset+local,y,tuple(original[pixel:pixel+4]))
    # Sparse chips soften all four rough drawings. Ink is compensated for
    # the shared skin multiplication; no second skin material is required.
    for offset in (256,384):
        for y in (*range(30,74),*range(153,194)):
            for local in range(51,78):
                p=((y*512)+offset+local)*4
                if atlas.pixels[p:p+3]==bytes(ink[:3]) and (local*7+y*11)%19==0:
                    atlas.put(offset+local,y,faded)
    return publish_bytes(path,atlas.png_bytes(),validate_only)


def outward_box(center,size):
    v,f=base.make_box(center,size);return v,[tuple(reversed(p)) for p in f]


def tube_path(points,radii,sides=10,ovals=None):
    vertices=[]
    for i,(p,r) in enumerate(zip(points,radii)):
        direction=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        q=direction.to_track_quat("Z","Y")
        for j in range(sides):
            a=j*math.tau/sides;oval=ovals[i] if ovals else .80
            vertices.append(tuple(Vector(p)+q@Vector((r*math.cos(a),r*oval*math.sin(a),0))))
    faces=[tuple(reversed(range(sides)))]
    for i in range(len(points)-1):
        for j in range(sides):
            k=i*sides+j;n=i*sides+(j+1)%sides;faces.append((k,n,n+sides,k+sides))
    faces.append(tuple((len(points)-1)*sides+j for j in range(sides)))
    return vertices,faces


class ReceiverBuilder(base.PedestrianBuilder):
    def __init__(self):super().__init__(None)
    def remap_geometry_point(self,point,bone_name,role,name):return Vector(point)
    def add(self,name,geometry,bone,color,role="clothing"):
        obj=self.add_part(name,geometry,bone,role,color)
        for polygon in obj.data.polygons:polygon.use_smooth=len(polygon.vertices)==4
        return obj

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

    def head(self):
        rows=((1.657,.049,.052,-.017),(1.678,.072,.073,-.017),(1.702,.093,.091,-.017),
              (1.726,.110,.104,-.017),(1.750,.117,.112,-.017),(1.776,.120,.119,-.017),
              (1.802,.121,.123,-.017),(1.827,.120,.122,-.017),(1.852,.117,.118,-.017),
              (1.877,.109,.109,-.017),(1.902,.094,.095,-.017),(1.925,.069,.073,-.017))
        face_angles=[math.radians(-68+i*136/10) for i in range(11)]
        back_angles=[math.radians(68+i*224/16) for i in range(17)]
        def surface(angles):
            vertices=[(rx*math.sin(a),cy-ry*math.cos(a),z) for z,rx,ry,cy in rows for a in angles]
            n=len(angles);faces=[(j*n+i,j*n+i+1,(j+1)*n+i+1,(j+1)*n+i) for j in range(len(rows)-1) for i in range(n-1)]
            return vertices,faces
        obj=self.add(FACE_NAME,surface(face_angles),"head","white","facial_atlas")
        uv=obj.data.uv_layers.new(name="UVMap")
        for loop in obj.data.loops:uv.data[loop.index].uv=(loop.vertex_index%11/10,loop.vertex_index//11/11)
        obj["bp_face_atlas_renderer"]=True;obj["bp_uv_contract"]="local_0_1_runtime_cell_scale_offset"
        self.add("GEO_Head",surface(back_angles),"head","skin","body")
        for part in self.result.parts:
            points=[part.obj.location+v.co for v in part.obj.data.vertices]
            part.obj.data.normals_split_custom_set_from_vertices([Vector((p.x/.12,(p.y+.017)/.12,(p.z-1.795)/.14)).normalized() for p in points])
        self.add("GEO_Neck",base.make_vertical_shell(((1.535,.107,.093,.004),(1.572,.102,.090,0),(1.610,.086,.080,-.009),(1.653,.075,.070,-.013),(1.69,.066,.064,-.013)),20),"neck","skin","body")
        for side,sign in (("L",1),("R",-1)):
            self.add("GEO_Ear."+side,base.make_ellipsoid((sign*.122,-.006,1.786),(.020,.023,.039),12,7),"head","skin","body_detail")
            self.add("GEO_EarFold."+side,tube_path(((sign*.128,-.022,1.804),(sign*.133,-.026,1.792),(sign*.129,-.024,1.776)),(.004,.005,.004),6),"head","skin","body_detail")
        self.add("HAIR_Crown",base.make_ellipsoid((0,.012,1.912),(.116,.117,.048),20,8),"head","hair","hair")
        # Attached occipital shell, ending above the thick neck. Recession is
        # authored in the upper side profile as well as on the face drawing.
        angles=[math.radians(66+i*228/16) for i in range(17)];vertices=[]
        for row in range(6):
            t=row/5
            for a in angles:
                lower=1.726+.027*abs(math.cos(a));z=lower+(1.925-lower)*t
                lo,hi=next((lo,hi) for lo,hi in zip(rows,rows[1:]) if lo[0]<=z<=hi[0])
                blend=(z-lo[0])/(hi[0]-lo[0]);rx=lo[1]+(hi[1]-lo[1])*blend+.005;ry=lo[2]+(hi[2]-lo[2])*blend+.005
                vertices.append((rx*math.sin(a),-.017-ry*math.cos(a),z))
        faces=[(j*17+i,j*17+i+1,(j+1)*17+i+1,(j+1)*17+i) for j in range(5) for i in range(16)]
        self.add("HAIR_Nape",(vertices,faces),"head","hair_dark","hair")
        for i in range(7):
            x=(i-3)*.028;y=-.060+.010*math.sin(i*1.7);z=1.933-.010*abs(i-3)
            self.add("HAIR_TousledLock"+str(i),tube_path(((x+.009,y+.016,z-.014),(x-.007,y,z+.013),(x-.018,y-.036,z-.007)),(.017,.014,.004),8),"head","hair_light" if i%3==0 else "hair","hair")
        self.glasses()

    def glasses(self):
        # Panto rims are genuine closed solid swept rings. The open centres
        # preserve painted eyes through the game's opaque shared material.
        # Clear lens surfaces are deliberately omitted: no opaque white discs.
        for side,sign in (("L",1),("R",-1)):
            vertices=[];faces=[];n=24;k=6
            for i in range(n):
                a=i*math.tau/n
                x=sign*.054+.046*math.cos(a);z=1.797+.034*math.sin(a)
                y=-.166+.012*abs(x)/.10
                for j in range(k):
                    b=j*math.tau/k;r=.0058
                    vertices.append((x+r*math.cos(b)*math.cos(a),y+r*math.sin(b),z+r*math.cos(b)*math.sin(a)))
            for i in range(n):
                for j in range(k):
                    a=i*k+j;b=i*k+(j+1)%k;c=((i+1)%n)*k+(j+1)%k;d=((i+1)%n)*k+j
                    faces.append((a,b,c,d))
            self.add("GEO_GlassesFrames."+side,(vertices,faces),"head","glasses","accessory")
            self.add("GEO_GlassesTemple."+side,tube_path(((sign*.098,-.154,1.809),(sign*.125,-.089,1.811),(sign*.143,-.013,1.801),(sign*.135,.014,1.783)),(.005,.0045,.0045,.005),8),"head","glasses","accessory")
        self.add("GEO_GlassesBridge",tube_path(((-.012,-.164,1.807),(0,-.168,1.813),(.012,-.164,1.807)),(.0048,.0048,.0048),8),"head","glasses","accessory")
        self.create_bone_anchor("ANCHOR_Glasses","head",GLASSES_BRIDGE,(0,-1,0))

    def beanie(self):
        # A soft close-fitting ski cap with a real turned-up cuff. It covers
        # the permanent crown and its small tufts without deleting that hair
        # from the actor: a future outfit may show the same hair again.
        bpy.context.view_layer.update()
        ear_top=max((p.obj.matrix_world@v.co).z for p in self.result.parts if p.obj.name.startswith("GEO_Ear.") for v in p.obj.data.vertices)
        side_drop=1.854+.016*(.01-.005)/.14-(ear_top+.001)
        stations=((1.894,.137,.145,.018),(1.935,.140,.145,.025),
                  (1.966,.129,.090,.045),(1.976,.105,.040,.082),
                  (1.914,.065,.024,.118),(1.864,.026,.016,.126))
        crown=base.make_vertical_shell(stations,16);shaped=[]
        # Cross-sections turn with the fold: the terminal seam faces back and
        # down, not up. This rounds the floppy tail below the middle crown and
        # removes the pointed ledge a stack of horizontal rings would create.
        for row,(z,rx,ry,cy) in enumerate(stations):
            tilt=math.radians((0,0,30,60,145,155)[row])
            for i in range(16):
                angle=i*math.tau/16
                fold=(.005*math.sin(angle*3+row*1.8)+.002*math.cos(angle*5-row))*math.cos(angle)**2 if row in (1,2,3,4) else 0
                if row>=3:fold*=.5
                across=(ry+fold)*math.sin(angle)
                x=(rx+fold)*math.cos(angle);y=cy+across*math.cos(tilt);height=z-across*math.sin(tilt)
                rear=max(0,math.sin(angle))
                height-=(.095,.060,.023,.008,0,0)[row]*rear**1.4
                if row<2:
                    blend=rear**1.2
                    # Pull the back wall to the actual occipital envelope,
                    # not merely down in empty space behind the hairstyle.
                    fit_x=(.127 if row==0 else .137)*math.cos(angle)
                    fit_y=-.017+(.135 if row==0 else .155)*math.sin(angle)
                    x=x*(1-blend)+fit_x*blend;y=y*(1-blend)+fit_y*blend
                # Compress only the remaining upper dome into a shallow
                # fabric crown; the low rear fit and the cuff stay authored.
                t=max(0,min(1,(height-1.95)/.061))
                height-=.027*t*t*(3-2*t)
                shaped.append((x,y,height))
        # Extend the wall down to the ears while retaining the original roof
        # over the permanent hair. Lowering that roof would cut through it.
        lower=[(x*.997,y,z-max(.002,side_drop*abs(math.cos(i*math.tau/16))**3)) for i,(x,y,z) in enumerate(shaped[:16])]
        crown_faces=[tuple(reversed(range(16)))]
        crown_faces.extend((i,(i+1)%16,(i+1)%16+16,i+16) for i in range(16))
        crown_faces.extend(tuple(index+16 for index in face) for face in crown[1][1:])
        crown=(lower+shaped,crown_faces)
        self.add("CLO_SkiBeanie",crown,"head","beanie")
        cuff=base.make_vertical_shell(((1.854,.135,.144,.005),
            (1.880,.141,.152,.008),(1.909,.147,.157,.011)),16)
        fitted=[]
        for index,(x,y,z) in enumerate(cuff[0]):
            row=index//16;angle=index%16*math.tau/16;rear=max(0,math.sin(angle));blend=rear**1.2
            height=z+.016*max(0,min(1,-(y-.01)/.14))-.095*rear**1.4
            height-=side_drop*abs(math.cos(angle))**3
            fit_x=(.128+row*.002)*math.cos(angle);fit_y=-.017+(.137+row*.002)*math.sin(angle)
            fitted.append((x*(1-blend)+fit_x*blend,y*(1-blend)+fit_y*blend,height))
        cuff=(fitted,cuff[1])
        self.add("CLO_BeanieCuff",cuff,"head","beanie_cuff")

    def arm(self,side):
        upper="upper_arm."+side;lower="forearm."+side
        a=Vector(base.BONE_BY_NAME[upper].head);b=Vector(base.BONE_BY_NAME[lower].head);c=Vector(base.BONE_BY_NAME[lower].tail)
        # The skin begins inside the sleeve, below its sewn shoulder. Keeping
        # a hidden full upper-arm cap would expose it above the inset seam.
        points=[a.lerp(b,t) for t in (.40,.6,.82,1.)]+[b.lerp(c,t) for t in (.18,.38,.62,.83,1.)]
        radii=(.095,.106,.088,.058,.078,.077,.062,.044,.029)
        obj=self.add("GEO_Arm."+side,tube_path(points,radii,16),upper,"skin","body")
        for vg in list(obj.vertex_groups):obj.vertex_groups.remove(vg)
        groups={n:obj.vertex_groups.new(name=n) for n in (upper,lower)}
        for v in obj.data.vertices:
            blend=max(0,min(1,(v.index//16-2)/2));groups[upper].add([v.index],1-blend,"REPLACE");groups[lower].add([v.index],blend,"REPLACE")
        # A rounded deltoid belongs to the upper arm, not to an extended
        # rectangular chest. Seven rings describe the shoulder head, muscular
        # belly and short fitted cuff; its hidden inner seam shares the chest.
        sides=14;pts=[a.lerp(b,t) for t in (0,.045,.13,.26,.38,.47,.55)]
        sleeve=self.add("CLO_ShortSleeve."+side,tube_path(pts,(.014,.045,.080,.107,.116,.112,.106),sides),upper,"shirt")
        for vg in list(sleeve.vertex_groups):sleeve.vertex_groups.remove(vg)
        ga=sleeve.vertex_groups.new(name=upper);gc=sleeve.vertex_groups.new(name="chest")
        # Only the buried inner edge follows the chest. Inverse skinning keeps
        # that blend from changing the authored rounded hanging silhouette.
        rig=self.result.rig;base.reset_pose(rig);base.apply_pose(rig,base.CITIZEN_HANGING_ARMS);bpy.context.view_layer.update()
        arm=rig.pose.bones[upper].matrix@rig.data.bones[upper].matrix_local.inverted()
        chest=rig.pose.bones["chest"].matrix@rig.data.bones["chest"].matrix_local.inverted()
        for vertex in sleeve.data.vertices:
            row=vertex.index//sides;desired=arm@(sleeve.matrix_world@vertex.co)
            weight=max(0,min(1,(.212-abs(desired.x))/.050))*max(0,1-row/5)
            ga.add([vertex.index],1-weight,"REPLACE")
            if weight:gc.add([vertex.index],weight,"REPLACE")
            skin=chest*weight+arm*(1-weight)
            vertex.co=sleeve.matrix_world.inverted()@skin.inverted()@desired
        base.reset_pose(rig);bpy.context.view_layer.update()
        end=a.lerp(b,.55);direction=(b-a).normalized()
        self.add("CLO_SleeveHem."+side,base.make_frustum_between(end-direction*.008,end+direction*.008,.107,.107,16,.8),upper,"shirt_edge")
        self.hand(side)

    def hand(self,side):
        bone="hand."+side;sign=1 if side=="L" else -1
        a=Vector(base.BONE_BY_NAME[bone].head);b=Vector(base.BONE_BY_NAME[bone].tail);q=(b-a).to_track_quat("Z","Y")
        def world(p):return tuple(a+q@(Vector(p)*(1.17*HAND_SCALE)))
        def radii(values):return tuple(r*HAND_SCALE for r in values)
        self.create_bone_anchor("ANCHOR_HandFingers."+side,bone,world((0,0,.10)),q@Vector((0,0,1)))
        self.create_bone_anchor("ANCHOR_HandPalm."+side,bone,world((0,-.04,0)),q@Vector((0,-1,0)))
        self.add("GEO_Palm."+side,base.make_ellipsoid(world((0,0,.042)),radii((.0495,.0287,.0621)),12,7,orientation=q),bone,"skin","body")
        # Both hands retain anatomical identities: index, middle, ring, little.
        # In the hanging rig -sign*X faces forward; -Y is the inward palm side.
        for i in range(4):
            x=sign*(i-1.5)*.020;length=(.051,.059,.055,.042)[i]
            pts=[world((x,0,.074)),world((x,-.003,.089)),world((x,-.015,.087+length*.50)),world((x,-.023,.078+length))]
            self.add("GEO_Finger"+str(i)+"."+side,tube_path(pts,radii((.0118,.0112,.0099,.0081)),6),bone,"skin","body_detail")
            if side=="R" and i==0:
                self.create_bone_anchor("ANCHOR_GlassesFinger",bone,pts[-1],Vector(pts[-1])-Vector(pts[-2]))
        pts=[world((-sign*.034,-.003,.019)),world((-sign*.056,-.012,.039)),world((-sign*.056,-.025,.064)),world((-sign*.042,-.029,.078))]
        self.add("GEO_Thumb."+side,tube_path(pts,radii((.0157,.0146,.0123,.0101)),8),bone,"skin","body_detail")

    def trousers(self):
        sides=24;vertices=[];faces=[];weights=[];tops={}
        stations=((.13,.143,.063,.068),(.25,.141,.072,.078),(.40,.140,.080,.085),(.544,.139,.074,.078),
                  (.66,.136,.093,.099),(.78,.133,.108,.114),(.90,.131,.113,.122),(.960,.130,.112,.125))
        for side,sign in (("L",1),("R",-1)):
            start=len(vertices)
            for z,cx,rx,ry in stations:
                for j in range(sides):
                    angle=math.tau*j/sides;vertices.append((sign*cx+rx*math.cos(angle),.006+ry*math.sin(angle),z))
                    knee=max(0,min(1,(z-.47)/.15));hip=max(0,min(1,(z-.85)/.15))
                    weights.append({"shin."+side:1-knee,"thigh."+side:knee*(1-hip),"pelvis":knee*hip})
            for row in range(len(stations)-1):
                for j in range(sides):
                    a=start+row*sides+j;b=start+row*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
            faces.append(tuple(reversed([start+j for j in range(sides)])))
            tops[side]=[start+(len(stations)-1)*sides+j for j in range(sides)]
        boundary=[tops["L"][j%sides] for j in range(15,34)]+[tops["R"][j%sides] for j in range(3,22)]
        li=[tops["L"][j] for j in range(9,16)];ri=[tops["R"][j%sides] for j in range(27,20,-1)]
        for i in range(6):faces.append((li[i],li[i+1],ri[i+1],ri[i]))
        # A shared front seam gives the longer rise a continuous cloth surface
        # over the centre, instead of stretching two disconnected thigh fronts.
        mid=len(vertices);vertices.append(tuple((Vector(vertices[boundary[-1]])+Vector(vertices[boundary[0]]))*.5))
        weights.append({n:(weights[boundary[-1]].get(n,0)+weights[boundary[0]].get(n,0))*.5 for n in ("pelvis","thigh.L","thigh.R")})
        a,b,c,d=faces[-1];faces[-1]=(a,b,mid,c,d);boundary.append(mid)
        previous=boundary;angles=[math.atan2((vertices[i][1]-.006)/.125,vertices[i][0]/.242) for i in boundary]
        for z,rx,ry,cy in ((.99,.235,.132,.009),(1.03,.218,.131,.007),(1.07,.202,.126,.002),(1.105,.188,.125,-.002),(1.128,.186,.125,-.004)):
            ring=[]
            for angle in angles:
                ring.append(len(vertices));vertices.append((rx*math.cos(angle),cy+ry*math.sin(angle),z));weights.append({"pelvis":1.})
            for i in range(len(ring)):
                j=(i+1)%len(ring);faces.append((previous[i],previous[j],ring[j],ring[i]))
            previous=ring
        faces.append(tuple(previous))
        shaped=[]
        for x,y,z in vertices:
            drop=TROUSER_RISE_DROP*max(0,min(1,(z-.78)/.18,(1.10-z)/.14))
            z-=drop
            front=max(0,min(1,-(y-.006)/.08))
            taper=max(0,min(1,(1.09-z)/.04))
            ease=TROUSER_FRONT_EASE*math.exp(-(x/.065)**2-((z-.99)/.072)**2)*front*taper
            shaped.append((x,y-ease,z))
        obj=self.add("CLO_WorkTrousers",(shaped,faces),"pelvis","pants")
        for vg in list(obj.vertex_groups):obj.vertex_groups.remove(vg)
        groups={n:obj.vertex_groups.new(name=n) for n in ("pelvis","thigh.L","thigh.R","shin.L","shin.R")}
        for i,assignment in enumerate(weights):
            for n,w in assignment.items():
                if w:groups[n].add([i],w,"REPLACE")
        self.add("CLO_LeatherBelt",base.make_vertical_shell(((1.09,.202,.131,-.002),(1.12,.193,.133,-.004)),32),"pelvis","belt")
        for x,z,w,h in ((-.021,1.107,.005,.031),(.021,1.107,.005,.031),(0,1.121,.043,.005),(0,1.093,.043,.005),(0,1.107,.035,.004)):
            self.add("CLO_BeltBuckle"+str(len(self.result.parts)),outward_box((x,-.142,z),(w,.006,h)),"pelvis","buckle")
        for angle in (-math.pi/2-.6,-math.pi/2+.6,0,math.pi/2,math.pi):
            self.add("CLO_BeltLoop"+str(len(self.result.parts)),outward_box((.198*math.cos(angle),-.004+.136*math.sin(angle),1.108),(.016,.009,.045)),"pelvis","pants")

    def build(self):
        self.reset_scene();collection=bpy.data.collections.new("EXPORT_CanneryReceiver");bpy.context.scene.collection.children.link(collection)
        base.PALETTE.update({n:hex_to_linear_rgba(c) for n,c in COLORS.items()})
        material=self.create_shared_material();root=bpy.data.objects.new("ROOT_Player",None);collection.objects.link(root)
        rig=self.create_armature(collection,root);self.result=base.BuildResult(root,rig,collection,material)
        self.head();self.beanie();self.trousers()
        # A heavy athletic V: narrow belt/waist, tapering lower ribs, broad
        # upper chest and lats. Mass is carried high, never as a round belly.
        # The front depth peaks over the pectorals and falls at the abdomen.
        stations=((1.085,.181,.117,-.003),(1.115,.187,.124,-.005),(1.15,.191,.128,-.007),
                  (1.19,.194,.130,-.009),(1.24,.201,.135,-.010),(1.29,.215,.143,-.014),
                  (1.34,.232,.155,-.018),(1.39,.242,.166,-.020),(1.44,.245,.169,-.020),
                  (1.49,.248,.163,-.010),(1.535,.243,.147,-.001),(1.572,.226,.120,-.002),
                  (1.60,.190,.105,-.005),(1.621,.112,.090,-.008),(1.626,.100,.083,-.010))
        shirt=self.add("CLO_WorkTShirt",base.make_vertical_shell(stations,24),"chest","shirt")
        self.weight_height(shirt,((1.125,"pelvis"),(1.27,"spine"),(1.47,"chest")))
        self.add("CLO_CrewNeckRib",base.make_vertical_shell(((1.619,.105,.087,-.010),(1.63,.100,.084,-.010)),28),"chest","shirt_edge")
        for side in ("L","R"):
            self.arm(side);ankle=base.BONE_BY_NAME["foot."+side].head
            for name,rows,color in (
                ("GEO_WorkBoot",((.026,.078,.145,-.072),(.055,.080,.145,-.072),(.083,.077,.133,-.064),(.116,.067,.101,-.034),(.155,.066,.074,-.006),(.198,.065,.068,.003)),"shoe"),
                ("GEO_BootWelt",((.007,.080,.146,-.073),(.023,.084,.150,-.073),(.039,.081,.147,-.073)),"sole"),
                ("GEO_BootHeel",((0,.060,.045,.016),(.026,.064,.046,.016)),"sole")):
                obj=self.add(name+"."+side,base.make_vertical_shell(rows,20),"foot."+side,color,"footwear")
                for v in obj.data.vertices:v.co.x+=ankle[0]
            for i in range(4):
                z=.102+i*.017;y=-.118+i*.010
                self.add("GEO_BootLace"+str(i)+"."+side,tube_path(((ankle[0]-.026,y,z),(ankle[0]+.026,y,z+.009)),(.0028,.0028),6),"foot."+side,"sole","footwear")
        for part in self.result.parts:
            if part.obj.name!=FACE_NAME:self.uv(part)
        return self.result

    def uv(self,part):
        bpy.context.view_layer.update();obj=part.obj;mesh=obj.data;layer=mesh.uv_layers.new(name="UVMap")
        points=[obj.matrix_world@v.co for v in mesh.vertices]
        low=[min(p[i] for p in points) for i in range(3)];high=[max(p[i] for p in points) for i in range(3)]
        arm_center=None
        if obj.name.startswith("GEO_Arm."):
            # Place the drawing on the actual outer/front forearm in the
            # hanging pose, then retain that cylindrical UV on the skin rig.
            side=obj.name[-1];sign=1 if side=="L" else -1;rig=self.result.rig
            base.reset_pose(rig);base.apply_pose(rig,base.CITIZEN_HANGING_ARMS);bpy.context.view_layer.update()
            bone="forearm."+side;skin=rig.pose.bones[bone].matrix@rig.data.bones[bone].matrix_local.inverted()
            ring=[skin@p for p in points[5*16:6*16]];center=sum(ring,Vector())/16
            outward=Vector((sign*.65,-.76,0));arm_center=max(range(16),key=lambda i:(ring[i]-center).dot(outward))
            base.reset_pose(rig);bpy.context.view_layer.update()
        for polygon in mesh.polygons:
            turns=[(mesh.loops[i].vertex_index%16-arm_center+8)%16 for i in polygon.loop_indices] if arm_center is not None else []
            crosses=bool(turns) and max(turns)-min(turns)>8
            for index in polygon.loop_indices:
                p=points[mesh.loops[index].vertex_index];u=(p.x-low[0])/max(.001,high[0]-low[0]);v=(p.z-low[2])/max(.001,high[2]-low[2])
                if arm_center is not None:
                    vertex=mesh.loops[index].vertex_index;turn=(vertex%16-arm_center+8)%16
                    if crosses and turn<8:turn+=16
                    uv=((256+(128 if obj.name.endswith("R") else 0)+2+turn/16*124)/512,(2+(1-vertex//16/8)*252)/256)
                    if len(polygon.vertices)>4:
                        uv=((256+(128 if obj.name.endswith("R") else 0)+2)/512,.995)
                    layer.data[index].uv=uv;continue
                if obj.name=="CLO_WorkTrousers":uv=((2+u*123+(0 if polygon.normal.y<0 else 128))/256,(2+v*123)/256)
                elif part.role=="hair":uv=((130+u*59)/256,(130+v*123)/256)
                elif obj.name in ("CLO_SkiBeanie","CLO_BeanieCuff"):
                    # Cylindrical wrap keeps the knit ribs vertical around
                    # the cap; all seams land at the back of the head.
                    angle=math.atan2(p.x,-(p.y-.012));height=55 if obj.name=="CLO_BeanieCuff" else 123
                    uv=((194+(angle/math.tau+.5)*59)/256,(130+v*height)/256)
                elif obj.name.startswith(("CLO_WorkTShirt","CLO_ShortSleeve")):uv=((2+u*60+(0 if polygon.normal.y<0 else 64))/256,(130+v*123)/256)
                else:uv=(.005,.995)
                layer.data[index].uv=(uv[0]*.5,uv[1])


def action_pose(result,name,phase):
    short=name.removeprefix("CanneryReceiver");pose=resident.action_pose("Walk" if short=="Walk" else "Idle",phase)
    wave=math.sin(phase*math.tau);pulse=math.sin(phase*math.pi)**2
    if short=="Walk":
        for n,value in list(pose.items()):
            if n.startswith(("thigh.","shin.","foot.")):
                pose[n]=base.BonePose(rotation_degrees=tuple(v*.83 for v in value.rotation_degrees),location_m=value.location_m,target_direction=value.target_direction)
        pose["spine"]=base.BonePose(rotation_degrees=(1.4,0,wave*.8))
        pose["chest"]=base.BonePose(rotation_degrees=(.5,0,-wave*.6))
    else:
        pose["spine"]=base.BonePose(rotation_degrees=(.4+pulse*.25,0,.45))
        pose["chest"]=base.BonePose(rotation_degrees=(-.4+wave*.28,0,-.3))
        if short=="Listen":
            pose["head"]=base.BonePose(rotation_degrees=(-2*pulse,0,-6*pulse))
            pose["neck"]=base.BonePose(rotation_degrees=(-.6*pulse,0,-2*pulse))
        elif short=="Break":
            pose["head"]=base.BonePose(rotation_degrees=(-1*pulse,0,5*pulse))
        elif short=="Work":
            pose["spine"]=base.BonePose(rotation_degrees=(9+wave*.3,0,0))
            pose["chest"]=base.BonePose(rotation_degrees=(4,0,0))
            pose["head"]=base.BonePose(rotation_degrees=(3+pulse*2,0,0))
    base.reset_pose(result.rig);base.apply_pose(result.rig,pose)


def contact_pose(result,name,phase):
    rig=result.rig;action_pose(result,name,phase)
    low=min(base.evaluated_part_min_z(p,bpy.context.evaluated_depsgraph_get()) for p in result.parts if p.bone.startswith("foot."))
    pelvis=rig.pose.bones["pelvis"];m=pelvis.matrix.copy();m.translation.z-=low;pelvis.matrix=m;bpy.context.view_layer.update()
    targets={}
    if name=="CanneryReceiverWork":
        pulse=math.sin(phase*math.tau)**2
        targets={side:(sign*.23,-.56,1.15+.018*pulse) for side,sign in (("L",1),("R",-1))}
    if targets:resident.solve_grips(rig,targets)
    return targets


def make_actions(result):
    rig=result.rig;curves=[];max_ground=0.;max_grip=0.;endpoints={}
    for name,seconds,loop in CLIPS:
        action=bpy.data.actions.new(name);action.use_fake_user=True;action.use_frame_range=True
        frames=round(seconds*FPS);action.frame_start=0;action.frame_end=frames;action.use_cyclic=loop
        rig.animation_data_create().action=action;previous={};endpoint=[]
        for frame in range(frames+1):
            targets=contact_pose(result,name,frame/frames)
            if targets:max_grip=max(max_grip,max((rig.pose.bones["SOCKET_Grip."+side].head-Vector(p)).length for side,p in targets.items()))
            for bone in rig.pose.bones:
                bone.rotation_mode="QUATERNION"
                if bone.name in previous:bone.rotation_quaternion.make_compatible(previous[bone.name])
                previous[bone.name]=bone.rotation_quaternion.copy()
                for channel in ("location","rotation_quaternion","scale"):bone.keyframe_insert(channel,frame=frame,group=bone.name)
            if frame in (0,frames):endpoint.append({b.name:[round(v,6) for row in b.matrix for v in row] for b in rig.pose.bones})
            max_ground=max(max_ground,abs(min(base.evaluated_part_min_z(p,bpy.context.evaluated_depsgraph_get()) for p in result.parts if p.bone.startswith("foot."))))
        endpoints[name]=max(abs(a-b) for n in endpoint[0] for a,b in zip(endpoint[0][n],endpoint[1][n]))
        for curve in base.iter_action_fcurves(action):
            for key in curve.keyframe_points:key.interpolation="LINEAR"
            curves.append((name,curve.data_path,curve.array_index,[[round(k.co.x,6),round(k.co.y,6)] for k in curve.keyframe_points]))
        rig.animation_data.action=None;print("Authored "+name,flush=True)
    base.reset_pose(rig)
    if max_ground>.002 or max_grip>.002 or max(endpoints.values())>.002:
        raise RuntimeError(f"Action contact mismatch ground={max_ground}, grip={max_grip}, loops={endpoints}")
    return {"generator":Path(__file__).name,"version":VERSION,"bone_count":31,"fps":FPS,"root_motion":False,
            "clips":[{"name":n,"duration_seconds":s,"loop":l} for n,s,l in CLIPS],
            "work_grips_unity":{"L":[.23,1.15,.56],"R":[-.23,1.15,.56]},
            "validation":{"max_ground_error_m":round(max_ground,7),"max_grip_error_m":round(max_grip,7),
                          "max_loop_endpoint_error":round(max(endpoints.values()),7),"curve_signature":hash_json(curves)}}


def manifest(result,wardrobe_hash,face_hash):
    metrics=resident.measured(result)
    if not 6000<=metrics["triangle_count"]<=8000:raise RuntimeError("Triangle budget 6000..8000: "+str(metrics))
    if len(result.rig.data.bones)!=31:raise RuntimeError("Exactly 31 shared body bone identities required")
    if any(p.bone.startswith("face.") for p in result.parts):raise RuntimeError("Facial features must stay painted")
    fingertip=result.anchors["ANCHOR_GlassesFinger"]
    finger=next(p.obj for p in result.parts if p.obj.name=="GEO_Finger0.R")
    ring_center=sum((finger.matrix_world@v.co for v in list(finger.data.vertices)[-6:]),Vector())/6
    if fingertip.parent_bone!="hand.R" or (fingertip.matrix_world.translation-ring_center).length>.00001:
        raise RuntimeError("Glasses contact anchor must follow the right index fingertip ring")
    hand_geometry={}
    for side in ("L","R"):
        bone=base.BONE_BY_NAME["hand."+side];wrist=Vector(bone.head)
        inverse=(Vector(bone.tail)-wrist).to_track_quat("Z","Y").inverted()
        palm=next(p.obj for p in result.parts if p.obj.name=="GEO_Palm."+side)
        points=[inverse@(palm.matrix_world@v.co-wrist) for v in palm.data.vertices]
        center=Vector((0,0,.042*1.17*HAND_SCALE));radii=Vector((.0495,.0287,.0621))*HAND_SCALE
        grip=inverse@(Vector(base.BONE_BY_NAME["SOCKET_Grip."+side].head)-wrist)
        grip_radius=sum(((grip[i]-center[i])/radii[i])**2 for i in range(3))
        wrist_radius=sum((center[i]/radii[i])**2 for i in range(3))
        if grip_radius>=.9 or wrist_radius>=.9:
            raise RuntimeError("Enlarged palm must enclose its unchanged grip and overlap the wrist: "+side)
        hand_geometry[side]={"palm_width_m":round(max(v.x for v in points)-min(v.x for v in points),6),
                             "palm_length_m":round(max(v.z for v in points)-min(v.z for v in points),6),
                             "grip_ellipsoid_radius_squared":round(grip_radius,6),"wrist_ellipsoid_radius_squared":round(wrist_radius,6)}
    contact_pose(result,"CanneryReceiverIdle",0)
    hand_directions={}
    for side,sign in (("L",1),("R",-1)):
        bone="hand."+side;rig=result.rig;skin=rig.pose.bones[bone].matrix@rig.data.bones[bone].matrix_local.inverted()
        def center(name,last=None):
            obj=next(p.obj for p in result.parts if p.obj.name==name)
            vertices=list(obj.data.vertices)[-last:] if last else list(obj.data.vertices)
            return sum((skin@(obj.matrix_world@v.co) for v in vertices),Vector())/len(vertices)
        index=center("GEO_Finger0."+side,6);little=center("GEO_Finger3."+side,6)
        wrist=rig.pose.bones[bone].head
        palm=(result.anchors["ANCHOR_HandPalm."+side].matrix_world.translation-wrist).normalized()
        distal=(result.anchors["ANCHOR_HandFingers."+side].matrix_world.translation-wrist).normalized()
        thumb=center("GEO_Thumb."+side)-center("GEO_Palm."+side)
        radial=(index-little).normalized()
        if thumb.y>=-.03 or radial.y>=-.90 or palm.x*sign>=-.90 or distal.z>=-.90:
            raise RuntimeError("Hanging hands must have thumbs forward, palms inward, fingers down: "+side)
        hand_directions[side]={"thumb_forward_m":round(-thumb.y,6),"index_side_forward_dot":round(-radial.y,6),
                               "palm_inward_dot":round(-palm.x*sign,6),"fingers_down_dot":round(-distal.z,6)}
    base.reset_pose(result.rig);bpy.context.view_layer.update()
    if abs(metrics["bounds_max"][2]-metrics["bounds_min"][2]-DRESSED_HEIGHT)>.002:raise RuntimeError("Measured dressed height must match the flattened cap")
    anatomy=[p.obj.matrix_world@v.co for p in result.parts if p.obj.name not in ("CLO_SkiBeanie","CLO_BeanieCuff") for v in p.obj.data.vertices]
    if abs(max(v.z for v in anatomy)-min(v.z for v in anatomy)-HEIGHT)>.002:raise RuntimeError("Hat must not change the 1.96 metre anatomy")
    cuff=next(p.obj for p in result.parts if p.obj.name=="CLO_BeanieCuff")
    cuff_points=[cuff.matrix_world@v.co for v in cuff.data.vertices]
    rear=[p for p in cuff_points if abs(p.x)<.0001 and p.y>0]
    front=[p for p in cuff_points if abs(p.x)<.0001 and p.y<0]
    if not 1.75<min(p.z for p in rear)<1.78 or max(p.y for p in rear)>.13 or min(p.z for p in front)<1.86:
        raise RuntimeError("Beanie must hug the low occiput while leaving the forehead open")
    ear_top=max((p.obj.matrix_world@v.co).z for p in result.parts if p.obj.name.startswith("GEO_Ear.") for v in p.obj.data.vertices)
    cuff_to_ear=max(abs(cuff_points[i].z-ear_top) for i in (0,8))
    if cuff_to_ear>.002:raise RuntimeError("Both beanie sides must reach the measured upper ear edge")
    cap=next(p.obj for p in result.parts if p.obj.name=="CLO_SkiBeanie")
    cap_points=[cap.matrix_world@v.co for v in cap.data.vertices]
    folded_tip=sum(cap_points[-16:],Vector())/16
    if (folded_tip-cap_points[32+4]).length>.03:
        raise RuntimeError("Beanie terminal fold must lie against its rear surface, without an airborne hook")
    cap_tree=BVHTree.FromPolygons(cap_points,[tuple(p.vertices) for p in cap.data.polygons]);hair_clearance=float("inf")
    for part in result.parts:
        if part.obj.name!="HAIR_Crown" and not part.obj.name.startswith("HAIR_TousledLock"):continue
        for vertex in part.obj.data.vertices:
            point=part.obj.matrix_world@vertex.co
            if point.z<1.90:continue
            hit,_,_,_=cap_tree.ray_cast(Vector((point.x,point.y,2.2)),Vector((0,0,-1)),.5)
            if hit is None or hit.z-point.z<.005:
                raise RuntimeError(f"Flattened beanie needs 5 mm hair clearance: {part.obj.name} {tuple(point)} -> {None if hit is None else hit.z-point.z}")
            hair_clearance=min(hair_clearance,hit.z-point.z)
    closed=[];negative=[];surface=[]
    for p in result.parts:
        mesh=p.obj.data;edges=Counter(tuple(sorted((f.vertices[i],f.vertices[(i+1)%len(f.vertices)]))) for f in mesh.polygons for i in range(len(f.vertices)))
        if edges and all(n==2 for n in edges.values()):
            volume=0.
            for f in mesh.polygons:
                points=[mesh.vertices[i].co for i in f.vertices]
                for i in range(1,len(points)-1):volume+=points[0].dot(points[i].cross(points[i+1]))/6
            if volume<=0:negative.append((p.obj.name,volume))
            closed.append(p.obj.name)
        elif p.obj.name=="CLO_WorkTrousers":raise RuntimeError("Continuous trousers have an open seam")
        for vertex in mesh.vertices:
            if abs(sum(g.weight for g in vertex.groups)-1)>.00001:raise RuntimeError("Non-normalized weights: "+p.obj.name)
        if p.obj.name!=FACE_NAME:
            coordinates=[v.uv for v in mesh.uv_layers.active.data]
            if p.obj.name.startswith("GEO_Arm."):
                left=.5 if p.obj.name.endswith("L") else .75
                if p.role!="body" or any(not left<=v.x<=left+.25 or not 0<=v.y<=1 for v in coordinates):
                    raise RuntimeError("Permanent forearm tattoos need their own body atlas cell: "+p.obj.name)
            elif any(not 0<=v.x<=.5 or not 0<=v.y<=1 for v in coordinates):
                raise RuntimeError("Original outfit/body UV must stay in the left atlas half: "+p.obj.name)
        surface.append((p.obj.name,[[round(v.uv.x,7),round(v.uv.y,7)] for v in mesh.uv_layers.active.data]))
    if negative:raise RuntimeError("Inward closed meshes: "+str(negative))
    face=next(p.obj for p in result.parts if p.obj.name==FACE_NAME);uv=[v.uv for v in face.data.uv_layers.active.data]
    if [min(v[i] for v in uv) for i in range(2)]!=[0,0] or [max(v[i] for v in uv) for i in range(2)]!=[1,1]:raise RuntimeError("Face needs the complete local UV square")
    shirt=next(p.obj for p in result.parts if p.obj.name=="CLO_WorkTShirt");pts=[shirt.matrix_world@v.co for v in shirt.data.vertices]
    def section(z):
        ring=[v for v in pts if abs(v.z-z)<.00001]
        return {"width":max(v.x for v in ring)-min(v.x for v in ring),"depth":max(v.y for v in ring)-min(v.y for v in ring)}
    chest=section(1.44);waist=section(1.15);abdomen=section(1.24)
    trousers=next(p.obj for p in result.parts if p.obj.name=="CLO_WorkTrousers")
    trouser_points=[trousers.matrix_world@v.co for v in trousers.data.vertices]
    seam=[v for v in trouser_points if abs(v.x)<.00001 and v.y<-.05 and v.z>.85]
    crotch_height=min(v.z for v in seam);rise=1.107-crotch_height
    ease_point=min(seam,key=lambda v:abs(v.z-1.0025));front_ease=-.124-ease_point.y
    if abs(rise-.202)>.00001 or not .017<front_ease<.018:
        raise RuntimeError("Longer trouser rise and subtle closed cloth volume must remain measured")
    profile={"shirt_width_m":round(max(v.x for v in pts)-min(v.x for v in pts),6),"shirt_depth_m":round(max(v.y for v in pts)-min(v.y for v in pts),6),
             "chest_width_m":round(chest["width"],6),"waist_width_m":round(waist["width"],6),
             "abdomen_depth_m":round(abdomen["depth"],6),"chest_to_waist_ratio":round(chest["width"]/waist["width"],6),
             "neck_width_m":.214,"head_width_m":.242,"glasses_bridge_blender":list(GLASSES_BRIDGE),
             "cap_hair_clearance_min_m":round(hair_clearance,7)}
    trouser_profile={"belt_center_height_m":1.107,"crotch_branch_height_m":round(crotch_height,6),
                     "front_rise_m":round(rise,6),"front_cloth_ease_m":round(front_ease,6)}
    if not .48<profile["shirt_width_m"]<.51 or not .33<profile["shirt_depth_m"]<.36:raise RuntimeError("Athletic upper torso envelope differs")
    if not 1.25<profile["chest_to_waist_ratio"]<1.32 or not .26<profile["abdomen_depth_m"]<.28:
        raise RuntimeError("Athletic chest-to-waist taper or flat abdomen differs")
    payload={"generator":Path(__file__).name,"version":VERSION,"role":"CanneryReceiver","anatomy_standard":"NpcHumanV2",
             "height_scale":1.,"height_m":HEIGHT,"dressed_height_m":DRESSED_HEIGHT,"bone_count":31,"body_bone_count":31,**metrics,"triangle_budget":[6000,8000],
             "closed_mesh_count":len(closed),"surface_signature":hash_json(surface),"atlas_sha256":wardrobe_hash,"face_atlas_sha256":face_hash,
             "profile":profile,"trouser_profile":trouser_profile,"beanie_side_to_ear_m":round(cuff_to_ear,6),
             "face":{"renderer":FACE_NAME,"texture":"CanneryReceiverFaceAtlas.png","width":512,"height":256,"columns":8,"rows":4,"cell_size":64,
             "uv_contract":"local_0_1_runtime_cell_scale_offset","expressions":["Rest","HalfBlink","Blink","Emphasis","Focused"],
             "mouths":["Closed","Narrow","Open","Round","Wide","Teeth"],"smug_cell":30,"raised_brow_smug_cell":31},
             "glasses":{"anchor":"ANCHOR_Glasses","finger_anchor":"ANCHOR_GlassesFinger","renderers":[p.obj.name for p in result.parts if p.obj.name.startswith("GEO_Glasses")],"open_clear_apertures":True},
             "hands":{"finger_renderers_in_anatomical_order":["GEO_Finger0","GEO_Finger1","GEO_Finger2","GEO_Finger3"],
                      "size_multiplier_from_original":HAND_SCALE,"measured_geometry":hand_geometry,
                      "finger_order":["index","middle","ring","little"],"glasses_contact_renderer":"GEO_Finger0.R",
                      "fingers_anchor_prefix":"ANCHOR_HandFingers.","palm_anchor_prefix":"ANCHOR_HandPalm.",
                      "idle_anatomy_validation":hand_directions},
             "body_atlas":{"width":512,"height":256,"outfit_region_pixels":[0,0,256,256],"skin_region_pixels":[256,0,256,256]},
             "tattoos":[{"renderer":"GEO_Arm.L","motif":"crude_middle_finger","placement":"outer_forearm"},
                        {"renderer":"GEO_Arm.R","motif":"snarling_face","placement":"outer_forearm"},
                        {"renderer":"GEO_Arm.L","motif":"rough_skull","placement":"exposed_biceps","drawing_scale":1.5},
                        {"renderer":"GEO_Arm.R","motif":"heart_pierced_by_arrow","placement":"exposed_biceps","drawing_scale":1.5}],
             "parts":[{"name":p.obj.name,"color":list(p.color),"role":p.role,"triangles":base.triangulated_count(p.obj.data)} for p in result.parts],
             "geometry_signature":resident.geometry_signature(result)}
    slots={"shirt":[],"trousers":[],"belt":[],"shoes":[],"headwear":[]}
    for p in result.parts:
        n=p.obj.name
        if n in ("CLO_SkiBeanie","CLO_BeanieCuff"):slot="headwear"
        elif p.role=="footwear":slot="shoes"
        elif n.startswith("CLO_Belt") or n=="CLO_LeatherBelt":slot="belt"
        elif n=="CLO_WorkTrousers":slot="trousers"
        elif p.role=="clothing":slot="shirt"
        else:continue
        slots[slot].append(n)
    payload["outfit"]={"id":"cannery_receiver_workwear","atlas":"CanneryReceiverAtlas.png","parts":[{"slot":s,"renderers":n} for s,n in slots.items()]}
    garments=[p.obj.name for p in result.parts if p.role in ("clothing","headwear","footwear")]
    if sorted(n for names in slots.values() for n in names)!=sorted(garments):
        raise RuntimeError("Every garment needs exactly one replaceable wardrobe slot")
    payload["signature"]=hash_json(payload);return payload


def previews(result,directory,wardrobe,faces,view=None):
    directory.mkdir(parents=True,exist_ok=True);scene=bpy.context.scene;scene.view_settings.view_transform="Standard"
    scene.world.use_nodes=True;bg=scene.world.node_tree.nodes.get("Background");bg.inputs["Color"].default_value=(.11,.13,.14,1);bg.inputs["Strength"].default_value=.55
    nodes=result.material.node_tree.nodes;links=result.material.node_tree.links;shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(wardrobe));tex.interpolation="Closest"
    info=nodes.new("ShaderNodeObjectInfo");mix=nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY";mix.inputs[0].default_value=1
    links.new(info.outputs["Color"],mix.inputs[1]);links.new(tex.outputs["Color"],mix.inputs[2]);links.new(mix.outputs[0],shader.inputs["Base Color"])
    material=result.material.copy();material.name="PREVIEW_PaintedFace";nodes=material.node_tree.nodes;links=material.node_tree.links;shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    tex=nodes.new("ShaderNodeTexImage");tex.image=bpy.data.images.load(str(faces));tex.interpolation="Closest"
    coords=nodes.new("ShaderNodeTexCoord");mapping=nodes.new("ShaderNodeVectorMath");mapping.operation="MULTIPLY_ADD";mapping.inputs[1].default_value=(.125,.25,1);mapping.inputs[2].default_value=(0,.75,0)
    links.new(coords.outputs["UV"],mapping.inputs[0]);links.new(mapping.outputs["Vector"],tex.inputs["Vector"]);links.new(tex.outputs["Color"],shader.inputs["Base Color"])
    face=next(p.obj for p in result.parts if p.obj.name==FACE_NAME);face.data.materials.clear();face.data.materials.append(material)
    for loc,power,size in (((-3,-4,4),500,4),((3,-1,3),260,4),((1,3,4),500,3)):
        light=bpy.data.lights.new("ReviewSoftbox","AREA");light.energy=power;light.shape="DISK";light.size=size;obj=bpy.data.objects.new("ReviewSoftbox",light);scene.collection.objects.link(obj);obj.location=loc;obj.rotation_euler=(Vector((0,0,1.2))-obj.location).to_track_quat("-Z","Y").to_euler()
    camera=bpy.data.cameras.new("ReviewCamera");obj=bpy.data.objects.new("ReviewCamera",camera);scene.collection.objects.link(obj);scene.camera=obj;camera.type="ORTHO"
    scene.render.engine="CYCLES";scene.cycles.samples=24
    for name,location,focus,size,clip,phase,cell in (
        ("BodyFront",(0,-4,1.45),(0,0,.99),2.25,"Idle",0,0),("BodyBack",(1.2,4,1.9),(0,0,.99),2.25,"Idle",0,0),
        ("BodyProfile",(4,-.2,1.60),(0,0,.99),2.25,"Idle",0,0),("BodyThreeQuarter",(2,-4,1.9),(0,0,.99),2.25,"Idle",0,0),
        ("FaceFront",(0,-4,1.79),(0,-.01,1.79),.52,"Idle",0,0),("FaceThreeQuarter",(1,-2,1.82),(0,-.01,1.79),.52,"Idle",0,0),
        ("FaceProfile",(3,-.25,1.8),(0,0,1.79),.52,"Idle",0,0),("FaceSmug",(0,-4,1.79),(0,-.01,1.79),.52,"Idle",0,31),
        ("Working",(2,-4,2),(0,-.12,1),2.25,"Work",.3,24),("Rest",(1.3,-4,2),(0,-.03,1.35),1.35,"Break",.55,30),
        ("Walking",(2,-4,1.9),(0,0,.99),2.25,"Walk",.25,0),
        ("TrousersThreeQuarter",(1.2,-3,1.08),(0,-.025,.985),.50,"Idle",0,0),
        ("TattooLeft",(1,-2,1.25),(.315,-.03,1.09),.40,"Idle",0,0),
        ("TattooRight",(-1,-2,1.25),(-.315,-.03,1.09),.40,"Idle",0,0),
        ("BicepsLeft",(1,-2,1.45),(.31,-.025,1.33),.32,"Idle",0,0),
        ("BicepsRight",(-1,-2,1.45),(-.31,-.025,1.33),.32,"Idle",0,0)):
        if view is not None and name not in view.split(","):continue
        if result.rig.animation_data:result.rig.animation_data.action=None
        contact_pose(result,"CanneryReceiver"+clip,phase)
        obj.location=location;obj.rotation_euler=(Vector(focus)-obj.location).to_track_quat("-Z","Y").to_euler();camera.ortho_scale=size
        mapping.inputs[2].default_value=((cell%8)*.125,(3-cell//8)*.25,0)
        scene.render.resolution_x=600;scene.render.resolution_y=780 if name.startswith("Body") or name=="Working" else 600
        scene.render.filepath=str(directory/(name+".png"));bpy.ops.render.render(write_still=True)
    base.reset_pose(result.rig)


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/City/Cannery/Receiver")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/City/CanneryReceiver")
    parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true");parser.add_argument("--preview-only",action="store_true")
    parser.add_argument("--atlas-only",action="store_true",help="Publish only the body atlas and manifest after proving all non-atlas model data unchanged.")
    parser.add_argument("--preview-view",help="Render named source views, separated by commas, during a focused art iteration.")
    parser.add_argument("--inspect-hands",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if args.inspect_hands:
        result=ReceiverBuilder().build();contact_pose(result,"CanneryReceiverIdle",0)
        for side in ("L","R"):
            bone="hand."+side;a=Vector(base.BONE_BY_NAME[bone].head);b=Vector(base.BONE_BY_NAME[bone].tail)
            q=(b-a).to_track_quat("Z","Y");skin=result.rig.pose.bones[bone].matrix@result.rig.data.bones[bone].matrix_local.inverted()
            origin=skin@a
            axes={axis:tuple(round(v,5) for v in ((skin@(a+q@Vector(direction)))-origin)) for axis,direction in (("finger_Z",(0,0,1)),("palm_minusY",(0,-1,0)),("thumb_signedX",((-1 if side=="L" else 1),0,0)))}
            print("HAND_BASIS",side,json.dumps(axes),flush=True)
        return
    wardrobe=args.model_dir/"CanneryReceiverAtlas.png";faces=args.model_dir/"CanneryReceiverFaceAtlas.png"
    if args.preview_only or args.atlas_only:wardrobe=args.source_dir/wardrobe.name;faces=args.source_dir/faces.name
    wh=wardrobe_atlas(wardrobe,args.validate_only);fh=face_atlas(faces,args.validate_only)
    result=ReceiverBuilder().build();body=manifest(result,wh,fh)
    print("CanneryReceiver geometry",body["triangle_count"],"triangles",body["mesh_count"],"meshes",json.dumps(body["profile"]),flush=True)
    if args.preview_only:previews(result,args.source_dir,wardrobe,faces,args.preview_view);return
    if args.atlas_only:
        previous=json.loads((args.model_dir/"CanneryReceiver.json").read_text(encoding="utf8"))
        without_art=lambda value:{k:v for k,v in value.items() if k not in ("atlas_sha256","signature","tattoos")}
        if without_art(previous)!=without_art(body):
            raise RuntimeError("Atlas-only publication requires unchanged model geometry, UV, rig, anatomy, outfit, face and color data")
        publish_bytes(args.model_dir/wardrobe.name,wardrobe.read_bytes(),args.validate_only)
        publish_bytes(args.model_dir/"CanneryReceiver.json",(json.dumps(body,indent=2)+"\n").encode(),args.validate_only)
        if not args.no_preview:previews(result,args.source_dir,wardrobe,faces,args.preview_view)
        print("CANNERY RECEIVER ATLAS CONTRACT OK: all non-atlas model data unchanged",flush=True);return
    if not args.validate_only:base.export_fbx(args.model_dir/"CanneryReceiver.fbx",result)
    actions=make_actions(result)
    for name,payload in (("CanneryReceiver",body),("CanneryReceiverActions",actions)):
        publish_bytes(args.model_dir/(name+".json"),(json.dumps(payload,indent=2)+"\n").encode(),args.validate_only)
    if not args.validate_only:
        base.export_animation_fbx(args.model_dir/"CanneryReceiverActions.fbx",result)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"CanneryReceiver.blend"),check_existing=False)
        if not args.no_preview:previews(result,args.source_dir,wardrobe,faces,args.preview_view)
        for asset in sorted(args.model_dir.iterdir()):
            if asset.suffix not in (".png",".fbx",".json"):continue
            meta=asset.with_suffix(asset.suffix+".meta")
            if not meta.exists():
                guid=hashlib.sha256(asset.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]
                meta.write_text("fileFormatVersion: 2\nguid: "+guid+"\n",encoding="utf8")
    print("CANNERY RECEIVER ART CONTRACT OK: 1.96m athletic male, 31 body bones, modeled glasses, painted 32-cell face, grounded loops",flush=True)
    print(json.dumps(actions["validation"]),flush=True)


if __name__=="__main__":main()
