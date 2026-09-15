#!/usr/bin/env python3
"""Deterministic passive City fair kit, authored in Unity metres in Blender.

Small worn market furniture, actual loose goods, manual organ, rope bell and
facade garland. The world owns vendors, collision, light and all interaction.
Every solid is checked for outward winding before Unity-to-Blender conversion.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
import interior_kit as kit

VERSION = "1.1.0"
NAMES = ("Stall", "Bread", "Fruit", "Pottery", "WoodenToys", "Organ", "Bell",
         "Clutter", "Garland", "Bench")
MODEL_DIR = ROOT / "Assets/Resources/City/Fair"
SOURCE_DIR = ROOT / "ArtSource/City/Fair"
# Visual sRGB values. Runtime converts these exactly once, in its shared owner.
SURFACES = {
    "Timber": (.46, .34, .23), "TimberLight": (.58, .46, .31),
    "TimberDark": (.25, .20, .16), "FabricPlum": (.40, .26, .34),
    "FabricCream": (.67, .61, .48), "Steel": (.23, .25, .24),
    "Brass": (.56, .43, .22), "Rubber": (.115, .12, .115),
    "Bread": (.67, .40, .15), "Crust": (.40, .215, .075),
    "Flour": (.77, .64, .40), "Apple": (.53, .205, .13),
    "Pear": (.58, .58, .245), "Leaf": (.27, .33, .17),
    "Clay": (.57, .31, .195), "Glaze": (.28, .435, .40),
    "Rope": (.57, .50, .35), "Bulb": (.95, .70, .36),
}


def translated(geometry, p):
    return kit.translated(geometry, p)


def ring(profile, sides=12):
    """A closed ring-profile solid around Unity Y; an inner profile makes a cup."""
    verts = [(math.cos(i*math.tau/sides)*r, y, math.sin(i*math.tau/sides)*r)
             for y, r in profile for i in range(sides)]
    rows = [list(range(j*sides, (j+1)*sides)) for j in range(len(profile))]
    faces = [tuple(rows[0]), tuple(reversed(rows[-1]))]
    for a, b in zip(rows, rows[1:]):
        faces.extend((a[(i+1)%sides], a[i], b[i], b[(i+1)%sides]) for i in range(sides))
    return verts, faces


def rod(a, b, radius, sides=8):
    a, b = Vector(a), Vector(b)
    direction = (b-a).normalized()
    tangent = direction.cross(Vector((0, 1, 0)))
    if tangent.length < .1:
        tangent = direction.cross(Vector((1, 0, 0)))
    tangent.normalize()
    second = direction.cross(tangent).normalized()
    verts = [tuple(p + radius*(math.cos(i*math.tau/sides)*tangent +
              math.sin(i*math.tau/sides)*second)) for p in (a,b) for i in range(sides)]
    faces = [tuple(reversed(range(sides))), tuple(range(sides, sides*2))]
    faces.extend((i, (i+1)%sides, (i+1)%sides+sides, i+sides) for i in range(sides))
    return verts, faces


def ellipsoid(p, size, sides=10, rows=6):
    # Small capped rings avoid zero-area pole faces while preserving the silhouette.
    profile = []
    for j in range(rows+1):
        angle = .025+(math.pi-.05)*j/rows
        profile.append((-math.cos(angle)*size[1]*.5, math.sin(angle)*.5))
    geom = ring(profile, sides)
    verts = [(x*size[0]+p[0], y+p[1], z*size[2]+p[2]) for x,y,z in geom[0]]
    return verts, geom[1]


class Item:
    def __init__(self, name):
        self.name, self.parts, self.anchors, self.solids = name, {}, {}, 0

    def add(self, role, geometry, group="Body"):
        volume = bp.signed_volume(geometry)
        if volume <= 1e-11:
            raise RuntimeError(f"Inward or degenerate fair solid {self.name}/{group}/{role}: {volume}")
        converted = bp.to_source(geometry)
        if bp.signed_volume(converted) <= 1e-11:
            raise RuntimeError("Unity-axis swap lost solid winding")
        self.parts.setdefault((group,role), []).append(geometry)
        self.solids += 1

    def box(self, role, p, size, group="Body", chamfer=.009):
        self.add(role, bp.u_box(p, size, min(chamfer, min(size)*.23)), group)

    def rod(self, role, a, b, radius, group="Body", sides=8):
        self.add(role, rod(a,b,radius,sides), group)

    def anchor(self, name, p, parent=None):
        self.anchors[name] = {"position": list(p), "parent": parent}


def crate(item, p, size, group="Body"):
    x,y,z = p; w,h,d = size
    # Open ends, individually spaced slats and corner fastenings expose real depth.
    for side in (-1,1):
        for end in (-1,1):
            item.box("TimberDark", (x+side*(w/2-.028),y+h/2,z+end*(d/2-.028)), (.048,h,.048), group)
        for level in range(3):
            item.box("Timber", (x,y+.055+level*(h-.075)/3,z+side*(d/2-.015)), (w,.055,.03), group)
            item.box("TimberLight", (x+side*(w/2-.015),y+.055+level*(h-.075)/3,z), (.03,.055,d), group)
    for slat in range(4):
        item.box("Timber", (x+(slat-1.5)*w/4,y+.017,z), (w/4-.008,.034,d), group)


def basket(item, p, r, h):
    item.add("Rope", translated(ring([(0,r*.7),(.025,r*.76),(h,r),
                  (h+.025,r),(h+.025,r-.025),(.035,r*.7-.018)],14),p))
    for row in range(4):
        y = h*(row+.5)/4
        radius = r*(.76+.24*y/h)
        item.add("TimberLight", translated(ring([(y-.009,radius+.007),(y+.009,radius+.007),
                       (y+.009,radius-.007),(y-.009,radius-.007)],14), p))
    for i in range(14):
        a=i*math.tau/14
        item.rod("TimberDark", (p[0]+math.cos(a)*r*.74,p[1]+.025,p[2]+math.sin(a)*r*.74),
                 (p[0]+math.cos(a)*r,p[1]+h,p[2]+math.sin(a)*r), .008)


def wheel(item, p, r, group="Body"):
    geometry = bp.u_rotated(ring([(-.035,r),(.035,r),(.035,r-.04),(-.035,r-.04)],14),(0,0,90))
    item.add("Rubber", translated(geometry,p),group)
    item.rod("Steel", (p[0]-.065,p[1],p[2]),(p[0]+.065,p[1],p[2]),.05,group)
    for i in range(6):
        a=i*math.tau/6
        item.rod("TimberDark",p,(p[0],p[1]+math.sin(a)*(r-.02),p[2]+math.cos(a)*(r-.02)),.018,group)


def make_stall():
    item=Item("Stall")
    # Narrow posts keep the vendor visible from all three public sides.
    for x in (-1.32,1.32):
        for z in (-1.0,1.0):
            item.box("TimberDark",(x,1.175,z),(.095,2.35,.095))
            item.box("Steel",(x,.07,z),(.115,.14,.115))
        item.box("Timber",(x,.54,.60),(.10,.77,.71))
        item.rod("TimberDark",(x,1.78,-1),(x,2.30,-.50),.035)
    for i in range(8):
        item.box("TimberLight" if i%3==0 else "Timber",((i-3.5)*.332,.902,.5125),(.324,.096,.975))
        item.box("Timber" if i%2==0 else "TimberDark",((i-3.5)*.333,.525,.963),(.324,.64,.052))
    item.box("TimberDark",(0,.24,.60),(2.70,.095,.10))
    item.box("TimberLight",(0,.842,1.005),(2.76,.115,.058))
    item.box("TimberDark",(0,2.28,-1),(2.8,.10,.10))
    item.box("TimberDark",(0,2.28,1),(2.8,.10,.10))
    item.box("Timber",(0,.36,-.90),(2.54,.055,.25))
    # Gabled taut canvas is a closed strip mesh; scalloped hanging edges are solids.
    for stripe in range(10):
        x0=-1.49+stripe*.298; x1=x0+.298
        verts=[(x,y,z) for x in (x0,x1) for z,y in ((-1.2,2.29),(0,2.56),(1.2,2.29),
                   (1.2,2.27),(0,2.54),(-1.2,2.27))]
        faces=[(5,4,3,2,1,0),(6,7,8,9,10,11)]+[(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)]
        # Extrusion is along X, and its profile was traversed clockwise in YZ.
        if bp.signed_volume((verts,faces))<0: faces=[tuple(reversed(f)) for f in faces]
        role="FabricPlum" if stripe%2==0 else "FabricCream"
        item.add(role,(verts,faces))
        for z in (-1.195,1.195):
            for segment in range(3):
                x=x0+(segment+.5)*.099
                height=.15+(.045 if segment==1 else 0)
                item.box(role,(x,2.29-height/2,z),(.098,height,.024),chamfer=.004)
        for z in (-1.15,1.15):
            item.rod("Rope",(x0+.012,2.29,z),(x0+.012,2.265,z),.009)
    # Honest repair: a stitched patch and a darker replacement board, without text.
    patch=bp.u_rotated(bp.u_plate((0,0,0),(.31,.012,.26)),(12.7,0,0))
    item.add("FabricCream", translated(patch,(-.65,2.37,.80)))
    for x in (-1.18,-.82,-.49,-.16,.16,.49,.82,1.18):
        item.rod("Steel",(x,.82,1.028),(x,.82,1.039),.013)
    item.anchor("VendorAnchor",(0,0,-.60))
    item.anchor("CounterAnchor",(0,.95,.625))
    return item


def make_bread():
    item=Item("Bread")
    for x in (-.72,.20):
        crate(item,(x,.952,.58),(.76,.095,.55))
    for n,(x,z,angle) in enumerate(((-.96,.46,-8),(-.53,.55,5),(-.80,.72,2),(.02,.42,13),(.35,.62,-4))):
        y=1.095
        geom=ellipsoid((0,0,0),(.33,.17,.18),12)
        item.add("Bread",translated(bp.u_rotated(geom,(0,angle,0)),(x,y,z)))
        for slash in range(3):
            sx=x+(slash-1)*.068
            item.rod("Flour",(sx-.020,y+.070,z-.055),(sx+.020,y+.080,z+.045),.012)
    for x,z in ((.88,.42),(.91,.68),(.65,.62)):
        item.add("Crust",ellipsoid((x,1.04,z),(.19,.155,.18),10))
        item.rod("Flour",(x-.045,1.110,z),(x+.045,1.110,z),.008)
    item.box("FabricCream",(.84,.959,.58),(.50,.014,.52))
    return item


def make_fruit():
    item=Item("Fruit")
    for x in (-.65,.53):
        crate(item,(x,.952,.60),(.96,.205,.64))
    for n in range(12):
        x=-.98+(n%4)*.21; z=.395+(n//4)*.20; y=1.10+(.05 if n in (5,6) else 0)
        item.add("Apple",ellipsoid((x,y,z),(.175,.17,.173),10))
        item.rod("TimberDark",(x,y+.074,z),(x+.01,y+.11,z),.009, sides=6)
    for n in range(8):
        x=.25+(n%3)*.24; z=.40+(n//3)*.19
        item.add("Pear",translated(ring([(0,.055),(.04,.085),(.095,.083),(.15,.049),(.19,.022)],10),(x,1.01,z)))
        item.rod("TimberDark",(x,1.193,z),(x-.01,1.23,z),.007,sides=6)
        if n%3==0:
            item.add("Leaf",ellipsoid((x+.025,1.195,z),(.085,.012,.036),6,4))
    return item


def handle(item,p,radius,height,role="Glaze",group="Body"):
    last=None
    for i in range(9):
        a=-math.pi*.5+i*math.pi/8
        point=(p[0]+math.cos(a)*radius,p[1]+math.sin(a)*height*.5,p[2])
        if last is not None: item.rod(role,last,point,.018,group)
        last=point


def make_pottery():
    item=Item("Pottery")
    item.box("FabricCream",(0,.958,.60),(2.40,.016,.68))
    for n,(x,z) in enumerate(((-1,.43),(-.72,.69),(-.35,.47),(-.04,.69))):
        item.add("Glaze" if n%2==0 else "Clay",translated(ring([(0,.064),(.018,.068),(.17,.089),
                    (.18,.089),(.18,.074),(.026,.050)],12),(x,.972,z)))
        handle(item,(x+.08,1.06,z),.067,.13,"Glaze" if n%2==0 else "Clay")
    for x,z,r in ((.28,.44,.18),(.54,.72,.15)):
        item.add("Clay",translated(ring([(0,r*.44),(.03,r*.55),(.125,r),(.145,r),
                     (.145,r-.020),(.035,r*.45)],14),(x,.973,z)))
    item.add("Glaze",translated(ring([(0,.11),(.04,.135),(.18,.155),(.28,.078),
                  (.365,.073),(.385,.092),(.385,.070),(.295,.056),(.265,.055)],14),(.99,.975,.64)))
    handle(item,(1.07,1.195,.64),.12,.245)
    return item


def make_toys():
    item=Item("WoodenToys")
    item.box("FabricCream",(0,.958,.61),(2.42,.016,.68))
    # Two recognisable wheeled toy horses, pegged necks and upturned heads.
    for x,z in ((-.78,.51),(-.21,.73)):
        item.box("TimberLight",(x,1.10,z),(.30,.14,.11))
        item.box("Timber",(x+.105,1.205,z),(.07,.18,.085))
        item.box("TimberLight",(x+.145,1.29,z),(.15,.09,.09))
        item.rod("TimberDark",(x-.155,1.14,z),(x-.21,1.20,z),.021)
        for a in (-.10,.10):
            for side in (-1,1):
                item.box("Timber",(x+a,1.015,z+side*.045),(.033,.085,.032))
            item.rod("Steel",(x+a,.993,z-.10),(x+a,.993,z+.10),.016)
            for side in (-1,1):
                item.add("Clay",translated(bp.u_rotated(bp.u_cylinder((0,0,0),(.079,.014,.079),8),(90,0,0)),(x+a,.998,z+side*.095)))
    # Peg dolls and spinning tops read without painted faces or iconography.
    for x,z in ((.42,.40),(.69,.47),(.99,.71)):
        item.add("TimberLight",translated(ring([(0,.060),(.045,.067),(.14,.030),(.17,.022)],10),(x,.972,z)))
        item.add("Timber",ellipsoid((x,1.17,z),(.083,.083,.083),10))
    for x,z in ((.07,.40),(.43,.73),(.87,.39)):
        item.add("Clay",translated(ring([(0,.005),(.042,.074),(.077,.054),(.085,.011),(.125,.011)],10),(x,.974,z)))
    return item


def make_organ():
    item=Item("Organ")
    for x in (-.44,.44):
        wheel(item,(x,.23,-.04),.23)
    item.box("TimberDark",(0,.34,-.02),(1.06,.11,.76))
    item.box("Timber",(0,.76,-.06),(.87,.75,.64))
    item.box("TimberLight",(0,1.17,-.06),(.98,.105,.72))
    item.box("TimberDark",(0,.52,.275),(.81,.105,.045))
    item.box("TimberDark",(0,1.075,.275),(.81,.078,.045))
    for i in range(9):
        x=(i-4)*.078; h=.23+.21*(1-abs(i-4)/5)
        item.rod("Brass",(x,.61,.286),(x,.61+h,.286),.025)
        item.box("Steel",(x,.647,.313),(.019,.027,.018),chamfer=.002)
    for x in (-.41,.41):
        item.box("TimberLight",(x,.805,.294),(.055,.57,.064))
        item.rod("TimberDark",(x,.39,-.35),(x,.62,-.40),.033)
    # Scroll-like cap, wooden lid battens, and transport handles keep it a handcart.
    for x in (-.26,.26):
        item.box("TimberDark",(x,1.227,-.06),(.035,.015,.64))
    item.anchor("CrankPivot",(.32,1.10,.43))
    item.rod("Brass",(.32,1.10,.295),(.32,1.10,.47),.027,"CrankPivot")
    item.rod("Brass",(.32,1.10,.47),(.32,1.28,.47),.025,"CrankPivot")
    item.rod("TimberDark",(.32,1.28,.46),(.32,1.28,.63),.032,"CrankPivot",10)
    item.anchor("CrankGripAnchor",(.32,1.28,.58),"CrankPivot")
    item.anchor("InteractionDock",(.54,0,1.08))
    item.anchor("AudioAnchor",(0,.85,.28))
    return item


def make_bell():
    item=Item("Bell")
    for x in (-.34,.34):
        item.box("TimberDark",(x,.045,0),(.13,.09,.65))
        item.box("Timber",(x,.99,0),(.085,1.91,.085))
        item.rod("Steel",(x,.1,-.28),(x,.56,0),.025)
        item.rod("Steel",(x,.1,.28),(x,.56,0),.025)
    item.box("TimberDark",(0,1.91,0),(.80,.14,.13))
    item.rod("Steel",(-.35,1.76,0),(.35,1.76,0),.035)
    item.anchor("BellSwingPivot",(0,1.76,0))
    item.add("Brass", translated(ring([(-.50,.25),(-.47,.255),(-.43,.222),(-.35,.17),
                    (-.15,.092),(-.10,.05),(-.095,.025),(-.16,.07),(-.36,.145),(-.46,.21)],16),(0,1.76,0)),"BellSwingPivot")
    item.rod("Steel",(0,1.72,0),(0,1.28,0),.021,"BellSwingPivot")
    item.add("Steel",ellipsoid((0,1.28,0),(.085,.11,.085),10),"BellSwingPivot")
    item.rod("Steel",(.02,1.76,0),(.25,1.62,.24),.022,"BellSwingPivot")
    item.anchor("RopeTopAnchor",(.25,1.62,.24),"BellSwingPivot")
    item.anchor("RopePivot",(.25,1.56,.24))
    item.add("Rope",ellipsoid((.25,1.105,.24),(.054,.071,.054),8),"RopePivot")
    item.anchor("RopeGripAnchor",(.25,1.13,.24),"RopePivot")
    item.anchor("RopeTailAnchor",(.25,1.08,.24),"RopePivot")
    # The hand moves the knot; this independent flexible span stretches between
    # the swinging lever and moving tail instead of tearing away from the lever.
    item.anchor("RopeSpanPivot",(.25,1.62,.24))
    item.rod("Rope",(.25,1.62,.24),(.25,1.08,.24),.018,"RopeSpanPivot")
    item.anchor("RopeSpanTopAnchor",(.25,1.62,.24),"RopeSpanPivot")
    item.anchor("RopeSpanBottomAnchor",(.25,1.08,.24),"RopeSpanPivot")
    item.anchor("InteractionDock",(.47,0,.76))
    item.anchor("AudioAnchor",(0,1.42,0))
    return item


def make_clutter():
    item=Item("Clutter")
    crate(item,(-.50,0,.06),(.56,.38,.47))
    crate(item,(-.50,.38,.06),(.46,.31,.42))
    basket(item,(.02,0,.23),.19,.29)
    # Little empty barrow with open slatted bed and two handles.
    crate(item,(.48,.28,-.08),(.60,.26,.45))
    for x in (.18,.78): wheel(item,(x,.17,-.08),.17)
    for x in (.24,.71):
        item.rod("TimberDark",(x,.26,.1),(x,.65,.43),.026)
    # Open galvanised bin with rolled rim, rather than a sealed cylinder.
    item.add("Steel",translated(ring([(0,.14),(.04,.15),(.47,.18),(.49,.185),
                        (.49,.161),(.045,.128)],12),(-.03,0,-.26)))
    for side in (-1,1):
        item.rod("Steel",(-.03+side*.19,.37,-.31),(-.03+side*.19,.37,-.21),.019)
    return item


def make_garland():
    item=Item("Garland")
    def point(t): return (10*t,-.65*4*t*(1-t),0)
    for n in range(40): item.rod("Steel",point(n/40),point((n+1)/40),.012,"Wire")
    for index in range(13):
        p=point((index+.5)/13); name=f"Fixture_{index:02d}"
        item.anchor(name,p)
        item.rod("Steel",p,(p[0],p[1]-.10,0),.013,name)
        item.add("Steel",translated(ring([(-.13,.031),(-.08,.023)],8),p),name)
        item.add("Bulb",ellipsoid((p[0],p[1]-.183,0),(.082,.108,.082),10),name)
    for index,p in enumerate((point(0),point(1))):
        name=f"Mount_{index:02d}"; item.anchor(name,p)
        item.box("Steel",(p[0],p[1],-.02),(.13,.18,.04),name)
        item.rod("Brass",(p[0],p[1],0),(p[0],p[1],.075),.027,name)
    item.anchor("SpanStart",(0,0,0))
    item.anchor("SpanEnd",(10,0,0))
    return item


def make_bench():
    item=Item("Bench")
    for x in (-.66,.66):
        for z in (-.25,.23):
            item.box("Steel",(x,.235,z),(.064,.47,.064))
            item.box("Steel",(x,.023,z),(.14,.046,.12))
        item.box("Steel",(x,.426,0),(.07,.07,.62))
        item.rod("Steel",(x,.35,-.245),(x,.89,-.315),.029)
    for z in (-.21,-.07,.07,.21):
        item.box("Timber" if z<0 else "TimberLight",(0,.474,z),(1.8,.052,.132))
    for y in (.685,.82):
        item.box("Timber",(0,y,-.30),(1.80,.12,.055))
    item.box("Steel",(0,.27,-.235),(1.39,.045,.045))
    item.anchor("SeatAnchor",(0,.50,.07))
    return item


def make_items():
    return [make_stall(),make_bread(),make_fruit(),make_pottery(),make_toys(),
            make_organ(),make_bell(),make_clutter(),make_garland(),make_bench()]


def signature(items):
    payload={"version":VERSION,"surfaces":SURFACES,"items":[]}
    for item in items:
        record={"name":item.name,"anchors":item.anchors,"parts":[]}
        for (group,role),solids in sorted(item.parts.items()):
            g=kit.merge_all(solids)
            record["parts"].append({"group":group,"role":role,"vertices":[[round(v,7) for v in p] for p in g[0]],"faces":g[1]})
        payload["items"].append(record)
    return hashlib.sha256(json.dumps(payload,sort_keys=True,separators=(",",":")).encode()).hexdigest()


def make_material(role):
    mat=bpy.data.materials.new("Fair_"+role); mat.diffuse_color=(*SURFACES[role],1)
    mat.use_nodes=True; shader=mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value=(*SURFACES[role],1)
    shader.inputs["Roughness"].default_value=.80 if role not in ("Brass","Glaze") else .38
    if role=="Bulb":
        shader.inputs["Emission Color"].default_value=(*SURFACES[role],1)
        shader.inputs["Emission Strength"].default_value=1.3
    return mat


def empty(name,parent=None,p=(0,0,0)):
    obj=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(obj)
    obj.parent=parent; obj.location=(p[0],p[2],p[1]); return obj


def build_objects(items):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system="METRIC"
    mats={name:make_material(name) for name in SURFACES}
    roots=[]
    for item in items:
        root=empty(item.name); roots.append(root); groups={"Body":root}
        for name,anchor in item.anchors.items():
            parent=groups.get(anchor["parent"],root)
            pp=item.anchors[anchor["parent"]]["position"] if anchor["parent"] else (0,0,0)
            groups[name]=empty(name,parent,tuple(v-b for v,b in zip(anchor["position"],pp)))
        for (group,role),solids in item.parts.items():
            if group not in groups: groups[group]=empty(group,root)
            offset=item.anchors[group]["position"] if group in item.anchors else (0,0,0)
            geom=bp.to_source(translated(kit.merge_all(solids),tuple(-v for v in offset)))
            mesh=bpy.data.meshes.new(item.name+"_"+group+"__"+role)
            mesh.from_pydata(geom[0],[],geom[1]); mesh.update(calc_edges=True)
            uv=mesh.uv_layers.new(name="UVMap")
            for face in mesh.polygons:
                axis=max(range(3),key=lambda i:abs(face.normal[i]))
                for loop in face.loop_indices:
                    p=mesh.vertices[mesh.loops[loop].vertex_index].co
                    uv.data[loop].uv=(p[1],p[2]) if axis==0 else (p[0],p[2]) if axis==1 else (p[0],p[1])
            mesh.materials.append(mats[role])
            obj=bpy.data.objects.new(group+"__"+role,mesh); bpy.context.collection.objects.link(obj)
            obj.parent=groups[group]; obj["bp_surface"]=role
    bpy.context.view_layer.update()
    return roots


def manifest(items,checksum):
    records=[]
    for item in items:
        all_geometry=kit.merge_all(g for solids in item.parts.values() for g in solids)
        low,high=kit.bounds(all_geometry)
        records.append({"name":item.name,"bounds_min":[round(v,6) for v in low],
            "bounds_max":[round(v,6) for v in high],"triangle_count":kit.triangle_count(all_geometry),
            "solid_count":item.solids,"mesh_count":len(item.parts),
            "anchors":[{"name":n,"position":a["position"],"parent":a["parent"]} for n,a in item.anchors.items()]})
    triangles=sum(v["triangle_count"] for v in records)
    if triangles>48000: raise RuntimeError(f"Fair triangle budget exceeded: {triangles}")
    return {"generator":"tools/build-city-fair-3d-model.py","generator_version":VERSION,
        "blender_version":bpy.app.version_string,"build_signature":checksum,
        "units":"metres","unity_forward":"+Z","unity_up":"+Y","passive":True,
        "fbx_bake_space_transform":False,"exported_fbx_validation":"mesh and anchor world metres after FBX round trip",
        "signed_volumes":"every solid positive before and after axis swap",
        "determinism":"two independent geometry builds match","triangle_count":triangles,
        "materials":SURFACES,"models":records}


def export(root,path):
    bpy.ops.object.select_all(action="DESELECT"); root.select_set(True)
    for obj in root.children_recursive: obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"EMPTY","MESH"},
        # The experimental bake rotates children of nested empties twice. Keep
        # their hierarchy transforms, exactly as the working port exporter does.
        axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_space_transform=False,
        add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE",use_custom_props=True)


def verify_fbx(model_dir, payload):
    """Measure the exported hierarchy after FBX import, including nested pivots.

    Pure geometry checks cannot see exporter space baking that rotates nested
    mesh data twice. This round trip measures the produced file, not the plan.
    """
    for expected in payload["models"]:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(model_dir/(expected["name"]+".fbx")),use_anim=False)
        bpy.context.view_layer.update()
        points=[]
        for obj in bpy.context.scene.objects:
            if obj.type!="MESH": continue
            vertices=[obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
            points.extend((p.x,p.z,p.y) for p in vertices)
        low=[min(p[i] for p in points) for i in range(3)]
        high=[max(p[i] for p in points) for i in range(3)]
        print("FBX ROUND TRIP",expected["name"],"min",[round(v,6) for v in low],"max",[round(v,6) for v in high])
        if any(abs(actual-target)>.002 for actual,target in zip(low+high,expected["bounds_min"]+expected["bounds_max"])):
            for obj in bpy.context.scene.objects:
                p=obj.matrix_world.translation
                print("  FBX NODE",obj.name,"parent",obj.parent.name if obj.parent else "none",
                      "position",tuple(round(v,6) for v in (p.x,p.z,p.y)),
                      "rotation",tuple(round(math.degrees(v),3) for v in obj.matrix_world.to_euler()))
            raise RuntimeError("Exported FBX world geometry differs from authored metres: "+expected["name"])
        for anchor in expected["anchors"]:
            matches=[obj for obj in bpy.context.scene.objects if obj.type=="EMPTY" and obj.name.split('.')[0]==anchor["name"]]
            if len(matches)!=1: raise RuntimeError("Exported fair anchor missing: "+anchor["name"])
            p=matches[0].matrix_world.translation
            if any(abs(actual-target)>.002 for actual,target in zip((p.x,p.z,p.y),anchor["position"])):
                raise RuntimeError("Exported fair anchor moved: "+anchor["name"])
    print("CITY FAIR EXPORTED FBX ROUND TRIP OK")


def preview(roots,path):
    # Present products on the complete stall; remaining kit is grouped by use.
    placements=[(-4,0,0),(-4,0,0),(-.6,0,0),(2.4,0,0),(5.4,0,0),
                (-3.9,2.9,0),(-1.6,2.9,0),(.6,2.9,0),(-5,0,3.4),(3.1,2.9,0)]
    for root,p in zip(roots,placements): root.location=p
    # Other three displays sit on their own duplicates of the stall shell.
    for index in (2,3,4):
        shell=roots[0].copy(); bpy.context.collection.objects.link(shell); shell.location=placements[index]
        for child in roots[0].children_recursive:
            if child.type!="MESH": continue
            duplicate=child.copy(); bpy.context.collection.objects.link(duplicate); duplicate.parent=shell
    scene=bpy.context.scene; scene.render.engine="BLENDER_EEVEE"
    scene.render.resolution_x=1800; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("Fair Preview World"); scene.world.color=(.075,.079,.084)
    for name,p,power,size in (("Key",(0,3,12),2200,10),("Fill",(-7,-4,7),1000,8)):
        data=bpy.data.lights.new(name,"AREA"); data.energy=power; data.shape="DISK"; data.size=size
        obj=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(obj); obj.location=p
        obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat("-Z","Y").to_euler()
    data=bpy.data.cameras.new("Preview"); camera=bpy.data.objects.new("Preview",data)
    bpy.context.collection.objects.link(camera); camera.location=(11,16,11)
    camera.rotation_euler=(Vector((.8,1.1,1.1))-camera.location).to_track_quat("-Z","Y").to_euler()
    data.type="ORTHO";data.ortho_scale=18.5;scene.camera=camera
    scene.render.filepath=str(path);scene.render.image_settings.file_format="PNG"
    bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=MODEL_DIR)
    parser.add_argument("--source-dir",type=Path,default=SOURCE_DIR)
    parser.add_argument("--no-preview",action="store_true")
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--verify-fbx",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    items=make_items();checksum=signature(items)
    if signature(make_items())!=checksum: raise RuntimeError("Fair generation is not deterministic")
    payload=manifest(items,checksum)
    if args.verify_fbx:
        verify_fbx(args.model_dir,payload)
        return
    if not args.validate_only:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        roots=build_objects(items)
        for root in roots: export(root,args.model_dir/(root.name+".fbx"))
        (args.model_dir/"CityFair3D.json").write_text(json.dumps(payload,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"CityFair3D.blend"),check_existing=False)
        if not args.no_preview: preview(roots,args.source_dir/"CityFair3D.png")
        verify_fbx(args.model_dir,payload)
    print("CITY FAIR 3D CONTRACT OK")
    print(f"Models: {len(items)}; triangles: {payload['triangle_count']}; signature: {checksum}")
    print("Repeated geometry signatures, outward solid volumes, metric bounds and contact anchors validated.")


if __name__=="__main__": main()
