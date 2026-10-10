"""Source-aware, deterministic CombatTest body partitions and visible anatomy.

Original FBXs remain read-only. Exterior triangles are clipped offline with
interpolated UVs and skin; flesh is a finite closed shell of the actual bare
surface. Renderable ribs, vertebrae, pelvis and paired limb bones are authored
here and bound to the existing production rig, never animation-stick proxies.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import zlib

import bpy
from mathutils import Vector
from mathutils.geometry import closest_point_on_tri

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {"Hero": "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx",
           "Npc": "Assets/Resources/VillageLife/StationWorker.fbx"}
NAMES = ("Head", "Neck", "Chest", "Abdomen", "Pelvis", "LeftUpperArm",
         "LeftForearm", "LeftHand", "RightUpperArm", "RightForearm", "RightHand",
         "LeftThigh", "LeftShin", "LeftFoot", "RightThigh", "RightShin", "RightFoot")
REGION_BONES = ("head", "neck", "chest", "spine", "pelvis", "upper_arm.L",
                "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R",
                "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R")
EPS = 1e-8
TORSO_REGIONS = (2, 3, 4)
TORSO_CELL_EDGE_M = .045


def radial_target(point, basis, scale):
    axis_point = basis["origin"] + basis["axis"]*(point-basis["origin"]).dot(basis["axis"])
    return axis_point+(point-axis_point)*scale


def tessellate(poly):
    """Split the longest edge in world metres, retaining source UV and skin.

    An edge is divided only while longer than the common limit, so shared
    boundaries acquire the same dyadic samples. Quarter clipping happens AFTER
    this subdivision; it cannot introduce a different sampling of either rim.
    """
    pending = [(poly[0], poly[i], poly[i+1]) for i in range(1, len(poly)-1)]
    while pending:
        tri = pending.pop()
        lengths = [(tri[(i+1)%3][0]-tri[i][0]).length for i in range(3)]
        edge = max(range(3), key=lambda i: (lengths[i], -i))
        if lengths[edge] <= TORSO_CELL_EDGE_M+1e-9:
            if (tri[1][0]-tri[0][0]).cross(tri[2][0]-tri[0][0]).length > 1e-12:
                yield tri
            continue
        a, b, c = tri[edge], tri[(edge+1)%3], tri[(edge+2)%3]
        midpoint = interpolate(a, b, .5)
        pending.extend(((a, midpoint, c), (midpoint, b, c)))


def region_patches(poly, basis, region):
    for part in tessellate(poly) if region in TORSO_REGIONS else (poly,):
        yield from patches(part, basis)


def cell_uv_layers(obj, rows):
    """FBX carries eight Vector2 UV channels without transforming their values.

    UV1=(cell id, outer flag); UV2/3 are reserved source correspondence.
    UV4/5=(inner X,Y)/(inner Z,0), UV6/7=(surface-centre X,Y)/(Z,0).
    Coordinates are mesh-local metres, axes swapped to Unity. Importer measures
    a separate affine frame for each source renderer from exterior cell centres
    and flesh inner vertices, maps directly to restored source-local geometry,
    then packs XYZ into Vector4 UV4 and UV6 matching imported mesh.vertices.
    Cell IDs are local to a renderer, integer-valued and contiguous from zero.
    Flesh centres always sample ORIGINAL bare skin, never its recessed layer.
    """
    inverse = obj.matrix_world.inverted()
    scale = obj.matrix_world.to_scale()
    if max(scale)-min(scale) > 1e-6:
        raise RuntimeError("Torso metadata requires uniform authored scale "+obj.name)
    def encoded(point):
        local = (inverse @ point)*scale.x
        return (local.x, local.z, local.y)
    values = []
    for cell, outer, inner, centre in rows:
        x,y,z = encoded(inner); a,b,c = encoded(centre)
        values.append(((cell, outer), (0,0), (0,0), (x,y), (z,0), (a,b), (c,0)))
    for channel, name in enumerate(("TorsoCell", "SourceIndices", "SourceBarycentric",
                                    "TorsoInnerXY", "TorsoInnerZ", "TorsoCentreXY", "TorsoCentreZ")):
        layer = obj.data.uv_layers.new(name=name)
        for loop in obj.data.loops:
            layer.data[loop.index].uv = values[loop.vertex_index][channel]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def rnd(values):
    return [round(float(v), 7) for v in values]


def unity(point):
    return rnd((point.x, point.z, point.y))


def bounds(points):
    points = [unity(p) for p in points]
    low = [min(p[a] for p in points) for a in range(3)]
    high = [max(p[a] for p in points) for a in range(3)]
    return {"min": low, "max": high, "size": rnd(high[a]-low[a] for a in range(3))}


def branch(bone):
    if bone == "head" or bone.startswith(("face.", "Hair")):
        return 0
    if bone == "neck":
        return 1
    for side, arm, leg in (("L", 2, 4), ("R", 3, 5)):
        if bone in ("upper_arm."+side, "forearm."+side, "hand."+side, "clavicle."+side):
            return arm
        if bone in ("thigh."+side, "shin."+side, "foot."+side):
            return leg
    return 6


def interpolate(a, b, t):
    weights = {key: a[3].get(key, 0)*(1-t)+b[3].get(key, 0)*t for key in a[3].keys() | b[3].keys()}
    return (a[0].lerp(b[0], t), a[1].lerp(b[1], t), a[2].lerp(b[2], t), weights,
            [x*(1-t)+y*t for x, y in zip(a[4], b[4])])


def clip(poly, distance):
    if not poly:
        return []
    result = []
    previous, pd = poly[-1], distance(poly[-1])
    for current in poly:
        cd = distance(current)
        if (pd >= -EPS) != (cd >= -EPS):
            result.append(interpolate(previous, current, max(0, min(1, pd/(pd-cd)))))
        if cd >= -EPS:
            result.append(current)
        previous, pd = current, cd
    return result


def divide_plane(poly, distance):
    # Authored flat joint caps can differ from their rig plane by float noise.
    # Give a fully coplanar polygon one owner instead of duplicating a cap.
    if not poly:return [],[]
    if max(abs(distance(p)) for p in poly)<1e-6:return poly,[]
    return clip(poly,lambda p:-distance(p)),clip(poly,distance)


def frame(start, end):
    axis = (end-start).normalized()
    cross = Vector((1, 0, 0))-axis*axis.x
    if cross.length < .1:
        cross = Vector((0, 1, 0))-axis*axis.y
    cross.normalize()
    second = axis.cross(cross).normalized()
    return {"origin": (start+end)*.5, "axis": axis, "cross": cross, "second": second,
            "start": start, "end": end}


def region_frames(arm):
    def head(name):
        return arm.matrix_world @ arm.data.bones[name].head_local
    def tail(name):
        return arm.matrix_world @ arm.data.bones[name].tail_local
    pelvis, spine, chest, neck = (head(n) for n in ("pelvis", "spine", "chest", "neck"))
    # Shared geometric planes cut clothing and bare surfaces at the same height.
    low = (pelvis.z+spine.z)*.5
    high = (spine.z+chest.z)*.5
    frames = {1: frame(neck, head("head")),
              2: frame(Vector((0, chest.y, high)), neck),
              3: frame(Vector((0, spine.y, low)), Vector((0, spine.y, high))),
              4: frame(Vector((0, pelvis.y, pelvis.z-.11)), Vector((0, pelvis.y, low)))}
    for region in range(5, 17):
        name = REGION_BONES[region]
        frames[region] = frame(head(name), tail(name))
    return frames, low, high


def split_regions(poly, arm, low, high):
    result = []
    for group in range(1, 7):
        part = poly
        for other in range(7):
            if group != other:
                # A shared joint cap can be equally weighted to two branches
                # over its entire area. Give that face one deterministic owner;
                # clipping both sides would duplicate its exterior coverage.
                if part and max(abs(p[4][group]-p[4][other]) for p in part) <= EPS:
                    if group > other:
                        part = []
                        break
                    continue
                part = clip(part, lambda p, g=group, o=other: p[4][g]-p[4][o])
        if len(part) < 3:
            continue
        if group == 1:
            result.append((1, part))
        elif group == 6:
            result.extend(((2, clip(part, lambda p: p[0].z-high)),
                           (3, clip(clip(part, lambda p: p[0].z-low), lambda p: high-p[0].z)),
                           (4, clip(part, lambda p: low-p[0].z))))
        else:
            side = "L" if group in (2, 4) else "R"
            arm_branch = group in (2, 3)
            start = 5 if group == 2 else 8 if group == 3 else 11 if group == 4 else 14
            joints = ("forearm.", "hand.") if arm_branch else ("shin.", "foot.")
            plane_bones = ("forearm.", "forearm.") if arm_branch else ("shin.", "shin.")
            points = [arm.matrix_world @ arm.data.bones[j+side].head_local for j in joints]
            axes = [(arm.matrix_world.to_3x3() @
                     (arm.data.bones[j+side].tail_local-arm.data.bones[j+side].head_local)).normalized()
                    for j in plane_bones]
            before,distal = divide_plane(part,lambda p:(p[0]-points[0]).dot(axes[0]))
            middle,after = divide_plane(distal,lambda p:(p[0]-points[1]).dot(axes[1]))
            result.extend(((start, before), (start+1, middle), (start+2, after)))
    return [(region, part) for region, part in result if len(part) >= 3]


def patches(poly, basis):
    result = []
    for patch in range(4):
        sx = -1 if patch & 1 else 1
        sy = -1 if patch & 2 else 1
        part = clip(poly, lambda p: sx*(p[0]-basis["origin"]).dot(basis["cross"]))
        part = clip(part, lambda p: sy*(p[0]-basis["origin"]).dot(basis["second"]))
        if len(part) >= 3:
            result.append((patch, part))
    return result


def build_mesh(name, source, polys, basis=None):
    inverse = source.matrix_world.inverted()
    verts, faces, uvs, skin, materials, lookup = [], [], [], [], [], {}
    metadata = []
    if basis is not None:
        # A triangle cell owns its vertices, including at coincident skin seams.
        # Removing one cell therefore cannot remove or move its neighbour's data.
        polys = [(tuple(poly[j] for j in (0,i,i+1)), material)
                 for poly,material in polys for i in range(1,len(poly)-1)
                 if (poly[i][0]-poly[0][0]).cross(poly[i+1][0]-poly[0][0]).length > 1e-12]
    cell = 0
    for poly, material in polys:
        vertex_start, face_start = len(verts), len(faces)
        added_keys = []
        centre = sum((point[0] for point in poly), Vector())/len(poly)
        face = []
        for point in poly:
            local = inverse @ point[0]
            key = (tuple(round(v, 9) for v in local), tuple(round(v, 9) for v in point[1]),
                   tuple(sorted((k, round(v, 7)) for k, v in point[3].items() if v > 1e-7)),
                   cell if basis is not None else None)
            if key not in lookup:
                lookup[key] = len(verts)
                added_keys.append(key)
                verts.append(local); uvs.append(point[1]); skin.append(point[3])
                if basis is not None:
                    metadata.append((cell, 1, radial_target(point[0], basis, .32), centre))
            face.append(lookup[key])
        for i in range(1, len(face)-1):
            tri = (face[0], face[i], face[i+1])
            if len(set(tri)) == 3 and (verts[tri[1]]-verts[tri[0]]).cross(verts[tri[2]]-verts[tri[0]]).length > 1e-14:
                faces.append(tri); materials.append(material)
        if basis is not None:
            # Clipped slivers can merge below the mesh's vertex precision.
            # Number only emitted cells; discard their unused vertex metadata
            # so every authored cell still owns an actual surface triangle.
            if len(faces) > face_start:
                cell += 1
            else:
                for key in added_keys: del lookup[key]
                del verts[vertex_start:]; del uvs[vertex_start:]
                del skin[vertex_start:]; del metadata[vertex_start:]
    if not faces:
        return None
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces); mesh.update()
    obj = bpy.data.objects.new(name, mesh); bpy.context.collection.objects.link(obj)
    obj.parent = source.parent; obj.matrix_world = source.matrix_world.copy()
    for material in source.data.materials:
        mesh.materials.append(material)
    for poly, material in zip(mesh.polygons, materials):
        poly.material_index = material
    layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        layer.data[loop.index].uv = uvs[loop.vertex_index]
    if basis is not None:
        cell_uv_layers(obj, metadata)
    for name in sorted({key for row in skin for key in row}):
        group = obj.vertex_groups.new(name=name)
        for i, row in enumerate(skin):
            if row.get(name, 0) > 1e-7:
                group.add([i], row[name], "REPLACE")
    for mod in source.modifiers:
        if mod.type == "ARMATURE":
            obj.modifiers.new("Skin", "ARMATURE").object = mod.object
    return obj


def flesh_cells(name, source, polys, basis):
    """Small closed authored prisms; runtime erodes them while they stay skinned.

    They are never loose objects or fragment templates. Each cell's first vertex
    is on the outer layer, so runtime can use its skin for its surface sample.
    """
    inverse = source.matrix_world.inverted()
    vertices, faces, uv, skin, rows = [], [], [], [], []
    cell = 0
    for poly, _ in polys:
        for i in range(1, len(poly)-1):
            tri = (poly[0],poly[i],poly[i+1])
            normal = (tri[1][0]-tri[0][0]).cross(tri[2][0]-tri[0][0])
            # Clipping a shared plane within EPS can leave numerical slivers.
            # Such sub-square-millimetre remnants cannot form a stable volume.
            longest=max((tri[(j+1)%3][0]-tri[j][0]).length for j in range(3))
            if normal.length <= 1e-9 or normal.length/max(1e-8,longest) < 1e-5:
                continue
            normal.normalize()
            centre = sum((p[0] for p in tri), Vector())/3
            outer = [radial_target(p[0],basis,.94) for p in tri]
            inner = [radial_target(p[0],basis,.32) for p in tri]
            topology = [(0,1,2),(5,4,3)]
            for quad in ((1,0,3,4),(2,1,4,5),(0,2,5,3)):
                # Radial recess can make the side nonplanar. Pick one diagonal
                # explicitly; reversing a quad would otherwise change BOTH its
                # winding and its triangulated volume. Neighbour cells use the
                # same diagonal by ordering the original endpoint coordinates.
                if tuple(outer[quad[0]]) > tuple(outer[quad[1]]):
                    quad = quad[1:]+quad[:1]
                topology.extend(((quad[0],quad[1],quad[2]),(quad[0],quad[2],quad[3])))
            # Horizontal source caps and axis vertices have no radial thickness.
            # Measure depth along the surface normal: radial travel in a flat
            # cap can acquire a spurious volume from float32 round-off.
            # Give those cells a 2mm inward floor instead of a degenerate prism.
            if abs(signed_volume(outer+inner,topology,stable=True)) <= 1e-13 or any(
                    abs((a-b).dot(normal)) <= 1e-6 for a,b in zip(outer,inner)):
                inner = [point-normal*.002 for point in inner]
            volume = signed_volume(outer+inner,topology,stable=True)
            if abs(volume) <= 1e-13:
                raise RuntimeError("Degenerate torso cell "+name+" "+str(cell))
            if volume < 0:
                topology = [tuple(reversed(face)) for face in topology]
            offset = len(vertices)
            for outer_flag, points in ((1,outer),(0,inner)):
                for corner, point in enumerate(points):
                    vertices.append(inverse@point);uv.append(tri[corner][1]);skin.append(tri[corner][3])
                    rows.append((cell,outer_flag,inner[corner],centre))
            faces.extend(tuple(offset+j for j in face) for face in topology)
            cell += 1
    if not faces:
        return None
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices,[],faces);mesh.update()
    obj = bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    obj.parent=source.parent;obj.matrix_world=source.matrix_world.copy()
    layer=mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        layer.data[loop.index].uv=uv[loop.vertex_index]
    cell_uv_layers(obj,rows)
    for bone in sorted({key for row in skin for key in row}):
        group=obj.vertex_groups.new(name=bone)
        for vertex,row in enumerate(skin):
            if row.get(bone,0)>1e-7:group.add([vertex],row[bone],"REPLACE")
    for mod in source.modifiers:
        if mod.type=="ARMATURE":obj.modifiers.new("Skin","ARMATURE").object=mod.object
    return obj


def simple_mesh(name, source, vertices, faces, bone):
    inverse = source.matrix_world.inverted()
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([inverse@p for p in vertices],[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    obj.parent=source.parent;obj.matrix_world=source.matrix_world.copy()
    group=obj.vertex_groups.new(name=bone);group.add(list(range(len(vertices))),1.,"REPLACE")
    layer=mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        point=vertices[loop.vertex_index];layer.data[loop.index].uv=(point.x*3+.5,point.z*3+.5)
    for mod in source.modifiers:
        if mod.type=="ARMATURE":obj.modifiers.new("Skin","ARMATURE").object=mod.object
    return obj


def signed_volume(vertices, faces, stable=False):
    if stable:
        # Float32 cross/dot at a 1m world offset can give a flat cap a spurious
        # signed volume. Translate to a vertex and calculate in Python float64.
        origin=vertices[faces[0][0]]
        points=[tuple(float(p[i])-float(origin[i]) for i in range(3)) for p in vertices]
        terms=[]
        for face in faces:
            for i in range(1,len(face)-1):
                a,b,c=(points[j] for j in (face[0],face[i],face[i+1]))
                terms.append((a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0]))/6)
        return math.fsum(terms)
    return sum(vertices[a].dot(vertices[b].cross(vertices[c]))/6
               for face in faces for a,b,c in [(face[0],face[i],face[i+1]) for i in range(1,len(face)-1)])


def flesh_shell(name, source, polys, basis, bone):
    vertices, faces, lookup = [], [], {}
    for poly, island in polys:
        face = []
        for point in poly:
            key = (island, tuple(round(v, 7) for v in point[0]))
            if key not in lookup:
                lookup[key] = len(vertices); vertices.append(point[0])
            face.append(lookup[key])
        for i in range(1, len(face)-1):
            tri = (face[0], face[i], face[i+1])
            if len(set(tri)) == 3 and (vertices[tri[1]]-vertices[tri[0]]).cross(vertices[tri[2]]-vertices[tri[0]]).length > 1e-10:
                faces.append(tri)
    if not faces:
        return None
    # A plane may pass through a shared vertex of two otherwise disconnected
    # skin islands (notably the articulated hand). Separate those vertex fans
    # before capping; joining both rims through one radial edge is nonmanifold.
    for vertex in range(len(vertices)):
        incident=[i for i,face in enumerate(faces) if vertex in face]
        pending=set(incident);components=[]
        while pending:
            group={pending.pop()};queue=list(group)
            while queue:
                index=queue.pop();neighbors=set(faces[index])-{vertex}
                joined={other for other in pending if neighbors.intersection(set(faces[other])-{vertex})}
                pending.difference_update(joined);group.update(joined);queue.extend(joined)
            components.append(group)
        for component in components[1:]:
            replacement=len(vertices);vertices.append(vertices[vertex].copy())
            for index in component:faces[index]=tuple(replacement if i==vertex else i for i in faces[index])
    outer, inner = [], []
    for point in vertices:
        axis_point = basis["origin"] + basis["axis"]*(point-basis["origin"]).dot(basis["axis"])
        delta = point-axis_point
        outer.append(axis_point+delta*.94)
        inner.append(axis_point+delta*.32)
    count = len(outer)
    result = list(faces)+[tuple(i+count for i in reversed(face)) for face in faces]
    edges = {}
    for face in faces:
        for a,b in zip(face, face[1:]+face[:1]):
            edges.setdefault(tuple(sorted((a,b))), []).append((a,b))
    for edge in edges.values():
        if len(edge) == 1:
            a,b = edge[0]; result.append((b,a,a+count,b+count))
        elif len(edge) > 2:
            raise RuntimeError("Nonmanifold bare-source damage shell: "+name)
    all_vertices = outer+inner
    if signed_volume(all_vertices, result) < 0:
        result = [tuple(reversed(face)) for face in result]
    return simple_mesh(name, source, all_vertices, result, bone)


class Anatomy:
    def __init__(self):
        self.vertices, self.faces = [], []

    def add(self, vertices, faces):
        offset = len(self.vertices)
        if signed_volume(vertices, faces) < 0:
            faces = [tuple(reversed(f)) for f in faces]
        self.vertices.extend(vertices)
        self.faces.extend(tuple(i+offset for i in f) for f in faces)

    def ball(self, centre, radius, sides=8, rings=5):
        rx,ry,rz = radius if isinstance(radius, tuple) else (radius,)*3
        vertices = [centre+Vector((rx*math.sin(math.pi*j/rings)*math.cos(math.tau*i/sides),
                                  ry*math.sin(math.pi*j/rings)*math.sin(math.tau*i/sides),
                                  rz*math.cos(math.pi*j/rings)))
                    for j in range(1,rings) for i in range(sides)]
        vertices.extend((centre+Vector((0,0,rz)),centre-Vector((0,0,rz))))
        top,bottom = len(vertices)-2,len(vertices)-1
        faces = [(top,i,(i+1)%sides) for i in range(sides)]
        for j in range(rings-2):
            for i in range(sides):
                a=j*sides+i;b=j*sides+(i+1)%sides
                faces.append((a,a+sides,b+sides,b))
        last=(rings-2)*sides
        faces.extend((bottom,last+(i+1)%sides,last+i) for i in range(sides))
        self.add(vertices,faces)

    def tube(self, points, radii, sides=8):
        vertices=[]
        for i,point in enumerate(points):
            axis = (points[min(i+1,len(points)-1)]-points[max(i-1,0)]).normalized()
            cross = axis.cross(Vector((0,1,0)))
            if cross.length < .1:
                cross=axis.cross(Vector((1,0,0)))
            cross.normalize();second=axis.cross(cross)
            radius=radii[i] if isinstance(radii,(list,tuple)) else radii
            for j in range(sides):
                angle=math.tau*j/sides
                vertices.append(point+radius*(cross*math.cos(angle)+second*math.sin(angle)))
        faces=[tuple(reversed(range(sides))),tuple(range(len(vertices)-sides,len(vertices)))]
        for row in range(len(points)-1):
            for j in range(sides):
                a=row*sides+j;b=row*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
        self.add(vertices,faces)

    def long_bone(self, a,b,radius):
        self.tube([a,a.lerp(b,.12),a.lerp(b,.47),a.lerp(b,.85),b],
                  [radius*1.7,radius*.95,radius*.78,radius,radius*1.7],10)
        self.ball(a,radius*1.8);self.ball(b,radius*1.65)


def bone_geometry(region, basis, region_bounds):
    art=Anatomy();a,b=basis["start"],basis["end"];axis=basis["axis"]
    length=(b-a).length;centre=basis["origin"]
    if region in (1,2,3):
        count=3 if region==1 else 7 if region==2 else 4
        # Distinct vertebral bodies, paired transverse processes, rear spinous process.
        for i in range(count):
            c=a.lerp(b,(i+.5)/count)+Vector((0,.025,0))
            radius=.016 if region==1 else .024
            art.ball(c,(radius,radius*.8,length/count*.38),10,5)
            art.tube([c-Vector((radius*1.6,0,0)),c+Vector((radius*1.6,0,0))],radius*.26)
            art.tube([c,c+Vector((0,.028,0))],radius*.3)
        if region==2:
            low,high=region_bounds
            half_width=min(.155,(high.x-low.x)*.38)
            depth=min(.081,(high.y-low.y)*.36)
            # Curved paired ribs: rear vertebral attachment -> sides -> sternum.
            for row in range(6):
                z=a.z+(b.z-a.z)*(.18+row*.118)
                size=.68+.30*math.sin((row+1)*math.pi/7)
                for sign in (-1,1):
                    points=[Vector((sign*half_width*size*math.sin(t),
                                    centre.y+depth*math.cos(t),z-.018*math.sin(t)))
                            for t in [math.pi*j/10 for j in range(11)]]
                    art.tube(points,.0055,7)
            art.tube([Vector((0,centre.y-depth,a.z+length*.16)),
                      Vector((0,centre.y-depth,b.z-length*.13))],.010,8)
    elif region==4:
        # Two flared iliac wings, acetabular cups, sacrum and closed pubic arch.
        for sign in (-1,1):
            pts=[centre+Vector((sign*x,y,z)) for x,y,z in
                 ((.035,.018,.055),(.085,.025,.065),(.122,.011,.045),(.116,-.026,.003),
                  (.078,-.046,-.035),(.032,-.044,-.025))]
            art.tube(pts,.012,8)
            art.tube([pts[0],pts[-1]],.011,8)
            art.ball(centre+Vector((sign*.083,-.012,-.017)),(.023,.019,.026),10,6)
        art.ball(centre+Vector((0,.025,.008)),(.029,.016,.047),10,6)
        art.tube([centre+Vector((-.068,-.043,-.025)),centre+Vector((0,-.059,-.044)),
                  centre+Vector((.068,-.043,-.025))],.009,8)
    elif region in (5,8,11,14):
        radius=.012 if region in (5,8) else .017
        art.long_bone(a.lerp(b,.055),a.lerp(b,.955),radius)
    elif region in (6,9,12,15):
        offset=basis["cross"]*(.013 if region in (6,9) else .014)
        art.long_bone(a.lerp(b,.045)-offset,b.lerp(a,.045)-offset,
                      .008 if region in (6,9) else .013)
        art.long_bone(a.lerp(b,.08)+offset,b.lerp(a,.08)+offset,.0065)
    else:
        # Carpal/tarsal cluster and separate metacarpal/metatarsal rays with phalanges.
        foot=region in (13,16);width=.035 if foot else .031
        art.ball(a.lerp(b,.16),(.022,.020,.019))
        for finger in range(5):
            spread=(finger-2)*width*.44
            start=a.lerp(b,.26)+basis["cross"]*spread
            reach=(.68 if finger in (0,4) else .91)*length
            end=a+axis*reach+basis["cross"]*spread*1.45
            middle=start.lerp(end,.55)
            art.long_bone(start,middle,.0048 if foot else .004)
            art.long_bone(middle,end,.0038 if foot else .003)
    return art.vertices,art.faces


def is_body_source(obj):
    return obj.name.startswith("GEO_") and not any(s in obj.name for s in
        ("Hair","Face","Head","Ear","Eye","Brow","Mouth"))


def head_family(obj):
    # Keep precisely the ownership predicate used by the old head derivative.
    # Mixed upper-neck/body meshes remain here, including their head weights.
    weighted={obj.vertex_groups[g.group].name for v in obj.data.vertices for g in v.groups if g.weight>1e-7}
    return bool(weighted) and all(n=="head" or n.startswith(("face.","HairBack.","HairLeft.","HairRight.")) for n in weighted)


def region_source(candidates, region):
    bone=REGION_BONES[region]
    matching=[(obj,area) for obj,area in candidates.values() if any(
        obj.vertex_groups[g.group].name==bone and g.weight>1e-7
        for v in obj.data.vertices for g in v.groups)]
    if not matching:
        raise RuntimeError("No production skin actually influenced by "+bone)
    # A large thigh renderer cannot supply foot skin just because it has more
    # polygons. Rank only matching bone influences by the emitted regional area.
    return sorted(matching,key=lambda item:(-item[1],item[0].name))[0][0]


def fit_terminal_frame(basis, points):
    # FBX control tails can extend far beyond the actual boot; use the authored
    # distal surface for anatomical length while keeping the same cut axes.
    reach=max((p-basis["start"]).dot(basis["axis"]) for p in points)
    result=frame(basis["start"],basis["start"]+basis["axis"]*max(.02,reach-.005))
    # The foot control axis is near the top of the boot; quadrants must pass
    # through the surface volume rather than leave an entire half empty.
    result["origin"]=Vector(tuple((min(p[a] for p in points)+max(p[a] for p in points))*.5 for a in range(3)))
    return result


def fit_terminal_frames(originals,arm,frames,low,high):
    points={region:[] for region in (7,10,13,16)}
    for source in originals:
        if not is_body_source(source) or head_family(source):continue
        source.data.calc_loop_triangles()
        for tri in source.data.loop_triangles:
            poly=[]
            for index in tri.vertices:
                vertex=source.data.vertices[index]
                weights={source.vertex_groups[g.group].name:g.weight for g in vertex.groups if g.weight>1e-7}
                scores=[0.]*7
                for bone,weight in weights.items():
                    group=branch(bone);scores[1 if group==0 else group]+=weight
                poly.append((source.matrix_world@vertex.co,Vector((0,0)),vertex.normal.copy(),weights,scores))
            for region,part in split_regions(poly,arm,low,high):
                if region in points:points[region].extend(p[0] for p in part)
    for region,values in points.items():
        if not values:raise RuntimeError("Missing terminal surface "+NAMES[region])
        frames[region]=fit_terminal_frame(frames[region],values)


def fit_bone_envelope(vertices, low, high):
    # Retain each closed anatomical component and its proportions per axis;
    # fit the complete hand/foot/pelvis inside the actual authored bare bounds.
    result=[p.copy() for p in vertices]
    for axis in range(3):
        minimum=min(p[axis] for p in vertices);maximum=max(p[axis] for p in vertices)
        margin=min(.002,(high[axis]-low[axis])*.04)
        lo,hi=low[axis]+margin,high[axis]-margin
        scale=min(1.,(hi-lo)/max(1e-8,maximum-minimum))
        shift=max(lo-minimum*scale,min(0.,hi-maximum*scale))
        for point in result:point[axis]=point[axis]*scale+shift
    if any(p[a]<low[a]-1e-6 or p[a]>high[a]+1e-6 for p in result for a in range(3)):
        raise RuntimeError("Terminal anatomy escaped its source bounds")
    return result


def source_polygon_warp(source):
    """A bound on alternate FBX tessellations of nonplanar source polygons.

    A tetrahedral quad can have two valid diagonals whose interiors differ even
    though every boundary vertex and UV is identical. Bound that difference by
    the largest vertex-to-other-corner-plane height, in world metres. This is
    source-specific; flat source polygons retain the usual submillimetre guard.
    """
    maximum=0.;warped=0
    for polygon in source.data.polygons:
        if len(polygon.vertices)<=3:continue
        points=[source.matrix_world@source.data.vertices[i].co for i in polygon.vertices]
        height=0.
        for i in range(len(points)):
            a,b,c=points[i-2],points[i-1],points[i]
            normal=(b-a).cross(c-a)
            if normal.length<1e-12:continue
            normal.normalize()
            height=max(height,max(abs((p-a).dot(normal)) for p in points))
        maximum=max(maximum,height)
        if height>1e-6:warped+=1
    return {"source":source.name,"maximum_nonplanarity_m":round(maximum,8),"nonplanar_polygon_count":warped}


def measure(obj, closed=False):
    mesh=obj.data;mesh.calc_loop_triangles()
    world=[obj.matrix_world@v.co for v in mesh.vertices]
    weights=[sorted((obj.vertex_groups[g.group].name,round(g.weight,7)) for g in v.groups if g.weight>1e-7)
             for v in mesh.vertices]
    if any(not row or abs(sum(w for _,w in row)-1)>.002 for row in weights):
        raise RuntimeError("Invalid body weights "+obj.name)
    if any(not math.isfinite(v) for p in world for v in p):
        raise RuntimeError("Nonfinite body geometry "+obj.name)
    volume=None
    if closed:
        counts={}
        for poly in mesh.polygons:
            for a,b in zip(poly.vertices, list(poly.vertices[1:])+[poly.vertices[0]]):
                edge=tuple(sorted((a,b)));counts[edge]=counts.get(edge,0)+1
        if any(n!=2 for n in counts.values()):
            bad=[(edge,n,[unity(world[i]) for i in edge]) for edge,n in counts.items() if n!=2]
            raise RuntimeError("Open anatomy/cut rim "+obj.name+" "+str(bad[:8]))
        volume=signed_volume(world,[tuple(p.vertices) for p in mesh.polygons])
        if volume<=1e-11:
            raise RuntimeError("Nonpositive anatomy volume "+obj.name+" "+str(volume))
    cells=None
    if len(mesh.uv_layers)==8:
        labels={}
        for loop in mesh.loops:
            value=mesh.uv_layers[1].data[loop.index].uv
            label=(round(value.x),round(value.y))
            if abs(value.x-label[0])>1e-5 or label[0]<0 or label[1] not in (0,1):
                raise RuntimeError("Invalid authored torso cell label "+obj.name)
            if loop.vertex_index in labels and labels[loop.vertex_index]!=label:
                raise RuntimeError("A torso vertex belongs to two cells "+obj.name)
            labels[loop.vertex_index]=label
        grouped={}
        for poly in mesh.polygons:
            ids={labels[vertex][0] for vertex in poly.vertices}
            if len(ids)!=1:raise RuntimeError("Triangle crosses torso cells "+obj.name)
            grouped.setdefault(ids.pop(),[]).append(tuple(poly.vertices))
        if set(grouped)!=set(range(len(grouped))):raise RuntimeError("Noncontiguous torso cells "+obj.name)
        maximum=0.;volumes=[]
        for cell,polygons in grouped.items():
            members=sorted({vertex for poly in polygons for vertex in poly})
            if labels[members[0]][1]!=1:raise RuntimeError("Torso cell starts at inner skin "+obj.name)
            for poly in polygons:
                for a,b in zip(poly,poly[1:]+poly[:1]):
                    if labels[a][1] and labels[b][1]:maximum=max(maximum,(world[a]-world[b]).length)
            if closed:
                # Only this cell is needed, avoiding an O(cell_count*vertices)
                # conversion while retaining the precise translated calculation.
                remap={vertex:index for index,vertex in enumerate(members)}
                cell_volume=signed_volume([world[v] for v in members],
                    [tuple(remap[v] for v in poly) for poly in polygons],stable=True)
                if cell_volume<=1e-13:raise RuntimeError("Nonpositive closed torso cell "+obj.name+" "+str((cell,cell_volume,[rnd(world[v]) for v in members],polygons)))
                volumes.append(cell_volume)
        if maximum>TORSO_CELL_EDGE_M+1e-6:raise RuntimeError("Torso cell exceeds metre edge budget "+obj.name)
        cells={"count":len(grouped),"maximum_outer_edge_m":round(maximum,8),
               "minimum_closed_volume_m3":round(min(volumes),13) if volumes else None}
    payload={"vertices":[rnd(p) for p in world],"triangles":[list(t.vertices) for t in mesh.loop_triangles],
             "uv":[rnd(v.uv) for v in mesh.uv_layers.active.data],"weights":weights,
             "materials":[p.material_index for p in mesh.polygons]}
    if cells:payload["cell_uv_channels"]=[[rnd(v.uv) for v in layer.data] for layer in mesh.uv_layers]
    result={"name":obj.name,"vertices":len(world),"triangles":len(mesh.loop_triangles),
            "bounds_unity_m":bounds(world),"closed_volume_m3":round(volume,10) if volume else None,
            "semantic_sha256":digest(json.dumps(payload,sort_keys=True,separators=(",",":")).encode())}
    if cells:result["torso_cells"]=cells
    return result


def build(kind,out,publish=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/SOURCES[kind]),use_anim=False)
    originals=sorted((o for o in bpy.context.scene.objects if o.type=="MESH"),key=lambda o:o.name)
    arm=next(o for o in bpy.context.scene.objects if o.type=="ARMATURE")
    frames,low,high=region_frames(arm)
    fit_terminal_frames(originals,arm,frames,low,high)
    outputs=[];flesh={};bare_sources={};coverage=[];tessellation=[]
    for source_index,source in enumerate(originals):
        if head_family(source):continue
        source.data.calc_loop_triangles()
        collected={};original_area=0.;partitioned_area=0.;cut_tessellation=0.
        polygon_fans={};sampled=set()
        for tri in source.data.loop_triangles:
            poly=[]
            for index,loop in zip(tri.vertices,tri.loops):
                vertex=source.data.vertices[index]
                weights={source.vertex_groups[g.group].name:g.weight for g in vertex.groups if g.weight>1e-7}
                scores=[0.]*7
                for bone,weight in weights.items():
                    group=branch(bone)
                    scores[1 if group==0 else group]+=weight
                uv=source.data.uv_layers.active.data[loop].uv.copy() if source.data.uv_layers.active else Vector((vertex.co.x,vertex.co.z))
                poly.append((source.matrix_world@vertex.co,uv,
                             vertex.normal.copy(),weights,scores))
            triangle_area=(poly[1][0]-poly[0][0]).cross(poly[2][0]-poly[0][0]).length*.5
            original_area+=triangle_area
            for region,part in split_regions(poly,arm,low,high):
                for patch,piece in region_patches(part,frames[region],region):
                    # Blender/Unity may choose opposite diagonals of a warped
                    # source quad. Measure the actual emitted cut vertices
                    # against every source-corner fan, not a global tolerance.
                    polygon=source.data.polygons[tri.polygon_index]
                    if len(polygon.vertices)>3:
                        if polygon.index not in polygon_fans:
                            points=[source.matrix_world@source.data.vertices[i].co for i in polygon.vertices]
                            polygon_fans[polygon.index]=[[
                                (points[anchor],points[(anchor+i)%len(points)],points[(anchor+i+1)%len(points)])
                                for i in range(1,len(points)-1)] for anchor in range(len(points))]
                        for vertex in piece:
                            key=(polygon.index,tuple(round(v,8) for v in vertex[0]))
                            if key in sampled:continue
                            sampled.add(key)
                            for fan in polygon_fans[polygon.index]:
                                distance=min((closest_point_on_tri(vertex[0],a,b,c)-vertex[0]).length for a,b,c in fan)
                                cut_tessellation=max(cut_tessellation,distance)
                    piece_area=sum((piece[i][0]-piece[0][0]).cross(piece[i+1][0]-piece[0][0]).length*.5
                                   for i in range(1,len(piece)-1))
                    partitioned_area+=piece_area
                    collected.setdefault((region,patch),[]).append((piece,tri.material_index))
                    if is_body_source(source):
                        flesh.setdefault((region,patch),[]).append((piece,source_index))
                        candidates=bare_sources.setdefault(region,{})
                        _,area=candidates.get(source.name,(source,0.))
                        candidates[source.name]=(source,area+piece_area)
        if abs(original_area-partitioned_area)>max(1e-7,original_area*1e-5):
            raise RuntimeError("Exterior partition area changed "+source.name+" "+str((original_area,partitioned_area)))
        if collected:
            tolerance=source_polygon_warp(source)
            tolerance["maximum_cut_tessellation_deviation_m"]=round(cut_tessellation,8)
            tessellation.append(tolerance)
            if source.name=="CLO_ApronStrap.L":print("APRON SOURCE TESSELLATION",kind,tolerance,flush=True)
            coverage.append({"source":source.name,"source_area_m2":round(original_area,8),
                             "body_area_m2":round(partitioned_area,8),"head_excluded":False})
        for (region,patch),polys in sorted(collected.items()):
            obj=build_mesh(f"Region{region}Patch{patch}__{source.name}",source,polys,
                           frames[region] if region in TORSO_REGIONS else None)
            if obj is not None:outputs.append(obj)
    contracts=[]
    for region in range(1,17):
        if region not in bare_sources:
            raise RuntimeError("Missing actual bare anatomy for "+NAMES[region])
        source=region_source(bare_sources[region],region)
        region_points=[p[0] for patch in range(4) for poly,_ in flesh.get((region,patch),[]) for p in poly]
        region_min=Vector(tuple(min(p[a] for p in region_points) for a in range(3)))
        region_max=Vector(tuple(max(p[a] for p in region_points) for a in range(3)))
        for patch in range(4):
            polys=flesh.get((region,patch),[])
            if not polys:
                raise RuntimeError("Empty flesh patch "+str((kind,region,patch)))
            name=f"FleshRegion{region}Patch{patch}__{source.name}"
            obj=(flesh_cells(name,source,polys,frames[region]) if region in TORSO_REGIONS else
                 flesh_shell(name,source,polys,frames[region],REGION_BONES[region]))
            if obj is None:raise RuntimeError("Missing flesh patch")
            outputs.append(obj)
        vertices,faces=bone_geometry(region,frames[region],(region_min,region_max))
        if region in (4,7,10,13,16):vertices=fit_bone_envelope(vertices,region_min,region_max)
        outputs.append(simple_mesh(f"BoneRegion{region}__{source.name}",source,vertices,faces,REGION_BONES[region]))
        contracts.append({"id":region,"name":NAMES[region],"bone":REGION_BONES[region],"source":source.name,
                          "frame_unity_m":{key:unity(value) for key,value in frames[region].items()},
                          "bare_bounds_unity_m":bounds(region_points)})
    measurements=[measure(o,o.name.startswith(("Flesh","Bone"))) for o in sorted(outputs,key=lambda o:o.name)]
    for source in originals:bpy.data.objects.remove(source,do_unlink=True)
    arm.animation_data_clear()
    for bone in arm.pose.bones:bone.matrix_basis.identity()
    if publish:
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(out/("Body"+kind+".fbx")),use_selection=True,
            object_types={"EMPTY","ARMATURE","MESH"},axis_forward="-Z",axis_up="Y",add_leaf_bones=False,
            bake_anim=False,use_armature_deform_only=False,use_mesh_modifiers=False,mesh_smooth_type="FACE")
    print("BODY ANATOMY",kind,len(outputs),"meshes",flush=True)
    return {"source_sha256":digest((ROOT/SOURCES[kind]).read_bytes()),"coverage":coverage,"source_tessellation":tessellation,
            "regions":contracts,"meshes":measurements,
            "semantic_sha256":digest(json.dumps(measurements,sort_keys=True,separators=(",",":")).encode())}


def texture():
    width=64;rows=[]
    for y in range(width):
        row=bytearray([0])
        for x in range(width):
            grain=((x*17+y*31+x*y*7)%17)-8
            line=5 if (x*3+y)%29==0 else 0
            row.extend((max(0,178+grain-line),max(0,169+grain-line),max(0,139+grain-line)))
        rows.append(bytes(row))
    def chunk(kind,data):return struct.pack(">I",len(data))+kind+data+struct.pack(">I",zlib.crc32(kind+data)&0xffffffff)
    return b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",width,width,8,2,0,0,0))+chunk(b"IDAT",zlib.compress(b"".join(rows),9))+chunk(b"IEND",b"")


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--output-dir",type=Path,default=ROOT/"Assets/Resources/CombatGore")
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--diagnose-triangulation",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if args.diagnose_triangulation:
        for kind,path in SOURCES.items():
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=str(ROOT/path),use_anim=False)
            entries=[source_polygon_warp(o) for o in bpy.context.scene.objects if o.type=="MESH"]
            print("SOURCE TESSELLATION",kind,json.dumps(sorted(entries,key=lambda e:e["maximum_nonplanarity_m"],reverse=True)[:12]),flush=True)
        return
    out=args.output_dir;out.mkdir(parents=True,exist_ok=True)
    old=json.loads((out/"CombatBody3D.json").read_text(encoding="utf-8")) if args.validate_only else None
    if old:
        for file,expected in old["files"].items():
            if digest((out/file).read_bytes())!=expected:raise RuntimeError("Body published asset changed "+file)
    models={kind:build(kind,out,not args.validate_only) for kind in SOURCES}
    rebuilt={kind:build(kind,out) for kind in SOURCES}
    if models!=rebuilt:raise RuntimeError("Body derivative differs across independent source rebuilds")
    pixels=texture()
    if args.validate_only:
        if (out/"BoneSurface.png").read_bytes()!=pixels:raise RuntimeError("Bone texture is not deterministic")
    else:(out/"BoneSurface.png").write_bytes(pixels)
    manifest={"version":2,"generator":"tools/build-combat-body-3d-model.py","test_only":True,
              "region_count":17,"body_region_count":16,"patch_count":4,
              "patch_mapping":"id=(dot(point-origin,cross)<0 ? 1 : 0)+(dot(point-origin,second)<0 ? 2 : 0)",
              "head_owner":"CombatGore/HeadHero.fbx,HeadNpc.fbx","sources":SOURCES,"models":models,
              "torso_cells":{"regions":list(TORSO_REGIONS),"maximum_edge_m":TORSO_CELL_EDGE_M,
                  "layer_scales":{"outer":.94,"inner":.32,"degenerate_cap_floor_m":.002},
                  "coordinates":"mesh-local metres, Unity XYZ=(Blender X,Z,Y); import measures per-source affine frame from exterior centres and flesh inner vertices to restored source-local geometry",
                  "uv_channels":{"1":"cell id, outer flag","2":"reserved source indices","3":"reserved source barycentric",
                      "4":"inner X,Y","5":"inner Z,0","6":"original surface centre X,Y","7":"original surface centre Z,0"},
                  "runtime_channels":{"4":"imported mesh-local inner XYZ","6":"imported mesh-local original surface centre XYZ"}},
              "source_tessellation_tolerances":[dict(kind=kind,**entry) for kind,model in models.items() for entry in model["source_tessellation"]],
              "files":{name:digest((out/name).read_bytes()) for name in ("BodyHero.fbx","BodyNpc.fbx","BoneSurface.png")}}
    if args.validate_only:
        if manifest!=old:raise RuntimeError("Body source geometry/skin/UV/partition contract changed")
        print("COMBAT BODY SOURCE COVERAGE, CLOSED CUT RIMS, VISIBLE ANATOMY, SKIN, UV, METRES AND DETERMINISM OK",flush=True)
    else:
        (out/"CombatBody3D.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")


if __name__=="__main__":main()
