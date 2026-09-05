#!/usr/bin/env python3
"""Deterministic, passive fishing boats. Run with Blender 5 --background --python.

Authors Unity metres (+Z bow, Y=0 waterline), swapping Y/Z and reversing
winding on export. Two full-scale boats are presented smaller by the world.
No occupants, text, flags, collision, imported lights, animation or gameplay.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
MODEL_DIR = ROOT / "Assets/Resources/City/OffshoreBoats"
SOURCE_DIR = ROOT / "ArtSource/City/Blender"
MANIFEST = ROOT / "Assets/City/Models/CityOffshoreBoats3D.json"
NAMES = ("OldTrawler", "OldMotorboat")
PAINT = ((0.205, 0.285, 0.285, 1), (0.285, 0.295, 0.225, 1))
DARK = (0.085, 0.105, 0.108, 1)
TIMBER = (0.29, 0.25, 0.20, 1)
CABIN = (0.45, 0.455, 0.40, 1)
METAL = (0.19, 0.20, 0.19, 1)
WORN = (0.36, 0.345, 0.295, 1)


def source(p):
    return (p[0], p[2], p[1])


class Geometry:
    def __init__(self):
        self.vertices, self.faces, self.colors, self.uvs = [], [], [], []

    def add(self, vertices, faces, color, uvs=None, solid=True):
        # Every primitive is separately proven outward before it joins a role.
        vertices = [source(p) for p in vertices]
        faces = [tuple(reversed(f)) for f in faces]
        if solid:
            mesh = bpy.data.meshes.new("_validate")
            mesh.from_pydata(vertices, [], faces)
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
            bm.to_mesh(mesh)
            if bm.calc_volume(signed=True) <= 0:
                raise RuntimeError("Inward or zero-volume boat solid")
            faces = [tuple(p.vertices) for p in mesh.polygons]
            bm.free()
            bpy.data.meshes.remove(mesh)
        start = len(self.vertices)
        self.vertices.extend(vertices)
        self.faces.extend(tuple(start + i for i in face) for face in faces)
        self.colors.extend([color] * len(faces))
        self.uvs.extend(uvs or [(0, 0)] * len(vertices))

    def box(self, center, size, color):
        x, y, z = center
        a, b, c = (v / 2 for v in size)
        self.add([(x-a,y-b,z-c),(x+a,y-b,z-c),(x+a,y+b,z-c),(x-a,y+b,z-c),
                  (x-a,y-b,z+c),(x+a,y-b,z+c),(x+a,y+b,z+c),(x-a,y+b,z+c)],
                 [(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)], color)

    def rod(self, start, end, radius, color, segments=6, end_radius=None):
        a, b = Vector(start), Vector(end)
        direction = (b-a).normalized()
        tangent = direction.cross(Vector((0,1,0)))
        if tangent.length < .1:
            tangent = direction.cross(Vector((1,0,0)))
        tangent.normalize()
        second = direction.cross(tangent).normalized()
        verts = []
        for point, width in ((a, radius),(b, radius if end_radius is None else end_radius)):
            verts.extend(tuple(point + width * (math.cos(i*math.tau/segments)*tangent +
                          math.sin(i*math.tau/segments)*second)) for i in range(segments))
        faces = [tuple(reversed(range(segments))), tuple(range(segments,segments*2))]
        faces.extend((i,(i+1)%segments,(i+1)%segments+segments,i+segments) for i in range(segments))
        self.add(verts, faces, color)

    def object(self, name, parent, material):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        colors = mesh.color_attributes.new(name="Color", type="FLOAT_COLOR", domain="CORNER")
        uv = mesh.uv_layers.new(name="UVMap")
        for polygon, color in zip(mesh.polygons, self.colors):
            for loop in polygon.loop_indices:
                # Palette constants are visual sRGB swatches. Store their linear
                # values exactly once; FBX exports LINEAR explicitly below.
                colors.data[loop].color_srgb = color
                uv.data[loop].uv = self.uvs[mesh.loops[loop].vertex_index]
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = parent
        return obj


def empty(name, parent=None, position=(0,0,0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.location = source(position)
    return obj


def material(name, emission=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    shader = nodes.get("Principled BSDF")
    attribute = nodes.new("ShaderNodeVertexColor")
    attribute.layer_name = "Color"
    mat.node_tree.links.new(attribute.outputs["Color"], shader.inputs["Base Color"])
    shader.inputs["Roughness"].default_value = .87
    if emission:
        mat.node_tree.links.new(attribute.outputs["Color"], shader.inputs["Emission Color"])
        shader.inputs["Emission Strength"].default_value = 1.8
    return mat


def build_boat(variant, opaque, glow):
    root = empty(NAMES[variant])
    hull = Geometry()
    long = variant == 0
    length, width = (12.6, 4.2) if long else (10.4, 3.2)
    aft, bow = -length*.44, length*.56
    # The bow rises and narrows while the broad transom keeps the hull working.
    stations = [(aft,.68,.84),(aft+.8,.91,.83),(-2,.99,.83),(1,1,.92),
                (length*.32,.72,1.10),(length*.48,.32,1.39),(bow,.025,1.57)]
    ring_count = 8
    vertices = []
    for z, spread, sheer in stations:
        w = width * .5 * spread
        vertices.extend([(-w,sheer,z),(-w*.99,.12,z),(-w*.77,-.63,z),(-w*.3,-1.05,z),
                         (w*.3,-1.05,z),(w*.77,-.63,z),(w*.99,.12,z),(w,sheer,z)])
    faces = [tuple(reversed(range(ring_count)))]
    for station in range(len(stations)-1):
        for side in range(ring_count):
            a = station*ring_count+side
            b = station*ring_count+(side+1)%ring_count
            faces.append((a,b,b+ring_count,a+ring_count))
    faces.append(tuple(range((len(stations)-1)*ring_count,len(stations)*ring_count)))
    hull.add(vertices, faces, PAINT[variant])
    # A separate low dark strake follows the actual chine, not a floating stripe.
    for sign in (-1,1):
        for index in range(len(stations)-1):
            za, wa, ya = stations[index]
            zb, wb, yb = stations[index+1]
            hull.rod((sign*width*.5*wa,ya+.04,za),(sign*width*.5*wb,yb+.04,zb),.09,DARK)
            hull.rod((sign*width*.497*wa,.08,za),(sign*width*.497*wb,.08,zb),.072,DARK)
        # Raised bow safety rail ends before the working afterdeck.
        for i in (3,4,5):
            z,w,y = stations[i]
            hull.rod((sign*width*.5*w,y,z),(sign*width*.5*w,y+.62,z),.035,METAL)
        for i in (3,4):
            za,wa,ya = stations[i]
            zb,wb,yb = stations[i+1]
            hull.rod((sign*width*.5*wa,ya+.62,za),(sign*width*.5*wb,yb+.62,zb),.035,METAL)
    hull.box((0,.90,-1.55),(width*.73,.13,4.5 if long else 3.5),TIMBER)
    cabin_width = 2.50 if long else 1.94
    cabin_height = 1.95 if long else 1.6
    cabin_z = 1.65 if long else 1.1
    roof_y = .98+cabin_height
    hull.box((0,.98+cabin_height*.5,cabin_z),(cabin_width,cabin_height,2.75),CABIN)
    hull.box((0,roof_y+.11,cabin_z-.04),(cabin_width+.32,.22,3.07),DARK)
    # Black window reveals and narrow separating mullions remain legible in profile.
    for sign in (-1,1):
        for z in (cabin_z-.6,cabin_z+.6):
            hull.box((sign*(cabin_width*.5+.015),roof_y-.66,z),(.045,.74,.84),DARK)
        hull.box((sign*(cabin_width*.5+.027),1.62,cabin_z-1.0),(.03,.91,.49),WORN)
        hull.rod((sign*(cabin_width*.5+.13),1.25,cabin_z-.8),
                 (sign*(cabin_width*.5+.13),2.12,cabin_z-.8),.035,METAL)
    for x in (-cabin_width*.265,cabin_width*.265):
        hull.box((x,roof_y-.66,cabin_z+1.385),(cabin_width*.41,.75,.04),DARK)
    # One modest window, the other panes stay dark. Runtime owns emission strength.
    cabin_glow = Geometry()
    cabin_glow.box((-cabin_width*.265,roof_y-.66,cabin_z+1.412),
                   (cabin_width*.34,.62,.022),(.58,.42,.20,1))
    cabin_glow.box((-cabin_width*.5-.043,roof_y-.66,cabin_z+.6),
                   (.025,.62,.72),(.39,.30,.17,1))
    cabin_glow.box((cabin_width*.5+.043,roof_y-.66,cabin_z+.6),
                   (.025,.62,.72),(.39,.30,.17,1))
    cabin_glow.object("CabinGlow",root,glow)
    # Funnel, a raked mast, derrick and useful deck gear give two distinct profiles.
    mast_y = 5.50 if long else 4.02
    mast_z = -.25 if long else .05
    hull.rod((0,roof_y,mast_z),(0,mast_y,mast_z-.3),.10,METAL,end_radius=.055)
    hull.rod((-.72,mast_y-.78,mast_z-.22),(.72,mast_y-.78,mast_z-.22),.055,METAL)
    hull.rod((.65,roof_y,cabin_z-.8),(.65,roof_y+.74,cabin_z-.9),.20,DARK,8)
    hull.box((.65,roof_y+.79,cabin_z-.9),(.47,.13,.48),METAL)
    if long:
        for sign in (-1,1):
            hull.rod((sign*1.52,.98,-3.1),(sign*1.35,3.9,-3.0),.095,METAL)
        hull.rod((-1.35,3.9,-3),(1.35,3.9,-3),.10,METAL)
        hull.rod((0,3.6,-2.9),(.2,2.7,-4.75),.07,WORN)
        hull.rod((0,mast_y-.6,-.5),(0,3.9,-3),.022,DARK)
    else:
        hull.rod((-.7,1,-2.7),(-.7,2.8,-2.9),.075,METAL)
        hull.rod((-.7,2.75,-2.9),(.65,2.42,-3.8),.075,METAL)
    for sign in (-1,1):
        for z in (-2.45,-3.1):
            hull.rod((sign*.9,.99,z),(sign*.9,1.42,z),.30,(.20,.245,.215,1),8)
            for i in range(3):
                hull.rod((sign*.9-.24,1.10+i*.1,z),(sign*.9+.24,1.10+i*.1,z),.025,DARK)
    hull.box((.1,1.17,-3.85),(1.02,.40,.68),TIMBER)
    for x in (-.49,.49):
        hull.box((x,1.4,-3.85),(.07,.08,.7),WORN)
    # Sparse paint repairs: chips are small dull scuffs, never a rust aesthetic.
    rng = random.Random(820+variant)
    for sign in (-1,1):
        for i in range(10):
            z = rng.uniform(-1.8,.65)
            y = rng.uniform(.30,.64)
            hull.box((sign*(width*.496),y,z),(.028,rng.uniform(.03,.08),rng.uniform(.12,.42)),WORN)
    for z in (-4.3,3.3):
        for sign in (-1,1):
            x = sign*(1.2 if long else .85)
            hull.rod((x,1.08,z),(x,1.28,z),.075,DARK)
            hull.rod((x-.16,1.25,z),(x+.16,1.25,z),.05,DARK)
    hull.object("Hull",root,opaque)
    pivot_position = (0,roof_y+.48,cabin_z+.8)
    pivot = empty("SearchlightPivot",root,pivot_position)
    housing = Geometry()
    housing.box((0,-.24,-.04),(.48,.17,.43),METAL)
    housing.rod((0,0,-.29),(0,0,.12),.235,DARK,10)
    housing.rod((0,0,.10),(0,0,.18),.255,WORN,10)
    housing.object("SearchlightHousing",pivot,opaque)
    lens = Geometry()
    lens.rod((0,0,.182),(0,0,.198),.211,(.80,.59,.30,1),12)
    lens.object("Lens",pivot,glow)
    beam = Geometry()
    verts, uv = [], []
    for z, radius, along in ((.2,.19,0),(4, .65,.32),(8,1.2,.66),(12,1.7,1)):
        for i in range(12):
            angle = i*math.tau/12
            verts.append((math.cos(angle)*radius,math.sin(angle)*radius,z))
            uv.append((along,i/12))
    faces = [(r*12+i,r*12+(i+1)%12,(r+1)*12+(i+1)%12,(r+1)*12+i)
             for r in range(3) for i in range(12)]
    beam.add(verts,faces,(.42,.31,.16,1),uv,solid=False)
    beam.object("Beam",pivot,glow)
    wake = Geometry()
    verts = [(-.30,.035,aft+.1),(.30,.035,aft+.1),(-2.0,.035,aft-7.5),(2.0,.035,aft-7.5)]
    wake.add(verts,[(0,1,3,2)],(.18,.20,.18,1),[(0,0),(0,1),(1,0),(1,1)],solid=False)
    wake.object("Wake",root,opaque)
    empty("ANCHOR_Horn",root,(.45,roof_y+.29,cabin_z+.3))
    empty("ANCHOR_Engine",root,(0,.31,-1.15))
    # The horn itself is visibly attached to the cabin roof.
    horn = Geometry()
    horn.rod((.45,roof_y+.29,cabin_z+.05),(.45,roof_y+.29,cabin_z+.53),.055,METAL,8,end_radius=.16)
    horn_obj = horn.object("HornHousing",root,opaque)
    # Consolidate every non-moving opaque piece into the main Hull renderer.
    bpy.ops.object.select_all(action="DESELECT")
    hull_object = next(obj for obj in root.children if obj.name.split('.')[0] == "Hull")
    hull_object.select_set(True)
    horn_obj.select_set(True)
    bpy.context.view_layer.objects.active = hull_object
    bpy.ops.object.join()
    return root


def descendants(root):
    return [root] + list(root.children_recursive)


def describe(root, variant):
    points = [root.matrix_world.inverted() @ obj.matrix_world @ vertex.co
              for obj in root.children_recursive if obj.type == "MESH" and obj.name.split('.')[0] in ("Hull","SearchlightHousing")
              for vertex in obj.data.vertices]
    converted = [source(p) for p in points]
    low = [round(min(p[i] for p in converted),5) for i in range(3)]
    high = [round(max(p[i] for p in converted),5) for i in range(3)]
    meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    triangles = sum(sum(len(p.vertices)-2 for p in obj.data.polygons) for obj in meshes)
    if triangles > 7000 or low[1] > -.9 or high[2] <= abs(low[2]):
        raise RuntimeError("Boat geometry budget, waterline or +Z bow contract violated")
    anchors = []
    for obj in root.children_recursive:
        if obj.type == "EMPTY":
            point = root.matrix_world.inverted() @ obj.matrix_world.translation
            anchors.append({"name":obj.name.split('.')[0],"position":[round(v,5) for v in source(point)]})
    payload = {"variant":variant,"name":NAMES[variant],"bounds_min":low,"bounds_max":high,
               "triangle_count":triangles,"anchors":anchors,"beam_length_m":12.0}
    opaque_colors = [tuple(c.color[:3]) for obj in meshes if obj.name.split('.')[0] in ("Hull","SearchlightHousing")
                     for c in obj.data.color_attributes[0].data]
    payload["opaque_color_min"] = [round(min(c[i] for c in opaque_colors),6) for i in range(3)]
    payload["opaque_color_max"] = [round(max(c[i] for c in opaque_colors),6) for i in range(3)]
    geometry = []
    for obj in sorted(meshes,key=lambda o:o.name):
        geometry.append((obj.name.split('.')[0],[[round(v,6) for v in p.co] for p in obj.data.vertices],
                         [list(p.vertices) for p in obj.data.polygons],
                         [[round(v,6) for v in p.color] for p in obj.data.color_attributes[0].data]))
    payload["geometry_signature"] = hashlib.sha256(json.dumps(geometry,sort_keys=True).encode()).hexdigest()
    return payload


def export(root, path):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in descendants(root):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"EMPTY","MESH"},
                             axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=False,add_leaf_bones=False,bake_anim=False,
                             use_mesh_modifiers=True,mesh_smooth_type="FACE",colors_type="LINEAR")


def preview(roots):
    # Presentation objects are added only after export and excluded from manifest.
    for index, root in enumerate(roots):
        root.location.x = -4.7 if index == 0 else 4.7
        for obj in root.children_recursive:
            if obj.name.startswith(("Beam","Wake")):
                obj.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.world.color = (.23,.25,.25)
    target = Vector((0,0,1.4))
    cam_data = bpy.data.cameras.new("ReviewCamera")
    cam = bpy.data.objects.new("ReviewCamera",cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = (19,23,14)
    cam.rotation_euler = (target-cam.location).to_track_quat("-Z","Y").to_euler()
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 23
    scene.camera = cam
    light_data = bpy.data.lights.new("ReviewSoftbox","AREA")
    light_data.energy = 4200
    light_data.size = 16
    light = bpy.data.objects.new("ReviewSoftbox",light_data)
    bpy.context.collection.objects.link(light)
    light.location = (2,8,17)
    light.rotation_euler = (target-light.location).to_track_quat("-Z","Y").to_euler()
    scene.render.resolution_x,scene.render.resolution_y = 1300,850
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.filepath = str(SOURCE_DIR / "CityOffshoreBoats3D.png")
    bpy.ops.render.render(write_still=True)
    for root in roots:
        root.location = (0,0,0)


def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--no-preview",action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    MODEL_DIR.mkdir(parents=True,exist_ok=True)
    SOURCE_DIR.mkdir(parents=True,exist_ok=True)
    MANIFEST.parent.mkdir(parents=True,exist_ok=True)
    reset()
    opaque,glow = material("BoatVertexPaint"),material("BoatWarmGlass",True)
    roots = [build_boat(i,opaque,glow) for i in range(2)]
    bpy.context.view_layer.update()
    entries = [describe(root,i) for i,root in enumerate(roots)]
    for i,root in enumerate(roots):
        export(root,MODEL_DIR/(NAMES[i]+".fbx"))
    manifest = {"design_id":"city_offshore_fishing_boats_v1","generator":Path(__file__).name,
                "coordinate_system":"Unity +Z bow, +Y up, origin at waterline; authored metres",
                "color_space":"linear vertex RGB; visual sRGB swatches converted once at authoring; FBX LINEAR",
                "suggested_presentation_scale":.42,"variants":entries}
    MANIFEST.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_DIR/"CityOffshoreBoats3D.blend"),check_existing=False)
    if not args.no_preview:
        preview(roots)
    reset()
    rerun = [build_boat(i,opaque,glow) for i in range(2)]
    bpy.context.view_layer.update()
    repeated = [describe(root,i) for i,root in enumerate(rerun)]
    if repeated != entries:
        raise RuntimeError("Offshore boat deterministic rebuild mismatch")
    print(json.dumps(entries,indent=2))
    print("CITY OFFSHORE BOATS ART BUILD OK; deterministic rebuild matches")


if __name__ == "__main__":
    main()
