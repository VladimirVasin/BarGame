#!/usr/bin/env python3
"""Build the deterministic surface albedos for the supermarket interior.

Six seamless 1024x1024 sheets, imported by Unity at 512, tiled by metres
through `SupermarketSurfaceAppearance` exactly the way the home pipeline
does it: each renderer's authored flat colour (brightened by the solved
per-sheet compensation) multiplies the sheet in linear space.

The whole measured contract - the linear luminance rule, wrap-by-
construction drawing, compensation solving and validation - is imported
from `build-home-textures.py`; this script only adds the supermarket
grammars (worn linoleum tile, ceiling panels, corrugated cardboard) and
reuses the home stucco/painted-metal/laminate grammars for the rest.
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
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Supermarket" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "Supermarket"

_home_spec = importlib.util.spec_from_file_location(
    "build_home_textures",
    Path(__file__).resolve().parent / "build-home-textures.py",
)
home = importlib.util.module_from_spec(_home_spec)
sys.modules["build_home_textures"] = home
_home_spec.loader.exec_module(home)

SHEET_SIZE = home.SHEET_SIZE


# --------------------------------------------------------------------------
# supermarket grammars
# --------------------------------------------------------------------------


def draw_market_linoleum(base: Image.Image, rng) -> Image.Image:
    """Big worn linoleum squares: 4x4 tiles, grout, traffic scuffs."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.8), (256, 0.5)), rng),
    )
    draw = ImageDraw.Draw(image)
    pitch = SHEET_SIZE // 4
    for row in range(4):
        for column in range(4):
            tone = 128 + rng.randint(-11, 11)
            x = column * pitch
            y = row * pitch
            tile = Image.new("L", (pitch - 4, pitch - 4), tone)
            image.paste(
                ImageChops.overlay(
                    image.crop((x + 2, y + 2, x + pitch - 2, y + pitch - 2)),
                    tile,
                ),
                (x + 2, y + 2),
            )

    for line in range(5):
        offset = (line * pitch) % SHEET_SIZE
        home.wrap_line(
            draw,
            (offset, 0, offset, SHEET_SIZE),
            96,
            width=5,
        )
        home.wrap_line(
            draw,
            (0, offset, SHEET_SIZE, offset),
            96,
            width=5,
        )

    def scuffs(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(26):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            length = rng.randint(70, 300)
            home.wrap_line(
                layer_draw,
                (x, y, x + length, y + rng.randint(-24, 24)),
                rng.randint(96, 116),
                width=rng.randint(2, 6),
            )
        for _ in range(9):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 190), y + rng.randint(40, 130)),
                fill=rng.randint(112, 122),
            )

    return home.soft_overlay(image, 7, scuffs)


def draw_market_ceiling(base: Image.Image, rng) -> Image.Image:
    """Suspended ceiling panels over the whitewash field, with stains."""
    image = home.draw_whitewash(base, rng)
    draw = ImageDraw.Draw(image)
    pitch = SHEET_SIZE // 4
    for line in range(5):
        offset = (line * pitch) % SHEET_SIZE
        home.wrap_line(
            draw,
            (offset, 0, offset, SHEET_SIZE),
            104,
            width=7,
        )
        home.wrap_line(
            draw,
            (0, offset, SHEET_SIZE, offset),
            104,
            width=7,
        )

    def stains(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(5):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 260), y + rng.randint(70, 200)),
                fill=rng.randint(104, 118),
            )

    return home.soft_overlay(image, 12, stains)


