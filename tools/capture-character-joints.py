#!/usr/bin/env python3
"""Render reproducible character joint studies from a source Blender file."""
from __future__ import annotations
import argparse
import math
from pathlib import Path
import sys
import bpy
from mathutils import Matrix,Vector

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source",type=Path,required=True)
parser.add_argument("--output-dir",type=Path,required=True)
parser.add_argument("--bare",action="store_true")
parser.add_argument("--trousers-only",action="store_true")
parser.add_argument("--joint",choices=("elbow","knee","torso","pelvis"),default="elbow")
parser.add_argument("--validate-only",action="store_true")
parser.add_argument("--views",help="Comma-separated named torso/pelvis views")
args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
bpy.ops.wm.open_mainfile(filepath=str(args.source.resolve()))
rig=next(o for o in bpy.data.objects if o.type=="ARMATURE")
relaxed=next((action for action in bpy.data.actions if action.name=="Relaxed"),None)
if relaxed:
    rig.animation_data_create();rig.animation_data.action=relaxed
    bpy.context.scene.frame_set(0);bpy.context.view_layer.update()
    relaxed_basis={bone.name:bone.matrix_basis.copy() for bone in rig.pose.bones}
else:relaxed_basis={}
rig.animation_data_clear();rig.data.pose_position="POSE"
for bone in rig.pose.bones:
    bone.rotation_mode="XYZ";bone.rotation_euler=(0,0,0);bone.location=(0,0,0);bone.scale=(1,1,1)
if args.bare or args.trousers_only:
    for obj in bpy.data.objects:
        if obj.type=="MESH":
            if obj.get("bp_body_coverage"):
                obj.hide_render=args.trousers_only and obj.get("bp_body_coverage") in {"trousers","boots"}
            elif obj.name.startswith("CLO_"):
                obj.hide_render=not (args.trousers_only and obj.name.startswith(("CLO_Trousers","CLO_Boot","CLO_Belt")))
scene=bpy.context.scene;scene.render.engine="CYCLES";scene.cycles.samples=12
scene.render.resolution_x=scene.render.resolution_y=640;scene.render.resolution_percentage=100
scene.world.color=(.08,.08,.08)
for obj in list(scene.objects):
    if obj.type in {"LIGHT","CAMERA"}:bpy.data.objects.remove(obj,do_unlink=True)
bpy.context.view_layer.update()
distal="forearm.L" if args.joint=="elbow" else "shin.L"
target=Vector((0,0,1.25)) if args.joint=="torso" else Vector((0,0,.84)) if args.joint=="pelvis" else rig.matrix_world@rig.pose.bones[distal].head
positions=((2,-3,4),(-2,-1,2),(2,3,3),(-2,2,1.5)) if args.joint=="pelvis" else ((2,-3,4),(-2,-1,2))
for position in positions:
    data=bpy.data.lights.new("Joint study light","AREA");data.energy=250;data.size=3
    obj=bpy.data.objects.new("Joint study light",data);scene.collection.objects.link(obj)
    obj.location=position;obj.rotation_euler=(target-obj.location).to_track_quat("-Z","Y").to_euler()
data=bpy.data.cameras.new("Joint study camera");camera=bpy.data.objects.new("Joint study camera",data)
scene.collection.objects.link(camera);scene.camera=camera
camera.location=target+Vector((.68,-.70,.25))
camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
data.type="ORTHO";data.ortho_scale=.63
args.output_dir.mkdir(parents=True,exist_ok=True)
if args.joint=="pelvis":
    data.ortho_scale=.70
    for name,offset,bend in (("front",(0,-3,.08),0),("rear",(0,3,.08),0),
                             ("side",(3,0,.08),0),("rear-quarter",(2,3,.15),0),("hip-bent",(3,.5,.12),90),
                             ("crouch",(.65,3,.15),65)):
        if args.views and name not in args.views.split(","):continue
        for side in ("L","R"):
            rig.pose.bones["thigh."+side].rotation_euler.x=math.radians(-bend) if name=="crouch" or side=="L" else 0
            rig.pose.bones["shin."+side].rotation_euler.x=math.radians(105) if name=="crouch" else 0
        factor=(1/math.cos(math.radians(105)/2)-1)/(1/math.cos(math.radians(135)/2)-1) if name=="crouch" else 0
        for obj in bpy.data.objects:
            if obj.type=="MESH" and obj.data.shape_keys:
                for key in obj.data.shape_keys.key_blocks:
                    if key.name.startswith(("JointVolume.Knee.","TrouserKneeFold.")):key.value=factor
                    if key.name.startswith("JointVolume.Hip."):
                        hip_angle=bend if name=="crouch" or key.name.endswith(".L") else 0
                        key.value=(1/math.cos(math.radians(hip_angle)/2)-1)/(1/math.cos(math.radians(135)/2)-1)
        camera.location=target+Vector(offset)
        camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
        bpy.context.view_layer.update()
        scene.render.filepath=str((args.output_dir/("pelvis-"+name+".png")).resolve())
        bpy.ops.render.render(write_still=True)
        print("PELVIS STUDY",name,flush=True)
    raise SystemExit(0)
if args.joint=="torso":
    data.ortho_scale=1.12
    for name,offset,raised in (("front",(0,-3,0),False),("side",(3,0,0),False),
                               ("shoulder-raised",(.45,-3,.12),True),
                               ("relaxed-front",(0,-3,0),False),("relaxed-side",(3,0,0),False),
                               ("neck-turned",(.45,-3,.12),False),("neck-tilted",(.45,-3,.12),False)):
        if args.views and name not in args.views.split(","):continue
        for bone in rig.pose.bones:
            bone.matrix_basis=relaxed_basis.get(bone.name,Matrix.Identity(4)) if name.startswith("relaxed-") else Matrix.Identity(4)
        if name.startswith("relaxed-"):
            camera.location=target+Vector(offset)
            camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
            bpy.context.view_layer.update()
            scene.render.filepath=str((args.output_dir/("torso-"+name+".png")).resolve())
            bpy.ops.render.render(write_still=True);print("TORSO STUDY",name,flush=True);continue
        rig.pose.bones["upper_arm.L"].rotation_euler.x=math.radians(65) if raised else 0
        rig.pose.bones["head"].rotation_euler=(math.radians(-25) if name=="neck-tilted" else 0,
            math.radians(60) if name=="neck-turned" else 0,math.radians(15) if name=="neck-tilted" else 0)
        camera.location=target+Vector(offset)
        camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler()
        bpy.context.view_layer.update()
        scene.render.filepath=str((args.output_dir/("torso-"+name+".png")).resolve())
        bpy.ops.render.render(write_still=True)
        print("TORSO STUDY",name,flush=True)
    raise SystemExit(0)
for angle in (0,90,135):
    rig.pose.bones[distal].rotation_euler.x=math.radians(angle)
    factor=(1/math.cos(math.radians(angle)/2)-1)/(1/math.cos(math.radians(135)/2)-1)
    for obj in bpy.data.objects:
        if obj.type=="MESH" and obj.data.shape_keys:
            for key in obj.data.shape_keys.key_blocks:
                if key.name.endswith(".L") and (
                    key.name=="JacketElbowFold.L" and args.joint=="elbow" or
                    key.name=="TrouserKneeFold.L" and args.joint=="knee" or
                    key.name=="JointVolume."+args.joint.title()+".L"):
                    key.value=factor
    bpy.context.view_layer.update()
    scene.render.filepath=str((args.output_dir/(args.joint+"-"+str(angle)+".png")).resolve())
    bpy.ops.render.render(write_still=True)
    print("JOINT STUDY",args.joint,angle,flush=True)
