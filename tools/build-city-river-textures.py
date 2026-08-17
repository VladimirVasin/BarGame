#!/usr/bin/env python3
"""Build the deterministic surface albedos for the river embankments.

Three seamless 1024x1024 sheets, imported by Unity at 512 and applied by
`CityRiverSurfaceAppearance`: each batch's authored flat colour (brightened
by the solved per-sheet compensation) multiplies the sheet in linear space,
exactly like the home/POI/park/cemetery pipelines.

The measured contract - linear luminance rule, wrap-by-construction
drawing, compensation solving and validation - is imported from
`build-home-textures.py`; this script adds three grammars, one per thing
the embankment is actually made of:

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

The pitches are metre-true: paving tiles four 0.8 m flags across its
3.2 m, quay four 0.55 m courses across its 2.2 m - about the height of
one wall span - and iron repeats every 1.2 m along a rail.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
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


home.GRAMMARS["river_paving"] = draw_river_paving
home.GRAMMARS["river_quay"] = draw_river_quay
home.GRAMMARS["river_iron"] = draw_river_iron


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
