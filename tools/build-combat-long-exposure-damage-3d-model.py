"""Build opaque LongExposure damage from its own closed swept source volume.

The retained rig, source UV0 and source skin drive all exterior partitions and
source-bound tissue, structural layers and soft head pieces. Inter-phase
concavities stay empty; no radial human core or runtime ghost is introduced.
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

ROOT = Path(__file__).resolve().parents[1]
SOURCE = "Assets/Resources/Combat/LongExposureBody.fbx"
SOURCE_MANIFEST = ROOT / "Assets/Resources/Combat/LongExposureBody.json"
HEAD_LAYERS = {"tissue": (.0008, .003), "skull": (.005, .008), "soft": (.022, .028)}
FILES = ("CombatGore/BodyLongExposure.fbx", "CombatGore/HeadLongExposure.fbx",
         "CombatGore/SkullLongExposure.fbx", "CombatBlood/WoundsLongExposure.fbx")


def module(label, filename):
    spec = importlib.util.spec_from_file_location(label, Path(__file__).with_name(filename))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


body = module("long_exposure_body_damage_art", "build-combat-body-3d-model.py")
gore = module("long_exposure_head_damage_art", "build-combat-gore-3d-model.py")
blood = module("long_exposure_wound_damage_art", "build-combat-blood-3d-model.py")
for builder in (body, gore, blood):
    builder.SOURCES = {"LongExposure": SOURCE}
queries = {}
body_sources = []
layer_proofs = []
erosion_target_count = 0
mesh_skin_rows = {}
_simple_mesh = body.simple_mesh


def source_simple_mesh(name, source, vertices, faces, bone, uvs=None):
    obj = _simple_mesh(name, source, vertices, faces, bone, uvs)
    if name in mesh_skin_rows:
        rows = mesh_skin_rows[name]
        if len(rows) != len(vertices):
            raise RuntimeError("LongExposure structural skin lost source correspondence: " + name)
        obj.vertex_groups.clear()
        for key in sorted({key for row in rows for key in row}):
            group = obj.vertex_groups.new(name=key)
            for vertex,row in enumerate(rows):
                if row.get(key,0.) > 1e-7:
                    group.add([vertex],row[key],"REPLACE")
    return obj


body.simple_mesh = source_simple_mesh


def digest(data):
    return hashlib.sha256(data).hexdigest()


def source_triangles(source):
    source.data.calc_loop_triangles()
    normal_matrix = source.matrix_world.to_3x3().inverted().transposed()
    rows = []
    for triangle in source.data.loop_triangles:
        poly = []
        for index, loop in zip(triangle.vertices, triangle.loops):
            vertex = source.data.vertices[index]
            weights = {source.vertex_groups[g.group].name: g.weight for g in vertex.groups if g.weight > 1e-7}
            poly.append((source.matrix_world @ vertex.co, source.data.uv_layers.active.data[loop].uv.copy(),
                         (normal_matrix @ vertex.normal).normalized(), weights, [0.]*7))
        rows.append((tuple(poly), triangle.polygon_index))
    return rows


class SourceSolid:
    """The actual closed source surface, including its concave inter-phase surfaces."""
    def __init__(self, source):
        points, faces, welded = [], [], {}
        self.triangles = source_triangles(source)
        if not self.triangles or len(self.triangles) > 8192:
            raise RuntimeError("LongExposure damage source exceeds its bounded surface budget: " + source.name)
        for poly, _ in self.triangles:
            face = []
            for point in poly:
                key = tuple(round(float(v), 7) for v in point[0])
                if key not in welded:
                    welded[key] = len(points)
                    points.append(point[0].copy())
                face.append(welded[key])
            if len(set(face)) == 3:
                faces.append(tuple(face))
        gore.closed_edges(faces, "LongExposure source " + source.name)
        volume = body.signed_volume(points, faces, stable=True)
        if volume <= 1e-10:
            raise RuntimeError("LongExposure source is not an outward closed solid: " + source.name)
        self.name = source.name
        self.tree = BVHTree.FromPolygons(points, faces, all_triangles=True)
        self.points, self.faces = points, faces
        self.ray_directions = [Vector(values).normalized() for values in
            ((.719246,.423714,.55128),(-.42171,.73134,.53727),(.31337,-.55821,.76891))]
        low = Vector(tuple(min(point[a] for point in points) for a in range(3)))
        high = Vector(tuple(max(point[a] for point in points) for a in range(3)))
        interior = []
        for ratios in itertools.product((.05,.15,.3,.5,.7,.85,.95), repeat=3):
            point = Vector(tuple(low[a]+(high[a]-low[a])*ratios[a] for a in range(3)))
            nearest, normal, _, distance = self.tree.find_nearest(point)
            if nearest is not None and self.contains(point, .000001):
                interior.append((distance, point))
        if not interior:
            raise RuntimeError("LongExposure source has no measured interior reference: " + source.name)
        self.reference = max(interior, key=lambda item: item[0])[1]
        self.maximum_clearance = max(distance for distance,_ in interior)
        self.interior = interior
        self.proof = {"source": source.name, "welded_vertices": len(points), "triangles": len(faces),
                      "closed_volume_m3": round(volume, 10)}

    def contains(self, point, clearance=0.):
        nearest, normal, index, distance = self.tree.find_nearest(point)
        if nearest is None or distance < clearance-2e-7:
            return False
        if distance <= 2e-7:
            return True
        # A face normal is a signed-distance test only when the closest point
        # lies inside that face. At concave edges its sign can flip while the
        # point remains well inside the solid. Those cases use actual ray parity.
        a,b,c = (self.points[i] for i in self.faces[index])
        first,second,target = b-a,c-a,nearest-a
        aa,ab,bb = first.dot(first),first.dot(second),second.dot(second)
        determinant = aa*bb-ab*ab
        if determinant > 1e-20:
            at,bt = first.dot(target),second.dot(target)
            u,v = (bb*at-ab*bt)/determinant,(aa*bt-ab*at)/determinant
            if min(u,v,1-u-v) > .0001:
                return normal.dot(point-nearest) <= 2e-7
        votes = []
        for direction in self.ray_directions:
            result = self.ray_inside(point,direction)
            if result is not None:
                votes.append(result)
            if votes.count(True) >= 2:return True
            if votes.count(False) >= 2:return False
        if len(votes) == 1:return votes[0]
        # Edge-near ray hits can all be tangent or numerically repeated. Resolve
        # only those rare cases by the closed mesh's bounded solid angle sum,
        # using scalar double arithmetic instead of float32 Vector differences.
        angles = []
        for face in self.faces:
            a,b,c = [tuple(float(self.points[index][i])-float(point[i]) for i in range(3)) for index in face]
            lengths = [math.sqrt(sum(value*value for value in vector)) for vector in (a,b,c)]
            if min(lengths) < 1e-12:return True
            ab,bc,ca = (sum(x*y for x,y in zip(first,second)) for first,second in ((a,b),(b,c),(c,a)))
            determinant = a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0])
            denominator = math.prod(lengths)+ab*lengths[2]+bc*lengths[0]+ca*lengths[1]
            angles.append(2*math.atan2(determinant,denominator))
        return abs(math.fsum(angles)) > math.tau

    def ray_inside(self,point,direction):
        origin = point.copy()
        crossings = 0
        previous = None
        for _ in range(64):
            hit,normal,_,_ = self.tree.ray_cast(origin,direction,10.)
            if hit is None:
                return crossings % 2 == 1
            if previous is not None and (hit-previous).length < .0001:
                return None
            slope = abs(normal.dot(direction))
            if slope < .0001:return None
            crossings += 1
            previous = hit
            origin = hit+direction*max(.00001,min(.0002,.000001/slope))
        raise RuntimeError("LongExposure containment exceeded its bounded crossing budget: " + self.name)

    def segment_inside(self,start,end):
        delta = end-start
        length = delta.length
        if length < .000002:return self.contains(end)
        direction = delta/length
        epsilon = min(.00001,length*.01)
        origin = start+direction*epsilon
        if not self.contains(origin):
            self.last_segment_failure = {"kind":"start", "point":body.unity(origin),
                "start":body.unity(start),"end":body.unity(end)}
            return False
        remaining = length-2*epsilon
        for _ in range(16):
            if remaining <= 0:return True
            hit,normal,_,distance = self.tree.ray_cast(origin,direction,remaining)
            if hit is None:return True
            if abs(normal.dot(direction)) > .000001:
                self.last_segment_failure = {"kind":"crossing", "point":body.unity(hit),
                    "distance_m":distance,"normal_dot":normal.dot(direction),
                    "start":body.unity(start),"end":body.unity(end)}
                return False
            # A path along an existing boundary is legal; move past only the
            # tangent self-contact. A real exit/entry is never skipped.
            advance = distance+epsilon
            origin += direction*advance
            remaining -= advance
        return False


def query(source):
    if source.name not in queries:
        queries[source.name] = SourceSolid(source)
    return queries[source.name]


def triangle_samples(points, faces):
    result = list(points)
    for face in faces:
        for i in range(1, len(face)-1):
            a, b, c = (points[j] for j in (face[0], face[i], face[i+1]))
            result.extend(((a+b+c)/3, (a+b)/2, (b+c)/2, (c+a)/2))
    return result


PRISM_FACES = ((0,1,2),(5,4,3),(0,3,4),(0,4,1),(1,4,5),(1,5,2),(2,5,3),(2,3,0))
PRISM_EDGES = sorted({tuple(sorted((a,b))) for face in PRISM_FACES
                      for a,b in zip(face,face[1:]+face[:1])})


def inset_triangle(poly, solid, near, far, require_path=False, clearance=0.):
    """Keep an entire finite prism inside its own source, including edges.

    At sharp source corners a face-normal offset may leave the adjacent face.
    Shrink only this hidden layer's triangle in its source plane, preserving
    interpolated UV/skin. The visible exterior is never changed or projected.
    Local measured interior references handle oblique rims where one surface
    normal cannot fit a complete prism inside the source.
    """
    normal = (poly[1][0]-poly[0][0]).cross(poly[2][0]-poly[0][0])
    if normal.length <= 1e-10:
        return None
    normal.normalize()
    middle = body.interpolate(body.interpolate(poly[0], poly[1], .5), poly[2], 1/3)
    references = [point for distance,point in sorted(solid.interior,
                  key=lambda item: (item[1]-middle[0]).length_squared) if distance >= far*1.1][:12]
    for multiplier in (1.5,2.,3.):
        point = middle[0]-normal*far*multiplier
        if solid.contains(point, far*.25):
            references.insert(0, point)
    directions = [normal] + [("reference",point) for point in references]
    directions.append((middle[0]-solid.reference).normalized())
    for axis in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)):
        axis = Vector(axis)
        directions.append(axis)
        if axis.dot(normal) > -.2:
            directions.extend((normal+axis*amount).normalized() for amount in (.25,.5,1.,2.,4.,10.))
    for direction in directions:
        for shrink in (0., .03, .10, .25, .45, .65, .8):
            tri = tuple(body.interpolate(point, middle, shrink) for point in poly)
            offsets = tuple((point[0]-direction[1]).normalized() for point in tri) if isinstance(direction,tuple) else (direction,)*3
            vertices = [point[0]-offset*depth for depth in (near, far) for point,offset in zip(tri,offsets)]
            # At near=0 this face is already proven by source correspondence.
            # A BVH built from float32 world coordinates can put its computed
            # centroid a fraction of a micron above that very same plane.
            # Test the first strictly inward slice instead; the full original
            # boundary-to-target segments are still checked below.
            tested = vertices if near > 0 else [point[0].lerp(target,.01)
                     for point,target in zip(tri,vertices[3:])]+vertices[3:]
            if all(solid.contains(point,clearance) for point in triangle_samples(tested, PRISM_FACES)):
                if any(not solid.segment_inside(tested[a],tested[b]) for a,b in PRISM_EDGES):
                    continue
                if require_path:
                    starts = triangle_samples([p[0] for p in poly],[(0,1,2)])
                    ends = triangle_samples(vertices[3:],[(0,1,2)])
                    if any(not solid.segment_inside(start,end) for start,end in zip(starts,ends)):
                        continue
                    if any(not solid.contains(point) for step in range(1,17)
                            for point in triangle_samples([p[0].lerp(target,step/16) for p,target in zip(poly,vertices[3:])],[(0,1,2)])):
                        continue
                return tri, offsets, vertices
    diagnostic = []
    for point in poly:
        sample = point[0]-(point[0]-solid.reference).normalized()*far
        nearest, local_normal, _, distance = solid.tree.find_nearest(sample)
        diagnostic.append({"source":body.unity(point[0]),"target":body.unity(sample),
                           "signed":round(local_normal.dot(sample-nearest),7),"distance":round(distance,7)})
    first_vertices = [point[0]-normal*depth for depth in (near,far) for point in poly]
    first_failures = []
    for point in triangle_samples(first_vertices,PRISM_FACES):
        if not solid.contains(point,clearance):
            nearest,local_normal,_,distance = solid.tree.find_nearest(point)
            first_failures.append({"point":body.unity(point),"distance":distance,
                                   "signed":local_normal.dot(point-nearest)})
    raise RuntimeError("LongExposure inset exceeds its actual source solid: " + solid.name +
                       "; layer=" + str((near, far)) + "; centre=" + str(body.unity(middle[0]))+
                       "; reference="+str(body.unity(solid.reference))+"; samples="+str(diagnostic)+
                       "; segment="+str(getattr(solid,"last_segment_failure",None))+"; first_failures="+str(first_failures))


def prism_geometry(entries, near, far, limit=None, clearance=0., weighted=False):
    if limit is not None:
        entries = entries[::max(1, math.ceil(len(entries)/limit))]
    vertices, faces, uv, skin = [], [], [], []
    for poly, solid in entries:
        inset = inset_triangle(poly, solid, near, far, clearance=clearance)
        if inset is None:
            continue
        tri, _, points = inset
        topology = PRISM_FACES
        volume = body.signed_volume(points, topology, stable=True)
        if abs(volume) < 1e-13:
            continue
        if volume < 0:
            topology = tuple(tuple(reversed(face)) for face in topology)
        offset = len(vertices)
        vertices.extend(points)
        uv.extend(point[1].copy() for _ in range(2) for point in tri)
        skin.extend(dict(point[3]) for _ in range(2) for point in tri)
        faces.extend(tuple(offset+i for i in face) for face in topology)
    if not vertices:
        raise RuntimeError("LongExposure layer has no finite source-bound volume")
    return (vertices, faces, uv, skin) if weighted else (vertices, faces, uv)


def layer_mesh(name, source, entries, near, far, bone="head", limit=None, clearance=0.):
    vertices, faces, uv, skin = prism_geometry(entries, near, far, limit, clearance, weighted=True)
    mesh_skin_rows[name] = skin
    obj = body.simple_mesh(name, source, vertices, faces, bone, uv)
    measured = body.measure(obj, True)
    layer_proofs.append({"name": name, "source": source.name, "inset_range_m": [near, far],
                         "closed_volume_m3": measured["closed_volume_m3"],
                         "source_uv0": True, "source_skin": True,
                         "containment": "vertices, whole edges and triangle centres inside actual closed source"})
    return obj


def source_interior(source, selected_faces, sector, centre):
    solid = query(source)
    selected = {polygon.index for original, polygon in selected_faces if original.name == source.name}
    entries = [(poly, solid) for poly, polygon in solid.triangles if polygon in selected]
    obj = layer_mesh(f"Sector{sector:02d}__Interior", source, entries, *HEAD_LAYERS["tissue"])
    return obj, body.measure(obj, True)["closed_volume_m3"]


def sector_index(point, centre, band):
    angle = math.atan2(point.y-centre.y, point.x-centre.x) % math.tau
    return min(7, int(angle*8/math.tau)) + (8 if point.z >= band else 0)


def source_soft_parts(source, skull_sources, centre, half_extent):
    solid = query(source)
    groups = [[] for _ in range(8)]
    band = centre.z+half_extent.z*.08
    for poly, _ in solid.triangles:
        midpoint = sum((p[0] for p in poly), Vector())/3
        groups[sector_index(midpoint, centre, band)//2].append((poly, solid))
    pieces = [layer_mesh(f"Brain{i:02d}__GEO_Head", source, entries, *HEAD_LAYERS["soft"], clearance=.006)
              for i, entries in enumerate(groups)]
    world = [obj.matrix_world@v.co for obj in pieces for v in obj.data.vertices]
    low = Vector(tuple(min(point[a] for point in world) for a in range(3)))
    high = Vector(tuple(max(point[a] for point in world) for a in range(3)))
    common = (low+high)*.5
    directions = [Vector(values).normalized()*.025 for values in itertools.product((-1.,0.,1.), repeat=3) if any(values)]
    minimum_clearance = math.inf
    for obj in pieces:
        points = [obj.matrix_world@v.co for v in obj.data.vertices]
        faces = [tuple(poly.vertices) for poly in obj.data.polygons]
        for sample in triangle_samples(points, faces):
            nearest, _, _, distance = solid.tree.find_nearest(sample)
            minimum_clearance = min(minimum_clearance, distance)
            for strain in directions:
                scale = Vector((1+strain.x,1+strain.y,1+strain.z))
                scale /= (scale.x*scale.y*scale.z)**(1/3)
                delta = sample-common
                changed = common+Vector((delta.x*scale.x,delta.y*scale.y,delta.z*scale.z))
                if not solid.contains(changed, .0002):
                    changed_nearest,changed_normal,_,changed_distance=solid.tree.find_nearest(changed)
                    raise RuntimeError("LongExposure retained soft strain crosses swept source volume: " + obj.name+
                        "; rest="+str(body.unity(sample))+"; rest_clearance="+str(distance)+
                        "; changed="+str(body.unity(changed))+"; delta_m="+str((changed-sample).length)+
                        "; changed_clearance="+str(changed_distance)+
                        "; signed="+str(changed_normal.dot(changed-changed_nearest)))
    volumes = {str(i): body.measure(obj, True)["closed_volume_m3"] for i, obj in enumerate(pieces)}
    return pieces, {"partition": "eight angular source-bound swept-volume lamina groups",
        "center_unity_m": body.unity(common), "bounds_unity_m": body.bounds(world),
        "volume_m3": round(sum(volumes.values()), 10), "piece_volumes_m3": volumes,
        "minimum_source_clearance_m": round(minimum_clearance, 7),
        "retained_strain_limit": .025, "strain_containment": "all 26 normalized axial/diagonal extrema; actual source surface",
        "source_uv0": True}


gore.interior = source_interior
gore.brain = source_soft_parts


def build_skull(out, head, publish):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/SOURCE), use_anim=False)
    originals = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    source = next(obj for obj in originals if obj.name == "GEO_Head")
    solid = query(source)
    centre = Vector((head["head_center_unity_m"][0], head["head_center_unity_m"][2], head["head_center_unity_m"][1]))
    groups = [[] for _ in range(16)]
    for poly, _ in solid.triangles:
        point = sum((p[0] for p in poly), Vector())/3
        groups[sector_index(point, centre, head["band_height_unity_m"])].append((poly, solid))
    pieces = [layer_mesh(f"SkullSector{i}__GEO_Head", source, entries, *HEAD_LAYERS["skull"])
              for i, entries in enumerate(groups)]
    measured = [body.measure(obj, True) for obj in pieces]
    for original in originals:
        bpy.data.objects.remove(original, do_unlink=True)
    for arm in (obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"):
        arm.animation_data_clear()
        for bone in arm.pose.bones:
            bone.matrix_basis.identity()
    if publish:
        gore.export(out/"SkullLongExposure.fbx")
    return {"source": SOURCE, "source_sha256": digest((ROOT/SOURCE).read_bytes()), "sector_count": 16,
            "geometry": "closed source-bound swept-volume skull layers", "source_uv0": True,
            "meshes": measured, "semantic_sha256": digest(json.dumps(measured, sort_keys=True, separators=(",", ":")).encode())}


def register_body_source(obj):
    if obj.name.startswith("GEO_"):
        body_sources.append(obj)
        return True
    return False


def entries_for(polys):
    return [(tri, query(body_sources[index])) for poly, index in polys for tri in body.tessellate(poly)]


def body_flesh(region, patch, name, source, polys, basis, bone):
    depth = .0015 if region in (7,10) else .004
    prepared = []
    entries = entries_for(polys)
    solids = {solid.name:solid for _,solid in entries}
    # Fingers remain the original thin closed anatomy. Let their measured
    # available volume bound this whole finite patch's layer, rather than
    # importing any fixed assumption about the torso's or head's thickness.
    depth = min(depth, min(solid.maximum_clearance*.55 for solid in solids.values()))
    for poly, solid in entries:
        inset = inset_triangle(poly, solid, depth*.06/.68, depth)
        if inset is not None:
            prepared.append((tuple((p[0],p[1],direction,p[3],p[4]) for p,direction in zip(inset[0],inset[1])), 0))
    obj = body.flesh_cells(name, source, prepared, basis, depth,
                           metadata=region in body.TORSO_REGIONS, point_normals=True)
    if obj is None:
        raise RuntimeError("LongExposure source-bound tissue patch has no finite cells: " + name)
    actual = [obj.matrix_world@vertex.co for vertex in obj.data.vertices]
    faces = [tuple(poly.vertices) for poly in obj.data.polygons]
    if any(not any(solid.contains(point) for solid in solids.values()) for point in triangle_samples(actual,faces)):
        raise RuntimeError("LongExposure emitted flesh cells escaped their actual source: " + name)
    edges = {tuple(sorted((a,b))) for face in faces for a,b in zip(face,face[1:]+face[:1])}
    if any(not any(solid.segment_inside(actual[a],actual[b]) for solid in solids.values()) for a,b in sorted(edges)):
        raise RuntimeError("LongExposure emitted flesh edge crosses an inter-phase source boundary: " + name)
    layer_proofs.append({"name": name, "source": source.name, "maximum_inset_m": depth,
                         "source_uv0": True, "source_skin": True,
                         "containment": "source-bound closed cells and whole edges; actual source surface"})
    return obj


def body_structure(region, source, basis, flesh):
    entries = entries_for([entry for patch in range(4) for entry in flesh.get((region,patch), [])])
    near, far = (.0025,.0045) if region in (7,10) else (.006,.009)
    far = min(far,min(solid.maximum_clearance*.55 for _,solid in entries))
    near = min(near,far*.6)
    vertices, faces, uv, skin = prism_geometry(entries, near, far, limit=48, weighted=True)
    mesh_skin_rows[f"BoneRegion{region}__{source.name}"] = skin
    layer_proofs.append({"name": f"BoneRegion{region}__{source.name}", "source": source.name,
                         "inset_range_m": [near,far], "maximum_prisms": 48, "source_uv0": True, "source_skin": True,
                         "containment": "bounded closed source-bound structural fragments"})
    return vertices, faces, uv


def erosion_targets(source, poly, basis, depth):
    global erosion_target_count
    solid = query(source)
    if (poly[1][0]-poly[0][0]).cross(poly[2][0]-poly[0][0]).length <= 1e-10:
        # Region clipping can leave a finite but microscopic surface sliver.
        # Keeping it on its original boundary is an exact, source-contained
        # zero-length erosion path, rather than inventing an unstable normal.
        targets = [point[0].copy() for point in poly]
        if not all(solid.contains(point) for point in triangle_samples(targets,[(0,1,2)])):
            raise RuntimeError("LongExposure retained sliver is outside its actual source: " + source.name)
        erosion_target_count += 1
        return targets
    inset = inset_triangle(poly, solid, 0., depth, require_path=True)
    if inset is None:
        raise RuntimeError("LongExposure exterior erosion cell has no finite source target: " + source.name)
    erosion_target_count += 1
    return inset[2][3:]


def build(out, publish, source_contract):
    global erosion_target_count
    queries.clear(); body_sources.clear(); layer_proofs.clear(); mesh_skin_rows.clear()
    erosion_target_count = 0
    gore_out, blood_out = out/"CombatGore", out/"CombatBlood"
    head = gore.build("LongExposure", gore_out, publish)
    skull = build_skull(gore_out, head, publish)
    anatomy = body.build("LongExposure", gore_out, publish, source_filter=register_body_source,
        torso_tissue_depth=.004, region_flesh_factory=body_flesh, region_bone_factory=body_structure,
        allow_empty_torso_patches=True, torso_target_factory=erosion_targets)
    patches, wounds = blood.wounds("LongExposure", blood_out, publish,
        surface_filter=lambda name: name.startswith("GEO_") and not name.startswith(("GEO_Ear", "GEO_FaceSurface")),
        required_palms={"GEO_HandPalm.L", "GEO_HandPalm.R"})
    return {"HeadLongExposure":head, "SkullLongExposure":skull, "BodyLongExposure":anatomy, "WoundsLongExposure":wounds}, patches, {
        "retained_erosion_target_cells": erosion_target_count,
        "retained_erosion_path": "whole vertex, edge-midpoint and triangle-centre segments have no source crossing; sixteen cross-sections inside source",
        "closed_sources": [queries[name].proof for name in sorted(queries)],
        "authored_sweep": source_contract["long_exposure_form"],
        "source_bound_layers": list(layer_proofs)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root", type=Path, default=ROOT/"Assets/Resources")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--inspect-head-only", action="store_true")
    parser.add_argument("--inspect-body-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if not (ROOT/SOURCE).exists() or not SOURCE_MANIFEST.exists():
        raise RuntimeError("Publish and lock LongExposureBody before deriving its damage banks")
    source_contract = json.loads(SOURCE_MANIFEST.read_text(encoding="utf-8"))
    if source_contract["design_id"] != "long_exposure_body_surface_v1":
        raise RuntimeError("LongExposure damage requires its own authored swept body")
    if args.inspect_body_only:
        result = body.build("LongExposure", args.output_root/"CombatGore", False,
            source_filter=register_body_source, torso_tissue_depth=.004,
            region_flesh_factory=body_flesh, region_bone_factory=body_structure,
            allow_empty_torso_patches=True, torso_target_factory=erosion_targets)
        print("LONG EXPOSURE BODY CLOSED SOURCE, EROSION PATHS AND SOURCE LAYERS OK; meshes="+
              str(len(result["meshes"]))+"; erosion_cells="+str(erosion_target_count), flush=True)
        return
    if args.inspect_head_only:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(ROOT/SOURCE), use_anim=False)
        source = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.name == "GEO_Head")
        solid = query(source)
        points = [source.matrix_world@v.co for v in source.data.vertices]
        low = Vector(tuple(min(p[a] for p in points) for a in range(3)))
        high = Vector(tuple(max(p[a] for p in points) for a in range(3)))
        for name,(near,far) in HEAD_LAYERS.items():
            prism_geometry([(poly, solid) for poly, _ in solid.triangles], near, far,
                           clearance=.006 if name == "soft" else 0.)
        _, proof = source_soft_parts(source, [source], (low+high)*.5, (high-low)*.5)
        print("LONG EXPOSURE HEAD CLOSED SOURCE, SOURCE LAYERS AND RETAINED STRAIN OK; clearance_m="+
              str(proof["minimum_source_clearance_m"]), flush=True)
        return
    out = args.output_root.resolve()
    manifest_path = out/"CombatGore/CombatLongExposureDamage3D.json"
    for folder in (out/"CombatGore", out/"CombatBlood"):
        folder.mkdir(parents=True, exist_ok=True)
    previous = json.loads(manifest_path.read_text(encoding="utf-8")) if args.validate_only else None
    if previous:
        for filename, expected in previous["files"].items():
            if digest((out/filename).read_bytes()) != expected:
                raise RuntimeError("LongExposure damage asset bytes changed: " + filename)
    print("LONG EXPOSURE SOURCE-BOUND DAMAGE FIRST BUILD", flush=True)
    models, patches, proof = build(out, not args.validate_only, source_contract)
    print("LONG EXPOSURE SOURCE-BOUND DAMAGE INDEPENDENT REBUILD", flush=True)
    if (models, patches, proof) != build(out, False, source_contract):
        raise RuntimeError("LongExposure damage differs across independent source rebuilds")
    manifest = {"generator":"tools/build-combat-long-exposure-damage-3d-model.py", "version":1, "test_only":True,
        "source":{"path":SOURCE,"sha256":digest((ROOT/SOURCE).read_bytes())},
        "source_manifest_sha256":digest(SOURCE_MANIFEST.read_text(encoding="utf-8").encode("utf-8")), "body_region_count":16, "patch_count":4,
        "head_sector_count":16, "brain_count":8, "surface_lift_m":.0018,
        "head_source_bound_layers_m":{name:list(depths) for name,depths in HEAD_LAYERS.items()},
        "torso_tissue_depth_m":.004, "palm_tissue_depth_m":.0015,
        "flesh_patch_count":sum(part["name"].startswith("FleshRegion") for part in models["BodyLongExposure"]["meshes"]),
        "empty_torso_flesh_patches":models["BodyLongExposure"]["empty_flesh_patches"],
        "models":models, "wound_patches":patches, "source_volume_proof":proof,
        "source_tessellation_tolerances":[dict(kind="LongExposure",**row) for row in models["BodyLongExposure"]["source_tessellation"]],
        "files":{filename:digest((out/filename).read_bytes()) for filename in FILES}}
    if args.validate_only:
        if manifest != previous:
            raise RuntimeError("LongExposure swept-volume, source UV/skin, tissue or deterministic contract changed")
        print("LONG EXPOSURE DAMAGE SOURCE UV, SKIN, CLOSED LAYERS, SWEPT VOLUME, RETAINED STRAIN AND DETERMINISM OK", flush=True)
    else:
        manifest_path.write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
        print("LONG EXPOSURE DAMAGE BANKS GENERATED AND VALIDATED", flush=True)


if __name__ == "__main__":
    main()
