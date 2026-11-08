#!/usr/bin/env python3
"""Build the fixed-metre pieces of the cobbler's front yard, using the workshop's surface atlas."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import random
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("cobbler_workshop", TOOLS / "build-city-cobbler-3d-model.py")
workshop = importlib.util.module_from_spec(spec)
spec.loader.exec_module(workshop)
port, Parts = workshop.port, workshop.Parts
OUT, SOURCE = workshop.OUT, workshop.SOURCE
PART_NAMES = ("SeatBench", "Footrest", "BootScraper", "KerbLong", "KerbShort", "Lilac",
              "GravelApproach", "FeedCable")


def octagon(width, depth, cut):
    x, z = width*.5, depth*.5
    return [(-x+cut,-z), (x-cut,-z), (x,-z+cut), (x,z-cut),
            (x-cut,z), (-x+cut,z), (-x,z-cut), (-x,-z+cut)]


def bench(m, mat):
    # The paired vinyl backs, tubular ends and grab rail retain the proportions
    # of rigid public-bus seats. Two weather-grey sleepers anchor the reused frame.
    for x in (-.48,.48):
        m.box("wear", (x,.025,-.016), (.14,.066,.59), .012)
        for z in (-.204,.186):
            m.box("iron", (x,.064,z), (.103,.024,.09), .007)
            m.rod("iron", (x,.067,z),(x,.384,z*.82), .021, 8)
    for z in (-.158,.16):
        m.rod("iron",(-.637,.386,z),(.637,.386,z),.023,8)
    for x in (-.32,.32):
        seat = []
        for y,w,d,dz in ((.401,.55,.436,0),(.418,.607,.494,.006),(.455,.614,.504,.008),
                          (.474,.581,.475,.003),(.480,.497,.385,0)):
            seat.append([(x+dx,y,dz+zz) for dx,zz in octagon(w,d,.043)])
        m.rings("leather",seat)
        # The pipecord is sewn into the front of the seat, not printed stripes.
        m.rod("oxblood",(x-.243,.449,.260),(x+.243,.449,.260),.0048,6)
        back = []
        for y,zc,w,d in ((.480,-.210,.493,.068),(.507,-.216,.568,.096),
                          (.678,-.258,.582,.108),(.898,-.314,.554,.103),
                          (.955,-.323,.491,.080),(.969,-.325,.438,.057)):
            back.append([(x+dx,y,zc+zz) for dx,zz in octagon(w,d,.021)])
        m.rings("leather",back)
        for side in (-1,1):
            xbar=x+side*.211
            m.rod("rubber",(xbar,.375,-.165),(xbar,.853,-.366),.018,7)
        m.rod("oxblood",(x-.229,.681,-.204),(x+.229,.681,-.204),.004,7)
    # One nick shows the backing on the exposed left passenger corner.
    m.rod("wear",(-.567,.445,.254),(-.525,.438,.258),.005,6)
    rail = [(-.645,.402,-.182),(-.645,.620,-.177),(-.645,.725,-.110),(-.645,.722,.096),(-.645,.604,.152)]
    for a,b in zip(rail,rail[1:]):
        m.rod("zinc",a,b,.018,8)
    workshop.collider(m.root,mat,"SeatBench",(0,.473,-.045),(1.33,.946,.65))
    for label,x in (("Left",-.32),("Right",.32)):
        port.empty("ANCHOR_YardSeat"+label,m.root,(x,.480,0))


def footrest(m, mat):
    # A small sloping boot board fits beneath the customer's sole.
    for x in (-.170,.170):
        m.box("iron",(x,.053,-.105),(.039,.114,.038),.004)
        m.box("iron",(x,.078,.104),(.039,.165,.038),.004)
        m.rod("iron",(x,.031,-.106),(x,.136,.104),.010,6)
    rings=[]
    for z,y,width in ((-.154,.108,.423),(-.130,.113,.476),(.133,.175,.476),(.155,.180,.429)):
        rings.append([(-width*.5,y-.032,z),(width*.5,y-.032,z),(width*.5,y,z),(-width*.5,y,z)])
    m.rings("wood",rings)
    for z in (-.067,.012,.083):
        y=.113+(z+.13)*(.062/.263)
        m.rod("rubber",(-.205,y+.003,z),(.205,y+.003,z),.0055,7)
    workshop.collider(m.root,mat,"Footrest",(0,.084,0),(.477,.183,.312))
    port.empty("ANCHOR_YardFootrestContact",m.root,(0,.147,.013))


def scraper(m, mat):
    # Cast-iron rail between two mortared feet. The worn top is the exposed
    # working edge; a screwed-on sign or a second action would add no function.
    for x in (-.157,.157):
        m.box("concrete",(x,.012,0),(.124,.085,.197),.015)
        m.rod("iron",(x,.051,0),(x,.134,0),.011,7)
    m.rings("iron", [[(x,.091,-.008),(x,.091,.008),(x,.145,.004),(x,.145,-.004)] for x in (-.181,.181)])
    m.rod("zinc",(-.18,.146,0),(.18,.146,0),.0038,6)
    workshop.collider(m.root,mat,"BootScraper",(0,.063,0),(.438,.177,.197))


def kerb(m, mat, long):
    # The physical, half-buried concrete has a rounded roadside shoulder and
    # independently broken ends. The south-edge placements use different pieces.
    length,width=(1.04,.19) if long else (.73,.18)
    profile=[(-width*.5,-.09),(.5*width,-.09),(.5*width,.046),
             (.5*width-.021,.093),(-.5*width+.019,.103),(-.5*width,.083)]
    rings=[]
    stations=[(-length*.5,.73),(-length*.5+.066,.99),(-length*.13,1),
              (length*.30,.95),(length*.5,.71 if long else .64)]
    for i,(x,scale) in enumerate(stations):
        rings.append([(x+(0.026 if i==0 and j in (3,4) else 0),
                       y*scale if y>0 else y,z*(.93+.07*scale)) for j,(z,y) in enumerate(profile)])
    m.rings("concrete",rings)
    workshop.collider(m.root,mat,m.root.name,(0,.004,0),(length,.188,width))


def leaf(m, base, direction, length, width, swatch):
    # Lilac leaves come in opposite pairs with a broad heart-shaped heel
    # and a pointed tip. A folded centre catches light on one half of a leaf.
    a=Vector(base)
    forward=Vector(direction).normalized()
    side=forward.cross(Vector((0,1,0))).normalized()
    normal=side.cross(forward).normalized()
    outline=[(0,0),(-.76,.025),(-1,.26),(-.69,.64),(0,1),(.69,.64),(1,.26),(.76,.025)]
    edge=[a+forward*length*t+side*width*s+normal*(.08*width*math.sin(math.pi*t))
          for s,t in outline]
    centre=a+forward*length*.32+normal*width*.27
    underside=centre-normal*width*.33
    points=[tuple(p) for p in edge]+[tuple(centre),tuple(underside)]
    n=len(edge)
    faces=[(i,(i+1)%n,n) for i in range(n)]+[((i+1)%n,i,n+1) for i in range(n)]
    m.g.add(points,faces,workshop.COLOR[swatch])


def lilac(m, mat):
    rng=random.Random(1108)
    # A low common stool throws up thin, uneven canes. Gaps between the fans
    # retain the view around the workshop; there are no flower heads.
    stems=((-0.09,-.045,-.52,.22,1.37),(.015,-.098,.18,-.56,1.61),
           (.118,-.035,.55,-.19,1.64),(.092,.092,.39,.47,1.51),
           (-.095,.062,-.32,.53,1.56),(-.006,.005,-.12,-.16,1.95),
           (.004,.053,.15,.16,1.79))
    for rank,(bx,bz,ex,ez,h) in enumerate(stems):
        base=Vector((bx,-.035,bz))
        knee=Vector((bx+(ex-bx)*.38,.37*h,bz+(ez-bz)*.44))
        tip=Vector((ex,h,ez))
        m.rod("wear",base,knee,.024 if rank<5 else .027,7,.014)
        m.rod("wood",knee,tip,.015,7,.0038)
        for j,t in enumerate((.35,.59,.78)):
            fork=knee.lerp(tip,t)
            theta=rank*2.399+j*2.15+rng.uniform(-.25,.25)
            reach=.16+.12*(1-t)
            fork_tip=fork+Vector((math.cos(theta)*reach,(1-t)*.22+.10,math.sin(theta)*reach))
            m.rod("wear",fork,fork_tip,.0067,6,.0026)
            for tt in (.58,.92):
                node=fork.lerp(fork_tip,tt)
                for hand in (-1,1):
                    angle=theta+hand*(1.02+.12*j)
                    direction=Vector((math.cos(angle),rng.uniform(.12,.44),math.sin(angle)))
                    stem_end=node+direction*.022
                    m.rod("wear",node,stem_end,.002,5,.0014)
                    leaf(m,stem_end,direction,rng.uniform(.074,.119),rng.uniform(.025,.045),
                         ("shade","lining","shade","faded")[(rank+j+(hand>0))%4])
        # The central leaders terminate in two small pointed living leaves.
        for side in (-1,1):
            theta=rank*1.76+side*.86
            leaf(m,tip, (math.cos(theta),.48,math.sin(theta)),.082,.029,"shade")
    workshop.collider(m.root,mat,"Lilac",(0,.195,0),(.265,.44,.234))


def gravel(m, mat):
    # Compacted earth feathers into the verge at both ends. Use the two darker
    # neutral pigments beside the soil, and leave actual terrain between chips.
    rng=random.Random(317)
    banks=[(-.481,-.36,.21),(-.396,-.56,.43),(-.302,-.49,.66),
           (-.183,-.72,.57),(-.081,-.62,.70),(.029,-.71,.61),
           (.141,-.59,.73),(.242,-.69,.52),(.338,-.46,.64),
           (.411,-.51,.38),(.483,-.24,.23)]
    points=[]
    for z,left,right in banks:
        for fraction in (0,.24,.49,.76,1):
            across=fraction+rng.uniform(-.035,.035) if 0<fraction<1 else fraction
            points.append((left+(right-left)*across,0,z+rng.uniform(-.023,.023)))
    faces=[]
    for row in range(len(banks)-1):
        for col in range(4):
            a=row*5+col
            faces.extend(((a,a+5,a+6),(a,a+6,a+1)) if rng.random()<.5
                         else ((a,a+5,a+1),(a+1,a+5,a+6)))
    m.g.add(points,faces,workshop.COLOR["sole"],solid=False)
    # Small irregular pockets of charcoal fines are part of this triangulation.
    for i in (4,13,14,21,37,38,52,59,60,76):
        m.g.colors[i]=workshop.COLOR["rubber"]
    for side in (-1,1):
        edge=points[0::5] if side<0 else points[4::5]
        for x,_,z in edge:
            for i in range(2):
                cx=x+side*(.085+i*.056+rng.uniform(0,.009))
                cz=z+rng.uniform(-.004,.004)
                rx,rz=rng.uniform(.011,.025),rng.uniform(.004,.009)
                chip=[(cx-rx,0,cz),(cx-.23*rx,0,cz+rz),(cx+rx,0,cz+.12*rz),
                      (cx+.27*rx,0,cz-.73*rz)]
                pigment=rng.choice(("sole","rubber","iron","signboard"))
                m.g.add(chip,[(0,1,2,3)],workshop.COLOR[pigment],solid=False)


def check_gravel(mesh):
    points=[port.source(v.co) for v in mesh.vertices]
    if any(abs(y)>.00001 or abs(x)>.90 or abs(z)>.525 for x,y,z in points):
        raise ValueError("Gravel must fit the flat terrain sampling parcel")
    footprints=[]
    for face in mesh.polygons:
        if face.normal.z<.999:
            raise ValueError("Gravel must face upward in Unity")
        polygon=[(points[i][0],points[i][2]) for i in face.vertices]
        for index,old in enumerate(footprints):
            if workshop._convex_intersection_area(polygon,old,1e-9)>1e-8:
                raise ValueError(f"Overlapping coplanar dirt or grit faces {index} and {face.index}")
        footprints.append(polygon)


def cable(m, mat):
    # The black lead hangs from two different practical fasteners. Its centre
    # deviates from the line between the two terminations by 22 cm at three metres.
    points=[]
    for i in range(13):
        t=i/12
        points.append((0,-.88*t*(1-t),3*t))
    for a,b in zip(points,points[1:]):m.rod("rubber",a,b,.009,7)
    m.box("lining",(0,.015,-.022),(.126,.109,.062),.010)
    m.box("iron",(0,.015,.012),(.099,.082,.011),.003)
    m.rod("rubber",(0,0,.018),(0,-.004,.058),.023,8,.013)
    m.box("iron",(0,.003,3.015),(.083,.078,.040),.007)
    m.rod("rubber",(0,-.009,2.953),(0,0,3),.010,8,.017)
    port.empty("ANCHOR_FeedStart",m.root,(0,0,0))
    port.empty("ANCHOR_FeedEnd",m.root,(0,0,3))


def build():
    port.base.reset()
    mat=port.base.material("CobblerYardPalette")
    root=port.empty("CobblerYard")
    descriptions=[]
    recipes=(bench,footrest,scraper,lambda m,material:kerb(m,material,True),
             lambda m,material:kerb(m,material,False),lilac,gravel,cable)
    for name,recipe in zip(PART_NAMES,recipes):
        part=port.empty(name,root)
        m=Parts(part,mat)
        recipe(m,mat)
        visible=m.seal()
        bpy.context.view_layer.update()
        desc=port.describe(part)
        boxes=[]
        for obj in part.children:
            if not obj.name.startswith("COL_"):continue
            xyz=[port.source(part.matrix_world.inverted() @ obj.matrix_world @ v.co) for v in obj.data.vertices]
            lo,hi=([f(p[j] for p in xyz) for j in range(3)] for f in (min,max))
            boxes.append({"name":obj.name,"center":[round((a+b)*.5,5) for a,b in zip(lo,hi)],
                          "size":[round(b-a,5) for a,b in zip(lo,hi)]})
        desc["boxes"]=boxes
        if len(boxes)!=(0 if name in ("GravelApproach","FeedCable") else 1):
            raise ValueError("Unexpected yard collision at "+name)
        if not any(child.name==name+"__Paint" for child in part.children):
            raise ValueError("Missing registered shared-atlas part "+name)
        if name=="GravelApproach":
            check_gravel(visible.data)
        if name=="Lilac":
            spans=[high-low for low,high in zip(desc["bounds_min"],desc["bounds_max"])]
            if not (1.18<spans[0]<1.74 and 1.94<spans[1]<2.07 and 1.06<spans[2]<1.70):
                raise ValueError("Lilac no longer fits the sparse two-metre yard parcel: "+str(spans))
        descriptions.append(desc)
    total=sum(p["triangles"] for p in descriptions)
    if total>11500:raise ValueError(f"Yard geometry exceeds budget: {total}")
    return root,{"coordinateSystem":"Local Unity metres; origin at ground; +Z seat forward or cable length",
                "surface":"City/Cobbler/CobblerSurface","generator":Path(__file__).name,
                "triangles":total,"parts":descriptions}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check","--validate-only",action="store_true")
    parser.add_argument("--model-dir",type=Path,default=OUT)
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    root,description=build()
    manifest=args.model_dir/"CobblerYard.json"
    model=args.model_dir/"CobblerYard.fbx"
    blend=args.source_dir/"CobblerYard.blend"
    if args.check:
        if json.loads(manifest.read_text())!=description or not model.is_file() or not blend.is_file():
            raise ValueError("Published cobbler yard differs from its measured reconstruction")
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        manifest.write_text(json.dumps(description,indent=2)+"\n")
        port.base.export(root,model)
        # Spread the pieces only on the authoring sheet. Exported prototype roots
        # all coincide with their own metric datum for direct placement in City.
        for i,name in enumerate(PART_NAMES):
            part=bpy.data.objects[name]
            part.location=port.source(((i%4)*2.2,0,(i//4)*3.1))
            for obj in part.children:
                if obj.name.startswith("COL_"):obj.hide_set(True)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
    print("Cobbler yard verified:",description["triangles"],"triangles;",", ".join(PART_NAMES),flush=True)


if __name__=="__main__":
    main()
