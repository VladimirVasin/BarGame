"""Geometry-only Hero refresh through the production generator's source contract."""
from __future__ import annotations
import hashlib
import json
import os
import runpy
import sys
from pathlib import Path
import bpy


def _curves(module,action):
    return [[curve.data_path,curve.array_index,
             [[list(key.co),list(key.handle_left),list(key.handle_right),key.interpolation]
              for key in curve.keyframe_points]]
            for curve in module.common.iter_action_fcurves(action)]


def refresh(module,config,clothing_atlas=None):
    """Rebuild surfaces, append unchanged complete actions, export only the model."""
    if config.animation_fbx is not None:
        raise ValueError("Joint source refresh requires --skip-animation-export")
    original=json.loads(module.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
    if (config.height,config.seed,config.pose)!=(original["height_m"],original["seed"],original["pose"]):
        raise ValueError("Joint refresh must retain production height, seed and A-pose")
    clothing_atlas=clothing_atlas or module.DEFAULT_CLOTHING_ATLAS
    # The requested boots alone own these two atlas cells. Patching the prior
    # PNG retains all existing character and garment artwork byte-for-byte.
    clothing_sha=module.player_detailed_model.player_boots.patch_png(
        module.DEFAULT_CLOTHING_ATLAS,clothing_atlas,module)
    builder=module.HeroV2Builder(config,module.DEFAULT_FACE_ATLAS,clothing_atlas)
    builder.preview_only=True
    result=builder.build()
    for record in result.actions.values():
        bpy.data.actions.remove(record.action)
    result.actions.clear()
    names=[row["name"] for row in original["actions"]]
    with bpy.data.libraries.load(str(module.DEFAULT_OUTPUT),link=False) as (source,target):
        if set(names)-set(source.actions):
            raise ValueError("Verified source is missing production actions")
        target.actions=list(names)
    loaded={action.name:action for action in target.actions}
    before={name:_curves(module,loaded[name]) for name in names}
    result.actions={row["name"]:module.common.ActionRecord(loaded[row["name"]],row["category"],
        row["duration_seconds"],row["loop"],row["source_frame_count"],row["source_fps"])
        for row in original["actions"]}
    for record in result.actions.values():record.action.use_fake_user=True
    builder._reset_pose()
    report=module.validate_v2_result(config,result,module.DEFAULT_FACE_ATLAS,clothing_atlas,
                                     module.DEFAULT_BARE_SKIN_ATLAS)
    hashes=(original["face_atlas"]["sha256"],clothing_sha,
            original["bare_skin_atlas"]["sha256"])
    signature=module.content_signature(config,result,*hashes)
    if before!={name:_curves(module,result.actions[name].action) for name in names}:
        raise RuntimeError("Joint refresh changed preserved action curves")
    result.root["bp_content_signature_sha256"]=signature
    bpy.context.scene["bp_content_signature_sha256"]=signature
    # FBX texture references must target the final atlas location too. Images
    # already hold the staged pixels, so this changes links without reloading.
    for image in bpy.data.images:
        if image.source=="FILE" and image.filepath:
            absolute=Path(bpy.path.abspath(image.filepath)).resolve()
            image.filepath=str(module.PUBLISHED_PATHS.get(absolute,absolute))
    module.export_model_fbx(config.fbx,result)
    # Staging moves this source into its published directory after generation.
    # Store links relative to that final path, not the temporary staging path.
    published=module.PUBLISHED_PATHS.get(config.output.resolve(),config.output.resolve())
    for image in bpy.data.images:
        if image.source=="FILE" and image.filepath:
            absolute=Path(bpy.path.abspath(image.filepath)).resolve()
            absolute=module.PUBLISHED_PATHS.get(absolute,absolute)
            image.filepath="//"+os.path.relpath(absolute,published.parent).replace("\\","/")
    config.output.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(config.output.resolve()),compress=True,relative_remap=False)
    module.write_v2_manifest(config.manifest,config,result,report,module.DEFAULT_FACE_ATLAS,hashes[0],
        clothing_atlas,hashes[1],module.DEFAULT_BARE_SKIN_ATLAS,hashes[2],signature)
    payload=json.loads(config.manifest.read_text(encoding="utf-8"))
    payload["joint_surface_authoring_sha256"]=hashlib.sha256(Path(module.player_detailed_model.character_joint_surfaces.__file__).read_bytes()).hexdigest()
    payload["footwear_authoring_sha256"]=hashlib.sha256(Path(module.player_detailed_model.player_boots.__file__).read_bytes()).hexdigest()
    payload["joint_surfaces"]["preserved_action_bank"]=True
    config.manifest.write_text(json.dumps(payload,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    # Prove the actual staged FBX before the common launcher publishes it.
    previous=sys.argv
    try:
        sys.argv=["check-character-joint-export.py","--","--model",str(config.fbx),
                  "--manifest",str(config.manifest)]
        runpy.run_path(str(Path(__file__).with_name("check-character-joint-export.py")),run_name="__main__")
    finally:
        sys.argv=previous
    print("Hero geometry and isolated footwear pixels refreshed; production action curves and animation FBX preserved",flush=True)
