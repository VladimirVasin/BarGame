#!/usr/bin/env python3
"""Render the published fisherman source in its real fishing pose for review."""
from pathlib import Path
import argparse
import json
import importlib.util
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("--blend",type=Path,default=ROOT / "ArtSource/Pedestrians/Blender/LakeFisherman3D.blend")
parser.add_argument("--refresh-pipe-ember",action="store_true")
parser.add_argument("--grip-review",action="store_true")
parser.add_argument("--refresh-reel",action="store_true")
parser.add_argument("--publish-reel-preview",action="store_true",
                    help="Save only the refreshed source preview reel and source PNG; never export the body.")
args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
if args.publish_reel_preview and not args.refresh_reel:
    parser.error("--publish-reel-preview requires --refresh-reel")
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("fisherman_authoring", ROOT / "tools/build-city-pedestrian-3d-model.py")
api = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = api
spec.loader.exec_module(api)
bpy.ops.wm.open_mainfile(filepath=str(args.blend.resolve()))
scene = bpy.context.scene
scene.view_settings.exposure = -1
rig = next(obj for obj in scene.objects if obj.type == "ARMATURE")
head=bpy.data.objects["GEO_Head"]
head_points=[head.matrix_world @ vertex.co for vertex in head.data.vertices]
head_bvh=BVHTree.FromPolygons(head_points,[tuple(p.vertices) for p in head.data.polygons])
hood=bpy.data.objects["CLO_HoodShell"]
hood_points=[hood.matrix_world @ vertex.co for vertex in hood.data.vertices]
clearance=float("inf")
for polygon in hood.data.polygons:
    corners=[hood_points[i] for i in polygon.vertices]
    probes=corners+[sum(corners,Vector())/len(corners)]
    probes += [a.lerp(b,t) for a,b in zip(corners,corners[1:]+corners[:1]) for t in (.25,.5,.75)]
    for point in probes:
        if point.z<1.60: continue
        location,normal,_,distance=head_bvh.find_nearest(point)
        clearance=min(clearance,distance if (point-location).dot(normal)>=0 else -distance)
print(f"Upper hood/head surface clearance: {clearance:.6f} m")
if clearance<.008:
    raise RuntimeError("Upper hood must enclose the skull with at least 8 mm clearance")
if args.refresh_reel:
    prop=next(prop for prop in api.HAND_PROPS if prop.id=="fishing_rod")
    geometry=api.hand_prop_rest_geometry(prop)["ACC_RodReel"]
    reel=bpy.data.objects["PREVIEW_ACC_RodReel"]
    reel["bp_bone"]="hand.R"
    api.principal_npc_detail.detail.replace_mesh(reel,geometry)
    if args.publish_reel_preview:
        # Save before temporary pose/material/camera inspection overrides.
        # All export renderers and rig remain exactly as loaded.
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.blend.resolve()))
if args.refresh_pipe_ember:
    binding=json.loads((ROOT/"Assets/Pedestrians/Staged/Models/LakeFisherman3D.json").read_text())['face_atlas']
    prop=next(prop for prop in api.HAND_PROPS if prop.id=="smoking_pipe")
    rest=api.hand_prop_rest_geometry(prop)
    socket=Vector(api.fisherman_detail.PIPE_SOCKET_BIND)
    offset=Vector(binding["pipe_mount_offset_m"])
    for part in ("ACC_PipeBowl","ACC_PipeEmber"):
        points,polygons=rest[part]
        points=[socket+offset+binding["pipe_scale"]*(p-socket) for p in points]
        obj=bpy.data.objects["PREVIEW_"+part]
        obj["bp_bone"]="head"
        api.principal_npc_detail.detail.replace_mesh(obj,(points,polygons))
    ember=bpy.data.objects["PREVIEW_ACC_PipeEmber"]
    material=bpy.data.materials.new("MAT_EmberInspectionOnly")
    material.use_nodes=True
    shader=material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value=(.30,.001,.001,1)
    shader.inputs["Emission Color"].default_value=(1.,.006,.002,1)
    shader.inputs["Emission Strength"].default_value=3
    shader.inputs["Roughness"].default_value=.95
    ember.data.materials[0]=material
api.apply_pose(rig, api.fisherman_base_pose())
bpy.context.view_layer.update()
face = bpy.data.objects[api.FACE_SURFACE_PART]
material = bpy.data.materials.new("MAT_FishermanFaceInspection")
material.use_nodes = True
nodes, links = material.node_tree.nodes, material.node_tree.links
shader = nodes.get("Principled BSDF")
shader.inputs["Roughness"].default_value = .9
tex = nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(str(ROOT / "Assets/Pedestrians/Textures/LakeFishermanFaceAtlas.png"), check_existing=True)
tex.interpolation = "Closest"
coord = nodes.new("ShaderNodeTexCoord")
mapping = nodes.new("ShaderNodeVectorMath")
mapping.operation = "MULTIPLY_ADD"
mapping.inputs[1].default_value = (.25,.25,1)
mapping.inputs[2].default_value = (0,.75,0)
links.new(coord.outputs["UV"], mapping.inputs[0])
links.new(mapping.outputs[0], tex.inputs["Vector"])
links.new(tex.outputs["Color"], shader.inputs["Base Color"])
face.data.materials[0] = material
if args.publish_reel_preview:
    scene.render.filepath=str(args.blend.resolve().with_suffix(".png"))
    bpy.ops.render.render(write_still=True)
