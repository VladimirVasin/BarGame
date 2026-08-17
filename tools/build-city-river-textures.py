#!/usr/bin/env python3
"""Build the deterministic surface sheets for the river.

Two families, because the river is two different rendering problems.

The **albedo** family - four seamless 1024x1024 sheets imported at 512 and
applied by `CityRiverSurfaceAppearance`: each batch's authored flat colour
(brightened by the solved per-sheet compensation) multiplies the sheet in
linear space, exactly like the home/POI/park/cemetery pipelines. The
measured contract - linear luminance rule, wrap-by-construction drawing,
compensation solving and validation - is imported from
`build-home-textures.py`; this script adds one grammar per thing the
embankment is actually made of:

* paving - the promenade underfoot, the stair treads and the lower
  landings: large granite flags in a running bond, tight recessed joints,
  chamfer highlights, polished centres and damp mottling.
* quay   - the retaining wall that holds the river: coursed rusticated
  blocks with deep mortar, staggered per course, plus runoff streaks and
  efflorescence. Deliberately free of any single waterline band - the
  runtime picks each wall's UV offset from a hash, so a band would land
  at a different height on every span.
* iron   - the cast railings, posts, bollards and lamp posts: brushed
  paint over castings, chipped to freckles of bare metal.
* bed    - the channel floor and its submerged sides, seen only as a
  narrow refracted band at the foot of each wall: river silt with
  scattered gravel and the odd sunk stone.

The pitches are metre-true: paving tiles four 0.8 m flags across its
3.2 m, quay four 0.55 m courses across its 2.2 m - about the height of
one wall span - iron repeats every 1.2 m along a rail, and the bed reads
at 2.0 m.

The **water** family - two sheets consumed directly by
`CityRiverWater.shader`, not by the albedo pipeline, because neither is a
diffuse colour:

* water normal - a derivative map of the surface ripple, stored as
  (-dH/du, -dH/dv, 1). It carries no diffuse tint, so the mean-luminance
  rule, the compensation solve and the channel-ratio bound all fail by
  construction on it; it is imported **linear** (`sRGBTexture: 0`) and
  the shader normalizes after unpacking. Its features are stretched
  along V, which the shader maps to world +Z: a river's surface is
  smeared downstream, and an isotropic sheet reads as a pond.
* water foam  - a mask, mostly dark with bright streaks. A mask has no
  business sitting at a mean of 0.5.

Both are still validated for wrap, which is the one contract they share
with the albedos.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import random
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

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
# river grammars
# --------------------------------------------------------------------------


def draw_river_paving(base: Image.Image, rng) -> Image.Image:
    """Granite flags in a running bond: joints, chamfers, worn centres."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((256, 0.55), (512, 0.85)), rng),
    )
    draw = ImageDraw.Draw(image)

    flag = SHEET_SIZE // 4
    joint = home.BASE - 46
    chamfer = home.BASE + 20

    # Four courses of four flags, every other course shifted half a flag,
    # so the sheet reads as laid stone instead of bathroom tile. Both the
    # course count and the shift divide the sheet, so the bond survives
    # wrapping.
    for row in range(4):
        shift = (row % 2) * (flag // 2)
        for column in range(4):
            x0 = column * flag + shift
            y0 = row * flag
            x1 = x0 + flag
            y1 = y0 + flag

            # The flag itself, a couple of levels off its neighbours.
            home.wrap_rect(
                draw,
                (x0 + 3, y0 + 3, x1 - 3, y1 - 3),
                home.BASE + rng.choice((-11, -6, -2, 4, 9, 13)),
            )

            # Foot traffic polishes the middle of a flag. An oval wear
            # patch reads as that; an inset rectangle reads as a border
            # scored into the stone, which is not a thing granite does.
            inset = rng.randint(30, 58)
            home.wrap_ellipse(
                draw,
                (x0 + inset, y0 + inset, x1 - inset, y1 - inset),
                fill=home.BASE + rng.randint(5, 11),
            )

            # Chamfer: the lit top and left arris of each flag.
            home.wrap_line(draw, (x0 + 3, y0 + 3, x1 - 3, y0 + 3), chamfer)
            home.wrap_line(draw, (x0 + 3, y0 + 3, x0 + 3, y1 - 3), chamfer)

            # The recessed joint, drawn last so nothing paints over it,
            # and centred on the grid line rather than laid to its right:
            # a one-sided joint would make column 0 mortar and column
            # 1023 flag, which is exactly a seam under Repeat sampling.
            home.wrap_rect(draw, (x0 - 2, y0, x0 + 2, y1), joint)
            home.wrap_rect(draw, (x0, y0 - 2, x1, y0 + 2), joint)

    # Granite grain: quartz and feldspar a pixel or two wide, dense
    # enough to hold at half a metre and quiet at ten.
    for _ in range(9000):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        side = rng.randint(1, 2)
        tone = home.BASE + rng.choice((-19, -13, 11, 16))
        home.wrap_rect(draw, (x, y, x + side, y + side), tone)

    # A few hairline cracks running across the bond.
    for _ in range(7):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        for _ in range(rng.randint(3, 7)):
            nx = x + rng.uniform(-70.0, 70.0)
            ny = y + rng.uniform(25.0, 80.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                home.BASE - rng.randint(26, 38),
                width=1,
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # Damp: broad soft staining, the river never quite drying off the
    # stone. Blurred through the wrap-aware filter, so no seam.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(11):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(150, 380), y + rng.randint(90, 240)),
                fill=rng.randint(110, 120),
            )
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 210), y + rng.randint(60, 150)),
                fill=rng.randint(134, 142),
            )

    return home.soft_overlay(image, 13, weathering)


