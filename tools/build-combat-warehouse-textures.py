#!/usr/bin/env python3
"""Publish fixed ImageGen sources as deterministic warehouse-only RGB albedos."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import sys
import uuid

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image
import toolchain

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/CombatWarehouse/Textures"
OUTPUT = ROOT / "Assets/Resources/CombatWarehouse/Textures"
NAMES = tuple("Warehouse" + name for name in
              ("Concrete", "Paint", "Steel", "Rust", "Timber", "Cardboard", "Rubber"))
SIZE = 512


def meta(path, folder=False):
    sidecar = Path(str(path) + ".meta")
    if sidecar.exists():
        return
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/" + path.relative_to(ROOT).as_posix()).hex
    if path.suffix == ".png":
        template = (ROOT / "Assets/Resources/City/Port/Textures/PortConcreteAlbedo.png.meta").read_text(encoding="utf8")
        lines = template.splitlines()
        lines[1] = "guid: " + guid
        body = "\n".join(lines) + "\n"
    else:
        body = "fileFormatVersion: 2\nguid: " + guid + "\n"
        if folder:
            body += "folderAsset: yes\n"
        body += "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    sidecar.write_text(body, encoding="utf8")


def build(validate_only):
    provenance = json.loads((SOURCE / "generation.json").read_text(encoding="utf8"))
    assert provenance["generator"] == "image_gen (built-in)"
    assert tuple(row["name"] for row in provenance["images"]) == NAMES
    textures = {}
    measures = []
    for row in provenance["images"]:
        path = ROOT / row["source"]
        assert path.resolve().is_relative_to(SOURCE.resolve())
        raw = path.read_bytes()
        assert hashlib.sha256(raw).hexdigest() == row["sha256"], "Changed source " + row["name"]
        assert row["prompt"]
        original = Image.open(io.BytesIO(raw))
        assert original.width == original.height and original.width >= SIZE
        # Asset publication only: keep the drawing intact, convert and downsample.
        output = original.convert("RGB").resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        values = np.asarray(output).astype(np.float64) / 255
        linear = np.where(values <= .04045, values / 12.92, ((values + .055) / 1.055) ** 2.4)
        luminance = linear @ np.array([.2126, .7152, .0722])
        assert .002 < float(luminance.mean()) < .65, "Lost matte albedo range"
        assert float(values.std()) > .015, "Empty surface"
        buffer = io.BytesIO()
        output.save(buffer, format="PNG", optimize=False)
        encoded = buffer.getvalue()
        textures[row["name"]] = encoded
        measures.append(dict(name=row["name"], sha256=hashlib.sha256(encoded).hexdigest(),
                             mean_linear_luminance=round(float(luminance.mean()), 6)))
    assert len({row["sha256"] for row in measures}) == len(NAMES)
    manifest = json.dumps(dict(design_id="warehouse_surfaces_v2", size=SIZE, mode="RGB",
                              mipmaps=True, wrap="Repeat", windows_compression="BC7", sheets=measures), indent=2) + "\n"
    if validate_only:
        for name, data in textures.items():
            assert (OUTPUT / (name + ".png")).read_bytes() == data, "Stale surface " + name
        assert (OUTPUT / "WarehouseTextures.json").read_text(encoding="utf8") == manifest
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        meta(OUTPUT, True)
        for name, data in textures.items():
            path = OUTPUT / (name + ".png")
            path.write_bytes(data)
            meta(path)
        path = OUTPUT / "WarehouseTextures.json"
        path.write_text(manifest, encoding="utf8")
        meta(path)
    print("WAREHOUSE TEXTURES OK: fixed sources, distinct deterministic 512 RGB surfaces, BC7/mips/repeat")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    options = parser.parse_args()
    toolchain.check_python(toolchain.load_config(), ("Pillow", "numpy"))
    build(options.validate_only)
