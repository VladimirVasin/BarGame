#!/usr/bin/env python3
"""Compare the published hero before/after corruption with a real FBX import."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import sys
import bpy
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[1]


def snapshot(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_custom_normals=True)
    for obj in bpy.data.objects:
        if obj.type=="ARMATURE": obj.data.pose_position="REST"
    bpy.context.view_layer.update()
    rows={}
    for obj in bpy.data.objects:
        if obj.type!="MESH":continue
        obj.data.calc_loop_triangles()
        uv=obj.data.uv_layers.get("UVMap")
        rows[obj.name]=dict(points=[list(obj.matrix_world@v.co) for v in obj.data.vertices],
            indices=[list(t.vertices) for t in obj.data.loop_triangles],
            uv=[[[obj.data.loops[li].vertex_index, *[round(float(v),6) for v in uv.data[li].uv]]
                 for li in t.loops] for t in obj.data.loop_triangles] if uv else [],
            weights=[sorted((obj.vertex_groups[g.group].name,round(float(g.weight),6))
                for g in vertex.groups if g.weight>1e-6) for vertex in obj.data.vertices],
            shapes={key.name:[list(obj.matrix_world@v.co) for v in key.data]
                    for key in obj.data.shape_keys.key_blocks} if obj.data.shape_keys else {},
            topology=sorted(tuple(sorted(tuple(round(c,7) for c in obj.matrix_world@obj.data.vertices[index].co)
                for index in t.vertices)) for t in obj.data.loop_triangles),
            triangles=len(obj.data.polygons), matrix=[list(row) for row in obj.matrix_world],
            shape_names=list(obj.data.shape_keys.key_blocks.keys()) if obj.data.shape_keys else [])
    return rows


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline",type=Path,required=True)
    parser.add_argument("--model",type=Path,default=ROOT/"Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx")
    parser.add_argument("--source",type=Path)
    parser.add_argument("--report",type=Path,default=ROOT/"Captures/hero-corruption-source/export-comparison.json")
    parser.add_argument("--inspect",action="store_true",help="Write diagnostics even when a source contract differs")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    before,after=snapshot(args.baseline),snapshot(args.model)
    rows=[]
    for name,a in before.items():
        b=after.get(name)
        if b is None:
            rows.append(dict(name=name,missing=True));continue
        kd=KDTree(len(b["points"]))
        for i,p in enumerate(b["points"]):kd.insert(p,i)
        kd.balance()
        delta=max((kd.find(p)[2] for p in a["points"]),default=0.)
        before_kd=KDTree(len(a["points"]))
        for i,p in enumerate(a["points"]):before_kd.insert(p,i)
        before_kd.balance()
        indices=[before_kd.find(p)[1] for p in b["points"]]
        before_indices=[before_kd.find(p)[1] for p in a["points"]]
        before_faces=sorted(tuple(sorted(before_indices[i] for i in t)) for t in a["indices"])
        after_faces=sorted(tuple(sorted(indices[i] for i in t)) for t in b["indices"])
        before_uv=sorted(tuple(sorted((before_indices[r[0]],*r[1:]) for r in t)) for t in a["uv"])
        after_uv=sorted(tuple(sorted((indices[r[0]],*r[1:]) for r in t)) for t in b["uv"])
        same_weights=all(b["weights"][i]==a["weights"][indices[i]] for i in range(len(indices)))
        shape_error=0.
        for shape,points in a["shapes"].items():
            target=b["shapes"].get(shape)
            if target is None:shape_error=float("inf");break
            for i,p in enumerate(target):
                shape_error=max(shape_error,sum((p[axis]-points[indices[i]][axis])**2 for axis in range(3))**.5)
        rows.append(dict(name=name,before_vertices=len(a["points"]),after_vertices=len(b["points"]),
            maximum_nearest_metres=delta,same_triangles=before_faces==after_faces,
            same_uv=before_uv==after_uv,same_weights=same_weights,existing_shape_error_metres=shape_error,
            before_triangle_count=len(a["topology"]),after_triangle_count=len(b["topology"]),
            before_shapes=a["shape_names"],after_shapes=b["shape_names"]))
    payload=dict(changed=[row for row in rows if row.get("maximum_nearest_metres",1)>1e-6 or row.get("before_vertices")!=row.get("after_vertices") or not row.get("same_triangles",False) or not row.get("same_uv",False) or not row.get("same_weights",False) or row.get("existing_shape_error_metres",1)>1e-6],
                 total=len(rows))
    if args.source:
        bpy.ops.wm.open_mainfile(filepath=str(args.source))
        payload["source_modifiers"]={obj.name:[[m.name,m.type,m.show_viewport,m.show_render] for m in obj.modifiers]
            for obj in bpy.data.objects if obj.type=="MESH" and obj.name in {r["name"] for r in payload["changed"]}}
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(payload,indent=2)+"\n")
    print("HERO_CORRUPTION_EXPORT "+json.dumps(payload),flush=True)
    if payload["changed"] and not args.inspect:
        raise RuntimeError("Hero corruption changed an existing published FBX surface contract")


if __name__=="__main__":main()
