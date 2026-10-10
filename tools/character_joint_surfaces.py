"""Deterministic, separately addressable character regions sharing a deforming joint.

Region caps may stay closed for independent coverage/contact use. Their perimeter
is one authored ring, copied with identical weights and normals into both regions;
it is never two rigid ends that merely overlap. Existing topology and UVs survive.
"""
from __future__ import annotations

import math
import bpy
from mathutils import Matrix, Quaternion, Vector

CONTRACT = "character_joint_surfaces_v1"
MAX_ANGLE = 135.0


def _data(result):
    if not hasattr(result, "joint_surfaces"):
        result.joint_surfaces = {
            "contract": CONTRACT, "source_space": "blender_z_up_minus_y_forward",
            "max_influences": 4, "seams": [], "surfaces": [], "attachments": [],
        }
    return result.joint_surfaces


def _point(obj, index):
    return obj.data.vertices[index].co + obj.location


def _move(obj, index, point):
    delta=point-_point(obj,index)
    obj.data.vertices[index].co += delta
    if obj.data.shape_keys:
        for key in obj.data.shape_keys.key_blocks:
            key.data[index].co += delta


def terminal_ring(obj, axis, endpoint="start"):
    """Find a planar end ring along an authored bone direction, in angular order."""
    direction = Vector(axis).normalized()
    distances = [_point(obj, v.index).dot(direction) for v in obj.data.vertices]
    extreme = min(distances) if endpoint == "start" else max(distances)
    ids = [i for i, distance in enumerate(distances) if abs(distance-extreme) < 1e-5]
    # Authored overlapping sleeves may have a slightly different axis from the
    # bone. Infer the actual terminal cap plane rather than losing its ring to
    # one numerically extreme point; this also handles triangulated convex caps.
    candidates=[p for p in obj.data.polygons if abs(p.normal.dot(direction))>.8]
    if candidates:
        rank=lambda p:(p.center+obj.location).dot(direction)
        cap=min(candidates,key=rank) if endpoint=="start" else max(candidates,key=rank)
        plane=cap.center+obj.location
        ids=[v.index for v in obj.data.vertices if abs((_point(obj,v.index)-plane).dot(cap.normal))<1e-5]
    if len(ids) < 3:
        raise ValueError(obj.name + " has no planar " + endpoint + " joint ring")
    centre = sum((_point(obj, i) for i in ids), Vector()) / len(ids)
    across = direction.cross(Vector((0, 1, 0)))
    if across.length_squared < 1e-8:
        across = direction.cross(Vector((1, 0, 0)))
    across.normalize(); normal = direction.cross(across).normalized()
    return sorted(ids, key=lambda i: math.atan2((_point(obj, i)-centre).dot(normal),
                                             (_point(obj, i)-centre).dot(across)))


def _weights(obj, index):
    names = {g.index: g.name for g in obj.vertex_groups}
    return {names[g.group]: g.weight for g in obj.data.vertices[index].groups if g.weight > 1e-7}


def _assign(obj, index, weights):
    for group in obj.vertex_groups:
        group.remove([index])
    total = sum(weights.values())
    for name, value in sorted(weights.items()):
        if value <= 1e-7:
            continue
        group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        group.add([index], value/total, "REPLACE")


def _surface(result, obj):
    obj["bp_joint_surface"] = CONTRACT
    rows = _data(result)["surfaces"]
    names = sorted({name for vertex in obj.data.vertices for name in _weights(obj, vertex.index)})
    row = next((r for r in rows if r["name"] == obj.name), None)
    if row is None:
        rows.append({"name": obj.name, "bones": names})
    else:
        row["bones"] = names


def _smooth(value):
    t = max(0.0, min(1.0, value))
    return t*t*(3.0-2.0*t)


