#!/usr/bin/env python3
"""Build the deterministic surface sheets for the north seacoast.

Two families, the same split the river and the lake make.

The **albedo** family - four seamless 1024x1024 sheets imported at 512
and applied by `CitySeacoastSurfaceAppearance` over the world-planar UVs
baked into the combined seacoast meshes. Each batch's authored flat
colour, brightened by the solved per-sheet compensation, multiplies the
sheet in linear space. The measured contract is imported from
`build-home-textures.py`:

* sand     - the whole shore underfoot: wet-dry tide banding, shell
  grit, pebble drift lines and the ripple marks the last tide left.
  The one band structure runs along U, because the tide comes in along
  world Z and the sheet is applied world-planar.
* concrete - the mol, the mouth sill, the slipway and the port's
  terraces: cast courses, shutter joints, spall, rust runs and the
  dark tide stain that never dries.
* plank    - the boat station's decking and boards. The grammar moved
  here from `build-city-lake-textures.py` with the station itself:
  the same boards, nailed by the same hands, now over salt water.
* hull     - the hire hulls' peeling municipal paint, moved with the
  boats for the same reason.

The esplanade's granite deliberately has no sheet here: it reuses the
river's quay stone (`Textures/CityRiverQuayAlbedo`), because the
esplanade IS the embankment vocabulary carried to the sea.

The **water** family - one sheet consumed by `CityRiverWater.shader`
through `CitySeaResources`:

* sea water normal - the isotropic ripple recipe the lake proved out
  (ring crests, near-round chop, `slopeAnisotropy` bounded), at a
  coarser metre pitch: sea ripple reads at a larger scale or it reads
  as shimmer. Standing water is directionless whether it is a pond or
  a sea; the swell lives in the vertex stage, not in this sheet.

The sea borrows the river's white foam mask rather than authoring its
own: air in water is white on a shore the same way it is white under a
quay.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "City"


def _load(module_name: str, file_name: str):
    spec = importlib.util.spec_from_file_location(
        module_name,
        Path(__file__).resolve().parent / file_name,
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


home = _load("build_home_textures", "build-home-textures.py")
river = _load("build_city_river_textures", "build-city-river-textures.py")

SHEET_SIZE = home.SHEET_SIZE
WaterSheetSpec = river.WaterSheetSpec


# --------------------------------------------------------------------------
# seacoast albedo grammars
# --------------------------------------------------------------------------


def draw_seacoast_sand(base: Image.Image, rng) -> Image.Image:
    """Tide-banded sand: damp bands, grit, drift lines, ripple marks."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((64, 0.7), (128, 0.6), (256, 0.5), (512, 0.35)), rng),
    )
    draw = ImageDraw.Draw(image)

    # The tide bands: broad damp stripes across the sheet, each with a
    # ragged edge of overlapping ellipses. The tide comes and goes
    # along one axis, and the bands are the only structure the sand
    # keeps.
    for _ in range(5):
        band_y = rng.randrange(SHEET_SIZE)
        band_height = rng.randint(60, 150)
        tone = home.BASE - rng.randint(10, 20)
        for _ in range(70):
            x = rng.randrange(SHEET_SIZE)
            y = band_y + rng.randint(-band_height // 3, band_height // 3)
            home.wrap_ellipse(
                draw,
                (x, y, x + rng.randint(50, 160), y + rng.randint(16, 44)),
                fill=tone + rng.randint(-4, 4),
            )

    # Ripple marks: the short parallel crests a receding tide combs
    # into wet sand. Faint, close, and only in patches.
    for _ in range(26):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        for ripple in range(rng.randint(4, 8)):
            y = cy + ripple * rng.randint(7, 10)
            home.wrap_line(
                draw,
                (cx, y, cx + rng.randint(60, 150), y + rng.randint(-3, 3)),
                home.BASE + rng.choice((-12, -8, 9, 13)),
                width=1,
            )

    # Shell grit and pebbles, each with its pressed shadow.
    for _ in range(900):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(2, 6)
        home.wrap_ellipse(
            draw,
            (x, y + 1, x + size, y + size + 1),
            fill=home.BASE - 18,
        )
        home.wrap_ellipse(
            draw,
            (x, y, x + size, y + size),
            fill=home.BASE + rng.choice((-14, -6, 10, 16, 22)),
        )

    # Drift lines: the thin dark seams of weed and coal dust the water
    # leaves at its highest reach.
    for _ in range(14):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(120, 380)
        segments = rng.randint(4, 8)
        step = length // segments
        for segment in range(segments):
            home.wrap_line(
                draw,
                (
                    x + segment * step,
                    y + rng.randint(-6, 6),
                    x + (segment + 1) * step,
                    y + rng.randint(-6, 6),
                ),
                home.BASE - rng.randint(16, 28),
                width=rng.randint(1, 3),
            )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        # Standing damp where the sand never dries.
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(140, 340), y + rng.randint(80, 200)),
                fill=rng.randint(108, 120),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(70, 180), y + rng.randint(50, 130)),
                fill=rng.randint(136, 148),
            )

    return home.soft_overlay(image, 12, weathering)


