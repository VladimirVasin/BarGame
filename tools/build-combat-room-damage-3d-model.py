"""Independent, cavity-preserving damage banks for the Room combat enemy.

Run after publishing Combat/RoomBody.fbx. Exterior cuts retain that model's
UV0/skin; the corridor's tissue follows its actual thin walls instead of
filling the opening with the ordinary anatomical radial core.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import itertools
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

ROOT = Path(__file__).resolve().parents[1]
SOURCE = "Assets/Resources/Combat/RoomBody.fbx"
TORSO_TISSUE_DEPTH_M = .004
RETAINED_HEAD_STRAIN = .025
ARM_REGIONS = (5, 6, 7, 8, 9, 10)


def module(label, filename):
    spec = importlib.util.spec_from_file_location(label, Path(__file__).with_name(filename))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


body = module("room_body_damage_art", "build-combat-body-3d-model.py")
gore = module("room_head_damage_art", "build-combat-gore-3d-model.py")
skull = module("room_skull_damage_art", "build-combat-skull-3d-model.py")
blood = module("room_wound_damage_art", "build-combat-blood-3d-model.py")
for builder in (body, gore, skull.body, blood):
    builder.SOURCES = {"Room": SOURCE}
arm_sources = {}
arm_projection = {}
arm_translations = {}
hand_frames = {}
body_source_names = []
fragment_skin = {}
hidden_slivers = {"count":0,"area_m2":0.}
_simple_mesh = body.simple_mesh


def digest(data):
    return hashlib.sha256(data).hexdigest()


def skin_row(source, vertex):
    row = {source.vertex_groups[group.group].name:float(group.weight)
           for group in vertex.groups if group.weight > 1e-7}
    total = sum(row.values())
    if not row or len(row) > 4 or abs(total-1.) > .002:
        raise RuntimeError("Room arm source has invalid skin: "+source.name)
    return {name:weight/total for name,weight in row.items()}


def barycentric(point, a, b, c):
    value = barycentric_transform(point,a,b,c,Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
    value = Vector(tuple(max(0.,min(1.,v)) for v in value))
    return value/sum(value)


def weighted_row(rows, ratios):
    result = {}
    for row,ratio in zip(rows,ratios):
        for name,weight in row.items():result[name] = result.get(name,0.)+weight*ratio
    result = {name:weight for name,weight in result.items() if weight > 1e-7}
    total = sum(result.values())
    if not total:raise RuntimeError("Room internal arm lost its source skin")
    return {name:weight/total for name,weight in result.items()}


class ArmSurface:
    """Own source triangles, UVs and skin; retained after body removes sources."""
    def __init__(self, source):
        self.name = source.name
        self.points = [source.matrix_world @ vertex.co for vertex in source.data.vertices]
        self.rows = [skin_row(source,vertex) for vertex in source.data.vertices]
        source.data.calc_loop_triangles()
        self.faces = [tuple(triangle.vertices) for triangle in source.data.loop_triangles]
        self.uvs = [tuple(source.data.uv_layers.active.data[index].uv.copy() for index in triangle.loops)
                    for triangle in source.data.loop_triangles]
        self.tree = BVHTree.FromPolygons(self.points,self.faces,all_triangles=True)
        self.transformed = {}

    def project(self, point):
        nearest,_,index,distance = self.tree.find_nearest(point)
        if nearest is None:raise RuntimeError("Room internal arm cannot find source: "+self.name)
        face = self.faces[index]
        ratios = barycentric(nearest,*(self.points[i] for i in face))
        uv = sum((value*ratio for value,ratio in zip(self.uvs[index],ratios)),Vector((0,0)))
        row = weighted_row([self.rows[i] for i in face],ratios)
        return uv,row,distance

    def stretched(self, fraction, hand_scale=1.):
        pose_key = (fraction,hand_scale)
        if pose_key not in self.transformed:
            points = [stretch(point,row,fraction,hand_scale) for point,row in zip(self.points,self.rows)]
            welded,vertices,faces = {},[],[]
            for face in self.faces:
                indices = []
                for index in face:
                    key = tuple(round(value,7) for value in points[index])
                    if key not in welded:
                        welded[key] = len(vertices)
                        vertices.append(points[index])
                    indices.append(welded[key])
                if len(set(indices)) == 3:faces.append(tuple(indices))
            gore.closed_edges(faces,"Room stretched source "+self.name)
            if body.signed_volume(vertices,faces,stable=True) <= 0:
                raise RuntimeError("Room stretched source has nonpositive volume: "+self.name)
            low = Vector(tuple(min(point[a] for point in vertices) for a in range(3)))
            high = Vector(tuple(max(point[a] for point in vertices) for a in range(3)))
            self.transformed[pose_key] = (BVHTree.FromPolygons(vertices,faces,all_triangles=True),vertices,faces,low,high)
        return self.transformed[pose_key]


def stretch(point, row, fraction, hand_scale=1.):
    delta = sum((arm_translations.get(name,Vector((0,0,0)))*(weight*fraction)
                 for name,weight in row.items()),Vector((0,0,0)))
    if hand_scale != 1.:
        for name,weight in sorted(row.items()):
            if name not in hand_frames:continue
            frame,inverse = hand_frames[name]
            local = inverse @ point
            local.y *= hand_scale
            delta += ((frame @ local)-point)*weight
    return point+delta


def capture_arm_source(source):
    if not source.name.startswith("GEO_"):return False
    body_source_names.append(source.name)
    if source.name.startswith(("GEO_Arm.","GEO_Hand")):
        arm_sources[source.name] = ArmSurface(source)
    return True


def prepare_arm_skin():
    arm_sources.clear()
    arm_projection.clear()
    arm_translations.clear()
    hand_frames.clear()
    body_source_names.clear()
    fragment_skin.clear()
    hidden_slivers.update(count=0,area_m2=0.)
    contract = json.loads((ROOT/"Assets/Resources/Combat/RoomBody.json").read_text(encoding="utf-8"))["proportions"]
    scales = (contract["upper_arm_length_scale"],contract["forearm_length_scale"])
    if scales != (1.3,1.8):raise RuntimeError("Room damage requires the approved 1.3/1.8 arm source contract")
    # The source/native rig stays canonical. Runtime translates these two child
    # joints per side after each sample; descendants inherit both translations.
    rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    for side in ("L","R"):
        upper,forearm,hand = (rig.data.bones[name+"."+side] for name in ("upper_arm","forearm","hand"))
        first = rig.matrix_world.to_3x3() @ ((forearm.head_local-upper.head_local)*(scales[0]-1))
        second = rig.matrix_world.to_3x3() @ ((hand.head_local-forearm.head_local)*(scales[1]-1))
        hand_frame = rig.matrix_world @ hand.matrix_local
        for bone in rig.data.bones:
            chain = {bone.name}|{parent.name for parent in bone.parent_recursive}
            if forearm.name in chain:
                arm_translations[bone.name] = first+(second if hand.name in chain else Vector((0,0,0)))
            if hand.name in chain:hand_frames[bone.name] = (hand_frame.copy(),hand_frame.inverted())
    return scales


def source_simple_mesh(name, source, vertices, faces, bone, uvs=None):
    obj = _simple_mesh(name,source,vertices,faces,bone,uvs)
    if bone not in {body.REGION_BONES[region] for region in ARM_REGIONS}:return obj
    if source.name not in arm_sources:
        raise RuntimeError("Room internal arm has no actual source projection: "+name)
    authored = fragment_skin.get(name)
    if authored is not None and len(authored) != len(vertices):
        raise RuntimeError("Room fragment skin lost source correspondence: "+name)
    projected = [(uvs[index],row,0.) for index,row in enumerate(authored)] if authored is not None else [
        arm_sources[source.name].project(point) for point in vertices]
    obj.vertex_groups.clear()
    for key in sorted({key for _,row,_ in projected for key in row}):
        group = obj.vertex_groups.new(name=key)
        for index,(_,row,_) in enumerate(projected):
            if row.get(key,0.) > 1e-7:group.add([index],row[key],"REPLACE")
    layer = obj.data.uv_layers.active
    for loop in obj.data.loops:layer.data[loop.index].uv = projected[loop.vertex_index][0]
    arm_projection[name] = {"source":source.name,"vertices":len(vertices),
        "skin":"authored source-triangle correspondence" if authored is not None else "closest actual source triangle",
        "maximum_source_projection_m":round(max(distance for _,_,distance in projected),7)}
    return obj


body.simple_mesh = source_simple_mesh


def inside_surface(point, solid, triangles=None):
    tree,points,faces,low,high = solid
    # Bounds only reject impossible candidates; acceptance always uses the
    # actual closed source triangles below, including concave cap edges.
    if any(point[a] < low[a]-2e-7 or point[a] > high[a]+2e-7 for a in range(3)):return False
    nearest,normal,index,distance = tree.find_nearest(point)
    if nearest is None:return False
    if distance <= 2e-7:return True
    ratios = barycentric(nearest,*(points[i] for i in faces[index]))
    if min(ratios) > .0001:return normal.dot(point-nearest) <= 2e-7
    if triangles is not None:
        # The exported-bank hand audit batches the same double-precision
        # closed-solid angle formula. Thresholds and the scalar default stay
        # unchanged; this avoids a Python triangle loop for every cap sample.
        import numpy as np
        vectors = triangles-np.asarray(point,dtype=np.float64)
        lengths = np.linalg.norm(vectors,axis=2)
        if np.min(lengths) < 1e-12:return True
        a,b,c = vectors[:,0],vectors[:,1],vectors[:,2]
        dot = lambda first,second:np.einsum('ij,ij->i',first,second)
        determinant = dot(a,np.cross(b,c))
        denominator = np.prod(lengths,axis=1)+dot(a,b)*lengths[:,2]+dot(b,c)*lengths[:,0]+dot(c,a)*lengths[:,1]
        return abs(float(np.sum(2*np.arctan2(determinant,denominator),dtype=np.float64))) > math.tau
    # At a source edge the nearest face normal is ambiguous. Resolve against
    # its closed triangles with a double-precision solid angle, not an AABB.
    angles = []
    for face in faces:
        a,b,c = [tuple(float(points[i][axis])-float(point[axis]) for axis in range(3)) for i in face]
        lengths = [math.sqrt(sum(v*v for v in vector)) for vector in (a,b,c)]
        if min(lengths) < 1e-12:return True
        ab,bc,ca = (sum(x*y for x,y in zip(first,second)) for first,second in ((a,b),(b,c),(c,a)))
        determinant = a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0])
        angles.append(2*math.atan2(determinant,math.prod(lengths)+ab*lengths[2]+bc*lengths[0]+ca*lengths[1]))
    return abs(math.fsum(angles)) > math.tau


def arm_stretch_proof(scales):
    pieces = sorted((obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.name in arm_projection),
                    key=lambda obj:obj.name)
    if len(pieces) != 30:raise RuntimeError("Room arm proof requires all six regions' closed flesh and structural pieces")
    sample_count = 0
    for obj in pieces:
        world = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        rows = [skin_row(obj,vertex) for vertex in obj.data.vertices]
        obj.data.calc_loop_triangles()
        side = obj.name[-1]
        sources = [source for name,source in sorted(arm_sources.items()) if name.endswith("."+side)]
        for fraction in (0.,.5,1.):
            solids = [source.stretched(fraction) for source in sources]
            points = [stretch(point,row,fraction) for point,row in zip(world,rows)]
            samples = list(points)
            for triangle in obj.data.loop_triangles:
                a,b,c = (points[i] for i in triangle.vertices)
                samples.extend(((a+b+c)/3,(a+b)/2,(b+c)/2,(c+a)/2))
            sample_count += len(samples)
            for point in samples:
                if not any(inside_surface(point,solid) for solid in solids):
                    raise RuntimeError("Room internal arm leaves actual stretched source volume: "+obj.name+
                        "; fraction="+str(fraction)+"; point="+str(body.unity(point)))
    return {"upper_arm_length_scale":scales[0],"forearm_length_scale":scales[1],
        "pose":"canonical rest rig with the runtime four child-joint translations",
        "stretch_fractions":[0.,.5,1.],"piece_count":len(pieces),"sample_count":sample_count,
        "containment":"vertices, edge midpoints and triangle centres inside actual same-side source solids",
        "projection":"internal UV0/skin from actual source triangles; authored fragment correspondence or closest triangle",
        "source_bound_fragment_regions":{"flesh":[6,7,9,10],"structural":[5,7,8,10]},
        "hidden_clipping_slivers":{"minimum_edge_m":.000001,"skipped_fragment_inputs":hidden_slivers["count"],
            "skipped_source_area_m2":round(hidden_slivers["area_m2"],14),"maximum_count":64,
            "maximum_total_source_area_m2":.0000001,"exterior_triangles":"retained without sliver filtering"},
        "projected_pieces":dict(sorted(arm_projection.items()))}


def exported_hand_length_proof(resources, manifest):
    """Audit the immutable exported bank under the runtime hand affine.

    The native meshes, grip shapes, UVs and weights are read from FBX; no
    derivative geometry is reconstructed or projected by this proof.
    """
    import numpy as np
    if digest((ROOT/SOURCE).read_bytes()) != manifest["source"]["sha256"]:
        raise RuntimeError("Room hand proof requires the damage bank's exact native source")
    for filename,expected in manifest["files"].items():
        if digest((resources/filename).read_bytes()) != expected:
            raise RuntimeError("Room hand proof cannot reuse changed bank bytes: "+filename)
    contract = json.loads((ROOT/"Assets/Resources/Combat/RoomBody.json").read_text(encoding="utf-8"))["proportions"]
    if contract.get("hand_length_scale") != 1.5 or contract.get("hand_longitudinal_axis") != "local_y":
        raise RuntimeError("Room hand proof requires approved local-Y hand length scale 1.5")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/SOURCE),use_anim=False)
    scales = prepare_arm_skin()
    for source in sorted(bpy.context.scene.objects,key=lambda item:item.name):
        if source.type == "MESH":capture_arm_source(source)
    basis = {}
    for side in ("L","R"):
        frame,_ = hand_frames["hand."+side]
        basis[side] = {"rest_pivot_unity_m":body.unity(frame.translation),
                       "rest_longitudinal_axis_unity":body.unity(frame.to_3x3().col[1].normalized())}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(resources/"CombatGore/BodyRoom.fbx"),use_anim=False)
    expected_names = set(manifest["arm_stretch_proof"]["projected_pieces"])
    pieces = sorted((obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.name in expected_names),
                    key=lambda obj:obj.name)
    if len(pieces) != 30 or {obj.name for obj in pieces} != expected_names:
        raise RuntimeError("Room exported hand proof lost one of its thirty actual arm layers")
    sample_count,max_influences = 0,0
    poses = (1.,1.25,1.5)
    surfaces = {}
    for side in ("L","R"):
        sources = [source for name,source in sorted(arm_sources.items()) if name.endswith("."+side)]
        for scale in poses:
            solids = [source.stretched(1.,scale) for source in sources]
            surfaces[side,scale] = [(solid,np.asarray([[solid[1][i] for i in face] for face in solid[2]],dtype=np.float64))
                                    for solid in solids]
    for obj in pieces:
        world = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        rows = [skin_row(obj,vertex) for vertex in obj.data.vertices]
        max_influences = max(max_influences,max(len(row) for row in rows))
        obj.data.calc_loop_triangles()
        faces = [tuple(triangle.vertices) for triangle in obj.data.loop_triangles]
        for scale in poses:
            points = [stretch(point,row,1.,scale) for point,row in zip(world,rows)]
            if body.signed_volume(points,faces,stable=True) <= 1e-11:
                raise RuntimeError("Room hand affine inverted a closed internal layer: "+obj.name)
            samples = {tuple(point) for point in points}
            for face in faces:
                a,b,c = (points[index] for index in face)
                samples.update(tuple(point) for point in ((a+b+c)/3,(a+b)/2,(b+c)/2,(c+a)/2))
            sample_count += len(samples)
            for values in samples:
                point = Vector(values)
                if not any(inside_surface(point,solid,triangles) for solid,triangles in surfaces[obj.name[-1],scale]):
                    raise RuntimeError("Room exported internal layer crosses actual source under full hand affine: "+obj.name+
                                       "; hand_scale="+str(scale)+"; point="+str(body.unity(point)))
        print("ROOM EXPORTED HAND AFFINE",obj.name,"PASS",flush=True)
    return {"source_sha256":manifest["source"]["sha256"],
        "body_bank_sha256":manifest["files"]["CombatGore/BodyRoom.fbx"],
        "native_bank_files":"unchanged; actual FBX vertices, UV0, raw skin and bind skeleton",
        "upper_arm_length_scale":scales[0],"forearm_length_scale":scales[1],
        "hand_length_scale":1.5,"hand_longitudinal_axis":"local_y","checked_hand_scales":list(poses),
        "matrix":"T(upper+forearm extension) * M_hand * diag(1,handScale,1) * inverse(M_hand); inherited by descendants",
        "rest_hand_basis":basis,"piece_count":len(pieces),"sample_count":sample_count,
        "maximum_skin_influences":max_influences,
        "containment":"actual exported vertices, unique edge midpoints and triangle centres inside actual same-side source solids",
        "tolerance_m":.0000002,"actor_uniform_scale":1.2}


def inward_head_source():
    """Capture the same actual inward source surface used by gore.brain.

    Room retains its closed, star-shaped head. Its inward shell is a .78
    homothety of the authored surface, not the anatomical head collider or an
    approximate ellipsoid. Uniform actor scale preserves this relative proof.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / SOURCE), use_anim=False)
    source = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.name == "GEO_Head")
    outer = [source.matrix_world @ vertex.co for vertex in source.data.vertices]
    low = Vector(tuple(min(point[axis] for point in outer) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in outer) for axis in range(3)))
    centre, half = (low+high)*.5, (high-low)*.5
    vertices, mapping, welded = [], {}, {}
    for index,point in enumerate(outer):
        inner = centre+(point-centre)*.78
        key = tuple(round(value,8) for value in inner)
        if key not in welded:
            welded[key] = len(vertices)
            vertices.append(inner)
        mapping[index] = welded[key]
    faces = [tuple(mapping[index] for index in poly.vertices) for poly in source.data.polygons]
    gore.closed_edges(faces, "Room actual inward source skull")
    if body.signed_volume(vertices, faces, stable=True) <= 0:
        raise RuntimeError("Room inward source skull has no positive closed volume")
    tree = BVHTree.FromPolygons(vertices, faces, all_triangles=False)
    origin = centre+Vector((0,0,half.z*.32))
    nearest, normal, _, _ = tree.find_nearest(origin)
    if nearest is None or normal.dot(origin-nearest) >= 0:
        raise RuntimeError("Room retained strain origin is outside its actual inward skull")
    return tree, origin, body.bounds(outer)


