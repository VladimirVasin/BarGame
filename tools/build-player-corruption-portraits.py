#!/usr/bin/env python3
"""Render source-derived corruption portraits and a bounded 1/4/7 art review.

Uses the production source and unchanged original rig; this command never
saves the source or exports/rebuilds the animation bank. Review PNGs are
temporary Captures outputs. Portraits use the existing transparent 192x256
inventory camera and material/shape profile for each appearance.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / "tools"))
import player_corruption
import player_corruption_hair


def load_module():
    spec = importlib.util.spec_from_file_location("hero_corruption_source", ROOT / "tools/build-player-3d-model-v2.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def load_result(module):
    original = json.loads(module.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(module.DEFAULT_OUTPUT))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    rig.animation_data_create().action = None
    parts = [module.common.PartRecord(bpy.data.objects[p["name"]], p["role"], p["bone"], p["sprite_part"], p["side"])
             for p in original["parts"]]
    actions = {p["name"]: module.common.ActionRecord(bpy.data.actions[p["name"]], p["category"],
        p["duration_seconds"], p["loop"], p["source_frame_count"], p["source_fps"]) for p in original["actions"]}
    return module.common.BuildResult(rig.parent, rig, {}, {}, parts=parts, actions=actions)


def apply_stage(result, stage, originals):
    amount = player_corruption.STAGE_WEIGHTS[stage - 1] / 100.
    clothing = {material.name for part in result.parts if part.role == "clothing"
                for material in part.obj.data.materials}
    linear = lambda value: value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4
    for part in result.parts:
        keys = part.obj.data.shape_keys
        if keys and keys.key_blocks.get(player_corruption.SHAPE_NAME):
            keys.key_blocks[player_corruption.SHAPE_NAME].value = amount
        if keys and keys.key_blocks.get(player_corruption_hair.SHORT_SHAPE):
            keys.key_blocks[player_corruption_hair.SHORT_SHAPE].value = player_corruption_hair.SHORT_WEIGHTS[stage-1]/100.
            keys.key_blocks[player_corruption_hair.LONG_SHAPE].value = player_corruption_hair.LONG_WEIGHTS[stage-1]/100.
    for material, color in originals:
        skin = material.name.startswith(("MAT_Skin", "MAT_Lip"))
        if skin or material.name in clothing:
            multiplier = tuple(linear(1. - amount * (.20, .09, .18)[i]) if skin else
                               1. - amount * (.22, .23, .33)[i] for i in range(3))
            target = tuple(color[i] * multiplier[i] for i in range(3)) + (color[3],)
            material.diffuse_color = target
            node = material.node_tree.nodes.get("Principled BSDF") if material.use_nodes else None
            if node:
                base = node.inputs["Base Color"]
                tint = material.node_tree.nodes.get("CorruptionPreviewTint")
                if base.is_linked and not skin:
                    # Textured garments use the complete authored wear atlas;
                    # flat shirt/edge cloth still takes the shared runtime tint.
                    texture = next((n for n in material.node_tree.nodes if n.type == "TEX_IMAGE"), None)
                    if texture is not None:
                        path = player_corruption.ROOT/"Assets/Player3D/V2/Textures/PlayerClothingAtlas.png" if stage == 1 else player_corruption.TEXTURE_DIR/f"Clothing{stage}.png"
                        texture.image = bpy.data.images.load(str(path), check_existing=True)
                        multiplier = (1.,1.,1.)
                    if tint is None:
                        source = base.links[0].from_socket
                        tint = material.node_tree.nodes.new("ShaderNodeMixRGB")
                        tint.name = "CorruptionPreviewTint"
                        tint.blend_type = "MULTIPLY"
                        tint.inputs[0].default_value = 1.
                        material.node_tree.links.new(source, tint.inputs[1])
                        material.node_tree.links.new(tint.outputs[0], base)
                    tint.inputs[2].default_value = (*multiplier, 1.)
                else:
                    base.default_value = target
    face = bpy.data.materials["MAT_FaceAtlas"].node_tree.nodes["TEX_PlayerFaceAtlas"]
    path = player_corruption.SOURCES["Face"] if stage == 1 else player_corruption.TEXTURE_DIR / f"Face{stage}.png"
    face.image = bpy.data.images.load(str(path), check_existing=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=player_corruption.TEXTURE_DIR)
    parser.add_argument("--review-dir", type=Path, default=ROOT / "Captures/hero-corruption-source")
    parser.add_argument("--no-portraits", action="store_true")
    parser.add_argument("--no-review", action="store_true")
    args = parser.parse_args(argv)
    module = load_module()
    result = load_result(module)
    originals = [(m, tuple(m.diffuse_color)) for m in bpy.data.materials]
    current_stage = [1]
    def apply_hair_after_action(scene, depsgraph=None):
        player_corruption_hair.apply_preview_pose(result, current_stage[0])
    bpy.app.handlers.frame_change_post.append(apply_hair_after_action)
    if not args.no_review:
        args.review_dir.mkdir(parents=True, exist_ok=True)
        for stage in (1, 4, 7):
            current_stage[0] = stage
            apply_stage(result, stage, originals)
            for name, position, target, lens in (
                ("front", (0., -3.5, 1.03), (0., 0., .9), 54.),
                ("side", (3.5, 0., 1.03), (0., 0., .9), 54.),
                ("face", (0., -1.28, 1.625), (0., -.020, 1.565), 78.),
            ):
                module.render_relaxed_study(args.review_dir / f"stage{stage}-{name}.png", result,
                    Vector(position), Vector(target), lens)
    if not args.no_portraits:
        for stage in range(1, 8):
            current_stage[0] = stage
            apply_stage(result, stage, originals)
            module.common.render_inventory_portrait(args.output_dir / f"Portrait{stage}.png", result)
    print("HERO CORRUPTION SOURCE REVIEW OK: original rig; neutral 1/4/7 form studies; transparent stage portraits", flush=True)


if __name__ == "__main__":
    main()
