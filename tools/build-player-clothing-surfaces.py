#!/usr/bin/env python3
"""Bake restrained Hero V2 response/normal data over the existing clothing UV0.

The full-colour atlas, FBX and rig are inputs only. Normal RGB is tangent-space
positive Y; response RGBA is metallic (zero), fabric mask, wear, smoothness.
Run with --check to compare every output byte without writing. Plain Python;
the shared atlas kit supplies the same deterministic PNG codec as the hero.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path

from atlas_kit import PixelCanvas, read_generated_png


ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json"
TEXTURES = ROOT / "Assets/Player3D/V2/Textures"
REPORT = ROOT / "ArtSource/Player/player-clothing-surfaces.json"
VERSION = "1.0.0"
REGIONS = {
    "JacketBody", "JacketSleeveLeft", "JacketSleeveRight",
    "JacketForearmLeft", "JacketForearmRight", "JeansPelvis",
    "JeansThighLeft", "JeansThighRight", "JeansShinLeft", "JeansShinRight",
    "BootLeft", "BootRight",
}


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def canonical(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def byte(value: float) -> int:
    return max(0, min(255, round(value * 255)))


def kind_at(name: str, y: int, height: int) -> str:
    # The top 13 pixels of a shin cell paint the existing leather boot shaft.
    if name.startswith("Boot"):
        return "sole" if y < 7 else "leather"
    if name.startswith("JeansShin") and y >= height - 13:
        return "leather"
    return "fabric"


def cell_pixels(pixels: bytes, size: int, region: dict) -> list[list[tuple]]:
    x0, y0 = region["x_px"], region["y_px"]
    width, height = region["width_px"], region["height_px"]
    rows = []
    for y in range(height):
        row = []
        for x in range(width):
            offset = ((size - 1 - y0 - y) * size + x0 + x) * 4
            row.append(tuple(pixels[offset:offset + 3]))
        rows.append(row)
    return rows


def make_cell(region: dict, colors: list[list[tuple]]) -> tuple[list, list]:
    name = region["name"]
    width, height = region["width_px"], region["height_px"]
    # Separate cloth/leather/sole reference colours: a painted boot shaft is
    # leather even though it occupies a trousers cell. No palette is changed.
    dominant = {}
    for kind in ("fabric", "leather", "sole"):
        samples = [colors[y][x] for y in range(height) for x in range(width)
                   if kind_at(name, y, height) == kind]
        if samples:
            dominant[kind] = Counter(samples).most_common(1)[0][0]
    heights, responses = [], []
    for y in range(height):
        row, response = [], []
        kind = kind_at(name, y, height)
        for x in range(width):
            reference = dominant[kind]
            colour_delta = sum(colors[y][x][i] - reference[i] for i in range(3)) / 3
            # Only a bounded local seam signal, never full albedo displacement.
            seam = max(-1.0, min(1.0, colour_delta / 36.0))
            worn = min(1.0, abs(colour_delta) / 42.0)
            if kind == "fabric":
                weave = math.sin(x * math.tau / 4) * math.sin(y * math.tau / 4)
                diagonal = math.sin((x + y) * math.tau / 9)
                value = .050 * weave + .020 * diagonal + .022 * seam
                smoothness = .075 + .014 * diagonal - .011 * worn
            elif kind == "leather":
                grain = (math.sin(x * 1.73 + math.sin(y * .71)) +
                         math.sin(y * 1.29 + math.sin(x * .59))) * .5
                value = .041 * grain + .028 * seam
                smoothness = max(.16, min(.25, .235 - .065 * worn + .012 * grain))
            else:
                value, smoothness, worn = 0.0, .03, 0.0
            row.append(value)
            response.append((0, 255 if kind == "fabric" else 0,
                             byte(worn), byte(smoothness)))
        heights.append(row)
        responses.append(response)

    normals = []
    for y in range(height):
        row = []
        for x in range(width):
            # Two neutral outer texels and a three-texel fade isolate cells;
            # derivatives clamp within the cell, never sample its neighbour.
            edge = min(x, y, width - 1 - x, height - 1 - y)
            fade = max(0.0, min(1.0, (edge - 1) / 3))
            fade = fade * fade * (3 - 2 * fade)
            dx = (heights[y][min(width - 1, x + 1)] -
                  heights[y][max(0, x - 1)]) * .5
            dy = (heights[min(height - 1, y + 1)][x] -
                  heights[max(0, y - 1)][x]) * .5
            # y increases with authored V (bottom-left), so -dy encodes +Y
            # tangent-space relief with no image-row flip in the normal itself.
            nx, ny = -dx * 3.5 * fade, -dy * 3.5 * fade
            length = math.sqrt(nx * nx + ny * ny + 1)
            row.append((byte(nx / length * .5 + .5),
                        byte(ny / length * .5 + .5), byte(.5 / length + .5), 255))
        normals.append(row)
    return normals, responses


def build() -> dict[Path, bytes]:
    model = json.loads(MODEL.read_text(encoding="utf-8"))
    bindings = model["texture_bindings"]
    if len(bindings) != 1:
        raise ValueError("Expected the single production clothing atlas binding")
    binding = bindings[0]
    if (binding["texture_asset"] != "Assets/Player3D/V2/Textures/PlayerClothingAtlas.png" or
            binding["uv_channel"] != 0 or binding["uv_origin"] != "bottom_left" or
            binding["width_px"] != 256 or binding["height_px"] != 256):
        raise ValueError("Hero clothing UV0/atlas contract changed")
    source = ROOT / binding["texture_asset"]
    source_hash = sha(source.read_bytes())
    if source_hash != binding["sha256"]:
        raise ValueError("Base clothing atlas differs from its measured model manifest")
    size, image_height, pixels = read_generated_png(source)
    if (size, image_height) != (256, 256):
        raise ValueError("Expected the original 256x256 clothing atlas")
    regions = binding["regions"]
    if len(regions) != len(REGIONS) or {r["name"] for r in regions} != REGIONS:
        raise ValueError("Hero clothing atlas region ownership changed")
    normal, response = PixelCanvas(size, size), PixelCanvas(size, size)
    normal.rect(0, 0, size, size, (128, 128, 255, 255))
    response.rect(0, 0, size, size, (0, 0, 0, 0))
    occupied, measured, cells = set(), [], {}
    for region in regions:
        x0, y0, width, height = (region[k] for k in
                                ("x_px", "y_px", "width_px", "height_px"))
        if min(x0, y0) < 0 or min(width, height) < 8 or max(x0 + width, y0 + height) > size:
            raise ValueError("Out-of-bounds clothing atlas cell: " + region["name"])
        cell_normal, cell_response = make_cell(region, cell_pixels(pixels, size, region))
        for y in range(height):
            for x in range(width):
                if (x0 + x, y0 + y) in occupied:
                    raise ValueError("Clothing atlas cells overlap")
                occupied.add((x0 + x, y0 + y))
                normal.put(x0 + x, size - 1 - y0 - y, cell_normal[y][x])
                response.put(x0 + x, size - 1 - y0 - y, cell_response[y][x])
                surface = kind_at(region["name"], y, height)
                low, high = {"fabric": (.05, .10), "leather": (.16, .25), "sole": (.03, .03)}[surface]
                if cell_response[y][x][0] != 0 or not byte(low) <= cell_response[y][x][3] <= byte(high):
                    raise ValueError("Metallic/smoothness response exceeds its material range")
                if min(x, y, width - 1 - x, height - 1 - y) < 2 and cell_normal[y][x] != (128, 128, 255, 255):
                    raise ValueError("Normal gutter is not neutral")
        cells[region["name"]] = (cell_normal, cell_response)
        alpha = [p[3] for row in cell_response for p in row]
        measured.append({**region, "smoothness_u8_min": min(alpha),
                         "smoothness_u8_max": max(alpha),
                         "normal_xy_u8_max_deviation": max(abs(p[c] - 128)
                             for row in cell_normal for p in row for c in (0, 1))})
    if cells["JacketForearmLeft"] != cells["JacketForearmRight"]:
        raise ValueError("The two lower sleeves must retain identical surface detail")

    normal_bytes, response_bytes = normal.png_bytes(), response.png_bytes()
    report = {
        "generator": "tools/build-player-clothing-surfaces.py", "version": VERSION,
        "source_model": MODEL.relative_to(ROOT).as_posix(),
        "source_clothing_binding_sha256": sha(canonical(binding)),
        "source_albedo": source.relative_to(ROOT).as_posix(),
        "source_albedo_sha256": source_hash, "size_px": [size, size],
        "uv_channel": 0, "uv_origin": "bottom_left", "normal_neutral_gutter_px": 2,
        "normal_fade_px": 3, "recommended_bump_scale": .65,
        "normal_encoding": "RGB tangent-space positive Y; A opaque",
        "response_encoding": "linear RGBA: R=metallic zero, G=fabric, B=wear, A=absolute smoothness",
        "smoothness_limits": {"fabric": [.05, .10], "leather": [.16, .25], "sole": [.03, .03]},
        "forearm_maps_identical": True, "regions": measured,
        "outputs": {
            "Assets/Player3D/V2/Textures/PlayerClothingNormal.png": sha(normal_bytes),
            "Assets/Player3D/V2/Textures/PlayerClothingResponse.png": sha(response_bytes),
        },
    }
    # The source is never a target and its ownership/hash is proved before any write.
    return {TEXTURES / "PlayerClothingNormal.png": normal_bytes,
            TEXTURES / "PlayerClothingResponse.png": response_bytes,
            REPORT: canonical(report)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Compare canonical outputs without writing")
    args = parser.parse_args()
    outputs = build()
    for path, expected in outputs.items():
        if args.check:
            if not path.is_file() or path.read_bytes() != expected:
                raise SystemExit("STALE: " + path.relative_to(ROOT).as_posix())
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(expected)
    print("HERO CLOTHING SURFACES OK: " + ("deterministic data and source contract verified" if args.check else
          "normal, response and measured manifest generated; base atlas unchanged"))


if __name__ == "__main__":
    main()
