#!/usr/bin/env python3
"""Measure the production support grip; optionally compare it to a baseline."""
from pathlib import Path
import argparse
import importlib.util
import sys
import hashlib

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("--baseline",type=Path)
parser.add_argument("--candidate",type=Path,default=ROOT/"ArtSource/Pedestrians/Blender/LakeFisherman3D.blend")
parser.add_argument("--validate-only",action="store_true")
args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
sys.path.insert(0,str(ROOT/"tools"))
spec=importlib.util.spec_from_file_location("fisherman_authoring",ROOT/"tools/build-city-pedestrian-3d-model.py")
api=importlib.util.module_from_spec(spec); sys.modules[spec.name]=api; spec.loader.exec_module(api)

def snapshot(path):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    meshes={obj.name:(tuple(tuple(v.co) for v in obj.data.vertices),
                     tuple(tuple(p.vertices) for p in obj.data.polygons))
            for obj in bpy.data.objects if obj.type=="MESH" and obj.get("bp_export")}
    rig=next(obj for obj in bpy.data.objects if obj.type=="ARMATURE")
    bones=tuple((b.name,b.parent.name if b.parent else None,tuple(tuple(row) for row in b.matrix_local))
                for b in rig.data.bones)
    return meshes,bones

baseline=snapshot(args.baseline.resolve()) if args.baseline else None
after,after_bones=snapshot(args.candidate.resolve())
if len(after_bones)!=31: raise RuntimeError("Fisherman must retain the canonical 31-bone rig")
comparison="production model"
if baseline:
    before,before_bones=baseline
    if before_bones!=after_bones: raise RuntimeError("Canonical fisherman rig changed")
    if before.keys()!=after.keys(): raise RuntimeError("Fisherman renderer set changed")
    changed=[name for name in before if before[name]!=after[name]]
    if any(name not in {"GEO_Hand.L","CLO_SleeveCuff.L"} for name in changed):
        raise RuntimeError(f"Unrelated final fisherman geometry changed: {changed}")
    comparison=f"{len(before)-len(changed)} unchanged final meshes; changed {changed}"
    candidate_atlas=args.candidate.resolve().parent/"LakeFishermanFaceAtlas.png"
    if candidate_atlas.exists():
        atlas_before=(ROOT/"Assets/Pedestrians/Textures/LakeFishermanFaceAtlas.png").read_bytes()
        if hashlib.sha256(atlas_before).digest()!=hashlib.sha256(candidate_atlas.read_bytes()).digest():
            raise RuntimeError("Final fisherman face PNG changed")
rig=next(obj for obj in bpy.data.objects if obj.type=="ARMATURE")
parts={side:[bpy.data.objects[name+side] for name in
             ("CLO_Sleeve.","CLO_SleeveLower.","CLO_SleeveCuff.","GEO_Hand.")]
       for side in ("L","R")}
def bvh(obj,graph):
    evaluated=obj.evaluated_get(graph)
    return BVHTree.FromPolygons([evaluated.matrix_world@v.co for v in evaluated.data.vertices],
                               [tuple(p.vertices) for p in evaluated.data.polygons])
smallest_elbow_x=float("inf")
for frame,pose in api.animation_keys()["FishermanLean"]:
    api.reset_pose(rig); api.apply_pose(rig,pose)
    bpy.context.view_layer.update(); graph=bpy.context.evaluated_depsgraph_get()
    smallest_elbow_x=min(smallest_elbow_x,rig.pose.bones["forearm.L"].head.x)
    evaluated={obj.name:bvh(obj,graph) for group in parts.values() for obj in group}
    for left in parts["L"]:
        for right in parts["R"]:
            if evaluated[left.name].overlap(evaluated[right.name]):
                raise RuntimeError(f"Arm geometry crosses at frame {frame}: {left.name}/{right.name}")
if smallest_elbow_x<=0: raise RuntimeError("The left elbow crossed the body midline")
print(f"PASS: {comparison}; "
      f"all FishermanLean keys have separate arm surfaces; left elbow X >= {smallest_elbow_x:.4f} m")
