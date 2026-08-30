#!/usr/bin/env python3
"""Build the supermarket exterior's deterministic authored albedos.

The convenience-store reference is used only for the broad storefront grammar:
a low fascia, deep green and aged warm accent bands, brick piers and a glassy
front.  This is an original neighbourhood shop, so the sheets contain no logo,
number, price, slogan or other readable brand material.

The wall and fascia sheets are four-side atlases.  Their quadrants are, from
top-left clockwise, front, right, left and rear; the model generator maps each
side into its own quadrant.  Brick and painted metal are seamless physical
materials.  Unity imports the four PNGs from
``Resources/Supermarket/ExteriorTextures`` and applies them to authored UV0.

Run from the repository root::

    python tools/build-supermarket-exterior-textures.py --validate-only
    python tools/build-supermarket-exterior-textures.py
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PIL import Image, ImageChops, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = (
    ROOT / "Assets" / "Resources" / "Supermarket" / "ExteriorTextures"
)
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "Supermarket" / "ExteriorTextures"
SHEET_SIZE = 1024
QUADRANT = SHEET_SIZE // 2
GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "supermarket_exterior_surfaces_v1"


@dataclass(frozen=True)
class TextureSpec:
    basename: str
    seed: int
    wrap: str
    meters_per_tile: float
    smoothness: float
    metallic: float
    builder: Callable[[int], Image.Image]


def clamp_byte(value: float) -> int:
    return max(0, min(255, int(round(value))))


def sha256_image(image: Image.Image) -> str:
    return hashlib.sha256(image.convert("RGB").tobytes()).hexdigest()


def periodic_noise(size: int, seed: int, cell: int = 48) -> Image.Image:
    """Return low-frequency repeatable noise with welded opposite edges."""
    rng = random.Random(seed)
    grid = max(4, math.ceil(size / cell))
    small = Image.new("L", (grid + 1, grid + 1))
    pixels = small.load()
    for y in range(grid):
        for x in range(grid):
            pixels[x, y] = rng.randrange(78, 178)
    for y in range(grid):
        pixels[grid, y] = pixels[0, y]
    for x in range(grid + 1):
        pixels[x, grid] = pixels[x, 0]
    noise = small.resize((size, size), Image.Resampling.BICUBIC)
    return weld_repeat_edges(noise)


def weld_repeat_edges(image: Image.Image) -> Image.Image:
    """Make opposite edge texels byte-identical for Repeat import mode."""
    image = image.copy()
    pixels = image.load()
    width, height = image.size
    for y in range(height):
        pixels[width - 1, y] = pixels[0, y]
    for x in range(width):
        pixels[x, height - 1] = pixels[x, 0]
    return image


def colour_noise(
    size: int,
    seed: int,
    base: tuple[int, int, int],
    amplitude: float,
    *,
    periodic: bool,
) -> Image.Image:
    rng = random.Random(seed)
    low = periodic_noise(size, seed, 56) if periodic else Image.new("L", (size, size))
    if not periodic:
        tiny = Image.new("L", (33, 33))
        tiny.putdata([rng.randrange(72, 184) for _ in range(33 * 33)])
        low = tiny.resize((size, size), Image.Resampling.BICUBIC)
    fine = Image.new("L", (size, size))
    fine.putdata([rng.randrange(96, 161) for _ in range(size * size)])
    fine = fine.filter(ImageFilter.GaussianBlur(0.45))
    field = ImageChops.blend(low, fine, 0.27)
    source = field.load()
    image = Image.new("RGB", (size, size))
    target = image.load()
    for y in range(size):
        for x in range(size):
            factor = 1.0 + ((source[x, y] - 128.0) / 128.0) * amplitude
            target[x, y] = tuple(clamp_byte(channel * factor) for channel in base)
    return weld_repeat_edges(image) if periodic else image


def wrapped_line(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int, int, int],
    fill: tuple[int, int, int],
    width: int,
    size: int,
) -> None:
    x0, y0, x1, y1 = xy
    for dx in (-size, 0, size):
        for dy in (-size, 0, size):
            draw.line((x0 + dx, y0 + dy, x1 + dx, y1 + dy), fill=fill, width=width)


def build_brick(seed: int) -> Image.Image:
    """Dark old brick with wrapped courses and quiet mortar variation."""
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (77, 52, 44), 0.22, periodic=True)
    draw = ImageDraw.Draw(image)
    course = 92
    mortar = 8
    for row, y0 in enumerate(range(0, size + course, course)):
        wrapped_line(draw, (0, y0, size, y0), (50, 48, 43), mortar, size)
        offset = -(course // 2) if row % 2 else 0
        for x0 in range(offset, size + course, course):
            wrapped_line(
                draw,
                (x0, y0, x0, y0 + course),
                (48, 45, 41),
                mortar,
                size,
            )
            if rng.random() < 0.34:
                shade = rng.choice(((91, 54, 43), (64, 47, 43), (86, 59, 48)))
                x1 = x0 + mortar + 3
                y1 = y0 + mortar + 3
                x2 = x0 + course - 3
                y2 = y0 + course - 3
                for dx in (-size, 0, size):
                    for dy in (-size, 0, size):
                        draw.rectangle((x1 + dx, y1 + dy, x2 + dx, y2 + dy), outline=shade, width=3)
    for _ in range(30):
        x = rng.randrange(size)
        y = rng.randrange(size)
        length = rng.randrange(18, 80)
        wrapped_line(draw, (x, y, x + length, y + rng.randrange(-5, 6)), (45, 39, 37), 2, size)
    return weld_repeat_edges(image)


def build_metal(seed: int) -> Image.Image:
    """Painted municipal metal: matte, rubbed and lightly oxidised."""
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (106, 111, 103), 0.12, periodic=True)
    draw = ImageDraw.Draw(image)
    for _ in range(46):
        x = rng.randrange(size)
        y = rng.randrange(size)
        length = rng.randrange(24, 220)
        tone = rng.choice(((78, 83, 79), (133, 132, 117), (86, 73, 62)))
        wrapped_line(draw, (x, y, x + length, y + rng.randrange(-3, 4)), tone, rng.randrange(1, 4), size)
    for _ in range(11):
        x = rng.randrange(size)
        y = rng.randrange(size)
        radius = rng.randrange(18, 70)
        for dx in (-size, 0, size):
            for dy in (-size, 0, size):
                draw.ellipse(
                    (x - radius + dx, y - radius + dy, x + radius + dx, y + radius + dy),
                    outline=(103, 75, 55),
                    width=3,
                )
    return weld_repeat_edges(image)


def wall_tile(seed: int, side_index: int) -> Image.Image:
    rng = random.Random(seed + side_index * 1709)
    bases = ((151, 148, 130), (143, 145, 131), (136, 138, 126), (146, 142, 127))
    tile = colour_noise(QUADRANT, seed + side_index * 29, bases[side_index], 0.16, periodic=False)
    draw = ImageDraw.Draw(tile, "RGBA")

    # Rain and road grime remain broad material events, not decals.
    for y in range(QUADRANT):
        ground = max(0.0, 1.0 - y / (QUADRANT * 0.28))
        if ground > 0:
            draw.line((0, QUADRANT - 1 - y, QUADRANT, QUADRANT - 1 - y), fill=(40, 45, 40, int(31 * ground)))
    for _ in range(18):
        x = rng.randrange(QUADRANT)
        length = rng.randrange(38, 190)
        draw.line((x, 0, x + rng.randrange(-10, 11), length), fill=(65, 70, 63, rng.randrange(10, 27)), width=rng.randrange(2, 8))

    # One differently-aged repair per elevation makes the atlas genuinely
    # side-specific while staying non-readable and non-coplanar.
    patch_x = (74, 292, 194, 336)[side_index]
    patch_y = (238, 174, 282, 226)[side_index]
    patch_w = (118, 92, 146, 105)[side_index]
    patch_h = (126, 174, 95, 132)[side_index]
    patch = tuple(clamp_byte(channel * (0.91 + side_index * 0.025)) for channel in bases[side_index])
    draw.rectangle((patch_x, patch_y, patch_x + patch_w, patch_y + patch_h), fill=(*patch, 175))
    draw.line((patch_x, patch_y, patch_x + patch_w, patch_y), fill=(86, 86, 76, 80), width=3)
    return tile


def build_wall_atlas(seed: int) -> Image.Image:
    atlas = Image.new("RGB", (SHEET_SIZE, SHEET_SIZE), (140, 140, 128))
    # UV quadrants: front TL, right TR, rear BL, left BR.
    for index, origin in enumerate(((0, 0), (QUADRANT, 0), (0, QUADRANT), (QUADRANT, QUADRANT))):
        atlas.paste(wall_tile(seed, index), origin)
    return atlas


def unity_y_to_pixel(height: float) -> int:
    return clamp_byte(255.0 * (1.0 - height / 6.4)) * 2


def fascia_tile(seed: int, side_index: int) -> Image.Image:
    rng = random.Random(seed + side_index * 2861)
    tile = colour_noise(QUADRANT, seed + side_index * 43, (166, 160, 137), 0.09, periodic=False)
    draw = ImageDraw.Draw(tile, "RGBA")
    bands = (
        (5.55, 5.32, (151, 89, 39, 235)),       # faded ochre
        (5.27, 4.72, (35, 80, 68, 238)),        # broad bottle green
        (4.66, 4.40, (100, 34, 43, 236)),       # restrained oxblood
    )
    for top, bottom, colour in bands:
        y0 = unity_y_to_pixel(top)
        y1 = unity_y_to_pixel(bottom)
        draw.rectangle((0, y0, QUADRANT, y1), fill=colour)
        for _ in range(45):
            x = rng.randrange(QUADRANT)
            y = rng.randrange(max(1, y0), min(QUADRANT, y1 + 1))
            length = rng.randrange(6, 54)
            draw.line((x, y, min(QUADRANT, x + length), y), fill=(202, 195, 169, rng.randrange(10, 34)), width=rng.randrange(1, 4))

    # The motif wraps, but differently faded/repaired panels interrupt the
    # corporate rhythm on every elevation.
    panel_x = (352, 74, 254, 154)[side_index]
    panel_width = (96, 112, 84, 132)[side_index]
    draw.rectangle(
        (panel_x, unity_y_to_pixel(5.31), panel_x + panel_width, unity_y_to_pixel(4.69)),
        fill=(43 + side_index * 3, 77 + side_index * 4, 69 + side_index * 2, 214),
    )
    draw.line((panel_x, unity_y_to_pixel(5.60), panel_x, unity_y_to_pixel(4.37)), fill=(115, 108, 93, 125), width=3)
    for _ in range(10):
        x = rng.randrange(QUADRANT)
        y = rng.randrange(unity_y_to_pixel(5.68), unity_y_to_pixel(4.34))
        draw.line((x, y, x + rng.randrange(-4, 5), y + rng.randrange(18, 70)), fill=(67, 69, 62, 42), width=rng.randrange(2, 6))
    return tile


def build_fascia_atlas(seed: int) -> Image.Image:
    atlas = Image.new("RGB", (SHEET_SIZE, SHEET_SIZE), (164, 159, 137))
    for index, origin in enumerate(((0, 0), (QUADRANT, 0), (0, QUADRANT), (QUADRANT, QUADRANT))):
        atlas.paste(fascia_tile(seed, index), origin)
    return atlas


SPECS: tuple[TextureSpec, ...] = (
    TextureSpec("SupermarketExteriorWallAtlas", 0x5357414C, "Clamp", 15.5, 0.05, 0.0, build_wall_atlas),
    TextureSpec("SupermarketExteriorFasciaAtlas", 0x53464153, "Clamp", 15.5, 0.12, 0.0, build_fascia_atlas),
    TextureSpec("SupermarketExteriorBrickAlbedo", 0x5342524B, "Repeat", 1.2, 0.08, 0.0, build_brick),
    TextureSpec("SupermarketExteriorMetalAlbedo", 0x534D4554, "Repeat", 1.35, 0.24, 0.28, build_metal),
)


def build_all() -> list[tuple[TextureSpec, Image.Image]]:
    return [(spec, spec.builder(spec.seed).convert("RGB")) for spec in SPECS]


def validate(built: list[tuple[TextureSpec, Image.Image]]) -> None:
    repeated = build_all()
    repeat_hashes = {spec.basename: sha256_image(image) for spec, image in repeated}
    problems: list[str] = []
    for spec, image in built:
        if image.size != (SHEET_SIZE, SHEET_SIZE):
            problems.append(f"{spec.basename} is {image.size}, expected {SHEET_SIZE} square")
        if image.mode != "RGB":
            problems.append(f"{spec.basename} is {image.mode}, expected RGB")
        if sha256_image(image) != repeat_hashes[spec.basename]:
            problems.append(f"{spec.basename} is not deterministic")
        extrema = image.convert("L").getextrema()
        if extrema[1] - extrema[0] < 18:
            problems.append(f"{spec.basename} has no useful material contrast")
        if spec.wrap == "Repeat":
            for y in range(SHEET_SIZE):
                if image.getpixel((0, y)) != image.getpixel((SHEET_SIZE - 1, y)):
                    problems.append(f"{spec.basename} has an X seam at row {y}")
                    break
            for x in range(SHEET_SIZE):
                if image.getpixel((x, 0)) != image.getpixel((x, SHEET_SIZE - 1)):
                    problems.append(f"{spec.basename} has a Y seam at column {x}")
                    break
    if problems:
        raise SystemExit("Supermarket exterior texture validation failed:\n  " + "\n  ".join(problems))


def write_contact_sheet(built: list[tuple[TextureSpec, Image.Image]], path: Path) -> None:
    thumb = 440
    margin = 36
    label_height = 42
    sheet = Image.new("RGB", (margin * 3 + thumb * 2, margin * 3 + (thumb + label_height) * 2), (28, 31, 30))
    draw = ImageDraw.Draw(sheet)
    for index, (spec, image) in enumerate(built):
        column = index % 2
        row = index // 2
        x = margin + column * (thumb + margin)
        y = margin + row * (thumb + label_height + margin)
        sheet.paste(image.resize((thumb, thumb), Image.Resampling.LANCZOS), (x, y))
        draw.text((x, y + thumb + 10), spec.basename, fill=(220, 216, 198))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path, optimize=True)


def write_outputs(built: list[tuple[TextureSpec, Image.Image]], texture_dir: Path, art_source: Path) -> None:
    texture_dir.mkdir(parents=True, exist_ok=True)
    art_source.mkdir(parents=True, exist_ok=True)
    records: list[dict] = []
    for spec, image in built:
        path = texture_dir / f"{spec.basename}.png"
        image.save(path, optimize=True)
        records.append({
            "basename": spec.basename,
            "resource_path": f"Supermarket/ExteriorTextures/{spec.basename}",
            "asset_path": str(path.relative_to(ROOT)).replace("\\", "/"),
            "size": [SHEET_SIZE, SHEET_SIZE],
            "wrap": spec.wrap,
            "meters_per_tile": spec.meters_per_tile,
            "smoothness": spec.smoothness,
            "metallic": spec.metallic,
            "sha256_rgb": sha256_image(image),
        })
    payload = {
        "generator": "tools/build-supermarket-exterior-textures.py",
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "brand_marks": False,
        "atlas_quadrants": {
            "top_left": "front",
            "top_right": "right",
            "bottom_left": "rear",
            "bottom_right": "left",
        },
        "textures": records,
    }
    (art_source / "supermarket-exterior-textures.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_contact_sheet(built, art_source / "supermarket-exterior-textures-contact-sheet.png")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--textures", type=Path, default=DEFAULT_TEXTURE_DIR)
    parser.add_argument("--art-source", type=Path, default=DEFAULT_ART_SOURCE)
    parser.add_argument("--validate-only", "--verify", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    built = build_all()
    validate(built)
    signature = hashlib.sha256(
        "".join(f"{spec.basename}:{sha256_image(image)}" for spec, image in built).encode("utf-8")
    ).hexdigest()
    if args.validate_only:
        print(f"SUPERMARKET EXTERIOR TEXTURES VALID: {len(built)} sheets, signature {signature[:16]}")
        return 0
    write_outputs(built, args.textures, args.art_source)
    print(f"Supermarket exterior textures written: {len(built)} sheets, signature {signature[:16]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