def retained_head_strain_proof(inward):
    tree, origin, source_bounds = inward
    pieces = sorted((obj for obj in bpy.context.scene.objects if obj.type == "MESH" and
                     obj.name.startswith("Brain") and obj.name.endswith("__GEO_Head")), key=lambda obj:obj.name)
    if len(pieces) != 8:
        raise RuntimeError("Room retained strain requires its eight own brain pieces")
    matrix = pieces[0].matrix_world.copy()
    if any(max(abs(obj.matrix_world[row][column]-matrix[row][column]) for row in range(4) for column in range(4)) > 1e-7
           for obj in pieces):
        raise RuntimeError("Room retained brain pieces must share one renderer-local strain frame")
    points = [vertex.co.copy() for obj in pieces for vertex in obj.data.vertices]
    low = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    # Runtime initializes every retained tissue piece from the common union of
    # SoftMesh.bounds, whose centre can differ from the cranial partition origin.
    centre = (low+high)*.5
    strains = [Vector((0,0,0))]+[Vector(values).normalized()*RETAINED_HEAD_STRAIN
        for values in itertools.product((-1.,0.,1.), repeat=3) if any(values)]
    minimum_rest, minimum_strained = math.inf, math.inf
    sample_count = 0
    for obj in pieces:
        samples = [vertex.co.copy() for vertex in obj.data.vertices]
        obj.data.calc_loop_triangles()
        for tri in obj.data.loop_triangles:
            a,b,c = (obj.data.vertices[index].co for index in tri.vertices)
            samples.extend(((a+b+c)/3,(a+b)/2,(b+c)/2,(c+a)/2))
        sample_count += len(samples)
        for sample in samples:
            for index,strain in enumerate(strains):
                scale = Vector((1+strain.x,1+strain.y,1+strain.z))
                scale /= (scale.x*scale.y*scale.z)**(1/3)
                delta = sample-centre
                changed = matrix @ (centre+Vector((delta.x*scale.x,delta.y*scale.y,delta.z*scale.z)))
                radial = changed-origin
                if radial.length > 1e-8:
                    hit, _, _, distance = tree.ray_cast(origin, radial.normalized(), .5)
                    if hit is None or distance < radial.length-1e-7:
                        raise RuntimeError("Room retained .025 strain crosses its actual inward skull: "+obj.name+
                            "; point="+str(body.unity(changed))+"; strain="+str(tuple(strain)))
                nearest, _, _, clearance = tree.find_nearest(changed)
                if nearest is None:
                    raise RuntimeError("Room retained strain lacks a measurable inward source surface")
                if index == 0:minimum_rest = min(minimum_rest,clearance)
                else:minimum_strained = min(minimum_strained,clearance)
    return {"source_renderer":"GEO_Head", "inward_source_scale":.78,
        "source_bounds_unity_m":source_bounds, "strain_center_unity_m":body.unity(matrix @ centre),
        "retained_strain_limit":RETAINED_HEAD_STRAIN, "normalized_axial_diagonal_extrema":26,
        "sample_count":sample_count, "minimum_rest_clearance_m":round(minimum_rest,7),
        "minimum_strained_clearance_m":round(minimum_strained,7),
        "containment":"vertices, edge midpoints and triangle centres inside actual inward source skull",
        "strain_frame":"common renderer-local Brain mesh bounds; volume-normalized scale"}


