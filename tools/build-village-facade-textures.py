#!/usr/bin/env python3
"""Quiet, detailed neutral albedos for the village's existing surface families.

House geometry owns log courses, masonry joints, openings and repairs. These
repeatable sheets supply wood fibres, small checks, mineral grain and worn
shingles. They contain no illumination, windows, signs, snow or story detail.
The runtime reads the measured linear mean and compensates the caller's tint.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import uuid

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

import toolchain

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Village/Textures"
SOURCE = ROOT / "ArtSource/Village/FacadeTextures"
SIZE = 1024
DESIGN = "village_facade_surfaces_v1"
VERSION = "1.1.0"
MEAN_TARGET = .58
LINEAR = np.array([v / 255 / 12.92 if v / 255 <= .04045 else
                   ((v / 255 + .055) / 1.055) ** 2.4 for v in range(256)])
SPECS = (("VillageTimberAlbedo", "Timber", 1.4, "U"),
         ("VillageJoineryAlbedo", "Timber", 1.4, "V"),
         ("VillageStoneAlbedo", "LayeredStone", 2.4, "none"),
         ("VillageRoofAlbedo", "Timber", 2.4, "U"),
         ("VillagePlasterAlbedo", "Masonry", 2.4, "none"))


def noise(seed, width, height):
    """Periodic, low-amplitude material grain with no broad random stains."""
    values = np.random.default_rng(seed).integers(72, 185, (height + 1, width + 1), dtype=np.uint8)
    values[-1] = values[0]
    values[:, -1] = values[:, 0]
    layer = Image.fromarray(values).resize((SIZE, SIZE), Image.Resampling.BICUBIC)
    return (np.array(layer, dtype=np.float32) - 128) / 56


def wrapped_line(draw, points, fill, width=1):
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            draw.line([(x + dx, y + dy) for x, y in points], fill=int(fill), width=width)


def wrapped_polygon(draw, points, fill):
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            draw.polygon([(x + dx, y + dy) for x, y in points], fill=int(fill))


def timber():
    y, x = np.mgrid[0:SIZE, 0:SIZE] / SIZE
    # Several widths of long grain, including 4-8 cm weathered furrows that
    # survive the actual 64/128-pixel lane view. Sub-pixel grain alone vanished
    # completely under PS1 quantisation in the first in-world capture.
    field = np.full((SIZE, SIZE), 202.0, dtype=np.float32)
    for frequency, amplitude, bend, phase in ((64, 12.0, .38, .7), (23, 21.0, .29, 1.4),
                                              (137, 4.5, .38, 2.3), (7, 2.5, .11, 3.1)):
        field += amplitude * np.sin(math.tau * (frequency * y +
            bend * np.sin(math.tau * x * 2 + phase) + phase))
    field += noise(601, 16, 180) * 6.8 + noise(602, 320, 320) * 2.2
    image = Image.fromarray(np.clip(field, 0, 255).astype(np.uint8))
    draw = ImageDraw.Draw(image)
    rng = np.random.default_rng(603)
    for index in range(58):
        start = int(rng.integers(0, SIZE))
        level = int(rng.integers(0, SIZE))
        length = int(rng.integers(42, 240))
        points = [(start + step, level + math.sin(step / 43 + index) * 3.0)
                  for step in range(0, length, 5)]
        wrapped_line(draw, points, int(rng.integers(136, 169)), int(rng.integers(3, 9)))
        if index % 3 == 0:
            wrapped_line(draw, points[2:-3], 122 + index % 11, 2)
    # Rare exposed slivers have irregular pointed ends instead of random
    # circular stains; they run with the wood rather than crossing it.
    for index in range(21):
        cx, cy = int(rng.integers(0, SIZE)), int(rng.integers(0, SIZE))
        length, width = int(rng.integers(34, 85)), int(rng.integers(5, 11))
        wrapped_polygon(draw, [(cx - length, cy), (cx - length * .35, cy - width),
            (cx + length * .68, cy - width * .45), (cx + length, cy + 2),
            (cx + length * .25, cy + width * .35)], 222 + index % 13)
    # Seven readable knots, mostly 4-8 cm across, with broken growth rings.
    for index in range(7):
        cx, cy = int(rng.integers(0, SIZE)), int(rng.integers(0, SIZE))
        for radius in (1.0, 1.8, 2.7):
            points = [(cx + math.cos(a) * 15 * radius, cy + math.sin(a) * 6.5 * radius)
                      for a in np.linspace(0, math.tau, 33)]
            wrapped_line(draw, points, 142 + radius * 8, 3)
        wrapped_line(draw, [(cx - 10, cy), (cx + 10, cy)], 125, 7)
    return image


def stone():
    field = 202 + noise(611, 60, 72) * 24 + noise(612, 180, 160) * 8.0
    field += noise(613, 450, 450) * 2.4 + noise(614, 18, 24) * 6.0
    image = Image.fromarray(np.clip(field, 0, 255).astype(np.uint8))
    draw = ImageDraw.Draw(image)
    rng = np.random.default_rng(615)
    # Mineral inclusions and pores live at several readable sizes; the 6-18px
    # chipped aggregate provides a middle scale without another masonry grid.
    for index in range(1200):
        x, y = (int(value) for value in rng.integers(0, SIZE, 2))
        width = int(rng.integers(2, 9))
        wrapped_line(draw, [(x, y), (x + width, y + 1)], int(rng.integers(147, 194)),
                     int(rng.integers(1, 5)))
    for index in range(180):
        x, y = (int(value) for value in rng.integers(0, SIZE, 2))
        size = int(rng.integers(7, 20))
        wrapped_polygon(draw, [(x - size, y), (x - size * .25, y - size * .55),
            (x + size * .70, y - size * .24), (x + size, y + size * .45),
            (x - size * .28, y + size * .66)], 224 + index % 15)
        if index % 3 == 0:
            wrapped_polygon(draw, [(x - size, y), (x - size * .25, y - size * .55),
                (x + size * .10, y), (x - size * .22, y + size * .20)], 146 + index % 21)
    for index in range(34):
        x, y = (int(value) for value in rng.integers(0, SIZE, 2))
        points = [(x + step * 5, y + step * .9 + math.sin(step * 1.8 + index) * 2)
                  for step in range(int(rng.integers(3, 12)))]
        wrapped_line(draw, points, 145 + index % 16, 3)
    return image


def plaster():
    field = 202 + noise(621, 150, 150) * 8.0 + noise(622, 42, 50) * 6.0
    field += noise(623, 8, 9) * 2.2
    image = Image.fromarray(np.clip(field, 0, 255).astype(np.uint8))
    draw = ImageDraw.Draw(image)
    rng = np.random.default_rng(624)
    for index in range(260):
        x, y = (int(value) for value in rng.integers(0, SIZE, 2))
        length = int(rng.integers(3, 25))
        wrapped_line(draw, [(x, y), (x + length, y + index % 3)], 176 + index % 19, 2)
    return image


def roof(wood):
    image = wood.copy()
    draw = ImageDraw.Draw(image)
    rng = np.random.default_rng(631)
    # The U direction crosses the roof slope. Course joints are narrow and
    # restrained; the existing roof shell supplies silhouette and thickness.
    for course in range(8):
        x = course * 128
        wrapped_line(draw, [(x, 0), (x, SIZE)], 136, 5)
        for shingle in range(14):
            y = shingle * SIZE / 14 + (course % 2) * SIZE / 28
            y += int(rng.integers(-4, 5))
            wrapped_line(draw, [(x + 3, y), (x + 126, y + 2)], 151, 3)
            # Sparse splitting follows the wood, short of the next course.
            if (course * 3 + shingle) % 9 == 0:
                wrapped_line(draw, [(x + 22, y + 13), (x + 79, y + 15)], 139, 4)
    return image


def finish(image):
    """Neutral RGB, measured mean, and byte-identical repeat edges."""
    array = np.array(image, dtype=np.float64)
    low, high = -24.0, 24.0
    for _ in range(22):
        offset = (low + high) * .5
        candidate = np.clip(np.rint(array + offset), 0, 255).astype(np.uint8)
        if float(LINEAR[candidate].mean()) < MEAN_TARGET:
            low = offset
        else:
            high = offset
    candidate[:, -1] = candidate[:, 0]
    candidate[-1, :] = candidate[0, :]
    return Image.fromarray(candidate).convert("RGB")


def build_all():
    wood = timber()
    images = (wood, wood.transpose(Image.Transpose.TRANSPOSE), stone(), roof(wood), plaster())
    return {spec[0]: finish(image) for spec, image in zip(SPECS, images)}


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=False, compress_level=9)
    return buffer.getvalue()


def measure(name, image):
    array = np.array(image)
    grey = array[:, :, 0]
    if image.size != (SIZE, SIZE) or image.mode != "RGB":
        raise ValueError(f"{name}: wrong dimensions/format")
    if not np.array_equal(array[:, :, 0], array[:, :, 1]) or not np.array_equal(array[:, :, 0], array[:, :, 2]):
        raise ValueError(f"{name}: albedo carries unwanted colour tint")
    if not np.array_equal(array[0], array[-1]) or not np.array_equal(array[:, 0], array[:, -1]):
        raise ValueError(f"{name}: repeat edges do not match")
    mean = float(LINEAR[grey].mean())
    low, high = (float(value) for value in np.percentile(grey, [5, 95]))
    macro = np.asarray(image.convert("L").resize((32, 32), Image.Resampling.BOX), dtype=float)
    reduced = np.asarray(image.convert("L").resize((128, 128), Image.Resampling.BOX), dtype=float)
    near = np.asarray(image.convert("L").resize((64, 64), Image.Resampling.BOX), dtype=float)
    is_plaster = "Plaster" in name
    minimum_contrast = 10 if is_plaster else 40
    minimum_read = 2 if is_plaster else 8
    if (abs(mean - MEAN_TARGET) > .006 or not minimum_contrast <= high - low <= 98 or
            macro.std() > 8 or reduced.std() < minimum_read or near.std() < minimum_read * .60):
        raise ValueError(f"{name}: mean/contrast/macroscopic quietness drift ({mean}, {low}/{high}, {macro.std()})")
    return {"mean_linear_luminance": round(mean, 8), "percentile_5": low,
            "percentile_95": high, "macro_stddev": round(float(macro.std()), 4),
            "read_128_stddev": round(float(reduced.std()), 4),
            "read_64_stddev": round(float(near.std()), 4)}


def write_meta(path, folder=False):
    sidecar = Path(str(path) + ".meta")
    if sidecar.exists():
        return
    relative = str(path.relative_to(ROOT)).replace("\\", "/")
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "barpromenade:" + relative).hex
    text = f"fileFormatVersion: 2\nguid: {guid}\n"
    if folder:
        text += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
    elif path.suffix == ".png":
        template = (ROOT / "Assets/Resources/Textures/CityParkTimberAlbedo.png.meta").read_text(encoding="utf-8")
        lines = template.splitlines()
        lines[1] = "guid: " + guid
        text = "\n".join(lines).replace("maxTextureSize: 512", "maxTextureSize: 1024") + "\n"
    else:
        text += "TextScriptImporter:\n  externalObjects: {}\n"
    sidecar.write_text(text, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    toolchain.check_python(toolchain.load_config(), ("Pillow", "numpy"))
    built = build_all()
    repeated = build_all()
    records = []
    for name, surface, pitch, grain in SPECS:
        image = built[name]
        if image.tobytes() != repeated[name].tobytes():
            raise ValueError(f"{name}: non-deterministic pixels")
        raw = png_bytes(image)
        record = {"name": name, "surface": surface, "meters_per_tile": pitch,
                  "grain_axis": grain, "sha256": hashlib.sha256(raw).hexdigest(), **measure(name, image)}
        records.append(record)
        path = OUTPUT / (name + ".png")
        if args.validate_only:
            if not path.is_file() or path.read_bytes() != raw:
                raise ValueError(f"{name}: packaged PNG differs from deterministic output")
        else:
            OUTPUT.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
            write_meta(path)
        print(f"{name}: linear mean={record['mean_linear_luminance']:.6f}, macro std={record['macro_stddev']:.3f}")
    signature = hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    manifest = {"design_id": DESIGN, "generator_version": VERSION, "build_signature": signature, "texture_size": SIZE,
                "grayscale": True, "mipmaps": True, "wrap_mode": "Repeat", "sheets": records}
    manifest_path = OUTPUT / "VillageFacadeTextures.json"
    encoded = json.dumps(manifest, indent=2) + "\n"
    if args.validate_only:
        if manifest_path.read_text(encoding="utf-8") != encoded:
            raise ValueError("Village facade manifest differs from measured output")
    else:
        manifest_path.write_text(encoded, encoding="utf-8")
        write_meta(manifest_path)
        write_meta(OUTPUT, folder=True)
        SOURCE.mkdir(parents=True, exist_ok=True)
        contact = Image.new("RGB", (5 * 328, 360), (48, 50, 51))
        draw = ImageDraw.Draw(contact)
        for index, (name, image) in enumerate(built.items()):
            contact.paste(image.resize((320, 320), Image.Resampling.LANCZOS), (index * 328, 0))
            draw.text((index * 328 + 4, 332), name, fill=(225, 225, 225))
        contact.save(SOURCE / "VillageFacadeContact.png")
    print("VILLAGE FACADE TEXTURES OK: 5 neutral sheets; repeat edges, fine detail, quiet mass, deterministic PNGs")
    print("Build signature: " + signature)


if __name__ == "__main__":
    main()
