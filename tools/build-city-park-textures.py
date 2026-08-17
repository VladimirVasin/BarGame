#!/usr/bin/env python3
"""Build the deterministic surface albedos for the Central Park zone.

Six seamless 1024x1024 sheets, imported by Unity at 512 and applied by
`CityParkSurfaceAppearance`: the park's authored flat colours (brightened
by the solved per-sheet compensation) multiply the sheet in linear space,
exactly like the home/POI/cemetery pipelines.

Park geometry is combined meshes, so the sheets ride baked UVs rather
than a per-renderer UV transform. The ground, the paths and the plaza
discs carry world-planar XZ UVs at their own metre pitch; the trees,
benches and hedges carry box-projected world UVs, so a hedge run reads
as leaf mass along its whole length instead of one stretched streak.

The measured contract - linear luminance rule, wrap-by-construction
drawing, compensation solving and validation - is imported from
`build-home-textures.py`; this script adds six grammars, transcribing
the art bible's park materials (dark grass, trodden earth, sand-stone
path, dirty stone, dark wood):

* lawn     - dark turf broken by bald trodden earth on the shortcuts.
* path     - the sand-stone walk: fine grit, embedded pebbles, ruts.
* plaza    - dirty stone slabs on a 4x4 joint grid, chipped and stained.
* bark     - vertical fibrous ridges, knots and one mossy side.
* foliage  - overlapping leaf clumps over the dark gaps between them.
* timber   - bench wood: long grain under flaked, faded paint.
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


# --------------------------------------------------------------------------
# wrap-aware shapes the shared module does not carry
# --------------------------------------------------------------------------


def wrap_polygon(
    draw: ImageDraw.ImageDraw,
    points: list[tuple[float, float]],
    fill: int,
) -> None:
    """The wrapping counterpart of the shared rect/line/ellipse stamps."""
    for dx in home.OFFSETS:
        for dy in home.OFFSETS:
            draw.polygon(
                [(x + dx, y + dy) for x, y in points],
                fill=fill,
            )


def wrap_leaf(
    draw: ImageDraw.ImageDraw,
    center: tuple[float, float],
    length: float,
    width: float,
    angle: float,
    fill: int,
) -> None:
    """One pointed leaf: an almond with a tip, a shoulder and a stem."""
    cosine = math.cos(angle)
    sine = math.sin(angle)
    outline = (
        (length * 0.5, 0.0),
        (length * 0.12, -width * 0.5),
        (-length * 0.28, -width * 0.36),
        (-length * 0.5, 0.0),
        (-length * 0.28, width * 0.36),
        (length * 0.12, width * 0.5),
    )
    wrap_polygon(
        draw,
        [
            (
                center[0] + x * cosine - y * sine,
                center[1] + x * sine + y * cosine,
            )
            for x, y in outline
        ],
        fill,
    )


def wrap_flake(
    draw: ImageDraw.ImageDraw,
    center: tuple[float, float],
    radius: float,
    rng,
    fill: int,
    lifted_edge: int,
) -> None:
    """One paint flake: a jagged blob, never a circle.

    The lifted edge is the same outline grown by a pixel and a half, so
    the dark rim actually follows the flake instead of being a second
    unrelated shape underneath it.
    """
    corners = rng.randint(6, 10)
    reaches = [rng.uniform(0.45, 1.0) for _ in range(corners)]

    def outline(scale: float) -> list[tuple[float, float]]:
        points = []
        for index, reach in enumerate(reaches):
            angle = math.tau * index / corners
            span = radius * reach + scale
            points.append(
                (
                    center[0] + math.cos(angle) * span * 1.8,
                    center[1] + math.sin(angle) * span,
                )
            )
        return points

    wrap_polygon(draw, outline(1.5), lifted_edge)
    wrap_polygon(draw, outline(0.0), fill)


# --------------------------------------------------------------------------
# park grammars
# --------------------------------------------------------------------------


def draw_park_lawn(base: Image.Image, rng) -> Image.Image:
    """Dark turf worn through to earth wherever people cut corners."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((16, 0.8), (64, 0.6), (256, 0.45)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Bald patches first: the trodden earth the blades grow back over.
    for _ in range(34):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(60, 180), y + rng.randint(45, 130)),
            fill=home.BASE - rng.randint(12, 24),
        )

    # The blade field: short strokes leaning every which way. Dense and
    # contrasted enough to read as turf at a metre and as tone at ten -
    # under the shared macro noise a timid blade disappears into cloud.
    for _ in range(8400):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(-5, 5), y - rng.randint(7, 17)),
            home.BASE + rng.choice((-38, -27, -19, 20, 29, 37)),
            width=1,
        )

    # Tufts: clumps that survived the mowing, a touch paler.
    for _ in range(220):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        for _ in range(rng.randint(5, 11)):
            x = cx + rng.randint(-11, 11)
            y = cy + rng.randint(-9, 9)
            home.wrap_line(
                draw,
                (x, y, x + rng.randint(-5, 5), y - rng.randint(10, 20)),
                home.BASE + rng.randint(22, 34),
                width=1,
            )

    # Dropped leaves and seed heads: small warm flecks over the green.
    for _ in range(420):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(3, 7), y + rng.randint(2, 5)),
            fill=home.BASE + rng.choice((-28, -20, 20, 28)),
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(11):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(120, 320), y + rng.randint(90, 220)),
                fill=rng.randint(108, 118),
            )
        for _ in range(8):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(80, 200), y + rng.randint(60, 150)),
                fill=rng.randint(136, 144),
            )

    return home.soft_overlay(image, 13, weathering)


