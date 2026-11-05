#!/usr/bin/env python3
"""Fixed-metre Ridge Street kiosk and fried potato pie. Local +Z faces the street."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import sys
import zlib

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as parts
import interior_kit as kit

OUT = ROOT / "Assets/Resources/RidgeKiosk"
SOURCE = ROOT / "ArtSource/City/RidgeKiosk"
ANCHORS = {
    "KioskVendor": (0, .08, .45), "KioskLamp": (0, 2.13, .93),
    "KioskBeer": (-1.04, 1.04, 1.36), "KioskVodka": (-.42, 1.04, 1.36),
    "KioskPie": (.78, 1.04, 1.36), "KioskTalk": (0, 1.63, 1.17),
    "KioskApproach": (0, 0, 2.22),
}
# One paint/material atlas. Its alpha is smoothness for the URP lit surface.
SURFACES = {
    "sage": ((103, 127, 116), .34), "kick": ((65, 83, 80), .37),
    "iron": ((52, 57, 55), .46), "aluminium": ((162, 160, 147), .66),
    "roof": ((89, 95, 91), .50), "concrete": ((132, 128, 116), .15),
    "wood": ((126, 100, 73), .27), "enamel": ((182, 178, 151), .49),
    "paper": ((168, 139, 100), .12), "pastry": ((194, 135, 66), .27),
    "seam": ((156, 95, 43), .35), "tile": ((151, 151, 129), .35),
    "gasket": ((52, 50, 45), .20), "rust": ((111, 78, 57), .21),
    "tea": ((129, 88, 67), .45), "stitch": ((176, 164, 130), .27),
}
ATLAS = "RidgeKioskSurface"
GLASS = "KioskSashGlass"
LAMP = "KioskLampGlass"


def field(rng, size, cells):
    """Wrap an interpolated noise field periodically within the swatch."""
    grid = rng.normal(size=(cells, cells))
    p = np.arange(size) * cells / size
    integer = p.astype(int)
    t = p - integer
    t = t*t*(3-2*t)
    a = grid[integer[:, None], integer]
    b = grid[integer[:, None], (integer+1) % cells]
    c = grid[(integer[:, None]+1) % cells, integer]
    d = grid[(integer[:, None]+1) % cells, (integer+1) % cells]
    return ((1-t)*a + t*b)*(1-t[:, None]) + ((1-t)*c + t*d)*t[:, None]


def atlas_bytes(cell=256):
    pixels = np.zeros((cell*4, cell*4, 4), dtype=np.uint8)
    y, x = np.mgrid[0:cell, 0:cell] / cell
    for index, (name, (rgb, smoothness)) in enumerate(SURFACES.items()):
        rng = np.random.default_rng(834 + index)
        cloud, local = field(rng, cell, 5), field(rng, cell, 24)
        grit = rng.normal(size=(cell, cell))
        shade = 3.3*cloud + 2*local + 1.6*grit
        stain = np.zeros_like(x)
        if name in ("sage", "kick", "roof"):
            runoff = np.maximum(0, np.sin(x*77 + 2*cloud) - .62)
            shade -= (5 + 16*y**3) * runoff
            stain = np.clip((y-.64)*1.8 + local*.12 + grit*.06, 0, .8)
        elif name == "wood":
            grain = np.sin(37*y + 2*cloud + 3*np.sin(6*x+9*y))
            shade += 7*grain + 2*np.sin(grain*12)
        elif name == "concrete":
            shade += 4*grit + 5*local
        elif name in ("pastry", "seam"):
            # Bubbles in the fried skin, dispersed blisters and dark oil spots.
            shade += 15*cloud + 8*local + 2*grit
            for _ in range(150):
                cx, cy = rng.random(2)
                rx = rng.uniform(.004, .023)
                d = ((x-cx)/rx)**2 + ((y-cy)/(rx*.71))**2
                shade += 13*np.exp(-3.7*d) - 11*np.exp(-12*(d-.82)**2)
        elif name in ("paper", "stitch"):
            shade += 4*cloud + 2*np.sin(x*851) - 7*np.maximum(0, cloud-.6)
        elif name == "aluminium":
            shade += 2*np.sin(y*1214) + 2*cloud
        tone = np.array(rgb)[None, None, :] + shade[:, :, None]
        tone += stain[:, :, None]*np.array((7, -8, -12))
        rgba = np.dstack((np.clip(tone, 0, 255), np.clip(255*smoothness - 6*cloud - 4*local, 0, 255)))
        row, col = divmod(index, 4)
        pixels[row*cell:(row+1)*cell, col*cell:(col+1)*cell] = rgba.astype(np.uint8)
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag+data))
    width = cell*4
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, width, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(b"".join(b"\0" + row.tobytes() for row in pixels), 9)) + chunk(b"IEND", b""))


class Model:
    def __init__(self):
        self.solids = []

    def add(self, surface, solid):
        vertices, faces = solid
        volume = parts.signed_volume(solid)
        if abs(volume) < 1e-13:
            raise ValueError(f"Degenerate solid on {surface}")
        if volume < 0:
            faces = [tuple(reversed(face)) for face in faces]
        self.solids.append((surface, (vertices, faces)))

    def box(self, surface, xyz, size, bevel=.008):
        self.add(surface, parts.u_box(xyz, size, bevel))

    def beam(self, surface, first, second, width, depth):
        delta = Vector(second) - Vector(first)
        mid = (Vector(second) + Vector(first)) * .5
        geom = parts.u_box((0, 0, 0), (width, delta.length, depth), min(width, depth)*.16)
        rotation = Vector((0, 1, 0)).rotation_difference(delta)
        self.add(surface, ([tuple(mid + rotation @ Vector(v)) for v in geom[0]], geom[1]))

    def cylinder(self, surface, xyz, radius, height, sides=12):
        self.add(surface, parts.u_cylinder(xyz, (2*radius, height/2, 2*radius), sides))


def kiosk():
    m = Model()
    # Plinth and the top landing meet the graded pavement on its own level.
    m.box("concrete", (0, -.205, 0), (2.91, .49, 2.40), .018)
    m.box("concrete", (0, -.25, 2.315), (3, .50, 2.17), .006)
    m.box("iron", (0, .054, 0), (3, .052, 2.50))
    m.box("tile", (0, .068, 0), (2.91, .024, 2.40), .003)
    m.box("sage", (0, 1.31, -1.205), (2.93, 2.46, .09))
    for x in (-1.455, 1.455):
        m.box("sage", (x, 1.31, 0), (.09, 2.46, 2.43))
    # Wainscot and projecting sill are separate from the clear aperture.
    m.box("sage", (0, .54, 1.205), (2.93, .92, .09))
    m.box("sage", (0, 2.335, 1.205), (2.93, .43, .09))
    for x in (-1.30, 1.30):
        m.box("sage", (x, 1.57, 1.205), (.37, 1.14, .09))
    for x in (-1.47, 1.47):
        for z in (-1.22, 1.22):
            m.box("kick", (x, 1.32, z), (.062, 2.51, .064))
        m.box("kick", (x, .225, 0), (.064, .29, 2.47))
        for z in (-.84, -.39, .06, .51, .96):
            m.box("sage", (x, 1.42, z), (.066, 2.09, .024), .003)
    for z in (-1.22, 1.22):
        m.box("kick", (0, .22, z), (2.99, .28, .064))
        for x in (-.98, -.50, 0, .50, .98):
            m.box("sage", (x, .68 if z > 0 else 1.415, z), (.028, .57 if z > 0 else 2.08, .065), .002)
    # Staff service door in the side wall with return, worn threshold and latch.
    m.box("iron", (-1.493, 1.11, -.21), (.034, 2.02, .84))
    m.box("kick", (-1.514, 1.11, -.21), (.028, 1.96, .79))
    m.box("sage", (-1.531, 1.26, -.21), (.012, 1.59, .71))
    for y in (.36, 1.82):
        m.box("iron", (-1.536, y, -.58), (.035, .102, .05))
    m.box("aluminium", (-1.55, 1.08, .10), (.036, .20, .057))
    m.box("iron", (-1.58, 1.09, .055), (.04, .025, .12))
    # The sheet falls to the rear. Raised seams, a shallow fascia and a hemmed canopy.
    m.box("roof", (0, 2.54, -.025), (3.10, .06, 2.59))
    for x in (-1.54, 1.54):
        m.box("iron", (x, 2.505, -.015), (.052, .134, 2.61))
    for x in (-1.37, -.82, -.27, .28, .83, 1.38):
        m.beam("roof", (x, 2.574, -1.30), (x, 2.606, 1.265), .028, .030)
    m.box("kick", (0, 2.536, 1.275), (3.14, .155, .066))
    m.box("aluminium", (0, 2.608, 1.275), (3.14, .021, .082), .002)
    canopy = parts.u_rotated(parts.u_box((0,0,0), (2.74, .051, .60)), (8, 0, 0))
    m.add("roof", kit.translated(canopy, (0, 2.255, 1.475)))
    m.box("kick", (0, 2.208, 1.763), (2.76, .073, .034))
    for x in (-1.27, 1.27):
        m.beam("iron", (x, 1.85, 1.265), (x, 2.202, 1.686), .03, .033)
    # Sliding glass is parked in a pair over the right-hand jamb. Sightline to the face stays open.
    for x in (-1.11, 1.11):
        m.box("aluminium", (x, 1.575, 1.247), (.047, 1.11, .045))
    for y in (1.055, 2.108):
        m.box("aluminium", (0, y, 1.248), (2.26, .033, .07))
    for x, z in ((.99, 1.221), (.973, 1.249)):
        m.box(GLASS, (x, 1.58, z), (.23, 1.012, .005), .001)
        m.box("aluminium", (x-.116, 1.58, z), (.018, 1.025, .016), .002)
    m.box("iron", (.858, 1.48, 1.264), (.032, .119, .034))
    m.box("wood", (0, .989, 1.207), (2.44, .066, .588))
    m.box("aluminium", (0, 1.028, 1.207), (2.48, .024, .594), .003)
    for x in (-.88, .81):
        m.beam("iron", (x,.74,1.25), (x,.95,1.43), .033, .037)
    # Enamel cold cabinet, modest storage and the vendor's work surface.
    m.box("enamel", (-.92, .826, -.73), (.74, 1.49, .65), .030)
    m.box("gasket", (-.92, .86, -.396), (.691, 1.31, .027), .015)
    m.box("enamel", (-.92, .84, -.37), (.672, 1.24, .040), .018)
    m.box("aluminium", (-.663, 1.097, -.325), (.025, .257, .044))
    for y in (.204, .239, .274):
        m.box("iron", (-.95, y, -.339), (.43,.012,.021), .002)
    for y in (.94, 1.53, 1.95):
        m.box("wood", (.49, y, -.91), (1.57, .045, .43))
        for x in (-.06, 1.03):
            m.beam("iron", (x, y-.17, -1.13), (x,y-.025,-.78), .021,.024)
    for i, x in enumerate((-.09, .07, .27, .78, .94)):
        height = (.165,.152,.192,.18,.175)[i]
        m.cylinder("tea" if i<3 else "enamel", (x,1.553+height*.5,-.87), .052, height)
        m.cylinder("aluminium", (x,1.556+height,-.87), .054, .009)
    for i in range(5):
        m.box("paper", (.64+i*.044, 1.65, -.9), (.040,.176,.123), .004)
    # Halted warming tray: the hinged lid is tipped behind the pastry stock.
    m.box("enamel", (.75,1.065,1.076), (.55,.048,.22), .008)
    for x in (.49, 1.01):
        m.box("aluminium", (x, 1.095, 1.076), (.025, .043,.223))
    lid = parts.u_rotated(parts.u_box((0,0,0), (.55,.022,.226)), (-61,0,0))
    m.add("enamel", kit.translated(lid, (.75,1.184,.925)))
    m.box("iron", (.75,1.271,.956), (.116,.024,.027))
    for i, x in enumerate((.575, .735, .895)):
        m.add("pastry", kit.translated(pastry(.86+i*.03), (x,1.088,1.073)))
    # Frosted diffuser underneath its visible sheet-metal shade, at the lamp anchor.
    m.box("enamel", (0,2.184,.93), (.58,.074,.16))
    m.box(LAMP, (0,2.144,.93), (.49,.032,.103))
    # Side vent, visible rivets and a single repair where road spray reaches the skin.
    for i in range(7):
        m.box("iron",(1.51,.76+.032*i,-.55), (.029,.014,.37), .003)
    m.box("kick",(1.51,.45,.68), (.027,.32,.48))
    for z in (.47,.89):
        for y in (.32,.58):
            m.box("rust",(1.532,y,z), (.020,.016,.016), .002)
    return m


def crown(x):
    profile = max(.002, 1 - (x/.075)**2)**.62
    return .019 + .009*profile + .026*profile


def pastry(scale=1.0):
    # The pan flattens the lower face. Hand-stretched dough bulges on either
    # side of the top fold; the two ends narrow before they turn under.
    rings, sides = 29, 18
    points, faces = [], []
    for row in range(rings):
        x = -.07497*math.cos(math.pi*row/(rings-1))
        u = x / .075
        profile = max(.002, 1-u*u)**.62
        center = .019 + .009*profile
        for i in range(sides):
            angle = math.tau*i/sides
            h = math.sin(angle)
            ripple = .024*math.cos(4.7*math.pi*u+2.8*angle)+.013*math.sin(8.4*math.pi*u-angle)
            z = .045*profile*math.cos(angle)*(1+ripple) + .003*profile*math.sin(2.4*u)
            y = center + h*.026*profile*(1 + .052*math.sin(3*angle+4*u))
            y = max(.0034+.0018*(1-profile), y)
            points.append((x*scale, y*scale, z*scale))
    faces.append(tuple(reversed(range(sides))))
    for row in range(rings-1):
        for i in range(sides):
            a = row*sides+i
            b = row*sides+(i+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.append(tuple(range((rings-1)*sides,rings*sides)))
    return points, faces


def seam():
    # Continuous crimp: an indented, slightly asymmetrical crest melds into the dough.
    points, faces = [], []
    steps = 53
    for i in range(steps):
        t=i/(steps-1)
        x=-.065+.130*t
        taper=.30+.70*math.sin(math.pi*t)**.48
        phase=t*math.pi*18
        z=.0025*math.sin(3.3*t)-.0014+.0014*math.sin(phase)
        y=crown(x)-.003
        for dz,dy in ((-.007,-.004),(-.0045,.005),(.0007,.0085),(.0045,.0035),(.006,-.005)):
            roll=.0015*math.cos(phase+dz*270)
            points.append((x,y+(dy+roll)*taper,z+dz*taper))
    faces.append(tuple(reversed(range(5))))
    for row in range(steps-1):
        for i in range(5):
            a=row*5+i
            b=row*5+(i+1)%5
            faces.append((a,b,b+5,a+5))
    faces.append(tuple(range((steps-1)*5,steps*5)))
    return points, faces


def paper_fold(m, polygon, thickness=.0014):
    """Folded paper faces have a real back side in world and 3D receipt inspection."""
    normal = (Vector(polygon[1])-Vector(polygon[0])).cross(Vector(polygon[2])-Vector(polygon[0])).normalized()
    n = len(polygon)
    v = list(polygon) + [tuple(Vector(p)-normal*thickness) for p in polygon]
    f = [tuple(range(n)), tuple(reversed(range(n,2*n)))]
    f += [(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)]
    # Top face faces away from the matching backside. Rim winding must follow it.
    f[2:] = [tuple(reversed(face)) for face in f[2:]]
    m.add("paper", (v,f))


def pie():
    m = Model()
    m.add("pastry", pastry())
    m.add("seam", seam())
    m.box("paper", (-.015,.002,0), (.131,.004,.095), .001)
    for side in (-1,1):
        edge = .047*side
        paper_fold(m, [(-.079,.003,edge),(.052,.003,edge),(.038,.013,side*.043),
                       (-.025,.037,side*.040),(-.074,.034,side*.029)])
        paper_fold(m, [(-.078,.004,edge),(-.074,.034,side*.029),(-.069,.033,0)])
    paper_fold(m, [(-.080,.004,-.045),(-.080,.004,.045),(-.072,.034,.028),
                   (-.068,.037,0),(-.072,.033,-.028)])
    return m


def signature(model):
    stable = [(surface, [[tuple(round(x,7) for x in point) for point in vertices], faces])
              for surface,(vertices,faces) in model.solids]
    return hashlib.sha256(json.dumps(stable,separators=(",",":")).encode()).hexdigest()


def measure(model):
    combined = kit.merge_all(solid for _,solid in model.solids)
    low, high = kit.bounds(combined)
    return dict(bounds_min=[round(v,5) for v in low],bounds_max=[round(v,5) for v in high],
                triangles=kit.triangle_count(combined), geometry_sha256=signature(model))


def swatch_uv(points, faces, surface):
    # Project each closed solid, leaving padding in the tile for mip filtering.
    tile = list(SURFACES).index(surface) if surface in SURFACES else 0
    row, col = divmod(tile,4)
    low = np.min(points,axis=0)
    size = np.maximum(np.max(points,axis=0) - low, .0001)
    result = []
    for f in faces:
        p0,p1,p2 = (np.array(points[i]) for i in f[:3])
        normal_axis = int(np.argmax(np.abs(np.cross(p1-p0,p2-p0))))
        # A rounded crust must have a continuous mapping across the shoulder.
        # Orthogonal wall projections would produce a stair of visible seams.
        axes = (0,2) if surface in ("pastry", "seam") else ((2,1),(0,2),(0,1))[normal_axis]
        for i in f:
            u,v = ((points[i][axis]-low[axis])/size[axis] for axis in axes)
            result.append(((col+.026+.948*u)/4,(3-row+.026+.948*v)/4))
    return result


def emit(name, model, marks, model_dir, source_dir, image):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials = {}
    for slot in (ATLAS, GLASS, LAMP):
        mat = bpy.data.materials.new(slot)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if slot == ATLAS:
            node = mat.node_tree.nodes.new("ShaderNodeTexImage")
            node.image = bpy.data.images.load(str(image))
            node.image.alpha_mode = "CHANNEL_PACKED"
            mat.node_tree.links.new(node.outputs["Color"],bsdf.inputs["Base Color"])
        else:
            bsdf.inputs["Base Color"].default_value = (.29,.38,.37,1) if slot == GLASS else (.91,.78,.49,1)
            if slot == GLASS:
                bsdf.inputs["Alpha"].default_value = .4
        materials[slot] = mat
    root = bpy.data.objects.new(name+"_MetricRoot", None)
    bpy.context.collection.objects.link(root)
    for slot in (ATLAS, GLASS, LAMP):
        solids = [(s,solid) for s,solid in model.solids if (s if s in (GLASS,LAMP) else ATLAS) == slot]
        if not solids:
            continue
        vertices, faces, uvs, smooth = [], [], [], []
        for surface, solid in solids:
            points, oriented = solid
            source_points, source_faces = parts.to_source(solid)
            offset = len(vertices)
            vertices.extend(source_points)
            faces.extend([tuple(offset+i for i in f) for f in source_faces])
            smooth.extend([surface in ("pastry", "seam")]*len(source_faces))
            uvs.extend(swatch_uv(points,source_faces,surface))
        mesh = bpy.data.meshes.new(name+"_"+slot)
        mesh.from_pydata(vertices,[],faces)
        mesh.update()
        mesh.uv_layers.new(name="UVMap").data.foreach_set("uv",np.array(uvs,dtype=np.float32).ravel())
        mesh.polygons.foreach_set("use_smooth",smooth)
        mesh.materials.append(materials[slot])
        obj = bpy.data.objects.new(mesh.name,mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
    for key,(x,y,z) in marks.items():
        marker = bpy.data.objects.new(key,None)
        bpy.context.collection.objects.link(marker)
        marker.empty_display_size = .04
        marker.location = (x,z,y)
        marker.parent = root
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(model_dir/(name+".fbx")), use_selection=True,
                              object_types={"MESH","EMPTY"}, axis_forward="-Z", axis_up="Y",
                              apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
                              add_leaf_bones=False, bake_anim=False, mesh_smooth_type="FACE", path_mode="STRIP")
    # Both editing files share the tracked atlas, including after a staged build.
    for texture in bpy.data.images:
        if texture.source == "FILE" and texture.filepath:
            texture.filepath = "//" + os.path.relpath(OUT/image.name, SOURCE).replace(os.sep, "/")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir/(name+".blend")), compress=True, relative_remap=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=OUT)
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    parser.add_argument("--validate-only",action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    builds = (("RidgeKiosk", kiosk, ANCHORS),("PotatoPie", pie, {}))
    texture = atlas_bytes()
    payload = {"front":"+Z","unit":"metre","palette_sha256":hashlib.sha256(texture).hexdigest(),
               "anchors":ANCHORS,"models":{}}
    for name,make,_ in builds:
        record = measure(make())
        if record != measure(make()):
            raise ValueError(name+" geometry is not deterministic")
        payload["models"][name] = record
    bounds = payload["models"]["RidgeKiosk"]
    assert bounds["bounds_min"][0] < -1.5 and bounds["bounds_min"][1] == -.5
    assert bounds["bounds_max"][2] == 3.4 and 2.61 < bounds["bounds_max"][1] < 2.65
    assert sum(record["triangles"] for record in payload["models"].values()) < 13000
    manifest = json.dumps(payload, indent=2)+"\n"
    if args.validate_only:
        assert (args.model_dir/"RidgeKiosk.json").read_text() == manifest
        assert (args.model_dir/"RidgeKioskPalette.png").read_bytes() == texture
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        path = args.model_dir/"RidgeKioskPalette.png"
        path.write_bytes(texture)
        (args.model_dir/"RidgeKiosk.json").write_text(manifest)
        for name,make,marks in builds:
            emit(name,make(),marks,args.model_dir,args.source_dir,path)
    print("RIDGE KIOSK AND POTATO PIE OK")

if __name__ == "__main__":
    main()
