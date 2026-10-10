#!/usr/bin/env python3
"""Verify the actual exported FBX carries declared joint surfaces and shapes."""
from __future__ import annotations
import argparse
import json
import math
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Matrix,Quaternion,Vector

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("--model",type=Path,required=True)
parser.add_argument("--manifest",type=Path,required=True)
parser.add_argument("--validate-only",action="store_true")
args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
manifest=json.loads(args.manifest.read_text(encoding="utf-8"))
payload=manifest["joint_surfaces"]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(args.model.resolve()),use_anim=False)
rig=next(obj for obj in bpy.data.objects if obj.type=="ARMATURE")
parts={obj.name:obj for obj in bpy.data.objects if obj.type=="MESH"}
rig_inverse=rig.matrix_world.inverted()
errors=[]
def section_area(points):
    centre=sum(points,Vector())/len(points)
    return .5*sum(((points[i]-centre).cross(points[(i+1)%len(points)]-centre)
                   for i in range(len(points))),Vector()).length
for row in payload["seams"]:
    matched=[]
    for name in row["renderers"]:
        obj=parts[name];to_rig=rig_inverse@obj.matrix_world
        points=[to_rig@vertex.co for vertex in obj.data.vertices]
        allowed=set(row["bones"])
        indices=[]
        for coordinate in row["points_blender"]:
            point=Vector(tuple(coordinate[k] for k in ("x","y","z")))
            index=min(range(len(points)),key=lambda i:(points[i]-point).length_squared)
            if (points[index]-point).length>2e-5:
                errors.append(row["id"]+" exported point missing on "+name)
            indices.append(index)
            weights={obj.vertex_groups[g.group].name:g.weight for g in obj.data.vertices[index].groups if g.weight>1e-7}
            if set(weights)-allowed or len(weights)>4 or abs(sum(weights.values())-1)>1e-5:
                errors.append(row["id"]+" exported seam skin is outside its declared normalized bone palette")
        key=row.get("corrective_shape")
        if key and (not obj.data.shape_keys or key not in obj.data.shape_keys.key_blocks):
            errors.append(row["id"]+" exported corrective missing on "+name)
        matched.append((obj,to_rig,indices))
    if row.get("topology")!="continuous":
        normal_rows=[]
        for obj,to_rig,indices in matched:
            normals={index:[] for index in indices}
            normal_matrix=to_rig.to_3x3().inverted().transposed()
            for loop,normal in zip(obj.data.loops,obj.data.corner_normals):
                if loop.vertex_index in normals:
                    normals[loop.vertex_index].append((normal_matrix@normal.vector).normalized())
            normal_rows.append([sum(normals[i],Vector()).normalized() for i in indices])
        if any(a.dot(b)<.999 for a,b in zip(*normal_rows)):
            errors.append(row["id"]+" exported partition normals no longer match")
    for angle in (0,45,90,135):
        for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
        bone=rig.pose.bones[row["bone"]];bone.rotation_mode="QUATERNION"
        bone.rotation_quaternion=Quaternion((1,0,0),math.radians(angle))
        bpy.context.view_layer.update()
        skins={b.name:b.matrix@b.bone.matrix_local.inverted() for b in rig.pose.bones}
        factor=(1/math.cos(math.radians(angle)/2)-1)/(1/math.cos(math.radians(135)/2)-1)
        def posed(item,index):
            obj,to_rig,_=item;vertex=obj.data.vertices[index];point=vertex.co.copy()
            if row.get("corrective_shape") and obj.data.shape_keys:
                point+=(obj.data.shape_keys.key_blocks[row["corrective_shape"]].data[index].co-vertex.co)*factor
            point=to_rig@point
            names={group.index:group.name for group in obj.vertex_groups}
            return sum((skins[names[g.group]]@point*g.weight for g in vertex.groups if g.weight>1e-7),Vector())
        first,second=matched
        gap=max((posed(first,ia)-posed(second,ib)).length for ia,ib in zip(first[2],second[2]))
        if gap>2e-5:errors.append(f"{row['id']} FBX opens {gap:.8f}m at{angle}degrees")
        if row.get("corrective_shape"):
            original=section_area([second[1]@second[0].data.vertices[i].co for i in second[2]])
            ratio=section_area([posed(second,i) for i in second[2]])/original
            if not .85<=ratio<=1.15:errors.append(f"{row['id']} FBX section area is{ratio:.4f}at{angle}degrees")
