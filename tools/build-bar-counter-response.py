#!/usr/bin/env python3
"""Publish two deterministic physical roughness fields for the existing bar counter.

Albedo is never read. RG is neutral height slope, B is independent roughness,
A is neutral cavity. Periodic fields retain the authored metric UVs and mean
smoothness: rubbed lacquer and uneven old brass, without painted lighting,
new stains, geometry or another reflection capture.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import uuid

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Bar/Response"
MANIFEST = ROOT / "ArtSource/Bar/bar-counter-response.json"
SIZE = 512
SHEETS = (
    ("BarCounterWoodResponse", .66, .75, ("Counter Top", "Counter Return Top")),
    ("BarCounterBrassResponse", .58, .42, (
        "Counter Foot Rail", "Counter Return Foot Rail", "Beer Tap Stem 1", "Beer Tap Spout 1",
        "Beer Tap Stem 2", "Beer Tap Spout 2", "Beer Tap Stem 3", "Beer Tap Spout 3")),
)


def field(name: str) -> np.ndarray:
    u, v = np.meshgrid((np.arange(SIZE, dtype=np.float64) + .5) / SIZE,
                       (np.arange(SIZE, dtype=np.float64) + .5) / SIZE)
    phase = 2 * np.pi
    if name == "BarCounterWoodResponse":
        # Long rubbed bands and fine grain are independent physical variation,
        # rather than a contrast copy of the coloured timber sheet.
        roughness = .66 + .12 * np.sin(phase * (2 * u + v)) + \
            .06 * np.sin(phase * 6 * u) * np.sin(phase * 14 * v) + \
            .035 * np.sin(phase * (8 * u + 31 * v))
    else:
        # Uneven burnishing and fine machining retain a broad old-metal response.
        roughness = .58 + .11 * np.sin(phase * (2 * u + 3 * v)) + \
            .055 * np.cos(phase * (13 * u - v)) + .04 * np.sin(phase * (33 * u + 5 * v))
    result = np.empty((SIZE, SIZE, 4), dtype=np.float64)
    result[..., :2] = .5
    result[..., 2] = roughness
    result[..., 3] = 1
    return np.uint8(np.clip(result[::-1] * 255 + .5, 0, 255))


def png(pixels: np.ndarray) -> bytes:
    stream = io.BytesIO()
    Image.fromarray(pixels).save(stream, format="PNG", compress_level=9)
    return stream.getvalue()


def guid(path: Path) -> str:
    return uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/" + path.relative_to(ROOT).as_posix()).hex


def texture_meta(path: Path) -> bytes:
    # Reuse the project's complete importer schema, while keeping the bar's
    # physical data outside the legacy albedo-only Bar/Textures processor.
    source = ROOT / "Assets/Resources/Bar/Textures/BarPolishedWoodAlbedo.png.meta"
    text = source.read_text(encoding="utf8")
    text = re.sub(r"(?m)^guid: \w+$", "guid: " + guid(path), text)
    for key, value in (("maxTextureSize", SIZE), ("enableMipMap", 1), ("sRGBTexture", 0),
                       ("linearTexture", 1), ("isReadable", 0), ("streamingMipmaps", 0),
                       ("filterMode", 2), ("aniso", 4), ("wrapU", 0), ("wrapV", 0), ("wrapW", 0),
                       ("textureCompression", 2), ("alphaUsage", 1), ("alphaIsTransparency", 0),
                       ("ignoreMipmapLimit", 1), ("crunchedCompression", 0)):
        text = re.sub(r"(?m)^(\s*" + key + r":) -?\d+$", lambda match: match[1] + " " + str(value), text)
    blocks = text.split("  - serializedVersion:")
    for index in range(1, len(blocks)):
        if "buildTarget: Standalone" not in blocks[index]:
            continue
        blocks[index] = re.sub(r"(?m)^(\s*textureFormat:) -?\d+$", r"\g<1> 25", blocks[index])
        blocks[index] = re.sub(r"(?m)^(\s*overridden:) \d+$", r"\g<1> 1", blocks[index])
    return ("\n".join(line.rstrip() for line in "  - serializedVersion:".join(blocks).splitlines()) + "\n").encode()


def write_or_check(path: Path, data: bytes, check: bool) -> None:
    if check:
        if not path.exists() or path.read_bytes() != data:
            raise ValueError("Stale bar physical response: " + str(path.relative_to(ROOT)))
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Validate committed outputs without writing.")
    args = parser.parse_args()
    records = []
    total_bytes = 0
    for name, mean, pitch, receivers in SHEETS:
        pixels = field(name)
        roughness = pixels[..., 2].astype(np.float64) / 255
        if not np.all(pixels[..., :2] == 128) or not np.all(pixels[..., 3] == 255):
            raise ValueError("Non-neutral relief/cavity in roughness-only pilot.")
        if abs(float(roughness.mean()) - mean) > .002 or not .30 < roughness.min() < roughness.max() < .90:
            raise ValueError("Bar physical range or retained mean drifted.")
        # Paired edge steps must be no worse than ordinary adjacent texels.
        seam = max(np.max(abs(roughness[:, 0] - roughness[:, -1])), np.max(abs(roughness[0] - roughness[-1])))
        interior = max(np.max(abs(np.diff(roughness, axis=0))), np.max(abs(np.diff(roughness, axis=1))))
        if seam > interior + .005:
            raise ValueError("Bar physical field is not periodic.")
        data = png(pixels)
        path = OUTPUT / (name + ".png")
        write_or_check(path, data, args.check)
        write_or_check(Path(str(path) + ".meta"), texture_meta(path), args.check)
        width = SIZE
        texture_bytes = 0
        while True:
            texture_bytes += ((width + 3) // 4) ** 2 * 16
            if width == 1:
                break
            width = max(1, width // 2)
        total_bytes += texture_bytes
        records.append({"resource": "Bar/Response/" + name, "size": SIZE, "linear": True,
                        "meters_per_tile": pitch, "roughness_min": round(float(roughness.min()), 6),
                        "roughness_mean": round(float(roughness.mean()), 6),
                        "roughness_max": round(float(roughness.max()), 6), "receiver_names": receivers,
                        "sha256": hashlib.sha256(data).hexdigest(), "bc7_bytes_with_mips": texture_bytes})
    if total_bytes > 768 * 1024:
        raise ValueError("Bar response maps exceed their bounded texture budget.")
    manifest = {"design_id": "bar_counter_physical_response_v1", "generator_version": "1.0.0",
                "channels": {"RG": "neutral signed metre slope", "B": "independent roughness", "A": "neutral cavity"},
                "albedo_read": False, "normal_strength": 0, "response_strength": 1,
                "maximum_shared_materials": 1, "gpu_budget_bytes": 768 * 1024,
                "estimated_bc7_bytes_with_mips": total_bytes, "sheets": records}
    write_or_check(MANIFEST, (json.dumps(manifest, indent=2) + "\n").encode(), args.check)
    folder = ("fileFormatVersion: 2\nguid: " + guid(OUTPUT) + "\nfolderAsset: yes\nDefaultImporter:\n"
              "  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    write_or_check(Path(str(OUTPUT) + ".meta"), folder.encode(), args.check)
    print(("Validated" if args.check else "Published") + " bar counter physical response: two linear maps, one shared material.")


if __name__ == "__main__":
    main()
