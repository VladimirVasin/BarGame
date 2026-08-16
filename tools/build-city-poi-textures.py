#!/usr/bin/env python3
"""Build the deterministic surface albedos for the district points of interest.

Four seamless 1024x1024 sheets, imported by Unity at 512 and tiled by
metres through `CityPointOfInterestSurfaceAppearance` exactly the way the
home/supermarket pipelines do it: each renderer's authored flat colour
(brightened by the solved per-sheet compensation) multiplies the sheet in
linear space.

The measured contract - linear luminance rule, wrap-by-construction
drawing, compensation solving and validation - is imported from
`build-home-textures.py`; this script only adds the yard paving grammar
(large weathered concrete slabs) and reuses the home painted-metal,
linen and plank grammars for the drying frames, the hanging wash and the
shared bench.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "City"

_home_spec = importlib.util.spec_from_file_location(
    "build_home_textures",
    Path(__file__).resolve().parent / "build-home-textures.py",
)
home = importlib.util.module_from_spec(_home_spec)
sys.modules["build_home_textures"] = home
_home_spec.loader.exec_module(home)

SHEET_SIZE = home.SHEET_SIZE


# --------------------------------------------------------------------------
# point-of-interest grammars
# --------------------------------------------------------------------------


def draw_poi_paving(base: Image.Image, rng) -> Image.Image:
    """Large weathered concrete yard slabs: 4x4, joints, cracks, stains."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.8), (256, 0.5), (512, 0.3)), rng),
    )
    draw = ImageDraw.Draw(image)
    pitch = SHEET_SIZE // 4
    for row in range(4):
        for column in range(4):
            tone = 128 + rng.randint(-9, 9)
            x = column * pitch
            y = row * pitch
            slab = Image.new("L", (pitch - 6, pitch - 6), tone)
            image.paste(
                ImageChops.overlay(
                    image.crop((x + 3, y + 3, x + pitch - 3, y + pitch - 3)),
                    slab,
                ),
                (x + 3, y + 3),
            )

    for line in range(5):
        offset = (line * pitch) % SHEET_SIZE
        home.wrap_line(
            draw,
            (offset, 0, offset, SHEET_SIZE),
            92,
            width=6,
        )
        home.wrap_line(
            draw,
            (0, offset, SHEET_SIZE, offset),
            92,
            width=6,
        )

    for _ in range(14):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        for _ in range(rng.randint(3, 7)):
            step_x = x + rng.randint(-70, 70)
            step_y = y + rng.randint(20, 80)
            home.wrap_line(
                draw,
                (x, y, step_x, step_y),
                100,
                width=rng.randint(1, 3),
            )
            x, y = step_x, step_y

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(11):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(80, 260), y + rng.randint(60, 200)),
                fill=rng.randint(106, 120),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(30, 90), y + rng.randint(24, 70)),
                fill=rng.randint(134, 146),
            )

    return home.soft_overlay(image, 10, weathering)


home.GRAMMARS["poi_paving"] = draw_poi_paving


# --------------------------------------------------------------------------
# specs — tints transcribe CityDistrictPointOfInterestWorldBuilder's palette
# --------------------------------------------------------------------------


POI_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="CityPoiPavingAlbedo",
        grammar="poi_paving",
        seed=0x43504156,
        cast=(1.00, 1.01, 1.00),
        mean_target=0.50,
        meters_per_tile=3.0,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("CityDistrictPointOfInterestWorldBuilder.OldTownPaving",
             (0.255, 0.235, 0.190)),
            ("CityDistrictPointOfInterestWorldBuilder.ResidentialPaving",
             (0.235, 0.275, 0.270)),
            ("CityDistrictPointOfInterestWorldBuilder.IndustrialPaving",
             (0.200, 0.220, 0.210)),
            ("CityDistrictPointOfInterestWorldBuilder.NightlifePaving",
             (0.170, 0.175, 0.215)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityPoiPaintedMetalAlbedo",
        grammar="painted_metal",
        seed=0x43504D54,
        cast=(0.99, 1.00, 1.01),
        mean_target=0.50,
        meters_per_tile=1.4,
        smoothness=0.22,
        metallic=0.25,
        tints=(
            ("CityDistrictPointOfInterestWorldBuilder.ResidentialFrame",
             (0.145, 0.190, 0.185)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityPoiClothAlbedo",
        grammar="linen",
        seed=0x43504354,
        cast=(1.01, 1.00, 0.98),
        mean_target=0.50,
        meters_per_tile=1.6,
        smoothness=0.0,
        metallic=0.0,
        tints=(
            ("CityDistrictPointOfInterestWorldBuilder.ResidentialCloth",
             (0.405, 0.245, 0.185)),
            ("CityDistrictPointOfInterestWorldBuilder.ResidentialClothCold",
             (0.225, 0.350, 0.375)),
            ("CityDistrictPointOfInterestWorldBuilder.ResidentialPatch",
             (0.600, 0.500, 0.285)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityPoiTimberAlbedo",
        grammar="planks",
        seed=0x43505442,
        cast=(1.04, 1.00, 0.94),
        mean_target=0.48,
        meters_per_tile=1.6,
        smoothness=0.08,
        metallic=0.0,
        tints=(
            ("CityDistrictPointOfInterestWorldBuilder.ResidentialCloth",
             (0.405, 0.245, 0.185)),
        ),
    ),
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
        help="Destination directory for the opaque 1024x1024 albedos.",
    )
    parser.add_argument(
        "--art-source",
        type=Path,
        default=DEFAULT_ART_SOURCE,
        help="Destination for the measured contract and the contact sheet.",
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Build and validate without writing anything.",
    )
    args = parser.parse_args()

    records: list[dict] = []
    built: list[tuple[home.HomeSheetSpec, Image.Image]] = []
    for spec in POI_SHEET_SPECS:
        image, record = home.build_sheet(spec)
        record["resourcePath"] = f"Textures/{spec.key}"
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
    manifest_path = args.art_source / "poi-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "poi-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
