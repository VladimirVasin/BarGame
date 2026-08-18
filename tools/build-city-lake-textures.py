#!/usr/bin/env python3
"""Build the deterministic surface sheets for the lake precinct.

Two families, the same split the river makes, for the same reason.

The **albedo** family - three seamless 1024x1024 sheets imported at 512
and applied by `CityLakeSurfaceAppearance` over the world-planar UVs
baked into the combined lake meshes. Each batch's authored flat colour,
brightened by the solved per-sheet compensation, multiplies the sheet in
linear space, exactly like the home/POI/park/cemetery/river pipelines.
The measured contract - linear luminance rule, wrap-by-construction
drawing, compensation solving and validation - is imported from
`build-home-textures.py`; this script adds one grammar per thing an
abandoned boat station is actually made of:

* plank - the pier decking, the hut's boards and the slipway kerbs:
  weathered grey-brown boards with split ends, cupped grain and rust
  running from every nail. Eight boards across its 1.2 m, so a board is
  a hand's width, which is what makes the pier read as a pier.
* bank  - the trodden clay of the shore and the bank ring: wet earth,
  pebbles pressed in, root fibre and boot scuff. It carries the shore
  ring's tint as well as the bank's, which is what lets the two read as
  one continuous ground rather than as a lawn meeting a ramp.
* hull  - the hire boats: municipal paint peeling off caulked clinker
  seams, tar showing through underneath.

The **water** family - two sheets consumed directly by
`CityRiverWater.shader` through `CityLakeResources`, not by the albedo
pipeline, because neither is a diffuse colour:

* water normal - a derivative map of the surface ripple, stored as
  (-dH/du, -dH/dv, 1) and imported **linear** (`sRGBTexture: 0`).
  This is the sheet the whole script exists for. The river's normal map
  is deliberately smeared along V, and its own docstring says an
  isotropic sheet reads as a pond - which is precisely what a pond
  wants. So this one is built on square lattices, its long downstream
  shear lines are replaced by scattered ring crests (rain rings, insect
  rings), and its capillary chop keeps a near-round aspect. That the
  result really is directionless is not left to the eye: `slopeAnisotropy`
  measures it, the validator bounds it, and an EditMode test pins it.
* scum mask - blobby duckweed and scum, not torn downstream streaks.
  Kept thinner than the river's foam, because on a still pond this is a
  line at the boards rather than a field.

Both are still validated for wrap, which is the one contract they share
with the albedos; that check is imported from the river script rather
than copied.
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
# lake albedo grammars
# --------------------------------------------------------------------------


def draw_lake_plank(base: Image.Image, rng) -> Image.Image:
    """Weathered decking: eight boards, split ends, nail rust."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((128, 0.6), (256, 0.55), (512, 0.4)), rng),
    )
    draw = ImageDraw.Draw(image)

    board = SHEET_SIZE // 8
    for index in range(8):
        x0 = index * board
        x1 = x0 + board

        # The board face, each a little different from its neighbour:
        # this decking was replaced a plank at a time over decades.
        home.wrap_rect(
            draw,
            (x0 + 2, 0, x1 - 2, SHEET_SIZE),
            home.BASE + rng.choice((-16, -9, -3, 5, 11, 17)),
        )

        # The gap between boards. Deep, because a municipal pier's
        # planks shrank apart years ago and nobody closed them.
        home.wrap_rect(
            draw,
            (x1 - 2, 0, x1 + 1, SHEET_SIZE),
            home.BASE - rng.randint(34, 46),
        )

        # Grain: long faint lines down the board, cupping toward the
        # edges where the weather got in.
        for _ in range(rng.randint(16, 26)):
            gx = x0 + rng.randint(4, board - 6)
            gy = rng.randrange(SHEET_SIZE)
            home.wrap_line(
                draw,
                (gx, gy, gx + rng.randint(-2, 2), gy + rng.randint(90, 400)),
                home.BASE + rng.choice((-14, -9, 8, 13)),
                width=1,
            )

        # Splits at the ends of the boards, running with the grain.
        for _ in range(rng.randint(1, 3)):
            sx = x0 + rng.randint(6, board - 8)
            sy = rng.randrange(SHEET_SIZE)
            home.wrap_line(
                draw,
                (sx, sy, sx + rng.randint(-3, 3), sy + rng.randint(30, 90)),
                home.BASE - rng.randint(26, 38),
                width=rng.randint(1, 2),
            )

        # Nails, two to a bearer, and the rust that has been running
        # out of each one since the station closed.
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
        # Where the boards stay wet: the shaded side, the ends, the
        # places a boat was dragged over.
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