def draw_park_path(base: Image.Image, rng) -> Image.Image:
    """The sand-stone walk: grit, embedded pebbles and worn ruts."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((32, 0.7), (128, 0.6), (512, 0.4)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Ruts: long shallow depressions where the walking concentrates.
    for _ in range(16):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        tone = home.BASE - rng.randint(10, 18)
        for _ in range(rng.randint(6, 12)):
            nx = x + rng.uniform(70.0, 160.0)
            ny = y + rng.uniform(-22.0, 22.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                tone,
                width=rng.randint(6, 16),
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # The grit bed: thousands of sand specks a step either side of base.
    for _ in range(9000):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        side = rng.randint(1, 3)
        home.wrap_rect(
            draw,
            (x, y, x + side, y + side),
            home.BASE + rng.choice((-20, -13, 12, 17, 23)),
        )

    # Pebbles pressed into the surface, each with its own shadow.
    for _ in range(700):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(5, 12)
        height = rng.randint(4, 9)
        home.wrap_ellipse(
            draw,
            (x, y + 1, x + width, y + height + 1),
            fill=home.BASE - rng.randint(20, 30),
        )
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + height),
            fill=home.BASE + rng.choice((-8, 14, 20, 26)),
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(10):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(140, 340), y + rng.randint(70, 170)),
                fill=rng.randint(112, 122),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 220), y + rng.randint(60, 150)),
                fill=rng.randint(134, 142),
            )

    return home.soft_overlay(image, 12, weathering)


PLAZA_SLABS = 4
PLAZA_PITCH = SHEET_SIZE // PLAZA_SLABS


def draw_park_plaza(base: Image.Image, rng) -> Image.Image:
    """Dirty stone slabs: a 4x4 joint grid, chipped corners, stains."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.65), (256, 0.55), (512, 0.35)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Each slab gets its own tone, so the grid reads as laid stone
    # rather than one field scored with lines.
    for row in range(PLAZA_SLABS):
        for column in range(PLAZA_SLABS):
            x = column * PLAZA_PITCH
            y = row * PLAZA_PITCH
            home.wrap_rect(
                draw,
                (x + 2, y + 2, x + PLAZA_PITCH - 3, y + PLAZA_PITCH - 3),
                home.BASE + rng.choice((-14, -8, -3, 6, 11)),
            )

    # Joints: dark mortar gaps, and the pale worn lip beside them.
    for index in range(PLAZA_SLABS):
        offset = index * PLAZA_PITCH
        home.wrap_rect(
            draw,
            (offset - 2, 0, offset + 1, SHEET_SIZE),
            home.BASE - rng.randint(30, 40),
        )
        home.wrap_rect(
            draw,
            (0, offset - 2, SHEET_SIZE, offset + 1),
            home.BASE - rng.randint(30, 40),
        )

    # Chipped corners and surface pitting.
    for _ in range(340):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(2, 6)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius * 2, y + radius),
            fill=home.BASE + rng.choice((-24, -17, 15, 21)),
        )

    # Hairline cracks crossing a slab and dying at a joint.
    for _ in range(22):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        for _ in range(rng.randint(2, 5)):
            nx = x + rng.uniform(-45.0, 45.0)
            ny = y + rng.uniform(20.0, 60.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                home.BASE - rng.randint(26, 38),
                width=1,
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(110, 300), y + rng.randint(80, 210)),
                fill=rng.randint(110, 120),
            )
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(70, 170), y + rng.randint(50, 130)),
                fill=rng.randint(134, 142),
            )

    return home.soft_overlay(image, 10, weathering)


