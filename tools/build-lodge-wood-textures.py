#!/usr/bin/env python3
"""Publish the seven fixed ImageGen lodge wood sources as measured game albedos.

Only deterministic RGB conversion/downsampling occurs here; the drawn originals,
prompts and hashes live in ArtSource/Village/LodgeWood/generation.json.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import uuid

import numpy as np
from PIL import Image
import toolchain

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/Village/LodgeWood"
OUTPUT = ROOT / "Assets/Resources/Village/Textures/LodgeWood"
SIZE = 512
ROLES = ("LodgeFloorWood", "LodgeHatchWood", "LodgePaintedWood", "LodgeOakWood",
         "LodgePaleWood", "LodgeDarkWood", "LodgeRoughWood")
PITCHES = (2.4, 1.2, .6, 1.6, 1.6, 1.25, 1.8)


def meta(path: Path, folder: bool = False):
    sidecar = Path(str(path) + ".meta")
    if sidecar.exists():
        return
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/" + path.relative_to(ROOT).as_posix()).hex
    body = f"fileFormatVersion: 2\nguid: {guid}\n"
    if folder:
        body += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    elif path.suffix == ".png":
        template = (ROOT / "Assets/Resources/Textures/CityParkTimberAlbedo.png.meta").read_text(encoding="utf-8")
        lines = template.splitlines()
        lines[1] = "guid: " + guid
        body = "\n".join(lines) + "\n"
    else:
        body += "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    sidecar.write_text(body, encoding="utf-8")


def linear(values):
    values = values.astype(np.float64) / 255
    return np.where(values <= .04045, values / 12.92, ((values + .055) / 1.055) ** 2.4)


def build(validate_only=False):
    provenance = json.loads((SOURCE / "generation.json").read_text(encoding="utf-8"))
    assert provenance["generator"] == "image_gen (built-in)"
    assert tuple(row["name"] for row in provenance["images"]) == ROLES
    images, rows = {}, []
    for row, pitch in zip(provenance["images"], PITCHES):
        name = row["name"]
        path = ROOT / row["source"]
        assert path.resolve().is_relative_to(SOURCE.resolve()), "Source outside lodge source folder"
        raw = path.read_bytes()
        assert hashlib.sha256(raw).hexdigest() == row["sha256"], "Changed source: " + name
        assert row["prompt"] and row["grain_axis"] == "V"
        original = Image.open(io.BytesIO(raw))
        assert original.width == original.height and original.width >= SIZE
        image = original.convert("RGB").resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        pixels = np.asarray(image)
        assert float(pixels.std()) > 5, "Missing drawn wood character: " + name
        luminance = linear(pixels) @ np.array([.2126, .7152, .0722])
        mean = float(luminance.mean())
        assert .035 < mean < .7, "Albedo loses the aged matte range: " + name
        stream = io.BytesIO()
        image.save(stream, format="PNG", optimize=False)
        encoded = stream.getvalue()
        images[name] = encoded
        rows.append(dict(name=name, meters_per_tile=pitch, grain_axis="V",
                         mean_linear_luminance=round(mean, 6),
                         sha256=hashlib.sha256(encoded).hexdigest()))
    means = {row["name"]: row["mean_linear_luminance"] for row in rows}
    assert means["LodgeHatchWood"] > means["LodgeFloorWood"] * 1.7, "Hatch must separate by value"
    assert means["LodgePaintedWood"] > means["LodgeFloorWood"] * 1.4, "Chair must separate by value"
    assert len({row["sha256"] for row in rows}) == len(ROLES), "Repeated source texture"
    manifest = json.dumps(dict(design_id="lodge_wood_surfaces_v1", texture_size=SIZE,
        color_mode="RGB", wrap_mode="Repeat", mipmaps=True, sheets=rows), indent=2) + "\n"
    if validate_only:
        for name, encoded in images.items():
            assert (OUTPUT / (name + ".png")).read_bytes() == encoded, "Stale game albedo: " + name
        assert (OUTPUT / "LodgeWoodTextures.json").read_text(encoding="utf-8") == manifest
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        meta(OUTPUT, True)
        for name, encoded in images.items():
            target = OUTPUT / (name + ".png")
            target.write_bytes(encoded)
            # Import settings are owned by VillageExpansionModelImporter.
            meta(target)
        target = OUTPUT / "LodgeWoodTextures.json"
        target.write_text(manifest, encoding="utf-8")
        meta(target)
    print("LODGE WOOD TEXTURES OK: fixed sources, deterministic RGB maps, distinct roles and hatch/chair value separation")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    toolchain.check_python(toolchain.load_config(), ("Pillow", "numpy"))
    build(args.validate_only)
