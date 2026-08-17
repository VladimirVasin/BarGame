#!/usr/bin/env python3
"""Build the deterministic surface albedos for the cemetery precinct.

Four seamless 1024x1024 sheets, imported by Unity at 512 and applied by
`CityCemeterySurfaceAppearance` over world-planar UVs baked into the
combined cemetery meshes: each batch's authored flat colour (brightened
by the solved per-sheet compensation) multiplies the sheet in linear
space, exactly like the home/POI pipelines.

The measured contract - linear luminance rule, wrap-by-construction
drawing, compensation solving and validation - is imported from
`build-home-textures.py`; this script adds four grammars:

* granite   - dark fine-speckled monument stone with faint pale veins.
  Deliberately low contrast: monument side faces sample the planar-XZ
  UVs as vertical streaks, and a quiet sheet reads as weathering there
  instead of smearing.
* stone     - weathered concrete/limestone for pillars: cracks, lichen
  speckle and rain-drip staining.
* gravel    - the packed alley: dense pebble field over trodden dirt.
* soil      - dark humus with leaf litter and moss for the cemetery
  ground sheet and the overgrown grave mounds.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

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
# cemetery grammars
# --------------------------------------------------------------------------


def draw_cemetery_granite(base: Image.Image, rng) -> Image.Image:
    """Fine dark speckle with sparse pale veins and soft polish bands."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((256, 0.55), (512, 0.85)), rng),
    )
    draw = ImageDraw.Draw(image)

    # The speckle field: quartz and feldspar grains a couple of pixels
    # wide, half a step off the base either way.
    for _ in range(7000):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        side = rng.randint(1, 2)
        tone = home.BASE + rng.choice((-16, -11, 10, 14))
        home.wrap_rect(draw, (x, y, x + side, y + side), tone)

    # Sparse pale veins wandering mostly downward, so a stretched
    # monument face reads them as natural stone banding.
    for _ in range(5):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        for _ in range(rng.randint(8, 14)):
            nx = x + rng.uniform(-30.0, 30.0)
            ny = y + rng.uniform(60.0, 130.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                home.BASE + rng.randint(12, 18),
                width=1,
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(180, 420), y + rng.randint(90, 200)),
                fill=rng.randint(118, 124),
            )
        for _ in range(4):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(120, 260), y + rng.randint(70, 150)),
                fill=rng.randint(132, 138),
            )

    return home.soft_overlay(image, 14, weathering)


def draw_cemetery_stone(base: Image.Image, rng) -> Image.Image:
    """Weathered concrete: cracks, lichen speckle, rain-drip stains."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.7), (256, 0.55), (512, 0.35)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Cracks: dark wanderers with an occasional branch.
    for _ in range(8):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        for _ in range(rng.randint(4, 9)):
            nx = x + rng.uniform(-60.0, 60.0)
            ny = y + rng.uniform(30.0, 90.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                home.BASE - rng.randint(42, 58),
                width=rng.randint(1, 2),
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # Lichen: clustered pale and dark dots colonising the surface.
    for _ in range(26):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        tone = home.BASE + rng.choice((-24, 18, 22))
        for _ in range(rng.randint(6, 16)):
            x = cx + rng.randint(-34, 34)
            y = cy + rng.randint(-26, 26)
            radius = rng.randint(2, 6)
            home.wrap_ellipse(
                draw,
                (x, y, x + radius * 2, y + radius * 2),
                fill=tone + rng.randint(-5, 5),
            )

    # Rain drips: soft vertical staining below imagined ledges.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            width = rng.randint(10, 26)
            length = rng.randint(120, 320)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + width, y + length),
                fill=rng.randint(108, 118),
            )
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 150), y + rng.randint(40, 110)),
                fill=rng.randint(134, 144),
            )

    return home.soft_overlay(image, 9, weathering)


def draw_cemetery_gravel(base: Image.Image, rng) -> Image.Image:
    """Packed alley gravel: dense pebbles trodden into darker dirt."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((128, 0.6), (256, 0.55), (512, 0.4)), rng),
    )
    draw = ImageDraw.Draw(image)

    # The dirt bed first: broad slightly-dark patches.
    for _ in range(26):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(60, 170), y + rng.randint(40, 120)),
            fill=home.BASE - rng.randint(8, 16),
        )

    # Then the pebbles: thousands of small stones in a tight tonal
    # band, each with a one-pixel shadow so the field reads granular
    # at half a metre and calm at ten.
    for _ in range(2600):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(4, 9)
        height = rng.randint(3, 7)
        tone = home.BASE + rng.choice((-18, -10, 8, 14, 20))
        home.wrap_ellipse(
            draw,
            (x, y + 1, x + width, y + height + 1),
            fill=home.BASE - 24,
        )
        home.wrap_ellipse(draw, (x, y, x + width, y + height), fill=tone)

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(9):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(120, 300), y + rng.randint(80, 180)),
                fill=rng.randint(112, 122),
            )

    return home.soft_overlay(image, 12, weathering)


