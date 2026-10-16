#!/usr/bin/env python3
"""Publish one unique warehouse painting, registered response and fixed cutouts."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import re
import sys
import uuid

sys.dont_write_bytecode = True
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import toolchain

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/CombatWarehouse/Textures"
OUTPUT = ROOT / "Assets/Resources/CombatWarehouse/Textures"
NAMES = tuple("Warehouse" + name for name in
              ("Concrete", "Paint", "Steel", "Rust", "Timber", "Cardboard", "Rubber"))
SIZE = 512
ATLAS_NAMES = ("WarehouseMasterAtlas", "WarehouseMaterialResponse")
MASTER_SIZE = 4096
RESPONSE_SIZE = 2048
GPU_BUDGET_BYTES = 28 * 1024 * 1024


def prepare_room_numbers():
    """Print the project's ordinary typeface onto enamel, never digit meshes.

    These small authoring images are baked into the existing furnishings atlas;
    no extra runtime texture, material instance or live text surface is needed.
    """
    font_path = ROOT / "Assets/Resources/Fonts/Roboto-Regular.ttf"
    font = ImageFont.truetype(str(font_path), 232)
    directory = SOURCE / "RoomNumbers"
    directory.mkdir(parents=True, exist_ok=True)
    records = []
    for number in range(1, 10):
        image = Image.new("RGB", (256, 288), (191, 185, 158))
        draw = ImageDraw.Draw(image)
        # A quiet worn border leaves the numeral itself intact at PS1 resolution.
        draw.rectangle((4, 4, 251, 283), outline=(146, 143, 122), width=3)
        text = str(number)
        left, top, right, bottom = draw.textbbox((0, 0), text, font=font)
        position = ((256 - (right - left)) / 2 - left,
                    (288 - (bottom - top)) / 2 - top)
        draw.text(position, text, font=font, fill=(18, 23, 21))
        raw = encode_png(image)
        path = directory / f"RoomNumber{number}.png"
        path.write_bytes(raw)
        records.append({"number": number, "file": path.name,
                        "sha256": hashlib.sha256(raw).hexdigest()})
    manifest = {"font": "Assets/Resources/Fonts/Roboto-Regular.ttf",
                "font_sha256": hashlib.sha256(font_path.read_bytes()).hexdigest(),
                "size": [256, 288], "numbers": records}
    (directory / "RoomNumbers.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf8")
    print("WAREHOUSE ROOM NUMBERS PREPARED: Roboto texture numerals 1-9 for the shared furnishings atlas")


def meta(path, folder=False, size=SIZE, linear=False, clamp=False, alpha=False):
    sidecar = Path(str(path) + ".meta")
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/" + path.relative_to(ROOT).as_posix()).hex
    if path.suffix == ".png":
        template = sidecar.read_text(encoding="utf8") if sidecar.exists() else (
            ROOT / "Assets/Resources/City/Port/Textures/PortConcreteAlbedo.png.meta").read_text(encoding="utf8")
        lines = template.splitlines()
        if not sidecar.exists():
            lines[1] = "guid: " + guid
        body = "\n".join(lines) + "\n"
        body = re.sub(r"(?m)^(\s*maxTextureSize:) \d+$", lambda match: match[1] + " " + str(size), body)
        for key, value in (("sRGBTexture", int(not linear)), ("linearTexture", int(linear)),
                           ("wrapU", int(clamp)), ("wrapV", int(clamp)), ("wrapW", int(clamp)),
                           ("alphaIsTransparency", int(alpha)), ("mipMapsPreserveCoverage", int(alpha))):
            body = re.sub(r"(?m)^(\s*" + key + r":) \d+$", lambda match: match[1] + " " + str(value), body)
        body = re.sub(r"(?m)^(\s*alphaTestReferenceValue:) [\d.]+$",
                      lambda match: match[1] + (" 0.16" if alpha else " 0.5"), body)
    else:
        if sidecar.exists():
            return
        body = "fileFormatVersion: 2\nguid: " + guid + "\n"
        if folder:
            body += "folderAsset: yes\n"
        body += "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    sidecar.write_text("\n".join(line.rstrip() for line in body.splitlines()) + "\n", encoding="utf8", newline="\n")


def encode_png(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=False)
    return buffer.getvalue()


def bc7_mip_bytes(size):
    total = 0
    while True:
        total += max(1, (size + 3) // 4) ** 2 * 16
        if size == 1: return total
        size //= 2


def build(validate_only):
    manifest_source = json.loads((OUTPUT.parent / "CombatWarehouse3D.json").read_text(encoding="utf8"))["surfaces"]
    authored = {row["name"]: row for row in manifest_source["atlases"]}
    if set(authored) != set(ATLAS_NAMES) or manifest_source["size"] != MASTER_SIZE or manifest_source["tiled_models"]:
        raise RuntimeError("Republish the unique master UV/physical bake before its runtime textures")
    textures, measures = {}, []
    for name in ATLAS_NAMES:
        path = SOURCE / "Atlases" / (name + ".source.png")
        raw = path.read_bytes()
        checksum = hashlib.sha256(raw).hexdigest()
        assert checksum == authored[name]["sha256"], "Changed registered warehouse bake " + name
        linear = name == "WarehouseMaterialResponse"
        size = RESPONSE_SIZE if linear else MASTER_SIZE
        image = Image.open(io.BytesIO(raw)).convert("RGBA" if linear else "RGB")
        assert image.size == (size, size)
        assert np.asarray(image)[..., :3].std() > 3, "Empty master bake"
        encoded = encode_png(image)
        textures[name] = (encoded, dict(size=size, clamp=True, linear=linear))
        measures.append(dict(name=name, sha256=hashlib.sha256(encoded).hexdigest(), source_sha256=checksum,
            size=size, mode=image.mode, sRGB=not linear, wrap="Clamp",
            authoring="registered unique placement master bake v5",
            channels=["height slope U", "height slope V", "roughness", "cavity"] if linear else ["albedo R", "albedo G", "albedo B"]))
    provenance = json.loads((SOURCE / "wear-generation.json").read_text(encoding="utf8"))
    row = provenance["images"][0]
    assert provenance["generator"] == "image_gen (built-in)" and row["name"] == "WarehouseWearDecals"
    raw = (ROOT / row["source"]).read_bytes()
    assert hashlib.sha256(raw).hexdigest() == row["sha256"]
    image = Image.open(io.BytesIO(raw)).convert("RGBA").resize((SIZE, SIZE), Image.Resampling.LANCZOS)
    encoded = encode_png(image)
    textures[row["name"]] = (encoded, dict(clamp=True, alpha=True))
    measures.append(dict(name=row["name"], sha256=hashlib.sha256(encoded).hexdigest(),
        source_sha256=row["sha256"], size=SIZE, mode="RGBA", sRGB=True, wrap="Clamp", authoring="unchanged authored sparse wear cutouts"))
    label_bytes = (OUTPUT / "WarehouseFishLabels.png").read_bytes()
    label = Image.open(io.BytesIO(label_bytes))
    assert label.size == (SIZE, SIZE)
    measures.append(dict(name="WarehouseFishLabels", sha256=hashlib.sha256(label_bytes).hexdigest(),
        size=SIZE, mode=label.mode, sRGB=True, wrap="Clamp", authoring="accounted external fish-label drawing"))
    estimated = sum(bc7_mip_bytes(row["size"]) for row in measures)
    assert estimated <= GPU_BUDGET_BYTES
    manifest = json.dumps(dict(design_id="warehouse_unique_surface_v5", mipmaps=True,
        windows_compression="BC7", gpu_budget_bytes=GPU_BUDGET_BYTES, estimated_gpu_bytes=estimated,
        sheets=measures), indent=2) + "\n"
    if validate_only:
        for name, (data, _) in textures.items():
            assert (OUTPUT / (name + ".png")).read_bytes() == data, "Stale warehouse map " + name
        assert (OUTPUT / "WarehouseTextures.json").read_text(encoding="utf8") == manifest
    else:
        for name, (data, profile) in textures.items():
            path = OUTPUT / (name + ".png")
            path.write_bytes(data)
            meta(path, **profile)
        path = OUTPUT / "WarehouseTextures.json"
        path.write_text(manifest, encoding="utf8")
        meta(path)
        # Old maps remain preserved as authoring sources. Remove obsolete
        # published resources so a packaging/load cannot retain the old bank.
        obsolete = (*NAMES, "WarehouseMechanismsAtlas", "WarehouseFurnishingsAtlas", "WarehouseCargoAtlas", "WarehouseMacroMask")
        for name in obsolete:
            for suffix in (".png", ".png.meta"):
                path = OUTPUT / (name + suffix)
                if path.exists():
                    path.unlink()
    print(f"WAREHOUSE MASTER TEXTURES OK: {MASTER_SIZE} albedo / {RESPONSE_SIZE} unique response / two {SIZE} detail sheets; BC7+mips {estimated} <= {GPU_BUDGET_BYTES}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--prepare-signs", action="store_true", help="Prepare only the deterministic number bake sources")
    options = parser.parse_args()
    toolchain.check_python(toolchain.load_config(), ("Pillow", "numpy"))
    if options.prepare_signs:
        prepare_room_numbers()
    else:
        build(options.validate_only)
