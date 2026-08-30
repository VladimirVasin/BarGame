#!/usr/bin/env python3
"""Build deterministic authored albedos for the player-home exterior.

The sheets describe one restrained, weathered two-storey coastal house:
cool stucco, visibly different repairs, a dark brick plinth, old slate,
painted gallery joinery, dull metal, separate window frames and glass, and
worked concrete.  Every opaque sheet is a physically tiled material rather
than a facade-sized sample, so the model can keep stable texel density on
short rails, deep reveals and complete elevations alike.

No sheet contains text, a logo, a number or other readable branding.

Run from the repository root::

    python tools/build-player-home-exterior-textures.py --validate-only
    python tools/build-player-home-exterior-textures.py
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
    ROOT / "Assets" / "Resources" / "PlayerHome" / "ExteriorTextures"
)
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "PlayerHome" / "ExteriorTextures"
SHEET_SIZE = 1024
GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "player_home_exterior_surfaces_v1"


@dataclass(frozen=True)
class TextureSpec:
    sheet: str
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


def weld_repeat_edges(image: Image.Image) -> Image.Image:
    """Make opposite texels byte-identical for Unity Repeat sampling."""
    image = image.copy()
    pixels = image.load()
    width, height = image.size
    for y in range(height):
        pixels[width - 1, y] = pixels[0, y]
    for x in range(width):
        pixels[x, height - 1] = pixels[x, 0]
    return image


def periodic_noise(size: int, seed: int, cell: int = 48) -> Image.Image:
    rng = random.Random(seed)
    grid = max(4, math.ceil(size / cell))
    small = Image.new("L", (grid + 1, grid + 1))
    pixels = small.load()
    for y in range(grid):
        for x in range(grid):
            pixels[x, y] = rng.randrange(76, 181)
    for y in range(grid):
        pixels[grid, y] = pixels[0, y]
    for x in range(grid + 1):
        pixels[x, grid] = pixels[x, 0]
    return weld_repeat_edges(
        small.resize((size, size), Image.Resampling.BICUBIC)
    )


def colour_noise(
    size: int,
    seed: int,
    base: tuple[int, int, int],
    amplitude: float,
) -> Image.Image:
    rng = random.Random(seed)
    broad = periodic_noise(size, seed, 58)
    # A compact deterministic field is enough for sub-course grain.  Building
    # a million Python RNG samples per sheet made the nine-sheet validator
    # spend most of its time allocating values rather than checking art.
    fine_size = 257
    fine = Image.new("L", (fine_size, fine_size))
    fine.putdata([
        rng.randrange(94, 164) for _ in range(fine_size * fine_size)
    ])
    fine = fine.resize((size, size), Image.Resampling.BILINEAR)
    fine = fine.filter(ImageFilter.GaussianBlur(0.42))
    field = ImageChops.blend(broad, fine, 0.24)
    channels = []
    for channel in base:
        lookup = [
            clamp_byte(
                channel * (1.0 + ((value - 128.0) / 128.0) * amplitude)
            )
            for value in range(256)
        ]
        channels.append(field.point(lookup))
    image = Image.merge("RGB", tuple(channels))
    return weld_repeat_edges(image)


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
            draw.line(
                (x0 + dx, y0 + dy, x1 + dx, y1 + dy),
                fill=fill,
                width=width,
            )


def wrapped_rectangle(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int, int, int],
    *,
    outline: tuple[int, int, int],
    width: int,
    size: int,
) -> None:
    x0, y0, x1, y1 = xy
    for dx in (-size, 0, size):
        for dy in (-size, 0, size):
            draw.rectangle(
                (x0 + dx, y0 + dy, x1 + dx, y1 + dy),
                outline=outline,
                width=width,
            )


def build_stucco_primary(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (125, 128, 115), 0.18)
    draw = ImageDraw.Draw(image)
    for _ in range(54):
        x = rng.randrange(size)
        y = rng.randrange(size)
        length = rng.randrange(34, 190)
        tone = rng.choice(((101, 106, 98), (145, 144, 126), (91, 99, 93)))
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(-8, 9), y + length),
            tone,
            rng.randrange(1, 5),
            size,
        )
    for _ in range(28):
        x = rng.randrange(size)
        y = rng.randrange(size)
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(18, 110), y + rng.randrange(-9, 10)),
            (86, 91, 85),
            1,
            size,
        )
    return weld_repeat_edges(image)


def build_stucco_repair(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (147, 143, 127), 0.15)
    draw = ImageDraw.Draw(image)
    for _ in range(72):
        x = rng.randrange(size)
        y = rng.randrange(size)
        length = rng.randrange(28, 128)
        tone = rng.choice(((126, 123, 111), (164, 158, 138), (113, 116, 107)))
        wrapped_line(
            draw,
            (x, y, x + length, y + rng.randrange(-5, 6)),
            tone,
            rng.randrange(1, 4),
            size,
        )
    for _ in range(10):
        x = rng.randrange(size)
        y = rng.randrange(size)
        width = rng.randrange(70, 210)
        height = rng.randrange(45, 140)
        wrapped_rectangle(
            draw,
            (x, y, x + width, y + height),
            outline=(108, 107, 98),
            width=2,
            size=size,
        )
    return weld_repeat_edges(image)


def build_brick_plinth(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (72, 50, 43), 0.22)
    draw = ImageDraw.Draw(image)
    course = 92
    brick = 184
    mortar = 7
    for row, y in enumerate(range(0, size + course, course)):
        wrapped_line(draw, (0, y, size, y), (47, 48, 44), mortar, size)
        offset = -(brick // 2) if row % 2 else 0
        for x in range(offset, size + brick, brick):
            wrapped_line(
                draw,
                (x, y, x, y + course),
                (45, 44, 41),
                mortar,
                size,
            )
            if rng.random() < 0.42:
                wrapped_rectangle(
                    draw,
                    (x + 10, y + 10, x + brick - 8, y + course - 7),
                    outline=rng.choice(
                        ((91, 54, 43), (59, 46, 43), (84, 60, 48))
                    ),
                    width=3,
                    size=size,
                )
    return weld_repeat_edges(image)


def build_roof_slate(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (48, 55, 58), 0.16)
    draw = ImageDraw.Draw(image)
    course = 84
    slate = 116
    for row, y in enumerate(range(0, size + course, course)):
        wrapped_line(draw, (0, y, size, y), (28, 34, 37), 5, size)
        offset = -(slate // 2) if row % 2 else 0
        for x in range(offset, size + slate, slate):
            wrapped_line(
                draw,
                (x, y, x + rng.randrange(-2, 3), y + course),
                (35, 41, 44),
                3,
                size,
            )
    for _ in range(38):
        x = rng.randrange(size)
        y = rng.randrange(size)
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(16, 82), y),
            rng.choice(((67, 72, 72), (34, 43, 45), (76, 67, 57))),
            rng.randrange(1, 3),
            size,
        )
    return weld_repeat_edges(image)


def build_painted_wood(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (75, 96, 87), 0.14)
    draw = ImageDraw.Draw(image)
    for _ in range(96):
        x = rng.randrange(size)
        bend = rng.randrange(-18, 19)
        tone = rng.choice(((54, 72, 67), (101, 114, 99), (59, 79, 72)))
        wrapped_line(draw, (x, 0, x + bend, size), tone, rng.randrange(1, 4), size)
    for _ in range(28):
        x = rng.randrange(size)
        y = rng.randrange(size)
        length = rng.randrange(18, 100)
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(-3, 4), y + length),
            (121, 114, 91),
            rng.randrange(2, 5),
            size,
        )
    return weld_repeat_edges(image)


def build_painted_metal(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (78, 88, 84), 0.12)
    draw = ImageDraw.Draw(image)
    for _ in range(58):
        x = rng.randrange(size)
        y = rng.randrange(size)
        length = rng.randrange(24, 210)
        wrapped_line(
            draw,
            (x, y, x + length, y + rng.randrange(-3, 4)),
            rng.choice(((55, 63, 62), (112, 111, 96), (104, 70, 50))),
            rng.randrange(1, 4),
            size,
        )
    for _ in range(14):
        x = rng.randrange(size)
        y = rng.randrange(size)
        radius = rng.randrange(16, 58)
        wrapped_rectangle(
            draw,
            (x - radius, y - radius, x + radius, y + radius),
            outline=(101, 68, 49),
            width=3,
            size=size,
        )
    return weld_repeat_edges(image)


def build_window_frame(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (166, 164, 145), 0.12)
    draw = ImageDraw.Draw(image)
    for _ in range(70):
        x = rng.randrange(size)
        y = rng.randrange(size)
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(20, 180), y + rng.randrange(-2, 3)),
            rng.choice(((137, 137, 124), (184, 178, 151), (112, 126, 119))),
            rng.randrange(1, 3),
            size,
        )
    for _ in range(18):
        x = rng.randrange(size)
        y = rng.randrange(size)
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(-4, 5), y + rng.randrange(28, 130)),
            (99, 108, 103),
            2,
            size,
        )
    return weld_repeat_edges(image)


def build_window_glass(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (56, 73, 77), 0.18)
    draw = ImageDraw.Draw(image)
    for _ in range(32):
        x = rng.randrange(size)
        y = rng.randrange(size)
        length = rng.randrange(90, 360)
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(-14, 15), y + length),
            rng.choice(((82, 100, 101), (39, 57, 63), (111, 112, 99))),
            rng.randrange(2, 7),
            size,
        )
    for offset in (-size, 0, size):
        draw.line(
            (70 + offset, 0, 410 + offset, size),
            fill=(118, 134, 130),
            width=7,
        )
    return weld_repeat_edges(image.filter(ImageFilter.GaussianBlur(0.35)))


def build_concrete(seed: int) -> Image.Image:
    size = SHEET_SIZE
    rng = random.Random(seed)
    image = colour_noise(size, seed, (105, 108, 103), 0.20)
    draw = ImageDraw.Draw(image)
    for _ in range(190):
        x = rng.randrange(size)
        y = rng.randrange(size)
        radius = rng.randrange(1, 5)
        tone = rng.choice(((70, 74, 72), (132, 132, 122), (91, 87, 81)))
        for dx in (-size, 0, size):
            for dy in (-size, 0, size):
                draw.ellipse(
                    (x - radius + dx, y - radius + dy,
                     x + radius + dx, y + radius + dy),
                    fill=tone,
                )
    for _ in range(20):
        x = rng.randrange(size)
        y = rng.randrange(size)
        wrapped_line(
            draw,
            (x, y, x + rng.randrange(35, 150), y + rng.randrange(-9, 10)),
            (66, 70, 68),
            1,
            size,
        )
    return weld_repeat_edges(image)


SPECS: tuple[TextureSpec, ...] = (
    TextureSpec(
        "StuccoPrimary", "PlayerHomeExteriorStuccoPrimaryAlbedo",
        0x50485350, "Repeat", 2.4, 0.06, 0.0, build_stucco_primary),
    TextureSpec(
        "StuccoRepair", "PlayerHomeExteriorStuccoRepairAlbedo",
        0x50485352, "Repeat", 1.8, 0.05, 0.0, build_stucco_repair),
    TextureSpec(
        "BrickPlinth", "PlayerHomeExteriorBrickPlinthAlbedo",
        0x50484250, "Repeat", 1.2, 0.07, 0.0, build_brick_plinth),
    TextureSpec(
        "RoofSlate", "PlayerHomeExteriorRoofSlateAlbedo",
        0x50485253, "Repeat", 2.4, 0.12, 0.0, build_roof_slate),
    TextureSpec(
        "PaintedWood", "PlayerHomeExteriorPaintedWoodAlbedo",
        0x50485057, "Repeat", 1.0, 0.14, 0.0, build_painted_wood),
    TextureSpec(
        "PaintedMetal", "PlayerHomeExteriorPaintedMetalAlbedo",
        0x5048504D, "Repeat", 1.2, 0.22, 0.24, build_painted_metal),
    TextureSpec(
        "WindowFrame", "PlayerHomeExteriorWindowFrameAlbedo",
        0x50485746, "Repeat", 0.8, 0.13, 0.0, build_window_frame),
    TextureSpec(
        "WindowGlass", "PlayerHomeExteriorWindowGlassAlbedo",
        0x50485747, "Repeat", 1.0, 0.34, 0.0, build_window_glass),
    TextureSpec(
        "Concrete", "PlayerHomeExteriorConcreteAlbedo",
        0x5048434F, "Repeat", 1.5, 0.08, 0.0, build_concrete),
)


def build_all() -> list[tuple[TextureSpec, Image.Image]]:
    return [
        (spec, spec.builder(spec.seed).convert("RGB"))
        for spec in SPECS
    ]


def validate(built: list[tuple[TextureSpec, Image.Image]]) -> None:
    repeated = build_all()
    repeat_hashes = {
        spec.basename: sha256_image(image) for spec, image in repeated
    }
    problems: list[str] = []
    expected_names = {
        "PlayerHomeExteriorStuccoPrimaryAlbedo",
        "PlayerHomeExteriorStuccoRepairAlbedo",
        "PlayerHomeExteriorBrickPlinthAlbedo",
        "PlayerHomeExteriorRoofSlateAlbedo",
        "PlayerHomeExteriorPaintedWoodAlbedo",
        "PlayerHomeExteriorPaintedMetalAlbedo",
        "PlayerHomeExteriorWindowFrameAlbedo",
        "PlayerHomeExteriorWindowGlassAlbedo",
        "PlayerHomeExteriorConcreteAlbedo",
    }
    if {spec.basename for spec, _ in built} != expected_names:
        problems.append("runtime texture basenames drifted from the home contract")
    if len({spec.sheet for spec, _ in built}) != len(built):
        problems.append("semantic sheet names are not unique")
    for spec, image in built:
        if image.size != (SHEET_SIZE, SHEET_SIZE):
            problems.append(
                f"{spec.basename} is {image.size}, expected {SHEET_SIZE} square"
            )
        if image.mode != "RGB":
            problems.append(f"{spec.basename} is {image.mode}, expected RGB")
        if sha256_image(image) != repeat_hashes[spec.basename]:
            problems.append(f"{spec.basename} is not deterministic")
        extrema = image.convert("L").getextrema()
        if extrema[1] - extrema[0] < 18:
            problems.append(f"{spec.basename} has no useful material contrast")
        if spec.wrap != "Repeat":
            problems.append(f"{spec.basename} must use physically tiled Repeat UVs")
        for y in range(SHEET_SIZE):
            if image.getpixel((0, y)) != image.getpixel((SHEET_SIZE - 1, y)):
                problems.append(f"{spec.basename} has an X seam at row {y}")
                break
        for x in range(SHEET_SIZE):
            if image.getpixel((x, 0)) != image.getpixel((x, SHEET_SIZE - 1)):
                problems.append(f"{spec.basename} has a Y seam at column {x}")
                break
    if problems:
        raise SystemExit(
            "Player-home exterior texture validation failed:\n  "
            + "\n  ".join(problems)
        )


def write_contact_sheet(
    built: list[tuple[TextureSpec, Image.Image]],
    path: Path,
) -> None:
    thumb = 300
    margin = 28
    label_height = 38
    columns = 3
    rows = 3
    sheet = Image.new(
        "RGB",
        (
            margin * (columns + 1) + thumb * columns,
            margin * (rows + 1) + (thumb + label_height) * rows,
        ),
        (27, 31, 31),
    )
    draw = ImageDraw.Draw(sheet)
    for index, (spec, image) in enumerate(built):
        column = index % columns
        row = index // columns
        x = margin + column * (thumb + margin)
        y = margin + row * (thumb + label_height + margin)
        sheet.paste(
            image.resize((thumb, thumb), Image.Resampling.LANCZOS),
            (x, y),
        )
        draw.text((x, y + thumb + 9), spec.sheet, fill=(220, 216, 198))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path, optimize=True)


def write_outputs(
    built: list[tuple[TextureSpec, Image.Image]],
    texture_dir: Path,
    art_source: Path,
) -> None:
    texture_dir.mkdir(parents=True, exist_ok=True)
    art_source.mkdir(parents=True, exist_ok=True)
    records: list[dict] = []
    for spec, image in built:
        path = texture_dir / f"{spec.basename}.png"
        image.save(path, optimize=True)
        records.append({
            "sheet": spec.sheet,
            "basename": spec.basename,
            "resource_path": (
                f"PlayerHome/ExteriorTextures/{spec.basename}"
            ),
            "asset_path": str(path.relative_to(ROOT)).replace("\\", "/"),
            "size": [SHEET_SIZE, SHEET_SIZE],
            "wrap": spec.wrap,
            "meters_per_tile": spec.meters_per_tile,
            "smoothness": spec.smoothness,
            "metallic": spec.metallic,
            "sha256_rgb": sha256_image(image),
        })
    payload = {
        "generator": "tools/build-player-home-exterior-textures.py",
        "generator_version": GENERATOR_VERSION,
        "design_id": DESIGN_ID,
        "authored_text": [],
        "brand_marks": False,
        "uv_contract": "world_metre_projected_repeat",
        "textures": records,
    }
    (art_source / "player-home-exterior-textures.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_contact_sheet(
        built,
        art_source / "player-home-exterior-textures-contact-sheet.png",
    )


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
        "".join(
            f"{spec.basename}:{sha256_image(image)}"
            for spec, image in built
        ).encode("utf-8")
    ).hexdigest()
    if args.validate_only:
        print(
            "PLAYER-HOME EXTERIOR TEXTURES VALID: "
            f"{len(built)} sheets, signature {signature[:16]}"
        )
        return 0
    write_outputs(built, args.textures, args.art_source)
    print(
        "Player-home exterior textures written: "
        f"{len(built)} sheets, signature {signature[:16]}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
