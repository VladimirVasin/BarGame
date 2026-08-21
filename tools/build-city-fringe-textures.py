#!/usr/bin/env python3
"""Build deterministic albedo sheets for the city's service fringe.

The west/south yards need their own material language without adding one
material instance per prop.  This tool produces three seamless 1024x1024 RGB
sources under ``Resources``; their checked-in Unity importers cap the runtime
copy at 512.  ``CityFringeYardSurfaceAppearance`` then applies the sheets and
their measured tint compensation through ``MaterialPropertyBlock`` recipes on
the shared runtime primitive material.

The common measured pipeline comes from ``build-home-textures.py``: seeded
generation, wrap-aware drawing, linear-luminance normalization, compensation
solving, seam/contrast validation, PNG hashing and a tiled contact sheet.  The
three grammars here are deliberately specific to the edge-of-city service
belt:

* service track - compacted fines, broad tyre presses, washed aggregate and
  occasional repaired potholes, readable without forcing every yard into one
  road direction;
* concrete - old three-metre pours, recessed joints, aggregate, hairline
  cracks, damp bloom and restrained rust runoff;
* masonry - irregular 2.4-metre coursed retaining stone, deep mortar,
  chipped arrises, efflorescence and damp staining.
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
    """Draw a polygon across every repeat touching the source sheet."""
    for dx in home.OFFSETS:
        for dy in home.OFFSETS:
            draw.polygon(
                [(x + dx, y + dy) for x, y in points],
                fill=fill,
            )


def draw_service_track(base: Image.Image, rng) -> Image.Image:
    """Compacted service ground: pressed fines, repairs and aggregate."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((32, 0.62), (128, 0.75), (512, 0.46)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Two broad, slightly wandering wheel presses give the sheet a service-
    # track scale cue.  A second faint cross-run keeps rotated yard pieces
    # plausible: this is a repeatedly trafficked apron, not one pristine road.
    for centre in (SHEET_SIZE * 0.27, SHEET_SIZE * 0.73):
        points: list[tuple[float, float]] = []
        for step in range(-1, 10):
            y = step * (SHEET_SIZE / 8)
            x = centre + math.sin(step * 1.37) * 28 + rng.uniform(-13, 13)
            points.append((x, y))
        for first, second in zip(points, points[1:]):
            home.wrap_line(
                draw,
                (*first, *second),
                home.BASE - rng.randint(15, 24),
                width=rng.randint(48, 70),
            )
            home.wrap_line(
                draw,
                (*first, *second),
                home.BASE + rng.randint(5, 11),
                width=rng.randint(3, 7),
            )

    for centre in (SHEET_SIZE * 0.31, SHEET_SIZE * 0.69):
        home.wrap_line(
            draw,
            (-120, centre + rng.randint(-24, 24),
             SHEET_SIZE + 120, centre + rng.randint(-24, 24)),
            home.BASE - rng.randint(7, 13),
            width=rng.randint(26, 42),
        )

    # Old potholes have been filled with coarser, slightly darker material.
    for _ in range(17):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(42, 112)
        height = rng.randint(25, 74)
        tone = home.BASE - rng.randint(18, 31)
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + height),
            fill=tone,
        )
        home.wrap_ellipse(
            draw,
            (x + 5, y + 4, x + width - 5, y + height - 5),
            fill=tone + rng.randint(6, 13),
        )

    # Aggregate gives the four-metre sheet a close-range scale cue.
    for _ in range(7200):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(1, 4)
        height = rng.randint(1, 3)
        tone = home.BASE + rng.choice((-25, -17, -10, 12, 18, 27))
        wrap_polygon(
            draw,
            [
                (x, y + height),
                (x + width * 0.45, y),
                (x + width, y + height * 0.72),
            ],
            tone,
        )

    # Broad damp and dust fields avoid a procedural-noise-only read.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(150, 390),
                 y + rng.randint(90, 260)),
                fill=rng.randint(106, 119),
            )
        for _ in range(8):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(100, 280),
                 y + rng.randint(70, 190)),
                fill=rng.randint(135, 146),
            )

    return home.soft_overlay(image, 15, weathering)


