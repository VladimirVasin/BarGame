#!/usr/bin/env python3
"""Build the deterministic surface albedos for the player home interior.

Twelve seamless 1024x1024 sheets, imported by Unity at 512 (the exact 2:1
box downsample the facade pipeline established). The runtime tiles them by
metres through `HomeSurfaceAppearance`, which multiplies each renderer's
authored flat colour (brightened by a per-sheet compensation constant) with
the sheet in linear space, exactly as URP/Lit does.

The luminance rule is the city-facade linear rule, NOT the stairwell gamma
rule: for every real tint channel the seven Home builders pass, the solved
compensation must satisfy `linear(min(1, ch * c)) * mean == linear(ch)`
within 8%, and must never clamp a channel past one. Channels below
TINT_CHANNEL_FLOOR sit in the sRGB toe where relative error is meaningless
(the absolute linear values are thousandths); they are held to the clamp
check only, which mirrors how the facade script checks only its per-lot
peaks.

Noise is periodic by construction (3x3-tiled lattice, centre crop), every
stamp goes through a wrapping helper, and every pattern pitch divides the
sheet, so the sheets wrap rather than merely passing the wrap check by
luck. Pillow is the only dependency.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Home" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "Home"

SHEET_SIZE = 1024

MEAN_LUMINANCE_TOLERANCE = 0.02
BRIGHTNESS_ERROR_LIMIT = 0.08
EDGE_DELTA_LIMIT = 16.0
SEAM_RATIO_LIMIT = 2.5
CHANNEL_RATIO_LIMIT = 1.22
DEFAULT_CONTRAST_FLOOR = 40

# Below this sRGB channel value the curve leaves its power-law region and a
# single per-sheet compensation cannot hold relative brightness -- but there
# the absolute linear values are thousandths, so only the clamp check
# applies. Everything at or above it must land within the 8% limit.
TINT_CHANNEL_FLOOR = 0.09

REC709 = (0.2126, 0.7152, 0.0722)

BASE = 170


# --------------------------------------------------------------------------
# colour helpers (shared contract with the facade generator)
# --------------------------------------------------------------------------


def srgb_to_linear(value: float) -> float:
    """The exact sRGB curve, matching UnityEngine.Mathf.GammaToLinearSpace."""
    if value <= 0.04045:
        return value / 12.92
    return math.pow((value + 0.055) / 1.055, 2.4)


LINEAR_TABLE = [srgb_to_linear(value / 255.0) for value in range(256)]


def channel_means(image: Image.Image) -> tuple[float, float, float]:
    histogram = image.histogram()
    pixels = image.width * image.height
    means = []
    for channel in range(3):
        bins = histogram[channel * 256:(channel + 1) * 256]
        means.append(
            sum(value * count for value, count in enumerate(bins)) / pixels
        )
    return means[0], means[1], means[2]


def mean_linear_luminance(image: Image.Image) -> float:
    histogram = image.histogram()
    pixels = image.width * image.height
    total = 0.0
    for channel in range(3):
        bins = histogram[channel * 256:(channel + 1) * 256]
        total += REC709[channel] * sum(
            count * LINEAR_TABLE[value] for value, count in enumerate(bins)
        )
    return total / pixels


def luminance_percentiles(
    image: Image.Image,
    low: float,
    high: float,
) -> tuple[int, int]:
    grey = image.convert("L")
    bins = grey.histogram()
    pixels = grey.width * grey.height
    results: list[int] = []
    for fraction in (low, high):
        threshold = pixels * fraction
        running = 0
        chosen = 255
        for value, count in enumerate(bins):
            running += count
            if running >= threshold:
                chosen = value
                break
        results.append(chosen)
    return results[0], results[1]


# --------------------------------------------------------------------------
# periodic noise and wrap-aware drawing
# --------------------------------------------------------------------------


def periodic_noise(lattice: int, rng: random.Random) -> Image.Image:
    if SHEET_SIZE % lattice != 0:
        raise ValueError(
            f"Lattice {lattice} does not divide the {SHEET_SIZE} sheet."
        )

    tile = Image.new("L", (lattice, lattice))
    tile.putdata([rng.randrange(256) for _ in range(lattice * lattice)])
    tripled = Image.new("L", (lattice * 3, lattice * 3))
    for row in range(3):
        for column in range(3):
            tripled.paste(tile, (column * lattice, row * lattice))
    grown = tripled.resize(
        (SHEET_SIZE * 3, SHEET_SIZE * 3),
        Image.Resampling.BICUBIC,
    )
    return grown.crop(
        (SHEET_SIZE, SHEET_SIZE, SHEET_SIZE * 2, SHEET_SIZE * 2)
    )


def wrap_filter(
    image: Image.Image,
    image_filter: ImageFilter.Filter,
    pad: int = 6,
) -> Image.Image:
    size = image.width
    padded = Image.new(image.mode, (size + pad * 2, size + pad * 2))
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            padded.paste(image, (pad + dx * size, pad + dy * size))
    return padded.filter(image_filter).crop(
        (pad, pad, pad + size, pad + size)
    )


def fractal_noise(
    octaves: tuple[tuple[int, float], ...],
    rng: random.Random,
) -> Image.Image:
    accumulated: Image.Image | None = None
    weight_sum = 0.0
    for lattice, weight in octaves:
        layer = periodic_noise(lattice, rng)
        weight_sum += weight
        accumulated = (
            layer
            if accumulated is None
            else Image.blend(accumulated, layer, weight / weight_sum)
        )
    if accumulated is None:
        raise ValueError("Fractal noise needs at least one octave.")
    return accumulated


OFFSETS = (-SHEET_SIZE, 0, SHEET_SIZE)


def wrap_rect(
    draw: ImageDraw.ImageDraw,
    box: tuple[float, float, float, float],
    fill: int,
) -> None:
    x0, y0, x1, y1 = box
    for dx in OFFSETS:
        for dy in OFFSETS:
            draw.rectangle((x0 + dx, y0 + dy, x1 + dx, y1 + dy), fill=fill)


def wrap_line(
    draw: ImageDraw.ImageDraw,
    points: tuple[float, float, float, float],
    fill: int,
    width: int = 1,
) -> None:
    x0, y0, x1, y1 = points
    for dx in OFFSETS:
        for dy in OFFSETS:
            draw.line(
                (x0 + dx, y0 + dy, x1 + dx, y1 + dy),
                fill=fill,
                width=width,
            )


def wrap_ellipse(
    draw: ImageDraw.ImageDraw,
    box: tuple[float, float, float, float],
    fill: int | None = None,
    outline: int | None = None,
    width: int = 1,
) -> None:
    x0, y0, x1, y1 = box
    for dx in OFFSETS:
        for dy in OFFSETS:
            draw.ellipse(
                (x0 + dx, y0 + dy, x1 + dx, y1 + dy),
                fill=fill,
                outline=outline,
                width=width,
            )


def soft_overlay(
    base: Image.Image,
    blur: float,
    painter,
) -> Image.Image:
    """Overlay a blurred mid-grey layer: 128 is neutral, darker darkens.

    The painter receives an ImageDraw bound to a 128-filled layer; whatever
    it stamps is Gaussian-blurred through the wrap-aware filter and then
    overlaid, so soft stains never introduce a seam.
    """
    layer = Image.new("L", (SHEET_SIZE, SHEET_SIZE), 128)
    painter(ImageDraw.Draw(layer))
    layer = wrap_filter(
        layer,
        ImageFilter.GaussianBlur(blur),
        pad=int(blur * 3) + 6,
    )
    return ImageChops.overlay(base, layer)


# --------------------------------------------------------------------------
# specs
# --------------------------------------------------------------------------


@dataclass(frozen=True)
class HomeSheetSpec:
    """One authored home surface sheet.

    `tints` transcribes every flat colour the Home builders pass for this
    surface kind; the solved compensation is validated against each of their
    channels, so a palette edit in a builder fails the build here rather
    than silently shifting the room. `cast` is near-neutral because the
    tint owns the hue and the texture owns the material.
    """

    key: str
    grammar: str
    seed: int
    cast: tuple[float, float, float]
    mean_target: float
    meters_per_tile: float
    smoothness: float
    metallic: float
    tints: tuple[tuple[str, tuple[float, float, float]], ...]
    contrast_floor: int = DEFAULT_CONTRAST_FLOOR


HOME_SHEET_SPECS: tuple[HomeSheetSpec, ...] = (
    HomeSheetSpec(
        key="HomeWallpaperAlbedo",
        grammar="wallpaper",
        seed=0x484D5750,
        cast=(1.04, 1.00, 0.94),
        mean_target=0.50,
        meters_per_tile=1.9,
        smoothness=0.05,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.Wall", (0.255, 0.225, 0.175)),
            ("HomeInteriorWorldBuilder.Trim", (0.37, 0.27, 0.16)),
            ("HomeBathroomBuilder.Partition", (0.255, 0.235, 0.205)),
        ),
    ),
    HomeSheetSpec(
        key="HomeCeilingPlasterAlbedo",
        grammar="whitewash",
        seed=0x484D4350,
        cast=(1.00, 1.00, 0.97),
        mean_target=0.55,
        meters_per_tile=2.8,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.Ceiling", (0.16, 0.14, 0.11)),
        ),
        contrast_floor=24,
    ),
    HomeSheetSpec(
        key="HomePlankFloorAlbedo",
        grammar="planks",
        seed=0x484D5046,
        cast=(1.05, 0.99, 0.92),
        mean_target=0.45,
        meters_per_tile=1.6,
        smoothness=0.10,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.Floor", (0.115, 0.085, 0.065)),
            ("HomeBalconyWorldBuilder.Frame", (0.22, 0.27, 0.25)),
        ),
    ),
    HomeSheetSpec(
        key="HomeDarkWoodAlbedo",
        grammar="veneer",
        seed=0x484D4457,
        cast=(1.06, 0.98, 0.90),
        mean_target=0.52,
        meters_per_tile=1.1,
        smoothness=0.12,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.DarkWood", (0.115, 0.055, 0.036)),
            ("HomeBathroomBuilder.Door", (0.19, 0.105, 0.065)),
            ("HomeBalconyWorldBuilder.Door", (0.08, 0.16, 0.15)),
            ("HomeInteriorWorldBuilder.KitchenBrokenDoor",
             (0.12, 0.14, 0.12)),
            ("HomeInteriorDressingBuilder.WindowBoardUpper",
             (0.25, 0.14, 0.08)),
            ("HomeInteriorDressingBuilder.WindowBoardLower",
             (0.20, 0.11, 0.065)),
            ("HomeInteriorDressingBuilder.OldRadio", (0.10, 0.095, 0.08)),
            ("HomeInteriorWorldBuilder.CameraJunkBase",
             (0.10, 0.055, 0.035)),
            ("HomeInteriorWorldBuilder.WardrobeDoor",
             (0.19, 0.105, 0.060)),
            ("HomeInteriorWorldBuilder.Suitcase", (0.16, 0.12, 0.075)),
        ),
    ),
    HomeSheetSpec(
        key="HomeWornLaminateAlbedo",
        grammar="laminate",
        seed=0x484D4C4D,
        cast=(1.03, 1.00, 0.95),
        mean_target=0.48,
        meters_per_tile=1.2,
        smoothness=0.18,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.Trim", (0.37, 0.27, 0.16)),
            ("HomeInteriorWorldBuilder.KitchenCounter",
             (0.16, 0.18, 0.16)),
        ),
    ),
    HomeSheetSpec(
        key="HomeUpholsteryAlbedo",
        grammar="weave",
        seed=0x484D5548,
        cast=(0.97, 1.01, 1.00),
        mean_target=0.45,
        meters_per_tile=0.85,
        smoothness=0.03,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.Fabric", (0.14, 0.20, 0.18)),
            ("HomeInteriorWorldBuilder.Blanket", (0.17, 0.255, 0.23)),
            ("HomeInteriorWorldBuilder.SunkenCushion",
             (0.18, 0.235, 0.20)),
            ("HomeInteriorWorldBuilder.OldCoat", (0.105, 0.14, 0.13)),
        ),
    ),
    HomeSheetSpec(
        key="HomeBedLinenAlbedo",
        grammar="linen",
        seed=0x484D424C,
        cast=(1.02, 1.00, 0.95),
        mean_target=0.58,
        meters_per_tile=0.9,
        smoothness=0.03,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.DirtyLinen", (0.43, 0.39, 0.29)),
            ("HomeInteriorWorldBuilder.Pillow", (0.47, 0.43, 0.34)),
            ("HomeBathroomBuilder.Curtain", (0.38, 0.36, 0.22)),
        ),
        contrast_floor=24,
    ),
    HomeSheetSpec(
        key="HomeBathroomTileAlbedo",
        grammar="tile",
        seed=0x484D4254,
        cast=(0.97, 1.00, 0.99),
        mean_target=0.52,
        meters_per_tile=1.2,
        smoothness=0.35,
        metallic=0.0,
        tints=(
            ("HomeBathroomBuilder.Tile", (0.28, 0.31, 0.28)),
            ("HomeBathroomBuilder.DirtyTile", (0.17, 0.21, 0.18)),
        ),
    ),
    HomeSheetSpec(
        key="HomeEnamelAlbedo",
        grammar="enamel",
        seed=0x484D454E,
        cast=(1.02, 1.01, 0.95),
        mean_target=0.62,
        meters_per_tile=1.0,
        smoothness=0.45,
        metallic=0.05,
        tints=(
            ("HomeBathroomBuilder.Porcelain", (0.49, 0.50, 0.42)),
            ("HomeBathroomBuilder.PorcelainShadow", (0.31, 0.33, 0.29)),
            ("HomeBathroomBuilder.ToiletSeat", (0.33, 0.31, 0.24)),
            ("HomeRefrigeratorWorldBuilder.Enamel", (0.54, 0.55, 0.43)),
            ("HomeRefrigeratorWorldBuilder.EnamelHighlight",
             (0.69, 0.70, 0.57)),
            ("HomeRefrigeratorWorldBuilder.EnamelShadow",
             (0.27, 0.29, 0.23)),
            ("HomeRefrigeratorWorldBuilder.Interior", (0.70, 0.73, 0.64)),
            ("HomeRefrigeratorWorldBuilder.InteriorShadow",
             (0.34, 0.38, 0.34)),
            ("HomeRefrigeratorWorldBuilder.Shelf", (0.49, 0.57, 0.55)),
            ("HomeBalconyWorldBuilder.Frame", (0.22, 0.27, 0.25)),
            ("HomeInteriorWorldBuilder.KitchenSink", (0.32, 0.38, 0.36)),
        ),
        contrast_floor=24,
    ),
    HomeSheetSpec(
        key="HomePaintedMetalAlbedo",
        grammar="painted_metal",
        seed=0x484D504D,
        cast=(0.98, 1.00, 1.00),
        mean_target=0.45,
        meters_per_tile=1.0,
        smoothness=0.22,
        metallic=0.30,
        tints=(
            ("HomeBalconyWorldBuilder.Rail", (0.18, 0.25, 0.25)),
            ("HomeBathroomBuilder.Rust", (0.40, 0.17, 0.075)),
            ("HomeInteriorDressingBuilder.Rust", (0.38, 0.16, 0.075)),
            ("HomeRefrigeratorWorldBuilder.Metal", (0.30, 0.31, 0.27)),
        ),
    ),
    HomeSheetSpec(
        key="HomeConcreteAlbedo",
        grammar="stucco",
        seed=0x484D434E,
        cast=(1.00, 1.00, 0.97),
        mean_target=0.45,
        meters_per_tile=2.4,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("HomeBalconyWorldBuilder.Facade", (0.20, 0.22, 0.19)),
            ("HomeBalconyWorldBuilder.Slab", (0.23, 0.23, 0.20)),
            ("HomeInteriorWorldBuilder.Wall", (0.255, 0.225, 0.175)),
        ),
    ),
    HomeSheetSpec(
        key="HomeRugAlbedo",
        grammar="rug",
        seed=0x484D5247,
        cast=(1.06, 0.97, 0.94),
        mean_target=0.42,
        meters_per_tile=1.5,
        smoothness=0.02,
        metallic=0.0,
        tints=(
            ("HomeInteriorWorldBuilder.EntryRug", (0.22, 0.075, 0.065)),
        ),
    ),
)


# --------------------------------------------------------------------------
# grammars: the grey value field of each material
# --------------------------------------------------------------------------


def draw_wallpaper(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Vertical stripes at a pitch that divides the sheet.
    stripe = 64
    for x in range(0, SHEET_SIZE, stripe):
        wrap_rect(draw, (x, 0, x + stripe // 2 - 1, SHEET_SIZE), BASE + 9)
        wrap_line(draw, (x, 0, x, SHEET_SIZE), BASE - 12, width=2)
    # Floral sprigs on a staggered 128 px lattice, quantised into a few
    # rectangles so they survive the 512 import and the PS1 composite.
    cell = 128
    for row in range(SHEET_SIZE // cell):
        offset = (row % 2) * (cell // 2)
        for column in range(SHEET_SIZE // cell):
            cx = column * cell + offset + cell // 2
            cy = row * cell + cell // 2
            tone = BASE - rng.randint(18, 26)
            wrap_rect(draw, (cx - 3, cy - 11, cx + 3, cy + 11), tone)
            wrap_rect(draw, (cx - 11, cy - 3, cx + 11, cy + 3), tone)
            wrap_rect(draw, (cx - 6, cy - 6, cx + 6, cy + 6), tone + 8)
            wrap_rect(draw, (cx - 2, cy - 2, cx + 2, cy + 2), tone - 6)
    # The pale ghosts of two taken-down pictures.
    for _ in range(2):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(120, 190)
        height = rng.randint(150, 230)
        wrap_rect(draw, (x, y, x + width, y + height), BASE + 22)
        wrap_rect(
            draw,
            (x + 5, y + 5, x + width - 5, y + height - 5),
            BASE + 15,
        )
    # Damp mottling low on the value scale, softened so it reads as staining.
    def stains(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(9):
            sx = rng.randrange(SHEET_SIZE)
            sy = rng.randrange(SHEET_SIZE)
            wrap_ellipse(
                layer_draw,
                (sx, sy, sx + rng.randint(90, 240), sy + rng.randint(70, 180)),
                fill=128 - rng.randint(10, 18),
            )

    return soft_overlay(base, 22.0, stains)


def draw_whitewash(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Fine speckle, the roller texture of an old distemper coat.
    for _ in range(2200):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(2, 4)
        wrap_rect(
            draw,
            (x, y, x + size, y + size),
            BASE + rng.randint(-7, 7),
        )
    # Hairline cracks: short chained segments wandering across the sheet.
    for _ in range(4):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        heading = rng.uniform(0.0, math.tau)
        for _ in range(rng.randint(14, 24)):
            length = rng.uniform(18.0, 42.0)
            nx = x + math.cos(heading) * length
            ny = y + math.sin(heading) * length
            wrap_line(draw, (x, y, nx, ny), BASE - 26, width=1)
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE
            heading += rng.uniform(-0.7, 0.7)
    # Water-stain rings: nested soft ellipses with a darker rim.
    def stains(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(3):
            sx = rng.randrange(SHEET_SIZE)
            sy = rng.randrange(SHEET_SIZE)
            width = rng.randint(150, 260)
            height = rng.randint(110, 200)
            wrap_ellipse(
                layer_draw,
                (sx, sy, sx + width, sy + height),
                outline=128 - 22,
                width=7,
            )
            wrap_ellipse(
                layer_draw,
                (sx + 18, sy + 14, sx + width - 18, sy + height - 14),
                fill=128 - 8,
            )

    return soft_overlay(base, 9.0, stains)


def draw_planks(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    board = 128
    joint_pitch = 256
    # Fill every board before any detail: a later board's fill would
    # otherwise overwrite the wrapped half of an earlier board's edge gap.
    tones: list[int] = []
    for index in range(SHEET_SIZE // board):
        tone = BASE + rng.randint(-12, 12)
        tones.append(tone)
        wrap_rect(
            draw,
            (index * board, 0, index * board + board - 1, SHEET_SIZE),
            tone,
        )
    for index in range(SHEET_SIZE // board):
        x = index * board
        tone = tones[index]
        # Grain: sparse full-height streaks inside the board.
        for _ in range(rng.randint(5, 8)):
            gx = x + rng.randint(6, board - 6)
            wrap_line(
                draw,
                (gx, 0, gx, SHEET_SIZE),
                tone + rng.choice((-7, -5, 5, 7)),
                width=1,
            )
        # Butt joints staggered per board at a pitch that divides the sheet.
        joint_offset = rng.randrange(joint_pitch)
        y = joint_offset
        while y < SHEET_SIZE + joint_offset:
            wrap_rect(draw, (x + 2, y - 2, x + board - 2, y + 2), tone - 34)
            y += joint_pitch
        # A knot or two.
        for _ in range(rng.randint(0, 2)):
            kx = x + rng.randint(20, board - 20)
            ky = rng.randrange(SHEET_SIZE)
            wrap_ellipse(
                draw,
                (kx - 7, ky - 11, kx + 7, ky + 11),
                fill=tone - 28,
            )
            wrap_ellipse(
                draw,
                (kx - 3, ky - 5, kx + 3, ky + 5),
                fill=tone - 40,
            )
        # Board gaps, centred on the board edge so the wrap column carries
        # the same half of the gap as its true neighbour.
        wrap_rect(draw, (x - 2, 0, x + 2, SHEET_SIZE), BASE - 48)
    # Pale scuffing along the walking line, soft and wide.
    def scuffs(layer_draw: ImageDraw.ImageDraw) -> None:
        track = rng.randrange(SHEET_SIZE)
        wrap_rect(
            layer_draw,
            (0, track, SHEET_SIZE, track + 190),
            128 + 12,
        )
        for _ in range(7):
            sx = rng.randrange(SHEET_SIZE)
            sy = track + rng.randint(-40, 190)
            wrap_ellipse(
                layer_draw,
                (sx, sy, sx + rng.randint(60, 140), sy + rng.randint(28, 60)),
                fill=128 + rng.randint(8, 15),
            )

    return soft_overlay(base, 16.0, scuffs)


def draw_veneer(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Directional grain: long horizontal streaks of slightly varied value.
    for _ in range(240):
        y = rng.randrange(SHEET_SIZE)
        x = rng.randrange(SHEET_SIZE)
        length = rng.randint(120, 420)
        thickness = rng.randint(2, 4)
        wrap_rect(
            draw,
            (x, y, x + length, y + thickness),
            BASE + rng.randint(-11, 11),
        )
    # Edge banding: one seam pair across the sheet.
    seam_y = rng.randrange(SHEET_SIZE)
    wrap_line(draw, (0, seam_y, SHEET_SIZE, seam_y), BASE - 30, width=2)
    wrap_line(
        draw,
        (0, seam_y + 3, SHEET_SIZE, seam_y + 3),
        BASE + 14,
        width=1,
    )
    # Chipped laminate: light flecks clustered along two wear bands.
    for _ in range(2):
        band_y = rng.randrange(SHEET_SIZE)
        for _ in range(rng.randint(9, 14)):
            x = rng.randrange(SHEET_SIZE)
            y = band_y + rng.randint(-26, 26)
            wrap_rect(
                draw,
                (x, y, x + rng.randint(4, 12), y + rng.randint(3, 8)),
                BASE + rng.randint(26, 40),
            )
    # One darker repair patch.
    x = rng.randrange(SHEET_SIZE)
    y = rng.randrange(SHEET_SIZE)
    wrap_rect(draw, (x, y, x + 130, y + 90), BASE - 18)
    return base


def draw_laminate(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Two-tone speckle.
    for _ in range(2600):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(2, 5)
        wrap_rect(
            draw,
            (x, y, x + size, y + size),
            BASE + rng.choice((-9, -6, 6, 9)),
        )
    # Ring stains where glasses and pans stood.
    for _ in range(5):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        diameter = rng.randint(46, 92)
        wrap_ellipse(
            draw,
            (x, y, x + diameter, y + diameter),
            outline=BASE - 26,
            width=3,
        )
    # Knife scratches.
    for _ in range(14):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        angle = rng.uniform(-0.5, 0.5)
        length = rng.uniform(60.0, 190.0)
        wrap_line(
            draw,
            (
                x,
                y,
                x + math.cos(angle) * length,
                y + math.sin(angle) * length,
            ),
            BASE + 22,
            width=1,
        )
    # One burn blotch.
    def burn(layer_draw: ImageDraw.ImageDraw) -> None:
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        wrap_ellipse(layer_draw, (x, y, x + 90, y + 70), fill=128 - 30)
        wrap_ellipse(
            layer_draw,
            (x + 22, y + 18, x + 68, y + 52),
            fill=128 - 44,
        )

    return soft_overlay(base, 7.0, burn)


def draw_weave(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Cross-hatch weave on a 16 px pitch (8 px per thread at 512 import).
    pitch = 16
    for y in range(0, SHEET_SIZE, pitch):
        wrap_rect(draw, (0, y, SHEET_SIZE, y + pitch // 2 - 1), BASE + 7)
        wrap_line(draw, (0, y, SHEET_SIZE, y), BASE - 13, width=2)
    for x in range(0, SHEET_SIZE, pitch):
        wrap_line(draw, (x, 0, x, SHEET_SIZE), BASE - 9, width=1)
    # Sag shading: large soft blotches where the stuffing has given way.
    def sags(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(180, 340), y + rng.randint(140, 260)),
                fill=128 - rng.randint(9, 15),
            )
        # Shiny worn patches read lighter.
        for _ in range(4):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 170), y + rng.randint(60, 120)),
                fill=128 + rng.randint(9, 14),
            )

    return soft_overlay(base, 26.0, sags)


def draw_linen(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Faint ticking stripes.
    pitch = 32
    for x in range(0, SHEET_SIZE, pitch):
        wrap_line(draw, (x, 0, x, SHEET_SIZE), BASE - 9, width=2)
        wrap_line(draw, (x + 5, 0, x + 5, SHEET_SIZE), BASE - 6, width=1)
    # Wrinkle bands: soft diagonal folds.
    def wrinkles(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(9):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            length = rng.randint(260, 520)
            drop = rng.randint(-140, 140)
            for offset in range(0, rng.randint(6, 12), 3):
                wrap_line(
                    layer_draw,
                    (
                        x,
                        y + offset,
                        x + length,
                        y + drop + offset,
                    ),
                    128 - rng.randint(8, 13),
                    width=4,
                )
        for _ in range(5):
            sx = rng.randrange(SHEET_SIZE)
            sy = rng.randrange(SHEET_SIZE)
            wrap_ellipse(
                layer_draw,
                (sx, sy, sx + rng.randint(70, 150), sy + rng.randint(50, 110)),
                fill=128 - rng.randint(7, 12),
            )

    return soft_overlay(base, 12.0, wrinkles)


def draw_tile(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    cell = 128
    grout = 5
    tiles = SHEET_SIZE // cell
    cracked = {(rng.randrange(tiles), rng.randrange(tiles)) for _ in range(4)}
    for row in range(tiles):
        for column in range(tiles):
            x = column * cell
            y = row * cell
            tone = BASE + rng.randint(-9, 9)
            if (column, row) in cracked:
                tone -= 16
            wrap_rect(
                draw,
                (x + grout, y + grout, x + cell - grout, y + cell - grout),
                tone,
            )
            # A subtle glaze highlight along the tile's top edge.
            wrap_rect(
                draw,
                (x + grout, y + grout, x + cell - grout, y + grout + 7),
                tone + 10,
            )
            if (column, row) in cracked:
                wrap_line(
                    draw,
                    (
                        x + grout + rng.randint(6, 30),
                        y + grout,
                        x + cell - grout - rng.randint(6, 30),
                        y + cell - grout,
                    ),
                    tone - 24,
                    width=2,
                )
    # Grout base value and grime along a couple of grout rows.
    for row in range(tiles + 1):
        y = row % tiles * cell
        wrap_rect(draw, (0, y - 2, SHEET_SIZE, y + 2), BASE - 52)
    for column in range(tiles + 1):
        x = column % tiles * cell
        wrap_rect(draw, (x - 2, 0, x + 2, SHEET_SIZE), BASE - 52)
    for _ in range(2):
        y = rng.randrange(tiles) * cell
        wrap_rect(draw, (0, y - 4, SHEET_SIZE, y + 4), BASE - 64)
    return base


def draw_enamel(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Yellowed mottling, very soft.
    def mottle(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(8):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(140, 300), y + rng.randint(100, 220)),
                fill=128 - rng.randint(5, 9),
            )

    base = soft_overlay(base, 30.0, mottle)
    draw = ImageDraw.Draw(base)
    # Chips cluster along one band, the way enamel fails around handling.
    band_y = rng.randrange(SHEET_SIZE)
    chips: list[tuple[int, int]] = []
    for _ in range(rng.randint(10, 14)):
        x = rng.randrange(SHEET_SIZE)
        y = band_y + rng.randint(-70, 70)
        size = rng.randint(3, 7)
        wrap_rect(draw, (x, y, x + size, y + size), BASE - 96)
        wrap_rect(draw, (x + 1, y + 1, x + size, y + size), BASE - 74)
        chips.append((x, y + size))
    # Rust weeps below a few chips, fading as they fall.
    for x, y in chips[:3]:
        length = rng.randint(40, 110)
        steps = 4
        for step in range(steps):
            value = BASE - 52 + step * 11
            wrap_rect(
                draw,
                (
                    x,
                    y + length * step // steps,
                    x + 3,
                    y + length * (step + 1) // steps,
                ),
                value,
            )
    # Sparse pinholes elsewhere.
    for _ in range(10):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        wrap_rect(draw, (x, y, x + 2, y + 2), BASE - 58)
    return base


def draw_painted_metal(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Brush direction: vertical streaks.
    for _ in range(360):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(90, 320)
        wrap_rect(
            draw,
            (x, y, x + rng.randint(1, 3), y + length),
            BASE + rng.randint(-9, 9),
        )
    # Bolts on a 128 px lattice with rust bleeding downward.
    pitch = 128
    for row in range(SHEET_SIZE // pitch):
        for column in range(SHEET_SIZE // pitch):
            x = column * pitch + pitch // 2
            y = row * pitch + pitch // 2
            wrap_rect(draw, (x - 4, y - 4, x + 4, y + 4), BASE - 62)
            wrap_rect(draw, (x - 2, y - 2, x + 2, y + 2), BASE - 40)
            if rng.random() < 0.55:
                length = rng.randint(26, 84)
                steps = 3
                for step in range(steps):
                    value = BASE - 44 + step * 10
                    wrap_rect(
                        draw,
                        (
                            x - 3,
                            y + 4 + length * step // steps,
                            x + 3,
                            y + 4 + length * (step + 1) // steps,
                        ),
                        value,
                    )
    # Paint-loss islands.
    for _ in range(7):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        wrap_ellipse(
            draw,
            (x, y, x + rng.randint(30, 90), y + rng.randint(20, 60)),
            fill=BASE - 30,
        )
        wrap_ellipse(
            draw,
            (x + 8, y + 6, x + rng.randint(22, 60), y + rng.randint(14, 40)),
            fill=BASE - 44,
        )
    return base


def draw_stucco(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Formwork seams at a pitch that divides the sheet, offset off zero so
    # no seam is phase-locked to the wrap boundary.
    pitch = 256
    for row in range(SHEET_SIZE // pitch):
        y = row * pitch + 128
        wrap_line(draw, (0, y, SHEET_SIZE, y), BASE - 30, width=2)
        wrap_line(draw, (0, y + 3, SHEET_SIZE, y + 3), BASE + 18, width=1)
        # Tie holes along the seam.
        for column in range(4):
            x = column * 256 + 128
            wrap_ellipse(
                draw,
                (x - 6, y - 6, x + 6, y + 6),
                fill=BASE - 42,
            )
    # Patch repairs.
    for _ in range(4):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        wrap_rect(
            draw,
            (x, y, x + rng.randint(80, 210), y + rng.randint(60, 150)),
            BASE + rng.randint(8, 18),
        )
    # Vertical damp streaks fading as they fall.
    for _ in range(12):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(8, 30)
        length = rng.randint(80, 240)
        steps = 4
        for step in range(steps):
            value = BASE - 34 + step * 7
            wrap_rect(
                draw,
                (
                    x,
                    y + length * step // steps,
                    x + width,
                    y + length * (step + 1) // steps,
                ),
                value,
            )
    # Aggregate speckle.
    for _ in range(1600):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(2, 4)
        wrap_rect(
            draw,
            (x, y, x + size, y + size),
            BASE + rng.randint(-14, 14),
        )
    return base


def draw_rug(base: Image.Image, rng: random.Random) -> Image.Image:
    draw = ImageDraw.Draw(base)
    # Diamond lattice at a 128 px pitch: both diagonal families.
    pitch = 128
    for offset in range(-SHEET_SIZE, SHEET_SIZE * 2, pitch):
        wrap_line(
            draw,
            (offset, 0, offset + SHEET_SIZE, SHEET_SIZE),
            BASE - 26,
            width=5,
        )
        wrap_line(
            draw,
            (offset + SHEET_SIZE, 0, offset, SHEET_SIZE),
            BASE - 26,
            width=5,
        )
    # Medallions at the lattice cell centres.
    for row in range(SHEET_SIZE // pitch):
        for column in range(SHEET_SIZE // pitch):
            cx = column * pitch + pitch // 2
            cy = row * pitch + ((column % 2) * pitch // 2 + pitch // 2) % pitch
            tone = BASE - 40 if (row + column) % 2 == 0 else BASE + 14
            wrap_rect(draw, (cx - 3, cy - 13, cx + 3, cy + 13), tone)
            wrap_rect(draw, (cx - 13, cy - 3, cx + 13, cy + 3), tone)
            wrap_rect(draw, (cx - 6, cy - 6, cx + 6, cy + 6), tone)
    # Fringe rows would break tiling; instead a pile-direction banding.
    for y in range(0, SHEET_SIZE, 8):
        if (y // 8) % 2 == 0:
            wrap_line(draw, (0, y, SHEET_SIZE, y), BASE - 6, width=1)
    # The worn walking track, lighter where the pile has flattened.
    def track(layer_draw: ImageDraw.ImageDraw) -> None:
        y = rng.randrange(SHEET_SIZE)
        wrap_rect(layer_draw, (0, y, SHEET_SIZE, y + 230), 128 + 14)
        for _ in range(5):
            sx = rng.randrange(SHEET_SIZE)
            sy = y + rng.randint(-30, 220)
            wrap_ellipse(
                layer_draw,
                (sx, sy, sx + rng.randint(80, 170), sy + rng.randint(40, 90)),
                fill=128 + rng.randint(8, 14),
            )

    return soft_overlay(base, 20.0, track)


GRAMMARS = {
    "wallpaper": draw_wallpaper,
    "whitewash": draw_whitewash,
    "planks": draw_planks,
    "veneer": draw_veneer,
    "laminate": draw_laminate,
    "weave": draw_weave,
    "linen": draw_linen,
    "tile": draw_tile,
    "enamel": draw_enamel,
    "painted_metal": draw_painted_metal,
    "stucco": draw_stucco,
    "rug": draw_rug,
}


# --------------------------------------------------------------------------
# tone mapping and compensation
# --------------------------------------------------------------------------


def cast_tables(
    cast: tuple[float, float, float],
) -> tuple[list[int], list[int], list[int]]:
    return tuple(  # type: ignore[return-value]
        [min(255, int(round(value * channel))) for value in range(256)]
        for channel in cast
    )


SHADOW_KNEE = 0.22


def tone_table(exposure: float) -> list[int]:
    table: list[int] = []
    for value in range(256):
        t = value / 255.0
        blend = min(1.0, t / SHADOW_KNEE)
        blend = blend * blend * (3.0 - 2.0 * blend)
        lifted = t + (math.pow(t, exposure) - t) * blend
        table.append(min(255, int(round(255.0 * lifted))))
    return table


def normalise_luminance(
    image: Image.Image,
    target: float,
) -> tuple[Image.Image, float]:
    low, high = 0.05, 4.0
    best = image
    exposure = 1.0
    for _ in range(30):
        exposure = math.sqrt(low * high)
        table = tone_table(exposure)
        best = Image.merge(
            "RGB",
            tuple(channel.point(table) for channel in image.split()),
        )
        mean = mean_linear_luminance(best)
        if abs(mean - target) < 0.0004:
            break
        if mean < target:
            high = exposure
        else:
            low = exposure
    if abs(mean_linear_luminance(best) - target) > 0.004:
        raise ValueError(
            "Exposure could not reach the target mean; the sheet carries "
            "too much dark area for the field to compensate."
        )
    return best, exposure


def solve_compensation(
    mean: float,
    spec: HomeSheetSpec,
) -> tuple[float, float]:
    """The brightening the runtime applies to this kind's flat tints.

    Solved by scanning for the constant that minimises the worst linear
    brightness error across every eligible builder tint channel, under the
    hard cap that no channel may clamp past one.
    """
    channels = sorted(
        {
            round(channel, 4)
            for _, tint in spec.tints
            for channel in tint
        }
    )
    eligible = [
        channel for channel in channels if channel >= TINT_CHANNEL_FLOOR
    ]
    if not eligible:
        raise ValueError(f"{spec.key} has no tint channel above the floor.")
    cap = 1.0 / max(channels)

    best_compensation = 1.0
    best_error = float("inf")
    steps = int((cap - 1.0) / 0.0005)
    for step in range(max(1, steps) + 1):
        candidate = 1.0 + step * 0.0005
        if candidate > cap + 1e-9:
            break
        error = max(
            abs(
                srgb_to_linear(min(1.0, channel * candidate)) * mean
                / srgb_to_linear(channel)
                - 1.0
            )
            for channel in eligible
        )
        if error < best_error:
            best_error = error
            best_compensation = candidate
    return best_compensation, best_error


def build_sheet(spec: HomeSheetSpec) -> tuple[Image.Image, dict]:
    rng = random.Random(spec.seed)
    base = Image.new("L", (SHEET_SIZE, SHEET_SIZE), BASE)
    structure = GRAMMARS[spec.grammar](base, rng)

    macro = fractal_noise(
        ((8, 1.0), (32, 0.62), (128, 0.38), (256, 0.22), (512, 0.12)),
        rng,
    )
    field_image = wrap_filter(
        ImageChops.overlay(structure, macro),
        ImageFilter.SMOOTH,
    )

    tables = cast_tables(spec.cast)
    image = Image.merge(
        "RGB",
        (
            field_image.point(tables[0]),
            field_image.point(tables[1]),
            field_image.point(tables[2]),
        ),
    )
    image, exposure = normalise_luminance(image, spec.mean_target)

    mean = mean_linear_luminance(image)
    compensation, brightness_error = solve_compensation(mean, spec)
    return image, {
        "key": spec.key,
        "grammar": spec.grammar,
        "resourcePath": f"Home/Textures/{spec.key}",
        "meanLinearLuminance": round(mean, 6),
        "albedoCompensation": round(compensation, 6),
        "brightnessError": round(brightness_error, 6),
        "metersPerTile": spec.meters_per_tile,
        "smoothness": spec.smoothness,
        "metallic": spec.metallic,
        "exposure": round(exposure, 6),
        "tints": {
            name: [round(value, 4) for value in tint]
            for name, tint in spec.tints
        },
    }


# --------------------------------------------------------------------------
# validation
# --------------------------------------------------------------------------


def channel_delta(first: tuple[int, ...], second: tuple[int, ...]) -> float:
    return sum(abs(a - b) for a, b in zip(first, second))


def mean_line_delta(image: Image.Image, first: int, second: int) -> float:
    columns = (
        list(image.crop((first, 0, first + 1, SHEET_SIZE)).getdata()),
        list(image.crop((second, 0, second + 1, SHEET_SIZE)).getdata()),
    )
    rows = (
        list(image.crop((0, first, SHEET_SIZE, first + 1)).getdata()),
        list(image.crop((0, second, SHEET_SIZE, second + 1)).getdata()),
    )
    total = sum(
        channel_delta(a, b)
        for a, b in list(zip(*columns)) + list(zip(*rows))
    )
    return total / (SHEET_SIZE * 2 * 3)


def validate(image: Image.Image, spec: HomeSheetSpec, record: dict) -> None:
    if image.size != (SHEET_SIZE, SHEET_SIZE):
        raise ValueError(f"{spec.key} is {image.size}, expected square sheet.")
    if image.mode != "RGB":
        raise ValueError(f"{spec.key} must be opaque RGB, got {image.mode}.")

    edge_delta = mean_line_delta(image, 0, SHEET_SIZE - 1)
    if edge_delta > EDGE_DELTA_LIMIT:
        raise ValueError(
            f"{spec.key} edges diverge by {edge_delta:.2f}, "
            f"limit {EDGE_DELTA_LIMIT} for Repeat sampling."
        )

    interior = sorted(
        mean_line_delta(image, offset, offset + 1)
        for offset in range(0, SHEET_SIZE - 1, 7)
    )
    interior_delta = interior[int(len(interior) * 0.9)]
    seam_ratio = edge_delta / max(1e-6, interior_delta)
    if seam_ratio > SEAM_RATIO_LIMIT:
        raise ValueError(
            f"{spec.key} outer lines differ {seam_ratio:.2f}x more than the "
            f"sheet's strongest interior transition; that is a seam."
        )

    low, high = luminance_percentiles(image, 0.05, 0.95)
    if high - low < spec.contrast_floor:
        raise ValueError(
            f"{spec.key} spans only {high - low} luminance levels, too "
            f"subtle for the 640x360 composite "
            f"(floor {spec.contrast_floor})."
        )

    mean = record["meanLinearLuminance"]
    if abs(mean - spec.mean_target) > MEAN_LUMINANCE_TOLERANCE:
        raise ValueError(
            f"{spec.key} mean linear luminance {mean:.4f} is outside "
            f"{spec.mean_target} +/- {MEAN_LUMINANCE_TOLERANCE}."
        )

    compensation = record["albedoCompensation"]
    brightest = max(
        channel for _, tint in spec.tints for channel in tint
    )
    if brightest * compensation > 1.0 + 1e-6:
        raise ValueError(
            f"{spec.key} compensation {compensation:.4f} would drive a "
            f"builder tint to {brightest * compensation:.4f} and clamp."
        )

    if record["brightnessError"] > BRIGHTNESS_ERROR_LIMIT:
        raise ValueError(
            f"{spec.key} would shift surface brightness by "
            f"{record['brightnessError'] * 100:.1f}%, above the "
            f"{BRIGHTNESS_ERROR_LIMIT * 100:.0f}% allowed."
        )

    means = channel_means(image)
    ratio = max(means) / max(1e-6, min(means))
    if ratio > CHANNEL_RATIO_LIMIT:
        raise ValueError(
            f"{spec.key} channel means {means} differ by {ratio:.3f}; the "
            f"per-renderer tint owns the hue, limit {CHANNEL_RATIO_LIMIT}."
        )

    record["edgeDelta"] = round(edge_delta, 4)
    record["seamRatio"] = round(seam_ratio, 4)
    record["contrast"] = high - low
    record["channelRatio"] = round(ratio, 4)


def build_contact_sheet(
    sheets: list[tuple[HomeSheetSpec, Image.Image]],
) -> Image.Image:
    tile = 168
    columns = 3
    rows = (len(sheets) + columns - 1) // columns
    sheet = Image.new(
        "RGB",
        (columns * (tile * 2 + 12) + 12, rows * (tile + 26) + 12),
        (18, 18, 20),
    )
    draw = ImageDraw.Draw(sheet)
    for index, (spec, image) in enumerate(sheets):
        column = index % columns
        row = index // columns
        x = 12 + column * (tile * 2 + 12)
        y = 12 + row * (tile + 26)
        small = image.resize((tile // 2, tile // 2), Image.Resampling.BOX)
        block = Image.new("RGB", (tile, tile))
        for ty in range(2):
            for tx in range(2):
                block.paste(small, (tx * tile // 2, ty * tile // 2))
        sheet.paste(block, (x, y))
        sheet.paste(block.convert("L").convert("RGB"), (x + tile + 4, y))
        draw.text((x, y + tile + 6), spec.key, fill=(196, 196, 190))
    return sheet


def save_png(image: Image.Image, path: Path) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=False, compress_level=9)
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


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
    built: list[tuple[HomeSheetSpec, Image.Image]] = []
    for spec in HOME_SHEET_SPECS:
        image, record = build_sheet(spec)
        validate(image, spec, record)
        built.append((spec, image))
        if args.verify:
            record["sha256"] = (
                hashlib.sha256(image.tobytes()).hexdigest().upper()
            )
        else:
            record["sha256"] = save_png(
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
        "meanLuminanceTolerance": MEAN_LUMINANCE_TOLERANCE,
        "brightnessErrorLimit": BRIGHTNESS_ERROR_LIMIT,
        "tintChannelFloor": TINT_CHANNEL_FLOOR,
        "sheets": records,
    }
    manifest_path = args.art_source / "home-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = build_contact_sheet(built)
    contact_path = args.art_source / "home-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
