#!/usr/bin/env python3
"""Metric kit for the inhabited Ridge pigeon courtyard, using the shared loft atlas."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import bar_parts as parts
import interior_kit as kit

spec = importlib.util.spec_from_file_location("loft", ROOT / "tools/build-city-pigeon-loft-3d-model.py")
loft = importlib.util.module_from_spec(spec)
spec.loader.exec_module(loft)
Assembly = loft.Assembly
NAME = "PigeonCourtyard"
OUT = ROOT / "Assets/Resources/City/PigeonCourtyard"
SOURCE = ROOT / "ArtSource/City/PigeonCourtyard"
TEXTURE = ROOT / "Assets/Resources/City/PigeonLoft/PigeonLoftSurface.png"
PIVOTS = {"ViceHandle": (.615, .946, .46), "GrainLid": (-.29, .76, .03)}
PARENTS = {"BathWater": "Bath", "HandLampGlass": "HandLamp",
           "ViceHandle": "Workbench", "GrainLid": "GrainStore"}
ANCHORS = {
    "Bath": {"PerchBath": (0, .105, -.49), "BathDrink": (0, .086, -.37)},
    "ViceHandle": {"ViceGrip": (.10, 0, 0)},
    "GrainLid": {"GrainLidGripRight": (.12, .045, 0), "GrainLidGripLeft": (-.12, .045, 0),
                 "GrainLidHandle": (0, .065, 0)},
}


def fence(length, gate=False, repaired=False):
    m = Assembly()
    for x in (.022, length-.022):
        m.box("iron", (x,.71,0), (.028,1.27,.032), .002)
    for y in (.09, 1.335):
        m.box("iron", (length*.5,y,0), (length,.03,.032), .002)
    columns = round(length/.125)
    for i in range(1, columns):
        m.box("zinc" if repaired and i>=columns//2 else "iron", (length*i/columns,.71,.003), (.008,1.23,.008), 0)
    for i in range(1,11):
        m.box("zinc" if repaired and i>=5 else "iron", (length*.5,.09+i*.124,-.004), (length-.04,.008,.008), 0)
    if gate:
        for y in (.30,1.11):
            m.disk("rust", (0,y,0), .027,.11,10)
        m.bar("rust", (.05,.14,.014),(length-.05,1.29,.014),.014,.014,0)
        m.box("bare", (length-.045,.79,.034), (.085,.028,.07),.003)
        m.bar("iron", (length-.09,.80,.05),(length+.028,.80,.05),.012,.012,0)
    return m


def post():
    m = Assembly()
    m.box("iron",(0,.73,0),(.07,1.5,.07),.004)
    m.box("zinc",(0,1.487,0),(.083,.026,.083),.003)
    m.box("rust",(0,.08,0),(.075,.16,.075),.003)
    return m


def workbench():
    m = Assembly()
    for i in range(5):
        m.box(("bare","tar","bare","fade","bare")[i],(0,.82,-.36+i*.18),(1.92,.065,.175),.007)
    for x in (-.78,.78):
        for z in (-.31,.31):
            m.bar("tar",(x*.98,.018,z*.98),(x,.79,z),.085)
        m.bar("bare",(x,.24,-.31),(x,.24,.31),.055)
    for z in (-.31,.31):
        m.bar("iron",(-.78,.71,z),(.78,.71,z),.045,.055)
        m.bar("bare",(-.75,.25,z),(.75,.25,z),.04,.06)
    for i in range(4):
        m.box("bare",(0,.23,-.30+i*.20),(1.53,.034,.14),.004)
    # A visibly repaired panel leans behind the table, with a replacement stile.
    for x in (-.55,.49):
        m.bar("moss" if x<0 else "bare",(x,.85,-.35),(x,1.39,-.49),.035,.045)
    for y,z in ((.89,-.36),(1.36,-.483)):
        m.box("fade",(-.03,y,z),(1.08,.035,.042),.003)
    for i in range(1,10):
        m.bar("iron",(-.55+i*.104,.91,-.366),(-.55+i*.104,1.345,-.478),.006,.006,0)
    # Cast-iron vice has opposing jaws, a moving screw and a loose cross handle.
    m.box("iron",(.61,.893,.225),(.24,.074,.19),.009)
    m.box("iron",(.61,.96,.225),(.16,.12,.145),.008)
    for z in (.145,.337):
        m.box("zinc",(.61,1.035,z),(.26,.06,.05),.003)
    m.bar("iron",(.61,.946,.30),(.61,.946,.46),.034)
    handle = Assembly()
    handle.bar("bare",(.48,.946,.46),(.75,.946,.46),.020)
    for x in (.48,.75):
        handle.disk("rust",(x,.946,.46),.015,.029,8)
    # Galvanized mesh roll and two short frame offcuts rest on the low shelf.
    for z in (-.12,.12):
        for i in range(12):
            a=math.tau*i/12
            b=math.tau*(i+1)/12
            m.bar("zinc",(-.37+math.cos(a)*.13,.385+math.sin(a)*.13,z),
                  (-.37+math.cos(b)*.13,.385+math.sin(b)*.13,z),.008,.008,0)
    for i in range(12):
        a=math.tau*i/12
        m.bar("iron",(-.37+math.cos(a)*.13,.385+math.sin(a)*.13,-.15),
              (-.37+math.cos(a)*.13,.385+math.sin(a)*.13,.15),.006,.006,0)
    m.box("bare",(.33,.285,-.12),(.50,.044,.055),.003)
    m.box("fade",(.33,.332,-.16),(.43,.045,.043),.003)
    return m, at_pivot(handle, PIVOTS["ViceHandle"])


def grain_store():
    m = Assembly()
    for x in (-.49,0,.49):
        m.box("tar",(x,.045,0),(.10,.09,.76),.004)
    for z in (-.30,-.15,0,.15,.30):
        m.box("bare",(0,.115,z),(1.12,.045,.13),.005)
    # Preserve the outside silhouette while returning down the inner wall.
    # The lifted lid reveals a real rim, depth and ordinary stored grain.
    m.turned("zinc",(-.29,.14,.03),[(.16,0),(.185,.025),(.19,.59),(.18,.62),
        (.17,.62),(.18,.59),(.175,.03),(.15,.025)],24)
    m.turned("grain",(-.29,.55,.03),[(.165,0),(.17,.012)],24)
    for y in (.22,.42,.66):
        ring, faces = kit.lathe([(.19,0),(.198,.009),(.198,.022),(.19,.029),
            (.186,.029),(.186,0),(.19,0)], 24)
        # A closed annular profile owns its underside. Lathe's generic end
        # disks would otherwise seal the whole opening at every reinforcing band.
        m.add("iron",kit.translated(([(x,h,z) for x,z,h in ring],faces[:-2]),(-.29,y,.03)))
    lid = Assembly()
    lid.turned("zinc",(-.29,.76,.03),[(.184,0),(.20,.012),(.20,.03),(.176,.05),(.015,.05)],24)
    lid.bar("iron",(-.36,.825,.03),(-.22,.825,.03),.017,.025)
    m.box("straw",(.29,.365,-.07),(.35,.45,.36),.072)
    m.box("bare",(.29,.60,-.07),(.25,.038,.115),.015)
    # A closed sack and a spare scoop are protected beneath the loft.
    m.box("straw",(.24,.28,.27),(.37,.27,.21),.065)
    m.box("zinc",(.26,.43,.27),(.13,.016,.12),.003)
    m.bar("bare",(.26,.45,.24),(.26,.45,.43),.025,.03)
    return m, at_pivot(lid, PIVOTS["GrainLid"])


def at_pivot(assembly, pivot):
    result = Assembly()
    offset = tuple(-value for value in pivot)
    for surface, solid in assembly.solids:
        result.add(surface, kit.translated(solid, offset))
    return result


def bath():
    m = Assembly()
    profile=[(.39,0),(.43,.012),(.476,.089),(.48,.101),(.469,.105),(.457,.098),(.413,.027),(.10,.027)]
    v,f=kit.lathe(profile,40)
    m.add("enamel",([(x,z,y*.92) for x,y,z in v],f))
    # Fine dark rolled lip and the still water surface remain opaque shared swatches.
    v,f=kit.lathe([(.472,.093),(.48,.099),(.475,.106),(.465,.101)],40)
    m.add("iron",([(x,z,y*.92) for x,y,z in v],f))
    water=Assembly()
    v,f=kit.lathe([(.447,.082),(.447,.086)],40)
    water.add("water",([(x,z,y*.92) for x,y,z in v],f))
    # The feet anchor is backed by a broad stepping stone with a flat contact top.
    m.box("stone",(0,.0525,-.49),(.32,.105,.28),.025)
    m.box("stone",(-.54,.041,.16),(.24,.082,.29),.035)
    for x,y,z in ((.54,.013,-.1),(.38,.009,.48),(-.38,.011,-.48)):
        m.disk("stone",(x,y,z),.025,.018,7)
    return m, water


def gravel_patch():
    m=Assembly()
    # Full-width flush ends join into one track; the long sides have shallow wear variation.
    # Runtime drapes the shallow closed mesh to existing earth while retaining its relief.
    rim=[(-.5,-.4),(-.28,-.37),(0,-.395),(.22,-.38),(.5,-.4),(.5,.4),(.27,.365),(.02,.39),(-.24,.375),(-.5,.4)]
    vertices=[(x,-.005,z) for x,z in rim]+[(x,.003,z) for x,z in rim]
    n=len(rim)
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    m.add("tar",(vertices,faces))
    for i in range(22):
        x=.40*math.sin(i*2.399); z=.29*math.cos(i*1.731)
        m.disk("stone" if i%3==0 else "bare",(x,.009,z),.009+.004*(i%3),.012,5)
    return m


def hand_lamp():
    # Same measured silhouette as CityHandLampWorldBuilder, authored as an imported mesh.
    body, glass = Assembly(), Assembly()
    body.box("iron",(0,.02,0),(.13,.04,.13),.001)
    body.box("iron",(0,.215,0),(.14,.04,.14),.001)
    for sign in (-1,1):
        body.box("iron",(sign*.055,.262,0),(.012,.07,.012),.001)
    body.box("iron",(0,.292,0),(.122,.012,.012),.001)
    glass.box("lens",(0,.13,0),(.095,.145,.095),.001)
    return body, glass


def build():
    basin, water = bath()
    lamp, glass = hand_lamp()
    bench, handle = workbench()
    grain, lid = grain_store()
    pieces={"FencePanel":fence(1),"FencePanelRepair":fence(1,repaired=True),"FencePost":post(),
            "Gate":fence(1.45,gate=True),"Workbench":bench,"ViceHandle":handle,
            "GrainStore":grain,"GrainLid":lid,
            "Bath":basin,"BathWater":water,"GravelPatch":gravel_patch(),
            "HandLamp":lamp,"HandLampGlass":glass}
    record={"design":"ridge_pigeon_courtyard_v1","units":"Unity metres; props face +Z; fences extend +X",
            "pieces":{key:loft.metrics(value) for key,value in pieces.items()},"anchors":ANCHORS,
            "pivots": PIVOTS, "parents": PARENTS,
            "shared_material":"City/PigeonLoft/PigeonLoftSurface"}
    lamp_bounds=record["pieces"]["HandLamp"]
    assert lamp_bounds["min"]==[-.07,0,-.07] and lamp_bounds["max"]==[.07,.298,.07]
    assert record["pieces"]["Gate"]["max"][0]<1.49
    assert record["pieces"]["GrainStore"]["max"][1]<1
    assert record["pieces"]["Bath"]["max"][1]<.15
    # The vice retains its resting topology. The bin's added inner wall and
    # grain bed stay inside the original exterior and below the separate lid.
    assert record["pieces"]["Workbench"]["triangles"] + record["pieces"]["ViceHandle"]["triangles"] == 1464
    assert record["pieces"]["GrainLid"]["triangles"] == 264
    assert record["pieces"]["GrainStore"]["triangles"] < 1900
    for child in PIVOTS:
        for surface, solid in pieces[child].solids:
            restored = kit.translated(solid, PIVOTS[child])
            assert parts.signed_volume(restored) > 0, (child, surface)
    grain_tree = BVHTree.FromPolygons(*kit.merge_all(solid for _, solid in grain.solids))
    for dx, dz in ((0, 0), (.08, 0), (0, .08), (-.08, -.08)):
        hit = grain_tree.ray_cast(Vector((-.29 + dx, .90, .03 + dz)), Vector((0, -1, 0)), .6)[0]
        assert hit is not None and abs(hit.y - .562) < .001, ("Open grain bin", hit)
    mesh=kit.merge_all(solid for _,solid in pieces["Bath"].solids)
    tree=BVHTree.FromPolygons(*mesh)
    x,y,z=ANCHORS["Bath"]["PerchBath"]
    for dx in (-.07,0,.07):
        for dz in (-.06,0,.06):
            hit=tree.ray_cast(Vector((x+dx,y+.03,z+dz)),Vector((0,-1,0)),.05)[0]
            assert hit is not None and abs(hit.y-y)<.001, ("Bath toe contact",hit)
    water_tree=BVHTree.FromPolygons(*kit.merge_all(solid for _,solid in water.solids))
    x,y,z=ANCHORS["Bath"]["BathDrink"]
    hit=water_tree.ray_cast(Vector((x,y+.03,z)),Vector((0,-1,0)),.05)[0]
    assert hit is not None and abs(hit.y-y)<.001, ("Bath bill contact",hit)
    assert sum(piece["triangles"] for piece in record["pieces"].values())<15000
    return pieces,record


def emit(pieces,model_dir,source_dir):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    material=bpy.data.materials.new("PigeonLoftSurface")
    material.use_nodes=True
    tex=material.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image=bpy.data.images.load(str(TEXTURE))
    tex.image.alpha_mode="CHANNEL_PACKED"
    material.node_tree.links.new(tex.outputs["Color"],material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
    root=loft.empty(NAME+"_MetricRoot",None,(0,0,0))
    objects={}
    for name,piece in pieces.items():
        obj=loft.mesh_object(name,piece.solids,material)
        parent_name=PARENTS.get(name)
        obj.parent=objects[parent_name] if parent_name else root
        pivot = PIVOTS.get(name, (0, 0, 0))
        obj.location = (pivot[0], pivot[2], pivot[1])
        objects[name]=obj
        for anchor,point in ANCHORS.get(name,{}).items():
            loft.empty(anchor,obj,point)
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(model_dir/(NAME+".fbx")),use_selection=True,
        object_types={"MESH","EMPTY"},axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,add_leaf_bones=False,bake_anim=False,mesh_smooth_type="FACE",path_mode="STRIP")
    tex.image.filepath="//"+os.path.relpath(TEXTURE,SOURCE).replace(os.sep,"/")
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(source_dir/(NAME+".blend")),compress=True,relative_remap=False)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=OUT)
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    pieces,record=build()
    assert build()[1]==record,"Non-deterministic courtyard geometry"
    record["atlas_sha256"]=hashlib.sha256(TEXTURE.read_bytes()).hexdigest()
    manifest=json.dumps(record,indent=2)+"\n"
    if args.validate_only:
        assert (args.model_dir/(NAME+".json")).read_text()==manifest
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        args.source_dir.mkdir(parents=True,exist_ok=True)
        emit(pieces,args.model_dir,args.source_dir)
        (args.model_dir/(NAME+".json")).write_text(manifest)
    print("PIGEON COURTYARD", {key:piece["triangles"] for key,piece in record["pieces"].items()})


if __name__=="__main__":
    main()