def blend_attachment(result, obj, bone, parent_bone, *, name, band_m=.06, endpoint="start"):
    """Blend an existing overlapping attachment; does not claim a welded seam."""
    rig = result.rig
    source = rig.data.bones[bone]
    direction = (source.tail_local-source.head_local).normalized()
    pivot = source.head_local if endpoint == "start" else source.tail_local
    for vertex in obj.data.vertices:
        along = (_point(obj, vertex.index)-pivot).dot(direction)
        distance = along if endpoint == "start" else -along
        mix = .5*_smooth(1.0-max(0.0, distance)/band_m)
        if mix > 0:
            current = _weights(obj, vertex.index)
            weights = {n: w*(1-mix) for n, w in current.items()}
            weights[parent_bone] = weights.get(parent_bone, 0)+mix
            _assign(obj, vertex.index, weights)
    _surface(result, obj)
    _data(result)["attachments"].append({"id": name, "name": obj.name,
        "bone": bone, "parent_bone": parent_bone, "band_m": band_m, "endpoint": endpoint})


def join_ring_seam(result, obj_a, ring_a, obj_b, ring_b, bone_a, bone_b, *,
                   name, band_m, loose=False, corrective_name=None):
    """Use the distal region's unchanged ring for both sides of one joint band.

    Call after detail replacements, before attachment fields and normal/export
    processing. Explicit source IDs preserve UV strips and all named renderers.
    """
    if len(ring_a) != len(ring_b) or len(ring_a) < 3:
        raise ValueError(name + " needs matching tessellation on both joint rings")
    pivot = result.rig.data.bones[bone_b].head_local.copy()
    # Correspondence minimizes angular mismatch, allowing either source winding.
    candidates = [list(order[s:])+list(order[:s]) for order in (ring_b, list(reversed(ring_b)))
                  for s in range(len(ring_b))]
    paired_b = min(candidates, key=lambda ids: sum((_point(obj_a, a)-_point(obj_b, b)).length_squared
                                                  for a, b in zip(ring_a, ids)))
    points = [_point(obj_b, i).copy() for i in paired_b]
    # Canonical seam stays on the distal authored pivot, not an overlap offset.
    centre = sum(points, Vector())/len(points)
    direction_b = (result.rig.data.bones[bone_b].tail_local-pivot).normalized()
    points = [p-direction_b*((p-pivot).dot(direction_b)) for p in points]
    for a, b, point in zip(ring_a, paired_b, points):
        _move(obj_a,a,point)
        _move(obj_b,b,point)
    direction_a = (pivot-result.rig.data.bones[bone_a].head_local).normalized()
    for obj, direction, proximal in ((obj_a, direction_a, True), (obj_b, direction_b, False)):
        for vertex in obj.data.vertices:
            along = (_point(obj, vertex.index)-pivot).dot(direction)
            distance = max(0.0, -along if proximal else along)
            mix = .5*_smooth(1.0-distance/band_m)
            if mix <= 1e-7:
                continue  # Preserve another joint's field on this same region.
            weights = {bone_a: 1-mix, bone_b: mix} if proximal else {bone_a: mix, bone_b: 1-mix}
            _assign(obj, vertex.index, weights)
    for a, b in zip(ring_a, paired_b):
        _assign(obj_a, a, {bone_a:.5, bone_b:.5})
        _assign(obj_b, b, {bone_a:.5, bone_b:.5})
    for obj in (obj_a, obj_b):
        obj.data.update(); _surface(result, obj)
    row = {"id": name, "bone": bone_b, "renderers": [obj_a.name, obj_b.name],
           "vertices_a": list(ring_a), "vertices_b": paired_b, "bones": [bone_a, bone_b],
           "points_blender": [dict(zip(("x","y","z"), map(float,p))) for p in points],
           "band_m": band_m, "loose": bool(loose), "corrective_shape": corrective_name or "",
           "corrective_angle_degrees": MAX_ANGLE if corrective_name else 0.,
           "weight_curve": "inverse_half_angle_cosine" if corrective_name else "none"}
    _data(result)["seams"].append(row)
    if corrective_name:
        _corrective(result, row)
    reconcile_normals(result)
    return row


