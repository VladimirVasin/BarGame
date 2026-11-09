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
from mathutils.bvhtree import BVHTree

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("cobbler_workshop", TOOLS / "build-city-cobbler-3d-model.py")
workshop = importlib.util.module_from_spec(spec)
spec.loader.exec_module(workshop)
port, Parts = workshop.port, workshop.Parts
OUT, SOURCE = workshop.OUT, workshop.SOURCE
PART_NAMES = ("SeatBench", "Footrest", "BootScraper", "KerbLong", "KerbShort", "Lilac",
              "GravelApproach", "FeedCable", "OldBusWheels")


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


def wheel_point(origin, tilt, yaw):
    # Polar coordinates on the axle. Positive depth faces the viewer on the
    # leaning wheel. Both tip and rotation in the dirt are authored in metres.
    axis=Vector((math.sin(tilt)*math.cos(yaw),math.cos(tilt),math.sin(tilt)*math.sin(yaw)))
    u=Vector((math.cos(tilt)*math.cos(yaw),-math.sin(tilt),math.cos(tilt)*math.sin(yaw)))
    v=Vector((-math.sin(yaw),0,math.cos(yaw)))
    def point(radius, angle, depth):
        return origin+axis*depth+(u*math.cos(angle)+v*math.sin(angle))*radius
    return point


