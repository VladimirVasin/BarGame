"""Cylindrical hand contact on the original HeroV2 meshes and bones.

The full generator and its focused source refresh call the same authoring.
Neutral vertices, topology, skin weights and every animation stay unchanged.
"""
from __future__ import annotations

import hashlib
import copy
import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
from mathutils.geometry import closest_point_on_tri
import hand_bowl_grip

SHAPE = "CylindricalGrip"
RADIUS = .022
# The hero's distal palm station is .060 m before hand-size scaling;
# the NPC's .063 m station belongs to its slightly longer palm.
PALM_STATION = .060


def _names(side):
    return ["GEO_Hand." + side, "GEO_Thumb." + side,
            *(f"GEO_Finger{i}.{side}" for i in range(4))]


def _frame(result, side):
    bone = result.rig.data.bones["hand." + side]
    wrist = bone.head_local.copy()
    fingers = (bone.tail_local - wrist).normalized()
    across = (Vector((0, 1, 0)) - fingers * fingers.y).normalized()
    palm = fingers.cross(across).normalized() * (-1 if side == "L" else 1)
    return wrist, fingers, across, palm


def _source_points(result, obj):
    matrix = result.rig.matrix_world.inverted() @ obj.matrix_world
    return [matrix @ vertex.co for vertex in obj.data.vertices], matrix


def _shape(obj, matrix, points, name=SHAPE):
    if obj.data.shape_keys is None:
        obj.shape_key_add(name="Basis")
    key = obj.data.shape_keys.key_blocks.get(name) or obj.shape_key_add(name=name)
    inverse = matrix.inverted()
    for vertex, point in zip(key.data, points):
        vertex.co = inverse @ point
    key.value = 0


def _bend(points, old_start, old_end, centres):
    """Move each authored ring rigidly, retaining its original cross section."""
    old_axis = (old_end - old_start).normalized()
    length = (old_end - old_start).length
    fractions = sorted({round((point-old_start).dot(old_axis) / length, 5) for point in points})
    if len(fractions) != len(centres):
        raise RuntimeError("Original hand segment rings differ from the authored profile")
    bent = []
    for point in points:
        fraction = (point-old_start).dot(old_axis) / length
        ring = min(range(len(fractions)), key=lambda i: abs(fraction-fractions[i]))
        before = old_start + old_axis * (fractions[ring] * length)
        after = centres[ring]
        tangent = (centres[min(ring+1, len(centres)-1)] - centres[max(0, ring-1)]).normalized()
        bent.append(after + old_axis.rotation_difference(tangent) @ (point-before))
    return bent


