#!/usr/bin/env python3
"""Build deterministic albedo sheets for the generated Gothic church.

The sheets are intentionally modest 512 px authored textures.  Broad surfaces
tile, while sacred art, murals and glass are atlases consumed by UV'd
Blender panels.  Unity binds them to material assets cloned from the project's
shared RuntimePrimitiveLit and CityNoirEmission materials.

Run from the repository root::

    python tools/build-church-textures.py
"""

from __future__ import annotations

import argparse
import hashlib
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "Assets" / "Church" / "Textures"
SIZE = 512
SEED = 260826


def clamp(value: float) -> int:
    return max(0, min(255, round(value)))


def wrapping_noise(
    rng: random.Random,
    lattice: int,
    amplitude: float,
) -> Image.Image:
    cell = Image.new("L", (lattice, lattice))
    cell.putdata([
        clamp(128 + rng.uniform(-amplitude, amplitude))
        for _ in range(lattice * lattice)
    ])
    tiled = Image.new("L", (lattice * 3, lattice * 3))
    for x in range(3):
        for y in range(3):
            tiled.paste(cell, (x * lattice, y * lattice))
    expanded = tiled.resize((SIZE * 3, SIZE * 3), Image.Resampling.BICUBIC)
    return expanded.crop((SIZE, SIZE, SIZE * 2, SIZE * 2))


def tinted_noise(
    rng: random.Random,
    color: tuple[int, int, int],
    lattice: int,
    amplitude: float,
) -> Image.Image:
    noise = wrapping_noise(rng, lattice, amplitude)
    image = Image.new("RGB", (SIZE, SIZE), color)
    source = noise.load()
    pixels = image.load()
    for y in range(SIZE):
        for x in range(SIZE):
            delta = source[x, y] - 128
            pixels[x, y] = tuple(clamp(channel + delta) for channel in color)
    return image


def wrapped_line(
    draw: ImageDraw.ImageDraw,
    start: tuple[float, float],
    end: tuple[float, float],
    fill: tuple[int, int, int],
    width: int,
) -> None:
    for ox in (-SIZE, 0, SIZE):
        for oy in (-SIZE, 0, SIZE):
            draw.line(
                ((start[0] + ox, start[1] + oy),
                 (end[0] + ox, end[1] + oy)),
                fill=fill,
                width=width,
            )


def wrapped_ellipse(
    draw: ImageDraw.ImageDraw,
    center: tuple[float, float],
    radii: tuple[float, float],
    fill: tuple[int, int, int],
) -> None:
    for ox in (-SIZE, 0, SIZE):
        for oy in (-SIZE, 0, SIZE):
            cx, cy = center[0] + ox, center[1] + oy
            rx, ry = radii
            draw.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=fill)


def plaster(rng: random.Random) -> Image.Image:
    image = tinted_noise(rng, (218, 216, 201), 20, 13)
    draw = ImageDraw.Draw(image)
    for _ in range(42):
        x = rng.uniform(0, SIZE)
        y = rng.uniform(0, SIZE)
        length = rng.uniform(18, 90)
        tone = rng.choice(((166, 169, 157), (184, 180, 163)))
        wrapped_line(
            draw,
            (x, y),
            (x + rng.uniform(-9, 9), y + length),
            tone,
            rng.choice((1, 1, 2)),
        )
    for _ in range(18):
        wrapped_ellipse(
            draw,
            (rng.uniform(0, SIZE), rng.uniform(0, SIZE)),
            (rng.uniform(10, 38), rng.uniform(4, 15)),
            rng.choice(((181, 185, 171), (194, 190, 172))),
        )
    return image.filter(ImageFilter.GaussianBlur(0.75))