def draw_river_quay(base: Image.Image, rng) -> Image.Image:
    """Coursed rusticated blocks: deep mortar, staggered, streaked."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((128, 0.6), (256, 0.6), (512, 0.7)), rng),
    )
    draw = ImageDraw.Draw(image)

    course = SHEET_SIZE // 4
    block = SHEET_SIZE // 2
    mortar = home.BASE - 52

    for row in range(4):
        shift = (row % 2) * (block // 2)
        y0 = row * course
        y1 = y0 + course
        for column in range(2):
            x0 = column * block + shift
            x1 = x0 + block

            face = home.BASE + rng.choice((-13, -7, -1, 5, 11))
            home.wrap_rect(draw, (x0 + 5, y0 + 5, x1 - 5, y1 - 5), face)

            # Rustication: the block's face stands proud of its margin,
            # so the top and left arris catch light and the bottom and
            # right drop into shadow.
            margin = rng.randint(14, 22)
            home.wrap_rect(
                draw,
                (x0 + margin, y0 + margin, x1 - margin, y1 - margin),
                face + rng.randint(7, 13),
            )
            home.wrap_line(
                draw,
                (x0 + margin, y1 - margin, x1 - margin, y1 - margin),
                face - rng.randint(20, 30),
                width=3,
            )
            home.wrap_line(
                draw,
                (x1 - margin, y0 + margin, x1 - margin, y1 - margin),
                face - rng.randint(20, 30),
                width=3,
            )
            home.wrap_line(
                draw,
                (x0 + margin, y0 + margin, x1 - margin, y0 + margin),
                face + rng.randint(14, 22),
                width=2,
            )

            # Pitted stone: the tooled face of a quay block.
            for _ in range(320):
                px = x0 + rng.randrange(block)
                py = y0 + rng.randrange(course)
                radius = rng.randint(1, 3)
                home.wrap_ellipse(
                    draw,
                    (px, py, px + radius, py + radius),
                    fill=face + rng.choice((-16, -11, 9, 14)),
                )

            # The mortar bed and the vertical joint, cut last and centred
            # on the course line so both sheet edges land in mortar.
            home.wrap_rect(draw, (x0 - 2, y0, x0 + 2, y1), mortar)
            home.wrap_rect(draw, (x0, y0 - 2, x1, y0 + 2), mortar)

    # Runoff: rain and river spray leave vertical stains that read at any
    # UV offset, which is why there is no single waterline band here.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(16):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (
                    x,
                    y,
                    x + rng.randint(14, 40),
                    y + rng.randint(160, 420),
                ),
                fill=rng.randint(104, 116),
            )
        # Efflorescence: pale salt bloom crusting out of the mortar.
        for _ in range(9):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(70, 190), y + rng.randint(40, 120)),
                fill=rng.randint(136, 146),
            )

    return home.soft_overlay(image, 10, weathering)


def draw_river_iron(base: Image.Image, rng) -> Image.Image:
    """Painted cast iron: brush streaks, casting pits, chipped freckles."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.5), (256, 0.7), (512, 0.6)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Brushwork: long streaks along the rail. A railing is painted by
    # hand and repainted for decades, so the direction has to show -
    # without it the sheet reads as poured concrete, not painted iron.
    for _ in range(520):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        length = rng.randint(200, 760)
        home.wrap_line(
            draw,
            (x, y, x + length, y + rng.uniform(-5.0, 5.0)),
            home.BASE + rng.choice((-17, -12, -8, 9, 13, 17)),
            width=rng.randint(1, 4),
        )

    # Casting pits: the iron underneath was never smooth.
    for _ in range(2400):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(1, 3)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius, y + radius),
            fill=home.BASE - rng.randint(14, 26),
        )

    # Chips: paint knocked off to bare metal, each a bright lip around a
    # darker pit. These carry the sheet's contrast.
    for _ in range(300):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(5, 16)
        height = rng.randint(4, 12)
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + height),
            fill=home.BASE + rng.randint(26, 38),
        )
        home.wrap_ellipse(
            draw,
            (x + 1, y + 1, x + width - 1, y + height - 1),
            fill=home.BASE - rng.randint(6, 18),
        )

    # Rust freckles clustering out of the chips.
    for _ in range(150):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        for _ in range(rng.randint(3, 9)):
            x = cx + rng.randint(-16, 16)
            y = cy + rng.randint(-16, 16)
            radius = rng.randint(1, 3)
            home.wrap_ellipse(
                draw,
                (x, y, x + radius, y + radius),
                fill=home.BASE + rng.randint(12, 20),
            )

    # Drips and grime, softened: paint runs and the river's own dirt.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(14):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(10, 26), y + rng.randint(90, 260)),
                fill=rng.randint(108, 118),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 160), y + rng.randint(40, 110)),
                fill=rng.randint(132, 142),
            )

    return home.soft_overlay(image, 8, weathering)