def author(result, scale=1.):
    parts = {record.obj.name: record.obj for record in result.parts}
    result.authored_anchors = []
    result.grip_root_indices = {}
    for side in ("L", "R"):
        wrist, direction, across, normal = _frame(result, side)
        centre = wrist + direction * (PALM_STATION*scale) + normal * (.030*scale)
        for label, point in (("Centre", centre), ("Axis", centre-across*(.05*scale)),
                             ("Palm", centre+normal*(.05*scale))):
            name = "ANCHOR_HandGrip" + label + "." + side
            anchor = bpy.data.objects.get(name)
            if anchor is None:
                anchor = bpy.data.objects.new(name, None)
                result.root.users_collection[0].objects.link(anchor)
            anchor.parent = result.rig
            anchor.parent_type, anchor.parent_bone = "BONE", "hand." + side
            bpy.context.view_layer.update()
            anchor.matrix_world = result.rig.matrix_world @ Matrix.Translation(point)
            result.authored_anchors.append(anchor)

        palm = parts["GEO_Hand." + side]
        points, matrix = _source_points(result, palm)
        closed = []
        for point in points:
            along, depth = (point-wrist).dot(direction), (point-wrist).dot(normal)
            inset = max(0, depth-(.030*scale-RADIUS*scale-.001*scale)) if along>.038*scale else 0
            closed.append(point-normal*inset)
        _shape(palm, matrix, closed)

        # Recover each neutral straight segment from its source geometry. The
        # hull repair may reorder vertices; no operation relies on their order.
        for name in _names(side)[1:]:
            obj = parts[name]
            points, matrix = _source_points(result, obj)
            if "Finger" in name:
                index = int(name.split("Finger")[1][0])
                offset, length = ((-.023,.033),(-.007,.042),(.009,.039),(.024,.030))[index]
                start = wrist + (direction*.052 + across*offset)*1.20*scale
                end = wrist + (direction*(.052+length) + across*offset + normal*.004)*1.20*scale
                axis = (end-start).normalized()
                radius = max((point-start-axis*(point-start).dot(axis)).length for point in points)
                ring_radius = RADIUS*scale + radius + .003*scale
                fractions = (0., .42, .80, 1.)
                angles = [math.radians(30)]
                for p, q in zip(fractions, fractions[1:]):
                    angles.append(angles[-1] + 2*math.asin((end-start).length*(q-p)/(2*ring_radius)))
                # The shortest finger still reaches around the far half.
                # Move its embedded knuckle around the same contact circle;
                # stretching its phalanges would change the hero's anatomy.
                advance = max(0., math.radians(98)-angles[-1])
                angles = [angle+advance for angle in angles]
                centres = [centre + across*(offset*1.20*scale) +
                           direction*(ring_radius*math.sin(angle)) - normal*(ring_radius*math.cos(angle))
                           for angle in angles]
            else:
                def local(along, width):
                    size = 1 + .20*min(1, along/.030)
                    return wrist + (direction*along+across*width)*size*scale
                start, end = local(.018,-.022), local(.055,-.041) + normal*(.002*1.20*scale)
                length = (end-start).length
                middle_hint = wrist + (direction*.028 + across*-.041 + normal*.024)*scale
                tip_hint = wrist + (direction*.032 + across*-.042 + normal*.052)*scale
                middle = start + (middle_hint-start).normalized()*(length*.45)
                tip = middle + (tip_hint-middle).normalized()*(length*.55)
                centres = [start, middle, tip]
            if "Finger" in name:
                old_axis=(end-start).normalized()
                fractions=[(point-start).dot(old_axis) for point in points]
                minimum=min(fractions)
                result.grip_root_indices[name]=[i for i,value in enumerate(fractions) if value-minimum<1e-6]
            _shape(obj, matrix, _bend(points, start, end, centres))
    author_bowls(result, scale)
    bpy.context.view_layer.update()
    return validate(result, scale)


def author_bowls(result, scale=1.):
    parts = {record.obj.name: record.obj for record in result.parts}
    result.bowl_grip_segments = []
    for shape_name, radius in hand_bowl_grip.VARIANTS:
        radius *= scale
        for side in ("L", "R"):
            wrist, direction, across, normal = _frame(result, side)
            centre = wrist + direction*(PALM_STATION*scale) + normal*(radius+.008*scale)
            for label, point in (("Centre", centre), ("Axis", centre-across*(.05*scale)),
                                 ("Palm", centre+normal*(.05*scale))):
                name = "ANCHOR_"+shape_name+label+"."+side
                anchor = bpy.data.objects.get(name)
                if anchor is None:
                    anchor = bpy.data.objects.new(name, None)
                    result.root.users_collection[0].objects.link(anchor)
                anchor.parent = result.rig
                anchor.parent_type, anchor.parent_bone = "BONE", "hand."+side
                bpy.context.view_layer.update()
                anchor.matrix_world = result.rig.matrix_world @ Matrix.Translation(point)
                result.authored_anchors.append(anchor)
            palm = parts["GEO_Hand."+side]
            points, matrix = _source_points(result, palm)
            tangent = (centre-wrist).dot(normal)-radius-.001*scale
            closed = [point-normal*max(0., (point-wrist).dot(normal)-tangent)
                      if (point-wrist).dot(direction)>.038*scale else point for point in points]
            _shape(palm, matrix, closed, shape_name)
            for name in _names(side)[1:]:
                obj = parts[name]
                points, matrix = _source_points(result, obj)
                if "Finger" in name:
                    index = int(name.split("Finger")[1][0])
                    offset, length = ((-.023,.033),(-.007,.042),(.009,.039),(.024,.030))[index]
                    start = wrist+(direction*.052+across*offset)*1.20*scale
                    end = wrist+(direction*(.052+length)+across*offset+normal*.004)*1.20*scale
                    axis = (end-start).normalized()
                    thickness = max((point-start-axis*(point-start).dot(axis)).length for point in points)
                    neutral = [start+(end-start)*fraction for fraction in (0.,.42,.80,1.)]
                    centres = hand_bowl_grip.finger_centres(neutral,centre,direction,normal,radius,thickness)
                else:
                    def local(along, width):
                        size = 1+.20*min(1,along/.030)
                        return wrist+(direction*along+across*width)*size*scale
                    start = local(.018,-.022)
                    end = local(.055,-.041)+normal*(.002*1.20*scale)
                    axis = (end-start).normalized()
                    thickness = max((point-start-axis*(point-start).dot(axis)).length for point in points)*.70
                    neutral = [start,start+(end-start)*.45,end]
                    centres = hand_bowl_grip.thumb_centres(neutral,centre,direction,normal,radius,thickness)
                _shape(obj,matrix,_bend(points,start,end,centres),shape_name)
                result.bowl_grip_segments.append((shape_name,name,neutral,centres))


