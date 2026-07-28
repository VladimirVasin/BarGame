#!/usr/bin/env python3
"""Build deterministic pixel art for the Split the G minigame.

The scene is authored at 320x180 and the atlas at 256x256, then enlarged
with nearest-neighbour sampling. This keeps every final pixel aligned to the
project's 640x360 retro canvas while making the source easy to tune.

Pillow is the only dependency.
"""

from __future__ import annotations

import argparse
import hashlib
import random
from pathlib import Path
from typing import Callable

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_BACKGROUND = (
    ROOT / "Assets" / "Resources" / "SplitTheG" /
    "SplitTheGBackground.png"
)
DEFAULT_ATLAS = (
    ROOT / "Assets" / "Resources" / "SplitTheG" /
    "SplitTheGAtlas.png"
)

BACKGROUND_SOURCE_SIZE = (320, 180)
BACKGROUND_OUTPUT_SIZE = (640, 360)
ATLAS_COLUMNS = 4
ATLAS_ROWS = 4
CELL_SOURCE_SIZE = 64
ATLAS_OUTPUT_SIZE = 512
TRANSPARENT = (0, 0, 0, 0)


def rect(
    draw: ImageDraw.ImageDraw,
    bounds: tuple[int, int, int, int],
    color: tuple[int, int, int, int],
) -> None:
    draw.rectangle(bounds, fill=color)


def line(
    draw: ImageDraw.ImageDraw,
    points: list[tuple[int, int]],
    color: tuple[int, int, int, int],
    width: int = 1,
) -> None:
    draw.line(points, fill=color, width=width)