def draw_park_bark(base: Image.Image, rng) -> Image.Image:
    """Trunk bark: vertical fibrous ridges, knots and mossed sides.

    The V axis runs up the trunk under the box projection, so the
    ridges are drawn vertically here and stay vertical in the world.
    """
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((32, 0.6), (128, 0.7), (512, 0.45)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Ridges: long near-vertical fibres, paler crest beside a dark
    # fissure, wandering only a little so the grain stays readable.
    for _ in range(520):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        crest = home.BASE + rng.randint(10, 26)
        fissure = home.BASE - rng.randint(24, 44)
        width = rng.randint(1, 3)
        for _ in range(rng.randint(5, 10)):
            nx = x + rng.uniform(-7.0, 7.0)
            ny = y + rng.uniform(45.0, 110.0)
            home.wrap_line(draw, (x, y, nx, ny), fissure, width=width)
            home.wrap_line(
                draw,
                (x + width + 1, y, nx + width + 1, ny),
                crest,
                width=1,
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # Knots: a few closed scars where a branch was lost.
    for _ in range(7):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        for ring in range(rng.randint(3, 6)):
            radius = 5 + ring * rng.randint(4, 7)
            home.wrap_ellipse(
                draw,
                (cx - radius, cy - int(radius * 1.4),
                 cx + radius, cy + int(radius * 1.4)),
                outline=home.BASE + (18 if ring % 2 else -30),
                width=2,
            )

    # Moss and damp shade, broad and soft on one side of the trunk.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(9):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 170), y + rng.randint(140, 380)),
                fill=rng.randint(106, 116),
            )
        for _ in range(5):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(50, 130), y + rng.randint(100, 260)),
                fill=rng.randint(136, 146),
            )

    return home.soft_overlay(image, 11, weathering)


