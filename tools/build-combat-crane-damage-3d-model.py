"""Author the independent Crane damage banks without rebuilding Hero/Npc assets.

Run through tools/run-blender.py after publishing Combat/CraneBody.fbx.
The shared builders still own all cuts, tissue cells, fracture and wound rules.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
SOURCE="Assets/Resources/Combat/CraneBody.fbx"


def module(label, filename):
    spec=importlib.util.spec_from_file_location(label,Path(__file__).with_name(filename))
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result)
    return result


body=module("crane_body_damage_art","build-combat-body-3d-model.py")
gore=module("crane_head_damage_art","build-combat-gore-3d-model.py")
skull=module("crane_skull_damage_art","build-combat-skull-3d-model.py")
blood=module("crane_wound_damage_art","build-combat-blood-3d-model.py")
for builder in (body,gore,skull.body,blood):builder.SOURCES={"Crane":SOURCE}


def digest(data):return hashlib.sha256(data).hexdigest()


def build(out,publish):
    gore_out=out/"CombatGore";blood_out=out/"CombatBlood"
    head=gore.build("Crane",gore_out,publish)
    models={"HeadCrane":head,
            "SkullCrane":skull.build("Crane",gore_out,publish,head_contract={"models":{"Crane":head}}),
            "BodyCrane":body.build("Crane",gore_out,publish,source_filter=lambda obj:obj.name.startswith("GEO_"))}
    patches,wounds=blood.wounds("Crane",blood_out,publish,
        surface_filter=lambda name:name.startswith("GEO_") and not name.startswith(
            ("GEO_Hand","GEO_Foot","GEO_Ear","GEO_Finger","GEO_Thumb")),
        required_palms=set())
    models["WoundsCrane"]=wounds
    return models,patches


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output-root",type=Path,default=ROOT/"Assets/Resources")
    parser.add_argument("--validate-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if not (ROOT/SOURCE).exists():raise RuntimeError("Publish the independent Crane body before its damage banks.")
    out=args.output_root.resolve();manifest_path=out/"CombatGore/CombatCraneDamage3D.json"
    for folder in (out/"CombatGore",out/"CombatBlood"):folder.mkdir(parents=True,exist_ok=True)
    previous=json.loads(manifest_path.read_text(encoding="utf-8")) if args.validate_only else None
    if previous:
        for filename,expected in previous["files"].items():
            if digest((out/filename).read_bytes())!=expected:raise RuntimeError("Crane damage bank changed: "+filename)
    models,patches=build(out,not args.validate_only)
    if (models,patches)!=build(out,False):raise RuntimeError("Crane damage banks differ across independent source rebuilds.")
    manifest={"generator":"tools/build-combat-crane-damage-3d-model.py","version":1,"test_only":True,
        "source":{"path":SOURCE,"sha256":digest((ROOT/SOURCE).read_bytes())},
        "body_region_count":16,"patch_count":4,"head_sector_count":16,"brain_count":8,"surface_lift_m":.0018,
        "models":models,"wound_patches":patches,
        "source_tessellation_tolerances":[dict(kind="Crane",**entry) for entry in models["BodyCrane"]["source_tessellation"]],
        "files":{filename:digest((out/filename).read_bytes()) for filename in
            ("CombatGore/BodyCrane.fbx","CombatGore/HeadCrane.fbx","CombatGore/SkullCrane.fbx","CombatBlood/WoundsCrane.fbx")}}
    if args.validate_only:
        if manifest!=previous:raise RuntimeError("Crane source/UV/skin/cut/containment contract changed.")
        print("CRANE DAMAGE SOURCE COVERAGE, UV, SKIN, CLOSED TISSUE, HEAD CONTAINMENT AND DETERMINISM OK",flush=True)
    else:
        manifest_path.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
        print("CRANE DAMAGE BANKS GENERATED",flush=True)


if __name__=="__main__":main()
