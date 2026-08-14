#!/usr/bin/env python3
"""Build the deterministic window-pane albedo for the generated city.

The sheet is a 2x2 grid of pane variants: plain glass, curtains, blinds and
a lamp burning near one end. The runtime maps one whole cell onto one
geometric pane -- a 1.6-1.9 m by 0.48 m strip standing inside the facade
albedo's baked reveal -- and picks the variant per pane with a stable hash,
so one seed always lights the same rooms the same way.

Authoring rules, both inherited from the facade sheets:

* Light glass carrying dark features. URP multiplies ``_BaseColor`` by
  ``_BaseMap``; the per-family window colour (warm, cold, bar, home,
  supermarket) keeps providing the hue and the sheet only shapes it. Frames
  and mullions are dark, so they stay dark inside a glowing pane; glass is
  near-white, so it carries the full glow at night and reads as dark glazing
  once the day factor pulls the colour down.
* The cell is square but the pane is roughly 3.5:1, so vertical features are
  authored thin (one pixel is about 7 mm across the pane) and horizontal
  features thick (one pixel is about 2 mm up the pane). The frame tones
  match the facade sheet's reveal and mullion values, which is what keeps
  the standing pane and the painted aperture reading as one window.

Pillow is the only dependency. Run from the repository root::

    python tools/build-city-window-textures.py
"""

from __future__ import annotations

import argparse
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TEXTURE_DIR = ROOT / "Assets" / "Resources" / "Textures"

SEED = 260814
SHEET_SIZE = 512
CELL = SHEET_SIZE // 2
VARIANTS = ("plain", "curtains", "blinds", "lamp")

# Tones shared with tools/build-city-facade-textures.py so the standing pane
# agrees with the painted reveal around it.
FRAME = 46
FRAME_SHADOW = 28
MULLION = 58
GLASS = 230
GLASS_TOP = 214
CURTAIN = 148
BLIND_DARK = 196
BLIND_LIGHT = 238
LAMP_BASE = 202

# Frame thicknesses in cell pixels, pre-corrected for the 3.5:1 stretch.
SIDE_FRAME = 10
TOP_FRAME = 24
SIDE_SHADOW = 3
TOP_SHADOW = 7
MULLION_WIDTH = 7
CASEMENTS = 3


def draw_glass(draw: ImageDraw.ImageDraw, box, rng: random.Random,
               base: int, top: int) -> None:
    """Vertical gradient glass with a faint per-row shimmer."""
    left, upper, right, lower = box
    height = lower - upper
    for row in range(height):
        blend = row / max(1, height - 1)
        tone = round(top + (base - top) * blend)
        tone += rng.randint(-2, 2)
        draw.line(
            [(left, upper + row), (right - 1, upper + row)],
            fill=max(0, min(255, tone)),
        )


def casement_boxes(glass_box):
    """Split the glass strip into the mullioned casements."""
    left, upper, right, lower = glass_box
    width = right - left
    boxes = []
    for index in range(CASEMENTS):
        inner_left = left + round(width * index / CASEMENTS)
        inner_right = left + round(width * (index + 1) / CASEMENTS)
        if index > 0:
            inner_left += MULLION_WIDTH // 2 + 1
        if index < CASEMENTS - 1:
            inner_right -= MULLION_WIDTH // 2 + 1
        boxes.append((inner_left, upper, inner_right, lower))
    return boxes


def draw_curtains(cell: Image.Image, glass_box, rng: random.Random) -> None:
    """Soft curtain bands hanging into each casement from its edges."""
    overlay = Image.new("L", cell.size, 0)
    mask = Image.new("L", cell.size, 0)
    overlay_draw = ImageDraw.Draw(overlay)
    mask_draw = ImageDraw.Draw(mask)
    for left, upper, right, lower in casement_boxes(glass_box):
        width = right - left
        reach = round(width * rng.uniform(0.22, 0.34))
        for side_left, side_right in (
            (left, left + reach),
            (right - reach, right),
        ):
            overlay_draw.rectangle(
                (side_left, upper, side_right, lower),
                fill=CURTAIN + rng.randint(-6, 6),
            )
            mask_draw.rectangle(
                (side_left, upper, side_right, lower),
                fill=255,
            )
    overlay = overlay.filter(ImageFilter.GaussianBlur(2.2))
    mask = mask.filter(ImageFilter.GaussianBlur(2.2))
    cell.paste(overlay, (0, 0), mask)