def torso_structure(region, source, basis, flesh):
    """Sparse closed fragments inside actual shell surfaces, never a rib cage.

    The source has closed coat flanks, a thick tapered corridor and closed
    doorframes. Each retained prism lies 6--10mm behind one source triangle;
    the minimum authored solid thickness is 18mm. Population is bounded.
    """
    triangles = [tri for patch in range(4) for poly, _ in flesh.get((region, patch), [])
                 for tri in body.tessellate(poly)]
    step = max(1, math.ceil(len(triangles) / 48))
    vertices, faces, uv = [], [], []
    for tri in triangles[::step]:
        points = [p[0] for p in tri]
        normal = (points[1]-points[0]).cross(points[2]-points[0]).normalized()
        if normal.length < .9:
            continue
        outer = [point-normal*.006 for point in points]
        inner = [point-normal*.010 for point in points]
        topology = [(0,1,2),(5,4,3),(0,3,4),(0,4,1),
                    (1,4,5),(1,5,2),(2,5,3),(2,3,0)]
        if body.signed_volume(outer+inner, topology, stable=True) < 0:
            topology = [tuple(reversed(face)) for face in topology]
        offset = len(vertices)
        vertices.extend(outer+inner)
        uv.extend(p[1].copy() for _ in range(2) for p in tri)
        faces.extend(tuple(offset+i for i in face) for face in topology)
    if not vertices:
        raise RuntimeError("Room torso lacks source-bound structural fragments")
    return vertices, faces, uv