def draw_concrete(base: Image.Image, rng) -> Image.Image:
    """Weathered service concrete: large pours, cracks and runoff."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.72), (256, 0.60), (512, 0.34)), rng),
    )
    draw = ImageDraw.Draw(image)

    panel = SHEET_SIZE // 2
    joint = home.BASE - 43
    for row in range(2):
        for column in range(2):
            x0 = column * panel
            y0 = row * panel
            x1 = x0 + panel
            y1 = y0 + panel
            face = home.BASE + rng.choice((-9, -4, 2, 7, 11))
            home.wrap_rect(
                draw,
                (x0 + 4, y0 + 4, x1 - 4, y1 - 4),
                face,
            )

            # Form tie scars and small aggregate voids make this poured
            # concrete, not the river's dressed quay stone.
            for tx, ty in ((78, 78), (panel - 92, 88),
                           (84, panel - 90), (panel - 80, panel - 76)):
                radius = rng.randint(7, 12)
                home.wrap_ellipse(
                    draw,
                    (x0 + tx - radius, y0 + ty - radius,
                     x0 + tx + radius, y0 + ty + radius),
                    fill=face - rng.randint(22, 34),
                )
                home.wrap_ellipse(
                    draw,
                    (x0 + tx - radius + 3, y0 + ty - radius + 2,
                     x0 + tx + radius - 3, y0 + ty + radius - 3),
                    fill=face - rng.randint(7, 14),
                )

            for _ in range(650):
                x = x0 + rng.randrange(panel)
                y = y0 + rng.randrange(panel)
                radius = rng.randint(1, 3)
                home.wrap_ellipse(
                    draw,
                    (x, y, x + radius, y + radius),
                    fill=face + rng.choice((-16, -10, 9, 15)),
                )

            # Recessed pour joints are centred on the tile edges, so the
            # repeated source meets mortar-to-mortar rather than face-to-joint.
            home.wrap_rect(draw, (x0 - 3, y0, x0 + 3, y1), joint)
            home.wrap_rect(draw, (x0, y0 - 3, x1, y0 + 3), joint)
            home.wrap_line(
                draw,
                (x0 + 4, y0 + 4, x1 - 4, y0 + 4),
                home.BASE + 19,
                width=2,
            )

    # Hairline cracks branch without dominating the three-metre repeat.
    for _ in range(14):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        tone = home.BASE - rng.randint(29, 43)
        for segment in range(rng.randint(3, 7)):
            nx = x + rng.uniform(-54, 60)
            ny = y + rng.uniform(26, 82)
            home.wrap_line(draw, (x, y, nx, ny), tone, width=1)
            if segment > 0 and rng.random() < 0.24:
                home.wrap_line(
                    draw,
                    (x, y, x + rng.uniform(-42, 42), y + rng.uniform(12, 38)),
                    tone + 6,
                    width=1,
                )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        # Cool damp blooms and restrained rust drips from old fixings.
        for _ in range(13):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(100, 290),
                 y + rng.randint(80, 220)),
                fill=rng.randint(108, 119),
            )
        for _ in range(11):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(12, 34),
                 y + rng.randint(90, 250)),
                fill=rng.randint(116, 126),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(80, 220),
                 y + rng.randint(55, 150)),
                fill=rng.randint(136, 146),
            )

    return home.soft_overlay(image, 12, weathering)


def draw_masonry(base: Image.Image, rng) -> Image.Image:
    """Old retaining masonry: irregular courses, chips and salt bloom."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.60), (256, 0.62), (512, 0.40)), rng),
    )
    draw = ImageDraw.Draw(image)

    course = SHEET_SIZE // 4
    blocks_per_row = (3, 4, 3, 4)
    mortar = home.BASE - 49
    for row, count in enumerate(blocks_per_row):
        block = SHEET_SIZE / count
        shift = (block * 0.5) if row % 2 else 0.0
        y0 = row * course
        y1 = y0 + course
        for column in range(count):
            x0 = column * block + shift
            x1 = x0 + block
            jitter = rng.randint(-8, 8)
            face = home.BASE + rng.choice((-13, -8, -2, 5, 10, 15))
            points = [
                (x0 + 5, y0 + 5 + jitter * 0.20),
                (x1 - 5, y0 + 5 - jitter * 0.15),
                (x1 - 6, y1 - 5 + jitter * 0.12),
                (x0 + 5, y1 - 5 - jitter * 0.18),
            ]
            wrap_polygon(draw, points, face)

            # Chipped, proud centre and a dark lower arris produce a rough
            # old retaining wall rather than regular new brickwork.
            inset = rng.randint(15, 25)
            centre = [
                (x0 + inset, y0 + inset),
                (x1 - inset - rng.randint(0, 8), y0 + inset + rng.randint(0, 5)),
                (x1 - inset, y1 - inset),
                (x0 + inset + rng.randint(0, 7), y1 - inset - rng.randint(0, 5)),
            ]
            wrap_polygon(draw, centre, face + rng.randint(5, 13))
            home.wrap_line(
                draw,
                (x0 + inset, y1 - inset, x1 - inset, y1 - inset),
                face - rng.randint(18, 29),
                width=rng.randint(2, 4),
            )

            for _ in range(90):
                px = x0 + rng.random() * block
                py = y0 + rng.random() * course
                radius = rng.randint(1, 4)
                home.wrap_ellipse(
                    draw,
                    (px, py, px + radius, py + radius),
                    fill=face + rng.choice((-18, -11, 8, 14)),
                )

            home.wrap_rect(draw, (x0 - 3, y0, x0 + 3, y1), mortar)
            home.wrap_rect(draw, (x0, y0 - 3, x1, y0 + 3), mortar)

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(15):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(18, 52),
                 y + rng.randint(130, 360)),
                fill=rng.randint(103, 116),
            )
        for _ in range(10):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(70, 200),
                 y + rng.randint(42, 125)),
                fill=rng.randint(138, 149),
            )

    return home.soft_overlay(image, 10, weathering)


