#!/usr/bin/env python3
"""Deterministic expressive speech faces derived from the production pixel faces.

The original neutral pixels own each identity; only brows, lids and the mouth
move. No Blender import is needed. Run directly to publish, or --validate-only
to compare the generated PNGs and manifest without writing assets.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True

from atlas_kit import PixelCanvas, read_generated_png

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Dialogue/Faces"
CELL = 64
COLUMNS = 8
MOUTHS = ("Closed", "Narrow", "Open", "Round", "Wide", "Teeth")
UPPER_FACES = ("Rest", "HalfBlink", "Blink", "Emphasis", "Skeptical")
SOURCES = {
    "Hero": ROOT / "Assets/Player3D/V2/Textures/PlayerFaceAtlas.png",
    "Foreman": ROOT / "Assets/Resources/City/Port/Foreman/PortForemanAtlas.png",
}


def crop(source, x, y):
    width, _, pixels = source
    canvas = PixelCanvas(CELL, CELL)
    for row in range(CELL):
        start = ((y + row) * width + x) * 4
        canvas.pixels[row * CELL * 4:(row + 1) * CELL * 4] = pixels[start:start + CELL * 4]
    return canvas


def copy_tile(source, target, index):
    x, y = index % COLUMNS * CELL, index // COLUMNS * CELL
    for row in range(CELL):
        start = ((y + row) * target.width + x) * 4
        target.pixels[start:start + CELL * 4] = source.pixels[row * CELL * 4:(row + 1) * CELL * 4]


def upper_face(canvas, identity, state):
    if state == "Rest":
        return
    hero = identity == "Hero"
    skin = (174, 141, 123, 255) if hero else (248, 241, 232, 255)
    dark = (57, 45, 46, 255) if hero else (63, 58, 50, 255)
    brow = (8, 8, 11, 255) if hero else (127, 126, 112, 255)
    shadow = (111, 82, 78, 255) if hero else (155, 145, 127, 255)
    white = (143, 135, 128, 255) if hero else (222, 217, 196, 255)
    eye_y = 26 if hero else 29
    centers = (21, 43) if hero else (19, 45)
    for side, center in enumerate(centers):
        left, right = center - 7, center + 7
        canvas.rect(left - 1, 17 if hero else 19, right + 2, eye_y + 7, skin)
        if state == "Skeptical":
            brow_start, brow_end = ((17, 18) if side == 0 else (24, 23)) if hero else ((19, 22) if side == 0 else (28, 24))
            height = 4 if side == 0 else 1
        elif state == "Emphasis":
            brow_start, brow_end = ((17, 19) if side == 0 else (20, 17)) if hero else ((20, 25) if side == 0 else (25, 20))
            height = 5 if hero else 4
        else:
            brow_start, brow_end = ((21, 21) if side == 0 else (22, 21)) if hero else ((23, 27) if side == 0 else (27, 23))
            height = 1 if state == "HalfBlink" else 0
        canvas.line(left, brow_start, right, brow_end, brow, 1 if hero else 2)
        top = eye_y + (2 if state in ("HalfBlink", "Blink") else 0)
        if height:
            canvas.rect(left, top, right + 1, top + height + 1, white)
            canvas.line(left, top, right, top, dark)
            canvas.rect(center, top + 1, center + 3, top + height + 1, dark)
            canvas.line(left + 1, top + height + 2, right - 1, top + height + 3, shadow)
        else:
            canvas.line(left, top, center, top + 1, dark)
            canvas.line(center, top + 1, right, top, dark)
            canvas.line(left + 2, top + 4, right - 2, top + 4, shadow)
    if state == "Emphasis":
        # A short forehead fold connects the raised brow accent to the face.
        canvas.line(25, 13 if hero else 16, 39, 14 if hero else 17, shadow)


def mouth(canvas, identity, shape, clean, soiled):
    if shape == "Closed":
        return
    hero = identity == "Hero"
    skin = (174, 141, 123, 255) if hero else (248, 241, 232, 255)
    lip = (88, 58, 58, 255) if hero else (104, 91, 76, 255)
    cavity = (45, 34, 34, 255) if hero else (48, 40, 33, 255)
    teeth = (175, 165, 143, 255) if hero else (230, 219, 184, 255)
    enamel_shadow = (128, 119, 105, 255) if hero else (177, 161, 129, 255)
    tongue = (116, 73, 69, 255) if hero else (157, 124, 102, 255)
    center_y = 48 if hero else 50
    opening_y = center_y + (1 if hero else 0)
    # Clear the previous line, retaining the jaw/stubble and dirt outside the
    # aperture. The latter stays fixed across all syllables instead of boiling.
    canvas.rect(19, 44, 48, 56 if hero else 57, skin)
    if soiled:
        for y in range(44, 56):
            for x in range(19, 48):
                offset = (y * CELL + x) * 4
                if clean.pixels[offset:offset + 4] != soiled.pixels[offset:offset + 4]:
                    canvas.pixels[offset:offset + 4] = soiled.pixels[offset:offset + 4]
    if shape == "Narrow":
        canvas.ellipse(32, center_y, 11 if hero else 12, 3, lip)
        canvas.ellipse(32, center_y, 9 if hero else 10, 2, cavity)
        canvas.line(27, center_y + 3, 38, center_y + 3, tongue)
    elif shape == "Open":
        canvas.ellipse(32, opening_y, 9 if hero else 11, 7, lip)
        canvas.ellipse(32, opening_y, 7 if hero else 9, 5, cavity)
        canvas.rect(27 if hero else 25, center_y - 3, 38 if hero else 40, center_y - 1, teeth)
        canvas.line(28, center_y + 5, 37, center_y + 5, tongue)
    elif shape == "Round":
        canvas.ellipse(32, opening_y, 6, 7, lip)
        canvas.ellipse(32, opening_y, 3, 5, cavity)
        canvas.line(22, center_y - 1, 24, center_y + 3, lip)
        canvas.line(42, center_y - 1, 40, center_y + 3, lip)
    elif shape == "Wide":
        # Horizontal vowel opening: corners stay level/down, never a smile.
        canvas.ellipse(32, center_y, 14, 5, lip)
        canvas.ellipse(32, center_y, 12, 3, cavity)
        canvas.rect(23, center_y - 2, 42, center_y, teeth)
        canvas.line(20, center_y, 19, center_y + 3, lip)
        canvas.line(44, center_y, 46, center_y + 3, lip)
        canvas.line(27, center_y + 3, 38, center_y + 3, tongue)
    else:
        canvas.ellipse(32, center_y, 12, 4, lip)
        canvas.rect(22, center_y - 2, 43, center_y + 3, cavity)
        canvas.rect(23, center_y - 2, 42, center_y + 1, teeth)
        canvas.line(23, center_y, 41, center_y, enamel_shadow)
        canvas.line(25, center_y + 3, 40, center_y + 3, tongue)
        for x in (28, 34, 39):
            canvas.put(x, center_y - 1, enamel_shadow)


def build(identity, source):
    hero = identity == "Hero"
    clean = crop(source, 0, 0) if hero else crop(source, 192, 192)
    dirty = crop(source, 256, 0) if hero else None
    canvas = PixelCanvas(512, 512 if hero else 256)
    for index in range(canvas.width * canvas.height // (CELL * CELL)):
        copy_tile(dirty if hero and index >= 32 else clean, canvas, index)
    for soil in (False, True) if hero else (False,):
        for upper_index, state in enumerate(UPPER_FACES):
            for mouth_index, shape in enumerate(MOUTHS):
                tile = PixelCanvas(CELL, CELL)
                tile.pixels[:] = (dirty if soil else clean).pixels
                upper_face(tile, identity, state)
                mouth(tile, identity, shape, clean, dirty if soil else None)
                copy_tile(tile, canvas, upper_index * len(MOUTHS) + mouth_index + (32 if soil else 0))
    return canvas


def region(tile, x0, y0, x1, y1):
    return b"".join(tile.pixels[(y * CELL + x0) * 4:(y * CELL + x1) * 4] for y in range(y0, y1))


def validate(identity, canvas, source):
    assert canvas.pixels == build(identity, source).pixels, "Non-deterministic face atlas"
    assert all(alpha == 255 for alpha in canvas.pixels[3::4]), "Faces must be opaque"
    decoded = (canvas.width, canvas.height, canvas.pixels)
    tiles = [crop(decoded, index % COLUMNS * CELL, index // COLUMNS * CELL) for index in range(30)]
    assert len({bytes(tile.pixels) for tile in tiles}) == 30, "Every speech/upper-face combination must differ"
    for state in range(5):
        faces = tiles[state * 6:state * 6 + 6]
        assert len({region(tile, 0, 0, 64, 41) for tile in faces}) == 1, "Mouth shapes must not move the eyes"
        assert len({region(tile, 17, 42, 49, 59) for tile in faces}) == 6, "Six legible mouth silhouettes required"
    for shape in range(6):
        assert len({region(tiles[state * 6 + shape], 0, 0, 64, 41) for state in range(5)}) == 5, "Upper-face accents must read independently"
    if identity == "Hero":
        for index, tile in enumerate(tiles):
            twin = crop(decoded, (index + 32) % COLUMNS * CELL, (index + 32) // COLUMNS * CELL)
            assert region(tile, 0, 0, 64, 44) == region(twin, 0, 0, 64, 44), "Soil cannot alter the upper face"
            assert region(tile, 17, 46, 49, 60) != region(twin, 17, 46, 49, 60), "Soil must survive every mouth"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=OUTPUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    payloads = {}
    manifest = {"generator": Path(__file__).name, "version": "1.0.0", "cell_size": CELL,
                "columns": COLUMNS, "index_origin": "top_left",
                "index_formula": "upper_face * 6 + mouth + (hero_soiled ? 32 : 0)",
                "mouths": list(MOUTHS), "upper_faces": list(UPPER_FACES), "faces": {}}
    for identity, path in SOURCES.items():
        source = read_generated_png(path)
        canvas = build(identity, source)
        validate(identity, canvas, source)
        data = canvas.png_bytes()
        filename = identity + "DialogueFace.png"
        payloads[filename] = data
        manifest["faces"][identity] = {"texture": filename, "width": canvas.width, "height": canvas.height,
            "rows": canvas.height // CELL, "soiled_offset": 32 if identity == "Hero" else 0,
            "source": path.relative_to(ROOT).as_posix(), "source_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "sha256": hashlib.sha256(data).hexdigest(), "base_tint": [1, 1, 1, 1] if identity == "Hero" else [.61, .47, .385, 1]}
    payloads["DialogueFaces.json"] = (json.dumps(manifest, indent=2) + "\n").encode("utf-8")
    if args.validate_only:
        for filename, data in payloads.items():
            assert (args.output_dir / filename).read_bytes() == data, "Published dialogue atlas differs: " + filename
    else:
        from asset_pipeline import publish_files, workspace_temporary_directory
        with workspace_temporary_directory("dialogue-faces-") as staging:
            pairs = []
            for filename, data in payloads.items():
                temporary = Path(staging) / filename
                temporary.write_bytes(data)
                pairs.append((temporary, args.output_dir / filename))
            publish_files(pairs)
    print("DIALOGUE FACE ATLAS CONTRACT OK: deterministic cells, independent mouth/brows/blinks, persistent hero soil")


if __name__ == "__main__":
    main()