def draw_seacoast_concrete(base: Image.Image, rng) -> Image.Image:
    """Marine concrete: cast courses, spall, rust runs, tide stain."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((128, 0.6), (256, 0.55), (512, 0.4)), rng),
    )
    draw = ImageDraw.Draw(image)

    # The cast courses: horizontal pour joints every fifth of the
    # sheet, each with the lip the next pour left.
    course = SHEET_SIZE // 5
    for index in range(5):
        y = index * course
        home.wrap_rect(
            draw,
            (0, y, SHEET_SIZE, y + 3),
            home.BASE - rng.randint(26, 38),
        )
        home.wrap_rect(
            draw,
            (0, y + 3, SHEET_SIZE, y + 5),
            home.BASE + rng.randint(10, 18),
        )
        # Shutter joints: the vertical board marks inside each course.
        for _ in range(rng.randint(4, 7)):
            x = rng.randrange(SHEET_SIZE)
            home.wrap_line(
                draw,
                (x, y + 5, x + rng.randint(-2, 2), y + course - 2),
                home.BASE - rng.randint(8, 16),
                width=1,
            )

    # Spall: patches where the face burst off the reinforcement,
    # darker inside with a bright broken rim.
    for _ in range(26):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(20, 70)
        height = rng.randint(14, 44)
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + height),
            fill=home.BASE - rng.randint(22, 36),
        )
        home.wrap_ellipse(
            draw,
            (x - 2, y - 2, x + width + 2, y + height + 2),
            outline=home.BASE + rng.randint(12, 22),
            width=2,
        )

    # Rust running out of every tie hole and cracked bar.
    for _ in range(46):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + 5, y + 5),
            fill=home.BASE - rng.randint(30, 42),
        )
        home.wrap_line(
            draw,
            (x + 2, y + 4, x + 2 + rng.randint(-4, 4), y + rng.randint(40, 160)),
            home.BASE - rng.randint(12, 22),
            width=rng.randint(2, 4),
        )

    # Aggregate speckle: the fine grain sea concrete weathers down to.
    for _ in range(1200):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(1, 4)
        home.wrap_ellipse(
            draw,
            (x, y, x + size, y + size),
            fill=home.BASE + rng.choice((-16, -9, 8, 14)),
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        # The tide stain: the broad dark wash the water paints and
        # repaints. Blobbed rather than banded, so the sheet wraps.
        for _ in range(13):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(120, 320), y + rng.randint(70, 190)),
                fill=rng.randint(106, 120),
            )
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 160), y + rng.randint(40, 120)),
                fill=rng.randint(134, 146),
            )

    return home.soft_overlay(image, 12, weathering)


def draw_seacoast_plank(base: Image.Image, rng) -> Image.Image:
    """Weathered decking: eight boards, split ends, nail rust.

    Moved verbatim from the lake generator with the boat station the
    boards belong to.
    """
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((128, 0.6), (256, 0.55), (512, 0.4)), rng),
    )
    draw = ImageDraw.Draw(image)

    board = SHEET_SIZE // 8
    for index in range(8):
        x0 = index * board
        x1 = x0 + board

        home.wrap_rect(
            draw,
            (x0 + 2, 0, x1 - 2, SHEET_SIZE),
            home.BASE + rng.choice((-16, -9, -3, 5, 11, 17)),
        )
        home.wrap_rect(
            draw,
            (x1 - 2, 0, x1 + 1, SHEET_SIZE),
            home.BASE - rng.randint(34, 46),
        )
        for _ in range(rng.randint(16, 26)):
            gx = x0 + rng.randint(4, board - 6)
            gy = rng.randrange(SHEET_SIZE)
            home.wrap_line(
                draw,
                (gx, gy, gx + rng.randint(-2, 2), gy + rng.randint(90, 400)),
                home.BASE + rng.choice((-14, -9, 8, 13)),
                width=1,
            )
        for _ in range(rng.randint(1, 3)):
            sx = x0 + rng.randint(6, board - 8)
            sy = rng.randrange(SHEET_SIZE)
            home.wrap_line(
                draw,
                (sx, sy, sx + rng.randint(-3, 3), sy + rng.randint(30, 90)),
                home.BASE - rng.randint(26, 38),
                width=rng.randint(1, 2),
            )
        for _ in range(4):
            nx = x0 + rng.choice((board // 4, board - board // 4))
            ny = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                draw,
                (nx, ny, nx + 4, ny + 4),
                fill=home.BASE - rng.randint(30, 44),
            )
            home.wrap_ellipse(
                draw,
                (nx - 2, ny + 3, nx + 6, ny + rng.randint(16, 34)),
                fill=home.BASE - rng.randint(10, 20),
            )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(11):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(70, 210), y + rng.randint(90, 300)),
                fill=rng.randint(110, 122),
            )
        for _ in range(6):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(50, 140), y + rng.randint(60, 180)),
                fill=rng.randint(134, 146),
            )

    return home.soft_overlay(image, 10, weathering)


def draw_seacoast_hull(base: Image.Image, rng) -> Image.Image:
    """Peeling municipal paint over caulked clinker seams and tar.

    Moved verbatim from the lake generator with the hulls it paints.
    """
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((256, 0.6), (512, 0.8)), rng),
    )
    draw = ImageDraw.Draw(image)

    strake = SHEET_SIZE // 6
    for index in range(6):
        y = index * strake
        home.wrap_rect(
            draw,
            (0, y, SHEET_SIZE, y + 3),
            home.BASE - rng.randint(30, 42),
        )
        home.wrap_rect(
            draw,
            (0, y + 3, SHEET_SIZE, y + 6),
            home.BASE + rng.randint(14, 24),
        )
        for _ in range(rng.randint(6, 12)):
            cx = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                draw,
                (cx, y - 2, cx + rng.randint(20, 70), y + 5),
                fill=home.BASE - rng.randint(14, 24),
            )

    for _ in range(220):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(10, 46)
        height = rng.randint(6, 26)
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + height),
            fill=home.BASE - rng.randint(24, 40),
        )
        for _ in range(rng.randint(2, 5)):
            fx = x + rng.randint(-6, width)
            fy = y + rng.randint(-4, height)
            home.wrap_ellipse(
                draw,
                (fx, fy, fx + rng.randint(4, 14), fy + rng.randint(3, 10)),
                fill=home.BASE - rng.randint(18, 34),
            )
        home.wrap_line(
            draw,
            (x, y, x + width, y + rng.randint(-3, 3)),
            home.BASE + rng.randint(16, 28),
            width=1,
        )

    for _ in range(360):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(30, 130), y + rng.randint(-2, 2)),
            home.BASE + rng.choice((-10, -6, 7, 11)),
            width=1,
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(10):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(90, 260), y + rng.randint(50, 150)),
                fill=rng.randint(112, 124),
            )
        for _ in range(5):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 160), y + rng.randint(40, 110)),
                fill=rng.randint(134, 146),
            )

    return home.soft_overlay(image, 9, weathering)


home.GRAMMARS["seacoast_sand"] = draw_seacoast_sand
home.GRAMMARS["seacoast_concrete"] = draw_seacoast_concrete
home.GRAMMARS["seacoast_plank"] = draw_seacoast_plank
home.GRAMMARS["seacoast_hull"] = draw_seacoast_hull


# --------------------------------------------------------------------------
# the sea's water normal
# --------------------------------------------------------------------------


def square_fractal(
    octaves: tuple[tuple[int, float], ...],
    rng: random.Random,
) -> Image.Image:
    """`home.periodic_noise` blended over square lattices, so features
    come out round. Standing water has no axis, sea or pond."""
    accumulated: Image.Image | None = None
    weight_sum = 0.0
    for lattice, weight in octaves:
        layer = home.periodic_noise(lattice, rng)
        weight_sum += weight
        accumulated = (
            layer
            if accumulated is None
            else Image.blend(accumulated, layer, weight / weight_sum)
        )
    if accumulated is None:
        raise ValueError("Square fractal needs at least one octave.")
    return accumulated


def draw_sea_ripple_height(rng: random.Random) -> Image.Image:
    """The height field the sea's normal map differentiates.

    The lake's recipe - ring crests and near-round chop - because a
    standing surface is disturbed from points whether it is a pond or
    a sea. The counts differ: fewer rings (the sea's rain rings are
    lost in its chop) and more chop, slightly larger, because this
    sheet tiles at 4.5 m instead of 3.
    """
    height = square_fractal(
        ((32, 1.0), (64, 0.75), (128, 0.45), (256, 0.22)),
        rng,
    )
    draw = ImageDraw.Draw(height)

    for _ in range(50):
        cx = float(rng.randrange(SHEET_SIZE))
        cy = float(rng.randrange(SHEET_SIZE))
        radius = float(rng.randint(16, 80))
        rings = rng.randint(2, 3)
        for ring in range(rings):
            r = radius * (1.0 + ring * rng.uniform(0.26, 0.44))
            tone = 128 + rng.choice((-32, -22, 20, 30)) // (ring + 1)
            home.wrap_ellipse(
                draw,
                (cx - r, cy - r, cx + r, cy + r),
                outline=tone,
                width=rng.randint(2, 5),
            )

    for _ in range(1200):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(7, 20)
        home.wrap_ellipse(
            draw,
            (x, y, x + size, y + rng.randint(size - 2, size + 2)),
            fill=128 + rng.choice((-30, -20, 22, 32)),
        )

    return home.wrap_filter(height, ImageFilter.GaussianBlur(1.7), pad=14)


def build_sea_water_normal(spec) -> tuple[Image.Image, dict]:
    """Differentiate the ripple height into (-dH/du, -dH/dv, 1)."""
    rng = random.Random(spec.seed)
    height = draw_sea_ripple_height(rng)

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

    horizontal = river.channel_span(red, 0.02, 0.98)
    vertical = river.channel_span(green, 0.02, 0.98)
    return image, {
        "key": spec.key,
        "grammar": spec.grammar,
        "resourcePath": f"Textures/{spec.key}",
        "metersPerTile": spec.meters_per_tile,
        "linear": spec.linear,
        "slopeSpan": max(horizontal, vertical),
        "slopeAnisotropy": round(horizontal / max(1, vertical), 4),
    }


WATER_BUILDERS = {
    "sea_water_normal": build_sea_water_normal,
}

# The lake's bound, kept for the same reason: a directional ripple
# sheet reads as a current, and the sea's swell is the vertex stage's
# business, not this sheet's.
MAXIMUM_RIPPLE_ANISOTROPY = 1.25


def validate_water(image: Image.Image, spec, record: dict) -> None:
    river.validate_water_wrap(image, spec, record)
    span = record["slopeSpan"]
    if not 40 <= span <= 190:
        raise ValueError(
            f"{spec.key} slope span {span} is outside 40..190; the "
            f"ripple is either too flat to light or clipping."
        )
    anisotropy = record["slopeAnisotropy"]
    low = 1.0 / MAXIMUM_RIPPLE_ANISOTROPY
    if not low <= anisotropy <= MAXIMUM_RIPPLE_ANISOTROPY:
        raise ValueError(
            f"{spec.key} tilts {anisotropy:.3f}x more along one axis "
            f"than the other, outside {low:.3f}..{MAXIMUM_RIPPLE_ANISOTROPY}."
        )


# --------------------------------------------------------------------------
# specs — tints transcribe CitySeacoastWorldBuilder's palette and
# CityExteriorAppearance.BeachSand
# --------------------------------------------------------------------------


SEACOAST_SHEET_SPECS: tuple = (
    home.HomeSheetSpec(
        key="CitySeacoastSandAlbedo",
        grammar="seacoast_sand",
        seed=0x53435341,
        cast=(1.04, 1.00, 0.93),
        mean_target=0.50,
        meters_per_tile=2.6,
        smoothness=0.03,
        metallic=0.0,
        tints=(
            ("CityExteriorAppearance.BeachSand", (0.520, 0.450, 0.300)),
        ),
    ),
    home.HomeSheetSpec(
        key="CitySeacoastConcreteAlbedo",
        grammar="seacoast_concrete",
        seed=0x53434343,
        cast=(0.99, 1.00, 1.00),
        mean_target=0.50,
        meters_per_tile=2.2,
        smoothness=0.05,
        metallic=0.0,
        tints=(
            ("CitySeacoastWorldBuilder.Concrete", (0.290, 0.290, 0.270)),
        ),
    ),
    home.HomeSheetSpec(
        key="CitySeacoastPlankAlbedo",
        grammar="seacoast_plank",
        seed=0x5343504C,
        cast=(1.01, 1.00, 0.98),
        mean_target=0.50,
        meters_per_tile=1.2,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("CitySeacoastWorldBuilder.Planking", (0.310, 0.280, 0.240)),
            ("CitySeacoastWorldBuilder.TarredTimber", (0.160, 0.140, 0.120)),
        ),
    ),
    home.HomeSheetSpec(
        key="CitySeacoastHullAlbedo",
        grammar="seacoast_hull",
        seed=0x5343484C,
        cast=(0.99, 1.00, 1.01),
        mean_target=0.50,
        meters_per_tile=1.6,
        smoothness=0.12,
        metallic=0.0,
        tints=(
            ("CitySeacoastWorldBuilder.HullPaint", (0.260, 0.320, 0.310)),
            ("CitySeacoastWorldBuilder.HullTar", (0.130, 0.120, 0.110)),
        ),
    ),
)


WATER_SHEET_SPECS: tuple = (
    WaterSheetSpec(
        key="CitySeaWaterNormal",
        grammar="sea_water_normal",
        seed=0x5343574E,
        meters_per_tile=4.5,
        linear=True,
    ),
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
        help="Destination directory for the opaque 1024x1024 sheets.",
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
    built: list = []
    for spec in SEACOAST_SHEET_SPECS:
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
        print(
            f"{'Checked' if args.verify else 'Wrote'} {water_spec.key} "
            f"({image.width}x{image.height}) "
            f"{'linear' if water_spec.linear else 'sRGB'} "
            f"slope={record['slopeSpan']} "
            f"anisotropy={record['slopeAnisotropy']:.3f} "
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
        "maximumRippleAnisotropy": MAXIMUM_RIPPLE_ANISOTROPY,
        "sheets": records,
        "waterSheets": water_records,
    }
    manifest_path = args.art_source / "seacoast-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "seacoast-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