home.GRAMMARS.update(
    {
        "city_fringe_service_track": draw_service_track,
        "city_fringe_concrete": draw_concrete,
        "city_fringe_masonry": draw_masonry,
    }
)


FRINGE_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="CityFringeServiceTrackAlbedo",
        grammar="city_fringe_service_track",
        seed=0x4652544B,
        cast=(1.035, 1.000, 0.945),
        mean_target=0.48,
        meters_per_tile=4.0,
        smoothness=0.035,
        metallic=0.0,
        tints=(("CityFringeYardWorldBuilder.ServiceGround",
                (0.300, 0.275, 0.215)),),
        contrast_floor=46,
    ),
    home.HomeSheetSpec(
        key="CityFringeConcreteAlbedo",
        grammar="city_fringe_concrete",
        seed=0x4652434E,
        cast=(0.985, 1.015, 1.010),
        mean_target=0.50,
        meters_per_tile=3.0,
        smoothness=0.055,
        metallic=0.0,
        tints=(("CityFringeYardWorldBuilder.Concrete",
                (0.285, 0.315, 0.305)),),
        contrast_floor=42,
    ),
    home.HomeSheetSpec(
        key="CityFringeMasonryAlbedo",
        grammar="city_fringe_masonry",
        seed=0x46524D53,
        cast=(1.005, 1.020, 0.975),
        mean_target=0.50,
        meters_per_tile=2.4,
        smoothness=0.035,
        metallic=0.0,
        tints=(("CityFringeYardWorldBuilder.OldMasonry",
                (0.335, 0.350, 0.325)),),
        contrast_floor=48,
    ),
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
        help="Destination for the opaque 1024x1024 source sheets.",
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
    for spec in FRINGE_SHEET_SPECS:
        image, record = home.build_sheet(spec)
        record["resourcePath"] = f"Textures/{spec.key}"
        home.validate(image, spec, record)
        built.append((spec, image))
        if args.verify:
            record["sha256"] = hashlib.sha256(
                image.tobytes()
            ).hexdigest().upper()
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
        print(f"Validated {len(records)} fringe sheets; nothing written.")
        return

    manifest = {
        "sheetSize": SHEET_SIZE,
        "runtimeImportSize": 512,
        "meanLuminanceTolerance": home.MEAN_LUMINANCE_TOLERANCE,
        "brightnessErrorLimit": home.BRIGHTNESS_ERROR_LIMIT,
        "tintChannelFloor": home.TINT_CHANNEL_FLOOR,
        "sheets": records,
    }
    manifest_path = args.art_source / "fringe-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "fringe-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