def _corrective(result, row):
    """Authored loose cloth volume, distributed through its own broad elbow band."""
    parts = {part.obj.name: part.obj for part in result.parts}
    rig = result.rig
    a, b = row["bones"]
    pivot = rig.data.bones[b].head_local.copy()
    hinge=(rig.data.bones[b].matrix_local.to_3x3()@Vector((1,0,0))).normalized()
    for name, proximal in dict(zip(row["renderers"], (True, False))).items():
        obj = parts[name]
        if obj.data.shape_keys is None:
            obj.shape_key_add(name="Basis")
        key = obj.shape_key_add(name=row["corrective_shape"])
        axis = ((pivot-rig.data.bones[a].head_local) if proximal else
                (rig.data.bones[b].tail_local-pivot)).normalized()
        for vertex, target in zip(obj.data.vertices, key.data):
            point = _point(obj, vertex.index)
            if row.get("topology")=="continuous":
                upper_axis=(pivot-rig.data.bones[a].head_local).normalized()
                axis=upper_axis if (point-pivot).dot(upper_axis)<0 else (rig.data.bones[b].tail_local-pivot).normalized()
            along = (point-pivot).dot(axis)
            radial = point-(pivot+axis*along)
            compressed=radial-hinge*radial.dot(hinge)
            field = _smooth(1-abs(along)/row["band_m"])
            weights=_weights(obj,vertex.index)
            pair=weights.get(a,0)+weights.get(b,0)
            fraction=weights.get(b,0)/pair if pair>0 else 0
            compression=math.sqrt(max(.01,1-4*fraction*(1-fraction)*math.sin(math.radians(MAX_ANGLE)*.5)**2))
            target.co = vertex.co+compressed*(1/compression-1) if field>0 else vertex.co
        key.value = 0
    # Shape seam must obey the same equality as its Basis, despite tiny A-pose
    # differences between proximal and distal axes.
    obj_a, obj_b = [parts[n] for n in row["renderers"]]
    key_a = obj_a.data.shape_keys.key_blocks[row["corrective_shape"]]
    key_b = obj_b.data.shape_keys.key_blocks[row["corrective_shape"]]
    for ia, ib in zip(row["vertices_a"], row["vertices_b"]):
        key_a.data[ia].co = key_b.data[ib].co+obj_b.location-obj_a.location


def correct_continuous_joint(result,obj,bone_a,bone_b,*,name,band_m,corrective_name,ring=None):
    """Give an already continuous skinned limb the same measured volume field.

    Existing topology/weights are retained. A self seam declares the measured
    transition cross-section without inventing a second renderer or a boundary.
    """
    if ring is None:
        candidates=[]
        for vertex in obj.data.vertices:
            weights=_weights(obj,vertex.index)
            if weights.get(bone_a,0)>0 and weights.get(bone_b,0)>0:
                fraction=weights[bone_b]/(weights[bone_a]+weights[bone_b])
                candidates.append((abs(fraction-.5),round(fraction,5),vertex.index))
        if not candidates:
            raise ValueError(obj.name+" has no existing blended "+name+" section")
        chosen=min(candidates)[1]
        ring=[index for _,fraction,index in candidates if fraction==chosen]
        axis=(result.rig.data.bones[bone_b].tail_local-result.rig.data.bones[bone_b].head_local).normalized()
        centre=sum((_point(obj,i) for i in ring),Vector())/len(ring)
        across=axis.cross(Vector((0,1,0)))
        if across.length_squared<1e-8:across=axis.cross(Vector((1,0,0)))
        across.normalize();normal=axis.cross(across).normalized()
        ring.sort(key=lambda i:math.atan2((_point(obj,i)-centre).dot(normal),(_point(obj,i)-centre).dot(across)))
    points=[_point(obj,i) for i in ring]
    row={"id":name,"bone":bone_b,"renderers":[obj.name,obj.name],"topology":"continuous",
         "vertices_a":list(ring),"vertices_b":list(ring),"bones":[bone_a,bone_b],
         "points_blender":[dict(zip(("x","y","z"),map(float,p))) for p in points],
         "band_m":band_m,"loose":False,"corrective_shape":corrective_name,
         "corrective_angle_degrees":MAX_ANGLE,"weight_curve":"inverse_half_angle_cosine"}
    _data(result)["seams"].append(row);_surface(result,obj);_corrective(result,row)
    return row