def wheel_shell(point, profile, segments, tread=False):
    vertices=[]
    faces=[]
    for row,(radius,depth) in enumerate(profile):
        for i in range(segments):
            angle=math.tau*i/segments
            r=radius
            if tread and radius>=.448:
                # Worn cross-cuts meet two circumferential grooves.
                # Offset their phase at each shoulder to leave shallow chevrons.
                groove,step=divmod(i,3)
                angle=math.tau*(groove+(0,.12,.80)[step])/(segments//3)+abs(depth)*.59
                if step==0:r-=.007
            vertices.append(point(r,angle,depth))
            j=(i+1)%segments
            next_row=(row+1)%len(profile)
            faces.append((row*segments+i,row*segments+j,next_row*segments+j,next_row*segments+i))
    return vertices,faces


def pressed_wheel_dish(m,point):
    # Each sector contains a small stud hole and a larger brake ventilation
    # opening punched through a dished steel plate. The centre is an open bore.
    vertices=[]
    lookup={}
    faces=[]
    def vertex(radius,angle,back):
        depth=.057-.065*max(0,(radius-.146)/.098)-(.012 if back else 0)
        p=tuple(point(radius,angle,depth))
        key=tuple(round(k,6) for k in p)
        if key not in lookup:
            lookup[key]=len(vertices)
            vertices.append(p)
        return lookup[key]
    for sector in range(8):
        angle=math.tau*sector/8
        for r0,r1,hole,radial,angular in ((.072,.151,.119,.013,.112),(.151,.246,.200,.027,.211)):
            contour=((r0,-1),((r0+r1)/2,-1),(r1,-1),(r1,0),
                     (r1,1),((r0+r1)/2,1),(r0,1),(r0,0))
            outside=[[vertex(r,angle+a*math.pi/8,back) for r,a in contour] for back in (False,True)]
            inside=[]
            for back in (False,True):
                inside.append([vertex(hole+radial*math.sin((j-1)*math.pi/4),
                                      angle-angular*math.cos((j-1)*math.pi/4),back) for j in range(8)])
            for i in range(8):
                j=(i+1)%8
                for out,inn in zip(outside,inside):
                    faces.append((out[i],out[j],inn[j],inn[i]))
                faces.append((inside[0][i],inside[0][j],inside[1][j],inside[1][i]))
                if (r0==.072 and i in (6,7)) or (r1==.246 and i in (2,3)):
                    faces.append((outside[0][i],outside[0][j],outside[1][j],outside[1][i]))
    start=len(m.g.colors)
    m.g.add(vertices,faces,workshop.COLOR["wear"])
    for i in range(len(faces)):
        if i%13 in (3,4,5):m.g.colors[start+i]=workshop.COLOR["leather"]
    # From the exposed face, bolts and cooling slots must read as through holes.
    dish=BVHTree.FromPolygons(vertices,faces)
    inward=(point(0,0,0)-point(0,0,.1)).normalized()
    for radius,angle,steel in ((0,0,False),(.119,0,False),(.200,0,False),(.145,.18,True)):
        hit=dish.ray_cast(point(radius,angle,.155),inward,.30)[0]
        if (hit is not None)!=steel:
            raise ValueError("The visible wheel dish has a sealed opening or missing steel")
    # The rolled land around the hub bore stands proud of the flat stud flange.
    hub=((.075,.054),(.075,.079),(.080,.088),(.088,.086),(.091,.061),(.095,.054))
    vertices,faces=wheel_shell(point,hub,24)
    m.g.add(vertices,faces,workshop.COLOR["iron"])


def old_bus_wheels(m,mat):
    # Two horizontal, offset casings bear on their flat sidewalls. The third
    # lies against the high edge of this stack with its tread on the ground.
    profile=((.256,-.085),(.293,-.125),(.363,-.125),(.446,-.103),(.469,-.081),
             (.460,-.039),(.469,-.030),(.469,.030),(.460,.039),(.469,.081),
             (.446,.103),(.363,.125),(.293,.125),(.256,.085))
    origins=[Vector((-.285,.125,-.066)),Vector((-.212,.375,.039)),Vector((0,0,0))]
    tilts=(0,0,math.radians(52))
    yaws=(.19,-.43,.13)
    casings=[]
    for origin,tilt,yaw in zip(origins,tilts,yaws):
        casings.append(wheel_shell(wheel_point(origin,tilt,yaw),profile,36,tread=True))
    verts,faces=casings[-1]
    origins[-1].z=.027
    origins[-1].y=-min(v.y for v in verts)
    stack=[BVHTree.FromPolygons(v,f) for v,f in casings[:2]]
    def touches_stack(x):
        delta=origins[-1]+Vector((x,0,0))
        tyre=BVHTree.FromPolygons([v+delta for v in verts],faces)
        return any(tyre.overlap(support) for support in stack)
    touching,clear=.24,.94
    if not touches_stack(touching) or touches_stack(clear):
        raise ValueError("Leaning wheel has no supported position on the two tyres")
    for _ in range(12):
        mid=(touching+clear)/2
        if touches_stack(mid):touching=mid
        else:clear=mid
    origins[-1].x=clear
    barrel=((.255,-.086),(.264,-.091),(.264,-.072),(.253,-.055),
            (.253,.055),(.264,.076),(.264,.088),(.248,.084),(.244,.063),(.244,-.061))
    for i,(origin,tilt,yaw) in enumerate(zip(origins,tilts,yaws)):
        point=wheel_point(origin,tilt,yaw)
        vertices,faces=wheel_shell(point,profile,36,tread=True)
        m.g.add(vertices,faces,workshop.COLOR["rubber"])
        if i==2:
            vertices,faces=wheel_shell(point,barrel,24)
            m.g.add(vertices,faces,workshop.COLOR["iron"])
            pressed_wheel_dish(m,point)
        else:
            # The two reversed rims show their plain inboard barrel and bore.
            inner=((.256,-.083),(.265,-.089),(.265,-.072),(.252,-.051),(.252,.067),
                   (.263,.084),(.245,.083),(.240,.060),(.239,-.026),(.147,-.065),
                   (.078,-.065),(.078,-.077),(.149,-.077),(.245,-.039),(.245,-.070))
            vertices,faces=wheel_shell(point,inner,16)
            m.g.add(vertices,faces,workshop.COLOR["iron"])
    # A single broad low obstacle leaves the air beside the leaning tyre free.
    workshop.collider(m.root,mat,"OldBusWheels",(-.011,.317,0),(1.43,.634,.87))


def build():
    port.base.reset()
    mat=port.base.material("CobblerYardPalette")
    root=port.empty("CobblerYard")
    descriptions=[]
    recipes=(bench,footrest,scraper,lambda m,material:kerb(m,material,True),
             lambda m,material:kerb(m,material,False),lilac,gravel,cable,old_bus_wheels)
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
        if name=="OldBusWheels":
            lo,hi=desc["bounds_min"],desc["bounds_max"]
            if lo[1]!=0 or not (1.45<hi[0]-lo[0]<1.60 and .77<hi[1]<.93 and .99<hi[2]-lo[2]<1.12):
                raise ValueError("The wheel pile no longer rests inside its yard parcel")
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
