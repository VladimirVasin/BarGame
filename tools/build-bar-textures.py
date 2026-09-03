#!/usr/bin/env python3
"""Build deterministic surface albedos for the bar interior and exterior.

Seventeen seamless 1024x1024 sheets, imported by Unity at 512. Fifteen split
the pub into visibly different physical materials instead of tinting bare
meshes: floorboards, wallpaper, carved and polished timber, leather, plaster,
brass, mirror and patterned glass, carpet, cloth, painted metal, paper,
bottle glass and ceramic. Two carry the old neighbourhood-pub exterior.

The measured contract is imported from `build-home-textures.py`, following
the houses' material-per-surface approach. Shared grammars remain shared;
pub-specific brass, glass and paper wear live here.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Bar" / "Textures"
DEFAULT_ART_SOURCE = ROOT / "ArtSource" / "Bar"

_home_spec = importlib.util.spec_from_file_location(
    "build_home_textures",
    Path(__file__).resolve().parent / "build-home-textures.py",
)
home = importlib.util.module_from_spec(_home_spec)
sys.modules["build_home_textures"] = home
_home_spec.loader.exec_module(home)

SHEET_SIZE = home.SHEET_SIZE


def draw_exterior_brick(base: Image.Image, rng) -> Image.Image:
    """Small stretcher-bond brick, periodic by construction.

    The 128 x 64 px cell gives a deliberately old, fine urban course at the
    sheet's 1.2 m pitch. Every line wraps; the albedo contains material grain
    and repair only, never windows or architectural markings.
    """
    draw = ImageDraw.Draw(base)
    brick_width = 128
    course_height = 64
    mortar = 5

    for row in range(SHEET_SIZE // course_height):
        y = row * course_height
        offset = brick_width // 2 if row % 2 else 0
        for column in range(-1, SHEET_SIZE // brick_width + 1):
            x = column * brick_width + offset
            tone = home.BASE + rng.randint(-15, 13)
            home.wrap_rect(
                draw,
                (
                    x + mortar,
                    y + mortar,
                    x + brick_width - mortar,
                    y + course_height - mortar,
                ),
                tone,
            )

            # Fired flecks and chipped arrises survive the 512 import without
            # turning the wall into a noisy damage decal.
            for _ in range(rng.randint(1, 3)):
                fleck_x = x + rng.randint(12, brick_width - 15)
                fleck_y = y + rng.randint(11, course_height - 13)
                fleck_width = rng.randint(3, 8)
                home.wrap_rect(
                    draw,
                    (
                        fleck_x,
                        fleck_y,
                        fleck_x + fleck_width,
                        fleck_y + rng.randint(2, 4),
                    ),
                    tone + rng.choice((-22, -16, 12)),
                )

        mortar_tone = home.BASE + rng.randint(19, 28)
        home.wrap_rect(
            draw,
            (0, y - mortar // 2, SHEET_SIZE, y + mortar // 2),
            mortar_tone,
        )
        for column in range(-1, SHEET_SIZE // brick_width + 1):
            x = column * brick_width + offset
            home.wrap_rect(
                draw,
                (
                    x - mortar // 2,
                    y + mortar // 2,
                    x + mortar // 2,
                    y + course_height - mortar // 2,
                ),
                mortar_tone + rng.randint(-5, 4),
            )

    def soot_and_lime(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(7):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(80, 190), y + rng.randint(90, 240)),
                fill=128 - rng.randint(7, 14),
            )
        for _ in range(5):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(45, 130), y + rng.randint(35, 90)),
                fill=128 + rng.randint(5, 10),
            )

    return home.soft_overlay(base, 18.0, soot_and_lime)


def draw_exterior_plaster(base: Image.Image, rng) -> Image.Image:
    """Weathered stucco/whitewash without concrete formwork marks."""
    plaster = home.draw_whitewash(base, rng)
    draw = ImageDraw.Draw(plaster)

    # Thin trowel scars and small repairs keep the close read handmade. They
    # remain sparse so the wall does not become a map of narrative damage.
    for _ in range(18):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(45, 150)
        home.wrap_line(
            draw,
            (x, y, x + length, y + rng.randint(-3, 3)),
            home.BASE + rng.randint(-10, 8),
            width=rng.randint(1, 2),
        )
    for _ in range(4):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        width = rng.randint(70, 170)
        height = rng.randint(45, 120)
        home.wrap_rect(
            draw,
            (x, y, x + width, y + height),
            home.BASE + rng.randint(5, 13),
        )
        home.wrap_line(
            draw,
            (x, y + height, x + width, y + height),
            home.BASE - rng.randint(11, 18),
            width=2,
        )

    def runoff(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(13):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            width = rng.randint(5, 19)
            length = rng.randint(80, 260)
            steps = 4
            for step in range(steps):
                tone = 128 - 18 + step * 4
                home.wrap_rect(
                    layer_draw,
                    (
                        x,
                        y + length * step // steps,
                        x + width,
                        y + length * (step + 1) // steps,
                    ),
                    tone,
                )

    return home.soft_overlay(plaster, 13.0, runoff)


def draw_polished_wood(base: Image.Image, rng) -> Image.Image:
    """Long lacquered grain with rings, scratches and repaired dull patches."""
    wood = home.draw_veneer(base, rng)
    draw = ImageDraw.Draw(wood)
    for _ in range(9):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        diameter = rng.randint(38, 82)
        home.wrap_ellipse(
            draw,
            (x, y, x + diameter, y + diameter),
            outline=home.BASE - rng.randint(16, 28),
            width=2,
        )
    for _ in range(26):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        length = rng.randint(45, 210)
        home.wrap_line(
            draw,
            (x, y, x + length, y + rng.randint(-4, 4)),
            home.BASE + rng.choice((-20, -13, 14)),
            width=1,
        )
    return wood


def draw_aged_brass(base: Image.Image, rng) -> Image.Image:
    """Brushed brass with dark handling bands and green-black tarnish islands."""
    draw = ImageDraw.Draw(base)
    for y in range(0, SHEET_SIZE, 16):
        tone = home.BASE + (5 if (y // 16) % 2 else -5)
        home.wrap_line(draw, (0, y, SHEET_SIZE, y), tone, width=1)
    for _ in range(520):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(24, 180), y + rng.randint(-2, 2)),
            home.BASE + rng.randint(-13, 12),
            width=1,
        )

    def tarnish(layer_draw: ImageDraw.ImageDraw) -> None:
        for _ in range(13):
            x = rng.randrange(SHEET_SIZE)
            y = rng.randrange(SHEET_SIZE)
            home.wrap_ellipse(
                layer_draw,
                (x, y, x + rng.randint(50, 180), y + rng.randint(28, 100)),
                fill=128 - rng.randint(18, 34),
            )

    return home.soft_overlay(base, 8.0, tarnish)


def draw_mirror_glass(base: Image.Image, rng) -> Image.Image:
    """Old mirror: vertical cleaning streaks, edge haze and sparse foxing."""
    draw = ImageDraw.Draw(base)
    for _ in range(150):
        x = rng.randrange(SHEET_SIZE)
        tone = home.BASE + rng.randint(-12, 12)
        home.wrap_line(
            draw,
            (x, 0, x + rng.randint(-5, 5), SHEET_SIZE),
            tone,
            width=rng.randint(1, 3),
        )
    for _ in range(95):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(2, 7)
        home.wrap_ellipse(
            draw,
            (x - radius, y - radius, x + radius, y + radius),
            fill=home.BASE - rng.randint(28, 58),
        )
    return base


def draw_patterned_glass(base: Image.Image, rng) -> Image.Image:
    """Pressed diamond glass whose broad relief survives the PS1 composite."""
    draw = ImageDraw.Draw(base)
    pitch = 128
    for offset in range(-SHEET_SIZE, SHEET_SIZE * 2, pitch):
        home.wrap_line(
            draw,
            (offset, 0, offset + SHEET_SIZE, SHEET_SIZE),
            home.BASE - 24,
            width=5,
        )
        home.wrap_line(
            draw,
            (offset + SHEET_SIZE, 0, offset, SHEET_SIZE),
            home.BASE + 14,
            width=3,
        )
    return base


def draw_pub_paper(base: Image.Image, rng) -> Image.Image:
    """Fibrous, finger-marked stock for menus, labels and unlettered notices."""
    paper = home.draw_whitewash(base, rng)
    draw = ImageDraw.Draw(paper)
    for _ in range(180):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_line(
            draw,
            (x, y, x + rng.randint(8, 55), y + rng.randint(-3, 3)),
            home.BASE + rng.randint(-13, 12),
            width=1,
        )
    for _ in range(7):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        home.wrap_ellipse(
            draw,
            (x, y, x + rng.randint(55, 150), y + rng.randint(35, 110)),
            outline=home.BASE - rng.randint(16, 25),
            width=3,
        )
    return paper


def draw_bottle_glass(base: Image.Image, rng) -> Image.Image:
    """Moulded glass with vertical flow, bubbles and uneven old bottle walls."""
    draw = ImageDraw.Draw(base)
    for x in range(0, SHEET_SIZE, 32):
        tone = home.BASE + (9 if (x // 32) % 2 else -8)
        home.wrap_rect(draw, (x, 0, x + 3, SHEET_SIZE), tone)
    for _ in range(115):
        x = rng.randrange(SHEET_SIZE)
        y = rng.randrange(SHEET_SIZE)
        radius = rng.randint(2, 9)
        home.wrap_ellipse(
            draw,
            (x - radius, y - radius, x + radius, y + radius),
            outline=home.BASE + rng.randint(15, 28),
            width=2,
        )
    return base


home.GRAMMARS["bar_exterior_brick"] = draw_exterior_brick
home.GRAMMARS["bar_exterior_plaster"] = draw_exterior_plaster
home.GRAMMARS["bar_polished_wood"] = draw_polished_wood
home.GRAMMARS["bar_aged_brass"] = draw_aged_brass
home.GRAMMARS["bar_mirror_glass"] = draw_mirror_glass
home.GRAMMARS["bar_patterned_glass"] = draw_patterned_glass
home.GRAMMARS["bar_paper"] = draw_pub_paper
home.GRAMMARS["bar_bottle_glass"] = draw_bottle_glass


BAR_SHEET_SPECS: tuple[home.HomeSheetSpec, ...] = (
    home.HomeSheetSpec(
        key="BarWornPlankAlbedo",
        grammar="planks",
        seed=0x42415250,
        cast=(1.03, 1.00, 0.95),
        mean_target=0.50,
        meters_per_tile=1.5,
        smoothness=0.08,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Floor",
             (0.14, 0.06, 0.042)),
            ("BarInteriorWorldBuilder.Wood",
             (0.16, 0.055, 0.028)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarWallpaperAlbedo",
        grammar="wallpaper",
        seed=0x42415257,
        cast=(1.03, 1.00, 0.96),
        mean_target=0.50,
        meters_per_tile=1.8,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Wall",
             (0.29, 0.075, 0.075)),
            ("BarInteriorWorldBuilder.WallPanel",
             (0.13, 0.042, 0.032)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarDarkWoodAlbedo",
        grammar="veneer",
        seed=0x42415244,
        cast=(1.02, 1.00, 0.96),
        mean_target=0.50,
        meters_per_tile=1.1,
        smoothness=0.12,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.DarkWood",
             (0.075, 0.024, 0.017)),
            ("BarInteriorWorldBuilder.Wood",
             (0.16, 0.055, 0.028)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarWornLeatherAlbedo",
        grammar="weave",
        seed=0x4241524C,
        cast=(1.02, 0.99, 0.97),
        mean_target=0.50,
        meters_per_tile=0.9,
        smoothness=0.06,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Leather",
             (0.30, 0.035, 0.045)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarCeilingPlasterAlbedo",
        grammar="whitewash",
        seed=0x4243504C,
        cast=(1.03, 1.00, 0.94),
        mean_target=0.54,
        meters_per_tile=2.4,
        smoothness=0.025,
        metallic=0.0,
        tints=(("BarInteriorWorldBuilder.Ceiling", (0.18, 0.14, 0.11)),),
    ),
    home.HomeSheetSpec(
        key="BarPolishedWoodAlbedo",
        grammar="bar_polished_wood",
        seed=0x42505744,
        cast=(1.05, 1.00, 0.92),
        mean_target=0.52,
        meters_per_tile=0.75,
        smoothness=0.34,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.CounterTop", (0.16, 0.055, 0.028)),
            ("BarInteriorWorldBuilder.TableTop", (0.22, 0.095, 0.045)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarAgedBrassAlbedo",
        grammar="bar_aged_brass",
        seed=0x42425253,
        cast=(1.08, 1.00, 0.84),
        mean_target=0.72,
        meters_per_tile=0.42,
        smoothness=0.42,
        metallic=0.72,
        tints=(
            ("BarDistrictIdentity.Memory.Metal", (0.62, 0.34, 0.13)),
            ("BarDistrictIdentity.Household.Metal", (0.86, 0.46, 0.14)),
            ("BarDistrictIdentity.AfterShift.Metal", (0.32, 0.40, 0.38)),
            ("BarDistrictIdentity.Escape.Metal", (0.52, 0.18, 0.44)),
        ),
        contrast_floor=26,
    ),
    home.HomeSheetSpec(
        key="BarMirrorGlassAlbedo",
        grammar="bar_mirror_glass",
        seed=0x424D4952,
        cast=(0.95, 1.02, 1.05),
        mean_target=0.62,
        meters_per_tile=1.35,
        smoothness=0.78,
        metallic=0.12,
        tints=(("BarInteriorWorldBuilder.Mirror", (0.22, 0.34, 0.38)),),
        contrast_floor=28,
    ),
    home.HomeSheetSpec(
        key="BarPatternedGlassAlbedo",
        grammar="bar_patterned_glass",
        seed=0x4250474C,
        cast=(0.96, 1.02, 1.04),
        mean_target=0.58,
        meters_per_tile=0.72,
        smoothness=0.64,
        metallic=0.04,
        tints=(("BarInteriorWorldBuilder.PatternedGlass", (0.18, 0.28, 0.30)),),
        contrast_floor=30,
    ),
    home.HomeSheetSpec(
        key="BarPubCarpetAlbedo",
        grammar="rug",
        seed=0x42435250,
        cast=(1.06, 0.98, 0.92),
        mean_target=0.56,
        meters_per_tile=1.15,
        smoothness=0.015,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Carpet", (0.22, 0.055, 0.052)),
            ("BarInteriorWorldBuilder.CarpetBorder", (0.36, 0.27, 0.10)),
            ("BarInteriorWorldBuilder.BayRug", (0.16, 0.065, 0.055)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarWornFabricAlbedo",
        grammar="linen",
        seed=0x42464142,
        cast=(1.04, 0.99, 0.95),
        mean_target=0.48,
        meters_per_tile=0.68,
        smoothness=0.02,
        metallic=0.0,
        tints=(
            ("BarInteriorWorldBuilder.Curtain", (0.30, 0.035, 0.045)),
            ("BarInteriorWorldBuilder.Shade", (0.32, 0.18, 0.14)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarPaintedMetalAlbedo",
        grammar="painted_metal",
        seed=0x424D4554,
        cast=(0.99, 1.00, 0.98),
        mean_target=0.56,
        meters_per_tile=0.82,
        smoothness=0.20,
        metallic=0.30,
        tints=(
            ("BarInteriorWorldBuilder.DarkMetal", (0.18, 0.20, 0.19)),
            ("BarInteriorWorldBuilder.Speaker", (0.12, 0.12, 0.13)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarPaperAlbedo",
        grammar="bar_paper",
        seed=0x42504150,
        cast=(1.04, 1.00, 0.90),
        mean_target=0.70,
        meters_per_tile=0.55,
        smoothness=0.025,
        metallic=0.0,
        tints=(
            ("BarService.MenuPage", (0.74, 0.66, 0.47)),
            ("BarService.BottleLabel", (0.72, 0.66, 0.48)),
            ("BarInteriorWorldBuilder.Notice", (0.62, 0.38, 0.20)),
        ),
        contrast_floor=24,
    ),
    home.HomeSheetSpec(
        key="BarBottleGlassAlbedo",
        grammar="bar_bottle_glass",
        seed=0x42474C53,
        cast=(0.95, 1.03, 1.02),
        mean_target=0.78,
        meters_per_tile=0.36,
        smoothness=0.68,
        metallic=0.02,
        tints=(
            ("BarService.ColouredBottle", (0.28, 0.20, 0.11)),
            ("BarService.ClearGlass", (0.62, 0.82, 0.86)),
            ("BarService.Liquid", (0.90, 0.58, 0.18)),
        ),
        contrast_floor=24,
    ),
    home.HomeSheetSpec(
        key="BarCeramicAlbedo",
        grammar="enamel",
        seed=0x42434552,
        cast=(1.02, 1.00, 0.96),
        mean_target=0.70,
        meters_per_tile=0.42,
        smoothness=0.48,
        metallic=0.04,
        tints=(
            ("BarInteriorWorldBuilder.Cup", (0.82, 0.12, 0.10)),
            ("BarInteriorWorldBuilder.Foam", (0.74, 0.62, 0.42)),
        ),
        contrast_floor=22,
    ),
    home.HomeSheetSpec(
        key="BarExteriorBrickAlbedo",
        grammar="bar_exterior_brick",
        seed=0x42455842,
        cast=(1.04, 0.99, 0.94),
        mean_target=0.46,
        meters_per_tile=1.2,
        smoothness=0.04,
        metallic=0.0,
        tints=(
            ("bar_exterior.py.BRICK",
             (0.30, 0.12, 0.075)),
        ),
    ),
    home.HomeSheetSpec(
        key="BarExteriorPlasterAlbedo",
        grammar="bar_exterior_plaster",
        seed=0x42455850,
        cast=(1.03, 1.00, 0.95),
        mean_target=0.48,
        meters_per_tile=2.6,
        smoothness=0.035,
        metallic=0.0,
        tints=(
            ("bar_exterior.py.PLASTER",
             (0.48, 0.44, 0.34)),
        ),
    ),
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--textures",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
    )
    parser.add_argument(
        "--art-source",
        type=Path,
        default=DEFAULT_ART_SOURCE,
    )
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    records: list[dict] = []
    built: list[tuple[home.HomeSheetSpec, Image.Image]] = []
    for spec in BAR_SHEET_SPECS:
        image, record = home.build_sheet(spec)
        record["resourcePath"] = f"Bar/Textures/{spec.key}"
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
    manifest_path = args.art_source / "bar-textures.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    contact = home.build_contact_sheet(built)
    contact_path = args.art_source / "bar-contact-sheet.png"
    contact.save(contact_path, format="PNG", compress_level=9)
    print(f"Wrote {manifest_path} and {contact_path}.")


if __name__ == "__main__":
    main()
