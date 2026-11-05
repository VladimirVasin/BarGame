#!/usr/bin/env python3
"""Give each existing worker face the six mouth poses and five expressions of shared speech."""
from pathlib import Path

from atlas_kit import PixelCanvas, read_generated_png
from dialogue_face_atlas import CELL, COLUMNS, MOUTHS, UPPER_FACES, copy_tile, ROOT


def draw_expression(tile, shape, expression):
    # Preserve the assigned skin, beard, brows and hair pixels around each moving feature.
    if expression in ("HalfBlink", "Blink"):
        for x in (20, 44):
            tile.rect(x - 4, 27, x + 5, 29 if expression == "HalfBlink" else 31, (172, 138, 114, 255))
            tile.line(x - 4, 28 if expression == "HalfBlink" else 30, x + 4, 29 if expression == "HalfBlink" else 30,
                      (95, 78, 65, 255))
    if expression in ("Emphasis", "Skeptical"):
        for x in ((20, 44) if expression == "Emphasis" else (20,)):
            for y in range(18, 26):
                a = (64*y + x - 6) * 4
                tile.pixels[a:a+52] = tile.pixels[a+256:a+308]
    if shape == "Closed":
        return
    rx, ry = {"Narrow": (6, 2), "Open": (5, 4), "Round": (3, 4),
              "Wide": (7, 3), "Teeth": (7, 2)}[shape]
    tile.ellipse(32, 50, rx, ry, (115, 77, 61, 255))
    tile.ellipse(32, 50, rx - 1, ry - 1, (67, 48, 43, 255))
    if shape in ("Open", "Wide", "Teeth"):
        tile.line(33 - rx, 50 - (ry - 1), 31 + rx, 50 - (ry - 1), (181, 166, 139, 255))


def generate(path):
    width, height, data = read_generated_png(path)
    assert (width, height) == (512, 512)
    neutral = PixelCanvas(CELL, CELL)
    for y in range(CELL):
        for x in range(CELL):
            at = ((385 + y * 2) * width + 385 + x * 2) * 4
            neutral.put(x, y, data[at:at+4])
    atlas = PixelCanvas(CELL * COLUMNS, CELL * 4)
    for index in range(COLUMNS * 4):
        tile = PixelCanvas(CELL, CELL)
        tile.pixels[:] = neutral.pixels
        if index < len(UPPER_FACES) * len(MOUTHS):
            draw_expression(tile, MOUTHS[index % len(MOUTHS)], UPPER_FACES[index // len(MOUTHS)])
        copy_tile(tile, atlas, index)
    return atlas.png_bytes()


if __name__ == "__main__":
    source = ROOT / "Assets/Resources/VillageLife"
    output = ROOT / "Assets/Resources/Dialogue/Faces/Workers"
    output.mkdir(exist_ok=True)
    for original in sorted(source.glob("StationWorker*Atlas.png")):
        (output / original.name).write_bytes(generate(original))