def arm_structure(region, source, basis, flesh):
    """Keep shoulder and palm structures in their actual closed sources.

    Generic upper-arm caps and hand bones can cross these retained surfaces.
    Each existing region still owns one closed structural mesh.
    """
    if region not in (5,7,8,10):return None
    entries = [(tri,arm_sources[source.name]) for patch in range(4)
               for poly,index in flesh.get((region,patch),[]) if body_source_names[index] == source.name
               for tri in body.tessellate(poly)]
    near,far = (.003,.006) if region in (7,10) else (.006,.010)
    vertices,faces,uv,skin = source_fragments(entries,source,near,far,48)
    fragment_skin["BoneRegion"+str(region)+"__"+source.name] = skin
    return vertices,faces,uv


def source_fragments(entries, source, near, far, limit=None):
    """Bounded finite closed layers in actual source solids, including caps.

    An inward normal at an oblique cap can exit an adjacent face. Only hidden
    layer triangles may contract in their original plane; exterior geometry
    and the source's closed-volume guard remain unchanged. Candidate skin is
    the original source triangle's barycentric correspondence, including
    arm-cuff and finger triangles within the hand damage region.
    """
    step = max(1,math.ceil(len(entries)/limit)) if limit else 1
    vertices,faces,uv,skin = [],[],[],[]
    side = source.name[-1]
    solids = {fraction:[item.stretched(fraction) for name,item in sorted(arm_sources.items())
                        if name.endswith("."+side)] for fraction in (0.,.5,1.)}
    topology = [(0,1,2),(5,4,3),(0,3,4),(0,4,1),(1,4,5),(1,5,2),(2,5,3),(2,3,0)]
    for triangle,actual_source in entries[::step]:
        normal = (triangle[1][0]-triangle[0][0]).cross(triangle[2][0]-triangle[0][0])
        if min((triangle[a][0]-triangle[b][0]).length for a,b in ((0,1),(1,2),(2,0))) < .000001:
            hidden_slivers["count"] += 1
            hidden_slivers["area_m2"] += normal.length*.5
            if hidden_slivers["count"] > 64 or hidden_slivers["area_m2"] > .0000001:
                raise RuntimeError("Room hidden clipping slivers exceed their bounded diagnostic contract")
            continue
        normal.normalize()
        middle = body.interpolate(body.interpolate(triangle[0],triangle[1],.5),triangle[2],1/3)
        reference = sum(actual_source.points,Vector((0,0,0)))/len(actual_source.points)
        fitted = None
        for radial in (False,True):
            for depth_scale in (1.,.7,.4,.2):
                for shrink in (0.,.03,.10,.25,.45,.65,.8):
                    tri = tuple(body.interpolate(point,middle,shrink) for point in triangle)
                    directions = [(point[0]-reference).normalized() if radial else normal for point in tri]
                    points = [point[0]-direction*(depth*depth_scale) for depth in (near,far)
                              for point,direction in zip(tri,directions)]
                    if abs(body.signed_volume(points,topology,stable=True)) < 1e-13:continue
                    rows = [dict(point[3]) for _ in range(2) for point in tri]
                    valid = True
                    for fraction in (0.,.5,1.):
                        changed = [stretch(point,row,fraction) for point,row in zip(points,rows)]
                        samples = list(changed)
                        for face in topology:
                            a,b,c = (changed[index] for index in face)
                            samples.extend(((a+b+c)/3,(a+b)/2,(b+c)/2,(c+a)/2))
                        if any(not any(inside_surface(point,solid) for solid in solids[fraction]) for point in samples):
                            valid = False
                            break
                    if valid:
                        fitted = tri,points
                        break
                if fitted:break
            if fitted:break
        if fitted is None:
            raise RuntimeError("Room source-bound arm layer cannot fit actual closed source: "+source.name+
                               "; actual="+actual_source.name+"; centre="+str(body.unity(middle[0])))
        tri,points = fitted
        closed_faces = topology if body.signed_volume(points,topology,stable=True) > 0 else [tuple(reversed(face)) for face in topology]
        offset = len(vertices)
        vertices.extend(points)
        uv.extend(point[1].copy() for _ in range(2) for point in tri)
        skin.extend(dict(point[3]) for _ in range(2) for point in tri)
        faces.extend(tuple(offset+i for i in face) for face in closed_faces)
    if not vertices:raise RuntimeError("Room arm lacks finite source-bound closed fragments")
    return vertices,faces,uv,skin