def draw_blinds(draw: ImageDraw.ImageDraw, glass_box,
                rng: random.Random) -> None:
    """Horizontal slats over the whole strip, slightly uneven."""
    left, upper, right, lower = glass_box
    row = upper
    slat = 13
    while row < lower:
        tone = BLIND_DARK if ((row - upper) // slat) % 2 == 0 else BLIND_LIGHT
        tone += rng.randint(-3, 3)
        draw.rectangle(
            (left, row, right - 1, min(lower - 1, row + slat - 1)),
            fill=max(0, min(255, tone)),
        )
        row += slat


def draw_lamp(cell: Image.Image, glass_box, rng: random.Random) -> None:
    """One bright pool near an end of the pane: somebody is home."""
    left, upper, right, lower = glass_box
    width = right - left
    height = lower - upper
    center_x = left + round(width * rng.uniform(0.18, 0.30))
    center_y = upper + round(height * rng.uniform(0.55, 0.72))
    radius = round(width * 0.24)
    glow = Image.new("L", cell.size, 0)
    glow_draw = ImageDraw.Draw(glow)
    for step in range(radius, 0, -2):
        alpha = round(255 * (1 - step / radius) ** 1.6)
        glow_draw.ellipse(
            (
                center_x - step,
                center_y - round(step * height / width) ,
                center_x + step,
                center_y + round(step * height / width),
            ),
            fill=alpha,
        )
    glow = glow.filter(ImageFilter.GaussianBlur(3.0))
    bright = Image.new("L", cell.size, 255)
    cell.paste(bright, (0, 0), glow.point(lambda a: a * 95 // 255))


def build_cell(variant: str, rng: random.Random) -> Image.Image:
    cell = Image.new("L", (CELL, CELL), FRAME)
    draw = ImageDraw.Draw(cell)

    # Frame with its shadowed inner reveal line.
    for _ in range(140):
        x = rng.randrange(CELL)
        y = rng.randrange(CELL)
        draw.point((x, y), fill=FRAME + rng.randint(-5, 5))
    draw.rectangle(
        (
            SIDE_FRAME,
            TOP_FRAME,
            CELL - 1 - SIDE_FRAME,
            CELL - 1 - TOP_FRAME,
        ),
        fill=FRAME_SHADOW,
    )
    glass_box = (
        SIDE_FRAME + SIDE_SHADOW,
        TOP_FRAME + TOP_SHADOW,
        CELL - SIDE_FRAME - SIDE_SHADOW,
        CELL - TOP_FRAME - TOP_SHADOW,
    )

    base = LAMP_BASE if variant == "lamp" else GLASS
    top = GLASS_TOP if variant != "lamp" else LAMP_BASE - 16
    draw_glass(draw, glass_box, rng, base, top)

    if variant == "curtains":
        draw_curtains(cell, glass_box, rng)
    elif variant == "blinds":
        draw_blinds(draw, glass_box, rng)
    elif variant == "lamp":
        draw_lamp(cell, glass_box, rng)

    # Mullions go on top of every treatment: the sash is in front of
    # whatever hangs behind it.
    left, upper, right, lower = glass_box
    width = right - left
    for index in range(1, CASEMENTS):
        center = left + round(width * index / CASEMENTS)
        draw.rectangle(
            (
                center - MULLION_WIDTH // 2,
                upper - TOP_SHADOW,
                center + MULLION_WIDTH // 2,
                lower + TOP_SHADOW - 1,
            ),
            fill=MULLION,
        )

    return cell


def build_sheet() -> Image.Image:
    sheet = Image.new("L", (SHEET_SIZE, SHEET_SIZE), FRAME)
    for index, variant in enumerate(VARIANTS):
        rng = random.Random(f"{SEED}:{variant}")
        cell = build_cell(variant, rng)
        sheet.paste(
            cell,
            ((index % 2) * CELL, (index // 2) * CELL),
        )
    return sheet.convert("RGB")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--texture-dir",
        type=Path,
        default=DEFAULT_TEXTURE_DIR,
    )
    args = parser.parse_args()
    args.texture_dir.mkdir(parents=True, exist_ok=True)

    sheet = build_sheet()
    target = args.texture_dir / "CityWindowAlbedo.png"
    sheet.save(target, optimize=True)

    print("CITY WINDOW TEXTURES BUILD OK")
    print(f"  Sheet: {SHEET_SIZE} with {len(VARIANTS)} pane variants")
    print(f"  Output: {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