def draw_park_foliage(base: Image.Image, rng) -> Image.Image:
    """Leaf mass: overlapping clumps with dark gaps showing between."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((16, 0.9), (64, 0.7), (256, 0.5)), rng),
    )
    draw = ImageDraw.Draw(image)

    # The gaps first: the shaded interior the leaves sit in front of.
    for _ in range(160):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(24, 90), y + rng.randint(20, 70)),
            fill=home.BASE - rng.randint(26, 40),
        )

    # Leaf clumps: pointed leaves clustered around a twig, each cluster
    # a shade of its own so the canopy breaks into masses instead of a
    # uniform stipple. Leaves within a clump share a lean.
    for _ in range(300):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        clump = home.BASE + rng.choice((-14, -6, 8, 18, 26))
        lean = rng.uniform(0.0, math.tau)
        for _ in range(rng.randint(8, 18)):
            wrap_leaf(
                draw,
                (cx + rng.randint(-26, 26), cy + rng.randint(-22, 22)),
                rng.uniform(11.0, 22.0),
                rng.uniform(5.0, 9.0),
                lean + rng.uniform(-0.7, 0.7),
                clump + rng.randint(-6, 6),
            )

    # Individual lit leaves catching the sky through the mass.
    for _ in range(900):
        wrap_leaf(
            draw,
            (rng.randrange(SHEET_SIZE), rng.randrange(SHEET_SIZE)),
            rng.uniform(8.0, 15.0),
            rng.uniform(3.5, 6.0),
            rng.uniform(0.0, math.tau),
            home.BASE + rng.randint(24, 38),
        )

    # A few bare twigs crossing the leaves.
    for _ in range(70):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(-40, 40), y + rng.randint(-40, 40)),
            home.BASE - rng.randint(22, 34),
            width=1,
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(110, 300), y + rng.randint(90, 240)),
                fill=rng.randint(104, 116),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(80, 190), y + rng.randint(60, 160)),
                fill=rng.randint(136, 146),
            )

    return home.soft_overlay(image, 14, weathering)


def draw_park_timber(base: Image.Image, rng) -> Image.Image:
    """Bench wood: long grain under paint that has mostly flaked off."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((32, 0.7), (128, 0.55), (512, 0.35)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Slat joints: the boards a bench is actually made of.
    for index in range(4):
        y = index * (SHEET_SIZE // 4)
        home.wrap_rect(
            draw,
            (0, y - 2, SHEET_SIZE, y + 1),
            home.BASE - rng.randint(28, 40),
        )

    # Grain: long horizontal fibres, mostly straight, a few splitting.
    for _ in range(1400):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        tone = home.BASE + rng.choice((-24, -15, -9, 11, 18, 25))
        for _ in range(rng.randint(2, 5)):
            nx = x + rng.uniform(60.0, 150.0)
            ny = y + rng.uniform(-4.0, 4.0)
            home.wrap_line(draw, (x, y, nx, ny), tone, width=1)
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # Splits opening along the grain at the weathered ends.
    for _ in range(26):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(40, 130)
        home.wrap_line(
            draw,
            (x, y, x + length, y + rng.randint(-3, 3)),
            home.BASE - rng.randint(32, 44),
            width=rng.randint(1, 2),
        )

    # Surviving paint: jagged flakes stretched along the grain, brighter
    # and flatter than bare wood, each ringed by the darker line where
    # its edge has lifted. Never round - a round patch reads as a stone.
    for _ in range(150):
        center = (
            float(rng.randrange(SHEET_SIZE)),
            float(rng.randrange(SHEET_SIZE)),
        )
        wrap_flake(
            draw,
            center,
            rng.uniform(9.0, 26.0),
            rng,
            home.BASE + rng.randint(16, 28),
            home.BASE - rng.randint(18, 28),
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(10):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(140, 360), y + rng.randint(50, 140)),
                fill=rng.randint(110, 120),
            )
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 220), y + rng.randint(40, 110)),
                fill=rng.randint(134, 144),
            )

    return home.soft_overlay(image, 11, weathering)


