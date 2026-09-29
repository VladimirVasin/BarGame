#!/usr/bin/env python3
"""Check the rounded reel against both hands throughout the fishing breath."""
from pathlib import Path
import argparse,sys,importlib.util
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--candidate',type=Path,default=ROOT/'ArtSource/Pedestrians/Blender/CityPedestrianHandProps.blend')
parser.add_argument('--baseline',type=Path)
parser.add_argument('--validate-only',action='store_true')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
sys.path.insert(0,str(ROOT/'tools'))
spec=importlib.util.spec_from_file_location('fisherman_authoring',ROOT/'tools/build-city-pedestrian-3d-model.py')
api=importlib.util.module_from_spec(spec);sys.modules[spec.name]=api;spec.loader.exec_module(api)
def snapshot(path):
    bpy.ops.wm.open_mainfile(filepath=str(path.resolve()))
    return {o.name:([tuple(o.matrix_world@v.co) for v in o.data.vertices],
                    [tuple(p.vertices) for p in o.data.polygons],tuple(o.color))
            for o in bpy.data.objects if o.type=='MESH' and o.get('bp_export')}
before=snapshot(args.baseline) if args.baseline else None
after=snapshot(args.candidate)
if before is not None:
    if before.keys()!=after.keys(): raise RuntimeError('Prop renderer set changed')
    changed=[name for name in before if before[name]!=after[name]]
    if any(name!='ACC_RodReel' for name in changed): raise RuntimeError(f'Unrelated prop changed: {changed}')
    print(f'PASS: {len(before)-1} other prop meshes unchanged')
reel_points,reel_faces,_=after['ACC_RodReel']
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Pedestrians/Blender/LakeFisherman3D.blend'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
parts=[bpy.data.objects[name+side] for side in ('L','R') for name in
       ('GEO_Hand.','CLO_Sleeve.','CLO_SleeveLower.','CLO_SleeveCuff.')]
clearance=float('inf')
for frame,pose in api.animation_keys()['FishermanLean']:
    api.reset_pose(rig);api.apply_pose(rig,pose)
    bpy.context.view_layer.update();graph=bpy.context.evaluated_depsgraph_get()
    delta=rig.pose.bones['hand.R'].matrix@rig.data.bones['hand.R'].matrix_local.inverted()
    vertices=[delta@Vector(p) for p in reel_points]
    reel=BVHTree.FromPolygons(vertices,reel_faces)
    probes=vertices+[sum((vertices[i] for i in face),Vector())/len(face) for face in reel_faces]
    for obj in parts:
        evaluated=obj.evaluated_get(graph)
        hand_points=[evaluated.matrix_world@v.co for v in evaluated.data.vertices]
        hand=BVHTree.FromPolygons(hand_points,[tuple(p.vertices) for p in evaluated.data.polygons])
        if reel.overlap(hand): raise RuntimeError(f'Reel intersects {obj.name} at {frame}')
        clearance=min(clearance,min(hand.find_nearest(p)[3] for p in probes),
                      min(reel.find_nearest(p)[3] for p in hand_points))
if clearance<.003: raise RuntimeError(f'Reel is too close to the skin/sleeve: {clearance}')
print(f'PASS: all 9 Lean keys have no reel/hand/sleeve intersections; sampled clearance {clearance*1000:.2f} mm')