def reconcile_normals(result):
    """Shared radial shading basis survives independent renderer import."""
    parts = {part.obj.name:part.obj for part in result.parts}
    normals = {name:[v.normal.copy() for v in parts[name].data.vertices]
               for row in _data(result)["seams"] for name in row["renderers"]}
    for row in _data(result)["seams"]:
        a,b = row["renderers"]
        for ia,ib in zip(row["vertices_a"], row["vertices_b"]):
            normal = (normals[a][ia]+normals[b][ib]).normalized()
            normals[a][ia]=normal; normals[b][ib]=normal
    for name,values in normals.items():
        parts[name].data.normals_split_custom_set_from_vertices(values)


def manifest(result):
    data = _data(result)
    for part in result.parts:
        if part.obj.get("bp_joint_surface"):
            _surface(result, part.obj)
    return data


def _section_area(points):
    centre=sum(points,Vector())/len(points)
    return .5*sum(((points[i]-centre).cross(points[(i+1)%len(points)]-centre)
                   for i in range(len(points))),Vector()).length


def validate(result, errors):
    """Measure actual paired skinned points through the supported bend range."""
    parts = {p.obj.name:p.obj for p in result.parts}
    rig = result.rig
    basis = {b.name:b.matrix_basis.copy() for b in rig.pose.bones}
    action = rig.animation_data.action if rig.animation_data else None
    try:
        if rig.animation_data:
            rig.animation_data.action = None
        for row in _data(result)["seams"]:
            a,b = [parts[n] for n in row["renderers"]]
            original_area=_section_area([_point(b,i) for i in row["vertices_b"]])
            ratios=[]
            for angle in (0.,45.,90.,MAX_ANGLE):
                for bone in rig.pose.bones:
                    bone.matrix_basis = Matrix.Identity(4)
                rig.pose.bones[row["bone"]].rotation_mode="QUATERNION"
                rig.pose.bones[row["bone"]].rotation_quaternion=Quaternion((1,0,0),math.radians(angle))
                bpy.context.view_layer.update()
                matrices = {bone.name:bone.matrix@bone.bone.matrix_local.inverted() for bone in rig.pose.bones}
                factor = ((1/math.cos(math.radians(angle)*.5)-1)/
                          (1/math.cos(math.radians(MAX_ANGLE)*.5)-1)) if row["corrective_shape"] else 0.
                def posed(obj, index):
                    point = _point(obj,index)
                    if factor:
                        key = obj.data.shape_keys.key_blocks[row["corrective_shape"]]
                        point += (key.data[index].co-obj.data.vertices[index].co)*factor
                    return sum((matrices[name]@point*weight for name,weight in _weights(obj,index).items()),Vector())
                worst = max((posed(a,ia)-posed(b,ib)).length for ia,ib in zip(row["vertices_a"],row["vertices_b"]))
                ratio=_section_area([posed(b,i) for i in row["vertices_b"]])/original_area
                ratios.append(ratio)
                if worst > 2e-6:
                    errors.append(f"{row['id']} opens {worst:.8f} m at {angle:g} degrees")
                if row["corrective_shape"] and not .85<=ratio<=1.15:
                    errors.append(f"{row['id']} section area is {ratio:.4f} of bind at {angle:g} degrees")
            row["validation"]={"angles_degrees":[0,45,90,MAX_ANGLE],
                "section_area_ratios":[round(value,6) for value in ratios],"seam_tolerance_m":.000002}
            for ia,ib in zip(row["vertices_a"],row["vertices_b"]):
                if _weights(a,ia) != _weights(b,ib):
                    errors.append(row["id"]+" has different seam weights")
        for row in manifest(result)["surfaces"]:
            obj = parts[row["name"]]
            for vertex in obj.data.vertices:
                weights = _weights(obj,vertex.index)
                if len(weights)>4 or abs(sum(weights.values())-1)>1e-5:
                    errors.append(obj.name+" has invalid joint skin weights"); break
    finally:
        for bone in rig.pose.bones:
            bone.matrix_basis=basis[bone.name]
        if rig.animation_data:
            rig.animation_data.action=action
        bpy.context.view_layer.update()