def _triangle_axis_distance(triangle):
    p, q, r = triangle
    def cross(a, b):
        return a.x*b.y-a.y*b.x
    signs = [cross(p,q),cross(q,r),cross(r,p)]
    if abs(cross(q-p,r-p))>1e-12 and (min(signs)>=0 or max(signs)<=0):
        return 0.
    def segment(a,b):
        edge=b-a
        t=max(0,min(1,-a.dot(edge)/edge.length_squared)) if edge.length_squared>1e-14 else 0
        return (a+edge*t).length
    return min(segment(p,q),segment(q,r),segment(r,p))


def _outside_surface_gap(point, triangles):
    candidates = []
    for a,b,c in triangles:
        nearest = closest_point_on_tri(point,a,b,c)
        delta = point-nearest
        candidates.append((delta.length,delta.dot((b-a).cross(c-a))))
    distance, signed = min(candidates,key=lambda item:item[0])
    return distance if signed>0 else 0.


def validate(result, scale=1.):
    parts = {row.obj.name: row.obj for row in result.parts}
    clearances, far_sides, root_gaps = {}, {}, {}
    for side in ("L", "R"):
        wrist, direction, across, normal = _frame(result, side)
        centre = wrist + direction*(PALM_STATION*scale) + normal*(.030*scale)
        palm=parts["GEO_Hand."+side]
        _,palm_matrix=_source_points(result,palm)
        palm_points=[palm_matrix @ vertex.co for vertex in palm.data.shape_keys.key_blocks[SHAPE].data]
        palm_triangles=[tuple(palm_points[p.vertices[j]] for j in (0,i,i+1))
                        for p in palm.data.polygons for i in range(1,len(p.vertices)-1)]
        for name in _names(side):
            obj = parts[name]
            keys = obj.data.shape_keys.key_blocks
            joint_keys={row["corrective_shape"] for row in getattr(result,"joint_surfaces",{}).get("seams",())
                        if name in row["renderers"] and row.get("corrective_shape")}
            if set(keys.keys()) != {"Basis",SHAPE,*(row[0] for row in hand_bowl_grip.VARIANTS),*joint_keys} or any(key.value!=0 for key in keys if key.name!="Basis"):
                raise RuntimeError("Missing neutral authored hand shape: " + name)
            if any((vertex.co-keys["Basis"].data[vertex.index].co).length>1e-8 for vertex in obj.data.vertices):
                raise RuntimeError("Hand grip changed neutral geometry: " + name)
            _, matrix = _source_points(result, obj)
            points = [matrix @ vertex.co for vertex in keys[SHAPE].data]
            volume = sum(points[p.vertices[0]].dot(points[p.vertices[i]].cross(points[p.vertices[i+1]]))/6
                         for p in obj.data.polygons for i in range(1,len(p.vertices)-1))
            if volume <= 0:
                raise RuntimeError("Hand grip winding flipped: " + name)
            projected = [Vector(((point-centre).dot(direction),(point-centre).dot(normal))) for point in points]
            gap = min(_triangle_axis_distance([projected[p.vertices[j]] for j in (0,i,i+1)])
                      for p in obj.data.polygons for i in range(1,len(p.vertices)-1)) - RADIUS*scale
            clearances[name] = round(gap,6)
            if gap < -.0025*scale or gap > .008*scale:
                raise RuntimeError(f"Hand grip misses cylinder: {name}, {gap:.6f} m")
            if "Finger" in name or "Thumb" in name:
                far = max((point-centre).dot(normal) for point in points)
                far_sides[name] = round(far,6)
                if far <= .004*scale:
                    raise RuntimeError(f"Hand digit does not wrap the cylinder: {name}, {far:.6f} m")
            if "Finger" in name:
                gap=min(_outside_surface_gap(points[i],palm_triangles)
                        for i in result.grip_root_indices[name])
                root_gaps[name]=round(gap,6)
                if gap > .003*scale:
                    raise RuntimeError(f"Hand finger root separates from palm: {name}, {gap:.6f} m")
            if name.startswith("GEO_Hand"):
                neutral, _ = _source_points(result,obj)
                if any((points[i]-point).length>1e-7 for i,point in enumerate(neutral)
                       if (point-wrist).dot(direction)<=.038*scale):
                    raise RuntimeError("Grip moved the wrist: " + name)
    bowls = validate_bowls(result,scale,parts)
    return {"surface_clearances_m":clearances,"far_side_reach_m":far_sides,**bowls,
            "finger_root_surface_gaps_m":root_gaps,
            "neutral_vertices_unchanged":True,"wrist_unchanged":True}


