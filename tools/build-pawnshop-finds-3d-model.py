#!/usr/bin/env python3
"""Generate the five hand-scale City finds with shared matte and metal finishes."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("street_models", TOOLS / "build-city-street-utilities-3d-model.py")
street = importlib.util.module_from_spec(spec)
spec.loader.exec_module(street)
from world_contact_finishes import BARE_METAL, RUBBER, response
street.FINISHES = {
    "paint": ((130, 55, 40), .30), "iron": ((116, 116, 104), response(BARE_METAL, .12)[0]),
    "rubber": ((42, 41, 34), response(RUBBER, .30)[0]), "enamel": ((179, 169, 134), .35),
    "paper": ((109, 85, 57), .16), "concrete": ((158, 151, 124), .31),
    "inside": ((70, 73, 60), .23), "edge": ((166, 168, 155), response(BARE_METAL, .42)[0]),
}
MATERIALS = {"default": "FindFinish", "Metal": "FindMetal", "SecondHand": "FindMetal"}


def screwdriver():
    m = street.Model()
    m.anchors["Grip"] = (-.026, .018, 0)
    m.box("Body", "paint", (-.026,.014,0), (.101,.023,.028), .005)
    m.box("Body", "iron", (-.025,.0035,0), (.096,.007,.027), .001)
    # Folded steel bit projects beyond the worn red scales.
    m.box("Metal", "iron", (.039,.010,0), (.073,.010,.014), .002)
    m.box("Metal", "edge", (.080,.010,0), (.018,.005,.012), .001)
    for x in (-.060,.013):
        m.rod("Metal", "edge", (x,.022,0), (x,.025,0), .004)
    m.box("Body", "rubber", (-.042,.026,.008), (.013,.001,.002), .0002, tilt=31)
    return m


def caliper():
    m = street.Model()
    m.anchors["Grip"] = (.014, .017, .003)
    m.box("Body", "paper", (.005,.002,0), (.191,.004,.057), .002, tilt=7)
    m.box("Metal", "iron", (.004,.010,0), (.185,.009,.014), .002)
    for x, size in ((-.084,.045),(-.037,.034)):
        m.box("Metal", "edge", (x,.011,.019), (.009,.012,size), .002)
    m.box("Metal", "iron", (-.023,.014,0), (.038,.015,.032), .003)
    m.rod("Metal", "edge", (-.026,.019,-.018), (-.012,.019,-.018), .006)
    for i in range(22):
        size = .008 if i % 5 == 0 else .004
        m.box("Body", "rubber", (-.016+i*.0045,.0146,.005-size*.5), (.0006,.0004,size), .00008)
    return m


def radio():
    m = street.Model()
    m.anchors["Grip"] = (.020,.025,-.012)
    m.box("Body", "inside", (0,.015,0), (.112,.030,.069), .006)
    m.box("Body", "rubber", (-.026,.031,0), (.044,.003,.048), .001)
    for j in range(9):
        m.box("Body", "iron", (-.026,.033,-.019+j*.0047), (.033,.0008,.0018), .00015)
    m.box("Body", "enamel", (.034,.031,.006), (.030,.003,.037), .001)
    for j in range(7):
        m.box("Body", "rubber", (.028,.033,-.007+j*.0043), (.009 if j%2 else .013,.0005,.001), .0001)
    m.rod("Metal", "edge", (.048,.020,-.038), (.051,.020,-.021), .007)
    m.box("Body", "rubber", (.039,.034,-.019), (.050,.0012,.013), .0002, tilt=24)
    m.box("Body", "iron", (-.049,.029,.029), (.014,.003,.013), .001, tilt=36)
    return m


def watch():
    m = street.Model()
    m.anchors.update(Grip=(.007,.013,.020), SecondHand=(0,.017,0))
    for pos, length, tilt in ((-.039,.034,-10),(.029,.016,13)):
        m.box("Body", "paper", (0,.004,pos), (.016,.008,length), .002, tilt=tilt)
    m.rod("Metal", "iron", (0,.003,0), (0,.015,0), .021)
    m.rod("Body", "enamel", (0,.015,0), (0,.016,0), .0175)
    m.rod("Metal", "edge", (.017,.010,0), (.024,.010,0), .003)
    for i in range(12):
        angle = i*math.tau/12
        p = (math.sin(angle)*.015,.0164,math.cos(angle)*.015)
        m.box("Body", "rubber", p, (.001,.0006,.0034), .00015, tilt=math.degrees(angle))
    for x,z in ((.006,-.002),(-.004,.005)):
        m.rod("Body", "rubber", (0,.0168,0), (x,.0168,z), .0008)
    m.rod("SecondHand", "edge", (0,.0175,-.003), (0,.0175,.012), .00035)
    return m


def ring():
    m = street.Model()
    m.anchors["Grip"] = (0,.003,.008)
    vertices, faces = [], []
    around, section = 24, 6
    for i in range(around):
        a = i*math.tau/around
        for j in range(section):
            b = j*math.tau/section
            radius = .0101 + .0018*math.cos(b)
            vertices.append((radius*math.cos(a),.0021+.0018*math.sin(b),radius*math.sin(a)))
    for i in range(around):
        for j in range(section):
            a, b = i*section, ((i+1)%around)*section
            k = (j+1)%section
            faces.append((a+j,b+j,b+k,a+k))
    m.add("Metal", "edge", (vertices, faces))
    return m


MODELS = {
    "FoldingScrewdriver": screwdriver, "Caliper": caliper, "PocketRadio": radio,
    "MechanicalWatch": watch, "SilverRing": ring,
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path, default=TOOLS.parent / "Assets/Resources/Pawnshop/Finds")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    street.OUT = args.model_dir
    street.OUT.mkdir(parents=True, exist_ok=True)
    street.atlas("PawnshopFindWear.png")
    contract = {}
    for name, build in MODELS.items():
        model = build()
        model.export(name, MATERIALS)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(street.OUT / (name + ".fbx")))
        meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
        points = [obj.matrix_world @ vert.co for obj in meshes for vert in obj.data.vertices]
        grip = bpy.data.objects["Grip"].matrix_world.translation
        low = [min(p[i] for p in points) for i in range(3)]
        high = [max(p[i] for p in points) for i in range(3)]
        if len(meshes) > 3 or abs(low[2]) > .004 or max(h-l for h,l in zip(high,low)) > .21:
            raise ValueError(f"{name} exceeds its metre bounds or surface budget")
        if not all(l-.001 <= grip[i] <= h+.001 for i,(l,h) in enumerate(zip(low,high))):
            raise ValueError(f"{name} grip is outside the imported object")
        contract[name] = {
            "minimum_unity": [round(low[i], 5) for i in (0,2,1)],
            "maximum_unity": [round(high[i], 5) for i in (0,2,1)],
            "grip_unity": [round(grip[i], 5) for i in (0,2,1)],
        }
    (street.OUT/"PawnshopFinds3D.json").write_text(json.dumps(contract, indent=2)+"\n")
    print("PAWNSHOP FINDS FBX CONTACTS OK")


if __name__ == "__main__":
    main()