def draw_river_bed(base: Image.Image, rng) -> Image.Image:
    """River silt: fine sediment, scattered gravel, a few sunk stones."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.5), (128, 0.7), (512, 0.8)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Sunk stones: the few pieces big enough to read as shapes rather
    # than grain. Lit crown, shadowed skirt, because they sit proud of
    # the silt rather than in it.
    for _ in range(90):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(18, 52)
        height = int(width * rng.uniform(0.6, 0.95))
        tone = home.BASE + rng.choice((-14, -8, 6, 12))
        home.wrap_ellipse(draw, (x, y, x + width, y + height), fill=tone)
        home.wrap_ellipse(
            draw,
            (x + 2, y + 2, x + width - 3, y + height // 2),
            fill=tone + rng.randint(9, 16),
        )
        home.wrap_ellipse(
            draw,
            (x + 3, y + height // 2, x + width - 3, y + height - 1),
            fill=tone - rng.randint(10, 18),
        )

    # Gravel: the grade that actually covers the floor.
    for _ in range(5200):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(2, 6)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius, y + radius),
            fill=home.BASE + rng.choice((-22, -15, -9, 10, 17, 23)),
        )

    # Silt grain, fine enough to disappear at a metre and hold the
    # sheet's contrast floor at half of one.
    for _ in range(11000):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_rect(
            draw,
            (x, y, x + 1, y + 1),
            home.BASE + rng.choice((-18, -11, 9, 15)),
        )

    # Current ripples in the sediment: shallow dunes lying across the
    # flow, the one thing that says this floor has water moving over it.
    for _ in range(26):
        y = float(rng.randrange(SHEET_SIZE))
        x = float(rng.randrange(SHEET_SIZE))
        for _ in range(rng.randint(4, 8)):
            nx = x + rng.uniform(90.0, 200.0)
            ny = y + rng.uniform(-18.0, 18.0)
            home.wrap_line(
                draw,
                (x, y, nx, ny),
                home.BASE + rng.choice((-16, 14)),
                width=rng.randint(2, 5),
            )
            x, y = nx % SHEET_SIZE, ny % SHEET_SIZE

    # Settled muck pooling in the hollows.
    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(13):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(140, 340), y + rng.randint(90, 220)),
                fill=rng.randint(106, 118),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(80, 180), y + rng.randint(50, 130)),
                fill=rng.randint(134, 144),
            )

    return home.soft_overlay(image, 12, weathering)


home.GRAMMARS["river_paving"] = draw_river_paving
home.GRAMMARS["river_quay"] = draw_river_quay
home.GRAMMARS["river_iron"] = draw_river_iron
home.GRAMMARS["river_bed"] = draw_river_bed


# --------------------------------------------------------------------------
# water sheets — consumed by the shader, not by the albedo pipeline
# --------------------------------------------------------------------------


@dataclass(frozen=True)
class WaterSheetSpec:
    """One sheet the water shader samples directly.

    `linear` records the import the sheet needs; the normal map is the
    project's first non-sRGB texture and the EditMode contract test reads
    this field to pin that.
    """

    key: str
    grammar: str
    seed: int
    meters_per_tile: float
    linear: bool


def periodic_noise_rect(
    lattice_x: int,
    lattice_y: int,
    rng: random.Random,
) -> Image.Image:
    """`home.periodic_noise` on a rectangular lattice.

    A square lattice can only make round blobs. The river needs features
    longer than they are wide, so the two axes take separate cell counts:
    fewer cells down the sheet means taller cells, and V is downstream.
    """
    if SHEET_SIZE % lattice_x != 0 or SHEET_SIZE % lattice_y != 0:
        raise ValueError(
            f"Lattice {lattice_x}x{lattice_y} does not divide the "
            f"{SHEET_SIZE} sheet."
        )

    tile = Image.new("L", (lattice_x, lattice_y))
    tile.putdata(
        [rng.randrange(256) for _ in range(lattice_x * lattice_y)]
    )
    tripled = Image.new("L", (lattice_x * 3, lattice_y * 3))
    for row in range(3):
        for column in range(3):
            tripled.paste(tile, (column * lattice_x, row * lattice_y))
    grown = tripled.resize(
        (SHEET_SIZE * 3, SHEET_SIZE * 3),
        Image.Resampling.BICUBIC,
    )
    return grown.crop(
        (SHEET_SIZE, SHEET_SIZE, SHEET_SIZE * 2, SHEET_SIZE * 2)
    )


def stretched_fractal(
    octaves: tuple[tuple[int, int, float], ...],
    rng: random.Random,
) -> Image.Image:
    accumulated: Image.Image | None = None
    weight_sum = 0.0
    for lattice_x, lattice_y, weight in octaves:
        layer = periodic_noise_rect(lattice_x, lattice_y, rng)
        weight_sum += weight
        accumulated = (
            layer
            if accumulated is None
            else Image.blend(accumulated, layer, weight / weight_sum)
        )
    if accumulated is None:
        raise ValueError("Stretched fractal needs at least one octave.")
    return accumulated


def draw_water_ripple_height(rng: random.Random) -> Image.Image:
    """The height field the normal map differentiates.

    Four stretched octaves carry the body of the ripple; the drawn
    streaks on top are what stops it reading as generic noise. Everything
    is blurred at the end - the sheet is differentiated next, and a hard
    edge in height is a mirror-bright crease in the normal.
    """
    height = stretched_fractal(
        ((32, 8, 1.0), (64, 16, 0.75), (128, 32, 0.45), (256, 128, 0.22)),
        rng,
    )
    draw = ImageDraw.Draw(height)

    # Downstream shear lines: the long, faint creases a current drags
    # along itself.
    for _ in range(220):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        length = rng.randint(180, 620)
        home.wrap_line(
            draw,
            (x + rng.uniform(-24.0, 24.0), y, x, y + length),
            128 + rng.choice((-38, -26, -16, 18, 28, 40)),
            width=rng.randint(2, 7),
        )

    # Capillary chop: short crests lying across the flow, the small
    # high-frequency detail that survives the 640x360 composite as a
    # glitter rather than as shapes.
    for _ in range(900):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(12, 34), y + rng.randint(3, 8)),
            fill=128 + rng.choice((-30, -20, 22, 32)),
        )

    return home.wrap_filter(height, ImageFilter.GaussianBlur(1.7), pad=14)


def channel_span(channel: Image.Image, low: float, high: float) -> int:
    """Percentile span of one 8-bit channel.

    `home.luminance_percentiles` converts to L first, which on a
    derivative map would weigh dU against dV through Rec601 and report
    the span of a quantity that is not in the sheet.
    """
    bins = channel.histogram()
    pixels = channel.width * channel.height
    edges: list[int] = []
    for fraction in (low, high):
        threshold = pixels * fraction
        running = 0
        chosen = 255
        for value, count in enumerate(bins):
            running += count
            if running >= threshold:
                chosen = value
                break
        edges.append(chosen)
    return edges[1] - edges[0]


def build_water_normal(spec: WaterSheetSpec) -> tuple[Image.Image, dict]:
    """Differentiate the ripple height into (-dH/du, -dH/dv, 1).

    Stored unnormalized on purpose: the shader normalizes anyway, and
    leaving Z at full lets `_NormalStrength` scale XY against it without
    the sheet having baked a strength in. The kernels are signed so no
    inversion pass is needed - inverting an 8-bit channel maps the
    neutral 128 to 127 and tilts the whole sheet half a level.
    """
    rng = random.Random(spec.seed)
    height = draw_water_ripple_height(rng)

    negative_dx = ImageFilter.Kernel(
        (3, 3),
        (1, 0, -1, 2, 0, -2, 1, 0, -1),
        scale=2,
        offset=128,
    )
    negative_dy = ImageFilter.Kernel(
        (3, 3),
        (1, 2, 1, 0, 0, 0, -1, -2, -1),
        scale=2,
        offset=128,
    )
    red = home.wrap_filter(height, negative_dx, pad=8)
    green = home.wrap_filter(height, negative_dy, pad=8)
    blue = Image.new("L", (SHEET_SIZE, SHEET_SIZE), 255)
    image = Image.merge("RGB", (red, green, blue))

    return image, {
        "key": spec.key,
        "grammar": spec.grammar,
        "resourcePath": f"Textures/{spec.key}",
        "metersPerTile": spec.meters_per_tile,
        "linear": spec.linear,
        "slopeSpan": max(
            channel_span(red, 0.02, 0.98),
            channel_span(green, 0.02, 0.98),
        ),
    }


def build_water_foam(spec: WaterSheetSpec) -> tuple[Image.Image, dict]:
    """A mask: dark water, bright foam, streaked downstream."""
    rng = random.Random(spec.seed)
    image = ImageChops.multiply(
        stretched_fractal(
            ((32, 8, 1.0), (128, 32, 0.6), (256, 64, 0.4)),
            rng,
        ),
        Image.new("L", (SHEET_SIZE, SHEET_SIZE), 108),
    )
    draw = ImageDraw.Draw(image)

    # The foam itself: torn streaks running downstream, fraying out
    # behind. Sparse on purpose - this is what the shader multiplies a
    # depth threshold by, so the sheet has to be mostly water. Foam that
    # covers the sheet cannot be thresholded into a band at the bank; it
    # just brightens the whole river.
    for _ in range(110):
        x = float(rng.randrange(SHEET_SIZE))
        y = float(rng.randrange(SHEET_SIZE))
        tone = rng.randint(200, 255)
        length = rng.randint(60, 300)
        home.wrap_line(
            draw,
            (x, y, x + rng.uniform(-20.0, 20.0), y + length),
            tone,
            width=rng.randint(2, 6),
        )
        for _ in range(rng.randint(2, 5)):
            fx = x + rng.uniform(-14.0, 14.0)
            fy = y + rng.uniform(0.0, length)
            radius = rng.randint(3, 9)
            home.wrap_ellipse(
                draw,
                (fx, fy, fx + radius, fy + radius),
                fill=rng.randint(180, 245),
            )

    # Clots: where the streaks pile into each other.
    for _ in range(26):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(14, 40)
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + int(width * rng.uniform(0.8, 1.9))),
            fill=rng.randint(170, 230),
        )

    # Bubble speckle, so the mask has something to say up close.
    for _ in range(3000):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(1, 3)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius, y + radius),
            fill=rng.randint(150, 255),
        )

    softened = home.wrap_filter(
        image,
        ImageFilter.GaussianBlur(1.4),
        pad=10,
    )
    return Image.merge("RGB", (softened, softened, softened)), {
        "key": spec.key,
        "grammar": spec.grammar,
        "resourcePath": f"Textures/{spec.key}",
        "metersPerTile": spec.meters_per_tile,
        "linear": spec.linear,
        "coverage": round(
            sum(softened.histogram()[171:]) / float(SHEET_SIZE * SHEET_SIZE),
            6,
        ),
    }


WATER_BUILDERS = {
    "river_water_normal": build_water_normal,
    "river_water_foam": build_water_foam,
}


def validate_water(
    image: Image.Image,
    spec: WaterSheetSpec,
    record: dict,
) -> None:
    """The one contract a water sheet shares with an albedo: it wraps.

    Everything else the albedo validator checks - mean linear luminance,
    solved compensation, channel balance - describes a diffuse colour
    multiplied by a builder tint, which is not what either of these
    sheets is.
    """
    if image.size != (SHEET_SIZE, SHEET_SIZE):
        raise ValueError(f"{spec.key} is {image.size}, expected square sheet.")
    if image.mode != "RGB":
        raise ValueError(f"{spec.key} must be opaque RGB, got {image.mode}.")

    edge_delta = home.mean_line_delta(image, 0, SHEET_SIZE - 1)
    if edge_delta > home.EDGE_DELTA_LIMIT:
        raise ValueError(
            f"{spec.key} edges diverge by {edge_delta:.2f}, "
            f"limit {home.EDGE_DELTA_LIMIT} for Repeat sampling."
        )

    interior = sorted(
        home.mean_line_delta(image, offset, offset + 1)
        for offset in range(0, SHEET_SIZE - 1, 7)
    )
    interior_delta = interior[int(len(interior) * 0.9)]
    seam_ratio = edge_delta / max(1e-6, interior_delta)
    if seam_ratio > home.SEAM_RATIO_LIMIT:
        raise ValueError(
            f"{spec.key} outer lines differ {seam_ratio:.2f}x more than the "
            f"sheet's strongest interior transition; that is a seam."
        )

    if spec.grammar == "river_water_normal":
        # A derivative map that has clipped its slopes has lost the
        # ripple, not compressed it: both tails pin to 0 and 255 and the
        # surface shades as a set of flat facets.
        span = record["slopeSpan"]
        if not 40 <= span <= 190:
            raise ValueError(
                f"{spec.key} slope span {span} is outside 40..190; the "
                f"ripple is either too flat to light or clipping."
            )
    else:
        coverage = record["coverage"]
        if not 0.03 <= coverage <= 0.30:
            raise ValueError(
                f"{spec.key} foam covers {coverage * 100:.1f}% of the "
                f"sheet, outside 3..30%; a mask this full or this empty "
                f"cannot be thresholded by depth at the bank."
            )

    record["edgeDelta"] = round(edge_delta, 4)
    record["seamRatio"] = round(seam_ratio, 4)


# --------------------------------------------------------------------------
# specs — tints transcribe CityRiverWorldBuilder's palette
# --------------------------------------------------------------------------


RIVER_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="CityRiverPavingAlbedo",
        grammar="river_paving",
        seed=0x52565047,
        cast=(1.00, 1.01, 0.99),
        mean_target=0.50,
        meters_per_tile=3.2,
        smoothness=0.10,
        metallic=0.0,
        tints=(
            ("CityRiverWorldBuilder.Granite", (0.340, 0.360, 0.340)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityRiverQuayAlbedo",
        grammar="river_quay",
        seed=0x52565157,
        cast=(0.99, 1.00, 1.01),
        mean_target=0.50,
        meters_per_tile=2.2,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("CityRiverWorldBuilder.GraniteEdge", (0.250, 0.280, 0.270)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityRiverIronAlbedo",
        grammar="river_iron",
        seed=0x5256524E,
        cast=(1.00, 1.00, 1.02),
        mean_target=0.50,
        meters_per_tile=1.2,
        smoothness=0.18,
        metallic=0.20,
        tints=(
            ("CityRiverWorldBuilder.Iron", (0.075, 0.100, 0.105)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityRiverBedAlbedo",
        grammar="river_bed",
        seed=0x52564244,
        cast=(1.02, 1.00, 0.97),
        mean_target=0.50,
        meters_per_tile=2.0,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("CityRiverWorldBuilder.Riverbed", (0.185, 0.190, 0.155)),
        ),
    ),
)


WATER_SHEET_SPECS: tuple[WaterSheetSpec, ...] = (
    WaterSheetSpec(
        key="CityRiverWaterNormal",
        grammar="river_water_normal",
        seed=0x52565752,
        meters_per_tile=4.0,
        linear=True,
    ),
    WaterSheetSpec(
        key="CityRiverWaterFoam",
        grammar="river_water_foam",
        seed=0x52565746,
        meters_per_tile=3.0,
        linear=False,
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
    for spec in RIVER_SHEET_SPECS:
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

    water_records: list[dict] = []
    for water_spec in WATER_SHEET_SPECS:
        image, record = WATER_BUILDERS[water_spec.grammar](water_spec)
        validate_water(image, water_spec, record)
        built.append((water_spec, image))
        if args.verify:
            record["sha256"] = (
                hashlib.sha256(image.tobytes()).hexdigest().upper()
            )
        else:
            record["sha256"] = home.save_png(
                image,
                args.textures / f"{water_spec.key}.png",
            )
        water_records.append(record)
        measured = (
            f"slope={record['slopeSpan']}"
            if "slopeSpan" in record
            else f"coverage={record['coverage'] * 100:.1f}%"
        )
        print(
            f"{'Checked' if args.verify else 'Wrote'} {water_spec.key} "
            f"({image.width}x{image.height}) "
            f"{'linear' if water_spec.linear else 'sRGB'} "
            f"{measured} "
            f"edge={record['edgeDelta']:.2f} "
            f"seam={record['seamRatio']:.2f}x"
        )

    if args.verify:
        print(
            f"Validated {len(records)} albedo and {len(water_records)} "
            f"water sheets; nothing written."
        )
        return

    manifest = {
        "sheetSize": SHEET_SIZE,
        "meanLuminanceTolerance": home.MEAN_LUMINANCE_TOLERANCE,
        "brightnessErrorLimit": home.BRIGHTNESS_ERROR_LIMIT,
        "tintChannelFloor": home.TINT_CHANNEL_FLOOR,
        "sheets": records,
        "waterSheets": water_records,
    }
    manifest_path = args.art_source / "river-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "river-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