def draw_cemetery_soil(base: Image.Image, rng) -> Image.Image:
    """Dark humus: leaf litter, moss patches and thin grass blades."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((32, 0.8), (128, 0.6), (512, 0.4)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Leaf litter: small irregular flakes both lighter and darker.
    for _ in range(900):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(3, 8)
        height = rng.randint(2, 5)
        tone = home.BASE + rng.choice((-22, -14, 12, 18))
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + height),
            fill=tone,
        )

    # Grass: sparse clusters of short pale blades leaning about.
    for _ in range(150):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        for _ in range(rng.randint(3, 7)):
            x = cx + rng.randint(-12, 12)
            y = cy + rng.randint(-8, 8)
            home.wrap_line(
                draw,
                (x, y, x + rng.randint(-4, 4), y - rng.randint(8, 16)),
                home.BASE + rng.randint(14, 22),
                width=1,
            )

    # Moss and damp ground, softened into broad staining.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(10):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(100, 280), y + rng.randint(80, 200)),
                fill=rng.randint(110, 120),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(70, 180), y + rng.randint(50, 130)),
                fill=rng.randint(134, 142),
            )

    return home.soft_overlay(image, 11, weathering)


home.GRAMMARS["cemetery_granite"] = draw_cemetery_granite
home.GRAMMARS["cemetery_stone"] = draw_cemetery_stone
home.GRAMMARS["cemetery_gravel"] = draw_cemetery_gravel
home.GRAMMARS["cemetery_soil"] = draw_cemetery_soil


# --------------------------------------------------------------------------
# specs — tints transcribe CityCemeteryWorldBuilder's palette (plus the
# shared cemetery ground colour from CityExteriorAppearance)
# --------------------------------------------------------------------------


CEMETERY_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="CityCemeteryGraniteAlbedo",
        grammar="cemetery_granite",
        seed=0x4347524E,
        cast=(0.99, 1.00, 1.03),
        mean_target=0.50,
        meters_per_tile=1.4,
        smoothness=0.18,
        metallic=0.0,
        tints=(
            ("CityCemeteryWorldBuilder.GraniteDark",
             (0.210, 0.220, 0.240)),
            ("CityCemeteryWorldBuilder.MarbleLight",
             (0.440, 0.440, 0.410)),
        ),
        contrast_floor=26,
    ),
    home.HomeSheetSpec(
        key="CityCemeteryStoneAlbedo",
        grammar="cemetery_stone",
        seed=0x4353544E,
        cast=(1.00, 1.00, 0.98),
        mean_target=0.50,
        meters_per_tile=1.8,
        smoothness=0.05,
        metallic=0.0,
        tints=(
            ("CityCemeteryWorldBuilder.WeatheredConcrete",
             (0.300, 0.310, 0.280)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityCemeteryGravelAlbedo",
        grammar="cemetery_gravel",
        seed=0x4347564C,
        cast=(1.02, 1.00, 0.96),
        mean_target=0.50,
        meters_per_tile=1.6,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("CityCemeteryWorldBuilder.Gravel",
             (0.300, 0.280, 0.230)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityCemeterySoilAlbedo",
        grammar="cemetery_soil",
        seed=0x43534F4C,
        cast=(1.00, 1.02, 0.94),
        mean_target=0.50,
        meters_per_tile=3.0,
        smoothness=0.03,
        metallic=0.0,
        tints=(
            ("CityCemeteryWorldBuilder.Soil",
             (0.160, 0.130, 0.090)),
            ("CityExteriorAppearance.CemeteryGround",
             (0.150, 0.200, 0.160)),
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
    for spec in CEMETERY_SHEET_SPECS:
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
    manifest_path = args.art_source / "cemetery-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "cemetery-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
