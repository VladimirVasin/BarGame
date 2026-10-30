#!/usr/bin/env python3
"""Measure real corrupted facial triangles against the underlying cranial shell."""
from __future__ import annotations
import argparse
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[1]


def points(obj,amount):
    keys=obj.data.shape_keys
    basis=keys.key_blocks[0].data if keys else obj.data.vertices
    key=keys.key_blocks.get("HeroCorruption") if keys else None
    return [obj.matrix_world @ (v.co.lerp(key.data[i].co,amount) if key else v.co)
            for i,v in enumerate(basis)]


def measure(amount):
    head,face=bpy.data.objects["GEO_Head"],bpy.data.objects["GEO_FaceSurface"]
    head.data.calc_loop_triangles();face.data.calc_loop_triangles()
    skull=BVHTree.FromPolygons(points(head,amount),[tuple(t.vertices) for t in head.data.loop_triangles],all_triangles=True)
    vertices=points(face,amount)
    uv=face.data.uv_layers["UVMap"]
    rows=[]
    for yaw in (-40,-25,0,25,40):
        for pitch in (0,20,40):
            y,p=math.radians(yaw),math.radians(pitch)
            direction=Vector((math.sin(y)*math.cos(p),-math.cos(y)*math.cos(p),math.sin(p)))
            minimum=1.;penetrations=0;samples=0;worst=None
            for triangle in face.data.loop_triangles:
                coords=[vertices[i] for i in triangle.vertices]
                texels=[uv.data[i].uv for i in triangle.loops]
                for first in range(13):
                    for second in range(13-first):
                        a,b=first/12.,second/12.;c=1-a-b
                        texel=texels[0]*a+texels[1]*b+texels[2]*c
                        if not (.12<texel.x<.88 and .10<texel.y<.90):continue
                        point=coords[0]*a+coords[1]*b+coords[2]*c
                        location,_,_,distance=skull.ray_cast(point+direction*.5,-direction,1.)
                        if location is None:continue
                        clearance=distance-.5;samples+=1
                        if clearance<0:penetrations+=1
                        if clearance<minimum:minimum=clearance;worst=[float(v) for v in texel]
            rows.append(dict(yaw=yaw,pitch=pitch,minimum_m=minimum,penetrations=penetrations,samples=samples,worst_uv=worst))
    return rows


def normals():
    result={}
    for name in ("GEO_Head","GEO_FaceSurface"):
        obj=bpy.data.objects[name];obj.data.calc_loop_triangles()
        base,target=points(obj,0.),points(obj,1.)
        dots=[]
        for t in obj.data.loop_triangles:
            a,b,c=t.vertices
            n0=(base[b]-base[a]).cross(base[c]-base[a]).normalized()
            n1=(target[b]-target[a]).cross(target[c]-target[a]).normalized()
            dots.append(n0.dot(n1))
        result[name]=dict(minimum_normal_dot=min(dots),reversed_triangles=sum(value<=0 for value in dots))
    return result


def exported_shape_normals():
    from io_scene_fbx import parse_fbx
    root,_=parse_fbx.parse(str(ROOT/"Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx"))
    objects=next(e for e in root.elems if e.id==b"Objects")
    connections=next(e for e in root.elems if e.id==b"Connections")
    nodes={e.props[0]:e for e in objects.elems if e.props}
    parents={e.props[1]:e.props[2] for e in connections.elems if e.id==b"C" and e.props[0]==b"OO"}
    result={}
    for key,node in nodes.items():
        if node.id!=b"Geometry" or node.props[-1]!=b"Shape" or b"HeroCorruption" not in node.props[1]:continue
        link=key
        for _ in range(4):
            link=parents.get(link)
            target=nodes.get(link)
            if target is not None and target.id==b"Geometry" and target.props[-1]==b"Mesh":break
        else:continue
        name=target.props[1].split(b"\x00",1)[0].decode()
        if "Head" not in name and "FaceSurface" not in name:continue
        array=next(e.props[0] for e in node.elems if e.id==b"Normals")
        lengths=[sum(float(c)**2 for c in array[i:i+3])**.5 for i in range(0,len(array),3)]
        result[name]=dict(delta_normal_count=len(lengths),maximum_delta_normal=max(lengths),
            nonfinite=sum(not math.isfinite(v) for v in lengths))
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source",type=Path,default=ROOT/"ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend")
    parser.add_argument("--report",type=Path,default=ROOT/"Captures/hero-corruption-source/face-clearance.json")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    bpy.ops.wm.open_mainfile(filepath=str(args.source))
    for obj in bpy.data.objects:
        if obj.type=="ARMATURE":
            obj.animation_data_clear();obj.data.pose_position="REST"
    bpy.context.view_layer.update()
    result={str(amount):measure(amount) for amount in (0.,.4,1.)}
    result["normals"]=normals()
    result["exported_shape_normals"]=exported_shape_normals()
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(result,indent=2)+"\n")
    print("HERO_CORRUPTION_CLEARANCE "+json.dumps(result),flush=True)
    for amount in ("0.0", "0.4", "1.0"):
        if any(row["penetrations"] or row["minimum_m"] < .003 for row in result[amount]):
            raise RuntimeError("Corrupted face lost its three-millimetre cranial clearance")
    if any(row["reversed_triangles"] for row in result["normals"].values()):
        raise RuntimeError("Corrupted head or face has reversed triangles")


if __name__=="__main__":main()