graph = bpy.context.evaluated_depsgraph_get()
anchor=bpy.data.objects.get("ANCHOR_FishermanExhale")
if anchor is not None:
    mouth_local=face.matrix_world @ face.data.vertices[10].co.lerp(face.data.vertices[17].co,6*(1-50.5/64)-1)
    head_delta=rig.pose.bones["head"].matrix @ rig.data.bones["head"].matrix_local.inverted()
    expected=head_delta @ (mouth_local+Vector((0,-.001,0)))
    error=(anchor.matrix_world.translation-expected).length
    print(f"Head-bound exhale anchor error: {error:.8f} m")
    if error > .00001:
        raise RuntimeError("Fisherman exhale anchor does not follow the painted mouth")
    expected_forward=(head_delta.to_3x3() @ Vector((0,-1,0))).normalized()
    actual_forward=(anchor.matrix_world.to_3x3() @ Vector((0,1,0))).normalized()
    print(f"Exhale local Y / face outward alignment: {actual_forward.dot(expected_forward):.8f}")
    if actual_forward.dot(expected_forward)<.9999:
        raise RuntimeError("Shared exhale needs its local Y axis pointing out of the mouth")
evaluated = face.evaluated_get(graph)
center = sum((evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices), Vector()) / len(evaluated.data.vertices)
normal = (evaluated.matrix_world.to_3x3() @ sum((polygon.normal for polygon in evaluated.data.polygons), Vector())).normalized()
camera = scene.camera
scene.render.resolution_x = 900
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
output = ROOT / "Captures/City"
output.mkdir(parents=True, exist_ok=True)

def shot(name, target, location, lens):
    camera.location = location
    camera.rotation_euler = (target-location).to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = lens
    bpy.context.view_layer.update()
    hit = scene.ray_cast(bpy.context.evaluated_depsgraph_get(), location, (target-location).normalized())
    print(f"{name}: target={tuple(target)}, camera={tuple(location)}, center hit={hit[4].name if hit[0] else 'none'}")
    scene.render.filepath = str(output / f"fisherman-source-{name}.png")
    bpy.ops.render.render(write_still=True)

shot("face", center, center + Vector((-.32,-.52,.23)), 58)
shot("face-front", center, center + Vector((-.035,-.39,.025)), 48)
for row in range(7):
    index = row*7+3
    point = evaluated.matrix_world @ evaluated.data.vertices[index].co
    uv = next(face.data.uv_layers.active.data[loop.index].uv[:] for loop in face.data.loops if loop.vertex_index == index)
    hit = scene.ray_cast(graph, camera.location, (point-camera.location).normalized())
    print(f"face row {row}: uv={uv}, rest={tuple(face.matrix_world @ face.data.vertices[index].co)}, posed={tuple(point)}, visible={hit[4].name if hit[0] else 'none'}")
hands = [bpy.data.objects[f"GEO_Hand.{side}"].evaluated_get(graph) for side in ("L", "R")]
hand_points = [obj.matrix_world @ vertex.co for obj in hands for vertex in obj.data.vertices]
grip_center = sum(hand_points, Vector()) / len(hand_points)
shot("grip", grip_center, grip_center + Vector((-.48,-.62,.22)), 50)
if args.grip_review:
    left=bpy.data.objects["GEO_Hand.L"].evaluated_get(graph)
    left_center=sum((left.matrix_world @ p.co for p in left.data.vertices),Vector())/len(left.data.vertices)
    shot("arms-front",grip_center,grip_center+Vector((.10,-.90,.30)),50)
    shot("arms-top",grip_center,grip_center+Vector((.02,-.08,.85)),50)
    shot("arms-side",grip_center,grip_center+Vector((.78,.02,.25)),50)
    # Inspection views hide only the reel and opposite arm that would mask
    # the wrist. The full context shot above retains every real part.
    inspection_hidden=[]
    for obj in scene.objects:
        if obj.type=="MESH" and (obj.name=="PREVIEW_ACC_RodReel" or
                obj.name in {"CLO_Sleeve.R","CLO_SleeveLower.R","CLO_SleeveCuff.R","GEO_Hand.R"}):
            inspection_hidden.append((obj,obj.hide_render)); obj.hide_render=True
    shot("left-front",left_center,left_center+Vector((.12,-.32,.12)),60)
    shot("left-side",left_center,left_center+Vector((.36,.04,.10)),60)
    shot("left-back",left_center,left_center+Vector((-.10,.32,.12)),60)
    for obj,hidden in inspection_hidden: obj.hide_render=hidden
if args.refresh_pipe_ember:
    coal=bpy.data.objects["PREVIEW_ACC_PipeEmber"].evaluated_get(graph)
    coal_center=sum((coal.matrix_world @ p.co for p in coal.data.vertices),Vector())/len(coal.data.vertices)
    light_data=bpy.data.lights.new("PreviewCoalInterior","POINT")
    light_data.energy=.06
    light_data.color=(1.,.006,.002)
    light_data.shadow_soft_size=.002
    light_data.use_custom_distance=True
    light_data.cutoff_distance=.05
    light=bpy.data.objects.new("PreviewCoalInterior",light_data)
    scene.collection.objects.link(light)
    light.location=coal_center
    shot("ember",coal_center,coal_center+Vector((-.09,-.10,.14)),65)
