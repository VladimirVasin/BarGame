#!/usr/bin/env python3
"""Build the deterministic surface albedos for the worn bar interior.

Four seamless 1024x1024 sheets, imported by Unity at 512, tiled by
metres through `BarSurfaceAppearance` exactly the way the home and
supermarket pipelines do it. This is the Residential district's bar —
a bar for people without money: trodden plank floor, old wallpaper
over the panels, dark tired wood and upholstery rubbed to the weave.

The whole measured contract is imported from `build-home-textures.py`
and every sheet reuses an existing home grammar (planks, wallpaper,
veneer, weave) — the worn look is the home look; that is the point.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Bar" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "Bar"

_home_spec = importlib.util.spec_from_file_location(
    "build_home_textures",
    Path(__file__).resolve().parent / "build-home-textures.py",
)
home = importlib.util.module_from_spec(_home_spec)
sys.modules["build_home_textures"] = home
_home_spec.loader.exec_module(home)

SHEET_SIZE = home.SHEET_SIZE


BAR_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="BarWornPlankAlbedo",
        grammar="planks",
        seed=0x42415250,
        cast=(1.03, 1.00, 0.95),
        mean_target=0.50,
        meters_per_tile=1.5,
        smoothness=0.08,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Floor",
             (0.14, 0.06, 0.042)),
            ("BarInteriorWorldBuilder.Wood",
             (0.16, 0.055, 0.028)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarWallpaperAlbedo",
        grammar="wallpaper",
        seed=0x42415257,
        cast=(1.03, 1.00, 0.96),
        mean_target=0.50,
        meters_per_tile=1.8,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Wall",
             (0.29, 0.075, 0.075)),
            ("BarInteriorWorldBuilder.WallPanel",
             (0.13, 0.042, 0.032)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarDarkWoodAlbedo",
        grammar="veneer",
        seed=0x42415244,
        cast=(1.02, 1.00, 0.96),
        mean_target=0.50,
        meters_per_tile=1.1,
        smoothness=0.12,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.DarkWood",
             (0.075, 0.024, 0.017)),
            ("BarInteriorWorldBuilder.Wood",
             (0.16, 0.055, 0.028)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarWornLeatherAlbedo",
        grammar="weave",
        seed=0x4241524C,
        cast=(1.02, 0.99, 0.97),
        mean_target=0.50,
        meters_per_tile=0.9,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Leather",
             (0.30, 0.035, 0.045)),
        ),
    ),
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
    )
    parser.add_argument(
        "--art-source",
        type=Path,
        default=DEFAULT_ART_SOURCE,
    )
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    records: list[dict] = []
    built: list[tuple[home.HomeSheetSpec, Image.Image]] = []
    for spec in BAR_SHEET_SPECS:
        image, record = home.build_sheet(spec)
        record["resourcePath"] = f"Bar/Textures/{spec.key}"
        home.validate(image, spec, record)
        built.append((spec, image))
        if args.verify:
            record["sha256"] = (
                hashlib.sha256(image.tobytes()).hexdigest().upper()
            )
        else:
            record["sha256"] = home.save_png(
                image,
                args.textures / f"{spec.key}.png",
            )
        records.append(record)
        print(
            f"{'Checked' if args.verify else 'Wrote'} {spec.key} "
            f"({image.width}x{image.height}) "
            f"mean={record['meanLinearLuminance']:.4f} "
            f"compensation={record['albedoCompensation']:.4f} "
            f"error={record['brightnessError'] * 100:.1f}% "
            f"edge={record['edgeDelta']:.2f} "
            f"seam={record['seamRatio']:.2f}x "
            f"contrast={record['contrast']} "
            f"chroma={record['channelRatio']:.3f}"
        )

    if args.verify:
        print(f"Validated {len(records)} sheets; nothing written.")
        return

    manifest = {
        "sheetSize": SHEET_SIZE,
        "meanLuminanceTolerance": home.MEAN_LUMINANCE_TOLERANCE,
        "brightnessErrorLimit": home.BRIGHTNESS_ERROR_LIMIT,
        "tintChannelFloor": home.TINT_CHANNEL_FLOOR,
        "sheets": records,
    }
    manifest_path = args.art_source / "bar-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "bar-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
