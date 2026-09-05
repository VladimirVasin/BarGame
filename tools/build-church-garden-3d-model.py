#!/usr/bin/env python3
"""Deterministic fixed-metre church garden sculpture and small forms.

Blender 5: --background --factory-startup --python tools/build-church-garden-3d-model.py
The passive kit has no collider, light, text, animation, sacred mechanic or rig.
All pieces stand at their own origin and face Blender +Y / Unity +Z. The water
and stream deliberately use the same ground origin as the complete fountain.
FBX_SCALE_ALL and baked axes follow the park chess mesh pipeline: Unity gets
metre-space bare meshes, not the usual hidden hundredfold authoring root.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import sys

import bpy
import bmesh
from mathutils import Vector

VERSION = "1.1.0"
DESIGN_ID = "church_garden_v1"
ROOT = Path(__file__).resolve().parents[1]
ASSET = ROOT / "Assets/ChurchGarden/Models"
SOURCE = ROOT / "ArtSource/ChurchGarden/Blender"
TEXTURES = ROOT / "Assets/ChurchGarden/Textures"
KINDS = ("FountainStone", "FountainWater", "FountainStream", "MaryStatue",
         "PotSmall", "PotMedium", "PotLarge", "StonePottingLedge",
         "HedgeSegment", "GardenUplight")
COLORS = {"Stone": (0.49, 0.515, 0.49, 1),
          "Statue": (0.66, 0.665, 0.605, 1),
          "Water": (0.145, 0.19, 0.18, 1),
          "Stream": (0.365, 0.405, 0.38, 1),
          "Terracotta": (0.43, 0.255, 0.17, 1),
          "Foliage": (0.115, 0.185, 0.11, 1),
          "Metal": (0.095, 0.11, 0.105, 1),
          "Lens": (0.79, 0.73, 0.59, 1)}

# The extension must leave the already accepted fountain/statue/pot geometry
# and UVs byte-identical at the stable rounded mesh-signature boundary.
LEGACY_MESH_SIGNATURES = {
    "GEO_FountainStone": "f9e0262a925e8df9198598e0e9b431915903d9e472d21a17b400e77f3a0e2991",
    "GEO_FountainStream": "d5f013abe04281ff537418900237b7aa68e04b78e50d8f322738795d17b202d8",
    "GEO_FountainWater": "2f003064450d982196d9204f4c02526b6f8db50198f0acbdb9dec3a1487a5083",
    "GEO_MaryStatue": "cd44064be44f3629f7d56bebbd3331348f564d0cbca78d1bab8b8ba16e58a541",
    "GEO_PotLarge": "4d3cc3655b72300decc0643adb9f628c665676d59f8202ab73c7d386a5bbb631",
    "GEO_PotMedium": "0b7b07ba7e245512c16e73384da0f4479eed7b74bbd0f37513ac60eba0ab754b",
    "GEO_PotSmall": "2f9eafacdfda66c1bdeae80eae17b929500953d978a30eb4388be54b9f403346",
    "GEO_StonePottingLedge": "0d3f2e1a182680751419d046e84567ef2c1859ccee34b878fcfc84929a979dde",
}
UPLIGHT_DIRECTION = Vector((0, math.cos(math.radians(35)), math.sin(math.radians(35))))
UPLIGHT_CENTRE = Vector((0, .02, .106))
UPLIGHT_LENS = UPLIGHT_CENTRE + UPLIGHT_DIRECTION * .042


class Mesh:
    def __init__(self):
        self.vertices, self.faces = [], []
        self.material_indices = []
        self.active_material = 0

    def part(self, vertices, faces):
        """Orient each closed authored component before adding it to the kit."""
        volume = 0.0
        for face in faces:
            a = Vector(vertices[face[0]])
            for index in range(1, len(face) - 1):
                b, c = Vector(vertices[face[index]]), Vector(vertices[face[index + 1]])
                volume += a.dot(b.cross(c)) / 6.0
        if abs(volume) < 1e-10:
            raise ValueError("A garden component must be a closed physical solid")
        if volume < 0:
            faces = [tuple(reversed(face)) for face in faces]
        offset = len(self.vertices)
        self.vertices.extend(vertices)
        self.faces.extend(tuple(offset + i for i in face) for face in faces)
        self.material_indices.extend([self.active_material] * len(faces))

    def rings(self, rings, close=True):
        vertices = [point for ring in rings for point in ring]
        n = len(rings[0])
        if any(len(ring) != n for ring in rings):
            raise ValueError("Every loft ring needs the same cardinality")
        faces = []
        for row in range(len(rings) - 1):
            for i in range(n):
                j = (i + 1) % n
                faces.append((row*n+i, row*n+j, (row+1)*n+j, (row+1)*n+i))
        if close:
            faces.extend((tuple(reversed(range(n))),
                          tuple((len(rings)-1)*n+i for i in range(n))))
        self.part(vertices, faces)

    def lathe(self, profile, n=24, center=(0, 0, 0), phase=0):
        # The profile is a complete boundary: a hollow pot returns down its
        # inner wall and across its bottom, never a capped solid cylinder.
        rings = []
        for radius, z in profile:
            rings.append([(center[0] + radius*math.cos(math.tau*i/n+phase),
                           center[1] + radius*math.sin(math.tau*i/n+phase),
                           center[2] + z) for i in range(n)])
        self.rings(rings)

    def ellipsoid(self, center, radii, n=16, rows=10):
        # Tiny end rings keep all quads nondegenerate and close with n-gons.
        rings = []
        for j in range(rows + 1):
            a = math.pi * (0.002 + .996*j/rows)
            rings.append([(center[0]+radii[0]*math.sin(a)*math.cos(math.tau*i/n),
                           center[1]+radii[1]*math.sin(a)*math.sin(math.tau*i/n),
                           center[2]-radii[2]*math.cos(a)) for i in range(n)])
        self.rings(rings)

    def tube(self, points, radii, n=10):
        rings = []
        for j, point in enumerate(points):
            tangent = Vector(points[min(j+1, len(points)-1)]) - Vector(points[max(0,j-1)])
            tangent.normalize()
            helper = Vector((1,0,0)) if abs(tangent.x) < .9 else Vector((0,1,0))
            u = tangent.cross(helper).normalized()
            v = tangent.cross(u).normalized()
            rings.append([tuple(Vector(point) + radii[j]*(u*math.cos(math.tau*i/n) +
                                                         v*math.sin(math.tau*i/n)))
                          for i in range(n)])
        self.rings(rings)

    def dressed_slab(self, width, depth, z0, z1, bevel=.02, x=0, y=0):
        def ring(w, d, z, c):
            points = [(-w/2+c,-d/2), (w/2-c,-d/2), (w/2,-d/2+c),
                      (w/2,d/2-c), (w/2-c,d/2), (-w/2+c,d/2),
                      (-w/2,d/2-c), (-w/2,-d/2+c)]
            return [(px+x,py+y,z) for px,py in points]
        self.rings([ring(width-2*bevel,depth-2*bevel,z0,bevel),
                    ring(width,depth,z0+bevel,bevel),
                    ring(width,depth,z1-bevel,bevel),
                    ring(width-2*bevel,depth-2*bevel,z1,bevel)])

    def torus(self, center, radius, tube_radius, n=32, rows=6):
        vertices=[]
        for i in range(n):
            a=math.tau*i/n
            for j in range(rows):
                b=math.tau*j/rows
                r=radius+tube_radius*math.cos(b)
                vertices.append((center[0]+r*math.cos(a),center[1]+r*math.sin(a),
                                 center[2]+tube_radius*math.sin(b)))
        faces=[]
        for i in range(n):
            for j in range(rows):
                faces.append((i*rows+j,((i+1)%n)*rows+j,
                              ((i+1)%n)*rows+(j+1)%rows,i*rows+(j+1)%rows))
        self.part(vertices,faces)


def fountain():
    mesh = Mesh()
    # A squat stepped footing and hollow monolithic bowl. Its inside is
    # physically modelled to the floor below the independent water mesh.
    mesh.lathe([(.73,0),(.77,.025),(.77,.065),(.735,.095),(.72,.15),
                (.72,.24),(.745,.34),(.775,.42),(.8,.47),(.8,.53),
                (.785,.56),(.735,.56),(.715,.535),(.707,.485),
                (.66,.415),(.585,.29),(.18,.26),(.006,.26)],32)
    # Short centre pedestal and a small physical horizontal outlet.
    mesh.lathe([(.006,.255),(.14,.255),(.155,.29),(.13,.325),(.09,.35),
                (.075,.56),(.095,.605),(.10,.655),(.082,.685),(.065,.71),
                (.006,.73)],20)
    mesh.tube([(0,.055,.672),(0,.09,.69),(0,.14,.686)], [.032,.028,.021],12)
    return mesh


def water():
    mesh = Mesh()
    mesh.lathe([(.005,.447),(.681,.447),(.684,.453),(.678,.459),(.005,.459)],48)
    return mesh


def stream():
    mesh = Mesh()
    # A very short gravity-driven arc, not a jet or monumental cascade.
    points = [(0,.137+.19*t,.686+.075*t-.298*t*t) for t in [i/14 for i in range(15)]]
    mesh.tube(points,[.0105-.003*i/14 for i in range(15)],8)
    # Two small raised ripple lips, physically attached to the pool height.
    for radius in (.045,.098):
        mesh.torus((0,.327,.4595),radius,.0018)
    return mesh


def pot(height, radius):
    mesh = Mesh()
    # Stable sole, tapered body, rounded rolled rim and an actual hollow
    # interior with a thick base; no soil in the portable medium pot.
    profile = [(.58*radius,0),(.69*radius,.018*height),(.71*radius,.075*height),
               (.91*radius,.82*height),(.98*radius,.83*height),
               (radius,.90*height),(radius,.965*height),(.97*radius,height),
               (.85*radius,height),(.82*radius,.96*height),
               (.82*radius,.87*height),(.78*radius,.82*height),
               (.57*radius,.13*height),(.005,.13*height)]
    mesh.lathe(profile,24)
    # Incised throwing rings arise from the outer profile, not decals or
    # randomized damage; the clean lip must remain safe for a visible grip.
    return mesh


def mary():
    mesh = Mesh()
    # Restrained octagonal stepped plinth, no plaque and no object text.
    mesh.dressed_slab(.58,.51,0,.085,.018)
    mesh.dressed_slab(.49,.425,.085,.13,.012)
    mesh.dressed_slab(.405,.345,.13,.355,.012)
    mesh.dressed_slab(.465,.405,.355,.42,.015)
    # A single continuous robe: the folds fan at the hem and draw together
    # under the folded hands rather than reading as stacked horizontal discs.
    rings=[]
    rows=[(.43,.205,.15,.024),(.46,.214,.154,.027),(.56,.204,.15,.029),
          (.70,.18,.137,.025),(.86,.145,.115,.021),(.99,.115,.092,.014),
          (1.10,.131,.10,.011),(1.22,.159,.104,.009),
          (1.30,.145,.093,.005),(1.345,.074,.061,.002)]
    for z, rx, ry, amplitude in rows:
        ring=[]
        for i in range(40):
            angle=math.tau*i/40
            fold=amplitude*(.70*math.sin(7*angle+.22*z)+.30*math.sin(11*angle-.5*z))
            ring.append(((rx+fold)*math.cos(angle),
                         (ry+fold)*math.sin(angle),z))
        rings.append(ring)
    mesh.rings(rings)
    # Neck and oval head bowed subtly forward; low-relief facial features
    # stay in the same stone and are legible only at ordinary close range.
    mesh.ellipsoid((0,.012,1.356),(.048,.048,.075),12,8)
    mesh.ellipsoid((0,.032,1.487),(.079,.072,.115),20,14)
    mesh.ellipsoid((0,.087,1.448),(.048,.028,.040),14,8)
    mesh.tube([(0,.098,1.505),(0,.122,1.473),(0,.124,1.465)], [.013,.014,.009],8)
    for side in (-1,1):
        mesh.tube([(side*.012,.100,1.508),(side*.029,.098,1.511),
                   (side*.045,.092,1.506)], [.005,.005,.003],7)
        mesh.tube([(side*.013,.106,1.493),(side*.029,.105,1.490),
                   (side*.043,.097,1.490)], [.003,.003,.002],6)
    mesh.ellipsoid((0,.111,1.437),(.025,.007,.006),12,6)
    # A thick U-shaped mantle wraps the head and descends down the back.
    # It has an open front, curved edges and an actual inner face.
    mantle_rows=[(.48,.218,.153,.045),(.66,.207,.148,.044),
                 (.91,.167,.116,.03),(1.10,.171,.114,.018),
                 (1.27,.192,.127,.010),(1.34,.159,.119,.006),
                 (1.44,.122,.110,.003),(1.54,.111,.107,.002),
                 (1.61,.085,.082,.001),(1.65,.024,.040,0)]
    n=28
    vertices=[]
    for inner in (False,True):
        for z,rx,ry,amp in mantle_rows:
            for i in range(n+1):
                a=math.pi/2+.68+(math.tau-1.36)*i/n
                fold=amp*(.7*math.cos(6*a+.65*z)+.3*math.sin(9*a))
                inset=.018 if inner else 0
                vertices.append(((rx+fold-inset)*math.cos(a),
                                 (ry+fold-inset)*math.sin(a)-.018,z))
    row_width=n+1
    half=len(mantle_rows)*row_width
    faces=[]
    for side in (0,1):
        offset=side*half
        for j in range(len(mantle_rows)-1):
            for i in range(n):
                a=offset+j*row_width+i
                face=(a,a+1,a+row_width+1,a+row_width)
                faces.append(face if side==0 else tuple(reversed(face)))
    for j in range(len(mantle_rows)-1):
        for i in (0,n):
            a=j*row_width+i
            faces.append((a,a+row_width,a+row_width+half,a+half))
    for j in (0,len(mantle_rows)-1):
        for i in range(n):
            a=j*row_width+i
            faces.append((a,a+half,a+half+1,a+1))
    mesh.part(vertices,faces)
    # The hood has a covered crown above the face, not a hole through its top.
    mesh.ellipsoid((0,-.019,1.607),(.055,.058,.042),14,8)
    # Cloth sleeves converge toward prayer hands; neither arm floats free
    # from the torso. Small fingers remain a single tactile stone assembly.
    for side in (-1,1):
        mesh.tube([(side*.139,.015,1.264),(side*.191,.059,1.128),
                   (side*.146,.118,1.100),(side*.047,.171,1.197)],
                  [.069,.065,.055,.032],12)
        mesh.tube([(side*.036,.18,1.187),(side*.021,.196,1.227),
                   (side*.013,.197,1.282)], [.027,.023,.014],10)
        for digit in range(3):
            mesh.tube([(side*(.012+.008*digit),.213,1.233),
                       (side*(.010+.007*digit),.211,1.279-.007*digit)],
                      [.0055,.0045],6)
    # Bare toe ends emerge beneath the robe on the plinth.
    mesh.ellipsoid((-.075,.114,.449),(.045,.080,.027),12,7)
    mesh.ellipsoid((.075,.106,.449),(.044,.076,.027),12,7)
    return mesh


def ledge():
    mesh=Mesh()
    mesh.dressed_slab(1.15,.52,.545,.65,.022)
    for x in (-.39,.39):
        mesh.dressed_slab(.245,.43,0,.07,.012,x=x)
        mesh.dressed_slab(.19,.365,.07,.525,.016,x=x)
        mesh.dressed_slab(.23,.41,.525,.55,.006,x=x)
    return mesh


def hedge():
    """One connected clipped shrub envelope with three quiet growth lobes.

    Lofted rounded rectangular sections keep a trimmed hedge silhouette while
    bowed ends, uneven shoulders and small leaf-scale offsets avoid box walls.
    Place at about 1.8 m pitch when a continuously planted run is wanted.
    """
    mesh=Mesh(); rings=[]
    def signed_power(value,power):
        return math.copysign(abs(value)**power,value)
    for j in range(29):
        x=-1+2*j/28
        end=.025+.975*math.sqrt(max(0,1-abs(x)**8))
        growth=.947+.053*math.cos(3*math.pi*x)
        ring=[]
        for i in range(24):
            angle=math.tau*i/24
            leaf=.008*math.sin(17*x+3*angle)+.006*math.cos(29*x-5*angle)
            depth=.425*end*growth
            height=.393*end*growth
            y=depth*signed_power(math.cos(angle),.56)
            z=.393+height*signed_power(math.sin(angle),.58)
            y+=leaf*math.cos(angle)*end
            z+=leaf*math.sin(angle)*end
            ring.append((x,y,z))
        rings.append(ring)
    vertices=[point for ring in rings for point in ring]
    lo=[min(p[i] for p in vertices) for i in range(3)]
    hi=[max(p[i] for p in vertices) for i in range(3)]
    # Exact fixed metre dimensions, even after the small organic offsets.
    for ring in rings:
        for i,p in enumerate(ring):
            ring[i]=(-1+2*(p[0]-lo[0])/(hi[0]-lo[0]),
                     -.425+.85*(p[1]-lo[1])/(hi[1]-lo[1]),
                     .8*(p[2]-lo[2])/(hi[2]-lo[2]))
    mesh.rings(rings)
    return mesh


def uplight():
    mesh=Mesh()
    # A small mounting shoe and short stem, without an imported Light.
    mesh.lathe([(.036,0),(.055,.007),(.055,.020),(.04,.027),(.006,.027)],16)
    mesh.tube([(0,0,.024),(0,0,.067)], [.015,.015],10)
    for side in (-1,1):
        mesh.tube([(side*.045,0,.045),(side*.060,.004,.099)], [.011,.011],8)
    rotation=Vector((0,0,1)).rotation_difference(UPLIGHT_DIRECTION).to_matrix()
    def tilted_profile(profile,slot):
        part=Mesh(); part.lathe(profile,24)
        mesh.active_material=slot
        mesh.part([tuple(UPLIGHT_CENTRE+rotation@Vector(v)) for v in part.vertices],part.faces)
    # Recessed glass sits inside a dark weather hood. The opening points
    # 35 degrees above the ground so only roots and lower foliage catch it.
    tilted_profile([(.071,-.062),(.083,-.052),(.09,-.039),(.09,.045),
                    (.087,.055),(.071,.055),(.069,.040),(.065,.038),
                    (.001,.038)],0)
    tilted_profile([(.002,.039),(.064,.039),(.066,.0405),(.064,.042),
                    (.002,.042)],1)
    return mesh


BUILDERS={"FountainStone":(fountain,"Stone"),"FountainWater":(water,"Water"),
          "FountainStream":(stream,"Stream"),"MaryStatue":(mary,"Statue"),
          "PotSmall":(lambda:pot(.23,.12),"Terracotta"),
          "PotMedium":(lambda:pot(.32,.17),"Terracotta"),
          "PotLarge":(lambda:pot(.44,.24),"Terracotta"),
          "StonePottingLedge":(ledge,"Stone"),
          "HedgeSegment":(hedge,"Foliage"),
          "GardenUplight":(uplight,("Metal","Lens"))}


def build_textures():
    """Neutral material grain, with no masonry joints on carved sculpture.

    These small deterministic sheets use the church's old-stone/earth palette
    through shared material tint. Wheel striations give clay its own close read.
    """
    TEXTURES.mkdir(parents=True,exist_ok=True)
    for name,clay in (("GardenStoneAlbedo",False),("GardenTerracottaAlbedo",True)):
        image=bpy.data.images.new(name,width=256,height=256,alpha=False)
        pixels=[]
        for y in range(256):
            for x in range(256):
                h=((x*374761393+y*668265263+9719)^((x+y*31)*1274126177))&0xffffffff
                h=((h^(h>>13))*1274126177)&0xffffffff
                noise=((h^(h>>16))&65535)/65535
                broad=.5+.5*math.sin(math.tau*x/128)*math.sin(math.tau*y/256)
                value=.78+.105*noise+.045*broad
                if clay:
                    value+=.015*math.sin(y*math.tau/8)+.01*math.sin(y*math.tau/32)
                elif noise<.012:
                    value-=.10
                pixels.extend((value,value,value,1))
        image.pixels.foreach_set(pixels)
        image.filepath_raw=str(TEXTURES/(name+".png")); image.file_format="PNG"; image.save()
    image=bpy.data.images.new("GardenFoliageAlbedo",width=256,height=256,alpha=False)
    values=[]
    for y in range(256):
        for x in range(256):
            h=((x*374761393+y*668265263+3911)*1274126177)&0xffffffff
            broad=math.sin(math.tau*x/128+.4)*math.sin(math.tau*y/128-.7)
            values.append(.40+.055*broad+.065*((h>>11)%127)/127)
    rng=random.Random(781309)
    # Overlapping leaves wrap across the tile seam. Unaligned positions,
    # sizes and directions prevent a grid or textile read on clipped faces.
    for _ in range(1650):
        cx,cy=rng.uniform(0,256),rng.uniform(0,256)
        rx,ry=rng.uniform(1.8,4.7),rng.uniform(.9,2.1)
        angle=rng.uniform(0,math.tau)
        c,s=math.cos(angle),math.sin(angle)
        tone=rng.uniform(.49,.83)
        reach=math.ceil(rx+ry)
        for py in range(math.floor(cy)-reach,math.ceil(cy)+reach+1):
            for px in range(math.floor(cx)-reach,math.ceil(cx)+reach+1):
                dx,dy=px-cx,py-cy
                u,v=(dx*c+dy*s)/rx,(-dx*s+dy*c)/ry
                distance=u*u+v*v
                if distance>=1: continue
                index=(py%256)*256+px%256
                value=tone+.035*(1-abs(v))-.03*u
                blend=min(1,(1-distance)*3)
                values[index]=values[index]*(1-blend)+value*blend
    pixels=[]
    for value in values: pixels.extend((value,value,value,1))
    image.pixels.foreach_set(pixels)
    image.filepath_raw=str(TEXTURES/"GardenFoliageAlbedo.png"); image.file_format="PNG"; image.save()


def material(role):
    mat=bpy.data.materials.get("MAT_Garden"+role)
    if mat: return mat
    mat=bpy.data.materials.new("MAT_Garden"+role)
    mat.diffuse_color=COLORS[role]
    mat.use_nodes=True
    shader=mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value=COLORS[role]
    shader.inputs["Roughness"].default_value=.29 if role in ("Water","Stream") else .84
    if role=="Lens":
        shader.inputs["Emission Color"].default_value=COLORS[role]
        shader.inputs["Emission Strength"].default_value=.22
    if role in ("Stone","Statue","Terracotta","Foliage"):
        filename=("GardenFoliageAlbedo.png" if role=="Foliage" else
                  "GardenTerracottaAlbedo.png" if role=="Terracotta" else "GardenStoneAlbedo.png")
        path=TEXTURES/filename
        tex=mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image=bpy.data.images.load(str(path),check_existing=True)
        multiply=mat.node_tree.nodes.new("ShaderNodeMixRGB")
        multiply.blend_type="MULTIPLY"
        multiply.inputs[0].default_value=1.0
        multiply.inputs[1].default_value=COLORS[role]
        mat.node_tree.links.new(tex.outputs["Color"],multiply.inputs[2])
        mat.node_tree.links.new(multiply.outputs[0],shader.inputs["Base Color"])
    return mat


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system="METRIC"
    bpy.context.scene.unit_settings.scale_length=1.0
    build_textures()
    collection=bpy.data.collections.new("SOURCE_ChurchGarden3D")
    bpy.context.scene.collection.children.link(collection)
    root=bpy.data.objects.new("SRC_ChurchGarden3D",None)
    collection.objects.link(root)
    objects=[]
    for kind in KINDS:
        builder,role=BUILDERS[kind]
        source=builder()
        mesh=bpy.data.meshes.new("GEO_"+kind)
        mesh.from_pydata(source.vertices,[],source.faces)
        if mesh.validate(): raise RuntimeError(kind+" contains invalid topology")
        for material_role in role if isinstance(role,tuple) else (role,):
            mesh.materials.append(material(material_role))
        for polygon,index in zip(mesh.polygons,source.material_indices):
            polygon.material_index=index
        mesh.update()
        bm=bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(mesh); bm.free()
        mesh.update()
        uv=mesh.uv_layers.new(name="GardenMetreUv")
        for face in mesh.polygons:
            axis=max(range(3),key=lambda i:abs(face.normal[i]))
            axes=[i for i in range(3) if i!=axis]
            for index in face.loop_indices:
                point=mesh.vertices[mesh.loops[index].vertex_index].co
                uv.data[index].uv=(point[axes[0]]*1.9,point[axes[1]]*1.9)
            face.use_smooth=False
        obj=bpy.data.objects.new("GEO_"+kind,mesh)
        collection.objects.link(obj)
        obj.parent=root
        objects.append(obj)
    return root,objects


def bounds(obj):
    vertices=[v.co for v in obj.data.vertices]
    lo=[min(v[i] for v in vertices) for i in range(3)]
    hi=[max(v[i] for v in vertices) for i in range(3)]
    return [round(lo[i],6) for i in (0,2,1)], [round(hi[i],6) for i in (0,2,1)]


def signature(objects):
    data=[]
    for obj in objects:
        data.append([obj.name,[[round(c,7) for c in v.co] for v in obj.data.vertices],
                     [list(f.vertices) for f in obj.data.polygons],
                     [[round(c,7) for c in v.uv] for v in obj.data.uv_layers.active.data],
                     [f.material_index for f in obj.data.polygons]])
    textures={p.name:hashlib.sha256(p.read_bytes()).hexdigest()
              for p in sorted(TEXTURES.glob("*.png"))}
    return hashlib.sha256((VERSION+json.dumps([data,COLORS,textures],separators=(",",":"))).encode()).hexdigest()


def geometry_signature(obj):
    data=[[[round(c,7) for c in v.co] for v in obj.data.vertices],
          [list(f.vertices) for f in obj.data.polygons],
          [[round(c,7) for c in v.uv] for v in obj.data.uv_layers.active.data]]
    return hashlib.sha256(json.dumps(data,separators=(",",":")).encode()).hexdigest()


def validate(objects):
    expected={"FountainStone":(1.6,.73),"MaryStatue":(.58,1.65),
              "PotSmall":(.24,.23),"PotMedium":(.34,.32),"PotLarge":(.48,.44),
              "StonePottingLedge":(1.15,.65),"HedgeSegment":(2.0,.8)}
    total=0
    for kind,obj in zip(KINDS,objects):
        lo,hi=bounds(obj)
        triangles=sum(len(f.vertices)-2 for f in obj.data.polygons)
        total+=triangles
        if obj.name in LEGACY_MESH_SIGNATURES:
            assert geometry_signature(obj)==LEGACY_MESH_SIGNATURES[obj.name],(kind,"legacy geometry changed")
        if kind=="HedgeSegment":
            assert abs(hi[2]-lo[2]-.85)<.00001,(kind,"depth")
        if kind=="GardenUplight":
            assert hi[1]<=.22 and abs(lo[1])<.00001,(kind,"fixture height")
            assert .18-.00001<=hi[0]-lo[0]<=.24,(kind,"fixture width")
            assert {f.material_index for f in obj.data.polygons}=={0,1},(kind,"housing/lens slots")
        if kind in expected:
            width,height=expected[kind]
            assert abs((hi[0]-lo[0])-width)<.001,(kind,"width",lo,hi)
            assert abs(hi[1]-height)<.001,(kind,"height",lo,hi)
            assert abs(lo[1])<.00001,(kind,"ungrounded",lo)
        bm=bmesh.new(); bm.from_mesh(obj.data)
        assert all(edge.is_manifold for edge in bm.edges),(kind,"non-manifold edge")
        unseen=set(bm.verts)
        while unseen:
            frontier=[unseen.pop()]; component=set(frontier)
            while frontier:
                for edge in frontier.pop().link_edges:
                    for v in edge.verts:
                        if v in unseen:
                            unseen.remove(v); component.add(v); frontier.append(v)
            faces={f for v in component for f in v.link_faces}
            volume=0
            for f in faces:
                verts=list(f.verts); a=verts[0].co
                for j in range(1,len(verts)-1):
                    volume+=a.dot(verts[j].co.cross(verts[j+1].co))/6
            assert volume>1e-10,(kind,"inward solid",volume)
        bm.free()
    assert total<16000,total
    print(f"CHURCH GARDEN VALIDATOR OK: {len(objects)} assets, {total} triangles",flush=True)
    return total


def preview(objects,path,extension=False):
    # The source stays untouched at its true origin; only review copies move.
    collection=bpy.data.collections.new("PRESENTATION_ChurchGardenExtension" if extension else
                                        "PRESENTATION_ChurchGarden3D")
    bpy.context.scene.collection.children.link(collection)
    placements={"FountainStone":(-1.0,.20,0),"FountainWater":(-1.0,.20,0),
                "FountainStream":(-1.0,.20,0),"MaryStatue":(.72,.20,0),
                "PotSmall":(1.58,.28,.65),"PotMedium":(1.96,.28,.65),
                "PotLarge":(2.67,.12,0),"StonePottingLedge":(1.82,.28,0),
                "HedgeSegment":(-3.35,.2,0),"GardenUplight":(-3.35,.88,0)}
    if extension:
        for prior in bpy.data.collections:
            if prior.name=="PRESENTATION_ChurchGarden3D": prior.hide_render=True
        placements={"HedgeSegment":(0,-.17,0),"GardenUplight":(0,.58,0)}
    for kind,obj in zip(KINDS,objects):
        obj.hide_render=True
        if kind not in placements: continue
        copy=obj.copy(); copy.data=obj.data; copy.parent=None
        collection.objects.link(copy); copy.location=placements[kind]; copy.hide_render=False
    if not extension:
        bpy.ops.mesh.primitive_plane_add(size=200)
        floor=bpy.context.object; floor.name="ReviewFloor"
        floor.data.materials.append(material("Stone"))
    bpy.ops.object.camera_add(location=(2.6,4.5,1.5) if extension else (4.35,7.45,3.20))
    camera=bpy.context.object
    target=Vector((0,.03,.34) if extension else (-.5,.15,.72))
    camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.type="ORTHO"; camera.data.ortho_scale=2.8 if extension else 8.4
    scene=bpy.context.scene; scene.camera=camera
    scene.world=bpy.data.worlds.new("GardenReviewWorld"); scene.world.use_nodes=True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value=(.22,.24,.235,1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value=.6
    for location,power,size in ([] if extension else [((1,3,5),560,4),((-3,-1,3),250,3)]):
        bpy.ops.object.light_add(type="AREA",location=location)
        light=bpy.context.object; light.data.energy=power; light.data.shape="DISK"; light.data.size=size
        light.rotation_euler=(Vector((.4,.1,.5))-light.location).to_track_quat("-Z","Y").to_euler()
    scene.render.engine="CYCLES"; scene.cycles.samples=28
    scene.cycles.use_denoising=True
    scene.render.resolution_x=1600; scene.render.resolution_y=860
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"
    scene.view_settings.view_transform="AgX"
    scene.render.filepath=str(path)
    print("Rendering garden kit review",flush=True)
    bpy.ops.render.render(write_still=True)
    # Preserve the review camera exactly; source meshes remain available in
    # their own collection and are excluded only from this review view.
    for obj in objects: obj.hide_set(True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--no-preview",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    ASSET.mkdir(parents=True,exist_ok=True); SOURCE.mkdir(parents=True,exist_ok=True)
    root,objects=build(); triangles=validate(objects); sig=signature(objects)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(ASSET/"ChurchGarden3D.fbx"),use_selection=True,
                             object_types={"EMPTY","MESH"},axis_forward="-Z",axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL",bake_space_transform=True,
                             add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=True,
                             mesh_smooth_type="FACE")
    manifest={"generator":"tools/build-church-garden-3d-model.py","generator_version":VERSION,
              "design_id":DESIGN_ID,"build_signature":sig,"triangle_count":triangles,
              "coordinate_contract":"Unity x,y,z = Blender x,z,y; fixed metre bare meshes",
              "textures":[{"path":str(p.relative_to(ROOT)).replace("\\","/"),
                           "sha256":hashlib.sha256(p.read_bytes()).hexdigest()}
                          for p in sorted(TEXTURES.glob("*.png"))],
              "pieces":[{"kind":kind,"mesh":obj.data.name,
                          "material_role":(BUILDERS[kind][1][0] if isinstance(BUILDERS[kind][1],tuple)
                                           else BUILDERS[kind][1]),
                          "material_roles":(list(BUILDERS[kind][1]) if isinstance(BUILDERS[kind][1],tuple)
                                            else [BUILDERS[kind][1]]),
                          "geometry_signature":geometry_signature(obj),
                          "bounds_min":bounds(obj)[0],"bounds_max":bounds(obj)[1],
                          "triangle_count":sum(len(f.vertices)-2 for f in obj.data.polygons)}
                         for kind,obj in zip(KINDS,objects)],
              "pot_medium":{"outer_radius_m":.17,"height_m":.32,"rim_inner_radius_m":.1394,
                            "empty":True},
              "fountain":{"water_height_m":.459,"stream_outlet":[0,.686,.137],
                          "stream_landing":[0,.463,.327]},
              "statue":{"figure_height_m":1.23,"pedestal_height_m":.42,"faces":"+Z",
                         "identity":"veiled Virgin Mary with folded prayer hands; passive stone"},
              "hedge":{"length_m":2.0,"depth_m":.85,"height_m":.8,"suggested_continuous_pitch_m":1.8},
              "uplight":{"lens_position":[round(UPLIGHT_LENS[i],8) for i in (0,2,1)],
                           "lens_direction":[round(UPLIGHT_DIRECTION[i],8) for i in (0,2,1)],
                           "lens_material_index":1,"elevation_degrees":35,"imports_light":False}}
    (ASSET/"ChurchGarden3D.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    if not args.no_preview:
        preview(objects,SOURCE/"ChurchGarden3DContactSheet.png")
        preview(objects,SOURCE/"ChurchGardenHedgeUplightContactSheet.png",True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"ChurchGarden3D.blend"),check_existing=False)
    _,repeated=build(); validate(repeated)
    assert signature(repeated)==sig,"Garden generation is not deterministic"
    print("CHURCH GARDEN ART BUILD OK: determinism match "+sig,flush=True)


if __name__=="__main__":
    main()
