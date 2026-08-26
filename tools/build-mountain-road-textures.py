#!/usr/bin/env python3
"""Build the deterministic surface albedos for the Mountain Road area.

The mountain road is a separately loaded area with a material language the
City families do not cover: cold worn mountain asphalt, a damp coniferous
forest floor, wind-packed dirty snow, large layered mountain stone, dark
uneven conifer needles and bark/weathered deadwood.  This tool produces those
six seamless 1024x1024 RGB sources under ``Assets/Resources/Textures``; their
checked-in Unity importers cap the runtime copy at 512.
``MountainRoadSurfaceAppearance`` then applies the sheets and their measured
tint compensation through ``MaterialPropertyBlock`` recipes on the shared
runtime primitive material.

The bridge, the cableway and the cafe do NOT get new sheets.  Their concrete,
rusted iron, painted metal, masonry, linoleum, timber and wall paint are
BORROWED from families that already ship.  A borrowed sheet keeps its bytes
and its own generator; only its *compensation* is re-solved here, because
compensation is a runtime constant fitted to the tints that multiply the
sheet, not a property of the PNG.  Those entries are validated against the
same clamp and 8% brightness rules and recorded in the manifest with the
source sheet they read, so a regeneration upstream is caught by the SHA.

The common measured pipeline comes from ``build-home-textures.py``: seeded
generation, wrap-aware drawing, linear-luminance normalization, compensation
solving, seam/contrast validation, PNG hashing and a tiled contact sheet.
The six grammars here are specific to the climb:

* asphalt - cold, frost-damaged, non-directional mountain blacktop with map
  cracking, cut repairs and washed shoulder grit; it must read the same on a
  hairpin as on a straight, so no wheel bands and no travel direction;
* forest floor - damp humus, fallen needle litter, embedded pebbles and
  patches of loose scree, quiet enough to cover the whole slope;
* wind snow - packed sastrugi banding, wind-carried grit and rare exposed
  stones, low contrast because snow is the brightest thing in the area;
* layered stone - coarse bedding planes with blocky fracture and lichen, the
  one grammar shared by the ridges, the tunnel shell and the boulders;
* conifer needles - dense dark needle clusters with deep gaps, never a leaf
  shape and never a repeating sprig;
* bark and deadwood - vertical ridge-and-furrow bark plus the checked,
  sun-bleached grain of stumps, fallen logs and standing dead trees.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "MountainRoad"
RESOURCES_ROOT = ROOT / "Assets" / "Resources"

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


def wrap_stroke(
    draw: ImageDraw.ImageDraw,
    x: float,
    y: float,
    angle: float,
    length: float,
    fill: int,
    width: int = 1,
) -> None:
    """A short straight mark at an angle, wrapped like every other stamp."""
    home.wrap_line(
        draw,
        (
            x,
            y,
            x + math.cos(angle) * length,
            y + math.sin(angle) * length,
        ),
        fill,
        width=width,
    )


def partition(low: int, high: int, rng) -> list[int]:
    """Cut the sheet into runs of varied width that still sum to 1024.

    Evenly spaced beds and bark plates are the failure mode this exists to
    avoid: at 640x360 a fixed pitch reads as corduroy long before it reads
    as rock or bark. The runs stay whole pixels and their total is exact,
    so the partition boundary at zero is the same boundary at 1024 and the
    pattern still wraps.
    """
    runs: list[int] = []
    remaining = SHEET_SIZE
    while remaining > 0:
        run = rng.randint(low, high)
        if remaining - run < low:
            run = remaining
        runs.append(run)
        remaining -= run

    # A final short run would betray the seam, so fold it back instead.
    if len(runs) > 1 and runs[-1] < low:
        runs[-2] += runs.pop()
    return runs


def wrap_wave(
    draw: ImageDraw.ImageDraw,
    offset: float,
    vertical: bool,
    rng,
    fill: int,
    width: int = 1,
    octaves: tuple[tuple[int, float], ...] = ((1, 9.0), (3, 4.0), (7, 2.0)),
) -> None:
    """A line that crosses the whole sheet and closes on itself.

    A random walk that starts at one edge cannot arrive at the other edge
    where it began, which is exactly how a full-width bedding parting or a
    full-height bark furrow turns into a seam. Summing whole-number
    harmonics of the sheet width makes the wander periodic by construction,
    so the line meets itself across the repeat.
    """
    phases = [rng.uniform(0.0, math.tau) for _ in octaves]
    step = 8
    previous: tuple[float, float] | None = None
    for position in range(0, SHEET_SIZE + step, step):
        wander = offset
        for (harmonic, amplitude), phase in zip(octaves, phases):
            wander += amplitude * math.sin(
                math.tau * harmonic * position / SHEET_SIZE + phase
            )
        point = (
            (wander, float(position))
            if vertical
            else (float(position), wander)
        )
        if previous is not None:
            home.wrap_line(
                draw,
                (previous[0], previous[1], point[0], point[1]),
                fill,
                width=width,
            )
        previous = point


def wrap_crack(
    draw: ImageDraw.ImageDraw,
    x: float,
    y: float,
    angle: float,
    rng,
    segments: int,
    step: tuple[int, int],
    fill: int,
    width: int = 1,
    spread: float = 0.55,
) -> None:
    """A wandering polyline: cracks, roots and partings all use this walk.

    `spread` bounds the wander around the heading it started on, so a long
    crack cannot double back over itself the way an unbounded walk does.
    Structure that has to cross the WHOLE sheet uses `wrap_wave` instead:
    no bounded walk can arrive at the far edge where it began.
    """
    heading = angle
    for _ in range(segments):
        length = rng.randint(*step)
        next_x = x + math.cos(angle) * length
        next_y = y + math.sin(angle) * length
        home.wrap_line(draw, (x, y, next_x, next_y), fill, width=width)
        x, y = next_x, next_y
        angle = heading + max(
            -spread,
            min(spread, angle - heading + rng.uniform(-spread, spread)),
        )


# --------------------------------------------------------------------------
# grammars
# --------------------------------------------------------------------------


def draw_asphalt(base: Image.Image, rng) -> Image.Image:
    """Cold worn blacktop with no travel direction and no wheel bands.

    The road climbs through ten hairpins, so any directional wear would run
    across the carriageway half the time. Everything here is isotropic:
    aggregate, frost map-cracking, cut repairs and washed grit.
    """
    grain = home.fractal_noise(
        ((16, 0.85), (64, 0.60), (256, 0.34), (512, 0.20)),
        rng,
    )
    image = Image.blend(base, grain, 0.30)
    draw = ImageDraw.Draw(image)

    # Exposed aggregate. Facet angles and aspect ratios are independent, so
    # no run of stones ever lines up into a lane.
    for _ in range(5200):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(1, 4)
        angle = rng.uniform(0.0, math.tau)
        stretch = rng.uniform(0.7, 1.3)
        points = []
        for corner in range(4):
            theta = angle + corner * math.tau / 4.0 + rng.uniform(-0.3, 0.3)
            points.append(
                (
                    x + math.cos(theta) * radius * stretch,
                    y + math.sin(theta) * radius,
                )
            )
        wrap_polygon(
            draw,
            points,
            home.BASE + rng.choice((-26, -19, -12, 11, 17, 24)),
        )

    # Frost map-cracking: closed-ish cells rather than long joints, which is
    # what freeze-thaw does to a mountain road that is never resurfaced.
    for _ in range(46):
        wrap_crack(
            draw,
            rng.randrange(SHEET_SIZE),
            rng.randrange(SHEET_SIZE),
            rng.uniform(0.0, math.tau),
            rng,
            rng.randint(5, 11),
            (14, 46),
            home.BASE - rng.randint(30, 46),
            width=rng.randint(1, 2),
        )

    # Cut-and-fill repairs: darker rectangles with a lighter sealed rim.
    for _ in range(9):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(70, 190)
        height = rng.randint(52, 150)
        patch = home.BASE - rng.randint(6, 16)
        wrap_polygon(
            draw,
            [
                (x + rng.randint(-6, 6), y),
                (x + width, y + rng.randint(-7, 7)),
                (x + width + rng.randint(-6, 6), y + height),
                (x, y + height + rng.randint(-7, 7)),
            ],
            patch,
        )
        for _ in range(320):
            px = x + rng.random() * width
            py = y + rng.random() * height
            radius = rng.randint(1, 3)
            home.wrap_ellipse(
                draw,
                (px, py, px + radius, py + radius),
                fill=patch + rng.choice((-17, -11, 10, 15)),
            )

    # Washed shoulder grit: loose pale fines that collect wherever the crown
    # sheds water, i.e. in irregular blots, not along an edge.
    for _ in range(150):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(6, 20)
        height = rng.randint(5, 16)
        wrap_polygon(
            draw,
            [
                (x, y + height * 0.5),
                (x + width * 0.34, y),
                (x + width, y + height * 0.42),
                (x + width * 0.66, y + height),
            ],
            home.BASE + rng.randint(18, 34),
        )

    def damp(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(20):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(90, 300),
                    y + rng.randint(80, 260),
                ),
                fill=rng.randint(104, 119),
            )
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(60, 190),
                    y + rng.randint(55, 170),
                ),
                fill=rng.randint(136, 148),
            )

    return home.soft_overlay(image, 14, damp)


def draw_forest_floor(base: Image.Image, rng) -> Image.Image:
    """Damp humus, needle litter and loose scree under the conifers.

    This sheet covers the entire soil terrain, so its structure has to stay
    quiet at five metres per tile while still giving close range something
    to read: litter reads as direction-free slivers, never as a mat.
    """
    grain = home.fractal_noise(
        ((16, 0.90), (64, 0.62), (128, 0.44), (512, 0.22)),
        rng,
    )
    image = Image.blend(base, grain, 0.34)
    draw = ImageDraw.Draw(image)

    # Fallen needles. Short, thin, every angle, two tones so the litter has
    # depth rather than reading as one felt layer.
    for _ in range(6400):
        wrap_stroke(
            draw,
            rng.randrange(SHEET_SIZE),
            rng.randrange(SHEET_SIZE),
            rng.uniform(0.0, math.tau),
            rng.randint(5, 16),
            home.BASE + rng.choice((-24, -17, -11, 12, 19, 27)),
            width=1,
        )

    # Embedded pebbles and the odd larger stone working up through the soil.
    for _ in range(1300):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(2, 5)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius * rng.uniform(0.8, 1.5), y + radius),
            fill=home.BASE + rng.choice((-20, -14, 15, 22)),
        )

    # Scree pockets: tight clusters of angular chips where the slope has
    # shed rock onto the forest floor.
    for _ in range(34):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        pocket = rng.randint(28, 78)
        for _ in range(rng.randint(30, 80)):
            x = cx + rng.gauss(0.0, pocket * 0.5)
            y = cy + rng.gauss(0.0, pocket * 0.5)
            size = rng.randint(3, 8)
            angle = rng.uniform(0.0, math.tau)
            points = []
            for corner in range(3):
                theta = angle + corner * math.tau / 3.0
                points.append(
                    (
                        x + math.cos(theta) * size,
                        y + math.sin(theta) * size * rng.uniform(0.7, 1.2),
                    )
                )
            wrap_polygon(
                draw,
                points,
                home.BASE + rng.choice((-22, -15, 18, 26)),
            )

    # A few exposed roots crossing the litter.
    for _ in range(16):
        wrap_crack(
            draw,
            rng.randrange(SHEET_SIZE),
            rng.randrange(SHEET_SIZE),
            rng.uniform(0.0, math.tau),
            rng,
            rng.randint(5, 10),
            (22, 62),
            home.BASE + rng.choice((-21, 17)),
            width=rng.randint(2, 4),
        )

    def damp(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(24):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(110, 340),
                    y + rng.randint(95, 300),
                ),
                fill=rng.randint(103, 118),
            )
        for _ in range(14):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(70, 210),
                    y + rng.randint(60, 185),
                ),
                fill=rng.randint(137, 150),
            )

    return home.soft_overlay(image, 16, damp)


def draw_wind_snow(base: Image.Image, rng) -> Image.Image:
    """Wind-packed snow with carried grit and rare exposed stone.

    Snow is the brightest surface in the area and covers the whole upper
    slope, so the structure is deliberately soft: broad sastrugi banding at
    one prevailing wind angle, and dirt that gathers in the troughs.
    """
    grain = home.fractal_noise(
        ((8, 1.0), (32, 0.55), (128, 0.30), (512, 0.14)),
        rng,
    )
    image = Image.blend(base, grain, 0.22)
    draw = ImageDraw.Draw(image)

    # Sastrugi: long shallow drift ridges combed by one prevailing wind.
    # The pitch divides the sheet so the banding wraps.
    wind = math.radians(24.0)
    for band in range(64):
        y = band * (SHEET_SIZE / 64)
        length = rng.randint(240, 620)
        x = rng.randrange(SHEET_SIZE)
        tone = home.BASE + rng.choice((-13, -9, 9, 14))
        wrap_stroke(
            draw,
            x,
            y + rng.randint(-6, 6),
            wind + rng.uniform(-0.12, 0.12),
            length,
            tone,
            width=rng.randint(5, 16),
        )

    # Wind-carried grit, dark and fine, gathering in the lee of the ridges.
    for _ in range(4200):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(1, 3)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius, y + radius),
            fill=home.BASE - rng.randint(14, 34),
        )

    # Rare stones breaking the crust. Few, because a field of them would
    # read as scree rather than as snow.
    for _ in range(58):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(5, 14)
        angle = rng.uniform(0.0, math.tau)
        points = []
        for corner in range(5):
            theta = angle + corner * math.tau / 5.0
            reach = size * rng.uniform(0.65, 1.15)
            points.append(
                (x + math.cos(theta) * reach, y + math.sin(theta) * reach)
            )
        wrap_polygon(draw, points, home.BASE - rng.randint(30, 52))

    def drift(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(18):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(150, 430),
                    y + rng.randint(70, 190),
                ),
                fill=rng.randint(133, 144),
            )
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(120, 360),
                    y + rng.randint(60, 160),
                ),
                fill=rng.randint(110, 122),
            )

    return home.soft_overlay(image, 18, drift)


def draw_layered_stone(base: Image.Image, rng) -> Image.Image:
    """Coarse bedded mountain rock: the ridges, the tunnel and the boulders.

    Six metres per tile, so the bedding has to be legible at ridge scale.
    Beds run horizontally in sheet space; the runtime picks the projection
    plane per face, which is what keeps a boulder from looking combed.
    """
    grain = home.fractal_noise(
        ((16, 0.85), (64, 0.58), (256, 0.36), (512, 0.20)),
        rng,
    )
    image = Image.blend(base, grain, 0.30)
    draw = ImageDraw.Draw(image)

    # Bedding planes of uneven thickness, the way sediment actually lays
    # down. Their run lengths sum to the sheet, so the banding wraps.
    y = 0
    for bed_height in partition(58, 190, rng):
        tone = home.BASE + rng.choice((-19, -12, -5, 7, 14, 21))
        home.wrap_rect(draw, (0, y, SHEET_SIZE, y + bed_height), tone)

        # Blocky fracture inside the bed: irregular quadrilaterals with a
        # lit upper arris and a dark undercut.
        cursor = rng.randrange(SHEET_SIZE)
        while cursor < SHEET_SIZE * 2:
            block = rng.randint(74, 210)
            top = y + rng.randint(-7, 7)
            bottom = y + bed_height + rng.randint(-7, 7)
            face = tone + rng.choice((-11, -6, 4, 9, 15))
            wrap_polygon(
                draw,
                [
                    (cursor + 4, top + 4),
                    (cursor + block - 4, top + rng.randint(-3, 6)),
                    (cursor + block - 5, bottom - 4),
                    (cursor + 5, bottom + rng.randint(-6, 3)),
                ],
                face,
            )
            home.wrap_line(
                draw,
                (cursor + 5, top + 5, cursor + block - 5, top + 5),
                face + rng.randint(9, 18),
                width=rng.randint(2, 4),
            )
            home.wrap_line(
                draw,
                (cursor + 5, bottom - 5, cursor + block - 5, bottom - 5),
                face - rng.randint(16, 28),
                width=rng.randint(2, 5),
            )
            for _ in range(150):
                px = cursor + rng.random() * block
                py = y + rng.random() * bed_height
                radius = rng.randint(1, 4)
                home.wrap_ellipse(
                    draw,
                    (px, py, px + radius, py + radius),
                    fill=face + rng.choice((-19, -12, 10, 16)),
                )
            cursor += block

        # The parting between beds wanders, but periodically, so it is the
        # same line where the sheet repeats.
        wrap_wave(
            draw,
            y,
            False,
            rng,
            tone - rng.randint(20, 32),
            width=rng.randint(2, 5),
        )
        y += bed_height

    # Cross joints cutting the beds, so the rock breaks in two directions.
    for _ in range(30):
        wrap_crack(
            draw,
            rng.randrange(SHEET_SIZE),
            rng.randrange(SHEET_SIZE),
            rng.uniform(-1.9, -1.25),
            rng,
            rng.randint(4, 9),
            (26, 78),
            home.BASE - rng.randint(28, 44),
            width=rng.randint(1, 3),
        )

    def lichen(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(26):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(40, 165),
                    y + rng.randint(34, 140),
                ),
                fill=rng.randint(136, 150),
            )
        for _ in range(20):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(30, 130),
                    y + rng.randint(90, 300),
                ),
                fill=rng.randint(104, 118),
            )

    return home.soft_overlay(image, 11, lichen)


def draw_conifer_needles(base: Image.Image, rng) -> Image.Image:
    """Dark uneven needle mass for the crowns.

    Clusters radiate from scattered centres and are separated by genuinely
    dark gaps, because a crown lit only by cold sky reads as depth, not as
    a leaf pattern. There is no sprig shape to repeat.
    """
    grain = home.fractal_noise(
        ((16, 0.90), (64, 0.66), (256, 0.40), (512, 0.24)),
        rng,
    )
    image = Image.blend(base, grain, 0.36)
    draw = ImageDraw.Draw(image)

    # Deep gaps first, so needles drawn after can hang over them. They are
    # ragged polygons, not discs: a disc reads as a hole punched in a mat.
    for _ in range(300):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        reach = rng.randint(9, 30)
        angle = rng.uniform(0.0, math.tau)
        points = []
        for corner in range(7):
            theta = angle + corner * math.tau / 7.0
            radius = reach * rng.uniform(0.35, 1.35)
            points.append(
                (
                    x + math.cos(theta) * radius,
                    y + math.sin(theta) * radius * rng.uniform(0.7, 1.3),
                )
            )
        wrap_polygon(draw, points, home.BASE - rng.randint(32, 56))

    # Needle clusters. Each centre throws a fan of strokes; overlapping
    # fans build the mass without any of them being separable.
    for _ in range(520):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        heading = rng.uniform(0.0, math.tau)
        tone = home.BASE + rng.choice((-27, -20, -13, 13, 20, 29))
        for _ in range(rng.randint(10, 22)):
            wrap_stroke(
                draw,
                cx + rng.gauss(0.0, 7.0),
                cy + rng.gauss(0.0, 7.0),
                heading + rng.uniform(-0.95, 0.95),
                rng.randint(7, 21),
                tone + rng.randint(-6, 6),
                width=1,
            )

    # A scatter of brighter frost-caught needles keeps the mass from
    # flattening into one value under the composite.
    for _ in range(3000):
        wrap_stroke(
            draw,
            rng.randrange(SHEET_SIZE),
            rng.randrange(SHEET_SIZE),
            rng.uniform(0.0, math.tau),
            rng.randint(4, 12),
            home.BASE + rng.randint(22, 38),
            width=1,
        )

    def depth(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(30):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(60, 230),
                    y + rng.randint(55, 210),
                ),
                fill=rng.randint(100, 116),
            )
        for _ in range(16):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(50, 175),
                    y + rng.randint(45, 160),
                ),
                fill=rng.randint(139, 152),
            )

    return home.soft_overlay(image, 9, depth)


def draw_bark_deadwood(base: Image.Image, rng) -> Image.Image:
    """Ridge-and-furrow conifer bark that also has to read as deadwood.

    Trunks, fallen logs, cut stumps, standing dead trees and the utility
    poles all share this sheet, so the vertical furrows carry the bark and
    the drying checks carry the bleached wood.
    """
    grain = home.fractal_noise(
        ((16, 0.80), (64, 0.60), (256, 0.34), (512, 0.20)),
        rng,
    )
    image = Image.blend(base, grain, 0.30)
    draw = ImageDraw.Draw(image)

    # Vertical bark plates in columns of uneven width. An even pitch here
    # is the difference between bark and corduroy, so the widths vary by
    # more than three to one and only their total is fixed.
    x = 0
    for column_width in partition(18, 64, rng):
        tone = home.BASE + rng.choice((-21, -14, -6, 6, 13, 20))
        cursor = rng.randrange(SHEET_SIZE)
        while cursor < SHEET_SIZE * 2:
            plate = rng.randint(46, 165)
            drift = rng.randint(-6, 6)
            face = tone + rng.choice((-9, -4, 5, 11))
            wrap_polygon(
                draw,
                [
                    (x + 2 + drift, cursor + 3),
                    (x + column_width - 2, cursor + rng.randint(-3, 5)),
                    (x + column_width - 3 - drift, cursor + plate - 3),
                    (x + 3, cursor + plate + rng.randint(-5, 3)),
                ],
                face,
            )
            home.wrap_line(
                draw,
                (
                    x + 2 + drift,
                    cursor + 3,
                    x + 3,
                    cursor + plate + 2,
                ),
                face - rng.randint(17, 30),
                width=rng.randint(2, 4),
            )
            cursor += plate

        # The furrow between plates wanders across its own height rather
        # than running dead straight, which is what stops the columns from
        # lining up into a grating.
        wrap_wave(
            draw,
            x,
            True,
            rng,
            tone - rng.randint(20, 34),
            width=rng.randint(2, 4),
            octaves=((1, 5.0), (2, 3.0), (5, 1.5)),
        )
        x += column_width

    # Drying checks: long near-vertical splits, the deadwood half of the
    # sheet. They cross plate boundaries, which bark furrows never do.
    for _ in range(54):
        wrap_crack(
            draw,
            rng.randrange(SHEET_SIZE),
            rng.randrange(SHEET_SIZE),
            rng.uniform(1.30, 1.84),
            rng,
            rng.randint(6, 13),
            (26, 84),
            home.BASE - rng.randint(26, 44),
            width=rng.randint(1, 3),
        )

    # Knots and torn fibre, so a stump end is not a blank disc.
    for _ in range(120):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(4, 13)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius, y + radius * rng.uniform(1.1, 1.9)),
            fill=home.BASE - rng.randint(22, 38),
        )
        home.wrap_ellipse(
            draw,
            (
                x + radius * 0.3,
                y + radius * 0.4,
                x + radius * 0.75,
                y + radius * 1.1,
            ),
            fill=home.BASE + rng.randint(10, 22),
        )

    def bleach(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(20):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(30, 110),
                    y + rng.randint(140, 400),
                ),
                fill=rng.randint(137, 151),
            )
        for _ in range(16):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            layer_draw.ellipse(
                (
                    x,
                    y,
                    x + rng.randint(26, 96),
                    y + rng.randint(120, 340),
                ),
                fill=rng.randint(102, 117),
            )

    return home.soft_overlay(image, 10, bleach)


home.GRAMMARS.update(
    {
        "mountain_road_asphalt": draw_asphalt,
        "mountain_road_forest_floor": draw_forest_floor,
        "mountain_road_wind_snow": draw_wind_snow,
        "mountain_road_layered_stone": draw_layered_stone,
        "mountain_road_conifer_needles": draw_conifer_needles,
        "mountain_road_bark_deadwood": draw_bark_deadwood,
    }
)


# --------------------------------------------------------------------------
# specs
# --------------------------------------------------------------------------


MOUNTAIN_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="MountainRoadAsphaltAlbedo",
        grammar="mountain_road_asphalt",
        seed=0x4D524153,
        cast=(0.985, 1.000, 1.020),
        mean_target=0.50,
        meters_per_tile=3.5,
        smoothness=0.045,
        metallic=0.0,
        tints=(
            ("MountainRoadWorldBuilder.Road", (0.115, 0.125, 0.118)),
            (
                "MountainRoadWorldBuilder.TerminalApron",
                (0.075, 0.084, 0.080),
            ),
        ),
        contrast_floor=52,
    ),
    home.HomeSheetSpec(
        key="MountainRoadForestFloorAlbedo",
        grammar="mountain_road_forest_floor",
        seed=0x4D524646,
        cast=(1.020, 1.000, 0.960),
        mean_target=0.50,
        meters_per_tile=5.0,
        smoothness=0.030,
        metallic=0.0,
        tints=(
            ("MountainRoadWorldBuilder.Soil", (0.170, 0.180, 0.155)),
        ),
        contrast_floor=46,
    ),
    home.HomeSheetSpec(
        key="MountainRoadSnowAlbedo",
        grammar="mountain_road_wind_snow",
        seed=0x4D52534E,
        cast=(0.990, 1.000, 1.015),
        mean_target=0.50,
        meters_per_tile=5.0,
        smoothness=0.050,
        metallic=0.0,
        tints=(
            ("MountainRoadWorldBuilder.Snow", (0.630, 0.665, 0.650)),
            (
                "MountainRoadWorldBuilder.FarSnowyRidge",
                (0.470, 0.520, 0.525),
            ),
        ),
        contrast_floor=34,
    ),
    home.HomeSheetSpec(
        key="MountainRoadStoneAlbedo",
        grammar="mountain_road_layered_stone",
        seed=0x4D525354,
        cast=(1.000, 1.010, 0.995),
        mean_target=0.52,
        meters_per_tile=6.0,
        smoothness=0.025,
        metallic=0.0,
        tints=(
            ("MountainRoadWorldBuilder.Rock", (0.245, 0.265, 0.245)),
            (
                "MountainRoadWorldBuilder.TunnelRock",
                (0.115, 0.130, 0.120),
            ),
            (
                "MountainRoadWorldBuilder.MiddleRidge",
                (0.190, 0.215, 0.205),
            ),
        ),
        contrast_floor=54,
    ),
    home.HomeSheetSpec(
        key="MountainRoadNeedleAlbedo",
        grammar="mountain_road_conifer_needles",
        seed=0x4D524E44,
        cast=(0.975, 1.015, 0.990),
        mean_target=0.52,
        meters_per_tile=2.5,
        smoothness=0.020,
        metallic=0.0,
        tints=(
            (
                "MountainRoadWorldBuilder.PhysicalCrown",
                (0.115, 0.165, 0.125),
            ),
            ("MountainRoadWorldBuilder.MidCrown", (0.095, 0.140, 0.115)),
            ("MountainRoadWorldBuilder.FarCrown", (0.075, 0.105, 0.090)),
        ),
        contrast_floor=56,
    ),
    home.HomeSheetSpec(
        key="MountainRoadBarkAlbedo",
        grammar="mountain_road_bark_deadwood",
        seed=0x4D52424B,
        cast=(1.020, 1.000, 0.965),
        mean_target=0.50,
        meters_per_tile=2.5,
        smoothness=0.040,
        metallic=0.0,
        tints=(
            ("MountainRoadWorldBuilder.Trunk", (0.190, 0.165, 0.135)),
            ("MountainRoadWorldBuilder.DeadWood", (0.270, 0.250, 0.210)),
        ),
        contrast_floor=54,
    ),
)


@dataclass(frozen=True)
class BorrowedSheetSpec:
    """One packaged sheet the mountain road reads without reprinting it.

    `source_key` names the PNG under `Assets/Resources`; `source_manifest`
    is the family contract that owns its bytes. The compensation is solved
    HERE against `tints`, because a compensation constant fits the tints
    that multiply a sheet, not the sheet itself - the same masonry that
    serves a city retaining wall at one tint has to serve a cafe's brick
    gable at another.
    """

    kind: str
    source_key: str
    source_manifest: str
    resource_path: str
    meters_per_tile: float
    smoothness: float
    metallic: float
    tints: tuple[tuple[str, tuple[float, float, float]], ...]


BORROWED_SHEET_SPECS: tuple[BorrowedSheetSpec, ...] = (
    BorrowedSheetSpec(
        kind="Concrete",
        source_key="CityFringeConcreteAlbedo",
        source_manifest="ArtSource/City/fringe-textures.json",
        resource_path="Textures/CityFringeConcreteAlbedo",
        meters_per_tile=3.0,
        smoothness=0.055,
        metallic=0.0,
        tints=(
            (
                "MountainRoadBridgeWorldBuilder.AgedConcrete",
                (0.285, 0.300, 0.285),
            ),
            (
                "MountainRoadBridgeWorldBuilder.DarkConcrete",
                (0.225, 0.240, 0.230),
            ),
            (
                "MountainCablewayWorldBuilder.Concrete",
                (0.230, 0.245, 0.225),
            ),
        ),
    ),
    BorrowedSheetSpec(
        kind="RustedIron",
        source_key="CityRiverIronAlbedo",
        source_manifest="ArtSource/City/river-textures.json",
        resource_path="Textures/CityRiverIronAlbedo",
        meters_per_tile=1.2,
        smoothness=0.180,
        metallic=0.200,
        tints=(
            ("MountainRoadWorldBuilder.Iron", (0.200, 0.230, 0.220)),
            ("MountainRoadWorldBuilder.Rust", (0.330, 0.245, 0.170)),
            (
                "MountainRoadBridgeWorldBuilder.OxidizedSteel",
                (0.205, 0.225, 0.215),
            ),
            (
                "MountainRoadBridgeWorldBuilder.RailSteel",
                (0.245, 0.275, 0.260),
            ),
            ("MountainCablewayWorldBuilder.Rust", (0.370, 0.245, 0.150)),
        ),
    ),
    BorrowedSheetSpec(
        kind="PaintedMetal",
        source_key="CityParkPaintedMetalAlbedo",
        source_manifest="ArtSource/City/park-textures.json",
        resource_path="Textures/CityParkPaintedMetalAlbedo",
        meters_per_tile=1.2,
        smoothness=0.160,
        metallic=0.150,
        tints=(
            (
                "MountainCablewayWorldBuilder.DarkSteel",
                (0.105, 0.145, 0.135),
            ),
            (
                "MountainCablewayWorldBuilder.GreenSteel",
                (0.160, 0.225, 0.190),
            ),
            (
                "MountainCablewayWorldBuilder.CabinWarm",
                (0.310, 0.140, 0.110),
            ),
            (
                "MountainCablewayWorldBuilder.CabinCool",
                (0.105, 0.230, 0.200),
            ),
            (
                "MountainCablewayWorldBuilder.Cable",
                (0.045, 0.055, 0.052),
            ),
            (
                "MountainRoadCafeWorldBuilder.StoolMetal",
                (0.140, 0.160, 0.145),
            ),
            (
                "MountainRoadCafeWorldBuilder.ApplianceDark",
                (0.200, 0.235, 0.215),
            ),
            (
                "MountainRoadWorldBuilder.UtilityCabinet",
                (0.200, 0.265, 0.240),
            ),
        ),
    ),
    BorrowedSheetSpec(
        kind="PaleEnamel",
        source_key="CityParkPaintedMetalAlbedo",
        source_manifest="ArtSource/City/park-textures.json",
        resource_path="Textures/CityParkPaintedMetalAlbedo",
        meters_per_tile=1.2,
        smoothness=0.160,
        metallic=0.150,
        tints=(
            (
                "MountainCablewayWorldBuilder.FadedSign",
                (0.560, 0.520, 0.390),
            ),
            (
                "MountainRoadCafeWorldBuilder.Appliance",
                (0.510, 0.530, 0.430),
            ),
            (
                "MountainRoadCafeWorldBuilder.RefrigeratorDoor",
                (0.570, 0.580, 0.470),
            ),
            (
                "MountainRoadCafeWorldBuilder.BrassHandle",
                (0.550, 0.380, 0.120),
            ),
            (
                "MountainRoadCafeWorldBuilder.CoffeeCup",
                (0.700, 0.680, 0.550),
            ),
            (
                "MountainRoadWorldBuilder.ConvexMirror",
                (0.480, 0.540, 0.520),
            ),
            (
                "MountainRoadWorldBuilder.SnowPole",
                (0.620, 0.220, 0.180),
            ),
            (
                "MountainRoadWorldBuilder.SnowPoleBand",
                (0.630, 0.665, 0.650),
            ),
        ),
    ),
    BorrowedSheetSpec(
        kind="Masonry",
        source_key="CityFringeMasonryAlbedo",
        source_manifest="ArtSource/City/fringe-textures.json",
        resource_path="Textures/CityFringeMasonryAlbedo",
        meters_per_tile=2.4,
        smoothness=0.035,
        metallic=0.0,
        tints=(
            (
                "MountainRoadCafeWorldBuilder.Brick",
                (0.290, 0.105, 0.065),
            ),
        ),
    ),
    BorrowedSheetSpec(
        kind="Linoleum",
        source_key="SupermarketLinoleumAlbedo",
        source_manifest="ArtSource/Supermarket/supermarket-textures.json",
        resource_path="Supermarket/Textures/SupermarketLinoleumAlbedo",
        meters_per_tile=2.4,
        smoothness=0.160,
        metallic=0.0,
        tints=(
            (
                "MountainRoadCafeWorldBuilder.FloorLinoleum",
                (0.160, 0.320, 0.270),
            ),
        ),
    ),
    BorrowedSheetSpec(
        kind="Timber",
        source_key="CityParkTimberAlbedo",
        source_manifest="ArtSource/City/park-textures.json",
        resource_path="Textures/CityParkTimberAlbedo",
        meters_per_tile=1.4,
        smoothness=0.080,
        metallic=0.0,
        tints=(
            (
                "MountainRoadCafeWorldBuilder.CounterWood",
                (0.340, 0.105, 0.045),
            ),
            (
                "MountainRoadCafeWorldBuilder.CounterTop",
                (0.460, 0.160, 0.060),
            ),
            (
                "MountainRoadCafeWorldBuilder.StoolSeat",
                (0.410, 0.120, 0.055),
            ),
        ),
    ),
    BorrowedSheetSpec(
        kind="WallPaint",
        source_key="SupermarketWallPaintAlbedo",
        source_manifest="ArtSource/Supermarket/supermarket-textures.json",
        resource_path="Supermarket/Textures/SupermarketWallPaintAlbedo",
        meters_per_tile=2.6,
        smoothness=0.050,
        metallic=0.0,
        tints=(
            (
                "MountainRoadCafeWorldBuilder.Facade",
                (0.035, 0.075, 0.068),
            ),
            (
                "MountainRoadCafeWorldBuilder.FacadeTrim",
                (0.045, 0.125, 0.105),
            ),
        ),
    ),
    BorrowedSheetSpec(
        kind="InteriorPaint",
        source_key="SupermarketWallPaintAlbedo",
        source_manifest="ArtSource/Supermarket/supermarket-textures.json",
        resource_path="Supermarket/Textures/SupermarketWallPaintAlbedo",
        meters_per_tile=2.6,
        smoothness=0.050,
        metallic=0.0,
        tints=(
            (
                "MountainRoadCafeWorldBuilder.InteriorCream",
                (0.720, 0.620, 0.370),
            ),
        ),
    ),
)


def build_mountain_sheet(
    spec: home.HomeSheetSpec,
) -> tuple[Image.Image, dict]:
    """Build one sheet, holding the two terrain-wide fields quieter.

    The shared macro pass gives close-read materials strong relief. The
    forest floor and the wind snow each cover the whole 76-metre envelope,
    so their finished value range is compressed before the usual measured
    normalization and compensation solve.
    """
    image, record = home.build_sheet(spec)
    if spec.grammar not in (
        "mountain_road_forest_floor",
        "mountain_road_wind_snow",
    ):
        return image, record

    compression = (
        0.72 if spec.grammar == "mountain_road_forest_floor" else 0.62
    )
    image = ImageEnhance.Contrast(image).enhance(compression)
    image, exposure = home.normalise_luminance(image, spec.mean_target)
    mean = home.mean_linear_luminance(image)
    compensation, brightness_error = home.solve_compensation(mean, spec)
    record["meanLinearLuminance"] = round(mean, 6)
    record["albedoCompensation"] = round(compensation, 6)
    record["brightnessError"] = round(brightness_error, 6)
    record["exposure"] = round(record["exposure"] * exposure, 6)
    return image, record


def flatten_tints(
    tints: tuple[tuple[str, tuple[float, float, float]], ...],
) -> list[float]:
    """Every tint channel this sheet serves, as one sorted flat list.

    Unity's JsonUtility cannot read the manifest's per-builder tint map, so
    the EditMode contract test re-checks the clamp and brightness rules
    against this list instead.
    """
    return sorted(
        {round(channel, 4) for _, tint in tints for channel in tint}
    )


def measure_borrowed(spec: BorrowedSheetSpec) -> dict:
    """Re-solve one packaged sheet's compensation for the mountain tints."""
    path = RESOURCES_ROOT / f"{spec.resource_path}.png"
    if not path.is_file():
        raise FileNotFoundError(
            f"{spec.kind} borrows '{spec.resource_path}', which is not at "
            f"{path}."
        )

    image = Image.open(path).convert("RGB")
    if image.size != (SHEET_SIZE, SHEET_SIZE):
        raise ValueError(
            f"{spec.source_key} is {image.size}; a borrowed source must be "
            f"the same {SHEET_SIZE}px sheet the families print."
        )

    mean = home.mean_linear_luminance(image)
    solver_spec = home.HomeSheetSpec(
        key=spec.kind,
        grammar="borrowed",
        seed=0,
        cast=(1.0, 1.0, 1.0),
        mean_target=mean,
        meters_per_tile=spec.meters_per_tile,
        smoothness=spec.smoothness,
        metallic=spec.metallic,
        tints=spec.tints,
    )
    compensation, brightness_error = home.solve_compensation(
        mean,
        solver_spec,
    )

    brightest = max(
        channel for _, tint in spec.tints for channel in tint
    )
    if brightest * compensation > 1.0 + 1e-6:
        raise ValueError(
            f"{spec.kind} borrowed from {spec.source_key}: compensation "
            f"{compensation:.4f} would drive a builder tint to "
            f"{brightest * compensation:.4f} and clamp."
        )

    if brightness_error > home.BRIGHTNESS_ERROR_LIMIT:
        raise ValueError(
            f"{spec.kind} cannot borrow {spec.source_key}: it would shift "
            f"surface brightness by {brightness_error * 100:.1f}%, above "
            f"the {home.BRIGHTNESS_ERROR_LIMIT * 100:.0f}% allowed. Pick a "
            f"source sheet whose mean linear luminance is closer to what "
            f"these tints need, or split the kind."
        )

    return {
        "key": spec.kind,
        "grammar": "borrowed",
        "borrowedFrom": spec.source_key,
        "sourceManifest": spec.source_manifest,
        "resourcePath": spec.resource_path,
        "meanLinearLuminance": round(mean, 6),
        "albedoCompensation": round(compensation, 6),
        "brightnessError": round(brightness_error, 6),
        "metersPerTile": spec.meters_per_tile,
        "smoothness": spec.smoothness,
        "metallic": spec.metallic,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper(),
        "tints": {
            name: [round(value, 4) for value in tint]
            for name, tint in spec.tints
        },
        "tintValues": flatten_tints(spec.tints),
    }


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
    for spec in MOUNTAIN_SHEET_SPECS:
        image, record = build_mountain_sheet(spec)
        record["resourcePath"] = f"Textures/{spec.key}"
        record["tintValues"] = flatten_tints(spec.tints)
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

    borrowed: list[dict] = []
    for borrowed_spec in BORROWED_SHEET_SPECS:
        borrowed_record = measure_borrowed(borrowed_spec)
        borrowed.append(borrowed_record)
        print(
            f"Borrowed {borrowed_spec.kind} <- "
            f"{borrowed_spec.source_key} "
            f"mean={borrowed_record['meanLinearLuminance']:.4f} "
            f"compensation={borrowed_record['albedoCompensation']:.4f} "
            f"error={borrowed_record['brightnessError'] * 100:.1f}% "
            f"pitch={borrowed_spec.meters_per_tile}m"
        )

    if args.verify:
        print(
            f"Validated {len(records)} mountain-road sheets and "
            f"{len(borrowed)} borrowed contracts; nothing written."
        )
        return

    manifest = {
        "sheetSize": SHEET_SIZE,
        "runtimeImportSize": 512,
        "meanLuminanceTolerance": home.MEAN_LUMINANCE_TOLERANCE,
        "brightnessErrorLimit": home.BRIGHTNESS_ERROR_LIMIT,
        "tintChannelFloor": home.TINT_CHANNEL_FLOOR,
        "sheets": records,
        "borrowed": borrowed,
    }
    manifest_path = args.art_source / "mountain-road-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "mountain-road-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