def arm_flesh(region, patch, name, source, polys, basis, bone):
    if region in (6,7,9,10):
        entries = [(tri,arm_sources[body_source_names[index]]) for poly,index in polys for tri in body.tessellate(poly)]
        near,far = (.0008,.0015) if region in (7,10) else (.0008,.004)
        vertices,faces,uv,skin = source_fragments(entries,source,near,far)
        fragment_skin[name] = skin
        print("ROOM SOURCE-BOUND FLESH",region,patch,len(vertices)//6,"closed fragments",flush=True)
        return body.simple_mesh(name,source,vertices,faces,bone,uv)
    if region in body.TORSO_REGIONS:
        return body.flesh_cells(name,source,polys,basis,TORSO_TISSUE_DEPTH_M)
    return body.flesh_shell(name,source,polys,basis,bone)


def validate_corridor():
    """A nine-ray bundle must pass through source, tissue and inner fragments.

    All derivatives are tested together, including normally hidden tissue.
    This detects a radial core, an anatomical rib cage or an accidental rear
    cap even when the intact outer model still looks hollow.
    """
    points, faces = [], []
    for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name):
        if obj.type != "MESH" or not obj.name.endswith("__GEO_Torso"):
            continue
        offset = len(points)
        points.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
        faces.extend(tuple(offset+i for i in poly.vertices) for poly in obj.data.polygons)
    if not faces:
        raise RuntimeError("Room damage lacks its authored torso shell")
    tree = BVHTree.FromPolygons(points, faces, all_triangles=False)
    contract = json.loads((ROOT / "Assets/Resources/Combat/RoomBody.json").read_text(encoding="utf-8"))["corridor"]
    mouth, far = contract["mouth"], contract["far"]
    # A real far wall is allowed; its surface is beyond the interior reach.
    start = mouth["depth"]-.05
    distance = far["depth"]-start + (.02 if contract["open_far_end"] else -.002)
    width = min(mouth["half_width"], far["half_width"])*.30
    centre = (far["bottom"]+far["top"])*.5
    offset = (far["top"]-far["bottom"])*.10
    probes = []
    for x in (-width, 0., width):
        for z in (centre-offset, centre, centre+offset):
            origin = Vector((x, start, z))
            location, _, _, _ = tree.ray_cast(origin, Vector((0, 1, 0)), distance)
            if location is not None:
                raise RuntimeError("Room damage closed the corridor: " + str(body.unity(location)))
            probes.append({"origin_unity_m": body.unity(origin), "direction_unity": [0,0,1],
                           "clear_distance_m": round(distance, 7)})
    return probes


