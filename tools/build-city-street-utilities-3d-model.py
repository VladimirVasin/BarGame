#!/usr/bin/env python3
"""Build the two fixed-metre municipal props. Unity +Z is the public side."""
import math
from pathlib import Path
import struct
import sys
import zlib

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as parts

OUT = ROOT / "Assets/Resources/City/StreetUtilities"
FINISHES = {
    "paint": ((83, 104, 96), .32), "iron": ((92, 88, 78), .48),
    "rubber": ((47, 48, 44), .28), "enamel": ((176, 169, 142), .49),
    "paper": ((115, 89, 63), .15), "concrete": ((123, 123, 113), .18),
    "inside": ((78, 83, 74), .25), "edge": ((148, 142, 125), .53),
}


def atlas(filename="StreetUtilityWear.png"):
    tile = 128
    rgba = np.empty((tile * 2, tile * 4, 4), dtype=np.uint8)
    y, x = np.mgrid[0:tile, 0:tile] / tile
    for i, (name, (color, gloss)) in enumerate(FINISHES.items()):
        rng = np.random.default_rng(20261123 + i)
        grit = rng.normal(0, 1, x.shape)
        cloud = np.sin(11*x + np.cos(y*9)) * np.sin(7*y + np.sin(13*x))
        shade = 4*cloud + grit*2
        if name in ("paint", "inside", "iron"):
            runoff = np.maximum(0, np.cos(41*x + .4*np.sin(y*8)) - .79)
            shade -= 62*runoff*(1-y)
        if name == "paper":
            tide = y + .12*np.sin(x*9) + .09*np.cos(x*15)
            shade += 2*np.sin(211*x) - 16*np.maximum(0, cloud+.18)
            shade -= 12*np.clip((.28-tide)*14,0,1) + 7*np.exp(-((tide-.28)/.021)**2)
        if name == "concrete":
            shade += grit*6
        color = color + shade[:, :, None]
        if name == "paint":
            chips = ((np.minimum(np.minimum(x, 1-x), np.minimum(y, 1-y)) < .032) & (cloud+grit*.4 > .2))
            color[chips] = (117, 90, 70)
        row, col = divmod(i, 4)
        swatch = rgba[row*tile:(row+1)*tile, col*tile:(col+1)*tile]
        swatch[:,:,:3] = np.clip(color, 0, 255)
        swatch[:,:,3] = np.clip(gloss*255 + 5*cloud, 0, 255)
    def chunk(tag, data):
        return struct.pack(">I", len(data))+tag+data+struct.pack(">I", zlib.crc32(tag+data))
    data = b"".join(b"\0"+row.tobytes() for row in rgba)
    (OUT/filename).write_bytes(b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",4*tile,2*tile,8,6,0,0,0))+chunk(b"IDAT",zlib.compress(data,9))+chunk(b"IEND",b""))


class Model:
    def __init__(self):
        self.groups = {}
        self.anchors = {}

    def add(self, group, finish, solid):
        self.groups.setdefault(group, []).append((finish, solid))

    def box(self, group, finish, center, size, bevel=.009, tilt=0):
        solid = parts.u_box(center, size, min(bevel, min(size)*.24))
        if tilt:
            solid = parts.u_rotated_about(solid, (0, tilt, 0), center)
        self.add(group, finish, solid)

    def rod(self, group, finish, a, b, radius):
        delta = Vector(b)-Vector(a)
        v, f = parts.u_cylinder((0,0,0), (2*radius,delta.length*.5,2*radius),10)
        q = Vector((0,1,0)).rotation_difference(delta)
        self.add(group,finish,([tuple((Vector(a)+Vector(b))*.5+q@Vector(p)) for p in v],f))

    def export(self, name, materials=None):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root = bpy.data.objects.new(name+"Root", None)
        bpy.context.collection.objects.link(root)
        def empty(key, xyz):
            obj = bpy.data.objects.new(key,None)
            bpy.context.collection.objects.link(obj)
            obj.parent = root
            x,y,z = xyz
            obj.location = (x,z,y)
            return obj
        for key, xyz in self.anchors.items():
            empty(key,xyz)
        for key, solids in self.groups.items():
            pivot = self.anchors.get(key,(0,0,0))
            vertices, faces, uv = [], [], []
            for finish, solid in solids:
                if parts.signed_volume(solid) < 0:
                    solid = (solid[0],[tuple(reversed(f)) for f in solid[1]])
                pts, fs = parts.to_source(solid)
                points = np.asarray(solid[0])
                low, span = points.min(axis=0), np.maximum(np.ptp(points,axis=0),.00001)
                row,col = divmod(list(FINISHES).index(finish),4)
                offset = len(vertices)
                vertices.extend((x-pivot[0],z-pivot[2],y-pivot[1]) for x,z,y in pts)
                for face in fs:
                    faces.append(tuple(offset+i for i in face))
                    a,b,c = points[list(face[:3])]
                    axes = ((2,1),(0,2),(0,1))[np.argmax(np.abs(np.cross(b-a,c-a)))]
                    for i in face:
                        u,v = ((points[i][j]-low[j])/span[j] for j in axes)
                        uv.append(((col+.025+.95*u)/4,(1-row+.025+.95*v)/2))
            mesh = bpy.data.meshes.new(key+"Mesh")
            slot = "UtilityGlass" if key in ("Glass","DoorGlass") else "UtilityLamp" if key == "Lamp" else "UtilityWear"
            if materials is not None:
                slot = materials.get(key, materials["default"])
            mesh.materials.append(bpy.data.materials.get(slot) or bpy.data.materials.new(slot))
            mesh.from_pydata(vertices,[],faces)
            mesh.update()
            mesh.uv_layers.new(name="Wear").data.foreach_set("uv",np.asarray(uv,dtype=np.float32).ravel())
            obj = bpy.data.objects.new(key+"Surface",mesh)
            bpy.context.collection.objects.link(obj)
            obj.parent = bpy.data.objects[key] if key in self.anchors else root
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(OUT/(name+".fbx")),use_selection=True,
            object_types={"MESH","EMPTY"},axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
            add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE")


def booth():
    m = Model()
    m.anchors.update(Door=(-.50,0,.49), Handle=(.37,1.09,.54),
                     Receiver=(.22,1.46,-.315), Key=(.005,1.38,-.27))
    box = m.box
    box("Frame","concrete",(0,-.065,.15),(1.52,.16,1.51),.028)
    box("Frame","rubber",(0,.025,.01),(1.24,.026,1.03))
    for x in (-.64,.64):
        for z in (-.52,.52):
            box("Frame","paint",(x,1.29,z),(.075,2.52,.075))
        for y in (.36,1.12,2.34):
            box("Frame","paint",(x,y,0),(.08,.055,1.02))
        box("Frame","paint",(x,.19,0),(.05,.28,1.01))
        box("Glass","inside",(x,1.36,0),(.018,1.92,.98))
    box("Frame","paint",(0,1.24,-.53),(1.26,2.44,.06))
    box("Frame","paint",(0,2.54,0),(1.46,.20,1.15),.033)
    box("Lamp","enamel",(0,2.56,.59),(1.32,.135,.035))
    for x in (-.57,.57):
        box("Frame","paint",(x,1.20,.49),(.095,2.40,.057))
    for x in (-.478,.478):
        box("Door","paint",(x,1.21,.49),(.062,2.33,.063))
    for y in (.08,.56,1.12,2.35):
        box("Door","paint",(0,y,.49),(.96,.065,.065))
    box("Door","paint",(0,.31,.49),(.93,.43,.04))
    # Separate glass child shares the door pivot.
    m.anchors["DoorGlass"] = m.anchors["Door"]
    box("DoorGlass","inside",(0,1.455,.49),(.93,1.72,.018))
    m.rod("Door","edge",(.37,.99,.54),(.37,1.18,.54),.024)
    box("Frame","iron",(0,1.39,-.39),(.51,.69,.20),.023)
    box("Frame","enamel",(-.04,1.56,-.28),(.23,.095,.022))
    # Raised, worn numerals on the cast keys. Cut the speaker grille and coin slot visually.
    segments = ((0,.010,.016,.0025),(.008,.005,.0025,.009),(.008,-.005,.0025,.009),
                (0,-.010,.016,.0025),(-.008,-.005,.0025,.009),(-.008,.005,.0025,.009),(0,0,.016,.0025))
    digits = ("bc","abdeg","abcdg","bcfg","acdfg","acdefg","abc","abcdefg","abcdfg","","abcdef","")
    for r in range(4):
        for c in range(3):
            # The caller looks toward -Z, so the right side of the keypad is -X.
            x, y = .007-.061*c, 1.44-.058*r
            number = 3*r+c+1
            group = f"Key{number}" if number in (2,4,5) else "Frame"
            if group != "Frame":
                m.anchors[group] = (x,y,-.261)
            box(group,"edge",(x,y,-.277),(.046,.042,.027))
            for letter in digits[3*r+c]:
                dx,dy,w,h = segments[ord(letter)-ord("a")]
                box(group,"rubber",(x-dx,y+dy,-.2617),(w,h,.0014),.00025)
    for x in (-.106,-.082,-.058,-.034,-.010):
        box("Frame","inside",(x,1.565,-.266),(.010,.037,.0025),.0005)
    box("Frame","rubber",(.052,1.562,-.266),(.048,.006,.003),.0006)
    box("Frame","rubber",(0,1.106,-.281),(.155,.027,.022))
    box("Frame","iron",(.21,1.50,-.345),(.113,.15,.12))
    box("Receiver","rubber",(.22,1.46,-.295),(.056,.218,.057),.014)
    for y in (1.34,1.58):
        box("Receiver","rubber",(.22,y,-.315),(.095,.081,.109),.019)
    # A single flexible black lead has two lengths joined at the hanging bight.
    for key in ("UpperCord", "LowerCord"):
        m.anchors[key] = (0,0,0)
        m.rod(key,"rubber",(0,0,0),(0,1,0),.007)
    return m


def wet_cardboard(index, floor, inner_x, inner_z):
    """A thin folded sheet with a nicked corner, fitted after its final rotation."""
    width = .62 + .055*(index % 4)
    depth = .29 + .027*(index % 5)
    thickness = .008 + .002*(index % 3)
    fold = .019 + .008*(index % 4)
    outline = ((-.49,-.46),(.10,-.50),(.46,-.43),(.50,.34),
               (.40,.48),(.10,.50),(-.48,.45),(-.50,-.26))
    angle = math.radians(19*index - 12)
    cs, sn = math.cos(angle), math.sin(angle)
    top = []
    for u,v in outline:
        x,z = u*width,v*depth
        y = floor + thickness + max(0,u-.10)*2*fold + .004*(v+.5)
        top.append((cs*x-sn*z, y, sn*x+cs*z))

    def fit(preferred, coordinate, limit):
        values = [p[coordinate] for p in top]
        return min(limit-max(values), max(-limit-min(values), preferred))

    gap = .025
    dx = fit(.47*math.sin(index*2.16), 0, inner_x-gap)
    dz = fit(.21*math.cos(index*.89), 2, inner_z-gap)
    upper = [(x+dx,y,z+dz) for x,y,z in top]
    vertices = upper + [(x,y-thickness,z) for x,y,z in upper]
    count = len(top)
    # Share the crease between the two top faces and their corresponding undersides.
    panels = [(0,1,5,6,7), (1,2,3,4,5)]
    faces = panels + [tuple(i+count for i in reversed(panel)) for panel in panels]
    for i in range(count):
        j = (i+1) % count
        faces.append((j,i,i+count,j+count))
    if any(abs(x)>inner_x-gap+1e-6 or abs(z)>inner_z-gap+1e-6 for x,_,z in vertices):
        raise ValueError(f"Cardboard {index} reaches a dumpster wall")
    return vertices, faces


def dumpster():
    m = Model()
    m.anchors.update(Lid=(0,1.08,-.46), Handle=(0,1.115,.435))
    box=m.box
    box("Body","concrete",(0,-.065,.15),(2.06,.15,1.55),.03)
    for x in (-.71,.71):
        for z in (-.34,.34):
            m.rod("Body","rubber",(x-.048,.145,z),(x+.048,.145,z),.138)
            box("Body","iron",(x,.235,z),(.094,.17,.12))
    floor_top = .315 + .075 / 2
    side_x, side_z = .855, .45
    wall_x, wall_z = .053, .052
    box("Body","inside",(0,.315,0),(1.73,.075,.89))
    for side in (-1,1):
        box("Body","paint",(side*side_x,.68,0),(wall_x,.77,.93))
        box("Body","paint",(0,.68,side*side_z),(1.71,.77,wall_z))
        box("Body","edge",(0,1.065,side*.45),(1.78,.04,.057))
        box("Body","edge",(side*.86,1.065,0),(.047,.04,.89))
    for x in (-.63,-.31,0,.31,.63):
        box("Body","paint",(x,.67,.483),(.054,.55,.034))
    for i in range(12):
        sheet = wet_cardboard(i, floor_top + i*.033, side_x-wall_x/2, side_z-wall_z/2)
        if i == 11:
            m.anchors["Cardboard"] = (0, .72, .16)
            front = max(sheet[0][:8], key=lambda point: point[2])
            m.anchors["CardboardGrip"] = front
            m.add("Cardboard", "paper", sheet)
        else:
            m.add("Body", "paper", sheet)
    box("Lid","paint",(0,1.101,-.004),(1.83,.068,1.005),.016)
    for x in (-.72,-.36,0,.36,.72):
        box("Lid","paint",(x,1.147,-.015),(.047,.034,.78))
    for x in (-.62,.62):
        m.rod("Body","iron",(x-.08,1.08,-.46),(x+.08,1.08,-.46),.042)
    m.rod("Lid","edge",(-.17,1.147,.464),(.17,1.147,.464),.022)
    return m


def bread_heel():
    model = Model()
    # Angular crust wraps an uneven exposed break. The wrapper folds under the crumb.
    ring = ((-.039,.010),(.035,.010),(.047,.022),(.043,.043),
            (.019,.061),(-.020,.065),(-.044,.050),(-.049,.027))
    def wedge(sections):
        vertices = [(x*scale, y*scale + .009, z + (i%3 - 1)*jag)
                    for z, scale, jag in sections for i,(x,y) in enumerate(ring)]
        n = len(ring)
        faces = [tuple(reversed(range(n)))]
        for offset in range(0, len(vertices)-n, n):
            for i in range(n):
                j = (i+1)%n
                faces.append((offset+i, offset+j, offset+n+j, offset+n+i))
        faces.append(tuple(range(len(vertices)-n, len(vertices))))
        return vertices, faces

    model.add("Bread", "paper", wedge(((-.039,.80,.001),(-.020,1,.003),(.023,.91,.006))))
    model.add("Bread", "enamel", wedge(((.015,.80,.003),(.027,.77,.006))))
    model.box("Bread", "inside", (0,.010,0), (.143,.006,.111), .002, tilt=12)
    model.box("Bread", "paper", (-.053,.020,.003), (.031,.012,.081), .002, tilt=-11)
    return model


def imported_surface(model, name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(model+".fbx")))
    obj = bpy.data.objects[name+"Surface"]
    mesh = obj.data
    points = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    tree = BVHTree.FromPolygons(points, [poly.vertices[:] for poly in mesh.polygons])
    uv = mesh.uv_layers.active.data
    finishes = []
    for poly in mesh.polygons:
        u,v = uv[poly.loop_start].uv
        finishes.append(list(FINISHES)[(1-int(2*v))*4+int(4*u)])
    return mesh, points, tree, finishes