def draw_market_cardboard(base: Image.Image, rng) -> Image.Image:
    """Corrugated carton: flute lines, crease shadows, a tape band."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((32, 0.7), (128, 0.5), (512, 0.25)), rng),
    )
    draw = ImageDraw.Draw(image)
    pitch = 16
    for line in range(SHEET_SIZE // pitch):
        y = line * pitch
        home.wrap_line(
            draw,
            (0, y, SHEET_SIZE, y),
            120 + (line % 2) * 14,
            width=2,
        )

    for crease in range(4):
        x = (crease * (SHEET_SIZE // 4) +
             rng.randint(-40, 40)) % SHEET_SIZE
        home.wrap_line(
            draw,
            (x, 0, x, SHEET_SIZE),
            102,
            width=rng.randint(3, 6),
        )

    home.wrap_rect(
        draw,
        (0, SHEET_SIZE // 2 - 44, SHEET_SIZE, SHEET_SIZE // 2 + 44),
        fill=150,
    )

    def shading(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(10):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 240), y + rng.randint(60, 160)),
                fill=rng.randint(112, 126),
            )

    return home.soft_overlay(image, 9, shading)


home.GRAMMARS["market_linoleum"] = draw_market_linoleum
home.GRAMMARS["market_ceiling"] = draw_market_ceiling
home.GRAMMARS["market_cardboard"] = draw_market_cardboard


# --------------------------------------------------------------------------
# specs — tints transcribe SupermarketInteriorWorldBuilder's palette
# --------------------------------------------------------------------------


SUPERMARKET_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="SupermarketLinoleumAlbedo",
        grammar="market_linoleum",
        seed=0x534D464C,
        cast=(1.00, 1.01, 0.99),
        mean_target=0.50,
        meters_per_tile=2.4,
        smoothness=0.16,
        metallic=0.0,
        tints=(
            ("SupermarketInteriorWorldBuilder.Floor",
             (0.255, 0.285, 0.265)),
            ("SupermarketInteriorWorldBuilder.FloorPatch",
             (0.165, 0.185, 0.170)),
        ),
    ),
    home.HomeSheetSpec(
        key="SupermarketWallPaintAlbedo",
        grammar="stucco",
        seed=0x534D5750,
        cast=(1.01, 1.00, 0.97),
        mean_target=0.52,
        meters_per_tile=2.6,
        smoothness=0.05,
        metallic=0.0,
        tints=(
            ("SupermarketInteriorWorldBuilder.Wall",
             (0.56, 0.57, 0.47)),
            ("SupermarketInteriorWorldBuilder.WallShadow",
             (0.275, 0.305, 0.265)),
        ),
    ),
    home.HomeSheetSpec(
        key="SupermarketCeilingAlbedo",
        grammar="market_ceiling",
        seed=0x534D434C,
        cast=(1.00, 1.00, 0.98),
        mean_target=0.52,
        meters_per_tile=3.0,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("SupermarketInteriorWorldBuilder.Ceiling",
             (0.40, 0.43, 0.38)),
        ),
    ),
    home.HomeSheetSpec(
        key="SupermarketShelfMetalAlbedo",
        grammar="painted_metal",
        seed=0x534D534D,
        cast=(0.99, 1.00, 0.99),
        mean_target=0.50,
        meters_per_tile=1.3,
        smoothness=0.24,
        metallic=0.25,
        tints=(
            ("SupermarketInteriorWorldBuilder.ShelfFrame",
             (0.34, 0.36, 0.31)),
            ("SupermarketInteriorWorldBuilder.ShelfSurface",
             (0.47, 0.48, 0.39)),
            ("SupermarketInteriorWorldBuilder.ColdMetal",
             (0.38, 0.47, 0.45)),
            ("SupermarketInteriorWorldBuilder.ColdInterior",
             (0.245, 0.315, 0.305)),
        ),
    ),
    home.HomeSheetSpec(
        key="SupermarketCounterAlbedo",
        grammar="laminate",
        seed=0x534D434F,
        cast=(1.02, 1.00, 0.96),
        mean_target=0.50,
        meters_per_tile=1.2,
        smoothness=0.20,
        metallic=0.0,
        tints=(
            ("SupermarketInteriorWorldBuilder.CheckoutBase",
             (0.31, 0.38, 0.32)),
            ("SupermarketInteriorWorldBuilder.CheckoutTrim",
             (0.67, 0.61, 0.38)),
        ),
    ),
    home.HomeSheetSpec(
        key="SupermarketCardboardAlbedo",
        grammar="market_cardboard",
        seed=0x534D4342,
        cast=(1.04, 1.00, 0.93),
        mean_target=0.50,
        meters_per_tile=0.9,
        smoothness=0.03,
        metallic=0.0,
        tints=(
            ("SupermarketInteriorWorldBuilder.Cardboard",
             (0.44, 0.34, 0.20)),
            ("SupermarketInteriorWorldBuilder.CartonUpper",
             (0.38, 0.29, 0.17)),
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
    for spec in SUPERMARKET_SHEET_SPECS:
        image, record = home.build_sheet(spec)
        record["resourcePath"] = f"Supermarket/Textures/{spec.key}"
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
    manifest_path = args.art_source / "supermarket-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "supermarket-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