def validate_bowls(result,scale,parts):
    clearances,root_gaps={},{}
    for shape_name,radius in hand_bowl_grip.VARIANTS:
        for side in ("L","R"):
            wrist,direction,across,normal=_frame(result,side)
            centre=wrist+direction*(PALM_STATION*scale)+normal*((radius+.008)*scale)
            palm=parts["GEO_Hand."+side]
            _,matrix=_source_points(result,palm)
            palm_points=[matrix@vertex.co for vertex in palm.data.shape_keys.key_blocks[shape_name].data]
            triangles=[tuple(palm_points[p.vertices[j]] for j in (0,i,i+1))
                       for p in palm.data.polygons for i in range(1,len(p.vertices)-1)]
            for name in _names(side):
                obj=parts[name];_,matrix=_source_points(result,obj)
                points=[matrix@vertex.co for vertex in obj.data.shape_keys.key_blocks[shape_name].data]
                projected=[Vector(((point-centre).dot(direction),(point-centre).dot(normal))) for point in points]
                gap=min(_triangle_axis_distance([projected[p.vertices[j]] for j in (0,i,i+1)])
                        for p in obj.data.polygons for i in range(1,len(p.vertices)-1))-radius*scale
                clearances[shape_name+":"+name]=round(gap,6)
                if not -.0025*scale <= gap <= .008*scale:
                    raise RuntimeError(f"Bowl grip misses cylinder: {shape_name} {name} {gap:.6f} m")
                if "Finger" in name:
                    gap=min(_outside_surface_gap(points[i],triangles) for i in result.grip_root_indices[name])
                    root_gaps[shape_name+":"+name]=round(gap,6)
                    if gap>.003*scale:raise RuntimeError(f"Bowl finger root separates from palm: {shape_name} {name} {gap:.6f} m")
                if name.startswith("GEO_Hand"):
                    neutral,_=_source_points(result,obj)
                    if any((points[i]-point).length>1e-7 for i,point in enumerate(neutral)
                           if (point-wrist).dot(direction)<=.038*scale):
                        raise RuntimeError("Bowl grip moved the wrist: "+name)
    segment_error=max((abs((before[i+1]-before[i]).length-(after[i+1]-after[i]).length)
                      for _,_,before,after in result.bowl_grip_segments for i in range(len(before)-1)),default=0.)
    if segment_error>1e-6:raise RuntimeError("Bowl grip stretches original phalange lengths")
    return {"bowl_surface_clearances_m":clearances,"bowl_finger_root_surface_gaps_m":root_gaps,
            "bowl_maximum_segment_length_error_m":round(segment_error,8)}


