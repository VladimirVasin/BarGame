#!/usr/bin/env python3
"""Hand-built raised municipal pigeon loft. Fixed Unity metres, +Z rear, -Z work face."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import sys
import zlib

sys.dont_write_bytecode = True
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as parts
import interior_kit as kit

NAME = "PigeonLoft"
OUT = ROOT / "Assets/Resources/City/PigeonLoft"
SOURCE = ROOT / "ArtSource/City/PigeonLoft"
# Swatch alphas supply the opaque URP material's smoothness.
SURFACES = {
    "sage": ((107, 121, 111), .27), "fade": ((133, 139, 123), .25),
    "moss": ((88, 109, 99), .28), "bare": ((132, 111, 86), .23),
    "tar": ((78, 76, 67), .24), "zinc": ((133, 141, 135), .52),
    "roof": ((103, 111, 109), .47), "iron": ((69, 72, 65), .44),
    "rust": ((119, 92, 67), .28), "stone": ((139, 137, 123), .17),
    "enamel": ((155, 164, 147), .5), "grain": ((166, 145, 95), .20),
    "water": ((111, 132, 125), .8), "straw": ((140, 119, 80), .24),
    "latch": ((162, 151, 123), .57), "lens": ((214, 202, 160), .42),
}
PIVOTS = {"Feeder": (.28, 2.75, -1.35), "Water": (.86, 2.75, -1.35),
          "Scoop": (-.32, 2.79, -1.39), "Broom": (-1.02, 2.91, -1.31), "WaterTap": (1.08,3.11,-1.27),
          "FeederGrain": (0,.035,0),"WaterStream": (.86,2.959,-1.35)}
SUPPORTS = [(-1.35,-2.12),(-1.35,1.03),(1.35,-2.12),(1.35,1.03),(-2.39,-1.86),(-1.65,-1.86)]
for i,(x,z) in enumerate(SUPPORTS):
    PIVOTS[f"Support{i}"] = (x,1.92,z)
MARKS = {"FloorAnchor": (0, 2, -1.75), "KeeperWorkAnchor": (0, 2, -1.75),
         "DoorwayAnchor": (0, 2, -1.2), "LampAnchor": (.49, 3.85, -1.45),
         "LampAviaryAnchor": (2.014, 3.732, 0),
         "FeederPerch": (.28, 2.73, -1.57), "WaterPerch": (.86, 2.73, -1.57),
         "Perch3": (1.83, 3.88, -.81), "Perch4": (0, 4.50, .70),
         "Perch5": (-.98, 3.59, -1.43),
         "PerchGround6": (-.65, 0, -2.72), "PerchGround7": (.60, 0, -2.80)}
for row, height in enumerate((2.75, 3.38)):
    for index, z in enumerate((-.67, 0, .67)):
        MARKS[f"PerchCovered{3*row+index}"] = (1.64, height, z)
PROP_MARKS = {
    "Water": {"WaterGripRight": (.12, .108, -.066), "WaterGripLeft": (-.12, .108, -.066)},
    "Broom": {"BroomLowerGrip": (0, -.285, 0), "BroomFoot": (0, -.90, 0)},
}
STAIR = {"x": -2.02, "width": .9, "foot_z": 1.7, "top_z": -1.3,
         "rise": 2 / 12, "tread": .25, "steps": 12}
WOOD = ("sage", "fade", "moss", "bare", "tar")


def atlas():
    cell = 192
    image = np.zeros((4*cell, 4*cell, 4), dtype=np.uint8)
    y, x = np.mgrid[0:cell, 0:cell] / cell
    for tile, (key, (rgb, smoothness)) in enumerate(SURFACES.items()):
        rng = np.random.default_rng(4301+tile)
        grit = rng.normal(size=x.shape)
        wave = np.sin(8.3*x+1.8*np.sin(4.8*y)) + .35*np.sin(23*x+11*y)
        shade = 2.1*grit+2*wave
        if key in WOOD:
            # Long fibre, splitting finish, small knots and thin, dull bare edges.
            rings = 56*x+2.6*np.sin(6.1*y+1.2*x)+1.4*wave
            knots = np.sqrt(((x-.43)/.10)**2 + ((y-.69)/.11)**2)
            shade += 4.8*np.sin(rings)+3*np.sin(3*rings) + 8*np.sin(13*knots)*np.exp(-knots)
            chip = np.clip((np.sin(121*x+wave)*.73 + np.sin(29*y+7*x)*.35-.57)*2.7,0,1)
            tone = np.array(rgb) + shade[:,:,None] + chip[:,:,None]*np.array([14,3,-7])
        else:
            if key in ("roof", "zinc", "iron"):
                shade += 3*np.sin(85*x+wave) + 3*np.maximum(0,np.sin(15*y+9*wave))
            if key in ("grain", "straw", "stone"):
                shade += 2.8*grit
            tone = np.array(rgb) + shade[:,:,None]
        row, col = divmod(tile,4)
        image[row*cell:(row+1)*cell,col*cell:(col+1)*cell] = np.dstack(
            (np.clip(tone,0,255), np.clip(smoothness*255 - 3*wave,0,255))).astype(np.uint8)
    def chunk(tag, data):
        return struct.pack(">I",len(data))+tag+data+struct.pack(">I", zlib.crc32(tag+data))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB",4*cell,4*cell,8,6,0,0,0)) +
            chunk(b"IDAT",zlib.compress(b"".join(b"\0"+row.tobytes() for row in image),9)) + chunk(b"IEND",b""))


class Assembly:
    def __init__(self):
        self.solids = []

    def add(self, surface, solid):
        vertices, faces = solid
        volume = parts.signed_volume(solid)
        if abs(volume) < 1e-12:
            raise ValueError("Open or collapsed " + surface)
        if volume < 0:
            faces = [tuple(reversed(f)) for f in faces]
        self.solids.append((surface, (vertices, faces)))

    def box(self, surface, xyz, dims, bevel=.005):
        self.add(surface, parts.u_box(xyz, dims, bevel))

    def bar(self, surf, a, b, width, depth=None, bevel=.004):
        delta = Vector(b)-Vector(a)
        mid = (Vector(a)+Vector(b))*.5
        verts, faces = parts.u_box((0,0,0),(width,delta.length,depth or width),bevel)
        rot = Vector((0,1,0)).rotation_difference(delta)
        self.add(surf, ([tuple(mid + rot @ Vector(p)) for p in verts],faces))

    def disk(self, surface, xyz, radius, height, sides=12):
        self.add(surface,parts.u_cylinder(xyz,(radius*2,height/2,radius*2),sides))

    def turned(self,surface,xyz,profile,sides=20):
        vertices, faces = kit.lathe(profile,sides)
        self.add(surface,kit.translated(([(x,h,z) for x,z,h in vertices],faces),xyz))


def structure():
    m = Assembly()
    # Spalled shoes and diagonal timber ties support the loft with open air beneath.
    for x in (-1.35, 1.35):
        m.bar("bare",(x,.38,.98),(x,1.8,-1.98),.085,.075)
    for z in (-2.12,-1.34,.98):
        left=-2.52 if z<0 else -1.50
        m.box("tar",((1.53+left)*.5,1.83,z),(1.53-left,.20,.13))
    for i in range(19):
        x=-1.5+(i+.5)*3/19
        m.box(("bare","tar","bare")[i%3],(x,1.965,0),(3/19-.005,.07,2.4))
    for i in range(7):
        z=-2.28+(i+.5)*1.08/7
        # The left landing ends at the top of the staircase.
        left=-2.52 if z<=-1.30 else -1.50
        m.box(("bare","fade","bare")[i%3],((1.53+left)*.5,1.965,z),(1.53-left,.07,1.08/7-.004))
    for x in (-2.43,-1.61):
        m.bar("tar",(x,.035,1.65),(x,1.94,-1.33),.10,.14)
    for i in range(12):
        y=(i+1)*STAIR["rise"]
        z=1.7-(i+.5)*.25
        m.box("bare",(-2.02,y-.025,z),(.90,.05,.25),.007)
        m.box("iron",(-2.02,y-.035,z+.116),(.88,.046,.014),.002)
    # Outer guard and only the exposed deck edges. The eastward route stays open.
    for z in (1.62,.65,-.32,-1.28,-2.21):
        top = min(2,(1.7-z)/.25*STAIR["rise"]+.10)
        m.box("iron",(-2.45,top+.43,z),(.043,.89,.043))
    m.bar("bare",(-2.45,1.01,1.62),(-2.45,2.98,-1.33),.065,.060)
    m.bar("bare",(-2.45,2.98,-1.33),(-2.45,2.98,-2.22),.065,.060)
    for x in (-2.45,-1.37,-.27,.72,1.47):
        m.box("moss",(x,2.48,-2.22),(.064,.96,.064))
    for y in (2.27,2.95):
        m.box("bare",(-.49,y,-2.22),(3.98,.052,.065))
        m.bar("bare",(1.47,y,-2.22),(1.47,y,-1.22),.052,.065)
    # Sawn upright siding, joints and physical gable infill. The forward door is inset.
    def gable(x):
        return 4.43-.39*abs(x)/1.50
    boards=("sage","sage","fade","moss","sage","fade","sage")
    for i in range(19):
        x=-1.5+(i+.5)*3/19
        high=gable(x)
        for side in (-1,1):
            low=3.82 if side<0 and abs(x)<.42 else 2.03
            m.box(boards[(i+side)%7],(x,(low+high)*.5,side*1.16),(3/19-.004,high-low,.083))
    for i in range(16):
        z=-1.16+(i+.5)*2.32/16
        m.box(boards[i%7],(-1.455,3.035,z),(.09,2.01,2.32/16-.004))
        # Right-hand aviary stands against a sheltered, shadowed wall.
        m.box("tar",(1.42,3.035,z),(.08,2.01,2.32/16-.004))
    for x in (-1.47,1.47):
        for z in (-1.17,1.17):
            m.box("moss",(x,3.03,z),(.101,2.06,.101))
    for i in range(6):
        x=-.43+(i+.5)*.86/6
        m.box(("fade","sage","fade")[i%3],(x,2.91,-1.124),(.86/6-.003,1.77,.065))
    for x in (-.46,.46):
        m.box("bare",(x,2.91,-1.191),(.067,1.84,.067))
    m.box("moss",(0,3.827,-1.195),(.99,.087,.085))
    for y in (2.28,3.57):
        m.box("iron",(-.37,y,-1.168),(.129,.035,.020),.003)
    m.box("iron",(.299,2.98,-1.179),(.052,.123,.032),.004)
    m.box("latch",(.265,2.99,-1.206),(.094,.024,.038),.004)
    # Folded standing-seam saddle. Fascia boards are separately bolted to the end.
    for sign in (-1,1):
        m.bar("roof",(0,4.434,0),(sign*1.68,3.997,0),.062,2.83)
        for z in (-1.385,-.96,-.51,-.06,.40,.86,1.365):
            m.bar("zinc",(0,4.466,z),(sign*1.68,4.029,z),.022,.024)
        for z in (-1.376,1.376):
            m.bar("fade",(0,4.39,z),(sign*1.66,3.959,z),.099,.067)
    m.box("zinc",(0,4.482,0),(.11,.036,2.87))
    return m


def roosts(m):
    # Six unobstructed flying holes, under a folded awning, with distinct footholds.
    # Only the end panels have wire. The keeper has left the front trap doors open.
    for z in (-1.10,1.10):
        for x in (1.49,1.92):
            m.box("moss",(x,3.05,z),(.045,1.60,.045))
        for i in range(1,7):
            x=1.49+i*.060
            m.bar("iron",(x,2.28,z),(x,3.81,z),.010,.010,0)
        for i in range(1,15):
            y=2.28+.105*i
            m.bar("iron",(1.49,y,z),(1.92,y,z),.010,.010,0)
    for z in (-1.03,-.335,.335,1.03):
        m.box("fade",(1.72,3.07,z),(.48,1.6,.035))
    for y in (2.75,3.38):
        for z in (-.67,0,.67):
            m.box("bare",(1.70,y-.025,z),(.49,.05,.61))
            m.box("moss",(1.922,y+.025,z),(.040,.058,.49))
            # Center of landing board stays free through the open front.
            m.box("bare",(1.865,y-.016,z),(.19,.032,.40))
    m.box("roof",(1.72,3.85,0),(.56,.060,2.33))
    m.box("zinc",(1.98,3.808,0),(.038,.075,2.34))
    # A hood hooked over the folded fascia exposes the lens below the front hem.
    # Its outside shield leaves the inboard, downward beam clear of its housing.
    m.box("iron",(1.975,3.802,0),(.16,.032,.17))
    m.box("enamel",(2.040,3.770,0),(.033,.082,.165),.006)
    for z in (-.077,.077):
        m.box("iron",(2.011,3.757,z),(.075,.045,.012),.003)
    m.box("bare",(-.99,3.562,-1.35),(.62,.056,.30))
    for x in (-1.23,-.76):
        m.bar("iron",(x,3.34,-1.19),(x,3.535,-1.46),.032)
    # Maintenance shelf, twin flat perches, work lamp and its folded rain hood.
    m.box("bare",(.24,2.725,-1.349),(1.60,.05,.285))
    for x in (-.46,.89):
        m.bar("iron",(x,2.51,-1.21),(x,2.69,-1.469),.027)
    for x in (.28,.86):
        m.box("bare",(x,2.711,-1.557),(.27,.038,.115))
    m.box("iron",(.49,3.88,-1.33),(.30,.074,.28))
    m.box("enamel",(.49,3.872,-1.44),(.32,.056,.23))
    m.box("lens",(.49,3.836,-1.45),(.22,.024,.14),.008)
    for x in (.42,.56):
        m.bar("iron",(x,3.862,-1.52),(x,3.819,-1.52),.010,.010,0)
    # Galvanized gravity tank, crimped lid, tap and downturned supply spout.
    m.box("zinc",(.84,3.40,-1.275),(.39,.40,.17),.023)
    m.box("iron",(.84,3.614,-1.275),(.416,.031,.194),.008)
    m.bar("zinc",(.86,3.21,-1.28),(.86,3.02,-1.28),.030)
    m.bar("zinc",(.86,3.02,-1.28),(.86,3.02,-1.35),.030)
    m.bar("zinc",(.86,3.02,-1.35),(.86,2.959,-1.35),.030)
    m.bar("iron",(.86,3.11,-1.27),(1.055,3.11,-1.27),.030)
    # Stool and grain sack beneath the deck give the open supports human scale.
    m.box("bare",(.70,.365,-1.93),(.46,.065,.35))
    for x in (.53,.87):
        for z in (-2.05,-1.81):
            m.bar("tar",(x,.01,z),(x,.337,z),.048)
    m.box("straw",(1.04,.20,-1.62),(.29,.38,.20),.065)
    m.box("bare",(1.04,.395,-1.62),(.18,.030,.095),.008)


def utensils():
    feeder, water, scoop, broom = (Assembly() for _ in range(4))
    # Each prop is built around its real handling pivot in Unity metres.
    feeder.box("bare",(0,.022,0),(.40,.044,.21))
    for z in (-.099,.099):
        feeder.box("zinc",(0,.073,z),(.396,.098,.018))
    for x in (-.193,.193):
        feeder.box("bare",(x,.079,0),(.016,.112,.212))
    stock=Assembly()
    stock.box("grain",(0,.028,0),(.365,.041,.176),.009)
    for i in range(16):
        stock.disk("grain",(-.157+.022*i,.050+.002*(i%2),.049*math.sin(2.7*i)),.008,.006,6)
    water.turned("enamel",(0,0,0),[(.083,0),(.098,.015),(.154,.102),(.155,.114),
                                    (.148,.116),(.141,.104),(.091,.024)],24)
    water.disk("water",(0,.092,0),.133,.004,24)
    # Open sheet-metal scoop with shallow curved side returns, handle origin at the grip.
    scoop.bar("bare",(0,0,-.052),(0,0,.095),.026,.031)
    scoop.box("zinc",(0,-.008,.145),(.105,.014,.119),.004)
    for x in (-.050,.050):
        scoop.box("zinc",(x,.012,.141),(.009,.042,.107),.002)
    scoop.box("zinc",(0,.011,.099),(.106,.04,.014))
    broom.bar("bare",(0,-.77,0),(0,.48,0),.028,.030)
    broom.box("bare",(0,-.772,0),(.229,.046,.057))
    for i in range(15):
        x=-.107+i*.0153
        broom.bar("straw",(x,-.786,0),(x*1.17,-.90,.006*math.sin(i*1.8)),.017,.043,.002)
    aviary_lens=Assembly()
    glass=parts.u_rotated(parts.u_box((0,0,0),(.059,.018,.128),.005),(0,0,-28))
    aviary_lens.add("lens",kit.translated(glass,(2.024,3.754,0)))
    tap=Assembly()
    tap.box("latch",(0,0,0),(.040,.086,.018),.006)
    flow=Assembly()
    flow.disk("water",(0,-.058,0),.0055,.116,6)
    pieces={"LampAviaryLens":aviary_lens, "WaterStream":flow, "Feeder":feeder,"Water":water,"Scoop":scoop,"Broom":broom,"WaterTap":tap,"FeederGrain":stock}
    for i in range(len(SUPPORTS)):
        leg=Assembly()
        thin=i>=4
        foot=.25 if thin else .34
        thick=.13 if thin else .165
        # Each pivot is at the top bearing so runtime can bed the solid at surveyed ground.
        leg.box("tar",(0,-.90,0),(thick,1.80,thick))
        leg.box("stone",(0,-1.83,0),(foot,.20,foot+.012),.018)
        pieces[f"Support{i}"]=leg
    return pieces


def metrics(component):
    geom=kit.merge_all(solid for _,solid in component.solids)
    lo,hi=kit.bounds(geom)
    payload=[(k,[[tuple(round(v,6) for v in p) for p in verts],faces]) for k,(verts,faces) in component.solids]
    return {"min":[round(n,5) for n in lo],"max":[round(n,5) for n in hi],
            "triangles":kit.triangle_count(geom),
            "sha256":hashlib.sha256(json.dumps(payload,separators=(",",":")).encode()).hexdigest()}


def validate_contacts(body):
    mesh=kit.merge_all(solid for _,solid in body.solids)
    tree=BVHTree.FromPolygons(*mesh)
    def contact(point):
        x,y,z=point
        hit=tree.ray_cast(Vector((x,y+.065,z)),Vector((0,-1,0)),.10)[0]
        assert hit is not None and abs(hit.y-y)<.003, ("Unsupported foot contact",point,hit)
    for key,point in MARKS.items():
        if key.startswith("PerchGround") or key in ("LampAnchor","LampAviaryAnchor","DoorwayAnchor"): continue
        contact(point)
    for index in range(STAIR["steps"]):
        z=STAIR["foot_z"]-(index+.5)*STAIR["tread"]
        y=(index+1)*STAIR["rise"]
        for x in (-2.29,-2.02,-1.75):
            contact((x,y,z))
            ceiling=tree.ray_cast(Vector((x,y+.02,z)),Vector((0,1,0)),1.82)[0]
            assert ceiling is None, ("Stair headroom",index,x,ceiling)
    for sign in (-1,1):
        contact((sign*.95,2,-1.895))


def build():
    body=structure()
    roosts(body)
    validate_contacts(body)
    pieces={"LoftStructure":body,**utensils()}
    record={"design":"ridge_pigeon_loft_v1","units":"Unity metres; +Z rear",
            "pieces":{k:metrics(v) for k,v in pieces.items()},"anchors":MARKS,
            "prop_pivots":PIVOTS,"prop_anchors":PROP_MARKS,"stair":STAIR,
            "platform":{"min":[-2.52,1.93,-2.28],"max":[1.53,2,-1.2]},
            "cabin":{"min":[-1.50,2,-1.2],"max":[1.50,4.43,1.2]}}
    b=record["pieces"]["LoftStructure"]
    assert abs(b["max"][1]-4.5)<1e-5
    assert all(record["pieces"][f"Support{i}"]["min"][1]<-1.92 for i in range(len(SUPPORTS)))
    assert b["max"][0]-b["min"][0]<6 and b["max"][2]-b["min"][2]<6
    assert sum(p["triangles"] for p in record["pieces"].values()) < 29000
    return pieces,record


def mesh_object(name,solids,material):
    verts, faces, uvs=[],[],[]
    for i,(surface,geom) in enumerate(solids):
        points,faces_b=parts.to_source(geom)
        q=np.array(geom[0]); low=q.min(axis=0); span=np.maximum(q.max(axis=0)-low,.0001)
        shifted=(q-low)/span
        tile=list(SURFACES).index(surface)
        row,col=divmod(tile,4)
        offset=len(verts)
        verts.extend(points)
        for f in faces_b:
            pts=q[list(f)]
            normal=np.cross(pts[1]-pts[0],pts[2]-pts[0])
            axes=[axis for axis in range(3) if axis!=abs(normal).argmax()]
            if span[axes[0]]>span[axes[1]]:
                axes.reverse()
            # Each individual stave samples a different strip; long fibres follow it.
            uv=shifted[list(f)][:,axes]
            uv[:,0]=.065+((i*13)%29)*.012+uv[:,0]*.55
            if i%3: uv[:,1]=1-uv[:,1]
            uvs.extend(zip((col+.016+.967*uv[:,0])/4,(3-row+.016+.967*uv[:,1])/4))
            faces.append(tuple(offset+j for j in f))
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(verts,[],faces)
    mesh.update()
    mesh.uv_layers.new(name="UVMap").data.foreach_set("uv",np.array(uvs,dtype=np.float32).ravel())
    mesh.materials.append(material)
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def empty(name,parent,xyz,yaw=0):
    obj=bpy.data.objects.new(name,None)
    bpy.context.collection.objects.link(obj)
    obj.parent=parent
    obj.empty_display_size=.05
    obj.location=(xyz[0],xyz[2],xyz[1])
    obj.rotation_euler.z=-math.radians(yaw)
    return obj


def emit(pieces,model_dir,source_dir,texture):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mat=bpy.data.materials.new("PigeonLoftSurface")
    mat.use_nodes=True
    tex=mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image=bpy.data.images.load(str(texture))
    tex.image.alpha_mode="CHANNEL_PACKED"
    mat.node_tree.links.new(tex.outputs["Color"],mat.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
    lamp=bpy.data.materials.new("KioskLampGlass")
    lamp.diffuse_color=(.8,.73,.55,1)
    root=empty(NAME+"_MetricRoot",None,(0,0,0))
    objects={}
    for name,assembly in pieces.items():
        if name=="LampAviaryLens":
            mesh_object(name,assembly.solids,lamp).parent=root
            continue
        solids=[it for it in assembly.solids if it[0]!="lens"]
        obj=mesh_object(name,solids,mat)
        obj.parent=objects["Feeder"] if name=="FeederGrain" else root
        objects[name]=obj
        x,y,z=PIVOTS.get(name,(0,0,0))
        obj.location=(x,z,y)
        for mark,xyz in PROP_MARKS.get(name,{}).items():
            empty(mark,obj,xyz)
        lens=[it for it in assembly.solids if it[0]=="lens"]
        if lens:
            mesh_object("LampLens",lens,lamp).parent=root
    for name,xyz in MARKS.items():
        yaw=90 if name.startswith("PerchCovered") or name in ("Perch3","Perch4") else 180 if name in ("FeederPerch","WaterPerch","Perch5") else 0
        empty(name,root,xyz,yaw)
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(model_dir/(NAME+".fbx")),use_selection=True,object_types={"MESH","EMPTY"},
        axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_space_transform=False,
        add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE",path_mode="STRIP")
    tex.image.filepath="//"+os.path.relpath(OUT/texture.name,SOURCE).replace(os.sep,"/")
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir/(NAME+".blend")),compress=True,relative_remap=False)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=OUT)
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    pieces,record=build()
    assert build()[1]==record,"Non-deterministic loft geometry"
    texture=atlas()
    record["atlas_sha256"]=hashlib.sha256(texture).hexdigest()
    manifest=json.dumps(record,indent=2)+"\n"
    path=args.model_dir/"PigeonLoftSurface.png"
    if args.validate_only:
        assert path.read_bytes()==texture and (args.model_dir/(NAME+".json")).read_text()==manifest
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        path.write_bytes(texture)
        emit(pieces,args.model_dir,args.source_dir,path)
        (args.model_dir/(NAME+".json")).write_text(manifest)
    print("PIGEON LOFT:",record["pieces"]["LoftStructure"])


if __name__=="__main__":
    main()
