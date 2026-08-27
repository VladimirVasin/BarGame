#!/usr/bin/env python3
"""Build deterministic surface albedos for the bar interior and exterior.

Six seamless 1024x1024 sheets, imported by Unity at 512. Four carry the
worn Residential interior and two carry the old neighbourhood-pub exterior:
small urban brick with no painted windows, and dirty patched whitewash.

The whole measured contract is imported from `build-home-textures.py`
and the interior sheets reuse its established home grammars. The two exterior
grammars remain here because their coursed brick and weather-exposed plaster
belong to the pub, not to a generic room.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

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


def draw_exterior_brick(base: Image.Image, rng) -> Image.Image:
    """Small stretcher-bond brick, periodic by construction.

    The 128 x 64 px cell gives a deliberately old, fine urban course at the
    sheet's 1.2 m pitch. Every line wraps; the albedo contains material grain
    and repair only, never windows or architectural markings.
    """
    draw = ImageDraw.Draw(base)
    brick_width = 128
    course_height = 64
    mortar = 5

    for row in range(SHEET_SIZE // course_height):
        y = row * course_height
        offset = brick_width // 2 if row % 2 else 0
        for column in range(-1, SHEET_SIZE // brick_width + 1):
            x = column * brick_width + offset
            tone = home.BASE + rng.randint(-15, 13)
            home.wrap_rect(
                draw,
                (
                    x + mortar,
                    y + mortar,
                    x + brick_width - mortar,
                    y + course_height - mortar,
                ),
                tone,
            )

            # Fired flecks and chipped arrises survive the 512 import without
            # turning the wall into a noisy damage decal.
            for _ in range(rng.randint(1, 3)):
                fleck_x = x + rng.randint(12, brick_width - 15)
                fleck_y = y + rng.randint(11, course_height - 13)
                fleck_width = rng.randint(3, 8)
                home.wrap_rect(
                    draw,
                    (
                        fleck_x,
                        fleck_y,
                        fleck_x + fleck_width,
                        fleck_y + rng.randint(2, 4),
                    ),
                    tone + rng.choice((-22, -16, 12)),
                )

        mortar_tone = home.BASE + rng.randint(19, 28)
        home.wrap_rect(
            draw,
            (0, y - mortar // 2, SHEET_SIZE, y + mortar // 2),
            mortar_tone,
        )
        for column in range(-1, SHEET_SIZE // brick_width + 1):
            x = column * brick_width + offset
            home.wrap_rect(
                draw,
                (
                    x - mortar // 2,
                    y + mortar // 2,
                    x + mortar // 2,
                    y + course_height - mortar // 2,
                ),
                mortar_tone + rng.randint(-5, 4),
            )

    def soot_and_lime(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(80, 190), y + rng.randint(90, 240)),
                fill=128 - rng.randint(7, 14),
            )
        for _ in range(5):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(45, 130), y + rng.randint(35, 90)),
                fill=128 + rng.randint(5, 10),
            )

    return home.soft_overlay(base, 18.0, soot_and_lime)


def draw_exterior_plaster(base: Image.Image, rng) -> Image.Image:
    """Weathered stucco/whitewash without concrete formwork marks."""
    plaster = home.draw_whitewash(base, rng)
    draw = ImageDraw.Draw(plaster)

    # Thin trowel scars and small repairs keep the close read handmade. They
    # remain sparse so the wall does not become a map of narrative damage.
    for _ in range(18):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(45, 150)
        home.wrap_line(
            draw,
            (x, y, x + length, y + rng.randint(-3, 3)),
            home.BASE + rng.randint(-10, 8),
            width=rng.randint(1, 2),
        )
    for _ in range(4):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(70, 170)
        height = rng.randint(45, 120)
        home.wrap_rect(
            draw,
            (x, y, x + width, y + height),
            home.BASE + rng.randint(5, 13),
        )
        home.wrap_line(
            draw,
            (x, y + height, x + width, y + height),
            home.BASE - rng.randint(11, 18),
            width=2,
        )

    def runoff(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(13):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            width = rng.randint(5, 19)
            length = rng.randint(80, 260)
            steps = 4
            for step in range(steps):
                tone = 128 - 18 + step * 4
                home.wrap_rect(
                    layer_draw,
                    (
                        x,
                        y + length * step // steps,
                        x + width,
                        y + length * (step + 1) // steps,
                    ),
                    tone,
                )

    return home.soft_overlay(plaster, 13.0, runoff)


home.GRAMMARS["bar_exterior_brick"] = draw_exterior_brick
home.GRAMMARS["bar_exterior_plaster"] = draw_exterior_plaster


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
    home.HomeSheetSpec(
        key="BarExteriorBrickAlbedo",
        grammar="bar_exterior_brick",
        seed=0x42455842,
        cast=(1.04, 0.99, 0.94),
        mean_target=0.46,
        meters_per_tile=1.2,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("bar_exterior.py.BRICK",
             (0.30, 0.12, 0.075)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarExteriorPlasterAlbedo",
        grammar="bar_exterior_plaster",
        seed=0x42455850,
        cast=(1.03, 1.00, 0.95),
        mean_target=0.48,
        meters_per_tile=2.6,
        smoothness=0.035,
        metallic=0.0,
        tints=(
            ("bar_exterior.py.PLASTER",
             (0.48, 0.44, 0.34)),
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
