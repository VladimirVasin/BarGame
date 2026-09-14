#!/usr/bin/env python3
"""Bake the garden–checkpoint transition from the two existing ground sheets.

Opaque, linear-light blending carries both compensated Unity tints. The 24 m
horizontal repeat is a common multiple of the original 3 m lawn / 8 m soil.
Vertical coordinates are relative to the shared terrain seam. The outer rows
are the unmodified source recipes; Unity partitions the visible terrain rather
than stacking a decal. No new shader or runtime texture is needed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
TEXTURES = ROOT / "Assets/Resources/Textures"
OUTPUT = TEXTURES / "CityEastGroundTransitionAlbedo.png"
MANIFEST = ROOT / "ArtSource/City/EastExit/GroundTransition.json"
SIZE = (2048, 1024)
REPEAT = 24.0
HALF_DEPTH = 4.0
RECIPES = (
    ("CityParkLawnAlbedo.png", 3.0, (0.20, 0.26, 0.17), 1.394),
    ("CityFringeForefieldAlbedo.png", 8.0, (0.30, 0.26, 0.19), 1.4415),
)


def linear(value):
    return np.where(value <= .04045, value / 12.92, ((value + .055) / 1.055) ** 2.4)


def srgb(value):
    return np.where(value <= .0031308, value * 12.92, 1.055 * value ** (1 / 2.4) - .055)


def sample(recipe, x, z):
    name, pitch, tint, compensation = recipe
    # Mirror Unity's 512 px import and bottom-up UV convention.
    source = np.asarray(Image.open(TEXTURES / name).convert("RGB").resize(
        (512, 512), Image.Resampling.LANCZOS), dtype=np.float32)[::-1] / 255.0
    source = linear(source)
    sx, sz = np.mod(x / pitch, 1) * 512 - .5, np.mod(z / pitch, 1) * 512 - .5
    ix, iz = np.floor(sx).astype(int), np.floor(sz).astype(int)
    fx, fz = (sx - ix)[..., None], (sz - iz)[..., None]
    a = source[iz % 512, ix % 512] * (1 - fx) + source[iz % 512, (ix + 1) % 512] * fx
    b = source[(iz + 1) % 512, ix % 512] * (1 - fx) + source[(iz + 1) % 512, (ix + 1) % 512] * fx
    return (a * (1 - fz) + b * fz) * linear(np.asarray(tint) * compensation)


def mask(x, z):
    phase = x * (2 * np.pi / REPEAT)
    center = -.55 + .43 * np.sin(phase) + .24 * np.sin(3 * phase + .7)
    width = 3.7 + .45 * np.sin(2 * phase - .6)
    # A few broad lobes and embedded grass remnants survive low resolution;
    # the outer metre at either side stays precisely its source surface.
    irregular = .20 * np.sin(5 * phase + z * 2.1) + .12 * np.sin(9 * phase - z * 3.4)
    t = np.clip((z - center + irregular) / width + .5, 0, 1)
    return t * t * (3 - 2 * t)


def build():
    x = (np.arange(SIZE[0], dtype=np.float32)[None, :] + .5) / SIZE[0] * REPEAT
    z = (np.arange(SIZE[1], dtype=np.float32)[:, None] + .5) / SIZE[1] * (2 * HALF_DEPTH) - HALF_DEPTH
    weight = mask(x, z)[..., None]
    lawn, soil = (sample(recipe, x, z) for recipe in RECIPES)
    result = lawn * (1 - weight) + soil * weight
    pixels = np.clip(np.rint(srgb(result) * 255), 0, 255).astype(np.uint8)
    assert np.all(weight[:80] == 0) and np.all(weight[-80:] == 1), "Outer edges must exactly retain source recipes"
    probes = np.linspace(-HALF_DEPTH, HALF_DEPTH, 97)
    assert np.max(np.abs(mask(0, probes) - mask(REPEAT, probes))) < 1e-6
    for recipe in RECIPES:
        assert np.max(np.abs(sample(recipe, 0., probes) - sample(recipe, REPEAT, probes))) < 1e-6
    image = Image.fromarray(pixels[::-1])
    record = {"version": 1, "size": list(SIZE), "worldRepeat": REPEAT,
              "halfDepth": HALF_DEPTH, "surface": "opaque PS1 Lit / baked compensated linear-light mix",
              "uvOrigin": "world X; Z relative to church–yard seam",
              "recipes": [{"file": recipe[0], "metersPerTile": recipe[1], "tint": recipe[2],
                           "compensation": recipe[3], "sha256": hashlib.sha256((TEXTURES / recipe[0]).read_bytes()).hexdigest()}
                          for recipe in RECIPES],
              "pixelSha256": hashlib.sha256(image.tobytes()).hexdigest()}
    return image, record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    image, record = build()
    if args.verify:
        assert json.loads(MANIFEST.read_text(encoding="utf-8")) == json.loads(json.dumps(record)), "Stale transition manifest"
        assert Image.open(OUTPUT).convert("RGB").tobytes() == image.tobytes(), "Stale transition texture"
        print("Verified deterministic transition, source hashes, exact outer recipes and horizontal wrapping.")
    else:
        image.save(OUTPUT, compress_level=9)
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print(f"Wrote {OUTPUT.name}: {SIZE[0]}x{SIZE[1]}, 24x8 m; source edges and periodic mask verified.")


if __name__ == "__main__":
    main()
