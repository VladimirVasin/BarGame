#!/usr/bin/env python3
"""Build the deterministic tileable albedo sheets for the production City bus.

The bus model carries world-scale box-projected UVs (see
`tools/build-city-bus-3d-model.py`), so every sheet here is authored to wrap
seamlessly and is consumed by `CityBusAssetSetup.BuildMaterials` at (1, 1)
tiling. Like the facade sheets, each albedo is a light base carrying dark
features: URP/Lit multiplies `_BaseColor` by `_BaseMap`, so the existing flat
material colors keep providing the actual hue and most of the value while the
texture adds panel, weave and wear detail. Pillow is the only dependency.

Run from the repository root::

    python tools/build-city-bus-textures.py
"""

from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Vehicles" / "Textures"

SEED = 260813
SHEET_SIZE = 512

# Light albedo bases keep the multiplied material color close to its flat
# value; anything much darker would visibly dim the bus against its city
# materials that never multiplied a texture.
PAINT_BASE = 204
METAL_BASE = 206
INTERIOR_BASE = 196
SEAT_BASE = 192


def tileable_noise(
    rng: random.Random,
    size: int,
    lattice: int,
    amplitude: float,
) -> Image.Image:
    """Return an L-mode sheet of smooth wrapping noise centred on 128."""
    values = [
        [rng.uniform(-1.0, 1.0) for _ in range(lattice)]
        for _ in range(lattice)
    ]
    cell = Image.new("L", (lattice, lattice))
    cell.putdata(
        [
            max(0, min(255, round(128 + values[y][x] * amplitude)))
            for y in range(lattice)
            for x in range(lattice)
        ]
    )
    tiled = Image.new("L", (lattice * 3, lattice * 3))
    for tile_x in range(3):
        for tile_y in range(3):
            tiled.paste(cell, (tile_x * lattice, tile_y * lattice))
    upsampled = tiled.resize((size * 3, size * 3), Image.BICUBIC)
    return upsampled.crop((size, size, size * 2, size * 2))


def apply_noise(base: Image.Image, noise: Image.Image) -> Image.Image:
    """Add signed noise (128-centred) onto a grayscale base sheet."""
    result = base.point(lambda value: value)
    base_pixels = result.load()
    noise_pixels = noise.load()
    width, height = result.size
    for y in range(height):
        for x in range(width):
            value = base_pixels[x, y] + noise_pixels[x, y] - 128
            base_pixels[x, y] = max(0, min(255, value))
    return result


def wrapped_line(
    draw: ImageDraw.ImageDraw,
    size: int,
    start: tuple[float, float],
    end: tuple[float, float],
    fill: int,
    width: int,
) -> None:
    """Draw a line at every 3x3 wrap offset so it tiles seamlessly."""
    for offset_x in (-size, 0, size):
        for offset_y in (-size, 0, size):
            draw.line(
                [
                    (start[0] + offset_x, start[1] + offset_y),
                    (end[0] + offset_x, end[1] + offset_y),
                ],
                fill=fill,
                width=width,
            )


def wrapped_dot(
    draw: ImageDraw.ImageDraw,
    size: int,
    center: tuple[float, float],
    radius: float,
    fill: int,
) -> None:
    for offset_x in (-size, 0, size):
        for offset_y in (-size, 0, size):
            cx = center[0] + offset_x
            cy = center[1] + offset_y
            draw.ellipse(
                (cx - radius, cy - radius, cx + radius, cy + radius),
                fill=fill,
            )


def mean_luminance(sheet: Image.Image) -> float:
    histogram = sheet.convert("L").histogram()
    total = sum(histogram)
    return sum(index * count for index, count in enumerate(histogram)) / (
        total * 255.0
    )


