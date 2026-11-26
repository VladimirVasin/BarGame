#!/usr/bin/env python3
"""Build a dry, curled courtyard leaf in metres and its measured mesh contract."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import zlib

import bpy

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Resources/City/Grass"
NAME = "JanitorLeaf"


def mesh_data():
    # Stations from stalk to tapered tip. Asymmetry and a folded midrib catch side light.
    stations = ((-.084,.0015,.003),(-.061,.002,.002),(-.055,.014,.003),
                (-.030,.034,.006),(.004,.047,.012),(.029,.036,.016),
                (.054,.024,.014),(.082,.001,.020))
    upper = []
    for i,(y, width, height) in enumerate(stations):
        bias = .003*math.sin(i*.71)
        upper.extend(((bias-width, y, max(.0013,height-width*.18)),
                      (bias, y, height),
                      (bias+width*(.87+.11*math.sin(i)), y+.002*math.sin(i), max(.002,height-width*.11))))
    count = len(upper)
    vertices = upper + [(x,y,z-.0013) for x,y,z in upper]
    faces = []
    for k in range(len(stations)-1):
        for side in range(2):
            a = 3*k + side
            face = (a, a+1, a+4, a+3)
            faces.extend((face,tuple(count+j for j in reversed(face))))
    rim = [0,1,2] + list(range(5,count,3)) + [count-2] + list(range(count-3,0,-3))
    for i,a in enumerate(rim):
        b = rim[(i+1)%len(rim)]
        faces.append((a+count,b+count,b,a))
    return vertices, faces


def albedo():
    # Shared neutral dry lamina; each leaf has its own restrained pigment in the street.
    n = 64
    rows = []
    for row in range(n):
        y = 1 - (row+.5)/n
        pixels = bytearray()
        for column in range(n):
            x = (column+.5)/n-.5
            rib = math.exp(-((x-.022*math.sin(y*4))/.019)**2)
            veins = (y-abs(x)*.52)*6
            branch = max(0., 1-abs(veins-round(veins))/.042)
            grain = math.sin(column*3.71+row*7.3) + math.sin(column*.67+row*1.19)
            value = round(207 + 16*rib + 7*branch + 4*grain - 12*abs(x))
            pixels.extend((value, value-3, value-7))
        rows.append(b"\0"+pixels)
    def chunk(tag,data):
        return struct.pack(">I",len(data))+tag+data+struct.pack(">I",zlib.crc32(tag+data))
    return (b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",n,n,8,2,0,0,0))+
            chunk(b"IDAT",zlib.compress(b"".join(rows),9))+chunk(b"IEND",b""))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=OUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    vertices, faces = mesh_data()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = bpy.data.meshes.new(NAME)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.calc_loop_triangles()
    assert not mesh.validate()
    volume = sum(mesh.vertices[a].co.dot(mesh.vertices[b].co.cross(mesh.vertices[c].co))/6
                 for a,b,c in (tri.vertices for tri in mesh.loop_triangles))
    assert volume > .000005 and min(v[2] for v in vertices) >= 0.
    uv = mesh.uv_layers.new(name="Lamina")
    for loop in mesh.loops:
        x,y,_ = vertices[loop.vertex_index]
        uv.data[loop.index].uv = (x/.104+.5,(y+.085)/.17)
    obj = bpy.data.objects.new(NAME,mesh)
    bpy.context.collection.objects.link(obj)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    pixels = albedo()
    assert (vertices,faces) == mesh_data() and pixels == albedo()
    manifest = {"generator":Path(__file__).name, "triangles":len(mesh.loop_triangles),
                "dimensions_metres":[round(max(v[i] for v in vertices)-min(v[i] for v in vertices),5) for i in (0,2,1)],
                "signature":hashlib.sha256(json.dumps([vertices,faces]).encode()+pixels).hexdigest()}
    target = args.model_dir
    if args.validate_only:
        assert manifest == json.loads((target/(NAME+".json")).read_text())
        assert (target/(NAME+"Albedo.png")).read_bytes() == pixels
        return
    target.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str((target/(NAME+".fbx")).resolve()),use_selection=True,
        object_types={"MESH"},axis_forward="-Z",axis_up="Y",bake_space_transform=False,
        apply_scale_options="FBX_SCALE_NONE",bake_anim=False,mesh_smooth_type="FACE")
    (target/(NAME+".json")).write_text(json.dumps(manifest,indent=2)+"\n")
    (target/(NAME+"Albedo.png")).write_bytes(pixels)


if __name__ == "__main__":
    main()
