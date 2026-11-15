#!/usr/bin/env python3
"""Fit the Ferryman and passenger actions to the sedan on their production rigs."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
from types import SimpleNamespace

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
import last_route_driver_poses


def source_module(name, file):
    spec = importlib.util.spec_from_file_location(name, TOOLS / file)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


worker = source_module("ferryman_worker_source", "build-default-npc-3d-model.py")
car = source_module("cabin_vehicle_source", "build-last-route-car-3d-model.py")
legacy = worker.base
FPS = 24
PERCH_HIP_RISE = .112
PERCH_HIP_REACH = .058
PASSENGER_CLEARANCE = .02
DRIVER_BANK = "LastRouteFerrymanActions"
HERO_BANK = "LastRoutePassengerActions"


def sample(rig, action, fraction):
    rig.animation_data.action = action
    frame = action.frame_start + (action.frame_end - action.frame_start) * fraction
    bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1.)
    bpy.context.view_layer.update()


def pose(rig):
    return {b.name: legacy.BonePose(
        rotation_degrees=tuple(math.degrees(x) for x in b.rotation_quaternion.to_euler("XYZ")),
        location_m=tuple(b.location), scale=tuple(b.scale)) for b in rig.pose.bones}


def matrices(rig):
    return {b.name:b.matrix.copy() for b in rig.pose.bones}


def foot_contact(rig, mesh_name, side, heel=False):
    bone = rig.data.bones["foot." + side]
    obj = bpy.data.objects[mesh_name + "." + side]
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    sole_height = min(point.z for point in points)
    # The narrow bumper supports the heel, while the broader cabin floor
    # supports the instep. Measure both from the actual sole mesh.
    sole_y = max(point.y for point in points) - .022 if heel else -.092
    return bone.matrix_local.inverted() @ Vector((bone.head_local.x, sole_y, sole_height))


def turn_bone(bone, current, goal):
    rotation = current.rotation_difference(goal) @ bone.matrix.to_quaternion()
    bone.matrix = Matrix.Translation(bone.head.copy()) @ rotation.to_matrix().to_4x4()
    bpy.context.view_layer.update()


def aim_limb(rig, first, middle, last, goal, pole, rotation):
    upper, lower, end = [rig.pose.bones[n] for n in (first, middle, last)]
    origin = upper.head.copy()
    a, b = (lower.head-origin).length, (end.head-lower.head).length
    delta = goal-origin
    direction = delta.normalized()
    length = min(a+b-.001, max(abs(a-b)+.001,delta.length))
    along = (a*a-b*b+length*length)/(2*length)
    bend = (pole-direction*direction.dot(pole)).normalized()
    elbow = origin+direction*along+bend*math.sqrt(max(0.,a*a-along*along))
    turn_bone(upper,lower.head-origin,elbow-origin)
    turn_bone(lower,end.head-lower.head,origin+direction*length-lower.head)
    end.matrix = Matrix.Translation(end.head.copy()) @ rotation.to_matrix().to_4x4()
    bpy.context.view_layer.update()
    return (end.head-goal).length


def feet(rig, sole_points, scale, offset_y, offset_z, kick=None, width=.103):
    pelvis = rig.pose.bones["pelvis"].head.copy()
    errors = []
    for side, sign in (("L",1.),("R",-1.)):
        add_y,add_z = (kick[1],kick[2]) if kick and kick[0]==side else (0.,0.)
        foot = rig.pose.bones["foot." + side]
        rotation = foot.bone.matrix_local.to_quaternion()
        target = pelvis + Vector((sign*width, offset_y+add_y, offset_z+add_z))/scale
        errors.append(aim_limb(rig,"thigh."+side,"shin."+side,"foot."+side,
            target-rotation@sole_points[side], Vector((sign*.12,-.52,.76)),rotation))
    return max(errors)


def measured_extent(rig, prefixes):
    dg=bpy.context.evaluated_depsgraph_get()
    values=[]
    for obj in bpy.data.objects:
        if obj.type!="MESH" or not obj.name.startswith(prefixes): continue
        evaluated=obj.evaluated_get(dg)
        world=evaluated.matrix_world
        values.extend(world @ vertex.co for vertex in evaluated.data.vertices)
    if not values:
        raise RuntimeError(f"Missing the fitted clothing or body meshes: {prefixes}")
    return values


def front_body():
    model = car.LastRouteCarBuilder().build()
    parts = ("body_shell", "exterior_trim", "headlight_rim", "headlight", "bumper", "number_plate")
    points, faces = [], []
    for part in model.parts:
        if part.role not in parts:
            continue
        mesh = part.obj.data
        first = len(points)
        points.extend(vertex.co.copy() for vertex in mesh.vertices)
        faces.extend(tuple(first + i for i in face.vertices) for face in mesh.polygons)
    return BVHTree.FromPolygons(points, faces)


def perch_clearance(rig, action, front):
    # The runtime aligns the pelvis at the same authored wait phase. Check
    # actual trouser/boot skin against the first drawn surface at every frame.
    sample(rig, action, 1.5 / 4.)
    origin = Vector(car.PERCH_SEAT) + Vector((0., -PERCH_HIP_REACH, PERCH_HIP_RISE))
    origin -= rig.pose.bones["pelvis"].head * worker.HEIGHT_SCALE
    minimum = math.inf
    closest = None
    for index in range(4 * FPS + 1):
        sample(rig, action, index / (4 * FPS))
        for vertex in measured_extent(rig, ("CLO_Trousers_everyday", "CLO_Boot_work")):
            point = origin + vertex * worker.HEIGHT_SCALE
            if not car.PERCH_SOLES[2] + .022 < point.z < car.hood_z(point.x, car.FRONT_Y) - .008:
                continue
            hit, _, _, _ = front.ray_cast(Vector((point.x, -3.1, point.z)), Vector((0., 1., 0.)), 1.12)
            if hit is not None and hit.y - point.y < minimum:
                minimum = hit.y - point.y
                closest = (index, tuple(round(value, 4) for value in point))
    if not math.isfinite(minimum) or minimum < .013:
        raise RuntimeError(f"The perched trouser or boot intersects the nose: clearance {minimum:.4f} m at {closest}")
    return round(minimum, 6)


def make(rig, name, keys, seconds, loop, version):
    spec = legacy.ActionSpec(name,version,seconds,round(seconds*FPS),
                             "measured seat and footing", "in place",one_shot=not loop)
    return legacy.create_action(rig,spec,keys)


def apply(rig, value):
    rig.animation_data.action = None
    legacy.reset_pose(rig)
    legacy.apply_pose(rig,value)


def driver_actions(rig):
    keys=last_route_driver_poses.action_keys(legacy)
    sole={s:foot_contact(rig,"CLO_Boot_workSole",s) for s in ("L","R")}
    heel={s:foot_contact(rig,"CLO_Boot_workSole",s,heel=True) for s in ("L","R")}
    scale=worker.HEIGHT_SCALE
    z_perch=-car.PERCH_DROP_M-PERCH_HIP_RISE
    y_perch=car.PERCH_SOLES[1]-car.PERCH_SEAT[1]+PERCH_HIP_REACH
    max_ankle=0.
    wait=[]
    for t,value in keys["FerrymanWait"]:
        apply(rig,value)
        kicks={.25:("R",-.10,.115),.625:("L",-.089,.108),.875:("R",-.063,.065)}
        max_ankle=max(max_ankle,feet(rig,heel,scale,y_perch,z_perch,kicks.get(t)))
        wait.append((t,pose(rig)))
    drive=[]
    for t,value in keys["FerrymanDrive"]:
        apply(rig,value)
        max_ankle=max(max_ankle,feet(rig,sole,scale,-.568,car.FLOOR_Z+.014-car.SEAT_PELVIS_Z,width=.103))
        drive.append((t,pose(rig)))
    cap=[]
    for _,value in drive:
        apply(rig,value)
        cap.append(scale*(max(p.z for p in measured_extent(rig,("CLO_Hat_everyday","GEO_Head")))-
                          rig.pose.bones["pelvis"].head.z))
    if max(cap) > car.ROOF_UNDERSIDE_Z-car.SEAT_PELVIS_Z-.045 or max_ankle>.006:
        raise RuntimeError(f"The fitted driver protrudes from the car: cap {max(cap):.4f}, feet {max_ankle:.4f}")

    # Entry and exit reuse the timed body beats. Both ends share the exact
    # revised key with the fitted perch and drive loops.
    dismount=list(keys["FerrymanDismount"])
    dismount[0]=wait[0]
    board=list(keys["FerrymanBoard"])
    board[-1]=drive[-1]
    clips=[("FerrymanWait",wait,4.,True),("FerrymanTrudge",keys["FerrymanTrudge"],1.5,True),
           ("FerrymanDismount",dismount,1.,False),("FerrymanBoard",board,2.5,False),
           ("FerrymanDrive",drive,3.,True)]
    return clips,{"perch_hip_rise_m":PERCH_HIP_RISE,"perch_hip_reach_m":PERCH_HIP_REACH,
                  "driver_crown_above_pelvis_m":round(max(cap),6),"maximum_sole_fit_error_m":round(max_ankle,7)}


def blend_snapshot(rig, initial, desired, weight, bones):
    for name in bones:
        bone=rig.pose.bones[name]
        start_r,start_t=initial[name]
        end_r,end_t=desired[name]
        bone.rotation_quaternion=start_r.slerp(end_r,weight)
        bone.location=start_t.lerp(end_t,weight)
    bpy.context.view_layer.update()


def hero_actions(rig):
    original={n:bpy.data.actions[n] for n in ("CarBoardEnter","CarAlightExit","BusRideLoop")}
    sole={s:foot_contact(rig,"CLO_BootSole",s) for s in ("L","R")}
    lower=tuple(n+"."+s for n in ("thigh","shin","foot") for s in ("L","R"))
    torso=("spine","chest","neck","head")
    def snapshot(names):
        return {n:(rig.pose.bones[n].rotation_quaternion.copy(), rig.pose.bones[n].location.copy()) for n in names}
    sample(rig,original["BusRideLoop"],0.)
    initial=pose(rig)
    initial.update({
        "spine":legacy.BonePose(rotation_degrees=(7.,0.,0.)),
        "chest":legacy.BonePose(rotation_degrees=(1.,0.,0.)),
        "neck":legacy.BonePose(rotation_degrees=(-3.,0.,0.)),
        "head":legacy.BonePose(rotation_degrees=(-2.,0.,0.)),
        "upper_arm.L":legacy.BonePose(target_direction=(.27,-.12,-.72)),
        "upper_arm.R":legacy.BonePose(target_direction=(-.27,-.12,-.72)),
        "forearm.L":legacy.BonePose(target_direction=(-.11,-.64,-.42)),
        "forearm.R":legacy.BonePose(target_direction=(.11,-.64,-.42)),
    })
    apply(rig,initial)
    fit=feet(rig,sole,1.,-.535,car.FLOOR_Z+.014-car.SEAT_PELVIS_Z-PASSENGER_CLEARANCE,width=.123)
    seated=pose(rig)
    fitted=snapshot(lower+torso)
    height=max(p.z for p in measured_extent(rig,("GEO_Head","HAIR_")))-rig.pose.bones["pelvis"].head.z
    if height+PASSENGER_CLEARANCE>car.ROOF_UNDERSIDE_Z-car.SEAT_PELVIS_Z-.035 or fit>.006:
        raise RuntimeError(f"The passenger clips the new sedan: head {height:.4f}, feet {fit:.4f}")
    breathe=dict(seated)
    breathe["chest"]=legacy.BonePose(rotation_degrees=(1.75,.24,0.))
    clips=[("LastRoutePassengerLoop",[(0.,seated),(.5,breathe),(1.,seated)],2.,True)]
    for departing, original_name, name in (
        (False,"CarBoardEnter","LastRoutePassengerEnter"),(True,"CarAlightExit","LastRoutePassengerExit")):
        samples=[]
        for frame in range(FPS*3+1):
            t=frame/(FPS*3)
            if (t==1. and not departing) or (t==0. and departing):
                samples.append((t,seated))
                continue
            sample(rig,original[original_name],t)
            if (t>.62 and not departing) or (t<.34 and departing):
                w=max(0.,min(1.,(1.-t/.34) if departing else ((t-.62)/.38)))
                weight=w*w*(3.-2.*w)
                blend_snapshot(rig,snapshot(lower+torso),fitted,weight,lower+torso)
            if .94<t<1. and not departing or 0.<t<.055 and departing:
                w=min(1.,(t-.94)/.06 if not departing else (1.-t/.055))
                names=tuple(n for n in seated if n.startswith(("upper_arm","forearm","hand")))
                hand_pose=snapshot(names)
                current=pose(rig)
                apply(rig,seated)
                hand_target=snapshot(names)
                apply(rig,current)
                blend_snapshot(rig,hand_pose,hand_target,w*w*(3.-2.*w),names)
            samples.append((t,pose(rig)))
        clips.append((name,samples,3.,False))
    return clips,{"passenger_crown_above_pelvis_m":round(height,6),"maximum_sole_fit_error_m":round(fit,7)}


def prove_endpoints(rig,bank,actions):
    if bank==HERO_BANK:
        pairs=(("LastRoutePassengerEnter",1.,"LastRoutePassengerLoop",0.),
               ("LastRoutePassengerExit",0.,"LastRoutePassengerLoop",0.))
    else:
        pairs=(("FerrymanDismount",0.,"FerrymanWait",0.),("FerrymanBoard",1.,"FerrymanDrive",0.))
    for a,t,b,u in pairs:
        sample(rig,actions[a],t); first=matrices(rig)
        sample(rig,actions[b],u); second=matrices(rig)
        if max(abs(first[n][r][c]-second[n][r][c]) for n in first for r in range(4) for c in range(4))>.00005:
            raise RuntimeError(f"Endpoints do not share a pose: {a} and {b}")
    for name,action in actions.items():
        if any(not curve.data_path.startswith('pose.bones[') for curve in legacy.iter_action_fcurves(action)):
            raise RuntimeError(f"Object/root animation in {name}")


def export_bank(source, folder, bank, builder, validate_only, front=None):
    bpy.ops.wm.open_mainfile(filepath=str(source))
    rig=bpy.data.objects["RIG_Player"]
    scene=bpy.context.scene; scene.render.fps=FPS; scene.render.fps_base=1.
    if rig.animation_data is None: rig.animation_data_create()
    for bone in rig.pose.bones: bone.rotation_mode="QUATERNION"
    previous=set(bpy.data.actions)
    clips,fit=builder(rig)
    actions={name:make(rig,name,keys,seconds,loop,bank) for name,keys,seconds,loop in clips}
    prove_endpoints(rig,bank,actions)
    if front is not None:
        fit["minimum_perch_shin_clearance_m"] = perch_clearance(rig, actions["FerrymanWait"], front)
    digest=hashlib.sha256()
    for name in sorted(actions):
        for curve in legacy.iter_action_fcurves(actions[name]):
            digest.update(f"{name}:{curve.data_path}:{curve.array_index}".encode())
            for k in curve.keyframe_points:
                digest.update(f"{k.co.x:.4f},{k.co.y:.6f};".encode())
    manifest={"generator":Path(__file__).name,"bank":bank,"bone_count":len(rig.pose.bones),
              "signature":digest.hexdigest(), "contacts":fit,
              "clips":[{"name":n,"seconds":s,"loop":l} for n,_,s,l in clips]}
    path=folder/(bank+".json")
    if validate_only:
        if json.loads(path.read_text())!=manifest:
            raise RuntimeError(f"Deterministic bank differs: {bank}")
    else:
        rig.animation_data.action=None
        for action in previous: bpy.data.actions.remove(action)
        legacy.reset_pose(rig)
        folder.mkdir(parents=True,exist_ok=True)
        legacy.export_animation_fbx(folder/(bank+".fbx"),SimpleNamespace(root=rig.parent,rig=rig))
        path.write_text(json.dumps(manifest,indent=2)+"\n")
    print(bank,json.dumps(fit),flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--npc-source",type=Path,default=ROOT/"ArtSource/VillageLife/StationWorker.blend")
    parser.add_argument("--npc-dir",type=Path,default=ROOT/"Assets/Resources/City")
    parser.add_argument("--hero-dir",type=Path,default=ROOT/"Assets/Resources/Player")
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    export_bank(args.npc_source,args.npc_dir,DRIVER_BANK,driver_actions,args.validate_only, front_body())
    export_bank(ROOT/"ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend",
                args.hero_dir,HERO_BANK,hero_actions,args.validate_only)


if __name__=="__main__":
    main()
