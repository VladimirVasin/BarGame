#!/usr/bin/env python3
"""Three apartments, their fixed furniture and the nine objects that can leave them."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("finds", TOOLS / "build-pawnshop-finds-3d-model.py")
finds = importlib.util.module_from_spec(spec)
spec.loader.exec_module(finds)
street = finds.street
STOREY = 2.96


def shell(m, group, finish, rings):
    """Joined cross-sections with closed ends; atlas projection follows each face."""
    n = len(rings[0])
    faces = [tuple(reversed(range(n))), tuple(range((len(rings)-1)*n, len(rings)*n))]
    for j in range(len(rings)-1):
        for i in range(n):
            a, b = j*n+i, j*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    m.add(group, finish, ([p for ring in rings for p in ring], faces))


def loop(m, group, finish, centre, rx, rz, wire, yscale=1, angle=0):
    x,y,z = centre
    rings, faces = [], []
    for i in range(24):
        a = i*math.tau/24
        for j in range(6):
            b = j*math.tau/6
            u = (rx+wire*math.cos(b))*math.cos(a)
            v = (rz+wire*math.cos(b))*math.sin(a)
            rings.append((x+u*math.cos(angle)-v*math.sin(angle),y+yscale*wire*math.sin(b),
                          z+u*math.sin(angle)+v*math.cos(angle)))
            s,t = i*6, ((i+1)%24)*6
            faces.append((s+j,t+j,t+(j+1)%6,s+(j+1)%6))
    m.add(group,finish,(rings,faces))


def disk(m, group, finish, centre, width, height, depth):
    m.add(group,finish,street.parts.u_cylinder(centre,(width,height/2,depth),20))


def shoe(m, group, x,y,z, size=1, new=False, brace=False, last=False):
    # Narrow heel and instep widen into a blunt, upturned child's toe.
    stations = [(-.089,.024,.055),(-.071,.032,.084),(-.036,.034,.089),
                (-.008,.033,.068),(.036,.042,.048),(.075,.039,.038),(.089,.020,.030)]
    def section(d,w,h,sole):
        h = .012 if sole else h
        base = 0 if sole else .011
        return [(x+u*size,y+(base+v)*size,z+d*size) for u,v in
                [(-w*.78,.001),(-w,.010),(-w*.91,h*.69),(-w*.55,h),(w*.57,h),
                 (w*.94,h*.73),(w,.01),(w*.80,.001)]]
    shell(m,group,"enamel" if new else "rubber",[section(d,w,h,True) for d,w,h in stations])
    shell(m,group,"paper" if last else "inside",[section(d,w*.97,h,False) for d,w,h in stations])
    if last: return
    disk(m,group,"rubber",(x,y+.096*size,z-.044*size),.050*size,.004*size,.059*size)
    m.box(group,"paper",(x,y+.086*size,z-.008*size),(.066*size,.008*size,.015*size),.002)
    if new:
        loop(m,"Metal","edge",(x+.023*size,y+.091*size,z-.009*size),.012*size,.008*size,.0023*size)
        for k in range(3):
            m.box(group,"paper",(x-.012*size,y+.0007*size,z+(.028+k*.011)*size),
                  (.036*size,.0015*size,.0016*size),.00025,tilt=-18)
    if brace:
        for side in (-1,1):
            u = x+side*.035*size
            m.rod("Metal","iron",(u,y+.031*size,z-.046*size),
                  (u+side*.008,y+.246*size,z-.055*size),.005)
        loop(m,group,"paper",(x,y+.233*size,z-.052*size),.045,.034,.005)
        m.box("Metal","enamel",(x-.025*size,y+.036*size,z+.010*size),(.015,.037,.041),.002)
        for h in (.007,.013):
            m.box(group,"paper",(x,y+h,z-.066*size),(.055*size,.005,.041*size),.002)


def last():
    m=street.Model()
    shoe(m,"Body",0,0,0,1.22,last=True)
    m.box("Metal","enamel",(-.036,.061,.0),(.016,.042,.047),.002)
    m.box("Metal","iron",(0,.103,-.051),(.021,.009,.064),.002)
    for i in range(5):
        disk(m,"Body","rubber",(0,.108,-.073+i*.010),.005,.002,.005)
    m.rod("Metal","edge",(-.022,.097,-.081),(.022,.097,-.081),.004)
    m.anchors["Grip"]=(0,.063,0)
    return m


def buckle_shoes():
    m=street.Model()
    for x,z in ((-.046,.012),(.046,-.012)):
        shoe(m,"Body",x,0,z,new=True)
    m.anchors["Grip"]=(.046,.058,-.048)
    return m


def locket():
    m=street.Model()
    for x in (-.024,.023):
        disk(m,"Metal","enamel",(x,.005,0),.049,.009,.062)
        disk(m,"Body","rubber",(x,.010,0),.043,.003,.055)
        loop(m,"Metal","edge",(x,.012,0),.021,.027,.0013)
    for i in range(12):
        a=i*math.tau/12
        m.rod("Body","paper",(.023+.011*math.cos(a),.014,.016*math.sin(a)),
              (.023+.011*math.cos(a+.68),.0145,.016*math.sin(a+.68)),.0028)
    m.box("Body","inside",(.034,.016,0),(.008,.004,.009),.0008)
    m.rod("Metal","edge",(0,.010,-.012),(0,.010,.012),.0026)
    loop(m,"Metal","enamel",(-.024,.006,.034),.005,.006,.0016)
    m.anchors["Grip"]=(-.018,.007,0)
    return m


def brace_boot():
    m=street.Model()
    shoe(m,"Body",0,0,0,1.42,brace=True)
    m.anchors["Grip"]=(.053,.18,-.073)
    return m


def mirror():
    m=street.Model()
    # The stone projects 8 cm beyond the plaster. Two jaws under and over
    # that lip carry the upright; the screw and thumb bar remain in full view.
    m.box("Metal","iron",(0,.014,.001),(.091,.024,.084),.004)
    m.box("Metal","iron",(0,.067,-.034),(.095,.127,.020),.004)
    m.box("Metal","iron",(0,.117,.001),(.111,.019,.090),.004)
    disk(m,"Body","rubber",(0,.105,.019),.054,.006,.040)
    m.rod("Metal","edge",(0,.004,.023),(0,.025,.023),.009)
    disk(m,"Metal","iron",(0,.027,.023),.050,.008,.043)
    m.rod("Metal","edge",(-.061,.008,.025),(.061,.008,.025),.004)
    m.rod("Metal","iron",(0,.128,.021),(0,.160,.098),.014)
    m.rod("Metal","edge",(0,.154,.097),(0,.399,.097),.010)
    # A clamp screw turns with the head. Its pin and the back of the frame
    # meet at the socket, leaving the entire silvered face clear.
    m.rod("Bezel","enamel",(-.052,.408,.097),(.052,.408,.097),.013)
    m.rod("Bezel","paper",(.048,.408,.097),(.065,.408,.097),.022)
    for x in (-.038,.038):
        m.rod("Bezel","iron",(x,.408,.097),(x,.408,.138),.010)
    m.anchors["MirrorPivot"]=(0,.408,.097)
    m.box("Bezel","iron",(0,.408,.140),(.326,.242,.021),.009)
    m.box("Glass","edge",(0,.408,.153),(.306,.220,.006),.002)
    m.anchors["Grip"]=(0,.19,.097)
    return m


def bell():
    m=street.Model()
    disk(m,"Body","rubber",(0,.010,0),.104,.020,.104)
    rings=[]
    for h,r in ((.016,.049),(.022,.051),(.034,.048),(.047,.041),(.056,.026),(.060,.009)):
        rings.append([(r*math.cos(i*math.tau/20),h,r*math.sin(i*math.tau/20)) for i in range(20)])
    shell(m,"Metal","enamel",rings)
    m.rod("Metal","iron",(0,.055,0),(0,.071,0),.005)
    m.rod("Metal","enamel",(0,.071,0),(.094,.061,-.008),.0045)
    disk(m,"Metal","edge",(.091,.062,-.008),.017,.004,.018)
    m.anchors["Grip"]=(.022,.032,0)
    return m


def shears():
    m=street.Model()
    for s in (-1,1):
        loop(m,"Metal","iron",(s*.018,.008,-.061),.015 if s<0 else .021,.031,.0037,angle=s*.20)
        m.rod("Metal","iron",(s*.015,.008,-.036),(0,.009,0),.004)
        rings=[]
        for z,w,x in ((-.01,.006,-s*.002),(.014,.008,s*.002),(.104,.001,s*.016)):
            rings.append([(x-w,.005,z),(x+w,.005,z),(x+w,.010,z),(x-w,.011,z)])
        shell(m,"Metal","edge",rings)
    m.box("Body","paper",(-.034,.009,-.060),(.014,.014,.039),.004,tilt=-9)
    disk(m,"Metal","enamel",(0,.012,0),.012,.005,.012)
    m.anchors["Grip"]=(-.022,.009,-.053)
    return m


def gauge():
    m=street.Model()
    # Real notches in the edge can be counted with a finger; worn brass lands between them.
    x=-.122
    rim=[(x,-.016),(.122,-.016),(.122,.016)]
    for i in reversed(range(1,13)):
        mark=x+i*.018
        rim.extend(((mark+.003,.016),(mark,.016-(.009 if i%3==0 else .0045)),(mark-.002,.016)))
        m.box("Body","inside",(mark,.0065,-.007),(.001,.001,.009 if i%3==0 else .005),.0002)
    rim.append((x,.016))
    shell(m,"Metal","enamel",[[(u,y,v) for u,v in rim] for y in (.001,.006)])
    loop(m,"Metal","edge",(-.098,.007,0),.004,.004,.001)
    m.anchors["Grip"]=(-.095,.005,0)
    return m


def rings():
    m=street.Model()
    loop(m,"Metal","enamel",(-.012,.004,0),.011,.011,.0025)
    loop(m,"Metal","enamel",(.008,.005,.002),.0088,.009,.0020)
    cord=[(-.014,.007,.008),(-.022,.004,.029),(-.047,.004,.060),(-.033,.004,.086),
          (.014,.004,.094),(.044,.004,.068),(.034,.004,.037),(.008,.008,.009)]
    for a,b in zip(cord,cord[1:]): m.rod("Body","inside",a,b,.002)
    for i in range(4):
        loop(m,"Body","paper",(.028+i*.0018,.005,.033+i*.002),.0033,.0029,.0016,angle=.3*i)
    m.anchors["Grip"]=(.010,.005,.005)
    return m


ITEMS={"ChildShoeLast":last,"SilverBuckleShoes":buckle_shoes,"MourningLocket":locket,
       "BracedBoot":brace_boot,"ClampMirror":mirror,"BedsideBell":bell,
       "TailorShears":shears,"NotchedGauge":gauge,"CordedWeddingRings":rings}


class Rooms(street.Model):
    def __init__(self):
        super().__init__()
        self.docks = []
        self.occupied = []
        self.floor = 0

    def block(self, x,z,w,d):
        self.occupied.append(dict(floor=self.floor,x=x,z=z,width=w,depth=d))

    def dock(self, name, x,h,z, stand_x,stand_z):
        self.docks.append(dict(item=name,position=vector(x,self.floor*STOREY+h,z),
                               approach=vector(stand_x,self.floor*STOREY,stand_z)))

    def plank(self, finish,x,y,z,w,h,d,bevel=.012,tilt=0):
        self.box("Room"+str(self.floor),finish,(x,self.floor*STOREY+y,z),(w,h,d),bevel,tilt)

    def bar(self, finish,a,b,r):
        self.rod("Room"+str(self.floor),finish,(a[0],a[1]+self.floor*STOREY,a[2]),
                 (b[0],b[1]+self.floor*STOREY,b[2]),r)

    def table(self,x,z,w,d,h):
        self.plank("paper",x,h-.036,z,w,.072,d,.017)
        for sx in (-1,1):
            for sz in (-1,1):
                self.plank("paper",x+sx*(w/2-.055),h*.48,z+sz*(d/2-.052),.068,h*.96,.068)
            self.plank("paper",x+sx*(w/2-.055),h-.13,z,.055,.15,d-.06)
        self.block(x,z,w,d)

    def striped(self,x,h,z,w,d):
        self.plank("paper",x,h,z,w,.023,d,.008)
        for col in range(1,9):
            self.plank("inside",x-w*.5+col*w/9,h+.013,z,.011,.004,d-.015,.001)


def vector(x,y,z): return dict(x=round(x,5),y=round(y,5),z=round(z,5))


def apartments():
    r=Rooms()
    # Bench, three sizes of shoes and a child's polished foot rail.
    r.table(-.89,4.068,1.99,.74,.73)
    r.plank("paper",-.89,.79,4.393,1.94,.13,.060)
    r.plank("paper",-.84,.235,3.99,1.79,.042,.61)
    for i,x in enumerate((-1.56,-1.15)):
        for hand in (-1,1): shoe(r,"Room0",x+hand*.037,.257,3.83,.71+.13*i)
    r.striped(-.25,.258,3.83,.38,.30)
    r.plank("inside",-.10,.275,3.77,.040,.008,.035)
    # Cobbling pegs, a spool and the jaws of a bench vise sit against the splash rail.
    for i in range(4):
        r.bar("iron",(-.57+i*.06,.73,4.27),(-.57+i*.06,.79,4.29),.005)
    r.plank("iron",-.13,.773,4.19,.13,.085,.12)
    r.plank("paper",-.14,.825,4.19,.16,.026,.10)
    r.bar("enamel",(-.15,.77,4.19),(-.15,.77,4.02),.010)
    r.table(-2.22,4.20,.40,.38,.34)
    r.plank("paper",-2.22,.54,4.38,.37,.36,.050)
    r.bar("enamel",(-2.39,.135,4.07),(-2.05,.135,4.07),.018)
    r.table(-1.30,-4.178,.95,.51,.83)
    r.plank("inside",-1.34,.84,-4.13,.37,.018,.27)
    r.dock("ChildShoeLast",-1.51,.73,3.80,-1.52,3.18)
    r.dock("SilverBuckleShoes",-.23,.274,3.77,-.17,3.14)
    r.dock("MourningLocket",-1.32,.85,-4.12,-1.30,-3.48)
    # Long side of the bed runs against the solid wall beside the window.
    # A shaped quilt falls over the open side, exposing the lighter sheet.
    r.floor=1
    x,z=-1.34,4.032
    r.plank("paper",x,.384,z,1.74,.105,.79)
    r.plank("concrete",x,.476,z,1.72,.136,.78,.034)
    for sx in (-1,1):
        xx=x+sx*.832
        for sz in (-1,1):
            zz=z+sz*.363
            r.bar("iron",(xx,.018,zz),(xx,.65 if sx>0 else .51,zz),.025)
        r.bar("iron",(xx,.65 if sx>0 else .51,z-.363),(xx,.65 if sx>0 else .51,z+.363),.020)
    rings=[]
    for dx,dip in ((-.79,.0),(-.70,.014),(-.52,0),(-.25,.011),(.02,.002),(.20,.022)):
        profile=[(-.424,.40-dip),(-.442,.415-dip),(-.427,.538),(-.37,.574),
                 (-.13,.594+dip),(.11,.587),(.369,.562),(.375,.542),(-.372,.527)]
        rings.append([(x+dx,STOREY+y,z+dz) for dz,y in profile])
    shell(r,"Room1","inside",rings)
    r.plank("concrete",x+.57,.602,z,.37,.139,.60,.035,tilt=-5)
    r.plank("enamel",x+.31,.551,z,.13,.020,.70,.004)
    r.block(x,z,1.77,.81)
    # A folded camp bed rests upright on its rubber feet against the rear wall.
    for side in (-1,1):
        xx=-1.78+side*.285
        r.bar("iron",(xx,.028,-4.22),(xx,.96,-4.394),.019)
        r.bar("iron",(xx,.07,-4.29),(xx,.865,-4.21),.014)
        r.plank("rubber",xx,.025,-4.22,.065,.045,.067)
        r.bar("iron",(xx,.08,-4.24),(-1.78-side*.285,.84,-4.28),.012)
    for h,zz in ((.10,-4.232),(.88,-4.373)):
        r.bar("iron",(-2.065,h,zz),(-1.495,h,zz),.020)
    r.plank("inside",-1.78,.47,-4.32,.52,.74,.066,.018)
    # Floor beneath the folding legs was worn through beside the child's mattress.
    for xx in (-1.96,-.69): r.plank("paper",xx,.006,3.38,.09,.008,.16,.002)
    r.block(-1.78,-4.315,.65,.22)
    r.table(.025,4.163,.47,.53,.61)
    # Chair's padded lap belt keeps the other half of the tailor's striped pocket.
    x,z=1.09,-4.127
    r.table(x,z,.55,.55,.48)
    r.plank("inside",x,.72,z-.25,.52,.50,.067,.029)
    r.striped(x,.50,z,.54,.16)
    r.plank("enamel",x+.205,.522,z,.043,.010,.040)
    for side in (-1,1):
        xx=x+side*.30
        for i in range(16):
            a,b=i*math.tau/16,(i+1)*math.tau/16
            p=(xx,.292+.281*math.sin(a),z+.281*math.cos(a))
            q=(xx,.292+.281*math.sin(b),z+.281*math.cos(b))
            r.bar("rubber",p,q,.016)
            if i%2==0: r.bar("iron",(xx,.292,z),p,.006)
        r.bar("iron",(xx,.31,z+.10),(xx,.14,z+.43),.014)
        r.plank("iron",xx-side*.06,.14,z+.43,.18,.025,.13)
    r.block(x,z,.66,.88)
    # Stone sill top .8575, underside .7725, inner lip at z=4.36.
    # The pale rubbed patch stays attached to the stone when the clamp leaves.
    r.plank("concrete",.974,.8585,4.395,.12,.002,.071,.0004)
    r.dock("ClampMirror",.974,.7495,4.369,1.19,3.75)
    r.dock("BedsideBell",.038,.61,4.03,.11,3.46)
    r.dock("BracedBoot",1.10,.525,-4.06,1.12,-3.31)
    # Upper workshop. One table still has the touch rail, tool hollows and unlike pulls.
    r.floor=2
    r.table(1.26,4.07,1.71,.73,.78)
    r.table(-1.67,-4.126,1.23,.62,.76)
    r.plank("paper",1.26,.799,3.735,1.65,.038,.032)
    for i,finish in enumerate(("enamel","inside","iron")):
        xx=.79+i*.43
        r.plank("paper",xx,.64,3.74,.40,.16,.06)
        r.plank(finish,xx,.64,3.69,.045+i*.022,.042,.055)
    for a,b in (((1.35,.784,3.89),(1.37,.784,4.08)),((1.38,.784,3.90),(1.40,.784,4.08))):
        r.bar("inside",a,b,.004)
    # A handwheel, needle arm, bed and treadle still belong to the other place.
    r.plank("inside",-1.76,.787,-4.21,.41,.054,.23)
    r.plank("inside",-1.62,.91,-4.21,.085,.24,.11,.023)
    r.plank("inside",-1.75,1.02,-4.21,.31,.077,.094,.025)
    r.bar("iron",(-1.88,.99,-4.21),(-1.88,.815,-4.21),.006)
    for i in range(14):
        a,b=i*math.tau/14,(i+1)*math.tau/14
        r.bar("iron",(-1.57,.99+.10*math.cos(a),-4.21+.10*math.sin(a)),
              (-1.57,.99+.10*math.cos(b),-4.21+.10*math.sin(b)),.012)
    r.plank("iron",-1.62,.115,-4.034,.33,.038,.21)
    # Garments hang between the surviving shelves. The jacket is open; two light
    # pocket outlines and their unpicked seams interrupt its striped lining.
    for xx,coat in ((.83,True),(1.58,False)):
        r.bar("iron",(xx,1.60,-4.03),(xx,1.53,-4.03),.008)
        for s in (-1,1):
            r.bar("paper",(xx,1.54,-4.03),(xx+s*.22,1.46,-4.03),.015)
            r.bar("inside",(xx+s*.183,1.43,-4.03),(xx+s*.243,1.02,-3.99),.046)
        shape = [(-.19,.85),(.18,.865),(.185,1.16),(.19,1.41),(.135,1.47),
                 (.063,1.50),(0,1.44),(-.059,1.50),(-.15,1.47),(-.20,1.42),(-.172,1.18)]
        shell(r,"Room2","inside", [[(xx+a,2*STOREY+h,z) for a,h in shape] for z in (-4.105,-4.022)])
        if coat:
            for s in (-1,1):
                r.plank("paper",xx+s*.097,1.18,-4.014,.14,.56,.018)
                for k in (-1,0,1):
                    r.plank("inside",xx+s*.097+k*.037,1.18,-4.001,.007,.54,.006)
                r.plank("paper",xx+s*.097,1.14,-3.996,.10,.14,.013)
                for k in range(4):
                    r.plank("enamel",xx+s*.097-.036+k*.024,1.20,-3.984,.010,.004,.004)
                lapel=[(s*.16,1.47),(s*.063,1.50),(s*.023,1.35),(s*.057,1.29),(s*.12,1.405)]
                shell(r,"Room2","inside",[[(xx+a,2*STOREY+h,z) for a,h in lapel] for z in (-4.004,-3.980)])
        else:
            skirt=[(-.147,1.17),(.151,1.17),(.248,.714),(.182,.690),(.09,.707),
                   (.012,.686),(-.073,.704),(-.180,.690),(-.241,.711)]
            shell(r,"Room2","inside",[[(xx+a,2*STOREY+h,z) for a,h in skirt] for z in (-4.094,-3.995)])
            r.plank("paper",xx,1.17,-4.005,.43,.035,.03)
    r.dock("TailorShears",1.38,.780,3.97,1.4,3.24)
    r.dock("NotchedGauge",-1.45,.760,-3.96,-1.55,-3.35)
    r.dock("CordedWeddingRings",1.63,.654,-3.87,1.65,-3.16)
    return r


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--model-dir",type=Path,required=True)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    street.OUT=args.model_dir
    street.OUT.mkdir(parents=True,exist_ok=True)
    rooms=apartments()
    if {d["item"] for d in rooms.docks} != set(ITEMS): raise ValueError("Missing family dock")
    for dock in rooms.docks:
        name=dock["item"]
        model=ITEMS[name]()
        model.export(name,finds.MATERIALS)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(street.OUT/(name+".fbx")))
        meshes=[o for o in bpy.context.scene.objects if o.type=="MESH"]
        pts=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
        lo,hi=([fn(p[i] for p in pts) for i in range(3)] for fn in (min,max))
        grip=bpy.data.objects["Grip"].matrix_world.translation
        if not all(a-.001<=g<=b+.001 for a,g,b in zip(lo,grip,hi)): raise ValueError(name+" grip")
        if abs(lo[2])>.006 or max(b-a for a,b in zip(lo,hi))>(.56 if name=="ClampMirror" else .45): raise ValueError(name+" scale")
        dock["size"]=vector(hi[0]-lo[0],hi[2]-lo[2],hi[1]-lo[1])
    rooms.export("RavineFamilyRooms",finds.MATERIALS)
    (street.OUT/"RavineFamilyLayout.json").write_text(json.dumps(dict(docks=rooms.docks,
        occupied=rooms.occupied,pillow=vector(-.72,STOREY+.81,3.96)),indent=2)+"\n")
    print("RAVINE FAMILIES: room contacts, nine FBX bounds and counter grips verified")


if __name__=="__main__":
    main()
