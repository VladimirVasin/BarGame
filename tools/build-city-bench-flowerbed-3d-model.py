#!/usr/bin/env python3
"""A planted municipal trough, reusing the existing courtyard planter kit.

Fixed metres; shared concrete/soil/foliage and three non-emissive petal tints.
Only passive flower/stem/leaf geometry is new. Runtime owns placement, shared
material bindings and the modest edging collider. No lights or effects.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as bp
spec = importlib.util.spec_from_file_location("bench_flower_courtyard", ROOT / "tools/build-city-residential-courtyard-3d-model.py")
courtyard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(courtyard)
port = courtyard.port

NAME = "BenchFlowerbed"
MODEL_DIR = ROOT / "Assets/Resources/City/BenchAccess"
SOURCE_DIR = ROOT / "ArtSource/City/BenchAccess"
PETALS = {"Cream": (.84, .79, .64, 1), "Red": (.58, .22, .25, 1), "Purple": (.46, .34, .56, 1)}


def merge(first, second, name):
    bpy.ops.object.select_all(action="DESELECT")
    first.select_set(True); second.select_set(True)
    bpy.context.view_layer.objects.active = first
    bpy.ops.object.join()
    first.name = name
    return first


def mesh_of(group):
    return next(obj for obj in group.children_recursive if obj.type == "MESH")


def petal(geometry, center, angle, color):
    radial = Vector((math.sin(angle), 0, math.cos(angle)))
    across = Vector((math.cos(angle), 0, -math.sin(angle)))
    c = Vector(center)
    vertices = [c-radial*.005, c+radial*.027+across*.022,
                c+radial*.056+Vector((0,.008,0)), c+radial*.027-across*.022,
                c+radial*.026+Vector((0,.014,0)), c+radial*.026-Vector((0,.004,0))]
    faces = []
    for i in range(4):
        after = (i+1) % 4
        faces.extend(((4, i, after), (5, after, i)))
    geometry.add([tuple(v) for v in vertices], faces, color)


def build():
    port.base.reset()
    material = port.base.material("BenchFlowerbedSharedSurfaceSource")
    # Reuse the measured existing 1.3 x .65 trough and its soil/low leaves.
    root = courtyard.planter(material)
    root.name = NAME
    source_meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    edging = next(obj for obj in source_meshes if obj.name.endswith("__Concrete"))
    soil = next(obj for obj in source_meshes if obj.name.endswith("__Soil"))
    leaves = next(obj for obj in source_meshes if obj.name.endswith("__Foliage"))
    soil.name = "Soil__Plain"
    # A worn shallow rim raises the ordinary concrete edge to .35 m.
    cap = courtyard.geometry("Concrete")
    for z in (-.275,.275):
        courtyard.box(cap, (0,.32,z), (1.30,.06,.10), port.CONCRETE, .009)
    for x in (-.60,.60):
        courtyard.box(cap, (x,.32,0), (.10,.06,.45), port.CONCRETE, .009)
    merge(edging, mesh_of(courtyard.emit(cap,"FlowerbedRim",root,material)), "Edging__Concrete")
    greens = courtyard.geometry("Foliage")
    flowers = {name:courtyard.geometry("Plaster") for name in PETALS}
    for row,z in enumerate((-.13,0,.13)):
        for column,x in enumerate((-.44,-.22,0,.22,.44)):
            n = row*5+column
            px = x+.012*math.sin(n*2.1)
            pz = z+.009*math.cos(n*1.7)
            top = .48+.045*((column*2+row)%3)
            center = (px+.018*math.sin(n),top,pz+.015*math.cos(n))
            greens.rod((px,.24,pz),center,.0045,port.PAINT,5)
            for level in range(3):
                y = .305+level*.055
                direction = -1 if (level+n)%2 else 1
                courtyard.leaf(greens,(px,y,pz),
                               (px+direction*.075,y+.045,pz+(.04 if (n+level)%2 else -.04)),.021)
            color_name = ("Cream","Red","Purple")[(column+row*2)%3]
            color = PETALS[color_name]
            head = flowers[color_name]
            for index in range(6): petal(head,center,index*math.tau/6+n*.37,color)
            # A small raised centre makes each flower readable from above.
            head.rod((center[0],top+.004,center[2]),(center[0],top+.024,center[2]),
                     .011,color,8,end_radius=.009)
    merge(leaves,mesh_of(courtyard.emit(greens,"FlowerStems",root,material)),"Leaves__Plain")
    for name,geometry in flowers.items():
        mesh_of(courtyard.emit(geometry,"Flowers"+name,root,material)).name="Flowers"+name+"__Plaster"
    port.empty("ANCHOR_Ground",root,(0,0,0))
    return root


def validate(root):
    measured = port.describe(root)
    meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    assert len(meshes) == 6 and measured["triangles"] < 3500
    assert measured["bounds_min"] == [-.65,0,-.325]
    assert abs(measured["bounds_max"][0]-.65)<.00001 and abs(measured["bounds_max"][2]-.325)<.00001
    assert .59<=measured["bounds_max"][1]<=.64
    for obj in meshes:
        assert obj.data.uv_layers and obj.data.color_attributes
        assert bp.signed_volume(([tuple(v.co) for v in obj.data.vertices],
                                 [tuple(p.vertices) for p in obj.data.polygons]))>0
    return {"design_id":"city_bench_flowerbed_v1","version":"1.0.0","generator":Path(__file__).name,
            "reused_geometry":"CourtyardPlanter trough/soil/low leaves; unchanged source asset",
            "coordinate_system":"Unity metres +Y up/+Z forward; retain imported FBX unit root",
            "contract":{"footprint":[1.3,.65],"edging_height":.35,"soil_top":.245,
                        "height":measured["bounds_max"][1],"flower_heads":15,
                        "renderers":6,"collider_size":[1.3,.35,.65],"collider_center":[0,.175,0],
                        "bindings":{"Edging__Concrete":"Existing City concrete",
                                    "Soil__Plain":"Existing ground soil material/appearance",
                                    "Leaves__Plain":"Existing foliage material/appearance",
                                    **{"Flowers"+k+"__Plaster":{"shared":"Existing opaque Lit, white BaseMap, no emission",
                                                             "srgb_tint":list(v[:3])} for k,v in PETALS.items()}},
                        "no_animation_text_light_or_collision":True},"model":measured}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=MODEL_DIR)
    parser.add_argument("--source-dir",type=Path,default=SOURCE_DIR)
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    report=validate(build())
    assert validate(build())==report,"Flowerbed must rebuild deterministically"
    manifest=args.model_dir/(NAME+".json")
    if args.validate_only:
        assert json.loads(manifest.read_text())==report,"Flowerbed manifest is stale"
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
        port.base.export(bpy.data.objects[NAME],args.model_dir/(NAME+".fbx"))
        manifest.write_text(json.dumps(report,indent=2)+"\n")
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/(NAME+".blend")))
    print("BENCH FLOWERBED CONTRACT OK: reused trough, planted heads/leaves, fixed bounds, winding and deterministic rebuild")
    print(json.dumps(report["model"]))


if __name__=="__main__":main()
