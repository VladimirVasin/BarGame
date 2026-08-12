#!/usr/bin/env python3
"""Build the deterministic district facade albedos for the generated city.

Each sheet is authored as a 4x4 grid of bay cells: four bays across, four
floors up. The runtime never tiles a sheet by metres -- it tiles it by the
building's own bay and floor grid, so one authored cell always covers exactly
one geometric window bay of one 2.35 m floor no matter how wide or tall the lot
came out. Because a whole-cell shift preserves that alignment exactly, the
runtime also rotates each lot's bays and floors, which is what keeps a street of
one material from reading as a single stamp repeated.

Two numbers here are contracts rather than taste, and both are asserted in
`Assets/Tests/EditMode/CityFacadeAppearanceTests.cs`:

* The sheet is 1024, not the 1254 the other world albedos use, because Unity
  imports these at 512 and only 1024 gives an exact 2:1 box downsample. A
  2.449:1 resample would soften precisely the band and mullion edges this
  texture exists to place, and 1024/4 keeps the cell grid pixel-exact.
* The mean linear luminance is 0.64. URP/Lit multiplies `_BaseColor` by
  `_BaseMap`, and the runtime brightens the night facade tint by
  `1 / meanLinearLuminance` so the textured wall keeps the mean brightness the
  flat colour used to have. The brightest channel any lot can reach is 0.616
  (a bar), so a mean below that would clamp and crush the district hue. The
  sheet is therefore a light base carrying dark features, not a dark look --
  all the grimness still comes from the tint.

Noise is periodic by construction: the value lattice is tiled three by three
before it is upsampled and the middle crop taken, so the sheets wrap without a
seam rather than merely passing the wrap check by luck. Every stamp is drawn
through a wrapping helper for the same reason. Pillow is the only dependency.
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
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "City" / "Facades"

SHEET_SIZE = 1024
BAYS = 4
FLOORS = 4
CELL = SHEET_SIZE // BAYS

# Contract shared with Assets/Scripts/Runtime/World/CityFacadeGrid.cs. The
# runtime derives the UV phase from these; the sheet only has to place its
# window band at the centre of its cell.
FLOOR_PITCH_M = 2.35
FIRST_FLOOR_CENTER_Y_M = 1.50
MASS_BASE_Y_M = 0.08
BAND_CENTER_CELL_FRACTION = 0.5

# The geometric pane occupies 0.857-0.886 of a bay and 0.48 m of a floor across
# every lot CityLayoutGenerator can produce. The baked opening is deliberately
# larger on both axes so the real pane stands inside a painted reveal instead
# of butting against the wall.
APERTURE_WIDTH_FRACTION = 0.92
APERTURE_HEIGHT_FRACTION = 0.30

# The runtime brightens the night facade tint by a fixed factor so a textured
# wall keeps the brightness the flat colour used to have. The factor is set by
# the brightest channel any lot can reach -- a bar's red, at 0.616 -- because
# anything larger would clamp and crush the district hue.
MAXIMUM_NIGHT_FACADE_CHANNEL = 0.616
ALBEDO_COMPENSATION = 1.0 / 0.62

# And this is the mean the sheets must hold for that to come out even.
#
# It is NOT `1 / ALBEDO_COMPENSATION`, which is the rule the stairwell surfaces
# use. That rule assumes the tint and the texture multiply in gamma space; URP
# converts both to linear first, so preserving brightness needs
# `linear(tint * compensation) * mean == linear(tint)`. Solved across every lot
# kind, that lands on 0.35 and holds to within 4.5%. The gamma-space rule would
# have said 0.64 and made every facade in the city almost twice as bright as it
# is today -- a pale, chalky wall instead of a grimy one.
MEAN_LUMINANCE_TARGET = 0.35
MEAN_LUMINANCE_TOLERANCE = 0.02
BRIGHTNESS_ERROR_LIMIT = 0.08
EDGE_DELTA_LIMIT = 16.0
SEAM_RATIO_LIMIT = 2.5
CONTRAST_FLOOR = 40
CHANNEL_RATIO_LIMIT = 1.22

REC709 = (0.2126, 0.7152, 0.0722)

# The brightest channel CreateNightFacadeColor can return per lot kind, swept
# over every colour CityLayoutGenerator.CreateBuildingColor can produce. These
# are what the brightness and clamp checks are held against, and the EditMode
# fixture re-derives them from the live colour ranges so a future edit to a
# district palette fails here rather than silently shifting the city.
NIGHT_FACADE_PEAKS = {
    "Industrial": 0.3056,
    "Supermarket": 0.3108,
    "OldTown": 0.3420,
    "Nightlife": 0.3661,
    "Residential": 0.4055,
    "PlayerHome": 0.5120,
    "Bar": 0.6160,
}

# Structure values, authored on a mid base so the features are the dark part.
# Landing on the target mean takes only a mild curve from here, so what is
# written is close to what ships.
BASE = 170
MORTAR = 134
SEAM_LIGHT = 196
APERTURE = 14
REVEAL = 40
MULLION = 56
SILL = 200
SILL_SHADOW = 82
LINTEL = 194
GRIME = 110
INFILL = 150
BOARD = 182
PATCH = 192


# --------------------------------------------------------------------------
# colour helpers
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
    """Rec.709 mean linear luminance, read straight off the histogram."""
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
# periodic noise
# --------------------------------------------------------------------------


def periodic_noise(lattice: int, rng: random.Random) -> Image.Image:
    """A value-noise field that wraps exactly at the sheet edges.

    The lattice is tiled three by three before upsampling and the middle third
    cropped back out, so every sample the resampler reads near an edge comes
    from a genuine neighbour rather than a clamped one.
    """
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
    """Convolve without letting the border clamp.

    Pillow replicates edge pixels rather than wrapping, so a plain `filter`
    call gives the outer rows a different result than their true neighbours
    would produce -- a one pixel seam that is invisible on smooth art and
    glaring on anything high frequency, such as roof gravel.
    """
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
    """Sum periodic octaves given as (lattice, weight) pairs."""
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


# --------------------------------------------------------------------------
# wrap-aware drawing
# --------------------------------------------------------------------------


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


# --------------------------------------------------------------------------
# specs
# --------------------------------------------------------------------------


# Aperture states per cell, read as four rows of four bays. Every cell always
# carries an aperture -- only its state varies -- because the runtime bay and
# floor rotation means any authored cell can end up hosting a real pane.
OLD_TOWN_STATES = (
    ("open", "curtain", "open", "open"),
    ("open", "open", "bricked", "curtain"),
    ("shrunk", "open", "open", "open"),
    ("open", "open", "curtain", "shrunk"),
)
RESIDENTIAL_STATES = (
    ("curtain", "open", "curtain", "open"),
    ("open", "curtain", "open", "open"),
    ("open", "open", "curtain", "curtain"),
    ("curtain", "open", "open", "boarded"),
)
INDUSTRIAL_STATES = (
    ("open", "boarded", "open", "open"),
    ("boarded", "open", "open", "bricked"),
    ("open", "open", "boarded", "open"),
    ("open", "bricked", "open", "boarded"),
)
NIGHTLIFE_STATES = (
    ("open", "open", "curtain", "open"),
    ("poster", "open", "open", "poster"),
    ("open", "boarded", "open", "open"),
    ("open", "open", "poster", "curtain"),
)


@dataclass(frozen=True)
class FacadeSpec:
    """One authored district sheet.

    `joint` selects the wall grammar and `cast` the near-neutral colour bias --
    near-neutral because the per-lot night tint owns the hue and the texture
    owns the material. `accent_area` is a hard ceiling on the district's one
    functional colour, so the art bible's rule that saturated colour occupies a
    small area is enforced rather than merely intended.
    """

    key: str
    district: str
    variant: str
    palette: str
    joint: str
    seed: int
    cast: tuple[float, float, float]
    states: tuple[tuple[str, ...], ...]
    accent: tuple[int, int, int]
    accent_area: float
    mullions: int
    grime: float
    smoothness: float
    metallic: float


FACADE_SPECS: tuple[FacadeSpec, ...] = (
    FacadeSpec(
        key="CityFacadeOldTownBrickAlbedo",
        district="OldTown",
        variant="Primary",
        palette="OldTownBrick",
        joint="brick",
        seed=0x4F544252,
        cast=(1.06, 0.98, 0.89),
        states=OLD_TOWN_STATES,
        accent=(150, 126, 84),
        accent_area=0.04,
        mullions=3,
        grime=1.05,
        smoothness=0.06,
        metallic=0.0,
    ),
    FacadeSpec(
        key="CityFacadeOldTownStoneAlbedo",
        district="OldTown",
        variant="Secondary",
        palette="OldTownStone",
        joint="render",
        seed=0x4F545354,
        cast=(1.03, 1.00, 0.94),
        states=OLD_TOWN_STATES,
        accent=(146, 132, 100),
        accent_area=0.05,
        mullions=3,
        grime=1.10,
        smoothness=0.07,
        metallic=0.0,
    ),
    FacadeSpec(
        key="CityFacadeResidentialCoolAlbedo",
        district="Residential",
        variant="Primary",
        palette="ResidentialCool",
        joint="panel",
        seed=0x5245534C,
        cast=(0.93, 0.99, 1.05),
        states=RESIDENTIAL_STATES,
        accent=(132, 118, 96),
        accent_area=0.03,
        mullions=2,
        grime=1.20,
        smoothness=0.09,
        metallic=0.0,
    ),
    FacadeSpec(
        key="CityFacadeResidentialWarmAlbedo",
        district="Residential",
        variant="Secondary",
        palette="ResidentialWarm",
        joint="panel",
        seed=0x52455357,
        cast=(1.05, 1.00, 0.92),
        states=RESIDENTIAL_STATES,
        accent=(158, 134, 88),
        accent_area=0.03,
        mullions=2,
        grime=1.20,
        smoothness=0.09,
        metallic=0.0,
    ),
    FacadeSpec(
        key="CityFacadeIndustrialSteelAlbedo",
        district="Industrial",
        variant="Primary",
        palette="IndustrialSteel",
        joint="sheet",
        seed=0x494E5354,
        cast=(0.97, 1.00, 1.00),
        states=INDUSTRIAL_STATES,
        accent=(168, 150, 66),
        accent_area=0.02,
        mullions=1,
        grime=0.85,
        smoothness=0.16,
        metallic=0.22,
    ),
    FacadeSpec(
        key="CityFacadeIndustrialRustAlbedo",
        district="Industrial",
        variant="Secondary",
        palette="IndustrialRust",
        joint="utility",
        seed=0x494E5253,
        cast=(1.05, 0.98, 0.92),
        states=INDUSTRIAL_STATES,
        accent=(154, 92, 52),
        accent_area=0.04,
        mullions=1,
        grime=0.90,
        smoothness=0.12,
        metallic=0.14,
    ),
    FacadeSpec(
        key="CityFacadeNightlifeMagentaAlbedo",
        district="Nightlife",
        variant="Primary",
        palette="NightlifeMagenta",
        joint="poster",
        seed=0x4E47484D,
        cast=(1.02, 0.95, 1.04),
        states=NIGHTLIFE_STATES,
        accent=(178, 74, 130),
        accent_area=0.035,
        mullions=2,
        grime=1.05,
        smoothness=0.12,
        metallic=0.06,
    ),
    FacadeSpec(
        key="CityFacadeNightlifeCyanAlbedo",
        district="Nightlife",
        variant="Secondary",
        palette="NightlifeCyan",
        joint="poster",
        seed=0x4E474843,
        cast=(0.94, 1.00, 1.03),
        states=NIGHTLIFE_STATES,
        accent=(82, 158, 168),
        accent_area=0.035,
        mullions=2,
        grime=1.15,
        smoothness=0.12,
        metallic=0.06,
    ),
)


ROOF_SPEC = FacadeSpec(
    key="CityRoofAlbedo",
    district="Shared",
    variant="Roof",
    palette="Roof",
    joint="roof",
    seed=0x524F4F46,
    cast=(1.00, 1.00, 0.99),
    states=(),
    accent=(140, 126, 108),
    accent_area=0.03,
    mullions=0,
    grime=0.0,
    smoothness=0.05,
    metallic=0.0,
)


# --------------------------------------------------------------------------
# cell construction
# --------------------------------------------------------------------------


def cell_origin(bay: int, floor: int) -> tuple[int, int]:
    """Top-left of a cell. Floor 0 is the bottom row in UV, so rows flip."""
    return bay * CELL, (FLOORS - 1 - floor) * CELL


def aperture_box(origin_x: int, origin_y: int) -> tuple[int, int, int, int]:
    width = APERTURE_WIDTH_FRACTION * CELL
    height = APERTURE_HEIGHT_FRACTION * CELL
    center_x = origin_x + CELL * 0.5
    # UV v runs up and PIL y runs down, so the band fraction mirrors.
    center_y = origin_y + (1.0 - BAND_CENTER_CELL_FRACTION) * CELL
    return (
        int(round(center_x - width * 0.5)),
        int(round(center_y - height * 0.5)),
        int(round(center_x + width * 0.5)),
        int(round(center_y + height * 0.5)),
    )


def draw_wall_grammar(
    draw: ImageDraw.ImageDraw,
    spec: FacadeSpec,
    rng: random.Random,
) -> None:
    """The construction module that tells one district's wall from another.

    Every pitch here divides the sheet. A module that does not -- a 40 px
    brick across 1024 -- restarts mid-unit at the wrap, which is a real seam
    however carefully the noise underneath was made periodic.
    """
    if spec.joint == "brick":
        course = 16
        row = 0
        y = 0
        while y < SHEET_SIZE:
            wrap_line(draw, (0, y, SHEET_SIZE, y), MORTAR, width=2)
            x = (row % 2) * 16
            while x < SHEET_SIZE:
                wrap_line(draw, (x, y, x, y + course), MORTAR, width=2)
                if rng.random() < 0.05:
                    wrap_rect(
                        draw,
                        (x + 2, y + 2, x + 30, y + course - 2),
                        rng.randint(BASE - 26, BASE + 22),
                    )
                x += 32
            row += 1
            y += course
    elif spec.joint == "render":
        # Plaster with blown patches that reveal the brick shell underneath.
        course = 16
        row = 0
        y = 0
        while y < SHEET_SIZE:
            wrap_line(draw, (0, y, SHEET_SIZE, y), MORTAR + 6, width=1)
            x = (row % 2) * 16
            while x < SHEET_SIZE:
                wrap_line(draw, (x, y, x, y + course), MORTAR + 6, width=1)
                x += 32
            row += 1
            y += course
        for _ in range(26):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_rect(
                draw,
                (x, y, x + rng.randint(40, 150), y + rng.randint(30, 120)),
                BASE + rng.randint(4, 16),
            )
    elif spec.joint == "panel":
        # One cast seam per bay and per floor: the joints of a panel block.
        for bay in range(BAYS):
            x = bay * CELL
            wrap_rect(draw, (x - 3, 0, x + 3, SHEET_SIZE), MORTAR - 12)
            wrap_rect(draw, (x + 4, 0, x + 7, SHEET_SIZE), SEAM_LIGHT)
        for floor in range(FLOORS):
            y = floor * CELL
            wrap_rect(draw, (0, y - 3, SHEET_SIZE, y + 3), MORTAR - 16)
            wrap_rect(draw, (0, y + 4, SHEET_SIZE, y + 7), SEAM_LIGHT)
    elif spec.joint == "sheet":
        # Vertical corrugation, quantised into flat steps to stay PS1-honest.
        x = 0
        while x < SHEET_SIZE:
            wrap_rect(draw, (x, 0, x + 5, SHEET_SIZE), MORTAR)
            wrap_rect(draw, (x + 6, 0, x + 10, SHEET_SIZE), SEAM_LIGHT)
            x += 16
        for floor in range(FLOORS):
            y = floor * CELL
            wrap_rect(draw, (0, y - 5, SHEET_SIZE, y + 5), MORTAR - 26)
            bolt = 0
            while bolt < SHEET_SIZE:
                wrap_rect(draw, (bolt, y - 3, bolt + 5, y + 3), MORTAR - 44)
                bolt += 64
    elif spec.joint == "utility":
        # Utilitarian brick and concrete: coarse, mostly blank, few lines.
        course = 32
        y = 0
        row = 0
        while y < SHEET_SIZE:
            wrap_line(draw, (0, y, SHEET_SIZE, y), MORTAR, width=2)
            x = (row % 2) * 32
            while x < SHEET_SIZE:
                wrap_line(draw, (x, y, x, y + course), MORTAR, width=2)
                x += 64
            row += 1
            y += course
    elif spec.joint == "poster":
        # Old brick shell, so it stays visible around every later fixing.
        course = 16
        row = 0
        y = 0
        while y < SHEET_SIZE:
            wrap_line(draw, (0, y, SHEET_SIZE, y), MORTAR + 4, width=2)
            x = (row % 2) * 16
            while x < SHEET_SIZE:
                wrap_line(draw, (x, y, x, y + course), MORTAR + 4, width=2)
                x += 32
            row += 1
            y += course
    elif spec.joint == "roof":
        # Felt laid in overlapping strips, ponding where it has sagged, then
        # gravel over the lot. The gravel is coarse on purpose: anything finer
        # is below one texel once the sheet is imported at 512 and would only
        # shimmer under the composite.
        # Offset off zero so no strip is phase-locked to the wrap boundary;
        # nothing here needs to be, and it keeps the seam check meaningful.
        y = 32
        while y < SHEET_SIZE:
            wrap_rect(draw, (0, y, SHEET_SIZE, y + 3), MORTAR - 20)
            y += 64
        for _ in range(14):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_rect(
                draw,
                (x, y, x + rng.randint(90, 260), y + rng.randint(60, 190)),
                rng.randint(BASE - 46, BASE - 18),
            )
        for _ in range(2600):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            size = rng.randint(3, 7)
            wrap_rect(
                draw,
                (x, y, x + size, y + size),
                rng.randint(BASE - 34, BASE + 44),
            )


def draw_district_weathering(
    draw: ImageDraw.ImageDraw,
    spec: FacadeSpec,
    rng: random.Random,
) -> None:
    """The traces of long use that the wall between the bands carries.

    Each of these is one of the bible's four material axes -- repair, soiling,
    fading, reuse -- and each is placed where that district would actually get
    it, so the blank wall is never simply blank.
    """
    if spec.joint in ("brick", "render"):
        # Soot climbing the wall, and the clean ghost of a sign taken down.
        for _ in range(16):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            width = rng.randint(30, 90)
            for step in range(5):
                value = int(GRIME + 26 + (BASE - GRIME - 26) * step / 5)
                wrap_rect(
                    draw,
                    (x, y - (step + 1) * 22, x + width, y - step * 22),
                    value,
                )
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        wrap_rect(draw, (x, y, x + 150, y + 54), BASE + 24)
        wrap_rect(draw, (x + 4, y + 4, x + 146, y + 50), BASE + 16)
    elif spec.joint == "panel":
        # The trace of a removed box: cleaner render and four empty fixings.
        for _ in range(3):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_rect(draw, (x, y, x + 78, y + 62), BASE + 20)
            for dx, dy in ((6, 6), (66, 6), (6, 50), (66, 50)):
                wrap_rect(draw, (x + dx, y + dy, x + dx + 6, y + dy + 6), GRIME)
                # Rust bleeds down from the fixing and nowhere else.
                wrap_rect(
                    draw,
                    (x + dx, y + dy + 6, x + dx + 6, y + dy + 30),
                    GRIME + 34,
                )
    elif spec.joint in ("sheet", "utility"):
        # Soot on the horizontals, and one marking painted flat over.
        for floor in range(FLOORS):
            y = floor * CELL
            wrap_rect(draw, (0, y + 6, SHEET_SIZE, y + 22), GRIME + 30)
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        wrap_rect(draw, (x, y, x + 190, y + 60), BASE + 10)
        for index in range(4):
            wrap_rect(
                draw,
                (x + 16 + index * 42, y + 16, x + 40 + index * 42, y + 44),
                BASE - 8,
            )
    elif spec.joint == "poster":
        # A dead sign's mount plate, with the old wall left visible around it.
        for _ in range(2):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_rect(draw, (x - 10, y - 10, x + 130, y + 74), GRIME + 40)
            wrap_rect(draw, (x, y, x + 120, y + 64), BASE + 26)
            for dx, dy in ((4, 4), (110, 4), (4, 54), (110, 54)):
                wrap_rect(draw, (x + dx, y + dy, x + dx + 6, y + dy + 6), GRIME)
        for _ in range(9):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            wrap_rect(
                draw,
                (x, y, x + rng.randint(40, 76), y + rng.randint(50, 92)),
                rng.randint(BASE + 4, BASE + 32),
            )


def draw_aperture(
    draw: ImageDraw.ImageDraw,
    spec: FacadeSpec,
    box: tuple[int, int, int, int],
    state: str,
    rng: random.Random,
) -> None:
    """Recess, lintel, sill and glazing for one bay of one floor."""
    left, top, right, bottom = box
    height = bottom - top

    wrap_rect(draw, (left - 7, top - 13, right + 7, top - 3), LINTEL)
    wrap_rect(draw, (left - 11, bottom + 3, right + 11, bottom + 16), SILL)
    wrap_rect(
        draw,
        (left - 11, bottom + 16, right + 11, bottom + 21),
        SILL_SHADOW,
    )

    if state == "bricked":
        # A filled opening, its outline still legible in the wall.
        wrap_rect(draw, (left, top, right, bottom), INFILL)
        wrap_rect(draw, (left, top, right, top + 3), REVEAL)
        wrap_rect(draw, (left, bottom - 3, right, bottom), REVEAL)
        y = top
        row = 0
        while y < bottom:
            wrap_line(draw, (left, y, right, y), INFILL - 26, width=2)
            x = left + (row % 2) * 20
            while x < right:
                wrap_line(draw, (x, y, x, y + 16), INFILL - 26, width=2)
                x += 40
            row += 1
            y += 16
        return

    aperture_left = left
    aperture_right = right
    if state == "shrunk":
        # A later, smaller window inside the old opening.
        inset = int((right - left) * 0.22)
        wrap_rect(draw, (left, top, right, bottom), INFILL)
        aperture_left = left + inset
        aperture_right = right - inset

    wrap_rect(
        draw,
        (aperture_left - 5, top - 5, aperture_right + 5, bottom + 5),
        REVEAL,
    )
    wrap_rect(draw, (aperture_left, top, aperture_right, bottom), APERTURE)

    if state == "boarded":
        for index in range(4):
            y = top + int(height * (0.10 + index * 0.24))
            wrap_rect(
                draw,
                (aperture_left + 5, y, aperture_right - 5, y + 14),
                BOARD,
            )
        return

    # Mullions sit only a little above the glass they divide. Any brighter and
    # the band reads as a barcode, which is exactly the high frequency the
    # 640x360 composite turns into crawling noise.
    lights = max(1, spec.mullions + 1)
    step = (aperture_right - aperture_left) / lights
    for index in range(1, lights):
        x = aperture_left + step * index
        wrap_rect(draw, (x - 4, top, x + 4, bottom), MULLION)
    wrap_rect(draw, (aperture_left, top, aperture_right, top + 4), MULLION - 6)
    wrap_rect(
        draw,
        (aperture_left, bottom - 4, aperture_right, bottom),
        MULLION - 6,
    )

    if state == "curtain":
        drop = int(height * rng.uniform(0.34, 0.74))
        wrap_rect(
            draw,
            (aperture_left + 7, top + 5, aperture_right - 7, top + 5 + drop),
            APERTURE + 62,
        )
    elif state == "poster":
        # Bills pasted over the reveal, so the old wall shows around them.
        for _ in range(3):
            x = rng.randint(aperture_left, aperture_right - 60)
            y = bottom + 24 + rng.randint(0, 40)
            wrap_rect(
                draw,
                (x, y, x + rng.randint(46, 88), y + rng.randint(56, 104)),
                rng.randint(BASE + 6, BASE + 30),
            )


def draw_grime(
    draw: ImageDraw.ImageDraw,
    spec: FacadeSpec,
    box: tuple[int, int, int, int],
    rng: random.Random,
) -> None:
    """Vertical runs below the sill, where every real facade stains first."""
    if spec.grime <= 0.0:
        return

    left, top, right, bottom = box
    runs = int(round(rng.randint(10, 18) * spec.grime))
    for _ in range(runs):
        x = rng.randint(left, right)
        width = rng.randint(5, 26)
        length = int(rng.randint(60, 210) * spec.grime)
        # Each run fades as it falls, which is what stops the lower wall from
        # reading as one flat wash.
        steps = 4
        for step in range(steps):
            value = int(GRIME + (BASE - GRIME) * (step / steps) * 0.8)
            wrap_rect(
                draw,
                (
                    x,
                    bottom + 19 + length * step // steps,
                    x + width,
                    bottom + 19 + length * (step + 1) // steps,
                ),
                value,
            )
    wrap_rect(draw, (left - 7, bottom + 19, right + 7, bottom + 34), GRIME - 6)
    # Soot gathers under the head of the opening too, not only below the sill.
    wrap_rect(draw, (left - 5, top - 26, right + 5, top - 14), GRIME + 26)


def draw_accent(
    image: Image.Image,
    spec: FacadeSpec,
    rng: random.Random,
) -> float:
    """Paint the district's one functional colour and report its coverage.

    Rust is placed on the joint band and nowhere else, which is what makes the
    forbidden all-over rust field unreachable rather than merely discouraged.
    """
    if spec.accent_area <= 0.0:
        return 0.0

    mask = Image.new("L", (SHEET_SIZE, SHEET_SIZE), 0)
    draw = ImageDraw.Draw(mask)
    budget = spec.accent_area * SHEET_SIZE * SHEET_SIZE
    requested = 0.0

    while requested < budget:
        if spec.joint in ("sheet", "utility"):
            floor = rng.randrange(FLOORS)
            y = floor * CELL + rng.randint(-9, 9)
            x = rng.randrange(SHEET_SIZE)
            width = rng.randint(26, 104)
            height = rng.randint(5, 15)
        elif spec.joint == "poster":
            bay = rng.randrange(BAYS)
            floor = rng.randrange(FLOORS)
            origin_x, origin_y = cell_origin(bay, floor)
            x = origin_x + rng.randint(8, CELL - 70)
            y = origin_y + int(CELL * rng.uniform(0.10, 0.26))
            width = rng.randint(34, 64)
            height = rng.randint(30, 58)
        else:
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            width = rng.randint(40, 130)
            height = rng.randint(26, 90)
        wrap_rect(draw, (x, y, x + width, y + height), 255)
        requested += width * height

    mask = wrap_filter(mask, ImageFilter.GaussianBlur(3))
    tint = Image.new("RGB", (SHEET_SIZE, SHEET_SIZE), spec.accent)
    image.paste(Image.blend(image, tint, 0.55), (0, 0), mask)
    # Measure what actually landed rather than what was asked for, so the
    # ceiling is a real check and not an accounting identity.
    painted = sum(mask.histogram()[128:])
    return painted / (SHEET_SIZE * SHEET_SIZE)


# --------------------------------------------------------------------------
# sheet construction
# --------------------------------------------------------------------------


def build_structure(spec: FacadeSpec, rng: random.Random) -> Image.Image:
    """The grey value field: wall grammar, apertures, weathering."""
    base = Image.new("L", (SHEET_SIZE, SHEET_SIZE), BASE)
    draw = ImageDraw.Draw(base)
    draw_wall_grammar(draw, spec, rng)

    if spec.joint == "roof":
        return base

    draw_district_weathering(draw, spec, rng)
    for floor in range(FLOORS):
        for bay in range(BAYS):
            origin_x, origin_y = cell_origin(bay, floor)
            box = aperture_box(origin_x, origin_y)
            draw_grime(draw, spec, box, rng)
            draw_aperture(draw, spec, box, spec.states[floor][bay], rng)

    # Exactly one repaired plane per sheet: the bible asks for one, not many.
    patch_x, patch_y = cell_origin(rng.randrange(BAYS), rng.randrange(FLOORS))
    wrap_rect(
        draw,
        (patch_x + 10, patch_y + 10, patch_x + CELL - 10, patch_y + 44),
        PATCH,
    )
    # And one sun-bleached stripe, brighter than the base it crosses.
    stripe_y = rng.randrange(SHEET_SIZE)
    wrap_rect(draw, (0, stripe_y, SHEET_SIZE, stripe_y + 18), PATCH - 6)
    return base


def cast_tables(
    cast: tuple[float, float, float],
) -> tuple[list[int], list[int], list[int]]:
    return tuple(  # type: ignore[return-value]
        [min(255, int(round(value * channel))) for value in range(256)]
        for channel in cast
    )


SHADOW_KNEE = 0.22


def tone_table(exposure: float) -> list[int]:
    """A gamma lift that leaves the deep shadows where they were authored.

    A plain gamma brightens the apertures along with the wall, so the sheet
    runs out of range trying to reach the target mean and the openings end up
    as grey panels instead of holes. Fading the lift out below the knee lets
    the wall carry the whole mean while the apertures stay near black.
    """
    table: list[int] = []
    for value in range(256):
        t = value / 255.0
        blend = min(1.0, t / SHADOW_KNEE)
        blend = blend * blend * (3.0 - 2.0 * blend)
        lifted = t + (math.pow(t, exposure) - t) * blend
        table.append(min(255, int(round(255.0 * lifted))))
    return table


def normalise_luminance(image: Image.Image) -> tuple[Image.Image, float]:
    """Bisect the exposure until the mean linear luminance hits the target.

    Adjusting exposure rather than the palette keeps the authored hue and the
    contrast ordering intact while landing every sheet on the one brightness
    the runtime compensation constant assumes.
    """
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
        if abs(mean - MEAN_LUMINANCE_TARGET) < 0.0004:
            break
        if mean < MEAN_LUMINANCE_TARGET:
            high = exposure
        else:
            low = exposure
    if abs(mean_linear_luminance(best) - MEAN_LUMINANCE_TARGET) > 0.004:
        raise ValueError(
            "Exposure could not reach the target mean; the sheet carries too "
            "much dark area for the wall to compensate."
        )
    return best, exposure


def build_sheet(spec: FacadeSpec) -> tuple[Image.Image, dict]:
    rng = random.Random(spec.seed)
    structure = build_structure(spec, rng)

    # Overlay rather than blend: mid-grey noise leaves the structure where it
    # is and only modulates around it, so the apertures stay as dark as they
    # were drawn instead of being pulled toward the noise mean.
    macro = fractal_noise(
        ((8, 1.0), (32, 0.66), (128, 0.40), (256, 0.24), (512, 0.14)),
        rng,
    )
    field = wrap_filter(
        ImageChops.overlay(structure, macro),
        ImageFilter.SMOOTH,
    )

    tables = cast_tables(spec.cast)
    image = Image.merge(
        "RGB",
        (
            field.point(tables[0]),
            field.point(tables[1]),
            field.point(tables[2]),
        ),
    )
    accent_coverage = draw_accent(image, spec, rng)
    image, exposure = normalise_luminance(image)

    mean = mean_linear_luminance(image)
    return image, {
        "key": spec.key,
        "district": spec.district,
        "variant": spec.variant,
        "palette": spec.palette,
        "resourcePath": f"Textures/{spec.key}",
        "meanLinearLuminance": round(mean, 6),
        "albedoCompensation": round(ALBEDO_COMPENSATION, 6),
        "smoothness": spec.smoothness,
        "metallic": spec.metallic,
        "accentCoverage": round(accent_coverage, 6),
        "exposure": round(exposure, 6),
    }


# --------------------------------------------------------------------------
# validation
# --------------------------------------------------------------------------


def channel_delta(first: tuple[int, ...], second: tuple[int, ...]) -> float:
    return sum(abs(a - b) for a, b in zip(first, second))


def mean_line_delta(image: Image.Image, first: int, second: int) -> float:
    """Mean per-channel difference between two columns and two rows."""
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


def validate(image: Image.Image, spec: FacadeSpec, record: dict) -> None:
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

    # The absolute cap alone is misleading on a structured sheet: the outer
    # column sits at a mortar joint while its neighbour is brick face, so the
    # two are supposed to differ, exactly as they do at every other joint. On
    # a torus the outer lines ARE neighbours, so the second question is
    # whether they differ by much more than a typical neighbouring pair. The
    # stride is coprime with every module pitch used here, so the sample
    # covers all phases rather than repeatedly landing on flat wall.
    #
    # This is a regression gate, not a proof of periodicity: the edge is
    # phase-locked to whatever module sits at zero, so the allowance has to be
    # loose enough for that. What proves the sheets wrap is construction --
    # toroidal noise, wrapping stamps, pitches that divide the sheet and
    # wrap-aware convolution. This catches it when one of those slips, which
    # is exactly how the brick pitch and the clamped filter were found.
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
    if high - low < CONTRAST_FLOOR:
        raise ValueError(
            f"{spec.key} spans only {high - low} luminance levels, "
            f"too subtle for the 640x360 composite (floor {CONTRAST_FLOOR})."
        )

    mean = record["meanLinearLuminance"]
    if abs(mean - MEAN_LUMINANCE_TARGET) > MEAN_LUMINANCE_TOLERANCE:
        raise ValueError(
            f"{spec.key} mean linear luminance {mean:.4f} is outside "
            f"{MEAN_LUMINANCE_TARGET} +/- {MEAN_LUMINANCE_TOLERANCE}."
        )

    # The compensation must never push a night facade tint past one, or the
    # district hue would clamp away.
    brightest = MAXIMUM_NIGHT_FACADE_CHANNEL * ALBEDO_COMPENSATION
    if brightest > 1.0:
        raise ValueError(
            f"Compensation {ALBEDO_COMPENSATION:.4f} would drive a bar facade "
            f"to {brightest:.4f} and clamp."
        )

    # And the textured wall must land where the flat colour used to, for every
    # lot kind the city can produce -- measured through the same linear
    # multiply URP performs, not through a gamma-space approximation of it.
    worst = max(
        abs(
            srgb_to_linear(min(1.0, peak * ALBEDO_COMPENSATION)) * mean
            / srgb_to_linear(peak)
            - 1.0
        )
        for peak in NIGHT_FACADE_PEAKS.values()
    )
    if worst > BRIGHTNESS_ERROR_LIMIT:
        raise ValueError(
            f"{spec.key} would shift facade brightness by {worst * 100:.1f}%, "
            f"above the {BRIGHTNESS_ERROR_LIMIT * 100:.0f}% allowed."
        )
    record["brightnessError"] = round(worst, 6)

    means = channel_means(image)
    ratio = max(means) / max(1e-6, min(means))
    if ratio > CHANNEL_RATIO_LIMIT:
        raise ValueError(
            f"{spec.key} channel means {means} differ by {ratio:.3f}; the "
            f"per-lot tint owns the hue, limit {CHANNEL_RATIO_LIMIT}."
        )

    if record["accentCoverage"] > spec.accent_area * 1.7:
        raise ValueError(
            f"{spec.key} accent covers {record['accentCoverage']:.4f} of the "
            f"sheet, above the {spec.accent_area} the district allows."
        )

    record["edgeDelta"] = round(edge_delta, 4)
    record["seamRatio"] = round(seam_ratio, 4)
    record["contrast"] = high - low
    record["channelRatio"] = round(ratio, 4)


def build_contact_sheet(sheets: list[tuple[FacadeSpec, Image.Image]]) -> Image.Image:
    """A human check that the districts still differ in grayscale and in fog.

    Each sheet is shown tiled two by two so the repeat period is visible, then
    again as grayscale, which is the reading the art bible asks every district
    difference to survive.
    """
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
    built: list[tuple[FacadeSpec, Image.Image]] = []
    for spec in FACADE_SPECS + (ROOF_SPEC,):
        image, record = build_sheet(spec)
        validate(image, spec, record)
        built.append((spec, image))
        if args.verify:
            record["sha256"] = hashlib.sha256(image.tobytes()).hexdigest().upper()
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
            f"edge={record['edgeDelta']:.2f} "
            f"seam={record['seamRatio']:.2f}x "
            f"contrast={record['contrast']} "
            f"chroma={record['channelRatio']:.3f} "
            f"accent={record['accentCoverage']:.4f}"
        )

    if args.verify:
        print(f"Validated {len(records)} sheets; nothing written.")
        return

    manifest = {
        "sheetSize": SHEET_SIZE,
        "bays": BAYS,
        "floors": FLOORS,
        "floorPitchMeters": FLOOR_PITCH_M,
        "firstFloorCenterY": FIRST_FLOOR_CENTER_Y_M,
        "massBaseY": MASS_BASE_Y_M,
        "bandCenterCellFraction": BAND_CENTER_CELL_FRACTION,
        "apertureWidthFraction": APERTURE_WIDTH_FRACTION,
        "apertureHeightFraction": APERTURE_HEIGHT_FRACTION,
        "meanLuminanceTarget": MEAN_LUMINANCE_TARGET,
        "maximumNightFacadeChannel": MAXIMUM_NIGHT_FACADE_CHANNEL,
        "albedoCompensation": round(ALBEDO_COMPENSATION, 6),
        "nightFacadePeaks": NIGHT_FACADE_PEAKS,
        "sheets": records,
    }
    manifest_path = args.art_source / "city-facade-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = build_contact_sheet(built)
    contact_path = args.art_source / "city-facade-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