def manifest(result, scale=1.):
    if not getattr(result,"authored_anchors",None):
        return None
    rows = {row.obj.name: row.obj for row in result.parts}
    hands = []
    for side in ("L","R"):
        hands.append({"side":side,"bone":"hand."+side,
                      **{label.lower()+"_anchor":"ANCHOR_HandGrip"+label+"."+side
                         for label in ("Centre","Axis","Palm")},"renderers":_names(side)})
    payload = {"shape_name":SHAPE,"cylinder_radius_m":RADIUS*scale,"hands":hands}
    content = {"shapes":{name:[[round(float(c),7) for c in vertex.co]
                      for vertex in rows[name].data.shape_keys.key_blocks[SHAPE].data]
                      for side in ("L","R") for name in _names(side)},
               "anchors":{obj.name:[[round(float(v),7) for v in row] for row in obj.matrix_local]
                          for obj in result.authored_anchors}}
    payload["shape_signature"] = hashlib.sha256(json.dumps(content,sort_keys=True).encode()).hexdigest()
    variants=[]
    for shape_name,radius in hand_bowl_grip.VARIANTS:
        if shape_name not in rows[_names("L")[0]].data.shape_keys.key_blocks:
            continue
        variant_hands=[{"side":side,"bone":"hand."+side,
                        **{label.lower()+"_anchor":"ANCHOR_"+shape_name+label+"."+side
                           for label in ("Centre","Axis","Palm")},"renderers":_names(side)}
                       for side in ("L","R")]
        geometry={name:[[round(float(c),7) for c in vertex.co]
                       for vertex in rows[name].data.shape_keys.key_blocks[shape_name].data]
                  for side in ("L","R") for name in _names(side)}
        variants.append({"shape_name":shape_name,"cylinder_radius_m":radius*scale,
                         "hands":variant_hands,"shape_signature":hashlib.sha256(
                             json.dumps(geometry,sort_keys=True).encode()).hexdigest()})
    if variants:payload["variants"]=variants
    return payload


