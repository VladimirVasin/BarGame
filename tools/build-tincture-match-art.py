#!/usr/bin/env python3
"""Build deterministic pixel art for the Tincture Match minigame.

The backdrop is authored at 320x180 and the 4x4 atlas at 256x256, then both
are enlarged with nearest-neighbour sampling. Pillow is the only dependency.
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
    ROOT / "Assets" / "Resources" / "TinctureMatch" /
    "TinctureMatchBackground.png"
)
DEFAULT_ATLAS = (
    ROOT / "Assets" / "Resources" / "TinctureMatch" /
    "TinctureMatchAtlas.png"
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
    image = Image.new("RGBA", BACKGROUND_SOURCE_SIZE, (12, 8, 15, 255))
    draw = ImageDraw.Draw(image)
    rng = random.Random(0x54494E4354555245)

    # Deep plum plaster, stepped to keep the low-resolution look explicit.
    wall_bands = (
        (0, 22, (15, 9, 18, 255)),
        (22, 48, (31, 13, 29, 255)),
        (48, 76, (43, 17, 34, 255)),
        (76, 106, (36, 15, 30, 255)),
        (106, 124, (25, 12, 24, 255)),
    )
    for y_min, y_max, color in wall_bands:
        rect(draw, (0, y_min, 319, y_max), color)
    rect(draw, (0, 0, 319, 8), (6, 5, 10, 255))
    rect(draw, (0, 10, 319, 13), (54, 24, 39, 255))

    # Brick seams and sparse chipped highlights.
    for y in range(26, 116, 10):
        offset = 0 if (y // 10) % 2 == 0 else 13
        line(draw, [(0, y), (319, y)], (22, 10, 23, 255))
        for x in range(-offset, 321, 26):
            line(draw, [(x, y), (x, y + 9)], (22, 10, 23, 255))
            if rng.randrange(4) == 0 and x + 5 >= 0:
                rect(
                    draw,
                    (max(0, x + 3), y + 2, min(319, x + 5), y + 3),
                    (71, 29, 44, 255),
                )

    # Bottle shelves frame the central board without competing with pieces.
    bottle_colors = (
        (125, 38, 43, 255),
        (188, 95, 35, 255),
        (42, 75, 92, 255),
        (45, 104, 70, 255),
        (121, 87, 42, 255),
        (77, 45, 92, 255),
    )
    for shelf_x in (9, 238):
        rect(draw, (shelf_x, 25, shelf_x + 72, 103), (8, 7, 12, 255))
        rect(draw, (shelf_x + 3, 28, shelf_x + 69, 100),
             (27, 14, 24, 255))
        for shelf_y in (51, 75, 99):
            rect(draw, (shelf_x, shelf_y, shelf_x + 72, shelf_y + 3),
                 (116, 62, 34, 255))
            rect(draw, (shelf_x + 3, shelf_y + 4,
                        shelf_x + 69, shelf_y + 5),
                 (45, 22, 25, 255))
        for row, base_y in enumerate((50, 74, 98)):
            for bottle in range(6):
                x = shelf_x + 7 + bottle * 10
                height = 13 + ((bottle * 3 + row) % 7)
                color = bottle_colors[(bottle + row * 2) % len(bottle_colors)]
                rect(draw, (x, base_y - height, x + 6, base_y), color)
                rect(draw, (x + 2, base_y - height - 5,
                            x + 4, base_y - height), color)
                rect(draw, (x + 1, base_y - height + 3,
                            x + 2, base_y - 2),
                     (230, 175, 101, 125))
                rect(draw, (x + 1, base_y - 7, x + 5, base_y - 4),
                     (215, 184, 118, 255))

    # A quiet, green-black tiled alcove is the stage behind the game board.
    rect(draw, (87, 21, 232, 111), (7, 7, 12, 255))
    rect(draw, (90, 24, 229, 108), (125, 67, 36, 255))
    rect(draw, (94, 28, 225, 104), (13, 28, 27, 255))
    for y in range(30, 104, 8):
        shade = (18, 42, 37, 255) if (y // 8) % 2 else (15, 35, 33, 255)
        rect(draw, (96, y, 223, y + 3), shade)
    for x in range(98, 224, 14):
        line(draw, [(x, 29), (x, 103)], (10, 25, 25, 255))
    rect(draw, (90, 24, 229, 27), (206, 128, 58, 255))
    rect(draw, (90, 105, 229, 109), (71, 34, 29, 255))
    rect(draw, (91, 28, 94, 104), (166, 89, 44, 255))
    rect(draw, (225, 28, 228, 104), (77, 37, 31, 255))

    # Twin pendant lamps give the backdrop a readable central rhythm.
    for lamp_x in (126, 194):
        rect(draw, (lamp_x - 1, 4, lamp_x + 1, 16),
             (56, 30, 33, 255))
        draw.polygon(
            [(lamp_x - 10, 17), (lamp_x + 10, 17),
             (lamp_x + 15, 25), (lamp_x - 15, 25)],
            fill=(35, 18, 24, 255),
        )
        rect(draw, (lamp_x - 12, 24, lamp_x + 12, 28),
             (132, 69, 37, 255))
        rect(draw, (lamp_x - 7, 28, lamp_x + 7, 30),
             (252, 190, 82, 255))
        draw.polygon(
            [(lamp_x - 7, 31), (lamp_x + 7, 31),
             (lamp_x + 29, 108), (lamp_x - 29, 108)],
            fill=(72, 55, 31, 45),
        )

    # Foreground counter with inlaid serving mat and shot silhouettes.
    draw.polygon(
        [(42, 118), (277, 118), (319, 179), (0, 179)],
        fill=(48, 23, 24, 255),
    )
    draw.polygon(
        [(51, 122), (268, 122), (302, 166), (17, 166)],
        fill=(105, 51, 30, 255),
    )
    draw.polygon(
        [(63, 128), (256, 128), (282, 158), (37, 158)],
        fill=(123, 61, 33, 255),
    )
    line(draw, [(42, 118), (277, 118)], (235, 148, 67, 255), 3)
    line(draw, [(17, 166), (302, 166)], (29, 14, 20, 255), 4)
    for y in (136, 145, 153):
        inset = (y - 128) * 2
        line(draw, [(63 - inset, y), (256 + inset, y)],
             (84, 38, 29, 255))

    draw.polygon(
        [(88, 132), (231, 132), (252, 158), (67, 158)],
        fill=(18, 33, 31, 255),
    )
    draw.polygon(
        [(93, 135), (226, 135), (241, 153), (78, 153)],
        fill=(23, 50, 43, 255),
    )
    line(draw, [(93, 135), (226, 135)], (55, 114, 85, 255), 2)

    # Small decorative shot lineup; silhouettes only, gameplay remains on top.
    for index, x in enumerate((105, 124, 143, 162, 181, 200)):
        liquid = bottle_colors[index]
        draw.polygon(
            [(x, 136), (x + 12, 136), (x + 10, 149), (x + 2, 149)],
            fill=(20, 17, 23, 255),
        )
        rect(draw, (x + 2, 141, x + 10, 147), liquid)
        line(draw, [(x + 1, 136), (x + 11, 136)], (205, 218, 204, 255), 1)

    # Deterministic highlights keep large dark areas from feeling flat.
    for _ in range(92):
        x = rng.randrange(4, 316)
        y = rng.randrange(18, 164)
        if 86 <= x <= 233 and 20 <= y <= 112:
            continue
        color = (
            (138, 62, 40, 255)
            if rng.randrange(5)
            else (234, 154, 66, 255)
        )
        rect(draw, (x, y, x + rng.randrange(1, 3), y), color)

    image.putalpha(255)
    return image.resize(BACKGROUND_OUTPUT_SIZE, Image.Resampling.NEAREST)


GLASS_OUTLINE = (28, 22, 32, 255)
GLASS_EDGE = (190, 213, 208, 255)
GLASS_LIGHT = (247, 244, 218, 255)


def draw_shot_glass(
    draw: ImageDraw.ImageDraw,
    liquid_dark: tuple[int, int, int, int],
    liquid: tuple[int, int, int, int],
    icon: Callable[[ImageDraw.ImageDraw], None],
    clear: bool = False,
) -> None:
    # Broad, squat silhouette stays readable at the intended ~36px draw size.
    draw.ellipse((13, 50, 51, 59), fill=(17, 13, 21, 150))
    draw.polygon(
        [(11, 8), (53, 8), (48, 54), (16, 54)],
        fill=GLASS_OUTLINE,
    )
    draw.polygon(
        [(14, 11), (50, 11), (45, 51), (19, 51)],
        fill=(120, 151, 151, 110),
    )
    fill_color = (185, 205, 194, 115) if clear else liquid
    dark_color = (83, 112, 111, 170) if clear else liquid_dark
    draw.polygon(
        [(17, 25), (47, 25), (44, 49), (20, 49)],
        fill=dark_color,
    )
    draw.polygon(
        [(18, 27), (46, 27), (43, 47), (21, 47)],
        fill=fill_color,
    )
    rect(draw, (12, 7, 52, 11), GLASS_OUTLINE)
    rect(draw, (15, 8, 49, 9), GLASS_LIGHT)
    line(draw, [(16, 13), (20, 47)], GLASS_LIGHT, 2)
    line(draw, [(48, 13), (44, 47)], (94, 131, 135, 255), 2)
    rect(draw, (21, 50, 43, 53), GLASS_EDGE)
    icon(draw)


def draw_cherry_icon(draw: ImageDraw.ImageDraw) -> None:
    icon_dark = (52, 16, 27, 255)
    icon_light = (255, 223, 152, 255)
    line(draw, [(31, 37), (34, 28), (39, 24)], icon_light, 2)
    line(draw, [(34, 28), (28, 24)], icon_light, 2)
    draw.ellipse((23, 34, 31, 42), fill=icon_dark)
    draw.ellipse((33, 34, 41, 42), fill=icon_dark)
    rect(draw, (25, 35, 27, 37), icon_light)
    rect(draw, (35, 35, 37, 37), icon_light)


def draw_sea_buckthorn_icon(draw: ImageDraw.ImageDraw) -> None:
    dark = (91, 42, 16, 255)
    light = (255, 242, 174, 255)
    for x, y in ((32, 32), (25, 35), (39, 35), (28, 42), (36, 42)):
        draw.ellipse((x - 4, y - 4, x + 4, y + 4), fill=dark)
        rect(draw, (x - 1, y - 2, x + 1, y), light)
    line(draw, [(32, 27), (32, 45)], light, 1)


def draw_blueberry_icon(draw: ImageDraw.ImageDraw) -> None:
    dark = (16, 22, 50, 255)
    light = (220, 225, 255, 255)
    for x, y in ((26, 38), (38, 38), (32, 31)):
        draw.ellipse((x - 6, y - 6, x + 6, y + 6), fill=dark)
        rect(draw, (x - 1, y - 3, x + 1, y - 1), light)
        line(draw, [(x - 3, y - 1), (x + 3, y - 1)], light, 1)


def draw_mint_icon(draw: ImageDraw.ImageDraw) -> None:
    dark = (14, 53, 36, 255)
    light = (222, 246, 193, 255)
    draw.polygon(
        [(23, 41), (24, 31), (31, 25), (41, 26),
         (41, 35), (34, 42)],
        fill=dark,
    )
    line(draw, [(23, 44), (38, 28)], light, 2)
    line(draw, [(30, 36), (27, 30)], light, 1)
    line(draw, [(34, 32), (41, 33)], light, 1)


def draw_horseradish_icon(draw: ImageDraw.ImageDraw) -> None:
    dark = (66, 46, 26, 255)
    light = (250, 230, 180, 255)
    draw.polygon(
        [(27, 25), (39, 27), (36, 34), (42, 37),
         (35, 41), (32, 48), (27, 45), (29, 38),
         (23, 35)],
        fill=dark,
    )
    line(draw, [(35, 27), (29, 45)], light, 2)
    line(draw, [(32, 33), (25, 30)], light, 2)
    line(draw, [(30, 38), (39, 36)], light, 2)


def draw_moonshine_icon(draw: ImageDraw.ImageDraw) -> None:
    # Literal three-X mark, deliberately large enough to survive point scaling.
    dark = (18, 31, 37, 255)
    light = (244, 247, 220, 255)
    rect(draw, (18, 29, 46, 44), (205, 220, 203, 210))
    rect(draw, (19, 30, 45, 43), dark)
    for center_x in (23, 32, 41):
        line(draw, [(center_x - 3, 32), (center_x + 3, 41)], light, 2)
        line(draw, [(center_x + 3, 32), (center_x - 3, 41)], light, 2)


def draw_cherry(draw: ImageDraw.ImageDraw) -> None:
    draw_shot_glass(
        draw,
        (91, 18, 37, 255),
        (184, 37, 56, 255),
        draw_cherry_icon,
    )


def draw_sea_buckthorn(draw: ImageDraw.ImageDraw) -> None:
    draw_shot_glass(
        draw,
        (145, 64, 14, 255),
        (235, 124, 31, 255),
        draw_sea_buckthorn_icon,
    )


def draw_blueberry(draw: ImageDraw.ImageDraw) -> None:
    draw_shot_glass(
        draw,
        (31, 28, 85, 255),
        (66, 59, 153, 255),
        draw_blueberry_icon,
    )


def draw_mint(draw: ImageDraw.ImageDraw) -> None:
    draw_shot_glass(
        draw,
        (23, 84, 54, 255),
        (52, 151, 87, 255),
        draw_mint_icon,
    )


def draw_horseradish(draw: ImageDraw.ImageDraw) -> None:
    draw_shot_glass(
        draw,
        (131, 97, 38, 255),
        (205, 169, 77, 255),
        draw_horseradish_icon,
    )


def draw_moonshine(draw: ImageDraw.ImageDraw) -> None:
    draw_shot_glass(
        draw,
        (83, 112, 111, 170),
        (185, 205, 194, 115),
        draw_moonshine_icon,
        clear=True,
    )


def draw_shadow(draw: ImageDraw.ImageDraw) -> None:
    draw.ellipse((7, 22, 57, 51), fill=(10, 9, 18, 95))
    draw.ellipse((13, 27, 51, 47), fill=(10, 9, 18, 125))
    draw.ellipse((20, 31, 44, 44), fill=(5, 5, 12, 145))


def draw_selection(draw: ImageDraw.ImageDraw) -> None:
    dark = (60, 31, 25, 255)
    gold = (242, 171, 61, 255)
    light = (255, 239, 149, 255)
    for x, y, sx, sy in (
        (7, 7, 1, 1), (57, 7, -1, 1),
        (7, 57, 1, -1), (57, 57, -1, -1),
    ):
        line(draw, [(x, y), (x + sx * 15, y)], dark, 5)
        line(draw, [(x, y), (x, y + sy * 15)], dark, 5)
        line(draw, [(x, y), (x + sx * 14, y)], gold, 3)
        line(draw, [(x, y), (x, y + sy * 14)], gold, 3)
        rect(draw, (x - 1, y - 1, x + 1, y + 1), light)


def draw_swap_arrows(draw: ImageDraw.ImageDraw) -> None:
    dark = (36, 24, 34, 255)
    cyan = (98, 211, 194, 255)
    light = (224, 255, 225, 255)
    line(draw, [(12, 24), (49, 24)], dark, 7)
    line(draw, [(15, 22), (49, 22)], cyan, 3)
    draw.polygon([(55, 22), (44, 14), (44, 30)], fill=cyan)
    line(draw, [(52, 42), (15, 42)], dark, 7)
    line(draw, [(49, 40), (15, 40)], cyan, 3)
    draw.polygon([(9, 40), (20, 32), (20, 48)], fill=cyan)
    rect(draw, (22, 21, 41, 21), light)
    rect(draw, (23, 39, 42, 39), light)


def draw_match_flash(draw: ImageDraw.ImageDraw) -> None:
    dark = (89, 43, 21, 230)
    gold = (248, 181, 62, 245)
    light = (255, 248, 192, 255)
    draw.polygon(
        [(32, 4), (38, 22), (54, 10), (44, 27),
         (61, 32), (44, 37), (54, 54), (38, 42),
         (32, 61), (26, 42), (10, 54), (20, 37),
         (3, 32), (20, 27), (10, 10), (26, 22)],
        fill=dark,
    )
    draw.polygon(
        [(32, 10), (36, 26), (48, 17), (40, 29),
         (54, 32), (40, 35), (48, 47), (36, 38),
         (32, 54), (28, 38), (16, 47), (24, 35),
         (10, 32), (24, 29), (16, 17), (28, 26)],
        fill=gold,
    )
    rect(draw, (29, 17, 35, 47), light)
    rect(draw, (17, 29, 47, 35), light)


def draw_shards(draw: ImageDraw.ImageDraw) -> None:
    edge = (111, 159, 161, 225)
    light = (231, 247, 224, 245)
    fill = (102, 151, 151, 90)
    shards = (
        [(8, 42), (21, 25), (23, 48)],
        [(25, 13), (36, 8), (32, 29)],
        [(38, 25), (56, 18), (48, 40)],
        [(29, 35), (43, 54), (25, 52)],
        [(8, 18), (19, 10), (19, 29)],
    )
    for points in shards:
        draw.polygon(points, fill=fill)
        line(draw, points + [points[0]], edge, 2)
        line(draw, [points[0], points[1]], light, 1)


def draw_droplets(draw: ImageDraw.ImageDraw) -> None:
    dark = (56, 35, 50, 240)
    amber = (214, 115, 52, 245)
    light = (255, 220, 134, 255)
    for x, y, radius in (
        (18, 42, 7), (32, 24, 9), (45, 43, 6), (51, 19, 4),
    ):
        points = [
            (x, y - radius * 2),
            (x - radius, y),
            (x - radius + 1, y + radius),
            (x, y + radius + 3),
            (x + radius - 1, y + radius),
            (x + radius, y),
        ]
        draw.polygon(points, fill=dark)
        inner = [(px, py + 2) for px, py in points]
        draw.polygon(inner, fill=amber)
        rect(draw, (x - 2, y - radius, x, y - radius + 3), light)


def draw_combo(draw: ImageDraw.ImageDraw) -> None:
    dark = (38, 25, 42, 255)
    magenta = (215, 75, 117, 255)
    gold = (246, 177, 60, 255)
    light = (255, 239, 168, 255)
    for x, y in ((18, 32), (32, 20), (46, 32), (32, 46)):
        draw.ellipse((x - 8, y - 8, x + 8, y + 8), fill=dark)
        draw.ellipse((x - 5, y - 5, x + 5, y + 5),
                     fill=magenta if (x + y) % 3 else gold)
    line(draw, [(18, 32), (32, 20), (46, 32), (32, 46), (18, 32)],
         gold, 3)
    draw.polygon(
        [(32, 25), (35, 30), (41, 32), (35, 35),
         (32, 41), (29, 35), (23, 32), (29, 29)],
        fill=light,
    )


def draw_moonshine_burst(draw: ImageDraw.ImageDraw) -> None:
    dark = (17, 25, 35, 255)
    blue = (92, 201, 221, 255)
    light = (239, 255, 239, 255)
    draw.polygon(
        [(32, 3), (39, 19), (54, 9), (47, 25),
         (62, 32), (47, 39), (54, 55), (39, 45),
         (32, 62), (25, 45), (10, 55), (17, 39),
         (2, 32), (17, 25), (10, 9), (25, 19)],
        fill=dark,
    )
    draw.polygon(
        [(32, 8), (37, 23), (49, 15), (43, 27),
         (56, 32), (43, 37), (49, 49), (37, 41),
         (32, 56), (27, 41), (15, 49), (21, 37),
         (8, 32), (21, 27), (15, 15), (27, 23)],
        fill=blue,
    )
    rect(draw, (13, 26, 51, 39), dark)
    for center_x in (20, 32, 44):
        line(draw, [(center_x - 4, 28), (center_x + 4, 37)], light, 3)
        line(draw, [(center_x + 4, 28), (center_x - 4, 37)], light, 3)


def draw_invalid(draw: ImageDraw.ImageDraw) -> None:
    dark = (48, 17, 27, 255)
    red = (210, 47, 55, 255)
    light = (255, 129, 91, 255)
    draw.ellipse((7, 7, 57, 57), fill=dark)
    draw.ellipse((11, 11, 53, 53), fill=red)
    draw.ellipse((17, 17, 47, 47), fill=(42, 20, 30, 230))
    line(draw, [(19, 19), (45, 45)], light, 7)
    line(draw, [(45, 19), (19, 45)], light, 7)
    line(draw, [(20, 20), (44, 44)], (255, 218, 165, 255), 2)


def draw_reshuffle(draw: ImageDraw.ImageDraw) -> None:
    dark = (26, 29, 40, 255)
    cyan = (80, 190, 178, 255)
    light = (219, 255, 221, 255)
    draw.arc((10, 10, 54, 54), 200, 350, fill=dark, width=8)
    draw.arc((10, 10, 54, 54), 200, 350, fill=cyan, width=4)
    draw.polygon([(56, 30), (45, 20), (44, 36)], fill=cyan)
    draw.arc((10, 10, 54, 54), 20, 170, fill=dark, width=8)
    draw.arc((10, 10, 54, 54), 20, 170, fill=cyan, width=4)
    draw.polygon([(8, 34), (19, 44), (20, 28)], fill=cyan)
    rect(draw, (38, 14, 43, 16), light)
    rect(draw, (21, 47, 26, 49), light)


CELL_DRAWERS: tuple[Callable[[ImageDraw.ImageDraw], None], ...] = (
    draw_cherry,
    draw_sea_buckthorn,
    draw_blueberry,
    draw_mint,
    draw_horseradish,
    draw_moonshine,
    draw_shadow,
    draw_selection,
    draw_swap_arrows,
    draw_match_flash,
    draw_shards,
    draw_droplets,
    draw_combo,
    draw_moonshine_burst,
    draw_invalid,
    draw_reshuffle,
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
        help="Destination for the opaque 640x360 bar backdrop.",
    )
    parser.add_argument(
        "--atlas",
        type=Path,
        default=DEFAULT_ATLAS,
        help="Destination for the transparent 512x512 4x4 atlas.",
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
