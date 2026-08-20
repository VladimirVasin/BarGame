#!/usr/bin/env python3
"""Build the deterministic low-poly mountain rock albedo.

The seamless 1024x1024 source sheet is packaged under Resources and imported
by Unity at 512 through its checked-in ``.meta`` file. Runtime mountain meshes
bake box-projected world UVs at the metre pitch declared here, then
``CityMountainSurfaceAppearance`` applies the sheet and compensated authored
tint through a ``MaterialPropertyBlock`` on the shared primitive material.

The measured contract and texture mechanics come from
``build-home-textures.py``. This generator adds one mountain grammar: broad
folded strata, fractured rock plates, dark drainage seams and small scree.
Every mark wraps by construction, so the large boundary meshes can repeat the
sheet without a visible seam.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
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


def wrap_polygon(
    draw: ImageDraw.ImageDraw,
    points: list[tuple[float, float]],
    fill: int,
) -> None:
    """Draw an irregular plate across every repeat of the source sheet."""
    for dx in home.OFFSETS:
        for dy in home.OFFSETS:
            draw.polygon(
                [(x + dx, y + dy) for x, y in points],
                fill=fill,
            )


def draw_mountain_rock(base: Image.Image, rng) -> Image.Image:
    """Cold coastal rock: folded strata, split faces and loose scree."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((16, 0.75), (64, 0.72), (256, 0.48)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Broad slanted strata create a geological direction that survives at
    # distance. Alternating pale lips and dark beds make the folds readable
    # under the city's dense green-grey fog.
    band_pitch = 128
    for band in range(-2, SHEET_SIZE // band_pitch + 3):
        offset = band * band_pitch + rng.randint(-12, 12)
        lean = rng.randint(115, 175)
        home.wrap_line(
            draw,
            (-160, offset, SHEET_SIZE + 160, offset + lean),
            home.BASE - rng.randint(24, 36),
            width=rng.randint(12, 22),
        )
        home.wrap_line(
            draw,
            (-160, offset - 8, SHEET_SIZE + 160, offset + lean - 8),
            home.BASE + rng.randint(13, 23),
            width=rng.randint(3, 6),
        )

    # Broken plates interrupt the regular beds. Each plate has a darker
    # undercut shifted down-right, followed by the face itself.
    for _ in range(78):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        width = rng.randint(45, 140)
        height = rng.randint(28, 95)
        corners = rng.randint(5, 8)
        points: list[tuple[float, float]] = []
        for corner in range(corners):
            angle = math.tau * corner / corners
            reach = rng.uniform(0.72, 1.0)
            points.append(
                (
                    cx + math.cos(angle) * width * 0.5 * reach,
                    cy + math.sin(angle) * height * 0.5 * reach,
                )
            )
        wrap_polygon(
            draw,
            [(x + 5, y + 7) for x, y in points],
            home.BASE - rng.randint(34, 48),
        )
        wrap_polygon(
            draw,
            points,
            home.BASE + rng.choice((-12, -5, 8, 15, 22)),
        )

    # Drainage seams and cracks wander down the face, occasionally forking.
    for _ in range(28):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        tone = home.BASE - rng.randint(42, 62)
        for segment in range(rng.randint(3, 7)):
            nx = x + rng.uniform(-38.0, 42.0)
            ny = y + rng.uniform(35.0, 105.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                tone,
                width=rng.randint(1, 3),
            )
            if segment > 0 and rng.random() < 0.28:
                home.wrap_line(
                    draw,
                    (x, y, x + rng.uniform(-45.0, 45.0), y + 34.0),
                    tone + 7,
                    width=1,
                )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # Angular scree gives the lower-frequency sheet a close-range scale cue.
    for _ in range(2100):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(2, 8)
        height = rng.randint(2, 6)
        tone = home.BASE + rng.choice((-28, -18, 14, 21, 29))
        wrap_polygon(
            draw,
            [
                (x, y + height),
                (x + width * 0.45, y),
                (x + width, y + height * 0.72),
            ],
            tone,
        )

    # Damp shadow and salt-bleached faces soften into the structure without
    # breaking the repeat at the source edges.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(120, 340), y + rng.randint(90, 250)),
                fill=rng.randint(106, 119),
            )
        for _ in range(8):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 250), y + rng.randint(70, 190)),
                fill=rng.randint(136, 147),
            )

    return home.soft_overlay(image, 13, weathering)


home.GRAMMARS["mountain_rock"] = draw_mountain_rock


MOUNTAIN_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="CityMountainRockAlbedo",
        grammar="mountain_rock",
        seed=0x4D54524B,
        cast=(0.98, 1.02, 1.00),
        mean_target=0.50,
        meters_per_tile=6.0,
        smoothness=0.025,
        metallic=0.0,
        tints=(
            ("CityMountainBoundaryWorldBuilder.ForeRock",
             (0.210, 0.235, 0.215)),
            ("CityMountainBoundaryWorldBuilder.MidRock",
             (0.255, 0.280, 0.255)),
            ("CityMountainBoundaryWorldBuilder.HighRock",
             (0.295, 0.320, 0.300)),
        ),
        contrast_floor=44,
    ),
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
        help="Destination for the opaque 1024x1024 source/runtime sheet.",
    )
    parser.add_argument(
        "--art-source",
        type=Path,
        default=DEFAULT_ART_SOURCE,
        help="Destination for the measured manifest and contact sheet.",
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Build and validate without writing anything.",
    )
    args = parser.parse_args()

    records: list[dict] = []
    built: list[tuple[home.HomeSheetSpec, Image.Image]] = []
    for spec in MOUNTAIN_SHEET_SPECS:
        image, record = home.build_sheet(spec)
        record["resourcePath"] = f"Textures/{spec.key}"
        home.validate(image, spec, record)
        built.append((spec, image))
        if args.verify:
            record["sha256"] = hashlib.sha256(image.tobytes()).hexdigest().upper()
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
        print(f"Validated {len(records)} mountain sheet; nothing written.")
        return

    manifest = {
        "sheetSize": SHEET_SIZE,
        "runtimeImportSize": 512,
        "meanLuminanceTolerance": home.MEAN_LUMINANCE_TOLERANCE,
        "brightnessErrorLimit": home.BRIGHTNESS_ERROR_LIMIT,
        "tintChannelFloor": home.TINT_CHANNEL_FLOOR,
        "sheets": records,
    }
    manifest_path = args.art_source / "mountain-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "mountain-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
