#!/usr/bin/env python3
"""Focused Hero joint source check; reconstruct geometry without publishing assets.

Run through run-blender.py --validate-only. --output-dir optionally saves the
measured manifest and reconstructed source for visual inspection/publication.
Production action generation and Unity are deliberately outside this check.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

import bpy

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"tools"))
import character_joint_surfaces


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--output-dir",type=Path)
    parser.add_argument("--review-atlas",type=Path)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    spec=importlib.util.spec_from_file_location("hero_joint_source",ROOT/"tools/build-player-3d-model-v2.py")
    hero=importlib.util.module_from_spec(spec);sys.modules[spec.name]=hero;spec.loader.exec_module(hero)
    old=json.loads(hero.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
    config=hero.common.BuildConfig(output=hero.DEFAULT_OUTPUT,preview=None,portrait=None,manifest=None,
        glb=None,fbx=None,animation_fbx=None,height=old["height_m"],seed=old["seed"],pose="apose")
    clothing=hero.DEFAULT_CLOTHING_ATLAS
    if args.review_atlas:
        hero.player_detailed_model.player_boots.patch_png(clothing,args.review_atlas,hero)
        clothing=args.review_atlas
    builder=hero.HeroV2Builder(config,hero.DEFAULT_FACE_ATLAS,clothing)
    builder.preview_only=True
    result=builder.build()
    errors=[]
    hero.player_detailed_model.validate(result,errors)
    for part in result.parts:
        if part.obj.name!="GEO_FaceSurface":
            try:hero.common.validate_manifold(part.obj)
            except RuntimeError as error:errors.append(str(error))
    if errors:
        raise RuntimeError("Hero joint source failed:\n"+"\n".join(errors))
    data=hero.player_detailed_model.manifest(result)
    if args.output_dir:
        args.output_dir.mkdir(parents=True,exist_ok=True)
        (args.output_dir/"hero-joint-surfaces.json").write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        hero.common.save_blend(args.output_dir/"hero-joint-surfaces.blend")
    print("Hero joint source passed: "+json.dumps({"quality":data["quality"],
        "seams":len(data["joint_surfaces"]["seams"]),"attachments":len(data["joint_surfaces"]["attachments"])}),flush=True)


if __name__=="__main__":
    main()