def draw_lake_bank(base: Image.Image, rng) -> Image.Image:
    """Trodden clay: pebbles, root fibre, boot scuff, standing damp."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((32, 0.8), (128, 0.6), (256, 0.5), (512, 0.35)), rng),
    )
    draw = ImageDraw.Draw(image)

    # Broad patches of bare and grown-over earth. The bank is neither
    # path nor lawn; it is the ground between them.
    for _ in range(30):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(70, 220), y + rng.randint(50, 160)),
            fill=home.BASE + rng.choice((-15, -9, 7, 12)),
        )

    # Pebbles pressed into the clay, each with its shadow, so the bank
    # is granular underfoot and calm from the street.
    for _ in range(1700):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(3, 8)
        height = rng.randint(3, 6)
        home.wrap_ellipse(
            draw,
            (x, y + 1, x + width, y + height + 1),
            fill=home.BASE - 22,
        )
        home.wrap_ellipse(
            draw,
            (x, y, x + width, y + height),
            fill=home.BASE + rng.choice((-16, -8, 9, 15, 21)),
        )

    # Root fibre and dead stems lying flat, the mat that holds a
    # trodden bank together.
    for _ in range(260):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(-26, 26), y + rng.randint(-20, 20)),
            home.BASE + rng.choice((-18, -12, 11, 16)),
            width=1,
        )

    # Boot scuff: the short drag marks of people coming down to the
    # water and going back up.
    for _ in range(40):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(18, 52)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(-8, 8), y + length),
            home.BASE - rng.randint(12, 24),
            width=rng.randint(3, 7),
        )

    def weathering(layer_draw: ImageDraw.ImageDraw) -> None:
        # Standing damp. A shore that never quite dries is the whole
        # difference between this and a yard.
        for _ in range(12):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(120, 320), y + rng.randint(90, 220)),
                fill=rng.randint(106, 120),
            )
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(60, 170), y + rng.randint(50, 130)),
                fill=rng.randint(136, 148),
            )

    return home.soft_overlay(image, 13, weathering)


def draw_lake_hull(base: Image.Image, rng) -> Image.Image:
    """Peeling municipal paint over caulked clinker seams and tar."""
    image = ImageChops.overlay(
        base,
        home.fractal_noise(((256, 0.6), (512, 0.8)), rng),
    )
    draw = ImageDraw.Draw(image)

    # The clinker seams: overlapping strakes running the length of the
    # hull. Six across the sheet, so a 4 m boat shows about ten.
    strake = SHEET_SIZE // 6
    for index in range(6):
        y = index * strake
        home.wrap_rect(
            draw,
            (0, y, SHEET_SIZE, y + 3),
            home.BASE - rng.randint(30, 42),
        )
        # The lip of the plank above catches the light.
        home.wrap_rect(
            draw,
            (0, y + 3, SHEET_SIZE, y + 6),
            home.BASE + rng.randint(14, 24),
        )
        # Caulking squeezed out of the seam and left there.
        for _ in range(rng.randint(6, 12)):
            cx = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                draw,
                (cx, y - 2, cx + rng.randint(20, 70), y + 5),
                fill=home.BASE - rng.randint(14, 24),
            )

    # Paint peeling off in flakes, each showing the darker tar beneath.
    # Ragged edges: a flake with a clean edge reads as a decal.
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
        # The curl of paint still attached at the edge of the flake.
        home.wrap_line(
            draw,
            (x, y, x + width, y + rng.randint(-3, 3)),
            home.BASE + rng.randint(16, 28),
            width=1,
        )

    # Brush chatter in what paint is left.
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


home.GRAMMARS["lake_plank"] = draw_lake_plank
home.GRAMMARS["lake_bank"] = draw_lake_bank
home.GRAMMARS["lake_hull"] = draw_lake_hull


# --------------------------------------------------------------------------
# lake water sheets
# --------------------------------------------------------------------------


def square_fractal(
    octaves: tuple[tuple[int, float], ...],
    rng: random.Random,
) -> Image.Image:
    """`home.periodic_noise` blended over square lattices.

    The river's equivalent takes two cell counts so its features come
    out longer than they are wide. A pond wants exactly the opposite,
    so this takes one: a square lattice can only make round blobs, and
    round blobs are the point.
    """
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


def draw_lake_ripple_height(rng: random.Random) -> Image.Image:
    """The height field the lake's normal map differentiates.

    Where the river drew long downstream creases, this draws rings.
    A still surface is disturbed from points - a raindrop, an insect,
    a fish coming up - and the expanding circle each one leaves is the
    only structure a pond has. Blurred at the end, because the sheet is
    differentiated next and a hard edge in height is a mirror-bright
    crease in the normal.
    """
    height = square_fractal(
        ((32, 1.0), (64, 0.75), (128, 0.45), (256, 0.22)),
        rng,
    )
    draw = ImageDraw.Draw(height)

    # Ring crests. Two or three concentric circles per centre, fading
    # outward, and never a filled disc: what is visible on water is the
    # rim, not the middle.
    for _ in range(90):
        cx = float(rng.randrange(SHEET_SIZE))
        cy = float(rng.randrange(SHEET_SIZE))
        radius = float(rng.randint(14, 90))
        rings = rng.randint(2, 3)
        for ring in range(rings):
            r = radius * (1.0 + ring * rng.uniform(0.26, 0.44))
            tone = 128 + rng.choice((-34, -24, 22, 32)) // (ring + 1)
            home.wrap_ellipse(
                draw,
                (cx - r, cy - r, cx + r, cy + r),
                outline=tone,
                width=rng.randint(2, 5),
            )

    # Capillary chop, near-round. The river stretches these 3:1 to read
    # as a current; keeping them square is what keeps this a pond.
    for _ in range(900):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        size = rng.randint(6, 18)
        home.wrap_ellipse(
            draw,
            (x, y, x + size, y + rng.randint(size - 2, size + 2)),
            fill=128 + rng.choice((-30, -20, 22, 32)),
        )

    return home.wrap_filter(height, ImageFilter.GaussianBlur(1.7), pad=14)


def build_lake_water_normal(spec) -> tuple[Image.Image, dict]:
    """Differentiate the ripple height into (-dH/du, -dH/dv, 1).

    The kernels, the offset-128 convention and the unnormalized Z are
    the river's, deliberately: the two sheets have to be interchangeable
    inputs to one shader. Only the height field underneath them differs,
    and `slopeAnisotropy` is what proves it did.
    """
    rng = random.Random(spec.seed)
    height = draw_lake_ripple_height(rng)

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
        # The measurement this whole sheet exists to satisfy: how much
        # more the surface tilts along one world axis than the other.
        # One means directionless. The river's sheet is nowhere near it.
        "slopeAnisotropy": round(horizontal / max(1, vertical), 4),
    }


def build_lake_scum_mask(spec) -> tuple[Image.Image, dict]:
    """A mask: dark water, bright scum, pooled rather than streaked."""
    rng = random.Random(spec.seed)
    image = ImageChops.multiply(
        square_fractal(((32, 1.0), (128, 0.6), (256, 0.4)), rng),
        Image.new("L", (SHEET_SIZE, SHEET_SIZE), 92),
    )
    draw = ImageDraw.Draw(image)

    # Duckweed rafts: irregular blobs built from overlapping ellipses,
    # so the outline is ragged. Sparse on purpose - the shader
    # multiplies this by a depth threshold to get the line at the
    # boards, and a full mask cannot be thresholded into anything.
    for _ in range(30):
        cx = rng.randrange(SHEET_SIZE)
        cy = rng.randrange(SHEET_SIZE)
        tone = rng.randint(190, 245)
        for _ in range(rng.randint(3, 6)):
            ox = cx + rng.randint(-26, 26)
            oy = cy + rng.randint(-20, 20)
            width = rng.randint(14, 40)
            home.wrap_ellipse(
                draw,
                (ox, oy, ox + width, oy + int(width * rng.uniform(0.7, 1.3))),
                fill=tone + rng.randint(-18, 10),
            )

    # Pollen and dust film: the fine bright speckle that collects on
    # water nothing is moving.
    for _ in range(1100):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(1, 3)
        home.wrap_ellipse(
            draw,
            (x, y, x + radius, y + radius),
            fill=rng.randint(150, 250),
        )

    softened = home.wrap_filter(
        image,
        ImageFilter.GaussianBlur(1.6),
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
    "lake_water_normal": build_lake_water_normal,
    "lake_scum_mask": build_lake_scum_mask,
}

# Transcribed into CityLakeResources.MaximumRippleAnisotropy, which the
# EditMode contract test reads back out of the manifest.
MAXIMUM_RIPPLE_ANISOTROPY = 1.25


def validate_water(image: Image.Image, spec, record: dict) -> None:
    """Wrap, then the bound that belongs to this grammar."""
    river.validate_water_wrap(image, spec, record)

    if spec.grammar == "lake_water_normal":
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
                f"than the other, outside {low:.3f}..{MAXIMUM_RIPPLE_ANISOTROPY}. "
                f"A directional ripple sheet reads as a current no matter "
                f"what the vertex stage does, and the lake has none - that "
                f"is what separates this sheet from the river's."
            )
    else:
        coverage = record["coverage"]
        if not 0.03 <= coverage <= 0.15:
            raise ValueError(
                f"{spec.key} scum covers {coverage * 100:.1f}% of the "
                f"sheet, outside 3..15%. On still water this is a line at "
                f"the boards, not the river's foam field."
            )


# --------------------------------------------------------------------------
# specs — tints transcribe CityLakeWorldBuilder's palette
# --------------------------------------------------------------------------


LAKE_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="CityLakePlankAlbedo",
        grammar="lake_plank",
        seed=0x4C4B504C,
        cast=(1.01, 1.00, 0.98),
        mean_target=0.50,
        meters_per_tile=1.2,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("CityLakeWorldBuilder.Planking", (0.310, 0.280, 0.240)),
            ("CityLakeWorldBuilder.TarredTimber", (0.160, 0.140, 0.120)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityLakeBankAlbedo",
        grammar="lake_bank",
        seed=0x4C4B424E,
        cast=(1.02, 1.00, 0.97),
        mean_target=0.50,
        meters_per_tile=2.4,
        smoothness=0.03,
        metallic=0.0,
        tints=(
            ("CityLakeWorldBuilder.BankClay", (0.240, 0.220, 0.170)),
            ("CityExteriorAppearance.LakeShore", (0.250, 0.340, 0.250)),
        ),
    ),
    home.HomeSheetSpec(
        key="CityLakeHullAlbedo",
        grammar="lake_hull",
        seed=0x4C4B484C,
        cast=(0.99, 1.00, 1.01),
        mean_target=0.50,
        meters_per_tile=1.6,
        smoothness=0.12,
        metallic=0.0,
        tints=(
            ("CityLakeWorldBuilder.HullPaint", (0.260, 0.320, 0.310)),
            ("CityLakeWorldBuilder.HullTar", (0.130, 0.120, 0.110)),
        ),
    ),
)


WATER_SHEET_SPECS: tuple = (
    WaterSheetSpec(
        key="CityLakeWaterNormal",
        grammar="lake_water_normal",
        seed=0x4C4B574E,
        meters_per_tile=3.0,
        linear=True,
    ),
    WaterSheetSpec(
        key="CityLakeScumMask",
        grammar="lake_scum_mask",
        seed=0x4C4B534D,
        meters_per_tile=2.0,
        linear=False,
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
    for spec in LAKE_SHEET_SPECS:
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
            f"slope={record['slopeSpan']} "
            f"anisotropy={record['slopeAnisotropy']:.3f}"
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
        "maximumRippleAnisotropy": MAXIMUM_RIPPLE_ANISOTROPY,
        "sheets": records,
        "waterSheets": water_records,
    }
    manifest_path = args.art_source / "lake-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "lake-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