def build_background() -> Image.Image:
    image = Image.new("RGBA", BACKGROUND_SOURCE_SIZE, (15, 8, 15, 255))
    draw = ImageDraw.Draw(image)
    rng = random.Random(0x53504C495447)

    # Stepped wall gradient and deep ceiling.
    wall_bands = (
        (0, 20, (18, 10, 19, 255)),
        (20, 42, (35, 14, 27, 255)),
        (42, 66, (48, 18, 31, 255)),
        (66, 92, (55, 20, 31, 255)),
        (92, 118, (39, 17, 26, 255)),
    )
    for y_min, y_max, color in wall_bands:
        rect(draw, (0, y_min, 319, y_max), color)
    rect(draw, (0, 0, 319, 8), (7, 5, 10, 255))
    rect(draw, (0, 9, 319, 12), (28, 13, 23, 255))
    rect(draw, (0, 14, 319, 16), (10, 7, 12, 255))

    # Pixel brickwork, deliberately irregular but reproducible.
    brick_dark = (28, 12, 21, 255)
    brick_light = (76, 28, 37, 255)
    for y in range(23, 92, 9):
        offset = 0 if (y // 9) % 2 == 0 else 11
        line(draw, [(0, y), (319, y)], brick_dark)
        for x in range(-offset, 321, 22):
            line(draw, [(x, y), (x, y + 8)], brick_dark)
            if 0 <= x + 2 < 320 and rng.randrange(3) == 0:
                rect(draw, (x + 2, y + 2, x + 3, y + 3), brick_light)

    # Central mirror alcove leaves a calm, high-contrast stage for the glass.
    rect(draw, (97, 24, 222, 108), (12, 8, 15, 255))
    rect(draw, (100, 27, 219, 105), (90, 50, 39, 255))
    rect(draw, (104, 31, 215, 101), (17, 28, 33, 255))
    rect(draw, (108, 35, 211, 97), (20, 39, 43, 255))
    for y in range(37, 97, 6):
        shade = 25 + ((y // 6) % 3) * 4
        rect(draw, (109, y, 210, y + 2), (shade, 48, 49, 255))
    rect(draw, (108, 83, 211, 97), (11, 19, 23, 255))
    line(draw, [(109, 95), (210, 95)], (139, 74, 43, 255), 2)
    rect(draw, (101, 27, 104, 105), (126, 66, 42, 255))
    rect(draw, (215, 27, 218, 105), (85, 39, 31, 255))
    rect(draw, (100, 27, 219, 29), (180, 102, 53, 255))

    # Bottle shelves flank the play area.
    for shelf_x in (8, 235):
        rect(draw, (shelf_x, 28, shelf_x + 76, 88), (10, 7, 12, 255))
        rect(draw, (shelf_x + 2, 30, shelf_x + 74, 86), (31, 14, 22, 255))
        for shelf_y in (48, 68, 86):
            rect(
                draw,
                (shelf_x, shelf_y, shelf_x + 76, shelf_y + 2),
                (129, 67, 36, 255),
            )
            rect(
                draw,
                (shelf_x + 2, shelf_y + 3, shelf_x + 74, shelf_y + 4),
                (52, 24, 24, 255),
            )
        bottle_colors = (
            (90, 100, 50, 255),
            (123, 53, 31, 255),
            (53, 77, 91, 255),
            (117, 92, 45, 255),
            (70, 43, 67, 255),
        )
        for row, shelf_y in enumerate((48, 68, 86)):
            base_y = shelf_y - 2
            for bottle in range(7):
                x = shelf_x + 6 + bottle * 10 + (row % 2)
                height = 10 + ((bottle + row * 2) % 5)
                color = bottle_colors[(bottle + row) % len(bottle_colors)]
                rect(draw, (x, base_y - height, x + 5, base_y), color)
                rect(draw, (x + 1, base_y - height - 4, x + 4, base_y - height), color)
                rect(
                    draw,
                    (x + 1, base_y - height + 3, x + 2, base_y - 2),
                    (190, 135, 70, 150),
                )

    # Amber wall lamps and their hard pixel halos.
    for lamp_x in (91, 228):
        rect(draw, (lamp_x - 6, 45, lamp_x + 6, 62), (54, 24, 23, 255))
        rect(draw, (lamp_x - 4, 47, lamp_x + 4, 59), (181, 87, 35, 255))
        rect(draw, (lamp_x - 2, 49, lamp_x + 2, 57), (255, 191, 79, 255))
        rect(draw, (lamp_x - 8, 51, lamp_x + 8, 55), (176, 76, 35, 50))

    # Hanging light above the pint.
    rect(draw, (158, 4, 161, 16), (50, 27, 29, 255))
    draw.polygon(
        [(147, 17), (172, 17), (178, 27), (141, 27)],
        fill=(38, 18, 22, 255),
    )
    rect(draw, (146, 24, 173, 28), (104, 52, 33, 255))
    rect(draw, (151, 28, 168, 30), (239, 165, 68, 255))
    draw.polygon(
        [(151, 31), (168, 31), (190, 101), (130, 101)],
        fill=(57, 43, 31, 255),
    )
    for beam_y in range(35, 101, 7):
        half_width = 9 + (beam_y - 35) * 3 // 8
        line(
            draw,
            [(159 - half_width, beam_y), (159 + half_width, beam_y)],
            (68, 48, 30, 255),
        )

    # Wainscot and carved wood panels.
    rect(draw, (0, 104, 319, 128), (26, 12, 17, 255))
    rect(draw, (0, 105, 319, 108), (111, 53, 31, 255))
    rect(draw, (0, 124, 319, 128), (82, 37, 27, 255))
    for x in range(5, 320, 28):
        rect(draw, (x, 111, min(x + 22, 319), 122), (44, 19, 24, 255))
        line(
            draw,
            [(x, 122), (min(x + 22, 319), 111)],
            (69, 29, 27, 255),
        )

    # Foreground bar top in strong one-point perspective.
    draw.polygon(
        [(48, 127), (271, 127), (319, 179), (0, 179)],
        fill=(47, 22, 22, 255),
    )
    draw.polygon(
        [(56, 130), (263, 130), (302, 166), (17, 166)],
        fill=(89, 42, 27, 255),
    )
    draw.polygon(
        [(65, 134), (254, 134), (285, 160), (34, 160)],
        fill=(105, 52, 30, 255),
    )
    line(draw, [(48, 127), (271, 127)], (226, 138, 62, 255), 3)
    line(draw, [(17, 166), (302, 166)], (35, 16, 19, 255), 3)
    for y in (140, 148, 156):
        inset = (y - 134) * 2
        line(
            draw,
            [(65 - inset, y), (254 + inset, y)],
            (76, 34, 26, 255),
        )
    for x in range(42, 287, 24):
        line(draw, [(x, 135), (x - 12, 160)], (67, 30, 25, 255))

    # A subtle central coaster anchors the overlaid pint.
    draw.ellipse((130, 147, 189, 164), fill=(31, 14, 19, 255))
    draw.ellipse((136, 149, 183, 161), fill=(126, 64, 33, 255))
    draw.ellipse((140, 151, 179, 159), fill=(65, 29, 26, 255))

    # Sparse, deterministic highlights keep the background alive.
    for _ in range(105):
        x = rng.randrange(4, 316)
        y = rng.randrange(20, 165)
        if 98 <= x <= 221 and 24 <= y <= 107:
            continue
        color = (
            (132, 65, 37, 255)
            if rng.randrange(4)
            else (230, 148, 64, 255)
        )
        rect(draw, (x, y, x + rng.randrange(1, 3), y), color)

    image.putalpha(255)
    return image.resize(BACKGROUND_OUTPUT_SIZE, Image.Resampling.NEAREST)


def draw_glass_back(draw: ImageDraw.ImageDraw) -> None:
    draw.polygon(
        [(15, 8), (49, 8), (46, 57), (20, 57)],
        fill=(86, 111, 119, 34),
    )
    draw.polygon(
        [(18, 11), (46, 11), (43, 54), (22, 54)],
        fill=(154, 178, 178, 18),
    )
    rect(draw, (16, 7, 48, 9), (104, 125, 130, 120))
    rect(draw, (21, 55, 44, 58), (80, 98, 107, 100))


def draw_glass_front(draw: ImageDraw.ImageDraw) -> None:
    shadow = (25, 25, 39, 220)
    glass = (190, 208, 203, 235)
    highlight = (242, 235, 205, 245)
    cool = (91, 124, 137, 220)
    line(draw, [(14, 7), (50, 7), (47, 58), (19, 58), (14, 7)], shadow, 3)
    line(draw, [(16, 8), (48, 8), (45, 56), (21, 56), (16, 8)], glass, 2)
    line(draw, [(19, 11), (22, 51)], highlight, 2)
    rect(draw, (23, 54, 43, 57), cool)
    rect(draw, (20, 8, 44, 9), highlight)
    rect(draw, (42, 13, 45, 47), (120, 155, 160, 150))
    rect(draw, (23, 14, 24, 36), (255, 255, 231, 165))


def draw_hand_grip(draw: ImageDraw.ImageDraw) -> None:
    outline = (38, 20, 28, 255)
    skin_dark = (126, 65, 48, 255)
    skin = (191, 113, 73, 255)
    light = (235, 167, 99, 255)
    sleeve = (73, 20, 33, 255)
    draw.polygon(
        [(33, 25), (23, 22), (15, 27), (9, 41), (17, 49), (33, 52)],
        fill=outline,
    )
    draw.polygon(
        [(33, 28), (24, 25), (18, 28), (12, 40), (18, 46), (33, 49)],
        fill=skin,
    )
    rect(draw, (25, 29, 33, 48), skin_dark)
    for y in (30, 35, 40, 45):
        rect(draw, (10, y, 23, y + 2), light)
        rect(draw, (10, y + 3, 20, y + 4), skin_dark)
    rect(draw, (28, 24, 33, 51), sleeve)


def draw_hand_release(draw: ImageDraw.ImageDraw) -> None:
    outline = (38, 20, 28, 255)
    skin_dark = (126, 65, 48, 255)
    skin = (191, 113, 73, 255)
    light = (235, 167, 99, 255)
    sleeve = (73, 20, 33, 255)
    draw.polygon(
        [(39, 43), (26, 43), (18, 38), (10, 30), (13, 26), (22, 31),
         (17, 20), (22, 18), (28, 31), (27, 16), (32, 16), (36, 34),
         (39, 35)],
        fill=outline,
    )
    draw.polygon(
        [(39, 46), (26, 40), (20, 36), (13, 29), (15, 27), (24, 34),
         (20, 21), (22, 20), (29, 35), (29, 18), (32, 18), (35, 37),
         (39, 38)],
        fill=skin,
    )
    rect(draw, (30, 39, 38, 49), sleeve)
    rect(draw, (23, 32, 31, 35), light)
    rect(draw, (33, 37, 38, 40), skin_dark)


def draw_foam_calm(draw: ImageDraw.ImageDraw) -> None:
    shadow = (145, 113, 70, 220)
    cream = (238, 222, 175, 255)
    light = (255, 246, 210, 255)
    rect(draw, (12, 28, 52, 38), shadow)
    for bounds in ((13, 23, 23, 35), (21, 19, 34, 36),
                   (31, 22, 44, 36), (41, 25, 52, 36)):
        draw.ellipse(bounds, fill=cream)
    rect(draw, (15, 31, 49, 38), cream)
    rect(draw, (18, 24, 23, 27), light)
    rect(draw, (27, 21, 34, 24), light)
    rect(draw, (39, 25, 45, 28), light)


def draw_foam_rough(draw: ImageDraw.ImageDraw) -> None:
    cream = (238, 222, 175, 255)
    light = (255, 246, 210, 255)
    amber = (207, 158, 83, 235)
    draw.polygon(
        [(8, 39), (13, 26), (18, 29), (22, 15), (28, 23), (35, 10),
         (39, 24), (48, 18), (50, 29), (57, 26), (54, 41)],
        fill=amber,
    )
    for bounds in ((10, 27, 24, 42), (17, 20, 34, 42),
                   (28, 17, 45, 42), (39, 24, 56, 42)):
        draw.ellipse(bounds, fill=cream)
    rect(draw, (11, 35, 54, 43), cream)
    rect(draw, (23, 21, 28, 25), light)
    rect(draw, (35, 18, 40, 23), light)
    rect(draw, (45, 28, 50, 32), light)


def draw_g_mark(draw: ImageDraw.ImageDraw) -> None:
    shadow = (49, 24, 29, 255)
    gold_dark = (156, 87, 32, 255)
    gold = (237, 166, 58, 255)
    light = (255, 219, 104, 255)
    # Dark under-print keeps the letter readable over pale foam or dark beer.
    rect(draw, (20, 16, 43, 20), shadow)
    rect(draw, (16, 20, 22, 45), shadow)
    rect(draw, (20, 43, 43, 49), shadow)
    rect(draw, (34, 30, 47, 36), shadow)
    rect(draw, (41, 32, 47, 46), shadow)
    rect(draw, (22, 18, 41, 21), gold)
    rect(draw, (18, 21, 23, 44), gold)
    rect(draw, (22, 42, 41, 47), gold)
    rect(draw, (34, 31, 45, 35), gold)
    rect(draw, (40, 34, 45, 44), gold)
    rect(draw, (23, 19, 39, 20), light)
    rect(draw, (19, 23, 20, 39), light)
    rect(draw, (24, 43, 39, 44), gold_dark)


def draw_target_pulse(draw: ImageDraw.ImageDraw) -> None:
    dark = (49, 24, 29, 230)
    amber = (237, 166, 58, 245)
    light = (255, 225, 117, 255)
    rect(draw, (7, 29, 57, 35), dark)
    rect(draw, (10, 31, 54, 33), amber)
    rect(draw, (15, 31, 49, 31), light)
    draw.polygon([(4, 32), (11, 25), (11, 39)], fill=amber)
    draw.polygon([(60, 32), (53, 25), (53, 39)], fill=amber)


def draw_bubble_trail(draw: ImageDraw.ImageDraw) -> None:
    outline = (123, 91, 58, 220)
    foam = (249, 232, 183, 245)
    for x, y, radius in (
        (24, 49, 3), (29, 40, 2), (26, 32, 2),
        (34, 26, 3), (31, 17, 2), (39, 10, 2),
    ):
        draw.ellipse((x - radius, y - radius, x + radius, y + radius),
                     fill=outline)
        rect(draw, (x - radius + 1, y - radius + 1, x, y), foam)


def draw_bubble_burst(draw: ImageDraw.ImageDraw) -> None:
    outline = (123, 91, 58, 230)
    foam = (255, 238, 189, 255)
    positions = (
        (32, 31, 7), (19, 35, 5), (44, 39, 5),
        (25, 19, 4), (42, 19, 3), (14, 23, 3), (51, 27, 3),
    )
    for x, y, radius in positions:
        draw.ellipse((x - radius, y - radius, x + radius, y + radius),
                     fill=outline)
        draw.ellipse(
            (x - radius + 2, y - radius + 2, x + radius - 2, y + radius - 2),
            fill=foam,
        )
        rect(draw, (x - radius + 2, y - radius + 2, x, y - radius + 3),
             (255, 251, 220, 255))


def draw_gold_spark(draw: ImageDraw.ImageDraw) -> None:
    dark = (133, 68, 31, 255)
    gold = (245, 173, 56, 255)
    light = (255, 239, 142, 255)
    draw.polygon(
        [(32, 5), (37, 24), (57, 19), (42, 32),
         (56, 45), (37, 40), (32, 59), (27, 40),
         (8, 45), (22, 32), (7, 19), (27, 24)],
        fill=dark,
    )
    draw.polygon(
        [(32, 10), (35, 27), (51, 23), (39, 32),
         (50, 41), (35, 37), (32, 54), (29, 37),
         (13, 41), (25, 32), (13, 23), (29, 27)],
        fill=gold,
    )
    rect(draw, (30, 18, 33, 45), light)
    rect(draw, (20, 30, 44, 33), light)


def draw_splash(draw: ImageDraw.ImageDraw) -> None:
    dark = (117, 78, 45, 240)
    foam = (244, 224, 174, 255)
    light = (255, 248, 216, 255)
    draw.polygon(
        [(7, 48), (13, 29), (20, 39), (25, 13), (33, 35),
         (43, 17), (45, 38), (56, 28), (58, 49)],
        fill=dark,
    )
    draw.polygon(
        [(10, 46), (15, 34), (22, 42), (27, 20), (34, 40),
         (41, 24), (43, 43), (54, 34), (55, 47)],
        fill=foam,
    )
    rect(draw, (9, 45, 56, 51), foam)
    rect(draw, (15, 35, 18, 39), light)
    rect(draw, (27, 22, 30, 29), light)
    rect(draw, (42, 27, 45, 34), light)


def draw_droplet(draw: ImageDraw.ImageDraw) -> None:
    dark = (93, 52, 37, 255)
    amber = (211, 141, 55, 255)
    light = (255, 219, 111, 255)
    draw.polygon(
        [(32, 8), (20, 29), (19, 42), (25, 51),
         (32, 55), (40, 51), (46, 42), (44, 29)],
        fill=dark,
    )
    draw.polygon(
        [(32, 13), (24, 30), (23, 41), (28, 48),
         (33, 51), (39, 47), (42, 40), (40, 29)],
        fill=amber,
    )
    rect(draw, (27, 29, 30, 41), light)


def draw_coaster(draw: ImageDraw.ImageDraw) -> None:
    dark = (44, 20, 27, 255)
    wine = (104, 29, 42, 255)
    gold = (207, 126, 50, 255)
    draw.ellipse((7, 20, 57, 49), fill=dark)
    draw.ellipse((10, 22, 54, 46), fill=gold)
    draw.ellipse((14, 25, 50, 43), fill=wine)
    rect(draw, (17, 31, 47, 35), dark)
    rect(draw, (22, 29, 42, 37), wine)
    rect(draw, (25, 30, 39, 33), (185, 68, 50, 255))


def draw_perfect_burst(draw: ImageDraw.ImageDraw) -> None:
    gold = (238, 166, 55, 255)
    light = (255, 237, 146, 255)
    green = (73, 143, 104, 255)
    dark = (25, 36, 35, 255)
    draw.ellipse((14, 14, 50, 50), fill=dark)
    draw.ellipse((17, 17, 47, 47), fill=green)
    draw.ellipse((22, 22, 42, 42), fill=(20, 63, 54, 255))
    for x, y in ((32, 5), (32, 55), (5, 32), (55, 32),
                 (12, 12), (52, 12), (12, 52), (52, 52)):
        rect(draw, (x - 2, y - 2, x + 2, y + 2), gold)
        rect(draw, (x - 1, y - 1, x + 1, y + 1), light)
    draw.polygon(
        [(31, 24), (35, 29), (43, 21), (47, 25),
         (35, 39), (26, 31)],
        fill=light,
    )


def draw_miss_burst(draw: ImageDraw.ImageDraw) -> None:
    dark = (42, 18, 28, 255)
    red = (171, 46, 52, 255)
    light = (239, 103, 73, 255)
    draw.polygon(
        [(32, 4), (39, 14), (52, 10), (50, 24), (61, 32),
         (50, 40), (53, 54), (39, 50), (32, 61), (25, 50),
         (11, 54), (14, 40), (3, 32), (14, 24), (11, 10),
         (25, 14)],
        fill=dark,
    )
    draw.polygon(
        [(32, 9), (38, 18), (48, 15), (46, 26), (55, 32),
         (46, 38), (48, 49), (38, 46), (32, 55), (26, 46),
         (16, 49), (18, 38), (9, 32), (18, 26), (16, 15),
         (26, 18)],
        fill=red,
    )
    line(draw, [(23, 23), (41, 41)], light, 5)
    line(draw, [(41, 23), (23, 41)], light, 5)


CELL_DRAWERS: tuple[Callable[[ImageDraw.ImageDraw], None], ...] = (
    draw_glass_back,
    draw_glass_front,
    draw_hand_grip,
    draw_hand_release,
    draw_foam_calm,
    draw_foam_rough,
    draw_g_mark,
    draw_target_pulse,
    draw_bubble_trail,
    draw_bubble_burst,
    draw_gold_spark,
    draw_splash,
    draw_droplet,
    draw_coaster,
    draw_perfect_burst,
    draw_miss_burst,
)


def build_atlas() -> Image.Image:
    source_size = CELL_SOURCE_SIZE * ATLAS_COLUMNS
    source = Image.new("RGBA", (source_size, source_size), TRANSPARENT)
    for index, painter in enumerate(CELL_DRAWERS):
        cell = Image.new(
            "RGBA",
            (CELL_SOURCE_SIZE, CELL_SOURCE_SIZE),
            TRANSPARENT,
        )
        painter(ImageDraw.Draw(cell))
        column = index % ATLAS_COLUMNS
        row = index // ATLAS_COLUMNS
        source.alpha_composite(
            cell,
            (column * CELL_SOURCE_SIZE, row * CELL_SOURCE_SIZE),
        )

    return source.resize(
        (ATLAS_OUTPUT_SIZE, ATLAS_OUTPUT_SIZE),
        Image.Resampling.NEAREST,
    )


def save_png(image: Image.Image, path: Path) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=False, compress_level=9)
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--background",
        type=Path,
        default=DEFAULT_BACKGROUND,
        help="Destination for the 640x360 opaque bar background.",
    )
    parser.add_argument(
        "--atlas",
        type=Path,
        default=DEFAULT_ATLAS,
        help="Destination for the 512x512 transparent 4x4 sprite atlas.",
    )
    args = parser.parse_args()

    background = build_background()
    atlas = build_atlas()
    background_hash = save_png(background, args.background)
    atlas_hash = save_png(atlas, args.atlas)

    print(
        f"Wrote {args.background} "
        f"({background.width}x{background.height}) SHA256={background_hash}"
    )
    print(
        f"Wrote {args.atlas} "
        f"({atlas.width}x{atlas.height}) SHA256={atlas_hash}"
    )


if __name__ == "__main__":
    main()
