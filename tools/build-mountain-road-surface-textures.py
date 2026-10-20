#!/usr/bin/env python3
"""Publish road-owned repeat/unique maps with the complete 80 MiB PC budget."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import uuid

import numpy as np
from PIL import Image

import mountain_road_surface_authoring as art

ROOT, OUTPUT = art.ROOT, art.OUTPUT
PREFIX = "MountainRoad/Surfaces/"
MANIFEST = OUTPUT / "MountainRoadSurfaceTextures.json"
_spec = importlib.util.spec_from_file_location("road_private_texture_publisher", ROOT / "tools/build-village-surface-textures.py")
_publisher = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_publisher)
_publisher.art = art
_publisher.OUTPUT = OUTPUT
bc7_bytes, publish, png = _publisher.bc7_bytes, _publisher.publish, _publisher.png


def build(check):
    art.sources()
    _publisher.check_physics()
    rows, extra = [], []
    estimated = 0
    for family, _, pitch, _, full_colour in art.SPECS:
        size = art.size_for(family)
        row = {"family": family, "size": size, "response_size": size // 2,
               "meters_per_tile": pitch, "full_color": full_colour,
               "ground_response": "", "ground_response_sha256": ""}
        for kind, resolution, linear in (("albedo", size, False), ("response", size // 2, True)):
            values = _publisher.render(family, resolution, response=linear)
            if not linear:
                row["mean_linear_luminance"] = round(float(np.mean(np.sum(art.linear(values) *
                    np.asarray((.2126, .7152, .0722)), axis=-1))), 8)
            raw = png(values)
            if linear:
                _publisher.check_packed_slopes(family, raw, resolution)
            name = family + ("Albedo" if kind == "albedo" else "Response")
            row[kind] = PREFIX + name
            row[kind + "_sha256"] = publish(OUTPUT / (name + ".png"), raw, resolution, linear, check)
            estimated += bc7_bytes(resolution)
        if family in art.GROUND:
            name = family + "GroundResponse"
            row["ground_response"] = PREFIX + name
            raw = png(_publisher.render(family, 512, ground=True))
            row["ground_response_sha256"] = publish(OUTPUT / (name + ".png"), raw, 512, True, check)
            estimated += bc7_bytes(512)
        rows.append(row)
        print("Road surface:", family, flush=True)
    for name, size, linear in (("MountainRoadCafeSurfaceAtlas", 4096, False),
                               ("MountainRoadCafeSurfaceResponse", 2048, True)):
        source = ROOT / ("ArtSource/MountainRoad/Surfaces/Atlases/" + name + ".source.png")
        with Image.open(source) as image:
            if image.size != (size, size):
                raise ValueError("Wrong cafe surface map dimensions: " + str(source))
        resource = "MountainRoad/Cafe/Textures/" + name
        checksum = publish(ROOT / ("Assets/Resources/" + resource + ".png"), source.read_bytes(),
                           size, linear, check, clamp=True)
        estimated += bc7_bytes(size)
        extra.append({"resource": resource, "sha256": checksum, "size": size, "mipmaps": True, "sRGB": not linear})
    # These specialised UV sheets stay byte-for-byte authored; budget their
    # existing PC import rather than folding glass/emission/menu content into pigments.
    for resource in ("MountainRoad/Cafe/Textures/MountainRoadCafeGlassDetail",
                     "MountainRoad/Cafe/Textures/MountainRoadCafeCounterDetail",
                     "Textures/CityParkPaintedMetalAlbedo"):
        path = ROOT / ("Assets/Resources/" + resource + ".png")
        with Image.open(path) as image:
            width, height = image.size
        import re
        metadata = Path(str(path) + ".meta").read_text(encoding="utf8")
        size = int(re.search(r"(?m)^  maxTextureSize: (\d+)$", metadata)[1])
        mipmaps = re.search(r"(?m)^    enableMipMap: (\d+)$", metadata)[1] == "1"
        scale = min(1, size / max(width, height))
        imported_width, imported_height = round(width * scale), round(height * scale)
        # Retained shared donors are not republished. A conservative RGBA32
        # bound covers their existing uncompressed/automatic import profile.
        estimated += bc7_bytes(imported_width, imported_height, mipmaps) * 4
        extra.append({"resource": resource, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                      "size": max(imported_width, imported_height), "mipmaps": mipmaps,
                      "sRGB": True, "budget_format": "RGBA32 upper bound"})
    if estimated > art.BUDGET:
        raise ValueError(f"Road texture set exceeds 80 MiB: {estimated}")
    payload = {"design_id": art.DESIGN, "generator_version": art.VERSION, "gpu_budget_bytes": art.BUDGET,
               "estimated_gpu_bytes": estimated,
               "channels": ["signed_physical_slope_u", "signed_physical_slope_v", "roughness", "cavity"],
               "ground_channels": ["smoothness_multiplier", "centred_height", "cavity_tendency", "opaque"],
               "source_provenance": art.provenance(), "sheets": rows, "additional_maps": extra}
    _publisher.write_or_check(MANIFEST, (json.dumps(payload, indent=2) + "\n").encode(), check)
    meta = Path(str(MANIFEST) + ".meta")
    if not meta.exists() and not check:
        meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid5(uuid.NAMESPACE_URL,
            MANIFEST.relative_to(ROOT).as_posix()).hex + "\n", encoding="utf8")
    print(f"ROAD SURFACE TEXTURES OK: BC7+mips {estimated} <= {art.BUDGET}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", "--check", dest="check", action="store_true")
    args = parser.parse_args()
    build(args.check)