def refresh(module, config):
    """Refresh only model contact shapes from a verified complete source file."""
    original = json.loads(module.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
    preserved_manifest=copy.deepcopy(original)
    bpy.ops.wm.open_mainfile(filepath=str(module.DEFAULT_OUTPUT))
    rig = next(obj for obj in bpy.data.objects if obj.type=="ARMATURE")
    rig.animation_data_create().action=None
    parts = [module.common.PartRecord(bpy.data.objects[row["name"]],row["role"],row["bone"],row["sprite_part"],row["side"])
             for row in original["parts"]]
    # The flat part manifest is alphabetical, while wardrobe/hair catalogs
    # preserve authoring order and participate in the content signature.
    ordered = [name for item in original["wardrobe"]["items"] for name in item["renderers"]]
    ordered += [name for chain in original["hair"]["chains"] for name in chain["renderers"]]
    order = {name:index for index,name in enumerate(ordered)}
    parts.sort(key=lambda part:order.get(part.obj.name,len(order)))
    actions = {row["name"]:module.common.ActionRecord(bpy.data.actions[row["name"]],row["category"],row["duration_seconds"],
                row["loop"],row["source_frame_count"],row["source_fps"]) for row in original["actions"]}
    result=module.common.BuildResult(rig.parent,rig,{}, {},parts=parts,actions=actions)
    # BuildResult is reconstructed from the saved source, so restore the
    # existing authored joint records too. They participate in the complete
    # content signature; the action-bank proof flag was added after hashing.
    if original.get("joint_surfaces"):
        result.joint_surfaces=copy.deepcopy(original["joint_surfaces"])
        result.joint_surfaces.pop("preserved_action_bank",None)
    result.authored_anchors=[obj for obj in bpy.data.objects if obj.name.startswith(
        ("ANCHOR_HandGrip","ANCHOR_WineBowlGrip","ANCHOR_SnifterBowlGrip"))]
    builder=module.HeroV2Builder(config,module.DEFAULT_FACE_ATLAS,module.DEFAULT_CLOTHING_ATLAS)
    builder.result=result
    builder._reset_pose()
    hashes=(original["face_atlas"]["sha256"],original["texture_bindings"][0]["sha256"],original["bare_skin_atlas"]["sha256"])
    signature=lambda:module.content_signature(config,result,*hashes)
    if signature()!=original["content_signature_sha256"]:
        raise RuntimeError("Production hero source differs from its manifest; refusing a stale source refresh: " + signature())
    preserved_morphs=_non_hand_morphs(result)
    # The existing signature covers all basis meshes, topology, weights,
    # materials, bones and curves. Removing only the new contact record later
    # must reproduce it exactly.
    previous_anchors=result.authored_anchors
    result.authored_anchors=[]
    before=signature()
    result.authored_anchors=previous_anchors
    measured=author(result,config.height/1.75)
    first=manifest(result,config.height/1.75)
    author(result,config.height/1.75)
    if first!=manifest(result,config.height/1.75):
        raise RuntimeError("Hand shapes or anchors are nondeterministic")
    preserved=result.authored_anchors
    result.authored_anchors=[]
    without_grip=signature()
    result.authored_anchors=preserved
    if without_grip!=before:
        raise RuntimeError("Hand refresh altered the original hero basis, rig or animation bank")
    if _non_hand_morphs(result)!=preserved_morphs:
        raise RuntimeError("Hand refresh altered an existing non-hand morph")
    original["object_count"]=len(result.export_objects)
    original["hand_grip"]={**first,"validation":measured}
    original["hand_grip_authoring_sha256"]=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    original["content_signature_sha256"]=signature()
    changed_fields={"object_count","hand_grip","hand_grip_authoring_sha256","content_signature_sha256"}
    if {k:v for k,v in original.items() if k not in changed_fields}!={k:v for k,v in preserved_manifest.items() if k not in changed_fields}:
        raise RuntimeError("Hand refresh changed unrelated production manifest metadata")
    result.root["bp_content_signature_sha256"]=original["content_signature_sha256"]
    bpy.context.scene["bp_content_signature_sha256"]=original["content_signature_sha256"]
    # Keep the production hero's joint-surface modifier/custom-normal policy.
    # A generic writer changes imported split vertices outside the hand meshes
    # and invalidates the prefab's authored cloth vertex bindings.
    module.export_model_fbx(config.fbx,result)
    bpy.context.preferences.filepaths.save_version=0
    module.common.save_blend(config.output)
    config.manifest.parent.mkdir(parents=True,exist_ok=True)
    config.manifest.write_text(json.dumps(original,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    validate_export(config.fbx, first, preserved_morphs)
    print("Hero cylinder/bowl grips: deterministic shapes/anchors; unchanged neutral mesh, rig and actions",flush=True)
    print(json.dumps(measured),flush=True)


def _non_hand_morphs(result):
    result_profiles={}
    hand_names={name for side in ("L","R") for name in _names(side)}
    for record in result.parts:
        obj=record.obj
        if obj.name in hand_names or obj.data.shape_keys is None:continue
        keys=obj.data.shape_keys.key_blocks
        matrix=obj.matrix_world.to_3x3()
        result_profiles[obj.name]={key.name:round(max((matrix@(vertex.co-base.co)).length
                                                    for vertex,base in zip(key.data,keys[0].data)),7)
                                   for key in keys if key.name!="Basis"}
    return result_profiles


def validate_export(path, catalog, preserved_morphs=None):
    """Published model must carry the shapes through the actual FBX writer."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    for profile in (catalog,*catalog.get("variants",())):
        for hand in profile["hands"]:
            for name in hand["renderers"]:
                obj = bpy.data.objects.get(name)
                keys = obj.data.shape_keys.key_blocks if obj is not None and obj.type == "MESH" and obj.data.shape_keys else None
                if keys is None or profile["shape_name"] not in keys:
                    raise RuntimeError("Published hero FBX lost its grip shape: " + name)
                if max((a.co-b.co).length for a,b in zip(keys[0].data,keys[profile["shape_name"]].data)) < .001:
                    raise RuntimeError("Published hero FBX grip has no deformation: " + name)
            for label in ("centre_anchor","axis_anchor","palm_anchor"):
                if bpy.data.objects.get(hand[label]) is None:
                    raise RuntimeError("Published hero FBX lost a grip anchor: " + hand[label])
    for name,expected in (preserved_morphs or {}).items():
        obj=bpy.data.objects.get(name)
        keys=obj.data.shape_keys.key_blocks if obj is not None and obj.type=="MESH" and obj.data.shape_keys else None
        if keys is None or set(keys.keys())!={"Basis",*expected}:
            raise RuntimeError("Published hero FBX lost an existing non-hand morph: "+name)
        matrix=obj.matrix_world.to_3x3()
        for shape,magnitude in expected.items():
            actual=max((matrix@(vertex.co-base.co)).length for vertex,base in zip(keys[shape].data,keys[0].data))
            if abs(actual-magnitude)>1e-4:
                raise RuntimeError(f"Published hero FBX changed non-hand morph magnitude: {name} {shape} {actual} vs {magnitude}")
    print("Hero FBX round trip: cylinder and bowl hand shapes/anchors present", flush=True)