def build_paint(rng: random.Random) -> Image.Image:
    """Exterior paint: panel seams, rivet rows and streaked grime."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), PAINT_BASE)
    sheet = apply_noise(sheet, tileable_noise(rng, size, 24, 9.0))
    draw = ImageDraw.Draw(sheet)

    # Two horizontal panel seams per tile with a rivet row under each.
    for seam_y in (size * 0.25, size * 0.75):
        wrapped_line(draw, size, (0, seam_y), (size, seam_y), 150, 2)
        wrapped_line(
            draw, size, (0, seam_y + 2), (size, seam_y + 2), 226, 1
        )
        for rivet in range(16):
            rivet_x = (rivet + 0.5) * size / 16
            wrapped_dot(draw, size, (rivet_x, seam_y + 8), 2.0, 158)
    # One vertical seam so long side panels break up as well.
    wrapped_line(draw, size, (size * 0.5, 0), (size * 0.5, size), 156, 2)

    # Sparse vertical grime streaks running down from the upper seam line.
    for _ in range(26):
        streak_x = rng.uniform(0, size)
        streak_top = rng.uniform(0, size)
        streak_length = rng.uniform(size * 0.08, size * 0.35)
        tone = rng.randint(172, 194)
        wrapped_line(
            draw,
            size,
            (streak_x, streak_top),
            (streak_x, streak_top + streak_length),
            tone,
            rng.randint(1, 3),
        )
    return sheet.filter(ImageFilter.GaussianBlur(0.6))


def build_metal(rng: random.Random) -> Image.Image:
    """Brushed metal: long horizontal grain plus faint scratches."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), METAL_BASE)
    grain = tileable_noise(rng, size, 12, 14.0)
    grain = grain.resize((size // 8, size), Image.BICUBIC).resize(
        (size, size), Image.BICUBIC
    )
    sheet = apply_noise(sheet, grain)
    draw = ImageDraw.Draw(sheet)
    for _ in range(40):
        scratch_y = rng.uniform(0, size)
        scratch_x = rng.uniform(0, size)
        scratch_length = rng.uniform(size * 0.05, size * 0.5)
        tone = rng.choice((178, 186, 222, 230))
        wrapped_line(
            draw,
            size,
            (scratch_x, scratch_y),
            (scratch_x + scratch_length, scratch_y),
            tone,
            1,
        )
    return sheet.filter(ImageFilter.GaussianBlur(0.5))


def build_interior(rng: random.Random) -> Image.Image:
    """Interior linoleum: speckle field over soft ribbing."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), INTERIOR_BASE)
    sheet = apply_noise(sheet, tileable_noise(rng, size, 32, 8.0))
    draw = ImageDraw.Draw(sheet)
    for rib in range(8):
        rib_x = (rib + 0.5) * size / 8
        wrapped_line(draw, size, (rib_x, 0), (rib_x, size), 178, 2)
        wrapped_line(draw, size, (rib_x + 3, 0), (rib_x + 3, size), 212, 1)
    for _ in range(900):
        speck_x = rng.uniform(0, size)
        speck_y = rng.uniform(0, size)
        tone = rng.choice((168, 176, 216, 224))
        wrapped_dot(draw, size, (speck_x, speck_y), rng.uniform(0.6, 1.4), tone)
    return sheet.filter(ImageFilter.GaussianBlur(0.4))


def build_seat(rng: random.Random) -> Image.Image:
    """Seat fabric: fine two-way weave with soft wear blotches."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), SEAT_BASE)
    pixels = sheet.load()
    for y in range(size):
        for x in range(size):
            weave = 6 if ((x // 4) + (y // 4)) % 2 == 0 else -6
            fine = 3 if (x + y) % 2 == 0 else -3
            pixels[x, y] = max(0, min(255, SEAT_BASE + weave + fine))
    sheet = apply_noise(sheet, tileable_noise(rng, size, 16, 10.0))
    return sheet.filter(ImageFilter.GaussianBlur(0.3))


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate the deterministic City bus albedo sheets."
    )
    parser.add_argument(
        "--texture-dir",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
    )
    args = parser.parse_args()
    args.texture_dir.mkdir(parents=True, exist_ok=True)

    builders = {
        "CityBusPaintAlbedo.png": build_paint,
        "CityBusMetalAlbedo.png": build_metal,
        "CityBusInteriorAlbedo.png": build_interior,
        "CityBusSeatAlbedo.png": build_seat,
    }
    print("CITY BUS TEXTURES BUILD OK")
    for file_name, builder in builders.items():
        rng = random.Random(f"{SEED}:{file_name}")
        sheet = builder(rng).convert("RGB")
        if sheet.size != (SHEET_SIZE, SHEET_SIZE):
            raise RuntimeError(f"{file_name} lost its {SHEET_SIZE} px contract")
        target = args.texture_dir / file_name
        sheet.save(target, format="PNG")
        print(f"  {file_name}: mean luminance {mean_luminance(sheet):.3f}")
    print(f"  Output: {args.texture_dir}")


if __name__ == "__main__":
    sys.exit(main())
