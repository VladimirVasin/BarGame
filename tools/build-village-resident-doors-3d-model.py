#!/usr/bin/env python3
"""Carve three existing village houses, retaining their default-seed exterior.

Only the original walls/plinth/timber are replaced. Roof, snow, chimney,
windows and weathering remain the original village kit. A small real L-shaped
entrance gives two active residents an occluded place to stay inside.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import interior_kit as kit
import bar_parts as bp
spec = importlib.util.spec_from_file_location("resident_house_source", ROOT / "tools/build-village-3d-model.py")
village = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = village
spec.loader.exec_module(village)
VERSION = "1.0.0"
DESIGN = "village_resident_doorways_v1"
# Exact float32 results of the pre-door planner at seed 20260727.
HOUSES = ((4, 7.2338385581970215, 6.997286796569824, 6.366440773010254, -.09230518341064453),
          (8, 7.917183876037598, 6.21844482421875, 5.696389198303223, -.003998279571533203),
          (11, 6.618419170379639, 7.481746196746826, 6.358501434326172, -.023012757301330566))
ROLES = ("Walls", "Plinth", "Timber")


def mesh_object(name, geometry, root, material, original_uv=False):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(geometry[0], [], geometry[1]); mesh.update(calc_edges=True)
    if original_uv:
        village.assign_uv(mesh, geometry)
    else:
        uv = mesh.uv_layers.new(name="UVMap")
        for face in mesh.polygons:
            axis = max(range(3), key=lambda i: abs(face.normal[i]))
            axes = (0, 1) if axis == 2 else ((0, 2) if axis == 1 else (1, 2))
            for loop in face.loop_indices:
                point = mesh.vertices[mesh.loops[loop].vertex_index].co
                uv.data[loop].uv = (point[axes[0]], point[axes[1]])
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(obj)
    obj.parent = root
    return obj


def geometry(obj):
    return ([tuple(v.co) for v in obj.data.vertices], [tuple(p.vertices) for p in obj.data.polygons])


def cut_box(obj, name, center, size):
    cutter = mesh_object(name, kit.box(center, size), None, obj.data.materials[0])
    modifier = obj.modifiers.new(name, "BOOLEAN")
    modifier.operation = "DIFFERENCE"; modifier.solver = "EXACT"
    modifier.use_self = True; modifier.object = cutter
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def part_row(obj, role, surface, tint):
    verts, faces = geometry(obj)
    low, high = kit.bounds(([(x, z, y) for x, y, z in verts], faces))
    assert all(math.isfinite(c) for v in verts for c in v)
    assert village.signed_volume((verts, faces)) > 0, obj.name
    return dict(mesh=obj.name, role=role, surface=surface, tint=tint,
                bounds_min=low, bounds_max=high, triangles=sum(len(f)-2 for f in faces))


def build_pack():
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
    mats = {}
    for name, color in (("Stone", (.29,.30,.27,1)), ("Wood", (.23,.19,.14,1)),
                        ("Dark", (.14,.13,.105,1)), ("Iron", (.24,.23,.20,1))):
        material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        material.diffuse_color = color; mats[name] = material
    houses, objects = [], []
    for index, width, depth, height, across in HOUSES:
        name = f"village-house-{index:02d}"
        root = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(root)
        wall = .430 * depth
        parts, shell_objects = [], []
        for part in village.build_village_house(1).parts:
            if part.part_role not in ROLES: continue
            obj = mesh_object(f"GEO_House{index:02d}_{part.part_role}", part.geometry, root,
                              mats["Stone" if part.part_role == "Plinth" else "Wood"], True)
            for vertex in obj.data.vertices:
                x,y,z = vertex.co; vertex.co = (x*width, y*depth, (z+.5)*height)
            before = kit.bounds(geometry(obj))
            # Door aperture, wider passage behind the jambs, then one side pocket.
            for suffix, center, size in (
                ("aperture", (across, wall-.07, 1.075), (1.04,.80,2.19)),
                ("passage", (across, wall-1.67, 1.115), (1.72,2.88,2.27)),
                ("pocket", (across+1.435, wall-2.25, 1.115), (1.99,1.70,2.27))):
                cut_box(obj, suffix, center, size)
            after = kit.bounds(geometry(obj))
            assert all(abs(a-b)<.00002 for p,q in zip(before,after) for a,b in zip(p,q)), (name,part.part_role,"silhouette changed")
            parts.append(part_row(obj, part.part_role, part.surface_kind,
                                  list(mats["Stone" if part.part_role == "Plinth" else "Wood"].diffuse_color)))
            shell_objects.append(obj); objects.append(obj)
        # An actual floor/ceiling, and dark lining inside the already carved volume.
        lining = [bp.u_box((across, -.065, wall-1.55), (1.71,.13,3.12), .006),
                  bp.u_box((across+1.43,-.065,wall-2.25),(1.99,.13,1.70),.006),
                  bp.u_box((across,2.225,wall-1.67),(1.71,.055,2.88),.003),
                  bp.u_box((across+1.43,2.225,wall-2.25),(1.99,.055,1.70),.003)]
        obj = mesh_object(f"GEO_House{index:02d}_Lining", bp.to_source(kit.merge_all(lining)), root, mats["Dark"])
        parts.append(part_row(obj,"Lining","Timber",list(mats["Dark"].diffuse_color))); objects.append(obj)
        anchors = {"Threshold":(across,0,wall+.04), "Interior":(across,0,wall-1.93),
                   "Turn0":(across+.98,0,wall-1.75), "Turn1":(across+.98,0,wall-2.70),
                   "Hidden0":(across+2.08,0,wall-1.75),
                   "Hidden1":(across+2.08,0,wall-2.70), "Hinge":(across-.49,.03,wall+.05)}
        # Geometry, not an out-of-camera toggle, hides both parked bodies.
        trees = [BVHTree.FromPolygons(*geometry(obj), all_triangles=False) for obj in shell_objects]
        for elevation in (.35,1.1,1.75):
            start = Vector((across,wall+.8,elevation))
            end = Vector((across,wall-1.93,elevation))
            direction = end-start
            assert not any(tree.ray_cast(start,direction.normalized(),direction.length)[0] is not None for tree in trees), (name,"blocked aperture")
            for key in ("Hidden0","Hidden1"):
                target = anchors[key]; end = Vector((target[0],target[2],elevation)); direction=end-start
                assert any(tree.ray_cast(start,direction.normalized(),direction.length)[0] is not None for tree in trees), (name,key,"visible parked resident")
        for slot in (0,1):
            parked=Vector(anchors["Hidden"+str(1-slot)])
            route=[Vector(anchors[key]) for key in ("Interior","Turn"+str(slot),"Hidden"+str(slot))]
            for a,b in zip(route,route[1:]):
                axis=b-a; t=max(0,min(1,(parked-a).dot(axis)/axis.length_squared))
                assert (a+axis*t-parked).length>.90,(name,"parked neighbour blocks independent interior route")
        houses.append(dict(plot_id=name, width=width, depth=depth, height=height, door_across=across,
                           wall_face=wall, parts=parts, anchors=[dict(name=n,position=p) for n,p in anchors.items()]))
    # These fixed-metre parts are shared by all three portals. Only the hinge rotates.
    root = bpy.data.objects.new("Door", None); bpy.context.scene.collection.objects.link(root)
    frame = kit.door_frame(1.0,2.12,.28,.065,.025,.008)
    leaf = kit.panelled_leaf(.98,2.05,.065,3,.10,.014,.008)
    handle = kit.merge(bp.u_box((.83,1.01,.052),(.038,.16,.026),.005),
                       bp.u_box((.76,1.01,.075),(.16,.028,.035),.005))
    rows=[]
    for role,geom,surface,tint in (("Frame",frame,"Timber","Wood"),
                                  ("Leaf",leaf,"Timber","Wood"),
                                  ("Handle",bp.to_source(handle),"RustedIron","Iron")):
        obj=mesh_object("GEO_Door_"+role,geom,root,mats[tint]); objects.append(obj)
        rows.append(part_row(obj,role,surface,list(mats[tint].diffuse_color)))
    digest=hashlib.sha256()
    for obj in objects:
        digest.update(json.dumps((obj.name,[[round(c,7) for c in v] for v in geometry(obj)[0]],geometry(obj)[1]),separators=(",",":")).encode())
    return dict(generator_version=VERSION,design_id=DESIGN,build_signature=digest.hexdigest(),
                scale_mode="fixed_metres",mesh_count=len(objects),houses=houses,door_parts=rows), objects


def preview(path,objects):
    # Show one complete original roof over its carved lower storey, through the open door.
    for obj in objects: obj.hide_render = not obj.name.startswith("GEO_House04_")
    index,width,depth,height,across=HOUSES[0]
    wall=depth*.43
    for part in village.build_village_house(1).parts:
        if part.part_role in ROLES: continue
        obj=mesh_object("Review_"+part.part_role,part.geometry,None,bpy.data.materials["Wood"],True)
        for vertex in obj.data.vertices:
            x,y,z=vertex.co;vertex.co=(x*width,y*depth,(z+.5)*height)
    scene=bpy.context.scene
    cam=bpy.data.objects.new("Review Camera",bpy.data.cameras.new("Review Camera")); scene.collection.objects.link(cam)
    cam.location=(across+.8,wall+3.3,1.65)
    cam.rotation_euler=(Vector((across,wall-1,1.1))-cam.location).to_track_quat("-Z","Y").to_euler()
    cam.data.lens=26;scene.camera=cam;scene.render.engine="BLENDER_WORKBENCH"
    scene.display.shading.light="STUDIO";scene.display.shading.color_type="MATERIAL"
    scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True
    scene.world.color=(.09,.10,.105);scene.render.resolution_x=1280;scene.render.resolution_y=800
    scene.render.resolution_percentage=100;scene.render.image_settings.file_format="PNG"
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/VillageLife")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/VillageLife")
    parser.add_argument("--validate-only",action="store_true");parser.add_argument("--no-preview",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    first,_=build_pack();data,objects=build_pack()
    assert first==data,"Non-deterministic doorway Boolean result"
    if args.validate_only:
        assert json.loads((args.model_dir/"VillageResidentDoors3D.json").read_text())==json.loads(json.dumps(data))
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.export_scene.fbx(filepath=str(args.model_dir/"VillageResidentDoors3D.fbx"),use_selection=True,
            object_types={"EMPTY","MESH"},axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
            bake_space_transform=True,add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE")
        (args.model_dir/"VillageResidentDoors3D.json").write_text(json.dumps(data,indent=2)+"\n",encoding="utf-8")
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"VillageResidentDoors3D.blend"))
        if not args.no_preview:preview(args.source_dir/"VillageResidentDoors3D.png",objects)
    print("VILLAGE RESIDENT DOORS VALIDATION OK: 3 unchanged exterior envelopes, clear apertures, 6 occluded parking docks; "+data["build_signature"])


if __name__=="__main__":main()
