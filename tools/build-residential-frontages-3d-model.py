"""Author fixed-metre apartment bays, balcony fittings and alpine joinery in Blender."""
import hashlib
import math
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
from atlas_kit import PixelCanvas
from interior_kit import translated

OUT = ROOT / "Assets/Resources/Housing"
# The same UV chart drives the material and its metallic/smoothness response.
FINISH = {
    "cream": ((186, 177, 146), "paint"), "green": ((83, 108, 94), "paint"),
    "slate": ((107, 125, 129), "paint"), "plaster": ((154, 147, 129), "stone"),
    "cement": ((129, 127, 116), "stone"), "brick": ((142, 108, 87), "brick"),
    "iron": ((71, 76, 73), "iron"), "zinc": ((147, 150, 142), "zinc"),
    "wood": ((132, 102, 76), "wood"), "oldwood": ((100, 88, 73), "wood"),
    "linen": ((186, 177, 153), "cloth"), "stripe": ((151, 135, 119), "stripe"),
    "wine": ((130, 78, 73), "cloth"), "clay": ((150, 99, 75), "stone"),
    "leaf": ((85, 108, 65), "leaf"), "flower": ((156, 68, 70), "leaf"),
    "enamel": ((134, 152, 143), "enamel"), "jar": ((107, 144, 128), "enamel"),
    "mat": ((143, 124, 86), "mat"), "dark": ((70, 66, 58), "wood"),
    "soil": ((78, 66, 52), "earth"), "sage": ((111, 131, 86), "leaf"),
    "rose": ((182, 111, 110), "petal"), "ivory": ((218, 210, 184), "petal"), "pollen": ((158, 129, 83), "petal"),
}
TILE, COLS, ROWS = 64, 8, 4