def stone(rng: random.Random) -> Image.Image:
    image = tinted_noise(rng, (178, 177, 166), 24, 15)
    draw = ImageDraw.Draw(image)
    course = 64
    for row in range(SIZE // course):
        y = row * course
        wrapped_line(draw, (0, y), (SIZE, y), (111, 113, 108), 5)
        offset = 0 if row % 2 == 0 else course
        for x in range(offset, SIZE + course, course * 2):
            wrapped_line(
                draw,
                (x, y),
                (x, y + course),
                (119, 120, 113),
                4,
            )
    for _ in range(80):
        wrapped_ellipse(
            draw,
            (rng.uniform(0, SIZE), rng.uniform(0, SIZE)),
            (rng.uniform(2, 8), rng.uniform(1, 5)),
            rng.choice(((135, 137, 130), (204, 199, 181))),
        )
    return image.filter(ImageFilter.GaussianBlur(0.45))


def wood(rng: random.Random) -> Image.Image:
    image = tinted_noise(rng, (172, 139, 103), 16, 13)
    draw = ImageDraw.Draw(image)
    for x in range(0, SIZE, 64):
        wrapped_line(draw, (x, 0), (x, SIZE), (103, 73, 52), 5)
        wrapped_line(draw, (x + 5, 0), (x + 5, SIZE), (207, 174, 127), 2)
    for _ in range(45):
        x = rng.uniform(0, SIZE)
        y = rng.uniform(0, SIZE)
        wrapped_line(
            draw,
            (x, y),
            (x + rng.uniform(-5, 5), y + rng.uniform(25, 100)),
            rng.choice(((119, 83, 58), (201, 164, 119))),
            rng.choice((1, 2)),
        )
    return image.filter(ImageFilter.GaussianBlur(0.4))


def metal(rng: random.Random) -> Image.Image:
    image = tinted_noise(rng, (154, 160, 151), 28, 17)
    draw = ImageDraw.Draw(image)
    for x in range(0, SIZE, 86):
        wrapped_line(draw, (x, 0), (x, SIZE), (81, 91, 86), 4)
        wrapped_line(draw, (x + 4, 0), (x + 4, SIZE), (196, 199, 183), 2)
    for _ in range(70):
        x = rng.uniform(0, SIZE)
        y = rng.uniform(0, SIZE)
        length = rng.uniform(8, 70)
        wrapped_line(
            draw,
            (x, y),
            (x + length, y + rng.uniform(-2, 2)),
            rng.choice(((101, 111, 104), (185, 189, 174))),
            1,
        )
    return image.filter(ImageFilter.GaussianBlur(0.35))


def floor(rng: random.Random) -> Image.Image:
    image = tinted_noise(rng, (154, 149, 137), 24, 12)
    draw = ImageDraw.Draw(image)
    tile = 64
    for index in range(0, SIZE, tile):
        wrapped_line(draw, (index, 0), (index, SIZE), (91, 89, 84), 4)
        wrapped_line(draw, (0, index), (SIZE, index), (91, 89, 84), 4)
        wrapped_line(draw, (index + 4, 0), (index + 4, SIZE), (185, 179, 161), 1)
        wrapped_line(draw, (0, index + 4), (SIZE, index + 4), (185, 179, 161), 1)
    for _ in range(35):
        x = rng.uniform(0, SIZE)
        y = rng.uniform(0, SIZE)
        wrapped_line(
            draw,
            (x, y),
            (x + rng.uniform(8, 34), y + rng.uniform(-3, 3)),
            (103, 100, 94),
            1,
        )
    return image.filter(ImageFilter.GaussianBlur(0.3))


def textile(rng: random.Random) -> Image.Image:
    image = tinted_noise(rng, (111, 34, 31), 20, 11)
    draw = ImageDraw.Draw(image)
    gold = (179, 139, 58)
    for x in range(0, SIZE, 48):
        wrapped_line(draw, (x, 0), (x, SIZE), gold, 3)
    for y in range(0, SIZE, 48):
        wrapped_line(draw, (0, y), (SIZE, y), gold, 3)
    for x in range(24, SIZE, 48):
        for y in range(24, SIZE, 48):
            wrapped_ellipse(draw, (x, y), (6, 6), (191, 151, 66))
    return image.filter(ImageFilter.GaussianBlur(0.25))


def sacred_art_atlas(rng: random.Random) -> Image.Image:
    image = Image.new("RGB", (SIZE, SIZE), (92, 58, 34))
    draw = ImageDraw.Draw(image)
    cell = SIZE // 4
    robes = ((89, 35, 31), (36, 58, 83), (75, 52, 77), (43, 75, 57))
    for row in range(4):
        for column in range(4):
            left = column * cell
            top = row * cell
            inset = 8
            draw.rectangle(
                (left + inset, top + inset, left + cell - inset, top + cell - inset),
                fill=(130 + row * 6, 94 + column * 4, 39),
                outline=(225, 185, 73),
                width=6,
            )
            cx = left + cell // 2
            halo_y = top + 43
            draw.ellipse((cx - 30, halo_y - 30, cx + 30, halo_y + 30), fill=(211, 169, 58))
            draw.ellipse((cx - 18, halo_y - 20, cx + 18, halo_y + 21), fill=(177, 132, 91))
            robe = robes[(row + column) % len(robes)]
            draw.polygon(
                ((cx, top + 64), (left + 28, top + 116), (left + cell - 28, top + 116)),
                fill=robe,
            )
            draw.line((cx, top + 70, cx, top + 112), fill=(215, 179, 78), width=4)
            if (row + column) % 2 == 0:
                draw.line((cx - 13, top + 84, cx + 13, top + 84), fill=(215, 179, 78), width=3)
            # A few stable chips stop the atlas reading as pristine print.
            for _ in range(5):
                x = rng.randint(left + 12, left + cell - 12)
                y = rng.randint(top + 12, top + cell - 12)
                draw.rectangle((x, y, x + 2, y + 2), fill=(77, 52, 37))
    return image.filter(ImageFilter.GaussianBlur(0.35))


def mural_atlas(rng: random.Random) -> Image.Image:
    image = Image.new("RGB", (SIZE, SIZE), (107, 119, 116))
    draw = ImageDraw.Draw(image)
    cell_w = SIZE // 4
    cell_h = SIZE // 2
    for row in range(2):
        for column in range(4):
            x0 = column * cell_w
            y0 = row * cell_h
            draw.rectangle(
                (x0 + 5, y0 + 5, x0 + cell_w - 5, y0 + cell_h - 5),
                fill=(84 + row * 12, 106 + column * 4, 111 - row * 7),
                outline=(159, 137, 89),
                width=5,
            )
            cx = x0 + cell_w // 2
            draw.ellipse((cx - 34, y0 + 33, cx + 34, y0 + 101), fill=(172, 143, 73))
            draw.ellipse((cx - 20, y0 + 47, cx + 20, y0 + 91), fill=(160, 119, 89))
            draw.polygon(
                ((cx, y0 + 95), (x0 + 20, y0 + 230), (x0 + cell_w - 20, y0 + 230)),
                fill=((87, 42, 42) if column % 2 == 0 else (44, 63, 87)),
            )
            for _ in range(15):
                px = rng.randint(x0 + 8, x0 + cell_w - 8)
                py = rng.randint(y0 + 8, y0 + cell_h - 8)
                draw.point((px, py), fill=(70, 74, 69))
    return image.filter(ImageFilter.GaussianBlur(0.6))


def glass_atlas(_: random.Random) -> Image.Image:
    image = Image.new("RGB", (SIZE, SIZE), (42, 70, 77))
    draw = ImageDraw.Draw(image)
    panes = 8
    pane = SIZE // panes
    palette = ((66, 102, 109), (106, 63, 54), (108, 92, 48), (51, 82, 73))
    for y in range(panes):
        for x in range(panes):
            inset = 4
            draw.rectangle(
                (x * pane + inset, y * pane + inset,
                 (x + 1) * pane - inset, (y + 1) * pane - inset),
                fill=palette[(x + y * 3) % len(palette)],
            )
    for index in range(panes + 1):
        draw.line((index * pane, 0, index * pane, SIZE), fill=(24, 29, 28), width=6)
        draw.line((0, index * pane, SIZE, index * pane), fill=(24, 29, 28), width=6)
    return image.filter(ImageFilter.GaussianBlur(0.3))


BUILDERS = {
    "ChurchPlasterAlbedo.png": plaster,
    "ChurchStoneAlbedo.png": stone,
    "ChurchWoodAlbedo.png": wood,
    "ChurchMetalAlbedo.png": metal,
    "ChurchFloorAlbedo.png": floor,
    "ChurchTextileAlbedo.png": textile,
    "ChurchSacredArtAtlasAlbedo.png": sacred_art_atlas,
    "ChurchMuralAtlasAlbedo.png": mural_atlas,
    "ChurchGlassAtlasAlbedo.png": glass_atlas,
}

TILEABLE = {
    "ChurchPlasterAlbedo.png",
    "ChurchStoneAlbedo.png",
    "ChurchWoodAlbedo.png",
    "ChurchMetalAlbedo.png",
    "ChurchFloorAlbedo.png",
    "ChurchTextileAlbedo.png",
}


def seal_edges(image: Image.Image) -> Image.Image:
    """Make the sampled wrap boundary exact after non-wrapping blur passes."""
    pixels = image.load()
    for y in range(SIZE):
        pixels[SIZE - 1, y] = pixels[0, y]
    for x in range(SIZE):
        pixels[x, SIZE - 1] = pixels[x, 0]
    return image


def seam_error(image: Image.Image) -> int:
    pixels = image.load()
    horizontal = max(
        max(abs(a - b) for a, b in zip(pixels[0, y], pixels[SIZE - 1, y]))
        for y in range(SIZE)
    )
    vertical = max(
        max(abs(a - b) for a, b in zip(pixels[x, 0], pixels[x, SIZE - 1]))
        for x in range(SIZE)
    )
    return max(horizontal, vertical)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    print("CHURCH TEXTURES BUILD OK")
    for name, builder in BUILDERS.items():
        rng = random.Random(f"{SEED}:{name}")
        image = builder(rng).convert("RGB")
        if name in TILEABLE:
            image = seal_edges(image)
        if image.size != (SIZE, SIZE):
            raise RuntimeError(f"{name} lost its {SIZE} px contract")
        target = args.output / name
        image.save(target, "PNG", optimize=False)
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        if name in TILEABLE and seam_error(image) > 2:
            raise RuntimeError(f"{name} has an excessive wrapped-edge delta")
        print(f"  {name}: sha256 {digest[:16]}")
    print(f"  Output: {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
