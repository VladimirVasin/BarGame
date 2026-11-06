#!/usr/bin/env python3
"""Deterministic fixed-metre passive municipal fittings for covered City passages.

Use tools/run-blender.py. The wall kits face Unity +Z with their back at Z=0.
The lamp mounts at ceiling Y=0 and extends down. No fixture owns a runtime Light.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("passage_kit", ROOT / "tools/build-city-residential-courtyard-3d-model.py")
kit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kit)
port = kit.port
port.SURFACE_TILES.update({"Lens": 1.0, "Graffiti": 1.0})
MODEL_DIR = ROOT / "Assets/Resources/City/CoveredPassage"
SOURCE_DIR = ROOT / "ArtSource/CityCoveredPassage"
MANIFEST = "CityCoveredPassage3D.json"
NAMES = ("PassageLamp", "JunctionBox", "ConduitRun", "CableTray", "VentGrille", "CornerGuard", "PlasterPatch", "GraffitiPanel")
ROLES = ("SteelDark", "Steel", "SteelLight", "Concrete", "Plaster", "Rubber", "Lens", "Graffiti")
ENVELOPES = (
    ((-.59, -.14, -.12), (.59, 0., .12)),
    ((-.25, -.31, 0.), (.25, .31, .142)),
    ((-.045, -.90, 0.), (.045, .90, .06)),
    ((-1., -.08, 0.), (1., .065, .15)),
    ((-.31, -.23, 0.), (.31, .23, .08)),
    ((-.06, -.60, 0.), (.06, .60, .12)),
    ((-.85, -.575, 0.), (.85, .575, .012)),
    ((-.5, -.5, 0.), (.5, .5, 0.)),
)
BUDGETS = (3500, 2200, 2000, 3000, 2600, 1500, 400, 2)


def group(role):
    return kit.geometry(role)


def emit(g, name, root, material):
    return kit.emit(g, name, root, material)


def box(g, position, size, color=port.METAL, bevel=.006):
    kit.box(g, position, size, color, bevel)


def screw(g, x, y, z, radius=.012):
    # A real shallow hex head, with no floating decal at the contact face.
    g.rod((x, y, z-.005), (x, y, z+.005), radius, port.DARK, 6)


def passage_lamp(material):
    root = port.empty("PassageLamp")
    steel, cage, lens = group("Steel"), group("SteelDark"), group("Lens")
    box(steel, (0, -.04, 0), (1.18, .08, .24), port.CABIN, .014)
    for x in (-.565, .565):
        box(cage, (x, -.09, 0), (.05, .10, .24), port.METAL, .009)
    box(lens, (0, -.102, 0), (1.04, .056, .156), port.CABIN, .016)
    for z in (-.093, .093):
        cage.rod((-.53, -.137, z), (.53, -.137, z), .003, port.METAL, 6)
    for x in (-.44, -.15, .15, .44):
        cage.rod((x, -.137, -.093), (x, -.137, .093), .003, port.METAL, 6)
    for x in (-.51, .51):
        for z in (-.084, .084):
            # The screw is vertical because this is the ceiling-facing part.
            steel.rod((x, -.008, z), (x, 0, z), .012, port.DARK, 6)
    emit(steel, "FoldedCeilingChassis", root, material)
    emit(cage, "TamperCapsAndProtectiveCage", root, material)
    emit(lens, "OpalFluorescentDiffuser", root, material)
    kit.anchor(root, "ANCHOR_LightSource", (0, -.145, 0))
    kit.anchor(root, "ANCHOR_CeilingMount", (0, 0, 0))
    return root


def junction_box(material):
    root = port.empty("JunctionBox")
    casing, hardware = group("Steel"), group("SteelDark")
    box(casing, (0, 0, .01), (.50, .62, .02), port.PAINT, .012)
    box(casing, (0, 0, .06), (.47, .59, .10), port.PAINT, .018)
    box(casing, (0, 0, .122), (.454, .574, .036), port.PAINT, .012)
    for x in (-.195, .195):
        for y in (-.253, .253):
            screw(hardware, x, y, .137, .009)
    for x in (-.12, .12):
        box(hardware, (x, -.285, .052), (.055, .05, .055), port.DARK, .006)
    emit(casing, "RepaintedServiceCaseAndLid", root, material)
    emit(hardware, "FastenersAndCableGlands", root, material)
    return root


def conduit_run(material):
    root = port.empty("ConduitRun")
    pipe, clips = group("Steel"), group("SteelDark")
    pipe.rod((0, -.90, .035), (0, .90, .035), .019, port.PAINT, 8)
    for y in (-.70, 0., .70):
        box(clips, (0, y, .0275), (.09, .035, .055), port.METAL, .004)
        for x in (-.031, .031):
            screw(clips, x, y, .055, .005)
    emit(pipe, "ContinuousVerticalConduit", root, material)
    emit(clips, "WallSaddlesAndBolts", root, material)
    return root


def cable_tray(material):
    root = port.empty("CableTray")
    metal, dark, cable = group("Steel"), group("SteelDark"), group("Rubber")
    box(metal, (0, -.020, .065), (2., .025, .13), port.METAL, .004)
    box(metal, (0, .016, .140), (2., .098, .020), port.METAL, .004)
    box(metal, (0, .008, .010), (2., .080, .020), port.METAL, .004)
    for x in (-.72, .72):
        box(dark, (x, -.055, .032), (.065, .05, .064), port.DARK, .004)
    for z in (.035, .068, .101):
        cable.rod((-.975, .010, z), (.975, .010, z), .009, port.DARK, 6)
    emit(metal, "FoldedOpenServiceTray", root, material)
    emit(dark, "TrayBearingClips", root, material)
    emit(cable, "ThreeSupportedCableRuns", root, material)
    return root


def vent_grille(material):
    root = port.empty("VentGrille")
    frame, slats, recess = group("Steel"), group("SteelLight"), group("SteelDark")
    for x in (-.2875, .2875):
        box(frame, (x, 0, .028), (.045, .46, .056), port.PAINT, .006)
    for y in (-.2075, .2075):
        box(frame, (0, y, .028), (.53, .045, .056), port.PAINT, .006)
    box(recess, (0, 0, .007), (.53, .37, .014), port.DARK, .004)
    for index in range(7):
        y = -.158 + index * .0525
        box(slats, (0, y, .056), (.522, .030, .048), port.CABIN, .005)
    for x in (-.285, .285):
        for y in (-.205, .205):
            screw(recess, x, y, .056, .009)
    emit(frame, "BoltedVentFrame", root, material)
    emit(slats, "SeparateVentLouvres", root, material)
    emit(recess, "VentRecessAndFasteners", root, material)
    return root


def corner_guard(material):
    root = port.empty("CornerGuard")
    steel, bolts = group("Steel"), group("SteelDark")
    box(steel, (0, 0, .010), (.12, 1.20, .020), port.PAINT, .004)
    box(steel, (-.050, 0, .070), (.020, 1.20, .100), port.PAINT, .004)
    for y in (-.48, 0, .48):
        screw(bolts, .027, y, .021, .008)
    emit(steel, "FoldedLSectionEdgeGuard", root, material)
    emit(bolts, "GuardFixings", root, material)
    return root


def plaster_patch(material):
    root = port.empty("PlasterPatch")
    plaster = group("Plaster")
    outline = [(-.85,-.39),(-.81,-.50),(-.56,-.52),(-.48,-.575),(-.14,-.55),
               (.04,-.565),(.16,-.51),(.52,-.53),(.57,-.46),(.81,-.44),(.85,-.24),
               (.80,.04),(.83,.29),(.70,.34),(.67,.51),(.43,.50),(.37,.575),
               (.12,.54),(-.11,.56),(-.19,.50),(-.49,.52),(-.56,.46),(-.77,.43),(-.81,.21)]
    n = len(outline)
    vertices = [(x,y,z) for z in (0.,.012) for x,y in outline]
    faces = [tuple(reversed(range(n))), tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)]
    plaster.add(vertices, faces, port.CABIN)
    emit(plaster, "IrregularTrowelledRepair", root, material)
    return root


def graffiti_panel(material):
    root = port.empty("GraffitiPanel")
    g = group("Graffiti")
    g.add([(-.5,-.5,0),(.5,-.5,0),(.5,.5,0),(-.5,.5,0)], [(0,1,2,3)],
          port.CABIN, [(0,0),(1,0),(1,1),(0,1)], solid=False)
    emit(g, "GraffitiSurface", root, material)
    # Semantic metre projection would alter the requested unit atlas domain. Keep
    # the authored UVs instead, while retaining the source-kit axis/winding contract.
    mesh = next(o.data for o in root.children_recursive if o.type == "MESH")
    for loop in mesh.loops:
        p = port.source(mesh.vertices[loop.vertex_index].co)
        mesh.uv_layers.active.data[loop.index].uv = (p[0]+.5,p[1]+.5)
    return root


def build():
    mat = port.base.material("CoveredPassageSource")
    roots = [builder(mat) for builder in (passage_lamp,junction_box,conduit_run,cable_tray,
                                        vent_grille,corner_guard,plaster_patch,graffiti_panel)]
    port.validate_surfaces(roots)
    models = []
    for index,root in enumerate(roots):
        model = port.describe(root)
        meshes = [o for o in root.children_recursive if o.type == "MESH"]
        roles = sorted({o.name.rsplit("__",1)[1].split(".")[0] for o in meshes})
        for key,wanted in zip(("bounds_min","bounds_max"), ENVELOPES[index]):
            if any(abs(actual-target) > .012 for actual,target in zip(model[key],wanted)):
                raise ValueError(f"Covered passage metric envelope differs: {root.name} {key}={model[key]}, expected {wanted}")
        if model["triangles"] > BUDGETS[index] or not set(roles).issubset(ROLES):
            raise ValueError(f"Covered passage mesh/role budget exceeded: {model}")
        model.update({"material_roles":roles,"renderer_count":len(meshes),"maximum_triangles":BUDGETS[index]})
        models.append(model)
    panel = next(o for o in roots[-1].children_recursive if o.type == "MESH")
    normal = port.source(panel.data.polygons[0].normal)
    if normal[2] < .999 or any(v < -.00001 or v > 1.00001
                             for uv in panel.data.uv_layers.active.data for v in uv.uv):
        raise ValueError("Graffiti panel must face Unity +Z with unit UVs")
    return roots,{"design_id":"city_covered_passage_kit_v1","version":"1.0.0",
        "generator":Path(__file__).name,"coordinate_system":"Unity metres, +Y up, wall +Z outward; lamp root at ceiling Y0 extends down",
        "geometry_signature_includes":["vertices","faces","colors","uv0","transforms"],
        "models":models,"assets":[name+".fbx" for name in NAMES],
        "graffiti_atlas":{"columns":4,"rows":2,"uv_origin":"bottom-left","cell_padding_fraction":.07},
        "runtime_contract":"Retain imported FBX authoring wrapper and units; shared semantic materials; no collision, text, Light or behaviour authored in the models"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=MODEL_DIR)
    parser.add_argument("--source-dir",type=Path,default=SOURCE_DIR)
    parser.add_argument("--validate-only",action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    port.base.reset()
    roots,data = build()
    if args.validate_only:
        if json.loads((args.model_dir/MANIFEST).read_text()) != data:
            raise ValueError("Covered passage manifest differs from deterministic geometry")
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        for root in roots: port.base.export(root,args.model_dir/(root.name+".fbx"))
        (args.model_dir/MANIFEST).write_text(json.dumps(data,indent=2)+"\n")
        for index,root in enumerate(roots): root.location = port.source((index*2.6,0,0))
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/"CityCoveredPassage3D.blend"),check_existing=False)
    port.base.reset()
    if build()[1] != data: raise ValueError("Covered passage deterministic rebuild differs")
    print("CITY COVERED PASSAGE ART CONTRACT OK: metric geometry, outward winding, semantic UVs, unit graffiti plane and deterministic rebuild")
    print(json.dumps({"models":[{k:m[k] for k in ("name","bounds_min","bounds_max","triangles","renderer_count")} for m in data["models"]]}))


if __name__ == "__main__": main()
