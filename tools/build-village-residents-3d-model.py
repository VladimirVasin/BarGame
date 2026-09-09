#!/usr/bin/env python3
"""Two ordinary winter villagers, with the production NpcHumanV2 skeleton.

Run with tools/run-blender.py. Bodies, bone-only actions, colour/detail atlas
and measured contacts are independently reproducible; no world placement lives here.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("village_human_base", Path(__file__).with_name("build-city-pedestrian-3d-model.py"))
base = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = base
spec.loader.exec_module(base)
VERSION = "1.0.0"
WOMAN_SCALE = 1.64 / 1.75
ROLE_NAMES = ("StationWorker", "WoodWoman")
LIFE_ROLES = ("RepairNeighbor", "SewingWoman", "SnowNeighbor", "BasketVisitor")
LIFE_HEIGHTS = {"RepairNeighbor":1.86,"SewingWoman":1.56,"SnowNeighbor":1.68,"BasketVisitor":1.70}
CLIPS = (("Idle", 4., True), ("Walk", 1.25, True), ("Reach", 3., False),
         ("Carry", 4., True), ("CarryWalk", 1.5, True), ("Place", 3., False),
         ("StationWork", 6., True), ("WoodWork", 6., True))


def atlas(path, female, validate_only=False, role=None):
    c = base.atlas_kit.PixelCanvas(256, 256)
    c.rect(0, 0, 256, 256, (255, 255, 255, 255))
    # Four grayscale material cells: woven cloth, joined coat front, worn boot,
    # knit. Distinct faces occupy the bottom-right 64 pixels, at hero texel density.
    for y in range(128):
        for x in range(128):
            value = 242 + ((x * 13 + y * 19) % 11)
            c.put(x, y, (value, value, value, 255))
    for x in (7, 60, 67, 120):
        c.line(x, 5, x, 121, (163, 159, 150, 255))
        for y in range(7, 119, 5):
            c.line(x + 2, y, x + 2, y + 1, (220, 215, 198, 255))
    # Quiet flat pockets and repaired lower corner; no military insignia.
    for x in (14, 77):
        c.rect(x, 63, x + 35, 94, (224, 222, 211, 255))
        c.line(x, 63, x + 35, 63, (136, 133, 120, 255), 1)
        c.line(x, 94, x + 35, 94, (181, 178, 167, 255))
    c.rect(81, 104, 106, 118, (207, 205, 190, 255))
    for x in range(83, 106, 4):
        c.line(x, 102, x + 1, 106, (141, 138, 125, 255))
    for y in range(8, 119, 7):
        c.line(134, y, 248, y, (219, 219, 211, 255))
        for x in range(137, 246, 6):
            c.line(x, y, x + 2, y + 3, (231, 230, 219, 255))
    # Boot quarters, welt, leather crease and lace crossings.
    c.rect(0, 128, 128, 256, (247, 247, 243, 255))
    for y in (224, 231, 239):
        c.line(6, y, 120, y, (164, 159, 144, 255))
    for y in range(150, 206, 9):
        c.line(42, y, 82, y + 8, (152, 147, 129, 255), 1)
        c.line(82, y, 42, y + 8, (177, 172, 155, 255), 1)
    skin = (245, 238, 229, 255)
    c.rect(192, 192, 256, 256, skin)
    c.ellipse(224, 224, 26, 31, skin)
    for x in (208, 235):
        c.line(x - 5, 214, x + 5, 213 if female else 215, (65, 54, 45, 255), 1)
        c.line(x - 5, 220, x + 5, 220, (112, 89, 75, 255))
        c.line(x - 3, 219, x + 3, 219, (183, 163, 131, 255))
        c.rect(x - 1, 218, x + 2, 221, (36, 36, 30, 255))
        c.line(x - 5, 224, x + 5, 225, (131, 102, 86, 255))
    if female:
        c.rect(197,208,249,227,skin)
        for x in (211,233):
            c.line(x-5,215,x-1,213,(93,79,65,255))
            c.line(x-1,213,x+4,214,(93,79,65,255))
            c.line(x-4,221,x+4,221,(166,145,118,255))
            c.rect(x-1,219,x+2,222,(43,45,35,255))
            c.line(x-5,224,x+4,225,(170,142,118,255))
    c.line(224, 220, 221, 231, (125, 93, 76, 255))
    c.line(221, 232, 226, 232, (102, 77, 64, 255))
    c.line(213, 240, 232, 240, (100, 71, 62, 255))
    c.line(214, 242, 231, 242, (167, 127, 112, 255))
    if not female:
        for y in range(237, 252):
            for x in range(202, 244):
                if (x + 3 * y) % 7 == 0:
                    c.put(x, y, (115, 101, 84, 255))
        c.line(211, 205, 233, 205, (132, 99, 82, 255))
    else:
        c.line(204, 228, 208, 234, (127, 94, 81, 255))
        c.line(239, 228, 235, 234, (127, 94, 81, 255))
    # The face is a neutral detail sheet multiplied by the same skin as the head.
    for y in range(192,256):
        for x in range(192,256):
            offset=(y*256+x)*4
            r,g,b,a=c.pixels[offset:offset+4]
            if (r,g,b)!=(245,238,229):
                value=min(255,round(r*1.35))
                c.put(x,y,(value,value,value,255))
    c.rect(128,128,192,256,(245,243,236,255))
    for y in range(132,253,3):
        for x in range(130,190,3):
            c.put(x,y,(237,236,229,255))
    if role is not None:
        # Each new face and garment has its own drawing, not a palette swap.
        c.rect(192,192,256,256,(245,238,229,255))
        profiles={"RepairNeighbor":(210,235,218,239),"SewingWoman":(211,232,220,241),
                  "SnowNeighbor":(208,236,218,240),"BasketVisitor":(212,233,219,240)}
        first,last,eye,mouth=profiles[role]
        for x in (first,last):
            brow=(108,108,108,255) if role=="SewingWoman" else (75,75,75,255)
            c.line(x-5,eye-5,x,eye-7 if role=="BasketVisitor" else eye-6,brow)
            c.line(x,eye-7 if role=="BasketVisitor" else eye-6,x+4,eye-5,brow)
            c.line(x-4,eye,x+4,eye,(187,187,180,255))
            c.rect(x-1,eye-1,x+2,eye+2,(46,48,43,255))
            if role!="BasketVisitor":
                c.line(x-6,eye+5,x+4,eye+6,(183,183,176,255))
                c.line(x-7,eye+2,x-9,eye+4,(168,168,159,255))
        nose=224 if role!="RepairNeighbor" else 225
        c.line(nose,eye+1,nose-2,233,(184,179,170,255))
        c.line(nose-3,234,nose+3,234,(122,118,111,255))
        c.line(214,mouth,231,mouth+(-1 if role=="SewingWoman" else 0),(125,117,108,255))
        if role=="RepairNeighbor":
            c.rect(213,236,234,239,(112,107,99,255))
            for x in range(214,234,4):c.line(x,236,x-1,239,(171,168,158,255))
            c.line(211,204,237,204,(193,190,180,255))
        elif role=="SnowNeighbor":
            for y in range(238,253):
                for x in range(203,244):
                    if (x*3+y*7)%11==0:c.put(x,y,(175,173,160,255))
        elif role=="SewingWoman":
            for y in (203,207):c.line(210,y,236,y,(193,193,182,255))
            c.line(210,231,207,240,(176,174,166,255));c.line(237,231,240,240,(176,174,166,255))
        # Garment fronts differ: work-coat buttons, a crossed house coat,
        # a short padded jacket, or a long diagonal fastening.
        c.rect(55,2,73,125,(242,241,234,255))
        if role=="BasketVisitor":
            c.line(39,8,76,116,(146,147,137,255),1)
            for y in (30,57,85):c.ellipse(round(39+y*.32),y,2,2,(101,107,99,255))
        elif role=="SewingWoman":
            c.line(49,5,76,120,(168,162,150,255))
            for y in (36,65,94):
                for x in (61,74):c.ellipse(x,y,2,2,(149,141,128,255))
        elif role=="SnowNeighbor":
            for y in range(20,122,22):c.line(10,y,119,y,(209,207,196,255))
            c.line(63,5,63,120,(129,133,126,255))
        else:
            c.line(62,5,62,121,(157,157,145,255))
            for y in (23,46,69,92):c.ellipse(66,y,2,2,(123,119,107,255))
    if validate_only:
        if path.read_bytes()!=c.png_bytes(): raise RuntimeError("Deterministic atlas differs: "+str(path))
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(c.png_bytes())
    return hashlib.sha256(c.png_bytes()).hexdigest()


class ResidentBuilder(base.PedestrianBuilder):
    def __init__(self, female):
        super().__init__(None)
        self.female = female

    def build(self):
        self.reset_scene()
        collection = bpy.data.collections.new("EXPORT_VillageResident")
        bpy.context.scene.collection.children.link(collection)
        material = self.create_shared_material()
        root = bpy.data.objects.new("ROOT_Player", None)
        collection.objects.link(root)
        rig = self.create_armature(collection, root)
        self.result = base.BuildResult(root, rig, collection, material)
        base.PALETTE.update({
            "v_skin": (.60, .46, .38, 1), "v_coat": ((.38, .22, .17, 1) if self.female else (.18, .25, .24, 1)),
            "v_dark": ((.27, .15, .12, 1) if self.female else (.12, .17, .17, 1)),
            "v_pants": ((.23, .21, .18, 1) if self.female else (.17, .18, .19, 1)),
            "v_knit": ((.49, .43, .32, 1) if self.female else (.23, .25, .24, 1)),
            "v_glove": (.27, .23, .17, 1), "v_boot": (.14, .125, .105, 1),
            "v_hair": ((.24, .20, .16, 1) if self.female else (.28, .28, .25, 1)),
            "v_trim": (.35, .32, .26, 1), "v_white": (1, 1, 1, 1),
        })
        self.build_ordinary_adult_body(
            skin="v_skin", coat="v_coat", coat_dark="v_dark", trousers="v_pants",
            shoulder_width=.465 if self.female else .480, waist_width=.465 if self.female else .440,
            chest_depth=.140, waist_depth=.132, seat_depth=.133,
            head_radii=(.099, .096, .124) if self.female else (.108, .098, .125),
            shoe="v_boot", sole="v_boot", shin_material="v_boot", hand_scale=1.13,
            shoe_shape=(1.04, 1.10, 1.09))
        # A continuous, weighted winter shell overlays the substrate seams.
        for name in ("CLO_Chest", "CLO_Waist", "CLO_Seat"):
            part = next(p for p in self.result.parts if p.obj.name == name)
            self.result.parts.remove(part)
            bpy.data.objects.remove(part.obj, do_unlink=True)
        stations = (((.59,.245,.15,.012),(.80,.249,.155,.009),(.98,.244,.15,.003),
                     (1.14,.23,.145,-.002),(1.27,.229,.145,-.007),(1.335,.213,.14,-.010)) if self.female else
                    ((.67,.23,.15,.012),(.80,.235,.15,.009),(.98,.23,.145,.003),
                     (1.14,.24,.145,-.002),(1.27,.25,.15,-.007),(1.335,.235,.145,-.010)))
        shell = self.add_part("CLO_WinterCoat", base.make_vertical_shell(stations, 16), "chest", "clothing", "v_coat")
        bpy.context.view_layer.update()
        # Source was authored in V1 landmarks, so weight using the mapped V2 height.
        for vg in list(shell.vertex_groups):
            shell.vertex_groups.remove(vg)
        groups = {n:shell.vertex_groups.new(name=n) for n in ("pelvis","spine","chest")}
        for vertex in shell.data.vertices:
            z = (shell.matrix_world @ vertex.co).z
            if z < 1.02:
                t=max(0.,min(1.,(z-.88)/.14)); pair=("pelvis","spine")
            else:
                t=max(0.,min(1.,(z-1.02)/.20)); pair=("spine","chest")
            groups[pair[0]].add([vertex.index],1-t,"REPLACE")
            groups[pair[1]].add([vertex.index],t,"REPLACE")
        self.build_cafe_face("v_skin", "v_skin", self.female)
        face = next(p.obj for p in self.result.parts if p.obj.name == "GEO_FaceSurface")
        for vertex in face.data.vertices:
            vertex.co.y -= .012
            if self.female:
                # The seven authored rows narrow below the cheekbone, not a
                # uniformly scaled copy of the man's jaw and nose.
                row=vertex.index//7
                vertex.co.x *= (.73,.80,.89,.95,.98,1.,1.)[row]
                if row in (3,4) and vertex.index%7 in (2,3,4):
                    vertex.co.y += .006
        for side, sign in (("L",1), ("R",-1)):
            self.add_part("CLO_Collar."+side, base.make_frustum_between((sign*.04,-.08,1.335),(sign*.095,-.073,1.41),.050,.044,8),"chest","clothing","v_knit")
            self.add_part("CLO_Cuff."+side, base.make_frustum_between((sign*.642,-.017,1.093),(sign*.684,-.018,1.072),.055,.053,10),"forearm."+side,"clothing","v_knit")
            self.add_part("HAIR_Temple."+side,base.make_ellipsoid((sign*.083,-.017,1.590),(.018,.046,.065),8,5),"head","hair","v_hair")
            self.add_part("GEO_BootHeel."+side,base.make_box((sign*.112,.004,.040),(.125,.091,.055)),"foot."+side,"footwear_detail","v_boot")
            self.add_part("GEO_BootToeCap."+side,base.make_ellipsoid((sign*.112,-.165,.074),(.068,.090,.045),12,5),"foot."+side,"footwear_detail","v_boot")
            self.add_part("CLO_PocketFlap."+side,base.make_tapered_box((sign*.138,-.143,.900),(sign*.138,-.139,.928),(.114,.020,0),(.124,.021,0)),"spine","clothing","v_dark")
        self.add_part("CLO_ScarfNeck",base.make_frustum_between((0,-.013,1.315),(0,-.020,1.445),.110,.086,14),"neck","clothing","v_knit")
        self.add_part("CLO_ScarfTail",base.make_tapered_box((-.080,-.146,1.075),(-.070,-.143,1.350),(.105,.024,0),(.095,.025,0)),"chest","clothing","v_knit")
        if self.female:
            # A thick open-faced wool hood tied below the chin, visibly unlike
            # the station worker's folded cap and ear flaps.
            hood_vertices=[]; hood_faces=[]; rings=((1.44,.105,.098),(1.51,.121,.109),(1.62,.128,.117),(1.70,.100,.095),(1.736,.052,.053))
            for z,rx,ry in rings:
                for i in range(13):
                    theta=math.radians(-53+i*(286/12))
                    hood_vertices.append((rx*math.cos(theta),.010+ry*math.sin(theta),z))
            for ring in range(4):
                for i in range(12):
                    a=ring*13+i;hood_faces.append((a,a+1,a+14,a+13))
            hood_vertices.append((0,.010,1.749))
            for i in range(12):
                hood_faces.append((52+i,52+i+1,65))
            hood=self.add_part("CLO_WoolHood",(hood_vertices,hood_faces),"head","headwear","v_knit")
            solid=hood.modifiers.new("WoolThickness","SOLIDIFY");solid.thickness=.009
            self.add_part("CLO_HoodKnot",base.make_ellipsoid((.008,-.106,1.430),(.054,.034,.040),10,5),"neck","clothing","v_knit")
            self.add_part("HAIR_ForeheadLock",base.make_frustum_between((-.035,-.105,1.662),(-.075,-.117,1.611),.026,.012,8),"head","hair","v_hair")
            self.add_part("HAIR_BackBun",base.make_ellipsoid((0,.055,1.548),(.070,.050,.062),12,6),"head","hair","v_hair")
        else:
            self.add_part("CLO_KnitCap",base.make_ellipsoid((0,-.006,1.681),(.117,.115,.075),16,7),"head","headwear","v_knit")
            self.add_part("CLO_CapFold",base.make_frustum_between((0,-.005,1.632),(0,-.005,1.666),.113,.115,16),"head","headwear","v_dark")
            self.add_part("CLO_CapEarFlap.L",base.make_ellipsoid((.090,-.010,1.586),(.035,.060,.073),8,5),"head","headwear","v_knit")
            self.add_part("CLO_CapEarFlap.R",base.make_ellipsoid((-.090,-.010,1.586),(.035,.060,.073),8,5),"head","headwear","v_knit")
        for part in self.result.parts:
            if part.obj.name.startswith(("GEO_Hand.","GEO_Thumb.")):
                part.obj.color = base.PALETTE["v_glove"]
                part.color = base.PALETTE["v_glove"]
            self.uv(part)
        return self.result

    def uv(self, part):
        mesh=part.obj.data
        layer=mesh.uv_layers.new(name="UVMap")
        coords=[v.co for v in mesh.vertices]
        low=[min(v[i] for v in coords) for i in range(3)]
        high=[max(v[i] for v in coords) for i in range(3)]
        face=part.obj.name=="GEO_FaceSurface"
        boot=part.bone.startswith("foot") or part.obj.name.startswith("CLO_Shin")
        knit=any(s in part.obj.name for s in ("Cap","Scarf","Cuff","Collar","Hood"))
        for loop in mesh.loops:
            v=mesh.vertices[loop.vertex_index].co
            u=(v.x-low[0])/max(.0001,high[0]-low[0])
            w=(v.z-low[2])/max(.0001,high[2]-low[2])
            if face:
                uv=((193+u*62)/256,(1+w*62)/256)
            elif boot:
                uv=((2+u*123)/256,(2+w*123)/256)
            elif knit:
                uv=((130+u*123)/256,(130+w*123)/256)
            elif part.obj.name=="CLO_WinterCoat":
                uv=((2+u*123)/256,(130+w*123)/256)
            elif part.obj.name.startswith("CLO_"):
                uv=((130+u*60)/256,(2+w*123)/256)
            else:
                uv=(.65,.25) # reserved white cell
            layer.data[loop.index].uv=uv


class LifeResidentBuilder(ResidentBuilder):
    """Four silhouettes share only the proven adult anatomical substrate."""
    def __init__(self, role):
        super().__init__(role in ("SewingWoman","BasketVisitor"));self.role=role

    def remove(self, *names):
        for part in list(self.result.parts):
            if part.obj.name in names:
                self.result.parts.remove(part);bpy.data.objects.remove(part.obj,do_unlink=True)

    def build(self):
        result=super().build();role=self.role
        palettes={
            "RepairNeighbor":((.255,.245,.175,1),(.155,.155,.115,1),(.235,.20,.14,1),(.36,.35,.30,1)),
            "SewingWoman":((.19,.22,.29,1),(.13,.16,.21,1),(.39,.28,.22,1),(.52,.50,.43,1)),
            "SnowNeighbor":((.16,.23,.28,1),(.095,.15,.19,1),(.23,.23,.19,1),(.18,.17,.14,1)),
            "BasketVisitor":((.22,.30,.22,1),(.14,.20,.145,1),(.28,.17,.155,1),(.13,.095,.07,1))}
        coat,dark,knit,hair=palettes[role]
        base.PALETTE.update({"life_coat":coat,"life_dark":dark,"life_knit":knit,"life_hair":hair})
        color_map={"v_coat":coat,"v_dark":dark,"v_knit":knit,"v_hair":hair}
        self.remove("CLO_ScarfTail")
        if role in ("RepairNeighbor","SewingWoman","BasketVisitor"):
            self.remove("CLO_KnitCap","CLO_CapFold","CLO_CapEarFlap.L","CLO_CapEarFlap.R",
                "CLO_WoolHood","CLO_HoodKnot","HAIR_ForeheadLock","HAIR_BackBun")
        bpy.context.view_layer.update()
        width={"RepairNeighbor":.86,"SewingWoman":1.025,"SnowNeighbor":1.10,"BasketVisitor":.87}[role]
        lower={"RepairNeighbor":.69,"SewingWoman":.52,"SnowNeighbor":.75,"BasketVisitor":.60}[role]
        for part in result.parts:
            if part.palette_name in color_map:
                part.color=color_map[part.palette_name];part.obj.color=part.color
            if part.obj.name=="CLO_WinterCoat":
                world=[part.obj.matrix_world @ v.co for v in part.obj.data.vertices]
                bottom=min(v.z for v in world);top=max(v.z for v in world)
                for vertex,point in zip(part.obj.data.vertices,world):
                    point.x*=width
                    point.z=lower+(point.z-bottom)*(top-lower)/(top-bottom)
                    if role=="SewingWoman":point.y+=.018*(1-(point.z-lower)/(top-lower))
                    vertex.co=part.obj.matrix_world.inverted() @ point
            elif part.obj.name=="GEO_FaceSurface":
                for vertex in part.obj.data.vertices:
                    row=vertex.index//7
                    if role=="RepairNeighbor":vertex.co.x*=.90;vertex.co.y-=.003 if row in (3,4) else 0
                    elif role=="SewingWoman":vertex.co.x*=1.07 if row<4 else 1.01
                    elif role=="SnowNeighbor":vertex.co.x*=1.12 if row<4 else 1.06
                    elif role=="BasketVisitor":vertex.co.x*=.93;vertex.co.y+=.002 if row in (3,4) else 0
            elif part.obj.name=="GEO_Head":
                for vertex in part.obj.data.vertices:
                    vertex.co.x*={"RepairNeighbor":.93,"SewingWoman":1.03,"SnowNeighbor":1.06,"BasketVisitor":.94}[role]
        if role=="RepairNeighbor":
            self.add_part("CLO_FlatCapCrown",base.make_ellipsoid((0,.005,1.676),(.123,.132,.065),18,8),"head","headwear","life_dark")
            self.add_part("CLO_FlatCapBrim",base.make_ellipsoid((0,-.121,1.640),(.096,.071,.013),14,4),"head","headwear","life_knit")
            self.add_part("CLO_CapBand",base.make_frustum_between((0,0,1.616),(0,0,1.645),.104,.107,16),"head","headwear","life_dark")
            self.add_part("CLO_NarrowScarf",base.make_tapered_box((.053,-.15,1.16),(.055,-.15,1.38),(.056,.022,0),(.060,.024,0)),"chest","clothing","life_knit")
            for side,sign in (("L",1),("R",-1)):
                self.add_part("CLO_ReinforcedElbow."+side,base.make_ellipsoid((sign*.483,.020,1.161),(.056,.027,.073),10,5),"forearm."+side,"clothing","life_dark")
        elif role=="SewingWoman":
            self.add_part("CLO_HeadscarfCrown",base.make_ellipsoid((0,.010,1.655),(.123,.120,.094),16,8),"head","headwear","life_knit")
            for side,sign in (("L",1),("R",-1)):
                self.add_part("CLO_HeadscarfSide."+side,base.make_frustum_between((sign*.098,-.01,1.61),(sign*.073,-.053,1.46),.050,.034,10),"head","headwear","life_knit")
            self.add_part("CLO_WoolShawl",base.make_vertical_shell(((1.10,.208,.122,.040),(1.27,.272,.155,.025),(1.40,.130,.122,.01),(1.445,.080,.084,-.005)),16),"chest","clothing","life_knit")
            self.add_part("CLO_ShawlTie",base.make_ellipsoid((.055,-.15,1.24),(.039,.029,.050),10,5),"chest","clothing","life_dark")
            self.add_part("HAIR_SilverFringe",base.make_ellipsoid((0,-.101,1.620),(.076,.020,.032),14,4),"head","hair","life_hair")
        elif role=="SnowNeighbor":
            for side,sign in (("L",1),("R",-1)):
                self.add_part("CLO_PaddedElbow."+side,base.make_ellipsoid((sign*.488,.025,1.168),(.065,.030,.088),12,6),"forearm."+side,"clothing","life_dark")
            self.add_part("CLO_ScarfTucked",base.make_vertical_shell(((1.325,.101,.113,-.01),(1.37,.108,.114,-.005),(1.408,.080,.083,-.01)),14),"neck","clothing","life_knit")
        else:
            self.add_part("CLO_RibbedCap",base.make_ellipsoid((0,.006,1.676),(.112,.111,.080),18,8),"head","headwear","life_knit")
            self.add_part("CLO_RibbedFold",base.make_frustum_between((0,.004,1.626),(0,.004,1.66),.111,.112,18),"head","headwear","life_dark")
            self.add_part("HAIR_LowBun",base.make_ellipsoid((.015,.067,1.52),(.062,.056,.060),12,7),"head","hair","life_hair")
            self.add_part("HAIR_SideLock",base.make_frustum_between((.047,-.102,1.658),(.083,-.109,1.567),.027,.013,10),"head","hair","life_hair")
            self.add_part("CLO_ScarfShoulder",base.make_frustum_between((-.06,.032,1.39),(-.222,.018,1.32),.075,.055,12),"chest","clothing","life_knit")
            self.add_part("CLO_ScarfSideTail",base.make_tapered_box((-.224,.046,1.03),(-.221,.026,1.32),(.082,.026,0),(.102,.030,0)),"chest","clothing","life_knit")
        # Refresh weights after the hem's deliberate change. A long coat bends
        # continuously through the waist instead of splitting into rigid boxes.
        shell=next(p.obj for p in result.parts if p.obj.name=="CLO_WinterCoat")
        for group in list(shell.vertex_groups):shell.vertex_groups.remove(group)
        groups={n:shell.vertex_groups.new(name=n) for n in ("pelvis","spine","chest")}
        for vertex in shell.data.vertices:
            z=(shell.matrix_world @ vertex.co).z
            pair=("pelvis","spine") if z<1.02 else ("spine","chest")
            t=max(0,min(1,(z-.88)/.14 if z<1.02 else (z-1.02)/.20))
            groups[pair[0]].add([vertex.index],1-t,"REPLACE");groups[pair[1]].add([vertex.index],t,"REPLACE")
        for part in result.parts:
            if not part.obj.data.uv_layers:self.uv(part)
        if role=="SewingWoman":
            bpy.context.view_layer.update()
            # This short older woman's skull sits into her shoulders. Move the
            # entire head assembly, not just its visible face or scarf; retain
            # the shared rig pivot within the cranio-cervical junction.
            for part in result.parts:
                if part.bone=="head":
                    for vertex in part.obj.data.vertices:
                        point=part.obj.matrix_world @ vertex.co
                        point.z-=.045;point.y-=.008
                        vertex.co=part.obj.matrix_world.inverted() @ point
                elif part.obj.name=="GEO_Neck":
                    points=[part.obj.matrix_world @ v.co for v in part.obj.data.vertices]
                    low=min(p.z for p in points);high=max(p.z for p in points)
                    for vertex,point in zip(part.obj.data.vertices,points):
                        point.z-=.045*(point.z-low)/(high-low)
                        vertex.co=part.obj.matrix_world.inverted() @ point
                elif part.obj.name=="CLO_ScarfNeck":
                    points=[part.obj.matrix_world @ v.co for v in part.obj.data.vertices]
                    low=min(p.z for p in points);high=max(p.z for p in points)
                    for vertex,point in zip(part.obj.data.vertices,points):
                        point.z=1.390+(point.z-low)*.065/(high-low)
                        vertex.co=part.obj.matrix_world.inverted() @ point
                elif part.obj.name.startswith("CLO_Collar."):
                    for vertex in part.obj.data.vertices:
                        point=part.obj.matrix_world @ vertex.co
                        point.z=1.405+(point.z-1.405)*.48
                        vertex.co=part.obj.matrix_world.inverted() @ point
                elif part.obj.name=="CLO_WoolShawl":
                    # The wrap lies across shoulder mass, with a short closed
                    # neckline, rather than standing up as a tall funnel.
                    vertices,faces=base.make_vertical_shell(((1.235,.226,.142,.006),
                        (1.365,.251,.158,-.008),(1.418,.154,.128,-.012),
                        (1.447,.077,.084,-.020)),16)
                    for vertex,point in zip(part.obj.data.vertices,vertices):
                        vertex.co=part.obj.matrix_world.inverted() @ Vector(point)
        return result


def solve_grips(rig, targets, strict=True):
    """Analytic two-bone arm solve onto exact grip sockets, no exported IK."""
    for side in ("L","R"):
        if side not in targets:
            continue
        upper=rig.pose.bones["upper_arm."+side]; lower=rig.pose.bones["forearm."+side]
        hand=rig.pose.bones["hand."+side]; socket=rig.pose.bones["SOCKET_Grip."+side]
        # Keep relaxed wrist orientation: a mitten wraps down around each side handle.
        hand_q=hand.matrix.to_quaternion().copy()
        local_grip=hand.bone.matrix_local.inverted() @ socket.bone.head_local
        target=Vector(targets[side]); wrist=target-hand_q @ local_grip
        shoulder=upper.head.copy(); line=wrist-shoulder; distance=line.length
        a=upper.length; b=lower.length
        if not abs(a-b)+.001 < distance < a+b-.001:
            if strict:
                raise RuntimeError(f"Unreachable {side} grip {target}, wrist distance {distance:.3f}, length {a+b:.3f}")
            distance=max(abs(a-b)+.001,min(distance,a+b-.001))
            wrist=shoulder+line.normalized()*distance
            target=wrist+hand_q @ local_grip
        axis=line.normalized(); outward=Vector((1 if side=="L" else -1,.2,-.15))
        bend=(outward-axis*outward.dot(axis)).normalized()
        along=(a*a-b*b+distance*distance)/(2*distance)
        elbow=shoulder+axis*along+bend*math.sqrt(max(0,a*a-along*along))
        for bone,tip in ((upper,elbow),(lower,wrist)):
            direction=(tip-bone.head).normalized()
            rest=(bone.bone.tail_local-bone.bone.head_local).normalized()
            rotation=rest.rotation_difference(direction) @ bone.bone.matrix_local.to_quaternion()
            bone.matrix=Matrix.Translation(bone.head.copy()) @ rotation.to_matrix().to_4x4()
            bpy.context.view_layer.update()
        hand.matrix=Matrix.Translation(wrist) @ hand_q.to_matrix().to_4x4()
        bpy.context.view_layer.update()
        error=(socket.head-target).length
        if error>.0001:
            raise RuntimeError(f"{side} grip residual {error}")


def targets(height, forward=.44):
    return {side:(sign*.29/WOMAN_SCALE,-forward/WOMAN_SCALE,height/WOMAN_SCALE)
            for side,sign in (("L",1),("R",-1))}


def smooth(t):
    t=max(0,min(1,t)); return t*t*(3-2*t)


def action_pose(name, phase):
    neutral=dict(base.CITIZEN_HANGING_ARMS)
    if name in ("Walk","CarryWalk"):
        keys=base.citizen_walk_keys(neutral)
        i=min(7,int(phase*8)); t=(phase-keys[i][0])*8
        pose=base.interpolate_pose(keys[i][1],keys[i+1][1],t)
    else:
        pose=dict(neutral)
    if name=="Idle":
        breath=math.sin(phase*math.tau)**2
        pose["chest"]=base.BonePose(rotation_degrees=(breath*.7,0,0))
    lean=0
    if name in ("Carry","CarryWalk"):
        lean=12
    elif name in ("Reach","Place"):
        p=phase if name=="Reach" else 1-phase
        lean=22*smooth(p*2) if p<=.5 else 22-10*smooth((p-.5)*2)
    elif name in ("StationWork","WoodWork"):
        lean=22*smooth(phase*4) if phase<.25 else 22*smooth((1-phase)*4) if phase>.75 else 22
    if lean:
        pose["spine"]=base.BonePose(rotation_degrees=(lean,0,0))
    return pose


def make_actions(result):
    rig=result.rig
    report=[]
    for name,seconds,loop in CLIPS:
        action=bpy.data.actions.new(name); action.use_fake_user=True
        action.use_frame_range=True; action.frame_start=0; action.frame_end=round(seconds*24)
        action.use_cyclic=loop
        rig.animation_data_create().action=action
        previous_quaternions={}
        for frame in range(round(seconds*24)+1):
            phase=frame/(seconds*24)
            base.reset_pose(rig); base.apply_pose(rig,action_pose(name,phase))
            # Ground every frame BEFORE solving hands, so the carry never bobs away.
            deps=bpy.context.evaluated_depsgraph_get()
            low=min(base.evaluated_part_min_z(p,deps) for p in result.parts if p.bone.startswith("foot."))
            pelvis=rig.pose.bones["pelvis"]; m=pelvis.matrix.copy(); m.translation.z-=low; pelvis.matrix=m
            bpy.context.view_layer.update()
            target=None; blend=1.
            if name in ("Carry","CarryWalk"):
                target=targets(.93)
            elif name in ("Reach","Place"):
                p=phase if name=="Reach" else 1-phase
                if p<=.5:
                    blend=smooth(p*2); target=targets(.81)
                else:
                    target=targets(.81+.12*smooth((p-.5)*2))
            elif name in ("StationWork","WoodWork"):
                blend=smooth(phase*4) if phase<.25 else smooth((1-phase)*4) if phase>.75 else 1
                if name=="StationWork":
                    angle=math.radians(-8)*math.sin(math.pi*max(0,min(1,(phase-.25)*2)))**2
                    height=.80+.035*math.cos(angle)-.61*math.sin(angle)
                    forward=.66-(.31+.035*math.sin(angle)+.61*(math.cos(angle)-1))
                    # Closed front edge is .30 ahead of the crate centre.
                    target={side:(sign*.22/(1.78/1.75),-forward/(1.78/1.75),height/(1.78/1.75)) for side,sign in (("L",1),("R",-1))}
                else:
                    target={side:(sign*.22/WOMAN_SCALE,-.36/WOMAN_SCALE,.86/WOMAN_SCALE) for side,sign in (("L",1),("R",-1))}
            if target is not None:
                if blend<1:
                    target={side:rig.pose.bones["SOCKET_Grip."+side].head.lerp(Vector(target[side]),blend)
                            for side in ("L","R")}
                solve_grips(rig,target,blend>=.9999)
            for bone in rig.pose.bones:
                bone.rotation_mode="QUATERNION"
                if bone.name in previous_quaternions:
                    bone.rotation_quaternion.make_compatible(previous_quaternions[bone.name])
                previous_quaternions[bone.name]=bone.rotation_quaternion.copy()
                bone.keyframe_insert("location",frame=frame,group=bone.name)
                bone.keyframe_insert("rotation_quaternion",frame=frame,group=bone.name)
                bone.keyframe_insert("scale",frame=frame,group=bone.name)
        for curve in base.iter_action_fcurves(action):
            for key in curve.keyframe_points:
                key.interpolation="LINEAR"
        rig.animation_data.action=None
        report.append({"name":name,"duration_seconds":seconds,"loop":loop})
    base.reset_pose(rig)
    return report


LIFE_CLIPS = (("DoorOpen",3.5,False),("DoorClose",3.5,False),
    ("ShovelPickUp",3.,False),("ShovelWork",4.,True),("ShovelPutBack",3.,False),
    ("Gust",2.25,False),("ShovelHold",4.,True)) + tuple(
    item for role in LIFE_ROLES for item in ((role+"Idle",4.,True),
    (role+"Walk",1.5 if role=="SewingWoman" else 1.375 if role=="SnowNeighbor" else 1.25,True))) + (
    ("BasketVisitorReach",3.,False),("BasketVisitorCarry",4.,True),
    ("BasketVisitorCarryWalk",1.5,True),("BasketVisitorPlace",3.,False))


def unity_rotation(x=0.,y=0.,z=0.):
    return Quaternion((0,1,0),math.radians(y)) @ Quaternion((1,0,0),math.radians(x)) @ Quaternion((0,0,1),math.radians(z))


def shovel_pose(name, seconds):
    rest=(Vector((0,0,.48)),unity_rotation());hold=(Vector((0,.14,.40)),unity_rotation(-12))
    def blend(a,b,t):
        return a[0].lerp(b[0],smooth(t)),a[1].slerp(b[1],smooth(t))
    if name=="ShovelPickUp":return blend(rest,hold,(seconds-1.5)/1.5)
    if name=="ShovelPutBack":return blend(rest,hold,(1.5-seconds)/1.5)
    if name!="ShovelWork":return hold
    t=seconds%4.;low=(Vector((0,.02,.48)),unity_rotation());push=(Vector((0,.02,.57)),unity_rotation(4))
    lift=(Vector((-.12,.23,.38)),unity_rotation(-12,0,-18))
    if t<.7:return blend(hold,low,t/.7)
    if t<1.65:return blend(low,push,(t-.7)/.95)
    if t<2.45:return blend(push,lift,(t-1.65)/.8)
    if t<3.2:return blend(lift,hold,(t-2.45)/.75)
    return hold


def world_target(point,scale=1.):
    # Character model wrapper faces +Z in Unity; authoring faces -Y.
    return Vector((-point[0]/scale,-point[2]/scale,point[1]/scale))


def life_pose_and_contacts(name,seconds,duration):
    phase=seconds/duration; pose=dict(base.CITIZEN_HANGING_ARMS); contacts={}; strict=True
    if name.startswith("BasketVisitor") and name[len("BasketVisitor"):] in ("Reach","Place","Carry","CarryWalk"):
        original=name[len("BasketVisitor"):];pose=action_pose(original,phase)
        scale=LIFE_HEIGHTS["BasketVisitor"]/1.75
        height=.93;weight=1
        if original in ("Reach","Place"):
            p=phase if original=="Reach" else 1-phase
            height=.81 if p<.5 else .81+.12*smooth((p-.5)*2)
            weight=smooth(p*2) if p<.5 else 1
            lean=28*smooth(p*2) if p<.5 else 28-12*smooth((p-.5)*2)
        else:lean=16
        pose["spine"]=base.BonePose(rotation_degrees=(lean,0,0))
        contacts={side:(world_target((sign*.29,height,.44),scale),weight) for side,sign in (("L",-1),("R",1))}
        strict=weight>.9999
    elif name.startswith("Shovel"):
        t=seconds;lean=18.;weight=1
        if name in ("ShovelPickUp","ShovelPutBack"):
            p=t/3 if name=="ShovelPickUp" else 1-t/3
            lean=50*smooth(p*2) if p<.5 else 50-32*smooth((p-.5)*2)
            weight=smooth(p*2) if p<.5 else 1
        elif name=="ShovelWork":
            t=t%4
            keys=((0,18),(.7,50),(1.65,53),(2.45,28),(3.2,18),(4,18))
            for (a,x),(b,y) in zip(keys,keys[1:]):
                if a<=t<=b:lean=x+(y-x)*smooth((t-a)/(b-a));break
        pose["spine"]=base.BonePose(rotation_degrees=(lean,0,0))
        pose["head"]=base.BonePose(rotation_degrees=(-lean*.22,0,0))
        crouch=max(0,min(1,(lean-18)/32))
        knee=(12+28*crouch)*weight
        for side in ("L","R"):
            pose["thigh."+side]=base.BonePose(rotation_degrees=(-knee,0,0))
            pose["shin."+side]=base.BonePose(rotation_degrees=(2*knee,0,0))
            pose["foot."+side]=base.BonePose(rotation_degrees=(-knee,0,0))
        position,rotation=shovel_pose(name,seconds)
        scale=LIFE_HEIGHTS["SnowNeighbor"]/1.75
        contacts={side:(world_target(position+rotation @ Vector((0,height,0)),scale),weight)
                  for side,height in (("R",1.05),("L",.65))}
        strict=weight>.9999
    elif name in ("DoorOpen","DoorClose"):
        weight=smooth(seconds) if seconds<1 else smooth(3.5-seconds) if seconds>2.5 else 1
        pose["spine"]=base.BonePose(rotation_degrees=(5*weight,0,0))
        contacts={"R":(world_target((.22,1.05,.40)),weight)};strict=weight>.9999
    elif name=="Gust":
        weight=math.sin(math.pi*phase)**2
        pose["spine"]=base.BonePose(rotation_degrees=(5*weight,0,-3*weight))
        pose["chest"]=base.BonePose(rotation_degrees=(2*weight,0,1.5*weight))
        pose["neck"]=base.BonePose(rotation_degrees=(5*weight,-7*weight,0))
        contacts={"R":(world_target((-.06,1.29,.23)),weight)};strict=weight>.9999
    else:
        role=next(r for r in LIFE_ROLES if name.startswith(r))
        walk=name.endswith("Walk")
        pose=action_pose("Walk" if walk else "Idle",phase)
        if walk:
            stride={"RepairNeighbor":.95,"SewingWoman":.62,"SnowNeighbor":.80,"BasketVisitor":.90}[role]
            for bone in ("thigh.L","thigh.R","shin.L","shin.R","foot.L","foot.R"):
                original=pose[bone]
                pose[bone]=base.BonePose(rotation_degrees=tuple(a*stride for a in original.rotation_degrees),location_m=original.location_m)
        pose["neck"]=base.BonePose(rotation_degrees=({"RepairNeighbor":6,"SewingWoman":4,"SnowNeighbor":2,"BasketVisitor":0}[role],0,0))
    return pose,contacts,strict


def make_life_actions(result):
    rig=result.rig;records=[];curves=[];largest_error=0.;ground_error=0.;samples=0
    for name,duration,loop in LIFE_CLIPS:
        frame_end=round(duration*24);action=bpy.data.actions.new(name);action.use_fake_user=True
        action.use_frame_range=True;action.frame_start=0;action.frame_end=frame_end;action.use_cyclic=loop
        rig.animation_data_create().action=action;previous={}
        for frame in range(frame_end+1):
            seconds=duration*frame/frame_end
            pose,contacts,strict=life_pose_and_contacts(name,seconds,duration)
            base.reset_pose(rig);base.apply_pose(rig,pose)
            deps=bpy.context.evaluated_depsgraph_get()
            low=min(base.evaluated_part_min_z(p,deps) for p in result.parts if p.bone.startswith("foot."))
            pelvis=rig.pose.bones["pelvis"];matrix=pelvis.matrix.copy();matrix.translation.z-=low;pelvis.matrix=matrix
            bpy.context.view_layer.update()
            targets={side:rig.pose.bones["SOCKET_Grip."+side].head.lerp(point,weight)
                     for side,(point,weight) in contacts.items()}
            if targets:
                try:solve_grips(rig,targets,strict)
                except RuntimeError as error:raise RuntimeError(f"{name} frame {frame}: {error}") from error
            for bone in rig.pose.bones:
                bone.rotation_mode="QUATERNION"
                if bone.name in previous:bone.rotation_quaternion.make_compatible(previous[bone.name])
                previous[bone.name]=bone.rotation_quaternion.copy()
                for channel in ("location","rotation_quaternion","scale"):bone.keyframe_insert(channel,frame=frame,group=bone.name)
        for curve in base.iter_action_fcurves(action):
            for key in curve.keyframe_points:key.interpolation="LINEAR"
            curves.append((name,curve.data_path,curve.array_index,[[round(k.co.x,7),round(k.co.y,7)] for k in curve.keyframe_points]))
        # Read the evaluated curves back, independently of the authoring poses.
        for frame in range(frame_end+1):
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update();samples+=1
            _,contacts,strict=life_pose_and_contacts(name,duration*frame/frame_end,duration)
            if strict:
                for side,(point,weight) in contacts.items():
                    largest_error=max(largest_error,(rig.pose.bones["SOCKET_Grip."+side].head-point).length)
            deps=bpy.context.evaluated_depsgraph_get()
            low=min(base.evaluated_part_min_z(p,deps) for p in result.parts if p.bone.startswith("foot."))
            ground_error=max(ground_error,abs(low))
        records.append({"name":name,"duration_seconds":frame_end/24,"loop":loop})
        print(f"Validated {name}: {frame_end+1} authored frames",flush=True)
        rig.animation_data.action=None
    base.reset_pose(rig)
    if largest_error>.002 or ground_error>.002:raise RuntimeError(f"Life action contact={largest_error}, ground={ground_error}")
    # Both directions of every take/put handoff meet the same held pose.
    def snapshot(name,frame):
        rig.animation_data.action=bpy.data.actions[name];bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
        return {b.name:b.matrix.copy() for b in rig.pose.bones}
    endpoint_error=0.
    for hold,reach,place in (("ShovelHold","ShovelPickUp","ShovelPutBack"),("BasketVisitorCarry","BasketVisitorReach","BasketVisitorPlace")):
        end=snapshot(hold,0)
        for name,frame in ((reach,72),(place,0)):
            other=snapshot(name,frame)
            endpoint_error=max(endpoint_error,max(abs(end[n][i][j]-other[n][i][j]) for n in end for i in range(4) for j in range(4)))
    rig.animation_data.action=None;base.reset_pose(rig)
    if endpoint_error>.002:raise RuntimeError(f"Life held endpoints diverge: {endpoint_error}")
    return records,{"sampled_frames":samples,"max_grip_error_m":round(largest_error,7),
        "max_ground_error_m":round(ground_error,7),"max_endpoint_error":round(endpoint_error,7),
        "curve_signature":hashlib.sha256(json.dumps(curves,separators=(",",":")).encode()).hexdigest()}


def build_phase_two(args,assets,sources):
    # This branch never exports any of the first two bodies or their eight clips.
    if args.preview_only:
        for role in LIFE_ROLES:
            if args.preview_role is not None and args.preview_role!=role:continue
            bpy.ops.wm.open_mainfile(filepath=str(sources/(role+".blend")))
            for obj in list(bpy.data.objects):
                if obj.type in ("CAMERA","LIGHT"):bpy.data.objects.remove(obj,do_unlink=True)
            manifest=json.loads((assets/(role+".json")).read_text(encoding="utf8"))
            parts=[base.PartRecord(bpy.data.objects[p["name"]],bpy.data.objects[p["name"]]["bp_bone"],
                bpy.data.objects[p["name"]]["bp_role"],bpy.data.objects[p["name"]]["bp_palette"],tuple(p["color"])) for p in manifest["parts"]]
            result=base.BuildResult(bpy.data.objects["ROOT_Player"],bpy.data.objects["RIG_Player"],
                bpy.data.collections["EXPORT_VillageResident"],parts[0].obj.data.materials[0],parts)
            preview(result,sources/(role+".png"),assets/(role+"Atlas.png"))
            print("Rendered "+role,flush=True)
        return
    protected=[assets/(name+suffix) for name in ROLE_NAMES for suffix in (".fbx",".json","Atlas.png")]
    protected += [assets/("VillageResidentActions"+suffix) for suffix in (".fbx",".json")]
    if args.body_only_role:
        protected += [assets/(role+suffix) for role in LIFE_ROLES if role!=args.body_only_role
                      for suffix in (".fbx",".json","Atlas.png")]
        protected += [assets/("VillageResidentLifeActions"+suffix) for suffix in (".fbx",".json")]
    before={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    for role in LIFE_ROLES:
        if args.body_only_role and args.body_only_role!=role:continue
        texture=assets/(role+"Atlas.png");texture_hash=atlas(texture,role in ("SewingWoman","BasketVisitor"),args.validate_only,role)
        result=LifeResidentBuilder(role).build();metrics=measured(result)
        if metrics["triangle_count"]<2384 or metrics["mesh_count"]<34:raise RuntimeError(f"{role} below hero detail: {metrics}")
        manifest={"generator":Path(__file__).name,"version":"2.0.0","role":role,"anatomy_standard":"NpcHumanV2",
            "height_scale":LIFE_HEIGHTS[role]/1.75,**metrics,"atlas_sha256":texture_hash,"geometry_signature":geometry_signature(result),
            "parts":[{"name":p.obj.name,"color":list(p.color)} for p in result.parts]}
        path=assets/(role+".json")
        if args.validate_only:
            if json.loads(path.read_text(encoding="utf8"))!=manifest:raise RuntimeError(role+" deterministic manifest differs")
        else:
            path.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf8");base.export_fbx(assets/(role+".fbx"),result)
            if not args.no_preview and (args.preview_role is None or args.preview_role==role):preview(result,sources/(role+".png"),texture)
            bpy.ops.wm.save_as_mainfile(filepath=str(sources/(role+".blend")))
            if args.review_neck:
                for suffix,location,turn in (("Front",(0,-4,1.53),0),
                    ("ThreeQuarter",(2.5,-4,1.75),0),("HeadTurn",(0,-4,1.53),30)):
                    for obj in list(bpy.data.objects):
                        if obj.type in ("CAMERA","LIGHT"):bpy.data.objects.remove(obj,do_unlink=True)
                    preview(result,sources/(role+suffix+".png"),texture,location,(0,-.01,1.41),.69,turn)
        print(role,json.dumps(metrics),flush=True)
    if args.body_only_role:
        if before!={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}:raise RuntimeError("Unselected resident asset changed")
        print("Protected all other bodies and both action banks; selected body passed.",flush=True)
        return
    # Feet and joint rests are common. Snow is the measuring body for the tool;
    # the visitor's authored grips compensate her own declared stature.
    result=LifeResidentBuilder("SnowNeighbor").build()
    clips,validation=make_life_actions(result)
    manifest={"generator":Path(__file__).name,"version":"2.0.0","bone_count":31,"fps":24,"clips":clips,
        "door_contact_seconds":[1.,2.5],"shovel_contact_seconds":1.5,
        "shovel_right_grip":[0,1.05,0],"shovel_left_grip":[0,.65,0],"preserved_phase_one":before,"validation":validation}
    path=assets/"VillageResidentLifeActions.json"
    if args.validate_only:
        if json.loads(path.read_text(encoding="utf8"))!=manifest:raise RuntimeError("Deterministic life action manifest differs")
    else:
        base.export_animation_fbx(assets/"VillageResidentLifeActions.fbx",result)
        path.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf8")
        bpy.ops.wm.save_as_mainfile(filepath=str(sources/"VillageResidentLifeActions.blend"))
    if before!={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}:raise RuntimeError("Phase one assets changed")
    if not args.validate_only:
        # Preserve stable Unity identities without guessing serialized importer
        # defaults. The editor setup explicitly configures units and 2D atlases.
        generated=[assets/(role+suffix) for role in LIFE_ROLES for suffix in (".fbx",".json","Atlas.png")]
        generated += [assets/("VillageResidentLifeActions"+suffix) for suffix in (".fbx",".json")]
        for asset in generated:
            meta=asset.with_name(asset.name+".meta")
            if not meta.exists():
                guid=hashlib.sha256(asset.relative_to(ROOT).as_posix().encode()).hexdigest()[:32]
                meta.write_text("fileFormatVersion: 2\nguid: "+guid+"\n",encoding="utf8")
    print(json.dumps(validation),flush=True)


def measured(result):
    bpy.context.view_layer.update()
    verts=[p.obj.matrix_world @ v.co for p in result.parts for v in p.obj.data.vertices]
    triangles=0; deps=bpy.context.evaluated_depsgraph_get()
    for part in result.parts:
        evaluated=part.obj.evaluated_get(deps); mesh=evaluated.to_mesh()
        triangles+=base.triangulated_count(mesh);evaluated.to_mesh_clear()
    return {"mesh_count":len(result.parts),"triangle_count":triangles,
            "bounds_min":[round(min(v[i] for v in verts),6) for i in range(3)],
            "bounds_max":[round(max(v[i] for v in verts),6) for i in range(3)]}


def geometry_signature(result):
    payload=[]
    for part in result.parts:
        payload.append((part.obj.name,part.bone,list(part.color),
            [[round(c,7) for c in v.co] for v in part.obj.data.vertices],
            [list(p.vertices) for p in part.obj.data.polygons],
            [[[g.group,round(g.weight,7)] for g in v.groups] for v in part.obj.data.vertices]))
    return hashlib.sha256(json.dumps(payload,separators=(",",":")).encode()).hexdigest()


def validate_actions(result):
    rig=result.rig; data=rig.animation_data_create(); scene=bpy.context.scene
    maximum_error=0.; maximum_foot_error=0.; sampled=0; curve_data=[]
    for name,seconds,loop in CLIPS:
        action=bpy.data.actions[name]; data.action=action
        for frame in range(round(seconds*24)+1):
            scene.frame_set(frame); bpy.context.view_layer.update(); sampled+=1
            deps=bpy.context.evaluated_depsgraph_get()
            foot=min(base.evaluated_part_min_z(p,deps) for p in result.parts if p.bone.startswith("foot."))
            maximum_foot_error=max(maximum_foot_error,abs(foot))
            expected=None
            if name in ("Carry","CarryWalk"):
                expected=targets(.93)
            elif name=="Reach" and frame>=36:
                expected=targets(.81+.12*smooth((frame/72-.5)*2))
            elif name=="Place" and frame<=36:
                expected=targets(.81+.12*smooth((.5-frame/72)*2))
            if expected:
                for side in ("L","R"):
                    error=(rig.pose.bones["SOCKET_Grip."+side].head-Vector(expected[side])).length*WOMAN_SCALE
                    maximum_error=max(maximum_error,error)
        for curve in base.iter_action_fcurves(action):
            curve_data.append((name,curve.data_path,curve.array_index,
                [[round(k.co.x,7),round(k.co.y,7)] for k in curve.keyframe_points]))
    def endpoint(name,frame):
        data.action=bpy.data.actions[name];scene.frame_set(frame);bpy.context.view_layer.update()
        return {b.name:b.matrix.copy() for b in rig.pose.bones}
    carry=endpoint("Carry",0)
    endpoint_error=0
    for name,frame in (("Reach",72),("Place",0)):
        pose=endpoint(name,frame)
        endpoint_error=max(endpoint_error,max(abs(pose[n][i][j]-carry[n][i][j]) for n in carry for i in range(4) for j in range(4)))
    data.action=None;base.reset_pose(rig)
    if maximum_error>.002 or maximum_foot_error>.002 or endpoint_error>.002:
        raise RuntimeError(f"Animation contract failed: grip={maximum_error}, feet={maximum_foot_error}, endpoint={endpoint_error}")
    return {"sampled_frames":sampled,"max_grip_error_m":round(maximum_error,7),
            "max_ground_error_m":round(maximum_foot_error,7),"max_endpoint_error":round(endpoint_error,7),
            "curve_signature":hashlib.sha256(json.dumps(curve_data,separators=(",",":")).encode()).hexdigest()}


def preview(result,path,atlas_path,camera_location=None,focus=None,ortho_size=None,head_yaw=0):
    mat=result.material; mat.use_nodes=True
    nodes=mat.node_tree.nodes; shader=next(n for n in nodes if n.type=="BSDF_PRINCIPLED")
    obj=nodes.new("ShaderNodeObjectInfo"); tex=nodes.new("ShaderNodeTexImage"); tex.image=bpy.data.images.load(str(atlas_path)); tex.interpolation="Closest"
    mix=nodes.new("ShaderNodeMixRGB"); mix.blend_type="MULTIPLY"; mix.inputs[0].default_value=1
    mat.node_tree.links.new(obj.outputs["Color"],mix.inputs[1]); mat.node_tree.links.new(tex.outputs["Color"],mix.inputs[2]); mat.node_tree.links.new(mix.outputs[0],shader.inputs["Base Color"])
    shader.inputs["Roughness"].default_value=.85
    base.apply_pose(result.rig,base.CITIZEN_HANGING_ARMS)
    if head_yaw:
        bone=result.rig.pose.bones["head"]
        pivot=bone.matrix.translation.copy()
        bone.matrix=Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(head_yaw),4,"Z") @ Matrix.Translation(-pivot) @ bone.matrix
        bpy.context.view_layer.update()
    scene=bpy.context.scene; scene.render.resolution_x=700; scene.render.resolution_y=900
    scene.world=bpy.data.worlds.new("VillageReviewWorld"); scene.world.color=(.22,.24,.26)
    for loc,power,size in (((-3,-4,5),800,5),((3,-1,3),500,4),((0,3,3),700,3)):
        light=bpy.data.lights.new("ReviewSoftbox","AREA"); light.energy=power; light.shape="DISK"; light.size=size
        ob=bpy.data.objects.new("ReviewSoftbox",light); scene.collection.objects.link(ob); ob.location=loc; ob.rotation_euler=(Vector((0,0,1))-ob.location).to_track_quat("-Z","Y").to_euler()
    cam=bpy.data.cameras.new("ReviewCamera"); ob=bpy.data.objects.new("ReviewCamera",cam); scene.collection.objects.link(ob)
    ob.location=camera_location or (2.65,-5.1,2.35); ob.rotation_euler=(Vector(focus or (0,0,.89))-ob.location).to_track_quat("-Z","Y").to_euler(); cam.type="ORTHO";cam.ortho_scale=ortho_size or 2.05;scene.camera=ob
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
    base.reset_pose(result.rig)


def main():
    parser=argparse.ArgumentParser(); parser.add_argument("--no-preview",action="store_true")
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--preview-role",choices=ROLE_NAMES+LIFE_ROLES)
    parser.add_argument("--phase-two",action="store_true")
    parser.add_argument("--preview-only",action="store_true")
    parser.add_argument("--body-only-role",choices=LIFE_ROLES)
    parser.add_argument("--review-neck",action="store_true")
    parser.add_argument("--workroom",action="store_true")
    parser.add_argument("--workroom-probe",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    assets=ROOT/"Assets/Resources/VillageLife"; sources=ROOT/"ArtSource/VillageLife"
    assets.mkdir(parents=True,exist_ok=True); sources.mkdir(parents=True,exist_ok=True)
    if args.workroom:
        module_spec=importlib.util.spec_from_file_location("village_workroom_actions",Path(__file__).with_name("build-village-workroom-actions-3d-model.py"))
        module=importlib.util.module_from_spec(module_spec);module_spec.loader.exec_module(module)
        module.build(sys.modules[__name__],args,assets,sources)
        return
    if args.phase_two:
        build_phase_two(args,assets,sources)
        return
    for index,name in enumerate(ROLE_NAMES):
        texture=assets/(name+"Atlas.png"); texture_hash=atlas(texture,index==1,args.validate_only)
        result=ResidentBuilder(index==1).build(); metrics=measured(result)
        if metrics["triangle_count"]<2384 or metrics["mesh_count"]<34:
            raise RuntimeError(f"Resident must preserve or exceed the hero's silhouette/detail density: {metrics}")
        if len(result.rig.data.bones)!=31:
            raise RuntimeError("Village body must retain NpcHumanV2")
        payload={"generator":Path(__file__).name,"version":VERSION,"role":name,"anatomy_standard":"NpcHumanV2",
                 "height_scale":1.78/1.75 if index==0 else WOMAN_SCALE,**metrics,"atlas_sha256":texture_hash,
                 "parts":[{"name":p.obj.name,"color":list(p.color)} for p in result.parts]}
        payload["geometry_signature"]=geometry_signature(result)
        payload["signature"]=hashlib.sha256(json.dumps(payload,sort_keys=True).encode()).hexdigest()
        if args.validate_only:
            recorded=json.loads((assets/(name+".json")).read_text(encoding="utf8"))
            if recorded!=payload: raise RuntimeError(name+" deterministic body manifest differs")
        else:
            (assets/(name+".json")).write_text(json.dumps(payload,indent=2)+"\n",encoding="utf8")
            base.export_fbx(assets/(name+".fbx"),result)
            if not args.no_preview and (args.preview_role is None or args.preview_role==name):
                preview(result,sources/(name+".png"),texture)
            bpy.ops.wm.save_as_mainfile(filepath=str(sources/(name+".blend")))
        print(name,json.dumps(metrics),flush=True)
    clips=make_actions(result)
    validation=validate_actions(result)
    manifest={"generator":Path(__file__).name,"version":VERSION,"bone_count":31,"fps":24,"clips":clips,
              "pickup_contact_seconds":1.5,"place_contact_seconds":1.5,"basket_grip_half_width":.29,
              "basket_grip_height":.43,"basket_rest_local":[0,.38,.44],"basket_carry_local":[0,.50,.44],
              "validation":validation}
    if args.validate_only:
        recorded=json.loads((assets/"VillageResidentActions.json").read_text(encoding="utf8"))
        if recorded!=manifest: raise RuntimeError("Deterministic action manifest differs")
    else:
        base.export_animation_fbx(assets/"VillageResidentActions.fbx",result)
        (assets/"VillageResidentActions.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf8")
        bpy.ops.wm.save_as_mainfile(filepath=str(sources/"VillageResidentActions.blend"))
    print(json.dumps(validation),flush=True)


if __name__=="__main__":
    main()