def paint():
    colour, response = (PixelCanvas(TILE*COLS, TILE*ROWS) for _ in range(2))
    for index, (rgb, kind) in enumerate(FINISH.values()):
        for y in range(TILE):
            for x in range(TILE):
                speck = (((x*1129+y*1301+x*y*7) ^ (y*911)) % 17 - 8)*.52
                value = speck
                if kind == "wood":
                    value += 9*math.sin(y*.67+1.8*math.sin(x*.12)) + 5*math.sin(y*1.83+x*.04)
                elif kind in ("cloth", "stripe", "mat"):
                    value += (x % 3-1)*2.1 + (y % 3-1)*2.3
                    if kind == "stripe" and 9 <= y%21 <= 13:
                        value -= 39
                elif kind == "brick":
                    if y % 8 == 0 or (x + 4 * (y // 8)) % 8 == 0:
                        value += 26
                elif kind in ("paint", "zinc", "iron"):
                    value += 1.8*math.sin(x*.3+y*.15)
                px = (index%COLS)*TILE+x
                py = (ROWS-1-index//COLS)*TILE+y
                colour.put(px,py,tuple(round(v+value) for v in rgb)+(255,))
                metal,smooth = {"iron":(122,110),"zinc":(170,132),"enamel":(12,145),
                                "paint":(4,86),"wood":(0,69)}.get(kind,(0,40))
                response.put(px,py,(metal,0,0,round(smooth+speck)))
    for name,img in (("FrontagePigment",colour),("FrontageResponse",response)):
        (OUT/(name+".png")).write_bytes(img.png_bytes())


class Module:
    def __init__(self, name):
        self.name, self.parts = name, []

    def add(self, geom, finish):
        self.parts.append((geom,finish))

    def box(self, p, size, finish, angle=None):
        bevel = min(.013, min(size)*.19)
        mesh = bp.u_box((0,0,0),size,bevel)
        if angle:
            mesh = bp.u_rotated(mesh,angle)
        self.add(translated(mesh,p),finish)

    def pot(self,x,y,z,r,h,finish,base=.74):
        self.lathe(x,y,z,[(0,r*base),(h*.85,r),(h,r*1.04)],finish)

    def lathe(self,x,y,z,profile,finish,n=10):
        pts=[(x+r*math.cos(i*math.tau/n), y+dy, z+r*math.sin(i*math.tau/n))
             for dy,r in profile for i in range(n)]
        faces=[tuple(range(n)),tuple(reversed(range(len(pts)-n,len(pts))))]
        for row in range(len(profile)-1):
            for i in range(n):
                a=row*n+i
                b=row*n+(i+1)%n
                faces.append((a,a+n,b+n,b))
        self.add((pts,faces),finish)

    def rod(self,a,b,r,finish):
        from mathutils import Vector
        d=Vector(b)-Vector(a)
        mid=(Vector(b)+Vector(a))*.5
        q=Vector((0,1,0)).rotation_difference(d.normalized())
        verts,faces=bp.u_cylinder((0,0,0),(r*2,d.length*.5,r*2),7)
        self.add(([tuple(mid+q@Vector(v)) for v in verts],faces),finish)

    def export(self):
        verts,faces,tiles=[],[],[]
        for geometry,finish in self.parts:
            points,polys=bp.to_source(geometry)
            offset=len(verts)
            verts.extend(points)
            faces.extend(tuple(offset+i for i in p) for p in polys)
            tiles.extend([list(FINISH).index(finish)]*len(polys))
        mesh=bpy.data.meshes.new(self.name)
        mesh.from_pydata(verts,[],faces)
        mesh.update()
        uv=mesh.uv_layers.new(name="Finish")
        for poly,tile in zip(mesh.polygons,tiles):
            axis=max(range(3),key=lambda j:abs(poly.normal[j]))
            axes=[j for j in range(3) if j!=axis]
            pts=[verts[mesh.loops[i].vertex_index] for i in poly.loop_indices]
            low=[min(p[j] for p in pts) for j in axes]
            for i,p in zip(poly.loop_indices,pts):
                # Protected tile edges survive bilinear filtering. Large panels
                # span the tile, short pieces keep a physically sized fragment.
                coords=[(p[j]-lo)/max(.56,max(v[j] for v in pts)-lo) for j,lo in zip(axes,low)]
                uv.data[i].uv=((tile%COLS+.05+.9*coords[0])/COLS,(tile//COLS+.05+.9*coords[1])/ROWS)
        o=bpy.data.objects.new(self.name,mesh)
        bpy.context.collection.objects.link(o)
        return len(verts),len(faces)


def plant(m,x,y,z,scale=1.,flowers=False):
    def pt(a,b,c):
        return (x+a*scale,y+b*scale,z+c*scale)
    m.pot(*pt(0,0,0),.11*scale,.15*scale,"clay")
    for i in range(5):
        angle=i*2.4
        dx,dz=.115*math.cos(angle),.09*math.sin(angle)
        top=.34+.05*(i%3)
        m.rod(pt(0,.13,0),pt(dx,top,dz),.008*scale,"leaf")
        m.box(pt(dx*.7,top-.055,dz*.7),(.124*scale,.031*scale,.081*scale),"leaf",(22,i*54,25))
        if flowers and i%2==0:
            m.pot(*pt(dx,top,dz),.052*scale,.044*scale,"flower",.65)


def chair(m,x,y,z,scale=1.):
    def box(a,b,c,w,h,d,mat,angle=None):
        m.box((x+a*scale,y+b*scale,z+c*scale),(w*scale,h*scale,d*scale),mat,angle)
    for dx in (-.19,.19):
        for dz in (-.17,.17):
            box(dx,.205,dz,.04,.41,.047,"wood")
        box(dx,.62,-.17,.045,.45,.048,"wood",(-5,0,0))
    box(0,.42,0,.46,.065,.42,"wood")
    for h in (.58,.70,.80):
        box(0,h,-.19,.42,.078,.035,"wood")
    box(.08,.66,-.157,.21,.29,.029,"wine",(-5,0,-4))


def windows():
    result=[]
    for name in ("Kitchen","Plants","Sitting","Storage"):
        m=Module("Window"+name)
        # Model depth runs from the backing to the sill nose. The installer
        # fits this entire profile between the host pane and its sash.
        m.box((0,.015,.127),(.98,.055,.27),"zinc")
        for side in (-1,1):
            m.box((side*.47,.49,.12),(.042,.98,.24),"cream")
        m.box((0,.98,.12),(.98,.045,.24),"cream")
        if name=="Kitchen":
            m.lathe(-.20,.044,.12,[(0,.10),(.045,.115),(.135,.101),(.15,.072),(.17,.046)],"enamel")
            m.rod((-.12,.11,.12),(-.034,.21,.12),.029,"enamel")
            for i in range(8):
                def arc(j):
                    return (-.2+.089*math.cos(j*math.pi/8),.172+.109*math.sin(j*math.pi/8),.12)
                m.rod(arc(i),arc(i+1),.012,"iron")
            for i in range(6):
                m.box((.15+i*.052,.84,.076+(.012 if i%2 else 0)),(.055,.24,.024),"linen")
        elif name=="Plants":
            plant(m,-.2,.043,.12,.95,True)
            m.pot(.21,.043,.11,.056,.13,"jar",.95)
            m.rod((.21,.165,.11),(.25,.32,.10),.008,"leaf")
            m.box((.245,.29,.103),(.085,.02,.05),"leaf",(0,22,23))
        elif name=="Sitting":
            for i in range(4):
                m.box((-.39+i*.048,.69,.055+i*.003),(.052,.53-i*.055,.025),"linen",(0,0,-5))
            m.box((.20,.14,.09),(.30,.07,.075),"wood")
            for dx in (.07,.33):
                m.box((dx,.13,.085),(.038,.25,.04),"wood")
        else:
            for i in range(5):
                m.box((0,.56+i*.09,.065),(.90,.080,.025),"mat",(8,0,0))
        result.append(m)
    glazing = Module("WindowGlass")
    glazing.box((0, .49, .237), (.90, .93, .010), "jar")
    result.append(glazing)
    return result


def balconies():
    out=[]
    for name,finish in (("Panel","cement"),("Brick","brick"),("Glazed","green")):
        m=Module("Balcony"+name)
        if name in ("Panel","Brick"):
            m.box((0,.49,0),(2.32,.79,.065),finish)
            m.box((0,.90,0),(2.34,.047,.081),"zinc")
            for side in (-1,1):
                m.box((side*1.15,.49,-.27),(.057,.78,.55),finish)
        else:
            for x in (-1.18,-.58,0,.58,1.18):
                m.box((x,1.11,0),(.045,2.19,.055),finish)
            for y in (.12,.86,1.78,2.19):
                m.box((0,y,0),(2.40,.05,.061),finish)
            for side in (-1,1):
                m.box((side*1.18,2.19,-.53),(.056,.06,1.08),finish)
                m.box((side*1.18,1.51,-.52),(.048,1.32,.04),finish)
            m.box((0,2.24,-.50),(2.45,.065,1.12),"zinc")
        out.append(m)
    glass=Module("BalconyGlass")
    for x in (-.88,-.29,.29,.88):
        for y,h in ((.49,.68),(1.99,.34)):
            glass.box((x,y,-.013),(.537,h,.013),"jar")
    for x in (-.88,.88):
        glass.box((x,1.32,-.013),(.538,.86,.013),"jar")
    out.append(glass)
    for name in ("Kitchen","Plants","Sitting","Storage"):
        m=Module("Deck"+name)
        if name=="Kitchen":
            for x in (-.16,.16):
                m.box((x,.2,0),(.045,.41,.045),"wood",(19,0,0))
                m.box((x,.2,0),(.043,.41,.042),"wood",(-19,0,0))
            m.box((0,.40,0),(.43,.045,.32),"wood")
            m.box((.05,.29,.164),(.22,.23,.023),"stripe",(0,0,3))
        elif name=="Plants":
            m.box((0,.47,0),(.68,.045,.26),"oldwood")
            for x in (-.25,.25):
                m.box((x,.23,0),(.052,.46,.15),"green")
            plant(m,-.18,.50,0,.82,True)
            plant(m,.18,.50,0,.70)
            m.lathe(0,0,.10,[(0,.083),(.16,.10),(.25,.061)],"zinc")
            m.rod((.07,.09,.10),(.19,.22,.10),.026,"zinc")
        elif name=="Sitting": chair(m,0,0,0)
        else:
            for x in (-.22,.22):
                m.rod((x,0,.08),(x,1.17,-.08),.019,"zinc")
            for y in (.39,.54,.69,.84,.99,1.14):
                m.rod((-.22,y,.08-y*.137),(.22,y,.08-y*.137),.012,"zinc")
            m.pot(.27,0,0,.085,.83,"mat",.94)
        out.append(m)
    return out


def leaf(module, base, tip, width, finish):
    """A closed, folded blade. The raised midrib catches light from either side."""
    from mathutils import Vector
    root, end = Vector(base), Vector(tip)
    axis = end - root
    across = Vector((axis.z, 0, -axis.x)).normalized() * width
    middle = root + axis * .46
    verts = [root, middle + across, end, middle - across,
             middle + Vector((0, .014, 0)), middle - Vector((0, .008, 0))]
    faces = [((i + 1) % 4, i, 4) for i in range(4)]
    faces += [(i, (i + 1) % 4, 5) for i in range(4)]
    module.add(([tuple(v) for v in verts], faces), finish)


def petunia(module, center, scale):
    """A five-lobed upward-opening corolla with a recessed throat."""
    x, y, z = center
    vertices = []
    for level, (radius, height) in enumerate(((.012, 0), (.035, .035), (.068, .062))):
        for j in range(10):
            r = radius * (.72 if level == 2 and j % 2 else 1) * scale
            angle = j * math.tau / 10
            vertices.append((x + r * math.cos(angle), y + height * scale, z - r * math.sin(angle)))
    vertices.append((x, y + .027 * scale, z))
    faces = [tuple(reversed(range(10)))]
    for level in range(2):
        for j in range(10):
            a, b = level * 10 + j, level * 10 + (j + 1) % 10
            faces.append((a, b, b + 10, a + 10))
    faces += [(20 + j, 20 + (j + 1) % 10, 30) for j in range(10)]
    module.add((vertices, faces), "ivory")
    module.lathe(x, y + .028 * scale, z, [(0, .009 * scale), (.007 * scale, .012 * scale)],
                 "pollen", n=6)


def flower_box(trailing):
    module = Module("RailPetunias" if trailing else "RailGeraniums")
    finish = "green" if trailing else "clay"
    # Origin is the top centre of the 50 mm deep horizontal handrail.
    # Two bent straps wrap over it, run down the back and carry the trough.
    for x in (-.29, .29):
        module.box((x, .009, .004), (.038, .018, .090), "iron")
        module.box((x, -.034, -.034), (.038, .084, .016), "iron")
        module.box((x, -.119, .042), (.038, .254, .018), "iron")
        module.box((x, -.239, .156), (.038, .018, .246), "iron")
    module.box((0, -.219, .165), (.82, .024, .208), finish)
    for sign in (-1, 1):
        module.box((0, -.114, .165 + sign * .108), (.86, .205, .023), finish, (sign * 6, 0, 0))
        module.box((sign * .423, -.115, .165), (.022, .205, .235), finish, (0, 0, -sign * 5))
        module.box((0, -.012, .165 + sign * .121), (.90, .027, .032), finish)
        module.box((sign * .438, -.012, .165), (.031, .027, .250), finish)
    module.box((0, -.038, .165), (.84, .029, .205), "soil")

    for index, x in enumerate((-.32, -.11, .10, .31)):
        root = (x, -.025, .155 + .016 * (index % 2))
        for blade in range(5):
            angle = index * 1.7 + blade * math.tau / 5
            tip = (x + .116 * math.cos(angle), .042 + .018 * (blade % 3),
                   root[2] + .102 * math.sin(angle))
            leaf(module, root, tip, .043, "sage" if blade % 3 == 0 else "leaf")
        if trailing:
            bend = (x + .025, .047, .285)
            tip = (x - .028, -.205 - .043 * (index % 3), .330)
            module.rod(root, bend, .009, "leaf")
            module.rod(bend, tip, .008, "leaf")
            for step in range(3):
                t = step / 3
                node = tuple(a + (b - a) * t for a, b in zip(bend, tip))
                end = (node[0] + (-1) ** (step + index) * .067, node[1] - .063, node[2] + .027)
                leaf(module, node, end, .030, "leaf")
            # The runners trail over the trough. Each short pedicel turns
            # upwards whether its flower is above the soil or below the rim.
            for node, offset, size in (
                (root, (-.035, .098 + .024 * (index % 2), -.018), 1.10),
                (bend, (.042, .070, .036), .90),
                (tip, (-.035, .045, .032), 1.0)
            ):
                flower = tuple(a + b for a, b in zip(node, offset))
                module.rod(node, flower, .006, "leaf")
                petunia(module, flower, size)
        else:
            head = (x + .015 * (-1) ** index, .16 + .035 * (index % 3), root[2] + .025)
            module.rod(root, head, .009, "leaf")
            branch = (x, head[1] * .43, root[2] + .010)
            for side in (-1, 1):
                leaf(module, branch, (x + side * .093, branch[1] + .039, root[2] + .049), .042, "leaf")
            # The florets overlap to form the broad, irregular geranium head.
            for j in range(6):
                angle = j * math.tau / 5
                offset = .039 if j < 5 else 0
                module.lathe(head[0] + offset * math.cos(angle), head[1] + (.045 if j == 5 else -.017 + .014 * (j % 3)),
                             head[2] + offset * math.sin(angle), [(0, .017), (.032, .048), (.058, .030)],
                             "rose" if (index + j) % 4 == 0 else "flower", n=6)
    return module


def village():
    out=[]
    for name,width,colour in (("Single",.32,"oldwood"),("Repaired",.36,"wood"),("Slatted",.40,"green")):
        m=Module("Shutter"+name)
        for i in range(4):
            m.box(((i-1.5)*width/4,0,.015),(width/4-.006,.98,.055),colour if i!=2 or name!="Repaired" else "oldwood")
        for h in (-.33,.33):
            m.box((0,h,.054),(width+.014,.076,.04),"iron" if name=="Single" else "oldwood")
        m.box((0,0,.062),(.052,.77,.037),"oldwood",(0,0,-17))
        if name=="Slatted":
            for h in (-.19,-.07,.05,.17):
                m.box((0,h,.08),(width-.03,.052,.035),"wood",(18,0,0))
        out.append(m)
    m=Module("FoundationPatch")
    for x,y,w in ((-.27,.12,.49),(.24,.105,.45),(-.035,.31,.40)):
        m.box((x,y,0),(w,.19,.15),"cement",(2,2,2))
    out.append(m)
    return out


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    modules = windows() + balconies() + village() + [flower_box(False), flower_box(True)]
    assert len({m.name for m in modules})==len(modules)
    signature=hashlib.sha256()
    for m in modules:
        for geom,finish in m.parts:
            assert bp.signed_volume(geom)>0,(m.name,finish)
        signature.update(repr(m.parts).encode())
        m.export()
    paint()
    bpy.ops.export_scene.fbx(filepath=str(OUT/"FrontageModules.fbx"),object_types={"MESH"},
        axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_space_transform=False,
        add_leaf_bones=False,bake_anim=False,mesh_smooth_type="OFF",use_custom_props=False)
    print("Housing modules",len(modules),signature.hexdigest())


if __name__=="__main__":
    main()