def draw_park_stone(base: Image.Image, rng) -> Image.Image:
    """Park masonry: fine aggregate, hairline cracks, rain staining.

    Deliberately jointless - the plaza sheet owns the slab grid. This
    one dresses a fountain basin, a pedestal, a statue and a chess
    table, where a joint line would read as a crack in the carving.
    """
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.6), (256, 0.6), (512, 0.45)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Aggregate: the grain of a cast, ground and weathered surface.
    for _ in range(6500):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        side = rng.randint(1, 3)
        home.wrap_rect(
            draw,
            (x, y, x + side, y + side),
            home.BASE + rng.choice((-19, -12, 11, 16, 21)),
        )

    # Hairline cracks wandering downward, thinner than the plaza's.
    for _ in range(14):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        for _ in range(rng.randint(4, 9)):
            nx = x + rng.uniform(-40.0, 40.0)
            ny = y + rng.uniform(35.0, 95.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                home.BASE - rng.randint(30, 44),
                width=1,
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # Chips and pitting where a corner has taken knocks.
    for _ in range(420):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(2, 7)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius * 2, y + int(radius * 1.4)),
            fill=home.BASE + rng.choice((-26, -18, 15, 22)),
        )

    # Rain staining down the vertical faces and moss near the ground.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(13):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(18, 60), y + rng.randint(150, 400)),
                fill=rng.randint(108, 118),
            )
        for _ in range(8):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 240), y + rng.randint(60, 170)),
                fill=rng.randint(134, 143),
            )

    return home.soft_overlay(image, 10, weathering)