def check_exports():
    # Sample the imported key faces from the caller's side. These strings list
    # the seven segments in screen order: top, upper-right, lower-right, bottom,
    # lower-left, upper-left, middle. The two blank corners are checked as well.
    _, _, panel, finishes = imported_surface("PhoneBooth","Frame")
    moving = {digit: imported_surface("PhoneBooth",f"Key{digit}")[2:] for digit in (2,4,5)}
    expected = ("0110000","1101101","1111001","0110011","1011011","1011111",
                "1110000","1111111","1111011","0000000","1111110","0000000")
    probes = ((0,.010),(.008,.005),(.008,-.005),(0,-.010),
              (-.008,-.005),(-.008,.005),(0,0))
    for button, strokes in enumerate(expected):
        row, column = divmod(button,3)
        surface, ink = moving.get(button+1,(panel,finishes))
        actual = ""
        for right, up in probes:
            origin = (.007-column*.061-right, -.22, 1.44-row*.058+up)
            _, _, face, _ = surface.ray_cast(origin,(0,-1,0),.12)
            if face is None:
                raise ValueError(f"Missing face on key at row {row+1}, column {column+1}")
            actual += "1" if ink[face] == "rubber" else "0"
        if actual != strokes:
            raise ValueError(f"Unreadable key at row {row+1}, column {column+1}: {actual}")

    print("FBX check passed: caller reads 123 / 456 / 789 / 0")


