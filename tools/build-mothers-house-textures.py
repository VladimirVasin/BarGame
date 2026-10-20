#!/usr/bin/env python3
"""Publish the registered unique house maps and unchanged-layout auxiliary atlas."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import uuid

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/MothersHouse/Textures/Atlases"
OUTPUT = ROOT / "Assets/Resources/MothersHouse/Textures"
MANIFEST = ROOT / "Assets/MothersHouse/Models/MothersHouseInterior3D.json"
MAPS = (("MothersHouseSurfaceAtlas", 4096, False, "albedo_sha256"),
        ("MothersHouseMaterialResponse", 2048, True, "response_sha256"),
        ("MothersHousePositiveAtlas", 1024, False, "auxiliary_sha256"))
BUDGET = 30*1024*1024


def gpu_bytes(size):
    total = 0
    while True:
        total += max(1, (size+3)//4)**2*16
        if size == 1: return total
        size //= 2


def metadata(path, size, linear, filter_mode):
    sidecar = Path(str(path)+".meta")
    template = ROOT / "Assets/Resources/CombatWarehouse/Textures/WarehouseMasterAtlas.png.meta"
    text = (sidecar if sidecar.exists() else template).read_text(encoding="utf8")
    guid = re.search(r"(?m)^guid: (\w+)$", text)[1] if sidecar.exists() else \
        uuid.uuid5(uuid.NAMESPACE_URL, "bar-promenade/"+path.relative_to(ROOT).as_posix()).hex
    text = re.sub(r"(?m)^guid: \w+$", "guid: "+guid, text)
    for key, value in (("maxTextureSize", size), ("enableMipMap", 1), ("sRGBTexture", int(not linear)),
                       ("linearTexture", int(linear)), ("isReadable", 0), ("filterMode", filter_mode),
                       ("wrapU", 1), ("wrapV", 1), ("wrapW", 1), ("alphaIsTransparency", 0),
                       ("textureCompression", 2), ("alphaUsage", 1 if linear else 0), ("aniso", 1),
                       ("crunchedCompression", 0), ("ignoreMipmapLimit", 1)):
        text = re.sub(r"(?m)^(\s*"+key+r":) \d+$", lambda m: m[1]+" "+str(value), text)
    # Only the PC target is overridden; preserve all other importer metadata/GUIDs.
    blocks = text.split("  - serializedVersion:")
    for index in range(1, len(blocks)):
        if "buildTarget: Standalone" not in blocks[index]: continue
        blocks[index] = re.sub(r"(?m)^(\s*textureFormat:) -?\d+$", r"\g<1> 25", blocks[index])
        blocks[index] = re.sub(r"(?m)^(\s*overridden:) \d+$", r"\g<1> 1", blocks[index])
    return "\n".join(line.rstrip() for line in "  - serializedVersion:".join(blocks).splitlines())+"\n"


def build(validate_only):
    payload = json.loads(MANIFEST.read_text(encoding="utf8"))
    contract = payload["surface_contract"]
    if payload["generator_version"] != "1.12.0" or contract["design_id"] != "mothers_house_unique_surface_v1":
        raise RuntimeError("Rebuild the house's unique UV/physical maps before publishing textures")
    rows = []
    for name, size, linear, key in MAPS:
        source = SOURCE/(name+".source.png")
        raw = source.read_bytes(); checksum = hashlib.sha256(raw).hexdigest()
        if checksum != contract[key]: raise RuntimeError("Unregistered house texture source: "+name)
        with Image.open(source) as image:
            if image.size != (size, size): raise RuntimeError("Wrong house map size: "+name)
        destination = OUTPUT/(name+".png")
        meta = metadata(destination, size, linear, 1 if size == 1024 else 2)
        if validate_only:
            # Unity writes a trailing blank after empty YAML values. Compare
            # the same canonical whitespace while retaining every setting.
            actual_meta = "\n".join(line.rstrip() for line in
                Path(str(destination)+".meta").read_text(encoding="utf8").splitlines())+"\n"
            if destination.read_bytes() != raw or actual_meta != meta:
                raise RuntimeError("Stale house map/import profile: "+name)
        else:
            destination.write_bytes(raw)
            Path(str(destination)+".meta").write_text(meta, encoding="utf8", newline="\n")
        rows.append({"name": name, "source": source.relative_to(ROOT).as_posix(), "sha256": checksum,
                     "size": size, "sRGB": not linear, "wrap": "Clamp", "filter": "Bilinear" if size == 1024 else "Trilinear",
                     "channels": contract["channels"] if linear else ["albedo_r", "albedo_g", "albedo_b"]})
    estimated = sum(gpu_bytes(row["size"]) for row in rows)
    if estimated > BUDGET: raise RuntimeError("House BC7/mip resident texture set exceeds 30 MiB")
    result = {"design_id": contract["design_id"], "mipmaps": True, "windows_compression": "BC7",
              "estimated_gpu_bytes": estimated, "gpu_budget_bytes": BUDGET, "sheets": rows}
    text = json.dumps(result, indent=2)+"\n"
    path = OUTPUT/"MothersHouseTextures.json"
    if validate_only:
        if path.read_text(encoding="utf8") != text: raise RuntimeError("House texture contract is stale")
    else:
        path.write_text(text, encoding="utf8", newline="\n")
        sidecar = Path(str(path)+".meta")
        if not sidecar.exists():
            sidecar.write_text("fileFormatVersion: 2\nguid: "+uuid.uuid5(uuid.NAMESPACE_URL,
                "bar-promenade/"+path.relative_to(ROOT).as_posix()).hex+"\n", encoding="utf8")
    print(f"MOTHER HOUSE SURFACE TEXTURES OK: BC7+mips {estimated} <= {BUDGET}; all 3 registered maps")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    build(parser.parse_args().validate_only)