def draw_park_painted_metal(base: Image.Image, rng) -> Image.Image:
    """Municipal paint over steel: brush lay, chips, rust bleed, runs."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((128, 0.5), (256, 0.55), (512, 0.5)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Brush lay: long faint strokes in one direction, the way a
    # railing gets repainted by hand every few years.
    for _ in range(900):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(70, 190), y + rng.randint(-2, 2)),
            home.BASE + rng.choice((-9, -5, 6, 10)),
            width=rng.randint(1, 2),
        )

    # Chips: paint gone, dark metal under, a pale lip on one side.
    for _ in range(260):
        center = (
            float(rng.randrange(SHEET_SIZE)),
            float(rng.randrange(SHEET_SIZE)),
        )
        wrap_flake(
            draw,
            center,
            rng.uniform(3.5, 11.0),
            rng,
            home.BASE - rng.randint(26, 40),
            home.BASE + rng.randint(12, 20),
        )

    # Rust bleeding out of the chips and down from the fixings.
    for _ in range(120):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(20, 90)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(3, 8), y + length),
            fill=home.BASE - rng.randint(12, 22),
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(11):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 180), y + rng.randint(120, 320)),
                fill=rng.randint(110, 120),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(70, 190), y + rng.randint(50, 140)),
                fill=rng.randint(136, 146),
            )

    return home.soft_overlay(image, 12, weathering)


home.GRAMMARS["park_lawn"] = draw_park_lawn
home.GRAMMARS["park_path"] = draw_park_path
home.GRAMMARS["park_plaza"] = draw_park_plaza
home.GRAMMARS["park_bark"] = draw_park_bark
home.GRAMMARS["park_foliage"] = draw_park_foliage
home.GRAMMARS["park_timber"] = draw_park_timber
home.GRAMMARS["park_stone"] = draw_park_stone
home.GRAMMARS["park_painted_metal"] = draw_park_painted_metal


# --------------------------------------------------------------------------
# specs — tints transcribe CityWorldBuilder's park palette plus the shared
# park path colour from CityExteriorAppearance
# --------------------------------------------------------------------------


PARK_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="CityParkLawnAlbedo",
        grammar="park_lawn",
        seed=0x504B4C57,
        cast=(0.98, 1.02, 0.96),
        mean_target=0.52,
        meters_per_tile=3.0,
        smoothness=0.03,
        metallic=0.0,
        tints=(
            ("CityWorldBuilder.ParkGrass", (0.160, 0.300, 0.180)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityParkPathAlbedo",
        grammar="park_path",
        seed=0x504B5054,
        cast=(1.02, 1.00, 0.96),
        mean_target=0.50,
        meters_per_tile=2.2,
        smoothness=0.05,
        metallic=0.0,
        tints=(
            ("CityExteriorAppearance.ParkPath", (0.390, 0.340, 0.240)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityParkPlazaAlbedo",
        grammar="park_plaza",
        seed=0x504B504C,
        cast=(1.00, 1.00, 0.98),
        mean_target=0.50,
        meters_per_tile=2.8,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("CityWorldBuilder.ParkPlaza", (0.380, 0.350, 0.290)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityParkBarkAlbedo",
        grammar="park_bark",
        seed=0x504B424B,
        cast=(1.02, 1.00, 0.95),
        mean_target=0.48,
        meters_per_tile=1.2,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("CityWorldBuilder.ParkTrunk", (0.200, 0.120, 0.070)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityParkFoliageAlbedo",
        grammar="park_foliage",
        seed=0x504B464C,
        cast=(0.97, 1.02, 0.97),
        mean_target=0.55,
        meters_per_tile=1.6,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("CityWorldBuilder.ParkCanopy", (0.120, 0.270, 0.150)),
            ("CityWorldBuilder.ParkHedge", (0.100, 0.240, 0.130)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityParkTimberAlbedo",
        grammar="park_timber",
        seed=0x504B544D,
        cast=(1.04, 0.99, 0.93),
        mean_target=0.58,
        meters_per_tile=1.4,
        smoothness=0.08,
        metallic=0.0,
        tints=(
            ("CityWorldBuilder.ParkBench", (0.380, 0.220, 0.100)),
            ("CityDecorationWorldBuilder.MasonryColor",
             (0.270, 0.220, 0.170)),
            ("CityDecorationWorldBuilder.ResidentialColor",
             (0.200, 0.290, 0.300)),
            ("CityDecorationWorldBuilder.StreetColor",
             (0.100, 0.120, 0.130)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityParkStoneAlbedo",
        grammar="park_stone",
        seed=0x504B534E,
        cast=(1.01, 1.00, 0.97),
        mean_target=0.58,
        meters_per_tile=1.5,
        smoothness=0.05,
        metallic=0.0,
        tints=(
            ("CityDecorationWorldBuilder.MasonryColor",
             (0.270, 0.220, 0.170)),
            ("CityDecorationWorldBuilder.StreetColor",
             (0.100, 0.120, 0.130)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityParkPaintedMetalAlbedo",
        grammar="park_painted_metal",
        seed=0x504B504D,
        cast=(0.99, 1.00, 1.01),
        mean_target=0.58,
        meters_per_tile=1.2,
        smoothness=0.16,
        metallic=0.15,
        tints=(
            ("CityDecorationWorldBuilder.ResidentialColor",
             (0.200, 0.290, 0.300)),
            ("CityDecorationWorldBuilder.StreetColor",
             (0.100, 0.120, 0.130)),
        ),
    ),
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
        help="Destination directory for the opaque 1024x1024 albedos.",
    )
    parser.add_argument(
        "--art-source",
        type=Path,
        default=DEFAULT_ART_SOURCE,
        help="Destination for the measured contract and the contact sheet.",
    )
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Build and validate without writing anything.",
    )
    args = parser.parse_args()

    records: list[dict] = []
    built: list[tuple[home.HomeSheetSpec, Image.Image]] = []
    for spec in PARK_SHEET_SPECS:
        image, record = home.build_sheet(spec)
        record["resourcePath"] = f"Textures/{spec.key}"
        home.validate(image, spec, record)
        built.append((spec, image))
        if args.verify:
            record["sha256"] = (
                hashlib.sha256(image.tobytes()).hexdigest().upper()
            )
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
        print(f"Validated {len(records)} sheets; nothing written.")
        return

    manifest = {
        "sheetSize": SHEET_SIZE,
        "meanLuminanceTolerance": home.MEAN_LUMINANCE_TOLERANCE,
        "brightnessErrorLimit": home.BRIGHTNESS_ERROR_LIMIT,
        "tintChannelFloor": home.TINT_CHANNEL_FLOOR,
        "sheets": records,
    }
    manifest_path = args.art_source / "park-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "park-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