fit=manifest.get("fit",{})
if fit.get("torso_contract")=="lean_waist_to_shoulder_v1":
    torso=parts["GEO_Torso"];to_rig=rig_inverse@torso.matrix_world
    points=[to_rig@v.co for v in torso.data.vertices]
    for z,key in ((.970,"waist_half_width_m"),(1.350,"upper_ribcage_half_width_m")):
        ring=[p for p in points if abs(p.z-z)<2e-5]
        expected=fit["measured"][key]
        if not ring or abs(max(abs(p.x) for p in ring)-expected)>2e-5:
            errors.append("FBX actual torso taper disagrees with source measurement: "+key)
    for name in ("GEO_Neck","GEO_UpperArm.L","GEO_UpperArm.R","GEO_Shoulder.L","GEO_Shoulder.R","GEO_Pelvis","GEO_Thigh.L","GEO_Thigh.R"):
        if not parts[name].data.materials or parts[name].data.materials[0].name!="MAT_Skin":
            errors.append("FBX joint skin must keep its continuous skin material: "+name)
    if fit.get("pelvis_contract")=="male_pelvis_gluteal_v1":
        obj=parts["GEO_Pelvis"];matrix=rig_inverse@obj.matrix_world
        ring=[matrix@v.co for v in obj.data.vertices if abs((matrix@v.co).z-.850)<2e-5]
        expected=fit["pelvis_measured"]
        if not ring or abs(max(p.y for p in ring)-expected["posterior_extent_m"])>2e-5:
            errors.append("FBX actual paired gluteal volume disagrees with measured source")
        if any(name not in parts for name in ("CLO_Belt","CLO_BeltBuckle")):
            errors.append("FBX must retain the independent military belt and buckle")
    jacket=parts["CLO_JacketBody"];matrix=rig_inverse@jacket.matrix_world
    rear=[matrix@v.co for v in jacket.data.vertices if abs((matrix@v.co).x)<2e-5]
    for station in fit["measured"]["jacket_stations"]:
        if station["z_m"]>1.361 or "rear_extent_m" not in station:continue
        points=[p for p in rear if abs(p.z-station["z_m"])<2e-5]
        if not points or abs(max(p.y for p in points)-station["rear_extent_m"])>2e-5:
            errors.append("FBX hanging jacket rear disagrees with measured source")
if manifest.get("footwear",{}).get("contract")=="hero_footwear_v1":
    import player_boots
    for foot in manifest["footwear"]["feet"]:
        side=foot["side"]
        for name in foot["renderers"]:
            obj=parts[name];matrix=rig_inverse@obj.matrix_world
            points=[matrix@v.co for v in obj.data.vertices]
            keys=obj.data.shape_keys.key_blocks if obj.data.shape_keys else {}
            expected={row["shape"] for row in foot["toe_shapes"]}
            if name=="CLO_Boot."+side:
                expected.update((foot["ankle_dorsiflex_shape"],foot["ankle_plantarflex_shape"]))
            if not expected.issubset(keys.keys()):
                errors.append(name+" FBX lacks native ankle/toe corrective channels");continue
            obj.data.calc_loop_triangles()
            def volume(values):
                return abs(sum(values[t.vertices[0]].dot(values[t.vertices[1]].cross(values[t.vertices[2]]))/6
                               for t in obj.data.loop_triangles))
            baseline=volume(points)
            if name=="CLO_Boot."+side:
                ring=[]
                for coordinate in foot["ankle_section_blender"]:
                    point=Vector(tuple(coordinate[k] for k in ("x","y","z")))
                    index=min(range(len(points)),key=lambda i:(points[i]-point).length_squared)
                    if (points[index]-point).length>2e-5:
                        errors.append(name+" FBX ankle section vertex is missing")
                    ring.append(index)
                ankle=rig.data.bones[foot["foot_bone"]].head_local
                original=section_area([points[i] for i in ring])
                for angle in (-30,-15,0,15,30,45):
                    label="ankle_dorsiflex_shape" if angle>=0 else "ankle_plantarflex_shape"
                    maximum=foot["ankle_dorsiflex_max_degrees" if angle>=0 else "ankle_plantarflex_max_degrees"]
                    key=keys[foot[label]];rotation=Quaternion((1,0,0),-math.radians(angle));posed=[]
                    for index in ring:
                        point=points[index]+(matrix@key.data[index].co-points[index])*abs(angle)/maximum
                        weights={obj.vertex_groups[g.group].name:g.weight for g in obj.data.vertices[index].groups}
                        posed.append(point*weights.get(foot["shin_bone"],0)+
                                     (rotation@(point-ankle)+ankle)*weights.get(foot["foot_bone"],0))
                    ratio=section_area(posed)/original
                    if not .95<=ratio<=1.05:
                        errors.append(name+f" FBX ankle section area is {ratio:.4f} at {angle:g} degrees")
            for angle in (7.5,15,22.5,30,37.5,45):
                posed=[point.copy() for point in points]
                for endpoint,weight in player_boots.toe_weights(angle).items():
                    key=keys[f"BootToeRoll{endpoint}.{side}"]
                    for i in range(len(posed)):
                        posed[i]+=(matrix@key.data[i].co-points[i])*weight
                ratio=volume(posed)/baseline
                if not .75<=ratio<=1.25:
                    errors.append(name+f" FBX forefoot flex volume is {ratio:.4f} at {angle:g} degrees")
            if name=="CLO_BootSole."+side:
                ankle=rig.data.bones[foot["foot_bone"]].head_local
                # FBX leaf bones have no authored tail endpoint. The source
                # ball pivot preserves the exact toe/support contract.
                toe_y=foot["ball_pivot_blender"]["y"]-.070
                if abs(min(p.z for p in points))>2e-5 or abs(min(p.y for p in points)-toe_y)>2e-5 or abs(max(p.y for p in points)-ankle.y-.073)>2e-5 or abs(max(abs(p.x-ankle.x) for p in points)-.065)>2e-5:
                    errors.append(name+" FBX changed the existing ground support footprint")
if errors:raise RuntimeError("Joint export failed:\n"+"\n".join(errors))
print(f"Character joint FBX round-trip passed: {len(payload['seams'])} declared sections, shapes and posed boundaries",flush=True)
