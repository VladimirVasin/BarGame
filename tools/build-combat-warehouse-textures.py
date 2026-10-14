#!/usr/bin/env python3
"""Publish warehouse bases, Blender UV bakes, packed masks and fixed decals."""
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
ATLAS_NAMES = ("WarehouseMechanismsAtlas", "WarehouseFurnishingsAtlas", "WarehouseCargoAtlas")
GPU_BUDGET_BYTES = 8 * 1024 * 1024


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


def packed_macro_mask():
    """Periodic low frequency channel data, rather than an albedo painting.

    R = moisture, G = oxide deposits, B = rubbed transport patches. The
    harmonics are fixed and periodic so the large-scale repeated map has
    continuous gradients; all costly construction occurs at publication.
    """
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64) * (2 * math.pi / SIZE)
    channels = []
    modes = (((1, 0, .52, .37), (0, 1, .31, 1.13), (1, 2, .17, 2.17), (3, -1, .10, .53)),
             ((1, 1, .47, 1.67), (2, -1, .28, .23), (0, 3, .16, 2.81), (4, 2, .07, .37)),
             ((2, 0, .45, .83), (0, 2, .29, 2.33), (2, 3, .18, 1.23), (5, -2, .08, 2.73)))
    for channel in modes:
        field = sum(weight * np.sin(xx * x + yy * y + phase) for x, y, weight, phase in channel)
        channels.append(np.clip(.5 + field * .45, 0, 1))
    data = np.rint(np.stack(channels, axis=2) * 255).astype(np.uint8)
    # The last pixel and first pixel are neighbours in a periodic texture.
    assert max(np.abs(data[0].astype(int) - data[-1].astype(int)).max(),
               np.abs(data[:, 0].astype(int) - data[:, -1].astype(int)).max()) <= 8
    return Image.fromarray(data)


def bc7_mip_bytes(size):
    total = 0
    while True:
        total += max(1, (size + 3) // 4) ** 2 * 16
        if size == 1: return total
        size //= 2


def add_detail_sheets(textures, measures):
    uv_manifest = json.loads((OUTPUT.parent / "CombatWarehouse3D.json").read_text(encoding="utf8"))["surfaces"]
    authored = {record["name"]: record["sha256"] for record in uv_manifest["atlases"]}
    assert set(authored) == set(ATLAS_NAMES)
    for name in ATLAS_NAMES:
        path = SOURCE / "Atlases" / (name + ".source.png")
        source_checksum = hashlib.sha256(path.read_bytes()).hexdigest()
        assert source_checksum == authored[name], "UV manifest and baked source differ: " + name
        original = Image.open(path)
        assert original.size == (1024, 1024), "Blender atlas must retain its authored UV resolution"
        output = original.convert("RGB")
        assert float(np.asarray(output).std()) > 3, "Atlas bake is empty: " + name
        encoded = encode_png(output)
        textures[name] = (encoded, dict(size=1024, clamp=True))
        measures.append(dict(name=name, sha256=hashlib.sha256(encoded).hexdigest(),
                             source_sha256=source_checksum,
                             size=1024, mode="RGB", sRGB=True, wrap="Clamp", authoring="Blender EMIT UV bake"))
    name = "WarehouseMacroMask"
    encoded = encode_png(packed_macro_mask())
    textures[name] = (encoded, dict(linear=True))
    measures.append(dict(name=name, sha256=hashlib.sha256(encoded).hexdigest(), size=SIZE,
                         mode="RGB", sRGB=False, wrap="Repeat", authoring="periodic packed channel data v1",
                         channels=["moisture", "oxide", "rubbed patches"]))
    provenance = json.loads((SOURCE / "wear-generation.json").read_text(encoding="utf8"))
    assert provenance["generator"] == "image_gen (built-in)"
    row = provenance["images"][0]
    assert row["name"] == "WarehouseWearDecals" and row["prompt"] and row["layout_edit"]
    path = ROOT / row["source"]
    assert path.resolve().is_relative_to(SOURCE.resolve())
    raw = path.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == row["sha256"]
    original = Image.open(io.BytesIO(raw))
    assert original.mode == "RGBA" and original.width == original.height
    output = original.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
    alpha = np.asarray(output)[:, :, 3]
    assert alpha.min() == 0 and alpha.max() == 255
    assert max(alpha[SIZE//2-4:SIZE//2+4].max(), alpha[:, SIZE//2-4:SIZE//2+4].max()) < 16, \
        "Decal designs crossed their UV cells"
    for y in range(2):
        for x in range(2):
            cell = alpha[y*SIZE//2:(y+1)*SIZE//2, x*SIZE//2:(x+1)*SIZE//2]
            assert float((cell > 40).mean()) > .015, "A decal cell is empty"
    encoded = encode_png(output)
    textures[row["name"]] = (encoded, dict(clamp=True, alpha=True))
    measures.append(dict(name=row["name"], sha256=hashlib.sha256(encoded).hexdigest(),
                         source_sha256=row["sha256"], size=SIZE, mode="RGBA", sRGB=True,
                         wrap="Clamp", authoring="image_gen fixed transparent 2x2 source"))


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
        textures[row["name"]] = (encoded, {})
        measures.append(dict(name=row["name"], sha256=hashlib.sha256(encoded).hexdigest(),
                             mean_linear_luminance=round(float(luminance.mean()), 6),
                             size=SIZE, mode="RGB", sRGB=True, wrap="Repeat"))
    assert len({row["sha256"] for row in measures}) == len(NAMES)
    add_detail_sheets(textures, measures)
    estimated_gpu_bytes = sum(bc7_mip_bytes(row["size"]) for row in measures)
    assert estimated_gpu_bytes <= GPU_BUDGET_BYTES, "Warehouse texture memory budget exceeded"
    manifest = json.dumps(dict(design_id="warehouse_uv_surfaces_v3", mipmaps=True,
                              windows_compression="BC7", gpu_budget_bytes=GPU_BUDGET_BYTES,
                              estimated_gpu_bytes=estimated_gpu_bytes, sheets=measures), indent=2) + "\n"
    if validate_only:
        for name, (data, _) in textures.items():
            assert (OUTPUT / (name + ".png")).read_bytes() == data, "Stale surface " + name
        assert (OUTPUT / "WarehouseTextures.json").read_text(encoding="utf8") == manifest
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        meta(OUTPUT, True)
        for name, (data, profile) in textures.items():
            path = OUTPUT / (name + ".png")
            path.write_bytes(data)
            meta(path, **profile)
        path = OUTPUT / "WarehouseTextures.json"
        path.write_text(manifest, encoding="utf8")
        meta(path)
    print("WAREHOUSE TEXTURES OK: fixed bases, 3 UV atlases, packed macro data and RGBA decals; "
          f"BC7/mips {estimated_gpu_bytes} bytes <= {GPU_BUDGET_BYTES}")


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