def check_dumpster():
    mesh, points, body, finishes = imported_surface("Dumpster","Body")
    paper = [points[i] for face,finish in zip(mesh.polygons,finishes)
             if finish=="paper" for i in face.vertices]
    _, moving_points, _, moving_finishes = imported_surface("Dumpster", "Cardboard")
    if set(moving_finishes) != {"paper"}:
        raise ValueError("The hinged cardboard must retain its paper finish")
    paper.extend(moving_points)
    if not paper:
        raise ValueError("No cardboard in the exported dumpster")
    clearance = float("inf")
    for axis in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0)):
        _, _, wall, distance = body.ray_cast((0,0,.80),axis,1)
        if wall is None or finishes[wall] != "paint":
            raise ValueError(f"Missing inner wall on axis {axis}")
        clearance = min(clearance, distance-max(point.dot(Vector(axis)) for point in paper))
    if clearance < .0249:
        raise ValueError(f"Insufficient cardboard clearance: {clearance:.4f} m")
    print(f"FBX check passed: minimum cardboard clearance {clearance:.3f} m")


if __name__ == "__main__":
    if "--model-dir" in sys.argv:
        OUT = Path(sys.argv[sys.argv.index("--model-dir")+1])
    dumpster_only = "--dumpster-only" in sys.argv
    if "--validate-only" not in sys.argv:
        OUT.mkdir(parents=True,exist_ok=True)
        if not dumpster_only:
            atlas()
            booth().export("PhoneBooth")
        dumpster().export("Dumpster")
        bread_heel().export("BreadHeel")
    if not dumpster_only:
        check_exports()
    check_dumpster()
    _, bread_points, _, _ = imported_surface("BreadHeel", "Bread")
    bounds = [max(p[i] for p in bread_points)-min(p[i] for p in bread_points) for i in range(3)]
    if not all(.04 < size < .20 for size in bounds):
        raise ValueError(f"Bread heel has invalid hand-scale bounds: {bounds}")
