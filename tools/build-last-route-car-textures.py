#!/usr/bin/env python3
"""Build the deterministic tileable albedo sheets for the Last Route car.

The car model carries world-scale box-projected UVs (see
`tools/build-last-route-car-3d-model.py`), so every sheet here wraps
seamlessly and is consumed by `LastRouteCarAssetSetup` at (1, 1) tiling. As
with the bus sheets, each albedo is a LIGHT base carrying dark features: URP
Lit multiplies `_BaseColor` by `_BaseMap`, so the material colour still
supplies the hue and most of the value while the texture adds the wear.

Four sheets, because the car is four different states of neglect: lacquer
that has chalked and been scuffed back to primer, rust that has bloomed
through it, a cabin lining gone brittle, and seat cloth worn through at the
edges. Everything else on the car reuses the bus's own metal sheet - a
chrome bumper is a chrome bumper.

Pillow is the only dependency. Run from the repository root::

    python tools/build-last-route-car-textures.py
"""

from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Vehicles" / "Textures"

SEED = 470303
SHEET_SIZE = 512

# Light bases, for the same reason the bus's are light: the sheet multiplies
# the flat colour, and a dark sheet would sink the car below every other
# material in the city.
PAINT_BASE = 200
RUST_BASE = 198
INTERIOR_BASE = 190
SEAT_BASE = 188


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


def wrapped_blob(
    draw: ImageDraw.ImageDraw,
    rng: random.Random,
    size: int,
    center: tuple[float, float],
    radius: float,
    fill: int,
) -> None:
    """An irregular patch, drawn as a ring of overlapping dots.

    Rust and scuffs do not have circular edges; a blob assembled from
    jittered dots keeps the silhouette ragged at every wrap offset.
    """
    for _ in range(10):
        angle = rng.uniform(0.0, 6.283185)
        distance = rng.uniform(0.0, radius * 0.7)
        wrapped_dot(
            draw,
            size,
            (
                center[0] + distance * rng.uniform(0.6, 1.0) * (1 if angle < 3.14 else -1),
                center[1] + distance * rng.uniform(0.6, 1.0),
            ),
            radius * rng.uniform(0.35, 0.65),
            fill,
        )


def mean_luminance(sheet: Image.Image) -> float:
    histogram = sheet.convert("L").histogram()
    total = sum(histogram)
    return sum(index * count for index, count in enumerate(histogram)) / (
        total * 255.0
    )


def build_paint(rng: random.Random) -> Image.Image:
    """Chalked lacquer: panel seams, scuffs back to primer, water streaks."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), PAINT_BASE)
    sheet = apply_noise(sheet, tileable_noise(rng, size, 20, 11.0))
    draw = ImageDraw.Draw(sheet)

    # One horizontal panel seam per tile, with the shadow under it.
    for seam_y in (size * 0.34, size * 0.82):
        wrapped_line(draw, size, (0, seam_y), (size, seam_y), 158, 2)
        wrapped_line(draw, size, (0, seam_y + 2), (size, seam_y + 2), 222, 1)

    # Scuffs: bright, because paint rubbed off shows lighter primer.
    for _ in range(26):
        wrapped_blob(
            draw,
            rng,
            size,
            (rng.uniform(0, size), rng.uniform(0, size)),
            rng.uniform(4.0, 13.0),
            rng.choice((218, 226, 232)),
        )

    # Water streaks running down from the seams.
    for _ in range(22):
        x = rng.uniform(0, size)
        top = rng.uniform(0, size)
        wrapped_line(
            draw,
            size,
            (x, top),
            (x + rng.uniform(-3.0, 3.0), top + rng.uniform(18.0, 70.0)),
            rng.choice((172, 180, 188)),
            1,
        )

    return sheet.filter(ImageFilter.GaussianBlur(0.5))


def build_rust(rng: random.Random) -> Image.Image:
    """Rust: blooms with hard scale at their centres and soft haloes."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), RUST_BASE)
    sheet = apply_noise(sheet, tileable_noise(rng, size, 12, 18.0))
    draw = ImageDraw.Draw(sheet)

    for _ in range(14):
        cx = rng.uniform(0, size)
        cy = rng.uniform(0, size)
        wrapped_blob(draw, rng, size, (cx, cy), rng.uniform(16.0, 34.0), 168)
        wrapped_blob(draw, rng, size, (cx, cy), rng.uniform(7.0, 15.0), 128)

    # Flaking scale: small hard specks over the blooms.
    for _ in range(320):
        wrapped_dot(
            draw,
            size,
            (rng.uniform(0, size), rng.uniform(0, size)),
            rng.uniform(0.6, 2.0),
            rng.choice((104, 120, 214)),
        )

    return sheet.filter(ImageFilter.GaussianBlur(0.6))


def build_interior(rng: random.Random) -> Image.Image:
    """Cabin lining: brittle vinyl with a cracked grain."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), INTERIOR_BASE)
    sheet = apply_noise(sheet, tileable_noise(rng, size, 28, 8.0))
    draw = ImageDraw.Draw(sheet)

    for _ in range(90):
        x = rng.uniform(0, size)
        y = rng.uniform(0, size)
        length = rng.uniform(6.0, 26.0)
        angle = rng.choice((0.0, 0.6, -0.6, 1.4))
        wrapped_line(
            draw,
            size,
            (x, y),
            (x + length, y + length * angle),
            rng.choice((162, 170, 208)),
            1,
        )

    return sheet.filter(ImageFilter.GaussianBlur(0.4))


def build_seat(rng: random.Random) -> Image.Image:
    """Seat cloth: a coarse weave worn shiny in patches."""
    size = SHEET_SIZE
    sheet = Image.new("L", (size, size), SEAT_BASE)
    pixels = sheet.load()
    for y in range(size):
        for x in range(size):
            weave = 7 if ((x // 5) + (y // 5)) % 2 == 0 else -7
            fine = 3 if (x + y) % 2 == 0 else -3
            pixels[x, y] = max(0, min(255, SEAT_BASE + weave + fine))
    sheet = apply_noise(sheet, tileable_noise(rng, size, 14, 12.0))

    draw = ImageDraw.Draw(sheet)
    for _ in range(12):
        wrapped_blob(
            draw,
            rng,
            size,
            (rng.uniform(0, size), rng.uniform(0, size)),
            rng.uniform(10.0, 24.0),
            rng.choice((206, 214)),
        )

    return sheet.filter(ImageFilter.GaussianBlur(0.35))


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate the deterministic Last Route car albedo sheets."
    )
    parser.add_argument(
        "--texture-dir",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
    )
    args = parser.parse_args()
    args.texture_dir.mkdir(parents=True, exist_ok=True)

    builders = {
        "LastRouteCarPaintAlbedo.png": build_paint,
        "LastRouteCarRustAlbedo.png": build_rust,
        "LastRouteCarInteriorAlbedo.png": build_interior,
        "LastRouteCarSeatAlbedo.png": build_seat,
    }
    print("LAST ROUTE CAR TEXTURES BUILD OK")
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
