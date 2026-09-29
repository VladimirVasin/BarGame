#!/usr/bin/env python3
"""Prove the production pipe is hollow; optionally compare to a baseline."""
from pathlib import Path
import argparse
import sys
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("--baseline",type=Path)
parser.add_argument("--validate-only",action="store_true")
parser.add_argument("--candidate",type=Path,default=ROOT/"ArtSource/Pedestrians/Blender/CityPedestrianHandProps.blend")
args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
def snapshot(path):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    result={}
    for obj in bpy.data.objects:
        if obj.type!="MESH": continue
        result[obj.name]={
            "parent":obj.parent.name if obj.parent else None,
            "points":[tuple(round(v,7) for v in obj.matrix_world@point.co) for point in obj.data.vertices],
            "faces":[tuple(face.vertices) for face in obj.data.polygons],
            "color":tuple(obj.color),
        }
    return result

before=snapshot(args.baseline.resolve()) if args.baseline else None
after=snapshot(args.candidate.resolve())
comparison="production geometry"
if before:
    if before.keys()!=after.keys():
        raise RuntimeError("Hand-prop object set changed")
    changed=[name for name in before if before[name]!=after[name]]
    if any(name not in ("ACC_PipeBowl","ACC_PipeEmber") for name in changed):
        raise RuntimeError(f"Only the pipe bowl and coals may change, found {changed}")
    comparison=f"exact outer bowl; {len(before)-2} other meshes unchanged"
def bounds(points):
    return tuple(min(p[i] for p in points) for i in range(3)),tuple(max(p[i] for p in points) for i in range(3))
if before:
    old_bounds=bounds(before["ACC_PipeBowl"]["points"])
    new_bounds=bounds(after["ACC_PipeBowl"]["points"])
    if any(abs(a-b)>1e-6 for aa,bb in zip(old_bounds,new_bounds) for a,b in zip(aa,bb)):
        raise RuntimeError(f"Outer pipe bowl bounds changed: {old_bounds} -> {new_bounds}")
    if before["ACC_PipeBowl"]["points"][:16]!=after["ACC_PipeBowl"]["points"][:16]:
        raise RuntimeError("Original outer pipe bowl vertices changed")
points=after["ACC_PipeEmber"]["points"]
faces=after["ACC_PipeEmber"]["faces"]
remaining=set(range(len(points))); components=0
while remaining:
    connected={next(iter(remaining))}
    while True:
        grown=connected|{i for face in faces if connected.intersection(face) for i in face}
        if grown==connected:break
        connected=grown
    remaining-=connected; components+=1
if components!=5:raise RuntimeError(f"Expected five separate coals, found {components}")
bowl=[Vector(p) for p in after["ACC_PipeBowl"]["points"]]
coals=[Vector(p) for p in points]
rim=sum(bowl[8:16],Vector())/8
floor=sum(bowl[24:32],Vector())/8
normal=(bowl[9]-bowl[8]).cross(bowl[10]-bowl[9]).normalized()
floor_depth=(rim-floor).dot(normal)
coal_depth=min((rim-p).dot(normal) for p in coals)*.72
if not .005<=coal_depth<=.007:
    raise RuntimeError(f"Coal must lie 5-7 mm below the final scaled rim: {coal_depth}")
if any((rim-p).dot(normal)>floor_depth for p in coals):
    raise RuntimeError("A coal penetrates the cavity floor")
wall_clearance=float("inf")
for i in range(8):
    a,b,c=bowl[16+i],bowl[16+(i+1)%8],bowl[24+(i+1)%8]
    inward=(b-a).cross(c-a).normalized()
    wall_clearance=min(wall_clearance,min((p-a).dot(inward) for p in coals))
if wall_clearance<.0005:
    raise RuntimeError(f"Coals must clear the inner walls: {wall_clearance}")
bvh=BVHTree.FromPolygons(bowl,after["ACC_PipeBowl"]["faces"])
hit,_,_,_=bvh.ray_cast(rim+normal*.05,-normal)
if hit is None or abs((rim-hit).dot(normal)-floor_depth)>.0001:
    raise RuntimeError("Pipe mouth must be open: the central ray must reach the floor")
print(f"PASS: five recessed coals; opening depth {floor_depth*.72*1000:.2f} mm; "
      f"coal rim depth {coal_depth*1000:.2f} mm; wall clearance {wall_clearance*.72*1000:.2f} mm; "
      +comparison)
