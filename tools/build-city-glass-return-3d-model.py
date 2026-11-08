#!/usr/bin/env python3
"""Build the fixed-metre Ridge glass collection pavilion, sorting yard and wall floodlight."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import interior_kit as kit


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, TOOLS / filename)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


port = module("return_port_geometry", "build-city-port-3d-model.py")
litter = module("return_litter_geometry", "build-city-litter-3d-model.py")
OUT = TOOLS.parent / "Assets/Resources/City/GlassReturn"
ANCHORS = {
    "Worker": (0, 0, .89), "Counter": (0, 1.02, 1.60),
    "BottleRest": (-.32, 1.01, 1.37), "CrateSlot": (.38, 1, 1.35),
    "Clink": (.38, 1, 1.35), "WorkLight": (-.88, 2.44, .94),
    "LightTarget": (.18, 1, 1.46),
}
GLASS = {"GlassGreen": (.19,.31,.21,1), "GlassBrown": (.31,.19,.09,1),
         "GlassClear": (.48,.53,.51,1)}
PORT = {"Brick": port.RUST, "Plaster": port.CABIN, "Concrete": port.CONCRETE,
        "Timber": port.WOOD, "Steel": port.PAINT, "SteelDark": port.DARK,
        "Roof": port.DARK, "Lens": port.LAMP, **GLASS}
port.SURFACE_TILES.update({"Brick": 1.5, "Lens": 1, **{k: 1 for k in GLASS}})


class Model:
    """One joined mesh per visible surface. The movable bottle has its own root."""
    def __init__(self, root):
        self.root = root
        self.geometry = {}

    def part(self, role):
        if role not in self.geometry:
            g = self.geometry[role] = port.Geometry()
            g.role = role
        return self.geometry[role]

    def box(self, role, center, size, bevel=0):
        if bevel:
            port.chamfer(self.part(role), center, size, PORT[role], bevel)
        else:
            self.part(role).box(center, size, PORT[role])

    def rod(self, role, a, b, radius, sides=6):
        self.part(role).rod(a, b, radius, PORT[role], sides)

    def emit(self, material):
        for role, geo in self.geometry.items():
            # The semantic role suffix also selects the runtime shared material.
            if role == "Lens":
                geo.object("LampLens",self.root,material)
            else:
                port.obj(geo,self.root.name+role,self.root,material)


def bottle(model, role, foot, full=False):
    # The same continuous thick-wall/lip construction as the City's glass catalog.
    # Only the rim cavity and visible outer shoulder survive on the joined stack meshes.
    x,y,z = foot
    scale = {"GlassGreen": (1,1), "GlassBrown": (1.1,.92), "GlassClear": (.94,1.05)}[role]
    profile = [(0,.031),(.012,.037),(.174,.037),(.202,.020),(.212,.014),
               (.265,.013),(.269,.016),(.276,.016)]
    if full:
        verts,faces = litter.hollow(profile,.003,12)
    else:
        # The crate hides the foot of each sorted bottle. Each open throat still
        # has its own dark cavity, inner wall and lip rather than a solid cap.
        verts,faces = litter.hollow([(.130,.037),(.174,.037),(.205,.014),(.265,.014),
                                     (.269,.016),(.276,.016)], .003, 7)
    points = [(x+a*scale[0],y+b*scale[1],z+c*scale[0]) for a,b,c in verts]
    model.part(role).add(points,faces,PORT[role])


def crate(model, x, base, z, filled=None, height=.23):
    # Reusable slatted wooden return crate. Only the open top tier carries
    # individual glass; closed lower tiers remain visible between the slats.
    w, d = .69, .48
    model.box("Timber",(x,base+.02,z),(w,.04,d),.004)
    for side in (-1,1):
        for h in (.072, height-.034):
            model.box("Timber",(x,base+h,z+side*(d/2-.012)),(w,.07,.024),.003)
            model.box("Timber",(x+side*(w/2-.012),base+h,z),(.024,.07,d-.042),.003)
        for side_z in (-1,1):
            model.box("Timber",(x+side*.307,base+height/2,z+side_z*.203),
                      (.045,height,.045))
    if filled:
        for i in range(5):
            for j in range(3):
                # Occupied crates are sorted by colour with occasional empty cells.
                if (i==0 and j==2) or (i==4 and j==0):
                    continue
                bottle(model, filled, (x+(i-2)*.114,base+.04,z+(j-1)*.113))


def pavilion(material):
    root = port.empty("Pavilion")
    m = Model(root)
    # Brick masonry has a real 2.62 m wide opening, a deep reveal, and a
    # differently finished inner leaf. The counter has a separate work bay.
    wall = kit.wall_run(4.48,2.70,.18,(kit.Opening(0,2.62,2.20,.95),))
    port.kit_add(m.part("Brick"),kit.translated(wall,(0,1.48,0)),PORT["Brick"])
    m.box("Brick",(0,1.35,-1.49),(4.48,2.70,.18))
    for x in (-2.15,2.15):
        m.box("Brick",(x,1.35,0),(.18,2.70,2.80))
    m.box("Plaster",(0,1.36,-1.38),(4.10,2.62,.036))
    for x in (-2.04,2.04):
        m.box("Plaster",(x,1.36,0),(.036,2.62,2.77))
    m.box("Concrete",(0,-.225,0),(4.6,.45,3.2),.013)
    m.box("Roof",(0,2.755,0),(4.6,.11,3.2),.015)
    # Front fascia, canopy lip, folded standing seams, two triangulated brackets.
    m.box("SteelDark",(0,2.33,1.94),(3.57,.105,.88),.018)
    m.box("SteelDark",(0,2.27,2.35),(3.58,.15,.06),.010)
    for x in (-1.60,-.80,0,.80,1.60):
        m.box("Roof",(x,2.39,1.94),(.025,.02,.86))
    for x in (-1.53,1.53):
        m.rod("SteelDark",(x,1.96,1.58),(x,2.27,2.23),.026)
        m.box("SteelDark",(x,2.12,1.59),(.09,.41,.055),.005)
    for x in (-1.31,1.31):
        m.box("Steel",(x,1.57,1.49),(.058,1.25,.20),.005)
    m.box("Steel",(0,2.19,1.50),(2.67,.055,.20),.004)
    m.box("Steel",(0,.985,1.62),(2.75,.07,.33),.009)
    # Old paint at the inside, worn timber under the sliding glass and a
    # practical sorting crate with one open cell precisely at the hand target.
    m.box("Timber",(-.57,.975,1.32),(1.10,.07,.31),.010)
    m.box("Timber",(.53,.935,1.25),(.76,.05,.48),.006)
    crate(m,.53,.96,1.25)
    for bx,bz in ((.66,1.15),(.79,1.15),(.78,1.35)):
        bottle(m,"GlassClear",(bx,1.00,bz),full=True)
    for side in (-1,1):
        m.box("Timber",(side*1.19,.492,1.40),(.062,.98,.062))
    m.box("SteelDark",(-.88,2.50,.94),(.68,.093,.22),.012)
    m.box("Lens",(-.88,2.448,.94),(.57,.016,.158),.005)
    m.box("SteelDark",(-.88,2.59,.94),(.09,.16,.08))
    # Rear shelving silhouettes create depth behind the worker.
    for y in (.42,1.04,1.68):
        m.box("Timber",(.39,y,-1.03),(2.08,.055,.46),.007)
    for x in (-.62,1.40):
        m.box("SteelDark",(x,.90,-1.02),(.038,1.80,.038))
    for i,(x,role) in enumerate(((-.16,"GlassGreen"),(.58,"GlassBrown"))):
        crate(m,x,.448,-1.03,role if i == 0 else None)
    # Closed service leaf opens into the yard between collections.
    m.box("Steel",(2.25,1.065,-.48),(.035,2.13,.90),.008)
    m.box("SteelDark",(2.28,1.06,-.82),(.035,.04,.15),.004)
    for y in (.30,1.82):
        m.box("SteelDark",(2.276,y,-.07),(.045,.12,.06))
    m.emit(material)
    for key, pos in ANCHORS.items():
        port.empty("ANCHOR_"+key, root, pos)
    moving = Model(port.empty("MOVE_Bottle",root,ANCHORS["BottleRest"]))
    # Profile height .276 m; working grip is 19 cm above the foot.
    bottle(moving,"GlassGreen",(0,0,0),full=True)
    moving.emit(material)
    return root


def mesh_panel(model, a, b):
    # Two clipped families of crossing galvanized strands, one mesh for all
    # four sides. All strands end on the rails or uprights; there is no
    # alpha-tested double layer shimmer and no collider per strand.
    ax,az=a; bx,bz=b
    length=math.hypot(bx-ax,bz-az)
    low,high=.18,1.49
    def pos(s,y): return (ax+(bx-ax)*s/length, y, az+(bz-az)*s/length)
    for y in (low-.025,high+.025):
        model.rod("SteelDark",pos(0,y),pos(length,y),.021)
    for slope in (-1,1):
        # x = offset + slope * (y - low). Trim both ends to the panel.
        k0=math.floor(-abs(high-low)/.26)-1
        for k in range(k0,math.ceil((length+high-low)/.26)+1):
            offset=k*.26
            t0,t1=sorted((-offset/slope,(length-offset)/slope))
            t0=max(0,t0); t1=min(high-low,t1)
            if t1-t0>.012:
                model.rod("SteelDark",pos(offset+slope*t0,low+t0),
                          pos(offset+slope*t1,low+t1),.0065,4)


def yard(material):
    root = port.empty("Yard")
    m = Model(root)
    m.box("Concrete",(0,-.225,0),(4.2,.45,3.8),.012)
    west,east,back,front=-2.045,2.045,-1.845,1.845
    corners=((west,back),(west,front),(east,front),(east,back))
    for a,b in zip(corners,corners[1:]+corners[:1]):
        if a[0] == b[0] == east:
            mesh_panel(m,a,(east,.64))
            mesh_panel(m,(east,.58),(east,-.46))
            mesh_panel(m,(east,-.52),b)
        else:
            mesh_panel(m,a,b)
    for x,z in (*corners,(east,.62),(east,-.50),(west,0),(0,front),(0,back)):
        m.box("SteelDark",(x,.81,z),(.07,1.62,.07),.005)
        m.box("Concrete",(x,.07,z),(.105,.14,.105),.004)
    # Gate hinge straps and a horizontal latch at hand height, on the road face.
    for h in (.32,1.27):
        m.box("Steel",(east+.015,h,-.38),(.065,.06,.30),.004)
    m.box("Steel",(east+.018,.91,.55),(.07,.07,.28),.004)
    # The back row rises; three sorted foreground crates leave a work aisle.
    for ix,(cx,role,levels) in enumerate(((-1.57,"GlassGreen",3),(-.80,"GlassGreen",2),
                                         (-.025,"GlassBrown",3),(.745,"GlassBrown",2),
                                         (1.51,"GlassClear",2))):
        for j in range(levels):
            crate(m,cx,j*.25,-1.30,role if j==levels-1 else None)
    for x,z,h,role in ((-1.53,-.57,2,"GlassGreen"),(-1.46,.17,1,"GlassClear"),
                       (.73,-.57,1,"GlassBrown"),(1.48,-.55,1,"GlassClear")):
        for j in range(h):
            crate(m,x,j*.25,z,role if j==h-1 else None)
    m.emit(material)
    return root


def wall_floodlight(material):
    root = port.empty("WallFloodlight")
    # Keep the same City service fitting as the courtyard. Its plate and aimable
    # head come from the original deterministic geometry, with a separate lens.
    mount = Model(root)
    plate = litter.misc.build_yard_spotlight_wall_mount().parts[0].geometry
    port.kit_add(mount.part("SteelDark"), plate, PORT["SteelDark"])
    for x in (-.226,.226):
        mount.rod("SteelDark",(x,0,-.09),(x,0,.08),.018)
    mount.rod("SteelDark",(-.258,0,.08),(.258,0,.08),.022)
    mount.emit(material)
    hinge = port.empty("MOVE_Head",root,(0,0,.08))
    head = Model(hinge)
    shell = litter.misc.build_yard_spotlight_head_shell().parts[0].geometry
    port.kit_add(head.part("SteelDark"), kit.translated(shell,(0,.1645,0)), PORT["SteelDark"])
    head.emit(material)
    lens = port.Geometry()
    port.chamfer(lens,(0,0,.201),(.374,.196,.008),port.LAMP,.0015)
    lens.object("FloodlightLens",hinge,material)
    port.empty("ANCHOR_Source",hinge,(0,0,.205))
    return root


def generate():
    port.base.reset()
    mat=port.base.material("GlassReturnPaint")
    roots=[pavilion(mat),yard(mat),wall_floodlight(mat)]
    descriptions=[]
    for root, expected_min, expected_max, limit in zip(roots,
            ((-2.30,-.45,-1.60),(-2.10,-.45,-1.90),(-.31,-.21,-.18)),
            ((2.30,2.81,2.38),(2.10,1.62,1.90),(.31,.21,.285)), (13000,32000,900)):
        desc=port.describe(root)
        for key,want in (("bounds_min",expected_min),("bounds_max",expected_max)):
            if max(abs(a-b) for a,b in zip(desc[key],want)) > .001:
                raise ValueError(f"{root.name}: incorrect physical {key} {desc[key]}")
        if desc["triangles"]>limit:
            raise ValueError(f"{root.name} exceeded triangle limit {limit}: {desc['triangles']}")
        desc["renderer_count"]=sum(o.type=="MESH" for o in root.children_recursive)
        if desc["renderer_count"]>13:
            raise ValueError(f"{root.name} surface batches exceeded")
        desc["anchors"]=[a for a in desc["anchors"] if a["name"].startswith(("ANCHOR_","MOVE_"))]
        descriptions.append(desc)
    mesh=next(o for o in roots[0].children_recursive if o.type=="MESH" and o.name.endswith("__Brick"))
    for x in (-1.2,0,1.2):
        if mesh.ray_cast(Vector(port.source((x,1.5,1.2))),Vector(port.source((0,0,1))),distance=.5)[0]:
            raise ValueError("Pavilion window is sealed")
    source=next(o for o in roots[2].children_recursive if o.name=="ANCHOR_Source")
    if source.parent.name!="MOVE_Head" or abs(port.source(source.location)[2]-.205) > .0001:
        raise ValueError("Floodlight source must move with the face of the hinged head")
    return roots,{"design_id":"city_ridge_glass_return_v1","coordinate_system":"Unity metres; +Z counter; Y=0 floor",
                  "generator": Path(__file__).name, "working_grip_above_base": .19, "models":descriptions}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--only-model", choices=("Pavilion","Yard","WallFloodlight"),
                        help="Export one revised FBX after checking the unchanged published siblings.")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    roots,contract=generate()
    manifest=OUT/"GlassReturn3D.json"
    if args.validate_only:
        if json.loads(manifest.read_text()) != contract or any(not (OUT/(r.name+".fbx")).exists() for r in roots):
            raise ValueError("Published glass return model contract differs from the rebuilt geometry")
    else:
        OUT.mkdir(parents=True,exist_ok=True)
        previous = {m["name"]:m for m in json.loads(manifest.read_text())["models"]} if manifest.exists() else {}
        for root, desc in zip(roots,contract["models"]):
            path = OUT/(root.name+".fbx")
            if not args.only_model or root.name==args.only_model:
                port.base.export(root,path)
            elif previous.get(root.name) != desc or not path.exists():
                raise ValueError(f"{root.name} needs a full export before updating a single model")
        manifest.write_text(json.dumps(contract,indent=2)+"\n")
    if generate()[1] != contract:
        raise ValueError("Glass return model is not deterministic")
    print("GLASS RETURN ART CONTRACT OK", [(r["name"],r["triangles"],r["renderer_count"]) for r in contract["models"]],flush=True)


if __name__=="__main__":
    main()