def build(out, publish):
    gore_out = out / "CombatGore"
    blood_out = out / "CombatBlood"
    inward = inward_head_source()
    arm_scales = prepare_arm_skin()
    head = gore.build("Room", gore_out, publish)
    strain = retained_head_strain_proof(inward)
    models = {"HeadRoom": head,
              "SkullRoom": skull.build("Room", gore_out, publish, head_contract={"models": {"Room": head}}),
              "BodyRoom": body.build("Room", gore_out, publish,
                  source_filter=capture_arm_source,
                  torso_tissue_depth=TORSO_TISSUE_DEPTH_M, torso_bone_factory=torso_structure,
                  region_bone_factory=arm_structure,
                  region_flesh_factory=arm_flesh,
                  allow_empty_torso_patches=True)}
    corridor = validate_corridor()
    arms = arm_stretch_proof(arm_scales)
    patches, wounds = blood.wounds("Room", blood_out, publish,
        surface_filter=lambda name: name.startswith("GEO_") and not name.startswith(("GEO_Ear", "GEO_FaceSurface")),
        required_palms={"GEO_HandPalm.L", "GEO_HandPalm.R"})
    models["WoundsRoom"] = wounds
    return models, patches, corridor, strain, arms


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root", type=Path, default=ROOT / "Assets/Resources")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--hand-proof-only", action="store_true",
                        help="Audit existing exported Room banks and update only their hand affine proof")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if not (ROOT / SOURCE).exists():
        raise RuntimeError("Publish the independent Room body before its damage banks")
    out = args.output_root.resolve()
    manifest_path = out / "CombatGore/CombatRoomDamage3D.json"
    for folder in (out / "CombatGore", out / "CombatBlood"):
        folder.mkdir(parents=True, exist_ok=True)
    if args.hand_proof_only:
        fallback = ROOT/"Assets/Resources"
        if args.validate_only and not manifest_path.exists():
            raise RuntimeError("Room hand validation requires an existing output manifest")
        existing_path = manifest_path if manifest_path.exists() else fallback/"CombatGore/CombatRoomDamage3D.json"
        existing = json.loads(existing_path.read_text(encoding="utf-8"))
        resources = out if all((out/name).exists() for name in existing["files"]) else fallback
        proof = exported_hand_length_proof(resources,existing)
        if proof != exported_hand_length_proof(resources,existing):
            raise RuntimeError("Room full hand affine differs across independent exported-bank imports")
        updated = dict(existing,hand_length_proof=proof)
        if args.validate_only:
            if updated != existing:raise RuntimeError("Room exported hand affine proof changed")
        else:
            manifest_path.write_text(json.dumps(updated,indent=2)+"\n",encoding="utf-8")
        print("ROOM EXPORTED HAND AFFINE, CLOSED SOURCE CONTAINMENT AND UNCHANGED BANK BYTES OK",flush=True)
        return
    previous = json.loads(manifest_path.read_text(encoding="utf-8")) if args.validate_only else None
    if previous:
        for filename, expected in previous["files"].items():
            if digest((out / filename).read_bytes()) != expected:
                raise RuntimeError("Room damage bank changed: " + filename)
    models, patches, corridor, strain, arms = build(out, not args.validate_only)
    if (models, patches, corridor, strain, arms) != build(out, False):
        raise RuntimeError("Room damage banks differ across independent source rebuilds")
    manifest = {"generator": "tools/build-combat-room-damage-3d-model.py", "version": 1, "test_only": True,
        "source": {"path": SOURCE, "sha256": digest((ROOT / SOURCE).read_bytes())},
        "body_region_count": 16, "patch_count": 4, "head_sector_count": 16, "brain_count": 8,
        "surface_lift_m": .0018, "torso_tissue_depth_m": TORSO_TISSUE_DEPTH_M,
        "flesh_patch_count": sum(mesh["name"].startswith("FleshRegion") for mesh in models["BodyRoom"]["meshes"]),
        "empty_torso_flesh_patches": models["BodyRoom"]["empty_flesh_patches"],
        "torso_structure": "bounded_source_surface_fragments", "clear_corridor_probes": corridor,
        "retained_head_strain_proof":strain,
        "arm_stretch_proof":arms,
        "models": models, "wound_patches": patches,
        "source_tessellation_tolerances": [dict(kind="Room", **entry) for entry in models["BodyRoom"]["source_tessellation"]],
        "files": {filename: digest((out / filename).read_bytes()) for filename in
            ("CombatGore/BodyRoom.fbx", "CombatGore/HeadRoom.fbx", "CombatGore/SkullRoom.fbx", "CombatBlood/WoundsRoom.fbx")}}
    proportions = json.loads((ROOT/"Assets/Resources/Combat/RoomBody.json").read_text(encoding="utf-8"))["proportions"]
    if proportions.get("hand_length_scale",1.) != 1.:
        manifest["hand_length_proof"] = exported_hand_length_proof(out,manifest)
    if args.validate_only:
        if manifest != previous:
            raise RuntimeError("Room source/UV/skin/cavity/cut/containment contract changed")
        print("ROOM DAMAGE SOURCE, UV, SKIN, CLOSED TISSUE, CLEAR CORRIDOR AND DETERMINISM OK", flush=True)
    else:
        manifest_path.write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
        print("ROOM DAMAGE BANKS GENERATED", flush=True)


if __name__ == "__main__":
    main()
