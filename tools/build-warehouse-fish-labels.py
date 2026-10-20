#!/usr/bin/env python3
"""Paint three large warehouse can-label drawings into one bounded atlas."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import sys
import uuid

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image, ImageDraw
import toolchain
from asset_pipeline import publish_files, workspace_temporary_directory

ROOT = Path(__file__).resolve().parents[1]
TEXTURE = ROOT / "Assets/Resources/CombatWarehouse/Textures/WarehouseFishLabels.png"
SOURCE = ROOT / "ArtSource/CombatWarehouse/FishProps/WarehouseFishLabels.json"
LABELS = (("RottenFishTailCan", "tail"), ("RottenFishBellyCan", "belly"), ("RottenFishHeadCan", "head"))
SIZE = 512


def drawing(draw, kind, top):
    ink, paper = (14, 20, 15), (220, 217, 188)
    def points(values):
        return [(x, y + top) for x, y in values]
    def line(values, color=paper, width=9):
        draw.line(points(values), fill=color, width=width, joint="curve")
    def ellipse(bounds, color):
        draw.ellipse((bounds[0], top + bounds[1], bounds[2], top + bounds[3]), fill=color)
    if kind == "tail":
        draw.polygon(points([(91, 56), (191, 65), (283, 31), (421, 16),
            (329, 85), (421, 154), (283, 139), (191, 105), (91, 114)]), fill=ink)
        line([(114, 66), (114, 103)], width=7)
        for tip in ((340, 37), (369, 30), (340, 133), (369, 140)):
            line([(189, 85), tip], width=7)
    elif kind == "belly":
        draw.polygon(points([(112, 43), (303, 39), (373, 61), (409, 91),
             (369, 126), (307, 138), (284, 154), (258, 139), (112, 129)]), fill=ink)
        ellipse((75, 41, 161, 131), ink)
        ellipse((87, 53, 149, 119), paper)
        ellipse((106, 75, 132, 99), ink)
        line([(158, 64), (295, 60), (365, 82)], width=9)
        for x in (181, 215, 249, 283, 317):
            line([(x, 77), (x + 4, 97), (x - 7, 121)], width=9)
    elif kind == "head":
        draw.polygon(points([(102, 36), (266, 32), (326, 49), (395, 75),
             (427, 80), (427, 91), (395, 98), (406, 109), (352, 131),
             (291, 142), (102, 134)]), fill=ink)
        line([(119, 48), (119, 122)], width=7)
        line([(227, 46), (251, 67), (253, 99), (235, 125)], width=11)
        ellipse((307, 57, 341, 91), paper)
        ellipse((317, 67, 331, 81), ink)
        line([(354, 92), (424, 89)], width=9)
    else:
        raise ValueError("Unknown printed fish drawing")


def build():
    # Only faint paper grain; darker wear is confined to the edge so it cannot
    # erase broad picture strokes at distance or under mip filtering.
    yy, xx = np.mgrid[:SIZE, :SIZE]
    grain = ((xx * 13 + yy * 7 + xx * yy * 3) % 5 - 2).astype(np.int16)
    pixels = np.clip(np.array((220, 217, 188), dtype=np.int16) + grain[:, :, None], 0, 255).astype(np.uint8)
    image = Image.fromarray(pixels)
    draw = ImageDraw.Draw(image)
    records = []
    for index, (name, kind) in enumerate(LABELS):
        top, bottom = index * 170, (index + 1) * 170 if index < 2 else SIZE
        draw.rectangle((8, top + 7, SIZE - 9, bottom - 8), outline=(167, 166, 139), width=2)
        for patch in range(10):
            x = 13 + ((patch * 41 + index * 67) % 481)
            y = top + 8 if patch % 2 else bottom - 14
            draw.rectangle((x, y, min(x + 8, SIZE - 14), y + 4), fill=(190, 188, 159))
        drawing(draw, kind, top)
        records.append({"model": name, "kind": kind, "cell_pixels": [0, top, SIZE, bottom],
                        "front_arc_radians": [-1.3, 1.3], "ink_bounds_pixels": [75, top + 16, 428, top + 155]})
    stream = io.BytesIO()
    image.save(stream, format="PNG", optimize=False)
    raw = stream.getvalue()
    record = {"generator": "tools/build-warehouse-fish-labels.py", "version": "2.0.0",
        "texture": "Assets/Resources/CombatWarehouse/Textures/WarehouseFishLabels.png",
        "sha256": hashlib.sha256(raw).hexdigest(), "size": SIZE, "mode": "RGB", "sRGB": True,
        "mipmaps": True, "wrap": "Clamp", "windows_compression": "BC7",
        "labels": records, "text": False, "authoring": "large filled fish-part drawings and broad cut/rib/fin strokes",
        "wear": "faint paper grain and border only; drawings intact"}
    return raw, record


def meta():
    sidecar = Path(str(TEXTURE) + ".meta")
    if sidecar.exists():
        return
    source = ROOT / "Assets/Resources/CombatWarehouse/Textures/WarehouseMasterAtlas.png.meta"
    settings = source.read_text(encoding="utf8")
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/Assets/Resources/CombatWarehouse/Textures/WarehouseFishLabels.png").hex
    settings = re.sub(r"^guid: [0-9a-f]+$", "guid: " + guid, settings, flags=re.MULTILINE)
    settings = re.sub(r"(?m)^(\s*maxTextureSize:) \d+$", r"\1 512", settings)
    sidecar.write_text(settings, encoding="utf8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    toolchain.check_python(toolchain.load_config(), ("Pillow", "numpy"))
    raw, record = build()
    if build() != (raw, record):
        raise RuntimeError("Fish-label drawings are not deterministic")
    manifest = json.dumps(record, ensure_ascii=False, indent=2) + "\n"
    if args.validate_only:
        if TEXTURE.read_bytes() != raw or SOURCE.read_text(encoding="utf8") != manifest:
            raise RuntimeError("Published fish drawings differ from the deterministic picture contract")
    else:
        with workspace_temporary_directory("fish-labels-") as staging:
            texture, source = staging / TEXTURE.name, staging / SOURCE.name
            texture.write_bytes(raw)
            source.write_text(manifest, encoding="utf8")
            publish_files([(texture, TEXTURE), (source, SOURCE)])
        meta()
    print("WAREHOUSE FISH LABEL DRAWINGS OK: one 512px RGB atlas, large high-contrast pictures and no written text")


if __name__ == "__main__":
    main()
