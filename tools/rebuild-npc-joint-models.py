#!/usr/bin/env python3
"""Stage the complete NPC joint migration without unchanged action banks.

Run through run-blender.py with --stage-output=--asset-dir=Assets and
--stage-output=--source-dir=ArtSource. The default/receiver selections let the
body donors publish before their combat derivatives are generated.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
from pathlib import Path
import sys
import bpy
from bpy.app.handlers import persistent

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
import npc_detail_atlas


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / filename)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def invoke(module, arguments):
    previous = sys.argv
    try:
        sys.argv = [module.__file__, "--", *map(str, arguments)]
        module.main()
    finally:
        sys.argv = previous


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-dir", type=Path, required=True)
    parser.add_argument("--source-dir", type=Path, required=True)
    parser.add_argument("--family", choices=("default", "receiver", "legacy", "wood-woman"), required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    assets, sources = args.asset_dir.resolve(), args.source_dir.resolve()
    if not assets.is_relative_to(ROOT / "Captures/Tooling") or not sources.is_relative_to(ROOT / "Captures/Tooling"):
        raise RuntimeError("This migration must use the launcher's isolated staging directories")
    # These source previews reuse the one already-published ordinary atlas.
    # Its style is outside the joint change and must remain byte identical.
    def existing_atlas(path):
        path = Path(path)
        if path.read_bytes() != npc_detail_atlas.png_bytes():
            raise RuntimeError("Published ordinary atlas differs from its source")
        return path
    npc_detail_atlas.publish = existing_atlas
    @persistent
    def rewrite_image_paths(*_):
        for image in bpy.data.images:
            if not image.filepath:
                continue
            path = Path(bpy.path.abspath(image.filepath)).resolve()
            if path.is_relative_to(assets):
                image.filepath = str(ROOT / "Assets" / path.relative_to(assets))
    bpy.app.handlers.save_pre.append(rewrite_image_paths)
    if args.family == "default":
        module = load("joint_publish_default", "build-default-npc-3d-model.py")
        module.ASSETS = assets / "Resources/VillageLife"
        module.SOURCE = sources / "VillageLife"
        module.SOURCE.mkdir(parents=True, exist_ok=True)
        invoke(module, ("--no-preview",))
    elif args.family == "receiver":
        module = load("joint_publish_receiver", "build-cannery-receiver-3d-model.py")
        invoke(module, ("--model-dir", assets / "Resources/City/Cannery/Receiver",
                        "--source-dir", sources / "City/CanneryReceiver", "--models-only", "--no-preview"))
    elif args.family == "wood-woman":
        module = load("joint_publish_wood_woman", "build-village-residents-3d-model.py")
        invoke(module, ("--model-dir", assets / "Resources/VillageLife", "--source-dir", sources / "VillageLife",
                        "--models-only", "--no-preview"))
    else:
        base = load("joint_publish_city", "build-city-pedestrian-3d-model.py")
        base.texture_asset_path = lambda path: ("Assets/" + Path(path).resolve().relative_to(assets).as_posix()
                                               if Path(path).resolve().is_relative_to(assets) else
                                               Path(path).resolve().relative_to(ROOT).as_posix())
        common = ("--source-dir", sources / "Pedestrians/Blender", "--model-dir", assets / "Pedestrians/Models",
                  "--staged-model-dir", assets / "Pedestrians/Staged/Models", "--texture-dir", assets / "Pedestrians/Textures",
                  "--models-only", "--no-preview")
        for selector in (("--archetype", "all"), ("--mother",), ("--cafe-cast",), ("--shelter-residents",)):
            invoke(base, (*common, *selector))
        for name, filename, source_path, model_path, stem in (
                ("driver", "build-city-bus-driver-3d-model.py", "Vehicles/Drivers/Blender", "Vehicles/Drivers/Models", "CityBusDriver3D"),
                ("bartender", "build-ordinary-bartender-3d-model.py", "Bar/Bartender/Blender", "Bar/Bartender/Models", "BarBartenderOrdinary3D")):
            module = load("joint_publish_" + name, filename)
            invoke(module, ("--output", sources / source_path / (stem + ".blend"),
                            "--fbx", assets / model_path / (stem + ".fbx"),
                            "--manifest", assets / model_path / (stem + ".json"), "--no-preview"))
        cashier = load("joint_publish_cashier", "build-supermarket-cashier-3d-model.py")
        def cashier_atlas(canvas, path):
            if Path(path).read_bytes() != canvas.png_bytes():
                raise RuntimeError("Published cashier atlas differs from its source")
            return cashier.DetailAtlasReport(Path(path), hashlib.sha256(canvas.png_bytes()).hexdigest(),
                                             cashier.DETAIL_ATLAS_SIZE, cashier.DETAIL_ATLAS_SIZE)
        cashier.write_detail_atlas = cashier_atlas
        for variant in (cashier.NORMAL_VARIANT, cashier.WATCHER_VARIANT):
            stem = variant.output_stem
            invoke(cashier, ("--variant", variant.key, "--output", sources / "Supermarket/Cashier/Blender" / (stem + ".blend"),
                            "--fbx", assets / "Supermarket/Cashier/Models" / (stem + ".fbx"),
                            "--manifest", assets / "Supermarket/Cashier/Models" / (stem + ".json"), "--no-preview"))
        village = load("joint_publish_village", "build-village-residents-3d-model.py")
        common = ("--model-dir", assets / "Resources/VillageLife", "--source-dir", sources / "VillageLife", "--models-only", "--no-preview")
        invoke(village, common)
        invoke(village, (*common, "--phase-two"))
    for path in assets.rglob("*.png"):
        published = ROOT / "Assets" / path.relative_to(assets)
        if not published.is_file() or published.read_bytes() != path.read_bytes():
            raise RuntimeError("Joint migration changed atlas style: " + str(published))
    print("NPC JOINT MODELS STAGED:", args.family, flush=True)


if __name__ == "__main__":
    main()
